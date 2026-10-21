using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RankCalculator;

public sealed class ShardedRedis : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShardedRedis> _logger;
    private readonly Dictionary<string, IConnectionMultiplexer> _connections = new();

    public ShardedRedis(IConfiguration configuration, ILogger<ShardedRedis> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ConnectAsync()
    {
        try
        {
            foreach (string region in new[] { "MAIN", "RU", "EU", "ASIA" })
            {
                string setting = $"DB_{region}";
                string? address = _configuration[setting];
                if (string.IsNullOrWhiteSpace(address))
                {
                    throw new InvalidOperationException($"Не задана настройка {setting}.");
                }
                _connections.Add(region, await ConnectionMultiplexer.ConnectAsync(address));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private IDatabase GetDatabase(string region)
    {
        if (region is not ("RU" or "EU" or "ASIA"))
        {
            throw new InvalidDataException($"Недопустимый код сегмента: {region}.");
        }
        return _connections[region].GetDatabase();
    }

    public async Task<IDatabase?> GetDatabaseByTextIdAsync(string id)
    {
        RedisValue region = await _connections["MAIN"].GetDatabase().StringGetAsync($"SHARD-{id}");
        if (region.IsNull)
        {
            return null;
        }
        string shardKey = region.ToString();
        IDatabase database = GetDatabase(shardKey);
        _logger.LogInformation("LOOKUP: {TextId}, {Region}", id, shardKey);
        return database;
    }

    public async Task PingAsync()
    {
        foreach (var connection in _connections.Values)
        {
            await connection.GetDatabase().PingAsync();
        }
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
        _connections.Clear();
    }
}
