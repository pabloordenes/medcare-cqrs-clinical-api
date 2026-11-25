using MassTransit;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Appointments.Events;

[MessageUrn("medcare:overbook-opened")]
public sealed record OverbookOpened(
    Guid OverbookBlockId,
    Guid TenantId,
    string Specialty,
    Guid StaffId,
    Guid BoxId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    Guid SourceAppointmentId,
    OverbookDecisionMode DecisionMode,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
