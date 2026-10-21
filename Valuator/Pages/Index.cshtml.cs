using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private readonly IDatabase _database;

    public IndexModel(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
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

        double rank = CalculateRank(Text);
        await _database.StringSetAsync($"RANK-{id}", rank.ToString(CultureInfo.InvariantCulture));

        bool isNewText = await _database.SetAddAsync("PROCESSED-TEXTS", Text);
        double similarity = isNewText ? 0 : 1;
        await _database.StringSetAsync($"SIMILARITY-{id}", similarity.ToString(CultureInfo.InvariantCulture));

        return RedirectToPage("/Summary", new { id });
    }

    private static double CalculateRank(string text)
    {
        int totalCount = 0;
        int nonAlphabeticCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            totalCount++;
            if (!IsAlphabetic(rune.Value))
            {
                nonAlphabeticCount++;
            }
        }

        return (double)nonAlphabeticCount / totalCount;
    }

    private static bool IsAlphabetic(int symbol)
    {
        return symbol is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
            or >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё';
    }
}
