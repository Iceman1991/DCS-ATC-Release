using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DcsAtc;

/// BMS-style radio wheel as an overlay over DCS (windowed mode).
/// Key (default Ä) opens/closes, 1–9 selects, 0/Enter = suggested radio call, Backspace back, Esc closes.
/// HOTAS: button opens/closes, coolie hat up/down highlights, right selects, left back.
/// Controllers like the F10 menu of the mission (DcsAtcMission.lua), plus Range, AWACS, Tanker, Debriefing.
sealed class Wheel : Form
{
    internal record Item(string Label, string? Text = null, Item[]? Sub = null);

    /// Each level at most 9 entries (keyboard 1–9, R17): "Platz wählen" and "Say again" in Allgemein, end FF via Enter (Tower.Suggest).
    /// R307: only what fits the situation is shown (Fits, Vis); entries behind the ninth only if enough before it are hidden.
    internal static readonly Item[] All =
    {
        new("Ground", Sub: new Item[]
        {
            new("Request startup", "Ground: request startup"), new("IFR clearance", "Ground: request IFR clearance", Array.Empty<Item>()), new("Request taxi", "Ground: request taxi"),
            new("Vacated, taxi to parking", "Ground: runway vacated, request taxi to parking"), new("Progressive taxi", "Ground: request progressive taxi"),
            new("Hot brakes", "Ground: hot brakes"),   // R251
        }),
        new("Tower", Sub: new Item[]
        {
            new("Ready for departure", "Tower: ready for departure"), new("Ready, closed pattern", "Tower: ready for departure, closed pattern"),
            new("Initial", "Tower: initial"), new("Overhead", "Tower: overhead"), new("Base, gear down", "Tower: base, gear down"),   // Reporting duty: overhead join and landing report only when called
            new("Final, gear down, full stop", "Tower: final, gear down, full stop"), new("Base, touch and go", "Tower: base, gear down, touch and go"),
            new("Going around", "Tower: going around"),
            new("Closed / SFO", Sub: new Item[]   // R202/R204
            {
                new("Request closed", "Tower: request closed traffic"), new("Request SFO", "Tower: request S F O"), new("High key", "Tower: high key"), new("Low key, gear down", "Tower: low key, gear down"),
                new("Ready, practice approach", "Tower: ready for departure, practice approach"),   // R253: IMC instead of closed pattern (radar pattern)
            }),
        }),
        new("Approach", Sub: new Item[]
        {
            new(L("Airborne (nach Start)", "Airborne (after takeoff)"), "Approach: airborne, climbing"), new("Inbound for landing", "Approach: inbound for landing"), new("Inbound, pattern work", "Approach: inbound for pattern work, touch and go"),
            new("Request ILS / straight in", "Approach: request straight in"), new("Flight following", "Approach: request flight following"),
            new(L("Abmelden (cancel approach)", "Cancel approach"), "Approach: cancel approach"),
            new(L("Verkehr", "Traffic"), Sub: new Item[] { new("Traffic in sight", "Approach: traffic in sight"), new("Negative contact", "Approach: negative contact") }),   // max. 9: answers to traffic advisories bundled (no procedure suggestion)
            new("C R P", "Approach: C R P"),   // Reporting duty: handoff at C R P only when called
            new(L("Fahrt melden", "Report airspeed"), "Approach: report airspeed"),   // R129: answer to "say airspeed", Program inserts the speed ("340 knots")
            new("Say again", "Approach: say again"), new("Request higher", "Approach: request higher"),   // R307: only if enough before it is hidden (fellow players: the first 9 as before)
        }),
        new("Range", Sub: new Item[]
        {
            new("Check in", "Range: checking in"), new("IP inbound", "Range: IP inbound"), new("In hot", "Range: in hot"), new("Off safe", "Range: off safe"),
            new("Check out", "Range: checking out"), new("Check out, hung ordnance", "Range: checking out, hung ordnance"),   // R251
        }),
        new("AWACS", Sub: new Item[]
        {
            new("Check in", "AWACS: checking in"), new("Picture", "AWACS: request picture"), new("Bogey dope", "AWACS: bogey dope"), new("Sort", "AWACS: request sort"),
            new(L("Nächster Tanker", "Nearest tanker"), "AWACS: vector to tanker"), new(L("Vektor nächster Platz", "Vector nearest airfield"), "AWACS: vector to nearest airfield"),
            new("Check out", "AWACS: checking out"),
            new(L("Gefecht", "Combat"), Sub: new Item[]   // LD17: same calls as spoken (flight: the assigned or next one)
            {
                new("Committing", "AWACS: committing"), new("Fox three", "AWACS: fox three"), new("Splash", "AWACS: splash one"),
                new("Defending", "AWACS: missile, defending"), new("Request support", "AWACS: request support"), new("Unable", "AWACS: unable"),
                new("Winchester", "AWACS: winchester"), new("Bingo, RTB", "AWACS: bingo, RTB"), new("Say again", "AWACS: say again"),
            }),
        }),
        new("Tanker", Sub: new Item[]
        {
            new("Request rejoin", "Tanker: request rejoin"), new("Visual", "Tanker: visual"), new("Observation", "Tanker: observation"), new("Pre-contact", "Tanker: pre contact"),
            new("Refuel complete", "Tanker: refuel complete"),
        }),
        new("Carrier", Sub: new Item[]
        {
            new("Marshal check in", "Carrier: Marshal, checking in"), new("See you at", "Carrier: see you at angels"),
            new("Initial", "Carrier: initial"), new("Commencing", "Carrier: commencing"), new("Platform", "Carrier: platform"),
            new("Ball", "Carrier: ball"), new("Clara", "Carrier: Clara"), new("Pigeons", "Carrier: pigeons"),
        }),
        new(L("Notfall", "Emergency"), Sub: new Item[]
        {
            new("MAYDAY", Sub: Kinds("mayday mayday mayday", "immediate")), new("PAN PAN", Sub: Kinds("pan pan, pan pan, pan pan", "priority")),
            new(L("Spritmangel", "Minimum fuel"), "minimum fuel"),   // R245: AIM 5-5-15, no emergency, no priority
            new("Hung ordnance", "Approach: hung ordnance"),   // R251 (to Approach; no airfield in the Approach menu anymore, max. 9)
            new("Flameout, high key", "Tower: flameout, high key"),   // R296: real flameout over the airfield (emergency), then Enter "low key, gear down"
            new(L("Notfall beenden", "Cancel emergency"), "cancel emergency"),
        }),
        new(L("Allgemein", "General"), Sub: new Item[]
        {
            new(L("Platz wählen", "Select airfield"), Sub: Array.Empty<Item>()),   // content on opening: nearest airfields (Near)
            new("Radio check", "radio check"), new("Say again", "say again"), new("QNH / weather", "request weather"), new(L("ATIS hören", "Listen to ATIS"), "atis"),
            new("Request zone transit", "Approach: request zone transit"),   // N3: transit through the control zone
            new("Debriefing", "debrief"), new(L("Einstellungen", "Settings"), "settings"),
        }),
    };
    /// A30: type of emergency; Program adds position, altitude and heading (PilotCall), Approach picks the nearest suitable airfield.
    /// R245: fuel only as MAYDAY ("MAYDAY MAYDAY MAYDAY FUEL", ICAO Doc 4444 15.5.4; USAF "emergency fuel"), never as PAN. R251: bird strike.
    static Item[] Kinds(string sig, string land) => new (string De, string En, string Say)[]
    {
        ("Triebwerksausfall", "Engine failure", "engine failure"), ("Treibstoff", "Fuel", "emergency fuel"), ("Hydraulik", "Hydraulic failure", "hydraulic failure"),
        ("Gefechtsschaden", "Battle damage", "battle damage"), ("Medizinisch", "Medical", "medical emergency"), ("Vogelschlag", "Bird strike", "bird strike"),
    }.Where(k => k.Say != "emergency fuel" || sig.StartsWith("mayday"))
     .Select(k => new Item(L(k.De, k.En), $"{(k.Say == "emergency fuel" ? sig + " fuel" : sig)}, {k.Say}, request {land} landing")).ToArray();
    static Item[]? top;
    /// Range or Carrier only if the mission has one, AWACS or Tanker only if one of the own side is present, controller only with a fitting entry (R307)
    /// (same array as long as this does not change; with the wheel open the last built applies: items == top)
    static Item[] Top
    {
        get
        {
            var (aw, tk) = Up();
            var t = Vis(All.Where(i => i.Label switch { "Range" => Ops.Range != null, "Carrier" => Carrier.Boats.Count > 0, "AWACS" => aw, "Tanker" => tk, _ => true }).ToArray(), Fits());
            if (top == null || !t.SequenceEqual(top)) top = t;
            return top;
        }
    }
    public static Func<(bool Awacs, bool Tanker)> Up = () => (true, true);   // own AWACS/GCI or tanker in the air, Program sets it (fellow players, preview: both)
    /// R307: does this radio call fit right now (Program.WheelFits: phase, open question, traffic advisory, Range/Tanker/Carrier)? Fellow players/preview: no state, everything.
    public static Func<Func<string, bool>> Fits = () => _ => true;
    /// R307: visible with text if it fits; submenu if one in it fits; with neither (Platz wählen) always. At most 9 (keys 1–9); if nothing fits, everything.
    internal static Item[] Vis(Item[] xs, Func<string, bool> f)
    {
        bool Ok(Item i) => i.Text != null ? f(i.Text) : i.Sub is not { Length: > 0 } || i.Sub.Any(Ok);
        var v = xs.Where(Ok).Take(9).ToArray();
        return v.Length > 0 ? v : xs.Take(9).ToArray();
    }

