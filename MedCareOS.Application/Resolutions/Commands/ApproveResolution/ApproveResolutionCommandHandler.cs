using System.Text.Json;
using MassTransit;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Omro.Contracts.Events;
using Omro.Contracts.Plans;

namespace MedCareOS.Application.Resolutions.Commands.ApproveResolution;

public class ApproveResolutionCommandHandler : IRequestHandler<ApproveResolutionCommand>
{
    private static readonly JsonSerializerOptions PlanSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAiResolutionTaskRepository _repository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;

    public ApproveResolutionCommandHandler(
        IAiResolutionTaskRepository repository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint)
    {
        _repository = repository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
    }
    
    public async Task Handle(ApproveResolutionCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.ResolutionTaskId, cancellationToken);
        
        if (task == null) throw new KeyNotFoundException("La tarea de resolución no existe.");

        if (task.Status == AiResolutionStatus.Pending)
        {
            var appointment = await _appointmentRepository.GetAppointmentByIdAsync(task.ResourceId, cancellationToken);
            if (appointment is not null &&
                (appointment.OrchestrationSuppressed ||
                 appointment.IsPatientConfirmed ||
                 appointment.Status != AppointmentStatus.Scheduled))
            {
                task.Supersede();
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"La tarea {task.Id} fue reemplazada (Superseded) porque la cita {appointment.Id} ya fue confirmada o dejó de estar activa.");
            }
        }

        // mutacion de dominio
        task.Approve(request.ApprovedBy);

        var structuredPlan = TryParseStructuredV2Plan(task.AiPlanJson);
        var resolutionApprovedEvent = structuredPlan is not null
            ? new ResolutionApproved(
                TenantId: RiskThresholds.DefaultTenantId,
                CorrelationId: Guid.NewGuid(),
                ResolutionTaskId: task.Id,
                ResourceId: task.ResourceId,
                AiPlanJson: task.AiPlanJson,
                SchemaVersion: PlanContractVersions.V2Structured,
                AiPlan: structuredPlan)
            : new ResolutionApproved(
                TenantId: RiskThresholds.DefaultTenantId,
                CorrelationId: Guid.NewGuid(),
                ResolutionTaskId: task.Id,
                ResourceId: task.ResourceId,
                AiPlanJson: task.AiPlanJson,
                SchemaVersion: PlanContractVersions.V1Legacy,
                AiPlan: null);

        await _publishEndpoint.Publish(resolutionApprovedEvent, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static StructuredAiPlan? TryParseStructuredV2Plan(string? aiPlanJson)
    {
        if (string.IsNullOrWhiteSpace(aiPlanJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(aiPlanJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!TryGetPropertyIgnoreCase(doc.RootElement, "schemaVersion", out var versionProp) ||
                versionProp.ValueKind != JsonValueKind.String ||
                !string.Equals(versionProp.GetString()?.Trim(), PlanContractVersions.V2Structured, StringComparison.Ordinal))
            {
                return null;
            }

            var plan = JsonSerializer.Deserialize<StructuredAiPlan>(aiPlanJson, PlanSerializerOptions);
            return plan is { DeferredActions: not null } ? plan : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
            return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}