using MassTransit;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.WaitingLists.Services;

/// <summary>
/// Cancela ofertas pendientes cuando el paciente original confirma tarde o libera su cita antes de que el sobrecupo se ocupe.
/// Participa en la unidad de trabajo transaccional del llamador (sin llamar a SaveChangesAsync por separado).
/// </summary>
public class WaitingListOfferCancellationService : IWaitingListOfferCancellationService
{
    private readonly IWaitingListOfferRepository _offerRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IPatientActionTokenRepository _tokenRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<WaitingListOfferCancellationService> _logger;

    public WaitingListOfferCancellationService(
        IWaitingListOfferRepository offerRepository,
        IWaitingListRepository waitingListRepository,
        IPatientActionTokenRepository tokenRepository,
        IPatientRepository patientRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        INotificationOutboxService notificationOutboxService,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<WaitingListOfferCancellationService> logger)
    {
        _offerRepository = offerRepository;
        _waitingListRepository = waitingListRepository;
        _tokenRepository = tokenRepository;
        _patientRepository = patientRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _policyRepository = policyRepository;
        _notificationOutboxService = notificationOutboxService;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<int> CancelPendingOffersForOverbookUndoAsync(
        Guid overbookBlockId,
        string reason,
        DateTimeOffset utcNow,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var offer = await _offerRepository.GetPendingByScheduleBlockIdAsync(overbookBlockId, cancellationToken);
        if (offer is null)
            return 0;

        offer.CancelByOriginalConfirmation(reason, utcNow);
        await _tokenRepository.RevokeActiveForWaitingListOfferAsync(offer.Id, utcNow, cancellationToken);

        var waitingListEntry = await _waitingListRepository.GetByIdAsync(offer.WaitingListId, cancellationToken);
        if (waitingListEntry is not null && waitingListEntry.Status == WaitingListStatus.Offered)
        {
            waitingListEntry.ReturnToActive(utcNow);
        }

        var patient = await _patientRepository.GetPatientByIdAsync(offer.PatientId, cancellationToken);
        var block = await _scheduleBlockRepository.GetByIdAsync(offer.ScheduleBlockId, cancellationToken);
        if (patient is not null && block is not null)
        {
            var policy = await _policyRepository.GetOrDefaultAsync(offer.TenantId, cancellationToken);
            var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
                block.StartTime,
                policy.TimeZoneId);

            var enqueueResult = await _notificationOutboxService.EnqueueAsync(
                new EnqueueNotificationRequest(
                    TenantId: offer.TenantId,
                    AppointmentId: null,
                    Patient: patient,
                    Type: NotificationType.OverbookOfferCancelled,
                    TemplateKey: DefaultNotificationTemplateCatalog.Keys.OverbookOfferCancelled,
                    Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                    ScheduledFor: utcNow,
                    Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["patientFirstName"] = patient.FirstName,
                        ["appointmentDate"] = appointmentDate,
                        ["appointmentTime"] = appointmentTime,
                        ["clinicName"] = _notificationOptions.ClinicName
                    },
                    CorrelationId: correlationId,
                    ForceImmediateDispatch: true,
                    WaitingListOfferId: offer.Id,
                    DeferPublishToCaller: true),
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (enqueueResult.PendingDispatchEvent is not null)
            {
                await _publishEndpoint.Publish(enqueueResult.PendingDispatchEvent, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Oferta {OfferId} cancelada por deshacer sobrecupo ({Reason}). Candidato {PatientId} restituido a Active y disculpa encolada.",
            offer.Id,
            reason,
            offer.PatientId);

        return 1;
    }
}
