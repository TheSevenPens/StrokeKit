using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using StrokeKit.Figures;
using StrokeKit.Views;

namespace StrokeFieldGuide.Lab.Tests;

/// <summary>
/// What the compositor actually put on the screen, at a scaling of 1 and at 2.25.
/// <para>
/// Everything the guide checks stops at <c>Presenter</c>. These start at the other end: a
/// real window, a real layout pass, the control's own bitmap, and the frame the compositor
/// produced from it.
/// </para>
/// </summary>
public class ThePresentedFrame
{
    [AvaloniaFact]
    public void At_a_zoom_of_one_the_frame_is_the_surface()
    {
        var (window, canvas) = Headless.Open();

        canvas.SetView(View.At(1, 0, 0));

        using var captured = window.CaptureRenderedFrame()!;
        var frame = new Frame(captured);
        var (originX, originY) = Frame.Origin(canvas, window, 1);

        // The figure is drawn into the surface when the window opens, so the surface's
        // pixels are the answer and nothing here needs to know what is in them.
        var art = canvas.Surface;

        var differ = 0;
        var checked_ = 0;

        for (var y = 0; y < Math.Min(200, canvas.ViewportHeight); y++)
        {
            for (var x = 0; x < Math.Min(200, canvas.ViewportWidth); x++)
            {
                var (r, g, b, _) = frame[originX + x, originY + y];
                var stored = art.ReadStored(x, y);

                checked_++;
                if (r != stored.Red || g != stored.Green || b != stored.Blue) differ++;
            }
        }

        Assert.True(checked_ > 10000, $"only {checked_} pixels were compared");
        Assert.True(differ == 0, $"{differ} of {checked_} composited pixels differ from the surface");
    }

    [AvaloniaFact]
    public void At_a_scaling_of_2_25_the_frame_is_still_the_surface()
    {
        // The fault this catches is the one constructing-the-view is built around: drawing
        // the presented bitmap into the control's own bounds asks the windowing system for a
        // quarter of a pixel more than the bitmap has, and it resizes by a factor of 1.00025.
        // Nothing about the arithmetic shows it; a composited pixel that is no longer the
        // surface's does.
        var (window, canvas) = Headless.Open();

        window.SetRenderScaling(2.25);
        Dispatcher.UIThread.RunJobs();

        canvas.SetView(View.At(1, 0, 0));

        Assert.True(Math.Abs(canvas.RenderScale - 2.25) < 1e-9,
            $"the window reports a scaling of {canvas.RenderScale}");

        using var captured = window.CaptureRenderedFrame()!;
        var frame = new Frame(captured);
        var (originX, originY) = Frame.Origin(canvas, window, 2.25);

        var art = canvas.Surface;
        var differ = 0;
        var counted = 0;

        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var (r, g, b, _) = frame[originX + x, originY + y];
                var stored = art.ReadStored(x, y);

                counted++;
                if (r != stored.Red || g != stored.Green || b != stored.Blue) differ++;
            }
        }

        Assert.True(differ == 0,
            $"{differ} of {counted} composited pixels differ from the surface at a scaling of 2.25");

        // And the single-pixel dot is still a single pixel: at 1.00025 it would bleed into
        // one neighbour and not the other, which the block comparison above would catch as a
        // handful of pixels rather than as the thing it is.
        var dot = (Demo.Dot.Red, Demo.Dot.Green, Demo.Dot.Blue);

        var (dr, dg, db, _) = frame[originX + 104, originY + 80];
        Assert.True((dr, dg, db) == dot, $"the dot came back as ({dr}, {dg}, {db})");

        foreach (var offset in new[] { -1, 1 })
        {
            var (nr, ng, nb, _) = frame[originX + 104 + offset, originY + 80];
            Assert.True((nr, ng, nb) != dot, $"the neighbour at {offset} is the dot's own colour");
        }
    }
}
