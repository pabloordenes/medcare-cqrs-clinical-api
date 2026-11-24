using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;

public class GetPatientNotificationPreferencesQueryHandler
    : IRequestHandler<GetPatientNotificationPreferencesQuery, PatientNotificationPreferencesDto>
{
    private readonly IApplicationDbContext _dbContext;

    public GetPatientNotificationPreferencesQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PatientNotificationPreferencesDto> Handle(
        GetPatientNotificationPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var patient = request.AuthenticatedUserId.HasValue
            ? await _dbContext.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == request.AuthenticatedUserId.Value, cancellationToken)
            : await _dbContext.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken);

        if (patient is null)
            throw new KeyNotFoundException("No se encontró el perfil del paciente.");

        return new PatientNotificationPreferencesDto(
            PatientId: patient.Id,
            PreferredChannel: patient.PreferredChannel,
            EffectiveReminderChannel: patient.ToPredictReminderChannel(),
            WhatsAppOptIn: patient.WhatsAppOptIn,
            WhatsAppOptInAt: patient.WhatsAppOptInAt,
            WhatsAppOptInSource: patient.WhatsAppOptInSource,
            HasOptedOut: patient.HasOptedOut(),
            OptOutAt: patient.OptOutAt,
            OptOutSource: patient.OptOutSource,
            HasEmail: !string.IsNullOrWhiteSpace(patient.Email),
            HasValidPhone: PhoneNormalizer.ToWhatsAppDigits(patient.Phone) is not null,
            UpdatedAt: patient.UpdatedAt);
    }
}
