using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        // 1. Mapeo a la tabla exacta de la Biblia
        builder.ToTable("citas");

        // 2. Llave Primaria
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        // 3. Foreign Keys y Tenant (Forzando el snake_case de Supabase)
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasDefaultValue(RiskThresholds.DefaultTenantId)
            .IsRequired();

        builder.Property(x => x.ScheduleBlockId).HasColumnName("schedule_block_id");
        builder.Property(x => x.PatientId).HasColumnName("paciente_id");
        
        builder.Property(x => x.Status)
            .HasColumnName("estado")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.ConfirmationState)
            .HasColumnName("confirmation_state")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(ConfirmationState.Pending)
            .IsRequired();

        builder.Property(x => x.OrchestrationSuppressed)
            .HasColumnName("orchestration_suppressed")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(x => x.OfferAcceptedAt)
            .HasColumnName("offer_accepted_at");

        builder.Property(x => x.CheckedInAt)
            .HasColumnName("checked_in_at");

        builder.Property(x => x.FinalConfirmationRequestedAt)
            .HasColumnName("final_confirmation_requested_at");

        builder.Property(x => x.DeadlineMissedAt)
            .HasColumnName("deadline_missed_at");

        builder.Property(x => x.IsDisplacedByOverbook)
            .HasColumnName("is_displaced_by_overbook")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.WasEverDisplacedByOverbook)
            .HasColumnName("was_ever_displaced_by_overbook")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.DisplacedAt)
            .HasColumnName("displaced_at");

        builder.Property(x => x.WaitingListOfferId)
            .HasColumnName("waiting_list_offer_id");

        builder.Property(x => x.ConfirmationOutcome)
            .HasColumnName("confirmation_outcome")
            .HasConversion<string>()
            .HasMaxLength(40);
        
        builder.Property(x => x.SymptomsRaw)
            .HasColumnName("sintomas_texto");
        
        builder.Property(x => x.ProcessedSymptoms)
            .HasColumnName("sintomas_procesados")
            .HasColumnType("jsonb");
        
        builder.Property(x => x.NlpSummary)
            .HasColumnName("resumen_nlp");
        
        builder.Property(x => x.NoShowRiskScore)
            .HasColumnName("score_riesgo")
            .HasColumnType("numeric(5,4)");

        builder.Property(x => x.RiskBand)
            .HasColumnName("banda_riesgo");

        builder.Property(x => x.ModelRiskScore)
            .HasColumnName("score_modelo")
            .HasColumnType("numeric(5,4)");

        builder.Property(x => x.LatestModelRiskScore)
            .HasColumnName("latest_model_score")
            .HasColumnType("numeric(5,4)");

        builder.Property(x => x.RescoredAt)
            .HasColumnName("rescored_at");

        builder.Property(x => x.EffectiveRiskScore)
            .HasColumnName("score_efectivo")
            .HasColumnType("numeric(5,4)");

        builder.Property(x => x.EffectiveBand)
            .HasColumnName("effective_band")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.ScoringStatus)
            .HasColumnName("scoring_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(ScoringStatus.Pending)
            .IsRequired();

        builder.Property(x => x.NlpStatus)
            .HasColumnName("nlp_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(NlpStatus.Pending)
            .IsRequired();

        builder.Property(x => x.ScoringAttempts)
            .HasColumnName("scoring_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.NextScoringRetryAt)
            .HasColumnName("next_scoring_retry_at");

        builder.Property(x => x.LastScoringError)
            .HasColumnName("last_scoring_error")
            .HasMaxLength(500);

        builder.Property(x => x.ScoredAt)
            .HasColumnName("scored_at");

        builder.Property(x => x.SchedulingSource)
            .HasColumnName("fuente_agendamiento");
        
        builder.Property(x => x.ActualAttendance)
            .HasColumnName("asistio");
        
        // fk
        builder.HasOne<ScheduleBlock>()
            .WithMany()
            .HasForeignKey(x => x.ScheduleBlockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ScheduleBlockId)
            .HasDatabaseName("IX_citas_schedule_block_id")
            .IsUnique()
            .HasFilter("estado <> 'Cancelled'");

        builder.HasIndex(x => new { x.ScoringStatus, x.NlpStatus, x.NextScoringRetryAt })
            .HasDatabaseName("IX_citas_scoring_retry")
            .HasFilter("estado IN ('Scheduled', 'Confirmed') AND (scoring_status <> 'Scored' OR nlp_status IN ('Pending', 'Failed'))");

        builder.HasIndex(x => new { x.TenantId, x.ConfirmationOutcome, x.ScoredAt })
            .HasDatabaseName("IX_citas_calibration_outcome")
            .HasFilter("confirmation_outcome IS NOT NULL");
    }
}