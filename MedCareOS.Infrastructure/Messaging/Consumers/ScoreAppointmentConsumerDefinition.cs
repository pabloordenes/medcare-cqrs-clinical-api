using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Infrastructure.Messaging.Configuration;
using MedCareOS.Infrastructure.Messaging.Filters;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Messaging.Consumers;

public class ScoreAppointmentConsumerDefinition : ConsumerDefinition<ScoreAppointmentConsumer>
{
    private readonly RabbitMqMessagingOptions _messagingOptions;

    public ScoreAppointmentConsumerDefinition(IOptions<RabbitMqMessagingOptions> messagingOptions)
    {
        _messagingOptions = messagingOptions.Value;
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ScoreAppointmentConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        var redeliveryIntervals = (_messagingOptions.RedeliveryIntervalsMs is { Length: > 0 }
                ? _messagingOptions.RedeliveryIntervalsMs
                : [60_000, 300_000, 900_000])
            .Select(ms => TimeSpan.FromMilliseconds(Math.Max(10, ms)))
            .ToArray();

        if (string.Equals(_messagingOptions.RedeliveryMode, "RabbitMqDelayedExchange", StringComparison.OrdinalIgnoreCase))
        {
            endpointConfigurator.UseDelayedRedelivery(r =>
            {
                ConfigureIgnoredExceptions(r);
                r.Intervals(redeliveryIntervals);
            });
        }
        else if (string.Equals(_messagingOptions.RedeliveryMode, "QuartzInMemory", StringComparison.OrdinalIgnoreCase))
        {
            endpointConfigurator.UseScheduledRedelivery(r =>
            {
                ConfigureIgnoredExceptions(r);
                r.Intervals(redeliveryIntervals);
            });
        }

        endpointConfigurator.UseMessageRetry(r =>
        {
            ConfigureIgnoredExceptions(r);
            r.Exponential(
                retryLimit: Math.Max(0, _messagingOptions.RetryLimit),
                minInterval: TimeSpan.FromMilliseconds(Math.Max(10, _messagingOptions.RetryMinIntervalMs)),
                maxInterval: TimeSpan.FromMilliseconds(Math.Max(50, _messagingOptions.RetryMaxIntervalMs)),
                intervalDelta: TimeSpan.FromMilliseconds(Math.Max(10, _messagingOptions.RetryIntervalDeltaMs)));
        });

        endpointConfigurator.UseConsumeFilter(typeof(IdempotentConsumeFilter<>), context);
    }

    private static void ConfigureIgnoredExceptions(IRetryConfigurator retryConfigurator)
    {
        retryConfigurator.Ignore<ArgumentException>();
        retryConfigurator.Ignore<FormatException>();
        retryConfigurator.Ignore<JsonException>();
        retryConfigurator.Ignore<KeyNotFoundException>();
        retryConfigurator.Ignore<InvalidOperationException>();
        retryConfigurator.Ignore<AiServicePermanentException>();
    }
}
