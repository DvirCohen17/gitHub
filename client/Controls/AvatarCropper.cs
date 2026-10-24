using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GitHubDemo.Controls;

// Crop a picture to a circle: drag to move it, use the slider (or mouse wheel)
// to zoom. RenderPng() produces the square image that is uploaded.
public sealed class AvatarCropper : Grid
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(AvatarCropper), new PropertyMetadata(null, (d, _) => ((AvatarCropper)d).Reset()));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(AvatarCropper), new PropertyMetadata(1.0, (d, _) => ((AvatarCropper)d).Clamp()));

    private const double ViewSize = 280;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Canvas _canvas = new() { Width = ViewSize, Height = ViewSize, ClipToBounds = true, Background = Brushes.Black };
    private double _offsetX, _offsetY;   // top-left of the image inside the view
    private double _baseScale = 1;       // image pixels -> view units at zoom 1 (image covers the view)
    private Point? _dragStart;
    private double _dragOffsetX, _dragOffsetY;

    public AvatarCropper()
    {
        Width = ViewSize;
        Height = ViewSize;
        _canvas.Children.Add(_image);
        Children.Add(_canvas);

        // dim everything outside the circle
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, ViewSize, ViewSize)), new EllipseGeometry(new Point(ViewSize / 2, ViewSize / 2), ViewSize / 2 - 2, ViewSize / 2 - 2));
        Children.Add(new System.Windows.Shapes.Path { Data = outside, Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), IsHitTestVisible = false });
        Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(2), IsHitTestVisible = false, Opacity = 0.9 });

        Cursor = Cursors.SizeAll;
        MouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            _dragOffsetX = _offsetX;
            _dragOffsetY = _offsetY;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (_dragStart is not { } start) return;
            var p = e.GetPosition(this);
            _offsetX = _dragOffsetX + p.X - start.X;
            _offsetY = _dragOffsetY + p.Y - start.Y;
            Clamp();
        };
        MouseLeftButtonUp += (_, _) =>
        {
            _dragStart = null;
            ReleaseMouseCapture();
        };
        MouseWheel += (_, e) => Zoom = Math.Clamp(Zoom + (e.Delta > 0 ? 0.1 : -0.1), 1, 4);
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private double ImageWidth => Source == null ? 0 : Source.PixelWidth * _baseScale * Zoom;
    private double ImageHeight => Source == null ? 0 : Source.PixelHeight * _baseScale * Zoom;

    private void Reset()
    {
        _image.Source = Source;
        if (Source == null) return;
        _baseScale = ViewSize / Math.Min(Source.PixelWidth, Source.PixelHeight);
        SetCurrentValue(ZoomProperty, 1.0);
        _offsetX = (ViewSize - ImageWidth) / 2;
        _offsetY = (ViewSize - ImageHeight) / 2;
        Clamp();
    }

    // Keep the circle covered by the picture, and zoom around the centre.
    private double _lastZoom = 1;

    private void Clamp()
    {
        if (Source == null) return;
        if (Math.Abs(Zoom - _lastZoom) > 1e-9)
        {
            double factor = Zoom / _lastZoom;
            _offsetX = ViewSize / 2 - (ViewSize / 2 - _offsetX) * factor;
            _offsetY = ViewSize / 2 - (ViewSize / 2 - _offsetY) * factor;
            _lastZoom = Zoom;
        }
        _offsetX = Math.Min(0, Math.Max(ViewSize - ImageWidth, _offsetX));
        _offsetY = Math.Min(0, Math.Max(ViewSize - ImageHeight, _offsetY));
        _image.Width = ImageWidth;
        _image.Height = ImageHeight;
        Canvas.SetLeft(_image, _offsetX);
        Canvas.SetTop(_image, _offsetY);
    }

    // The visible square, as a PNG of size x size pixels.
    public byte[] RenderPng(int size = 256)
    {
        if (Source == null) throw new InvalidOperationException("No picture.");
        double scale = size / ViewSize;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(Source, new Rect(_offsetX * scale, _offsetY * scale, ImageWidth * scale, ImageHeight * scale));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
