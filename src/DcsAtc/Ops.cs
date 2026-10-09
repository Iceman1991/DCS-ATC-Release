using System.Text.RegularExpressions;
using static DcsAtc.Tower;

namespace DcsAtc;

/// Further controllers per player: range control (Range Alpha), AWACS (Overlord), tanker (Shell/Texaco …)
/// and notes for the debriefing. Rule-based like Tower: text/telemetry in, radio calls out.
partial class Ops
{
    const double NM = 1852, Ft = 0.3048, Kt = 0.514444;
    public record Call(string Role, string Station, string Text, double Freq = 0, Ops? To = null, bool All = false, int Lead = 0);   // Freq: own frequency (mission AWACS), 0 = from the config; To: call to this other player (tanker: the next one); All: to everyone (Gid 0, tanker turn A121)
    /// The player from the controllers' point of view.
    public record Me(string Callsign, Telemetry? Tel, string Type, int Coalition, int Flight = 1, double Fuel = -1);   // Flight: aircraft in the flight (player + AI wingmen); Fuel: tank 0–1, -1 unknown

    // ------------------------------------------------------------ World (Program sets it from DcsAtc-State.txt)
    public static (double X, double Z, double R)? Range;              // Range Alpha (TrainingRange_build.lua)
    public static Dictionary<int, (double X, double Z)> Bulls = new(); // Bullseye per side (1 red, 2 blue; 0 = old mission)
    public static Dictionary<string, string> Beacons = new();          // tanker group -> TACAN "51X"
    public static Dictionary<string, string> Tracks = new();           // tanker group -> track "Alpha" (mission)
    public static Dictionary<string, double> AwacsFreq = new();        // AWACS group -> radio frequency MHz (mission)
    public static IReadOnlyDictionary<string, string> Callsigns = new Dictionary<string, string>();   // AI group -> callsign "Magic11" (Program)
    public static double MagVar = 6;
    public static bool AwacsBullseye = true;                            // LK4: picture and updates to everyone as bullseye (config AwacsBullseye, Program); false = all BRAA
    public static double NewGroupNm = 200;                              // AW-P3: radius for new group/split/combined (config AwacsNewGroupNm, Program)
    public const double RadarNm = 250;                                  // Radar picture (hostiles); R53: picture clean only without a group in it, also AI radio (Flights)
    static readonly HashSet<Ops> hot = new();                           // who is currently "in hot" (range occupied)

    public readonly List<string> Debrief = new();
    int tkContacts; double tkLb;   // N39: counters for the debriefing (contacts, fuel taken); not in State: after a restart counting begins anew
    int rgPasses; double rgBestD = double.MaxValue; int rgBestClock;   // N39: range over all visits of the flight (passes/bestD begin anew per visit, A126)

    /// N39: summary of the individual scores (tanker, range); empty if there was nothing.
    public List<string> DebriefSummary()
    {
        var l = new List<string>();
        if (tkContacts > 0) l.Add(L($"Luftbetankung gesamt: {tkContacts} Kontakt{(tkContacts > 1 ? "e" : "")}", $"Air refueling total: {tkContacts} contact{(tkContacts > 1 ? "s" : "")}") + (tkLb > 0 ? L($", {tkLb:0} lb erhalten", $", {tkLb:0} lb received") : ""));   // without fuel capacity of the type (MaxLb 0) no amount, like the single line
        if (rgPasses > 0) l.Add(L($"Range Alpha gesamt: {rgPasses} {(rgPasses > 1 ? "Anflüge" : "Anflug")}", $"Range Alpha total: {rgPasses} pass{(rgPasses > 1 ? "es" : "")}") + (rgBestD < 5000 ? L($", bester Einschlag {(rgBestD < 1.5 ? "shack" : $"{Feet(rgBestD)} bei {rgBestClock} Uhr")}", $", best impact {Score(rgBestD, rgBestClock)}") : ""));
        return l;
    }
    public void ResetDebrief() { Debrief.Clear(); (tkContacts, tkLb, rgPasses, rgBestD, rgBestClock) = (0, 0, 0, double.MaxValue, 0); }

    // Range
    bool checkedIn, isHot, isDry;   // isDry: R285 "cleared dry", occupies the range until "off"
    string? hung;   // R290: "hung ordnance"/"hot gun" reported -> no further passes, exit via the hung-ordnance route
    int passes, gunHits;
    readonly List<(double D, int Clock)> pass = new();
    double bestD = double.MaxValue;
    int bestClock;
    double lastCold = -99;
    // Impacts of a drop (A72): same weapon, gap < 3 s -> one call from Tick; cold (R58) counts as one violation
    readonly List<(double D, int Clock)> vol = new();
    string volW = "";
    double volAt;
    bool volCold, wasHot, killTold;
    double hotAt = -99, coldAt = -99;   // Pass start/end; impacts count by the drop time (time of flight from the mission), without it and for kills 20 s grace after the end
    string? passRow;   // Debriefing line of the most recently ended pass: late impacts (after "off") supplement it
    double rgActAt;   // A119: "in hot" or last scored impact; without "off" the range frees itself
    double rgAway = double.NaN;   // A119: since when over 3 NM with heading away (NaN = not away)
    // Tanker: 0 nothing, 1 approach, 2 observation, 3 pre-contact, 4 contact
    string tanker = "";   // Tanker's group (R119: not the call name, two Texaco are two tankers)
    int tank;
    double contactAt, offload;                // offload: amount received until departure in lb ("total offload")
    public (string Step, string Group)? Menu;  // due DCS radio menu action: "intent" / "precontact"
    public bool DcsIntent;                    // Mission has pressed "Intent to refuel" (M reply); otherwise no direct F1 "Ready pre-contact" (would hit an open DCS-ATC dialog)
    public Airfield? Divert;                  // A15: MAYDAY/PAN at the AWACS -> Program selects this airfield and gives the call to its Approach
    // AWACS: memory per unit (Id), a group counts as reported if one of its units is reported (lead kill, regrouping)
    readonly Dictionary<int, double> mergeTold = new();
    readonly Dictionary<int, (int Band, double At)> threatTold = new();   // unit -> last reported range step (NM) and time
    readonly HashSet<int> groupsTold = new(), committed = new(), mine = new(), judy = new();   // mine: LK5 taken by the player (targeting/engaged); judy: LD3 he has it himself (targeted/engaged/fox/judy) -> no more target updates
    readonly HashSet<int> single = new();   // LD8: reported as "single group" -> new name comes with a short picture
    double renameAt = -1e9;
    bool hadAw;   // LD10: an own AWACS in the air in the last tick
    readonly HashSet<int> hvaaTold = new();   // LD9: threat to the AWACS reported
    readonly Dictionary<int, string> members = new();   // V10/A56: unit -> group identifier (confirmed); one that misses a detection cycle stays in
    Dictionary<int, string> named => Named(side);       // R54/LK3: unit -> group name ("north group") of the side, until the next picture, picture clean or forgotten
    int side;                                           // LK3: the player's side (SetAwacs)
    readonly Dictionary<int, double> seenAt = new();    // AW-P3: unit -> last detected; gone over 2 min = forgotten, return is reported anew
    Dictionary<string, double> evSince = new();         // A56: split/combined only after 12 s stable ...
    readonly Dictionary<string, double> evAt = new();   // ... and per group at most every 60 s
    int tokSeq;
    static readonly Dictionary<string, (double At, Ops By)> net = new();   // A64: per side and AWACS frequency last announced ("2:251:42" unit, "2:251:clean") and by whom
    bool radarContact, hadContacts, awacsIn, watching, onStation;   // awacsIn: checked in with the AWACS (only then does it call by itself); watching: memory running; onStation: LK5 CAP reported ("on station")
    public bool AwacsIn => awacsIn;   // N2: tactical control (Tower.Tactical)
    public bool RangeIn => checkedIn;   // R307: radio wheel (Program.WheelFits)
    public int Tank => tank;
    double lastHostile;
    double? telGone;   // R309: since when no telemetry (data gap vs. gone)

    /// Role by name/keyword of a call without prefix. R388: own = the sender's call sign (blanked, "Spike 1-1" is no AWACS name); "declare" counts only with a position
    /// and not in a call to an airfield station or with an emergency word ("Kutaisi Tower, declare an emergency").
    static string Blank(string n, string? own) => own == null ? n : Regex.Replace(n, $@"\b{Regex.Replace(Normalize(own), @"(?<=\d) (?=\d)", " ?")}\b", " ");
    /// R393: "say again" without a station (own call sign blanked): repeats the last call of the controller that spoke last (Program.Request)
    public static bool BareSayAgain(string n, string? own = null) => Regex.IsMatch(Blank(n, own).Trim(), @"^(?:say again|say again please)$");
    public static string? RoleOf(string n, string? own = null)
    {
        n = Blank(n, own);
        return Range != null && (Regex.IsMatch(n, @"^range\b") || Has(n, "range control", "range alpha", "range ")) ? "Range"   // A17: without a range zone in the mission no range controller
          : Has(n, "overlord", "awacs", "magic", "moscow", "darkstar", "wizard", "focus", "spike", "request sort", "picture", "bogey dope")
            || Has(n, "declare") && !Has(n, "tower", "ground", "approach", "departure", "mayday", "pan pan", "emergency") && Regex.IsMatch(n, @"\bdeclare\b(?: [a-z]+){0,3}? \d") ? "AWACS"
          : Has(n, "tanker", "shell", "texaco", "arco", "refuel") ? "Tanker"
          : Regex.IsMatch(n, @"\b(?:jtac|fac a|faca|forward air controller)\b") || Leaders.Any(l => CsKey(l) is { Length: > 2 } k && Has(n, k)) ? "JTAC"   // J5
          : null;
    }

    static string Cs(Me me) => SpokenCallsign(me.Callsign);
    internal static string Brg(double trueBrg) { int m = (int)Math.Round(((trueBrg - MagVar) % 360 + 360) % 360); return Digits((m == 0 ? 360 : m).ToString("000")); }
    internal static string Angels(double altM) => altM / Ft < 1000 ? $"cherubs {Math.Max(1, (int)Math.Round(altM / Ft / 100))}" : $"angels {(int)Math.Round(altM / Ft / 1000)}";
    /// Enemy altitude as usual on the radio: "20 thousand" (R52, ATP 1-02.1); „angels/cherubs“ remains for own and tankers (Angels).
    internal static string Thousand(double altM) { double ft = altM / Ft; return ft < 1000 ? $"{Math.Max(1, (int)Math.Round(ft / 100))} hundred" : $"{(int)Math.Round(ft / 1000)} thousand"; }
    static string Feet(double d) => $"{Math.Round(d / Ft / 5) * 5:0} feet";   // R374: range scores in feet (US ranges), rounded to 5 ft
    /// R237: direct hit is called "shack" (ATP 1-02.1), without clock position; otherwise "40 feet at 6 o'clock".
    static string Score(double d, int clock) => d < 1.5 ? "shack" : $"{Feet(d)} at {clock} o'clock";

    /// unsure: Whisper confidence below the threshold (R16) -> ask back instead of acting (an unclear "in hot" is not "cleared hot").
    public List<Call> OnTranscript(string role, string text, Me me, IReadOnlyList<Traffic> air, IReadOnlyList<Airfield> fields, double now, bool unsure = false, double cfgAwacs = 251.5, Func<Airfield, (double Qnh, double Wx, double Wz)>? qnhOf = null)   // cfgAwacs: AWACS frequency from the config (handover at range check-out); qnhOf: QNH (0 = unknown) and wind of an airfield from the mission weather
    {
        var n = Normalize(text.Contains(':') ? text[(text.IndexOf(':') + 1)..] : text);
        if (role == "JTAC") { var jr = JtacCall(n, me, now, unsure); if (jr.Count > 0 && !jr.Any(c => c.Text.Contains("say again"))) lastRole[role] = jr; return jr; }   // J6
        if (unsure || Has(n, "radio check", "how do you read"))
        {
            var st = role == "Range" ? "Range Alpha" : role == "AWACS" ? AwacsName(AwacsOf(air, me), me) : Tankers(air, me).FirstOrDefault()?.Name ?? "Tanker";
            return new() { new(role, st, unsure ? $"{Cs(me)}, {st}, say again." : $"{Cs(me)}, {st}, read you five.", role == "AWACS" ? AwacsFreqOf(AwacsOf(air, me)) : 0) };
        }
        if (role != "AWACS" && Has(n, "say again") && lastRole.TryGetValue(role, out var lc)) return lc;   // R393: repeat the last call of the Range/tanker (AWACS: `last`)
        var res = role == "Range" ? RangeCall(n, me, air, fields, cfgAwacs, now, qnhOf) : role == "AWACS" ? AwacsCall(n, me, air, fields, now) : TankerCall(n, me, air, now);
        if (role == "Range") HotEdge(now);   // Pass start/end to the second of the call
        if (role != "AWACS" && res.Count > 0 && !res.Any(c => c.Text.Contains("say again"))) lastRole[role] = res;   // R393 (not the "say again" of the controller himself)
        return res;
    }
    readonly Dictionary<string, List<Call>> lastRole = new();   // R393: last reply per role ("say again")

    // ======================================================================= Range
    static Call R(string s) => new("Range", "Range Alpha", s);

    List<Call> RangeCall(string n, Me me, IReadOnlyList<Traffic> air, IReadOnlyList<Airfield> fields, double cfgAwacs, double now, Func<Airfield, (double Qnh, double Wx, double Wz)>? qnhOf)
    {
        var c = Cs(me);
        if (Range is not { } z) return new() { R($"{c}, Range Alpha, range is closed.") };
        if (Has(n, "hung", "hot gun")) hung = Has(n, "hot gun") ? "hot gun" : "hung ordnance";   // R290: remembered in every range call (until the next check-in)
        if (Has(n, "check out", "checking out", "leaving", "departing", "good day"))
        {
            if (!checkedIn) return new() { R($"{c}, Range Alpha, you are not checked in.") };
            checkedIn = false; if (isHot) EndPass(); else Cold();   // R58: pass without "off" counts (debriefing, total)
            var sum = passes == 0 ? "no passes" : $"{passes} pass{(passes > 1 ? "es" : "")}" + (bestD < 5000 ? $", best impact {Score(bestD, bestClock)}" : "");
            var own = fields.Where(f => f.Side == 0 || f.Side == me.Coalition).MinBy(f => Dist(z.X, z.Z, f.X, f.Z));   // A123: exit toward the nearest own airfield
            var exit = own != null ? $" to the {Card(Bearing(z.X, z.Z, own.X, own.Z))}" : "";
            var next = AwacsContact(air, me, cfgAwacs) is { } aw ? $", contact {aw}" : "";
            if (hung != null)   // R251 (AFI 13-212): exit via the hung-ordnance route, inform Approach
                return new() { R($"{c}, Range Alpha, copy {hung}, check switches safe. {sum}. Cleared off the range{exit} via the hung ordnance route, advise Approach, good day.") };
            return new() { R($"{c}, Range Alpha, check switches safe. {sum}. Cleared off the range{exit}{next}, good day.") };
        }
        if (Has(n, "winchester"))   // N36: ammunition empty -> end pass, switch safe
            return new() { R($"{c}, {Ended(post: ", ")}check switches safe, report checking out.") };
        if (Has(n, "off"))
        {
            if (hung != null) return new() { R($"{c}, {Ended(post: ", ")}copy {hung}, check switches safe, no further hot passes, report checking out.") };   // R290 (AFI 11-214)
            if (!isHot) { Cold(); return new() { R($"{c}, roger, range is cold.") }; }   // Cold: no longer waits for "cleared hot" (R236); also end of an "in dry"
            return new() { R($"{c}, {EndPass()}. Report in hot or checking out.") };
        }
        if (Has(n, "dry"))   // N36: "in dry" = dry pass without weapons; R285: checked like "in hot", occupies the range until "off", without scoring
            { dryReq = true; return new() { R(InHot(me, z, now)) }; }
        if (Has(n, "in hot", "rolling in", "inbound hot") || !Has(n, "check") && (Has(n, "in from", "in heading", "in now", "ready in") || Regex.IsMatch(n, @"\bin$")))   // "checking in from the north" is a check-in, not an in hot
            { dryReq = false; return new() { R(InHot(me, z, now)) }; }
        // R234: "IP inbound" after the check-in is not a new check-in (no reset, no briefing), only "continue"; before it, it is not one
        if (Has(n, "inbound") && !Has(n, "check") && (checkedIn || Has(n, "ip", "i p")))
            return new() { R(checkedIn ? $"{c}, continue." : $"{c}, Range Alpha, negative, you are not checked in. Report check in.") };
        if (Has(n, "check in", "checking in", "inbound", "request entry", "entering", "with you", "request range"))
        {
            RangeReset(); checkedIn = true;   // A126: every visit starts at pass 1
            var t = me.Tel;
            var far = t != null && Dist(t.X, t.Z, z.X, z.Z) > 3 * NM;
            // QNH of the nearest airfield from the mission weather like Tower; reduced from the aircraft only as a fallback (standard atmosphere, wrong at altitude in a warm/cold mission)
            var nf = qnhOf == null ? default : fields.Select(f => (f, w: qnhOf(f))).Where(x => x.w.Qnh > 0).OrderBy(x => Dist(z.X, z.Z, x.f.X, x.f.Z)).Select(x => x.w).FirstOrDefault();   // nearest airfield with weather data (QNH 0 = none)
            var fq = nf.Qnh;
            var qv = fq > 0 ? fq : t != null && Baro(t) is { } b ? b.Qnh : 0;
            var qnh = qv > 0 ? $", {QnhSay(qv, InHg(me.Type))}" : "";
            // R132: wind at the target (unguided weapons) from the same airfield, magnetic like Tower.Wind, under 3 kt calm, omitted without weather data
            var wk = Math.Sqrt(nf.Wx * nf.Wx + nf.Wz * nf.Wz) / Kt;
            var wd = (int)Math.Round(((Math.Atan2(-nf.Wz, -nf.Wx) * 180 / Math.PI - MagVar) % 360 + 360) % 360 / 10) * 10;
            var wind = nf.Qnh <= 0 ? "" : wk < 3 ? ", wind on target calm" : $", wind on target {Digits((wd == 0 ? 360 : wd).ToString("000"))} at {Digits(((int)Math.Round(wk)).ToString())}";
            var where = far ? $" Range bears {Brg(Bearing(t!.X, t.Z, z.X, z.Z))}, {MilesTxt(Dist(t.X, t.Z, z.X, z.Z))}." : "";
            // R57: approach headings = the side the player is on (magnetic rounded to 10°, ±20°); close in or without position free
            var rh = far ? Math.Round((Bearing(t!.X, t.Z, z.X, z.Z) - MagVar) / 10) * 10 + MagVar : 0;
            var runIn = far ? $"Enter from the {Card(rh + 180)}, run-in headings {Brg(rh - 20)} to {Brg(rh + 20)}" : "Run-in headings any";
            // R235: wording per JFIRE/AFI 13-212 ("range is cold" or "one aircraft in"), no targets (the range knows none), continue with "IP inbound"
            var busy = hot.Count > 0 ? ", one aircraft in" : ", range is cold";
            return new() { R($"{c}, Range Alpha{qnh}{wind}{busy}.{where} {runIn}, minimum altitude 1500 feet AGL. Report IP.") };
        }
        return new() { R($"{c}, Range Alpha, say again. Report check in, in hot, off, or checking out.") };
    }

    void Cold() { isHot = isDry = false; hot.Remove(this); waitHot = false; offAsk = -1; }
    /// End pass or dry pass: hot "<pre>pass N, …<post>", otherwise only range free (R285).
    string Ended(string pre = "", string post = "") { if (isHot) return pre + EndPass() + post; Cold(); return ""; }
    bool waitHot;        // R236: "in" with occupied range -> "cleared hot" by itself as soon as it is free (Tick)
    bool dryReq;         // R285: last request was "in dry" (also for the clearance from Tick)
    double offAsk = -1;  // #21: when "report off" was asked (-1 = not)

    /// "in hot"/"in" (also from Tick after R236): clearance or reason against.
    string InHot(Me me, (double X, double Z, double R) z, double now)
    {
        var c = Cs(me);
        waitHot = false;
        if (!checkedIn) return $"{c}, Range Alpha, negative, you are not checked in. Report check in.";   // R57: cleared hot only after the check-in
        if (hung != null) return $"{c}, negative, {hung}, report checking out.";   // R290: no further passes
        // R14: cleared hot only airborne, within 10 NM and (outside the zone) heading toward the area (JFIRE)
        if (me.Tel is { } ht && (OnGround(ht) || Dist(ht.X, ht.Z, z.X, z.Z) > 10 * NM)) return $"{c}, continue, report in inside 10 miles.";
        if (me.Tel is { } ha && Dist(ha.X, ha.Z, z.X, z.Z) > z.R && HdgDiff(ha.Hdg * 180 / Math.PI, Bearing(ha.X, ha.Z, z.X, z.Z)) > 60) return $"{c}, continue dry.";
        if (hot.Any(o => o != this)) { waitHot = true; return $"{c}, continue, one aircraft in."; }   // A119: no "fouled"; R236: clearance comes after it becomes free
        if (dryReq) { if (isHot) EndPass(); isDry = true; hot.Add(this); rgActAt = now; rgAway = double.NaN; offAsk = -1; return $"{c}, cleared dry."; }   // R285: occupied until "off", without scoring/pass counter
        isHot = true; isDry = false; hot.Add(this); rgActAt = now; rgAway = double.NaN; offAsk = -1;
        pass.Clear(); gunHits = 0; passRow = null;
        return $"{c}, cleared hot.";
    }
    /// New visit or player gone: close the running pass (R58), range free, reset hit/drop counters and best value to zero (A126).
    void RangeReset() { if (isHot) EndPass(); else Cold(); hung = null; passes = gunHits = 0; pass.Clear(); vol.Clear(); passRow = null; bestD = double.MaxValue; bestClock = 0; }

    /// End pass ("off", "winchester" or by itself): range free, evaluation for the debriefing, "pass N, <result>".
    string EndPass()
    {
        Cold();
        passes++; rgPasses++;
        var res = PassText();
        Debrief.Add(passRow = PassRow());
        vol.Clear();   // A72: what has impacted so far is in the pass report, Tick does not say it again (pass remains until "in hot" for late impacts)
        return pass.Count + gunHits == 0 ? $"pass {passes}" : $"pass {passes}, {res}";   // A18: no impact yet (bomb still falling): scoring comes with the impact, no "no hits scored"
    }
    /// Player gone (crash, left slot): range free again.
    public void Leave() { RangeReset(); checkedIn = false; OffLine(); if (cas > 0) CasEnd(); }

    /// Save session (app crash/restart): AWACS check-in and debriefing; range/tanker must be checked in again.
    public record State(bool AwacsIn, bool RadarContact, List<string> Debrief);
    public State Save() => new(awacsIn, radarContact, Debrief.ToList());
    public void Load(State s) { (awacsIn, radarContact) = (s.AwacsIn, s.RadarContact); Debrief.AddRange(s.Debrief); }

    string PassRow() => L($"Range Alpha Anflug {passes}: {PassText()}", $"Range Alpha pass {passes}: {PassText()}");
    string PassText()
    {
        var parts = new List<string>();
        var hits = pass.Where(p => p.D < 5000).ToList();
        if (hits.Count > 0)
        {
            var b = hits.MinBy(p => p.D);
            parts.Add(hits.Count == 1 ? (b.D < 1.5 ? "shack" : $"impact {Score(b.D, b.Clock)}") : $"{hits.Count} impacts, best {Score(b.D, b.Clock)}");
        }
        else if (pass.Count > 0) parts.Add($"{pass.Count} impact{(pass.Count > 1 ? "s" : "")} off target");
        if (gunHits > 0) parts.Add($"{gunHits} gun hit{(gunHits > 1 ? "s" : "")}");
        return parts.Count == 0 ? "no hits scored" : string.Join(", ", parts);
    }

    /// Remember hot change (Tick, impact, kill): new pass -> again a "target destroyed"; pass end -> time for the grace period.
    void HotEdge(double now)
    {
        if (isHot == wasHot) return;
        if (isHot) { killTold = false; hotAt = now; } else coldAt = now;
        wasHot = isHot;
    }
    /// R58: clearance at the time of the drop (now - flight, 2 s margin for event latency); without time of flight (kill, older mission) 60 s grace after the pass (A18: drop, "off", impact)
    bool Cleared(double now, double flight = -1)
    {
        HotEdge(now);
        if (flight < 0) return isHot || now - coldAt < 60;
        var rel = now - flight;
        return rel >= hotAt && (isHot || rel <= coldAt + 2);
    }

    static string Kind(string w) => Regex.IsMatch(w, "nurs|hydra|zuni|apkws", RegexOptions.IgnoreCase) ? "rocket" : Regex.IsMatch(w, "missile", RegexOptions.IgnoreCase) ? "missile" : "";

    /// Impact of a bomb/missile in the range (KutaisiATC.lua): distance to the nearest target, clock position (12 = approach direction).
    /// A72: impacts of the same drop (same weapon, gap < 3 s) are collected and scored from Tick as one call.
    /// R58: without clearance "check fire" (at most every 30 s) and one violation line per drop in the debriefing, no scoring.
    public List<Call> OnImpact(Me me, double dist, int clock, double now, string weapon = "", double flight = -1)   // flight: weapon time of flight in s (-1 unknown)
    {
        var res = vol.Count > 0 && (weapon != volW || now - volAt >= 3) ? Flush(me, now, true) : new();   // other drop: the old one first
        if (vol.Count == 0)
        {
            volW = weapon; volCold = !Cleared(now, flight);
            if (volCold)
            {
                Debrief.Add(L($"Verstoß: Abwurf ohne Freigabe auf Range Alpha ({WeaponName(weapon)})", $"Deviation: release without clearance on Range Alpha ({WeaponName(weapon)})"));
                if (now - lastCold >= 30)
                {
                    lastCold = now;
                    res.Add(R($"{Cs(me)}, check fire, check fire, check fire. Release without clearance, switches safe."));
                }
            }
        }
        vol.Add((dist < 0 ? 9999 : dist, clock)); volAt = now;
        if (!volCold)
        {
            pass.Add(vol[^1]); rgActAt = now;
            if (dist >= 0 && dist < bestD) { bestD = dist; bestClock = clock; }
            if (dist >= 0 && dist < rgBestD) { rgBestD = dist; rgBestClock = clock; }
            if (!isHot && passRow != null && Debrief.IndexOf(passRow) is var i and >= 0) Debrief[i] = passRow = PassRow();   // late impact: add to the pass line in the debriefing
        }
        return res;
    }

    /// "weapons.nurs.HYDRA_70_M151" -> "HYDRA 70 M151"
    static string WeaponName(string w) => w.Split('.')[^1].Replace('_', ' ') is { Length: > 0 } s ? s : "?";

    /// Finished drop (3 s no more impact, or force) as one call with the best value.
    List<Call> Flush(Me me, double now, bool force = false)
    {
        if (vol.Count == 0 || (!force && now - volAt < 3)) return new();
        var n = vol.Count;
        var hits = vol.Where(v => v.D < 5000).ToList();
        vol.Clear();
        if (volCold) return new();
        if (hits.Count == 0) return new() { R($"{Cs(me)}, no score.") };
        var b = hits.MinBy(v => v.D);
        var k = Kind(volW);
        return new() { R($"{Cs(me)}, {(k == "" ? "" : k + (n > 1 ? "s, " : ", "))}{(n > 1 && b.D >= 1.5 ? "best " : "")}{Score(b.D, b.Clock)}.") };
    }

    public void OnGunHit() { if (isHot) gunHits++; }

    /// Kill in the range: only an object of the opposing side, only with clearance (cold = no scoring), "target destroyed" once per pass (A72/R58).
    public List<Call> OnKill(Me me, double now, int target = -1)
    {
        if (!Cleared(now) || killTold || target >= 0 && target != 3 - me.Coalition) return new();   // R369: only the opposing side counts (target: coalition of the destroyed object, -1 = older mission)
        killTold = true;
        return new() { R($"{Cs(me)}, good hits, target destroyed.") };
    }

    // ======================================================================= AWACS
    string awName = "Overlord";
    double awFreq;
    Call A(string s) { last = s; return new("AWACS", awName, s, awFreq); }
    string? last;                                        // LD14: last call to this player ("say again")
    sealed record Ask(double At, int[] Ids, string Text, bool Asked);
    Ask? ask;                                            // LD14: commit to him, still without reply (follow-up after 10 s, after 25 s to the next flight)
    int silent; bool noComm;                             // LD14: unanswered taskings; from 2 "no radio" (no longer planned until he speaks)
    double dirAt = -1e9;                                 // LD14: last item was a tasking to him (acknowledgment "Dagger one." gets no reply)
    Call ToAll(string s) => new("AWACS", awName, s, awFreq, All: true);   // to everyone on the AWACS frequency (Gid 0), without callsign

    /// Own AWACS (the nearest) with call name and frequency from the mission ("Magic11" -> Magic), otherwise Overlord / config.
    internal static Traffic? AwacsOf(IReadOnlyList<Traffic> air, Me me) =>
        air.Where(a => a.InAir && a.Coalition == me.Coalition && Regex.IsMatch(a.Type, "E-3|E-2|A-50|KJ-2000"))
           .MinBy(a => me.Tel == null ? 0 : Dist(me.Tel.X, me.Tel.Z, a.X, a.Z));
    internal static string AwacsName(Traffic? a, Me me) => a == null && Gci.Contains(me.Coalition) ? (me.Coalition == 1 ? "Moscow" : "Magic") :   // GCI (V19)
        a != null && CsWord(a) is { } s ? s : a != null && Callsigns.TryGetValue(a.Group, out var cs) && Regex.IsMatch(cs, @"^\d+$") ? Digits(cs) : "Overlord";   // plain number: "two four seven" (A117)
    /// Letter call name of the group's DCS callsign ("Texaco11" / "Texaco 1-1" -> Texaco), null = none.
    static string? CsWord(Traffic a) => Callsigns.TryGetValue(a.Group, out var cs) && Regex.Match(cs, "^[A-Za-z]{3,}").Value is { Length: > 0 } s ? Cap(s.ToLowerInvariant()) : null;
    internal static double AwacsFreqOf(Traffic? a) => a == null ? 0 : AwacsFreq.TryGetValue(a.Group, out var f) ? f
        : AwacsFreq.GetValueOrDefault(Regex.Replace(a.Group, @"\s*#\d+$", ""));   // spawned later (MOOSE "Name#001", MIST "Name #1"): frequency of the template
    /// Frequency of the own AWACS (radio wheel display, Program), 0 = config.
    public static double AwacsFreqFor(IReadOnlyList<Traffic> air, Me me) => AwacsFreqOf(AwacsOf(air, me));
    /// Handover after departure: "Overlord two five one decimal zero" (own AWACS or GCI), null = none in the air.
    public static string? AwacsContact(IReadOnlyList<Traffic> air, Me me, double cfgFreq) =>
        AwacsUp(air, me) && AwacsOf(air, me) is var a ? $"{AwacsName(a, me)} {Tower.FreqSay(AwacsFreqOf(a) is > 0 and var f ? f : cfgFreq)}" : null;
    void SetAwacs(IReadOnlyList<Traffic> air, Me me) { var a = AwacsOf(air, me); awName = AwacsName(a, me); awFreq = AwacsFreqOf(a); side = me.Coalition; flight = Flight(me); }

    // LK3: group names per side, not per player: all players and AI flights of a side hear the same AWACS and talk about the same group
    static readonly Dictionary<int, Dictionary<int, string>> labels = new();   // side -> enemy unit -> "north group"
    static readonly Dictionary<int, List<Grp>> scope = new();                  // side -> most recently detected groups (hostiles)
    static Dictionary<int, string> Named(int s) => labels.TryGetValue(s, out var d) ? d : labels[s] = new();
    /// LK3: name of the group containing the enemy unit unitId (Traffic.Id = hash of the unit name) at the side's AWACS side ("north group", "single group"), null = not in the side's radar picture.
    public static string? GroupLabel(int side, int unitId) =>
        scope.GetValueOrDefault(side)?.FirstOrDefault(g => g.Ids!.Contains(unitId)) is { } g ? Label(side, g) : Named(side).GetValueOrDefault(unitId);
    /// LK9: enemy groups in the side's radar picture for target assignment (Abm) with assigned name (null = none yet; Abm names only at assignment).
    internal static List<Abm.Group> AbmGroups(int side) =>
        (scope.GetValueOrDefault(side) ?? new()).Select(g => new Abm.Group(g.Ids!, g.Ids!.Select(id => Named(side).GetValueOrDefault(id)).FirstOrDefault(s => s != null), g.Lead.X, g.Lead.Z, g.Lead.Hdg, g.Count)).ToList();
    /// Group name: assigned (picture, split, named earlier) stays; alone in the picture "single group" (not stored); otherwise named once by its position relative to the other
    /// groups ("north group", an already assigned name not twice) and kept until the picture changes. null = all names assigned.
    static string? Label(int side, Grp g)
    {
        var d = Named(side);
        if (g.Ids!.Select(id => d.GetValueOrDefault(id)).FirstOrDefault(s => s != null) is { } s) { foreach (var id in g.Ids!) d[id] = s; return s; }   // Latecomers get the name too
        var others = (scope.GetValueOrDefault(side) ?? new()).Where(o => !o.Ids!.Intersect(g.Ids!).Any()).ToList();
        if (others.Count == 0) return "single group";
        var all = others.Append(g).ToList();
        var taken = others.SelectMany(o => o.Ids!).Select(id => d.GetValueOrDefault(id)).ToHashSet();
        double brg = Bearing(all.Average(o => o.Lead.X), all.Average(o => o.Lead.Z), g.Lead.X, g.Lead.Z);
        var name = new[] { 0, 45, -45, 90, -90, 135, -135, 180 }.Select(o => Card(brg + o) + " group").FirstOrDefault(n => !taken.Contains(n));
        if (name != null) foreach (var id in g.Ids!) d[id] = name;
        return name;
    }

