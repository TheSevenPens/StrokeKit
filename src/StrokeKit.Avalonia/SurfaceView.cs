using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using StrokeKit.Surfaces;
using StrokeKit.Views;

namespace StrokeKit.Avalonia;

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
[SuppressMessage("Usage", "CA1001:Types that own disposable fields should be disposable",
    Justification =
        "The bitmap this owns is released in OnDetachedFromVisualTree, which is a control's " +
        "lifetime. Implementing IDisposable would say the caller should dispose the control, " +
        "and nothing in Avalonia does that -- so it would add a second release path that " +
        "nobody calls and imply an ownership the visual tree actually has. The other " +
        "disposable here, the surface, is borrowed and deliberately never disposed.")]
public sealed class SurfaceView : Control
{
    private Surface _art;

    private WriteableBitmap? _shown;

    private bool _dragging;
    private int _dragPointer;
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

    /// <summary>Whether a "the frame is done" notification is already on its way.</summary>
    private bool _reporting;

    public SurfaceView(Surface art)
    {
        _art = art;
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>The surface being shown. Exposed so a test can compare a frame against it.</summary>
    public Surface Surface => _art;

    /// <summary>
    /// Shows a different surface. The one being replaced is not disposed, for the same
    /// reason this control never disposes one: it belongs to whoever passed it in.
    /// <para>
    /// A surface cannot be resized, only replaced, so an application whose surface follows
    /// its window has to hand over a new one -- which is what
    /// <see cref="Surface.Grown(int, int, double, double)"/> answers. The view is left
    /// alone: where the reader had got to is not something a bigger surface should disturb.
    /// </para>
    /// </summary>
    public void Show(Surface art)
    {
        if (ReferenceEquals(_art, art)) return;

        _art = art;

        InvalidateVisual();
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
    /// Whether a drag on this control moves the view. True unless an application says
    /// otherwise.
    /// <para>
    /// A control that shows a surface cannot know what a drag across it is for. Where there
    /// is nothing else competing, a drag panning is the useful default and is what the Lab
    /// wants. Where the drag is the subject -- the recorder's strip, which exists to be drawn
    /// on -- panning under the pen moves the drawing away from the tip while it is being
    /// made, so the mark lands where the pen was rather than where it is.
    /// </para>
    /// <para>
    /// Panning with the space bar held stays available either way, because it is a separate
    /// gesture and does not compete with drawing.
    /// </para>
    /// </summary>
    public bool DragPans { get; set; } = true;

    /// <summary>
    /// Whether the space bar is held, while this control has the focus.
    /// <para>
    /// Handled by the canvas rather than by the window. Intercepting it at the window was
    /// simpler and took the space bar away from every other control in the application: a
    /// focused button could not be pressed with it, and a text field -- the moment there is
    /// one -- could not have a space typed into it. A canvas may own the space bar while the
    /// reader is working on the canvas, and not otherwise.
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

    /// <summary>
    /// Raised after a frame, so that anything reporting on the frame reports on the one just
    /// drawn.
    /// <para>
    /// <see cref="ViewChanged"/> fires when the view is set, which is before the frame that
    /// results from it, so a status line refreshed from that alone shows the time the
    /// <em>previous</em> frame took. During a drag every reading is one frame stale, which is
    /// a small lie in the one number a reader would use to judge whether a frame is cheap.
    /// </para>
    /// <para>
    /// Posted and coalesced: raising it inside the render pass is what crashed the
    /// application the first time, and one notification per frame is enough however many
    /// times the view changed to produce it.
    /// </para>
    /// </summary>
    public event EventHandler? Rendered;

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            HandPanning = true;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            HandPanning = false;
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    /// <summary>The release of a key held while the focus moves away never arrives here.</summary>
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        HandPanning = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();

        // Nothing to start if a drag is not this control's to act on. The press is still
        // taken -- it moved the focus -- but no capture is taken with it, so whatever the
        // drag is for sees the whole of it.
        if (!DragPans && !HandPanning) return;

        // The left button, and only it. Every press starting a drag means a right-click
        // menu or a barrel button pans the canvas out from under whatever it was for, and
        // the pan a second pointer contributes is measured from the first one's origin.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        _dragPointer = e.Pointer.Id;
        _dragFrom = e.GetPosition(this);
        _panFrom = (View.PanX, View.PanY);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging || e.Pointer.Id != _dragPointer) return;

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
        EndGesture();
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// A pointer can be taken away mid-gesture: another control captures it, the window
    /// loses activation, a tablet leaves proximity. None of those arrive as a release.
    /// </summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndGesture();

    /// <summary>
    /// Ends a drag that is in progress, without waiting for a release that may never come.
    /// <para>
    /// Left running, the next move the control sees is measured from where the pointer went
    /// down, so a gesture interrupted and resumed elsewhere moves the drawing by the whole
    /// distance between the two -- which reads as the canvas jumping for no reason.
    /// </para>
    /// </summary>
    public void EndGesture() => _dragging = false;

    // global::, because this namespace is called Avalonia too: inside it, a qualified name
    // beginning Avalonia resolves to StrokeKit.Avalonia and not to the framework.
    public override void Render(global::Avalonia.Media.DrawingContext context)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var scale = RenderScale;

        // Everything below is in physical pixels, and the viewport was worked out during
        // layout rather than here. A frame renders a state that was already prepared: this
        // method changes nothing that anything else reads.
        var width = ViewportWidth;
        var height = ViewportHeight;
        if (width <= 0 || height <= 0) return;

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

        if (_reporting) return;

        _reporting = true;

        Dispatcher.UIThread.Post(() =>
        {
            _reporting = false;
            Rendered?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Layout is where the viewport's size is settled, so it is where the view is adjusted
    /// to it -- before the frame that shows the result, rather than during it.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);

        AdjustToViewport(finalSize);

        return arranged;
    }

    /// <summary>
    /// A scaling change need not come with a layout change: the same window on a monitor
    /// with a different scaling is the same size in device independent units and a different
    /// number of pixels. Without this the viewport would only be noticed at the next resize.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } top) top.ScalingChanged += ScalingChanged;
    }

    private void ScalingChanged(object? sender, EventArgs e)
    {
        AdjustToViewport(Bounds.Size);
        InvalidateVisual();
    }

    private void AdjustToViewport(Size finalSize)
    {
        var scale = RenderScale;
        var (width, height) = Presentation.PixelSize(finalSize.Width, finalSize.Height, scale);

        if (width <= 0 || height <= 0) return;

        ViewportWidth = width;
        ViewportHeight = height;

        KeepTheCentreAcrossChanges(scale, width, height);
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

        // Posted rather than raised. The handler writes the status text, which invalidates
        // that text block, and this runs inside a layout pass -- and used to run inside the
        // render pass, where raising it directly crashed the application on the first resize
        // with "Visual was invalidated during the render pass". Guarding against
        // re-invalidating this control was not enough, because the handler touches another.
        Dispatcher.UIThread.Post(() => ViewChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Releases the bitmap this control allocated, and only that.
    /// <para>
    /// The surface belongs to whoever passed it in and is not this control's to dispose. A
    /// control that freed what it was lent would be a worse bug than the leak.
    /// </para>
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.ScalingChanged -= ScalingChanged;

        base.OnDetachedFromVisualTree(e);

        _shown?.Dispose();
        _shown = null;
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
