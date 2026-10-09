using System.Text.RegularExpressions;
using static DcsAtc.Tower;

namespace DcsAtc;

/// JTAC / FAC(A) close air support (JTAC-PLAN J5-J12): leaders and visible enemy groups come from the mission (J/Q/N state lines),
/// the app talks: check-in, 9-line, readback, mark, clearance, BDA. One CAS contact per player (cas), a small table per leader kind.
partial class Ops
{
    public record Leader(string Id, string Kind, string Cs, int Side, double X, double Z, double Alt, double Freq, string Mod, string Code, bool Alive);   // J line
    public record Trg(string Id, string Group, string Type, int Count, double X, double Z, double Alt, bool Moving, bool Flak, string Mgrs, double Lat, double Lon, double Fx, double Fz);   // Q line; Fx/Fz NaN = no friendlies near
    public static List<Leader> Leaders = new();
    public static List<Trg> Targets = new();
    public static List<(int Side, string Name, double X, double Z)> NavPts = new();   // N line: mission navigation points (IP)
    static readonly List<Ops> casAll = new();   // players in contact with a leader, in check-in order (E5: one attack at a time)
    static readonly Dictionary<string, (string Role, string Mark, int Delay)> CasKind = new() { ["jtac"] = ("JTAC", "smoke_red", 30), ["faca"] = ("FACA", "wp", 0) };   // J6: voice/role, mark command, delay in s

    // cas: 0 off, 1 checked in, 2 9-line given, 3 readback ok, 4 IN (continue), 5 cleared hot, 6 off / BDA
    int cas, casType, casHoldAlt, casFmt, casKills, casTotal, casSide;   // casFmt: 0 by aircraft type, 1 MGRS, 2 lat/long
    string casL = "", casLcs = "", casCs = "", casG = "", casNoun = "", casEl4 = "", casLoc6 = "", casRb4 = "", casMarkTxt = "";
    bool casHold, casTally, casLaser, casDanger;
    double casFah, casEgr, casKillAt, casMarkAt, casGone = -1, casAt;
    readonly string[] casLines = new string[10];   // spoken 9-line lines 1-9, [0] remarks
    readonly List<Call> casOut = new();   // from mission events, delivered by Tick
    public readonly List<string> Cmds = new();   // MARK / LASE / LASEOFF for the mission (Program drains them to AiCmd)
    public int Cas => cas;
    public static double DefFreq = 133.5;   // JTAC frequency from the config (leader without own frequency)

