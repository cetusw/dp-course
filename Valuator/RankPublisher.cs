using System.Text;
using RabbitMQ.Client;

namespace Valuator;

public sealed class RankPublisher : IAsyncDisposable
{
    private const string QueueName = "valuator.processing.rank";
    private readonly IConnection _connection;

    private RankPublisher(IConnection connection)
    {
        _connection = connection;
    }

    public static async Task<RankPublisher> CreateAsync(IConfiguration configuration)
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
            await channel.ExchangeDeclareAsync(QueueName, ExchangeType.Direct, durable: true,
                cancellationToken: timeout.Token);
            await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
                cancellationToken: timeout.Token);
            await channel.QueueBindAsync(QueueName, QueueName, "", cancellationToken: timeout.Token);
            return new RankPublisher(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task PublishAsync(string id, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await _connection.CreateChannelAsync(options, timeout.Token);
        await channel.BasicPublishAsync(QueueName, "", mandatory: true,
            basicProperties: new BasicProperties { Persistent = true },
            body: Encoding.UTF8.GetBytes(id), cancellationToken: timeout.Token);
    }

    public ValueTask DisposeAsync()
    {
        return _connection.DisposeAsync();
    }
}
