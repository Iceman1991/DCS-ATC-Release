using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace DcsAtc;

/// Settings window (DcsAtc --settings) in the radio wheel look: sidebar with one page per topic, writes config.jsonc (comments stay).
/// The running app applies callsign, volume, pace and radio options immediately (Program.ReloadLive).
static class Settings
{
    /// Ko-fi supporters, named on the "Thanks" page (only with their OK, under the name they want)
    static readonly string[] Supporters = { "Heling", "buur", "Pascali Alessandro" };

    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    static readonly Color Box = Color.FromArgb(30, 36, 44), Line = Color.FromArgb(70, 78, 88);   // Input fields, frame

    public static void Show(Config cfg)
    {
        FreeConsole();   // window only, no console window beside it (start menu)
        Sta(() => Application.Run(Build(cfg, true)));
    }

    /// Preview as PNG (DcsAtc --settings-png datei.png [de|en] [page]): draw the window off-screen.
    public static void Snapshot(Config cfg, string file, int page = 0) => Sta(() =>
    {
        using var f = Build(cfg);
        (f.StartPosition, f.Location, f.ShowInTaskbar, f.TopMost) = (FormStartPosition.Manual, new Point(-20000, -20000), false, false);
        f.Show();
        ((Action<int>)f.Tag!)(page);
        Application.DoEvents();
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
        bmp.Save(file);
        Console.WriteLine($"{file}: {f.Width} x {f.Height}");
    });

    static void Sta(Action a)
    {
        var t = new Thread(() => { Application.EnableVisualStyles(); a(); });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }

