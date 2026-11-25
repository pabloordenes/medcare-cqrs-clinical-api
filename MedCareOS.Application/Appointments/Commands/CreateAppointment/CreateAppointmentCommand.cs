using System.Text.Json.Serialization;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CreateAppointment;

public record CreateAppointmentCommand(
    Guid ScheduleBlockId,
    Guid PatientId,
    string SymptomsRaw,
    string SchedulingSource,
    Guid? AuthenticatedUserId = null,
    [property: JsonIgnore] bool AllowOverbookBlock = false
    ) : IRequest<Guid>;
