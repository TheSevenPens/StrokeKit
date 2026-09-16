namespace StrokeFieldGuide.Surfaces;

/// <summary>
/// Where a position given in logical units lands in surface pixels.
/// <para>
/// Scale and origin, per axis. Nothing else: no rotation, no skew. A stroke pipeline that
/// needs those has a different problem, and keeping them out means every fault this
/// transform can have is one of four, each with its own signature in the pixels.
/// </para>
/// <para>
/// This is the <b>ink</b> transform. It decides where a mark is made, once, permanently.
/// It is not the view transform, which decides how the finished surface is shown and may
/// change all day without the ink moving. Conflating them is the fault that produces ink
/// correctly placed at one zoom and wrong at another.
/// </para>
/// </summary>
public readonly record struct InkTransform(double ScaleX, double ScaleY, double OriginX, double OriginY)
{
    /// <summary>
    /// The transform that puts the whole of a surface's logical area onto its pixels.
    /// <para>
    /// Derived from the two sizes the surface actually has, not from the scale anyone asked
    /// for. A surface rounded to whole pixels does not have exactly the scale it was
    /// requested with, and using the requested one puts every position slightly wrong in
    /// proportion to its distance from the origin.
    /// </para>
    /// </summary>
    public static InkTransform For(Surface surface) => new(surface.ScaleX, surface.ScaleY, 0, 0);

    /// <summary>
    /// The transform that marks the surface pixel a pen is over, for a pen whose readings
    /// are in desktop pixels and a surface shown in a box at a zoom and a pan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the hop from the device into the drawing, and it is the one an application has
    /// to get right before any of the rest of this guide applies to it. There are two parts
    /// and leaving out either produces a mark in the wrong place with nothing reported.
    /// </para>
    /// <para>
    /// <b>Where the box is.</b> A session reports positions on the desktop, so the box's own
    /// top left has to come off them. Leave this out and every mark is displaced by the
    /// distance from the desktop's corner to the box's -- the same displacement everywhere,
    /// which is <see cref="RegistrationFault.Origin"/>.
    /// </para>
    /// <para>
    /// <b>Where the view has got to.</b> What is left is a position in the box, and a box
    /// shows a surface through a view, which has a pan and a zoom. The pan is not zero and
    /// does not stay put: it is recentred whenever the box changes size or the window moves
    /// to a monitor at a different scaling. Leave this out and the mark lands where the
    /// surface would have been had the reader never resized anything -- again the same
    /// displacement everywhere, so the two omissions look alike and are told apart by
    /// whether the displacement changes when the window is resized.
    /// </para>
    /// <para>
    /// The zoom divides rather than multiplies, because this runs the opposite way to the
    /// view: the view takes a surface pixel to a display pixel, and a pen arrives as a
    /// display pixel wanting the surface pixel under it. Magnify twice and a hand moving two
    /// display pixels has moved one surface pixel.
    /// </para>
    /// </remarks>
    /// <param name="zoom">Display pixels per surface pixel, from the view.</param>
    /// <param name="panX">Where the surface's top left sits in the box, in display pixels.</param>
    /// <param name="boxLeft">The box's top left on the desktop, in physical pixels.</param>
    public static InkTransform ForPenOver(
        double zoom, double panX, double panY, double boxLeft, double boxTop)
    {
        if (!(zoom > 0)) throw new ArgumentOutOfRangeException(nameof(zoom), "a zoom is positive");

        return new(
            1 / zoom,
            1 / zoom,
            -(boxLeft + panX) / zoom,
            -(boxTop + panY) / zoom);
    }

    /// <summary>Logical position to pixel position.</summary>
    public (double X, double Y) ToSurface(double x, double y) =>
        (OriginX + x * ScaleX, OriginY + y * ScaleY);

    /// <summary>
    /// Pixel position back to logical.
    /// <para>
    /// Here because a round trip is the cheapest check a transform can have, and because a
    /// pipeline that stores pixel positions and later wants logical ones needs the inverse
    /// to be the inverse rather than a second transform built by hand.
    /// </para>
    /// </summary>
    public (double X, double Y) ToLogical(double x, double y) =>
        (ScaleX == 0 ? 0 : (x - OriginX) / ScaleX,
         ScaleY == 0 ? 0 : (y - OriginY) / ScaleY);
}

/// <summary>
/// What a registration error looks like, named by what causes it.
/// <para>
/// The value of the taxonomy is that the four are distinguishable from the marks alone,
/// without reading any code. Measuring displacement at two or more positions says which
/// one you have, and therefore which of the four numbers in an
/// <see cref="InkTransform"/> is wrong.
/// </para>
/// </summary>
public enum RegistrationFault
{
    /// <summary>Every mark is where it should be. Displacement is zero everywhere.</summary>
    Registered,

    /// <summary>The same displacement everywhere, in both axes. The origin is wrong.</summary>
    Origin,

    /// <summary>Displacement growing with distance from the origin. The scale is wrong.</summary>
    Scale,

    /// <summary>Both: displaced at the origin and growing from there.</summary>
    OriginAndScale,

    /// <summary>Fewer than two distinct positions were measured, which cannot tell these apart.</summary>
    NotEnoughPositions,

