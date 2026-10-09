using System.Text.RegularExpressions;

namespace DcsAtc;

/// Simulated player (--anflugtest): flies only what is announced – heading, altitude, speed. On "intercept ... runway" he captures the
/// centerline himself (strict: not, turns only on instruction), flies it on "on course"/"cleared ... approach", break/downwind/base
/// per Tower, final 3°. Reports to the Tower after "contact Tower" and reports "final". Reacts after React seconds.
class SimPilot
{
    const double NM = 1852, Ft = 0.3048, Kt = 0.514444;
    public const int React = 3;
    static readonly string Dw = string.Join("|", Tower.DigitWords), RwW = $@"((?:{Dw}) (?:{Dw})(?: left| right| center)?)";
    static readonly Regex HdgRe = new($@"heading ((?:{Dw}) (?:{Dw}) (?:{Dw}))"), FtRe = new(@"(?<!above )\b(\d{3,5}) feet(?! above| below)"),
        FlRe = new($@"flight level ((?:{Dw})(?: (?:{Dw})){{1,2}})"), KtRe = new(@"speed (\d+) knots"),
        IcptRe = new($@"(?:intercept|join) (?:the extended centerline|the localizer|final approach course) runway {RwW}"),
        RwRe = new($@"runway {RwW}"), JoinRe = new($@"join (left|right) hand downwind(?: runway {RwW})?", RegexOptions.IgnoreCase), CircRe = new($@"circle to runway {RwW}, (left|right) traffic");
    readonly Airfield F;
    readonly bool strict;
    readonly (double X, double Z) wind;
    public double X, Z, Alt, Hdg, Ias, Vs;           // m, m MSL, degrees true, m/s
    double tHdg, tAlt, tIas, side = 1, tDir;         // side: traffic pattern 1 left, -1 right; tDir: "turn right" 1, "turn left" -1 (also the long way round)
    public string Mode = "hdg";                      // hdg: heading; loc: centerline; brk: initial to runway center, then break; dw: downwind; fin: base + final
    public string Rw = "";
    string icpt = "";
    public bool Glide, Cleared, Landed;              // Landing clearance; touched down (--mptest)
    public string Cs = "Enfield 1-1";                // Callsign of its own radio calls
    bool finalCalled, extend, iniDue, iniCalled, ovhDue;   // extend: "extend downwind" heard, base only on instruction; iniDue/ovhDue: "report initial"/"report overhead" heard; iniCalled: "initial" reported
    string due = "";   // required landing report: base / final / four miles final (reporting obligation: no landing clearance without the report)
    readonly List<(double At, string Text)> inbox = new(), outbox = new();

    public SimPilot(Airfield f, double x, double z, double alt, double hdg, double ias, (double X, double Z) w, bool strict)
    {
        F = f; this.strict = strict; wind = w;
        (X, Z, Alt, Hdg, Ias) = (x, z, alt, hdg, ias);
        (tHdg, tAlt, tIas) = (hdg, alt, ias);
    }

