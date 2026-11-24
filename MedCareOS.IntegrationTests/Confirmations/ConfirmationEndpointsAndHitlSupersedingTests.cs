using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Resolutions.Queries.GetPendingResolutions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omro.Contracts.Events;

namespace MedCareOS.IntegrationTests.Confirmations;

public sealed class ConfirmationEndpointsApiFactory : CustomApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Notifications:RateLimiting:PublicActionsPermitLimit", "200");
        builder.UseSetting("Notifications:RateLimiting:ProviderWebhooksPermitLimit", "200");
    }
}

public class ConfirmationEndpointsAndHitlSupersedingTests : IClassFixture<ConfirmationEndpointsApiFactory>
{
    private readonly ConfirmationEndpointsApiFactory _factory;

    public ConfirmationEndpointsAndHitlSupersedingTests(ConfirmationEndpointsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicConfirmAlias_EarlyConfirm_AppliesLogitSignal_CancelsScheduledActions_SupersedesHitlTasks_AndIsIdempotentAcrossTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var (appointmentId, patientId) = await SeedAppointmentWithScoreAsync(
            initialScore: 0.75m,
            initialBandLabel: "Alto",
            whatsAppOptIn: true,
            phone: $"+569{Random.Shared.Next(10000000, 99999999)}",
            now: now);

        Guid task1Id;
        Guid task2Id;
        string token1;
        string token2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var corrId = Guid.NewGuid();

            db.AppointmentScheduledActions.AddRange(
                AppointmentScheduledAction.Create(
                    RiskThresholds.DefaultTenantId,
                    appointmentId,
                    ScheduledActionKind.Reminder2AndRescore,
                    now.AddHours(24),
                    corrId,
                    planVersion: 1,
                    maxAttempts: 3,
                    utcNow: now),
                AppointmentScheduledAction.Create(
                    RiskThresholds.DefaultTenantId,
                    appointmentId,
                    ScheduledActionKind.FinalConfirmation,
                    now.AddHours(48),
                    corrId,
                    planVersion: 1,
                    maxAttempts: 3,
                    utcNow: now));

            var task1 = AiResolutionTask.Create(appointmentId, 0.75m, "{\"DeferredActions\":[\"Overbook\"]}");
            var task2 = AiResolutionTask.Create(appointmentId, 0.78m, "{\"DeferredActions\":[\"Monitor\"]}");
            db.AiResolutionTasks.AddRange(task1, task2);
            await db.SaveChangesAsync();

            task1Id = task1.Id;
            task2Id = task2.Id;

            var tokenService = scope.ServiceProvider.GetRequiredService<IPatientActionTokenService>();
            token1 = (await tokenService.IssueAsync(
                appointmentId,
                patientId,
                notificationId: null,
                PatientActionScope.ConfirmOrCancel,
                TimeSpan.FromHours(24))).RawToken;
            token2 = (await tokenService.IssueAsync(
                appointmentId,
                patientId,
                notificationId: null,
                PatientActionScope.ConfirmOrCancel,
                TimeSpan.FromHours(24))).RawToken;
        }

        using var staffClient = CreateAuthenticatedClient("Recepcionista", Guid.NewGuid());
        var pendingBefore = await staffClient.GetFromJsonAsync<List<ResolutionTaskDto>>("/api/resolutions/pending");
        pendingBefore!.Select(t => t.Id).Should().Contain([task1Id, task2Id]);

