using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Entities;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.MonitorAppointment;

public class MonitorAppointmentCommandHandler :  IRequestHandler<MonitorAppointmentCommand>
{
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
            // Creamos una orden de trabajo para que la recepcionista contacte al paciente
            var notification = Notification.Create(
                appointmentId: appointment.Id,
                patientId: appointment.PatientId,
                type: "Confirmación Manual",
                channel: "Teléfono/WhatsApp",
                content: "Riesgo moderado (50/50) detectado por IA. Contactar al paciente urgentemente para re-confirmar asistencia."
            );

            _dbContext.Notifications.Add(notification);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
    
}