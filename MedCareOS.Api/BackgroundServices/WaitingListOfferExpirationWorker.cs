using MedCareOS.Application.WaitingLists.Workers;

namespace MedCareOS.Api.BackgroundServices;

public class WaitingListOfferExpirationWorker : BackgroundService
{
    private readonly IWaitingListOfferExpirationProcessor _processor;
    private readonly ILogger<WaitingListOfferExpirationWorker> _logger;

    public WaitingListOfferExpirationWorker(
        IWaitingListOfferExpirationProcessor processor,
        ILogger<WaitingListOfferExpirationWorker> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var processed = await _processor.ProcessBatchAsync(25, stoppingToken);
                if (processed > 0)
                {
                    _logger.LogInformation(
                        "WaitingListOfferExpirationWorker procesó {Processed} oferta(s) expirada(s) en cascada.",
                        processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el ciclo de expiración de ofertas de lista de espera.");
            }
        }
    }
}
