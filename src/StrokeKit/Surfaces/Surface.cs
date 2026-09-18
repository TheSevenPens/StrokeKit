using SkiaSharp;

namespace StrokeKit.Surfaces;

/// <summary>
/// A rectangle of pixels, and the logical size it is shown at.
/// <para>
/// Skia's vocabulary, kept deliberately: a <b>surface</b> owns pixels and a <b>canvas</b> is
/// what a drawing operation is issued to. That split is the reason this type exists separately from anything that
/// presents it, and the reason it knows nothing about windows, pens or strokes.
/// </para>
/// <para>
/// The two sizes are the whole point. <see cref="PixelWidth"/> is how many pixels there
/// are; <see cref="LogicalWidth"/> is how large the surface is in the coordinate system a
/// caller works in. On an unscaled display they are equal and every confusion between them
/// is invisible. At 1.75 they are not.
/// </para>
/// </summary>
public sealed class Surface : IDisposable
{
    private readonly SKSurface _surface;

    private Surface(SKSurface surface, int pixelWidth, int pixelHeight,
                    double logicalWidth, double logicalHeight)
    {
        _surface = surface;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
    }

    /// <summary>How many pixels the surface actually has across.</summary>
    public int PixelWidth { get; }

    public int PixelHeight { get; }

    /// <summary>How wide the surface is in the caller's units. Not necessarily a whole number.</summary>
    public double LogicalWidth { get; }

    public double LogicalHeight { get; }

    /// <summary>
    /// Pixels per logical unit, horizontally.
    /// <para>
    /// Two scales rather than one, and not because non-square pixels are common. A surface
    /// whose pixel dimensions were rounded to whole numbers from a fractional logical size
    /// has slightly different scales on the two axes, and a surface given the wrong size on
    /// one axis has very different ones. Collapsing them to a single number hides both, and
    /// the second is a fault with a recognisable signature: error growing along one axis
    /// and not the other.
    /// </para>
    /// </summary>
    public double ScaleX => LogicalWidth > 0 ? PixelWidth / LogicalWidth : 1;

    public double ScaleY => LogicalHeight > 0 ? PixelHeight / LogicalHeight : 1;

    /// <summary>The drawing interface. Holds the transform and the clip; owns no pixels.</summary>
    public SKCanvas Canvas => _surface.Canvas;

    /// <summary>
    /// How many times anything has asked this surface for its pixels.
    /// <para>
    /// A diagnostic, and the only reason it is here is that it makes a claim about cost into
    /// a claim that can be checked the same way every other claim in this guide is. Acquiring
    /// the pixels is not free, and code that does it once per pixel rather than once per
    /// frame is correct, looks reasonable, and runs a hundred times slower. Counting is
    /// deterministic; timing is a different number on every machine and would have to be
    /// given a threshold somebody would eventually have to loosen.
    /// </para>
    /// </summary>
    public long Reads { get; private set; }

    /// <summary>The one place the pixels are acquired, so that <see cref="Reads"/> is true.</summary>
    private SKPixmap Pixels()
    {
        Reads++;

        return _surface.PeekPixels()
            ?? throw new InvalidOperationException("the surface did not expose its pixels");
    }

    /// <summary>
    /// A surface of <paramref name="logicalWidth"/> by <paramref name="logicalHeight"/>,
    /// with enough pixels for <paramref name="scale"/>.
    /// <para>
    /// The pixel count is rounded, because a surface cannot have 1225.4 pixels. That
    /// rounding is why <see cref="ScaleX"/> is derived from the two numbers the surface
    /// actually has rather than stored from the argument: the scale you asked for and the
    /// scale you got are not always the same, and everything downstream must use the one
    /// you got.
    /// </para>
    /// </summary>
    public static Surface Create(double logicalWidth, double logicalHeight, double scale = 1)
    {
        if (!(logicalWidth > 0) || !(logicalHeight > 0))
            throw new ArgumentOutOfRangeException(nameof(logicalWidth), "a surface has a positive size");

        if (!(scale > 0))
            throw new ArgumentOutOfRangeException(nameof(scale), "a surface has a positive scale");

        var pixelWidth = (int)Math.Round(logicalWidth * scale);
        var pixelHeight = (int)Math.Round(logicalHeight * scale);

        return CreateExactly(pixelWidth, pixelHeight, logicalWidth, logicalHeight);
    }

