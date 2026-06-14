using System.Windows;
using System.Windows.Controls;
using PromptPaste.Models;

namespace PromptPaste.Views.Dialogs;

public partial class ItemDialog : Window
{
    private readonly ClipboardItem? _originalItem;
    private readonly List<CategoryNode> _categories;
    private readonly Dictionary<int, CategoryNode> _categoryById;
    private readonly List<CategoryNode> _rootCategories;
    private readonly Dictionary<int, List<CategoryNode>> _childrenByParent;
    private readonly HashSet<int> _selectedCategoryIds;

    public ClipboardItem? Result { get; private set; }

    public ItemDialog(
        ClipboardItem? item,
        List<CategoryNode> categories,
        List<TagInfo> tags,
        string? clipboardContent = null,
        IEnumerable<int>? defaultCategoryIds = null)
    {
        InitializeComponent();

        _originalItem = item;
        _categories = categories;
        _categoryById = _categories.ToDictionary(c => c.Id);
        _rootCategories = _categories
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
        _childrenByParent = _categories
            .Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId)
            .ToDictionary(g => g.Key!.Value, g => g.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList());
        _selectedCategoryIds = new HashSet<int>(
            item?.CategoryIds.Count > 0 == true
                ? item.CategoryIds
                : defaultCategoryIds ?? Enumerable.Empty<int>());
        BuildCategoryTree();
        RefreshSelectedCategories();

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

    private void BuildCategoryTree()
    {
        CategoryTree.Items.Clear();
        var keyword = CategorySearchBox.Text.Trim();
        foreach (var root in _rootCategories)
        {
            if (CategoryMatchesOrHasMatchingChild(root, keyword))
                CategoryTree.Items.Add(CreateCategoryTreeItem(root, keyword));
        }
    }

    private TreeViewItem CreateCategoryTreeItem(CategoryNode category, string keyword)
    {
        var item = new TreeViewItem
        {
            IsExpanded = !string.IsNullOrWhiteSpace(keyword) || _selectedCategoryIds.Contains(category.Id)
        };
        var checkBox = new CheckBox
        {
            Content = category.Name,
            ToolTip = category.Path,
            Tag = category.Id,
            IsChecked = _selectedCategoryIds.Contains(category.Id),
            Margin = new Thickness(0, 2, 0, 2)
        };
        checkBox.Checked += CategoryCheckBox_Changed;
        checkBox.Unchecked += CategoryCheckBox_Changed;
        item.Header = checkBox;

        foreach (var child in GetChildren(category.Id))
        {
            if (CategoryMatchesOrHasMatchingChild(child, keyword))
                item.Items.Add(CreateCategoryTreeItem(child, keyword));
        }
        return item;
    }

    private IEnumerable<CategoryNode> GetChildren(int parentId)
        => _childrenByParent.TryGetValue(parentId, out var children)
            ? children
            : Enumerable.Empty<CategoryNode>();

    private bool CategoryMatchesOrHasMatchingChild(CategoryNode category, string keyword)
        => CategoryMatches(category, keyword)
        || GetChildren(category.Id).Any(child => CategoryMatchesOrHasMatchingChild(child, keyword));

    private static bool CategoryMatches(CategoryNode category, string keyword)
        => string.IsNullOrWhiteSpace(keyword)
        || category.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || category.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private void CategoryCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: int categoryId } checkBox) return;

        if (checkBox.IsChecked == true)
            _selectedCategoryIds.Add(categoryId);
        else
            _selectedCategoryIds.Remove(categoryId);

        RefreshSelectedCategories();
    }

    private void RefreshSelectedCategories()
    {
        SelectedCategoriesPanel.ItemsSource = _selectedCategoryIds
            .Where(_categoryById.ContainsKey)
            .Select(id => _categoryById[id])
            .OrderBy(c => c.Path)
            .ToList();
    }

    private void CategorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        BuildCategoryTree();
    }

    private void RemoveSelectedCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int categoryId })
        {
            _selectedCategoryIds.Remove(categoryId);
            BuildCategoryTree();
            RefreshSelectedCategories();
        }
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text)
                         && !string.IsNullOrWhiteSpace(ContentBox.Text);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var categoryIds = _selectedCategoryIds
            .Where(_categoryById.ContainsKey)
            .ToList();
        var paths = categoryIds.Select(id => _categoryById[id].Path).ToList();
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
