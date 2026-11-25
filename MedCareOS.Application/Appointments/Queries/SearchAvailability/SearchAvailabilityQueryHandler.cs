using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Appointments.Queries.SearchAvailability
{
    public class SearchAvailabilityQueryHandler : IRequestHandler<SearchAvailabilityQuery, List<AgendaBloqueDto>>
    {
        private readonly IApplicationDbContext _dbContext;

        public SearchAvailabilityQueryHandler(IApplicationDbContext applicationDbContext)
        {
            _dbContext = applicationDbContext;
        }

        public async Task<List<AgendaBloqueDto>> Handle(SearchAvailabilityQuery request, CancellationToken cancellationToken)
        {
            var utcNow = DateTime.UtcNow;
            var utcNowOffset = new DateTimeOffset(utcNow, TimeSpan.Zero);
            var desdeUtc = DateTime.SpecifyKind(request.Desde.Date, DateTimeKind.Utc);
            var hastaFinDelDiaUtc = DateTime.SpecifyKind(request.Hasta.Date.AddDays(1), DateTimeKind.Utc);
            var availableScheduleBlocks = await (

                from block in _dbContext.ScheduleBlocks

                where !block.IsOverbook
                    && block.StartTime > utcNow
                    && block.StartTime.Date >= desdeUtc
                    && block.EndTime.Date < hastaFinDelDiaUtc

                join staff in _dbContext.Staffs on block.StaffId equals staff.Id
                where staff.Specialty == request.Especialidad

                join box in _dbContext.Boxes on block.BoxId equals box.Id
                where box.IsActive

                join appBlock in _dbContext.Appointments on block.Id equals appBlock.ScheduleBlockId into appBlockGroup
                from activeApp in appBlockGroup.Where(x => x.Status != AppointmentStatus.Cancelled).DefaultIfEmpty()
                where activeApp == null

                // Soft-hold: ocultar bloques retenidos por una oferta activa pendiente hacia lista de espera
                where !_dbContext.WaitingListOffers.Any(o =>
                    o.ScheduleBlockId == block.Id
                    && o.Status == WaitingListOfferStatus.Pending
                    && o.ExpiresAt > utcNowOffset)

                select new AgendaBloqueDto(
                    block.Id, block.StaffId, block.BoxId, block.StartTime, block.EndTime, "libre", null, null, null
                    )
                ).AsNoTracking().ToListAsync(cancellationToken);

            return availableScheduleBlocks;
        }
    }
}
