using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

        /// <summary>截图并合成鼠标光标。</summary>
        public static Bitmap CaptureScreenWithCursor(Rectangle bounds)
        {
            Bitmap bmp = CaptureScreen(bounds);

            var ci = new NativeMethods.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(ci);
            if (NativeMethods.GetCursorInfo(out ci) && ci.flags == NativeMethods.CURSOR_SHOWING && ci.hCursor != IntPtr.Zero)
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    int x = ci.ptScreenPos.X - bounds.Left;
                    int y = ci.ptScreenPos.Y - bounds.Top;
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        NativeMethods.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
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

        /// <summary>实时获取光标下的窗口矩形和标题。</summary>
        public static bool GetWindowInfoAtPoint(Point screenPoint, out Rectangle screenRect, out string title)
        {
            return GetWindowInfoAtPoint(screenPoint, out screenRect, out title, false);
        }

        /// <summary>
        /// 获取光标处的窗口/元素矩形。elementMode=true 时优先返回最深层可见子控件
        /// （按钮/面板/输入框等），实现 PixPin/Snipaste 风格的元素级高亮。
        /// </summary>
        public static bool GetWindowInfoAtPoint(Point screenPoint, out Rectangle screenRect, out string title, bool elementMode)
        {
            screenRect = Rectangle.Empty;
            title = null;

            IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT(screenPoint.X, screenPoint.Y));
            if (hwnd == IntPtr.Zero) return false;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;

            IntPtr target = root;

            // 元素模式：从根窗口往下找最深的可见子控件
            if (elementMode && root != IntPtr.Zero)
            {
                IntPtr child = root;
                var pt = new NativeMethods.POINT(screenPoint.X, screenPoint.Y);
                // 根→屏幕坐标转客户坐标：用 ChildWindowFromPointEx 需要 root 的客户坐标
                // 简化：逐层下钻，每层用屏幕坐标的相对偏移
                IntPtr prev;
                do
                {
                    prev = child;
                    NativeMethods.RECT r;
                    if (!NativeMethods.GetWindowRect(child, out r)) break;
                    var local = new NativeMethods.POINT(screenPoint.X - r.Left, screenPoint.Y - r.Top);
                    IntPtr next = NativeMethods.ChildWindowFromPointEx(child, local, NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPTRANSPARENT);
                    if (next == IntPtr.Zero || next == child) break;
                    // 仅当子元素明显小于父（真正是一个子控件）才采用
                    NativeMethods.RECT nr;
                    if (NativeMethods.GetWindowRect(next, out nr))
                    {
                        Rectangle nRect = Rectangle.FromLTRB(nr.Left, nr.Top, nr.Right, nr.Bottom);
                        Rectangle pRect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                        if (nRect.Width > 4 && nRect.Height > 4 && nRect.Width <= pRect.Width && nRect.Height <= pRect.Height)
                            child = next;
                        else
                            break;
                    }
                    else break;
                } while (child != prev);
                if (child != IntPtr.Zero) target = child;
            }

            NativeMethods.RECT nativeRect;
            if (!NativeMethods.GetWindowRect(target, out nativeRect)) return false;

            screenRect = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            if (screenRect.Width < 4 || screenRect.Height < 4) return false;

            // 获取标题
            int len = NativeMethods.GetWindowTextLength(target);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                NativeMethods.GetWindowText(target, sb, sb.Capacity);
                title = sb.ToString();
            }

            return true;
        }

        public static List<Rectangle> EnumerateVisibleWindows()
        {
            var rects = new List<Rectangle>();
            Rectangle virtualBounds = SystemInformation.VirtualScreen;
            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;
                NativeMethods.RECT nativeRect;
                if (NativeMethods.GetWindowRect(hWnd, out nativeRect))
                {
                    Rectangle r = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
                    r = Rectangle.Intersect(r, virtualBounds);
                    if (r.Width > 4 && r.Height > 4)
                    {
                        rects.Add(r);
                    }
                }
                return true;
            }, IntPtr.Zero);
            return rects;
        }
    }
}

