using SkiaSharp;
using StrokeKit.Surfaces;

namespace StrokeKit.Tests;

/// <summary>
/// That a surface follows its host's size and scale without losing what is on it.
/// </summary>
/// <remarks>
/// <para>
/// Reported on <c>#1</c>, after PenDynamicsLab drew the fault with a real pen. A surface could
/// only grow, so every consumer wrote its own orchestration — and the one that wrote it keyed
/// off the pixel count alone. Moving a window from a 2.25x display to a 1.75x one needs
/// <i>fewer</i> pixels, so nothing was rebuilt and the surface went on carrying a transform for
/// a scale it was no longer shown at. Ink landed long by the ratio: right at the origin,
/// drifting further out the further the pen went.
/// </para>
/// </remarks>
public class Resizing
{
    private static Surface Marked(double logical, double scale, double at = 100)
    {
        var art = Surface.Create(logical, logical, scale);

        art.Canvas.Clear(SKColors.Transparent);

        // A mark at a known place in logical units, drawn through the same transform a caller
        // would use, so where it lands says what the surface thinks its scale is.
        art.Canvas.Save();
        art.Canvas.Scale((float)art.ScaleX, (float)art.ScaleY);

        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
        {
            art.Canvas.DrawRect((float)at, (float)at, 10, 10, paint);
        }

        art.Canvas.Restore();

        return art;
    }

    [Fact]
    public void A_host_that_has_not_changed_gets_the_same_surface_back()
    {
        using var art = Surface.Create(200, 200, 2);

        Assert.Same(art, art.Resized(200, 200, 2));
    }

    [Fact]
    public void A_scale_that_differs_only_by_rounding_is_the_same_scale()
    {
        // 1177 * 2.25 rounds to 2648, and 2648 / 1177 is not 2.25. Comparing those directly
        // rebuilds the surface on every frame for ever.
        using var art = Surface.Create(1177, 653, 2.25);

        Assert.NotEqual(2.25, art.ScaleX);
        Assert.Same(art, art.Resized(1177, 653, 2.25));
    }

    [Theory]
    [InlineData(2.25, 1.75)]
    [InlineData(1.0, 2.0)]
    [InlineData(1.75, 2.25)]
    public void A_change_of_scale_rebuilds_even_when_it_needs_fewer_pixels(double was, double now)
    {
        using var art = Marked(200, was);

        using var moved = art.Resized(200, 200, now);

        Assert.NotSame(art, moved);

        // The scale it was asked for, within the rounding its own pixel count imposes.
        Assert.True(Math.Abs(moved.ScaleX - now) * 200 < 1,
            $"asked for {now} and got {moved.ScaleX}");
    }

    [Theory]
    [InlineData(2.25, 1.75)]
    [InlineData(1.0, 2.0)]
    public void A_mark_keeps_its_place_in_logical_units_across_a_change_of_scale(
        double was, double now)
    {
        using var art = Marked(200, was, at: 100);
        using var moved = art.Resized(200, 200, now);

        var ink = moved.InkBounds();

        Assert.NotNull(ink);

        // The mark was at 100 logical units and should still be, which is 100 * the new scale
        // in pixels. Resampling costs a pixel or so at the edges.
        Assert.True(Math.Abs(ink.Value.Left - 100 * now) < 3,
            $"a mark at 100 logical units should be near {100 * now}px after {was}x to {now}x; "
            + $"it is at {ink.Value.Left}px");
    }

    [Fact]
    public void A_host_that_grows_gets_a_bigger_surface_with_its_ink_still_on_it()
    {
        using var art = Marked(200, 1);
        using var grown = art.Resized(400, 300, 1);

        Assert.NotSame(art, grown);
        Assert.True(grown.PixelWidth >= 400 && grown.PixelHeight >= 300);

        var ink = grown.InkBounds();

        Assert.NotNull(ink);
        Assert.True(Math.Abs(ink.Value.Left - 100) < 2, $"the mark moved to {ink.Value.Left}");
    }

    [Fact]
    public void A_host_that_shrinks_keeps_the_surface_it_had()
    {
        // Shrinking would throw away whatever is outside the smaller bounds, and it does not
        // come back when the host grows again.
        using var art = Surface.Create(400, 400, 1);

        Assert.Same(art, art.Resized(100, 100, 1));
    }

    [Fact]
    public void Nonsense_is_refused_by_changing_nothing()
    {
        using var art = Surface.Create(200, 200, 1);

        Assert.Same(art, art.Resized(0, 200, 1));
        Assert.Same(art, art.Resized(200, 200, 0));
        Assert.Same(art, art.Resized(200, 200, double.NaN));
    }
}
