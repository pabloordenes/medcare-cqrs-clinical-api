using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Appointments.Queries.GetRescheduleOptions;

public class GetRescheduleOptionsQueryHandler
    : IRequestHandler<GetRescheduleOptionsQuery, RescheduleOptionsResponseDto>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IApplicationDbContext _dbContext;

    public GetRescheduleOptionsQueryHandler(
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        IPatientRepository patientRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IApplicationDbContext dbContext)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _patientRepository = patientRepository;
        _policyRepository = policyRepository;
        _dbContext = dbContext;
    }

    public async Task<RescheduleOptionsResponseDto> Handle(
        GetRescheduleOptionsQuery request,
        CancellationToken cancellationToken)
    {
        Guid? expectedPatientId = null;
        if (request.AuthenticatedUserId.HasValue)
        {
            var caller = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
                ?? throw new UnauthorizedAccessException("El usuario autenticado no tiene perfil de paciente.");
            expectedPatientId = caller.Id;
        }
        else if (request.ExpectedPatientId.HasValue && request.ExpectedPatientId.Value != Guid.Empty)
        {
            expectedPatientId = request.ExpectedPatientId.Value;
        }

        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita {request.AppointmentId} no existe.");

        if (expectedPatientId.HasValue && appointment.PatientId != expectedPatientId.Value)
            throw new UnauthorizedAccessException("No tienes permiso para consultar opciones de reagendamiento de otro paciente.");

        if (appointment.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed))
            throw new InvalidOperationException($"Una cita en estado '{appointment.Status}' no admite reagendamiento.");

        var currentBlock = await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, cancellationToken)
            ?? throw new KeyNotFoundException("El bloque actual de la cita no existe.");
        var currentStaff = await _staffRepository.GetByIdAsync(currentBlock.StaffId, cancellationToken)
            ?? throw new KeyNotFoundException("El profesional actual de la cita no existe.");

        var policy = await _policyRepository.GetOrDefaultAsync(appointment.TenantId, cancellationToken);
        var isDisplaced = appointment.IsDisplacedByOverbook || appointment.WasEverDisplacedByOverbook;
        int? remaining = isDisplaced
            ? null
            : Math.Max(0, policy.MaxReschedulesPerAppointment - appointment.ReschedulesCount);

        var utcNow = DateTime.UtcNow;
        var utcNowOffset = new DateTimeOffset(utcNow, TimeSpan.Zero);
        var fromUtc = request.FromDate.HasValue
            ? request.FromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : utcNow;
        var toUtc = request.ToDate.HasValue
            ? request.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : utcNow.AddDays(30);

        var candidates = await (
            from block in _dbContext.ScheduleBlocks.AsNoTracking()
            where !block.IsOverbook
                  && block.Id != appointment.ScheduleBlockId
                  && block.StartTime > utcNow
                  && block.StartTime >= fromUtc
                  && block.EndTime < toUtc
            join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
            where staff.Specialty == currentStaff.Specialty
                  && (!request.PreferredStaffId.HasValue || block.StaffId == request.PreferredStaffId.Value)
            join box in _dbContext.Boxes.AsNoTracking() on block.BoxId equals box.Id
            where box.IsActive
            where !_dbContext.Appointments.Any(a =>
                a.ScheduleBlockId == block.Id && a.Status != AppointmentStatus.Cancelled)
            where !_dbContext.WaitingListOffers.Any(o =>
                o.ScheduleBlockId == block.Id
                && o.Status == WaitingListOfferStatus.Pending
                && o.ExpiresAt > utcNowOffset)
            select new RescheduleSlotOptionDto(
                block.Id,
                block.StaffId,
                staff.FirstName + " " + staff.LastName,
                block.BoxId,
                staff.Specialty ?? "Atención médica",
                DateTime.SpecifyKind(block.StartTime, DateTimeKind.Utc),
                DateTime.SpecifyKind(block.EndTime, DateTimeKind.Utc),
                block.StaffId == currentBlock.StaffId,
                isDisplaced)
        ).ToListAsync(cancellationToken);

        var ordered = isDisplaced
            ? candidates.OrderBy(c => c.StartTimeUtc).ThenByDescending(c => c.IsSameStaff).ToList()
            : candidates.OrderByDescending(c => c.IsSameStaff).ThenBy(c => c.StartTimeUtc).ToList();

        return new RescheduleOptionsResponseDto(
            AppointmentId: appointment.Id,
            CurrentScheduleBlockId: appointment.ScheduleBlockId,
            Specialty: currentStaff.Specialty ?? "Atención médica",
            PriorityAccess: isDisplaced,
            ReschedulesCount: appointment.ReschedulesCount,
            ReschedulesRemaining: remaining,
            AvailableOptions: ordered);
    }
}
