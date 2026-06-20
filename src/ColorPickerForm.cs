using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>
    /// 独立屏幕取色器：跟随光标显示放大镜 + HEX/RGB/HSB 读数，
    /// 点击取色并复制 HEX；保留最近 16 个颜色历史。
    /// 比 Snipaste 更好：取色器内直接显示颜色历史色板，并可一键复制多种格式。
    /// </summary>
    internal sealed class ColorPickerForm : Form
    {
        private const int GridSize = 9;       // 放大镜像素网格（奇数，中心为光标像素）
        private const int CellSize = 16;
        private static readonly int Pad = 8;
        private const int SwatchSize = 22;
        private const int SwatchSpacing = 4;

        private readonly Timer timer;
        private Color centerColor;
        private Color[] gridPixels = new Color[GridSize * GridSize];
        private List<Color> history = new List<Color>();

        public ColorPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;

            int boxW = Pad * 2 + GridSize * CellSize;
            int boxH = Pad * 2 + GridSize * CellSize + 36 + SwatchSize + SwatchSpacing;
            Width = boxW;
            Height = boxH;

            // 加载颜色历史
            AppSettings prefs = SettingsStore.Load();
            for (int i = 0; i < prefs.ColorHistory.Count; i++) history.Add(Color.FromArgb(prefs.ColorHistory[i]));

            timer = new Timer { Interval = 30 };
            timer.Tick += delegate { UpdatePositionAndSample(); };
            timer.Start();

            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 排除自身出屏幕捕获，确保 CopyFromScreen 取到的是底层像素而非本窗体
            try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null) { timer.Stop(); timer.Dispose(); }
            base.Dispose(disposing);
        }

        private void UpdatePositionAndSample()
        {
            Point cursor = Cursor.Position;
            // 让放大镜中心对准光标，框体偏移避免遮挡
            int left = cursor.X - Width / 2;
            int top = cursor.Y - Height - 24;
            if (top < 0) top = cursor.Y + 24;
            Location = new Point(left, top);

            SampleAt(cursor);
            Invalidate();
        }

        private void SampleAt(Point screen)
        {
            try
            {
                // 窗体在 OnShown 中已通过 SetWindowDisplayAffinity 排除出屏幕捕获，
                // 因此 CopyFromScreen 不会捕获到本窗体，取色准确。
                using (var bmp = new Bitmap(GridSize, GridSize, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(screen.X - GridSize / 2, screen.Y - GridSize / 2, 0, 0, new Size(GridSize, GridSize));
                    for (int y = 0; y < GridSize; y++)
                        for (int x = 0; x < GridSize; x++)
                            gridPixels[y * GridSize + x] = bmp.GetPixel(x, y);
                }
                centerColor = gridPixels[(GridSize / 2) * GridSize + (GridSize / 2)];
            }
            catch
            {
                // 截屏失败（如安全桌面），保持上一次读数
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 背景
            using (var bg = new SolidBrush(Color.FromArgb(235, 24, 26, 30)))
                g.FillRectangle(bg, 0, 0, Width, Height);
            using (var border = new Pen(Color.FromArgb(120, 120, 180, 255), 1.5f))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            // 像素网格
            int gx = Pad, gy = Pad;
            for (int y = 0; y < GridSize; y++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    Color c = gridPixels[y * GridSize + x];
                    using (var brush = new SolidBrush(c))
                        g.FillRectangle(brush, gx + x * CellSize, gy + y * CellSize, CellSize - 1, CellSize - 1);
                }
            }
            // 中心像素高亮
            int ccx = gx + (GridSize / 2) * CellSize;
            int ccy = gy + (GridSize / 2) * CellSize;
            using (var hp = new Pen(Color.White, 2))
                g.DrawRectangle(hp, ccx - 1, ccy - 1, CellSize + 1, CellSize + 1);

            // 读数行
            int infoY = gy + GridSize * CellSize + 6;
            string hex = "#" + centerColor.R.ToString("X2") + centerColor.G.ToString("X2") + centerColor.B.ToString("X2");
            float[] hsb = RgbToHsb(centerColor);
            string rgb = string.Format("RGB({0},{1},{2})", centerColor.R, centerColor.G, centerColor.B);
            string hsbText = string.Format("HSB({0:0}°,{1:0}%,{2:0}%)", hsb[0], hsb[1] * 100, hsb[2] * 100);

            using (var font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                // 色块
                using (var sw = new SolidBrush(centerColor))
                    g.FillRectangle(sw, Pad, infoY, 16, 16);
                g.DrawString(hex + "  " + rgb, font, Brushes.White, Pad + 20, infoY + 1);
                g.DrawString(hsbText, font, Brushes.LightGray, Pad, infoY + 16);
            }

            // 颜色历史色板
            int sY = infoY + 36;
            for (int i = 0; i < history.Count && i < 16; i++)
            {
                using (var brush = new SolidBrush(history[i]))
                    g.FillRectangle(brush, Pad + i * (SwatchSize + SwatchSpacing), sY, SwatchSize, SwatchSize);
            }
            if (history.Count == 0)
            {
                using (var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular, GraphicsUnit.Pixel))
                    g.DrawString("点击取色（自动复制 HEX）· Shift 复制 RGB · Esc 取消", font, Brushes.Gray, Pad, sY + 4);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Pick();
        }

        private void Pick()
        {
            // 写入历史（去重，最新在前）
            int argb = centerColor.ToArgb();
            history.RemoveAll(c => c.ToArgb() == argb);
            history.Insert(0, centerColor);
            if (history.Count > 16) history.RemoveRange(16, history.Count - 16);

            // 持久化
            AppSettings prefs = SettingsStore.Load();
            prefs.ColorHistory = new List<int>();
            for (int i = 0; i < history.Count; i++) prefs.ColorHistory.Add(history[i].ToArgb());
            SettingsStore.Save(prefs);

            // 复制到剪贴板
            string text;
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                text = string.Format("rgb({0}, {1}, {2})", centerColor.R, centerColor.G, centerColor.B);
            else
                text = "#" + centerColor.R.ToString("X2") + centerColor.G.ToString("X2") + centerColor.B.ToString("X2");
            string clipErr;
            ClipboardService.TrySetText(text, out clipErr);

            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        private static float[] RgbToHsb(Color c)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            float h = 0;
            if (delta > 0.0001f)
            {
                if (max == r) h = ((g - b) / delta) % 6;
                else if (max == g) h = (b - r) / delta + 2;
                else h = (r - g) / delta + 4;
                h *= 60;
                if (h < 0) h += 360;
            }
            float s = max < 0.0001f ? 0 : delta / max;
            return new float[] { h, s, max };
        }
    }
}
