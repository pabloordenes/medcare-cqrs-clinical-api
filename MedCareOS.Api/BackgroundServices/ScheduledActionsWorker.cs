using MedCareOS.Application.Appointments.Workers;
using MedCareOS.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace MedCareOS.Api.BackgroundServices;

public class ScheduledActionsWorker : BackgroundService
{
    private readonly IScheduledActionsProcessor _processor;
    private readonly ScheduledActionsOptions _options;
    private readonly ILogger<ScheduledActionsWorker> _logger;
    private readonly string _workerId;

    public ScheduledActionsWorker(
        IScheduledActionsProcessor processor,
        IOptions<ScheduledActionsOptions> options,
        ILogger<ScheduledActionsWorker> logger)
    {
        _processor = processor;
        _options = options.Value;
        _logger = logger;
        _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await _processor.ProcessDueBatchAsync(_workerId, null, stoppingToken);
                if (result.Claimed > 0)
                {
                    _logger.LogInformation(
                        "ScheduledActionsWorker ({WorkerId}) procesó {Claimed} acción(es): Completed={Completed}, Skipped={Skipped}, Retried={Retried}, Failed={Failed}.",
                        _workerId,
                        result.Claimed,
                        result.Completed,
                        result.Skipped,
                        result.Retried,
                        result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el ciclo de polling de ScheduledActionsWorker ({WorkerId}).", _workerId);
            }
        }
    }
}
