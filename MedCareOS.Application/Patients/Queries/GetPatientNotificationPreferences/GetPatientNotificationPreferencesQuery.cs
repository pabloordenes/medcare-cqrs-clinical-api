using MediatR;

namespace MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;

public record GetPatientNotificationPreferencesQuery(
    Guid? PatientId = null,
    Guid? AuthenticatedUserId = null) : IRequest<PatientNotificationPreferencesDto>;
