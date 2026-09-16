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
/// The device's full-scale pressure. Stated here because <b>a pen point does not carry
/// it</b> -- <c>PenPoint.Pressure</c> is a raw number and the range is the session's, so a
/// brush that assumes 1024 draws at an eighth strength on a device reporting 8192 and at
/// full strength on one reporting 127.
/// <para>
/// Required rather than defaulted, so that the question is answered where a brush is made
/// instead of being answered wrongly somewhere quiet.
/// </para>
/// </param>
public readonly record struct Width(double AtNoPressure, double AtFullPressure, uint Range)
{
    /// <summary>The diameter for a raw reading, clamped to the range it was measured in.</summary>
    public double For(uint pressure)
    {
        if (Range == 0) throw new InvalidOperationException("a pressure range is not zero");

        var fraction = Math.Clamp(pressure / (double)Range, 0, 1);

        return AtNoPressure + (AtFullPressure - AtNoPressure) * fraction;
    }
}
