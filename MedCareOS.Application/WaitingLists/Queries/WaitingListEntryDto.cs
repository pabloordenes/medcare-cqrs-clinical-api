using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.WaitingLists.Queries;

public sealed record WaitingListEntryDto(
    Guid Id,
    Guid TenantId,
    Guid PatientId,
    string Specialty,
    DateTimeOffset RegistrationDate,
    int Priority,
    WaitingListStatus Status,
    string? ClinicalContextJson,
    Guid? PreferredStaffId,
    DateOnly? PreferredFromDate,
    DateOnly? PreferredToDate,
    TimeOnly? PreferredTimeStart,
    TimeOnly? PreferredTimeEnd,
    bool IsDisplacedBonus,
    Guid? DisplacedSourceAppointmentId,
    DateTimeOffset? ExpiresAt,
    decimal? RankScore = null,
    string? ScoreBreakdown = null);
