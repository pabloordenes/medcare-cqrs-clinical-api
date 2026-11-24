using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Appointments.Risk;

public sealed record RecordRiskSignalRequest(
    Guid AppointmentId,
    RiskSignalKind Kind,
    string Source,
    string IdempotencyKey,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    decimal? WeightOverride = null,
    string? MetadataJson = null,
    bool CreateFailedDeliveryReceptionTask = false,
    string? ReceptionTaskReason = null);

public sealed record RiskSignalApplicationResult(
    bool Applied,
    bool DuplicateSkipped,
    Guid? SignalId = null,
    decimal? PreviousEffectiveScore = null,
    decimal? NewEffectiveScore = null,
    RiskBandLevel? PreviousBand = null,
    RiskBandLevel? NewBand = null,
    bool ReceptionTaskCreated = false);

public interface IRiskSignalProcessor
{
    Task<RiskSignalApplicationResult> RecordAsync(
        RecordRiskSignalRequest request,
        CancellationToken cancellationToken = default);
}
