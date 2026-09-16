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

        // Two that cross, so the overlap between separate strokes is visible next to the
        // overlap within one.
        Lay(art, transform, new Brush(30, new SKColor(0x8A, 0x4B, 0x12).WithAlpha(0x60), 4),
            Synthetic.Line(200, 420, 800, 900, 120));

        Lay(art, transform, new Brush(30, new SKColor(0x1E, 0x4D, 0x8A).WithAlpha(0x60), 4),
            Synthetic.Line(800, 420, 200, 900, 120));
    }

    /// <summary>
    /// Readings to strokes to stamps, which is the path itself and is why it is one line.
    /// <para>
    /// The readings go through <c>Strokes.From</c> rather than straight into the brush, even
    /// though they are all in contact and it will always return one stroke. Bypassing it
    /// would make this a different pipeline from the one the page checks.
    /// </para>
    /// </summary>
    private static void Lay(
        Surface art, InkTransform transform, Brush brush, IReadOnlyList<WinPenKit.PenPoint> readings)
    {
        foreach (var stroke in Strokes.Strokes.From(readings)) brush.Draw(art, transform, stroke);
    }
}
