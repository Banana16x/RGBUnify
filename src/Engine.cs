using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;

namespace RGBUnify
{
    // Runs the OpenRGB driver engine invisibly and streams our effects to every detected device.
    public class Engine
    {
        public const string EngineUrl = "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip";
        const int Port = 6742;

        public readonly EffectRenderer Renderer = new EffectRenderer();
        public HashSet<string> DisabledKeys = new HashSet<string>();
        public int Fps = 30;

        public event Action DevicesChanged;
        public event Action<string> StatusChanged;

        OpenRgbClient client;
        Process engineProc;
        Thread worker;
        volatile bool running;
        volatile bool reloadRequested;
        public volatile bool Paused;
        List<RgbDevice> devices = new List<RgbDevice>();
        readonly object devLock = new object();
        Rgb[] frame = new Rgb[0];
        readonly Rgb[] previewFrame = new Rgb[64];
        int identifyIndex = -1;
        DateTime identifyUntil;
        readonly Dictionary<int, Rgb> lastStatic = new Dictionary<int, Rgb>();
        DateTime lastStaticSend;

        public string Status { get; private set; }

        public static string AppDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string FindEngine()
        {
            string[] candidates = {
                Path.Combine(AppDir, "engine"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\OpenRGB"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\OpenRGB")
            };
            foreach (string dir in candidates)
            {
                if (!Directory.Exists(dir)) continue;
                string[] found = Directory.GetFiles(dir, "OpenRGB.exe", SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
            return null;
        }

        public static void DownloadEngine(Action<int> progress)
        {
            DownloadEngine(AppDir, progress);
        }

        public static void DownloadEngine(string appDir, Action<int> progress)
        {
            string dir = Path.Combine(appDir, "engine");
            string zip = Path.Combine(appDir, "engine_download.zip");
            DownloadFile(EngineUrl, zip, progress);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            ZipFile.ExtractToDirectory(zip, dir);
            File.Delete(zip);
        }

        public const string PawnIOUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";

        public static bool PawnIOInstalled()
        {
            foreach (string path in new[] { @"SYSTEM\CurrentControlSet\Services\PawnIO", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" })
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path))
                    if (k != null) return true;
            return false;
        }

        // ---------- one-time setup (engine download, PawnIO driver) ----------

        public volatile int DownloadPercent = -1;
        public volatile string SetupError;
        public volatile string SetupTask;
        bool autoDownloadTried;

        public bool SetupBusy { get { return SetupTask != null; } }

        public void BeginInstallEngine()
        {
            RunSetup("Downloading RGB engine", delegate
            {
                Thread.Sleep(300);
                KillEngine();
                Thread.Sleep(300);
                DownloadEngine(delegate (int p) { DownloadPercent = p; });
            }, "Engine download failed: ");
        }

        // PawnIO is the signed kernel driver OpenRGB needs for RAM, motherboard and many GPU lights.
        public void BeginInstallPawnIO()
        {
            RunSetup("Installing PawnIO driver", delegate
            {
                InstallPawnIO(delegate (int p) { DownloadPercent = p; }, delegate { DownloadPercent = -1; KillEngine(); });
                // Lets the uninstaller offer to remove the driver again, since RGB Unify put it there.
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RGBUnify", true))
                        if (k != null) k.SetValue("InstalledPawnIO", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }
            }, "PawnIO install failed: ");
        }

        // beforeRun fires between the download and the silent install (the engine must not hold the driver).
        public static void InstallPawnIO(Action<int> progress, Action beforeRun)
        {
            string exe = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
            DownloadFile(PawnIOUrl, exe, progress);
            if (beforeRun != null) beforeRun();
            ProcessStartInfo psi = new ProcessStartInfo(exe, "-install -silent");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi)) p.WaitForExit(120000);
            try { File.Delete(exe); } catch { }
            if (!PawnIOInstalled()) throw new Exception("the installer did not finish (try running RGB Unify as administrator)");
        }

        void RunSetup(string task, ThreadStart work, string errPrefix)
        {
            if (SetupBusy) return;
            SetupTask = task;
            SetupError = null;
            DownloadPercent = 0;
            Thread t = new Thread(delegate ()
            {
                Paused = true;
                try { work(); }
                catch (Exception ex) { SetupError = errPrefix + ex.Message; }
                DownloadPercent = -1;
                engineCheckedAt = DateTime.MinValue;
                SetupTask = null;
                Paused = false;
                Rescan();
            });
            t.IsBackground = true;
            t.Start();
        }

