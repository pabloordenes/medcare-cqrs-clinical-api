using System.Collections.Concurrent;
using MedCareOS.Application.Common.Interfaces.Notifications;

namespace MedCareOS.Infrastructure.Notifications.Channels;

public interface IFakeNotificationStore
{
    IReadOnlyCollection<NotificationMessage> SentMessages { get; }
    void SetOverrideHandler(Func<NotificationMessage, DeliveryResult>? handler);
    DeliveryResult Execute(string providerName, NotificationMessage message);
    void Clear();
}

public sealed class InMemoryFakeNotificationStore : IFakeNotificationStore
{
    private readonly ConcurrentQueue<NotificationMessage> _messages = new();
    private Func<NotificationMessage, DeliveryResult>? _overrideHandler;

    public IReadOnlyCollection<NotificationMessage> SentMessages => _messages.ToArray();

    public void SetOverrideHandler(Func<NotificationMessage, DeliveryResult>? handler)
    {
        _overrideHandler = handler;
    }

    public DeliveryResult Execute(string providerName, NotificationMessage message)
    {
        if (_overrideHandler is not null)
        {
            var custom = _overrideHandler(message);
            if (custom.IsSuccess)
                _messages.Enqueue(message);
            return custom;
        }

        _messages.Enqueue(message);
        return DeliveryResult.Success($"fake_{message.Channel.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}", providerName);
    }

    public void Clear()
    {
        while (_messages.TryDequeue(out _)) { }
        _overrideHandler = null;
    }
}
