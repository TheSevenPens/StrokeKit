using SkiaSharp;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Brushes;

/// <summary>
/// A stroke that is still arriving: something to extend, something to show, and a lift.
/// <para>
/// An interface because the wrong ways of doing this are worth writing down beside the right
/// one. A page in this part implements two of them on purpose.
/// </para>
/// </summary>
public interface ILive : IDisposable
{
    /// <summary>Lays whatever is new since the last call, and answers how many that was.</summary>
    int Extend(Stroke sofar);

    /// <summary>What the reader should be looking at now.</summary>
    void PresentOnto(Surface display);

    /// <summary>The pen has lifted.</summary>
    void Finish();
}

/// <summary>
/// A stroke being drawn while it is still arriving.
/// <para>
/// <see cref="Stroke"/> is the finished form. This is the other one: a stroke that has a
/// beginning, more readings coming, and no end yet. It exists because the two buildups need
/// very different things from it, and neither of them is "draw the stroke again".
/// </para>
/// <para>
/// Under <see cref="Buildup.PerStamp"/> there is nothing to hold. Each new stamp is
/// composited onto the surface as it arrives and the surface is correct after every reading.
/// Under <see cref="Buildup.OncePerStroke"/> the stroke's own surface has to stay alive until
/// the pen lifts, because the whole point is that it meets the real surface once.
/// </para>
/// </summary>
public sealed class Wet : ILive
{
    private readonly Brush _brush;
    private readonly Surface _target;
    private readonly InkTransform _transform;
    private readonly Surface? _wet;

    /// <summary>The path so far, grown rather than rebuilt, so appending is amortised.</summary>
    private readonly List<(double X, double Y)> _path = [];

    private readonly Walk _walk;

    private int _readings;
    private int _laid;

    public Wet(Brush brush, Surface target, InkTransform transform)
    {
        _brush = brush;
        _target = target;
        _transform = transform;
        _walk = new Walk(brush.Spacing);

        // The stroke's own surface, the same size and logical size as the one it will meet,
        // so the transform means the same thing on both.
        _wet = brush.Buildup == Buildup.OncePerStroke
            ? Surface.CreateExactly(
                target.PixelWidth, target.PixelHeight, target.LogicalWidth, target.LogicalHeight)
            : null;
    }

    /// <summary>How many stamps have been laid so far.</summary>
    public int Laid => _laid;

    /// <summary>
    /// Lays whatever is new since the last call, and answers how many that was.
    /// <para>
    /// Only what is new. Laying the whole stroke again on every reading is the mistake this
    /// type exists to make impossible: under per-stamp buildup it composites every earlier
    /// stamp a second time, and a translucent stroke goes black in a few frames.
    /// </para>
    /// <para>
    /// What makes this legal is that the stamps of a stroke so far are a <b>prefix</b> of the
    /// stamps of the finished one. That is a property of spacing by distance travelled, and a
    /// spacing rule that moved its last stamp onto the end of the path would not have it.
    /// </para>
    /// </summary>
    public int Extend(Stroke sofar)
    {
        // Only the readings that are new, and only the segments they added. Recomputing the
        // whole path's stamps each time would be linear per call and quadratic over the
        // stroke -- the same shape as redrawing it, arrived at by recomputation rather than
        // by compositing, and just as invisible until a stroke gets long.
        for (; _readings < sofar.Count; _readings++)
            _path.Add((sofar.Points[_readings].DesktopX, sofar.Points[_readings].DesktopY));

        var placements = _walk.Advance(_path);
        if (placements.Count == 0) return 0;

        var surface = _wet ?? _target;
        using var blender = _wet is null ? null : AlphaDarken.Blender();

        foreach (var placement in placements)
        {
            var stamp = new Stamp(_brush.DiameterAt(sofar, placement), _brush.Colour);

            Stamps.Draw(surface, _transform, stamp, placement.X, placement.Y, blender);
        }

        _laid += placements.Count;

        return placements.Count;
    }

    /// <summary>
    /// What the reader should be looking at now: the surface, with anything not yet committed
    /// over the top.
    /// <para>
    /// For per-stamp buildup that is just the surface. For once-per-stroke it is the surface
    /// and the wet stroke composited for display only -- the document itself has not changed
    /// and will not until the pen lifts.
    /// </para>
    /// </summary>
    public void PresentOnto(Surface display)
    {
        display.Canvas.Clear(SKColors.Transparent);

        using var beneath = _target.Snapshot();
        display.Canvas.DrawImage(beneath, 0, 0);

        if (_wet is null) return;

        using var above = _wet.Snapshot();
        display.Canvas.DrawImage(above, 0, 0);
    }

    /// <summary>
    /// The pen has lifted. Merges the stroke into the surface, once.
    /// <para>
    /// Once, and at the end. Merging on every reading is the same arithmetic as compositing
    /// per stamp, arrived at by a longer route.
    /// </para>
    /// </summary>
    public void Finish()
    {
        if (_wet is null) return;

        using var image = _wet.Snapshot();
        _target.Canvas.DrawImage(image, 0, 0);

        _wet.Canvas.Clear(SKColors.Transparent);
    }

    public void Dispose() => _wet?.Dispose();
}
