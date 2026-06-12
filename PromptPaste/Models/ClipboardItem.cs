namespace PromptPaste.Models;

public class ClipboardItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ItemType { get; set; } = "AI提示词";
    public int UsageCount { get; set; }
    public DateTime? LastUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<int> CategoryIds { get; set; } = new();
    public List<string> CategoryPaths { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public ClipboardItem Clone() => new()
    {
        Id = Id,
        Title = Title,
        Content = Content,
        ItemType = ItemType,
        UsageCount = UsageCount,
        LastUsed = LastUsed,
        CreatedAt = CreatedAt,
        CategoryIds = CategoryIds.ToList(),
        CategoryPaths = CategoryPaths.ToList(),
        Tags = Tags.ToList()
    };
}
