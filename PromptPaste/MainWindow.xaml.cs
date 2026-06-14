using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Controls;
using PromptPaste.Database;
using PromptPaste.Models;
using PromptPaste.Services;
using PromptPaste.Views;
using PromptPaste.Views.Dialogs;

namespace PromptPaste;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppSettingsService _settingsService = new();
    private readonly ClipboardWatcher _watcher;
    private readonly HotKeyService _hotkey;
    private readonly HotKeyService _quickPasteGlobalHotKey;
    private readonly LowLevelHotKeyService _quickPasteKeyboardHook;
    private AppSettings _settings = new();
    private DatabaseService _db = null!;
    private nint _targetWindowHandle;
    private string _searchText = "";
    private string _tagSearchText = "";
    private bool _isTrashView;
    private bool _isUncategorizedView;
    private string _emptyStateText = "暂无内容";
    private string? _toastMessage;

    public ObservableCollection<ClipboardItem> Items { get; } = new();
    public ObservableCollection<CategoryNode> CategoryNodes { get; } = new();
    public ObservableCollection<TagInfo> Tags { get; } = new();
    public ObservableCollection<TagInfo> SelectedTags { get; } = new();
    public ObservableCollection<TagInfo> TagSuggestions { get; } = new();

    private CategoryNode? _selectedCategory;
    private CategoryNode? _contextCategory;
    private CategoryNode? _dragCategory;
    private ClipboardItem? _dragItem;
    private bool _suppressCategorySelectionChanged;
    private Point? _categoryDragStartPoint;

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
    public bool IsTrashView
    {
        get => _isTrashView;
        private set
        {
            if (_isTrashView == value) return;
            _isTrashView = value;
            OnPropertyChanged(nameof(IsTrashView));
            OnPropertyChanged(nameof(PrimaryCardActionText));
            OnPropertyChanged(nameof(SecondaryCardActionText));
        }
    }

    public string PrimaryCardActionText => IsTrashView ? "恢复" : "复制";
    public string SecondaryCardActionText => IsTrashView ? "彻删" : "编辑";
    public string EmptyStateText
    {
        get => _emptyStateText;
        private set { _emptyStateText = value; OnPropertyChanged(nameof(EmptyStateText)); }
    }

    public string? ToastMessage
    {
        get => _toastMessage;
        private set
        {
            _toastMessage = value;
            OnPropertyChanged(nameof(ToastMessage));
            OnPropertyChanged(nameof(HasToastMessage));
        }
    }

    public bool HasToastMessage => !string.IsNullOrWhiteSpace(ToastMessage);

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new(propertyName));

    public MainWindow()
    {
        _settings = _settingsService.Load();
        _db = CreateStartupDatabase();

        InitializeComponent();
        DataContext = this;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Items.CollectionChanged += (_, _) => ItemCount = Items.Count;

        _watcher = new ClipboardWatcher(OnClipboardChanged);
        _hotkey = new HotKeyService(this, ToggleVisibility);
        _quickPasteGlobalHotKey = new HotKeyService(this, ShowQuickPaste);
        _quickPasteKeyboardHook = new LowLevelHotKeyService(ShowQuickPaste);

        Loaded += (_, _) =>
        {
            _watcher.Start(this);
            if (!_hotkey.Register(_settings.HotKey, _settings.EnableGlobalHotKey))
                ShowToast("唤出热键注册失败，请在选项中更换");
            RegisterQuickPasteHotKey(_settings);
        };

        _settingsService.RememberDatabase(_settings, _db.DatabasePath);
        LoadData();
        RefreshRecentDatabasesMenu();
        LogService.Info($"Main window loaded. Database={_db.DatabasePath}");
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
        catch (Exception ex)
        {
            LogService.Error($"Open startup database failed, fallback to default: {path}", ex);
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

    private void LoadData(HashSet<int>? expandedCategoryIds = null, int? selectedCategoryId = null)
    {
        expandedCategoryIds ??= CaptureExpandedCategoryIds();
        var categoryIdToRestore = selectedCategoryId
            ?? (!IsTrashView && !_isUncategorizedView ? _selectedCategory?.Id : null);

        _suppressCategorySelectionChanged = true;
        try
        {
            CategoryNodes.Clear();
            foreach (var node in _db.GetCategoryTree())
                CategoryNodes.Add(node);
        }
        finally
        {
            _suppressCategorySelectionChanged = false;
        }

        Tags.Clear();
        foreach (var tag in _db.GetTags())
            Tags.Add(tag);

        RestoreSelectedCategoryModel(categoryIdToRestore);
        ReconcileSelectedTags();
        RefreshTagSuggestions();
        ApplyFilter();
        RestoreExpandedCategoryIds(expandedCategoryIds);
        RestoreSelectedCategoryVisual(categoryIdToRestore);
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
        var source = IsTrashView
            ? _db.GetTrashItems()
            : _isUncategorizedView && string.IsNullOrWhiteSpace(_searchText)
                ? _db.GetUncategorizedItems()
                : string.IsNullOrWhiteSpace(_searchText)
                    ? _db.GetAllItems()
                    : _db.SearchItems(_searchText);

        if (IsTrashView && !string.IsNullOrWhiteSpace(_searchText))
        {
            source = source.Where(i => i.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                                    || i.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!IsTrashView && _isUncategorizedView && !string.IsNullOrWhiteSpace(_searchText))
        {
            source = source.Where(i => i.CategoryIds.Count == 0).ToList();
        }

        if (!IsTrashView && !_isUncategorizedView && _selectedCategory != null)
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
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (IsTrashView)
            EmptyStateText = "回收站为空";
        else if (_isUncategorizedView)
            EmptyStateText = "未分类暂无片段";
        else if (!string.IsNullOrWhiteSpace(_searchText) || SelectedTags.Count > 0)
            EmptyStateText = "没有匹配的片段";
        else if (_selectedCategory != null)
            EmptyStateText = "当前分类暂无片段";
        else
            EmptyStateText = "暂无片段，点击右上角新建";
    }

    private void ShowToast(string message)
    {
        ToastMessage = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ToastMessage == message) ToastMessage = null;
        };
        timer.Start();
    }

    private void CopyToClipboard(string text)
    {
        ClipboardWatcher.MarkInternalCopy(text);
        Clipboard.SetText(text);
        ShowToast("已复制");
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_settings.EnableQuickPasteHotKey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var hotKeyText = FormatCurrentHotKey(Keyboard.Modifiers, key);
        if (!string.Equals(hotKeyText, _settings.QuickPasteHotKey, StringComparison.OrdinalIgnoreCase)) return;

        e.Handled = true;
        ShowQuickPaste();
    }

    private static string FormatCurrentHotKey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Space ? "Space" : key.ToString());
        return string.Join("+", parts);
    }

    private bool RegisterQuickPasteHotKey(AppSettings settings)
    {
        _quickPasteKeyboardHook.Unregister();
        var globalRegistered = _quickPasteGlobalHotKey.Register(settings.QuickPasteHotKey, settings.EnableQuickPasteHotKey);
        var hookRegistered = !globalRegistered && _quickPasteKeyboardHook.Register(settings.QuickPasteHotKey, settings.EnableQuickPasteHotKey);
        LogService.Info($"Quick paste hotkey register. HotKey={settings.QuickPasteHotKey}, Enabled={settings.EnableQuickPasteHotKey}, RegisterHotKey={globalRegistered}, KeyboardHook={hookRegistered}");

        if (!settings.EnableQuickPasteHotKey) return true;
        if (globalRegistered || hookRegistered) return true;

        ShowToast("快速候选热键注册失败，请在选项中更换");
        return false;
    }

    private void ShowQuickPaste()
    {
        try
        {
            LogService.Info("Show quick paste requested");
            var context = TextInputContextService.TryGetExternalTextInputContext();
            if (context == null)
            {
                LogService.Info("Show quick paste skipped: no external text input context");
                ShowToast("请先聚焦外部文本输入框");
                return;
            }

            LogService.Info($"Show quick paste context. TargetHwnd={context.TargetHwnd}, PopupPoint={context.PopupPoint.X:F0},{context.PopupPoint.Y:F0}, Source={context.Source}, DpiScale={context.DpiScale:F2}");
            var items = _db.GetAllItems();
            if (items.Count == 0)
            {
                LogService.Info("Show quick paste skipped: no items");
                ShowToast("暂无可插入片段");
                return;
            }

            var popup = new QuickPasteWindow(items, context.PopupPoint, (item, owner) => CommitQuickPaste(item, context.TargetHwnd, owner));
            popup.Show();
            popup.Activate();
            LogService.Info($"Show quick paste opened. ItemCount={items.Count}");
        }
        catch (Exception ex)
        {
            LogService.Error("Show quick paste failed", ex);
            ShowToast("快速候选弹窗打开失败，请查看日志");
        }
    }

    private void CommitQuickPaste(ClipboardItem item, nint targetHwnd, Window owner)
    {
        var content = ResolveContent(item, owner);
        if (content == null) return;

        PasteService.PasteToForeWindow(targetHwnd, content);
        _db.IncrementUsageCount(item.Id);
        ApplyFilter();
        ShowToast("已插入片段");
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

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        e.Handled = true;
    }

    // ── Add ──

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowItemDialog(null, defaultCategoryIds: GetDefaultCategoryIdsForNewItem());
    }

    // ── Card interactions ──

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTrashView) return;
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
        if (IsTrashView)
        {
            AddMenuItem(menu, "恢复", (_, _) => RestoreItem(item));
            AddMenuItem(menu, "永久删除", (_, _) => PermanentDeleteItem(item));
        }
        else
        {
            AddMenuItem(menu, "粘贴到当前窗口", (_, _) => PasteItem(item));
            AddMenuItem(menu, "仅复制", (_, _) => CopyOnly(item));
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "编辑", (_, _) => { _ = ShowItemDialog(item.Clone()); });
            if (_selectedCategory != null)
                AddMenuItem(menu, "从当前分类移除", (_, _) => RemoveItemFromCurrentCategory(item));
            AddMenuItem(menu, "移入回收站", (_, _) => MoveItemToTrash(item));
        }
        menu.IsOpen = true;
        menu.PlacementTarget = fe;
        e.Handled = true;
    }

    private void CopyCard_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardItem(sender) is ClipboardItem item)
        {
            if (IsTrashView) RestoreItem(item);
            else CopyOnly(item);
        }
        e.Handled = true;
    }

    private void EditCard_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardItem(sender) is ClipboardItem item)
        {
            if (IsTrashView) PermanentDeleteItem(item);
            else _ = ShowItemDialog(item.Clone());
        }
        e.Handled = true;
    }

    private void DeleteCard_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardItem(sender) is ClipboardItem item)
            MoveItemToTrash(item);
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
        ShowToast("已粘贴");
    }

    private void CopyOnly(ClipboardItem item)
    {
        var content = ResolveContent(item);
        if (content == null) return;

        CopyToClipboard(content);
        _db.IncrementUsageCount(item.Id);
        ApplyFilter();
    }

    private string? ResolveContent(ClipboardItem item) => ResolveContent(item, this, useInlineVariable: true);

    private string? ResolveContent(ClipboardItem item, Window owner) => ResolveContent(item, owner, useInlineVariable: false);

    private string? ResolveContent(ClipboardItem item, Window owner, bool useInlineVariable)
    {
        var vars = TextProcessor.ExtractVariables(item.Content);
        if (vars.Count == 0) return item.Content;

        if (useInlineVariable && vars.Count == 1 && !string.IsNullOrWhiteSpace(VarBox.Text))
        {
            var d = new Dictionary<string, string> { { vars[0], VarBox.Text.Trim() } };
            return TextProcessor.ReplaceVariables(item.Content, d);
        }

        // Multiple variables or empty box → show dialog
        var dialog = new VariableDialog(vars) { Owner = owner };
        return dialog.ShowDialog() == true && dialog.Values != null
            ? TextProcessor.ReplaceVariables(item.Content, dialog.Values)
            : null;
    }

    private void MoveItemToTrash(ClipboardItem item)
    {
        if (MessageBox.Show($"确定将「{item.Title}」移入回收站吗？", "移入回收站",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        BackupService.BackupDatabase(_db.DatabasePath, "before-trash");
        _db.MoveItemToTrash(item.Id);
        Items.Remove(item);
        LoadData();
        ShowToast("已移入回收站");
    }

    private void RemoveItemFromCurrentCategory(ClipboardItem item)
    {
        if (_selectedCategory == null) return;
        if (MessageBox.Show($"确定从「{_selectedCategory.Name}」移除「{item.Title}」吗？条目仍会保留在其他分类中。", "从当前分类移除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _db.RemoveItemFromCategory(item.Id, _selectedCategory.Id);
        Items.Remove(item);
        LoadData();
        ShowToast("已从当前分类移除");
    }

    private void RestoreItem(ClipboardItem item)
    {
        _db.RestoreItemFromTrash(item.Id);
        Items.Remove(item);
        LoadData();
        ShowToast("已恢复");
    }

    private void PermanentDeleteItem(ClipboardItem item)
    {
        if (MessageBox.Show($"永久删除「{item.Title}」？此操作不可恢复。", "永久删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        BackupService.BackupDatabase(_db.DatabasePath, "before-permanent-delete");
        _db.PermanentDeleteItem(item.Id);
        Items.Remove(item);
        LoadData();
        ShowToast("已永久删除");
    }

    private void ClearTrash_Click(object sender, RoutedEventArgs e)
    {
        if (!IsTrashView) return;
        if (MessageBox.Show("确定清空回收站吗？此操作不可恢复。", "清空回收站",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        BackupService.BackupDatabase(_db.DatabasePath, "before-clear-trash");
        _db.ClearTrash();
        LoadData();
        ShowToast("已清空回收站");
    }

    // ── Dialogs ──

    private bool ShowItemDialog(ClipboardItem? item, string? clipboardContent = null, IEnumerable<int>? defaultCategoryIds = null)
    {
        var dialog = new ItemDialog(item, _db.GetAllCategoriesFlat(), _db.GetTags(), clipboardContent, defaultCategoryIds) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return false;

        var wasTrashView = IsTrashView;
        var wasUncategorizedView = _isUncategorizedView;
        var selectedCategoryId = _selectedCategory?.Id;
        var expandedCategoryIds = CaptureExpandedCategoryIds();

        if (item == null)
            _db.AddItem(dialog.Result);
        else
            _db.UpdateItem(dialog.Result);

        IsTrashView = wasTrashView;
        _isUncategorizedView = wasUncategorizedView;
        LoadData(expandedCategoryIds, selectedCategoryId);
        ShowToast(item == null ? "已新建片段" : "已保存片段");
        return true;
    }

    private IEnumerable<int> GetDefaultCategoryIdsForNewItem()
        => !IsTrashView && !_isUncategorizedView && _selectedCategory != null
            ? new[] { _selectedCategory.Id }
            : Array.Empty<int>();

    private void SavePendingClipboard_Click(object sender, RoutedEventArgs e)
    {
        var text = PendingClipboardText;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (ShowItemDialog(null, text, GetDefaultCategoryIdsForNewItem()))
            PendingClipboardText = null;
    }

    private void DismissPendingClipboard_Click(object sender, RoutedEventArgs e)
    {
        PendingClipboardText = null;
    }

    // ── Category / Tag filters ──

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressCategorySelectionChanged) return;

        _selectedCategory = e.NewValue as CategoryNode;
        IsTrashView = false;
        _isUncategorizedView = false;
        ApplyFilter();
    }

    private void AllCategories_Click(object sender, RoutedEventArgs e)
    {
        IsTrashView = false;
        _isUncategorizedView = false;
        _selectedCategory = null;
        ClearTreeViewSelectionSuppressingEvents();
        ApplyFilter();
        e.Handled = true;
    }

    private void Trash_Click(object sender, RoutedEventArgs e)
    {
        IsTrashView = true;
        _isUncategorizedView = false;
        _selectedCategory = null;
        ClearTreeViewSelectionSuppressingEvents();
        ApplyFilter();
        e.Handled = true;
    }

    private void Uncategorized_Click(object sender, RoutedEventArgs e)
    {
        IsTrashView = false;
        _isUncategorizedView = true;
        _selectedCategory = null;
        ClearTreeViewSelectionSuppressingEvents();
        ApplyFilter();
        e.Handled = true;
    }

    private void ClearTreeViewSelectionSuppressingEvents()
    {
        _suppressCategorySelectionChanged = true;
        try
        {
            ClearTreeViewSelection(CategoryTree);
        }
        finally
        {
            _suppressCategorySelectionChanged = false;
        }
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

    private void CategoryTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _categoryDragStartPoint = e.GetPosition(null);
        _dragCategory = null;
    }

    private void CategoryTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _categoryDragStartPoint = null;
            return;
        }

        if (_categoryDragStartPoint is not Point startPoint) return;
        var currentPoint = e.GetPosition(null);
        if (Math.Abs(currentPoint.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = FindAncestor<TreeViewItem>(e.OriginalSource);
        if (item?.DataContext is CategoryNode node)
        {
            _dragCategory = node;
            DragDrop.DoDragDrop(item, node, DragDropEffects.Move);
            _dragCategory = null;
            _categoryDragStartPoint = null;
            e.Handled = true;
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
            if (source.Id == target.Id)
            {
                e.Handled = true;
                return;
            }

            if (_db.GetDescendantCategoryIds(source.Id).Contains(target.Id))
            {
                ShowToast("不能移动到自身或子分类下");
                e.Handled = true;
                return;
            }

            var expanded = CaptureExpandedCategoryIds();
            expanded.Add(target.Id);
            try
            {
                if (MessageBox.Show($"将「{source.Name}」移动到「{target.Name}」下；如果存在同名分类将自动合并。是否继续？", "移动或合并分类",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                _db.MoveOrMergeCategory(source.Id, target.Id);
                LoadData(expanded);
                ShowToast("分类已移动或合并");
            }
            catch (Exception ex)
            {
                LogService.Error("Move or merge category failed", ex);
                MessageBox.Show($"移动或合并分类失败：{ex.Message}", "分类操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        if (e.Data.GetDataPresent(typeof(ClipboardItem)) && e.Data.GetData(typeof(ClipboardItem)) is ClipboardItem item)
        {
            var expanded = CaptureExpandedCategoryIds();
            expanded.Add(target.Id);
            _db.AddItemToCategory(item.Id, target.Id);
            LoadData(expanded);
            ShowToast("已加入分类");
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

        var expanded = CaptureExpandedCategoryIds();
        if (parent != null) expanded.Add(parent.Id);
        _db.CreateCategory(name, parent?.Id);
        LoadData(expanded);
    }

    private void AddSubCategory_Click(object sender, RoutedEventArgs e)
    {
        var parent = _contextCategory;
        var name = PromptText("新增子分类", "分类名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        var expanded = CaptureExpandedCategoryIds();
        if (parent != null) expanded.Add(parent.Id);
        _db.CreateCategory(name, parent?.Id);
        LoadData(expanded);
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        var category = _contextCategory;
        if (category == null) return;
        var name = PromptText("重命名分类", "新名称：", category.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var expanded = CaptureExpandedCategoryIds();
        _db.RenameCategory(category.Id, name);
        LoadData(expanded);
    }

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        var category = _contextCategory;
        if (category == null) return;
        if (MessageBox.Show($"删除「{category.Name}」及其子分类？片段不会被删除。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        BackupService.BackupDatabase(_db.DatabasePath, "before-delete-category");
        var expanded = CaptureExpandedCategoryIds();
        var parentId = category.ParentId;
        _db.DeleteCategoryCascade(category.Id);
        expanded.Remove(category.Id);
        if (parentId != null) expanded.Add(parentId.Value);
        _selectedCategory = null;
        LoadData(expanded);
        ShowToast("分类已删除");
    }

    private HashSet<int> CaptureExpandedCategoryIds()
    {
        var ids = new HashSet<int>();
        if (!IsInitialized) return ids;

        CategoryTree.UpdateLayout();
        foreach (var item in CategoryTree.Items)
            CaptureExpandedCategoryIds(CategoryTree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem, ids);
        return ids;
    }

    private static void CaptureExpandedCategoryIds(TreeViewItem? treeItem, HashSet<int> ids)
    {
        if (treeItem == null) return;

        if (treeItem.IsExpanded && treeItem.DataContext is CategoryNode node)
            ids.Add(node.Id);

        treeItem.UpdateLayout();
        foreach (var child in treeItem.Items)
            CaptureExpandedCategoryIds(treeItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem, ids);
    }

    private void RestoreExpandedCategoryIds(HashSet<int> ids)
    {
        if (ids.Count == 0) return;

        Dispatcher.BeginInvoke(() =>
        {
            CategoryTree.UpdateLayout();
            foreach (var item in CategoryTree.Items)
                RestoreExpandedCategoryIds(CategoryTree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem, ids);
        }, DispatcherPriority.Loaded);
    }

    private static void RestoreExpandedCategoryIds(TreeViewItem? treeItem, HashSet<int> ids)
    {
        if (treeItem == null) return;

        if (treeItem.DataContext is CategoryNode node && ids.Contains(node.Id))
        {
            treeItem.IsExpanded = true;
            treeItem.UpdateLayout();
        }

        foreach (var child in treeItem.Items)
            RestoreExpandedCategoryIds(treeItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem, ids);
    }

    private void RestoreSelectedCategoryModel(int? categoryId)
    {
        if (IsTrashView || _isUncategorizedView)
        {
            _selectedCategory = null;
            return;
        }

        _selectedCategory = categoryId is int id
            ? FindCategoryById(CategoryNodes, id)
            : null;
    }

    private static CategoryNode? FindCategoryById(IEnumerable<CategoryNode> nodes, int id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id) return node;

            var child = FindCategoryById(node.Children, id);
            if (child != null) return child;
        }

        return null;
    }

    private void RestoreSelectedCategoryVisual(int? categoryId)
    {
        if (categoryId == null || IsTrashView || _isUncategorizedView) return;

        Dispatcher.BeginInvoke(() =>
        {
            var treeItem = FindCategoryTreeItem(categoryId.Value);
            if (treeItem == null) return;

            _suppressCategorySelectionChanged = true;
            try
            {
                treeItem.IsSelected = true;
                treeItem.BringIntoView();
            }
            finally
            {
                _suppressCategorySelectionChanged = false;
            }
        }, DispatcherPriority.Loaded);
    }

    private TreeViewItem? FindCategoryTreeItem(int categoryId)
    {
        CategoryTree.UpdateLayout();
        foreach (var item in CategoryTree.Items)
        {
            var treeItem = FindCategoryTreeItem(CategoryTree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem, categoryId);
            if (treeItem != null) return treeItem;
        }

        return null;
    }

    private static TreeViewItem? FindCategoryTreeItem(TreeViewItem? treeItem, int categoryId)
    {
        if (treeItem == null) return null;

        if (treeItem.DataContext is CategoryNode node && node.Id == categoryId)
            return treeItem;

        treeItem.UpdateLayout();
        foreach (var child in treeItem.Items)
        {
            var childItem = FindCategoryTreeItem(treeItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem, categoryId);
            if (childItem != null) return childItem;
        }

        return null;
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
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 12), Focusable = true };
        var ok = new Button { Content = "确定", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, IsCancel = true };
        ok.Click += (_, _) => win.DialogResult = true;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } });
        win.Content = panel;
        win.Loaded += (_, _) =>
        {
            win.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }, DispatcherPriority.Input);
        };
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
            ShowToast("已切换数据库");
            LogService.Info($"Database switched: {_db.DatabasePath}");
        }
        catch (Exception ex)
        {
            next?.Dispose();
            LogService.Error($"Open database failed: {dbPath}", ex);
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

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        new LogViewerDialog { Owner = this }.ShowDialog();
    }

    private void License_Click(object sender, RoutedEventArgs e)
    {
        new LicenseDialog { Owner = this }.ShowDialog();
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OptionsDialog(_settings, _db.DatabasePath) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        ApplySettings(dialog.Result);
    }

    private void ApplySettings(AppSettings nextSettings)
    {
        var oldSettings = _settings.Clone();
        if (!_hotkey.Register(nextSettings.HotKey, nextSettings.EnableGlobalHotKey) ||
            !RegisterQuickPasteHotKey(nextSettings))
        {
            _hotkey.Register(oldSettings.HotKey, oldSettings.EnableGlobalHotKey);
            RegisterQuickPasteHotKey(oldSettings);
            MessageBox.Show("热键注册失败，可能已被其他程序占用。已保留原热键设置。", "选项", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        nextSettings.CurrentDatabasePath = _settings.CurrentDatabasePath;
        nextSettings.RecentDatabasePaths = _settings.RecentDatabasePaths.ToList();
        _settings = nextSettings;
        _settingsService.Save(_settings);

        if (!_settings.EnableClipboardWatcher)
            PendingClipboardText = null;
        ShowToast("选项已保存");
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
            if (MessageBox.Show($"将导入 {items.Count} 条 JSON 记录。导入前会自动备份当前数据库，是否继续？", "导入数据",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BackupService.BackupDatabase(_db.DatabasePath, "before-import");
            var imported = _db.ImportData(items);
            LoadData();
            ShowToast($"已导入 {imported} 条记录");
            MessageBox.Show($"成功导入 {imported} 条记录，跳过 {items.Count - imported} 条无效记录。", "导入成功");
        }
        catch (Exception ex)
        {
            LogService.Error("Import failed", ex);
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
            ShowToast("已导出");
            MessageBox.Show($"成功导出 {items.Count} 条记录", "导出成功");
        }
        catch (Exception ex)
        {
            LogService.Error("Export failed", ex);
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ShutdownApp();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";
        MessageBox.Show(
            $"PromptPaste {version}\n\n剪贴板管理工具\n基于 WPF 构建\n\n数据库：{_db.DatabasePath}\n配置：{_settingsService.SettingsPath}\n数据目录：{DatabaseService.AppDataDirectory}",
            "关于 PromptPaste");
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
        _quickPasteGlobalHotKey.Dispose();
        _quickPasteKeyboardHook.Dispose();
        _watcher.Dispose();
        _db.Dispose();
        base.OnClosed(e);
    }
}
