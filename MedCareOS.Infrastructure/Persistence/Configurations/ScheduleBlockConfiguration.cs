using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.ToTable("schedule_blocks");
        
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id");
        
        builder.Property(x => x.StaffId)
            .HasColumnName("medico_id")
            .IsRequired();

        builder.Property(x => x.BoxId)
            .HasColumnName("box_id")
            .IsRequired();
        
        builder.Property(x => x.StartTime)
            .HasColumnName("fecha_inicio")
            .IsRequired();
        
        builder.Property(x => x.EndTime)
            .HasColumnName("fecha_fin")
            .IsRequired();
        
        // propiedades de regla
        
        builder.Property(x => x.RuleGroupId)
            .HasColumnName("rule_group_id")
            .IsRequired();
        
        builder.Property(x => x.RuleStartDate)
            .HasColumnName("rule_start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.RuleEndDate)
            .HasColumnName("rule_end_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.RuleStartTime)
            .HasColumnName("rule_start_time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property(x => x.RuleEndTime)
            .HasColumnName("rule_end_time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property(x => x.RuleDaysOfWeek)
            .HasColumnName("rule_days_of_week")
            .IsRequired();

        // fk
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Box>()
            .WithMany()
            .HasForeignKey(x => x.BoxId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}