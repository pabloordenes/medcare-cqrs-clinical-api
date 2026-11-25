using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _dbContext;

    public NotificationRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<Notification?> GetByProviderMessageIdAsync(
        string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
            return null;

        return await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.ProviderMessageId == providerMessageId, cancellationToken);
    }

    public async Task<bool> ExistsByIdempotencyKeyAsync(
        Guid appointmentId,
        NotificationType type,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(
                n => n.AppointmentId == appointmentId
                     && n.Type == type
                     && n.ScheduledFor == scheduledFor
                     && n.Status != NotificationStatus.Cancelled,
                cancellationToken);
    }

    public async Task<bool> ExistsByWaitingListOfferAsync(
        Guid waitingListOfferId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(
                n => n.WaitingListOfferId == waitingListOfferId
                     && n.Type == type
                     && n.Status != NotificationStatus.Cancelled,
                cancellationToken);
    }

    public async Task<Notification?> GetLatestActionableByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.PatientId == patientId
                        && (n.AppointmentId != null
                            || (n.WaitingListOfferId != null && n.Type == NotificationType.WaitingListOffer))
                        && (n.Type == NotificationType.Reminder
                            || n.Type == NotificationType.FinalConfirmation
                            || n.Type == NotificationType.OverbookNotice
                            || n.Type == NotificationType.WaitingListOffer)
                        && (n.Status == NotificationStatus.Sent
                            || n.Status == NotificationStatus.Delivered
                            || n.Status == NotificationStatus.Read))
            .OrderByDescending(n => n.SentAt ?? n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Notification?> GetLatestByAppointmentAndTypeAsync(
        Guid appointmentId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.AppointmentId == appointmentId
                        && n.Type == type
                        && n.Status != NotificationStatus.Cancelled)
            .OrderByDescending(n => n.SentAt ?? n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Notification>> GetPendingRedactionBatchAsync(
        DateTimeOffset createdBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .Where(n => n.ContentRedactedAt == null && n.CreatedAt <= createdBeforeUtc)
            .OrderBy(n => n.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }
}
