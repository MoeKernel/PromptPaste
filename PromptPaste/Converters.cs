using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PromptPaste.Models;

namespace PromptPaste;

[ValueConversion(typeof(DateTime?), typeof(string))]
public class DateTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ClipboardItem item)
        {
            if (item.LastUsed is DateTime lastUsed)
                return $"使用 {lastUsed:yyyy-MM-dd HH:mm}";
            return $"创建 {item.CreatedAt:yyyy-MM-dd HH:mm}";
        }

        return value is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : "";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(string), typeof(string))]
public class ContentPreviewConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return "";
        var maxLength = parameter is string text && int.TryParse(text, out var parsed)
            ? parsed
            : 150;
        return Services.TextProcessor.Truncate(s, maxLength);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(int), typeof(Visibility))]
public class CountToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var isEmpty = parameter is string s && s == "empty";
        return (count > 0 ^ isEmpty) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
