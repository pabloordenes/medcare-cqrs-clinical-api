using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class Appointment
{
    public Guid Id { get; private set; }
    public Guid ScheduleBlockId { get; private set; }
    public Guid PatientId { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string SymptomsRaw { get; private set; } = string.Empty;
    public string? ProcessedSymptoms { get; private set; }
    public string? NlpSummary { get; private set; }
    public decimal NoShowRiskScore { get; private set; }
    public string? RiskBand { get; private set; }
    public string? SchedulingSource { get; private set; }
    public bool? ActualAttendance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Appointment() { } // ef

    private Appointment(Guid id,
        Guid scheduleBlockId, Guid patientId,
        AppointmentStatus status, string symptomsRaw,
        string? processedSymptoms, string? nlpSummary,
        decimal noShowRiskScore, string? riskBand, string? schedulingSource,
        bool? actualAttendance)
    {
        Id = id;
        ScheduleBlockId = scheduleBlockId;
        PatientId = patientId;
        Status = status;
        SymptomsRaw = symptomsRaw;
        ProcessedSymptoms = processedSymptoms;
        NlpSummary = nlpSummary;
        NoShowRiskScore = noShowRiskScore;
        RiskBand = riskBand;
        SchedulingSource = schedulingSource;
        ActualAttendance = actualAttendance;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static Appointment Create(Guid scheduleBlockId, Guid patientId, string symptomsRaw, string schedulingSource = "paciente")
    {
        if (patientId == Guid.Empty)
            throw new InvalidOperationException("Debes incluir un paciente.");

        if (scheduleBlockId == Guid.Empty)
            throw new InvalidOperationException("Debes incluir un bloque horario.");

        return new Appointment(
            Guid.NewGuid(),
            scheduleBlockId,
            patientId,
            AppointmentStatus.Scheduled,
            symptomsRaw,
            null,
            null,
            0,
            null,
            schedulingSource,
            null);
    }

    public void Confirm()
    {
        if (Status != AppointmentStatus.Scheduled)
            throw new InvalidOperationException($"No se puede confirmar una cita en estado '{Status}'.");

        Status = AppointmentStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CheckIn()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede registrar check-in para una cita en estado '{Status}'.");

        Status = AppointmentStatus.Confirmed;
        ActualAttendance = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void StartConsultation()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException("No puedes iniciar una cita pasada o cancelada.");

        Status = AppointmentStatus.InProgress;
        ActualAttendance = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void FinishConsultation()
    {
        if (Status is not AppointmentStatus.InProgress)
            throw new InvalidOperationException("No puedes finalizar una cita que no este en curso.");

        Status = AppointmentStatus.Finished;
        ActualAttendance = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede cancelar una cita en estado '{Status}'.");

        Status = AppointmentStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNoShow()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede marcar inasistencia (NoShow) en una cita en estado '{Status}'.");

        Status = AppointmentStatus.NoShow;
        ActualAttendance = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateRiskScore(decimal noShowRiskScore, string band)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No se puede actualizar el riesgo en una cita cancelada.");

        NoShowRiskScore = noShowRiskScore;
        RiskBand = band;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyNlpAnalysis(string summary, string? processedSymptoms)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No puedes analizar una cita cancelada o con inasistencia.");

        NlpSummary = summary;
        ProcessedSymptoms = processedSymptoms;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
