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
        private readonly List<Bitmap> images;
        private readonly FlowLayoutPanel panel;

        public CaptureHistoryForm()
        {
            images = CaptureStore.GetAll();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < images.Count; i++)
                {
                    images[i].Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void BuildItems()
        {
            panel.Controls.Clear();
            if (images.Count == 0)
            {
                var empty = new Label();
                empty.Text = "还没有截图历史。";
                empty.AutoSize = true;
                empty.Padding = new Padding(16);
                panel.Controls.Add(empty);
                return;
            }

            for (int i = 0; i < images.Count; i++)
            {
                panel.Controls.Add(CreateItem(i));
            }
        }

        private Control CreateItem(int index)
        {
            Bitmap image = images[index];
            var box = new PictureBox();
            box.Width = 150;
            box.Height = 110;
            box.SizeMode = PictureBoxSizeMode.Zoom;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Margin = new Padding(8);
            box.Image = image;
            box.Tag = index;
            box.DoubleClick += delegate { Pin(index); };
            box.ContextMenuStrip = BuildMenu(index);
            return box;
        }

        private ContextMenuStrip BuildMenu(int index)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("贴到屏幕", null, delegate { Pin(index); });
            menu.Items.Add("复制", null, delegate { Copy(index); });
            menu.Items.Add("保存", null, delegate { Save(index); });
            return menu;
        }

        private void Pin(int index)
        {
            if (index < 0 || index >= images.Count) return;
            Point cursor = Cursor.Position;
            PinManager.Show(images[index], new Point(cursor.X + 24, cursor.Y + 24));
        }

        private void Copy(int index)
        {
            if (index < 0 || index >= images.Count) return;
            string error;
            if (!ClipboardService.TrySetImage(images[index], out error))
            {
                MessageBox.Show(error, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Save(int index)
        {
            if (index < 0 || index >= images.Count) return;
            string file = AppPaths.NewImagePath("_history");
            try
            {
                images[index].Save(file, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}

