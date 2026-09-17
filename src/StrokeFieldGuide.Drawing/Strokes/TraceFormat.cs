using System.Globalization;
using System.Text.Json;

namespace StrokeFieldGuide.Strokes;

/// <summary>
/// What a trace file is: its columns, its field names, and how a reading crosses in and out.
/// </summary>
/// <remarks>
/// <para>
/// <b>One definition, because three was not survivable.</b> The writer, the recorder's reader
/// and the library's reader each knew the format separately, from their own string literals
/// and their own column resolution, and nothing connected them. A disagreement between them
/// was invisible until a file went through both halves.
/// </para>
/// <para>
/// Two have already cost something. The <c>arrived</c> column was decided nullable per reading
/// by the writer and read unconditionally by both readers, so all 33 published recordings
/// failed a second round trip — and each reader then had to be fixed on its own, which the
/// library's own note called "the cost of there being two". The column <em>order</em> was the
/// same fault waiting: it was stated once in a list and repeated by hand in the code that
/// emitted the cells, so reordering the list would have silently written every value into the
/// wrong slot.
/// </para>
/// <para>
/// So a column is declared once, here, carrying both directions with it. There is no way to
/// add one to the list and forget to write it, and no way to write them in an order other than
/// the one the file declares.
/// </para>
/// <para>
/// In the library rather than the recorder because the readers are on both sides of that line:
/// the recorder references the library, the corpus is read through the library, and a format
/// owned by an application is a format nobody else can read.
/// </para>
/// </remarks>
public static class TraceFormat
{
    public const string Format = "stroke-field-guide/take";

    /// <summary>
    /// Six, since the approach was aged on the host clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Version one put a single <c>readings</c> array at the top level, because a take was a
    /// single stroke and nothing else was imaginable. Version two replaces it with
    /// <c>strokes</c>, an array of objects each holding their own <c>readings</c> — so a
    /// one-stroke take is an array of one rather than a special case, and there is one shape to
    /// read instead of two.
    /// </para>
    /// <para>
    /// Each stroke may also carry <c>approach</c> and <c>departure</c> — the pen in the air for
    /// up to a quarter of a second either side of it, in the same columns and on the same
    /// clock. Both are absent where there is nothing to report, which is an ordinary answer
    /// rather than a fault: a pen already resting on the tablet has no approach.
    /// </para>
    /// <para>
    /// Version three adds a <c>height</c> column, from Wintab's <c>pkZ</c>. The columns are
    /// declared in the file, so a reader that takes them from there rather than counting
    /// positions needs no change; one that assumed seven does.
    /// </para>
    /// <para>
    /// Version four adds a <c>status</c> column, raw and undecoded. On Wintab it is
    /// <c>pkStatus</c>, and it is kept because two open questions are questions about what its
    /// bits mean — a queue overflow nothing has ever checked, and a proximity bit that does not
    /// behave as its name suggests.
    /// </para>
    /// <para>
    /// Version five adds <c>arrived</c>: the host clock, beside the pen's own. The columns of
    /// version four are unchanged and a reader of either can take both.
    /// </para>
    /// <para>
    /// Version six changes no columns. It marks the take where <c>approach</c>,
    /// <c>departure</c> and <c>lastSeenInTheAirMs</c> started being decided on that host clock
    /// rather than the pen's — so an approach present in a version-six file was within a
    /// quarter second of the landing in real time, and one in an earlier file was within a
    /// quarter second of a counter. Every empty approach in the corpus before this is a reading
    /// the recorder was given and discarded.
    /// </para>
    /// <para>
    /// The twelve version-one traces already recorded are <b>left as they are</b>. They are
    /// evidence, they are cited by number in the notes, and rewriting them to tidy the format
    /// would churn the corpus without adding a reading. Anything reading these files takes both
    /// shapes: a top-level <c>readings</c> is a take of one stroke.
    /// </para>
    /// </remarks>
    public const int Version = 6;

    /// <summary>What the two clocks are, said in the file so a reader need not be told.</summary>
    public const string Clocks =
        "'at' is the pen's own timestamp and 'arrived' is this application's clock, both in "
        + "microseconds from the take's first reading. They are independent: a difference in "
        + "'at' with no matching difference in 'arrived' is the device stamping a packet late, "
        + "not the pen falling silent. Readings that reached the application in the same poll "
        + "share an 'arrived' exactly.";

