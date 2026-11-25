using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.WaitingLists.Services;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.WaitingLists.Consumers;

public class SlotReleasedConsumer : IConsumer<SlotReleased>
{
    private readonly IWaitingListBackfillService _backfillService;
    private readonly ILogger<SlotReleasedConsumer> _logger;

    public SlotReleasedConsumer(
        IWaitingListBackfillService backfillService,
        ILogger<SlotReleasedConsumer> logger)
    {
        _backfillService = backfillService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlotReleased> context)
    {
        var msg = context.Message;
        var result = await _backfillService.OfferNextCandidateAsync(
            scheduleBlockId: msg.ScheduleBlockId,
            tenantId: msg.TenantId,
            sourceType: "SlotReleased",
            sourceAppointmentId: null,
            correlationId: msg.CorrelationId,
            utcNow: DateTimeOffset.UtcNow,
            cancellationToken: context.CancellationToken);

        _logger.LogInformation(
            "SlotReleased procesado para bloque {ScheduleBlockId} ({Reason}): Outcome={Outcome}, OfferId={OfferId}.",
            msg.ScheduleBlockId,
            msg.TriggerReason,
            result.Outcome,
            result.OfferId);
    }
}
