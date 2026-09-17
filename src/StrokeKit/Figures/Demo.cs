using SkiaSharp;
using StrokeKit.Surfaces;

namespace StrokeKit.Figures;

/// <summary>
/// Geometric figures drawn into a surface, chosen so that a presentation fault shows.
/// <para>
/// None of this is a stroke. The part this belongs to is about getting a surface onto a
/// display correctly, and a brush would add its own faults to the ones being looked for.
/// </para>
/// <para>
/// In the library rather than in the application, and in five pieces rather than one, for
/// the same reason: a page that claims the figure can reveal a fault has to be able to draw
/// it, and a check that claims one piece of it matters has to be able to put four real
/// pieces beside one broken one.
/// </para>
/// </summary>
public static class Demo
{
    public static readonly SKColor Paper = new(0xFA, 0xFA, 0xF8);

    public static readonly SKColor Hairline = new(0xC8, 0xD2, 0xD6);

    public static readonly SKColor Dot = new(0xB0, 0x2E, 0x2E);

    public static readonly SKColor Marker = new(0x8A, 0x4B, 0x12);

    /// <summary>How far a corner marker's arms run.</summary>
    public const int Arm = 48;

    public static void Draw(Surface surface)
    {
        Background(surface);
        Rings(surface);
        Diagonals(surface);
        Dots(surface);
        CornerMarkers(surface);
    }

    /// <summary>
    /// Paper, and a one-pixel grid every sixteen pixels.
    /// <para>
    /// Exactly one pixel wide and not antialiased, so anything soft on screen came from the
    /// presentation and not from here. Under a point-sampled minification the grid aliases
    /// into bands; under a filtered one it stays an even grey.
    /// </para>
    /// </summary>
    public static void Background(Surface surface)
    {
        surface.Canvas.Clear(Paper);

        using var hairline = new SKPaint { Color = Hairline, IsAntialias = false, Style = SKPaintStyle.Fill };

        for (var x = 0; x < surface.PixelWidth; x += 16)
            surface.Canvas.DrawRect(SKRect.Create(x, 0, 1, surface.PixelHeight), hairline);

        for (var y = 0; y < surface.PixelHeight; y += 16)
            surface.Canvas.DrawRect(SKRect.Create(0, y, surface.PixelWidth, 1), hairline);
    }

    /// <summary>
    /// Concentric circles, which put curvature at every angle.
    /// <para>
    /// A resampler's failures are easiest to see on a curve, because every part of it meets
    /// the pixel grid at a different angle and one of those angles will be the worst case.
    /// </para>
    /// </summary>
    public static void Rings(Surface surface)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x0D, 0x6A, 0x6A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };

        var centreX = surface.PixelWidth / 2f;
        var centreY = surface.PixelHeight / 2f;

        for (var radius = 40; radius < Math.Min(surface.PixelWidth, surface.PixelHeight) / 2; radius += 40)
            surface.Canvas.DrawCircle(centreX, centreY, radius, paint);
    }

    /// <summary>
    /// Straight lines at several angles, including some that are not 45 degrees.
    /// <para>
    /// An uneven magnification is most obvious on a shallow slope, where one surface pixel
    /// being wider than its neighbour moves a long run of the line rather than one step of it.
    /// </para>
    /// </summary>
    public static void Diagonals(Surface surface)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x10, 0x2A, 0x33),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
        };

        var centreX = surface.PixelWidth / 2f;
        var centreY = surface.PixelHeight / 2f;
        var reach = Math.Min(surface.PixelWidth, surface.PixelHeight) * 0.32;

        foreach (var (dx, dy) in new[] { (1.0, 1.0), (1.0, 0.5), (0.5, 1.0), (1.0, -1.0) })
        {
            surface.Canvas.DrawLine(
                (float)(centreX - reach * dx), (float)(centreY - reach * dy),
                (float)(centreX + reach * dx), (float)(centreY + reach * dy),
                paint);
        }
    }

    /// <summary>
    /// Single pixels, spaced widely, with nothing antialiased about them.
    /// <para>
    /// This is the guarantee at 100% made visible: one of these should be one pixel on the
    /// display, countable with good eyes. Drawn at a pixel's corner rather than its centre,
    /// because a rectangle from (80, 80) to (81, 81) covers pixel (80, 80) exactly and one
    /// from (80.5, 80.5) covers a quarter of each of four.
    /// </para>
    /// </summary>
    public static void Dots(Surface surface)
    {
        using var paint = new SKPaint { Color = Dot, IsAntialias = false, Style = SKPaintStyle.Fill };

        for (var step = 0; step < 12; step++)
        {
            var x = 80 + step * 24;
            if (x >= surface.PixelWidth) break;

            surface.Canvas.DrawRect(SKRect.Create(x, 80, 1, 1), paint);
        }
    }

    /// <summary>
    /// An L in each corner, on the surface's own outermost row and column.
    /// <para>
    /// So that the edges of the surface are visible against the window rather than guessed
    /// at: a wrong origin moves them and a wrong scale moves them by different amounts, and
    /// neither is apparent from the middle of the figure. The arms are deliberately on the
    /// last row and column and not one in from them, which is where a presentation with an
    /// off-by-one puts them.
    /// </para>
    /// </summary>
    public static void CornerMarkers(Surface surface)
    {
        using var paint = new SKPaint { Color = Marker, IsAntialias = false, Style = SKPaintStyle.Fill };

        var right = surface.PixelWidth - 1;
        var bottom = surface.PixelHeight - 1;

        foreach (var (x, y, towardsRight, towardsBottom) in new[]
                 {
                     (0, 0, true, true),
                     (right, 0, false, true),
                     (0, bottom, true, false),
                     (right, bottom, false, false),
                 })
        {
            var left = towardsRight ? x : x - Arm + 1;
            var top = towardsBottom ? y : y - Arm + 1;

            surface.Canvas.DrawRect(SKRect.Create(left, y, Arm, 1), paint);
            surface.Canvas.DrawRect(SKRect.Create(x, top, 1, Arm), paint);
        }
    }
}
