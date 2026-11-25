using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.WaitingLists.Services;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.WaitingLists.Consumers;

public class OverbookOpenedConsumer : IConsumer<OverbookOpened>
{
    private readonly IWaitingListBackfillService _backfillService;
    private readonly ILogger<OverbookOpenedConsumer> _logger;

    public OverbookOpenedConsumer(
        IWaitingListBackfillService backfillService,
        ILogger<OverbookOpenedConsumer> logger)
    {
        _backfillService = backfillService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OverbookOpened> context)
    {
        var msg = context.Message;
        var result = await _backfillService.OfferNextCandidateAsync(
            scheduleBlockId: msg.OverbookBlockId,
            tenantId: msg.TenantId,
            sourceType: "Overbook",
            sourceAppointmentId: msg.SourceAppointmentId,
            correlationId: msg.CorrelationId,
            utcNow: DateTimeOffset.UtcNow,
            cancellationToken: context.CancellationToken);

        _logger.LogInformation(
            "OverbookOpened procesado para bloque {OverbookBlockId}: Outcome={Outcome}, OfferId={OfferId}.",
            msg.OverbookBlockId,
            result.Outcome,
            result.OfferId);
    }
}
