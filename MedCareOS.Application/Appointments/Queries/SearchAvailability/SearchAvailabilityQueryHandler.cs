using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;

namespace MedCareOS.Application.Appointments.Queries.SearchAvailability
{
    public class SearchAvailabilityQueryHandler : IRequestHandler<SearchAvailabilityQuery, List<AgendaBloqueDto>>
    {
        private readonly ApplicationDbContext _dbContext;

        public SearchAvailabilityQueryHandler(ApplicationDbContext applicationDbContext)
        {
            _dbContext = applicationDbContext;
        }

        public async Task<List<AgendaBloqueDto>> Handle(SearchAvailabilityQuery request, CancellationToken cancellationToken)
        {
            var availableScheduleBlocks = await (

                from block in _dbContext.ScheduleBlocks

                where block.StartTime >= request.Desde
                    && block.EndTime.Date <= request.Hasta.Date

                join staff in _dbContext.Staffs on block.DoctorId equals staff.Id
                where staff.Specialty == request.Especialidad

                join appBlock in _dbContext.Appointments on block.Id equals appBlock.ScheduleBlockId into appBlockGroup
                from activeApp in appBlockGroup.Where(x => x.Status != AppointmentStatus.Cancelled).DefaultIfEmpty()
                where activeApp == null

                select new AgendaBloqueDto(
                    block.Id, block.DoctorId, block.BoxId, block.StartTime, block.EndTime, "libre", null, null, null
                    )
                ).AsNoTracking().ToListAsync(cancellationToken);

            return availableScheduleBlocks;
        }
    }
}
