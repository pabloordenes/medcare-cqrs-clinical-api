using MedCareOS.Domain.Common;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class Patient
{
    public const int MaxConsentSourceLength = 100;

    public Guid Id { get; private set; }
    public Guid UserId { get; set; }
    public string Rut { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public string Gender { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? Neighbourhood { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public NotificationChannel PreferredChannel { get; private set; } = NotificationChannel.Email;
    public bool WhatsAppOptIn { get; private set; }
    public DateTimeOffset? WhatsAppOptInAt { get; private set; }
    public string? WhatsAppOptInSource { get; private set; }
    public DateTimeOffset? OptOutAt { get; private set; }
    public string? OptOutSource { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private Patient() { } // ef core

    private Patient(
        Guid id,
        Guid userId,
        string rut,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string gender,
        string? address,
        string? neighbourhood,
        string phone,
        string? email,
        NotificationChannel preferredChannel,
        bool whatsAppOptIn,
        string? consentSource)
    {
        var utcNow = DateTimeOffset.UtcNow;

        Id = id;
        UserId = userId;
        Rut = rut;
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Phone = phone.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Address = address;
        Neighbourhood = neighbourhood;
        PreferredChannel = preferredChannel;
        WhatsAppOptIn = whatsAppOptIn;
        if (whatsAppOptIn)
        {
            WhatsAppOptInAt = utcNow;
            WhatsAppOptInSource = NormalizeSource(consentSource, "registration");
        }
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public static Patient Create(
        Guid userId,
        string rut,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string gender,
        string phone,
        string? email = null,
        string? address = null,
        string? neighbourhood = null,
        NotificationChannel preferredChannel = NotificationChannel.Email,
        bool whatsAppOptIn = false,
        string? consentSource = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Debe vincularse a un usuario de Supabase Auth.");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Ambos nombres deben llenarse.");

        if (string.IsNullOrWhiteSpace(gender))
            throw new ArgumentException("Debe especificar su genero.");

        if (string.IsNullOrWhiteSpace(rut))
            throw new ArgumentException("Debe ingresar su RUT.");

        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Debe ingresar su Numero Telefonico.");

        if (preferredChannel == NotificationChannel.WhatsApp && !whatsAppOptIn)
            throw new ArgumentException("Para elegir WhatsApp como canal preferido se requiere consentimiento explícito (WhatsAppOptIn).");

        return new Patient(
            Guid.NewGuid(),
            userId,
            rut,
            firstName,
            lastName,
            dateOfBirth,
            gender,
            address,
            neighbourhood,
            phone,
            email,
            preferredChannel,
            whatsAppOptIn,
            consentSource);
    }

    public void UpdateNotificationPreferences(
        NotificationChannel preferredChannel,
        bool whatsAppOptIn,
        string source,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("La fuente de actualización del consentimiento es obligatoria.", nameof(source));

        if (preferredChannel == NotificationChannel.WhatsApp && !whatsAppOptIn)
            throw new InvalidOperationException("Para elegir WhatsApp como canal preferido se requiere consentimiento explícito (WhatsAppOptIn).");

        if (whatsAppOptIn && PhoneNormalizer.ToWhatsAppDigits(Phone) is null)
            throw new InvalidOperationException("Se requiere un teléfono válido para activar notificaciones por WhatsApp.");

        OptOutAt = null;
        OptOutSource = null;
        PreferredChannel = preferredChannel;
        WhatsAppOptIn = whatsAppOptIn;

        if (whatsAppOptIn)
        {
            WhatsAppOptInAt = utcNow;
            WhatsAppOptInSource = NormalizeSource(source, "patient_portal");
        }
        else
        {
            WhatsAppOptInAt = null;
            WhatsAppOptInSource = null;
        }

        UpdatedAt = utcNow;
    }

    public void OptOut(string source, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("La fuente del opt-out es obligatoria.", nameof(source));

        OptOutAt ??= utcNow;
        OptOutSource = string.IsNullOrWhiteSpace(OptOutSource)
            ? NormalizeSource(source, "unknown")
            : OptOutSource;
        WhatsAppOptIn = false;
        UpdatedAt = utcNow;
    }

    public bool HasOptedOut() => OptOutAt.HasValue;

    public bool CanReceiveChannel(NotificationChannel channel)
    {
        if (OptOutAt.HasValue)
            return false;

        return channel switch
        {
            NotificationChannel.WhatsApp => WhatsAppOptIn && PhoneNormalizer.ToWhatsAppDigits(Phone) is not null,
            NotificationChannel.Email => !string.IsNullOrWhiteSpace(Email) && Email.Contains('@'),
            _ => false
        };
    }

    public NotificationChannel? ResolvePrimaryChannel(PrimaryChannelStrategy strategy)
    {
        if (OptOutAt.HasValue)
            return null;

        return strategy switch
        {
            PrimaryChannelStrategy.WhatsAppFirst =>
                CanReceiveChannel(NotificationChannel.WhatsApp)
                    ? NotificationChannel.WhatsApp
                    : (CanReceiveChannel(NotificationChannel.Email) ? NotificationChannel.Email : null),
            PrimaryChannelStrategy.EmailFirst =>
                CanReceiveChannel(NotificationChannel.Email)
                    ? NotificationChannel.Email
                    : (CanReceiveChannel(NotificationChannel.WhatsApp) ? NotificationChannel.WhatsApp : null),
            _ => GetEffectiveReminderChannel()
        };
    }

    public NotificationChannel? ResolveAlternateChannel(
        NotificationChannel previousChannel,
        AlternateChannelStrategy strategy)
    {
        if (OptOutAt.HasValue)
            return null;

        if (strategy == AlternateChannelStrategy.SameChannelOnly)
        {
            return CanReceiveChannel(previousChannel)
                ? previousChannel
                : GetEffectiveReminderChannel();
        }

        var opposite = previousChannel == NotificationChannel.WhatsApp
            ? NotificationChannel.Email
            : NotificationChannel.WhatsApp;

        if (CanReceiveChannel(opposite))
            return opposite;

        if (CanReceiveChannel(previousChannel))
            return previousChannel;

        return GetEffectiveReminderChannel();
    }

    public NotificationChannel? GetEffectiveReminderChannel()
    {
        if (OptOutAt.HasValue)
            return null;

        var hasValidPhone = PhoneNormalizer.ToWhatsAppDigits(Phone) is not null;
        var hasEmail = !string.IsNullOrWhiteSpace(Email);

        if (PreferredChannel == NotificationChannel.WhatsApp && WhatsAppOptIn && hasValidPhone)
            return NotificationChannel.WhatsApp;

        if (hasEmail)
            return NotificationChannel.Email;

        if (WhatsAppOptIn && hasValidPhone)
            return NotificationChannel.WhatsApp;

        return null;
    }

    public string ToPredictReminderChannel()
    {
        return GetEffectiveReminderChannel() switch
        {
            NotificationChannel.WhatsApp => "whatsapp",
            NotificationChannel.Email => "email",
            _ => "ninguno"
        };
    }

    private static string NormalizeSource(string? source, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(source) ? fallback : source.Trim();
        return value.Length <= MaxConsentSourceLength ? value : value[..MaxConsentSourceLength];
    }
}
