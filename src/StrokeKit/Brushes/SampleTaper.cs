using SkiaSharp;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;

namespace StrokeKit.Brushes;

/// <summary>
/// One filled taper between each pair of consecutive readings, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The simple engine, on purpose.</b> <see cref="Taper"/> decides where to cut by looking
/// at the whole path: it halves a step until the worst width error anywhere is inside a
/// tolerance, and then fills each run of equal-coloured pieces as a single path. Both of those
/// are why its mark does not depend on how often the tablet reported, and both are why it
/// cannot be drawn while the stroke is still arriving -- see <c>#2</c>.
/// </para>
/// <para>
/// This engine decides nothing globally. The cut positions are the readings, each piece is
/// filled on its own, and the operations for a stroke are a function of its readings in order.
/// So the same stroke drawn in one call and drawn a reading at a time issues the same drawing
/// commands in the same sequence, whatever the pen's packets were grouped into.
/// </para>
/// <para>
/// <b>What it gives up is independence from sample density.</b> Inserting readings changes the
/// width approximation, which colour each piece is filled with, and how much the pieces
/// overlap. The same gesture reported twice as often makes a different mark. That is the fault
/// <c>Taper.Pieces</c> cuts at fixed distances to avoid, and its warning stands -- the
/// difference here is that it is the stated behaviour of an engine somebody chose rather than
/// something that happened to them.
/// </para>
/// </remarks>
public static class SampleTapers
{
    /// <summary>
    /// Draws the whole stroke, and answers how many pieces that took.
    /// </summary>
    /// <remarks>
    /// One call into the same routine the live path uses, starting from nothing laid. That is
    /// the whole of how the two are kept identical: there is one sequence of operations and
    /// two ways of asking for it.
    /// </remarks>
    public static int Draw(Surface surface, InkTransform transform, Brush brush, Stroke stroke) =>
        Lay(surface, transform, brush, stroke, 0);

    /// <summary>
    /// Lays the pieces of <paramref name="stroke"/> that are not among the first
    /// <paramref name="laid"/> readings, and answers how many that was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first reading lays a dot, and every reading after it lays one taper from the
    /// reading before. So the number of pieces equals the number of readings, and the piece
    /// for reading <c>i</c> depends only on readings <c>i - 1</c> and <c>i</c>.
    /// </para>
    /// <para>
    /// <b>A tap is a dot.</b> One reading, or a pen that pressed and never moved, lays the dot
    /// and stops -- which is what the stamping engine does with the same stroke, and they have
    /// to agree: a stroke is a stroke whichever engine draws it.
    /// </para>
    /// <para>
    /// <b>A stationary reading still lays a piece.</b> Two readings at one position with
    /// different pressures produce a taper of zero length, which <see cref="Taper.Outline"/>
    /// answers as a circle of the larger radius. It is drawn rather than skipped because
    /// skipping it would make the mark depend on whether the hand moved between two packets,
    /// which is not a thing a brush should notice.
    /// </para>
    /// </remarks>
    internal static int Lay(
        Surface surface, InkTransform transform, Brush brush, Stroke stroke, int laid)
    {
        if (brush.Buildup != Buildup.PerStamp)
        {
            throw new NotSupportedException(
                $"{Engine.SampleTaper} is implemented for {Buildup.PerStamp} only, and this "
                + $"brush asks for {brush.Buildup}. Holding a stroke back to composite it once "
                + "needs a surface of its own and a batch path that matches, neither of which "
                + "is here yet -- refused rather than quietly drawn the other way.");
        }

        using var path = new SKPath { FillType = SKPathFillType.Winding };

        var pieces = 0;

        // The dot. Only ever laid once, which is what `laid` is for: a live caller hands the
        // same stroke back grown, and the readings it has already been given must not be
        // drawn again.
        if (laid == 0 && stroke.Count > 0)
        {
            Fill(surface, transform, brush, stroke, path, at: 0, from: 0);

            pieces++;
        }

        for (var at = Math.Max(1, laid); at < stroke.Count; at++)
        {
            Fill(surface, transform, brush, stroke, path, at, from: at - 1);

            pieces++;
        }

        return pieces;
    }

