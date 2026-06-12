using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PromptPaste.Services;

/// <summary>
/// Loads application icons from resources/icons/app_icon.svg.
/// Windows tray and executable icons use the sibling .ico file because Windows APIs do not accept SVG directly.
/// </summary>
public static class IconHelper
{
    private const string SvgResourcePath = "resources/icons/app_icon.svg";
    private const string IcoResourcePath = "resources/icons/app_icon.ico";

    public static Icon CreateTrayIcon()
    {
        var icoPath = FindIconFile(IcoResourcePath);
        if (icoPath != null)
            return new Icon(icoPath);

        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/{IcoResourcePath}"))?.Stream;
        if (stream != null)
            return new Icon(stream);

        return SystemIcons.Application;
    }

    public static ImageSource CreateWindowIcon(int size = 32)
    {
        var svg = LoadSvgText();
        if (svg == null)
            return Imaging.CreateBitmapSourceFromHIcon(SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));

        var viewBox = ParseViewBox(svg);
        var pathData = ParseFirstPathData(svg);
        if (viewBox == null || string.IsNullOrWhiteSpace(pathData))
            return Imaging.CreateBitmapSourceFromHIcon(SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));

        var geometry = Geometry.Parse(pathData);
        geometry.Freeze();

        var scale = Math.Min(size / viewBox.Value.Width, size / viewBox.Value.Height);
        var offsetX = (size - viewBox.Value.Width * scale) / 2 - viewBox.Value.X * scale;
        var offsetY = (size - viewBox.Value.Height * scale) / 2 - viewBox.Value.Y * scale;

        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Brush = new SolidColorBrush(Colors.Black)
        };
        drawing.Freeze();

        var group = new DrawingGroup
        {
            Transform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY)
        };
        group.Children.Add(drawing);
        group.Freeze();

        var renderTarget = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
            ctx.DrawDrawing(group);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    private static string? LoadSvgText()
    {
        var filePath = FindIconFile(SvgResourcePath);
        if (filePath != null)
            return File.ReadAllText(filePath);

        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/{SvgResourcePath}"))?.Stream;
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? FindIconFile(string relativePath)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, relativePath),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", relativePath)),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", relativePath)),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static Rect? ParseViewBox(string svg)
    {
        var match = Regex.Match(svg, "viewBox=\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var parts = match.Groups["value"].Value
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : double.NaN)
            .ToArray();

        return parts.Length == 4 && parts.All(v => !double.IsNaN(v))
            ? new Rect(parts[0], parts[1], parts[2], parts[3])
            : null;
    }

    private static string? ParseFirstPathData(string svg)
    {
        var match = Regex.Match(svg, "<path[^>]*\\sd=\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }
}
