namespace MedCareOS.Domain.Enums;

public enum PrimaryChannelStrategy
{
    PatientPreferred,
    WhatsAppFirst,
    EmailFirst
}

public enum AlternateChannelStrategy
{
    AlternateIfAvailable,
    SameChannelOnly
}
