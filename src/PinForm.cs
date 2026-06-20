using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class PinForm : Form
    {
        private readonly Bitmap sourceImage;   // 始终保持原始像素，绝不破坏性变换
        private float scale;
        private float rotationDegrees;          // 任意角度旋转
        private float signX = 1f, signY = 1f;   // 翻转符号（1 或 -1）
        private bool dragging;
        private Point dragStart;
        private Point formStart;
        private bool locked;
        private Label scaleLabel;
        private System.Windows.Forms.Timer scaleTimer;
        private System.Windows.Forms.Timer hintTimer;
        private bool showResizeHint;

        // 鼠标穿透
        private bool clickThrough;
        private double opacityBeforeClickThrough = 1.0;

        // 缩放手柄
        private const int HandleSize = 8;
        private const int HandleHitTolerance = 6;
        private bool isResizingFromHandle;
        private float resizeStartScale;
        private Point resizeStartMouse;
        private int resizeCornerIndex; // 0=TL, 1=TR, 2=BR, 3=BL

        public PinForm(Bitmap image, Point location) : this(image, location, false) { }

        /// <param name="takeOwnership">为 true 时直接引用 image 而不 Clone，调用方不能再使用该 image。</param>
        public PinForm(Bitmap image, Point location, bool takeOwnership)
        {
            this.sourceImage = takeOwnership ? image : (Bitmap)image.Clone();
            scale = 1.0f;
            rotationDegrees = 0;
            locked = false;
            showResizeHint = true;
            isResizingFromHandle = false;

            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = Color.Black;
            Location = location;
            ContextMenuStrip = BuildMenu();
            ApplySize();

            scaleLabel = new Label();
            scaleLabel.BackColor = System.Drawing.Color.FromArgb(180, 0, 0, 0);
            scaleLabel.ForeColor = System.Drawing.Color.White;
            scaleLabel.Font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold, GraphicsUnit.Pixel);
            scaleLabel.AutoSize = true;
            scaleLabel.Visible = false;
            scaleLabel.Padding = new Padding(4, 2, 4, 2);
            Controls.Add(scaleLabel);

            scaleTimer = new Timer();
            scaleTimer.Interval = 800;
            scaleTimer.Tick += delegate { scaleLabel.Visible = false; scaleTimer.Stop(); };

            // 3 秒后隐藏缩放提示
            hintTimer = new Timer();
            hintTimer.Interval = 3000;
            hintTimer.Tick += delegate { showResizeHint = false; hintTimer.Stop(); hintTimer.Dispose(); if (!IsDisposed) Invalidate(); };
            hintTimer.Start();
        }

        /// <summary>变换后的包围盒尺寸（窗口客户端大小）。</summary>
        private Size TransformedSize()
        {
            double rad = rotationDegrees * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(rad));
            double sin = Math.Abs(Math.Sin(rad));
            double w = sourceImage.Width * scale * cos + sourceImage.Height * scale * sin;
            double h = sourceImage.Width * scale * sin + sourceImage.Height * scale * cos;
            return new Size(Math.Max(40, (int)Math.Round(w)), Math.Max(40, (int)Math.Round(h)));
        }

        /// <summary>把当前变换（旋转+翻转）应用到目标 Graphics，坐标系已平移到窗口中心。</summary>
        private void ApplyTransform(Graphics g)
        {
            g.TranslateTransform(Width / 2f, Height / 2f);
            g.RotateTransform(rotationDegrees);
            g.ScaleTransform(signX, signY);
        }

        /// <summary>渲染完整变换后的图像到给定 Graphics（无平移，绘制在 0,0 起的区域）。</summary>
        private void RenderTransformed(Graphics g)
        {
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            ApplyTransform(g);
            float dw = sourceImage.Width * scale;
            float dh = sourceImage.Height * scale;
            g.DrawImage(sourceImage, -dw / 2f, -dh / 2f, dw, dh);
            g.Restore(state);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (clickThrough)
                {
                    // WS_EX_TRANSPARENT | WS_EX_LAYERED —— 鼠标点击穿透到下层窗口
                    cp.ExStyle |= 0x20 | 0x80000;
                }
                return cp;
            }
        }

        /// <summary>切换鼠标穿透。穿透时自动降低不透明度便于作为参考叠加层。</summary>
        public void ToggleClickThrough()
        {
            clickThrough = !clickThrough;
            if (clickThrough)
            {
                opacityBeforeClickThrough = Opacity;
                if (Opacity > 0.8) Opacity = 0.8;
            }
            else
            {
                Opacity = opacityBeforeClickThrough;
            }
            RecreateHandle(); // 重建窗口以应用新的 CreateParams ExStyle
            Invalidate();
        }

        public bool IsClickThrough { get { return clickThrough; } }

        /// <summary>把原始图像保存到文件（贴图组持久化用）。</summary>
        public void SaveSourceTo(string path)
        {
            sourceImage.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>当前贴图状态快照（用于贴图组保存）。</summary>
        public PinSnapshot GetSnapshot()
        {
            return new PinSnapshot
            {
                Location = Location,
                Scale = scale,
                Rotation = rotationDegrees,
                SignX = signX,
                SignY = signY,
                Opacity = Opacity,
                Locked = locked,
                ClickThrough = clickThrough,
                TopMost = TopMost
            };
        }

        /// <summary>应用快照状态（用于贴图组恢复）。</summary>
        public void ApplySnapshot(PinSnapshot snap)
        {
            scale = snap.Scale;
            rotationDegrees = snap.Rotation;
            signX = snap.SignX;
            signY = snap.SignY;
            locked = snap.Locked;
            TopMost = snap.TopMost;
            Location = snap.Location;
            ApplySize();
            if (snap.Opacity > 0) Opacity = snap.Opacity;
            if (snap.ClickThrough && !clickThrough) ToggleClickThrough();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (scaleTimer != null) { scaleTimer.Stop(); scaleTimer.Dispose(); }
                if (hintTimer != null) { hintTimer.Stop(); hintTimer.Dispose(); }
                if (sourceImage != null) sourceImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("复制贴图", null, delegate { CopyImage(); });
            menu.Items.Add("保存贴图", null, delegate { SaveImage(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("50%", null, delegate { SetScale(0.5f); });
            menu.Items.Add("100%", null, delegate { SetScale(1.0f); });
            menu.Items.Add("150%", null, delegate { SetScale(1.5f); });
            menu.Items.Add("200%", null, delegate { SetScale(2.0f); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("透明度 60%", null, delegate { Opacity = 0.6; });
            menu.Items.Add("透明度 80%", null, delegate { Opacity = 0.8; });
            menu.Items.Add("透明度 100%", null, delegate { Opacity = 1.0; });
            menu.Items.Add(new ToolStripSeparator());
            var topItem = new ToolStripMenuItem("置顶");
            topItem.Checked = TopMost;
            topItem.CheckOnClick = true;
            topItem.CheckedChanged += delegate { TopMost = topItem.Checked; };
            menu.Items.Add(topItem);
            menu.Items.Add("旋转 ↻ 5°", null, delegate { RotateBy(5f); });
            menu.Items.Add("旋转 ↺ 5°", null, delegate { RotateBy(-5f); });
            menu.Items.Add("旋转 90°", null, delegate { RotateBy(90f); });
            menu.Items.Add("水平翻转", null, delegate { signX = -signX; ApplySize(); Invalidate(); });
            menu.Items.Add("垂直翻转", null, delegate { signY = -signY; Invalidate(); });
            var ctItem = new ToolStripMenuItem("鼠标穿透");
            ctItem.CheckOnClick = true;
            ctItem.Checked = clickThrough;
            ctItem.CheckedChanged += delegate { if (ctItem.Checked != clickThrough) ToggleClickThrough(); };
            menu.Items.Add(ctItem);
            menu.Items.Add(new ToolStripSeparator());
            var lockItem = new ToolStripMenuItem("锁定移动");
            lockItem.CheckOnClick = true;
            lockItem.CheckedChanged += delegate { locked = lockItem.Checked; };
            menu.Items.Add(lockItem);
            menu.Items.Add("关闭", null, delegate { Close(); });
            return menu;
        }

        private void RotateBy(float degrees)
        {
            rotationDegrees += degrees;
            // 吸附到 0/45/90/... 的倍数（接近时）
            float nearest = (float)Math.Round(rotationDegrees / 45.0) * 45f;
            if (Math.Abs(rotationDegrees - nearest) < 3f) rotationDegrees = nearest;
            rotationDegrees = rotationDegrees % 360f;
            if (rotationDegrees < 0) rotationDegrees += 360f;
            ApplySize();
            ShowScaleHint();
            Invalidate();
        }

        private void SaveImage()
        {
            string file = AppPaths.NewImagePath("_pin");
            try
            {
                using (Bitmap rendered = RenderToBitmap())
                {
                    OverlayForm.SaveImageAutoFormat(rendered, file);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "保存贴图失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyImage()
        {
            string error;
            using (Bitmap rendered = RenderToBitmap())
            {
                if (!ClipboardService.TrySetImage(rendered, out error))
                {
                    MessageBox.Show(error, "复制贴图失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>渲染变换后的图像为新 Bitmap（用于保存/复制）。</summary>
        private Bitmap RenderToBitmap()
        {
            Size s = TransformedSize();
            var bmp = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                // RenderTransformed 用窗口中心做变换，这里改用 bmp 中心
                var state = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(s.Width / 2f, s.Height / 2f);
                g.RotateTransform(rotationDegrees);
                g.ScaleTransform(signX, signY);
                float dw = sourceImage.Width * scale;
                float dh = sourceImage.Height * scale;
                g.DrawImage(sourceImage, -dw / 2f, -dh / 2f, dw, dh);
                g.Restore(state);
            }
            return bmp;
        }

        private void SetScale(float value)
        {
            scale = Math.Max(0.2f, Math.Min(4.0f, value));
            ApplySize();
            ShowScaleHint();
            Invalidate();
        }

        private void ShowScaleHint()
        {
            scaleLabel.Text = (scale * 100).ToString("0") + "%  " + Width + "×" + Height + (rotationDegrees != 0 ? "  " + rotationDegrees.ToString("0") + "°" : "");
            scaleLabel.Location = new Point(Math.Max(0, Width / 2 - scaleLabel.Width / 2), Math.Max(0, Height / 2 - scaleLabel.Height / 2));
            scaleLabel.Visible = true;
            scaleLabel.BringToFront();
            scaleTimer.Stop();
            scaleTimer.Start();
        }

        private void ApplySize()
        {
            Size s = TransformedSize();
            Width = s.Width;
            Height = s.Height;
        }

        private Rectangle[] GetHandleRects()
        {
            int half = HandleSize / 2;
            return new Rectangle[]
            {
                new Rectangle(0 - half, 0 - half, HandleSize, HandleSize),                        // TL
                new Rectangle(Width - HandleSize + half, 0 - half, HandleSize, HandleSize),       // TR
                new Rectangle(Width - HandleSize + half, Height - HandleSize + half, HandleSize, HandleSize), // BR
                new Rectangle(0 - half, Height - HandleSize + half, HandleSize, HandleSize)       // BL
            };
        }

        private int HitTestHandle(Point location)
        {
            Rectangle[] handles = GetHandleRects();
            for (int i = 0; i < handles.Length; i++)
            {
                Rectangle inflated = handles[i];
                inflated.Inflate(HandleHitTolerance, HandleHitTolerance);
                if (inflated.Contains(location)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 无损变换式渲染：旋转/翻转通过 Graphics 变换栈，源图永不被破坏
            RenderTransformed(e.Graphics);

            // 边框：悬停时高亮
            bool hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
            int alpha = hovered ? 220 : 100;
            using (var pen = new Pen(Color.FromArgb(alpha, 100, 180, 255), hovered ? 2 : 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            // 锁定标记
            if (locked)
            {
                using (var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.FromArgb(180, 255, 200, 100)))
                {
                    e.Graphics.DrawString("🔒", font, brush, 4, 2);
                }
            }

            // 悬停时显示四角缩放手柄
            if (hovered && !locked)
            {
                Rectangle[] handles = GetHandleRects();
                using (var hBrush = new SolidBrush(Color.White))
                using (var hPen = new Pen(Color.FromArgb(0, 120, 215), 1))
                {
                    for (int i = 0; i < handles.Length; i++)
                    {
                        e.Graphics.FillRectangle(hBrush, handles[i]);
                        e.Graphics.DrawRectangle(hPen, handles[i]);
                    }
                }
            }

            // 首次显示缩放操作提示
            if (showResizeHint)
            {
                using (var hintFont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var hintBg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    string hint = "滚轮缩放 | 拖角调整大小 | R 旋转";
                    SizeF hintSize = e.Graphics.MeasureString(hint, hintFont);
                    float hx = (Width - hintSize.Width) / 2.0f;
                    float hy = Height - hintSize.Height - 8;
                    e.Graphics.FillRectangle(hintBg, hx - 4, hy - 2, hintSize.Width + 8, hintSize.Height + 4);
                    e.Graphics.DrawString(hint, hintFont, Brushes.White, hx, hy);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // 检查四角缩放手柄
            if (e.Button == MouseButtons.Left && !locked)
            {
                int handleIdx = HitTestHandle(e.Location);
                if (handleIdx >= 0)
                {
                    isResizingFromHandle = true;
                    resizeCornerIndex = handleIdx;
                    resizeStartScale = scale;
                    resizeStartMouse = Cursor.Position;
                    return;
                }
            }

            if (e.Button == MouseButtons.Left && !locked)
            {
                dragging = true;
                dragStart = Cursor.Position;
                formStart = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // 四角缩放手柄拖拽
            if (isResizingFromHandle)
            {
                Point current = Cursor.Position;
                float dx = current.X - resizeStartMouse.X;
                float dy = current.Y - resizeStartMouse.Y;

                // 根据拖拽角计算新的缩放
                float delta;
                switch (resizeCornerIndex)
                {
                    case 0: delta = -Math.Max(dx, dy); break; // TL: 向外拉=放大
                    case 1: delta = Math.Max(dx, -dy); break;  // TR
                    case 2: delta = Math.Max(dx, dy); break;   // BR
                    case 3: delta = Math.Max(-dx, dy); break;  // BL
                    default: delta = 0; break;
                }

                float newScale = resizeStartScale * (1.0f + delta / 200.0f);
                SetScale(newScale);
                return;
            }

            if (!dragging) return;
            Point cur = Cursor.Position;
            int newX = formStart.X + cur.X - dragStart.X;
            int newY = formStart.Y + cur.Y - dragStart.Y;

            // 屏幕边缘吸附（10px 内自动吸附）
            const int snapDist = 10;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle bounds = screen.WorkingArea;
                if (Math.Abs(newX - bounds.Left) < snapDist) newX = bounds.Left;
                if (Math.Abs(newX + Width - bounds.Right) < snapDist) newX = bounds.Right - Width;
                if (Math.Abs(newY - bounds.Top) < snapDist) newY = bounds.Top;
                if (Math.Abs(newY + Height - bounds.Bottom) < snapDist) newY = bounds.Bottom - Height;
            }

            Location = new Point(newX, newY);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isResizingFromHandle)
            {
                isResizingFromHandle = false;
                return;
            }
            dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Shift+滚轮：任意角度旋转（5° 增量）
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                RotateBy(e.Delta > 0 ? 5f : -5f);
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                double next = Opacity + (e.Delta > 0 ? 0.05 : -0.05);
                if (next < 0.2) next = 0.2;
                if (next > 1.0) next = 1.0;
                Opacity = next;
                return;
            }

            float oldScale = scale;
            SetScale(scale * (e.Delta > 0 ? 1.1f : 0.9f));
            Point cursor = Cursor.Position;
            float ratio = scale / oldScale;
            Location = new Point(
                (int)Math.Round(cursor.X - (cursor.X - Location.X) * ratio),
                (int)Math.Round(cursor.Y - (cursor.Y - Location.Y) * ratio));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
            if (e.Control && e.KeyCode == Keys.C) CopyImage();
            if (e.Control && e.KeyCode == Keys.S) SaveImage();
            if (e.KeyCode == Keys.Space)
            {
                locked = !locked;
            }
            if (e.KeyCode == Keys.R)
            {
                RotateBy(90f);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }

        /// <summary>拖到不同 DPI 显示器时重算窗口尺寸并清晰重绘（PerMonitorV2）。</summary>
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // 交给基类处理建议的新尺寸/位置，然后按当前 scale 重新计算变换包围盒
            ApplySize();
            Invalidate();
            // 不阻止缩放——让 WinForms 自动调整位置/字体
        }
    }
}
