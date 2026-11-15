using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Appointments.Commands.GenerateOverbook;

public class GenerateOverbookCommandHandler : IRequestHandler<GenerateOverbookCommand, bool>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GenerateOverbookCommandHandler> _logger;

    public GenerateOverbookCommandHandler(
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IUnitOfWork unitOfWork,
        ILogger<GenerateOverbookCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<bool> Handle(GenerateOverbookCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Iniciando generación de SOBRECUPO para la cita {ResourceId}. Riesgo detectado: {Risk}", 
            request.ResourceId, request.TargetRisk);

        // traemos la cita original
        var originalAppointment = await _appointmentRepository.GetAppointmentByIdAsync(request.ResourceId, cancellationToken);
        if (originalAppointment == null)
        {
            _logger.LogWarning("La cita {ResourceId} no fue encontrada.", request.ResourceId);
            return false;
        }

        // traemos el bloque original
        var originalBlock = await _scheduleBlockRepository.GetByIdAsync(originalAppointment.ScheduleBlockId, cancellationToken);
        if (originalBlock == null)
        {
            _logger.LogWarning("El bloque horario {BlockId} no fue encontrado.", originalAppointment.ScheduleBlockId);
            return false;
        }

        // creamos un clon fisico
        var overbookBlock = ScheduleBlock.Create(
            originalBlock.StaffId,
            originalBlock.BoxId,
            originalBlock.StartTime,
            originalBlock.EndTime,
            originalBlock.RuleGroupId,
            originalBlock.RuleStartDate,
            originalBlock.RuleEndDate,
            originalBlock.RuleStartTime,
            originalBlock.RuleEndTime,
            originalBlock.RuleDaysOfWeek
        );
        
        _scheduleBlockRepository.AddRange(new[] { overbookBlock });
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Sobrecupo generado exitosamente con el nuevo ScheduleBlockId: {NewBlockId}", overbookBlock.Id);

        return true;
    }
}