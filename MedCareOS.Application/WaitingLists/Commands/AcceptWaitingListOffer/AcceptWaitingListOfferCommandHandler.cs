using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;

public class AcceptWaitingListOfferCommandHandler : IRequestHandler<AcceptWaitingListOfferCommand, AcceptWaitingListOfferResult>
{
    private readonly IWaitingListOfferRepository _offerRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IPatientActionTokenRepository _tokenRepository;
    private readonly IPatientActionTokenService _tokenService;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IApplicationDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<AcceptWaitingListOfferCommandHandler> _logger;

    public AcceptWaitingListOfferCommandHandler(
        IWaitingListOfferRepository offerRepository,
        IWaitingListRepository waitingListRepository,
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IPatientRepository patientRepository,
        IStaffRepository staffRepository,
        IBoxRepository boxRepository,
        IPatientActionTokenRepository tokenRepository,
        IPatientActionTokenService tokenService,
        ITenantConfirmationPolicyRepository policyRepository,
        INotificationOutboxService notificationOutboxService,
        ITransactionalExecutor transactionalExecutor,
        IApplicationDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<AcceptWaitingListOfferCommandHandler> logger)
    {
        _offerRepository = offerRepository;
        _waitingListRepository = waitingListRepository;
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _patientRepository = patientRepository;
        _staffRepository = staffRepository;
        _boxRepository = boxRepository;
        _tokenRepository = tokenRepository;
        _tokenService = tokenService;
        _policyRepository = policyRepository;
        _notificationOutboxService = notificationOutboxService;
        _transactionalExecutor = transactionalExecutor;
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<AcceptWaitingListOfferResult> Handle(
        AcceptWaitingListOfferCommand request,
        CancellationToken cancellationToken)
    {
        Guid offerId;
        Guid? resolvedPatientId = null;

        if (!string.IsNullOrWhiteSpace(request.RawToken))
        {
            var consumed = await _tokenService.ConsumeAsync(request.RawToken, PatientReplyIntent.Confirm, cancellationToken);
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
            throw new UnauthorizedAccessException("No tienes permiso para aceptar una oferta de otro paciente.");

        var lockKey = initialOffer.SourceAppointmentId.HasValue
            ? $"overbook-source:{initialOffer.SourceAppointmentId.Value:N}"
            : $"block:{initialOffer.ScheduleBlockId:N}";

        return await _transactionalExecutor.ExecuteAsync(
            lockKey,
            async ct =>
            {
                var utcNow = DateTimeOffset.UtcNow;
                var offer = await _offerRepository.GetByIdAsync(offerId, ct)
                    ?? throw new KeyNotFoundException($"La oferta {offerId} no existe.");

                if (resolvedPatientId.HasValue && offer.PatientId != resolvedPatientId.Value)
                    throw new UnauthorizedAccessException("No tienes permiso para aceptar una oferta de otro paciente.");

                if (offer.Status == WaitingListOfferStatus.Accepted && offer.ResultAppointmentId.HasValue)
                {
                    return new AcceptWaitingListOfferResult(
                        IsSuccess: true,
                        OfferId: offer.Id,
                        AppointmentId: offer.ResultAppointmentId,
                        PatientId: offer.PatientId,
                        OfferStatus: offer.Status,
                        WasIdempotentReplay: true,
                        Message: "Oferta ya aceptada anteriormente.");
                }

                var waitingListEntry = await _waitingListRepository.GetByIdAsync(offer.WaitingListId, ct);

                if (offer.Status != WaitingListOfferStatus.Pending)
                {
                    var msg = offer.Status == WaitingListOfferStatus.CancelledByOriginalConfirmation
                        ? "El cupo fue desactivado porque el paciente original confirmó su asistencia."
                        : $"La oferta ya no está disponible (estado actual: '{offer.Status}').";
                    throw new InvalidOperationException(msg);
                }

                if (offer.ExpiresAt <= utcNow)
                {
                    offer.Expire(utcNow);
                    await _tokenRepository.RevokeActiveForWaitingListOfferAsync(offer.Id, utcNow, ct);
                    if (waitingListEntry?.Status == WaitingListStatus.Offered)
                        waitingListEntry.ReturnToActive(utcNow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    throw new InvalidOperationException("La oferta expiró antes de ser aceptada.");
                }

                var block = await _scheduleBlockRepository.GetByIdForUpdateAsync(offer.ScheduleBlockId, ct)
                    ?? await _scheduleBlockRepository.GetByIdAsync(offer.ScheduleBlockId, ct)
                    ?? throw new InvalidOperationException("El bloque horario asociado ya no existe.");

                if (block.StartTime <= utcNow.UtcDateTime)
                    throw new InvalidOperationException("El bloque horario ya comenzó o expiró.");

                var box = await _boxRepository.GetByIdAsync(block.BoxId, ct);
                if (box is null || !box.IsActive)
                    throw new InvalidOperationException("El box asignado al horario ya no está activo.");

                if (block.IsOverbook && !block.IsAvailableForBackfillOffer)
                {
                    offer.CancelByOriginalConfirmation("OverbookNoLongerAvailable", utcNow);
                    if (waitingListEntry?.Status == WaitingListStatus.Offered)
                        waitingListEntry.ReturnToActive(utcNow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    throw new InvalidOperationException("El bloque de sobrecupo ya no se encuentra disponible.");
                }

                if (await _appointmentRepository.HasActiveAppointmentForBlockAsync(block.Id, ct))
                {
                    offer.Supersede("BlockAlreadyOccupied", utcNow);
                    await _tokenRepository.RevokeActiveForWaitingListOfferAsync(offer.Id, utcNow, ct);
                    if (waitingListEntry?.Status == WaitingListStatus.Offered)
                        waitingListEntry.ReturnToActive(utcNow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    throw new DuplicateAppointmentException("El cupo horario ya fue asignado a otro paciente.");
                }

                var patient = await _patientRepository.GetPatientByIdAsync(offer.PatientId, ct)
                    ?? throw new KeyNotFoundException($"El paciente {offer.PatientId} no existe.");

                var staff = await _staffRepository.GetByIdAsync(block.StaffId, ct)
                    ?? throw new KeyNotFoundException($"El profesional {block.StaffId} no existe.");

                var symptomsRaw = !string.IsNullOrWhiteSpace(request.SymptomsRaw)
                    ? request.SymptomsRaw.Trim()
                    : "Asignación desde lista de espera";

                var newAppointment = Appointment.CreateFromWaitingListOffer(
                    scheduleBlockId: block.Id,
                    patientId: patient.Id,
                    waitingListOfferId: offer.Id,
                    offerAcceptedAtUtc: utcNow,
                    symptomsRaw: symptomsRaw,
                    schedulingSource: string.IsNullOrWhiteSpace(request.Channel) ? "lista_espera" : $"lista_espera_{request.Channel}",
                    tenantId: offer.TenantId,
                    confirmImmediately: true);

                var correlationId = Guid.NewGuid();
                await _appointmentRepository.AddAppointmentAsync(newAppointment, ct);
                offer.Accept(newAppointment.Id, utcNow);
                waitingListEntry?.MarkAccepted(utcNow);
                await _tokenRepository.RevokeActiveForWaitingListOfferAsync(offer.Id, utcNow, ct);

                if (block.IsOverbook)
                {
                    block.MarkOverbookOccupied(newAppointment.Id, utcNow);
                }

                await _publishEndpoint.Publish(
                    new AppointmentCreated(
                        AppointmentId: newAppointment.Id,
                        OccurredAtUtc: utcNow,
                        CorrelationId: correlationId),
                    ct);

                var policy = await _policyRepository.GetOrDefaultAsync(offer.TenantId, ct);
                var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
                    block.StartTime,
                    policy.TimeZoneId);
                var specialtyDisplay = NotificationFormattingHelper.ResolveSpecialtyDisplay(
                    staff.Specialty,
                    _notificationOptions.IncludeSpecialtyInMessages,
                    _notificationOptions.SensitiveSpecialtiesExcluded);

                var enqueueResult = await _notificationOutboxService.EnqueueAsync(
                    new EnqueueNotificationRequest(
                        TenantId: offer.TenantId,
                        AppointmentId: newAppointment.Id,
                        Patient: patient,
                        Type: NotificationType.AppointmentCreated,
                        TemplateKey: DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
                        Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                        ScheduledFor: utcNow,
                        Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["patientFirstName"] = patient.FirstName,
                            ["clinicName"] = _notificationOptions.ClinicName,
                            ["appointmentDate"] = appointmentDate,
                            ["appointmentTime"] = appointmentTime,
                            ["specialtyDisplay"] = specialtyDisplay
                        },
                        CorrelationId: correlationId,
                        ForceImmediateDispatch: true,
                        DeferPublishToCaller: true),
                    ct);

                try
                {
                    await _unitOfWork.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex, "IX_citas_schedule_block_id"))
                {
                    // Garantizar que si falla el commit por colisión de bloque, el candidato vuelva a Active sin perder antigüedad
                    if (_dbContext is DbContext efContext)
                    {
                        efContext.ChangeTracker.Clear();
                    }

                    var reloadedOffer = await _offerRepository.GetByIdAsync(offer.Id, ct);
                    var reloadedEntry = await _waitingListRepository.GetByIdAsync(offer.WaitingListId, ct);
                    if (reloadedOffer is not null && reloadedOffer.Status == WaitingListOfferStatus.Pending)
                    {
                        reloadedOffer.Supersede("ConcurrentAppointmentCreated", utcNow);
                    }
                    if (reloadedEntry is not null && reloadedEntry.Status == WaitingListStatus.Offered)
                    {
                        reloadedEntry.ReturnToActive(utcNow);
                    }
                    await _unitOfWork.SaveChangesAsync(ct);

                    throw new DuplicateAppointmentException("El cupo horario ya fue asignado concurrentemente a otro paciente.");
                }

                if (enqueueResult.PendingDispatchEvent is not null)
                {
                    await _publishEndpoint.Publish(enqueueResult.PendingDispatchEvent, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                }

                _logger.LogInformation(
                    "Oferta {OfferId} aceptada por paciente {PatientId}. Cita {AppointmentId} creada en bloque {BlockId} (OfferAcceptedAt={AcceptedAt}).",
                    offer.Id,
                    offer.PatientId,
                    newAppointment.Id,
                    block.Id,
                    utcNow);

                return new AcceptWaitingListOfferResult(
                    IsSuccess: true,
                    OfferId: offer.Id,
                    AppointmentId: newAppointment.Id,
                    PatientId: offer.PatientId,
                    OfferStatus: offer.Status,
                    WasIdempotentReplay: false,
                    Message: "Cupo aceptado y cita confirmada exitosamente.");
            },
            cancellationToken);
    }
}
