using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class NotificationStateMachineTests
{
    [Fact]
    public void Create_WithImmediateSchedule_InitializesPendingStatus()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.AppointmentCreated,
            NotificationChannel.WhatsApp,
            "appointment.created",
            1,
            "es-CL",
            "{}",
            now,
            Guid.NewGuid(),
            now);

        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.OriginalChannel.Should().BeNull();
        notification.Attempts.Should().Be(0);
    }

    [Fact]
    public void Create_WithFutureSchedule_InitializesScheduledStatus()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.Reminder,
            NotificationChannel.Email,
            "appointment.reminder",
            1,
            "es-CL",
            "{}",
            now.AddHours(24),
            Guid.NewGuid(),
            now);

        notification.Status.Should().Be(NotificationStatus.Scheduled);
    }

    [Fact]
    public void ApplyDeliveryStatus_ShouldBeMonotonic_NeverDowngradingReadToDelivered()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.Reminder,
            NotificationChannel.WhatsApp,
            "appointment.reminder",
            1,
            "es-CL",
            "{}",
            now,
            Guid.NewGuid(),
            now);

        notification.MarkSent("MetaCloudApi", "wamid.1", "Hola Ana", now.AddSeconds(5));

        var readAt = now.AddSeconds(30);
        var deliveredAt = now.AddSeconds(20);

        notification.ApplyDeliveryStatus(NotificationStatus.Read, readAt);
        notification.Status.Should().Be(NotificationStatus.Read);
        notification.ReadAt.Should().Be(readAt);

        // Out-of-order delivered webhook arrives later
        notification.ApplyDeliveryStatus(NotificationStatus.Delivered, deliveredAt);
        notification.Status.Should().Be(NotificationStatus.Read);
        notification.ReadAt.Should().Be(readAt);
        notification.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void FallbackToChannel_PreservesOriginalChannelAndResetsToPending()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.FinalConfirmation,
            NotificationChannel.WhatsApp,
            "appointment.final_confirmation",
            1,
            "es-CL",
            "{}",
            now,
            Guid.NewGuid(),
            now);

        notification.FallbackToChannel(NotificationChannel.Email, "Meta error 131026", now.AddSeconds(2));

        notification.OriginalChannel.Should().Be(NotificationChannel.WhatsApp);
        notification.Channel.Should().Be(NotificationChannel.Email);
        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.Error.Should().Be("Meta error 131026");
    }

    [Fact]
    public void RedactContent_ReplacesContentAndParamsAndSetsTimestampOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.AppointmentCreated,
            NotificationChannel.Email,
            "appointment.created",
            1,
            "es-CL",
            "{\"patientFirstName\":\"Ana\"}",
            now,
            Guid.NewGuid(),
            now);
        notification.MarkSent("Resend", "re_123", "Hola Ana, tu cita en Clínica...", now);

        var redactAt = now.AddDays(31);
        notification.RedactContent(redactAt);

        notification.Content.Should().Be(Notification.RedactedPlaceholder);
        notification.TemplateParamsJson.Should().Be("{}");
        notification.ContentRedactedAt.Should().Be(redactAt);
    }
}