    /// "Parent": { ... } holds keys that also exist elsewhere (Tower in Frequencies and Voices): change only inside that block; block missing: insert it whole.
    internal static string WithNested(string c, string parent, IEnumerable<(string Key, string Value)> changed, string full)
    {
        if (!changed.Any()) return c;
        var m = Regex.Match(c, $@"""{parent}""\s*:\s*\{{[^}}]*\}}");   // ponytail: flat block, no "}" in its comments; deeper nesting needs a JSONC parser
        if (!m.Success) return Program.WithConfigValue(c, parent, full, "");
        var block = changed.Aggregate(m.Value, (b, x) => Program.WithConfigValue(b, x.Key, x.Value, ""));
        return c[..m.Index] + block + c[(m.Index + m.Length)..];
    }

    /// Voice value "a@0.94|b" as (a, "0.94"), (b, "1.00") – the pitch the way the list shows it – and back.
    internal static (string Model, string Pitch)[] VoiceParts(string v) => v.Split('|').Select(x => x.Split('@')).Select(x =>
        (x[0], (x.Length > 1 && double.TryParse(x[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 1).ToString("0.00", CultureInfo.InvariantCulture))).ToArray();
    internal static string VoiceText(IEnumerable<(string Model, string Pitch)> parts) =>
        string.Join("|", parts.Select(x => x.Model + (x.Pitch == "1.00" ? "" : "@" + x.Pitch.TrimEnd('0').TrimEnd('.'))));

    /// live: the real window (asks GitHub for a newer version when shown), not the off-screen preview.
    static Form Build(Config cfg, bool live = false)
    {
        var wa = Screen.PrimaryScreen!.WorkingArea.Size;
        var f = new Form
        {
            Text = "DCS-ATC – " + L("Einstellungen", "Settings"), Font = Wheel.F(Wheel.Font1, 10f), BackColor = Wheel.Hub, ForeColor = Wheel.Ink,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            TopMost = true,   // from the radio wheel over DCS
            ClientSize = new Size(Math.Min(836, wa.Width - 40), Math.Min(660, wa.Height - 60)),   // too small: the page scrolls
        };
        f.HandleCreated += (_, _) => { int on = 1; DwmSetWindowAttribute(f.Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref on, 4); };
        f.Shown += (_, _) =>   // always to the front (from wheel, start menu, setup), not behind DCS: the wheel never activates itself, then Windows blocks SetForegroundWindow – attaching to the input thread of the foreground window lifts the lock
        {
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
            bool att = fg != 0 && fg != me && AttachThreadInput(me, fg, true);
            BringWindowToTop(f.Handle); SetForegroundWindow(f.Handle); f.Activate();
            if (att) AttachThreadInput(me, fg, false);
        };

        // Sidebar on the left (search, one button per page), pages on the right, Save/Cancel below
        var nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 190, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Wheel.Seg, Padding = new Padding(8, 14, 8, 8) };
        var host = new Panel { Dock = DockStyle.Fill };
        var pages = new List<FlowLayoutPanel>();
        var tabs = new List<Tab>();
        var find = new List<(Control C, int Page)>();   // what the search looks through: row labels and checkboxes
        int sel = 0;
        string q = "";
        bool Hit(Control c) => c.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        void Mark()   // show page sel; search: hits in accent color, pages without a hit dimmed
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                pages[i].Visible = i == sel;
                bool hit = q == "" || find.Any(x => x.Page == i && Hit(x.C));
                (tabs[i].BackColor, tabs[i].ForeColor) = (i == sel ? Box : Wheel.Seg, i == sel ? Wheel.Accent : hit ? Wheel.Ink : Line);
            }
            foreach (var (c, _) in find) c.ForeColor = q != "" && Hit(c) ? Wheel.Accent : Wheel.Ink;
        }
        var search = new TextBox { Width = 174, BackColor = Box, ForeColor = Wheel.Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = L("Suchen", "Search"), Margin = new Padding(0, 0, 0, 12) };
        search.TextChanged += (_, _) =>
        {
            q = search.Text.Trim();
            if (q != "" && !find.Any(x => x.Page == sel && Hit(x.C))) sel = find.Where(x => Hit(x.C)).Select(x => x.Page).DefaultIfEmpty(sel).First();
            Mark();
        };
        nav.Controls.Add(search);

        FlowLayoutPanel cur = null!;
        TableLayoutPanel grid = null!;
        void Page(string title)
        {
            int i = pages.Count;
            cur = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18, 12, 18, 12), Visible = false };
            cur.Controls.Add(new Label { Text = title, Font = Wheel.F(Wheel.Font1, 15f, FontStyle.Bold), AutoSize = true, UseMnemonic = false, Margin = Padding.Empty });
            var b = new Tab { Text = title, Size = new Size(174, 34), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, 0, 0, 2),
                                 Cursor = Cursors.Hand, UseMnemonic = false };
            (b.FlatAppearance.BorderSize, b.FlatAppearance.MouseOverBackColor, b.FlatAppearance.MouseDownBackColor) = (0, Box, Box);
            b.Click += (_, _) => { sel = i; Mark(); };
            pages.Add(cur); tabs.Add(b); host.Controls.Add(cur); nav.Controls.Add(b);
        }
        // Group: heading in accent color, below it tile like a wheel segment
        void Group(string title)
        {
            cur.Controls.Add(new Label { Text = title.ToUpperInvariant(), Font = Wheel.F(Wheel.Font1, 11f, FontStyle.Bold), ForeColor = Wheel.Accent, AutoSize = true, UseMnemonic = false,
                                         Margin = new Padding(0, 14, 0, 5) });
            grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Wheel.Seg, Padding = new Padding(14, 8, 14, 8), Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
            var g = grid;
            g.Paint += (_, e) => { using var p = new Pen(Line); e.Graphics.DrawRectangle(p, 0, 0, g.Width - 1, g.Height - 1); };
            cur.Controls.Add(grid);
        }
        void Wide(Control c) { grid.Controls.Add(c); grid.SetColumnSpan(c, 2); }
        void Row(string label, Control c)
        {
            var l = new Label { Text = label, AutoSize = true, MaximumSize = new Size(240, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6), UseMnemonic = false };
            grid.Controls.Add(l);
            find.Add((l, pages.Count - 1));
            c.Anchor = AnchorStyles.Left; c.Margin = new Padding(0, 5, 0, 5);
            grid.Controls.Add(c);
        }
        CheckBox Check(string text, bool on)
        {
            var b = new CheckBox { Text = text, Checked = on, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 6, 0, 6), UseMnemonic = false };
            (b.FlatAppearance.BorderColor, b.FlatAppearance.CheckedBackColor) = (Wheel.Grey, Box);
            Wide(b);
            find.Add((b, pages.Count - 1));
            return b;
        }
        void Note(string text) => Wide(new Label { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Wheel.Grey, Margin = new Padding(0, 4, 0, 2), UseMnemonic = false });
        TextBox Field(string text, int width = 290, string hint = "") =>
            new() { Text = text, Width = width, BackColor = Box, ForeColor = Wheel.Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = hint };
        ComboBox Combo(string[] items, int sel)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, FlatStyle = FlatStyle.Flat, BackColor = Box, ForeColor = Wheel.Ink, DrawMode = DrawMode.OwnerDrawFixed };
            c.DrawItem += (_, e) =>   // List dark, selection in accent color
            {
                if (e.Index < 0) return;
                bool hi = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using var b = new SolidBrush(hi ? Wheel.Accent : Box);
                e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, c.Items[e.Index]!.ToString(), c.Font, e.Bounds, hi ? Wheel.Dark : Wheel.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            c.Items.AddRange(items);
            if (items.Length > 0) c.SelectedIndex = Math.Max(0, sel);
            return c;
        }
        Button Btn(string text, bool main)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = main ? Wheel.Accent : Wheel.Seg, ForeColor = main ? Wheel.Dark : Wheel.Accent,
                                 Font = Wheel.F(Wheel.Font1, 10f, FontStyle.Bold), Padding = new Padding(10, 3, 10, 3), Cursor = Cursors.Hand };
            (b.FlatAppearance.BorderColor, b.FlatAppearance.BorderSize) = (Wheel.Accent, 1);
            return b;
        }
        void Notepad(string file) => Process.Start("notepad.exe", $"\"{file}\"");
        string D(double v) => v.ToString(CultureInfo.InvariantCulture);
        string J(object v) => JsonSerializer.Serialize(v);

        Page(L("Allgemein", "General"));
        Group(L("Rufzeichen & Sprache", "Callsign & language"));
        var cs = Field(cfg.MyCallsign, hint: L("leer = Rufzeichen aus dem DCS-Slot", "empty = callsign from the DCS slot"));
        Row(L("Eigenes Rufzeichen", "Your callsign"), cs);
        var unitCs = Check(L("Rufzeichen aus dem Unitnamen der Mission (z. B. VICTORY 9-1)", "Callsign from the mission unit name (e.g. VICTORY 9-1)"), cfg.CallsignFromUnit);
        var lang = Combo(new[] { "Deutsch", "English" }, En ? 1 : 0);
        Row(L("Sprache der Oberfläche *", "Interface language *"), lang);
        Group("Cockpit");
        string[] units = { "auto", "hpa", "inhg" };
        var unit = Combo(new[] { L("automatisch (nach Flugzeugtyp)", "automatic (by aircraft type)"), "hPa (QNH)", "inHg (altimeter)" }, Math.Max(0, Array.IndexOf(units, cfg.AltimeterUnit.ToLowerInvariant())));
        Row(L("Höhenmesser-Einstellung", "Altimeter setting"), unit);
        var knee = Check(L("Kneeboard-Seiten bei Missionsstart schreiben *", "Write kneeboard pages at mission start *"), cfg.KneeboardPages);
        var f10 = Check(L("Kontrollzonen, Meldepunkte und Routen auf die F10-Karte zeichnen *", "Draw control zones, reporting points and routes on the F10 map *"), cfg.F10Drawings);
        // Update from the GitHub releases: look when the window opens (Config.UpdateCheck) or on the button; download and setup only on the button
        Group("Update");
        var updState = new Label { Text = L($"Installiert: {Update.Own}", $"Installed: {Update.Own}"), AutoSize = true, MaximumSize = new Size(285, 0), UseMnemonic = false };
        Row("Version", updState);
        var updBtn = Btn(L("Nach Updates suchen", "Check for updates"), false);
        Row("Update", updBtn);
        var updAuto = Check(L("Beim Start nach einer neuen Version suchen", "Look for a new version at start"), cfg.UpdateCheck);
        Update.Release? upd = null;
        bool updBusy = false;
        void UpdUi(Action a) { if (f.IsHandleCreated && !f.IsDisposed) f.BeginInvoke(a); }
        void UpdCheck()
        {
            if (updBusy) return;
            updBusy = true;
            updState.Text = L("Suche nach Updates …", "Checking for updates …");
            Task.Run(() =>
            {
                var r = Update.Check();
                UpdUi(() =>
                {
                    (updBusy, upd) = (false, r);
                    updState.Text = r == null ? L($"Installiert: {Update.Own}. Nichts Neueres gefunden.", $"Installed: {Update.Own}. Nothing newer found.")
                                              : L($"Installiert: {Update.Own}. Neu: {r.Version} ({r.Size / 1048576} MB).", $"Installed: {Update.Own}. New: {r.Version} ({r.Size / 1048576} MB).");
                    if (r != null) (updBtn.Text, updBtn.BackColor, updBtn.ForeColor) = (L("Jetzt aktualisieren", "Update now"), Wheel.Accent, Wheel.Dark);
                });
            });
        }
        updBtn.Click += (_, _) =>
        {
            if (updBusy) return;
            if (upd is not { } r) { UpdCheck(); return; }
            if (Program.DcsProcs.Any(n => Process.GetProcessesByName(n).Length > 0))   // setup replaces the running app and may ask for admin rights: not during a flight
            {
                updState.Text = L("DCS läuft. Erst DCS beenden, dann aktualisieren.", "DCS is running. Close DCS first, then update.");
                return;
            }
            updBusy = true;
            Task.Run(() =>
            {
                try
                {
                    int last = -1;
                    var setup = Update.Download(r, p => { int pc = (int)(p * 100); if (pc != last) { last = pc; UpdUi(() => updState.Text = L($"Lade {r.Version}: {pc} %", $"Downloading {r.Version}: {pc} %")); } });
                    Update.Install(setup);
                    Environment.Exit(0);   // setup replaces this exe and opens the settings window again
                }
                catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    UpdUi(() => { updBusy = false; updState.Text = L($"Update fehlgeschlagen: {e.Message}", $"Update failed: {e.Message}"); });
                }
            });
        };
        if (live && cfg.UpdateCheck) f.Shown += (_, _) => UpdCheck();

        Page(L("Stimmen", "Voices"));
        Group(L("Lotsen-Stimmen", "Controller voices"));
        var vol = new Slider(0, 100, 5, Math.Round(Math.Clamp(cfg.Volume * 100, 0, 100)), "0' %'");
        Row(L("Lautstärke Lotsen", "Controller volume"), vol);
        var rate = new Slider(0.7, 1.5, 0.05, cfg.SpeechRate, "0.00");
        Row(L("Sprechtempo (1 = normal, größer = langsamer)", "Speech pace (1 = normal, higher = slower)"), rate);
        var pause = new Slider(0, 5, 0.5, cfg.ReplyPause, "0.0' s'");
        Row(L("Pause zwischen Funksprüchen", "Pause between calls"), pause);
        var fx = Check(L("Funkklang (Rauschen, Squelch)", "Radio effect (noise, squelch)"), cfg.RadioFx);
        var rtx = Check(L("Funksprüche auch als Text im Spiel", "Radio calls also as text in the game"), cfg.RadioText);
        var htx = Check(L("Hinweise von DCS-ATC als Text im Spiel (Fehler, Landebewertung, Verstöße)", "Notes from DCS-ATC as text in the game (errors, landing grade, deviations)"), cfg.HintText);
        var mdir = Path.Combine(Path.GetDirectoryName(Program.ConfigPath)!, "models");
        var models = Directory.Exists(mdir) ? Directory.GetFiles(mdir, "*.onnx").Select(m => Path.GetFileNameWithoutExtension(m)).ToArray() : Array.Empty<string>();
        var std = new Config().Voices;
        var known = models.Concat(cfg.Voices.Values.Concat(std.Values).Append(cfg.PilotVoice).SelectMany(VoiceParts).Select(p => p.Model))   // also voices named in the file that are not installed
                          .Where(m => m != "" && m != "random").Distinct().ToArray();
        string[] Names(string first) => known.Select(m => Regex.Replace(m, @"^[a-z]+_([A-Z]+)-(.+?)(-medium)?$", "$2 ($1)") + (models.Contains(m) ? "" : L(" – fehlt", " – missing"))).Prepend(first).ToArray();
        var pilot = cfg.PilotVoice.Split('@')[0];
        var pv = Combo(Names(L("aus", "off")), 1 + Array.IndexOf(known, pilot));
        Row(L("Pilotenstimme (spricht eigene Anfragen vor)", "Pilot voice (speaks your wheel requests)"), pv);

        Group(L("Stimme je Station *", "Voice per station *"));
        var vt = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, Margin = Padding.Empty };
        foreach (var w in new[] { 84, 150, 64, 150, 64, 28 }) vt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
        foreach (var (h, i) in new[] { "", L("Stimme", "Voice"), L("Tonhöhe", "Pitch"), L("Zweite Stimme", "Second voice"), L("Tonhöhe", "Pitch") }.Select((h, i) => (h, i)))
            vt.Controls.Add(new Label { Text = h, AutoSize = true, ForeColor = Wheel.Grey, Margin = new Padding(0, 0, 0, 2), UseMnemonic = false }, i, 0);   // fixed cells: hidden boxes must not shift the rest
        var voices = new List<(string Key, Func<string> Get, Action<string> Set, string Was, Control C)>();
        foreach (var v in cfg.Voices)
        {
            var lab = new Label { Text = v.Key, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6), UseMnemonic = false };
            int row = voices.Count + 1;
            vt.Controls.Add(lab, 0, row);
            find.Add((lab, pages.Count - 1));
            var play = Btn("▶", false);
            (play.AutoSize, play.Size, play.Padding, play.Margin, play.Anchor) = (false, new Size(26, 25), Padding.Empty, new Padding(0, 5, 0, 5), AnchorStyles.Left);
            Func<string> get;
            Action<string> set;
            Control first;
            if (VoiceParts(v.Value).Length > 2)   // three and more in the file: stays text
            {
                var t = Field(v.Value, 420);
                (t.Anchor, t.Margin) = (AnchorStyles.Left, new Padding(0, 5, 0, 5));
                vt.Controls.Add(t, 1, row); vt.SetColumnSpan(t, 4);
                (get, set, first) = (() => t.Text.Trim(), s => t.Text = s, t);
            }
            else
            {
                ComboBox Cell(string[] items, int width, int col)
                {
                    var c = Combo(items, 0);
                    (c.Width, c.Anchor, c.Margin) = (width, AnchorStyles.Left, new Padding(0, 5, 0, 5));
                    vt.Controls.Add(c, col, row);
                    return c;
                }
                var pitches = Enumerable.Range(80, 41).Select(i => (i / 100.0).ToString("0.00", CultureInfo.InvariantCulture)).ToArray();
                ComboBox v1 = Cell(Names(L("Zufällig", "Random")), 144, 1), p1 = Cell(pitches, 58, 2), v2 = Cell(Names("–"), 144, 3), p2 = Cell(pitches, 58, 4);
                void Sync() => (p1.Visible, v2.Visible, p2.Visible) = (v1.SelectedIndex > 0, v1.SelectedIndex > 0, v1.SelectedIndex > 0 && v2.SelectedIndex > 0);   // random takes every voice: nothing else to choose
                v1.SelectedIndexChanged += (_, _) => Sync();
                v2.SelectedIndexChanged += (_, _) => Sync();
                void Pick(ComboBox voice, ComboBox pitch, (string Model, string Pitch) x)
                {
                    voice.SelectedIndex = 1 + Array.IndexOf(known, x.Model);   // "random" and "" are not in the list: first entry
                    if (!pitch.Items.Contains(x.Pitch)) pitch.Items.Add(x.Pitch);
                    pitch.SelectedItem = x.Pitch;
                }
                (get, set, first) = (() => v1.SelectedIndex == 0 ? "random" : VoiceText(new[] { (v1, p1), (v2, p2) }.Where(x => x.Item1.SelectedIndex > 0).Select(x => (known[x.Item1.SelectedIndex - 1], x.Item2.Text))),
                                     s => { var p = VoiceParts(s); Pick(v1, p1, p[0]); Pick(v2, p2, p.Length > 1 ? p[1] : ("", "1.00")); Sync(); }, v1);
            }
            vt.Controls.Add(play, 5, row);
            play.Click += async (_, _) =>   // sample on the PC speakers
            {
                play.Enabled = false;
                var say = get();
                try { await Task.Run(() => Program.Preview(say)); }
                catch (Exception e) { MessageBox.Show(f, e.Message, "DCS-ATC"); }
                play.Enabled = true;
            };
            set(v.Value);
            voices.Add((v.Key, get, set, get(), first));
        }
        Wide(vt);
        var allRandom = Btn(L("Alle zufällig", "All random"), false);
        allRandom.Click += (_, _) => voices.ForEach(v => v.Set("random"));
        var allStd = Btn(L("Standard", "Defaults"), false);
        allStd.Click += (_, _) => voices.ForEach(v => v.Set(std.GetValueOrDefault(v.Key, std["Approach"])));
        var vb = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        vb.Controls.AddRange(new Control[] { allRandom, allStd });
        Wide(vb);
        Note(L("Zufällig: jede Station bekommt bei jedem Start eine der installierten Stimmen (ohne die Pilotenstimme) und behält sie, solange DCS-ATC läuft. " +
               "Zweite Stimme: die Plätze wechseln sich ab, etwa Senaki Tower und Kutaisi Tower. Tonhöhe 1.00 = unverändert, kleiner = tiefer. ▶ spricht eine Probe über die PC-Lautsprecher.",
               "Random: at each start every station gets one of the installed voices (not the pilot voice) and keeps it while DCS-ATC runs. " +
               "Second voice: airfields alternate, e.g. Senaki Tower and Kutaisi Tower. Pitch 1.00 = unchanged, lower = deeper. ▶ plays a sample on the PC speakers."));

        Page(L("Funk & Verkehr", "Radio & traffic"));
        Group(L("KI-Verkehr", "AI traffic"));
        var chat = Check(L("KI-Verkehr funkt hörbar mit", "Hear AI traffic on frequency"), cfg.AiChatter);
        string[] modes = { "voll", "taktisch", "aus" };
        var combat = Combo(new[] { L("voll", "full"), L("taktisch", "tactical"), L("aus", "off") }, Array.IndexOf(modes, cfg.AiFlightComms));
        Row(L("KI-Flüge im Gefecht", "AI flights in combat"), combat);
        var aiNm = new Slider(10, 200, 10, cfg.AiFlightRadiusNm, "0' NM'");
        Row(L("KI-Funk nur im Umkreis eigener Spieler", "AI radio only within this range of players"), aiNm);
        var wingmen = Check(L("Eigene KI-Wingmen funken mit (DCS spricht sie schon selbst)", "Your AI wingmen transmit too (DCS already speaks for them)"), cfg.AiOwnWingmen);
        var tvec = Check(L("Tanker führt selbst zum Join (Kurs, Entfernung, Höhe bis \"visual\")", "Tanker guides you to the join itself (heading, range, altitude until \"visual\")"), cfg.TankerVectors);
        Group(L("Flugplatz", "Airfield"));
        var atis = Check(L("ATIS-Dauersendung", "ATIS broadcast"), cfg.Atis);
        string[] lights = { "silent", "dcs", "off" };
        var light = Combo(new[] { L("stumm", "silent"), L("mit DCS-Lotse (hörbar)", "with DCS controller (audible)"), L("aus", "off") }, Math.Max(0, Array.IndexOf(lights, cfg.RunwayLights)));
        Row(L("Bahnbefeuerung (nachts)", "Runway lights (at night)"), light);
        var notam = Check(L("Zufällige NOTAMs (ILS aus, Bahn gesperrt, Vogelschlag)", "Random NOTAMs (ILS out, runway closed, bird activity)"), cfg.RandomNotams);
        var watch = Check(L("Luftraum überwachen (Kontrollzonen, Verstöße, Debriefing)", "Watch airspace (control zones, deviations, debriefing)"), cfg.AirspaceWatch);

        Page("AWACS");
        Group(L("Lagebild", "Picture"));
        var bull = Check(L("AWACS-Picture als Bullseye (statt BRAA)", "AWACS picture as bullseye (instead of BRAA)"), cfg.AwacsBullseye);
        var awNm = new Slider(40, 200, 10, cfg.AwacsNewGroupNm, "0' NM'");
        Row(L("AWACS meldet neue Gruppen bis", "AWACS calls new groups within"), awNm);
        var follow = Check(L("KI-Jäger fliegen die AWACS-Zuweisung (aus: nur Funk)", "AI fighters fly the AWACS assignment (off: radio only)"), cfg.AiFollowAwacs);

        Page(L("Frequenzen", "Frequencies"));
        Group(L("Funktionsfrequenzen in MHz *", "Station frequencies in MHz *"));
        var freqs = cfg.Frequencies.Select(x => { var t = Field(D(x.Value), 100); Row(x.Key, t); return (x.Key, Box: t); }).ToArray();
        var modu = Combo(new[] { "AM", "FM" }, cfg.Modulation.Equals("FM", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        Row(L("Modulation *", "Modulation *"), modu);
        var side = Combo(new[] { L("blau", "blue"), L("rot", "red") }, cfg.Coalition == 1 ? 1 : 0);
        Row(L("SRS-Seite für Stationen ohne eigene Seite *", "SRS side for stations without their own *"), side);
        Group(L("Flugplätze", "Airfields"));
        var fq = Btn("frequencies.jsonc …", false);
        fq.Click += (_, _) =>
        {
            if (!File.Exists(Program.FreqPath))   // the app writes it with all airfields as commented lines
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--frequencies") { WorkingDirectory = AppContext.BaseDirectory, CreateNoWindow = true })!.WaitForExit();
            Notepad(Program.FreqPath);
        };
        Row(L("Eigene Frequenzen je Platz (gilt sofort)", "Own frequencies per airfield (applied at once)"), fq);

        Page(L("Tasten & Funkrad", "Keys & radio wheel"));
        Group(L("Eingabe", "Input"));
        var keys = Btn(L("Tasten festlegen …", "Set keys …"), false);
        keys.Click += (_, _) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--keys") { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = true });
        Row(L("Funkrad-Taste, HOTAS, Sprechtaste", "Wheel key, HOTAS, push-to-talk"), keys);
        var mics = Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToArray();
        var mic = Combo(mics.Prepend(L("Windows-Standard", "Windows default")).ToArray(),
                        cfg.MicName == "" ? 0 : Array.FindIndex(mics, m => m.Contains(cfg.MicName, StringComparison.OrdinalIgnoreCase)) + 1);
        Row(L("Mikrofon (nur ohne SRS) *", "Microphone (only without SRS) *"), mic);
        Group(L("Funkrad", "Radio wheel"));
        string[] ingames = { "auto", "on", "off", "window" };
        var ingame = Combo(new[] { L("Text, automatisch (wenn DCS im VR-Modus)", "text, automatic (when DCS is in VR mode)"), L("Text, immer", "text, always"), L("aus", "off"), L("Rad als Bild (Test)", "wheel as a picture (test)") }, Array.IndexOf(ingames, cfg.WheelInGame));
        Row(L("Funkrad im Spiel (VR)", "Radio wheel in game (VR)"), ingame);
        var wsize = new Slider(25, 95, 5, Math.Round(cfg.WheelSize * 100), "0' %'");
        Row(L("Größe (Anteil der Fensterhöhe, im Rad mit + / -) *", "Size (share of the window height, + / - in the wheel) *"), wsize);

        Page("SRS");
        Group(L("Verbindung", "Connection"));
        var srs = Field(cfg.SrsPath, 246);
        srs.Margin = Padding.Empty;
        var browse = Btn("…", false);
        (browse.AutoSize, browse.Padding, browse.Margin, browse.Size) = (false, Padding.Empty, new Padding(6, 0, 0, 0), new Size(36, srs.Height));
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = L("SRS-Ordner (mit ExternalAudio)", "SRS folder (containing ExternalAudio)"), UseDescriptionForTitle = true, SelectedPath = srs.Text };
            if (d.ShowDialog(f) == DialogResult.OK) srs.Text = d.SelectedPath;
        };
        var srsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        srsRow.Controls.AddRange(new Control[] { srs, browse });
        Row(L("SRS-Ordner *", "SRS folder *"), srsRow);
        var listen = Check(L("Über SRS auf der Lotsen-Frequenz sprechen (aus: eigene Sprechtaste) *", "Talk via SRS on the controller frequency (off: own push-to-talk key) *"), cfg.SrsListen);
        var auto = Check(L("SRS-Client beim Start mit dem lokalen SRS-Server verbinden *", "Connect the SRS client to the local SRS server on start *"), cfg.SrsAutoConnect);
        Group(L("Funk über SRS", "Radio via SRS"));
        var iffc = Check(L("Transponder prüfen (Code, Ident, Mode 4, über SRS)", "Check transponder (code, ident, Mode 4, via SRS)"), cfg.IffCheck);
        var spos = Check(L("Lotsen senden mit Standort (SRS-Reichweite und Sichtlinie)", "Controllers transmit with their position (SRS range and line of sight)"), cfg.StationPositions);

        Page(L("Module", "Modules"));   // like the installer: modules.txt (app) and DcsAtcModules.txt (F10 menu)
        Group(L("Ein- und ausschalten", "Switch on and off"));
        var offFile = Path.Combine(Program.SavedGamesDcs(), "Scripts", "DcsAtc", "DcsAtcOff.txt");   // forum 0.13: off without uninstalling, hook and mission script check it
        var active = Check(L("DCS-ATC aktiv (aus: startet ab der nächsten Mission nicht mehr)", "DCS-ATC active (off: no longer starts from the next mission)"), !File.Exists(offFile));
        var mods = Program.Modules.Select(m => { var b = Check(L(m.De, m.En), Program.On(m.Id)); grid.SetColumnSpan(b, 1); return (m.Id, Box: b); }).ToArray();
        Note(L("abgewählt: kein Funk, kein Eintrag im Funkrad und F10-Menü (F10 ab der nächsten Mission)", "unticked: no radio, no entry in the wheel and F10 menu (F10 from the next mission)"));

        Page(L("Erweitert", "Advanced"));
        Group(L("Diagnose", "Diagnostics"));
        var dbg = Check(L("Debug-Log für Fehlerberichte (atc-debug.txt)", "Debug log for bug reports (atc-debug.txt)"), cfg.DebugLog);
        Group(L("Spracherkennung *", "Speech recognition *"));
        var gpu = Check(L("Auf der Grafikkarte rechnen (Vulkan), sonst CPU", "Run on the graphics card (Vulkan), otherwise CPU"), cfg.AsrGpu);
        var apiUrl = Field(cfg.AsrApiUrl, hint: "https://…/v1/audio/transcriptions");
        Row(L("API-Adresse (leer = lokal erkennen)", "API URL (empty = recognize locally)"), apiUrl);
        var apiKey = Field(cfg.AsrApiKey);
        apiKey.UseSystemPasswordChar = true;
        Row(L("API-Schlüssel", "API key"), apiKey);
        var apiModel = Field(cfg.AsrApiModel);
        Row(L("API-Modell", "API model"), apiModel);
        Group(L("Lagekarte im Browser *", "Situation map in the browser *"));
        var port = Field(cfg.MapPort.ToString(), 100);
        Row(L("Port (0 = aus)", "Port (0 = off)"), port);
        var lan = Check(L("Auch von anderen Geräten im Netzwerk (Tablet)", "Also from other devices on the network (tablet)"), cfg.MapLan);
        Group(L("Alles andere", "Everything else"));
        var raw = Btn("config.jsonc …", false);
        raw.Click += (_, _) => Notepad(Program.ConfigPath);
        Row(L("Alle Optionen als Textdatei", "All options as a text file"), raw);
        Note(L("Nur dort: Center-Namen und -Sektoren, eigene NOTAMs, ATIS-Frequenz je Platz, On-speed-AoA.", "Only there: center names and sectors, own NOTAMs, ATIS frequency per airfield, on-speed AoA."));

        Page(L("Danke", "Thanks"));
        Group(L("Spender", "Supporters"));   // uppercase Ü loses its dots in the heading font
        Note(L("DCS-ATC ist kostenlos und bleibt es. Danke an alle, die es trotzdem mit einer Spende unterstützen", "DCS-ATC is free and will always stay free. Thanks to everyone who supports it anyway with a tip")
             + (Supporters.Length > 0 ? ":" : "."));
        foreach (var n in Supporters)
            Wide(new Label { Text = n, AutoSize = true, ForeColor = Wheel.Accent, Font = Wheel.F(Wheel.Font1, 12f, FontStyle.Bold), Margin = new Padding(0, 4, 0, 4), UseMnemonic = false });
        var kofi = Btn("ko-fi.com/christianwagner", false);
        kofi.Click += (_, _) => Process.Start(new ProcessStartInfo("https://ko-fi.com/christianwagner") { UseShellExecute = true });
        kofi.Margin = new Padding(0, 10, 0, 4);
        Wide(kofi);
        // supporters also in the sidebar below the page buttons: in view on every page
        if (Supporters.Length > 0)
        {
            nav.Controls.Add(new Label { Text = L("DANKE AN", "THANKS TO"), Font = Wheel.F(Wheel.Font1, 9f, FontStyle.Bold), ForeColor = Wheel.Grey, AutoSize = true, Margin = new Padding(8, 26, 0, 4), UseMnemonic = false });
            nav.Controls.Add(new Label { Text = string.Join("\n", Supporters), Font = Wheel.F(Wheel.Font1, 10f, FontStyle.Bold), ForeColor = Wheel.Accent, AutoSize = true, MaximumSize = new Size(166, 0), Margin = new Padding(8, 0, 0, 0), UseMnemonic = false });
        }

        // bottom: hint left, Save/Cancel right
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 60 };
        bar.Paint += (_, e) => { using var p = new Pen(Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        var save = Btn(L("Speichern", "Save"), true);
        var cancel = Btn(L("Abbrechen", "Cancel"), false);
        cancel.Click += (_, _) => f.Close();   // DialogResult closes only ShowDialog windows, this one runs via Application.Run (Esc via CancelButton likewise)
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Dock = DockStyle.Right, Padding = new Padding(0, 14, 18, 0) };
        save.Margin = new Padding(0, 0, 10, 0); cancel.Margin = Padding.Empty;
        buttons.Controls.AddRange(new Control[] { cancel, save });
        bar.Controls.Add(new Label { Text = L("* gilt ab dem nächsten Start, alles andere sofort", "* takes effect on next start, everything else immediately"), AutoSize = true, ForeColor = Wheel.Grey, Location = new Point(18, 21) });
        bar.Controls.Add(buttons);
        f.Controls.AddRange(new Control[] { host, bar, nav });   // docked back to front: sidebar full height, then the bar, pages get the rest
        f.AcceptButton = save; f.CancelButton = cancel;
        search.Enter += (_, _) => f.AcceptButton = null;   // Enter in the search field must not save
        search.Leave += (_, _) => f.AcceptButton = save;
        f.Shown += (_, _) => f.ActiveControl = tabs[0];   // Callsign field without focus: hint "leer = aus dem DCS-Slot" visible
        f.Tag = (Action<int>)(i => { sel = Math.Clamp(i, 0, pages.Count - 1); Mark(); });   // Snapshot picks the page
        Mark();

        save.Click += (_, _) =>
        {
            bool Bad(Control c, string msg)   // message, then the page with the field
            {
                MessageBox.Show(f, msg, "DCS-ATC");
                sel = pages.FindIndex(p => p.Contains(c)); Mark(); c.Focus();
                return true;
            }
            var call = cs.Text.Trim() == "" ? "" : Program.NormCallsign(cs.Text);
            if (call == null && Bad(cs, L("Rufzeichen wie in DCS: Name und zwei Ziffern, z. B. \"Viper 1-1\".", "Callsign like in DCS: a name and two digits, e.g. \"Viper 1-1\"."))) return;
            if (srs.Text.Trim() != cfg.SrsPath && Program.SrsRoot(srs.Text) == null &&
                Bad(srs, L("Kein SRS-Ordner: darin fehlt ExternalAudio\\DCS-SR-ExternalAudio.exe.", "Not an SRS folder: ExternalAudio\\DCS-SR-ExternalAudio.exe is missing."))) return;
            var mhz = new Dictionary<string, double>(cfg.Frequencies);
            foreach (var (key, box) in freqs)
            {
                if (double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v is >= 30 and <= 400) mhz[key] = v;
                else if (Bad(box, L($"{key}: Frequenz in MHz zwischen 30 und 400, z. B. 265.0.", $"{key}: frequency in MHz between 30 and 400, e.g. 265.0."))) return;
            }
            if ((!int.TryParse(port.Text.Trim(), out var mapPort) || mapPort is < 0 or > 65535) && Bad(port, L("Port: Zahl von 0 bis 65535.", "Port: a number from 0 to 65535."))) return;
            var voice = voices.ToDictionary(v => v.Key, v => v.Get() == v.Was ? cfg.Voices[v.Key] : v.Get());   // untouched row: as written in the file ("@1.0" stays)
            if (voices.FirstOrDefault(v => voice[v.Key] == "").C is { } empty && Bad(empty, L("Stimme fehlt.", "Voice is missing."))) return;
            var set = new (string Key, string Value, string After)[]
            {
                ("MyCallsign", J(call!), "Callsign"), ("CallsignFromUnit", J(unitCs.Checked), "MyCallsign"), ("Volume", D(vol.Value / 100), "RadioFx"), ("SpeechRate", D(rate.Value), "Volume"),
                ("ReplyPause", D(pause.Value), "SpeechRate"), ("RadioFx", J(fx.Checked), "PilotVoice"), ("RadioText", J(rtx.Checked), "RadioFx"), ("HintText", J(htx.Checked), "RadioText"),
                ("PilotVoice", J(pv.SelectedIndex == 0 ? "" : known[pv.SelectedIndex - 1] == pilot ? cfg.PilotVoice : known[pv.SelectedIndex - 1]), "Voices"),   // same model: its "@pitch" stays
                ("AiChatter", J(chat.Checked), "ReplyPause"), ("AwacsBullseye", J(bull.Checked), "Coalition"), ("AwacsNewGroupNm", D(awNm.Value), "AwacsBullseye"), ("Atis", J(atis.Checked), "AiChatter"), ("RunwayLights", J(lights[light.SelectedIndex]), "Atis"), ("AltimeterUnit", J(units[unit.SelectedIndex]), "RunwayLights"), ("AiFlightComms", J(modes[combat.SelectedIndex]), "Volume"), ("IffCheck", J(iffc.Checked), "AirspaceWatch"), ("StationPositions", J(spos.Checked), "IffCheck"), ("RandomNotams", J(notam.Checked), "StationPositions"),("AirspaceWatch", J(watch.Checked), "RunwayLights"), ("DebugLog", J(dbg.Checked), "AirspaceWatch"),
                ("WheelInGame", J(ingames[ingame.SelectedIndex]), "WheelSize"), ("MicName", J(mic.SelectedIndex == 0 ? "" : mics[mic.SelectedIndex - 1]), "WheelSize"), ("SrsPath", J(srs.Text.Trim()), "DebugLog"),
                ("KneeboardPages", J(knee.Checked), "DebugLog"), ("F10Drawings", J(f10.Checked), "KneeboardPages"),("AiFlightRadiusNm", D(aiNm.Value), "AiFlightComms"), ("AiOwnWingmen", J(wingmen.Checked), "AiFlightRadiusNm"), ("TankerVectors", J(tvec.Checked), "AiOwnWingmen"),
                ("AiFollowAwacs", J(follow.Checked), "AiFlightComms"), ("Modulation", J(modu.Text), "AwacsNewGroupNm"), ("Coalition", side.SelectedIndex == 1 ? "1" : "2", "Modulation"),
                ("WheelSize", D(wsize.Value / 100), "WheelButton"), ("SrsListen", J(listen.Checked), "AiChatter"), ("SrsAutoConnect", J(auto.Checked), "SrsListen"),
                ("AsrGpu", J(gpu.Checked), "DebugLog"), ("AsrApiUrl", J(apiUrl.Text.Trim()), "AsrGpu"), ("AsrApiKey", J(apiKey.Text.Trim()), "AsrApiUrl"), ("AsrApiModel", J(apiModel.Text.Trim()), "AsrApiKey"),
                ("MapPort", mapPort.ToString(), "DebugLog"), ("MapLan", J(lan.Checked), "MapPort"), ("UpdateCheck", J(updAuto.Checked), "DebugLog"),
            };
            var was = JsonSerializer.SerializeToElement(cfg);   // write only what changed: the file keeps its shape, and a value another window wrote meanwhile (wheel size) stays
            try
            {
                var dcs = Path.Combine(Program.SavedGamesDcs(), "Scripts", "DcsAtc");
                var ml = string.Join(",", mods.Where(m => m.Box.Checked).Select(m => m.Id).Append("m2").Append("m3"));   // before config.jsonc: its reload reads modules.txt; m2 = list knows jtac, m3 = center (installer update)
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "modules.txt"), ml);
                if (Directory.Exists(dcs)) File.WriteAllText(Path.Combine(dcs, "DcsAtcModules.txt"), ml);
                if (!active.Checked && Directory.Exists(dcs)) File.WriteAllText(offFile, "DCS-ATC aus / off: DCS-ATC Einstellungen / settings -> DCS-ATC aktiv / active\n");
                else if (active.Checked && File.Exists(offFile)) File.Delete(offFile);
                string text = File.ReadAllText(Program.ConfigPath), before = text;
                foreach (var s in set.Where(s => !was.TryGetProperty(s.Key, out var o) || o.GetRawText() != s.Value)) text = Program.WithConfigValue(text, s.Key, s.Value, s.After);
                text = WithNested(text, "Frequencies", mhz.Where(x => x.Value != cfg.Frequencies[x.Key]).Select(x => (x.Key, D(x.Value))), J(mhz));
                text = WithNested(text, "Voices", voice.Where(x => x.Value != cfg.Voices[x.Key]).Select(x => (x.Key, J(x.Value))), J(voice));
                if (text != before) File.WriteAllText(Program.ConfigPath, text);
                if ((lang.SelectedIndex == 1) != En)   // like the installer: app and mission (F10 menu)
                {
                    var code = lang.SelectedIndex == 1 ? "en" : "de";
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "lang.txt"), code);
                    if (Directory.Exists(dcs)) File.WriteAllText(Path.Combine(dcs, "DcsAtcLang.txt"), code);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(f, L("Speichern fehlgeschlagen: ", "Saving failed: ") + e.Message, "DCS-ATC"); return;
            }
            f.Close();
        };
        return f;
    }

    /// Sidebar button: no white default-button frame while it has the focus.
    sealed class Tab : Button { public override void NotifyDefault(bool value) => base.NotifyDefault(false); }

    /// Slider in radio wheel look (TrackBar and NumericUpDown cannot be colored dark): mouse, mouse wheel, arrow keys.
    sealed class Slider : Control
    {
        readonly double min, max, step;
        readonly string fmt;
        public double Value { get; private set; }
        const int Txt = 64;   // Space for the value on the right

        public Slider(double min, double max, double step, double value, string fmt)
        {
            (this.min, this.max, this.step, this.fmt, Value) = (min, max, step, fmt, Math.Clamp(value, min, max));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            (TabStop, Size, Cursor) = (true, new Size(290, 26), Cursors.Hand);
        }

        float Span => Width - Txt - 18;
        float X(double v) => 9 + (float)((v - min) / (max - min)) * Span;
        void Set(double v) { Value = Math.Round(Math.Clamp(Math.Round(v / step) * step, min, max), 3); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); Set(min + (e.X - 9) / Span * (max - min)); }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Set(min + (e.X - 9) / Span * (max - min)); }
        protected override void OnMouseWheel(MouseEventArgs e) => Set(Value + Math.Sign(e.Delta) * step);
        protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Left or Keys.Right) Set(Value + (e.KeyCode == Keys.Right ? step : -step)); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent!.BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float y = Height / 2f, x = X(Value);
            using (var track = new Pen(Line, 4)) g.DrawLine(track, X(min), y, X(max), y);
            using (var fill = new Pen(Wheel.Accent, 4)) g.DrawLine(fill, X(min), y, x, y);
            using (var knob = new SolidBrush(Wheel.Accent)) g.FillEllipse(knob, x - 8, y - 8, 16, 16);
            if (Focused) { using var ring = new Pen(Wheel.Ink, 1.5f); g.DrawEllipse(ring, x - 9, y - 9, 18, 18); }
            TextRenderer.DrawText(g, Value.ToString(fmt, CultureInfo.InvariantCulture), Font, new Rectangle(Width - Txt, 0, Txt, Height), Wheel.Ink,
                                  TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
