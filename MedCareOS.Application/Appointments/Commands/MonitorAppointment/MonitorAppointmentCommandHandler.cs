using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Appointments.Commands.MonitorAppointment;

public class MonitorAppointmentCommandHandler : IRequestHandler<MonitorAppointmentCommand>
{
    private const string ManualConfirmationNotificationType = "Confirmación Manual";
    private readonly IApplicationDbContext _dbContext;

    public MonitorAppointmentCommandHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Handle(MonitorAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _dbContext.Appointments.FindAsync(new object[] { request.ResourceId }, cancellationToken);

        if (appointment == null)
            throw new KeyNotFoundException("La cita no existe.");

        if (request.RequiresAction)
        {
            var alreadyExists = await _dbContext.Notifications.AnyAsync(
                n => n.AppointmentId == appointment.Id && n.Type == ManualConfirmationNotificationType,
                cancellationToken);

            if (alreadyExists)
                return;

            var notification = Notification.Create(
                appointmentId: appointment.Id,
                patientId: appointment.PatientId,
                type: ManualConfirmationNotificationType,
                channel: "Teléfono/WhatsApp",
                content: "Riesgo moderado detectado por IA. Contactar al paciente urgentemente para re-confirmar asistencia."
            );

            _dbContext.Notifications.Add(notification);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