    /// Self-test R17: every level reachable by keyboard, also with Range and Carrier.
    public static void SelfTest(Action<bool, string> check)
    {
        foreach (var (name, n) in All.Where(i => i.Sub != null).SelectMany(i => i.Sub!.Where(s => s.Sub != null).Select(s => ($"{i.Label}/{s.Label}", s.Sub!.Length)).Prepend((i.Label, i.Sub!.Length))).Prepend(("Top", All.Length)))
            check(n <= 9 || name == "Approach", $"Funkrad {name}: {n} Einträge (max. 9)");   // R307: Approach has 11, visible at most 9 (Vis)
        // R307: without state (fellow players, preview) everything as before: Approach the first 9, without Say again/Request higher
        var apC = string.Join(",", Vis(All.First(i => i.Label == "Approach").Sub!, _ => true).Select(i => i.Label));
        check(apC == $"{L("Airborne (nach Start)", "Airborne (after takeoff)")},Inbound for landing,Inbound, pattern work,Request ILS / straight in,Flight following,{L("Abmelden (cancel approach)", "Cancel approach")},{L("Verkehr", "Traffic")},C R P,{L("Fahrt melden", "Report airspeed")}",
              "R307 Funkrad ohne Zustand (Mitspieler) Approach: " + apC);
        // AWACS/Tanker only if present; back from "Platz wählen" to Allgemein, from there to the top level
        var up = Up;
        Up = () => (false, true);
        var tl = string.Join(",", Top.Select(i => i.Label));
        Up = up;
        check(!tl.Contains("AWACS") && tl.Contains("Tanker") && Top.Any(i => i.Label == "AWACS"), "Funkrad ohne AWACS: " + tl);
        // R13: AWACS "Nächster Tanker" names the tanker (with frequency), not the nearest airfield
        var tkText = All.First(i => i.Label == "AWACS").Sub!.First(i => i.Label == L("Nächster Tanker", "Nearest tanker")).Text!;
        var me = new Ops.Me("Enfield 1-1", new Telemetry(6000, 6000, 200, 0, 0, 0, 0, 0, 0), "FA-18C_hornet", 2);
        var air = new List<Traffic> { new(4, "E-3A", -60 * 1852, 0, 9000, 0, 150, "Overlord", "", true, 2), new(2, "KC135MPRS", 0, 30 * 1852, 6700, 0, 140, "ATC Texaco #1", "", true, 2) };
        var tkr = new Ops().OnTranscript("AWACS", tkText, me, air, new List<Airfield> { Airfield.Kutaisi() }, 0).FirstOrDefault()?.Text ?? "";
        check(tkr.StartsWith("Enfield one one, Overlord, nearest tanker Texaco, bearing zero eight four, 30 miles") && tkr.Contains(", contact Texaco, two five five") && !tkr.Contains("Kutaisi"), $"R13 Funkrad {tkText} -> {tkr}");
        // R56: Tanker "Visual" from the wheel, 1 NM before the tanker -> cleared to join
        var vsText = All.First(i => i.Label == "Tanker").Sub!.FirstOrDefault(i => i.Label == "Visual")?.Text ?? "";
        var vs = vsText == "" ? "" : new Ops().OnTranscript("Tanker", vsText, me with { Tel = me.Tel! with { Z = 29 * 1852 } }, air, new List<Airfield>(), 0).FirstOrDefault()?.Text ?? "";
        check(vs.StartsWith("Enfield one one, Texaco, cleared to join, left observation"), $"R56 Funkrad Visual {vsText} -> {vs}");
        var w = new Wheel(new Config(), _ => { }, () => null);
        var gen = L("Allgemein", "General");
        w.Choose(Array.FindIndex(Top, i => i.Label == gen)); w.Choose(0); w.Back();
        var back1 = (w.title, w.mark);
        w.Back();
        check(back1 == (gen, 0) && w.items == Top, $"Funkrad: zurück aus Platz wählen -> {back1}, dann {w.title}");
        // R8: Ground -> IFR clearance: destinations without carrier and own airfield (first without destination), back to Ground
        var (near0, fld0, all0) = (Near, Field, Tower.All);
        (Near, Field, Tower.All) = (() => new[] { "Kutaisi", "CVN-73", "Batumi" }, "Kutaisi", new List<Airfield> { new() { Name = "Kutaisi" }, new() { Name = "Batumi" } });
        w.Choose(Array.FindIndex(Top, i => i.Label == "Ground")); w.Choose(1);
        var ifr = string.Join(",", w.items.Select(i => i.Text));
        w.Back();
        var back2 = (w.title, w.mark);
        w.Back();
        (Near, Field, Tower.All) = (near0, fld0, all0);
        check(ifr == "Ground: request IFR clearance,Ground: request IFR clearance to Batumi" && back2 == ("Ground", 1) && w.items == Top, $"Funkrad IFR clearance: {ifr}, zurück -> {back2}");
        // A30: Emergency -> MAYDAY -> type, back to Emergency
        var emg = L("Notfall", "Emergency");
        w.Choose(Array.FindIndex(Top, i => i.Label == emg)); w.Choose(0);
        var kinds = w.items;
        w.Back();
        check(kinds.Length == 6 && kinds[0].Text == "mayday mayday mayday, engine failure, request immediate landing" && (w.title, w.mark) == (emg, 0),
              $"Funkrad Notfall: {kinds.Length} Arten, {kinds[0].Text}, zurück -> {w.title}");
        // R245: fuel only as MAYDAY FUEL, PAN without fuel, own entry "minimum fuel"
        var emgSub = All.First(i => i.Label == emg).Sub!;
        var panKinds = emgSub[1].Sub!.Select(i => i.Text).ToList();
        check(kinds[1].Text == "mayday mayday mayday fuel, emergency fuel, request immediate landing" && !panKinds.Any(s => s!.Contains("fuel")) && emgSub.Any(i => i.Text == "minimum fuel"),
              $"R245 Funkrad Sprit: {kinds[1].Text} | PAN {string.Join(", ", panKinds)}");
        w.Back();
        // Tanker lands with the wheel open: top level stays the same until closing (frequencies, Backspace closes)
        var (fo, fq) = (FreqOf, false);
        (Up, FreqOf) = (() => (true, false), _ => { fq = true; return null; });
        using (var bmp = new Bitmap(56, 56)) using (var g = Graphics.FromImage(bmp)) w.Draw(g, 560);
        (Up, FreqOf) = (up, fo);
        check(fq, "Funkrad: Tanker weg bei offenem Rad -> Oberebene zeigt weiter Frequenzen");
        // VR: wheel text in the game with highlight and suggestion, closing clears it
        var (ig, sent) = (InGame, new List<string>());
        InGame = sent.Add;
        w.sug = ("Tower", "ready for departure"); w.Choose(Array.FindIndex(Top, i => i.Label == "Tower")); w.mark = 1;
        var vt = w.VrText();
        w.Close2();
        InGame = ig;
        check(vt.StartsWith("ENTER ▸ Tower: ready for departure\nDCS-ATC – Tower\n") && vt.Contains("\n> 2 Ready, closed pattern") && vt.Contains("\n   1 Ready for departure")
              && !vt.Contains("Suggested") && !vt.Contains("Vorschlag") && sent.SequenceEqual(new[] { "" }),
              "Funkrad VR-Text: " + vt.Replace("\n", " | ") + " / Schließen: " + string.Join(",", sent.Select(x => $"\"{x}\"")));
        var shot = ShotPath(new DateTime(2026, 10, 8, 14, 5, 9));
        check(shot.EndsWith(@"\DCS-ATC\DCS-ATC-20261008-140509.png") && shot.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), "Funkrad-Screenshot: " + shot);
    }

    readonly Config cfg;
    readonly Action<string> send;
    readonly Func<(string Role, string Text)?> suggest;
    Item[] items = Top;
    string title = Field;
    public static string Field = "ATC";   // current airfield (Program sets it), center of the wheel
    public static Func<string[]> Near = Array.Empty<string>;   // nearest airfields, Program sets it
    /// R8: destination of the en-route clearance: nearest airfields without carrier and the own one, first without destination (Tower: nearest airfield in departure direction).
    static Item[] Dests(string text) => Near().Where(f => f != Field && Tower.All.Any(a => a.Name == f)).Select(f => new Item(f, $"{text} to {f}"))
        .Prepend(new Item(L("Nächster in Abflugrichtung", "Next in departure direction"), text)).ToArray();
    public static Func<string, double?> FreqOf = _ => null;   // frequency per controller at the current airfield, Program sets it
    public static Func<string, double, bool> Untuned = (_, _) => false;   // SRS: player has not tuned this controller frequency (red), Program sets it
    int mark;
    (string Role, string Text)? sug;

    Wheel(Config cfg, Action<string> send, Func<(string Role, string Text)?> suggest)
    {
        this.cfg = cfg; this.send = send; this.suggest = suggest;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    public static void Start(Config cfg, Action<string> send, Func<(string Role, string Text)?> suggest)
    {
        if (cfg.WheelKey == 0 && cfg.WheelJoystick < 0) return;
        var t = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);   // DCS runs with 125 % scaling
            var w = new Wheel(cfg, send, suggest);
            w.hookProc = w.Hook;
            w.hook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, w.hookProc, GetModuleHandle(null), 0);
            var timer = new System.Windows.Forms.Timer { Interval = 30 };
            timer.Tick += (_, _) => { if (w.Visible && !DcsInFront()) w.Close2(); w.PollJoystick(); };   // tabbed out: wheel closed
            timer.Start();
            Application.Run(new ApplicationContext());   // message loop for hook and overlay
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    /// Preview as PNG (DcsAtc --wheel-png datei.png ["Rolle: Vorschlag"]): level 1 and tower level side by side.
    public static void Snapshot(Config cfg, string file, string? sugText = null)
    {
        var sp = sugText?.Split(": ", 2);
        var w = new Wheel(cfg, _ => { }, () => sp is [var r, var x] ? (r, x) : ("Tower", "ready for departure"));
        Field = "Senaki-Kolkhi";
        FreqOf = r => r is "Ground" or "Tower" or "Approach" ? 261.0 : null;
        Untuned = (r, _) => r == "Tower";   // Preview: tower not tuned (red)
        w.title = Field;
        w.sug = w.suggest();
        using var bmp = new Bitmap(1120, 560);
        using var g = Graphics.FromImage(bmp);
        using (var sky = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 1120, 560), Color.FromArgb(96, 140, 190), Color.FromArgb(190, 200, 205), 90f))
            g.FillRectangle(sky, 0, 0, 1120, 560);
        using (var ground = new SolidBrush(Color.FromArgb(88, 98, 72))) g.FillRectangle(ground, 0, 400, 1120, 160);
        w.mark = 1;
        w.Draw(g, 560);
        w.Choose(1);
        w.mark = 0;
        g.TranslateTransform(560, 0);
        w.Draw(g, 560);
        bmp.Save(file);
    }

    /// Test of real transparency (DcsAtc --wheel-live datei.png): show the wheel for 2 s and take a screenshot of it.
    public static void LiveShot(Config cfg, string file)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var w = new Wheel(cfg, _ => { }, () => ("Tower", "ready for departure"));
        Field = "Senaki-Kolkhi";
        w.Open();
        var end = DateTime.Now.AddSeconds(2);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(30); }
        Console.WriteLine($"Bounds {w.Bounds} Visible {w.Visible} Dpi {w.DeviceDpi}");
        using var bmp = new Bitmap(w.Width, w.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(w.Bounds.Location, Point.Empty, w.Size);
        bmp.Save(file);
        w.Close();
    }

    // ------------------------------------------------------------ Overlay: never focus, click-through
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x20 /*TRANSPARENT*/ | 0x80000 /*LAYERED*/ | 0x8 /*TOPMOST*/;
            return cp;
        }
    }

    void Open()
    {
        items = Top; sug = suggest(); title = Field;
        mark = sug == null ? 0 : Math.Max(0, Array.FindIndex(items, i => i.Label == sug.Value.Role));
        Place();
        Show();
        Render();
    }

    /// Size: fraction of the DCS window height (config "WheelSize", adjustable in the wheel with + / -)
    void Place()
    {
        var r = DcsRect() ?? Screen.PrimaryScreen!.Bounds;
        int s = (int)(r.Height * Math.Clamp(cfg.WheelSize, 0.25, 0.95));
        Bounds = new Rectangle(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s);
    }
    public static Action<double> SaveSize = _ => { };   // Program writes config.jsonc

    void Close2() { Hide(); items = Top; InGame(""); }
    public static Action<string> InGame = _ => { };   // VR: wheel as text in the game (Program sets it), "" = clear

    /// Print key with the wheel open: screen of the wheel including the wheel (layered window is in the screenshot) as PNG to Pictures\DCS-ATC, wheel stays open.
    void Shot()
    {
        var file = ShotPath(DateTime.Now);
        var r = Screen.FromRectangle(Bounds).Bounds;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var bmp = new Bitmap(r.Width, r.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(file);
            Log($"[ATC] Screenshot: {file}");
            note = L("Screenshot gespeichert", "Screenshot saved");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException or System.ComponentModel.Win32Exception)
        {
            Log($"[ATC] Screenshot fehlgeschlagen: {e.Message}");
            note = L("Screenshot fehlgeschlagen", "Screenshot failed");
        }
        Render();
        var t = new System.Windows.Forms.Timer { Interval = 3000 };   // hint in the wheel gone again after 3 s
        t.Tick += (_, _) => { t.Dispose(); note = null; Render(); };
        t.Start();
    }
    string? note;
    internal static string ShotPath(DateTime t) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DCS-ATC", $"DCS-ATC-{t:yyyyMMdd-HHmmss}.png");
    public static Action<string> Log = Console.WriteLine;   // Program sets its log (atc-log.txt)

    /// VR: wheel as text for the mission (outTextForUnit): suggestion (Enter/0) first, title, entries (">" = highlighted)
    string VrText() =>
        (sug is { } g ? $"ENTER ▸ {g.Role}: {g.Text}\n" : "") +
        $"DCS-ATC – {title}\n" + string.Join("\n", Slots().Select(s => $"{(s.Mark ? "> " : "   ")}{s.Num} {s.Label}{(s.Freq != null ? "  " + s.Freq : "")}"));

    void Choose(int i)
    {
        if (i < 0 || i >= items.Length) return;
        var it = items[i];
        if (it.Sub != null)
        {
            items = it.Sub.Length > 0 ? Vis(it.Sub, Fits()) : it.Text == null ? Near().Select(f => new Item(f, "switch " + f)).ToArray() : Dests(it.Text); title = it.Label;
            mark = Math.Max(0, Array.FindIndex(items, x => sug != null && x.Text?.EndsWith(sug.Value.Text) == true));
            Render();
            return;
        }
        Close2();
        if (it.Text == "settings") { AllowSetForegroundWindow(-1); Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--settings") { UseShellExecute = false, CreateNoWindow = true }); return; }   // Settings window (own process, the app takes over on saving)
        if (it.Text != null) Task.Run(() => send(it.Text));
    }

    void Suggested()
    {
        if (sug is not { } s) return;
        Close2();
        Task.Run(() => send($"{s.Role}: {s.Text}"));
    }

    void Back()
    {
        if (items == top) { Close2(); return; }
        var up = All.FirstOrDefault(p => p.Sub?.Any(s => s.Sub != null && s.Label == title) == true);   // Platz wählen -> Allgemein, otherwise top level
        var ps = up != null ? Vis(up.Sub!, Fits()) : null;   // R307: as when entering
        (items, title, mark) = ps != null ? (ps, up!.Label, Math.Max(0, Array.FindIndex(ps, s => s.Label == title))) : (Top, Field, 0);
        Render();
    }

    // Real per-pixel transparency: draw in a 32-bit bitmap (layout like the preview, 560 px, scaled) and hand over as a layered window
    void Render()
    {
        if (!Visible) return;
        InGame(VrText());
        int s = Width;
        using var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);   // otherwise the bitmap takes the screen DPI (125 %) and the font becomes larger than planned
        using (var g = Graphics.FromImage(bmp)) { g.ScaleTransform(s / 560f, s / 560f); Draw(g, 560); }
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hb);
        var size = new SIZE { cx = s, cy = s };
        var src = new POINT();
        var pos = new POINT { x = Bounds.X, y = Bounds.Y };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /*AC_SRC_ALPHA*/ };
        UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        SelectObject(mem, old); DeleteObject(hb); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
    }
    struct POINT { public int x, y; }
    struct SIZE { public int cx, cy; }
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);   // Settings process may come to the front
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    // Colors and font of the wheel (the settings window uses them too)
    internal const string Font1 = "Bahnschrift";
    internal static readonly Color Accent = Color.FromArgb(255, 176, 46), Seg = Color.FromArgb(16, 20, 26), Hub = Color.FromArgb(10, 12, 16),
        Ink = Color.FromArgb(235, 240, 245), Grey = Color.FromArgb(160, 170, 180), Dark = Color.FromArgb(20, 20, 20);

    sealed record Slot(string Num, string Label, string? Freq, bool Mark, bool Sug, bool Off = false);
    static readonly Color Bad = Color.FromArgb(255, 72, 60);   // Radio set incorrectly

    Slot[] Slots()
    {
        bool atTop = items == top;   // not Top: AWACS/Tanker come or go with the wheel open, the shown top level stays until closing
        return items.Select((it, i) =>
        {
            double? f = atTop ? FreqOf(it.Label) ?? (cfg.Mode != "client" && cfg.Frequencies.TryGetValue(it.Label, out var cf) ? cf : null) : null;   // Fellow players: frequencies (map, frequencies.jsonc) known only to the host, show no own config values
            bool off = f is { } x && Untuned(it.Label, x);   // Radio not on this frequency: red with ✗
            return new Slot((i + 1).ToString(), it.Label, f?.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture) is { } fs ? fs + (off ? " ✗" : "") : null,
                            i == mark, sug != null && (atTop ? it.Label == sug.Value.Role : it.Text?.EndsWith(sug.Value.Text) == true), off);
        }).ToArray();
    }


    static PointF Polar(float c, float r, double deg) => new(c + r * (float)Math.Cos(deg * Math.PI / 180), c + r * (float)Math.Sin(deg * Math.PI / 180));
    static readonly StringFormat Mid = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    /// Largest font up to "size" at which the text fits in "box" (wrapping only between words).
    static Font Fit(Graphics g, string text, string family, float size, FontStyle st, SizeF box)
    {
        while (true)
        {
            var f = F(family, size, st);
            var z = g.MeasureString(text, f, (int)box.Width);
            bool words = text.Split(' ').All(w => g.MeasureString(w, f).Width <= box.Width);
            if (size <= 6 || z.Height <= box.Height && words) return f;
            f.Dispose();
            size *= 0.92f;
        }
    }

    internal static Font F(string family, float size, FontStyle st = FontStyle.Regular)
    {
        var f = new Font(family, size, st);
        if (f.Name == family) return f;
        f.Dispose();
        return new Font("Segoe UI", size, st);
    }

    // --- Ring of pie slices (like modern game radial menus), semi-transparent, amber accent
    void Draw(Graphics g, float s)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var slots = Slots();
        float c = s / 2, rOut = s * 0.49f, rIn = s * 0.22f, rText = (rOut + rIn) / 2;
        int n = slots.Length;
        double sweep = 360.0 / n;
        var accent = Accent;
        using var font = F(Font1, s / 36f, FontStyle.Bold);
        using var num = F(Font1, s / 50f, FontStyle.Bold);
        using var small = F(Font1, s / 46f);
        using var line = new Pen(Color.FromArgb(120, 255, 255, 255), 1.2f);
        using var accPen = new Pen(accent, 2);
        for (int i = 0; i < n; i++)
        {
            double a0 = i * sweep - 90 - sweep / 2;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(c - rOut, c - rOut, 2 * rOut, 2 * rOut, (float)(a0 + 0.8), (float)(sweep - 1.6));
            path.AddArc(c - rIn, c - rIn, 2 * rIn, 2 * rIn, (float)(a0 + sweep - 1.6), (float)-(sweep - 3.2));
            path.CloseFigure();
            var sl = slots[i];
            using var fill = new SolidBrush(sl.Mark ? Color.FromArgb(225, accent) : Color.FromArgb(238, Seg));
            g.FillPath(fill, path);
            g.DrawPath(sl.Mark ? accPen : line, path);
            var p = Polar(c, rText, i * sweep - 90);
            var ink = sl.Mark ? Dark : sl.Sug ? accent : Ink;
            using var b = new SolidBrush(ink);
            using var bDim = new SolidBrush(Color.FromArgb(sl.Mark ? 220 : 190, ink));
            float w = rText * (float)Math.Min(0.8, sweep * Math.PI / 180 * 0.95), nh = num.GetHeight(g);
            using var lf = Fit(g, sl.Label, Font1, s / 36f, FontStyle.Bold, new SizeF(w, font.GetHeight(g) * 2.1f));   // max. two lines
            var lz = g.MeasureString(sl.Label, lf, (int)w);
            float top = p.Y - lz.Height / 2;
            g.DrawString(sl.Num, num, bDim, new RectangleF(p.X - 60, top - nh, 120, nh), Mid);
            g.DrawString(sl.Label, lf, b, new RectangleF(p.X - w / 2, top, w, lz.Height), Mid);
            using var bad = new SolidBrush(sl.Mark ? Color.FromArgb(150, 0, 0) : Bad);   // darker on amber
            if (sl.Freq != null) g.DrawString(sl.Freq, small, sl.Off ? bad : bDim, new RectangleF(p.X - 60, top + lz.Height, 120, nh), Mid);
        }
        using var hub = new SolidBrush(Color.FromArgb(248, Hub));
        g.FillEllipse(hub, c - rIn + 6, c - rIn + 6, 2 * rIn - 12, 2 * rIn - 12);
        using var ring = new Pen(Color.FromArgb(200, accent), 2.5f);
        g.DrawEllipse(ring, c - rIn + 6, c - rIn + 6, 2 * rIn - 12, 2 * rIn - 12);
        using var white = new SolidBrush(Color.White);
        using var acc = new SolidBrush(accent);
        using var grey = new SolidBrush(Grey);
        float th = sug == null ? 0.5f : 0.24f;   // with suggestion: airfield small at top, the suggestion is the main thing
        using var big = Fit(g, title.ToUpperInvariant(), Font1, sug == null ? s / 28f : s / 44f, FontStyle.Bold, new SizeF(rIn * (sug == null ? 1.7f : 1.2f), rIn * th * 0.64f));   // one line
        g.DrawString(title.ToUpperInvariant(), big, sug == null ? white : grey, new RectangleF(c - rIn, c - rIn * (sug == null ? 0.62f : 0.84f), 2 * rIn, rIn * th), Mid);
        if (sug is { } sg2)
        {
            // Key label ENTER (amber, dark text), next to it the controller small; below it the suggestion large, bold, max. three lines
            var role = sg2.Role.ToUpperInvariant();
            var (kz, rz) = (g.MeasureString("ENTER", num), g.MeasureString(role, small));
            float kw = kz.Width + 8, kh = kz.Height + 2, x0 = c - (kw + 6 + rz.Width) / 2, y0 = c - rIn * 0.56f;
            using (var key = new System.Drawing.Drawing2D.GraphicsPath())
            {
                float d = kh * 0.6f;
                key.AddArc(x0, y0, d, d, 180, 90); key.AddArc(x0 + kw - d, y0, d, d, 270, 90); key.AddArc(x0 + kw - d, y0 + kh - d, d, d, 0, 90); key.AddArc(x0, y0 + kh - d, d, d, 90, 90);
                key.CloseFigure();
                g.FillPath(acc, key);
            }
            using var dark = new SolidBrush(Dark);
            g.DrawString("ENTER", num, dark, new RectangleF(x0, y0, kw, kh), Mid);
            g.DrawString(role, small, grey, new RectangleF(x0 + kw + 6, y0, rz.Width + 4, kh), new StringFormat { LineAlignment = StringAlignment.Center });
            var box = new RectangleF(c - rIn * 0.8f, c - rIn * 0.3f, rIn * 1.6f, rIn * 0.8f);
            var txt = sg2.Text;
            var sf = Fit(g, txt, Font1, s / 26f, FontStyle.Bold, box.Size);
            if (sf.Size < s / 40f) { sf.Dispose(); sf = F(Font1, s / 40f, FontStyle.Bold); }   // not smaller than legible
            float lim = Math.Min(box.Height, sf.GetHeight(g) * 3.2f);   // max. three lines, otherwise truncated at the end
            if (g.MeasureString(txt, sf, (int)box.Width).Height > lim)
            {
                while (txt.Contains(' ') && g.MeasureString(txt + "…", sf, (int)box.Width).Height > lim) txt = txt[..txt.LastIndexOf(' ')].TrimEnd(',');
                txt += "…";
            }
            g.DrawString(txt, sf, acc, box, Mid);
            sf.Dispose();
        }
        var tinyText = note ?? L("⌫ zurück · Esc · +/− Größe", "⌫ back · Esc · +/− size");
        using var tiny = Fit(g, tinyText, Font1, s / 64f, FontStyle.Regular, new SizeF(rIn * 1.3f, rIn * 0.2f));
        g.DrawString(tinyText, tiny, note != null ? acc : grey, new RectangleF(c - rIn, c + rIn * 0.56f, 2 * rIn, rIn * 0.22f), Mid);
    }

    // ------------------------------------------------------------ Keyboard (global hook, only when DCS is in front)
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, System.Text.StringBuilder s, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    HookProc? hookProc;
    IntPtr hook;
    readonly HashSet<int> swallowed = new();

    IntPtr Hook(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(l);
            bool keyDown = w == 0x100 || w == 0x104, keyUp = w == 0x101 || w == 0x105;
            if (keyDown && OnKey(vk)) { swallowed.Add(vk); return 1; }
            if (keyUp && vk == 0x2C && Visible) { swallowed.Remove(vk); BeginInvoke(Shot); return 1; }   // Print: on release (arrives reliably, once per press), after the hook (which must return quickly)
            if (keyUp && swallowed.Remove(vk)) return 1;
        }
        return CallNextHookEx(hook, code, w, l);
    }

    bool OnKey(int vk)
    {
        if (vk == cfg.WheelKey)
        {
            if (swallowed.Contains(vk)) return true;          // key repeat
            if (!Visible && !DcsInFront()) return false;      // type normally in other programs
            if (Visible) Close2(); else Open();
            return true;
        }
        if (!Visible) return false;
        if (vk is >= 0x31 and <= 0x39) { Choose(vk - 0x31); return true; }   // 1–9
        if (vk is >= 0x61 and <= 0x69) { Choose(vk - 0x61); return true; }   // Numpad 1–9
        if (vk is 0x30 or 0x60 or 0x0D) { Suggested(); return true; }        // 0 / Enter
        if (vk == 0x08) { Back(); return true; }
        if (vk == 0x1B) { Close2(); return true; }
        if (vk == 0x2C) return true;   // Print: screenshot on release (hook), do not open Windows Snipping Tool
        if (vk is 0xBB or 0x6B or 0xBD or 0x6D)   // + / - : size
        {
            cfg.WheelSize = Math.Round(Math.Clamp(cfg.WheelSize + (vk is 0xBB or 0x6B ? 0.05 : -0.05), 0.25, 0.95), 2);
            Place(); Render();
            SaveSize(cfg.WheelSize);
            return true;
        }
        return false;
    }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, KeyInput[] inputs, int size);
    [StructLayout(LayoutKind.Explicit, Size = 40)]   // INPUT (x64) with KEYBDINPUT
    struct KeyInput { [FieldOffset(0)] public int Type; [FieldOffset(10)] public ushort Scan; [FieldOffset(12)] public uint Flags; }

    /// Operate the DCS radio menu: tap scancodes one after another, only while DCS is in front (otherwise the keys land elsewhere).
    public static void PressDcs(params ushort[] scans) => new Thread(() =>
    {
        foreach (var s in scans)
        {
            if (!DcsInFront()) return;
            foreach (uint up in new uint[] { 0, 2 })   // KEYEVENTF_SCANCODE (8), KEYUP (2)
            {
                SendInput(1, new[] { new KeyInput { Type = 1, Scan = s, Flags = 8 | up } }, Marshal.SizeOf<KeyInput>());
                Thread.Sleep(up == 0 ? 60 : 90);   // DCS polls keys per frame (≥ 1 frame pressed, 2–3 frames until the next): menu only briefly visible
            }
        }
    }) { IsBackground = true }.Start();

    static bool DcsInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out int pid);
        try { return Process.GetProcessById(pid).ProcessName.StartsWith("DCS", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    static Rectangle? DcsRect()
    {
        var h = Process.GetProcessesByName("DCS").Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
        return h != IntPtr.Zero && GetWindowRect(h, out var r) ? Rectangle.FromLTRB(r.L, r.T, r.R, r.B) : null;
    }

    public static string KeyName(int vk)
    {
        if (vk == 0xDE) return L("' (Apostroph, rechts neben Ö/;)", "' (apostrophe, right of ;)");   // US-International calls it "ACUTE/CEDILLA"
        var sb = new System.Text.StringBuilder(32);
        return GetKeyNameText((int)(MapVirtualKey((uint)vk, 0) << 16), sb, 32) > 0 ? sb.ToString() : $"0x{vk:X2}";
    }

    // ------------------------------------------------------------ HOTAS
    int lastButtons, lastPov = 65535;

    void PollJoystick()
    {
        if (cfg.WheelJoystick < 0) return;
        var (b, pov) = Program.Joy(cfg.WheelJoystick);
        int bit = 1 << (cfg.WheelButton - 1);
        if ((b & bit) != 0 && (lastButtons & bit) == 0) { if (Visible) Close2(); else if (DcsInFront()) Open(); }
        lastButtons = b;
        if (Visible && pov != lastPov && pov != 65535)
        {
            switch (pov)
            {
                case 0: mark = (mark + items.Length - 1) % items.Length; Render(); break;   // up
                case 18000: mark = (mark + 1) % items.Length; Render(); break;            // down
                case 9000: Choose(mark); break;                                                // right = select
                case 27000: Back(); break;                                                     // left = back
            }
        }
        lastPov = pov;
    }
}
