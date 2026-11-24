using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Testing;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Messaging.Configuration;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Messaging.Observers;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omro.Contracts.Events;
using Quartz;

namespace MedCareOS.IntegrationTests.Messaging;

public class ScoreAppointmentOutboxAndConsumerTests
{
    [Fact]
    public void ApplicationDbContext_ShouldMapMassTransitOutboxAndInboxTablesToMessagingSchema()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=medcare_design;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ApplicationDbContext(options);

        var inboxEntity = dbContext.Model.FindEntityType(typeof(InboxState));
        var outboxMessageEntity = dbContext.Model.FindEntityType(typeof(OutboxMessage));
        var outboxStateEntity = dbContext.Model.FindEntityType(typeof(OutboxState));
        var processedMessageEntity = dbContext.Model.FindEntityType(typeof(ProcessedMessage));

        inboxEntity.Should().NotBeNull();
        inboxEntity!.GetSchema().Should().Be("messaging");

        outboxMessageEntity.Should().NotBeNull();
        outboxMessageEntity!.GetSchema().Should().Be("messaging");

        outboxStateEntity.Should().NotBeNull();
        outboxStateEntity!.GetSchema().Should().Be("messaging");

        processedMessageEntity.Should().NotBeNull();
        processedMessageEntity!.GetSchema().Should().Be("messaging");
        processedMessageEntity.GetTableName().Should().Be("processed_messages");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScoreAppointmentConsumer_HappyPath_ShouldUpdateAppointment_AndRespectPublishAnomalyFlag(
        bool publishAnomalyOnCreate)
    {
        var fakeAi = new ProgrammableAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto(
                "Dolor torácico leve",
                [new SymptomEntityDto("sintoma", "dolor", "medio")],
                "media")),
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(
                req.AppointmentId,
                0.82m,
                RiskThresholds.HighRiskBand,
                "no_asiste")));

        await using var ctx = await TestHarnessHost.CreateAsync(fakeAi, publishAnomalyOnCreate: publishAnomalyOnCreate);
        var appointmentId = await ctx.SeedAppointmentAsync("Dolor torácico hace 2 días");

        await ctx.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var consumerHarness = ctx.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await consumerHarness.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        using var scope = ctx.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);

        updated.NlpSummary.Should().Be("Dolor torácico leve");
        updated.NoShowRiskScore.Should().Be(0.82m);
        updated.RiskBand.Should().Be(RiskThresholds.HighRiskBand);

        // Give bus a brief moment when checking absence of AnomalyDetected
        if (publishAnomalyOnCreate)
        {
            (await ctx.Harness.Published.Any<AnomalyDetected>(p => p.Context.Message.ResourceId == appointmentId))
                .Should().BeTrue();
        }
        else
        {
            await Task.Delay(150);
            (await ctx.Harness.Published.Any<AnomalyDetected>(p => p.Context.Message.ResourceId == appointmentId))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task ScoreAppointmentConsumer_WhenAiFailsTransientlyTwice_ShouldRetryAndSucceedOnThirdAttempt()
    {
        int predictAttempts = 0;
        var fakeAi = new ProgrammableAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto("Cefalea", [], "baja")),
            onPredict: (req, _) =>
            {
                var attempt = Interlocked.Increment(ref predictAttempts);
                if (attempt < 3)
                    throw new AiServiceTransientException("FastAPI 503 Service Unavailable", 503);

                return Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.35m, "Bajo", "asiste"));
            });

        await using var ctx = await TestHarnessHost.CreateAsync(fakeAi, retryLimit: 3);
        var appointmentId = await ctx.SeedAppointmentAsync("Dolor de cabeza");

        await ctx.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        Appointment? saved = null;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = ctx.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            saved = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            if (saved.RiskBand == "Bajo")
                break;
            await Task.Delay(30);
        }

        predictAttempts.Should().Be(3);
        saved.Should().NotBeNull();
        saved!.NoShowRiskScore.Should().Be(0.35m);
        saved.RiskBand.Should().Be("Bajo");
    }

    [Fact]
    public async Task ScoreAppointmentConsumer_WhenRetriesExhaustedAndQuartzRedeliveryEnabled_ShouldRedeliverAndSucceed()
    {
        int predictAttempts = 0;
        var fakeAi = new ProgrammableAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto("Control respiratorio", [], "baja")),
            onPredict: (req, _) =>
            {
                var attempt = Interlocked.Increment(ref predictAttempts);
                // RetryLimit=1 ejecuta intento inicial (#1) + 1 retry inmediato (#2);
                // redelivery #1 de Quartz ejecuta intento #3 -> éxito
                if (attempt < 3)
                    throw new AiServiceTransientException("FastAPI timeout", 504);

                return Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.45m, "Bajo", "asiste"));
            });

        await using var ctx = await TestHarnessHost.CreateAsync(
            fakeAi,
            retryLimit: 1,
            redeliveryMode: "QuartzInMemory",
            redeliveryIntervalsMs: [80, 160]);

        var appointmentId = await ctx.SeedAppointmentAsync("Tos persistente");

        await ctx.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        Appointment? saved = null;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = ctx.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            saved = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            if (saved.RiskBand == "Bajo")
                break;
            await Task.Delay(50);
        }

        predictAttempts.Should().Be(3);
        saved.Should().NotBeNull();
        saved!.NoShowRiskScore.Should().Be(0.45m);
        saved.RiskBand.Should().Be("Bajo");
    }

    [Fact]
    public async Task ScoreAppointmentConsumer_WhenAiFailsPermanently_ShouldNotRetryAndEmitDeadLetterAlert()
    {
        int predictAttempts = 0;
        var fakeAi = new ProgrammableAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto("Cefalea", [], "baja")),
            onPredict: (_, _) =>
            {
                Interlocked.Increment(ref predictAttempts);
                throw new AiServicePermanentException("Payload inválido 400", 400);
            });

        await using var ctx = await TestHarnessHost.CreateAsync(fakeAi, retryLimit: 3);
        var appointmentId = await ctx.SeedAppointmentAsync("Sintomas");

        await ctx.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var faulted = await ctx.Harness.Consumed.SelectAsync<AppointmentCreated>(
            c => c.Context.Message.AppointmentId == appointmentId && c.Exception != null).FirstOrDefaultAsync();
        faulted.Should().NotBeNull();

        predictAttempts.Should().Be(1);
        ctx.AlertSink.Alerts.Should().Contain(a =>
            a.ExceptionType == nameof(AiServicePermanentException) &&
            a.MessageType == nameof(AppointmentCreated));
    }

    [Fact]
    public async Task ScoreAppointmentConsumer_WhenSameMessageIdDeliveredTwice_ShouldDeduplicateViaInbox()
    {
        int nlpCalls = 0;
        int predictCalls = 0;
        var fakeAi = new ProgrammableAiClinicalService(
            onNlp: (_, _, _) =>
            {
                Interlocked.Increment(ref nlpCalls);
                return Task.FromResult(new NlpSummaryResponseDto("Resumen válido", [], "baja"));
            },
            onPredict: (req, _) =>
            {
                Interlocked.Increment(ref predictCalls);
                return Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.25m, "Bajo", "asiste"));
            });

        await using var ctx = await TestHarnessHost.CreateAsync(fakeAi);
        var appointmentId = await ctx.SeedAppointmentAsync("Control médico");
        var duplicateMessageId = Guid.NewGuid();

        await ctx.Harness.Bus.Publish(
            new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()),
            publishCtx => publishCtx.MessageId = duplicateMessageId);

        await ctx.Harness.Bus.Publish(
            new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()),
            publishCtx => publishCtx.MessageId = duplicateMessageId);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (ctx.ReceivedObserver.GetPostReceiveCount(duplicateMessageId) >= 2)
                break;
            await Task.Delay(30);
        }

        ctx.ReceivedObserver.GetPostReceiveCount(duplicateMessageId).Should().Be(2);
        nlpCalls.Should().Be(1);
        predictCalls.Should().Be(1);

        using var scope = ctx.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inboxRecords = await db.ProcessedMessages
            .Where(m => m.MessageId == duplicateMessageId)
            .ToListAsync();
        inboxRecords.Should().ContainSingle();
        inboxRecords[0].Status.Should().Be(ProcessedMessage.StatusCompleted);
    }

    [Theory]
    [InlineData("WhatsApp", true, false, "whatsapp")]
    [InlineData("Email", false, false, "email")]
    [InlineData("WhatsApp", true, true, "ninguno")]
    public async Task ScoreAppointmentConsumer_ShouldForwardRealPatientReminderChannelToPredict(
        string preferredChannelName,
        bool whatsAppOptIn,
        bool optedOut,
        string expectedReminderChannel)
    {
        string? capturedReminderChannel = null;
        var fakeAi = new ProgrammableAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto("Control", [], "baja")),
            onPredict: (req, _) =>
            {
                capturedReminderChannel = req.ReminderChannel;
                return Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.25m, "Bajo", "asiste"));
            });

        await using var ctx = await TestHarnessHost.CreateAsync(fakeAi);
        var preferredChannel = Enum.Parse<NotificationChannel>(preferredChannelName);
        var appointmentId = await ctx.SeedAppointmentAsync(
            "Control preventivo",
            configurePatient: p =>
            {
                p.UpdateNotificationPreferences(preferredChannel, whatsAppOptIn, "test_seed", DateTimeOffset.UtcNow);
                if (optedOut)
                    p.OptOut("test_opt_out", DateTimeOffset.UtcNow);
            });

        await ctx.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var consumerHarness = ctx.Harness.GetConsumerHarness<ScoreAppointmentConsumer>();
        (await consumerHarness.Consumed.Any<AppointmentCreated>(c => c.Context.Message.AppointmentId == appointmentId))
            .Should().BeTrue();

        capturedReminderChannel.Should().Be(expectedReminderChannel);
    }

    private sealed class ProgrammableAiClinicalService : IAiClinicalService
    {
        private readonly Func<Guid, string, CancellationToken, Task<NlpSummaryResponseDto>> _onNlp;
        private readonly Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> _onPredict;

        public ProgrammableAiClinicalService(
            Func<Guid, string, CancellationToken, Task<NlpSummaryResponseDto>> onNlp,
            Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> onPredict)
        {
            _onNlp = onNlp;
            _onPredict = onPredict;
        }

        public Task<NlpSummaryResponseDto> GetNlpSummaryAsync(Guid appointmentId, string symptomsRaw, CancellationToken ctx)
            => _onNlp(appointmentId, symptomsRaw, ctx);

        public Task<PredictResponseDto> PredictNoShowRiskAsync(PredictRequestDto request, CancellationToken ctx)
            => _onPredict(request, ctx);
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

    private sealed class CountingReceiveObserver : IReceiveObserver
    {
        private readonly ConcurrentDictionary<Guid, int> _postReceiveCounts = new();

        public int GetPostReceiveCount(Guid messageId) =>
            _postReceiveCounts.TryGetValue(messageId, out var count) ? count : 0;

        public Task PreReceive(ReceiveContext context) => Task.CompletedTask;

        public Task PostReceive(ReceiveContext context)
        {
            var msgId = context.GetMessageId();
            if (msgId.HasValue)
            {
                _postReceiveCounts.AddOrUpdate(msgId.Value, 1, (_, c) => c + 1);
            }
            return Task.CompletedTask;
        }

        public Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType) where T : class
            => Task.CompletedTask;

        public Task ConsumeFault<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception) where T : class
            => Task.CompletedTask;

        public Task ReceiveFault(ReceiveContext context, Exception exception) => Task.CompletedTask;
    }

    private sealed class TestHarnessHost : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public ITestHarness Harness { get; }
        public RecordingOperationalAlertSink AlertSink { get; }
        public CountingReceiveObserver ReceivedObserver { get; }
        private readonly IScheduler? _scheduler;

        private TestHarnessHost(
            ServiceProvider provider,
            ITestHarness harness,
            RecordingOperationalAlertSink alertSink,
            CountingReceiveObserver receivedObserver,
            IScheduler? scheduler)
        {
            Provider = provider;
            Harness = harness;
            AlertSink = alertSink;
            ReceivedObserver = receivedObserver;
            _scheduler = scheduler;
        }

        public static async Task<TestHarnessHost> CreateAsync(
            IAiClinicalService aiService,
            bool publishAnomalyOnCreate = false,
            int retryLimit = 2,
            string redeliveryMode = "None",
            int[]? redeliveryIntervalsMs = null)
        {
            var services = new ServiceCollection();
            var dbName = $"ScoreHarnessDb_{Guid.NewGuid()}";
            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(dbName).UseInternalServiceProvider(inMemoryProvider));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<IAppointmentRepository, AppointmentRepository>();
            services.AddScoped<IPatientRepository, PatientRepository>();
            services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
            services.AddScoped<IStaffRepository, StaffRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IMessageInboxStore, EfMessageInboxStore>();
            services.AddSingleton(aiService);

            services.Configure<ScoringOptions>(o => o.PublishAnomalyOnCreate = publishAnomalyOnCreate);
            services.Configure<RabbitMqMessagingOptions>(o =>
            {
                o.RedeliveryMode = redeliveryMode;
                o.RetryLimit = retryLimit;
                o.RetryMinIntervalMs = 15;
                o.RetryMaxIntervalMs = 40;
                o.RetryIntervalDeltaMs = 15;
                o.RedeliveryIntervalsMs = redeliveryIntervalsMs ?? [50, 100];
            });

            var alertSink = new RecordingOperationalAlertSink();
            var receivedObserver = new CountingReceiveObserver();
            services.AddSingleton<IOperationalAlertSink>(alertSink);
            services.AddSingleton<DeadLetterAlertObserver>();
            services.AddSingleton(receivedObserver);

            var useQuartz = string.Equals(redeliveryMode, "QuartzInMemory", StringComparison.OrdinalIgnoreCase);
            if (useQuartz)
            {
                services.AddQuartz();
            }

            services.AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(15), testInactivityTimeout: TimeSpan.FromSeconds(2));
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<ScoreAppointmentConsumer, ScoreAppointmentConsumerDefinition>();

                if (useQuartz)
                {
                    x.AddPublishMessageScheduler();
                    x.AddQuartzConsumers();
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.UsePublishMessageScheduler();
                        cfg.ConnectReceiveObserver(context.GetRequiredService<DeadLetterAlertObserver>());
                        cfg.ConnectReceiveObserver(context.GetRequiredService<CountingReceiveObserver>());
                        cfg.ConfigureEndpoints(context);
                    });
                }
                else
                {
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConnectReceiveObserver(context.GetRequiredService<DeadLetterAlertObserver>());
                        cfg.ConnectReceiveObserver(context.GetRequiredService<CountingReceiveObserver>());
                        cfg.ConfigureEndpoints(context);
                    });
                }
            });

            services.AddLogging();

            var provider = services.BuildServiceProvider();
            IScheduler? scheduler = null;
            if (useQuartz)
            {
                var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
                scheduler = await schedulerFactory.GetScheduler();
                await scheduler.Start();
            }

            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            return new TestHarnessHost(provider, harness, alertSink, receivedObserver, scheduler);
        }

        public async Task<Guid> SeedAppointmentAsync(string symptomsRaw, Action<Patient>? configurePatient = null)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patientUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dr", "Test", $"{Random.Shared.Next(10000000, 99999999)}-1", "Medicina General", "Medico", "+56911111111");
            var patient = Patient.Create(patientUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Ana", "Lopez", new DateOnly(1992, 3, 15), "femenino", "+56922222222", email: "ana@test.cl");
            configurePatient?.Invoke(patient);
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
            var start = DateTime.UtcNow.AddDays(2);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, start, start.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
                new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
            var appointment = Appointment.Create(block.Id, patient.Id, symptomsRaw, "paciente");

            db.Users.AddRange(staffUser, patientUser);
            db.Staffs.Add(staff);
            db.Patients.Add(patient);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();

            return appointment.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            if (_scheduler is not null)
                await _scheduler.Shutdown();
            await Provider.DisposeAsync();
        }
    }
}