    /// <summary>
    /// What the timestamps in a file are measured from, and whether one of the clocks exists.
    /// </summary>
    /// <param name="Start">
    /// The take's first reading on the pen's clock. A pen timestamp has no stated origin, so a
    /// difference between two is meaningful and one alone is not.
    /// </param>
    /// <param name="Began">
    /// The same reading's arrival. A separate origin on purpose: the two clocks are unrelated,
    /// and rebasing both against one would destroy the comparison the column exists for.
    /// </param>
    /// <param name="HasHostClock">
    /// Whether the take carries a host clock at all. A property of the take and never of a
    /// reading: rebasing makes the first arrival of every take zero, so a per-reading test for
    /// zero calls that reading's real timestamp missing.
    /// </param>
    public readonly record struct Origins(long Start, long Began, bool HasHostClock);

    /// <summary>One column of a reading row, in both directions.</summary>
    /// <param name="Name">What the file calls it.</param>
    /// <param name="Cell">The text this reading puts in that slot.</param>
    /// <param name="Into">That slot's value, put back into a reading.</param>
    public sealed record Column(
        string Name,
        Func<Reading, Origins, string> Cell,
        Func<Reading, double, Reading> Into);

    /// <summary>
    /// What goes in, in order, so a reader does not have to guess at the tuples.
    /// </summary>
    /// <remarks>
    /// Arrays rather than objects per reading. A stroke is thousands of them and the key names
    /// repeated that many times are most of the file; the column list says what each slot is
    /// once.
    /// </remarks>
    public static readonly IReadOnlyList<Column> Columns =
    [
        new("at",
            (reading, from) => Whole(reading.At - from.Start),
            (reading, value) => reading with { At = (long)value }),

        // Null, not zero, where the take has no host clock: a recording made before there was
        // one genuinely has no arrival, and zero is a real arrival -- the first of every take.
        new("arrived",
            (reading, from) => from.HasHostClock ? Whole(reading.Arrived - from.Began) : "null",
            (reading, value) => reading with { Arrived = (long)value }),

        new("x",
            (reading, _) => Round(reading.X, 3),
            (reading, value) => reading with { X = value }),

        new("y",
            (reading, _) => Round(reading.Y, 3),
            (reading, value) => reading with { Y = value }),

        new("pressure",
            (reading, _) => Whole(reading.Pressure),
            (reading, value) => reading with { Pressure = Count(value) }),

        new("height",
            (reading, _) => Round(reading.Height, 2),
            (reading, value) => reading with { Height = value }),

        new("status",
            (reading, _) => Whole(reading.Status),
            (reading, value) => reading with { Status = Count(value) }),

        new("lean",
            (reading, _) => Round(reading.Lean, 2),
            (reading, value) => reading with { Lean = value }),

        new("azimuth",
            (reading, _) => Round(reading.Azimuth, 2),
            (reading, value) => reading with { Azimuth = value }),

        new("twist",
            (reading, _) => Round(reading.Twist, 2),
            (reading, value) => reading with { Twist = value }),
    ];

    /// <summary>The column names, in order, as the file declares them.</summary>
    public static readonly IReadOnlyList<string> Names = [.. Columns.Select(column => column.Name)];

    /// <summary>A reading's cells, comma separated, in the order <see cref="Columns"/> declares.</summary>
    /// <remarks>
    /// The brackets and the indentation are the writer's business, not the format's. What is
    /// here is the part a reader has to agree with.
    /// </remarks>
    public static string Row(Reading reading, Origins from) =>
        string.Join(", ", Columns.Select(column => column.Cell(reading, from)));

    /// <summary>Invariant, because a file read on a machine with another decimal point is not a file.</summary>
    private static string Round(double value, int places) =>
        Math.Round(value, places).ToString(CultureInfo.InvariantCulture);

