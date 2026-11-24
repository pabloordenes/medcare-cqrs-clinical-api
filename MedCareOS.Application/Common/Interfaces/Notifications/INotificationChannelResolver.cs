using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Common.Interfaces.Notifications;

public interface INotificationChannelResolver
{
    INotificationChannel Resolve(NotificationChannel channel);
}
