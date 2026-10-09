using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;

namespace DcsAtc;

/// Settings window (DcsAtc --settings) in the radio wheel look: callsign and general, writes config.jsonc (comments stay).
/// The running app applies callsign, volume, pace and radio options immediately (Program.ReloadLive).
static class Settings
{
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
        Sta(() => Application.Run(Build(cfg)));
    }

    /// Preview as PNG (DcsAtc --settings-png datei.png [de|en]): draw the window off-screen.
    public static void Snapshot(Config cfg, string file) => Sta(() =>
    {
        using var f = Build(cfg);
        (f.StartPosition, f.Location, f.ShowInTaskbar, f.TopMost) = (FormStartPosition.Manual, new Point(-20000, -20000), false, false);
        f.Show();
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

    static Form Build(Config cfg)
    {
        var f = new Form
        {
            Text = "DCS-ATC – " + L("Einstellungen", "Settings"), Font = Wheel.F(Wheel.Font1, 10f), BackColor = Wheel.Hub, ForeColor = Wheel.Ink,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            TopMost = true,   // from the radio wheel over DCS
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScroll = true,
            MaximumSize = Screen.PrimaryScreen!.WorkingArea.Size,   // too small: scroll
        };
        f.HandleCreated += (_, _) => { int on = 1; DwmSetWindowAttribute(f.Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref on, 4); };
        f.Shown += (_, _) =>   // always to the front (from wheel, start menu, setup), not behind DCS: the wheel never activates itself, then Windows blocks SetForegroundWindow – attaching to the input thread of the foreground window lifts the lock
        {
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
            bool att = fg != 0 && fg != me && AttachThreadInput(me, fg, true);
            BringWindowToTop(f.Handle); SetForegroundWindow(f.Handle); f.Activate();
            if (att) AttachThreadInput(me, fg, false);
        };
        FlowLayoutPanel Column() => new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 18, 0) };
        var (left, right) = (Column(), Column());
        TableLayoutPanel grid = null!;
        // Group: heading in accent color, below it tile like a wheel segment
        void Group(FlowLayoutPanel col, string title)
        {
            col.Controls.Add(new Label { Text = title.ToUpperInvariant(), Font = Wheel.F(Wheel.Font1, 11f, FontStyle.Bold), ForeColor = Wheel.Accent, AutoSize = true, UseMnemonic = false,
                                         Margin = new Padding(0, col.Controls.Count == 0 ? 0 : 16, 0, 5) });
            grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Wheel.Seg, Padding = new Padding(14, 8, 14, 8), Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
            var g = grid;
            g.Paint += (_, e) => { using var p = new Pen(Line); e.Graphics.DrawRectangle(p, 0, 0, g.Width - 1, g.Height - 1); };
            col.Controls.Add(grid);
        }
        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(240, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) });
            c.Anchor = AnchorStyles.Left; c.Margin = new Padding(0, 5, 0, 5);
            grid.Controls.Add(c);
        }
        CheckBox Check(string text, bool on)
        {
            var b = new CheckBox { Text = text, Checked = on, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 6, 0, 6) };
            (b.FlatAppearance.BorderColor, b.FlatAppearance.CheckedBackColor) = (Wheel.Grey, Box);
            grid.Controls.Add(b); grid.SetColumnSpan(b, 2);
            return b;
        }
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
            c.Items.AddRange(items); c.SelectedIndex = Math.Max(0, sel);
            return c;
        }
        Button Btn(string text, bool main)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = main ? Wheel.Accent : Wheel.Seg, ForeColor = main ? Wheel.Dark : Wheel.Accent,
                                 Font = Wheel.F(Wheel.Font1, 10f, FontStyle.Bold), Padding = new Padding(10, 3, 10, 3), Cursor = Cursors.Hand };
            (b.FlatAppearance.BorderColor, b.FlatAppearance.BorderSize) = (Wheel.Accent, 1);
            return b;
        }

        Group(left, L("Rufzeichen & Sprache", "Callsign & language"));
        var cs = new TextBox { Text = cfg.MyCallsign, Width = 290, BackColor = Box, ForeColor = Wheel.Ink, BorderStyle = BorderStyle.FixedSingle,
                               PlaceholderText = L("leer = Rufzeichen aus dem DCS-Slot", "empty = callsign from the DCS slot") };
        Row(L("Eigenes Rufzeichen", "Your callsign"), cs);
        var lang = Combo(new[] { "Deutsch", "English" }, En ? 1 : 0);
        Row(L("Sprache der Oberfläche *", "Interface language *"), lang);

        Group(left, L("Lotsen-Stimmen", "Controller voices"));
        var vol = new Slider(0, 100, 5, Math.Round(Math.Clamp(cfg.Volume * 100, 0, 100)), "0' %'");
        Row(L("Lautstärke Lotsen", "Controller volume"), vol);
        var rate = new Slider(0.7, 1.5, 0.05, cfg.SpeechRate, "0.00");
        Row(L("Sprechtempo (1 = normal, größer = langsamer)", "Speech pace (1 = normal, higher = slower)"), rate);
        var pause = new Slider(0, 5, 0.5, cfg.ReplyPause, "0.0' s'");
        Row(L("Pause zwischen Funksprüchen", "Pause between calls"), pause);
        var fx = Check(L("Funkklang (Rauschen, Squelch)", "Radio effect (noise, squelch)"), cfg.RadioFx);
        var pv = Check(L("Eigene Anfragen vorsprechen (Pilotenstimme)", "Speak your wheel requests (pilot voice)"), cfg.PilotVoice != "");

        Group(left, "AWACS");
        var bull = Check(L("AWACS-Picture als Bullseye (statt BRAA)", "AWACS picture as bullseye (instead of BRAA)"), cfg.AwacsBullseye);
        var awNm = new Slider(40, 200, 10, cfg.AwacsNewGroupNm, "0' NM'");
        Row(L("AWACS meldet neue Gruppen bis", "AWACS calls new groups within"), awNm);

        Group(left, L("Module", "Modules"));   // like the installer: modules.txt (app) and DcsAtcModules.txt (F10 menu)
        var mods = Program.Modules.Select(m => { var b = Check(L(m.De, m.En), Program.On(m.Id)); grid.SetColumnSpan(b, 1); return (m.Id, Box: b); }).ToArray();
        var mnote = new Label { Text = L("abgewählt: kein Funk, kein Eintrag im Funkrad und F10-Menü (F10 ab der nächsten Mission)", "unticked: no radio, no entry in the wheel and F10 menu (F10 from the next mission)"),
                                AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Wheel.Grey, Margin = new Padding(0, 4, 0, 2) };
        grid.Controls.Add(mnote); grid.SetColumnSpan(mnote, 2);

        Group(right, L("Funk & Verkehr", "Radio & traffic"));
        var chat = Check(L("KI-Verkehr funkt hörbar mit", "Hear AI traffic on frequency"), cfg.AiChatter);
        string[] modes = { "voll", "taktisch", "aus" };
        var combat = Combo(new[] { L("voll", "full"), L("taktisch", "tactical"), L("aus", "off") }, Array.IndexOf(modes, cfg.AiFlightComms));
        Row(L("KI-Flüge im Gefecht", "AI flights in combat"), combat);
        var atis = Check(L("ATIS-Dauersendung", "ATIS broadcast"), cfg.Atis);
        var watch = Check(L("Luftraum überwachen (Kontrollzonen, Verstöße, Debriefing)", "Watch airspace (control zones, deviations, debriefing)"), cfg.AirspaceWatch);
        var dbg = Check(L("Debug-Log für Fehlerberichte (atc-debug.txt)", "Debug log for bug reports (atc-debug.txt)"), cfg.DebugLog);
        string[] lights = { "silent", "dcs", "off" };
        var light = Combo(new[] { L("stumm", "silent"), L("mit DCS-Lotse (hörbar)", "with DCS controller (audible)"), L("aus", "off") }, Math.Max(0, Array.IndexOf(lights, cfg.RunwayLights)));
        Row(L("Bahnbefeuerung (nachts)", "Runway lights (at night)"), light);
        string[] units = { "auto", "hpa", "inhg" };
        var unit = Combo(new[] { L("automatisch (nach Flugzeugtyp)", "automatic (by aircraft type)"), "hPa (QNH)", "inHg (altimeter)" }, Math.Max(0, Array.IndexOf(units, cfg.AltimeterUnit.ToLowerInvariant())));
        Row(L("Höhenmesser-Einstellung", "Altimeter setting"), unit);

        Group(right, L("Funkrad & Eingabe", "Radio wheel & input"));
        string[] ingames = { "auto", "on", "off" };
        var ingame = Combo(new[] { L("automatisch (wenn DCS im VR-Modus)", "automatic (when DCS is in VR mode)"), L("immer", "always"), L("aus", "off") }, Array.IndexOf(ingames, cfg.WheelInGame));
        Row(L("Funkrad im Spiel (VR)", "Radio wheel in game (VR)"), ingame);
        var mics = Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToArray();
        var mic = Combo(mics.Prepend(L("Windows-Standard", "Windows default")).ToArray(),
                        cfg.MicName == "" ? 0 : Array.FindIndex(mics, m => m.Contains(cfg.MicName, StringComparison.OrdinalIgnoreCase)) + 1);
        Row(L("Mikrofon (nur ohne SRS) *", "Microphone (only without SRS) *"), mic);
        var srs = new TextBox { Text = cfg.SrsPath, Width = 246, Margin = Padding.Empty, BackColor = Box, ForeColor = Wheel.Ink, BorderStyle = BorderStyle.FixedSingle };
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
        var keys = Btn(L("Tasten festlegen …", "Set keys …"), false);
        keys.Click += (_, _) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--keys") { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = true });
        Row(L("Funkrad-Taste, HOTAS, Sprechtaste", "Wheel key, HOTAS, push-to-talk"), keys);

        // bottom: hint and config.jsonc left, Save/Cancel right
        var raw = new LinkLabel { Text = L("Alle Optionen (config.jsonc) …", "All options (config.jsonc) …"), AutoSize = true, LinkColor = Wheel.Accent, ActiveLinkColor = Wheel.Ink, Margin = new Padding(0, 14, 0, 0) };
        raw.LinkClicked += (_, _) => Process.Start("notepad.exe", $"\"{Program.ConfigPath}\"");
        var note = new Label { Text = L("* gilt ab dem nächsten Start, alles andere sofort", "* takes effect on next start, everything else immediately"), AutoSize = true, ForeColor = Wheel.Grey, Margin = new Padding(0, 4, 0, 0) };
        var info = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty };
        info.Controls.AddRange(new Control[] { raw, note });
        var save = Btn(L("Speichern", "Save"), true);
        var cancel = Btn(L("Abbrechen", "Cancel"), false);
        cancel.Click += (_, _) => f.Close();   // DialogResult closes only ShowDialog windows, this one runs via Application.Run (Esc via CancelButton likewise)
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = new Padding(0, 14, 0, 0) };
        save.Margin = new Padding(0, 0, 10, 0); cancel.Margin = Padding.Empty;
        buttons.Controls.AddRange(new Control[] { cancel, save });

        var page = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Location = new Point(18, 18), Margin = new Padding(0, 0, 18, 18) };   // Form padding only works with Dock, Dock and AutoScroll do not get along
        page.Controls.AddRange(new Control[] { left, right, info, buttons });
        right.Margin = Padding.Empty;
        f.Controls.Add(page);
        f.AcceptButton = save; f.CancelButton = cancel;
        f.Shown += (_, _) => f.ActiveControl = vol;   // Callsign field without focus: hint "leer = aus dem DCS-Slot" visible

        save.Click += (_, _) =>
        {
            var call = cs.Text.Trim() == "" ? "" : Program.NormCallsign(cs.Text);
            if (call == null)
            {
                MessageBox.Show(f, L("Rufzeichen wie in DCS: Name und zwei Ziffern, z. B. \"Viper 1-1\".", "Callsign like in DCS: a name and two digits, e.g. \"Viper 1-1\"."), "DCS-ATC");
                cs.Focus(); return;
            }
            if (srs.Text.Trim() != cfg.SrsPath && Program.SrsRoot(srs.Text) == null)
            {
                MessageBox.Show(f, L("Kein SRS-Ordner: darin fehlt ExternalAudio\\DCS-SR-ExternalAudio.exe.", "Not an SRS folder: ExternalAudio\\DCS-SR-ExternalAudio.exe is missing."), "DCS-ATC");
                srs.Focus(); return;
            }
            string D(double v) => v.ToString(CultureInfo.InvariantCulture);
            string J(object v) => JsonSerializer.Serialize(v);
            var set = new (string Key, string Value, string After)[]
            {
                ("MyCallsign", J(call), "Callsign"), ("Volume", D(vol.Value / 100), "RadioFx"), ("SpeechRate", D(rate.Value), "Volume"),
                ("ReplyPause", D(pause.Value), "SpeechRate"), ("RadioFx", J(fx.Checked), "PilotVoice"),
                ("PilotVoice", J(pv.Checked ? (cfg.PilotVoice != "" ? cfg.PilotVoice : new Config().PilotVoice) : ""), "Voices"),
                ("AiChatter", J(chat.Checked), "ReplyPause"), ("AwacsBullseye", J(bull.Checked), "Coalition"), ("AwacsNewGroupNm", D(awNm.Value), "AwacsBullseye"), ("Atis", J(atis.Checked), "AiChatter"), ("RunwayLights", J(lights[light.SelectedIndex]), "Atis"), ("AltimeterUnit", J(units[unit.SelectedIndex]), "RunwayLights"), ("AiFlightComms", J(modes[combat.SelectedIndex]), "Volume"), ("AirspaceWatch", J(watch.Checked), "RunwayLights"), ("DebugLog", J(dbg.Checked), "AirspaceWatch"),
                ("WheelInGame", J(ingames[ingame.SelectedIndex]), "WheelSize"), ("MicName", J(mic.SelectedIndex == 0 ? "" : mics[mic.SelectedIndex - 1]), "WheelSize"), ("SrsPath", J(srs.Text.Trim()), "DebugLog"),
            };
            try
            {
                var dcs = Path.Combine(Program.SavedGamesDcs(), "Scripts", "DcsAtc");
                var ml = string.Join(",", mods.Where(m => m.Box.Checked).Select(m => m.Id).Append("m2"));   // before config.jsonc: its reload reads modules.txt; m2 = list knows jtac (installer update)
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "modules.txt"), ml);
                if (Directory.Exists(dcs)) File.WriteAllText(Path.Combine(dcs, "DcsAtcModules.txt"), ml);
                var text = File.ReadAllText(Program.ConfigPath);
                foreach (var s in set) text = Program.WithConfigValue(text, s.Key, s.Value, s.After);
                File.WriteAllText(Program.ConfigPath, text);
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
