namespace PromptPaste.Models;

public class AppSettings
{
    public string? CurrentDatabasePath { get; set; }
    public List<string> RecentDatabasePaths { get; set; } = new();
    public bool EnableGlobalHotKey { get; set; } = true;
    public string HotKey { get; set; } = "Ctrl+Shift+M";
    public bool EnableClipboardWatcher { get; set; } = true;
    public bool StartMinimizedToTray { get; set; }
    public bool CloseToTray { get; set; } = true;

    public AppSettings Clone()
        => new()
        {
            CurrentDatabasePath = CurrentDatabasePath,
            RecentDatabasePaths = RecentDatabasePaths.ToList(),
            EnableGlobalHotKey = EnableGlobalHotKey,
            HotKey = HotKey,
            EnableClipboardWatcher = EnableClipboardWatcher,
            StartMinimizedToTray = StartMinimizedToTray,
            CloseToTray = CloseToTray
        };
}