    public Telemetry Tel() => new(Alt, Alt - F.Elev, Ias, Hdg * Math.PI / 180, X, Z, wind.X, wind.Z, 1013.25 * Math.Pow(1 - 2.25577e-5 * Alt, 5.25588), Vs);   // Air pressure (hPa) of the standard atmosphere
    /// R352 (test rule): runways named in a runway announcement ("in use", "expect …", "approved"), otherwise null.
    internal static string[]? RwAnnounced(string text) => Regex.IsMatch(text, "in use|expect|approved") && RwRe.Matches(text.ToLower()) is { Count: > 0 } ms ? ms.Select(m => m.Groups[1].Value).ToArray() : null;
    /// R352 (test rule): landing clearance for a runway other than the last announced one -> error text.
    internal static string? RwCheck(string[]? ann, string text) =>
        text.Contains("cleared to land") && RwRe.Match(text.ToLower()) is { Success: true } m && ann != null && !ann.Contains(m.Groups[1].Value) ? $"Landung Bahn {m.Groups[1].Value}, angesagt {string.Join("/", ann)}" : null;
    static int Num(string w) => int.Parse(string.Concat(w.Split(' ').Select(d => (char)('0' + Array.IndexOf(Tower.DigitWords, d)))));
    static string RwOf(string w) { var p = w.Split(' '); return $"{Num(p[0] + " " + p[1]):00}" + (p.Length > 2 ? char.ToUpper(p[2][0]).ToString() : ""); }
    /// Announced magnetic heading as true heading.
    public double? HdgOf(string s) => HdgRe.Match(s) is { Success: true } m ? (Num(m.Groups[1].Value) + F.MagVar + 360) % 360 : null;
    public (double A, double L) AL(RwyEnd e) { double dx = X - e.ThrX, dz = Z - e.ThrZ; return (-(dx * e.Dx + dz * e.Dz), dx * e.Dz - dz * e.Dx); }
    string Aligned() => icpt != "" && Tower.HdgDiff(Hdg, F.End(icpt).Hdg) < 30 ? icpt   // Parallel runways: the announced centerline, otherwise the one he is on
        : F.Ends.Where(e => Tower.HdgDiff(Hdg, e.Hdg) < 30).MinBy(e => Math.Abs(AL(e).L))?.Name ?? F.Ends.MinBy(e => Tower.HdgDiff(Hdg, e.Hdg))!.Name;

    public void Hear(string s, double now) => inbox.Add((now + React, s));

    void Apply(string s, double now)
    {
        bool fixedTrack = Mode is "brk" or "dw" or "fin" || Mode == "loc" && Glide;   // Pattern/final: headings (PAR, steering) are advisory only
        if (HdgOf(s) is { } h && !fixedTrack && !(Mode == "loc" && Tower.HdgDiff(h, F.End(Rw).Hdg) < 15)) { tHdg = h; Mode = "hdg"; Glide = false; tDir = s.Contains("urn right") ? 1 : s.Contains("urn left") ? -1 : 0; }   // ≈ runway heading: stays on the centerline
        if (!strict && IcptRe.Match(s) is { Success: true } ic) icpt = RwOf(ic.Groups[1].Value);
        var alt = Regex.Replace(s, @"[Tt]raffic[^.]*?(?=advise you|\.|$)\.?", "");   // Traffic advisories ("..., 100 feet", "climbing through 1500 feet") are not an altitude instruction, the avoidance altitude in the safety alert ("advise you … climb to …", R214) is
        var fm = FtRe.Match(alt);
        var lm = FlRe.Match(alt);
        if (lm.Success && (!fm.Success || lm.Index < fm.Index)) tAlt = Num(lm.Groups[1].Value) * 100 * Ft;
        else if (fm.Success) tAlt = int.Parse(fm.Groups[1].Value) * Ft;
        if (KtRe.Match(s) is { Success: true } k) tIas = int.Parse(k.Groups[1].Value) * Kt;
        if (s.Contains("final approach speed")) tIas = 140 * Kt;
        if (!fixedTrack && Mode == "hdg" && Regex.Match(s, $@"until established[^,]*, cleared (?:ILS|P A R) approach {RwRe}") is { Success: true } ce && Tower.HdgDiff(tHdg, F.End(RwOf(ce.Groups[1].Value)).Hdg) >= 10)
        { icpt = RwOf(ce.Groups[1].Value); Glide = !s.Contains("circle to"); }   // R353: intercept heading with the clearance: fly it, capture, then the glide path
        else if (!fixedTrack && (s.Contains("on course") || Regex.IsMatch(s, @"cleared (ILS|P A R|straight in) approach|proceed straight in")))
        {
            Mode = "loc"; Rw = Aligned(); Glide = (s.Contains("approach runway") || s.Contains("proceed straight in runway")) && !s.Contains("circle to");   // R212: circling: stay at altitude
        }
        if (s.Contains("orbit left hand")) Mode = "orb";   // Holding: left-hand circle on the spot
        if (s.Contains("cleared to land")) Cleared = true;
        if (s.Contains("go around")) { Mode = "hdg"; Glide = Cleared = finalCalled = false; icpt = ""; tHdg = HdgOf(s) ?? (Rw != "" ? F.End(Rw).Hdg : Hdg); }   // fly straight ahead climbing or missed-approach heading, then as announced
        if (s.Contains("report initial")) iniDue = true;   // reports "initial" himself (Tower gives the break only on the call)
        if (s.Contains("report overhead")) ovhDue = true;
        if (Regex.Match(s, @"report (base|final|four miles? final)") is { Success: true } rp) { due = rp.Groups[1].Value; finalCalled = false; }   // new required report
        // Break clearance = answer to the "initial" call (R203: "runway two five, report base", right-hand pattern "right turns")
        if (iniCalled && Mode == "loc" && s.Contains("report base") && RwRe.Match(s) is { Success: true } bm) { iniCalled = false; Mode = "brk"; Rw = RwOf(bm.Groups[1].Value); side = s.Contains("right turns") ? -1 : 1; }
        if (JoinRe.Match(s) is { Success: true } j)
        {
            Mode = "dw"; side = j.Groups[1].Value.ToLowerInvariant() == "right" ? -1 : 1; tIas = 160 * Kt;
            ovhDue = false;
            if (j.Groups[2].Success) Rw = RwOf(j.Groups[2].Value);
        }
        if (CircRe.Match(s) is { Success: true } cr) { Mode = "dw"; Rw = RwOf(cr.Groups[1].Value); side = cr.Groups[2].Value == "right" ? -1 : 1; tIas = 160 * Kt; }   // R212: circling: from the approach into the pattern of the other runway
        if (s.Contains("extend downwind")) extend = true;
        if (s.Contains("turn base")) { Mode = "fin"; tIas = 140 * Kt; extend = false; }
        if (s.Contains("contact " + F.StationOf("Tower"))) outbox.Add((now + 4, $"{F.StationOf("Tower")}, {Cs}, inbound"));
    }

