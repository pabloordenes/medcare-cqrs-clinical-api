using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Appointments.Risk;

public sealed record PublishAnomalyContext(
    Appointment Appointment,
    string AnomalyType,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string TriggerSource,
    RiskBandLevel? PreviousBand = null,
    Patient? Patient = null,
    ScheduleBlock? ScheduleBlock = null,
    Staff? Staff = null);

public interface IOmroAnomalyPublisher
{
    Task<bool> PublishAsync(PublishAnomalyContext context, CancellationToken cancellationToken = default);
}
