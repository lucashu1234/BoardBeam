using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class PresenterApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon notifyIcon;
        private readonly HotkeyWindow hotkeyWindow;
        private readonly DemoTypeEngine demoTypeEngine;
        private readonly List<Timer> activeTimers = new List<Timer>();
        private AppSettings settings;
        private OverlayForm overlay;
        private MainForm mainForm;  // 主窗口仪表盘（单例，关闭=隐藏）
        private string lastClipboardSig; // 剪贴板自动贴图去重签名

        public PresenterApplicationContext()
        {
            settings = SettingsStore.Load();
            // 同步开机自启：以实际快捷方式存在为准（用户可能手动删除）
            try
            {
                bool actual = AutostartHelper.IsEnabled();
                if (settings.AutostartEnabled != actual)
                {
                    settings.AutostartEnabled = actual;
                    SettingsStore.Save(settings);
                }
            }
            catch { }
            demoTypeEngine = new DemoTypeEngine();
            hotkeyWindow = new HotkeyWindow(this);
            hotkeyWindow.RegisterSettings(settings);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Text = "BoardBeam";
            notifyIcon.Visible = true;
            notifyIcon.ContextMenuStrip = BuildMenu();
            notifyIcon.DoubleClick += delegate { ShowDashboard(); };
            notifyIcon.ShowBalloonTip(2500, "BoardBeam 已启动", "双击托盘图标或按 " + HotkeyFormatter.Format(settings.GetHotkey(31)) + " 打开控制面板。" + HotkeyCatalog.Definitions.Length + " 个功能、F9 批注。", ToolTipIcon.Info);
            if (hotkeyWindow.FailedHotkeys.Count > 0)
            {
                notifyIcon.ShowBalloonTip(5000, "部分快捷键注册失败", string.Join("\n", hotkeyWindow.FailedHotkeys.ToArray()), ToolTipIcon.Warning);
            }

            // 启动时显示主面板（让用户立刻看到界面）
            BeginInvokeSafe(delegate
            {
                try { ShowDashboard(); }
                catch (Exception ex) { CrashLogger.Log("启动显示主面板", ex); }
            });
        }

        /// <summary>在 UI 线程异步执行（构造函数期间还没有消息循环，延后到首次空闲）。</summary>
        private static void BeginInvokeSafe(Action action)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += delegate { timer.Stop(); timer.Dispose(); try { action(); } catch (Exception ex) { CrashLogger.Log("BeginInvokeSafe", ex); } };
            timer.Start();
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var dashItem = menu.Items.Add(MenuText(31), null, delegate { ShowDashboard(); });
            dashItem.Font = new Font(dashItem.Font, FontStyle.Bold);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuText(1), null, delegate { ShowOverlay(OverlayMode.Zoom); });
            menu.Items.Add(MenuText(2), null, delegate { ShowOverlay(OverlayMode.Draw); });
            menu.Items.Add(MenuText(3), null, delegate { ShowOverlay(OverlayMode.Timer); });
            menu.Items.Add(MenuText(5), null, delegate { ToggleRecording(); });
            menu.Items.Add(MenuText(10), null, delegate { ShowOverlay(OverlayMode.LiveDraw); });
            menu.Items.Add(MenuText(7), null, delegate { ShowOverlay(OverlayMode.RegionCopy); });
            menu.Items.Add(MenuText(8), null, delegate { ShowOverlay(OverlayMode.RegionSave); });
            menu.Items.Add(MenuText(11), null, delegate { RunDemoType(); });
            menu.Items.Add(MenuText(12), null, delegate { ShowOverlay(OverlayMode.Text); });
            menu.Items.Add(MenuText(13), null, delegate { ShowOverlay(OverlayMode.Spotlight); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuText(30), null, delegate { CaptureActiveMonitor(); });
            menu.Items.Add(MenuText(14), null, delegate { ShowOverlay(OverlayMode.PixPinCapture); });
            menu.Items.Add(MenuText(15), null, delegate { ShowOverlay(OverlayMode.RegionPin); });
            menu.Items.Add(MenuText(16), null, delegate { StartDelayedPixPinCapture(); });
            menu.Items.Add(MenuText(18), null, delegate { ShowCaptureHistory(); });
            menu.Items.Add(MenuText(19), null, delegate { PinWindowUnderCursor(); });
            menu.Items.Add(MenuText(20), null, delegate { CopyWindowUnderCursor(); });
            menu.Items.Add(MenuText(22), null, delegate { ShowOverlay(OverlayMode.ScrollingCapture); });
            menu.Items.Add(MenuText(4), null, delegate { LiveZoomTool.Toggle(); });
            menu.Items.Add(MenuText(9), null, delegate { OcrTool.ShowOcrCapture(this); });
            menu.Items.Add(MenuText(17), null, delegate { PinLatestImage(); });
            menu.Items.Add(MenuText(23), null, delegate { QuickSnipAndPin(); });
            menu.Items.Add(MenuText(24), null, delegate { PinManager.ToggleAllVisibility(); });
            menu.Items.Add(MenuText(26), null, delegate { ShowColorPicker(); });
            menu.Items.Add(MenuText(27), null, delegate { RecaptureLastRegion(); });
            menu.Items.Add("关闭所有贴图", null, delegate { PinManager.CloseAll(); });

            // 快贴槽位子菜单（1-9）
            var slotMenu = new ToolStripMenuItem("快贴槽位");
            for (int i = 1; i <= 9; i++)
            {
                int n = i;
                slotMenu.DropDownItems.Add("槽位 " + n + " 重截贴图", null, delegate { PinQuickSlot(n); });
            }
            slotMenu.DropDownItems.Add(new ToolStripSeparator());
            slotMenu.DropDownItems.Add("清除全部槽位", null, delegate
            {
                AppSettings p = SettingsStore.Load(); p.QuickSlots = ""; SettingsStore.Save(p);
                Notify("已清除全部快贴槽位", "");
            });
            menu.Items.Add(slotMenu);

            // 贴图组保存/恢复
            var groupMenu = new ToolStripMenuItem("贴图组");
            groupMenu.DropDownItems.Add("保存当前贴图为组…", null, delegate { SavePinGroup(); });
            groupMenu.DropDownItems.Add(new ToolStripSeparator());
            var existing = PinManager.ListGroups();
            if (existing.Length == 0)
                groupMenu.DropDownItems.Add("（暂无已保存的组）").Enabled = false;
            else
                foreach (string gname in existing)
                {
                    string captured = gname;
                    groupMenu.DropDownItems.Add("恢复组：" + captured, null, delegate { RestorePinGroup(captured); });
                }
            menu.Items.Add(groupMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("白板", null, delegate { ShowOverlay(OverlayMode.Whiteboard); });
            menu.Items.Add("黑板", null, delegate { ShowOverlay(OverlayMode.Blackboard); });
            menu.Items.Add(new ToolStripSeparator());

            // 剪贴板自动贴图开关
            var autoPinItem = new ToolStripMenuItem("剪贴板图片自动贴图");
            autoPinItem.CheckOnClick = true;
            autoPinItem.Checked = settings.AutoPinClipboard;
            autoPinItem.CheckedChanged += delegate
            {
                settings.AutoPinClipboard = autoPinItem.Checked;
                SettingsStore.Save(settings);
            };
            menu.Items.Add(autoPinItem);

            // 开机自启开关
            var autostartItem = new ToolStripMenuItem("开机自启");
            autostartItem.CheckOnClick = true;
            autostartItem.Checked = AutostartHelper.IsEnabled();
            autostartItem.CheckedChanged += delegate
            {
                AutostartHelper.SetEnabled(autostartItem.Checked);
                settings.AutostartEnabled = autostartItem.Checked;
                SettingsStore.Save(settings);
            };
            menu.Items.Add(autostartItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuText(28), null, delegate { ShowCommandPalette(); });
            menu.Items.Add("设置快捷键", null, delegate { ShowSettings(); });
            menu.Items.Add("打开截图目录", null, delegate { OpenCaptureDirectory(); });
            menu.Items.Add("打开录屏目录", null, delegate { OpenRecordingDirectory(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            return menu;
        }

        private string MenuText(int hotkeyId)
        {
            HotkeyDefinition definition = HotkeyCatalog.Find(hotkeyId);
            string name = definition == null ? "功能" : definition.Name;
            return name + "  " + HotkeyFormatter.Format(settings.GetHotkey(hotkeyId));
        }

        public void ToggleOverlay(OverlayMode mode)
        {
            if (overlay != null && !overlay.IsDisposed && overlay.CurrentMode == mode)
            {
                overlay.Close();
                overlay = null;
                return;
            }

            ShowOverlay(mode);
        }

        public void Notify(string title, string message)
        {
            notifyIcon.ShowBalloonTip(3500, title, message, ToolTipIcon.Info);
        }

        public void NotifyPending(string feature)
        {
            Notify(feature + " 尚未接入", "这个功能需要新增实时捕获、OCR 或视频编码引擎；当前原型已保留入口，避免生成不可靠结果。");
        }

        public void ShowSettings()
        {
            using (var form = new SettingsForm(settings))
            {
                if (form.ShowDialog() == DialogResult.OK && form.Result != null)
                {
                    settings = form.Result;
                    SettingsStore.Save(settings);
                    hotkeyWindow.RegisterSettings(settings);
                    ContextMenuStrip oldMenu = notifyIcon.ContextMenuStrip;
                    notifyIcon.ContextMenuStrip = BuildMenu();
                    if (oldMenu != null) oldMenu.Dispose();
                    Notify("快捷键已更新", hotkeyWindow.FailedHotkeys.Count == 0 ? "所有启用的快捷键已注册。" : "部分快捷键注册失败，请检查冲突。");
                }
            }
        }

        public void OpenCaptureDirectory()
        {
            try
            {
                Process.Start(AppPaths.CaptureDirectory);
            }
            catch (Exception ex)
            {
                Notify("打开目录失败", ex.Message);
            }
        }

        public void OpenRecordingDirectory()
        {
            try
            {
                Process.Start(AppPaths.RecordingDirectory);
            }
            catch (Exception ex)
            {
                Notify("打开目录失败", ex.Message);
            }
        }

        public void ToggleRecording()
        {
            if (RecordingTool.IsRecording)
            {
                RecordingTool.StopActive();
                Notify("录屏已停止", "GIF 文件已保存到录屏目录。");
            }
            else
            {
                ShowOverlay(OverlayMode.Recording);
            }
        }

        public void RunDemoType()
        {
            try
            {
                string message = demoTypeEngine.TypeNext();
                Notify("DemoType", message);
            }
            catch (Exception ex)
            {
                Notify("DemoType 失败", ex.Message);
            }
        }

        public void RunPreviousDemoType()
        {
            try
            {
                string message = demoTypeEngine.TypePrevious();
                Notify("DemoType", message);
            }
            catch (Exception ex)
            {
                Notify("DemoType 失败", ex.Message);
            }
        }

        public void StartDelayedPixPinCapture()
        {
            Notify("延时截图", "3 秒后进入 PixPin 截图模式。");
            var timer = new Timer();
            activeTimers.Add(timer);
            timer.Interval = 3000;
            timer.Tick += delegate
            {
                timer.Stop();
                activeTimers.Remove(timer);
                timer.Dispose();
                ShowOverlay(OverlayMode.PixPinCapture);
            };
            timer.Start();
        }

        public void PinLatestImage()
        {
            Bitmap bitmap = CaptureStore.GetLatest();
            if (bitmap == null)
            {
                bitmap = ClipboardService.TryGetImage();
            }

            if (bitmap == null)
            {
                Notify("没有可贴图内容", "请先截图，或把图片复制到剪贴板。");
                return;
            }

            Point cursor = Cursor.Position;
            PinManager.Show(bitmap, new Point(cursor.X + 24, cursor.Y + 24), true);
        }

        public void ShowCaptureHistory()
        {
            var form = new CaptureHistoryForm();
            form.Show();
        }

        public void PinWindowUnderCursor()
        {
            Bitmap bitmap = CaptureTool.CaptureWindowUnderCursor();
            if (bitmap == null)
            {
                Notify("窗口贴图失败", "没有识别到可截图的窗口。");
                return;
            }

            CaptureStore.Add(bitmap);
            Point cursor = Cursor.Position;
            PinManager.Show(bitmap, new Point(cursor.X + 24, cursor.Y + 24));
        }

        public void CopyWindowUnderCursor()
        {
            Bitmap bitmap = CaptureTool.CaptureWindowUnderCursor();
            if (bitmap == null)
            {
                Notify("窗口截图失败", "没有识别到可截图的窗口。");
                return;
            }

            CaptureStore.Add(bitmap);
            string error;
            if (ClipboardService.TrySetImage(bitmap, out error))
            {
                Notify("已复制窗口截图", "");
            }
            else
            {
                Notify("复制窗口截图失败", error);
            }
        }

        /// <summary>截图并立即贴图（Snipaste 快速贴图工作流）。</summary>
        public void QuickSnipAndPin()
        {
            ToggleOverlay(OverlayMode.RegionPin);
        }

        /// <summary>一键抓取光标所在整屏显示器，复制到剪贴板（不进 OverlayForm，最快路径）。</summary>
        public void CaptureActiveMonitor()
        {
            try
            {
                Screen screen = Screen.FromPoint(Cursor.Position);
                Bitmap bmp = CaptureTool.CaptureScreen(screen.Bounds);
                CaptureStore.Add(bmp);
                string err;
                ClipboardService.TrySetImage(bmp, out err);
                Notify("已抓取当前显示器", screen.Bounds.Width + " × " + screen.Bounds.Height);
            }
            catch (Exception ex) { Notify("抓取失败", ex.Message); }
        }

        // ===== 截图快贴槽位 1-9 =====
        private static Rectangle[] ParseQuickSlots(string s)
        {
            var result = new Rectangle[9];
            if (string.IsNullOrEmpty(s)) return result;
            string[] parts = s.Split(';');
            for (int i = 0; i < 9 && i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (string.IsNullOrEmpty(p)) continue;
                string[] c = p.Split(',');
                int x, y, w, h;
                if (c.Length == 4 && int.TryParse(c[0], out x) && int.TryParse(c[1], out y) &&
                    int.TryParse(c[2], out w) && int.TryParse(c[3], out h))
                    result[i] = new Rectangle(x, y, w, h);
            }
            return result;
        }

        private static string SerializeQuickSlots(Rectangle[] slots)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 9; i++)
            {
                if (i > 0) sb.Append(';');
                var r = slots[i];
                if (r.Width > 0 && r.Height > 0) sb.Append(r.X + "," + r.Y + "," + r.Width + "," + r.Height);
            }
            return sb.ToString();
        }

        /// <summary>把屏幕区域保存到快贴槽位 N（1..9）。</summary>
        public static void SaveQuickSlot(int slot, Rectangle screenRect)
        {
            if (slot < 1 || slot > 9) return;
            AppSettings prefs = SettingsStore.Load();
            var slots = ParseQuickSlots(prefs.QuickSlots);
            slots[slot - 1] = screenRect;
            prefs.QuickSlots = SerializeQuickSlots(slots);
            SettingsStore.Save(prefs);
        }

        /// <summary>重截并贴图到快贴槽位 N（1..9）。返回是否执行。</summary>
        public bool PinQuickSlot(int slot)
        {
            if (slot < 1 || slot > 9) return false;
            AppSettings prefs = SettingsStore.Load();
            var slots = ParseQuickSlots(prefs.QuickSlots);
            Rectangle rect = slots[slot - 1];
            if (rect.Width < 4 || rect.Height < 4)
            {
                Notify("槽位 " + slot + " 未设置", "在截图选区中按 Alt+" + slot + " 可存入此槽位。");
                return false;
            }
            rect = Rectangle.Intersect(rect, SystemInformation.VirtualScreen);
            if (rect.Width < 4 || rect.Height < 4) return false;
            Bitmap bmp = CaptureTool.CaptureScreen(rect);
            Point cursor = Cursor.Position;
            PinManager.Show(bmp, new Point(rect.Left + 24, rect.Top + 24), true);
            CaptureStore.Add((Bitmap)bmp.Clone());
            return true;
        }

        /// <summary>重截上次截图区域，无需打开遮罩。</summary>
        public void RecaptureLastRegion()
        {
            AppSettings prefs = SettingsStore.Load();
            if (!prefs.HasLastRegion || prefs.LastRegionW < 4 || prefs.LastRegionH < 4)
            {
                Notify("没有上次区域", "请先进行一次截图。");
                return;
            }
            Rectangle rect = new Rectangle(prefs.LastRegionX, prefs.LastRegionY, prefs.LastRegionW, prefs.LastRegionH);
            rect = Rectangle.Intersect(rect, SystemInformation.VirtualScreen);
            if (rect.Width < 4 || rect.Height < 4)
            {
                Notify("上次区域无效", "");
                return;
            }
            Bitmap bmp = CaptureTool.CaptureScreen(rect);
            CaptureStore.Add(bmp);
            string clipError;
            ClipboardService.TrySetImage(bmp, out clipError);
            Notify("已重截上次区域", rect.Width + " × " + rect.Height);
        }

        public void ShowColorPicker()
        {
            var picker = new ColorPickerForm();
            picker.Show();
        }

        /// <summary>显示主窗口仪表盘（单例，关闭=隐藏到托盘）。</summary>
        public void ShowDashboard()
        {
            if (mainForm == null || mainForm.IsDisposed)
            {
                mainForm = new MainForm(this);
            }
            mainForm.Show();
            mainForm.BringToFront();
            if (mainForm.WindowState == FormWindowState.Minimized)
                mainForm.WindowState = FormWindowState.Normal;
            mainForm.Activate();
        }

        public void ShowCommandPalette()
        {
            var palette = new CommandPaletteForm(this);
            palette.Show();
        }

        public void ShowClipboardHistory()
        {
            var form = new ClipboardHistoryForm();
            form.Show();
        }

        private void SavePinGroup()
        {
            if (PinManager.Count == 0) { Notify("没有可保存的贴图", "请先贴一张图。"); return; }
            string name = InputDialog.Show(null, "保存贴图组", "请输入组名：", "组" + DateTime.Now.ToString("MMdd_HHmm"));
            if (string.IsNullOrWhiteSpace(name)) return;
            if (PinManager.SaveGroup(name.Trim())) Notify("已保存贴图组", name.Trim() + "（" + PinManager.Count + " 张）");
            else Notify("保存失败", "");
        }

        private void RestorePinGroup(string name)
        {
            if (PinManager.RestoreGroup(name, true)) Notify("已恢复贴图组", name);
            else Notify("恢复失败", "组 " + name + " 可能已损坏");
        }

        /// <summary>按热键 id 执行动作（命令面板复用）。</summary>
        public void InvokeHotkey(int id)
        {
            if (id == 1) ToggleOverlay(OverlayMode.Zoom);
            else if (id == 2) ToggleOverlay(OverlayMode.Draw);
            else if (id == 3) ToggleOverlay(OverlayMode.Timer);
            else if (id == 4) LiveZoomTool.Toggle();
            else if (id == 5) ToggleRecording();
            else if (id == 6) ToggleOverlay(OverlayMode.Draw);
            else if (id == 7) ToggleOverlay(OverlayMode.RegionCopy);
            else if (id == 8) ToggleOverlay(OverlayMode.RegionSave);
            else if (id == 9) OcrTool.ShowOcrCapture(this);
            else if (id == 10) ToggleOverlay(OverlayMode.LiveDraw);
            else if (id == 11) RunDemoType();
            else if (id == 12) ToggleOverlay(OverlayMode.Text);
            else if (id == 13) ToggleOverlay(OverlayMode.Spotlight);
            else if (id == 14) ToggleOverlay(OverlayMode.PixPinCapture);
            else if (id == 15) ToggleOverlay(OverlayMode.RegionPin);
            else if (id == 16) StartDelayedPixPinCapture();
            else if (id == 17) PinLatestImage();
            else if (id == 18) ShowCaptureHistory();
            else if (id == 19) PinWindowUnderCursor();
            else if (id == 20) CopyWindowUnderCursor();
            else if (id == 21) RunPreviousDemoType();
            else if (id == 22) ToggleOverlay(OverlayMode.ScrollingCapture);
            else if (id == 23) QuickSnipAndPin();
            else if (id == 24) PinManager.ToggleAllVisibility();
            else if (id == 25) PinManager.ToggleClickThroughAt(Cursor.Position);
            else if (id == 26) ShowColorPicker();
            else if (id == 27) RecaptureLastRegion();
            else if (id == 28) ShowCommandPalette();
            else if (id == 29) ShowClipboardHistory();
            else if (id == 30) CaptureActiveMonitor();
            else if (id == 31) ShowDashboard();
        }

        public void Exit()
        {
            ExitThread();
        }

        /// <summary>全局剪贴板变化回调：图片一律记入历史；若开启自动贴图则贴到光标处（按签名去重）。</summary>
        public void OnClipboardChanged()
        {
            try
            {
                if (!Clipboard.ContainsImage()) return;
                Bitmap img = ClipboardService.TryGetImage();
                if (img == null) return;

                // 按尺寸+像素采样生成签名去重（避免 BoardBeam 自身复制图片的回声）
                string sig = img.Width + "x" + img.Height + "@" + ImageSignature(img);
                if (sig == lastClipboardSig) { img.Dispose(); return; }
                lastClipboardSig = sig;

                // 一律记入剪贴板图片历史（供 Ctrl+Shift+V 回贴）；排除 BoardBeam 自身输出避免噪声
                bool self = ClipboardHistoryStore.SuppressNext;
                ClipboardHistoryStore.SuppressNext = false;
                if (!self)
                    ClipboardHistoryStore.Add(img, DateTime.Now);

                AppSettings prefs = SettingsStore.Load();
                if (prefs.AutoPinClipboard)
                {
                    Point cursor = Cursor.Position;
                    PinManager.Show(img, new Point(cursor.X + 24, cursor.Y + 24), true);
                }
                else
                {
                    img.Dispose();
                }
            }
            catch
            {
                // 剪贴板操作可能因锁失败，静默忽略
            }
        }

        /// <summary>采样少量像素生成轻量签名用于去重。</summary>
        private static string ImageSignature(Bitmap bmp)
        {
            var sb = new System.Text.StringBuilder();
            int stepX = Math.Max(1, bmp.Width / 8);
            int stepY = Math.Max(1, bmp.Height / 8);
            for (int y = 0; y < bmp.Height; y += stepY)
                for (int x = 0; x < bmp.Width; x += stepX)
                    sb.Append(bmp.GetPixel(x, y).ToArgb()).Append(',');
            return sb.ToString();
        }

        public void ShowOverlay(OverlayMode mode)
        {
            if (overlay != null && !overlay.IsDisposed)
            {
                overlay.Close();
                overlay = null;
            }

            try
            {
                overlay = new OverlayForm(mode);
                overlay.FormClosed += delegate { overlay = null; };
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception ex)
            {
                overlay = null;
                Notify("打开功能失败", ex.Message);
            }
        }

        protected override void ExitThreadCore()
        {
            if (overlay != null && !overlay.IsDisposed)
            {
                overlay.Close();
            }

            hotkeyWindow.Dispose();
            for (int i = 0; i < activeTimers.Count; i++)
            {
                activeTimers[i].Stop();
                activeTimers[i].Dispose();
            }
            activeTimers.Clear();
            PinManager.CloseAll();
            LiveZoomTool.Shutdown();
            RecordingTool.Shutdown();
            CaptureStore.Clear();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            base.ExitThreadCore();
        }

        private sealed class HotkeyWindow : Form
        {
            private readonly PresenterApplicationContext owner;
            private readonly List<string> failedHotkeys = new List<string>();
            private readonly List<int> registeredIds = new List<int>();

            public HotkeyWindow(PresenterApplicationContext owner)
            {
                this.owner = owner;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                Opacity = 0;
                Size = new Size(1, 1);
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                CreateHandle();
                try { NativeMethods.AddClipboardFormatListener(Handle); } catch { }
            }

            public void RegisterSettings(AppSettings settings)
            {
                UnregisterAll();
                failedHotkeys.Clear();

                foreach (HotkeySetting setting in settings.GetAllHotkeys())
                {
                    if (!setting.Enabled) continue;
                    Register(setting);
                }
            }

            public List<string> FailedHotkeys
            {
                get { return failedHotkeys; }
            }

            private void Register(HotkeySetting setting)
            {
                if (NativeMethods.RegisterHotKey(Handle, setting.Id, setting.Modifiers, (uint)setting.Key))
                {
                    registeredIds.Add(setting.Id);
                }
                else
                {
                    failedHotkeys.Add(HotkeyCatalog.DisplayName(setting.Id) + "  " + HotkeyFormatter.Format(setting));
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id == 1) owner.ToggleOverlay(OverlayMode.Zoom);
                    if (id == 2) owner.ToggleOverlay(OverlayMode.Draw);
                    if (id == 3) owner.ToggleOverlay(OverlayMode.Timer);
                    if (id == 4) LiveZoomTool.Toggle();
                    if (id == 5) owner.ToggleRecording();
                    if (id == 6) owner.ToggleOverlay(OverlayMode.Draw);
                    if (id == 7) owner.ToggleOverlay(OverlayMode.RegionCopy);
                    if (id == 8) owner.ToggleOverlay(OverlayMode.RegionSave);
                    if (id == 9) OcrTool.ShowOcrCapture(owner);
                    if (id == 10) owner.ToggleOverlay(OverlayMode.LiveDraw);
                    if (id == 11) owner.RunDemoType();
                    if (id == 12) owner.ToggleOverlay(OverlayMode.Text);
                    if (id == 13) owner.ToggleOverlay(OverlayMode.Spotlight);
                    if (id == 14) owner.ToggleOverlay(OverlayMode.PixPinCapture);
                    if (id == 15) owner.ToggleOverlay(OverlayMode.RegionPin);
                    if (id == 16) owner.StartDelayedPixPinCapture();
                    if (id == 17) owner.PinLatestImage();
                    if (id == 18) owner.ShowCaptureHistory();
                    if (id == 19) owner.PinWindowUnderCursor();
                    if (id == 20) owner.CopyWindowUnderCursor();
                    if (id == 21) owner.RunPreviousDemoType();
                    if (id == 22) owner.ToggleOverlay(OverlayMode.ScrollingCapture);
                    if (id == 23) owner.QuickSnipAndPin();
                    if (id == 24) PinManager.ToggleAllVisibility();
                    if (id == 25) PinManager.ToggleClickThroughAt(Cursor.Position);
                    if (id == 26) owner.ShowColorPicker();
                    if (id == 27) owner.RecaptureLastRegion();
                    if (id == 28) owner.ShowCommandPalette();
                    if (id == 29) owner.ShowClipboardHistory();
                    if (id == 30) owner.CaptureActiveMonitor();
                    if (id == 31) owner.ShowDashboard();
                }
                else if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
                {
                    owner.OnClipboardChanged();
                }

                base.WndProc(ref m);
            }

            protected override void Dispose(bool disposing)
            {
                try { NativeMethods.RemoveClipboardFormatListener(Handle); } catch { }
                UnregisterAll();
                base.Dispose(disposing);
            }

            private void UnregisterAll()
            {
                for (int i = 0; i < registeredIds.Count; i++)
                {
                    NativeMethods.UnregisterHotKey(Handle, registeredIds[i]);
                }
                registeredIds.Clear();
            }
        }
    }
}

