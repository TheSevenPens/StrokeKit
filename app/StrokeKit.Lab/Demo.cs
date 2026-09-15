using SkiaSharp;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Lab;

/// <summary>
/// Geometric figures drawn into the surface, chosen so that a presentation fault shows.
/// <para>
/// None of this is a stroke. Stage one is about getting a surface onto a display correctly,
/// and a brush would add its own faults to the ones being looked for.
/// </para>
/// <para>
/// Each figure is here because a particular fault makes it look wrong in a particular way:
/// </para>
/// <list type="bullet">
/// <item><description>The <b>single pixels</b> are the guarantee at 100%: one of them should
/// be one pixel on the display, countable with good eyes.</description></item>
/// <item><description>The <b>one-pixel grid</b> aliases into bands under point-sampled
/// minification and stays an even grey under a filtered one.</description></item>
/// <item><description>The <b>diagonals</b> show uneven magnification: at a fractional zoom
/// their steps come out different sizes.</description></item>
/// <item><description>The <b>concentric circles</b> put curvature at every angle, which is
/// where a resampler's failures are easiest to see.</description></item>
/// <item><description>The <b>corner markers</b> show the edges of the surface, so a wrong
/// origin or a wrong scale is visible against the window rather than guessed at.</description></item>
/// </list>
/// </summary>
public static class Demo
{
    public static void Draw(Surface surface)
    {
        var width = surface.PixelWidth;
        var height = surface.PixelHeight;

        surface.Canvas.Clear(new SKColor(0xFA, 0xFA, 0xF8));

        using var hairline = new SKPaint
        {
            Color = new SKColor(0xC8, 0xD2, 0xD6),
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };

        // A one-pixel grid every sixteen pixels. Exactly one pixel wide and not antialiased,
        // so that anything soft on screen came from the presentation and not from here.
        for (var x = 0; x < width; x += 16) surface.Canvas.DrawRect(SKRect.Create(x, 0, 1, height), hairline);
        for (var y = 0; y < height; y += 16) surface.Canvas.DrawRect(SKRect.Create(0, y, width, 1), hairline);

        using var ink = new SKPaint
        {
            Color = new SKColor(0x10, 0x2A, 0x33),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
        };

        using var rings = new SKPaint
        {
            Color = new SKColor(0x0D, 0x6A, 0x6A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };

        var centreX = width / 2f;
        var centreY = height / 2f;
        for (var radius = 40; radius < Math.Min(width, height) / 2; radius += 40)
            surface.Canvas.DrawCircle(centreX, centreY, radius, rings);

        // Diagonals at several angles, including some that are not 45 degrees, because an
        // uneven magnification is most obvious on a shallow slope.
        foreach (var (dx, dy) in new[] { (1.0, 1.0), (1.0, 0.5), (0.5, 1.0), (1.0, -1.0) })
        {
            surface.Canvas.DrawLine(
                (float)(centreX - 320 * dx), (float)(centreY - 320 * dy),
                (float)(centreX + 320 * dx), (float)(centreY + 320 * dy),
                ink);
        }

        // Single pixels, spaced widely, with nothing antialiased about them. At 100% each is
        // one pixel on the display.
        using var dot = new SKPaint
        {
            Color = new SKColor(0xB0, 0x2E, 0x2E),
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };

        for (var step = 0; step < 12; step++)
            surface.Canvas.DrawRect(SKRect.Create(80 + step * 24, 80, 1, 1), dot);

        // Corner markers: an L in each corner, so the surface's own edges are visible.
        using var edge = new SKPaint
        {
            Color = new SKColor(0x8A, 0x4B, 0x12),
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };

        const int arm = 48;
        foreach (var (x, y, sx, sy) in new[]
                 {
                     (0, 0, 1, 1),
                     (width - 1, 0, -1, 1),
                     (0, height - 1, 1, -1),
                     (width - 1, height - 1, -1, -1),
                 })
        {
            var left = sx > 0 ? x : x - arm + 1;
            var top = sy > 0 ? y : y - arm + 1;

            edge.Color = new SKColor(0x8A, 0x4B, 0x12);
            surface.Canvas.DrawRect(SKRect.Create(left, y, arm, 1), edge);
            surface.Canvas.DrawRect(SKRect.Create(x, top, 1, arm), edge);
        }
    }
}
