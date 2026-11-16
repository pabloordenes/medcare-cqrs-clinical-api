using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Omro.Contracts.Events;

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
    private readonly IServiceScopeFactory _scopeFactory;

    public CreateAppointmentCommandHandler(IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository, IUnitOfWork unitOfWork,
        IScheduleBlockRepository scheduleBlockRepository, IStaffRepository staffRepository,
        IAiClinicalService aiClinicalService, IPublishEndpoint publishEndpoint, 
        IServiceScopeFactory scopeFactory)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _aiClinicalService = aiClinicalService;
        _publishEndpoint = publishEndpoint;
        _scopeFactory = scopeFactory;
    }

    public async Task<Guid> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        Guid effectivePatientId = request.PatientId;
        Patient? patient;

        if (request.AuthenticatedUserId.HasValue)
        {
            patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken);
            if (patient == null)
                throw new InvalidOperationException("El paciente autenticado no tiene un perfil registrado.");

            effectivePatientId = patient.Id;
        }
        else
        {
            patient = await _patientRepository.GetPatientByIdAsync(effectivePatientId, cancellationToken);
            if (patient == null) throw new InvalidOperationException("El paciente no existe."); // futura excepcion personalizada
        }
        
        var scheduleBlock = await _scheduleBlockRepository.GetByIdAsync(request.ScheduleBlockId, cancellationToken);
        if (scheduleBlock == null) throw new InvalidOperationException("El bloque horario no existe.");

        var staff = await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken);
        if (staff == null) throw new InvalidOperationException("El staff no existe.");
        
        var newAppointment = Appointment.Create(
            request.ScheduleBlockId,
            effectivePatientId,
            request.SymptomsRaw,
            request.SchedulingSource);
        
        // guardamos la cita de inmediato
        await _appointmentRepository.AddAppointmentAsync(newAppointment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        // extraemos variables primitivas para no arrastrar las entidades al otro hilo
        var appointmentId = newAppointment.Id;
        var patientDob = patient.DateOfBirth;
        var patientGender = patient.Gender;
        var blockStartTime = scheduleBlock.StartTime;
        var staffSpecialty = staff.Specialty;
        var symptomsRaw = request.SymptomsRaw;
        var patientId = effectivePatientId;
        
        // ejecucion de ia en segundo plano
        // FIRE-AND-FORGET
        _ = Task.Run(async () =>
        {
            // creamos un ciclo de vida nuevo para que el DbContext no muera con el HTTP Request
            using var scope = _scopeFactory.CreateScope();
            
            var aiService = scope.ServiceProvider.GetRequiredService<IAiClinicalService>();
            var appRepo = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CreateAppointmentCommandHandler>>();

            try
            {
                // reconstruimos la lógica matemática
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                int age = today.Year - patientDob.Year;
                if (patientDob > today.AddYears(-age)) age--;

                int daysOfWaiting = (blockStartTime.Date - DateTime.UtcNow.Date).Days;
                if (daysOfWaiting < 0) daysOfWaiting = 0;

                // usamos CancellationToken.None para que la tarea no se cancele al devolver el 200 OK
                int previousAbsences = await appRepo.GetNoShowCountByPatientId(patientId, CancellationToken.None);

                string specialtyNormalized = staffSpecialty?.Trim().ToLowerInvariant().Replace(" ", "_") ?? "medicina_general";

                var predictRequest = new PredictRequestDto(
                    appointmentId, age, patientGender, daysOfWaiting, specialtyNormalized, previousAbsences, "ninguno"
                );

                // llamadas a Python
                var nlpTask = aiService.GetNlpSummaryAsync(appointmentId, symptomsRaw, CancellationToken.None);
                var predictTask = aiService.PredictNoShowRiskAsync(predictRequest, CancellationToken.None);

                await Task.WhenAll(nlpTask, predictTask);

                var nlpResult = await nlpTask;
                var predictResult = await predictTask;

                string processedSymptomsJson = JsonSerializer.Serialize(nlpResult.ExtractedSymptoms);

                // recuperamos la cita en este nuevo DbContext y la actualizamos
                var appointmentToUpdate = await appRepo.GetAppointmentByIdAsync(appointmentId, CancellationToken.None);
                if (appointmentToUpdate != null)
                {
                    appointmentToUpdate.ApplyNlpAnalysis(nlpResult.Summary, processedSymptomsJson);
                    appointmentToUpdate.UpdateRiskScore(predictResult.RiskScore, predictResult.RiskBand);
                    
                    await uow.SaveChangesAsync(CancellationToken.None);
                    logger.LogInformation("Análisis de IA completado y guardado para la cita {CitaId}", appointmentId);
                }

                // llamamos al orquestador si el riesgo es moderado o alto
                if (predictResult.RiskBand == RiskThresholds.HighRiskBand || predictResult.RiskScore >= RiskThresholds.ModerateRiskMin)
                {
                    await publisher.Publish(
                        new AnomalyDetected(
                            TenantId: RiskThresholds.DefaultTenantId,
                            CorrelationId: Guid.NewGuid(),
                            AnomalyType: RiskThresholds.AppointmentNoShowAnomalyType,
                            ResourceId: appointmentId,
                            RiskProbability: predictResult.RiskScore,
                            Attributes: new Dictionary<string, string>
                            {
                                ["riskBand"] = predictResult.RiskBand ?? string.Empty,
                                ["specialty"] = specialtyNormalized
                            }),
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo catastrófico en el background worker de IA para la cita {CitaId}", appointmentId);
            }
        });
        
        return newAppointment.Id;
    }
}