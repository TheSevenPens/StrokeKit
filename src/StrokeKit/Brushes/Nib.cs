namespace StrokeKit.Brushes;

/// <summary>Where a nib's angle comes from.</summary>
public enum Held
{
    /// <summary>One angle for the whole stroke, like a calligraphy pen held in the hand.</summary>
    Fixed,

    /// <summary>
    /// Turned to follow the direction of travel, so the nib presents the same face throughout.
    /// </summary>
    /// <remarks>
    /// A ribbon rather than a pen. <see cref="Nib.Degrees"/> is then an offset from the
    /// direction rather than an angle: zero lays the long axis along the path, ninety lays it
    /// across.
    /// </remarks>
    ToThePath,

    /// <summary>
    /// Turned the way the pen is leaning, which is what a real nib does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A nib turns because the hand turns, not because the stroke does. The reading already
    /// carries which way the hand has tipped the pen -- its azimuth -- so this is one field
    /// rather than an arctangent over a pair of tilts whose sign convention differs between
    /// devices.
    /// </para>
    /// <para>
    /// <b>An upright pen has no azimuth</b>, and a device reporting one there is reporting
    /// nothing. The angle then falls back to <see cref="Nib.Degrees"/>, which is the same
    /// thing <see cref="Fixed"/> would have given -- the honest answer when the measurement
    /// is absent, rather than whatever number arrived in its place.
    /// </para>
    /// </remarks>
    ToTheLean,
}

/// <summary>
/// The shape of the mark a brush stamps, when it is not a circle.
/// </summary>
/// <remarks>
/// <para>
/// A circle is the only shape whose mark does not depend on which way it is going, which is
/// why every page before this one could ignore the question. An elliptical nib is what makes
/// a chisel, a flat brush and every calligraphic hand: the mark is broad across one direction
/// and thin across the other, and the stroke's weight then depends on where the hand is
/// travelling rather than on how hard it presses.
/// </para>
/// <para>
/// <b>A stamping engine only.</b> An outlined stroke has one shape for the whole mark rather
/// than one per stamp, and sweeping a non-round nib along a path is a different construction
/// from sweeping a circle -- see <c>outlining-and-stamping</c>.
/// </para>
/// </remarks>
/// <param name="Ratio">
/// The short axis as a fraction of the long one, from just above 0 to 1. One is a circle.
/// </param>
/// <param name="Degrees">
/// Where the long axis points. An angle when the nib is <see cref="Held.Fixed"/>, and an
/// offset from the direction of travel when it is <see cref="Held.ToThePath"/>.
/// </param>
/// <param name="Held">Whether the angle is the hand's or the path's.</param>
public readonly record struct Nib(double Ratio, double Degrees = 0, Held Held = Held.Fixed)
{
    /// <summary>A round nib, which is what every brush without one has.</summary>
    public static Nib Round => new(1);

    /// <summary>True when the nib's mark depends on which way it is going.</summary>
    public bool IsRound => Ratio >= 1;

    /// <summary>
    /// The nib's extent along a direction of travel, as a fraction of the long axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What spacing has to be measured against. A round nib is the same width whichever way
    /// it goes, so a spacing in diameters means one thing; an elliptical one is as wide as
    /// its long axis when travelling along it and as narrow as its short axis when travelling
    /// across, and a spacing that ignored the difference would leave the second beaded.
    /// </para>
    /// <para>
    /// For a unit-long-axis ellipse at angle <c>t</c> to the travel, it is
    /// <c>1 / sqrt(cos^2 t + (sin t / Ratio)^2)</c>.
    /// </para>
    /// <para>
    /// <b>Not the width of the ellipse's shadow on the travel.</b> That is
    /// <c>sqrt(cos^2 t + (Ratio sin t)^2)</c>, which agrees with this at 0 and at 90 degrees
    /// and disagrees everywhere between -- at 30 degrees on a 0.2 nib it says 0.87 where the
    /// answer is 0.38. Written the first way and checked only at the two ends, it looks
    /// right. Only an oblique angle tells them apart.
    /// </para>
    /// </remarks>
    public double AlongTravel(double travelDegrees) => Turned(Degrees - travelDegrees);

    /// <summary>
    /// The same extent, given the angle between the long axis and the travel directly.
    /// </summary>
    /// <remarks>
    /// <see cref="AlongTravel"/> takes that angle to be <c>Degrees - travel</c>, which is
    /// true of a nib the hand holds at one angle and false of every other <see cref="Held"/>:
    /// a nib turned to the path is at a constant angle to it, so its extent along the travel
    /// does not change at all, and a nib turned to the lean is at whatever angle the hand is
    /// holding it at. A caller that already knows where the nib is pointing -- which the
    /// brush does, because it lays the stamp -- subtracts for itself and asks this.
    /// </remarks>
    public double Turned(double betweenDegrees)
    {
        if (IsRound) return 1;

        var t = betweenDegrees * Math.PI / 180;
        var ratio = Math.Clamp(Ratio, 0.001, 1);

        return 1 / Math.Sqrt(Math.Pow(Math.Cos(t), 2) + Math.Pow(Math.Sin(t) / ratio, 2));
    }
}
