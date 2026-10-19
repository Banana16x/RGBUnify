using System;
using System.Runtime.InteropServices;

namespace RGBUnify
{
    // PerFan only applies to fan headers: each fan shows its own colour (Engine fills it in, not the renderer).
    public enum EffectKind { Static, Breathing, Spectrum, RainbowWave, ColorWave, Comet, Starlight, Music, Off, PerFan }

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
