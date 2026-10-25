using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class PopupPlacementTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void PopupFitsWorkingAreaForEachTaskbarEdge(double scale)
    {
        var areas = new[]
        {
            new PhysicalRect(0, 0, 1920, 1040),
            new PhysicalRect(0, 0, 1880, 1080),
            new PhysicalRect(0, 40, 1920, 1040),
            new PhysicalRect(40, 0, 1880, 1080)
        };
        var dpi = new DpiScale(scale, scale);

        foreach (var area in areas)
        {
            var position = PopupPlacement.PlaceNearCursor(area,
                new PhysicalPoint(area.Right - 2, area.Bottom - 2), dpi, 380, 520);
            var left = position.Left * scale;
            var top = position.Top * scale;
            var right = (position.Left + position.Width) * scale;
            var bottom = (position.Top + position.Height) * scale;

            Assert.InRange(left, area.Left - 0.01, area.Right + 0.01);
            Assert.InRange(top, area.Top - 0.01, area.Bottom + 0.01);
            Assert.True(right <= area.Right + 0.01);
            Assert.True(bottom <= area.Bottom + 0.01);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ScreenOverlayBoundsConvertPhysicalPixelsToDip(double scale)
    {
        var bounds = new PhysicalRect(-1920, 80, 1920, 1040);
        var dip = PopupPlacement.ToDipBounds(bounds, new DpiScale(scale, scale));

        Assert.Equal(bounds.Left, dip.Left * scale, 4);
        Assert.Equal(bounds.Top, dip.Top * scale, 4);
        Assert.Equal(bounds.Width, dip.Width * scale, 4);
        Assert.Equal(bounds.Height, dip.Height * scale, 4);
    }
}
