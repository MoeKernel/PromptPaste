using System.Windows;
using PromptPaste.Services;

namespace PromptPaste.Tests;

public class TextInputContextServiceTests
{
    [Fact]
    public void ConvertsPhysicalScreenPixelsToDeviceIndependentPixels()
    {
        var point = TextInputContextService.ToDeviceIndependentPoint(new Point(1500, 900), 1.5);

        Assert.Equal(1000, point.X);
        Assert.Equal(600, point.Y);
    }

    [Fact]
    public void PreservesMonitorOriginWhenConvertingMixedDpiCoordinates()
    {
        var point = TextInputContextService.ToDeviceIndependentPoint(
            new Point(2520, 900),
            1.5,
            new Rect(1920, 0, 2560, 1440));

        Assert.Equal(2320, point.X);
        Assert.Equal(600, point.Y);
    }

    [Fact]
    public void PreservesNegativeMonitorOriginWhenConvertingMixedDpiCoordinates()
    {
        var point = TextInputContextService.ToDeviceIndependentPoint(
            new Point(-900, 600),
            1.25,
            new Rect(-1280, 0, 1280, 1024));

        Assert.Equal(-976, point.X);
        Assert.Equal(480, point.Y);
    }

    [Fact]
    public void KeepsCoordinatesWhenDpiScaleIsOne()
    {
        var point = TextInputContextService.ToDeviceIndependentPoint(new Point(800, 500), 1.0);

        Assert.Equal(800, point.X);
        Assert.Equal(500, point.Y);
    }
}
