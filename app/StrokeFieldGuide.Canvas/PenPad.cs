using Avalonia;
using Avalonia.Controls;
using SkiaSharp;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Canvas;

/// <summary>
/// A surface you can draw on with a pen, sized to itself, with the coordinates already
/// worked out.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the three things it does together were got wrong separately. An
/// application putting a <see cref="SurfaceView"/> on screen and a pen session beside it has
/// to know that a drag on the view is not the view's to act on, that a reported position is
/// two conversions away from a surface pixel, and that a surface smaller than its box drops
/// the marks that fall outside it. None of those is hard; all three are invisible when wrong,
/// because each produces a picture that looks like a stroke.
/// </para>
/// <para>
/// So the knowledge lives here rather than in a page telling each application to remember it.
/// What is left to the application is what the application actually knows: which brush, which
/// readings, and when.
/// </para>
/// </remarks>
public sealed class PenPad : Decorator
{
    private Surface _art;
    private readonly SurfaceView _view;

    /// <param name="width">A starting size. It grows to the box on the first frame.</param>
    public PenPad(int width = 800, int height = 400)
    {
        _art = Surface.Create(width, height);
        _art.Canvas.Clear(SKColors.Transparent);

        // The drag belongs to the pen. Left to pan, the surface slides under the nib while
        // the mark is being laid and the mark trails the tip by however far the hand moved,
        // which reads as the brush engine placing stamps badly.
        _view = new SurfaceView(_art) { DragPans = false };

        _view.Rendered += (_, _) => FitToTheBox();

        Child = _view;
    }

    /// <summary>The surface being drawn on. Replaced when the pad grows, so do not hold it.</summary>
    public Surface Surface => _art;

    public SurfaceView View => _view;

    /// <summary>Raised after the pad has grown, because the old surface is gone by then.</summary>
    public event EventHandler? Grew;

    /// <summary>
    /// The transform from where a pen is on the desktop to the surface pixel under it.
    /// </summary>
    /// <remarks>
    /// Asked for per use rather than held, because both of the things it is made from move:
    /// the window can be dragged while the pen is down, and the view is recentred whenever
    /// the box is resized or the window crosses to a monitor at another scaling.
    /// <para>
    /// A take that has to stay in one frame of reference should ask once, at the start, and
    /// keep the answer -- which is the caller's decision and not this control's.
    /// </para>
    /// </remarks>
    public InkTransform ForPen()
    {
        var box = _view.PointToScreen(new Point(0, 0));

        return InkTransform.ForPenOver(_view.View.Zoom, _view.View.PanX, _view.View.PanY,
            box.X, box.Y);
    }

    /// <summary>Wipes the surface and asks for a frame.</summary>
    public void Clear()
    {
        _art.Canvas.Clear(SKColors.Transparent);

        Redraw();
    }

    /// <summary>
    /// Asks for the surface to be presented again.
    /// </summary>
    /// <remarks>
    /// Drawing on a surface does not tell the control that shows it: the control holds the
    /// surface rather than owning what goes on it.
    /// </remarks>
    public void Redraw() => _view.InvalidateVisual();

    /// <summary>Where a reading reported on the desktop lands on this pad, in surface pixels.</summary>
    public (double X, double Y) Under(Reading reading) => ForPen().ToSurface(reading.X, reading.Y);

    /// <summary>
    /// Grows the surface until it covers the box, so anywhere the pen can be pressed is
    /// somewhere a mark can land.
    /// </summary>
    /// <remarks>
    /// A surface smaller than the box leaves the rest of the box backed by nothing: the pen
    /// draws there, the engine places its stamps where it was told, and they fall outside the
    /// surface and are dropped. On screen that is a region that takes ink and a region that
    /// does not, with no line between them.
    /// <para>
    /// Grown rather than replaced, so what is already drawn survives, and the view is put
    /// back to the origin so the whole of the box is over the surface rather than most of it.
    /// </para>
    /// </remarks>
    private void FitToTheBox()
    {
        var wide = _view.ViewportWidth;
        var high = _view.ViewportHeight;

        if (wide <= 0 || high <= 0) return;
        if (_art.PixelWidth >= wide && _art.PixelHeight >= high) return;

        var grown = _art.Grown(wide, high, wide, high);

        _art.Dispose();
        _art = grown;

        _view.Show(_art);
        // Qualified: this class has a View of its own, which is the control and not the
        // view transform.
        _view.SetView(Views.View.At(1));

        Grew?.Invoke(this, EventArgs.Empty);
    }
}
