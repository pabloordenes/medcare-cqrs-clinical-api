using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Staffs.Queries.GetAllStaff;

public class GetAllStaffQueryHandler : IRequestHandler<GetAllStaffQuery, List<StaffGridDto>>
{
    private readonly ApplicationDbContext _dbContext;

    public GetAllStaffQueryHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<StaffGridDto>> Handle(GetAllStaffQuery request, CancellationToken cancellationToken)
    {
        var query = from staff in _dbContext.Staffs
            join user in _dbContext.Users on staff.UserId equals user.Id
            select new { staff, user };

        if (request.Role.HasValue)
        {
            query = query.Where(x => x.user.Role == request.Role.Value);
        }
        
        var staffs = await query
            .AsNoTracking()
            .Select(x => new StaffGridDto(
                    x.staff.Id,
                    x.staff.Rut,
                    x.staff.FirstName,
                    x.staff.LastName,
                    x.user.Email,
                    x.staff.Phone ?? "",
                    x.staff.Specialty ?? "",
                    x.staff.RoleName,
                    x.user.IsActive,
                    x.user.Role
                    )).ToListAsync(cancellationToken);
        
        return staffs;
    }
}