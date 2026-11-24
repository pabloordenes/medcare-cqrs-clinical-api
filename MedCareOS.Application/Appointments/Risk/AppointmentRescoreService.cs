using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;

namespace MedCareOS.Application.Appointments.Risk;

public class AppointmentRescoreService : IAppointmentRescoreService
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAiClinicalService _aiClinicalService;

    public AppointmentRescoreService(
        IAppointmentRepository appointmentRepository,
        IAiClinicalService aiClinicalService)
    {
        _appointmentRepository = appointmentRepository;
        _aiClinicalService = aiClinicalService;
    }

    public async Task<PredictResponseDto> PredictForAppointmentAsync(
        Appointment appointment,
        Patient patient,
        ScheduleBlock scheduleBlock,
        Staff staff,
        DateTimeOffset referenceUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(scheduleBlock);
        ArgumentNullException.ThrowIfNull(staff);

        var refDate = DateOnly.FromDateTime(referenceUtc.UtcDateTime);
        int age = refDate.Year - patient.DateOfBirth.Year;
        if (patient.DateOfBirth > refDate.AddYears(-age))
            age--;
        if (age < 0)
            age = 0;

        int daysOfWaiting = (scheduleBlock.StartTime.Date - referenceUtc.UtcDateTime.Date).Days;
        if (daysOfWaiting < 0)
            daysOfWaiting = 0;

        int previousAbsences = await _appointmentRepository.GetNoShowCountByPatientId(patient.Id, cancellationToken);
        string specialtyNormalized = staff.Specialty?.Trim().ToLowerInvariant().Replace(" ", "_") ?? "medicina_general";
        string reminderChannel = patient.ToPredictReminderChannel();

        var request = new PredictRequestDto(
            appointment.Id,
            age,
            patient.Gender,
            daysOfWaiting,
            specialtyNormalized,
            previousAbsences,
            reminderChannel);

        return await _aiClinicalService.PredictNoShowRiskAsync(request, cancellationToken);
    }
}