    /// <summary>
    /// An axis on which every sample sits at the same place, which cannot tell a translation
    /// from a scale on that axis however many samples there are.
    /// </summary>
    NotEnoughSpread,
}

/// <summary>
/// Reads a registration fault from measured displacements.
/// </summary>
public static class Registration
{
    /// <summary>
    /// Which fault the measurements show.
    /// <para>
    /// A line is fitted through the displacements against position, per axis, and the fault
    /// is read off its two terms: the intercept is the part that does not depend on where
    /// you measured, which is the origin, and the slope is the part that does, which is the
    /// scale.
    /// </para>
    /// <para>
    /// An earlier version compared the displacement at the nearest sample against the
    /// furthest, treating the nearest as the origin. It is not. A wrong scale displaces
    /// every position except the exact origin, so a sample taken at a tenth of the way
    /// across a surface is already displaced, and a pure scale fault was reported as a scale
    /// fault and an origin fault together. The fit has no such assumption: it extrapolates
    /// back to the origin instead of hoping a sample is there.
    /// </para>
    /// <para>
    /// Two positions are still the minimum, and they must be genuinely distinct <b>on each
    /// axis</b>. With two the fit is exact and any measurement noise goes straight into both
    /// terms, which is why the tolerance has to exceed the noise in the measurement rather
    /// than being as small as arithmetic allows.
    /// </para>
    /// <para>
    /// Spread bounds what a measurement can rule out, and refusing a range of zero is only
    /// the clearest case of that. Across a range <c>r</c> with a tolerance <c>t</c>, a scale
    /// error smaller than <c>t / r</c> produces less displacement than the tolerance and is
    /// reported as no fault. Samples close together therefore rule out large scale errors
    /// only. Far apart is not a stylistic preference.
    /// </para>
    /// </summary>
    /// <param name="samples">Expected and actual pixel positions, in any order.</param>
    /// <param name="tolerance">
    /// How large a term must be to count. The default allows for the roughly quarter-pixel
    /// difference between an antialiased dot's geometric centre and its measured one.
    /// </param>
    public static RegistrationFault Read(
        IReadOnlyList<((double X, double Y) Expected, (double X, double Y) Actual)> samples,
        double tolerance = 0.6)
    {
        var distinct = samples
            .Select(sample => (Math.Round(sample.Expected.X, 6), Math.Round(sample.Expected.Y, 6)))
            .Distinct()
            .Count();

        if (distinct < 2) return RegistrationFault.NotEnoughPositions;

        // Distinct positions is not enough: they have to be distinct on each axis separately.
        // Two samples at (10, 10) and (100, 10) are two distinct positions and say nothing
        // whatever about the vertical, because every scale through a single row can be
        // matched by a translation that puts the row in the same place. Asked about a pure
        // vertical scale of two, measured along one row, this used to answer Origin -- a
        // confident name for a fault it had no way to see.
        var across = Spread(samples.Select(sample => sample.Expected.X));
        var down = Spread(samples.Select(sample => sample.Expected.Y));

        if (across <= Nothing || down <= Nothing) return RegistrationFault.NotEnoughSpread;

        var horizontal = Fit(samples.Select(s => (s.Expected.X, s.Actual.X - s.Expected.X)).ToList());
        var vertical = Fit(samples.Select(s => (s.Expected.Y, s.Actual.Y - s.Expected.Y)).ToList());

        var shifted = Math.Abs(horizontal.Intercept) > tolerance
                      || Math.Abs(vertical.Intercept) > tolerance;

        // The slope is reported as the displacement it produces across the range actually
        // measured. A slope is not a length, and comparing one to a pixel tolerance would
        // make the verdict depend on how far apart the samples happened to be.
        var grows = Math.Abs(horizontal.Growth) > tolerance || Math.Abs(vertical.Growth) > tolerance;

        return (shifted, grows) switch
        {
            (false, false) => RegistrationFault.Registered,
            (true, false) => RegistrationFault.Origin,
            (false, true) => RegistrationFault.Scale,
            (true, true) => RegistrationFault.OriginAndScale,
        };
    }

    /// <summary>A range below this is no range at all, allowing for the arithmetic.</summary>
    private const double Nothing = 1e-6;

    private static double Spread(IEnumerable<double> positions)
    {
        var all = positions.ToList();

        return all.Max() - all.Min();
    }

    /// <summary>
    /// Least squares through (position, displacement), returning the displacement at the
    /// origin and the displacement the slope accumulates across the measured range.
    /// </summary>
    private static (double Intercept, double Growth) Fit(IReadOnlyList<(double At, double Displaced)> points)
    {
        var meanAt = points.Average(point => point.At);
        var meanDisplaced = points.Average(point => point.Displaced);

        var spread = points.Sum(point => Math.Pow(point.At - meanAt, 2));

        // Unreachable from Read, which refuses an axis with no spread before it gets here.
        // Kept so that the arithmetic below cannot divide by zero if this is called directly.
        if (spread == 0) return (meanDisplaced, 0);

        var slope = points.Sum(point => (point.At - meanAt) * (point.Displaced - meanDisplaced)) / spread;
        var intercept = meanDisplaced - slope * meanAt;

        var range = points.Max(point => point.At) - points.Min(point => point.At);

        return (intercept, slope * range);
    }
}
