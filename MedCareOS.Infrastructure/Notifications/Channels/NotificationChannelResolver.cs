using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Notifications.Channels;

public class NotificationChannelResolver : INotificationChannelResolver
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly NotificationOptions _options;

    public NotificationChannelResolver(
        IEnumerable<INotificationChannel> channels,
        IOptions<NotificationOptions> options)
    {
        _channels = channels.ToList();
        _options = options.Value;
    }

    public INotificationChannel Resolve(NotificationChannel channel)
    {
        var configuredProvider = channel switch
        {
            NotificationChannel.Email => string.IsNullOrWhiteSpace(_options.EmailProvider) ? "Fake" : _options.EmailProvider.Trim(),
            NotificationChannel.WhatsApp => string.IsNullOrWhiteSpace(_options.WhatsAppProvider) ? "Fake" : _options.WhatsAppProvider.Trim(),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Canal no soportado.")
        };

        var match = _channels.FirstOrDefault(c =>
            c.Channel == channel &&
            string.Equals(c.ProviderName, configuredProvider, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new InvalidOperationException(
            $"No se encontró un adapter registrado para el canal '{channel}' con proveedor '{configuredProvider}'.");
    }
}
