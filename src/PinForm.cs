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
        private readonly Bitmap image;
        private float scale;
        private bool dragging;
        private Point dragStart;
        private Point formStart;
        private bool locked;

        public PinForm(Bitmap image, Point location)
        {
            this.image = (Bitmap)image.Clone();
            scale = 1.0f;
            locked = false;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = Color.Black;
            Location = location;
            ContextMenuStrip = BuildMenu();
            ApplySize();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null)
            {
                image.Dispose();
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
            var lockItem = new ToolStripMenuItem("锁定移动");
            lockItem.CheckOnClick = true;
            lockItem.CheckedChanged += delegate { locked = lockItem.Checked; };
            menu.Items.Add(lockItem);
            menu.Items.Add("关闭", null, delegate { Close(); });
            return menu;
        }

        private void SaveImage()
        {
            string file = AppPaths.NewImagePath("_pin");
            try
            {
                image.Save(file, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "保存贴图失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyImage()
        {
            string error;
            if (!ClipboardService.TrySetImage(image, out error))
            {
                MessageBox.Show(error, "复制贴图失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetScale(float value)
        {
            scale = Math.Max(0.2f, Math.Min(4.0f, value));
            ApplySize();
            Invalidate();
        }

        private void ApplySize()
        {
            Width = Math.Max(40, (int)Math.Round(image.Width * scale));
            Height = Math.Max(40, (int)Math.Round(image.Height * scale));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawImage(image, new Rectangle(0, 0, Width, Height));
            using (var pen = new Pen(Color.FromArgb(190, 255, 255, 255), 2))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
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
            if (!dragging) return;
            Point current = Cursor.Position;
            Location = new Point(formStart.X + current.X - dragStart.X, formStart.Y + current.Y - dragStart.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
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
        }
    }
}

