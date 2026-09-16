using SkiaSharp;
using StrokeFieldGuide.Strokes;
using StrokeFieldGuide.Surfaces;

namespace StrokeFieldGuide.Brushes;

/// <summary>
/// The other way of laying a stroke down: one filled outline swept between two round ends,
/// rather than a row of stamps.
/// </summary>
/// <remarks>
/// <para>
/// A stamping engine puts down discrete marks and everything expressive hangs off them --
/// texture, scatter, a tip that is not a circle. A taper has none of that and gets one thing
/// in return: <b>its overlaps are not overlaps.</b> The region between two circles is filled
/// once, as a single closed contour, so a translucent stroke has nothing to double.
/// </para>
/// <para>
/// That is the whole of what outlining buys, and <c>blend-modes</c> is where it matters: no
/// blend mode can be idempotent at an antialiased edge, because coverage is applied after the
/// blend. An engine laying N shapes has N sets of edges to creep; an engine laying one has
/// none.
/// </para>
/// </remarks>
public static class Taper
{
    /// <summary>
    /// How far the swept width may depart from the width the brush actually asks for, in the
    /// stroke's own units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A taper's sides are straight, so the width it sweeps runs <b>linearly</b> between the
    /// two radii it was given. A brush with a linear response asks for exactly that and the
    /// taper is exact. A brush with a curve does not, and the difference is invisible until
    /// somebody reports the same gesture at a different rate: with one reading in the middle
    /// the piecewise line follows the curve more closely than with none, so the mark would
    /// depend on the report rate rather than on the stroke.
    /// </para>
    /// <para>
    /// <c>brush-control-contract</c> refuses that, so the engine subdivides until the straight
    /// run is within this of the curve rather than accepting whatever the samples happened to
    /// give. A twentieth of a unit is well under a pixel at any scale this guide draws at.
    /// </para>
    /// </remarks>
    public const double Tolerance = 0.05;

    /// <summary>The most pieces one segment is cut into, whatever the tolerance says.</summary>
    /// <remarks>
    /// A response with a threshold in it steps rather than curves, and no amount of halving
    /// brings a step under a tolerance. The cap stops a brush like that hanging; the page
    /// says what it costs.
    /// </remarks>
    public const int MostPieces = 64;

    /// <summary>
    /// How far a piece's one alpha may sit from the alpha wanted along it, out of 255.
    /// </summary>
    /// <remarks>
    /// Its own number rather than <see cref="Tolerance"/>, which is in surface pixels and
    /// means nothing here. Two levels is below what a reader can see against a flat ground
    /// and well above the rounding in a premultiplied byte.
    /// </remarks>
    public const double AlphaTolerance = 2;

    /// <summary>Draws the stroke, and answers how many pieces that took.</summary>
    /// <remarks>
    /// The count is returned for the same reason the stamping engine returns one: it is the
    /// number a check can compare against arithmetic without reading a pixel.
    /// </remarks>
    public static int Draw(Surface surface, InkTransform transform, Brush brush, Stroke stroke) =>
        DrawPieces(surface, transform, brush, Pieces(brush, stroke));

    /// <summary>
    /// Draws pieces that have already been cut, and answers how many that took.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Pieces"/> so that where the path is cut and how the pieces
    /// reach the surface are two questions. A check that wants to cut differently -- at the
    /// readings, say -- can then do so without reimplementing the compositing as well.
    /// </remarks>
    public static int DrawPieces(
        Surface surface, InkTransform transform, Brush brush, IReadOnlyList<Piece> pieces)
    {
        if (brush.Buildup == Buildup.PerStamp)
        {
            FillRuns(surface, transform, pieces, null);

            return pieces.Count;
        }

        // The stroke's own surface, so that the pieces meet each other before they meet this
        // one. A taper has no overlaps within a piece; between pieces it has the same ones a
        // stamping engine has, and they double the same way.
        using var stroking = Surface.CreateExactly(
            surface.PixelWidth, surface.PixelHeight, surface.LogicalWidth, surface.LogicalHeight);

        using var blender = AlphaDarken.Blender();

        FillRuns(stroking, transform, pieces, blender);

        using var image = stroking.Snapshot();
        surface.Canvas.DrawImage(image, 0, 0);

        return pieces.Count;
    }

    /// <summary>One length of the stroke, with the width and colour at each of its ends.</summary>
    public readonly record struct Piece(
        double FromX, double FromY, double FromDiameter,
        double ToX, double ToY, double ToDiameter,
        SKColor Colour);

