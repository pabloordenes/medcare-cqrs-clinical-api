using MediatR;

namespace MedCareOS.Application.Boxes.Queries.GetAllBoxes;

public record GetAllBoxesQuery() : IRequest<List<BoxDto>>;