namespace StrokeKit.Views;

/// <summary>
/// What a scroll bar should show, given a pan.
/// <para>
/// A scroll bar is a second way of saying what the pan already says, and the two have to
/// agree or the bar lies about where the reader is. Working the numbers out here rather
/// than in the control is what lets them be checked.
/// </para>
/// </summary>
public static class Scrolling
{
    /// <summary>
    /// One scroll bar's numbers, in display pixels.
    /// <para>
    /// <see cref="Value"/> is how far into the surface the viewport's left or top edge sits,
    /// which is the negative of the pan: panning the surface right moves the viewport left
    /// over it.
    /// </para>
    /// </summary>
    public readonly record struct Bar(double Minimum, double Maximum, double Value, double Viewport)
    {
        /// <summary>Whether there is anything to scroll. False when the surface fits.</summary>
        public bool Needed => Maximum > Minimum;
    }

    /// <summary>
    /// The bar for one axis: how much surface there is at the current zoom, how much viewport
    /// there is to show it in, and where the pan currently is.
    /// <para>
    /// Two cases, and the second is the one that is usually got wrong.
    /// </para>
    /// <para>
    /// When the surface is <b>larger</b> than the viewport, the bar runs from 0 to the part
    /// the viewport cannot show -- the content minus the viewport, not the content. A bar
    /// whose maximum is the content's own size lets the reader scroll the surface entirely
    /// out of the window and puts the thumb at the wrong place for every position but the
    /// first.
    /// </para>
    /// <para>
    /// When the surface is <b>smaller</b>, there is nothing to scroll and the bar says so.
    /// The pan is still free to be anything -- it is usually centring the surface, which is a
    /// negative value -- so the range is pinned to wherever the pan is rather than to zero.
    /// A bar cannot show a value outside its own range, and one that tried would snap the
    /// pan.
    /// </para>
    /// <para>
    /// The range is also widened to include the current value if a pan has taken the surface
    /// beyond what the bar would otherwise cover. That happens after a drag, which is not
    /// obliged to stop where a scroll bar would.
    /// </para>
    /// </summary>
    public static Bar For(double contentPixels, double viewportPixels, double pan)
    {
        var value = -pan;

        if (contentPixels <= viewportPixels) return new Bar(value, value, value, viewportPixels);

        return new Bar(
            Math.Min(0, value),
            Math.Max(contentPixels - viewportPixels, value),
            value,
            viewportPixels);
    }

    /// <summary>The pan a bar's value means. The inverse of <see cref="Bar.Value"/>.</summary>
    public static double PanFor(double value) => -value;
}
