using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class LiveZoomTool
    {
        private static LiveZoomForm activeForm;

        public static bool IsRunning
        {
            get { return activeForm != null && !activeForm.IsDisposed; }
        }

        public static void Toggle()
        {
            if (IsRunning)
            {
                activeForm.Close();
            }
            else
            {
                var form = new LiveZoomForm();
                activeForm = form;
                form.FormClosed += delegate
                {
                    if (object.ReferenceEquals(activeForm, form))
                    {
                        activeForm = null;
                    }
                };
                form.Show();
            }
        }

        public static void Shutdown()
        {
            if (activeForm == null || activeForm.IsDisposed) return;
            activeForm.Close();
            activeForm = null;
        }

        private sealed class LiveZoomForm : Form
        {
            private readonly Timer timer;
            private readonly Rectangle virtualBounds;
            private int zoomLevel;
            private bool frozen;

            // 缓冲区复用：避免每帧 new Bitmap
            private Bitmap frameBuffer;
            private Graphics frameBufferGfx;
            private int bufferW, bufferH;

            // 帧率自适应
            private readonly Stopwatch frameStopwatch = new Stopwatch();
            private int targetInterval = 33; // 初始 ~30fps

            // HUD 字体缓存
            private Font hudFont;
            private SolidBrush hudBgBrush;

            // 透明光标
            private IntPtr cursorHandle;

            public LiveZoomForm()
            {
                virtualBounds = SystemInformation.VirtualScreen;
                zoomLevel = 2;

                FormBorderStyle = FormBorderStyle.None;
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.Manual;
                Bounds = virtualBounds;
                TopMost = true;
                ShowInTaskbar = false;
                DoubleBuffered = true;

                // 使用透明光标隐藏系统光标
                try
                {
                    using (var bmp = new Bitmap(1, 1))
                    {
                        cursorHandle = bmp.GetHicon();
                        Cursor = new Cursor(cursorHandle);
                    }
                }
                catch { }

                timer = new Timer();
                timer.Interval = targetInterval;
                timer.Tick += OnTick;
            }

            /// <summary>确保帧缓冲区大小与需要捕获的区域匹配，复用已有缓冲区。</summary>
            private void EnsureFrameBuffer(int w, int h)
            {
                if (frameBuffer != null && bufferW == w && bufferH == h)
                    return;
                if (frameBufferGfx != null) { frameBufferGfx.Dispose(); frameBufferGfx = null; }
                if (frameBuffer != null) { frameBuffer.Dispose(); frameBuffer = null; }
                if (w < 4 || h < 4) return;
                frameBuffer = new Bitmap(w, h);
                frameBufferGfx = Graphics.FromImage(frameBuffer);
                bufferW = w;
                bufferH = h;
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                }
                catch { }
                frameStopwatch.Start();
                timer.Start();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop();
                timer.Dispose();
                frameStopwatch.Stop();
                if (frameBufferGfx != null) { frameBufferGfx.Dispose(); frameBufferGfx = null; }
                if (frameBuffer != null) { frameBuffer.Dispose(); frameBuffer = null; }
                if (hudFont != null) { hudFont.Dispose(); hudFont = null; }
                if (hudBgBrush != null) { hudBgBrush.Dispose(); hudBgBrush = null; }
                if (cursorHandle != IntPtr.Zero)
                {
                    try { NativeMethods.DestroyIcon(cursorHandle); } catch { }
                    cursorHandle = IntPtr.Zero;
                }
                base.OnFormClosed(e);
            }

            private void OnTick(object sender, EventArgs e)
            {
                if (frozen) return;

                frameStopwatch.Restart();

                Point cursor = Cursor.Position;

                // 计算源区域：以光标为中心，屏幕尺寸 / 缩放倍数
                int srcW = virtualBounds.Width / zoomLevel;
                int srcH = virtualBounds.Height / zoomLevel;
                int srcX = cursor.X - srcW / 2;
                int srcY = cursor.Y - srcH / 2;

                var srcRect = new Rectangle(srcX, srcY, srcW, srcH);
                srcRect = Rectangle.Intersect(srcRect, virtualBounds);
                if (srcRect.Width < 4 || srcRect.Height < 4) return;

                // 复用帧缓冲区
                EnsureFrameBuffer(srcRect.Width, srcRect.Height);
                if (frameBufferGfx == null) return;

                // 直接复制屏幕到缓冲区（不创建新 Bitmap）
                frameBufferGfx.CopyFromScreen(srcRect.X, srcRect.Y, 0, 0,
                    new Size(srcRect.Width, srcRect.Height));

                Invalidate();

                // 帧率自适应：如果处理耗时超过目标间隔，自动降帧率
                long elapsed = frameStopwatch.ElapsedMilliseconds;
                if (elapsed > targetInterval)
                {
                    // 降帧：确保间隔至少覆盖实际耗时 + 余量
                    int newInterval = (int)(elapsed + 5);
                    if (newInterval > 100) newInterval = 100; // 最低 10fps
                    if (newInterval != targetInterval)
                    {
                        targetInterval = newInterval;
                        timer.Interval = targetInterval;
                    }
                }
                else if (elapsed < targetInterval / 2 && targetInterval > 33)
                {
                    // 系统恢复了，逐步恢复帧率
                    int newInterval = Math.Max(33, targetInterval - 10);
                    if (newInterval != targetInterval)
                    {
                        targetInterval = newInterval;
                        timer.Interval = targetInterval;
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (frameBuffer == null) return;

                Graphics g = e.Graphics;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                // 将捕获的区域拉伸到全屏
                g.DrawImage(frameBuffer, new Rectangle(0, 0, Width, Height));

                // 计算光标在放大视图中的位置
                Point cursor = Cursor.Position;
                float viewX = (cursor.X - virtualBounds.Left) * (float)Width / virtualBounds.Width;
                float viewY = (cursor.Y - virtualBounds.Top) * (float)Height / virtualBounds.Height;

                // 绘制十字准星
                using (var pen = new Pen(Color.FromArgb(180, 255, 255, 255), 1))
                {
                    g.DrawLine(pen, viewX - 20, viewY, viewX - 6, viewY);
                    g.DrawLine(pen, viewX + 6, viewY, viewX + 20, viewY);
                    g.DrawLine(pen, viewX, viewY - 20, viewX, viewY - 6);
                    g.DrawLine(pen, viewX, viewY + 6, viewX, viewY + 20);
                }
                using (var pen2 = new Pen(Color.FromArgb(100, 0, 0, 0), 1))
                {
                    g.DrawLine(pen2, viewX - 19, viewY, viewX - 7, viewY);
                    g.DrawLine(pen2, viewX + 7, viewY, viewX + 19, viewY);
                    g.DrawLine(pen2, viewX, viewY - 19, viewX, viewY - 7);
                    g.DrawLine(pen2, viewX, viewY + 7, viewX, viewY + 19);
                }

                // HUD: 缓存字体和背景画刷（按绘制 DPI 缩放）
                float dp = DpiScale.Factor(g);
                if (hudFont == null)
                {
                    hudFont = new Font(FontFamily.GenericSansSerif, DpiScale.ScaleF(14, dp), FontStyle.Bold, GraphicsUnit.Pixel);
                    hudBgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                }
                string info = (frozen ? "■ FROZEN  " : "") + "LiveZoom " + zoomLevel + "x  滚轮调倍数  Space冻结  Esc退出";
                SizeF size = g.MeasureString(info, hudFont);
                int ox = DpiScale.Scale(12, dp), oy = DpiScale.Scale(12, dp);
                g.FillRectangle(hudBgBrush, ox, oy, size.Width + DpiScale.Scale(12, dp), size.Height + DpiScale.Scale(8, dp));
                g.DrawString(info, hudFont, Brushes.White, DpiScale.Scale(18, dp), DpiScale.Scale(16, dp));
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int old = zoomLevel;
                if (e.Delta > 0) zoomLevel++;
                if (e.Delta < 0) zoomLevel--;
                if (zoomLevel < 2) zoomLevel = 2;
                if (zoomLevel > 8) zoomLevel = 8;
                if (zoomLevel != old) Invalidate();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                    return;
                }
                if (e.KeyCode == Keys.Space)
                {
                    frozen = !frozen;
                    if (frozen)
                    {
                        // 冻结模式暂停 Timer，避免空转
                        timer.Stop();
                    }
                    else
                    {
                        // 恢复
                        frameStopwatch.Restart();
                        timer.Start();
                    }
                    Invalidate();
                    return;
                }
                if (e.KeyCode == Keys.D2) { zoomLevel = 2; Invalidate(); }
                if (e.KeyCode == Keys.D3) { zoomLevel = 3; Invalidate(); }
                if (e.KeyCode == Keys.D4) { zoomLevel = 4; Invalidate(); }
                if (e.KeyCode == Keys.D5) { zoomLevel = 5; Invalidate(); }
                if (e.KeyCode == Keys.D6) { zoomLevel = 6; Invalidate(); }
                if (e.KeyCode == Keys.D7) { zoomLevel = 7; Invalidate(); }
                if (e.KeyCode == Keys.D8) { zoomLevel = 8; Invalidate(); }
            }
        }
    }
}
