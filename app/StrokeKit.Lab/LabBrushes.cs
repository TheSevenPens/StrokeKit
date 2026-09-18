using SkiaSharp;
using StrokeKit.Brushes;

namespace StrokeKit.Lab;

/// <summary>A brush the lab offers, and what it is here to show.</summary>
public sealed record LabBrush(string Name, string Shows, Brush Brush)
{
    public override string ToString() => Name;
}

/// <summary>
/// The brushes this application draws with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own, and deliberately not the guide's.</b> These began as variants of
/// <c>StrokeFieldGuide.Brushes.Presets</c>, which the lab used while it lived in that
/// repository. Those are the brushes the book <em>measures against</em> — its own
/// documentation calls a preset "a fixture, not a claim" — and a kit cannot reference the book
/// that documents it, so they stayed where they were and these came instead.
/// </para>
/// <para>
/// The difference is not only bookkeeping. A fixture has to hold still, because pages assert
/// things about it. These are free to change whenever a better demonstration turns up, which
/// is what a demonstration should be able to do.
/// </para>
/// <para>
/// <b>Two of the four are drawn with an engine this application cannot draw live</b>, and that
/// is on purpose. <c>Wet</c> refuses anything but <see cref="Engine.Stamps"/>, so the pen
/// offers only the stamping ones and says so. Keeping a taper here means the refusal is
/// visible in the application rather than only in a constructor.
/// </para>
/// </remarks>
public static class LabBrushes
{
    /// <summary>The device range the responses are written against.</summary>
    /// <remarks>
    /// A brush carries the range it was authored for and <c>Brush.Ranged</c> adapts it to
    /// whatever device opens, so a brush written against 1024 draws the same on a tablet
    /// reporting 32767. Without that a preset handed a real pen drew at a thirty-second of the
    /// pressure applied, quietly.
    /// </remarks>
    public const uint Range = 1024;

    /// <summary>The narrowest a width may go, so a light touch still marks.</summary>
    public const double Thinnest = 0.25;

    /// <summary>
    /// Stamps close enough together to read as one stroke, with width and ink on separate
    /// curves.
    /// </summary>
    public static LabBrush RoundDabs => new(
        "round dabs",
        "size and ink driven from the same pen through different curves, so the width comes "
        + "on early and the ink holds back",
        new Brush(
            36, SKColors.Black, 0.1, Buildup: Buildup.OncePerStroke,
            Width: new Width(Thinnest, 36, Range, new Response(0.02, 1.0, 0.75)),
            SpacedBy: SpacedBy.Diameters,
            Flow: new Flow(0.15, 1.0, Range, new Response(0.02, 1.0, 1.4))));

    /// <summary>The same engine with the spacing walked apart, so the marks read separately.</summary>
    public static LabBrush Beads => new(
        "beads",
        "spacing measured in diameters rather than pixels, which is what makes the setting "
        + "visible rather than merely different",
        new Brush(
            28, SKColors.Black, 1.0, Buildup: Buildup.OncePerStroke,
            // A floor at a third of the size, so a bead never shrinks to nothing however
            // lightly the pen is used.
            Width: new Width(0.35 * 28, 28, Range, new Response(0, 1, 0.7)),
            SpacedBy: SpacedBy.Diameters));

    /// <summary>A hard nib laid as one swept outline rather than as marks.</summary>
    /// <remarks>
    /// Here to be refused by the live pen, and drawable from the replay button, which goes
    /// through the batch path.
    /// </remarks>
    public static LabBrush InkPen => new(
        "ink pen",
        "an outlined stroke rather than a stamped one — and one this application cannot draw "
        + "while the pen is down",
        new Brush(
            18, SKColors.Black,
            // Unused by a taper, which has no marks to space. Required all the same: spacing
            // is a property of the brush, not of whichever engine reads it.
            1,
            Buildup: Buildup.PerStamp,
            Width: new Width(Thinnest, 18, Range, new Response(0, 0.85, 1.4)),
            SpacedBy: SpacedBy.Distance, Flow: null, Engine: Engine.Taper));

    /// <summary>A constant-width nib whose ink follows the pen, with a ceiling on it.</summary>
    public static LabBrush Marker => new(
        "marker",
        "a ceiling on how much ink a stroke can reach however many times it crosses itself, "
        + "which only holds because the stroke meets the surface once",
        new Brush(
            42, SKColors.Black, 1,
            Buildup: Buildup.OncePerStroke,
            // Nothing drives the width. Said out loud because pressure driving size is the
            // ordinary case and this brush is defined by not doing it.
            Width: null,
            SpacedBy: SpacedBy.Distance,
            Flow: new Flow(0.007, 0.35, Range, new Response(0.05, 0.7, 1)),
            Engine: Engine.Taper));

    /// <summary>A taper cut at the readings, which is the one outline shape drawable live.</summary>
    /// <remarks>
    /// The same silhouette family as <see cref="InkPen"/> and a different policy underneath:
    /// no global cut spacing and no merging of equal-coloured runs, so a stroke drawn as it
    /// arrives makes the same mark as the same stroke drawn in one call. What it gives up is
    /// that the mark depends on how often the tablet reported — draw the same shape slowly and
    /// quickly and the silhouettes differ.
    /// </remarks>
    public static LabBrush SampleTaper => new(
        "sample taper",
        "an outline drawn live — one taper per reading, so the mark follows the report rate",
        new Brush(
            20, SKColors.Black, 1,
            Buildup: Buildup.PerStamp,
            Width: new Width(Thinnest, 20, Range, new Response(0, 0.9, 1.2)),
            SpacedBy: SpacedBy.Distance, Flow: null, Engine: Engine.SampleTaper));

    /// <summary>All of them, in the order the application offers them.</summary>
    public static IReadOnlyList<LabBrush> All => [RoundDabs, Beads, SampleTaper, InkPen, Marker];
}
