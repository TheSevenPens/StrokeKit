using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StrokeKit.Lab.Tests;

/// <summary>
/// Reading pixels back out of a frame the compositor produced.
/// <para>
/// The point of capturing rather than asserting on draw calls: everything between the
/// presenter and the screen -- the locked buffer, the channel order, the destination
/// rectangle, whatever the compositor does with it -- is downstream of every check the
/// guide has, and a claim about calls would not notice any of it.
/// </para>
/// </summary>
public sealed class Frame
{
    private readonly byte[] _bytes;
    private readonly int _stride;
    private readonly PixelFormat _format;

    public Frame(WriteableBitmap bitmap)
    {
        Width = bitmap.PixelSize.Width;
        Height = bitmap.PixelSize.Height;

        using var locked = bitmap.Lock();

        _stride = locked.RowBytes;
        _format = locked.Format;
        _bytes = new byte[_stride * Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, _bytes, 0, _bytes.Length);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>One pixel, in red/green/blue/alpha whatever order the framebuffer used.</summary>
    public (byte R, byte G, byte B, byte A) this[int x, int y]
    {
        get
        {
            var at = y * _stride + x * 4;

            var zero = _bytes[at];
            var one = _bytes[at + 1];
            var two = _bytes[at + 2];
            var three = _bytes[at + 3];

            return _format == PixelFormat.Bgra8888
                ? (two, one, zero, three)
                : (zero, one, two, three);
        }
    }

    public bool Same(int x, int y, (byte R, byte G, byte B, byte A) other) => this[x, y] == other;

    /// <summary>Where a control's top left sits in this frame, in the frame's own pixels.</summary>
    public static (int X, int Y) Origin(Visual control, Visual root, double scaling)
    {
        var point = control.TranslatePoint(default, root)
            ?? throw new InvalidOperationException("the control is not in that tree");

        return ((int)Math.Round(point.X * scaling), (int)Math.Round(point.Y * scaling));
    }
}
