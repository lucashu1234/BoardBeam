using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class CaptureTool
    {
        public static Bitmap CaptureScreen(Rectangle bounds)
        {
            bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("截图区域不在屏幕范围内。");
            }

            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static bool TryGetWindowUnderCursor(out Rectangle screenRect)
        {
            Point cursor = Cursor.Position;
            IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT(cursor.X, cursor.Y));
            if (hwnd != IntPtr.Zero)
            {
                hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            }

            if (hwnd == IntPtr.Zero)
            {
                screenRect = Rectangle.Empty;
                return false;
            }

            NativeMethods.RECT nativeRect;
            if (!NativeMethods.GetWindowRect(hwnd, out nativeRect))
            {
                screenRect = Rectangle.Empty;
                return false;
            }

            screenRect = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            return screenRect.Width > 4 && screenRect.Height > 4;
        }

        public static Bitmap CaptureWindowUnderCursor()
        {
            Rectangle rect;
            if (!TryGetWindowUnderCursor(out rect)) return null;
            Rectangle virtualBounds = SystemInformation.VirtualScreen;
            rect = Rectangle.Intersect(rect, virtualBounds);
            if (rect.Width < 4 || rect.Height < 4) return null;
            return CaptureScreen(rect);
        }
    }
}

