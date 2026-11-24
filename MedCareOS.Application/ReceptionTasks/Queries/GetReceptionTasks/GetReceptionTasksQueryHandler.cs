using MedCareOS.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.ReceptionTasks.Queries.GetReceptionTasks;

public class GetReceptionTasksQueryHandler : IRequestHandler<GetReceptionTasksQuery, PagedReceptionTasksDto>
{
    private readonly IApplicationDbContext _dbContext;

    public GetReceptionTasksQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedReceptionTasksDto> Handle(GetReceptionTasksQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query =
            from task in _dbContext.ReceptionTasks.AsNoTracking()
            join appointment in _dbContext.Appointments.AsNoTracking() on task.AppointmentId equals appointment.Id
            join patient in _dbContext.Patients.AsNoTracking() on task.PatientId equals patient.Id
            join block in _dbContext.ScheduleBlocks.AsNoTracking() on appointment.ScheduleBlockId equals block.Id
            join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
            select new
            {
                task,
                appointment.NoShowRiskScore,
                PatientName = patient.FirstName + " " + patient.LastName,
                PatientPhone = patient.Phone,
                AppointmentStartTime = block.StartTime,
                Specialty = staff.Specialty ?? "Medicina General"
            };

        if (request.Status.HasValue)
            query = query.Where(x => x.task.Status == request.Status.Value);

        if (request.Priority.HasValue)
            query = query.Where(x => x.task.Priority == request.Priority.Value);

        if (request.AssignedToUserId.HasValue)
            query = query.Where(x => x.task.AssignedToUserId == request.AssignedToUserId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.task.Priority)
            .ThenBy(x => x.task.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ReceptionTaskDto(
                x.task.Id,
                x.task.AppointmentId,
                x.task.PatientId,
                x.PatientName,
                x.PatientPhone,
                x.AppointmentStartTime,
                x.Specialty,
                x.NoShowRiskScore,
                x.task.TaskType,
                x.task.Priority,
                x.task.Status,
                x.task.Reason,
                x.task.AssignedToUserId,
                x.task.ResolutionNotes,
                x.task.ResolvedByUserId,
                x.task.CreatedAt,
                x.task.UpdatedAt,
                x.task.ResolvedAt))
            .ToListAsync(cancellationToken);

        return new PagedReceptionTasksDto(totalCount, page, pageSize, items);
    }
}
