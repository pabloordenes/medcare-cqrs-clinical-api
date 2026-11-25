using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.WaitingLists.Services;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.WaitingLists.Workers;

public interface IWaitingListOfferExpirationProcessor
{
    Task<int> ProcessBatchAsync(int batchSize = 25, CancellationToken cancellationToken = default);
}

public class WaitingListOfferExpirationProcessor : IWaitingListOfferExpirationProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WaitingListOfferExpirationProcessor> _logger;

    public WaitingListOfferExpirationProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<WaitingListOfferExpirationProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(int batchSize = 25, CancellationToken cancellationToken = default)
    {
        List<Guid> claimedOfferIds;
        using (var claimScope = _scopeFactory.CreateScope())
        {
            var offerRepo = claimScope.ServiceProvider.GetRequiredService<IWaitingListOfferRepository>();
            var expired = await offerRepo.ClaimExpiredPendingOffersAsync(DateTimeOffset.UtcNow, batchSize, cancellationToken);
            claimedOfferIds = expired.Select(o => o.Id).ToList();
        }

        if (claimedOfferIds.Count == 0)
            return 0;

        var processedCount = 0;

        foreach (var offerId in claimedOfferIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var offerRepo = scope.ServiceProvider.GetRequiredService<IWaitingListOfferRepository>();
            var waitingListRepo = scope.ServiceProvider.GetRequiredService<IWaitingListRepository>();
            var blockRepo = scope.ServiceProvider.GetRequiredService<IScheduleBlockRepository>();
            var tokenRepo = scope.ServiceProvider.GetRequiredService<IPatientActionTokenRepository>();
            var transactionalExecutor = scope.ServiceProvider.GetRequiredService<ITransactionalExecutor>();
            var backfillService = scope.ServiceProvider.GetRequiredService<IWaitingListBackfillService>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var initialOffer = await offerRepo.GetByIdAsync(offerId, cancellationToken);
            if (initialOffer is null || initialOffer.Status != WaitingListOfferStatus.Pending)
                continue;

            var lockKey = initialOffer.SourceAppointmentId.HasValue
                ? $"overbook-source:{initialOffer.SourceAppointmentId.Value:N}"
                : $"block:{initialOffer.ScheduleBlockId:N}";

            var (shouldCascade, tenantId, scheduleBlockId, sourceType, sourceAppointmentId) =
                await transactionalExecutor.ExecuteAsync(
                    lockKey,
                    async ct =>
                    {
                        var utcNow = DateTimeOffset.UtcNow;
                        var offer = await offerRepo.GetByIdAsync(offerId, ct);
                        if (offer is null || offer.Status != WaitingListOfferStatus.Pending || offer.ExpiresAt > utcNow)
                            return (false, Guid.Empty, Guid.Empty, string.Empty, (Guid?)null);

                        offer.Expire(utcNow);
                        await tokenRepo.RevokeActiveForWaitingListOfferAsync(offer.Id, utcNow, ct);

                        var waitingListEntry = await waitingListRepo.GetByIdAsync(offer.WaitingListId, ct);
                        if (waitingListEntry is not null && waitingListEntry.Status == WaitingListStatus.Offered)
                        {
                            waitingListEntry.ReturnToActive(utcNow);
                        }

                        var block = await blockRepo.GetByIdForUpdateAsync(offer.ScheduleBlockId, ct)
                            ?? await blockRepo.GetByIdAsync(offer.ScheduleBlockId, ct);

                        if (block is not null && block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                        {
                            block.RevertOverbookToOpen(utcNow);
                        }

                        await unitOfWork.SaveChangesAsync(ct);
                        return (true, offer.TenantId, offer.ScheduleBlockId, offer.SourceType, offer.SourceAppointmentId);
                    },
                    cancellationToken);

            if (!shouldCascade)
                continue;

            processedCount++;

            var cascade = await backfillService.OfferNextCandidateAsync(
                scheduleBlockId: scheduleBlockId,
                tenantId: tenantId,
                sourceType: sourceType,
                sourceAppointmentId: sourceAppointmentId,
                correlationId: Guid.NewGuid(),
                utcNow: DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Oferta expirada {OfferId} procesada. Siguiente cascada: Outcome={Outcome}, NextOfferId={NextOfferId}.",
                offerId,
                cascade.Outcome,
                cascade.OfferId);
        }

        return processedCount;
    }
}
