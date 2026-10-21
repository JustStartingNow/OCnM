using Slotty.Core.Geometry;

namespace Slotty.Tests;

public class ScreenMathTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Centers_on_the_cursor_when_there_is_room()
    {
        Assert.Equal(new ScreenPoint(750, 300), ScreenMath.CenterClamped(new(960, 510), 420, 420, Work));
    }

    [Theory]
    [InlineData(5, 5, 0, 0)]
    [InlineData(1915, 1035, 1500, 620)]
    [InlineData(1915, 5, 1500, 0)]
    public void Clamps_to_the_work_area(int cx, int cy, int ex, int ey)
    {
        Assert.Equal(new ScreenPoint(ex, ey), ScreenMath.CenterClamped(new(cx, cy), 420, 420, Work));
    }

    [Fact]
    public void Works_on_a_secondary_monitor_with_negative_coordinates()
    {
        var left = new ScreenRect(-1280, 0, 1280, 1024);
        Assert.Equal(new ScreenPoint(-1280, 604), ScreenMath.CenterClamped(new(-1270, 1020), 420, 420, left));
    }
}
