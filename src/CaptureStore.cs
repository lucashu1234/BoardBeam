using System;
using System.Collections.Generic;
using System.Drawing;

namespace BoardBeam
{
    internal static class CaptureStore
    {
        private const int MaxImages = 20;
        private static readonly List<Bitmap> images = new List<Bitmap>();

        public static void Add(Bitmap image)
        {
            if (image == null) return;

            images.Insert(0, (Bitmap)image.Clone());
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
    }
}

