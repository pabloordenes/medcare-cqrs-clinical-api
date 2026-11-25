using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.WaitingLists.Services;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Appointments.Services;

/// <summary>
/// Evalúa si al liberarse un bloque horario por cancelación, declinación o reagendamiento
/// se debe emitir SlotReleased hacia la lista de espera o suprimirlo si existe un sobrecupo ya ocupado.
/// </summary>
public class SlotReleaseService : ISlotReleaseService
{
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IWaitingListOfferCancellationService _offerCancellationService;
    private readonly ILogger<SlotReleaseService> _logger;

    public SlotReleaseService(
        IScheduleBlockRepository scheduleBlockRepository,
        IAppointmentRepository appointmentRepository,
        IStaffRepository staffRepository,
        IBoxRepository boxRepository,
        IWaitingListOfferCancellationService offerCancellationService,
        ILogger<SlotReleaseService> logger)
    {
        _scheduleBlockRepository = scheduleBlockRepository;
        _appointmentRepository = appointmentRepository;
        _staffRepository = staffRepository;
        _boxRepository = boxRepository;
        _offerCancellationService = offerCancellationService;
        _logger = logger;
    }

    public async Task<SlotReleasePreparationResult> PrepareSlotVacatedAsync(
        Appointment vacatedAppointment,
        ScheduleBlock? vacatedBlock,
        string triggerReason,
        Guid correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vacatedAppointment);

        if (vacatedBlock is null)
            return new SlotReleasePreparationResult(false);

        var box = await _boxRepository.GetByIdAsync(vacatedBlock.BoxId, cancellationToken);
        var staff = await _staffRepository.GetByIdAsync(vacatedBlock.StaffId, cancellationToken);
        var isFutureAndActiveBox = vacatedBlock.StartTime > utcNow.UtcDateTime && box is { IsActive: true };

        if (vacatedBlock.IsOverbook)
        {
            if (isFutureAndActiveBox && vacatedBlock.SourceAppointmentId.HasValue)
            {
                var sourceApp = await _appointmentRepository.GetAppointmentByIdAsync(vacatedBlock.SourceAppointmentId.Value, cancellationToken);
                if (sourceApp is { Status: AppointmentStatus.Scheduled, IsPatientConfirmed: false })
                {
                    vacatedBlock.RevertOverbookToOpen(utcNow);
                    var specialty = staff?.Specialty ?? "Atención médica";
                    return new SlotReleasePreparationResult(
                        false,
                        OverbookOpenedEvent: new OverbookOpened(
                            OverbookBlockId: vacatedBlock.Id,
                            TenantId: vacatedAppointment.TenantId,
                            Specialty: specialty,
                            StaffId: vacatedBlock.StaffId,
                            BoxId: vacatedBlock.BoxId,
                            StartTimeUtc: vacatedBlock.StartTime,
                            EndTimeUtc: vacatedBlock.EndTime,
                            SourceAppointmentId: vacatedBlock.SourceAppointmentId.Value,
                            DecisionMode: OverbookDecisionMode.Autonomous,
                            CorrelationId: correlationId,
                            OccurredAtUtc: utcNow));
                }
            }

            vacatedBlock.DeactivateOverbook($"OverbookVacated:{triggerReason}", utcNow);
            return new SlotReleasePreparationResult(false);
        }

        // Si el bloque regular liberado tenía un sobrecupo vinculado:
        var linkedOverbook = await _scheduleBlockRepository.GetOverbookByAppointmentIdForUpdateAsync(
            vacatedAppointment.Id,
            cancellationToken);

        if (linkedOverbook is not null)
        {
            var occupiedOverbookAppointment = linkedOverbook.OccupiedAppointmentId.HasValue
                ? await _appointmentRepository.GetAppointmentByIdAsync(linkedOverbook.OccupiedAppointmentId.Value, cancellationToken)
                : await _appointmentRepository.GetActiveByScheduleBlockIdAsync(linkedOverbook.Id, cancellationToken);

            if (linkedOverbook.OverbookStatus == OverbookBlockStatus.Occupied ||
                occupiedOverbookAppointment is not null)
            {
                _logger.LogInformation(
                    "SlotReleased SUPRIMIDO para bloque {BlockId} tras {Reason} de cita {AppointmentId}: el sobrecupo {OverbookBlockId} ya fue ocupado por otro paciente.",
                    vacatedBlock.Id,
                    triggerReason,
                    vacatedAppointment.Id,
                    linkedOverbook.Id);
                return new SlotReleasePreparationResult(SuppressedByOccupiedOverbook: true);
            }

            if (linkedOverbook.OverbookStatus is OverbookBlockStatus.Open or OverbookBlockStatus.Offered)
            {
                await _offerCancellationService.CancelPendingOffersForOverbookUndoAsync(
                    linkedOverbook.Id,
                    $"SourceSlotVacated:{triggerReason}",
                    utcNow,
                    correlationId,
                    cancellationToken);

                linkedOverbook.DeactivateOverbook($"SourceSlotVacated:{triggerReason}", utcNow);
            }
        }

        vacatedAppointment.ClearDisplacedByOverbook(utcNow);

        if (!isFutureAndActiveBox || staff is null || string.IsNullOrWhiteSpace(staff.Specialty))
            return new SlotReleasePreparationResult(false);

        return new SlotReleasePreparationResult(
            SuppressedByOccupiedOverbook: false,
            SlotReleasedEvent: new SlotReleased(
                ScheduleBlockId: vacatedBlock.Id,
                TenantId: vacatedAppointment.TenantId,
                Specialty: staff.Specialty,
                StaffId: vacatedBlock.StaffId,
                BoxId: vacatedBlock.BoxId,
                StartTimeUtc: DateTime.SpecifyKind(vacatedBlock.StartTime, DateTimeKind.Utc),
                EndTimeUtc: DateTime.SpecifyKind(vacatedBlock.EndTime, DateTimeKind.Utc),
                VacatedAppointmentId: vacatedAppointment.Id,
                TriggerReason: triggerReason,
                CorrelationId: correlationId,
                OccurredAtUtc: utcNow));
    }
}
