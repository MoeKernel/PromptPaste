using System.Windows;
using System.Windows.Controls;
using PromptPaste.Models;

namespace PromptPaste.Views.Dialogs;

public partial class ItemDialog : Window
{
    private readonly ClipboardItem? _originalItem;
    private readonly List<CategoryNode> _categories;

    public ClipboardItem? Result { get; private set; }

    public ItemDialog(ClipboardItem? item, List<CategoryNode> categories, List<TagInfo> tags, string? clipboardContent = null)
    {
        InitializeComponent();

        _originalItem = item;
        _categories = categories;
        BuildCategoryChecks();

        if (item != null)
        {
            Title = "编辑片段";
            HeaderTitle.Text = "编辑片段";
            HeaderSubtitle.Text = "修改标题、分类、标签或正文内容";
            TitleBox.Text = item.Title;
            ContentBox.Text = item.Content;
            TagsBox.Text = string.Join(", ", item.Tags);
            SaveBtn.IsEnabled = true;
        }
        else if (clipboardContent != null)
        {
            Title = "保存剪贴板内容";
            HeaderTitle.Text = "保存剪贴板内容";
            HeaderSubtitle.Text = "将刚检测到的文本保存为可复用片段";
            TitleBox.Text = clipboardContent.Length > 25 ? clipboardContent[..22] + "..." : clipboardContent;
            ContentBox.Text = clipboardContent;
            SaveBtn.IsEnabled = true;
        }
        else
        {
            HeaderTitle.Text = "新建片段";
            HeaderSubtitle.Text = "保存常用提示词、话术或文本模板";
        }

        Loaded += (_, _) => TitleBox.Focus();
    }

    private void BuildCategoryChecks()
    {
        CategoriesPanel.Children.Clear();
        foreach (var category in _categories.OrderBy(c => c.Path))
        {
            var cb = new CheckBox
            {
                Content = category.Path,
                Tag = category.Id,
                Margin = new Thickness(0, 0, 12, 8),
                IsChecked = _originalItem?.CategoryIds.Contains(category.Id) == true
            };
            CategoriesPanel.Children.Add(cb);
        }
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text)
                         && !string.IsNullOrWhiteSpace(ContentBox.Text);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var categoryIds = CategoriesPanel.Children.OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true && cb.Tag is int)
            .Select(cb => (int)cb.Tag)
            .ToList();
        var paths = _categories.Where(c => categoryIds.Contains(c.Id)).Select(c => c.Path).ToList();
        var tags = TagsBox.Text.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Result = new ClipboardItem
        {
            Id = _originalItem?.Id ?? 0,
            Title = TitleBox.Text.Trim(),
            Content = ContentBox.Text.Trim(),
            ItemType = paths.FirstOrDefault() ?? _originalItem?.ItemType ?? "未分类",
            CategoryIds = categoryIds,
            CategoryPaths = paths,
            Tags = tags,
            UsageCount = _originalItem?.UsageCount ?? 0,
            LastUsed = _originalItem?.LastUsed,
            CreatedAt = _originalItem?.CreatedAt ?? DateTime.Now,
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
