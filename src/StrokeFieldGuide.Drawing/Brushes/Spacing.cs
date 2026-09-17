namespace StrokeFieldGuide.Brushes;

/// <summary>
/// Where one stamp goes, and where it sits between the two readings it fell between.
/// </summary>
/// <param name="Segment">The index of the reading the stamp is past.</param>
/// <param name="Fraction">
/// How far from that reading to the next, from 0 to 1. Zero for the first stamp, which is at
/// the first reading.
/// </param>
public readonly record struct Placement(double X, double Y, int Segment, double Fraction);

/// <summary>
/// What a brush's spacing is measured in.
/// </summary>
/// <remarks>
/// <para>
/// A mode rather than two properties, so that a brush cannot carry both an absolute spacing
/// and a proportional one and leave which applies to be worked out.
/// </para>
/// </remarks>
public enum SpacedBy
{
    /// <summary>The stroke's own units, the same gap the whole way along.</summary>
    Distance,

    /// <summary>
    /// Multiples of the stamp's own diameter, so the gap grows and shrinks with the stamp.
    /// <para>
    /// The point of it is that <c>D/S</c> -- how many stamps cover a point, which is what
    /// sets the alpha -- stays constant while the diameter varies. With
    /// <see cref="Distance"/> a stroke that widens under pressure also darkens.
    /// </para>
    /// </summary>
    Diameters,
}

/// <summary>
/// Where along a path the stamps go.
/// <para>
/// By distance travelled, never by how many points arrived. The pen reports on a clock and
/// the hand moves at whatever speed it likes, so a stamp per reported point puts them close
/// together where the hand was slow and far apart where it was fast. The same gesture then
/// looks different depending on how quickly it was drawn, which is the commonest reason a
/// fast stroke comes out dotted.
/// </para>
/// </summary>
public static class Spacing
{
    /// <summary>
    /// Positions at a fixed distance apart along a path, in the path's own units.
    /// <para>
    /// The first stamp is at the start of the path. After that a stamp goes down every
    /// <paramref name="spacing"/> units of distance travelled.
    /// </para>
    /// <para>
    /// <b>The end of the path may carry no stamp.</b> A path of 25 units at a spacing of 10
    /// gets stamps at 0, 10 and 20, and the last five units are bare. That is a choice: the
    /// alternative is to move the last stamp so it lands on the end, which changes the gap
    /// between the last two and makes the spacing a near-constant rather than a constant.
    /// A stroke can therefore look up to one spacing short at its end, and that is the cost.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Along(
        IReadOnlyList<(double X, double Y)> path, double spacing) =>
        [.. Placements(path, spacing).Select(placement => (placement.X, placement.Y))];

    /// <summary>
    /// The same positions, each saying which pair of readings it fell between and how far
    /// along that pair it is.
    /// <para>
    /// Needed the moment a stamp carries anything that varies along the stroke. Stamps go
    /// down every fixed distance and readings arrive on a clock, so <b>most stamps are not at
    /// a reading</b>: a stamp that took the nearest reading's pressure would step rather than
    /// ramp, and the steps would be wherever the hand happened to be slow.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Placement> Placements(
        IReadOnlyList<(double X, double Y)> path, double spacing) =>
        new Walk(spacing).Advance(path);
}

/// <summary>
/// The spacing walk, stopped part-way and resumed.
/// <para>
/// A stroke that is still arriving asks for its stamps once per reading. Walking the whole
/// path each time is linear per call and therefore quadratic over the stroke -- which is the
/// fault <c>incremental-drawing</c> is about, reached by a different route: the stamps are
/// not redrawn, but they are recomputed.
/// </para>
/// <para>
/// This is the same walk with its state kept between calls: how many readings have been
/// consumed, and how far past the last stamp the path has travelled. Resuming is sound
/// because the walk only ever reads forward, which is the same property that makes a
/// stroke's stamps a prefix of the finished one's.
/// </para>
/// </summary>
/// <param name="spacing">
/// The gap between stamps. With <paramref name="gapAfter"/> supplied no stamp is placed by
/// this value -- every gap including the first comes from the function -- but it is still
/// required and still validated, because it is the number the caller was configured with and
/// a brush whose spacing is zero is worth refusing at the point it is built.
/// </param>
/// <param name="gapAfter">
/// The gap to leave after a stamp, or null for a constant spacing.
/// <para>
/// <b>Backward-looking on purpose.</b> A gap that depended on the next stamp's size would
/// depend on where that stamp lands, which depends on the gap -- a fixed point, solved
/// iteratively, for a difference no one can see. The gap after a stamp is decided by that
/// stamp.
/// </para>
/// <para>
/// Supplying this costs the walk its closed form. With a constant spacing, stamp <c>k</c>
/// sits at <c>k * spacing</c> and is computed; with a varying one each stamp is the previous
/// one plus a gap, which accumulates, and <c>stamp-spacing</c>'s boundary property goes with
/// it. Nothing else about the walk changes -- in particular it still only reads forward, so
/// the prefix property holds either way.
/// </para>
/// </param>
public sealed class Walk(double spacing, Func<Placement, double>? gapAfter = null)
{
    private readonly double _spacing =
        spacing > 0 ? spacing : throw new ArgumentOutOfRangeException(nameof(spacing), "a spacing is positive");

