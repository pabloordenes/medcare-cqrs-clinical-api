using MediatR;

namespace MedCareOS.Application.Patients.Commands.RecordPatientOptOut;

public record RecordPatientOptOutCommand(
    Guid? PatientId,
    Guid? AuthenticatedUserId,
    string Source) : IRequest;
