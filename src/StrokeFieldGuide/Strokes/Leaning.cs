namespace StrokeFieldGuide.Strokes;

/// <summary>
/// How the pen was held, as the vector it is rather than as two numbers.
/// </summary>
/// <remarks>
/// <para>
/// A lean and an azimuth are polar coordinates: how far from vertical, and which way. Stored
/// that way on a <see cref="Reading"/> because that is the form a nib wants — the azimuth is
/// the nib's angle and the lean is how far it foreshortens. Interpolated that way, they are
/// wrong twice.
/// </para>
/// <para>
/// <b>The azimuth wraps.</b> Halfway between 350 and 10 is 0, and interpolating the numbers
/// gives 180. That is <see cref="Turn"/>'s subject, and <see cref="Turn.Towards"/> fixes it
/// for an angle that has nothing else to say.
/// </para>
/// <para>
/// <b>And the azimuth means nothing when the lean is zero.</b> A pen standing straight up is
/// not leaning in any direction, so a reading taken there carries a number that is not a
/// measurement. Interpolating towards it, or away from it, is interpolating towards noise —
/// and a pen that passes through vertical and comes out leaning the other way has an azimuth
/// that jumps by 180 with nothing wrong.
/// </para>
/// <para>
/// Both dissolve in the vector form. The path from one lean to another goes the short way
/// round by construction, and a near-vertical reading has a near-zero magnitude, so it
/// contributes almost nothing to the direction — which is exactly what an unmeasured azimuth
/// deserves. A pen passing through vertical traces a path <b>through the origin</b> rather
/// than swinging round the rim.
/// </para>
/// <para>
/// So the two channels want different treatments, and the reason is statable: twist is a pure
/// angle and needs the angle machinery; the lean direction is not, because it has a length.
/// </para>
/// </remarks>
public readonly record struct Leaning(double X, double Y)
{
    /// <summary>The pen upright: no lean, and therefore no direction.</summary>
    public static Leaning Upright => new(0, 0);

    public static Leaning From(double lean, Turn azimuth) =>
        new(lean * Math.Cos(azimuth.Radians), lean * Math.Sin(azimuth.Radians));

    public static Leaning Of(Reading reading) => From(reading.Lean, Turn.At(reading.Azimuth));

    /// <summary>Degrees from vertical.</summary>
    public double Lean => Math.Sqrt(X * X + Y * Y);

    /// <summary>
    /// Which way it leans, or null when it does not lean at all.
    /// </summary>
    /// <remarks>
    /// Null rather than zero, because zero is a direction and "no direction" is not one. A
    /// caller that wants a nib angle from an upright pen has to decide what to do about it,
    /// which is the point: there is nothing here to decide it for them.
    /// </remarks>
    public Turn? Azimuth => Lean < 1e-12
        ? null
        : Turn.At(Math.Atan2(Y, X) * 180 / Math.PI);

    /// <summary>The lean a <paramref name="fraction"/> of the way to <paramref name="other"/>.</summary>
    /// <remarks>
    /// Straight-line interpolation, which is the whole trick: in this form it is correct, and
    /// it is correct for the two reasons the remarks above give rather than by accident.
    /// </remarks>
    public Leaning Towards(Leaning other, double fraction) =>
        new(X + (other.X - X) * fraction, Y + (other.Y - Y) * fraction);
}
