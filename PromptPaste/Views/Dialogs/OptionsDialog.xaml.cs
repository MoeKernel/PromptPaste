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
    private readonly string _databasePath;
    private readonly AppSettingsService _settingsService = new();

    public AppSettings Result { get; }

    public OptionsDialog(AppSettings settings, string databasePath)
    {
        InitializeComponent();
        Result = settings.Clone();
        _databasePath = databasePath;
        _hotKey = string.IsNullOrWhiteSpace(Result.HotKey) ? "Ctrl+Shift+M" : Result.HotKey;

        EnableHotKeyBox.IsChecked = Result.EnableGlobalHotKey;
        HotKeyBox.Text = _hotKey;
        EnableClipboardBox.IsChecked = Result.EnableClipboardWatcher;
        StartMinimizedBox.IsChecked = Result.StartMinimizedToTray;
        CloseToTrayBox.IsChecked = Result.CloseToTray;
        DatabasePathBox.Text = _databasePath;
        SettingsPathBox.Text = _settingsService.SettingsPath;
    }

    private void HotKeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotKeyBox.Text = "请按组合键...";
        HotKeyBox.SelectAll();
    }

    private void HotKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None || key == Key.None)
        {
            HotKeyBox.Text = _hotKey;
            return;
        }

        var text = FormatHotKey(modifiers, key);
        if (!HotKeyService.TryParseHotKey(text, out _, out _))
        {
            HotKeyBox.Text = _hotKey;
            return;
        }

        _hotKey = text;
        HotKeyBox.Text = _hotKey;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (EnableHotKeyBox.IsChecked == true && !HotKeyService.TryParseHotKey(_hotKey, out _, out _))
        {
            MessageBox.Show("热键无效，请按包含 Ctrl / Alt / Shift / Win 的组合键。", "热键设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.EnableGlobalHotKey = EnableHotKeyBox.IsChecked == true;
        Result.HotKey = _hotKey;
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
