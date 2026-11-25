using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.WaitingLists.Services;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.WaitingLists.Commands.DeclineWaitingListOffer;

public class DeclineWaitingListOfferCommandHandler : IRequestHandler<DeclineWaitingListOfferCommand, DeclineWaitingListOfferResult>
{
    private readonly IWaitingListOfferRepository _offerRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientActionTokenRepository _tokenRepository;
    private readonly IPatientActionTokenService _tokenService;
    private readonly IWaitingListBackfillService _backfillService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeclineWaitingListOfferCommandHandler> _logger;

    public DeclineWaitingListOfferCommandHandler(
        IWaitingListOfferRepository offerRepository,
        IWaitingListRepository waitingListRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IPatientRepository patientRepository,
        IPatientActionTokenRepository tokenRepository,
        IPatientActionTokenService tokenService,
        IWaitingListBackfillService backfillService,
        ITransactionalExecutor transactionalExecutor,
        IUnitOfWork unitOfWork,
        ILogger<DeclineWaitingListOfferCommandHandler> logger)
    {
        _offerRepository = offerRepository;
        _waitingListRepository = waitingListRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _patientRepository = patientRepository;
        _tokenRepository = tokenRepository;
        _tokenService = tokenService;
        _backfillService = backfillService;
        _transactionalExecutor = transactionalExecutor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<DeclineWaitingListOfferResult> Handle(
        DeclineWaitingListOfferCommand request,
        CancellationToken cancellationToken)
    {
        Guid offerId;
        Guid? resolvedPatientId = null;

        if (!string.IsNullOrWhiteSpace(request.RawToken))
        {
            var consumed = await _tokenService.ConsumeAsync(request.RawToken, PatientReplyIntent.Cancel, cancellationToken);
            if (!consumed.IsSuccess)
            {
                throw new ActionTokenValidationException(
                    consumed.ValidationStatus,
                    "El enlace de la oferta no es válido, ya fue usado o expiró.");
            }

            if (!consumed.WaitingListOfferId.HasValue)
            {
                throw new ActionTokenValidationException(
                    ActionTokenValidationStatus.ScopeNotAllowed,
                    "El token no corresponde a una oferta de lista de espera.");
            }

            offerId = consumed.WaitingListOfferId.Value;
            resolvedPatientId = consumed.PatientId;
        }
        else
        {
            if (!request.OfferId.HasValue || request.OfferId.Value == Guid.Empty)
                throw new ArgumentException("OfferId o RawToken es obligatorio.", nameof(request.OfferId));

            offerId = request.OfferId.Value;

            if (request.AuthenticatedUserId.HasValue)
            {
                var callerPatient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
                    ?? throw new UnauthorizedAccessException("El usuario autenticado no tiene perfil de paciente.");
                resolvedPatientId = callerPatient.Id;
            }
            else if (request.ExpectedPatientId.HasValue && request.ExpectedPatientId.Value != Guid.Empty)
            {
                resolvedPatientId = request.ExpectedPatientId.Value;
            }
        }

        var initialOffer = await _offerRepository.GetByIdAsync(offerId, cancellationToken)
            ?? throw new KeyNotFoundException($"La oferta {offerId} no existe.");

        if (resolvedPatientId.HasValue && initialOffer.PatientId != resolvedPatientId.Value)
            throw new UnauthorizedAccessException("No tienes permiso para rechazar una oferta de otro paciente.");

        var lockKey = initialOffer.SourceAppointmentId.HasValue
            ? $"overbook-source:{initialOffer.SourceAppointmentId.Value:N}"
            : $"block:{initialOffer.ScheduleBlockId:N}";

        var (shouldCascade, tenantId, blockId, sourceType, sourceAppointmentId, wasReplay, finalStatus) =
            await _transactionalExecutor.ExecuteAsync(
                lockKey,
                async ct =>
                {
                    var utcNow = DateTimeOffset.UtcNow;
                    var offer = await _offerRepository.GetByIdAsync(offerId, ct)
                        ?? throw new KeyNotFoundException($"La oferta {offerId} no existe.");

                    if (resolvedPatientId.HasValue && offer.PatientId != resolvedPatientId.Value)
                        throw new UnauthorizedAccessException("No tienes permiso para rechazar una oferta de otro paciente.");

                    if (offer.Status == WaitingListOfferStatus.Declined)
                    {
                        return (false, offer.TenantId, offer.ScheduleBlockId, offer.SourceType, offer.SourceAppointmentId, true, offer.Status);
                    }

                    if (offer.Status != WaitingListOfferStatus.Pending)
                    {
                        throw new InvalidOperationException($"No se puede rechazar una oferta en estado '{offer.Status}'.");
                    }

                    offer.Decline(request.Reason, utcNow);
                    await _tokenRepository.RevokeActiveForWaitingListOfferAsync(offer.Id, utcNow, ct);

                    var waitingListEntry = await _waitingListRepository.GetByIdAsync(offer.WaitingListId, ct);
                    if (waitingListEntry is not null && waitingListEntry.Status == WaitingListStatus.Offered)
                    {
                        waitingListEntry.ReturnToActive(utcNow);
                    }

                    var block = await _scheduleBlockRepository.GetByIdForUpdateAsync(offer.ScheduleBlockId, ct)
                        ?? await _scheduleBlockRepository.GetByIdAsync(offer.ScheduleBlockId, ct);

                    if (block is not null && block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                    {
                        block.RevertOverbookToOpen(utcNow);
                    }

                    await _unitOfWork.SaveChangesAsync(ct);

                    return (true, offer.TenantId, offer.ScheduleBlockId, offer.SourceType, offer.SourceAppointmentId, false, offer.Status);
                },
                cancellationToken);

        if (wasReplay)
        {
            return new DeclineWaitingListOfferResult(true, offerId, finalStatus, WasIdempotentReplay: true);
        }

        string? cascadeOutcome = null;
        Guid? nextOfferId = null;

        if (shouldCascade)
        {
            var cascade = await _backfillService.OfferNextCandidateAsync(
                scheduleBlockId: blockId,
                tenantId: tenantId,
                sourceType: sourceType,
                sourceAppointmentId: sourceAppointmentId,
                correlationId: Guid.NewGuid(),
                utcNow: DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken);

            cascadeOutcome = cascade.Outcome;
            nextOfferId = cascade.OfferId;

            _logger.LogInformation(
                "Oferta {OfferId} rechazada. Cascada hacia siguiente candidato resultó en '{Outcome}' (NextOfferId={NextOfferId}).",
                offerId,
                cascadeOutcome,
                nextOfferId);
        }

        return new DeclineWaitingListOfferResult(
            IsSuccess: true,
            OfferId: offerId,
            OfferStatus: finalStatus,
            WasIdempotentReplay: false,
            CascadeOutcome: cascadeOutcome,
            NextOfferId: nextOfferId);
    }
}
