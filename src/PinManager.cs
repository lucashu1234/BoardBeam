using System.Collections.Generic;
using System.Drawing;

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
    }
}

