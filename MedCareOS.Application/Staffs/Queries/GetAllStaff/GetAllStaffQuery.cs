using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Staffs.Queries.GetAllStaff;

public record GetAllStaffQuery(
    UserRole? Role = null
    ) : IRequest<List<StaffGridDto>>;