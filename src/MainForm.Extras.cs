using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RGBUnify
{
    // Things that run in the background whether or not the window is open:
    // lights-off toggle, hotkeys, sleep / lock, update check, game profiles.
    public partial class MainForm
    {
        // ---------- lights off (without touching the profile) ----------

        bool lightsOff;
        bool autoOff; // switched off by sleep / lock, so it comes back by itself

        void SetLightsOff(bool off, bool automatic)
        {
            lightsOff = off;
            autoOff = off && automatic;
            if (off)
            {
                engine.Renderer.Kind = EffectKind.Off;
                engine.FanEffect = EffectKind.Off;
            }
            else ApplyProfileToEngine();
            if (trayLightsItem != null) trayLightsItem.Checked = off;
        }

        // Any lighting change in the app turns the lights back on.
        void LightsBackOn()
        {
            if (lightsOff) SetLightsOff(false, false);
        }

        ToolStripMenuItem trayLightsItem;

        // ---------- hotkeys ----------

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        static readonly uint[] HotkeyKeys = { (uint)Keys.D1, (uint)Keys.D2, (uint)Keys.D3, (uint)Keys.L, (uint)Keys.F };
        public const string HotkeyHelp = "Ctrl+Alt+1 / 2 / 3: switch profile   •   Ctrl+Alt+L: lights on / off   •   Ctrl+Alt+F: next fan effect";
        bool hotkeysOn;

        void UpdateHotkeys()
        {
            if (!IsHandleCreated) return;
            for (int i = 0; i < HotkeyKeys.Length; i++) UnregisterHotKey(Handle, 100 + i);
            hotkeysOn = false;
            if (!settings.Hotkeys) return;
            int failed = 0;
            for (int i = 0; i < HotkeyKeys.Length; i++)
                if (!RegisterHotKey(Handle, 100 + i, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, HotkeyKeys[i])) failed++;
            hotkeysOn = true;
            if (failed > 0) RGBUnify.Log.Write(failed + " hotkey(s) are already used by another app");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && hotkeysOn)
            {
                int id = m.WParam.ToInt32() - 100;
                if (id >= 0 && id < Settings.ProfileCount) { LightsBackOn(); SwitchProfile(id); }
                else if (id == 3) SetLightsOff(!lightsOff, false);
                else if (id == 4) { LightsBackOn(); CycleFanEffect(); }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateHotkeys();
        }

        // ---------- sleep / lock ----------

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                if (settings.OffOnSleep && !lightsOff)
                {
                    SetLightsOff(true, true);
                    Thread.Sleep(600); // give the engine a few frames to send black before Windows sleeps
                }
            }
            else if (e.Mode == PowerModes.Resume)
            {
                // The clock jumped while asleep: don't mistake that for a frozen engine.
                engine.NotifyResume();
                if (autoOff) BeginInvoke(new Action(delegate { SetLightsOff(false, false); }));
            }
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock && settings.OffOnLock && !lightsOff)
                BeginInvoke(new Action(delegate { SetLightsOff(true, true); }));
            else if (e.Reason == SessionSwitchReason.SessionUnlock && autoOff)
                BeginInvoke(new Action(delegate { SetLightsOff(false, false); }));
        }

        // ---------- update check ----------

        System.Threading.Timer updateTimer;
        public string UpdateTag, UpdateUrl;

        void CheckForUpdates(object state)
        {
            if (!settings.CheckUpdates) return;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; // TLS 1.2
                string json;
                using (System.Net.WebClient wc = new System.Net.WebClient())
                {
                    wc.Headers.Add("User-Agent", "RGBUnify");
                    json = wc.DownloadString("https://api.github.com/repos/Banana16x/RGBUnify/releases/latest");
                }
                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                Match url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/tag/[^\"]+)\"");
                if (!tag.Success) return;
                Version latest, mine = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (!Version.TryParse(tag.Groups[1].Value, out latest)) return;
                if (latest <= new Version(mine.Major, mine.Minor, mine.Build)) return;
                UpdateTag = "v" + tag.Groups[1].Value;
                UpdateUrl = url.Success ? url.Groups[1].Value : "https://github.com/Banana16x/RGBUnify/releases/latest";
                RGBUnify.Log.Write("Update available: " + UpdateTag);
                BeginInvoke(new Action(ShowUpdate));
            }
            catch { } // offline or GitHub unreachable: try again tomorrow
        }

        void ShowUpdate()
        {
            if (updateLink != null && !updateLink.IsDisposed)
            {
                updateLink.Text = "⬆ " + UpdateTag + " available";
                updateLink.Visible = true;
            }
            // One tray bubble per new version.
            if (settings.UpdateNotified != UpdateTag)
            {
                settings.UpdateNotified = UpdateTag;
                settings.Save();
                tray.ShowBalloonTip(6000, "RGB Unify " + UpdateTag + " is available", "Click Update in the bottom bar of the window to download it.", ToolTipIcon.Info);
            }
        }

        LinkLabel updateLink;

        // ---------- game / app profiles ----------
        // While a listed program runs, its profile is used; when it closes, the profile from before comes back.

        System.Windows.Forms.Timer appTimer;
        string activeApp;
        int profileBeforeApp = -1;

        void CheckApps(object sender, EventArgs e)
        {
            if (settings.AppProfiles.Count == 0)
            {
                activeApp = null;
                return;
            }
            string running = null;
            int profile = -1;
            foreach (KeyValuePair<string, int> kv in settings.AppProfiles)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(kv.Key);
                Process[] ps = Process.GetProcessesByName(name);
                bool found = ps.Length > 0;
                foreach (Process p in ps) p.Dispose();
                if (found) { running = kv.Key; profile = kv.Value; break; }
            }
            if (running != null && running != activeApp)
            {
                if (activeApp == null) profileBeforeApp = settings.ActiveProfile;
                activeApp = running;
                RGBUnify.Log.Write("Game profile: " + running + " started, using " + settings.Profiles[profile].Name);
                LightsBackOn();
                SwitchProfile(profile);
            }
            else if (running == null && activeApp != null)
            {
                RGBUnify.Log.Write("Game profile: " + activeApp + " closed, back to " + settings.Profiles[Math.Max(0, profileBeforeApp)].Name);
                activeApp = null;
                if (profileBeforeApp >= 0) SwitchProfile(profileBeforeApp);
                profileBeforeApp = -1;
            }
        }

        // ---------- start / stop ----------

        void StartExtras()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            UpdateHotkeys();
            updateTimer = new System.Threading.Timer(CheckForUpdates, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24));
            appTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            appTimer.Tick += CheckApps;
            appTimer.Start();
        }

        void StopExtras()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (updateTimer != null) updateTimer.Dispose();
            if (appTimer != null) appTimer.Stop();
            if (IsHandleCreated) for (int i = 0; i < HotkeyKeys.Length; i++) UnregisterHotKey(Handle, 100 + i);
        }
    }
}
