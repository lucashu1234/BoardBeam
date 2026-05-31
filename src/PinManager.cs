using System.Collections.Generic;
using System.Drawing;

namespace BoardBeam
{
    internal static class PinManager
    {
        private static readonly List<PinForm> pins = new List<PinForm>();

        public static void Show(Bitmap image, Point location)
        {
            var pin = new PinForm(image, location);
            pins.Add(pin);
            pin.FormClosed += delegate { pins.Remove(pin); };
            pin.Show();
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
    }
}

