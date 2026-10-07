using MediatR;

namespace MedCareOS.Application.Appointments.Queries.GetDailyAppointments;

public record GetDailyAppointmentsQuery(
    Guid DoctorId,
    DateTime AppointmentDate
    ) : IRequest<List<DailyAppointmentDto>>;