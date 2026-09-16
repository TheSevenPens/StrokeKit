using WinPenKit;

namespace StrokeFieldGuide.Strokes;

/// <summary>
/// Pen points made up rather than recorded.
/// <para>
/// The whole of Part II runs on these, and the reason is worth stating once. A recorded
/// stroke carries everything at once: jitter, an uneven report rate, whatever the hardware
/// did at the edges of contact. When a mark comes out wrong there is no way to tell which of
/// those was responsible, or whether any of them was. A synthetic stroke has exactly the
/// properties it was given, so a mark that comes out wrong is the brush engine.
/// </para>
/// <para>
/// What that costs is that nothing here is evidence about real pens. A brush engine correct
/// on these is a brush engine with one class of fault ruled out, and Part IV is where the
/// rest are.
/// </para>
/// </summary>
public static class Synthetic
{
    /// <summary>
    /// Mid-range, and nothing about this number means anything yet.
    /// <para>
    /// Pressure decides contact and nothing else until width-from-pressure is written, so a
    /// constant is honest: a varying one would look like it was being used.
    /// </para>
    /// </summary>
    public const uint Pressing = 512;

    /// <summary>One reading, at a position, with the pressure that decides contact.</summary>
    public static PenPoint Reading(double x, double y, uint pressure = Pressing, long at = 0) =>
        new(x, y, (int)x, (int)y, pressure, 0, 90, 0, 0, 0, 0, 0, 0, 0, InputApi.WintabSystem, at);

    /// <summary>
    /// Readings evenly spaced along a straight line, the first at the start and the last at
    /// the end.
    /// <para>
    /// Evenly spaced in <b>distance</b>, which a real pen's readings are not: a hand moves at
    /// whatever speed it likes and the pen reports on a clock. That difference is the subject
    /// of stamp spacing, and a generator that reproduced it here would mean a mark could be
    /// wrong for two reasons at once.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PenPoint> Line(
        double fromX, double fromY, double toX, double toY, int readings, long microsecondsApart = 8000)
    {
        if (readings < 2) throw new ArgumentOutOfRangeException(nameof(readings), "a line has two ends");

        var points = new List<PenPoint>(readings);

        for (var index = 0; index < readings; index++)
        {
            var along = index / (double)(readings - 1);

            points.Add(Reading(
                fromX + (toX - fromX) * along,
                fromY + (toY - fromY) * along,
                at: index * microsecondsApart));
        }

        return points;
    }

    /// <summary>
    /// Readings along an arc, for when a straight line would hide a fault that only a change
    /// of direction produces.
    /// </summary>
    public static IReadOnlyList<PenPoint> Arc(
        double centreX, double centreY, double radius, double fromDegrees, double toDegrees, int readings,
        long microsecondsApart = 8000)
    {
        if (readings < 2) throw new ArgumentOutOfRangeException(nameof(readings), "an arc has two ends");

        var points = new List<PenPoint>(readings);

        for (var index = 0; index < readings; index++)
        {
            var along = index / (double)(readings - 1);
            var radians = (fromDegrees + (toDegrees - fromDegrees) * along) * Math.PI / 180;

            points.Add(Reading(
                centreX + Math.Cos(radians) * radius,
                centreY + Math.Sin(radians) * radius,
                at: index * microsecondsApart));
        }

        return points;
    }

    /// <summary>
    /// A hover reading: off the tablet, so it separates strokes rather than joining them.
    /// </summary>
    public static PenPoint Hover(double x, double y, long at = 0) => Reading(x, y, 0, at);
}
