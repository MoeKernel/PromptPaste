using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PromptPaste.Database;
using PromptPaste.Models;
using PromptPaste.Services;

namespace PromptPaste.Views.Dialogs;

public partial class OptionsDialog : Window
{
    private string _hotKey;
    private string _quickPasteHotKey;
    private readonly string _databasePath;
    private readonly AppSettingsService _settingsService = new();

    public AppSettings Result { get; }

    public OptionsDialog(AppSettings settings, string databasePath)
    {
        InitializeComponent();
        Result = settings.Clone();
        _databasePath = databasePath;
        _hotKey = string.IsNullOrWhiteSpace(Result.HotKey) ? "Ctrl+Shift+M" : Result.HotKey;
        _quickPasteHotKey = string.IsNullOrWhiteSpace(Result.QuickPasteHotKey) ? "Ctrl+Alt+Space" : Result.QuickPasteHotKey;

        EnableHotKeyBox.IsChecked = Result.EnableGlobalHotKey;
        HotKeyBox.Text = _hotKey;
        EnableQuickPasteHotKeyBox.IsChecked = Result.EnableQuickPasteHotKey;
        QuickPasteHotKeyBox.Text = _quickPasteHotKey;
        EnableClipboardBox.IsChecked = Result.EnableClipboardWatcher;
        StartMinimizedBox.IsChecked = Result.StartMinimizedToTray;
        CloseToTrayBox.IsChecked = Result.CloseToTray;
        DatabasePathBox.Text = _databasePath;
        SettingsPathBox.Text = _settingsService.SettingsPath;
    }

    private void HotKeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box) return;
        box.Text = "请按组合键...";
        box.SelectAll();
    }

    private void HotKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var modifiers = Keyboard.Modifiers;
        if (sender is not System.Windows.Controls.TextBox box) return;
        var current = box == QuickPasteHotKeyBox ? _quickPasteHotKey : _hotKey;
        if (modifiers == ModifierKeys.None || key == Key.None)
        {
            box.Text = current;
            return;
        }

        var text = FormatHotKey(modifiers, key);
        if (!HotKeyService.TryParseHotKey(text, out _, out _))
        {
            box.Text = current;
            return;
        }

        if (box == QuickPasteHotKeyBox)
        {
            _quickPasteHotKey = text;
            QuickPasteHotKeyBox.Text = _quickPasteHotKey;
        }
        else
        {
            _hotKey = text;
            HotKeyBox.Text = _hotKey;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (EnableHotKeyBox.IsChecked == true && !HotKeyService.TryParseHotKey(_hotKey, out _, out _))
        {
            MessageBox.Show("唤出热键无效，请按包含 Ctrl / Alt / Shift / Win 的组合键。", "热键设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EnableQuickPasteHotKeyBox.IsChecked == true && !HotKeyService.TryParseHotKey(_quickPasteHotKey, out _, out _))
        {
            MessageBox.Show("快速候选热键无效，请按包含 Ctrl / Alt / Shift / Win 的组合键。", "热键设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EnableHotKeyBox.IsChecked == true && EnableQuickPasteHotKeyBox.IsChecked == true &&
            string.Equals(_hotKey, _quickPasteHotKey, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("唤出热键和快速候选热键不能相同。", "热键设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.EnableGlobalHotKey = EnableHotKeyBox.IsChecked == true;
        Result.HotKey = _hotKey;
        Result.EnableQuickPasteHotKey = EnableQuickPasteHotKeyBox.IsChecked == true;
        Result.QuickPasteHotKey = _quickPasteHotKey;
        Result.EnableClipboardWatcher = EnableClipboardBox.IsChecked == true;
        Result.StartMinimizedToTray = StartMinimizedBox.IsChecked == true;
        Result.CloseToTray = CloseToTrayBox.IsChecked == true;
        DialogResult = true;
    }

    private static string FormatHotKey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Space ? "Space" : key.ToString());
        return string.Join("+", parts);
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
        => OpenDirectory(DatabaseService.AppDataDirectory);

    private void OpenLogDir_Click(object sender, RoutedEventArgs e)
        => OpenDirectory(LogService.LogDirectory);

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        var path = BackupService.BackupDatabase(_databasePath, "manual");
        BackupHint.Text = path == null ? "备份失败，请查看日志。" : $"已备份到：{path}";
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
