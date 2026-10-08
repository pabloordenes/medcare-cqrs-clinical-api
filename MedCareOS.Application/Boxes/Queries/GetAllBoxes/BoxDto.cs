namespace MedCareOS.Application.Boxes.Queries.GetAllBoxes;

public record BoxDto(
    Guid BoxId,
    string Name,
    string Type,
    int Capacity,
    string Floor,
    bool IsActive
    );