using SkiaSharp;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Views;

/// <summary>
/// How a surface is shown: a zoom, a pan, and the filtering those imply.
/// <para>
/// Everything here is in <b>physical display pixels</b>. The guarantee this type exists to
/// keep is that at a zoom of 1 a surface pixel occupies exactly one display pixel, so a
/// hundred-pixel-wide surface is a hundred display pixels wide and a single dot drawn on it
/// is a single dot on screen. What that works out to in a windowing system's device
/// independent units is the presenter's problem and not this type's.
/// </para>
/// </summary>
public readonly record struct View
{
    private View(double zoom, double panX, double panY)
    {
        Zoom = zoom;
        PanX = panX;
        PanY = panY;
    }

    /// <summary>Display pixels per surface pixel. Above 1 it is a whole number; below 1 it is free.</summary>
    public double Zoom { get; }

    /// <summary>Where the surface's top left sits, in display pixels. Always whole numbers.</summary>
    public double PanX { get; }

    public double PanY { get; }

    public static View At(double zoom, double panX = 0, double panY = 0) =>
        new(Snap(zoom), Math.Round(panX), Math.Round(panY));

    /// <summary>
    /// The nearest legal zoom.
    /// <para>
    /// Magnification is whole numbers only. At 3.5 a display pixel takes source pixel
    /// <c>floor(d / 3.5)</c>, and the runs come out 4, 3, 4, 3: surface pixels of alternating
    /// widths on screen, which is what a diagonal line jumping actually is. At any whole
    /// number every run is the same length.
    /// </para>
    /// <para>
    /// Minification has no such constraint, because it is filtered rather than replicated and
    /// any ratio is as good as any other.
    /// </para>
    /// </summary>
    public static double Snap(double zoom)
    {
        if (!(zoom > 0)) throw new ArgumentOutOfRangeException(nameof(zoom), "a zoom is positive");

        return zoom <= 1 ? zoom : Math.Round(zoom);
    }

    /// <summary>
    /// The next magnification step up, and the one below.
    /// <para>
    /// Whole numbers, so a user zooming in goes 1, 2, 3 and each surface pixel is a square
    /// block of that many display pixels on a side.
    /// </para>
    /// </summary>
    public View In() => new(Zoom < 1 ? NextUpFromMinified(Zoom) : Zoom + 1, PanX, PanY);

    public View Out() => new(Zoom <= 1 ? Zoom / 2 : Zoom - 1, PanX, PanY);

    private static double NextUpFromMinified(double zoom) => Math.Min(1, zoom * 2);

    public View PannedTo(double x, double y) => new(Zoom, Math.Round(x), Math.Round(y));

    /// <summary>This zoom, with the surface in the middle of a viewport of this size.</summary>
    public static View Centred(double zoom, int surfaceWidth, int surfaceHeight,
                               int viewportWidth, int viewportHeight)
    {
        // Snapped first, because the pan has to centre the surface at the zoom that will
        // actually be used rather than the one that was asked for. Centring at 3.5 and then
        // drawing at 4 leaves the surface a quarter of its width off to one side.
        var snapped = Snap(zoom);

        return At(
            snapped,
            (viewportWidth - surfaceWidth * snapped) / 2,
            (viewportHeight - surfaceHeight * snapped) / 2);
    }

    /// <summary>
    /// The view that shows the whole surface at once, centred.
    /// <para>
    /// Zooming <b>out</b> only. If the surface already fits, this is 1 and not more: a fit
    /// that magnified would make a small surface fill a large window with blocks, which is
    /// not what a reader asking to see the whole thing is asking for.
    /// </para>
    /// <para>
    /// The fitting zoom is whatever ratio it takes and is almost never a whole number. That
    /// is allowed, and it is the reason minification is the one case left free: below 1 a
    /// surface pixel has no display pixel of its own, so there is no block structure that a
    /// fractional ratio could make uneven.
    /// </para>
    /// </summary>
    public static View Fitting(int surfaceWidth, int surfaceHeight, int viewportWidth, int viewportHeight)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return At(1);

        var zoom = Math.Min(
            1,
            Math.Min(viewportWidth / (double)surfaceWidth, viewportHeight / (double)surfaceHeight));

        return Centred(zoom, surfaceWidth, surfaceHeight, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// How the surface should be sampled at this zoom.
    /// <para>
    /// Three cases, and the middle one is the guarantee.
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>At 1</b>, nothing is resampled. Nearest with no mipmaps against a
    /// whole-pixel offset copies the surface across unchanged.</description></item>
    /// <item><description><b>Magnified</b>, nearest. A surface pixel becomes a square block of
    /// identical display pixels with a hard edge, which is what lets a user address an
    /// individual pixel. Any smoothing here would soften exactly the thing they are aiming
    /// at.</description></item>
    /// <item><description><b>Minified</b>, linear with mipmaps. Not a cubic resampler:
    /// SkiaSharp offers cubic and mipmaps as alternatives rather than together, and below
    /// about half size the mipmaps are what stop the aliasing. A cubic filter still samples
    /// the full-size image and still skips pixels, so a thin diagonal breaks up.</description></item>
    /// </list>
    /// </summary>
    public SKSamplingOptions Sampling => Zoom < 1
        ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
        : new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

    /// <summary>Where a surface position lands on the display.</summary>
    public (double X, double Y) ToDisplay(double surfaceX, double surfaceY) =>
        (PanX + surfaceX * Zoom, PanY + surfaceY * Zoom);

    /// <summary>Which surface position a display position is over.</summary>
    public (double X, double Y) ToSurface(double displayX, double displayY) =>
        ((displayX - PanX) / Zoom, (displayY - PanY) / Zoom);
}

public static class Presenter
{
    /// <summary>
    /// Draws a surface into another one as this view would show it.
    /// <para>
    /// The destination stands in for the display, which is what makes every claim on this
    /// subject checkable: a presentation can be read back pixel by pixel and compared with
    /// what it should be, rather than looked at.
    /// </para>
    /// </summary>
    public static void Present(Surface source, Surface display, View view) =>
        Present(source, display.Canvas, view);

    /// <summary>
    /// The same, onto a bare canvas.
    /// <para>
    /// The application presents into its own bitmap in the windowing system's channel order,
    /// and this overload is what lets it do that without a per-frame conversion. Every
    /// decision that matters -- the scale, the offset, the sampling -- happens here either
    /// way, so what the application shows is what the checks measured.
    /// </para>
    /// </summary>
    public static void Present(Surface source, SKCanvas display, View view)
    {
        display.Clear(SKColors.Transparent);

        using var image = SnapshotOf(source);

        var (x, y) = view.ToDisplay(0, 0);
        var destination = SKRect.Create(
            (float)x,
            (float)y,
            (float)(source.PixelWidth * view.Zoom),
            (float)(source.PixelHeight * view.Zoom));

        using var paint = new SKPaint();
        display.DrawImage(image, destination, view.Sampling, paint);
    }

    /// <summary>
    /// An image of the bytes a surface holds.
    /// <para>
    /// Public because a page checking this code has to be able to build a deliberately
    /// broken presenter, and one that could not take the same snapshot would be comparing
    /// two different pictures rather than two presentations of one.
    /// </para>
    /// </summary>
    public static SKImage SnapshotOf(Surface source) => source.Snapshot();
}
