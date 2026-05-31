using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class HotkeyCaptureForm : Form
    {
        private readonly int id;
        private readonly Label label;

        public HotkeySetting Captured { get; private set; }

        public HotkeyCaptureForm(int id, string actionName)
        {
            this.id = id;
            Text = "设置快捷键";
            Width = 420;
            Height = 170;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            label = new Label();
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Regular, GraphicsUnit.Pixel);
            label.Text = "请按下“" + actionName + "”的新快捷键\nEsc 取消";
            Controls.Add(label);

            KeyDown += OnCaptureKeyDown;
        }

        private void OnCaptureKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (IsModifierOnly(e.KeyCode))
            {
                return;
            }

            uint modifiers = NativeMethods.MOD_NOREPEAT;
            if (e.Control) modifiers |= NativeMethods.MOD_CONTROL;
            if (e.Alt) modifiers |= NativeMethods.MOD_ALT;
            if (e.Shift) modifiers |= NativeMethods.MOD_SHIFT;

            Captured = new HotkeySetting
            {
                Id = id,
                Enabled = true,
                Key = e.KeyCode,
                Modifiers = modifiers
            };

            e.SuppressKeyPress = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool IsModifierOnly(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                   key == Keys.LControlKey || key == Keys.RControlKey ||
                   key == Keys.LShiftKey || key == Keys.RShiftKey ||
                   key == Keys.LMenu || key == Keys.RMenu;
        }
    }
}

