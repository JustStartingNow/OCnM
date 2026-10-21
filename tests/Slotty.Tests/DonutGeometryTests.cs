using Slotty.Core.Geometry;

namespace Slotty.Tests;

public class DonutGeometryTests
{
    private readonly DonutGeometry _g = new(outerRadius: 200, innerRadius: 80, hubRadius: 60);

    private DonutHit HitAt(double angle, double radius)
    {
        var (x, y) = DonutGeometry.PointAt(angle, radius);
        return _g.HitTest(x, y);
    }

    [Theory]
    [InlineData(0, -1, 0)]      // 12 o'clock
    [InlineData(1, 0, 90)]      // 3 o'clock
    [InlineData(0, 1, 180)]     // 6 o'clock
    [InlineData(-1, 0, 270)]    // 9 o'clock
    [InlineData(1, -1, 45)]
    [InlineData(-1, -1, 315)]
    public void AngleOf_is_clockwise_from_twelve_oclock(double dx, double dy, double expected)
    {
        Assert.Equal(expected, DonutGeometry.AngleOf(dx, dy), 9);
    }

    [Fact]
    public void Slice_one_starts_at_twelve_and_slices_go_clockwise()
    {
        Assert.Equal(0, HitAt(1, 150).SliceIndex);
        Assert.Equal(0, HitAt(20, 150).SliceIndex);
        Assert.Equal(1, HitAt(60, 150).SliceIndex);
        Assert.Equal(2, HitAt(100, 150).SliceIndex);
        Assert.Equal(4, HitAt(180, 150).SliceIndex);
        Assert.Equal(8, HitAt(359, 150).SliceIndex);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(39.999, 0)]
    [InlineData(40.0, 1)]
    [InlineData(40.001, 1)]
    [InlineData(80.0, 2)]
    [InlineData(319.999, 7)]
    [InlineData(320.0, 8)]
    [InlineData(359.9999, 8)]
    [InlineData(360.0, 0)]
    [InlineData(720.5, 0)]
    [InlineData(-0.0001, 8)]
    [InlineData(-40.0, 8)]
    [InlineData(-40.0001, 7)]
    public void SliceAtAngle_handles_boundaries_and_wraparound(double angle, int expected)
    {
        Assert.Equal(expected, _g.SliceAtAngle(angle));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(120)]
    [InlineData(160)]
    [InlineData(200)]
    [InlineData(240)]
    [InlineData(280)]
    [InlineData(320)]
    public void Points_just_either_side_of_a_boundary_land_in_adjacent_slices(double boundary)
    {
        var before = HitAt(boundary - 0.01, 150).SliceIndex;
        var after = HitAt(boundary + 0.01, 150).SliceIndex;
        Assert.Equal((before + 1) % 9, after);
    }

    [Fact]
    public void Center_is_the_hub()
    {
        Assert.Equal(DonutHitKind.Hub, _g.HitTest(0, 0).Kind);
        Assert.Equal(DonutHitKind.Hub, HitAt(123, 60).Kind);
    }

    [Fact]
    public void Gap_between_hub_and_ring_is_the_hole()
    {
        var hit = HitAt(200, 70);
        Assert.Equal(DonutHitKind.Hole, hit.Kind);
        Assert.False(hit.IsSlice);
        Assert.Equal(-1, hit.SliceIndex);
    }

    [Fact]
    public void Ring_edges_are_inclusive()
    {
        Assert.Equal(DonutHitKind.Slice, _g.HitTest(0, -80).Kind);
        Assert.Equal(DonutHitKind.Slice, _g.HitTest(200, 0).Kind);
        Assert.Equal(DonutHitKind.Slice, _g.HitTest(0, 200).Kind);
        Assert.Equal(DonutHitKind.Hole, _g.HitTest(-79.999, 0).Kind);
        Assert.Equal(DonutHitKind.Outside, _g.HitTest(0, 200.001).Kind);
    }

    [Theory]
    [InlineData(200.001, 0)]
    [InlineData(500, 0)]
    [InlineData(150, 150)] // corner of the bounding square: outside the circle
    public void Points_beyond_the_outer_radius_are_outside(double x, double y)
    {
        var hit = y == 0 ? HitAt(90, x) : _g.HitTest(x, y);
        Assert.Equal(DonutHitKind.Outside, hit.Kind);
    }

    [Fact]
    public void NaN_is_outside()
    {
        Assert.Equal(DonutHitKind.Outside, _g.HitTest(double.NaN, 1).Kind);
    }

    [Fact]
    public void Mid_angles_hit_their_own_slice()
    {
        for (var i = 0; i < 9; i++)
            Assert.Equal(i, HitAt(_g.SliceMidAngle(i), 140).SliceIndex);
    }

    [Fact]
    public void Invalid_radii_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new DonutGeometry(100, 120, 10));
        Assert.Throws<ArgumentException>(() => new DonutGeometry(100, 50, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DonutGeometry(100, 50, 10, 0));
    }
}
