using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RGBUnify
{
    // One of the three quick-switch lighting setups.
    public class Profile
    {
        public string Name;
        public EffectKind Effect = EffectKind.RainbowWave;
        public Rgb Color1 = new Rgb(0, 140, 255);
        public Rgb Color2 = new Rgb(255, 0, 140);
        public int Brightness = 100;
        public int Speed = 5;
        // null = fans follow the main effect; otherwise the effect the Fan button has cycled to.
        public EffectKind? FanEffect;
        // Colour of fan 1, fan 2... for the "Each fan its own colour" fan effect.
        public Rgb[] FanColors = (Rgb[])Engine.TestColors.Clone();

        public Profile(string name) { Name = name; }
    }

    public class Settings
    {
        public const int ProfileCount = 3;
        public const int MaxProfileName = 14;

        public readonly Profile[] Profiles = { new Profile("Profile 1"), new Profile("Profile 2"), new Profile("Profile 3") };
        public int ActiveProfile;

        public Profile Current
        {
            get { return Profiles[ActiveProfile]; }
        }

        public int Fps = 30;
        public bool StartMinimized = true;
        public bool CloseToTray = true;
        public List<string> DisabledDevices = new List<string>();
        // Devices whose ARGB headers run as one long chain instead of matching each other (the default).
        public List<string> FlowDevices = new List<string>();
        public bool RamEnabled;
        // Set just before a RAM scan and cleared when it finishes: still set at startup means the PC or engine died mid-scan.
        public bool RamScanPending;
        public bool RamDark;
        // Boot time (UTC ticks) of the Windows session in which RAM detection already ran; see Engine.ConfigureDetectors.
        public long RamDetectBoot;
        // Headers the user marked as fans / fan hub ("device key|zone name"). Null until chosen: then every ARGB header counts.
        public List<string> FanZones;
        // The hub's own button is in charge: RGB Unify stops sending to fan headers.
        public bool HubInCharge;
        // Fans chained on each header (zone key -> count), for "Each fan its own colour".
        public Dictionary<string, int> ZoneFans = new Dictionary<string, int>();
        // How far each fan's LED ring is turned (and whether it runs backwards) so all fans line up: fan key -> { offset, reversed }.
        public Dictionary<string, int[]> FanTwists = new Dictionary<string, int[]>();
        // Picking a Lighting effect normally makes the fans follow it again; this keeps their own look instead.
        public bool KeepFanLook;
        public string Theme = "Midnight";
        public bool Maximized = true;

        public Settings()
        {
            Profiles[1].Effect = EffectKind.Static;
            Profiles[2].Effect = EffectKind.Breathing;
        }

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGBUnify");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();

                    // "P2.Effect=..." belongs to profile 2; un-prefixed lighting keys come from v1.2 and fill every profile.
                    Profile[] targets = s.Profiles;
                    if (k.Length > 3 && k[0] == 'P' && k[2] == '.' && k[1] >= '1' && k[1] <= '3')
                    {
                        targets = new[] { s.Profiles[k[1] - '1'] };
                        k = k.Substring(3);
                    }
                    if (LoadProfileKey(targets, k, v)) continue;

                    switch (k)
                    {
                        case "Profile": int.TryParse(v, out s.ActiveProfile); break;
                        case "Fps": int.TryParse(v, out s.Fps); break;
                        case "StartMinimized": s.StartMinimized = v == "1"; break;
                        case "CloseToTray": s.CloseToTray = v == "1"; break;
                        case "Disabled": s.DisabledDevices.Add(v); break;
                        case "Flow": s.FlowDevices.Add(v); break;
                        case "RamEnabled": s.RamEnabled = v == "1"; break;
                        case "RamScanPending": s.RamScanPending = v == "1"; break;
                        case "RamDark": s.RamDark = v == "1"; break;
                        case "RamDetectBoot": long.TryParse(v, out s.RamDetectBoot); break;
                        case "FanZonesChosen": if (s.FanZones == null) s.FanZones = new List<string>(); break;
                        case "FanZone": if (s.FanZones == null) s.FanZones = new List<string>(); s.FanZones.Add(v); break;
                        case "HubInCharge": s.HubInCharge = v == "1"; break;
                        case "Twist":
                            // "<fan key>=<offset>,<reversed>"
                            int eqAt = v.LastIndexOf('=');
                            if (eqAt > 0)
                            {
                                string[] tw = v.Substring(eqAt + 1).Split(',');
                                int off, rev;
                                if (tw.Length == 2 && int.TryParse(tw[0], out off) && int.TryParse(tw[1], out rev))
                                    s.FanTwists[v.Substring(0, eqAt)] = new[] { off, rev };
                            }
                            break;
                        case "ZoneFans":
                            // "<zone key>=<fans>"; the key itself may contain '=' so split on the last one.
                            int at = v.LastIndexOf('=');
                            int fans;
                            if (at > 0 && int.TryParse(v.Substring(at + 1), out fans)) s.ZoneFans[v.Substring(0, at)] = Math.Max(1, Math.Min(16, fans));
                            break;
                        case "KeepFanLook": s.KeepFanLook = v == "1"; break;
                        case "Theme": s.Theme = v; break;
                        case "Maximized": s.Maximized = v == "1"; break;
                    }
                }
            }
            catch { }
            s.ActiveProfile = Math.Max(0, Math.Min(ProfileCount - 1, s.ActiveProfile));
            foreach (Profile p in s.Profiles)
            {
                p.Brightness = Math.Max(0, Math.Min(100, p.Brightness));
                p.Speed = Math.Max(1, Math.Min(10, p.Speed));
            }
            if (s.Fps < 10 || s.Fps > 60) s.Fps = 30;

            return s;
        }

        static bool LoadProfileKey(Profile[] targets, string k, string v)
        {
            foreach (Profile p in targets)
            {
                switch (k)
                {
                    case "Name": if (v.Length > 0) p.Name = v.Length > MaxProfileName ? v.Substring(0, MaxProfileName) : v; break;
                    case "Effect": try { p.Effect = (EffectKind)Enum.Parse(typeof(EffectKind), v); } catch { } break;
                    case "Color1": p.Color1 = ParseColor(v, p.Color1); break;
                    case "Color2": p.Color2 = ParseColor(v, p.Color2); break;
                    case "Brightness": int.TryParse(v, out p.Brightness); break;
                    case "Speed": int.TryParse(v, out p.Speed); break;
                    case "FanColors":
                        string[] parts = v.Split(',');
                        for (int i = 0; i < parts.Length && i < p.FanColors.Length; i++) p.FanColors[i] = ParseColor(parts[i], p.FanColors[i]);
                        break;
                    case "Fan":
                        try { p.FanEffect = v == "Follow" ? (EffectKind?)null : (EffectKind)Enum.Parse(typeof(EffectKind), v); } catch { }
                        break;
                    default: return false;
                }
            }
            return true;
        }

        static readonly object saveLock = new object();

        public void Save()
        {
            lock (saveLock)
            try
            {
                List<string> lines = new List<string>();
                lines.Add("Profile=" + ActiveProfile);
                for (int i = 0; i < ProfileCount; i++)
                {
                    Profile p = Profiles[i];
                    string pre = "P" + (i + 1) + ".";
                    lines.Add(pre + "Name=" + p.Name);
                    lines.Add(pre + "Effect=" + p.Effect);
                    lines.Add(pre + "Color1=" + Hex(p.Color1));
                    lines.Add(pre + "Color2=" + Hex(p.Color2));
                    lines.Add(pre + "Brightness=" + p.Brightness);
                    lines.Add(pre + "Speed=" + p.Speed);
                    lines.Add(pre + "Fan=" + (p.FanEffect.HasValue ? p.FanEffect.Value.ToString() : "Follow"));
                    List<string> fc = new List<string>();
                    foreach (Rgb c in p.FanColors) fc.Add(Hex(c));
                    lines.Add(pre + "FanColors=" + string.Join(",", fc.ToArray()));
                }
                lines.Add("Fps=" + Fps);
                lines.Add("StartMinimized=" + (StartMinimized ? "1" : "0"));
                lines.Add("CloseToTray=" + (CloseToTray ? "1" : "0"));
                lines.Add("RamEnabled=" + (RamEnabled ? "1" : "0"));
                lines.Add("RamScanPending=" + (RamScanPending ? "1" : "0"));
                lines.Add("RamDark=" + (RamDark ? "1" : "0"));
                lines.Add("RamDetectBoot=" + RamDetectBoot);
                lines.Add("HubInCharge=" + (HubInCharge ? "1" : "0"));
                foreach (KeyValuePair<string, int> kv in ZoneFans) lines.Add("ZoneFans=" + kv.Key + "=" + kv.Value);
                foreach (KeyValuePair<string, int[]> kv in FanTwists) lines.Add("Twist=" + kv.Key + "=" + kv.Value[0] + "," + kv.Value[1]);
                lines.Add("KeepFanLook=" + (KeepFanLook ? "1" : "0"));
                lines.Add("Theme=" + Theme);
                lines.Add("Maximized=" + (Maximized ? "1" : "0"));
                if (FanZones != null)
                {
                    lines.Add("FanZonesChosen=1");
                    foreach (string z in FanZones) lines.Add("FanZone=" + z);
                }
                foreach (string d in DisabledDevices) lines.Add("Disabled=" + d);
                foreach (string d in FlowDevices) lines.Add("Flow=" + d);
                // Written straight through to the disk (not left in the write cache), so it survives a hard reboot.
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines.ToArray()) + "\r\n");
                using (FileStream fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
            }
            catch { }
        }

        static string Hex(Rgb c)
        {
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        static Rgb ParseColor(string v, Rgb fallback)
        {
            try
            {
                int x = Convert.ToInt32(v, 16);
                return new Rgb((x >> 16) & 255, (x >> 8) & 255, x & 255);
            }
            catch { return fallback; }
        }

        // Runs at logon with admin rights (needed for RAM/motherboard lighting) without a UAC prompt.
        const string TaskName = "RGB Unify";

        public static bool StartupEnabled()
        {
            return RunHidden("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0;
        }

        public static void SetStartup(bool on)
        {
            SetStartup(on, Process.GetCurrentProcess().MainModule.FileName);
        }

        public static void SetStartup(bool on, string exePath)
        {
            if (on)
            {
                // XML form so the task isn't killed after 3 days or skipped on battery (schtasks defaults).
                string exe = System.Security.SecurityElement.Escape(exePath);
                string user = System.Security.SecurityElement.Escape(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
                string xml =
                    "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                    "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                    "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n" +
                    "<Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                    "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                    "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority></Settings>\r\n" +
                    "<Actions Context=\"Author\"><Exec><Command>\"" + exe + "\"</Command><Arguments>--tray</Arguments></Exec></Actions>\r\n" +
                    "</Task>\r\n";
                string tmp = Path.Combine(Path.GetTempPath(), "rgbunify_task.xml");
                File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
                RunHidden("schtasks.exe", "/Create /F /TN \"" + TaskName + "\" /XML \"" + tmp + "\"");
                try { File.Delete(tmp); } catch { }
            }
            else
            {
                RunHidden("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"");
            }
        }

        static int RunHidden(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