    /// Course over ground onto the line (point p, direction h): 1 NM lateral = 60°, at most 90°.
    double Track(double h, (double X, double Z) p)
    {
        double r = h * Math.PI / 180, left = (X - p.X) * Math.Sin(r) - (Z - p.Z) * Math.Cos(r);
        return h + Math.Clamp(left / NM * 60, -90, 90);
    }
    /// Lead against the crosswind.
    double Crab(double trk, double tas)
    {
        double r = trk * Math.PI / 180, wr = -wind.X * Math.Sin(r) + wind.Z * Math.Cos(r);
        return trk - Math.Asin(Math.Clamp(wr / tas, -1, 1)) * 180 / Math.PI;
    }

    public void Step(double now)
    {
        foreach (var m in inbox.Where(m => m.At <= now).ToList()) { inbox.Remove(m); Apply(m.Text, now); }
        double tas = Ias * (1 + 0.02 * Alt / Ft / 1000), want = tHdg;
        // Capture centerline: lead the turn so he rolls out on it; only on the announced heading (as in reality). Otherwise he captured while still in the turn toward it,
        // across the line, but in "loc" flew 90° toward it and shot through (Kerman 1751 m: 350 kt TAS, turn radius 3 NM, 1.6 NM over -> landing clearance from radar vectoring).
        // Almost parallel, just off to the side ("join ... heading 290", Shiraz 0.4 NM): turn in.
        if (Mode == "hdg" && icpt != "" && Tower.HdgDiff(Hdg, tHdg) < 5)
        {
            var ie = F.End(icpt);
            var (a, l) = AL(ie);
            double dh = Tower.HdgDiff(Hdg, ie.Hdg), r = tas * tas / (9.81 * Math.Tan(Math.PI / 6));
            double vLeft = Math.Cos(Hdg * Math.PI / 180) * ie.Dz - Math.Sin(Hdg * Math.PI / 180) * ie.Dx;
            if (a > 0 && dh < 120 && (Math.Abs(l) < r * (1 - Math.Cos(Math.Min(dh, 90) * Math.PI / 180)) + 100 && l * vLeft <= 0 || dh < 10 && Math.Abs(l) < 0.5 * NM)) { Mode = "loc"; Rw = icpt; }
        }
        var e = Rw != "" ? F.End(Rw) : null;
        if (Mode == "brk" && AL(e!).A < -e!.Len / 2) { Mode = "dw"; tIas = 160 * Kt; }
        if (Mode == "dw" && !extend && AL(e!).A > 3000 + 80 * Math.Max(0, (wind.X * e!.Dx + wind.Z * e.Dz) / Kt)) { Mode = "fin"; tIas = 140 * Kt; }   // Base himself (as in reality, tailwind: later), extended only on instruction
        if (Mode is "loc" or "brk" or "fin") want = Crab(Track(e!.Hdg, (e.ThrX, e.ThrZ)), tas);
        if (Mode == "dw") want = Crab(Track(e!.Hdg + 180, e.At(0, side * 1.3 * NM)), tas);
        if (Mode == "orb") want = Hdg - 90;
        double bank = Mode is "dw" ? 60 : Mode == "fin" ? 45 : 30, rate = 9.81 * Math.Tan(bank * Math.PI / 180) / tas * 180 / Math.PI;
        if (Landed) want = e!.Hdg;
        double dt = ((want - Hdg) % 360 + 540) % 360 - 180;
        if (Mode == "hdg" && tDir * dt < 0 && Math.Abs(dt) > 2 * rate) dt += 360 * tDir;   // the announced way round
        if (Math.Abs(dt) <= rate) tDir = 0;
        Hdg = (Hdg + Math.Clamp(dt, -rate, rate) + 360) % 360;
        Ias += Math.Clamp(tIas - Ias, -1, 1);
        double ta = tAlt;
        bool gl = e != null && (Mode == "fin" || Mode == "loc" && Glide);
        if (gl) ta = Math.Min(ta, F.Elev + Math.Max(AL(e!).A, 0) * Math.Tan(3 * Math.PI / 180) + 15);
        if (gl && Cleared && AL(e!).A < 0.3 * NM) ta = F.Elev;   // touch down
        if (gl && !Cleared && !Landed && AL(e!).A < 0.5 * NM)   // no landing clearance: go around himself
        {
            Mode = "hdg"; Glide = finalCalled = false; tHdg = e!.Hdg; tAlt = F.PatternFt * Ft;
            outbox.Add((now + 1, $"{F.StationOf("Tower")}, {Cs}, going around"));
        }
        Vs = Math.Clamp((ta - Alt) / 4, gl ? -10 : -7.6, 7.6);   // 1500 ft/min, more on the glide path
        Alt += Vs;
        if (gl && Cleared && Alt <= F.Elev + 0.5) { Landed = true; tIas = 0; }
        if (Landed) { Alt = F.Elev; Vs = 0; Ias = Math.Max(0, Ias - 3); tas = Ias; }   // roll out
        X += tas * Math.Cos(Hdg * Math.PI / 180) + (Landed ? 0 : wind.X);
        Z += tas * Math.Sin(Hdg * Math.PI / 180) + (Landed ? 0 : wind.Z);
    }

