using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Appointments.Queries.GetDailyAppointments;

public class GetDailyAppointmentsQueryHandler : IRequestHandler<GetDailyAppointmentsQuery, List<DailyAppointmentDto>>
{
    private readonly ApplicationDbContext _dbContext;
    
    public GetDailyAppointmentsQueryHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<DailyAppointmentDto>> Handle(GetDailyAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var startDayUtc = DateTime.SpecifyKind(request.AppointmentDate.Date, DateTimeKind.Utc);
        var endDayUtc = startDayUtc.AddDays(1);

        var appointments = await (
            from app in _dbContext.Appointments

            join block in _dbContext.ScheduleBlocks on app.ScheduleBlockId equals block.Id

            join patient in _dbContext.Patients on app.PatientId equals patient.Id

            join box in _dbContext.Boxes on block.BoxId equals box.Id

            where block.DoctorId == request.DoctorId
                  && block.StartTime >= startDayUtc && block.StartTime < endDayUtc
                  && app.Status != AppointmentStatus.Cancelled

            orderby block.StartTime ascending

            select new DailyAppointmentDto(
                app.Id,
                patient.FirstName + " " + patient.LastName,
                DateTime.UtcNow.Year - patient.DateOfBirth.Year, // todo: mejorar exactitud en el calculo
                box.Name,
                block.StartTime,
                block.EndTime,
                app.Status,
                app.NlpSummary,
                app.RiskBand
            )
        ).AsNoTracking().ToListAsync(cancellationToken);

        return appointments;
    }
}