    /// <summary>
    /// A surface with the pixel dimensions and logical size given independently.
    /// <para>
    /// Here so that a surface whose two sizes disagree can be constructed on purpose. That
    /// is the fault the first page of this guide is about, and a type that made it
    /// unrepresentable could not demonstrate it.
    /// </para>
    /// </summary>
    public static Surface CreateExactly(int pixelWidth, int pixelHeight,
                                        double logicalWidth, double logicalHeight)
    {
        // Premultiplied, because that is what Skia's raster surfaces use and what a
        // readback therefore returns. Saying so here rather than taking the default keeps
        // the pixel-values page honest about what a read value means.
        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"could not create a {pixelWidth}x{pixelHeight} surface");

        surface.Canvas.Clear(SKColors.Transparent);

        return new Surface(surface, pixelWidth, pixelHeight, logicalWidth, logicalHeight);
    }

    /// <summary>
    /// One pixel, by its pixel coordinates.
    /// <para>
    /// Pixel coordinates, never logical ones. Reading at a logical position on a scaled
    /// surface reads the wrong pixel, and it reads a real pixel rather than failing, so
    /// nothing reports the mistake.
    /// </para>
    /// <para>
    /// The surface stores premultiplied bytes and this read <b>un-premultiplies</b> them,
    /// so a red drawn at half alpha comes back with a red channel of 255 rather than the
    /// 128 the surface holds. <see cref="ReadStored"/> returns the stored bytes instead.
    /// </para>
    /// <para>
    /// The alpha is exact at every level. The recovered colour is not: measured on
    /// SkiaSharp 3.119.4, a red of 200 drawn at an alpha of 3 comes back as 170. Anything
    /// measuring faint coverage should measure the alpha.
    /// </para>
    /// </summary>
    public SKColor ReadPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= PixelWidth || y >= PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(x),
                $"({x}, {y}) is outside a {PixelWidth}x{PixelHeight} surface");
        }

        using var pixmap = Pixels();

        return pixmap.GetPixelColor(x, y);
    }

    /// <summary>
    /// One pixel exactly as the surface holds it: premultiplied bytes, no conversion.
    /// <para>
    /// The other read. <see cref="ReadPixel"/> un-premultiplies on the way out, so the two
    /// return different numbers for the same pixel and both are right about what they
    /// claim. Which one a check should use depends on what it is asserting, and the
    /// reading-pixels-back page says which is which.
    /// </para>
    /// </summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) ReadStored(int x, int y)
    {
        if (x < 0 || y < 0 || x >= PixelWidth || y >= PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(x),
                $"({x}, {y}) is outside a {PixelWidth}x{PixelHeight} surface");
        }

        using var pixmap = Pixels();

        var bytes = pixmap.GetPixelSpan();
        var offset = (y * PixelWidth + x) * 4;

        return (bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
    }

    /// <summary>
    /// An image of the pixels this surface holds.
    /// <para>
    /// One operation, and the reason to prefer it over assembling an image a pixel at a time
    /// is not tidiness. Every <see cref="ReadStored"/> call acquires and releases its own
    /// pixmap, so reading a 1000x1000 surface that way is a million of them: measured at 189
    /// milliseconds, which is five frames a second and is what a reader feels as a drag that
    /// will not keep up.
    /// </para>
    /// <para>
    /// The image carries what the surface holds, premultiplied, with no conversion in either
    /// direction. Drawing into the surface afterwards leaves the image as it was.
    /// </para>
    /// </summary>
    public SKImage Snapshot()
    {
        Reads++;

        return _surface.Snapshot();
    }

    /// <summary>
    /// Every pixel, as a copy, for a check that needs to scan rather than sample.
    /// </summary>
    public SKColor[] ReadAll()
    {
        using var pixmap = Pixels();

        var pixels = new SKColor[PixelWidth * PixelHeight];
        for (var y = 0; y < PixelHeight; y++)
        {
            for (var x = 0; x < PixelWidth; x++) pixels[y * PixelWidth + x] = pixmap.GetPixelColor(x, y);
        }

        return pixels;
    }

    /// <summary>
    /// The smallest rectangle containing every pixel with any ink in it, in pixel
    /// coordinates, with the right and bottom edges exclusive.
    /// <para>
    /// The measurement a size claim needs. Any alpha at all counts, so an antialiased edge
    /// widens the answer by up to a pixel on each side, and a check on a diameter has to
    /// leave room for that rather than demanding an exact figure.
    /// </para>
    /// <returns>Null when nothing was drawn, which is a different answer from an empty box.</returns>
    /// </summary>
    public (int Left, int Top, int Right, int Bottom)? InkBounds()
    {
        using var pixmap = Pixels();

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

        for (var y = 0; y < PixelHeight; y++)
        {
            for (var x = 0; x < PixelWidth; x++)
            {
                if (pixmap.GetPixelColor(x, y).Alpha == 0) continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return right == int.MinValue ? null : (left, top, right, bottom);
    }

    /// <summary>
    /// The strongest alpha anywhere on the surface.
    /// <para>
    /// Alpha rather than colour, because alpha is stored exactly and a recovered colour is
    /// not: see the page on reading pixels back.
    /// </para>
    /// </summary>
    public int PeakAlpha()
    {
        using var pixmap = Pixels();

        var peak = 0;
        for (var y = 0; y < PixelHeight; y++)
        {
            for (var x = 0; x < PixelWidth; x++) peak = Math.Max(peak, pixmap.GetPixelColor(x, y).Alpha);
        }

        return peak;
    }

    /// <summary>
    /// The centre of mass of everything drawn, in pixel coordinates, weighted by alpha.
    /// <para>
    /// The measurement the registration checks are built on. A single sampled pixel answers
    /// "is there ink here", which an antialiased edge makes into a judgement call; the
    /// weighted centre answers "where is the ink", to a fraction of a pixel, and is what
    /// makes a displacement of a quarter of a pixel visible to a check.
    /// </para>
    /// <returns>Null when nothing was drawn, which is a different answer from the centre.</returns>
    /// </summary>
    public (double X, double Y)? CentreOfInk()
    {
        using var pixmap = Pixels();

        double weight = 0, sumX = 0, sumY = 0;

        for (var y = 0; y < PixelHeight; y++)
        {
            for (var x = 0; x < PixelWidth; x++)
            {
                double alpha = pixmap.GetPixelColor(x, y).Alpha;
                if (alpha == 0) continue;

                // The centre of the pixel, not its corner. A pixel covers the square from
                // (x, y) to (x+1, y+1), so a mark filling exactly pixel 0 is centred at
                // 0.5. Using the corner puts every measured position half a pixel low and
                // left, which is a constant error and therefore the kind that survives a
                // check written against the same mistake.
                weight += alpha;
                sumX += alpha * (x + 0.5);
                sumY += alpha * (y + 0.5);
            }
        }

        return weight == 0 ? null : (sumX / weight, sumY / weight);
    }

    /// <summary>
    /// A surface at least this big, carrying this one's pixels at the origin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never smaller.</b> Content is preserved by drawing the old surface in at the
    /// origin, so allocating smaller discards whatever falls outside -- and growing back
    /// cannot recover it, because there is nothing left to recover it from. A window dragged
    /// narrower and widened again would come back with the mark cut off, and a resize drag
    /// does that on every step of the way.
    /// </para>
    /// <para>
    /// The cost is that growth is monotonic for the life of the surface: maximise and restore
    /// and it stays at the maximum. That is bounded by the largest size anybody asked for,
    /// which makes it a stated consequence rather than a leak -- and it is the trade
    /// <c>surface-lifetime</c> is about.
    /// </para>
    /// <para>
    /// Answers a new surface rather than resizing this one, so a caller cannot keep a
    /// reference to something that has silently changed size underneath it. This one is left
    /// to the caller to dispose.
    /// </para>
    /// </remarks>
    public Surface Grown(int pixelWidth, int pixelHeight, double logicalWidth, double logicalHeight)
    {
        var wide = Math.Max(pixelWidth, PixelWidth);
        var high = Math.Max(pixelHeight, PixelHeight);

        var grown = CreateExactly(wide, high,
            Math.Max(logicalWidth, LogicalWidth), Math.Max(logicalHeight, LogicalHeight));

        using var image = Snapshot();
        grown.Canvas.DrawImage(image, 0, 0);

        return grown;
    }

    /// <summary>
    /// The surface to draw on now that the host is <paramref name="logicalWidth"/> by
    /// <paramref name="logicalHeight"/> at <paramref name="scale"/>, which may be this one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Answers this surface when nothing needs to change</b>, so a caller compares by
    /// reference to know whether to hand the new one on and release the old:
    /// </para>
    /// <code>
    /// var next = art.Resized(width, height, scale);
    ///
    /// if (!ReferenceEquals(next, art)) { Show(next); art.Dispose(); art = next; }
    /// </code>
    /// <para>
    /// <b>A change of scale rebuilds and resamples.</b> A surface carries the scale it was made
    /// at, and one made at 2.25 and presented at 1.75 draws every mark long by the ratio —
    /// correct at the origin and drifting further out the further it goes. Moving a window
    /// between displays does it. The old content is redrawn at its <i>apparent</i> size rather
    /// than copied pixel for pixel, because it was put down in logical units and belongs where
    /// those units are.
    /// </para>
    /// <para>
    /// <b>A change of size at the same scale only ever grows.</b> Shrinking would throw away
    /// whatever is outside the smaller bounds, and it does not come back when the host grows
    /// again — so a host that gets smaller keeps the surface it had. What that costs is memory;
    /// what shrinking costs is a reader's work.
    /// </para>
    /// <para>
    /// This is what <see cref="Grown"/> does, plus the scale. Grown is still here because
    /// growing at a known pixel count is a different question from fitting a host, and
    /// <c>PenPad</c> asks the first one.
    /// </para>
    /// </remarks>
    public Surface Resized(double logicalWidth, double logicalHeight, double scale)
    {
        if (!(logicalWidth > 0) || !(logicalHeight > 0) || !(scale > 0) || double.IsNaN(scale))
        {
            return this;
        }

        if (!SameScaleAs(scale, logicalWidth, logicalHeight))
        {
            var moved = Create(logicalWidth, logicalHeight, scale);

            // At the size it looked, not the pixels it occupied.
            using var image = Snapshot();

            moved.Canvas.DrawImage(image, new SKRect(
                0, 0,
                (float)(PixelWidth * moved.ScaleX / ScaleX),
                (float)(PixelHeight * moved.ScaleY / ScaleY)));

            return moved;
        }

        var wide = (int)Math.Round(logicalWidth * scale);
        var high = (int)Math.Round(logicalHeight * scale);

        if (wide <= PixelWidth && high <= PixelHeight) return this;

        return Grown(wide, high, logicalWidth, logicalHeight);
    }

    /// <summary>Whether a wanted scale is the one this surface already has.</summary>
    /// <remarks>
    /// <para>
    /// Not an equality test, and it cannot be one. <see cref="ScaleX"/> is derived by dividing
    /// two numbers that were themselves arrived at by rounding, so a surface built for 2.25
    /// does not report exactly 2.25 and comparing them directly rebuilds on every frame.
    /// </para>
    /// <para>
    /// The tolerance is a pixel across the whole surface, which is the smallest difference that
    /// can move a mark at all. Anything finer is a rounding artefact and anything coarser could
    /// leave a visible drift standing.
    /// </para>
    /// </remarks>
    private bool SameScaleAs(double scale, double logicalWidth, double logicalHeight) =>
        Math.Abs(scale - ScaleX) * logicalWidth < 1
        && Math.Abs(scale - ScaleY) * logicalHeight < 1;

    public void Dispose() => _surface.Dispose();
}
