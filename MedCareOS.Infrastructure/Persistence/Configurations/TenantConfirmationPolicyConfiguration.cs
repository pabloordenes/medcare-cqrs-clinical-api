using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class TenantConfirmationPolicyConfiguration : IEntityTypeConfiguration<TenantConfirmationPolicy>
{
    public void Configure(EntityTypeBuilder<TenantConfirmationPolicy> builder)
    {
        builder.ToTable("tenant_confirmation_policies", t =>
        {
            t.HasCheckConstraint(
                "CK_tenant_confirmation_policies_offsets",
                "reminder1_offset_hours > reminder2_offset_hours AND reminder2_offset_hours > final_confirmation_offset_hours AND final_confirmation_offset_hours > deadline_offset_hours AND deadline_offset_hours > 0 AND minimum_delay_after_creation_minutes >= 0 AND same_day_reminder_min_lead_hours >= 0 AND same_day_reminder_min_lead_hours <= deadline_offset_hours");

            t.HasCheckConstraint(
                "CK_tenant_confirmation_policies_hysteresis",
                "medium_exit_threshold > 0 AND medium_exit_threshold < medium_enter_threshold AND medium_enter_threshold < high_exit_threshold AND high_exit_threshold < high_enter_threshold AND high_enter_threshold < 1");

            t.HasCheckConstraint(
                "CK_tenant_confirmation_policies_weights",
                "weight_confirmed_early <= 0 AND weight_reminder_unanswered >= 0 AND max_reminder_unanswered_cumulative >= 0 AND weight_delivery_failed >= 0");

            t.HasCheckConstraint(
                "CK_tenant_confirmation_policies_quiet_hours",
                "(quiet_hours_start_local IS NULL AND quiet_hours_end_local IS NULL) OR (quiet_hours_start_local IS NOT NULL AND quiet_hours_end_local IS NOT NULL AND quiet_hours_start_local <> quiet_hours_end_local)");

            t.HasCheckConstraint(
                "CK_tenant_confirmation_policies_overbook",
                "hitl_overbook_min_effective_score > 0 AND hitl_overbook_min_effective_score < auto_overbook_min_effective_score AND auto_overbook_min_effective_score < 1 AND excluded_senior_age_years BETWEEN 18 AND 120 AND waiting_list_offer_ttl_minutes > 0 AND max_waiting_list_cascade_offers >= 1 AND min_lead_minutes_for_waiting_list_offer >= 0 AND max_reschedules_per_appointment >= 0 AND min_shadow_evaluations_for_activation >= 1 AND max_false_positive_rate_for_activation > 0 AND max_false_positive_rate_for_activation < 1");
        });

        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .ValueGeneratedNever();

        builder.Property(x => x.Reminder1OffsetHours)
            .HasColumnName("reminder1_offset_hours")
            .IsRequired();

        builder.Property(x => x.Reminder2OffsetHours)
            .HasColumnName("reminder2_offset_hours")
            .IsRequired();

        builder.Property(x => x.FinalConfirmationOffsetHours)
            .HasColumnName("final_confirmation_offset_hours")
            .IsRequired();

        builder.Property(x => x.DeadlineOffsetHours)
            .HasColumnName("deadline_offset_hours")
            .IsRequired();

        builder.Property(x => x.MinimumDelayAfterCreationMinutes)
            .HasColumnName("minimum_delay_after_creation_minutes")
            .IsRequired();

        builder.Property(x => x.SameDayReminderMinLeadHours)
            .HasColumnName("same_day_reminder_min_lead_hours")
            .IsRequired();

        builder.Property(x => x.TimeZoneId)
            .HasColumnName("timezone_id")
            .HasMaxLength(TenantConfirmationPolicy.MaxTimeZoneIdLength)
            .IsRequired();

        builder.Property(x => x.QuietHoursStartLocal)
            .HasColumnName("quiet_hours_start_local")
            .HasColumnType("time");

        builder.Property(x => x.QuietHoursEndLocal)
            .HasColumnName("quiet_hours_end_local")
            .HasColumnType("time");

        builder.Property(x => x.PrimaryChannelStrategy)
            .HasColumnName("primary_channel_strategy")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.AlternateChannelStrategy)
            .HasColumnName("alternate_channel_strategy")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.EnableRescoreAtReminder2)
            .HasColumnName("enable_rescore_at_reminder2")
            .IsRequired();

        builder.Property(x => x.CountSentAsDeliveredForUnanswered)
            .HasColumnName("count_sent_as_delivered_for_unanswered")
            .IsRequired();

        builder.Property(x => x.WeightConfirmedEarly)
            .HasColumnName("weight_confirmed_early")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        builder.Property(x => x.WeightReminderUnanswered)
            .HasColumnName("weight_reminder_unanswered")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        builder.Property(x => x.MaxReminderUnansweredCumulative)
            .HasColumnName("max_reminder_unanswered_cumulative")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        builder.Property(x => x.WeightDeliveryFailed)
            .HasColumnName("weight_delivery_failed")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        builder.Property(x => x.WeightRepliedWithoutConfirmation)
            .HasColumnName("weight_replied_without_confirmation")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        builder.Property(x => x.WeightFinalConfirmed)
            .HasColumnName("weight_final_confirmed")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        builder.Property(x => x.MediumExitThreshold)
            .HasColumnName("medium_exit_threshold")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.MediumEnterThreshold)
            .HasColumnName("medium_enter_threshold")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.HighExitThreshold)
            .HasColumnName("high_exit_threshold")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.HighEnterThreshold)
            .HasColumnName("high_enter_threshold")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(x => x.AutoOverbookEnabled)
            .HasColumnName("auto_overbook_enabled")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.AutoOverbookMinEffectiveScore)
            .HasColumnName("auto_overbook_min_effective_score")
            .HasColumnType("numeric(5,4)")
            .HasDefaultValue(0.8500m)
            .IsRequired();

        builder.Property(x => x.HitlOverbookMinEffectiveScore)
            .HasColumnName("hitl_overbook_min_effective_score")
            .HasColumnType("numeric(5,4)")
            .HasDefaultValue(0.5000m)
            .IsRequired();

        builder.Property(x => x.ExcludedSeniorAgeYears)
            .HasColumnName("excluded_senior_age_years")
            .HasDefaultValue(70)
            .IsRequired();

        builder.Property(x => x.ExcludedClinicalSpecialtiesJson)
            .HasColumnName("excluded_clinical_specialties_json")
            .HasColumnType("jsonb")
            .HasDefaultValue(TenantConfirmationPolicy.DefaultExcludedClinicalSpecialtiesJson)
            .IsRequired();

        builder.Property(x => x.ExcludedClinicalKeywordsJson)
            .HasColumnName("excluded_clinical_keywords_json")
            .HasColumnType("jsonb")
            .HasDefaultValue(TenantConfirmationPolicy.DefaultExcludedClinicalKeywordsJson)
            .IsRequired();

        builder.Property(x => x.WaitingListOfferTtlMinutes)
            .HasColumnName("waiting_list_offer_ttl_minutes")
            .HasDefaultValue(60)
            .IsRequired();

        builder.Property(x => x.MaxWaitingListCascadeOffers)
            .HasColumnName("max_waiting_list_cascade_offers")
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(x => x.MinLeadMinutesForWaitingListOffer)
            .HasColumnName("min_lead_minutes_for_waiting_list_offer")
            .HasDefaultValue(60)
            .IsRequired();

        builder.Property(x => x.MaxReschedulesPerAppointment)
            .HasColumnName("max_reschedules_per_appointment")
            .HasDefaultValue(3)
            .IsRequired();

        builder.Property(x => x.MinShadowEvaluationsForActivation)
            .HasColumnName("min_shadow_evaluations_for_activation")
            .HasDefaultValue(200)
            .IsRequired();

        builder.Property(x => x.MaxFalsePositiveRateForActivation)
            .HasColumnName("max_false_positive_rate_for_activation")
            .HasColumnType("numeric(5,4)")
            .HasDefaultValue(0.1500m)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
