using System;
using System.Runtime.InteropServices;

namespace RGBUnify
{
    // PerFan only applies to fan headers: each fan shows its own colour (Engine fills it in, not the renderer).
    public enum EffectKind { Static, Breathing, Spectrum, RainbowWave, ColorWave, Comet, Starlight, Music, Off, PerFan, Temperature, Screen }

    // Computes colours for every LED in the rig. Position p (0..1) is the LED's place across ALL devices,
    // which is what makes waves flow from one device into the next.
    public class EffectRenderer
    {
        public EffectKind Kind = EffectKind.RainbowWave;
        public Rgb Color1 = new Rgb(0, 140, 255);
        public Rgb Color2 = new Rgb(255, 0, 140);
        public int Brightness = 100; // 0..100
        public int Speed = 5;        // 1..10

        readonly Random rnd = new Random();
        float[] twinkle = new float[0];
        double phase;
        float smoothLevel;
        AudioMeter meter;
        // Temperature effect: current GPU temperature in degrees C (NaN = no reading) and the cool/hot ends.
        public double Temperature = double.NaN;
        public const double TempCool = 30, TempHot = 85;
        // Screen effect: the screen's colours, left to right (set by the engine from ScreenSampler).
        public Rgb[] ScreenColumns = new Rgb[0];

        public static string DisplayName(EffectKind k)
        {
            switch (k)
            {
                case EffectKind.Static: return "Solid Color";
                case EffectKind.Breathing: return "Breathing";
                case EffectKind.Spectrum: return "Color Cycle";
                case EffectKind.RainbowWave: return "Rainbow Wave";
                case EffectKind.ColorWave: return "Two-Color Wave";
                case EffectKind.Comet: return "Comet";
                case EffectKind.Starlight: return "Starlight";
                case EffectKind.Music: return "Music Sync";
                case EffectKind.PerFan: return "Each fan its own colour";
                case EffectKind.Temperature: return "GPU Temperature";
                case EffectKind.Screen: return "Screen Ambient";
                default: return "Lights Off";
            }
        }

        public static bool UsesColor1(EffectKind k)
        {
            return k == EffectKind.Static || k == EffectKind.Breathing || k == EffectKind.ColorWave ||
                   k == EffectKind.Comet || k == EffectKind.Starlight || k == EffectKind.Music;
        }

        public static bool UsesColor2(EffectKind k)
        {
            return k == EffectKind.ColorWave || k == EffectKind.Starlight || k == EffectKind.Music;
        }

        // dt = seconds since last frame
        public void Render(Rgb[] leds, double dt)
        {
            int n = leds.Length;
            if (n == 0) return;
            double spd = 0.04 + Speed * 0.045;
            phase += dt * spd;
            if (phase > 1000) phase -= 1000;
            double f = Brightness / 100.0;
            // Read once: the UI thread can switch effects mid-frame.
            EffectKind kind = Kind;

            if (kind == EffectKind.Music)
            {
                if (meter == null) meter = new AudioMeter();
                float lvl = meter.Peak();
                smoothLevel = lvl > smoothLevel ? lvl : smoothLevel * 0.88f + lvl * 0.12f;
            }
            float[] twinkle = this.twinkle;
            if (kind == EffectKind.Starlight && twinkle.Length != n) this.twinkle = twinkle = new float[n];

            for (int i = 0; i < n; i++)
            {
                double p = n == 1 ? 0 : (double)i / n;
                Rgb c;
                switch (kind)
                {
                    case EffectKind.Static:
                    case EffectKind.PerFan:
                        c = Color1; break;
                    case EffectKind.Breathing:
                        c = Scale(Color1, 0.5 - 0.5 * Math.Cos(phase * Math.PI * 2 * 1.5)); break;
                    case EffectKind.Spectrum:
                        c = Hsv(phase * 0.5, 1, 1); break;
                    case EffectKind.RainbowWave:
                        c = Hsv(p - phase, 1, 1); break;
                    case EffectKind.ColorWave:
                        c = Lerp(Color1, Color2, 0.5 + 0.5 * Math.Sin((p * 2 - phase * 2) * Math.PI * 2)); break;
                    case EffectKind.Comet:
                        {
                            double head = (phase * 1.2) % 1.0;
                            double d = head - p;
                            if (d < 0) d += 1;
                            double tail = Math.Max(0, 1 - d / 0.25);
                            c = Scale(Color1, tail * tail);
                            break;
                        }
                    case EffectKind.Starlight:
                        {
                            if (twinkle[i] <= 0 && rnd.NextDouble() < dt * (0.15 + Speed * 0.08)) twinkle[i] = 1;
                            twinkle[i] = (float)Math.Max(0, twinkle[i] - dt * 0.9);
                            double t = Math.Sin(twinkle[i] * Math.PI);
                            c = Lerp(Scale(Color2, 0.15), Color1, t);
                            break;
                        }
                    case EffectKind.Temperature:
                        c = double.IsNaN(Temperature) ? Scale(Color1, 0.3) : TempColor(Temperature);
                        break;
                    case EffectKind.Screen:
                        {
                            Rgb[] cols = ScreenColumns;
                            c = cols.Length == 0 ? new Rgb(0, 0, 0) : cols[Math.Min(cols.Length - 1, (int)(p * cols.Length))];
                            break;
                        }
                    case EffectKind.Music:
                        {
                            double lvl = Math.Min(1, smoothLevel * 1.6);
                            // Lights fill from both ends of the rig toward the middle with the beat.
                            double dist = Math.Abs(p - 0.5) * 2;
                            double lit = dist >= 1 - lvl ? 1 : 0.08;
                            c = Scale(Lerp(Color1, Color2, lvl), Math.Max(lit, lvl * 0.35));
                            break;
                        }
                    default:
                        c = new Rgb(0, 0, 0); break;
                }
                leds[i] = Scale(c, f);
            }
        }

