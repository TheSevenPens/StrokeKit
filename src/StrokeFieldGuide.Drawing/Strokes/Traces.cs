using System.Text.Json;

namespace StrokeFieldGuide.Strokes;

/// <summary>
/// Reads a recorded trace into readings.
/// </summary>
/// <remarks>
/// <para>
/// The recorder's own <c>Reopen</c> rebuilds a whole take — its gesture, its backend, its
/// counts, its airborne record — because the recorder shows all of that. A fixture needs none
/// of it. What a brush is given is a list of readings, so that is all this returns, and it can
/// live in the library where the corpus lives rather than in an application the library does
/// not reference.
/// </para>
/// <para>
/// <b>Every format version</b>, because <see cref="TraceFormat.Layout"/> takes the columns from
/// the file rather than assuming this version's. What each version added is recorded on
/// <see cref="TraceFormat.Version"/>; what is left here is the one shape change a column list
/// cannot describe, which is version one putting its readings at the top level rather than in
/// strokes.
/// </para>
/// </remarks>
public static class Traces
{
    /// <summary>Every stroke of a trace, in the order it was drawn.</summary>
    public static IReadOnlyList<IReadOnlyList<Reading>> Read(string path)
    {
        using var file = File.OpenRead(path);
        using var json = JsonDocument.Parse(file);

        var root = json.RootElement;

        if (!root.TryGetProperty(TraceFormat.Field.Columns, out var columns))
        {
            throw new InvalidOperationException($"{path} has no columns and is not a trace.");
        }

        var layout = new TraceFormat.Layout(columns);

        if (!layout.Positioned)
        {
            throw new InvalidOperationException(
                "A trace without x, y and pressure columns is not a stroke.");
        }

        var strokes = new List<IReadOnlyList<Reading>>();

        // Version one: no strokes array, every reading at the top level, one stroke.
        if (root.TryGetProperty(TraceFormat.Field.Readings, out var flat)
            && flat.ValueKind == JsonValueKind.Array)
        {
            strokes.Add([.. flat.EnumerateArray().Select(layout.Of)]);
        }

        if (root.TryGetProperty(TraceFormat.Field.Strokes, out var many)
            && many.ValueKind == JsonValueKind.Array)
        {
            foreach (var stroke in many.EnumerateArray())
            {
                if (!stroke.TryGetProperty(TraceFormat.Field.Readings, out var rows)) continue;

                var readings = rows.EnumerateArray().Select(layout.Of).ToList();

                if (readings.Count > 0) strokes.Add(readings);
            }
        }

        return strokes;
    }

    /// <summary>The pen in the air before each stroke, where the trace kept it.</summary>
    /// <remarks>
    /// Separate from <see cref="Read"/> rather than bundled with it, because a brush is never
    /// given an approach: a stroke starts where the tip goes down. This is here for the checks
    /// that are about how a stroke <em>begins</em>, which need the readings before it and must
    /// not be handed them as though they were part of the mark.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<Reading>> Approaches(string path)
    {
        using var file = File.OpenRead(path);
        using var json = JsonDocument.Parse(file);

        var root = json.RootElement;

        if (!root.TryGetProperty(TraceFormat.Field.Columns, out var columns)) return [];
        if (!root.TryGetProperty(TraceFormat.Field.Strokes, out var many)) return [];

        var layout = new TraceFormat.Layout(columns);

        return [.. many.EnumerateArray().Select(stroke =>
            stroke.TryGetProperty(TraceFormat.Field.Approach, out var rows)
                ? (IReadOnlyList<Reading>)[.. rows.EnumerateArray().Select(layout.Of)]
                : [])];
    }
}
