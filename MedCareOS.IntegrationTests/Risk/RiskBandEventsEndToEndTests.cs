using System.Text.Json;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Appointments.Workers;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Commands.ProcessProviderDeliveryStatus;
using MedCareOS.Application.Notifications.Consumers;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Messaging.Configuration;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Messaging.Observers;
using MedCareOS.Infrastructure.Notifications.Channels;
using MedCareOS.Infrastructure.Notifications.Security;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Omro.Contracts.Events;

namespace MedCareOS.IntegrationTests.Risk;

public class RiskBandEventsEndToEndTests
{
    [Fact]
    public async Task FullLifecycleE2E_SilentT0_FakeChannelR1R2Final_RiskEscalatedOnce_AndConfirmationDeadlineMissedAtT12h()
    {
        var aiCall = 0;
        var aiService = new DelegateAiService(
            onPredict: (req, _) =>
            {
                var n = Interlocked.Increment(ref aiCall);
                // T0 = 0.45 (Bajo), T-48h rescore = 0.62 (Medio) -> con ReminderUnanswered (+0.4) cruza a Alto (~0.7089)
                return Task.FromResult(n == 1
                    ? new PredictResponseDto(req.AppointmentId, 0.45m, "Bajo", "asiste")
                    : new PredictResponseDto(req.AppointmentId, 0.62m, "Medio", "no_asiste"));
            });

        await using var host = await E2ETestHost.CreateAsync(aiService);

        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(5);
        var (appointmentId, patientId) = await host.CreateFullAppointmentAsync(
            startUtc: startUtc,
            firstName: "Valeria",
            lastName: "Paredes",
            email: "valeria.e2e@test.cl",
            phone: "+56977665544",
            symptomsRaw: "Dolor lumbar agudo");

        // 1. T0: scoring silencioso + mensaje informativo por FakeWhatsApp -> ZERO AnomalyDetected
        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();
        await host.WaitForSentMessagesCountAsync(1);
        await Task.Delay(120);

        host.GetPublishedAnomalies(appointmentId).Should().BeEmpty("T0 es silencioso y no debe orquestar");
        host.FakeStore.SentMessages.ElementAt(0).Channel.Should().Be(NotificationChannel.WhatsApp);

        // 2. T-72h: Reminder 1 vía WhatsApp + webhook simulado Delivered -> sigue sin cruzar a Alto
        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        var t72 = startUtc.AddHours(-72).AddMinutes(1);
        (await processor.ProcessDueBatchAsync("e2e-worker", t72)).Completed.Should().Be(1);
        await host.WaitForSentMessagesCountAsync(2);
        await host.MarkLatestNotificationDeliveredAsync(appointmentId, NotificationType.Reminder, t72.AddSeconds(10));

        host.GetPublishedAnomalies(appointmentId).Should().BeEmpty();

        // 3. T-48h: Reminder 2 por canal alterno (Email) + Re-score + ReminderUnanswered -> cruza a Alto => EMITE 1 RiskEscalated
        var t48 = startUtc.AddHours(-48).AddMinutes(1);
        (await processor.ProcessDueBatchAsync("e2e-worker", t48)).Completed.Should().Be(1);
        await host.WaitForSentMessagesCountAsync(3);
        host.FakeStore.SentMessages.ElementAt(2).Channel.Should().Be(NotificationChannel.Email);

        var afterR2 = await host.WaitForPublishedAnomaliesCountAsync(appointmentId, expectedMinCount: 1);
        afterR2.Should().ContainSingle();
        var escalated = afterR2[0];
        escalated.TenantId.Should().Be(RiskThresholds.DefaultTenantId);
        escalated.AnomalyType.Should().Be(RiskThresholds.RiskEscalatedAnomalyType);
        escalated.ResourceId.Should().Be(appointmentId);
        escalated.RiskProbability.Should().BeGreaterThanOrEqualTo(0.70m);
        escalated.Attributes["previous_band"].Should().Be("Bajo");
        escalated.Attributes["risk_band"].Should().Be("Alto");
        escalated.Attributes["especialidad"].Should().Be("traumatologia");
        escalated.Attributes["canal"].Should().Be("whatsapp");
        escalated.Attributes["confirmation_state"].Should().Be(ConfirmationState.Pending.ToString());
        escalated.Attributes["previous_absences"].Should().Be("1");
        decimal.Parse(escalated.Attributes["horas_restantes"], System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(40m);
        int.Parse(escalated.Attributes["edad"], System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(20);

        // Cero PII ni datos clínicos en payload OMRO
        var serializedEscalated = JsonSerializer.Serialize(escalated);
        serializedEscalated.Should().NotContain("Valeria")
            .And.NotContain("Paredes")
            .And.NotContain("valeria.e2e")
            .And.NotContain("56977665544")
            .And.NotContain("Dolor lumbar agudo");

        // Marcar R2 (Email) como Delivered
        await host.MarkLatestNotificationDeliveredAsync(appointmentId, NotificationType.Reminder, t48.AddSeconds(10));

        // 4. T-24h: Confirmación Final -> aunque aplica +0.4 de ReminderUnanswered R2, ya estaba en Alto => 0 RiskEscalated duplicado
        var t24 = startUtc.AddHours(-24).AddMinutes(1);
        (await processor.ProcessDueBatchAsync("e2e-worker", t24)).Completed.Should().Be(1);
        await host.WaitForSentMessagesCountAsync(4);
        await host.MarkLatestNotificationDeliveredAsync(appointmentId, NotificationType.FinalConfirmation, t24.AddSeconds(10));
        await Task.Delay(120);

        host.GetPublishedAnomalies(appointmentId).Should().HaveCount(1, "ya estaba en banda Alto; no debe re-emitir RiskEscalated");

        // 5. T-12h: Deadline sin confirmar -> transición a Unresponsive + EMITE 1 ConfirmationDeadlineMissed
        var t12 = startUtc.AddHours(-12).AddMinutes(1);
        (await processor.ProcessDueBatchAsync("e2e-worker", t12)).Completed.Should().Be(1);

        var finalEvents = await host.WaitForPublishedAnomaliesCountAsync(appointmentId, expectedMinCount: 2);
        finalEvents.Should().HaveCount(2);

        var deadlineEvt = finalEvents[1];
        deadlineEvt.AnomalyType.Should().Be(RiskThresholds.ConfirmationDeadlineMissedAnomalyType);
        deadlineEvt.ResourceId.Should().Be(appointmentId);
        deadlineEvt.Attributes["confirmation_state"].Should().Be(ConfirmationState.Unresponsive.ToString());
        deadlineEvt.Attributes["risk_band"].Should().Be("Alto");
        JsonSerializer.Serialize(deadlineEvt).Should().NotContain("Valeria").And.NotContain("Dolor lumbar agudo");

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            saved.Status.Should().Be(AppointmentStatus.Scheduled);
            saved.ConfirmationState.Should().Be(ConfirmationState.Unresponsive);
            saved.DeadlineMissedAt.Should().Be(t12);
            saved.PatientId.Should().Be(patientId);
        }
    }

    [Fact]
    public async Task FullLifecycleE2E_HighRiskAtT0_PatientConfirmsEarlyViaFakeChannel_ZeroOmroEvents()
    {
        var aiService = new DelegateAiService(
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.85m, "Alto", "no_asiste")));

