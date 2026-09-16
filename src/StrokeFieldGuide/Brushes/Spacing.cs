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
        IReadOnlyList<(double X, double Y)> path, double spacing)
    {
        if (!(spacing > 0)) throw new ArgumentOutOfRangeException(nameof(spacing), "a spacing is positive");
        if (path.Count == 0) return [];

        var positions = new List<Placement> { new(path[0].X, path[0].Y, 0, 0) };

        // How far past the last stamp we have travelled. A segment shorter than what is left
        // to run adds to this and produces nothing, which is what keeps the spacing constant
        // across a corner rather than restarting at each one.
        var since = 0.0;

        for (var index = 1; index < path.Count; index++)
        {
            var from = path[index - 1];
            var to = path[index];

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);

            if (length == 0) continue;

            var travelled = 0.0;
            while (since + (length - travelled) >= spacing)
            {
                travelled += spacing - since;
                since = 0;

                var at = travelled / length;
                positions.Add(new Placement(from.X + dx * at, from.Y + dy * at, index - 1, at));
            }

            since += length - travelled;
        }

        return positions;
    }
}
