using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using StrokeKit.Avalonia;
using StrokeFieldGuide.Lab;
using StrokeFieldGuide.Lab.Tests;

[assembly: AvaloniaTestApplication(typeof(Headless))]

namespace StrokeFieldGuide.Lab.Tests;

/// <summary>
/// A real Avalonia application with no screen attached.
/// <para>
/// This exists because of a claim this guide made and had to withdraw: that a control is
/// the one thing in an application that cannot be run without a screen. It can. What is
/// true is that arithmetic is checkable at no setup cost and a control is not -- this file
/// and its project are the cost -- so the split between <c>Presentation</c> and
/// <c>SurfaceView</c> is still worth having. What it is not is an excuse for leaving the
/// control unchecked.
/// </para>
/// <para>
/// <c>UseHeadlessDrawing = false</c> is the part that matters. The default headless backend
/// records drawing operations and renders nothing, which would make every claim here a
/// claim about calls rather than pixels. With it off, Skia rasterises into a real
/// framebuffer and a frame can be read back -- so the bitmap bridge and the compositor are
/// exercised rather than mocked.
/// </para>
/// </summary>
public static class Headless
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>The application's own window, shown, with its canvas found in the tree.</summary>
    public static (MainWindow Window, SurfaceView Canvas) Open(double scaling = 1)
    {
        var window = new MainWindow();
        window.Show();

        var host = window.FindControl<Panel>("Host")
            ?? throw new InvalidOperationException("the window has no canvas host");

        // Anywhere under the host, not only directly in it. The nib cursor has to sit over
        // the view rather than in it, so the two are wrapped together in a panel -- and a
        // helper that only looked one level down turned that into sixteen failing tests
        // saying "the host has no surface view" about a window that had one.
        var canvas = Somewhere(host)
            ?? throw new InvalidOperationException("the host has no surface view");

        return (window, canvas);
    }

    /// <summary>The one surface view under a control, however deep.</summary>
    private static SurfaceView? Somewhere(Control at)
    {
        if (at is SurfaceView found) return found;

        if (at is not Panel panel) return null;

        return panel.Children.Select(Somewhere).FirstOrDefault(child => child is not null);
    }
}
