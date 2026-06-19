using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class ToastForm : Form
    {
        private readonly string text;
        private readonly Timer fadeTimer;
        private const int FadeStep = 20;       // 每次变化 5% (0.05)
        private const int FadeInterval = 15;   // 毫秒
        private double targetOpacity = 1.0;
        private bool fadingOut;

        public ToastForm(string text, Rectangle ownerBounds)
        {
            this.text = text;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(35, 35, 35);
            ForeColor = Color.White;
            DoubleBuffered = true;
            Opacity = 0; // 初始透明，等待淡入

            // 自适应宽度
            using (var font = new Font(FontFamily.GenericSansSerif, 15, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var measureBmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(measureBmp))
            {
                SizeF textSize = g.MeasureString(text, font, 480);
                Width = (int)Math.Ceiling(textSize.Width) + 40;
                Height = (int)Math.Ceiling(textSize.Height) + 32;
                if (Width < 120) Width = 120;
                if (Height < 50) Height = 50;
            }

            Left = ownerBounds.Left + ownerBounds.Width - Width - 32;
            Top = ownerBounds.Top + ownerBounds.Height - Height - 46;

            // 圆角 Region
            SetRoundedRegion(10);

            // 淡入淡出定时器
            fadeTimer = new Timer { Interval = FadeInterval };
            fadeTimer.Tick += OnFadeTick;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 开始淡入
            targetOpacity = 1.0;
            fadingOut = false;
            fadeTimer.Start();
        }

        /// <summary>启动淡出，完成后自动关闭并 Dispose。</summary>
        public void BeginFadeOut()
        {
            fadingOut = true;
            targetOpacity = 0;
            if (!fadeTimer.Enabled)
                fadeTimer.Start();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            double step = FadeStep / 100.0;
            if (fadingOut)
            {
                Opacity = Math.Max(0, Opacity - step);
                if (Opacity <= 0)
                {
                    fadeTimer.Stop();
                    Close();
                }
            }
            else
            {
                Opacity = Math.Min(targetOpacity, Opacity + step);
                if (Opacity >= targetOpacity)
                    fadeTimer.Stop();
            }
        }

        private void SetRoundedRegion(int radius)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(Width - d, 0, d, d, 270, 90);
                path.AddArc(Width - d, Height - d, d, d, 0, 90);
                path.AddArc(0, Height - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SetRoundedRegion(10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 圆角背景
            using (var path = new GraphicsPath())
            {
                int r = 10;
                int d = r * 2;
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(Width - d, 0, d, d, 270, 90);
                path.AddArc(Width - d, Height - d, d, d, 0, 90);
                path.AddArc(0, Height - d, d, d, 90, 90);
                path.CloseFigure();
                using (var bg = new SolidBrush(Color.FromArgb(230, 35, 35, 35)))
                {
                    e.Graphics.FillPath(bg, path);
                }
                using (var border = new Pen(Color.FromArgb(80, 255, 255, 255)))
                {
                    e.Graphics.DrawPath(border, path);
                }
            }

            using (var font = new Font(FontFamily.GenericSansSerif, 15, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.DrawString(text, font, brush, new RectangleF(20, 16, Width - 40, Height - 32));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && fadeTimer != null)
            {
                fadeTimer.Stop();
                fadeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
