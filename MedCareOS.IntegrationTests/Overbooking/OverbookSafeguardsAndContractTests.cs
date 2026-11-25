using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Overbooking;

public class OverbookSafeguardsAndContractTests : IClassFixture<CustomApiFactory>
{
    private readonly CustomApiFactory _factory;

    public OverbookSafeguardsAndContractTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GenerateOverbook_WhenValidAndSameIdempotencyKeyReplayed_ShouldReturnExactResponseAndPublishSingleOverbookOpened()
    {
        var seeded = await SeedOverbookScenarioAsync(autoOverbookEnabled: true);

        using var client = CreateOmroClient();
        var correlationId = Guid.NewGuid();
        var idemKey = $"overbook:{seeded.AppointmentId}:{Guid.NewGuid():N}";

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/appointments/overbook");
        req1.Headers.Add("Idempotency-Key", idemKey);
        req1.Content = JsonContent.Create(new
        {
            resourceId = seeded.AppointmentId,
            reason = "Deadline missed and score >= 0.85",
            correlationId,
            decisionMode = "Autonomous"
        });

        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var json1 = await res1.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(json1);
        doc1.RootElement.GetProperty("status").GetString().Should().Be("Created");
        var overbookBlockId = doc1.RootElement.GetProperty("overbookBlockId").GetGuid();
        overbookBlockId.Should().NotBeEmpty();

        // Replay con la misma Idempotency-Key devuelve exactamente la misma respuesta cacheada
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/appointments/overbook");
        req2.Headers.Add("Idempotency-Key", idemKey);
        req2.Content = JsonContent.Create(new
        {
            resourceId = seeded.AppointmentId,
            reason = "Retry",
            correlationId,
            decisionMode = "Autonomous"
        });

        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var json2 = await res2.Content.ReadAsStringAsync();
        json2.Should().Be(json1);

        // Llamada con nueva Idempotency-Key retorna AlreadyExists idempotente sin duplicar evento
        using var req3 = new HttpRequestMessage(HttpMethod.Post, "/api/appointments/overbook");
        req3.Headers.Add("Idempotency-Key", $"other:{Guid.NewGuid():N}");
        req3.Content = JsonContent.Create(new
        {
            resourceId = seeded.AppointmentId,
            reason = "Second key",
            correlationId,
            decisionMode = "Autonomous"
        });

        var res3 = await client.SendAsync(req3);
        res3.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc3 = JsonDocument.Parse(await res3.Content.ReadAsStringAsync());
        doc3.RootElement.GetProperty("status").GetString().Should().Be("AlreadyExists");
        doc3.RootElement.GetProperty("overbookBlockId").GetGuid().Should().Be(overbookBlockId);
        doc3.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var openedEvents = await harness.Published
            .SelectAsync<OverbookOpened>(e => e.Context.Message.SourceAppointmentId == seeded.AppointmentId)
            .ToListAsync();
        openedEvents.Should().ContainSingle();
        openedEvents[0].Context.Message.OverbookBlockId.Should().Be(overbookBlockId);
        openedEvents[0].Context.Message.StaffId.Should().Be(seeded.StaffId);
        openedEvents[0].Context.Message.DecisionMode.Should().Be(OverbookDecisionMode.Autonomous);
    }

    [Fact]
    public async Task GenerateOverbook_WhenFinalNotificationNotDelivered_ShouldReturn422AndCreateSingleReceptionTask()
    {
        var seeded = await SeedOverbookScenarioAsync(finalNotificationStatus: NotificationStatus.Sent, autoOverbookEnabled: true);

        using var client = CreateOmroClient();
        var payload = new
        {
            resourceId = seeded.AppointmentId,
            reason = "Final notification only Sent, not Delivered",
            correlationId = Guid.NewGuid(),
            decisionMode = "Autonomous"
        };

        var res1 = await client.PostAsJsonAsync("/api/appointments/overbook", payload);
        var res2 = await client.PostAsJsonAsync("/api/appointments/overbook", payload);

        res1.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        res2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        doc1.RootElement.GetProperty("code").GetString().Should().Be("FINAL_NOTIFICATION_NOT_DELIVERED");
        var taskId = doc1.RootElement.GetProperty("receptionTaskId").GetGuid();
        taskId.Should().NotBeEmpty();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tasks = await db.ReceptionTasks
            .Where(t => t.AppointmentId == seeded.AppointmentId && t.TaskType == ReceptionTaskType.FailedNotificationEscalation)
            .ToListAsync();
        tasks.Should().ContainSingle();
        tasks[0].Id.Should().Be(taskId);
        tasks[0].Priority.Should().Be(ReceptionTaskPriority.High);

        var overbooks = await db.ScheduleBlocks.Where(b => b.SourceAppointmentId == seeded.AppointmentId).ToListAsync();
        overbooks.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateOverbook_WhenClinicalExclusionApplies_ShouldReject422AutonomousAndAllow200Approved()
    {
        var seeded = await SeedOverbookScenarioAsync(
            patientBirthDate: new DateOnly(1948, 4, 1),
            autoOverbookEnabled: true);

        using var client = CreateOmroClient();

        var autonomousRes = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = seeded.AppointmentId,
            reason = "Auto attempt on senior patient",
            correlationId = Guid.NewGuid(),
            decisionMode = "Autonomous"
        });

        autonomousRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var autoDoc = JsonDocument.Parse(await autonomousRes.Content.ReadAsStringAsync());
        autoDoc.RootElement.GetProperty("code").GetString().Should().Be("CLINICAL_EXCLUSION");

        var approvedRes = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = seeded.AppointmentId,
            reason = "Approved via HITL review",
            correlationId = Guid.NewGuid(),
            decisionMode = "Approved"
        });

        approvedRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GenerateOverbook_WhenDeadlineNotPassed_OrAutoModeDisabled_OrBoxInactive_ShouldReturn422()
    {
        var noDeadline = await SeedOverbookScenarioAsync(markDeadlineMissed: false, autoOverbookEnabled: true);
        var shadowOnly = await SeedOverbookScenarioAsync(autoOverbookEnabled: false);
        var inactiveBox = await SeedOverbookScenarioAsync(boxActive: false, autoOverbookEnabled: true);

        using var client = CreateOmroClient();

        var resNoDeadline = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = noDeadline.AppointmentId,
            decisionMode = "Autonomous"
        });
        resNoDeadline.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadCodeAsync(resNoDeadline)).Should().Be("DEADLINE_NOT_PASSED");

        var resShadow = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = shadowOnly.AppointmentId,
            decisionMode = "Autonomous"
        });
        resShadow.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadCodeAsync(resShadow)).Should().Be("AUTONOMOUS_MODE_DISABLED");

        var resInactiveBox = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = inactiveBox.AppointmentId,
            decisionMode = "Autonomous"
        });
        resInactiveBox.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadCodeAsync(resInactiveBox)).Should().Be("BOX_INACTIVE_OR_AT_CAPACITY");
    }

    [Fact]
    public async Task GenerateOverbook_WhenAlreadyConfirmedOrDeactivated_ShouldReturn409Conflict()
    {
        var confirmed = await SeedOverbookScenarioAsync(autoOverbookEnabled: true);
        var deactivated = await SeedOverbookScenarioAsync(autoOverbookEnabled: true);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var confirmedApp = await db.Appointments.FirstAsync(a => a.Id == confirmed.AppointmentId);
            confirmedApp.RegisterPatientConfirmation(DateTimeOffset.UtcNow);

            var deactivatedSourceBlock = await db.ScheduleBlocks.FirstAsync(b => b.Id == deactivated.BlockId);
            var overbookBlock = ScheduleBlock.CreateOverbook(deactivatedSourceBlock, deactivated.AppointmentId);
            overbookBlock.DeactivateOverbook("LateConfirmationUndo", DateTimeOffset.UtcNow);
            db.ScheduleBlocks.Add(overbookBlock);

            await db.SaveChangesAsync();
        }

        using var client = CreateOmroClient();

        var resConfirmed = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = confirmed.AppointmentId,
            decisionMode = "Approved"
        });
        resConfirmed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadCodeAsync(resConfirmed)).Should().Be("APPOINTMENT_ALREADY_CONFIRMED");

        var resDeactivated = await client.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = deactivated.AppointmentId,
            decisionMode = "Approved"
        });
        resDeactivated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadCodeAsync(resDeactivated)).Should().Be("OVERBOOK_BLOCK_DEACTIVATED");
    }

    [Fact]
    public async Task CreateAppointment_DirectBookingOnOverbookBlock_ShouldReturn409Conflict()
    {
        var seeded = await SeedOverbookScenarioAsync(autoOverbookEnabled: true);
        using var omroClient = CreateOmroClient();
        var res = await omroClient.PostAsJsonAsync("/api/appointments/overbook", new
        {
            resourceId = seeded.AppointmentId,
            decisionMode = "Autonomous"
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var overbookBlockId = doc.RootElement.GetProperty("overbookBlockId").GetGuid();

        Guid secondPatientId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var u = User.Create(Guid.NewGuid(), $"p2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var p2 = Patient.Create(u.Id, $"{Random.Shared.Next(10000000, 99999999)}-1", "Marco", "Rojas", new DateOnly(1992, 1, 1), "M", "+56912345678");
            db.Users.Add(u);
            db.Patients.Add(p2);
            await db.SaveChangesAsync();
            secondPatientId = p2.Id;
        }

        using var receptionClient = _factory.CreateClient();
        receptionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        receptionClient.DefaultRequestHeaders.Add("X-Test-Role", "Recepcionista");

        var directBookingRes = await receptionClient.PostAsJsonAsync("/api/appointments", new
        {
            ScheduleBlockId = overbookBlockId,
            PatientId = secondPatientId,
            SymptomsRaw = "Intento directo",
            SchedulingSource = "recepcion"
        });

        directBookingRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private HttpClient CreateOmroClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.GenerateOmroServiceToken());
        return client;
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    private async Task<(Guid AppointmentId, Guid BlockId, Guid StaffId, Guid PatientId)> SeedOverbookScenarioAsync(
        bool markDeadlineMissed = true,
        NotificationStatus? finalNotificationStatus = NotificationStatus.Delivered,
        DateOnly? patientBirthDate = null,
        bool boxActive = true,
        bool autoOverbookEnabled = false)
    {
        var tenantId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var policy = TenantConfirmationPolicy.CreateDefault(tenantId);
        if (autoOverbookEnabled)
        {
            policy.SetAutoOverbookEnabled(true);
        }
        db.TenantConfirmationPolicies.Add(policy);

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dr", "Campos", $"{Random.Shared.Next(10000000, 99999999)}-3", "Traumatologia", "Medico", "+56933333333");
        var patient = Patient.Create(
            patUser.Id,
            $"{Random.Shared.Next(10000000, 99999999)}-4",
            "Elena",
            "Nunez",
            patientBirthDate ?? new DateOnly(1990, 6, 15),
            "F",
            "+56944444444",
            email: $"elena_{Guid.NewGuid():N}@test.cl");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
        if (!boxActive)
        {
            box.Deactivate();
        }

        var start = DateTime.UtcNow.AddDays(4);
        var block = ScheduleBlock.Create(
            staff.Id,
            box.Id,
            start,
            start.AddHours(1),
            Guid.NewGuid(),
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start),
            new TimeOnly(10, 0),
            new TimeOnly(11, 0),
            [start.DayOfWeek]);

        var appointment = Appointment.Create(block.Id, patient.Id, "Control traumatología", "paciente", tenantId);
        var now = DateTimeOffset.UtcNow;

        if (markDeadlineMissed)
        {
            appointment.MarkFinalConfirmationRequested(now.AddHours(-12));
            appointment.MarkUnresponsive(now.AddMinutes(-5));
        }

        if (finalNotificationStatus.HasValue)
        {
            var notif = Notification.Create(
                tenantId,
                appointment.Id,
                patient.Id,
                NotificationType.FinalConfirmation,
                NotificationChannel.Email,
                "appointment.final_confirmation",
                1,
                "es-CL",
                "{}",
                now.AddHours(-12),
                Guid.NewGuid(),
                now.AddHours(-12));

            if (finalNotificationStatus.Value is NotificationStatus.Sent or NotificationStatus.Delivered or NotificationStatus.Read)
            {
                notif.MarkSent("Resend", $"re_{Guid.NewGuid():N}", "Final confirmation", now.AddHours(-12));
                if (finalNotificationStatus.Value != NotificationStatus.Sent)
                {
                    notif.ApplyDeliveryStatus(finalNotificationStatus.Value, now.AddHours(-11));
                }
            }
            else if (finalNotificationStatus.Value == NotificationStatus.Failed)
            {
                notif.RecordAttemptFailure("Bounce", isTerminal: true, now.AddHours(-11));
            }

            db.Notifications.Add(notif);
        }

        db.Users.AddRange(staffUser, patUser);
        db.Staffs.Add(staff);
        db.Patients.Add(patient);
        db.Boxes.Add(box);
        db.ScheduleBlocks.Add(block);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (appointment.Id, block.Id, staff.Id, patient.Id);
    }
}