    /// Own radio call due (reporting obligation: all mandatory reports himself): Tower call after the handoff, "initial", "overhead",
    /// landing report "base" when turning onto base, "four mile final" or otherwise "final" aligned under 3.5 NM.
    public string? Call(double now)
    {
        if (!finalCalled && Rw != "" && AL(F.End(Rw)) is var (a, l) && a > 0.5 * NM)
        {
            double dh = Tower.HdgDiff(Hdg, F.End(Rw).Hdg);
            var rep = Mode == "fin" && due == "base" && dh is < 160 and > 30 ? "base, gear down, full stop"
                    : Mode == "loc" && Glide && due.StartsWith("four") && a < 4.2 * NM && Math.Abs(l) < 0.5 * NM ? "four mile final, gear down"
                    : (Mode == "fin" || Mode == "loc" && Glide) && a < 3.5 * NM && Math.Abs(l) < 0.3 * NM && dh < 20 ? "final, gear down, full stop" : null;
            if (rep != null)
            {
                finalCalled = true;
                return $"{F.StationOf("Tower")}, {Cs}, {rep}";
            }
        }
        var o = outbox.FirstOrDefault(m => m.At <= now);
        if (o.Text == null && iniDue && Mode == "loc" && !Glide && Rw != "" && AL(F.End(Rw)) is var (ia, il) && ia > 0 && ia < Tower.InitialDist + NM && Math.Abs(il) < 0.5 * NM)
        {
            iniDue = false; iniCalled = true;
            return $"{F.StationOf("Tower")}, {Cs}, initial";
        }
        if (o.Text == null && ovhDue && F.Ends.Min(e => Math.Sqrt((X - e.CX) * (X - e.CX) + (Z - e.CZ) * (Z - e.CZ))) < NM)
        {
            ovhDue = false;
            return $"{F.StationOf("Tower")}, {Cs}, overhead";
        }
        if (o.Text == null) return null;
        outbox.Remove(o);
        return o.Text;
    }
}

