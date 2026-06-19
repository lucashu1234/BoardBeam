using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BoardBeam
{
    /// <summary>
    /// 剪贴板图片历史：监听全局剪贴板（WM_CLIPBOARDUPDATE），把复制的图片存为缩略图持久化。
    /// 内存只留缩略图，原图按需从磁盘加载。跨进程持久化，比系统 Win+V 更适合图片。
    /// </summary>
    internal static class ClipboardHistoryStore
    {
        private const int MaxItems = 30;
        private const int ThumbW = 200;

        /// <summary>BoardBeam 自身输出到剪贴板前置位，OnClipboardChanged 据此跳过记录，避免历史被自家截图淹没。</summary>
        public static bool SuppressNext;

        /// <summary>把一张图加入历史（去重）。原图存盘 + 缩略图入内存列表。</summary>
        public static void Add(Bitmap fullImage, DateTime when)
        {
            if (fullImage == null) return;
            try
            {
                string stamp = when.ToString("yyyyMMdd_HHmmss_fff");
                string fullFile = Path.Combine(AppPaths.ClipboardHistoryDirectory, stamp + ".png");
                // 限制原图尺寸避免磁盘膨胀（超 1920 宽按比例缩）
                using (Bitmap toSave = MaybeDownscale(fullImage, 1920))
                    toSave.Save(fullFile, ImageFormat.Png);

                // 缩略图入内存
                var thumb = MakeThumbnail(fullImage, ThumbW);
                Items.Insert(0, new HistoryItem { FullPath = fullFile, Thumbnail = thumb, When = when });
                while (Items.Count > MaxItems)
                {
                    var old = Items[Items.Count - 1];
                    Items.RemoveAt(Items.Count - 1);
                    if (old.Thumbnail != null) old.Thumbnail.Dispose();
                    try { if (File.Exists(old.FullPath)) File.Delete(old.FullPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Log("剪贴板历史入队", ex);
            }
        }

        public class HistoryItem
        {
            public string FullPath;
            public Bitmap Thumbnail;
            public DateTime When;
        }
        public static readonly List<HistoryItem> Items = new List<HistoryItem>();

        private static Bitmap MakeThumbnail(Bitmap src, int maxW)
        {
            float ratio = Math.Min(1f, (float)maxW / src.Width);
            int w = Math.Max(1, (int)(src.Width * ratio));
            int h = Math.Max(1, (int)(src.Height * ratio));
            var t = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return t;
        }

        private static Bitmap MaybeDownscale(Bitmap src, int maxW)
        {
            if (src.Width <= maxW) return new Bitmap(src);
            float ratio = (float)maxW / src.Width;
            int w = maxW;
            int h = Math.Max(1, (int)(src.Height * ratio));
            var t = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return t;
        }
    }
}
