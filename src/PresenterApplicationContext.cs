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

        public PresenterApplicationContext()
        {
            settings = SettingsStore.Load();
            demoTypeEngine = new DemoTypeEngine();
            hotkeyWindow = new HotkeyWindow(this);
            hotkeyWindow.RegisterSettings(settings);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Text = "BoardBeam";
            notifyIcon.Visible = true;
            notifyIcon.ContextMenuStrip = BuildMenu();
            notifyIcon.DoubleClick += delegate { ShowOverlay(OverlayMode.Draw); };
            notifyIcon.ShowBalloonTip(2500, "BoardBeam", "已启动。F9 一键批注，Ctrl+1 缩放，Ctrl+2 批注。", ToolTipIcon.Info);
            if (hotkeyWindow.FailedHotkeys.Count > 0)
            {
                notifyIcon.ShowBalloonTip(5000, "部分快捷键注册失败", string.Join("\n", hotkeyWindow.FailedHotkeys.ToArray()), ToolTipIcon.Warning);
            }
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
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
            menu.Items.Add(MenuText(14), null, delegate { ShowOverlay(OverlayMode.PixPinCapture); });
            menu.Items.Add(MenuText(15), null, delegate { ShowOverlay(OverlayMode.RegionPin); });
            menu.Items.Add(MenuText(16), null, delegate { StartDelayedPixPinCapture(); });
            menu.Items.Add(MenuText(18), null, delegate { ShowCaptureHistory(); });
            menu.Items.Add(MenuText(19), null, delegate { PinWindowUnderCursor(); });
            menu.Items.Add(MenuText(20), null, delegate { CopyWindowUnderCursor(); });
            menu.Items.Add(MenuText(22), null, delegate { ShowOverlay(OverlayMode.ScrollingCapture); });
            menu.Items.Add(MenuText(17), null, delegate { PinLatestImage(); });
            menu.Items.Add("关闭所有贴图", null, delegate { PinManager.CloseAll(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("白板", null, delegate { ShowOverlay(OverlayMode.Whiteboard); });
            menu.Items.Add("黑板", null, delegate { ShowOverlay(OverlayMode.Blackboard); });
            menu.Items.Add(new ToolStripSeparator());
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
            PinManager.Show(bitmap, new Point(cursor.X + 24, cursor.Y + 24));
            bitmap.Dispose();
        }

        public void ShowCaptureHistory()
        {
            var form = new CaptureHistoryForm();
            form.Show();
        }

        public void PinWindowUnderCursor()
        {
            using (Bitmap bitmap = CaptureTool.CaptureWindowUnderCursor())
            {
                if (bitmap == null)
                {
                    Notify("窗口贴图失败", "没有识别到可截图的窗口。");
                    return;
                }

                CaptureStore.Add(bitmap);
                Point cursor = Cursor.Position;
                PinManager.Show(bitmap, new Point(cursor.X + 24, cursor.Y + 24));
            }
        }

        public void CopyWindowUnderCursor()
        {
            using (Bitmap bitmap = CaptureTool.CaptureWindowUnderCursor())
            {
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
                    if (id == 4) owner.NotifyPending("LiveZoom");
                    if (id == 5) owner.ToggleRecording();
                    if (id == 6) owner.ToggleOverlay(OverlayMode.Draw);
                    if (id == 7) owner.ToggleOverlay(OverlayMode.RegionCopy);
                    if (id == 8) owner.ToggleOverlay(OverlayMode.RegionSave);
                    if (id == 9) owner.NotifyPending("OCR");
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
                }

                base.WndProc(ref m);
            }

            protected override void Dispose(bool disposing)
            {
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

