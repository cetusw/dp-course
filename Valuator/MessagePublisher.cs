using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Valuator;

public sealed class MessagePublisher : IAsyncDisposable
{
    private const string QueueName = "valuator.processing.rank";
    private const string EventsExchange = "valuator.events";
    private readonly IConnection _connection;

    private MessagePublisher(IConnection connection)
    {
        _connection = connection;
    }

    public static async Task<MessagePublisher> CreateAsync(IConfiguration configuration)
    {
        var factory = new ConnectionFactory();
        string? address = configuration.GetConnectionString("RabbitMQ");
        if (address is not null)
        {
            factory.Uri = new Uri(address);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connection = await factory.CreateConnectionAsync(timeout.Token);
        try
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
            await DeclareTopologyAsync(channel, timeout.Token);
            return new MessagePublisher(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
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

    public Task PublishRankAsync(string id, CancellationToken cancellationToken)
    {
        return PublishAsync(QueueName, "", Encoding.UTF8.GetBytes(id), true, cancellationToken);
    }

    public Task PublishSimilarityAsync(string id, double similarity, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { textId = id, value = similarity });
        return PublishAsync(EventsExchange, "valuator.similarity.calculated", body, false, cancellationToken);
    }

    private async Task PublishAsync(string exchange, string routingKey, byte[] body, bool mandatory,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await _connection.CreateChannelAsync(options, timeout.Token);
        await channel.BasicPublishAsync(exchange, routingKey, mandatory,
            basicProperties: new BasicProperties { Persistent = true },
            body: body, cancellationToken: timeout.Token);
    }

    public ValueTask DisposeAsync()
    {
        return _connection.DisposeAsync();
    }
}
