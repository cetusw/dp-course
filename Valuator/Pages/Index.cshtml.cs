using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private readonly IDatabase _database;
    private readonly RankPublisher _rankPublisher;

    public IndexModel(IConnectionMultiplexer connection, RankPublisher rankPublisher)
    {
        _database = connection.GetDatabase();
        _rankPublisher = rankPublisher;
    }

    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Text { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return BadRequest("Введите непустой текст.");
        }

        var id = Guid.NewGuid().ToString();
        await _database.StringSetAsync($"TEXT-{id}", Text);

        bool isNewText = await _database.SetAddAsync("PROCESSED-TEXTS", Text);
        double similarity = isNewText ? 0 : 1;
        await _database.StringSetAsync($"SIMILARITY-{id}", similarity.ToString(CultureInfo.InvariantCulture));

        await _rankPublisher.PublishAsync(id, HttpContext.RequestAborted);

        return RedirectToPage("/Summary", new { id });
    }
}
