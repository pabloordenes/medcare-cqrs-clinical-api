using MedCareOS.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Boxes.Queries.GetAllBoxes;

public class GetAllBoxesQueryHandler : IRequestHandler<GetAllBoxesQuery, List<BoxDto>>
{
    private readonly IApplicationDbContext _dbContext;
    
    public GetAllBoxesQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<BoxDto>> Handle(GetAllBoxesQuery request, CancellationToken cancellationToken)
    {
        return await _dbContext.Boxes
            .AsNoTracking()
            .Select(box => new BoxDto(
                box.Id,
                box.Name,
                box.Type,
                box.Capacity,
                box.Floor ?? "No especificado.",
                box.IsActive))
            .ToListAsync(cancellationToken);
    }
}