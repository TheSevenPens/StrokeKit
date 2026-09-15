using WinPenKit;

namespace StrokeFieldGuide.Strokes;

/// <summary>
/// The pen points of one continuous contact, in the order they arrived.
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
    public Stroke(IReadOnlyList<PenPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException("a stroke has at least one point", nameof(points));

        Points = points;
    }

    /// <summary>In the order they arrived. Never reordered, never deduplicated.</summary>
    public IReadOnlyList<PenPoint> Points { get; }

    public int Count => Points.Count;

    public PenPoint First => Points[0];

    public PenPoint Last => Points[^1];

    /// <summary>
    /// How long the contact lasted, in microseconds.
    /// <para>
    /// A difference between two timestamps and never a timestamp itself, because a pen
    /// point's timestamp has no stated origin. Zero is a possible answer rather than an
    /// error: a backend whose clock is coarser than its report rate gives every point in a
    /// short stroke the same reading, and so does a backend that supplies no clock at all.
    /// </para>
    /// </summary>
    public long DurationMicroseconds => Last.TimestampMicroseconds - First.TimestampMicroseconds;
}

/// <summary>
/// Turning a stream of pen points into strokes.
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
    public static bool InContact(PenPoint point) => point.Pressure > 0;

    /// <summary>
    /// Every stroke in a stream of pen points, in order.
    /// <para>
    /// An application is given readings while the pen merely hovers as well as while it
    /// draws, and the two arrive on the same stream. A stroke is a contiguous run of
    /// in-contact readings: it begins at the first one after the tip goes down and ends at
    /// the last one before it lifts. Hovering readings separate strokes and belong to none
    /// of them.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Stroke> From(IEnumerable<PenPoint> points)
    {
        var found = new List<Stroke>();
        var current = new List<PenPoint>();

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
