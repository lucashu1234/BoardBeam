using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class ScrollingCaptureTool
    {
        public static string Capture(Rectangle screenRegion)
        {
            return Capture(screenRegion, 8, 4, 360);
        }

        public static string Capture(Rectangle screenRegion, int maxScrolls, int wheelClicks, int waitMilliseconds)
        {
            if (screenRegion.Width < 16 || screenRegion.Height < 16)
            {
                throw new InvalidOperationException("长截图区域太小。");
            }

            screenRegion = Rectangle.Intersect(screenRegion, SystemInformation.VirtualScreen);
            if (screenRegion.Width < 16 || screenRegion.Height < 16)
            {
                throw new InvalidOperationException("长截图区域不在屏幕内。");
            }

            var frames = new List<Bitmap>();
            Point originalCursor = Cursor.Position;
            try
            {
                NativeMethods.SetCursorPos(screenRegion.Left + screenRegion.Width / 2, screenRegion.Top + screenRegion.Height / 2);
                Thread.Sleep(120);
                frames.Add(CaptureTool.CaptureScreen(screenRegion));

                for (int i = 0; i < maxScrolls; i++)
                {
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, -120 * wheelClicks, UIntPtr.Zero);
                    Thread.Sleep(waitMilliseconds);
                    Bitmap next = CaptureTool.CaptureScreen(screenRegion);
                    if (LooksSame(frames[frames.Count - 1], next))
                    {
                        next.Dispose();
                        break;
                    }
                    frames.Add(next);
                }

                using (Bitmap stitched = Stitch(frames))
                {
                    string file = AppPaths.NewImagePath("_long");
                    CaptureStore.Add(stitched);
                    stitched.Save(file, ImageFormat.Png);
                    return file;
                }
            }
            finally
            {
                NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
                for (int i = 0; i < frames.Count; i++)
                {
                    frames[i].Dispose();
                }
            }
        }

        private static Bitmap Stitch(List<Bitmap> frames)
        {
            Bitmap stitched = new Bitmap(frames[0].Width, frames[0].Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(stitched))
            {
                g.DrawImage(frames[0], 0, 0);
            }

            for (int i = 1; i < frames.Count; i++)
            {
                Bitmap previous = frames[i - 1];
                Bitmap current = frames[i];
                int overlap = FindVerticalOverlap(previous, current);
                if (overlap > current.Height - 24)
                {
                    continue;
                }

                int appendHeight = current.Height - overlap;
                int newHeight = stitched.Height + appendHeight;
                if (newHeight > 32000)
                {
                    break;
                }

                Bitmap combined = new Bitmap(stitched.Width, newHeight, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(combined))
                {
                    g.DrawImage(stitched, 0, 0);
                    Rectangle sourceRect = new Rectangle(0, overlap, current.Width, appendHeight);
                    Rectangle destRect = new Rectangle(0, stitched.Height, current.Width, appendHeight);
                    g.DrawImage(current, destRect, sourceRect, GraphicsUnit.Pixel);
                }
                stitched.Dispose();
                stitched = combined;
            }

            return stitched;
        }

        private static int FindVerticalOverlap(Bitmap previous, Bitmap current)
        {
            int maxOverlap = Math.Min(previous.Height, current.Height) - 24;
            if (maxOverlap < 80) return 0;

            int minOverlap = Math.Min(80, maxOverlap);
            int bestOverlap = 0;
            double bestScore = double.MaxValue;

            for (int overlap = maxOverlap; overlap >= minOverlap; overlap -= 8)
            {
                double score = Difference(previous, current, overlap);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOverlap = overlap;
                }

                if (score < 14.0)
                {
                    return overlap;
                }
            }

            return bestScore < 30.0 ? bestOverlap : 0;
        }

        private static bool LooksSame(Bitmap a, Bitmap b)
        {
            return DifferenceSameSize(a, b) < 4.0;
        }

        private static double Difference(Bitmap previous, Bitmap current, int overlap)
        {
            int stepX = Math.Max(1, previous.Width / 90);
            int stepY = Math.Max(1, overlap / 55);
            long total = 0;
            int count = 0;

            for (int y = 0; y < overlap; y += stepY)
            {
                int py = previous.Height - overlap + y;
                int cy = y;
                for (int x = 0; x < previous.Width; x += stepX)
                {
                    Color ca = previous.GetPixel(x, py);
                    Color cb = current.GetPixel(x, cy);
                    total += Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
                    count += 3;
                }
            }

            if (count == 0) return double.MaxValue;
            return (double)total / count;
        }

        private static double DifferenceSameSize(Bitmap a, Bitmap b)
        {
            int stepX = Math.Max(1, a.Width / 90);
            int stepY = Math.Max(1, a.Height / 55);
            long total = 0;
            int count = 0;

            for (int y = 0; y < a.Height; y += stepY)
            {
                for (int x = 0; x < a.Width; x += stepX)
                {
                    Color ca = a.GetPixel(x, y);
                    Color cb = b.GetPixel(x, y);
                    total += Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
                    count += 3;
                }
            }

            if (count == 0) return double.MaxValue;
            return (double)total / count;
        }
    }
}

