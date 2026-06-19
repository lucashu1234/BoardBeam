using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class ClipboardService
    {
        public static bool TrySetImage(Bitmap image, out string error)
        {
            error = null;
            if (image == null)
            {
                error = "没有可复制的图片。";
                return false;
            }

            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetText(string text, out string error)
        {
            error = null;
            try
            {
                Clipboard.SetText(text ?? "");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static Bitmap TryGetImage()
        {
            try
            {
                if (!Clipboard.ContainsImage()) return null;
                using (Image image = Clipboard.GetImage())
                {
                    if (image == null) return null;
                    return new Bitmap(image);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

