namespace StrokeFieldGuide.Strokes;

/// <summary>
/// An angle on a circle, in degrees, which is not a number on a line.
/// </summary>
/// <remarks>
/// <para>
/// A type rather than a <c>double</c>, and with <b>no conversion to one</b>, because the
/// operations a stroke pipeline performs on every other field are all wrong here. Subtracting
/// 359 from 1 gives -358 where the pen turned two degrees. Interpolating halfway between 350
/// and 10 gives 180 — the pen pointing exactly the wrong way, for the whole span between two
/// readings. Averaging them gives 180 again.
/// </para>
/// <para>
/// <see cref="Pressures"/> is the precedent and says why it exists: the inline arithmetic was
/// wrong in one direction only, and it survived a page of checks because every fixture ramped
/// upward. This is the same trap with a wider blast radius, because a stroke that never
/// crosses zero passes everything.
/// </para>
/// <para>
/// <b>What this cannot do.</b> At exactly 180 degrees apart there is no shortest way round:
/// both are equal, and <see cref="To"/> answers -180 by convention. The ambiguity is real — a
/// pen rolled exactly half a turn between two readings is indistinguishable from one rolled
/// half a turn the other way. That is a sampling limit and not a rounding error, and no
/// arithmetic recovers it.
/// </para>
/// </remarks>
public readonly record struct Turn
{
    private Turn(double degrees) => Degrees = degrees;

    /// <summary>Where it points, in [0, 360).</summary>
    public double Degrees { get; }

    public double Radians => Degrees * Math.PI / 180;

    /// <summary>
    /// The turn a device reported, brought into range.
    /// </summary>
    /// <remarks>
    /// Normalised on the way in, so nothing downstream has to wonder whether it was. A
    /// negative input is as legitimate as a positive one: -10 and 350 are the same direction
    /// and this is where they stop being two numbers.
    /// </remarks>
    public static Turn At(double degrees)
    {
        var wrapped = degrees % 360;

        if (wrapped < 0) wrapped += 360;

        // A hair below zero lands a hair below 360 after that addition, and at this
        // magnitude the addition rounds to exactly 360 -- so a direction that is zero comes
        // out as a full circle. It reached the probe as an average of 350 and 10 reading
        // 360.0 instead of 0.0, which is right modulo a circle and wrong for anything that
        // compares two turns.
        if (wrapped >= 360) wrapped = 0;

        return new(wrapped);
    }

    /// <summary>
    /// The signed shortest way from this to <paramref name="other"/>, in [-180, 180).
    /// </summary>
    /// <remarks>
    /// Positive is the direction of increasing degrees. At exactly 180 the answer is
    /// <b>-180</b>, which is a convention and not a measurement: the two ways round are the
    /// same length and nothing in the readings says which the hand took. The half-open range
    /// is which end the arithmetic below happens to put it at, and it is stated here because
    /// the first version of these remarks said the opposite and the probe said otherwise.
    /// </remarks>
    public double To(Turn other) => ((other.Degrees - Degrees + 540) % 360) - 180;

    /// <summary>
    /// The turn a <paramref name="fraction"/> of the way to <paramref name="other"/>, the
    /// short way round.
    /// </summary>
    public Turn Towards(Turn other, double fraction) => At(Degrees + To(other) * fraction);

    /// <summary>
    /// Where a run of turns points on average.
    /// </summary>
    /// <remarks>
    /// Summed as unit vectors rather than as numbers, because shortest-arc steps do not
    /// compose: chaining them pairwise gives a different answer depending on the order, and
    /// averaging the degrees of 350 and 10 gives 180. This is the only form that is both
    /// order-independent and right across the seam.
    /// <para>
    /// A set with no direction — two turns exactly opposite, or four at the compass points —
    /// has no average, and this answers null rather than one of them. It is a real absence:
    /// the directions cancel, and any angle would be as good as any other.
    /// </para>
    /// </remarks>
    public static Turn? Average(IEnumerable<Turn> turns)
    {
        var x = 0.0;
        var y = 0.0;

        foreach (var turn in turns)
        {
            x += Math.Cos(turn.Radians);
            y += Math.Sin(turn.Radians);
        }

        // Not a tolerance on the inputs: a genuinely cancelling set lands here, and so does
        // one whose resultant is too short for its direction to mean anything.
        if (Math.Sqrt(x * x + y * y) < 1e-12) return null;

        return At(Math.Atan2(y, x) * 180 / Math.PI);
    }

    /// <summary>
    /// How far the pen turned in total across a run of readings, which may exceed a circle.
    /// </summary>
    /// <remarks>
    /// Shortest-arc steps summed, so this recovers a pen that rolled twice round — but only
    /// while every true step is under half a turn. At the 240 readings a second measured on a
    /// Cintiq that means under 43,200 degrees a second, which no hand approaches. The
    /// condition is stated because it is what makes the answer trustworthy, not because it is
    /// close.
    /// </remarks>
    public static double Total(IReadOnlyList<Turn> turns)
    {
        var total = 0.0;

        for (var index = 1; index < turns.Count; index++) total += turns[index - 1].To(turns[index]);

        return total;
    }

    public override string ToString() => $"{Degrees:F1}°";
}
