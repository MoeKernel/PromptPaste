namespace PromptPaste.Models;

public class ExportClipboardItem
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> CategoryPaths { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int UsageCount { get; set; }
    public DateTime? LastUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
