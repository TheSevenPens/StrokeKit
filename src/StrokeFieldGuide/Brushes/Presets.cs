using SkiaSharp;

namespace StrokeFieldGuide.Brushes;

/// <summary>
/// A named brush, and the reason it is worth having one.
/// </summary>
/// <param name="Exposes">
/// What this preset does that no other one here does. Required for the same reason a stroke
/// fixture states one: a preset that cannot say it is a preset somebody liked the look of, and
/// whether a mark looks good is not something this guide can check.
/// </param>
public sealed record Preset(string Name, string Exposes, Brush Brush)
{
    public override string ToString() => Name;
}

/// <summary>
/// The brushes this guide measures against, named, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <b>A preset is a fixture, not a claim.</b> Whether "round dabs" is a brush anyone wants is
/// a matter of taste and there is no check for it. What is checkable is that the settings it
/// advertises reach the pixels: disable one and the thing it names has to move.
/// </para>
/// <para>
/// These come from PenDynamicsPaint's opening set, and they are <b>variants rather than
/// replicas</b>. That application's presets are drawn through stroke smoothing and a fitted
/// path, neither of which is here yet, and its engine floors the width and the spacing where
/// this one refuses them. A mark drawn here and there will not be the same mark.
/// </para>
/// </remarks>
public static class Presets
{
    /// <summary>
    /// The device scale these presets are written against.
    /// </summary>
    /// <remarks>
    /// One range for the whole brush, not one per property: size and ink reading the same pen
    /// against different scales is a brush that cannot be reasoned about, which is
    /// <c>brush-control-contract</c>'s first section. A real device's is Part IV's.
    /// </remarks>
    public const uint Range = 1024;

    /// <summary>The floor under a stamp's diameter, in the stroke's own units.</summary>
    /// <remarks>
    /// <para>
    /// <b>On the preset, not in the engine.</b> A size response with a threshold reaches zero
    /// at a pressure the pen is genuinely in contact at, and under spacing in diameters a
    /// diameter of zero is a gap of zero, which this engine refuses rather than floors.
    /// </para>
    /// <para>
    /// The application these came from floors it at 0.25 DIP inside the engine, and is right
    /// to: it has a person at a slider and no way to ask them. Here the number is on the brush
    /// where it can be seen, which is the difference between a policy and a constant.
    /// </para>
    /// </remarks>
    public const double Thinnest = 0.25;

    /// <summary>
    /// Stamped marks close enough together to read as a continuous stroke, with pressure
    /// driving the width and the ink on curves of their own.
    /// </summary>
    public static Preset RoundDabs => new(
        "round dabs",
        "size and ink driven by the same pen through different curves, so the width comes on "
        + "early and the ink holds back -- which one shared curve could not say",
        new Brush(
            36, SKColors.Black, 0.1, Buildup.OncePerStroke,
            new Width(Thinnest, 36, Range, new Response(0.02, 1.0, 0.75)),
            SpacedBy.Diameters,
            new Flow(0.15, 1.0, Range, new Response(0.02, 1.0, 1.4))));

    /// <summary>
    /// A hard nib whose width follows the pen and whose ink does not, laid as one swept
    /// outline rather than as marks.
    /// </summary>
    public static Preset InkPen => new(
        "ink pen",
        "an outlined stroke, and the only preset whose curve reaches full width before full "
        + "pressure -- so the nib bottoms out under the hand rather than under the sensor",
        new Brush(
            18, SKColors.Black,
            // Unused: an outlining engine has no marks to space. Required all the same,
            // because a spacing is a property of the brush and not of whichever engine is
            // reading it today.
            1,
            // PerStamp, where the application this came from uses its equivalent of
            // OncePerStroke. On an opaque brush the two are indistinguishable -- overlapping
            // opaque marks composite to the same colour -- and OncePerStroke costs a surface
            // the size of the document for the length of the stroke. Measured at 256 MiB on
            // an 8192-square document, which is a great deal to spend on no difference.
            Buildup.PerStamp,
            new Width(Thinnest, 18, Range, new Response(0, 0.85, 1.4)),
            SpacedBy.Distance, null, Engine.Taper));

    /// <summary>
    /// A constant-width nib whose ink follows the pen, with a ceiling on how much of it the
    /// stroke can reach however many times it crosses itself.
    /// </summary>
    public static Preset Marker => new(
        "marker",
        "the only preset with a ceiling on its ink, and the only one whose buildup is load "
        + "bearing rather than a preference -- laid per stamp the same brush goes past it",
        new Brush(
            42, SKColors.Black, 1,
            // Not a preference. The ceiling is only a ceiling because the stroke meets the
            // surface once: laid per stamp, the marks accumulate and pass it.
            Buildup.OncePerStroke,
            // Nothing drives the width. Said out loud because pressure driving size is the
            // ordinary case and this brush is defined by not doing it.
            null,
            SpacedBy.Distance,
            new Flow(0.007, 0.35, Range, new Response(0.05, 0.7, 1)),
            Engine.Taper));

    /// <summary>
    /// The same engine as round dabs with the spacing walked apart, so the marks read as
    /// beads rather than as a stroke.
    /// </summary>
    public static Preset Beads => new(
        "beads",
        "spacing at a whole diameter, which is what makes spacing in diameters visible rather "
        + "than merely different -- and the only preset whose size keeps a floor no curve "
        + "could express",
        new Brush(
            28, SKColors.Black, 1.0, Buildup.OncePerStroke,
            // The floor is a third of the size, so the beads never shrink to nothing however
            // lightly the pen is used. On the property rather than on the input, which is why
            // it stays a third however many inputs there come to be.
            new Width(0.35 * 28, 28, Range, new Response(0, 1, 0.7)),
            SpacedBy.Diameters));

    /// <summary>Every preset, for a check that wants to state something about all of them.</summary>
    public static IReadOnlyList<Preset> All => [InkPen, Marker, RoundDabs, Beads];
}
