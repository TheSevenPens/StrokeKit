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
/// <b>Every format version.</b> Version one put the readings at the top level with no strokes;
/// two introduced the strokes array; three added height, four status, five the host clock. A
/// column a file does not carry gives the field's default, because in an old trace the absence
/// is real: a version-two recording genuinely has no height, and a fixture that pretended
/// otherwise would be testing a number nobody measured.
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

        if (!root.TryGetProperty("columns", out var columns))
        {
            throw new InvalidOperationException($"{path} has no columns and is not a trace.");
        }

        var slot = new Slots(columns);
        var strokes = new List<IReadOnlyList<Reading>>();

        // Version one: no strokes array, every reading at the top level, one stroke.
        if (root.TryGetProperty("readings", out var flat) && flat.ValueKind == JsonValueKind.Array)
        {
            strokes.Add([.. flat.EnumerateArray().Select(slot.Of)]);
        }

        if (root.TryGetProperty("strokes", out var many) && many.ValueKind == JsonValueKind.Array)
        {
            foreach (var stroke in many.EnumerateArray())
            {
                if (!stroke.TryGetProperty("readings", out var rows)) continue;

                var readings = rows.EnumerateArray().Select(slot.Of).ToList();

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

        if (!root.TryGetProperty("columns", out var columns)) return [];
        if (!root.TryGetProperty("strokes", out var many)) return [];

        var slot = new Slots(columns);

        return [.. many.EnumerateArray().Select(stroke =>
            stroke.TryGetProperty("approach", out var rows)
                ? (IReadOnlyList<Reading>)[.. rows.EnumerateArray().Select(slot.Of)]
                : [])];
    }

    /// <summary>Which column of a row each field sits in, for one file.</summary>
    /// <remarks>
    /// Read once per file rather than per row. A trace of five thousand readings would
    /// otherwise walk the column list five thousand times to learn the same nine answers.
    /// </remarks>
    private sealed class Slots
    {
        private readonly int _at, _arrived, _x, _y, _pressure, _height, _status, _lean, _azimuth, _twist;

        public Slots(JsonElement columns)
        {
            var named = columns.EnumerateArray().Select(column => column.GetString()).ToList();

            int Of(string name) => named.IndexOf(name);

            _at = Of("at");
            _arrived = Of("arrived");
            _x = Of("x");
            _y = Of("y");
            _pressure = Of("pressure");
            _height = Of("height");
            _status = Of("status");
            _lean = Of("lean");
            _azimuth = Of("azimuth");
            _twist = Of("twist");

            if (_x < 0 || _y < 0 || _pressure < 0)
            {
                throw new InvalidOperationException(
                    "A trace without x, y and pressure columns is not a stroke.");
            }
        }

        public Reading Of(JsonElement row) => new(
            X: Cell(row, _x),
            Y: Cell(row, _y),
            Pressure: (uint)Math.Max(0, Cell(row, _pressure)),
            At: (long)Cell(row, _at),
            Height: Cell(row, _height),
            Status: (uint)Math.Max(0, Cell(row, _status)),
            Lean: Cell(row, _lean),
            Azimuth: Cell(row, _azimuth),
            Twist: Cell(row, _twist),
            Arrived: (long)Cell(row, _arrived));

        /// <summary>A cell of a row, or zero where the column is absent or the row is short.</summary>
        /// <remarks>
        /// <b>A null is an absence, not an error.</b> A take that carries no host clock has a
        /// null in that column on every row, and <c>GetDouble</c> throws on it. This is the
        /// second reader in this repository to have had that fault and the second to have it
        /// fixed, which is the cost of there being two: see the note on this class.
        /// </remarks>
        private static double Cell(JsonElement row, int column) =>
            column >= 0 && column < row.GetArrayLength()
            && row[column].ValueKind == JsonValueKind.Number
                ? row[column].GetDouble()
                : 0;
    }
}