    /// <summary>
    /// The stroke cut into pieces short enough that a straight run of width is within
    /// <see cref="Tolerance"/> of the width the brush asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cut at fixed distances along the path, not at the readings.</b> Cutting at readings
    /// makes the pieces a property of how often the tablet reported: the same gesture sent
    /// twice as often is cut twice as finely, and the mark moves. That is the fault
    /// <c>stamp-spacing</c> exists to avoid for stamps, and it is the same fault here.
    /// </para>
    /// <para>
    /// A reading only forces a cut when the path actually <b>turns</b> there, because a turn
    /// is a real feature of the path and a reading in the middle of a straight run is not.
    /// So inserting a reading between two others adds no cut, and the pieces -- and the mark
    /// -- are unchanged.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Piece> Pieces(Brush brush, Stroke stroke)
    {
        var pieces = new List<Piece>();

        var along = new Along(stroke);

        // A dot: one reading, or a pen that pressed and never moved. The stamping engine lays
        // one stamp for either and this has to agree -- a stroke is a stroke whichever engine
        // draws it.
        if (along.Length == 0)
        {
            var still = new Placement(stroke.Points[0].X, stroke.Points[0].Y, 0, 0);
            var across = brush.DiameterAt(stroke, still);

            pieces.Add(new Piece(still.X, still.Y, across, still.X, still.Y, across,
                brush.ColourAt(stroke, still)));

            return pieces;
        }

        var cuts = CutsFor(brush, stroke, along);

        for (var index = 1; index < cuts.Count; index++)
        {
            var start = along.At(cuts[index - 1]);
            var end = along.At(cuts[index]);

            if (start.X == end.X && start.Y == end.Y) continue;

            pieces.Add(new Piece(
                start.X, start.Y, brush.DiameterAt(stroke, start),
                end.X, end.Y, brush.DiameterAt(stroke, end),
                // The colour of the piece's far end. A filled taper has one alpha, so there
                // is nothing to ramp across it -- which is why the pieces have to be short
                // rather than a reason they need not be.
                brush.ColourAt(stroke, end)));
        }

        return pieces;
    }

    /// <summary>
    /// Where the stroke is cut, as distances along it: every turn, and a fixed step between.
    /// </summary>
    private static IReadOnlyList<double> CutsFor(Brush brush, Stroke stroke, Along along)
    {
        var turns = along.Turns();

        var step = along.Length;

        while ((Worst(step) > Tolerance || WorstAlpha(step) > AlphaTolerance)
               && along.Length / step < MostPieces)
        {
            step /= 2;
        }

        var cuts = new List<double>(turns);

        // Where an attribute stops being a straight line between its neighbours. A turn is
        // the same idea for geometry, and leaving these out was the reason a pressure spike
        // between two readings could vanish: with the ends and the midpoint agreeing, halving
        // the step never found it however far it went.
        cuts.AddRange(Knots());

        for (var at = step; at < along.Length; at += step) cuts.Add(at);

        cuts.Sort();

        // Two cuts closer together than a rounding error are one cut.
        var kept = new List<double> { cuts[0] };

        foreach (var cut in cuts)
        {
            if (cut - kept[^1] > along.Length * 1e-12) kept.Add(cut);
        }

        return kept;

        double Worst(double spacing)
        {
            var worst = 0.0;

            for (var at = 0.0; at < along.Length; at += spacing)
            {
                var to = Math.Min(at + spacing, along.Length);

                var atStart = brush.DiameterAt(stroke, along.At(at));
                var atEnd = brush.DiameterAt(stroke, along.At(to));
                var atMiddle = brush.DiameterAt(stroke, along.At((at + to) / 2));

                worst = Math.Max(worst, Math.Abs((atStart + atEnd) / 2 - atMiddle));
            }

            return worst;
        }

        /// <summary>
        /// The worst alpha a piece would be drawn at, against the alpha wanted along it.
        /// </summary>
        /// <remarks>
        /// Measured against the far endpoint rather than against the average, because that
        /// is what a piece is filled with: a piece's width ramps from one end to the other
        /// and its colour does not. The two error measures are different for that reason and
        /// not by oversight.
        /// <para>
        /// Absent entirely until 16 September 2026, so a stroke whose ink varied and whose
        /// width did not was one piece at the ink of its far end -- a ramp from nothing to
        /// full drawn as full everywhere.
        /// </para>
        /// </remarks>
        double WorstAlpha(double spacing)
        {
            var worst = 0.0;

            for (var at = 0.0; at < along.Length; at += spacing)
            {
                var to = Math.Min(at + spacing, along.Length);

                var filled = brush.ColourAt(stroke, along.At(to)).Alpha;

                foreach (var sample in new[] { at, (at + to) / 2 })
                {
                    var wanted = brush.ColourAt(stroke, along.At(sample)).Alpha;

                    worst = Math.Max(worst, Math.Abs(filled - wanted));
                }
            }

            return worst;
        }

        /// <summary>
        /// The distances at which an attribute stops being linear between its neighbours.
        /// </summary>
        /// <remarks>
        /// Judged on what the attribute produces -- a diameter and an alpha -- rather than on
        /// the raw reading. A pen's pressure moves at every reading and most of that movement
        /// changes neither; testing the raw number would make every reading a cut and say
        /// nothing about whether the mark needed one.
        /// </remarks>
        IEnumerable<double> Knots()
        {
            for (var index = 1; index < stroke.Count - 1; index++)
            {
                var before = along.DistanceTo(index - 1);
                var here = along.DistanceTo(index);
                var after = along.DistanceTo(index + 1);

                var span = after - before;
                if (span <= 0) continue;

                var fraction = (here - before) / span;

                var straightDiameter = Between(
                    brush.DiameterAt(stroke, along.At(before)),
                    brush.DiameterAt(stroke, along.At(after)), fraction);

                var straightAlpha = Between(
                    brush.ColourAt(stroke, along.At(before)).Alpha,
                    brush.ColourAt(stroke, along.At(after)).Alpha, fraction);

                var diameter = brush.DiameterAt(stroke, along.At(here));
                var alpha = brush.ColourAt(stroke, along.At(here)).Alpha;

                if (Math.Abs(diameter - straightDiameter) > Tolerance
                    || Math.Abs(alpha - straightAlpha) > AlphaTolerance)
                {
                    yield return here;
                }
            }
        }

        static double Between(double from, double to, double fraction) =>
            from + (to - from) * fraction;
    }

