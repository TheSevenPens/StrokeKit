using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using StrokeFieldGuide.Strokes;
using WinPenKit;

namespace StrokeFieldGuide.Canvas;

/// <summary>
/// An open pen session, polled, with every packet turned into a <see cref="Reading"/>.
/// </summary>
/// <remarks>
/// <para>
/// The recorder had all of this inside its window, which was fine while it was the only
/// application that opened a session. It is not any more: the lab draws with a live pen now,
/// and the parts that are easy to get subtly wrong -- when a batch is stamped, what an
/// altitude means, which clock is which -- are exactly the parts that must not be written
/// twice and drift.
/// </para>
/// <para>
/// <b>Polled rather than pushed.</b> WinPenKit's sessions queue packets and hand them over on
/// request, so this owns a timer and drains on each tick. The alternative -- a callback per
/// packet -- would put the application's drawing on the driver's thread at 162 packets a
/// second.
/// </para>
/// </remarks>
public sealed class PenStream : IDisposable
{
    private readonly DispatcherTimer _poll;
    private IPenSession? _session;

    /// <param name="every">
    /// How often to drain. Sixteen milliseconds is one frame at 60 Hz, which is about ten
    /// packets at the rate a Cintiq 24 actually reports.
    /// </param>
    public PenStream(TimeSpan? every = null)
    {
        _poll = new DispatcherTimer { Interval = every ?? TimeSpan.FromMilliseconds(16) };
        _poll.Tick += (_, _) => Drain();
    }

    /// <summary>One drained batch: the readings, and how many came across together.</summary>
    /// <remarks>
    /// The batch size is given because it is the one number that says whether the application
    /// is keeping up. A queue that is always shallow means the poll is faster than the pen; a
    /// queue that grows means it is not, and readings are waiting rather than being lost.
    /// </remarks>
    public event EventHandler<IReadOnlyList<Reading>>? Arrived;

    /// <summary>Raised when a drain hands over nothing, so a gauge can say so.</summary>
    public event EventHandler<int>? Drained;

    public IPenSession? Session => _session;

    public bool IsRunning => _session is { IsRunning: true };

    /// <summary>The device's full-scale pressure, or zero when nothing is open.</summary>
    /// <remarks>
    /// Asked of the session rather than assumed, because it is the whole reason a raw
    /// pressure count means anything: the Wintab backends ask the device and the pointer
    /// backends declare a fixed 1024. See <see cref="Backends"/>.
    /// </remarks>
    public int MaxPressure => _session?.MaxPressure ?? 0;

    /// <summary>
    /// Opens a backend, or answers why it could not be opened.
    /// </summary>
    /// <param name="on">
    /// The control a framework backend listens on, and the window whose handle a WM_POINTER
    /// session subclasses. Give it the window rather than the drawing surface: a session
    /// bound to a control that is not on screen hears nothing, and reports no error while
    /// doing so.
    /// </param>
    public string? Start(InputApi api, Control on, IntPtr handle)
    {
        Stop();

        var session = PenBackends.Open(api, on);
        var failure = session.Start(handle);

        if (failure is not null)
        {
            session.Dispose();

            return failure;
        }

        _session = session;
        _poll.Start();

        return null;
    }

    public void Stop()
    {
        _poll.Stop();

        if (_session is null) return;

        _session.Stop();
        _session.Dispose();
        _session = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Microseconds on a clock this process owns, for stamping when a reading arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Environment.TickCount64"/>, which moves in steps of about 16 ms on
    /// Windows -- the same order as the poll it would be measuring, so a gap of one poll and
    /// a gap of none would read alike. <see cref="Stopwatch"/> is monotonic and does not step.
    /// </para>
    /// <para>
    /// Static, so every reading in a process is on one origin. The origin itself is
    /// meaningless; the differences are the point.
    /// </para>
    /// <para>
    /// <b>This is the second clock, and it is why anything here can be trusted.</b> The pen's
    /// own timestamp is a packet counter: it advances a flat 4.166 ms per packet delivered,
    /// runs at 0.673 of real time, and resynchronises at every contact transition. A single
    /// clock cannot tell "the device stopped sending" from "the device stamped late", and
    /// five separate explanations for a gap in the data died on that before this existed.
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
    /// falling steadily as the pen comes down. It was added to chase an apparent silence
    /// before a landing, which turned out to be the pen's timestamp resynchronising rather
    /// than any silence -- so it explains nothing, and is kept because it is a real channel
    /// nothing else here measures.
    /// </para>
    /// <para>
    /// <see cref="Reading.Status"/> is raw. Bit 1 is a queue overflow and has been clear on
    /// every reading of every take since the column existed. Bit 0 is documented as "the
    /// cursor is out of the context", so zero while hovering <em>and</em> in contact is the
    /// spec's own polarity -- and asking <c>IsInProximity</c> for it rejected every hovering
    /// reading, which is WinPenKit#125.
    /// </para>
    /// </remarks>
    public static Reading Of(PenPoint point, long arrived = 0) => new(
        point.DesktopX, point.DesktopY, point.Pressure, point.TimestampMicroseconds,
        Height: point.Z,
        Status: point.Status,
        Lean: 90 - point.Altitude, Azimuth: point.Azimuth, Twist: point.Twist,
        Arrived: arrived);

    /// <summary>
    /// Takes whatever the session has, and stamps the whole batch with one arrival time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One stamp for the batch, deliberately.</b> Every reading that came across together
    /// genuinely arrived together -- they were sitting in the driver's queue and were handed
    /// over in one call -- so giving each its own timestamp would invent a spread that the
    /// delivery did not have. Stamping once makes "did these arrive together?" an equality
    /// check rather than an argument about the clock's resolution.
    /// </para>
    /// <para>
    /// <b>Stamped before anything else is told.</b> The <see cref="Drained"/> event used to
    /// be raised first, so a subscriber updating a gauge or laying out a control did its work
    /// inside the measurement: the timestamp said when the application got round to stamping
    /// rather than when the batch came over. Nothing here may run between the drain and the
    /// stamp.
    /// </para>
    /// <para>
    /// <b>What it is a time of.</b> This is the <i>drain observation</i>: when this
    /// application took the batch out of the session's queue. It is not when each packet
    /// reached the driver, and it is not when the pen reported. Those are the device's
    /// business and only the pen's own timestamp speaks to them -- badly, since it is a
    /// packet counter.
    /// </para>
    /// </remarks>
    private void Drain()
    {
        if (_session is not { IsRunning: true } session) return;

        var points = session.DrainPoints();

        // Immediately, and before any subscriber is given the chance to do work.
        var arrived = Arrival();

        Drained?.Invoke(this, points.Length);

        if (points.Length == 0) return;

        var readings = new Reading[points.Length];

        for (var each = 0; each < points.Length; each++) readings[each] = Of(points[each], arrived);

        Arrived?.Invoke(this, readings);
    }
}
