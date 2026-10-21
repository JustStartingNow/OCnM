namespace OCnM.Core.Geometry;

public enum DonutHitKind
{
    /// <summary>Beyond the outer radius.</summary>
    Outside,

    /// <summary>The gap between the hub and the ring.</summary>
    Hole,

    /// <summary>The small center disc.</summary>
    Hub,

    Slice,
}

public readonly record struct DonutHit(DonutHitKind Kind, int SliceIndex = -1)
{
    public bool IsSlice => Kind == DonutHitKind.Slice;
}

/// <summary>
/// Pure math for a ring split into equal slices. Angles are in degrees, measured clockwise
/// from 12 o'clock, in screen coordinates (y grows downward). Slice 0 starts at 12 o'clock.
/// </summary>
public sealed class DonutGeometry
{
    public DonutGeometry(double outerRadius, double innerRadius, double hubRadius, int sliceCount = 9)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sliceCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(hubRadius);
        if (!(hubRadius <= innerRadius && innerRadius < outerRadius))
            throw new ArgumentException("Radii must satisfy hub <= inner < outer.");

        OuterRadius = outerRadius;
        InnerRadius = innerRadius;
        HubRadius = hubRadius;
        SliceCount = sliceCount;
    }

    public double OuterRadius { get; }

    public double InnerRadius { get; }

    public double HubRadius { get; }

    public int SliceCount { get; }

    public double SliceSweep => 360.0 / SliceCount;

    public double SliceStartAngle(int index) => index * SliceSweep;

    public double SliceMidAngle(int index) => (index + 0.5) * SliceSweep;

    /// <summary>Hit-tests a point given relative to the donut center.</summary>
    public DonutHit HitTest(double dx, double dy)
    {
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (double.IsNaN(r) || r > OuterRadius)
            return new DonutHit(DonutHitKind.Outside);
        if (r < InnerRadius)
            return r <= HubRadius ? new DonutHit(DonutHitKind.Hub) : new DonutHit(DonutHitKind.Hole);
        return new DonutHit(DonutHitKind.Slice, SliceAtAngle(AngleOf(dx, dy)));
    }

    /// <summary>Angle of a vector relative to the center, in [0, 360).</summary>
    public static double AngleOf(double dx, double dy)
    {
        // atan2(x, -y) puts 0 at 12 o'clock and grows clockwise because screen y points down.
        var deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        return NormalizeAngle(deg);
    }

    public static double NormalizeAngle(double degrees)
    {
        var a = degrees % 360.0;
        if (a < 0)
            a += 360.0;
        // -1e-15 % 360 + 360 rounds to exactly 360.
        return a >= 360.0 ? 0.0 : a;
    }

    /// <summary>A boundary angle belongs to the slice that starts there.</summary>
    public int SliceAtAngle(double degrees)
    {
        var a = NormalizeAngle(degrees);
        var index = (int)Math.Floor(a / SliceSweep);
        return Math.Clamp(index, 0, SliceCount - 1);
    }

    /// <summary>Point at an angle and radius, relative to the center.</summary>
    public static (double X, double Y) PointAt(double degrees, double radius)
    {
        var rad = degrees * Math.PI / 180.0;
        return (radius * Math.Sin(rad), -radius * Math.Cos(rad));
    }
}
