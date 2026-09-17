using System.Text.Json;
using StrokeKit.Strokes;

namespace StrokeKit.Tests;

/// <summary>
/// That a trace is read by the columns it declares, not by the columns this version writes.
/// </summary>
/// <remarks>
/// <para>
/// The format is self-describing on purpose: a file carries a <c>columns</c> list so that a
/// reader can take an older recording whose rows are shorter, or in another order, without
/// knowing which version wrote it. That is real code — and until now it had no test, because
/// <b>every fixture in the corpus was written by the current writer</b>. The reader was
/// general and the evidence was not: a change that broke the handling of a missing column
/// would have passed the whole suite.
/// </para>
/// <para>
/// So these fixtures move one thing each, and the thing they move is the <em>layout</em>
/// rather than the stroke. None of them is a recording of anything; they exist to be
/// awkward in a way no recording this tool makes ever is.
/// </para>
/// </remarks>
public class TraceLayouts
{
    private static TraceFormat.Layout Of(params string[] columns) =>
        new(JsonDocument.Parse(JsonSerializer.Serialize(columns)).RootElement);

    private static JsonElement Row(string cells) =>
        JsonDocument.Parse(cells).RootElement;

    /// <summary>The layout this version writes, read back as it was written.</summary>
    [Fact]
    public void TakesTheCanonicalLayout()
    {
        var reading = Of([.. TraceFormat.Names])
            .Of(Row("[17, 31, 4.5, 6.25, 900, 12.5, 3, 20.5, 175.25, 90.5]"));

        Assert.Equal(17, reading.At);
        Assert.Equal(31, reading.Arrived);
        Assert.Equal(4.5, reading.X);
        Assert.Equal(6.25, reading.Y);
        Assert.Equal(900u, reading.Pressure);
        Assert.Equal(12.5, reading.Height);
        Assert.Equal(3u, reading.Status);
        Assert.Equal(20.5, reading.Lean);
        Assert.Equal(175.25, reading.Azimuth);
        Assert.Equal(90.5, reading.Twist);
    }

    /// <summary>
    /// A value follows its column's name, never its position.
    /// </summary>
    /// <remarks>
    /// The fixture that would have caught a reader counting slots. Every column is present and
    /// the order is reversed, so a reader that assumed this version's order would read the
    /// twist as the timestamp and still produce a plausible-looking stroke.
    /// </remarks>
    [Fact]
    public void FollowsTheNamesAndNotTheOrder()
    {
        var backwards = TraceFormat.Names.Reverse().ToArray();

        var reading = Of(backwards)
            .Of(Row("[90.5, 175.25, 20.5, 3, 12.5, 900, 6.25, 4.5, 31, 17]"));

        Assert.Equal(17, reading.At);
        Assert.Equal(4.5, reading.X);
        Assert.Equal(900u, reading.Pressure);
        Assert.Equal(90.5, reading.Twist);
    }

    /// <summary>
    /// A version-two trace: no height and no status, and the absence is real.
    /// </summary>
    /// <remarks>
    /// Those fields take their defaults rather than borrowing the next column along. A reader
    /// counting positions would have read this file's lean as its height.
    /// </remarks>
    [Fact]
    public void LeavesAnAbsentColumnAtItsDefault()
    {
        var reading = Of("at", "x", "y", "pressure", "lean", "azimuth", "twist")
            .Of(Row("[17, 4.5, 6.25, 900, 20.5, 175.25, 90.5]"));

        Assert.Equal(4.5, reading.X);
        Assert.Equal(20.5, reading.Lean);

        Assert.Equal(0, reading.Height);
        Assert.Equal(0u, reading.Status);
        Assert.Equal(0, reading.Arrived);
    }

    /// <summary>
    /// A null is an absence, not an error.
    /// </summary>
    /// <remarks>
    /// The writer puts null across the whole <c>arrived</c> column for a take with no host
    /// clock, and <c>GetDouble</c> throws on it. Both of this repository's readers had that
    /// fault, separately, and it is what made all 33 published recordings fail a second round
    /// trip — so it is pinned here, once, for the one reader there now is.
    /// </remarks>
    [Fact]
    public void ReadsANullAsAnAbsence()
    {
        var reading = Of([.. TraceFormat.Names])
            .Of(Row("[17, null, 4.5, 6.25, 900, 12.5, 3, 20.5, 175.25, 90.5]"));

        Assert.Equal(0, reading.Arrived);
        Assert.Equal(17, reading.At);
        Assert.Equal(4.5, reading.X);
    }

