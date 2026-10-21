using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private readonly ShardedRedis _redis;
    private readonly MessagePublisher _messagePublisher;

    public IndexModel(ShardedRedis redis, MessagePublisher messagePublisher)
    {
        _redis = redis;
        _messagePublisher = messagePublisher;
    }

    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Text { get; set; }

    [BindProperty]
    public string? Country { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return BadRequest("Введите непустой текст.");
        }

        string? region = GetRegionByCountry(Country);
        if (region is null)
        {
            return BadRequest("Выберите страну из списка.");
        }

        var id = Guid.NewGuid().ToString();
        var database = _redis.GetDatabase(region);
        await database.StringSetAsync($"TEXT-{id}", Text);
        await database.StringSetAsync($"COUNTRY-{id}", Country);

        double similarity = await _redis.CalculateSimilarityAsync(Text, region);
        await database.StringSetAsync($"SIMILARITY-{id}", similarity.ToString(CultureInfo.InvariantCulture));
        await _redis.SaveShardAsync(id, region);

        await _messagePublisher.PublishSimilarityAsync(id, similarity, HttpContext.RequestAborted);
        await _messagePublisher.PublishRankAsync(id, HttpContext.RequestAborted);

        return RedirectToPage("/Summary", new { id });
    }

    private static string? GetRegionByCountry(string? country)
    {
        return country switch
        {
            "Russia" => "RU",
            "France" or "Germany" => "EU",
            "UAE" or "India" => "ASIA",
            _ => null
        };
    }
}
