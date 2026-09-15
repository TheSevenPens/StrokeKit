using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StrokeFieldGuide.Surfaces;
using StrokeFieldGuide.Views;

namespace StrokeFieldGuide.Lab;

public partial class MainWindow : Window
{
    /// <summary>
    /// The document. A thousand pixels square, fixed, because stage one is about showing a
    /// surface correctly and a resizable one would add a second subject.
    /// </summary>
    private readonly Surface _art = Surface.CreateExactly(1000, 1000, 1000, 1000);

    private readonly SurfaceView _view;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _view = new SurfaceView(_art);
        _view.ViewChanged += (_, _) => ShowStatus();

        this.FindControl<Panel>("Host")!.Children.Add(_view);

        this.FindControl<Button>("DrawDemo")!.Click += (_, _) => { Demo.Draw(_art); _view.InvalidateVisual(); };
        this.FindControl<Button>("ClearIt")!.Click += (_, _) => { _art.Canvas.Clear(); _view.InvalidateVisual(); };

        this.FindControl<Button>("ZoomIn")!.Click += (_, _) => _view.SetView(_view.View.In());
        this.FindControl<Button>("ZoomOut")!.Click += (_, _) => _view.SetView(_view.View.Out());
        this.FindControl<Button>("ZoomReset")!.Click += (_, _) => _view.SetView(View.At(1, _view.View.PanX, _view.View.PanY));
        this.FindControl<Button>("Centre")!.Click += (_, _) => _view.CentreOnSurface();

        Opened += (_, _) => { Demo.Draw(_art); _view.CentreOnSurface(); ShowStatus(); };
    }

    /// <summary>
    /// The numbers that decide whether what is on screen is right, shown rather than
    /// inferred. Somebody reading the page about a surface having two sizes should be able
    /// to watch both of them here.
    /// </summary>
    private void ShowStatus()
    {
        var scale = _view.RenderScale;
        var zoom = _view.View.Zoom;

        var describe = zoom >= 1
            ? $"{zoom:0}x  one surface pixel is {zoom:0}x{zoom:0} display pixels, nearest"
            : $"{zoom:0.###}x  minified, linear with mipmaps";

        this.FindControl<TextBlock>("Status")!.Text =
            $"surface {_art.PixelWidth}x{_art.PixelHeight} px   "
            + $"zoom {describe}   "
            + $"pan {_view.View.PanX:0},{_view.View.PanY:0} display px   "
            + $"render scaling {scale:0.####}   "
            + $"viewport {_view.Bounds.Width * scale:0}x{_view.Bounds.Height * scale:0} display px";
    }
}
