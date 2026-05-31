using System;
using System.Collections.Generic;
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
        ScrollingCapture
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
            Def(22, "滚动长截图", HotkeyAction.ScrollingCapture, Keys.D6, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT)
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
                if (!pair[0].StartsWith("Hotkey.")) continue;

                int id;
                if (!int.TryParse(pair[0].Substring("Hotkey.".Length), out id)) continue;
                HotkeySetting parsed = ParseHotkey(id, pair[1]);
                if (parsed != null) settings.SetHotkey(parsed);
            }

            return settings;
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
            foreach (HotkeySetting hotkey in settings.GetAllHotkeys())
            {
                lines.Add("Hotkey." + hotkey.Id + "=" + hotkey.Enabled + "," + hotkey.Modifiers + "," + hotkey.Key);
            }
            File.WriteAllLines(AppPaths.SettingsFile, lines.ToArray());
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

