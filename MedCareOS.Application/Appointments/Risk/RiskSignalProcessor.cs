using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.Appointments.Risk;

public class RiskSignalProcessor : IRiskSignalProcessor
{
    private readonly IRiskSignalRepository _riskSignalRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IReceptionTaskRepository _receptionTaskRepository;
    private readonly IOmroAnomalyPublisher _anomalyPublisher;

    public RiskSignalProcessor(
        IRiskSignalRepository riskSignalRepository,
        IAppointmentRepository appointmentRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IReceptionTaskRepository receptionTaskRepository,
        IOmroAnomalyPublisher anomalyPublisher)
    {
        _riskSignalRepository = riskSignalRepository;
        _appointmentRepository = appointmentRepository;
        _policyRepository = policyRepository;
        _receptionTaskRepository = receptionTaskRepository;
        _anomalyPublisher = anomalyPublisher;
    }

    public async Task<RiskSignalApplicationResult> RecordAsync(
        RecordRiskSignalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alreadyExists = await _riskSignalRepository.ExistsByIdempotencyKeyAsync(
            request.AppointmentId,
            request.IdempotencyKey,
            cancellationToken);

        if (alreadyExists)
        {
            return new RiskSignalApplicationResult(
                Applied: false,
                DuplicateSkipped: true);
        }

        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(
            request.AppointmentId,
            cancellationToken);

        if (appointment is null)
        {
            return new RiskSignalApplicationResult(
                Applied: false,
                DuplicateSkipped: false);
        }

        var policy = await _policyRepository.GetOrDefaultAsync(appointment.TenantId, cancellationToken);
        var weight = request.WeightOverride ?? ResolvePolicyWeight(request.Kind, policy);

        var signal = RiskSignal.Create(
            tenantId: appointment.TenantId,
            appointmentId: appointment.Id,
            kind: request.Kind,
            weight: weight,
            source: request.Source,
            idempotencyKey: request.IdempotencyKey,
            occurredAt: request.OccurredAtUtc,
            correlationId: request.CorrelationId,
            metadataJson: request.MetadataJson,
            createdAt: request.OccurredAtUtc);

        await _riskSignalRepository.AddAsync(signal, cancellationToken);

        var previousScore = appointment.EffectiveRiskScore ?? appointment.CurrentBaseModelScore;
        var existingSignals = await _riskSignalRepository.GetByAppointmentIdAsync(appointment.Id, cancellationToken);

        var merged = new List<RiskSignal>(existingSignals.Count + 1);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in existingSignals)
        {
            if (seenKeys.Add(item.IdempotencyKey))
            {
                merged.Add(item);
            }
        }
        if (seenKeys.Add(signal.IdempotencyKey))
        {
            merged.Add(signal);
        }

        var (previousBand, newBand) = appointment.RecalculateEffectiveRisk(merged, policy, request.OccurredAtUtc);

        bool riskEscalatedPublished = false;
        if (!request.SuppressBandEscalationEvent &&
            RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, previousBand, newBand))
        {
            riskEscalatedPublished = await _anomalyPublisher.PublishAsync(
                new PublishAnomalyContext(
                    Appointment: appointment,
                    AnomalyType: RiskThresholds.RiskEscalatedAnomalyType,
                    CorrelationId: request.CorrelationId,
                    OccurredAtUtc: request.OccurredAtUtc,
                    TriggerSource: request.Source,
                    PreviousBand: previousBand),
                cancellationToken);
        }

        bool receptionTaskCreated = false;
        if (request.Kind == RiskSignalKind.DeliveryFailed && request.CreateFailedDeliveryReceptionTask)
        {
            var hasActiveTask = await _receptionTaskRepository.HasActiveTaskAsync(
                appointment.Id,
                ReceptionTaskType.FailedNotificationEscalation,
                cancellationToken);

            if (!hasActiveTask)
            {
                var reason = string.IsNullOrWhiteSpace(request.ReceptionTaskReason)
                    ? "Fallo entrega de notificación al paciente. Contactar por otro canal."
                    : request.ReceptionTaskReason.Trim();

                await _receptionTaskRepository.AddAsync(
                    ReceptionTask.Create(
                        appointment.Id,
                        appointment.PatientId,
                        ReceptionTaskType.FailedNotificationEscalation,
                        ReceptionTaskPriority.High,
                        reason),
                    cancellationToken);
                receptionTaskCreated = true;
            }
        }

        return new RiskSignalApplicationResult(
            Applied: true,
            DuplicateSkipped: false,
            SignalId: signal.Id,
            PreviousEffectiveScore: previousScore,
            NewEffectiveScore: appointment.EffectiveRiskScore,
            PreviousBand: previousBand,
            NewBand: newBand,
            ReceptionTaskCreated: receptionTaskCreated,
            RiskEscalatedEventPublished: riskEscalatedPublished);
    }

    private static decimal ResolvePolicyWeight(RiskSignalKind kind, TenantConfirmationPolicy policy) => kind switch
    {
        RiskSignalKind.ConfirmedEarly => policy.WeightConfirmedEarly,
        RiskSignalKind.ReminderUnanswered => policy.WeightReminderUnanswered,
        RiskSignalKind.DeliveryFailed => policy.WeightDeliveryFailed,
        RiskSignalKind.RepliedWithoutConfirmation => policy.WeightRepliedWithoutConfirmation,
        RiskSignalKind.FinalConfirmed => policy.WeightFinalConfirmed,
        RiskSignalKind.DeclinedByPatient => 0m,
        RiskSignalKind.ModelRescored => 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de señal de riesgo no soportado.")
    };
}
