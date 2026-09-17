using StrokeKit.Strokes;
using WinPenKit;

namespace StrokeKit.Tests;

/// <summary>
/// That a drained batch is stamped when it was drained, and stamped once.
/// </summary>
/// <remarks>
/// <para>
/// None of this could be checked before. Draining lived inside two windows — a timer, a live
/// session and a tablet between the test and the behaviour — so the two copies drifted on the
/// one property that matters most and nothing said so. The lab stamped a batch the moment it
/// came over; the recorder, whose output is published, stamped it after updating two readouts
/// and walking a ring buffer.
/// </para>
/// <para>
/// <see cref="Draining"/> takes its clock as an argument, so a test can hand over a counter and
/// say exactly what each batch should carry.
/// </para>
/// </remarks>
public class DrainingBatches
{
    /// <summary>A session that hands over whatever it was given, without a tablet.</summary>
    private sealed class Queued : IPenSession
    {
        private readonly Queue<PenPoint[]> _batches = new();

        /// <summary>What the clock did while DrainPoints was running, if anything.</summary>
        public Action? WhileDraining { get; set; }

        public void Give(params PenPoint[] points) => _batches.Enqueue(points);

        public PenPoint[] DrainPoints()
        {
            WhileDraining?.Invoke();

            return _batches.Count > 0 ? _batches.Dequeue() : [];
        }

        public int DrainPoints(Span<PenPoint> buffer)
        {
            var points = DrainPoints();

            points.CopyTo(buffer);

            return points.Length;
        }

        public string? Start(IntPtr appWindowHandle = default) => null;

        public void Stop() { }

        public bool IsRunning => true;

        public bool HasNewData => _batches.Count > 0;

        public int MaxPressure => 8191;

        public InputApi Api => InputApi.WintabDigitizer;

        public PenCapabilities Capabilities => default;

        public PenConventions Conventions => default;

        public string DebugInfo => nameof(Queued);

        public IPenCaptureRegion? CaptureRegion { get; set; }

        public void RefreshMapping() { }

        /// <summary>Counted, so a test can say the session was closed exactly once.</summary>
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private static PenPoint At(double x, double y, uint pressure = 500, long at = 0) =>
        new(x, y, (int)x, (int)y, pressure,
            Azimuth: 90, Altitude: 60, Twist: 0, TiltX: 5, TiltY: -5,
            Z: 0, Status: 0, Buttons: 0, Cursor: 0,
            Source: InputApi.WintabDigitizer, TimestampMicroseconds: at);

    /// <summary>
    /// Every reading of a batch carries the same arrival, exactly.
    /// </summary>
    /// <remarks>
    /// Exactly, not nearly. They were sitting in the driver's queue and were handed over in one
    /// call, so "did these two packets reach the application in the same poll" should be an
    /// equality check rather than an argument about a timer's resolution.
    /// </remarks>
    [Fact]
    public void AWholeBatchSharesOneArrival()
    {
        var session = new Queued();

        session.Give(At(1, 1), At(2, 2), At(3, 3));

        var batch = new Draining(() => 4_242).Took(session);

        Assert.Equal(3, batch.Count);
        Assert.Equal(4_242, batch.Arrived);
        Assert.All(batch.Readings, reading => Assert.Equal(4_242, reading.Arrived));
    }

    /// <summary>
    /// The stamp is taken after the packets are in hand, not before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clock moves while the drain is happening, so a stamp taken before it would read
    /// zero. This pins the order of those two, which is the half of the contract a test can
    /// see.
    /// </para>
    /// <para>
    /// <b>It does not pin the other half.</b> That nothing runs <em>between</em> the drain and
    /// the stamp was checked by breaking it on purpose — moving the stamp below the conversion
    /// loop — and every test here still passed, because conversion does not touch the clock.
    /// So that property is held by the shape of the code and not by this file: an application
    /// is handed a batch that is already stamped, and there is no seam left to insert work
    /// into. It was a seam when each window drained for itself, and that is exactly where the
    /// recorder went wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStampIsTakenAfterTheDrain()
    {
        var clock = 0L;
        var session = new Queued { WhileDraining = () => clock = 100 };

        session.Give(At(1, 1));

        var batch = new Draining(() => clock).Took(session);

        Assert.Equal(100, batch.Arrived);
    }

