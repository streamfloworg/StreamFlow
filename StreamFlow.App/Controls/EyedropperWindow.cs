using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// UseWindowsForms pulls in System.Drawing/System.Windows.Forms as implicit usings project-wide,
// which collide with several WPF type names below (Color, Image, Point, MouseEventArgs,
// KeyEventArgs) — aliased explicitly rather than fully qualifying every use.
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Cursors = System.Windows.Input.Cursors;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace StreamFlow.App.Controls;

/// <summary>Full-screen "eyedropper" color picker — freezes a screenshot of the current desktop
/// (across all monitors, whatever's actually on screen right now, StreamFlow's own windows
/// included) behind a magnified loupe that follows the cursor, and returns the exact pixel color
/// clicked. Lets a user sample a color (e.g. for chromakey) directly from the live preview or
/// any other on-screen content, rather than guessing/typing RGB values into the standard
/// <see cref="System.Windows.Forms.ColorDialog"/>.</summary>
public sealed class EyedropperWindow : Window
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int LoupeSamplePixels = 15; // odd, so the crosshair centers on one exact pixel
    private const double LoupeDisplaySize = 150;

    private readonly BitmapSource _screenshot; // physical-pixel screenshot of the whole virtual screen
    private readonly double _dpiScaleX;
    private readonly double _dpiScaleY;

    private readonly Image _loupeImage = new() { Width = LoupeDisplaySize, Height = LoupeDisplaySize };
    private readonly Border _loupeContainer;
    private readonly Canvas _overlayCanvas = new() { IsHitTestVisible = false };
    private readonly TextBlock _hexReadout = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 4, 0, 0),
    };

    /// <summary>The picked color, or null if the user canceled (Escape/right-click).</summary>
    public Color? PickedColor { get; private set; }

    private EyedropperWindow(BitmapSource screenshot, double dpiScaleX, double dpiScaleY)
    {
        _screenshot = screenshot;
        _dpiScaleX = dpiScaleX;
        _dpiScaleY = dpiScaleY;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;

        var root = new Grid();

        var background = new Image { Source = _screenshot, Stretch = Stretch.Fill, IsHitTestVisible = false };
        root.Children.Add(background);

        RenderOptions.SetBitmapScalingMode(_loupeImage, BitmapScalingMode.NearestNeighbor);

        var loupeInner = new Grid();
        loupeInner.Children.Add(_loupeImage);
        loupeInner.Children.Add(new Rectangle
        {
            Width = LoupeDisplaySize / LoupeSamplePixels,
            Height = LoupeDisplaySize / LoupeSamplePixels,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _loupeContainer = new Border
        {
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Black,
            Child = new StackPanel { Children = { loupeInner, _hexReadout }, Margin = new Thickness(4) },
        };
        _overlayCanvas.Children.Add(_loupeContainer);
        root.Children.Add(_overlayCanvas);

        Content = root;

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += OnKeyDown;
        Loaded += (_, _) => { Activate(); Focus(); };
    }

    /// <summary>Shows the eyedropper full-screen and blocks until the user picks a color or
    /// cancels. Captures the screen fresh every call, so it always reflects whatever's currently
    /// rendered (the app's own live preview included) rather than a stale/cached image.</summary>
    public static Color? PickColor()
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;

        using var bmp = new System.Drawing.Bitmap(vs.Width, vs.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size);
        }

        var hbitmap = bmp.GetHbitmap();
        BitmapSource screenshot;
        try
        {
            screenshot = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            screenshot.Freeze();
        }
        finally
        {
            DeleteObject(hbitmap);
        }

        // DPI of the app's main window — used to map this window's device-independent
        // coordinates back to the screenshot's physical pixels. Assumes a uniform DPI scale
        // across the virtual screen; on a genuinely mixed-DPI multi-monitor setup, sampling on a
        // secondary monitor at a different scale than the primary will be off by that ratio.
        var source = PresentationSource.FromVisual(System.Windows.Application.Current.MainWindow!);
        var dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var window = new EyedropperWindow(screenshot, dpiScaleX, dpiScaleY)
        {
            Owner = System.Windows.Application.Current.MainWindow,
            Left = vs.Left / dpiScaleX,
            Top = vs.Top / dpiScaleY,
            Width = vs.Width / dpiScaleX,
            Height = vs.Height / dpiScaleY,
        };

        window.ShowDialog();
        return window.PickedColor;
    }

    private Color SampleAt(Point windowPos)
    {
        var px = Math.Clamp((int)(windowPos.X * _dpiScaleX), 0, _screenshot.PixelWidth - 1);
        var py = Math.Clamp((int)(windowPos.Y * _dpiScaleY), 0, _screenshot.PixelHeight - 1);

        var pixel = new byte[4];
        _screenshot.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]); // BGRA -> RGB
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var color = SampleAt(pos);
        _hexReadout.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        var half = LoupeSamplePixels / 2;
        var cropX = Math.Clamp((int)(pos.X * _dpiScaleX), half, _screenshot.PixelWidth - 1 - half);
        var cropY = Math.Clamp((int)(pos.Y * _dpiScaleY), half, _screenshot.PixelHeight - 1 - half);
        _loupeImage.Source = new CroppedBitmap(_screenshot, new Int32Rect(cropX - half, cropY - half, LoupeSamplePixels, LoupeSamplePixels));

        // Offset the loupe from the cursor, flipping to the other side near screen edges so it
        // never renders off-window.
        const double offset = 24;
        var left = pos.X + offset;
        var top = pos.Y + offset;
        if (left + LoupeDisplaySize + 16 > ActualWidth) left = pos.X - offset - LoupeDisplaySize - 16;
        if (top + LoupeDisplaySize + 48 > ActualHeight) top = pos.Y - offset - LoupeDisplaySize - 48;
        Canvas.SetLeft(_loupeContainer, left);
        Canvas.SetTop(_loupeContainer, top);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PickedColor = SampleAt(e.GetPosition(this));
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
