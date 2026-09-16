using StrokeFieldGuide.Canvas;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Lab.Tests;

/// <summary>
/// The parts of the application the split deliberately left in the control: keyboard
/// routing, the drag, the scroll bars, and reacting to a viewport that changed underneath.
/// <para>
/// The arithmetic each of these uses is checked on its page, from the library side. What is
/// checked here is that the control reaches it -- with the right numbers, in the right
/// units, at the right time.
/// </para>
/// </summary>
public class TheControl
{
    private static (MainWindow Window, SurfaceView Canvas) At(double scaling)
    {
        var (window, canvas) = Headless.Open();

        window.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();

        return (window, canvas);
    }

    /// <summary>
    /// Drives a real render pass.
    /// <para>
    /// The viewport is something the control learns while rendering, not while laying out,
    /// so running the dispatcher's queue is not enough: without this, a test that resizes
    /// the window reads the old viewport back and compares it with itself.
    /// </para>
    /// </summary>
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A point inside the canvas, in the window's device independent units.</summary>
    private static Point Inside(SurfaceView canvas, Window window, double dx = 0, double dy = 0)
    {
        var origin = canvas.TranslatePoint(default, window)!.Value;

        return new Point(origin.X + 40 + dx, origin.Y + 40 + dy);
    }

    // ------------------------------------------------------------------------ the buttons

    [AvaloniaFact]
    public void Draw_strokes_puts_a_brush_engine_mark_on_the_document()
    {
        // The button is wiring, and wiring is what a control-level test is for: every stage
        // it reaches is checked on its own page, and none of that says the button calls them.
        var (window, canvas) = At(1);

        canvas.Surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        Assert.Null(canvas.Surface.InkBounds());

        window.FindControl<Button>("DrawStroke")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Render(window);

        var bounds = canvas.Surface.InkBounds();
        Assert.True(bounds is not null, "the button drew nothing");

        // Inside the document, and using most of it: the strokes are laid out across the
        // surface rather than piled in a corner.
        var (left, top, right, bottom) = bounds!.Value;

        Assert.True(left >= 0 && top >= 0, $"the mark starts at ({left}, {top})");
        Assert.True(right <= canvas.Surface.PixelWidth, $"the mark reaches {right}");
        Assert.True(bottom <= canvas.Surface.PixelHeight, $"the mark reaches {bottom}");
        Assert.True(right - left > 500, $"the mark is only {right - left} across");
    }

    // ------------------------------------------------------------------ keyboard routing

    [AvaloniaFact]
    public void Space_reaches_the_canvas()
    {
        var (window, canvas) = At(1);

        Assert.False(canvas.HandPanning);

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.True(canvas.HandPanning, "holding space did not put the canvas in hand mode");

        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.False(canvas.HandPanning, "releasing space left the canvas in hand mode");
    }

    [AvaloniaFact]
    public void Space_belongs_to_the_button_that_has_the_focus_instead()
    {
        // The canvas owns the space bar while the reader is working on the canvas, and not
        // otherwise. Handling it at the window was simpler and took the space bar away from
        // every other control: a focused button could not be pressed with it, and a text
        // field -- the moment there is one -- could not have a space typed into it.
        var (window, canvas) = At(1);

        var zoomOut = window.FindControl<Button>("ZoomOut")!;
        zoomOut.Focus();
        Render(window);

        Assert.True(zoomOut.IsFocused, "the button never took the focus, so this proves nothing");

        var before = canvas.View.Zoom;

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Render(window);

        Assert.True(canvas.View.Zoom < before, "space did not reach the focused button");
        Assert.False(canvas.HandPanning, "space put the canvas in hand mode from another control");
    }

    [AvaloniaFact]
    public void Clicking_the_canvas_gives_the_space_bar_back_to_it()
    {
        // Which is what makes the rule above workable: a reader who wants to pan is already
        // pointing at the canvas.
        var (window, canvas) = At(1);

        window.FindControl<Button>("ZoomOut")!.Focus();
        Render(window);

        var from = Inside(canvas, window);
        window.MouseDown(from, MouseButton.Left);
        window.MouseUp(from, MouseButton.Left);
        Render(window);

        Assert.True(canvas.IsFocused, "clicking the canvas did not focus it");

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.True(canvas.HandPanning);

        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.False(canvas.HandPanning);
    }

    // ------------------------------------------------------------------------- the drag

    [AvaloniaFact]
    public void A_drag_moves_the_pan_by_the_distance_in_display_pixels()
    {
        // The drag arrives in device independent units and the pan is in physical pixels, so
        // a hundred units at 2.25 is two hundred and twenty five pixels. Unconverted, the
        // drawing travels a fraction of the distance and the drag feels heavy.
        var (window, canvas) = At(2.25);

        canvas.SetView(View.At(1, 0, 0));
        var before = canvas.View.PanX;

        var from = Inside(canvas, window);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(100, 0));
        window.MouseUp(from + new Vector(100, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before + 225, canvas.View.PanX, 0.5);
    }

    [AvaloniaFact]
    public void A_drag_that_loses_the_pointer_does_not_carry_on()
    {
        // A pointer can be taken away mid-gesture -- another control captures it, the window
        // loses activation, a tablet leaves proximity. Whatever happens next is not a
        // continuation of the drag, and the pan must not jump when the pointer reappears.
        var (window, canvas) = At(1);

        canvas.SetView(View.At(1, 0, 0));

        var from = Inside(canvas, window);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(20, 0));
        Dispatcher.UIThread.RunJobs();

