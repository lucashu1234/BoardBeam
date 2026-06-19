using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class ScrollingCaptureTool
    {
        private static bool isCapturing;

        public static string Capture(Rectangle screenRegion)
        {
            return Capture(screenRegion, 40, 4, 360);
        }

        public static string Capture(Rectangle screenRegion, int maxScrolls, int wheelClicks, int waitMilliseconds)
        {
            if (isCapturing) throw new InvalidOperationException("正在执行长截图，请稍后重试。");
            isCapturing = true;
            try
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
                bool cancelled = false;

                // 进度提示窗
                var progressForm = new Form();
                progressForm.Text = "BoardBeam 长截图";
                progressForm.Width = 300;
                progressForm.Height = 100;
                progressForm.StartPosition = FormStartPosition.CenterScreen;
                progressForm.TopMost = true;
                progressForm.UseWaitCursor = true;
                progressForm.ShowInTaskbar = false;
                progressForm.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                progressForm.ControlBox = false;
                var progressLabel = new Label();
                progressLabel.Text = "正在滚动截图... 1/" + maxScrolls;
                progressLabel.Left = 10;
                progressLabel.Top = 10;
                progressLabel.Width = 270;
                progressLabel.Height = 20;
                progressForm.Controls.Add(progressLabel);
                var progressBar = new ProgressBar();
                progressBar.Left = 10;
                progressBar.Top = 36;
                progressBar.Width = 200;
                progressBar.Height = 20;
                progressBar.Minimum = 0;
                progressBar.Maximum = maxScrolls;
                progressBar.Value = 0;
                progressForm.Controls.Add(progressBar);
                var cancelButton = new Button();
                cancelButton.Text = "取消";
                cancelButton.Left = 220;
                cancelButton.Top = 36;
                cancelButton.Width = 60;
                cancelButton.Height = 22;
                cancelButton.UseVisualStyleBackColor = true;
                cancelButton.Click += delegate { cancelled = true; };
                progressForm.Controls.Add(cancelButton);
                progressForm.Show();
                Application.DoEvents();

                try
                {
                    NativeMethods.SetCursorPos(screenRegion.Left + screenRegion.Width / 2, screenRegion.Top + screenRegion.Height / 2);
                    Thread.Sleep(120);
                    frames.Add(CaptureTool.CaptureScreen(screenRegion));

                    for (int i = 0; i < maxScrolls; i++)
                    {
                        if (cancelled) break;
                        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, -120 * wheelClicks, UIntPtr.Zero);
                        Thread.Sleep(waitMilliseconds);
                        if (cancelled) break;
                        Bitmap next = CaptureTool.CaptureScreen(screenRegion);
                        if (LooksSame(frames[frames.Count - 1], next))
                        {
                            next.Dispose();
                            progressLabel.Text = "页面已到底，正在拼接...";
                            Application.DoEvents();
                            break;
                        }
                        frames.Add(next);
                        progressLabel.Text = "正在滚动截图... " + (i + 1) + "/" + maxScrolls;
                        progressBar.Value = i + 1;
                        Application.DoEvents();
                    }

                    if (cancelled)
                    {
                        // 已取消：尝试拼接已捕获的帧
                        if (frames.Count < 2) return null;
                        progressLabel.Text = "正在拼接已捕获的 " + frames.Count + " 帧...";
                        progressBar.Value = progressBar.Maximum;
                        Application.DoEvents();
                    }

                    progressLabel.Text = "正在拼接 " + frames.Count + " 帧...";
                    Application.DoEvents();
                    Bitmap stitched;
                    try
                    {
                        stitched = Stitch(frames);
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.Log("滚动截图拼接", ex);
                        MessageBox.Show("拼接失败：" + ex.Message, "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return null;
                    }
                    if (stitched.Height >= 32000)
                    {
                        progressLabel.Text = "已截断到 32000px（达到最大高度）";
                        Application.DoEvents();
                    }
                    progressLabel.Text = "正在保存... 共 " + frames.Count + " 帧，高度 " + stitched.Height;
                    Application.DoEvents();
                    string file = AppPaths.NewImagePath("_long");
                    string savedFile = OverlayForm.SaveImageAutoFormat(stitched, file);
                    CaptureStore.Add(stitched);
                    return savedFile;
                }
                finally
                {
                    NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i].Dispose();
                    }
                    progressForm.Close();
                    progressForm.Dispose();
                }
            }
            finally
            {
                isCapturing = false;
            }
        }

        private static Bitmap Stitch(List<Bitmap> frames)
        {
            if (frames.Count == 1) return (Bitmap)frames[0].Clone();

            // 预计算每帧的重叠量，得到总高度
            int[] overlaps = new int[frames.Count];
            overlaps[0] = 0;
            int totalHeight = frames[0].Height;
            for (int i = 1; i < frames.Count; i++)
            {
                int overlap = FindVerticalOverlap(frames[i - 1], frames[i]);
                if (overlap > frames[i].Height - 24) overlap = 0;
                overlaps[i] = overlap;
                int appendHeight = frames[i].Height - overlap;
                totalHeight += appendHeight;
            }
            if (totalHeight > 32000) totalHeight = 32000;

            // 一次性分配最终 Bitmap
            Bitmap stitched = new Bitmap(frames[0].Width, totalHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(stitched))
            {
                g.DrawImage(frames[0], 0, 0);
                int y = frames[0].Height;
                for (int i = 1; i < frames.Count; i++)
                {
                    int appendHeight = frames[i].Height - overlaps[i];
                    if (y + appendHeight > totalHeight) break;
                    Rectangle sourceRect = new Rectangle(0, overlaps[i], frames[i].Width, appendHeight);
                    Rectangle destRect = new Rectangle(0, y, frames[i].Width, appendHeight);
                    g.DrawImage(frames[i], destRect, sourceRect, GraphicsUnit.Pixel);
                    y += appendHeight;
                }
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

            // 第一步：粗搜索（步长 8）
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
                    bestOverlap = overlap;
                    bestScore = score;
                    break;
                }
            }

            if (bestScore >= 30.0) return 0;

            // 第二步：精细搜索（步长 1），在粗搜索最优位置 ±12 像素范围内
            int fineMin = Math.Max(minOverlap, bestOverlap - 12);
            int fineMax = Math.Min(maxOverlap, bestOverlap + 12);
            for (int overlap = fineMin; overlap <= fineMax; overlap++)
            {
                double score = Difference(previous, current, overlap);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOverlap = overlap;
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

            // 锁定 previous 底部 overlap 行
            int prevLockY = previous.Height - overlap;
            var prevRect = new Rectangle(0, prevLockY, previous.Width, overlap);
            var prevData = previous.LockBits(prevRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int prevStride = prevData.Stride;
            byte[] prevPixels = new byte[prevStride * overlap];
            Marshal.Copy(prevData.Scan0, prevPixels, 0, prevPixels.Length);
            previous.UnlockBits(prevData);

            // 锁定 current 顶部 overlap 行
            var curRect = new Rectangle(0, 0, current.Width, overlap);
            var curData = current.LockBits(curRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int curStride = curData.Stride;
            byte[] curPixels = new byte[curStride * overlap];
            Marshal.Copy(curData.Scan0, curPixels, 0, curPixels.Length);
            current.UnlockBits(curData);

            long total = 0;
            int count = 0;

            for (int y = 0; y < overlap; y += stepY)
            {
                for (int x = 0; x < previous.Width; x += stepX)
                {
                    int pOff = y * prevStride + x * 4;
                    int cOff = y * curStride + x * 4;
                    total += Math.Abs(prevPixels[pOff + 2] - curPixels[cOff + 2])
                           + Math.Abs(prevPixels[pOff + 1] - curPixels[cOff + 1])
                           + Math.Abs(prevPixels[pOff] - curPixels[cOff]);
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

            var aRect = new Rectangle(0, 0, a.Width, a.Height);
            var aData = a.LockBits(aRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int aStride = aData.Stride;
            byte[] aPixels = new byte[aStride * a.Height];
            Marshal.Copy(aData.Scan0, aPixels, 0, aPixels.Length);
            a.UnlockBits(aData);

            var bRect = new Rectangle(0, 0, b.Width, b.Height);
            var bData = b.LockBits(bRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int bStride = bData.Stride;
            byte[] bPixels = new byte[bStride * b.Height];
            Marshal.Copy(bData.Scan0, bPixels, 0, bPixels.Length);
            b.UnlockBits(bData);

            long total = 0;
            int count = 0;

            for (int y = 0; y < a.Height; y += stepY)
            {
                for (int x = 0; x < a.Width; x += stepX)
                {
                    int aOff = y * aStride + x * 4;
                    int bOff = y * bStride + x * 4;
                    total += Math.Abs(aPixels[aOff + 2] - bPixels[bOff + 2])
                           + Math.Abs(aPixels[aOff + 1] - bPixels[bOff + 1])
                           + Math.Abs(aPixels[aOff] - bPixels[bOff]);
                    count += 3;
                }
            }

            if (count == 0) return double.MaxValue;
            return (double)total / count;
        }
    }
}

