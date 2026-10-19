using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace RGBUnify
{
    public enum ConflictKind { App, Service, Driver, Process }

    public class Conflict
    {
        public ConflictKind Kind;
        public string Name;
        public string Publisher = "";
        public string Brand;
        public string Note = "";
        public string UninstallCmd;
        public string QuietUninstallCmd;
        public string ServiceName;
        public int ProcessId;

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case ConflictKind.App: return "Program";
                    case ConflictKind.Service: return "Background service";
                    case ConflictKind.Driver: return "Low-level driver";
                    default: return "Running now";
                }
            }
        }
    }

    // Finds other RGB software that fights over the same hardware.
    public static class ConflictScanner
    {
        class Sig
        {
            public string Brand, Note;
            public string[] Words;
            public Sig(string brand, string note, params string[] words) { Brand = brand; Note = note; Words = words; }
        }

        const string AlsoPeripherals = "Also stores keyboard/mouse settings (DPI, macros).";
        const string AlsoFans = "Also controls fan curves / performance on some systems.";

        static readonly Sig[] Sigs = {
            new Sig("MSI", AlsoFans, "mystic light", "msi center", "dragon center", "msi.centralserver", "msi sdk", "mystic_light", "msi_center", "lightkeeper"),
            new Sig("Corsair", AlsoPeripherals, "icue", "corsair link", "corsair utility engine", "corsairservice", "corsair.service", "corsair llaccess", "corsairllaccess", "corsair cpuid"),
            new Sig("ASUS", AlsoFans, "armoury crate", "armourycrate", "aura sync", "aura creator", "lightingservice", "asus aura", "aura service", "rog live service", "asus framework"),
            new Sig("Gigabyte / Aorus", AlsoFans, "rgb fusion", "rgbfusion", "gigabyte control center", "gcc service", "aorus engine"),
            new Sig("ASRock", "", "polychrome", "asrock rgb"),
            new Sig("Razer", AlsoPeripherals, "razer synapse", "razer chroma", "razer central", "razer game manager"),
            new Sig("NZXT", AlsoFans, "nzxt cam", "nzxt.cam"),
            new Sig("Lian Li", AlsoFans, "l-connect", "lconnect", "lian li"),
            new Sig("Thermaltake", AlsoFans, "tt rgb plus", "thermaltake", "dps g app"),
            new Sig("Cooler Master", AlsoPeripherals, "masterplus", "masterctrl", "cooler master"),
            new Sig("SignalRGB", "", "signalrgb", "signal rgb"),
            new Sig("HyperX", AlsoPeripherals, "ngenuity", "hyperx"),
            new Sig("Logitech", AlsoPeripherals, "g hub", "lghub", "logitech gaming software"),
            new Sig("SteelSeries", AlsoPeripherals, "steelseries gg", "steelseries engine"),
            new Sig("EVGA", AlsoFans, "precision x1", "evga led sync"),
            new Sig("DeepCool", "", "deepcool"),
            new Sig("Phanteks", "", "phanteks"),
            new Sig("Patriot", "", "viper rgb", "viper gaming rgb"),
            new Sig("TeamGroup", "", "t-force blitz", "tforce blitz"),
            new Sig("G.Skill", "", "trident z lighting", "g.skill lighting"),
            new Sig("Kingston", "", "fury ctrl", "fury beast"),
            new Sig("ADATA / XPG", "", "xpg prime", "xpg rgb"),
            new Sig("Alienware / Dell", AlsoFans, "alienfx", "alienware command center", "awcc"),
            new Sig("HP Omen", AlsoFans, "omen light studio"),
            new Sig("Roccat / Turtle Beach", AlsoPeripherals, "roccat swarm", "swarm ii"),
            new Sig("Glorious", AlsoPeripherals, "glorious core"),
            new Sig("SnowmanPC", "", "snowman", "snowmanpc"),
            new Sig("Generic ARGB", "", "argb controller", "rgb controller", "jonsbo", "ezdiy", "coolmoon", "aigo"),
        };

        // Kernel drivers left behind by vendor RGB tools.
        static readonly string[][] DriverSigs = {
            new[] { "CorsairLLAccess", "Corsair" },
            new[] { "CorsairVBus", "Corsair" },
            new[] { "NTIOLib", "MSI" },
            new[] { "AsIO", "ASUS" },
            new[] { "AsUpIO", "ASUS" },
            new[] { "GLCKIO", "ASUS" },
            new[] { "gdrv", "Gigabyte" },
            new[] { "GVCIDrv", "Gigabyte" },
            new[] { "RTCore64", "MSI" },
            new[] { "EneIo", "Generic ENE" },
            new[] { "EneTechIo", "Generic ENE" },
            new[] { "AxtuDrv", "ASRock" },
        };

        static readonly string[][] ProcessSigs = {
            new[] { "iCUE", "Corsair" }, new[] { "Corsair.Service", "Corsair" },
            new[] { "LightingService", "ASUS" }, new[] { "ArmouryCrate", "ASUS" }, new[] { "AuraWallpaperService", "ASUS" },
            new[] { "MSI.CentralServer", "MSI" }, new[] { "MSI Center", "MSI" }, new[] { "MysticLight", "MSI" }, new[] { "LEDKeeper", "MSI" },
            new[] { "RGBFusion", "Gigabyte" }, new[] { "GCC$", "Gigabyte" },
            new[] { "Polychrome", "ASRock" },
            new[] { "Razer Synapse", "Razer" }, new[] { "RazerCentralService", "Razer" }, new[] { "Razer Synapse Service", "Razer" },
            new[] { "NZXT CAM", "NZXT" }, new[] { "L-Connect", "Lian Li" }, new[] { "LConnect", "Lian Li" },
            new[] { "TT RGB Plus", "Thermaltake" }, new[] { "SignalRgb", "SignalRGB" }, new[] { "SignalRgbLauncher", "SignalRGB" },
            new[] { "NGenuity", "HyperX" }, new[] { "lghub", "Logitech" }, new[] { "SteelSeriesGG", "SteelSeries" },
            new[] { "PrecisionX_x64", "EVGA" }, new[] { "OmenLightStudio", "HP Omen" }, new[] { "AWCC$", "Alienware" },
            new[] { "Snowman", "SnowmanPC" },
        };

        static Sig Match(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string t = text.ToLowerInvariant();
            if (t.Contains("openrgb") || t.Contains("rgb unify")) return null;
            foreach (Sig s in Sigs)
                foreach (string w in s.Words)
                    if (t.Contains(w)) return s;
            return null;
        }

        public static List<Conflict> Scan()
        {
            List<Conflict> found = new List<Conflict>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ScanUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, found, seen);
            ScanUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, found, seen);
            ScanUninstall(RegistryHive.CurrentUser, RegistryView.Default, found, seen);

            try
            {
                foreach (ServiceController sc in ServiceController.GetServices())
                {
                    Sig s = Match(sc.ServiceName + " " + sc.DisplayName);
                    if (s == null) continue;
                    found.Add(new Conflict { Kind = ConflictKind.Service, Name = sc.DisplayName, Brand = s.Brand, ServiceName = sc.ServiceName,
                        Note = sc.Status == ServiceControllerStatus.Running ? "Running" : "Stopped" });
                }
            }
            catch { }

            try
            {
                foreach (ServiceController sc in ServiceController.GetDevices())
                {
                    foreach (string[] d in DriverSigs)
                    {
                        if (!sc.ServiceName.StartsWith(d[0], StringComparison.OrdinalIgnoreCase)) continue;
                        found.Add(new Conflict { Kind = ConflictKind.Driver, Name = sc.ServiceName + " (" + sc.DisplayName + ")", Brand = d[1],
                            ServiceName = sc.ServiceName, Note = "Leftover hardware-access driver" });
                        break;
                    }
                }
            }
            catch { }

            foreach (Process p in Process.GetProcesses())
            {
                foreach (string[] ps in ProcessSigs)
                {
                    // A trailing $ means the process name must match exactly (avoids e.g. gcc.exe compilers).
                    bool exact = ps[0].EndsWith("$");
                    string sig = ps[0].TrimEnd('$');
                    if (exact ? !p.ProcessName.Equals(sig, StringComparison.OrdinalIgnoreCase)
                              : !p.ProcessName.StartsWith(sig, StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add(new Conflict { Kind = ConflictKind.Process, Name = p.ProcessName + ".exe", Brand = ps[1], ProcessId = p.Id,
                        Note = "Currently fighting for control of your lights" });
                    break;
                }
            }
            return found;
        }

        static void ScanUninstall(RegistryHive hive, RegistryView view, List<Conflict> found, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey un = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (un == null) return;
                    foreach (string sub in un.GetSubKeyNames())
                    {
                        using (RegistryKey k = un.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string name = k.GetValue("DisplayName") as string;
                            string pub = k.GetValue("Publisher") as string ?? "";
                            string cmd = k.GetValue("UninstallString") as string;
                            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cmd)) continue;
                            object sys = k.GetValue("SystemComponent");
                            if (sys is int && (int)sys == 1) continue;
                            Sig s = Match(name);
                            if (s == null) continue;
                            if (!seen.Add(name)) continue;
                            found.Add(new Conflict
                            {
                                Kind = ConflictKind.App, Name = name, Publisher = pub, Brand = s.Brand, Note = s.Note,
                                UninstallCmd = cmd, QuietUninstallCmd = k.GetValue("QuietUninstallString") as string
                            });
                        }
                    }
                }
            }
            catch { }
        }

        // Stops it running right now (safe, undone by reboot unless the service is disabled too).
        public static string StopNow(Conflict c)
        {
            try
            {
                switch (c.Kind)
                {
                    case ConflictKind.Process:
                        Process.GetProcessById(c.ProcessId).Kill();
                        return "Closed " + c.Name;
                    case ConflictKind.Service:
                        using (ServiceController sc = new ServiceController(c.ServiceName))
                        {
                            if (sc.Status != ServiceControllerStatus.Stopped) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                        }
                        Run("sc.exe", "config \"" + c.ServiceName + "\" start= disabled");
                        return "Stopped and disabled service " + c.Name;
                    default:
                        return null;
                }
            }
            catch (Exception ex) { return "Could not stop " + c.Name + ": " + ex.Message; }
        }

        public static string Remove(Conflict c)
        {
            try
            {
                switch (c.Kind)
                {
                    case ConflictKind.App:
                        return Uninstall(c);
                    case ConflictKind.Service:
                    case ConflictKind.Driver:
                        Run("sc.exe", "stop \"" + c.ServiceName + "\"");
                        int code = Run("sc.exe", "delete \"" + c.ServiceName + "\"");
                        if (code == 1060) return c.ServiceName + " was already removed";
                        return code == 0 ? "Removed " + c.KindText.ToLower() + " " + c.ServiceName + " (fully gone after reboot)"
                                         : "Could not remove " + c.ServiceName + " (sc error " + code + ")";
                    default:
                        return StopNow(c);
                }
            }
            catch (Exception ex) { return "Failed on " + c.Name + ": " + ex.Message; }
        }

        static string Uninstall(Conflict c)
        {
            string cmd = c.QuietUninstallCmd;
            bool silent = !string.IsNullOrEmpty(cmd);
            if (!silent)
            {
                cmd = c.UninstallCmd;
                // MSI-based installers can always be removed silently.
                int g = cmd.IndexOf('{');
                if (cmd.ToLowerInvariant().Contains("msiexec") && g >= 0)
                {
                    int e = cmd.IndexOf('}', g);
                    if (e > g) { cmd = "msiexec.exe /x " + cmd.Substring(g, e - g + 1) + " /qn /norestart"; silent = true; }
                }
            }
            string file, args;
            SplitCommand(cmd, out file, out args);
            int code = Run(file, args, true);
            if (code == 0 || code == 3010 || code == 1605)
                return "Uninstalled " + c.Name + (silent ? "" : " (vendor uninstaller)");
            return "Uninstaller for " + c.Name + " exited with code " + code;
        }

        static void SplitCommand(string cmd, out string file, out string args)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                file = cmd.Substring(1, end - 1);
                args = cmd.Substring(end + 1).Trim();
                return;
            }
            int exe = cmd.ToLowerInvariant().IndexOf(".exe");
            if (exe > 0)
            {
                file = cmd.Substring(0, exe + 4);
                args = cmd.Substring(exe + 4).Trim();
                return;
            }
            int sp = cmd.IndexOf(' ');
            file = sp < 0 ? cmd : cmd.Substring(0, sp);
            args = sp < 0 ? "" : cmd.Substring(sp + 1);
        }

        static int Run(string file, string args)
        {
            return Run(file, args, false);
        }

        static int Run(string file, string args, bool visible)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = visible;
            psi.CreateNoWindow = !visible;
            if (!visible) psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(30 * 60 * 1000);
                return p.HasExited ? p.ExitCode : -1;
            }
        }

        public static string CreateRestorePoint()
        {
            try
            {
                int code = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction SilentlyContinue; Checkpoint-Computer -Description 'RGB Unify cleanup' -RestorePointType APPLICATION_UNINSTALL\"");
                return code == 0 ? "Restore point created" : "Restore point could not be created (Windows allows one per 24h) — continuing";
            }
            catch (Exception ex) { return "Restore point skipped: " + ex.Message; }
        }
    }
}
