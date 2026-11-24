using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Appointments.Commands.CreateAppointment;

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Guid>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly NotificationOptions _notificationOptions;

    public CreateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        IPublishEndpoint publishEndpoint,
        INotificationOutboxService notificationOutboxService,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IOptions<NotificationOptions> notificationOptions)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _publishEndpoint = publishEndpoint;
        _notificationOutboxService = notificationOutboxService;
        _lifecyclePlanner = lifecyclePlanner;
        _notificationOptions = notificationOptions.Value;
    }

    public async Task<Guid> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        Guid effectivePatientId = request.PatientId;
        Patient? patient;

        if (request.AuthenticatedUserId.HasValue)
        {
            patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken);
            if (patient == null)
                throw new InvalidOperationException("El paciente autenticado no tiene un perfil registrado.");

            effectivePatientId = patient.Id;
        }
        else
        {
            patient = await _patientRepository.GetPatientByIdAsync(effectivePatientId, cancellationToken);
            if (patient == null) throw new InvalidOperationException("El paciente no existe.");
        }

        var scheduleBlock = await _scheduleBlockRepository.GetByIdAsync(request.ScheduleBlockId, cancellationToken);
        if (scheduleBlock == null) throw new InvalidOperationException("El bloque horario no existe.");

        var blockOccupied = await _appointmentRepository.HasActiveAppointmentForBlockAsync(request.ScheduleBlockId, cancellationToken);
        if (blockOccupied)
            throw new DuplicateAppointmentException("El bloque horario seleccionado ya se encuentra reservado.");

        var staff = await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken);
        if (staff == null) throw new InvalidOperationException("El staff no existe.");

        var newAppointment = Appointment.Create(
            request.ScheduleBlockId,
            effectivePatientId,
            request.SymptomsRaw,
            request.SchedulingSource);

        var correlationId = Guid.NewGuid();
        var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
            scheduleBlock.StartTime,
            _notificationOptions.TimeZoneId);
        var specialtyDisplay = NotificationFormattingHelper.ResolveSpecialtyDisplay(
            staff.Specialty,
            _notificationOptions.IncludeSpecialtyInMessages,
            _notificationOptions.SensitiveSpecialtiesExcluded);

        // Persistencia atómica: cita + AppointmentCreated + notificación T0 en la misma transacción vía Bus Outbox
        await _appointmentRepository.AddAppointmentAsync(newAppointment, cancellationToken);
        await _publishEndpoint.Publish(
            new AppointmentCreated(
                AppointmentId: newAppointment.Id,
                OccurredAtUtc: newAppointment.CreatedAt,
                CorrelationId: correlationId),
            cancellationToken);

        await _notificationOutboxService.EnqueueAsync(
            new EnqueueNotificationRequest(
                TenantId: RiskThresholds.DefaultTenantId,
                AppointmentId: newAppointment.Id,
                Patient: patient,
                Type: NotificationType.AppointmentCreated,
                TemplateKey: DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
                Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                ScheduledFor: newAppointment.CreatedAt,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["patientFirstName"] = patient.FirstName,
                    ["clinicName"] = _notificationOptions.ClinicName,
                    ["appointmentDate"] = appointmentDate,
                    ["appointmentTime"] = appointmentTime,
                    ["specialtyDisplay"] = specialtyDisplay
                },
                CorrelationId: correlationId),
            cancellationToken);

        await _lifecyclePlanner.ScheduleForAppointmentAsync(
            newAppointment,
            scheduleBlock,
            correlationId,
            newAppointment.CreatedAt,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return newAppointment.Id;
    }
}
