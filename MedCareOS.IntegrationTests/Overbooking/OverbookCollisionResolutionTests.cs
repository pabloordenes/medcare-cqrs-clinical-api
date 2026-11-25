using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Services;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Overbooking;

public sealed class OverbookCollisionApiFactory : CustomApiFactory
{
    protected override void ConfigureTestMassTransit(IBusRegistrationConfigurator configurator)
    {
        configurator.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(30), testInactivityTimeout: TimeSpan.FromSeconds(10));
        configurator.AddConsumer<LateConfirmationReceivedConsumer, LateConfirmationReceivedConsumerDefinition>();
    }
}

public class OverbookCollisionResolutionTests : IAsyncDisposable
{
    private readonly OverbookCollisionApiFactory _factory = new();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task LateConfirmationReceived_WhenOverbookIsFree_ShouldDeactivateOverbookBlock()
    {
        var setup = await SeedOriginalWithOverbookBlockAsync(withOverbookAppointment: false);

        using (var scope = _factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var lateNow = DateTimeOffset.UtcNow;
            var res = await sender.Send(new ConfirmAppointmentCommand(
                AppointmentId: setup.OriginalAppointmentId,
                ExpectedPatientId: setup.OriginalPatientId,
                Source: "public_token",
                OccurredAtUtc: lateNow));
            res.ConfirmationState.Should().Be(ConfirmationState.FinalConfirmed);
        }

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        (await harness.Consumed.Any<LateConfirmationReceived>(c => c.Context.Message.AppointmentId == setup.OriginalAppointmentId))
            .Should().BeTrue();

        // Esperar persistencia de desactivación del bloque
        var deadline = DateTime.UtcNow.AddSeconds(5);
        ScheduleBlock? savedOverbook = null;
        while (DateTime.UtcNow < deadline)
        {
            using var checkScope = _factory.Services.CreateScope();
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            savedOverbook = await db.ScheduleBlocks.AsNoTracking().FirstAsync(b => b.Id == setup.OverbookBlockId);
            if (savedOverbook.OverbookStatus == OverbookBlockStatus.Deactivated)
                break;
            await Task.Delay(25);
        }

        savedOverbook!.OverbookStatus.Should().Be(OverbookBlockStatus.Deactivated);
        savedOverbook.DeactivationReason.Should().Be("LateConfirmationUndo");
        savedOverbook.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LateConfirmationReceived_WhenOverbookAlreadyOccupied_ShouldDisplaceOriginalKeepOverbookCreateTaskAndRescheduleHelp()
    {
        var setup = await SeedOriginalWithOverbookBlockAsync(withOverbookAppointment: true);

        using (var scope = _factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ConfirmAppointmentCommand(
                AppointmentId: setup.OriginalAppointmentId,
                ExpectedPatientId: setup.OriginalPatientId,
                Source: "whatsapp_inbound",
                OccurredAtUtc: DateTimeOffset.UtcNow));
        }

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        (await harness.Consumed.Any<LateConfirmationReceived>(c => c.Context.Message.AppointmentId == setup.OriginalAppointmentId))
            .Should().BeTrue();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        Appointment? original = null;
        while (DateTime.UtcNow < deadline)
        {
            using var checkScope = _factory.Services.CreateScope();
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            original = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == setup.OriginalAppointmentId);
            if (original.IsDisplacedByOverbook)
                break;
            await Task.Delay(25);
        }

        original!.IsDisplacedByOverbook.Should().BeTrue();
        original.WasEverDisplacedByOverbook.Should().BeTrue();
        original.Status.Should().Be(AppointmentStatus.Confirmed, "la cita original NUNCA se cancela automáticamente");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var overbook = await verifyDb.Appointments.AsNoTracking().FirstAsync(a => a.Id == setup.OverbookAppointmentId!.Value);
        overbook.IsDisplacedByOverbook.Should().BeFalse();
        overbook.Status.Should().Be(AppointmentStatus.Confirmed);

        var tasks = await verifyDb.ReceptionTasks.AsNoTracking()
            .Where(t => t.AppointmentId == setup.OriginalAppointmentId && t.TaskType == ReceptionTaskType.OverbookConflict)
            .ToListAsync();
        tasks.Should().ContainSingle();
        tasks[0].Priority.Should().Be(ReceptionTaskPriority.High);