    /// <summary>A row shorter than its column list gives defaults, not an exception.</summary>
    [Fact]
    public void ToleratesAShortRow()
    {
        var reading = Of([.. TraceFormat.Names]).Of(Row("[17, 31, 4.5, 6.25, 900]"));

        Assert.Equal(4.5, reading.X);
        Assert.Equal(900u, reading.Pressure);
        Assert.Equal(0, reading.Twist);
    }

    /// <summary>A column this version has never heard of is ignored, not fatal.</summary>
    /// <remarks>
    /// So that a trace written by a later version, or by somebody else's recorder, still reads
    /// as far as it can. The corpus invites contributions and a stricter reader would reject
    /// the first one that carried anything extra.
    /// </remarks>
    [Fact]
    public void IgnoresAColumnItDoesNotKnow()
    {
        var reading = Of("x", "y", "pressure", "barometricPressure")
            .Of(Row("[4.5, 6.25, 900, 1013]"));

        Assert.Equal(4.5, reading.X);
        Assert.Equal(6.25, reading.Y);
        Assert.Equal(900u, reading.Pressure);
    }

    /// <summary>A pressure the file spells negative is still a count.</summary>
    [Fact]
    public void WillNotTakeANegativeCount()
    {
        var reading = Of("x", "y", "pressure", "status").Of(Row("[4.5, 6.25, -3, -1]"));

        Assert.Equal(0u, reading.Pressure);
        Assert.Equal(0u, reading.Status);
    }

    [Theory]
    [InlineData(true, "x", "y", "pressure")]
    [InlineData(true, "at", "arrived", "x", "y", "pressure", "height")]
    [InlineData(false, "x", "y")]
    [InlineData(false, "at", "x", "pressure")]
    [InlineData(false, "lean", "azimuth", "twist")]
    public void KnowsWhetherItIsAStrokeAtAll(bool positioned, params string[] columns) =>
        Assert.Equal(positioned, Of(columns).Positioned);

    /// <summary>
    /// The cells go out in the order the file declares, because they are made from that list.
    /// </summary>
    /// <remarks>
    /// <b>This is the pairing the owner exists for.</b> The order used to be stated once in a
    /// column list and repeated by hand in the code that emitted the cells, so the two could
    /// drift and nothing would say so — reordering the list would have written every value into
    /// the wrong slot, in a file that still parsed. Written and read through one list, a
    /// reading survives the trip whatever that list says.
    /// </remarks>
    [Fact]
    public void WritesWhatItReads()
    {
        var reading = new Reading(
            X: 4.5, Y: 6.25, Pressure: 900, At: 17, Height: 12.5, Status: 3,
            Lean: 20.5, Azimuth: 175.25, Twist: 90.5, Arrived: 31);

        var row = TraceFormat.Row(reading, new TraceFormat.Origins(0, 0, true));

        Assert.Equal(reading, Of([.. TraceFormat.Names]).Of(Row($"[{row}]")));
    }

    /// <summary>The exact text of a row, so a change to it has to be meant.</summary>
    /// <remarks>
    /// Pinned rather than derived. A test that asked the writer what it writes would agree with
    /// any answer, including one that silently rounds a coordinate differently and churns every
    /// file in the corpus on its next rewrite.
    /// </remarks>
    [Fact]
    public void PinsTheCellText()
    {
        var reading = new Reading(
            X: 4.5678, Y: 6.2543, Pressure: 900, At: 1017, Height: 12.567, Status: 3,
            Lean: 20.5432, Azimuth: 175.2567, Twist: 90.5, Arrived: 2031);

        Assert.Equal(
            "1000, 2000, 4.568, 6.254, 900, 12.57, 3, 20.54, 175.26, 90.5",
            TraceFormat.Row(reading, new TraceFormat.Origins(17, 31, true)));
    }

    /// <summary>With no host clock the whole column is null, and zero is never mistaken for it.</summary>
    /// <remarks>
    /// Zero is a real arrival: after rebasing it is the first reading of every take. That is
    /// why the decision is a property of the take and not a test on the reading.
    /// </remarks>
    [Fact]
    public void WritesNullForAMissingHostClock()
    {
        var reading = new Reading(X: 1, Y: 2, Pressure: 3, At: 10, Arrived: 0);

        Assert.Contains("null", TraceFormat.Row(reading, new TraceFormat.Origins(0, 0, false)));
        Assert.DoesNotContain("null", TraceFormat.Row(reading, new TraceFormat.Origins(0, 0, true)));
    }
}
