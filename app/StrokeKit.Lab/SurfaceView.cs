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
/// Shows a surface, at a zoom and a pan, with the guarantees stage one exists to keep.
/// <para>
/// The division of labour is the point. Every decision that affects a pixel — the scale, the
/// offset, which sampling — is made by <see cref="Presenter"/>, which is a raster operation
/// with no window behind it and is checked by reading pixels back. This control's only jobs
/// are to work out how many physical pixels it has, hand that to the presenter, and put the
/// result on screen without touching it.
/// </para>
/// <para>
/// That last part is what the explicit <see cref="Control.Width"/> in device independent
/// units below is for: sized so the presented bitmap maps one physical pixel to one physical
/// pixel, Avalonia performs a copy rather than a scale, and nothing it does can soften what
/// the presenter decided.
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

    public SurfaceView(Surface art)
    {
        _art = art;
        ClipToBounds = true;
        Focusable = true;
    }

    public View View { get; private set; } = View.At(1);

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
        var width = (int)Math.Round(Bounds.Width * RenderScale);
        var height = (int)Math.Round(Bounds.Height * RenderScale);

        SetView(View.PannedTo(
            (width - _art.PixelWidth * View.Zoom) / 2,
            (height - _art.PixelHeight * View.Zoom) / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _dragging = true;
        _dragFrom = e.GetPosition(this);
        _panFrom = (View.PanX, View.PanY);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;

        // The drag arrives in device independent units and the pan is in physical pixels, so
        // the scale has to be applied here. Rounding happens inside the view: a pan that is
        // not a whole number of physical pixels puts every surface pixel halfway between two
        // display pixels, and the whole image softens without the zoom having changed.
        var moved = e.GetPosition(this) - _dragFrom;

        SetView(View.PannedTo(
            _panFrom.X + moved.X * RenderScale,
            _panFrom.Y + moved.Y * RenderScale));
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
        var width = (int)Math.Round(Bounds.Width * scale);
        var height = (int)Math.Round(Bounds.Height * scale);
        if (width <= 0 || height <= 0) return;

        KeepTheCentreAcrossChanges(scale, width, height);
        EnsurePresentationBitmap(width, height);

        Presenter.Present(_art, _presentedCanvas!, View);
        CopyToShown(width, height);

        // Drawn at its physical size divided by the scale, so one pixel of the presented
        // bitmap covers exactly one physical pixel of the display. Any other destination
        // rectangle here would hand Avalonia a resampling job and undo the presenter's work.
        context.DrawImage(
            _shown!,
            new Rect(0, 0, width, height),
            new Rect(0, 0, width / scale, height / scale));
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

        if (!first)
        {
            // Where the old viewport's middle was, in surface coordinates, and where the new
            // one's middle has to be for it to stay there.
            var (surfaceX, surfaceY) = View.ToSurface(_lastWidth / 2.0, _lastHeight / 2.0);

            View = View.PannedTo(
                width / 2.0 - surfaceX * View.Zoom,
                height / 2.0 - surfaceY * View.Zoom);
        }

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
