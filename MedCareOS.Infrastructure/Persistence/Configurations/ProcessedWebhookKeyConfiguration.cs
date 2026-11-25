using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class ProcessedWebhookKeyConfiguration : IEntityTypeConfiguration<ProcessedWebhookKey>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookKey> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key)
            .HasColumnName("key")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Endpoint)
            .HasColumnName("endpoint")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ResponseStatusCode)
            .HasColumnName("response_status_code");

        builder.Property(x => x.ResponseBodyJson)
            .HasColumnName("response_body_json")
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
