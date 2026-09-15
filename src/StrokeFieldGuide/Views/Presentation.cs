namespace StrokeFieldGuide.Views;

/// <summary>
/// The arithmetic a presenting control does around a <see cref="Presenter"/> call.
/// <para>
/// Here rather than in the control because none of it needs a window, and all of it can be
/// wrong. A control is the one place in an application that cannot be run without a screen,
/// so anything left inside one is a decision nobody can check. What remains in the control
/// after this is the part that genuinely needs Avalonia: asking for the scaling, allocating
/// the bitmap, and handing it over.
/// </para>
/// <para>
/// Everything a <see cref="View"/> holds is in physical display pixels. Everything a
/// windowing system says is in device independent units. These four functions are the whole
/// of the conversion between the two, which is why they are worth naming.
/// </para>
/// </summary>
public static class Presentation
{
    /// <summary>
    /// How many physical pixels a control of this size in device independent units occupies.
    /// <para>
    /// Rounded, because a bitmap cannot have 1001.25 pixels. That rounding is the reason
    /// <see cref="Destination"/> exists rather than being the control's own size.
    /// </para>
    /// </summary>
    public static (int Width, int Height) PixelSize(double dipWidth, double dipHeight, double scale) =>
        ((int)Math.Round(dipWidth * scale), (int)Math.Round(dipHeight * scale));

    /// <summary>
    /// The rectangle, in device independent units, that puts one bitmap pixel on one
    /// physical pixel.
    /// <para>
    /// The obvious thing to draw a presented bitmap into is the control's own bounds, and it
    /// is wrong by the rounding above. At a scaling of 2.25 a control 445 units wide is 1001
    /// pixels, and 445 units is 1001.25 of them: the windowing system is handed a bitmap a
    /// quarter of a pixel too narrow for its destination and resizes it by a factor of
    /// 1.00025. Nothing looks obviously wrong. Everything is very slightly soft, at every
    /// zoom, for reasons no amount of looking at the presenter will explain.
    /// </para>
    /// </summary>
    public static (double Width, double Height) Destination(int pixelWidth, int pixelHeight, double scale) =>
        (pixelWidth / scale, pixelHeight / scale);

    /// <summary>
    /// A pointer movement, in device independent units, as a movement of the pan, in
    /// physical pixels.
    /// <para>
    /// Left unconverted, the drawing moves by the pointer's distance in pixels rather than
    /// in units and so travels a fraction of the distance the pointer did. On this machine
    /// that fraction is 1/2.25, and the drag feels like dragging something heavy through
    /// treacle. It is exactly right on an unscaled display.
    /// </para>
    /// </summary>
    public static double PanFromDrag(double dipDelta, double scale) => dipDelta * scale;

    /// <summary>
    /// The view that keeps the surface point at the old viewport's middle at the new one's
    /// middle.
    /// <para>
    /// Needed because a pan is in physical display pixels and a viewport can change size or
    /// scaling underneath it. Moving a window to a monitor at a different scaling changes
    /// the number of pixels without changing the size in device independent units; resizing
    /// changes the size without changing the scaling. In both cases the same pan is a
    /// different place, so the drawing jumps and part of it leaves the window.
    /// </para>
    /// <para>
    /// Holding the middle is a choice rather than the only answer -- holding the top left
    /// would also be stable -- and it is the one that matches what a reader is looking at.
    /// </para>
    /// </summary>
    public static View KeepingCentre(View view, int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        var (surfaceX, surfaceY) = view.ToSurface(oldWidth / 2.0, oldHeight / 2.0);

        return view.PannedTo(
            newWidth / 2.0 - surfaceX * view.Zoom,
            newHeight / 2.0 - surfaceY * view.Zoom);
    }
}
