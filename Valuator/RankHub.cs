using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Valuator;

public sealed class RankHub : Hub
{
    private readonly IDatabase _database;

    public RankHub(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public async Task<double?> WatchText(string id)
    {
        if (!Guid.TryParse(id, out var textId))
        {
            throw new HubException("Некорректный идентификатор текста.");
        }
        var normalizedId = textId.ToString();
        if (!await _database.KeyExistsAsync($"TEXT-{normalizedId}"))
        {
            throw new HubException("Текст не найден.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"rank:{normalizedId}", Context.ConnectionAborted);
        RedisValue rank = await _database.StringGetAsync($"RANK-{normalizedId}");
        return rank.IsNull ? null : double.Parse(rank.ToString(), CultureInfo.InvariantCulture);
    }
}
