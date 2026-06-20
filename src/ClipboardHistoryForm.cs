using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>剪贴板图片历史浏览窗：缩略图网格，双击贴图/复制/保存。</summary>
    internal sealed class ClipboardHistoryForm : Form
    {
        private readonly FlowLayoutPanel panel;

        public ClipboardHistoryForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "剪贴板图片历史";
            Load += delegate { DpiScale.CenterOnActiveMonitor(this); };
            Width = 720;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;

            panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
            Controls.Add(panel);
            BuildItems();
        }

        private void BuildItems()
        {
            panel.Controls.Clear();
            if (ClipboardHistoryStore.Items.Count == 0)
            {
                var empty = new Label { Text = "还没有剪贴板图片历史。\n复制任意图片后会自动记录到这里。", AutoSize = true, Padding = new Padding(16) };
                panel.Controls.Add(empty);
                return;
            }
            for (int i = 0; i < ClipboardHistoryStore.Items.Count; i++)
                panel.Controls.Add(CreateItem(i));
        }

        private Control CreateItem(int index)
        {
            var item = ClipboardHistoryStore.Items[index];
            var container = new Panel { Width = 178, Height = 132, Margin = new Padding(8) };

            var box = new PictureBox
            {
                Width = 170, Height = 100, SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle, Image = item.Thumbnail, Tag = index
            };
            box.DoubleClick += delegate { Pin(index); };
            box.ContextMenuStrip = BuildMenu(index);
            container.Controls.Add(box);

            var label = new Label
            {
                Text = item.When.ToString("MM-dd HH:mm:ss"),
                Dock = DockStyle.Bottom, Height = 20, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText
            };
            container.Controls.Add(label);
            return container;
        }

        private ContextMenuStrip BuildMenu(int index)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("贴到屏幕", null, delegate { Pin(index); });
            menu.Items.Add("复制", null, delegate { Copy(index); });
            menu.Items.Add("保存", null, delegate { Save(index); });
            return menu;
        }

        private Bitmap LoadFull(int index)
        {
            if (index < 0 || index >= ClipboardHistoryStore.Items.Count) return null;
            string path = ClipboardHistoryStore.Items[index].FullPath;
            if (!File.Exists(path)) return null;
            try { return new Bitmap(path); } catch { return null; }
        }

        private void Pin(int index)
        {
            using (Bitmap bmp = LoadFull(index))
            {
                if (bmp == null) return;
                Point cursor = Cursor.Position;
                PinManager.Show((Bitmap)bmp.Clone(), new Point(cursor.X + 24, cursor.Y + 24), true);
            }
        }

        private void Copy(int index)
        {
            using (Bitmap bmp = LoadFull(index))
            {
                if (bmp == null) return;
                string err;
                ClipboardService.TrySetImage(bmp, out err);
            }
        }

        private void Save(int index)
        {
            using (Bitmap bmp = LoadFull(index))
            {
                if (bmp == null) return;
                string file = AppPaths.NewImagePath("_clip");
                try { OverlayForm.SaveImageAutoFormat(bmp, file); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }
}
