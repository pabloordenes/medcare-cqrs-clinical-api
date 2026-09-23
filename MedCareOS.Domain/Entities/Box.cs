namespace MedCareOS.Domain.Entities;

public class Box
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public string? Floor { get; private set; }
    
    private Box() {} // ef

    public static Box Create(string name, string type, string? floor)
    {
        return new Box
        {
            Id = Guid.NewGuid(), Name = name, Type = type, Capacity = 1, Floor = floor
        };
    }
}