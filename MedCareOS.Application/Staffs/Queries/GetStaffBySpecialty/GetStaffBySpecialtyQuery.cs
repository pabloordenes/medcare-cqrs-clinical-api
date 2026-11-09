using MedCareOS.Application.Staffs.Queries.GetAllStaff;
using MediatR;

namespace MedCareOS.Application.Staffs.Queries.GetStaffBySpecialty;

public record GetStaffBySpecialtyQuery(
    string Specialty
    ) : IRequest<List<StaffDto>>;