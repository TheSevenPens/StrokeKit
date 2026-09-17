using Avalonia.Controls;
using Avalonia.Threading;
using StrokeKit.Strokes;
using WinPenKit;

namespace StrokeKit.Avalonia;

/// <summary>
/// An open pen session, polled, handing over each batch as it is drained.
/// </summary>
/// <remarks>
/// <para>
/// What is left here is the part that genuinely needs a windowing framework: a timer on the
/// dispatcher, a control for a framework backend to listen on, and the session's lifetime.
/// Taking the batch and stamping it is <see cref="Draining"/>, which needs none of that and is
/// checked without it.
/// </para>
/// <para>
/// <b>Both applications drive the pen through this.</b> The recorder used to own a timer, a
/// drain and a session of its own, borrowing two static helpers from here — which is not
/// sharing, and the two had already drifted on the one thing that matters most. See
/// <see cref="Draining"/>.
/// </para>
/// <para>
/// <b>Polled rather than pushed.</b> WinPenKit's sessions queue packets and hand them over on
/// request, so this owns a timer and drains on each tick. The alternative — a callback per
/// packet — would put the application's drawing on the driver's thread at 162 packets a second.
/// </para>
/// </remarks>
public sealed class PenStream : IDisposable
{
    /// <summary>One frame at 60 Hz.</summary>
    /// <remarks>
    /// Which is about three packets, not the ten this used to claim. A Cintiq 24 reports 161.6
    /// a second, so sixteen milliseconds is 2.6 of them — and a take recorded through this
    /// came out at a mean of 3.0 readings a batch over 310 batches, the extra fraction being
    /// polls that ran a little late. Ten would need sixty milliseconds.
    /// </remarks>
    public const double PollMilliseconds = 16;

    private readonly DispatcherTimer _poll;
    private readonly Draining _draining;

    private IPenSession? _session;

    /// <param name="every">How often to drain. Defaults to <see cref="PollMilliseconds"/>.</param>
    /// <param name="now">
    /// The clock a batch is stamped with. Given rather than taken so a test can say exactly
    /// what each batch should carry.
    /// </param>
    public PenStream(TimeSpan? every = null, Func<long>? now = null)
    {
        _draining = new Draining(now);

        _poll = new DispatcherTimer
        {
            Interval = every ?? TimeSpan.FromMilliseconds(PollMilliseconds),
        };

        _poll.Tick += (_, _) => Drain();
    }

    /// <summary>
    /// One drained batch, empty or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised on every tick, including the ones that brought nothing, because an empty drain is
    /// an answer: a queue that is always shallow means the poll is faster than the pen.
    /// </para>
    /// <para>
    /// <b>By the time a subscriber runs, the batch is already stamped.</b> That is the point of
    /// there being one event carrying a whole <see cref="Batch"/> rather than a notification
    /// that a drain happened: there is no longer anywhere for an application to put work
    /// between the two.
    /// </para>
    /// </remarks>
    public event EventHandler<Batch>? Drained;

    public IPenSession? Session => _session;

    public bool IsRunning => _session is { IsRunning: true };

    /// <summary>The device's full-scale pressure, or zero when nothing is open.</summary>
    /// <remarks>
    /// Asked of the session rather than assumed, because it is the whole reason a raw pressure
    /// count means anything: the Wintab backends ask the device and the pointer backends
    /// declare a fixed 1024. See <see cref="PenBackends"/>.
    /// </remarks>
    public int MaxPressure => _session?.MaxPressure ?? 0;

    /// <summary>
    /// Opens a backend, or answers why it could not be opened.
    /// </summary>
    /// <param name="on">
    /// The control a framework backend listens on, and the window whose handle a WM_POINTER
    /// session subclasses. Give it the window rather than the drawing surface: a session bound
    /// to a control that is not on screen hears nothing, and reports no error while doing so.
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

    /// <summary>Stops polling first, then closes the session.</summary>
    /// <remarks>
    /// That order and not the other: a tick that fired after the session closed would drain a
    /// session that is already gone.
    /// </remarks>
    public void Stop()
    {
        _poll.Stop();

        if (_session is null) return;

        _session.Stop();
        _session.Dispose();
        _session = null;
    }

    public void Dispose() => Stop();

    private void Drain()
    {
        if (_session is not { IsRunning: true } session) return;

        Drained?.Invoke(this, _draining.Took(session));
    }
}
