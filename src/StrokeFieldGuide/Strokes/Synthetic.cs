
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

    /// <summary>
    /// One reading, at a position, with the pressure that decides contact.
    /// <para>
    /// The position is in the ink's units and nothing else, which is what changed when
    /// <see cref="Strokes.Reading"/> took over from the device's point type here. Until then
    /// this method packed its arguments into fields documented as physical screen pixels and
    /// the brush engine read them back out as the drawing's own. That held for every fixture
    /// in this guide -- because every fixture is made here -- and stopped holding for the
    /// first application that drew with a real pen.
    /// </para>
    /// </summary>
    public static Reading Reading(double x, double y, uint pressure = Pressing, long at = 0) =>
        new(x, y, pressure, at);

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
    public static IReadOnlyList<Reading> Line(
        double fromX, double fromY, double toX, double toY, int readings, long microsecondsApart = 8000)
    {
        if (readings < 2) throw new ArgumentOutOfRangeException(nameof(readings), "a line has two ends");

        var points = new List<Reading>(readings);

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
    public static IReadOnlyList<Reading> Arc(
        double centreX, double centreY, double radius, double fromDegrees, double toDegrees, int readings,
        long microsecondsApart = 8000)
    {
        if (readings < 2) throw new ArgumentOutOfRangeException(nameof(readings), "an arc has two ends");

        var points = new List<Reading>(readings);

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
    /// A tap: one reading, in contact, and nothing else.
    /// <para>
    /// The shortest stroke there is, and a real one -- a dot on a page is a tap. It is here
    /// because a path with one point has no length, no direction and no segments, so every
    /// loop over a stroke's segments runs zero times and every division by its length is a
    /// division by zero. A brush engine that has never been given one usually crashes on the
    /// first dot somebody draws.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Reading> Tap(double x, double y, long at = 0) => [Reading(x, y, at: at)];

    /// <summary>
    /// A hover reading: off the tablet, so it separates strokes rather than joining them.
    /// </summary>
    public static Reading Hover(double x, double y, long at = 0) => Reading(x, y, 0, at);
}
