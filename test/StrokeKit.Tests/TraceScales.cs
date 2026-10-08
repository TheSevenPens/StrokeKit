using System.Text.Json;
using StrokeKit.Strokes;

namespace StrokeKit.Tests;

/// <summary>
/// That a trace of either kind answers "how far is one unit of x and y, in millimetres".
/// </summary>
/// <remarks>
/// The two kinds are a tablet recording, in digitizer counts, from a tool that reads the
/// device's driver directly, and a desktop recording, in pixels, from this one. They state
/// their size differently and the question is the same, so it is asked in one place.
/// </remarks>
public class TraceScales
{
    private static (double X, double Y)? Scale(string coordinates) =>
        TraceFormat.MillimetresPerUnit(
            JsonDocument.Parse($$"""{ "formatVersion": 8, "coordinates": {{coordinates}} }""").RootElement);

    [Fact]
    public void A_tablet_recording_scales_by_its_surface_over_its_largest_count()
    {
        var scale = Scale("""
            { "space": "tablet", "units": "digitizer counts",
              "maxX": 62500, "maxY": 39062, "widthMm": 224.0, "heightMm": 126.0 }
            """);

        Assert.NotNull(scale);
        Assert.Equal(224.0 / 62500, scale.Value.X, 12);
        Assert.Equal(126.0 / 39062, scale.Value.Y, 12);
    }

    [Fact]
    public void A_desktop_recording_scales_by_the_figures_it_states_for_each_axis()
    {
        var scale = Scale("""
            { "space": "desktop", "units": "desktop physical pixels",
              "widthMm": 349, "heightMm": 195, "mmPerPixelX": 0.090885, "mmPerPixelY": 0.060185 }
            """);

        Assert.NotNull(scale);
        Assert.Equal(0.090885, scale.Value.X, 9);
        Assert.Equal(0.060185, scale.Value.Y, 9);

        // The two axes differ, which is the reason there are two figures.
        Assert.NotEqual(scale.Value.X, scale.Value.Y, 3);
    }

    [Fact]
    public void A_desktop_recording_that_could_not_ask_has_no_scale()
    {
        // Not derived from the surface size and a screen: the mapped part of the tablet is not
        // necessarily all of it, and a guess here would be a distance nobody measured.
        Assert.Null(Scale("""{ "space": "desktop", "units": "desktop physical pixels" }"""));
        Assert.Null(Scale("""{ "space": "desktop", "units": "px", "widthMm": 349, "heightMm": 195 }"""));
    }

    [Fact]
    public void A_file_before_version_eight_has_no_scale()
    {
        Assert.Null(TraceFormat.MillimetresPerUnit(
            JsonDocument.Parse("""{ "formatVersion": 7, "device": { "tablet": "A tablet" } }""").RootElement));
    }

    [Theory]
    [InlineData("""{ "space": "tablet", "units": "c", "maxX": 0, "maxY": 10, "widthMm": 5, "heightMm": 5 }""")]
    [InlineData("""{ "space": "tablet", "units": "c", "maxX": 10, "maxY": 10, "widthMm": -5, "heightMm": 5 }""")]
    [InlineData("""{ "space": "tablet", "units": "c", "maxX": 10, "maxY": 10, "widthMm": 5 }""")]
    [InlineData("""{ "space": "desktop", "units": "px", "mmPerPixelX": 0.1, "mmPerPixelY": 0 }""")]
    [InlineData("""{ "space": "desktop", "units": "px", "mmPerPixelX": "0.1", "mmPerPixelY": 0.1 }""")]
    [InlineData("""{ "space": "somewhere", "units": "px", "mmPerPixelX": 0.1, "mmPerPixelY": 0.1 }""")]
    public void A_figure_that_is_missing_or_not_positive_gives_no_scale(string coordinates)
    {
        Assert.Null(Scale(coordinates));
    }
}
