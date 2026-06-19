using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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

            // 收集荧光笔索引（在 DrawImageSpace 之外，以便单独按图层合成）
            var highlighterIndices = new List<int>();
            for (int i = 0; i < annotations.Count; i++)
            {
                var stroke = annotations[i] as StrokeAnnotation;
                if (stroke != null && stroke.Highlighter)
                    highlighterIndices.Add(i);
            }
            bool hasActiveHighlighter = activeStroke != null && activeStroke.Highlighter;

            // 非荧光笔标注：正常绘制（在图像变换空间内）
            DrawImageSpace(g, delegate(Graphics ig)
            {
                for (int i = 0; i < annotations.Count; i++)
                {
                    var stroke = annotations[i] as StrokeAnnotation;
                    if (stroke != null && stroke.Highlighter) continue;
                    annotations[i].Draw(ig);
                }
                if (activeStroke != null && !activeStroke.Highlighter) activeStroke.Draw(ig);
                if (activeShape != null) activeShape.Draw(ig);
            });

            // 荧光笔图层：渲染到屏幕尺寸离屏位图（不透明），再以荧光笔 alpha 一次合成到外层 g，
            // 避免笔画自交/相交处的色带。变换与 DrawImageSpace 完全一致，保证位置正确。
            if (highlighterIndices.Count > 0 || hasActiveHighlighter)
            {
                foreach (int idx in highlighterIndices)
                    ((StrokeAnnotation)annotations[idx])._forceOpaque = true;
                if (hasActiveHighlighter) activeStroke._forceOpaque = true;

                using (var offBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                {
                    using (var offG = Graphics.FromImage(offBmp))
                    {
                        offG.SmoothingMode = SmoothingMode.AntiAlias;
                        offG.Clear(Color.Transparent);
                        // 与 DrawImageSpace 相同的图像→屏幕变换
                        offG.TranslateTransform(viewCenter.X, viewCenter.Y);
                        offG.ScaleTransform(zoom, zoom);
                        offG.TranslateTransform(-imageCenter.X, -imageCenter.Y);
                        foreach (int idx in highlighterIndices)
                            annotations[idx].Draw(offG);
                        if (hasActiveHighlighter) activeStroke.Draw(offG);
                    }

                    foreach (int idx in highlighterIndices)
                        ((StrokeAnnotation)annotations[idx])._forceOpaque = false;
                    if (hasActiveHighlighter) activeStroke._forceOpaque = false;

                    // 合成到外层 g（屏幕空间，恒等变换），1:1 像素映射
                    using (var attr = new System.Drawing.Imaging.ImageAttributes())
                    {
                        float alpha = 95f / 255f;
                        var cm = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                            new float[] {1, 0, 0, 0, 0},
                            new float[] {0, 1, 0, 0, 0},
                            new float[] {0, 0, 1, 0, 0},
                            new float[] {0, 0, 0, alpha, 0},
                            new float[] {0, 0, 0, 0, 1}
                        });
                        attr.SetColorMatrix(cm);
                        g.DrawImage(offBmp,
                            new Rectangle(0, 0, Width, Height),
                            0, 0, Width, Height,
                            GraphicsUnit.Pixel, attr);
                    }
                }
            }


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

            Bitmap bmp = RenderToBitmap();
            string savedFile = SaveImageAutoFormat(bmp, file);
            CaptureStore.Add(bmp);
            PlayCaptureSound();

            ShowToast("已保存截图", savedFile);
        }

        private void CopyToClipboard()
        {
            CommitTextInput(true);
            Bitmap bmp = RenderToBitmap();
            string error;
            if (!ClipboardService.TrySetImage(bmp, out error))
            {
                bmp.Dispose();
                ShowToast("复制失败", error);
                return;
            }
            CaptureStore.Add(bmp);
            PlayCaptureSound();
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

        /// <summary>只渲染指定屏幕区域，避免为小选区分配整个虚拟屏幕的 Bitmap。</summary>
        private Bitmap RenderRegion(Rectangle rect)
        {
            var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TranslateTransform(-rect.X, -rect.Y);
                RenderScene(g, false, true);
            }
            return bmp;
        }

        private void ShowToast(string title, string message)
        {
            ShowToast(title, message, string.IsNullOrEmpty(title) ? 800 : 1500);
        }

        private void ShowToast(string title, string message, int durationMs)
        {
            if (activeToastTimer != null) { activeToastTimer.Stop(); activeToastTimer.Dispose(); activeToastTimer = null; }
            if (activeToast != null && !activeToast.IsDisposed)
            {
                // 被新 Toast 取代时直接关闭（不带动画，避免视觉干扰）
                activeToast.Close();
                activeToast.Dispose();
                activeToast = null;
            }

            string text = title;
            if (!string.IsNullOrEmpty(message)) text += "\n" + message;
            activeToast = new ToastForm(text, Bounds);
            activeToast.Show(this);
            activeToastTimer = new System.Windows.Forms.Timer();
            activeToastTimer.Interval = durationMs;
            var capturedToast = activeToast;
            var capturedTimer = activeToastTimer;
            activeToastTimer.Tick += delegate
            {
                capturedTimer.Stop();
                capturedTimer.Dispose();
                if (capturedToast == activeToast) activeToast = null;
                if (capturedTimer == activeToastTimer) activeToastTimer = null;
                if (!capturedToast.IsDisposed)
                {
                    // 触发淡出动画，淡出完成后 ToastForm 自行关闭
                    capturedToast.BeginFadeOut();
                }
            };
            activeToastTimer.Start();
        }

        private void OnCountdownTick(object sender, EventArgs e)
        {
            if (!countdownRunning) return;
            if (countdownSeconds > 0)
            {
                countdownSeconds--;
                Invalidate();
                if (countdownSeconds == 0)
                {
                    // 倒计时结束：播放提示音
                    countdownRunning = false;
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                    ShowToast("⏰ 时间到！", "按 Space 继续  R 重置");
                }
            }
        }

        /// <summary>根据设置或自动判断保存格式。返回实际保存的文件路径。</summary>
        internal static string SaveImageAutoFormat(Bitmap bmp, string pngPath)
        {
            AppSettings prefs = SettingsStore.Load();
            int fmt = prefs.SaveFormat;

            if (fmt == 1)
            {
                // 强制 PNG
                bmp.Save(pngPath, ImageFormat.Png);
                return pngPath;
            }
            else if (fmt == 2)
            {
                // 强制 JPG
                string jpgPath = Path.ChangeExtension(pngPath, ".jpg");
                using (var encoderParams = new EncoderParameters(1))
                {
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                    ImageCodecInfo jpegCodec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
                    if (jpegCodec != null)
                    {
                        bmp.Save(jpgPath, jpegCodec, encoderParams);
                        return jpgPath;
                    }
                }
                bmp.Save(pngPath, ImageFormat.Png);
                return pngPath;
            }
            else if (fmt == 3)
            {
                // 强制 BMP
                string bmpPath = Path.ChangeExtension(pngPath, ".bmp");
                bmp.Save(bmpPath, ImageFormat.Bmp);
                return bmpPath;
            }

            // 自动模式：大截图（>400万像素）保存为 JPG，否则 PNG
            long pixels = (long)bmp.Width * bmp.Height;
            if (pixels > 4000000)
            {
                string jpgPath = Path.ChangeExtension(pngPath, ".jpg");
                using (var encoderParams = new EncoderParameters(1))
                {
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                    ImageCodecInfo jpegCodec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
                    if (jpegCodec != null)
                    {
                        bmp.Save(jpgPath, jpegCodec, encoderParams);
                        return jpgPath;
                    }
                }
            }
            bmp.Save(pngPath, ImageFormat.Png);
            return pngPath;
        }

        /// <summary>截图完成时播放系统音效。</summary>
        private static void PlayCaptureSound()
        {
            System.Media.SystemSounds.Asterisk.Play();
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
            EnsureTimerResources();
            g.FillRectangle(timerBgBrush, 0, 0, Width, Height);

            int minutes = countdownSeconds / 60;
            int seconds = countdownSeconds % 60;
            string time = minutes.ToString("00") + ":" + seconds.ToString("00");
            SizeF size = g.MeasureString(time, timerBigFont);
            float x = (Width - size.Width) / 2.0f;
            float y = (Height - size.Height) / 2.0f - 20;

            // 颜色逻辑：最后 30 秒渐变为橙红 + 脉冲闪烁
            Color timeColor;
            if (countdownSeconds == 0)
            {
                timeColor = Color.OrangeRed;
            }
            else if (countdownSeconds <= 30 && countdownRunning)
            {
                // 从白色渐变到橙色
                float t = 1.0f - countdownSeconds / 30.0f; // 0→1 随时间减少
                int r = 255;
                int green = (int)(255 * (1 - t));  // 255→0
                int b = (int)(255 * (1 - t));       // 255→0
                // 脉冲：用正弦波让亮度在 0.7~1.0 之间波动
                float pulse = 0.85f + 0.15f * (float)Math.Sin(Environment.TickCount / 200.0 * Math.PI);
                timeColor = Color.FromArgb((int)(255 * pulse), r, green, b);
            }
            else
            {
                timeColor = Color.White;
            }

            using (var brush = new SolidBrush(timeColor))
                g.DrawString(time, timerBigFont, brush, x, y);
            string hint = countdownRunning ? "Space 暂停  1/3/5/10/15/45 分钟预设  Up/Down 调整  R 重置" : "已暂停  Space 继续";
            SizeF hintSize = g.MeasureString(hint, timerSmallFont);
            g.DrawString(hint, timerSmallFont, Brushes.WhiteSmoke, (Width - hintSize.Width) / 2.0f, y + size.Height + 10);
        }

        private void DrawHelp(Graphics g)
        {
            EnsureHelpResources();
            float rw = 740, rh = 560;
            float rx = Width / 2.0f - rw / 2, ry = Height / 2.0f - rh / 2;
            RectangleF rect = new RectangleF(rx, ry, rw, rh);
            int cr = 14;

            // 圆角背景
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddArc(rx, ry, cr * 2, cr * 2, 180, 90);
                path.AddArc(rx + rw - cr * 2, ry, cr * 2, cr * 2, 270, 90);
                path.AddArc(rx + rw - cr * 2, ry + rh - cr * 2, cr * 2, cr * 2, 0, 90);
                path.AddArc(rx, ry + rh - cr * 2, cr * 2, cr * 2, 90, 90);
                path.CloseFigure();
                g.FillPath(helpBgBrush, path);
                g.DrawPath(helpBorderPen, path);
            }

            string help =
                "P 画笔    H 荧光笔    L 直线    A 箭头\n" +
                "R 矩形    O 椭圆      V 遮罩    M 编号\n" +
                "X 模糊笔  T 文字      E 橡皮    I 印章\n" +
                "D 测距    W 白板      K 黑板    Shift+T 右对齐\n\n" +
                "I 再按切换印章: ★✓✗●▲♥    F 矩形/椭圆填充\n" +
                "右键按住：临时橡皮    中键/Alt+左键拖动：平移\n" +
                "1-9 切换颜色    0 自定义颜色    鼠标滚轮 调整线宽\n" +
                "Shift 拖动：锁角直线    Ctrl 拖动：矩形\n" +
                "Ctrl+Shift 拖动：箭头    Ctrl+滚轮：缩放\n" +
                "方向键：平移画面    Home：回到中心\n\n" +
                "Ctrl+Z 撤销    Ctrl+Y / Ctrl+Shift+Z 重做    C 清屏\n" +
                "S 保存截图    Ctrl+C 复制    Ctrl+Shift+C/S 裁剪\n" +
                "Esc 关闭    F1 / ? 显示此帮助\n\n" +
                "文字：点击输入，Enter 换行，Ctrl+Enter 确认\n" +
                "计时器：Space 暂停/继续，1-6 设定时长\n" +
                "聚光灯：滚轮调半径，Shift+滚轮调暗度";

            g.DrawString("⌨  操作帮助", helpTitleFont, Brushes.White, rect.X + 28, rect.Y + 22);
            g.DrawLine(helpSepPen, rect.X + 28, rect.Y + 62, rect.X + rect.Width - 28, rect.Y + 62);
            g.DrawString(help, helpBodyFont, Brushes.WhiteSmoke, new RectangleF(rect.X + 30, rect.Y + 72, rect.Width - 60, rect.Height - 100));
        }

        private void DrawHud(Graphics g)
        {
            EnsureHudResources();
            string status = GetStatusText();
            SizeF size = g.MeasureString(status, hudFont);
            RectangleF rect = new RectangleF(16, 16, size.Width + 24, size.Height + 16);
            g.FillRectangle(hudBgBrush, rect);
            g.DrawRectangle(hudBorderPen, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawString(status, hudFont, Brushes.White, rect.X + 12, rect.Y + 8);

            g.FillEllipse(colorBrush, Width - 52, 20, 28, 28);
            g.DrawEllipse(colorBorderPen, Width - 52, 20, 28, 28);
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

            int ti = (int)tool;
            string name = (ti >= 0 && ti < DrawingToolNames.Length) ? DrawingToolNames[ti] : "画笔";

            if (tool == DrawingTool.Stamp)
            {
                int si = (int)currentStampType;
                name = "印章 " + (si < StampAnnotation.StampChars.Length ? StampAnnotation.StampChars[si].ToString() : "★");
            }

            string fillIndicator = "";
            if (shapeFilled && (tool == DrawingTool.Rectangle || tool == DrawingTool.Ellipse))
            {
                fillIndicator = " [填充]";
            }

            string surface = "";
            if (mode == OverlayMode.Whiteboard) surface = " 白板";
            if (mode == OverlayMode.Blackboard) surface = " 黑板";
            return name + fillIndicator + surface + "  宽度 " + currentWidth.ToString("0") + "  缩放 " + zoom.ToString("0.0") + "x";
        }
    }
}

