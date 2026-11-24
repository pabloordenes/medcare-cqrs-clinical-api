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
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IRiskSignalRepository _riskSignalRepository;
    private readonly IAiClinicalService _aiClinicalService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ScoringOptions _scoringOptions;
    private readonly ScoringRetryOptions _retryOptions;
    private readonly ILogger<ScoreAppointmentConsumer> _logger;

    public ScoreAppointmentConsumer(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IRiskSignalRepository riskSignalRepository,
        IAiClinicalService aiClinicalService,
        IUnitOfWork unitOfWork,
        IOptions<ScoringOptions> scoringOptions,
        IOptions<ScoringRetryOptions> retryOptions,
        ILogger<ScoreAppointmentConsumer> logger)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _policyRepository = policyRepository;
        _riskSignalRepository = riskSignalRepository;
        _aiClinicalService = aiClinicalService;
        _unitOfWork = unitOfWork;
        _scoringOptions = scoringOptions.Value;
        _retryOptions = retryOptions.Value;
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

        bool needsNlp = appointment.NlpStatus is NlpStatus.Pending or NlpStatus.Failed
                        && !string.IsNullOrWhiteSpace(appointment.SymptomsRaw);
        bool needsPredict = appointment.ScoringStatus is ScoringStatus.Pending or ScoringStatus.Failed;

        if (!needsNlp && !needsPredict)
        {
            _logger.LogInformation(
                "Cita {AppointmentId} ya tiene NLP ({NlpStatus}) y Scoring ({ScoringStatus}) resueltos. No-op.",
                appointmentId,
                appointment.NlpStatus,
                appointment.ScoringStatus);
            return;
        }

        var patient = await _patientRepository.GetPatientByIdAsync(appointment.PatientId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el paciente {appointment.PatientId} para la cita {appointmentId}.");

        var scheduleBlock = await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el bloque horario {appointment.ScheduleBlockId} para la cita {appointmentId}.");

        var staff = await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el profesional {scheduleBlock.StaffId} para la cita {appointmentId}.");

        string specialtyNormalized = staff.Specialty?.Trim().ToLowerInvariant().Replace(" ", "_") ?? "medicina_general";

        Task<(NlpSummaryResponseDto? Result, Exception? Error)> nlpTask = needsNlp
            ? ExecuteSafelyAsync(() => _aiClinicalService.GetNlpSummaryAsync(appointmentId, appointment.SymptomsRaw, cancellationToken))
            : Task.FromResult<(NlpSummaryResponseDto?, Exception?)>((null, null));

        Task<(PredictResponseDto? Result, Exception? Error)> predictTask = needsPredict
            ? ExecutePredictAsync(appointment.Id, appointment.PatientId, patient, scheduleBlock, specialtyNormalized, cancellationToken)
            : Task.FromResult<(PredictResponseDto?, Exception?)>((null, null));

        await Task.WhenAll(nlpTask, predictTask);

        var (nlpResult, nlpError) = await nlpTask;
        var (predictResult, predictError) = await predictTask;

        if (nlpResult is not null)
        {
            string processedSymptomsJson = JsonSerializer.Serialize(nlpResult.ExtractedSymptoms);
            appointment.ApplyNlpAnalysis(nlpResult.Summary, processedSymptomsJson);
        }

        if (predictResult is not null)
        {
            var scoreUtc = DateTimeOffset.UtcNow;
            var policy = await _policyRepository.GetOrDefaultAsync(appointment.TenantId, cancellationToken);
            appointment.RecordInitialModelScore(
                predictResult.RiskScore,
                predictResult.RiskBand,
                policy.GetHysteresisThresholds(),
                scoreUtc);

            var existingSignals = await _riskSignalRepository.GetByAppointmentIdAsync(appointment.Id, cancellationToken);
            if (existingSignals.Count > 0)
            {
                appointment.RecalculateEffectiveRisk(existingSignals, policy, scoreUtc);
            }
        }

        if (nlpError is not null || predictError is not null)
        {
            var utcNow = DateTimeOffset.UtcNow;
            var permanentException = FindPermanentException(predictError, nlpError);
            var combinedError = BuildCombinedError(predictError, nlpError);

            if (permanentException is not null)
            {
                appointment.MarkScoringTerminalFailure(
                    combinedError,
                    utcNow,
                    scoringFailed: predictError is not null,
                    nlpFailed: nlpError is not null);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                ExceptionDispatchInfo.Capture(permanentException).Throw();
            }

            var nextAttempt = appointment.ScoringAttempts + 1;
            var backoff = _retryOptions.ComputeBackoff(nextAttempt);

            appointment.RecordScoringFailure(
                combinedError,
                _retryOptions.MaxAttempts,
                utcNow,
                backoff,
                scoringFailed: predictError is not null,
                nlpFailed: nlpError is not null);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var transientException = predictError ?? nlpError!;
            ExceptionDispatchInfo.Capture(transientException).Throw();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Análisis IA completado para cita {AppointmentId}. ScoringStatus={ScoringStatus}, NlpStatus={NlpStatus}, Score={Score}, Band={Band}",
            appointmentId,
            appointment.ScoringStatus,
            appointment.NlpStatus,
            appointment.NoShowRiskScore,
            appointment.RiskBand);

        if (predictResult is not null &&
            _scoringOptions.PublishAnomalyOnCreate &&
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

    private async Task<(PredictResponseDto? Result, Exception? Error)> ExecutePredictAsync(
        Guid appointmentId,
        Guid patientId,
        Domain.Entities.Patient patient,
        Domain.Entities.ScheduleBlock scheduleBlock,
        string specialtyNormalized,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int age = today.Year - patient.DateOfBirth.Year;
        if (patient.DateOfBirth > today.AddYears(-age)) age--;

        int daysOfWaiting = (scheduleBlock.StartTime.Date - DateTime.UtcNow.Date).Days;
        if (daysOfWaiting < 0) daysOfWaiting = 0;

        int previousAbsences = await _appointmentRepository.GetNoShowCountByPatientId(patientId, cancellationToken);

        var predictRequest = new PredictRequestDto(
            appointmentId,
            age,
            patient.Gender,
            daysOfWaiting,
            specialtyNormalized,
            previousAbsences,
            patient.ToPredictReminderChannel());

        return await ExecuteSafelyAsync(() => _aiClinicalService.PredictNoShowRiskAsync(predictRequest, cancellationToken));
    }

    private static async Task<(T? Result, Exception? Error)> ExecuteSafelyAsync<T>(Func<Task<T>> action)
        where T : class
    {
        try
        {
            var result = await action();
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static Exception? FindPermanentException(params Exception?[] errors)
    {
        foreach (var err in errors)
        {
            if (err is AiServicePermanentException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
                return err;
        }
        return null;
    }

    private static string BuildCombinedError(Exception? predictError, Exception? nlpError)
    {
        if (predictError is not null && nlpError is not null)
            return $"Predict: {predictError.Message} | NLP: {nlpError.Message}";
        return predictError?.Message ?? nlpError?.Message ?? "Error desconocido";
    }
}