    /// Group; identification hostile, bogey (detected, type unknown), neutral (coalition 0, only with declare).
    record Grp(string Name, Traffic Lead, int Count, bool Known = true, int[]? Ids = null, bool Hostile = true)
    {
        public string Id => Lead.Coalition == 0 ? "neutral" : Hostile ? "hostile" : "bogey";   // LK1: bogey until the ID criteria are met
        /// Identification and fill-ins individually: identification, count, speed, altitude band, type (null = none)
        public string?[] Parts => new[] { Id, Count == 1 ? "single" : Count == 2 ? "two contacts" : $"heavy, {Count} contacts",
            Lead.Speed / Kt > 900 ? "very fast" : Lead.Speed / Kt >= 600 ? "fast" : null,
            Lead.AltMsl / Ft > 40000 ? "high" : Lead.AltMsl / Ft - Math.Max(0, Airfield.Map.TerrainFt(Lead.X, Lead.Z, 0.5)) < 1000 ? "low" : null,   // low: map grid (V15), without: MSL
            Known ? Flights.Nato(Lead.Type) ?? TypeSay(Lead.Type) : null };
    }

    /// LK2 (ATP 1-02.1 fill-ins, radio discipline): identification and fill-ins this player has already heard for the group. Follow-up reports name only what changed
    /// ("single" after a kill, "hostile" after the declaration, a new type); full: everything (picture, requests like bogey dope/declare/snap).
    readonly Dictionary<int, string?[]> heard = new();
    string Fill(Grp g, bool full = false)
    {
        var now = g.Parts;
        var was = full ? null : g.Ids!.Select(id => heard.GetValueOrDefault(id)).FirstOrDefault(p => p != null);
        foreach (var id in g.Ids!) heard[id] = now;
        return string.Join(", ", now.Where((s, i) => s != null && (was == null || s != was[i])));
    }
    /// "…, BRAA …" plus fill-ins, no trailing comma if none
    static string With(string s, string fill) => fill == "" ? s : $"{s}, {fill}";

    /// Radar picture per side (V9, mission): detected unit -> type known. Side missing = no data (old mission, test) -> everything visible.
    public static Dictionary<int, Dictionary<int, bool>> Detected = new();
    /// Sides with ground radar (EWR, mission): without AWACS GCI takes over (V19), range is limited by DCS through detection.
    public static HashSet<int> Gci = new();
    /// LK1 (ATP 1-02.1 BOGEY/HOSTILE): new contacts are bogey; hostile when the type is recognized and the track is followed that long (ID criteria), or immediately after a hostile act.
    public static double IdSec = 40;
    static readonly Dictionary<int, Dictionary<int, double>> firstSeen = new();   // side -> enemy unit -> first detected
    static readonly Dictionary<int, HashSet<int>> hostileAct = new();             // side -> units that have fired at this side

    static readonly Dictionary<int, double> killed = new();                       // LK6/LK8: shot-down unit -> time (not "faded", but splash)

    /// Mission event (Program, X lines like Flights.OnEvent): X;shot;unit;group;side;weapon;…;target;… – a shot at the other side is a hostile act (LK1);
    /// X;kill;shooter;group;victim;… – the victim is shot down, not vanished (LK6/LK8).
    public static void OnEvent(string[] e, double now)
    {
        if (e.Length >= 10 && e[1] == "shot" && e[9] != "" && int.TryParse(e[4], out var s) && s is 1 or 2)
        {
            if (!hostileAct.TryGetValue(3 - s, out var h)) hostileAct[3 - s] = h = new();
            h.Add(e[2].GetHashCode());   // Traffic.Id = hash of the unit name
        }
        if (e.Length >= 5 && e[1] == "kill")
        {
            int v = e[4].GetHashCode();
            killed[v] = now;
            // LK8: an AI fighter shoots down the last enemy in the radar picture of his side -> Flights says "Overlord copies, splash, picture clean"; this applies to the side (no second clean from Ops)
            if (Flights.Mode != "aus" && Flights.Units.TryGetValue(e[2], out var k))
                if (scope.GetValueOrDefault(k.Side) is { } gs && gs.Any(g => g.Ids!.Contains(v)) && gs.All(g => g.Ids!.All(killed.ContainsKey))) cleanAt[k.Side] = now;
        }
    }
    static readonly Dictionary<int, double> cleanAt = new();   // LK8: side -> last time "picture clean" was said (player splash, AI splash)

    /// Enemy (with neutral also neutral) groups detected by own sensors, up to 250 NM from the player.
    /// Group = contacts ≤ 3 NM together (V10, per coalition); name = smallest unit id, stays the same in flight.
    static List<Grp> Hostiles(IReadOnlyList<Traffic> air, Me me, double now, bool neutral = false)
    {
        var det = Detected.GetValueOrDefault(me.Coalition);
        if (!firstSeen.TryGetValue(me.Coalition, out var fs)) firstSeen[me.Coalition] = fs = new();
        var act = hostileAct.GetValueOrDefault(me.Coalition);
        var left = air.Where(a => a.InAir && (a.Coalition != 0 || neutral) && a.Coalition != me.Coalition && (det == null || det.ContainsKey(a.Id)) &&
                                  (me.Tel == null || Dist(me.Tel.X, me.Tel.Z, a.X, a.Z) < RadarNm * NM)).OrderBy(a => a.Id).ToList();
        var res = new List<Grp>();
        while (left.Count > 0)   // ponytail: single-linkage O(n²), enough for DCS volumes
        {
            var g = new List<Traffic> { left[0] };
            left.RemoveAt(0);
            for (int i = 0; i < g.Count; i++)
            {
                var near = left.Where(a => a.Coalition == g[i].Coalition && Dist(g[i].X, g[i].Z, a.X, a.Z) <= 3 * NM).ToList();
                g.AddRange(near); left.RemoveAll(near.Contains);
            }
            foreach (var a in g) fs.TryAdd(a.Id, now);
            bool known = det == null || g.Any(a => det[a.Id]);
            bool hostile = act != null && g.Any(a => act.Contains(a.Id)) || known && (IdSec <= 0 || g.Any(a => now - fs[a.Id] >= IdSec));   // LK1: type recognized and ID time over, or hostile act
            res.Add(new Grp(g[0].Id.ToString(), g[0], g.Count, known, g.Select(a => a.Id).ToArray(), hostile));
        }
        if (!neutral) scope[me.Coalition] = res;   // LK3
        return res;
    }

    /// Spoken name of Western types (A118); everything else: type with a space before the number ("MiG 21Bis").
    static readonly (string Re, string Name)[] TypeNames =
    {
        (@"^F/?A-18", "Hornet"), (@"^F-16", "Viper"), (@"^F-15E", "Strike Eagle"), (@"^F-15", "Eagle"), (@"^F-14", "Tomcat"), (@"^A-10", "Warthog"),
        (@"^F-5", "Tiger"), (@"^AV8B", "Harrier"), (@"^F-4E", "Phantom"), (@"^AH-64", "Apache"), (@"^UH-1H", "Huey"),
        // J12: ground targets as a JTAC says them
        (@"^(?:T-\d\d|M-1 |M-60|Leopard|Challenger|Merkava)", "tanks"), (@"^(?:BTR|BRDM|M-113|M1126|LAV|Stryker|MTLB|AAV)", "APCs"), (@"^(?:BMP|M-2 Bradley|Marder|Warrior|BMD)", "IFVs"),
        (@"^ZSU-23", "Shilka"), (@"^2S6", "Tunguska"), (@"^Strela", "Strela"), (@"^Osa", "SA-8"), (@"^2S1", "Gvozdika"), (@"^(?:SA-18|Igla|Stinger)", "MANPADS"),
        (@"^(?:Ural|KAMAZ|ZIL|GAZ|M 818|M-818)", "trucks"), (@"^(?:Hummer|HMMWV)", "vehicles"), (@"^(?:Soldier|Infantry|Paratrooper)", "infantry"),
    };
    internal static string TypeSay(string type) => TypeNames.FirstOrDefault(n => Regex.IsMatch(type, n.Re)).Name
        ?? Regex.Replace(type.Split('_')[0], @"(?<=[A-Za-z])-?(?=\d)", " ");
    internal static string Card(double trueDeg) =>
        new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" }[(int)Math.Round(((trueDeg % 360) + 360) % 360 / 45) % 8];

    /// "BRAA 045, 30, 15 thousand, flank north" from the player (brevity: hot / flank / beam / drag + flight direction).
    /// LD4: bring the BRAA in the call to the state at transmit time (the call waited in the queue).
    public static string Rebraa(string text, Telemetry t, Traffic a) => BraaRx.Replace(text, Braa(t, a), 1);
    static readonly Regex BraaRx = new(@"BRAA [a-z ]+, \d+, [^,]+, (?:hot|(?:flank|beam|drag) \w+)");
    static string Braa(Telemetry t, Traffic a)
    {
        double brg = Bearing(t.X, t.Z, a.X, a.Z), back = Bearing(a.X, a.Z, t.X, t.Z), hdg = a.Hdg * 180 / Math.PI;
        double off = HdgDiff(hdg, back);
        var aspect = off <= 30 ? "hot" : off <= 70 ? $"flank {Card(hdg)}" : off <= 120 ? $"beam {Card(hdg)}" : $"drag {Card(hdg)}";
        return $"BRAA {Brg(brg)}, {Miles(Dist(t.X, t.Z, a.X, a.Z))}, {Thousand(a.AltMsl)}, {aspect}";
    }

    /// LK4 (ATP 1-02.1: PICTURE in bullseye format; BRAA for one flight): picture and situation updates with bullseye, if the mission has one and AwacsBullseye is on
    /// (default); null = all BRAA from the player (fallback). Threat, bogey dope, snap, spike and commit stay BRAA to the one flight.
    (double X, double Z)? Bull() => AwacsBullseye && (Bulls.TryGetValue(side, out var b) || Bulls.TryGetValue(0, out b)) ? b : null;
    /// Location for picture/updates: "bullseye 030, 45, 20 thousand, track south" (same for all), without bullseye BRAA from the player.
    string Loc(Grp g, Telemetry? t) => Bull() is { } b
        ? $"bullseye {Brg(Bearing(b.X, b.Z, g.Lead.X, g.Lead.Z))}, {Miles(Dist(b.X, b.Z, g.Lead.X, g.Lead.Z))}, {Thousand(g.Lead.AltMsl)}, track {Card(g.Lead.Hdg * 180 / Math.PI)}"
        : t != null ? Braa(t, g.Lead) : Thousand(g.Lead.AltMsl);
    string Describe(Grp g, Telemetry? t)
    {
        return With(Loc(g, t), Fill(g, true)) + Tgt(g);
    }

    static bool Hot(Traffic a, Telemetry t) => HdgDiff(a.Hdg * 180 / Math.PI, Bearing(a.X, a.Z, t.X, t.Z)) <= 70;   // hot or flank on the player
    static int Band(double d) => d < 5 * NM ? 5 : d < 10 * NM ? 10 : d < 20 * NM ? 20 : 30;

    /// A55: what a reply has named counts as reported (no "new group" afterward); with BRAA hot ≤ 30 NM also no Threat until the next step.
    void Told(IEnumerable<Grp> gs, Telemetry? t, double now, bool braa = false)
    {
        foreach (var g in gs)
        {
            groupsTold.UnionWith(g.Ids!);
            foreach (var id in g.Ids!) seenAt[id] = now;
            double d = t == null ? double.MaxValue : Dist(t.X, t.Z, g.Lead.X, g.Lead.Z);
            if (d < NewGroupNm * NM && g.Lead.Coalition != 0) { hadContacts = true; lastTold = now; }   // declare on neutrals: no "picture clean" afterward
            if (braa && d < 30 * NM && Hot(g.Lead, t!)) { committed.UnionWith(g.Ids!); foreach (var id in g.Ids!) threatTold[id] = (Band(d), now); }
        }
    }

    List<Call> AwacsCall(string n, Me me, IReadOnlyList<Traffic> air, IReadOnlyList<Airfield> fields, double now)
    {
        if (!AwacsUp(air, me)) return new();   // no own AWACS in the air: nobody answers
        var c = Cs(me);
        var t = me.Tel;
        silent = 0; noComm = false;   // LD14: he speaks (again)
        if (Has(n, "say again")) return last == null ? new() : new() { A(last) };   // LD14: the player asks again -> last call to him (no check-in)
        var (wasIn, wasRc) = (awacsIn, radarContact);   // "say again" is not a check-in (otherwise after a mishear while taxiing an unasked "radar contact, picture" after takeoff)
        awacsIn = true;
        if (t != null && !OnGround(t)) radarContact = true;  // R50: any reply in the air (also low after takeoff) is the radar contact, otherwise "radar contact, picture" still comes while climbing
        SetAwacs(air, me);
        var groups = Hostiles(air, me, now);
        if (t != null) groups = groups.OrderBy(g => Dist(t.X, t.Z, g.Lead.X, g.Lead.Z)).ToList();
        if (Has(n, "mayday", "pan pan", "declaring emergency", "emergency fuel") && !Has(n, "cancel"))   // A15: emergency -> nearest suitable own airfield, handover to its Approach (ICAO Doc 4444 ch. 15)
        {
            var sig = Has(n, "mayday") ? "roger mayday" : Has(n, "pan pan") ? "roger pan pan" : "roger";   // R242: acknowledge "declaring emergency" without a signal word
            if ((Divert = t == null ? null : Airfield.Emergency(fields, t.X, t.Z, me.Coalition)) is not { } ef) return new() { A($"{c}, {awName}, {sig}, say intentions.") };
            // R250: with "contact" the GCI's service ends, no more "say intentions" (he tells his intention to Approach)
            // Review R250: terse like GCI ("Kobuleti bears 245, 38, contact …"), without "nearest friendly field … miles"
            return new() { A($"{c}, {awName}, {sig}, {ef.Name.Replace('-', ' ')} bears {Brg(Bearing(t!.X, t.Z, ef.X, ef.Z))}, {Miles(Dist(t.X, t.Z, ef.X, ef.Z))}, " +
                             $"contact {ef.StationOf("Approach")} {FreqSay(ef.FreqsOf("Approach") is { } af ? af.FirstOrDefault(x => x >= 200, af[0]) : Freqs.GetValueOrDefault("Approach", 266.5))}.") };
        }
        if (Has(n, "check out", "checking out"))
        {
            awacsIn = onStation = false; Abm.Release(side, Flight(me));   // no more calls by itself; LK9: no longer planned
            return new() { A($"{c}, {awName}, copy checking out, good day.") };
        }
        if (t != null && OnGround(t)) { awacsIn = wasIn; return new() { A($"{c}, {awName}, check in airborne.") }; }   // R51: no radar contact on the ground (parked deletes the check-in via TickLive anyway)
        if (Has(n, "unable"))   // LD14 (ATP 1-02.1 UNABLE): tasking declined -> Abm gives the group to the next flight; winchester/bingo: no longer planned
        {
            ask = null; Abm.Unable(side, Flight(me), now);
            if (Has(n, "winchester", "bingo", "rtb", "fuel")) { onStation = false; Abm.Release(side, Flight(me)); }
            return new() { A($"{c}, {awName}, copy.") };
        }
        // LD16: defensive -> the own group gets a second flight, no reply (the defense has the frequency); LD15: request reinforcement
        if (Has(n, "defensive", "defending", "missile") && groups.FirstOrDefault(Own) is { } dg) { Abm.Assist(side, dg.Ids!); return new(); }
        if (Has(n, "support", "assist") && (groups.FirstOrDefault(g => Said(g, groups, n)) ?? groups.FirstOrDefault(Own) ?? groups.FirstOrDefault()) is { } sg2)
        {
            Abm.Assist(side, sg2.Ids!);
            return new() { A($"{c}, {awName}, copy, {Nm(sg2)}.") };
        }
        if (Has(n, "on station")) onStation = true;   // LK5: CAP tasking (DCA): only then does the AWACS give "commit"
        if (Has(n, "rtb", "off station", "bingo", "winchester")) { onStation = false; Abm.Release(side, Flight(me)); }   // LD16: winchester releases
        // declare: spoken position before the locked target (tighter: 3 NM, ±5000 ft); R371: an aircraft there that the AWACS does not detect -> clean, with or without altitude; detected, unidentified -> bogey (Fill)
        if (Has(n, "declare") && t != null && (DeclarePos(n, t, me.Coalition) ?? LockPos(t)) is { } q)
        {
            var near = air.Where(a => a.InAir && Dist(q.X, q.Z, a.X, a.Z) < (q.Alt == null ? 5 : 3) * NM && (q.Alt is not { } h || Math.Abs(a.AltMsl - h) < 5000 * Ft))
                          .OrderBy(a => Dist(q.X, q.Z, a.X, a.Z)).ToList();
            var gs = Hostiles(air, me, now, true);
            var hit = near.Select(a => gs.FirstOrDefault(g => g.Ids!.Contains(a.Id))).FirstOrDefault(g => g != null);
            bool fr = near.Any(a => a.Coalition == me.Coalition);
            if (hit != null) Told(new[] { hit }, t, now);
            return new() { A($"{c}, {awName}, {q.Say}, {(hit == null ? fr ? "friendly" : "clean" : fr ? "furball" : Fill(hit, true))}.") };
        }
        if (Has(n, "spike") && t != null)   // V20: "spiked 270" -> known group ±30° in that direction
        {
            var sm = Regex.Match(n, @"\bspiked? ((?:\d ?){3})");
            if (!sm.Success) return new() { A($"{c}, {awName}, say spike bearing.") };
            var sb = sm.Groups[1].Value.Replace(" ", "");
            var sg = groups.FirstOrDefault(g => HdgDiff(Bearing(t.X, t.Z, g.Lead.X, g.Lead.Z), int.Parse(sb) + MagVar) <= 30);
            if (sg != null) Told(new[] { sg }, t, now, true);
            // LD12 BUDDY SPIKE (ATP 1-02.1): no enemy group there, but an own fighter -> its location
            var bud = sg != null ? null : air.Where(a => a.InAir && a.Coalition == me.Coalition && !a.Heli && !Support.IsMatch(a.Type) && Dist(t.X, t.Z, a.X, a.Z) is > 0.1 * NM and < 40 * NM &&
                HdgDiff(Bearing(t.X, t.Z, a.X, a.Z), int.Parse(sb) + MagVar) <= 30).MinBy(a => Dist(t.X, t.Z, a.X, a.Z));
            return new() { A($"{c}, {awName}, spike {Digits(sb)}, {(bud != null ? $"buddy spike, {Braa(t, bud)}" : sg == null ? "nothing known" : With($"{Nm(sg)} {Braa(t, sg.Lead)}", Fill(sg, true)))}.") };
        }
        if ((Has(n, "sort", "request target") || Has(n, "targeting") && !Regex.IsMatch(n, @"\b(?:north|south|east|west|northeast|northwest|southeast|southwest|lead|trail|middle|single)\b")) && t != null)   // V20/R55: one group per element (section 1-1, 1-3 …), from left to right; callsign before the sender
        {
            if (groups.Count == 0) return new() { A($"{c}, {awName}, clean.") };
            var cs = Regex.Match(me.Callsign, @"^(.*?)(\d)$");
            var gs = groups.Take(cs.Success ? Math.Max(1, (me.Flight + 1) / 2) : 1).OrderBy(g => (Bearing(t.X, t.Z, g.Lead.X, g.Lead.Z) - t.Hdg * 180 / Math.PI + 540) % 360).ToList();
            Told(gs, t, now, true); committed.UnionWith(gs.SelectMany(g => g.Ids!));   // assigned: no "commit" afterward
            return new() { A(string.Join(" ", gs.Select((g, i) => (gs.Count == 1 ? $"{c}, {awName}" : SpokenCallsign(cs.Groups[1].Value + (1 + 2 * i)) + (i == 0 ? $", {awName}" : "")) +
                With($", target {Nm(g)} {Braa(t, g.Lead)}", Fill(g)) + "."))) };
        }
        if (Has(n, "snap") && t != null)   // R238: SNAP = heading to the named group (name from the picture), otherwise to the nearest (ATP 1-02.1)
        {
            if ((groups.FirstOrDefault(g => Said(g, groups, n)) ?? groups.FirstOrDefault()) is not { } g) return new() { A($"{c}, {awName}, clean.") };
            Told(new[] { g }, t, now, true);
            return new() { A($"{c}, {awName}, snap {Brg(Bearing(t.X, t.Z, g.Lead.X, g.Lead.Z))}, {With($"{Nm(g)} {Braa(t, g.Lead)}", Fill(g, true))}{Tgt(g)}.") };
        }
        if (Has(n, "bogey dope", "declare", "braa", "threat", "nearest bandit", "nearest group"))
        {
            if (Has(n, "declare") && t != null) groups = Hostiles(air, me, now, true).OrderBy(g => Dist(t.X, t.Z, g.Lead.X, g.Lead.Z)).ToList();   // declare: also neutrals
            if (groups.Count == 0 || t == null) return new() { A($"{c}, {awName}, clean.") };
            var g = groups[0];
            Told(new[] { g }, t, now, true);
            return new() { A($"{c}, {awName}, {With($"{Nm(g)} {Braa(t, g.Lead)}", Fill(g, true))}{Tgt(g)}.") };
        }
        bool pic = Has(n, "picture");
        if (pic || Has(n, "check in", "checking in", "with you", "on station"))   // P3-AW2: check-in before tanker and vector ("checking in, fuel 5.2")
            return new() { A($"{c}, {awName}, {PictureCall(groups, t, me, now, !pic)}.") };
        if (Has(n, "tanker", "texaco", "shell", "arco"))   // R13: before the vector (radio wheel "vector to tanker"), without "fuel" ("bingo fuel" should go home)
        {
            var tk = Tankers(air, me).FirstOrDefault();
            if (tk == null || t == null) return new() { A($"{c}, {awName}, {(tk == null && Tankers(air, me, true).Count > 0 ? "negative, no compatible tanker" : "no tanker")} airborne.") };   // A125: tanker there, but none suitable
            var anchor = Tracks.TryGetValue(tk.A.Group, out var trk) ? $", anchor {trk}" : "";
            var contact = $", contact {tk.Name}, {FreqSay(Freqs.GetValueOrDefault("Tanker", 255.5))}";   // R304: our tanker also for DCS tankers of the mission
            return new() { A($"{c}, {awName}, nearest tanker {tk.Name}{anchor}, bearing {Brg(Bearing(t.X, t.Z, tk.A.X, tk.A.Z))}, " +
                             $"{MilesTxt(Dist(t.X, t.Z, tk.A.X, tk.A.Z))}, {Angels(tk.A.AltMsl)}{TacanSay(tk.A)}{contact}.") };
        }
        // Bingo/alternate airfield; only own/neutral airfields
        if (Has(n, "vector", "home plate", "bingo", "nearest airfield", "recovery", "divert") && t != null &&
            fields.Where(f => f.Side == 0 || f.Side == me.Coalition).ToList() is { Count: > 0 } own)
        {
            var nn = n;
            var f = own.FirstOrDefault(f => f.Name.ToLowerInvariant().Split('-', ' ').Any(w => w.Length >= 4 && nn.Contains(w)))
                    ?? own.MinBy(f => Dist(t.X, t.Z, f.X, f.Z))!;
            return new() { A($"{c}, {awName}, {f.Name.Replace('-', ' ')} bears {Brg(Bearing(t.X, t.Z, f.X, f.Z))}, {MilesTxt(Dist(t.X, t.Z, f.X, f.Z))}.") };
        }
        // A14: acknowledge combat reports (ATP 1-02.1) instead of check-in with full picture
        if (Has(n, "splash", "kill"))
        {
            var k = Regex.Match(n, @"\b(?:splash|kill) (\d+)\b");
            // LK8: which group (last shot-down unit with name) and what remains ("South group remains." / "picture clean"); shot-down ones no longer count
            var rest = groups.Where(g => !g.Ids!.All(killed.ContainsKey)).ToList();
            var kg = killed.Where(x => now - x.Value < 30).OrderByDescending(x => x.Value).Select(x => named.GetValueOrDefault(x.Key)).FirstOrDefault(s => s != null);
            bool clean = rest.Count == 0;
            if (clean) { Clean(); cleanAt[side] = now; }   // just said: no "picture clean" by itself afterward, not for the others of the side either
            var rn = rest.Select(Stored).ToList();   // only assigned names (the acknowledgment names nothing anew)
            var left = clean ? ", picture clean." : rest.Count == 1 ? $". {Cap(rn[0] ?? "single group")} remains." : rn.Count == 2 && rn.All(s => s != null) ? $". {Cap(rn[0]!)} and {rn[1]} remain." : $". {rest.Count} groups remain.";
            return new() { A($"{c}, {awName} copies splash{(k.Success ? " " + Digits(k.Groups[1].Value) : "")}{(kg != null ? ", " + kg : "")}{left}") };
        }
        if (Has(n, "merged") && (groups.FirstOrDefault(Own) ?? groups.FirstOrDefault()) is { } mg) { foreach (var id in mg.Ids!) mergeTold[id] = now; return new(); }   // LD3: if he said it himself, no "merged" from the AWACS
        if (tank == 1 && Has(n, "judy")) { vecEnd = true; return new() { A($"{c}, {awName}, roger.") }; }   // E1: radar contact with the tanker ends the AWACS join vectors
        if (Has(n, "commit", "targeting", "targeted", "engaged", "engaging", "fox", "judy") && t != null)   // Confirm target: named group/direction, otherwise the assigned one, otherwise the nearest
        {
            var dir = Regex.Match(n, @"\b(?:north|south|east|west)\b").Value;
            var gn = groups.FirstOrDefault(g => Said(g, groups, n));   // R54: name from the picture ("committing lead group")
            var gd = gn ?? (dir == "" ? null : groups.FirstOrDefault(g => Card(Bearing(t.X, t.Z, g.Lead.X, g.Lead.Z)).Contains(dir)));
            if ((gd ?? groups.FirstOrDefault(Own) ?? groups.FirstOrDefault()) is not { } g) return Has(n, "fox") ? new() : new() { A($"{c}, {awName}, clean.") };
            if (gn == null && gd != null && Stored(gd) == null && !named.ContainsValue(dir + " group")) foreach (var id in gd.Ids!) named[id] = dir + " group";   // LK3: the player names a nameless group first -> the name applies
            var pend = ask; ask = null;
            if (!Has(n, "commit")) judy.UnionWith(g.Ids!);   // LD3 (ATP 1-02.1 JUDY): he leads himself, the AWACS keeps the frequency clear
            if (Has(n, "fox"))   // LD16: shot -> the group belongs to him (AI flights on it get skip it), no reply
            {
                Abm.Claim(side, Flight(me), g.Ids!, Nm(g), g.Count, now); committed.UnionWith(g.Ids!); mine.UnionWith(g.Ids!);
                return new();
            }
            // LK7/LK9 (ATP 1-02.1 SKIP IT): if another flight already has the group, the AWACS declines ("skip it, north group Ford one targeted")
            if (Abm.Want(side, Flight(me), g.Ids!, Nm(g), g.Count, now, post: false) is { } by) return new() { A($"{c}, {awName}, skip it, {Nm(g)} {by} targeted.") };
            Told(new[] { g }, t, now, true); committed.UnionWith(g.Ids!);   // no "commit"/Threat afterward
            mine.UnionWith(g.Ids!);   // LK5/LK9: committed or targeted -> no more Threat on it, only target updates (ATP 1-02.1 THREAT: untargeted)
            if (Has(n, "judy") || pend != null && pend.Ids.Intersect(g.Ids!).Any() && now - pend.At < 15) return new();   // LD3: no echo – the location just came with the commit; judy: radio silence
            return new() { A($"{c}, {awName}, {With($"{Nm(g)} {Braa(t, g.Lead)}", Fill(g))}.") };
        }
        // LD14: acknowledgment of a tasking ("Dagger one.", "copy", "resetting") gets no reply
        if (now - dirAt < 30 && !n.Contains(awName.ToLowerInvariant()) && (Has(n, "resetting", "copy", "wilco", "roger") || n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3))
        {
            ask = null;
            return new();
        }
        if (Has(n, "winchester", "rtb", "tally", "commit", "targeted", "engaged", "engaging")) return new() { A($"{c}, {awName} copies.") };
        // callsign only: not checked in -> check-in, otherwise callback without picture
        if (Has(n, "overlord", "awacs", "magic", "moscow", "darkstar", "wizard", "focus") || n.Contains(awName.ToLowerInvariant()))
            return new() { A(wasIn ? $"{c}, {awName}, go ahead." : $"{c}, {awName}, {PictureCall(groups, t, me, now, true)}.") };
        (awacsIn, radarContact) = (wasIn, wasRc);   // "say again" is not a check-in
        return new() { A($"{c}, {awName}, say again.") };
    }

    /// picture clean (by itself or after "splash"): forget what was reported, every group is new again afterward (AW-P3).
    void Clean() { hadContacts = false; groupsTold.Clear(); committed.Clear(); judy.Clear(); threatTold.Clear(); members.Clear(); named.Clear(); heard.Clear(); mine.Clear(); lastPos.Clear(); furTold.Clear(); leakTold.Clear(); }

    /// R54/LK3: side's group name ("north group", "single group"), "group" only when all names are assigned.
    string Nm(Grp g) { var s = g.Lead.Coalition != 0 ? Label(side, g) : null; if (s == "single group") single.UnionWith(g.Ids!); return s ?? "group"; }   // Neutrals (declare) get no name
    /// LK9: player's flight name for target assignment ("Enfield 1-1" -> "Enfield one"; player and AI in the same flight share it).
    static string Flight(Me me) => Regex.Match(me.Callsign, @"^([A-Za-z]+)\s*(\d)") is { Success: true } m ? SpokenCallsign($"{m.Groups[1].Value} {m.Groups[2].Value}") : Cs(me);
    string flight = "";   // Flight(me) of the last call (SetAwacs)
    readonly List<Abm.Event> abmQ = new();   // LK9: picked-up assignments, one per tick
    readonly Dictionary<int, double> furTold = new();   // LK8: unit -> furball called
    readonly HashSet<int> leakTold = new();              // LK8: reported as leaker (once)
    double lastTold = -1e9;                              // LK8: last reported something about a group to me (Tell/Upd/Told)
    internal static readonly Regex Support = new("E-3|E-2|A-50|KJ-2000|KC|Tanker|IL-78|C-130|C-17|An-26|An-30|Il-76|Yak-40", RegexOptions.IgnoreCase);   // LK8: AWACS, tanker, transport do not fight (no furball)
    /// LK7 (Deconfliction): ", Ford one targeted" if another flight of the side has the group (Abm table, also AI flights), otherwise "".
    string Tgt(Grp g) => g.Ids!.Select(id => Abm.TargetedBy(side, id, flight)).FirstOrDefault(w => w != null) is { } w ? $", {w} targeted" : "";
    /// LK9: this flight's group (committed/targeted or assigned by the AWACS): no THREAT on it (ATP 1-02.1 THREAT: untargeted).
    bool Own(Grp g) => g.Ids!.Any(id => mine.Contains(id) || Abm.Mine(side, flight, id));
    /// Assigned name, without renaming (merged, split/combined: what was it called before), null = none.
    string? Stored(Grp g) => g.Ids!.Select(id => named.GetValueOrDefault(id)).FirstOrDefault(s => s != null);
    /// Has the player named this group by name ("snap trail group", "targeting single group")? Only assigned names, without renaming.
    bool Said(Grp g, List<Grp> all, string n) => (Stored(g) ?? (all.Count == 1 ? "single group" : null)) is { } s && Regex.IsMatch(n, $@"\b{s}\b");

    /// Picture/check-in (groups by range, names the 3 nearest): AW-P3 a hot group ≤ 30 NM first as Threat (counts as reported, no Threat afterward), then the picture.
    string PictureCall(List<Grp> groups, Telemetry? t, Me me, double now, bool contact)
    {
        Told(groups.Take(3), t, now);
        var pic = (contact ? "radar contact, " : "") + Picture(groups, t, me.Coalition);
        if (t == null || groups.FirstOrDefault(g => Dist(t.X, t.Z, g.Lead.X, g.Lead.Z) < 30 * NM && Hot(g.Lead, t) && !Own(g) && Tgt(g) == "") is not { } thr) return pic;
        Told(new[] { thr }, t, now, true);
        return $"{With($"threat, {Nm(thr)} {Braa(t, thr.Lead)}", Fill(thr, true))}. {Cap(pic)}";
    }

