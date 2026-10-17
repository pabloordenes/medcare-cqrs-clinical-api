using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.ScheduleBlocks.Commands.CreateScheduleBlock;

public class CreateScheduleBlockCommandHandler : IRequestHandler<CreateScheduleBlockCommand, Guid>
{
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IUnitOfWork _unitOfWork;
    
    public CreateScheduleBlockCommandHandler(
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        IBoxRepository boxRepository,
        IUnitOfWork unitOfWork)
    {
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _boxRepository = boxRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateScheduleBlockCommand request, CancellationToken cancellationToken)
    {
        // validaciones de dominio
        var staff = await _staffRepository.GetByIdAsync(request.StaffId, cancellationToken);
        if (staff == null) throw new ArgumentException("El médico seleccionado no existe.");

        var box = await _boxRepository.GetByIdAsync(request.BoxId, cancellationToken);
        if (box == null) throw new ArgumentException("El box seleccionado no existe.");
        
        // rango macro en utc
        DateTime macroInicioUtc = 
            DateTime.SpecifyKind(request.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        
        DateTime macroFinUtc =
            DateTime.SpecifyKind(request.EndDate.ToDateTime(TimeOnly.MinValue).AddDays(1).AddTicks(-1),
                DateTimeKind.Utc);

        var existingBlocks = await _scheduleBlockRepository.GetAssignedBlocksInRangeAsync(request.StaffId,
            request.BoxId, macroInicioUtc, macroFinUtc, cancellationToken);

        // agrupar slots de 1 hora en un solo bloque horario completo mediante id
        var ruleGroupId = Guid.NewGuid(); 
        
        var newBlocksToInsert = new List<ScheduleBlock>();

        // algoritmo para separar bloques horarios en slots de 1 hora
        for (DateOnly currentDate = request.StartDate; currentDate <= request.EndDate; currentDate = currentDate.AddDays(1))
        {
            if (!request.DaysOfWeek.Contains(currentDate.DayOfWeek)) continue;

            TimeOnly currentTime = request.StartTime;

            while (currentTime < request.EndTime)
            {
                DateTime slotStartUtc = DateTime.SpecifyKind(currentDate.ToDateTime(currentTime), DateTimeKind.Utc);
                DateTime slotEndUtc = DateTime.SpecifyKind(currentDate.ToDateTime(currentTime.AddHours(1)), DateTimeKind.Utc);
                
                bool occupiedSlot = existingBlocks.Any(s => 
                    s.StartTime < slotEndUtc && s.EndTime > slotStartUtc);

                if (occupiedSlot) 
                    throw new Exception("Choque horario detectado.");

                var newScheduleBlock = ScheduleBlock.Create(
                    request.StaffId,
                    request.BoxId,
                    slotStartUtc,
                    slotEndUtc,
                    ruleGroupId,
                    request.StartDate,
                    request.EndDate,
                    request.StartTime,
                    request.EndTime,
                    request.DaysOfWeek
                );
                
                newBlocksToInsert.Add(newScheduleBlock);

                currentTime = currentTime.AddHours(1);
            }
        }

        if (!newBlocksToInsert.Any())
            throw new ArgumentException(
                "El rango de fechas seleccionado no contiene ninguno de los dias de la semana indicados.");
        
        _scheduleBlockRepository.AddRange(newBlocksToInsert);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ruleGroupId;
    }
}