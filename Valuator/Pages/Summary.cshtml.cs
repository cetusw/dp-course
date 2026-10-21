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

    public string TextId { get; private set; } = string.Empty;

    public double? Rank { get; private set; }
    public double Similarity { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        if (!Guid.TryParse(id, out var textId))
        {
            return BadRequest();
        }

        TextId = textId.ToString();
        bool textExists = await _database.KeyExistsAsync($"TEXT-{TextId}");
        RedisValue similarity = await _database.StringGetAsync($"SIMILARITY-{TextId}");
        if (!textExists || similarity.IsNull)
        {
            return NotFound();
        }

        RedisValue rank = await _database.StringGetAsync($"RANK-{TextId}");
        Rank = rank.IsNull ? null : double.Parse(rank.ToString(), CultureInfo.InvariantCulture);
        Similarity = double.Parse(similarity.ToString(), CultureInfo.InvariantCulture);
        return Page();
    }
}
