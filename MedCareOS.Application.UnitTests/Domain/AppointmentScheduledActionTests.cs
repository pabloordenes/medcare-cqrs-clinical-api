using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class AppointmentScheduledActionTests
{
    [Fact]
    public void Lifecycle_ClaimCompleteSkipCancelAndRetryToFailed_BehavesCorrectly()
    {
        var t0 = DateTimeOffset.UtcNow;
        var action = AppointmentScheduledAction.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            ScheduledActionKind.Reminder1,
            t0.AddHours(1),
            Guid.NewGuid(),
            planVersion: 1,
            maxAttempts: 2,
            utcNow: t0);

        action.Status.Should().Be(ScheduledActionStatus.Pending);
        action.Attempts.Should().Be(0);

        var lock1 = Guid.NewGuid();
        action.Claim("worker-a", t0.AddHours(1), TimeSpan.FromMinutes(2), lock1);
        action.Status.Should().Be(ScheduledActionStatus.Processing);
        action.Attempts.Should().Be(1);
        action.IsOwnedBy("worker-a", lock1).Should().BeTrue();
        action.IsOwnedBy("worker-b", lock1).Should().BeFalse();

        // No se puede reclamar mientras el lease sigue vigente
        ((Action)(() => action.Claim("worker-b", t0.AddHours(1).AddSeconds(30), TimeSpan.FromMinutes(2), Guid.NewGuid())))
            .Should().Throw<InvalidOperationException>();

        // Retry #1 -> vuelve a Pending
        action.ScheduleRetry(t0.AddHours(1).AddMinutes(2), "Timeout temporal", t0.AddHours(1).AddSeconds(5));
        action.Status.Should().Be(ScheduledActionStatus.Pending);
        action.LockToken.Should().BeNull();
        action.LockedBy.Should().BeNull();

        // Intento #2 tras expirar backoff -> al fallar alcanza MaxAttempts=2 y pasa a Failed
        var lock2 = Guid.NewGuid();
        action.Claim("worker-b", t0.AddHours(1).AddMinutes(3), TimeSpan.FromMinutes(2), lock2);
        action.Attempts.Should().Be(2);
        action.ScheduleRetry(t0.AddHours(1).AddMinutes(10), "Error definitivo", t0.AddHours(1).AddMinutes(3).AddSeconds(2));
        action.Status.Should().Be(ScheduledActionStatus.Failed);
        action.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void ExpiredProcessingLease_CanBeReclaimedAndCompleted()
    {
        var t0 = DateTimeOffset.UtcNow;
        var action = AppointmentScheduledAction.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            ScheduledActionKind.FinalConfirmation,
            t0,
            Guid.NewGuid(),
            maxAttempts: 5,
            utcNow: t0);

        var lock1 = Guid.NewGuid();
        action.Claim("crashed-worker", t0, TimeSpan.FromSeconds(60), lock1);

        var lock2 = Guid.NewGuid();
        action.Claim("recovery-worker", t0.AddSeconds(61), TimeSpan.FromSeconds(60), lock2);

        action.Attempts.Should().Be(2);
        action.IsOwnedBy("recovery-worker", lock2).Should().BeTrue();
        action.IsOwnedBy("crashed-worker", lock1).Should().BeFalse();

        action.MarkCompleted(t0.AddSeconds(62), "{\"sent\":true}");
        action.Status.Should().Be(ScheduledActionStatus.Completed);
        action.MetadataJson.Should().Be("{\"sent\":true}");
        action.LockToken.Should().BeNull();
    }

    [Fact]
    public void CancelAndSkip_AreProtectedAndCancelIsIdempotent()
    {
        var t0 = DateTimeOffset.UtcNow;
        var cancelled = AppointmentScheduledAction.Create(
            RiskThresholds.DefaultTenantId, Guid.NewGuid(), ScheduledActionKind.Reminder1, t0, Guid.NewGuid(), utcNow: t0);

        cancelled.Cancel("Cita cancelada", t0.AddMinutes(1));
        cancelled.Status.Should().Be(ScheduledActionStatus.Cancelled);
        cancelled.Cancel("Segundo intento", t0.AddMinutes(2)); // no-op
        cancelled.Status.Should().Be(ScheduledActionStatus.Cancelled);

        var skipped = AppointmentScheduledAction.Create(
            RiskThresholds.DefaultTenantId, Guid.NewGuid(), ScheduledActionKind.Reminder2AndRescore, t0, Guid.NewGuid(), utcNow: t0);
        skipped.MarkSkipped("Ya confirmada", t0.AddMinutes(1));
        skipped.Status.Should().Be(ScheduledActionStatus.Skipped);
    }

    [Fact]
    public void PatientChannelResolution_SupportsPrimaryAndAlternateStrategies()
    {
        var patientBoth = Patient.Create(
            Guid.NewGuid(), "11.111.111-1", "Maria", "Gomez", new DateOnly(1990, 5, 1), "F",
            "+56912345678", email: "maria@test.cl", preferredChannel: NotificationChannel.WhatsApp, whatsAppOptIn: true);

        patientBoth.ResolvePrimaryChannel(PrimaryChannelStrategy.PatientPreferred).Should().Be(NotificationChannel.WhatsApp);
        patientBoth.ResolvePrimaryChannel(PrimaryChannelStrategy.EmailFirst).Should().Be(NotificationChannel.Email);
        patientBoth.ResolveAlternateChannel(NotificationChannel.WhatsApp, AlternateChannelStrategy.AlternateIfAvailable)
            .Should().Be(NotificationChannel.Email);
        patientBoth.ResolveAlternateChannel(NotificationChannel.WhatsApp, AlternateChannelStrategy.SameChannelOnly)
            .Should().Be(NotificationChannel.WhatsApp);

        // Si solo tiene WhatsApp, el canal alterno cae a WhatsApp sin fallar
        var patientWaOnly = Patient.Create(
            Guid.NewGuid(), "22.222.222-2", "Juan", "Soto", new DateOnly(1988, 2, 2), "M",
            "+56987654321", email: null, preferredChannel: NotificationChannel.WhatsApp, whatsAppOptIn: true);
        patientWaOnly.ResolveAlternateChannel(NotificationChannel.WhatsApp, AlternateChannelStrategy.AlternateIfAvailable)
            .Should().Be(NotificationChannel.WhatsApp);

        // Tras opt-out no resuelve ningún canal
        patientBoth.OptOut("whatsapp_stop", DateTimeOffset.UtcNow);
        patientBoth.ResolvePrimaryChannel(PrimaryChannelStrategy.WhatsAppFirst).Should().BeNull();
        patientBoth.ResolveAlternateChannel(NotificationChannel.WhatsApp, AlternateChannelStrategy.AlternateIfAvailable)
            .Should().BeNull();
    }
}
