using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= ATIS
    static readonly string[] Alphabet =
    {
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliett", "Kilo", "Lima", "Mike",
        "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu",
    };
    public static string Phonetic(string letter) => Alphabet[(char.ToUpperInvariant(letter[0]) - 'A') % 26];

    /// Pilot states the current ATIS identifier ("information Charlie", "with Charlie", "have Charlie"); never without ATIS at the airfield.
    bool AtisHeard(string n) => AtisLetter != "" && Has(n, "(?:information|atis|with|have) " + Normalize(Phonetic(AtisLetter))[..4]);

    /// " Information Charlie is current." – if the pilot did not state the current identifier.
    string AtisNote(string n) => AtisLetter == "" || AtisHeard(n) ? "" : $" Information {Phonetic(AtisLetter)} is current.";

    /// ATIS text of the airfield. w: wind/pressure at the airfield (AltMsl = measurement altitude), clouds from the mission.
    public string AtisText(string letter, Telemetry w, double tempC, bool clouds, double baseM, double visM, double clock = -1)   // clock: mission clock (local time, s since midnight) -> "time HHMM Zulu", -1 = no time
    {
        var word = Phonetic(letter);
        var zulu = clock < 0 || Airfield.UtcOffset is not { } off ? "" : $", time {Digits(TimeSpan.FromSeconds((clock - off * 3600 + 86400) % 86400).ToString("hhmm"))} Zulu";   // R125: Zulu per map, unknown map without time
        var rw = WindRunway(w);
        // R208: visibility obstruction per ICAO Annex 3 (FG under 1000 m, BR up to 5000 m)
        var vis = visM >= 9500 ? "visibility one zero kilometers or more" : visM < 1000 ? $"visibility {Math.Round(visM / 50) * 50} meters, fog"
                : $"visibility {Digits(Math.Max(1, Math.Round(visM / 1000)).ToString())} kilometers{(visM < 5000 ? ", mist" : "")}";
        var sky = clouds ? $"clouds {Math.Round(Math.Max(0, baseM - F.Elev) / Ft / 100) * 100:0} feet" : "sky clear";   // A34: ceiling above airfield (AGL), not above MSL
        var temp = (tempC < -0.5 ? "minus " : "") + Digits(Math.Abs(Math.Round(tempC)).ToString());
        return $"{F.Name.Replace('-', ' ')} information {word}{zulu}. Runway {RwSay(rw)} in use, {Hand(rw)} pattern, {Alt(PatternFt)}. " +
               $"{Cap(Wind(w))}. {Cap(vis)}, {sky}. Temperature {temp}. {Cap(AtisQnh(w))}. " +
               (F.FreqsOf("Tower") is { } tf ? (tf.Any(x => x < 200) && tf.Any(x => x >= 200) ? $"Tower {FreqSay(tf.First(x => x < 200))}, UHF {FreqSay(tf.First(x => x >= 200))}. " : $"Tower {FreqSay(tf[0])}. ")   // K5: map frequency (VHF) first; own (AirfieldFrequencies) likewise
                          : $"Ground {FreqSay(Freqs["Ground"])}, tower {FreqSay(Freqs["Tower"])}, approach {FreqSay(Freqs["Approach"])}. ") +
               (!Ifr ? "" : FinalOk(rw) ? $"Instrument conditions, expect vectors for {ProcSay(rw)}. "
                    : FinalOk(Airfield.Opposite(rw)) && TailKt(w, Airfield.Opposite(rw)) <= 10 ? $"Instrument conditions, due to terrain expect vectors for {ProcSay(Airfield.Opposite(rw))}. "
                    : $"Instrument conditions, due to terrain expect vectors for circling approach runway {RwSay(rw)}. ") +
               $"Advise on initial contact you have information {word}.";
    }

    /// Announcement to everyone at the airfield when the ATIS identifier changes.
    public string AtisCall(string letter, Telemetry w) => $"Attention all aircraft, {F.Name.Replace('-', ' ')} information {Phonetic(letter)} now current, {AtisQnh(w)}.";

    string TakeoffClearance(Telemetry? t, IReadOnlyList<Traffic> traffic, Traffic? fin = null)   // fin: approach 2.5-6 NM -> "immediate" with traffic (A10)
    {
        Phase = Phase.ClearedTakeoff; handedOff = zoneReport = false; wxTold = false;   // A145: new flight, next approach states the weather again
        lineUp = rejecting = false; popM = 0;
        var c = (Cs() + ", " + Wake(t, traffic, Runway)).TrimEnd(',', ' ');   // R205: wake turbulence ahead
        // A95: departure part before the clearance ("after departure exit via …, runway …, wind …, cleared for takeoff")
        var clr = $"runway {RwSay(Runway)}, {Wind(t)}, cleared for {(fin != null ? "immediate " : "")}takeoff{(fin != null ? $", traffic {Describe(fin, Runway)}" : "")}";
        goFt = 0;
        if (stayPattern && Ifr)   // R253: practice approaches in IMC as radar pattern (FAA JO 7110.65 4-8-11): runway heading and altitude, after liftoff "contact Approach" (tick), vectors after check-in
        {
            nav = null; wantStraight = true; goFt = ClimbFt(Runway);
            return $"{c}, after departure fly runway heading, climb and maintain {Alt(goFt)}, {clr}.";
        }
        if (stayPattern)
        {
            nav = null;
            // R200: traffic pattern with the takeoff clearance (FAA JO 7110.65 3-10-11), reporting point base
            return Closed() is var cl && cl != "" ? $"{c}, {clr}{cl}." : $"{c}, after departure join {Hand(Runway)} downwind, {Alt(PatternFt)}, {clr}. Report final.";
        }
        clr += ".";
        if (depClr)
        {
            nav = null;
            return $"{c}, after departure fly runway heading, climb and maintain {Alt(depFt)}, {clr}";
        }
        if (!F.Charted && exitDir != null)   // A92: without map departure direction instead of CRP
        {
            nav = null;
            return $"{c}, after departure leave the control zone {exitDir}bound, not above {Alt(MaxFt)}, {clr}";
        }
        if (!F.Charted || exitDir == null)
        {
            nav = null;
            return $"{c}, after departure own navigation, not above {Alt(MaxFt)} until leaving the control zone, {clr}";
        }
        nav = Crp[exitDir];
        navName = $"C R P {exitDir}";
        return $"{c}, after departure exit via C R P {exitDir}, not above {Alt(MaxFt)} until leaving the control zone, {clr}";
    }

    /// Takeoff clearance, otherwise wait: emergency / landing traffic up to 4 NM -> hold short, runway occupied -> line up and wait (only behind a rolling aircraft with no approach traffic within 6 NM, A10),
    /// standing traffic on the runway or another takeoff clearance (A9) -> hold short.
    string DepartureClearance(Telemetry? t, IReadOnlyList<Traffic> traffic, double now)
    {
        var c = Cs();
        var rw = Runway;
        var opp = Airfield.Opposite(rw);
        var fin = FinalTraffic(traffic, rw, 6 * NM).FirstOrDefault();
        bool near = fin != null && Approach(fin.X, fin.Z, rw).along < 4 * NM;
        var head = FinalTraffic(traffic, opp, 4 * NM).FirstOrDefault();   // Landing aircraft on the opposite direction (runway request A41, AI lands per DCS wind)
        var on = RwyTraffic(traffic);
        if (EmgWithin(15) || near || head != null || on != null || RunwayClaimed > 0)
        {
            Phase = Phase.HoldShort;
            holdSince = now;
            lineUp = !EmgWithin(15) && fin == null && head == null && RunwayClaimed == 0 && on is { Speed: > 3 };
            return EmgWithin(15) ? $"{c}, hold short runway {RwSay(rw)}, emergency traffic inbound."
                 : lineUp ? $"{c}, runway {RwSay(rw)}, line up and wait, traffic on the runway."
                 : fin != null ? $"{c}, hold short runway {RwSay(rw)}, traffic {Describe(fin, rw)}."
                 : head != null ? $"{c}, hold short runway {RwSay(rw)}, opposite direction traffic, {Describe(head, opp)} runway {RwSay(opp)}."
                 : RunwayClaimed > 0 ? $"{c}, hold short runway {RwSay(rw)}, number {RunwayClaimed + 1} for departure."
                 : $"{c}, hold short runway {RwSay(rw)}, traffic {Describe(on!, rw)}.";
        }
        if (now - heavyDepAt < 120)   // R205: 2 min behind a departing Heavy (FAA JO 7110.65 3-9-6), clearance afterwards by itself (tick)
        {
            (Phase, holdSince, lineUp) = (Phase.HoldShort, now, false);
            return $"{c}, hold short runway {RwSay(rw)}, wake turbulence, 2 minute interval.";
        }
        var far = FinalTraffic(traffic, rw, 8 * NM).FirstOrDefault();
        return TakeoffClearance(t, traffic) + (far != null ? $" Traffic, {Describe(far, rw)}." : "");
    }

    /// V16: heavy traffic shortly ahead of me on the same runway axis (landed, departed, on final ahead of me).
    public static bool Heavy(string type) => Regex.IsMatch(type, @"^(KC-?135|KC[-_]?10|E-3|A-50|IL-7[68]|C-17|C-5|B-52|B-1B|Tu-95|Tu-160|Tu-142|An-124|KJ-2000)", RegexOptions.IgnoreCase);
    /// R205: at the front of the clearance with traffic information (FAA JO 7110.65 2-1-20): "caution wake turbulence, departing KC 135, ", otherwise "".
    string Wake(Telemetry? t, IReadOnlyList<Traffic> traffic, string rw)
    {
        double mine = t == null ? 0 : Approach(t.X, t.Z, rw).along;
        var w = traffic.FirstOrDefault(a => Heavy(a.Type) && a.Speed > 30 && a.AltMsl - FieldElev < 900 &&
            Approach(a.X, a.Z, rw) is var (al, lat) && lat < 0.5 * NM && al < mine && al > -(F.End(rw).Len + 5 * NM));
        return w == null ? "" : $"caution wake turbulence, {(HeavyDep(w, rw) ? "departing" : "landing")} {Ops.TypeSay(w.Type)}, ";
    }
    /// R205: Heavy in the takeoff roll or departure of runway rw (AI flag dep, otherwise climbing behind the threshold).
    bool HeavyDep(Traffic a, string rw) => a.Flag.StartsWith("dep") || a.InAir && a.Vs > 1 && Approach(a.X, a.Z, rw).along < 0;

    string ClearText() => option switch
    {
        "touch and go" => "cleared touch and go" + Closed(),
        "the option" => "cleared for the option" + Closed(),
        "low approach" => "cleared low approach" + Closed(),
        "low pass" => $"cleared low pass, not below {Alt(Math.Ceiling((FieldElev / Ft + 500) / 100) * 100)}, after the pass fly runway heading, climb and maintain {Alt(TransitFt)}",   // R326: pattern altitude + 500 ft (FAA JO 7110.65 3-10-10)   // A47 (ICAO Doc 4444 Ch. 12)
        _ => "cleared to land",
    };
    string Closed() => stayPattern && !Ifr && !wantStraight ? $", {Hand(Runway).Split(' ')[0]} closed traffic approved, report base" : "";   // FAA 7110.65 3-10: traffic pattern together with the clearance, then quiet

    /// baseCall: "base" report (A44), the base position then counts like the final.
    List<Msg> Landing(Telemetry? t, IReadOnlyList<Traffic> traffic, string c, bool baseCall = false)
    {
        var rw = t == null ? Runway : Aligned(t);
        rwSwitched = false;   // R334: every answer here names the runway
        if (Emergency)
        {
            Phase = Phase.ClearedLand;
            return Say($"{c}, runway {RwSay(EmergencyRunway(t))}, {Wind(t)}, cleared to land, emergency services standing by.", "Tower");
        }
        bool lined = t != null && (OnFinalLoose(t, rw) || OnLongFinal(t, rw) || baseCall);
        if (Phase == Phase.ClearedLand && lined) return Say($"{c}, roger, runway {RwSay(rw)}, {ClearText()}.");   // R336: runway first (FAA JO 7110.65 3-10-5 a)   // already cleared (tick): confirm briefly, not everything again
        // A43: no landing clearance on the initial, first the break and "base" (FAA JO 7110.65 3-10); tick switches to Pattern on downwind
        if (Phase == Phase.Initial && !baseCall && (t == null || lined) && !(t != null && StraightIn(t, rw))) return Say($"{c}, roger, report base.");
        if (t != null && !lined && Phase == Phase.Entering && navName == "circle") return CircleJoin(t, traffic, $"{c}, {F.StationOf("Tower")}");   // R212: "final" on the opposite runway = first call for circling
        if (t != null && !lined)
        {
            var other = F.Ends.Select(e => e.Name).FirstOrDefault(e => HdgDiff(LandHdg(e), LandHdg(rw)) > 10 && OnFinalLoose(t, e));   // Parallel runway: Aligned
            if (other != null && ViaOther(other) && Phase == Phase.Entering)   // guided this way: overhead via the opposite direction
                return Say($"{c}, negative, runway {RwSay(rw)} in use, continue overhead, {Hand(rw)} pattern runway {RwSay(rw)}, report overhead.");
            if (other != null)
            {
                Phase = Phase.Pattern;
                PatternReset();
                wrongRwyTold = true;   // otherwise tick says it again right away
                return Say($"{c}, check runway! You are lined up for runway {RwSay(other)}, runway {RwSay(rw)} in use. " +
                           $"Join {Hand(rw)} downwind runway {RwSay(rw)}, {Alt(PatternFt)}.");
            }
            return Neg($"{c}, negative, I don't have you on final. You are {Where(t)}. Report final runway {RwSay(rw)} when established.");
        }
        landCall = true;   // Reporting duty: landing report made, from now on the tower gives the clearance on its own (runway clear, predecessor landed)
        if (EmgWithin(8) || TrafficOnRunway(traffic))   // R37: MAYDAY further out: landing clearance with "expedite vacating"
        {
            Phase = Phase.Pattern;
            rwyBusyTold = true;
            return Say($"{c}, continue approach, {(EmgWithin(8) ? "emergency traffic" : BusyWhy(traffic))}.");
        }
        var (no, lead) = Sequence(t, traffic, rw);
        if (no > 1)
        {
            Phase = Phase.Pattern;
            return Say($"{c}, number {no}, follow the {Follow(lead!, rw)}, continue approach.");   // R335: leg instead of position
        }
        Phase = Phase.ClearedLand;
        return Say($"{c}, {Wheels()}{Wake(t, traffic, rw)}runway {RwSay(rw)}, {Wind(t)}, {ClearText()}{EmgNote()}.");
    }

    /// Foreign MAYDAY: into holding (like case "inbound"), out only after the emergency (tick, free).
    List<Msg> EmergencyHold(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        bool pattern = Phase != Phase.Inbound;
        var by = Owner();   // Traffic pattern: Tower, otherwise Approach
        Phase = Phase.Inbound;
        PatternReset();
        holding = true; holdArrived = false; vec = null; entry = null; lastHoldInfo = now; lastEat = -1;
        var fix = HoldFix(t);
        holdFt = RouteFt(t, traffic, fix, HoldFtAt(fix));
        double d = Dist(t.X, t.Z, fix.X, fix.Z);
        return Say($"{Cs()}, emergency in progress, {(pattern ? "leave the pattern, " : "")}{Steer(t, fix, "the hold")}, {AltTo(t, holdFt)}{HoldSpd(t)}, " +
                   $"{(HoldAt(fix) is { } hr ? $"hold {hr}, {Miles(d)} miles to go" : $"{Miles(d)} miles to the holding point")}, I will call you.", by);
    }

    /// Emergency: the runway the pilot is already aligned with, otherwise the active one.
    string EmergencyRunway(Telemetry? t) =>
        t == null ? Runway : F.Ends.Where(e => OnFinalLoose(t, e.Name)).MinBy(e => Approach(t.X, t.Z, e.Name).lateral)?.Name ?? Runway;

    /// Parallel runways: on final (OnFinal up to 5 NM) aligned with the other one -> this one applies to the approach (clearance, glide path, low-altitude warning, scoring).
    /// Aligned = with landing indicators, heading within 10° and in the inner third of the runway spacing (Dubai 383 m): not when turning in across the other, not in the overhead approach.
    string Aligned(Telemetry t)
    {
        var (r, p) = (F.End(Runway), F.Ends.Where(e => HdgDiff(e.Hdg, LandHdg(Runway)) < 10).MinBy(e => Approach(t.X, t.Z, e.Name).lateral)!);
        if (p != r && LandingCues(t) && OnFinal(t.X, t.Z, t.Hdg, t.Agl, p.Name, 5 * NM) && HdgDiff(t.Hdg * 180 / Math.PI, p.Hdg) < 10 &&
            Approach(t.X, t.Z, p.Name).lateral < Math.Abs((p.CX - r.CX) * r.Dz - (p.CZ - r.CZ) * r.Dx) / 3)
        { if (vrw == Runway) vrw = p.Name; rwSwitched |= Phase == Phase.ClearedLand; Runway = p.Name; }
        return Runway;
    }

    /// Approach hands over to the Tower at the CRP.
    List<Msg> AtCrp(string at, string pre = "")
    {
        var c = Cs();
        var rw = Runway;
        Phase = Phase.Entering; lastAltFt = PatternFt;   // R256: Approach states the altitude, the Tower does not repeat it
        handoffAt = lastSeen; towerDue = true; towerNag = false;   // R32: no "check altitude" for 60 s after this handover either
        entry = at;
        if (at == "north")
        {
            nav = Ovh; navName = "overhead";
            return Say($"{c}, {pre}proceed overhead, {Alt(PatternFt)}, contact {Contact("Tower")}, report overhead.", "Approach");
        }
        nav = OnCenterline(rw, InitialDist); navName = "initial";
        return Say($"{c}, {pre}proceed to initial runway {RwSay(rw)}, {Alt(PatternFt)}, contact {Contact("Tower")}, report initial for overhead break.", "Approach");
    }

    // Holding if the pattern is full (>= 2 aircraft) or another emergency is running
    int PatternCount(IReadOnlyList<Traffic> traffic) =>
        traffic.Count(a => a.InAir && a.Speed > 30 && Dist(a.X, a.Z, CX, CZ) < 4 * NM && a.AltMsl - FieldElev < 900);

    /// Holding 15 NM from the airfield, outside radar vectoring (downwind, base) and away from the runway axis (final, departure).
    (double X, double Z) HoldFix(Telemetry? t)
    {
        // Directions up to 90° beside the pilot's (never across the airfield, 600 ft per 45° detour), not close to the runway axis (< 30°: 3000 ft, < 60°: 500 ft), terrain
        double b0 = t == null ? 0 : Bearing(CX, CZ, t.X, t.Z);
        return Enumerable.Range(-2, 5).Select(k => (k, B: b0 + k * 45)).Select(c => (c.k, c.B, P: (X: CX + 15 * NM * Math.Cos(c.B * Math.PI / 180), Z: CZ + 15 * NM * Math.Sin(c.B * Math.PI / 180))))
                         .MinBy(c => F.MvaLeg(c.P.X, c.P.Z, CX, CZ) + 600 * Math.Abs(c.k) +
                                     (Math.Min(HdgDiff(c.B, LandHdg(Runway)), HdgDiff(c.B, LandHdg(Runway) + 180)) is var ax && ax < 30 ? 3000 : ax < 60 ? 500 : 0)).P;
    }

    /// Holding on the TACAN radial/DME of the airfield ("on the 260 radial, 15 DME", radial magnetic from the airfield), null without TACAN.
    string? HoldAt((double X, double Z) p)
    {
        if (F.Tacan <= 0) return null;
        int rad = (int)Math.Round(((Bearing(CX, CZ, p.X, p.Z) - MagVar) % 360 + 360) % 360);
        return $"on the {Digits((rad == 0 ? 360 : rad).ToString("000"))} radial, {Miles(Dist(CX, CZ, p.X, p.Z))} DME";
    }

    /// R126: first contact with the Tower (not handed over by Approach) gets runway, wind and QNH before entering the pattern, unless the ATIS is confirmed.
    bool WxDue(string n) => (Phase is Phase.Away or Phase.Departing or Phase.Inbound || needWx) && !AtisHeard(n);

    List<Msg> InitialClear(Telemetry? t, IReadOnlyList<Traffic> traffic, string brk = "", bool wx = false)
    {
        var c = Cs();
        var rw = Runway;
        var w = wx ? $", {Wind(t)}, {Qnh(t)}" : "";
        needWx = false;
        Phase = Phase.Initial;
        PatternReset();
        var (no, lead) = Sequence(t, traffic, rw, true);
        // R200/R203: answer to "initial" is the break clearance with reporting point base (FAA JO 7110.65 3-10-12), direction only for right-hand pattern ("right turns")
        var rt = Hand(rw) == "right hand" ? ", right turns" : "";
        return Say(no > 1
            ? $"{c}, number {no}, follow the {Follow(lead!, rw)}, runway {RwSay(rw)}{w}{rt}, {brk}report base."
            : $"{c}, runway {RwSay(rw)}{w}{rt}, {brk}report base.");
    }

    List<Msg> OverheadJoin(Telemetry? t = null, bool wx = false)
    {
        Phase = Phase.Pattern;
        PatternReset();
        needWx = false;
        return Say($"{Cs()}, join {Hand(Runway)} downwind runway {RwSay(Runway)}{(wx ? $", {Wind(t)}, {Qnh(t)}" : "")}, {Alt(PatternFt)}, report base.");
    }

    /// R212: first call to the Tower after the circling clearance: "circle to runway 31, left traffic, report base" (FAA JO 7110.65 4-8-6, 3-10-1), then traffic pattern.
    List<Msg> CircleJoin(Telemetry? t, IReadOnlyList<Traffic> traffic, string hdr)
    {
        Phase = Phase.Pattern;
        PatternReset(); navName = "circle";   // Pattern after circling: "base", path via the opposite runway (ViaOther); PatternReset afterwards (go-around, touch and go) clears it
        needWx = false;
        return Say($"{hdr}, {Wind(t)}, {Qnh(t)}, " + (CircleIfr ? $"circle to runway {RwSay(Runway)}, {Hand(Runway).Split(' ')[0]} traffic" : $"join {Hand(Runway)} downwind runway {RwSay(Runway)}, {Alt(PatternFt)}") +   // Review R212: without procedure downwind
                   $", report base.{TrafficNote(traffic, Runway)}", "Tower");
    }

    void PatternReset() { nav = null; lastAltFt = -1; if (navName == "circle") navName = "";   // Review R212: no old "circle" (otherwise a later first call at the Tower becomes circling)
        baseCalled = extended = s360 = wrongRwyTold = finalLowTold = iniAsked = iniWaved = ovhAsked = ovhWaved = crpAsked = crpWaved = landCall = finAsked = gearDown = sfo = towerDue = towerNag = false; }
    double vecLim;   // R315: lead for the next waypoint turn
    bool intentsAsked;   // R318: "say intentions" already said within the gate
    bool fromHold;   // R319: radar vectoring from holding
    bool s360;   // R330: full circle on base already instructed
    bool iniAsked, iniWaved;   // at the initial without a call "report initial" said once; afterwards into the break without a call: once "no break clearance"
    bool ovhAsked, ovhWaved, crpAsked, crpWaved;   // Reporting duty overhead/C R P: at the point without a call once "report …", afterwards continuing without a call once "no clearance to join" or "say position"
    double keepAt = 1e9, keepRef; bool keepTold;   // Review l1: time of the guidance "remain outside …", distance C R P – airfield, violation already announced
    bool landCall, finAsked, gearDown;   // Reporting duty landing: landing report (base/final) made for this approach / "report final" said without a report / R201 "gear down" reported
    bool towerDue, towerNag;   // Reporting duty: handed over to the tower ("contact Tower"), no call there yet / "contact Tower now" already said
    bool sfo;   // R204: simulated flameout running (high key -> low key -> clearance)
    public bool Sfo => sfo;   // R307: radio wheel High key/Low key
    double heavyDepAt = -999;   // R205: last seen Heavy in takeoff roll/departure of the runway (2-min interval)
    /// R201: wheels-down check before every landing clearance without reported gear (FAA JO 7110.65 2-1-24), R261: also low approach/SFO, not for the low pass.
    /// No check for helicopters/types with fixed gear.
    string Wheels() => gearDown || option == "low pass" || Regex.IsMatch(AcType, @"^(UH-|Mi-8|AH-|CH-|OH|SA342|AS532|Christen)") ? "" : "check wheels down, ";
    /// Reporting point for the landing report: visual pattern "base" (R200), straight-in/instrument approach "final". R212: circling likewise "base".
    string RepPt() => (Ifr || wantStraight) && navName != "circle" || Phase == Phase.Entering && navName == "six mile final" ? "final" : "base";
    /// Violation only into the debriefing (without "possible pilot deviation" in the call), once per kind like Dev.
    List<Msg> Note(List<Msg> m, string de, string en)
    {
        if (!Watch || !devTold.Add(en)) return m;
        var at = Carrier.Clock >= 0 ? $" ({(int)(Carrier.Clock / 3600) % 24:00}:{(int)(Carrier.Clock / 60) % 60:00})" : "";
        m.Add(new Msg("Info", L($"Verstoß: {de}{at}", $"Deviation: {en}{at}")));
        return m;
    }
    /// Go-around by the tower or detected go-around (A11, A12, wording A98): back into the traffic pattern.
    List<Msg> GoAround(string c, string head)
    {
        Phase = Phase.Pattern; PatternReset();
        return Say($"{c}, {head}, climb and maintain {Alt(PatternFt)}, join {Hand(Runway)} downwind runway {RwSay(Runway)}, report base.");
    }
    /// Terrain: Approach deliberately leads over the opposite-direction centerline to overhead or circling -> no "check runway" there.
    bool ViaOther(string other) => vecOver && other == vrw && (Phase == Phase.Inbound && vec != null || Phase == Phase.Entering && navName is "overhead" or "circle" || Phase == Phase.Pattern && navName == "circle");   // R212: Circling onto the opposite runway
}
