using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class Appointment
{
    public Guid Id { get; private set; }
    public Guid ScheduleBlockId { get; private set; }
    public Guid PatientId { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string SymptomsRaw { get; private set; } = string.Empty;
    public decimal NoShowRiskScore { get; private set; }
    public bool? ActualAttendance { get; private set; }
    public string? NlpSummary { get; private set; }

    private Appointment() { } // ef

    private Appointment(Guid id,  Guid scheduleBlockId, Guid patientId, AppointmentStatus status, string symptomsRaw, decimal noShowRiskScore, bool? actualAttendance, string? nlpSummary)
    {
        Id = id;
        ScheduleBlockId = scheduleBlockId;
        PatientId = patientId;
        Status = status;
        SymptomsRaw = symptomsRaw;
        NoShowRiskScore = noShowRiskScore;
        ActualAttendance = actualAttendance;
        NlpSummary = nlpSummary;
    }

    public static Appointment Create(Guid scheduleBlockId, Guid patientId, string  symptomsRaw)
    {
        if (patientId == Guid.Empty)
            throw new InvalidOperationException("Debes incluir un paciente.");
        
        if (scheduleBlockId == Guid.Empty)
            throw new InvalidOperationException("Debes incluir un bloque horario.");
        
        return new Appointment(
            Guid.NewGuid(),
            scheduleBlockId,
            patientId,
            AppointmentStatus.Scheduled,
            symptomsRaw,
            0,
            null,
            null);
    }

    public void UpdateRiskScore(decimal noShowRiskScore)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new InvalidOperationException("No se puede actualizar el riesgo en una cita cancelada.");
        
        NoShowRiskScore = noShowRiskScore;
    }
}