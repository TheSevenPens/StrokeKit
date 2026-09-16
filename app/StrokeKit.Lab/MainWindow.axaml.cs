using StrokeFieldGuide.Canvas;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StrokeFieldGuide.Figures;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Lab;

public partial class MainWindow : Window
{
    /// <summary>
    /// The document. A thousand pixels square, fixed, because stage one is about showing a
    /// surface correctly and a resizable one would add a second subject.
    /// </summary>
    private readonly Surface _art = Surface.CreateExactly(1000, 1000, 1000, 1000);

    private readonly SurfaceView _view;

    /// <summary>
    /// Set while the scroll bars are being written to from the view, so that the value
    /// changes this causes are not read back as the reader having moved one.
    /// </summary>
    private bool _syncing;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _view = new SurfaceView(_art);
        _view.ViewChanged += (_, _) => ShowState();

        // The frame time belongs to the frame that just finished, so it is read after one
        // rather than before the next. Everything else is known as soon as the view is set.
        _view.Rendered += (_, _) => ShowStatus();

        this.FindControl<Panel>("Host")!.Children.Add(_view);

        this.FindControl<Button>("DrawDemo")!.Click += (_, _) => { Demo.Draw(_art); Redraw(); };
        this.FindControl<Button>("DrawStroke")!.Click += (_, _) => { SyntheticStrokes.Draw(_art); Redraw(); };
        this.FindControl<Button>("ClearIt")!.Click += (_, _) => { EmptyDocument(); Redraw(); };

        this.FindControl<Button>("ZoomIn")!.Click += (_, _) => _view.SetView(_view.View.In());
        this.FindControl<Button>("ZoomOut")!.Click += (_, _) => _view.SetView(_view.View.Out());
        this.FindControl<Button>("ZoomReset")!.Click += (_, _) => _view.SetView(View.At(1, _view.View.PanX, _view.View.PanY));
        this.FindControl<Button>("FitIt")!.Click += (_, _) => _view.FitSurface();
        this.FindControl<Button>("Centre")!.Click += (_, _) => _view.CentreOnSurface();

        this.FindControl<ScrollBar>("ScrollX")!.ValueChanged += (sender, _) =>
            Scrolled(horizontally: true, ((ScrollBar)sender!).Value);

        this.FindControl<ScrollBar>("ScrollY")!.ValueChanged += (sender, _) =>
            Scrolled(horizontally: false, ((ScrollBar)sender!).Value);

        // A key released while the window is not in front never arrives, and the cursor would
        // stay a hand until the reader pressed space again just to let it go.
        Deactivated += (_, _) => _view.HandPanning = false;

        Opened += (_, _) => { Demo.Draw(_art); _view.FitSurface(); _view.Focus(); ShowState(); };

        // The document is this window's, so this window releases it. A surface holds an
        // SKSurface, which holds pixels that are not the garbage collector's to hurry.
        Closed += (_, _) => _art.Dispose();
    }

    /// <summary>
    /// Clearing leaves an empty document rather than nothing at all.
    /// <para>
    /// A surface cleared to transparent is a real and useful thing -- it is what any layer
    /// above the bottom one looks like -- but as the whole document it reads as the
    /// application having lost the page rather than as the page being blank. Filling with
    /// paper keeps the document's own edges visible, which is the one thing a reader needs in
    /// order to tell an empty document from a broken presentation.
    /// </para>
    /// </summary>
    private void EmptyDocument() => _art.Canvas.Clear(Demo.Paper);

    private void Redraw()
    {
        _view.InvalidateVisual();
        ShowState();
    }

    private void Scrolled(bool horizontally, double value)
    {
        if (_syncing) return;

        var view = _view.View;

        _view.SetView(horizontally
            ? view.PannedTo(Scrolling.PanFor(value), view.PanY)
            : view.PannedTo(view.PanX, Scrolling.PanFor(value)));
    }

    private void ShowState()
    {
        ShowScrollBars();
        ShowStatus();
    }

    /// <summary>
    /// The bars, from the view. Written here and never read back except when the reader moves
    /// one: the view is what is true, and a scroll bar is a second way of saying it.
    /// </summary>
    private void ShowScrollBars()
    {
        var zoom = _view.View.Zoom;

        _syncing = true;

        try
        {
            Apply(
                this.FindControl<ScrollBar>("ScrollX")!,
                Scrolling.For(_art.PixelWidth * zoom, _view.ViewportWidth, _view.View.PanX));

            Apply(
                this.FindControl<ScrollBar>("ScrollY")!,
                Scrolling.For(_art.PixelHeight * zoom, _view.ViewportHeight, _view.View.PanY));
        }
        finally
        {
            _syncing = false;
        }

        static void Apply(ScrollBar bar, Scrolling.Bar numbers)
        {
            // Widened before the value is set and narrowed after, because a range that does
            // not yet contain the new value would clamp it on the way through and the bar
            // would end up somewhere the view never asked for.
            bar.Minimum = Math.Min(bar.Minimum, numbers.Minimum);
            bar.Maximum = Math.Max(bar.Maximum, numbers.Maximum);

            bar.ViewportSize = numbers.Viewport;
            bar.Value = numbers.Value;

            bar.Minimum = numbers.Minimum;
            bar.Maximum = numbers.Maximum;

            bar.LargeChange = Math.Max(1, numbers.Viewport * 0.9);
            bar.SmallChange = Math.Max(1, numbers.Viewport * 0.1);

            bar.IsEnabled = numbers.Needed;
        }
    }

    /// <summary>
    /// The numbers that decide whether what is on screen is right, shown rather than
    /// inferred. Somebody reading the page about a surface having two sizes should be able
    /// to watch both of them here.
    /// </summary>
    private void ShowStatus()
    {
        var scale = _view.RenderScale;
        var zoom = _view.View.Zoom;

        var describe = zoom >= 1
            ? $"{zoom:0}x  one surface pixel is {zoom:0}x{zoom:0} display pixels, nearest"
            : $"{zoom:0.###}x  minified, linear with mipmaps";

        this.FindControl<TextBlock>("Status")!.Text =
            $"surface {_art.PixelWidth}x{_art.PixelHeight} px   "
            + $"zoom {describe}   "
            + $"pan {_view.View.PanX:0},{_view.View.PanY:0} display px   "
            + $"render scaling {scale:0.####}   "
            + $"viewport {_view.ViewportWidth}x{_view.ViewportHeight} display px   "
            + $"frame {_view.LastFrameMilliseconds:0.0} ms";
    }
}
