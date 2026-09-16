using SkiaSharp;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Brushes;

/// <summary>
/// One application of a brush shape: what a brush engine asks a canvas to draw at one
/// position along a stroke.
/// <para>
/// A diameter and a colour, and nothing about where it goes. A stamp is the same stamp
/// wherever it lands, which is what lets a brush engine decide the shape once and the
/// positions separately.
/// </para>
/// </summary>
/// <param name="Diameter">
/// Across, not out from the centre. Stated because taking this for a radius is the
/// commonest way a brush comes out twice the size it should be, and the fault looks like a
/// scaling problem rather than a naming one.
/// </param>
/// <param name="Hardness">
/// How much of the stamp is at full strength before its edge begins, from 0 to 1.
/// <para>
/// 1 is the hard nib every page before this one used: full strength to the rim, and an edge
/// only as wide as antialiasing makes it. Below 1 the stamp holds full strength out to
/// <c>Hardness</c> of its radius and falls to nothing at the rim.
/// </para>
/// </param>
public readonly record struct Stamp(double Diameter, SKColor Colour, double Hardness = 1)
{
    public double Radius => Diameter / 2;

    /// <summary>True when the stamp has an edge worth drawing with a gradient.</summary>
    public bool IsSoft => Hardness < 1;
}

public static class Stamps
{
    /// <summary>
    /// Draws one stamp, centred at a position given in the stroke's own coordinates.
    /// <para>
    /// The position and the diameter are both converted by the transform, so a brush engine
    /// asking for a four-unit stamp gets four units of stroke regardless of how many pixels
    /// that turns out to be.
    /// </para>
    /// </summary>
    public static void Draw(
        Surface surface, InkTransform transform, Stamp stamp, double x, double y, SKBlender? blender = null)
    {
        var (centreX, centreY) = transform.ToSurface(x, y);

        // The diameter scales with the transform as the position does. Using the horizontal
        // scale for a round stamp is a simplification that holds while the two scales agree,
        // and a surface whose scales differ would need an ellipse rather than a circle.
        var radius = stamp.Radius * transform.ScaleX;

        using var paint = new SKPaint
        {
            Color = stamp.Colour,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        // A soft stamp is a different shape, not a fainter one: full strength out to its
        // hardness and falling to nothing at the rim. Drawn as a shader rather than by
        // stacking rings, because a ring is a mark of its own and would composite like one.
        using var falloff = stamp.IsSoft
            ? SKShader.CreateRadialGradient(
                new SKPoint((float)centreX, (float)centreY), (float)radius,
                [stamp.Colour, stamp.Colour.WithAlpha(0)],
                [(float)Math.Clamp(stamp.Hardness, 0, 1), 1],
                SKShaderTileMode.Clamp)
            : null;

        if (falloff is not null)
        {
            paint.Shader = falloff;

            // The paint's own alpha multiplies whatever the shader produces, and the shader
            // is already carrying the stamp's. Left as it was, a quarter-alpha stamp came out
            // at a sixteenth -- faint enough to read as a soft brush being subtle rather than
            // as the colour being applied twice.
            paint.Color = stamp.Colour.WithAlpha(255);
        }

        // Given one, the stamp composites by it rather than by source-over. That is the whole
        // difference between a stroke that darkens itself and one that does not.
        if (blender is not null) paint.Blender = blender;

        surface.Canvas.DrawCircle((float)centreX, (float)centreY, (float)radius, paint);
    }
}
