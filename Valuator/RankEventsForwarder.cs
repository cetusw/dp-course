using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Valuator;

public sealed class RankEventsForwarder : BackgroundService
{
    private const string EventsExchange = "valuator.events";
    private readonly IConfiguration _configuration;
    private readonly IHubContext<RankHub> _hubContext;
    private readonly ILogger<RankEventsForwarder> _logger;

    public RankEventsForwarder(IConfiguration configuration, IHubContext<RankHub> hubContext,
        ILogger<RankEventsForwarder> logger)
    {
        _configuration = configuration;
        _hubContext = hubContext;
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
        _logger.LogInformation("Подписчик RankCalculated готов: {Queue}", queueName);
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
        await channel.BasicQosAsync(0, 1, false, cancellationToken);
        return queue.QueueName;
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs message, CancellationToken cancellationToken)
    {
        try
        {
            if (message.RoutingKey != "valuator.rank.calculated" ||
                !TryReadRank(message.Body, out var textId, out double rank))
            {
                _logger.LogWarning("Отклонено некорректное событие RankCalculated");
                await channel.BasicRejectAsync(message.DeliveryTag, false, cancellationToken);
                return;
            }
            await _hubContext.Clients.Group($"rank:{textId}").SendAsync("RankCalculated", rank, cancellationToken);
            await channel.BasicAckAsync(message.DeliveryTag, false, cancellationToken);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Некорректный JSON события RankCalculated");
            await channel.BasicRejectAsync(message.DeliveryTag, false, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Ошибка обработки уведомления RankCalculated");
            if (channel.IsOpen)
            {
                await channel.BasicRejectAsync(message.DeliveryTag, false, cancellationToken);
            }
        }
    }

    private static bool TryReadRank(ReadOnlyMemory<byte> body, out Guid textId, out double rank)
    {
        textId = default;
        rank = default;
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("textId", out var id) && id.ValueKind == JsonValueKind.String &&
            id.TryGetGuid(out textId) &&
            root.TryGetProperty("value", out var metric) && metric.ValueKind == JsonValueKind.Number &&
            metric.TryGetDouble(out rank) && double.IsFinite(rank) && rank is >= 0 and <= 1;
    }
}
