using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using StackExchange.Redis;

namespace RankCalculator;

public sealed class RankWorker : BackgroundService
{
    private const string QueueName = "valuator.processing.rank";
    private const string EventsExchange = "valuator.events";
    private readonly IConfiguration _configuration;
    private readonly ILogger<RankWorker> _logger;
    private readonly ShardedRedis _redis;

    public RankWorker(IConfiguration configuration, ILogger<RankWorker> logger, ShardedRedis redis)
    {
        _configuration = configuration;
        _logger = logger;
        _redis = redis;
    }

    public static async Task CheckDependenciesAsync(ShardedRedis redis, IConfiguration configuration)
    {
        await redis.PingAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await CreateFactory(configuration).CreateConnectionAsync(timeout.Token);
        Console.WriteLine("DB_MAIN, DB_RU, DB_EU, DB_ASIA и RabbitMQ доступны.");
    }

    private static ConnectionFactory CreateFactory(IConfiguration configuration)
    {
        var factory = new ConnectionFactory();
        string? address = configuration.GetConnectionString("RabbitMQ");
        if (address is not null)
        {
            factory.Uri = new Uri(address);
        }
        return factory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = await CreateFactory(_configuration).CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, message) =>
            HandleAsync(channel, connection, message, stoppingToken);
        string consumerTag = await channel.BasicConsumeAsync(QueueName, false, consumer, stoppingToken);
        _logger.LogInformation("Потребитель очереди {Queue} готов", QueueName);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicCancelAsync(consumerTag);
        }
    }

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(QueueName, ExchangeType.Direct, durable: true,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(QueueName, QueueName, "", cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);
    }

    private async Task HandleAsync(IChannel channel, IConnection connection, BasicDeliverEventArgs message,
        CancellationToken cancellationToken)
    {
        string id = Encoding.UTF8.GetString(message.Body.Span);
        try
        {
            if (await CalculateAndSaveAsync(connection, id, cancellationToken))
            {
                await channel.BasicAckAsync(message.DeliveryTag, false, cancellationToken);
            }
            else
            {
                await channel.BasicRejectAsync(message.DeliveryTag, false, cancellationToken);
            }
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(exception, "Отклонено задание {TextId}: недопустимый сегмент", id);
            await channel.BasicRejectAsync(message.DeliveryTag, false, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is RedisConnectionException or TimeoutException or RabbitMQClientException
                or IOException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Сбой обработки задания {TextId}; повтор через 5 секунд", id);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(message.DeliveryTag, false, true, cancellationToken);
            }
        }
    }

    private async Task<bool> CalculateAndSaveAsync(IConnection connection, string id,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var textId))
        {
            _logger.LogWarning("Отклонено задание с некорректным ID {TextId}", id);
            return false;
        }
        id = textId.ToString();
        IDatabase? database = await _redis.GetDatabaseByTextIdAsync(id);
        if (database is null)
        {
            _logger.LogWarning("Отклонено задание {TextId}: сегмент не найден", id);
            return false;
        }
        RedisValue text = await database.StringGetAsync($"TEXT-{id}");
        if (text.IsNullOrEmpty)
        {
            _logger.LogWarning("Отклонено задание {TextId}: текст отсутствует или пуст", id);
            return false;
        }
        double rank = CalculateRank(text.ToString());
        await database.StringSetAsync($"RANK-{id}", rank.ToString(CultureInfo.InvariantCulture));
        await PublishRankEventAsync(connection, id, rank, cancellationToken);
        _logger.LogInformation("RankCalculated {TextId}: {Rank}", id, rank);
        return true;
    }

    private static async Task PublishRankEventAsync(IConnection connection, string id, double rank,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(options, timeout.Token);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { textId = id, value = rank });
        await channel.BasicPublishAsync(EventsExchange, "valuator.rank.calculated", mandatory: false,
            basicProperties: new BasicProperties { ContentType = "application/json" },
            body: body, cancellationToken: timeout.Token);
    }

    private static double CalculateRank(string text)
    {
        int totalCount = 0;
        int nonAlphabeticCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            totalCount++;
            if (!IsAlphabetic(rune.Value))
            {
                nonAlphabeticCount++;
            }
        }

        return (double)nonAlphabeticCount / totalCount;
    }

    private static bool IsAlphabetic(int symbol)
    {
        return symbol is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
            or >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё';
    }
}
