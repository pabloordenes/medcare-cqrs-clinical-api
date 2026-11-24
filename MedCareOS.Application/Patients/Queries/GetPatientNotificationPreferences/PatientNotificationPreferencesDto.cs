using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;

public record PatientNotificationPreferencesDto(
    Guid PatientId,
    NotificationChannel PreferredChannel,
    string EffectiveReminderChannel,
    bool WhatsAppOptIn,
    DateTimeOffset? WhatsAppOptInAt,
    string? WhatsAppOptInSource,
    bool HasOptedOut,
    DateTimeOffset? OptOutAt,
    string? OptOutSource,
    bool HasEmail,
    bool HasValidPhone,
    DateTimeOffset? UpdatedAt);
