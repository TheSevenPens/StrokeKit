using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SkiaSharp;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;
using StrokeKit.Views;

namespace StrokeKit.Avalonia;

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
public sealed class PenPad : Decorator, IDisposable
{
    private Surface _art;
    private readonly SurfaceView _view;
    private readonly NibCursor _cursor = new();

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

        // The cursor sits over the view rather than in it. Ink is permanent and a cursor is
        // not, so they do not share a surface -- see NibCursor for why that is the whole
        // technique rather than an implementation detail.
        Child = new Panel { Children = { _view, _cursor } };

        // One pointer on screen, not two. The system's arrow beside a nib outline is the
        // commonest thing that makes a brush cursor look wrong.
        Cursor = new Cursor(StandardCursorType.None);
    }

    /// <summary>The surface being drawn on. Replaced when the pad grows, so do not hold it.</summary>
    public Surface Surface => _art;

    public SurfaceView View => _view;

    /// <summary>
    /// Raised before the pad grows, while the surface it is replacing is still alive.
    /// </summary>
    /// <remarks>
    /// The one chance anything holding that surface has to finish with it. A live stroke is
    /// laying ink onto it, and <see cref="Grew"/> arrives too late to be told.
    /// </remarks>
    public event EventHandler? Growing;

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

    /// <summary>
    /// Shows the nib the brush would lay, where the pen is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conversions again, and the sizes need one the positions do not. A pen reports
    /// desktop pixels, so the box's own corner comes off; then the control draws in device
    /// independent units, so what is left is divided by the window's scaling.
    /// </para>
    /// <para>
    /// The nib's size arrives in <b>surface</b> pixels, which is what a brush works in. On
    /// screen it is that times the view's zoom, and then in units it is that over the
    /// scaling -- so the cursor grows when the reader zooms in, which is what makes it a
    /// picture of the mark rather than a fixed decoration.
    /// </para>
    /// </remarks>
    /// <param name="desktopX">Where the pen is, as the session reports it.</param>
    /// <param name="surfaceDiameter">The nib's long diameter, in surface pixels.</param>
    public void ShowNib(double desktopX, double desktopY,
                        double surfaceDiameter, double ratio, double degrees)
    {
        var box = _view.PointToScreen(new Point(0, 0));
        var scale = _view.RenderScale;

        if (scale <= 0) return;

        _cursor.Show(
            new Point((desktopX - box.X) / scale, (desktopY - box.Y) / scale),
            surfaceDiameter * _view.View.Zoom / scale,
            ratio,
            degrees);
    }

    /// <summary>Takes the nib off, for a pen that has left the tablet.</summary>
    public void HideNib() => _cursor.Hide();

    /// <summary>
    /// Whether a position reported on the desktop is over this pad.
    /// </summary>
    /// <remarks>
    /// A tablet session reports the pen wherever it is, not only where the drawing is. An
    /// application that acts on every reading acts on the ones made while the reader is
    /// pressing a button -- which is how tapping "Arm" with the pen armed the recorder and
    /// then instantly recorded that same tap as a two-reading stroke, and looked from the
    /// outside like the button un-arming itself.
    /// </remarks>
    public bool Covers(double desktopX, double desktopY)
    {
        var box = _view.PointToScreen(new Point(0, 0));
        var scale = _view.RenderScale;

        return desktopX >= box.X && desktopY >= box.Y
               && desktopX < box.X + Bounds.Width * scale
               && desktopY < box.Y + Bounds.Height * scale;
    }

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

        // Said before the old surface is released, not after.
        //
        // Grew announced a surface that had already been disposed, which was harmless while
        // the only listeners redrew from scratch. It stops being harmless the moment anything
        // is mid-stroke on this pad: a live stroke holds the surface it is laying ink onto,
        // and by the time it heard, that surface was gone. Told first, a listener can finish
        // or abandon what it is drawing while the target is still there.
        Growing?.Invoke(this, EventArgs.Empty);

        _art.Dispose();
        _art = grown;

        _view.Show(_art);
        // Qualified: this class has a View of its own, which is the control and not the
        // view transform.
        _view.SetView(Views.View.At(1));

        Grew?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Releases the surface this pad made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A surface holds an <c>SKSurface</c>, which holds pixels the garbage collector is in no
    /// hurry over. This pad creates one, replaces it as it grows, and disposed every
    /// replacement — but had no way to release the last one, so the final surface of every
    /// pad ever shown was simply left. The recorder's close handler closed its pen session
    /// and nothing else.
    /// </para>
    /// <para>
    /// <b>Owner-driven, and not on detach.</b> A pad taken off screen and put back is an
    /// ordinary thing; disposing on that would destroy a drawing for being briefly invisible.
    /// Whoever created the pad says when it is finished with.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        _art.Dispose();
    }
}
