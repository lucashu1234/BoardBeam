using System;
using System.Collections.Generic;
using System.Drawing;

namespace BoardBeam
{
    internal static class CaptureStore
    {
        private const int MaxImages = 20;
        private static readonly List<Bitmap> images = new List<Bitmap>();

        /// <summary>取得传入 Bitmap 的所有权（不 Clone），调用方不应再使用或 Dispose 该 Bitmap。</summary>
        public static void Add(Bitmap image)
        {
            if (image == null) return;

            images.Insert(0, image);
            while (images.Count > MaxImages)
            {
                Bitmap old = images[images.Count - 1];
                images.RemoveAt(images.Count - 1);
                old.Dispose();
            }
        }

        public static Bitmap GetLatest()
        {
            if (images.Count == 0) return null;
            return (Bitmap)images[0].Clone();
        }

        public static int Count
        {
            get { return images.Count; }
        }

        public static List<Bitmap> GetAll()
        {
            var copies = new List<Bitmap>();
            for (int i = 0; i < images.Count; i++)
            {
                copies.Add((Bitmap)images[i].Clone());
            }
            return copies;
        }

        public static void Clear()
        {
            for (int i = 0; i < images.Count; i++)
            {
                images[i].Dispose();
            }
            images.Clear();
        }

        /// <summary>删除指定索引的截图（与 CaptureHistoryForm 的显示顺序对应）。</summary>
        public static void RemoveAt(int index)
        {
            if (index < 0 || index >= images.Count) return;
            images[index].Dispose();
            images.RemoveAt(index);
        }
    }
}
