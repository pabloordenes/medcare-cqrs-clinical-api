using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Commands.LeaveWaitingList;

public class LeaveWaitingListCommandHandler : IRequestHandler<LeaveWaitingListCommand>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;

    public LeaveWaitingListCommandHandler(
        IWaitingListRepository waitingListRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(LeaveWaitingListCommand request, CancellationToken cancellationToken)
    {
        var entry = await _waitingListRepository.GetByIdAsync(request.WaitingListId, cancellationToken)
            ?? throw new KeyNotFoundException($"La inscripción {request.WaitingListId} no existe.");

        if (request.AuthenticatedUserId.HasValue)
        {
            var patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
                ?? throw new UnauthorizedAccessException("El usuario autenticado no tiene perfil de paciente.");

            if (entry.PatientId != patient.Id)
                throw new UnauthorizedAccessException("No tienes permiso para cancelar la inscripción de otro paciente.");
        }

        entry.Cancel(request.Reason, DateTimeOffset.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
