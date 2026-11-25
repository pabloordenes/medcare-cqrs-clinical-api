using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Appointments.Overbooking;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Appointments.Consumers;

public class LateConfirmationReceivedConsumer : IConsumer<LateConfirmationReceived>
{
    private readonly IOverbookCollisionResolutionService _resolutionService;
    private readonly ILogger<LateConfirmationReceivedConsumer> _logger;

    public LateConfirmationReceivedConsumer(
        IOverbookCollisionResolutionService resolutionService,
        ILogger<LateConfirmationReceivedConsumer> logger)
    {
        _resolutionService = resolutionService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<LateConfirmationReceived> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Procesando LateConfirmationReceived para cita {AppointmentId} (ConfirmedAtUtc={ConfirmedAtUtc}, CorrelationId={CorrelationId}).",
            message.AppointmentId,
            message.ConfirmedAtUtc,
            message.CorrelationId);

        await _resolutionService.HandleLateConfirmationAsync(
            message.AppointmentId,
            message.ConfirmedAtUtc,
            message.CorrelationId,
            context.CancellationToken);
    }
}
