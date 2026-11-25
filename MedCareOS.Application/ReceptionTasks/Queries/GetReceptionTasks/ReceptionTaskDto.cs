using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.ReceptionTasks.Queries.GetReceptionTasks;

public record ReceptionTaskDto(
    Guid Id,
    Guid AppointmentId,
    Guid PatientId,
    string PatientName,
    string PatientPhone,
    DateTime AppointmentStartTime,
    string Specialty,
    decimal? RiskScore,
    ReceptionTaskType TaskType,
    ReceptionTaskPriority Priority,
    ReceptionTaskStatus Status,
    string Reason,
    Guid? AssignedToUserId,
    string? ResolutionNotes,
    Guid? ResolvedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);

public record PagedReceptionTasksDto(
    int TotalCount,
    int Page,
    int PageSize,
    List<ReceptionTaskDto> Items);
