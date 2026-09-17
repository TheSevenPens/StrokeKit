
namespace StrokeFieldGuide.Strokes;

/// <summary>
/// The readings of one continuous contact, in the order they arrived.
/// <para>
/// Abstract, and deliberately so. A stroke has no width, no colour and no shape: it is the
/// readings and nothing more. What a brush engine needs from one in order to draw it is a
/// separate question, and a separate page.
/// </para>
/// <para>
/// <b>This type is the finished form.</b> While the pen is down a stroke is still arriving,
/// and an interactive pipeline draws each new point as it comes rather than waiting for the
/// lift. The two views agree because a growing stroke is a prefix of the finished one, which
/// the points-to-stroke page checks. Nothing here yet serves the growing case, because every
/// page using it so far replays a recording.
/// </para>
/// </summary>
public sealed class Stroke
{
    /// <summary>
    /// A stroke over a list the caller keeps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The list is borrowed, not copied.</b> A stroke still arriving is one the caller
    /// appends to between calls, and copying on every reading would be quadratic over the
    /// stroke — the exact cost <c>incremental-drawing</c> exists to avoid. So a stroke built
    /// this way is a <i>view</i> of the caller's list and changes when the caller does.
    /// </para>
    /// <para>
    /// That is fine for the thing being drawn and wrong for anything kept. Use
    /// <see cref="Finished"/> for a stroke that must not change under whoever is holding it.
    /// </para>
    /// </remarks>
    public Stroke(IReadOnlyList<Reading> points)
    {
        if (points.Count == 0) throw new ArgumentException("a stroke has at least one point", nameof(points));

        Points = points;
    }

    /// <summary>
    /// A stroke that cannot change, whatever the caller does to the list afterwards.
    /// </summary>
    /// <remarks>
    /// For anything that outlives the drawing of it: a recording being kept, a fixture, a
    /// stroke handed to something that will read it later. The copy is the point, and one
    /// copy at the end is nothing beside a copy per reading.
    /// </remarks>
    public static Stroke Finished(IReadOnlyList<Reading> points) => new([.. points]);

    /// <summary>
    /// In the order they arrived. Never reordered, never deduplicated.
    /// </summary>
    /// <remarks>
    /// Borrowed unless this stroke was made by <see cref="Finished"/>. See the constructor.
    /// </remarks>
    public IReadOnlyList<Reading> Points { get; }

    public int Count => Points.Count;

    public Reading First => Points[0];

    public Reading Last => Points[^1];

    /// <summary>
    /// How long the contact lasted, in microseconds.
    /// <para>
    /// A difference between two timestamps and never a timestamp itself, because a pen
    /// point's timestamp has no stated origin. Zero is a possible answer rather than an
    /// error: a backend whose clock is coarser than its report rate gives every point in a
    /// short stroke the same reading, and so does a backend that supplies no clock at all.
    /// </para>
    /// </summary>
    public long DurationMicroseconds => Last.At - First.At;
}

/// <summary>
/// Turning a stream of readings into strokes.
/// </summary>
public static class Strokes
{
    /// <summary>
    /// Whether this reading was taken with the tip on the tablet.
    /// <para>
    /// Non-zero pressure, which is the simplest rule that works and is the one this guide
    /// uses. It has a known limitation worth stating rather than hiding: a device that
    /// reports zero pressure on the first or last reading of a real contact loses that
    /// reading here. What tablets actually do at the edges of contact is a Part IV subject,
    /// measured rather than assumed.
    /// </para>
    /// </summary>
    public static bool InContact(Reading point) => point.Pressure > 0;

    /// <summary>
    /// Every stroke in a stream of readings, in order.
    /// <para>
    /// An application is given readings while the pen merely hovers as well as while it
    /// draws, and the two arrive on the same stream. A stroke is a contiguous run of
    /// in-contact readings: it begins at the first one after the tip goes down and ends at
    /// the last one before it lifts. Hovering readings separate strokes and belong to none
    /// of them.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Stroke> From(IEnumerable<Reading> points)
    {
        var found = new List<Stroke>();
        var current = new List<Reading>();

        foreach (var point in points)
        {
            if (InContact(point))
            {
                current.Add(point);
                continue;
            }

            // The tip has lifted. Whatever was being collected is a finished stroke, and a
            // run of hovering readings after it is not the start of another.
            if (current.Count > 0)
            {
                found.Add(new Stroke(current));
                current = [];
            }
        }

        if (current.Count > 0) found.Add(new Stroke(current));

        return found;
    }
}
