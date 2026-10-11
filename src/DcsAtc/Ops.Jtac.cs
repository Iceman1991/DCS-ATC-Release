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
    string casL = "", casLcs = "", casCs = "", casG = "", casNoun = "", casEl4 = "", casLoc6 = "", casRb4 = "", casMarkTxt = "", casOrd = "";   // casOrd: ordnance from the check-in, "|"-separated
    bool casHold, casTally, casLaser, casDanger;
    double casFah, casEgr, casKillAt, casMarkAt, casGone = -1, casAt, casLast, casOffAt, casPlay;   // casPlay: end of the playtime from the check-in; casLast: last call (R418 idle end); casOffAt: "off" said, BDA follows (R417)
    readonly string[] casLines = new string[10];   // spoken 9-line lines 1-9, [0] remarks
    readonly List<Call> casOut = new();   // from mission events, delivered by Tick
    public readonly List<string> Cmds = new();   // MARK / LASE / LASEOFF for the mission (Program drains them to AiCmd)
    public int Cas => cas;
    public bool GroundJtac => Leaders.FirstOrDefault(x => x.Id == casL)?.Kind == "jtac";   // smoke colours / illumination only from a ground JTAC (wheel)
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
        return mine.FirstOrDefault(l => n != "" && CsKey(l) is { Length: > 2 } k && Has(n, k))
            ?? (me.Freq > 0 && mine.Where(l => Math.Abs((l.Freq > 0 ? l.Freq : DefFreq) - me.Freq) < 0.01).ToList() is { Count: > 0 } on   // SRS: only controllers on the called frequency
                ? on.FirstOrDefault(l => l.Id == casL) ?? (me.Tel is { } t0 ? on.OrderBy(l => Dist(t0.X, t0.Z, l.X, l.Z)).First() : on[0]) : null)
            ?? mine.FirstOrDefault(l => l.Id == casL)
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

    /// Save session: JTAC/FAC(A) contact with 9-line (arrays in field order; times are TickCount seconds, valid across an app restart).
    public record CasState(int[] I, string[] S, bool[] B, double[] D, string?[] Lines);
    CasState CasSave() => new(new[] { cas, casType, casHoldAlt, casFmt, casKills, casTotal, casSide }, new[] { casL, casLcs, casCs, casG, casNoun, casEl4, casLoc6, casRb4, casMarkTxt, casOrd },
                              new[] { casHold, casTally, casLaser, casDanger }, new[] { casFah, casEgr, casKillAt, casMarkAt, casGone, casAt, casLast, casOffAt, casPlay }, casLines.ToArray());
    void CasLoad(CasState s)
    {
        if (s is not { I: [var a0, var a1, var a2, var a3, var a4, var a5, var a6], S: [var s0, var s1, var s2, var s3, var s4, var s5, var s6, var s7, var s8, var s9],
                       B: [var b0, var b1, var b2, var b3], D: [var d0, var d1, var d2, var d3, var d4, var d5, var d6, var d7, var d8], Lines.Length: 10 }) return;   // other app version: start anew
        (cas, casType, casHoldAlt, casFmt, casKills, casTotal, casSide) = (a0, a1, a2, a3, a4, a5, a6);
        (casL, casLcs, casCs, casG, casNoun, casEl4, casLoc6, casRb4, casMarkTxt, casOrd) = (s0, s1, s2, s3, s4, s5, s6, s7, s8, s9);
        (casHold, casTally, casLaser, casDanger) = (b0, b1, b2, b3);
        (casFah, casEgr, casKillAt, casMarkAt, casGone, casAt, casLast, casOffAt, casPlay) = (d0, d1, d2, d3, d4, d5, d6, d7, d8);
        s.Lines.CopyTo(casLines, 0);
        casAll.Remove(this); casAll.Add(this);
    }

    void CasEnd()
    {
        if (casLaser) Cmds.Add($"LASEOFF;{casL}");
        casAll.Remove(this);
        (cas, casL, casLcs, casG, casHold, casTally, casLaser, casKills, casTotal, casMarkAt, casGone, casFmt, casOffAt, casPlay) = (0, "", "", "", false, false, false, 0, 0, 0, -1, 0, 0, 0);
    }

    List<Call> JtacCall(string n, Me me, double now, bool unsure = false)
    {
        (casCs, casLast) = (me.Callsign, now);
        var l = LeaderFor(me, n);
        if (l == null) return new() { new("Info", "JTAC", L("JTAC: keiner auf deiner Seite.", "JTAC: none on your side.")) };
        if (cas > 0 && l.Id != casL) CasEnd();
        List<Call> Say(string t) => new() { C(l, me, t) };
        var code = l.Code is { Length: > 0 } cd ? cd : "1688";
        if (unsure || Has(n, "radio check", "how do you read")) return Say(unsure ? "say again" : OneOf("read you five", "loud and clear"));
        var cur = Cur(l);
        if (Has(n, "checking out", "check out", "off station", "signing off", "bingo", "rtb"))
        {
            var tot = casTotal + casKills;
            CasEnd();
            return Say(OneOf("copy checking out", "roger checking out") + (tot > 0 ? $", {tot} destroyed in total" : "") + OneOf(". Good work", ". Thanks for the support"));
        }
        if (cas >= 2 && cur != null && !Has(n, "readback", "read back") && Regex.IsMatch(n, @"\b(?:mgrs|lat long|latitude|lat lon)\b"))   // E3: "say again in MGRS / lat long"
        {
            casFmt = Has(n, "mgrs") ? 1 : 2;
            (casLines[6], casLoc6) = Loc(me, cur);
            return Say($"line 6, {casLines[6]}");
        }
        if (cas >= 2 && casLines[1] != null && Regex.IsMatch(n, @"\b(?:say again|repeat)\b.*\b(?:9 ?liner?|all lines|all$)"))   // whole 9-line again (before "say again line N": "9 line" is not line 9)
            return Say("I say again 9-line. " + Nine() + (cas == 2 ? ". Read back lines 4, 6 and restrictions" : ""));
        if (cas >= 2 && Regex.Match(n, @"say again (?:the |your )?(?:line )?(\d)\b") is { Success: true } sl && int.Parse(sl.Groups[1].Value) is >= 1 and <= 9 and var ln && casLines[ln] != null)
            return Say(casLines[ln] == "" ? $"line {ln} not applicable" : $"line {ln}, {casLines[ln]}");
        if (Has(n, "say again") && lastRole.TryGetValue("JTAC", out var lc)) return lc;
        if (Has(n, "laser code")) return Say($"laser code {Digits(code)}");
        if (Has(n, "threat", "air defense") && !Has(n, "readback", "read back")) return Say(Threats(l, cur) is { Length: > 0 } th ? th : OneOf("no threats observed", "no threats at this time"));
        if (cas == 0) return CheckIn(l, me, n, now);
        if (PlayOf(n) is > 0 and var pt) { casPlay = now + pt * 60; return Say($"copy playtime {Digits(pt.ToString())} minutes"); }   // new playtime
        if (Has(n, "ready to copy", "ready for 9 line", "ready for nine line", "go ahead", "ready for tasking", "ready for next", "ready for re attack", "re attack", "reattack", "ready for the 9 line"))   // 9-line (also new target / re-attack)
        {
            if (casHold) return Say("stand by, hold as directed");
            if (cas is >= 3 and <= 5) return Say("9-line is out, say again which line");
            if (cas == 6 && cur != null && cur.Group == casG && Has(n, "re attack", "reattack")) { (cas, casTally) = (3, true); return Say($"re-attack approved, same 9-line, final attack heading {Brg(casFah)}, advise IN"); }   // same target: no new 9-line
            var nt = cur ?? Pick(l);
            if (nt == null) return Say(OneOf("no more targets observed, stand by", "no further targets observed, stand by"));
            if (Hold(l, me) is { } h) return h;   // R418: another flight attacks now (also when both were checked in at the same time)
            casG = nt.Group;
            return NineLine(l, me);
        }
        if (cas == 2 && (Has(n, "readback", "read back", "line 4", "line four") || Regex.IsMatch(n, @"\d")))
        {
            var digs = string.Concat(n.Where(char.IsDigit));
            if (digs.Length == 0) return Say("say again your readback");
            bool ok4 = digs.Contains(casEl4), ok6 = digs.Contains(casLoc6), okI = !casDanger || n.Contains(Initials());   // danger close: ground commander's initials come back
            if (ok4 && ok6 && okI) { cas = 3; return Say(OneOf("readback correct", "good readback") + (casType == 3 ? ", you are cleared to engage when ready" : ", push when ready, advise IN")); }
            return Say(string.Join(". ", new[] { !ok4 ? $"negative line 4, line 4 {casLines[4]}" : "", !ok6 ? $"negative line 6, line 6 {casLines[6]}" : "", !okI ? $"negative initials, ground commander's initials {Initials()}" : "" }.Where(s => s != "")) + ". Say again your readback");
        }
        if (Has(n, "laser off", "cease laser", "stop lasing", "terminate")) { if (casLaser) { casLaser = false; Cmds.Add($"LASEOFF;{casL}"); } return Say("laser off"); }
        // laser-guided bomb: "ten seconds" -> JTAC lases ("laser on"), "spot" = seeker/pod has the spot, "shift" = from the offset onto the target
        if (Has(n, "laser on", "request laser", "lase", "ten seconds", "10 seconds"))
        {
            if (cas < 3 || cur == null) return Say(cas < 3 ? "negative, readback first" : "no target to lase");
            casLaser = true; Cmds.Add($"LASE;{casL};{casG};{code}");
            return Say(Has(n, "ten seconds", "10 seconds") ? "laser on" : $"lasing, code {Digits(code)}");
        }
        if (Has(n, "spot", "captured", "shift")) return Say(!casLaser ? "not lasing, call laser on" : Has(n, "shift") ? "shifting" : OneOf("copy spot", "roger spot"));
        if (cas == 5 && Has(n, "cleared hot", "copy hot")) return new();   // pilot acknowledges the clearance: no answer
        // release call ("pickle" is the button, on the radio "bombs away", "rifle" = Maverick, "guns", "rockets")
        if (Has(n, "bombs away", "bomb away", "weapons away", "weapon away", "rifle", "guns", "rockets away", "missile away", "pickle", "pickled"))
            return Say(cas == 5 ? (casLaser ? OneOf("copy, lasing", "roger, lasing") : OneOf("copy", "roger")) : cas >= 3 ? "you were not cleared hot" : "say again");
        if (cas is >= 3 and <= 4 && Has(n, "ip inbound", "inbound", "push")) return Say(casTally || casType == 1 ? "continue, advise IN" : "continue, advise tally target");
        if (Has(n, "request mark", "mark my target", "mark target", "smoke", "mark on request", "mark please", "request smoke", "ir pointer", "ir mark", "infrared", "sparkle", "illum") && !Has(n, "contact", "tally", "visual"))   // "contact smoke / sparkle" is the tally
        {
            if (cas < 3 || cur == null) return Say(cas < 3 ? "negative, readback first" : "no target to mark");
            var (_, kind, delay) = CasKind[l.Kind];
            // requested mark: IR pointer at once (both), smoke colour / illumination by mortar only from the JTAC; FAC(A) otherwise WP
            if (l.Kind == "faca" && !Has(n, "smoke", "rocket", "wp", "willie", "sparkle", "ir", "infrared") && (Laser(me.Type) || Lgb(Weapon(cur))))   // FAC(A) marks a laser-capable aircraft with its laser
            {
                casLaser = true; Cmds.Add($"LASE;{casL};{casG};{code}");
                return Say($"laser mark, code {Digits(code)}, advise spot");
            }
            if (Has(n, "ir pointer", "ir mark", "infrared", "sparkle")) (kind, delay) = ("ir", 0);
            else if (l.Kind == "jtac" && Has(n, "illum")) kind = "illum";
            else if (l.Kind == "jtac" && Regex.Match(n, @"\b(green|red|white|orange|blue) smoke\b") is { Success: true } sc) kind = "smoke_" + sc.Groups[1].Value;
            Cmds.Add($"MARK;{casL};{casG};{kind};{delay}");
            (casMarkAt, casMarkTxt) = (delay > 0 ? now + delay : 0, kind switch { "wp" => "white phosphorus", "ir" => "sparkle", "illum" => "illumination", _ => kind.Replace("smoke_", "") + " smoke" });
            return Say(kind == "ir" ? "sparkle, advise contact sparkle" : delay > 0 ? $"mark on the way, {Digits(delay.ToString())} seconds" : $"rocket away, mark is {casMarkTxt}, advise contact");
        }
        bool noTally = Regex.IsMatch(n, @"\b(?:negative|no|not|unable)\b.*\b(?:tally|visual|contact|target|mark)\b|\b(?:no joy|blind|talk on|describe the target|target description)\b");   // R419: "negative contact", "no tally", "no joy"; talk-on on request
        if (cas >= 3 && cas <= 5 && Regex.IsMatch(n, @"\bin\b") && !Has(n, "checking in", "check in") && !Regex.IsMatch(n, @"\bin (sight|trail)\b"))   // attack: IN from the west
        {
            if (noTally) casTally = false;
            else if (Has(n, "tally", "visual", "contact")) casTally = true;
            var fromDir = CardOf(n);
            double diff = fromDir is { } fd ? HdgDiff((fd + 180) % 360, casFah) : 0;   // attack heading = reverse of the side he comes from
            var fah = Brg(casFah);
            if (diff >= 90) { cas = 3; return Say($"abort, abort, abort, final attack heading {fah}"); }
            if (Has(n, "dry")) { cas = 4; return Say("continue dry"); }   // practice run without weapons
            if (!casTally && casType != 1) { cas = 4; return Say("continue, advise tally target or mark"); }
            if (diff > 40) { cas = 4; return Say($"continue, fly final attack heading {fah}"); }
            cas = 5;
            return Say((casDanger ? "danger close, " : "") + "cleared hot");
        }
        if (noTally)   // R419: talk-on instead of a clearance
        {
            if (cas < 3) return Say("stand by");
            casTally = false;
            if (cur == null) return Say(OneOf("copy, target no longer observed, stand by", "roger, target no longer observed, stand by"));
            var where = l.Kind == "jtac" ? $"{Meters(Dist(l.X, l.Z, cur.X, cur.Z))} {Dir8(Bearing(l.X, l.Z, cur.X, cur.Z))} of my position" : $"line 6, {casLines[6]}";
            return Say($"{OneOf("copy no joy", "roger no joy")}. Target is {Desc(cur)}, {(cur.Moving ? "moving" : "stationary")}, {where}. Request mark if needed, advise tally");
        }
        if (Has(n, "tally", "contact the mark", "contact mark", "have the mark", "visual", "have the target", "contact"))
        {
            if (cas < 3) return Say("stand by");
            casTally = true;
            return Say(OneOf("roger, advise IN", "copy, advise IN"));
        }
        if (cas >= 4 && Has(n, "off", "winchester", "out of ammo", "complete"))
        {
            if (cas == 6) return Say(OneOf("copy off", "roger off"));
            (cas, casOffAt) = (6, now);   // R417: BDA after the impacts (JtacTick), not before the weapons arrive
            return Say(OneOf("copy off, stand by for BDA", "roger off, stand by for BDA"));
        }
        if (Has(n, "bda", "battle damage", "status", "did i hit", "any hits", "hit anything", "effects", "results")) return Say(cas >= 4 ? Bda(cur, true) : "no attack yet");
        if (Has(n, "abort")) { if (cas >= 3) cas = 3; return Say(OneOf("copy abort, standing by", "roger abort, standing by")); }
        return Say("say again");
    }

    List<Call> CheckIn(Leader l, Me me, string n, double now)
    {
        casAll.Remove(this);
        (cas, casL, casLcs, casSide, casAt, casG, casTally, casKills, casTotal, casLaser) = (1, l.Id, l.Cs, me.Coalition, now, "", false, 0, 0, false);
        var pt = PlayOf(n);   // check-in: "4 GBU-12, 2 Mavericks, playtime 30" -> the JTAC confirms, picks the weapon in the 9-line, watches the playtime
        (casOrd, casPlay) = (OrdOf(n), pt > 0 ? now + pt * 60 : 0);
        var ack = string.Join(", ", casOrd.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(WSay).Append(pt > 0 ? $"playtime {Digits(pt.ToString())} minutes" : "").Where(s => s != ""));
        casAll.Add(this);
        var pre = ack == "" ? "" : $"copy {ack}. ";
        return Hold(l, me, pre) ?? Situation(l, me, pre);
    }

    /// E5: one attack at a time, stack with 1000 ft steps. null = nobody else attacks.
    List<Call>? Hold(Leader l, Me me, string pre = "")
    {
        if (!casAll.Any(o => o != this && o.casL == l.Id && o.cas is >= 2 and <= 5)) return null;
        var alt = me.Tel is { } t ? (int)Math.Round(t.AltMsl / Ft / 1000) : 10;
        while (casAll.Any(o => o != this && o.casL == l.Id && o.casHold && o.casHoldAlt == alt)) alt++;
        (cas, casHold, casHoldAlt, casG) = (1, true, alt, "");
        var tg = Pick(l);
        var ip = tg == null ? null : IpFor(me, tg);
        return new() { C(l, me, pre + $"hold at {(ip is { } i ? SpokenCallsign(i.Name) : "present position")}, angels {alt}, I'll call you when I'm ready for you") };
    }

    /// J6: check-in answer: situation, type of control. Without a target: wait (Tick calls when one appears).
    List<Call> Situation(Leader l, Me me, string pre = "")
    {
        casHold = false;
        var t = Pick(l);
        if (t == null) { casG = ""; return new() { C(l, me, pre + "no enemy observed at this time, stand by. I'll call when I have a target") }; }
        (casG, casType) = (t.Group, TypeOf(l, me));
        var fr = l.Kind == "faca" ? (double.IsNaN(t.Fx) ? "friendlies none known" : $"friendlies {Meters(Dist(t.Fx, t.Fz, t.X, t.Z))} {Dir8(Bearing(t.X, t.Z, t.Fx, t.Fz))} of the target") : "friendlies at my position";
        var where = l.Kind == "jtac" ? $", {Meters(Dist(l.X, l.Z, t.X, t.Z))} {Dir8(Bearing(l.X, l.Z, t.X, t.Z))} of my position" : "";
        var ctl = casType switch { 1 => "Type 1 control", 3 => "Type 3 control, cleared to engage in the target area", _ => "Type 2 control, bomb on target" };
        return new() { C(l, me, pre + $"situation: enemy {Desc(t)} {(t.Moving ? "moving" : "stationary")}{where}{(t.Flak ? ", air defense present" : "")}, {fr}. {ctl}. {OneOf("Advise when ready for 9-line", "Advise ready for 9-line")}") };
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

    /// Check-in ordnance ("4 GBU-12, 2 Mavericks, guns" -> "gbu 12|maverick|gun") and playtime in minutes (0 = none).
    static string OrdOf(string n) => string.Join("|", Regex.Matches(n, @"\b(?:gbu \d+|mk \d+|cbu \d+|agm \d+|maverick|hellfire|apkws|rocket|jdam|gun)").Select(m => m.Value).Distinct());
    static int PlayOf(string n) => Regex.Match(n, @"\bplay ?time (\d(?: ?\d){0,2})\b") is { Success: true } m ? int.Parse(m.Groups[1].Value.Replace(" ", "")) : 0;
    static string WSay(string w) => w switch { "gun" => "guns", "rocket" => "rockets", "maverick" => "Maverick", "hellfire" => "Hellfire", "apkws" => "APKWS", "jdam" => "JDAM", _ => Regex.Replace(w.ToUpperInvariant(), "^MK", "Mark") };
    static bool Lgb(string? w) => w != null && Regex.IsMatch(w, @"^gbu (?:10|12|16|24|49|54)$");
    /// Weapon the JTAC asks for: bombs (laser first) on a stationary target, missile / rockets / guns against a mover.
    string? Weapon(Trg t) => casOrd.Split('|', StringSplitOptions.RemoveEmptyEntries).OrderBy(w =>
    {
        int k = Lgb(w) ? 0 : Regex.IsMatch(w, "^(?:gbu|jdam)") ? 1 : Regex.IsMatch(w, "^(?:mk|cbu)") ? 2 : Regex.IsMatch(w, "^(?:agm|maverick|hellfire)") ? 3 : Regex.IsMatch(w, "^(?:apkws|rocket)") ? 4 : 5;
        return t.Moving ? (k + 3) % 6 : k;
    }).FirstOrDefault();
    /// Danger close: initials of the ground commander who accepts the risk (fixed per target group, survives a restart).
    string Initials() { var s = casG.Sum(c => c); return $"{Nato[s % 26]} {Nato[s / 26 % 26]}"; }

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
        var wpn = Weapon(t);
        casLines[7] = Laser(me.Type) || Lgb(wpn) ? $"laser {Digits(cd)}" : l.Kind == "faca" ? "WP" : "red smoke";   // FAC(A) lases for laser-capable aircraft, otherwise WP rocket
        casLines[8] = fri ? $"friendlies {Meters(fd)} {Dir8(Bearing(t.X, t.Z, fr.Item1, fr.Item2))}" : "friendlies none within five kilometers";
        casLines[9] = $"egress {Dir8(casEgr)}";
        var rem = new List<string> { $"final attack heading {Brg(casFah)}" };
        if (wpn != null) rem.Add($"ordnance {WSay(wpn)}");
        if (casDanger) rem.Add($"danger close, ground commander's initials {Initials()}");
        if (l.Kind == "jtac" && (Laser(me.Type) || Lgb(wpn))) rem.Add($"laser to target line {Brg(Bearing(l.X, l.Z, t.X, t.Z))}");
        if (Threats(l, t) is { Length: > 0 } th) rem.Add("threats: " + th);
        casLines[0] = string.Join(", ", rem);
        (cas, casTally) = (2, false);
        return new() { C(l, me, OneOf("standby for 9-line. ", "stand by for 9-line. ") + Nine() + ". Read back lines 4, 6 and restrictions") };
    }
    string Nine() => string.Join(". ", Enumerable.Range(1, 9).Where(k => casLines[k] != "").Select(k => $"Line {k}, {casLines[k]}")) + $". Remarks, {casLines[0]}";

    string Bda(Trg? cur, bool asked = false)   // asked: pilot asks again after the report, kills are already counted
    {
        var k = casKills; casTotal += k; casKills = 0;
        var left = cur?.Count ?? 0;
        var what = casNoun == "" ? "targets" : (k > 0 ? k : casTotal) == 1 ? Regex.Replace(casNoun, @"(?<=[A-Za-z])s$", "") : casNoun;   // "tanks" -> "tank" for one
        return k > 0 ? $"{OneOf("good hits", "good effects")}, {Cnt(k)} {what} destroyed" + (left > 0 ? $", {Cnt(left)} remaining. {OneOf("Advise ready for re-attack", "Advise when ready for re-attack")}" : ". Advise ready for the next 9-line")
                     : asked && casTotal > 0 ? $"no new damage, {Cnt(casTotal)} {what} destroyed so far" + (left > 0 ? $", {Cnt(left)} remaining" : "")
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

    /// Radio wheel Enter during the CAS contact: the next step (Program.Request completes readback/IN/OFF via CasWheel). null = no contact, holding or no step.
    public (string Role, string Text)? CasSuggest() => (cas switch
    {
        1 when !casHold => "ready to copy",
        2 => "readback",
        3 or 4 => casTally || casType == 1 ? "in" : "tally target",
        5 => "off",
        6 => "ready to copy",
        _ => null,
    }) is { } s ? ("JTAC", s) : null;

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
        if (cas == 6 && casOffAt > 0 && now - casOffAt > 15 && (casKills == 0 || now - casKillAt > 6)) { casOffAt = 0; res.Add(C(l, me, Bda(cur))); }   // R417
        if (cas >= 2 && now - Math.Max(casLast, casAt) > 600)   // R418: no call for 10 minutes -> the controller works the next flight
        {
            res.Add(new("Info", "JTAC", L($"JTAC {casLcs}: Kontakt beendet (10 Minuten ohne Funk).", $"{casLcs}: contact ended (10 minutes without a call).")));
            CasEnd();
        }
        if (casMarkAt > 0 && now >= casMarkAt) { casMarkAt = 0; res.Add(C(l, me, $"mark is {casMarkTxt}, advise contact")); }
        if (casPlay > 0 && now >= casPlay) { casPlay = 0; res.Add(C(l, me, "your playtime is up, say intentions")); }   // playtime from the check-in
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
        var rp = Say(o, m1, "JTAC: say again the nine-line", 1.5);
        check(rp.Contains("I say again 9-line. Line ") && rp.Contains("Line 4, elevation six six zero feet") && rp.Contains("Read back") && o.Cas == 2 && Say(o, m1, "JTAC: say again line 4", 1.6).Contains("line 4, elevation"), "9-Liner komplett wiederholen (nicht Zeile 9) -> " + rp);
        var wrong = Say(o, m1, "JTAC: readback line 4 one one zero zero line 6 " + o.casLines[6], 2);
        check(wrong.Contains("negative line 4, line 4 elevation six six zero") && !wrong.Contains("negative line 6") && o.Cas == 2, "J7: Readback mit falscher Zeile 4 -> nur Zeile 4 korrigiert -> " + wrong);
        var oR = new Ops(); oR.Load(System.Text.Json.JsonSerializer.Deserialize<State>(System.Text.Json.JsonSerializer.Serialize(o.Save()))!);   // app restart mid 9-line (session file)
        var rbR = Say(oR, m1, "JTAC: " + oR.CasWheel("readback") + ", threats: air defense at the target", 3); oR.CasEnd();   // restrictions with threats (Shilka) are no threat question
        check(rbR.StartsWith("Enfield one one, Axeman one one, readback correct"), "Sitzung: JTAC-Kontakt nach Neustart, Readback geht weiter -> " + rbR);
        var rb = Say(o, m1, "JTAC: " + o.CasWheel("readback"), 3);
        check(rb.StartsWith("Enfield one one, Axeman one one, readback correct") && o.Cas == 3, "J7: Readback richtig -> " + rb);
        var west = Say(o, m1, "JTAC: in from the west", 4);
        check(west.Contains("abort") && !west.Contains("cleared hot") && o.Cas == 3, "J6: IN aus West bei Anflugkurs Nord -> abort -> " + west);
        var wInS = o.CasWheel("in");
        var cont = Say(o, m1, "JTAC: " + wInS, 5);
        check(wInS == "in from the south" && cont.Contains("continue") && !cont.Contains("cleared hot") && o.Cas == 4, "J6: IN ohne Tally -> continue, Funkrad ergaenzt Himmelsrichtung -> " + wInS + " / " + cont);
        var nj = Say(o, m1, "JTAC: negative contact", 5.5);
        var nj2 = Say(o, m1, "JTAC: in from the south, no joy", 5.6);
        check(nj.Contains("copy no joy. Target is four tanks") && !nj.Contains("advise IN") && nj2.Contains("continue") && !nj2.Contains("cleared hot"), "R419: Verneinung ist kein Tally -> Talk-on -> " + nj + " / " + nj2);
        var gs = Say(o, m1, "JTAC: request green smoke", 5.7); var cs = Say(o, m1, "JTAC: contact green smoke", 5.8);
        check(gs.Contains("mark on the way") && o.Cmds.Count(c => c.EndsWith(";smoke_green;30")) == 1 && cs.Contains("advise IN"), "Markierung auf Wunsch: JTAC gruener Rauch, contact smoke ist Tally -> " + gs + " / " + cs);
        var tk = Say(o, m1, "JTAC: request talk on", 5.9);
        check(tk.Contains("copy no joy. Target is four tanks"), "Talk-on auf Wunsch -> " + tk);
        var ty = Say(o, m1, "JTAC: tally target", 6);
        var hot = Say(o, m1, "JTAC: in from the south", 7);
        check(ty.Contains("advise IN") && hot.EndsWith("cleared hot") && o.Cas == 5, "J6: Tally, IN -> cleared hot -> " + ty + " / " + hot);
        var ba = Say(o, m1, "JTAC: bombs away", 7.5);
        check(ba.EndsWith("Axeman one one, copy") || ba.EndsWith(", copy"), "Abwurfmeldung bombs away nach cleared hot -> " + ba);
        o.JtacTick(m1, 8);
        o.OnCasEvent(new[] { "X", "kill", "U1", "Enfield 1-1", "T-1-1", "T-1", "T-72B", "0" }, "U1", 9);
        Targets = new() { t4 with { Count = 3 } };
        var bda = string.Join(" | ", o.JtacTick(m1, 16).Select(c => c.Text));
        check(bda.Contains("good hits, one tank destroyed") && bda.Contains("three remaining") && o.Cas == 6, "J9: Kill der Zielgruppe -> BDA mit Zaehlung -> " + bda);
        var dh = Say(o, m1, "JTAC: did I hit", 17);
        check(dh.Contains("no new damage, one tank destroyed so far, three remaining"), "BDA auf Nachfrage (did I hit) -> " + dh);
        var ra = Say(o, m1, "JTAC: request re-attack", 18); var ib = Say(o, m1, "JTAC: IP inbound", 19); var ba2 = Say(o, m1, "JTAC: bombs away", 20); var bg = Say(o, m1, "JTAC: bingo", 21);
        check(ra.Contains("re-attack approved, same 9-line") && ib.EndsWith("continue, advise IN") && ba2.EndsWith("you were not cleared hot") && bg.Contains("copy checking out") && o.Cas == 0, "Re-attack ohne neuen 9-Liner, IP inbound, Abwurf ohne Freigabe, bingo -> " + ra + " / " + ib + " / " + ba2 + " / " + bg);
        // check-in with ordnance and playtime, weapon choice, danger close initials, push, in dry, LGB calls (ten seconds / spot / shift)
        var t3 = Targets[0]; Targets = new() { t3 with { X = 600 } };
        var (m2, o2) = (Mk("Enfield 2-1", "Su-25T", 3000), new Ops());
        var ci = Say(o2, m2, "JTAC: Axeman, Enfield 2-1, checking in, 2 Mavericks, 4 GBU-12, guns, playtime 3 0 minutes", 21.5);
        var n2 = Say(o2, m2, "JTAC: ready to copy", 22);
        var rbI = Say(o2, m2, "JTAC: readback line 4 " + o2.casEl4 + " line 6 " + o2.casLoc6, 23);
        var rbO = Say(o2, m2, "JTAC: " + o2.CasWheel("readback"), 24);
        check(ci.Contains("copy Maverick, GBU 12, guns, playtime three zero minutes. situation") && n2.Contains("ordnance GBU 12") && n2.Contains("Line 7, laser one six eight eight") && n2.Contains("danger close, ground commander's initials whiskey golf")
              && rbI.Contains("negative initials, ground commander's initials whiskey golf") && !rbI.Contains("line 4") && rbO.Contains("readback correct, push when ready"), "Check-in mit Bewaffnung/Playtime, Waffenwahl, danger close mit Kuerzel im Readback -> " + ci + " / " + n2 + " / " + rbI + " / " + rbO);
        var pu = Say(o2, m2, "JTAC: pushing", 25); Say(o2, m2, "JTAC: tally target", 26); var dr = Say(o2, m2, "JTAC: in from the south, dry", 27);
        var hot2 = Say(o2, m2, "JTAC: " + o2.CasWheel("in"), 28); var ack2 = o2.OnTranscript("JTAC", "JTAC: cleared hot", m2, none, fl, 29);
        var ten = Say(o2, m2, "JTAC: ten seconds", 30); var spt = Say(o2, m2, "JTAC: spot", 31); var sh = Say(o2, m2, "JTAC: shift", 32);
        var pt2 = Say(o2, m2, "JTAC: playtime 5 minutes", 33); var up = string.Join(" | ", o2.JtacTick(m2, 334).Select(c => c.Text));
        check(pu.EndsWith("continue, advise tally target") && dr.EndsWith("continue dry") && hot2.EndsWith("danger close, cleared hot") && ack2.Count == 0 && ten.EndsWith("laser on") && o2.Cmds.Contains("LASE;JTAC-1;T-1;1688")
              && spt.Contains(" spot") && sh.EndsWith("shifting") && pt2.EndsWith("copy playtime five minutes") && up.Contains("your playtime is up"), "push, in dry, Quittung cleared hot, ten seconds/spot/shift, Playtime-Ende -> " + pu + " / " + dr + " / " + hot2 + " / " + ten + " / " + spt + " / " + sh + " / " + pt2 + " / " + up);
        Say(o2, m2, "JTAC: checking out", 335); Targets = new() { t3 };
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
        // R417: "off" before the impact -> BDA only after the weapons arrived; R418: idle end, hold when both were checked in
        var (m7, o7) = (Mk("Enfield 7-1", "FA-18C_hornet", 3000), new Ops());
        Cleared(o7, m7, 50);
        var off = Say(o7, m7, "JTAC: off south", 55);
        o7.OnCasEvent(new[] { "X", "kill", "U7", "g", "T-1-2", "T-1", "T-72B", "0", "1" }, "U7", 60);
        var early7 = o7.JtacTick(m7, 62).Count;
        var bda7 = string.Join(" | ", o7.JtacTick(m7, 71).Select(c => c.Text));
        check(off.EndsWith("stand by for BDA") && early7 == 0 && bda7.Contains("good hits, one tank destroyed"), "R417: off vor Einschlag -> BDA erst nach dem Kill -> " + off + " / " + early7 + " / " + bda7);
        var idle = o7.JtacTick(m7, 700);
        check(o7.Cas == 0 && idle.Count == 1 && idle[0].Role == "Info", "R418: 10 Minuten ohne Funk -> Kontakt endet");
        var (m8, o8, m9, o9) = (Mk("Enfield 8-1", "FA-18C_hornet", 3000), new Ops(), Mk("Enfield 9-1", "FA-18C_hornet", 4000), new Ops());
        Say(o8, m8, "JTAC: checking in", 710); Say(o9, m9, "JTAC: checking in", 711);
        Say(o8, m8, "JTAC: ready to copy", 712);
        var h9 = Say(o9, m9, "JTAC: ready to copy", 713);
        check(o8.Cas == 2 && h9.Contains("hold at") && o9.Cas == 1, "R418: beide eingecheckt -> zweiter haelt statt zweitem 9-Liner -> " + h9);
        o8.Leave(); o9.Leave();
        // FAC(A): Type 1, MGRS for the A-10C, mark and laser commands, handover when it is lost
        var fa = new Leader("FAC-1", "faca", "Rover 2-1", 2, -14000, 0, 3000, 251.5, "FM", "1514", true);
        Leaders = new() { fa };
        Targets = new() { new("FAC-1", "T-2", "BTR-80", 2, 0, 0, 100, true, false, "37T GG 10000 20000", 42.0, 42.0, -500, 0) };
        var (m6, o6) = (Mk("Enfield 6-1", "A-10C_2", 3000), new Ops());
        Leaders = new() { new("JTAC-9", "jtac", "Axeman 9-1", 2, -14000, 0, 10, 0, "AM", "", true), fa };
        check(o6.LeaderFor(m6 with { Freq = 251.5 })?.Id == "FAC-1" && o6.LeaderFor(m6 with { Freq = DefFreq })?.Id == "JTAC-9", "JTAC und FAC(A) auf eigenen Frequenzen: SRS-Frequenz waehlt den Lotsen");
        Leaders = new() { fa };
        var f1 = o6.OnTranscript("JTAC", "Rover checking in", m6, none, fl, 40, false);
        check(f1.Count == 1 && f1[0].Role == "FACA" && f1[0].Freq == 251.5 && f1[0].Text.Contains("Type 1 control") && f1[0].Text.Contains("two APCs moving"), "J8: FAC(A) mit Sicht auf den Spieler -> Type 1, Rolle FACA, eigene Frequenz -> " + string.Join(" | ", f1.Select(c => c.Text)));
        var fn = Say(o6, m6, "JTAC: ready to copy", 41);
        check(fn.Contains("MGRS three seven tango, golf golf, one zero zero zero zero, two zero zero zero zero") && fn.Contains("Line 7, laser one five one four") && fn.Contains("danger close") && fn.Contains("friendlies five zero zero meters"), "J7: A-10C bekommt MGRS, FAC(A) markiert per Laser, danger close -> " + fn);
        var early = Say(o6, m6, "JTAC: request mark", 42);
        var rbw = o6.CasWheel("readback"); var rbr = Say(o6, m6, "JTAC: " + rbw, 43);
        var mk = Say(o6, m6, "JTAC: request smoke", 44);
        var ls = Say(o6, m6, "JTAC: laser on", 45);
        check(early.Contains("readback first") && mk.Contains("rocket away") && ls.Contains("lasing, code one five one four") && o6.Cmds.SequenceEqual(new[] { "MARK;FAC-1;T-2;wp;0", "LASE;FAC-1;T-2;1514" }), "J4/J6: MARK/LASE nach Readback, Befehle an die Mission -> " + string.Join(" ; ", o6.Cmds) + " / " + rbw + " / " + rbr + " / " + o6.casLoc6 + " / " + o6.casEl4);
        var sp = Say(o6, m6, "JTAC: request sparkle", 45.5); var gw = Say(o6, m6, "JTAC: request green smoke", 45.6); var csp = Say(o6, m6, "JTAC: contact sparkle", 45.7);
        check(sp.EndsWith("sparkle, advise contact sparkle") && o6.Cmds[^2] == "MARK;FAC-1;T-2;ir;0" && o6.Cmds[^1] == "MARK;FAC-1;T-2;wp;0" && gw.Contains("white phosphorus") && csp.Contains("advise IN") && o6.Cmds.Count == 4, "Markierung auf Wunsch: sparkle = IR, FAC(A) ohne Farbrauch -> WP, contact sparkle = Tally -> " + sp + " / " + gw + " / " + csp);
        var lm = Say(o6, m6, "JTAC: request mark", 45.8);
        check(lm.EndsWith("laser mark, code one five one four, advise spot") && o6.Cmds[^1] == "LASE;FAC-1;T-2;1514", "FAC(A) markiert fuer laserfaehige Flugzeuge per Laser -> " + lm);
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
