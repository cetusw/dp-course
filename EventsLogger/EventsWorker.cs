using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventsLogger;

public sealed class EventsWorker : BackgroundService
{
    private const string EventsExchange = "valuator.events";
    private readonly IConfiguration _configuration;
    private readonly ILogger<EventsWorker> _logger;

    public EventsWorker(IConfiguration configuration, ILogger<EventsWorker> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private ConnectionFactory CreateFactory()
    {
        var factory = new ConnectionFactory();
        string? address = _configuration.GetConnectionString("RabbitMQ");
        if (address is not null)
        {
            factory.Uri = new Uri(address);
        }
        return factory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = await CreateFactory().CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        string queueName = await SubscribeAsync(channel, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, message) => HandleAsync(channel, message, stoppingToken);
        string consumerTag = await channel.BasicConsumeAsync(queueName, false, consumer, stoppingToken);
        _logger.LogInformation("Подписчик событий готов: {Queue}", queueName);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicCancelAsync(consumerTag);
        }
    }

    private static async Task<string> SubscribeAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);
        var queue = await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue.QueueName, EventsExchange, "valuator.rank.calculated",
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue.QueueName, EventsExchange, "valuator.similarity.calculated",
            cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, false, cancellationToken);
        return queue.QueueName;
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs message, CancellationToken cancellationToken)
    {
        bool valid;
        try
        {
            valid = LogEvent(message.RoutingKey, message.Body);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Некорректный JSON события {RoutingKey}", message.RoutingKey);
            valid = false;
        }
        if (valid)
        {
            await channel.BasicAckAsync(message.DeliveryTag, false, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Отклонено некорректное событие {RoutingKey}", message.RoutingKey);
            await channel.BasicRejectAsync(message.DeliveryTag, false, cancellationToken);
        }
    }

    private bool LogEvent(string routingKey, ReadOnlyMemory<byte> body)
    {
        string? eventName = routingKey switch
        {
            "valuator.rank.calculated" => "RankCalculated",
            "valuator.similarity.calculated" => "SimilarityCalculated",
            _ => null
        };
        if (eventName is null)
        {
            return false;
        }
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("textId", out var textId) || textId.ValueKind != JsonValueKind.String ||
            !textId.TryGetGuid(out var id) ||
            !root.TryGetProperty("value", out var metric) || metric.ValueKind != JsonValueKind.Number ||
            !metric.TryGetDouble(out double value) || !double.IsFinite(value))
        {
            return false;
        }
        _logger.LogInformation("{EventName} {TextId}: {Value}",
            eventName, id, value.ToString(CultureInfo.InvariantCulture));
        return true;
    }
}
