using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class ToastForm : Form
    {
        private readonly string text;

        public ToastForm(string text, Rectangle ownerBounds)
        {
            this.text = text;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(35, 35, 35);
            ForeColor = Color.White;
            Width = 520;
            Height = 86;
            Left = ownerBounds.Left + ownerBounds.Width - Width - 32;
            Top = ownerBounds.Top + ownerBounds.Height - Height - 46;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var font = new Font(FontFamily.GenericSansSerif, 15, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                e.Graphics.DrawString(text, font, Brushes.White, new RectangleF(18, 16, Width - 36, Height - 32));
            }
        }
    }
}

