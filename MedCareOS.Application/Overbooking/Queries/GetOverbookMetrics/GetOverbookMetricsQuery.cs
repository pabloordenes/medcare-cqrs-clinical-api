using MediatR;

namespace MedCareOS.Application.Overbooking.Queries.GetOverbookMetrics;

public sealed record OverbookMetricsDto(
    Guid TenantId,
    int SlotRefilled,
    int OverbookSlotsOpened,
    int OverbookSlotsOccupied,
    int OverbookCollisions,
    int DisplacedPatients,
    int ConfirmedLateAfterOverbook,
    int OverbookUndone,
    decimal FalsePositiveRate,
    int ShadowEvaluatedCount,
    int ShadowFalsePositivesCount,
    bool IncludesSyntheticData);

public sealed record GetOverbookMetricsQuery(Guid? TenantId = null) : IRequest<OverbookMetricsDto>;