    /// Declare location: "declare bullseye 030 45", "declare 0 3 0 for 4 5" (without bullseye: BRAA from the player); bearing magnetic.
    static (double X, double Z, string Say, double? Alt)? DeclarePos(string n, Telemetry t, int side)
    {
        var m = Regex.Match(n, @"\bdeclare\b((?: [a-z]+){0,3}?) (\d+(?: (?:for )?\d+)*)");
        if (!m.Success) return null;
        var tok = m.Groups[2].Value.Replace("for ", "").Split(' ');
        string brg, rng;
        if (tok[0].Length >= 3) { brg = tok[0][..3]; rng = tok[0].Length > 3 ? tok[0][3..] : tok.ElementAtOrDefault(1) ?? ""; }
        else
        {
            var d = string.Concat(tok);   // spoken individually: 3 digits bearing, rest range
            if (d.Length < 4) return null;
            (brg, rng) = (d[..3], d[3..Math.Min(d.Length, 6)]);
            if (rng.Length == 3 && int.Parse(rng) > 250) rng = rng[..2];
        }
        if (rng == "" || int.Parse(brg) > 360) return null;
        bool bulls = m.Groups[1].Value.Contains("bull");
        var (ox, oz) = bulls && (Bulls.TryGetValue(side, out var b) || Bulls.TryGetValue(0, out b)) ? (b.X, b.Z) : (t.X, t.Z);
        double r = (int.Parse(brg) + MagVar) * Math.PI / 180, dist = int.Parse(rng) * NM;
        return (ox + Math.Cos(r) * dist, oz + Math.Sin(r) * dist, $"{(bulls ? "bullseye" : "BRAA")} {Digits(brg)}, {int.Parse(rng)}", null);
    }

    /// Target locked in the cockpit (Export.lua, host/solo only): BRAA from the player with altitude.
    static (double X, double Z, string Say, double? Alt)? LockPos(Telemetry t) => t.Lock is { } k
        ? (k.X, k.Z, $"BRAA {Brg(Bearing(t.X, t.Z, k.X, k.Z))}, {Miles(Dist(t.X, t.Z, k.X, k.Z))}, {Thousand(k.AltMsl)}", k.AltMsl) : null;

    /// Picture (V10): 1 group single group; 2 groups azimuth/range; 3 groups wall/ladder/vic/champagne (seen from the player);
    /// more: count + the 3 nearest.
    string Picture(List<Grp> groups, Telemetry? t, int side)
    {
        if (groups.Count == 0) return "picture clean";
        var gs = groups.Take(3).ToList();
        var head = groups.Count == 1 ? "single group" : $"{groups.Count} groups";
        int[] ord = Enumerable.Range(0, gs.Count).ToArray();   // order of the announcement
        string[] names = gs.Select(_ => "").ToArray();
        if (t != null && groups.Count is 2 or 3)
        {
            var (al, cr, R, L) = Geo(gs, t);
            double deep = al.Max() - al.Min(), wide = cr.Max() - cr.Min();
            int[] byA = ord.OrderBy(i => al[i]).ToArray(), byC = ord.OrderByDescending(i => cr[i]).ToArray();
            if (gs.Count == 2 && wide >= deep) (head, ord, names) = ($"2 groups azimuth {Miles(wide)}", byC, new[] { R, L });
            else if (gs.Count == 2) (head, ord, names) = ($"2 groups range {Miles(deep)}", byA, new[] { "lead", "trail" });
            else if (wide >= 2 * deep) (head, ord, names) = ($"3 groups wall {Miles(wide)}", byC, new[] { R, "middle", L });
            else if (deep >= 2 * wide) (head, ord, names) = ($"3 groups ladder {Miles(deep)}", byA, new[] { "lead", "middle", "trail" });
            else if (al[byA[1]] - al[byA[0]] > al[byA[2]] - al[byA[1]])   // vic: one in front, two behind side by side
                (head, ord, names) = ($"3 groups vic, {Miles(wide)} wide, {Miles(deep)} deep",
                    new[] { byA[0] }.Concat(new[] { byA[1], byA[2] }.OrderByDescending(i => cr[i])).ToArray(), new[] { "lead", $"{R} trail", $"{L} trail" });
            else   // champagne: two in front side by side, one behind
                (head, ord, names) = ($"3 groups champagne, {Miles(wide)} wide, {Miles(deep)} deep",
                    new[] { byA[0], byA[1] }.OrderByDescending(i => cr[i]).Append(byA[2]).ToArray(), new[] { $"{R} lead", $"{L} lead", "trail" });
        }
        named.Clear();   // R54: the new picture assigns the names
        for (int k = 0; k < ord.Length; k++) if (names[k] != "") foreach (var id in gs[ord[k]].Ids!) named[id] = names[k] + " group";
        return $"picture, {head}. " + string.Join(". ", ord.Select((g, k) => Cap((names[k] == "" ? "" : names[k] + " group, ") + Describe(gs[g], t))));
    }

    /// Position of the groups from the player: along = range on the line of sight to the center, across = right positive; names of the right/left side.
    static (List<double> Al, List<double> Cr, string R, string L) Geo(List<Grp> gs, Telemetry t)
    {
        double brg = Bearing(t.X, t.Z, gs.Average(g => g.Lead.X), gs.Average(g => g.Lead.Z)), r = brg * Math.PI / 180;
        return (gs.Select(g => (g.Lead.X - t.X) * Math.Cos(r) + (g.Lead.Z - t.Z) * Math.Sin(r)).ToList(),
                gs.Select(g => (g.Lead.Z - t.Z) * Math.Cos(r) - (g.Lead.X - t.X) * Math.Sin(r)).ToList(), Card(brg + 90), Card(brg - 90));
    }

    /// R239: "north group split, 2 groups azimuth 5, east group BRAA …, west group BRAA …" – both parts renamed by their position to each other (as in the picture).
    /// ponytail: only the pair is named, not the whole picture; if another group already has the name, it loses it ("group" + BRAA stays unambiguous)
    string SplitCall(string old, Grp a, Grp b, Telemetry t)
    {
        var gs = new List<Grp> { a, b };
        var (al, cr, R, L) = Geo(gs, t);
        double deep = Math.Abs(al[0] - al[1]), wide = Math.Abs(cr[0] - cr[1]);
        bool az = wide >= deep;
        var ord = (az ? cr[0] >= cr[1] : al[0] <= al[1]) ? gs : new List<Grp> { b, a };
        var names = az ? new[] { R + " group", L + " group" } : new[] { "lead group", "trail group" };
        var ids = a.Ids!.Concat(b.Ids!).ToHashSet();
        foreach (var k in named.Where(x => !ids.Contains(x.Key) && names.Contains(x.Value)).Select(x => x.Key).ToList()) named.Remove(k);
        for (int k = 0; k < 2; k++) foreach (var id in ord[k].Ids!) named[id] = names[k];
        return $"{old} split, 2 groups {(az ? "azimuth" : "range")} {Miles(az ? wide : deep)}, " + string.Join(", ", ord.Select((g, k) => With($"{names[k]} {Loc(g, t)}", Fill(g))));
    }

    static bool AwacsUp(IReadOnlyList<Traffic> air, Me me) => AwacsOf(air, me) != null || Gci.Contains(me.Coalition);
    /// Radio wheel top level (Program): AWACS only with own AWACS/GCI, tanker only with own tanker in the air (otherwise nobody answers).
    public static (bool Awacs, bool Tanker) WheelUp(IReadOnlyList<Traffic> air, Me me) => (AwacsUp(air, me), Tankers(air, me).Count > 0);

    /// By itself (only after check-in, with own AWACS in the air): radar contact after takeoff, merged, threat (≤ 30 NM, hot/flank),
    /// split/combined, commit, new group/pop-up (≤ NewGroupNm, once per group), picture clean when no group has been in the radar picture for 2 min (R53).
    /// AW-P3: picture clean and not detected for over 2 min clear the memory, the group is reported anew on reappearing (≤ 30 NM pop-up).
    Call? AwacsAuto(Me me, Telemetry t, IReadOnlyList<Traffic> air, double now)
    {
        bool lost = hadAw && AwacsOf(air, me) == null; hadAw = AwacsOf(air, me) != null;   // LD10: AWACS shot down/landed
        if (!awacsIn || !AwacsUp(air, me)) { members.Clear(); watching = false; if (AwacsUp(air, me)) Hostiles(air, me, now); return null; }   // only after check-in; LK1: the AWACS tracks the tracks anyway (ID time)
        SetAwacs(air, me);
        bool scan = ScanSec <= 0 || Math.Floor(now / ScanSec) > Math.Floor(scanPrev / ScanSec); scanPrev = now;   // LD11
        var c = Cs(me);
        var groups = Hostiles(air, me, now).Select(g => (G: g, D: Dist(t.X, t.Z, g.Lead.X, g.Lead.Z))).OrderBy(x => x.D).ToList();
        foreach (var (g, _) in groups)
        {
            if (g.Ids!.All(id => now - seenAt.GetValueOrDefault(id, now) > 120)) Forget(g.Ids!);
            foreach (var id in g.Ids!) { seenAt[id] = now; lastPos[id] = (g.Lead.X, g.Lead.Z); }
        }
        // V10/A56: groups against the memory (unit -> identifier) instead of against the last tick: one that misses a round makes no pop-up/split/combined.
        // The identifier keeps the group with its smallest unit (lead kill changes nothing); split/combined only after 12 s stable, then adopted.
        bool first = !watching;
        watching = true;
        var seen = groups.Where(x => x.G.Ids!.Any(members.ContainsKey)).Select(x => x.G).ToHashSet();
        var owner = groups.SelectMany(x => x.G.Ids!.Where(members.ContainsKey).Select(id => (Tok: members[id], Id: id, x.G)))
                          .GroupBy(p => p.Tok).ToDictionary(k => k.Key, k => k.MinBy(p => p.Id).G);
        var ev = new Dictionary<Grp, (string E, string Tok, string[] Old)>();
        var since = new Dictionary<string, double>();
        // LD10: the AWACS is gone, a ground radar (GCI) of the side takes over -> with its own callsign radar contact and picture to everyone checked in
        if (lost) return A($"{c}, {awName}, {PictureCall(groups.Select(x => x.G).ToList(), t, me, now, true)}.");
        // LD8: "single group" becomes a name as soon as a second group is there -> short picture to him (at most every 60 s)
        if (single.Count > 0 && groups.Count >= 2 && now - renameAt >= 60 && groups.Any(x => x.G.Ids!.Any(single.Contains) && Nm(x.G) != "single group"))
        {
            single.Clear(); renameAt = now;
            return A($"{c}, {awName}, {PictureCall(groups.Select(x => x.G).ToList(), t, me, now, false)}.");
        }
        foreach (var (g, d) in groups)
        {
            var old = g.Ids!.Where(members.ContainsKey).Select(id => members[id]).Distinct().ToArray();
            var e = old.Length >= 2 ? "groups combined" : old.Length == 1 && owner[old[0]] != g ? "split, new group" : null;
            var tok = old.FirstOrDefault(o => owner[o] == g) ?? $"#{++tokSeq}";
            if (e == null || d >= NewGroupNm * NM || first) { foreach (var id in g.Ids!) members[id] = tok; continue; }
            var key = e + g.Lead.Id;
            since[key] = evSince.GetValueOrDefault(key, now);
            if (now - since[key] >= 12 && old.All(o => now - evAt.GetValueOrDefault(o, -1e9) >= 60)) ev[g] = (e, tok, old);
        }
        evSince = since;
        Call Tell(Grp g, string s) { groupsTold.UnionWith(g.Ids!); hadContacts = true; lastTold = now; Nm(g); return A($"{c}, {awName}, {s}.") with { Lead = s.Contains("BRAA") ? g.Lead.Id : 0 }; }   // LD4: BRAA anew at transmit (Program, Rebraa)
        Call Upd(Grp g, string s) { groupsTold.UnionWith(g.Ids!); hadContacts = true; lastTold = now; Nm(g); return Bull() != null ? ToAll($"{awName}, {s}.") : A($"{c}, {awName}, {s}."); }   // LK4: situation update to all (Bullseye)
        var nk = $"{me.Coalition}:{awFreq}:";   // A64: blocks only what another player of the same side has just heard on the frequency
        bool Heard(string k, double s) => net.TryGetValue(nk + k, out var w) && w.By != this && now - w.At < s;
        if (!radarContact && t.Agl > 900)
        {
            radarContact = true;
            return A($"{c}, {awName}, {PictureCall(groups.Select(x => x.G).ToList(), t, me, now, true)}.");
        }
        if (groups.Count > 0) lastHostile = now;   // R53: "picture clean" only without a group in the radar picture (also beyond NewGroupNm)
        // LD9 (protection of the AWACS, HVAA): group hot on the AWACS under 50 NM -> to everyone "Overlord, north group, 45 miles from Overlord, hot, Overlord moving south.",
        // planned first (like a leaker, Abm); mode awacs: the AWACS evades 40 NM (mission script ORBIT, after 10 min continues as planned)
        if (AwacsOf(air, me) is { } awT && groups.Select(x => x.G).FirstOrDefault(g => !g.Ids!.Any(hvaaTold.Contains) && Dist(g.Lead.X, g.Lead.Z, awT.X, awT.Z) < 50 * NM &&
                HdgDiff(g.Lead.Hdg * 180 / Math.PI, Bearing(g.Lead.X, g.Lead.Z, awT.X, awT.Z)) <= 30) is { } hg)
        {
            hvaaTold.UnionWith(hg.Ids!); groupsTold.UnionWith(hg.Ids!); hadContacts = true; Abm.Leaker(side, hg.Ids!);   // reported: no "new group" afterward
            if (!hg.Ids!.Any(id => Heard("hvaa:" + id, 600)))
            {
                foreach (var id in hg.Ids!) net[nk + "hvaa:" + id] = (now, this);
                double away = Bearing(hg.Lead.X, hg.Lead.Z, awT.X, awT.Z), rad = away * Math.PI / 180;
                if (Flights.Follow) Flights.Cmd?.Invoke(FormattableString.Invariant($"ORBIT;{awT.Group};{awT.X + Math.Cos(rad) * 40 * NM:0};{awT.Z + Math.Sin(rad) * 40 * NM:0}"));
                return ToAll($"{awName}, {Nm(hg)}, {MilesTxt(Dist(hg.Lead.X, hg.Lead.Z, awT.X, awT.Z))} from {awName}, hot, {awName} moving {Card(away)}.");
            }
        }
        // LK9: the side's Air Battle Manager distributes the groups (Abm, with the AI fighters); the player is planned only with a CAP tasking ("on station", LK5),
        // otherwise he hears only Threat reports and the others' assignments
        // LD14: commit without reply -> ask again once after 10 s, to the next flight after 25 s; twice without reply -> "no radio", no longer planned until he speaks
        if (ask != null && !onStation) ask = null;
        if (ask is { } q)
        {
            if (!q.Asked && now - q.At > 10) { ask = q with { Asked = true }; return A($"{c}, {awName}, {q.Text}, how copy?"); }
            if (now - q.At > 25) { ask = null; Abm.Unable(side, flight, now); if (++silent >= 2) noComm = true; }
        }
        if (onStation && !noComm) Abm.Update(side, new Abm.Fighter(flight, t.X, t.Z, me.Fuel), now);
        Abm.Tick(side, now);
        abmQ.AddRange(Abm.Take(side, flight, now));
        while (abmQ.Count > 0)
        {
            var e = abmQ[0]; abmQ.RemoveAt(0);
            dirAt = now;
            if (e.Kind == "maintain") return A($"{c}, {awName}, maintain CAP.");
            if (e.Kind == "skip") { ask = null; return A(Abm.Say(e, awName).Replace(e.Flight + ",", c + ",")); }   // LD16: another player has fired
            if (e.Kind == "aspect") return A(Abm.Say(e, awName).Replace(e.Flight + ",", c + ","));   // LD8: assigned group turns
            if (e.Kind == "reset")
            {
                if (e.Label != "picture clean") return A($"{c}, {awName}, reset.");
                Clean(); net[nk + "clean"] = (now, this); cleanAt[side] = now;   // is already "picture clean": no second one afterward
                return A($"{c}, {awName}, picture clean, reset.");
            }
            if (groups.Select(x => x.G).FirstOrDefault(g => g.Ids!.Any(e.Ids.Contains)) is not { } eg) continue;   // commit/recommit: group gone meanwhile
            committed.UnionWith(eg.Ids!);
            ask = new(now, eg.Ids!, $"{e.Kind} {Nm(eg)}", false);
            return Tell(eg, $"{e.Kind} {Nm(eg)}, {With(Braa(t, eg.Lead), Fill(eg))}");
        }
        foreach (var (g, d) in groups)
        {
            bool hotAspect = Hot(g.Lead, t);
            if (d < 3 * NM && Math.Abs(g.Lead.AltMsl - t.AltMsl) < 5000 * Ft && !g.Ids!.Any(id => now - mergeTold.GetValueOrDefault(id, -1e9) < 180))
            {
                foreach (var id in g.Ids!) mergeTold[id] = now;
                return Tell(g, Stored(g) is { } mn ? mn + " merged" : "merged");   // R291: "north group merged" (ATP 1-02.1), unnamed just "merged"
            }
            // LK8 (ATP 1-02.1 FURBALL: own and enemies mixed within 5 NM): once "Overlord, furball, bullseye …" to everyone, then no individual Threats on the group
            bool fur = air.Any(a => a.InAir && a.Coalition == me.Coalition && !Support.IsMatch(a.Type) && Dist(a.X, a.Z, g.Lead.X, g.Lead.Z) < 5 * NM && Dist(a.X, a.Z, t.X, t.Z) > NM);   // another own fighter (not me: merged)
            if (fur && !g.Ids!.Any(id => now - furTold.GetValueOrDefault(id, -1e9) < 120))
            {
                foreach (var id in g.Ids!) furTold[id] = now;
                if (!g.Ids!.Any(id => Heard("fur:" + id, 120)))
                {
                    foreach (var id in g.Ids!) net[nk + "fur:" + id] = (now, this);
                    groupsTold.UnionWith(g.Ids!); hadContacts = true; lastTold = now;
                    return Bull() is { } fb ? ToAll($"{awName}, furball, bullseye {Brg(Bearing(fb.X, fb.Z, g.Lead.X, g.Lead.Z))}, {Miles(Dist(fb.X, fb.Z, g.Lead.X, g.Lead.Z))}.")
                                            : A($"{c}, {awName}, furball, {Braa(t, g.Lead)}.");
                }
            }
            // LK1: reported as bogey, now hostile -> own short declaration to everyone ("Overlord, north group, hostile."); if another player has just heard it, only remember
            if (g.Hostile && g.Ids!.Select(id => heard.GetValueOrDefault(id)).FirstOrDefault(p => p != null) is { } hp && hp[0] == "bogey")
            {
                foreach (var id in g.Ids!) heard[id] = hp.Select((s, i) => i == 0 ? "hostile" : s).ToArray();
                if (!g.Ids!.Any(id => Heard($"decl:{id}", 300)))
                {
                    foreach (var id in g.Ids!) net[nk + "decl:" + id] = (now, this);
                    return ToAll($"{awName}, {Nm(g)}, hostile.");
                }
            }
            // Threat (hot ≤ 30 NM): new only at each closer step 30/20/10/5 NM, under 10 NM hot every 45 s; if it recedes (step + 5 NM), the step is open again
            var told = g.Ids!.Where(threatTold.ContainsKey).Select(id => threatTold[id]).OrderBy(x => x.Band).ThenByDescending(x => x.At).FirstOrDefault((Band: 99, At: -1e9));
            if (d > (told.Band + 5) * NM) foreach (var id in g.Ids!) threatTold.Remove(id);
            int band = Band(d);
            // LK9 (ATP 1-02.1 THREAT: untargeted): the own group (committed/targeted/assigned) gets only a short target update without fill-ins at 20 and 10 NM,
            // a group that another flight has gets no Threat at all
            bool own = Own(g);
            if (own && !fur && !g.Ids!.Any(judy.Contains) && d < 30 * NM && hotAspect && band <= 20 && band >= 10 && band < told.Band)
            {
                foreach (var id in g.Ids!) threatTold[id] = (band, now);
                return Tell(g, $"{Nm(g)} {Braa(t, g.Lead)}");
            }
            if (d < 30 * NM && hotAspect && !own && !fur && Tgt(g) == "" && (band < told.Band || band <= 10 && now - told.At > 45))
            {
                foreach (var id in g.Ids!) threatTold[id] = (band, now);
                committed.UnionWith(g.Ids!);
                return Tell(g, With($"threat, {Nm(g)} {Braa(t, g.Lead)}", Fill(g)));
            }
            // LK8 (ATP 1-02.1 LEAKER: past a defensive line): hostile, taken by no flight, closer to the own airfield than any CAP and heading toward it
            // -> once "Overlord, leaker, north group, bullseye …, track east, hostile." (without CAP there is no line, so no leaker)
            if (!own && !fur && g.Hostile && Tgt(g) == "" && !g.Ids!.Any(leakTold.Contains) && Abm.Caps(side) is { Count: > 0 } caps &&
                Tower.All.Where(f => f.Side == me.Coalition).MinBy(f => Dist(f.X, f.Z, g.Lead.X, g.Lead.Z)) is { } af &&
                HdgDiff(g.Lead.Hdg * 180 / Math.PI, Bearing(g.Lead.X, g.Lead.Z, af.X, af.Z)) <= 30 && caps.All(p => Dist(p.X, p.Z, af.X, af.Z) > Dist(g.Lead.X, g.Lead.Z, af.X, af.Z)))
            {
                leakTold.UnionWith(g.Ids!); Abm.Leaker(side, g.Ids!);   // LD2: leaker gets a flight regardless of aspect
                if (!g.Ids!.Any(id => Heard("leak:" + id, 600)))
                {
                    foreach (var id in g.Ids!) net[nk + "leak:" + id] = (now, this);
                    return Upd(g, $"leaker, {Nm(g)}, {Loc(g, t)}, {g.Id}");
                }
            }
            if (ev.TryGetValue(g, out var e))
            {
                // R54/R239: split "north group split, 2 groups azimuth 5, …" with both parts; combined: "north and south groups combined", then one name
                var cn = e.Old.Select(o => g.Ids!.Where(id => members.GetValueOrDefault(id) == o).Select(id => named.GetValueOrDefault(id)).FirstOrDefault(s => s != null)).OfType<string>().Distinct().ToList();
                foreach (var id in g.Ids!) members[id] = e.Tok;
                foreach (var o in e.Old.Append(e.Tok)) evAt[o] = now;
                if (e.E[0] == 's') { var rest = owner[e.Old[0]]; groupsTold.UnionWith(rest.Ids!); return Upd(g, SplitCall(cn.Count > 0 ? cn[0] : "group", rest, g, t)); }   // rest: keeps the old identifier
                var say = cn.Count >= 2 ? $"{cn[0][..^6]} and {cn[1][..^6]} groups combined" : e.E;
                var keep = Stored(g);
                foreach (var id in g.Ids!) if (keep == null) named.Remove(id); else named[id] = keep;
                return Upd(g, With($"{say}, {Loc(g, t)}", Fill(g)));
            }
            if (d < NewGroupNm * NM && !g.Ids!.Any(groupsTold.Contains) && (scan || !seen.Contains(g) && d < 30 * NM && !first))   // new group as soon as detected; once per group (until picture clean or 2 min gone), pop-up only at first detection
            {
                groupsTold.UnionWith(g.Ids!);
                if (g.Ids!.Any(id => Heard($"{id}", 300))) continue;   // A64: another player on the frequency has just heard it
                foreach (var id in g.Ids!) net[nk + id] = (now, this);
                return !seen.Contains(g) && d < 30 * NM && !first ? Tell(g, With($"pop-up group, {Braa(t, g.Lead)}", Fill(g))) : Upd(g, With($"new group, {Loc(g, t)}", Fill(g)));   // pop-up: for this flight (BRAA)
            }
        }
        // LK8: the side has just heard "picture clean" (AI splash via Flights, another player's splash), nothing new to me since -> forget silently, do not repeat
        if (hadContacts && groups.Count == 0 && cleanAt.GetValueOrDefault(side, -1e9) >= lastTold) { Clean(); return null; }
        if (scan && Faded(groups.Select(x => x.G).ToList(), t, c, now, Heard, nk) is { } fc) return fc;
        if (hadContacts && now - lastHostile > 120)
        {
            Clean();   // once, until something is reported again; AW-P3: after "clean" every group is new again
            if (Heard("clean", 120)) return null;   // A64: just said for another already
            net[nk + "clean"] = (now, this);
            return ToAll($"{awName}, picture clean.");   // LK8: situation status to everyone on the frequency (without location, also without bullseye)
        }
        return null;
    }

    /// LK6 (ATP 1-02.1 FADED: sensor data on the group lost): a reported group of which no unit has been detected for FadeSec and which was not shot down,
    /// "Overlord, north group faded, last known bullseye 030, 40." (to everyone; without bullseye to the player); then forgotten, return = new group/pop-up.
    public static double FadeSec = 30;
    public static double ScanSec = 10;   // LD11: E-3 antenna sweep – new group and faded only with the next scan (pop-up immediately), 0 = immediately
    double scanPrev = -1e9;
    readonly Dictionary<int, (double X, double Z)> lastPos = new();   // unit -> last detected location (lead of the group)
    Call? Faded(List<Grp> groups, Telemetry t, string c, double now, Func<string, double, bool> heardBy, string nk)
    {
        var vis = groups.SelectMany(g => g.Ids!).ToHashSet();
        var visTok = vis.Where(members.ContainsKey).Select(id => members[id]).ToHashSet();
        foreach (var fg in groupsTold.Where(id => !vis.Contains(id) && lastPos.ContainsKey(id)).GroupBy(id => members.GetValueOrDefault(id, "#" + id)).ToList())
        {
            var ids = fg.ToArray();
            if (visTok.Contains(fg.Key) || ids.Any(id => now - seenAt.GetValueOrDefault(id, now) < FadeSec)) continue;
            var label = ids.Select(id => named.GetValueOrDefault(id)).FirstOrDefault(s => s != null) ?? (groups.Count == 0 ? "single group" : "group");
            var live = ids.Where(id => !killed.ContainsKey(id)).ToArray();
            var p = live.Length > 0 ? lastPos[live[0]] : default;
            Forget(ids);
            foreach (var id in ids) lastPos.Remove(id);
            if (live.Length == 0 || live.Any(id => heardBy("fade:" + id, 300))) continue;   // shot down: splash, no faded (LK8); A64: just said for another
            foreach (var id in live) net[nk + "fade:" + id] = (now, this);
            if (Bull() is not { } b) return A($"{c}, {awName}, {label} faded.");
            return ToAll($"{awName}, {label} faded, last known bullseye {Brg(Bearing(b.X, b.Z, p.X, p.Z))}, {Miles(Dist(b.X, b.Z, p.X, p.Z))}.");
        }
        return null;
    }

    /// AW-P3: clear memory of these units (reported, threat step, commit, group identifier).
    void Forget(int[] ids)
    {
        groupsTold.ExceptWith(ids); committed.ExceptWith(ids); mine.ExceptWith(ids); judy.ExceptWith(ids);
        foreach (var id in ids) { threatTold.Remove(id); members.Remove(id); named.Remove(id); heard.Remove(id); }
    }

    // ======================================================================= Tanker
    record Tk(string Name, Traffic A, string Num = "");   // Name as spoken ("Texaco two one" only with two same-named tankers, R119), Num = digits of the callsign

    // DCS callsign "Texaco11" -> Texaco, not the group name ("KC-135 North" -> KC, A69); without callsign only "ATC Texaco #3" -> Texaco
    static string TankerName(Traffic a) => CsWord(a) ?? (Regex.Match(a.Group, @"^ATC\s+([A-Za-z]+)").Groups[1].Value is { Length: > 0 } s ? s : "Tanker");
    static bool Boom(Traffic a) => a.Type is "KC-135" or "KC-10" or "KC_10_Extender";   // KC_10_Extender_D and KC135MPRS have the basket
    static bool WantBoom(Me me) => Regex.IsMatch(me.Type, "F-16|F-15|F-4|A-10|B-1|B-52|C-17|E-3");
    // Types without refueling equipment (piston, helicopter, MiG-15/19/21/29, F-5, Su-27, trainer …); ponytail: unknown ones (mods) still get a rendezvous
    static readonly Regex NoAar = new(@"^(UH-|Mi-|Ka-|SA342|AH-|OH58|CH-47|MH-60|MiG-(15|19|21|29)|F-5|F-86|Su-27|Su-25(?!T)|L-39|C-101|Yak-52|I-16|P-47|P-51|TF-51|Bf-109|FW-190|Spitfire|Mosquito|Hawk|MB-339|Christen|AJS37)", RegexOptions.IgnoreCase);

    /// Tankers of the own side, only matching (boom for F-16/F-15/F-4E/A-10, otherwise basket; type without air refueling: none), then by range.
    /// any: all tankers, matching first (for the rejection "negative, no compatible tanker", A125).
    static List<Tk> Tankers(IReadOnlyList<Traffic> air, Me me, bool any = false)
    {
        if (!any && NoAar.IsMatch(me.Type)) return new();
        bool wantBoom = WantBoom(me);
        var t = me.Tel;
        // R240: full callsign with number if the DCS callsign has one ("Texaco 1-1" -> "Texaco one one", ATP-56(C)); without callsign number only with two same-named tankers (R119)
        var side = air.Where(a => a.InAir && (a.Coalition == me.Coalition || a.Coalition == 0) && Regex.IsMatch(a.Type, "KC|Tanker|IL-78")).Select(TankerName).ToList();
        Tk Mk(Traffic a)
        {
            var w = TankerName(a);
            var num = Regex.Replace(Callsigns.GetValueOrDefault(a.Group, ""), @"\D", "") is { Length: > 0 } d ? d : side.Count(x => x == w) < 2 ? "" : Regex.Match(a.Group, @"#(\d+)").Groups[1].Value;
            return new(num == "" ? w : $"{w} {Digits(num)}", a, num);
        }
        return air.Where(a => a.InAir && (a.Coalition == me.Coalition || a.Coalition == 0) && Regex.IsMatch(a.Type, "KC|Tanker|IL-78") && (any || Boom(a) == wantBoom))
                  .OrderBy(a => Boom(a) == wantBoom ? 0 : 1).ThenBy(a => t == null ? 0 : Dist(t.X, t.Z, a.X, a.Z))
                  .Select(Mk).ToList();
    }

    static string TacanSay(Traffic a) =>
        Beacons.TryGetValue(a.Group, out var b) && Regex.Match(b, @"^(\d+)([XY])$") is { Success: true } m
            ? $", TACAN {Digits(m.Groups[1].Value)} {(m.Groups[2].Value == "X" ? "X-ray" : "Yankee")}" : "";

