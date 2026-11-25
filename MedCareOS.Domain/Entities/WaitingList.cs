using System.Globalization;
using System.Text;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class WaitingList
{
    public const int MaxSpecialtyLength = 120;
    public const int MaxCancellationReasonLength = 300;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = RiskThresholds.DefaultTenantId;
    public Guid PatientId { get; private set; }
    public string Specialty { get; private set; } = string.Empty;
    public string SpecialtyNormalized { get; private set; } = string.Empty;
    public DateTimeOffset RegistrationDate { get; private set; }
    public int Priority { get; private set; }
    public WaitingListStatus Status { get; private set; }
    public string? ClinicalContext { get; private set; }
    public Guid? PreferredStaffId { get; private set; }
    public DateOnly? PreferredFromDate { get; private set; }
    public DateOnly? PreferredToDate { get; private set; }
    public TimeOnly? PreferredTimeStart { get; private set; }
    public TimeOnly? PreferredTimeEnd { get; private set; }
    public bool IsDisplacedBonus { get; private set; }
    public Guid? DisplacedSourceAppointmentId { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WaitingList() { } // ef

    public static WaitingList Create(Guid patientId, string specialty, string? clinicalContext)
        => Create(
            patientId: patientId,
            specialty: specialty,
            clinicalContext: clinicalContext,
            priority: 0);

    public static WaitingList Create(
        Guid patientId,
        string specialty,
        string? clinicalContext = null,
        int priority = 0,
        Guid? tenantId = null,
        Guid? preferredStaffId = null,
        DateOnly? preferredFromDate = null,
        DateOnly? preferredToDate = null,
        TimeOnly? preferredTimeStart = null,
        TimeOnly? preferredTimeEnd = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? utcNow = null)
    {
        if (patientId == Guid.Empty)
            throw new ArgumentException("El PatientId no puede estar vacío.", nameof(patientId));

        if (string.IsNullOrWhiteSpace(specialty))
            throw new ArgumentException("La especialidad es obligatoria.", nameof(specialty));

        if (priority < 0)
            throw new ArgumentOutOfRangeException(nameof(priority), "La prioridad no puede ser negativa.");

        if (tenantId.HasValue && tenantId.Value == Guid.Empty)
            throw new ArgumentException("El TenantId no puede estar vacío.", nameof(tenantId));

        if (preferredFromDate.HasValue && preferredToDate.HasValue && preferredFromDate.Value > preferredToDate.Value)
            throw new ArgumentException("La fecha de inicio preferida no puede ser posterior a la fecha de término.");

        if (preferredTimeStart.HasValue != preferredTimeEnd.HasValue)
            throw new ArgumentException("PreferredTimeStart y PreferredTimeEnd deben especificarse juntos.");

        if (preferredTimeStart.HasValue && preferredTimeStart.Value >= preferredTimeEnd!.Value)
            throw new ArgumentException("La hora de inicio preferida debe ser anterior a la hora de término.");

        var now = utcNow ?? DateTimeOffset.UtcNow;
        if (expiresAt.HasValue && expiresAt.Value <= now)
            throw new ArgumentException("La fecha de expiración de la lista de espera debe ser futura.", nameof(expiresAt));

        var trimmedSpecialty = specialty.Trim();
        if (trimmedSpecialty.Length > MaxSpecialtyLength)
            trimmedSpecialty = trimmedSpecialty[..MaxSpecialtyLength];

        return new WaitingList
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? RiskThresholds.DefaultTenantId,
            PatientId = patientId,
            Specialty = trimmedSpecialty,
            SpecialtyNormalized = NormalizeSpecialty(trimmedSpecialty),
            RegistrationDate = now,
            Priority = priority,
            Status = WaitingListStatus.Active,
            ClinicalContext = string.IsNullOrWhiteSpace(clinicalContext) ? null : clinicalContext.Trim(),
            PreferredStaffId = preferredStaffId == Guid.Empty ? null : preferredStaffId,
            PreferredFromDate = preferredFromDate,
            PreferredToDate = preferredToDate,
            PreferredTimeStart = preferredTimeStart,
            PreferredTimeEnd = preferredTimeEnd,
            ExpiresAt = expiresAt,
            UpdatedAt = now
        };
    }

    public void ApplyDisplacedBonus(Guid sourceAppointmentId, DateTimeOffset utcNow)
    {
        if (sourceAppointmentId == Guid.Empty)
            throw new ArgumentException("SourceAppointmentId no puede estar vacío.", nameof(sourceAppointmentId));

        IsDisplacedBonus = true;
        DisplacedSourceAppointmentId = sourceAppointmentId;
        UpdatedAt = utcNow;
    }

    public void MarkOffered(DateTimeOffset utcNow)
    {
        if (Status != WaitingListStatus.Active)
            throw new InvalidOperationException($"Solo una entrada 'Active' puede pasar a 'Offered' (actual: '{Status}').");

        Status = WaitingListStatus.Offered;
        UpdatedAt = utcNow;
    }

    public void ReturnToActive(DateTimeOffset utcNow)
    {
        if (Status != WaitingListStatus.Offered)
            throw new InvalidOperationException($"Solo una entrada 'Offered' puede volver a 'Active' (actual: '{Status}').");

        // Conserva RegistrationDate y Priority originales al retornar a lista activa
        Status = WaitingListStatus.Active;
        UpdatedAt = utcNow;
    }

    public void MarkAccepted(DateTimeOffset utcNow)
    {
        if (Status != WaitingListStatus.Offered)
            throw new InvalidOperationException($"Solo una entrada 'Offered' puede pasar a 'Accepted' (actual: '{Status}').");

        Status = WaitingListStatus.Accepted;
        UpdatedAt = utcNow;
    }

    public void MarkExpired(DateTimeOffset utcNow)
    {
        if (Status is not (WaitingListStatus.Active or WaitingListStatus.Offered))
            throw new InvalidOperationException($"No se puede expirar una entrada en estado '{Status}'.");

        Status = WaitingListStatus.Expired;
        UpdatedAt = utcNow;
    }

    public void Cancel(string? reason, DateTimeOffset utcNow)
    {
        if (Status is not (WaitingListStatus.Active or WaitingListStatus.Offered))
            throw new InvalidOperationException($"No se puede cancelar una entrada en estado '{Status}'.");

        Status = WaitingListStatus.Cancelled;
        CancelledAt = utcNow;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var trimmed = reason.Trim();
            CancellationReason = trimmed.Length <= MaxCancellationReasonLength
                ? trimmed
                : trimmed[..MaxCancellationReasonLength];
        }
        UpdatedAt = utcNow;
    }

    public static string NormalizeSpecialty(string? specialty)
    {
        if (string.IsNullOrWhiteSpace(specialty))
            return string.Empty;

        var decomposed = specialty.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
