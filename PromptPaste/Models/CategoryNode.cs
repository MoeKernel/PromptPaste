using System.Collections.ObjectModel;

namespace PromptPaste.Models;

public class CategoryNode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public string Path { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public ObservableCollection<CategoryNode> Children { get; } = new();

    public string DisplayName => ItemCount > 0 ? $"{Name}  {ItemCount}" : Name;
}
