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
/// The gap between stamps, in whatever <paramref name="SpacedBy"/> says it is measured in.
/// <para>
/// In the stroke's own units by default. Keep it well under the diameter: at a spacing equal
/// to the diameter the stamps touch without overlapping and the stroke comes out as a row of
/// circles.
/// </para>
/// </param>
/// <param name="Buildup">
/// Whether the stamps meet the surface one at a time or the stroke does, once. Defaults to
/// one at a time, which is the simpler behaviour and the one most engines have.
/// </param>
/// <param name="Width">
/// How pressure becomes a diameter, or null for a stamp that is always
/// <paramref name="Diameter"/> across. Null by default, because the pages before this one
/// are about everything a stroke does before its width varies.
/// </param>
/// <param name="Hardness">
/// How much of a stamp is at full strength before its edge begins, from 0 to 1. Hard by
/// default, which is what every page before this one was measured against.
/// <para>
/// Read by the stamping engine only. An outlined stroke has one edge for the whole mark
/// rather than one per stamp, and softening it is a different piece of work.
/// </para>
/// </param>
/// <param name="Engine">
/// Which engine lays the marks down. Stamps by default, which is what every page before the
/// taper was measured against.
/// <para>
/// A named choice on the brush rather than an object, so that a stroke can record what drew
/// it: an engine holds a path and a paint, and a record of something finished should not be
/// holding a live one.
/// </para>
/// </param>
/// <param name="SpacedBy">
/// Whether <paramref name="Spacing"/> is a distance or a number of diameters. Distance by
/// default, which is the simpler rule and the one every page before this one was measured
/// against.
/// </param>
public readonly record struct Brush(
    double Diameter,
    SKColor Colour,
    double Spacing,
    Buildup Buildup = Buildup.PerStamp,
    Width? Width = null,
    SpacedBy SpacedBy = SpacedBy.Distance,
    Flow? Flow = null,
    Engine Engine = Engine.Stamps,
    double Hardness = 1)
{
    /// <summary>
    /// The colour a stamp is laid in, which is <see cref="Colour"/> unless the pen decides
    /// its alpha.
    /// </summary>
    /// <remarks>
    /// Only the alpha varies. A brush whose hue followed the pen is a different kind of
    /// control and would want saying so; what a pressure-driven ink does is put down more or
    /// less of the same colour.
    /// </remarks>
    public SKColor ColourAt(Stroke stroke, Placement placement)
    {
        if (Flow is not { } flow) return Colour;

        var from = stroke.Points[placement.Segment];
        var to = stroke.Points[Math.Min(placement.Segment + 1, stroke.Count - 1)];

        var pressure = Pressures.Between(from.Pressure, to.Pressure, placement.Fraction);

        return Colour.WithAlpha(flow.AlphaFor((uint)Math.Round(Math.Max(0, pressure))));
    }

    /// <summary>
    /// The gap to leave after a stamp, which is the whole of what <see cref="SpacedBy"/>
    /// decides.
    /// </summary>
    /// <remarks>
    /// Measured from the stamp just laid rather than the one about to be. The next stamp's
    /// diameter depends on where it lands, which depends on this gap, so asking it first is
    /// a fixed point -- solved iteratively, for a difference below a pixel.
    /// </remarks>
    public double GapAfter(Stroke stroke, Placement placement) =>
        SpacedBy == SpacedBy.Distance ? Spacing : Spacing * DiameterAt(stroke, placement);

    /// <summary>The walk this brush needs: one constant gap, or a gap per stamp.</summary>
    public Walk WalkAlong(Stroke stroke)
    {
        if (SpacedBy == SpacedBy.Distance) return new Walk(Spacing);

        // Copied into locals first: a lambda in a struct cannot capture "this".
        var brush = this;
        var along = stroke;

        return new Walk(Spacing, placement => brush.GapAfter(along, placement));
    }

    /// <summary>Where this brush's stamps go, and between which readings each one fell.</summary>
    public IReadOnlyList<Placement> Placements(Stroke stroke) =>
        WalkAlong(stroke).Advance([.. stroke.Points.Select(point => (point.DesktopX, point.DesktopY))]);

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
        var positions = new List<(double X, double Y)>();

        foreach (var placement in Placements(stroke)) positions.Add((placement.X, placement.Y));

        return positions;
    }

    /// <summary>
    /// The stamps this brush would lay: where each goes and how wide it is there.
    /// <para>
    /// Separate from drawing them for the same reason the positions are: the sizes can be
    /// compared against arithmetic without a surface existing.
    /// </para>
    /// </summary>
    public IReadOnlyList<Stamp> Stamps(Stroke stroke)
    {
        var stamps = new List<Stamp>();

        foreach (var placement in Placements(stroke))
            stamps.Add(new Stamp(DiameterAt(stroke, placement), ColourAt(stroke, placement), Hardness));

        return stamps;
    }

    /// <summary>
    /// The diameter where a stamp landed, which is almost never at a reading.
    /// <para>
    /// The pressure is interpolated between the two readings the stamp fell between. Taking
    /// the nearer reading's pressure instead makes the width step rather than ramp, and puts
    /// the steps wherever the hand happened to be slow -- which is a property of the report
    /// rate rather than of the stroke.
    /// </para>
    /// </summary>
    public double DiameterAt(Stroke stroke, Placement placement)
    {
        if (Width is not { } width) return Diameter;

        var from = stroke.Points[placement.Segment];
        var to = stroke.Points[Math.Min(placement.Segment + 1, stroke.Count - 1)];

        var pressure = Pressures.Between(from.Pressure, to.Pressure, placement.Fraction);

        return width.For((uint)Math.Round(pressure));
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
        // Dispatched once, here, rather than branched on further down. The two engines share
        // a brush and share nothing else: one asks where stamps go, the other asks where the
        // path turns, and below this line everything is the stamping engine's.
        if (Engine == Engine.Taper) return Taper.Draw(surface, transform, this, stroke);

        var placements = Placements(stroke);

        var laid = new List<(double X, double Y, Stamp Stamp)>(placements.Count);

        foreach (var placement in placements)
        {
            laid.Add((placement.X, placement.Y,
                new Stamp(DiameterAt(stroke, placement), ColourAt(stroke, placement), Hardness)));
        }

        if (Buildup == Buildup.PerStamp)
        {
            foreach (var (x, y, stamp) in laid) Brushes.Stamps.Draw(surface, transform, stamp, x, y);

            return laid.Count;
        }

        // The stroke's own coverage first, on a surface of its own, where overlapping stamps
        // take the greater alpha instead of adding. Then that surface onto this one, once.
        //
        // The same size and the same logical size, so the transform means the same thing on
        // both and the stamps land where they would have.
        using var stroking = Surface.CreateExactly(
            surface.PixelWidth, surface.PixelHeight, surface.LogicalWidth, surface.LogicalHeight);

        using var blender = AlphaDarken.Blender();

        foreach (var (x, y, stamp) in laid) Brushes.Stamps.Draw(stroking, transform, stamp, x, y, blender);

        using var image = stroking.Snapshot();
        surface.Canvas.DrawImage(image, 0, 0);

        return laid.Count;
    }
}
