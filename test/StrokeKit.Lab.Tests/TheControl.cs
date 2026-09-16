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
    public void Space_does_not_press_the_button_that_has_the_focus()
    {
        // The reason the handler tunnels. With the focus on a button, an untunnelled space
        // would activate it, and in this application that means zooming rather than panning.
        var (window, canvas) = At(1);

        var zoomOut = window.FindControl<Button>("ZoomOut")!;
        zoomOut.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(zoomOut.IsFocused, "the button never took the focus, so this proves nothing");

        var before = canvas.View.Zoom;

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, canvas.View.Zoom);

        // And the button is not simply dead: Enter, which nothing intercepts, works.
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.True(canvas.View.Zoom < before,
            "Enter did not reach the focused button either, so nothing here is about space");
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
