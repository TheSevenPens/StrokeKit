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
/// A control is the one place in an application that cannot run without a screen, so a
/// decision left inside one is a decision nobody can check. That is the reason for the
/// split, and it is the reason this file is as short as it is.
/// </para>
/// </summary>
public sealed class SurfaceView : Control
{
    private readonly Surface _art;

    private SKBitmap? _presented;
    private SKCanvas? _presentedCanvas;
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
        var scale = RenderScale;

        // How many physical pixels this control actually occupies. Everything below is in
        // those; nothing below is in device independent units.
        var (width, height) = Presentation.PixelSize(Bounds.Width, Bounds.Height, scale);
        if (width <= 0 || height <= 0) return;

        ViewportWidth = width;
        ViewportHeight = height;

        KeepTheCentreAcrossChanges(scale, width, height);
        EnsurePresentationBitmap(width, height);

        Presenter.Present(_art, _presentedCanvas!, View);
        CopyToShown(width, height);

        // One pixel of the presented bitmap over exactly one physical pixel of the display.
        // Any other destination rectangle here — the control's own bounds, most temptingly —
        // hands Avalonia a resampling job and undoes the presenter's work.
        var (destinationWidth, destinationHeight) = Presentation.Destination(width, height, scale);

        context.DrawImage(
            _shown!,
            new Rect(0, 0, width, height),
            new Rect(0, 0, destinationWidth, destinationHeight));
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
        if (_presented is not null && _presented.Width == width && _presented.Height == height) return;

        _presentedCanvas?.Dispose();
        _presented?.Dispose();
        _shown?.Dispose();

        // Bgra8888 because that is what the windowing system wants, which makes the copy
        // below a straight run of bytes rather than a channel swap on every frame.
        _presented = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _presentedCanvas = new SKCanvas(_presented);

        _shown = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
    }

    private void CopyToShown(int width, int height)
    {
        using var locked = _shown!.Lock();

        var source = _presented!.GetPixels();
        var rows = Math.Min(height, locked.Size.Height);
        var bytes = Math.Min(width * 4, locked.RowBytes);

        for (var row = 0; row < rows; row++)
        {
            unsafe
            {
                Buffer.MemoryCopy(
                    (byte*)source + row * _presented.RowBytes,
                    (byte*)locked.Address + row * locked.RowBytes,
                    locked.RowBytes,
                    bytes);
            }
        }
    }
}
