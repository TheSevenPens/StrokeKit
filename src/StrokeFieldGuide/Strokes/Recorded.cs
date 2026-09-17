namespace StrokeFieldGuide.Strokes;

/// <summary>
/// Strokes drawn by a hand, for the checks that <see cref="Corpus"/>'s cannot answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>These do not replace the generated fixtures and must not.</b> A brush engine correct on
/// strokes with no timing noise, no dropouts and no jitter has a whole class of fault ruled
/// out, and that is worth keeping separable — a check that fails on a recording and passes on
/// <c>syn-straight</c> is telling you something a check that fails on both is not.
/// </para>
/// <para>
/// What they add is the argument <see cref="Corpus"/> already makes about itself: a page's
/// checks only ever see the strokes that page's author thought of. Nobody would think to
/// generate a stroke that sits still for three hundred milliseconds and then covers 240 px in
/// twelve readings. A hand made one on the first try, and it is in this list.
/// </para>
/// <para>
/// <b>Translated into place, never scaled.</b> A recorded stroke sits at desktop coordinates
/// near 3000 and can run 800 px long, where every generated fixture fits 20..220. Scaling one
/// to match would change the ratio between the step between readings and the nib drawn over
/// them, which is the exact quantity most of these are here to exercise: a flick with 44 px
/// between readings stops being a flick if it is shrunk until the gap is 15. So a recorded
/// fixture keeps its true size and its true spacing, and only its origin moves.
/// </para>
/// </remarks>
public static class Recorded
{
    /// <summary>
    /// Where the recordings live, relative to the repository root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>corpus/</c> is a submodule of <c>StrokeCorpus</c>, which is where these recordings
    /// are published and where new ones arrive. <b>The submodule is pinned, and that is the
    /// point.</b> This corpus is not only a folder of data: five page checks draw against
    /// <see cref="Telling"/>, whose members are chosen by measurement, so a recording
    /// contributed by a stranger with a wider step than anything here would silently become
    /// one of the fixtures those checks run on. Pinned, adopting new recordings is a commit
    /// in this repository that says so.
    /// </para>
    /// <para>
    /// A bare <c>traces/</c> is still accepted, because that is where they lived before the
    /// corpus was published and a checkout from then should still find them.
    /// </para>
    /// </remarks>
    public static readonly string[] Folders = ["corpus/traces", "traces"];

    /// <summary>The corner every recorded fixture is moved to.</summary>
    /// <remarks>
    /// The same inset the generated corpus uses, so a surface sized for one holds the other's
    /// beginning. Nothing promises a recorded stroke ends inside a 240 px box — many do not,
    /// which is the point — so a check drawing these sizes its surface from the fixture rather
    /// than from a constant.
    /// </remarks>
    public const double Inset = 20;

    /// <summary>Every stroke of every trace in the folder, longest file first.</summary>
    /// <remarks>
    /// Enumerated from disk rather than listed by name. A corpus naming its files is a corpus
    /// that silently shrinks when one is renamed, and these are evidence: the folder is the
    /// list.
    /// </remarks>
    public static IReadOnlyList<Fixture> All => Read(Root());

    /// <summary>
    /// A handful of recorded strokes, each the most extreme of the corpus on one axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For checks that draw. <see cref="All"/> is 212 strokes of up to 520 readings, and a
    /// check that redraws a stroke a reading at a time over four brush configurations is
    /// quadratic in the readings — pointed at the whole corpus it turns a ten-second suite
    /// into a coffee break, and a suite nobody runs proves nothing.
    /// </para>
    /// <para>
    /// Chosen by measurement rather than by name, so the set follows the folder: the stroke
    /// whose spacing varies most, the one with the widest single step, the one with the most
    /// steps of no length, the one whose pressure swings furthest, and the longest. A stroke
    /// that is the extreme on two axes appears once.
    /// </para>
    /// <para>
    /// <b>This is a test set and <see cref="All"/> is the evidence.</b> Anything measuring the
    /// corpus — how uneven it is, what it covers — asks <see cref="All"/>. Anything drawing
    /// hundreds of marks asks this.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Fixture> Telling
    {
        get
        {
            var all = All;

            if (all.Count == 0) return [];

            Fixture Most(Func<IReadOnlyList<Reading>, double> by) =>
                all.MaxBy(fixture => by(fixture.Readings))!;

            return [.. new[]
            {
                Most(Uneven),
                Most(Widest),
                Most(Stalls),
                Most(Swing),
                Most(readings => readings.Count),
            }.Distinct()];
        }
    }

    private static double Uneven(IReadOnlyList<Reading> readings)
    {
        var steps = Steps(readings);

        if (steps.Count == 0) return 1;

        steps.Sort();

        var median = steps[steps.Count / 2];

        return median > 0 ? steps[^1] / median : 1;
    }

    private static double Widest(IReadOnlyList<Reading> readings) =>
        Steps(readings) is { Count: > 0 } steps ? steps.Max() : 0;

    private static double Stalls(IReadOnlyList<Reading> readings) =>
        Steps(readings).Count(step => step == 0);

    private static double Swing(IReadOnlyList<Reading> readings)
    {
        var low = readings.Min(reading => reading.Pressure);
        var high = readings.Max(reading => reading.Pressure);

        return low > 0 ? (double)high / low : high;
    }

