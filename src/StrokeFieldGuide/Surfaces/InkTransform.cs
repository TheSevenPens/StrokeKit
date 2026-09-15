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
    /// Two positions are still the minimum, and they must be genuinely distinct. With two
    /// the fit is exact and any measurement noise goes straight into both terms, which is
    /// why the tolerance has to exceed the noise in the measurement rather than being as
    /// small as arithmetic allows.
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

    /// <summary>
    /// Least squares through (position, displacement), returning the displacement at the
    /// origin and the displacement the slope accumulates across the measured range.
    /// </summary>
    private static (double Intercept, double Growth) Fit(IReadOnlyList<(double At, double Displaced)> points)
    {
        var meanAt = points.Average(point => point.At);
        var meanDisplaced = points.Average(point => point.Displaced);

        var spread = points.Sum(point => Math.Pow(point.At - meanAt, 2));

        // Every sample at the same place on this axis. The axis says nothing about a slope,
        // and the mean displacement is the whole of what it does say.
        if (spread == 0) return (meanDisplaced, 0);

        var slope = points.Sum(point => (point.At - meanAt) * (point.Displaced - meanDisplaced)) / spread;
        var intercept = meanDisplaced - slope * meanAt;

        var range = points.Max(point => point.At) - points.Min(point => point.At);

        return (intercept, slope * range);
    }
}
