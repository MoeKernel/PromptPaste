using System.Text.RegularExpressions;

namespace PromptPaste.Services;

public static class TextProcessor
{
    private static readonly Regex VarPattern = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    public static List<string> ExtractVariables(string text)
        => VarPattern.Matches(text)
                     .Select(m => m.Groups[1].Value.Trim())
                     .Distinct()
                     .OrderBy(v => v)
                     .ToList();

    public static string ReplaceVariables(string text, Dictionary<string, string>? vars)
    {
        if (vars == null || vars.Count == 0) return text;
        return VarPattern.Replace(text, m =>
            vars.TryGetValue(m.Groups[1].Value.Trim(), out var v) ? v : m.Value);
    }

    public static string Truncate(string text, int max = 120, string suffix = "...")
        => text.Length <= max ? text : text[..(max - suffix.Length)] + suffix;
}
