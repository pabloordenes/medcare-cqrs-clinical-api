using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<ScheduleBlock> ScheduleBlocks { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<AppointmentScheduledAction> AppointmentScheduledActions { get; }
    DbSet<RiskSignal> RiskSignals { get; }
    DbSet<TenantConfirmationPolicy> TenantConfirmationPolicies { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Staff> Staffs { get; }
    DbSet<Box> Boxes { get; }
    DbSet<User> Users { get; }
    DbSet<AiResolutionTask> AiResolutionTasks { get; }
    DbSet<ReceptionTask> ReceptionTasks { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PatientActionToken> PatientActionTokens { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<ProcessedWebhookKey> ProcessedWebhookKeys { get; }
    DbSet<ProcessedMessage> ProcessedMessages { get; }
    DbSet<OverbookCollisionEvent> OverbookCollisionEvents { get; }
    DbSet<WaitingList> WaitingLists { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}