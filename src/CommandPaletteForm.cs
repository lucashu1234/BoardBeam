using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>
    /// 命令面板：搜索并执行 BoardBeam 的任何动作。
    /// 收集所有热键动作 + 常用菜单动作，子串过滤，↑↓选择，Enter 执行。
    /// 比 Snipaste 更好：零记忆发现全部能力。
    /// </summary>
    internal sealed class CommandPaletteForm : Form
    {
        public class Command
        {
            public string Title;
            public string Hint;     // 快捷键或分类提示
            public Action Run;
        }

        private readonly TextBox searchBox;
        private readonly ListBox list;
        private readonly List<Command> allCommands = new List<Command>();
        private readonly PresenterApplicationContext owner;

        public CommandPaletteForm(PresenterApplicationContext owner)
        {
            this.owner = owner;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Load += delegate { DpiScale.CenterOnActiveMonitor(this); };
            ShowInTaskbar = false;
            Width = 520;
            Height = 420;
            BackColor = Color.FromArgb(30, 30, 34);
            DoubleBuffered = true;
            KeyPreview = true;

            searchBox = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Regular, GraphicsUnit.Pixel),
                BackColor = Color.FromArgb(45, 45, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Height = 38,
            };
            searchBox.TextChanged += delegate { Filter(); };
            searchBox.KeyDown += OnSearchKey;

            list = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular, GraphicsUnit.Pixel),
                ItemHeight = 28,
                DrawMode = DrawMode.OwnerDrawFixed,
            };
            list.DrawItem += OnDrawItem;
            list.DoubleClick += delegate { ExecuteSelected(); };

            Controls.Add(list);
            Controls.Add(searchBox);

            BuildCommands();
            Filter();
            Shown += delegate { searchBox.Focus(); };
        }

        private void BuildCommands()
        {
            AppSettings prefs = SettingsStore.Load();
            // 热键动作
            foreach (HotkeyDefinition def in HotkeyCatalog.Definitions)
            {
                HotkeySetting hs = prefs.GetHotkey(def.Id);
                int id = def.Id;
                allCommands.Add(new Command
                {
                    Title = def.Name,
                    Hint = HotkeyFormatter.Format(hs),
                    Run = delegate { owner.InvokeHotkey(id); }
                });
            }
            // 常用菜单动作（非热键）
            allCommands.Add(new Command { Title = "打开设置", Hint = "菜单", Run = () => owner.ShowSettings() });
            allCommands.Add(new Command { Title = "打开截图目录", Hint = "菜单", Run = () => System.Diagnostics.Process.Start(AppPaths.CaptureDirectory) });
            allCommands.Add(new Command { Title = "打开录屏目录", Hint = "菜单", Run = () => System.Diagnostics.Process.Start(AppPaths.RecordingDirectory) });
            allCommands.Add(new Command { Title = "显示/隐藏所有贴图", Hint = "Alt+Shift+7", Run = () => PinManager.ToggleAllVisibility() });
            allCommands.Add(new Command { Title = "关闭所有贴图", Hint = "菜单", Run = () => PinManager.CloseAll() });
            allCommands.Add(new Command { Title = "退出 BoardBeam", Hint = "菜单", Run = () => owner.Exit() });
        }

        private void Filter()
        {
            string q = (searchBox.Text ?? "").Trim().ToLowerInvariant();
            list.Items.Clear();
            foreach (Command c in allCommands)
            {
                if (q.Length == 0 || c.Title.ToLowerInvariant().Contains(q))
                    list.Items.Add(c);
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                if (list.SelectedIndex > 0) list.SelectedIndex--;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                ExecuteSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void ExecuteSelected()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= list.Items.Count) return;
            var cmd = (Command)list.Items[list.SelectedIndex];
            Close();
            try { cmd.Run(); } catch (Exception ex) { CrashLogger.Log("命令面板执行", ex); }
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var cmd = (Command)list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Brush bg = selected ? new SolidBrush(Color.FromArgb(70, 100, 180, 255)) : new SolidBrush(Color.FromArgb(30, 30, 34));
            e.Graphics.FillRectangle(bg, e.Bounds);
            bg.Dispose();
            using (var titleBrush = new SolidBrush(Color.White))
            using (var hintBrush = new SolidBrush(Color.FromArgb(160, 160, 170)))
            using (var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var hintFont = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                e.Graphics.DrawString(cmd.Title, font, titleBrush, e.Bounds.X + 10, e.Bounds.Y + 5);
                SizeF hintSize = e.Graphics.MeasureString(cmd.Hint, hintFont);
                e.Graphics.DrawString(cmd.Hint, hintFont, hintBrush, e.Bounds.Right - hintSize.Width - 12, e.Bounds.Y + 7);
            }
            e.DrawFocusRectangle();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(80, 120, 180, 255), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