    static string CsKey(Leader l) => Normalize(l.Cs).Split(' ')[0];
    static bool Laser(string type) => Regex.IsMatch(type, @"^(?:A-10|FA-18|F/A-18|F-16|AV8B|AH-64|F-15E|F-14|Ka-50)");
    static bool MgrsType(string type) => Regex.IsMatch(type, @"^(?:A-10|AH-64|Ka-50)");   // E3: format as the avionics takes it
    static readonly string[] Nato = { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa", "quebec", "romeo", "sierra", "tango", "uniform", "victor", "whiskey", "xray", "yankee", "zulu" };
    static readonly string[] Num = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
    static string Meters(double m) => Digits(((int)Math.Round(m / 50) * 50).ToString()) + " meters";
    static string Cnt(int n) => n is >= 0 and <= 10 ? Num[n] : n.ToString();
    static string Noun(Trg t) => t.Count == 1 ? Regex.Replace(TypeSay(t.Type), @"^(tank|APC|IFV|truck|vehicle)s$", "$1") : TypeSay(t.Type);
    static string Desc(Trg t) => $"{Cnt(t.Count)} {Noun(t)}";
    static double? CardOf(string n)
    {
        var m = Regex.Match(n, @"\b(north ?east|north ?west|south ?east|south ?west|north|south|east|west)\b");
        return !m.Success ? null : Array.IndexOf(new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" }, m.Value.Replace(" ", "")) * 45.0;
    }

    Leader? LeaderFor(Me me, string n = "")
    {
        var mine = Leaders.Where(l => l.Alive && l.Side == me.Coalition).ToList();
        return mine.FirstOrDefault(l => n != "" && CsKey(l) is { Length: > 2 } k && Has(n, k)) ?? mine.FirstOrDefault(l => l.Id == casL)
            ?? (me.Tel is { } t ? mine.OrderBy(l => Dist(t.X, t.Z, l.X, l.Z)).FirstOrDefault() : mine.FirstOrDefault());
    }
    (string Name, double X, double Z)? IpFor(Me me, Trg t) =>
        NavPts.Where(p => p.Side == me.Coalition && Dist(p.X, p.Z, t.X, t.Z) is > 3000 and < 60000)
              .OrderBy(p => me.Tel is { } m ? Dist(p.X, p.Z, m.X, m.Z) : Dist(p.X, p.Z, t.X, t.Z)).Select(p => ((string, double, double)?)(p.Name, p.X, p.Z)).FirstOrDefault();

    /// J8: type of control: FAC(A) with the player in sight Type 1, several groups Type 3, otherwise Type 2.
    int TypeOf(Leader l, Me me) =>
        l.Kind == "faca" && me.Tel is { } t && Dist(t.X, t.Z, l.X, l.Z) < 15000 ? 1 : Targets.Count(q => q.Id == l.Id) >= 2 ? 3 : 2;

    /// J7: target of the leader: air defense first, then moving, then the nearest.
    static Trg? Pick(Leader l) => Targets.Where(t => t.Id == l.Id).OrderByDescending(t => t.Flak).ThenByDescending(t => t.Moving).ThenBy(t => Dist(t.X, t.Z, l.X, l.Z)).FirstOrDefault();
    Trg? Cur(Leader l) => Targets.FirstOrDefault(t => t.Id == l.Id && t.Group == casG);

    Call C(Leader l, Me me, string t) => new(CasKind[l.Kind].Role, l.Cs, $"{Cs(me)}, {SpokenCallsign(l.Cs)}, {t}", l.Freq > 0 ? l.Freq : DefFreq);

    void CasEnd()
    {
        if (casLaser) Cmds.Add($"LASEOFF;{casL}");
        casAll.Remove(this);
        (cas, casL, casLcs, casG, casHold, casTally, casLaser, casKills, casTotal, casMarkAt, casGone, casFmt) = (0, "", "", "", false, false, false, 0, 0, 0, -1, 0);
    }

    List<Call> JtacCall(string n, Me me, double now, bool unsure = false)
    {
        casCs = me.Callsign;
        var l = LeaderFor(me, n);
        if (l == null) return new() { new("Info", "JTAC", L("JTAC: keiner auf deiner Seite.", "JTAC: none on your side.")) };
        if (cas > 0 && l.Id != casL) CasEnd();
        List<Call> Say(string t) => new() { C(l, me, t) };
        var code = l.Code is { Length: > 0 } cd ? cd : "1688";
        if (unsure || Has(n, "radio check", "how do you read")) return Say(unsure ? "say again" : "read you five");
        var cur = Cur(l);
        if (Has(n, "checking out", "check out", "off station", "signing off"))
        {
            var tot = casTotal + casKills;
            CasEnd();
            return Say("copy checking out" + (tot > 0 ? $", {tot} destroyed in total" : "") + ". Good work");
        }
        if (cas >= 2 && cur != null && !Has(n, "readback", "read back") && Regex.IsMatch(n, @"\b(?:mgrs|lat long|latitude|lat lon)\b"))   // E3: "say again in MGRS / lat long"
        {
            casFmt = Has(n, "mgrs") ? 1 : 2;
            (casLines[6], casLoc6) = Loc(me, cur);
            return Say($"line 6, {casLines[6]}");
        }
        if (cas >= 2 && Regex.Match(n, @"say again (?:the |your )?(?:line )?(\d)\b") is { Success: true } sl && int.Parse(sl.Groups[1].Value) is >= 1 and <= 9 and var ln && casLines[ln] != null)
            return Say(casLines[ln] == "" ? $"line {ln} not applicable" : $"line {ln}, {casLines[ln]}");
        if (Has(n, "say again") && lastRole.TryGetValue("JTAC", out var lc)) return lc;
        if (Has(n, "laser code")) return Say($"laser code {Digits(code)}");
        if (Has(n, "threat", "air defense")) return Say(Threats(l, cur) is { Length: > 0 } th ? th : "no threats observed");
        if (cas == 0) return CheckIn(l, me, now);
        if (Has(n, "ready to copy", "ready for 9 line", "ready for nine line", "go ahead", "ready for tasking", "ready for next", "ready for re attack", "ready for the 9 line"))   // 9-line (also new target / re-attack)
        {
            if (casHold) return Say("stand by, hold as directed");
            if (cas is >= 3 and <= 5) return Say("9-line is out, say again which line");
            var nt = cur ?? Pick(l);
            if (nt == null) return Say("no more targets observed, stand by");
            casG = nt.Group;
            return NineLine(l, me);
        }
        if (cas == 2 && (Has(n, "readback", "read back", "line 4", "line four") || Regex.IsMatch(n, @"\d")))
        {
            var digs = string.Concat(n.Where(char.IsDigit));
            if (digs.Length == 0) return Say("say again your readback");
            bool ok4 = digs.Contains(casEl4), ok6 = digs.Contains(casLoc6);
            if (ok4 && ok6) { cas = 3; return Say("readback correct" + (casType == 3 ? ", you are cleared to engage when ready" : ", advise when ready for mark or IN")); }
            return Say(string.Join(". ", new[] { !ok4 ? $"negative line 4, line 4 {casLines[4]}" : "", !ok6 ? $"negative line 6, line 6 {casLines[6]}" : "" }.Where(s => s != "")) + ". Say again your readback");
        }
        if (Has(n, "laser off", "cease laser", "stop lasing")) { if (casLaser) { casLaser = false; Cmds.Add($"LASEOFF;{casL}"); } return Say("laser off"); }
        if (Has(n, "laser on", "request laser", "sparkle", "lase"))
        {
            if (cas < 3 || cur == null) return Say(cas < 3 ? "negative, readback first" : "no target to lase");
            casLaser = true; Cmds.Add($"LASE;{casL};{casG};{code}");
            return Say($"laser on, code {Digits(code)}");
        }
        if (Has(n, "request mark", "mark my target", "mark target", "smoke", "mark on request", "mark please", "request smoke"))
        {
            if (cas < 3 || cur == null) return Say(cas < 3 ? "negative, readback first" : "no target to mark");
            var (_, kind, delay) = CasKind[l.Kind];
            Cmds.Add($"MARK;{casL};{casG};{kind};{delay}");
            (casMarkAt, casMarkTxt) = (now + delay, kind == "wp" ? "white phosphorus" : kind.Replace("smoke_", "") + " smoke");
            return Say(delay > 0 ? $"mark on the way, {Digits(delay.ToString())} seconds" : "rocket away, mark is " + casMarkTxt);
        }
        if (cas >= 3 && cas <= 5 && Regex.IsMatch(n, @"\bin\b") && !Has(n, "checking in", "check in") && !Regex.IsMatch(n, @"\bin (sight|trail)\b"))   // attack: IN from the west
        {
            if (Has(n, "tally", "visual", "contact")) casTally = true;
            var fromDir = CardOf(n);
            double diff = fromDir is { } fd ? HdgDiff((fd + 180) % 360, casFah) : 0;   // attack heading = reverse of the side he comes from
            var fah = Brg(casFah);
            if (diff >= 90) { cas = 3; return Say($"abort, abort, abort, final attack heading {fah}"); }
            if (!casTally && casType != 1) { cas = 4; return Say("continue, advise tally target or mark"); }
            if (diff > 40) { cas = 4; return Say($"continue, fly final attack heading {fah}"); }
            cas = 5;
            return Say((casDanger ? "danger close, " : "") + "cleared hot");
        }
        if (Has(n, "tally", "contact the mark", "contact mark", "have the mark", "visual", "have the target", "contact"))
        {
            if (cas < 3) return Say("stand by");
            casTally = true;
            return Say("roger, advise IN");
        }
        if (cas >= 4 && Has(n, "off", "winchester", "out of ammo", "complete"))
        {
            if (cas == 6) return Say("copy off");
            cas = 6;
            return Say(Bda(cur));
        }
        if (Has(n, "bda", "battle damage", "status")) return Say(cas >= 4 ? Bda(cur) : "no attack yet");
        if (Has(n, "abort")) { if (cas >= 3) cas = 3; return Say("copy abort, standing by"); }
        return Say("say again");
    }

    List<Call> CheckIn(Leader l, Me me, double now)
    {
        casAll.Remove(this);
        (cas, casL, casLcs, casSide, casAt, casG, casTally, casKills, casTotal, casLaser) = (1, l.Id, l.Cs, me.Coalition, now, "", false, 0, 0, false);
        casAll.Add(this);
        if (casAll.Any(o => o != this && o.casL == l.Id && o.cas is >= 2 and <= 5))   // E5: one attack at a time, stack with 1000 ft steps
        {
            var alt = me.Tel is { } t ? (int)Math.Round(t.AltMsl / Ft / 1000) : 10;
            while (casAll.Any(o => o != this && o.casL == l.Id && o.casHold && o.casHoldAlt == alt)) alt++;
            (casHold, casHoldAlt) = (true, alt);
            var tg = Pick(l);
            var ip = tg == null ? null : IpFor(me, tg);
            return new() { C(l, me, $"hold at {(ip is { } i ? SpokenCallsign(i.Name) : "present position")}, angels {alt}, I'll call you when I'm ready for you") };
        }
        return Situation(l, me);
    }

    /// J6: check-in answer: situation, type of control. Without a target: wait (Tick calls when one appears).
    List<Call> Situation(Leader l, Me me)
    {
        casHold = false;
        var t = Pick(l);
        if (t == null) { casG = ""; return new() { C(l, me, "no enemy observed at this time, stand by. I'll call when I have a target") }; }
        (casG, casType) = (t.Group, TypeOf(l, me));
        var fr = l.Kind == "faca" ? (double.IsNaN(t.Fx) ? "friendlies none known" : $"friendlies {Meters(Dist(t.Fx, t.Fz, t.X, t.Z))} {Dir8(Bearing(t.X, t.Z, t.Fx, t.Fz))} of the target") : "friendlies at my position";
        var where = l.Kind == "jtac" ? $", {Meters(Dist(l.X, l.Z, t.X, t.Z))} {Dir8(Bearing(l.X, l.Z, t.X, t.Z))} of my position" : "";
        var ctl = casType switch { 1 => "Type 1 control", 3 => "Type 3 control, cleared to engage in the target area", _ => "Type 2 control, bomb on target" };
        return new() { C(l, me, $"situation: enemy {Desc(t)} {(t.Moving ? "moving" : "stationary")}{where}{(t.Flak ? ", air defense present" : "")}, {fr}. {ctl}. Advise when ready for 9-line") };
    }

    /// Line 6 spoken and the digits the readback must contain. E3: MGRS for A-10C / AH-64 / Ka-50, otherwise lat/long degrees and decimal minutes.
    (string Say, string Key) Loc(Me me, Trg t)
    {
        if ((casFmt == 1 || casFmt == 0 && MgrsType(me.Type)) && Regex.Match(t.Mgrs.Replace(" ", ""), @"^(\d{1,2})([A-Z])([A-Z]{2})(\d+)$") is { Success: true } m && m.Groups[4].Value.Length % 2 == 0)
        {
            var h = m.Groups[4].Value.Length / 2;
            string N(char ch) => Nato[ch - 'A'];
            return ($"MGRS {Digits(m.Groups[1].Value)} {N(m.Groups[2].Value[0])}, {N(m.Groups[3].Value[0])} {N(m.Groups[3].Value[1])}, {Digits(m.Groups[4].Value[..h])}, {Digits(m.Groups[4].Value[h..])}", m.Groups[1].Value + m.Groups[4].Value);
        }
        string lk = "";
        string Dm(double v, string pos, string neg, int dg)
        {
            var a = Math.Abs(v); int d = (int)a; var mn = Math.Round((a - d) * 60 * 100) / 100;
            if (mn >= 60) { d++; mn = 0; }
            var (w, f) = ((int)mn, (int)Math.Round((mn - (int)mn) * 100));
            string ds = d.ToString(new string('0', dg)), ws = w.ToString("00"), fs = f.ToString("00");
            lk += ds + ws + fs;
            return $"{(v >= 0 ? pos : neg)} {Digits(ds)} degrees {Digits(ws)} decimal {Digits(fs)} minutes";
        }
        var la = Dm(t.Lat, "north", "south", 2);
        return ($"{la}, {Dm(t.Lon, "east", "west", 3)}", lk);
    }

    string Threats(Leader l, Trg? t) => string.Join("; ", Targets.Where(q => q.Id == l.Id && q.Flak).Select(q =>
        t != null && q.Group == t.Group ? "air defense at the target"
        : t == null ? $"{Noun(q)}, {Meters(Dist(q.X, q.Z, l.X, l.Z))} {Dir8(Bearing(l.X, l.Z, q.X, q.Z))} of my position"
        : $"{Noun(q)}, {Meters(Dist(q.X, q.Z, t.X, t.Z))} {Dir8(Bearing(t.X, t.Z, q.X, q.Z))} of the target"));

    /// J7: 9-line from target, leader, IP (mission navigation point), friendlies and threats.
    List<Call> NineLine(Leader l, Me me)
    {
        var t = Cur(l)!;
        casType = TypeOf(l, me);
        var ip = IpFor(me, t);
        var org = ip is { } i ? (i.X, i.Z) : me.Tel is { } m ? (m.X, m.Z) : (l.X, l.Z);
        casFah = Bearing(org.Item1, org.Item2, t.X, t.Z);
        var fr = !double.IsNaN(t.Fx) ? (t.Fx, t.Fz) : l.Kind == "jtac" && Dist(l.X, l.Z, t.X, t.Z) < 5000 ? (l.X, l.Z) : (double.NaN, double.NaN);
        bool fri = !double.IsNaN(fr.Item1);
        var fd = fri ? Dist(fr.Item1, fr.Item2, t.X, t.Z) : 0;
        casDanger = fri && fd < 1000;
        casEgr = fri && HdgDiff(Bearing(t.X, t.Z, fr.Item1, fr.Item2), casFah) < 60 ? (casFah + 180) % 360 : casFah;   // egress away from the friendlies
        casEl4 = Math.Max(0, (int)Math.Round(t.Alt / Ft / 10) * 10).ToString();
        casRb4 = Digits(casEl4);
        casLines[4] = $"elevation {casRb4} feet";
        var cd = l.Code is { Length: > 0 } c ? c : "1688";
        (casLines[1], casLines[2], casLines[3]) = ip is { } p
            ? ($"IP {SpokenCallsign(p.Name)}", $"heading {Brg(casFah)}" + (fri ? (Math.Sin((Bearing(org.Item1, org.Item2, fr.Item1, fr.Item2) - casFah) * Math.PI / 180) > 0 ? ", offset left" : ", offset right") : ""), MilesTxt(Dist(p.X, p.Z, t.X, t.Z)))
            : ("IP none, from your position", "", "");   // without IP lines 2 and 3 do not apply
        casLines[5] = $"{Desc(t)}, {(t.Moving ? "moving" : "stationary")}";
        (casLines[6], casLoc6) = Loc(me, t);
        casLines[7] = l.Kind == "faca" ? "WP" : Laser(me.Type) ? $"laser {Digits(cd)}" : "red smoke";
        casLines[8] = fri ? $"friendlies {Meters(fd)} {Dir8(Bearing(t.X, t.Z, fr.Item1, fr.Item2))}" : "friendlies none within five kilometers";
        casLines[9] = $"egress {Dir8(casEgr)}";
        var rem = new List<string> { $"final attack heading {Brg(casFah)}" };
        if (casDanger) rem.Add("danger close");
        if (l.Kind == "jtac" && Laser(me.Type)) rem.Add($"laser to target line {Brg(Bearing(l.X, l.Z, t.X, t.Z))}");
        if (Threats(l, t) is { Length: > 0 } th) rem.Add("threats: " + th);
        casLines[0] = string.Join(", ", rem);
        (cas, casTally) = (2, false);
        return new() { C(l, me, "standby for 9-line. " + string.Join(". ", Enumerable.Range(1, 9).Where(k => casLines[k] != "").Select(k => $"Line {k}, {casLines[k]}")) + $". Remarks, {casLines[0]}. Read back lines 4, 6 and restrictions") };
    }

    string Bda(Trg? cur)
    {
        var k = casKills; casTotal += k; casKills = 0;
        var left = cur?.Count ?? 0;
        var what = casNoun == "" ? "targets" : k == 1 ? Regex.Replace(casNoun, @"(?<=[A-Za-z])s$", "") : casNoun;   // "tanks" -> "tank" for one
        return k > 0 ? $"good hits, {Cnt(k)} {what} destroyed" + (left > 0 ? $", {Cnt(left)} remaining. Advise ready for re-attack" : ". Advise ready for the next 9-line")
                     : cur != null ? "no damage observed, target still active. Advise ready for re-attack" : "target no longer observed. Advise ready for the next 9-line";
    }

    /// Radio wheel texts that the app completes: readback with lines 4/6 and restrictions, IN / OFF with a direction. null = text unchanged.
    public string? CasWheel(string spoken) => spoken switch
    {
        "readback" when cas == 2 => $"readback, line 4 {casRb4}, line 6 {casLines[6]}, restrictions {casLines[0]}",
        "in" when cas >= 3 => $"in from the {Dir8((casFah + 180) % 360)}",
        "off" when cas >= 4 => $"off {Dir8(casEgr)}",
        _ => null,
    };

    /// J9: mission event X: kill / hit of the target group (BDA), of own troops (check fire), spot lost (laser off).
    /// The victim side is optional at index 8 (kill/hit lines of the mission); without it only the own leaders count as friendly.
    public void OnCasEvent(string[] e, string unit, double now)
    {
        if (cas == 0 || e.Length < 3 || Leaders.FirstOrDefault(x => x.Id == casL) is not { } l) return;
        var me = new Me(casCs, null, "", casSide);
        if (e[1] == "spotlost" && e[2] == casL && casLaser) { casLaser = false; casOut.Add(C(l, me, "lost the target, laser off")); return; }
        if (e[1] is not ("kill" or "hit") || e.Length < 6 || cas < 4) return;
        bool friendly = e.Length > 8 && e[8] == casSide.ToString() || Leaders.Any(x => x.Id == e[5] && x.Side == casSide);
        if (friendly && e[2] == unit)
        {
            (cas, casKills) = (3, 0);
            casOut.Add(C(l, me, "check fire, check fire, check fire, abort, abort, abort"));
        }
        else if (e[1] == "kill" && !friendly && e[5] == casG && e.Length > 7 && e[7] != "1") { casKills++; casKillAt = now; }
    }

    /// Every second: hold release, waiting target, BDA after kills, mark splash, leader lost (J10).
    List<Call> JtacTick(Me me, double now)
    {
        var res = new List<Call>();
        if (casOut.Count > 0) { res.AddRange(casOut); casOut.Clear(); }
        if (cas == 0) return res;
        casCs = me.Callsign;
        var l = Leaders.FirstOrDefault(x => x.Id == casL);
        if (l is not { Alive: true })
        {
            var nl = Leaders.Where(x => x.Alive && x.Side == casSide && x.Id != casL).OrderBy(x => x.Kind == "jtac" ? 0 : 1).FirstOrDefault();
            if (nl == null) { res.Add(new("Info", "JTAC", L($"JTAC {casLcs} ausgefallen, kein Leiter mehr.", $"{casLcs} is down, no controller left."))); CasEnd(); return res; }
            (casL, casLcs, casG, cas, casHold, casLaser) = (nl.Id, nl.Cs, "", 1, false, false);   // J10: the JTAC takes over from a lost FAC(A)
            res.Add(C(nl, me, "I have control"));
            res.AddRange(Situation(nl, me));
            return res;
        }
        var cur = Cur(l);
        if (cas == 1 && casHold && casAll.FirstOrDefault(o => o.casL == casL && o.casHold) == this && !casAll.Any(o => o != this && o.casL == casL && o.cas is >= 2 and <= 5)) res.AddRange(Situation(l, me));
        else if (cas == 1 && !casHold && casG == "" && now - casAt > 5 && Pick(l) != null) res.AddRange(Situation(l, me));
        if (cas is >= 2 and <= 5 && casKills == 0 && cur == null)
        {
            if (casGone < 0) casGone = now;
            else if (now - casGone > 10) { (casGone, cas, casG) = (-1, 1, ""); res.Add(C(l, me, "target no longer observed, stand by for an update")); }
        }
        else casGone = -1;
        if (cur != null) casNoun = Noun(cur);
        if (cas is 4 or 5 && casKills > 0 && now - casKillAt > 6) { cas = 6; res.Add(C(l, me, Bda(cur))); }
        if (casMarkAt > 0 && now >= casMarkAt) { casMarkAt = 0; res.Add(C(l, me, $"mark is {casMarkTxt}, advise contact")); }
        return res;
    }

    /// Self-test J6-J12: check-in, 9-line, readback, IN, BDA, check fire, hold, Type 1, handover, ground types.
    internal static void JtacTest(Action<bool, string> check)
    {
        var (ol, ot, on) = (Leaders, Targets, NavPts);
        casAll.Clear();
        var none = Array.Empty<Traffic>();
        var fl = new List<Airfield>();
        Me Mk(string cs, string type, double alt) => new(cs, new Telemetry(alt, alt, 200, 0, -15000, 0, 0, 0, 0), type, 2);
        string Say(Ops x, Me m, string t, double now) => string.Join(" | ", x.OnTranscript("JTAC", t, m, none, fl, now).Select(c => c.Text));
        Leaders = new() { new("JTAC-1", "jtac", "Axeman 1-1", 2, 0, 0, 100, 0, "AM", "", true) };
        var t4 = new Trg("JTAC-1", "T-1", "T-72B", 4, 3000, 0, 200, false, false, "37T GG 12345 67890", 42.1234, 42.5678, double.NaN, double.NaN);
        Targets = new() { t4 };
        NavPts = new() { (2, "IP Hammer", -8000, 0) };
        check(RoleOf("jtac checking in") == "JTAC" && RoleOf(Tower.Normalize("Axeman one one, ready to copy")) == "JTAC" && RoleOf("request picture") == "AWACS" && RoleOf("ready for departure") == null, "J5: Rolle JTAC im Routing (Name, Rufzeichen des Leiters)");
        var (m1, o) = (Mk("Enfield 1-1", "FA-18C_hornet", 3000), new Ops());
        var cc = o.OnTranscript("JTAC", "JTAC: checking in", m1, none, fl, 0);
        check(cc.Count == 1 && cc[0].Role == "JTAC" && cc[0].Freq == DefFreq && cc[0].Text.StartsWith("Enfield one one, Axeman") && cc[0].Text.Contains("Type 2 control") && cc[0].Text.Contains("Advise when ready for 9-line") && o.Cas == 1,
              "J6: Check-in -> Lage, Type 2, 'advise when ready for 9-line' -> " + string.Join(" | ", cc.Select(c => c.Text)));
        var nl = Say(o, m1, "JTAC: ready to copy", 1);
        check(nl.Contains("Line 4, elevation six six zero feet") && nl.Contains("north four two degrees zero seven decimal four zero minutes") && nl.Contains("laser one six eight eight")
              && nl.Contains("friendlies three zero zero zero meters") && nl.Contains("final attack heading") && nl.Contains("four tanks, stationary") && o.Cas == 2, "J7: 9-Liner mit Hoehe, Lat/Long (Hornet), Laser-Code, Friendlies -> " + nl);
        var wrong = Say(o, m1, "JTAC: readback line 4 one one zero zero line 6 " + o.casLines[6], 2);
        check(wrong.Contains("negative line 4, line 4 elevation six six zero") && !wrong.Contains("negative line 6") && o.Cas == 2, "J7: Readback mit falscher Zeile 4 -> nur Zeile 4 korrigiert -> " + wrong);
        var rb = Say(o, m1, "JTAC: " + o.CasWheel("readback"), 3);
        check(rb.StartsWith("Enfield one one, Axeman one one, readback correct") && o.Cas == 3, "J7: Readback richtig -> " + rb);
        var west = Say(o, m1, "JTAC: in from the west", 4);
        check(west.Contains("abort") && !west.Contains("cleared hot") && o.Cas == 3, "J6: IN aus West bei Anflugkurs Nord -> abort -> " + west);
        var wInS = o.CasWheel("in");
        var cont = Say(o, m1, "JTAC: " + wInS, 5);
        check(wInS == "in from the south" && cont.Contains("continue") && !cont.Contains("cleared hot") && o.Cas == 4, "J6: IN ohne Tally -> continue, Funkrad ergaenzt Himmelsrichtung -> " + wInS + " / " + cont);
        var ty = Say(o, m1, "JTAC: tally target", 6);
        var hot = Say(o, m1, "JTAC: in from the south", 7);
        check(ty.Contains("advise IN") && hot.EndsWith("cleared hot") && o.Cas == 5, "J6: Tally, IN -> cleared hot -> " + ty + " / " + hot);
        o.JtacTick(m1, 8);
        o.OnCasEvent(new[] { "X", "kill", "U1", "Enfield 1-1", "T-1-1", "T-1", "T-72B", "0" }, "U1", 9);
        Targets = new() { t4 with { Count = 3 } };
        var bda = string.Join(" | ", o.JtacTick(m1, 16).Select(c => c.Text));
        check(bda.Contains("good hits, one tank destroyed") && bda.Contains("three remaining") && o.Cas == 6, "J9: Kill der Zielgruppe -> BDA mit Zaehlung -> " + bda);
        var (m3, o3) = (Mk("Enfield 3-1", "FA-18C_hornet", 3000), new Ops());
        void Cleared(Ops x, Me m, double t0) { Say(x, m, "JTAC: checking in", t0); Say(x, m, "JTAC: ready to copy", t0 + 1); Say(x, m, "JTAC: " + x.CasWheel("readback"), t0 + 2); Say(x, m, "JTAC: tally target", t0 + 3); Say(x, m, "JTAC: " + x.CasWheel("in"), t0 + 4); }
        Cleared(o3, m3, 20);
        o3.OnCasEvent(new[] { "X", "kill", "U3", "g", "INF-1-1", "JTAC-1", "Soldier M4", "0" }, "U3", 30);
        var cf = string.Join(" | ", o3.JtacTick(m3, 31).Select(c => c.Text));
        check(o3.Cas == 3 && cf.Contains("check fire"), "J9: Wirkung auf eigene Truppen -> check fire, Abbruch -> " + cf);
        var (m5, o5) = (Mk("Enfield 5-1", "FA-18C_hornet", 3000), new Ops());
        var hold = Say(o5, m5, "JTAC: checking in", 32);
        check(hold.Contains("hold at") && hold.Contains("angels 10") && o5.Cas == 1, "J6: zweiter Spieler -> hold, angels -> " + hold);
        Say(o3, m3, "JTAC: checking out", 33);
        var rel = string.Join(" | ", o5.JtacTick(m5, 34).Select(c => c.Text));
        check(rel.Contains("situation: enemy") && o3.Cas == 0, "J6: erster fertig -> zweiter bekommt die Lage -> " + rel);
        o5.Leave();
        // FAC(A): Type 1, MGRS for the A-10C, mark and laser commands, handover when it is lost
        var fa = new Leader("FAC-1", "faca", "Rover 2-1", 2, -14000, 0, 3000, 251.5, "FM", "1514", true);
        Leaders = new() { fa };
        Targets = new() { new("FAC-1", "T-2", "BTR-80", 2, 0, 0, 100, true, false, "37T GG 10000 20000", 42.0, 42.0, -500, 0) };
        var (m6, o6) = (Mk("Enfield 6-1", "A-10C_2", 3000), new Ops());
        var f1 = o6.OnTranscript("JTAC", "Rover checking in", m6, none, fl, 40, false);
        check(f1.Count == 1 && f1[0].Role == "FACA" && f1[0].Freq == 251.5 && f1[0].Text.Contains("Type 1 control") && f1[0].Text.Contains("two APCs moving"), "J8: FAC(A) mit Sicht auf den Spieler -> Type 1, Rolle FACA, eigene Frequenz -> " + string.Join(" | ", f1.Select(c => c.Text)));
        var fn = Say(o6, m6, "JTAC: ready to copy", 41);
        check(fn.Contains("MGRS three seven tango, golf golf, one zero zero zero zero, two zero zero zero zero") && fn.Contains("Line 7, WP") && fn.Contains("danger close") && fn.Contains("friendlies five zero zero meters"), "J7: A-10C bekommt MGRS, FAC(A) markiert mit WP, danger close -> " + fn);
        var early = Say(o6, m6, "JTAC: request mark", 42);
        var rbw = o6.CasWheel("readback"); var rbr = Say(o6, m6, "JTAC: " + rbw, 43);
        var mk = Say(o6, m6, "JTAC: request mark", 44);
        var ls = Say(o6, m6, "JTAC: laser on", 45);
        check(early.Contains("readback first") && mk.Contains("rocket away") && ls.Contains("laser on, code one five one four") && o6.Cmds.SequenceEqual(new[] { "MARK;FAC-1;T-2;wp;0", "LASE;FAC-1;T-2;1514" }), "J4/J6: MARK/LASE nach Readback, Befehle an die Mission -> " + string.Join(" ; ", o6.Cmds) + " / " + rbw + " / " + rbr + " / " + o6.casLoc6 + " / " + o6.casEl4);
        o6.Cmds.Clear();
        o6.OnCasEvent(new[] { "X", "spotlost", "FAC-1" }, "U6", 46);
        var sl = string.Join(" | ", o6.JtacTick(m6, 47).Select(c => c.Text));
        check(sl.Contains("laser off"), "J9: spotlost -> laser off -> " + sl);
        Leaders = new() { fa with { Alive = false }, new("JTAC-1", "jtac", "Axeman 1-1", 2, 0, 0, 100, 0, "AM", "", true) };
        var hv = string.Join(" | ", o6.JtacTick(m6, 48).Select(c => c.Text));
        check(hv.Contains("Axeman one one, I have control") && o6.Cas == 1, "J10: FAC(A) ausgefallen -> JTAC uebernimmt -> " + hv);
        Leaders = new() { new("JTAC-1", "jtac", "Axeman 1-1", 2, 0, 0, 100, 0, "AM", "", false) };
        var dd = o6.JtacTick(m6, 49);
        check(dd.Count == 1 && dd[0].Role == "Info" && o6.Cas == 0 && !casAll.Contains(o6), "J10: Leiter tot, keiner mehr -> Funk endet -> " + dd.Count + " " + string.Join(" | ", dd.Select(c => c.Role + ":" + c.Text)) + " cas=" + o6.Cas);
        check(TypeSay("T-72B") == "tanks" && TypeSay("BTR-80") == "APCs" && TypeSay("ZSU-23-4 Shilka") == "Shilka" && TypeSay("Ural-375") == "trucks" && TypeSay("FA-18C_hornet") == "Hornet"
              && Desc(t4 with { Type = "BTR-80", Count = 1 }) == "one APC" && Desc(t4) == "four tanks", "J12: Bodentypen sprechbar (Mehrzahl/Einzahl)");
        (Leaders, Targets, NavPts) = (ol, ot, on);
        casAll.Clear();
    }
}