        await using var host = await E2ETestHost.CreateAsync(aiService);

        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(5);
        var (appointmentId, patientId) = await host.CreateFullAppointmentAsync(startUtc);

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();
        await host.WaitForSentMessagesCountAsync(1); // T0 informativo

        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        var t72 = startUtc.AddHours(-72).AddMinutes(1);
        (await processor.ProcessDueBatchAsync("e2e-worker", t72)).Completed.Should().Be(1);
        await host.WaitForSentMessagesCountAsync(2); // R1 vía FakeWhatsApp

        // Paciente confirma vía token/botón en R1
        using (var scope = host.Provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var confirmResult = await sender.Send(new ConfirmAppointmentCommand(
                AppointmentId: appointmentId,
                ExpectedPatientId: patientId,
                Channel: NotificationChannel.WhatsApp,
                Source: "fake_whatsapp_reply",
                OccurredAtUtc: t72.AddHours(1),
                PublishPatientReplyEvent: true));

            confirmResult.ConfirmationState.Should().Be(ConfirmationState.ConfirmedEarly);
            confirmResult.CancelledScheduledActionsCount.Should().Be(3); // R2, Final, Deadline
        }

        // Avanzar reloj pasando T-48h, T-24h, T-12h -> 0 acciones ejecutadas, 0 eventos OMRO
        (await processor.ProcessDueBatchAsync("e2e-worker", startUtc.AddHours(-10))).Claimed.Should().Be(0);
        await Task.Delay(120);

