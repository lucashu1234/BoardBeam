using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class CaptureHistoryForm : Form
    {
        private readonly List<Bitmap> fullImages;   // 全尺寸图，用于复制/保存
        private readonly List<Bitmap> thumbnails;   // 缩略图，用于显示
        private readonly FlowLayoutPanel panel;

        private const int ThumbWidth = 150;
        private const int ThumbHeight = 100;

        public CaptureHistoryForm()
        {
            fullImages = CaptureStore.GetAll();
            // 生成缩略图：大幅减少显示内存
            thumbnails = new List<Bitmap>(fullImages.Count);
            for (int i = 0; i < fullImages.Count; i++)
            {
                thumbnails.Add(CreateThumbnail(fullImages[i], ThumbWidth, ThumbHeight));
            }

            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "截图历史";
            Width = 720;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;

            panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.AutoScroll = true;
            panel.Padding = new Padding(12);
            Controls.Add(panel);

            BuildItems();
        }

        private static Bitmap CreateThumbnail(Bitmap source, int maxW, int maxH)
        {
            float ratio = Math.Min((float)maxW / source.Width, (float)maxH / source.Height);
            int w = Math.Max(1, (int)(source.Width * ratio));
            int h = Math.Max(1, (int)(source.Height * ratio));
            var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, w, h);
            }
            return thumb;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < fullImages.Count; i++)
                    fullImages[i].Dispose();
                for (int i = 0; i < thumbnails.Count; i++)
                    thumbnails[i].Dispose();
            }
            base.Dispose(disposing);
        }

        private void BuildItems()
        {
            panel.Controls.Clear();
            if (fullImages.Count == 0)
            {
                var empty = new Label();
                empty.Text = "还没有截图历史。";
                empty.AutoSize = true;
                empty.Padding = new Padding(16);
                panel.Controls.Add(empty);
                return;
            }

            for (int i = 0; i < fullImages.Count; i++)
            {
                panel.Controls.Add(CreateItem(i));
            }
        }

        private Control CreateItem(int index)
        {
            var panel = new Panel();
            panel.Width = 158;
            panel.Height = 132;
            panel.Margin = new Padding(8);
            panel.BackColor = Color.Transparent;

            var box = new PictureBox();
            box.Width = ThumbWidth;
            box.Height = ThumbHeight;
            box.Location = new Point(0, 0);
            box.SizeMode = PictureBoxSizeMode.Zoom;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Image = thumbnails[index];
            box.Tag = index;
            box.DoubleClick += delegate { Pin(index); };
            box.ContextMenuStrip = BuildMenu(index);

            // 悬停高亮
            box.MouseEnter += delegate { panel.BackColor = Color.FromArgb(40, 100, 180, 255); };
            box.MouseLeave += delegate { panel.BackColor = Color.Transparent; };

            var label = new Label();
            label.Text = fullImages[index].Width + " × " + fullImages[index].Height;
            label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            label.Dock = DockStyle.Bottom;
            label.Height = 20;
            label.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular, GraphicsUnit.Pixel);
            label.ForeColor = SystemColors.GrayText;

            panel.Controls.Add(box);
            panel.Controls.Add(label);
            return panel;
        }

        private ContextMenuStrip BuildMenu(int index)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("贴到屏幕", null, delegate { Pin(index); });
            menu.Items.Add("复制", null, delegate { Copy(index); });
            menu.Items.Add("保存", null, delegate { Save(index); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除", null, delegate { Delete(index); });
            return menu;
        }

        private void Delete(int index)
        {
            if (index < 0 || index >= fullImages.Count) return;
            fullImages[index].Dispose();
            fullImages.RemoveAt(index);
            thumbnails[index].Dispose();
            thumbnails.RemoveAt(index);
            CaptureStore.RemoveAt(index);
            BuildItems();
        }

        private void Pin(int index)
        {
            if (index < 0 || index >= fullImages.Count) return;
            Point cursor = Cursor.Position;
            PinManager.Show(fullImages[index], new Point(cursor.X + 24, cursor.Y + 24));
        }

        private void Copy(int index)
        {
            if (index < 0 || index >= fullImages.Count) return;
            string error;
            if (!ClipboardService.TrySetImage(fullImages[index], out error))
            {
                MessageBox.Show(error, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Save(int index)
        {
            if (index < 0 || index >= fullImages.Count) return;
            string file = AppPaths.NewImagePath("_history");
            try
            {
                OverlayForm.SaveImageAutoFormat(fullImages[index], file);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
