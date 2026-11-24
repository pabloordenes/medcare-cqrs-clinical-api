using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MedCareOS.Api.BackgroundServices;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Consumers;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedCareOS.IntegrationTests.Messaging;

public class SendNotificationOutboxAndConsumerTests
{
    [Fact]
    public async Task SendNotificationConsumer_HappyPathWhatsApp_ShouldSendAndStoreSanitizedContentWithoutRawToken()
    {
        await using var host = await NotificationTestHost.CreateAsync();
        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            email: "ana@test.cl");

        var notificationId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.WhatsApp);

        await host.Harness.Bus.Publish(new SendNotificationRequested(notificationId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var consumerHarness = host.Harness.GetConsumerHarness<SendNotificationConsumer>();
        (await consumerHarness.Consumed.Any<SendNotificationRequested>(c => c.Context.Message.NotificationId == notificationId))
            .Should().BeTrue();

        host.Store.SentMessages.Should().ContainSingle(m => m.NotificationId == notificationId && m.Channel == NotificationChannel.WhatsApp);

        using var verifyScope = host.Provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);

        saved.Status.Should().Be(NotificationStatus.Sent);
        saved.Channel.Should().Be(NotificationChannel.WhatsApp);
        saved.OriginalChannel.Should().BeNull();
        saved.ProviderName.Should().Be("Fake");
        saved.ProviderMessageId.Should().StartWith("fake_whatsapp_");
        saved.SentAt.Should().NotBeNull();
        saved.Content.Should().NotBeNullOrWhiteSpace();
        saved.Content.Should().NotContain("?token=");
    }

    [Fact]
    public async Task SendNotificationConsumer_WhenWhatsAppFailsPermanently_ShouldFallbackImmediatelyToEmail()
    {
        await using var host = await NotificationTestHost.CreateAsync();
        host.Store.SetOverrideHandler(msg =>
        {
            if (msg.Channel == NotificationChannel.WhatsApp)
                return DeliveryResult.Failure("Fake", DeliveryFailureKind.InvalidRecipient, "131026 Undeliverable", 400, "131026");

            return DeliveryResult.Success($"fake_email_{Guid.NewGuid():N}", "Fake");
        });

        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            email: "fallback@test.cl");

        var notificationId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.WhatsApp);
        await host.Harness.Bus.Publish(new SendNotificationRequested(notificationId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var consumerHarness = host.Harness.GetConsumerHarness<SendNotificationConsumer>();
        (await consumerHarness.Consumed.Any<SendNotificationRequested>(c => c.Context.Message.NotificationId == notificationId))
            .Should().BeTrue();

        host.Store.SentMessages.Should().ContainSingle(m => m.NotificationId == notificationId && m.Channel == NotificationChannel.Email);
        var sentEmail = host.Store.SentMessages.Single();
        sentEmail.Rendered.PlainTextBody.Should().Contain("?token=");

        using var scope = host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);

        saved.Status.Should().Be(NotificationStatus.Sent);
        saved.OriginalChannel.Should().Be(NotificationChannel.WhatsApp);
        saved.Channel.Should().Be(NotificationChannel.Email);
        saved.ProviderMessageId.Should().StartWith("fake_email_");
        saved.Content.Should().Contain("[ACTION_LINK_REDACTED]");
    }

    [Fact]
    public async Task SendNotificationConsumer_WhenPatientLacksWhatsAppOptInAtSendTime_ShouldFallbackToEmail()
    {
        await using var host = await NotificationTestHost.CreateAsync();
        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            email: "optin_revoked@test.cl");

        var notificationId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.WhatsApp);

        // Revoke WhatsApp opt-in after enqueue but before dispatch (stays Email-reachable)
        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var patient = await db.Patients.FirstAsync(p => p.Id == patientId);
            patient.UpdateNotificationPreferences(NotificationChannel.Email, whatsAppOptIn: false, "portal", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await host.Harness.Bus.Publish(new SendNotificationRequested(notificationId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var consumerHarness = host.Harness.GetConsumerHarness<SendNotificationConsumer>();
        (await consumerHarness.Consumed.Any<SendNotificationRequested>(c => c.Context.Message.NotificationId == notificationId))
            .Should().BeTrue();

        using var verifyScope = host.Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await verifyDb.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);

        saved.Status.Should().Be(NotificationStatus.Sent);
        saved.OriginalChannel.Should().Be(NotificationChannel.WhatsApp);
        saved.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task SendNotificationConsumer_WhenPatientOptedOutBeforeDispatch_ShouldCancelAndNeverCallProvider()
    {
        await using var host = await NotificationTestHost.CreateAsync();
        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.Email,
            whatsAppOptIn: false,
            email: "optout@test.cl");

        var notificationId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.Email);

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var patient = await db.Patients.FirstAsync(p => p.Id == patientId);
            patient.OptOut("patient_portal", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await host.Harness.Bus.Publish(new SendNotificationRequested(notificationId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var consumerHarness = host.Harness.GetConsumerHarness<SendNotificationConsumer>();
        (await consumerHarness.Consumed.Any<SendNotificationRequested>(c => c.Context.Message.NotificationId == notificationId))
            .Should().BeTrue();

        host.Store.SentMessages.Should().BeEmpty();

        using var verifyScope = host.Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await verifyDb.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);
        saved.Status.Should().Be(NotificationStatus.Cancelled);
    }

    [Fact]
    public async Task SendNotificationConsumer_WhenWhatsAppFailsTransientlyTwice_ShouldFallbackToEmailOnThirdAttempt()
    {
        int waAttempts = 0;
        await using var host = await NotificationTestHost.CreateAsync(retryLimit: 3, maxPrimaryChannelAttempts: 2);
        host.Store.SetOverrideHandler(msg =>
        {
            if (msg.Channel == NotificationChannel.WhatsApp)
            {
                Interlocked.Increment(ref waAttempts);
                return DeliveryResult.Failure("Fake", DeliveryFailureKind.Transient, "Meta 503", 503);
            }

            return DeliveryResult.Success($"fake_email_{Guid.NewGuid():N}", "Fake");
        });

        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            email: "transient_fallback@test.cl");

        var notificationId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.WhatsApp);
        await host.Harness.Bus.Publish(new SendNotificationRequested(notificationId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        Notification? saved = null;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = host.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            saved = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);
            if (saved.Status == NotificationStatus.Sent)
                break;
            await Task.Delay(30);
        }

        waAttempts.Should().Be(2);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(NotificationStatus.Sent);
        saved.OriginalChannel.Should().Be(NotificationChannel.WhatsApp);
        saved.Channel.Should().Be(NotificationChannel.Email);
        saved.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task SendNotificationConsumer_WhenEmailFailsTransientlyBeyondRetries_ShouldMoveToDlqAndAlert()
    {
        await using var host = await NotificationTestHost.CreateAsync(retryLimit: 1);
        host.Store.SetOverrideHandler(_ =>
            DeliveryResult.Failure("Fake", DeliveryFailureKind.Transient, "SMTP/HTTP 503", 503));

        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.Email,
            whatsAppOptIn: false,
            email: "dlq@test.cl");

        var notificationId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.Email);
        await host.Harness.Bus.Publish(new SendNotificationRequested(notificationId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var faulted = await host.Harness.Consumed.SelectAsync<SendNotificationRequested>(
            c => c.Context.Message.NotificationId == notificationId && c.Exception != null).FirstOrDefaultAsync();
        faulted.Should().NotBeNull();

        host.AlertSink.Alerts.Should().Contain(a =>
            a.ExceptionType == nameof(NotificationTransientDeliveryException) &&
            a.MessageType == nameof(SendNotificationRequested));
    }

    [Fact]
    public async Task OutboxAndInbox_ShouldDeduplicateSameNotificationKeyAndSameMessageId()
    {
        await using var host = await NotificationTestHost.CreateAsync();
        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.Email,
            whatsAppOptIn: false,
            email: "dedupe@test.cl");

        var scheduledFor = DateTimeOffset.UtcNow;
        Guid notificationId;

        using (var scope = host.Provider.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxService>();
            var patientRepo = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var patient = (await patientRepo.GetPatientByIdAsync(patientId))!;

            var req = new EnqueueNotificationRequest(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patient,
                NotificationType.AppointmentCreated,
                DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
                "es-CL",
                scheduledFor,
                new Dictionary<string, string>
                {
                    ["patientFirstName"] = patient.FirstName,
                    ["clinicName"] = "Clínica MedCare",
                    ["appointmentDate"] = "26/11/2026",
                    ["appointmentTime"] = "12:00 hrs",
                    ["specialtyDisplay"] = "Medicina General"
                },
                Guid.NewGuid());

            var first = await outbox.EnqueueAsync(req);
            await uow.SaveChangesAsync();

            var second = await outbox.EnqueueAsync(req);

            first.Outcome.Should().Be(NotificationEnqueueOutcome.Enqueued);
            second.Outcome.Should().Be(NotificationEnqueueOutcome.DuplicateSkipped);
            notificationId = first.NotificationId!.Value;
        }

        // Duplicate MessageId delivery through MassTransit inbox filter
        var pendingForInboxTestId = await host.SeedPendingReminderInDbAsync(appointmentId, patientId, NotificationChannel.Email);
        var messageId = Guid.NewGuid();
        await host.Harness.Bus.Publish(
            new SendNotificationRequested(pendingForInboxTestId, Guid.NewGuid(), DateTimeOffset.UtcNow),
            p => p.MessageId = messageId);
        await host.Harness.Bus.Publish(
            new SendNotificationRequested(pendingForInboxTestId, Guid.NewGuid(), DateTimeOffset.UtcNow),
            p => p.MessageId = messageId);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
               !host.Store.SentMessages.Any(m => m.NotificationId == pendingForInboxTestId))
        {
            await Task.Delay(30);
        }

        await Task.Delay(150);
        host.Store.SentMessages.Count(m => m.NotificationId == pendingForInboxTestId).Should().Be(1);

        using var verifyScope = host.Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inboxRow = await verifyDb.ProcessedMessages.AsNoTracking().SingleAsync(m => m.MessageId == messageId);
        inboxRow.Status.Should().Be(ProcessedMessage.StatusCompleted);
    }

    [Fact]
    public async Task NotificationRetentionJob_ShouldRedactExpiredContentWhilePreservingAuditFields()
    {
        await using var host = await NotificationTestHost.CreateAsync(contentRetentionDays: 30);
        var (appointmentId, patientId) = await host.SeedAppointmentAsync(
            preferredChannel: NotificationChannel.Email,
            whatsAppOptIn: false,
            email: "retention@test.cl");

        Guid oldId;
        Guid recentId;

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var oldCreated = DateTimeOffset.UtcNow.AddDays(-35);
            var recentCreated = DateTimeOffset.UtcNow.AddDays(-2);

            var oldNotification = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.AppointmentCreated,
                NotificationChannel.Email,
                "appointment.created",
                1,
                "es-CL",
                "{\"patientFirstName\":\"Ana\"}",
                oldCreated,
                Guid.NewGuid(),
                oldCreated);
            oldNotification.MarkSent("Resend", "re_old", "Hola Ana, cita agendada...", oldCreated);

            var recentNotification = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.Reminder,
                NotificationChannel.Email,
                "appointment.reminder",
                1,
                "es-CL",
                "{\"patientFirstName\":\"Ana\"}",
                recentCreated,
                Guid.NewGuid(),
                recentCreated);
            recentNotification.MarkSent("Resend", "re_recent", "Recordatorio cita...", recentCreated);

            db.Notifications.AddRange(oldNotification, recentNotification);
            await db.SaveChangesAsync();

            oldId = oldNotification.Id;
            recentId = recentNotification.Id;
        }

        var job = new NotificationRetentionBackgroundService(
            host.Provider.GetRequiredService<IServiceScopeFactory>(),
            host.Provider.GetRequiredService<IOptions<NotificationOptions>>(),
            NullLogger<NotificationRetentionBackgroundService>.Instance);

        var redactedCount = await job.RedactExpiredNotificationContentsAsync(CancellationToken.None);
        redactedCount.Should().Be(1);

        using var verifyScope = host.Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldSaved = await verifyDb.Notifications.AsNoTracking().FirstAsync(n => n.Id == oldId);
        var recentSaved = await verifyDb.Notifications.AsNoTracking().FirstAsync(n => n.Id == recentId);

        oldSaved.Content.Should().Be(Notification.RedactedPlaceholder);
        oldSaved.TemplateParamsJson.Should().Be("{}");
        oldSaved.ContentRedactedAt.Should().NotBeNull();
        oldSaved.ProviderMessageId.Should().Be("re_old");

        recentSaved.Content.Should().Be("Recordatorio cita...");
        recentSaved.ContentRedactedAt.Should().BeNull();
    }

    private sealed class RecordingOperationalAlertSink : IOperationalAlertSink
    {
        public ConcurrentBag<DeadLetterAlert> Alerts { get; } = new();

        public Task NotifyDeadLetterAsync(DeadLetterAlert alert, CancellationToken cancellationToken = default)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class NotificationTestHost : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public ITestHarness Harness { get; }
        public InMemoryFakeNotificationStore Store { get; }
        public RecordingOperationalAlertSink AlertSink { get; }

        private NotificationTestHost(
            ServiceProvider provider,
            ITestHarness harness,
            InMemoryFakeNotificationStore store,
            RecordingOperationalAlertSink alertSink)
        {
            Provider = provider;
            Harness = harness;
            Store = store;
            AlertSink = alertSink;
        }

        public static async Task<NotificationTestHost> CreateAsync(
            int retryLimit = 2,
            int maxPrimaryChannelAttempts = 3,
            int contentRetentionDays = 30)
        {
            var services = new ServiceCollection();
            var dbName = $"SendNotificationHarnessDb_{Guid.NewGuid()}";
            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(dbName).UseInternalServiceProvider(inMemoryProvider));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<IAppointmentRepository, AppointmentRepository>();
            services.AddScoped<IPatientRepository, PatientRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<IPatientActionTokenRepository, PatientActionTokenRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IMessageInboxStore, EfMessageInboxStore>();

            var store = new InMemoryFakeNotificationStore();
            var alertSink = new RecordingOperationalAlertSink();

            services.AddSingleton(store);
            services.AddSingleton<IFakeNotificationStore>(store);
            services.AddScoped<INotificationChannel, FakeEmailNotificationChannel>();
            services.AddScoped<INotificationChannel, FakeWhatsAppNotificationChannel>();
            services.AddScoped<INotificationChannelResolver, NotificationChannelResolver>();
            services.AddScoped<INotificationTemplateRenderer, NotificationTemplateRenderer>();
            services.AddScoped<INotificationOutboxService, NotificationOutboxService>();
            services.AddScoped<IPatientActionTokenService, PatientActionTokenService>();

            services.Configure<NotificationOptions>(o =>
            {
                o.ClinicName = "Clínica MedCare";
                o.TimeZoneId = "America/Santiago";
                o.PublicBaseUrl = "https://app.medcare.cl";
                o.ActionTokenSecret = "test-secret-for-send-notification-consumer-32-chars!";
                o.MaxPrimaryChannelAttempts = maxPrimaryChannelAttempts;
                o.ContentRetentionDays = contentRetentionDays;
                o.EmailProvider = "Fake";
                o.WhatsAppProvider = "Fake";
            });

            services.Configure<RabbitMqMessagingOptions>(o =>
            {
                o.RedeliveryMode = "None";
                o.RetryLimit = retryLimit;
                o.RetryMinIntervalMs = 15;
                o.RetryMaxIntervalMs = 40;
                o.RetryIntervalDeltaMs = 15;
            });

            services.AddSingleton<IOperationalAlertSink>(alertSink);
            services.AddSingleton<DeadLetterAlertObserver>();

            services.AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(15), testInactivityTimeout: TimeSpan.FromSeconds(2));
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConnectReceiveObserver(context.GetRequiredService<DeadLetterAlertObserver>());
                    cfg.ConfigureEndpoints(context);
                });
            });

            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            return new NotificationTestHost(provider, harness, store, alertSink);
        }

        public async Task<(Guid AppointmentId, Guid PatientId)> SeedAppointmentAsync(
            NotificationChannel preferredChannel,
            bool whatsAppOptIn,
            string? email)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patientUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dra", "Soto", $"{Random.Shared.Next(10000000, 99999999)}-1", "Medicina General", "Medico", "+56911111111");
            var patient = Patient.Create(
                patientUser.Id,
                $"{Random.Shared.Next(10000000, 99999999)}-2",
                "Ana",
                "Lopez",
                new DateOnly(1992, 3, 15),
                "femenino",
                "+56922222222",
                email: email,
                preferredChannel: preferredChannel,
                whatsAppOptIn: whatsAppOptIn,
                consentSource: "test_seed");
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
            var start = DateTime.UtcNow.AddDays(2);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, start, start.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
                new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
            var appointment = Appointment.Create(block.Id, patient.Id, "Control", "paciente");

            db.Users.AddRange(staffUser, patientUser);
            db.Staffs.Add(staff);
            db.Patients.Add(patient);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();

            return (appointment.Id, patient.Id);
        }

        public async Task<Guid> SeedPendingReminderInDbAsync(Guid appointmentId, Guid patientId, NotificationChannel channel)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId);
            var now = DateTimeOffset.UtcNow;

            var paramsJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["patientFirstName"] = patient.FirstName,
                ["clinicName"] = "Clínica MedCare",
                ["appointmentDate"] = "26/11/2026",
                ["appointmentTime"] = "10:00 hrs",
                ["specialtyDisplay"] = "Medicina General"
            });

            var notification = Notification.Create(
                RiskThresholds.DefaultTenantId,
                appointmentId,
                patientId,
                NotificationType.Reminder,
                channel,
                DefaultNotificationTemplateCatalog.Keys.Reminder,
                1,
                "es-CL",
                paramsJson,
                now,
                Guid.NewGuid(),
                now);

            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            return notification.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await Provider.DisposeAsync();
        }
    }
}
