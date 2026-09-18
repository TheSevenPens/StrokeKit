using Avalonia;

namespace StrokeKit.Lab;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The self-test runs the same presentation code the window does and reports on it,
        // so CI can gate on the stage's guarantees without a person looking at a screen.
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase)) return SelfTest.Run();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // UseWin32().UseSkia() rather than UsePlatformDetect(), which ships in Avalonia.Desktop
    // and pulls backends this Windows-only application cannot load. UseHarfBuzz() because
    // Avalonia 12 split text shaping out of Skia and the application throws without it.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseWin32()
        .UseSkia()
        .UseHarfBuzz()
        .LogToTrace();
}