    /// <summary>
    /// One piece: the taper from reading <paramref name="from"/> to reading
    /// <paramref name="at"/>, in the colour at <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// The far end's colour, as the batch engine also takes it. A filled shape has one alpha
    /// and there is nothing to ramp across it, so one of the two ends has to be chosen, and
    /// choosing the newer one means a piece never changes after it is laid.
    /// </remarks>
    private static void Fill(
        Surface surface, InkTransform transform, Brush brush, Stroke stroke,
        SKPath path, int at, int from)
    {
        var start = Placed(stroke, from);
        var end = Placed(stroke, at);

        var (fromX, fromY) = transform.ToSurface(start.X, start.Y);
        var (toX, toY) = transform.ToSurface(end.X, end.Y);

        Taper.Outline(path,
            new SKPoint((float)fromX, (float)fromY),
            (float)(brush.DiameterAt(stroke, start) / 2 * transform.ScaleX),
            new SKPoint((float)toX, (float)toY),
            (float)(brush.DiameterAt(stroke, end) / 2 * transform.ScaleX));

        using var paint = new SKPaint
        {
            Color = brush.ColourAt(stroke, end),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        surface.Canvas.DrawPath(path, paint);
    }

    /// <summary>A reading, as the placement the brush's controls are asked about.</summary>
    /// <remarks>
    /// Fraction zero, so the width and colour are the ones at that reading rather than
    /// interpolated towards the next. <c>DiameterAt</c> clamps the segment it looks ahead to,
    /// so the last reading answers for itself.
    /// </remarks>
    private static Placement Placed(Stroke stroke, int at) =>
        new(stroke.Points[at].X, stroke.Points[at].Y, at, 0);
}

/// <summary>
/// A <see cref="Engine.SampleTaper"/> stroke being drawn while it is still arriving.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="Wet"/>, and much smaller than it, because there is nothing to
/// hold. Each piece is composited onto the target as it arrives and the target is correct
/// after every reading -- so there is no stroke surface, no alpha-darkening between pieces,
/// and nothing that happens at the lift.
/// </para>
/// <para>
/// All it keeps is how many readings it has already been given.
/// </para>
/// </remarks>
public sealed class Trail : ILive
{
    private readonly Brush _brush;
    private readonly Surface _target;
    private readonly InkTransform _transform;

    private int _laid;

    public Trail(Brush brush, Surface target, InkTransform transform)
    {
        // Refused rather than approximated, the way Wet refuses a taper. An engine drawn as
        // something it is not is the same ink laid a completely different way, with nothing
        // saying so.
        if (brush.Engine != Engine.SampleTaper)
        {
            throw new NotSupportedException(
                $"{nameof(Trail)} draws {Engine.SampleTaper}, and this brush asks for "
                + $"{brush.Engine}. {nameof(Wet)} draws {Engine.Stamps}; no live renderer "
                + $"draws {Engine.Taper} yet.");
        }

        _brush = brush;
        _target = target;
        _transform = transform;
    }

    public int Extend(Stroke sofar)
    {
        // The same contract Wet states, checked the same way: what is handed over has to
        // begin with everything handed over before it. Given something shorter this would lay
        // nothing and silently stop drawing, which is worse than refusing.
        if (sofar.Count < _laid)
        {
            throw new ArgumentException(
                $"a stroke being drawn can only grow: this had {_laid} readings and has been "
                + $"given {sofar.Count}. Use a new {nameof(Trail)} for a new stroke.",
                nameof(sofar));
        }

        var pieces = SampleTapers.Lay(_target, _transform, _brush, sofar, _laid);

        _laid = sofar.Count;

        return pieces;
    }

    /// <summary>The target, which is already correct.</summary>
    public void PresentOnto(Surface display)
    {
        display.Canvas.Clear(SKColors.Transparent);

        using var beneath = _target.Snapshot();

        display.Canvas.DrawImage(beneath, 0, 0);
    }

    /// <summary>Nothing. The ink went straight onto the target as it arrived.</summary>
    public void Finish()
    {
    }

    public void Dispose()
    {
    }
}
