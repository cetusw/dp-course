using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Valuator;

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

    public IDatabase GetDatabase(string region)
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

    public async Task<double> CalculateSimilarityAsync(string text, string region)
    {
        bool isNewText = await GetDatabase(region).SetAddAsync("PROCESSED-TEXTS", text);
        bool isDuplicate = !isNewText;
        foreach (string otherRegion in new[] { "RU", "EU", "ASIA" })
        {
            if (otherRegion == region)
            {
                continue;
            }
            bool containsText = await GetDatabase(otherRegion).SetContainsAsync("PROCESSED-TEXTS", text);
            if (containsText)
            {
                isDuplicate = true;
            }
        }
        return isDuplicate ? 1 : 0;
    }

    public async Task SaveShardAsync(string id, string region)
    {
        GetDatabase(region);
        await _connections["MAIN"].GetDatabase().StringSetAsync($"SHARD-{id}", region);
        _logger.LogInformation("LOOKUP: {TextId}, {Region}", id, region);
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
