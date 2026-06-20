using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>
    /// DPI 缩放辅助。统一规则：所有自定义绘制的整数常量和 GraphicsUnit.Pixel 字号
    /// 都经此乘以 (当前DPI/96)。OnPaint 内用 Factor(Graphics)（绘制 Graphics 已是 DPI 校正的）；
    /// 构造期/控件尺寸用 Factor(Control/Handle)。
    /// 与 AutoScaleMode.Dpi 配合：后者管标准控件布局，本类管自定义 GDI+ 绘制。
    /// </summary>
    internal static class DpiScale
    {
        /// <summary>96-DPI 基准下的缩放因子（用于构造期/控件尺寸）。</summary>
        public static float Factor(Control c)
        {
            if (c != null && c.IsHandleCreated)
            {
                try { return Factor(c.Handle); } catch { }
            }
            return 1f;
        }

        public static float Factor(IntPtr hwnd)
        {
            try
            {
                uint dpi = NativeMethods.DpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch { }
            return 1f;
        }

        /// <summary>OnPaint 内用：绘制 Graphics 已是当前 DPI 校正的，DpiX/96 即缩放因子。</summary>
        public static float Factor(Graphics g)
        {
            if (g != null)
            {
                try { return g.DpiX / 96f; } catch { }
            }
            return 1f;
        }

        /// <summary>整数按因子缩放（四舍五入，最小 1）。</summary>
        public static int Scale(int value, float factor)
        {
            if (factor <= 0) factor = 1f;
            int r = (int)Math.Round(value * factor);
            return r < 1 ? 1 : r;
        }

        public static float ScaleF(float value, float factor)
        {
            if (factor <= 0) factor = 1f;
            return value * factor;
        }
    }
}