        static void DownloadFile(string url, string dest, Action<int> progress)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "RGBUnify");
                ManualResetEvent done = new ManualResetEvent(false);
                Exception err = null;
                wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e) { progress(e.ProgressPercentage); };
                wc.DownloadFileCompleted += delegate (object s, System.ComponentModel.AsyncCompletedEventArgs e) { err = e.Error; done.Set(); };
                wc.DownloadFileAsync(new Uri(url), dest);
                done.WaitOne();
                if (err != null) throw err;
            }
        }

        public List<RgbDevice> Devices
        {
            get { lock (devLock) return new List<RgbDevice>(devices); }
        }

        public bool IsEnabled(RgbDevice d)
        {
            if (d.TypeName == "RAM" && !RamEnabled) return false;
            return !DisabledKeys.Contains(d.Key);
        }

        public void SetEnabled(RgbDevice d, bool on)
        {
            if (on) DisabledKeys.Remove(d.Key); else DisabledKeys.Add(d.Key);
        }

        public void Identify(RgbDevice d)
        {
            identifyIndex = d.Index;
            identifyUntil = DateTime.UtcNow.AddSeconds(2);
        }

        // Addressable headers often report 0 LEDs until the user says how many are attached.
        public void ResizeZone(RgbDevice d, int zone, int size)
        {
            pendingResizes.Enqueue(new int[] { d.Index, zone, size });
            reloadRequested = true;
        }

        readonly System.Collections.Concurrent.ConcurrentQueue<int[]> pendingResizes = new System.Collections.Concurrent.ConcurrentQueue<int[]>();

        public Rgb[] GetPreview()
        {
            lock (previewFrame) return (Rgb[])previewFrame.Clone();
        }

        [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        public void Start()
        {
            timeBeginPeriod(1); // default 15.6ms sleep granularity makes animation uneven
            running = true;
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "RGB engine";
            worker.Start();
        }

        public void Stop()
        {
            running = false;
            if (worker != null) worker.Join(2000);
            timeEndPeriod(1);
            if (client != null) client.Dispose();
        }

        // Restarts the driver engine so it re-detects hardware.
        public void Rescan()
        {
            KillEngine();
            reloadRequested = true;
        }

        public void KillEngine()
        {
            if (client != null) { client.Dispose(); }
            try
            {
                if (engineProc != null && !engineProc.HasExited) engineProc.Kill();
            }
            catch { }
            engineProc = null;
        }

        void SetStatus(string s)
        {
            if (s == Status) return;
            Status = s;
            if (StatusChanged != null) StatusChanged(s);
        }

        DateTime nextConnectTry = DateTime.MinValue;
        DateTime engineStartedAt = DateTime.MinValue;
        static string cachedEngine;
        static DateTime engineCheckedAt = DateTime.MinValue;

        // Never blocks the animation: only connects once the engine's port is actually listening.
        bool EnsureConnected()
        {
            if (client != null && client.Connected) return true;
            DateTime now = DateTime.UtcNow;
            if (now < nextConnectTry) return false;
            nextConnectTry = now.AddSeconds(1);

            if (PortListening())
            {
                client = new OpenRgbClient();
                if (client.Connect("127.0.0.1", Port, 1000)) { Log.Write("Connected to engine"); connectedAt = now; DetectionDone = false; return true; }
                client = null;
            }

            string exe = FindEngineCached();
            if (exe == null)
            {
                if (!autoDownloadTried) { autoDownloadTried = true; BeginInstallEngine(); return false; }
                SetStatus(SetupError != null ? "Engine not installed (download failed)" : "Engine not installed");
                nextConnectTry = now.AddSeconds(3);
                return false;
            }
            if (engineProc == null || engineProc.HasExited)
            {
                try { ConfigureDetectors(Path.GetDirectoryName(exe)); }
                catch (Exception ex) { SetStatus("Could not configure engine: " + ex.Message); nextConnectTry = now.AddSeconds(5); return false; }
                ProcessStartInfo psi = new ProcessStartInfo(exe, "--server --server-port " + Port + " --localconfig");
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                try
                {
                    engineProc = Process.Start(psi);
                    engineStartedAt = now;
                    Log.Write("Started engine: " + exe);
                    // Lighting is never more important than the game being played.
                    try { engineProc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                }
                catch (Exception ex) { SetStatus("Could not start engine: " + ex.Message); nextConnectTry = now.AddSeconds(5); return false; }
            }
            SetStatus((now - engineStartedAt).TotalSeconds < 30 ? "Starting RGB engine..." : "Engine did not respond");
            return false;
        }

        // ---------- RAM safety ----------
        // OpenRGB's RAM detectors probe and remap addresses on the memory SMBus. On some DDR5 boards (XPG Lancer
        // among them) repeating that within one Windows session - rescans, engine restarts, sleep/wake - has
        // corrupted SPD data and stopped PCs from POSTing (OpenRGB issue #4934). So RAM detection is opt-in and
        // runs at most once per boot; every other engine start has all RAM detectors switched off.

        static readonly string[] RamDetectors = {
            "Corsair DRAM", "Corsair Vengeance RGB DRAM", "Crucial Ballistix", "ENE SMBus DRAM", "Gigabyte RGB Fusion 2 DRAM",
            "HyperX DRAM", "Kingston Fury DDR4 DRAM", "Kingston Fury DDR5 DRAM", "T-Force Xtreem DDR4 DRAM"
        };

        public bool RamEnabled;
        public long RamDetectBoot;          // boot this was last used in (UTC ticks), persisted by the UI
        public event Action RamDetectBootChanged;
        public volatile bool RamDetectionThisRun;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        public static long CurrentBoot
        {
            get { return DateTime.UtcNow.Ticks - (long)GetTickCount64() * TimeSpan.TicksPerMillisecond; }
        }

        public static bool SameBoot(long a, long b)
        {
            return Math.Abs(a - b) < TimeSpan.TicksPerMinute * 2;
        }

        // True when RAM is enabled but this boot's one detection has been used up.
        public bool RamWaitingForReboot
        {
            get { return RamEnabled && !RamDetectionThisRun && SameBoot(RamDetectBoot, CurrentBoot); }
        }

        void ConfigureDetectors(string engineDir)
        {
            bool allowRam = RamEnabled && PawnIOInstalled() && !SameBoot(RamDetectBoot, CurrentBoot);
            RamDetectionThisRun = allowRam;
            if (allowRam)
            {
                RamDetectBoot = CurrentBoot;
                if (RamDetectBootChanged != null) RamDetectBootChanged();
            }
            try
            {
                WriteRamDetectors(Path.Combine(engineDir, "OpenRGB.json"), allowRam);
                Log.Write("RAM detection " + (allowRam ? "ENABLED for this boot" : "disabled") + " for this engine start");
            }
            catch (Exception ex)
            {
                // Never start the engine with RAM detection in an unknown state.
                Log.Write("Could not write detector config: " + ex.Message);
                throw;
            }
        }

        // Sets every RAM detector in OpenRGB.json without a JSON library: rewrites existing entries and
        // adds missing ones (OpenRGB treats a missing detector as enabled).
        public static void WriteRamDetectors(string path, bool enabled)
        {
            string val = enabled ? "true" : "false";
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            List<string> missing = new List<string>(RamDetectors);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"([^\"]*(DRAM|Ballistix)[^\"]*)\"(\\s*):(\\s*)(true|false)",
                delegate (System.Text.RegularExpressions.Match m)
                {
                    missing.Remove(m.Groups[1].Value);
                    return "\"" + m.Groups[1].Value + "\"" + m.Groups[3].Value + ":" + m.Groups[4].Value + val;
                });
            if (missing.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (string n in missing) parts.Add("\"" + n + "\": " + val);
                string entries = string.Join(", ", parts.ToArray());
                System.Text.RegularExpressions.Match det = System.Text.RegularExpressions.Regex.Match(text, "\"detectors\"\\s*:\\s*\\{");
                if (det.Success)
                {
                    int at = det.Index + det.Length;
                    bool empty = text.Substring(at).TrimStart().StartsWith("}");
                    text = text.Insert(at, " " + entries + (empty ? " " : ", "));
                }
                else if (text.Trim().Length == 0)
                {
                    text = "{ \"Detectors\": { \"detectors\": { " + entries + " } } }";
                }
                else
                {
                    int brace = text.IndexOf('{');
                    if (brace < 0) throw new Exception("OpenRGB.json is not valid");
                    bool empty = text.Substring(brace + 1).TrimStart().StartsWith("}");
                    text = text.Insert(brace + 1, " \"Detectors\": { \"detectors\": { " + entries + " } }" + (empty ? " " : ", "));
                }
            }
            File.WriteAllText(path, text);
        }

        static bool PortListening()
        {
            try
            {
                foreach (System.Net.IPEndPoint ep in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                    if (ep.Port == Port) return true;
            }
            catch { return true; }
            return false;
        }

        public static string FindEngineCached()
        {
            if ((DateTime.UtcNow - engineCheckedAt).TotalSeconds > 3)
            {
                cachedEngine = FindEngine();
                engineCheckedAt = DateTime.UtcNow;
            }
            return cachedEngine;
        }

        DateTime connectedAt;
        DateTime lastLogCheck;
        public volatile bool DetectionDone;
        public volatile bool PawnIOFailed;

        // OpenRGB logs "Detection completed" when its hardware scan finishes; that's how we know
        // an empty list really means "nothing found" rather than "still looking".
        void CheckDetection(DateTime now)
        {
            if (DetectionDone || (now - lastLogCheck).TotalMilliseconds < 700) return;
            lastLogCheck = now;
            bool done = (now - connectedAt).TotalSeconds > 25;
            try
            {
                string exe = FindEngineCached();
                string logs = exe == null ? null : Path.Combine(Path.GetDirectoryName(exe), "logs");
                if (logs != null && Directory.Exists(logs))
                {
                    string newest = null;
                    DateTime newestTime = DateTime.MinValue;
                    foreach (string f in Directory.GetFiles(logs, "*.log"))
                    {
                        DateTime t = File.GetLastWriteTimeUtc(f);
                        if (t > newestTime) { newestTime = t; newest = f; }
                    }
                    if (newest != null && newestTime > connectedAt.AddMinutes(-2))
                    {
                        string text;
                        using (FileStream fs = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader sr = new StreamReader(fs)) text = sr.ReadToEnd();
                        PawnIOFailed = text.Contains("Could not open PawnIO");
                        if (text.Contains("Detection completed")) done = true;
                    }
                }
            }
            catch { }
            if (done)
            {
                DetectionDone = true;
                reloadRequested = true; // fetch the final list once more
            }
        }

        void UpdateStatus(List<RgbDevice> list)
        {
            int leds = 0;
            foreach (RgbDevice d in list) leds += d.LedCount;
            if (list.Count > 0)
                SetStatus(list.Count + " device" + (list.Count == 1 ? "" : "s") + " synced • " + leds + " LEDs" + (DetectionDone ? "" : " (still scanning)"));
            else
                SetStatus(DetectionDone ? "No RGB devices found" : "Scanning for RGB hardware...");
        }

        void ReloadDevices(string reason)
        {
            List<RgbDevice> list = client.LoadDevices();
            int switched = 0;
            foreach (RgbDevice d in list)
                if (d.SupportsDirect && !d.InDirectMode) { client.SetCustomMode(d.Index); switched++; }
            Log.Write("Reload (" + reason + "): " + list.Count + " devices, " + switched + " switched to Direct");
            lock (devLock) devices = list;
            lastStatic.Clear();
            lastSent.Clear();
            lastSentAt.Clear();
            UpdateStatus(list);
            if (DevicesChanged != null) DevicesChanged();
        }

        void Loop()
        {
            DateTime last = DateTime.UtcNow;
            int lastCount = -1;
            while (running)
            {
                DateTime now = DateTime.UtcNow;
                // Capped so a stall (reload, sleep/resume) doesn't make the effect jump.
                double dt = Math.Min(0.1, (now - last).TotalSeconds);
                last = now;
                if (Paused)
                {
                    if (client != null) client.Dispose();
                    lastCount = -1;
                    string task = SetupTask ?? "Updating engine";
                    SetStatus(task + (DownloadPercent >= 0 ? "... " + DownloadPercent + "%" : "..."));
                    lock (devLock) devices = new List<RgbDevice>();
                    RenderPreviewOnly(dt);
                    SleepFrame(now);
                    continue;
                }
                try
                {
                    if (!EnsureConnected())
                    {
                        lock (devLock) devices = new List<RgbDevice>();
                        RenderPreviewOnly(dt);
                        SleepFrame(now);
                        continue;
                    }
                    int[] rz;
                    while (pendingResizes.TryDequeue(out rz)) client.ResizeZone(rz[0], rz[1], rz[2]);
                    // Hotplug notifications arrive in bursts; wait for them to settle before reloading once.
                    bool listSettled = client.DeviceListChanged && (now - client.LastListUpdate).TotalMilliseconds > 1500;
                    if (reloadRequested || lastCount < 0 || listSettled)
                    {
                        ReloadDevices(lastCount < 0 ? "connect" : listSettled ? "device list changed" : "requested");
                        reloadRequested = false;
                        lastCount = Devices.Count;
                    }
                    // Pump also picks up "device list updated" notifications while detection is still running.
                    client.Pump();
                    if (!DetectionDone) { CheckDetection(now); UpdateStatus(Devices); }
                    RenderAndSend(dt, now);
                }
                catch (Exception ex)
                {
                    Log.Write("Engine connection lost: " + ex.GetType().Name + ": " + ex.Message);
                    if (client != null) client.Dispose();
                    SetStatus("Reconnecting to engine...");
                    lastCount = -1;
                    Thread.Sleep(1000);
                }
                SleepFrame(now);
            }
        }

        static bool Moves(EffectKind k)
        {
            return k != EffectKind.Static && k != EffectKind.Off && k != EffectKind.PerFan;
        }

        // Nothing on screen changes (solid colour, lights off...): no need to redraw 30 times a second.
        bool Animating
        {
            get
            {
                if (Moves(Renderer.Kind) || (FanEffect.HasValue && Moves(FanEffect.Value))) return true;
                return FanTesting || Aligning || DateTime.UtcNow < identifyUntil || !DetectionDone || !pendingResizes.IsEmpty;
            }
        }

        void SleepFrame(DateTime frameStart)
        {
            // Still effects tick 5 times a second, so changes from the window still show within 0.2 s.
            int frameMs = Animating ? 1000 / Math.Max(5, Fps) : 200;
            int ms = frameMs - (int)(DateTime.UtcNow - frameStart).TotalMilliseconds;
            if (ms > 0) Thread.Sleep(ms);
        }

        void RenderPreviewOnly(double dt)
        {
            Rgb[] buf = new Rgb[previewFrame.Length];
            Renderer.Render(buf, dt);
            lock (previewFrame) Array.Copy(buf, previewFrame, buf.Length);
        }

        // ---------- zone layout ----------
        // Each zone (header) of a device has a role:
        //   Main - shows the main effect. A device's main zones match each other (default) or flow as one strip (FlowKeys).
        //   Fan  - a fan/hub header with its own effect (the Fan button); all fan headers in the rig match each other.
        //   Hub  - a fan header the hub's own button is in charge of: frozen at whatever it last showed.
        // RAM in "dark" mode is held black and takes no part in the effect.
        enum ZoneRole { Main, Fan, Hub }

        public HashSet<string> FlowKeys = new HashSet<string>();
        // null = every resizable (ARGB) header counts as a fan header.
        public HashSet<string> FanZoneKeys;
        public readonly EffectRenderer FanRenderer = new EffectRenderer();
        public EffectKind? FanEffect;       // null = fans follow the main effect
        public bool HubInCharge;
        public bool RamDark;
        Rgb[] fanFrame = new Rgb[0];

        // Each fan its own colour: fans on a header are chained one after another, so the header's LEDs are
        // split into one equal block per fan. FanCounts: zone key -> fans on that header (set in the LED count window).
        public Dictionary<string, int> FanCounts = new Dictionary<string, int>();

        public int FansOn(RgbDevice d, RgbZone z)
        {
            int f;
            return FanCounts.TryGetValue(ZoneKey(d, z), out f) ? Math.Max(1, f) : 1;
        }
        public Rgb[] FanColors = new Rgb[0];
        public static readonly Rgb[] TestColors = {
            new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 0, 255), new Rgb(255, 200, 0),
            new Rgb(0, 220, 255), new Rgb(255, 0, 200), new Rgb(255, 90, 0), new Rgb(255, 255, 255)
        };
        // Fan test: until then, fan 1 = red, fan 2 = green, fan 3 = blue... whatever else is set.
        public DateTime FanTestUntil = DateTime.MinValue;

        bool FanTesting
        {
            get { return DateTime.UtcNow < FanTestUntil; }
        }

        Rgb PerFanColor(int j, int n, int fans)
        {
            int fan = (int)((long)j * fans / Math.Max(1, n));
            if (FanTesting) return TestColors[fan % TestColors.Length];
            Rgb[] pal = FanColors;
            Rgb c = pal.Length == 0 ? new Rgb(255, 255, 255) : pal[fan % pal.Length];
            return EffectRenderer.Scale(c, Renderer.Brightness / 100.0);
        }

        // ---------- lining up fans ----------
        // Fans mounted at different angles start their LED ring at different spots, so the same effect looks
        // rotated from fan to fan. A twist shifts (and optionally reverses) each fan's ring so they all match.
        // FanTwists: "<zone key>#<fan index>" -> { offset, reversed (0/1) }.
        public Dictionary<string, int[]> FanTwists = new Dictionary<string, int[]>();
        // While the Line up fans window is open: each fan shows its start LED white and the next two blue.
        public volatile bool Aligning;

        public static string FanKey(RgbDevice d, RgbZone z, int fan)
        {
            return ZoneKey(d, z) + "#" + fan;
        }

        // Calls fn(zone, first LED of the zone, fans on it, LEDs per fan) for every ARGB header the app drives.
        void ForEachFanZone(RgbDevice d, Action<RgbZone, int, int, int> fn)
        {
            int start = 0;
            foreach (RgbZone z in d.Zones)
            {
                int n = (int)z.LedsCount;
                if (n > 0 && z.LedsMin != z.LedsMax && RoleOf(d, z) != ZoneRole.Hub)
                {
                    int fans = Math.Min(FansOn(d, z), n);
                    fn(z, start, fans, n / fans);
                }
                start += n;
            }
        }

        void FillAlignPattern(RgbDevice d, Rgb[] colors)
        {
            ForEachFanZone(d, delegate (RgbZone z, int start, int fans, int per)
            {
                for (int i = start; i < start + (int)z.LedsCount && i < colors.Length; i++) colors[i] = new Rgb(0, 0, 0);
                for (int k = 0; k < fans; k++)
                {
                    int b = start + k * per;
                    if (b < colors.Length) colors[b] = new Rgb(255, 255, 255);
                    for (int j = 1; j <= 2 && j < per && b + j < colors.Length; j++) colors[b + j] = new Rgb(0, 90, 255);
                }
            });
        }

        void ApplyTwists(RgbDevice d, Rgb[] colors)
        {
            Dictionary<string, int[]> twists = FanTwists;
            if (twists.Count == 0) return;
            ForEachFanZone(d, delegate (RgbZone z, int start, int fans, int per)
            {
                if (per < 2) return;
                for (int k = 0; k < fans; k++)
                {
                    int[] tw;
                    if (!twists.TryGetValue(FanKey(d, z, k), out tw) || (tw[0] % per == 0 && tw[1] == 0)) continue;
                    int b = start + k * per;
                    if (b + per > colors.Length) break;
                    Rgb[] ring = new Rgb[per];
                    Array.Copy(colors, b, ring, 0, per);
                    int off = ((tw[0] % per) + per) % per;
                    for (int i = 0; i < per; i++)
                    {
                        int p = tw[1] != 0 ? ((off - i) % per + per) % per : (off + i) % per;
                        colors[b + p] = ring[i];
                    }
                }
            });
        }

        public static string ZoneKey(RgbDevice d, RgbZone z)
        {
            return d.Key + "|" + z.Name;
        }

        public bool IsFanZone(RgbDevice d, RgbZone z)
        {
            HashSet<string> keys = FanZoneKeys;
            return keys != null ? keys.Contains(ZoneKey(d, z)) : z.LedsMin != z.LedsMax;
        }

        ZoneRole RoleOf(RgbDevice d, RgbZone z)
        {
            if (!IsFanZone(d, z)) return ZoneRole.Main;
            if (FanTesting) return ZoneRole.Fan;
            if (HubInCharge) return ZoneRole.Hub;
            return FanEffect.HasValue ? ZoneRole.Fan : ZoneRole.Main;
        }

        bool IsDarkRam(RgbDevice d)
        {
            return RamDark && d.TypeName == "RAM";
        }

        public bool MatchesZones(RgbDevice d)
        {
            if (FlowKeys.Contains(d.Key)) return false;
            int lit = 0;
            foreach (RgbZone z in d.Zones) if (z.LedsCount > 0 && RoleOf(d, z) == ZoneRole.Main) lit++;
            return lit >= 2;
        }

        // How many LEDs of the rig-wide main effect this device takes up.
        int SpanOf(RgbDevice d)
        {
            if (IsDarkRam(d)) return 0;
            if (d.Zones.Count == 0) return d.LedCount;
            int sum = 0, max = 0;
            foreach (RgbZone z in d.Zones)
            {
                if (RoleOf(d, z) != ZoneRole.Main) continue;
                sum += (int)z.LedsCount;
                max = Math.Max(max, (int)z.LedsCount);
            }
            return MatchesZones(d) ? max : sum;
        }

        int FanSpanOf(RgbDevice d)
        {
            int max = 0;
            if (FanTesting || FanEffect == EffectKind.PerFan) return 0; // no animated fan frame needed
            foreach (RgbZone z in d.Zones) if (RoleOf(d, z) == ZoneRole.Fan) max = Math.Max(max, (int)z.LedsCount);
            return max;
        }

        void RenderAndSend(double dt, DateTime now)
        {
            List<RgbDevice> list = Devices;
            int total = 0, fanSpan = 0;
            foreach (RgbDevice d in list)
            {
                if (!IsEnabled(d)) continue;
                total += SpanOf(d);
                fanSpan = Math.Max(fanSpan, FanSpanOf(d));
            }

            if (total == 0) RenderPreviewOnly(dt);
            else
            {
                if (frame.Length != total) frame = new Rgb[total];
                Renderer.Render(frame, dt);
                // Preview strip: sample the whole rig down to 64 cells.
                lock (previewFrame)
                    for (int i = 0; i < previewFrame.Length; i++)
                        previewFrame[i] = frame[(int)((long)i * total / previewFrame.Length)];
            }
            if (fanSpan > 0)
            {
                // Fans share the profile's colours, brightness and speed; only the effect differs.
                FanRenderer.Kind = FanEffect.Value;
                FanRenderer.Color1 = Renderer.Color1;
                FanRenderer.Color2 = Renderer.Color2;
                FanRenderer.Brightness = Renderer.Brightness;
                FanRenderer.Speed = Renderer.Speed;
                if (fanFrame.Length != fanSpan) fanFrame = new Rgb[fanSpan];
                FanRenderer.Render(fanFrame, dt);
            }

            bool sendStatic = (now - lastStaticSend).TotalMilliseconds > 250;
            if (sendStatic) lastStaticSend = now;
            bool identifying = now < identifyUntil;

            int offset = 0;
            foreach (RgbDevice d in list)
            {
                if (!IsEnabled(d) || d.LedCount == 0) continue;
                Rgb[] colors = new Rgb[d.LedCount];
                int span = SpanOf(d);
                if (identifying && d.Index == identifyIndex)
                {
                    bool on = ((int)((identifyUntil - now).TotalMilliseconds / 200)) % 2 == 0;
                    for (int i = 0; i < colors.Length; i++) colors[i] = on ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
                }
                else if (IsDarkRam(d))
                {
                    // colors stay black
                }
                else if (d.Zones.Count == 0)
                {
                    Array.Copy(frame, offset, colors, 0, Math.Min(colors.Length, span));
                }
                else
                {
                    bool match = MatchesZones(d);
                    int led = 0, chain = 0;
                    foreach (RgbZone z in d.Zones)
                    {
                        int n = (int)z.LedsCount;
                        ZoneRole role = RoleOf(d, z);
                        for (int j = 0; j < n && led < colors.Length; j++, led++)
                        {
                            // The hub's headers keep exactly what they last showed (one write per frame, nothing changes there).
                            if (role == ZoneRole.Hub)
                                colors[led] = led < d.Colors.Count ? d.Colors[led] : new Rgb(0, 0, 0);
                            else if (role == ZoneRole.Fan && (FanTesting || FanEffect == EffectKind.PerFan))
                                colors[led] = PerFanColor(j, n, FansOn(d, z));
                            else if (role == ZoneRole.Fan)
                                colors[led] = fanFrame[(int)((2L * j + 1) * fanFrame.Length / (2L * n))];
                            else if (role == ZoneRole.Main)
                                // Matching zones each sample the device's whole stretch of the effect, scaled to their length.
                                colors[led] = match ? frame[offset + (int)((2L * j + 1) * span / (2L * n))] : frame[offset + chain + j];
                        }
                        if (role == ZoneRole.Main) chain += n;
                    }
                    if (Aligning) FillAlignPattern(d, colors);
                    ApplyTwists(d, colors);
                }
                offset += span;

                if (d.SupportsDirect)
                {
                    if (ShouldSend(d, colors, now)) client.UpdateLeds(d.Index, colors);
                }
                else if (sendStatic)
                {
                    SendStatic(d, Average(colors));
                }
            }
        }

        readonly Dictionary<int, Rgb[]> lastSent = new Dictionary<int, Rgb[]>();
        readonly Dictionary<int, DateTime> lastSentAt = new Dictionary<int, DateTime>();

        // Skips unchanged frames (static effects cost almost nothing) and caps slow SMBus devices
        // like RAM and motherboards, which make the whole PC stutter if flooded.
        bool ShouldSend(RgbDevice d, Rgb[] colors, DateTime now)
        {
            Rgb[] prev;
            DateTime at;
            lastSentAt.TryGetValue(d.Index, out at);
            double since = (now - at).TotalMilliseconds;
            bool slowBus = d.Location.StartsWith("I2C", StringComparison.OrdinalIgnoreCase);
            if (slowBus && since < 66) return false;
            // Unchanged frames are re-sent now and then in case a device drifted back to its own mode;
            // rarely on the SMBus (RAM), where every write is a small risk.
            double refresh = slowBus ? 15000 : 2000;
            if (lastSent.TryGetValue(d.Index, out prev) && prev.Length == colors.Length && since < refresh)
            {
                bool same = true;
                for (int i = 0; i < colors.Length && same; i++)
                    same = prev[i].R == colors[i].R && prev[i].G == colors[i].G && prev[i].B == colors[i].B;
                if (same) return false;
            }
            lastSent[d.Index] = colors;
            lastSentAt[d.Index] = now;
            return true;
        }

        // Devices with no per-LED control get the average colour through their built-in static mode.
        void SendStatic(RgbDevice d, Rgb c)
        {
            int mi = d.FindStaticMode();
            if (mi < 0) return;
            Rgb prev;
            if (lastStatic.TryGetValue(d.Index, out prev) &&
                Math.Abs(prev.R - c.R) + Math.Abs(prev.G - c.G) + Math.Abs(prev.B - c.B) < 6) return;
            lastStatic[d.Index] = c;
            RgbMode m = d.Modes[mi];
            m.ColorMode = RgbMode.COLOR_MODE_SPECIFIC;
            int n = (int)Math.Max(1, m.ColorsMin);
            if (m.Colors.Length > 0) n = m.Colors.Length;
            m.Colors = new Rgb[n];
            for (int i = 0; i < n; i++) m.Colors[i] = c;
            if ((m.Flags & RgbMode.FLAG_BRIGHTNESS) != 0) m.Brightness = m.BrightnessMax;
            client.UpdateMode(d.Index, mi, m);
        }

        static Rgb Average(Rgb[] c)
        {
            long r = 0, g = 0, b = 0;
            foreach (Rgb x in c) { r += x.R; g += x.G; b += x.B; }
            int n = Math.Max(1, c.Length);
            return new Rgb((int)(r / n), (int)(g / n), (int)(b / n));
        }
    }

    // %APPDATA%\RGBUnify\rgbunify.log - reconnects and reloads, for tracking down flicker reports.
    public static class Log
    {
        static readonly object sync = new object();

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"RGBUnify\rgbunify.log"); }
        }

        public static void Write(string msg)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + "\r\n");
                }
            }
            catch { }
        }
    }
}
