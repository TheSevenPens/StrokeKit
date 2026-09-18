namespace StrokeKit.Brushes;

/// <summary>
/// Which engine lays a brush's marks down.
/// </summary>
/// <remarks>
/// A stamping engine puts down discrete marks and everything expressive hangs off them; an
/// outlining engine fills one region and gets, in exchange, overlaps that are not overlaps.
/// Choosing between them is the largest thing a brush decides, and
/// <c>outlining-and-stamping</c> is what each one costs.
/// <para>
/// The third is an outlining engine with the cutting taken out, and it is here because the
/// cutting is what stops the other one being drawn live.
/// </para>
/// </remarks>
public enum Engine
{
    /// <summary>Round marks laid every fixed distance along the path.</summary>
    Stamps,

    /// <summary>One filled outline swept between the round ends of each piece of the path.</summary>
    Taper,

    /// <summary>
    /// One filled outline between each pair of consecutive readings, and a dot at the first.
    /// </summary>
    /// <remarks>
    /// It decides nothing about the path as a whole, so it can be drawn while the stroke is
    /// still arriving, and a stroke drawn that way makes the same mark as the same stroke
    /// drawn in one call. What it gives up is what the cutting was for: its mark depends on
    /// how often the tablet reported. See <see cref="SampleTapers"/>.
    /// </remarks>
    SampleTaper,
}
