using System.IO;
using System.Text.Json;
using PromptPaste.Database;
using PromptPaste.Models;

namespace PromptPaste.Services;

public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SettingsPath { get; } = Path.Combine(DatabaseService.AppDataDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DatabaseService.AppDataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public void RememberDatabase(AppSettings settings, string dbPath)
    {
        var fullPath = NormalizePath(dbPath);
        settings.CurrentDatabasePath = fullPath;
        settings.RecentDatabasePaths = settings.RecentDatabasePaths
            .Select(NormalizePathOrNull)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => !string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(fullPath)
            .Take(10)
            .ToList()!;
        Save(settings);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    private static string? NormalizePathOrNull(string path)
    {
        try
        {
            return NormalizePath(path);
        }
        catch
        {
            return null;
        }
    }
}
