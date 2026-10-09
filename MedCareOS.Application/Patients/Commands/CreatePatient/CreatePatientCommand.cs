using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Patients.Commands.CreatePatient;

public record CreatePatientCommand(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string Rut,
    DateOnly BirthDate,
    string Gender,
    string Phone,
    string Address,
    string Neighbourhood
) : IRequest<Guid>;