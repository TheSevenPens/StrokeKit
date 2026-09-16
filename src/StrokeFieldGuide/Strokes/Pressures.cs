namespace StrokeFieldGuide.Strokes;

/// <summary>
/// Arithmetic on raw pressures, which are unsigned.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>PenPoint.Pressure</c> is a <c>uint</c>, so the obvious interpolation is wrong in one
/// direction only.</b> Written inline, <c>from + (to - from) * fraction</c> subtracts before
/// anything becomes a <c>double</c>: for a falling pressure the difference wraps to about
/// 4.29e9, the result saturates, and every stamp comes out at full width.
/// </para>
/// <para>
/// A rising pressure is unaffected, which is why this survived a page of checks -- every
/// fixture in them ramped upwards. It exists as a named function so the conversion happens in
/// one place rather than being got right at four call sites and wrong at the fifth.
/// </para>
/// </remarks>
public static class Pressures
{
    /// <summary>
    /// The pressure a <paramref name="fraction"/> of the way from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// Answers a <c>double</c> rather than a <c>uint</c> because a stamp between two readings
    /// is genuinely between two pressures, and rounding is the caller's business at the point
    /// it needs a device-scale number.
    /// </remarks>
    public static double Between(uint from, uint to, double fraction) =>
        from + ((double)to - from) * fraction;
}
