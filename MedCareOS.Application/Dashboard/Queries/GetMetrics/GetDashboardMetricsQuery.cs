using MediatR;

namespace MedCareOS.Application.Dashboard.Queries.GetMetrics;

public record GetDashboardMetricsQuery() : IRequest<DashboardAusentismoDto>;