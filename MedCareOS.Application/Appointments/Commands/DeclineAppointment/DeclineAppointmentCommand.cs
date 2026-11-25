using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.DeclineAppointment;

public sealed record DeclineAppointmentCommand(
    Guid AppointmentId,
    Guid? ExpectedPatientId = null,
    NotificationChannel? Channel = null,
    Guid? NotificationId = null,
    string Source = "patient_decline",
    string? Reason = null,
    string? NormalizedKeyword = null,
    DateTimeOffset? OccurredAtUtc = null,
    Guid? CorrelationId = null,
    bool PublishPatientReplyEvent = false) : IRequest<DeclineAppointmentResult>;

public sealed record DeclineAppointmentResult(
    Guid AppointmentId,
    ConfirmationTransitionOutcome Outcome,
    AppointmentStatus AppointmentStatus,
    ConfirmationState ConfirmationState,
    bool StateChanged,
    bool WasIdempotentReplay,
    int CancelledScheduledActionsCount,
    int SupersededHitlTasksCount);
