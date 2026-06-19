using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BoardBeam
{
    internal static class PinManager
    {
        private static readonly List<PinForm> pins = new List<PinForm>();

        public static void Show(Bitmap image, Point location)
        {
            Show(image, location, false);
        }

        /// <param name="takeOwnership">为 true 时 PinForm 直接引用 image 而不 Clone，节省内存。</param>
        public static void Show(Bitmap image, Point location, bool takeOwnership)
        {
            var pin = new PinForm(image, location, takeOwnership);
            pins.Add(pin);
            pin.FormClosed += delegate { pins.Remove(pin); };
            pin.Show();
            // Show() 会强制 Visible=true，故在 Show 之后再隐藏（保持"全部隐藏"语义）
            if (allHidden) pin.Visible = false;
        }

        public static void CloseAll()
        {
            PinForm[] snapshot = pins.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].IsDisposed)
                {
                    snapshot[i].Close();
                }
            }
            pins.Clear();
        }

        private static bool allHidden;

        /// <summary>切换所有贴图的可见性（保留窗口，区别于 CloseAll）。</summary>
        public static void ToggleAllVisibility()
        {
            allHidden = !allHidden;
            PinForm[] snapshot = pins.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].IsDisposed) continue;
                if (allHidden)
                    snapshot[i].Visible = false;
                else
                {
                    snapshot[i].Visible = true;
                    snapshot[i].TopMost = true; // 恢复置顶
                }
            }
        }

        public static bool IsAllHidden { get { return allHidden; } }
        public static int Count { get { return pins.Count; } }

        /// <summary>切换光标所在贴图的鼠标穿透；返回是否命中贴图。</summary>
        public static bool ToggleClickThroughAt(System.Drawing.Point screenPoint)
        {
            PinForm[] snapshot = pins.ToArray();
            // 从顶层往下找
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                if (snapshot[i].IsDisposed || !snapshot[i].Visible) continue;
                System.Drawing.Rectangle r = snapshot[i].Bounds;
                if (r.Contains(screenPoint))
                {
                    snapshot[i].ToggleClickThrough();
                    return true;
                }
            }
            return false;
        }

        // ===== 贴图组保存与恢复 =====
        /// <summary>把当前所有贴图保存为命名组（每图独立 PNG + 一个 JSON 描述）。</summary>
        public static bool SaveGroup(string name)
        {
            try
            {
                PinForm[] snapshot = pins.ToArray();
                if (snapshot.Length == 0) return false;
                string dir = System.IO.Path.Combine(AppPaths.PinGroupsDirectory, name);
                System.IO.Directory.CreateDirectory(dir);
                // 清空旧文件
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.png")) { try { System.IO.File.Delete(f); } catch { } }

                var lines = new System.Collections.Generic.List<string>();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (snapshot[i].IsDisposed) continue;
                    string imgFile = System.IO.Path.Combine(dir, i + ".png");
                    snapshot[i].SaveSourceTo(imgFile);
                    var s = snapshot[i].GetSnapshot();
                    lines.Add(imgFile + "|" + s.Location.X + "," + s.Location.Y + "|" + s.Scale + "|" + s.Rotation + "|" + s.SignX + "|" + s.SignY + "|" + s.Opacity + "|" + s.Locked + "|" + s.ClickThrough + "|" + s.TopMost);
                }
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "group.txt"), lines.ToArray());
                return true;
            }
            catch (Exception ex) { CrashLogger.Log("保存贴图组", ex); return false; }
        }

        /// <summary>从命名组恢复贴图。可选先关闭现有贴图。</summary>
        public static bool RestoreGroup(string name, bool closeExisting)
        {
            try
            {
                string dir = System.IO.Path.Combine(AppPaths.PinGroupsDirectory, name);
                string meta = System.IO.Path.Combine(dir, "group.txt");
                if (!System.IO.File.Exists(meta)) return false;
                if (closeExisting) CloseAll();

                string[] lines = System.IO.File.ReadAllLines(meta);
                foreach (string line in lines)
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 10) continue;
                    if (!System.IO.File.Exists(parts[0])) continue;
                    using (var bmp = new Bitmap(parts[0]))
                    {
                        string[] loc = parts[1].Split(',');
                        var snap = new PinSnapshot
                        {
                            Location = new System.Drawing.Point(int.Parse(loc[0]), int.Parse(loc[1])),
                            Scale = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                            Rotation = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                            SignX = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                            SignY = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                            Opacity = double.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture),
                            Locked = bool.Parse(parts[7]),
                            ClickThrough = bool.Parse(parts[8]),
                            TopMost = bool.Parse(parts[9])
                        };
                        var pin = new PinForm((Bitmap)bmp.Clone(), snap.Location, true);
                        pins.Add(pin);
                        pin.FormClosed += delegate { pins.Remove(pin); };
                        pin.Show();
                        pin.ApplySnapshot(snap);
                    }
                }
                return true;
            }
            catch (Exception ex) { CrashLogger.Log("恢复贴图组", ex); return false; }
        }

        /// <summary>列出已保存的贴图组名。</summary>
        public static string[] ListGroups()
        {
            try
            {
                string dir = AppPaths.PinGroupsDirectory;
                if (!System.IO.Directory.Exists(dir)) return new string[0];
                var dirs = System.IO.Directory.GetDirectories(dir);
                var names = new string[dirs.Length];
                for (int i = 0; i < dirs.Length; i++) names[i] = System.IO.Path.GetFileName(dirs[i]);
                return names;
            }
            catch { return new string[0]; }
        }
    }

    /// <summary>贴图状态快照（位置/变换/透明度/标志）。</summary>
    internal class PinSnapshot
    {
        public System.Drawing.Point Location;
        public float Scale;
        public float Rotation;
        public float SignX, SignY;
        public double Opacity;
        public bool Locked, ClickThrough, TopMost;
    }
}

