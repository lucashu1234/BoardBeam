using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings settings;
        private readonly ListView list;
        private bool loadingList;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings current)
        {
            settings = current.Clone();
            Text = "BoardBeam 设置";
            Width = 760;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.CheckBoxes = true;
            list.Columns.Add("功能", 230);
            list.Columns.Add("快捷键", 190);
            list.Columns.Add("状态", 90);
            list.DoubleClick += delegate { CaptureSelectedHotkey(); };
            list.ItemChecked += OnItemChecked;

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 48;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(8);

            var save = Button("保存", delegate { SaveAndClose(); });
            var cancel = Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            var reset = Button("恢复默认", delegate { ResetDefaults(); });
            var clear = Button("禁用所选", delegate { DisableSelected(); });
            var edit = Button("修改快捷键", delegate { CaptureSelectedHotkey(); });

            bottom.Controls.Add(save);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(reset);
            bottom.Controls.Add(clear);
            bottom.Controls.Add(edit);

            Controls.Add(list);
            Controls.Add(bottom);

            LoadList();
        }

        private static Button Button(string text, EventHandler handler)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 96;
            button.Height = 28;
            button.Click += handler;
            return button;
        }

        private void LoadList()
        {
            loadingList = true;
            list.Items.Clear();
            for (int i = 0; i < HotkeyCatalog.Definitions.Length; i++)
            {
                HotkeyDefinition definition = HotkeyCatalog.Definitions[i];
                HotkeySetting setting = settings.GetHotkey(definition.Id);
                var item = new ListViewItem(definition.Name);
                item.SubItems.Add(HotkeyFormatter.Format(setting));
                item.SubItems.Add(setting.Enabled ? "启用" : "禁用");
                item.Checked = setting.Enabled;
                item.Tag = definition.Id;
                list.Items.Add(item);
            }
            loadingList = false;
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (loadingList || e.Item.Tag == null) return;
            int id = (int)e.Item.Tag;
            HotkeySetting setting = settings.GetHotkey(id);
            setting.Enabled = e.Item.Checked;
            settings.SetHotkey(setting);
            e.Item.SubItems[1].Text = HotkeyFormatter.Format(setting);
            e.Item.SubItems[2].Text = setting.Enabled ? "启用" : "禁用";
        }

        private void CaptureSelectedHotkey()
        {
            if (list.SelectedItems.Count == 0) return;
            int id = (int)list.SelectedItems[0].Tag;
            string actionName = HotkeyCatalog.DisplayName(id);
            using (var capture = new HotkeyCaptureForm(id, actionName))
            {
                if (capture.ShowDialog(this) == DialogResult.OK && capture.Captured != null)
                {
                    settings.SetHotkey(capture.Captured);
                    LoadList();
                    SelectId(id);
                }
            }
        }

        private void DisableSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            int id = (int)list.SelectedItems[0].Tag;
            HotkeySetting setting = settings.GetHotkey(id);
            setting.Enabled = false;
            settings.SetHotkey(setting);
            LoadList();
            SelectId(id);
        }

        private void ResetDefaults()
        {
            if (MessageBox.Show(this, "恢复所有默认快捷键？", "恢复默认", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            AppSettings defaults = SettingsStore.CreateDefaults();
            foreach (HotkeySetting setting in defaults.GetAllHotkeys())
            {
                settings.SetHotkey(setting);
            }
            LoadList();
        }

        private void SaveAndClose()
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                int id = (int)list.Items[i].Tag;
                HotkeySetting setting = settings.GetHotkey(id);
                setting.Enabled = list.Items[i].Checked;
                settings.SetHotkey(setting);
            }

            string error;
            if (!ValidateHotkeys(out error))
            {
                MessageBox.Show(this, error, "快捷键冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = settings.Clone();
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool ValidateHotkeys(out string error)
        {
            var seen = new System.Collections.Generic.Dictionary<string, string>();
            foreach (HotkeySetting setting in settings.GetAllHotkeys())
            {
                if (!setting.Enabled) continue;
                if (!IsSafeGlobalHotkey(setting))
                {
                    error = HotkeyCatalog.DisplayName(setting.Id) + " 的快捷键过于容易误触：" + HotkeyFormatter.Format(setting) + "。请使用 Ctrl、Alt、Shift 组合，或使用 F1-F24 功能键。";
                    return false;
                }

                string key = ((int)setting.Key).ToString() + ":" + (setting.Modifiers & ~NativeMethods.MOD_NOREPEAT).ToString();
                string existing;
                if (seen.TryGetValue(key, out existing))
                {
                    error = existing + " 和 " + HotkeyCatalog.DisplayName(setting.Id) + " 使用了同一个快捷键：" + HotkeyFormatter.Format(setting);
                    return false;
                }
                seen[key] = HotkeyCatalog.DisplayName(setting.Id);
            }

            error = null;
            return true;
        }

        private static bool IsSafeGlobalHotkey(HotkeySetting setting)
        {
            uint modifiers = setting.Modifiers & ~NativeMethods.MOD_NOREPEAT;
            if (modifiers != 0) return true;
            return setting.Key >= Keys.F1 && setting.Key <= Keys.F24;
        }

        private void SelectId(int id)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                if ((int)list.Items[i].Tag == id)
                {
                    list.Items[i].Selected = true;
                    list.Items[i].Focused = true;
                    list.EnsureVisible(i);
                    return;
                }
            }
        }
    }
}

