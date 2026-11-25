using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Overbooking.Queries.GetOverbookMetrics;

public class GetOverbookMetricsQueryHandler : IRequestHandler<GetOverbookMetricsQuery, OverbookMetricsDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IOverbookShadowDecisionRepository _shadowDecisionRepository;

    public GetOverbookMetricsQueryHandler(
        IApplicationDbContext dbContext,
        IOverbookShadowDecisionRepository shadowDecisionRepository)
    {
        _dbContext = dbContext;
        _shadowDecisionRepository = shadowDecisionRepository;
    }

    public async Task<OverbookMetricsDto> Handle(
        GetOverbookMetricsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId ?? RiskThresholds.DefaultTenantId;

        // Sobrecupos pertenecientes al tenant (vinculados a su cita origen)
        var tenantOverbooks = await (
            from block in _dbContext.ScheduleBlocks.AsNoTracking()
            join sourceApp in _dbContext.Appointments.AsNoTracking() on block.SourceAppointmentId equals sourceApp.Id
            where block.IsOverbook && sourceApp.TenantId == tenantId
            select new
            {
                block.Id,
                block.OverbookStatus,
                block.DeactivationReason,
                block.OccupiedAppointmentId,
                sourceApp.DeadlineMissedAt,
                sourceApp.ConfirmedAt
            }
        ).ToListAsync(cancellationToken);

        int overbookOpened = tenantOverbooks.Count;
        int overbookOccupied = tenantOverbooks.Count(o => o.OverbookStatus == OverbookBlockStatus.Occupied);
        int overbookUndone = tenantOverbooks.Count(o =>
            o.OverbookStatus == OverbookBlockStatus.Deactivated &&
            string.Equals(o.DeactivationReason, "LateConfirmationUndo", StringComparison.OrdinalIgnoreCase));
        int confirmedLateAfterOverbook = tenantOverbooks.Count(o =>
            o.DeadlineMissedAt.HasValue && o.ConfirmedAt.HasValue);

        // SlotRefilled: cupos cubiertos por sobrecupo o lista de espera que efectivamente fueron atendidos (check-in / en curso / finalizados)
        int slotRefilled = await (
            from app in _dbContext.Appointments.AsNoTracking()
            join block in _dbContext.ScheduleBlocks.AsNoTracking() on app.ScheduleBlockId equals block.Id
            where app.TenantId == tenantId
                  && (block.IsOverbook || app.WaitingListOfferId != null)
                  && app.Status != AppointmentStatus.Cancelled
                  && (app.ActualAttendance == true || app.Status == AppointmentStatus.Finished || app.Status == AppointmentStatus.InProgress)
            select app.Id
        ).CountAsync(cancellationToken);

        int overbookCollisions = await _dbContext.OverbookCollisionEvents
            .AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.RuleApplied != "OVERBOOK_UNDO_DEACTIVATED", cancellationToken);

        int displacedPatients = await _dbContext.Appointments
            .AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.WasEverDisplacedByOverbook, cancellationToken);

        var shadowDecisions = await _shadowDecisionRepository.GetByTenantAsync(tenantId, isSyntheticOnly: null, cancellationToken);
        var resolvedWouldOverbook = shadowDecisions.Where(d => d.WouldHaveOverbooked && d.OutcomeAttendance.HasValue).ToList();
        int shadowEvaluated = resolvedWouldOverbook.Count;
        int shadowFp = resolvedWouldOverbook.Count(d => d.OutcomeAttendance == true);
        decimal fpr = shadowEvaluated == 0
            ? 0m
            : Math.Round((decimal)shadowFp / shadowEvaluated, 4, MidpointRounding.AwayFromZero);

        return new OverbookMetricsDto(
            TenantId: tenantId,
            SlotRefilled: slotRefilled,
            OverbookSlotsOpened: overbookOpened,
            OverbookSlotsOccupied: overbookOccupied,
            OverbookCollisions: overbookCollisions,
            DisplacedPatients: displacedPatients,
            ConfirmedLateAfterOverbook: confirmedLateAfterOverbook,
            OverbookUndone: overbookUndone,
            FalsePositiveRate: fpr,
            ShadowEvaluatedCount: shadowEvaluated,
            ShadowFalsePositivesCount: shadowFp,
            IncludesSyntheticData: shadowDecisions.Any(d => d.IsSynthetic));
    }
}
