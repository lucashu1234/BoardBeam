using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>极简输入对话框（VB InputBox 风格）。</summary>
    internal static class InputDialog
    {
        public static string Show(IWin32Window owner, string title, string prompt, string defaultValue)
        {
            string result = null;
            using (var f = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                Width = 380,
                Height = 170,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
            })
            {
                var lbl = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
                var box = new TextBox { Left = 12, Top = 38, Width = 340, Text = defaultValue ?? "" };
                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 200, Top = 78, Width = 70, Height = 26 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 282, Top = 78, Width = 70, Height = 26 };
                f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                box.SelectAll();
                box.Focus();
                if (f.ShowDialog(owner) == DialogResult.OK)
                    result = box.Text;
            }
            return result;
        }
    }
}
