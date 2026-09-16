namespace StrokeFieldGuide.Brushes;

/// <summary>
/// How a reading's pressure becomes a stamp's diameter.
/// </summary>
/// <param name="AtNoPressure">
/// The diameter at a pressure of zero. Rarely zero itself: a stroke that tapers to nothing
/// disappears at both ends, and a device whose first in-contact reading is already at some
/// pressure never reaches this value anyway.
/// </param>
/// <param name="AtFullPressure">The diameter at the device's full scale.</param>
/// <param name="Range">
/// The device's full-scale pressure. Stated here because <b>a reading does not carry
/// it</b> -- <c>Reading.Pressure</c> is a raw number and the range is the session's, so a
/// brush that assumes 1024 draws at an eighth strength on a device reporting 8192 and at
/// full strength on one reporting 127.
/// <para>
/// Required rather than defaulted, so that the question is answered where a brush is made
/// instead of being answered wrongly somewhere quiet.
/// </para>
/// </param>
/// <param name="Curve">
/// How hard the brush answers across the range. Linear by default, which is the response every
/// page written before this one was measured against.
/// <para>
/// Between the endpoints rather than replacing them, so that a width which <b>narrows</b> as
/// the pen presses is still expressible: the curve runs 0 to 1 and cannot fall, and
/// <c>Width(24, 6, range)</c> narrows because its endpoints say so.
/// </para>
/// </param>
public readonly record struct Width(
    double AtNoPressure, double AtFullPressure, uint Range, Response Curve = default)
{
    /// <summary>The diameter for a raw reading, clamped to the range it was measured in.</summary>
    /// <remarks>
    /// The order the contract states: the reading is normalised against the device's range,
    /// the curve shapes the fraction, and the endpoints map it to a diameter. Anything that
    /// shaped before normalising would be reading a curve against raw counts.
    /// </remarks>
    public double For(uint pressure) =>
        Driven.Between(AtNoPressure, AtFullPressure, Range, Curve, pressure);
}
