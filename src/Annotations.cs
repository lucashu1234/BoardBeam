using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BoardBeam
{
    internal abstract class Annotation
    {
        public abstract void Draw(Graphics g);
        public abstract Annotation Clone();
        public abstract bool HitTest(PointF point, float tolerance, Graphics g);

        /// <summary>编辑原语：整体平移标注。子类需重写以实际移动其几何数据。</summary>
        public virtual void Translate(float dx, float dy) { }

        /// <summary>返回标注的包围盒（图像坐标系）。默认返回空，子类按几何计算。</summary>
        public virtual RectangleF GetBounds(Graphics g) { return RectangleF.Empty; }

        /// <summary>命中 8 个调整手柄之一，返回 0-7（TL,T,BR,BL,T,R,B,L 顺时针），-1 表示未命中。</summary>
        public virtual int HitTestHandle(PointF point, Graphics g) { return -1; }

        /// <summary>按手柄索引调整标注尺寸。anchor 为对角锚点（图像坐标），newRect 为新包围盒。</summary>
        public virtual void ResizeByHandle(int handle, RectangleF newBounds) { }

        protected static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        protected static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f)
            {
                return Distance(p, a);
            }

            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        protected static RectangleF Normalize(PointF a, PointF b)
        {
            return new RectangleF(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X),
                Math.Abs(a.Y - b.Y));
        }

        /// <summary>给定包围盒，判断 point 是否命中某个角/边手柄（手柄半径 tol）。</summary>
        protected static int HitTestRectHandles(PointF point, RectangleF bounds, float tol)
        {
            PointF[] handles = RectHandlePoints(bounds);
            for (int i = 0; i < handles.Length; i++)
            {
                if (Math.Abs(point.X - handles[i].X) <= tol && Math.Abs(point.Y - handles[i].Y) <= tol)
                    return i;
            }
            return -1;
        }

        /// <summary>返回 8 个手柄位置，顺序：0=TL 1=T 2=TR 3=R 4=BR 5=B 6=BL 7=L。</summary>
        protected static PointF[] RectHandlePoints(RectangleF bounds)
        {
            float l = bounds.X, t = bounds.Y, r = bounds.Right, b = bounds.Bottom;
            float cx = (l + r) / 2f, cy = (t + b) / 2f;
            return new PointF[]
            {
                new PointF(l, t),   // 0 TL
                new PointF(cx, t),  // 1 T
                new PointF(r, t),   // 2 TR
                new PointF(r, cy),  // 3 R
                new PointF(r, b),   // 4 BR
                new PointF(cx, b),  // 5 B
                new PointF(l, b),   // 6 BL
                new PointF(l, cy),  // 7 L
            };
        }
    }

    internal class StrokeAnnotation : Annotation
    {
        public readonly List<PointF> Points;
        public Color Color;
        public float Width;
        public bool Highlighter;
        public float Opacity = 1.0f;  // 0..1，1=不透明，荧光笔默认 0.37

        public StrokeAnnotation()
        {
            Points = new List<PointF>();
            Color = Color.Red;
            Width = 5;
            Highlighter = false;
            _forceOpaque = false;
        }

        /// <summary>内部标记：离屏渲染时强制不透明绘制（用于消除荧光笔色带）。</summary>
        internal bool _forceOpaque;

        public override void Draw(Graphics g)
        {
            if (Points.Count == 0) return;

            float effOpacity = Highlighter ? (_forceOpaque ? 1.0f : 0.37f) : Opacity;
            Color drawColor = Color.FromArgb((int)(255 * effOpacity), Color);
            float drawWidth = Highlighter ? Width * 3.0f : Width;

            using (var pen = new Pen(drawColor, drawWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (Points.Count == 1)
                {
                    PointF p = Points[0];
                    float r = drawWidth / 2.0f;
                    using (var brush = new SolidBrush(drawColor))
                    {
                        g.FillEllipse(brush, p.X - r, p.Y - r, drawWidth, drawWidth);
                    }
                    return;
                }

                if (Points.Count < 4)
                {
                    g.DrawLines(pen, Points.ToArray());
                    return;
                }

                using (var path = new GraphicsPath())
                {
                    path.AddCurve(Points.ToArray(), 0.35f);
                    g.DrawPath(pen, path);
                }
            }
        }

        public override Annotation Clone()
        {
            var copy = new StrokeAnnotation();
            copy.Color = Color;
            copy.Width = Width;
            copy.Highlighter = Highlighter;
            copy.Opacity = Opacity;
            for (int i = 0; i < Points.Count; i++)
            {
                copy.Points.Add(Points[i]);
            }
            return copy;
        }

        public override bool HitTest(PointF point, float tolerance, Graphics g)
        {
            if (Points.Count == 0) return false;
            float limit = tolerance + Math.Max(Width, Highlighter ? Width * 3.0f : Width);
            if (Points.Count == 1) return Distance(point, Points[0]) <= limit;

            for (int i = 1; i < Points.Count; i++)
            {
                if (DistanceToSegment(point, Points[i - 1], Points[i]) <= limit)
                {
                    return true;
                }
            }
            return false;
        }

        public override void Translate(float dx, float dy)
        {
            for (int i = 0; i < Points.Count; i++)
            {
                Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
            }
        }

        public override RectangleF GetBounds(Graphics g)
        {
            if (Points.Count == 0) return RectangleF.Empty;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < Points.Count; i++)
            {
                if (Points[i].X < minX) minX = Points[i].X;
                if (Points[i].Y < minY) minY = Points[i].Y;
                if (Points[i].X > maxX) maxX = Points[i].X;
                if (Points[i].Y > maxY) maxY = Points[i].Y;
            }
            float pad = Math.Max(Width, Highlighter ? Width * 3.0f : Width);
            return RectangleF.Inflate(new RectangleF(minX, minY, maxX - minX, maxY - minY), pad, pad);
        }

        public override int HitTestHandle(PointF point, Graphics g)
        {
            return HitTestRectHandles(point, GetBounds(g), 8f);
        }

        public override void ResizeByHandle(int handle, RectangleF newBounds)
        {
            RectangleF old = GetBounds(null);
            if (old.Width < 0.1f || old.Height < 0.1f) return;
            float sx = newBounds.Width / old.Width;
            float sy = newBounds.Height / old.Height;
            for (int i = 0; i < Points.Count; i++)
            {
                float nx = newBounds.X + (Points[i].X - old.X) * sx;
                float ny = newBounds.Y + (Points[i].Y - old.Y) * sy;
                Points[i] = new PointF(nx, ny);
            }
        }
    }

    internal sealed class BlurStrokeAnnotation : StrokeAnnotation
    {
        private readonly Bitmap source;

        public BlurStrokeAnnotation(Bitmap source)
        {
            this.source = source;
            Width = 10;
        }

        public override void Draw(Graphics g)
        {
            if (Points.Count == 0) return;

            // 源位图已被释放时用灰色色块替代
            bool sourceValid = false;
            try { sourceValid = source != null && source.Width > 0 && source.Height > 0; } catch { }
            if (!sourceValid)
            {
                using (var pen = new Pen(Color.FromArgb(140, 120, 120, 120), Width * 4.0f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (Points.Count == 1)
                    {
                        PointF p = Points[0];
                        g.DrawEllipse(pen, p.X - Width, p.Y - Width, Width * 2, Width * 2);
                    }
                    else
                    {
                        g.DrawLines(pen, Points.ToArray());
                    }
                }
                return;
            }

            // 计算需要锁定的总区域（覆盖所有采样点）
            int radius = (int)Math.Max(18.0f, Width * 3.5f);
            int block = (int)Math.Max(7.0f, Width * 0.9f);
            int halfBlock = Math.Max(1, block / 2);
            int sampleStep = Math.Max(1, block / 3);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            float minStep = Math.Max(8.0f, Width * 1.15f);
            PointF last = new PointF(float.MinValue, float.MinValue);
            for (int i = 0; i < Points.Count; i++)
            {
                PointF p = Points[i];
                if (last.X != float.MinValue && Distance(last, p) < minStep) continue;
                int ix = (int)Math.Round(p.X);
                int iy = (int)Math.Round(p.Y);
                if (ix - radius < minX) minX = ix - radius;
                if (iy - radius < minY) minY = iy - radius;
                if (ix + radius > maxX) maxX = ix + radius;
                if (iy + radius > maxY) maxY = iy + radius;
                last = p;
            }

            if (minX == int.MaxValue) return;

            // 锁定源位图区域
            int lockX = Math.Max(0, minX);
            int lockY = Math.Max(0, minY);
            int lockRight = Math.Min(source.Width, maxX + 1);
            int lockBottom = Math.Min(source.Height, maxY + 1);
            int lockW = lockRight - lockX;
            int lockH = lockBottom - lockY;
            if (lockW <= 0 || lockH <= 0) return;

            byte[] pixels;
            int stride;
            var lockRect = new Rectangle(lockX, lockY, lockW, lockH);
            var bmpData = source.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = bmpData.Stride;
                pixels = new byte[stride * lockH];
                Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                source.UnlockBits(bmpData);
            }

            last = new PointF(float.MinValue, float.MinValue);
            for (int i = 0; i < Points.Count; i++)
            {
                PointF p = Points[i];
                if (last.X != float.MinValue && Distance(last, p) < minStep) continue;
                DrawMosaicPatchLocked(g, p, radius, block, halfBlock, sampleStep, pixels, stride, lockX, lockY, lockW, lockH);
                last = p;
            }
        }

        private void DrawMosaicPatchLocked(Graphics g, PointF center, int radius, int block, int halfBlock, int sampleStep, byte[] pixels, int stride, int lockX, int lockY, int lockW, int lockH)
        {
            for (int y = -radius; y <= radius; y += block)
            {
                for (int x = -radius; x <= radius; x += block)
                {
                    if (x * x + y * y > radius * radius) continue;

                    int cx = Clamp((int)Math.Round(center.X + x), 0, source.Width - 1);
                    int cy = Clamp((int)Math.Round(center.Y + y), 0, source.Height - 1);
                    Color color = AverageColorLocked(cx, cy, block, halfBlock, sampleStep, pixels, stride, lockX, lockY, lockW, lockH);
                    using (var brush = new SolidBrush(color))
                    {
                        g.FillRectangle(brush, center.X + x - block / 2, center.Y + y - block / 2, block + 1, block + 1);
                    }
                }
            }
        }

        private Color AverageColorLocked(int cx, int cy, int size, int half, int step, byte[] pixels, int stride, int lockX, int lockY, int lockW, int lockH)
        {
            long r = 0, g = 0, b = 0;
            int count = 0;

            for (int iy = cy - half; iy <= cy + half; iy += step)
            {
                for (int ix = cx - half; ix <= cx + half; ix += step)
                {
                    int px = Clamp(ix, 0, source.Width - 1);
                    int py = Clamp(iy, 0, source.Height - 1);
                    int relX = px - lockX;
                    int relY = py - lockY;
                    if (relX >= 0 && relX < lockW && relY >= 0 && relY < lockH)
                    {
                        int offset = relY * stride + relX * 4;
                        b += pixels[offset];
                        g += pixels[offset + 1];
                        r += pixels[offset + 2];
                    }
                    count++;
                }
            }

            if (count == 0) return Color.Gray;
            return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public override Annotation Clone()
        {
            var copy = new BlurStrokeAnnotation(source);
            copy.Color = Color;
            copy.Width = Width;
            copy.Highlighter = Highlighter;
            copy.Opacity = Opacity;
            for (int i = 0; i < Points.Count; i++)
            {
                copy.Points.Add(Points[i]);
            }
            return copy;
        }
    }

    internal sealed class ShapeAnnotation : Annotation
    {
        public DrawingTool Tool;
        public PointF Start;
        public PointF End;
        public Color Color;
        public float Width;
        public bool Highlighter;
        public bool Filled;
        public float Opacity = 1.0f;
        public DashStyle DashStyle = DashStyle.Solid;

        public ShapeAnnotation()
        {
            Tool = DrawingTool.Line;
            Color = Color.Red;
            Width = 5;
        }

        public override void Draw(Graphics g)
        {
            float effOpacity = Highlighter ? 0.37f : Opacity;
            Color drawColor = Color.FromArgb((int)(255 * effOpacity), Color);
            float drawWidth = Highlighter ? Width * 3.0f : Width;

            using (var pen = new Pen(drawColor, drawWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                // 仅描边类工具应用虚线（填充形状保持实线以便看清）
                if (Tool == DrawingTool.Line || Tool == DrawingTool.Arrow ||
                    Tool == DrawingTool.Rectangle || Tool == DrawingTool.Ellipse)
                {
                    pen.DashStyle = DashStyle;
                }

                if (Tool == DrawingTool.Arrow)
                {
                    g.DrawLine(pen, Start, End);

                    // 手动绘制更精致的三角形箭头
                    float angle = (float)Math.Atan2(End.Y - Start.Y, End.X - Start.X);
                    float headLen = Math.Max(12.0f, drawWidth * 3.0f);
                    float headWidth = headLen * 0.5f;

                    float tipX = End.X;
                    float tipY = End.Y;
                    float leftX = tipX - headLen * (float)Math.Cos(angle) + headWidth * (float)Math.Sin(angle);
                    float leftY = tipY - headLen * (float)Math.Sin(angle) - headWidth * (float)Math.Cos(angle);
                    float rightX = tipX - headLen * (float)Math.Cos(angle) - headWidth * (float)Math.Sin(angle);
                    float rightY = tipY - headLen * (float)Math.Sin(angle) + headWidth * (float)Math.Cos(angle);

                    using (var brush = new SolidBrush(drawColor))
                    {
                        g.FillPolygon(brush, new PointF[] {
                            new PointF(tipX, tipY),
                            new PointF(leftX, leftY),
                            new PointF(rightX, rightY)
                        });
                    }
                    return;
                }

                if (Tool == DrawingTool.Line)
                {
                    g.DrawLine(pen, Start, End);
                    return;
                }

                RectangleF rect = Normalize(Start, End);
                if (rect.Width < 1 || rect.Height < 1) return;

                if (Tool == DrawingTool.Cover)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(235, Color)))
                    using (var coverBorder = new Pen(Color.FromArgb(180, 40, 40, 40), Math.Max(1, Width / 2.0f)))
                    {
                        g.FillRectangle(fill, rect);
                        g.DrawRectangle(coverBorder, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                    return;
                }

                if (Tool == DrawingTool.Rectangle)
                {
                    if (Filled)
                    {
                        using (var fill = new SolidBrush(Color.FromArgb(160, drawColor)))
                        {
                            g.FillRectangle(fill, rect);
                        }
                    }
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                else if (Tool == DrawingTool.Ellipse)
                {
                    if (Filled)
                    {
                        using (var fill = new SolidBrush(Color.FromArgb(160, drawColor)))
                        {
                            g.FillEllipse(fill, rect);
                        }
                    }
                    g.DrawEllipse(pen, rect);
                }
            }
        }

        public override Annotation Clone()
        {
            var copy = new ShapeAnnotation();
            copy.Tool = Tool;
            copy.Start = Start;
            copy.End = End;
            copy.Color = Color;
            copy.Width = Width;
            copy.Highlighter = Highlighter;
            copy.Filled = Filled;
            copy.Opacity = Opacity;
            copy.DashStyle = DashStyle;
            return copy;
        }

        public override bool HitTest(PointF point, float tolerance, Graphics g)
        {
            float limit = tolerance + Math.Max(Width, Highlighter ? Width * 3.0f : Width);

            if (Tool == DrawingTool.Line || Tool == DrawingTool.Arrow)
            {
                return DistanceToSegment(point, Start, End) <= limit;
            }

            RectangleF rect = Normalize(Start, End);
            rect.Inflate(limit, limit);
            if (!rect.Contains(point)) return false;

            if (Tool == DrawingTool.Rectangle)
            {
                RectangleF inner = Normalize(Start, End);
                inner.Inflate(-limit, -limit);
                return !inner.Contains(point);
            }

            if (Tool == DrawingTool.Cover)
            {
                return rect.Contains(point);
            }

            if (Tool == DrawingTool.Ellipse)
            {
                float rx = Math.Max(1, Math.Abs(End.X - Start.X) / 2.0f);
                float ry = Math.Max(1, Math.Abs(End.Y - Start.Y) / 2.0f);
                float cx = Math.Min(Start.X, End.X) + rx;
                float cy = Math.Min(Start.Y, End.Y) + ry;
                float value = ((point.X - cx) * (point.X - cx)) / (rx * rx) + ((point.Y - cy) * (point.Y - cy)) / (ry * ry);
                return Math.Abs(value - 1.0f) <= 0.25f;
            }

            return false;
        }

        public override void Translate(float dx, float dy)
        {
            Start = new PointF(Start.X + dx, Start.Y + dy);
            End = new PointF(End.X + dx, End.Y + dy);
        }

        public override RectangleF GetBounds(Graphics g)
        {
            float pad = Math.Max(Width, Highlighter ? Width * 3.0f : Width);
            RectangleF b = Normalize(Start, End);
            return RectangleF.Inflate(b, pad, pad);
        }

        public override int HitTestHandle(PointF point, Graphics g)
        {
            return HitTestRectHandles(point, GetBounds(g), 8f);
        }

        public override void ResizeByHandle(int handle, RectangleF newBounds)
        {
            // 线/箭头：两端就是手柄；矩形/椭圆/遮罩：规范化到 Start/End
            if (Tool == DrawingTool.Line || Tool == DrawingTool.Arrow)
            {
                Start = new PointF(newBounds.X, newBounds.Y);
                End = new PointF(newBounds.Right, newBounds.Bottom);
                return;
            }
            Start = new PointF(newBounds.X, newBounds.Y);
            End = new PointF(newBounds.Right, newBounds.Bottom);
        }
    }

    internal sealed class NumberMarkerAnnotation : Annotation
    {
        public PointF Location;
        public int Number;
        public Color Color;
        public float Radius;

        public NumberMarkerAnnotation()
        {
            Number = 1;
            Color = Color.Red;
            Radius = 22;
        }

        public override void Draw(Graphics g)
        {
            RectangleF rect = new RectangleF(Location.X - Radius, Location.Y - Radius, Radius * 2, Radius * 2);
            using (var fill = new SolidBrush(Color.FromArgb(235, Color)))
            using (var border = new Pen(Color.White, 3))
            using (var font = new Font(FontFamily.GenericSansSerif, Radius * 1.05f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.FillEllipse(fill, rect);
                g.DrawEllipse(border, rect);
                g.DrawString(Number.ToString(), font, Brushes.White, rect, format);
            }
        }

        public override Annotation Clone()
        {
            var copy = new NumberMarkerAnnotation();
            copy.Location = Location;
            copy.Number = Number;
            copy.Color = Color;
            copy.Radius = Radius;
            return copy;
        }

        public override bool HitTest(PointF point, float tolerance, Graphics g)
        {
            return Distance(point, Location) <= Radius + tolerance;
        }

        public override void Translate(float dx, float dy)
        {
            Location = new PointF(Location.X + dx, Location.Y + dy);
        }

        public override RectangleF GetBounds(Graphics g)
        {
            return new RectangleF(Location.X - Radius, Location.Y - Radius, Radius * 2, Radius * 2);
        }

        public override int HitTestHandle(PointF point, Graphics g)
        {
            return HitTestRectHandles(point, GetBounds(g), 8f);
        }

        public override void ResizeByHandle(int handle, RectangleF newBounds)
        {
            Location = new PointF(newBounds.X + newBounds.Width / 2f, newBounds.Y + newBounds.Height / 2f);
            Radius = Math.Max(6f, Math.Min(newBounds.Width, newBounds.Height) / 2f);
        }
    }

    internal sealed class StampAnnotation : Annotation
    {
        public enum StampType
        {
            Star,
            Check,
            Cross,
            Dot,
            Triangle,
            Heart
        }

        public static readonly string[] StampNames = { "星标", "勾", "叉", "圆点", "三角", "爱心" };
        public static readonly char[] StampChars = { '★', '✓', '✗', '●', '▲', '♥' };

        public PointF Location;
        public StampType Type;
        public Color Color;
        public float Radius;

        public StampAnnotation()
        {
            Type = StampType.Star;
            Color = Color.Red;
            Radius = 22;
        }

        public override void Draw(Graphics g)
        {
            char c = (int)Type < StampChars.Length ? StampChars[(int)Type] : '★';
            string text = c.ToString();

            using (var font = new Font(FontFamily.GenericSansSerif, Radius * 1.8f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                // 绘制阴影
                using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    g.DrawString(text, font, shadow, Location.X + 1.5f, Location.Y + 1.5f, format);
                }

                // 绘制文字
                using (var brush = new SolidBrush(Color))
                {
                    g.DrawString(text, font, brush, Location.X, Location.Y, format);
                }
            }
        }

        public override Annotation Clone()
        {
            var copy = new StampAnnotation();
            copy.Location = Location;
            copy.Type = Type;
            copy.Color = Color;
            copy.Radius = Radius;
            return copy;
        }

        public override bool HitTest(PointF point, float tolerance, Graphics g)
        {
            return Distance(point, Location) <= Radius * 1.4f + tolerance;
        }

        public override void Translate(float dx, float dy)
        {
            Location = new PointF(Location.X + dx, Location.Y + dy);
        }

        public override RectangleF GetBounds(Graphics g)
        {
            return new RectangleF(Location.X - Radius, Location.Y - Radius, Radius * 2, Radius * 2);
        }

        public override int HitTestHandle(PointF point, Graphics g)
        {
            return HitTestRectHandles(point, GetBounds(g), 8f);
        }

        public override void ResizeByHandle(int handle, RectangleF newBounds)
        {
            Location = new PointF(newBounds.X + newBounds.Width / 2f, newBounds.Y + newBounds.Height / 2f);
            Radius = Math.Max(6f, Math.Min(newBounds.Width, newBounds.Height) / 2f);
        }
    }

    internal sealed class TextAnnotation : Annotation
    {
        public PointF Location;
        public string Text;
        public Color Color;
        public float FontSize;
        public bool RightAligned;
        public bool HasBackground;
        public Color BackgroundColor = Color.FromArgb(220, 0, 0, 0);
        public int BackgroundPadding = 6;

        public TextAnnotation()
        {
            Text = "";
            Color = Color.Red;
            FontSize = 28;
        }

        public override void Draw(Graphics g)
        {
            if (string.IsNullOrWhiteSpace(Text)) return;

            using (var font = new Font(FontFamily.GenericSansSerif, FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                SizeF size = g.MeasureString(Text, font);
                float x = RightAligned ? Location.X - size.Width : Location.X;
                float y = Location.Y;

                // 可选背景框：提升在复杂背景上的可读性
                if (HasBackground)
                {
                    using (var bgBrush = new SolidBrush(BackgroundColor))
                    {
                        g.FillRectangle(bgBrush, x - BackgroundPadding, y - BackgroundPadding / 2,
                                        size.Width + BackgroundPadding * 2, size.Height + BackgroundPadding);
                    }
                }
                else
                {
                    // 阴影偏移：提升在亮/暗背景上的可读性
                    g.DrawString(Text, font, shadowBrush, new PointF(x + 1, y + 1));
                }
                g.DrawString(Text, font, brush, new PointF(x, y));
            }
        }

        public override Annotation Clone()
        {
            var copy = new TextAnnotation();
            copy.Location = Location;
            copy.Text = Text;
            copy.Color = Color;
            copy.FontSize = FontSize;
            copy.RightAligned = RightAligned;
            copy.HasBackground = HasBackground;
            copy.BackgroundColor = BackgroundColor;
            copy.BackgroundPadding = BackgroundPadding;
            return copy;
        }

        public override bool HitTest(PointF point, float tolerance, Graphics g)
        {
            if (string.IsNullOrEmpty(Text)) return false;

            using (var font = new Font(FontFamily.GenericSansSerif, FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF size = g.MeasureString(Text, font);
                RectangleF rect = RightAligned
                    ? new RectangleF(Location.X - size.Width, Location.Y, size.Width, size.Height)
                    : new RectangleF(Location, size);
                rect.Inflate(tolerance, tolerance);
                return rect.Contains(point);
            }
        }

        public override void Translate(float dx, float dy)
        {
            Location = new PointF(Location.X + dx, Location.Y + dy);
        }

        public override RectangleF GetBounds(Graphics g)
        {
            if (string.IsNullOrEmpty(Text)) return RectangleF.Empty;
            using (var font = new Font(FontFamily.GenericSansSerif, FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF size = g.MeasureString(Text, font);
                return RightAligned
                    ? new RectangleF(Location.X - size.Width, Location.Y, size.Width, size.Height)
                    : new RectangleF(Location, size);
            }
        }

        public override int HitTestHandle(PointF point, Graphics g)
        {
            return HitTestRectHandles(point, GetBounds(g), 8f);
        }

        public override void ResizeByHandle(int handle, RectangleF newBounds)
        {
            // 按高度缩放字号
            float oldH = GetBounds(null).Height;
            if (oldH > 1f) FontSize = Math.Max(8f, FontSize * newBounds.Height / oldH);
            Location = new PointF(newBounds.X, newBounds.Y);
        }
    }

    internal sealed class RulerAnnotation : Annotation
    {
        public PointF Start;
        public PointF End;
        public Color Color;

        public override void Draw(Graphics g)
        {
            float dx = End.X - Start.X;
            float dy = End.Y - Start.Y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance < 1) return;

            // 测量线 + 端点标记
            using (var pen = new Pen(Color, 2))
            {
                // 虚线连接
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawLine(pen, Start, End);

                // 端点圆点
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Solid;
                float r = 4;
                g.FillEllipse(Brushes.White, Start.X - r, Start.Y - r, r * 2, r * 2);
                g.FillEllipse(Brushes.White, End.X - r, End.Y - r, r * 2, r * 2);
                g.DrawEllipse(pen, Start.X - r, Start.Y - r, r * 2, r * 2);
                g.DrawEllipse(pen, End.X - r, End.Y - r, r * 2, r * 2);
            }

            // 距离和角度标签
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;

            string label = distance.ToString("0") + "px  " + angle.ToString("0.0") + "°";
            using (var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF textSize = g.MeasureString(label, font);
                float midX = (Start.X + End.X) / 2;
                float midY = (Start.Y + End.Y) / 2;

                // 标签偏移到线的垂直方向
                float nx = -dy / distance * 20;
                float ny = dx / distance * 20;
                float labelX = midX + nx - textSize.Width / 2;
                float labelY = midY + ny - textSize.Height / 2;

                using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                {
                    g.FillRectangle(bg, labelX - 4, labelY - 2, textSize.Width + 8, textSize.Height + 4);
                }
                using (var brush = new SolidBrush(Color))
                {
                    g.DrawString(label, font, brush, labelX, labelY);
                }
            }
        }

        public override Annotation Clone()
        {
            return new RulerAnnotation { Start = Start, End = End, Color = Color };
        }

        public override bool HitTest(PointF point, float tolerance, Graphics g)
        {
            return DistanceToSegment(point, Start, End) <= tolerance + 6;
        }

        public override void Translate(float dx, float dy)
        {
            Start = new PointF(Start.X + dx, Start.Y + dy);
            End = new PointF(End.X + dx, End.Y + dy);
        }

        public override RectangleF GetBounds(Graphics g)
        {
            return Normalize(Start, End);
        }

        public override int HitTestHandle(PointF point, Graphics g)
        {
            return HitTestRectHandles(point, GetBounds(g), 8f);
        }

        public override void ResizeByHandle(int handle, RectangleF newBounds)
        {
            Start = new PointF(newBounds.X, newBounds.Y);
            End = new PointF(newBounds.Right, newBounds.Bottom);
        }
    }
}

