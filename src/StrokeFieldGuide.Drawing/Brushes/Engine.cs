namespace StrokeFieldGuide.Brushes;

/// <summary>
/// Which engine lays a brush's marks down.
/// </summary>
/// <remarks>
/// Two, and they are not variations on each other. A stamping engine puts down discrete
/// marks and everything expressive hangs off them; an outlining engine fills one region and
/// gets, in exchange, overlaps that are not overlaps. Choosing between them is the largest
/// thing a brush decides, and <c>outlining-and-stamping</c> is what each one costs.
/// </remarks>
public enum Engine
{
    /// <summary>Round marks laid every fixed distance along the path.</summary>
    Stamps,

    /// <summary>One filled outline swept between the round ends of each piece of the path.</summary>
    Taper,
}
