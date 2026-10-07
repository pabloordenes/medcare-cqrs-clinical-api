using MediatR;

namespace MedCareOS.Application.Boxes.Commands.CreateBox;

public record CreateBoxCommand(
    string Name,
    string Type,
    int Capacity,
    string Floor
    ) : IRequest<Guid>;