using MassTransit;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<AppointmentScheduledAction> AppointmentScheduledActions { get; set; }
    public DbSet<RiskSignal> RiskSignals { get; set; }
    public DbSet<TenantConfirmationPolicy> TenantConfirmationPolicies { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<ScheduleBlock> ScheduleBlocks { get; set; }
    public DbSet<Box> Boxes { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PatientActionToken> PatientActionTokens { get; set; }
    public DbSet<NotificationTemplate> NotificationTemplates { get; set; }
    public DbSet<Staff> Staffs { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<WaitingList> WaitingLists { get; set; }
    public DbSet<AiResolutionTask> AiResolutionTasks { get; set; }
    public DbSet<ReceptionTask> ReceptionTasks { get; set; }
    public DbSet<ProcessedWebhookKey> ProcessedWebhookKeys { get; set; }
    public DbSet<ProcessedMessage> ProcessedMessages { get; set; }
    public DbSet<OverbookCollisionEvent> OverbookCollisionEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.AddInboxStateEntity(x => x.ToTable("inbox_state", "messaging"));
        builder.AddOutboxMessageEntity(x => x.ToTable("outbox_message", "messaging"));
        builder.AddOutboxStateEntity(x => x.ToTable("outbox_state", "messaging"));

        base.OnModelCreating(builder);
    }
}