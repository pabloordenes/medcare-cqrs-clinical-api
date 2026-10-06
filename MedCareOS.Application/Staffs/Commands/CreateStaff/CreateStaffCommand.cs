using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Staffs.Commands.CreateStaff;

public record CreateStaffCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string Email,
    UserRole Role,
    string Rut,
    string Specialty,
    string RoleName, 
    string Phone
    ) : IRequest<Guid>;