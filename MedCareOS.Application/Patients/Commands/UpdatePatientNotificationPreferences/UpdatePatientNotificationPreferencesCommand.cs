using MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;
using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Patients.Commands.UpdatePatientNotificationPreferences;

public record UpdatePatientNotificationPreferencesCommand(
    Guid? PatientId,
    Guid? AuthenticatedUserId,
    NotificationChannel PreferredChannel,
    bool WhatsAppOptIn,
    string Source) : IRequest<PatientNotificationPreferencesDto>;
