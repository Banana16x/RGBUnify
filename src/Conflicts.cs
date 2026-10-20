using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace RGBUnify
{
    public enum ConflictKind { App, StoreApp, Service, Driver, Process, Task, Folder, RegKey, Reinstall }

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
        // Folder path, registry key ("HKLM\..."), task path, or Store package full name.
        public string Path;

        public bool IsLeftover
        {
            get { return Kind == ConflictKind.Folder || Kind == ConflictKind.RegKey || Kind == ConflictKind.Task; }
        }

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case ConflictKind.App: return "Program";
                    case ConflictKind.StoreApp: return "Store app";
                    case ConflictKind.Service: return "Background service";
                    case ConflictKind.Driver: return "Low-level driver";
                    case ConflictKind.Task: return "Scheduled task";
                    case ConflictKind.Folder: return "Leftover files";
                    case ConflictKind.RegKey: return "Leftover registry";
                    case ConflictKind.Reinstall: return "Auto-reinstall";
                    default: return "Running now";
                }
            }
        }
    }

    // Finds other RGB software that fights over the same hardware, and (like Revo Uninstaller) what it leaves behind.
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
            new Sig("ASUS", AlsoFans, "armoury crate", "armourycrate", "armoury device", "aura sync", "aurasync", "aura creator", "lightingservice", "asus aura", "aura service", "aurawallpaper",
                "rog live service", "asus framework", "gamesdk service"),
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

        // Never touched, even if a name happens to match: laptops need these for Fn keys, keyboard light, battery and fans.
        static readonly string[] Protected = {
            "openrgb", "rgb unify", "pawnio", "system control interface", "asus optimization", "myasus", "asus link", "glidex",
            "atk package", "atkwmiacpi", "asus smart display", "asus battery health", "asus system analysis", "asus system diagnosis",
            "lenovo vantage", "legion zone", "msi feature", "dell command", "hp system event"
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
            new[] { "LightingService", "ASUS" }, new[] { "ArmouryCrate", "ASUS" }, new[] { "AuraWallpaperService", "ASUS" }, new[] { "ArmourySocketServer", "ASUS" },
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

        // Microsoft Store packages (Get-AppxPackage names) of RGB suites.
        static readonly string[][] StoreSigs = {
            new[] { "ArmouryCrate", "ASUS" }, new[] { "B9ECED6F.Aura", "ASUS" }, new[] { "ROGLiveService", "ASUS" },
            new[] { "MSICenter", "MSI" }, new[] { "DragonCenter", "MSI" }, new[] { "MysticLight", "MSI" },
            new[] { "OMENLightStudio", "HP Omen" },
        };

        // Where each suite leaves files and settings behind. Only exact, suite-specific places (a * is allowed at the end),
        // never a shared vendor folder, and only reported once none of that brand's RGB programs are installed any more.
        class Leftovers
        {
            public string Brand;
            public string[] Folders, Keys;
            public Leftovers(string brand, string[] folders, string[] keys) { Brand = brand; Folders = folders; Keys = keys; }
        }

        static readonly Leftovers[] LeftoverSigs = {
            new Leftovers("ASUS", new[] {
                    @"%ProgramFiles%\ASUS\ARMOURY CRATE*", @"%ProgramFiles(x86)%\ASUS\ARMOURY CRATE*", @"%ProgramFiles%\ASUS\AXSP",
                    @"%ProgramFiles(x86)%\ASUS\AXSP", @"%ProgramFiles(x86)%\ASUS\ArmouryDevice", @"%ProgramFiles(x86)%\ASUS\GameSDK Service",
                    @"%ProgramFiles%\ASUS\ROG Live Service", @"%ProgramFiles(x86)%\LightingService", @"%ProgramFiles%\ASUS\Aura*",
                    @"%ProgramFiles(x86)%\ASUS\Aura*", @"%ProgramData%\ASUS\ARMOURY CRATE*", @"%ProgramData%\ASUS\AXSP",
                    @"%ProgramData%\ASUS\GameSDK Service", @"%ProgramData%\ASUS\ROG Live Service", @"%ProgramData%\ASUS\Aura*",
                    @"%LOCALAPPDATA%\ASUS\ARMOURY CRATE*", @"%APPDATA%\ASUS\ARMOURY CRATE*" },
                new[] {
                    @"HKLM\SOFTWARE\ASUS\ARMOURY CRATE*", @"HKLM\SOFTWARE\WOW6432Node\ASUS\ARMOURY CRATE*", @"HKLM\SOFTWARE\ASUS\AXSP",
                    @"HKLM\SOFTWARE\WOW6432Node\ASUS\AXSP", @"HKLM\SOFTWARE\WOW6432Node\LightingService", @"HKLM\SOFTWARE\ASUS\Aura*",
                    @"HKLM\SOFTWARE\WOW6432Node\ASUS\Aura*", @"HKLM\SOFTWARE\ASUS\ROG Live Service", @"HKCU\Software\ASUS\ARMOURY CRATE*",
                    @"HKCU\Software\ASUS\Aura*" }),
            new Leftovers("Corsair", new[] {
                    @"%ProgramFiles%\Corsair\CORSAIR iCUE*", @"%ProgramFiles(x86)%\Corsair\CORSAIR iCUE*", @"%ProgramData%\Corsair\CUE*",
                    @"%APPDATA%\Corsair\CUE*", @"%LOCALAPPDATA%\Corsair\CUE*" },
                new[] { @"HKLM\SOFTWARE\Corsair\Corsair Utility Engine", @"HKLM\SOFTWARE\WOW6432Node\Corsair\Corsair Utility Engine",
                    @"HKCU\Software\Corsair\Corsair Utility Engine" }),
            new Leftovers("MSI", new[] {
                    @"%ProgramFiles(x86)%\MSI\MSI Center", @"%ProgramFiles(x86)%\MSI\One Dragon Center", @"%ProgramFiles(x86)%\MSI\Dragon Center",
                    @"%ProgramFiles(x86)%\MSI\Mystic Light*", @"%ProgramData%\MSI\One Dragon Center", @"%ProgramData%\MSI\MSI Center" },
                new[] { @"HKLM\SOFTWARE\WOW6432Node\MSI\MSI Center", @"HKLM\SOFTWARE\WOW6432Node\MSI\One Dragon Center", @"HKCU\Software\MSI\MSI Center" }),
            new Leftovers("Gigabyte / Aorus", new[] { @"%ProgramFiles(x86)%\GIGABYTE\RGBFusion", @"%ProgramFiles%\GIGABYTE\RGBFusion" },
                new[] { @"HKLM\SOFTWARE\WOW6432Node\GIGABYTE\RGBFusion" }),
            new Leftovers("Razer", new[] { @"%ProgramFiles(x86)%\Razer\Synapse3", @"%ProgramData%\Razer\Synapse3", @"%LOCALAPPDATA%\Razer\Synapse3" },
                new[] { @"HKCU\Software\Razer\Synapse3" }),
            new Leftovers("NZXT", new[] { @"%LOCALAPPDATA%\Programs\NZXT CAM", @"%APPDATA%\NZXT CAM", @"%ProgramFiles%\NZXT CAM" }, new string[0]),
            new Leftovers("Lian Li", new[] { @"%ProgramFiles%\L-Connect*", @"%ProgramFiles(x86)%\L-Connect*", @"%APPDATA%\L-Connect*" }, new string[0]),
            new Leftovers("SignalRGB", new[] { @"%LOCALAPPDATA%\VortxEngine" }, new string[0]),
            new Leftovers("Glorious", new[] { @"%ProgramFiles%\Glorious Core", @"%ProgramFiles(x86)%\Glorious Core", @"%APPDATA%\Glorious Core" }, new string[0]),
        };

        static bool IsProtected(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            foreach (string p in Protected) if (t.Contains(p)) return true;
            return false;
        }

        static Sig Match(string text)
        {
            if (string.IsNullOrEmpty(text) || IsProtected(text)) return null;
            string t = text.ToLowerInvariant();
            foreach (Sig s in Sigs)
                foreach (string w in s.Words)
                    if (t.Contains(w)) return s;
            return null;
        }

        static string NoteFor(Sig s)
        {
            if (s.Brand == "ASUS" && SystemInfo.IsLaptop)
                return "On ASUS laptops this also runs fan/performance modes, GPU mode and the battery limit. G-Helper (free) can take those over.";
            return s.Note;
        }

        public static List<Conflict> Scan()
        {
            List<Conflict> found = new List<Conflict>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ScanUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, found, seen);
            ScanUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, found, seen);
            ScanUninstall(RegistryHive.CurrentUser, RegistryView.Default, found, seen);
            ScanStoreApps(found);

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

            ScanTasks(found);
            ScanLeftovers(found);
            ScanReinstall(found);
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
                            string keyPath = (hive == RegistryHive.CurrentUser ? "HKCU" : view == RegistryView.Registry32 ? "HKLM32" : "HKLM") +
                                             @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + sub;
                            // A Programs list entry whose uninstaller is gone can't be removed normally: offer to clear the entry.
                            if (!UninstallerExists(cmd))
                            {
                                found.Add(new Conflict { Kind = ConflictKind.RegKey, Name = "Broken uninstall entry: " + name, Brand = s.Brand, Path = keyPath,
                                    Note = "Its uninstaller is already gone; this only clears it from Settings > Apps." });
                                continue;
                            }
                            found.Add(new Conflict
                            {
                                Kind = ConflictKind.App, Name = name, Publisher = pub, Brand = s.Brand, Note = NoteFor(s),
                                UninstallCmd = cmd, QuietUninstallCmd = k.GetValue("QuietUninstallString") as string
                            });
                        }
                    }
                }
            }
            catch { }
        }

        static bool UninstallerExists(string cmd)
        {
            try
            {
                if (cmd.ToLowerInvariant().Contains("msiexec")) return true;
                string file, args;
                SplitCommand(cmd, out file, out args);
                file = Environment.ExpandEnvironmentVariables(file);
                return !System.IO.Path.IsPathRooted(file) || File.Exists(file);
            }
            catch { return true; }
        }

        static void ScanStoreApps(List<Conflict> found)
        {
            string output = RunCapture("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + '|' + $_.PackageFullName }\"");
            foreach (string line in output.Split('\n'))
            {
                string[] parts = line.Trim().Split('|');
                if (parts.Length != 2 || IsProtected(parts[0])) continue;
                foreach (string[] st in StoreSigs)
                {
                    if (parts[0].IndexOf(st[0], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found.Add(new Conflict { Kind = ConflictKind.StoreApp, Name = parts[0], Brand = st[1], Path = parts[1],
                        Note = "Microsoft Store version (not in the normal Programs list)" });
                    break;
                }
            }
        }

        static void ScanTasks(List<Conflict> found)
        {
            // CSV: "TaskName","Next Run Time","Status" (one line per task and trigger).
            string output = RunCapture("schtasks.exe", "/query /fo csv /nh");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in output.Split('\n'))
            {
                if (!line.StartsWith("\"")) continue;
                int end = line.IndexOf('"', 1);
                if (end < 0) continue;
                string task = line.Substring(1, end - 1);
                if (task.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) || !seen.Add(task)) continue;
                Sig s = Match(task);
                if (s == null && task.IndexOf("armoury", StringComparison.OrdinalIgnoreCase) >= 0 && !IsProtected(task)) s = Sigs[2];
                if (s == null) continue;
                found.Add(new Conflict { Kind = ConflictKind.Task, Name = task.TrimStart('\\'), Brand = s.Brand, Path = task,
                    Note = "Starts or updates it in the background" });
            }
        }

        // Leftovers of a brand are only listed once none of its RGB programs are installed any more.
        static void ScanLeftovers(List<Conflict> found)
        {
            HashSet<string> installed = new HashSet<string>();
            foreach (Conflict c in found) if (c.Kind == ConflictKind.App || c.Kind == ConflictKind.StoreApp) installed.Add(c.Brand);
            foreach (Leftovers l in LeftoverSigs)
            {
                if (installed.Contains(l.Brand)) continue;
                foreach (string pattern in l.Folders)
                    foreach (string dir in ExpandFolder(pattern))
                        found.Add(new Conflict { Kind = ConflictKind.Folder, Name = dir, Brand = l.Brand, Path = dir,
                            Note = "Goes to the Recycle Bin" });
                foreach (string pattern in l.Keys)
                    foreach (string key in ExpandKey(pattern))
                        found.Add(new Conflict { Kind = ConflictKind.RegKey, Name = key, Brand = l.Brand, Path = key,
                            Note = "Backed up to a .reg file first" });
            }
        }

        static void ScanReinstall(List<Conflict> found)
        {
            if (!SystemInfo.HasWpbt() || WpbtDisabled()) return;
            string brand = SystemInfo.MadeBy("ASUS") ? "ASUS" : SystemInfo.MadeBy("Micro-Star") || SystemInfo.MadeBy("MSI") ? "MSI" :
                           SystemInfo.MadeBy("Gigabyte") ? "Gigabyte / Aorus" : SystemInfo.Maker;
            found.Add(new Conflict
            {
                Kind = ConflictKind.Reinstall, Brand = brand, Name = "Stop the BIOS from reinstalling " + (brand == "ASUS" ? "Armoury Crate" : brand + " software"),
                Note = "Your BIOS tells Windows to install the maker's app at every start (WPBT). This turns that off for all apps."
            });
        }

        static bool WpbtDisabled()
        {
            object v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", null);
            return v is int && (int)v == 1;
        }

        // "%ProgramFiles%\ASUS\ARMOURY CRATE*" -> the folders that actually exist.
        static List<string> ExpandFolder(string pattern)
        {
            List<string> result = new List<string>();
            try
            {
                string p = Environment.ExpandEnvironmentVariables(pattern);
                if (p.Contains("%")) return result;
                string parent = System.IO.Path.GetDirectoryName(p), leaf = System.IO.Path.GetFileName(p);
                if (!Directory.Exists(parent)) return result;
                foreach (string d in Directory.GetDirectories(parent, leaf)) result.Add(d);
            }
            catch { }
            return result;
        }

        static List<string> ExpandKey(string pattern)
        {
            List<string> result = new List<string>();
            try
            {
                RegistryKey root;
                string rest;
                if (!OpenRoot(pattern, out root, out rest)) return result;
                using (root)
                {
                    int slash = rest.LastIndexOf('\\');
                    string parent = rest.Substring(0, slash), leaf = rest.Substring(slash + 1);
                    using (RegistryKey pk = root.OpenSubKey(parent))
                    {
                        if (pk == null) return result;
                        string prefix = pattern.Substring(0, pattern.Length - leaf.Length);
                        foreach (string sub in pk.GetSubKeyNames())
                            if (leaf.EndsWith("*") ? sub.StartsWith(leaf.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                                                   : sub.Equals(leaf, StringComparison.OrdinalIgnoreCase))
                                result.Add(prefix + sub);
                    }
                }
            }
            catch { }
            return result;
        }

        // "HKLM\...", "HKLM32\..." (32-bit view) or "HKCU\..." -> base key + the rest of the path.
        static bool OpenRoot(string path, out RegistryKey root, out string rest)
        {
            root = null;
            rest = null;
            int slash = path.IndexOf('\\');
            if (slash < 0) return false;
            string hive = path.Substring(0, slash).ToUpperInvariant();
            rest = path.Substring(slash + 1);
            if (hive == "HKLM") root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            else if (hive == "HKLM32") root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            else if (hive == "HKCU") root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            return root != null;
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
                    case ConflictKind.Task:
                        return Run("schtasks.exe", "/change /tn \"" + c.Path + "\" /disable") == 0 ? "Disabled task " + c.Name : "Could not disable task " + c.Name;
                    case ConflictKind.Reinstall:
                        return DisableWpbt();
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
                    case ConflictKind.StoreApp:
                        return RemoveStoreApp(c);
                    case ConflictKind.Service:
                    case ConflictKind.Driver:
                        Run("sc.exe", "stop \"" + c.ServiceName + "\"");
                        int code = Run("sc.exe", "delete \"" + c.ServiceName + "\"");
                        if (code == 1060) return c.ServiceName + " was already removed";
                        return code == 0 ? "Removed " + c.KindText.ToLower() + " " + c.ServiceName + " (fully gone after reboot)"
                                         : "Could not remove " + c.ServiceName + " (sc error " + code + ")";
                    case ConflictKind.Task:
                        return Run("schtasks.exe", "/delete /tn \"" + c.Path + "\" /f") == 0 ? "Removed task " + c.Name : "Could not remove task " + c.Name;
                    case ConflictKind.Folder:
                        return RemoveFolder(c.Path);
                    case ConflictKind.RegKey:
                        return RemoveKey(c.Path);
                    case ConflictKind.Reinstall:
                        return DisableWpbt();
                    default:
                        return StopNow(c);
                }
            }
            catch (Exception ex) { return "Failed on " + c.Name + ": " + ex.Message; }
        }

        static string RemoveStoreApp(Conflict c)
        {
            string name = c.Name.Replace("'", "");
            string full = c.Path.Replace("'", "");
            int code = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -AllUsers -Package '" + full + "'; " +
                "Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '" + name + "' | Remove-AppxProvisionedPackage -Online -AllUsers\"");
            return code == 0 ? "Removed Store app " + c.Name : "Store app " + c.Name + " could not be removed (code " + code + ")";
        }

        static string DisableWpbt()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", true))
                k.SetValue("DisableWpbtExecution", 1, RegistryValueKind.DWord);
            return "The BIOS can no longer reinstall vendor apps (DisableWpbtExecution = 1). To undo it, delete that value.";
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool MoveFileEx(string from, string to, int flags);
        const int MOVEFILE_DELAY_UNTIL_REBOOT = 4;

        static string RemoveFolder(string dir)
        {
            if (!Directory.Exists(dir)) return dir + " was already gone";
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return "Moved to the Recycle Bin: " + dir;
            }
            catch (Exception ex)
            {
                // Files still in use: Windows deletes them at the next restart.
                int pending = 0;
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        try { File.Delete(f); } catch { if (MoveFileEx(f, null, MOVEFILE_DELAY_UNTIL_REBOOT)) pending++; }
                    string[] dirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories);
                    Array.Sort(dirs);
                    Array.Reverse(dirs);
                    foreach (string d in dirs) try { Directory.Delete(d); } catch { MoveFileEx(d, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
                    try { Directory.Delete(dir); } catch { MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
                }
                catch { }
                return pending > 0 ? "Removed " + dir + " (" + pending + " file(s) in use are deleted at the next restart)"
                                   : "Could not fully remove " + dir + ": " + ex.Message;
            }
        }

        public static string BackupFolder
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"RGBUnify\Backups"); }
        }

        static string RemoveKey(string path)
        {
            RegistryKey root;
            string rest;
            if (!OpenRoot(path, out root, out rest)) return "Unknown registry path " + path;
            using (root)
            {
                using (RegistryKey k = root.OpenSubKey(rest))
                    if (k == null) return path + " was already gone";
                // Backup first: double-click the .reg file to put it back.
                Directory.CreateDirectory(BackupFolder);
                string file = System.IO.Path.Combine(BackupFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Math.Abs(path.GetHashCode()) + ".reg");
                string regPath = (path.StartsWith("HKCU") ? "HKCU" : "HKLM") + rest.Insert(0, "\\");
                bool wow = path.StartsWith("HKLM32");
                Run("reg.exe", "export \"" + regPath + "\" \"" + file + "\" /y" + (wow ? " /reg:32" : " /reg:64"));
                root.DeleteSubKeyTree(rest, false);
                return "Removed registry key " + path + (File.Exists(file) ? " (backup: " + file + ")" : "");
            }
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

        static string RunCapture(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    return output;
                }
            }
            catch { return ""; }
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