    private static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Whole(uint value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A raw count, which cannot be negative however the file spells it.</summary>
    private static uint Count(double value) => (uint)Math.Max(0, value);

    /// <summary>
    /// Which slot of a row each column sits in, for one file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved once per file rather than per row. A trace of five thousand readings would
    /// otherwise walk the column list five thousand times to learn the same ten answers.
    /// </para>
    /// <para>
    /// <b>A file's own column list is authoritative, not this one.</b> That is the whole point
    /// of declaring them in the file: a version-two trace carries neither height nor status,
    /// and a reader that counted positions would read its angles as its height. A column the
    /// file does not carry gives the field's default, because in an old trace the absence is
    /// real — a version-two recording genuinely has no height, and a fixture that pretended
    /// otherwise would be testing a number nobody measured.
    /// </para>
    /// </remarks>
    public sealed class Layout
    {
        private readonly (int Slot, Column Column)[] _present;

        public Layout(JsonElement columns)
        {
            var named = columns.EnumerateArray().Select(column => column.GetString()).ToList();

            _present = [.. Columns
                .Select(column => (Slot: named.IndexOf(column.Name), Column: column))
                .Where(found => found.Slot >= 0)];

            Positioned = Carries("x") && Carries("y") && Carries("pressure");
        }

        /// <summary>Whether the file carries a position and a pressure, without which it is not a stroke.</summary>
        public bool Positioned { get; }

        /// <summary>Whether the file declares a column at all.</summary>
        public bool Carries(string name) => _present.Any(found => found.Column.Name == name);

        /// <summary>One row, as a reading. Absent columns keep the reading's own defaults.</summary>
        public Reading Of(JsonElement row) =>
            _present.Aggregate(
                default(Reading),
                (reading, found) => found.Column.Into(reading, Cell(row, found.Slot)));

        /// <summary>A cell of a row, or zero where the row is short or the cell is not a number.</summary>
        /// <remarks>
        /// <b>A null is an absence, not an error.</b> The writer emits null across the
        /// <c>arrived</c> column for a take with no host clock, and <c>GetDouble</c> throws on
        /// it. Both of this repository's readers had that fault and both had to be fixed
        /// separately, which is the reason there is now one of them.
        /// </remarks>
        private static double Cell(JsonElement row, int slot) =>
            slot < row.GetArrayLength() && row[slot].ValueKind == JsonValueKind.Number
                ? row[slot].GetDouble()
                : 0;
    }

    /// <summary>
    /// Every property name a trace carries, so a reader and a writer cannot spell one differently.
    /// </summary>
    /// <remarks>
    /// Literals in two files is how <c>readingsAirborneAndNotKept</c> came to be added twice by
    /// hand, and a typo in either half would have read as a zero rather than as a mistake.
    /// </remarks>
    public static class Field
    {
        public const string Format = "format";
        public const string Version = "formatVersion";
        public const string Id = "id";
        public const string Gesture = "gesture";
        public const string Intent = "intent";
        public const string RecordedAt = "recordedAt";
        public const string EndedBy = "endedBy";
        public const string StrokeCount = "strokeCount";
        public const string Clocks = "clocks";
        public const string Columns = "columns";

        public const string KeptEveryAirborneReading = "keptEveryAirborneReading";
        public const string HandedOver = "readingsHandedToTheRecorder";
        public const string OffThePad = "readingsDroppedForBeingOffThePad";
        public const string AfterTheStop = "readingsAfterTheRecordingStopped";
        public const string AirborneNotKept = "readingsAirborneAndNotKept";
        public const string AirborneKeptAlongside = "readingsAirborneKeptWithAStroke";

        public const string Counted = "whatTheSessionCounted";
        public const string FromDriver = "packetsFromTheDriver";
        public const string OutsideRegion = "packetsOutsideTheCaptureRegion";
        public const string Delivered = "pointsDelivered";

        public const string Device = "device";
        public const string Tablet = "tablet";
        public const string Driver = "driver";
        public const string Api = "api";
        public const string FullScalePressure = "fullScalePressure";
        public const string Conventions = "conventions";

        public const string Placement = "placement";
        public const string Units = "units";
        public const string Note = "note";
        public const string ScaleX = "scaleX";
        public const string ScaleY = "scaleY";
        public const string OriginX = "originX";
        public const string OriginY = "originY";

        public const string Strokes = "strokes";
        public const string Readings = "readings";
        public const string ReadingCount = "readingCount";
        public const string Approach = "approach";
        public const string Departure = "departure";
        public const string LastSeenInTheAirMs = "lastSeenInTheAirMs";
        public const string LastSeenInTheAir = "lastSeenInTheAir";

        public const string Aloft = "aloft";
        public const string AloftNote = "aloftNote";
    }
}