    List<Call> TankerCall(string n, Me me, IReadOnlyList<Traffic> air, double now)
    {
        var c = Cs(me);
        var any = Tankers(air, me, true);
        var all = Tankers(air, me);   // only matching tankers (boom/basket), otherwise rejection (A125)
        // R119: "Texaco 2-1" (n: "texaco 2 1") selects the tanker with the number; "Texaco" only: the own, otherwise the nearest
        bool Said(Tk k, bool num) => TankerName(k.A).ToLowerInvariant() is var w && (num ? k.Num != "" && Regex.IsMatch(n, $@"\b{w} ?{Regex.Replace(k.Num, @"\d", "$0 ?")}\b") : n.Contains(w));
        var tk = all.FirstOrDefault(k => Said(k, true)) ?? all.FirstOrDefault(k => tank > 0 && k.A.Group == tanker && Said(k, false)) ?? all.FirstOrDefault(k => Said(k, false)) ?? all.FirstOrDefault(k => k.A.Group == tanker) ?? all.FirstOrDefault();
        // wrong type named (e.g. A-10 calls the basket tanker): that tanker refuses at once and names the matching one, instead of a silent switch to another tanker
        if (!NoAar.IsMatch(me.Type) && (any.FirstOrDefault(k => Said(k, true)) ?? any.FirstOrDefault(k => Said(k, false))) is { } wt && all.All(k => k.A.Group != wt.A.Group))
        {
            string Kind(Tk k) => Boom(k.A) ? "boom" : "basket";
            var tw = me.Tel;
            var ok = tk == null ? $"no {(Boom(wt.A) ? "basket" : "boom")} tanker airborne"
                : $"{tk.Name} has the {Kind(tk)}, {(tw == null ? "" : $"bearing {Brg(Bearing(tw.X, tw.Z, tk.A.X, tk.A.Z))}, {MilesTxt(Dist(tw.X, tw.Z, tk.A.X, tk.A.Z))}, ")}{TkInfo(tk, true, false, false, TacanSay(tk.A) != "")}";
            return new() { new("Tanker", wt.Name, $"{c}, {wt.Name}, negative, {Kind(wt)} only, {ok}.") };
        }
        if (tk == null) return new() { any.Count == 0 ? new("Tanker", "Tanker", $"{c}, no tanker airborne.") : new("Tanker", any[0].Name, $"{c}, {any[0].Name}, negative, no compatible tanker.") };
        List<Call> T(string s) => new() { new("Tanker", tk.Name, s) };   // R304: our controller always speaks, the DCS tanker is mute (only radio menu for basket/boom)
        var tp = me.Tel;
        double dTk = tp == null ? 0 : Dist(tp.X, tp.Z, tk.A.X, tk.A.Z);
        var brg = tp == null ? "" : $"bearing {Brg(Bearing(tp.X, tp.Z, tk.A.X, tk.A.Z))}, {MilesTxt(dTk)}, ";
        // TP2: queries in every state, without state change or radio menu; "say position/status" names everything
        bool qa = Has(n, "altitude", "angels", "level", "block"), qs = Has(n, "airspeed", "speed", "knots"), qh = Has(n, "heading", "track", "course"), qt = Has(n, "tacan"), qp = Has(n, "position", "bearing", "range", "where", "status") || tank == 1 && Has(n, "vector");   // "vector" only during the rendezvous (otherwise e.g. "request radar vectors" on the wrong frequency)
        if ((qa || qs || qh || qt || qp) && Has(n, "say", "request", "confirm", "what") && !Has(n, "join", "rejoin", "rendezvous", "refuel"))
        {
            if (tank == 1 && Has(n, "vector") && TankVec(me, air, now, true) is { } v) return new() { v };   // "request vector": the next vector at once
            return T($"{c}, {tk.Name}, {(qp ? brg + TkInfo(tk) : TkInfo(tk, qa, qh, qs, qt))}.");
        }
        var was = tanker;
        tanker = tk.A.Group;
        cs = c;
        var l = Line(tk.A.Group);
        if (Has(n, "complete", "departing", "thank", "good day", "leaving"))
        {
            bool first = l.FirstOrDefault(o => o.tank >= 2) == this;   // had the spot (e.g. after a deliberate partial amount, OnRefuel tank 3): the next one moves up (A66/A128)
            tank = 0; OffLine();
            var total = offload > 0 ? $"total offload {offload / 1000:0.0}, " : "";   // thousand lb; without known internal tank size no amount
            offload = 0;
            var dep = T($"{c}, {tk.Name}, {total}cleared to depart, " + (AwacsContact(air, me, Freqs.GetValueOrDefault("AWACS", 251.0)) is { } aw ? $"contact {aw}." : "frequency change approved."));
            if (first && l.FirstOrDefault(o => o.tank >= 2) is { } nx) dep.Add(new("Tanker", tk.Name, $"{nx.cs}, {tk.Name}, cleared pre-contact.", To: nx));
            return dep;
        }
        if (Has(n, "disconnect"))   // disconnected, not yet finished: to the right wing, no departure clearance (A67)
        {
            var dc = T($"{c}, {tk.Name}, copy disconnect, move to the right wing.");
            if (tank != 3) return dc;
            tank = 2; OffLine();   // without DCS event (OnRefuel) only the report frees the spot, the next gets it as in OnRefuel (A65)
            if (l.FirstOrDefault(o => o.tank >= 2) is { } nx) dc.Add(new("Tanker", tk.Name, $"{nx.cs}, {tk.Name}, cleared pre-contact.", To: nx));
            return dc;
        }
        if (Has(n, "right wing"))   // back on the right wing: no clearance, continue with "pre contact" or "complete"
            return T($"{c}, {tk.Name}, roger, report complete or request more.");
        // R56: confirm join/pre-contact only if the receiver is close enough to the tanker (ATP-56(C)), otherwise state the situation
        List<Call> Far() => T($"{c}, {tk.Name}, negative, {brg}report visual.");
        bool far = dTk > 2 * NM;
        List<Call> Join(string neg)   // "cleared to join, left observation" (R287: before that without clearance "negative, not cleared pre-contact, ")
        {
            if (tp != null && Along(tp, tk.A) > 0.5 * NM)   // TP5 (ATP-56(C)): join only from astern
                return T($"{c}, {tk.Name}, negative, rejoin from astern, {Clock(tp, tk)}, report visual.");
            Menu = ("intent", tk.A.Group);   // TP6: DCS "Intent to refuel" only now, not with the request
            if (!l.Contains(this)) l.Add(this);
            Arrive(l);
            tank = 2;
            var ahead = l.Where(o => o.tank >= 2).ToList();   // Order after arrival (A65)
            var order = ahead.Count > 1 ? $" {ahead[0].cs} first, then {string.Join(", then ", ahead.Skip(1).Select(o => o.cs))}." : "";
            return T($"{c}, {tk.Name}, {neg}cleared to join, left observation, number {Pos(l)}.{order}");
        }
        if (Has(n, "pre contact", "precontact", "in position", "ready for contact", "stabilized", "ready to refuel"))
        {
            if (far) return Far();
            if (tank < 2) return Join("negative, not cleared pre-contact, ");   // R287 (ATP-56(C)): pre-contact only after "cleared to join"
            if (!l.Contains(this)) l.Add(this);   // refuel without a new request: line up at the back
            Arrive(l);
            if (Pos(l) > 1) { tank = 2; return T($"{c}, {tk.Name}, negative, hold left observation, number {Pos(l)}."); }
            Menu = (tank < 1 || !DcsIntent ? "intent" : "precontact", tk.A.Group);
            tank = 3;
            return T($"{c}, cleared contact, {(Boom(tk.A) ? "boom" : "basket")} ready.");
        }
        if (tank < 2 && Has(n, "judy", "radar contact")) { vecEnd = true; return T($"{c}, {tk.Name}, roger, report visual."); }   // TP3: radar contact ends the vectors, the join needs "visual"
        if (tank < 2 && Has(n, "visual", "tally", "in sight"))
            return dTk > 8 * NM ? T($"{c}, {tk.Name}, continue, {MilesTxt(dTk)}, report visual.") : Join("");   // R117: "visual" releases the join (2-5 NM as in TankVec), only not beyond 8 NM (TickLive discards over 10 NM); R286: continue with "visual", the tanker understands that
        if (Has(n, "observation", "left wing", "visual", "tally", "in sight", "joined"))
        {
            if (far) return Far();
            if (tank < 2) return Join("");   // R287: without "cleared to join" no "cleared pre-contact"
            if (!l.Contains(this)) l.Add(this);
            tank = 2;
            return T(Pos(l) > 1 ? $"{c}, {tk.Name}, stabilize left observation, number {Pos(l)}." : $"{c}, {tk.Name}, cleared pre-contact.");
        }
        // Only a real request starts the rendezvous; chatter with the tanker (fuel state etc.) does not reset contact
        if (tank >= 2 || !Has(n, "join", "rejoin", "rendezvous", "refuel", "fuel", "tank", "gas", "check in", "checking in", "aar", "a a r", "texaco", "shell", "arco"))
            return new() { new("Tanker", tk.Name, $"{c}, {tk.Name}, say again.") };
        if (tank == 1 && was == tk.A.Group) return T($"{c}, {tk.Name}, continue, {brg}report visual.");   // TP1: repeated request during the rendezvous: no restart, no radio menu
        tank = 1; offload = 0;
        if (!l.Contains(this)) l.Add(this);
        // R56: rendezvous 1000 ft below the tanker (ATP-56(C)), climb only with "cleared to join"
        if (tp == null) return T($"{c}, {tk.Name}, {TkInfo(tk)}. Join 1000 feet below, report visual.");
        double closure = Math.Max(50, Tas(tp) - tk.A.Speed * Math.Cos(tk.A.Hdg - Bearing(tp.X, tp.Z, tk.A.X, tk.A.Z) * Math.PI / 180));   // R370: straight to it at current TAS (both as ground speed, as in TankVec)
        var min = Math.Max(1, Math.Ceiling(dTk / closure / 60));
        return T($"{c}, {tk.Name}, {brg}{TkInfo(tk)}. Expect the join in {min} minute{(min > 1 ? "s" : "")}, 1000 feet below, report visual.");
    }

    // TP2: tanker data for the initial call and queries (displayed speed from ground speed/TAS); TACAN with everything if the tanker has one, "negative TACAN" only when asked
    static string TkInfo(Tk tk, bool alt = true, bool hdg = true, bool spd = true, bool tcn = false)
    {
        var a = tk.A; var p = new List<string>();
        int ang = (int)Math.Round(a.AltMsl / Ft / 1000);
        if (alt) p.Add(Tracks.TryGetValue(a.Group, out var trk) ? $"anchor {trk}, block {ang - 1} to {ang + 1}" : Angels(a.AltMsl));
        if (hdg) p.Add($"track {Card(a.Hdg * 180 / Math.PI)}");
        if (spd) p.Add($"{Math.Round(a.Speed / Kt / (1 + 0.02 * a.AltMsl / Ft / 1000) / 10) * 10:0} knots");
        if (tcn || alt && hdg && spd && TacanSay(a) != "") p.Add(TacanSay(a) is { Length: > 0 } tc ? tc[2..] : "negative TACAN");
        return string.Join(", ", p);
    }
    static double Along(Telemetry t, Traffic a) => (t.X - a.X) * Math.Cos(a.Hdg) + (t.Z - a.Z) * Math.Sin(a.Hdg);   // > 0: ahead of the tanker's 3-9 line
    static string Clock(Telemetry t, Tk tk)
    {
        int clock = (int)Math.Round(((Bearing(t.X, t.Z, tk.A.X, tk.A.Z) - t.Hdg * 180 / Math.PI + 360) % 360) / 30) % 12;
        return $"{tk.Name} {(clock == 0 ? 12 : clock)} o'clock, {MilesTxt(Dist(t.X, t.Z, tk.A.X, tk.A.Z))}";
    }

    // Order per tanker (V11/A65): whoever was at the tanker first (insert on arrival); position counts only for those already at the tanker (tank ≥ 2)
    static readonly Dictionary<string, List<Ops>> line = new();
    string cs = "";
    static List<Ops> Line(string tk) { if (!line.TryGetValue(tk, out var l)) line[tk] = l = new(); return l; }
    void Arrive(List<Ops> l) { if (tank < 2) { l.Remove(this); l.Add(this); } }   // A65: position by arrival, not by request
    void OffLine() { foreach (var l in line.Values) l.Remove(this); }
    int Pos(List<Ops> l) => l.TakeWhile(o => o != this).Count(o => o.tank >= 2) + 1;

    const double Lb = 2.20462;   // kg -> lb; DCS tank fill 0–1 refers to the internal tank (mission's fuelMassMax, A124)

    // ======================================================================= every second: contact at the tanker, threats
    public List<Call> Tick(Me me, IReadOnlyList<Traffic> air, double now, bool awacs = true, Ops? lead = null)   // awacs: false for wingmen (lead only); lead: its Ops (tanker check-in applies to the flight)
    {
        HotEdge(now);
        var res = Flush(me, now);   // A72: completed drop (3 s without impact) as one call, also on the ground
        if (TankerBlock(me, air, now, lead) is { } tb) res.Add(tb);   // N44
        res.AddRange(TickLive(me, air, now, awacs));
        if (lead == null) res.AddRange(JtacTick(me, now));   // J9/J10 (wingmen: lead only)
        if (TankVec(me, air, now, aw: awacs) is { } tv) res.Add(tv);   // N38: join vectors, separate from TickLive
        return res;
    }

    // N38: join vectors to the tanker (tank == 1), until "visual"/"judy", "report visual" (within 5 NM behind the 3-9 line, not on opposite course) or turning away (5 NM beyond the smallest distance against the vector);
    // lead-pursuit intercept on a point 2 NM in trail, ahead of the 3-9 line 2 NM offset to the receiver's side (TP4: no head-on into the nose), target 1000 ft below the tanker.
    // TP3: first vector 30 s after the request, then only on deviation > 30° or when crossing 10 NM, at most every 60 s (30 s in the turn behind the tanker); force: "request vector".
    public static bool Vectors = true;   // Config TankerVectors
    double vecAt;
    int vecBand = 99;
    double vecMin = 1e9;   // smallest distance since the request (NM)
    bool vecEnd, vecFirst;
    internal static double Tas(Telemetry t) => Math.Max(50, t.Ias * (1 + 0.02 * t.AltMsl / Ft / 1000));   // R370: TAS approximation in m/s (+2 % per 1000 ft)
    Call? TankVec(Me me, IReadOnlyList<Traffic> air, double now, bool force = false, bool aw = false)
    {
        var t = me.Tel;
        if (tank != 1) { vecAt = now; vecBand = 99; vecMin = 1e9; vecEnd = vecFirst = false; return null; }
        if (force) { vecEnd = vecFirst = false; vecMin = 1e9; }
        else if (!Vectors || vecEnd || now - vecAt < 30) return null;
        if (t == null || OnGround(t)) return null;
        var tk = Tankers(air, me).FirstOrDefault(k => k.A.Group == tanker);
        if (tk == null) return null;
        var a = tk.A;
        bool byAw = aw && awacsIn && AwacsUp(air, me);   // E1: checked in with the AWACS -> it gives the join vectors (as in reality), otherwise the tanker
        if (byAw) SetAwacs(air, me);
        Call V(string s) => byAw ? A(s) : new("Tanker", tk.Name, s);
        double d = Dist(t.X, t.Z, a.X, a.Z), nm = d / NM, hdg = t.Hdg * 180 / Math.PI, fx = Math.Cos(a.Hdg), fz = Math.Sin(a.Hdg), along = Along(t, a);
        bool conv = along > 0 || HdgDiff(hdg, a.Hdg * 180 / Math.PI) >= 90;   // still ahead of the 3-9 line or not yet turned behind the tanker
        // Intercept time tt from |P + V·tt| = s·tt (P: aim point relative to us, V: tanker velocity, s: our TAS); without a solution, straight to it
        double off = along > 0 ? (Along(t, a with { Hdg = a.Hdg + Math.PI / 2 }) > 0 ? 2 : -2) * NM : 0;   // side of the receiver: + right of the tanker
        double s = Tas(t), px = a.X - 2 * NM * fx - off * fz - t.X, pz = a.Z - 2 * NM * fz + off * fx - t.Z, vx = a.Speed * fx, vz = a.Speed * fz;
        double qa = vx * vx + vz * vz - s * s, qb = 2 * (px * vx + pz * vz), qc = px * px + pz * pz, tt = 0;
        if (Math.Abs(qa) < 1e-6) { if (qb < 0) tt = -qc / qb; }
        else if (qb * qb - 4 * qa * qc is var disc and >= 0)
        {
            var r = new[] { (-qb - Math.Sqrt(disc)) / (2 * qa), (-qb + Math.Sqrt(disc)) / (2 * qa) }.Where(x => x > 0).ToList();
            if (r.Count > 0) tt = Math.Min(r.Min(), 3600);
        }
        double want = Bearing(t.X, t.Z, t.X + px + vx * tt, t.Z + pz + vz * tt);
        vecMin = Math.Min(vecMin, nm);
        if (nm > vecMin + 5 && HdgDiff(want, hdg) > 90) { vecEnd = true; return null; }   // turned away or flew off (e.g. back to the airfield, new flight in the same slot): no more vectors until the next request
        var pos = Clock(t, tk);
        if (nm <= 5 && !conv) { vecEnd = true; return V(byAw ? $"{Cs(me)}, {awName}, {pos}, report visual to {tk.Name}." : $"{Cs(me)}, {pos}, report visual."); }
        int band = nm <= 10 ? 10 : 99;
        if (vecFirst && (band >= vecBand && HdgDiff(want, hdg) <= 30 || !conv && now - vecAt < 60)) return null;
        vecAt = now; vecFirst = true; vecBand = Math.Min(vecBand, band);
        double turn = ((want - hdg) % 360 + 540) % 360 - 180;
        var go = Math.Abs(turn) > 5 ? $"turn {(turn > 0 ? "right" : "left")} heading {Brg(want)}" : $"continue heading {Brg(want)}";
        double dAlt = (a.AltMsl - 1000 * Ft - t.AltMsl) / Ft;   // R372: climb/descend verb as with the tower's AltTo; within ±500 ft plain maintain
        return V($"{Cs(me)}, {(byAw ? awName : tk.Name)}, {go} for the join, {pos}, {(Math.Abs(dAlt) <= 500 ? "" : dAlt > 0 ? "climb and " : "descend and ")}maintain {Angels(a.AltMsl - 1000 * Ft)}.");
    }

    List<Call> TickLive(Me me, IReadOnlyList<Traffic> air, double now, bool awacs)
    {
        var t = me.Tel;
        // R309: data gap (pause, mission data briefly missing) is not a shutdown: check-in, radar contact and reported items stay, reset only after 2 min without telemetry
        if (t != null) telGone = null;
        else if (now - (telGone ??= now) < 120) return new();
        if (t == null || OnGround(t))
        {
            Cold();   // landed / gone: range free
            if (t == null || t.Ias < 5)   // shut down: next flight with a new picture; touch-and-go keeps radar contact and reported items
            {
                radarContact = awacsIn = hadContacts = onStation = false;
                groupsTold.Clear(); committed.Clear(); threatTold.Clear(); mergeTold.Clear(); judy.Clear(); members.Clear(); seenAt.Clear(); heard.Clear(); mine.Clear(); lastPos.Clear(); watching = false;   // LK3: group names belong to the side, not the flight
                RangeReset(); checkedIn = false;   // R131: landing/shutdown ends the range visit (debrief and rgPasses stay), touch-and-go stays checked in; the new flight starts with check-in again
            }
            return new();
        }
        if ((isHot || isDry) && Range is { } z)   // R285: a dry pass without "off" also frees the range
        {
            var d = Dist(t.X, t.Z, z.X, z.Z);
            // A119: no "off" -> 60 s after the last impact (without impact 180 s after "in hot") or 60 s beyond 3 NM heading away the range frees itself
            // (the 60 s cover the turn-in after "in hot" and weapons still flying after turning away: loft, Maverick, GBU; "in hot" and every impact restart it)
            if (d > 3 * NM && HdgDiff(t.Hdg * 180 / Math.PI, Bearing(t.X, t.Z, z.X, z.Z)) > 90) { if (double.IsNaN(rgAway)) rgAway = now; } else rgAway = double.NaN;
            if (d > z.R + 10 * NM)   // R14: out of the range, withdrawal announced
                { var lv = R($"{Cs(me)}, Range Alpha, leaving the range, range is cold{Ended(", ")}. Report in hot or checking out."); HotEdge(now); return new() { lv }; }
            else if (now - rgActAt > (pass.Count > 0 ? 60 : 180) || now - Math.Max(rgAway, rgActAt) >= 60)
            {
                // #21 reporting duty: first "report off", 30 s later without "off" freed as before plus violation
                if (offAsk < 0) { offAsk = now; return new() { R($"{Cs(me)}, Range Alpha, report off.") }; }
                if (now - offAsk >= 30)
                {
                    Debrief.Add(L("Verstoß: kein „off“ auf Range Alpha gemeldet", "Deviation: no \"off\" call on Range Alpha"));
                    var ao = R($"{Cs(me)}, Range Alpha, assuming you are off, range is cold{Ended(", ")}. Report in hot or checking out."); HotEdge(now); return new() { ao };   // HotEdge: pass end for the grace period (R58)
                }
            }
        }
        // R236: waiting for the range ("continue, one aircraft in") -> free: "cleared hot" if position and heading still fit, otherwise silent (new "in")
        if (waitHot && Range is { } zw && !hot.Any(o => o != this) && InHot(me, zw, now) is var ih && (isHot || isDry)) { HotEdge(now); return new() { R(ih) }; }
        var tk = tank >= 2 ? Tankers(air, me).FirstOrDefault(k => k.A.Group == tanker) : null;
        if (tk != null && Dist(t.X, t.Z, tk.A.X, tk.A.Z) > 10 * NM) { tank = 0; OffLine(); tk = null; }   // away from the tanker
        if (tk != null && TankerCoach(me, t, tk, now) is { } tc) return new() { tc };
        return awacs && AwacsAuto(me, t, air, now) is { } call ? new() { call } : new();
    }

    // Tanker coaching and turn call (V18), roughly from the positions at one-second rate
    double coachAt = -99, cAlong, cLat, cVert, cT = -99, stillSince, tkRef = double.NaN, tkPrev, tkStable, breakAt = -99;
    bool turning;
    static readonly Dictionary<string, (double At, bool Right)> tkTurn = new();   // last turn call per tanker (A121): one call to all, not per receiver
    public static int Breakaways;   // counters for the debrief (P3-TK3)
    Call? TankerCoach(Me me, Telemetry t, Tk tk, double now)
    {
        var c = Cs(me);
        var a = tk.A;
        Call Say(string s, bool all = false) { coachAt = now; return new("Tanker", tk.Name, s, All: all); }
        // Turn: heading > 15° away from the last straight heading -> call it; 5 s straight (≤ 1°/s) -> new reference
        double h = a.Hdg * 180 / Math.PI;
        if (double.IsNaN(tkRef)) { tkRef = tkPrev = h; tkStable = now; }
        if (HdgDiff(h, tkPrev) > 1) tkStable = now;
        tkPrev = h;
        string? turn = null;
        if (now - tkStable >= 5) { tkRef = h; turning = false; }
        else if (!turning && HdgDiff(h, tkRef) > 15)
        {
            turning = true;
            bool right = ((h - tkRef) % 360 + 540) % 360 - 180 > 0;
            // if another receiver already called the same turn (< 20 s, same direction), stay silent
            if (!tkTurn.TryGetValue(tk.A.Group, out var last) || last.Right != right || now - last.At > 20)
            { tkTurn[tk.A.Group] = (now, right); turn = $"{tk.Name}, turning {(right ? "right" : "left")}."; }
        }
        // Position in the tanker frame: longitudinal (behind negative), lateral, altitude
        double fx = Math.Cos(a.Hdg), fz = Math.Sin(a.Hdg), rx = t.X - a.X, rz = t.Z - a.Z;
        double along = rx * fx + rz * fz, lat = rz * fx - rx * fz, vert = t.AltMsl - a.AltMsl, dt = now - cT;
        var (pa, pl, pv) = (cAlong, cLat, cVert);
        (cAlong, cLat, cVert, cT) = (along, lat, vert, now);
        if (dt <= 0 || dt > 3) { stillSince = now; return turn != null ? Say(turn, true) : null; }
        double closure = (along - pa) / dt, wobble = Math.Max(Math.Abs(lat - pl), Math.Abs(vert - pv)) / dt;
        if (Math.Abs(closure) > 0.5) stillSince = now;
        // Breakaway (P3-TK3, ATP-56(C)): too close to the tanker, closure too fast, or in contact out of the envelope; repeated every 15 s, priority over everything else
        if (tank >= 3 && now - breakAt >= 15 && Math.Abs(lat) < 30 && Math.Abs(vert) < 30 && along < 40 &&
            ((along > -20 && Math.Abs(lat) < 20 && Math.Abs(vert) < 20) || (closure > 6 && along > -50) || (tank == 4 && (Math.Abs(lat) > 25 || Math.Abs(vert) > 25))))
        { breakAt = now; Breakaways++; if (tank == 3) tank = 2; return Say($"{c}, breakaway, breakaway, breakaway."); }   // R289: back to observation, contact only after a new clearance (in contact: with the disconnect)
        if (turn != null) return Say(turn, true);
        if (tank == 4 || along > 0 || along < -300 || Math.Abs(lat) > 60 || now - coachAt < 8) return null;
        if (closure > 3 && along > -150) return Say($"{c}, slow closure.");   // > 6 kt
        if (wobble > 1.5 && along > -80) return Say($"{c}, stabilize.");
        // ponytail: contact point flat 45 m behind the tanker center, more exact only with a type table; basket is not steered (A127)
        double off = -along - 45;
        if (tank == 3 && Boom(a) && off is >= 3 and <= 30 && now - stillSince > 8) return Say($"{c}, forward {Math.Max(5, Math.Round(off / Ft / 5) * 5):0}.");
        return null;
    }

    // N44: protect the tanker block. Without check-in (tank 0) within 3 NM of an own tanker and within ±1000 ft of its altitude: warned off at most every 3 min.
    // Not: was checked in and still in the block (after "complete"), wingmen of a checked-in lead, join from behind with tanker heading
    // (DCS-ATC does not see the check-in via the DCS radio menu; ponytail: an unannounced join from behind therefore stays silent too)
    double blockAt = -1e9;
    bool tkOk;   // was checked in (tank > 0), until no tanker is in the block any more
    double jwAt = -1;   // #20: time of the "not cleared to join" warning (-1 = none, infinity = violation recorded)
    Call? TankerBlock(Me me, IReadOnlyList<Traffic> air, double now, Ops? lead)
    {
        var t = me.Tel;
        // #20 reporting duty: join requested (tank 1) but without "visual" -> "cleared to join" below 1 NM and ±500 ft of the tanker: one warning, violation if still there 30 s later
        if (tank != 1) jwAt = -1;
        else if (t != null && !OnGround(t) && Tankers(air, me).FirstOrDefault(k => k.A.Group == tanker) is { } tj
                 && Dist(t.X, t.Z, tj.A.X, tj.A.Z) < NM && Math.Abs(t.AltMsl - tj.A.AltMsl) < 500 * Ft)
        {
            if (jwAt < 0) { jwAt = now; return new("Tanker", tj.Name, $"{Cs(me)}, {tj.Name}, not cleared to join, remain 1000 feet below, report visual."); }
            if (now - jwAt >= 30) { jwAt = double.PositiveInfinity; Debrief.Add(L($"Verstoß: Annäherung an {tj.Name} ohne Freigabe", $"Deviation: approached {tj.Name} without clearance")); }
        }
        if (tank != 0) tkOk = true;
        if (tank != 0 || t == null || OnGround(t)) return null;
        var a = air.Where(k => k.InAir && (k.Coalition == me.Coalition || k.Coalition == 0) && Regex.IsMatch(k.Type, "KC|Tanker|IL-78")
                               && Dist(t.X, t.Z, k.X, k.Z) < 3 * NM && Math.Abs(t.AltMsl - k.AltMsl) < 1000 * Ft)
                      .OrderBy(k => Dist(t.X, t.Z, k.X, k.Z)).FirstOrDefault();
        if (a == null) { tkOk = false; return null; }
        bool astern = (t.X - a.X) * Math.Cos(a.Hdg) + (t.Z - a.Z) * Math.Sin(a.Hdg) < 0 && HdgDiff(t.Hdg * 180 / Math.PI, a.Hdg * 180 / Math.PI) < 45;
        if (tkOk || lead != null && (lead.tank != 0 || lead.tkOk) || astern || now - blockAt < 180) return null;
        blockAt = now;
        var n = Tankers(air, me, true).FirstOrDefault(k => k.A == a)?.Name ?? TankerName(a);   // R240: full callsign
        Debrief.Add(L($"Verstoß: Annäherung an {n} ohne Freigabe", $"Deviation: approached {n} without clearance"));
        return new("Tanker", n, $"{Cs(me)}, {n}, you are not cleared to join, remain 1000 feet below, report ready for rejoin.");
    }

    /// Real contact/disconnect at the tanker (DCS event S_EVENT_REFUELING/_STOP via the mission), tank 0–1, kgMax = internal tank in kg (0 = unknown).
    /// DCS also reports the disconnect on every slip off the basket: it is wanted only with a (nearly) full tank, otherwise back to pre-contact (A66).
    double fuelAtContact, contactSum, rowLb, lastStop = -99;
    double ncAt = -1e9;   // #19: last contact without clearance
    bool broken;      // last disconnect unintended: contact shortly afterwards continues the same sequence
    string? dbText;   // Debriefing line of this sequence, each disconnect replaces it
    public List<Call> OnRefuel(Me me, bool start, double fuel, IReadOnlyList<Traffic> air, double now, double kgMax = 0)
    {
        var tkr = (tanker != "" ? Tankers(air, me, true).FirstOrDefault(k => k.A.Group == tanker) : null) ?? Tankers(air, me).FirstOrDefault();
        var name = tkr?.Name ?? "Tanker";
        if (start)
        {
            if (tank == 4) return new();
            if (tank != 3)   // #19 reporting duty: contact only after "pre-contact" -> "cleared contact" (ATP-56(C)); at most every 30 s (DCS reports every hookup)
            {
                if (now - ncAt < 30) return new();
                ncAt = now;
                Debrief.Add(L($"Verstoß: Kontakt an {name} ohne Freigabe", $"Deviation: contact with {name} without clearance"));
                // uncertain: another receiver is already on this tanker -> breakaway (ATP-56(C)) instead of orderly disconnect
                if (line.GetValueOrDefault(tkr?.A.Group ?? "")?.Any(o => o != this && o.tank == 4) == true) { Breakaways++; return new() { new("Tanker", name, $"{Cs(me)}, breakaway, breakaway, breakaway.") }; }
                return new() { new("Tanker", name, $"{Cs(me)}, {name}, you are not cleared contact, disconnect, return to left observation.") };
            }
            bool again = broken && tank >= 2 && now - lastStop < 120;   // back on the basket: no new "Contact.", quantity and time continue
            tank = 4; contactAt = now; broken = false;
            if (again) return new();
            fuelAtContact = fuel; contactSum = 0; dbText = null; rowLb = 0;
            return new() { new("Tanker", name, "Contact.") };
        }
        if (tank != 4) return new();
        contactSum += now - contactAt;
        double lb = Math.Round(kgMax * Lb * (fuel - fuelAtContact) / 100) * 100;
        if (dbText == null) tkContacts++;   // N39: one transaction per contact; continuation after a slip does not count anew
        offload += lb - rowLb; tkLb += lb - rowLb; rowLb = lb;   // continuation after a slip replaces the quantity of this transaction
        var row = L("Luftbetankung", "Air refueling") + $" {name}: {contactSum:0} s " + L("Kontakt", "contact") + $", " + L("Tank", "fuel") + $" {fuelAtContact * 100:0} -> {fuel * 100:0} %{(lb > 0 ? $", {lb:0} lb" : "")}";
        if (dbText != null && Debrief.IndexOf(dbText) is var i and >= 0) Debrief[i] = row; else Debrief.Add(row);
        dbText = row; lastStop = now;
        if (fuel < 0.97 && breakAt >= contactAt) { tank = 2; broken = false; return new(); }   // R289: disconnect after breakaway: observation, no "return to pre-contact", contact only after a new clearance
        if (fuel < 0.97)   // unwanted, stays in the queue (ponytail: desired quantity only with N37)
        {
            tank = 3; broken = true;
            return new() { new("Tanker", name, $"{Cs(me)}, {name}, disconnect, return to pre-contact.") };
        }
        tank = 2; broken = false;
        OffLine();
        var next = line.GetValueOrDefault(tkr?.A.Group ?? "")?.FirstOrDefault(o => o.tank >= 2);
        var done = new Call("Tanker", name, $"{Cs(me)}, {name}, disconnect{(lb > 0 ? $", you received {lb / 1000:0.0}" : "")}, move to the right wing.{(next == null ? " Report complete, or pre-contact for more." : "")}");   // R288: "complete" first (Enter suggestion: first option), full is full
        return next == null ? new() { done } : new() { done, new("Tanker", name, $"{next.cs}, {name}, cleared pre-contact.", To: next) };   // A128: the next one gets its own call
    }