        var rescheduleNotif = await verifyDb.Notifications.AsNoTracking()
            .Where(n => n.AppointmentId == setup.OriginalAppointmentId && n.Type == NotificationType.RescheduleHelp)
            .ToListAsync();
        rescheduleNotif.Should().ContainSingle();
    }

    [Fact]
    public async Task DoubleCheckIn_WhenOriginalNeverConfirmedAndArrivesFirst_OverbookWinsAndManualOverrideWorksWithReason()
    {
        var setup = await SeedOriginalWithOverbookBlockAsync(withOverbookAppointment: true);

        using var receptionClient = _factory.CreateClient();
        receptionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        receptionClient.DefaultRequestHeaders.Add("X-Test-Role", "Recepcionista");
        receptionClient.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());

        // Original (sin confirmación) llega primero
        var checkInOrig = await receptionClient.PostAsync($"/api/appointments/{setup.OriginalAppointmentId}/check-in", null);
        checkInOrig.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Sobrecupo (con OfferAcceptedAt previo) llega después
        var checkInOver = await receptionClient.PostAsync($"/api/appointments/{setup.OverbookAppointmentId}/check-in", null);
        checkInOver.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var orig = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == setup.OriginalAppointmentId);
            var over = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == setup.OverbookAppointmentId!.Value);

            orig.IsDisplacedByOverbook.Should().BeTrue();
            orig.CheckedInAt.Should().NotBeNull();
            over.IsDisplacedByOverbook.Should().BeFalse();
            over.CheckedInAt.Should().NotBeNull();

            var task = await db.ReceptionTasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.AppointmentId == setup.OriginalAppointmentId && t.TaskType == ReceptionTaskType.OverbookConflict);
            task.Should().NotBeNull();
            task!.Priority.Should().Be(ReceptionTaskPriority.Urgent);
        }

        // Override sin motivo válido -> 400 BadRequest
        var badOverride = await receptionClient.PostAsJsonAsync(
            $"/api/appointments/{setup.OriginalAppointmentId}/overbook-collision/override",
            new { winnerAppointmentId = setup.OriginalAppointmentId, reason = "corto" });
        badOverride.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Override por Paciente -> 403 Forbidden
        using var patientClient = _factory.CreateClient();
        patientClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        patientClient.DefaultRequestHeaders.Add("X-Test-Role", "Paciente");
        var forbiddenOverride = await patientClient.PostAsJsonAsync(
            $"/api/appointments/{setup.OriginalAppointmentId}/overbook-collision/override",
            new { winnerAppointmentId = setup.OriginalAppointmentId, reason = "Motivo suficientemente largo pero sin permisos" });
        forbiddenOverride.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Override válido por Recepcionista -> 200 OK y auditado
        var okOverride = await receptionClient.PostAsJsonAsync(
            $"/api/appointments/{setup.OriginalAppointmentId}/overbook-collision/override",
            new
            {
                winnerAppointmentId = setup.OriginalAppointmentId,
                reason = "Paciente adulto mayor con urgencia clínica verificada en recepción."
            });
        okOverride.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var orig = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == setup.OriginalAppointmentId);
            var over = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == setup.OverbookAppointmentId!.Value);

            orig.IsDisplacedByOverbook.Should().BeFalse();
            over.IsDisplacedByOverbook.Should().BeTrue();

            var overrideEvents = await db.OverbookCollisionEvents.AsNoTracking()
                .Where(e => e.OriginalAppointmentId == setup.OriginalAppointmentId && e.IsManualOverride)
                .ToListAsync();
            overrideEvents.Should().ContainSingle();
            overrideEvents[0].Winner.Should().Be(OverbookWinner.Original);
            overrideEvents[0].WinnerAppointmentId.Should().Be(setup.OriginalAppointmentId);
            overrideEvents[0].DisplacedAppointmentId.Should().Be(setup.OverbookAppointmentId);
            overrideEvents[0].OverrideReason.Should().Contain("urgencia clínica");
        }
    }

    private async Task<(Guid OriginalAppointmentId, Guid OriginalPatientId, Guid OverbookBlockId, Guid? OverbookAppointmentId)>
        SeedOriginalWithOverbookBlockAsync(bool withOverbookAppointment)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var pat1User = User.Create(Guid.NewGuid(), $"p1_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dra", "Molina", $"{Random.Shared.Next(10000000, 99999999)}-1", "Cardiologia", "Medico", "+56911111111");
        var pat1 = Patient.Create(pat1User.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Carlos", "Pinto", new DateOnly(1988, 3, 20), "M", "+56922222222", email: $"carlos_{Guid.NewGuid():N}@test.cl");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "2");

        var start = DateTime.UtcNow.AddHours(6);
        var originalBlock = ScheduleBlock.Create(
            staff.Id, box.Id, start, start.AddHours(1),
            Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
            new TimeOnly(14, 0), new TimeOnly(15, 0), [start.DayOfWeek]);

        var originalAppointment = Appointment.Create(originalBlock.Id, pat1.Id, "Dolor torácico leve", "paciente", tenantId);
        originalAppointment.MarkFinalConfirmationRequested(now.AddHours(-10));
        originalAppointment.MarkUnresponsive(now.AddHours(-2));

        var overbookBlock = ScheduleBlock.CreateOverbook(originalBlock, originalAppointment.Id);

        db.Users.AddRange(staffUser, pat1User);
        db.Staffs.Add(staff);
        db.Patients.Add(pat1);
        db.Boxes.Add(box);
        db.ScheduleBlocks.AddRange(originalBlock, overbookBlock);
        db.Appointments.Add(originalAppointment);

        Guid? overbookAppointmentId = null;
        if (withOverbookAppointment)
        {
            var pat2User = User.Create(Guid.NewGuid(), $"p2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var pat2 = Patient.Create(pat2User.Id, $"{Random.Shared.Next(10000000, 99999999)}-3", "Laura", "Vidal", new DateOnly(1993, 9, 10), "F", "+56933333333", email: $"laura_{Guid.NewGuid():N}@test.cl");
            var overbookAppointment = Appointment.CreateFromWaitingListOffer(
                overbookBlock.Id,
                pat2.Id,
                Guid.NewGuid(),
                now.AddHours(-1),
                "Control cardiología",
                "lista_espera",
                tenantId);
            overbookBlock.MarkOverbookOccupied(overbookAppointment.Id, now.AddHours(-1));

            db.Users.Add(pat2User);
            db.Patients.Add(pat2);
            db.Appointments.Add(overbookAppointment);
            overbookAppointmentId = overbookAppointment.Id;
        }

        await db.SaveChangesAsync();
        return (originalAppointment.Id, pat1.Id, overbookBlock.Id, overbookAppointmentId);
    }
}