    /// <summary>The clock is read once per batch, so a batch cannot be stamped twice.</summary>
    [Fact]
    public void TheClockIsReadOncePerBatch()
    {
        var session = new Queued();
        var reads = 0;

        session.Give(At(1, 1), At(2, 2), At(3, 3));

        var draining = new Draining(() => { reads++; return 42; });

        draining.Took(session);
        Assert.Equal(1, reads);

        // And the empty drain that follows is its own single reading of the clock.
        draining.Took(session);
        Assert.Equal(2, reads);
    }

    /// <summary>Two batches are two different times, and later is later.</summary>
    [Fact]
    public void SeparateBatchesAreSeparateArrivals()
    {
        var session = new Queued();

        session.Give(At(1, 1));
        session.Give(At(2, 2));

        var clock = 0L;
        var draining = new Draining(() => ++clock);

        Assert.Equal(1, draining.Took(session).Arrived);
        Assert.Equal(2, draining.Took(session).Arrived);
    }

    /// <summary>An empty drain is still a batch, because zero is an answer.</summary>
    /// <remarks>
    /// A queue that is always shallow means the poll is faster than the pen. A drain that
    /// reported nothing when it found nothing would make that indistinguishable from a poll
    /// that had stopped.
    /// </remarks>
    [Fact]
    public void AnEmptyDrainIsStillABatch()
    {
        var batch = new Draining(() => 7).Took(new Queued());

        Assert.Equal(0, batch.Count);
        Assert.Empty(batch.Readings);
        Assert.Equal(7, batch.Arrived);
    }

    /// <summary>The packets and the readings are the same batch, in the same order.</summary>
    /// <remarks>
    /// Paired by index, because an application showing the pen's raw channels needs the packet
    /// — a <see cref="Reading"/> keeps a lean and an azimuth where the device reported a tilt x
    /// and a tilt y — while the same application draws with the reading.
    /// </remarks>
    [Fact]
    public void ThePacketsAndTheReadingsArePaired()
    {
        var session = new Queued();

        session.Give(At(10, 20, 100), At(30, 40, 200));

        var batch = new Draining(() => 0).Took(session);

        Assert.Equal(batch.Points.Count, batch.Readings.Count);

        for (var each = 0; each < batch.Count; each++)
        {
            Assert.Equal(batch.Points[each].DesktopX, batch.Readings[each].X);
            Assert.Equal(batch.Points[each].DesktopY, batch.Readings[each].Y);
            Assert.Equal(batch.Points[each].Pressure, batch.Readings[each].Pressure);
        }

        // And the packet still carries what the reading dropped.
        Assert.Equal(5, batch.Points[0].TiltX);
    }

    /// <summary>
    /// A lean is what is left of a right angle after the altitude.
    /// </summary>
    /// <remarks>
    /// The conversion both applications used to reach for as a static. It is here so that it is
    /// checked once rather than trusted twice.
    /// </remarks>
    [Fact]
    public void AnAltitudeBecomesALean()
    {
        var session = new Queued();

        session.Give(At(1, 1) with { Altitude = 60, Azimuth = 175, Twist = 90 });

        var only = Assert.Single(new Draining(() => 0).Took(session).Readings);

        Assert.Equal(30, only.Lean);
        Assert.Equal(175, only.Azimuth);
        Assert.Equal(90, only.Twist);
    }

    /// <summary>The batch says which session it came from, so a subscriber need not be told.</summary>
    [Fact]
    public void ABatchKnowsItsSession()
    {
        var session = new Queued();

        session.Give(At(1, 1));

        Assert.Same(session, new Draining(() => 0).Took(session).Session);
    }
}
