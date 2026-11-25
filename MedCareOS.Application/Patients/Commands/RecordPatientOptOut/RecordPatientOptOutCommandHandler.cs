using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Patients.Commands.RecordPatientOptOut;

public class RecordPatientOptOutCommandHandler : IRequestHandler<RecordPatientOptOutCommand>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RecordPatientOptOutCommandHandler(
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RecordPatientOptOutCommand request, CancellationToken cancellationToken)
    {
        var patient = request.AuthenticatedUserId.HasValue
            ? await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
            : (request.PatientId.HasValue
                ? await _patientRepository.GetPatientByIdAsync(request.PatientId.Value, cancellationToken)
                : null);

        if (patient is null)
            throw new KeyNotFoundException("No se encontró el perfil del paciente.");

        patient.OptOut(request.Source, DateTimeOffset.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
