using Avalonia;
using StrokeFieldGuide.Canvas;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StrokeFieldGuide.Brushes;
using StrokeFieldGuide.Figures;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;
using WinPenKit;

namespace StrokeFieldGuide.Lab;

public partial class MainWindow : Window
{
    /// <summary>
    /// The document. A thousand pixels square, fixed, because stage one is about showing a
    /// surface correctly and a resizable one would add a second subject.
    /// </summary>
    private readonly Surface _art = Surface.CreateExactly(1000, 1000, 1000, 1000);

    /// <summary>
    /// What the view presents, which is not always the document.
    /// </summary>
    /// <remarks>
    /// Under once-per-stroke buildup a live stroke is held off the document until the pen
    /// lifts, so while one is in hand the right picture is the document with the uncommitted
    /// stroke over it. That is a third surface and the view shows it; the document is behind
    /// it and only changes at a lift.
    /// </remarks>
    private readonly Surface _shown = Surface.CreateExactly(1000, 1000, 1000, 1000);

    private readonly SurfaceView _view;

    private readonly PenStream _pen = new();
    private readonly NibCursor _nib = new();
    private LivePen? _live;

    /// <summary>
    /// The frame the stroke in hand is being drawn in, captured at the landing.
    /// </summary>
    /// <remarks>
    /// Held rather than asked for per reading. Both things it is made from move -- the window
    /// can be dragged while the pen is down and the view recentred when the box resizes -- and
    /// a stroke whose frame of reference moves under it bends in a way no brush asked for.
    /// </remarks>
    private InkTransform _frame;

    /// <summary>The last thing the pen said, for the status line.</summary>
    private Reading _last;
    private int _batch;

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

        // The cursor sits over the view rather than in it. Ink is permanent and a cursor is
        // not, so they do not share a surface -- see NibCursor for why that is the whole
        // technique rather than an implementation detail.
        this.FindControl<Panel>("Host")!.Children.Add(new Panel { Children = { _view, _nib } });

        WirePen();

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

        Opened += (_, _) => { Demo.Draw(_art); _view.FitSurface(); _view.Focus(); ListBackends(); Redraw(); };

