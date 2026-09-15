using SkiaSharp;
using StrokeFieldGuide.Figures;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Lab;

/// <summary>
/// The stage-one guarantees, checked against the same presentation code the window uses,
/// and reported so that CI can gate on them.
/// <para>
/// No window is opened. Everything here is a raster surface standing in for a display, which
/// is the whole reason the presenter was written to draw into one: a claim about what a user
/// sees becomes a claim about pixels that can be read back and counted.
/// </para>
/// <para>
/// This duplicates nothing from the test suite. The suite checks the same properties from
/// the library's side; this checks that the application, built and run as a program, still
/// has them.
/// </para>
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        var failures = 0;

        foreach (var check in new (string Name, Func<string?> Run)[]
                 {
                     ("100% is one surface pixel per display pixel", OneToOne),
                     ("magnified pixels are square blocks with hard edges", SquareBlocks),
                     ("magnification only goes in whole steps", WholeSteps),
                     ("a pan is always a whole number of display pixels", WholePans),
                     ("minification averages rather than aliases", NoAliasing),
                     ("fit brings the whole surface into the window", FitFits),
                     ("a scroll bar agrees with the pan", ScrollBarsAgree),
                     ("a frame does not read the surface per pixel", FrameCost),
                     ("the demo figure lands inside the surface", DemoFits),
                 })
        {
            var problem = check.Run();
            if (problem is null)
            {
                Console.WriteLine($"  ok    {check.Name}");
                continue;
            }

            Console.WriteLine($"  FAIL  {check.Name}: {problem}");
            failures++;
        }

        Console.WriteLine(failures == 0
            ? "stage one: all checks passed"
            : $"stage one: {failures} check(s) failed");

        return failures == 0 ? 0 : 1;
    }

    private static Surface Figure(int size)
    {
        var surface = Surface.CreateExactly(size, size, size, size);
        Demo.Draw(surface);
        return surface;
    }

    private static string? OneToOne()
    {
        using var art = Figure(200);
        using var display = Surface.CreateExactly(200, 200, 200, 200);

        Presenter.Present(art, display, View.At(1));

        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                if (art.ReadStored(x, y) != display.ReadStored(x, y))
                    return $"pixel ({x}, {y}) differs from the surface";
            }
        }

        return null;
    }

    private static string? SquareBlocks()
    {
        using var art = Figure(40);
        using var display = Surface.CreateExactly(120, 120, 120, 120);

        Presenter.Present(art, display, View.At(3));

        for (var y = 0; y < 120; y++)
        {
            for (var x = 0; x < 120; x++)
            {
                var expected = art.ReadStored(x / 3, y / 3);
                if (display.ReadStored(x, y) != expected)
                    return $"pixel ({x}, {y}) is not the surface pixel it magnifies";
            }
        }

        return null;
    }

    private static string? WholeSteps()
    {
        foreach (var asked in new[] { 1.5, 2.4, 3.5, 7.9 })
        {
            var got = View.At(asked).Zoom;
            if (Math.Abs(got - Math.Round(got)) > 1e-9)
                return $"a zoom of {asked} was kept as {got}, which gives uneven pixel blocks";
        }

        var stepped = View.At(1);
        foreach (var expected in new[] { 2.0, 3.0, 4.0 })
        {
            stepped = stepped.In();
            if (Math.Abs(stepped.Zoom - expected) > 1e-9)
                return $"zooming in reached {stepped.Zoom} where {expected} was expected";
        }

        return null;
    }

    private static string? WholePans()
    {
        var panned = View.At(1).PannedTo(10.5, -3.25);

        if (Math.Abs(panned.PanX - Math.Round(panned.PanX)) > 1e-9
            || Math.Abs(panned.PanY - Math.Round(panned.PanY)) > 1e-9)
        {
            return $"a pan of ({panned.PanX}, {panned.PanY}) puts surface pixels between display pixels";
        }

        return null;
    }

    private static string? NoAliasing()
    {
        // Alternating one-pixel lines. Point sampling takes every fourth row and returns
        // solid black or nothing depending on where it lands; averaging returns an even grey.
        using var art = Surface.CreateExactly(64, 64, 64, 64);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        for (var y = 0; y < 64; y += 2) art.Canvas.DrawRect(SKRect.Create(0, y, 64, 1), paint);

        using var display = Surface.CreateExactly(16, 16, 16, 16);
        Presenter.Present(art, display, View.At(0.25));

        var alphas = Enumerable.Range(0, 16).Select(row => display.ReadStored(4, row).Alpha).ToList();
        var spread = alphas.Max() - alphas.Min();

        if (spread > 8) return $"rows range from {alphas.Min()} to {alphas.Max()}, which is banding";
        if (alphas.Max() == 0 || alphas.Min() == 255) return "the pattern was lost rather than averaged";

        return null;
    }

    private static string? FitFits()
    {
        // A viewport the application's surface does not fit in, so fit has to zoom out, and
        // one it does, so fit has to leave the zoom alone.
        foreach (var (width, height, mustZoomOut) in new[] { (1246, 900, true), (1246, 1500, false) })
        {
            var view = View.Fitting(1000, 1000, width, height);

            if (view.Zoom > 1) return $"fit magnified to {view.Zoom}x in a {width}x{height} viewport";

            var (left, top) = view.ToDisplay(0, 0);
            var (right, bottom) = view.ToDisplay(1000, 1000);

            if (left < 0 || top < 0 || right > width || bottom > height)
            {
                return $"in a {width}x{height} viewport the surface runs from ({left}, {top}) "
                    + $"to ({right}, {bottom}), which is outside it";
            }

            if (!mustZoomOut)
            {
                // It already fitted, so fit is 100% and not something smaller that happens to
                // fit as well.
                if (view.Zoom != 1) return $"a surface that already fitted was shown at {view.Zoom}x";
                continue;
            }

            // It did not fit, so the fit is against the limiting pair of edges. Any further out
            // than that is zooming for no reason.
            if (left > 1 && top > 1)
                return $"the surface starts at ({left}, {top}) and reaches no edge, so the fit is too small";
        }

        return null;
    }

    private static string? ScrollBarsAgree()
    {
        var bar = Scrolling.For(1000, 445, 0);

        if (!bar.Needed) return "a surface wider than its viewport reports nothing to scroll";

        if (Math.Abs(bar.Maximum - bar.Minimum - 555) > 1e-9)
            return $"the bar spans {bar.Maximum - bar.Minimum} where the hidden part is 555";

        if (Scrolling.For(200, 445, 122).Needed)
            return "a surface smaller than its viewport reports something to scroll";

        return null;
    }

    /// <summary>
    /// The one check here that is not about which pixels come out.
    /// <para>
    /// A frame that acquires the surface's pixels once per pixel rather than once is correct
    /// -- every other check here passed while it took 189 milliseconds a frame -- and a
    /// reader calls it broken anyway. Counted rather than timed, so the answer is the same on
    /// every machine.
    /// </para>
    /// </summary>
    private static string? FrameCost()
    {
        using var display = Surface.CreateExactly(1000, 1000, 1000, 1000);

        long Frame(int size)
        {
            using var art = Figure(size);

            var before = art.Reads;
            Presenter.Present(art, display, View.At(1));

            return art.Reads - before;
        }

        var small = Frame(100);
        var large = Frame(400);

        if (large != small)
            return $"a 400px surface took {large} reads and a 100px one {small}, so the cost is per pixel";

        if (small > 4) return $"a frame reads the surface {small} times, which is not a fixed handful";

        return null;
    }

    private static string? DemoFits()
    {
        using var art = Figure(1000);

        var bounds = art.InkBounds();
        if (bounds is null) return "the demo drew nothing";

        var (left, top, right, bottom) = bounds.Value;
        if (left < 0 || top < 0 || right > 1000 || bottom > 1000)
            return $"the figure covers ({left}, {top}) to ({right}, {bottom}), outside the surface";

        return null;
    }
}
