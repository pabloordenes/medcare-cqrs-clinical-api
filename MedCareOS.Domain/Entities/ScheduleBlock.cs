namespace MedCareOS.Domain.Entities;

public class ScheduleBlock
{
    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid BoxId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    private ScheduleBlock() {} // ef

    private ScheduleBlock(Guid id,
        Guid doctorId, Guid boxId,
        DateTime startTime, DateTime endTime)
    {
        Id = id;
        DoctorId = doctorId;
        BoxId = boxId;
        StartTime = startTime;
        EndTime = endTime;
    }

    public static ScheduleBlock Create(Guid doctorId, Guid boxId, DateTime startTime, DateTime endTime)
    {
        if (startTime >= endTime)
            throw new ArgumentException("No puedes terminar un turno antes de empezarlo.");
        
        if (doctorId == Guid.Empty || boxId == Guid.Empty)
            throw new ArgumentException("Debes seleccionar un doctor/box.");
        
        return new ScheduleBlock(
            Guid.NewGuid(),
            doctorId,
            boxId,
            startTime,
            endTime
            );
    }
}