        // The document is this window's, so this window releases it. A surface holds an
        // SKSurface, which holds pixels that are not the garbage collector's to hurry. The
        // pen goes first: a session outliving the surfaces it draws onto is a session that
        // can still deliver a packet into a disposed canvas.
        Closed += (_, _) =>
        {
            _pen.Dispose();
            _live?.Dispose();
            _shown.Dispose();
            _art.Dispose();
        };
    }

    /// <summary>
    /// The pen row: which backend, which brush, and connect.
    /// </summary>
    /// <remarks>
    /// Every backend is listed and the ones this machine cannot open are listed as
    /// unavailable rather than left out. A backend absent because no driver is installed and
    /// one that was never offered look the same in a shorter list.
    /// </remarks>
    private void WirePen()
    {
        // Only the presets a live stroke can be drawn with. Wet refuses an outlining engine
        // because drawing one incrementally is a real piece of work and has not been done --
        // so ink pen and marker are absent here, and PenState says so rather than leaving a
        // reader to wonder which two are missing.
        var brushes = this.FindControl<ComboBox>("PenBrush")!;

        brushes.ItemsSource = Presets.All
            .Where(preset => preset.Brush.Engine == Engine.Stamps)
            .ToList();

        brushes.DisplayMemberBinding = new Avalonia.Data.Binding("Name");
        brushes.SelectedIndex = 0;
        brushes.SelectionChanged += (_, _) => TakeBrush();

        this.FindControl<Button>("PenConnect")!.Click += (_, _) =>
        {
            if (_pen.IsRunning) Disconnect(); else Connect();
        };

        _pen.Arrived += (_, readings) =>
        {
            foreach (var reading in readings) Took(reading);
        };

        _pen.Drained += (_, many) => _batch = many;

        // A pen out of range with the tip still down never reports a zero-pressure reading,
        // and a window that has lost focus stops being told anything at all. Either leaves a
        // stroke open, and a stroke left open under once-per-stroke buildup is ink that never
        // reaches the document.
        Deactivated += (_, _) => _live?.Lift();

        PenSays("no pen open");
    }

    /// <summary>
    /// Which backends this machine can open, listed once the window exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not in the constructor.</b> Asked there, Wintab reports itself unavailable and the
    /// list settles on WM_POINTER instead -- which is a working pen, so nothing looks broken,
    /// and the highest-resolution backend on the machine is never the one offered. Wintab
    /// wants a window before it will say yes, which is the same fact that makes a Wintab
    /// session deliver no packets without one.
    /// </para>
    /// <para>
    /// Every backend is listed, and the ones this machine cannot open are listed as
    /// unavailable rather than left out. A backend absent because no driver is installed and
    /// one that was never offered look the same in a shorter list.
    /// </para>
    /// </remarks>
    private void ListBackends()
    {
        var apis = this.FindControl<ComboBox>("PenApi")!;
        var available = PenBackends.Available();

        apis.ItemsSource = PenBackends.All
            .Select(backend => new Offered(
                backend.Api,
                available.Contains(backend.Api)
                    ? backend.Name
                    : $"{backend.Name} \u2014 not available"))
            .ToList();

        apis.SelectedIndex = PenBackends.All
            .Select((backend, index) => (backend, index))
            .Where(pair => available.Contains(pair.backend.Api))
            .Select(pair => pair.index)
            .DefaultIfEmpty(0)
            .First();
    }

    /// <summary>A backend as the list shows it.</summary>
    private sealed record Offered(InputApi Api, string Name)
    {
        public override string ToString() => Name;
    }

    private void Connect()
    {
        if (this.FindControl<ComboBox>("PenApi")!.SelectedItem is not Offered chosen) return;

        // The window, not the drawing surface. A framework session listens on the control it
        // is given and a WM_POINTER one subclasses the window handle; giving a session a
        // control that is not on screen is a session that hears nothing and says nothing
        // about it.
        var failure = _pen.Start(chosen.Api, this, TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

        if (failure is not null)
        {
            PenSays($"could not start: {failure}");

            return;
        }

        // The drag belongs to the pen now. Left panning, the surface slides under the nib
        // while the mark is being laid: the mark trails the tip by however far the hand
        // moved, and it reads as the brush engine placing stamps badly rather than as the
        // view moving. PenPad has this off permanently and says why; here it is off only
        // while a session is open, because without a tablet a drag to pan is still wanted.
        //
        // Measured before it was fixed: one injected stroke left the view at pan 2269,1057
        // where it had been 694,270, with the document scrolled off the screen entirely.
        _view.DragPans = false;

        _live = new LivePen(_art, _shown);
        _live.Changed += (_, _) => { _view.InvalidateVisual(); ShowStatus(); };

        // The composited surface is in front of the reader only while there is something to
        // composite. Shown all the time instead, every frame went through a copy of the whole
        // document -- and view-transform's guarantee that at a zoom of one the presented
        // frame IS the surface stopped holding, which its own check said so in so many words.
        _live.Holding += (_, holding) => _view.Show(holding ? _shown : _art);

        TakeBrush();

        this.FindControl<Button>("PenConnect")!.Content = "Disconnect";

        ShowStatus();
    }

    private void Disconnect()
    {
        _pen.Stop();

        _view.DragPans = true;

        _live?.Lift();
        _live?.Dispose();
        _live = null;

        _nib.Hide();

        this.FindControl<Button>("PenConnect")!.Content = "Connect";

        PenSays("no pen open");

        Redraw();
    }

    /// <summary>
    /// The chosen preset, ranged against the device that is actually open.
    /// </summary>
    /// <remarks>
    /// <see cref="Brush.Ranged"/> and not the preset as written. The presets are stated
    /// against 1024 and this tablet reports 32767, so a preset handed a real pen untouched
    /// draws at a thirty-second of the pressure the hand is applying -- quietly, with every
    /// mark at the thin end of its own range.
    /// </remarks>
    private void TakeBrush()
    {
        if (_live is null) return;
        if (this.FindControl<ComboBox>("PenBrush")!.SelectedItem is not Preset preset) return;

        var range = _pen.MaxPressure;

        _live.Brush = range > 0 ? preset.Brush.Ranged((uint)range) : preset.Brush;
    }

    /// <summary>
    /// One reading, routed to the document if it is over the drawing and to the cursor either
    /// way.
    /// </summary>
    /// <remarks>
    /// <b>Over the drawing, not merely on the tablet.</b> A session reports the pen wherever
    /// it is, including while the reader is pressing a button with it. Acting on every
    /// reading is how tapping a button with an armed pen recorded that same tap as a
    /// two-reading stroke in the recorder, and looked from the outside like the button
    /// un-arming itself.
    /// </remarks>
    private void Took(Reading reading)
    {
        _last = reading;

        if (_live is null) return;

        if (!Over(reading.X, reading.Y))
        {
            // Off the drawing. A stroke in hand is lifted rather than paused: a stroke that
            // left the canvas and came back would otherwise be one stroke with a straight
            // line across the gap, which is not what the hand did.
            _live.Lift();
            _nib.Hide();

            return;
        }

        // Once per stroke, at the landing. See the note on _frame.
        if (!_live.Drawing) _frame = ForPen();

        _live.Took(reading, _frame);

        ShowNib(reading);
    }

    /// <summary>The transform from where the pen is on the desktop to a document pixel.</summary>
    private InkTransform ForPen()
    {
        var box = _view.PointToScreen(new Point(0, 0));

        return InkTransform.ForPenOver(
            _view.View.Zoom, _view.View.PanX, _view.View.PanY, box.X, box.Y);
    }

    private bool Over(double desktopX, double desktopY)
    {
        var box = _view.PointToScreen(new Point(0, 0));
        var scale = _view.RenderScale;

        return scale > 0
               && desktopX >= box.X && desktopY >= box.Y
               && desktopX < box.X + _view.Bounds.Width * scale
               && desktopY < box.Y + _view.Bounds.Height * scale;
    }

    /// <summary>
    /// The nib the brush would lay, where the pen is.
    /// </summary>
    /// <remarks>
    /// Two conversions, and the size needs one the position does not. A pen reports desktop
    /// pixels, so the box's corner comes off; then the control draws in device independent
    /// units, so what is left is divided by the window's scaling. The diameter arrives in
    /// document pixels, is multiplied by the view's zoom to reach the screen, and divided by
    /// the scaling to reach units -- so the cursor grows as the reader zooms in, which is what
    /// makes it a picture of the mark rather than a fixed decoration.
    /// </remarks>
    private void ShowNib(Reading reading)
    {
        if (_live is null) return;

        var box = _view.PointToScreen(new Point(0, 0));
        var scale = _view.RenderScale;

        if (scale <= 0) return;

        var brush = _live.Brush;
        var across = brush.Width is { } width ? width.For(reading.Pressure) : brush.Diameter;

        _nib.Show(
            new Point((reading.X - box.X) / scale, (reading.Y - box.Y) / scale),
            across * _view.View.Zoom / scale,
            brush.Nib?.Ratio ?? 1,
            brush.Nib?.Degrees ?? 0);
    }

    /// <summary>
    /// The pen row's text, written only when it changes.
    /// </summary>
    /// <remarks>
    /// Written on every frame instead, a TextBlock assigned the same string still invalidates
    /// its layout -- and this one is called from the render handler, so each frame asked for
    /// the next. A row whose height is being remeasured while the view is presenting is a
    /// viewport that moves under the drawing.
    /// </remarks>
    private void PenSays(string what)
    {
        var block = this.FindControl<TextBlock>("PenState")!;

        if (block.Text != what) block.Text = what;
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

    /// <summary>
    /// The document changed, so the picture has to be rebuilt before it is presented.
    /// </summary>
    /// <remarks>
    /// <see cref="LivePen.Refresh"/> rather than a blit, because a stroke in hand has to go
    /// with the page it was being drawn on. Refresh abandons it; lifting it would merge it
    /// onto the document, so Clear pressed with the tip still down would empty the page and
    /// then paste the stroke onto it.
    /// </remarks>
    private void Redraw()
    {
        _live?.Refresh();

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

        PenSays(Pen());
    }

    /// <summary>
    /// What the pen is doing, on the pen's own row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On this row and not on the view's status line, because the view's line is about the
    /// view and because it was already running to the window's edge: the pen's numbers
    /// appended to it were simply off screen, at every window size tried, and wrapping it to
    /// fit them moved the viewport under the thing it was reporting on.
    /// </para>
    /// <para>
    /// The raw pressure and the range beside it, because the count means nothing without the
    /// scale -- which is the confusion <see cref="Brush.Ranged"/> exists to prevent. The queue
    /// depth is how many readings came across in the last drain: shallow means the poll is
    /// faster than the pen, and growing means it is not.
    /// </para>
    /// </remarks>
    private string Pen()
    {
        if (!_pen.IsRunning) return "no pen open";

        var api = _pen.Session?.Api.ToString() ?? "";

        return $"{api}   pressure {_last.Pressure}/{_pen.MaxPressure}   "
             + $"lean {_last.Lean:0.#}   azimuth {_last.Azimuth:0.#}   queue {_batch}   "
             + (_live is { Drawing: true } live
                 ? $"drawing: {live.Readings} readings, {live.Stamps} stamps"
                 : "not in contact");
    }
}
