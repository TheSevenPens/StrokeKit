using SkiaSharp;
using StrokeKit.Brushes;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;

namespace StrokeKit.Tests;

/// <summary>
/// That a sample-to-sample taper makes the same mark however its readings arrive.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole reason the engine exists, so it is checked in pixels rather than in
/// piece counts. <see cref="Engine.Taper"/> cannot pass this: it chooses a cut spacing from
/// the length of whatever path it is given, and it fills a run of equal-coloured pieces as one
/// path — so a prefix of a stroke is not a prefix of the finished stroke's drawing commands.
/// </para>
/// <para>
/// Reported on <c>#9</c>, after the evaluation on <c>#7</c> measured that the spacing keeps
/// changing to the last reading in 210 of 212 recorded strokes.
/// </para>
/// </remarks>
public class SampleTaperEngine
{
    private const int Size = 160;

    private static Brush Simple(byte alpha = 255) => new(
        14, new SKColor(0x20, 0x20, 0x20, alpha), 1,
        Buildup: Buildup.PerStamp,
        Width: new Width(2, 14, 1024, new Response(0, 1, 1)),
        SpacedBy: SpacedBy.Distance, Flow: null, Engine: Engine.SampleTaper);

    private static Reading At(double x, double y, uint pressure = 600) =>
        Synthetic.Reading(x, y, pressure);

    /// <summary>A stroke that turns and changes pressure, so the width is not a flat ramp.</summary>
    private static List<Reading> Wandering(int count = 24)
    {
        var readings = new List<Reading>();

        for (var each = 0; each < count; each++)
        {
            var along = each / (double)(count - 1);

            // A late pressure spike, which is the case that moves the other engine's grid.
            var pressure = (uint)(200 + (along > 0.8 ? 800 : 100 * along));

            readings.Add(At(20 + along * 110, 40 + Math.Sin(along * 3) * 30, pressure));
        }

        return readings;
    }

    private static Surface Drawn(Brush brush, IReadOnlyList<Reading> readings, int per)
    {
        var art = Surface.Create(Size, Size, 1);

        art.Canvas.Clear(SKColors.Transparent);

        if (per <= 0)
        {
            brush.Draw(art, InkTransform.For(art), new Stroke(readings));

            return art;
        }

        using var live = new Trail(brush, art, InkTransform.For(art));

        var sofar = new List<Reading>();

        foreach (var reading in readings)
        {
            sofar.Add(reading);

            // Delivered in batches of `per`, and whatever is left at the end.
            if (sofar.Count % per == 0) live.Extend(new Stroke(sofar));
        }

        if (sofar.Count % per != 0) live.Extend(new Stroke(sofar));

        live.Finish();

        return art;
    }

    private static int Differences(Surface a, Surface b)
    {
        var left = a.ReadAll();
        var right = b.ReadAll();

        var differences = 0;

        for (var each = 0; each < left.Length; each++)
        {
            if (left[each] != right[each]) differences++;
        }

        return differences;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100)]
    public void Drawn_as_it_arrives_it_matches_the_same_stroke_drawn_at_once(int per)
    {
        var readings = Wandering();

        using var batch = Drawn(Simple(), readings, per: 0);
        using var live = Drawn(Simple(), readings, per);

        Assert.Equal(0, Differences(batch, live));
    }

    [Fact]
    public void Translucent_crossings_match_too()
    {
        // Where a stroke crosses itself, per-stamp buildup means the overlap darkens. That is
        // the case where an engine that composited differently between the two paths would
        // show it, and an opaque brush would hide it.
        var readings = new List<Reading>();

        for (var each = 0; each < 40; each++)
        {
            var along = each / 39.0 * Math.PI * 2;

            readings.Add(At(80 + Math.Cos(along) * 40, 80 + Math.Sin(along * 2) * 40, 700));
        }

        using var batch = Drawn(Simple(90), readings, per: 0);
        using var live = Drawn(Simple(90), readings, per: 3);

        Assert.Equal(0, Differences(batch, live));
    }

    [Fact]
    public void A_tap_is_a_dot_and_it_is_the_same_dot_either_way()
    {
        var one = new List<Reading> { At(80, 80, 900) };

        using var batch = Drawn(Simple(), one, per: 0);
        using var live = Drawn(Simple(), one, per: 1);

        Assert.NotNull(batch.InkBounds());
        Assert.Equal(0, Differences(batch, live));
    }

    [Fact]
    public void A_stationary_reading_still_lays_a_piece()
    {
        // Two readings at one place with different pressures. Skipping a zero-length piece
        // would make the mark depend on whether the hand moved between two packets.
        var still = new List<Reading> { At(80, 80, 200), At(80, 80, 1000) };

        using var art = Drawn(Simple(), still, per: 0);

        var ink = art.InkBounds();

        Assert.NotNull(ink);

        // The second reading is the wider one, so the mark is wider than the first alone.
        var alone = Drawn(Simple(), [At(80, 80, 200)], per: 0);

        using (alone)
        {
            var small = alone.InkBounds();

            Assert.NotNull(small);
            Assert.True(ink.Value.Right - ink.Value.Left > small.Value.Right - small.Value.Left,
                "a stationary reading at higher pressure should widen the mark");
        }
    }

    [Fact]
    public void The_piece_count_is_the_reading_count()
    {
        var readings = Wandering(11);

        using var art = Surface.Create(Size, Size, 1);

        art.Canvas.Clear(SKColors.Transparent);

        Assert.Equal(11, Simple().Draw(art, InkTransform.For(art), new Stroke(readings)));
    }

    [Fact]
    public void A_stroke_being_drawn_may_not_shrink()
    {
        using var art = Surface.Create(Size, Size, 1);

        art.Canvas.Clear(SKColors.Transparent);

        using var live = new Trail(Simple(), art, InkTransform.For(art));

        var readings = Wandering(8);

        live.Extend(new Stroke(readings));

        Assert.Throws<ArgumentException>(() => live.Extend(new Stroke(readings.Take(3).ToList())));
    }

    [Fact]
    public void The_live_renderer_refuses_an_engine_it_does_not_draw()
    {
        using var art = Surface.Create(Size, Size, 1);

        var stamping = Simple() with { Engine = Engine.Stamps };

        Assert.Throws<NotSupportedException>(
            () => new Trail(stamping, art, InkTransform.For(art)));
    }

    [Fact]
    public void A_buildup_it_cannot_do_is_refused_rather_than_drawn_the_other_way()
    {
        using var art = Surface.Create(Size, Size, 1);

        art.Canvas.Clear(SKColors.Transparent);

        var held = Simple() with { Buildup = Buildup.OncePerStroke };

        Assert.Throws<NotSupportedException>(
            () => held.Draw(art, InkTransform.For(art), new Stroke(Wandering(6))));
    }
}
