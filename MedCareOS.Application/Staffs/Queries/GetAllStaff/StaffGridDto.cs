using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Staffs.Queries.GetAllStaff;

public record StaffGridDto(
    Guid StaffId,
    string Rut,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Specialty,
    string RoleName,
    bool IsActive,
    UserRole Role
    );