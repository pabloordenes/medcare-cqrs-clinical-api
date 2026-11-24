using System.Runtime.ExceptionServices;
using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Options;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Omro.Contracts.Events;

namespace MedCareOS.Application.Appointments.Consumers;

public class ScoreAppointmentConsumer : IConsumer<AppointmentCreated>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IAiClinicalService _aiClinicalService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ScoringOptions _scoringOptions;
    private readonly ILogger<ScoreAppointmentConsumer> _logger;

    public ScoreAppointmentConsumer(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        IAiClinicalService aiClinicalService,
        IUnitOfWork unitOfWork,
        IOptions<ScoringOptions> scoringOptions,
        ILogger<ScoreAppointmentConsumer> logger)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _aiClinicalService = aiClinicalService;
        _unitOfWork = unitOfWork;
        _scoringOptions = scoringOptions.Value;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AppointmentCreated> context)
    {
        var cancellationToken = context.CancellationToken;
        var appointmentId = context.Message.AppointmentId;

        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(appointmentId, cancellationToken);
        if (appointment is null)
        {
            _logger.LogWarning(
                "AppointmentCreated recibido para cita inexistente {AppointmentId}. Se descarta el mensaje.",
                appointmentId);
            return;
        }

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Finished)
        {
            _logger.LogInformation(
                "Scoring omitido para la cita {AppointmentId} porque su estado actual es {Status}.",
                appointmentId,
                appointment.Status);
            return;
        }

        var patient = await _patientRepository.GetPatientByIdAsync(appointment.PatientId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el paciente {appointment.PatientId} para la cita {appointmentId}.");

        var scheduleBlock = await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el bloque horario {appointment.ScheduleBlockId} para la cita {appointmentId}.");

        var staff = await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el profesional {scheduleBlock.StaffId} para la cita {appointmentId}.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int age = today.Year - patient.DateOfBirth.Year;
        if (patient.DateOfBirth > today.AddYears(-age)) age--;

        int daysOfWaiting = (scheduleBlock.StartTime.Date - DateTime.UtcNow.Date).Days;
        if (daysOfWaiting < 0) daysOfWaiting = 0;

        int previousAbsences = await _appointmentRepository.GetNoShowCountByPatientId(appointment.PatientId, cancellationToken);
        string specialtyNormalized = staff.Specialty?.Trim().ToLowerInvariant().Replace(" ", "_") ?? "medicina_general";

        var predictRequest = new PredictRequestDto(
            appointmentId,
            age,
            patient.Gender,
            daysOfWaiting,
            specialtyNormalized,
            previousAbsences,
            "ninguno");

        var nlpTask = _aiClinicalService.GetNlpSummaryAsync(appointmentId, appointment.SymptomsRaw, cancellationToken);
        var predictTask = _aiClinicalService.PredictNoShowRiskAsync(predictRequest, cancellationToken);

        try
        {
            await Task.WhenAll(nlpTask, predictTask);
        }
        catch
        {
            var nlpFault = nlpTask.Exception?.InnerException;
            var predictFault = predictTask.Exception?.InnerException;

            if (nlpFault is AiServicePermanentException)
                ExceptionDispatchInfo.Capture(nlpFault).Throw();
            if (predictFault is AiServicePermanentException)
                ExceptionDispatchInfo.Capture(predictFault).Throw();

            throw;
        }

        var nlpResult = await nlpTask;
        var predictResult = await predictTask;

        string processedSymptomsJson = JsonSerializer.Serialize(nlpResult.ExtractedSymptoms);
        appointment.ApplyNlpAnalysis(nlpResult.Summary, processedSymptomsJson);
        appointment.UpdateRiskScore(predictResult.RiskScore, predictResult.RiskBand);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Scoring y análisis NLP completados para la cita {AppointmentId}. Score={Score}, Band={Band}",
            appointmentId,
            predictResult.RiskScore,
            predictResult.RiskBand);

        if (_scoringOptions.PublishAnomalyOnCreate &&
            (predictResult.RiskBand == RiskThresholds.HighRiskBand || predictResult.RiskScore >= RiskThresholds.ModerateRiskMin))
        {
            await context.Publish(
                new AnomalyDetected(
                    TenantId: RiskThresholds.DefaultTenantId,
                    CorrelationId: context.Message.CorrelationId,
                    AnomalyType: RiskThresholds.AppointmentNoShowAnomalyType,
                    ResourceId: appointmentId,
                    RiskProbability: predictResult.RiskScore,
                    Attributes: new Dictionary<string, string>
                    {
                        ["riskBand"] = predictResult.RiskBand ?? string.Empty,
                        ["specialty"] = specialtyNormalized
                    }),
                cancellationToken);
        }
    }
}
