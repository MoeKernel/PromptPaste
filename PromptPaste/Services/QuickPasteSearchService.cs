using PromptPaste.Models;

namespace PromptPaste.Services;

public static class QuickPasteSearchService
{
    public const int DefaultLimit = 8;

    public static List<ClipboardItem> Search(IEnumerable<ClipboardItem> items, string? keyword, int limit = DefaultLimit)
    {
        var source = items.Where(i => i.DeletedAt == null).ToList();
        if (string.IsNullOrWhiteSpace(keyword))
            return source.Take(limit).ToList();

        var terms = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return source
            .Where(item => terms.All(term => Matches(item, term)))
            .Take(limit)
            .ToList();
    }

    private static bool Matches(ClipboardItem item, string term)
        => item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Content.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase));
}
