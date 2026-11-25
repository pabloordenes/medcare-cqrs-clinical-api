using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Appointments.Queries.GetMyAppointments;

public class GetMyAppointmentsQueryHandler
    : IRequestHandler<GetMyAppointmentsQuery, List<PatientAppointmentSummaryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IApplicationDbContext _dbContext;

    public GetMyAppointmentsQueryHandler(
        IPatientRepository patientRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IApplicationDbContext dbContext)
    {
        _patientRepository = patientRepository;
        _policyRepository = policyRepository;
        _dbContext = dbContext;
    }

    public async Task<List<PatientAppointmentSummaryDto>> Handle(
        GetMyAppointmentsQuery request,
        CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId, cancellationToken)
            ?? throw new KeyNotFoundException("El paciente autenticado no tiene perfil registrado.");

        var utcNow = DateTime.UtcNow;

        var rows = await (
            from app in _dbContext.Appointments.AsNoTracking()
            join block in _dbContext.ScheduleBlocks.AsNoTracking() on app.ScheduleBlockId equals block.Id
            join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
            where app.PatientId == patient.Id
                  && (request.IncludePast || block.EndTime >= utcNow)
            orderby block.StartTime ascending
            select new
            {
                app.Id,
                app.TenantId,
                app.ScheduleBlockId,
                block.StartTime,
                block.EndTime,
                Specialty = staff.Specialty ?? "Atención médica",
                DoctorName = staff.FirstName + " " + staff.LastName,
                block.StaffId,
                block.BoxId,
                app.Status,
                app.ConfirmationState,
                app.IsDisplacedByOverbook,
                app.WasEverDisplacedByOverbook,
                app.ReschedulesCount
            }
        ).ToListAsync(cancellationToken);

        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var maxByTenant = new Dictionary<Guid, int>();
        foreach (var tid in tenantIds)
        {
            var policy = await _policyRepository.GetOrDefaultAsync(tid, cancellationToken);
            maxByTenant[tid] = policy.MaxReschedulesPerAppointment;
        }

        return rows.Select(r =>
        {
            var maxAllowed = maxByTenant.GetValueOrDefault(r.TenantId, 3);
            var isFuture = r.StartTime > utcNow;
            var isModifiableState = r.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed;
            var isDisplaced = r.IsDisplacedByOverbook || r.WasEverDisplacedByOverbook;

            return new PatientAppointmentSummaryDto(
                AppointmentId: r.Id,
                ScheduleBlockId: r.ScheduleBlockId,
                StartTimeUtc: DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc),
                EndTimeUtc: DateTime.SpecifyKind(r.EndTime, DateTimeKind.Utc),
                Specialty: r.Specialty,
                DoctorName: r.DoctorName,
                StaffId: r.StaffId,
                BoxId: r.BoxId,
                Status: r.Status,
                ConfirmationState: r.ConfirmationState,
                IsDisplacedByOverbook: r.IsDisplacedByOverbook,
                WasEverDisplacedByOverbook: r.WasEverDisplacedByOverbook,
                ReschedulesCount: r.ReschedulesCount,
                MaxReschedulesAllowed: maxAllowed,
                CanCancel: isModifiableState && isFuture,
                CanReschedule: isModifiableState && isFuture && (isDisplaced || r.ReschedulesCount < maxAllowed));
        }).ToList();
    }
}
