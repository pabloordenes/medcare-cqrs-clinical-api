using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
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

        var existingOverbook = await _scheduleBlockRepository.GetOverbookByAppointmentIdAsync(request.ResourceId, cancellationToken);
        if (existingOverbook != null)
        {
            _logger.LogInformation(
                "Sobrecupo ya existente ({OverbookBlockId}) para la cita {ResourceId}. Retornando éxito idempotente.",
                existingOverbook.Id, request.ResourceId);
            return true;
        }

        // traemos la cita original (NO se cancela)
        var originalAppointment = await _appointmentRepository.GetAppointmentByIdAsync(request.ResourceId, cancellationToken);
        if (originalAppointment == null)
        {
            _logger.LogWarning("La cita {ResourceId} no fue encontrada.", request.ResourceId);
            return false;
        }

        if (originalAppointment.Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
        {
            _logger.LogWarning(
                "No se puede generar sobrecupo para la cita {ResourceId} en estado {Status}.",
                request.ResourceId, originalAppointment.Status);
            return false;
        }

        // traemos el bloque original
        var originalBlock = await _scheduleBlockRepository.GetByIdAsync(originalAppointment.ScheduleBlockId, cancellationToken);
        if (originalBlock == null)
        {
            _logger.LogWarning("El bloque horario {BlockId} no fue encontrado.", originalAppointment.ScheduleBlockId);
            return false;
        }

        if (originalBlock.StartTime <= DateTime.UtcNow)
        {
            _logger.LogWarning(
                "No se puede generar sobrecupo para el bloque {BlockId} porque no está en el futuro ({StartTime}).",
                originalBlock.Id, originalBlock.StartTime);
            return false;
        }

        // creamos un bloque de sobrecupo aislado (sin heredar RuleGroupId ni reglas de recurrencia)
        var overbookBlock = ScheduleBlock.CreateOverbook(originalBlock, originalAppointment.Id);

        _scheduleBlockRepository.AddRange(new[] { overbookBlock });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Sobrecupo generado exitosamente con el nuevo ScheduleBlockId: {NewBlockId} para la cita {AppointmentId}",
            overbookBlock.Id, originalAppointment.Id);

        return true;
    }
}
