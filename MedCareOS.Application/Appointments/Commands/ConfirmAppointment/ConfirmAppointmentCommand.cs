using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.ConfirmAppointment;

public sealed record ConfirmAppointmentCommand(
    Guid AppointmentId,
    Guid? ExpectedPatientId = null,
    NotificationChannel? Channel = null,
    Guid? NotificationId = null,
    string Source = "staff_manual",
    string? NormalizedKeyword = null,
    DateTimeOffset? OccurredAtUtc = null,
    Guid? CorrelationId = null,
    bool PublishPatientReplyEvent = false) : IRequest<ConfirmAppointmentResult>;

public sealed record ConfirmAppointmentResult(
    Guid AppointmentId,
    ConfirmationTransitionOutcome Outcome,
    AppointmentStatus AppointmentStatus,
    ConfirmationState ConfirmationState,
    bool StateChanged,
    bool WasIdempotentReplay,
    int CancelledScheduledActionsCount,
    int SupersededHitlTasksCount,
    decimal? EffectiveRiskScore,
    string? EffectiveBand);
