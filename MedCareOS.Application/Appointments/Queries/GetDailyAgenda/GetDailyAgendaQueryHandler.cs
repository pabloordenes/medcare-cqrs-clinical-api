using MedCareOS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MedCareOS.Application.Interfaces;

namespace MedCareOS.Application.Appointments.Queries.GetDailyAgenda
{
    public class GetDailyAgendaQueryHandler : IRequestHandler<GetDailyAgendaQuery, List<AgendaBloqueDto>>
    {
        private readonly IApplicationDbContext _dbContext;

        public GetDailyAgendaQueryHandler(IApplicationDbContext applicationDbContext)
        {
            _dbContext = applicationDbContext;
        }

        public async Task<List<AgendaBloqueDto>> Handle(GetDailyAgendaQuery request, CancellationToken cancellationToken)
        {
            var inicioDiaUtc = DateTime.SpecifyKind(request.Fecha.Date, DateTimeKind.Utc);
            var finDiaUtc = inicioDiaUtc.AddDays(1);
            var agenda = await (
                from block in _dbContext.ScheduleBlocks

                where block.StaffId == request.MedicoId
                    && block.StartTime >= inicioDiaUtc
                    && block.EndTime < finDiaUtc

                join app in _dbContext.Appointments on block.Id equals app.ScheduleBlockId into appGroup

                from activeApp in appGroup.Where(a => a.Status != AppointmentStatus.Cancelled).DefaultIfEmpty()

                join pat in _dbContext.Patients on activeApp.PatientId equals pat.Id into patGroup

                from patient in patGroup.DefaultIfEmpty()

                select new AgendaBloqueDto(
                    block.Id,
                    block.StaffId,
                    block.BoxId,
                    block.StartTime,
                    block.EndTime,
                    activeApp != null ? "ocupada" : "libre",
                    activeApp != null ? activeApp.Id : null,
                    patient != null ? patient.FirstName + " " + patient.LastName : null,
                    activeApp != null ? activeApp.NlpSummary : null
                    )
                )
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return agenda;
        }
    }
}
