namespace StrokeKit.Brushes;

/// <summary>
/// How a reading's pressure becomes the alpha of one stamp.
/// </summary>
/// <remarks>
/// <para>
/// The same arithmetic as <see cref="Width"/> on a different property, and named separately
/// because the two want different curves. A brush usually wants its width to come on faster
/// than its ink: one shared curve can say that pressure drives both and cannot say that it
/// drives them differently, which is the ordinary case.
/// </para>
/// <para>
/// <b>This is the alpha of a stamp, not a ceiling on the stroke.</b> Under
/// <see cref="Buildup.OncePerStroke"/> the stamps of a stroke meet each other before they meet
/// the surface, so the stroke reaches the greatest alpha any of its stamps asked for. Under
/// <see cref="Buildup.PerStamp"/> they accumulate and the stroke goes darker than any of them.
/// A separate ceiling for the whole stroke is a second quantity and is not here yet; see
/// <c>brush-control-contract</c> for what it would mean under each buildup.
/// </para>
/// </remarks>
/// <param name="AtNoPressure">
/// The alpha at the weakest reading the curve answers to, from 0 to 1.
/// <para>
/// This is the floor, and it is here rather than on an input because floors on inputs
/// multiply. A brush whose ink fades to nothing and one that thins to a sixth are different
/// brushes, and no curve running to zero can express the second.
/// </para>
/// </param>
/// <param name="AtFullPressure">The alpha at the device's full scale, from 0 to 1.</param>
/// <param name="Range">
/// The device's full-scale pressure, for the same reason <see cref="Width"/> takes one: a
/// reading does not carry it. The two must be given the same range -- size and ink reading the
/// same pen against different scales is a brush that cannot be reasoned about.
/// </param>
/// <param name="Curve">How hard the brush answers across the range. Linear by default.</param>
public readonly record struct Flow(
    double AtNoPressure, double AtFullPressure, uint Range, Response Curve = default)
{
    /// <summary>The alpha for a raw reading, from 0 to 1.</summary>
    public double For(uint pressure) =>
        Math.Clamp(Driven.Between(AtNoPressure, AtFullPressure, Range, Curve, pressure), 0, 1);

    /// <summary>The alpha for a raw reading, as the byte a colour takes.</summary>
    public byte AlphaFor(uint pressure) => (byte)Math.Clamp(Math.Round(For(pressure) * 255), 0, 255);
}

/// <summary>
/// One property of a mark, run between two endpoints by a reading and a curve.
/// </summary>
/// <remarks>
/// Shared by <see cref="Width"/> and <see cref="Flow"/> so there is one place the order is
/// decided. The order is <c>brush-control-contract</c>'s: normalise the reading against the
/// device's range, shape the fraction, then map it between the endpoints. Shaping before
/// normalising would read a curve against raw counts, and mapping before shaping would put a
/// straight line through a curve's output.
/// </remarks>
internal static class Driven
{
    public static double Between(
        double atNoPressure, double atFullPressure, uint range, Response curve, uint pressure)
    {
        if (range == 0) throw new InvalidOperationException("a pressure range is not zero");

        var fraction = Math.Clamp(pressure / (double)range, 0, 1);

        return atNoPressure + (atFullPressure - atNoPressure) * curve.Of(fraction);
    }
}
