using SkiaSharp;
using StrokeKit.Brushes;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;

namespace StrokeKit.Tests;

/// <summary>
/// The contracts the live drawing path rests on, which were unstated until now.
/// </summary>
/// <remarks>
/// Reported on <c>#84</c>. <see cref="Stroke"/> keeps the list it is given rather than
/// copying it, <c>LivePen</c> relies on exactly that every reading, and <see cref="Wet"/>
/// requires each stroke it is handed to begin with the last one — none of which was said
/// anywhere or checked.
/// </remarks>
public class LiveContracts
{
    private static Reading At(double x, uint pressure = 600) =>
        Synthetic.Reading(x, 100, pressure);

    [Fact]
    public void A_stroke_follows_the_list_it_was_given()
    {
        // Not a defect: it is what makes drawing a growing stroke affordable, since copying
        // per reading would be quadratic over the stroke. It is only a defect when nobody
        // says so, which is why it is written down here as a property rather than fixed.
        var readings = new List<Reading> { At(10), At(20) };
        var stroke = new Stroke(readings);

        Assert.Equal(2, stroke.Count);

        readings.Add(At(30));

        Assert.Equal(3, stroke.Count);
    }

    [Fact]
    public void A_finished_stroke_does_not()
    {
        var readings = new List<Reading> { At(10), At(20) };
        var stroke = Stroke.Finished(readings);

        readings.Add(At(30));

        Assert.Equal(2, stroke.Count);
        Assert.Equal(20, stroke.Last.X);
    }

    [Fact]
    public void A_stroke_being_drawn_may_grow_and_may_not_shrink()
    {
        using var art = Surface.Create(240, 240, 1);

        art.Canvas.Clear(SKColors.Transparent);

        var brush = new Brush(10, SKColors.Black, 4);

        using var wet = new Wet(brush, art, InkTransform.For(art));

        var readings = new List<Reading> { At(20), At(40) };

        wet.Extend(new Stroke(readings));

        readings.Add(At(60));

        wet.Extend(new Stroke(readings));

        // Handed something shorter, it lays only what it has not seen -- from a position the
        // path no longer reaches. Refused rather than drawn wrong, because a caller doing
        // this has lost track of which stroke it is drawing.
        var shorter = new Stroke([At(20)]);

        Assert.Throws<ArgumentException>(() => wet.Extend(shorter));
    }

    [Fact]
    public void The_stamps_of_a_growing_stroke_are_still_a_prefix_after_all_that()
    {
        // The property the whole arrangement rests on, restated here because the contract
        // above is what protects it rather than a separate concern.
        using var art = Surface.Create(240, 240, 1);

        art.Canvas.Clear(SKColors.Transparent);

        var brush = new Brush(10, SKColors.Black, 4);
        var readings = new List<Reading>();

        for (var each = 0; each < 20; each++) readings.Add(At(20 + each * 8));

        var finished = brush.Positions(new Stroke(readings));

        using var wet = new Wet(brush, art, InkTransform.For(art));

        var growing = new List<Reading>();
        var laid = 0;

        foreach (var reading in readings)
        {
            growing.Add(reading);

            laid += wet.Extend(new Stroke(growing));
        }

        Assert.Equal(finished.Count, laid);
    }
}
