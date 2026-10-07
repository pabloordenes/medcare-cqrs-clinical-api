using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Appointments.Queries.GetDailyAppointments;

public record DailyAppointmentDto(
    // datos formateados
    Guid AppointmentId,
    string PatientName,
    int PatientAge,
    string BoxName,
    DateTime StartTime,
    DateTime EndTime,
    AppointmentStatus Status,
    string? NlpSummary,
    string? RiskBand
    );