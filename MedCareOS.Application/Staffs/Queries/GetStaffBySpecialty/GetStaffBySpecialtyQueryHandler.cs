using MedCareOS.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Staffs.Queries.GetStaffBySpecialty;

public class GetStaffBySpecialtyQueryHandler : IRequestHandler<GetStaffBySpecialtyQuery, List<StaffDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetStaffBySpecialtyQueryHandler(IApplicationDbContext dbContext)
    {
         _dbContext = dbContext;
    }

    public async Task<List<StaffDto>> Handle(GetStaffBySpecialtyQuery request, CancellationToken cancellationToken)
    {
        var staffMembers = await _dbContext.Staffs
            .AsNoTracking()
            .Where(s => s.Specialty == request.Specialty)
            .Select(s => new StaffDto(
                s.Id,
                $"{s.FirstName} {s.LastName}"
            ))
            .ToListAsync(cancellationToken);
        
        if (!staffMembers.Any())
        {
            // todo: aplicar middleware
            throw new KeyNotFoundException($"No se encontraron médicos para la especialidad: {request.Specialty}"); 
        }

        return staffMembers;
    }
}