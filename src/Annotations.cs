using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BoardBeam
{
    internal abstract class Annotation
    {
        public abstract void Draw(Graphics g);
        public abstract Annotation Clone();
        public abstract bool HitTest(PointF point, float tolerance, Graphics g);

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
    }

    internal class StrokeAnnotation : Annotation
    {
        public readonly List<PointF> Points;
        public Color Color;
        public float Width;
        public bool Highlighter;

        public StrokeAnnotation()
        {
            Points = new List<PointF>();
            Color = Color.Red;
            Width = 5;
            Highlighter = false;
        }

        public override void Draw(Graphics g)
        {
            if (Points.Count == 0) return;

            Color drawColor = Highlighter ? Color.FromArgb(95, Color) : Color;
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

            if (source == null)
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

            float minStep = Math.Max(8.0f, Width * 1.15f);
            PointF last = new PointF(float.MinValue, float.MinValue);
            for (int i = 0; i < Points.Count; i++)
            {
                PointF p = Points[i];
                if (last.X != float.MinValue && Distance(last, p) < minStep)
                {
                    continue;
                }

                DrawMosaicPatch(g, p);
                last = p;
            }
        }

        private void DrawMosaicPatch(Graphics g, PointF center)
        {
            int radius = (int)Math.Max(18.0f, Width * 3.5f);
            int block = (int)Math.Max(7.0f, Width * 0.9f);

            for (int y = -radius; y <= radius; y += block)
            {
                for (int x = -radius; x <= radius; x += block)
                {
                    if (x * x + y * y > radius * radius) continue;

                    int sx = Clamp((int)Math.Round(center.X + x), 0, source.Width - 1);
                    int sy = Clamp((int)Math.Round(center.Y + y), 0, source.Height - 1);
                    Color color = AverageColor(sx, sy, block);
                    using (var brush = new SolidBrush(color))
                    {
                        g.FillRectangle(brush, sx - block / 2, sy - block / 2, block + 1, block + 1);
                    }
                }
            }
        }

        private Color AverageColor(int cx, int cy, int size)
        {
            long r = 0;
            long g = 0;
            long b = 0;
            int count = 0;
            int half = Math.Max(1, size / 2);
            int step = Math.Max(1, size / 3);

            for (int y = cy - half; y <= cy + half; y += step)
            {
                for (int x = cx - half; x <= cx + half; x += step)
                {
                    int px = Clamp(x, 0, source.Width - 1);
                    int py = Clamp(y, 0, source.Height - 1);
                    Color c = source.GetPixel(px, py);
                    r += c.R;
                    g += c.G;
                    b += c.B;
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

        public ShapeAnnotation()
        {
            Tool = DrawingTool.Line;
            Color = Color.Red;
            Width = 5;
        }

        public override void Draw(Graphics g)
        {
            Color drawColor = Highlighter ? Color.FromArgb(95, Color) : Color;
            float drawWidth = Highlighter ? Width * 3.0f : Width;

            using (var pen = new Pen(drawColor, drawWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (Tool == DrawingTool.Arrow)
                {
                    using (var cap = new AdjustableArrowCap(drawWidth * 1.4f, drawWidth * 1.8f, true))
                    {
                        pen.CustomEndCap = cap;
                        g.DrawLine(pen, Start, End);
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
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                else if (Tool == DrawingTool.Ellipse)
                {
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
    }

    internal sealed class TextAnnotation : Annotation
    {
        public PointF Location;
        public string Text;
        public Color Color;
        public float FontSize;
        public bool RightAligned;

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
            {
                if (RightAligned)
                {
                    SizeF size = g.MeasureString(Text, font);
                    g.DrawString(Text, font, brush, new PointF(Location.X - size.Width, Location.Y));
                }
                else
                {
                    g.DrawString(Text, font, brush, Location);
                }
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
    }
}

