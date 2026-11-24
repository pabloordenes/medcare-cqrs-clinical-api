using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Domain.Repositories;

namespace MedCareOS.Api.BackgroundServices;

public class NoShowDetectionBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoShowDetectionBackgroundService> _logger;

    public NoShowDetectionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<NoShowDetectionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessExpiredAppointmentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el job automático de detección de inasistencias (NoShow).");
            }
        }
    }

    public async Task<int> ProcessExpiredAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var appointmentRepository = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
        var lifecyclePlanner = scope.ServiceProvider.GetRequiredService<IAppointmentLifecyclePlanner>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expiredAppointments = await appointmentRepository.GetPastUnattendedAppointmentsAsync(DateTime.UtcNow, cancellationToken);
        if (expiredAppointments.Count == 0)
            return 0;

        foreach (var appointment in expiredAppointments)
        {
            appointment.MarkNoShow();
            await lifecyclePlanner.CancelPendingActionsAsync(appointment.Id, "AppointmentNoShow", cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job NoShow: {Count} cita(s) vencida(s) marcada(s) automáticamente como NoShow.",
            expiredAppointments.Count);

        return expiredAppointments.Count;
    }
}
