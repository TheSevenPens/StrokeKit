namespace StrokeKit.Brushes;

/// <summary>
/// How hard a brush responds across the part of the pen's range it uses.
/// </summary>
/// <remarks>
/// <para>
/// Three numbers rather than a spline. <see cref="Start"/> and <see cref="End"/> pick the part
/// of the range the brush answers to, and <see cref="Exponent"/> shapes the answer across it:
/// </para>
/// <code>
/// t   = clamp((fraction - Start) / (End - Start), 0, 1)
/// out = pow(t, Exponent)
/// </code>
/// <para>
/// <see cref="Start"/> removes the dead weight at the bottom of a tablet's range, which is what
/// makes a light touch mark at all on hardware reporting a couple of hundred counts before
/// anything happens. <see cref="End"/> lets a brush reach full strength without bottoming the
/// nib out. <see cref="Exponent"/> above 1 holds the brush light until it is leaned on; below 1
/// it comes on early and saturates.
/// </para>
/// <para>
/// <b>What it cannot say.</b> One inflection, so no S-curve; monotone, so no hump; and it always
/// runs from 0 to 1, so it cannot fall. That last one matters: a response that narrows as the
/// pen presses is a real brush, and it is expressed by the property's own endpoints rather than
/// here -- <c>Width(24, 6, range)</c> still narrows, and would not if this had replaced the
/// endpoints instead of sitting between them. The family is a bounded first version and the
/// evaluator is meant to be replaceable.
/// </para>
/// </remarks>
public readonly record struct Response
{
    // Stored as differences from the linear curve, so that default(Response) is linear rather
    // than merely usually constructed that way.
    //
    // A field initialiser does not run for default(T), for an array element, or for a struct
    // read back from a deserialiser. Written the obvious way -- _end = 1 with an initialiser --
    // default(Response) has Start 0, End 0 and Exponent 0, which is a zero-width range and
    // answers 1 to every reading. A curve meaning "leave the pen alone" that silently means
    // "full strength always" is the kind of fault this guide exists to catch, and it was caught
    // in review of the code this was ported from.
    private readonly double _startFromZero;
    private readonly double _endFromOne;
    private readonly double _exponentFromOne;

    public Response() { }

    public Response(double start, double end, double exponent)
    {
        Start = start;
        End = end;
        Exponent = exponent;
    }

    /// <summary>The reading reaches the brush unchanged.</summary>
    public static Response Linear => default;

    /// <summary>A reading at or below this asks for nothing. Clamped to [0, 1].</summary>
    public double Start
    {
        get => _startFromZero;
        init => _startFromZero = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
    }

    /// <summary>A reading at or above this asks for everything. Clamped to [0, 1].</summary>
    public double End
    {
        get => 1 + _endFromOne;
        init => _endFromOne = (double.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1)) - 1;
    }

    /// <summary>
    /// Shapes the answer between <see cref="Start"/> and <see cref="End"/>. 1 is a straight line.
    /// </summary>
    /// <remarks>
    /// Clamped to [0.1, 8]. Zero would make every contact full strength and a negative one would
    /// send a light touch to infinity, and neither is a brush.
    /// </remarks>
    public double Exponent
    {
        get => 1 + _exponentFromOne;
        init => _exponentFromOne = (double.IsNaN(value) ? 1 : Math.Clamp(value, 0.1, 8)) - 1;
    }

    /// <summary>True when this curve leaves the reading alone.</summary>
    public bool IsLinear => Start == 0 && End == 1 && Exponent == 1;

    /// <summary>
    /// What the brush makes of one reading, as a fraction of the device's range. In and out are
    /// both 0 to 1.
    /// </summary>
    /// <remarks>
    /// A range of no width -- <see cref="Start"/> at or past <see cref="End"/> -- is a threshold:
    /// nothing below it, everything at or above. Stated as a chosen policy rather than as the
    /// limit of the arithmetic, because it is a usable brush in its own right and because the
    /// arithmetic approaches it from one side only.
    /// </remarks>
    public double Of(double fraction)
    {
        if (double.IsNaN(fraction)) return 0;

        fraction = Math.Clamp(fraction, 0, 1);

        var span = End - Start;
        if (span <= 0) return fraction >= End ? 1 : 0;

        var t = Math.Clamp((fraction - Start) / span, 0, 1);

        return Exponent == 1 ? t : Math.Pow(t, Exponent);
    }

    /// <summary>
    /// What several inputs together ask of one property: the product of what each asks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The product, and it is a choice.</b> A pen held light and leaned over at once gives a
    /// thinner mark than either alone, and a second input can be switched on without retuning
    /// the first. Taking the smallest instead would leave whichever input is not currently
    /// winning doing nothing at all.
    /// </para>
    /// <para>
    /// It is <i>not</i> "the most restrictive wins", and calling it that hides what it costs:
    /// two inputs at 0.5 give 0.25, where the smallest would give 0.5, and four give 0.0625.
    /// Adding an input changes the mark every other input was tuned against.
    /// </para>
    /// <para>
    /// <b>The floor is not in here.</b> A property that keeps some of itself at the weakest
    /// reading says so with its own endpoints -- <c>Width</c>'s <c>AtNoPressure</c> -- which is
    /// one floor for the property however many inputs drive it. A floor per input would
    /// multiply with the rest: four inputs each keeping 0.35 would keep 0.015 between them,
    /// which is nobody's idea of a floor.
    /// </para>
    /// </remarks>
    public static double Together(params double[] asked)
    {
        var scale = 1.0;

        foreach (var one in asked) scale *= Math.Clamp(one, 0, 1);

        return scale;
    }
}
