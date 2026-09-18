using Avalonia.Headless.XUnit;

namespace StrokeKit.Lab.Tests;

public class Smoke
{
    [AvaloniaFact]
    public void The_window_opens_and_finds_its_canvas()
    {
        var (window, canvas) = Headless.Open();

        Assert.True(window.IsVisible);
        Assert.True(canvas.Bounds.Width > 0, $"the canvas laid out to {canvas.Bounds}");
    }
}
