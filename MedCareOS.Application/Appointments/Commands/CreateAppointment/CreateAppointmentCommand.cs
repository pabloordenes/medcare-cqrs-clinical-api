using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CreateAppointment;

public record CreateAppointmentCommand(
    Guid ScheduleBlockId,
    Guid PatientId,
    string SymptomsRaw,
    string SchedulingSource,
    Guid? AuthenticatedUserId = null
    ) : IRequest<Guid>;
