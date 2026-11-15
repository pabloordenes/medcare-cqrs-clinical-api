using MediatR;

namespace MedCareOS.Application.Resolutions.Queries.GetPendingResolutions;

public record GetPendingResolutionsQuery : IRequest<List<ResolutionTaskDto>>;