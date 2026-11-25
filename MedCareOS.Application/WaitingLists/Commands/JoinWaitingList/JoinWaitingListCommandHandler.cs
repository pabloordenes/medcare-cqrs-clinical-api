using System.Text.Json;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.WaitingLists.Commands.JoinWaitingList;

public class JoinWaitingListCommandHandler : IRequestHandler<JoinWaitingListCommand, Guid>
{
    private const int MaxClinicalContextBytes = 8192;

    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public JoinWaitingListCommandHandler(
        IWaitingListRepository waitingListRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(JoinWaitingListCommand request, CancellationToken cancellationToken)
    {
        Patient? patient;
        int effectivePriority;

        if (request.AuthenticatedUserId.HasValue)
        {
            patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("El paciente autenticado no tiene un perfil registrado.");
            // Un paciente autenticado nunca puede autoasignarse prioridad privilegiada
            effectivePriority = 0;
        }
        else
        {
            if (!request.PatientId.HasValue || request.PatientId.Value == Guid.Empty)
                throw new ArgumentException("PatientId es obligatorio.", nameof(request.PatientId));

            patient = await _patientRepository.GetPatientByIdAsync(request.PatientId.Value, cancellationToken)
                ?? throw new KeyNotFoundException($"El paciente {request.PatientId.Value} no existe.");
            effectivePriority = request.Priority;
        }

        var tenantId = request.TenantId ?? RiskThresholds.DefaultTenantId;
        var utcNow = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.ClinicalContextJson))
        {
            if (request.ClinicalContextJson.Length > MaxClinicalContextBytes)
                throw new ArgumentException($"El contexto clínico excede el máximo de {MaxClinicalContextBytes} caracteres.");

            try
            {
                using var _ = JsonDocument.Parse(request.ClinicalContextJson);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"ClinicalContextJson debe ser un JSON válido: {ex.Message}");
            }
        }

        if (await _waitingListRepository.HasActiveEntryAsync(tenantId, patient.Id, request.Specialty, cancellationToken))
        {
            throw new DuplicateWaitingListException(
                $"El paciente ya tiene una inscripción activa en lista de espera para '{request.Specialty.Trim()}'.");
        }

        var entry = WaitingList.Create(
            patientId: patient.Id,
            specialty: request.Specialty,
            clinicalContext: request.ClinicalContextJson,
            priority: effectivePriority,
            tenantId: tenantId,
            preferredStaffId: request.PreferredStaffId,
            preferredFromDate: request.PreferredFromDate,
            preferredToDate: request.PreferredToDate,
            preferredTimeStart: request.PreferredTimeStart,
            preferredTimeEnd: request.PreferredTimeEnd,
            expiresAt: request.ExpiresAt,
            utcNow: utcNow);

        if (request.DisplacedSourceAppointmentId.HasValue && request.DisplacedSourceAppointmentId.Value != Guid.Empty)
        {
            var displacedApp = await _appointmentRepository.GetAppointmentByIdAsync(
                request.DisplacedSourceAppointmentId.Value,
                cancellationToken)
                ?? throw new KeyNotFoundException($"La cita origen {request.DisplacedSourceAppointmentId.Value} no existe.");

            if (displacedApp.PatientId != patient.Id)
                throw new UnauthorizedAccessException("La cita indicada no pertenece al paciente.");

            if (!displacedApp.IsDisplacedByOverbook && !displacedApp.WasEverDisplacedByOverbook)
                throw new InvalidOperationException("La cita indicada no fue desplazada por sobrecupo.");

            entry.ApplyDisplacedBonus(displacedApp.Id, utcNow);
        }

        await _waitingListRepository.AddAsync(entry, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex, "UX_lista_espera_active_patient_specialty"))
        {
            throw new DuplicateWaitingListException(
                $"El paciente ya tiene una inscripción activa en lista de espera para '{request.Specialty.Trim()}'.");
        }

        return entry.Id;
    }
}
