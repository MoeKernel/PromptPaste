using System.IO;
using PromptPaste.Database;

namespace PromptPaste.Services;

public static class BackupService
{
    public static string BackupDirectory => Path.Combine(DatabaseService.AppDataDirectory, "backups");

    public static string? BackupDatabase(string databasePath, string reason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
                return null;

            Directory.CreateDirectory(BackupDirectory);
            var safeReason = string.Join("_", reason.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeReason)) safeReason = "backup";

            var name = $"{Path.GetFileNameWithoutExtension(databasePath)}-{DateTime.Now:yyyyMMdd-HHmmss}-{safeReason}.db";
            var target = Path.Combine(BackupDirectory, name);
            File.Copy(databasePath, target, overwrite: false);
            LogService.Info($"Database backup created: {target}");
            return target;
        }
        catch (Exception ex)
        {
            LogService.Error($"Database backup failed: {databasePath}", ex);
            return null;
        }
    }
}
