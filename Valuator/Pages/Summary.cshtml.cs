using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace Valuator.Pages;

public class SummaryModel : PageModel
{
    private readonly ShardedRedis _redis;

    public SummaryModel(ShardedRedis redis)
    {
        _redis = redis;
    }

    public double? Rank { get; private set; }
    public double Similarity { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        if (!Guid.TryParse(id, out var textId))
        {
            return BadRequest();
        }

        id = textId.ToString();
        IDatabase? database = await _redis.GetDatabaseByTextIdAsync(id);
        if (database is null)
        {
            return NotFound();
        }

        bool textExists = await database.KeyExistsAsync($"TEXT-{id}");
        RedisValue similarity = await database.StringGetAsync($"SIMILARITY-{id}");
        if (!textExists || similarity.IsNull)
        {
            return NotFound();
        }

        RedisValue rank = await database.StringGetAsync($"RANK-{id}");
        Rank = rank.IsNull ? null : double.Parse(rank.ToString(), CultureInfo.InvariantCulture);
        Similarity = double.Parse(similarity.ToString(), CultureInfo.InvariantCulture);
        return Page();
    }
}
