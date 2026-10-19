using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.ScheduleBlocks.Queries.GetAllScheduleBlocks;

public class GetAllScheduleBlocksQueryHandler : IRequest<List<ScheduleBlockDto>>
{
    private readonly ApplicationDbContext _dbContext;
    
    public GetAllScheduleBlocksQueryHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ScheduleBlockDto>> Handle()
    {
        var rawBlocks = await (
            from block in _dbContext.ScheduleBlocks
            
            where block.RuleEndDate >= DateOnly.FromDateTime(DateTime.Now)

            join staff in _dbContext.Staffs on block.StaffId equals staff.Id

            join box in _dbContext.Boxes on block.BoxId equals box.Id

            select new
            {
                block.RuleGroupId,
                block.StaffId,
                StaffFullName = staff.FirstName + " " + staff.LastName,
                StaffSpecialty = staff.Specialty ?? "",
                StaffRut = staff.Rut,
                block.BoxId,
                BoxName = box.Name,
                BoxLocation = box.Floor ?? "", // O la propiedad que guarde el sector/piso
                block.RuleStartDate,
                block.RuleEndDate,
                block.RuleStartTime,
                block.RuleEndTime,
                block.RuleDaysOfWeek
            }
        ).AsNoTracking()
            .ToListAsync();

        var uniqueRule = rawBlocks.DistinctBy(x => x.RuleGroupId);
            
        var result = uniqueRule
            .Select(x => new ScheduleBlockDto(
                x.RuleGroupId,
                x.StaffId,
                x.StaffFullName,
                x.StaffSpecialty,
                x.StaffRut,
                x.BoxId,
                x.BoxName,
                x.BoxLocation,
                x.RuleStartDate,
                x.RuleEndDate,
                x.RuleStartTime,
                x.RuleEndTime,
                x.RuleDaysOfWeek
            )).ToList();
        
        return result;
    }
}