using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;
using Omro.Domain.ResolutionTasks;

namespace MedCareOS.Application.Appointments.Commands.CreateAppointment;

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Guid>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IAiClinicalService _aiClinicalService;
    private readonly IPublishEndpoint _publishEndpoint;

    public CreateAppointmentCommandHandler(IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository, IUnitOfWork unitOfWork,
        IScheduleBlockRepository scheduleBlockRepository, IStaffRepository staffRepository,
        IAiClinicalService aiClinicalService, IPublishEndpoint publishEndpoint)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _aiClinicalService = aiClinicalService;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Guid> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetPatientByIdAsync(request.PatientId, cancellationToken);
        if (patient == null)
            throw new InvalidOperationException("El paciente no existe."); // futura excepcion personalizada
        
        var scheduleBlock = await _scheduleBlockRepository.GetByIdAsync(request.ScheduleBlockId, cancellationToken);
        if (scheduleBlock == null)
            throw new InvalidOperationException("El bloque horario no existe.");

        var staff = await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken);
        if (staff == null)
            throw new InvalidOperationException("El staff no existe.");
        
        var newAppointment = Appointment.Create(
            request.ScheduleBlockId,
            request.PatientId,
            request.SymptomsRaw,
            request.SchedulingSource);

        // calculos requeridos por el modelo predictivo
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int age = today.Year - patient.DateOfBirth.Year;
        if (patient.DateOfBirth > today.AddYears(-age)) age--; // ajuste cumpleaños
        
        int daysOfWaiting = (scheduleBlock.StartTime.Date - DateTime.UtcNow.Date).Days;
        if (daysOfWaiting < 0) daysOfWaiting = 0;
        
        int previousAbsences = await _appointmentRepository.GetNoShowCountByPatientId(request.PatientId, cancellationToken);

        string specialtyNormalized = staff.Specialty?
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", "_") ?? "medicina_general";
        
        var predictRequest = new PredictRequestDto(
            newAppointment.Id,
            age,
            patient.Gender,
            daysOfWaiting,
            specialtyNormalized,
            previousAbsences,
            "ninguno"
        );
        
        var nlpTask = _aiClinicalService.GetNlpSummaryAsync(newAppointment.Id, request.SymptomsRaw, cancellationToken);
        var predictTask = _aiClinicalService.PredictNoShowRiskAsync(predictRequest, cancellationToken);
        
        await Task.WhenAll(nlpTask, predictTask); // enviamos ambas peticiones a la api al mismo tiempo
        
        var nlpResult = await nlpTask;
        var predictResult = await predictTask;

        string processedSymptomsJson = JsonSerializer.Serialize(nlpResult.ExtractedSymptoms);
        // actualizamos usando metodos del aggregate
        newAppointment.ApplyNlpAnalysis(nlpResult.Summary, processedSymptomsJson);
        newAppointment.UpdateRiskScore(predictResult.RiskScore, predictResult.RiskBand);

        await _appointmentRepository.AddAppointmentAsync(newAppointment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        if (predictResult.RiskBand == "Alto" || predictResult.RiskScore >= 0.5m)
        {
            await _publishEndpoint.Publish(new AnomalyEvent(
                predictResult.RiskScore,
                newAppointment.Id
            ), cancellationToken);
        }
        
        return newAppointment.Id;
    }
}