public partial class Tower
{
    /// DcsAtc --anflugtest [Platz|*] [Bahn|*] [vfr|ifr|*] [Richtung|*] [strict]: every airfield × both landing directions (10 kt headwind) × VFR/IFR
    /// × 8 directions from 38 NM (altitude above terrain), pilot flies only as instructed until landing clearance. A single run shows the radio traffic.
    public static int AnflugTest(IReadOnlyList<Airfield> fields, string[] args)
    {
        string A(int i) => args.Length > i ? args[i] : "*";
        bool strict = args.Contains("strict");
        var runs = (from f in fields where A(0) == "*" || f.Name.Contains(A(0), StringComparison.OrdinalIgnoreCase)
                    from e in f.Ends where A(1) == "*" || e.Name == A(1)
                    from ifr in new[] { false, true } where A(2) == "*" || A(2) == (ifr ? "ifr" : "vfr")
                    from dir in Enumerable.Range(0, 8).Select(k => k * 45) where A(3) == "*" || dir.ToString() == A(3)
                    select (f, e.Name, ifr, dir)).ToList();
        int bad = 0;
        foreach (var (f, rw, ifr, dir) in runs)
        {
            var (ok, info, log) = SimRun(f, rw, ifr, dir, strict);
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {f.Name} {rw} {(ifr ? "IFR" : "VFR")} {dir:000}°: {info}");
            if (runs.Count == 1 || Environment.GetEnvironmentVariable("ANFLUGLOG") is var al && (al == "1" && !ok || al == "all")) log.ForEach(l => Console.WriteLine("    " + l));
        }
        ForceIfr = false;
        Console.WriteLine($"{runs.Count - bad}/{runs.Count} OK");
        return bad == 0 ? 0 : 1;
    }

    static readonly string[] Bad = { "check runway", "check altitude", "low altitude alert", "expedite", "wrong side", "negative", "not following", "go around", "too low", "unable", "say again", "pattern is south", "no transmissions", "verify heading" };   // R271: PAR lost-comm ("… five seconds on final approach") only with talk-down

