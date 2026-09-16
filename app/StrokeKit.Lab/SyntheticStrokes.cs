using SkiaSharp;
using StrokeFieldGuide.Brushes;
using StrokeFieldGuide.Figures;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Lab;

/// <summary>
/// The whole path from made-up pen points to pixels, run into the document so it can be
/// looked at.
/// <para>
/// Nothing here decides anything. Every stage is the library's — <see cref="Synthetic"/> for
/// the readings, <c>Strokes.From</c> for the contact runs, <see cref="Brush"/> for the
/// spacing and the stamps — and this file only chooses which strokes to draw and where. The
/// page that checks this path predicts the result from arithmetic; this is the same result,
/// at a size a person can see.
/// </para>
/// </summary>
public static class SyntheticStrokes
{
    /// <summary>
    /// A ruler, a curve, and a pair that cross, on a blank page.
    /// <para>
    /// The ruler is the one that can be measured against the arithmetic by eye. It is drawn
    /// at a spacing equal to its own diameter, which is the spacing at which stamps touch
    /// without overlapping: the stroke comes out as a row of circles, deliberately, because
    /// that is the fault stamp spacing exists to describe and it is much easier to recognise
    /// once seen.
    /// </para>
    /// </summary>
    public static void Draw(Surface art)
    {
        art.Canvas.Clear(Demo.Paper);

        var transform = InkTransform.For(art);

        // A row of circles: spacing equal to the diameter, so they touch and do not overlap.
        Lay(art, transform, new Brush(24, new SKColor(0xB0, 0x2E, 0x2E), 24),
            Synthetic.Line(120, 160, 880, 160, 61));

        // The same stroke, at a spacing of an eighth of the diameter, which is solid.
        Lay(art, transform, new Brush(24, new SKColor(0x10, 0x2A, 0x33), 3),
            Synthetic.Line(120, 260, 880, 260, 61));

        // A curve, because a straight line hides anything that only a change of direction
        // produces.
        Lay(art, transform, new Brush(18, new SKColor(0x0D, 0x6A, 0x6A), 2),
            Synthetic.Arc(500, 620, 260, 200, 340, 240));

        // The same translucent stroke, composited two ways. Per stamp it is nearly opaque
        // because the stamps accumulate; once per stroke it is the alpha it was asked for.
        // Same brush, same spacing, same path: the difference is only where the stamps meet.
        var wash = new SKColor(0x8A, 0x4B, 0x12).WithAlpha(0x40);

        Lay(art, transform, new Brush(34, wash, 3, Buildup.PerStamp),
            Synthetic.Line(120, 430, 880, 430, 200));

        Lay(art, transform, new Brush(34, wash, 3, Buildup.OncePerStroke),
            Synthetic.Line(120, 510, 880, 510, 200));

        // A pressure ramp, twice. Same width either way; the per-stamp one darkens as it
        // widens, because the number of stamps over a point is the diameter over the spacing
        // and the diameter is now varying.
        //
        // The ramp swells and falls again rather than only rising. Falling pressure used to
        // saturate -- the subtraction ran in unsigned arithmetic and wrapped -- and a demo
        // that only ever pressed harder could not have shown it. Both halves of each stroke
        // below should be mirror images; if the right half is a slab at full width, that is
        // the fault come back.
        var nib = new Width(4, 30, 1024);

        Lay(art, transform, new Brush(10, new SKColor(0x10, 0x2A, 0x33).WithAlpha(0x40), 3, Buildup.PerStamp, nib),
            Swell(120, 610, 880, 610, 100, 1000));

        Lay(art, transform, new Brush(10, new SKColor(0x10, 0x2A, 0x33).WithAlpha(0x40), 3, Buildup.OncePerStroke, nib),
            Swell(120, 700, 880, 700, 100, 1000));

        // And one stroke that crosses itself, composited once: uniform through the crossing,
        // where per stamp it would darken there.
        Lay(art, transform, new Brush(34, new SKColor(0x1E, 0x4D, 0x8A).WithAlpha(0x40), 3, Buildup.OncePerStroke),
            [.. Synthetic.Line(220, 780, 780, 940, 160), .. Synthetic.Line(780, 940, 220, 940, 160),
             .. Synthetic.Line(220, 940, 780, 780, 160)]);
    }

    /// <summary>
    /// Readings to strokes to stamps, which is the path itself and is why it is one line.
    /// <para>
    /// The readings go through <c>Strokes.From</c> rather than straight into the brush, even
    /// though they are all in contact and it will always return one stroke. Bypassing it
    /// would make this a different pipeline from the one the page checks.
    /// </para>
    /// </summary>
    /// <summary>
    /// Readings along a line whose pressure rises to the midpoint and falls again, which is
    /// the shape of an ordinary stroke and exercises the interpolation in both directions.
    /// </summary>
    private static IReadOnlyList<Reading> Swell(
        double fromX, double fromY, double toX, double toY, uint atEnds, uint atMiddle)
    {
        var midX = (fromX + toX) / 2;
        var midY = (fromY + toY) / 2;

        // Skip(1) on the second half: the midpoint reading would otherwise appear twice, which
        // is a zero-length segment and harmless, but not something to put in a fixture.
        return
        [
            .. Ramp(fromX, fromY, midX, midY, atEnds, atMiddle),
            .. Ramp(midX, midY, toX, toY, atMiddle, atEnds).Skip(1),
        ];
    }

    /// <summary>Readings along a line with the pressure ramped from one value to another.</summary>
    private static IReadOnlyList<Reading> Ramp(
        double fromX, double fromY, double toX, double toY, uint fromPressure, uint toPressure)
    {
        const int readings = 60;
        var points = new List<Reading>(readings);

        for (var index = 0; index < readings; index++)
        {
            var along = index / (double)(readings - 1);

            points.Add(Synthetic.Reading(
                fromX + (toX - fromX) * along,
                fromY + (toY - fromY) * along,
                (uint)Math.Round(Pressures.Between(fromPressure, toPressure, along))));
        }

        return points;
    }

    private static void Lay(
        Surface art, InkTransform transform, Brush brush, IReadOnlyList<Reading> readings)
    {
        foreach (var stroke in Strokes.Strokes.From(readings)) brush.Draw(art, transform, stroke);
    }
}
