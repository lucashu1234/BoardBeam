using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>
    /// 主窗口仪表盘：功能按钮矩阵 + 最近截图 + 快捷入口。
    /// 关闭=隐藏到托盘（不退出应用），由托盘双击/热键/菜单唤起。
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly PresenterApplicationContext owner;
        private FlowLayoutPanel thumbPanel;
        private FlowLayoutPanel cardGrid;

        public MainForm(PresenterApplicationContext owner)
        {
            this.owner = owner;
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "BoardBeam 控制面板";
            Width = 760;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(680, 460);
            BackColor = Color.FromArgb(245, 246, 248);
            DoubleBuffered = true;
            Icon = SystemIcons.Application;

            BuildHeader();
            BuildBody();
            BuildFooter();
            Load += delegate { ScaleCardsForDpi(); RefreshThumbnails(); DpiScale.CenterOnActiveMonitor(this); };
            VisibleChanged += delegate { if (Visible) RefreshThumbnails(); };
        }

        /// <summary>按当前 DPI 缩放卡片面板尺寸（容器随内容一起放大，避免高 DPI 下文字裁切）。</summary>
        private void ScaleCardsForDpi()
        {
            float dp = DpiScale.Factor(Handle);
            if (dp <= 1.001f) return;  // 100% DPI 无需调整
            int w = DpiScale.Scale(180, dp);
            int h = DpiScale.Scale(96, dp);
            if (cardGrid != null)
            {
                foreach (Control c in cardGrid.Controls)
                {
                    if (c is Panel) { c.Width = w; c.Height = h; }
                }
            }
            if (thumbPanel != null) thumbPanel.Width = DpiScale.Scale(220, dp);
        }

        // ===== 顶部标题栏 =====
        private void BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(34, 120, 200) };
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                float dp = DpiScale.Factor(e.Graphics);
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var f = new Font(FontFamily.GenericSansSerif, DpiScale.ScaleF(22, dp), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Color.White))
                    e.Graphics.DrawString("BoardBeam 控制面板", f, b, DpiScale.Scale(20, dp), DpiScale.Scale(14, dp));
                using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near })
                using (var f2 = new Font(FontFamily.GenericSansSerif, DpiScale.ScaleF(11, dp), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var b2 = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                    e.Graphics.DrawString("截图 · 贴图 · 标注 · 录屏 · 取色", f2, b2, new RectangleF(0, 0, header.Width - DpiScale.Scale(20, dp), header.Height), sf);
            };
            Controls.Add(header);
        }

        // ===== 主体：左侧按钮网格 + 右侧最近截图 =====
        private void BuildBody()
        {
            // 用 TableLayoutPanel 避免 SplitContainer 的 SplitterDistance 布局期异常
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(245, 246, 248),
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 左：功能按钮网格（2 列）
            var grid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.FromArgb(245, 246, 248),
            };
            grid.Controls.Add(MakeCard("截图", "框选区域标注", "✂", Color.FromArgb(231, 76, 60), () => owner.ShowOverlay(OverlayMode.PixPinCapture)));
            grid.Controls.Add(MakeCard("贴图", "截图并钉到桌面", "📌", Color.FromArgb(46, 134, 193), () => owner.QuickSnipAndPin()));
            grid.Controls.Add(MakeCard("录屏", "录制区域 GIF", "●", Color.FromArgb(192, 57, 43), () => owner.ToggleRecording()));
            grid.Controls.Add(MakeCard("取色", "屏幕像素取色", "🎨", Color.FromArgb(142, 68, 173), () => owner.ShowColorPicker()));
            grid.Controls.Add(MakeCard("OCR", "识别屏幕文字", "A", Color.FromArgb(39, 174, 96), () => OcrTool.ShowOcrCapture(owner)));
            grid.Controls.Add(MakeCard("滚动截图", "长网页长截图", "↕", Color.FromArgb(52, 152, 219), () => owner.ShowOverlay(OverlayMode.ScrollingCapture)));
            grid.Controls.Add(MakeCard("计时器", "课堂倒计时", "⏱", Color.FromArgb(211, 84, 0), () => owner.ShowOverlay(OverlayMode.Timer)));
            grid.Controls.Add(MakeCard("白板", "全屏白板批注", "▦", Color.FromArgb(100, 100, 110), () => owner.ShowOverlay(OverlayMode.Whiteboard)));
            body.Controls.Add(grid, 0, 0);
            cardGrid = grid;

            // 右：最近截图
            var right = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(240, 241, 244),
            };
            var title = new Label
            {
                Text = "最近截图（双击贴图）",
                AutoSize = true,
                Margin = new Padding(4, 4, 4, 8),
                Font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold, GraphicsUnit.Pixel),
                ForeColor = Color.FromArgb(60, 60, 70),
            };
            right.Controls.Add(title);
            thumbPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Width = 220,
            };
            right.Controls.Add(thumbPanel);
            body.Controls.Add(right, 1, 0);

            Controls.Add(body);
            Controls.SetChildIndex(body, 0);
        }

        // ===== 底部动作栏 =====
        private void BuildFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(16, 8, 16, 8),
                BackColor = Color.FromArgb(235, 236, 240),
                WrapContents = false,
            };
            footer.Controls.Add(MakeFooterButton("命令面板", () => owner.ShowCommandPalette()));
            footer.Controls.Add(MakeFooterButton("抓取当前屏", () => owner.CaptureActiveMonitor()));
            footer.Controls.Add(MakeFooterButton("剪贴板历史", () => owner.ShowClipboardHistory()));
            footer.Controls.Add(MakeFooterButton("截图历史", () => owner.ShowCaptureHistory()));
            footer.Controls.Add(MakeFooterButton("设置", () => owner.ShowSettings()));
            footer.Controls.Add(MakeFooterButton("退出", () => owner.Exit(), true));
            Controls.Add(footer);
        }

        private Control MakeCard(string title, string sub, string symbol, Color accent, Action onClick)
        {
            // 基准尺寸（96DPI），实际尺寸在 OnLoad 按当前 DPI 缩放
            const int baseW = 180, baseH = 96;
            var panel = new Panel { Width = baseW, Height = baseH, Margin = new Padding(6), Cursor = Cursors.Hand };
            bool hovered = false;
            panel.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                float dp = DpiScale.Factor(g);  // 当前绘制 DPI 因子
                int w = DpiScale.Scale(baseW, dp);
                int h = DpiScale.Scale(baseH, dp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, w - 1, h - 1);
                // 卡片背景
                using (var bg = new SolidBrush(hovered ? Color.White : Color.FromArgb(255, 252, 252, 254)))
                    CardBackground(g, bounds, bg);
                int bar = DpiScale.Scale(6, dp);
                int circleSize = DpiScale.Scale(40, dp);
                int circleX = DpiScale.Scale(18, dp);
                int circleY = (h - circleSize) / 2;
                // 左侧色条
                using (var accentBrush = new SolidBrush(accent))
                    g.FillRectangle(accentBrush, 0, 0, bar, h);
                // 符号圆
                using (var circle = new SolidBrush(accent))
                    g.FillEllipse(circle, circleX, circleY, circleSize, circleSize);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var symFont = new Font(FontFamily.GenericSansSerif, DpiScale.ScaleF(20, dp), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var symBrush = new SolidBrush(Color.White))
                    g.DrawString(symbol, symFont, symBrush, new RectangleF(circleX, circleY, circleSize, circleSize), sf);
                // 标题/副标题
                int textX = DpiScale.Scale(68, dp);
                using (var tf = new Font(FontFamily.GenericSansSerif, DpiScale.ScaleF(16, dp), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf2 = new Font(FontFamily.GenericSansSerif, DpiScale.ScaleF(11, dp), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(Color.FromArgb(45, 45, 55)))
                using (var sb = new SolidBrush(Color.FromArgb(130, 130, 140)))
                {
                    g.DrawString(title, tf, tb, textX, DpiScale.Scale(22, dp));
                    g.DrawString(sub, sf2, sb, textX, DpiScale.Scale(48, dp));
                }
                // hover 边框
                if (hovered)
                {
                    using (var pen = new Pen(Color.FromArgb(60, accent), 1.5f))
                        CardBorder(g, bounds, pen);
                }
            };
            panel.MouseEnter += delegate { hovered = true; panel.Invalidate(); };
            panel.MouseLeave += delegate { hovered = false; panel.Invalidate(); };
            panel.Click += delegate { try { Hide(); onClick(); } catch (Exception ex) { CrashLogger.Log("主面板按钮", ex); } };
            return panel;
        }

        private static void CardBackground(Graphics g, Rectangle bounds, Brush bg)
        {
            using (var path = RoundRect(bounds, 8))
                g.FillPath(bg, path);
        }
        private static void CardBorder(Graphics g, Rectangle bounds, Pen pen)
        {
            using (var path = RoundRect(bounds, 8))
                g.DrawPath(pen, path);
        }
        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private Button MakeFooterButton(string text, Action onClick, bool danger = false)
        {
            var b = new Button
            {
                Text = text,
                Width = 104,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Regular, GraphicsUnit.Pixel),
                BackColor = danger ? Color.FromArgb(231, 76, 60) : Color.White,
                ForeColor = danger ? Color.White : Color.FromArgb(60, 60, 70),
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 0, 4, 0),
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 215);
            b.Click += delegate { try { onClick(); } catch (Exception ex) { CrashLogger.Log("主面板底部按钮", ex); } };
            return b;
        }

        // ===== 最近截图缩略图 =====
        private void RefreshThumbnails()
        {
            if (thumbPanel == null) return;
            thumbPanel.Controls.Clear();
            List<Bitmap> all = CaptureStore.GetAll();
            int shown = 0;
            for (int i = 0; i < all.Count && shown < 8; i++)
            {
                Bitmap full = all[i];
                Bitmap thumb = MakeThumb(full, 200, 120);
                int capturedIndex = i;
                var box = new PictureBox
                {
                    Image = thumb,
                    Width = 200,
                    Height = 120,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 0, 0, 8),
                    Cursor = Cursors.Hand,
                };
                box.DoubleClick += delegate
                {
                    try
                    {
                        using (Bitmap src = (Bitmap)all[capturedIndex].Clone())
                        {
                            Point cursor = Cursor.Position;
                            PinManager.Show(src, new Point(cursor.X + 24, cursor.Y + 24), true);
                        }
                        Hide();
                    }
                    catch (Exception ex) { CrashLogger.Log("主面板贴图", ex); }
                };
                thumbPanel.Controls.Add(box);
                shown++;
            }
            foreach (Bitmap b in all) b.Dispose(); // RefreshThumbnails 用的是克隆，可释放
            if (shown == 0)
            {
                var empty = new Label
                {
                    Text = "还没有截图。\n点左侧「截图」开始。",
                    AutoSize = true,
                    ForeColor = Color.FromArgb(150, 150, 160),
                    Font = new Font(FontFamily.GenericSansSerif, 12, GraphicsUnit.Pixel),
                };
                thumbPanel.Controls.Add(empty);
            }
        }

        private static Bitmap MakeThumb(Bitmap src, int maxW, int maxH)
        {
            float ratio = Math.Min((float)maxW / src.Width, (float)maxH / src.Height);
            if (ratio > 1) ratio = 1;
            int w = Math.Max(1, (int)(src.Width * ratio));
            int h = Math.Max(1, (int)(src.Height * ratio));
            var t = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return t;
        }

        /// <summary>关闭按钮/Alt+F4 → 隐藏到托盘，不退出应用；仅应用退出/关机时真正关闭。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.ApplicationExitCall || e.CloseReason == CloseReason.WindowsShutDown)
            {
                base.OnFormClosing(e);
                return;
            }
            // 用户主动关闭或其它原因：隐藏而非退出
            e.Cancel = true;
            Hide();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
        }
    }
}
