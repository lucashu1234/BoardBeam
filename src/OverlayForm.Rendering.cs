using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed partial class OverlayForm
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RenderScene(e.Graphics, true);
        }

        private void RenderScene(Graphics g, bool includeHud)
        {
            RenderScene(g, includeHud, false);
        }

        private void RenderScene(Graphics g, bool includeHud, bool forceCapturedBackground)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = zoom > 1.01f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;

            if (mode == OverlayMode.Whiteboard)
            {
                g.Clear(Color.White);
            }
            else if (mode == OverlayMode.Blackboard)
            {
                g.Clear(Color.FromArgb(24, 32, 28));
            }
            else if (liveBackground && !forceCapturedBackground)
            {
                g.Clear(TransparencyKey);
            }
            else
            {
                DrawImageSpace(g, delegate(Graphics ig)
                {
                    ig.DrawImage(background, 0, 0, background.Width, background.Height);
                });
            }

            DrawImageSpace(g, delegate(Graphics ig)
            {
                for (int i = 0; i < annotations.Count; i++)
                {
                    annotations[i].Draw(ig);
                }

                if (activeStroke != null) activeStroke.Draw(ig);
                if (activeShape != null) activeShape.Draw(ig);
            });

            if (mode == OverlayMode.Spotlight)
            {
                DrawSpotlight(g);
            }

            if (mode == OverlayMode.Timer)
            {
                DrawTimer(g);
            }

            if (includeHud)
            {
                if (showHelp)
                {
                    DrawHelp(g);
                }
                DrawSelection(g);
                DrawHud(g);
            }
        }

        private delegate void DrawWithGraphics(Graphics g);

        private void DrawImageSpace(Graphics g, DrawWithGraphics draw)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(viewCenter.X, viewCenter.Y);
            g.ScaleTransform(zoom, zoom);
            g.TranslateTransform(-imageCenter.X, -imageCenter.Y);
            draw(g);
            g.Restore(state);
        }

        private void SaveScreenshot()
        {
            CommitTextInput(true);
            string file = AppPaths.NewImagePath("");

            using (Bitmap bmp = RenderToBitmap())
            {
                CaptureStore.Add(bmp);
                bmp.Save(file, ImageFormat.Png);
            }

            ShowToast("已保存截图", file);
        }

        private void CopyToClipboard()
        {
            CommitTextInput(true);
            using (Bitmap bmp = RenderToBitmap())
            {
                CaptureStore.Add(bmp);
                string error;
                if (!ClipboardService.TrySetImage(bmp, out error))
                {
                    ShowToast("复制失败", error);
                    return;
                }
            }
            ShowToast("已复制到剪贴板", "");
        }

        private Bitmap RenderToBitmap()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                RenderScene(g, false, true);
            }
            return bmp;
        }

        private void ShowToast(string title, string message)
        {
            string text = title;
            if (!string.IsNullOrEmpty(message)) text += "\n" + message;
            var toast = new ToastForm(text, Bounds);
            toast.Show(this);
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 1200;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                if (!toast.IsDisposed)
                {
                    toast.Close();
                    toast.Dispose();
                }
            };
            timer.Start();
        }

        private void OnCountdownTick(object sender, EventArgs e)
        {
            if (!countdownRunning) return;
            if (countdownSeconds > 0)
            {
                countdownSeconds--;
                Invalidate();
            }
        }

        private void DrawSpotlight(Graphics g)
        {
            using (var path = new GraphicsPath())
            {
                path.FillMode = FillMode.Alternate;
                path.AddRectangle(new Rectangle(0, 0, Width, Height));
                path.AddEllipse(spotlightPoint.X - spotlightRadius, spotlightPoint.Y - spotlightRadius, spotlightRadius * 2, spotlightRadius * 2);
                using (var brush = new SolidBrush(Color.FromArgb(spotlightOpacity, 0, 0, 0)))
                {
                    g.FillPath(brush, path);
                }
            }

            using (var brush = new SolidBrush(Color.FromArgb(220, Color.Red)))
            {
                g.FillEllipse(brush, spotlightPoint.X - 8, spotlightPoint.Y - 8, 16, 16);
            }
        }

        private void DrawTimer(Graphics g)
        {
            using (var brush = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
            {
                g.FillRectangle(brush, 0, 0, Width, Height);
            }

            int minutes = countdownSeconds / 60;
            int seconds = countdownSeconds % 60;
            string time = minutes.ToString("00") + ":" + seconds.ToString("00");
            using (var font = new Font(FontFamily.GenericSansSerif, 132, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(countdownSeconds == 0 ? Color.OrangeRed : Color.White))
            {
                SizeF size = g.MeasureString(time, font);
                float x = (Width - size.Width) / 2.0f;
                float y = (Height - size.Height) / 2.0f - 20;
                g.DrawString(time, font, brush, x, y);
                string hint = countdownRunning ? "Space 暂停  1/3/5/10/15/45 分钟预设  Up/Down 调整  R 重置" : "已暂停  Space 继续";
                SizeF hintSize = g.MeasureString(hint, small);
                g.DrawString(hint, small, Brushes.WhiteSmoke, (Width - hintSize.Width) / 2.0f, y + size.Height + 10);
            }
        }

        private void DrawHelp(Graphics g)
        {
            RectangleF rect = new RectangleF(Width / 2.0f - 360, Height / 2.0f - 260, 720, 520);
            using (var bg = new SolidBrush(Color.FromArgb(225, 20, 22, 26)))
            using (var border = new Pen(Color.FromArgb(130, Color.White)))
            {
                g.FillRectangle(bg, rect);
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            }

            string help =
                "BoardBeam 快捷键\n\n" +
                "P 画笔    H 荧光笔    L 直线    A 箭头\n" +
                "R 矩形    O 椭圆      V 遮罩    M 编号\n" +
                "X 模糊笔  T 文字      E 橡皮    W 白板\n" +
                "K 黑板    Shift+T 右对齐文字\n" +
                "右键按住：临时橡皮    中键/Alt+左键拖动：平移\n" +
                "1-8 切换颜色    鼠标滚轮/+/- 调整线宽\n" +
                "Shift 拖动：直线锁角    Ctrl 拖动：矩形    Ctrl+Shift：箭头\n" +
                "Ctrl+滚轮：缩放    方向键：移动缩放画面\n" +
                "Ctrl+Z 撤销    Ctrl+Y 重做    C 清屏\n" +
                "S 保存截图    Ctrl+C 复制画面    Ctrl+Shift+C/S 裁剪\n" +
                "F1/? 显示或隐藏此帮助\n\n" +
                "文字：点击输入，Enter 换行，Ctrl+Enter 确认。\n" +
                "计时器：Space 暂停，1-6 选择 1/3/5/10/15/45 分钟。\n" +
                "聚光灯：滚轮或 +/- 调半径，Shift+滚轮调暗度。";

            using (var titleFont = new Font(FontFamily.GenericSansSerif, 30, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bodyFont = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("操作帮助", titleFont, Brushes.White, rect.X + 28, rect.Y + 24);
                g.DrawString(help, bodyFont, Brushes.WhiteSmoke, new RectangleF(rect.X + 30, rect.Y + 78, rect.Width - 60, rect.Height - 104));
            }
        }

        private void DrawHud(Graphics g)
        {
            string status = GetStatusText();
            using (var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF size = g.MeasureString(status, font);
                RectangleF rect = new RectangleF(16, 16, size.Width + 24, size.Height + 16);
                using (var bg = new SolidBrush(Color.FromArgb(160, 20, 20, 20)))
                {
                    g.FillRectangle(bg, rect);
                }
                using (var pen = new Pen(Color.FromArgb(80, Color.White)))
                {
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                g.DrawString(status, font, Brushes.White, rect.X + 12, rect.Y + 8);
            }

            using (var brush = new SolidBrush(currentColor))
            {
                g.FillEllipse(brush, Width - 52, 20, 28, 28);
            }
            using (var pen = new Pen(Color.White, 2))
            {
                g.DrawEllipse(pen, Width - 52, 20, 28, 28);
            }
        }

        private string GetStatusText()
        {
            if (mode == OverlayMode.Timer) return "计时器";
            if (mode == OverlayMode.Spotlight) return "聚光灯 / 激光笔";
            if (mode == OverlayMode.LiveDraw) return "LiveDraw 透明批注";
            if (mode == OverlayMode.RegionCopy) return "区域截图：复制到剪贴板";
            if (mode == OverlayMode.RegionSave) return "区域截图：保存文件";
            if (mode == OverlayMode.Recording) return "区域 GIF 录屏：拖选范围";
            if (mode == OverlayMode.ScrollingCapture) return "滚动长截图：拖选滚动区域";
            if (selectionAction != SelectionAction.None) return "拖选截图区域";

            string name = "画笔";
            if (tool == DrawingTool.Highlighter) name = "荧光笔";
            if (tool == DrawingTool.Line) name = "直线";
            if (tool == DrawingTool.Arrow) name = "箭头";
            if (tool == DrawingTool.Rectangle) name = "矩形";
            if (tool == DrawingTool.Ellipse) name = "椭圆";
            if (tool == DrawingTool.Cover) name = "答案遮罩";
            if (tool == DrawingTool.NumberMarker) name = "编号标记";
            if (tool == DrawingTool.Blur) name = "模糊笔";
            if (tool == DrawingTool.Eraser) name = "橡皮";
            if (tool == DrawingTool.Text) name = "文字";

            string surface = "";
            if (mode == OverlayMode.Whiteboard) surface = " 白板";
            if (mode == OverlayMode.Blackboard) surface = " 黑板";
            return name + surface + "  宽度 " + currentWidth.ToString("0") + "  缩放 " + zoom.ToString("0.0") + "x";
        }
    }
}

