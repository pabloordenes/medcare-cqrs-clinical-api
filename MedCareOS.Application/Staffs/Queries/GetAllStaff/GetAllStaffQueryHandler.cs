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
        var staffs = await (
                from staff in _dbContext.Staffs

                join user in _dbContext.Users on staff.UserId equals user.Id

                select new StaffGridDto(
                    staff.Id,
                    staff.Rut,
                    staff.FirstName,
                    staff.LastName,
                    user.Email,
                    user.Role
                )
            ).AsNoTracking()
            .ToListAsync(cancellationToken);
        
        return staffs;
    }
}