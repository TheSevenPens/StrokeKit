using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Lab;

/// <summary>
/// Shows a surface, at a zoom and a pan, with the guarantees this part exists to keep.
/// <para>
/// The division of labour is the point. Every decision that affects a pixel — the scale, the
/// offset, which sampling — is made by <see cref="Presenter"/>, and every conversion between
/// display pixels and the windowing system's units is made by <see cref="Presentation"/>.
/// Both are raster or arithmetic with no window behind them, and both are checked. What is
/// left here is the part that genuinely needs Avalonia: asking for the scaling, allocating a
/// bitmap, and handing the result over.
/// </para>
/// <para>
/// A control can be exercised without a screen -- Avalonia has a headless platform -- but
/// nothing here is set up to do it, so for now a decision left inside this file is a decision
/// nothing checks. That is the reason for the split and for how short this file is. It is
/// also why the split is worth keeping even once there is a control-level suite: arithmetic
/// is checkable at no setup cost, and a window is not.
/// </para>
/// </summary>
public sealed class SurfaceView : Control
{
    private readonly Surface _art;

    private WriteableBitmap? _shown;

    private bool _dragging;
    private Point _dragFrom;
    private (double X, double Y) _panFrom;

    // What the viewport was the last time anything was drawn, so a change to either can be
    // noticed. Moving a window between monitors changes the scaling without changing the
    // size in device independent units, and resizing changes the size without changing the
    // scaling, so neither one alone is enough to watch.
    private double _lastScale;
    private int _lastWidth;
    private int _lastHeight;

    private bool _handPanning;

    public SurfaceView(Surface art)
    {
        _art = art;
        ClipToBounds = true;
        Focusable = true;
    }

    public View View { get; private set; } = View.At(1);

    /// <summary>The viewport, in physical display pixels. Zero until the first frame.</summary>
    public int ViewportWidth { get; private set; }

    public int ViewportHeight { get; private set; }

    /// <summary>
    /// How long the last frame took this control, in milliseconds.
    /// <para>
    /// Shown in the status line with everything else that decides whether what is on screen
    /// is right, because a presentation that is correct and slow is one a reader will call
    /// broken. It also separates the two halves of a slow frame: this number is the work
    /// this control does, and the difference between it and what the application actually
    /// achieves is the windowing system's.
    /// </para>
    /// </summary>
    public double LastFrameMilliseconds { get; private set; }

    /// <summary>
    /// Whether the space bar is held.
    /// <para>
    /// A drag pans either way for now, because there is no other tool competing for it. The
    /// key is here because it is the idiom a reader already has, and because the moment a
    /// brush exists the drag belongs to the brush and this becomes the only way to pan.
    /// </para>
    /// </summary>
    public bool HandPanning
    {
        get => _handPanning;
        set
        {
            if (_handPanning == value) return;

            _handPanning = value;
            Cursor = new Cursor(value ? StandardCursorType.Hand : StandardCursorType.Arrow);
        }
    }

    /// <summary>Physical pixels per device independent unit, as this window reports it.</summary>
    public double RenderScale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    public event EventHandler? ViewChanged;

