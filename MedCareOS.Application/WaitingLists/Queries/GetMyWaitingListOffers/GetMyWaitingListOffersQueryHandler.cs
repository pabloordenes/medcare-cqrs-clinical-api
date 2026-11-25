using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.WaitingLists.Queries.GetMyWaitingListOffers;

public class GetMyWaitingListOffersQueryHandler
    : IRequestHandler<GetMyWaitingListOffersQuery, List<WaitingListOfferDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IApplicationDbContext _dbContext;

    public GetMyWaitingListOffersQueryHandler(
        IPatientRepository patientRepository,
        IApplicationDbContext dbContext)
    {
        _patientRepository = patientRepository;
        _dbContext = dbContext;
    }

    public async Task<List<WaitingListOfferDto>> Handle(
        GetMyWaitingListOffersQuery request,
        CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId, cancellationToken)
            ?? throw new KeyNotFoundException("El usuario autenticado no tiene perfil de paciente.");

        var utcNow = DateTimeOffset.UtcNow;

        return await (
            from offer in _dbContext.WaitingListOffers.AsNoTracking()
            join block in _dbContext.ScheduleBlocks.AsNoTracking() on offer.ScheduleBlockId equals block.Id
            join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
            where offer.PatientId == patient.Id
                  && offer.Status == WaitingListOfferStatus.Pending
                  && offer.ExpiresAt > utcNow
            orderby offer.OfferedAt descending
            select new WaitingListOfferDto(
                offer.Id,
                offer.WaitingListId,
                offer.ScheduleBlockId,
                offer.SourceType,
                offer.CascadeAttemptNumber,
                offer.Status,
                block.StartTime,
                block.EndTime,
                staff.Specialty ?? "Atención médica",
                staff.FirstName + " " + staff.LastName,
                offer.OfferedAt,
                offer.ExpiresAt,
                offer.AcceptedAt)
        ).ToListAsync(cancellationToken);
    }
}
