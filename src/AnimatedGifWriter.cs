using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace BoardBeam
{
    internal sealed class AnimatedGifWriter : IDisposable
    {
        private readonly FileStream stream;
        private readonly int width;
        private readonly int height;
        private readonly int delay;
        private bool finished;

        public AnimatedGifWriter(string file, int sourceWidth, int sourceHeight, int fps, int maxWidth, int maxHeight)
        {
            if (fps < 1) fps = 1;
            float scale = 1.0f;
            if (sourceWidth > maxWidth || sourceHeight > maxHeight)
            {
                scale = Math.Min((float)maxWidth / sourceWidth, (float)maxHeight / sourceHeight);
            }

            width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            delay = Math.Max(1, (int)Math.Round(100.0 / fps));

            stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
            WriteHeader();
        }

        public int Width
        {
            get { return width; }
        }

        public int Height
        {
            get { return height; }
        }

        public void AddFrame(Bitmap source)
        {
            if (finished) return;

            using (Bitmap frame = CreateFrame(source))
            {
                byte[] indexedPixels = Quantize(frame);
                WriteGraphicControlExtension();
                WriteImageDescriptor();
                WritePalette();
                WriteImageData(indexedPixels);
            }
        }

        public void Finish()
        {
            if (finished) return;
            stream.WriteByte(0x3B);
            stream.Flush();
            finished = true;
        }

        public void Dispose()
        {
            Finish();
            stream.Dispose();
        }

        private void WriteHeader()
        {
            WriteAscii("GIF89a");
            WriteShort(width);
            WriteShort(height);
            stream.WriteByte(0x00);
            stream.WriteByte(0x00);
            stream.WriteByte(0x00);

            stream.WriteByte(0x21);
            stream.WriteByte(0xFF);
            stream.WriteByte(0x0B);
            WriteAscii("NETSCAPE2.0");
            stream.WriteByte(0x03);
            stream.WriteByte(0x01);
            WriteShort(0);
            stream.WriteByte(0x00);
        }

        private Bitmap CreateFrame(Bitmap source)
        {
            var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, width, height);
            }
            return frame;
        }

        private static byte[] Quantize(Bitmap frame)
        {
            Rectangle rect = new Rectangle(0, 0, frame.Width, frame.Height);
            BitmapData data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * frame.Height;
                byte[] raw = new byte[bytes];
                Marshal.Copy(data.Scan0, raw, 0, bytes);

                byte[] indexed = new byte[frame.Width * frame.Height];
                int target = 0;
                for (int y = 0; y < frame.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int offset = row + x * 4;
                        int b = raw[offset] >> 6;
                        int g = raw[offset + 1] >> 5;
                        int r = raw[offset + 2] >> 5;
                        indexed[target++] = (byte)((r << 5) | (g << 2) | b);
                    }
                }

                return indexed;
            }
            finally
            {
                frame.UnlockBits(data);
            }
        }

        private void WriteGraphicControlExtension()
        {
            stream.WriteByte(0x21);
            stream.WriteByte(0xF9);
            stream.WriteByte(0x04);
            stream.WriteByte(0x08);
            WriteShort(delay);
            stream.WriteByte(0x00);
            stream.WriteByte(0x00);
        }

        private void WriteImageDescriptor()
        {
            stream.WriteByte(0x2C);
            WriteShort(0);
            WriteShort(0);
            WriteShort(width);
            WriteShort(height);
            stream.WriteByte(0x87);
        }

        private void WritePalette()
        {
            for (int i = 0; i < 256; i++)
            {
                int r = (i >> 5) & 7;
                int g = (i >> 2) & 7;
                int b = i & 3;
                stream.WriteByte((byte)(r * 255 / 7));
                stream.WriteByte((byte)(g * 255 / 7));
                stream.WriteByte((byte)(b * 255 / 3));
            }
        }

        private void WriteImageData(byte[] indexedPixels)
        {
            stream.WriteByte(8);
            byte[] encoded = EncodeLzw(indexedPixels);
            int offset = 0;
            while (offset < encoded.Length)
            {
                int count = Math.Min(255, encoded.Length - offset);
                stream.WriteByte((byte)count);
                stream.Write(encoded, offset, count);
                offset += count;
            }
            stream.WriteByte(0x00);
        }

        private static byte[] EncodeLzw(byte[] pixels)
        {
            const int minCodeSize = 8;
            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int nextCode = endCode + 1;
            int codeSize = minCodeSize + 1;
            bool firstAfterClear = true;

            var writer = new LzwBitWriter();
            writer.WriteCode(clearCode, codeSize);

            for (int i = 0; i < pixels.Length; i++)
            {
                writer.WriteCode(pixels[i], codeSize);
                if (firstAfterClear)
                {
                    firstAfterClear = false;
                }
                else
                {
                    nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }

                    if (nextCode >= 4096)
                    {
                        writer.WriteCode(clearCode, codeSize);
                        nextCode = endCode + 1;
                        codeSize = minCodeSize + 1;
                        firstAfterClear = true;
                    }
                }
            }

            writer.WriteCode(endCode, codeSize);
            return writer.Finish();
        }

        private void WriteShort(int value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private void WriteAscii(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                stream.WriteByte((byte)text[i]);
            }
        }

        private sealed class LzwBitWriter
        {
            private readonly MemoryStream output = new MemoryStream();
            private int bitBuffer;
            private int bitCount;

            public void WriteCode(int code, int size)
            {
                bitBuffer |= code << bitCount;
                bitCount += size;
                while (bitCount >= 8)
                {
                    output.WriteByte((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            public byte[] Finish()
            {
                if (bitCount > 0)
                {
                    output.WriteByte((byte)(bitBuffer & 0xFF));
                    bitBuffer = 0;
                    bitCount = 0;
                }
                return output.ToArray();
            }
        }
    }
}

