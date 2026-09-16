using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StrokeFieldGuide.Canvas;

/// <summary>
/// The outline of the mark the brush would make, drawn where the pen is.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is not on the surface, and that is the whole technique.</b> Ink is permanent: a
/// cursor drawn onto the surface has to be taken off again, which means keeping a copy of
/// what was underneath and putting it back before every move. That is how it was done when a
/// window had one buffer, it is where "the cursor left a trail" came from, and none of it is
/// necessary. A cursor is a view of where the pen is, so it belongs in the view.
/// </para>
/// <para>
/// So this is a control laid over the one showing the surface. It draws after the surface is
/// presented and before anything else, it is never composited into the document, and clearing
/// it is a repaint rather than a restore.
/// </para>
/// <para>
/// <b>Two passes, light under dark.</b> A single dark outline vanishes over dark ink, which
/// is exactly where a brush cursor is most needed -- over the stroke you are about to add to.
/// A pale line drawn thick underneath a dark line drawn thin gives an edge that reads on any
/// ground, and costs one more call.
/// </para>
/// <para>
/// Everything below is in device independent units, because that is what a
/// <see cref="DrawingContext"/> works in. Turning a pen's desktop pixels into these is the
/// caller's job, and <see cref="PenPad"/> does it in one place.
/// </para>
/// </remarks>
public sealed class NibCursor : Control
{
    private static readonly IPen Under = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), 3);
    private static readonly IPen Over = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 20, 20, 20)), 1);

    private Point _at;
    private double _long;
    private double _ratio = 1;
    private double _degrees;
    private bool _showing;

    public NibCursor()
    {
        // The cursor must not take the pen's input away from the control underneath it.
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Puts the outline at a position, with a size and a shape.
    /// </summary>
    /// <param name="at">Where the nib is, in this control's own units.</param>
    /// <param name="longAxis">The nib's long diameter, in the same units.</param>
    /// <param name="ratio">The short axis over the long one. One is a circle.</param>
    /// <param name="degrees">Which way the long axis points, as an angle to draw at.</param>
    public void Show(Point at, double longAxis, double ratio, double degrees)
    {
        _at = at;
        _long = longAxis;
        _ratio = ratio;
        _degrees = degrees;
        _showing = true;

        InvalidateVisual();
    }

    /// <summary>Takes it off, which is a repaint and not a restore.</summary>
    public void Hide()
    {
        if (!_showing) return;

        _showing = false;

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (!_showing || _long <= 0) return;

        var radius = _long / 2;

        using var _ = context.PushTransform(
            Matrix.CreateRotation(_degrees * Math.PI / 180)
            * Matrix.CreateTranslation(_at.X, _at.Y));

        context.DrawEllipse(null, Under, new Point(0, 0), radius, radius * _ratio);
        context.DrawEllipse(null, Over, new Point(0, 0), radius, radius * _ratio);

        // A tick along the long axis, because a nearly round nib has an orientation that the
        // outline alone does not show and a reader turning the pen wants to see it move.
        context.DrawLine(Under, new Point(radius * 0.35, 0), new Point(radius, 0));
        context.DrawLine(Over, new Point(radius * 0.35, 0), new Point(radius, 0));
    }
}
