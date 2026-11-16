using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CheckInAppointment;

public class CheckInAppointmentCommandHandler : IRequestHandler<CheckInAppointmentCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CheckInAppointmentCommandHandler(IAppointmentRepository appointmentRepository, IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(CheckInAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

        appointment.CheckIn();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
