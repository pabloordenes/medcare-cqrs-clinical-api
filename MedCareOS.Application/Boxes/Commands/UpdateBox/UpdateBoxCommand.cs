using MediatR;

namespace MedCareOS.Application.Boxes.Commands.UpdateBox;

public record UpdateBoxCommand(
    Guid BoxId,
    string Name,
    string Type,
    int Capacity,
    string Floor,
    bool IsActive
    ) : IRequest;