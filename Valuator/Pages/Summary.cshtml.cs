using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace Valuator.Pages;

public class SummaryModel : PageModel
{
    private readonly IDatabase _database;

    public SummaryModel(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public double? Rank { get; private set; }
    public double Similarity { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        if (!Guid.TryParse(id, out _))
        {
            return BadRequest();
        }

        bool textExists = await _database.KeyExistsAsync($"TEXT-{id}");
        RedisValue similarity = await _database.StringGetAsync($"SIMILARITY-{id}");
        if (!textExists || similarity.IsNull)
        {
            return NotFound();
        }

        RedisValue rank = await _database.StringGetAsync($"RANK-{id}");
        Rank = rank.IsNull ? null : double.Parse(rank.ToString(), CultureInfo.InvariantCulture);
        Similarity = double.Parse(similarity.ToString(), CultureInfo.InvariantCulture);
        return Page();
    }
}
