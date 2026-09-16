namespace StrokeFieldGuide.Strokes;

/// <summary>
/// One reading of the pen, in the units the ink is laid out in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a device position.</b> Where the pen was on the desktop, in which coordinate space,
/// at what scaling, through which API, is a question for the code that opened the session.
/// By the time a reading reaches a stroke that question is answered, and what is left is a
/// position in the drawing -- which an <see cref="Surfaces.InkTransform"/> puts on a surface.
/// </para>
/// <para>
/// This type exists because the distinction was previously kept by convention and nothing
/// else. A stroke held <c>WinPenKit.PenPoint</c>, whose coordinates are documented as
/// physical screen pixels, and the brush engine read those coordinates and drew with them.
/// That is correct exactly when the drawing sits at the top left of the window at a zoom of
/// one, and silently wrong otherwise -- which is the fault
/// <see cref="Surfaces.RegistrationFault.Origin"/> names, arrived at from the input side
/// rather than the output side.
/// </para>
/// <para>
/// A <c>default</c> reading is at the origin, <b>not in contact</b> and upright. Both are the
/// honest reading of an absent value: a reading nobody supplied is not a pen pressed at full
/// force, and it is not a pen lying on its side. <see cref="Twist"/> is the exception and is
/// documented as one -- its zero is a real orientation rather than an absence.
/// </para>
/// <para>
/// How the pen was held is kept as a <b>lean and a direction</b> rather than as a tilt in x
/// and a tilt in y. They carry the same two degrees of freedom, and this pair is the one the
/// rest of the guide asks for: a nib's angle is the direction, a nib's elongation follows the
/// lean, and neither needs an arctangent or a sign convention to get at. The sign conventions
/// are also not uniform -- on the tablet measured here a lean to the right reports a
/// <em>negative</em> x -- so a guide storing tilts would be storing something it has to
/// qualify per device.
/// </para>
/// </remarks>
/// <param name="X">Across, in the ink's units.</param>
/// <param name="Y">Down, in the ink's units.</param>
/// <param name="Pressure">
/// A raw count, and meaningless without the device's full scale, which a reading does not
/// carry and cannot: two devices reporting 512 are not reporting the same force. Zero is out
/// of contact.
/// </param>
/// <param name="At">Microseconds, on whatever clock the session anchored to.</param>
/// <param name="Lean">
/// Degrees away from vertical. Zero is upright, and about 60 is as far as an EMR pen can
/// sense -- manufacturers specify plus or minus 60 and a Cintiq 24 was measured on
/// 16 Sep 2026 reaching 64.
/// </param>
/// <param name="Azimuth">
/// Which way the pen is leaning, in degrees, 0 to 359. <b>Meaningless when
/// <paramref name="Lean"/> is zero</b>: a pen standing straight up is not leaning in any
/// direction, and the number a device reports there says nothing.
/// <para>
/// This is the angle a nib wants. A nib turns because the hand turns, and the direction the
/// hand has turned the pen is exactly this -- so orienting a nib from it is one field rather
/// than an arctangent over a pair of tilts whose sign convention differs between devices.
/// It is stored in this form for that reason, rather than as the x and y tilts, which are
/// the same two degrees of freedom written down less usefully.
/// </para>
/// <para>
/// It wraps, as <paramref name="Twist"/> does, and for the same reason: 359 and 0 are one
/// degree apart and subtracting them says 359.
/// </para>
/// </param>
/// <param name="Twist">
/// Degrees of barrel rotation, absolute and wrapping: measured on a Cintiq 24 through Wintab
/// on 16 Sep 2026 as a full 0 to 359. So zero is an orientation the pen can actually be at,
/// not an absence, and this field cannot say whether the device reports rotation at all --
/// unlike <paramref name="Lean"/>, where upright and unreported coincide harmlessly.
/// </param>
public readonly record struct Reading(
    double X,
    double Y,
    uint Pressure,
    long At = 0,
    double Lean = 0,
    double Azimuth = 0,
    double Twist = 0)
{
    /// <summary>Whether the tip is down. Contact is non-zero pressure, which is a choice.</summary>
    /// <remarks>
    /// The choice, and what it costs at the edges of a real contact, is the subject of the
    /// points-to-stroke page. It is a property here so that the rule is written once rather
    /// than as a comparison against zero wherever a stroke is assembled.
    /// </remarks>
    public bool InContact => Pressure > 0;

    /// <summary>Whether this reading says anything about how the pen was held.</summary>
    /// <remarks>
    /// Read off the lean and not the azimuth, because an upright pen has no direction and a
    /// device is free to report any number for one. The corpus reports this per fixture,
    /// because a synthetic stroke carrying no tilt and a recorded one from a device that
    /// reports none are the same thing here and should not be described as though the pen
    /// were known to be upright.
    /// </remarks>
    public bool Tilted => Lean != 0;
}
