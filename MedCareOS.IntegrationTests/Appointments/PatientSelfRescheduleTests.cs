using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Commands.RescheduleAppointment;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Queries.GetMyAppointments;
using MedCareOS.Application.Appointments.Queries.GetRescheduleOptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Notifications.Consumers;
using MedCareOS.Application.WaitingLists.Consumers;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Appointments;

public sealed class SelfRescheduleApiFactory : CustomApiFactory
{
    protected override void ConfigureTestMassTransit(IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<SlotReleasedConsumer, SlotReleasedConsumerDefinition>();
        configurator.AddConsumer<OverbookOpenedConsumer, OverbookOpenedConsumerDefinition>();
        configurator.AddConsumer<LateConfirmationReceivedConsumer, LateConfirmationReceivedConsumerDefinition>();
        configurator.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();
    }
}

public class PatientSelfRescheduleTests : IClassFixture<SelfRescheduleApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SelfRescheduleApiFactory _factory;

    public PatientSelfRescheduleTests(SelfRescheduleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SelfServiceReschedule_ShouldEnforceIdor_PersistHistory_ResetConfirmation_AndBackfillVacatedSlot()
    {
        var specialty = $"Kine_{Guid.NewGuid():N}"[..20];
        var patientAUserId = Guid.NewGuid();
        var patientBUserId = Guid.NewGuid();
        Guid appAId, oldBlockId, freeTargetBlockId, waitCandId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uA = User.Create(patientAUserId, $"rsa_{patientAUserId:N}@test.cl", UserRole.Paciente);
            var uB = User.Create(patientBUserId, $"rsb_{patientBUserId:N}@test.cl", UserRole.Paciente);
            var uWait = User.Create(Guid.NewGuid(), $"rsw_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"rsd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var pA = Patient.Create(patientAUserId, $"{Random.Shared.Next(10000000, 99999999)}-1", "Andrea", "Self", new DateOnly(1990, 1, 1), "F", "+56921000001", $"rsa_{Guid.NewGuid():N}@test.cl");
            var pB = Patient.Create(patientBUserId, $"{Random.Shared.Next(10000000, 99999999)}-2", "Bruno", "Idor", new DateOnly(1991, 2, 2), "M", "+56921000002", $"rsb_{Guid.NewGuid():N}@test.cl");
            var pWait = Patient.Create(uWait.Id, $"{Random.Shared.Next(10000000, 99999999)}-3", "Carla", "Wait", new DateOnly(1992, 3, 3), "F", "+56921000003", $"rsw_{Guid.NewGuid():N}@test.cl");

            var staff = Staff.Create(uDoc.Id, "Klgo", "Lopez", $"{Random.Shared.Next(10000000, 99999999)}-4", specialty, "Medico", "+56921000004");
            var box = Box.Create("Box Self", "Consulta", 1, "1");
            var start1 = DateTime.UtcNow.AddDays(3);
            var start2 = DateTime.UtcNow.AddDays(4);

            var block1 = ScheduleBlock.Create(staff.Id, box.Id, start1, start1.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(start1), DateOnly.FromDateTime(start1), new TimeOnly(9, 0), new TimeOnly(10, 0), [start1.DayOfWeek]);
            var block2 = ScheduleBlock.Create(staff.Id, box.Id, start2, start2.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(start2), DateOnly.FromDateTime(start2), new TimeOnly(11, 0), new TimeOnly(12, 0), [start2.DayOfWeek]);

            var appA = Appointment.Create(block1.Id, pA.Id, "Control");
            appA.RegisterPatientConfirmation(DateTimeOffset.UtcNow);
            var wl = WaitingList.Create(pWait.Id, specialty, priority: 1);

            db.Users.AddRange(uA, uB, uWait, uDoc);
            db.Patients.AddRange(pA, pB, pWait);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(block1, block2);
            db.Appointments.Add(appA);
            db.WaitingLists.Add(wl);
            await db.SaveChangesAsync();

            appAId = appA.Id;
            oldBlockId = block1.Id;
            freeTargetBlockId = block2.Id;
            waitCandId = pWait.Id;
        }

        using var clientA = CreateClient(patientAUserId, "Paciente");
        using var clientB = CreateClient(patientBUserId, "Paciente");

        // IDOR checks con paciente B -> 403 Forbidden
        (await clientB.GetAsync($"/api/patients/me/appointments/{appAId}/reschedule-options")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clientB.PostAsJsonAsync($"/api/patients/me/appointments/{appAId}/reschedule", new { NewScheduleBlockId = freeTargetBlockId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clientB.PostAsJsonAsync($"/api/patients/me/appointments/{appAId}/cancel", new { Reason = "Hack" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // GET /api/patients/me/appointments
        var myRes = await clientA.GetAsync("/api/patients/me/appointments");
        myRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var myList = await myRes.Content.ReadFromJsonAsync<List<PatientAppointmentSummaryDto>>(JsonOptions);
        myList.Should().ContainSingle(a => a.AppointmentId == appAId && a.CanCancel && a.CanReschedule);

        // GET /reschedule-options
        var optRes = await clientA.GetAsync($"/api/patients/me/appointments/{appAId}/reschedule-options");
        optRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = await optRes.Content.ReadFromJsonAsync<RescheduleOptionsResponseDto>(JsonOptions);
        options!.AvailableOptions.Should().Contain(o => o.ScheduleBlockId == freeTargetBlockId);

        // POST /reschedule
        var reschedRes = await clientA.PostAsJsonAsync($"/api/patients/me/appointments/{appAId}/reschedule", new
        {
            NewScheduleBlockId = freeTargetBlockId,
            Reason = "Cambio de turno laboral"
        });
        reschedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var reschedBody = await reschedRes.Content.ReadFromJsonAsync<RescheduleAppointmentResult>(JsonOptions);
        reschedBody!.NewScheduleBlockId.Should().Be(freeTargetBlockId);
        reschedBody.ReschedulesCount.Should().Be(1);
        reschedBody.ConfirmationState.Should().Be(ConfirmationState.Pending);
        reschedBody.SlotReleasedTriggered.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var history = await db.AppointmentRescheduleHistories.AsNoTracking().SingleAsync(h => h.AppointmentId == appAId);
            history.FromScheduleBlockId.Should().Be(oldBlockId);
            history.ToScheduleBlockId.Should().Be(freeTargetBlockId);
            history.Reason.Should().Be("Cambio de turno laboral");
        }

        // Verificar que SlotReleased disparó oferta hacia pWait sobre oldBlockId
        var offerOnOldBlock = await PollAsync(async () =>
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.WaitingListOffers.AsNoTracking()
                .FirstOrDefaultAsync(o => o.ScheduleBlockId == oldBlockId && o.PatientId == waitCandId);
        });
        offerOnOldBlock.Should().NotBeNull();
        offerOnOldBlock!.SourceType.Should().Be("SlotReleased");
    }

    [Fact]
    public async Task ParallelReschedulesIntoSameBlock_WithBarrier_ShouldAllowOneWinnerAndReturn409ToLoser()
    {
        var specialty = $"Otor_{Guid.NewGuid():N}"[..20];
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        Guid app1Id, app2Id, targetBlockId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var u1 = User.Create(user1Id, $"pr1_{user1Id:N}@test.cl", UserRole.Paciente);
            var u2 = User.Create(user2Id, $"pr2_{user2Id:N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"prd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var p1 = Patient.Create(user1Id, $"{Random.Shared.Next(10000000, 99999999)}-5", "P1", "Race", new DateOnly(1990, 1, 1), "M", "+56922000001");
            var p2 = Patient.Create(user2Id, $"{Random.Shared.Next(10000000, 99999999)}-6", "P2", "Race", new DateOnly(1991, 2, 2), "F", "+56922000002");
            var staff = Staff.Create(uDoc.Id, "Dr", "Ear", $"{Random.Shared.Next(10000000, 99999999)}-7", specialty, "Medico", "+56922000003");
            var box = Box.Create("Box PRace", "Consulta", 1, "1");
            var s1 = DateTime.UtcNow.AddDays(3);
            var s2 = DateTime.UtcNow.AddDays(4);
            var s3 = DateTime.UtcNow.AddDays(5);

            var b1 = ScheduleBlock.Create(staff.Id, box.Id, s1, s1.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s1), DateOnly.FromDateTime(s1), new TimeOnly(9, 0), new TimeOnly(10, 0), [s1.DayOfWeek]);
            var b2 = ScheduleBlock.Create(staff.Id, box.Id, s2, s2.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s2), DateOnly.FromDateTime(s2), new TimeOnly(10, 0), new TimeOnly(11, 0), [s2.DayOfWeek]);
            var bTarget = ScheduleBlock.Create(staff.Id, box.Id, s3, s3.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s3), DateOnly.FromDateTime(s3), new TimeOnly(11, 0), new TimeOnly(12, 0), [s3.DayOfWeek]);

            var app1 = Appointment.Create(b1.Id, p1.Id, "Control");
            var app2 = Appointment.Create(b2.Id, p2.Id, "Control");

            db.Users.AddRange(u1, u2, uDoc);
            db.Patients.AddRange(p1, p2);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(b1, b2, bTarget);
            db.Appointments.AddRange(app1, app2);
            await db.SaveChangesAsync();

            app1Id = app1.Id;
            app2Id = app2.Id;
            targetBlockId = bTarget.Id;
        }

        using var barrier = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            using var c1 = CreateClient(user1Id, "Paciente");
            barrier.SignalAndWait();
            return await c1.PostAsJsonAsync($"/api/patients/me/appointments/{app1Id}/reschedule", new { NewScheduleBlockId = targetBlockId });
        });

        var t2 = Task.Run(async () =>
        {
            using var c2 = CreateClient(user2Id, "Paciente");
            barrier.SignalAndWait();
            return await c2.PostAsJsonAsync($"/api/patients/me/appointments/{app2Id}/reschedule", new { NewScheduleBlockId = targetBlockId });
        });

        var responses = await Task.WhenAll(t1, t2);
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activeOnTarget = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.ScheduleBlockId == targetBlockId && a.Status != AppointmentStatus.Cancelled);
            activeOnTarget.Should().Be(1);
        }
    }

    [Fact]
    public async Task Cancel_WithoutOverbookFiresSlotReleased_WithOccupiedOverbookSuppressesSlotReleased()
    {
        var specialty = $"Urol_{Guid.NewGuid():N}"[..20];
        var uFreeId = Guid.NewGuid();
        var uOccupiedId = Guid.NewGuid();
        Guid appFreeId, blockFreeId, appWithObId, blockWithObId, waitPatientId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uFree = User.Create(uFreeId, $"cf_{uFreeId:N}@test.cl", UserRole.Paciente);
            var uOcc = User.Create(uOccupiedId, $"co_{uOccupiedId:N}@test.cl", UserRole.Paciente);
            var uObHolder = User.Create(Guid.NewGuid(), $"coh_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uWait = User.Create(Guid.NewGuid(), $"cw_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"cwd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var pFree = Patient.Create(uFreeId, $"{Random.Shared.Next(10000000, 99999999)}-8", "Free", "Pac", new DateOnly(1990, 1, 1), "M", "+56923000001");
            var pOcc = Patient.Create(uOccupiedId, $"{Random.Shared.Next(10000000, 99999999)}-9", "Orig", "Ob", new DateOnly(1990, 2, 2), "M", "+56923000002");
            var pHolder = Patient.Create(uObHolder.Id, $"{Random.Shared.Next(10000000, 99999999)}-0", "Holder", "Ob", new DateOnly(1991, 3, 3), "F", "+56923000003");
            var pWait = Patient.Create(uWait.Id, $"{Random.Shared.Next(10000000, 99999999)}-1", "Wait", "Cand", new DateOnly(1992, 4, 4), "F", "+56923000004", $"cw_{Guid.NewGuid():N}@test.cl");
            var staff = Staff.Create(uDoc.Id, "Dr", "Urol", $"{Random.Shared.Next(10000000, 99999999)}-2", specialty, "Medico", "+56923000005");
            var box = Box.Create("Box CancelSR", "Consulta", 2, "1");

            var s1 = DateTime.UtcNow.AddDays(4);
            var s2 = DateTime.UtcNow.AddDays(5);
            var bFree = ScheduleBlock.Create(staff.Id, box.Id, s1, s1.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s1), DateOnly.FromDateTime(s1), new TimeOnly(9, 0), new TimeOnly(10, 0), [s1.DayOfWeek]);
            var bWithOb = ScheduleBlock.Create(staff.Id, box.Id, s2, s2.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s2), DateOnly.FromDateTime(s2), new TimeOnly(11, 0), new TimeOnly(12, 0), [s2.DayOfWeek]);

            var appFree = Appointment.Create(bFree.Id, pFree.Id, "Control");
            var appWithOb = Appointment.Create(bWithOb.Id, pOcc.Id, "Control");
            appWithOb.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow.AddHours(-2));
            appWithOb.MarkUnresponsive(DateTimeOffset.UtcNow.AddMinutes(-15));

            var obBlock = ScheduleBlock.CreateOverbook(bWithOb, appWithOb.Id);
            var obApp = Appointment.Create(obBlock.Id, pHolder.Id, "Sobrecupo");
            obBlock.MarkOverbookOccupied(obApp.Id, DateTimeOffset.UtcNow);

            var wl = WaitingList.Create(pWait.Id, specialty, priority: 1);

            db.Users.AddRange(uFree, uOcc, uObHolder, uWait, uDoc);
            db.Patients.AddRange(pFree, pOcc, pHolder, pWait);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(bFree, bWithOb, obBlock);
            db.Appointments.AddRange(appFree, appWithOb, obApp);
            db.WaitingLists.Add(wl);
            await db.SaveChangesAsync();

            appFreeId = appFree.Id;
            blockFreeId = bFree.Id;
            appWithObId = appWithOb.Id;
            blockWithObId = bWithOb.Id;
            waitPatientId = pWait.Id;
        }

        // 1. Cancelar cita CON sobrecupo ocupado -> NO dispara SlotReleased
        using var clientOcc = CreateClient(uOccupiedId, "Paciente");
        (await clientOcc.PostAsJsonAsync($"/api/patients/me/appointments/{appWithObId}/cancel", new { Reason = "Ya me atendi" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await Task.Delay(300);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.WaitingListOffers.AsNoTracking().AnyAsync(o => o.ScheduleBlockId == blockWithObId))
                .Should().BeFalse();
        }

        // 2. Cancelar cita SIN sobrecupo -> SÍ dispara SlotReleased y crea WaitingListOffer
        using var clientFree = CreateClient(uFreeId, "Paciente");
        (await clientFree.PostAsJsonAsync($"/api/patients/me/appointments/{appFreeId}/cancel", new { Reason = "Viaje imprevisto" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var offerCreated = await PollAsync(async () =>
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.WaitingListOffers.AsNoTracking()
                .FirstOrDefaultAsync(o => o.ScheduleBlockId == blockFreeId && o.PatientId == waitPatientId);
        });
        offerCreated.Should().NotBeNull();
        offerCreated!.SourceType.Should().Be("SlotReleased");
    }

    [Fact]
    public async Task DisplacedPriorityAndPublicRescheduleTokenBridge_ShouldBypassLimitAndEnforceSingleUseToken()
    {
        var specialty = $"Psic_{Guid.NewGuid():N}"[..20];
        var patientUserId = Guid.NewGuid();
        Guid appId, block1Id, block2Id, patientId;
        string rawRescheduleToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uP = User.Create(patientUserId, $"tok_{patientUserId:N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"tokd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var p = Patient.Create(patientUserId, $"{Random.Shared.Next(10000000, 99999999)}-3", "Elena", "Tok", new DateOnly(1993, 5, 5), "F", "+56924000001");
            var staff = Staff.Create(uDoc.Id, "Ps", "Mendez", $"{Random.Shared.Next(10000000, 99999999)}-4", specialty, "Medico", "+56924000002");
            var box = Box.Create("Box TokenR", "Consulta", 1, "1");
            var s1 = DateTime.UtcNow.AddDays(4);
            var s2 = DateTime.UtcNow.AddDays(5);

            var b1 = ScheduleBlock.Create(staff.Id, box.Id, s1, s1.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s1), DateOnly.FromDateTime(s1), new TimeOnly(10, 0), new TimeOnly(11, 0), [s1.DayOfWeek]);
            var b2 = ScheduleBlock.Create(staff.Id, box.Id, s2, s2.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s2), DateOnly.FromDateTime(s2), new TimeOnly(12, 0), new TimeOnly(13, 0), [s2.DayOfWeek]);

            var app = Appointment.Create(b1.Id, p.Id, "Control");
            // Simulamos que ya alcanzó el límite normal (3), pero fue desplazado por sobrecupo
            typeof(Appointment).GetProperty(nameof(Appointment.ReschedulesCount))!.SetValue(app, 3);
            app.MarkDisplacedByOverbook(DateTimeOffset.UtcNow);

            db.Users.AddRange(uP, uDoc);
            db.Patients.Add(p);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(b1, b2);
            db.Appointments.Add(app);
            await db.SaveChangesAsync();

            appId = app.Id;
            block1Id = b1.Id;
            block2Id = b2.Id;
            patientId = p.Id;

            var tokenService = scope.ServiceProvider.GetRequiredService<IPatientActionTokenService>();
            var issued = await tokenService.IssueAsync(app.Id, p.Id, null, PatientActionScope.Reschedule, TimeSpan.FromHours(12));
            rawRescheduleToken = issued.RawToken;
        }

        using var anonClient = _factory.CreateClient();

        // Consultar opciones dos veces NO consume el token y refleja PriorityAccess = true
        var opt1 = await anonClient.GetAsync($"/api/public/appointments/reschedule-options?token={Uri.EscapeDataString(rawRescheduleToken)}");
        opt1.StatusCode.Should().Be(HttpStatusCode.OK);
        var optBody = await opt1.Content.ReadFromJsonAsync<RescheduleOptionsResponseDto>(JsonOptions);
        optBody!.PriorityAccess.Should().BeTrue();
        optBody.ReschedulesRemaining.Should().BeNull();
        optBody.AvailableOptions.Should().Contain(o => o.ScheduleBlockId == block2Id && o.IsPrioritySlot);

        var opt2 = await anonClient.GetAsync($"/api/public/appointments/reschedule-options?token={Uri.EscapeDataString(rawRescheduleToken)}");
        opt2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Ejecutar reagendamiento por token (bypassea ReschedulesCount == 3)
        var execRes = await anonClient.PostAsJsonAsync("/api/public/appointments/reschedule", new
        {
            Token = rawRescheduleToken,
            NewScheduleBlockId = block2Id,
            Reason = "Enlace RescheduleHelp"
        });
        execRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var execBody = await execRes.Content.ReadFromJsonAsync<RescheduleAppointmentResult>(JsonOptions);
        execBody!.NewScheduleBlockId.Should().Be(block2Id);
        execBody.ReschedulesCount.Should().Be(4);

        // Replay del token ya usado -> 410 Gone
        var replayRes = await anonClient.PostAsJsonAsync("/api/public/appointments/reschedule", new
        {
            Token = rawRescheduleToken,
            NewScheduleBlockId = block1Id
        });
        replayRes.StatusCode.Should().Be(HttpStatusCode.Gone);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var appReloaded = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appId);
            appReloaded.ScheduleBlockId.Should().Be(block2Id);
            appReloaded.IsDisplacedByOverbook.Should().BeFalse();
            appReloaded.WasEverDisplacedByOverbook.Should().BeTrue();
        }
    }

    private static async Task<T?> PollAsync<T>(Func<Task<T?>> probe) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var res = await probe();
            if (res is not null)
                return res;
            await Task.Delay(25);
        }
        return null;
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
}
