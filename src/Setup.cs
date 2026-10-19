using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("RGB Unify Setup")]

namespace RGBUnify
{
    // One exe for both directions: RGBUnify-Setup.exe installs; the copy saved as Uninstall.exe removes.
    // /S runs either silently with default options.
    static class SetupProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool uninstall = Path.GetFileNameWithoutExtension(Application.ExecutablePath).Equals("Uninstall", StringComparison.OrdinalIgnoreCase);
            bool silent = false;
            foreach (string a in args)
            {
                string x = a.ToLowerInvariant();
                if (x == "/uninstall" || x == "--uninstall") uninstall = true;
                if (x == "/s" || x == "/silent" || x == "--silent") silent = true;
            }

            if (silent)
            {
                try
                {
                    if (uninstall) Installer.Uninstall(Installer.UninstallDir, false, Installer.WeInstalledPawnIO(), null);
                    else Installer.Install(InstallOptions.Defaults(), null);
                }
                catch { Environment.ExitCode = 1; }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(uninstall));
        }
    }

    public class InstallOptions
    {
        public string Dir;
        public bool DesktopShortcut = true, StartMenuShortcut = true, StartWithWindows = true, DownloadEngine = true, InstallPawnIO = true;

        public static InstallOptions Defaults()
        {
            InstallOptions o = new InstallOptions();
            o.Dir = Installer.ExistingInstallDir() ?? Installer.DefaultDir;
            o.DownloadEngine = !Installer.EngineExists(o.Dir);
            o.InstallPawnIO = !Engine.PawnIOInstalled();
            return o;
        }
    }

    public static class Installer
    {
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RGBUnify";
        const string LinkName = "RGB Unify.lnk";
        // Only these are ever deleted on uninstall, so a badly chosen install folder can't lose anyone's files.
        static readonly string[] OurFiles = { "RGBUnify.exe", "License.txt", "Uninstall.exe", "engine_download.zip" };

        public static string Version
        {
            get { Version v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RGB Unify"); }
        }

        public static string ThisDir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }

        // The registered install wins, so running Setup /uninstall from Downloads can't touch the Downloads folder.
        public static string UninstallDir
        {
            get { return ExistingInstallDir() ?? ThisDir; }
        }

        public static string ExistingInstallDir()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                {
                    string dir = k == null ? null : k.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(dir) ? null : dir;
                }
            }
            catch { return null; }
        }

        public static bool EngineExists(string dir)
        {
            try
            {
                string e = Path.Combine(dir, "engine");
                return Directory.Exists(e) && Directory.GetFiles(e, "OpenRGB.exe", SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

        static void Report(Action<string, int> report, string step, int percent)
        {
            if (report != null) report(step, percent);
        }

        public static List<string> Install(InstallOptions o, Action<string, int> report)
        {
            List<string> warnings = new List<string>();

            Report(report, "Closing RGB Unify", -1);
            CloseRunningApp();

            Report(report, "Copying files", -1);
            Directory.CreateDirectory(o.Dir);
            string exe = Path.Combine(o.Dir, "RGBUnify.exe");
            string uninstaller = Path.Combine(o.Dir, "Uninstall.exe");
            WriteResource("RGBUnify.exe", exe);
            WriteResource("License.txt", Path.Combine(o.Dir, "License.txt"));
            if (!SamePath(Application.ExecutablePath, uninstaller)) File.Copy(Application.ExecutablePath, uninstaller, true);

            Report(report, "Creating shortcuts", -1);
            string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), LinkName);
            string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), LinkName);
            if (o.StartMenuShortcut) CreateShortcut(startLnk, exe); else TryDelete(startLnk);
            if (o.DesktopShortcut) CreateShortcut(deskLnk, exe); else TryDelete(deskLnk);

            Report(report, "Setting up startup", -1);
            Settings.SetStartup(o.StartWithWindows, exe);

            if (o.DownloadEngine)
            {
                try
                {
                    Engine.DownloadEngine(o.Dir, delegate (int p) { Report(report, "Downloading RGB engine (OpenRGB)", p); });
                }
                catch (Exception ex)
                {
                    warnings.Add("The RGB engine could not be downloaded (" + ex.Message + "). RGB Unify will try again when it starts.");
                }
            }

            // Remembered so uninstalling RGB Unify can offer to remove a driver it put there (but not one the user already had).
            bool installedPawn = WeInstalledPawnIO();
            if (o.InstallPawnIO)
            {
                try
                {
                    Engine.InstallPawnIO(
                        delegate (int p) { Report(report, "Downloading PawnIO driver (from pawnio.eu)", p); },
                        delegate { Report(report, "Running PawnIO's own installer", -1); });
                    installedPawn = true;
                }
                catch (Exception ex)
                {
                    warnings.Add("The PawnIO driver could not be installed (" + ex.Message + "). RAM and motherboard lighting needs it; you can install it later from the Devices page.");
                }
            }

            Report(report, "Registering with Windows", -1);
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "RGB Unify");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "RGB Unify");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", o.Dir);
                k.SetValue("UninstallString", "\"" + uninstaller + "\"");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /S");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(DirSize(o.Dir) / 1024), RegistryValueKind.DWord);
                k.SetValue("InstalledPawnIO", installedPawn ? 1 : 0, RegistryValueKind.DWord);
            }

            Report(report, "Done", 100);
            return warnings;
        }

        // True when RGB Unify's setup installed PawnIO (rather than the user or another app).
        public static bool WeInstalledPawnIO()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                    return k != null && Convert.ToInt32(k.GetValue("InstalledPawnIO", 0)) == 1;
            }
            catch { return false; }
        }

        // Runs PawnIO's own silent uninstaller, exactly as listed in Settings > Apps.
        static string UninstallPawnIO()
        {
            string cmd;
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
            {
                if (k == null) return null;
                cmd = (k.GetValue("QuietUninstallString") as string) ?? ((k.GetValue("UninstallString") as string) + " -silent");
            }
            if (string.IsNullOrEmpty(cmd)) return "PawnIO has no uninstaller entry; remove it from Settings > Apps.";
            string exe = cmd, args = "";
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                exe = cmd.Substring(1, end - 1);
                args = cmd.Substring(end + 1).Trim();
            }
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi)) p.WaitForExit(120000);
            return Engine.PawnIOInstalled() ? "PawnIO could not be removed automatically; remove it from Settings > Apps." : null;
        }

        public static void Uninstall(string dir, bool deleteSettings, bool removePawnIO, Action<string, int> report)
        {
            Report(report, "Closing RGB Unify", -1);
            CloseRunningApp();

            Report(report, "Removing startup task", -1);
            Settings.SetStartup(false, "");

            Report(report, "Removing shortcuts", -1);
            foreach (Environment.SpecialFolder f in new[] {
                Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs,
                Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.DesktopDirectory })
                TryDelete(Path.Combine(Environment.GetFolderPath(f), LinkName));

            Report(report, "Removing files", -1);
            foreach (string name in OurFiles)
            {
                string f = Path.Combine(dir, name);
                if (!SamePath(f, Application.ExecutablePath)) TryDelete(f);
            }
            try { if (Directory.Exists(Path.Combine(dir, "engine"))) Directory.Delete(Path.Combine(dir, "engine"), true); } catch { }

            if (deleteSettings)
            {
                Report(report, "Removing settings", -1);
                try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGBUnify"), true); } catch { }
            }

            if (removePawnIO && Engine.PawnIOInstalled())
            {
                Report(report, "Removing the PawnIO driver (its own uninstaller)", -1);
                try { UninstallPawnIO(); } catch { }
            }

            Report(report, "Unregistering from Windows", -1);
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }

            // A running exe can't delete itself; a short-lived cmd finishes the job. rmdir without /s only removes an empty folder.
            if (SamePath(Path.GetDirectoryName(Application.ExecutablePath), dir))
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + Application.ExecutablePath + "\" & rmdir \"" + dir + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
            }
            else
            {
                try { Directory.Delete(dir, false); } catch { }
            }
            Report(report, "Done", 100);
        }

        // Asks a running RGB Unify to exit cleanly (so its engine stops too), then force-closes anything left,
        // including old portable copies that predate the exit signal.
        static void CloseRunningApp()
        {
            try
            {
                EventWaitHandle exit;
                if (EventWaitHandle.TryOpenExisting("RGBUnify_Exit", out exit)) { exit.Set(); exit.Close(); }
            }
            catch { }
            DateTime until = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < until && Process.GetProcessesByName("RGBUnify").Length > 0) Thread.Sleep(200);

            foreach (Process p in Process.GetProcessesByName("RGBUnify"))
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            foreach (Process p in Process.GetProcessesByName("OpenRGB"))
            {
                try
                {
                    // Only our hidden engine (lives in an "engine" folder) - not a separately installed OpenRGB.
                    if (p.MainModule.FileName.IndexOf(@"\engine\", StringComparison.OrdinalIgnoreCase) >= 0) { p.Kill(); p.WaitForExit(5000); }
                }
                catch { }
            }
        }

        static void WriteResource(string name, string dest)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    using (FileStream f = File.Create(dest)) s.CopyTo(f);
                    return;
                }
                catch (IOException)
                {
                    // The old exe can stay locked for a moment after its process exits.
                    if (attempt >= 20) throw;
                    Thread.Sleep(250);
                }
            }
        }

        static void CreateShortcut(string lnk, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object link = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type lt = link.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Sync all your RGB lighting" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }

        static void TryDelete(string f)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static long DirSize(string dir)
        {
            long total = 0;
            try { foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length; } catch { }
            return total;
        }

        public static string LicenseText()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("License.txt"))
            using (StreamReader r = new StreamReader(s)) return r.ReadToEnd();
        }
    }

    public class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(14, 16, 21);
        static readonly Color Card = Color.FromArgb(24, 27, 35);
        static readonly Color Card2 = Color.FromArgb(34, 38, 48);
        static readonly Color Text1 = Color.FromArgb(236, 239, 246);
        static readonly Color Text2 = Color.FromArgb(146, 153, 170);
        static readonly Color Accent = Color.FromArgb(124, 92, 255);
        static readonly Color Warn = Color.FromArgb(255, 200, 90);

        class Step
        {
            public string Title, Subtitle, NextText;
            public Panel Panel;
        }

        readonly bool uninstallMode;
        readonly InstallOptions opts = InstallOptions.Defaults();
        readonly List<Step> steps = new List<Step>();
        int current;
        bool busy;

        Label titleLbl, subLbl;
        Button backBtn, nextBtn, cancelBtn;
        Panel body;
        RadioButton acceptRb;
        TextBox dirBox;
        Label spaceLbl;
        CheckBox desktopChk, startMenuChk, startupChk, engineChk, pawnChk, launchChk, deleteSettingsChk, removePawnChk;
        ProgressBar progress;
        Label progressLbl;
        TextBox progressLog;
        Label finishHead, finishLbl;
        string lastLogged;

        public SetupForm(bool uninstall)
        {
            uninstallMode = uninstall;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = uninstall ? "Uninstall RGB Unify" : "RGB Unify " + Installer.Version + " Setup";
            Icon = AppIcon.Create(32);
            BackColor = Bg;
            ForeColor = Text1;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(680, 500);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            Panel header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Card };
            header.Controls.Add(new GradientTitle { Location = new Point(24, 14), Size = new Size(400, 36) });
            titleLbl = new Label { Location = new Point(26, 52), AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Text1 };
            subLbl = new Label { Location = new Point(200, 54), Size = new Size(460, 20), ForeColor = Text2 };
            header.Controls.Add(titleLbl);
            header.Controls.Add(subLbl);

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Card };
            cancelBtn = MakeButton("Cancel", 560, 14, 100, Card2);
            nextBtn = MakeButton("Next >", 448, 14, 104, Accent);
            backBtn = MakeButton("< Back", 336, 14, 104, Card2);
            footer.Controls.Add(cancelBtn);
            footer.Controls.Add(nextBtn);
            footer.Controls.Add(backBtn);
            backBtn.Click += delegate { ShowStep(current - 1); };
            nextBtn.Click += delegate { OnNext(); };
            cancelBtn.Click += delegate { Close(); };
            AcceptButton = nextBtn;

            body = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(28, 20, 28, 12) };

            if (uninstall)
            {
                AddStep("Uninstall", "Remove RGB Unify from this PC", "Uninstall", BuildUninstallPage());
                AddStep("Removing", "Please wait while RGB Unify is removed", null, BuildProgressPage());
                AddStep("Finished", "", "Close", BuildFinishPage());
            }
            else
            {
                AddStep("Welcome", "", "Next >", BuildWelcomePage());
                AddStep("License Agreement", "Please read the following terms", "Next >", BuildLicensePage());
                AddStep("Install Location", "Where should RGB Unify be installed?", "Next >", BuildLocationPage());
                AddStep("Options", "Choose additional tasks", "Install", BuildOptionsPage());
                AddStep("Installing", "Please wait while RGB Unify is installed", null, BuildProgressPage());
                AddStep("Finished", "", "Finish", BuildFinishPage());
            }
            foreach (Step s in steps) { s.Panel.Dock = DockStyle.Fill; s.Panel.Visible = false; body.Controls.Add(s.Panel); }

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
            ShowStep(0);
        }

        void AddStep(string title, string sub, string nextText, Panel panel)
        {
            Step s = new Step();
            s.Title = title;
            s.Subtitle = sub;
            s.NextText = nextText;
            s.Panel = panel;
            steps.Add(s);
        }

        static Button MakeButton(string text, int x, int y, int w, Color back)
        {
            Button b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, 36), FlatStyle = FlatStyle.Flat,
                BackColor = back, ForeColor = Color.White, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 9.5f)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        static Label Para(string text, int y, int h)
        {
            return new Label { Text = text, Location = new Point(0, y), Size = new Size(620, h), ForeColor = Text1 };
        }

        CheckBox Check(Panel p, string text, string hint, int y, bool on)
        {
            CheckBox c = new CheckBox { Text = text, Location = new Point(0, y), Size = new Size(620, 24), Checked = on, ForeColor = Text1, Cursor = Cursors.Hand };
            p.Controls.Add(c);
            if (hint != null) p.Controls.Add(new Label { Text = hint, Location = new Point(18, y + 24), Size = new Size(600, 20), ForeColor = Text2 });
            return c;
        }

        // ---------- pages ----------

        Panel BuildWelcomePage()
        {
            Panel p = new Panel();
            string existing = Installer.ExistingInstallDir();
            p.Controls.Add(new Label
            {
                Text = existing != null ? "Update RGB Unify" : "Welcome to RGB Unify", Location = new Point(0, 0), AutoSize = true,
                Font = new Font("Segoe UI Semibold", 16f), ForeColor = Text1
            });
            string intro = existing != null
                ? "RGB Unify is already installed. Setup will update it to version " + Installer.Version + ". Your effects and settings are kept."
                : "This will install RGB Unify " + Installer.Version + " on your computer.";
            p.Controls.Add(Para(intro, 48, 44));
            p.Controls.Add(Para("RGB Unify finds the RGB lighting in your PC (motherboard, RAM, graphics card, fans, LED strips, " +
                "keyboard and mouse) and syncs it all to one effect. It can also remove conflicting RGB software.", 96, 44));
            p.Controls.Add(new Label
            {
                Text = "Tip: close other RGB apps (iCUE, Armoury Crate, MSI Center, SignalRGB...) before continuing. " +
                       "Two programs controlling the same lights makes them flicker.",
                Location = new Point(0, 152), Size = new Size(620, 44), ForeColor = Warn
            });
            p.Controls.Add(new Label { Text = "Click Next to continue, or Cancel to exit Setup.", Location = new Point(0, 220), Size = new Size(620, 22), ForeColor = Text2 });
            return p;
        }

        Panel BuildLicensePage()
        {
            Panel p = new Panel();
            TextBox box = new TextBox
            {
                Text = Installer.LicenseText().Replace("\r\n", "\n").Replace("\n", "\r\n"), ReadOnly = true, Multiline = true,
                Location = new Point(0, 0), Size = new Size(624, 232), BackColor = Card, ForeColor = Text1, BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f), ScrollBars = ScrollBars.Vertical, TabStop = false
            };
            p.Controls.Add(box);
            acceptRb = new RadioButton { Text = "I accept the agreement", Location = new Point(0, 246), Size = new Size(400, 24), ForeColor = Text1, Cursor = Cursors.Hand };
            RadioButton decline = new RadioButton { Text = "I do not accept the agreement", Location = new Point(0, 272), Size = new Size(400, 24), ForeColor = Text1, Checked = true, Cursor = Cursors.Hand };
            acceptRb.CheckedChanged += delegate { UpdateButtons(); };
            p.Controls.Add(acceptRb);
            p.Controls.Add(decline);
            return p;
        }

        Panel BuildLocationPage()
        {
            Panel p = new Panel();
            p.Controls.Add(Para("Setup will install RGB Unify into the following folder. To use a different folder, click Browse.", 0, 40));
            dirBox = new TextBox { Text = opts.Dir, Location = new Point(0, 52), Size = new Size(510, 26), BackColor = Card2, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle };
            dirBox.TextChanged += delegate { UpdateSpace(); };
            Button browse = MakeButton("Browse...", 520, 48, 104, Card2);
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "Choose where to install RGB Unify";
                    d.SelectedPath = Directory.Exists(dirBox.Text) ? dirBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = WithOwnFolder(d.SelectedPath);
                }
            };
            p.Controls.Add(dirBox);
            p.Controls.Add(browse);
            spaceLbl = new Label { Location = new Point(0, 96), Size = new Size(620, 44), ForeColor = Text2 };
            p.Controls.Add(spaceLbl);
            UpdateSpace();
            return p;
        }

        // Never install loose into a folder that already holds other things (e.g. "C:\Games").
        static string WithOwnFolder(string dir)
        {
            try
            {
                if (dir.TrimEnd('\\').EndsWith("RGB Unify", StringComparison.OrdinalIgnoreCase)) return dir;
                if (File.Exists(Path.Combine(dir, "RGBUnify.exe"))) return dir;
                if (!Directory.Exists(dir) || Directory.GetFileSystemEntries(dir).Length == 0) return dir;
            }
            catch { }
            return Path.Combine(dir, "RGB Unify");
        }

        void UpdateSpace()
        {
            string text = "Required: about 55 MB (including the RGB engine).";
            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(dirBox.Text));
                text += "\nAvailable on " + d.Name.TrimEnd('\\') + ": " + (d.AvailableFreeSpace / (1024L * 1024 * 1024)) + " GB.";
            }
            catch { }
            spaceLbl.Text = text;
        }

        Panel BuildOptionsPage()
        {
            Panel p = new Panel();
            desktopChk = Check(p, "Create a desktop shortcut", null, 0, true);
            startMenuChk = Check(p, "Create a Start menu shortcut", null, 30, true);
            startupChk = Check(p, "Start RGB Unify when Windows starts (recommended)", "Starts quietly in the tray so your lighting is set right after you sign in.", 60, true);
            engineChk = Check(p, "Download the RGB engine now (recommended)", "OpenRGB 1.1, about 22 MB from codeberg.org. Does the actual talking to your hardware.", 112, true);
            pawnChk = Check(p, "Install the PawnIO driver (recommended)", "A separate, signed kernel driver by namazso (pawnio.eu), installed with its own installer and listed on its own in Settings > Apps.", 164, true);
            p.Controls.Add(new Label
            {
                Text = "Needed for motherboard and most graphics card lighting (and RAM, which is opt-in). Without it RGB Unify still works for USB devices " +
                       "like keyboards and mice. If Setup installs it, uninstalling RGB Unify offers to remove it again.",
                Location = new Point(18, 210), Size = new Size(600, 40), ForeColor = Text2
            });
            return p;
        }

        void RefreshOptions()
        {
            bool haveEngine = Installer.EngineExists(dirBox.Text);
            engineChk.Enabled = !haveEngine;
            engineChk.Checked = !haveEngine;
            engineChk.Text = haveEngine ? "RGB engine is already downloaded" : "Download the RGB engine now (recommended)";
            bool havePawn = Engine.PawnIOInstalled();
            pawnChk.Enabled = !havePawn;
            pawnChk.Checked = !havePawn;
            pawnChk.Text = havePawn ? "PawnIO driver is already installed" : "Install the PawnIO driver (recommended)";
            if (Installer.ExistingInstallDir() != null) startupChk.Checked = Settings.StartupEnabled();
        }

        Panel BuildUninstallPage()
        {
            Panel p = new Panel();
            p.Controls.Add(new Label { Text = "Uninstall RGB Unify", Location = new Point(0, 0), AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Text1 });
            p.Controls.Add(Para("RGB Unify will be removed from:\n" + Installer.UninstallDir, 48, 44));
            p.Controls.Add(Para("Your lights will keep whatever colour they show now until another program or a restart changes them.", 100, 40));
            deleteSettingsChk = Check(p, "Also delete my settings and logs", null, 150, false);
            bool havePawn = Engine.PawnIOInstalled(), ours = Installer.WeInstalledPawnIO();
            removePawnChk = Check(p, "Also remove the PawnIO driver",
                !havePawn ? "PawnIO isn't installed." :
                ours ? "RGB Unify's setup installed it. Untick if another app (e.g. FanControl, LibreHardwareMonitor) uses it."
                     : "It was already on this PC before RGB Unify, so other apps probably use it. Left installed unless you tick this.",
                186, havePawn && ours);
            removePawnChk.Enabled = havePawn;
            return p;
        }

        Panel BuildProgressPage()
        {
            Panel p = new Panel();
            progressLbl = new Label { Location = new Point(0, 0), Size = new Size(624, 22), ForeColor = Text1, Text = "Starting..." };
            progress = new ProgressBar { Location = new Point(0, 30), Size = new Size(624, 18), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            progressLog = new TextBox
            {
                Location = new Point(0, 64), Size = new Size(624, 230), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Card, ForeColor = Text2, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f)
            };
            p.Controls.Add(progressLbl);
            p.Controls.Add(progress);
            p.Controls.Add(progressLog);
            return p;
        }

        Panel BuildFinishPage()
        {
            Panel p = new Panel();
            finishHead = new Label { Location = new Point(0, 0), AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Text1 };
            finishLbl = new Label { Location = new Point(0, 48), Size = new Size(620, 180), ForeColor = Text1 };
            p.Controls.Add(finishHead);
            p.Controls.Add(finishLbl);
            if (!uninstallMode) launchChk = Check(p, "Launch RGB Unify now", null, 240, true);
            return p;
        }

        // ---------- navigation ----------

        void ShowStep(int i)
        {
            if (i < 0 || i >= steps.Count) return;
            current = i;
            for (int k = 0; k < steps.Count; k++) steps[k].Panel.Visible = k == i;
            titleLbl.Text = steps[i].Title;
            subLbl.Text = steps[i].Subtitle;
            subLbl.Left = titleLbl.Right + 12;
            if (steps[i].Title == "Options") RefreshOptions();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            Step s = steps[current];
            bool last = current == steps.Count - 1;
            bool working = s.NextText == null;
            nextBtn.Text = s.NextText ?? "Next >";
            nextBtn.Enabled = !working && (s.Title != "License Agreement" || acceptRb.Checked);
            nextBtn.BackColor = nextBtn.Enabled ? Accent : Card2;
            backBtn.Visible = current > 0 && !working && !last;
            cancelBtn.Visible = !last;
            nextBtn.Left = last ? cancelBtn.Right - nextBtn.Width : cancelBtn.Left - 8 - nextBtn.Width;
            cancelBtn.Enabled = !working;
        }

        void OnNext()
        {
            string title = steps[current].Title;
            if (current == steps.Count - 1)
            {
                if (launchChk != null && launchChk.Visible && launchChk.Checked)
                {
                    try { Process.Start(Path.Combine(opts.Dir, "RGBUnify.exe")); } catch { }
                }
                busy = false;
                Close();
                return;
            }
            if (title == "Install Location")
            {
                string dir = dirBox.Text.Trim();
                if (dir.Length < 4 || !Path.IsPathRooted(dir))
                {
                    MessageBox.Show(this, "Please choose a full folder path, for example C:\\Program Files\\RGB Unify.", "RGB Unify Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                dirBox.Text = WithOwnFolder(dir);
                opts.Dir = dirBox.Text;
            }
            if (title == "Options")
            {
                opts.DesktopShortcut = desktopChk.Checked;
                opts.StartMenuShortcut = startMenuChk.Checked;
                opts.StartWithWindows = startupChk.Checked;
                opts.DownloadEngine = engineChk.Enabled && engineChk.Checked;
                opts.InstallPawnIO = pawnChk.Enabled && pawnChk.Checked;
            }
            ShowStep(current + 1);
            if (steps[current].NextText == null) RunWork();
        }

        void RunWork()
        {
            busy = true;
            Action<string, int> report = delegate (string stepText, int pct) { BeginInvoke(new Action(delegate { OnProgress(stepText, pct); })); };
            Thread t = new Thread(delegate ()
            {
                List<string> warnings = new List<string>();
                string error = null;
                try
                {
                    if (uninstallMode) Installer.Uninstall(Installer.UninstallDir, deleteSettingsChk.Checked, removePawnChk.Checked, report);
                    else warnings = Installer.Install(opts, report);
                }
                catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate { OnWorkDone(warnings, error); }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void OnProgress(string stepText, int pct)
        {
            if (stepText != lastLogged)
            {
                lastLogged = stepText;
                progressLog.AppendText((progressLog.TextLength > 0 ? "\r\n" : "") + stepText + "...");
            }
            progressLbl.Text = stepText + (pct >= 0 && pct < 100 ? "... " + pct + "%" : "...");
            if (pct >= 0)
            {
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = Math.Max(0, Math.Min(100, pct));
            }
            else progress.Style = ProgressBarStyle.Marquee;
        }

        void OnWorkDone(List<string> warnings, string error)
        {
            busy = false;
            ShowStep(current + 1);
            if (error != null)
            {
                finishHead.Text = uninstallMode ? "Uninstall failed" : "Setup failed";
                finishLbl.Text = "Something went wrong:\n" + error + "\n\nMake sure RGB Unify isn't running and try again.";
                finishLbl.ForeColor = Warn;
                if (launchChk != null) launchChk.Visible = false;
                return;
            }
            if (uninstallMode)
            {
                finishHead.Text = "RGB Unify was removed";
                finishLbl.Text = "RGB Unify has been uninstalled from your computer.\n\nThanks for trying it!";
                return;
            }
            finishHead.Text = "RGB Unify is installed";
            string text = "Setup has finished installing RGB Unify on your computer. You can open it from the Start menu" +
                          (opts.DesktopShortcut ? " or the desktop shortcut" : "") + ".";
            if (warnings.Count > 0)
            {
                text += "\n\nSome optional steps didn't complete:";
                foreach (string w in warnings) text += "\n• " + w;
                finishLbl.ForeColor = Warn;
            }
            finishLbl.Text = text;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            if (current < steps.Count - 1 && e.CloseReason == CloseReason.UserClosing)
            {
                string q = uninstallMode ? "Cancel uninstalling RGB Unify?" : "Are you sure you want to cancel RGB Unify Setup?";
                if (MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }
    }

    // App icon + "RGB Unify" in the same gradient as the main window header.
    class GradientTitle : Control
    {
        public GradientTitle()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI Semibold", 15f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int s = Height - 8;
            using (Icon ic = AppIcon.Create(32)) g.DrawIcon(ic, new Rectangle(0, 4, s, s));
            SizeF sz = g.MeasureString("RGB Unify", Font);
            RectangleF r = new RectangleF(s + 8, 0, Math.Max(1, sz.Width), Height);
            using (LinearGradientBrush br = new LinearGradientBrush(r, Color.FromArgb(255, 90, 140), Color.FromArgb(80, 170, 255), 0f))
            {
                ColorBlend cb = new ColorBlend();
                cb.Colors = new[] { Color.FromArgb(255, 95, 130), Color.FromArgb(170, 110, 255), Color.FromArgb(70, 180, 255) };
                cb.Positions = new[] { 0f, 0.5f, 1f };
                br.InterpolationColors = cb;
                g.DrawString("RGB Unify", Font, br, s + 8, (Height - sz.Height) / 2);
            }
        }
    }
}
