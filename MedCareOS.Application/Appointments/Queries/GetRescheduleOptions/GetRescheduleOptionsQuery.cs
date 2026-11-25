using MediatR;

namespace MedCareOS.Application.Appointments.Queries.GetRescheduleOptions;

public sealed record RescheduleSlotOptionDto(
    Guid ScheduleBlockId,
    Guid StaffId,
    string DoctorName,
    Guid BoxId,
    string Specialty,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    bool IsSameStaff,
    bool IsPrioritySlot);

public sealed record RescheduleOptionsResponseDto(
    Guid AppointmentId,
    Guid CurrentScheduleBlockId,
    string Specialty,
    bool PriorityAccess,
    int ReschedulesCount,
    int? ReschedulesRemaining,
    IReadOnlyList<RescheduleSlotOptionDto> AvailableOptions);

public sealed record GetRescheduleOptionsQuery(
    Guid AppointmentId,
    Guid? AuthenticatedUserId,
    Guid? ExpectedPatientId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? PreferredStaffId = null) : IRequest<RescheduleOptionsResponseDto>;
