using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RGBUnify
{
    // Home tab: Lighting | Fans | Devices side by side, each in its own scrolling card.
    public partial class MainForm
    {
        Control BuildHomePage()
        {
            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, BackColor = Bg, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            BuildLightingCard(grid);
            BuildFansCard(grid);
            BuildDevicesCard(grid);
            return grid;
        }

        // ---------- Lighting ----------

        PreviewStrip preview;
        readonly Dictionary<EffectKind, Button> effectButtons = new Dictionary<EffectKind, Button>();
        Button color1Btn, color2Btn;
        Label color1Lbl, color2Lbl;
        Panel engineBanner;
        Label bannerText;
        ProgressBar dlProgress;
        Button dlButton;
        TrackBar brightSlider, speedSlider;

        void BuildLightingCard(TableLayoutPanel grid)
        {
            FlowLayoutPanel body = MakeCard(grid, "Lighting", "Everything here applies to your whole PC at once.\nIt's saved into the profile that's lit up at the top.");

            engineBanner = new Panel { Height = 100, Width = 300, Tag = "stretch", BackColor = Card2, Visible = false, Margin = new Padding(0, 0, 0, 8) };
            bannerText = new Label { Location = new Point(14, 10), Size = new Size(280, 44), ForeColor = Text1, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            engineBanner.Controls.Add(bannerText);
            dlButton = MakeButton("Install", 14, 56, 180, Accent);
            dlButton.Click += delegate { BannerAction(); };
            engineBanner.Controls.Add(dlButton);
            dlProgress = new ProgressBar { Location = new Point(14, 68), Size = new Size(260, 12), Visible = false, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            engineBanner.Controls.Add(dlProgress);
            body.Controls.Add(engineBanner);

            body.Controls.Add(Section("Live preview", "Shows what your whole PC is showing right now, squeezed into one row."));
            preview = new PreviewStrip { Height = 56, Width = 300, Tag = "stretch", Margin = new Padding(0, 0, 0, 4), BackColor = Bg };
            body.Controls.Add(preview);

            body.Controls.Add(Section("Effect", "Pick an effect and every synced device shows it at once.\n\nYour fans follow it too. If you gave the fans their own look\n(Fans card), picking an effect here switches them back,\nunless you tick \"Keep fans' own look\"."));
            FlowLayoutPanel effects = WrapRow();
            EffectKind[] kinds = {
                EffectKind.RainbowWave, EffectKind.Spectrum, EffectKind.Static, EffectKind.Breathing, EffectKind.ColorWave,
                EffectKind.Comet, EffectKind.Starlight, EffectKind.Music, EffectKind.Off
            };
            foreach (EffectKind kind in kinds)
            {
                EffectKind k = kind;
                Button b = new Button
                {
                    Text = EffectRenderer.DisplayName(k), Size = new Size(150, 52), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 8),
                    BackColor = Card2, ForeColor = Text1, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 10f)
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += delegate { SetEffect(k); };
                effectButtons[k] = b;
                effects.Controls.Add(b);
            }
            body.Controls.Add(effects);

            body.Controls.Add(Section("Colours", "Main and Second are the colours the effect uses.\nClick a big swatch to pick any colour, or use the quick picks:\nleft-click sets Main, right-click sets Second."));
            Panel colors = Row(74);
            color1Lbl = Small("Main", 0, 0, 70);
            color1Btn = SwatchButton(70, 44);
            color1Btn.Location = new Point(0, 24);
            color1Btn.Click += delegate { PickColor(true); };
            color2Lbl = Small("Second", 86, 0, 70);
            color2Btn = SwatchButton(70, 44);
            color2Btn.Location = new Point(86, 24);
            color2Btn.Click += delegate { PickColor(false); };
            colors.Controls.Add(color1Lbl); colors.Controls.Add(color1Btn); colors.Controls.Add(color2Lbl); colors.Controls.Add(color2Btn);
            body.Controls.Add(colors);
            Label quickLbl = Note("Quick picks (left-click = Main, right-click = Second)", Text2);
            quickLbl.Tag = "stretch";
            body.Controls.Add(quickLbl);
            FlowLayoutPanel quick = WrapRow();
            Color[] presets = {
                Color.FromArgb(255, 0, 0), Color.FromArgb(255, 90, 0), Color.FromArgb(255, 200, 0), Color.FromArgb(0, 255, 60),
                Color.FromArgb(0, 230, 200), Color.FromArgb(0, 120, 255), Color.FromArgb(120, 0, 255), Color.FromArgb(255, 0, 170),
                Color.FromArgb(255, 255, 255), Color.FromArgb(255, 170, 80)
            };
            foreach (Color preset in presets)
            {
                Color c = preset;
                Button s = SwatchButton(38, 38);
                s.BackColor = c;
                s.MouseUp += delegate (object o, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Right) P.Color2 = Rgb.FromColor(c); else P.Color1 = Rgb.FromColor(c);
                    ApplyColors();
                };
                quick.Controls.Add(s);
            }
            body.Controls.Add(quick);

            body.Controls.Add(Section("Brightness", "How bright everything is."));
            brightSlider = SliderRow(body, 0, 100, P.Brightness);
            brightSlider.ValueChanged += delegate { P.Brightness = brightSlider.Value; engine.Renderer.Brightness = brightSlider.Value; SaveSoon(); };
            body.Controls.Add(Section("Speed", "How fast the effect moves."));
            speedSlider = SliderRow(body, 1, 10, P.Speed);
            speedSlider.ValueChanged += delegate { P.Speed = speedSlider.Value; engine.Renderer.Speed = speedSlider.Value; SaveSoon(); };

            ApplyColors();
        }

        TrackBar SliderRow(FlowLayoutPanel body, int min, int max, int val)
        {
            Panel row = Row(44);
            TrackBar t = new TrackBar
            {
                Location = new Point(0, 0), Width = 300, Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)),
                TickStyle = TickStyle.None, BackColor = Card, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            row.Controls.Add(t);
            body.Controls.Add(row);
            return t;
        }

        void SetEffect(EffectKind k)
        {
            P.Effect = k;
            engine.Renderer.Kind = k;
            // The effect is for the whole PC: fans with their own look come back in line unless told to keep it.
            if (P.FanEffect.HasValue && !settings.KeepFanLook)
            {
                SetFanEffect(null);
                ShowFanNote("Fans now follow the Lighting effect again.");
            }
            UpdateEffectButtons();
            settings.Save();
        }

        void UpdateEffectButtons()
        {
            if (color1Btn == null) return;
            foreach (KeyValuePair<EffectKind, Button> kv in effectButtons)
            {
                bool sel = kv.Key == P.Effect;
                kv.Value.BackColor = sel ? Accent : Card2;
                kv.Value.ForeColor = sel ? Color.White : Text1;
            }
            bool c1 = EffectRenderer.UsesColor1(P.Effect), c2 = EffectRenderer.UsesColor2(P.Effect);
            color1Btn.Enabled = c1; color1Lbl.ForeColor = c1 ? Text2 : Card2;
            color2Btn.Enabled = c2; color2Lbl.ForeColor = c2 ? Text2 : Card2;
        }

        void PickColor(bool first)
        {
            using (ColorDialog d = new ColorDialog { FullOpen = true, Color = (first ? P.Color1 : P.Color2).ToColor() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (first) P.Color1 = Rgb.FromColor(d.Color); else P.Color2 = Rgb.FromColor(d.Color);
                ApplyColors();
            }
        }

        void ApplyColors()
        {
            engine.Renderer.Color1 = P.Color1;
            engine.Renderer.Color2 = P.Color2;
            if (color1Btn == null) return;
            color1Btn.BackColor = P.Color1.ToColor();
            color2Btn.BackColor = P.Color2.ToColor();
            SaveSoon();
        }

        // ---------- Fans ----------

        // What the Fan button cycles through, like the button on a hub. null = same as the Lighting effect.
        static readonly EffectKind?[] FanCycle = {
            null, EffectKind.PerFan, EffectKind.RainbowWave, EffectKind.Spectrum, EffectKind.Static, EffectKind.Breathing,
            EffectKind.ColorWave, EffectKind.Comet, EffectKind.Starlight, EffectKind.Music, EffectKind.Off
        };

        Button fanButton, followBtn;
        Label fanNowLbl, fanNoteLbl;
        FlowLayoutPanel fanZoneList;
        RadioButton appInCharge, hubInCharge;
        Label hubHelpLbl;
        string fanZoneSignature;
        readonly Button[] fanSwatches = new Button[8];
        Button fanUseBtn;
        Label fanTestLbl, fanCountHint;
        System.Windows.Forms.Timer fanTestTimer, fanNoteTimer;

        void BuildFansCard(TableLayoutPanel grid)
        {
            FlowLayoutPanel body = MakeCard(grid, "Fans", "Your fans normally follow the Lighting effect.\nHere you can give them their own look instead,\nlike pressing the button on a fan hub.");

            body.Controls.Add(Section("Fan look", "Next fan effect: works like the button on a fan hub. Every click\nswitches the fans to the next look.\nFollow Lighting: fans show the same effect as everything else.\n\nFans use the profile's colours, brightness and speed.\nEach profile remembers its own fan look."));
            Panel big = Row(72);
            fanButton = new Button
            {
                Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, BackColor = Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14f), Text = "Next fan effect  ►"
            };
            fanButton.FlatAppearance.BorderSize = 0;
            fanButton.Click += delegate { CycleFanEffect(); };
            big.Controls.Add(fanButton);
            body.Controls.Add(big);
            fanNowLbl = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Text1, Margin = new Padding(0, 8, 0, 4) };
            body.Controls.Add(fanNowLbl);
            Panel followRow = Row(42);
            followBtn = MakeButton("Follow Lighting", 0, 2, 160, Card2);
            followBtn.Click += delegate { SetFanEffect(null); };
            followRow.Controls.Add(followBtn);
            body.Controls.Add(followRow);
            CheckBox keep = Check("Keep fans' own look when I pick a Lighting effect", settings.KeepFanLook);
            keep.CheckedChanged += delegate { settings.KeepFanLook = keep.Checked; settings.Save(); };
            body.Controls.Add(keep);
            fanNoteLbl = Note("", Good);
            fanNoteLbl.Tag = "stretch";
            fanNoteLbl.Visible = false;
            body.Controls.Add(fanNoteLbl);

            body.Controls.Add(Section("Each fan its own colour",
                "Give fan 1, fan 2, fan 3... their own colour: click a numbered swatch.\n\n" +
                "Only works if your fans are chained (the colour data runs through\n" +
                "fan 1, then fan 2...). Many hubs with a button copy the SAME signal\n" +
                "to every fan instead; then every fan always shows the same colour,\n" +
                "in any app. Click \"Test my fans\" to find out.\n\n" +
                "How many fans are on each header is set in Devices > LED count."));
            Panel testRow = Row(44);
            Button test = MakeButton("Test my fans", 0, 2, 130, Card2);
            test.Click += delegate { StartFanTest(); };
            testRow.Controls.Add(test);
            body.Controls.Add(testRow);
            FlowLayoutPanel swatches = WrapRow();
            for (int i = 0; i < fanSwatches.Length; i++)
            {
                int idx = i;
                Button s = SwatchButton(44, 40);
                s.Text = (i + 1).ToString();
                s.Font = new Font("Segoe UI Semibold", 10f);
                s.Click += delegate { PickFanColor(idx); };
                swatches.Controls.Add(s);
                fanSwatches[i] = s;
            }
            fanUseBtn = MakeButton("Use these colours", 0, 0, 160, Accent);
            fanUseBtn.Height = 40;
            fanUseBtn.Margin = new Padding(0, 0, 8, 8);
            fanUseBtn.Click += delegate { SetFanEffect(EffectKind.PerFan); };
            swatches.Controls.Add(fanUseBtn);
            body.Controls.Add(swatches);
            fanCountHint = Note("Only one fan per header so far. If several fans are chained on a header, tell RGB Unify in Devices > LED count.", Text2);
            fanCountHint.Tag = "stretch";
            body.Controls.Add(fanCountHint);
            fanTestLbl = Note("", Text2);
            fanTestLbl.Tag = "stretch";
            fanTestLbl.Visible = false;
            body.Controls.Add(fanTestLbl);

            body.Controls.Add(Section("Line up fans",
                "Fans mounted at different angles start their lights at different spots,\n" +
                "so the same effect looks turned on each fan.\n\n" +
                "Line up fans lights one white LED (where each fan starts) and two blue\n" +
                "ones (which way it runs). Turn each fan until every white LED is at\n" +
                "the top and the blue ones go the same way. Then they all match.\n\n" +
                "Works for fans on the motherboard's JARGB headers. Set the fan count\n" +
                "first (Devices > LED count) if several fans are chained on one header."));
            Panel alignRow = Row(44);
            Button align = MakeButton("Line up fans...", 0, 2, 160, Card2);
            align.Click += delegate { ShowAlignDialog(); };
            alignRow.Controls.Add(align);
            body.Controls.Add(alignRow);

            body.Controls.Add(Section("Fan headers","Tick the motherboard headers your fans or fan hub are plugged into.\nOn MSI boards these are the JARGB headers (3-pin, 5V).\nNot sure? Use Identify in the Devices card, or leave them all ticked."));
            fanZoneList = WrapRow();
            body.Controls.Add(fanZoneList);

            body.Controls.Add(Section("Who's in charge?",
                "Fan hubs with a button can either run their own effects or follow\n" +
                "your PC. Only one can win:\n\n" +
                "• RGB Unify: the app controls the fans. Put the hub in PC/M-B sync\n" +
                "  mode (on most hubs: hold the button for about 3 seconds).\n" +
                "• Hub button: the hub's own effects win. RGB Unify leaves the fan\n" +
                "  headers alone, and you change effects with the hub's button."));
            appInCharge = new RadioButton { Text = "RGB Unify controls the fans", AutoSize = true, ForeColor = Text1, Checked = !settings.HubInCharge, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 0, 2) };
            hubInCharge = new RadioButton { Text = "Hub button (the app leaves the fans alone)", AutoSize = true, ForeColor = Text1, Checked = settings.HubInCharge, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 4) };
            appInCharge.CheckedChanged += delegate { SetHubInCharge(hubInCharge.Checked); };
            // Radio buttons in a FlowLayoutPanel would all share one group; keep them in their own panel.
            FlowLayoutPanel radios = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            radios.Controls.Add(appInCharge);
            radios.Controls.Add(hubInCharge);
            body.Controls.Add(radios);
            hubHelpLbl = Note("", Warn);
            hubHelpLbl.Tag = "stretch";
            body.Controls.Add(hubHelpLbl);
        }

        void ShowFanNote(string text)
        {
            if (fanNoteLbl == null) return;
            fanNoteLbl.Text = text;
            fanNoteLbl.Visible = true;
            if (fanNoteTimer == null)
            {
                fanNoteTimer = new System.Windows.Forms.Timer { Interval = 6000 };
                fanNoteTimer.Tick += delegate { fanNoteTimer.Stop(); if (fanNoteLbl != null && !fanNoteLbl.IsDisposed) fanNoteLbl.Visible = false; };
            }
            fanNoteTimer.Stop();
            fanNoteTimer.Start();
        }

        void UpdateFanSwatches()
        {
            if (fanSwatches[0] == null || fanSwatches[0].IsDisposed) return;
            // One swatch per fan on the busiest fan header.
            int most = 1;
            foreach (RgbDevice d in engine.Devices)
                foreach (RgbZone z in d.Zones)
                    if (engine.IsFanZone(d, z)) most = Math.Max(most, engine.FansOn(d, z));
            most = Math.Min(most, fanSwatches.Length);
            fanCountHint.Visible = most == 1;
            for (int i = 0; i < fanSwatches.Length; i++)
            {
                Rgb c = P.FanColors[i];
                fanSwatches[i].Visible = i < most;
                fanSwatches[i].BackColor = c.ToColor();
                fanSwatches[i].ForeColor = ReadableOn(c.ToColor());
            }
        }

        void PickFanColor(int idx)
        {
            using (ColorDialog d = new ColorDialog { FullOpen = true, Color = P.FanColors[idx].ToColor() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                P.FanColors[idx] = Rgb.FromColor(d.Color);
            }
            // Picking a fan colour means you want to see it.
            SetFanEffect(EffectKind.PerFan);
        }

        void StartFanTest()
        {
            engine.FanTestUntil = DateTime.UtcNow.AddSeconds(10);
            fanTestLbl.ForeColor = Warn;
            fanTestLbl.Visible = true;
            fanTestLbl.Text = "Testing for 10 seconds: fan 1 red, fan 2 green, fan 3 blue...\n" +
                              "• Each fan a different colour: it works.\n" +
                              "• Every fan red: your hub copies one signal to all fans, so per-fan colours aren't possible.\n" +
                              "• Colours split inside one fan: fix the LED count in the Devices card.";
            if (fanTestTimer == null)
            {
                fanTestTimer = new System.Windows.Forms.Timer { Interval = 10000 };
                fanTestTimer.Tick += delegate { fanTestTimer.Stop(); if (!fanTestLbl.IsDisposed) fanTestLbl.Visible = false; };
            }
            fanTestTimer.Stop();
            fanTestTimer.Start();
        }

        void CycleFanEffect()
        {
            int i = Array.IndexOf(FanCycle, P.FanEffect);
            SetFanEffect(FanCycle[(i + 1) % FanCycle.Length]);
        }

        void SetFanEffect(EffectKind? k)
        {
            P.FanEffect = k;
            engine.FanEffect = k;
            settings.Save();
            UpdateFanButton();
        }

        void UpdateFanButton()
        {
            if (fanNowLbl == null || fanNowLbl.IsDisposed) return;
            fanNowLbl.Text = "Fans now: " + (P.FanEffect.HasValue ? EffectRenderer.DisplayName(P.FanEffect.Value) : "Following Lighting");
            UpdateFanSwatches();
            fanButton.Enabled = !settings.HubInCharge;
            fanButton.BackColor = fanButton.Enabled ? Accent : Card2;
            followBtn.BackColor = P.FanEffect.HasValue ? Card2 : Accent;
            followBtn.ForeColor = P.FanEffect.HasValue ? Text1 : Color.White;
            hubHelpLbl.Text = settings.HubInCharge
                ? "The hub is in charge: change fan effects with the button on the hub. RGB Unify won't touch the fan headers."
                : "If the fans don't follow, hold the hub's button ~3 seconds to put it in PC / M-B sync mode.";
        }

        void SetHubInCharge(bool hub)
        {
            settings.HubInCharge = hub;
            engine.HubInCharge = hub;
            settings.Save();
            UpdateFanButton();
        }

        // Rebuilt only when the set of headers changes, so ticking boxes isn't interrupted.
        void RefreshFanZones()
        {
            List<KeyValuePair<RgbDevice, RgbZone>> zones = new List<KeyValuePair<RgbDevice, RgbZone>>();
            string sig = "";
            foreach (RgbDevice d in engine.Devices)
                foreach (RgbZone z in d.Zones)
                    if (z.LedsMin != z.LedsMax || engine.IsFanZone(d, z))
                    {
                        zones.Add(new KeyValuePair<RgbDevice, RgbZone>(d, z));
                        sig += Engine.ZoneKey(d, z) + "=" + z.LedsCount + ";";
                    }
            if (sig == fanZoneSignature) return;
            fanZoneSignature = sig;
            UpdateFanSwatches();
            fanZoneList.Controls.Clear();
            if (zones.Count == 0)
            {
                fanZoneList.Controls.Add(Note("No ARGB headers found yet. They appear here once your motherboard is detected.", Text2));
                return;
            }
            foreach (KeyValuePair<RgbDevice, RgbZone> kv in zones)
            {
                RgbDevice d = kv.Key;
                RgbZone z = kv.Value;
                string key = Engine.ZoneKey(d, z);
                CheckBox c = Check(ZoneLabel(z.Name) + (z.LedsCount == 0 ? " (0 LEDs: set the LED count)" : " (" + z.LedsCount + " LEDs)"), engine.IsFanZone(d, z));
                if (z.LedsCount == 0) c.ForeColor = Warn;
                c.Margin = new Padding(0, 4, 24, 4);
                c.CheckedChanged += delegate { SetFanZone(key, c.Checked); };
                fanZoneList.Controls.Add(c);
            }
        }

        void SetFanZone(string key, bool on)
        {
            // First change turns "every ARGB header" into an explicit list. Copy-on-write: the engine reads it every frame.
            HashSet<string> keys = engine.FanZoneKeys != null ? new HashSet<string>(engine.FanZoneKeys) : new HashSet<string>();
            if (engine.FanZoneKeys == null)
                foreach (RgbDevice d in engine.Devices)
                    foreach (RgbZone z in d.Zones)
                        if (engine.IsFanZone(d, z)) keys.Add(Engine.ZoneKey(d, z));
            if (on) keys.Add(key); else keys.Remove(key);
            engine.FanZoneKeys = keys;
            settings.FanZones = new List<string>(keys);
            settings.Save();
            devicesDirty = true;
        }

        // ---------- Devices ----------

        FlowLayoutPanel deviceList;
        Label pawnHint;
        string deviceSignature;

        void BuildDevicesCard(TableLayoutPanel grid)
        {
            FlowLayoutPanel body = MakeCard(grid, "Devices",
                "Every RGB device RGB Unify found.\n\n" +
                "• Tick box: untick a device to leave it alone.\n" +
                "• Identify: flashes that device white so you can see which one it is.\n" +
                "• LED count: how many LEDs your fans/strips have\n" +
                "  (ARGB headers can't detect this themselves).\n" +
                "• Match each other: JARGB 1 and JARGB 2 show exactly the same thing.\n" +
                "  Flow through: the effect runs from one header into the next.");
            Panel top = Row(44);
            Button rescan = MakeButton("Rescan hardware", 0, 2, 170, Card2);
            rescan.Click += delegate { engine.Rescan(); deviceSignature = null; deviceList.Controls.Clear(); deviceList.Controls.Add(Note("Rescanning... this can take up to 30 seconds.", Text2)); };
            top.Controls.Add(rescan);
            body.Controls.Add(top);

            pawnHint = Note("", Warn);
            pawnHint.Tag = "stretch";
            pawnHint.Cursor = Cursors.Hand;
            pawnHint.Visible = false;
            pawnHint.Click += delegate
            {
                if (!Engine.PawnIOInstalled()) engine.BeginInstallPawnIO();
                else if (!settings.RamEnabled) SelectPage("Settings");
            };
            body.Controls.Add(pawnHint);

            deviceList = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Tag = "stretch", BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 0)
            };
            // Cards wrap their text to the column width, so a new width means rebuilding them.
            int lastWidth = 0;
            deviceList.SizeChanged += delegate
            {
                if (deviceList.Width == lastWidth) return;
                lastWidth = deviceList.Width;
                deviceSignature = null;
                devicesDirty = true;
            };
            body.Controls.Add(deviceList);

            body.Controls.Add(Section("Graphics card",
                "Graphics card lights are found automatically when the card is supported\n" +
                "(most ASUS, MSI, Gigabyte, EVGA, Zotac, Galax/KFA2, Palit, Sapphire\n" +
                "and PowerColor gaming cards). It needs the PawnIO driver.\n\n" +
                "Cards from PC makers (Dell, HP, Lenovo) and NVIDIA/AMD's own\n" +
                "reference cards usually have no controllable RGB at all."));
            gpuList = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Tag = "stretch", BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 0)
            };
            body.Controls.Add(gpuList);

            // Only shown on laptops.
            laptopSection = Section("Laptop keyboard & light bar",
                "Gaming laptop keyboards, light bars and logos are found automatically\n" +
                "when the model is supported. Keyboards that can only show one colour\n" +
                "use their own built-in rainbow / colour cycle / breathing for those effects.\n\n" +
                "Close the laptop maker's app (Armoury Crate, Legion Zone, MSI Center,\n" +
                "OMEN Gaming Hub...) first: it can lock the keyboard lighting.");
            laptopSection.Visible = false;
            body.Controls.Add(laptopSection);
            laptopNote = Note("", Text2);
            laptopNote.Tag = "stretch";
            laptopNote.Visible = false;
            body.Controls.Add(laptopNote);
        }

        // ---------- laptops ----------

        Control laptopSection;
        Label laptopNote;
        string laptopSignature;

        // What the engine can drive on each laptop brand (OpenRGB 1.1), and what to do if nothing was found.
        static string LaptopAdvice()
        {
            string maker = SystemInfo.Maker.ToLowerInvariant(), model = SystemInfo.Model.ToLowerInvariant();
            if (maker.Contains("asus"))
            {
                if (model.Contains("tuf"))
                    return "ASUS TUF keyboards work through ASUS's \"System Control Interface\" driver. If it's missing, install it from your laptop's ASUS support page " +
                           "(it comes with MyASUS / the ASUS drivers). Never remove it: your Fn keys need it too.";
                return "Supported: ROG Strix SCAR 15 / 17 and older ROG models with the Aura Core keyboard. Newer ROG Strix / Zephyrus / Flow laptops (2021+) " +
                       "aren't supported by the engine yet. G-Helper (free) can set their keyboard colour in the meantime.";
            }
            if (maker.Contains("lenovo"))
                return "Supported: Legion 7 / 7S (Gen 5–10), Legion 5 (2020–2023 and Gen 10), Legion Y740 and IdeaPad 3 15ACH6. Close Legion Zone / Lenovo Vantage lighting first.";
            if (maker.Contains("micro-star") || maker.Contains("msi"))
                return "Supported: MSI Raider A18 and GE68HX (per-key keyboard and light bar), and older laptops with the SteelSeries 3-zone keyboard. Close MSI Center first.";
            if (maker.Contains("hp"))
                return "Supported: HP OMEN laptops with a 4-zone keyboard. Close OMEN Gaming Hub's lighting first.";
            if (maker.Contains("acer"))
                return "Supported: Acer Nitro laptops with the ENE keyboard controller. Close NitroSense / PredatorSense lighting first.";
            if (maker.Contains("gigabyte") || model.Contains("aorus"))
                return "Supported: Aorus 15BKF and 17X (keyboard and backlight).";
            if (maker.Contains("razer"))
                return "Supported: Razer Blade, Blade Pro and Blade Stealth (2016–2023). Close Razer Synapse first.";
            if (maker.Contains("dell") || maker.Contains("alienware"))
                return "Supported: Dell G-series keyboards. Alienware laptop lighting isn't supported by the engine yet.";
            if (maker.Contains("clevo") || maker.Contains("notebook") || maker.Contains("eluktronics") || maker.Contains("xmg") ||
                maker.Contains("schenker") || maker.Contains("sager") || maker.Contains("origin") || maker.Contains("tongfang"))
                return "Supported: Clevo-based laptops with the ITE keyboard and light bar controllers.";
            return "This laptop's keyboard lighting isn't supported yet. Send the log (Settings > Advanced > Open log) so support can be added.";
        }

        void RefreshLaptop()
        {
            if (laptopSection == null || gpus == null) return; // SystemInfo is read in the background with the GPU list
            if (!SystemInfo.IsLaptop) { laptopSection.Visible = laptopNote.Visible = false; return; }
            List<string> found = new List<string>();
            foreach (RgbDevice d in engine.Devices)
                if (d.TypeName == "Laptop" || d.TypeName == "Keyboard" || d.TypeName == "Light" || d.TypeName == "LED Strip") found.Add(d.Name);
            bool scanning = !engine.DetectionDone;
            string sig = string.Join("|", found.ToArray()) + scanning;
            if (sig == laptopSignature) return;
            laptopSignature = sig;
            laptopSection.Visible = laptopNote.Visible = true;
            string title = SystemInfo.Maker + " " + SystemInfo.Model;
            if (found.Count > 0)
            {
                laptopNote.Text = title + ": found " + string.Join(", ", found.ToArray()) + ". It's in the list above and synced with everything else.";
                laptopNote.ForeColor = Good;
            }
            else if (scanning)
            {
                laptopNote.Text = title + ": still scanning...";
                laptopNote.ForeColor = Text2;
            }
            else
            {
                laptopNote.Text = title + ": no keyboard lighting found yet.\n" + LaptopAdvice();
                laptopNote.ForeColor = Warn;
            }
        }

        // ---------- graphics cards ----------

        FlowLayoutPanel gpuList;
        string gpuSignature;
        static List<GpuInfo> gpus;

        class GpuInfo
        {
            public string Name, Maker;
            public bool Integrated, OemOrReference;
        }

        // PCI subsystem vendor IDs of card makers (the "SUBSYS_xxxxVVVV" part of the device ID).
        static readonly Dictionary<string, string> CardMakers = new Dictionary<string, string>
        {
            { "1043", "ASUS" }, { "1462", "MSI" }, { "1458", "Gigabyte" }, { "3842", "EVGA" }, { "19DA", "Zotac" },
            { "1B4C", "Galax / KFA2" }, { "1569", "Palit" }, { "10B0", "Gainward" }, { "1DA2", "Sapphire" }, { "148C", "PowerColor" },
            { "1682", "XFX" }, { "1849", "ASRock" }, { "7377", "Colorful" }, { "196E", "PNY" }, { "1ACC", "Point of View" },
            { "1028", "Dell (OEM)" }, { "103C", "HP (OEM)" }, { "17AA", "Lenovo (OEM)" }, { "1025", "Acer (OEM)" },
            { "10DE", "NVIDIA (Founders / reference)" }, { "1002", "AMD (reference)" }
        };

        // Reads the installed graphics cards once, in the background (WMI can take a second).
        void LoadGpus()
        {
            if (gpus != null) return;
            gpus = new List<GpuInfo>();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<GpuInfo> found = new List<GpuInfo>();
                bool laptop = SystemInfo.IsLaptop; // reads the PC type here, off the UI thread
                try
                {
                    using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                        foreach (System.Management.ManagementObject o in s.Get())
                        {
                            string name = (o["Name"] as string) ?? "Graphics card";
                            string pnp = ((o["PNPDeviceID"] as string) ?? "").ToUpperInvariant();
                            if (!pnp.StartsWith("PCI\\")) continue; // remote-desktop and virtual adapters
                            GpuInfo g = new GpuInfo { Name = name };
                            int at = pnp.IndexOf("SUBSYS_");
                            string sub = at >= 0 && pnp.Length >= at + 15 ? pnp.Substring(at + 11, 4) : "";
                            string maker;
                            g.Maker = CardMakers.TryGetValue(sub, out maker) ? maker : (sub.Length > 0 ? "maker ID " + sub : "unknown maker");
                            g.OemOrReference = g.Maker.Contains("OEM") || g.Maker.Contains("reference");
                            // CPU graphics: AMD "Radeon(TM) Graphics" and Intel UHD/Iris have no lights.
                            g.Integrated = name.IndexOf("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           (pnp.Contains("VEN_8086") && name.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) < 0);
                            found.Add(g);
                        }
                }
                catch { }
                gpus = found;
                devicesDirty = true;
            });
        }

        void RefreshGpus()
        {
            LoadGpus();
            bool rgbGpu = false;
            foreach (RgbDevice d in engine.Devices) if (d.TypeName == "Graphics Card") rgbGpu = true;
            bool scanning = !engine.DetectionDone;
            string sig = gpus.Count + "|" + rgbGpu + "|" + scanning + "|" + Engine.PawnIOInstalled() + "|" + gpuList.Width;
            if (sig == gpuSignature) return;
            gpuSignature = sig;
            gpuList.Controls.Clear();
            int w = Math.Max(200, gpuList.Width);
            foreach (GpuInfo g in gpus)
            {
                string text;
                Color c = Text2;
                if (g.Integrated)
                    text = "Built into the CPU, so there are no lights to control.";
                else if (rgbGpu)
                {
                    text = "RGB found. It's in the list above and synced with everything else.";
                    c = Good;
                }
                else if (scanning)
                    text = "Still scanning...";
                else if (!Engine.PawnIOInstalled())
                {
                    text = "No RGB found yet. Graphics card lights need the PawnIO driver: install it from the banner on the left.";
                    c = Warn;
                }
                else if (g.OemOrReference)
                    text = "Made by " + g.Maker + ". Cards like this usually have no controllable RGB (often just a white or fixed logo).";
                else
                {
                    text = "Made by " + g.Maker + ". No controllable RGB was found. This model may not be supported yet, or another GPU app " +
                           "(GPU Tweak, Mystic Light, RGB Fusion, Precision X1) is holding the lights: close it and click Rescan.";
                    c = Warn;
                }
                Label n = new Label { Text = g.Name, AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), ForeColor = Text1, Margin = new Padding(0, 4, 0, 0) };
                Label l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(w, 0), ForeColor = c, Margin = new Padding(0, 2, 0, 8) };
                gpuList.Controls.Add(n);
                gpuList.Controls.Add(l);
            }
            if (gpus.Count == 0) gpuList.Controls.Add(Note("Looking for graphics cards...", Text2));
        }

        void RebuildDevices()
        {
            List<RgbDevice> list = engine.Devices;
            string st = engine.Status ?? "";
            // Skip rebuilding (and the flicker) when nothing visible changed.
            string sig = list.Count == 0 ? "empty:" + st : "";
            foreach (RgbDevice d in list)
                sig += d.Key + "|" + d.LedCount + "|" + engine.IsEnabled(d) + "|" + engine.MatchesZones(d) + "|" + ZoneSummary(d) + ";";
            if (sig == deviceSignature) return;
            deviceSignature = sig;

            deviceList.SuspendLayout();
            deviceList.Controls.Clear();
            if (list.Count == 0)
            {
                string msg;
                if (Engine.FindEngineCached() == null || engine.SetupBusy)
                    msg = "Setting up... devices will appear here automatically.";
                else if (st == "No RGB devices found")
                    msg = "Scan finished: no controllable RGB hardware was found.\n\n" +
                        (Engine.PawnIOInstalled() ? "" : "•  Install the PawnIO driver (yellow link above).\n") +
                        "•  Close or remove other RGB apps (Clean Up tab).\n" +
                        "•  Plug fans/strips into a motherboard RGB header or controller.\n" +
                        "•  Laptops usually have no controllable RGB.";
                else
                    msg = "Scanning for RGB hardware... this usually takes a few seconds.";
                Label l = Note(msg, Text2);
                l.MaximumSize = new Size(Math.Max(200, deviceList.Width), 0);
                deviceList.Controls.Add(l);
            }
            foreach (RgbDevice d in list) deviceList.Controls.Add(DeviceCard(d));
            deviceList.ResumeLayout(true);
        }

        Control DeviceCard(RgbDevice d)
        {
            int w = Math.Max(300, deviceList.Width);
            bool zoned = d.LitZoneCount >= 2;
            bool resizable = false;
            List<string> unset = new List<string>();
            foreach (RgbZone z in d.Zones)
                if (z.LedsMin != z.LedsMax)
                {
                    resizable = true;
                    if (z.LedsCount == 0) unset.Add(ZoneLabel(z.Name));
                }

            int height = zoned ? 186 : 112;
            Panel card = new Panel { Width = w, Height = height, BackColor = Card2, Margin = new Padding(0, 0, 0, 10) };
            if (unset.Count > 0)
            {
                // ARGB headers start at 0 LEDs: whatever is plugged in stays dark until the count is set.
                Label warn = new Label
                {
                    Text = string.Join(", ", unset.ToArray()) + (unset.Count == 1 ? " has" : " have") + " 0 LEDs set, so anything plugged in there stays dark. Click Set LED count.",
                    Location = new Point(14, height - 4), AutoSize = true, MaximumSize = new Size(w - 28, 0), ForeColor = Warn
                };
                card.Controls.Add(warn);
                card.Height = height + warn.PreferredHeight + 6;
            }
            CheckBox on = new CheckBox { Location = new Point(14, 16), Size = new Size(20, 20), Checked = engine.IsEnabled(d), Cursor = Cursors.Hand };
            on.CheckedChanged += delegate
            {
                engine.SetEnabled(d, on.Checked);
                settings.DisabledDevices = new List<string>(engine.DisabledKeys);
                settings.Save();
            };
            card.Controls.Add(on);
            AnchorAll(card, new Label { Text = d.Name, Location = new Point(42, 12), Size = new Size(w - 56, 22), Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Text1, AutoEllipsis = true, UseMnemonic = false });
            string sub = d.TypeName + (string.IsNullOrEmpty(d.Vendor) ? "" : "  •  " + d.Vendor) + "  •  " + d.LedCount + " LEDs  •  " +
                         (d.SupportsDirect ? "Full effects" : "Basic (single colour)");
            AnchorAll(card, new Label { Text = sub, Location = new Point(42, 36), Size = new Size(w - 56, 20), ForeColor = Text2, AutoEllipsis = true });

            Button id = MakeButton("Identify", 14, 64, 100, Card);
            id.Click += delegate { engine.Identify(d); };
            card.Controls.Add(id);
            if (resizable)
            {
                Button sz = MakeButton(unset.Count > 0 ? "Set LED count!" : "LED count", 122, 64, 130, unset.Count > 0 ? Accent : Card);
                sz.Click += delegate { ShowZoneDialog(d); };
                card.Controls.Add(sz);
            }
            if (zoned)
            {
                // e.g. JARGB 1 + JARGB 2: identical copies of the effect, or one strip running through both.
                bool match = engine.MatchesZones(d);
                Button same = MakeButton("Match each other", 14, 110, 150, match ? Accent : Card);
                Button flow = MakeButton("Flow through", 172, 110, 124, match ? Card : Accent);
                same.Click += delegate { SetZoneFlow(d, false); };
                flow.Click += delegate { SetZoneFlow(d, true); };
                card.Controls.Add(same);
                card.Controls.Add(flow);
                AnchorAll(card, new Label { Text = ZoneSummary(d), Location = new Point(14, 154), Size = new Size(w - 28, 22), ForeColor = Text2, AutoEllipsis = true });
            }
            return card;
        }

        static void AnchorAll(Panel card, Label l)
        {
            l.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            card.Controls.Add(l);
        }

        void SetZoneFlow(RgbDevice d, bool flow)
        {
            // Copy-on-write: the engine thread reads this set every frame.
            HashSet<string> keys = new HashSet<string>(engine.FlowKeys);
            if (flow) keys.Add(d.Key); else keys.Remove(d.Key);
            engine.FlowKeys = keys;
            settings.FlowDevices = new List<string>(keys);
            settings.Save();
            devicesDirty = true;
        }

        // MSI's software and board silkscreen say JARGB; OpenRGB calls the same headers JRAINBOW.
        static string ZoneLabel(string name)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^JRAINBOW\s*(\d+)$");
            return m.Success ? "JARGB " + m.Groups[1].Value : name;
        }

        static string ZoneSummary(RgbDevice d)
        {
            List<string> parts = new List<string>();
            foreach (RgbZone z in d.Zones) if (z.LedsCount > 0) parts.Add(ZoneLabel(z.Name) + " (" + z.LedsCount + ")");
            return string.Join(", ", parts.ToArray());
        }

        void ShowZoneDialog(RgbDevice d)
        {
            using (Form f = new Form())
            {
                f.Text = "LED count – " + d.Name;
                f.BackColor = Bg; f.ForeColor = Text1; f.Font = Font;
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Controls.Add(new Label
                {
                    Text = "ARGB headers can't detect what's plugged in, so tell RGB Unify what's on each one.\n" +
                           "Fans: how many fans are chained on that header (1 for an LED strip, or for a hub that copies the same signal to every fan).\n" +
                           "LEDs per fan: check the box or product page; most fans have 8 to 24.",
                    Location = new Point(14, 12), Size = new Size(600, 84), ForeColor = Text2
                });
                f.Controls.Add(new Label { Text = "Fans", Location = new Point(250, 104), Size = new Size(70, 20), ForeColor = Text2 });
                f.Controls.Add(new Label { Text = "LEDs per fan", Location = new Point(352, 104), Size = new Size(100, 20), ForeColor = Text2 });
                int y = 128;
                List<object[]> inputs = new List<object[]>();
                for (int i = 0; i < d.Zones.Count; i++)
                {
                    RgbZone z = d.Zones[i];
                    if (z.LedsMin == z.LedsMax) continue;
                    string key = Engine.ZoneKey(d, z);
                    string label = ZoneLabel(z.Name);
                    int fans;
                    if (!settings.ZoneFans.TryGetValue(key, out fans)) fans = 1;
                    int perFan = z.LedsCount > 0 && z.LedsCount % fans == 0 ? (int)z.LedsCount / fans : (z.LedsCount > 0 ? (int)z.LedsCount : 8);
                    if (z.LedsCount > 0 && z.LedsCount % fans != 0) fans = 1;

                    f.Controls.Add(new Label { Text = label == z.Name ? label : label + "  (" + z.Name + ")", Location = new Point(14, y + 3), Size = new Size(230, 22), ForeColor = Text1 });
                    NumericUpDown nf = new NumericUpDown { Location = new Point(250, y), Width = 64, Minimum = 1, Maximum = 16, Value = fans };
                    f.Controls.Add(nf);
                    f.Controls.Add(new Label { Text = "×", Location = new Point(324, y + 3), Size = new Size(20, 22), ForeColor = Text2 });
                    NumericUpDown np = new NumericUpDown { Location = new Point(352, y), Width = 72, Minimum = 1, Maximum = z.LedsMax, Value = Math.Max(1, Math.Min(z.LedsMax, perFan)) };
                    f.Controls.Add(np);
                    Label total = new Label { Location = new Point(436, y + 3), Size = new Size(180, 22), ForeColor = Text1 };
                    f.Controls.Add(total);
                    RgbZone zone = z;
                    EventHandler update = delegate
                    {
                        long t = (long)nf.Value * (long)np.Value;
                        total.Text = "= " + t + " LEDs" + (t > zone.LedsMax ? " (max " + zone.LedsMax + ")" : "");
                        total.ForeColor = t > zone.LedsMax ? Warn : Text1;
                    };
                    nf.ValueChanged += update;
                    np.ValueChanged += update;
                    update(null, EventArgs.Empty);
                    inputs.Add(new object[] { i, key, nf, np, z });
                    y += 38;
                }
                Button ok = MakeButton("Save", 520, y + 14, 96, Accent);
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);
                f.AcceptButton = ok;
                f.ClientSize = new Size(630, y + 64);
                if (f.ShowDialog(this) != DialogResult.OK) return;

                foreach (object[] row in inputs)
                {
                    RgbZone z = (RgbZone)row[4];
                    int fans = (int)((NumericUpDown)row[2]).Value;
                    long count = fans * (long)((NumericUpDown)row[3]).Value;
                    engine.ResizeZone(d, (int)row[0], (int)Math.Max(z.LedsMin, Math.Min(z.LedsMax, count)));
                    settings.ZoneFans[(string)row[1]] = fans;
                }
                // Copy-on-write: the engine thread reads the fan counts every frame.
                engine.FanCounts = new Dictionary<string, int>(settings.ZoneFans);
                settings.Save();
                UpdateFanSwatches();
            }
        }

        // ---------- line up fans ----------

        void ShowAlignDialog()
        {
            // One row per fan on every ARGB header the app drives.
            List<object[]> fans = new List<object[]>(); // { label, fan key, LEDs per fan }
            foreach (RgbDevice d in engine.Devices)
                foreach (RgbZone z in d.Zones)
                {
                    if (z.LedsCount == 0 || z.LedsMin == z.LedsMax) continue;
                    int count = Math.Min(engine.FansOn(d, z), (int)z.LedsCount);
                    int per = (int)z.LedsCount / count;
                    for (int k = 0; k < count; k++)
                        fans.Add(new object[] { ZoneLabel(z.Name) + (count > 1 ? "  –  fan " + (k + 1) : ""), Engine.FanKey(d, z, k), per });
                }
            if (fans.Count == 0)
            {
                MessageBox.Show(this, "No fans to line up yet. Set the LED count of your JARGB headers first (Devices > LED count).",
                    "Line up fans", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (settings.HubInCharge)
            {
                MessageBox.Show(this, "The hub is in charge of the fans right now (Fans > Who's in charge?), so RGB Unify can't light them. Switch it to RGB Unify first.",
                    "Line up fans", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (Form f = new Form())
            {
                f.Text = "Line up fans";
                f.BackColor = Bg; f.ForeColor = Text1; f.Font = Font;
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false;
                f.Controls.Add(new Label
                {
                    Text = "Each fan now shows ONE white light (where it starts) and TWO blue lights (which way it runs).\n" +
                           "1.  Click ◄ or ► until the white light is at the TOP of that fan.\n" +
                           "2.  If its blue lights go anticlockwise while the others go clockwise, tick Flip.\n" +
                           "When every fan looks the same, click Done. Your effects will then line up on all fans.",
                    Location = new Point(14, 12), Size = new Size(620, 84), ForeColor = Text2
                });
                FlowLayoutPanel list = new FlowLayoutPanel
                {
                    Location = new Point(14, 104), Size = new Size(620, Math.Min(380, fans.Count * 46 + 4)), FlowDirection = FlowDirection.TopDown,
                    WrapContents = false, AutoScroll = true, BackColor = Bg
                };
                DarkScroll(list);
                foreach (object[] fan in fans)
                {
                    string key = (string)fan[1];
                    int per = (int)fan[2];
                    Panel row = new Panel { Size = new Size(590, 42), BackColor = Card, Margin = new Padding(0, 0, 0, 4) };
                    row.Controls.Add(new Label { Text = (string)fan[0], Location = new Point(12, 11), Size = new Size(220, 22), ForeColor = Text1 });
                    Label turned = new Label { Location = new Point(400, 11), Size = new Size(180, 22), ForeColor = Text2 };
                    Button left = MakeButton("◄", 240, 4, 44, Card2);
                    Button right = MakeButton("►", 290, 4, 44, Card2);
                    CheckBox flip = new CheckBox { Text = "Flip", Location = new Point(344, 11), AutoSize = true, ForeColor = Text1, Cursor = Cursors.Hand };
                    int[] cur;
                    if (!settings.FanTwists.TryGetValue(key, out cur)) cur = new[] { 0, 0 };
                    int[] state = { ((cur[0] % per) + per) % per, cur[1] };
                    flip.Checked = state[1] != 0;
                    Action apply = delegate
                    {
                        if (state[0] == 0 && state[1] == 0) settings.FanTwists.Remove(key);
                        else settings.FanTwists[key] = new[] { state[0], state[1] };
                        // Copy-on-write: the engine thread reads the twists every frame.
                        engine.FanTwists = new Dictionary<string, int[]>(settings.FanTwists);
                        turned.Text = state[0] == 0 ? "not turned" : "turned " + state[0] + " of " + per;
                    };
                    left.Click += delegate { state[0] = (state[0] + per - 1) % per; apply(); };
                    right.Click += delegate { state[0] = (state[0] + 1) % per; apply(); };
                    flip.CheckedChanged += delegate { state[1] = flip.Checked ? 1 : 0; apply(); };
                    apply();
                    row.Controls.Add(left); row.Controls.Add(right); row.Controls.Add(flip); row.Controls.Add(turned);
                    list.Controls.Add(row);
                }
                f.Controls.Add(list);
                int y = list.Bottom + 14;
                Button reset = MakeButton("Reset all", 14, y, 110, Card2);
                reset.Click += delegate
                {
                    foreach (object[] fan in fans) settings.FanTwists.Remove((string)fan[1]);
                    engine.FanTwists = new Dictionary<string, int[]>(settings.FanTwists);
                    f.DialogResult = DialogResult.Retry;
                };
                Button done = MakeButton("Done", 538, y, 96, Accent);
                done.DialogResult = DialogResult.OK;
                f.Controls.Add(reset); f.Controls.Add(done);
                f.AcceptButton = done;
                f.ClientSize = new Size(648, y + 50);

                engine.Aligning = true;
                DialogResult r;
                try { r = f.ShowDialog(this); }
                finally { engine.Aligning = false; }
                settings.Save();
                if (r == DialogResult.Retry) ShowAlignDialog();
            }
        }

        void UpdateDeviceHint(string exe)
        {
            bool hasRam = false;
            foreach (RgbDevice d in engine.Devices) if (d.TypeName == "RAM") hasRam = true;
            string text = null;
            if (exe != null && !Engine.PawnIOInstalled() && !engine.SetupBusy)
                text = "RAM, motherboard or graphics card lights missing? Click here to install the PawnIO driver they need.";
            else if (!settings.RamEnabled)
                text = "RAM lighting is off for safety. Click here to turn it on (Settings tab).";
            else if (!hasRam && engine.RamWaitingForReboot)
                text = "RAM was already scanned this Windows session. Restart your PC to scan it again.";
            pawnHint.Visible = text != null;
            if (text != null && pawnHint.Text != text) pawnHint.Text = text;
        }
    }
}
