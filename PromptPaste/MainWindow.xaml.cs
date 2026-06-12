using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Windows.Controls;
using PromptPaste.Database;
using PromptPaste.Models;
using PromptPaste.Services;
using PromptPaste.Views.Dialogs;

namespace PromptPaste;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppSettingsService _settingsService = new();
    private readonly ClipboardWatcher _watcher;
    private readonly HotKeyService _hotkey;
    private AppSettings _settings = new();
    private DatabaseService _db = null!;
    private nint _targetWindowHandle;
    private string _searchText = "";
    private string _tagSearchText = "";

    public ObservableCollection<ClipboardItem> Items { get; } = new();
    public ObservableCollection<CategoryNode> CategoryNodes { get; } = new();
    public ObservableCollection<TagInfo> Tags { get; } = new();
    public ObservableCollection<TagInfo> SelectedTags { get; } = new();
    public ObservableCollection<TagInfo> TagSuggestions { get; } = new();

    private CategoryNode? _selectedCategory;
    private CategoryNode? _contextCategory;
    private CategoryNode? _dragCategory;
    private ClipboardItem? _dragItem;

    private int _itemCount;
    public int ItemCount
    {
        get => _itemCount;
        set { _itemCount = value; OnPropertyChanged(nameof(ItemCount)); }
    }

    private string? _pendingClipboardText;
    public string? PendingClipboardText
    {
        get => _pendingClipboardText;
        private set
        {
            _pendingClipboardText = value;
            OnPropertyChanged(nameof(PendingClipboardText));
            OnPropertyChanged(nameof(PendingClipboardPreview));
            OnPropertyChanged(nameof(HasPendingClipboard));
        }
    }

    public bool HasPendingClipboard => !string.IsNullOrWhiteSpace(PendingClipboardText);

    public string PendingClipboardPreview
        => string.IsNullOrWhiteSpace(PendingClipboardText)
            ? string.Empty
            : TextProcessor.Truncate(PendingClipboardText.Replace("\r", " ").Replace("\n", " "), 90);

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CloseToTray => _settings.CloseToTray;
    public bool StartMinimizedToTray => _settings.StartMinimizedToTray;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new(propertyName));

    public MainWindow()
    {
        _settings = _settingsService.Load();
        _db = CreateStartupDatabase();

        InitializeComponent();
        DataContext = this;
        Items.CollectionChanged += (_, _) => ItemCount = Items.Count;

        _watcher = new ClipboardWatcher(OnClipboardChanged);
        _hotkey = new HotKeyService(this, ToggleVisibility);

        Loaded += (_, _) =>
        {
            _watcher.Start(this);
            _hotkey.Register(_settings.HotKey, _settings.EnableGlobalHotKey);
        };

        _settingsService.RememberDatabase(_settings, _db.DatabasePath);
        LoadData();
        RefreshRecentDatabasesMenu();
    }

    private DatabaseService CreateStartupDatabase()
    {
        var path = string.IsNullOrWhiteSpace(_settings.CurrentDatabasePath)
            ? DatabaseService.DefaultDatabasePath
            : _settings.CurrentDatabasePath;

        try
        {
            return new DatabaseService(path!, IsDefaultDatabasePath(path));
        }
        catch
        {
            return new DatabaseService(DatabaseService.DefaultDatabasePath, seedSampleData: true);
        }
    }

    private static bool IsDefaultDatabasePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        return string.Equals(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)),
            Path.GetFullPath(DatabaseService.DefaultDatabasePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private void LoadData()
    {
        CategoryNodes.Clear();
        foreach (var node in _db.GetCategoryTree())
            CategoryNodes.Add(node);

        Tags.Clear();
        foreach (var tag in _db.GetTags())
            Tags.Add(tag);

        ReconcileSelectedTags();
        RefreshTagSuggestions();
        ApplyFilter();
    }

    private List<CategoryNode> FlattenCategories()
    {
        var result = new List<CategoryNode>();
        void Walk(IEnumerable<CategoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                result.Add(node);
                Walk(node.Children);
            }
        }
        Walk(CategoryNodes);
        return result;
    }

    private void ApplyFilter()
    {
        var source = string.IsNullOrWhiteSpace(_searchText)
            ? _db.GetAllItems()
            : _db.SearchItems(_searchText);

        if (_selectedCategory != null)
        {
            var ids = new HashSet<int> { _selectedCategory.Id };
            if (IncludeChildrenMenuItem.IsChecked)
                foreach (var id in _db.GetDescendantCategoryIds(_selectedCategory.Id)) ids.Add(id);
            source = source.Where(i => i.CategoryIds.Any(ids.Contains)).ToList();
        }

        var selectedTags = SelectedTags.Select(t => t.Name).ToList();
        if (selectedTags.Count > 0)
        {
            source = source.Where(i => selectedTags.All(tag => i.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))).ToList();
        }

        Items.Clear();
        foreach (var item in source)
            Items.Add(item);
    }

    private void CopyToClipboard(string text)
    {
        ClipboardWatcher.MarkInternalCopy(text);
        Clipboard.SetText(text);
    }

    private void ToggleVisibility()
    {
        // Activate if: minimized, hidden, or visible but not the active window
        if (WindowState == WindowState.Minimized || !IsVisible || !IsActive)
        {
            _targetWindowHandle = PasteService.GetForegroundWindow();
            VirtualDesktopService.MoveWindowToCurrentDesktop(this);
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
            Keyboard.ClearFocus();
            Topmost = true;
            Topmost = false; // reset so it doesn't stay always-on-top
        }
        else
        {
            // Fully visible and active → hide to tray
            Hide();
        }
    }

    // ── Clipboard watcher callback ──

    private void OnClipboardChanged(string text)
    {
        if (!_settings.EnableClipboardWatcher) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_settings.EnableClipboardWatcher) return;
            if (!string.IsNullOrWhiteSpace(text) && text != PendingClipboardText)
                PendingClipboardText = text;
        });
    }

    // ── Search ──

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text.Trim();
        ApplyFilter();
    }

    // ── Add ──

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowItemDialog(null);
    }

    // ── Card interactions ──

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is ClipboardItem item) PasteItem(item);
    }

    private void Card_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var item = fe.DataContext as ClipboardItem;
        if (item == null) return;

        var menu = new ContextMenu();
        AddMenuItem(menu, "粘贴到当前窗口", (_, _) => PasteItem(item));
        AddMenuItem(menu, "仅复制", (_, _) => CopyOnly(item));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "编辑", (_, _) => { _ = ShowItemDialog(item.Clone()); });
        AddMenuItem(menu, "删除", (_, _) => DeleteItem(item));
        menu.IsOpen = true;
        menu.PlacementTarget = fe;
        e.Handled = true;
    }

    private void CopyCard_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardItem(sender) is ClipboardItem item)
            CopyOnly(item);
        e.Handled = true;
    }

    private void EditCard_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardItem(sender) is ClipboardItem item)
            _ = ShowItemDialog(item.Clone());
        e.Handled = true;
    }

    private void DeleteCard_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardItem(sender) is ClipboardItem item)
            DeleteItem(item);
        e.Handled = true;
    }

    private static ClipboardItem? GetCardItem(object sender)
    {
        if (sender is not FrameworkElement fe) return null;
        return fe.Tag as ClipboardItem ?? fe.DataContext as ClipboardItem;
    }

    // ── Item operations ──

    /// <summary>Resolve variables (may show dialog), then paste to target window.</summary>
    private void PasteItem(ClipboardItem item)
    {
        var content = ResolveContent(item);
        if (content == null) return;

        // Use hotkey-captured window, or fall back to current foreground
        var hwnd = _targetWindowHandle != nint.Zero ? _targetWindowHandle : PasteService.GetForegroundWindow();
        PasteService.PasteToForeWindow(hwnd, content);
        _db.IncrementUsageCount(item.Id);
        ApplyFilter();
    }

    private void CopyOnly(ClipboardItem item)
    {
        var content = ResolveContent(item);
        if (content == null) return;

        CopyToClipboard(content);
        _db.IncrementUsageCount(item.Id);
        ApplyFilter();
    }

    private string? ResolveContent(ClipboardItem item)
    {
        var vars = TextProcessor.ExtractVariables(item.Content);
        if (vars.Count == 0) return item.Content;

        if (vars.Count == 1 && !string.IsNullOrWhiteSpace(VarBox.Text))
        {
            var d = new Dictionary<string, string> { { vars[0], VarBox.Text.Trim() } };
            return TextProcessor.ReplaceVariables(item.Content, d);
        }

        // Multiple variables or empty box → show dialog
        var dialog = new VariableDialog(vars) { Owner = this };
        return dialog.ShowDialog() == true && dialog.Values != null
            ? TextProcessor.ReplaceVariables(item.Content, dialog.Values)
            : null;
    }

    private void DeleteItem(ClipboardItem item)
    {
        if (MessageBox.Show($"确定删除「{item.Title}」吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _db.DeleteItem(item.Id);
        Items.Remove(item);
    }

    // ── Dialogs ──

    private bool ShowItemDialog(ClipboardItem? item, string? clipboardContent = null)
    {
        var dialog = new ItemDialog(item, _db.GetAllCategoriesFlat(), _db.GetTags(), clipboardContent) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return false;

        if (item == null)
            _db.AddItem(dialog.Result);
        else
            _db.UpdateItem(dialog.Result);
        LoadData();
        return true;
    }

    private void SavePendingClipboard_Click(object sender, RoutedEventArgs e)
    {
        var text = PendingClipboardText;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (ShowItemDialog(null, text))
            PendingClipboardText = null;
    }

    private void DismissPendingClipboard_Click(object sender, RoutedEventArgs e)
    {
        PendingClipboardText = null;
    }

    // ── Category / Tag filters ──

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedCategory = e.NewValue as CategoryNode;
        ApplyFilter();
    }

    private void AllCategories_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategory = null;
        ClearTreeViewSelection(CategoryTree);
        ApplyFilter();
        e.Handled = true;
    }

    private void IncludeChildrenMenuItem_Click(object sender, RoutedEventArgs e) => ApplyFilter();

    private void TagSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _tagSearchText = TagSearchBox.Text.Trim();
        RefreshTagSuggestions();
    }

    private void TagSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || TagSuggestions.Count == 0) return;
        AddSelectedTag(TagSuggestions[0]);
        e.Handled = true;
    }

    private void TagSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagInfo tag })
            AddSelectedTag(tag);
    }

    private void RemoveTagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagInfo tag })
        {
            SelectedTags.Remove(tag);
            RefreshTagSuggestions();
            ApplyFilter();
        }
    }

    private void AddSelectedTag(TagInfo tag)
    {
        if (SelectedTags.Any(t => string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)))
            return;

        SelectedTags.Add(tag);
        TagSearchBox.Clear();
        _tagSearchText = "";
        RefreshTagSuggestions();
        ApplyFilter();
    }

    private void RefreshTagSuggestions()
    {
        TagSuggestions.Clear();
        if (string.IsNullOrWhiteSpace(_tagSearchText)) return;

        var selected = SelectedTags
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in Tags
                     .Where(t => !selected.Contains(t.Name))
                     .Where(t => t.Name.Contains(_tagSearchText, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(t => t.Name)
                     .Take(8))
        {
            TagSuggestions.Add(tag);
        }
    }

    private void ReconcileSelectedTags()
    {
        if (SelectedTags.Count == 0) return;

        var availableTags = Tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var selectedNames = SelectedTags
            .Select(t => t.Name)
            .Where(availableTags.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SelectedTags.Clear();
        foreach (var name in selectedNames)
            SelectedTags.Add(availableTags[name]);
    }

    private void CategoryTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource);
        if (e.LeftButton == MouseButtonState.Pressed && item?.DataContext is CategoryNode node)
        {
            _dragCategory = node;
            DragDrop.DoDragDrop(item, node, DragDropEffects.Move);
        }
    }

    private void CategoryTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource);
        if (item?.DataContext is not CategoryNode category) return;

        _contextCategory = category;
        item.IsSelected = true;

        var menu = new ContextMenu();
        AddMenuItem(menu, "新增子分类", AddSubCategory_Click);
        AddMenuItem(menu, "重命名", RenameCategory_Click);
        AddMenuItem(menu, "删除分类", DeleteCategory_Click);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CategoryTree_Drop(object sender, DragEventArgs e)
    {
        var treeItem = FindAncestor<TreeViewItem>(e.OriginalSource);
        if (treeItem?.DataContext is not CategoryNode target) return;

        if (e.Data.GetDataPresent(typeof(CategoryNode)) && e.Data.GetData(typeof(CategoryNode)) is CategoryNode source)
        {
            _db.MoveCategory(source.Id, target.Id);
            LoadData();
            return;
        }

        if (e.Data.GetDataPresent(typeof(ClipboardItem)) && e.Data.GetData(typeof(ClipboardItem)) is ClipboardItem item)
        {
            _db.AddItemToCategory(item.Id, target.Id);
            LoadData();
        }
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource) != null)
            return;

        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement { DataContext: ClipboardItem item })
        {
            _dragItem = item;
            DragDrop.DoDragDrop((DependencyObject)sender, item, DragDropEffects.Copy);
        }
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var parent = _selectedCategory;
        var title = parent == null
            ? "新增根分类"
            : $"在「{parent.Name}」下新增子分类";

        var name = PromptText(title, "分类名称：");
        if (string.IsNullOrWhiteSpace(name)) return;

        _db.CreateCategory(name, parent?.Id);
        LoadData();
    }

    private void AddSubCategory_Click(object sender, RoutedEventArgs e)
    {
        var parent = _contextCategory;
        var name = PromptText("新增子分类", "分类名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        _db.CreateCategory(name, parent?.Id);
        LoadData();
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        var category = _contextCategory;
        if (category == null) return;
        var name = PromptText("重命名分类", "新名称：", category.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _db.RenameCategory(category.Id, name);
        LoadData();
    }

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        var category = _contextCategory;
        if (category == null) return;
        if (MessageBox.Show($"删除「{category.Name}」及其子分类？片段不会被删除。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _db.DeleteCategoryCascade(category.Id);
        _selectedCategory = null;
        LoadData();
    }

    private static void ClearTreeViewSelection(TreeView tree)
    {
        foreach (var item in tree.Items)
        {
            if (ClearTreeViewItemSelection(tree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem))
                return;
        }
    }

    private static bool ClearTreeViewItemSelection(TreeViewItem? treeItem)
    {
        if (treeItem == null) return false;

        if (treeItem.IsSelected)
        {
            treeItem.IsSelected = false;
            return true;
        }

        foreach (var child in treeItem.Items)
        {
            if (ClearTreeViewItemSelection(treeItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem))
                return true;
        }

        return false;
    }

    private static T? FindAncestor<T>(object? source) where T : DependencyObject
    {
        var current = source as DependencyObject;
        while (current != null)
        {
            if (current is T match) return match;
            current = GetParentObject(current);
        }
        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is FrameworkElement fe && fe.Parent != null)
            return fe.Parent;

        if (current is FrameworkContentElement fce && fce.Parent != null)
            return fce.Parent;

        try
        {
            return VisualTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(current);
        }
    }

    private string? PromptText(string title, string label, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Owner = this,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 12) };
        var ok = new Button { Content = "确定", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, IsCancel = true };
        ok.Click += (_, _) => win.DialogResult = true;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } });
        win.Content = panel;
        return win.ShowDialog() == true ? box.Text.Trim() : null;
    }

    // ── Menu ──

    private void OpenDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "打开或创建数据库",
            Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            DefaultExt = ".db",
            CheckFileExists = false
        };
        if (dlg.ShowDialog() != true) return;

        SwitchDatabase(dlg.FileName);
    }

    private void SwitchDatabase(string dbPath)
    {
        DatabaseService? next = null;
        try
        {
            next = new DatabaseService(dbPath, seedSampleData: false);
            var old = _db;
            _db = next;
            old.Dispose();

            _settingsService.RememberDatabase(_settings, _db.DatabasePath);
            LoadData();
            RefreshRecentDatabasesMenu();
        }
        catch (Exception ex)
        {
            next?.Dispose();
            MessageBox.Show($"打开数据库失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshRecentDatabasesMenu()
    {
        RecentDatabasesMenu.Items.Clear();
        var paths = _settings.RecentDatabasePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            RecentDatabasesMenu.Items.Add(new MenuItem { Header = "暂无最近数据库", IsEnabled = false });
            return;
        }

        foreach (var path in paths)
        {
            var isCurrent = string.Equals(path, _db.DatabasePath, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = isCurrent ? $"✓ {path}" : path,
                ToolTip = path,
                IsEnabled = !isCurrent
            };
            var capturedPath = path;
            item.Click += (_, _) => SwitchDatabase(capturedPath);
            RecentDatabasesMenu.Items.Add(item);
        }
    }

    private void JsonFormat_Click(object sender, RoutedEventArgs e)
    {
        new JsonFormatDialog { Owner = this }.ShowDialog();
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OptionsDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        ApplySettings(dialog.Result);
    }

    private void ApplySettings(AppSettings nextSettings)
    {
        var oldSettings = _settings.Clone();
        if (!_hotkey.Register(nextSettings.HotKey, nextSettings.EnableGlobalHotKey))
        {
            _hotkey.Register(oldSettings.HotKey, oldSettings.EnableGlobalHotKey);
            MessageBox.Show("热键注册失败，可能已被其他程序占用。已保留原热键设置。", "选项", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        nextSettings.CurrentDatabasePath = _settings.CurrentDatabasePath;
        nextSettings.RecentDatabasePaths = _settings.RecentDatabasePaths.ToList();
        _settings = nextSettings;
        _settingsService.Save(_settings);

        if (!_settings.EnableClipboardWatcher)
            PendingClipboardText = null;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "导入数据", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            var items = JsonSerializer.Deserialize<List<ExportClipboardItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items == null || items.Count == 0) return;
            _db.ImportData(items);
            LoadData();
            MessageBox.Show($"成功导入 {items.Count} 条记录", "导入成功");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "导出数据", DefaultExt = ".json", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var items = _db.ExportData();
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(dlg.FileName, json, Encoding.UTF8);
            MessageBox.Show($"成功导出 {items.Count} 条记录", "导出成功");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ShutdownApp();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("PromptPaste 2.0\n\n剪贴板管理工具\n基于 WPF 构建", "关于 PromptPaste");
    }

    // ── Helpers ──

    private static MenuItem AddMenuItem(ItemsControl parent, string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
        parent.Items.Add(item);
        return item;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey.Dispose();
        _watcher.Dispose();
        _db.Dispose();
        base.OnClosed(e);
    }
}