        var during = canvas.View.PanX;
        Assert.Equal(20, during, 0.5);

        // The gesture ends without a button release, and a later move is not part of it.
        canvas.EndGesture();
        window.MouseMove(from + new Vector(400, 0));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(during, canvas.View.PanX, 0.5);
    }

    // ----------------------------------------------------------------------- scroll bars

    [AvaloniaFact]
    public void The_scroll_bars_say_what_the_view_says()
    {
        var (window, canvas) = At(1);

        // Magnified, so the surface is larger than the viewport and there is something to
        // scroll. 1000 at a zoom of 4 is 4000 display pixels of content.
        canvas.SetView(View.At(4, -600, -300));
        Render(window);

        var horizontal = window.FindControl<ScrollBar>("ScrollX")!;

        Assert.True(horizontal.IsEnabled, "a surface larger than the viewport does not scroll");

        Assert.Equal(600, horizontal.Value, 0.5);
        Assert.Equal(4000 - canvas.ViewportWidth, horizontal.Maximum, 0.5);
        Assert.Equal(canvas.ViewportWidth, horizontal.ViewportSize, 0.5);
    }

    [AvaloniaFact]
    public void Moving_a_scroll_bar_pans()
    {
        var (window, canvas) = At(1);

        canvas.SetView(View.At(4, 0, 0));
        Render(window);

        var horizontal = window.FindControl<ScrollBar>("ScrollX")!;
        horizontal.Value = 250;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(-250, canvas.View.PanX, 0.5);
    }

    [AvaloniaFact]
    public void A_surface_that_fits_does_not_scroll()
    {
        var (window, canvas) = At(1);

        // Magnified first, so the bars are enabled and the assertion below is a change.
        canvas.SetView(View.At(4, 0, 0));
        Render(window);

        Assert.True(window.FindControl<ScrollBar>("ScrollX")!.IsEnabled,
            "the bars were never enabled, so disabling them proves nothing");

        canvas.FitSurface();
        Render(window);

        Assert.False(window.FindControl<ScrollBar>("ScrollX")!.IsEnabled);
        Assert.False(window.FindControl<ScrollBar>("ScrollY")!.IsEnabled);
    }

    // ----------------------------------------------------------- the viewport changing

    [AvaloniaFact]
    public void A_resize_keeps_what_was_in_the_middle_of_the_viewport()
    {
        var (window, canvas) = At(1);

        canvas.SetView(View.At(2, -400, -200));
        Render(window);

        var wide = canvas.ViewportWidth;
        var was = canvas.View.ToSurface(canvas.ViewportWidth / 2.0, canvas.ViewportHeight / 2.0);

        window.Width = 700;
        window.Height = 560;
        Render(window);

        Assert.True(canvas.ViewportWidth != wide,
            $"the viewport is still {wide} wide, so nothing was resized and this proves nothing");

        var now = canvas.View.ToSurface(canvas.ViewportWidth / 2.0, canvas.ViewportHeight / 2.0);

        Assert.Equal(was.X, now.X, 0.5);
        Assert.Equal(was.Y, now.Y, 0.5);
    }

    [AvaloniaFact]
    public void A_scaling_change_and_back_again_ends_where_it_started()
    {
        // Monitors are left as well as arrived at. A transition that is right in one
        // direction and lossy in the other drifts every time a window is moved back and
        // forth, which is a thing people do all day.
        var (window, canvas) = At(1);

        canvas.SetView(View.At(2, -400, -200));
        Render(window);

        var was = canvas.View.ToSurface(canvas.ViewportWidth / 2.0, canvas.ViewportHeight / 2.0);

        foreach (var scaling in new[] { 2.25, 1.5, 1.0, 2.0, 1.0 })
        {
            window.SetRenderScaling(scaling);
            Render(window);

            Assert.True(Math.Abs(canvas.RenderScale - scaling) < 1e-9,
                $"the window reports a scaling of {canvas.RenderScale} rather than {scaling}");
        }

        var now = canvas.View.ToSurface(canvas.ViewportWidth / 2.0, canvas.ViewportHeight / 2.0);

        // Back at the scaling it started from, so back at the viewport it started with.
        Assert.Equal(was.X, now.X, 0.5);
        Assert.Equal(was.Y, now.Y, 0.5);
    }

    [AvaloniaFact]
    public void A_change_of_scaling_keeps_what_was_in_the_middle_of_the_viewport()
    {
        // Dragging a window to a monitor with different scaling, which is the case a pan
        // held in display pixels gets wrong: the same number is a different distance there.
        var (window, canvas) = At(1);

        canvas.SetView(View.At(2, -400, -200));
        Render(window);

        var wide = canvas.ViewportWidth;
        var was = canvas.View.ToSurface(canvas.ViewportWidth / 2.0, canvas.ViewportHeight / 2.0);

        window.SetRenderScaling(2.25);
        Render(window);

        Assert.True(Math.Abs(canvas.RenderScale - 2.25) < 1e-9,
            $"the window reports a scaling of {canvas.RenderScale}");

        Assert.True(canvas.ViewportWidth != wide,
            $"the viewport is still {wide} physical pixels wide, so the scaling did not reach it");

        var now = canvas.View.ToSurface(canvas.ViewportWidth / 2.0, canvas.ViewportHeight / 2.0);

        Assert.Equal(was.X, now.X, 0.5);
        Assert.Equal(was.Y, now.Y, 0.5);
    }
}
