using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;



using Windows.Devices.Display;
using Windows.Devices.Display.Core;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursor = System.Windows.Input.Cursor;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;

namespace StreamFlow.Core.Helpers;
public static class CursorHelper
{
    public static void SetCursor()
    {
        Mouse.OverrideCursor = CreateCursor(Brushes.Gold, new Pen(Brushes.Black, 0.1d), new Size(50, 50));
        //Mouse.OverrideCursor = CursorHelper.CreateCursor(Properties.Resources.cursor_move_picture, ImageFormat.Png, 13, 17);
    }



    public static Cursor CreateCursor(Brush brush, Pen pen, Size size)
    {
        var vis = new DrawingVisual();
        using (var dc = vis.RenderOpen())
        {
            dc.DrawRectangle(brush, pen, new Rect(0, 0, size.Width, size.Height));
            dc.Close();
        }
        var rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Default);
        rtb.Render(vis);

        return CreateCursor(rtb, (int)(size.Width / 2), (int)(size.Height / 2));
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true, ExactSpelling = true)]
    public static extern int GetDeviceCaps(IntPtr hDC, int nIndex);

    public enum DeviceCap
    {
        VERTRES = 10,
        DESKTOPVERTRES = 117
    }

    private static double GetWindowsScreenScalingFactor(bool percentage = true)
    {
        //Create Graphics object from the current windows handle
        Graphics GraphicsObject = Graphics.FromHwnd(IntPtr.Zero);
        //Get Handle to the device context associated with this Graphics object
        IntPtr DeviceContextHandle = GraphicsObject.GetHdc();
        //Call GetDeviceCaps with the Handle to retrieve the Screen Height
        int LogicalScreenHeight = GetDeviceCaps(DeviceContextHandle, (int)DeviceCap.VERTRES);
        int PhysicalScreenHeight = GetDeviceCaps(DeviceContextHandle, (int)DeviceCap.DESKTOPVERTRES);
        //Divide the Screen Heights to get the scaling factor and round it to two decimals
        double ScreenScalingFactor = Math.Round((double)PhysicalScreenHeight / (double)LogicalScreenHeight, 2);
        //If requested as percentage - convert it
        if (percentage)
        {
            ScreenScalingFactor *= 100.0;
        }
        //Release the Handle and Dispose of the GraphicsObject object
        GraphicsObject.ReleaseHdc(DeviceContextHandle);
        GraphicsObject.Dispose();
        //Return the Scaling Factor
        return ScreenScalingFactor;
    }

    public static Cursor CreateCursor(Image picture, ImageFormat format, int hotspotX = 0, int hotspotY = 0)
    {
        var vis = new DrawingVisual();
        var imageSource = ConvertBitmap(picture, format);
        using (var dc = vis.RenderOpen())
        {
            dc.DrawImage(imageSource, new Rect(0, 0, imageSource.Width, imageSource.Height));
            dc.Close();
        }
        var rtb = new RenderTargetBitmap((int)imageSource.Width, (int)imageSource.Height, 96, 96, PixelFormats.Default);
        rtb.Render(vis);

        return CreateCursor(rtb, hotspotX, hotspotY);
    }

    private static Cursor CreateCursor(BitmapSource bitmapSource, int hotspotX, int hotspotY)
    {
        using (var ms1 = new MemoryStream())
        {
            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            pngEncoder.Save(ms1);

            var pngBytes = ms1.ToArray();
            var size = pngBytes.GetLength(0);

            using (var ms = new MemoryStream())
            {
                //Reserved must be zero; 2 bytes
                ms.Write(BitConverter.GetBytes((short)0), 0, 2);

                //image Type 1 = ico 2 = cur; 2 bytes
                ms.Write(BitConverter.GetBytes((short)2), 0, 2);

                //number of images; 2 bytes
                ms.Write(BitConverter.GetBytes((short)1), 0, 2);

                //image width in pixels
                ms.WriteByte(32);

                //image height in pixels
                ms.WriteByte(32);

                //Number of Colors in the color palette. Should be 0 if the image doesn't use a color palette
                ms.WriteByte(0);

                //reserved must be 0
                ms.WriteByte(0);

                //2 bytes. In CUR format: Specifies the horizontal coordinates of the hotspot in number of pixels from the left.
                ms.Write(BitConverter.GetBytes((short)hotspotX), 0, 2);
                //2 bytes. In CUR format: Specifies the vertical coordinates of the hotspot in number of pixels from the top.
                ms.Write(BitConverter.GetBytes((short)hotspotY), 0, 2);

                //Specifies the size of the image's data in bytes
                ms.Write(BitConverter.GetBytes(size), 0, 4);

                //Specifies the offset of BMP or PNG data from the beginning of the ICO/CUR file
                ms.Write(BitConverter.GetBytes(22), 0, 4);

                ms.Write(pngBytes, 0, size); //write the png data.
                ms.Seek(0, SeekOrigin.Begin);
                return new Cursor(ms);
            }
        }
    }

    private static BitmapImage ConvertBitmap(Image src, ImageFormat format)
    {
        var ms = new MemoryStream();
        src.Save(ms, format);
        var image = new BitmapImage();
        image.BeginInit();
        ms.Seek(0, SeekOrigin.Begin);
        image.StreamSource = ms;
        image.EndInit();
        return image;
    }
}

