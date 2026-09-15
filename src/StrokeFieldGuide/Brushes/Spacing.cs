namespace StrokeFieldGuide.Brushes;

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
        IReadOnlyList<(double X, double Y)> path, double spacing)
    {
        if (!(spacing > 0)) throw new ArgumentOutOfRangeException(nameof(spacing), "a spacing is positive");
        if (path.Count == 0) return [];

        var positions = new List<(double X, double Y)> { path[0] };

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
                positions.Add((from.X + dx * at, from.Y + dy * at));
            }

            since += length - travelled;
        }

        return positions;
    }
}
