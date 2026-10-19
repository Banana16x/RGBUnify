using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RGBUnify
{
    // The main window. Split over several files:
    //   MainForm.cs          window, header (tabs + profiles), status bar, themes, shared helpers, tray, timer
    //   MainForm.Home.cs     Home tab: Lighting, Fans and Devices side by side
    //   MainForm.Clean.cs    Clean Up tab
    //   MainForm.Settings.cs Settings tab: general, themes, support, advanced (RAM, engine)
    public partial class MainForm : Form
    {
        static Color Bg { get { return Theme.Current.Bg; } }
        static Color Card { get { return Theme.Current.Card; } }
        static Color Card2 { get { return Theme.Current.Card2; } }
        static Color Text1 { get { return Theme.Current.Text1; } }
        static Color Text2 { get { return Theme.Current.Text2; } }
        static Color Accent { get { return Theme.Current.Accent; } }
        static Color Warn { get { return Theme.Current.Warn; } }
        static Color Good { get { return Theme.Current.Good; } }
        static Color Danger { get { return Theme.Current.Danger; } }

        readonly Settings settings;
        Profile P { get { return settings.Current; } }
        readonly Engine engine;
        NotifyIcon tray;
        bool allowShow;
        bool reallyExit;
        bool shownOnce;
        volatile bool devicesDirty = true;

        Panel content;
        readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        readonly Dictionary<string, Button> tabButtons = new Dictionary<string, Button>();
        string currentPage = "Home";
        Label statusLabel;
        System.Windows.Forms.Timer uiTimer;
        int slowTick;

        public MainForm(bool startHidden)
        {
            settings = Settings.Load();
            Theme.Current = Theme.Find(settings.Theme);
            engine = new Engine();
            ApplyProfileToEngine();
            engine.Fps = settings.Fps;
            engine.FanZoneKeys = settings.FanZones == null ? null : new HashSet<string>(settings.FanZones);
            engine.HubInCharge = settings.HubInCharge;
            engine.FanCounts = new Dictionary<string, int>(settings.ZoneFans);
            engine.FanTwists = new Dictionary<string, int[]>(settings.FanTwists);
            engine.RamDark = settings.RamDark;
            foreach (string k in settings.DisabledDevices) engine.DisabledKeys.Add(k);
            engine.FlowKeys = new HashSet<string>(settings.FlowDevices);
            engine.RamEnabled = settings.RamEnabled;
            engine.RamDetectBoot = settings.RamDetectBoot;
            // Saved straight away: this is what stops a second RAM scan if the app restarts in the same session.
            engine.RamDetectBootChanged += delegate { settings.RamDetectBoot = engine.RamDetectBoot; settings.Save(); };
            engine.DevicesChanged += delegate { devicesDirty = true; };
            allowShow = !startHidden;

            Text = "RGB Unify";
            Icon = AppIcon.Create(32);
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(1360, 820);
            MinimumSize = new Size(1280, 760);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            BuildUi();
            BuildTray();

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 33;
            uiTimer.Tick += OnUiTick;
            uiTimer.Start();

            engine.Start();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FirstShow();
        }

        // Opens full screen the first time (unless the user last left it smaller).
        void FirstShow()
        {
            if (shownOnce) return;
            shownOnce = true;
            if (settings.Maximized) WindowState = FormWindowState.Maximized;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            RememberWindowState();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RememberWindowState();
        }

        void RememberWindowState()
        {
            if (!shownOnce || WindowState == FormWindowState.Minimized || settings == null) return;
            bool max = WindowState == FormWindowState.Maximized;
            if (max != settings.Maximized) { settings.Maximized = max; SaveSoon(); }
        }

        public void ShowMain()
        {
            allowShow = true;
            Show();
            FirstShow();
            if (WindowState == FormWindowState.Minimized) WindowState = settings.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                tray.ShowBalloonTip(2500, "RGB Unify is still running", "Your lighting stays synced. Right-click the tray icon to exit.", ToolTipIcon.Info);
                return;
            }
            uiTimer.Stop();
            settings.Save();
            engine.Stop();
            engine.KillEngine();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        public void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        // ---------- layout ----------

        // Builds every control from scratch; also used to switch themes.
        void BuildUi()
        {
            SuspendLayout();
            BackColor = Bg;
            ForeColor = Text1;

            Panel header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Bg };
            header.Paint += PaintHeader;
            string[] tabs = { "Home", "Clean Up", "Settings" };
            for (int i = 0; i < tabs.Length; i++)
            {
                string n = tabs[i];
                Button b = new Button
                {
                    Text = n, Size = new Size(118, 40), Location = new Point(250 + i * 124, 16), FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI Semibold", 10.5f), Cursor = Cursors.Hand, UseMnemonic = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += delegate { SelectPage(n); };
                header.Controls.Add(b);
                tabButtons[n] = b;
            }
            Panel profileBar = new Panel { Dock = DockStyle.Right, Width = 500, BackColor = Bg };
            BuildProfileBar(profileBar);
            header.Controls.Add(profileBar);

            Panel status = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Card };
            statusLabel = new Label { Dock = DockStyle.Fill, ForeColor = Text2, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(20, 0, 0, 0), AutoEllipsis = true };
            LinkLabel coffee = new LinkLabel
            {
                Text = "☕ Buy me a coffee", Dock = DockStyle.Right, Width = 170, TextAlign = ContentAlignment.MiddleCenter,
                LinkColor = Warn, ActiveLinkColor = Accent, LinkBehavior = LinkBehavior.HoverUnderline
            };
            coffee.LinkClicked += delegate { OpenInBrowser(CoffeeUrl); };
            Label version = new Label
            {
                Text = "v" + Application.ProductVersion.Replace(".0.0", ".0"), Dock = DockStyle.Right, Width = 70,
                TextAlign = ContentAlignment.MiddleRight, ForeColor = Text2
            };
            status.Controls.Add(statusLabel);
            status.Controls.Add(version);
            status.Controls.Add(coffee);

            content = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(16, 4, 16, 16) };
            pages["Home"] = BuildHomePage();
            pages["Clean Up"] = BuildCleanPage();
            pages["Settings"] = BuildSettingsPage();
            foreach (Control p in pages.Values) { p.Dock = DockStyle.Fill; p.Visible = false; content.Controls.Add(p); }

            Controls.Add(content);
            Controls.Add(status);
            Controls.Add(header);
            SelectPage(currentPage);
            UpdateEffectButtons();
            UpdateFanButton();
            UpdateProfileButtons();
            ResumeLayout(true);
        }

        void ApplyTheme(Theme t)
        {
            Theme.Current = t;
            settings.Theme = t.Name;
            settings.Save();
            // Throw away every control and rebuild in the new colours.
            List<Control> old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (Control c in old) c.Dispose();
            pages.Clear();
            cardBodies.Clear();
            tabButtons.Clear();
            effectButtons.Clear();
            if (helpTips != null) { helpTips.Dispose(); helpTips = null; }
            fanZoneSignature = null;
            deviceSignature = null;
            gpuSignature = null;
            BuildUi();
            devicesDirty = true;
            if (statusLabel != null) statusLabel.Text = "";
        }

        void PaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (Icon ic = AppIcon.Create(32)) g.DrawIcon(ic, new Rectangle(24, 20, 32, 32));
            using (Font f = new Font("Segoe UI Semibold", 18f))
            {
                RectangleF r = new RectangleF(64, 16, 170, 40);
                using (LinearGradientBrush br = new LinearGradientBrush(r, Color.FromArgb(255, 90, 140), Color.FromArgb(80, 170, 255), 0f))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new[] { Color.FromArgb(255, 95, 130), Color.FromArgb(170, 110, 255), Color.FromArgb(70, 180, 255) };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    br.InterpolationColors = cb;
                    g.DrawString("RGB Unify", f, br, 64, 17);
                }
            }
        }

        void SelectPage(string name)
        {
            currentPage = name;
            foreach (KeyValuePair<string, Control> kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (KeyValuePair<string, Button> kv in tabButtons)
            {
                bool sel = kv.Key == name;
                kv.Value.BackColor = sel ? Accent : Bg;
                kv.Value.ForeColor = sel ? Color.White : Text2;
                kv.Value.FlatAppearance.MouseOverBackColor = sel ? Accent : Card;
            }
            if (name == "Clean Up" && conflicts.Count == 0 && conflictList.Items.Count == 0) RunScan();
        }

        // ---------- building blocks ----------

        // A rounded-feel card with a title, filling its cell; the body stacks rows top to bottom and scrolls.
        FlowLayoutPanel MakeCard(Control parent, string title, string help)
        {
            Panel card = new Panel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(20, 14, 8, 12), Margin = new Padding(8) };
            FlowLayoutPanel body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Card
            };
            body.Resize += delegate { FitRows(body); };
            DarkScroll(body);
            cardBodies.Add(body);
            card.Controls.Add(body);
            Panel head = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Card };
            Label t = new Label { Text = title, AutoSize = true, Location = new Point(0, 2), Font = new Font("Segoe UI Semibold", 15f), ForeColor = Text1 };
            head.Controls.Add(t);
            if (help != null)
            {
                int x = TextRenderer.MeasureText(title, t.Font).Width + 4;
                AddHelp(head, x, 9, help);
            }
            card.Controls.Add(head);
            parent.Controls.Add(card);
            return body;
        }

        // Stretches rows marked "stretch" to the card width (and caps wrapping rows at it).
        static void FitRows(FlowLayoutPanel body)
        {
            int w = body.ClientSize.Width - 14;
            if (w < 50) return;
            body.SuspendLayout();
            foreach (Control c in body.Controls)
            {
                if (!"stretch".Equals(c.Tag)) continue;
                Label lab = c as Label;
                if (lab != null && lab.AutoSize) { lab.MaximumSize = new Size(w, 0); continue; }
                FlowLayoutPanel wrap = c as FlowLayoutPanel;
                if (wrap != null && wrap.AutoSize)
                {
                    wrap.MaximumSize = new Size(w, 0);
                    wrap.MinimumSize = new Size(w, 0);
                }
                else c.Width = w;
            }
            body.ResumeLayout(true);
            TidyScroll(body);
        }

        readonly List<FlowLayoutPanel> cardBodies = new List<FlowLayoutPanel>();

        // FlowLayoutPanel keeps a stale scrollbar after its rows get shorter; drop it when everything fits.
        static void TidyScroll(FlowLayoutPanel body)
        {
            if (body.IsDisposed || !body.VerticalScroll.Visible) return;
            int bottom = 0;
            foreach (Control c in body.Controls) if (c.Visible) bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            if (bottom - body.AutoScrollPosition.Y <= body.ClientSize.Height)
            {
                body.AutoScroll = false;
                body.AutoScroll = true;
            }
        }

        // Section title inside a card, with an optional "?".
        Control Section(string text, string help)
        {
            Panel p = new Panel { Height = 40, Width = 300, Tag = "stretch", Margin = new Padding(0, 6, 0, 0), BackColor = Color.Transparent };
            Label l = new Label { Text = text, AutoSize = true, Location = new Point(0, 12), Font = new Font("Segoe UI Semibold", 11.5f), ForeColor = Text1 };
            p.Controls.Add(l);
            if (help != null) AddHelp(p, TextRenderer.MeasureText(text, l.Font).Width + 4, 15, help);
            return p;
        }

        // A fixed-height row whose children are laid out by hand (and may anchor right to stretch).
        static Panel Row(int height)
        {
            return new Panel { Height = height, Width = 300, Tag = "stretch", Margin = new Padding(0, 2, 0, 2), BackColor = Color.Transparent };
        }

        // A row of controls that wraps onto more lines when the card is narrow.
        static FlowLayoutPanel WrapRow()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Tag = "stretch", Margin = new Padding(0, 2, 0, 2), BackColor = Color.Transparent
            };
        }

        static Label Note(string text, Color color)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = color, Margin = new Padding(0, 4, 0, 4) };
        }

        static Label Small(string text, int x, int y, int w)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, 22), ForeColor = Text2 };
        }

        static Button MakeButton(string text, int x, int y, int w, Color back)
        {
            Button b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, 36), FlatStyle = FlatStyle.Flat, BackColor = back,
                ForeColor = back == Accent || back == Danger ? Color.White : Text1, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 9.5f),
                UseMnemonic = false
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        static Button SwatchButton(int w, int h)
        {
            Button b = new Button { Size = new Size(w, h), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 8) };
            b.FlatAppearance.BorderColor = Card2;
            b.FlatAppearance.BorderSize = 2;
            return b;
        }

        static CheckBox Check(string text, bool on)
        {
            return new CheckBox { Text = text, AutoSize = true, Checked = on, ForeColor = Text1, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 0, 4) };
        }

        // Light or dark text, whichever reads better on c.
        static Color ReadableOn(Color c)
        {
            return c.R * 299 + c.G * 587 + c.B * 114 > 140000 ? Color.Black : Color.White;
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        // Native scrollbars in the theme's darkness (Windows 10 1809+; older versions just keep the default look).
        static void DarkScroll(Control c)
        {
            string style = Theme.Current.Name == "Light" ? "Explorer" : "DarkMode_Explorer";
            c.HandleCreated += delegate { try { SetWindowTheme(c.Handle, style, null); } catch { } };
            if (c.IsHandleCreated) try { SetWindowTheme(c.Handle, style, null); } catch { }
        }

        // ---------- help "?" dots ----------

        ToolTip helpTips;

        // A small round "?" that explains the thing next to it when hovered (or clicked).
        void AddHelp(Control parent, int x, int y, string text)
        {
            if (helpTips == null)
                helpTips = new ToolTip { InitialDelay = 80, AutoPopDelay = 30000, ReshowDelay = 50, ToolTipTitle = "How to use", ToolTipIcon = ToolTipIcon.Info };
            HelpDot dot = new HelpDot { Location = new Point(x, y), Size = new Size(20, 20) };
            helpTips.SetToolTip(dot, text);
            dot.Click += delegate { helpTips.Show(text, dot, 24, 0, 15000); };
            parent.Controls.Add(dot);
            dot.BringToFront();
        }

        // ---------- profiles ----------

        readonly Button[] profileButtons = new Button[Settings.ProfileCount];

        void BuildProfileBar(Panel bar)
        {
            for (int i = 0; i < Settings.ProfileCount; i++)
            {
                int idx = i;
                Button b = new Button
                {
                    Size = new Size(110, 40), Location = new Point(8 + i * 116, 16), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI Semibold", 9.5f), UseMnemonic = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += delegate { SwitchProfile(idx); };
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Rename...", null, delegate { RenameProfile(idx); });
                b.ContextMenuStrip = menu;
                bar.Controls.Add(b);
                profileButtons[i] = b;
            }
            Button rename = MakeButton("Rename", 8 + Settings.ProfileCount * 116, 16, 80, Card2);
            rename.Height = 40;
            rename.Click += delegate { RenameProfile(settings.ActiveProfile); };
            bar.Controls.Add(rename);
            AddHelp(bar, 8 + Settings.ProfileCount * 116 + 88, 26,
                "Profiles are 3 saved lighting setups. Click one to switch to it instantly.\n" +
                "Whatever you change (effect, colours, brightness, speed, fans)\n" +
                "is saved into the profile that's lit up.\n\n" +
                "Rename: click Rename, or right-click a profile.\n" +
                "You can also switch profiles from the tray icon.");
        }

        void UpdateProfileButtons()
        {
            for (int i = 0; i < Settings.ProfileCount; i++)
            {
                if (profileButtons[i] == null) return;
                bool sel = i == settings.ActiveProfile;
                profileButtons[i].Text = settings.Profiles[i].Name;
                profileButtons[i].BackColor = sel ? Accent : Card2;
                profileButtons[i].ForeColor = sel ? Color.White : Text1;
            }
            RebuildTrayProfiles();
        }

        void SwitchProfile(int idx)
        {
            if (idx == settings.ActiveProfile) return;
            settings.ActiveProfile = idx;
            settings.Save();
            ApplyProfileToEngine();
            // Refresh every control that shows profile values.
            UpdateEffectButtons();
            ApplyColors();
            brightSlider.Value = Math.Max(brightSlider.Minimum, Math.Min(brightSlider.Maximum, P.Brightness));
            speedSlider.Value = Math.Max(speedSlider.Minimum, Math.Min(speedSlider.Maximum, P.Speed));
            UpdateFanButton();
            UpdateProfileButtons();
        }

        void ApplyProfileToEngine()
        {
            engine.Renderer.Kind = P.Effect;
            engine.Renderer.Color1 = P.Color1;
            engine.Renderer.Color2 = P.Color2;
            engine.Renderer.Brightness = P.Brightness;
            engine.Renderer.Speed = P.Speed;
            engine.FanEffect = P.FanEffect;
            engine.FanColors = P.FanColors;
        }

        void RenameProfile(int idx)
        {
            string name = AskText("Rename profile", "New name for " + settings.Profiles[idx].Name + " (up to " + Settings.MaxProfileName + " letters):",
                settings.Profiles[idx].Name, Settings.MaxProfileName);
            if (string.IsNullOrEmpty(name)) return;
            settings.Profiles[idx].Name = name;
            settings.Save();
            UpdateProfileButtons();
        }

        string AskText(string title, string prompt, string value, int maxLen)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.BackColor = Bg; f.ForeColor = Text1; f.Font = Font;
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false;
                f.ClientSize = new Size(380, 130);
                f.Controls.Add(new Label { Text = prompt, Location = new Point(14, 12), Size = new Size(352, 22), ForeColor = Text2 });
                TextBox box = new TextBox { Text = value, MaxLength = maxLen, Location = new Point(14, 40), Width = 352, BackColor = Card2, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle };
                f.Controls.Add(box);
                Button ok = MakeButton("Save", 166, 80, 96, Accent);
                ok.DialogResult = DialogResult.OK;
                Button cancel = MakeButton("Cancel", 270, 80, 96, Card2);
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                box.SelectAll();
                return f.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }

        int saveCountdown;
        void SaveSoon() { saveCountdown = 30; }

        // ---------- tray & timers ----------

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open RGB Unify", null, delegate { ShowMain(); });
            menu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < Settings.ProfileCount; i++)
            {
                int idx = i;
                trayProfileItems[i] = new ToolStripMenuItem("", null, delegate { SwitchProfile(idx); });
                menu.Items.Add(trayProfileItems[i]);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Next fan effect", null, delegate { if (!settings.HubInCharge) CycleFanEffect(); });
            foreach (EffectKind k in new[] { EffectKind.RainbowWave, EffectKind.Spectrum, EffectKind.Static, EffectKind.Breathing, EffectKind.Music })
            {
                EffectKind kk = k;
                menu.Items.Add(EffectRenderer.DisplayName(k), null, delegate { SetEffect(kk); });
            }
            menu.Items.Add("Lights off", null, delegate { SetEffect(EffectKind.Off); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });

            tray = new NotifyIcon { Icon = AppIcon.Create(16), Text = "RGB Unify", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowMain(); };
            RebuildTrayProfiles();
        }

        readonly ToolStripMenuItem[] trayProfileItems = new ToolStripMenuItem[Settings.ProfileCount];

        void RebuildTrayProfiles()
        {
            for (int i = 0; i < Settings.ProfileCount; i++)
            {
                if (trayProfileItems[i] == null) return;
                trayProfileItems[i].Text = settings.Profiles[i].Name;
                trayProfileItems[i].Checked = i == settings.ActiveProfile;
            }
        }

        string lastDeviceStatus;

        void OnUiTick(object sender, EventArgs e)
        {
            if (Visible) preview.SetColors(engine.GetPreview());

            if (saveCountdown > 0 && --saveCountdown == 0) settings.Save();

            if (++slowTick % 15 != 0) return; // twice a second
            foreach (FlowLayoutPanel b in cardBodies) TidyScroll(b);
            string status = engine.Status ?? "Starting...";
            if (statusLabel.Text != "●  " + status) statusLabel.Text = "●  " + status;
            statusLabel.ForeColor = status.Contains("synced") ? Good : Text2;
            tray.Text = ("RGB Unify – " + status).Length > 63 ? "RGB Unify" : "RGB Unify – " + status;

            string exe = Engine.FindEngineCached();
            RefreshBanner(exe);
            settingsDlButton.Enabled = !engine.SetupBusy;
            if (slowTick % 60 == 0 || engineInfo.Text == "")
            {
                engineInfo.Text = exe == null ? "Not installed yet." : "Installed at: " + exe +
                    "\nPawnIO driver (RAM / motherboard / GPU lights): " + (Engine.PawnIOInstalled() ? "installed" : "not installed");
                UpdateDeviceHint(exe);
                UpdateRamStatus();
            }

            if (devicesDirty || status != lastDeviceStatus)
            {
                devicesDirty = false;
                lastDeviceStatus = status;
                RebuildDevices();
                RefreshFanZones();
                RefreshGpus();
            }
        }
    }

    // Round "?" badge for hover help.
    public class HelpDot : Control
    {
        bool hover;

        public HelpDot()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Help;
            Font = new Font("Segoe UI Semibold", 8.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            Theme t = Theme.Current;
            using (SolidBrush b = new SolidBrush(hover ? t.Accent : t.Card2)) g.FillEllipse(b, r);
            TextRenderer.DrawText(g, "?", Font, r, hover ? Color.White : t.Text1, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Row of glowing LED dots showing exactly what the rig is displaying.
    public class PreviewStrip : Control
    {
        Rgb[] colors = new Rgb[0];

        public PreviewStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Current.Bg;
        }

        public void SetColors(Rgb[] c)
        {
            if (c.Length == colors.Length)
            {
                bool same = true;
                for (int i = 0; i < c.Length && same; i++)
                    same = c[i].R == colors[i].R && c[i].G == colors[i].G && c[i].B == colors[i].B;
                if (same) return;
            }
            colors = c;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int n = colors.Length;
            if (n == 0) return;
            float cell = (Width - 20f) / n;
            float d = Math.Min(cell * 0.72f, Height - 24);
            float cy = Height / 2f;
            for (int i = 0; i < n; i++)
            {
                Color c = colors[i].ToColor();
                float cx = 10 + cell * (i + 0.5f);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(60, c)))
                    g.FillEllipse(glow, cx - d, cy - d, d * 2, d * 2);
                using (SolidBrush b = new SolidBrush(c))
                    g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            }
        }
    }
}
