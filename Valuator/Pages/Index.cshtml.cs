using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private readonly IDatabase _database;
    private readonly MessagePublisher _messagePublisher;

    public IndexModel(IConnectionMultiplexer connection, MessagePublisher messagePublisher)
    {
        _database = connection.GetDatabase();
        _messagePublisher = messagePublisher;
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

        await _messagePublisher.PublishSimilarityAsync(id, similarity, HttpContext.RequestAborted);
        await _messagePublisher.PublishRankAsync(id, HttpContext.RequestAborted);

        return RedirectToPage("/Summary", new { id });
    }
}
