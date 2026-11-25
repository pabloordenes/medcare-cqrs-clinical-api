using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class PatientActionToken
{
    public const int TokenHashLength = 64;
    public const int MaxUsedActionLength = 30;

    public Guid Id { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid? AppointmentId { get; private set; }
    public Guid? WaitingListOfferId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid? NotificationId { get; private set; }
    public PatientActionScope ActionScope { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public string? UsedAction { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PatientActionToken() { } // ef

    public static PatientActionToken Create(
        Guid id,
        string tokenHash,
        Guid appointmentId,
        Guid patientId,
        Guid? notificationId,
        PatientActionScope scope,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El Id del token no puede estar vacío.", nameof(id));
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != TokenHashLength)
            throw new ArgumentException("El hash del token debe ser SHA-256 hex de 64 caracteres.", nameof(tokenHash));
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("AppointmentId no puede estar vacío.", nameof(appointmentId));
        if (patientId == Guid.Empty)
            throw new ArgumentException("PatientId no puede estar vacío.", nameof(patientId));
        if (scope == PatientActionScope.WaitingListOffer)
            throw new ArgumentException("Use CreateForWaitingListOffer para tokens de oferta de lista de espera.", nameof(scope));
        if (expiresAt <= utcNow)
            throw new ArgumentException("La fecha de expiración debe ser futura.", nameof(expiresAt));

        return new PatientActionToken
        {
            Id = id,
            TokenHash = tokenHash.ToLowerInvariant(),
            AppointmentId = appointmentId,
            WaitingListOfferId = null,
            PatientId = patientId,
            NotificationId = notificationId,
            ActionScope = scope,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow
        };
    }

    public static PatientActionToken CreateForWaitingListOffer(
        Guid id,
        string tokenHash,
        Guid waitingListOfferId,
        Guid patientId,
        Guid? notificationId,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El Id del token no puede estar vacío.", nameof(id));
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != TokenHashLength)
            throw new ArgumentException("El hash del token debe ser SHA-256 hex de 64 caracteres.", nameof(tokenHash));
        if (waitingListOfferId == Guid.Empty)
            throw new ArgumentException("WaitingListOfferId no puede estar vacío.", nameof(waitingListOfferId));
        if (patientId == Guid.Empty)
            throw new ArgumentException("PatientId no puede estar vacío.", nameof(patientId));
        if (expiresAt <= utcNow)
            throw new ArgumentException("La fecha de expiración debe ser futura.", nameof(expiresAt));

        return new PatientActionToken
        {
            Id = id,
            TokenHash = tokenHash.ToLowerInvariant(),
            AppointmentId = null,
            WaitingListOfferId = waitingListOfferId,
            PatientId = patientId,
            NotificationId = notificationId,
            ActionScope = PatientActionScope.WaitingListOffer,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow
        };
    }

    public bool IsActiveAt(DateTimeOffset utcNow) =>
        !UsedAt.HasValue && !RevokedAt.HasValue && ExpiresAt > utcNow;

    public void MarkUsed(string action, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("La acción ejecutada es obligatoria.", nameof(action));

        if (UsedAt.HasValue)
            throw new InvalidOperationException("El enlace ya fue utilizado anteriormente.");

        if (RevokedAt.HasValue)
            throw new InvalidOperationException("El enlace fue revocado.");

        if (ExpiresAt <= utcNow)
            throw new InvalidOperationException("El enlace ha expirado.");

        var normalized = action.Trim();
        UsedAt = utcNow;
        UsedAction = normalized.Length <= MaxUsedActionLength ? normalized : normalized[..MaxUsedActionLength];
    }

    public void Revoke(DateTimeOffset utcNow)
    {
        if (!UsedAt.HasValue && !RevokedAt.HasValue)
        {
            RevokedAt = utcNow;
        }
    }
}
