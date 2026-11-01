using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace RGBUnify
{
    // Translations. The code is written in English; every control's text is translated as it's set (Hook), so
    // screens don't need to know about languages. Lists are embedded text files: "English<TAB>Translation" per line,
    // with \n for line breaks. Built-up texts ("Found 3 item(s)") are translated piece by piece.
    public static class Lang
    {
        public static readonly string[] Codes = { "", "en", "fr" };
        public static readonly string[] Names = { "Automatic (Windows language)", "English", "Français" };

        public static string Current = "en";
        static Dictionary<string, string> exact = new Dictionary<string, string>();
        static List<KeyValuePair<string, string>> pieces = new List<KeyValuePair<string, string>>();

        // "" = follow Windows.
        public static void Set(string code)
        {
            if (string.IsNullOrEmpty(code)) code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            exact = new Dictionary<string, string>();
            pieces = new List<KeyValuePair<string, string>>();
            Current = "en";
            if (code == "en") return;
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(code + ".txt");
            if (s == null) return;
            Current = code;
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || line.StartsWith("#")) continue;
                    string en = Unescape(line.Substring(0, tab)), tr = Unescape(line.Substring(tab + 1));
                    if (tr.Length == 0 || en == tr) continue;
                    exact[en] = tr;
                    // Pieces of built-up texts start or end with a space or punctuation, or are whole phrases.
                    char a = en[0], z = en[en.Length - 1];
                    if (en.Length >= 3 && (en.Contains(" ") || !char.IsLetterOrDigit(a) || !char.IsLetterOrDigit(z)) && (en.Length >= 12 || !char.IsLetterOrDigit(a) || !char.IsLetterOrDigit(z)))
                        pieces.Add(new KeyValuePair<string, string>(en, tr));
                }
            }
            pieces.Sort(delegate(KeyValuePair<string, string> x, KeyValuePair<string, string> y) { return y.Key.Length.CompareTo(x.Key.Length); });
        }

        static string Unescape(string s) { return s.Replace("\\n", "\n").Replace("\\t", "\t"); }

        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || exact.Count == 0) return s;
            string t;
            if (exact.TryGetValue(s, out t)) return t;
            string trimmed = s.Trim();
            if (trimmed.Length != s.Length && exact.TryGetValue(trimmed, out t)) return s.Replace(trimmed, t);
            // Built-up text: swap each known piece. Each spot is translated once, so a translation is never re-translated.
            StringBuilder sb = new StringBuilder();
            int i = 0;
            bool changed = false;
            while (i < s.Length)
            {
                bool hit = false;
                foreach (KeyValuePair<string, string> p in pieces)
                    if (string.CompareOrdinal(s, i, p.Key, 0, p.Key.Length) == 0)
                    {
                        sb.Append(p.Value);
                        i += p.Key.Length;
                        hit = changed = true;
                        break;
                    }
                if (!hit) sb.Append(s[i++]);
            }
            return changed ? sb.ToString() : s;
        }

        // Translates a control and everything added to it later, including text changed later on.
        public static void Hook(Control c)
        {
            if (c == null) return;
            Translate(c);
            c.TextChanged += OnTextChanged;
            c.ControlAdded += OnControlAdded;
            foreach (Control child in c.Controls) Hook(child);
        }

        static bool busy;

        static void OnTextChanged(object sender, EventArgs e)
        {
            if (busy) return;
            Translate((Control)sender);
        }

        static void OnControlAdded(object sender, ControlEventArgs e) { Hook(e.Control); }

        static void Translate(Control c)
        {
            // Typed-in text is never touched.
            if (exact.Count == 0 || c is TextBoxBase || c is NumericUpDown) return;
            busy = true;
            try
            {
                if (!(c is Form) || c.Text != "RGB Unify")
                {
                    string t = T(c.Text);
                    if (t != c.Text) c.Text = t;
                }
                ComboBox cb = c as ComboBox;
                if (cb != null)
                    for (int i = 0; i < cb.Items.Count; i++)
                    {
                        string item = cb.Items[i] as string;
                        if (item != null && T(item) != item) cb.Items[i] = T(item);
                    }
                ToolStrip ts = c as ToolStrip;
                if (ts != null) Translate(ts.Items);
            }
            finally { busy = false; }
        }

        public static DialogResult Box(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return owner == null ? MessageBox.Show(T(text), T(caption), buttons, icon) : MessageBox.Show(owner, T(text), T(caption), buttons, icon);
        }

        public static DialogResult Box(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton def)
        {
            return MessageBox.Show(owner, T(text), T(caption), buttons, icon, def);
        }

        public static DialogResult Box(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Box(null, text, caption, buttons, icon);
        }

        public static void Translate(ToolStripItemCollection items)
        {
            if (exact.Count == 0) return;
            foreach (ToolStripItem it in items)
            {
                it.Text = T(it.Text);
                ToolStripDropDownItem dd = it as ToolStripDropDownItem;
                if (dd != null) Translate(dd.DropDownItems);
            }
        }
    }
}
