namespace MedCareOS.Domain.Entities;

public class AppointmentRescheduleHistory
{
    public const int MaxChannelLength = 40;
    public const int MaxReasonLength = 300;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid FromScheduleBlockId { get; private set; }
    public Guid ToScheduleBlockId { get; private set; }
    public Guid? TriggeredByUserId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public bool WasDisplacedPriority { get; private set; }
    public int RescheduleNumber { get; private set; }
    public DateTimeOffset RescheduledAtUtc { get; private set; }

    private AppointmentRescheduleHistory() { } // ef

    public static AppointmentRescheduleHistory Create(
        Guid tenantId,
        Guid appointmentId,
        Guid patientId,
        Guid fromScheduleBlockId,
        Guid toScheduleBlockId,
        Guid? triggeredByUserId,
        string channel,
        string? reason,
        bool wasDisplacedPriority,
        int rescheduleNumber,
        DateTimeOffset rescheduledAtUtc)
    {
        var effectiveChannel = string.IsNullOrWhiteSpace(channel) ? "portal" : channel.Trim();
        if (effectiveChannel.Length > MaxChannelLength)
            effectiveChannel = effectiveChannel[..MaxChannelLength];

        string? trimmedReason = null;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var t = reason.Trim();
            trimmedReason = t.Length <= MaxReasonLength ? t : t[..MaxReasonLength];
        }

        return new AppointmentRescheduleHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AppointmentId = appointmentId,
            PatientId = patientId,
            FromScheduleBlockId = fromScheduleBlockId,
            ToScheduleBlockId = toScheduleBlockId,
            TriggeredByUserId = triggeredByUserId == Guid.Empty ? null : triggeredByUserId,
            Channel = effectiveChannel,
            Reason = trimmedReason,
            WasDisplacedPriority = wasDisplacedPriority,
            RescheduleNumber = rescheduleNumber,
            RescheduledAtUtc = rescheduledAtUtc
        };
    }
}
