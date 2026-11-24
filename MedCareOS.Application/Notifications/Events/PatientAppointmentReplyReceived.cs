using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Notifications.Events;

public record PatientAppointmentReplyReceived(
    Guid AppointmentId,
    Guid PatientId,
    Guid? NotificationId,
    PatientReplyIntent Intent,
    NotificationChannel Channel,
    DateTimeOffset ReceivedAtUtc,
    string NormalizedKeyword);
