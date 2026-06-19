using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BoardBeam
{
    internal enum HotkeyAction
    {
        Zoom,
        Draw,
        Timer,
        LiveZoom,
        Recording,
        RegionCopy,
        RegionSave,
        Ocr,
        LiveDraw,
        DemoTypeNext,
        Text,
        Spotlight,
        PixPinCapture,
        RegionPin,
        DelayedCapture,
        PinLatest,
        CaptureHistory,
        PinWindow,
        CopyWindow,
        DemoTypePrevious,
        ScrollingCapture,
        QuickPaste,         // 截图并立即贴图
        TogglePins,         // 显示/隐藏所有贴图
        ClickThrough,       // 切换光标下贴图的鼠标穿透
        ColorPick,          // 屏幕取色器
        RecaptureLastRegion, // 重截上次区域
        CommandPalette,      // 命令面板（Ctrl+Space 搜索执行所有动作）
        ClipboardHistory     // 剪贴板图片历史（Ctrl+Shift+V）
    }

    internal sealed class HotkeyDefinition
    {
        public int Id;
        public string Name;
        public HotkeyAction Action;
        public Keys DefaultKey;
        public uint DefaultModifiers;

        public HotkeySetting CreateDefault()
        {
            return new HotkeySetting
            {
                Id = Id,
                Enabled = true,
                Key = DefaultKey,
                Modifiers = DefaultModifiers
            };
        }
    }

    internal sealed class HotkeySetting
    {
        public int Id;
        public bool Enabled;
        public Keys Key;
        public uint Modifiers;

        public HotkeySetting Clone()
        {
            return new HotkeySetting
            {
                Id = Id,
                Enabled = Enabled,
                Key = Key,
                Modifiers = Modifiers
            };
        }
    }

    internal sealed class AppSettings
    {
        private readonly Dictionary<int, HotkeySetting> hotkeys = new Dictionary<int, HotkeySetting>();

        // 持久化偏好设置
        public int DefaultColorArgb = Color.Red.ToArgb();
        public float DefaultWidth = 5.0f;
        public int DefaultStampType;
        public int DefaultCountdownSeconds = 10 * 60;
        public int LastTool;                // 上次使用的 DrawingTool 枚举值
        public int LastOverlayMode;         // 上次使用的 OverlayMode 枚举值
        public int SaveFormat;              // 0=自动, 1=PNG, 2=JPG, 3=BMP

        // 重截上次区域（持久化）
        public int LastRegionX, LastRegionY, LastRegionW, LastRegionH;
        public bool HasLastRegion;

        // 取色器历史（最多 16 个 ARGB）
        public List<int> ColorHistory = new List<int>();

        // 剪贴板图片自动贴图（默认关闭，避免误触）
        public bool AutoPinClipboard;

        // 开机自启
        public bool AutostartEnabled;

        // 截图水印
        public bool WatermarkEnabled;
        public string WatermarkText = "";
        public int WatermarkPosition = 0;  // 0=右下 1=左下 2=右上 3=左上 4=居中
        public int WatermarkOpacityPct = 50;  // 0..100

        // 截图快贴槽位 1-9：格式 "x,y,w,h;x,y,w,h;..." 每段一个槽（空段=未设置）
        public string QuickSlots = "";

        public HotkeySetting GetHotkey(int id)
        {
            HotkeySetting setting;
            if (hotkeys.TryGetValue(id, out setting))
            {
                return setting;
            }

            HotkeyDefinition definition = HotkeyCatalog.Find(id);
            if (definition == null) return null;
            setting = definition.CreateDefault();
            hotkeys[id] = setting;
            return setting;
        }

        public void SetHotkey(HotkeySetting setting)
        {
            if (setting == null) return;
            hotkeys[setting.Id] = setting.Clone();
        }

        public List<HotkeySetting> GetAllHotkeys()
        {
            var list = new List<HotkeySetting>();
            for (int i = 0; i < HotkeyCatalog.Definitions.Length; i++)
            {
                list.Add(GetHotkey(HotkeyCatalog.Definitions[i].Id).Clone());
            }
            return list;
        }

        public AppSettings Clone()
        {
            var copy = new AppSettings();
            copy.DefaultColorArgb = DefaultColorArgb;
            copy.DefaultWidth = DefaultWidth;
            copy.DefaultStampType = DefaultStampType;
            copy.DefaultCountdownSeconds = DefaultCountdownSeconds;
            copy.LastTool = LastTool;
            copy.LastOverlayMode = LastOverlayMode;
            copy.SaveFormat = SaveFormat;
            copy.LastRegionX = LastRegionX;
            copy.LastRegionY = LastRegionY;
            copy.LastRegionW = LastRegionW;
            copy.LastRegionH = LastRegionH;
            copy.HasLastRegion = HasLastRegion;
            copy.AutoPinClipboard = AutoPinClipboard;
            copy.AutostartEnabled = AutostartEnabled;
            copy.WatermarkEnabled = WatermarkEnabled;
            copy.WatermarkText = WatermarkText;
            copy.WatermarkPosition = WatermarkPosition;
            copy.WatermarkOpacityPct = WatermarkOpacityPct;
            copy.QuickSlots = QuickSlots;
            foreach (int c in ColorHistory) copy.ColorHistory.Add(c);
            foreach (HotkeySetting setting in GetAllHotkeys())
            {
                copy.SetHotkey(setting);
            }
            return copy;
        }
    }

    internal static class HotkeyCatalog
    {
        public static readonly HotkeyDefinition[] Definitions = new[]
        {
            Def(1, "冻结缩放", HotkeyAction.Zoom, Keys.D1, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(2, "批注画线", HotkeyAction.Draw, Keys.D2, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(3, "课堂计时", HotkeyAction.Timer, Keys.D3, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(4, "LiveZoom", HotkeyAction.LiveZoom, Keys.D4, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(5, "录屏", HotkeyAction.Recording, Keys.D5, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(6, "一键批注", HotkeyAction.Draw, Keys.F9, NativeMethods.MOD_NOREPEAT),
            Def(7, "区域截图复制", HotkeyAction.RegionCopy, Keys.D6, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(8, "区域截图保存", HotkeyAction.RegionSave, Keys.D6, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT),
            Def(9, "OCR", HotkeyAction.Ocr, Keys.D6, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(10, "LiveDraw", HotkeyAction.LiveDraw, Keys.D4, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT),
            Def(11, "DemoType 下一段", HotkeyAction.DemoTypeNext, Keys.D7, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(12, "输入文字", HotkeyAction.Text, Keys.T, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(13, "聚光灯", HotkeyAction.Spotlight, Keys.L, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(14, "PixPin 截图", HotkeyAction.PixPinCapture, Keys.D1, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(15, "PixPin 贴图截图", HotkeyAction.RegionPin, Keys.D2, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(16, "PixPin 延时截图", HotkeyAction.DelayedCapture, Keys.D3, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(17, "贴最新截图", HotkeyAction.PinLatest, Keys.D2, NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT),
            Def(18, "截图历史", HotkeyAction.CaptureHistory, Keys.D4, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(19, "窗口贴图", HotkeyAction.PinWindow, Keys.D5, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(20, "窗口截图复制", HotkeyAction.CopyWindow, Keys.D5, NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT),
            Def(21, "DemoType 上一段", HotkeyAction.DemoTypePrevious, Keys.D7, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT),
            Def(22, "滚动长截图", HotkeyAction.ScrollingCapture, Keys.D6, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(23, "快速贴图(截图并钉)", HotkeyAction.QuickPaste, Keys.D7, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(24, "显示/隐藏所有贴图", HotkeyAction.TogglePins, Keys.D7, NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT),
            Def(25, "切换鼠标穿透", HotkeyAction.ClickThrough, Keys.D8, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(26, "屏幕取色", HotkeyAction.ColorPick, Keys.C, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(27, "重截上次区域", HotkeyAction.RecaptureLastRegion, Keys.R, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT),
            Def(28, "命令面板", HotkeyAction.CommandPalette, Keys.Space, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT),
            Def(29, "剪贴板图片历史", HotkeyAction.ClipboardHistory, Keys.V, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT)
        };

        private static HotkeyDefinition Def(int id, string name, HotkeyAction action, Keys key, uint modifiers)
        {
            return new HotkeyDefinition
            {
                Id = id,
                Name = name,
                Action = action,
                DefaultKey = key,
                DefaultModifiers = modifiers
            };
        }

        public static HotkeyDefinition Find(int id)
        {
            for (int i = 0; i < Definitions.Length; i++)
            {
                if (Definitions[i].Id == id) return Definitions[i];
            }
            return null;
        }

        public static string DisplayName(int id)
        {
            HotkeyDefinition definition = Find(id);
            return definition == null ? "Hotkey " + id : definition.Name;
        }
    }

    internal static class HotkeyFormatter
    {
        public static string Format(HotkeySetting setting)
        {
            if (setting == null || !setting.Enabled) return "未启用";
            var parts = new List<string>();
            if ((setting.Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((setting.Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
            if ((setting.Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((setting.Modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(FormatKey(setting.Key));
            return string.Join(" + ", parts.ToArray());
        }

        private static string FormatKey(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            return key.ToString();
        }
    }

    internal static class SettingsStore
    {
        public static AppSettings Load()
        {
            AppSettings settings = CreateDefaults();
            string file = AppPaths.SettingsFile;
            if (!File.Exists(file)) return settings;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] pair = line.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;

                string key = pair[0].Trim();
                string val = pair[1].Trim();

                if (key.StartsWith("Hotkey."))
                {
                    int id;
                    if (!int.TryParse(key.Substring("Hotkey.".Length), out id)) continue;
                    HotkeySetting parsed = ParseHotkey(id, val);
                    if (parsed != null) settings.SetHotkey(parsed);
                }
                else if (key == "DefaultColor") { int v; if (int.TryParse(val, out v)) settings.DefaultColorArgb = v; }
                else if (key == "DefaultWidth") { float v; if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) settings.DefaultWidth = v; }
                else if (key == "DefaultStampType") { int v; if (int.TryParse(val, out v)) settings.DefaultStampType = v; }
                else if (key == "DefaultCountdown") { int v; if (int.TryParse(val, out v)) settings.DefaultCountdownSeconds = v; }
                else if (key == "LastTool") { int v; if (int.TryParse(val, out v)) settings.LastTool = v; }
                else if (key == "LastOverlayMode") { int v; if (int.TryParse(val, out v)) settings.LastOverlayMode = v; }
                else if (key == "SaveFormat") { int v; if (int.TryParse(val, out v)) settings.SaveFormat = v; }
                else if (key == "HasLastRegion") { bool v; if (bool.TryParse(val, out v)) settings.HasLastRegion = v; }
                else if (key == "LastRegion") { ParseRect(val, settings); }
                else if (key == "AutoPinClipboard") { bool v; if (bool.TryParse(val, out v)) settings.AutoPinClipboard = v; }
                else if (key == "Autostart") { bool v; if (bool.TryParse(val, out v)) settings.AutostartEnabled = v; }
                else if (key == "WatermarkEnabled") { bool v; if (bool.TryParse(val, out v)) settings.WatermarkEnabled = v; }
                else if (key == "WatermarkText") { settings.WatermarkText = val; }
                else if (key == "WatermarkPosition") { int v; if (int.TryParse(val, out v)) settings.WatermarkPosition = v; }
                else if (key == "WatermarkOpacity") { int v; if (int.TryParse(val, out v)) settings.WatermarkOpacityPct = v; }
                else if (key == "QuickSlots") { settings.QuickSlots = val; }
                else if (key == "ColorHistory") { ParseColorHistory(val, settings); }
            }

            return settings;
        }

        private static void ParseRect(string val, AppSettings settings)
        {
            string[] p = val.Split(',');
            int x, y, w, h;
            if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) &&
                int.TryParse(p[2], out w) && int.TryParse(p[3], out h))
            {
                settings.LastRegionX = x; settings.LastRegionY = y;
                settings.LastRegionW = w; settings.LastRegionH = h;
            }
        }

        private static void ParseColorHistory(string val, AppSettings settings)
        {
            if (string.IsNullOrEmpty(val)) return;
            string[] parts = val.Split(',');
            for (int i = 0; i < parts.Length && settings.ColorHistory.Count < 16; i++)
            {
                int c;
                if (int.TryParse(parts[i], out c)) settings.ColorHistory.Add(c);
            }
        }

        public static AppSettings CreateDefaults()
        {
            var settings = new AppSettings();
            for (int i = 0; i < HotkeyCatalog.Definitions.Length; i++)
            {
                settings.SetHotkey(HotkeyCatalog.Definitions[i].CreateDefault());
            }
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            var lines = new List<string>();
            lines.Add("# BoardBeam settings");
            lines.Add("DefaultColor=" + settings.DefaultColorArgb);
            lines.Add("DefaultWidth=" + settings.DefaultWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            lines.Add("DefaultStampType=" + settings.DefaultStampType);
            lines.Add("DefaultCountdown=" + settings.DefaultCountdownSeconds);
            lines.Add("LastTool=" + settings.LastTool);
            lines.Add("LastOverlayMode=" + settings.LastOverlayMode);
            lines.Add("SaveFormat=" + settings.SaveFormat);
            lines.Add("HasLastRegion=" + settings.HasLastRegion);
            lines.Add("LastRegion=" + settings.LastRegionX + "," + settings.LastRegionY + "," + settings.LastRegionW + "," + settings.LastRegionH);
            lines.Add("AutoPinClipboard=" + settings.AutoPinClipboard);
            lines.Add("Autostart=" + settings.AutostartEnabled);
            lines.Add("WatermarkEnabled=" + settings.WatermarkEnabled);
            lines.Add("WatermarkText=" + (settings.WatermarkText ?? ""));
            lines.Add("WatermarkPosition=" + settings.WatermarkPosition);
            lines.Add("WatermarkOpacity=" + settings.WatermarkOpacityPct);
            lines.Add("QuickSlots=" + (settings.QuickSlots ?? ""));
            lines.Add("ColorHistory=" + string.Join(",", settings.ColorHistory));
            foreach (HotkeySetting hotkey in settings.GetAllHotkeys())
            {
                lines.Add("Hotkey." + hotkey.Id + "=" + hotkey.Enabled + "," + hotkey.Modifiers + "," + hotkey.Key);
            }
            // 原子写入：先写临时文件，再替换，防止崩溃时丢失所有设置
            string targetPath = AppPaths.SettingsFile;
            string tempPath = targetPath + ".tmp";
            try
            {
                File.WriteAllLines(tempPath, lines.ToArray());
                File.Copy(tempPath, targetPath, true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static HotkeySetting ParseHotkey(int id, string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length != 3) return null;

            bool enabled;
            uint modifiers;
            Keys key;
            if (!bool.TryParse(parts[0], out enabled)) return null;
            if (!uint.TryParse(parts[1], out modifiers)) return null;
            try
            {
                key = (Keys)Enum.Parse(typeof(Keys), parts[2], true);
            }
            catch
            {
                return null;
            }

            return new HotkeySetting
            {
                Id = id,
                Enabled = enabled,
                Modifiers = modifiers,
                Key = key
            };
        }
    }
}

