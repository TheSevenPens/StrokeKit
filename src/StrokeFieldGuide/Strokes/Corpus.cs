
namespace StrokeFieldGuide.Strokes;

/// <summary>
/// One stroke from the corpus, with the reason it is in it.
/// </summary>
/// <param name="Name">
/// What to call it in a failure message.
/// <para>
/// Every name here begins <c>syn-</c>, so that a name alone says the stroke was generated
/// rather than recorded. Recordings of a real pen are <c>part:stroke-input</c>'s and will
/// need a prefix of their own; a corpus where the two are told apart by remembering which
/// list they came from is a corpus that will eventually mix them.
/// </para>
/// </param>
/// <param name="Exposes">
/// What this stroke is for: the fault it would show that a straight line at constant pressure
/// would not. Required, because a fixture nobody can say that about is a fixture that grew
/// out of whatever the last check happened to need.
/// </param>
public sealed record Fixture(string Name, string Exposes, IReadOnlyList<Reading> Readings)
{
    /// <summary>
    /// The stroke these readings make.
    /// </summary>
    /// <remarks>
    /// Every fixture here is exactly one stroke: the pressure never reaches zero, so nothing
    /// in the corpus lifts part-way. Where contact <i>does</i> break is
    /// <c>points-to-stroke</c>'s subject and a different question from what a brush does with
    /// a stroke it has been given.
    /// </remarks>
    public Stroke Stroke => Strokes.From(Readings) is [var only] ? only
        : throw new InvalidOperationException(
            $"{Name} is meant to be one stroke and is {Strokes.From(Readings).Count}");

    public override string ToString() => Name;
}

/// <summary>
/// The strokes this guide tests brushes against, named, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Before this, every fixture was built where it was used. Twenty-odd of them, none visible
/// to the next page, and two pages with a private <c>Ramp</c> written twice with different
/// defaults. The cost of that is not duplication. It is that a page's checks only ever see
/// the strokes that page's author thought of: <c>width-from-pressure</c> had four checks and
/// a figure that all ramped <b>upwards</b>, and the unsigned subtraction underneath them
/// survived every one.
/// </para>
/// <para>
/// So: named, shared, and enumerable. A check that runs over <see cref="All"/> is a much
/// stronger thing than a check that runs over the stroke its author had in mind.
/// </para>
/// <para>
/// <b>Generated, not recorded.</b> These stay synthetic on purpose -- a brush engine correct
/// on strokes with no timing noise, no dropouts and no jitter has one class of fault ruled
/// out, which is the whole premise of <c>part:brush-engine</c>. Recordings of a real pen
/// answer a different question and belong to <c>part:stroke-input</c>.
/// </para>
/// <para>
/// Every fixture fits inside 20..220 in both axes, so a 240x240 surface holds all of them
/// with room for a stamp of any reasonable diameter at the edges.
/// </para>
/// </remarks>
public static class Corpus
{
    /// <summary>The far side of the box every fixture stays inside.</summary>
    public const double Extent = 240;

    // ------------------------------------------------------------------ shape

    public static Fixture Straight => new(
        "syn-straight",
        "nothing. The baseline: if a property fails here it is not about shape, pressure or speed",
        Synthetic.Line(20, 120, 220, 120, 60));

    public static Fixture Hairpin => new(
        "syn-hairpin",
        "the sharpest turn there is. Stamps bunch at the point, and anything that outlines "
        + "rather than stamps puts a spike here",
        Then(Synthetic.Line(20, 100, 200, 100, 50), Synthetic.Line(200, 100, 20, 112, 50)));

    public static Fixture RightAngle => new(
        "syn-right-angle",
        "a corner that is sharp but not a reversal, where the carried distance has to cross "
        + "the turn rather than restart at it",
        Then(Synthetic.Line(40, 40, 200, 40, 40), Synthetic.Line(200, 40, 200, 200, 40)));

    public static Fixture Curve => new(
        "syn-curve",
        "direction changing at every reading rather than at one of them, which a fixture made "
        + "of straight legs never does",
        Synthetic.Arc(120, 120, 80, 180, 270, 60));

    public static Fixture Crossing => new(
        "syn-crossing",
        "a stroke that covers its own path at an angle, which is where compositing per stamp "
        + "darkens and compositing once does not",
        Then(
            Then(Synthetic.Line(40, 40, 200, 200, 50), Synthetic.Line(200, 200, 40, 200, 50)),
            Synthetic.Line(40, 200, 200, 40, 50)));

    public static Fixture Retrace => new(
        "syn-retrace",
        "the same ground covered twice in opposite directions. Where the length divides the "
        + "spacing the return lands exactly on the outward stamps, which is the worst case "
        + "for anything that assumes stamps do not coincide",
        Then(Synthetic.Line(20, 120, 220, 120, 50), Synthetic.Line(220, 120, 20, 120, 50)));

    public static Fixture Circled => new(
        "syn-circled",
        "the same circle three times over, so every point of the path is covered three times. "
        + "The strongest test of a claim that a stroke never exceeds its stamp's alpha",
        Synthetic.Arc(120, 120, 80, 0, 1080, 360));

    // ------------------------------------------------------------------- size

    public static Fixture Dot => new(
        "syn-dot",
        "one reading. No length, no direction, no segments: every loop over a stroke's "
        + "segments runs zero times and every division by its length divides by zero",
        Synthetic.Tap(120, 120));

    public static Fixture Flick => new(
        "syn-flick",
        "a stroke shorter than an ordinary spacing, which gets one stamp and makes a mark the "
        + "size of the brush rather than the length of the gesture",
        Synthetic.Line(120, 120, 122, 120, 3));

