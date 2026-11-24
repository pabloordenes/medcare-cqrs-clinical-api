using System.Globalization;
using MassTransit;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Omro.Contracts.Events;

namespace MedCareOS.Application.Appointments.Risk;

public class OmroAnomalyPublisher : IOmroAnomalyPublisher
{
    private readonly IPatientRepository _patientRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OmroAnomalyPublisher> _logger;

    public OmroAnomalyPublisher(
        IPatientRepository patientRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        IAppointmentRepository appointmentRepository,
        IPublishEndpoint publishEndpoint,
        ILogger<OmroAnomalyPublisher> logger)
    {
        _patientRepository = patientRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _appointmentRepository = appointmentRepository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<bool> PublishAsync(
        PublishAnomalyContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var appointment = context.Appointment;

        if (appointment.OrchestrationSuppressed ||
            appointment.IsPatientConfirmed ||
            appointment.Status != AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "Emisión OMRO omitida para cita {AppointmentId} (AnomalyType={AnomalyType}): Status={Status}, ConfirmationState={ConfirmationState}, Suppressed={Suppressed}.",
                appointment.Id,
                context.AnomalyType,
                appointment.Status,
                appointment.ConfirmationState,
                appointment.OrchestrationSuppressed);
            return false;
        }

        var patient = context.Patient ?? await _patientRepository.GetPatientByIdAsync(appointment.PatientId, cancellationToken);
        var scheduleBlock = context.ScheduleBlock ?? await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, cancellationToken);
        var staff = context.Staff ?? (scheduleBlock is null
            ? null
            : await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken));

        var previousAbsences = await _appointmentRepository.GetNoShowCountByPatientId(appointment.PatientId, cancellationToken);

        var age = patient is null ? 0 : ComputeAge(patient.DateOfBirth, context.OccurredAtUtc.UtcDateTime);
        var specialty = string.IsNullOrWhiteSpace(staff?.Specialty)
            ? "desconocida"
            : staff.Specialty.Trim().ToLowerInvariant().Replace(" ", "_");
        var channel = patient?.ToPredictReminderChannel() ?? "none";

        decimal hoursRemaining = 0m;
        if (scheduleBlock is not null)
        {
            var startUtc = scheduleBlock.StartTime.Kind == DateTimeKind.Utc
                ? scheduleBlock.StartTime
                : DateTime.SpecifyKind(scheduleBlock.StartTime, DateTimeKind.Utc);
            var diffHours = (new DateTimeOffset(startUtc, TimeSpan.Zero) - context.OccurredAtUtc).TotalHours;
            hoursRemaining = diffHours <= 0d ? 0m : Math.Round((decimal)diffHours, 1, MidpointRounding.AwayFromZero);
        }

        var riskProbability = appointment.EffectiveRiskScore
            ?? appointment.CurrentBaseModelScore
            ?? RiskThresholds.ModerateRiskMin;

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["edad"] = age.ToString(CultureInfo.InvariantCulture),
            ["especialidad"] = specialty,
            ["canal"] = channel,
            ["horas_restantes"] = hoursRemaining.ToString("F1", CultureInfo.InvariantCulture),
            ["confirmation_state"] = appointment.ConfirmationState.ToString(),
            ["risk_band"] = appointment.EffectiveBand?.ToLabel() ?? appointment.RiskBand ?? "Desconocido",
            ["previous_band"] = context.PreviousBand?.ToLabel() ?? "Ninguno",
            ["model_score"] = (appointment.CurrentBaseModelScore ?? riskProbability).ToString("F4", CultureInfo.InvariantCulture),
            ["effective_score"] = riskProbability.ToString("F4", CultureInfo.InvariantCulture),
            ["scoring_status"] = appointment.ScoringStatus.ToString(),
            ["previous_absences"] = previousAbsences.ToString(CultureInfo.InvariantCulture),
            ["trigger_source"] = string.IsNullOrWhiteSpace(context.TriggerSource) ? "system" : context.TriggerSource.Trim()
        };

        await _publishEndpoint.Publish(
            new AnomalyDetected(
                TenantId: appointment.TenantId,
                CorrelationId: context.CorrelationId,
                AnomalyType: context.AnomalyType,
                ResourceId: appointment.Id,
                RiskProbability: riskProbability,
                Attributes: attributes),
            cancellationToken);

        _logger.LogInformation(
            "AnomalyDetected emitido a OMRO: AppointmentId={AppointmentId}, AnomalyType={AnomalyType}, RiskProbability={RiskProbability}, PreviousBand={PreviousBand}, EffectiveBand={EffectiveBand}, TriggerSource={TriggerSource}.",
            appointment.Id,
            context.AnomalyType,
            riskProbability,
            context.PreviousBand?.ToLabel() ?? "Ninguno",
            appointment.EffectiveBand?.ToLabel() ?? "Ninguno",
            context.TriggerSource);

        return true;
    }

    private static int ComputeAge(DateOnly dateOfBirth, DateTime referenceUtc)
    {
        var today = DateOnly.FromDateTime(referenceUtc);
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
            age--;

        return Math.Max(0, age);
    }
}
