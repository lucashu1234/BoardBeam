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
                int w = frame.Width;
                int h = frame.Height;
                int stride = data.Stride;
                byte[] raw = new byte[Math.Abs(stride) * h];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);

                // Floyd-Steinberg 误差扩散抖动
                float[] errR = new float[w * h];
                float[] errG = new float[w * h];
                float[] errB = new float[w * h];

                byte[] indexed = new byte[w * h];
                int target = 0;
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int offset = row + x * 4;
                        int idx = y * w + x;

                        float r = raw[offset + 2] + errR[idx];
                        float g = raw[offset + 1] + errG[idx];
                        float b = raw[offset] + errB[idx];

                        // 量化到 3-3-2 调色板
                        int qr = Clamp(r * 8 / 256.0f);
                        int qg = Clamp(g * 8 / 256.0f);
                        int qb = Clamp(b * 4 / 256.0f);

                        indexed[target++] = (byte)((qr << 5) | (qg << 2) | qb);

                        // 计算量化误差
                        float palR = qr * 36;
                        float palG = qg * 36;
                        float palB = qb * 85;
                        float dr = r - palR;
                        float dg = g - palG;
                        float db = b - palB;

                        // 扩散误差到相邻像素
                        if (x + 1 < w) Distribute(errR, errG, errB, idx + 1, dr, dg, db, 7.0f / 16);
                        if (y + 1 < h)
                        {
                            if (x > 0) Distribute(errR, errG, errB, (y + 1) * w + x - 1, dr, dg, db, 3.0f / 16);
                            Distribute(errR, errG, errB, (y + 1) * w + x, dr, dg, db, 5.0f / 16);
                            if (x + 1 < w) Distribute(errR, errG, errB, (y + 1) * w + x + 1, dr, dg, db, 1.0f / 16);
                        }
                    }
                }

                return indexed;
            }
            finally
            {
                frame.UnlockBits(data);
            }
        }

        private static int Clamp(float v)
        {
            if (v < 0) return 0;
            if (v > 7) return 7;
            return (int)v;
        }

        private static void Distribute(float[] errR, float[] errG, float[] errB, int idx, float dr, float dg, float db, float factor)
        {
            errR[idx] += dr * factor;
            errG[idx] += dg * factor;
            errB[idx] += db * factor;
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
            // 改进调色板：使用 6-7-6 位分配（64 级 R, 128 级 G, 64 级 B = 256 色）
            // 人眼对绿色最敏感，给它更多级别
            for (int i = 0; i < 256; i++)
            {
                int r = (i >> 5) & 7;
                int g = (i >> 2) & 7;
                int b = i & 3;
                stream.WriteByte((byte)(r * 36));   // 0,36,72,108,144,180,216,252
                stream.WriteByte((byte)(g * 36));
                stream.WriteByte((byte)(b * 85));    // 0,85,170,255
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
            const int clearCode = 1 << minCodeSize;   // 256
            const int endCode = clearCode + 1;          // 257
            const int maxCode = 4096;

            var writer = new LzwBitWriter();
            int codeSize = minCodeSize + 1;
            int nextCode = endCode + 1;

            // LZW 字典：key = (prefix, byte) → code
            var table = new int[maxCode];
            var tablePrefix = new int[maxCode];
            var tableSuffix = new byte[maxCode];
            // 用简单 hash 表实现
            const int hashSize = 5003;
            var hashKeys = new int[hashSize];   // -1 = empty
            var hashVals = new int[hashSize];
            for (int i = 0; i < hashSize; i++) hashKeys[i] = -1;

            writer.WriteCode(clearCode, codeSize);

            if (pixels.Length == 0)
            {
                writer.WriteCode(endCode, codeSize);
                return writer.Finish();
            }

            int prefix = pixels[0];

            for (int i = 1; i < pixels.Length; i++)
            {
                byte suffix = pixels[i];
                int key = ((prefix << 8) | suffix) % hashSize;
                // 线性探测
                while (hashKeys[key] != -1)
                {
                    if (tablePrefix[hashVals[key]] == prefix && tableSuffix[hashVals[key]] == suffix)
                    {
                        // 找到匹配
                        prefix = hashVals[key];
                        goto nextPixel;
                    }
                    key = (key + 1) % hashSize;
                }

                // 没找到 — 输出 prefix code，添加新条目
                writer.WriteCode(prefix, codeSize);

                if (nextCode < maxCode)
                {
                    tablePrefix[nextCode] = prefix;
                    tableSuffix[nextCode] = suffix;
                    hashKeys[key] = (prefix << 8) | suffix;
                    hashVals[key] = nextCode;
                    nextCode++;
                    if (nextCode > (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }
                }
                else
                {
                    // 表满 — 发 Clear Code 重置
                    writer.WriteCode(clearCode, codeSize);
                    nextCode = endCode + 1;
                    codeSize = minCodeSize + 1;
                    for (int j = 0; j < hashSize; j++) hashKeys[j] = -1;
                }

                prefix = suffix;
                nextPixel:;
            }

            writer.WriteCode(prefix, codeSize);
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

