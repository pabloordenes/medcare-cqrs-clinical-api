namespace MedCareOS.Domain.Entities;

public class Box
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public string? Floor { get; private set; }
    public bool IsActive { get; private set; } = true;
    
    private Box() {} // ef

    public static Box Create(string name, string type, int capacity, string? floor)
    {
        return new Box
        {
            Id = Guid.NewGuid(), Name = name, Type = type, Capacity = capacity, Floor = floor
        };
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void UpdateDetails(string name, string type, int capacity, string floor, bool isActive)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("La capacidad debe ser mayor que cero.", nameof(capacity));
        }
        
        Name = name;
        Type = type;
        Capacity = capacity;
        Floor = floor;

        if (isActive)
        {
            Activate();
        }
        else
        {
            Deactivate();
        }
    }
}