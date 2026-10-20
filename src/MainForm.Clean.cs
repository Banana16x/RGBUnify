using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RGBUnify
{
    // Clean Up tab: find and pause/remove other RGB software that fights over the lights.
    public partial class MainForm
    {
        ListView conflictList;
        TextBox cleanLog;
        CheckBox restoreChk;
        Button scanBtn, stopBtn, removeBtn;
        List<Conflict> conflicts = new List<Conflict>();

        Control BuildCleanPage()
        {
            Panel outer = new Panel { BackColor = Bg, Padding = new Padding(8) };
            Panel card = new Panel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(20, 14, 20, 16) };
            outer.Controls.Add(card);

            Panel top = new Panel { Dock = DockStyle.Top, Height = 136, BackColor = Card };
            Label title = new Label { Text = "Remove conflicting RGB software", AutoSize = true, Location = new Point(0, 2), Font = new Font("Segoe UI Semibold", 15f), ForeColor = Text1 };
            top.Controls.Add(title);
            AddHelp(top, TextRenderer.MeasureText(title.Text, title.Font).Width + 4, 9,
                "Other RGB apps (iCUE, MSI Center, Armoury Crate, SignalRGB...) fight\n" +
                "over the same lights and cause flicker.\n\n" +
                "• Pause selected: stops their background services/processes\n" +
                "  until you turn them back on.\n" +
                "• Uninstall selected: removes them for good.\n\n" +
                "Leftover drivers are unticked by default; tick them to remove too.");
            Label desc = new Label
            {
                Text = "Apps like iCUE, MSI Center / Mystic Light, Armoury Crate, Synapse, SignalRGB and others fight over the same lights and cause flicker. " +
                       "Tick what you want gone. \"Pause\" stops them until you turn them back on; \"Uninstall\" removes them for good, then finds and removes their leftovers (folders, registry, tasks), like Revo Uninstaller.",
                Location = new Point(0, 44), Size = new Size(600, 40), ForeColor = Text2
            };
            top.Controls.Add(desc);
            top.Resize += delegate { desc.Width = Math.Max(200, top.ClientSize.Width); };
            scanBtn = MakeButton("Scan again", 0, 92, 130, Card2);
            scanBtn.Click += delegate { RunScan(); };
            stopBtn = MakeButton("Pause selected", 140, 92, 150, Card2);
            stopBtn.Click += delegate { RunCleanup(false); };
            removeBtn = MakeButton("Uninstall selected", 300, 92, 170, Danger);
            removeBtn.Click += delegate { RunCleanup(true); };
            restoreChk = new CheckBox { Text = "Create a restore point first (recommended)", Checked = true, Location = new Point(490, 100), AutoSize = true, ForeColor = Text2 };
            top.Controls.Add(scanBtn); top.Controls.Add(stopBtn); top.Controls.Add(removeBtn); top.Controls.Add(restoreChk);

            cleanLog = new TextBox
            {
                Dock = DockStyle.Bottom, Height = 170, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Card2, ForeColor = Text2, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f)
            };
            Panel gap = new Panel { Dock = DockStyle.Bottom, Height = 12, BackColor = Card };

            conflictList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
                BackColor = Card2, ForeColor = Text1, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable, OwnerDraw = true
            };
            conflictList.Columns.Add("Name", 360);
            conflictList.Columns.Add("Brand", 140);
            conflictList.Columns.Add("Type", 150);
            conflictList.Columns.Add("Notes", 420);
            conflictList.DrawColumnHeader += delegate (object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Card)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Text2, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            // Last column fills the rest, so no unpainted header strip is left on the right.
            conflictList.Resize += delegate
            {
                int used = 0;
                for (int i = 0; i < conflictList.Columns.Count - 1; i++) used += conflictList.Columns[i].Width;
                conflictList.Columns[conflictList.Columns.Count - 1].Width = Math.Max(120, conflictList.ClientSize.Width - used - 24);
            };
            DarkScroll(conflictList);
            DarkHeader(conflictList);
            DarkScroll(cleanLog);
            conflictList.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            conflictList.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };

            card.Controls.Add(conflictList);
            card.Controls.Add(gap);
            card.Controls.Add(cleanLog);
            card.Controls.Add(top);
            return outer;
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            if (cleanLog.IsDisposed) return;
            cleanLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        void SetCleanBusy(bool busy)
        {
            scanBtn.Enabled = stopBtn.Enabled = removeBtn.Enabled = !busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }

        void RunScan()
        {
            RunScan(false);
        }

        // afterUninstall: the Revo-style second pass - offer to remove whatever the uninstallers left behind.
        void RunScan(bool afterUninstall)
        {
            SetCleanBusy(true);
            conflictList.Items.Clear();
            Log(afterUninstall ? "Scanning for leftovers (files, registry, tasks)..." : "Scanning for RGB software, services, drivers and leftovers...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Conflict> found = ConflictScanner.Scan();
                BeginInvoke(new Action(delegate
                {
                    if (!uiBuilt) return; // window was closed to the tray meanwhile
                    conflicts = found;
                    conflictList.Items.Clear();
                    int leftovers = 0;
                    foreach (Conflict c in found)
                    {
                        ListViewItem it = new ListViewItem(new[] { c.Name, c.Brand, c.KindText, c.Note });
                        it.Tag = c;
                        // Drivers and the system-wide BIOS reinstall switch are opt-in.
                        it.Checked = c.Kind != ConflictKind.Driver && c.Kind != ConflictKind.Reinstall;
                        conflictList.Items.Add(it);
                        if (c.IsLeftover) leftovers++;
                    }
                    Log(found.Count == 0 ? "All clear — no conflicting RGB software or leftovers found." :
                        "Found " + found.Count + " item(s)" + (leftovers > 0 ? ", including " + leftovers + " leftover(s)" : "") +
                        ". Drivers and the BIOS reinstall switch are unticked by default; tick them to include them.");
                    SetCleanBusy(false);
                    if (afterUninstall && leftovers > 0 &&
                        MessageBox.Show(this, "The uninstallers left " + leftovers + " thing(s) behind (folders, registry keys, scheduled tasks).\n\n" +
                            "Remove them too? Folders go to the Recycle Bin and registry keys are backed up first.",
                            "Leftovers found", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        foreach (ListViewItem it in conflictList.Items) it.Checked = ((Conflict)it.Tag).IsLeftover;
                        RunCleanup(true, false);
                    }
                }));
            });
        }

        void RunCleanup(bool uninstall)
        {
            RunCleanup(uninstall, true);
        }

        void RunCleanup(bool uninstall, bool ask)
        {
            List<Conflict> sel = new List<Conflict>();
            foreach (ListViewItem it in conflictList.Items) if (it.Checked) sel.Add((Conflict)it.Tag);
            if (sel.Count == 0) { Log("Nothing selected."); return; }

            bool programs = false;
            foreach (Conflict c in sel) if (c.Kind == ConflictKind.App || c.Kind == ConflictKind.StoreApp) programs = true;
            if (uninstall && ask)
            {
                bool peripherals = false, asusLaptop = false;
                foreach (Conflict c in sel)
                {
                    if (c.Note.Contains("keyboard/mouse")) peripherals = true;
                    if (c.Note.Contains("G-Helper")) asusLaptop = true;
                }
                string msg = "Remove " + sel.Count + " item(s)?\n\nSome vendor uninstallers open their own window — just click through them." +
                             (peripherals ? "\n\nNote: some of these also store mouse DPI / keyboard macros. Those settings usually stay saved on the device itself." : "") +
                             (asusLaptop ? "\n\nASUS laptop: Armoury Crate also handles fan/performance modes, GPU mode and the battery limit. " +
                                           "G-Helper (free, github.com/seerge/g-helper) can do those instead. The ASUS drivers your Fn keys need are kept." : "") +
                             "\n\nAfterwards RGB Unify scans for leftovers, like Revo Uninstaller. A restart is recommended.";
                if (MessageBox.Show(this, msg, "RGB Unify", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            SetCleanBusy(true);
            bool restore = restoreChk.Checked && programs;
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (uninstall && restore)
                {
                    Log("Creating restore point (can take a minute)...");
                    Log(ConflictScanner.CreateRestorePoint());
                }
                // Close running apps and services first so uninstallers don't fail on locked files.
                foreach (Conflict c in sel)
                    if (c.Kind == ConflictKind.Process || c.Kind == ConflictKind.Service ||
                        (!uninstall && (c.Kind == ConflictKind.Task || c.Kind == ConflictKind.Reinstall)))
                    {
                        string r = ConflictScanner.StopNow(c);
                        if (r != null) Log(r);
                    }
                if (uninstall)
                {
                    // Order matters: the vendor's own uninstaller first, then whatever it doesn't clean up.
                    ConflictKind[] order = { ConflictKind.App, ConflictKind.StoreApp, ConflictKind.Service, ConflictKind.Driver,
                                             ConflictKind.Task, ConflictKind.Folder, ConflictKind.RegKey, ConflictKind.Reinstall };
                    foreach (ConflictKind kind in order)
                        foreach (Conflict c in sel)
                        {
                            if (c.Kind != kind) continue;
                            if (kind == ConflictKind.App || kind == ConflictKind.StoreApp) Log("Uninstalling " + c.Name + "...");
                            Log(ConflictScanner.Remove(c));
                        }
                }
                else
                {
                    foreach (Conflict c in sel)
                        if (c.Kind == ConflictKind.App || c.Kind == ConflictKind.StoreApp || c.Kind == ConflictKind.Folder || c.Kind == ConflictKind.RegKey)
                            Log(c.Name + ": can't be paused, only uninstalled (its services, processes and tasks are listed separately).");
                }
                Log(uninstall ? "Done. Restart your PC to finish, then RGB Unify will have full control." : "Done. Paused items stay off until you re-enable them.");
                BeginInvoke(new Action(delegate
                {
                    engine.Rescan();
                    if (!uiBuilt) return;
                    SetCleanBusy(false);
                    RunScan(uninstall && programs);
                }));
            });
        }
    }
}
