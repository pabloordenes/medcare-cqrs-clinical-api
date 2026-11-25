using MassTransit;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Application.WaitingLists.Ranking;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.WaitingLists.Services;

public class WaitingListBackfillService : IWaitingListBackfillService
{
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IWaitingListOfferRepository _waitingListOfferRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IWaitingListRanker _ranker;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<WaitingListBackfillService> _logger;

    public WaitingListBackfillService(
        IScheduleBlockRepository scheduleBlockRepository,
        IBoxRepository boxRepository,
        IStaffRepository staffRepository,
        IAppointmentRepository appointmentRepository,
        IWaitingListRepository waitingListRepository,
        IWaitingListOfferRepository waitingListOfferRepository,
        IPatientRepository patientRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IWaitingListRanker ranker,
        INotificationOutboxService notificationOutboxService,
        ITransactionalExecutor transactionalExecutor,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<WaitingListBackfillService> logger)
    {
        _scheduleBlockRepository = scheduleBlockRepository;
        _boxRepository = boxRepository;
        _staffRepository = staffRepository;
        _appointmentRepository = appointmentRepository;
        _waitingListRepository = waitingListRepository;
        _waitingListOfferRepository = waitingListOfferRepository;
        _patientRepository = patientRepository;
        _policyRepository = policyRepository;
        _ranker = ranker;
        _notificationOutboxService = notificationOutboxService;
        _transactionalExecutor = transactionalExecutor;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<WaitingListBackfillAttemptResult> OfferNextCandidateAsync(
        Guid scheduleBlockId,
        Guid tenantId,
        string sourceType,
        Guid? sourceAppointmentId,
        Guid correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var lockKey = sourceAppointmentId.HasValue && sourceAppointmentId.Value != Guid.Empty
            ? $"overbook-source:{sourceAppointmentId.Value:N}"
            : $"block:{scheduleBlockId:N}";

        return await _transactionalExecutor.ExecuteAsync(
            lockKey,
            async ct =>
            {
                var policy = await _policyRepository.GetOrDefaultAsync(tenantId, ct);
                var block = await _scheduleBlockRepository.GetByIdForUpdateAsync(scheduleBlockId, ct)
                    ?? await _scheduleBlockRepository.GetByIdAsync(scheduleBlockId, ct);

                if (block is null)
                    return new WaitingListBackfillAttemptResult("SlotUnavailable");

                var minStartUtc = utcNow.UtcDateTime.AddMinutes(Math.Max(0, policy.MinLeadMinutesForWaitingListOffer));
                if (block.StartTime <= minStartUtc)
                {
                    if (block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                    {
                        block.RevertOverbookToOpen(utcNow);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }

                    return new WaitingListBackfillAttemptResult("LeadTimeTooShort");
                }

                var box = await _boxRepository.GetByIdAsync(block.BoxId, ct);
                if (box is null || !box.IsActive)
                    return new WaitingListBackfillAttemptResult("SlotUnavailable");

                if (await _appointmentRepository.HasActiveAppointmentForBlockAsync(block.Id, ct))
                    return new WaitingListBackfillAttemptResult("SlotUnavailable");

                var effectiveSourceAppId = sourceAppointmentId ?? block.SourceAppointmentId;
                if (block.IsOverbook)
                {
                    if (block.OverbookStatus is not (OverbookBlockStatus.Open or OverbookBlockStatus.Offered) ||
                        !block.SourceAppointmentId.HasValue)
                    {
                        return new WaitingListBackfillAttemptResult("SlotUnavailable");
                    }

                    var sourceAppointment = await _appointmentRepository.GetAppointmentByIdAsync(block.SourceAppointmentId.Value, ct);
                    if (sourceAppointment is null ||
                        sourceAppointment.Status != AppointmentStatus.Scheduled ||
                        sourceAppointment.IsPatientConfirmed)
                    {
                        return new WaitingListBackfillAttemptResult("SlotUnavailable");
                    }

                    effectiveSourceAppId = block.SourceAppointmentId;
                }

                if (await _waitingListOfferRepository.HasActivePendingOfferForBlockAsync(block.Id, utcNow, ct))
                    return new WaitingListBackfillAttemptResult("AlreadyPending");

                var priorOffersCount = await _waitingListOfferRepository.CountOffersForBlockAsync(block.Id, ct);
                if (priorOffersCount >= policy.MaxWaitingListCascadeOffers)
                {
                    if (block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                    {
                        block.RevertOverbookToOpen(utcNow);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }

                    _logger.LogInformation(
                        "Límite de cascada ({Max}) alcanzado para ScheduleBlock {BlockId}. No se emiten más ofertas.",
                        policy.MaxWaitingListCascadeOffers,
                        block.Id);
                    return new WaitingListBackfillAttemptResult("CascadeExhausted");
                }

                var staff = await _staffRepository.GetByIdAsync(block.StaffId, ct);
                if (staff is null || string.IsNullOrWhiteSpace(staff.Specialty))
                    return new WaitingListBackfillAttemptResult("SlotUnavailable");

                var attemptedIds = await _waitingListOfferRepository.GetAttemptedWaitingListIdsForBlockAsync(block.Id, ct);
                var tz = NotificationFormattingHelper.ResolveTimeZone(policy.TimeZoneId);

                var candidates = await _waitingListRepository.QueryEligibleCandidatesAsync(
                    tenantId,
                    staff.Specialty,
                    block.StartTime,
                    block.EndTime,
                    tz,
                    attemptedIds,
                    utcNow,
                    ct);

                if (candidates.Count == 0)
                {
                    if (block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                    {
                        block.RevertOverbookToOpen(utcNow);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }

                    return new WaitingListBackfillAttemptResult("NoCandidates");
                }

                var slotContext = new WaitingListSlotContext(
                    ScheduleBlockId: block.Id,
                    TenantId: tenantId,
                    Specialty: staff.Specialty,
                    StaffId: block.StaffId,
                    StartTimeUtc: DateTime.SpecifyKind(block.StartTime, DateTimeKind.Utc),
                    EndTimeUtc: DateTime.SpecifyKind(block.EndTime, DateTimeKind.Utc));

                var snapshots = candidates.Select(c => new WaitingListCandidateSnapshot(
                    c.Id,
                    c.PatientId,
                    c.Specialty,
                    c.RegistrationDate,
                    c.Priority,
                    c.PreferredStaffId,
                    c.IsDisplacedBonus,
                    c.ClinicalContext)).ToList();

                var ranked = _ranker.Rank(slotContext, snapshots, utcNow);
                var byId = candidates.ToDictionary(c => c.Id);

                WaitingList? selectedEntry = null;
                Patient? selectedPatient = null;

                foreach (var rankedItem in ranked)
                {
                    var candidateEntry = byId[rankedItem.Candidate.WaitingListId];
                    var candidatePatient = await _patientRepository.GetPatientByIdAsync(candidateEntry.PatientId, ct);
                    if (candidatePatient is null ||
                        candidatePatient.HasOptedOut() ||
                        !candidatePatient.GetEffectiveReminderChannel().HasValue)
                    {
                        continue;
                    }

                    var hasConflict = await _appointmentRepository.HasOverlappingActiveAppointmentAsync(
                        candidatePatient.Id,
                        block.StartTime,
                        block.EndTime,
                        ct);
                    if (hasConflict)
                        continue;

                    selectedEntry = candidateEntry;
                    selectedPatient = candidatePatient;
                    break;
                }

                if (selectedEntry is null || selectedPatient is null)
                {
                    if (block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                    {
                        block.RevertOverbookToOpen(utcNow);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }

                    return new WaitingListBackfillAttemptResult("NoCandidates");
                }

                var blockStartOffset = new DateTimeOffset(DateTime.SpecifyKind(block.StartTime, DateTimeKind.Utc), TimeSpan.Zero);
                var maxExpiresAt = blockStartOffset.AddMinutes(-15);
                var configuredExpiresAt = utcNow.AddMinutes(Math.Max(5, policy.WaitingListOfferTtlMinutes));
                var expiresAt = configuredExpiresAt <= maxExpiresAt ? configuredExpiresAt : maxExpiresAt;

                if (expiresAt <= utcNow)
                {
                    if (block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Offered)
                    {
                        block.RevertOverbookToOpen(utcNow);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }

                    return new WaitingListBackfillAttemptResult("LeadTimeTooShort");
                }

                var offer = WaitingListOffer.Create(
                    tenantId: tenantId,
                    waitingListId: selectedEntry.Id,
                    patientId: selectedPatient.Id,
                    scheduleBlockId: block.Id,
                    sourceType: sourceType,
                    sourceAppointmentId: effectiveSourceAppId,
                    cascadeAttemptNumber: priorOffersCount + 1,
                    offeredAtUtc: utcNow,
                    expiresAtUtc: expiresAt);

                selectedEntry.MarkOffered(utcNow);
                if (block.IsOverbook && block.OverbookStatus == OverbookBlockStatus.Open)
                {
                    block.MarkOverbookOffered(utcNow);
                }

                await _waitingListOfferRepository.AddAsync(offer, ct);

                var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
                    block.StartTime,
                    policy.TimeZoneId);
                var deadlineFormatted = NotificationFormattingHelper.FormatDeadline(expiresAt, policy.TimeZoneId);
                var specialtyDisplay = NotificationFormattingHelper.ResolveSpecialtyDisplay(
                    staff.Specialty,
                    _notificationOptions.IncludeSpecialtyInMessages,
                    _notificationOptions.SensitiveSpecialtiesExcluded);

                var enqueueResult = await _notificationOutboxService.EnqueueAsync(
                    new EnqueueNotificationRequest(
                        TenantId: tenantId,
                        AppointmentId: null,
                        Patient: selectedPatient,
                        Type: NotificationType.WaitingListOffer,
                        TemplateKey: DefaultNotificationTemplateCatalog.Keys.WaitingListOffer,
                        Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                        ScheduledFor: utcNow,
                        Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["patientFirstName"] = selectedPatient.FirstName,
                            ["clinicName"] = _notificationOptions.ClinicName,
                            ["specialtyDisplay"] = specialtyDisplay,
                            ["appointmentDate"] = appointmentDate,
                            ["appointmentTime"] = appointmentTime,
                            ["deadline"] = deadlineFormatted
                        },
                        CorrelationId: correlationId,
                        ForceImmediateDispatch: true,
                        WaitingListOfferId: offer.Id,
                        DeferPublishToCaller: true),
                    ct);

                if (enqueueResult.NotificationId.HasValue)
                {
                    offer.AttachNotification(enqueueResult.NotificationId.Value, utcNow);
                }

                try
                {
                    await _unitOfWork.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex, "UX_waiting_list_offers_active_block"))
                {
                    return new WaitingListBackfillAttemptResult("AlreadyPending");
                }

                if (enqueueResult.PendingDispatchEvent is not null)
                {
                    await _publishEndpoint.Publish(enqueueResult.PendingDispatchEvent, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                }

                _logger.LogInformation(
                    "Oferta de lista de espera {OfferId} (intento {Attempt}) enviada al paciente {PatientId} para bloque {BlockId}. Expira {ExpiresAt}.",
                    offer.Id,
                    offer.CascadeAttemptNumber,
                    offer.PatientId,
                    offer.ScheduleBlockId,
                    offer.ExpiresAt);

                return new WaitingListBackfillAttemptResult("Offered", offer.Id, selectedEntry.Id, selectedPatient.Id);
            },
            cancellationToken);
    }
}
