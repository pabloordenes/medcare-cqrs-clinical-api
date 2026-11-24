using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;

namespace MedCareOS.Domain.Entities;

public class Appointment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = RiskThresholds.DefaultTenantId;
    public Guid ScheduleBlockId { get; private set; }
    public Guid PatientId { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public ConfirmationState ConfirmationState { get; private set; } = ConfirmationState.Pending;
    public bool OrchestrationSuppressed { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? FinalConfirmationRequestedAt { get; private set; }
    public DateTimeOffset? DeadlineMissedAt { get; private set; }
    public ConfirmationAttendanceOutcome? ConfirmationOutcome { get; private set; }
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

    public bool IsPatientConfirmed =>
        ConfirmationState is ConfirmationState.ConfirmedEarly or ConfirmationState.FinalConfirmed;

    private Appointment() { } // ef

    private Appointment(Guid id,
        Guid tenantId,
        Guid scheduleBlockId, Guid patientId,
        AppointmentStatus status, string symptomsRaw,
        string? processedSymptoms, string? nlpSummary,
        decimal? noShowRiskScore, string? riskBand,
        ScoringStatus scoringStatus, NlpStatus nlpStatus,
        string? schedulingSource, bool? actualAttendance)
    {
        Id = id;
        TenantId = tenantId;
        ScheduleBlockId = scheduleBlockId;
        PatientId = patientId;
        Status = status;
        ConfirmationState = ConfirmationState.Pending;
        OrchestrationSuppressed = false;
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

    public static Appointment Create(
        Guid scheduleBlockId,
        Guid patientId,
        string symptomsRaw,
        string schedulingSource = "paciente",
        Guid? tenantId = null)
    {
        if (patientId == Guid.Empty)
            throw new InvalidOperationException("Debes incluir un paciente.");

        if (scheduleBlockId == Guid.Empty)
            throw new InvalidOperationException("Debes incluir un bloque horario.");

        if (tenantId.HasValue && tenantId.Value == Guid.Empty)
            throw new ArgumentException("El TenantId no puede estar vacío.", nameof(tenantId));

        var resolvedTenantId = tenantId ?? RiskThresholds.DefaultTenantId;
        var normalizedSymptoms = symptomsRaw ?? string.Empty;
        var initialNlpStatus = string.IsNullOrWhiteSpace(normalizedSymptoms)
            ? NlpStatus.Skipped
            : NlpStatus.Pending;

        return new Appointment(
            Guid.NewGuid(),
            resolvedTenantId,
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

        if (ConfirmationState == ConfirmationState.DeclinedByPatient)
            throw new InvalidOperationException("No se puede confirmar una cita declinada por el paciente.");

        var utcNow = DateTimeOffset.UtcNow;

        if (ConfirmationState == ConfirmationState.Pending)
        {
            ConfirmationState = ConfirmationState.ConfirmedEarly;
        }
        else if (ConfirmationState is ConfirmationState.FinalRequested or ConfirmationState.Unresponsive)
        {
            ConfirmationState = ConfirmationState.FinalConfirmed;
        }

        Status = AppointmentStatus.Confirmed;
        OrchestrationSuppressed = true;
        ConfirmedAt ??= utcNow;
        UpdatedAt = utcNow;
    }

    public ConfirmationTransitionResult RegisterPatientConfirmation(DateTimeOffset utcNow)
    {
        if (ConfirmationState == ConfirmationState.DeclinedByPatient ||
            Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Finished or AppointmentStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"No se puede confirmar una cita en estado clínico '{Status}' y estado de confirmación '{ConfirmationState}'.");
        }

        if (IsPatientConfirmed)
        {
            return new ConfirmationTransitionResult(
                ConfirmationTransitionOutcome.AlreadyConfirmed,
                ConfirmationState,
                ConfirmationState,
                StateChanged: false,
                ShouldSupersedeHitlTasks: false,
                ShouldCancelPendingScheduledActions: false);
        }

        var previous = ConfirmationState;
        var next = previous is ConfirmationState.FinalRequested or ConfirmationState.Unresponsive
            ? ConfirmationState.FinalConfirmed
            : ConfirmationState.ConfirmedEarly;
        var outcome = next == ConfirmationState.ConfirmedEarly
            ? ConfirmationTransitionOutcome.ConfirmedEarly
            : ConfirmationTransitionOutcome.FinalConfirmed;

        ConfirmationState = next;
        if (Status == AppointmentStatus.Scheduled)
        {
            Status = AppointmentStatus.Confirmed;
        }

        OrchestrationSuppressed = true;
        ConfirmedAt ??= utcNow;
        UpdatedAt = utcNow;

        return new ConfirmationTransitionResult(
            outcome,
            previous,
            next,
            StateChanged: true,
            ShouldSupersedeHitlTasks: true,
            ShouldCancelPendingScheduledActions: true);
    }

    public ConfirmationTransitionResult MarkFinalConfirmationRequested(DateTimeOffset utcNow)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            throw new InvalidOperationException(
                $"No se puede solicitar confirmación final para una cita en estado clínico '{Status}'.");
        }

        if (ConfirmationState == ConfirmationState.FinalRequested)
        {
            return new ConfirmationTransitionResult(
                ConfirmationTransitionOutcome.AlreadyFinalRequested,
                ConfirmationState.FinalRequested,
                ConfirmationState.FinalRequested,
                StateChanged: false,
                ShouldSupersedeHitlTasks: false,
                ShouldCancelPendingScheduledActions: false);
        }

        if (ConfirmationState != ConfirmationState.Pending)
        {
            throw new InvalidOperationException(
                $"No se puede solicitar confirmación final desde el estado de confirmación '{ConfirmationState}'.");
        }

        ConfirmationState = ConfirmationState.FinalRequested;
        FinalConfirmationRequestedAt ??= utcNow;
        UpdatedAt = utcNow;

        return new ConfirmationTransitionResult(
            ConfirmationTransitionOutcome.FinalRequested,
            ConfirmationState.Pending,
            ConfirmationState.FinalRequested,
            StateChanged: true,
            ShouldSupersedeHitlTasks: false,
            ShouldCancelPendingScheduledActions: false);
    }

    public ConfirmationTransitionResult MarkUnresponsive(DateTimeOffset utcNow)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            throw new InvalidOperationException(
                $"No se puede marcar sin respuesta una cita en estado clínico '{Status}'.");
        }

        if (ConfirmationState == ConfirmationState.Unresponsive)
        {
            return new ConfirmationTransitionResult(
                ConfirmationTransitionOutcome.AlreadyUnresponsive,
                ConfirmationState.Unresponsive,
                ConfirmationState.Unresponsive,
                StateChanged: false,
                ShouldSupersedeHitlTasks: false,
                ShouldCancelPendingScheduledActions: false);
        }

        if (ConfirmationState is not (ConfirmationState.Pending or ConfirmationState.FinalRequested))
        {
            throw new InvalidOperationException(
                $"No se puede marcar sin respuesta una cita en estado de confirmación '{ConfirmationState}'.");
        }

        var previous = ConfirmationState;
        ConfirmationState = ConfirmationState.Unresponsive;
        DeadlineMissedAt ??= utcNow;
        UpdatedAt = utcNow;

        return new ConfirmationTransitionResult(
            ConfirmationTransitionOutcome.MarkedUnresponsive,
            previous,
            ConfirmationState.Unresponsive,
            StateChanged: true,
            ShouldSupersedeHitlTasks: false,
            ShouldCancelPendingScheduledActions: false);
    }

    public ConfirmationTransitionResult DeclineByPatient(string? reason, DateTimeOffset utcNow)
    {
        if (ConfirmationState == ConfirmationState.DeclinedByPatient && Status == AppointmentStatus.Cancelled)
        {
            return new ConfirmationTransitionResult(
                ConfirmationTransitionOutcome.AlreadyDeclined,
                ConfirmationState.DeclinedByPatient,
                ConfirmationState.DeclinedByPatient,
                StateChanged: false,
                ShouldSupersedeHitlTasks: false,
                ShouldCancelPendingScheduledActions: false);
        }

        if (Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed))
        {
            throw new InvalidOperationException(
                $"No se puede declinar una cita en estado clínico '{Status}'.");
        }

        var previous = ConfirmationState;
        ConfirmationState = ConfirmationState.DeclinedByPatient;
        Cancel(string.IsNullOrWhiteSpace(reason) ? "Declinada por el paciente." : reason);
        UpdatedAt = utcNow;

        return new ConfirmationTransitionResult(
            ConfirmationTransitionOutcome.DeclinedByPatient,
            previous,
            ConfirmationState.DeclinedByPatient,
            StateChanged: true,
            ShouldSupersedeHitlTasks: true,
            ShouldCancelPendingScheduledActions: true);
    }

    public void CheckIn()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede registrar check-in para una cita en estado '{Status}'.");

        Status = AppointmentStatus.Confirmed;
        ActualAttendance = true;
        OrchestrationSuppressed = true;
        UpdateConfirmationOutcome(attended: true);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void StartConsultation()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException("No puedes iniciar una cita pasada o cancelada.");

        Status = AppointmentStatus.InProgress;
        ActualAttendance = true;
        OrchestrationSuppressed = true;
        UpdateConfirmationOutcome(attended: true);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void FinishConsultation()
    {
        if (Status is not AppointmentStatus.InProgress)
            throw new InvalidOperationException("No puedes finalizar una cita que no este en curso.");

        Status = AppointmentStatus.Finished;
        ActualAttendance = true;
        OrchestrationSuppressed = true;
        UpdateConfirmationOutcome(attended: true);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede cancelar una cita en estado '{Status}'.");

        Status = AppointmentStatus.Cancelled;
        OrchestrationSuppressed = true;
        NextScoringRetryAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNoShow()
    {
        if (Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
            throw new InvalidOperationException($"No se puede marcar inasistencia (NoShow) en una cita en estado '{Status}'.");

        Status = AppointmentStatus.NoShow;
        ActualAttendance = false;
        OrchestrationSuppressed = true;
        UpdateConfirmationOutcome(attended: false);
        NextScoringRetryAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void UpdateConfirmationOutcome(bool attended)
    {
        ConfirmationOutcome = (IsPatientConfirmed, attended) switch
        {
            (true, true) => ConfirmationAttendanceOutcome.ConfirmedAndAttended,
            (true, false) => ConfirmationAttendanceOutcome.ConfirmedButNoShow,
            (false, true) => ConfirmationAttendanceOutcome.UnconfirmedAndAttended,
            (false, false) => ConfirmationAttendanceOutcome.UnconfirmedAndNoShow
        };
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
