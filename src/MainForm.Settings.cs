using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace RGBUnify
{
    // Settings tab: general + themes + support on the left, advanced (smoothness, RAM, engine, logs) on the right.
    public partial class MainForm
    {
        Button settingsDlButton;
        Label engineInfo;

        Control BuildSettingsPage()
        {
            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            BuildGeneralCard(grid);
            BuildAdvancedCard(grid);
            return grid;
        }

        // ---------- left: general, look, support ----------

        void BuildGeneralCard(TableLayoutPanel grid)
        {
            FlowLayoutPanel body = MakeCard(grid, "Settings", null);

            body.Controls.Add(Section("Startup", "Start with Windows: RGB Unify starts by itself when you sign in,\nso your lighting is set after every reboot.\n\nThe tray is the small icon area next to the clock."));
            CheckBox startup = Check("Start with Windows (keeps your lighting after every reboot)", Settings.StartupEnabled());
            startup.CheckedChanged += delegate { Settings.SetStartup(startup.Checked); };
            CheckBox minimized = Check("When starting with Windows, go straight to the tray", settings.StartMinimized);
            minimized.CheckedChanged += delegate { settings.StartMinimized = minimized.Checked; settings.Save(); };
            CheckBox toTray = Check("Closing the window keeps lighting running in the tray", settings.CloseToTray);
            toTray.CheckedChanged += delegate { settings.CloseToTray = toTray.Checked; settings.Save(); };
            body.Controls.Add(startup); body.Controls.Add(minimized); body.Controls.Add(toTray);

            body.Controls.Add(Section("Theme", "Changes the colours of this window (not your RGB lights).\nClick a theme to switch instantly."));
            FlowLayoutPanel tiles = WrapRow();
            foreach (Theme th in Theme.All)
            {
                Theme t = th;
                ThemeTile tile = new ThemeTile(t, t == Theme.Current) { Size = new Size(150, 92), Margin = new Padding(0, 0, 12, 12) };
                tile.Click += delegate { if (t != Theme.Current) BeginInvoke(new Action(delegate { ApplyTheme(t); })); };
                tiles.Controls.Add(tile);
            }
            body.Controls.Add(tiles);

            body.Controls.Add(Section("Support RGB Unify", null));
            Label thanks = Note("RGB Unify is free, with no ads and no account. If it saved you from installing five different RGB apps, " +
                                "you can say thanks with a coffee. Totally optional: everything keeps working either way.", Text2);
            thanks.Tag = "stretch";
            body.Controls.Add(thanks);
            Panel coffeeRow = Row(70);
            // Buy Me a Coffee's own yellow and black so it's recognisable.
            Button coffee = new Button
            {
                Text = "☕  Buy me a coffee", Location = new Point(0, 6), Size = new Size(280, 56), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(255, 221, 0), ForeColor = Color.Black, Font = new Font("Segoe UI Semibold", 13f)
            };
            coffee.FlatAppearance.BorderSize = 0;
            coffee.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 232, 80);
            coffee.Click += delegate { OpenInBrowser(CoffeeUrl); };
            coffeeRow.Controls.Add(coffee);
            body.Controls.Add(coffeeRow);

            body.Controls.Add(Section("About", null));
            Label about = Note("RGB Unify " + Application.ProductVersion.Replace(".0.0", ".0") + " syncs every RGB device in your PC to one effect. " +
                               "Hardware support is powered by OpenRGB (openrgb.org, GPL-2.0), which runs hidden in the background.", Text2);
            about.Tag = "stretch";
            body.Controls.Add(about);
        }

        // Your Buy Me a Coffee page.
        const string CoffeeUrl = "https://buymeacoffee.com/banana16x";

        // The app runs as admin; opening the URL through Explorer starts the browser as the normal user instead.
        static void OpenInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start("explorer.exe", "\"" + url + "\""); }
            catch { try { Process.Start(url); } catch { } }
        }

        // ---------- right: advanced ----------

        CheckBox ramChk;
        RadioButton ramSync, ramOff;
        Label ramStatus;
        bool ramToggleBusy;

        void BuildAdvancedCard(TableLayoutPanel grid)
        {
            FlowLayoutPanel body = MakeCard(grid, "Advanced", null);

            body.Controls.Add(Section("Smoothness", "How many times per second the lights update.\nHigher looks smoother but uses a little more CPU.\nBalanced is right for almost everyone."));
            Panel fpsRow = Row(40);
            ComboBox fps = new ComboBox { Location = new Point(0, 4), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Card2, ForeColor = Text1, FlatStyle = FlatStyle.Flat };
            int[] fpsVals = { 20, 30, 60 };
            fps.Items.AddRange(new object[] { "Light (20 fps, lowest CPU)", "Balanced (30 fps)", "Ultra smooth (60 fps)" });
            fps.SelectedIndex = Math.Max(0, Array.IndexOf(fpsVals, settings.Fps));
            fps.SelectedIndexChanged += delegate { settings.Fps = fpsVals[fps.SelectedIndex]; engine.Fps = settings.Fps; settings.Save(); };
            fpsRow.Controls.Add(fps);
            body.Controls.Add(fpsRow);

            body.Controls.Add(Section("RAM lighting (experimental)",
                "Turn this on to control the lights on your RAM sticks.\n\n" +
                "• Sync with my effect: RAM joins the effect like everything else.\n" +
                "• Off (dark): RAM lights are switched off (black).\n\n" +
                "RAM is scanned once each time Windows starts. If it isn't found,\n" +
                "use Restart (not Shut down) and it's picked up again."));
            ramChk = Check("Control RAM lighting", settings.RamEnabled);
            ramChk.CheckedChanged += delegate { OnRamToggled(); };
            body.Controls.Add(ramChk);
            FlowLayoutPanel ramModes = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(20, 0, 0, 0) };
            ramSync = new RadioButton { Text = "Sync with my effect", AutoSize = true, ForeColor = Text1, Checked = !settings.RamDark, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 24, 2) };
            ramOff = new RadioButton { Text = "Off (dark)", AutoSize = true, ForeColor = Text1, Checked = settings.RamDark, Cursor = Cursors.Hand };
            ramSync.CheckedChanged += delegate { settings.RamDark = ramOff.Checked; engine.RamDark = ramOff.Checked; settings.Save(); };
            ramModes.Controls.Add(ramSync);
            ramModes.Controls.Add(ramOff);
            body.Controls.Add(ramModes);
            Label ramWarn = Note("On some DDR5 kits (XPG Lancer among them) repeated RAM scans have corrupted the memory's SPD chip " +
                                 "and stopped PCs from booting. To keep the risk low, RGB Unify scans RAM at most once per Windows start.", Warn);
            ramWarn.Tag = "stretch";
            body.Controls.Add(ramWarn);
            ramStatus = Note("", Text2);
            ramStatus.Tag = "stretch";
            body.Controls.Add(ramStatus);
            UpdateRamControls();

            body.Controls.Add(Section("RGB engine", "The engine (OpenRGB) does the actual talking to your hardware.\nIt runs hidden. Use Download / repair if lights stop responding\nafter an update."));
            engineInfo = Note("", Text2);
            engineInfo.Tag = "stretch";
            body.Controls.Add(engineInfo);
            Panel engRow = Row(44);
            settingsDlButton = MakeButton("Download / repair engine", 0, 4, 220, Card2);
            settingsDlButton.Click += delegate { engine.BeginInstallEngine(); };
            engRow.Controls.Add(settingsDlButton);
            Button folder = MakeButton("Open app folder", 230, 4, 160, Card2);
            folder.Click += delegate { Process.Start(Engine.AppDir); };
            engRow.Controls.Add(folder);
            body.Controls.Add(engRow);

            body.Controls.Add(Section("Troubleshooting", "If something flickers or a device goes missing, open the log\nand send it to whoever is helping you. It lists every\nreconnect and rescan with the time."));
            Panel tRow = Row(44);
            Button log = MakeButton("Open log", 0, 4, 140, Card2);
            log.Click += delegate
            {
                if (File.Exists(RGBUnify.Log.FilePath)) Process.Start("notepad.exe", "\"" + RGBUnify.Log.FilePath + "\"");
                else MessageBox.Show(this, "No log yet: nothing has gone wrong.", "Log", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            tRow.Controls.Add(log);
            Button resetProfiles = MakeButton("Reset all 3 profiles", 150, 4, 190, Card2);
            resetProfiles.Click += delegate { ResetProfiles(); };
            tRow.Controls.Add(resetProfiles);
            body.Controls.Add(tRow);
        }

        void UpdateRamControls()
        {
            ramSync.Enabled = ramOff.Enabled = settings.RamEnabled;
        }

        void ResetProfiles()
        {
            if (MessageBox.Show(this, "Reset all 3 profiles (names, effects, colours, fans) to the defaults?", "Reset profiles",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Settings fresh = new Settings();
            for (int i = 0; i < Settings.ProfileCount; i++) settings.Profiles[i] = fresh.Profiles[i];
            int active = settings.ActiveProfile;
            settings.ActiveProfile = (active + 1) % Settings.ProfileCount; // force SwitchProfile to refresh everything
            SwitchProfile(active);
        }

        void OnRamToggled()
        {
            if (ramToggleBusy) return;
            if (ramChk.Checked)
            {
                DialogResult ok = MessageBox.Show(this,
                    "RAM lighting control is experimental.\n\n" +
                    "OpenRGB, the engine RGB Unify uses, has open reports of DDR5 RAM (including XPG Lancer) whose SPD chip " +
                    "was corrupted when the RAM was scanned repeatedly. Affected PCs failed to boot until the memory was repaired or replaced.\n\n" +
                    "RGB Unify only scans RAM once per Windows start to avoid the known trigger, but it can't rule the risk out. " +
                    "Other users with the same kit on MSI boards have reported it working fine.\n\n" +
                    "Turn on RAM lighting?",
                    "RAM lighting", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes)
                {
                    ramToggleBusy = true; ramChk.Checked = false; ramToggleBusy = false;
                    return;
                }
            }
            settings.RamEnabled = ramChk.Checked;
            engine.RamEnabled = ramChk.Checked;
            settings.Save();
            if (ramChk.Checked && Engine.PawnIOInstalled() && !Engine.SameBoot(settings.RamDetectBoot, Engine.CurrentBoot) &&
                MessageBox.Show(this, "Scan for RAM now? Your lights will pause for a few seconds.", "RAM lighting", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                engine.Rescan();
            devicesDirty = true;
            UpdateRamControls();
            UpdateRamStatus();
        }

        void UpdateRamStatus()
        {
            string s;
            if (!settings.RamEnabled) s = "RAM is not scanned or controlled.";
            else if (!Engine.PawnIOInstalled()) s = "Needs the PawnIO driver (see the banner on the Home tab) before RAM can be found.";
            else if (engine.RamDetectionThisRun) s = "RAM was scanned this session. Found RAM is synced with everything else.";
            else if (engine.RamWaitingForReboot) s = "RAM was already scanned this Windows session. Restart your PC (Restart, not Shut down) to scan it again.";
            else s = "RAM will be scanned the next time the engine starts (at sign-in, or click Rescan hardware).";
            if (ramStatus.Text != s) ramStatus.Text = s;
        }

        void BannerAction()
        {
            if (Engine.FindEngineCached() == null) engine.BeginInstallEngine();
            else engine.BeginInstallPawnIO();
        }

        // One banner (top of the Lighting card) walks through setup: engine download, then the PawnIO driver.
        void RefreshBanner(string exe)
        {
            bool needPawn = exe != null && !Engine.PawnIOInstalled();
            string text = null, button = null;
            if (engine.SetupBusy)
            {
                text = engine.SetupTask + "... " + (engine.DownloadPercent >= 0 ? engine.DownloadPercent + "%" : "") +
                       "\nThis only happens once. Your lights will start as soon as it's done.";
            }
            else if (exe == null)
            {
                text = (engine.SetupError ?? "The RGB engine isn't installed yet.") + "\nRGB Unify needs it (OpenRGB, ~22 MB) to talk to your hardware.";
                button = "Try again";
            }
            else if (needPawn)
            {
                text = "Install the PawnIO driver so motherboard, graphics card and RAM lights can be found.\n" +
                       (engine.SetupError ?? "It's free, signed, and the official driver OpenRGB uses. Takes a few seconds.");
                button = "Install PawnIO";
            }
            engineBanner.Visible = text != null;
            if (text == null) return;
            if (bannerText.Text != text) bannerText.Text = text;
            dlButton.Visible = button != null;
            if (button != null) dlButton.Text = button;
            dlProgress.Visible = engine.SetupBusy;
            if (engine.SetupBusy) dlProgress.Value = Math.Max(0, Math.Min(100, engine.DownloadPercent));
        }
    }

    // Theme preview: a mini window in the theme's colours.
    public class ThemeTile : Control
    {
        readonly Theme theme;
        readonly bool selected;
        bool hover;

        public ThemeTile(Theme t, bool isSelected)
        {
            theme = t;
            selected = isSelected;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 9.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Current.Card);
            Rectangle r = new Rectangle(2, 2, Width - 5, Height - 5);
            using (SolidBrush b = new SolidBrush(theme.Bg)) g.FillRectangle(b, r);
            using (SolidBrush b = new SolidBrush(theme.Card)) g.FillRectangle(b, r.X + 10, r.Y + 10, r.Width - 20, 30);
            using (SolidBrush b = new SolidBrush(theme.Accent)) g.FillRectangle(b, r.X + 16, r.Y + 18, 40, 14);
            using (SolidBrush b = new SolidBrush(theme.Card2)) g.FillRectangle(b, r.X + 62, r.Y + 18, 30, 14);
            TextRenderer.DrawText(g, theme.Name, Font, new Rectangle(r.X + 10, r.Y + 46, r.Width - 20, 30), theme.Text1, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Color border = selected ? theme.Accent : hover ? Theme.Current.Text2 : Theme.Current.Card2;
            using (Pen p = new Pen(border, selected ? 4 : 2)) g.DrawRectangle(p, r);
            if (selected) TextRenderer.DrawText(g, "✓", Font, new Rectangle(r.Right - 30, r.Y + 48, 24, 24), theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