        host.GetPublishedAnomalies(appointmentId).Should().BeEmpty();
    }

    [Fact]
    public async Task RiskEscalated_TriggersOnDeliveryFailure_PreventsHysteresisFlapping_AndSuppressesAfterConfirmation()
    {
        var aiService = new DelegateAiService(
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.58m, "Medio", "no_asiste")));

        await using var host = await E2ETestHost.CreateAsync(aiService);

        var now = DateTimeOffset.UtcNow;
        var startUtc = now.AddDays(5);
        var (appointmentId, patientId) = await host.CreateFullAppointmentAsync(startUtc);

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();
        await host.WaitForSentMessagesCountAsync(1);

        // Sembrar notificación Sent para simular webhook DeliveryFailed
        var providerMsgId = $"re_fail_{Guid.NewGuid():N}";
        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notif = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.Reminder,
                NotificationChannel.Email,
                "appointment.reminder",
                1,
                "es-CL",
                "{}",
                now,
                Guid.NewGuid(),
                now);
            notif.MarkSent("Resend", providerMsgId, "Recordatorio...", now);
            db.Notifications.Add(notif);
            await db.SaveChangesAsync();
        }

        // 1. Webhook DeliveryFailed (+0.8 logit): 0.58 -> ~0.7545 (Medio -> Alto) => EMITE 1 RiskEscalated + ReceptionTask
        using (var scope = host.Provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ProcessProviderDeliveryStatusCommand(
                Provider: "Resend",
                EventId: $"evt_{Guid.NewGuid():N}",
                ProviderMessageId: providerMsgId,
                Status: NotificationStatus.Failed,
                EventTimestampUtc: now.AddMinutes(5),
                Error: "Hard bounce 550"));
        }

        var events1 = await host.WaitForPublishedAnomaliesCountAsync(appointmentId, expectedMinCount: 1);
        events1.Should().ContainSingle();
        events1[0].AnomalyType.Should().Be(RiskThresholds.RiskEscalatedAnomalyType);
        events1[0].Attributes["previous_band"].Should().Be("Medio");
        events1[0].Attributes["risk_band"].Should().Be("Alto");

        // 2. Oscilación con histéresis: señal negativa (-0.6 -> ~0.6277 >= 0.55) mantiene High, luego positiva (+0.5 -> ~0.7354) -> NO debe re-emitir
        using (var scope = host.Provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IRiskSignalProcessor>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await processor.RecordAsync(new RecordRiskSignalRequest(
                appointmentId,
                RiskSignalKind.ConfirmedEarly,
                "calibration_probe_down",
                $"probe_down:{Guid.NewGuid():N}",
                Guid.NewGuid(),
                now.AddMinutes(10),
                WeightOverride: -0.60m));

            await processor.RecordAsync(new RecordRiskSignalRequest(
                appointmentId,
                RiskSignalKind.DeliveryFailed,
                "calibration_probe_up",
                $"probe_up:{Guid.NewGuid():N}",
                Guid.NewGuid(),
                now.AddMinutes(15),
                WeightOverride: 0.50m));

            await uow.SaveChangesAsync(CancellationToken.None);
        }

        await Task.Delay(120);
        host.GetPublishedAnomalies(appointmentId).Should().HaveCount(1, "histéresis mantiene Alto sin flapping ni re-emisiones");

        // 3. Paciente confirma -> OrchestrationSuppressed = true; nueva señal DeliveryFailed tardía NO debe emitir
        using (var scope = host.Provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ConfirmAppointmentCommand(appointmentId, patientId, OccurredAtUtc: now.AddMinutes(20)));
        }

        using (var scope = host.Provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IRiskSignalProcessor>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await processor.RecordAsync(new RecordRiskSignalRequest(
                appointmentId,
                RiskSignalKind.DeliveryFailed,
                "late_webhook",
                $"late_fail:{Guid.NewGuid():N}",
                Guid.NewGuid(),
                now.AddMinutes(25)));
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        await Task.Delay(120);
        host.GetPublishedAnomalies(appointmentId).Should().HaveCount(1);
    }

    [Fact]
    public async Task Reminder2AndRescore_WhenUnansweredWouldCrossHighButFreshRescoreStaysMedium_EmitsZeroFalseRiskEscalated()
    {
        var aiCall = 0;
        var aiService = new DelegateAiService(
            onPredict: (req, _) =>
            {
                var n = Interlocked.Increment(ref aiCall);
                // T0 = 0.65 (Medio); con +0.4 del R1 sin respuesta subiría temporalmente a ~0.7348 (Alto),
                // pero el re-score T-48h fresco devuelve 0.50 => efectivo final ~0.5987 (<0.70), manteniéndose en Medio.
                return Task.FromResult(n == 1
                    ? new PredictResponseDto(req.AppointmentId, 0.65m, "Medio", "no_asiste")
                    : new PredictResponseDto(req.AppointmentId, 0.50m, "Medio", "asiste"));
            });

        await using var host = await E2ETestHost.CreateAsync(aiService);

        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(5);
        var (appointmentId, _) = await host.CreateFullAppointmentAsync(startUtc);

        var scoreConsumer = host.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await scoreConsumer.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();
        await host.WaitForSentMessagesCountAsync(1);

        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        var t72 = startUtc.AddHours(-72).AddMinutes(1);
        await processor.ProcessDueBatchAsync("e2e-worker", t72);
        await host.WaitForSentMessagesCountAsync(2);
        await host.MarkLatestNotificationDeliveredAsync(appointmentId, NotificationType.Reminder, t72.AddSeconds(10));

        var t48 = startUtc.AddHours(-48).AddMinutes(1);
        (await processor.ProcessDueBatchAsync("e2e-worker", t48)).Completed.Should().Be(1);
        await host.WaitForSentMessagesCountAsync(3);
        await Task.Delay(120);

        host.GetPublishedAnomalies(appointmentId).Should().BeEmpty("el re-score fresco T-48h devolvió la cita a banda Media sin cruzar High");

        using var scope = host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
        app.EffectiveBand.Should().Be(RiskBandLevel.Medium);
        app.EffectiveRiskScore.Should().BeLessThan(0.70m);
    }

    private sealed class DelegateAiService : IAiClinicalService
    {
        private readonly Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> _onPredict;

        public DelegateAiService(Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> onPredict)
        {
            _onPredict = onPredict;
        }

        public Task<NlpSummaryResponseDto> GetNlpSummaryAsync(Guid appointmentId, string symptomsRaw, CancellationToken ctx)
            => Task.FromResult(new NlpSummaryResponseDto(symptomsRaw, [], "baja"));

        public Task<PredictResponseDto> PredictNoShowRiskAsync(PredictRequestDto request, CancellationToken ctx)
            => _onPredict(request, ctx);
    }

    private sealed class E2ETestHost : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public ITestHarness Harness { get; }
        public InMemoryFakeNotificationStore FakeStore { get; }

        private E2ETestHost(ServiceProvider provider, ITestHarness harness, InMemoryFakeNotificationStore fakeStore)
        {
            Provider = provider;
            Harness = harness;
            FakeStore = fakeStore;
        }

        public static async Task<E2ETestHost> CreateAsync(IAiClinicalService aiService)
        {
            var services = new ServiceCollection();
            var dbName = $"RiskBandEventsE2E_{Guid.NewGuid()}";
            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<ApplicationDbContext>(o =>
                o.UseInMemoryDatabase(dbName).UseInternalServiceProvider(inMemoryProvider));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<IAppointmentRepository, AppointmentRepository>();
            services.AddScoped<IPatientRepository, PatientRepository>();
            services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
            services.AddScoped<IBoxRepository, BoxRepository>();
            services.AddScoped<IStaffRepository, StaffRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<IPatientActionTokenRepository, PatientActionTokenRepository>();
            services.AddScoped<ITenantConfirmationPolicyRepository, TenantConfirmationPolicyRepository>();
            services.AddScoped<IAppointmentScheduledActionRepository, AppointmentScheduledActionRepository>();
            services.AddScoped<IRiskSignalRepository, RiskSignalRepository>();
            services.AddScoped<IReceptionTaskRepository, ReceptionTaskRepository>();
            services.AddScoped<IAiResolutionTaskRepository, AiResolutionTaskRepository>();
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
            services.AddScoped<IAiResolutionSupersedingService, AiResolutionSupersedingService>();
            services.AddScoped<IOmroAnomalyPublisher, OmroAnomalyPublisher>();
            services.AddScoped<IRiskSignalProcessor, RiskSignalProcessor>();
            services.AddScoped<IAppointmentRescoreService, AppointmentRescoreService>();
            services.AddSingleton<IScheduledActionsProcessor, ScheduledActionsProcessor>();
            services.AddSingleton(aiService);

            services.AddScoped<ISender, Mediator>();
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MedCareOS.Application.AssemblyReference).Assembly));

            services.Configure<ScheduledActionsOptions>(o =>
            {
                o.MaxAttempts = 3;
                o.LeaseDurationSeconds = 120;
                o.BatchSize = 25;
            });

            services.Configure<ScoringOptions>(o => o.PublishAnomalyOnCreate = false);
            services.Configure<NotificationOptions>(o =>
            {
                o.ClinicName = "Clínica MedCare";
                o.TimeZoneId = "America/Santiago";
                o.PublicBaseUrl = "https://app.medcare.cl";
                o.ActionTokenSecret = "test-secret-for-risk-band-events-e2e-32-chars!";
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

            return new E2ETestHost(provider, harness, store);
        }

        public async Task<(Guid AppointmentId, Guid PatientId)> CreateFullAppointmentAsync(
            DateTimeOffset startUtc,
            string firstName = "Sofia",
            string lastName = "Mendez",
            string email = "sofia.e2e@test.cl",
            string phone = "+56955443322",
            string symptomsRaw = "Control general")
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dra", "Navarro", $"{Random.Shared.Next(10000000, 99999999)}-1", "Traumatologia", "Medico", "+56911111111");
            var patient = Patient.Create(
                patUser.Id,
                $"{Random.Shared.Next(10000000, 99999999)}-2",
                firstName,
                lastName,
                new DateOnly(1990, 5, 10),
                "F",
                phone,
                email: email,
                preferredChannel: NotificationChannel.WhatsApp,
                whatsAppOptIn: true,
                consentSource: "test_seed");

            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");

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

            var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<CreateAppointmentCommand, Guid>>();
            var appointmentId = await handler.Handle(
                new CreateAppointmentCommand(futureBlock.Id, patient.Id, symptomsRaw, "paciente"),
                CancellationToken.None);

            return (appointmentId, patient.Id);
        }

        public async Task MarkLatestNotificationDeliveredAsync(
            Guid appointmentId,
            NotificationType type,
            DateTimeOffset deliveredAtUtc)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notif = await db.Notifications
                .Where(n => n.AppointmentId == appointmentId && n.Type == type)
                .OrderByDescending(n => n.CreatedAt)
                .FirstAsync();
            notif.ApplyDeliveryStatus(NotificationStatus.Delivered, deliveredAtUtc);
            await db.SaveChangesAsync();
        }

        public IReadOnlyList<AnomalyDetected> GetPublishedAnomalies(Guid appointmentId)
            => Harness.Published.Select<AnomalyDetected>(p => p.Context.Message.ResourceId == appointmentId)
                .Select(p => p.Context.Message)
                .ToList();

        public async Task<IReadOnlyList<AnomalyDetected>> WaitForPublishedAnomaliesCountAsync(
            Guid appointmentId,
            int expectedMinCount)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var current = GetPublishedAnomalies(appointmentId);
                if (current.Count >= expectedMinCount)
                    return current;
                await Task.Delay(25);
            }

            var final = GetPublishedAnomalies(appointmentId);
            final.Count.Should().BeGreaterThanOrEqualTo(expectedMinCount);
            return final;
        }

        public async Task WaitForSentMessagesCountAsync(int expectedTotalSent)
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
