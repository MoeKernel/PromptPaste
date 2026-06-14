using System.Windows;
using PromptPaste.Views;

namespace PromptPaste.Tests;

public class QuickPasteWindowPlacementTests
{
    private static readonly Size PopupSize = new(420, 360);
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void PlacesPopupBelowAndRightOfAnchorWhenThereIsRoom()
    {
        var location = QuickPasteWindowPlacement.CalculatePopupLocation(new Point(300, 200), PopupSize, Screen);

        Assert.Equal(302, location.X);
        Assert.Equal(202, location.Y);
    }

    [Fact]
    public void ShiftsLeftWhenAnchorIsNearRightEdge()
    {
        var location = QuickPasteWindowPlacement.CalculatePopupLocation(new Point(1850, 200), PopupSize, Screen);

        Assert.Equal(1500, location.X);
        Assert.Equal(202, location.Y);
    }

    [Fact]
    public void PlacesPopupAboveAnchorWhenAnchorIsNearBottomEdge()
    {
        var location = QuickPasteWindowPlacement.CalculatePopupLocation(new Point(300, 1040), PopupSize, Screen);

        Assert.Equal(302, location.X);
        Assert.Equal(678, location.Y);
    }

    [Fact]
    public void KeepsPopupInsideScreenNearBottomRightCorner()
    {
        var location = QuickPasteWindowPlacement.CalculatePopupLocation(new Point(1910, 1070), PopupSize, Screen);

        Assert.Equal(1500, location.X);
        Assert.Equal(708, location.Y);
    }
}
