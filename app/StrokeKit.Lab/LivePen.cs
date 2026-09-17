using StrokeFieldGuide.Brushes;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Lab;

/// <summary>
/// A real pen drawing on a real document, a reading at a time.
/// </summary>
/// <remarks>
/// <para>
/// Everything under this has been checked on strokes made up by a generator. This is where
/// the guide stops replaying its own fixtures and puts a hand on the other end, and it is
/// deliberately thin: contact in, <see cref="Wet.Extend"/> out. The interesting parts are all
/// underneath it and all already proven — <see cref="Wet"/> lays only what is new because the
/// stamps of a stroke so far are a prefix of the finished one's, and
/// <c>incremental-drawing</c> is the page that establishes it.
/// </para>
/// <para>
/// <b>Separate from the window on purpose.</b> The recorder learned this the expensive way:
/// its pen routing lives inside a 2,898-line code-behind and nobody can now say where a
/// reading goes. This is the same logic with nothing else in the file.
/// </para>
/// </remarks>
public sealed class LivePen : IDisposable
{
    private readonly Surface _document;
    private readonly Surface _shown;

    private Wet? _live;
    private List<Reading> _readings = [];

    /// <param name="document">Where the ink ends up.</param>
    /// <param name="shown">
    /// What the view presents. Not the same surface: under
    /// <see cref="Buildup.OncePerStroke"/> the stroke is held off the document until the pen
    /// lifts, so what the reader should be looking at is the document with an uncommitted
    /// stroke over it — which is a third picture, belonging to neither.
    /// </param>
    public LivePen(Surface document, Surface shown)
    {
        _document = document;
        _shown = shown;
    }

    /// <summary>The brush, already ranged against the device that is open.</summary>
    public Brush Brush { get; set; } = Presets.RoundDabs.Brush;

    /// <summary>Whether the tip is down.</summary>
    public bool Drawing => _live is not null;

    /// <summary>How many readings the stroke in progress holds.</summary>
    public int Readings => _readings.Count;

    /// <summary>How many stamps it has laid.</summary>
    public int Stamps => _live?.Laid ?? 0;

    /// <summary>Raised whenever the shown surface has changed and wants presenting.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when a stroke begins and when it ends, with whether one is in hand.
    /// </summary>
    /// <remarks>
    /// So the caller can put the composited surface in front of the reader only while there
    /// is something to composite. The rest of the time the view shows the document itself,
    /// which is what <c>view-transform</c> guarantees is presented pixel for pixel at a zoom
    /// of one -- and a document shown through a copy is not the document, however careful the
    /// copy is.
    /// </remarks>
    public event EventHandler<bool>? Holding;

    /// <summary>
    /// One reading from the pen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contact is the whole state machine: a reading with pressure while nothing is down
    /// starts a stroke, one without pressure while something is down ends it. That is
    /// <c>points-to-stroke</c>'s rule and not a second opinion about it.
    /// </para>
    /// <para>
    /// A hovering reading is not passed on. It is not nothing — the approach to a landing is
    /// real data and the recorder keeps it — but a brush is never given one: a stroke starts
    /// where the tip goes down.
    /// </para>
    /// </remarks>
    /// <param name="over">
    /// The transform from the pen's desktop position to a document pixel, captured by the
    /// caller. Read once per stroke, at the landing: the view can be panned or the window
    /// dragged while the pen is down, and a stroke whose frame of reference moves under it
    /// bends in a way no brush asked for.
    /// </param>
    public void Took(Reading reading, InkTransform over)
    {
        if (reading.InContact)
        {
            if (_live is null)
            {
                Begin(over);

                Holding?.Invoke(this, true);
            }

            _readings.Add(reading);

            _live!.Extend(new Stroke(_readings));

            Present();
        }
        else if (_live is not null)
        {
            Lift();
        }
    }

    /// <summary>The pen has gone out of range with the tip still down.</summary>
    /// <remarks>
    /// A pen lifted fast enough can leave the tablet's proximity without ever reporting a
    /// reading at zero pressure, so the lift has to be reachable from outside as well. A
    /// stroke left open is a stroke whose ink never reaches the document under
    /// <see cref="Buildup.OncePerStroke"/>.
    /// </remarks>
    public void Lift()
    {
        if (_live is null) return;

        _live.Finish();
        _live.Dispose();
        _live = null;
        _readings = [];

        // The document is whole again, so the caller puts it back in front of the reader
        // before this asks for a frame.
        Holding?.Invoke(this, false);

        Present();
    }

    /// <summary>
    /// Throws away the stroke in hand without committing it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Lift"/>. A lift is what the pen does and it ends with the stroke on the
    /// document; this is what happens when the document itself is replaced under a stroke,
    /// and the stroke has to go with the page it was being drawn on.
    /// </remarks>
    public void Abandon()
    {
        if (_live is null) return;

        _live.Dispose();
        _live = null;
        _readings = [];

        Holding?.Invoke(this, false);
    }

    /// <summary>
    /// Puts the document back on the shown surface, after something else changed it.
    /// </summary>
    /// <remarks>
    /// The stroke in hand is abandoned rather than lifted, and the difference is visible: a
    /// lift merges the stroke's own surface onto the document, so clearing the page with the
    /// tip still down and then lifting pastes the stroke onto the page the reader had just
    /// emptied. Written as a lift first, with a comment at the call site describing that as
    /// the thing being avoided.
    /// </remarks>
    public void Refresh()
    {
        Abandon();
        Present();
    }

    public void Dispose()
    {
        _live?.Dispose();
        _live = null;
    }


    private void Begin(InkTransform over)
    {
        // Refused rather than approximated, and said plainly. Wet throws for an outlining
        // engine because drawing one incrementally is a real piece of work and is not this;
        // the caller offers only the brushes it can draw, and this is the guard behind that.
        if (Brush.Engine != Engine.Stamps)
        {
            throw new NotSupportedException(
                $"a live stroke needs {Engine.Stamps}, and this brush asks for {Brush.Engine}");
        }

        _readings = [];
        _live = new Wet(Brush, _document, over);
    }

    private void Present()
    {
        // Only while a stroke is in hand. With nothing to composite the caller is showing
        // the document itself, so there is nothing for this to write.
        _live?.PresentOnto(_shown);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
