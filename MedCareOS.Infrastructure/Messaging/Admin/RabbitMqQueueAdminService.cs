using MedCareOS.Infrastructure.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace MedCareOS.Infrastructure.Messaging.Admin;

public class RabbitMqQueueAdminService : IRabbitMqQueueAdminService
{
    private static readonly string[] KnownQueues = ["score-appointment", "send-notification"];

    private readonly RabbitMqMessagingOptions _options;
    private readonly ILogger<RabbitMqQueueAdminService> _logger;

    public RabbitMqQueueAdminService(
        IOptions<RabbitMqMessagingOptions> options,
        ILogger<RabbitMqQueueAdminService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyCollection<string> AllowedQueues => KnownQueues;

    public async Task<List<ErrorQueueSummaryDto>> GetErrorQueueSummariesAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new List<ErrorQueueSummaryDto>();
        await using var connection = await CreateConnectionAsync(cancellationToken);
        foreach (var queueName in KnownQueues)
        {
            var errorQueueName = $"{queueName}_error";
            try
            {
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                var declareOk = await channel.QueueDeclarePassiveAsync(errorQueueName, cancellationToken);
                summaries.Add(new ErrorQueueSummaryDto(queueName, errorQueueName, declareOk.MessageCount, Exists: true));
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                summaries.Add(new ErrorQueueSummaryDto(queueName, errorQueueName, 0, Exists: false));
            }
        }

        return summaries;
    }

    public async Task<RequeueResultDto> RequeueErrorMessagesAsync(
        string queueName,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        if (!KnownQueues.Contains(queueName, StringComparer.Ordinal))
            throw new ArgumentException($"La cola '{queueName}' no está en la lista de colas permitidas.", nameof(queueName));

        if (maxMessages is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "maxMessages debe estar entre 1 y 500.");

        var errorQueueName = $"{queueName}_error";
        int requeuedCount = 0;
        int failedCount = 0;
        uint remaining = 0;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            var status = await channel.QueueDeclarePassiveAsync(errorQueueName, cancellationToken);
            remaining = status.MessageCount;
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return new RequeueResultDto(queueName, errorQueueName, 0, 0, 0);
        }

        for (int i = 0; i < maxMessages; i++)
        {
            var getResult = await channel.BasicGetAsync(errorQueueName, autoAck: false, cancellationToken);
            if (getResult is null)
            {
                remaining = 0;
                break;
            }

            try
            {
                var cleanProps = BuildCleanProperties(getResult.BasicProperties);
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: queueName,
                    mandatory: false,
                    basicProperties: cleanProps,
                    body: getResult.Body,
                    cancellationToken: cancellationToken);

                await channel.BasicAckAsync(getResult.DeliveryTag, multiple: false, cancellationToken);
                requeuedCount++;
                remaining = getResult.MessageCount;
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(
                    ex,
                    "Error al reprocesar mensaje {MessageId} desde {ErrorQueue} hacia {TargetQueue}",
                    getResult.BasicProperties.MessageId,
                    errorQueueName,
                    queueName);
                await channel.BasicNackAsync(getResult.DeliveryTag, multiple: false, requeue: true, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Requeue completado desde {ErrorQueue} hacia {TargetQueue}: Requeued={RequeuedCount}, Failed={FailedCount}, RemainingApprox={Remaining}",
            errorQueueName,
            queueName,
            requeuedCount,
            failedCount,
            remaining);

        return new RequeueResultDto(queueName, errorQueueName, requeuedCount, failedCount, remaining);
    }

    private async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var rmqHost = string.IsNullOrWhiteSpace(_options.Host) ? "localhost" : _options.Host;
        var rmqUser = string.IsNullOrWhiteSpace(_options.Username) ? "guest" : _options.Username;
        var rmqPass = string.IsNullOrWhiteSpace(_options.Password) ? "guest" : _options.Password;
        var rmqVHost = _options.VirtualHost;
        if (string.IsNullOrWhiteSpace(rmqVHost))
        {
            rmqVHost = rmqHost is "localhost" or "127.0.0.1" or "rabbitmq" ? "/" : rmqUser;
        }

        var factory = new ConnectionFactory
        {
            HostName = rmqHost,
            Port = _options.Port > 0 ? _options.Port : 5672,
            UserName = rmqUser,
            Password = rmqPass,
            VirtualHost = rmqVHost
        };

        return await factory.CreateConnectionAsync(cancellationToken);
    }

    private static BasicProperties BuildCleanProperties(IReadOnlyBasicProperties source)
    {
        var props = new BasicProperties
        {
            ContentType = source.ContentType,
            ContentEncoding = source.ContentEncoding,
            CorrelationId = source.CorrelationId,
            MessageId = source.MessageId,
            DeliveryMode = source.DeliveryMode,
            Type = source.Type,
            AppId = source.AppId
        };

        if (source.Headers is not null)
        {
            var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in source.Headers)
            {
                if (!kvp.Key.StartsWith("MT-Fault-", StringComparison.OrdinalIgnoreCase))
                {
                    filtered[kvp.Key] = kvp.Value;
                }
            }

            props.Headers = filtered;
        }

        return props;
    }
}
