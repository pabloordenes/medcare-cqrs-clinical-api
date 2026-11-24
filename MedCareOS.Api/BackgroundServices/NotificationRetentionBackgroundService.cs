using MedCareOS.Application.Common.Options;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace MedCareOS.Api.BackgroundServices;

public class NotificationRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationRetentionBackgroundService> _logger;

    public NotificationRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options,
        ILogger<NotificationRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.RetentionPollIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RedactExpiredNotificationContentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el job automático de retención/anonimización de notificaciones.");
            }
        }
    }

    public async Task<int> RedactExpiredNotificationContentsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var notificationRepository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var tokenRepository = scope.ServiceProvider.GetRequiredService<IPatientActionTokenRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var utcNow = DateTimeOffset.UtcNow;
        var retentionDays = Math.Max(1, _options.ContentRetentionDays);
        var batchSize = Math.Max(1, _options.RetentionBatchSize);
        var cutoffUtc = utcNow.AddDays(-retentionDays);

        var notifications = await notificationRepository.GetPendingRedactionBatchAsync(cutoffUtc, batchSize, cancellationToken);
        foreach (var notification in notifications)
        {
            notification.RedactContent(utcNow);
        }

        var deletedTokens = await tokenRepository.DeleteExpiredBeforeAsync(cutoffUtc, batchSize, cancellationToken);

        if (notifications.Count > 0 || deletedTokens > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Job Retención Notificaciones: {RedactedCount} contenido(s) anonimizado(s), {DeletedTokens} token(s) expirado(s) purgado(s).",
                notifications.Count,
                deletedTokens);
        }

        return notifications.Count;
    }
}
