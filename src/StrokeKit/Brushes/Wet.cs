using SkiaSharp;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;

namespace StrokeKit.Brushes;

/// <summary>
/// A stroke that is still arriving: something to extend, something to show, and a lift.
/// <para>
/// An interface because the wrong ways of doing this are worth writing down beside the right
/// one. A page in this part implements two of them on purpose.
/// </para>
/// </summary>
public interface ILive : IDisposable
{
    /// <summary>
    /// Lays whatever is new since the last call, and answers how many that was.
    /// </summary>
    /// <remarks>
    /// <b>Each call must be given the same stroke, grown.</b> What is passed has to begin
    /// with everything passed before it: this lays only the readings it has not seen, so a
    /// shorter stroke, or a different one, is not drawn and not refused — it simply comes out
    /// wrong. See <see cref="Wet.Extend"/>, which says what happens when it is not.
    /// </remarks>
    int Extend(Stroke sofar);

    /// <summary>What the reader should be looking at now.</summary>
    void PresentOnto(Surface display);

    /// <summary>The pen has lifted.</summary>
    void Finish();

    /// <summary>How many marks have been laid since the stroke began.</summary>
    /// <remarks>
    /// Stamps for one engine and filled pieces for another, which is why it is not named for
    /// either. It is here because a caller showing what a stroke has cost cannot ask the
    /// engine it deliberately does not know about.
    /// </remarks>
    int Laid { get; }
}

/// <summary>
/// Which live renderer draws a brush.
/// </summary>
/// <remarks>
/// A brush says which engine it wants, and the engines want different things while a stroke
/// is arriving: one holds a surface until the lift, the other holds a count. So the choice
/// belongs here rather than in every caller. <see cref="Engine.Taper"/> has none, because it
/// decides its cutting from the whole path — see <c>#2</c>.
/// </remarks>
public static class Live
{
    public static ILive For(Brush brush, Surface target, InkTransform transform) =>
        brush.Engine switch
        {
            Engine.Stamps => new Wet(brush, target, transform),
            Engine.SampleTaper => new Trail(brush, target, transform),
            _ => throw new NotSupportedException(
                $"no live renderer draws {brush.Engine}. {Engine.Stamps} and "
                + $"{Engine.SampleTaper} can be drawn as a stroke arrives; a taper's cuts "
                + "depend on the whole path."),
        };
}

/// <summary>
/// A stroke being drawn while it is still arriving.
/// <para>
/// <see cref="Stroke"/> is the finished form. This is the other one: a stroke that has a
/// beginning, more readings coming, and no end yet. It exists because the two buildups need
/// very different things from it, and neither of them is "draw the stroke again".
/// </para>
/// <para>
/// <b>Wet</b> is borrowed from paint: ink that is down and visible but not yet dry, and so is
/// still a thing in its own right rather than part of the picture. Under
/// <see cref="Buildup.OncePerStroke"/> it names <c>_wet</c>, the stroke's own surface, which
/// lives only between pen-down and pen-up:
/// </para>
/// <code>
/// pen down       create the wet layer, matching the target in size and scale
/// each reading   new stamps  -&gt; wet layer, alpha-darkened against each other
///                what is shown  =  target, with the wet layer drawn over it
/// pen up         wet layer  -&gt; target, once, then cleared
/// </code>
/// <para>
/// The target is untouched until <see cref="Finish"/>. Under <see cref="Buildup.PerStamp"/>
/// there is no wet layer -- <c>_wet</c> is null and stamps go straight onto the target -- so
/// every branch on <c>_wet</c> below is really a branch on which buildup is in force.
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

    /// <summary>
    /// The stroke as of the last <see cref="Extend"/>, for a brush whose gap depends on it.
    /// <para>
    /// Held rather than passed because the walk is built once, at pen-down, and the stroke it
    /// has to ask about arrives later and keeps growing. Every version of it is a prefix of
    /// the next, so the latest one answers for every stamp.
    /// </para>
    /// </summary>
    private Stroke? _sofar;

    public Wet(Brush brush, Surface target, InkTransform transform)
    {
        // Refused rather than approximated. Until 16 September 2026 a taper brush handed to
        // this silently came out stamped: the same ink, laid a completely different way, with
        // nothing anywhere saying the engine had been ignored. An outlining engine drawn
        // incrementally is a real thing to build and is not this.
        if (brush.Engine != Engine.Stamps)
        {
            throw new NotSupportedException(
                $"drawing a stroke as it arrives is only implemented for {Engine.Stamps}, "
                + $"and this brush asks for {brush.Engine}");
        }

        _brush = brush;
        _target = target;
        _transform = transform;
        _walk = brush.SpacedBy == SpacedBy.Distance
            ? new Walk(brush.Spacing)
            : new Walk(brush.Spacing, placement => _brush.GapAfter(_sofar!, placement));

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
        // The contract, checked rather than assumed. Everything below lays only what it has
        // not seen, which is legal exactly because the stamps of a stroke so far are a prefix
        // of the finished one's -- and that holds only if the stroke itself keeps growing
        // from the same beginning. Given a shorter stroke, the walk would carry on from a
        // position the path no longer reaches and lay stamps in mid air.
        //
        // Refused rather than handled: a caller doing this has lost track of which stroke it
        // is drawing, and quietly starting again would hide that.
        if (sofar.Count < _readings)
        {
            throw new ArgumentException(
                $"a stroke being drawn can only grow: this had {_readings} readings and has "
                + $"been given {sofar.Count}. Use a new Wet for a new stroke.",
                nameof(sofar));
        }

        // Only the readings that are new, and only the segments they added. Recomputing the
        // whole path's stamps each time would be linear per call and quadratic over the
        // stroke -- the same shape as redrawing it, arrived at by recomputation rather than
        // by compositing, and just as invisible until a stroke gets long.
        _sofar = sofar;

        // A nib that follows the path has no angle until there is a path to follow. With one
        // reading in hand the direction is read from that reading to itself, which is no
        // direction at all, and the stamp comes out at the nib's resting angle; the next
        // reading turns the same placement to the direction of travel. Positions stay a
        // prefix of the finished stroke and stamp commands stop being one, which is the
        // property this whole class rests on.
        //
        // So nothing is laid until the angle is settled. Ink cannot be un-drawn, and a stamp
        // at an orientation that was never going to be the right one is worse than a stamp
        // that arrives one reading late.
        if (_brush.Nib is { Held: Held.ToThePath } && sofar.Count < 2) return 0;

        for (; _readings < sofar.Count; _readings++)
            _path.Add((sofar.Points[_readings].X, sofar.Points[_readings].Y));

        var placements = _walk.Advance(_path);
        if (placements.Count == 0) return 0;

        var surface = _wet ?? _target;
        using var blender = _wet is null ? null : AlphaDarken.Blender();

        foreach (var placement in placements)
        {
            // The brush's own stamp, not one rebuilt from three of its five fields. Built
            // by hand here, this dropped the nib's ratio and its angle -- so a round stamp
            // went down wherever a shaped one was asked for, and live and batch differed by
            // 924 pixels in a 100-square fixture with nothing reporting a difference.
            var stamp = _brush.StampAt(sofar, placement);

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
