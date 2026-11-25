using MassTransit;

namespace MedCareOS.Application.Appointments.Events;

[MessageUrn("medcare:late-confirmation-received")]
public sealed record LateConfirmationReceived(
    Guid AppointmentId,
    Guid TenantId,
    DateTimeOffset ConfirmedAtUtc,
    Guid CorrelationId);
