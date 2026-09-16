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
public sealed class Walk(double spacing)
{
    private readonly double _spacing =
        spacing > 0 ? spacing : throw new ArgumentOutOfRangeException(nameof(spacing), "a spacing is positive");

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
            found.Add(new Placement(path[0].X, path[0].Y, 0, 0));

            _started = true;
            _consumed = 1;
            _laid = 1;
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

            // Every stamp whose distance falls within this segment. The test is
            // "k * spacing has been reached", which is the counting formula written out, so
            // the count cannot disagree with it.
            while (_laid * _spacing <= end)
            {
                // At most 1, because the loop only runs while k * spacing <= end, and
                // (end - _travelled) / length is exactly 1. So a stamp cannot be placed past
                // the segment it belongs to.
                var at = (_laid * _spacing - _travelled) / length;

                found.Add(new Placement(from.X + dx * at, from.Y + dy * at, _consumed - 1, at));

                _laid++;
            }

            _travelled = end;
        }

        return found;
    }
}