    /// <summary>
    /// A path measured by distance, so a position along it can be asked for without knowing
    /// which readings it fell between.
    /// </summary>
    private sealed class Along
    {
        private readonly Stroke _stroke;
        private readonly double[] _upTo;

        public Along(Stroke stroke)
        {
            _stroke = stroke;
            _upTo = new double[stroke.Count];

            for (var index = 1; index < stroke.Count; index++)
            {
                var from = stroke.Points[index - 1];
                var to = stroke.Points[index];

                _upTo[index] = _upTo[index - 1]
                    + Math.Sqrt(
                        Math.Pow(to.X - from.X, 2)
                        + Math.Pow(to.Y - from.Y, 2));
            }
        }

        public double Length => _upTo[^1];

        /// <summary>Where a distance along the path falls, as a placement.</summary>
        public Placement At(double distance)
        {
            distance = Math.Clamp(distance, 0, Length);

            var segment = 0;
            while (segment + 2 < _stroke.Count && _upTo[segment + 1] < distance) segment++;

            var from = _stroke.Points[segment];
            var to = _stroke.Points[segment + 1];

            var span = _upTo[segment + 1] - _upTo[segment];
            var fraction = span > 0 ? Math.Clamp((distance - _upTo[segment]) / span, 0, 1) : 0;

            return new Placement(
                from.X + (to.X - from.X) * fraction,
                from.Y + (to.Y - from.Y) * fraction,
                segment, fraction);
        }

        /// <summary>
        /// The distances at which the path turns, with its two ends.
        /// </summary>
        /// <remarks>
        /// A reading that sits on the straight run between its neighbours is not a turn, and
        /// so is not a cut. That is the whole of what makes inserting one leave the mark
        /// alone.
        /// </remarks>
        /// <summary>How far along the path a given reading sits.</summary>
        public double DistanceTo(int index) => _upTo[index];

        public IReadOnlyList<double> Turns()
        {
            var turns = new List<double> { 0 };

            for (var index = 1; index < _stroke.Count - 1; index++)
            {
                // Past the readings that sit exactly where their neighbour did. A pen held
                // still reports them, so they arrive in real strokes, and taking the null
                // direction as "no turn here" threw away the corner on both sides of one:
                // (20,20) to (60,20) to (60,20) to (60,60) came out as a single diagonal.
                var before = Backwards(index);
                var after = Forwards(index);

                if (before is null || after is null) continue;

                // How far the direction moved, as the distance between two unit vectors.
                // The cross product alone is not enough: it is zero for a straight run and
                // zero again for an exact reversal, so a stroke that doubles back on itself
                // would be read as one long straight piece from its start to its start --
                // which draws nothing at all, and did.
                var turned = Math.Sqrt(
                    Math.Pow(after.Value.X - before.Value.X, 2)
                    + Math.Pow(after.Value.Y - before.Value.Y, 2));

                if (turned > 1e-9) turns.Add(_upTo[index]);
            }

            turns.Add(Length);

            return turns;
        }

        /// <summary>The last direction there was, looking back from this reading.</summary>
        private (double X, double Y)? Backwards(int index)
        {
            for (var from = index - 1; from >= 0; from--)
            {
                if (Direction(from, index) is { } direction) return direction;
            }

            return null;
        }

