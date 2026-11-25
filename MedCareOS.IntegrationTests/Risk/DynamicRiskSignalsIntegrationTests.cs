using System.Text.Json;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Appointments.Workers;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Commands.ProcessProviderDeliveryStatus;
using MedCareOS.Application.Notifications.Commands.ProcessWhatsAppInboundMessage;
using MedCareOS.Application.Notifications.Consumers;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using MedCareOS.Infrastructure.Messaging.Configuration;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Messaging.Observers;
using MedCareOS.Infrastructure.Notifications.Channels;
using MedCareOS.Infrastructure.Notifications.Security;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedCareOS.IntegrationTests.Risk;

public class DynamicRiskSignalsIntegrationTests
{
    [Fact]
    public void ApplicationDbContext_ShouldMapRiskSignalsTableUniqueIdempotencyAndAppointmentScoreColumns()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=medcare_design;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var signalEntity = model.FindEntityType(typeof(RiskSignal));
        signalEntity.Should().NotBeNull();
        signalEntity!.GetTableName().Should().Be("risk_signals");
        signalEntity.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "UX_risk_signals_appointment_idempotency")!
            .IsUnique.Should().BeTrue();
        signalEntity.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "IX_risk_signals_appointment_occurred")
            .Should().NotBeNull();

        var appEntity = model.FindEntityType(typeof(Appointment))!;
        appEntity.FindProperty(nameof(Appointment.ModelRiskScore))!.GetColumnName().Should().Be("score_modelo");
        appEntity.FindProperty(nameof(Appointment.LatestModelRiskScore))!.GetColumnName().Should().Be("latest_model_score");
        appEntity.FindProperty(nameof(Appointment.RescoredAt))!.GetColumnName().Should().Be("rescored_at");
        appEntity.FindProperty(nameof(Appointment.EffectiveRiskScore))!.GetColumnName().Should().Be("score_efectivo");
        appEntity.FindProperty(nameof(Appointment.EffectiveBand))!.GetColumnName().Should().Be("effective_band");
    }

    [Fact]
    public async Task Reminder2AndRescore_CallsPredictWithRealFeatures_PreservesImmutableT0ModelScore_AndAppliesDeliveredUnansweredSignal()
    {
        var capturedRequests = new List<PredictRequestDto>();
        int callCount = 0;

        var aiService = new DelegateAiService(
            onPredict: (req, _) =>
            {
                capturedRequests.Add(req);
                var count = Interlocked.Increment(ref callCount);
                return Task.FromResult(count == 1
                    ? new PredictResponseDto(req.AppointmentId, 0.45m, "Bajo", "asiste")
                    : new PredictResponseDto(req.AppointmentId, 0.62m, "Medio", "no_asiste"));
            });

        await using var host = await RiskTestHost.CreateAsync(aiService);

        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(5);
        var (appointmentId, _) = await host.CreateAppointmentWithPastNoShowAsync(startUtc);

        // 1. Score inicial T0
        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.ModelRiskScore.Should().Be(0.45m);
            app.LatestModelRiskScore.Should().BeNull();
            app.EffectiveRiskScore.Should().Be(0.45m);
            app.EffectiveBand.Should().Be(RiskBandLevel.Low);
        }

        // 2. T-72h Reminder 1 -> marcar como Delivered vía proveedor
        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        var r1Time = startUtc.AddHours(-72).AddMinutes(1);
        await processor.ProcessDueBatchAsync("worker-1", r1Time);
        await host.WaitForSentNotificationsAsync(2); // T0 + R1

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var r1Notif = await db.Notifications
                .Where(n => n.AppointmentId == appointmentId && n.Type == NotificationType.Reminder)
                .OrderByDescending(n => n.CreatedAt)
                .FirstAsync();
            r1Notif.ApplyDeliveryStatus(NotificationStatus.Delivered, r1Time.AddSeconds(10));
            await db.SaveChangesAsync();
        }

        // 3. T-48h Reminder 2 + Re-score con daysOfWaiting real (2 días), 1 ausencia previa real (E0-8) y canal real
        var r2Time = startUtc.AddHours(-48).AddMinutes(1);
        var r2Batch = await processor.ProcessDueBatchAsync("worker-1", r2Time);
        r2Batch.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));

        capturedRequests.Should().HaveCount(2);
        capturedRequests[1].DaysOfWaiting.Should().Be((startUtc.UtcDateTime.Date - r2Time.UtcDateTime.Date).Days);
        capturedRequests[1].PreviousAbsences.Should().Be(1);
        capturedRequests[1].ReminderChannel.Should().Be("whatsapp");

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);

            app.ModelRiskScore.Should().Be(0.45m, "score_modelo T0 es inmutable");
            app.LatestModelRiskScore.Should().Be(0.62m);
            app.RescoredAt.Should().Be(r2Time);
            app.EffectiveRiskScore.Should().Be(RiskScoreCalculator.ApplyLogitDelta(0.62m, 0.4m));
            app.EffectiveBand.Should().Be(RiskBandLevel.High);

            var signals = await db.RiskSignals
                .AsNoTracking()
                .Where(s => s.AppointmentId == appointmentId)
                .OrderBy(s => s.OccurredAt)
                .ToListAsync();

            signals.Select(s => s.Kind).Should().Contain([RiskSignalKind.ReminderUnanswered, RiskSignalKind.ModelRescored]);
            signals.Single(s => s.Kind == RiskSignalKind.ReminderUnanswered).Weight.Should().Be(0.4000m);
            signals.Single(s => s.Kind == RiskSignalKind.ModelRescored).Weight.Should().Be(0.0000m);
        }
    }

    [Fact]
    public async Task Reminder2AndRescore_WhenFastApiFailsTransiently_RetriesAndGracefullyDegradesOnFinalAttempt()
    {
        int calls = 0;
        var aiService = new DelegateAiService(
            onPredict: (req, _) =>
            {
                var n = Interlocked.Increment(ref calls);
                if (n == 1)
                    return Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.40m, "Bajo", "asiste"));

                throw new AiServiceTransientException("FastAPI temporalmente caído", 503);
            });

        await using var host = await RiskTestHost.CreateAsync(aiService, scheduledMaxAttempts: 2);
        var startUtc = DateTimeOffset.UtcNow.AddDays(5);
        var (appointmentId, _) = await host.CreateAppointmentWithPastNoShowAsync(startUtc);

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-72).AddMinutes(1));
        await host.WaitForSentNotificationsAsync(2);

        // Intento 1 en T-48h -> falla transitoria -> vuelve a Pending (reintento antes de enviar R2)
        var r2Time = startUtc.AddHours(-48).AddMinutes(1);
        var attempt1 = await processor.ProcessDueBatchAsync("worker-1", r2Time);
        attempt1.Should().Be(new ScheduledActionsBatchResult(1, 0, 0, 1, 0));

        // Intento 2 (== MaxAttempts=2) -> degrada con gracia, mantiene score T0 y sí envía Reminder 2
        var attempt2 = await processor.ProcessDueBatchAsync("worker-1", r2Time.AddMinutes(10));
        attempt2.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));
        await host.WaitForSentNotificationsAsync(3);

        using var scope = host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
        app.ModelRiskScore.Should().Be(0.40m);
        app.LatestModelRiskScore.Should().BeNull();
        app.EffectiveRiskScore.Should().Be(0.40m);

        var r2Action = await db.AppointmentScheduledActions.AsNoTracking()
            .FirstAsync(a => a.AppointmentId == appointmentId && a.Kind == ScheduledActionKind.Reminder2AndRescore);
        r2Action.Status.Should().Be(ScheduledActionStatus.Completed);
        r2Action.MetadataJson.Should().Contain("\"rescoreSkipped\":true");
    }

    [Fact]
    public async Task UnansweredReminderSignals_RequireDeliveredStatusByDefault_AndRespectCumulativeCap1Point2()
    {
        var aiService = new DelegateAiService(
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.30m, "Bajo", "asiste")));

        await using var host = await RiskTestHost.CreateAsync(aiService);
        var startUtc = DateTimeOffset.UtcNow.AddDays(5);
        var (appointmentId, _) = await host.CreateAppointmentWithPastNoShowAsync(startUtc);

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();

        // 1. T-72h R1 queda solo en Sent (no Delivered) y CountSentAsDeliveredForUnanswered=false -> NO suma +0.4
        await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-72).AddMinutes(1));
        await host.WaitForSentNotificationsAsync(2);

        await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-48).AddMinutes(1));
        await host.WaitForSentNotificationsAsync(3);

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var signals = await db.RiskSignals.AsNoTracking()
                .Where(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.ReminderUnanswered)
                .ToListAsync();
            signals.Should().BeEmpty();

            // Marcar R2 como Delivered y FC también como Delivered más adelante, además de inyectar señales extra para validar tope +1.2
            var r2Notif = await db.Notifications
                .Where(n => n.AppointmentId == appointmentId && n.Type == NotificationType.Reminder)
                .OrderByDescending(n => n.ScheduledFor)
                .FirstAsync();
            r2Notif.ApplyDeliveryStatus(NotificationStatus.Delivered, startUtc.AddHours(-47));
            await db.SaveChangesAsync();
        }

        // T-24h -> detecta R2 Delivered -> +0.4
        await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-24).AddMinutes(1));
        await host.WaitForSentNotificationsAsync(4);

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fcNotif = await db.Notifications.FirstAsync(n =>
                n.AppointmentId == appointmentId && n.Type == NotificationType.FinalConfirmation);
            fcNotif.ApplyDeliveryStatus(NotificationStatus.Read, startUtc.AddHours(-23));
            await db.SaveChangesAsync();
        }

        // T-12h -> detecta FinalConfirmation Read -> +0.4 (acumulado 0.8)
        await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-12).AddMinutes(1));

        // Añadir 2 señales adicionales de ReminderUnanswered (total 4 * 0.4 = 1.6, debe topar en 1.2)
        using (var scope = host.Provider.CreateScope())
        {
            var signalProcessor = scope.ServiceProvider.GetRequiredService<IRiskSignalProcessor>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await signalProcessor.RecordAsync(new RecordRiskSignalRequest(
                appointmentId, RiskSignalKind.ReminderUnanswered, "test", "extra_unanswered_1", Guid.NewGuid(), DateTimeOffset.UtcNow));
            await signalProcessor.RecordAsync(new RecordRiskSignalRequest(
                appointmentId, RiskSignalKind.ReminderUnanswered, "test", "extra_unanswered_2", Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(1)));
            await db.SaveChangesAsync();

            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.EffectiveRiskScore.Should().Be(RiskScoreCalculator.ApplyLogitDelta(0.30m, 1.2m));
        }
    }

    [Fact]
    public async Task DeliveryFailedWebhookAndPermanentConsumerFailure_CreateSingleSignalAndSingleReceptionTask()
    {
        var aiService = new DelegateAiService(
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.40m, "Bajo", "asiste")));

        await using var host = await RiskTestHost.CreateAsync(aiService);
        var startUtc = DateTimeOffset.UtcNow.AddDays(3);
        var (appointmentId, patientId) = await host.CreateAppointmentWithPastNoShowAsync(startUtc);

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        Guid notif1Id;
        string providerMsgId = $"wamid.fail.{Guid.NewGuid():N}";
        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var notif = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.Reminder,
                NotificationChannel.WhatsApp,
                DefaultNotificationTemplateCatalog.Keys.Reminder,
                1,
                "es-CL",
                "{}",
                now.AddMinutes(5),
                Guid.NewGuid(),
                now);
            notif.MarkSent("MetaCloud", providerMsgId, "Recordatorio", now);
            db.Notifications.Add(notif);
            await db.SaveChangesAsync();
            notif1Id = notif.Id;
        }

        // 1. Webhook Failed + replay idempotente -> solo 1 RiskSignal(+0.8) y 1 ReceptionTask
        using (var scope = host.Provider.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<MediatR.IRequestHandler<ProcessProviderDeliveryStatusCommand>>();
            await handler.Handle(
                new ProcessProviderDeliveryStatusCommand("WhatsApp", "evt-fail-1", providerMsgId, NotificationStatus.Failed, DateTimeOffset.UtcNow, "Invalid phone number", false),
                CancellationToken.None);
            await handler.Handle(
                new ProcessProviderDeliveryStatusCommand("WhatsApp", "evt-fail-1", providerMsgId, NotificationStatus.Failed, DateTimeOffset.UtcNow, "Invalid phone number", false),
                CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.EffectiveRiskScore.Should().Be(RiskScoreCalculator.ApplyLogitDelta(0.40m, 0.8m));

            var signals = await db.RiskSignals.AsNoTracking()
                .Where(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.DeliveryFailed)
                .ToListAsync();
            signals.Should().ContainSingle();
            signals[0].Weight.Should().Be(0.8000m);

            var tasks = await db.ReceptionTasks.AsNoTracking()
                .Where(t => t.AppointmentId == appointmentId && t.TaskType == ReceptionTaskType.FailedNotificationEscalation)
                .ToListAsync();
            tasks.Should().ContainSingle();
        }

        // 2. Fallo permanente en SendNotificationConsumer -> agrega señal DeliveryFailed por notif2 sin duplicar ReceptionTask activa
        host.FakeStore.SetOverrideHandler(_ =>
            DeliveryResult.Failure("Fake", DeliveryFailureKind.Permanent, "Hard bounce 550", 550, "bounce"));

        Guid notif2Id;
        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var templateParams = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["patientFirstName"] = "Camila",
                ["clinicName"] = "Clínica MedCare",
                ["appointmentDate"] = "27/11/2026",
                ["appointmentTime"] = "11:00 hrs",
                ["deadline"] = "27/11/2026 08:00 hrs"
            });
            var notif2 = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.FinalConfirmation,
                NotificationChannel.Email,
                DefaultNotificationTemplateCatalog.Keys.FinalConfirmation,
                1,
                "es-CL",
                templateParams,
                now.AddMinutes(6),
                Guid.NewGuid(),
                now);
            db.Notifications.Add(notif2);
            await db.SaveChangesAsync();
            notif2Id = notif2.Id;
        }

        await host.Harness.Bus.Publish(new SendNotificationRequested(notif2Id, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var faulted = await host.Harness.Consumed.SelectAsync<SendNotificationRequested>(
            c => c.Context.Message.NotificationId == notif2Id && c.Exception != null).FirstOrDefaultAsync();
        faulted.Should().NotBeNull();
        faulted!.Exception.Should().BeOfType<NotificationPermanentDeliveryException>();

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.EffectiveRiskScore.Should().Be(RiskScoreCalculator.ApplyLogitDelta(0.40m, 1.6m));

            var signals = await db.RiskSignals.AsNoTracking()
                .Where(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.DeliveryFailed)
                .ToListAsync();
            signals.Should().HaveCount(2);

            var tasks = await db.ReceptionTasks.AsNoTracking()
                .Where(t => t.AppointmentId == appointmentId && t.TaskType == ReceptionTaskType.FailedNotificationEscalation)
                .ToListAsync();
            tasks.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task WhatsAppFreeTextInquiry_RecordsNeutralSignalWithoutChangingScoreOrStoringMessageBody()
    {
        var aiService = new DelegateAiService(
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.52m, "Medio", "no_asiste")));

        await using var host = await RiskTestHost.CreateAsync(aiService);
        var startUtc = DateTimeOffset.UtcNow.AddDays(3);
        var (appointmentId, patientId) = await host.CreateAppointmentWithPastNoShowAsync(startUtc, phone: "+56977778888");

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var notif = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.Reminder,
                NotificationChannel.WhatsApp,
                DefaultNotificationTemplateCatalog.Keys.Reminder,
                1,
                "es-CL",
                "{}",
                now.AddMinutes(1),
                Guid.NewGuid(),
                now);
            notif.MarkSent("MetaCloud", $"wamid.inq.{Guid.NewGuid():N}", "Recordatorio", now);
            db.Notifications.Add(notif);
            await db.SaveChangesAsync();
        }

        const string inquiryText = "A que hora debo llegar exactamente con mi examen?";
        using (var scope = host.Provider.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<MediatR.IRequestHandler<ProcessWhatsAppInboundMessageCommand>>();
            await handler.Handle(
                new ProcessWhatsAppInboundMessageCommand("wamid.in.inquiry.1", "56977778888", null, inquiryText, DateTimeOffset.UtcNow),
                CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.ConfirmationState.Should().Be(ConfirmationState.Pending);
            app.EffectiveRiskScore.Should().Be(0.52m);

            var signal = await db.RiskSignals.AsNoTracking()
                .SingleAsync(s => s.AppointmentId == appointmentId && s.Kind == RiskSignalKind.RepliedWithoutConfirmation);
            signal.Weight.Should().Be(0.0000m);
            signal.MetadataJson.Should().NotContain(inquiryText);
        }
    }

    private sealed class DelegateAiService : IAiClinicalService
    {
        private readonly Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> _onPredict;

        public DelegateAiService(Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> onPredict)
        {
            _onPredict = onPredict;
        }

        public Task<NlpSummaryResponseDto> GetNlpSummaryAsync(Guid appointmentId, string symptomsRaw, CancellationToken ctx)
            => Task.FromResult(new NlpSummaryResponseDto("Control", [], "baja"));

        public Task<PredictResponseDto> PredictNoShowRiskAsync(PredictRequestDto request, CancellationToken ctx)
            => _onPredict(request, ctx);
    }

    private sealed class RiskTestHost : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public ITestHarness Harness { get; }
        public InMemoryFakeNotificationStore FakeStore { get; }

        private RiskTestHost(ServiceProvider provider, ITestHarness harness, InMemoryFakeNotificationStore fakeStore)
        {
            Provider = provider;
            Harness = harness;
            FakeStore = fakeStore;
        }

        public static async Task<RiskTestHost> CreateAsync(IAiClinicalService aiService, int scheduledMaxAttempts = 5)
        {
            var services = new ServiceCollection();
            var dbName = $"DynamicRiskDb_{Guid.NewGuid()}";
            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<ApplicationDbContext>(o =>
                o.UseInMemoryDatabase(dbName).UseInternalServiceProvider(inMemoryProvider));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<IAppointmentRepository, AppointmentRepository>();
            services.AddScoped<IPatientRepository, PatientRepository>();
            services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
            services.AddScoped<IStaffRepository, StaffRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<IPatientActionTokenRepository, PatientActionTokenRepository>();
            services.AddScoped<ITenantConfirmationPolicyRepository, TenantConfirmationPolicyRepository>();
            services.AddScoped<IAppointmentScheduledActionRepository, AppointmentScheduledActionRepository>();
            services.AddScoped<IRiskSignalRepository, RiskSignalRepository>();
            services.AddScoped<IReceptionTaskRepository, ReceptionTaskRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IMessageInboxStore, EfMessageInboxStore>();

            var store = new InMemoryFakeNotificationStore();
            services.AddSingleton(store);
            services.AddSingleton<IFakeNotificationStore>(store);
            services.AddScoped<INotificationChannel, FakeEmailNotificationChannel>();
            services.AddScoped<INotificationChannel, FakeWhatsAppNotificationChannel>();
            services.AddScoped<INotificationChannelResolver, NotificationChannelResolver>();
            services.AddScoped<INotificationTemplateRenderer, NotificationTemplateRenderer>();
            services.AddScoped<INotificationOutboxService, NotificationOutboxService>();
            services.AddScoped<IPatientActionTokenService, PatientActionTokenService>();
            services.AddScoped<IAppointmentLifecyclePlanner, AppointmentLifecyclePlanner>();
            services.AddScoped<IRiskSignalProcessor, RiskSignalProcessor>();
            services.AddScoped<IOmroAnomalyPublisher, OmroAnomalyPublisher>();
            services.AddScoped<IAppointmentRescoreService, AppointmentRescoreService>();
            services.AddSingleton<IScheduledActionsProcessor, ScheduledActionsProcessor>();
            services.AddSingleton(aiService);

            services.AddScoped<MediatR.ISender, MediatR.Mediator>();
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MedCareOS.Application.AssemblyReference).Assembly));

            services.Configure<ScheduledActionsOptions>(o =>
            {
                o.MaxAttempts = scheduledMaxAttempts;
                o.LeaseDurationSeconds = 120;
                o.BatchSize = 25;
            });

            services.Configure<ScoringOptions>(o => o.PublishAnomalyOnCreate = false);
            services.Configure<NotificationOptions>(o =>
            {
                o.ClinicName = "Clínica MedCare";
                o.TimeZoneId = "America/Santiago";
                o.PublicBaseUrl = "https://app.medcare.cl";
                o.ActionTokenSecret = "test-secret-for-dynamic-risk-signals-32-chars!!";
                o.EmailProvider = "Fake";
                o.WhatsAppProvider = "Fake";
            });

            services.Configure<RabbitMqMessagingOptions>(o =>
            {
                o.RedeliveryMode = "None";
                o.RetryLimit = 1;
                o.RetryMinIntervalMs = 10;
                o.RetryMaxIntervalMs = 30;
                o.RetryIntervalDeltaMs = 10;
            });

            services.AddSingleton<IOperationalAlertSink, LoggingOperationalAlertSink>();
            services.AddSingleton<DeadLetterAlertObserver>();

            services.AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(15), testInactivityTimeout: TimeSpan.FromSeconds(2));
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<ScoreAppointmentConsumer, ScoreAppointmentConsumerDefinition>();
                x.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConnectReceiveObserver(context.GetRequiredService<DeadLetterAlertObserver>());
                    cfg.ConfigureEndpoints(context);
                });
            });

            services.AddScoped<TestOutboxBuffer>();
            services.RemoveAll<IPublishEndpoint>();
            services.AddScoped<IPublishEndpoint>(sp => sp.GetRequiredService<TestOutboxBuffer>());
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork, OutboxFlushingUnitOfWork>();

            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            return new RiskTestHost(provider, harness, store);
        }

        public async Task<(Guid AppointmentId, Guid PatientId)> CreateAppointmentWithPastNoShowAsync(
            DateTimeOffset startUtc,
            string phone = "+56933334444")
        {
            var sentBefore = FakeStore.SentMessages.Count;

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dr", "Rojas", $"{Random.Shared.Next(10000000, 99999999)}-1", "Cardiologia", "Medico", "+56911111111");
            var patient = Patient.Create(
                patUser.Id,
                $"{Random.Shared.Next(10000000, 99999999)}-2",
                "Camila",
                "Diaz",
                new DateOnly(1991, 4, 20),
                "F",
                phone,
                email: $"camila_{Guid.NewGuid():N}@test.cl",
                preferredChannel: NotificationChannel.WhatsApp,
                whatsAppOptIn: true,
                consentSource: "test_seed");

            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");

            // Cita histórica marcada como NoShow para validar previousAbsences=1 en E0-8
            var pastStart = DateTime.UtcNow.AddDays(-7);
            var pastBlock = ScheduleBlock.Create(
                staff.Id, box.Id, pastStart, pastStart.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(pastStart), DateOnly.FromDateTime(pastStart),
                new TimeOnly(9, 0), new TimeOnly(9, 30), [pastStart.DayOfWeek]);
            var pastNoShow = Appointment.Create(pastBlock.Id, patient.Id, "Control previo", "paciente");
            pastNoShow.MarkNoShow();

            var start = startUtc.UtcDateTime;
            var futureBlock = ScheduleBlock.Create(
                staff.Id, box.Id, start, start.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
                new TimeOnly(11, 0), new TimeOnly(11, 30), [start.DayOfWeek]);

            db.Users.AddRange(staffUser, patUser);
            db.Staffs.Add(staff);
            db.Patients.Add(patient);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(pastBlock, futureBlock);
            db.Appointments.Add(pastNoShow);
            await db.SaveChangesAsync();

            var handler = scope.ServiceProvider.GetRequiredService<MediatR.IRequestHandler<CreateAppointmentCommand, Guid>>();
            var appointmentId = await handler.Handle(
                new CreateAppointmentCommand(futureBlock.Id, patient.Id, "Dolor torácico", "paciente"),
                CancellationToken.None);

            await WaitForSentNotificationsAsync(sentBefore + 1);
            return (appointmentId, patient.Id);
        }

        public async Task WaitForSentNotificationsAsync(int expectedTotalSent)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (FakeStore.SentMessages.Count >= expectedTotalSent)
                    return;
                await Task.Delay(25);
            }

            FakeStore.SentMessages.Count.Should().BeGreaterThanOrEqualTo(expectedTotalSent);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await Provider.DisposeAsync();
        }
    }

    private sealed class TestOutboxBuffer : IPublishEndpoint
    {
        private readonly IBus _bus;
        private readonly List<(object Message, Type MessageType)> _pending = [];

        public TestOutboxBuffer(IBus bus)
        {
            _bus = bus;
        }

        public IReadOnlyList<(object Message, Type MessageType)> Drain()
        {
            if (_pending.Count == 0)
                return [];

            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => _bus.ConnectPublishObserver(observer);

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            _pending.Add((message, typeof(T)));
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _pending.Add((message, typeof(T)));
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _pending.Add((message, typeof(T)));
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            _pending.Add((message, message.GetType()));
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            _pending.Add((message, message.GetType()));
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            _pending.Add((message, messageType));
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            _pending.Add((message, messageType));
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        {
            _pending.Add((values, typeof(T)));
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _pending.Add((values, typeof(T)));
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _pending.Add((values, typeof(T)));
            return Task.CompletedTask;
        }
    }

    private sealed class OutboxFlushingUnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TestOutboxBuffer _buffer;
        private readonly IBus _bus;

        public OutboxFlushingUnitOfWork(ApplicationDbContext dbContext, TestOutboxBuffer buffer, IBus bus)
        {
            _dbContext = dbContext;
            _buffer = buffer;
            _bus = bus;
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var result = await _dbContext.SaveChangesAsync(cancellationToken);
            foreach (var (message, messageType) in _buffer.Drain())
            {
                await _bus.Publish(message, messageType, cancellationToken);
            }

            return result;
        }
    }
}
