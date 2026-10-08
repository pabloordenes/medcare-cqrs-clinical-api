using MediatR;

namespace MedCareOS.Application.Staffs.Queries.GetAllStaff;

public record GetAllStaffQuery() : IRequest<List<StaffGridDto>>;