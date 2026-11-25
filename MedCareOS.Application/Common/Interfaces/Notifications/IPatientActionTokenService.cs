using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Common.Interfaces.Notifications;

public interface IPatientActionTokenService
{
    Task<IssuedActionToken> IssueAsync(
        Guid appointmentId,
        Guid patientId,
        Guid? notificationId,
        PatientActionScope scope,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<IssuedActionToken> IssueForWaitingListOfferAsync(
        Guid waitingListOfferId,
        Guid patientId,
        Guid? notificationId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<ActionTokenPreviewResult> PreviewAsync(string rawToken, CancellationToken cancellationToken = default);

    Task<ActionTokenConsumeResult> ConsumeAsync(
        string rawToken,
        PatientReplyIntent requestedAction,
        CancellationToken cancellationToken = default);
}

public sealed record IssuedActionToken(
    Guid TokenId,
    string RawToken,
    DateTimeOffset ExpiresAt);

public enum ActionTokenValidationStatus
{
    Valid,
    InvalidSignatureOrFormat,
    NotFound,
    AlreadyUsed,
    Revoked,
    Expired,
    ScopeNotAllowed,
    AppointmentNotActionable
}

public sealed record ActionTokenPreviewResult(
    bool IsValid,
    ActionTokenValidationStatus ValidationStatus,
    string? ClinicName = null,
    string? AppointmentDateLocal = null,
    string? AppointmentTimeLocal = null,
    string? Specialty = null,
    IReadOnlyList<PatientReplyIntent>? AllowedActions = null,
    DateTimeOffset? ExpiresAt = null,
    Guid? WaitingListOfferId = null,
    string? TargetKind = null);

public sealed record ActionTokenConsumeResult(
    bool IsSuccess,
    ActionTokenValidationStatus ValidationStatus,
    Guid? AppointmentId = null,
    Guid? PatientId = null,
    Guid? NotificationId = null,
    PatientReplyIntent ExecutedIntent = PatientReplyIntent.Unknown,
    Guid? WaitingListOfferId = null);
