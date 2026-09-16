using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkiaSharp;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Lab.Tests;

/// <summary>
/// The step between the presenter and the screen: a surface built over somebody else's
/// buffer, and what survives the trip.
/// <para>
/// Channel order, the alpha channel, and the row stride are three separate ways for this to
/// be wrong, and all three produce a picture rather than an exception.
/// </para>
/// </summary>
public class TheBitmapBridge
{
    private const byte Half = 128;

    private static readonly SKColor Red = new(0xE0, 0x20, 0x20);
    private static readonly SKColor Blue = new(0x20, 0x20, 0xE0);

    /// <summary>Red, blue, and red at half alpha, on an otherwise transparent surface.</summary>
    private static void Swatches(Surface art)
    {
        art.Canvas.Clear(SKColors.Transparent);

        using var opaque = new SKPaint { Color = Red, IsAntialias = false };
        art.Canvas.DrawRect(SKRect.Create(10, 10, 20, 20), opaque);

        using var other = new SKPaint { Color = Blue, IsAntialias = false };
        art.Canvas.DrawRect(SKRect.Create(40, 10, 20, 20), other);

        using var faint = new SKPaint { Color = Red.WithAlpha(Half), IsAntialias = false };
        art.Canvas.DrawRect(SKRect.Create(70, 10, 20, 20), faint);
    }

    [AvaloniaFact]
    public void Red_blue_and_half_alpha_all_arrive_on_the_screen()
    {
        var (window, canvas) = Headless.Open();

        Swatches(canvas.Surface);
        canvas.SetView(View.At(1, 0, 0));
        Dispatcher.UIThread.RunJobs();

        using var captured = window.CaptureRenderedFrame()!;
        var frame = new Frame(captured);
        var (originX, originY) = Frame.Origin(canvas, window, 1);

        (byte R, byte G, byte B, byte A) At(int x, int y) => frame[originX + x, originY + y];

        // Distinct channels, so a swapped order is not a near miss.
        var red = At(20, 20);
        var blue = At(50, 20);

        Assert.True(red.R > 200 && red.B < 60, $"the red swatch came back as {red}");
        Assert.True(blue.B > 200 && blue.R < 60, $"the blue swatch came back as {blue}");

        // The background is read out of the frame rather than assumed, so the expected
        // composite below needs no knowledge of what is behind the canvas.
        var behind = At(20, 60);

        // Premultiplied source over that background: half the red, plus half of what was
        // there. Alpha dropped entirely would give the full red; alpha applied twice, or
        // straight alpha read as premultiplied, would land well outside the tolerance.
        var faint = At(80, 20);

        foreach (var (name, got, source) in new[]
                 {
                     ("red", faint.R, Red.Red),
                     ("green", faint.G, Red.Green),
                     ("blue", faint.B, Red.Blue),
                 })
        {
            var background = name switch
            {
                "red" => behind.R,
                "green" => behind.G,
                _ => behind.B,
            };

            var expected = source * (Half / 255.0) + background * (1 - Half / 255.0);

            Assert.True(Math.Abs(got - expected) <= 3,
                $"the half-alpha swatch's {name} channel came back as {got} where the source "
                + $"over the background it is on is {expected:0.0}");
        }
    }

    [Fact]
    public void A_padded_row_stride_is_honoured()
    {
        // The control builds a surface over a buffer it does not own, at whatever stride the
        // buffer reports. A stride wider than the pixels is ordinary -- alignment, or a
        // windowing system's own choice -- and a presenter that assumed rows were tight
        // would shear the image by a few pixels more on every row down.
        //
        // Not an AvaloniaFact: the technique is the subject, and no window is needed to put
        // a deliberate padding in front of it.
        const int width = 64;
        const int height = 48;
        // A multiple of the pixel size: Skia refuses a stride that does not divide by it,
        // which is a limit of the technique worth knowing rather than a limit of the test.
        const int padding = 36;

        using var art = Surface.CreateExactly(width, height, width, height);
        Swatches(art);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var rowBytes = width * 4 + padding;
        var buffer = new byte[rowBytes * height];

        // Pinned rather than unsafe: the application has no unsafe blocks left in it and a
        // test project is not the place to reintroduce the permission.
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            using var over = SKSurface.Create(info, pinned.AddrOfPinnedObject(), rowBytes)
                ?? throw new InvalidOperationException("no surface over the padded buffer");

            Presenter.Present(art, over.Canvas, View.At(1, 0, 0));
        }
        finally
        {
            pinned.Free();
        }

        // Read back through the padded stride and compare against the surface itself.
        var differ = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = y * rowBytes + x * 4;
                var stored = art.ReadStored(x, y);

                // Bgra8888 on the way out, Rgba8888 in the surface.
                if (buffer[at] != stored.Blue || buffer[at + 1] != stored.Green
                    || buffer[at + 2] != stored.Red || buffer[at + 3] != stored.Alpha)
                {
                    differ++;
                }
            }
        }

        Assert.True(differ == 0, $"{differ} of {width * height} pixels differ across a padded stride");
    }
}