    // ======================================================================= Self-test
    public static int SelfTest(Action<bool, string> check)
    {
        Abm.Reset();
        var (idSec, awBulls, fadeSec, scanSec) = (IdSec, AwacsBullseye, FadeSec, ScanSec); (IdSec, AwacsBullseye, FadeSec, ScanSec) = (0, false, 1e9, 0);   // older tests expect "hostile" immediately (LK1), the BRAA fallback (LK4) and no "faded" (LK6); the LK tests check those
        var me = new Me("Enfield 1-1", new Telemetry(6000, 6000, 200, 0, 0, 0, 0, 0, 0), "FA-18C_hornet", 2);
        var air = new List<Traffic>
        {
            new(1, "MiG-29A", 20 * NM, 0, 6100, Math.PI, 250, "Red 1", "", true, 1),                 // 20 NM north, coming toward us
            new(2, "KC135MPRS", 0, 30 * NM, 6700, 0, 140, "Texaco (KC-135MPRS Korb)", "", true, 2),
            new(3, "KC-135", 0, -30 * NM, 6700, 0, 140, "Shell (KC-135 Boom)", "", true, 2),
            new(4, "E-3A", -60 * NM, 0, 9000, 0, 150, "Overlord", "", true, 2),
            new(5, "Su-27", 35 * NM, 10 * NM, 8000, 0, 250, "Red 2", "", true, 1),                    // 36 NM, flies away
        };
        Callsigns = new Dictionary<string, string> { ["Texaco (KC-135MPRS Korb)"] = "Texaco11", ["Shell (KC-135 Boom)"] = "Shell21" };   // call sign from the DCS call sign (A69)
        Beacons["Texaco (KC-135MPRS Korb)"] = "52X";
        Bulls[0] = (0, 0); net.Clear();
        Range = (5 * NM, 0, 2500);   // R14: 5 NM in front of the player (cleared hot only within 10 NM)
        var o = new Ops();
        var f = new List<Airfield> { Airfield.Kutaisi() };
        string Say(string role, string text) => string.Join(" | ", o.OnTranscript(role, text, me, air, f, 0).Select(x => $"{x.Station}: {x.Text}"));
        var pic = Say("AWACS", "Overlord, Enfield 1-1, request picture");
        check(pic.StartsWith("Overlord: Enfield one one, Overlord, threat, lead group BRAA three five four, 20, 20 thousand, hot") && pic.Contains(". Picture, 2 groups") && pic.Contains("Fulcrum"),
              "AWACS: Bild mit 2 Gruppen, AW-P3 hot MiG 20 NM vorweg als Threat mit Namen aus dem Picture (R54) -> " + pic);
        // N34: AwacsBullseye off: BRAA from the player, on: bullseye (Bulls[0] is at 0/0)
        check(pic.Contains("BRAA") && !pic.Contains("bullseye"), "N34: Picture ohne AwacsBullseye BRAA -> " + pic);
        AwacsBullseye = true; var picB = Say("AWACS", "Overlord, Enfield 1-1, request picture"); AwacsBullseye = false;
        var picBr = picB[(picB.IndexOf("Picture") + 1)..];   // Threat (AW-P3) stays BRAA
        check(picB.Contains("threat, lead group BRAA") && picBr.Contains("bullseye") && !picBr.Contains("BRAA"), "N34: Picture mit AwacsBullseye -> " + picB);
        var bd = Say("AWACS", "bogey dope");
        check(bd.Contains("hot") && bd.Contains("20 thousand") && !bd.Contains("angels"), "AWACS: BRAA hot -> " + bd);
        check(Thousand(6096) == "20 thousand" && Thousand(150) == "5 hundred" && Thousand(10) == "1 hundred" && Thousand(2130) == "7 thousand" && Angels(6096) == "angels 20", "R52 Feindhöhe thousand/hundred, Angels bleibt für Tanker");
        var t1 = string.Join(" | ", new[] { 1, 2, 3 }.SelectMany(s => o.Tick(me, air, s)).Select(x => x.Text));
        check(t1 == "", "R50/A55: nach Picture und Bogey dope kein zweites radar contact, keine Threat zur eben genannten MiG -> " + t1);
        var og = new Ops();   // R51: on the ground "check in airborne", no check-in (no calls after takeoff), then radar contact in the air
        var g0 = og.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me with { Tel = me.Tel! with { Agl = 0 } }, air, f, 0);
        var g1 = og.Tick(me, air, 1).Concat(og.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, air, f, 2)).Select(x => x.Text).ToList();
        check(g0 is [{ Text: "Enfield one one, Overlord, check in airborne." }] && g1.Count == 1 && g1[0].Contains("Radar contact, picture, 2 groups") && og.Tick(me, air, 3).Count == 0,
              $"R51: Check-in am Boden -> {g0.FirstOrDefault()?.Text} | {string.Join(" | ", g1)}");
        // R50: check-in in the air exactly one "radar contact"; bogey dope without groups "clean", afterwards no "radar contact, picture clean"
        var oc = new Ops();
        int rc = oc.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, air, f, 0).Concat(new[] { 1, 2, 3, 4, 5 }.SelectMany(s => oc.Tick(me, air, s))).Count(x => x.Text.Contains("adar contact"));   // after the threat (AW-P3) "Radar contact"
        var blue = air.Where(a => a.Coalition != 1).ToList();
        var ob = new Ops();
        var bc = ob.OnTranscript("AWACS", "Overlord, Enfield 1-1, bogey dope", me, blue, f, 0).Concat(ob.Tick(me, blue, 1)).Concat(ob.Tick(me, blue, 2)).Select(x => x.Text).ToList();
        var ol = new Ops();   // checked in low after takeoff (handover "leaving control zone, contact Overlord" below 900 m): no second one while climbing
        int rl = ol.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me with { Tel = me.Tel! with { Agl = 300 } }, air, f, 0).Concat(ol.Tick(me, air, 1)).Count(x => x.Text.Contains("adar contact"));
        check(rc == 1 && rl == 1 && bc is ["Enfield one one, Overlord, clean."], $"R50: Check-in {rc}x/tief {rl}x radar contact, bogey dope -> {string.Join(" | ", bc)}");
        // Forum log 0.9.4 (awacsdup): Approach hands over to Overlord below 2200 ft, low check-in with bullseye and 2 groups around 90 NM, then climb above 900 m: exactly one radar contact/picture
        var awFl = new List<Traffic> { air[3], new(11, "MiG-29A", 88 * NM, 20 * NM, 8500, 1.25 * Math.PI, 250, "Red 11", "", true, 1),
                                     new(12, "MiG-29A", 88.5 * NM, 20 * NM, 8500, 1.25 * Math.PI, 250, "Red 11", "", true, 1), new(13, "MiG-29A", 80 * NM, -15 * NM, 8500, 1.25 * Math.PI, 250, "Red 12", "", true, 1) };
        Me AwUp(double s) => me with { Tel = me.Tel! with { Agl = 610 + 40 * s, AltMsl = 660 + 40 * s } };
        var awOd = new Ops(); AwacsBullseye = true;
        var awDup = awOd.OnTranscript("AWACS", "Overlord and Fig 1-1, checking in.", AwUp(0), awFl, f, 0)
                    .Concat(Enumerable.Range(1, 60).SelectMany(s => awOd.Tick(AwUp(s), awFl, s))).Select(x => x.Text).ToList();
        AwacsBullseye = false;
        check(awDup.Count == 1 && awDup[0].Contains("radar contact, picture, 2 groups") && awDup[0].Contains("bullseye"), "awacsdup: tief eingecheckt, beim Steigen kein zweites radar contact/picture -> " + string.Join(" | ", awDup));
        // Counter case: misheard call on the AWACS frequency while taxiing ("Request taxi to runway." -> R51 check in airborne) is not a check-in, no unsolicited radar contact after takeoff
        var awTx = new Ops();
        var awSa = awTx.OnTranscript("AWACS", "Request taxi to runway.", me with { Tel = me.Tel! with { Agl = 0, Ias = 8 } }, awFl, f, 0).Select(x => x.Text)
                   .Concat(Enumerable.Range(1, 60).SelectMany(s => awTx.Tick(AwUp(s), awFl, s)).Select(x => x.Text)).ToList();
        check(awSa is ["Enfield one one, Overlord, check in airborne."], "awacsdup: Verhörer beim Rollen (R51: check in airborne) kein Check-in -> " + string.Join(" | ", awSa));
        // A108: radio check with station name and "read you five"; A63: no check-in hint as text any more (user: radio only when you are on the frequency)
        var rcOps = new Ops();
        check(rcOps.OnTranscript("Range", "Range Alpha, Enfield 1-1, radio check", me, air, f, 0) is [{ Text: "Enfield one one, Range Alpha, read you five." }],
              "A108: Radio check Range -> " + Say("Range", "radio check"));
        var oh = new Ops();
        string Hint(double s) => string.Join("|", oh.Tick(me, air, s).Where(x => x.Role == "Info").Select(x => x.Text));
        var aw1 = Hint(0); var aw2 = Hint(119); var aw3 = Hint(121); var aw4 = Hint(300);
        check(aw1 == "" && aw2 == "" && aw3 == "" && aw4 == "", $"A63: kein Check-in-Hinweis -> {aw3}");
        var ohi = new Ops(); ohi.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, air, f, 0);
        check(new[] { 0, 130 }.SelectMany(s => ohi.Tick(me, air, s)).All(x => x.Role != "Info"), "A63: eingecheckt -> kein Hinweis");
        var closer = air.Select(a => a.Id == 1 ? a with { X = 8 * NM } : a).ToList();   // MiG closes to 8 NM: new stage -> report again
        check(o.Tick(me, closer, 4).FirstOrDefault()?.Text.Contains("threat") == true, "AWACS: Bedrohung näher (8 NM) erneut gemeldet");
        check(o.Tick(me, closer, 20).Count == 0 && o.Tick(me, closer, 50).FirstOrDefault()?.Text.Contains("threat") == true, "AWACS: nah und hot: alle 45 s");
        var calm = air.Where(a => a.Coalition != 1).ToList();
        check(o.Tick(me, calm, 60).Count == 0 && o.Tick(me, calm, 200).FirstOrDefault()?.Text.Contains("picture clean") == true, "AWACS: picture clean");
        var pop = new List<Traffic>(calm) { new(6, "MiG-21Bis", 0, 30 * NM, 5000, 0, 250, "Red 3", "", true, 1) };
        // R309: 21 s without telemetry (mission data briefly missing) does not clear the check-in, 2 min does
        var og2 = new Ops(); og2.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, calm, f, 0);
        foreach (var s in new[] { 1, 2, 10, 22 }) og2.Tick(me with { Tel = null }, calm, s);
        check(og2.AwacsIn && og2.Tick(me, new List<Traffic>(calm) { new(66, "MiG-21Bis", 0, 80 * NM, 5000, 0, 250, "Red 9", "", true, 1) }, 23).FirstOrDefault()?.Text.Contains("new group") == true, "R309: Datenlücke löscht AWACS-Check-in");
        foreach (var s in new[] { 30, 100, 160 }) og2.Tick(me with { Tel = null }, calm, s);
        check(!og2.AwacsIn, "R309: 2 min ohne Telemetrie -> Check-in weg");
        check(o.Tick(me, pop, 201).FirstOrDefault()?.Text.Contains("new group") == true, "AWACS: neue Gruppe");
        check(o.Tick(me, calm, 330).FirstOrDefault()?.Text.Contains("picture clean") == true && o.Tick(me, pop, 331).FirstOrDefault()?.Text.Contains("new group") == true &&
              o.Tick(me, calm, 460).FirstOrDefault()?.Text.Contains("picture clean") == true && o.Tick(me, calm, 600).Count == 0,
              "AW-P3: nach picture clean kommt die Gruppe wieder als neu, picture clean je Meldung einmal");
        // V20: commit (hot ≤ 40 NM, no threat yet), spike, sort
        Abm.Reset();   // LK9: forget the side's assignments from the preceding tests (same flight name)
        var o20 = new Ops();
        o20.OnTranscript("AWACS", "Overlord, Enfield 1-1, on station", me, air, f, 0);   // LK5: commit only with a CAP tasking
        var hot = air.Where(a => a.Id != 1).Select(a => a.Id == 5 ? a with { Hdg = Math.Atan2(-10, -35) + 2 * Math.PI } : a).ToList();
        var cm = o20.Tick(me, hot, 2).FirstOrDefault()?.Text ?? "-";
        check(cm.Contains("Enfield one one, Overlord, commit trail group, BRAA") && cm.EndsWith(", hot.") && o20.Tick(me, hot, 3).Count == 0, "AWACS V20/R54: commit mit Namen aus dem Picture (LK2: Zusätze hat das Picture schon genannt) -> " + cm);
        // LK5: only checked in (no "on station"): no commit at 36 NM, only the threat at 30 NM; "targeting single group" -> AWACS confirms with BRAA, afterwards no threat to it
        Abm.Reset();   // o20 is the same flight (Enfield one) and has the group
        var o5 = new Ops();
        o5.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, air, f, 0);
        o5.Tick(me, air, 1);
        var hot30 = hot.Select(a => a.Id == 5 ? a with { X = 28 * NM, Z = 8 * NM } : a).ToList();
        var lk5 = new[] { Tn(o5, hot, 2), Tn(o5, hot30, 3) };
        var o5b = new Ops();
        o5b.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, hot, f, 0);
        var tg5 = o5b.OnTranscript("AWACS", "Overlord, Enfield 1-1, targeting single group", me, hot, f, 1)[0].Text;
        check(lk5[0] == "-" && lk5[1].StartsWith("Enfield one one, Overlord, threat, trail group BRAA") && tg5.StartsWith("Enfield one one, Overlord, single group BRAA zero one zero") && Tn(o5b, hot30, 2) == "-",
              $"LK5 commit nur on station, targeting bestätigt: {string.Join(" | ", lk5)} | {tg5}");
        // V10: split, combined, pop-up, low
        var e3 = air.Where(a => a.Type == "E-3A").ToList();
        Traffic Mig(int id, double x, double z, double alt = 6000) => new(id, "MiG-21Bis", x, z, alt, 0, 250, "Red " + id, "", true, 1);
        var one = e3.Concat(new[] { Mig(41, 50 * NM, 0), Mig(42, 50 * NM, NM) }).ToList();
        var two = e3.Concat(new[] { Mig(41, 50 * NM, 0), Mig(42, 50 * NM, 10 * NM) }).ToList();
        var o10 = new Ops();
        o10.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, one, f, 0);
        o10.Tick(me, one, 1);
        string T10(List<Traffic> l, double s) => o10.Tick(me, l, s).FirstOrDefault()?.Text ?? "-";
        // A56: split/combined only after 12 s stable, per group at most every 60 s; pop-up only on first detection
        var sp10 = T10(two, 2) == "-" ? T10(two, 14) : "zu früh"; var cb10 = T10(one, 15) == "-" ? T10(one, 75) : "zu früh";
        var pu43 = one.Append(Mig(43, 20 * NM, 0, 200)).ToList();
        var pu10 = T10(pu43, 76);
        check(sp10.Contains("Overlord, group split, 2 groups azimuth 10, east group BRAA") && sp10.Contains(", west group BRAA") && !sp10.Contains("new group") && cb10.Contains("groups combined, BRAA") && cb10.Contains("two contacts") &&
              pu10.Contains("pop-up group, BRAA") && pu10.Contains("single, low") && T10(one, 77) == "-" && T10(pu43, 78) == "-", $"AWACS V10: {sp10} | {cb10} | {pu10}");
        // AW-P1: new group immediately up to NewGroupNm, once (also after dropout and lead shot down); threat at 25 NM not repeated after 3 min; A56 element missing for one round
        net.Clear();
        string Tn(Ops o, List<Traffic> l, double s) => o.Tick(me, l, s).FirstOrDefault()?.Text ?? "-";
        Ops In(List<Traffic> l, Me m) { var o = new Ops(); o.OnTranscript("AWACS", "Overlord, checking in", m, l, f, 0); o.Tick(m, l, 1); return o; }
        var g55 = e3.Concat(new[] { Mig(51, 55 * NM, 0), Mig(52, 55 * NM, NM) }).ToList();
        var only52 = g55.Where(a => a.Id != 51).ToList();
        var on = In(e3, me);
        var n55 = new[] { Tn(on, g55, 2), Tn(on, g55, 3), Tn(on, only52, 4), Tn(on, g55, 5), Tn(on, only52, 6), Tn(on, only52, 120) };   // 120: over 2 min without detection it would be new (AW-P3)
        check(n55[0].Contains("new group, BRAA three five four, 55") && n55.Skip(1).All(s => s == "-"), "AWACS: Gruppe bei 55 NM einmal -> " + string.Join(" | ", n55));
        var h25 = e3.Concat(new[] { Mig(53, 25 * NM, 0) with { Hdg = Math.PI }, Mig(54, 25 * NM, NM) with { Hdg = Math.PI } }).ToList();
        var ot = In(e3, me);
        var th = new[] { Tn(ot, h25, 2), Tn(ot, h25, 100), Tn(ot, h25, 182), Tn(ot, h25.Where(a => a.Id != 53).ToList(), 183) };   // 100: continuously detected (AW-P3)
        check(th[0].Contains("threat, single group BRAA") && th.Skip(1).All(s => s == "-"), "AWACS: Threat 25 NM nicht wiederholt, Lead-Wechsel still (LK3: einzige Gruppe = single group) -> " + string.Join(" | ", th));
        // LK2: follow-up call only name, BRAA, altitude, aspect (≤ 16 words, about 5 s); extras only when something changes (after the kill "single"); AWACS voice brisk
        var olk2 = In(e3, me);
        Traffic Hot(Traffic a, double nm) => a with { X = nm * NM, Hdg = Math.PI };
        var lk2 = new[] { Tn(olk2, h25, 2), Tn(olk2, h25.Select(a => a.Id > 50 ? Hot(a, 15) : a).ToList(), 3), Tn(olk2, h25.Where(a => a.Id != 54).Select(a => a.Id > 50 ? Hot(a, 8) : a).ToList(), 4) };
        check(lk2[0].EndsWith("25, 20 thousand, hot, hostile, two contacts, Fishbed.") && lk2[1] == "Enfield one one, Overlord, threat, single group BRAA three five four, 15, 20 thousand, hot." &&
              lk2[1].Split(' ').Length <= 16 && lk2[2].EndsWith(", 8, 20 thousand, hot, single.")
              && Program.PaceOf(new Tx("x", "AWACS", "Overlord", 0)) < 1 && Program.PaceOf(new Tx("x", "Tower", "Kutaisi Tower", 0)) == 1 && Program.PaceOf(new Tx("x", "AWACS", "Enfield 1-1", 0, true)) == 1,
              "LK2 Zusätze nur beim ersten Mal/bei Änderung: " + string.Join(" | ", lk2));
        var ch = e3.Concat(new[] { Mig(61, 50 * NM, 0), Mig(62, 50 * NM, 2.5 * NM), Mig(63, 50 * NM, 5 * NM) }).ToList();
        var gap = ch.Where(a => a.Id != 62).ToList();
        var oc56 = In(ch, me);
        var a56 = new[] { Tn(oc56, gap, 2), Tn(oc56, ch, 3), Tn(oc56, gap, 4), Tn(oc56, ch, 5) };
        check(a56.All(s => s == "-"), "A56: Glied fehlt eine Runde, kein split/combined -> " + string.Join(" | ", a56));
        // AW-P3: after picture clean the group is new again (≤ 30 NM pop-up); not detected for over 2 min -> new again, shorter not; report radius NewGroupNm
        net.Clear();
        var p20 = e3.Append(Mig(74, 20 * NM, 0)).ToList();   // drag: no threat
        var op3 = In(p20, me);
        var far = e3.Append(Mig(74, 220 * NM, 0)).ToList();   // R53: still detected but outside NewGroupNm: no picture clean, only when it has been completely gone for 2 min
        var r3 = new[] { Tn(op3, p20, 2), Tn(op3, far, 70), Tn(op3, far, 130), Tn(op3, e3, 200), Tn(op3, e3, 330), Tn(op3, p20, 331) };
        check(r3[..4].All(s => s == "-") && r3[4].EndsWith("picture clean.") && r3[5].Contains("pop-up group, BRAA three five four, 20"), "R53/AW-P3 nach picture clean: " + string.Join(" | ", r3));
        // R54: names from the picture in the auto calls ; R239: split names both parts by position ("west group split, 2 groups azimuth 6, east group …, west group …"), another "east group" loses the name
        var az = e3.Concat(new[] { Mig(81, 50 * NM, -10 * NM), Mig(83, 50 * NM, -9 * NM), Mig(82, 50 * NM, 10 * NM) }).ToList();
        var azS = az.Select(a => a.Id == 83 ? a with { Z = -4 * NM } : a).ToList();
        var azT = azS.Select(a => a.Id == 82 ? a with { X = 20 * NM, Z = 2 * NM, Hdg = Math.PI } : a.Id == 83 ? a with { X = 20 * NM, Z = -2 * NM, Hdg = Math.PI } : a).ToList();
        var o54 = new Ops();
        var p54 = o54.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, az, f, 0)[0].Text; o54.Tick(me, az, 1);
        var r54 = new[] { Tn(o54, azS, 2), Tn(o54, azS, 14), Tn(o54, azT, 15), Tn(o54, azT, 16) };
        check(p54.Contains("2 groups azimuth") && r54[0] == "-" && r54[1].StartsWith("Enfield one one, Overlord, west group split, 2 groups azimuth 6, east group BRAA") && r54[1].Contains(", west group BRAA") &&
              Regex.IsMatch(r54[2], "^Enfield one one, Overlord, threat, (north|south)\\w* group BRAA") && r54[3].StartsWith("Enfield one one, Overlord, threat, east group BRAA"), "R54 (LK3: die umbenannte Gruppe bekommt einen freien Namen statt \"group\"): " + string.Join(" | ", r54));
        var m291 = Tn(o54, azT.Select(a => a.Id == 83 ? a with { X = NM, Z = 0 } : a).ToList(), 17);
        check(m291 == "Enfield one one, Overlord, east group merged.", "R291: merged mit Gruppenname -> " + m291);
        var g50 = e3.Concat(new[] { Mig(72, 50 * NM, 0), Mig(73, 50 * NM, 20 * NM) }).ToList();
        var only73 = g50.Where(a => a.Id != 72).ToList();
        var ob3 = In(e3, me);
        var r4 = new[] { Tn(ob3, g50, 2), Tn(ob3, g50, 3), Tn(ob3, only73, 60), Tn(ob3, g50, 100), Tn(ob3, only73, 101), Tn(ob3, only73, 170), Tn(ob3, only73, 230), Tn(ob3, g50, 231) };
        check(r4[0].Contains("new group, BRAA three five four, 50") && r4[1].Contains("new group") && r4[2..7].All(s => s == "-") && r4[7].Contains("new group, BRAA three five four, 50"),
              "AW-P3 über 2 min weg -> wieder neu: " + string.Join(" | ", r4));
        var g90 = e3.Append(Mig(75, 180 * NM, 0)).ToList();
        var n90 = Tn(In(e3, me), g90, 2);
        net.Clear(); NewGroupNm = 60;
        var n90b = Tn(In(e3, me), g90, 2);
        NewGroupNm = 200;
        check(n90.Contains("new group, BRAA three five four, 180") && n90b == "-", $"AW-P3 Gruppe bei 180 NM: Standard 200 -> {n90} | 60 -> {n90b}");
        // A64: two players on the same AWACS frequency hear the new group and picture clean only once
        net.Clear();
        var meB = me with { Callsign = "Dodge 1-1" };
        var (pa, pb) = (In(e3, me), In(e3, meB));
        var a64 = pa.Tick(me, g55, 2).Concat(pb.Tick(meB, g55, 2)).ToList();
        pb.OnTranscript("AWACS", "Overlord, bogey dope", meB, g55, f, 3);   // Dodge also heard the group: both would have picture clean
        a64 = a64.Concat(pa.Tick(me, e3, 130)).Concat(pb.Tick(meB, e3, 130)).ToList();
        check(a64.Count == 2 && a64[0].Text.StartsWith("Enfield one one, Overlord, new group") && a64[1] is { All: true, Text: "Overlord, picture clean." }, "A64: " + string.Join(" | ", a64.Select(x => x.Text)));
        // R118: merged only with altitude difference below 5000 ft (A113), otherwise no "merged" call
        var mHi = Tn(In(e3, me), e3.Append(Mig(76, 1 * NM, 0, 6000 + 9800 * Ft)).ToList(), 2);
        var mLo = Tn(In(e3, me), e3.Append(Mig(76, 1 * NM, 0, 6000 + 3300 * Ft)).ToList(), 2);
        check(!mHi.Contains("merged") && mLo == "Enfield one one, Overlord, merged.", $"R118: 1 NM, 9800 ft höher -> {mHi} | 3300 ft höher -> {mLo}");
        // A64 blocks only for others: alone, after a crash/new flight the group comes back; declare on neutrals without "picture clean" afterwards
        pa.Tick(me with { Tel = null }, g55, 131);
        pa.OnTranscript("AWACS", "Overlord, checking in", me, e3, f, 132); pa.Tick(me, e3, 133);
        var yak = e3.Append(new Traffic(9, "Yak-52", 5 * NM, 0, 1000, 0, 60, "Civil", "", true, 0)).ToList();
        var oy = In(e3, me);
        oy.OnTranscript("AWACS", "Overlord, declare", me, yak, f, 2);
        var (re, yc) = (Tn(pa, g55, 134), Tn(oy, yak, 300));
        check(re.Contains("new group") && yc == "-", $"A64 eigener Spruch / declare neutral: {re} | {yc}");
        net.Clear();
        // R238: SNAP with heading to the named group (name from the picture), without a name to the nearest
        var osn = new Ops(); osn.OnTranscript("AWACS", "Overlord, Enfield 1-1, request picture", me, air, f, 0);
        var (sn1, sn2) = (osn.OnTranscript("AWACS", "Overlord, Enfield 1-1, snap trail group", me, air, f, 1)[0].Text, osn.OnTranscript("AWACS", "Overlord, Enfield 1-1, snap", me, air, f, 2)[0].Text);
        check(sn1.StartsWith("Enfield one one, Overlord, snap zero one zero, trail group BRAA zero one zero,") && sn1.Contains("Flanker") && sn2.StartsWith("Enfield one one, Overlord, snap three five four, lead group BRAA three five four, 20,"),
              $"R238: snap -> {sn1} | {sn2}");
        var sp = Say("AWACS", "Overlord, Enfield 1-1, spiked 3 6 0");
        check(sp.Contains("spike three six zero, lead group BRAA") && sp.Contains("Fulcrum") /* LK3: name from the side's last picture (osn) */ && Say("AWACS", "spike 180").Contains("nothing known"), "AWACS V20: spike -> " + sp);
        var os = new Ops(); os.OnTranscript("AWACS", "Overlord, Enfield 1-1, request picture", me, air, f, 0);
        var so = string.Join("", os.OnTranscript("AWACS", "Overlord, Enfield 1-1, request target", me with { Flight = 4 }, air, f, 0).Select(x => x.Text));
        var so2 = string.Join("", new Ops().OnTranscript("AWACS", "Overlord, Enfield 1-1, request sort", me with { Flight = 2 }, air, f, 0).Select(x => x.Text));
        check(so.StartsWith("Enfield one one, Overlord, target ") && so.Contains(". Enfield one three, target ") && so.Contains("lead group BRAA") && so.Contains("trail group BRAA") &&
              so2.StartsWith("Enfield one one, Overlord, target lead group BRAA three five four") && !so2.Contains("one two"), $"R55: target je Element (LK3: Namen der Seite gelten auch für einen neuen Spieler) -> {so} | {so2}");
        // LK3: group names per side: alone "single group"; when a second joins, both get a name by position, used identically by all (second player, AI via GroupLabel)
        labels.Clear(); net.Clear();
        var l3a = e3.Append(Mig(91, 40 * NM, 0)).ToList();
        var l3b = l3a.Append(Mig(92, 40 * NM, 20 * NM)).ToList();
        var o3a = In(l3a, me);
        var lbl1 = GroupLabel(2, 91);
        var bd3a = o3a.OnTranscript("AWACS", "Overlord, Enfield 1-1, bogey dope", me, l3b, f, 2)[0].Text;
        var bd3b = new Ops().OnTranscript("AWACS", "Overlord, Dodge 1-1, bogey dope", meB with { Tel = me.Tel! with { Z = 30 * NM } }, l3b, f, 3)[0].Text;
        check(lbl1 == "single group" && bd3a.StartsWith("Enfield one one, Overlord, west group BRAA") && bd3b.StartsWith("Dodge one one, Overlord, east group BRAA") &&
              GroupLabel(2, 91) == "west group" && GroupLabel(2, 92) == "east group" && GroupLabel(1, 91) == null && labels.GetValueOrDefault(2)?.ContainsValue("single group") != true,
              $"LK3: Gruppennamen je Seite: {lbl1} | {bd3a} | {bd3b} | {GroupLabel(2, 91)}/{GroupLabel(2, 92)}");
        // LK1: newly detected "bogey" (with type), after the ID time own declaration to all "Overlord, single group, hostile.", then declare hostile; shot at own side: hostile immediately
        labels.Clear(); net.Clear(); firstSeen.Clear(); hostileAct.Clear(); IdSec = 40;
        var l1 = e3.Append(Mig(95, 60 * NM, 0)).ToList();
        var o1 = new Ops();
        var ci1 = o1.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, l1, f, 100)[0].Text;
        var t1a = Tn(o1, l1, 120);
        var dl1 = o1.Tick(me, l1, 141).FirstOrDefault();
        var dc1 = o1.OnTranscript("AWACS", "Overlord, Enfield 1-1, declare", me, l1, f, 142)[0].Text;
        var l1b = l1.Append(Mig(96, 60 * NM, 30 * NM)).ToList();
        var t1b = Tn(o1, l1b, 143);   // LD8: second group -> "single group" becomes "west group": picture instead of "new group"
        OnEvent("X;shot;Red 96;Red 96;1;R-27R;missile;aam;radar_semi_active;Enfield11;Enfield;0;0".Split(';'), 143);
        hostileAct[2].Add(96);   // test traffic has Id 96 instead of hash of the name
        var t1c = Tn(o1, l1b, 144);
        IdSec = 0;
        check(ci1.Contains("bogey, single, Fishbed") && t1a == "-" && dl1 is { All: true, Text: "Overlord, single group, hostile." } && dc1.Contains("hostile, single, Fishbed") &&
              t1b.StartsWith("Enfield one one, Overlord, picture, 2 groups") && t1b.Contains("bogey") && t1c == "Overlord, east group, hostile." && hostileAct[2].Contains("Red 96".GetHashCode()),
              $"LK1 bogey -> hostile: {ci1} | {t1a} | {dl1?.Text} | {dc1} | {t1b} | {t1c}");
        // LK4: default (AwacsBullseye on, mission with bullseye): picture and new group/split/picture clean to all with bullseye; threat, bogey dope, pop-up to the one flight in BRAA
        labels.Clear(); net.Clear(); AwacsBullseye = true;
        var l4 = e3.Concat(new[] { Mig(97, 25 * NM, 0) with { Hdg = Math.PI }, Mig(98, 60 * NM, 40 * NM) }).ToList();
        var o4 = new Ops();
        var p4 = o4.OnTranscript("AWACS", "Overlord, Enfield 1-1, request picture", me, l4, f, 0)[0].Text;
        o4.Tick(me, l4, 1);
        var bd4 = o4.OnTranscript("AWACS", "Overlord, Enfield 1-1, bogey dope", me, l4, f, 2)[0].Text;
        var n4 = o4.Tick(me, l4.Append(Mig(99, 90 * NM, -40 * NM)).ToList(), 3).FirstOrDefault();
        var u4 = o4.Tick(me, l4.Append(Mig(99, 90 * NM, -40 * NM)).Append(Mig(100, 15 * NM, 15 * NM)).ToList(), 4).FirstOrDefault();
        var c4 = o4.Tick(me, e3, 200).FirstOrDefault();
        AwacsBullseye = false;
        var p4b = p4[p4.IndexOf("Picture")..];
        check(p4.StartsWith("Enfield one one, Overlord, threat, ") && p4.Contains(" BRAA three five four, 25") && p4b.Contains("bullseye") && !p4b.Contains("BRAA") && p4b.Contains("track south")
              && bd4.Contains("group BRAA three five four") && n4 is { All: true } && n4.Text.StartsWith("Overlord, new group, bullseye") && n4.Text.Contains("track north")
              && u4 is { All: false } && u4.Text.StartsWith("Enfield one one, Overlord, pop-up group, BRAA") && c4 is { All: true, Text: "Overlord, picture clean." },
              $"LK4 Bullseye an alle, BRAA an einen: {p4} | {bd4} | {n4?.Text} | {u4?.Text} | {c4?.Text}");
        // LK6: reported group not detected for 30 s -> "Overlord, west group faded, last known bullseye …" to all; shot-down group silent; return = new group
        labels.Clear(); net.Clear(); (AwacsBullseye, FadeSec) = (true, 30);
        var l6 = e3.Concat(new[] { Mig(101, 60 * NM, -20 * NM), Mig(102, 60 * NM, 20 * NM) }).ToList();
        var o6 = new Ops();
        o6.OnTranscript("AWACS", "Overlord, Enfield 1-1, request picture", me, l6, f, 0); o6.Tick(me, l6, 1);
        var only102 = l6.Where(a => a.Id != 101).ToList();
        var f6 = new[] { Tn(o6, only102, 20), Tn(o6, only102, 40) };
        killed[102] = 50;
        var f6k = Tn(o6, e3, 90);
        var f6n = Tn(o6, l6.Where(a => a.Id != 102).ToList(), 91);
        (AwacsBullseye, FadeSec) = (false, 1e9); killed.Clear();
        check(f6[0] == "-" && f6[1] == "Overlord, west group faded, last known bullseye three three six, 63." && f6k == "-" && f6n.StartsWith("Overlord, new group, bullseye"),
              $"LK6 faded: {string.Join(" | ", f6)} | abgeschossen: {f6k} | wieder da: {f6n}");
        // LK9/LK7: AWACS assigns (Abm): 80 NM new group -> 38 NM commit (CAP) -> 28 NM no threat -> 19 NM short target update; second, free group 25 NM hot -> threat;
        // AI flight Ford one takes it -> picture "Ford one targeted", player wants it too -> skip it, no more threat for it; both gone -> "picture clean, reset", no clean afterwards
        Abm.Reset(); labels.Clear(); net.Clear();
        Traffic M9(int id, double x, double z) => Mig(id, x, z) with { Hdg = Math.Atan2(-z, -x) + 2 * Math.PI };   // hot on the player
        List<Traffic> W9(params Traffic[] m) => e3.Concat(m).ToList();
        var o9 = new Ops();
        o9.OnTranscript("AWACS", "Overlord, Enfield 1-1, on station", me, e3, f, 0); o9.Tick(me, e3, 1);
        var s9 = new[] { Tn(o9, W9(M9(111, 80 * NM, 0)), 2), Tn(o9, W9(M9(111, 38 * NM, 0)), 3), Tn(o9, W9(M9(111, 28 * NM, 0)), 4), Tn(o9, W9(M9(111, 19 * NM, 0)), 5),
                         Tn(o9, W9(M9(111, 15 * NM, 0), M9(112, 25 * NM, 10 * NM)), 6) };
        check(s9[0].Contains("new group") && s9[1].StartsWith("Enfield one one, Overlord, commit single group, BRAA three five four, 38, 20 thousand, hot") && s9[2] == "-"
              && s9[3] == "Enfield one one, Overlord, single group BRAA three five four, 19, 20 thousand, hot." && s9[4].StartsWith("Enfield one one, Overlord, threat, ") && Abm.Mine(2, "Enfield one", 111),
              "LK9 commit statt Threat, Ziel-Update, Threat nur für die freie Gruppe: " + string.Join(" | ", s9));
        var n112 = GroupLabel(2, 112)!;
        check(Abm.Want(2, "Ford one", new[] { 112 }, n112, 1, 7, post: false) == null, "LK7: Ford one nimmt " + n112);
        var w9 = W9(M9(111, 14 * NM, 0), M9(112, 20 * NM, 8 * NM));
        var pic9 = o9.OnTranscript("AWACS", "Overlord, Enfield 1-1, request picture", me, w9, f, 8)[0].Text;
        n112 = GroupLabel(2, 112)!;   // the picture renamed it (lead/trail)
        var sk9 = o9.OnTranscript("AWACS", $"Overlord, Enfield 1-1, targeting {n112}", me, w9, f, 8)[0].Text;
        var nt9 = Tn(o9, W9(M9(111, 12 * NM, 0), M9(112, 9 * NM, 4 * NM)), 9);
        var rs9 = new[] { Tn(o9, e3, 10), Tn(o9, e3, 200) };
        check(pic9.EndsWith("Trail group, BRAA zero one six, 22, 20 thousand, hot, hostile, single, Fishbed, Ford one targeted.") && !pic9.Contains("threat") && sk9 == $"Enfield one one, Overlord, skip it, {n112} Ford one targeted." && nt9 == "-"
              && rs9[0] == "Enfield one one, Overlord, picture clean, reset." && rs9[1] == "-",
              $"LK7 Deconfliction, skip it, reset: {pic9} | {sk9} | {nt9} | {string.Join(" | ", rs9)}");
        // LD3/LD14: commit -> "committing" without echo, "say again" repeats; commit without reply -> "how copy?" after 10 s, free after 25 s; unable -> copy
        Abm.Reset(); labels.Clear(); net.Clear();
        var oD14 = new Ops();
        oD14.OnTranscript("AWACS", "Overlord, Enfield 1-1, on station", me, e3, f, 300); oD14.Tick(me, e3, 301);
        var cD14 = Tn(oD14, W9(M9(141, 38 * NM, 0)), 302);
        var eD14 = oD14.OnTranscript("AWACS", "Overlord, Enfield 1-1, committing", me, W9(M9(141, 37 * NM, 0)), f, 305);
        var aD14 = oD14.OnTranscript("AWACS", "Overlord, Enfield 1-1, say again", me, W9(M9(141, 37 * NM, 0)), f, 306);
        check(cD14.Contains("commit single group") && eD14.Count == 0 && aD14.Count == 1 && aD14[0].Text == cD14 && Abm.Mine(2, "Enfield one", 141), $"LD3/LD14 kein Echo, say again: {cD14} | {eD14.Count} | {string.Join(" ", aD14.Select(x => x.Text))}");
        // LD3: "judy" (he leads himself) -> no reply, no more target updates; his "merged" -> none from the AWACS
        var jD3 = oD14.OnTranscript("AWACS", "Overlord, Enfield 1-1, judy", me, W9(M9(141, 30 * NM, 0)), f, 310);
        var uD3 = new[] { Tn(oD14, W9(M9(141, 19 * NM, 0)), 320), Tn(oD14, W9(M9(141, 9 * NM, 0)), 330) };
        var mD3 = oD14.OnTranscript("AWACS", "Overlord, Enfield 1-1, merged", me, W9(M9(141, 2 * NM, 0)), f, 340);
        var nD3 = Tn(oD14, W9(M9(141, 1 * NM, 0)), 341);
        check(jD3.Count == 0 && uD3.All(s => s == "-") && mD3.Count == 0 && nD3 == "-", $"LD3 judy/merged: {jD3.Count} | {string.Join(" | ", uD3)} | {mD3.Count} | {nD3}");
        var rb4 = Rebraa("Enfield one one, Overlord, threat, single group BRAA three five four, 25, 20 thousand, hot, hostile, single.", me.Tel!, M9(141, 15 * NM, 0));   // LD4: waited 8 s -> distance at transmit time
        check(rb4 == "Enfield one one, Overlord, threat, single group BRAA three five four, 15, 20 thousand, hot, hostile, single.", "LD4 BRAA beim Senden: " + rb4);
        var bs = new Ops().OnTranscript("AWACS", "Overlord, Enfield 1-1, spiked 1 8 0", me, e3.Append(new Traffic(77, "F-16C_50", -15 * NM, 0, 7600, 0, 250, "Ford", "", true, 2)).ToList(), f, 600)[0].Text;
        check(bs == "Enfield one one, Overlord, spike one eight zero, buddy spike, BRAA one seven four, 15, 25 thousand, hot.", "LD12 buddy spike: " + bs);
        // LD9: group 45 NM in front of the AWACS, heading at it -> to all, leaker (planned first), AWACS evades to the south
        Abm.Reset(); labels.Clear(); net.Clear();
        var oD9 = new Ops(); var c9 = new List<string>(); var cmd9 = Flights.Cmd; Flights.Cmd = c9.Add;
        oD9.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, e3, f, 500); oD9.Tick(me, e3, 501);
        var h9 = Tn(oD9, W9(Mig(191, -15 * NM, 0) with { Hdg = Math.PI }), 502);
        var h9b = Tn(oD9, W9(Mig(191, -16 * NM, 0) with { Hdg = Math.PI }), 503);
        Flights.Cmd = cmd9;
        check(h9 == "Overlord, single group, 45 miles from Overlord, hot, Overlord moving south." && h9b == "-" && c9.Count == 1 && c9[0].StartsWith("ORBIT;Overlord;"),
              $"LD9 Schutz des AWACS: {h9} | {h9b} | {string.Join(" ", c9)}");
        Abm.Reset(); labels.Clear(); net.Clear();
        var oD15 = new Ops();
        oD15.OnTranscript("AWACS", "Overlord, Enfield 1-1, on station", me, e3, f, 400); oD15.Tick(me, e3, 401);
        var tD15 = new[] { Tn(oD15, W9(M9(151, 38 * NM, 0)), 402), Tn(oD15, W9(M9(151, 37 * NM, 0)), 413), Tn(oD15, W9(M9(151, 36 * NM, 0)), 428) };
        bool freeD15 = Abm.TargetedBy(2, 151) == null;
        var uD15 = oD15.OnTranscript("AWACS", "Overlord, Enfield 1-1, unable, winchester", me, W9(M9(151, 35 * NM, 0)), f, 430);
        check(tD15[0].Contains("commit single group") && tD15[1] == "Enfield one one, Overlord, commit single group, how copy?" && freeD15 && uD15.Count == 1 && uD15[0].Text == "Enfield one one, Overlord, copy.",
              $"LD14 Nachfrage, ohne Antwort frei, unable: {string.Join(" | ", tD15)} | frei {freeD15} | {string.Join(" ", uD15.Select(x => x.Text))}");
        // LD16: player's fox -> no reply, group belongs to him, the AI flight on it gets skip it
        Abm.Reset(); labels.Clear(); net.Clear();
        var oD16 = new Ops();
        oD16.Tick(me, W9(M9(161, 30 * NM, 0)), 500);
        Abm.Want(2, "Ford one", new[] { 161 }, "single group", 1, 500);
        var fD16 = oD16.OnTranscript("AWACS", "Overlord, Enfield 1-1, fox three", me, W9(M9(161, 25 * NM, 0)), f, 501);
        var kD16 = string.Join(" ", Abm.Take(2, "Ford one", 501).Select(e => Abm.Say(e, "Overlord")));
        check(fD16.Count == 0 && Abm.TargetedBy(2, 161) == "Enfield one" && kD16 == "Ford one, Overlord, skip it, single group Enfield one targeted.", $"LD16 fox des Spielers: {fD16.Count} | {kD16}");
        // LK8: AI fighter shoots down the last enemy (Flights says "Overlord copies, splash, picture clean") -> Ops then says no second picture clean (log 09.10. 00:13/00:15)
        Abm.Reset(); labels.Clear(); net.Clear();
        int r120 = "Red 120".GetHashCode(), r121 = "Red 121".GetHashCode(), r122 = "Red 122".GetHashCode();
        var keepU = Flights.Units;
        Flights.Units = new() { ["Ford11"] = new Flights.Unit("Ford11", "Ford", "FA-18C_hornet", "Ford11", 2, 0, 0, 6000, true, 0.8, 251, new[] { 2, 0, 2, 0, 0 }) };
        var o8 = new Ops();
        o8.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, e3, f, 0); o8.Tick(me, e3, 1);
        var c8 = new List<string> { Tn(o8, W9(Mig(r120, 60 * NM, 0)), 2) };
        OnEvent(new[] { "X", "kill", "Ford11", "Ford", "Red 120", "MiG-29S", "", "1" }, 10);
        c8.Add(Tn(o8, e3, 11)); c8.Add(Tn(o8, e3, 140));
        Flights.Units = keepU;
        check(c8[0].Contains("new group") && c8[1] == "-" && c8[2] == "-", "LK8: nach KI-splash mit picture clean kein zweites clean: " + string.Join(" | ", c8));
        // LK8: player splash names the group and what remains
        var l8 = W9(Mig(r121, 40 * NM, -10 * NM), Mig(r122, 40 * NM, 10 * NM));
        var o8b = new Ops();
        var pic8 = o8b.OnTranscript("AWACS", "Overlord, Enfield 1-1, request picture", me, l8, f, 20)[0].Text;
        var (n121, n122) = (GroupLabel(2, r121)!, GroupLabel(2, r122)!);
        OnEvent(new[] { "X", "kill", "Enfield11", "Enfield", "Red 121", "MiG-21Bis", "", "1" }, 25);
        var sp8 = o8b.OnTranscript("AWACS", "Overlord, Enfield 1-1, splash one", me, W9(Mig(r122, 40 * NM, 10 * NM)), f, 26)[0].Text;
        check(sp8 == $"Enfield one one, Overlord copies splash one, {n121}. {Cap(n122)} remains.", $"LK8 splash mit Gruppe und Rest: {pic8} | {sp8}");
        // LK8: furball: own fighter 2 NM from a MiG -> once "Overlord, furball, bullseye …" to all, afterwards no threat to it
        AwacsBullseye = true;
        var vip = new Traffic(130, "F-16C_50", 20 * NM, 2 * NM, 6000, 0, 250, "Blue 130", "", true, 2);
        var o8f = new Ops();
        o8f.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, e3, f, 30); o8f.Tick(me, e3, 31);
        var fb8 = new[] { Tn(o8f, W9(M9(131, 20 * NM, 0), vip), 32), Tn(o8f, W9(M9(131, 14 * NM, 0), vip with { X = 14 * NM }), 33) };
        AwacsBullseye = false;
        check(fb8[0] == "Overlord, furball, bullseye three five four, 20." && fb8[1] == "-", "LK8 furball: " + string.Join(" | ", fb8));
        // LK8: leaker: hostile group between the CAP (player on station) and own airfield, heading toward it, taken by no one -> once "leaker"
        var kf = Airfield.Kutaisi(); kf.Side = 2;
        var keepAll = Tower.All; Tower.All = new List<Airfield> { kf };
        double lx = kf.X / 3, lz = kf.Z / 3;
        var lkl = W9(Mig(132, lx, lz) with { Hdg = Math.Atan2(kf.Z - lz, kf.X - lx) });
        var o8l = new Ops();
        o8l.OnTranscript("AWACS", "Overlord, Enfield 1-1, on station", me, e3, f, 40); o8l.Tick(me, e3, 41);
        var lk8 = new[] { Tn(o8l, lkl, 42), Tn(o8l, lkl, 43) };
        var o8n = new Ops();   // without CAP no defensive line: no leaker
        Abm.Reset();
        o8n.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, e3, f, 40); o8n.Tick(me, e3, 41);
        var lk8n = Tn(o8n, lkl, 42);
        Tower.All = keepAll;
        check(lk8[0].StartsWith("Enfield one one, Overlord, leaker, single group, BRAA") && lk8[0].EndsWith(", hostile.") && lk8[1] == "-" && !lk8n.Contains("leaker"),
              $"LK8 leaker: {string.Join(" | ", lk8)} | ohne CAP: {lk8n}");
        Abm.Reset();
        check(new Ops().Tick(me, air.Where(a => a.Type != "E-3A").ToList(), 1).Count == 0, "AWACS: ohne AWACS keine Meldungen");
        var join = Say("Tanker", "request rejoin");
        check(join.StartsWith("Texaco") && join.Contains("five two X-ray"), "Tanker: Hornet bekommt Korb-Tanker Texaco -> " + join);
        var meN = me with { Tel = me.Tel! with { Z = 29 * NM } };   // 1 NM behind the tanker (Texaco at Z = 30 NM)
        string SayN(string text) => string.Join(" | ", o.OnTranscript("Tanker", text, meN, air, f, 0).Select(x => x.Text));
        var farPc = Say("Tanker", "pre contact");
        check(farPc.Contains("negative, bearing zero eight four, 30 miles, report visual"), "Tanker R56: pre-contact aus 30 NM -> " + farPc);
        var (pc287, ob287) = (SayN("Texaco, Enfield 1-1, pre contact"), new Ops());   // R287: pre-contact without "cleared to join" -> join first
        var rj286 = ob287.OnTranscript("Tanker", "Texaco, Enfield 1-1, request rejoin", meN, air, f, 0)[0].Text;
        check(rj286.Contains("Expect the join in 1 minute, 1000 feet below"), "R286: 1 minute (Einzahl) -> " + rj286);
        // R370: 300 KIAS at 20000 ft = 216 m/s TAS, tanker 40 NM ahead and inbound at 140 m/s: 74 km / 356 m/s = 4 minutes (with IAS 5 minutes)
        var air370 = air.Select(a => a.Group.StartsWith("Texaco") ? a with { X = 40 * NM, Z = 0, Hdg = Math.PI } : a).ToList();
        var rj370 = new Ops().OnTranscript("Tanker", "Texaco, Enfield 1-1, request rejoin", me with { Tel = new Telemetry(6096, 6000, 300 * Kt, 0, 0, 0, 0, 0, 0) }, air370, f, 0)[0].Text;
        check(rj370.Contains("40 miles") && rj370.Contains("Expect the join in 4 minutes"), "R370: ETA aus TAS statt IAS -> " + rj370);
        var obT = new Ops(); var rjT = obT.OnTranscript("Tanker", "Texaco, Enfield 1-1, request rejoin", meN, air, f, 0);   // R393: bare "say again" repeats the last tanker call
        var saT = obT.OnTranscript("Tanker", "Enfield 1-1, say again", meN, air, f, 0);
        check(saT.Count == rjT.Count && saT.Count > 0 && saT[0].Text == rjT[0].Text, "R393: say again wiederholt den letzten Tankerspruch -> " + string.Join(" | ", saT.Select(x => x.Text)));
        var ob287o = string.Join(" | ", ob287.OnTranscript("Tanker", "Texaco, Enfield 1-1, observation", meN, air, f, 0).Select(x => x.Text));
        check(pc287 == "Enfield one one, Texaco one one, negative, not cleared pre-contact, cleared to join, left observation, number 1." && ob287o.StartsWith("Enfield one one, Texaco one one, cleared to join, left observation, number 2.")
              && SayN("pre contact").Contains("basket ready"), $"Tanker R287: pre-contact/observation ohne Join-Freigabe -> {pc287} | {ob287o}");
        ob287.OffLine();
        check(o.OnRefuel(me, true, 0.4, air, 3).FirstOrDefault()?.Text == "Contact.", "Tanker: Kontakt (DCS-Ereignis)");
        check(Say("Tanker", "Texaco, fuel state four point two").Contains("say again") && o.OnRefuel(me, true, 0.5, air, 4).Count == 0, "Tanker: Gerede setzt Kontakt nicht zurück");
        check(o.OnRefuel(me, false, 0.9, air, 63).FirstOrDefault()?.Text.Contains("disconnect") == true
              && o.Debrief.Any(d => d.Contains("60 s") && d.Contains("40 -> 90 %")), "Tanker: Trennung + Debriefing");
        line.Clear();   // R304: also with a DCS tanker (radio menu in the background, Program clears Menu after the press) our controller speaks the whole sequence
        var od = new Ops();
        var dj = od.OnTranscript("Tanker", "Texaco, request rejoin", me, air, f, 0);
        var dm = od.Menu; od.Menu = null;
        var dv = od.OnTranscript("Tanker", "Texaco, visual", meN, air, f, 1);
        var dv2 = od.Menu; od.Menu = null;   // TP6: Intent only with the join
        var dq0 = od.OnTranscript("Tanker", "Texaco, ready pre contact", meN, air, f, 1);
        var dq = od.Menu; od.Menu = null;   // Intent rejected (DCS-ATC dialog open): not F1 directly, via the mission again
        od.DcsIntent = true;
        var dp = od.OnTranscript("Tanker", "Texaco, ready pre contact", meN, air, f, 1);
        var dk = od.OnRefuel(me, true, 0.4, air, 2);
        check(dj.Count == 1 && dj[0].Text.Contains("report visual") && dm == null && dv.Count == 1 && dv[0].Text.Contains("cleared to join") && dv2?.Step == "intent"
              && dq0.Count == 1 && dq0[0].Text.Contains("cleared contact") && dq?.Step == "intent" && dp.Count == 1 && od.Menu?.Step == "precontact" && dk.FirstOrDefault()?.Text == "Contact.",
              $"Tanker R304: Lotse spricht, Menü {dm} -> {dv2} -> {dq} -> {od.Menu} | {string.Join(" | ", dj.Concat(dv).Concat(dq0).Concat(dp).Concat(dk).Select(x => x.Text))}");
        line.Clear();
        {   // TP1/TP2/TP5/TP3: queries without state change or menu, second request without restart, join only from astern, "judy" ends the vectors
            var oq = new Ops();
            var meA = me with { Tel = me.Tel! with { X = 2 * NM, Z = 30 * NM } };   // 2 NM ahead of Texaco (flies north)
            var tkq = new List<string>();
            string Q(Me m, string text) { var r = string.Join(" | ", oq.OnTranscript("Tanker", text, m, air, f, 0).Select(x => x.Text)); tkq.Add($"{oq.tank}{(oq.Menu == null ? "" : "M")} {r}"); oq.Menu = null; return r; }
            var q0 = Q(me, "Texaco, Enfield 1-1, say altitude and airspeed");
            Q(me, "Texaco, Enfield 1-1, request rejoin");
            var q1 = Q(me, "Texaco, say altitude and airspeed");
            var r2 = Q(me, "Texaco, Enfield 1-1, request rejoin");
            var a5 = Q(meA, "Texaco, visual");
            Q(meN, "Texaco, visual");
            var q2 = Q(meN, "say altitude and airspeed");
            var qp2 = Q(meN, "say position");
            var oj = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
            var me15 = me with { Tel = me.Tel! with { Z = 15 * NM } };
            var j = oj.OnTranscript("Tanker", "Texaco, judy", me15, air, f, 0);
            int jv = Enumerable.Range(1, 300).Sum(s => oj.Tick(me15, air, s, false).Count);
            check(q0.Contains("angels") && q0.EndsWith("knots.") && q1 == q0 && q2 == q0 && r2.Contains("continue, bearing") && a5.Contains("negative, rejoin from astern, Texaco one one 6 o'clock, 2 miles")
                  && qp2.Contains("bearing") && !qp2.Contains("contact") && string.Join(",", tkq.Select(x => x[..2].Trim())) == "0,1,1,1,1,2M,2,2"
                  && j.Count == 1 && j[0].Text.EndsWith("roger, report visual.") && jv == 0 && oj.tank == 1,
                  $"Tanker TP1/TP2/TP5: Abfragen, zweiter Request, Join von vorn, judy -> {string.Join(" / ", tkq)} / {jv}");
            line.Clear();
        }
        var atc = new Traffic(7, "KC135MPRS", 0, 30 * NM, 6700, 0, 140, "ATC Texaco #3", "", true, 2);
        check(TankerName(atc) == "Texaco", "Tanker: Name ohne ATC-Präfix -> " + TankerName(atc));
        var kc = Callsigns; Callsigns = new Dictionary<string, string> { ["ATC Texaco #3"] = "Texaco31", ["KC-135 North"] = "Arco 2-1", ["Zahl"] = "247" };
        var kcN = new Traffic(9, "KC-135", 0, 0, 6700, 0, 140, "KC-135 North", "", true, 2);
        var zahl = new Traffic(10, "E-3A", 0, 0, 9000, 0, 150, "Zahl", "", true, 1);
        check(TankerName(atc) == "Texaco" && TankerName(kcN) == "Arco" && TankerName(zahl) == "Tanker" && AwacsName(zahl, me) == "two four seven",
              $"Rufname aus DCS-Rufzeichen (A69/A117): {TankerName(atc)} {TankerName(kcN)} {TankerName(zahl)} {AwacsName(zahl, me)}");
        Callsigns = kc;
        check(TankerName(atc) == "Texaco" && TankerName(kcN) == "Tanker", "Tanker: ohne Rufzeichen nur ATC-Präfix -> " + TankerName(atc) + " " + TankerName(kcN));
        // A67/A122: "disconnect" is not a departure, "right wing" without clearance, departure with total quantity and handover to the AWACS
        line.Clear();
        var ta = new Ops();
        string Ta(string s, Ops? x = null, Me? m = null, List<Traffic>? l = null) => string.Join(" | ", (x ?? ta).OnTranscript("Tanker", s, m ?? meN, l ?? air, f, 0).Select(y => y.Station + ": " + y.Text));
        Ta("Texaco, Enfield 1-1, request rejoin"); Ta("Texaco, Enfield 1-1, visual"); Ta("Texaco, Enfield 1-1, pre contact");
        ta.OnRefuel(me, true, 0.47, air, 10, 4899); ta.OnRefuel(me, false, 0.97, air, 40, 4899);   // full, Hornet internal tank
        var tdis = Ta("Texaco, Enfield 1-1, disconnect");
        var trw = Ta("Texaco, Enfield 1-1, right wing");
        var tdep = Ta("Texaco, Enfield 1-1, complete, thanks");
        var tdep2 = Ta("Texaco, Enfield 1-1, good day");
        check(tdis == "Texaco one one: Enfield one one, Texaco one one, copy disconnect, move to the right wing." && trw == "Texaco one one: Enfield one one, Texaco one one, roger, report complete or request more."
              && !trw.Contains("pre-contact"), $"Tanker A67: disconnect/right wing -> {tdis} | {trw}");
        check(tdep.StartsWith("Texaco one one: Enfield one one, Texaco one one, total offload 5.4, cleared to depart, contact Overlord two five one decimal ") && ta.tank == 0
              && tdep2.Contains("cleared to depart") && !tdep2.Contains("total offload"), $"Tanker A122: Abflug mit Menge, Übergabe -> {tdep} | {tdep2}");
        check(Ta("Texaco, Enfield 1-1, complete", new Ops(), null, air.Where(a => a.Type != "E-3A").ToList()).EndsWith("cleared to depart, frequency change approved."), "Tanker A122: ohne AWACS keine Übergabe, keine Menge");
        var tv = new Ops();   // without DCS event: "disconnect" frees the spot, the next one moves up
        Ta("Texaco, Enfield 1-1, request rejoin", tv); Ta("Texaco, Enfield 1-1, visual", tv); Ta("Texaco, Enfield 1-1, pre contact", tv);
        var tw = new Ops();   // second waits on the left (A65), gets the spot on the first's "disconnect"
        var tw1 = Ta("Enfield 1-2, Texaco, request rejoin", tw, meN with { Callsign = "Enfield 1-2" }); var tw2 = Ta("Texaco, Enfield 1-2, pre contact", tw, meN with { Callsign = "Enfield 1-2" });
        var tvd = tv.OnTranscript("Tanker", "Texaco, Enfield 1-1, disconnect", meN, air, f, 0);
        check(tw2.Contains("number 2") && tvd.Count == 2 && tvd[0].Text.Contains("copy disconnect") && tvd[1].Text == "Enfield one two, Texaco one one, cleared pre-contact." && tvd[1].To == tw
              && tv.tank == 2 && !line["Texaco (KC-135MPRS Korb)"].Contains(tv), $"Tanker A67: disconnect ohne Ereignis, kein Abflug, Nächster frei -> {tw1} | {tw2} | {string.Join(" | ", tvd.Select(y => y.Text))}");
        line.Clear();   // A66: wanted partial quantity (OnRefuel below 0.97 -> tank 3), then "complete": quantity and the waiting one gets the spot
        var tp = new Ops(); var tq = new Ops();
        Ta("Texaco, Enfield 1-1, request rejoin", tp); Ta("Texaco, Enfield 1-1, visual", tp); Ta("Texaco, Enfield 1-1, pre contact", tp);
        Ta("Enfield 1-2, Texaco, request rejoin", tq, meN with { Callsign = "Enfield 1-2" }); var tq2 = Ta("Texaco, Enfield 1-2, pre contact", tq, meN with { Callsign = "Enfield 1-2" });
        tp.OnRefuel(me, true, 0.4, air, 10, 4899); tp.OnRefuel(me, false, 0.7, air, 40, 4899);
        var tpc = tp.OnTranscript("Tanker", "Texaco, Enfield 1-1, complete", meN, air, f, 0);
        var tpc2 = Ta("Texaco, Enfield 1-2, complete", tq, meN with { Callsign = "Enfield 1-2" });
        check(tq2.Contains("number 2") && tpc.Count == 2 && tpc[0].Text.Contains("total offload 3.2, cleared to depart") && tpc[1].Text == "Enfield one two, Texaco one one, cleared pre-contact." && tpc[1].To == tq
              && tpc2.Split(" | ").Length == 1, $"Tanker A66: Teilmenge, complete gibt den Punkt weiter -> {string.Join(" | ", tpc.Select(y => y.Text))} | {tpc2}");
        line.Clear();
        // A125: F-4E (boom) gets the boom tanker, basket tanker KC_10_Extender_D is not a boom, aircraft without refueling equipment / without a matching tanker are rejected
        check(Ta("Enfield 1-1, request rejoin", new Ops(), me with { Type = "F-4E-45MC" }).StartsWith("Shell two one:"), "Tanker A125: F-4E -> Boom-Tanker Shell");
        check(Boom(new Traffic(0, "KC_10_Extender", 0, 0, 0, 0, 0)) && !Boom(new Traffic(0, "KC_10_Extender_D", 0, 0, 0, 0, 0)) && !Boom(atc), "Tanker A125: KC_10_Extender_D hat den Korb");
        var tneg = Ta("Enfield 1-1, request rejoin", new Ops(), me, air.Where(a => !a.Group.StartsWith("Texaco")).ToList());
        check(tneg == "Shell two one: Enfield one one, Shell two one, negative, no compatible tanker.", "Tanker A125: Hornet nur mit Boom-Tanker -> " + tneg);
        // wrong type named: that tanker refuses at once and names the matching one, no join started
        var owt = new Ops();
        var wtH = Ta("Shell 2-1, Enfield 1-1, request rejoin", owt, me);
        var wtA = Ta("Texaco, Enfield 1-1, request rejoin", new Ops(), me with { Type = "A-10C_2" });
        var wtN = Ta("Shell 2-1, Enfield 1-1, request rejoin", new Ops(), me, air.Where(a => !a.Group.StartsWith("Texaco")).ToList());
        check(Regex.IsMatch(wtH, @"^Shell two one: Enfield one one, Shell two one, negative, boom only, Texaco.* has the basket, bearing .*angels \d+") && owt.tank == 0
              && Regex.IsMatch(wtA, @"^Texaco.*negative, basket only, Shell two one has the boom, bearing") && wtN.EndsWith("negative, boom only, no basket tanker airborne."),
              $"Tanker: falscher Typ angefragt -> {wtH} | {wtA} | {wtN}");
        check(Ta("Enfield 1-1, request rejoin", new Ops(), me with { Type = "MiG-21Bis" }).Contains("negative, no compatible tanker")
              && Ta("Enfield 1-1, request rejoin", new Ops(), me with { Type = "UH-1H" }).Contains("negative, no compatible tanker")
              && Ta("Enfield 1-1, request rejoin", new Ops(), me with { Type = "Su-25T" }).Contains("bearing"), "Tanker A125: ohne Tankanlage abgelehnt, Su-25T nicht");
        check(Ta("Enfield 1-1, request rejoin", new Ops(), me, air.Where(a => !a.Type.StartsWith("KC")).ToList()).Contains("no tanker airborne"), "Tanker A125: kein Tanker in der Luft");
        line.Clear();
        // A125: boom/basket exact, F-4E boom, none without refueling equipment
        var kc10 = new Traffic(8, "KC_10_Extender", 0, 40 * NM, 6700, 0, 140, "Arco", "", true, 2);
        var kc10d = new Traffic(9, "KC_10_Extender_D", 0, -40 * NM, 6700, 0, 140, "Arco D", "", true, 2);
        Me Typ(string ty) => me with { Type = ty };
        var tkAll = air.Concat(new[] { kc10, kc10d }).ToList();
        string Names(string ty) => string.Join(",", Tankers(tkAll, Typ(ty)).Select(k => k.A.Type));
        check(Names("F-16C_50") == "KC-135,KC_10_Extender" && Names("F-4E-45MC") == "KC-135,KC_10_Extender" && Names("FA-18C_hornet") == "KC135MPRS,KC_10_Extender_D",
              $"Tanker A125: Boom/Korb exakt -> {Names("F-16C_50")} | {Names("F-4E-45MC")} | {Names("FA-18C_hornet")}");
        check(Names("MiG-21Bis") == "" && Names("Ka-50") == "" && Names("Mi-8MT") == "" && Names("F-86F Sabre") == "" && !WheelUp(tkAll, Typ("MiG-21Bis")).Tanker,
              "Tanker A125: Typ ohne Luftbetankung -> keiner");
        check(new Ops().OnTranscript("Tanker", "Texaco, Enfield 1-1, request refuel", Typ("MiG-21Bis"), tkAll, f, 0) is [{ Text: var nt }] && nt.Contains("negative, no compatible tanker"), "Tanker A125: Anfrage ohne Tankanlage -> negative, no compatible tanker");
        string Aw(Me m, List<Traffic> l) => string.Join(" | ", new Ops().OnTranscript("AWACS", "Overlord, Enfield 1-1, request nearest tanker", m, l, f, 0).Select(y => y.Text));
        var awNo = Aw(Typ("MiG-21Bis"), tkAll); var awHn = Aw(me, tkAll.Where(a => !a.Type.Contains("_D") && a.Type != "KC135MPRS").ToList());
        var awNone = Aw(me, tkAll.Where(a => !a.Type.StartsWith("KC")).ToList()); var awOk = Aw(me, tkAll);
        check(awNo.EndsWith("negative, no compatible tanker airborne.") && awHn.EndsWith("negative, no compatible tanker airborne.") && awNone.EndsWith(", no tanker airborne.") && !awNone.Contains("negative") && awOk.Contains("nearest tanker"),
              $"AWACS A125: nearest tanker ohne passenden -> negative, no compatible tanker -> {awNo} | {awHn} | {awNone} | {awOk}");
        check(new Ops().OnTranscript("AWACS", "Overlord, picture", me, air.Where(a => a.Type != "E-3A").ToList(), f, 0).Count == 0, "AWACS: ohne AWACS keine Antwort");
        var noOwn = air.Where(a => a.Coalition != 2).ToList();
        check(WheelUp(air, me) == (true, true) && WheelUp(noOwn, me) == (false, false) && WheelUp(air.Where(a => a.Type != "E-3A").ToList(), me) == (false, true),
              "Funkrad: AWACS/Tanker nur mit eigenem in der Luft");
        Gci.Add(2);   // V19: without AWACS, but with ground radar -> GCI "Magic"
        var gci = new Ops().OnTranscript("AWACS", "Magic, Enfield 1-1, picture", me, air.Where(a => a.Type != "E-3A").ToList(), f, 0);
        check(WheelUp(noOwn, me) == (true, false), "Funkrad: GCI zählt als AWACS");
        Gci.Clear();
        Gci.Add(2);   // LD10: AWACS shot down -> GCI takes over with picture
        var oD10 = new Ops();
        oD10.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, air, f, 0); oD10.Tick(me, air, 1);
        var g10 = Tn(oD10, air.Where(a => a.Type != "E-3A").ToList(), 2);
        Gci.Clear();
        check(g10.StartsWith("Enfield one one, Magic, ") && g10.Contains("adar contact, picture, 2 groups"), "LD10 GCI übernimmt: " + g10);
        ScanSec = 10;   // LD11: new group at t = 3 s -> call only with the scan at 10 s
        var oD11 = new Ops();
        oD11.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, e3, f, 0); oD11.Tick(me, e3, 1);
        var n11 = new[] { Tn(oD11, e3.Append(Mig(111, 50 * NM, 0)).ToList(), 3), Tn(oD11, e3.Append(Mig(111, 50 * NM, 0)).ToList(), 7), Tn(oD11, e3.Append(Mig(111, 50 * NM, 0)).ToList(), 10) };
        ScanSec = 0;
        check(n11[0] == "-" && n11[1] == "-" && n11[2].Contains("new group"), "LD11 Scan 10 s: " + string.Join(" | ", n11));
        check(gci.Count == 1 && gci[0].Station == "Magic" && gci[0].Text.StartsWith("Enfield one one, Magic, threat, lead group") && gci[0].Text.Contains(". Picture, 2 groups"), "GCI V19: " + (gci.FirstOrDefault()?.Text ?? "-"));
        var ci235 = Say("Range", "Range Alpha, Enfield 1-1, checking in");
        check(ci235.Contains(", range is cold. Range bears") && ci235.EndsWith("minimum altitude 1500 feet AGL. Report IP.") && !ci235.Contains("targets") && !ci235.Contains("cleared into"), "R235: Range check in ohne Ziele, range is cold, report IP -> " + ci235);
        check(o.OnImpact(me, 30, 6, 5)[0].Text.Contains("Release without clearance"), "Range: Einschlag ohne cleared hot");
        // R16: understood uncertainly -> say again, no clearance, no check-in with the AWACS
        var ou = new Ops();
        var uR = o.OnTranscript("Range", "in hot", me, air, f, 0, true);
        var uA = ou.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in", me, air, f, 0, true);
        check(uR is [{ Text: "Enfield one one, Range Alpha, say again." }] && !o.isHot && uA is [{ Station: "Overlord", Text: "Enfield one one, Overlord, say again." }] && !ou.awacsIn,
              $"R16 unsicher: {uR.FirstOrDefault()?.Text} | {uA.FirstOrDefault()?.Text}");
        check(Say("Range", "in hot").Contains("cleared hot"), "Range: cleared hot");
        var sa393 = Say("Range", "Range Alpha, Enfield 1-1, say again");   // R393: the repeat is the last Range call, no state change
        check(sa393.Contains("cleared hot") && o.isHot && BareSayAgain(Normalize("Enfield 1-1, say again"), "Enfield 1-1") && BareSayAgain("say again") && !BareSayAgain("overlord say again") && !BareSayAgain(Normalize("Enfield 1-1, say again the clearance"), "Enfield 1-1"), "R393: say again wiederholt den letzten Range-Spruch -> " + sa393);
        check(o.OnImpact(me, 12, 6, 40).Count == 0 && o.Flush(me, 42).Count == 0 && o.Flush(me, 43) is [{ Text: "Enfield one one, 40 feet at 6 o'clock." }], "Range: Einschlag 12 m 6 Uhr, ein Spruch nach 3 s");
        o.OnGunHit(); o.OnGunHit();
        var off = Say("Range", "off safe");
        check(off.Contains("40 feet") && off.Contains("2 gun hits"), "Range: Auswertung -> " + off);
        check(Say("Range", "checking out").Contains("1 pass"), "Range: check out");
        var rs = string.Join(" | ", o.DebriefSummary());
        check(rs.Contains(": 1 ") && rs.Contains("40 feet") && rs.Contains("6 "), "N39: Range-Zusammenfassung -> " + rs);
        // R57/A123/A126: cleared hot only after check-in (QNH or Hornet altimeter, approach headings, minimum altitude); check-out with exit and AWACS; every visit starts at pass 1
        var rgMe = me with { Tel = me.Tel! with { Pressure = 760, AltMsl = 0 } };
        var rgO = new Ops();
        string Rg(string s) => string.Join(" | ", rgO.OnTranscript("Range", s, rgMe, air, f, 0).Select(x => x.Text));
        var rgNo = Rg("in hot"); var rgNoHot = rgO.isHot; var rgIn = Rg("Range Alpha, Enfield 1-1, checking in"); var rgHot = Rg("in hot");
        check(rgNo.Contains("not checked in") && !rgNoHot && rgIn.StartsWith("Enfield one one, Range Alpha, altimeter two niner niner two, range is cold. Range bears") && !rgIn.Contains("targets") && rgIn.Contains("Enter from the south, run-in headings")
              && rgIn.Contains("minimum altitude 1500 feet AGL") && rgHot.Contains("cleared hot"), $"R57: {rgNo} | {rgIn}");
        rgO.OnImpact(rgMe, 12, 6, 1); Rg("off safe"); Rg("in hot"); Rg("off safe");
        var rgOut = Rg("checking out");
        var rgDir = Card(Bearing(Range!.Value.X, Range.Value.Z, f[0].X, f[0].Z));
        check(rgOut.Contains($"check switches safe. 2 passes, best impact 40 feet at 6 o'clock. Cleared off the range to the {rgDir}, contact Overlord two five one decimal five, good day")
              && Rg("checking out").Contains("not checked in"), "A123: " + rgOut);
        Rg("checking in"); Rg("in hot"); var rgP1 = Rg("off safe"); var rgOut2 = Rg("checking out");
        check(rgP1.Contains("pass 1. ") && rgOut2.Contains("check switches safe. 1 pass. ") && !rgOut2.Contains("best impact"), $"A126 zweiter Besuch: {rgP1} | {rgOut2}");
        Rg("checking in"); var rgHung = Rg("Range Alpha, Enfield 1-1, checking out, hung ordnance");   // R251
        check(rgHung.StartsWith("Enfield one one, Range Alpha, copy hung ordnance, check switches safe.") && rgHung.EndsWith($"Cleared off the range to the {rgDir} via the hung ordnance route, advise Approach, good day."), "R251 Range hung ordnance: " + rgHung);
        // R290: hung ordnance on "off" -> no more passes, "in hot" rejected, check-out via the hung route even without the word
        var o290 = new Ops(); string R290(string s) => string.Join(" | ", o290.OnTranscript("Range", s, rgMe, air, f, 0).Select(x => x.Text));
        R290("checking in"); R290("in hot"); var h290 = R290("Range Alpha, Enfield 1-1, off, hung bomb"); var h290b = R290("in hot"); var h290c = R290("checking out");
        check(h290 == "Enfield one one, pass 1, copy hung ordnance, check switches safe, no further hot passes, report checking out." && h290b == "Enfield one one, negative, hung ordnance, report checking out." && !o290.isHot
              && h290c.Contains("copy hung ordnance") && h290c.EndsWith("via the hung ordnance route, advise Approach, good day."), $"R290: {h290} | {h290b} | {h290c}");
        Rg("checking in"); Rg("in hot"); rgO.OnImpact(rgMe, 30, 3, 2); Rg("off safe"); Rg("in hot"); rgO.OnImpact(rgMe, 40, 2, 3); rgO.OnGunHit(); rgO.Leave();
        check(rgO.passes == 0 && rgO.gunHits == 0 && rgO.bestD == double.MaxValue && !rgO.checkedIn && !rgO.isHot && !Ops.hot.Contains(rgO), "A126: Leave setzt Zähler zurück");
        check(string.Join(" | ", rgO.DebriefSummary()).Contains("gesamt: 5 Anflüge, bester Einschlag 40 feet bei 6 Uhr") && rgO.Debrief[^1] == "Range Alpha Anflug 2: impact 130 feet at 2 o'clock, 1 gun hit",
              "N39/R58: Range-Summe über alle Besuche (A126 setzt nur den Besuch zurück), Leave schließt den heißen Pass ab -> " + string.Join(" | ", rgO.DebriefSummary()) + " | " + rgO.Debrief.LastOrDefault());
        // R58: check-out hot without "off" -> pass counts (total, debrief)
        Rg("checking in"); Rg("in hot"); rgO.OnImpact(rgMe, 20, 12, 4); var rgOutHot = Rg("checking out");
        check(rgOutHot.Contains("check switches safe. 1 pass, best impact 65 feet at 12 o'clock.") && rgO.Debrief[^1] == "Range Alpha Anflug 1: impact 65 feet at 12 o'clock" && !Ops.hot.Contains(rgO), "R58: Check-out heiß -> " + rgOutHot);
        rgO.Leave();
        // Check-in with direction/heading is not an "in hot"; QNH from the airfield weather (qnhOf) before the value reduced from the aircraft
        var rgW = new Ops();
        var rgWIn = string.Join(" | ", rgW.OnTranscript("Range", "Range Alpha, Viper 1-1, checking in from the north, angels 10", rgMe with { Type = "Su-25T" }, air, f, 0, qnhOf: _ => (1021.3, 0, 0)).Select(x => x.Text));
        var rgWHot = string.Join(" | ", rgW.OnTranscript("Range", "Viper 1-1, in heading 360", rgMe, air, f, 0).Select(x => x.Text));
        check(rgWIn.EndsWith("Report IP.") && rgWIn.Contains("QNH one zero two one") && rgWHot.Contains("cleared hot"), $"Range Check-in mit Richtung/QNH: {rgWIn} | {rgWHot}"); rgW.Leave();
        // R132: wind at the target in the check-in (wind from magnetic 270 at 10 kt = true 276); below 3 kt calm; without weather data nothing
        var rgWind = (Qnh: 1021.3, Wx: -Math.Cos(276 * Math.PI / 180) * 10 * 0.514444, Wz: -Math.Sin(276 * Math.PI / 180) * 10 * 0.514444);
        string RgWx(Func<Airfield, (double Qnh, double Wx, double Wz)>? q) { var o2 = new Ops(); var r = string.Join(" | ", o2.OnTranscript("Range", "Range Alpha, Viper 1-1, checking in", rgMe with { Type = "Su-25T" }, air, f, 0, qnhOf: q).Select(x => x.Text)); o2.Leave(); return r; }
        var rgW1 = RgWx(_ => rgWind); var rgW2 = RgWx(_ => (1021.3, 0.5, 0)); var rgW3 = RgWx(null);
        check(rgW1.Contains("QNH one zero two one, wind on target two seven zero at one zero, range is cold.") && rgW2.Contains(", wind on target calm, ") && !rgW3.Contains("wind on target"), $"R132: Wind am Ziel -> {rgW1} | {rgW2} | {rgW3}");
        // R131: landing/shutdown ends the check-in (new flight in the same slot must check in again), touch-and-go stays checked in
        var rgG = new Ops(); string RgG(string s) => string.Join(" | ", rgG.OnTranscript("Range", s, rgMe, air, f, 0).Select(x => x.Text));
        RgG("Range Alpha, Enfield 1-1, checking in");
        var rgGnd = rgMe with { Tel = rgMe.Tel! with { Agl = 0, Ias = 40 } };
        rgG.Tick(rgGnd, air, 1); var rgTg = rgG.checkedIn;
        rgG.Tick(rgGnd with { Tel = rgGnd.Tel! with { Ias = 0 } }, air, 2);
        check(rgTg && !rgG.checkedIn && RgG("in hot").Contains("not checked in"), "R131: Abstellen beendet den Range-Check-in, Touch-and-go nicht");
        // R234: "IP inbound" after check-in -> continue, no new check-in, passes keep counting; before check-in no check-in. R237: direct hit "shack" without time of day
        var rgI = new Ops(); string RgI(string s, double t = 0) => string.Join(" | ", rgI.OnTranscript("Range", s, rgMe, air, f, t).Select(x => x.Text));
        var i234 = new[] { RgI("Range Alpha, Enfield 1-1, IP inbound"), RgI("Range Alpha, Enfield 1-1, checking in"), RgI("in hot", 1), RgI("off", 2), RgI("Range Alpha, Enfield 1-1, IP inbound", 3), RgI("Enfield 1-1, in", 4) };
        rgI.OnImpact(rgMe, 1.0, 12, 5);
        var s237 = new[] { string.Join(" | ", rgI.Tick(rgMe, air, 9, false).Select(x => x.Text)), RgI("off", 10), RgI("checking out", 11) };
        check(i234[0].Contains("not checked in") && i234[4] == "Enfield one one, continue." && i234[5] == "Enfield one one, cleared hot." && s237[0] == "Enfield one one, shack." && s237[1] == "Enfield one one, pass 2, shack. Report in hot or checking out."
              && s237[2].Contains("2 passes, best impact shack. Cleared off") && rgI.Debrief[^1] == "Range Alpha Anflug 2: shack", "R234/R237: " + string.Join(" | ", i234.Concat(s237)));
        // A72/R58: salvo = one call with best value; without clearance one "check fire" and one violation line per drop; shot only with clearance, once per pass
        var mr = me with { Tel = me.Tel! with { X = 5 * NM } };   // in the range
        var rg = new Ops();
        string RS(Ops x, string s, double t = 0) => string.Join(" | ", x.OnTranscript("Range", s, mr, air, f, t).Select(c => c.Text));
        const string Hy = "weapons.nurs.HYDRA_70_M151", Mk = "weapons.bombs.Mk_82";
        RS(rg, "Range Alpha, Enfield 1-1, checking in");
        var cs = Enumerable.Range(0, 19).SelectMany(i => rg.OnImpact(mr, 30 + i, 7, 100 + i * 0.2, Hy)).ToList();
        check(cs.Count == 1 && cs[0].Text.Contains("check fire") && rg.Debrief.Count == 1 && rg.Debrief[0].Contains("HYDRA 70 M151") && rg.OnKill(mr, 104).Count == 0,
              $"R58: Salve ohne Freigabe: ein check fire, eine Debriefing-Zeile, kein Abschuss-Spruch -> {cs.Count} {rg.Debrief.Count}");
        check(rg.Tick(mr, air, 110).Count == 0 && rg.pass.Count == 0 && rg.Debrief.Count == 1, "R58: kalte Salve ohne Wertung");
        check(RS(rg, "in hot", 150).Contains("cleared hot"), "A72: cleared hot");
        var hs = Enumerable.Range(0, 19).SelectMany(i => rg.OnImpact(mr, 30 + i, 7, 200 + i * 0.2, Hy)).ToList();
        var own369 = rg.OnKill(mr, 201, 2).Count + rg.OnKill(mr, 201, 0).Count;   // R369: own and neutral object destroyed in the range: no hit, the pass keeps its kill call
        var hk = rg.OnKill(mr, 202, 1);
        check(own369 == 0 && hs.Count == 0 && hk is [{ Text: "Enfield one one, good hits, target destroyed." }] && rg.OnKill(mr, 209).Count == 0 && rg.Tick(mr, air, 205).Count == 0,
              "A72: 19 Raketen: keine Einzelsprüche, Abschuss nur einmal je Pass; R369: eigene/neutrale Kills zählen nicht");
        var fl = rg.Tick(mr, air, 207).Select(c => c.Text).ToList();
        check(fl.SequenceEqual(new[] { "Enfield one one, rockets, best 100 feet at 7 o'clock." }) && rg.Tick(mr, air, 208).Count == 0, "A72: ein Spruch mit bestem Wert -> " + string.Join(" | ", fl));
        var ro = RS(rg, "off safe", 209);
        rg.Tick(mr, air, 210);
        int dn = rg.Debrief.Count;   // incl. pass line
        var late = rg.OnImpact(mr, 20, 3, 215, Hy);   // flight time: impact 5 s after "off" still belongs to the pass
        check(ro.Contains("19 impacts, best 100 feet at 7 o'clock") && late.Count == 0 && rg.Debrief.Count == dn && rg.Tick(mr, air, 219).Count == 1 && rg.Debrief[dn - 1].Contains("20 impacts, best 65 feet at 3 o'clock"), "A72: Einschlag kurz nach off ist kein Verstoß, Pass-Zeile im Debriefing nachgetragen -> " + ro + " | " + rg.Debrief[dn - 1]);
        check(rg.OnImpact(mr, 20, 3, 300, Hy).Count == 1 && rg.Debrief.Count == dn + 1 && rg.OnKill(mr, 301).Count == 0, "R58: lange nach off ist es ein Verstoß");
        rg.Tick(mr, air, 304);
        RS(rg, "in hot", 305);
        check(rg.OnKill(mr, 320).Count == 1, "A72: neuer Pass, wieder ein Abschuss-Spruch");
        RS(rg, "checking out");
        var rb = new Ops();
        RS(rb, "checking in"); RS(rb, "in hot");
        rb.OnImpact(mr, -1, 0, 400, Mk);
        var ns = rb.Tick(mr, air, 404).Select(c => c.Text).ToList();
        rb.OnImpact(mr, 40, 2, 410, Hy);
        var sw = rb.OnImpact(mr, 15, 6, 411, Mk).Select(c => c.Text).ToList();   // other weapon: the old drop comes immediately, the new after 3 s
        var bm = rb.Tick(mr, air, 415).Select(c => c.Text).ToList();
        check(ns.SequenceEqual(new[] { "Enfield one one, no score." }) && sw.SequenceEqual(new[] { "Enfield one one, rocket, 130 feet at 2 o'clock." }) && bm.SequenceEqual(new[] { "Enfield one one, 50 feet at 6 o'clock." }),
              $"A72: kein Ziel -> no score, Waffenwechsel -> {string.Join(" | ", ns.Concat(sw).Concat(bm))}");
        RS(rb, "checking out");
        // R58: clearance at the time of release (flight time from the mission), not at impact; A72: "off" before the flush does not call the drop again
        var rf = new Ops();
        RS(rf, "checking in", 500); RS(rf, "in hot", 501);
        var fo = RS(rf, "off safe", 506);   // GBU-12 at 505 from 20000 ft, "off" right after
        var gb = rf.OnImpact(mr, 12, 6, 540, "weapons.bombs.GBU_12", 35);
        var gt = rf.Tick(mr, air, 544).Select(c => c.Text).ToList();
        check(fo == "Enfield one one, pass 1. Report in hot or checking out." && gb.Count == 0 && gt.SequenceEqual(new[] { "Enfield one one, 40 feet at 6 o'clock." }) && rf.Debrief.Count == 1 && rf.Debrief[0].Contains("impact 40 feet at 6 o'clock"),
              $"R58: lange Flugzeit, Abwurf mit Freigabe -> {string.Join(" | ", gb.Select(c => c.Text).Concat(gt))} | {string.Join(" | ", rf.Debrief)}");
        RS(rf, "in hot", 550);
        var cr = rf.OnImpact(mr, 15, 6, 560, Mk, 20);   // released cold at 540, impact after the "in hot"
        check(cr.Count == 1 && cr[0].Text.Contains("check fire") && rf.Debrief.Count == 2 && rf.pass.Count == 0, "R58: kalt abgeworfen, nach in hot eingeschlagen");
        Enumerable.Range(0, 19).SelectMany(i => rf.OnImpact(mr, 30 + i, 7, 600 + i * 0.2, Hy, 3)).ToList();
        var ro2 = RS(rf, "off safe", 605);
        var dup = rf.Tick(mr, air, 609);
        check(ro2.Contains("19 impacts, best 100 feet at 7 o'clock") && dup.Count == 0, $"A72: off vor dem Flush, kein zweiter Spruch -> {ro2} | {string.Join(" | ", dup.Select(c => c.Text))}");
        RS(rf, "checking out", 620);
        // A119/N36: second aircraft gets no "fouled"; range frees without "off"; "in dry" and "winchester"
        var o2 = new Ops();
        Say("Range", "checking in"); Say("Range", "in hot");
        o2.OnTranscript("Range", "checking in", me, air, f, 0);
        var busy = o2.OnTranscript("Range", "in hot", me, air, f, 0)[0].Text;
        check(busy == "Enfield one one, continue, one aircraft in.", "A119/R236: Range belegt -> " + busy);
        var meR = me with { Tel = me.Tel! with { X = 5 * NM, Z = 0 } };
        check(o.Tick(meR, air, 30, false).Count == 0 && o.isHot, "A119: 30 s nach in hot noch hot");
        var au = o.Tick(meR, air, 200, false); var au2 = o.Tick(meR, air, 220, false); var au3 = o.Tick(meR, air, 230, false);
        var w236 = o2.Tick(me, air, 231, false).Select(x => x.Text).ToList();
        check(au is [{ Text: "Enfield one one, Range Alpha, report off." }] && au2.Count == 0 && au3.Count == 1 && au3[0].Text.Contains("assuming you are off") && !o.isHot && o.Debrief.Any(d => d.Contains("kein „off“"))
              && w236 is ["Enfield one one, cleared hot."] && o2.isHot && o2.Tick(me, air, 232, false).Count == 0,
              $"A119/#21/R236: 180 s ohne off -> report off, 30 s später frei + Verstoß, Wartender bekommt cleared hot von selbst -> {string.Join(" | ", au.Concat(au2).Concat(au3).Select(x => x.Text))} | {string.Join(" | ", w236)}");
        o2.Leave();
        var oa2 = new Ops(); oa2.OnTranscript("Range", "checking in", me, air, f, 0); oa2.OnTranscript("Range", "in hot", me, air, f, 0); oa2.OnImpact(me, 10, 6, 5);
        var meW = me with { Tel = me.Tel! with { X = 9 * NM, Z = 0, Hdg = 0 } };   // 4 NM behind the center, heading +X = away
        var rgAw = oa2.Tick(meW, air, 10, false);   // A72: only the drop call, turned away frees only after 60 s
        var rgFly = oa2.Tick(meW, air, 40, false);   // weapon still in flight (loft/Maverick): still hot
        var rgLate = oa2.OnImpact(me, 8, 6, 45, "", 30);   // impact after turning away: scored, no check fire
        var rgOff = oa2.Tick(meW, air, 105, false).Concat(oa2.Tick(meW, air, 135, false)).ToList();   // gone 60 s after the last impact (the 60 s rule after impact applies only from > 60): report off, free 30 s later (#21)
        check(rgAw.Count == 1 && rgFly.Count == 0 && rgLate.Count == 0 && rgOff.Count == 3 && rgOff[1].Text.EndsWith("report off.") && rgOff[2].Text.Contains("assuming you are off") && rgOff[2].Text.Contains("pass 1, 2 impacts, best 25 feet") && !oa2.isHot,
            "A119: über 3 NM mit Kurs weg -> Range erst nach 60 s frei, später Einschlag zählt: " + string.Join(" | ", rgAw.Concat(rgFly).Concat(rgLate).Concat(rgOff).Select(x => x.Text)));
        oa2.Leave();
        var ody = new Ops();
        check(ody.OnTranscript("Range", "in dry", me, air, f, 0)[0].Text.Contains("not checked in"), "N36: in dry erst nach dem Check-in");
        ody.OnTranscript("Range", "checking in", me, air, f, 0);
        // R285: "in dry" occupies the range until "off" (the other waits, clearance afterwards by itself), with occupied range "continue, one aircraft in"; without pass/scoring
        var dry1 = ody.OnTranscript("Range", "Viper 1-1, in dry from the north", me, air, f, 0)[0].Text;
        var oq2 = new Ops(); oq2.OnTranscript("Range", "checking in", me, air, f, 0);
        var dry2 = oq2.OnTranscript("Range", "in hot", me, air, f, 0)[0].Text;
        var dryOff = ody.OnTranscript("Range", "off", me, air, f, 0)[0].Text;
        var dry3 = oq2.Tick(me, air, 1, false).Select(x => x.Text).ToList();
        var dry4 = ody.OnTranscript("Range", "in dry", me, air, f, 0)[0].Text;
        check(dry1 == "Enfield one one, cleared dry." && !ody.isHot && dry2 == "Enfield one one, continue, one aircraft in." && dryOff == "Enfield one one, roger, range is cold." && ody.passes == 0
              && dry3 is ["Enfield one one, cleared hot."] && dry4 == "Enfield one one, continue, one aircraft in.", $"R285: in dry belegt die Range -> {dry1} | {dry2} | {dryOff} | {string.Join(" | ", dry3)} | {dry4}");
        oq2.Leave();
        ody.OnTranscript("Range", "in hot", me, air, f, 0);
        var wn = ody.OnTranscript("Range", "winchester", me, air, f, 0)[0].Text;
        check(wn.Contains("pass 1") && wn.EndsWith("check switches safe, report checking out.") && !ody.isHot, "N36: winchester -> " + wn);
        ody.Leave();
        check(new Ops().OnTranscript("Range", "winchester", me, air, f, 0)[0].Text == "Enfield one one, check switches safe, report checking out.", "N36: winchester ohne hot");
        // R14: in hot only within 10 NM and heading toward the area; leaving the range is announced (range 5 NM north)
        var o14 = new Ops();
        Me At(double x, double hdg = 0) => me with { Tel = me.Tel! with { X = x, Hdg = hdg } };
        o14.OnTranscript("Range", "checking in", At(-10 * NM), air, f, 0);
        var r14 = new[] { o14.OnTranscript("Range", "in hot", At(-10 * NM), air, f, 1)[0].Text, o14.OnTranscript("Range", "in hot", At(0, Math.PI), air, f, 2)[0].Text,
                          o14.OnTranscript("Range", "in hot", At(0), air, f, 3)[0].Text, string.Join(" | ", o14.Tick(At(-7 * NM, Math.PI), air, 4, false).Select(x => x.Text)) };
        check(r14[0] == "Enfield one one, continue, report in inside 10 miles." && r14[1] == "Enfield one one, continue dry." && r14[2] == "Enfield one one, cleared hot."
              && r14[3] == "Enfield one one, Range Alpha, leaving the range, range is cold, pass 1. Report in hot or checking out." && !o14.isHot, "R14: " + string.Join(" | ", r14));
        o14.Leave();
        // A18: drop, "off", impact 30 s later (without flight time): scored, no check fire; "off" without "no hits scored"
        var ra18 = new Ops();
        ra18.OnTranscript("Range", "checking in", mr, air, f, 0); ra18.OnTranscript("Range", "in hot", mr, air, f, 1);
        var off18 = ra18.OnTranscript("Range", "Enfield 1-1, off west", mr, air, f, 10)[0].Text;
        var imp18 = ra18.OnImpact(mr, 12, 6, 40);
        var sc18 = ra18.Tick(mr, air, 44, false).Select(x => x.Text).ToList();
        check(off18 == "Enfield one one, pass 1. Report in hot or checking out." && imp18.Count == 0 && sc18.SequenceEqual(new[] { "Enfield one one, 40 feet at 6 o'clock." })
              && ra18.Debrief is [var d18] && d18.Contains("impact 40 feet at 6 o'clock"), $"A18: {off18} | {string.Join(" | ", imp18.Concat(ra18.Tick(mr, air, 45, false)).Select(x => x.Text))} | {string.Join(" | ", sc18)}");
        ra18.Leave();
        // A17: without a range zone in the mission no range controller reacts to "range"
        var keepR = Range; Range = null;
        var role17 = RoleOf("range alpha enfield 1 1 checking in");
        Range = keepR;
        check(role17 == null && RoleOf("range alpha enfield 1 1 checking in") == "Range", "A17: Range nur mit Zone -> " + role17);
        // R388: "declare" without position or to an airfield station is the emergency, not the AWACS; the own call sign is no station name
        var r388 = new[] { RoleOf(Normalize("Kutaisi Tower, Enfield 1-1, declare emergency")), RoleOf(Normalize("Kutaisi Tower, Enfield 1-1, declare an emergency, squawk 7700")),
                           RoleOf(Normalize("Spike 1-1, Kutaisi Tower, declare an emergency"), "Spike 1-1"), RoleOf(Normalize("Spike 1-1, Kutaisi Tower, request taxi"), "Spike 1-1"),
                           RoleOf(Normalize("Overlord, declare bullseye 030 45")), RoleOf(Normalize("declare 0 3 0 for 4 5")), RoleOf(Normalize("Overlord, Spike 1-1, declare emergency"), "Spike 1-1") };
        check(r388[0] == null && r388[1] == null && r388[2] == null && r388[3] == null && r388[4] == "AWACS" && r388[5] == "AWACS" && r388[6] == "AWACS", "R388: declare nur mit Position/AWACS-Name -> " + string.Join(",", r388));
        // A14: call sign alone -> check-in or callback; combat calls acknowledged instead of check-in with picture
        var o14a = new Ops();
        string Aw14(string s, List<Traffic>? l = null) => string.Join(" | ", o14a.OnTranscript("AWACS", s, me, l ?? air, f, 0).Select(x => x.Text));
        var a14 = new[] { Aw14("Overlord, Enfield 1-1"), Aw14("Overlord, Enfield 1-1"), Aw14("Overlord, Enfield 1-1, splash one", blue), Aw14("Overlord, Enfield 1-1, splash two"),
                          Aw14("Overlord, Enfield 1-1, committing north group"), Aw14("Overlord, Enfield 1-1, winchester, RTB") };
        check(a14[0].Contains("adar contact, picture") && a14[1] == "Enfield one one, Overlord, go ahead." && a14[2] == "Enfield one one, Overlord copies splash one, picture clean."
              && a14[3] == "Enfield one one, Overlord copies splash two. 2 groups remain." && a14[4].StartsWith("Enfield one one, Overlord, north group BRAA three five four, 20, 20 thousand, hot, hostile, single") && a14[4].Contains("Fulcrum")
              && a14[5] == "Enfield one one, Overlord copies.", "A14: " + string.Join(" | ", a14));
        // A15: Mayday to the AWACS -> nearest own airfield with approach frequency, Program hands over (divert); P3-AW2: check-in before tanker ("fuel" is not a tanker)
        var o15 = new Ops();
        var m15 = string.Join(" | ", o15.OnTranscript("AWACS", "Mayday mayday mayday, Overlord, Enfield 1-1, engine fire", me, air, f, 0).Select(x => x.Text));
        var ci2 = string.Join(" | ", new Ops().OnTranscript("AWACS", "Overlord, Enfield 1-1, checking in, fuel state 5 point 2", me, air, f, 0).Select(x => x.Text));
        check(Regex.IsMatch(m15, @"^Enfield one one, Overlord, roger mayday, Kutaisi bears \w+ \w+ \w+, \d+, contact Kutaisi Approach [\w ]+\.$") && !m15.Contains("intentions") && !m15.Contains("miles") && o15.Divert == f[0]   // Review R250: short like GCI
              && ci2.Contains("adar contact, picture") && !ci2.Contains("tanker"), $"A15/P3-AW2: {m15} | {ci2}");
        // V5: call sign and frequency of the mission AWACS, declare names neutrals, bogey dope does not
        var keep = Callsigns;
        Callsigns = new Dictionary<string, string> { ["Overlord"] = "Magic11" };
        AwacsFreq["Overlord"] = 260.5;
        var civ = new List<Traffic>(air) { new(8, "Yak-52", 5 * NM, 0, 1000, 0, 60, "Civil", "", true, 0) };
        var oa = new Ops();
        var dc = oa.OnTranscript("AWACS", "Magic, Enfield 1-1, declare", me, civ, f, 0)[0];
        check(dc.Station == "Magic" && dc.Freq == 260.5 && dc.Text.StartsWith("Enfield one one, Magic, group") && dc.Text.Contains("neutral, single, Yak 52"), "AWACS V5: Magic 260.5, declare -> " + dc.Text);
        var bd5 = oa.OnTranscript("AWACS", "Magic, bogey dope", me, civ, f, 0)[0].Text;
        check(bd5.Contains("hostile, single, Fulcrum"), "AWACS V5: bogey dope ohne Neutrale -> " + bd5);
        check(AwacsFreqOf(civ[^1] with { Group = "Overlord#002" }) == 260.5, "AWACS: nachträglich gespawnt (Overlord#002) -> Frequenz der Vorlage");
        Callsigns = keep; AwacsFreq.Clear();
        // V9: only what own sensors have detected (MiG yes, type unknown; Su-27 not)
        Detected[2] = new() { [1] = false };
        var p9 = new Ops().OnTranscript("AWACS", "Overlord, picture", me, air, f, 0)[0].Text;
        check(p9.Contains("single group.") && p9.Contains("bogey, single") && !p9.Contains("Flanker") && !p9.Contains("Fulcrum"), "AWACS V9: nur Erfasstes, Typ unbekannt -> " + p9);
        Detected.Clear();
        // V10: groups ≤ 3 NM (also across DCS groups), contact count, fill-ins, picture formats from the player (0/0, looking north)
        var aw = air.Where(a => a.Type == "E-3A").ToList();
        List<Traffic> Red(params (double N, double E)[] p) =>
            aw.Concat(p.Select((q, i) => new Traffic(100 + i, "Su-27", q.N * NM, q.E * NM, 8000, Math.PI, 250, "Red " + i, "", true, 1))).ToList();
        string Pic(List<Traffic> l) => new Ops().OnTranscript("AWACS", "Overlord, picture", me, l, f, 0)[0].Text;
        string Order(string s, params string[] w) { s = s[s.IndexOf("icture")..];   // from the picture (before that possibly threat, AW-P3)
            return w.Zip(w.Skip(1)).All(x => s.IndexOf(x.First) >= 0 && s.IndexOf(x.First) < s.IndexOf(x.Second)) ? "" : " (Reihenfolge)"; }
        var v10 = new (string P, string[] W, List<Traffic> L)[]
        {
            ("2 groups range 20", new[] { "Lead group", "two contacts", "Trail group", "heavy, 3 contacts" }, Red((20, 0), (21, 1), (40, 0), (40, 1), (41, 0))),
            ("2 groups azimuth 10", new[] { "East group", "West group" }, Red((30, -5), (30, 5))),
            ("3 groups wall 20", new[] { "East group", "Middle group", "West group" }, Red((30, -10), (30, 0), (30, 10))),
            ("3 groups ladder 20", new[] { "Lead group", "Middle group", "Trail group" }, Red((20, 0), (30, 0), (40, 0))),
            ("3 groups vic, 16 wide, 10 deep", new[] { "Lead group", "East trail group", "West trail group" }, Red((20, 0), (30, -8), (30, 8))),
            ("3 groups champagne, 16 wide, 10 deep", new[] { "East lead group", "West lead group", "Trail group" }, Red((20, -8), (20, 8), (30, 0))),
            ("4 groups.", Array.Empty<string>(), Red((20, 0), (30, 0), (40, 0), (50, 0))),
            ("single group.", new[] { "very fast", "high" }, aw.Append(new Traffic(200, "MiG-25PD", 30 * NM, 0, 13000, Math.PI, 500, "Red 9", "", true, 1)).ToList()),
        };
        foreach (var (p, w, l) in v10) { var s = Pic(l); check(s.Contains(p) && Order(s, w) == "", $"AWACS V10: {p}{Order(s, w)} -> {s}"); }
        // M3: declare with location (bullseye / BRAA, magnetic) and check-out
        var od2 = new Ops();
        var mix = new List<Traffic>(air) { new(9, "MiG-21Bis", 1 * NM, 30 * NM, 6000, 0, 250, "Red 4", "", true, 1) };   // at Texaco
        foreach (var (s, l, end) in new[] { ("Overlord, Enfield 1-1, declare bullseye 354 20", air, "bullseye three five four, 20, hostile, single, Fulcrum."),
                     ("declare 0 8 4 for 3 0", air, "BRAA zero eight four, 30, friendly."), ("declare bullseye 084/30", mix, "furball."),
                     ("declare bullseye 1 8 0 4 0", air, "bullseye one eight zero, 40, clean.") })
        { var r = od2.OnTranscript("AWACS", s, me, l, f, 0)[0].Text; check(r.EndsWith(end), $"AWACS declare: {s} -> {r}"); }
        // Declare by lock (Export.lua): without spoken position the locked target, 3 NM/±5000 ft; spoken position takes precedence
        Detected[2] = new() { [1] = true };   // MiG detected, Su-27 not
        foreach (var (lk, s, end) in new[] { ((0.0, 30 * NM, 6700.0), "declare", "BRAA zero eight four, 30, 22 thousand, friendly."),
                     ((20 * NM, 0.0, 5000.0), "Overlord, Enfield 1-1, declare", "BRAA three five four, 20, 16 thousand, hostile, single, Fulcrum."),
                     ((35 * NM, 10 * NM, 8000.0), "declare", "26 thousand, clean."),((20 * NM, 0.0, 9000.0), "declare", "30 thousand, clean."),
                     ((0.0, -10 * NM, 6000.0), "declare", "BRAA two six four, 10, 20 thousand, clean."), ((20 * NM, 0.0, 6100.0), "declare bullseye 1 8 0 4 0", "bullseye one eight zero, 40, clean.") })
        { var r = od2.OnTranscript("AWACS", s, me with { Tel = me.Tel! with { Lock = lk } }, air, f, 0)[0].Text; check(r.EndsWith(end), $"AWACS declare Lock {lk}: {s} -> {r}"); }
        // R371: the radar picture decides, not the truth: Su-27 not detected -> "clean" with and without altitude; detected, type unknown -> "bogey" (also without altitude)
        var su = new[] { od2.OnTranscript("AWACS", "declare", me with { Tel = me.Tel! with { Lock = (35 * NM, 10 * NM, 8000.0) } }, air, f, 0)[0].Text, od2.OnTranscript("AWACS", "declare bullseye 010 36", me, air, f, 0)[0].Text };
        Detected[2] = new() { [1] = true, [5] = false };
        var su2 = new[] { od2.OnTranscript("AWACS", "declare", me with { Tel = me.Tel! with { Lock = (35 * NM, 10 * NM, 8000.0) } }, air, f, 0)[0].Text, od2.OnTranscript("AWACS", "declare bullseye 010 36", me, air, f, 0)[0].Text };
        check(su.All(x => x.EndsWith("clean.")) && su2.All(x => x.Contains(", bogey, single.")), "AWACS R371: declare nicht erfasst -> clean, erfasst ohne Typ -> bogey: " + string.Join(" | ", su) + " | " + string.Join(" | ", su2));
        Detected.Clear();
        check(TypeSay("FA-18C_hornet") == "Hornet" && TypeSay("F-16C_50") == "Viper" && TypeSay("F-15ESE") == "Strike Eagle" && TypeSay("F-15C") == "Eagle" && TypeSay("F-14B") == "Tomcat" && TypeSay("A-10C_2") == "Warthog" && TypeSay("MiG-21Bis") == "MiG 21Bis" && TypeSay("C-130") == "C 130", "TypeSay: Sprechnamen");
        var co = od2.OnTranscript("AWACS", "Overlord, Enfield 1-1, checking out", me, air, f, 0)[0].Text;
        check(co.Contains("checking out") && !od2.awacsIn && od2.Tick(me, air, 1).Count == 0, "AWACS: check out -> " + co);
        // V11: two players at the tanker -> rendezvous, order, offload in pounds
        line.Clear();
        var me2 = me with { Callsign = "Enfield 1-2" };
        var (k1, k2) = (new Ops(), new Ops());
        string K(Ops o, Me m, string s) => string.Join(" | ", o.OnTranscript("Tanker", s, m, air, f, 0).Select(x => x.Text));
        Tracks["Texaco (KC-135MPRS Korb)"] = "Alpha";
        var r1 = K(k1, me, "Texaco, Enfield 1-1, request rejoin");
        K(k2, me2, "Texaco, Enfield 1-2, request rejoin");
        var vFar = K(k1, me, "Texaco, Enfield 1-1, visual");   // R56: no join from 30 NM
        var (nr1, nr2) = (me with { Tel = me.Tel! with { Z = 29 * NM } }, me2 with { Tel = me.Tel! with { Z = 29 * NM } });
        var v1 = K(k1, nr1, "Texaco, Enfield 1-1, visual");
        var h2 = K(k2, nr2, "Texaco, Enfield 1-2, pre contact");
        var c1 = K(k1, nr1, "Texaco, Enfield 1-1, pre contact");
        k1.OnRefuel(me, true, 0.4, air, 10, 4899);
        var d1 = k1.OnRefuel(me, false, 0.97, air, 70, 4899);
        var c2 = K(k2, nr2, "Texaco, Enfield 1-2, pre contact");
        check(r1.Contains("bearing zero eight four, 30 miles, anchor Alpha, block 21 to 23") && r1.Contains("Expect the join in 4 minutes, 1000 feet below, report visual"), "Tanker V11: Rendezvous -> " + r1);
        check(vFar.Contains("continue, 30 miles, report visual.") && v1.Contains("cleared to join, left observation, number 1.") && !v1.Contains("first, then") && h2.Contains("number 2") && c1.Contains("cleared contact"), $"Tanker V11/R56: Reihenfolge -> {vFar} | {v1} | {h2}");
        check(d1.Count == 2 && d1[0].Text == "Enfield one one, Texaco one one, disconnect, you received 6.2, move to the right wing." && d1[1].Text == "Enfield one two, Texaco one one, cleared pre-contact." && d1[1].To == k2 && c2.Contains("cleared contact"),
              $"Tanker V11/A128: Offload, Nächster eigener Spruch -> {string.Join(" | ", d1.Select(x => x.Text))} | {c2}");
        var ks = string.Join(" | ", k1.DebriefSummary());
        k1.ResetDebrief();
        check(ks.Contains(": 1 ") && ks.Contains("6200 lb") && k1.DebriefSummary().Count == 0 && k1.Debrief.Count == 0, "N39: Tanker-Zusammenfassung, Reset -> " + ks);
        // A66/A124: unwanted disconnect stays in the queue, contact shortly afterwards continues; quantity from the mission's internal tank (kg), without it no quantity
        line.Clear();
        var (a1, a2) = (new Ops(), new Ops());
        List<Call> Rf(Ops x, Me m, bool st, double fu, double t, double kg = 3000) => x.OnRefuel(m, st, fu, air, t, kg);
        K(a1, nr1, "Texaco, Enfield 1-1, request rejoin"); K(a2, nr2, "Texaco, Enfield 1-2, request rejoin");
        K(a1, nr1, "Texaco, Enfield 1-1, visual"); K(a2, nr2, "Texaco, Enfield 1-2, visual"); K(a1, nr1, "Texaco, Enfield 1-1, pre contact");
        Rf(a1, me, true, 0.4, 10);
        var slip = Rf(a1, me, false, 0.6, 30);
        var wait2 = K(a2, nr2, "Texaco, Enfield 1-2, pre contact");
        check(slip.Count == 1 && slip[0].Text == "Enfield one one, Texaco one one, disconnect, return to pre-contact." && a1.tank == 3 && wait2.Contains("number 2"), $"Tanker A66: Abrutschen bleibt in der Reihe -> {slip.FirstOrDefault()?.Text} | {wait2}");
        var again = Rf(a1, me, true, 0.6, 50);
        var full = Rf(a1, me, false, 0.99, 100);
        check(again.Count == 0 && full.Count == 2 && full[0].Text == "Enfield one one, Texaco one one, disconnect, you received 3.9, move to the right wing." && full[1].To == a2
              && a1.Debrief.Count == 1 && a1.Debrief[0].Contains("70 s") && a1.Debrief[0].Contains("40 -> 99 %") && a1.offload == 3900, $"Tanker A66/A124: Fortsetzung, voll -> {string.Join(" | ", full.Select(x => x.Text))} | {string.Join("", a1.Debrief)}");
        line.Clear();   // a2 still waiting
        var a3 = new Ops { tank = 3 };   // #19: cleared contact
        a3.OnRefuel(me, true, 0.4, air, 10);
        var a3t = a3.OnRefuel(me, false, 0.98, air, 40)[0].Text;
        check(a3t == "Enfield one one, Texaco one one, disconnect, move to the right wing. Report complete, or pre-contact for more.", "Tanker A124: ohne Innentank (alte Mission) keine Menge -> " + a3t);
        var a3s = string.Join(" | ", a3.DebriefSummary());
        check(a3s.Contains(": 1 ") && !a3s.Contains("lb"), "N39: Tanker-Zusammenfassung ohne bekannte Menge ohne lb -> " + a3s);
        var a4 = new Ops { tank = 3 };
        Rf(a4, me, true, 0.4, 10); Rf(a4, me, false, 0.6, 30);
        check(Rf(a4, me, true, 0.6, 200).FirstOrDefault()?.Text == "Contact." && Rf(a4, me, false, 0.99, 230).Count == 1 && a4.Debrief.Count == 2, "Tanker A66: Kontakt nach über 2 min ist ein neuer Vorgang");
        // #19: contact without "cleared contact" (joined but no pre-contact) -> rejected + violation, at most every 30 s, no transaction; R240: AWACS names the tanker with full call sign
        line.Clear();
        var c19 = new Ops();
        K(c19, nr1, "Texaco, Enfield 1-1, request rejoin"); K(c19, nr1, "Texaco, Enfield 1-1, visual");
        var (n19, n19b, n19c) = (Rf(c19, me, true, 0.4, 10), Rf(c19, me, false, 0.5, 15), Rf(c19, me, true, 0.5, 20));
        var (ok19, ct19) = (K(c19, nr1, "Texaco, Enfield 1-1, pre contact"), Rf(c19, me, true, 0.5, 50));
        var aw240 = new Ops().OnTranscript("AWACS", "Overlord, Enfield 1-1, vector to tanker", me, air, f, 0)[0].Text;
        check(n19 is [{ Text: "Enfield one one, Texaco one one, you are not cleared contact, disconnect, return to left observation." }] && n19b.Count == 0 && n19c.Count == 0
              && c19.Debrief.Count(d => d.StartsWith("Verstoß: Kontakt an Texaco one one")) == 1 && c19.Debrief.Count == 1 && ok19.Contains("cleared contact") && ct19 is [{ Text: "Contact." }]
              && aw240.Contains("nearest tanker Texaco one one, anchor Alpha") && aw240.EndsWith("contact Texaco one one, two five five decimal five."), $"#19/R240: Kontakt ohne Freigabe -> {n19.FirstOrDefault()?.Text} | {ok19} | {aw240}");
        line.Clear();
        // A65: 1-1 asks first, 1-2 arrives first -> 1-2 is number 1, 1-1 number 2 and waits
        var (b1, b2) = (new Ops(), new Ops());
        K(b1, nr1, "Texaco, Enfield 1-1, request rejoin");
        K(b2, nr2, "Texaco, Enfield 1-2, request rejoin");
        var j2 = K(b2, nr2, "Texaco, Enfield 1-2, visual");
        var j1 = K(b1, nr1, "Texaco, Enfield 1-1, visual");
        var p1 = K(b1, nr1, "Texaco, Enfield 1-1, pre contact");
        var p2 = K(b2, nr2, "Texaco, Enfield 1-2, pre contact");
        check(j2.Contains("cleared to join, left observation, number 1.") && j1.Contains("number 2. Enfield one two first, then Enfield one one") && p1.Contains("negative, hold left observation, number 2") && p2.Contains("cleared contact"),
              $"Tanker A65: Reihenfolge nach Ankunft -> {j2} | {j1} | {p1} | {p2}");
        // #19 uncertain: number 2 hooks up without clearance while number 1 is in contact -> breakaway + violation
        var (bc2, bc1) = (Rf(b2, me, true, 0.4, 300), Rf(b1, me, true, 0.4, 301));
        check(bc2 is [{ Text: "Contact." }] && bc1 is [{ Text: "Enfield one one, breakaway, breakaway, breakaway." }] && b1.tank == 2 && b1.Debrief.Count(d => d.StartsWith("Verstoß: Kontakt an")) == 1,
              $"#19: Kontakt ohne Freigabe neben einem Empfänger im Kontakt -> {string.Join(" | ", bc2.Concat(bc1).Select(x => x.Text))}");
        line.Clear();
        // R56: already joined (tank 2), far away again -> visual/observation give no "cleared pre-contact"
        var z1 = new Ops();
        K(z1, nr1, "Texaco, Enfield 1-1, request rejoin"); K(z1, nr1, "Texaco, Enfield 1-1, visual");
        var (zv, zo) = (K(z1, me, "Texaco, Enfield 1-1, visual"), K(z1, me, "Texaco, Enfield 1-1, observation"));
        check(zv.Contains("negative, bearing") && zo.Contains("negative, bearing") && !(zv + zo).Contains("cleared pre-contact"), $"Tanker R56: beigetreten, dann weit weg -> {zv} | {zo}");
        line.Clear();
        // R117: "visual" from 4.5 NM (TankVec itself says "report visual" from 5 NM) frees the join, from 20 NM not
        {
            var (rv, rw) = (new Ops(), new Ops());
            var (m45, m20) = (me with { Tel = me.Tel! with { Z = 25.5 * NM } }, me with { Tel = me.Tel! with { Z = 10 * NM } });
            K(rv, m45, "Texaco, Enfield 1-1, request rejoin"); K(rw, m20, "Texaco, Enfield 1-1, request rejoin");
            var (v45, v20) = (K(rv, m45, "Texaco, Enfield 1-1, visual"), K(rw, m20, "Texaco, Enfield 1-1, visual"));
            check(v45.Contains("cleared to join, left observation, number 1.") && v20 == "Enfield one one, Texaco one one, continue, 20 miles, report visual." && rw.tank == 1,
                  $"Tanker R117: visual aus 4,5 NM Join, aus 20 NM nicht -> {v45} | {v20}");
        }
        line.Clear();
        // R119: two tankers with the same call name (Texaco 1-1 / 2-1): queue, clearance and turn call per tanker, name with number
        {
            var kcSave = Callsigns;
            Callsigns = new Dictionary<string, string>(Callsigns) { ["Texaco B"] = "Texaco 2-1" };
            var air2 = air.Append(new Traffic(7, "KC135MPRS", 80 * NM, 30 * NM, 6700, 0, 140, "Texaco B", "", true, 2)).ToList();
            var (mA, mB) = (me with { Tel = me.Tel! with { Z = 29 * NM } }, me with { Callsign = "Enfield 2-1", Tel = me.Tel! with { X = 80 * NM, Z = 29 * NM } });
            string K2(Ops x, Me m, string s) => string.Join(" | ", x.OnTranscript("Tanker", s, m, air2, f, 0).Select(y => y.Text));
            var (xa, xb, xc) = (new Ops(), new Ops(), new Ops());
            K2(xa, mA, "Texaco 1-1, Enfield 1-1, request rejoin"); K2(xb, mB, "Texaco 2-1, Enfield 2-1, request rejoin");
            var rj = K2(xc, mA, "Texaco 2-1, Enfield 1-1, request rejoin");
            var (ja, jb) = (K2(xa, mA, "Texaco 1-1, Enfield 1-1, visual"), K2(xb, mB, "Texaco 2-1, Enfield 2-1, visual"));
            var (ca, cb) = (K2(xa, mA, "Texaco 1-1, Enfield 1-1, pre contact"), K2(xb, mB, "Texaco 2-1, Enfield 2-1, pre contact"));
            check(rj.Contains("Texaco two one, bearing three five five, 80 miles") && ja.StartsWith("Enfield one one, Texaco one one, cleared to join") && jb.StartsWith("Enfield two one, Texaco two one, cleared to join")
                  && jb.Contains("number 1.") && !jb.Contains("first, then") && ca.Contains("cleared contact") && cb.Contains("cleared contact"),
                  $"Tanker R119: zwei Texaco, getrennte Warteschlangen -> {rj} | {ja} | {jb} | {ca} | {cb}");
            tkTurn.Clear();
            var (tkA, tkB) = (air2.First(a => a.Group.StartsWith("Texaco (")), air2.First(a => a.Group == "Texaco B"));
            var (qa, qb) = (new Ops { tank = 3, tanker = tkA.Group }, new Ops { tank = 3, tanker = tkB.Group });
            string TurnAt(Ops x, Me m, Traffic k0, double now, double hdg)
            {
                var k = k0 with { Hdg = hdg * Math.PI / 180 };
                var mm = m with { Tel = new Telemetry(k.AltMsl, 6000, 140, k.Hdg, k.X - 110 * Math.Cos(k.Hdg), k.Z - 110 * Math.Sin(k.Hdg), 0, 0, 0) };
                return string.Join(" | ", x.Tick(mm, air2.Select(a => a == k0 ? k : a).ToList(), now, false).Select(y => y.Text));
            }
            for (int i = 1; i <= 10; i++) { TurnAt(qa, mA, tkA, i, 0); TurnAt(qb, mB, tkB, i, 0); }
            var (na, nb) = (TurnAt(qa, mA, tkA, 11, 20), TurnAt(qb, mB, tkB, 11, 20));
            check(na == "Texaco one one, turning right." && nb == "Texaco two one, turning right.", $"Tanker R119: Kurvenansage je Tanker -> {na} | {nb}");
            tkTurn.Clear(); Callsigns = kcSave;
        }
        line.Clear();
        // V18: coaching (too fast, move up) and turn call; player exactly behind the tanker
        var o18 = new Ops { tank = 3, tanker = "Texaco (KC-135MPRS Korb)" };
        var o18b = new Ops { tank = 3, tanker = "Texaco (KC-135MPRS Korb)" };   // second receiver: turn call only once (A121)
        tkTurn.Clear(); Breakaways = 0;
        var tex = air.First(a => a.Group.StartsWith("Texaco"));
        string Co(Ops oo, Me mm, Traffic k0, double now, double along, double hdg = 0)
        {
            var tk = k0 with { Hdg = hdg * Math.PI / 180 };
            var m = mm with { Tel = new Telemetry(tk.AltMsl, 6000, 140, tk.Hdg, tk.X + along * Math.Cos(tk.Hdg), tk.Z + along * Math.Sin(tk.Hdg), 0, 0, 0) };
            return string.Join(" | ", oo.Tick(m, air.Select(a => a == k0 ? tk : a).ToList(), now).Select(x => (x.All ? "*" : "") + x.Text));
        }
        var s18 = new List<string> { Co(o18, me, tex, 1, -120), Co(o18, me, tex, 2, -116) };
        for (int i = 3; i <= 14; i++) s18.Add(Co(o18, me, tex, i, -110));
        s18.Add(Co(o18, me, tex, 15, -110, 10)); s18.Add(Co(o18, me, tex, 16, -110, 20));
        var s18b = new List<string>();
        for (int i = 1; i <= 14; i++) s18b.Add(Co(o18b, me2, tex, i, -110));
        s18b.Add(Co(o18b, me2, tex, 16, -110, 20));
        check(s18[1] == "Enfield one one, slow closure." && s18[11] == "" && s18[^1] == "*Texaco one one, turning right." && s18.Count(x => x != "") == 2 && s18b.All(x => x == ""),
              "Tanker V18/A121/A127: Korb nicht gelenkt, Kurve einmal an alle -> " + string.Join(" / ", s18.Concat(s18b).Where(x => x != "")));
        // A127: boom, short calls near pre-contact; P3-TK3: breakaway on too close an approach
        var shell = air.First(a => a.Group.StartsWith("Shell"));
        var obm = new Ops { tank = 3, tanker = "Shell (KC-135 Boom)" };
        var meF = me with { Type = "F-16C_50" };   // boom tanker only for boom receivers (A125)
        var sb = new List<string>();
        for (int i = 1; i <= 12; i++) sb.Add(Co(obm, meF, shell, i, -80));   // 35 m before the contact point: no steering yet
        for (int i = 13; i <= 23; i++) sb.Add(Co(obm, meF, shell, i, -70));   // 25 m = 82 ft -> "forward 80"
        var brk = Co(obm, meF, shell, 24, -10);
        var brk2 = Co(obm, meF, shell, 25, -10);
        check(sb.Take(12).All(x => x == "") && sb.Contains("Enfield one one, forward 80.") && sb.All(x => !x.Contains("move")) && brk == "Enfield one one, breakaway, breakaway, breakaway." && brk2 == "" && Breakaways == 1,
              "Tanker A127/P3-TK3: Boom forward, Breakaway -> " + string.Join(" / ", sb.Where(x => x != "")) + " | " + brk + " | " + brk2);
        // R289: breakaway resets to observation (pre-contact: immediately, in contact: with the disconnect, without "return to pre-contact"); new contact only after a new clearance
        var obk = new Ops { tank = 3, tanker = "Shell (KC-135 Boom)" };
        obk.OnRefuel(meF, true, 0.4, air, 30);
        Co(obk, meF, shell, 31, -80); var bk4 = Co(obk, meF, shell, 32, -10);
        var (bkTank, bkStop) = (obk.tank, obk.OnRefuel(meF, false, 0.5, air, 33));
        var bkAgain = obk.OnRefuel(meF, true, 0.5, air, 34).FirstOrDefault()?.Text ?? "";
        check(obm.tank == 2 && bk4.EndsWith("breakaway, breakaway, breakaway.") && bkTank == 4 && bkStop.Count == 0 && bkAgain.Contains("not cleared contact"),
              $"R289: Breakaway -> Beobachtung: pre-contact {obm.tank}, Kontakt {bk4} | {bkTank} | {string.Join(" | ", bkStop.Select(x => x.Text))} | {bkAgain}");
        tkTurn.Clear();
        // N44: approach to the tanker without check-in is warned off, once per 3 min; far away or 1500 ft lower not (tick with awacs: false, otherwise the A63 hint comes after 2 min; likewise N38)
        var ob44 = new Ops();
        Me M44(double dx, double alt) => me with { Tel = new Telemetry(alt, 6000, 140, 0, tex.X + dx, tex.Z, 0, 0, 0) };
        var n44_1 = ob44.Tick(M44(2 * NM, tex.AltMsl - 100), air, 1, false).Select(x => x.Text).ToList();
        var n44_2 = ob44.Tick(M44(2 * NM, tex.AltMsl - 100), air, 100, false).Count;
        var n44_3 = ob44.Tick(M44(2 * NM, tex.AltMsl - 100), air, 200, false).Count;
        check(n44_1.SequenceEqual(new[] { "Enfield one one, Texaco one one, you are not cleared to join, remain 1000 feet below, report ready for rejoin." }) && n44_2 == 0 && n44_3 == 1 && ob44.Debrief.Count == 2,
              $"N44: Tanker-Block einmal je 3 min, Debriefing -> {string.Join(" | ", n44_1)} {n44_2} {n44_3}");
        // #20: join requested (tank 1), without "visual" below 1 NM/±500 ft of the tanker: one warning, violation if still there 30 s later; 1000 ft lower nothing
        var j20 = new Ops(); K(j20, M44(5 * NM, tex.AltMsl - 1000 * Ft), "Texaco, Enfield 1-1, request rejoin");
        string J20(double alt, double s) => string.Join(" | ", j20.Tick(M44(0.5 * NM, alt), air, s, false).Select(x => x.Text).Where(x => x.Contains("cleared to join")));
        var w20 = new[] { J20(tex.AltMsl - 1000 * Ft, 1), J20(tex.AltMsl, 2), J20(tex.AltMsl, 10), J20(tex.AltMsl, 40) };
        check(w20[0] == "" && w20[1] == "Enfield one one, Texaco one one, not cleared to join, remain 1000 feet below, report visual." && w20[2] == "" && w20[3] == ""
              && j20.Debrief.Count(d => d.StartsWith("Verstoß: Annäherung an Texaco one one")) == 1 && j20.tank == 1, "#20: Join ohne Freigabe -> " + string.Join(" | ", w20) + " | " + string.Join(" | ", j20.Debrief));
        var ob45 = new Ops();
        check(ob45.Tick(M44(4 * NM, tex.AltMsl), air, 1, false).Count == 0 && ob45.Tick(M44(2 * NM, tex.AltMsl - 1500 * Ft), air, 2, false).Count == 0, "N44: weit weg / 1500 ft tiefer: kein Block");
        // N44: after "complete" no block at the tanker, only again after leaving the block; wingmen of a checked-in lead; join from behind with tanker heading (DCS radio menu)
        var ob46 = new Ops { tank = 2, tanker = "Texaco (KC-135MPRS Korb)" };
        ob46.Tick(M44(20, tex.AltMsl), air, 299, false);   // on the wing, checked in
        ob46.OnTranscript("Tanker", "Texaco, refueling complete", M44(20, tex.AltMsl), air, f, 300);
        int n46a = ob46.Tick(M44(20, tex.AltMsl), air, 301, false).Count, n46b = ob46.Tick(M44(4 * NM, tex.AltMsl), air, 302, false).Count, n46c = ob46.Tick(M44(2 * NM, tex.AltMsl), air, 303, false).Count;
        var ld46 = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
        int n46d = new Ops().Tick(M44(2 * NM, tex.AltMsl), air, 1, false, ld46).Count, n46e = new Ops().Tick(M44(2 * NM, tex.AltMsl), air, 1, false, new Ops()).Count;
        Me M46(double hdg) => me with { Tel = new Telemetry(tex.AltMsl, 6000, 140, hdg, tex.X - NM, tex.Z, 0, 0, 0) };
        int n46f = new Ops().Tick(M46(0.3), air, 1, false).Count, n46g = new Ops().Tick(M46(Math.PI), air, 1, false).Count;
        check(n46a == 0 && n46b == 0 && n46c == 1 && n46d == 0 && n46e == 1 && n46f == 0 && n46g == 1,
              $"N44: complete/Rotte/von hinten kein Block -> {n46a} {n46b} {n46c} {n46d} {n46e} {n46f} {n46g}");
        // N38: intercept from 30 NM, the player flies every vector; at most 4 calls, last "report visual", then silent
        {
            var ov = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
            double px = 0, pz = 0, ph = 0, vt = 6400, tkx = 0, tkz = 30 * NM;   // player (north/east, true heading), tanker flies north
            var said = new List<string>();
            for (int s = 1; s <= 1200; s++)
            {
                tkx += 140; var tkv = air.First(a => a.Group.StartsWith("Texaco")) with { X = tkx, Z = tkz, Hdg = 0 };
                var mv = me with { Tel = new Telemetry(vt, 6000, 200, ph * Math.PI / 180, px, pz, 0, 0, 0) };
                foreach (var c in ov.Tick(mv, air.Select(a => a.Group.StartsWith("Texaco") ? tkv : a).ToList(), s, false))
                {
                    said.Add(c.Text);
                    if (Regex.Match(c.Text, @"heading ((?:\w+ ){2}\w+) for") is { Success: true } hm)
                        ph = (int.Parse(string.Concat(hm.Groups[1].Value.Split(' ').Select(w => Array.IndexOf(DigitWords, w)))) + MagVar) % 360;
                }
                double spd = 200 * (1 + 0.02 * vt / Ft / 1000);
                (px, pz) = (px + spd * Math.Cos(ph * Math.PI / 180), pz + spd * Math.Sin(ph * Math.PI / 180));
                if (said.Count > 0 && said[^1].Contains("report visual")) { said.Add(Dist(px, pz, tkx, tkz) / NM < 6 ? "ok" : "far"); break; }
            }
            check(said.Count is >= 3 and <= 5 && said[0].Contains("for the join") && said[0].Contains("maintain angels 21.")
                  && said[^2].EndsWith(", report visual.") && said[^1] == "ok", "Tanker N38: Abfang aus 30 NM, höchstens 4 Sprüche -> " + string.Join(" / ", said));
            // TP4: head-on from 30 NM (tanker flies south towards the player), the player flies every vector: offset, turn behind; "report visual" behind the 3-9 line, close, not on opposite course
            {
                var ohd = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
                double hx = 0, hz = 0, hh = 0, tx = 30 * NM, ra = 1, rd = 99, rh = 180;
                var hds = new List<string>();
                for (int s = 1; s <= 1200 && ra > 0; s++)
                {
                    tx -= 140; var tkv = air.First(a => a.Group.StartsWith("Texaco")) with { X = tx, Z = 0, Hdg = Math.PI };
                    var mv = me with { Tel = new Telemetry(6400, 6000, 200, hh * Math.PI / 180, hx, hz, 0, 0, 0) };
                    foreach (var c in ohd.Tick(mv, air.Select(a => a.Group.StartsWith("Texaco") ? tkv : a).ToList(), s, false))
                    {
                        hds.Add(c.Text);
                        if (c.Text.EndsWith("report visual.")) (ra, rd, rh) = (Along(mv.Tel!, tkv) / NM, Dist(hx, hz, tx, 0) / NM, HdgDiff(hh, 180));
                        if (Regex.Match(c.Text, @"heading ((?:\w+ ){2}\w+) for") is { Success: true } hm)
                            hh = (int.Parse(string.Concat(hm.Groups[1].Value.Split(' ').Select(w => Array.IndexOf(DigitWords, w)))) + MagVar) % 360;
                    }
                    double spd = 200 * (1 + 0.02 * 6400 / Ft / 1000);
                    (hx, hz) = (hx + spd * Math.Cos(hh * Math.PI / 180), hz + spd * Math.Sin(hh * Math.PI / 180));
                }
                check(ra < 0 && rd <= 6 && rh < 60 && hds.Count <= 8, $"Tanker TP4: Gegenkurs -> hinter 3-9 {ra:0.0} NM, {rd:0.0} NM, Kursdiff {rh:0}° -> " + string.Join(" / ", hds));
            }
            // R372: tanker at 21000 ft -> rendezvous altitude angels 20; player at 8000 ft climbs, at 30000 ft descends
            foreach (var (altM, verb) in new[] { (2438.0, "climb and maintain angels 20."), (9144.0, "descend and maintain angels 20.") })
            {
                var ot372 = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
                var tkv = air.First(a => a.Group.StartsWith("Texaco")) with { AltMsl = 6401 };
                var vec = Enumerable.Range(1, 40).SelectMany(s => ot372.Tick(me with { Tel = new Telemetry(altM, 6000, 200, 0, 0, 0, 0, 0, 0) }, air.Select(a => a.Group.StartsWith("Texaco") ? tkv : a).ToList(), s, false)).Select(c => c.Text).FirstOrDefault(x => x.Contains("for the join"));
                check(vec != null && vec.EndsWith(verb), "Tanker R372: Vektor mit climb/descend -> " + vec);
            }
            // R373: "1 mile" instead of "1 miles" (shared helper), tanker 1 NM ahead
            {
                var o373 = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
                var tk373 = air.First(a => a.Group.StartsWith("Texaco")) with { X = 1 * NM, Z = 0, AltMsl = 6401 };
                var v373 = o373.Tick(me with { Tel = new Telemetry(6400, 6000, 200, 0, 0, 0, 0, 0, 0) }, air.Select(a => a.Group.StartsWith("Texaco") ? tk373 : a).ToList(), 31, false).Select(c => c.Text).FirstOrDefault();
                check(v373 != null && v373.Contains("12 o'clock, 1 mile, report visual.") && MilesTxt(0.4 * NM) == "1 mile" && MilesTxt(2 * NM) == "2 miles", "Tanker R373: 1 mile -> " + v373);
            }
            // N38: receiver turns away after the request (back to the airfield): at most 2 vectors (up to 5 NM farther away), then silent (without abort every 30 s, also after a new start)
            var ow = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
            var gone = new List<string>();
            for (int s = 1; s <= 1200; s++)
            {
                var tkv = air.First(a => a.Group.StartsWith("Texaco")) with { X = 140.0 * s, Z = 30 * NM, Hdg = 0 };
                var mv = me with { Tel = new Telemetry(6400, 6000, 200, Math.PI, -284.0 * s, 0, 0, 0, 0) };
                gone.AddRange(ow.Tick(mv, air.Select(a => a.Group.StartsWith("Texaco") ? tkv : a).ToList(), s, false).Select(c => c.Text));
            }
            check(gone.Count is 1 or 2 && gone[0].Contains("for the join"), "Tanker N38: abgedreht -> höchstens 2 Vektoren, dann still -> " + string.Join(" / ", gone));
            // E1: checked in with the AWACS -> it speaks the join vectors on its frequency, "judy" to it ends them; without check-in the tanker
            var oaw = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)", awacsIn = true };
            var aaw = air.Select(a => a.Group.StartsWith("Texaco") ? a with { X = 0, Z = 30 * NM, Hdg = 0 } : a).ToList();
            var maw = me with { Tel = new Telemetry(6400, 6000, 200, 0, 0, 0, 0, 0, 0) };
            var vaw = oaw.TankVec(maw, aaw, 31, aw: true);
            var vtk = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" }.TankVec(maw, aaw, 31, aw: true);
            var jaw = oaw.OnTranscript("AWACS", "Overlord, Enfield 1-1, judy", maw, aaw, f, 32, false);
            check(vaw is { Role: "AWACS" } && vaw.Text.Contains(", Overlord, turn right") && vaw.Text.Contains("for the join") && vtk?.Role == "Tanker"
                  && jaw is [{ Text: "Enfield one one, Overlord, roger." }] && oaw.TankVec(maw, aaw, 100, aw: true) == null,
                  $"Tanker E1: AWACS gibt die Vektoren -> {vaw?.Role}: {vaw?.Text} / {vtk?.Role} / {string.Join(" / ", jaw.Select(c => c.Text))}");
            var oq = new Ops { tank = 1, tanker = "Texaco (KC-135MPRS Korb)" };
            Vectors = false;
            check(Enumerable.Range(1, 300).All(s => oq.Tick(me, air, s, false).Count == 0), "Tanker N38: Schalter aus -> keine Vektoren");
            Vectors = true;
        }
        JtacTest(check);   // J6-J12
        Bulls.Clear(); Range = null; Beacons.Clear(); Tracks.Clear(); (IdSec, AwacsBullseye, FadeSec, ScanSec) = (idSec, awBulls, fadeSec, scanSec); labels.Clear(); killed.Clear(); scope.Clear(); firstSeen.Clear(); hostileAct.Clear(); net.Clear(); Abm.Reset();
        return 0;
    }
}
