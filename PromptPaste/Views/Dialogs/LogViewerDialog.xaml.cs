using System.Diagnostics;
using System.IO;
using System.Windows;
using PromptPaste.Services;

namespace PromptPaste.Views.Dialogs;

public partial class LogViewerDialog : Window
{
    private const int MaxLogChars = 120_000;

    public LogViewerDialog()
    {
        InitializeComponent();
        LoadLatestLog();
    }

    private void LoadLatestLog()
    {
        CopiedHint.Visibility = Visibility.Collapsed;
        Directory.CreateDirectory(LogService.LogDirectory);

        var latestLog = Directory.GetFiles(LogService.LogDirectory, "app-*.log")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        if (latestLog == null)
        {
            LogPathBox.Text = LogService.LogDirectory;
            LogBox.Text = "暂无日志文件。请先触发一次操作后刷新。";
            return;
        }

        LogPathBox.Text = latestLog;
        var content = File.ReadAllText(latestLog);
        LogBox.Text = content.Length <= MaxLogChars
            ? content
            : content[^MaxLogChars..];
        LogBox.ScrollToEnd();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadLatestLog();

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText($"日志文件：{LogPathBox.Text}{Environment.NewLine}{Environment.NewLine}{LogBox.Text}");
        CopiedHint.Visibility = Visibility.Visible;
    }

    private void OpenLogDir_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogService.LogDirectory);
        Process.Start(new ProcessStartInfo { FileName = LogService.LogDirectory, UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