        // Cool blue -> green -> yellow -> red as the temperature climbs from TempCool to TempHot.
        public static Rgb TempColor(double celsius)
        {
            double t = Math.Max(0, Math.Min(1, (celsius - TempCool) / (TempHot - TempCool)));
            // Hue 0.62 (blue) down to 0 (red).
            return Hsv(0.62 * (1 - t), 1, 1);
        }

        public static Rgb Scale(Rgb c, double k)
        {
            return new Rgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
        }

        public static Rgb Lerp(Rgb a, Rgb b, double t)
        {
            return new Rgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Rgb Hsv(double h, double s, double v)
        {
            h = h - Math.Floor(h);
            double hh = h * 6;
            int sector = (int)hh;
            double fr = hh - sector;
            double p = v * (1 - s), q = v * (1 - s * fr), t = v * (1 - s * (1 - fr));
            double r, g, b;
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new Rgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
    }

    // Reads the current output volume peak of the default speakers (Windows Core Audio).
    public class AudioMeter
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        IAudioMeterInformation meter;
        DateTime nextRetry = DateTime.MinValue;

        public float Peak()
        {
            try
            {
                if (meter == null)
                {
                    if (DateTime.UtcNow < nextRetry) return 0;
                    nextRetry = DateTime.UtcNow.AddSeconds(5);
                    IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                    IMMDevice dev;
                    if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return 0;
                    Guid iid = typeof(IAudioMeterInformation).GUID;
                    object o;
                    if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return 0;
                    meter = (IAudioMeterInformation)o;
                }
                float v;
                if (meter.GetPeakValue(out v) != 0) { meter = null; return 0; }
                return v;
            }
            catch
            {
                meter = null;
                return 0;
            }
        }
    }
}

namespace RGBUnify
{
    // GPU temperature from the NVIDIA driver's own library (nvml.dll, installed with every NVIDIA driver).
    // No extra software or driver; returns NaN on PCs without an NVIDIA card.
    public static class GpuTemperature
    {
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] static extern int Init();
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] static extern int GetHandle(uint index, out IntPtr device);
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] static extern int GetTemp(IntPtr device, int sensor, out uint temp);

        static bool tried, ok;
        static IntPtr device;

        public static double Read()
        {
            try
            {
                if (!tried)
                {
                    tried = true;
                    ok = Init() == 0 && GetHandle(0, out device) == 0;
                }
                uint t;
                if (ok && GetTemp(device, 0, out t) == 0) return t;
            }
            catch { ok = false; } // no NVIDIA driver: nvml.dll isn't there
            return double.NaN;
        }

        public static bool Available
        {
            get { Read(); return ok; }
        }
    }

    // Screen colours for the Screen Ambient effect: the main screen shrunk to a strip of columns with GDI
    // (cheap, and good enough for lighting). Exclusive-fullscreen games may not be captured; borderless works.
    public static class ScreenSampler
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);

        public const int Columns = 32, Rows = 18;
        static Rgb[] smooth = new Rgb[0];

        public static Rgb[] Capture()
        {
            int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
            Rgb[] cols = new Rgb[Columns];
            if (sw <= 0 || sh <= 0) return cols;
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(Columns, Rows, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr dst = g.GetHdc(), src = GetDC(IntPtr.Zero);
                    SetStretchBltMode(dst, 4); // HALFTONE: averages the pixels instead of skipping them
                    StretchBlt(dst, 0, 0, Columns, Rows, src, 0, 0, sw, sh, 0x00CC0020);
                    ReleaseDC(IntPtr.Zero, src);
                    g.ReleaseHdc(dst);
                }
                for (int x = 0; x < Columns; x++)
                {
                    int r = 0, gr = 0, b = 0;
                    for (int y = 0; y < Rows; y++)
                    {
                        System.Drawing.Color px = bmp.GetPixel(x, y);
                        r += px.R; gr += px.G; b += px.B;
                    }
                    cols[x] = Vivid(new Rgb(r / Rows, gr / Rows, b / Rows));
                }
            }
            // Ease between captures so the lights don't flicker with small screen changes.
            if (smooth.Length != Columns) smooth = cols;
            for (int x = 0; x < Columns; x++) smooth[x] = EffectRenderer.Lerp(smooth[x], cols[x], 0.5);
            return (Rgb[])smooth.Clone();
        }

        // Screen colours are mostly greyish; push the saturation so the lights show the colour.
        static Rgb Vivid(Rgb c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max < 8) return new Rgb(0, 0, 0);
            double boost = 1.6;
            double avg = (c.R + c.G + c.B) / 3.0;
            Func<double, int> s = delegate (double v) { return (int)Math.Max(0, Math.Min(255, avg + (v - avg) * boost)); };
            Rgb v2 = new Rgb(s(c.R), s(c.G), s(c.B));
            int m2 = Math.Max(v2.R, Math.Max(v2.G, v2.B));
            return m2 == 0 ? v2 : EffectRenderer.Scale(v2, Math.Min(2.0, 255.0 / m2) * 0.9);
        }
    }
}