using System.Diagnostics;
using WinPenKit;

namespace StrokeFieldGuide.Strokes;

/// <summary>
/// One drained batch: the packets, the readings they became, and when they arrived.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both halves, because neither is enough.</b> A <see cref="Reading"/> is what this guide
/// decided to keep, and it deliberately drops things a packet carries — it stores a lean and an
/// azimuth where the device reported a tilt x and a tilt y. An application showing the pen's
/// raw channels has to see the packet. An application drawing with it wants the reading.
/// <see cref="Points"/> and <see cref="Readings"/> are the same batch, paired by index.
/// </para>
/// <para>
/// <see cref="Arrived"/> is one value for the whole batch and that is deliberate. Every packet
/// that came across together genuinely arrived together — they were sitting in the driver's
/// queue and were handed over in one call — so giving each its own would invent a spread the
/// delivery did not have.
/// </para>
/// </remarks>
public readonly record struct Batch(
    IReadOnlyList<PenPoint> Points,
    IReadOnlyList<Reading> Readings,
    long Arrived,
    IPenSession Session)
{
    /// <summary>How many packets came across. Zero is an answer, not an absence.</summary>
    /// <remarks>
    /// The one number that says whether the application is keeping up: a queue that is always
    /// shallow means the poll is faster than the pen, and a queue that grows means it is not.
    /// So an empty drain is still reported.
    /// </remarks>
    public int Count => Readings.Count;
}

/// <summary>
/// Taking whatever a pen session has, and stamping when it was taken.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the timer and the window that drive it, so the part that is easy to get
/// subtly wrong can be checked without either. <b>Both applications used to do this for
/// themselves</b>, and they had already drifted: the lab stamped a batch the moment it was
/// drained, and the recorder — the one whose whole purpose is measurement, and whose output is
/// published — stamped it after updating two readouts and walking a ring buffer.
/// </para>
/// <para>
/// <b>Nothing may run between the drain and the stamp.</b> That is the entire contract of this
/// type, and it is why it exists rather than being four lines in each window. A subscriber
/// updating a gauge or laying out a control inside that gap makes the timestamp say when the
/// application got round to stamping, not when the batch came over.
/// </para>
/// </remarks>
public sealed class Draining
{
    private readonly Func<long> _now;

    /// <param name="now">
    /// The clock, in microseconds. Given rather than taken so a test can hand over a counter
    /// and say exactly what each batch should be stamped with; defaults to
    /// <see cref="Arrival"/>, which is the one every application should use in earnest.
    /// </param>
    public Draining(Func<long>? now = null) => _now = now ?? Arrival;

    /// <summary>
    /// Everything the session has, stamped with one arrival.
    /// </summary>
    /// <remarks>
    /// The stamp is taken on the line after the drain, before the packets are even converted.
    /// Conversion after it is harmless — the moment being recorded has already passed — but
    /// nothing may move above it.
    /// </remarks>
    public Batch Took(IPenSession session)
    {
        var points = session.DrainPoints();

        // Immediately. See the note on this class: this line may not move.
        var arrived = _now();

        var readings = new Reading[points.Length];

        for (var each = 0; each < points.Length; each++)
        {
            readings[each] = Of(points[each], arrived);
        }

        return new Batch(points, readings, arrived, session);
    }

    /// <summary>
    /// Microseconds on a clock this process owns, for stamping when a reading arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Environment.TickCount64"/>, which moves in steps of about 16 ms on
    /// Windows — the same order as the poll it would be measuring, so a gap of one poll and a
    /// gap of none would read alike. <see cref="Stopwatch"/> is monotonic and does not step.
    /// </para>
    /// <para>
    /// Static, so every reading in a process is on one origin. The origin itself is
    /// meaningless; the differences are the point.
    /// </para>
    /// <para>
    /// <b>This is the second clock, and it is why anything here can be trusted.</b> The pen's
    /// own timestamp is a packet counter: it advances a flat 4.166 ms per packet delivered,
    /// runs at 0.673 of real time, and resynchronises at every contact transition. A single
    /// clock cannot tell "the device stopped sending" from "the device stamped late", and five
    /// separate explanations for a gap in the data died on that before this existed.
    /// </para>
    /// </remarks>
    public static long Arrival() =>
        (long)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));

    /// <summary>
    /// What a packet says, in this guide's terms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Altitude counts up from the tablet and a lean counts away from vertical, so one is the
    /// other subtracted from a right angle. Azimuth and twist come across untouched: both are
    /// already the angle the guide wants.
    /// </para>
    /// <para>
    /// <see cref="Reading.Height"/> is Wintab's pkZ, 0 to 401 on the tablet measured here,
    /// falling steadily as the pen comes down. It was added to chase an apparent silence before
    /// a landing, which turned out to be the pen's timestamp resynchronising rather than any
    /// silence — so it explains nothing, and is kept because it is a real channel nothing else
    /// here measures.
    /// </para>
    /// <para>
    /// <see cref="Reading.Status"/> is raw. Bit 1 is a queue overflow and has been clear on
    /// every reading of every take since the column existed. Bit 0 is documented as "the cursor
    /// is out of the context", so zero while hovering <em>and</em> in contact is the spec's own
    /// polarity — and asking <c>IsInProximity</c> for it rejected every hovering reading, which
    /// is WinPenKit#125.
    /// </para>
    /// </remarks>
    public static Reading Of(PenPoint point, long arrived = 0) => new(
        point.DesktopX, point.DesktopY, point.Pressure, point.TimestampMicroseconds,
        Height: point.Z,
        Status: point.Status,
        Lean: 90 - point.Altitude, Azimuth: point.Azimuth, Twist: point.Twist,
        Arrived: arrived);
}
