using System.IO;

namespace PromptPaste.Services;

public static class LogService
{
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".promptpaste",
        "logs");

    private static string LogPath => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
    private static readonly object LockObj = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
            if (exception != null) line += Environment.NewLine + exception;

            lock (LockObj)
                File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
