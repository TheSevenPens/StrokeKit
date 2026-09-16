using SkiaSharp;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Brushes;

/// <summary>
/// The smallest brush engine that draws a stroke: a stamp, and how far apart to put it.
/// <para>
/// Deliberately the smallest. Every other property a brush could have -- width from
/// pressure, a blend mode, caps, a textured tip -- is a page of its own in this part, and
/// adding any of them now would mean a mark that came out wrong had more than one candidate
/// explanation. What this establishes is that the path from a stroke to pixels works at all.
/// </para>
/// <para>
/// It decides <b>which</b> commands to issue and where. It does not decide which pixels
/// change: that is the canvas's, and the two being separate is what lets the same stroke be
/// drawn into a full-size surface and a thumbnail without the engine knowing either exists.
/// </para>
/// </summary>
/// <param name="Diameter">Across, in the stroke's own units. Not a radius.</param>
/// <param name="Spacing">
/// Distance between stamps, in the stroke's own units. A fraction of the diameter, because
/// at a spacing equal to the diameter the stamps touch without overlapping and the stroke
/// comes out as a row of circles.
/// </param>
/// <param name="Buildup">
/// Whether the stamps meet the surface one at a time or the stroke does, once. Defaults to
/// one at a time, which is the simpler behaviour and the one most engines have.
/// </param>
public readonly record struct Brush(
    double Diameter, SKColor Colour, double Spacing, Buildup Buildup = Buildup.PerStamp)
{
    /// <summary>
    /// Where the stamps go, in the stroke's own units, before anything is drawn.
    /// <para>
    /// Separate from drawing them so that a check can compare the positions against
    /// arithmetic rather than against pixels, and so that the positions can be counted
    /// without a surface existing.
    /// </para>
    /// </summary>
    public IReadOnlyList<(double X, double Y)> Positions(Stroke stroke)
    {
        // The pen's own coordinates, in physical screen pixels. Turning those into the
        // surface's is the ink transform's job and happens per stamp, below.
        var path = stroke.Points.Select(point => (point.DesktopX, point.DesktopY)).ToList();

        return Brushes.Spacing.Along(path, Spacing);
    }

    /// <summary>
    /// Draws the stroke, and answers how many stamps that took.
    /// <para>
    /// The count is returned because it is the one number about a finished mark that can be
    /// predicted exactly from the stroke's length and the spacing, and comparing it against
    /// that arithmetic is cheaper and sharper than comparing pixels.
    /// </para>
    /// </summary>
    public int Draw(Surface surface, InkTransform transform, Stroke stroke)
    {
        var stamp = new Stamp(Diameter, Colour);
        var positions = Positions(stroke);

        if (Buildup == Buildup.PerStamp)
        {
            foreach (var (x, y) in positions) Stamps.Draw(surface, transform, stamp, x, y);

            return positions.Count;
        }

        // The stroke's own coverage first, on a surface of its own, where overlapping stamps
        // take the greater alpha instead of adding. Then that surface onto this one, once.
        //
        // The same size and the same logical size, so the transform means the same thing on
        // both and the stamps land where they would have.
        using var stroking = Surface.CreateExactly(
            surface.PixelWidth, surface.PixelHeight, surface.LogicalWidth, surface.LogicalHeight);

        using var blender = AlphaDarken.Blender();

        foreach (var (x, y) in positions) Stamps.Draw(stroking, transform, stamp, x, y, blender);

        using var image = stroking.Snapshot();
        surface.Canvas.DrawImage(image, 0, 0);

        return positions.Count;
    }
}