        using var anonClient = _factory.CreateClient();
        var confirmRes1 = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/confirm",
            new { Token = token1 });
        confirmRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var doc = JsonDocument.Parse(await confirmRes1.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("appointmentId").GetGuid().Should().Be(appointmentId);
            doc.RootElement.GetProperty("confirmationState").GetString().Should().Be("ConfirmedEarly");
            doc.RootElement.GetProperty("appointmentStatus").GetString().Should().Be("Confirmed");
            doc.RootElement.GetProperty("wasIdempotentReplay").GetBoolean().Should().BeFalse();
        }

        // Verificar persistencia completa: score_modelo inmutable, logit -1.5, histéresis, acciones canceladas y HITL Superseded
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var appointment = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            appointment.Status.Should().Be(AppointmentStatus.Confirmed);
            appointment.ConfirmationState.Should().Be(ConfirmationState.ConfirmedEarly);
            appointment.OrchestrationSuppressed.Should().BeTrue();
            appointment.ModelRiskScore.Should().Be(0.75m);
            appointment.EffectiveRiskScore.Should().BeLessThan(0.55m);
            appointment.EffectiveBand.Should().Be(RiskBandLevel.Medium);

            var signals = await db.RiskSignals.AsNoTracking().Where(s => s.AppointmentId == appointmentId).ToListAsync();
            signals.Should().ContainSingle();
            signals[0].Kind.Should().Be(RiskSignalKind.ConfirmedEarly);
            signals[0].Weight.Should().Be(-1.5000m);
            signals[0].Source.Should().Be("public_token");

            var actions = await db.AppointmentScheduledActions.AsNoTracking()
                .Where(a => a.AppointmentId == appointmentId)
                .ToListAsync();
            actions.Should().HaveCount(2).And.OnlyContain(a =>
                a.Status == ScheduledActionStatus.Cancelled &&
                a.LastError == "PatientConfirmedEarly");

            var tasks = await db.AiResolutionTasks.AsNoTracking()
                .Where(t => t.ResourceId == appointmentId)
                .ToListAsync();
            tasks.Should().HaveCount(2).And.OnlyContain(t => t.Status == AiResolutionStatus.Superseded);
        }

        // UI de recepción ya no ve tareas obsoletas
        var pendingAfter = await staffClient.GetFromJsonAsync<List<ResolutionTaskDto>>("/api/resolutions/pending");
        pendingAfter!.Select(t => t.Id).Should().NotContain(task1Id).And.NotContain(task2Id);

        // Intentar aprobar una tarea Superseded devuelve 409 Conflict y nunca publica ResolutionApproved
        using var adminClient = CreateAuthenticatedClient("Administrador", Guid.NewGuid());
        var approveRes = await adminClient.PatchAsync($"/api/resolutions/{task1Id}/approve", null);
        approveRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var approvedEvents = await harness.Published.SelectAsync<ResolutionApproved>(
            e => e.Context.Message.ResolutionTaskId == task1Id).ToListAsync();
        approvedEvents.Should().BeEmpty();

        // Replay del mismo token -> 410 Gone
        var replaySameToken = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/confirm",
            new { Token = token1 });
        replaySameToken.StatusCode.Should().Be(HttpStatusCode.Gone);

        // Segundo token válido (p. ej. de recordatorio anterior) -> 200 OK idempotente sin duplicar señal
        var secondTokenConfirm = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/confirm",
            new { Token = token2 });
        secondTokenConfirm.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var doc2 = JsonDocument.Parse(await secondTokenConfirm.Content.ReadAsStringAsync()))
        {
            doc2.RootElement.GetProperty("wasIdempotentReplay").GetBoolean().Should().BeTrue();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var signalCount = await db.RiskSignals.AsNoTracking()
                .CountAsync(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.ConfirmedEarly);
            signalCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task PublicDeclineAlias_CancelsAppointment_RecordsDeclinedSignal_SupersedesHitl_AndBlocksSubsequentConfirmToken()
    {
        var now = DateTimeOffset.UtcNow;
        var (appointmentId, patientId) = await SeedAppointmentWithScoreAsync(
            initialScore: 0.65m,
            initialBandLabel: "Medio",
            whatsAppOptIn: false,
            phone: $"+569{Random.Shared.Next(10000000, 99999999)}",
            now: now);

        Guid taskId;
        string declineToken;
        string confirmToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AppointmentScheduledActions.Add(
                AppointmentScheduledAction.Create(
                    RiskThresholds.DefaultTenantId,
                    appointmentId,
                    ScheduledActionKind.ConfirmationDeadline,
                    now.AddHours(12),
                    Guid.NewGuid(),
                    planVersion: 1,
                    maxAttempts: 3,
                    utcNow: now));

            var task = AiResolutionTask.Create(appointmentId, 0.80m, "{\"DeferredActions\":[\"Overbook\"]}");
            db.AiResolutionTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;

            var tokenService = scope.ServiceProvider.GetRequiredService<IPatientActionTokenService>();
            declineToken = (await tokenService.IssueAsync(
                appointmentId,
                patientId,
                notificationId: null,
                PatientActionScope.ConfirmOrCancel,
                TimeSpan.FromHours(24))).RawToken;
            confirmToken = (await tokenService.IssueAsync(
                appointmentId,
                patientId,
                notificationId: null,
                PatientActionScope.ConfirmOrCancel,
                TimeSpan.FromHours(24))).RawToken;
        }

        using var anonClient = _factory.CreateClient();
        var declineRes = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/decline",
            new { Token = declineToken, Reason = "Me surgió un imprevisto" });
        declineRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var doc = JsonDocument.Parse(await declineRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("confirmationState").GetString().Should().Be("DeclinedByPatient");
            doc.RootElement.GetProperty("appointmentStatus").GetString().Should().Be("Cancelled");
            doc.RootElement.GetProperty("wasIdempotentReplay").GetBoolean().Should().BeFalse();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var appointment = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            appointment.Status.Should().Be(AppointmentStatus.Cancelled);
            appointment.ConfirmationState.Should().Be(ConfirmationState.DeclinedByPatient);
            appointment.OrchestrationSuppressed.Should().BeTrue();

            var signal = await db.RiskSignals.AsNoTracking()
                .SingleAsync(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.DeclinedByPatient);
            signal.Source.Should().Be("public_token");

            var action = await db.AppointmentScheduledActions.AsNoTracking()
                .SingleAsync(a => a.AppointmentId == appointmentId);
            action.Status.Should().Be(ScheduledActionStatus.Cancelled);
            action.LastError.Should().Be("PatientDeclined");

            var task = await db.AiResolutionTasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
            task.Status.Should().Be(AiResolutionStatus.Superseded);
        }

        using var staffClient = CreateAuthenticatedClient("Recepcionista", Guid.NewGuid());
        var pending = await staffClient.GetFromJsonAsync<List<ResolutionTaskDto>>("/api/resolutions/pending");
        pending!.Select(t => t.Id).Should().NotContain(taskId);

        // Intentar confirmar después con otro token -> 410 Gone (cita cancelada no accionable)
        var lateConfirm = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/confirm",
            new { Token = confirmToken });
        lateConfirm.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task WhatsAppWebhook_FinalConfirmation_SuppressesOrchestration_PreservesEffectiveScore_SupersedesHitl_AndIsIdempotent()
    {
        var now = DateTimeOffset.UtcNow;
        var phone = $"+569{Random.Shared.Next(10000000, 99999999)}";
        var (appointmentId, patientId) = await SeedAppointmentWithScoreAsync(
            initialScore: 0.65m,
            initialBandLabel: "Medio",
            whatsAppOptIn: true,
            phone: phone,
            now: now);

        decimal scoreBeforeConfirm;
        Guid deadlineActionId;
        Guid taskId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var processor = scope.ServiceProvider.GetRequiredService<IRiskSignalProcessor>();

            await processor.RecordAsync(
                new RecordRiskSignalRequest(
                    AppointmentId: appointmentId,
                    Kind: RiskSignalKind.ReminderUnanswered,
                    Source: "scheduled_actions_worker",
                    IdempotencyKey: $"unanswered_r1:{appointmentId:N}",
                    CorrelationId: Guid.NewGuid(),
                    OccurredAtUtc: now.AddHours(-24)),
                CancellationToken.None);

            await db.SaveChangesAsync();

            var appointment = await db.Appointments.FirstAsync(a => a.Id == appointmentId);
            appointment.MarkFinalConfirmationRequested(now.AddHours(-2));
            appointment.EffectiveBand.Should().Be(RiskBandLevel.High);
            scoreBeforeConfirm = appointment.EffectiveRiskScore!.Value;

            var notification = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.FinalConfirmation,
                NotificationChannel.WhatsApp,
                "appointment.final_confirmation",
                1,
                "es-CL",
                "{}",
                now.AddHours(-2),
                Guid.NewGuid(),
                now.AddHours(-2));
            notification.MarkSent("MetaCloud", $"wamid.out.{Guid.NewGuid():N}", "Última confirmación...", now.AddHours(-2));
            notification.ApplyDeliveryStatus(NotificationStatus.Delivered, now.AddHours(-2));

            var deadlineAction = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                ScheduledActionKind.ConfirmationDeadline,
                now.AddHours(10),
                Guid.NewGuid(),
                planVersion: 1,
                maxAttempts: 3,
                utcNow: now);

            var task = AiResolutionTask.Create(appointmentId, scoreBeforeConfirm, "{\"DeferredActions\":[\"Overbook\"]}");

            db.Notifications.Add(notification);
            db.AppointmentScheduledActions.Add(deadlineAction);
            db.AiResolutionTasks.Add(task);
            await db.SaveChangesAsync();

            deadlineActionId = deadlineAction.Id;
            taskId = task.Id;
        }

        using var client = _factory.CreateClient();
        var buttonJson = BuildMetaInboundButtonPayload(
            $"wamid.in.{Guid.NewGuid():N}",
            phone.TrimStart('+'),
            $"CONFIRM:{appointmentId}",
            "Confirmar");

        var firstRes = await client.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", buttonJson, _factory.TestMetaAppSecret));
        firstRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var appointment = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);

            appointment.ConfirmationState.Should().Be(ConfirmationState.FinalConfirmed);
            appointment.Status.Should().Be(AppointmentStatus.Confirmed);
            appointment.OrchestrationSuppressed.Should().BeTrue();
            appointment.EffectiveRiskScore.Should().Be(scoreBeforeConfirm);
            appointment.EffectiveBand.Should().Be(RiskBandLevel.High);

            var finalSignal = await db.RiskSignals.AsNoTracking()
                .SingleAsync(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.FinalConfirmed);
            finalSignal.Weight.Should().Be(0.0000m);
            finalSignal.Source.Should().Be("whatsapp_inbound");

            var deadline = await db.AppointmentScheduledActions.AsNoTracking().FirstAsync(a => a.Id == deadlineActionId);
            deadline.Status.Should().Be(ScheduledActionStatus.Cancelled);
            deadline.LastError.Should().Be("PatientFinalConfirmed");

            var task = await db.AiResolutionTasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
            task.Status.Should().Be(AiResolutionStatus.Superseded);
        }

        using var staffClient = CreateAuthenticatedClient("Recepcionista", Guid.NewGuid());
        var pending = await staffClient.GetFromJsonAsync<List<ResolutionTaskDto>>("/api/resolutions/pending");
        pending!.Select(t => t.Id).Should().NotContain(taskId);

        // Segunda respuesta WhatsApp con distinto InboundMessageId ("SI") -> idempotente, sin duplicar señal ni evento
        var replayTextJson = BuildMetaInboundTextPayload(
            $"wamid.in.{Guid.NewGuid():N}",
            phone.TrimStart('+'),
            "SI");
        var secondRes = await client.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", replayTextJson, _factory.TestMetaAppSecret));
        secondRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var count = await db.RiskSignals.AsNoTracking()
                .CountAsync(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.FinalConfirmed);
            count.Should().Be(1);
        }

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var replyEvents = await harness.Published.SelectAsync<PatientAppointmentReplyReceived>(
            e => e.Context.Message.AppointmentId == appointmentId &&
                 e.Context.Message.Intent == PatientReplyIntent.Confirm).ToListAsync();
        replyEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task WhatsAppWebhook_DeclineAndLateOmroProposal_SupersedesObsoleteProposalsAndIgnoresLateConfirm()
    {
        var now = DateTimeOffset.UtcNow;
        var phone1 = $"+569{Random.Shared.Next(10000000, 99999999)}";
        var (app1Id, pat1Id) = await SeedAppointmentWithScoreAsync(
            initialScore: 0.72m,
            initialBandLabel: "Alto",
            whatsAppOptIn: true,
            phone: phone1,
            now: now);

        Guid app1TaskId;
        Guid app1ActionId;
        Guid app2Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var notification = Notification.Create(
                RiskThresholds.DefaultTenantId,
                app1Id,
                pat1Id,
                NotificationType.Reminder,
                NotificationChannel.WhatsApp,
                "appointment.reminder",
                1,
                "es-CL",
                "{}",
                now.AddMinutes(-15),
                Guid.NewGuid(),
                now.AddMinutes(-15));
            notification.MarkSent("MetaCloud", $"wamid.out.{Guid.NewGuid():N}", "Recordatorio...", now.AddMinutes(-15));
            notification.ApplyDeliveryStatus(NotificationStatus.Delivered, now.AddMinutes(-14));

            var action = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId,
                app1Id,
                ScheduledActionKind.Reminder2AndRescore,
                now.AddHours(12),
                Guid.NewGuid(),
                planVersion: 1,
                maxAttempts: 3,
                utcNow: now);

            var task = AiResolutionTask.Create(app1Id, 0.72m, "{\"DeferredActions\":[\"Overbook\"]}");
            db.Notifications.Add(notification);
            db.AppointmentScheduledActions.Add(action);
            db.AiResolutionTasks.Add(task);
            await db.SaveChangesAsync();

            app1TaskId = task.Id;
            app1ActionId = action.Id;
        }

        using var webhookClient = _factory.CreateClient();

        // Parte A: WhatsApp "DECLINO" cancela app1, marca Superseded y registra señal
        var declineJson = BuildMetaInboundTextPayload($"wamid.in.{Guid.NewGuid():N}", phone1.TrimStart('+'), "DECLINO");
        var declineRes = await webhookClient.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", declineJson, _factory.TestMetaAppSecret));
        declineRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Respuesta tardía "SI" por WhatsApp tras declinar -> 200 OK sin error 500 y sin revertir estado
        var lateConfirmJson = BuildMetaInboundTextPayload($"wamid.in.{Guid.NewGuid():N}", phone1.TrimStart('+'), "SI");
        var lateConfirmRes = await webhookClient.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", lateConfirmJson, _factory.TestMetaAppSecret));
        lateConfirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app1 = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == app1Id);
            app1.Status.Should().Be(AppointmentStatus.Cancelled);
            app1.ConfirmationState.Should().Be(ConfirmationState.DeclinedByPatient);

            (await db.RiskSignals.AsNoTracking()
                .AnyAsync(s => s.AppointmentId == app1Id && s.Kind == RiskSignalKind.DeclinedByPatient))
                .Should().BeTrue();
            (await db.RiskSignals.AsNoTracking()
                .AnyAsync(s => s.AppointmentId == app1Id && (s.Kind == RiskSignalKind.ConfirmedEarly || s.Kind == RiskSignalKind.FinalConfirmed)))
                .Should().BeFalse();

            (await db.AppointmentScheduledActions.AsNoTracking().FirstAsync(a => a.Id == app1ActionId))
                .Status.Should().Be(ScheduledActionStatus.Cancelled);
            (await db.AiResolutionTasks.AsNoTracking().FirstAsync(t => t.Id == app1TaskId))
                .Status.Should().Be(AiResolutionStatus.Superseded);
        }

        // Parte B: Cita #2 ya confirmada recibe propuesta tardía de OMRO -> se persiste directamente como Superseded y no aparece en Pending
        var (seededApp2Id, _) = await SeedAppointmentWithScoreAsync(
            initialScore: 0.85m,
            initialBandLabel: "Alto",
            whatsAppOptIn: false,
            phone: $"+569{Random.Shared.Next(10000000, 99999999)}",
            now: now);
        app2Id = seededApp2Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app2 = await db.Appointments.FirstAsync(a => a.Id == app2Id);
            app2.RegisterPatientConfirmation(now);
            await db.SaveChangesAsync();
        }

        using var omroClient = _factory.CreateClient();
        omroClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.GenerateOmroServiceToken());

        var proposeRes = await omroClient.PostAsJsonAsync(
            "/api/resolutions/propose",
            new
            {
                ResourceId = app2Id,
                RiskProbability = 0.88m,
                AiPlanJson = "{\"DeferredActions\":[\"Overbook\"]}"
            });
        proposeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid proposedTaskId;
        using (var doc = JsonDocument.Parse(await proposeRes.Content.ReadAsStringAsync()))
        {
            proposedTaskId = doc.RootElement.GetProperty("taskId").GetGuid();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var savedTask = await db.AiResolutionTasks.AsNoTracking().FirstAsync(t => t.Id == proposedTaskId);
            savedTask.Status.Should().Be(AiResolutionStatus.Superseded);
        }

        using var staffClient = CreateAuthenticatedClient("Recepcionista", Guid.NewGuid());
        var pending = await staffClient.GetFromJsonAsync<List<ResolutionTaskDto>>("/api/resolutions/pending");
        pending!.Select(t => t.Id).Should().NotContain(proposedTaskId);
    }

    private HttpClient CreateAuthenticatedClient(string role, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        return client;
    }

    private async Task<(Guid AppointmentId, Guid PatientId)> SeedAppointmentWithScoreAsync(
        decimal initialScore,
        string initialBandLabel,
        bool whatsAppOptIn,
        string phone,
        DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dr", "Mora", $"{Random.Shared.Next(10000000, 99999999)}-1", "Cardiologia", "Medico", "+56911111111");
        var patient = Patient.Create(
            patUser.Id,
            $"{Random.Shared.Next(10000000, 99999999)}-2",
            "Camila",
            "Diaz",
            new DateOnly(1992, 3, 14),
            "F",
            phone,
            email: $"camila_{Guid.NewGuid():N}@test.cl",
            preferredChannel: whatsAppOptIn ? NotificationChannel.WhatsApp : NotificationChannel.Email,
            whatsAppOptIn: whatsAppOptIn,
            consentSource: "test_seed");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
        var start = now.UtcDateTime.AddDays(3);
        var block = ScheduleBlock.Create(
            staff.Id,
            box.Id,
            start,
            start.AddMinutes(30),
            Guid.NewGuid(),
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30),
            [start.DayOfWeek]);

        var appointment = Appointment.Create(block.Id, patient.Id, "Control preventivo", "paciente");
        var policy = TenantConfirmationPolicy.CreateDefault(appointment.TenantId, now);
        appointment.RecordInitialModelScore(initialScore, initialBandLabel, policy.GetHysteresisThresholds(), now);

        db.Users.AddRange(staffUser, patUser);
        db.Staffs.Add(staff);
        db.Patients.Add(patient);
        db.Boxes.Add(box);
        db.ScheduleBlocks.Add(block);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (appointment.Id, patient.Id);
    }

    private static HttpRequestMessage CreateMetaSignedRequest(string uri, string jsonBody, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonBody);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();

        var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(bytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req.Headers.Add("X-Hub-Signature-256", sig);
        return req;
    }

    private static string BuildMetaInboundButtonPayload(string msgId, string fromDigits, string payload, string title) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba_1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messages": [
                      {
                        "from": "{{fromDigits}}",
                        "id": "{{msgId}}",
                        "timestamp": "1732450200",
                        "type": "button",
                        "button": {
                          "payload": "{{payload}}",
                          "text": "{{title}}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string BuildMetaInboundTextPayload(string msgId, string fromDigits, string textBody) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba_1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messages": [
                      {
                        "from": "{{fromDigits}}",
                        "id": "{{msgId}}",
                        "timestamp": "1732450300",
                        "type": "text",
                        "text": {
                          "body": "{{textBody}}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;
}
