using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Queries.GetMyWaitingList;

public class GetMyWaitingListQueryHandler : IRequestHandler<GetMyWaitingListQuery, List<WaitingListEntryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IWaitingListRepository _waitingListRepository;

    public GetMyWaitingListQueryHandler(
        IPatientRepository patientRepository,
        IWaitingListRepository waitingListRepository)
    {
        _patientRepository = patientRepository;
        _waitingListRepository = waitingListRepository;
    }

    public async Task<List<WaitingListEntryDto>> Handle(
        GetMyWaitingListQuery request,
        CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId, cancellationToken)
            ?? throw new KeyNotFoundException("El paciente autenticado no tiene un perfil registrado.");

        var entries = await _waitingListRepository.GetByPatientIdAsync(patient.Id, cancellationToken);

        return entries
            .OrderByDescending(e => e.RegistrationDate)
            .Select(e => new WaitingListEntryDto(
                e.Id,
                e.TenantId,
                e.PatientId,
                e.Specialty,
                e.RegistrationDate,
                e.Priority,
                e.Status,
                e.ClinicalContext,
                e.PreferredStaffId,
                e.PreferredFromDate,
                e.PreferredToDate,
                e.PreferredTimeStart,
                e.PreferredTimeEnd,
                e.IsDisplacedBonus,
                e.DisplacedSourceAppointmentId,
                e.ExpiresAt))
            .ToList();
    }
}
