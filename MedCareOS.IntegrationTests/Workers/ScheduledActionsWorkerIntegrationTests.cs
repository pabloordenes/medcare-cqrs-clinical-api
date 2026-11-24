using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Commands.CancelAppointment;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Appointments.Workers;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Interfaces;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedCareOS.IntegrationTests.Workers;

public class ScheduledActionsWorkerIntegrationTests
{
    [Fact]
    public void ApplicationDbContext_ShouldMapScheduledActionsTableConcurrencyTokenAndPartialIndexes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=medcare_design;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var entity = model.FindEntityType(typeof(AppointmentScheduledAction));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("appointment_scheduled_actions");

        var lockToken = entity.FindProperty(nameof(AppointmentScheduledAction.LockToken))!;
        lockToken.GetColumnName().Should().Be("lock_token");
        lockToken.IsConcurrencyToken.Should().BeTrue();

        var dueIndex = entity.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == "IX_scheduled_actions_due_claim");
        dueIndex.Should().NotBeNull();
        dueIndex!.GetFilter().Should().Be("status = 'Pending'");

        var leaseIndex = entity.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == "IX_scheduled_actions_expired_lease");
        leaseIndex.Should().NotBeNull();
        leaseIndex!.GetFilter().Should().Be("status = 'Processing'");

        var activeUniqueIndex = entity.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == "UX_scheduled_actions_active_appointment_kind");
        activeUniqueIndex.Should().NotBeNull();
        activeUniqueIndex!.IsUnique.Should().BeTrue();
        activeUniqueIndex.GetFilter().Should().Be("status IN ('Pending', 'Processing')");

        var appStatusIndex = entity.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == "IX_scheduled_actions_appointment_status");
        appStatusIndex.Should().NotBeNull();
    }

    [Fact]
    public async Task FullConfirmationTimeline_ExecutesReminder1WhatsApp_Reminder2AlternateEmail_FinalConfirmation_AndDeadline()
    {
        await using var host = await WorkerTestHost.CreateAsync();
        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(5); // 120h -> 4 acciones

        var (appointmentId, _) = await host.CreateAppointmentViaHandlerAsync(
            startUtc,
            preferredChannel: NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            email: "paciente.dual@test.cl");

        List<AppointmentScheduledAction> planned;
        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            planned = await db.AppointmentScheduledActions
                .AsNoTracking()
                .Where(a => a.AppointmentId == appointmentId)
                .OrderBy(a => a.DueAt)
                .ToListAsync();
        }

        planned.Should().HaveCount(4);
        planned.Select(p => p.Kind).Should().ContainInOrder(
            ScheduledActionKind.Reminder1,
            ScheduledActionKind.Reminder2AndRescore,
            ScheduledActionKind.FinalConfirmation,
            ScheduledActionKind.ConfirmationDeadline);

        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();

        // 1. Vence T-72h (Reminder 1 -> WhatsApp)
        var r1Batch = await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-72).AddMinutes(1));
        r1Batch.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));
        await host.WaitForConsumedNotificationsAsync(2); // T0 + R1

        // 2. Vence T-48h (Reminder 2 -> canal alternativo Email)
        var r2Batch = await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-48).AddMinutes(1));
        r2Batch.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));
        await host.WaitForConsumedNotificationsAsync(3); // T0 + R1 + R2

        // 3. Vence T-24h (FinalConfirmation -> ConfirmationState = FinalRequested)
        var fcBatch = await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-24).AddMinutes(1));
        fcBatch.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));
        await host.WaitForConsumedNotificationsAsync(4); // T0 + R1 + R2 + FC

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.Status.Should().Be(AppointmentStatus.Scheduled);
            app.ConfirmationState.Should().Be(ConfirmationState.FinalRequested);
            app.FinalConfirmationRequestedAt.Should().NotBeNull();
        }

        // 4. Vence T-12h (ConfirmationDeadline -> ConfirmationState = Unresponsive)
        var dlBatch = await processor.ProcessDueBatchAsync("worker-1", startUtc.AddHours(-12).AddMinutes(1));
        dlBatch.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            app.Status.Should().Be(AppointmentStatus.Scheduled);
            app.ConfirmationState.Should().Be(ConfirmationState.Unresponsive);
            app.DeadlineMissedAt.Should().NotBeNull();

            var notifs = await db.Notifications
                .AsNoTracking()
                .Where(n => n.AppointmentId == appointmentId)
                .OrderBy(n => n.CreatedAt)
                .ToListAsync();

            notifs.Should().HaveCount(4);
            var reminders = notifs.Where(n => n.Type == NotificationType.Reminder).OrderBy(n => n.ScheduledFor).ToList();
            reminders.Should().HaveCount(2);
            reminders[0].Channel.Should().Be(NotificationChannel.WhatsApp);
            reminders[1].Channel.Should().Be(NotificationChannel.Email);

            var finalNotif = notifs.Single(n => n.Type == NotificationType.FinalConfirmation);
            finalNotif.TemplateParamsJson.Should().Contain("deadline");
        }

        host.FakeStore.SentMessages.Should().Contain(m =>
            m.AppointmentId == appointmentId &&
            m.Type == NotificationType.Reminder &&
            m.Channel == NotificationChannel.WhatsApp);
        host.FakeStore.SentMessages.Should().Contain(m =>
            m.AppointmentId == appointmentId &&
            m.Type == NotificationType.Reminder &&
            m.Channel == NotificationChannel.Email);
        host.FakeStore.SentMessages.Should().Contain(m =>
            m.AppointmentId == appointmentId &&
            m.Type == NotificationType.FinalConfirmation);
    }

    [Fact]
    public async Task ShortNoticeAppointments_CompressTimelineAndAvoidRetroactiveDeadlineOnSameDay()
    {
        await using var host = await WorkerTestHost.CreateAsync();
        var t0 = DateTimeOffset.UtcNow;

        // Cita a 18h: debe crear FinalConfirmation comprimida + ConfirmationDeadline
        var (app18hId, _) = await host.CreateAppointmentViaHandlerAsync(t0.AddHours(18));

        // Cita a 6h (mismo día): solo FinalConfirmation comprimida, SIN ConfirmationDeadline
        var (app6hId, _) = await host.CreateAppointmentViaHandlerAsync(t0.AddHours(6));

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var actions18 = await db.AppointmentScheduledActions
                .AsNoTracking()
                .Where(a => a.AppointmentId == app18hId)
                .OrderBy(a => a.DueAt)
                .ToListAsync();

            actions18.Select(a => a.Kind).Should().ContainInOrder(
                ScheduledActionKind.FinalConfirmation,
                ScheduledActionKind.ConfirmationDeadline);
            actions18[0].MetadataJson.Should().Contain("compressed");
            actions18[0].DueAt.Should().BeBefore(actions18[1].DueAt);

            var actions6 = await db.AppointmentScheduledActions
                .AsNoTracking()
                .Where(a => a.AppointmentId == app6hId)
                .ToListAsync();

            actions6.Should().ContainSingle();
            actions6[0].Kind.Should().Be(ScheduledActionKind.FinalConfirmation);
            actions6[0].MetadataJson.Should().Contain("compressed");
        }

        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        var batch = await processor.ProcessDueBatchAsync("worker-short", t0.AddHours(3));
        batch.Claimed.Should().Be(2);
        batch.Completed.Should().Be(2);
    }

    [Fact]
    public async Task ParallelWorkers_ClaimingSameDueAction_ExecuteExactlyOnceWithoutDuplicateNotification()
    {
        await using var host = await WorkerTestHost.CreateAsync();
        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(4);

        var (appointmentId, _) = await host.CreateAppointmentViaHandlerAsync(startUtc);
        var dueCheckTime = startUtc.AddHours(-72).AddMinutes(1);
        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();

        using var barrier = new Barrier(2);
        var taskA = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return processor.ProcessDueBatchAsync("worker-A", dueCheckTime);
        });
        var taskB = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return processor.ProcessDueBatchAsync("worker-B", dueCheckTime);
        });

        var results = await Task.WhenAll(taskA, taskB);
        results.Sum(r => r.Claimed).Should().Be(1);
        results.Sum(r => r.Completed).Should().Be(1);

        await host.WaitForConsumedNotificationsAsync(2); // T0 + 1 Reminder

        using var scope = host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reminders = await db.Notifications
            .AsNoTracking()
            .Where(n => n.AppointmentId == appointmentId && n.Type == NotificationType.Reminder)
            .ToListAsync();
        reminders.Should().ContainSingle();
    }

    [Fact]
    public async Task CrashRecovery_ExpiredLeaseIsReclaimed_AndPoisonPillTransitionsToFailedAtMaxAttempts()
    {
        await using var host = await WorkerTestHost.CreateAsync(maxAttempts: 2, leaseSeconds: 30);
        var t0 = DateTimeOffset.UtcNow;
        var startUtc = t0.AddDays(4);

        var (appointmentId, _) = await host.CreateAppointmentViaHandlerAsync(startUtc);
        var dueCheckTime = startUtc.AddHours(-72).AddMinutes(1);

        // Simulamos un pod que reclamó la acción y murió dejando lease_until vencido
        using (var scope = host.Provider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            var claimed = await repo.ClaimDueBatchAsync("dead-worker", dueCheckTime, TimeSpan.FromSeconds(30), 10);
            claimed.Should().ContainSingle();
            claimed[0].Attempts.Should().Be(1);
        }

        // Worker vivo reclama tras expirar lease -> Attempts = 2 -> completa exitosamente
        var processor = host.Provider.GetRequiredService<IScheduledActionsProcessor>();
        var recovered = await processor.ProcessDueBatchAsync("alive-worker", dueCheckTime.AddMinutes(1));
        recovered.Should().Be(new ScheduledActionsBatchResult(1, 1, 0, 0, 0));

        // Ahora simulamos un poison pill que ya agotó MaxAttempts=2 al expirar lease
        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var r2 = await db.AppointmentScheduledActions.FirstAsync(a =>
                a.AppointmentId == appointmentId && a.Kind == ScheduledActionKind.Reminder2AndRescore);
            r2.Claim("crash-1", dueCheckTime, TimeSpan.FromSeconds(10), Guid.NewGuid());
            r2.Claim("crash-2", dueCheckTime.AddSeconds(20), TimeSpan.FromSeconds(10), Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        var failedBatch = await processor.ProcessDueBatchAsync("alive-worker-2", startUtc.AddHours(-48));
        failedBatch.Should().Be(new ScheduledActionsBatchResult(1, 0, 0, 0, 1));

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var r2 = await db.AppointmentScheduledActions.AsNoTracking().FirstAsync(a =>
                a.AppointmentId == appointmentId && a.Kind == ScheduledActionKind.Reminder2AndRescore);
            r2.Status.Should().Be(ScheduledActionStatus.Failed);
            r2.Attempts.Should().Be(3);
            r2.LockToken.Should().BeNull();
        }
    }

    [Fact]
    public async Task CancelAndReschedule_CancelsPendingActionsAndRegeneratesPlanVersion2()
    {
        await using var host = await WorkerTestHost.CreateAsync();
        var t0 = DateTimeOffset.UtcNow;

        var (appointmentId, newBlock) = await host.CreateAppointmentViaHandlerAsync(t0.AddDays(5));

        using (var scope = host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var planner = scope.ServiceProvider.GetRequiredService<IAppointmentLifecyclePlanner>();
            var app = await db.Appointments.FirstAsync(a => a.Id == appointmentId);

            // Reagendar para un bloque nuevo 2 días después
            var rescheduledCount = await planner.RecalculateForRescheduleAsync(app, newBlock, Guid.NewGuid(), t0.AddMinutes(10));
            rescheduledCount.Should().Be(4);
            await db.SaveChangesAsync();

            var allActions = await db.AppointmentScheduledActions
                .AsNoTracking()
                .Where(a => a.AppointmentId == appointmentId)
                .ToListAsync();

            allActions.Where(a => a.PlanVersion == 1).Should().HaveCount(4)
                .And.OnlyContain(a => a.Status == ScheduledActionStatus.Cancelled);
            allActions.Where(a => a.PlanVersion == 2).Should().HaveCount(4)
                .And.OnlyContain(a => a.Status == ScheduledActionStatus.Pending);
        }

        // Cancelar cita mediante CancelAppointmentCommandHandler -> cancela todas las acciones Pending del plan v2
        using (var scope = host.Provider.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<MediatR.IRequestHandler<CancelAppointmentCommand>>();
            await handler.Handle(new CancelAppointmentCommand(appointmentId, "No asistirá"), CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pending = await db.AppointmentScheduledActions
                .AsNoTracking()
                .Where(a => a.AppointmentId == appointmentId && a.Status == ScheduledActionStatus.Pending)
                .ToListAsync();
            pending.Should().BeEmpty();
        }
    }

    private sealed class WorkerTestHost : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public ITestHarness Harness { get; }
        public InMemoryFakeNotificationStore FakeStore { get; }

        private WorkerTestHost(ServiceProvider provider, ITestHarness harness, InMemoryFakeNotificationStore fakeStore)
        {
            Provider = provider;
            Harness = harness;
            FakeStore = fakeStore;
        }

        public static async Task<WorkerTestHost> CreateAsync(int maxAttempts = 5, int leaseSeconds = 120)
        {
            var services = new ServiceCollection();
            var dbName = $"ScheduledActionsDb_{Guid.NewGuid()}";
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
            services.AddScoped<IAiResolutionTaskRepository, AiResolutionTaskRepository>();
            services.AddScoped<IAiResolutionSupersedingService, AiResolutionSupersedingService>();
            services.AddScoped<IRiskSignalProcessor, RiskSignalProcessor>();
            services.AddScoped<IAppointmentRescoreService, AppointmentRescoreService>();
            services.AddScoped<IAiClinicalService, StubWorkerAiClinicalService>();
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
            services.AddSingleton<IScheduledActionsProcessor, ScheduledActionsProcessor>();

            services.AddScoped<MediatR.IRequestHandler<CreateAppointmentCommand, Guid>, CreateAppointmentCommandHandler>();
            services.AddScoped<MediatR.IRequestHandler<CancelAppointmentCommand>, CancelAppointmentCommandHandler>();

            services.Configure<ScheduledActionsOptions>(o =>
            {
                o.MaxAttempts = maxAttempts;
                o.LeaseDurationSeconds = leaseSeconds;
                o.BatchSize = 25;
            });

            services.Configure<NotificationOptions>(o =>
            {
                o.ClinicName = "Clínica MedCare";
                o.TimeZoneId = "America/Santiago";
                o.PublicBaseUrl = "https://app.medcare.cl";
                o.ActionTokenSecret = "test-secret-for-scheduled-actions-worker-32-chars!";
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
                x.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConnectReceiveObserver(context.GetRequiredService<DeadLetterAlertObserver>());
                    cfg.ConfigureEndpoints(context);
                });
            });

            // Simular EF Core Bus Outbox: encolar publicaciones dentro del scope y despachar después de SaveChangesAsync()
            services.AddScoped<TestOutboxBuffer>();
            services.RemoveAll<IPublishEndpoint>();
            services.AddScoped<IPublishEndpoint>(sp => sp.GetRequiredService<TestOutboxBuffer>());
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork, OutboxFlushingUnitOfWork>();

            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            return new WorkerTestHost(provider, harness, store);
        }

        public async Task<(Guid AppointmentId, ScheduleBlock SecondaryBlock)> CreateAppointmentViaHandlerAsync(
            DateTimeOffset startUtc,
            NotificationChannel preferredChannel = NotificationChannel.WhatsApp,
            bool whatsAppOptIn = true,
            string? email = "ana.test@medcare.cl")
        {
            var sentBefore = FakeStore.SentMessages.Count;

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dra", "Lagos", $"{Random.Shared.Next(10000000, 99999999)}-1", "Cardiologia", "Medico", "+56911111111");
            var patient = Patient.Create(
                patUser.Id,
                $"{Random.Shared.Next(10000000, 99999999)}-2",
                "Sofia",
                "Perez",
                new DateOnly(1990, 5, 10),
                "F",
                "+56922222222",
                email: email,
                preferredChannel: preferredChannel,
                whatsAppOptIn: whatsAppOptIn,
                consentSource: "test_seed");
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
            var start1 = startUtc.UtcDateTime;
            var block1 = ScheduleBlock.Create(
                staff.Id, box.Id, start1, start1.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(start1), DateOnly.FromDateTime(start1),
                new TimeOnly(10, 0), new TimeOnly(10, 30), [start1.DayOfWeek]);
            var start2 = start1.AddDays(2);
            var block2 = ScheduleBlock.Create(
                staff.Id, box.Id, start2, start2.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(start2), DateOnly.FromDateTime(start2),
                new TimeOnly(10, 0), new TimeOnly(10, 30), [start2.DayOfWeek]);

            db.Users.AddRange(staffUser, patUser);
            db.Staffs.Add(staff);
            db.Patients.Add(patient);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(block1, block2);
            await db.SaveChangesAsync();

            var handler = scope.ServiceProvider.GetRequiredService<MediatR.IRequestHandler<CreateAppointmentCommand, Guid>>();
            var appointmentId = await handler.Handle(
                new CreateAppointmentCommand(block1.Id, patient.Id, "Control preventivo", "paciente"),
                CancellationToken.None);

            await WaitForConsumedNotificationsAsync(sentBefore + 1);
            return (appointmentId, block2);
        }

        public async Task WaitForConsumedNotificationsAsync(int expectedTotalSent)
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

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) =>
            _bus.ConnectPublishObserver(observer);

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

    private sealed class StubWorkerAiClinicalService : IAiClinicalService
    {
        public Task<NlpSummaryResponseDto> GetNlpSummaryAsync(Guid appointmentId, string symptomsRaw, CancellationToken ctx)
            => Task.FromResult(new NlpSummaryResponseDto(symptomsRaw, [], "baja"));

        public Task<PredictResponseDto> PredictNoShowRiskAsync(PredictRequestDto request, CancellationToken ctx)
            => Task.FromResult(new PredictResponseDto(request.AppointmentId, 0.30m, "Bajo", "asiste"));
    }
}
