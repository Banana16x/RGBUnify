using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// Draws the Buy Me a Coffee cover: big glossy rainbow bubble letters on a dark background full of RGB glow.
// Usage: CoverGen.exe <out.png> <width> <height>
static class CoverGen
{
    static readonly Color[] Rainbow = {
        Color.FromArgb(255, 40, 90), Color.FromArgb(255, 140, 0), Color.FromArgb(255, 225, 0), Color.FromArgb(40, 230, 90),
        Color.FromArgb(0, 210, 255), Color.FromArgb(70, 110, 255), Color.FromArgb(190, 70, 255), Color.FromArgb(255, 40, 160)
    };

    static void Main(string[] args)
    {
        int w = int.Parse(args[1]), h = int.Parse(args[2]);
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float s = h / 640f; // everything is designed at 640px tall

            Background(g, w, h, s);
            Title(g, w, h, s);
            bmp.Save(args[0], ImageFormat.Png);
        }
    }

    static Color Hue(double t)
    {
        t = t - Math.Floor(t);
        double x = t * Rainbow.Length;
        int i = (int)x;
        Color a = Rainbow[i % Rainbow.Length], b = Rainbow[(i + 1) % Rainbow.Length];
        double f = x - i;
        return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
    }

    static void Glow(Graphics g, float cx, float cy, float r, Color c, int alpha)
    {
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using (PathGradientBrush br = new PathGradientBrush(p))
            {
                br.CenterColor = Color.FromArgb(alpha, c);
                br.SurroundColors = new[] { Color.FromArgb(0, c) };
                g.FillPath(br, p);
            }
        }
    }

    static void Background(Graphics g, int w, int h, float s)
    {
        using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(10, 10, 20), Color.FromArgb(18, 12, 30), 90f))
            g.FillRectangle(bg, 0, 0, w, h);

        // Big soft colour clouds across the width, like a case lit up in the dark.
        Random rnd = new Random(7);
        for (int i = 0; i < 9; i++)
        {
            float x = w * (i + 0.5f) / 9f + (float)(rnd.NextDouble() - 0.5) * w * 0.05f;
            float y = h * (i % 2 == 0 ? 0.15f : 0.88f);
            Glow(g, x, y, h * 0.75f, Hue(i / 9.0), 120);
        }

        // Diagonal light streaks.
        for (int i = 0; i < 14; i++)
        {
            float x = w * i / 13f;
            using (Pen p = new Pen(Color.FromArgb(28, Hue(i / 14.0 + 0.3)), 26 * s))
                g.DrawLine(p, x - h * 0.6f, h + 20, x + h * 0.6f, -20);
        }

        // Glowing fan rings on both sides.
        FanRing(g, w * 0.085f, h * 0.5f, h * 0.30f, s, 0.0);
        FanRing(g, w * 0.915f, h * 0.5f, h * 0.30f, s, 0.5);
        FanRing(g, w * 0.17f, h * 0.83f, h * 0.15f, s, 0.25);
        FanRing(g, w * 0.83f, h * 0.17f, h * 0.15f, s, 0.75);

        // LED strips along the top and bottom edges.
        int leds = (int)(w / (22 * s));
        for (int i = 0; i < leds; i++)
        {
            float x = (i + 0.5f) * w / leds;
            Color c = Hue((double)i / leds * 2);
            foreach (float y in new[] { 14 * s, h - 14 * s })
            {
                Glow(g, x, y, 16 * s, c, 160);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, Lighten(c, 0.35f)))) g.FillEllipse(b, x - 4 * s, y - 4 * s, 8 * s, 8 * s);
            }
        }

        // Darken the middle so the title pops.
        Glow(g, w / 2f, h / 2f, h * 1.1f, Color.FromArgb(6, 6, 12), 200);
    }

    static void FanRing(Graphics g, float cx, float cy, float r, float s, double hueStart)
    {
        Glow(g, cx, cy, r * 1.35f, Hue(hueStart), 70);
        int seg = 48;
        for (int i = 0; i < seg; i++)
        {
            float a0 = 360f * i / seg;
            using (Pen p = new Pen(Hue(hueStart + (double)i / seg), r * 0.16f))
            {
                p.StartCap = p.EndCap = LineCap.Flat;
                g.DrawArc(p, cx - r, cy - r, r * 2, r * 2, a0, 360f / seg + 0.8f);
            }
        }
        using (Pen hub = new Pen(Color.FromArgb(90, 255, 255, 255), 2 * s)) g.DrawEllipse(hub, cx - r * 0.82f, cy - r * 0.82f, r * 1.64f, r * 1.64f);
        // Blades.
        // Swept blades, like a real case fan.
        using (SolidBrush blade = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
        using (Pen edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f * s))
            for (int i = 0; i < 9; i++)
            {
                GraphicsState st = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(i * 360f / 9 + (float)(hueStart * 360));
                using (GraphicsPath b = new GraphicsPath())
                {
                    b.AddBezier(r * 0.18f, -r * 0.04f, r * 0.38f, -r * 0.30f, r * 0.62f, -r * 0.32f, r * 0.78f, -r * 0.10f);
                    b.AddBezier(r * 0.78f, -r * 0.10f, r * 0.62f, -r * 0.02f, r * 0.40f, r * 0.06f, r * 0.18f, r * 0.06f);
                    b.CloseFigure();
                    g.FillPath(blade, b);
                    g.DrawPath(edge, b);
                }
                g.Restore(st);
            }
        using (SolidBrush c = new SolidBrush(Color.FromArgb(20, 20, 30))) g.FillEllipse(c, cx - r * 0.2f, cy - r * 0.2f, r * 0.4f, r * 0.4f);
    }

    static Color Lighten(Color c, float t)
    {
        return Color.FromArgb((int)(c.R + (255 - c.R) * t), (int)(c.G + (255 - c.G) * t), (int)(c.B + (255 - c.B) * t));
    }

    static GraphicsPath TextPath(string text, FontFamily fam, float size, float cx, float top, out RectangleF bounds)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddString(text, fam, (int)FontStyle.Regular, size, new PointF(0, 0), StringFormat.GenericTypographic);
        RectangleF b = p.GetBounds();
        using (Matrix m = new Matrix())
        {
            m.Translate(cx - b.Width / 2 - b.X, top - b.Y);
            p.Transform(m);
        }
        bounds = p.GetBounds();
        return p;
    }

    static void Title(Graphics g, int w, int h, float s)
    {
        FontFamily black = new FontFamily("Segoe UI Black");
        RectangleF tb;
        // Narrower banners shrink the title so it never runs into the side fans.
        float k = Math.Min(s, w / 2560f * 1.2f);
        GraphicsPath title = TextPath("RGB UNIFY", black, 250 * k, w / 2f, 0, out tb);
        // Centre title + tagline (about 0.62 of the title height below it) vertically.
        float block = tb.Height + (52 + 64) * k;
        using (Matrix mv = new Matrix()) { mv.Translate(0, (h - block) / 2f - tb.Y); title.Transform(mv); }
        tb = title.GetBounds();
        s = k;

        // Outer rainbow glow behind the letters.
        for (int i = 6; i >= 1; i--)
            using (Pen p = new Pen(Color.FromArgb(14, 255, 255, 255), (40 + i * 16) * s) { LineJoin = LineJoin.Round })
                g.DrawPath(p, title);

        // Drop shadow.
        using (GraphicsPath shadow = (GraphicsPath)title.Clone())
        using (Matrix m = new Matrix())
        {
            m.Translate(0, 16 * s);
            shadow.Transform(m);
            using (Pen p = new Pen(Color.FromArgb(200, 0, 0, 0), 44 * s) { LineJoin = LineJoin.Round }) g.DrawPath(p, shadow);
        }

        // Puffy bubble outline: a fat round-joined white border, then a dark inner rim.
        using (Pen p = new Pen(Color.White, 44 * s) { LineJoin = LineJoin.Round }) g.DrawPath(p, title);
        using (Pen p = new Pen(Color.FromArgb(25, 20, 45), 22 * s) { LineJoin = LineJoin.Round }) g.DrawPath(p, title);

        // Rainbow fill across the letters (fattened slightly so they look inflated).
        using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(tb.X - 1, tb.Y, tb.Width + 2, tb.Height), Color.Red, Color.Blue, 0f))
        {
            ColorBlend cb = new ColorBlend();
            cb.Colors = Rainbow;
            cb.Positions = new float[Rainbow.Length];
            for (int i = 0; i < Rainbow.Length; i++) cb.Positions[i] = i / (float)(Rainbow.Length - 1);
            fill.InterpolationColors = cb;
            g.FillPath(fill, title);
            using (Pen fat = new Pen(fill, 8 * s) { LineJoin = LineJoin.Round }) g.DrawPath(fat, title);
        }

        // Glossy shine on the top half of every letter.
        GraphicsState st = g.Save();
        g.SetClip(title);
        RectangleF shine = new RectangleF(tb.X - 20 * s, tb.Y - tb.Height * 0.9f, tb.Width + 40 * s, tb.Height * 1.35f);
        using (LinearGradientBrush gl = new LinearGradientBrush(shine, Color.FromArgb(170, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillEllipse(gl, shine);
        // Darker bottom for a rounded, 3D feel.
        RectangleF bottom = new RectangleF(tb.X, tb.Y + tb.Height * 0.55f, tb.Width, tb.Height * 0.5f);
        using (LinearGradientBrush dk = new LinearGradientBrush(bottom, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(90, 0, 0, 0), 90f))
            g.FillRectangle(dk, bottom);
        g.Restore(st);

        // Little sparkle highlights.
        foreach (PointF pt in new[] { new PointF(tb.X + tb.Width * 0.05f, tb.Y + tb.Height * 0.12f), new PointF(tb.X + tb.Width * 0.62f, tb.Y + tb.Height * 0.1f), new PointF(tb.Right - tb.Width * 0.04f, tb.Y + tb.Height * 0.2f) })
            Sparkle(g, pt.X, pt.Y, 22 * s);

        // Tagline in a dark pill.
        string tag = "ONE APP  •  EVERY LIGHT  •  TOTALLY IN SYNC";
        using (Font f = new Font("Segoe UI Black", 34 * s, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            SizeF ts = g.MeasureString(tag, f);
            float px = w / 2f - ts.Width / 2 - 34 * s, py = tb.Bottom + 52 * s;
            RectangleF pill = new RectangleF(px, py, ts.Width + 68 * s, ts.Height + 20 * s);
            using (GraphicsPath pp = RoundRect(pill, pill.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 12, 12, 22))) g.FillPath(b, pp);
                using (LinearGradientBrush edge = new LinearGradientBrush(pill, Rainbow[0], Rainbow[5], 0f))
                using (Pen p = new Pen(edge, 3 * s)) g.DrawPath(p, pp);
            }
            using (SolidBrush t = new SolidBrush(Color.White)) g.DrawString(tag, f, t, px + 34 * s, py + 10 * s);
        }
    }

    static void Sparkle(Graphics g, float x, float y, float r)
    {
        Glow(g, x, y, r * 1.6f, Color.White, 150);
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new[] {
                new PointF(x, y - r), new PointF(x + r * 0.18f, y - r * 0.18f), new PointF(x + r, y), new PointF(x + r * 0.18f, y + r * 0.18f),
                new PointF(x, y + r), new PointF(x - r * 0.18f, y + r * 0.18f), new PointF(x - r, y), new PointF(x - r * 0.18f, y - r * 0.18f)
            });
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
        }
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