    public void SetView(View view)
    {
        View = view;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void CentreOnSurface()
    {
        var (width, height) = Presentation.PixelSize(Bounds.Width, Bounds.Height, RenderScale);

        SetView(View.Centred(View.Zoom, _art.PixelWidth, _art.PixelHeight, width, height));
    }

    /// <summary>Zoom out, if needed, until the whole surface is in the window.</summary>
    public void FitSurface()
    {
        var (width, height) = Presentation.PixelSize(Bounds.Width, Bounds.Height, RenderScale);

        SetView(View.Fitting(_art.PixelWidth, _art.PixelHeight, width, height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();

        _dragging = true;
        _dragFrom = e.GetPosition(this);
        _panFrom = (View.PanX, View.PanY);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;

        // The drag arrives in device independent units and the pan is in physical pixels.
        // Rounding happens inside the view.
        var moved = e.GetPosition(this) - _dragFrom;
        var scale = RenderScale;

        SetView(View.PannedTo(
            _panFrom.X + Presentation.PanFromDrag(moved.X, scale),
            _panFrom.Y + Presentation.PanFromDrag(moved.Y, scale)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var scale = RenderScale;

        // How many physical pixels this control actually occupies. Everything below is in
        // those; nothing below is in device independent units.
        var (width, height) = Presentation.PixelSize(Bounds.Width, Bounds.Height, scale);
        if (width <= 0 || height <= 0) return;

        ViewportWidth = width;
        ViewportHeight = height;

        KeepTheCentreAcrossChanges(scale, width, height);
        EnsurePresentationBitmap(width, height);

        PresentInto(_shown!);

        // One pixel of the presented bitmap over exactly one physical pixel of the display.
        // Any other destination rectangle here — the control's own bounds, most temptingly —
        // hands Avalonia a resampling job and undoes the presenter's work.
        var (destinationWidth, destinationHeight) = Presentation.Destination(width, height, scale);

        context.DrawImage(
            _shown!,
            new Rect(0, 0, width, height),
            new Rect(0, 0, destinationWidth, destinationHeight));

        LastFrameMilliseconds = clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// When the viewport changes, hold the surface point that was in the middle of it.
    /// <para>
    /// Dragging a window to a monitor at a different scaling is the case that makes this
    /// necessary and is also the one that catches a pan kept in display pixels: the same
    /// number of display pixels is a different distance on the new monitor, so the image
    /// jumps and part of it leaves the window. Resizing does the same for a different
    /// reason. Holding the centre is what a reader expects and what stops either from
    /// looking like a fault in the presentation.
    /// </para>
    /// <para>
    /// The status line is refreshed from here too. It reports the scaling and the viewport,
    /// and both of those can change without anything touching the view -- which left it
    /// reporting the previous monitor's numbers.
    /// </para>
    /// </summary>
    private void KeepTheCentreAcrossChanges(double scale, int width, int height)
    {
        var first = _lastWidth == 0 && _lastHeight == 0;
        if (!first && scale == _lastScale && width == _lastWidth && height == _lastHeight) return;

        if (!first) View = Presentation.KeepingCentre(View, _lastWidth, _lastHeight, width, height);

        _lastScale = scale;
        _lastWidth = width;
        _lastHeight = height;

        // Posted rather than raised. This runs inside the render pass, and the handler
        // writes the status text, which invalidates that text block -- and Avalonia refuses
        // a visual invalidated while it is rendering. Raising it directly crashed the
        // application on the first resize with "Visual was invalidated during the render
        // pass". Guarding against re-invalidating this control was not enough, because the
        // handler touches a different one.
        Dispatcher.UIThread.Post(() => ViewChanged?.Invoke(this, EventArgs.Empty));
    }

    private void EnsurePresentationBitmap(int width, int height)
    {
        if (_shown is not null && _shown.PixelSize.Width == width && _shown.PixelSize.Height == height) return;

        _shown?.Dispose();

        // Bgra8888 because that is what the windowing system wants. Presenting straight into
        // it below is then a plain raster draw rather than a channel swap on every frame.
        _shown = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
    }

    /// <summary>
    /// Presents into the bitmap the windowing system is about to show, rather than into one
    /// of our own that is then copied into it.
    /// <para>
    /// A surface built over the locked buffer is still a surface, so the presenter is
    /// unchanged and everything checked about it still holds. What goes is a copy of the
    /// whole viewport on every frame -- at 3752x1782 that is 27 megabytes per frame, moved
    /// for no reason -- and with it the row loop that moved it, whose bounds were clamped
    /// with a pair of minimums that would have hidden a size disagreement rather than
    /// reporting one.
    /// </para>
    /// </summary>
    private void PresentInto(WriteableBitmap bitmap)
    {
        using var locked = bitmap.Lock();

        var info = new SKImageInfo(
            locked.Size.Width, locked.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info, locked.Address, locked.RowBytes)
            ?? throw new InvalidOperationException("could not draw into the presented bitmap");

        Presenter.Present(_art, surface.Canvas, View);
    }
}
