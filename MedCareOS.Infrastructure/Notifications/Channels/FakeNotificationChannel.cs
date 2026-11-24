using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Infrastructure.Notifications.Channels;

public abstract class FakeNotificationChannelBase : INotificationChannel
{
    private readonly IFakeNotificationStore _store;
    private readonly ILogger _logger;

    protected FakeNotificationChannelBase(IFakeNotificationStore store, ILogger logger)
    {
        _store = store;
        _logger = logger;
    }

    public abstract NotificationChannel Channel { get; }
    public string ProviderName => "Fake";

    public Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _store.Execute(ProviderName, message);
        _logger.LogInformation(
            "[FAKE NOTIFICATION] Channel={Channel}, NotificationId={NotificationId}, Recipient={Recipient}, Template={TemplateKey}, Success={IsSuccess}, ProviderMessageId={ProviderMessageId}",
            Channel,
            message.NotificationId,
            message.RecipientAddress,
            message.TemplateKey,
            result.IsSuccess,
            result.ProviderMessageId);

        return Task.FromResult(result);
    }
}

public sealed class FakeEmailNotificationChannel : FakeNotificationChannelBase
{
    public FakeEmailNotificationChannel(
        IFakeNotificationStore store,
        ILogger<FakeEmailNotificationChannel> logger)
        : base(store, logger)
    {
    }

    public override NotificationChannel Channel => NotificationChannel.Email;
}

public sealed class FakeWhatsAppNotificationChannel : FakeNotificationChannelBase
{
    public FakeWhatsAppNotificationChannel(
        IFakeNotificationStore store,
        ILogger<FakeWhatsAppNotificationChannel> logger)
        : base(store, logger)
    {
    }

    public override NotificationChannel Channel => NotificationChannel.WhatsApp;
}