    private static List<double> Steps(IReadOnlyList<Reading> readings)
    {
        var steps = new List<double>(Math.Max(0, readings.Count - 1));

        for (var each = 1; each < readings.Count; each++)
        {
            steps.Add(Math.Sqrt(Math.Pow(readings[each].X - readings[each - 1].X, 2)
                              + Math.Pow(readings[each].Y - readings[each - 1].Y, 2)));
        }

        return steps;
    }

    /// <summary>The box a fixture needs, whichever corpus it came from.</summary>
    /// <remarks>
    /// A recorded stroke can run 800 px where every generated one fits 240, so a check that
    /// sized its surface from <see cref="Corpus.Extent"/> would draw a recorded fixture with
    /// most of it off the edge — and then compare two clipped pictures and call them equal.
    /// </remarks>
    public static double Extent(Fixture fixture) => Math.Max(
        Corpus.Extent,
        Math.Ceiling(Math.Max(
            fixture.Readings.Max(reading => reading.X),
            fixture.Readings.Max(reading => reading.Y)) + Inset));

    /// <summary>Every stroke of every trace under one folder.</summary>
    public static IReadOnlyList<Fixture> Read(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return [];

        var fixtures = new List<Fixture>();

        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order())
        {
            var take = Path.GetFileNameWithoutExtension(path);

            // A trace this reader cannot make sense of is skipped rather than thrown on. The
            // folder is evidence and will accumulate files nobody planned for; one of them
            // must not take the whole corpus down.
            IReadOnlyList<IReadOnlyList<Reading>> strokes;

            try
            {
                strokes = Traces.Read(path);
            }
            catch (Exception)
            {
                continue;
            }

            for (var each = 0; each < strokes.Count; each++)
            {
                var readings = Placed(strokes[each]);

                // One reading is not a stroke to draw, whatever else it is. A tap belongs to
                // points-to-stroke's subject and has its own generated fixture.
                if (readings.Count < 2) continue;

                fixtures.Add(new Fixture(
                    $"rec-{Short(take)}-{each + 1}",
                    $"a stroke drawn by a hand: {Says(readings)}",
                    readings));
            }
        }

        return fixtures;
    }

    /// <summary>
    /// The stroke moved so its own bounding box starts at the inset, and nothing else changed.
    /// </summary>
    /// <remarks>
    /// Translation only. See the note on this class: scaling would alter the one relationship
    /// most of these fixtures exist to exercise.
    /// </remarks>
    private static IReadOnlyList<Reading> Placed(IReadOnlyList<Reading> readings)
    {
        if (readings.Count == 0) return readings;

        var lowX = readings.Min(reading => reading.X);
        var lowY = readings.Min(reading => reading.Y);

        return [.. readings.Select(reading => reading with
        {
            X = reading.X - lowX + Inset,
            Y = reading.Y - lowY + Inset,
        })];
    }

    /// <summary>What this stroke is, in the terms a failure message wants.</summary>
    private static string Says(IReadOnlyList<Reading> readings)
    {
        var length = 0.0;

        for (var each = 1; each < readings.Count; each++)
        {
            length += Math.Sqrt(Math.Pow(readings[each].X - readings[each - 1].X, 2)
                              + Math.Pow(readings[each].Y - readings[each - 1].Y, 2));
        }

        var steps = new double[Math.Max(1, readings.Count - 1)];

        for (var each = 1; each < readings.Count; each++)
        {
            steps[each - 1] = Math.Sqrt(Math.Pow(readings[each].X - readings[each - 1].X, 2)
                                      + Math.Pow(readings[each].Y - readings[each - 1].Y, 2));
        }

        var widest = steps.Max();

        return $"{readings.Count} readings over {length:F0} px, "
             + $"steps up to {widest:F0} px, "
             + $"pressure {readings.Min(reading => reading.Pressure)} to "
             + $"{readings.Max(reading => reading.Pressure)}";
    }

    /// <summary>
    /// The take's name without the tablet, which every one shares.
    /// </summary>
    /// <remarks>
    /// The time survives and the date does not. Cutting the whole timestamp gave two takes of
    /// the same gesture the same name — <c>rec-freeform-1</c> twice — and a corpus whose names
    /// collide is one where a failure message names a stroke that might be either of two.
    /// </remarks>
    private static string Short(string take)
    {
        var cut = take.IndexOf("-wacom-", StringComparison.OrdinalIgnoreCase);

        if (cut <= 0) return take;

        var when = take.LastIndexOf('-');

        return when > cut ? $"{take[..cut]}-{take[(when + 1)..]}" : take[..cut];
    }

    /// <summary>
    /// The recordings folder, found by walking up from wherever this is running.
    /// </summary>
    /// <remarks>
    /// Walked up to rather than configured, because a test host, the recorder and a tool
    /// each run from a different place and none of them is the repository root.
    /// </remarks>
    public static string? Root()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);

        while (at is not null)
        {
            foreach (var folder in Folders)
            {
                var here = Path.Combine(at.FullName, folder);

                if (Directory.Exists(here)) return here;
            }

            at = at.Parent;
        }

        return null;
    }
}
