using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Notifications.Commands.ProcessPatientReply;

public record ProcessPatientReplyCommand(
    Guid AppointmentId,
    Guid PatientId,
    Guid? NotificationId,
    PatientReplyIntent Intent,
    NotificationChannel Channel,
    string NormalizedKeyword,
    DateTimeOffset ReceivedAtUtc) : IRequest;