        /// <summary>The next direction there is, looking forward from this reading.</summary>
        private (double X, double Y)? Forwards(int index)
        {
            for (var to = index + 1; to < _stroke.Count; to++)
            {
                if (Direction(index, to) is { } direction) return direction;
            }

            return null;
        }

        private (double X, double Y)? Direction(int from, int to)
        {
            var a = _stroke.Points[from];
            var b = _stroke.Points[to];

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);

            return length == 0 ? null : (dx / length, dy / length);
        }
    }

    private static void FillRuns(
        Surface surface, InkTransform transform, IReadOnlyList<Piece> pieces, SKBlender? blender)
    {
        var at = 0;

        while (at < pieces.Count)
        {
            var colour = pieces[at].Colour;

            // Unioned rather than appended. Appending leaves the contours overlapping, and a
            // winding fill cancels two that run opposite ways -- a taper's arcs sweep the
            // other way round when the far end is the wider one, so a stroke that thickens
            // and then thins would have holes where the two met.
            var path = new SKPath { FillType = SKPathFillType.Winding };
            using var outline = new SKPath();

            while (at < pieces.Count && pieces[at].Colour == colour)
            {
                var piece = pieces[at];

                var (fromX, fromY) = transform.ToSurface(piece.FromX, piece.FromY);
                var (toX, toY) = transform.ToSurface(piece.ToX, piece.ToY);

                Outline(outline,
                    new SKPoint((float)fromX, (float)fromY),
                    (float)(piece.FromDiameter / 2 * transform.ScaleX),
                    new SKPoint((float)toX, (float)toY),
                    (float)(piece.ToDiameter / 2 * transform.ScaleX));

                path.AddPath(outline);

                at++;
            }

            using var paint = new SKPaint
            {
                Color = colour,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            if (blender is not null) paint.Blender = blender;

            surface.Canvas.DrawPath(path, paint);

            path.Dispose();
        }
    }

    /// <summary>
    /// The outline of two circles and the region swept between them, as one closed contour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One contour, not three overlapping shapes.</b> The obvious construction -- a quad
    /// between the tangent points plus a circle at each end -- is wrong the moment the brush
    /// is translucent, because the overlaps are painted twice and show as darker lozenges at
    /// every sample. A single filled path has no overlaps to double, and that is the whole
    /// reason to outline rather than stamp.
    /// </para>
    /// <para>
    /// The straight sides are the circles' external tangents. A tangent meets each radius at a
    /// right angle, so the tangent points sit on the normal to the centre line turned
    /// <b>back towards the wide end</b> by <c>asin((ra - rb) / d)</c>. Turning it the other way
    /// gives a shape that still closes and still looks like a taper, with sides that cut
    /// across the caps instead of meeting them.
    /// </para>
    /// </remarks>
    internal static void Outline(SKPath path, SKPoint a, float ra, SKPoint b, float rb)
    {
        path.Reset();

        ra = Math.Max(ra, 0.01f);
        rb = Math.Max(rb, 0.01f);

        float dx = b.X - a.X, dy = b.Y - a.Y;
        var d = MathF.Sqrt(dx * dx + dy * dy);

        // No tangents to compute: the centres coincide, or one circle swallows the other.
        if (d <= MathF.Abs(ra - rb) + 1e-4f)
        {
            if (ra >= rb) path.AddCircle(a.X, a.Y, ra);
            else path.AddCircle(b.X, b.Y, rb);

            return;
        }

        var phi = MathF.Atan2(dy, dx);
        var alpha = MathF.Asin(Math.Clamp((ra - rb) / d, -1f, 1f));

        var up = phi + MathF.PI / 2 - alpha;
        var down = phi - MathF.PI / 2 + alpha;

        var alphaDegrees = alpha * 180f / MathF.PI;

        path.MoveTo(a.X + ra * MathF.Cos(up), a.Y + ra * MathF.Sin(up));
        path.LineTo(b.X + rb * MathF.Cos(up), b.Y + rb * MathF.Sin(up));

        // Round the far end, then the near one, both the same way round so the contour stays
        // simple. Together they account for the 360 degrees the two caps share, and the wide
        // end takes the larger part of it.
        path.ArcTo(Bounds(b, rb), Degrees(up), -(180f - 2f * alphaDegrees), false);
        path.LineTo(a.X + ra * MathF.Cos(down), a.Y + ra * MathF.Sin(down));
        path.ArcTo(Bounds(a, ra), Degrees(down), -(180f + 2f * alphaDegrees), false);

        path.Close();

        static SKRect Bounds(SKPoint centre, float radius) =>
            new(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);

        static float Degrees(float radians) => radians * 180f / MathF.PI;
    }
}
