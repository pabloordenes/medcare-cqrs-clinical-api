using MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;
using MedCareOS.Domain.Common;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Patients.Commands.UpdatePatientNotificationPreferences;

public class UpdatePatientNotificationPreferencesCommandHandler
    : IRequestHandler<UpdatePatientNotificationPreferencesCommand, PatientNotificationPreferencesDto>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePatientNotificationPreferencesCommandHandler(
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<PatientNotificationPreferencesDto> Handle(
        UpdatePatientNotificationPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        var patient = request.AuthenticatedUserId.HasValue
            ? await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
            : (request.PatientId.HasValue
                ? await _patientRepository.GetPatientByIdAsync(request.PatientId.Value, cancellationToken)
                : null);

        if (patient is null)
            throw new KeyNotFoundException("No se encontró el perfil del paciente.");

        patient.UpdateNotificationPreferences(
            request.PreferredChannel,
            request.WhatsAppOptIn,
            request.Source,
            DateTimeOffset.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
