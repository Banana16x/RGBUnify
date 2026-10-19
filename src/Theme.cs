using System.Drawing;

namespace RGBUnify
{
    // App colour schemes. MainForm reads Theme.Current everywhere and rebuilds its controls when it changes.
    public class Theme
    {
        public string Name;
        public Color Bg, Card, Card2, Text1, Text2, Accent, Warn, Good, Danger;

        public static readonly Theme[] All = {
            Dark("Midnight", 14, 16, 21, 24, 27, 35, 34, 38, 48, 124, 92, 255),
            Dark("Neon", 8, 6, 14, 20, 14, 32, 36, 24, 54, 255, 40, 170),
            Dark("Ocean", 8, 16, 26, 14, 28, 44, 22, 42, 64, 0, 165, 255),
            Dark("Sunset", 22, 12, 16, 36, 20, 26, 54, 30, 38, 255, 110, 50),
            Dark("Forest", 10, 18, 14, 18, 30, 24, 28, 46, 36, 50, 190, 100),
            new Theme
            {
                Name = "Light",
                Bg = Color.FromArgb(236, 239, 245), Card = Color.White, Card2 = Color.FromArgb(222, 226, 236),
                Text1 = Color.FromArgb(24, 27, 36), Text2 = Color.FromArgb(92, 100, 120),
                Accent = Color.FromArgb(108, 76, 240), Warn = Color.FromArgb(176, 106, 0),
                Good = Color.FromArgb(20, 140, 75), Danger = Color.FromArgb(210, 50, 65)
            }
        };

        public static Theme Current = All[0];

        public static Theme Find(string name)
        {
            foreach (Theme t in All) if (t.Name == name) return t;
            return All[0];
        }

        static Theme Dark(string name, int br, int bg, int bb, int cr, int cg, int cb, int c2r, int c2g, int c2b, int ar, int ag, int ab)
        {
            return new Theme
            {
                Name = name,
                Bg = Color.FromArgb(br, bg, bb), Card = Color.FromArgb(cr, cg, cb), Card2 = Color.FromArgb(c2r, c2g, c2b),
                Text1 = Color.FromArgb(236, 239, 246), Text2 = Color.FromArgb(150, 157, 176),
                Accent = Color.FromArgb(ar, ag, ab), Warn = Color.FromArgb(255, 200, 90),
                Good = Color.FromArgb(90, 220, 140), Danger = Color.FromArgb(232, 72, 85)
            };
        }
    }
}
