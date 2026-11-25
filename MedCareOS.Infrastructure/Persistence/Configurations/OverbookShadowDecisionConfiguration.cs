using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class OverbookShadowDecisionConfiguration : IEntityTypeConfiguration<OverbookShadowDecision>
{
    public void Configure(EntityTypeBuilder<OverbookShadowDecision> builder)
    {
        builder.ToTable("overbook_shadow_decisions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.AppointmentId).HasColumnName("cita_id");
        builder.Property(x => x.ScheduleBlockId).HasColumnName("schedule_block_id");

        builder.Property(x => x.EffectiveRiskScore)
            .HasColumnName("effective_risk_score")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.ThresholdUsed)
            .HasColumnName("threshold_used")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.Decision)
            .HasColumnName("decision")
            .HasMaxLength(OverbookShadowDecision.MaxDecisionLength)
            .IsRequired();

        builder.Property(x => x.WouldHaveOverbooked).HasColumnName("would_have_overbooked").IsRequired();
        builder.Property(x => x.FinalNotificationDelivered).HasColumnName("final_notification_delivered").IsRequired();
        builder.Property(x => x.HasClinicalExclusion).HasColumnName("has_clinical_exclusion").IsRequired();
        builder.Property(x => x.ClinicalExclusionCode)
            .HasColumnName("clinical_exclusion_code")
            .HasMaxLength(OverbookShadowDecision.MaxClinicalExclusionCodeLength);
        builder.Property(x => x.OutcomeAttendance).HasColumnName("outcome_attendance");
        builder.Property(x => x.IsSynthetic).HasColumnName("is_synthetic").IsRequired();
        builder.Property(x => x.EvaluatedAtUtc).HasColumnName("evaluated_at_utc").IsRequired();
        builder.Property(x => x.OutcomeResolvedAtUtc).HasColumnName("outcome_resolved_at_utc");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.AppointmentId)
            .HasDatabaseName("IX_overbook_shadow_decisions_cita_id");

        builder.HasIndex(x => new { x.TenantId, x.WouldHaveOverbooked, x.IsSynthetic })
            .HasDatabaseName("IX_overbook_shadow_decisions_tenant_eval");
    }
}
