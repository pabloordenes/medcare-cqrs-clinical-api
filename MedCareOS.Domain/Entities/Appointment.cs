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
    public decimal? NoShowRiskScore { get; private set; }
    public string? RiskBand { get; private set; }
    public ScoringStatus ScoringStatus { get; private set; }
    public NlpStatus NlpStatus { get; private set; }
    public int ScoringAttempts { get; private set; }
    public DateTimeOffset? NextScoringRetryAt { get; private set; }
    public string? LastScoringError { get; private set; }
    public DateTimeOffset? ScoredAt { get; private set; }
    public string? SchedulingSource { get; private set; }
    public bool? ActualAttendance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Appointment() { } // ef

    private Appointment(Guid id,
        Guid scheduleBlockId, Guid patientId,
        AppointmentStatus status, string symptomsRaw,
        string? processedSymptoms, string? nlpSummary,
        decimal? noShowRiskScore, string? riskBand,
        ScoringStatus scoringStatus, NlpStatus nlpStatus,
        string? schedulingSource, bool? actualAttendance)
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
        ScoringStatus = scoringStatus;
        NlpStatus = nlpStatus;
        ScoringAttempts = 0;
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

        var normalizedSymptoms = symptomsRaw ?? string.Empty;
        var initialNlpStatus = string.IsNullOrWhiteSpace(normalizedSymptoms)
            ? NlpStatus.Skipped
            : NlpStatus.Pending;

        return new Appointment(
            Guid.NewGuid(),
            scheduleBlockId,
            patientId,
            AppointmentStatus.Scheduled,
            normalizedSymptoms,
            null,
            null,
            null,
            null,
            ScoringStatus.Pending,
            initialNlpStatus,
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
        NextScoringRetryAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNoShow()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede marcar inasistencia (NoShow) en una cita en estado '{Status}'.");

        Status = AppointmentStatus.NoShow;
        ActualAttendance = false;
        NextScoringRetryAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateRiskScore(decimal noShowRiskScore, string band)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No se puede actualizar el riesgo en una cita cancelada.");

        if (noShowRiskScore is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(noShowRiskScore), "El score de riesgo debe estar entre 0 y 1.");

        if (string.IsNullOrWhiteSpace(band) || band.Trim().Equals("Pendiente", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La banda de riesgo no puede ser vacía ni 'Pendiente'.", nameof(band));

        var utcNow = DateTimeOffset.UtcNow;
        NoShowRiskScore = noShowRiskScore;
        RiskBand = band.Trim();
        ScoringStatus = ScoringStatus.Scored;
        ScoredAt = utcNow;
        if (NlpStatus != NlpStatus.Failed)
        {
            NextScoringRetryAt = null;
            LastScoringError = null;
        }
        UpdatedAt = utcNow;
    }

    public void ApplyNlpAnalysis(string summary, string? processedSymptoms)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No puedes analizar una cita cancelada o con inasistencia.");

        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("El resumen NLP no puede estar vacío.", nameof(summary));

        NlpSummary = summary.Trim();
        ProcessedSymptoms = processedSymptoms;
        NlpStatus = NlpStatus.Completed;
        if (ScoringStatus == ScoringStatus.Scored)
        {
            NextScoringRetryAt = null;
            LastScoringError = null;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordScoringFailure(
        string error,
        int maxAttempts,
        DateTimeOffset utcNow,
        TimeSpan nextBackoff,
        bool scoringFailed = true,
        bool nlpFailed = false)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No se puede registrar fallo de scoring en una cita cancelada o con inasistencia.");

        ScoringAttempts++;
        if (scoringFailed)
        {
            ScoringStatus = ScoringStatus.Failed;
        }

        if (nlpFailed)
        {
            NlpStatus = NlpStatus.Failed;
        }

        LastScoringError = TruncateError(error);
        NextScoringRetryAt = ScoringAttempts < maxAttempts ? utcNow.Add(nextBackoff) : null;
        UpdatedAt = utcNow;
    }

    public void MarkScoringTerminalFailure(
        string error,
        DateTimeOffset utcNow,
        bool scoringFailed = true,
        bool nlpFailed = false)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No se puede registrar fallo terminal de scoring en una cita cancelada o con inasistencia.");

        if (scoringFailed)
        {
            ScoringStatus = ScoringStatus.Failed;
        }

        if (nlpFailed)
        {
            NlpStatus = NlpStatus.Failed;
        }

        LastScoringError = TruncateError(error);
        NextScoringRetryAt = null;
        UpdatedAt = utcNow;
    }

    public void ScheduleScoringRetryLease(DateTimeOffset leaseUntil)
    {
        NextScoringRetryAt = leaseUntil;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string TruncateError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Error desconocido en servicio de IA.";

        var trimmed = error.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
