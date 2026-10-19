using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace RGBUnify
{
    // Rainbow ring logo, drawn in code so no image files are needed.
    public static class AppIcon
    {
        public static Icon Create(int size)
        {
            using (Bitmap bmp = Draw(size))
                return Icon.FromHandle(bmp.GetHicon());
        }

        public static Bitmap Draw(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float w = size * 0.16f;
                int segs = 36;
                for (int i = 0; i < segs; i++)
                {
                    Rgb c = EffectRenderer.Hsv(i / (double)segs, 1, 1);
                    using (Pen p = new Pen(c.ToColor(), w))
                        g.DrawArc(p, w / 2 + 1, w / 2 + 1, size - w - 2, size - w - 2, i * 360f / segs - 90, 360f / segs + 1.5f);
                }
                float r = size * 0.18f;
                using (SolidBrush b = new SolidBrush(Color.White))
                    g.FillEllipse(b, size / 2f - r, size / 2f - r, r * 2, r * 2);
            }
            return bmp;
        }

        // Writes a multi-size .ico (PNG-compressed entries) for the exe resource.
        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            byte[][] pngs = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (Bitmap b = Draw(sizes[i]))
                using (MemoryStream ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); pngs[i] = ms.ToArray(); }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }
    }
}
