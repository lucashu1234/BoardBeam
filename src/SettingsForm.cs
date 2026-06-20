using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings settings;
        private ListView list;
        private bool loadingList;
        private ComboBox formatCombo;
        private TextBox searchBox;
        private CheckBox autostartCheck;
        private CheckBox autoPinCheck;
        private CheckBox cursorCheck;
        private ColorDialog colorDialog;
        private Button colorSwatch;
        private NumericUpDown widthInput;
        private ComboBox stampCombo;
        private ComboBox countdownCombo;
        private CheckBox watermarkCheck;
        private TextBox watermarkText;
        private ComboBox watermarkPos;
        private NumericUpDown watermarkOpacity;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings current)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            settings = current.Clone();
            Text = "BoardBeam 设置";
            Width = 780;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildHotkeyTab());
            tabs.TabPages.Add(BuildGeneralTab());
            tabs.TabPages.Add(BuildPasteTab());
            tabs.TabPages.Add(BuildPenTab());
            tabs.TabPages.Add(BuildWatermarkTab());

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 48;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(8);

            var save = Button("保存", delegate { SaveAndClose(); });
            var cancel = Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            bottom.Controls.Add(save);
            bottom.Controls.Add(cancel);

            Controls.Add(tabs);
            Controls.Add(bottom);
        }

        // ===== 标签页：快捷键（带搜索） =====
        private TabPage BuildHotkeyTab()
        {
            var page = new TabPage("快捷键");

            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 34 };
            var sLabel = new Label
            {
                Text = "搜索：",
                Location = new Point(8, 8),
                AutoSize = true
            };
            searchBox = new TextBox
            {
                Location = new Point(56, 5),
                Width = 240
            };
            searchBox.TextChanged += delegate { LoadList(); };
            searchPanel.Controls.Add(sLabel);
            searchPanel.Controls.Add(searchBox);

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

            var actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Bottom;
            actionPanel.Height = 44;
            actionPanel.FlowDirection = FlowDirection.LeftToRight;
            actionPanel.Padding = new Padding(8, 6, 8, 4);
            actionPanel.Controls.Add(Button("修改快捷键", delegate { CaptureSelectedHotkey(); }));
            actionPanel.Controls.Add(Button("禁用所选", delegate { DisableSelected(); }));
            actionPanel.Controls.Add(Button("恢复默认", delegate { ResetDefaults(); }));

            page.Controls.Add(list);
            page.Controls.Add(actionPanel);
            page.Controls.Add(searchPanel);
            LoadList();
            return page;
        }

        // ===== 标签页：常规 =====
        private TabPage BuildGeneralTab()
        {
            var page = new TabPage("常规");
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };

            autostartCheck = new CheckBox
            {
                Text = "开机自启动",
                AutoSize = true,
                Checked = AutostartHelper.IsEnabled()
            };
            panel.Controls.Add(autostartCheck);

            cursorCheck = new CheckBox
            {
                Text = "截图包含鼠标光标",
                AutoSize = true,
                Checked = settings.IncludeCursorInCapture
            };
            panel.Controls.Add(cursorCheck);
            panel.Controls.Add(Spacer(8));

            // 保存格式
            var fmtPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var fmtLabel = new Label { Text = "截图保存格式：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            formatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            formatCombo.Items.AddRange(new object[] { "自动（大图 JPG / 小图 PNG）", "PNG", "JPG", "BMP" });
            formatCombo.SelectedIndex = settings.SaveFormat >= 0 && settings.SaveFormat <= 3 ? settings.SaveFormat : 0;
            fmtPanel.Controls.Add(fmtLabel);
            fmtPanel.Controls.Add(formatCombo);
            panel.Controls.Add(fmtPanel);
            panel.Controls.Add(Spacer(16));

            var dirLabel = new Label { Text = "存储目录", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            panel.Controls.Add(dirLabel);
            var capBtn = Button("打开截图目录", delegate { System.Diagnostics.Process.Start(AppPaths.CaptureDirectory); });
            var recBtn = Button("打开录屏目录", delegate { System.Diagnostics.Process.Start(AppPaths.RecordingDirectory); });
            var cfgBtn = Button("打开配置目录", delegate { System.Diagnostics.Process.Start(AppPaths.ConfigDirectory); });
            panel.Controls.Add(capBtn);
            panel.Controls.Add(recBtn);
            panel.Controls.Add(cfgBtn);

            panel.Controls.Add(Spacer(16));
            var cfgLabel = new Label { Text = "配置管理", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            panel.Controls.Add(cfgLabel);

            // 便携模式状态
            var portLabel = new Label
            {
                Text = AppPaths.IsPortable ? "便携模式：已启用（数据存于程序目录）" : "便携模式：未启用（在程序目录放置 BoardBeam.portable 文件即启用）",
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };
            panel.Controls.Add(portLabel);

            var exportBtn = Button("导出设置…", delegate { ExportSettings(); });
            var importBtn = Button("导入设置…", delegate { ImportSettings(); });
            panel.Controls.Add(exportBtn);
            panel.Controls.Add(importBtn);

            page.Controls.Add(panel);
            return page;
        }

        private void ExportSettings()
        {
            using (var dlg = new SaveFileDialog { Filter = "BoardBeam 设置 (*.ini)|*.ini", FileName = "boardbeam_settings.ini" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try { System.IO.File.Copy(AppPaths.SettingsFile, dlg.FileName, true); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
        }

        private void ImportSettings()
        {
            using (var dlg = new OpenFileDialog { Filter = "BoardBeam 设置 (*.ini)|*.ini" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        System.IO.File.Copy(dlg.FileName, AppPaths.SettingsFile, true);
                        MessageBox.Show(this, "设置已导入，重启后生效。", "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
        }

        // ===== 标签页：贴图 =====
        private TabPage BuildPasteTab()
        {
            var page = new TabPage("贴图");
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };

            autoPinCheck = new CheckBox
            {
                Text = "剪贴板图片自动贴图（复制任意图片时自动钉到光标处）",
                AutoSize = true,
                Checked = settings.AutoPinClipboard
            };
            panel.Controls.Add(autoPinCheck);
            panel.Controls.Add(Spacer(12));

            var hint = new Label
            {
                Text = "贴图操作：\n  滚轮 缩放 · Ctrl+滚轮 透明度 · Shift+滚轮 任意旋转\n  R 旋转 90° · 右键菜单 更多操作（翻转/穿透/锁定）\n  Alt+Shift+7 显示/隐藏所有贴图 · Alt+8 切换鼠标穿透",
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };
            panel.Controls.Add(hint);

            page.Controls.Add(panel);
            return page;
        }

        // ===== 标签页：颜色与画笔 =====
        private TabPage BuildPenTab()
        {
            var page = new TabPage("颜色与画笔");
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };

            // 默认颜色
            var colorPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var cLabel = new Label { Text = "默认颜色：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            colorDialog = new ColorDialog { FullOpen = true, Color = Color.FromArgb(settings.DefaultColorArgb) };
            colorSwatch = new Button { Width = 48, Height = 28, BackColor = Color.FromArgb(settings.DefaultColorArgb), Text = "" };
            colorSwatch.Click += delegate
            {
                if (colorDialog.ShowDialog(this) == DialogResult.OK)
                {
                    colorSwatch.BackColor = colorDialog.Color;
                }
            };
            colorPanel.Controls.Add(cLabel);
            colorPanel.Controls.Add(colorSwatch);
            panel.Controls.Add(colorPanel);
            panel.Controls.Add(Spacer(8));

            // 默认线宽
            var widthPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var wLabel = new Label { Text = "默认线宽：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            widthInput = new NumericUpDown { Minimum = 1, Maximum = 40, Value = (decimal)settings.DefaultWidth, Width = 70 };
            widthPanel.Controls.Add(wLabel);
            widthPanel.Controls.Add(widthInput);
            panel.Controls.Add(widthPanel);
            panel.Controls.Add(Spacer(8));

            // 默认印章
            var stampPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var stLabel = new Label { Text = "默认印章：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            stampCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            stampCombo.Items.AddRange(StampAnnotation.StampNames);
            int st = settings.DefaultStampType;
            stampCombo.SelectedIndex = st >= 0 && st < StampAnnotation.StampNames.Length ? st : 0;
            stampPanel.Controls.Add(stLabel);
            stampPanel.Controls.Add(stampCombo);
            panel.Controls.Add(stampPanel);
            panel.Controls.Add(Spacer(8));

            // 默认倒计时
            var cdPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var cdLabel = new Label { Text = "默认倒计时：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            countdownCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            countdownCombo.Items.AddRange(new object[] { "1 分钟", "3 分钟", "5 分钟", "10 分钟", "15 分钟", "45 分钟" });
            int[] cdSeconds = { 60, 180, 300, 600, 900, 2700 };
            int cdIdx = Array.IndexOf(cdSeconds, settings.DefaultCountdownSeconds);
            countdownCombo.SelectedIndex = cdIdx >= 0 ? cdIdx : 3;
            countdownCombo.Tag = cdSeconds;
            cdPanel.Controls.Add(cdLabel);
            cdPanel.Controls.Add(countdownCombo);
            panel.Controls.Add(cdPanel);

            page.Controls.Add(panel);
            return page;
        }

        // ===== 标签页：水印 =====
        private TabPage BuildWatermarkTab()
        {
            var page = new TabPage("水印");
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };

            watermarkCheck = new CheckBox { Text = "在输出的截图上叠加水印", AutoSize = true, Checked = settings.WatermarkEnabled };
            panel.Controls.Add(watermarkCheck);
            panel.Controls.Add(Spacer(8));

            var textPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var tl = new Label { Text = "水印文字：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            watermarkText = new TextBox { Width = 240, Text = settings.WatermarkText ?? "" };
            textPanel.Controls.Add(tl);
            textPanel.Controls.Add(watermarkText);
            panel.Controls.Add(textPanel);
            panel.Controls.Add(Spacer(4));
            var hint = new Label { Text = "（可用 {date} {time} 占位符，如：© 张老师 {date}）", AutoSize = true, ForeColor = SystemColors.GrayText };
            panel.Controls.Add(hint);
            panel.Controls.Add(Spacer(8));

            var posPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var pl = new Label { Text = "位置：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            watermarkPos = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            watermarkPos.Items.AddRange(new object[] { "右下", "左下", "右上", "左上", "居中" });
            watermarkPos.SelectedIndex = settings.WatermarkPosition >= 0 && settings.WatermarkPosition <= 4 ? settings.WatermarkPosition : 0;
            posPanel.Controls.Add(pl);
            posPanel.Controls.Add(watermarkPos);
            panel.Controls.Add(posPanel);
            panel.Controls.Add(Spacer(8));

            var opPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var ol = new Label { Text = "透明度：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            watermarkOpacity = new NumericUpDown { Minimum = 10, Maximum = 100, Value = settings.WatermarkOpacityPct, Width = 70 };
            opPanel.Controls.Add(ol);
            opPanel.Controls.Add(watermarkOpacity);
            panel.Controls.Add(opPanel);

            page.Controls.Add(panel);
            return page;
        }

        private static Control Spacer(int h)
        {
            return new Panel { Height = h, Width = 1 };
        }

        private static Button Button(string text, EventHandler handler)
        {
            var button = new Button { Text = text, Width = 108, Height = 28 };
            button.Click += handler;
            return button;
        }

        private void LoadList()
        {
            if (list == null) return;
            loadingList = true;
            list.Items.Clear();
            string filter = searchBox == null ? "" : searchBox.Text.Trim().ToLowerInvariant();
            for (int i = 0; i < HotkeyCatalog.Definitions.Length; i++)
            {
                HotkeyDefinition definition = HotkeyCatalog.Definitions[i];
                if (filter.Length > 0 && !definition.Name.ToLowerInvariant().Contains(filter)) continue;
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
            // 重读磁盘最新设置，把表单未管理但运行时可能被修改的字段（QuickSlots/ColorHistory/LastRegion 等）
            // 合并进来，避免用陈旧快照覆盖造成数据丢失竞态。
            AppSettings fresh = SettingsStore.Load();
            settings.QuickSlots = fresh.QuickSlots;
            settings.ColorHistory = fresh.ColorHistory;
            settings.HasLastRegion = fresh.HasLastRegion;
            settings.LastRegionX = fresh.LastRegionX;
            settings.LastRegionY = fresh.LastRegionY;
            settings.LastRegionW = fresh.LastRegionW;
            settings.LastRegionH = fresh.LastRegionH;

            // 快捷键
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

            // 常规
            settings.SaveFormat = formatCombo.SelectedIndex;
            settings.AutostartEnabled = autostartCheck.Checked;
            AutostartHelper.SetEnabled(autostartCheck.Checked);
            settings.IncludeCursorInCapture = cursorCheck.Checked;

            // 贴图
            settings.AutoPinClipboard = autoPinCheck.Checked;

            // 颜色与画笔
            settings.DefaultColorArgb = colorSwatch.BackColor.ToArgb();
            settings.DefaultWidth = (float)widthInput.Value;
            settings.DefaultStampType = stampCombo.SelectedIndex;
            int[] cdSeconds = (int[])countdownCombo.Tag;
            settings.DefaultCountdownSeconds = countdownCombo.SelectedIndex >= 0 ? cdSeconds[countdownCombo.SelectedIndex] : 600;

            // 水印（保留占位符原文，绘制时再替换为实际时间）
            settings.WatermarkEnabled = watermarkCheck.Checked;
            settings.WatermarkText = watermarkText.Text;
            settings.WatermarkPosition = watermarkPos.SelectedIndex;
            settings.WatermarkOpacityPct = (int)watermarkOpacity.Value;

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
