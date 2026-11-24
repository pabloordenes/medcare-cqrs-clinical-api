using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Persistence;

public class ConfirmationSchemaAndPolicyTests
{
    [Fact]
    public void ApplicationDbContext_ShouldMapAppointmentConfirmationLifecycleAndTenantPolicyConstraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=medcare_design;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var designTimeModel = dbContext.GetService<IDesignTimeModel>().Model;

        var appointmentEntity = designTimeModel.FindEntityType(typeof(Appointment));
        appointmentEntity.Should().NotBeNull();
        appointmentEntity!.GetTableName().Should().Be("citas");

        appointmentEntity.FindProperty(nameof(Appointment.TenantId))!.GetColumnName().Should().Be("tenant_id");
        appointmentEntity.FindProperty(nameof(Appointment.ConfirmationState))!.GetColumnName().Should().Be("confirmation_state");
        appointmentEntity.FindProperty(nameof(Appointment.OrchestrationSuppressed))!.GetColumnName().Should().Be("orchestration_suppressed");
        appointmentEntity.FindProperty(nameof(Appointment.ConfirmedAt))!.GetColumnName().Should().Be("confirmed_at");
        appointmentEntity.FindProperty(nameof(Appointment.FinalConfirmationRequestedAt))!.GetColumnName().Should().Be("final_confirmation_requested_at");
        appointmentEntity.FindProperty(nameof(Appointment.DeadlineMissedAt))!.GetColumnName().Should().Be("deadline_missed_at");
        appointmentEntity.FindProperty(nameof(Appointment.ConfirmationOutcome))!.GetColumnName().Should().Be("confirmation_outcome");

        var calibrationIndex = appointmentEntity.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "IX_citas_calibration_outcome");
        calibrationIndex.Should().NotBeNull();
        calibrationIndex!.GetFilter().Should().Be("confirmation_outcome IS NOT NULL");

        var policyEntity = designTimeModel.FindEntityType(typeof(TenantConfirmationPolicy));
        policyEntity.Should().NotBeNull();
        policyEntity!.GetTableName().Should().Be("tenant_confirmation_policies");
        policyEntity.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName()).Should().ContainSingle("tenant_id");

        var checkNames = policyEntity.GetCheckConstraints().Select(c => c.Name).ToList();
        checkNames.Should().Contain([
            "CK_tenant_confirmation_policies_offsets",
            "CK_tenant_confirmation_policies_hysteresis",
            "CK_tenant_confirmation_policies_weights",
            "CK_tenant_confirmation_policies_quiet_hours"
        ]);
    }

    [Fact]
    public async Task TenantConfirmationPolicyRepository_ShouldFallbackToDefaultAndUpsertChanges()
    {
        var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PolicyRepoDb_{Guid.NewGuid()}")
            .UseInternalServiceProvider(serviceProvider)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        var repository = new TenantConfirmationPolicyRepository(dbContext);
        var tenantId = Guid.NewGuid();

        var fallback = await repository.GetOrDefaultAsync(tenantId);
        fallback.TenantId.Should().Be(tenantId);
        fallback.DeadlineOffsetHours.Should().Be(12);

        var custom = TenantConfirmationPolicy.Create(
            tenantId: tenantId,
            reminder1OffsetHours: 96,
            reminder2OffsetHours: 48,
            finalConfirmationOffsetHours: 24,
            deadlineOffsetHours: 16,
            minimumDelayAfterCreationMinutes: 20,
            sameDayReminderMinLeadHours: 3,
            timeZoneId: "America/Santiago",
            quietHoursStartLocal: new TimeOnly(22, 30),
            quietHoursEndLocal: new TimeOnly(7, 30),
            primaryChannelStrategy: PrimaryChannelStrategy.WhatsAppFirst,
            alternateChannelStrategy: AlternateChannelStrategy.AlternateIfAvailable,
            enableRescoreAtReminder2: true,
            countSentAsDeliveredForUnanswered: false,
            weightConfirmedEarly: -1.6m,
            weightReminderUnanswered: 0.4m,
            maxReminderUnansweredCumulative: 1.2m,
            weightDeliveryFailed: 0.8m,
            weightRepliedWithoutConfirmation: 0m,
            weightFinalConfirmed: 0m,
            mediumExitThreshold: 0.40m,
            mediumEnterThreshold: 0.50m,
            highExitThreshold: 0.55m,
            highEnterThreshold: 0.70m);

        await repository.UpsertAsync(custom);
        await dbContext.SaveChangesAsync();

        var fetched = await repository.GetOrDefaultAsync(tenantId);
        fetched.DeadlineOffsetHours.Should().Be(16);
        fetched.PrimaryChannelStrategy.Should().Be(PrimaryChannelStrategy.WhatsAppFirst);

        var updated = TenantConfirmationPolicy.CreateDefault(tenantId);
        updated.Update(
            reminder1OffsetHours: 72,
            reminder2OffsetHours: 48,
            finalConfirmationOffsetHours: 24,
            deadlineOffsetHours: 14,
            minimumDelayAfterCreationMinutes: 30,
            sameDayReminderMinLeadHours: 3,
            timeZoneId: "America/Lima",
            quietHoursStartLocal: null,
            quietHoursEndLocal: null,
            primaryChannelStrategy: PrimaryChannelStrategy.EmailFirst,
            alternateChannelStrategy: AlternateChannelStrategy.SameChannelOnly,
            enableRescoreAtReminder2: false,
            countSentAsDeliveredForUnanswered: true,
            weightConfirmedEarly: -1.4m,
            weightReminderUnanswered: 0.3m,
            maxReminderUnansweredCumulative: 0.9m,
            weightDeliveryFailed: 0.7m,
            weightRepliedWithoutConfirmation: 0m,
            weightFinalConfirmed: 0m,
            mediumExitThreshold: 0.38m,
            mediumEnterThreshold: 0.48m,
            highExitThreshold: 0.58m,
            highEnterThreshold: 0.72m);

        await repository.UpsertAsync(updated);
        await dbContext.SaveChangesAsync();

        var reloaded = await repository.GetByTenantIdAsync(tenantId);
        reloaded.Should().NotBeNull();
        reloaded!.DeadlineOffsetHours.Should().Be(14);
        reloaded.TimeZoneId.Should().Be("America/Lima");
        reloaded.CountSentAsDeliveredForUnanswered.Should().BeTrue();
        reloaded.EnableRescoreAtReminder2.Should().BeFalse();

        var defaultFallback = await repository.GetOrDefaultAsync(RiskThresholds.DefaultTenantId);
        defaultFallback.TenantId.Should().Be(RiskThresholds.DefaultTenantId);
    }
}
