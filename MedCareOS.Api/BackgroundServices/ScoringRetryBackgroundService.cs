using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Common.Options;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace MedCareOS.Api.BackgroundServices;

public class ScoringRetryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScoringRetryOptions _options;
    private readonly ILogger<ScoringRetryBackgroundService> _logger;

    public ScoringRetryBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ScoringRetryOptions> options,
        ILogger<ScoringRetryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingScoringRetriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el job automático de re-scoring de citas.");
            }
        }
    }

    public async Task<int> ProcessPendingScoringRetriesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var appointmentRepository = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var utcNow = DateTimeOffset.UtcNow;
        var pendingStaleAge = TimeSpan.FromMinutes(Math.Max(1, _options.PendingStaleMinutes));
        var candidates = await appointmentRepository.GetAppointmentsPendingScoringRetryAsync(
            utcNow,
            pendingStaleAge,
            Math.Max(1, _options.MaxAttempts),
            Math.Max(1, _options.BatchSize),
            cancellationToken);

        if (candidates.Count == 0)
            return 0;

        var leaseUntil = utcNow.AddMinutes(Math.Max(1, _options.LeaseMinutes));

        foreach (var appointment in candidates)
        {
            appointment.ScheduleScoringRetryLease(leaseUntil);
            await publishEndpoint.Publish(
                new AppointmentCreated(
                    AppointmentId: appointment.Id,
                    OccurredAtUtc: utcNow,
                    CorrelationId: Guid.NewGuid()),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job Re-scoring: {Count} cita(s) encolada(s) vía Outbox para reintento de scoring/NLP.",
            candidates.Count);

        return candidates.Count;
    }
}
