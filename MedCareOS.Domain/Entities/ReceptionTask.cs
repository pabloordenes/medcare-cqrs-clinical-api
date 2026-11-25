using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class ReceptionTask
{
    public const int MaxReasonLength = 500;
    public const int MaxResolutionNotesLength = 1000;

    public Guid Id { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid PatientId { get; private set; }
    public ReceptionTaskType TaskType { get; private set; }
    public ReceptionTaskPriority Priority { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public ReceptionTaskStatus Status { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private ReceptionTask() { } // ef

    public static ReceptionTask Create(
        Guid appointmentId,
        Guid patientId,
        ReceptionTaskType taskType,
        ReceptionTaskPriority priority,
        string reason)
    {
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("El AppointmentId no puede estar vacío.", nameof(appointmentId));

        if (patientId == Guid.Empty)
            throw new ArgumentException("El PatientId no puede estar vacío.", nameof(patientId));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("El motivo de la tarea no puede estar vacío.", nameof(reason));

        var utcNow = DateTimeOffset.UtcNow;

        return new ReceptionTask
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            PatientId = patientId,
            TaskType = taskType,
            Priority = priority,
            Reason = Truncate(reason, MaxReasonLength),
            Status = ReceptionTaskStatus.Open,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Assign(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("El identificador de usuario asignado no puede estar vacío.", nameof(userId));

        EnsureActive();

        AssignedToUserId = userId;
        if (Status == ReceptionTaskStatus.Open)
        {
            Status = ReceptionTaskStatus.InProgress;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Escalate(ReceptionTaskPriority priority, string reason, DateTimeOffset? utcNow = null)
    {
        EnsureActive();

        if (priority > Priority)
        {
            Priority = priority;
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            Reason = Truncate(reason, MaxReasonLength);
        }

        UpdatedAt = utcNow ?? DateTimeOffset.UtcNow;
    }

    public void Complete(Guid resolvedByUserId, string? resolutionNotes = null)
    {
        if (resolvedByUserId == Guid.Empty)
            throw new ArgumentException("El identificador de quien resuelve la tarea no puede estar vacío.", nameof(resolvedByUserId));

        EnsureActive();

        var utcNow = DateTimeOffset.UtcNow;
        Status = ReceptionTaskStatus.Done;
        ResolvedByUserId = resolvedByUserId;
        ResolutionNotes = string.IsNullOrWhiteSpace(resolutionNotes)
            ? null
            : Truncate(resolutionNotes, MaxResolutionNotesLength);
        ResolvedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void Cancel(Guid cancelledByUserId, string? reason = null)
    {
        if (cancelledByUserId == Guid.Empty)
            throw new ArgumentException("El identificador de quien cancela la tarea no puede estar vacío.", nameof(cancelledByUserId));

        EnsureActive();

        var utcNow = DateTimeOffset.UtcNow;
        Status = ReceptionTaskStatus.Cancelled;
        ResolvedByUserId = cancelledByUserId;
        ResolutionNotes = string.IsNullOrWhiteSpace(reason)
            ? null
            : Truncate(reason, MaxResolutionNotesLength);
        ResolvedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private void EnsureActive()
    {
        if (Status is not ReceptionTaskStatus.Open and not ReceptionTaskStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"La tarea de recepción {Id} ya fue cerrada en estado '{Status}' y no puede modificarse.");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
