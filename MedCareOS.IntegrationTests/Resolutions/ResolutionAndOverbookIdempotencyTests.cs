using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MassTransit.Testing;
using MedCareOS.Api.BackgroundServices;
using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MedCareOS.Application.ScheduleBlocks.Queries.GetAllScheduleBlocks;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omro.Contracts.Events;

namespace MedCareOS.IntegrationTests.Resolutions;

public class ResolutionAndOverbookIdempotencyTests : IClassFixture<CustomApiFactory>
{
    private readonly CustomApiFactory _factory;

    public ResolutionAndOverbookIdempotencyTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DoubleApproveResolution_ShouldReturn204Then409_AndPublishSingleEvent()
    {
        var task = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{\"DeferredActions\":[]}");
        var adminId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AiResolutionTasks.Add(task);
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        client.DefaultRequestHeaders.Add("X-Test-UserId", adminId.ToString());

        var firstResponse = await client.PatchAsync($"/api/resolutions/{task.Id}/approve", null);
        var secondResponse = await client.PatchAsync($"/api/resolutions/{task.Id}/approve", null);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.AiResolutionTasks.FirstAsync(t => t.Id == task.Id);
            saved.Status.Should().Be(AiResolutionStatus.Approved);
            saved.ApprovedBy.Should().Be(adminId);
            saved.ApprovedAt.Should().NotBeNull();
        }

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var publishedEvents = await harness.Published.SelectAsync<ResolutionApproved>(
            e => e.Context.Message.ResolutionTaskId == task.Id).ToListAsync();
        publishedEvents.Should().HaveCount(1);
    }

    [Fact]
    public async Task DoubleOverbook_ShouldCreateSingleOverbookBlock_KeepOriginalAppointment_AndExcludeFromQueries()
    {
        Guid appointmentId;
        Guid sourceBlockId;
        var specialty = $"Neurologia_{Guid.NewGuid():N}";
        var targetDate = DateTime.UtcNow.AddDays(5).Date.AddHours(10);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patientUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dra", "Lopez", $"{Random.Shared.Next(10000000, 99999999)}-4", specialty, "Medico", "+56944444444");
            var patient = Patient.Create(patientUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-5", "Maria", "Gomez", new DateOnly(1985, 2, 1), "F", "+56955555555");
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "2");
            var block = ScheduleBlock.Create(
                staff.Id, box.Id,
                targetDate, targetDate.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(targetDate), DateOnly.FromDateTime(targetDate),
                new TimeOnly(10, 0), new TimeOnly(11, 0), [targetDate.DayOfWeek]);
            var appointment = Appointment.Create(block.Id, patient.Id, "Cefalea intensa", "paciente");

            db.Users.AddRange(staffUser, patientUser);
            db.Staffs.Add(staff);
            db.Patients.Add(patient);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();

            appointmentId = appointment.Id;
            sourceBlockId = block.Id;
        }

        using var omroClient = _factory.CreateClient();
        omroClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.GenerateOmroServiceToken());

        var payload = new { ResourceId = appointmentId, TargetRisk = 0.91m, AutoApprove = true };
        var firstRes = await omroClient.PostAsJsonAsync("/api/appointments/overbook", payload);
        var secondRes = await omroClient.PostAsJsonAsync("/api/appointments/overbook", payload);

        firstRes.StatusCode.Should().Be(HttpStatusCode.OK);
        secondRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var overbooks = await db.ScheduleBlocks
                .Where(b => b.SourceAppointmentId == appointmentId)
                .ToListAsync();
            overbooks.Should().HaveCount(1);
            overbooks[0].IsOverbook.Should().BeTrue();
            overbooks[0].RuleGroupId.Should().BeNull();
            overbooks[0].Id.Should().NotBe(sourceBlockId);

            var originalAppointment = await db.Appointments.FirstAsync(a => a.Id == appointmentId);
            originalAppointment.Status.Should().Be(AppointmentStatus.Scheduled);
        }

        // Verificar que GetAllScheduleBlocks y SearchAvailability excluyen el bloque overbook
        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var rules = await adminClient.GetFromJsonAsync<List<ScheduleBlockDto>>("/api/scheduleblocks", jsonOptions);
        rules!.Should().OnlyContain(r => r.RuleGroupId != Guid.Empty);

        var availability = await adminClient.GetFromJsonAsync<List<AgendaBloqueDto>>(
            $"/api/agenda/disponibilidad?especialidad={Uri.EscapeDataString(specialty)}&desde={targetDate:yyyy-MM-dd}&hasta={targetDate:yyyy-MM-dd}");
        availability.Should().BeEmpty();
    }

    [Fact]
    public async Task DoubleBooking_SameScheduleBlock_ShouldReturn409Conflict()
    {
        Guid blockId;
        Guid patient1Id;
        Guid patient2Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var staffUser = User.Create(Guid.NewGuid(), $"doc2_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var p1User = User.Create(Guid.NewGuid(), $"p1_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var p2User = User.Create(Guid.NewGuid(), $"p2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);

            var staff = Staff.Create(staffUser.Id, "Dr", "Soto", $"{Random.Shared.Next(10000000, 99999999)}-6", "Dermatologia", "Medico", "+56966666666");
            var p1 = Patient.Create(p1User.Id, $"{Random.Shared.Next(10000000, 99999999)}-7", "Ana", "Uno", new DateOnly(1990, 1, 1), "F", "+56977777777");
            var p2 = Patient.Create(p2User.Id, $"{Random.Shared.Next(10000000, 99999999)}-8", "Luis", "Dos", new DateOnly(1991, 2, 2), "M", "+56988888888");
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
            var start = DateTime.UtcNow.AddDays(3);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, start, start.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
                new TimeOnly(11, 0), new TimeOnly(12, 0), [start.DayOfWeek]);

            db.Users.AddRange(staffUser, p1User, p2User);
            db.Staffs.Add(staff);
            db.Patients.AddRange(p1, p2);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            await db.SaveChangesAsync();

            blockId = block.Id;
            patient1Id = p1.Id;
            patient2Id = p2.Id;
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Recepcionista");

        var firstBooking = await client.PostAsJsonAsync("/api/appointments", new
        {
            ScheduleBlockId = blockId,
            PatientId = patient1Id,
            SymptomsRaw = "Lunar",
            SchedulingSource = "recepcion"
        });
        var secondBooking = await client.PostAsJsonAsync("/api/appointments", new
        {
            ScheduleBlockId = blockId,
            PatientId = patient2Id,
            SymptomsRaw = "Control",
            SchedulingSource = "recepcion"
        });

        firstBooking.StatusCode.Should().Be(HttpStatusCode.OK);
        secondBooking.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoShowDetectionJob_ShouldMarkExpiredUnattendedAppointmentsAsNoShow()
    {
        Guid expiredAppointmentId;
        Guid futureAppointmentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var staffUser = User.Create(Guid.NewGuid(), $"doc3_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patUser = User.Create(Guid.NewGuid(), $"pat3_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dr", "Vega", $"{Random.Shared.Next(10000000, 99999999)}-9", "Pediatria", "Medico", "+56999999999");
            var patient = Patient.Create(patUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-0", "Sofia", "Ruiz", new DateOnly(2015, 6, 1), "F", "+56910101010");
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "3");

            var pastStart = DateTime.UtcNow.AddHours(-3);
            var expiredBlock = ScheduleBlock.Create(
                staff.Id, box.Id, pastStart, pastStart.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(pastStart), DateOnly.FromDateTime(pastStart),
                new TimeOnly(8, 0), new TimeOnly(9, 0), [pastStart.DayOfWeek]);
            var futureStart = DateTime.UtcNow.AddHours(4);
            var futureBlock = ScheduleBlock.Create(
                staff.Id, box.Id, futureStart, futureStart.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(futureStart), DateOnly.FromDateTime(futureStart),
                new TimeOnly(15, 0), new TimeOnly(16, 0), [futureStart.DayOfWeek]);

            var expiredApp = Appointment.Create(expiredBlock.Id, patient.Id, "Fiebre", "paciente");
            var futureApp = Appointment.Create(futureBlock.Id, patient.Id, "Control", "paciente");

            db.Users.AddRange(staffUser, patUser);
            db.Staffs.Add(staff);
            db.Patients.Add(patient);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(expiredBlock, futureBlock);
            db.Appointments.AddRange(expiredApp, futureApp);
            await db.SaveChangesAsync();

            expiredAppointmentId = expiredApp.Id;
            futureAppointmentId = futureApp.Id;
        }

        var job = new NoShowDetectionBackgroundService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NoShowDetectionBackgroundService>.Instance);

        var markedCount = await job.ProcessExpiredAppointmentsAsync(CancellationToken.None);
        markedCount.Should().BeGreaterThanOrEqualTo(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expired = await db.Appointments.FirstAsync(a => a.Id == expiredAppointmentId);
            var future = await db.Appointments.FirstAsync(a => a.Id == futureAppointmentId);

            expired.Status.Should().Be(AppointmentStatus.NoShow);
            expired.ActualAttendance.Should().BeFalse();

            future.Status.Should().Be(AppointmentStatus.Scheduled);
            future.ActualAttendance.Should().BeNull();
        }
    }
}
