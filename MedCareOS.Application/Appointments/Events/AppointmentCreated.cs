using MassTransit;

namespace MedCareOS.Application.Appointments.Events;

[MessageUrn("medcare:appointment-created")]
public record AppointmentCreated(
    Guid AppointmentId,
    DateTimeOffset OccurredAtUtc,
    Guid CorrelationId
);
