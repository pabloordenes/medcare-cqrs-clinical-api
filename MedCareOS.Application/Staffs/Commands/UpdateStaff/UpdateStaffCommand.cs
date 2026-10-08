using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Staffs.Commands.UpdateStaff;

public record UpdateStaffCommand(
    Guid StaffId,
    string FirstName,
    string LastName,
    string Rut,
    string Phone,
    string RoleName,
    bool IsActive,
    string Specialty,
    UserRole Role
    ) : IRequest;