    static (bool Ok, string Info, List<string> Log) SimRun(Airfield f, string rw, bool ifr, double dir, bool strict)
    {
        var e = f.End(rw);
        var wind = (X: -e.Dx * 5, Z: -e.Dz * 5);   // 10 kt headwind: DCS (and the AI) land on rw
        ForceIfr = ifr;
        double x = f.X + 38 * NM * Math.Cos(dir * Math.PI / 180), z = f.Z + 38 * NM * Math.Sin(dir * Math.PI / 180);
        double ft = Math.Max(10000, Math.Ceiling(f.MvaLeg(x, z, f.X, f.Z) / 1000) * 1000);   // like a pilot: above the minimum altitude of the direct route
        var p = new SimPilot(f, x, z, ft * Ft, Bearing(x, z, f.X, f.Z), 300 * Kt, wind, strict);
        // ANFLUGSTART=x,z,ft,hdg,kt: start from a logged position ([TEL] line) instead of 38 NM out
        if (Environment.GetEnvironmentVariable("ANFLUGSTART")?.Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray() is [var sx, var sz, var sf, var sh, var sk])
            p = new SimPilot(f, sx, sz, sf * Ft, sh, sk * Kt, wind, strict);
        var tw = new Tower(f, "Enfield 1-1") { FieldWind = wind };
        var none = new List<Traffic>();
        var log = new List<string>();
        var errs = new List<string>();
        int calls = 0;
        double maxTurn = 0, clrVec = 1e9, clrTwr = 1e9, clrFin = 1e9, hoLat = double.NaN, hoFt = double.NaN, hoA = double.NaN, brkFt = double.NaN, clearedAt = double.NaN, sec = 0;
        double lastApp = -1e9, pilotAt = -1e9;   // R351: last Approach call, last pilot call
        int sinceIcpt = -1;   // R353: Approach calls since the intercept vector, -1 = none pending
        string[]? annRw = null;   // R352: runways of the last runway announcement
        var alts = new List<(double S, string A)>();   // R355: altitudes assigned by Approach
        void Hear(List<Msg> ms, bool first = false)
        {
            foreach (var m in ms.Where(m => m.Role != "Info"))
            {
                var (pa, pl) = p.AL(f.End(tw.Runway));
                log.Add($"{sec,4}s {(p.Alt / Ft - f.Elev / Ft),5:0} ft a={pa / NM,5:0.0} l={pl / NM,5:0.0} h={p.Hdg,3:0} {p.Mode,-3} {m.Role}: {m.Text}");
                if (Bad.FirstOrDefault(b => m.Text.Contains(b, StringComparison.OrdinalIgnoreCase)) is { } b) errs.Add($"\"{b}\" @{sec}s");
                if (SimPilot.RwCheck(annRw, m.Text) is { } rwErr) errs.Add($"{rwErr} @{sec}s");
                annRw = SimPilot.RwAnnounced(m.Text) ?? annRw;
                if (m.Role == "Approach")
                {
                    // R351: next vector only after the previous one was flown: two calls within 10 s without a pilot call in between (except the handoff)
                    if (!first && sec > lastApp && sec - lastApp <= 10 && lastApp > pilotAt && !m.Text.Contains("contact ")) errs.Add($"zwei Ansagen binnen {sec - lastApp:0} s @{sec}s");
                    lastApp = sec;
                    // R353: approach clearance with the intercept vector, at most one call in between
                    if (Regex.IsMatch(m.Text, @"cleared (ILS|P A R) approach")) { if (sinceIcpt > 1) errs.Add($"Freigabe {sinceIcpt} Ansagen nach dem Eindrehvektor @{sec}s"); sinceIcpt = -1; }
                    else if (m.Text.Contains("intercept")) sinceIcpt = 0;
                    else if (sinceIcpt >= 0 && !Regex.IsMatch(m.Text, @"^[^,]+, (descend|climb) and maintain [^,]+\.$")) sinceIcpt++;   // profile descent alone (R305, long final) does not count
                    // R355: no other altitude shortly before the handoff
                    if (Regex.Match(m.Text, @"maintain (flight level [a-z ]+?|\d+ feet)\b") is { Success: true } am)
                    {
                        if (m.Text.Contains("contact") && m.Text.Contains("Tower") && alts.LastOrDefault(x => sec - x.S <= 10 && x.A != am.Groups[1].Value) is { A: { } oa }) errs.Add($"{oa} und {am.Groups[1].Value} vor der Übergabe @{sec}s");
                        alts.Add((sec, am.Groups[1].Value));
                    }
                }
                if (m.Role == "Approach" && tw.Phase == Phase.Inbound)
                {
                    calls++;
                    if (!first && p.HdgOf(m.Text) is { } h) maxTurn = Math.Max(maxTurn, HdgDiff(h, p.Hdg));
                }
                if (double.IsNaN(brkFt) && Regex.IsMatch(m.Text, @"report base|hand downwind|cleared (ILS|P A R|straight in) approach|proceed straight in"))   // Altitude at initial/overhead or above the 3° glide path at approach clearance
                    brkFt = m.Text.Contains(" approach") ? (p.Alt - f.Elev - Math.Max(p.AL(f.End(tw.vrw != "" ? tw.vrw : tw.Runway)).A, 0) * Math.Tan(3 * Math.PI / 180)) / Ft : p.Alt / Ft - tw.PatternFt;
                p.Hear(m.Text, sec);
            }
        }
        tw.Tick(p.Tel(), none, 0);
        sec = 1;
        Hear(tw.OnTranscript($"{f.StationOf("Approach")}, Enfield 1-1, inbound for landing", p.Tel(), none, 1), true);
        var prev = tw.Phase;
        for (sec = 2; sec < 3600 && tw.Phase != Phase.ClearedLand; sec++)
        {
            p.Step(sec);
            var t = p.Tel();
            var ms = tw.Tick(t, none, sec);
            if (p.Call(sec) is { } call)
            {
                log.Add($"{sec,4}s Pilot: {call}");
                pilotAt = sec;
                ms.AddRange(tw.OnTranscript(call, t, none, sec));
            }
            Hear(ms);
            double clr = p.Alt / Ft - f.TerrainFt(p.X, p.Z, 0);
            var (a, l) = p.AL(f.End(tw.Runway));
            if (tw.Phase == Phase.Inbound) clrVec = Math.Min(clrVec, clr);
            else if (p.Mode == "fin" || p.Glide) { if (a > 2 * NM) clrFin = Math.Min(clrFin, clr); }
            else clrTwr = Math.Min(clrTwr, clr);
            if (prev == Phase.Inbound && tw.Phase == Phase.Entering)
            {
                hoLat = Math.Abs(p.AL(f.End(tw.vrw != "" ? tw.vrw : tw.Runway)).L) / NM;
                hoFt = p.Alt / Ft - tw.GateFt;
                hoA = (p.AL(f.End(tw.vrw != "" ? tw.vrw : tw.Runway)).A - tw.gate) / NM;   // R305: handoff at the gate (up to 2 NM before it), not 25 NM out
            }
            if (tw.Phase == Phase.ClearedLand) clearedAt = a / NM;
            prev = tw.Phase;
        }
        if (tw.Phase != Phase.ClearedLand) errs.Add($"keine Landefreigabe ({tw.Phase}, {p.Mode})");
        else if (clearedAt < 1) errs.Add($"Landefreigabe erst {clearedAt:0.0} NM");
        else if (p.Mode != "fin" && !p.Glide) errs.Add($"Landefreigabe vor der Platzrunde ({p.Mode})");
        if (maxTurn > 120) errs.Add($"Kurve {maxTurn:0}°");
        if (calls > 15) errs.Add($"{calls} Radar-Ansagen");
        if (clrVec < 900) errs.Add($"Gelände Radar {clrVec:0} ft");
        if (clrTwr < 300) errs.Add($"Gelände Platz {clrTwr:0} ft");
        if (clrFin < 200) errs.Add($"Gelände Endanflug {clrFin:0} ft");
        if (hoLat > 1.5) errs.Add($"Übergabe {hoLat:0.0} NM seitlich");
        if (hoA > 2.1) errs.Add($"Übergabe {hoA:0.0} NM vor dem Gate");   // R305: Approach leads to 2 NM before the gate
        string C(double v) => v > 1e8 ? "-" : v.ToString("0");
        var info = $"{sec,4} s, Bahn {tw.Runway}{(tw.vrw != tw.Runway && tw.vrw != "" ? $" via {tw.vrw}" : "")}, {calls} Ansagen, Kurve {maxTurn:0}°, Gelände {C(clrVec)}/{C(clrTwr)}/{C(clrFin)} ft, " +
                   $"Übergabe {hoLat:0.0} NM {hoA:+0.0;-0.0} NM vor Gate {hoFt:+0;-0} ft, Höhe {brkFt:+0;-0} ft";
        return (errs.Count == 0, errs.Count == 0 ? info : string.Join(", ", errs) + " | " + info, log);
    }
}
