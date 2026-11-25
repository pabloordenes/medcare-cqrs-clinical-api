using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MedCareOS.Api.BackgroundServices;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Dashboard.Queries.GetMetrics;
using MedCareOS.Application.Interfaces;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedCareOS.IntegrationTests.Messaging;

public class ScoringRetryJobTests
{
    [Fact]
    public async Task ScoreAppointmentConsumer_WhenAiFails_LeavesScoreNullAndNlpSummaryNull_NeverContaminatesWithFallback()
    {
        var aiService = new DelegateAiClinicalService(
            onNlp: (_, _, _) => throw new AiServiceTransientException("NLP offline", 503),
            onPredict: (_, _) => throw new AiServiceTransientException("Predict offline", 503));

        await using var host = await ScoringTestHost.CreateAsync(aiService, retryLimit: 1);
        var appointmentId = await host.SeedAppointmentAsync("Dolor lumbar crónico");

        await host.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var faulted = await host.Harness.Consumed.SelectAsync<AppointmentCreated>(
            c => c.Context.Message.AppointmentId == appointmentId && c.Exception != null).FirstOrDefaultAsync();
        faulted.Should().NotBeNull();

        using var scope = host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);

        saved.ScoringStatus.Should().Be(ScoringStatus.Failed);
        saved.NlpStatus.Should().Be(NlpStatus.Failed);
        saved.NoShowRiskScore.Should().BeNull();
        saved.RiskBand.Should().BeNull();
        saved.NlpSummary.Should().BeNull();
        saved.ScoringAttempts.Should().Be(2); // intento inicial + 1 retry
        saved.NextScoringRetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ScoreAppointmentConsumer_WhenPredictSucceedsAndNlpFails_PersistsScoreAndRetriesOnlyNlp()
    {
        int predictCalls = 0;
        int nlpCalls = 0;

        var aiService = new DelegateAiClinicalService(
            onNlp: (_, _, _) =>
            {
                var attempt = Interlocked.Increment(ref nlpCalls);
                if (attempt == 1)
                    throw new AiServiceTransientException("NLP 503", 503);

                return Task.FromResult(new NlpSummaryResponseDto("Cefalea tensional", [], "baja"));
            },
            onPredict: (req, _) =>
            {
                Interlocked.Increment(ref predictCalls);
                return Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.71m, "Moderado", "no_asiste"));
            });

        await using var host = await ScoringTestHost.CreateAsync(aiService, retryLimit: 2);
        var appointmentId = await host.SeedAppointmentAsync("Cefalea tensional hace 3 días");

        await host.Harness.Bus.Publish(new AppointmentCreated(appointmentId, DateTimeOffset.UtcNow, Guid.NewGuid()));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        Appointment? saved = null;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = host.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            saved = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            if (saved.NlpStatus == NlpStatus.Completed)
                break;
            await Task.Delay(30);
        }

        predictCalls.Should().Be(1, "Predict ya quedó Scored en el intento 1 y no debe repetirse");
        nlpCalls.Should().Be(2);
        saved.Should().NotBeNull();
        saved!.ScoringStatus.Should().Be(ScoringStatus.Scored);
        saved.NlpStatus.Should().Be(NlpStatus.Completed);
        saved.NoShowRiskScore.Should().Be(0.71m);
        saved.NlpSummary.Should().Be("Cefalea tensional");
    }

    [Fact]
    public async Task ScoringRetryJob_ShouldSelectEligiblePendingAndFailedAppointments_AndRespectBackoffAndMaxAttempts()
    {
        var aiService = new DelegateAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto("OK", [], "baja")),
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.20m, "Bajo", "asiste")));

        await using var host = await ScoringTestHost.CreateAsync(aiService, maxAttempts: 5);
        var now = DateTimeOffset.UtcNow;

        var stalePendingId = await host.SeedAppointmentAsync("Síntoma A", createdAtOverride: now.AddMinutes(-10));
        var freshPendingId = await host.SeedAppointmentAsync("Síntoma B", createdAtOverride: now);
        var dueFailedId = await host.SeedAppointmentAsync("Síntoma C", configure: a =>
            a.RecordScoringFailure("503", 5, now.AddMinutes(-5), TimeSpan.FromMinutes(2)));
        var futureBackoffFailedId = await host.SeedAppointmentAsync("Síntoma D", configure: a =>
            a.RecordScoringFailure("503", 5, now, TimeSpan.FromMinutes(15)));
        var exhaustedFailedId = await host.SeedAppointmentAsync("Síntoma E", configure: a =>
        {
            for (int i = 0; i < 5; i++)
                a.RecordScoringFailure("503", 5, now.AddMinutes(-10), TimeSpan.FromMinutes(1));
        });
        var pastAppointmentId = await host.SeedAppointmentAsync("Síntoma F", hoursFromNow: -3, createdAtOverride: now.AddHours(-4));

        var job = new ScoringRetryBackgroundService(
            host.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ScoringRetryOptions
            {
                MaxAttempts = 5,
                PendingStaleMinutes = 5,
                BatchSize = 20,
                LeaseMinutes = 5
            }),
            NullLogger<ScoringRetryBackgroundService>.Instance);

        var enqueued = await job.ProcessPendingScoringRetriesAsync(CancellationToken.None);

        enqueued.Should().Be(2);

        (await host.Harness.Published.Any<AppointmentCreated>(p => p.Context.Message.AppointmentId == stalePendingId)).Should().BeTrue();
        (await host.Harness.Published.Any<AppointmentCreated>(p => p.Context.Message.AppointmentId == dueFailedId)).Should().BeTrue();

        (await host.Harness.Published.Any<AppointmentCreated>(p => p.Context.Message.AppointmentId == freshPendingId)).Should().BeFalse();
        (await host.Harness.Published.Any<AppointmentCreated>(p => p.Context.Message.AppointmentId == futureBackoffFailedId)).Should().BeFalse();
        (await host.Harness.Published.Any<AppointmentCreated>(p => p.Context.Message.AppointmentId == exhaustedFailedId)).Should().BeFalse();
        (await host.Harness.Published.Any<AppointmentCreated>(p => p.Context.Message.AppointmentId == pastAppointmentId)).Should().BeFalse();

        // Verificar que la segunda pasada inmediata no re-encola las mismas citas gracias al lease
        var secondPass = await job.ProcessPendingScoringRetriesAsync(CancellationToken.None);
        secondPass.Should().Be(0);
    }

    [Fact]
    public async Task DashboardMetricsQuery_ShouldExcludeUnscoredAppointmentsFromRiskAlerts_AndUseFallbackLabelOnlyWhenNlpPendingOrFailed()
    {
        var aiService = new DelegateAiClinicalService(
            onNlp: (_, _, _) => Task.FromResult(new NlpSummaryResponseDto("OK", [], "baja")),
            onPredict: (req, _) => Task.FromResult(new PredictResponseDto(req.AppointmentId, 0.20m, "Bajo", "asiste")));

        await using var host = await ScoringTestHost.CreateAsync(aiService);

        var unscoredId = await host.SeedAppointmentAsync("Dolor agudo crudo");
        var scoredId = await host.SeedAppointmentAsync("Control cardiaco", configure: a =>
        {
            a.ApplyNlpAnalysis("Control cardiológico prioritario", "[]");
            a.UpdateRiskScore(0.88m, "Alto");
        });

        using var scope = host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new GetDashboardMetricsQueryHandler(db);

        var metrics = await handler.Handle(new GetDashboardMetricsQuery(), CancellationToken.None);

        metrics.AlertasRiesgo.Should().ContainSingle(a => a.Id == scoredId);
        metrics.AlertasRiesgo.Should().NotContain(a => a.Id == unscoredId);
        metrics.AlertasRiesgo.Single().FactorPrincipal.Should().Be("Control cardiológico prioritario");
    }

    private sealed class DelegateAiClinicalService : IAiClinicalService
    {
        private readonly Func<Guid, string, CancellationToken, Task<NlpSummaryResponseDto>> _onNlp;
        private readonly Func<PredictRequestDto, CancellationToken, Task<PredictResponseDto>> _onPredict;

        public DelegateAiClinicalService(
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

    private sealed class ScoringTestHost : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public ITestHarness Harness { get; }

        private ScoringTestHost(ServiceProvider provider, ITestHarness harness)
        {
            Provider = provider;
            Harness = harness;
        }

        public static async Task<ScoringTestHost> CreateAsync(
            IAiClinicalService aiService,
            int retryLimit = 2,
            int maxAttempts = 5)
        {
            var services = new ServiceCollection();
            var dbName = $"ScoringRetryDb_{Guid.NewGuid()}";
            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(dbName).UseInternalServiceProvider(inMemoryProvider));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<IAppointmentRepository, AppointmentRepository>();
            services.AddScoped<IPatientRepository, PatientRepository>();
            services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
            services.AddScoped<IBoxRepository, BoxRepository>();
            services.AddScoped<IStaffRepository, StaffRepository>();
            services.AddScoped<ITenantConfirmationPolicyRepository, TenantConfirmationPolicyRepository>();
            services.AddScoped<IRiskSignalRepository, RiskSignalRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IMessageInboxStore, EfMessageInboxStore>();
            services.AddSingleton(aiService);

            services.Configure<ScoringOptions>(o => o.PublishAnomalyOnCreate = false);
            services.Configure<ScoringRetryOptions>(o =>
            {
                o.MaxAttempts = maxAttempts;
                o.BaseBackoffMinutes = 2;
                o.MaxBackoffMinutes = 60;
            });
            services.Configure<RabbitMqMessagingOptions>(o =>
            {
                o.RedeliveryMode = "None";
                o.RetryLimit = retryLimit;
                o.RetryMinIntervalMs = 15;
                o.RetryMaxIntervalMs = 40;
                o.RetryIntervalDeltaMs = 15;
            });

            services.AddSingleton<IOperationalAlertSink, LoggingOperationalAlertSink>();
            services.AddSingleton<DeadLetterAlertObserver>();

            services.AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(15), testInactivityTimeout: TimeSpan.FromSeconds(2));
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<ScoreAppointmentConsumer, ScoreAppointmentConsumerDefinition>();
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

            return new ScoringTestHost(provider, harness);
        }

        public async Task<Guid> SeedAppointmentAsync(
            string symptomsRaw,
            double hoursFromNow = 24,
            DateTimeOffset? createdAtOverride = null,
            Action<Appointment>? configure = null)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var patientUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staff = Staff.Create(staffUser.Id, "Dr", "Test", $"{Random.Shared.Next(10000000, 99999999)}-1", "Medicina General", "Medico", "+56911111111");
            var patient = Patient.Create(patientUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Ana", "Lopez", new DateOnly(1992, 3, 15), "femenino", "+56922222222");
            var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
            var start = DateTime.UtcNow.AddHours(hoursFromNow);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, start, start.AddMinutes(30),
                Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
                new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
            var appointment = Appointment.Create(block.Id, patient.Id, symptomsRaw, "paciente");

            if (createdAtOverride.HasValue)
            {
                db.Entry(appointment).Property(nameof(Appointment.CreatedAt)).CurrentValue = createdAtOverride.Value;
            }

            configure?.Invoke(appointment);

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
            await Provider.DisposeAsync();
        }
    }
}