    private readonly Func<Placement, double>? _gapAfter = gapAfter;

    /// <summary>Where the next stamp goes, as a distance along the path.</summary>
    /// <remarks>
    /// Only used when the gap varies. With a constant spacing the same quantity is
    /// <c>_laid * _spacing</c>, which is a multiplication rather than a running total and is
    /// what keeps the count equal to the arithmetic.
    /// </remarks>
    private double _next;

    /// <summary>How many readings have been consumed. The next segment starts here.</summary>
    private int _consumed;

    /// <summary>
    /// How much path the walk has consumed, in the path's own units.
    /// <para>
    /// A segment shorter than what is left to run adds to this and produces nothing, which is
    /// what keeps the spacing constant across a corner rather than restarting at each one.
    /// </para>
    /// </summary>
    private double _travelled;

    /// <summary>
    /// How many stamps have been laid, which is also the index of the next one -- the stamp
    /// at the start of the path being stamp zero.
    /// </summary>
    /// <remarks>
    /// <b>Stamp <c>k</c> belongs at distance <c>k * spacing</c>, and that is computed rather
    /// than accumulated.</b> The distance to the next stamp used to be reached by adding and
    /// subtracting a spacing at a time, which drifts: a length-1 path at a spacing of 0.2 got
    /// five stamps where <c>floor(L/S) + 1</c> says six, because the last comparison saw
    /// 0.19999999999999996 and refused it. One multiplication has no such history. It also
    /// makes the loop condition the formula itself, rather than something that agrees with it.
    /// </remarks>
    private long _laid;

    private bool _started;

    /// <summary>
    /// How many segments this walk has looked at, over its whole life.
    /// <para>
    /// Here so that "resuming rather than restarting" is a number a check can compare against
    /// arithmetic, rather than a claim about the shape of a loop. A walk that resumes reads
    /// each segment once; one that restarts reads the whole path on every call.
    /// </para>
    /// </summary>
    public long SegmentsRead { get; private set; }

    /// <summary>
    /// The stamps this path has that the walk has not already reported.
    /// <para>
    /// Takes the whole path so far and reads only the part it has not seen, so a caller with
    /// a growing list need do nothing but pass it again.
    /// </para>
    /// </summary>
    public IReadOnlyList<Placement> Advance(IReadOnlyList<(double X, double Y)> path)
    {
        var found = new List<Placement>();

        if (path.Count == 0) return found;

        if (!_started)
        {
            var first = new Placement(path[0].X, path[0].Y, 0, 0);
            found.Add(first);

            _started = true;
            _consumed = 1;
            _laid = 1;
            _next = _gapAfter is null ? _spacing : Gap(first);
        }

        for (; _consumed < path.Count; _consumed++)
        {
            var from = path[_consumed - 1];
            var to = path[_consumed];

            SegmentsRead++;

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);

            if (length == 0) continue;

            var end = _travelled + length;

            // Every stamp whose distance falls within this segment. With a constant spacing
            // the next stamp is at k * spacing, which is the counting formula written out, so
            // the count cannot disagree with it. With a varying one it is the last stamp plus
            // its gap, and there is no formula to agree with.
            while (Next() <= end)
            {
                // At most 1, because the loop only runs while the next stamp is at or before
                // the end of this segment, and (end - _travelled) / length is exactly 1. So a
                // stamp cannot be placed past the segment it belongs to.
                var at = (Next() - _travelled) / length;

                var placement = new Placement(from.X + dx * at, from.Y + dy * at, _consumed - 1, at);
                found.Add(placement);

                _laid++;
                if (_gapAfter is not null) _next += Gap(placement);
            }

            _travelled = end;
        }

        return found;
    }

    /// <summary>Where the next stamp goes, as a distance along the path.</summary>
    private double Next() => _gapAfter is null ? _laid * _spacing : _next;

    /// <summary>The gap after a stamp, refused if it would not advance the walk.</summary>
    /// <remarks>
    /// <para>
    /// A gap of zero or less does not move, so the walk would stamp the same place forever.
    /// Worth a throw rather than a clamp: a brush that asks for it has a diameter of zero or
    /// a negative fraction, and both are questions for the caller.
    /// </para>
    /// <para>
    /// <b>Positive is not the same as advancing.</b> The next stamp's distance is added to
    /// the distance already travelled, and past about 1e15 units a gap of a thousandth is
    /// smaller than an ulp there -- the sum is the number it started from and the walk stops
    /// moving without anything being wrong with the gap. No stroke is that long, which is
    /// why the guard is written against the sum rather than against the gap: it costs one
    /// comparison and it is the condition that actually matters.
    /// </para>
    /// </remarks>
    private double Gap(Placement placement)
    {
        var gap = _gapAfter!(placement);

        if (gap > 0 && _next + gap > _next) return gap;

        throw new InvalidOperationException(
            $"a gap has to advance the walk; the brush asked for {gap} after a stamp at "
            + $"({placement.X}, {placement.Y}), {_next} along the path");
    }
}
