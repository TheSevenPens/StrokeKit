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

    /// <summary>Every preset, for a check that wants to state something about all of them.</summary>
    public static IReadOnlyList<Preset> All => [RoundDabs];
}
