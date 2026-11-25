using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Appointments.Queries.GetMyAppointments;

public sealed record PatientAppointmentSummaryDto(
    Guid AppointmentId,
    Guid ScheduleBlockId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string Specialty,
    string DoctorName,
    Guid StaffId,
    Guid BoxId,
    AppointmentStatus Status,
    ConfirmationState ConfirmationState,
    bool IsDisplacedByOverbook,
    bool WasEverDisplacedByOverbook,
    int ReschedulesCount,
    int MaxReschedulesAllowed,
    bool CanCancel,
    bool CanReschedule);

public sealed record GetMyAppointmentsQuery(
    Guid AuthenticatedUserId,
    bool IncludePast = false) : IRequest<List<PatientAppointmentSummaryDto>>;
