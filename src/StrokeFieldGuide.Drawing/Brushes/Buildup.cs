using SkiaSharp;

namespace StrokeFieldGuide.Brushes;

/// <summary>
/// When a stroke's stamps meet the surface: one at a time, or once as a stroke.
/// </summary>
public enum Buildup
{
    /// <summary>
    /// Each stamp composited onto the surface as it is laid.
    /// <para>
    /// The stroke darkens itself, by a factor its spacing sets. Simple, and the behaviour
    /// overlap-within-a-stroke is about.
    /// </para>
    /// </summary>
    PerStamp,

    /// <summary>
    /// The stroke's own coverage built first, then composited onto the surface once.
    /// <para>
    /// The stroke takes its stamp's alpha wherever it is covered at all, whatever the
    /// spacing and however often it crosses itself.
    /// </para>
    /// </summary>
    OncePerStroke,
}

/// <summary>
/// Overlapping marks taking the <b>greater</b> of two alphas rather than their sum.
/// <para>
/// This is what lets a stroke be built out of many stamps without darkening itself. Krita
/// calls it alpha-darken and paints a stroke's dabs into a temporary device with it, merging
/// that device onto the layer once.
/// </para>
/// <para>
/// <b>Skia has no such blend mode.</b> Two built-in modes look like one for a moment --
/// <c>Src</c> and <c>DstATop</c> both give the right answer when the two alphas are equal --
/// and neither is a maximum: at 200 then 80 they give 80, and at 80 then 200 they give 200.
/// Last writer wins. Order independence is what separates the two, and it is the property a
/// stroke needs, because the order stamps are laid in is an accident of which end the reader
/// started from.
/// </para>
/// <para>
/// What Skia does have is runtime blenders, and alpha-darken is four lines of SkSL. That this
/// works is measured rather than assumed, here and in PenDynamicsLab, whose
/// <c>AlphaDarkenBlenderTests</c> pinned it first.
/// </para>
/// </summary>
public static class AlphaDarken
{
    /// <summary>
    /// The alpha is the greater of the two. The colour is whichever source has coverage,
    /// un-premultiplied and re-premultiplied at the new alpha -- which for a stroke drawn in
    /// one colour is that colour, and the reason this stays this short.
    /// </summary>
    public const string Sksl =
        """
        half4 main(half4 src, half4 dst) {
            half a = max(src.a, dst.a);
            half3 c = src.a > 0.0 ? src.rgb / src.a
                    : (dst.a > 0.0 ? dst.rgb / dst.a : half3(0.0));
            return half4(c * a, a);
        }
        """;

    /// <summary>
    /// A blender that composites by the maximum of the alphas.
    /// <para>
    /// Compiled on every call, because a blender is cheap to make and a cached one is a
    /// lifetime question this guide has no reason to answer yet. The caller disposes it.
    /// </para>
    /// </summary>
    public static SKBlender Blender()
    {
        using var effect = SKRuntimeEffect.CreateBlender(Sksl, out var errors)
            ?? throw new InvalidOperationException($"alpha-darken did not compile: {errors}");

        return effect.ToBlender();
    }
}
