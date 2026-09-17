namespace StrokeKit.Strokes;

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
/// <param name="Across">Towards the east, which is rightwards on a surface.</param>
/// <param name="Down">
/// Towards the south, which is downwards on a surface — so a pen leaning <b>north</b> has a
/// negative one of these.
/// </param>
public readonly record struct Leaning(double Across, double Down)
{
    /// <summary>The pen upright: no lean, and therefore no direction.</summary>
    public static Leaning Upright => new(0, 0);

    /// <summary>
    /// The lean a bearing and a distance describe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An azimuth is a compass bearing, not an angle from the x axis.</b> Zero points north
    /// — away from the hand, up a surface — and it increases clockwise, so 90 is east. This is
    /// what Wintab reports and it is not what the obvious <c>cos</c> and <c>sin</c> give.
    /// </para>
    /// <para>
    /// Measured on 16 September 2026 by tipping the far end of the pen east and watching where
    /// a dial put the lean: it pointed south, a quarter turn clockwise of where the hand was.
    /// The same convention falls out of WinPenKit's own conversion — its
    /// <c>SphericalToTiltX/Y</c> give a negative x for a bearing of 90 and a positive y for a
    /// bearing of 0 — and out of the tilt signs measured on the same tablet, where leaning
    /// right reports a negative x and leaning up a positive y.
    /// </para>
    /// </remarks>
    public static Leaning From(double lean, Turn azimuth) =>
        new(lean * Math.Sin(azimuth.Radians), -lean * Math.Cos(azimuth.Radians));

    public static Leaning Of(Reading reading) => From(reading.Lean, Turn.At(reading.Azimuth));

    /// <summary>Degrees from vertical.</summary>
    public double Lean => Math.Sqrt(Across * Across + Down * Down);

    /// <summary>
    /// The compass bearing it leans along, or null when it does not lean at all.
    /// </summary>
    /// <remarks>
    /// Null rather than zero, because zero is a direction — north — and "no direction" is not
    /// one. A caller wanting a nib angle from an upright pen has to decide what to do about
    /// it, which is the point: there is nothing here to decide it for them.
    /// </remarks>
    public Turn? Azimuth => Lean < 1e-12
        ? null
        : Turn.At(Math.Atan2(Across, -Down) * 180 / Math.PI);

    /// <summary>
    /// The same direction as an angle to draw at, which is not the same number.
    /// </summary>
    /// <remarks>
    /// A drawing angle is measured from the x axis with y increasing downwards, and a bearing
    /// is measured from north increasing clockwise. They differ by a quarter turn — the
    /// drawing angle is the bearing less ninety — and using one where the other is wanted
    /// puts a nib square across the direction it should lie along, which looks like a
    /// deliberate choice rather than a mistake.
    /// <para>
    /// Both are offered, named for what they are, because a reader needs the bearing to talk
    /// about the hand and the drawing angle to lay a stamp.
    /// </para>
    /// </remarks>
    public Turn? Direction => Lean < 1e-12
        ? null
        : Turn.At(Math.Atan2(Down, Across) * 180 / Math.PI);

    /// <summary>The lean a <paramref name="fraction"/> of the way to <paramref name="other"/>.</summary>
    /// <remarks>
    /// Straight-line interpolation, which is the whole trick: in this form it is correct, and
    /// it is correct for the two reasons the remarks on this type give rather than by
    /// accident.
    /// </remarks>
    public Leaning Towards(Leaning other, double fraction) =>
        new(Across + (other.Across - Across) * fraction, Down + (other.Down - Down) * fraction);
}