    // --------------------------------------------------------------- pressure

    public static Fixture Swell => new(
        "syn-swell",
        "pressure rising to the middle and falling again, which is the shape of an ordinary "
        + "stroke and the only fixture here that exercises the interpolation in both "
        + "directions at once",
        Pressed(Synthetic.Line(20, 120, 220, 120, 120),
            along => (uint)Math.Round(80 + 920 * Math.Sin(along * Math.PI))));

    public static Fixture TapOff => new(
        "syn-tap-off",
        "pressure falling the whole way, which is the direction that used to wrap to full "
        + "width in unsigned arithmetic and looked correct in every rising fixture",
        Pressed(Synthetic.Line(20, 120, 220, 120, 120),
            along => (uint)Math.Round(900 - 840 * along)));

    public static Fixture PressedInPlace => new(
        "syn-pressed-in-place",
        "the hand stopped and the pressure still moving. Every segment has zero length, so "
        + "anything that divides by a segment's length or advances a walk by it has to cope "
        + "with a stroke that changes without going anywhere",
        Pressed(
            [.. Enumerable.Repeat(Synthetic.Reading(120, 120), 40)
                .Select((reading, index) => Synthetic.Reading(120, 120, at: index * 8000L))],
            along => (uint)Math.Round(100 + 900 * along)));

    // ----------------------------------------------------------------- timing

    public static Fixture Hurried => new(
        "syn-hurried",
        "the same path as straight, reported eight times instead of sixty. A hand moving fast "
        + "on a pen that reports on a clock, which is what spacing by distance exists to make "
        + "no difference to",
        Synthetic.Line(20, 120, 220, 120, 8));

    // ------------------------------------------------- spacing, within a stroke

    public static Fixture Quickened => new(
        "syn-quickened",
        "the step growing as the stroke goes: hurried and dawdled inside one stroke rather "
        + "than as a pair of them. Anything whose behaviour depends on the distance between "
        + "readings meets every distance here, in one mark",
        Synthetic.Quickening(20, 120, 220, 120, 40, bias: 3));

    public static Fixture Jolted => new(
        "syn-jolted",
        "one wide gap among even ones. Against quickened it separates a fault that needs a "
        + "trend from a fault that needs a single step out of place, which a recording — where "
        + "everything varies at once — cannot",
        Then(Synthetic.Line(20, 120, 100, 120, 20),
             Synthetic.Line(160, 120, 220, 120, 15)));

    public static Fixture Stalled => new(
        "syn-stalled",
        "readings that do not move, and then do. The steps at the start are zero, not small, "
        + "which is a division waiting to happen in anything that normalises a direction — and "
        + "which nothing else generated here contains",
        Synthetic.Stalled(60, 120, 220, 120, still: 12, moving: 20));

    public static Fixture Dawdled => new(
        "syn-dawdled",
        "the same path again, reported two hundred times. Against hurried it says whether a "
        + "mark follows the path or the report rate",
        Synthetic.Line(20, 120, 220, 120, 200));

    /// <summary>
    /// Every fixture, for a check that wants to state a property of brushes rather than a
    /// property of one stroke.
    /// </summary>
    public static IReadOnlyList<Fixture> All =>
    [
        Straight, Hairpin, RightAngle, Curve, Crossing, Retrace, Circled,
        Dot, Flick,
        Swell, TapOff, PressedInPlace,
        Hurried, Dawdled,
        Quickened, Jolted, Stalled,
    ];

    /// <summary>
    /// Readings joined end to end, with the second run's clock continued rather than restarted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reason this is a function and not a spread.</b> Written
    /// <c>[.. Line(a), .. Line(b)]</c> -- which is how every multi-segment fixture in this
    /// guide was built -- the second leg's timestamps start again at zero, so time runs
    /// backwards at the join. Measured on a two-leg fixture: <c>0 8000 16000 24000 32000 0
    /// 8000 16000 24000 32000</c>.
    /// </para>
    /// <para>
    /// Nothing in the brush engine reads a timestamp yet, so it has never mattered. Anything
    /// that works from speed would read a negative interval at every corner, and a corner is
    /// exactly where such a control is judged.
    /// </para>
    /// <para>
    /// The join point itself is dropped from the second run, because the two legs share it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Reading> Then(
        IReadOnlyList<Reading> first, IReadOnlyList<Reading> second)
    {
        if (first.Count == 0) return second;
        if (second.Count == 0) return first;

        // The second run's own cadence, so continuing the clock does not also change the rate.
        var cadence = second.Count > 1
            ? second[1].At - second[0].At
            : 8000;

        var from = first[^1].At + cadence;

        var joined = new List<Reading>(first);

        for (var index = 1; index < second.Count; index++)
        {
            var reading = second[index];

            joined.Add(Synthetic.Reading(
                reading.X, reading.Y, reading.Pressure,
                from + reading.At - second[0].At - cadence));
        }

        return joined;
    }

    /// <summary>
    /// The same readings with a pressure profile applied along them, from 0 at the first to 1
    /// at the last.
    /// </summary>
    private static IReadOnlyList<Reading> Pressed(
        IReadOnlyList<Reading> readings, Func<double, uint> profile)
    {
        var pressed = new List<Reading>(readings.Count);

        for (var index = 0; index < readings.Count; index++)
        {
            var along = readings.Count > 1 ? index / (double)(readings.Count - 1) : 0;

            pressed.Add(Synthetic.Reading(
                readings[index].X, readings[index].Y,
                profile(along), readings[index].At));
        }

        return pressed;
    }
}
