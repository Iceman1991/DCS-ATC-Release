using static DcsAtc.Tower;

namespace DcsAtc;

/// Carrier (M5/M6, plan ch. 5): Marshal, Tower and Paddles (LSO) per player; replaces the Supercarrier radio.
/// Ships come from the mission (C line), mission clock from the Y line.
class Carrier
{
    const double NM = 1852, Ft = 0.3048, Kt = 0.514444;

    /// Ship: Hdg true (rad), speed m/s, freq MHz (0 = Config "Carrier"), TACAN "73X", ICLS channel ("" = none).
    public record Boat(string Unit, string Group, string Type, double X, double Z, double Hdg, double Speed, int Coalition,
                       double Freq, string Tacan, string Icls, double Wx = 0, double Wz = 0, double Pa = 0, bool NoTurn = false)   // Wx, Wz: wind at the deck (toward, m/s); Pa: sea-level pressure; NoTurn: mission does not turn into the wind (land ahead)
    {
        public bool Angled => !Type.Contains("Tarawa");   // Angled deck 9.1° (Nimitz/Ford/Forrestal/Kuznetsov)
        /// R4: centerline of the landing area at the stern, m right of the ship axis; straight deck on the axis.
        /// ponytail: 7 m is the Stennis; add other types here per "[LSO] … Lat beim Trap" (atc-log.txt)
        public double SternLat => Angled ? 7 : 0;
        /// Base Recovery Course and Final Bearing, true in degrees.
        public double Brc => Hdg * 180 / Math.PI;
        public double Fb => (Brc - (Angled ? 9.1 : 0) + 360) % 360;
        /// Into the wind: heading (true °) at which the wind runs along the landing area, and speed (kt) for 27 kt wind over deck.
        /// Below 3 kt wind: heading stays.
        public (double Brc, double Kt) IntoWind
        {
            get
            {
                double w = Math.Sqrt(Wx * Wx + Wz * Wz) / Kt, from = Math.Atan2(-Wz, -Wx) * 180 / Math.PI;
                return w < 3 ? (Brc, 27) : ((from + (Angled ? 9.1 : 0) + 360) % 360, Math.Clamp(27 - w, 5, 30));
            }
        }
        /// Expected BRC/FB for Marshal: into the wind, with land ahead the heading the ship is sailing.
        public double ExpBrc => NoTurn ? Brc : IntoWind.Brc;
        public double ExpFb => (ExpBrc - (Angled ? 9.1 : 0) + 360) % 360;
    }

    public static List<Boat> Boats = new();
    public static double Clock = -1;   // Mission clock in s since midnight, -1 = unknown
    public static double Skew;   // R366: mission clock minus real time at the last Y line (Program); Charlie/EAT run in mission time
    public static double Sun = double.NaN;   // Sun elevation in degrees at the carrier (Y line), NaN = unknown
    public static (bool Clouds, double BaseM, double VisM) Sky = (false, 0, 80000);   // Weather (Program, G line)
    public static bool Ceiling = true;   // R367: lowest layer BKN/OVC (G line); FEW/SCT is no ceiling for the Case; true when the mission does not send it

    /// Case by weather and time: I with ceiling 3000 ft or more and 5 NM visibility by day, II from 1000 ft, otherwise and at night III.
    public static int CaseNow()
    {
        double ceil = Sky.Clouds && Ceiling ? Sky.BaseM / Ft : 99999, vis = Sky.VisM / NM;
        bool night = !double.IsNaN(Sun) ? Sun < -6 : Clock >= 0 && (Clock < 6.5 * 3600 || Clock > 18.5 * 3600);   // Night = sun below -6° (civil twilight); without sun elevation fixed time of day
        return night || ceil < 1000 || vis < 5 ? 3 : ceil < 3000 ? 2 : 1;
    }

    // ------------------------------------------------------------ per player (Case I, M5)
    public int Stage;          // 0 nothing, 1 Marshal holding, 2 Charlie (Case I) or commencing (CV-1), 3 Paddles (initial or 3 NM to groove), 4 ball
    int caseNo = 1, ladder;    // Case at check-in; CV-1 calls: 1 ACLS (say needles), 2 three miles
    bool needlesAsked;
    bool seeMe;                // R280: Case I "see you at" reported (then Tower, no second suggestion)
    string fuel = "";          // R23: last reported fuel ("state 4.5"), for acknowledgment
    double state = double.NaN; // R23: reported fuel in 1000 lb (check-in, "state"), for bingo without fuel gauge
    int passes;                // R23: bolters/waveoffs of this recovery, divert on the third
    bool radar;                // R20: Approach said "radar contact"
    double steerAt = -99;      // R20: last course correction in CV-1
    int angels;
    double charlieAt;          // Case I: Charlie time, Case II/III: Expected Approach Time
    double seenAt = -1e9;      // Review l4: time of the last tick (Suggest "commencing" only from EAT - CommenceEarly)
    const double CommenceEarly = 15;   // R277: "commencing" valid from EAT - 15 s (suggestion and acceptance the same)
    bool ballAsked, balled;    // #16: "call the ball" said; ball or Clara reported (before that no LSO calls, waveoff without ball call)
    double fbSaid = double.NaN;   // R228: last announced Final Bearing (true), NaN = none (Case I)
    double fbSeen = double.NaN, fbSeenAt;   // R228: FB differs by at most 0.5° since fbSeenAt (ship no longer turning)
    string boat = "", cs = "";
    int launch;                // R24: 1 on the deck, 2 Case II/III departure (Departure until "on top" or 20 NM)
    double depRad;             // R283: departure radial (true), set at takeoff
    double deckAt; (double Along, double Lat) deckPos; bool trapped, deckWind;   // R364: last move on the deck (ship frame), landed here (no launch window until airborne), launch window open
    public bool Launching => launch > 0;
    public bool OnDeck => launch == 1;
    /// R231: leading station (handoffs Marshal -> Approach or Tower in Case I, departure Departure).
    string Station => launch == 2 || ladder == -1 ? "Departure" : Stage < 2 ? (seeMe ? "Tower" : "Marshal") : caseNo >= 2 ? "Approach" : "Tower";
    public Carrier? Lead;      // R26: flight lead in formation (Program): his check-in and Charlie apply to the flight
    bool follow;               // R26: stage taken over from the lead (not in the stack)
    int dcsWire;               // R27: wire from the DCS event (LANDING_QUALITY_MARK), 0 = none
    public static bool WireEvents;   // R27: mission reports wires (Program: mission data), then the grade waits briefly for it
    public string BoatGroup => Stage > 0 || launch == 2 || launch == 1 && deckWind ? boat : "";   // Ship on which this player is currently in the recovery or taking off (Program: ship into the wind)
    /// R24: on the deck of an own carrier (on the ground, under 300 m from the ship).
    public static Boat? Deck(Telemetry? t, int side) => t != null && OnGround(t) ? Boats.FirstOrDefault(b => b.Coalition == side && Dist(t.X, t.Z, b.X, b.Z) < 300) : null;
    static readonly Dictionary<string, List<Carrier>> stack = new();   // Holding per ship, first to check in comes first
    static List<Carrier> Stack(string b) { if (!stack.TryGetValue(b, out var l)) stack[b] = l = new(); return l; }
    static readonly Dictionary<string, int> said = new();   // R229: last case announced via "99", per ship
    static readonly HashSet<string> deckDelta = new();   // R276: ships for which "99, signal Delta" (deck delayed) has been said, until the next Charlie
    /// Review l4: new mission or restart (Program.MissionGap): forget the "99" state per ship.
    public static void NewMission() { said.Clear(); deckDelta.Clear(); }

    static Ops.Call Say(string station, string text, Boat b) => new("Carrier", station, text, b.Freq);
    static int MagNum(double trueDeg) { int m = (int)Math.Round(((trueDeg - Ops.MagVar) % 360 + 360) % 360); return m == 0 ? 360 : m; }
    static string Mag(double trueDeg) => Digits(MagNum(trueDeg).ToString("000"));
    /// What is to be done now (text only in game): Case I/III phases in which nobody else says anything.
    static Ops.Call Hint(string s) => new("Info", "", L("Träger: ", "Carrier: ") + s, 0);
    static string ZipLip => L("Initial: zip lip, kein Funk bis zum eigenen Ball-Call (ca. 3/4 NM: „Hornet ball, 5.2“).", "Initial: zip lip, no radio until your own ball call (about 3/4 NM: \"Hornet ball, 5.2\").");
    static string DescendIII => L("4000 ft/min auf 5000 ft, dort „platform“ melden; dann 2000 ft/min auf 1200 ft; bei 10 DME Fahrwerk/Klappen/Haken.",
                                  "4000 ft/min to 5000 ft, call \"platform\" there; then 2000 ft/min to 1200 ft; at 10 DME gear/flaps/hook down.");
    static Boat? Nearest(Ops.Me me) => Boats.Where(b => b.Coalition == me.Coalition)
                                            .MinBy(b => me.Tel == null ? 0 : Dist(me.Tel.X, me.Tel.Z, b.X, b.Z));
    Boat? Mine => Boats.FirstOrDefault(b => b.Group == boat);

    /// N51: approach without check-in (Program.BoatWatch): callsign or "unidentified aircraft" with position from the ship, heading, altitude; second: after 60 s still under 2 NM.
    /// N51b: the first call redirects right away (away: true heading, rounded to 10°).
    public static Ops.Call WarshipCall(Boat b, Telemetry t, string? cs, bool second, double away)
    {
        var nm = Math.Max(1, (int)Math.Round(Dist(t.X, t.Z, b.X, b.Z) / NM));
        double hdg = t.Hdg * 180 / Math.PI;
        var who = cs ?? $"Unidentified aircraft {nm} mile{(nm == 1 ? "" : "s")} {Dir8(Bearing(b.X, b.Z, t.X, t.Z))} of the carrier, heading {Mag(hdg)}, {Math.Round(t.AltMsl / Ft / 100) * 100:0} feet";
        int h = (int)Math.Round(MagNum(away) / 10.0) * 10;
        var turn = HdgDiff(away, hdg) < 15 ? "fly" : $"turn {(((away - hdg) % 360 + 540) % 360 - 180 >= 0 ? "right" : "left")}";
        var hd = $"{turn} heading {Digits((h == 0 ? 360 : h).ToString("000"))}";
        // R249: the Carrier Control Zone (5 NM, CV NATOPS) is run by the Tower (Air Boss) in Case I/II, by Approach (CATCC) in Case III; unknowns get the warship's warning formula
        var st = CaseNow() == 3 ? "Approach" : "Tower";
        if (cs == null)
        {
            var ship = $"{(b.Coalition == 2 ? "US Navy" : "coalition")} warship";
            return Say(st, second ? $"{who}, this is {ship}, remain clear 5 miles, identify yourself and state your intentions."
                                  : $"{who}, this is {ship}, you are approaching a {ship}, {hd}, remain clear 5 miles, identify yourself and state your intentions.", b);
        }
        return Say(st, second ? $"{who}, {st}, remain clear of the carrier control zone, say intentions."
                              : $"{who}, {st}, you are {(Dist(t.X, t.Z, b.X, b.Z) <= 5 * NM ? "inside" : "approaching")} the carrier control zone, {hd}, remain clear 5 miles, say intentions.", b);
    }

    /// Position relative to the ship: along (aft negative) and across (right positive) the axis brgDeg (true) through the ship's center.
    static (double Along, double Lat) Rel(Boat b, Telemetry t, double brgDeg)
    {
        double r = brgDeg * Math.PI / 180, dx = t.X - b.X, dz = t.Z - b.Z;
        return (dx * Math.Cos(r) + dz * Math.Sin(r), dz * Math.Cos(r) - dx * Math.Sin(r));
    }

    /// In the groove (A74): aft up to 1.5 NM, at most 0.3 NM off the centerline, under 1500 ft.
    static bool InGroove(Boat b, Telemetry t) =>
        Landing(b, t) is var (d, l) && d is > -1.5 * NM and < 0 && Math.Abs(l) < 0.3 * NM && t.AltMsl < 1500 * Ft;

    /// Deviation from glide path and centerline in degrees (high/right positive), as with the "three miles" call.
    static (double Hi, double Rt) Dev(Boat b, Telemetry t)
    {
        var (deck, lat) = Landing(b, t);
        double d = -deck + Aim;
        return (Math.Atan2(t.AltMsl - Hook - DeckH, d) * 180 / Math.PI - Gs, Math.Atan2(lat, d) * 180 / Math.PI);
    }
    /// R230: wind over deck for "Roger ball" (Case III): strength in kt, "axial" (within 5° of the landing area) or from port/starboard.
    static string Wod(Boat b)
    {
        double x = b.Wx - Math.Cos(b.Hdg) * b.Speed, z = b.Wz - Math.Sin(b.Hdg) * b.Speed;   // Air against the ship (toward)
        double off = ((Math.Atan2(-z, -x) * 180 / Math.PI - b.Fb) % 360 + 540) % 360 - 180;   // which side relative to FB, right positive
        return $"{Math.Round(Math.Sqrt(x * x + z * z) / Kt)} knots, {(Math.Abs(off) <= 5 ? "axial" : off > 0 ? "starboard" : "port")}";
    }
    static int Sgn(double dev) => dev > 0.5 ? 1 : dev < -0.5 ? -1 : 0;   // up to 0.5° = "on"
    static string DevText(double hi, double rt) => $"{(Sgn(hi) > 0 ? "above glidepath" : Sgn(hi) < 0 ? "below glidepath" : "on glidepath")}, {(Sgn(rt) > 0 ? "right of course" : Sgn(rt) < 0 ? "left of course" : "on course")}";
    /// Needle call "up and on", "down and left", "on and on" -> (glide path, course) each -1/0/+1 (up/right positive); null = not recognized.
    static (int V, int H)? Needles(string n)
    {
        var m = System.Text.RegularExpressions.Regex.Match(n, @"\b(up|down|on) and (left|right|on)\b");
        return m.Success ? (Array.IndexOf(new[] { "down", "on", "up" }, m.Groups[1].Value) - 1, Array.IndexOf(new[] { "left", "on", "right" }, m.Groups[2].Value) - 1) : null;
    }

    /// Radio call to the carrier? (Marshal/Paddles by name, ball call; "initial"/"see you at" only if already checked in)
    public bool Wants(string n) => Boats.Count > 0 &&
        (Has(n, "marshal", "mother", "paddles", "ball", "clara", "carrier", "pigeons") ||
         Stage >= 1 && Has(n, "initial", "see you at", "commencing", "charlie", "platform", "state", "needles", "up and", "down and", "on and on") ||
        launch == 2 && Has(n, "airborne", "on top", "departure", "passing", "arcing", "outbound", "kilo"));

    /// Next radio call to the carrier (selection wheel: Enter), texts like the wheel entries. null = none (ball only after "call the ball", nothing on the deck).
    public (string Role, string Text)? Suggest(Telemetry? t) => Stage switch
    {
        0 => launch == 2 ? ("Carrier", !radar ? "airborne" : t == null ? "on top" : $"on top, angels {Math.Max(1, (int)Math.Round(t.AltMsl / Ft / 1000))}")   // R24: departure
           : t != null && OnGround(t) ? null : ("Carrier", "Marshal, checking in"),
        1 => caseNo >= 2 ? (seenAt >= charlieAt - CommenceEarly ? ("Carrier", "commencing") : null) : seeMe ? null : ("Carrier", "see you at angels"),   // R280: "see me" only once; R224: Case II/III at EAT "commencing" (R277: only when Marshal accepts it)
        2 => caseNo == 1 ? null : caseNo == 2 && t != null && Mine is { } sb && Dist(t.X, t.Z, sb.X, sb.Z) < 12 * NM ? ("Carrier", "see you at ten")   // #18: Case II in sight
           : ladder == 0 && !radar ? ("Carrier", "platform") : null,   // R280: "platform" until contact with Approach, not again after a bolter; Case I: zip lip, no initial call (R134); Case II/III: afterwards needles/position come from Approach
        3 => ballAsked ? ("Carrier", "ball") : null,
        _ => null,
    };

    /// Answer to "say needles": what the ACLS needles show (fly-to, like Needles/Dev), null without ship/position.
    public string? NeedlesSay(Telemetry? t) => t != null && Mine is { } b && Dev(b, t) is var (hi, rt)
        ? $"{new[] { "down", "on", "up" }[Sgn(-hi) + 1]} and {new[] { "left", "on", "right" }[Sgn(-rt) + 1]}" : null;

    /// unsure: Whisper confidence below the threshold (R16) -> ask again, change no stage.
    public List<Ops.Call> OnTranscript(string text, Ops.Me me, double now, bool unsure = false)
    {
        var n = Normalize(text.Contains(':') ? text[(text.IndexOf(':') + 1)..] : text);
        now = Mt(now);   // R366: only Charlie/EAT use the time here
        var b = Mine ?? Nearest(me);
        if (b == null) return new();
        boat = b.Group;
        var c = cs = SpokenCallsign(me.Callsign);
        bool grooved = Stage >= 3 && me.Tel is { } tg && InGroove(b, tg);   // R133: in the groove only Paddles speaks, Marshal never (LSO NATOPS)
        if (unsure && !(grooved && Has(n, "ball", "clara", "clarence"))) return grooved ? new() : new() { Say(Station, $"{c}, {Station}, say again.", b) };   // uncertain ball call in the groove counts; otherwise "call the ball" asks again; R231: the leading station
        if (launch == 1) return new() { Hint(L("Auf dem Deck kein Funk (kein Land-Tower): Anlassen, Rollen und Katapult nach Zeichen der Deckcrew.",
                                               "No radio on deck (no land tower): start, taxi and catapult on the deck crew's signals.")) };   // R24 (EMCON)
        if (launch == 2 && Has(n, "airborne")) { radar = true; return new() { Say("Departure", $"{c}, Departure, radar contact, departure radial {Mag(depRad)}.", b) }; }   // R24: Case II/III (CV NATOPS); R283: with departure radial
        if (launch == 2 && Has(n, "on top")) return Handoff(me, b);
        if (launch == 2 && Has(n, "passing", "arcing", "outbound", "established", "kilo")) return new() { Say("Departure", $"{c}, roger.", b) };   // R233: mandatory reports in the Case III departure are acknowledged by Departure
        if (Has(n, "clara", "clarence", "ball"))
        {
            if (me.Tel is { } gt && !InGroove(b, gt)) return new();   // A74: only in the groove, otherwise no radio and no stage (not checkable without position: as before)
            balled = true;   // #16: Clara counts as ball call (LSO leads), stage 4 only with ball
            if (Has(n, "clara", "clarence")) return new() { Say("Paddles", $"{c}, roger Clara, keep it coming.", b) };
            Stage = 4; OffStack();
            return new() { Say("Paddles", caseNo == 3 ? $"Roger ball, {Wod(b)}." : "Roger ball.", b) };   // R230: Case III/night with wind over deck
        }
        if (needlesAsked && Has(n, "needles", "up and", "down and", "on and") && Needles(n) is { } nd)
        {
            needlesAsked = false;
            // A129: needles are fly-to (up = aircraft too low, left = right of the line); if the report is wrong, Approach states the offset
            if (me.Tel is { } nt && Dev(b, nt) is var (hi, rt) && (Sgn(-hi), Sgn(-rt)) != nd)
                return new() { Say("Approach", $"{c}, disregard needles, {DevText(hi, rt)}.", b) };
            return new() { Say("Approach", $"{c}, concur.", b) };
        }
        if (caseNo >= 2 && (Stage >= 1 && Has(n, "platform") || Stage == 2 && !radar && Has(n, "commencing", "commence"))) return new() { Say("Approach", radar ? $"{c}, roger." : Contact(me, b), b) };   // R20/R282: first report to Approach (after "switch Approach") identifies Approach with distance
        if (caseNo >= 2 && Stage == 1 && Has(n, "commencing", "commence"))   // #17/R224: the pilot reports start of approach himself; R225: Marshal identifies with Final Bearing; R282: and hands off to Approach
        {
            // Review l4: too early (before EAT, tolerance 15 s as usual in fleet operations, ±10–15 s at the push point): Marshal declines, gives the time, the flight stays in holding
            if (Stage == 1 && now < charlieAt - CommenceEarly)
                return new() { Say("Marshal", Clock >= 0 ? $"{c}, negative, your expected approach time is {Mm()}."
                                                         : $"{c}, negative, continue holding, {Math.Max(1, (int)Math.Round((charlieAt - now) / 60))} minutes to your approach time.", b) };
            Stage = 2; OffStack(); radar = false; fbSaid = b.ExpFb; delta = false;   // R282: radar = contact with Approach, only on its first report
            return new() { Say("Marshal", $"{c}, radar contact, final bearing {Mag(b.ExpFb)}, switch Approach.", b), Hint(DescendIII) };
        }
        if (caseNo == 1 && Stage == 1 && Has(n, "initial"))   // R278: no initial without Charlie: rejection, holding, stack and Charlie time stay (CV NATOPS Case I)
            return new() { Say(Station, $"{c}, negative, not Charlie, hold angels {angels}{(Clock >= 0 ? ", expected Charlie " + Mm() : "")}.", b) };
        if (caseNo == 1 && Has(n, "initial"))   // R1: only Case I jumps to Paddles (zip lip, text only, P3-T5), from Charlie (stage 2)
        {
            Stage = 3; OffStack();
            return new() { Hint(ZipLip) };
        }
        if (Has(n, "initial", "commencing", "commence", "charlie"))   // R1: "Charlie" is an acknowledgment, "commencing" from stage 2 only roger (CV NATOPS CCA)
            return new() { Say(Station, $"{c}, roger.", b) };
        if (caseNo == 2 && Stage == 2 && Has(n, "see you at"))   // #18/R227: Case II ship in sight -> Tower, from the initial Case I pattern (zip lip)
        {
            caseNo = 1;
            return new() { Say("Approach", $"{c}, roger, switch Tower.", b),
                           Hint(L($"Sinken zum Initial: 3 NM hinter dem Heck, 800 ft, Kurs {MagNum(b.ExpBrc):000}, 350 kt; Break über dem Bug nach links, Downwind 600 ft.",
                                    $"Descend to the initial: 3 NM astern, 800 ft, heading {MagNum(b.ExpBrc):000}, 350 kt; break left over the bow, downwind 600 ft.")) };
        }
        if (caseNo == 1 && Stage == 1 && !seeMe && Has(n, "see you at"))   // R232: Case I in holding (CV NATOPS); R280: once, fuel in the call counts (bingo), then Tower
        {
            seeMe = true;
            if (FuelSlot(n) is { } sf && Signal(me, b, "Marshal", false, (state = sf) * 1000) is { } sg) return sg;
            return new() { Say("Marshal", $"{c}, {(FuelSlot(n) != null ? "roger" : "update state")}, switch Tower.", b) };
        }
        if (Stage >= 1 && Has(n, "see you at", "established", "holding", "in the stack"))
            return new() { Say(Station, $"{c}, roger.", b) };   // R280: after "see you at" the Tower
        if (Has(n, "pigeons", "tacan", "say position"))   // N29: bearing/distance to the ship, no check-in (stack, stage and Charlie stay)
        {
            var pos = "";
            if (me.Tel is { } pt)
            {
                var nm = Math.Max(1, (int)Math.Round(Dist(pt.X, pt.Z, b.X, b.Z) / NM));
                pos = $"mother bears {Mag(Bearing(pt.X, pt.Z, b.X, b.Z))}, {nm} mile{(nm == 1 ? "" : "s")}, ";
            }
            var tac = b.Tacan != "" ? $", TACAN {Digits(b.Tacan)} {(b.Tacan.EndsWith('Y') ? "Yankee" : "X-ray")}" : "";
            var icls = b.Icls != "" ? $", ICLS channel {Digits(b.Icls)}" : "";
            return new() { Say("Marshal", $"{c}, {pos}expected BRC {Mag(b.ExpBrc)}{tac}{icls}.", b) };
        }
        if (caseNo >= 2 && Stage == 1 && Has(n, "radial", "dme", "angels") && !Has(n, "check in", "checking in", "inbound", "recovery", "state"))   // R226/A25: readback of the Marshal instruction, no new check-in
        {
            string? Num(string re, int keep) => System.Text.RegularExpressions.Regex.Match(n, re) is { Success: true } m && m.Groups[1].Value.Replace(" ", "") is var d ? d[Math.Max(0, d.Length - keep)..] : null;   // strip the callsign digits in front
            var wrong = new[] { (Num(@"(\d+(?: \d+)*) radial", 3), MagNum(b.ExpFb + 180), $"{Mag(b.ExpFb + 180)} radial"), (Num(@"(\d+(?: \d+)*) dme", 2), angels + 15, $"{angels + 15} DME"),
                                (Num(@"\bangels (\d+)", 2), angels,$"angels {angels}") }
                        .Where(e => e.Item1 != null && int.Parse(e.Item1) != e.Item2).Select(e => e.Item3).ToList();
            return new() { Say("Marshal", wrong.Count == 0 ? $"{c}, readback correct." : $"{c}, negative, {string.Join(", ", wrong)}.", b) };
        }
        if (caseNo == 1 && Stage == 1 && !Has(n, "state") && System.Text.RegularExpressions.Regex.IsMatch(n, @"\b\d+ point \d\b")) n = System.Text.RegularExpressions.Regex.Replace(n, @"\b(?=\d+ point \d\b)", "state ");   // R232: answer to "update state" ("Enfield 1-1, 5.9")
        if (Stage >= 1 && Has(n, "state") && !Has(n, "check in", "checking in", "inbound", "recovery"))   // R23: acknowledge fuel report in holding, no new check-in (stack, stage and Charlie stay)
        {
            var fm = System.Text.RegularExpressions.Regex.Match(n, @"\bstate ((?:\d ?)+)(?:point ((?:\d ?)+))?");
            fuel = fm.Success ? Digits(fm.Groups[1].Value) + (fm.Groups[2].Success ? " point " + Digits(fm.Groups[2].Value) : "") : "";
            state = FuelSlot(n) ?? state;
            if (Signal(me, b, "Marshal", false, state * 1000) is { } sg) return sg;   // R23: reported fuel below bingo
            return new() { Say("Marshal", $"{c}, Marshal, roger{(fuel != "" ? ", state " + fuel : "")}.", b) };
        }
        if (Stage == 1 && Has(n, "marshal", "mother") && !Has(n, "check in", "checking in", "inbound", "recovery"))   // R368: repeated call in holding -> Marshal repeats the assignment, stack and EAT stay
            return Instr(me, b);
        if (Has(n, "marshal", "mother", "check in", "checking in", "inbound", "recovery", "state"))
        {
            state = FuelSlot(n) ?? double.NaN;
            if (Signal(me, b, "Marshal", false, state * 1000) is { } sg) return sg;   // R23: already below bingo at check-in
            caseNo = CaseNow();
            Stage = 1; ladder = launch = 0; delta = radar = follow = false;
            return Assign(me, b, now);
        }
        return new() { Say(Station, $"{c}, {Station}, say again.", b) };   // R231
    }

    /// Charlie time or EAT as minute of the hour ("zero seven"), "" without mission clock.
    string Mm() => Clock >= 0 ? Digits(((int)(charlieAt / 60) % 60).ToString("00")) : "";   // R366: charlieAt is mission time
    static double Mt(double now) => Clock >= 0 ? now + Skew : now;
    static double Minute(double t) => Clock >= 0 ? Math.Ceiling(t / 60) * 60 : t;   // R365: EAT/Charlie on a full mission minute
    static string Word(int caseNo) => caseNo switch { 1 => "one", 2 => "two", _ => "three" };
    static string Alt(Ops.Me me, Boat b) => (b.Pa > 0 ? b.Pa / 100 : me.Tel != null && Baro(me.Tel) is { } q ? q.Qnh : 0) is > 0 and var qnh ? ", " + QnhSay(qnh, InHg(me.Type)) : "";
    /// Check-in or new Case in holding (R229): slot in the stack, angels, Charlie time or EAT, Marshal instruction.
    List<Ops.Call> Assign(Ops.Me me, Boat b, double now)
    {
        seeMe = false;
        var st = Stack(b.Group);
        if (!st.Contains(this)) st.Add(this);
        int low = caseNo == 1 ? 2 : 6, gap = caseNo == 1 ? 90 : 60;   // Case I from angels 2, Charlie every 1.5 min; II/III from angels 6, EAT every minute
        angels = Enumerable.Range(low, 30).First(a => !st.Any(o => o != this && o.angels == a));
        double eta = me.Tel == null ? 0 : Math.Max(0, Dist(me.Tel.X, me.Tel.Z, b.X, b.Z) - (caseNo == 1 ? 5 : angels + 15) * NM) / Math.Max(me.Tel.Ias, 100);   // until holding
        charlieAt = Minute(Math.Max(now + Math.Max(caseNo == 1 ? 120 : 300, eta + (caseNo == 1 ? 60 : 120)), st.Where(o => o != this).Select(o => o.charlieAt + gap).DefaultIfEmpty(0).Max()));
        return Instr(me, b);
    }
    /// Marshal instruction for the assigned slot; R368: repeated unchanged on "say again" in holding.
    List<Ops.Call> Instr(Ops.Me me, Boat b)
    {
        var c = cs;
        fbSaid = caseNo >= 2 ? b.ExpFb : double.NaN;   // R228
        var alt = Alt(me, b);
        var mm = Mm();
        if (caseNo == 1)
            return new() { Say("Marshal", $"{c}, Marshal, case one recovery, expected BRC {Mag(b.ExpBrc)}{alt}, hold angels {angels}{(mm != "" ? $", expected Charlie {mm}" : "")}, report see me.", b),   // R232
                           Hint(L($"Holding über dem Schiff in Angels {angels}, Linkskreise (ca. 5 NM Ø). Warten auf „signal Charlie“.", $"Hold overhead the ship at angels {angels}, left-hand circles (about 5 NM wide). Wait for \"signal Charlie\".")) };
        return new() { Say("Marshal", $"{c}, Marshal, case {Word(caseNo)} recovery, CV-1 approach, expected final bearing {Mag(b.ExpFb)}{alt}. " +
                                      $"Marshal mother's {Mag(b.ExpFb + 180)} radial, {angels + 15} DME, angels {angels}.{(mm != "" ? $" Expected approach time {mm}." : "")}", b),
                       Hint(L($"Zum Marshal-Punkt: Radial {MagNum(b.ExpFb + 180):000}, {angels + 15} DME (TACAN {b.Tacan}), Angels {angels}; dort Holding, zur Approach-Zeit „commencing“ melden.",
                                $"To the marshal point: radial {MagNum(b.ExpFb + 180):000}, {angels + 15} DME (TACAN {b.Tacan}), angels {angels}; hold there and call \"commencing\" at your approach time.")) };
    }

    /// Save session (app crash/restart): recovery state; in holding back into the stack by Charlie time.
    public record State(int Stage, int Case, int Angels, double CharlieAt, string Boat, string Cs, int Ladder, bool NeedlesAsked, bool BallAsked);
    public State Save() => new(Stage, caseNo, angels, charlieAt, boat, cs, ladder, needlesAsked, ballAsked);
    public void Load(State s)
    {
        (Stage, caseNo, angels, charlieAt, boat, cs, ladder, needlesAsked, ballAsked) = (s.Stage, s.Case, s.Angels, s.CharlieAt, s.Boat, s.Cs, s.Ladder, s.NeedlesAsked, s.BallAsked);
        if (Stage == 1) { var st = Stack(boat); st.Add(this); st.Sort((a, b) => a.charlieAt.CompareTo(b.charlieAt)); }
    }

    void OffStack() { foreach (var l in stack.Values) l.Remove(this); }
    /// Player gone (left slot, crashed).
    public void Leave()   // R2: everything per approach reset, otherwise "call the ball" is missing in the next recovery or an old waveoff keeps counting
    {
        OffStack(); Stage = ladder = passes = launch = dcsWire = 0; needlesAsked = ballAsked = balled = foul = noGear = noBall = waved = bolterTold = delta = radar = follow = seeMe = trapped = false; cca = double.MaxValue; fbSaid = double.NaN;
        groove.Clear(); touchAt = -1; steerAt = -99;
    }

    /// R24: takeoff from the deck. Nothing on the deck; after takeoff Case I text only (500 ft parallel to BRC up to 7 NM),
    /// Case II/III Departure ("airborne" -> radar contact, "on top" or 20 NM -> AWACS). null = no takeoff.
    List<Ops.Call>? Launch(Ops.Me me, Telemetry t, double now)
    {
        if (Deck(t, me.Coalition) is { } db)   // R364: ship into the wind only while taxiing or up to 10 min after spawn/last move, not after a trap
        {
            var p = Rel(db, t, db.Brc);
            if (launch != 1 || Math.Abs(p.Along - deckPos.Along) + Math.Abs(p.Lat - deckPos.Lat) > 50) (deckAt, deckPos) = (now, p);   // ponytail: 50 m against position jitter on the moving ship; shorter taxi moves do not reopen the window
            (launch, boat, cs, deckWind) = (1, db.Group, SpokenCallsign(me.Callsign), !trapped && now - deckAt < 600);
            return new();
        }
        trapped = false;
        if (launch == 0 || Mine is not { } b) { launch = 0; return null; }
        if (launch == 1)
        {
            int lc = CaseNow();
            launch = lc >= 2 ? 2 : 0; radar = false;
            depRad = (b.Brc + 30) % 360;   // R283: departure radial (ponytail: fixed BRC + 30°, not the BRC itself; derive from the mission route if desired)
            int brc = MagNum(b.Brc), rad = MagNum(depRad);
            return new() { Hint(lc == 1 ? L($"Case-I-Abflug: geradeaus parallel zum BRC {brc:000}, 500 ft bis 7 NM, dann frei steigen; kein Funk.",
                                            $"Case I departure: straight ahead parallel to BRC {brc:000}, 500 ft until 7 NM, then climb unrestricted; no radio.")
                              : lc == 2 ? L($"Case-II-Abflug: „airborne“ melden, geradeaus parallel zum BRC {brc:000} in Sicht bleiben, bei 7 DME auf den 10-DME-Bogen zum Abflugradial {rad:000}, erst darauf durch die Wolken steigen, über den Wolken „on top“ melden.",
                                            $"Case II departure: call \"airborne\", straight ahead parallel to BRC {brc:000} staying visual, at 7 DME arc at 10 DME to the {rad:000} departure radial, climb through the clouds only on it, call \"on top\" above the clouds.")
                              : L($"Case-III-Abflug: „airborne“ melden, geradeaus steigen, bei 7 DME auf den 10-DME-Bogen zum Abflugradial {rad:000}, darauf auswärts, über den Wolken „on top“ melden.",
                                  $"Case III departure: call \"airborne\", climb straight ahead, at 7 DME arc at 10 DME to the {rad:000} departure radial, then outbound on it, call \"on top\" above the clouds.")) };
        }
        return Dist(t.X, t.Z, b.X, b.Z) > 20 * NM ? Handoff(me, b) : null;
    }

    /// R24: Departure hands off (AWACS as A21, otherwise frequency change).
    List<Ops.Call> Handoff(Ops.Me me, Boat b)
    {
        launch = 0;
        return new() { Say("Departure", Ops.AwacsContact(Air, me, Freqs.GetValueOrDefault("AWACS", 251.5)) is { } aw ? $"{cs}, Departure, switch {aw}." : $"{cs}, Departure, cleared to switch.", b) };   // R233: military instead of FAA civil phrase
    }

    /// R27: wire from the DCS event (mission "W;Unit;Wire"), applies to the current approach.
    public void OnWire(int w) { if (Stage >= 3 && w is >= 1 and <= 4) dcsWire = w; }

    /// R20: Approach identified ("radar contact, 18 miles").
    string Contact(Ops.Me me, Boat b)
    {
        radar = true;
        return $"{cs}, Approach, radar contact{(me.Tel is { } t ? $", {MilesTxt(Dist(t.X, t.Z, b.X, b.Z))}" : "")}.";
    }

    /// R23: alternate airfield (Program: nearest own/neutral with a long runway, N1); replaced in the self-test.
    public static Func<Telemetry, int, Airfield?> Alternate = Program.EmergencyField;
    /// Fuel in lb: fuel gauge (mission) times tank capacity per type, otherwise the reported; NaN = unknown.
    double Lb(Ops.Me me) => me.Fuel >= 0 && CvType(me.Type).Lb is > 0 and var l ? me.Fuel * l : state * 1000;
    /// R23/N18: fuel below bingo (distance to the alternate) or divert (third bolter/waveoff) -> signal with bearing/distance, recovery ends (Gone).
    /// Without own airfield no signal. ponytail: bingo = 1500 lb reserve + 20 lb/NM (Hornet, high); calibrate per type if players are sent too early/late.
    List<Ops.Call>? Signal(Ops.Me me, Boat b, string station, bool divert, double lb)
    {
        if (me.Tel is not { } t || Alternate(t, me.Coalition) is not { } f) return null;
        double d = Dist(t.X, t.Z, f.X, f.Z) / NM, brg = Bearing(t.X, t.Z, f.X, f.Z);
        bool bingo = lb < 1500 + 20 * d;   // NaN: unknown, no bingo
        if (!bingo && !divert) return null;
        var name = f.StationOf("").Trim();
        int nm = Math.Max(1, (int)Math.Round(d));
        Leave(); Gone = true;
        return new() { Say(station, $"{cs}, your signal is {(bingo ? "bingo" : "divert")}, pigeons {name} {Mag(brg)}, {(nm == 1 ? "1 mile" : $"{nm} miles")}.", b),
                       Hint(L($"{(bingo ? "Bingo" : "Divert")}: {name}, Kurs {MagNum(brg):000}, {nm} NM. Recovery beendet.", $"{(bingo ? "Bingo" : "Divert")}: {name}, heading {MagNum(brg):000}, {nm} NM. Recovery ended.")) };
    }
    public bool Gone;   // A21: leave recovery (50 NM or ashore): Program releases OnBoat, airfield radio back to the Tower
    bool inHold, delta;   // A20: under 10 NM (for the Charlie of the others in the stack); said once: "signal Delta" (Case I), "report commencing" (#17), "report see you at ten" (#18)
    double cca = double.MaxValue;   // A21: edge of the Carrier Control Area, 50 NM or 10 NM farther than the nearest (check-in from farther away)
    public string? LatLog;   // R4: position relative to the centerline at the trap (Program logs, to calibrate SternLat)
    /// Player really in the groove (aft up to 1 NM to just over the deck, near the centerline, low), not already from the initial or in the downwind after a bolter.
    public bool InGroove(Telemetry t) => Stage >= 3 && Mine is { } b && Landing(b, t) is var (d, l) && d is > -NM and < 300 && Math.Abs(l) < 0.3 * NM && t.AltMsl < 1000 * Ft;

    /// Every second: signal Charlie (first in the stack, time reached), detect initial by itself, "call the ball", landing.
    public List<Ops.Call> Tick(Ops.Me me, double now)
    {
        var t = me.Tel;
        double mt = Mt(now);   // R366: Charlie/EAT in mission time, LSO timing in real time
        seenAt = mt;
        if (t == null) return new();
        if (Lead is { Stage: 1 or 2, caseNo: >= 2 } lw && (Stage == 0 || follow) && !OnGround(t) && Boats.FirstOrDefault(x => x.Group == lw.boat) is { } lb)   // R279: Case II/III single approach: own check-in (angels, EAT one minute behind the lead), take nothing over from the lead
        {
            (Stage, caseNo, boat, cs, ladder, launch, follow, radar, delta) = (1, lw.caseNo, lw.boat, SpokenCallsign(me.Callsign), 0, 0, false, false, false);
            OffStack();
            return Assign(me, lb, mt);
        }
        if (Lead is { Stage: 1 or 2, caseNo: 1 } l && Stage < l.Stage && !OnGround(t))   // R26: flight in formation, Case I: check-in or Charlie of the lead applies to the flight (radio via the lead); not on the deck (lead circles above)
        {
            (Stage, caseNo, angels, charlieAt, boat, cs, ladder, launch, follow, radar) = (l.Stage, l.caseNo, l.angels, l.charlieAt, l.boat, SpokenCallsign(me.Callsign), 0, 0, true, false);   // radar: Departure contact does not count for Approach
            OffStack();
        }
        else if (Lead == null && follow)   // Leave formation: in holding own slot in the stack by Charlie time
        {
            follow = false;
            if (Stage == 1 && Stack(boat) is var st && !st.Contains(this)) st.Insert(st.FindLastIndex(o => o.charlieAt <= charlieAt) + 1, this);   // behind the lead (same time)
        }
        if (Stage == 0 && Launch(me, t, now) is { } lc) return lc;
        if (Stage == 0 && Nearest(me) is { } gb && Landing(gb, t) is var (gd, gl) && gd is > -NM and < -0.5 * NM && Math.Abs(gl) < 0.2 * NM && t.AltMsl < 1000 * Ft
            && t.Ias < GrooveIas && t.Gear is < 0 or >= 0.5 && HdgDiff(t.Hdg * 180 / Math.PI, gb.Fb) <= 15)   // R26: without check-in/Charlie in the groove: LSO looks after every own aircraft
            (Stage, caseNo, boat, cs, launch) = (3, CaseNow(), gb.Group, SpokenCallsign(me.Callsign), 0);
        if (Stage == 0 || Mine is not { } b) return new();
        var c = cs;
        var (al, lat) = Rel(b, t, b.Brc);
        double rel = Math.Sqrt(Sq(Math.Cos(t.Hdg) * t.Ias - Math.Cos(b.Hdg) * b.Speed) + Sq(Math.Sin(t.Hdg) * t.Ias - Math.Sin(b.Hdg) * b.Speed));
        if (Dist(t.X, t.Z, b.X, b.Z) < 300 && t.AltMsl < DeckH + 4 && rel < 15)   // Trap: stands on the deck
        {
            if (WireEvents && dcsWire == 0 && touchAt > 0 && now - touchAt < 5) return new();   // R27: wait for the mission's wire event (comes after the catch), otherwise the own estimate
            if (dcsWire > 0) wire = dcsWire;
            var note = waved ? Note("cut") : groove.Count > 0 ? Note("trap") : "";   // R103: waveoff ignored and landed = cut pass (LSO NATOPS), also without samples
            LatLog = FormattableString.Invariant($"[LSO] {me.Callsign}: Lat beim Trap {Landing(b, t).Lat:+0.0;-0.0} m ({b.Type}, SternLat {b.SternLat})");
            Leave(); trapped = true;   // also noGear: waveoff ignored and landed, do not carry the reason into the next approach
            return new() { new("Info", "", note != "" ? $"LSO: {note}" : L($"Träger: gelandet auf {b.Unit}.", $"Carrier: landed on {b.Unit}."), 0) };
        }
        double dist = Dist(t.X, t.Z, b.X, b.Z);
        if (OnGround(t) && t.Ias < 50 * Kt && dist > NM) { Leave(); Gone = true; return new(); }   // landed ashore and rolled out (alternate): out of carrier traffic, radio wheel back from the Tower
        cca = Math.Max(50 * NM, Math.Min(cca, dist + 10 * NM));
        if (dist > cca)   // A21: left Carrier Control Area (divert): recovery ends, handoff to AWACS or advisory
        {
            Leave(); Gone = true;
            return new() { Ops.AwacsContact(Air, me, Freqs.GetValueOrDefault("AWACS", 251.5)) is { } aw ? Say("Marshal", $"{c}, Marshal, switch {aw}.", b)
                                                                                                    : Hint(L("Recovery beendet (über 50 NM vom Schiff).", "Recovery ended (more than 50 NM from the ship).")) };
        }
        inHold = dist < 10 * NM;
        if (Stage >= 3 && !waved && t.Ias < GrooveIas && Landing(b, t) is var (pd, pl) && pd is > -0.6 * NM and < -100 && Math.Abs(pl) < 0.2 * NM
            && (FoulDeck(b) || t.Gear is >= 0 and < 0.5))   // Deck occupied or gear known and up (unknown = no waveoff)
        {
            foul = FoulDeck(b); noGear = !foul;
            waved = true; lsoAt = now; woRep = 0;
            return new() { Say("Paddles", foul ? "Wave off, wave off, foul deck." : "Wave off, wave off.", b) };
        }
        if (Stage == 3 && ballAsked && !balled && !waved && t.Ias < GrooveIas && t.AltMsl < 600 * Ft && HdgDiff(t.Hdg * 180 / Math.PI, b.Fb) <= 15
            && Landing(b, t) is var (nd, nl) && nd is > -0.3 * NM and < -100 && Math.Abs(nl) < 0.2 * NM)   // #16: in the groove without ball call (after "call the ball") -> waveoff, grade "WO (no ball call)"
        {
            noBall = waved = true; lsoAt = now; woRep = 0;
            return new() { Say("Paddles", "Wave off, wave off.", b) };
        }
        if (Stage >= 3 && LsoCall(now, Landing(b, t).Deck) is { } lso) return new() { Say("Paddles", lso, b) };
        if (Stage == 1 && !follow && CaseNow() is var cn && cn != caseNo)   // R229: Case changes (weather, twilight): "99" once per ship, new assignment in holding (anyone already on approach stays)
        {
            var res = new List<Ops.Call>();
            foreach (var k in said.Keys.Where(k => !Boats.Any(x => x.Group == k)).ToList()) said.Remove(k);   // Review l4: ship gone (mission change): no old "99" state
            if (said.GetValueOrDefault(b.Group) != cn)
            {
                said[b.Group] = cn;
                res.Add(Say("Marshal", $"99, Marshal, case {Word(cn)} recovery, {(cn >= 2 ? $"CV-1 approach, expected final bearing {Mag(b.ExpFb)}" : $"expected BRC {Mag(b.ExpBrc)}")}{Alt(me, b)}.", b) with { All = true });
            }
            caseNo = cn; delta = false;
            res.AddRange(Assign(me, b, mt));
            return res;
        }
        if (caseNo == 1 && Stage == 1 && Stack(b.Group).FirstOrDefault(o => o.inHold) == this && mt >= charlieAt && (b.NoTurn || HdgDiff(b.Brc, b.ExpBrc) < 10 || mt >= charlieAt + 300) && AiPattern(b) < 2)   // A20: whoever is not in holding is skipped; AI in the pattern: wait; R135: only when the ship is into the wind, at the latest 5 min after the Charlie time
        {
            Stage = 2; OffStack(); deckDelta.Remove(b.Group);
            return new() { Say("Tower", $"{c}, signal Charlie.", b),
                           Hint(L($"Sinken zum Initial: 3 NM hinter dem Heck, 800 ft, Kurs {MagNum(b.ExpBrc):000}, 350 kt; Break über dem Bug nach links, Downwind 600 ft.",
                                    $"Descend to the initial: 3 NM astern, 800 ft, heading {MagNum(b.ExpBrc):000}, 350 kt; break left over the bow, downwind 600 ft.")) };
        }
        if (caseNo == 1 && Stage == 1 && inHold && !follow && mt > charlieAt + 60 && deckDelta.Add(b.Group))   // R276: in holding, deck delayed (ship turning, AI in the pattern): once per ship to all, order and times stay (CV NATOPS)
            return new() { Say("Marshal", "99, signal Delta.", b) with { All = true } };
        if (caseNo == 1 && Stage == 1 && !inHold && !delta && !follow && mt > charlieAt + 60)   // A20: not in holding and Charlie time exceeded by 1 min -> Delta once with new time (as at check-in, behind the last)
        {
            delta = true;
            var st = Stack(b.Group);
            charlieAt = Minute(Math.Max(mt + Math.Max(90, Math.Max(0, dist - 5 * NM) / Math.Max(t.Ias, 100) + 60), st.Where(o => o != this).Select(o => o.charlieAt + 90).DefaultIfEmpty(0).Max()));
            st.Remove(this); st.Add(this);   // to the end: the stack stays ordered by Charlie time
            return new() { Say("Marshal", $"{c}, signal Delta{(Clock >= 0 ? ", expected Charlie " + Mm() : "")}.", b) };
        }
        if (caseNo >= 2 && Stage == 1 && !follow && mt >= charlieAt)   // EAT reached: Marshal transmits nothing, the pilot reports "commencing" (#17/R224; R26: wingmen come via the lead)
        {
            // Approach start at the Marshal point (radial ExpFb+180, angels+15 DME): farther than 5 NM from it and outside the DME immediately new time (A73);
            // there without report after 1 min once "report commencing", after 2 min new time at the back and violation into the debriefing
            var (ea, el) = Rel(b, t, b.ExpFb);
            double fixM = (angels + 15) * NM, toFix = Math.Sqrt(Sq(ea + fixM) + Sq(el));
            bool away = toFix > 5 * NM && dist > fixM;
            if (!away && !delta && mt >= charlieAt + 60) { delta = true; return new() { Say("Marshal", $"{c}, Marshal, report commencing.", b) }; }
            if (away || mt >= charlieAt + 120)
            {
                delta = false;
                var st = Stack(b.Group);
                charlieAt = Minute(Math.Max(mt + toFix / Math.Max(t.Ias, 100) + 120, st.Where(o => o != this).Select(o => o.charlieAt + 60).DefaultIfEmpty(0).Max()));   // as at check-in: toward the fix + 2 min, one minute after the last
                st.Remove(this); st.Add(this);
                var mm = Mm();
                return new() { Say("Marshal", $"{c}, Marshal, new expected approach time{(mm != "" ? " " + mm : "")}.", b),
                               away ? Hint(L($"Noch nicht am Marshal-Punkt (Radial {MagNum(b.ExpFb + 180):000}, {angels + 15} DME, Angels {angels}): neue Approach-Zeit abwarten.",
                                             $"Not at the marshal point yet (radial {MagNum(b.ExpFb + 180):000}, {angels + 15} DME, angels {angels}): wait for the new approach time."))
                                    : new("Info", "", L("Verstoß: Approach-Zeit ohne „commencing“ verstrichen", "Deviation: approach time passed without \"commencing\""), 0) };
            }
        }
        if (double.IsNaN(fbSeen) || HdgDiff(b.ExpFb, fbSeen) > 0.5) (fbSeen, fbSeenAt) = (b.ExpFb, now);
        if (caseNo >= 2 && Stage is 1 or 2 && !follow && !double.IsNaN(fbSaid) && HdgDiff(b.ExpFb, fbSaid) >= 3 && now - fbSeenAt >= 10)   // R228: announce new final bearing once (before the next heading instruction), only when the ship is no longer turning (NoTurn: FB follows the heading)
        {
            fbSaid = b.ExpFb;
            return new() { Say(Stage == 1 ? "Marshal" : "Approach", $"{c}, final bearing {Mag(b.ExpFb)}.", b) };
        }
        if (caseNo == 1 && Stage == 2 && al is > -6 * NM and < -1.5 * NM && Math.Abs(lat) < NM && t.AltMsl < 1500 * Ft)   // Initial: astern, low
        {
            Stage = 3;   // Case I: zip lip, no "Paddles contact" by radio
            return new() { Hint(ZipLip) };
        }
        if (caseNo >= 2 && Stage == 2 && !radar && !follow && (t.AltMsl < 4500 * Ft || dist < 12 * NM))   // R282: under the platform or near the ship without a report to Approach: Approach identifies by itself (no heading instructions before)
            return new() { Say("Approach", Contact(me, b), b) };
        if (caseNo == 2 && Stage == 2 && !delta && dist < 10 * NM)   // #18/R227: Case II to the initial only on the pilot's "see you at ten"; at 10 DME ask once, otherwise CV-1 until "call the ball"
        {
            delta = true;
            return new() { Say("Approach", $"{c}, report see you at ten.", b) };
        }
        var (fa, fl) = Rel(b, t, b.Fb);
        double hdgFb = Tower.HdgDiff(t.Hdg * 180 / Math.PI, b.Fb);
        if (caseNo >= 2 && Stage == 2 && ladder == -1 && fa < -5 * NM && hdgFb > 120)   // after bolter/waveoff in the downwind 5 NM aft: back to final
        {
            ladder = 0;
            return new() { Say("Approach", $"{c}, Approach, turn {(fl < 0 ? "left" : "right")} heading {Mag(b.Fb)}, intercept final.", b) };
        }
        if (caseNo >= 2 && Stage is 2 or 3 && ladder >= 0 && fa < 0 && Math.Abs(fl) < 2 * NM && hdgFb < 60)   // CV-1 on final (heading to the ship, not in the downwind): ACLS at 6 DME, position at 3, 2 and 1½ DME (R20)
        {
            double dme = Dist(t.X, t.Z, b.X, b.Z) / NM;
            if (ladder <= 0 && dme is <= 6.5 and > 4)
            {
                ladder = 1; needlesAsked = true;
                return new() { Say("Approach", $"{c}, Approach, ACLS lock on, say needles.", b) };
            }
            int mk = dme > 3.2 ? 0 : dme > 2.2 ? 2 : dme > 1.7 ? 3 : dme > 1.2 ? 4 : 5;   // ladder 2-4: mile call, 5: silent below that (¾ mile: call the ball)
            if (ladder < mk)
            {
                ladder = mk; Stage = 3;
                var (hi, rt) = Dev(b, t);
                if (mk < 5) return new() { Say("Approach", $"{c}, {new[] { "three miles", "two miles", "one and a half miles" }[mk - 2]}, {DevText(hi, rt)}.", b) };
            }
        }
        double side = ((t.Hdg * 180 / Math.PI - b.Fb) % 360 + 540) % 360 - 180;   // Heading against FB, right positive
        if (caseNo >= 2 && Stage == 2 && radar && ladder >= 0 && fa < 0 && hdgFb < 90 && Math.Abs(fl) > 0.3 * NM && !(fl < 0 ? side > 3 : side < -3) && now - steerAt >= 30)
        {   // R20: CV-1 beside final and not already on the way back -> heading to intercept (10° per started 0.5 NM, at most 30°), at most every 30 s
            steerAt = now;
            double cut = Math.Min(30, 10 * Math.Ceiling(Math.Abs(fl) / (0.5 * NM)));
            return new() { Say("Approach", $"{c}, {(fl < 0 ? "left" : "right")} of course, fly heading {Mag(b.Fb + (fl < 0 ? cut : -cut))}.", b) };
        }
        if (Stage == 3 && !ballAsked && (caseNo == 1 ? fa is > -0.6 * NM and < -0.45 * NM : fa is > -0.85 * NM and < -0.6 * NM) && Math.Abs(fl) < 0.25 * NM && t.AltMsl < 600 * Ft
            && t.Ias < GrooveIas && t.Gear is < 0 or >= 0.5)   // low initial before the break: fast, gear up -> not yet groove
        {
            ballAsked = true;
            return new() { caseNo == 1 ? Say("Paddles", $"{c}, call the ball.", b) : Say("Approach", $"{c}, three quarter mile, call the ball.", b) };
        }
        if (Stage >= 3 && al > 0.5 * NM)   // passed in front: bolter, waveoff or went around himself -> new attempt in the pattern
        {
            var note = groove.Count > 0 ? Note(touchAt > 0 ? "bolter" : waved ? "wo" : "owo") : "";
            bool wofd = foul;   // R363: foul deck is not the pilot's failed attempt
            if (foul) { note = L("WOFD (Deck nicht frei), keine Wertung", "WOFD (foul deck), no grade"); foul = waved = false; }
            if (noGear) { note = (note != "" ? note : "WO") + L(", Fahrwerk oben", ", gear up"); noGear = waved = false; }   // reason into the grade; waves off even without groove
            if (noBall) { note = L("WO (kein Ball-Call)", "WO (no ball call)") + (note.StartsWith("WO") ? note[2..] : ""); noBall = waved = false; }   // #16
            ballAsked = balled = false; Stage = 3;
            var res = note != "" ? new List<Ops.Call> { new("Info", "", $"LSO: {note}", 0) } : new();
            if (note != "" && Signal(me, b, caseNo >= 2 ? "Approach" : "Tower", !wofd && ++passes >= 3, Lb(me)) is { } sg) return res.Concat(sg).ToList();   // R23: bingo or third failed attempt -> alternate instead of new attempt
            if (caseNo >= 2 && ladder >= 2)   // CV-1: straight ahead to 1200 ft, Departure leads to the downwind (R281), Approach back to final (stage 2, new calls)
            {
                Stage = 2; ladder = -1; needlesAsked = false; radar = true;   // -1: downwind, vector to final pending
                res.Add(Say("Departure", $"{c}, Departure, radar contact, turn left heading {Mag(b.Fb + 180)}, maintain 1200, downwind.", b));
                res.Add(Hint(L($"Geradeaus auf 1200 ft, links auf Kurs {MagNum(b.Fb + 180):000} (Downwind); Departure führt dich in den Downwind, Approach zurück auf den Endanflug.",
                               $"Straight ahead to 1200 ft, left to heading {MagNum(b.Fb + 180):000} (downwind); Departure takes you downwind, Approach brings you back to the final.")));
            }
            return res;
        }
        if (Stage >= 3 && touchAt > 0 && !bolterTold && (Landing(b, t).Deck > Wires[^1] + 60 && rel > 40 || t.AltMsl > DeckH + 10 && Rel(b, t, b.Fb).Along > 0))   // touched down: 60 m behind wire 4 still fast (slides through; an arrested aircraft is long since slower after ~100 m rollout) or airborne again
        {
            bolterTold = true;
            return new() { Say("Paddles", "Bolter, bolter, bolter.", b) };
        }
        return new();
    }

    // ------------------------------------------------------------ LSO (M5 step 3)
    // ponytail: Nimitz-class dimensions for all carriers (Tarawa/Kuznetsov differ); error against 3.5° from the hook
    const double DeckH = 19.5, SternAlong = -164, Gs = 3.5, Hook = 2.5, Aim = 75;   // Aim: m behind the stern, just before wire 3
    const double GrooveIas = 100;   // m/s (195 kt): faster is initial/flyby, not a landing approach
    static readonly double[] Wires = { 55, 67, 79, 96 };   // m behind the stern, along the landing runway (FB)
    public static readonly string[] LsoLines = { "Roger ball.", "A little power.", "Power.", "Power, power.", "Don't settle.", "Easy with it.", "You're high.", "You're slow.", "You're fast.", "Right for lineup.", "Come left.", "Wave off, wave off.", "Bolter, bolter, bolter.", "Wave off, wave off, foul deck." };
    readonly List<(double R, double Gs, double Lu, double Ao, double T)> groove = new();   // R: NM before the stern; error in degrees (high/right/slow positive, Ao NaN = unknown)
    /// On-speed AoA in degrees (Export.lua delivers degrees from rad); ponytail: Hornet only, F-14 computes in units
    /// Table: type prefix -> degrees (config.jsonc OnSpeedAoa), the longest prefix wins, unknown type NaN = no speed calls
    /// ponytail: F-14/T-45 not in the default (F-14 computes in units, value unchecked) -> add to config.jsonc once measured (log "[LSO] AoA im Mittel")
    public static Dictionary<string, double> OnSpeedAoa = new() { ["FA-18"] = 8.1 };
    static double OnSpeed(string type) => OnSpeedAoa.Where(kv => kv.Key != "" && type.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)).OrderByDescending(kv => kv.Key.Length)
        .Select(kv => kv.Value).DefaultIfEmpty(double.NaN).First();
    double lsoAt = -99, touchAt = -1, wire, prevA = -1e9, prevH, slope, prevHdg, hdgT = 1e9;   // prevHdg/hdgT: heading of the last sample (turn rate, R3)
    public double AoaDev = double.NaN, AoaMean = double.NaN;   // last approach: AoA mean above (+) or below on-speed and raw, degrees (log for calibration)
    double aoaSum; int aoaN;   // raw AoA in the groove, also without known on-speed (otherwise a new type would have nothing to calibrate)
    /// Log line for calibration: raw mean (for OnSpeedAoa) and, if on-speed is known, the deviation; null without AoA (not the host).
    public string? AoaLog(string cs) => double.IsNaN(AoaMean) ? null : FormattableString.Invariant($"[LSO] {cs}: AoA im Mittel {AoaMean:0.0}°") + (double.IsNaN(AoaDev) ? " (on-speed unbekannt)" : FormattableString.Invariant($" ({AoaDev:+0.0;-0.0}° zu on-speed)"));
    int woRep;   // R103: repetitions of the waveoff
    bool waved, bolterTold, foul, noGear, noBall;   // foul: waveoff due to occupied deck (no grade); noGear: waveoff due to gear up (reason into the grade); noBall: without ball call (#16)

    /// Position relative to the landing runway: m behind the stern (deck positive), m right of the centerline.
    static (double Deck, double Lat) Landing(Boat b, Telemetry t)
    {
        double r = b.Brc * Math.PI / 180;
        return Rel(b with { X = b.X + SternAlong * Math.Cos(r) - b.SternLat * Math.Sin(r), Z = b.Z + SternAlong * Math.Sin(r) + b.SternLat * Math.Cos(r) }, t, b.Fb);
    }

    /// With every state (mission writes every 0.5 s, R27: player under 2 NM from the carrier every 0.25 s): groove samples from 0.75 NM, touchdown point -> wire.
    public void Sample(Telemetry? t, double now, double aoa = double.NaN, string type = "")
    {
        if (Stage < 3 || t == null || Mine is not { } b) return;
        var (deck, lat) = Landing(b, t);
        double hook = t.AltMsl - Hook - DeckH, hdg = t.Hdg * 180 / Math.PI;
        bool straight = HdgDiff(hdg, b.Fb) <= 15 && (now <= hdgT || HdgDiff(hdg, prevHdg) / (now - hdgT) < 3);   // R3: in the groove, not in the 180° turn (heading ±15° to FB, under 3°/s)
        (prevHdg, hdgT) = (hdg, now);
        if (!waved && straight && deck is > -0.75 * NM and < 0 && Math.Abs(lat) < 0.3 * NM && hook < 1000 * Ft && t.Ias < GrooveIas)   // after the waveoff nothing counts anymore; initial (fast) is not groove
        {
            double d = -deck + Aim;
            groove.Add((-deck / NM, Math.Atan2(hook, d) * 180 / Math.PI - Gs, Math.Atan2(lat, d) * 180 / Math.PI, aoa - OnSpeed(type), now));
            if (!double.IsNaN(aoa)) { aoaSum += aoa; aoaN++; }
        }
        // Hook at the deck (≤ 1.5 m, height on the deck fluctuates): touchdown point from the last sample in the air and its descent angle
        // ponytail: calibration knob Hook/DeckH, in case wires in the game are off
        if (touchAt < 0 && groove.Count > 0 && hook <= 1.5 && deck is > -20 and < 150)
        {
            double at = slope < 0 ? prevA - prevH / slope : deck;
            touchAt = now;
            wire = at is > 40 and < 110 ? Array.IndexOf(Wires, Wires.MinBy(w => Math.Abs(w - at))) + 1 : 0;
        }
        if (hook > 1.5)
        {
            if (deck > prevA) slope = (hook - prevH) / (deck - prevA);
            (prevA, prevH) = (deck, hook);
        }
    }

    /// LSO call from the last sample, at most every 4 s; waveoff between IC and AR.
    /// deck: m behind the stern from telemetry (R103: after the waveoff there are no more samples).
    string? LsoCall(double now, double deck = double.NaN)
    {
        if (waved)   // R103: waveoff is mandatory, if ignored -> repeat at most twice up to the stern (CV NATOPS)
        {
            if (touchAt > 0 || woRep >= 2 || now - lsoAt < 2 || deck is not (> -0.25 * NM and < 0)) return null;
            woRep++; lsoAt = now;
            return foul ? "Wave off, wave off, foul deck." : "Wave off, wave off.";
        }
        if (Stage < 4 && !balled || groove.Count == 0 || touchAt > 0) return null;   // #16: no LSO calls before the ball call
        var g = groove[^1];
        if (now - g.T > 1.5 || g.R > 0.75) return null;
        if (g.R is >= 0.04 and <= 0.25 && (g.Gs > 1.8 || g.Gs < -1.2 || Math.Abs(g.Lu) > 3)) { waved = true; lsoAt = now; woRep = 0; return "Wave off, wave off."; }
        if (now - lsoAt < 3) return null;
        // largest error first (per threshold), otherwise "Power" never gets a lineup call
        // Power by stage (deviation) and trend over at least 1 s (°/s; samples ~2 Hz with app timestamp, two samples alone overestimate the rate):
        // continued sinking -> "Don't settle." only instead of the lowest stage (lower and sinking stays "Power"), fast back -> "Easy with it."
        var p = groove.LastOrDefault(s => g.T - s.T >= 1);
        double rate = p.T > 0 ? (g.Gs - p.Gs) / (g.T - p.T) : 0;
        var power = rate > 0.5 ? "Easy with it." : -g.Gs < 0.9 ? (rate < -0.5 ? "Don't settle." : "A little power.") : -g.Gs < 1.3 ? "Power." : "Power, power.";
        var (v, call) = new[] { (-g.Gs / 0.6, power), (-g.Lu, "Right for lineup."), (g.Lu, "Come left."), (g.Ao / 1.2, "You're slow."), (-g.Ao / 1.2, "You're fast."), (g.Gs / 0.8, "You're high.") }
                        .Where(e => !double.IsNaN(e.Item1) && (Stage >= 4 || e.Item2.StartsWith("Power"))).DefaultIfEmpty().MaxBy(e => e.Item1);   // R5: before "Roger ball" only Power (and waveoff)
        if (v <= 1) return null;
        lsoAt = now;
        return call;
    }

    /// Grade like LSO: _OK_ 5 (only with very small errors and groove 15-18 s), OK 4, (OK) 3, -- 2, B 2.5, WO 1, OWO 2, C 0 (waveoff ignored, R103); comments per section X/IM/IC/AR; then new attempt.
    string Note(string how)
    {
        var parts = new List<string>();
        int worst = 0;
        double dev = 0;   // largest error relative to the OK threshold: _OK_ only with very small errors
        foreach (var (seg, lo, hi) in new[] { ("X", 0.5, 1.0), ("IM", 0.25, 0.5), ("IC", 0.04, 0.25), ("AR", 0.0, 0.04) })
        {
            var s = groove.Where(g => g.R >= lo && g.R < hi).ToList();
            var ao = s.Select(g => g.Ao).Where(a => !double.IsNaN(a)).DefaultIfEmpty(0).ToList();
            if (s.Count == 0) continue;
            bool m = lo < 0.25;   // R4: IC/AR lineup in meters (0.5° is only 1-4 m there), otherwise degrees
            Func<(double R, double Gs, double Lu, double Ao, double T), double> lu = g => m ? Math.Tan(g.Lu * Math.PI / 180) * (g.R * NM + Aim) : g.Lu;
            var (lu1, lu2, lu3) = m ? (1.5, 3.0, 6.0) : (0.5, 1.0, 3.0);
            foreach (var (v, small, mid, big, sym) in new[] { (s.Max(g => g.Gs), 0.4, 0.8, 1.5, "H"), (-s.Min(g => g.Gs), 0.3, 0.6, 0.9, "LO"),
                                                              (-s.Min(lu), lu1, lu2, lu3, "LUL"), (s.Max(lu), lu1, lu2, lu3, "LUR"),
                                                              (ao.Max(), 0.7, 1.2, 2.0, "SLO"), (-ao.Min(), 0.7, 1.2, 2.0, "F") })
            {
                dev = Math.Max(dev, v / small);
                int lvl = v > big ? 3 : v > mid ? 2 : v > small ? 1 : 0;
                if (lvl == 0) continue;
                worst = Math.Max(worst, lvl);
                parts.Add((lvl == 1 ? $"({sym})" : lvl == 3 ? $"_{sym}_" : sym) + seg);
            }
        }
        // Groove time from 0.5 NM (about wings level in Case I); from ball call (0.6-0.85 NM) or 0.75 NM there would almost always be LIG at real approach speed
        double start = groove.FirstOrDefault(g => g.R <= 0.5, groove.FirstOrDefault()).T;   // Cut also without samples
        double sec = (touchAt > 0 ? touchAt : groove.LastOrDefault().T) - start;
        if (caseNo == 1 && how is "trap" or "bolter") { if (sec < 15) { parts.Add("NESA"); dev = 1; } else if (sec > 18) { parts.Add("LIG"); dev = 1; } }   // Groove too short / too long (LSO NATOPS); on waveoff the groove is short anyway; Case II/III without NESA/LIG
        var (grade, pts) = how switch
        {
            "bolter" => ("B", 2.5), "wo" => ("WO", 1.0), "owo" => ("OWO", 2.0), "cut" => ("C", 0.0),
            _ => worst switch { 0 => dev <= 0.5 ? ("_OK_", 5.0) : ("OK", 4.0), 1 => ("OK", 4.0), 2 => ("(OK)", 3.0), _ => ("--", 2.0) },
        };
        if (how == "trap" && wire == 1 && pts > 3) (grade, pts) = ("(OK)", 3.0);   // 1-wire: at most (OK)
        var text = (how == "cut" ? $"{grade}, {L("Waveoff missachtet", "waveoff ignored")}{(wire > 0 ? $", {wire}-wire" : "")}{(foul ? ", foul deck" : "")}"
                                 : $"{grade}{(how == "trap" && wire > 0 ? $" {wire}-wire" : "")}, groove {sec:0} s{(parts.Count > 0 ? ", " + string.Join(" ", parts) : "")}") + $" ({pts:0.#} " + L($"Punkt{(pts == 1 ? "" : "e")}", $"point{(pts == 1 ? "" : "s")}") + ")";
        AoaDev = groove.Select(g => g.Ao).Where(a => !double.IsNaN(a)).DefaultIfEmpty(double.NaN).Average();
        AoaMean = aoaN > 0 ? aoaSum / aoaN : double.NaN; aoaSum = aoaN = 0;
        groove.Clear(); touchAt = -1; waved = bolterTold = false; wire = slope = 0; prevA = -1e9;
        return text;
    }


    // ------------------------------------------------------------ AI at the carrier (M6)
    /// AI traffic (Program); flag "cv" = landing waypoint on a carrier (mission).
    public static IReadOnlyList<Traffic> Air = Array.Empty<Traffic>();
    /// Player (Program.ReadState): only for foul deck, not in Air (AiPattern/Divert count AI there).
    public static IReadOnlyList<Traffic> Decks = Array.Empty<Traffic>();
    static Telemetry AsTel(Traffic a) => new(a.AltMsl, 0, a.Speed, a.Hdg, a.X, a.Z, 0, 0, 0);
    /// AI in the landing pattern (5 NM, under 2000 ft): from 2 no Charlie for players.
    static int AiPattern(Boat b) => Air.Count(a => a.Flag == "cv" && a.InAir && a.Coalition == b.Coalition && Dist(a.X, a.Z, b.X, b.Z) < 5 * NM && a.AltMsl < 2000 * Ft);
    /// Landing area occupied (AI or player on the angled deck up to 250 m before the stern).
    static bool FoulDeck(Boat b) => Air.Concat(Decks).Any(a => a.Coalition == b.Coalition && a.AltMsl < DeckH + 8 &&
                                                 Landing(b, AsTel(a)) is var (d, l) && d is > -10 and < 250 && Math.Abs(l) < 15);
    static readonly Dictionary<int, (bool Astern, int Passes, bool Foul)> aiPass = new();
    /// Guard against endless waveoffs: AI that goes around three times from the groove over the bow -> divert (Program: DIVERT); go-arounds at foul deck do not count (R363).
    public static List<string> Divert()
    {
        var res = new List<string>();
        foreach (var a in Air.Where(a => a.Flag == "cv" && a.InAir && a.AltMsl < 600 * Ft))
        {
            var b = Boats.Where(x => x.Coalition == a.Coalition).MinBy(x => Dist(a.X, a.Z, x.X, x.Z));
            if (b == null || Dist(a.X, a.Z, b.X, b.Z) > 2 * NM) continue;
            var (d, l) = Landing(b, AsTel(a));
            var s = aiPass.GetValueOrDefault(a.Id);
            if (d is > -0.5 * NM and < 0 && Math.Abs(l) < 100) { s.Astern = true; s.Foul |= FoulDeck(b); }
            else if (s.Astern && d > 400) s = (false, s.Passes + (s.Foul ? 0 : 1), false);
            if (s.Passes >= 3) { aiPass.Remove(a.Id); res.Add(a.Group); }
            else aiPass[a.Id] = s;
        }
        return res;
    }

    static readonly Dictionary<int, int> aiBall = new();   // AI -> 1 ball reported, 2 bolter reported (once per approach)
    /// Aircraft type -> (name on the radio, tank capacity in lb) for the ball call; ponytail: carrier types only, otherwise without fuel state
    internal static (string Name, double Lb) CvType(string type) =>
        type.StartsWith("FA-18") ? ("Hornet", 10800) : type.StartsWith("F-14") ? ("Tomcat", 16200) : type.StartsWith("S-3") ? ("Viking", 15000) :
        type.StartsWith("E-2") ? ("Hawkeye", 12400) : type.StartsWith("AV8") ? ("Harrier", 7500) : (type.Split('_')[0], 0);
    /// Radio of the mission AI in the groove (nothing spawned, N30): once per approach "<callsign>, Hornet ball, 5.4." with "Roger ball." from Paddles,
    /// on bolter "Bolter, bolter, bolter."; callsign and fuel per unit (Traffic.Cs/Fuel), player: a player is in the groove on this ship (then the AI is silent).
    public static List<(Boat B, string Group, string Cs, string Pilot, string Lso)> AiCalls(Func<Boat, bool> player)
    {
        var res = new List<(Boat, string, string, string, string)>();
        var keep = new HashSet<int>();
        foreach (var a in Air.Where(a => a.Flag == "cv" && a.InAir))
        {
            var b = Boats.Where(x => x.Coalition == a.Coalition).MinBy(x => Dist(a.X, a.Z, x.X, x.Z));
            if (b == null || Dist(a.X, a.Z, b.X, b.Z) > 2 * NM) continue;
            keep.Add(a.Id);
            var (d, l) = Landing(b, AsTel(a));
            var st = aiBall.GetValueOrDefault(a.Id);
            if (st == 0 && d is > -0.8 * NM and < -0.5 * NM && Math.Abs(l) < 0.25 * NM && a.AltMsl < 600 * Ft && a.Speed < GrooveIas)
            {
                aiBall[a.Id] = 1;
                if (player(b)) continue;
                var (kind, lb) = CvType(a.Type);
                var m = System.Text.RegularExpressions.Regex.Match(a.Cs, @"^([A-Za-z]+)\s*(\d)\s*-?\s*(\d)");
                int tenth = (int)Math.Round(a.Fuel * lb / 100);
                res.Add((b, a.Group, a.Cs, $"{(m.Success ? SpokenCallsign($"{m.Groups[1].Value} {m.Groups[2].Value}-{m.Groups[3].Value}") + ", " : "")}{kind} ball" +
                                     $"{(lb > 0 ? $", {Digits((tenth / 10).ToString())} point {Digits((tenth % 10).ToString())}" : "")}.", CaseNow() == 3 ? $"Roger ball, {Wod(b)}." : "Roger ball."));   // R230
            }
            else if (st == 1 && d > 260 && Math.Abs(l) < 100 && a.AltMsl < DeckH + 40)   // beyond the deck still airborne, low: bolter
            {
                aiBall[a.Id] = 2;
                if (!player(b)) res.Add((b, a.Group, a.Cs, "", "Bolter, bolter, bolter."));
            }
            if (d < -NM || d > 400) aiBall.Remove(a.Id);   // far aft or beyond the deck ahead (break, bolter, waveoff): new approach, as divert
        }
        foreach (var k in aiBall.Keys.Where(k => !keep.Contains(k)).ToList()) aiBall.Remove(k);   // landed or gone
        return res;
    }
    // ======================================================================= Self-test
    public static void SelfTest(Action<bool, string> check)
    {
        var b = new Boat("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 127.5, "73X", "11");   // sails north
        Boats = new() { b };
        var altOld = Alternate;
        Alternate = (_, _) => null;   // R23: no alternate (fields of other tests), only in the R23 test
        Clock = 10 * 3600;   // 10:00
        Telemetry At(double north, double east, double ft) => new(ft * Ft, ft * Ft, 150, 0, north, east, 0, 0, 101325 / 100.0 * Math.Pow(1 - 2.25577e-5 * ft * Ft, 5.25588));
        var (c1, c2) = (new Carrier(), new Carrier());
        Ops.Me M(string cs, Telemetry t) => new(cs, t, "FA-18C_hornet", 2);
        string S(Carrier c, string cs, string text, Telemetry t, double now) => string.Join(" | ", c.OnTranscript(text, M(cs, t), now).Select(x => $"{x.Station}: {x.Text}"));
        var far = At(-35 * NM, 0, 18000);
        var m1 = S(c1, "Enfield 1-1", "Marshal, Enfield 1-1, mother's 360 for 35, angels 18, state 6.2", far, 0);
        var m2 = S(c2, "Enfield 1-2", "Marshal, Enfield 1-2, checking in", far, 10);
        check(m1 == "Marshal: Enfield one one, Marshal, case one recovery, expected BRC three five four, altimeter two niner niner two, hold angels 2, expected Charlie zero eight, report see me. | : Träger: Holding über dem Schiff in Angels 2, Linkskreise (ca. 5 NM Ø). Warten auf „signal Charlie“."
              && m2.Contains("hold angels 3, expected Charlie one zero") && c1.Wants(Normalize("Enfield 1-1, see you at 2")), $"Träger: Marshal -> {m1} | {m2}");
        // Into the wind: west wind 10 m/s (19.4 kt) -> heading 270 + 9.1 (angled deck), 7.6 kt speed; Marshal gives the BRC into the wind
        var bw = b with { Wz = 10, Pa = 100000 };
        Boats = new() { bw };
        var cw = new Carrier();
        var mw = S(cw, "Enfield 1-3", "Marshal, Enfield 1-3, checking in", far, 20);
        cw.Leave();
        Boats = new() { b };
        check(Math.Abs(bw.IntoWind.Brc - 279.1) < 0.1 && Math.Abs(bw.IntoWind.Kt - 7.56) < 0.05 && b.IntoWind == (b.Brc, 27) &&
              mw.Contains($"expected BRC {Mag(279.1)}, altimeter two niner five three"), $"Träger in den Wind: {bw.IntoWind} -> {mw}");
        // Enter suggestion per stage (A76)
        var sg = new Carrier();
        string Sg(Telemetry? t) => sg.Suggest(t)?.Text ?? "-";
        var sgr = new List<string> { Sg(far) + "/" + Sg(far with { Agl = 0 }) };   // check in airborne, nothing on the deck
        sg.seenAt = sg.charlieAt;   // EAT reached
        foreach (var (stage, cn) in new[] { (1, 1), (2, 1), (3, 1), (4, 1), (2, 3), (1, 3) }) { (sg.Stage, sg.caseNo) = (stage, cn); sgr.Add(Sg(far)); }
        sg.Stage = 3; sg.ballAsked = true; sgr.Add(Sg(far));
        (sg.Stage, sg.caseNo, sg.ladder) = (2, 3, 1); sgr.Add(Sg(far));
        check(string.Join("|", sgr) == "Marshal, checking in/-|see you at angels|-|-|-|platform|commencing|ball|-", "Träger Enter-Vorschlag: " + string.Join("|", sgr));
        // Review l4/R277: "commencing" in the radio wheel exactly when Marshal accepts it (from EAT - 15 s), no suggestion that surely gets "negative"
        var r277 = "";
        foreach (var s in new[] { 939.0, 984, 985, 1000 })
        {
            var q = new Carrier { Stage = 1, caseNo = 3, boat = "CVN", cs = "Enfield one one", charlieAt = 1000, seenAt = s };
            r277 += $"{q.Suggest(far)?.Text ?? "-"}:{(S(q, "Enfield 1-1", "Enfield 1-1, commencing", far, s).Contains("negative") ? "neg" : "ok")}/";
        }
        check(r277 == "-:neg/-:neg/commencing:ok/commencing:ok/", "Träger Review l4/R277: commencing-Vorschlag = Annahme durch Marshal -> " + r277);
        var sl = new Carrier { Stage = 1, boat = "CVN", cs = "Enfield one one" };   // diverted and landed ashore: stage gone, otherwise the carrier suggestion would stay in the radio wheel
        sl.Tick(M("Enfield 1-1", far with { Agl = 0, Ias = 0 }), 30);
        check(sl.Stage == 0, "Träger: an Land gelandet -> Stufe " + sl.Stage);
        var cu = new Carrier { Stage = 1 };   // R16: unclear "commencing" -> say again, stage stays
        var un = cu.OnTranscript("Enfield 1-4, commencing", M("Enfield 1-4", far), 30, true);
        var un2 = new Carrier { Stage = 2, caseNo = 3 }.OnTranscript("Enfield 1-4, banana", M("Enfield 1-4", far), 30, true).Concat(new Carrier { Stage = 2 }.OnTranscript("Enfield 1-4, banana", M("Enfield 1-4", far), 30)).ToList();   // R231: the leading station asks back
        check(un is [{ Station: "Marshal", Text: "Enfield one four, Marshal, say again." }] && cu.Stage == 1 && un2 is [{ Station: "Approach", Text: "Enfield one four, Approach, say again." }, { Station: "Tower", Text: "Enfield one four, Tower, say again." }],
              "Träger R16/R231 unsicher: " + string.Join(" / ", un.Concat(un2).Select(x => x.Station + ": " + x.Text)));
        var hold = At(0, -2 * NM, 2000);
        string Tk(Carrier c, string cs, Telemetry t, double now) => string.Join(" | ", c.Tick(M(cs, t), now).Select(x => $"{x.Station}: {x.Text}"));
        var ch1 = Tk(c1, "Enfield 1-1", hold, 400) + "/" + Tk(c1, "Enfield 1-1", hold, 481);
        var sya = S(c1, "Enfield 1-1", "Enfield 1-1, see you at angels", hold, 482);
        var ch2 = Tk(c2, "Enfield 1-2", hold, 481) + "/" + Tk(c2, "Enfield 1-2", far, 601) + "/" + Tk(c2, "Enfield 1-2", hold, 602);   // 35 NM away: no Charlie yet
        check(ch1.StartsWith("/Tower: Enfield one one, signal Charlie. | : Träger: Sinken zum Initial: 3 NM hinter dem Heck, 800 ft, Kurs 354, 350 kt") && sya == "Tower: Enfield one one, roger." && ch2.StartsWith("//Tower: Enfield one two, signal Charlie."), $"Träger: Charlie nach Stack, Zeit und Holding -> {ch1} | {sya} | {ch2}");
        var ini = Tk(c1, "Enfield 1-1", At(-3 * NM, 0, 800), 600);
        var g34 = At(-0.75 * NM * Math.Cos(9.1 * Math.PI / 180), 0.75 * NM * Math.Sin(9.1 * Math.PI / 180), 350);
        var g50 = At(-0.5 * NM * Math.Cos(9.1 * Math.PI / 180), 0.5 * NM * Math.Sin(9.1 * Math.PI / 180), 350);
        var early = Tk(c1, "Enfield 1-1", g34 with { Ias = 180 }, 640) + "/" + Tk(c1, "Enfield 1-1", g34 with { Ias = 70, Gear = 0 }, 650);   // low initial (350 kt), gear up
        check(early == "/", "Träger: kein \"call the ball\" vor dem Break -> " + early);
        var grv = Tk(c1, "Enfield 1-1", g34 with { Ias = 70, Gear = 1 }, 660) + "/" + Tk(c1, "Enfield 1-1", g50 with { Ias = 70, Gear = 1 }, 661);   // Case I: only at 0.5 NM, not at 0.75
        var ball = S(c1, "Enfield 1-1", "Enfield 1-1, Hornet ball, 5.1", At(-0.45 * NM, 0, 330), 662);
        var trap = Tk(c1, "Enfield 1-1", new Telemetry(DeckH + 2, 0, 10, 0, -50, 0, 0, 0, 0), 680);   // stands on the deck, sails along
        check(ini.StartsWith(": Träger: Initial: zip lip") && grv == "/Paddles: Enfield one one, call the ball." && ball == "Paddles: Roger ball."
              && trap.Contains("gelandet") && c1.Stage == 0, $"Träger: Initial, Ball, Landung -> {ini} | {grv} | {ball} | {trap}");
        // R2: after the trap "call the ball" comes again in the next recovery (ballAsked/foul reset); R4: Lat at the trap in the log
        var cf = new Carrier { foul = true, waved = true };
        cf.Leave();
        check(!c1.ballAsked && !cf.foul && !cf.waved && (c1.LatLog ?? "").StartsWith("[LSO] Enfield 1-1: Lat beim Trap "), $"Träger R2: Leave setzt zurück -> {c1.ballAsked}/{cf.foul}/{cf.waved} | {c1.LatLog}");
        // R1: "commencing" from stage 2 and "Charlie" are acknowledgments, no jump to Paddles; "initial" only in Case I
        var r1 = new List<string>();
        foreach (var (txt, cn) in new[] { ("Enfield 1-1, commencing, angels 6, state 5.8", 3), ("Enfield 1-1, Charlie", 1), ("Enfield 1-1, initial", 3) })
        {
            var cr = new Carrier { Stage = 2, caseNo = cn, boat = "CVN", radar = true };   // already identified (otherwise R20: radar contact)
            r1.Add(S(cr, "Enfield 1-1", txt, far, 700) + " " + cr.Stage);
        }
        check(string.Join("/", r1) == "Approach: Enfield one one, roger. 2/Tower: Enfield one one, roger. 2/Approach: Enfield one one, roger. 2", "Träger R1: commencing/Charlie -> " + string.Join("/", r1));
        // A20: first in the stack 100 NM away -> the second in holding gets Charlie at his time; the first gets "signal Delta" once with new time and goes to the end
        var (da, db) = (new Carrier(), new Carrier());
        var away = At(-100 * NM, 0, 18000);
        S(da, "Enfield 2-1", "Marshal, Enfield 2-1, checking in", away, 1000);
        S(db, "Enfield 2-2", "Marshal, Enfield 2-2, checking in", hold, 1000);
        var dl = Tk(da, "Enfield 2-1", away, 2400) + "/" + Tk(db, "Enfield 2-2", hold, 2400) + "/" + Tk(da, "Enfield 2-1", away, 2401);
        check(dl.StartsWith("Marshal: Enfield two one, signal Delta, expected Charlie zero one./Tower: Enfield two two, signal Charlie.") && dl.EndsWith("/") && da.Stage == 1 && db.Stage == 2,
              $"Träger A20: Charlie an den Ersten im Holding, Delta -> {dl}");
        da.Leave(); db.Leave();
        // A21: stage 1, 60 NM away -> recovery ends (Gone for OnBoat), advisory or with AWACS "switch Overlord"; 49 NM stays; check-in from 100 NM (above) stays
        string Cca(IReadOnlyList<Traffic> air)
        {
            Air = air;
            var cc = new Carrier();
            S(cc, "Enfield 1-1", "Marshal, Enfield 1-1, checking in", far, 0);
            var r = Tk(cc, "Enfield 1-1", At(-30 * NM, 0, 18000), 10) + "/" + Tk(cc, "Enfield 1-1", At(-49 * NM, 0, 18000), 20) + "/" + Tk(cc, "Enfield 1-1", At(-60 * NM, 0, 18000), 30);
            Air = Array.Empty<Traffic>();
            return r + (cc.Stage == 0 && cc.Gone && !Stack("CVN").Contains(cc) ? " (weg)" : " (Stage " + cc.Stage + ")");
        }
        var (ccaNo, ccaAw) = (Cca(Array.Empty<Traffic>()), Cca(new[] { new Traffic(80, "E-3A", -60 * NM, 0, 9000, 0, 150, "AWACS", "", true, 2) }));
        check(ccaNo == "//: Träger: Recovery beendet (über 50 NM vom Schiff). (weg)" && ccaAw.StartsWith("//Marshal: Enfield one one, Marshal, switch Overlord ") && ccaAw.EndsWith(" (weg)"),
              $"Träger A21: 50 NM -> {ccaNo} | {ccaAw}");
        var czl = new Carrier { Stage = 2 };   // P3-T5: "initial" by radio/wheel in Case I -> text only, no "Paddles contact"
        var zl = S(czl, "Enfield 1-1", "Carrier, Enfield 1-1, initial", At(-3 * NM, 0, 800), 600);
        check(zl.StartsWith(": Träger: Initial: zip lip") && !zl.Contains("Paddles") && czl.Stage == 3, "Träger Case I initial per Funk: " + zl);
        // R278: Case I in holding "initial" without Charlie -> rejection with Charlie time; stage, stack and Charlie stay
        var c278 = new Carrier();
        S(c278, "Enfield 1-1", "Marshal, Enfield 1-1, checking in", hold, 5000);
        var (ca278, ix278) = (c278.charlieAt, Stack("CVN").IndexOf(c278));
        var i278 = S(c278, "Enfield 1-1", "Enfield 1-1, initial", hold, 5010);
        check(i278 == $"Marshal: Enfield one one, negative, not Charlie, hold angels {c278.angels}, expected Charlie {c278.Mm()}." && c278.Stage == 1 && c278.charlieAt == ca278 && ix278 >= 0 && Stack("CVN").IndexOf(c278) == ix278,
              $"Träger R278: initial ohne Charlie -> {i278} (Stufe {c278.Stage})");
        c278.Leave();
        // Initial: 800 ft, 150 m/s over the ship -> no LSO calls, no waveoff
        var cin = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" };
        var inCalls = new List<string>();
        for (double d = -3 * NM, ti = 700; d < NM; d += 37.5, ti += 0.25)
        {
            cin.Sample(At(d, 30, 800), ti, 3, "FA-18C_hornet");
            if (ti % 1 == 0) inCalls.Add(Tk(cin, "Enfield 1-1", At(d, 30, 800), ti));
        }
        inCalls.RemoveAll(x => x == "");
        check(inCalls.Count == 0 && !cin.waved, "Träger: Initial ohne LSO-Calls -> " + string.Join(" / ", inCalls));
        // LSO: groove at 4 Hz on the centerline, in IM 1° too high, in IC too slow -> calls, trap -> (OK) 3-wire, HIM SLOIC
        var c3 = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" };
        double fb = b.Fb * Math.PI / 180;
        Telemetry G(double deck, double err, double spd) =>
            new(DeckH + Hook + (deck < Aim ? (Aim - deck) * Math.Tan((Gs + err) * Math.PI / 180) : 0), 0, spd, fb,
                SternAlong + deck * Math.Cos(fb), b.SternLat + deck * Math.Sin(fb), 0, 0, 0);
        var calls = new List<string>();
        double now3 = 100;
        bool balled = false;
        for (double d = -NM; d < Aim + 20; d += 17.5, now3 += 0.25)
        {
            var tg = G(d, d is > -0.5 * NM and < -0.25 * NM ? 1.0 : 0, 70);
            if (!balled && d >= -0.7 * NM) { balled = true; calls.Add(S(c3, "Enfield 1-1", "Enfield 1-1, Hornet ball, 5.1", tg, now3)); }
            c3.Sample(tg, now3, 8.1 + (d is > -0.25 * NM and < -0.04 * NM ? 1.5 : 0), "FA-18C_hornet");   // in IC 1.5° too slow
            if (now3 % 1 == 0) calls.Add(Tk(c3, "Enfield 1-1", tg, now3));
        }
        calls.Add(Tk(c3, "Enfield 1-1", G(Aim, 0, 10), now3 + 1));
        calls.RemoveAll(x => x == "");
        check(calls.Count(x => x == "Paddles: You're high.") >= 1 && calls.Contains("Paddles: You're slow.") && calls[^1] == ": LSO: (OK) 3-wire, groove 14 s, HIM SLOIC NESA (3 Punkte)" && c3.Stage == 0,
              "Träger LSO: Calls + Note -> " + string.Join(" / ", calls));
        // Calibration log: raw AoA mean also for a type without on-speed (otherwise NaN, nothing for OnSpeedAoa), no line without AoA
        var ct = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" };
        double tc = 100;
        for (double d = -NM; d < Aim + 20; d += 17.5, tc += 0.25) ct.Sample(G(d, 0, 70), tc, 9.0, "T-45");
        Tk(ct, "Enfield 1-1", G(Aim, 0, 10), tc + 1);
        check(ct.AoaLog("Enfield 1-1") == "[LSO] Enfield 1-1: AoA im Mittel 9.0° (on-speed unbekannt)" && (c3.AoaLog("Enfield 1-1") ?? "").Contains("° zu on-speed)") && new Carrier().AoaLog("x") == null,
              $"LSO: AoA-Log zum Kalibrieren -> {ct.AoaLog("Enfield 1-1")} | {c3.AoaLog("Enfield 1-1")}");
        // P3-T3: _OK_ only with very small errors (here 0.1° too high) and groove 15-18 s from 0.5 NM (60 m/s, ball at 0.7 NM as in a real approach); 0.3° too high (under the OK threshold) -> OK; 82 m/s -> NESA, 48 m/s -> LIG, no LIG in Case III
        string TrapNote(double step, double err, int cn = 1)
        {
            var c = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one", caseNo = cn };
            double tt = 100; bool bl = false; string last = "";
            for (double d = -NM; d < Aim + 20; d += step, tt += 0.25)
            {
                var tg = G(d, d is > -0.5 * NM and < -0.25 * NM ? err : 0, 70);
                if (!bl && d >= -0.7 * NM) { bl = true; S(c, "Enfield 1-1", "Enfield 1-1, Hornet ball, 5.1", tg, tt); }
                c.Sample(tg, tt, 8.1, "FA-18C_hornet");
            }
            return Tk(c, "Enfield 1-1", G(Aim, 0, 10), tt + 1);
        }
        string nOk = TrapNote(15, 0.1), nOk2 = TrapNote(15, 0.3), nShort = TrapNote(20.5, 0.1), nLong = TrapNote(12, 0.1), nLong3 = TrapNote(12, 0.1, 3);
        check(nOk.Contains("LSO: _OK_ ") && !nOk.Contains("NESA") && !nOk.Contains("LIG") && nOk2.Contains("LSO: OK ") && nShort.Contains("NESA") && nShort.Contains("LSO: OK ") && nLong.Contains("LIG") && !nLong3.Contains("LIG") && nLong3.Contains("LSO: _OK_ "),
              $"Träger LSO: _OK_ selten, NESA/LIG -> {nOk} | {nOk2} | {nShort} | {nLong} | {nLong3}");
        // largest error first: slightly low (0.7°), but 2° left -> lineup instead of Power
        var cp = new Carrier { Stage = 4, boat = "CVN" };   // after "Roger ball" (R5)
        cp.groove.Add((0.5, -0.7, -2.0, double.NaN, 900));
        var cpc = cp.LsoCall(900);
        check(cpc == "Right for lineup.", "LSO: größter Fehler zuerst -> " + cpc);
        // Power graded by deviation, trend from the last two samples
        string Pw(double gs, double prev = double.NaN) { var q = new Carrier { Stage = 4, boat = "CVN" }; if (!double.IsNaN(prev)) q.groove.Add((0.6, prev, 0, double.NaN, 899)); q.groove.Add((0.5, gs, 0, double.NaN, 900)); return q.LsoCall(900) ?? ""; }
        check(Pw(-0.7) == "A little power." && Pw(-1.1) == "Power." && Pw(-1.4) == "Power, power." && Pw(-0.7, -0.1) == "Don't settle." && Pw(-0.7, -1.4) == "Easy with it.",
              $"LSO: Power abgestuft -> {Pw(-0.7)} / {Pw(-1.1)} / {Pw(-1.4)} / {Pw(-0.7, -0.1)} / {Pw(-0.7, -1.4)}");
        // lower and still sinking stays "Power"/"Power, power." (no escalation downward)
        check(Pw(-1.1, -0.4) == "Power." && Pw(-1.4, -0.7) == "Power, power.", $"LSO: tief und sinkend -> {Pw(-1.1, -0.4)} / {Pw(-1.4, -0.7)}");
        // Trend over >= 1 s: real drift 0.3°/s (samples 0.53 s, last one after 0.27 s with full 0.5 s change) -> no "Don't settle."
        var qt = new Carrier { Stage = 4, boat = "CVN" };
        foreach (var (tq, gq) in new[] { (897.9, -0.35), (898.43, -0.5), (898.96, -0.65), (899.23, -0.8) }) qt.groove.Add((0.5, gq, 0, double.NaN, tq));
        var qtc = qt.LsoCall(899.23);
        check(qtc == "A little power.", "LSO: Trend über 1 s statt zwei Proben -> " + qtc);
        // R5: before "Roger ball" only Power (lineup, slow, "A little power" silent)
        string Pre(double gs, double lu, double ao) { var q = new Carrier { Stage = 3, boat = "CVN", balled = true }; q.groove.Add((0.6, gs, lu, ao, 900)); return q.LsoCall(900) ?? "-"; }   // Clara: ball reported, Stage 3
        check(Pre(-0.3, -2, 2) == "-" && Pre(-0.7, 0, 0) == "-" && Pre(-1.1, -2, 2) == "Power.", $"LSO R5: vor dem Ball -> {Pre(-0.3, -2, 2)} / {Pre(-0.7, 0, 0)} / {Pre(-1.1, -2, 2)}");
        // R3: groove samples only from 0.75 NM, heading ±15° to FB, no turn (not 0.9 NM, not 40° oblique, not at 28°/s)
        var cg3 = new Carrier { Stage = 3, boat = "CVN" };
        int Smp(double deck, double hdgOff, double tt) { cg3.Sample(G(deck, 0, 70) with { Hdg = fb + hdgOff * Math.PI / 180 }, tt); return cg3.groove.Count; }
        var r3 = $"{Smp(-0.9 * NM, 0, 1)}{Smp(-0.6 * NM, 40, 1)}{Smp(-0.59 * NM, -14, 3)}{Smp(-0.58 * NM, 0, 3.5)}{Smp(-0.55 * NM, 0, 4)}";   // 3 s: 54°/s, 3.5 s: 28°/s
        check(r3 == "00001", "LSO R3: Proben nur im Groove -> " + r3);
        // R4: lineup in IC/AR in meters: 0.6° at 0.03 NM (1.4 m) no grade, 3° at 0.1 NM (13.6 m) _LUR_IC
        var c4 = new Carrier { Stage = 4, boat = "CVN" };
        c4.groove.AddRange(new[] { (0.1, 0.0, 3.0, double.NaN, 900.0), (0.03, 0.0, 0.6, double.NaN, 901.0) });
        var nr4 = c4.Note("wo");
        check(nr4.Contains("_LUR_IC") && !nr4.Contains("AR"), "LSO R4: Lineup in Metern -> " + nr4);
        // On-speed per type from the table: default Hornet 8.1 only, unknown NaN, longest prefix wins
        check(OnSpeed("FA-18C_hornet") == 8.1 && double.IsNaN(OnSpeed("T-45")) && double.IsNaN(OnSpeed("")), "LSO: On-speed-AoA Vorgabe nur Hornet");
        var osOld = OnSpeedAoa;
        OnSpeedAoa = new() { ["FA-18"] = 8.1, ["FA-18C_x"] = 7.5, ["F-14"] = 10.0 };
        check(OnSpeed("FA-18C_hornet") == 8.1 && OnSpeed("FA-18C_x1") == 7.5 && OnSpeed("F-14B") == 10.0 && double.IsNaN(OnSpeed("Su-33")), "LSO: On-speed-AoA je Typ aus der Tabelle");
        OnSpeedAoa = osOld;
        // Waveoff (in IC 1.5° too low) and bolter (touched down, off again): calls, grade, new attempt
        string Pass(Func<double, Telemetry> path, bool ball = true)
        {
            var c = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one", balled = ball };   // #16: ball call reported
            var l = new List<string>();
            double tt = 500;
            for (double d = -NM; d < 1200; d += 17.5, tt += 0.25)
            {
                c.Sample(path(d), tt);
                if (tt % 1 == 0) l.Add(Tk(c, "Enfield 1-1", path(d), tt));
            }
            return string.Join(" / ", l.Where(x => x != "")) + (c.Stage == 3 ? "" : " (Stage " + c.Stage + ")");
        }
        var wo = Pass(d => d < -0.04 * NM ? G(d, d > -0.25 * NM ? -1.5 : 0, 70) : G(d, 0, 70) with { AltMsl = 100 });
        var bo = Pass(d => d < 150 ? G(d, 0, 70) : G(d, 0, 70) with { AltMsl = 60 });
        check(wo.Contains("Paddles: Wave off, wave off.") && wo.Contains(": LSO: WO, groove ") && wo.EndsWith(", _LO_IC (1 Punkt)")
              && bo.Contains("Paddles: Bolter, bolter, bolter.") && bo.Contains(": LSO: B, groove ") && bo.EndsWith(", NESA (2.5 Punkte)"), $"Träger LSO: Waveoff -> {wo} | Bolter -> {bo}");
        // #16 reporting obligation: without ball call (in IM 1.2° too low) no LSO calls, waveoff at 0.3 NM, grade "WO (kein Ball-Call)"
        var nb = Pass(d => d < -0.25 * NM ? G(d, d > -0.6 * NM ? -1.2 : 0, 70) : G(d, 0, 70) with { AltMsl = 100 }, false);
        var nbx = Tk(new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" }, "Enfield 1-1", G(-0.2 * NM, 0, 70), 600);   // turned in late, never heard "call the ball": no consequence without warning
        check(nb.StartsWith("Paddles: Enfield one one, call the ball. / Paddles: Wave off, wave off.") && !nb.Contains("Power") && nb.Contains(": LSO: WO (kein Ball-Call), groove ") && nbx == "", $"Träger #16: ohne Ball-Call -> {nb} | ohne call the ball {nbx}");
        // M6 AI at the carrier: foul deck -> waveoff without grade; 2 AI in the pattern -> no Charlie; three go-arounds over the bow -> divert
        Traffic Ai(int id, Telemetry p) => new(id, "FA-18C_hornet", p.X, p.Z, p.AltMsl, p.Hdg, p.Ias, "AI " + id, "cv", p.AltMsl > DeckH + 8, 2);
        Air = new[] { Ai(91, G(120, 0, 5)) };   // stands on the landing area
        var fd = Pass(d => G(d, 0, 70));
        var ch = new Carrier { Stage = 1, boat = "CVN", cs = "Enfield one one", charlieAt = 990 };   // Charlie time just reached, not exceeded (A20)
        Stack("CVN").Insert(0, ch);
        Air = new[] { Ai(93, At(-2 * NM, 0, 800)), Ai(94, At(-3 * NM, 0, 800)) };
        var chAi = Tk(ch, "Enfield 1-1", hold, 1000);
        Air = Array.Empty<Traffic>();
        var chFree = Tk(ch, "Enfield 1-1", hold, 1001);
        ch.Leave();
        var dv = new List<string>();
        for (int k = 0; k < 3; k++)
            foreach (var d in new[] { -0.3 * NM, 500 }) { Air = new[] { Ai(92, G(d, 0, 70) with { AltMsl = 100 }) }; dv.AddRange(Divert()); }
        for (int k = 0; k < 3; k++)   // R363: go-arounds at foul deck do not count
            foreach (var d in new[] { -0.3 * NM, 500 }) { Air = new[] { Ai(96, G(d, 0, 70) with { AltMsl = 100 }), Ai(91, G(120, 0, 5)) }; dv.AddRange(Divert()); }
        Air = Array.Empty<Traffic>();
        Decks = new[] { Ai(95, G(120, 0, 5)) };   // player stands on the angled deck
        var fdp = Pass(d => G(d, 0, 70));
        Decks = Array.Empty<Traffic>();
        check(fdp.Contains("Paddles: Wave off, wave off, foul deck.") && fdp.Contains(": LSO: WOFD"), $"Träger Foul Deck durch Spieler: {fdp}");
        check(fd.Contains("Paddles: Wave off, wave off, foul deck.") && fd.Contains(": LSO: WOFD") && chAi == "" && chFree.StartsWith("Tower: Enfield one one, signal Charlie.") &&
              string.Join(",", dv) == "AI 92", $"Träger KI: {fd} | Charlie {chAi}/{chFree} | umleiten {string.Join(",", dv)}");
        // R135: ship still turning into the wind (BRC 40° off the target) -> no Charlie on time, Charlie after the turn or at the latest 5 min later
        var (bsv, bTurn) = (Boats, b with { Wz = 10, Hdg = 240 * Math.PI / 180 });
        var cvt = new Carrier { Stage = 1, boat = "CVN", cs = "Enfield one one", charlieAt = 990 };
        Stack("CVN").Insert(0, cvt);
        Boats = new() { bTurn };
        var cvw = new Carrier { Stage = 1, boat = "CVN", cs = "Enfield one two", charlieAt = 1080 };   // R276: second in holding
        Stack("CVN").Insert(1, cvw);
        var turnCalls = new List<string>();
        for (int s = 1000; s < 1290; s++) turnCalls.AddRange(new[] { Tk(cvt, "Enfield 1-1", hold, s), Tk(cvw, "Enfield 1-2", hold, s) }.Where(x => x != ""));   // R276: by the second, not 1000 -> 1291
        string crTurn = string.Join("/", turnCalls), crLate = Tk(cvt, "Enfield 1-1", hold, 1290);
        var r276 = (cvt.charlieAt, cvw.charlieAt, cvw.Stage, Stack("CVN").IndexOf(cvw), deckDelta.Contains("CVN"));
        cvt.Leave(); cvw.Leave(); cvt = new Carrier { Stage = 1, boat = "CVN", cs = "Enfield one one", charlieAt = 990 }; Stack("CVN").Insert(0, cvt);
        Boats = new() { bTurn with { Hdg = bTurn.ExpBrc * Math.PI / 180 } };
        string crDone = Tk(cvt, "Enfield 1-1", hold, 1001);
        cvt.Leave(); Boats = bsv;
        check(crTurn == "Marshal: 99, signal Delta." && r276 == (990, 1080, 1, 0, false) && crLate.StartsWith("Tower: Enfield one one, signal Charlie.") && crDone.StartsWith("Tower: Enfield one one, signal Charlie."),
              $"Träger R135/R276: Charlie erst im Wind, im Holding nur \"99, signal Delta\" -> {crTurn} | {r276} | {crLate}/{crDone}");
        // N30: AI at 0.7 NM aft -> one ball call (Hornet, fuel 5.4 from 0.5 tank), not repeated; bolter once; player in the groove -> silent
        Traffic Cv(int id, double d, string cs = "", double fuel = 0.5) => Ai(id, G(d, 0, 70) with { AltMsl = d > 0 ? 40 : 100 }) with { Group = "AI 95", Cs = cs, Fuel = fuel };
        Air = new[] { Cv(95, -0.7 * NM) };
        var n1 = AiCalls(_ => false);
        var n2 = AiCalls(_ => false);
        Air = new[] { Cv(95, 300) };
        var n3 = AiCalls(_ => false);
        var n4 = AiCalls(_ => false);
        Air = new[] { Cv(95, -0.7 * NM) };
        var n5 = AiCalls(_ => false);   // same approach after bolter not, only after the flyby ahead
        // second attempt after bolter: downwind and 180-degree turn stay within 1 NM aft, ahead over the deck (> 400 m) counts as a new approach
        Air = new[] { Cv(95, 600) with { AltMsl = 150 } };
        AiCalls(_ => false);
        Air = new[] { Cv(95, -0.75 * NM) };
        var n7 = AiCalls(_ => false);
        Air = new[] { Cv(95, 600) with { AltMsl = 150 } };
        AiCalls(_ => false);
        Air = new[] { Cv(95, -0.7 * NM) };
        var n6 = AiCalls(_ => true);
        // two aircraft of a group: each own callsign and own fuel from the unit's T line
        Air = new[] { Cv(96, -0.7 * NM, "Enfield11"), Cv(97, -0.6 * NM, "Enfield12", 0.25) };
        var n8 = AiCalls(_ => false);
        Air = Array.Empty<Traffic>();
        AiCalls(_ => false);
        check(n1.Count == 1 && n1[0].Pilot == "Hornet ball, five point four." && n1[0].Lso == "Roger ball." && n2.Count == 0 &&
              n3.Count == 1 && n3[0].Lso == "Bolter, bolter, bolter." && n3[0].Pilot == "" && n4.Count == 0 && n5.Count == 0 && n7.Count == 1 && n6.Count == 0 && aiBall.Count == 0 &&
              string.Join("/", n8.Select(x => x.Cs + ": " + x.Pilot)) == "Enfield11: Enfield one one, Hornet ball, five point four./Enfield12: Enfield one two, Hornet ball, two point seven.",
              $"Träger N30: KI-Funk -> {string.Join("/", n1.Select(x => x.Pilot))} {n2.Count} {string.Join("/", n3.Select(x => x.Lso))} {n4.Count} {n5.Count} 2. Anlauf {n7.Count} {n6.Count} {aiBall.Count} | {string.Join("/", n8.Select(x => x.Cs + ": " + x.Pilot))}");
        // N30: player lock for the AI radio only in the groove, not already from the initial (stage 3) or in the downwind after a bolter
        var ig = new Carrier { Stage = 3, boat = "CVN" };
        check(ig.InGroove(G(-0.5 * NM, 0, 70)) && !ig.InGroove(G(-3 * NM, 0, 150) with { AltMsl = 800 * Ft }) && !ig.InGroove(At(0, NM, 600)) && !new Carrier { boat = "CVN" }.InGroove(G(-0.5 * NM, 0, 70)),
              "Träger N30: InGroove nur im Groove");
        // A78: gear up in the groove -> waveoff, reason in the grade; gear down or unknown (bo, fd: -1) -> none; warm-up sentence as spoken
        var gu = Pass(d => d < -0.04 * NM ? G(d, 0, 70) with { Gear = 0 } : G(d, 0, 70) with { Gear = 0, AltMsl = 100 });   // after the waveoff he climbs
        var gd = Pass(d => G(d, 0, 70) with { Gear = 1 });
        check(gu.Contains("Paddles: Wave off, wave off.") && gu.Contains(": LSO: WO, groove ") && gu.EndsWith(", Fahrwerk oben") && !gd.Contains("Wave off") && !bo.Contains("Wave off") && LsoLines.Contains("Wave off, wave off, foul deck."),
              $"Träger: Fahrwerk oben -> Waveoff {gu} | unten {gd}");
        // R103: waveoff ignored and landed -> Paddles repeats at most twice, grade C with 0 points (with foul deck with reason)
        string Cut(bool fl)
        {
            var c = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one", balled = true };
            Decks = fl ? new[] { Ai(95, G(120, 0, 5)) } : Array.Empty<Traffic>();
            var l = new List<string>();
            double tt = 500;
            for (double d = -NM; d < Aim + 20; d += 17.5, tt += 0.25)
            {
                var tg = G(d, !fl && d is > -0.25 * NM and < -0.1 * NM ? -1.5 : 0, 70);
                c.Sample(tg, tt);
                if (tt % 1 == 0) l.Add(Tk(c, "Enfield 1-1", tg, tt));
            }
            l.Add(Tk(c, "Enfield 1-1", G(Aim, 0, 10), tt + 1));
            Decks = Array.Empty<Traffic>();
            return string.Join(" | ", l.Where(x => x != ""));
        }
        var (cut, cutF) = (Cut(false), Cut(true));
        int Wo(string s, string w) => s.Split(" | ").Count(x => x == w);
        check(Wo(cut, "Paddles: Wave off, wave off.") is 2 or 3 && cut.Contains(" | : LSO: C, Waveoff missachtet") && cut.EndsWith(" (0 Punkte)") && !cut.Contains("groove")
              && Wo(cutF, "Paddles: Wave off, wave off, foul deck.") is 2 or 3 && cutF.EndsWith(", foul deck (0 Punkte)") && cutF.Contains(": LSO: C, "),
              $"Träger R103: Waveoff missachtet -> {cut} || {cutF}");
        // R133: uncertainly understood ball call in the groove counts (Paddles, not Marshal "say again"); nonsense in the groove -> no call
        var ub = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" };
        var ubs = ub.OnTranscript("Enfield 1-1, Hornet ball, 5.1", M("Enfield 1-1", G(-0.45 * NM, 0, 70)), 600, true);
        var ubn = new Carrier { Stage = 3, boat = "CVN" }.OnTranscript("Enfield 1-1, banana split", M("Enfield 1-1", G(-0.45 * NM, 0, 70)), 600, true);
        check(ubs is [{ Station: "Paddles", Text: "Roger ball." }] && ub.Stage == 4 && ubn.Count == 0,
              $"Träger R133: unsicherer Ball-Call -> {string.Join(" / ", ubs.Select(x => x.Station + ": " + x.Text))} | Unsinn {ubn.Count}");
        // R230: Case III "Roger ball" with wind over deck (speed 19 kt, no wind: from ahead, 9° right of the landing area)
        var rb3 = S(new Carrier { Stage = 3, caseNo = 3, boat = "CVN" }, "Enfield 1-1", "Enfield 1-1, Hornet ball, 5.1", G(-0.45 * NM, 0, 70), 600);
        check(rb3 == "Paddles: Roger ball, 19 knots, starboard.", "Träger R230: Roger ball Case III -> " + rb3);
        // M6: Case by weather/time; Case III: Marshal (radial, DME, EAT), commence, ACLS/needles, three miles, ¾ mile
        Sky = (true, 600, 20000);
        int cII = CaseNow();
        Ceiling = false; int cFew = CaseNow(); Ceiling = true;   // R367: FEW/SCT at 600 m is no ceiling -> Case I
        check(cFew == 1 && cII == 2, $"Träger R367: Wolken ohne BKN/OVC -> Case {cFew}, mit Untergrenze Case {cII}");
        Sky = (true, 200, 20000);
        int cIII = CaseNow();
        Clock = 22 * 3600; Sky = (false, 0, 80000);
        int cNight = CaseNow();
        Clock = 19 * 3600; Sun = 10; int cSummer = CaseNow();   // A79: clear sky, 19:00 at sun 10° (summer) -> day; 17:30 at sun -8° (winter) -> night; without sun elevation old time rule (17:30 = day)
        Clock = 17.5 * 3600; Sun = -8; int cWinter = CaseNow();
        Sun = double.NaN; int cNoSun = CaseNow();
        Clock = 10 * 3600; Sky = (true, 200, 20000);
        var (k1, k2) = (new Carrier(), new Carrier());
        var mk1 = S(k1, "Enfield 1-1", "Marshal, Enfield 1-1, checking in, state 6.0", far, 0);
        var mk2 = S(k2, "Enfield 1-2", "Marshal, Enfield 1-2, checking in", far, 0);
        check(cII == 2 && cIII == 3 && cNight == 3 && cSummer == 1 && cWinter == 3 && cNoSun == 1 && mk1 == "Marshal: Enfield one one, Marshal, case three recovery, CV-1 approach, expected final bearing three four five, " +
              "altimeter two niner niner two. Marshal mother's one six five radial, 21 DME, angels 6. Expected approach time zero five. | : Träger: Zum Marshal-Punkt: Radial 165, 21 DME (TACAN 73X), Angels 6; dort Holding, zur Approach-Zeit „commencing“ melden." &&
              mk2.Contains("22 DME, angels 7. Expected approach time zero six."), $"Träger Case III: Marshal -> {cII}/{cIII}/{cNight} {mk1} | {mk2}");
        // A73: at EAT farther than 5 NM from the Marshal point (radial FB+180, 21 DME) and outside the DME: new time (once); #17/R224: at the fix Marshal transmits nothing at EAT,
        // after 1 min once "report commencing", after 2 min new time and violation; "commencing" -> R225 Marshal "radar contact, final bearing"
        var fixP = At(-21 * NM * Math.Cos(fb), -21 * NM * Math.Sin(fb), 18000);
        var eatFar = Tk(k1, "Enfield 1-1", far, 299) + "/" + Tk(k1, "Enfield 1-1", far, 300) + "/" + Tk(k1, "Enfield 1-1", far, 301) + "/" + Tk(k1, "Enfield 1-1", fixP, 400);
        double e0 = k1.charlieAt;
        var eat = Tk(k1, "Enfield 1-1", fixP, e0) + "/" + Tk(k1, "Enfield 1-1", fixP, e0 + 60) + "/" + Tk(k1, "Enfield 1-1", fixP, e0 + 61) + "/" + Tk(k1, "Enfield 1-1", fixP, e0 + 120)
                  + "/" + S(k1, "Enfield 1-1", "Enfield 1-1, commencing, angels 6, state 5.2", fixP, k1.charlieAt) + "/" + k1.Stage;   // Review l4: to the new EAT
        // Review l4: "commencing" before EAT (more than 15 s): "negative, your expected approach time is …", stays in holding; 10 s before counts
        var comEarly = S(k2, "Enfield 1-2", "Enfield 1-2, commencing, angels 7, state 5.8", far, k2.charlieAt - 60) + "/" + k2.Stage;
        check(comEarly.StartsWith("Marshal: Enfield one two, negative, your expected approach time is ") && comEarly.EndsWith("/1"), "Träger Review l4: commencing zu früh -> " + comEarly);
        var com = S(k2, "Enfield 1-2", "Enfield 1-2, commencing, angels 7, state 5.8", far, k2.charlieAt - 10);
        var lad = new List<string>();
        foreach (var dk in new[] { -10 * NM, -6 * NM, -5 * NM, -3 * NM, -1.5 * NM, -0.63 * NM })   // m before the stern; ¾ NM counts from ship's center
            lad.Add(Tk(k1, "Enfield 1-1", G(dk, 0, 70), 400 + dk / 100));
        var ndl = S(k1, "Enfield 1-1", "Enfield 1-1, on and on", G(-5 * NM, 0, 70), 330);   // exactly on glide path and centerline
        lad.RemoveAll(x => x == "");
        check(eatFar == "/Marshal: Enfield one one, Marshal, new expected approach time one one. | : Träger: Noch nicht am Marshal-Punkt (Radial 165, 21 DME, Angels 6): neue Approach-Zeit abwarten.//" &&
              eat.StartsWith("/Marshal: Enfield one one, Marshal, report commencing.//Marshal: Enfield one one, Marshal, new expected approach time ") && eat.Contains(". | : Verstoß: Approach-Zeit ohne „commencing“ verstrichen/Marshal: Enfield one one, radar contact, final bearing three four five, switch Approach. | : Träger: 4000 ft/min auf 5000 ft") && eat.EndsWith("/2")
              && com.StartsWith("Marshal: Enfield one two, radar contact, final bearing three four five, switch Approach. | : Träger: 4000") &&
              string.Join(" | ", lad) == "Approach: Enfield one one, Approach, radar contact, 10 miles. | Approach: Enfield one one, Approach, ACLS lock on, say needles. | Approach: Enfield one one, three miles, on glidepath, on course. | " +
                                         "Approach: Enfield one one, one and a half miles, on glidepath, on course. | Approach: Enfield one one, three quarter mile, call the ball." && ndl == "Approach: Enfield one one, concur.",
              $"Träger Case III: EAT, CV-1 -> {eatFar} | {eat} | {com} | {string.Join(" | ", lad)} | {ndl}");
        // R365/R366: EAT on a full mission minute (check-in at 10:00:10); mission clock twice as fast as real time -> rejection and acceptance by mission time
        double Ms(double mission) { double rt = (mission - 36010) / 2; (Clock, Skew) = (mission, mission - rt); return rt; }
        var k3 = new Carrier();
        var mk3 = S(k3, "Enfield 1-3", "Marshal, Enfield 1-3, checking in", far, Ms(36010));
        double e3 = k3.charlieAt;
        var mm3 = Digits(((int)(e3 / 60) % 60).ToString("00"));
        var neg3 = S(k3, "Enfield 1-3", "Enfield 1-3, commencing", far, Ms(e3 - 20)) + "/" + k3.Stage;
        var ok3 = S(k3, "Enfield 1-3", "Enfield 1-3, commencing", far, Ms(e3)) + "/" + k3.Stage;
        (Clock, Skew) = (10 * 3600, 0);
        check(e3 % 60 == 0 && e3 > 36010 && mk3.Contains($"Expected approach time {mm3}.") && neg3 == $"Marshal: Enfield one three, negative, your expected approach time is {mm3}./1"
              && ok3.StartsWith("Marshal: Enfield one three, radar contact, final bearing") && ok3.EndsWith("/2"), $"Träger R365/R366: EAT Missionszeit -> {e3} {mk3} | {neg3} | {ok3}");
        // A129: needles against the offset (1.5° too high, 1° right of the line: fly-to needles "down and left"); wrong report -> disregard + offset
        double off = (5 * NM + Aim) * Math.Tan(Math.PI / 180);
        var hiRt = G(-5 * NM, 1.5, 70);
        hiRt = hiRt with { X = hiRt.X - off * Math.Sin(fb), Z = hiRt.Z + off * Math.Cos(fb) };
        k1.needlesAsked = true;
        var ndOk = S(k1, "Enfield 1-1", "Enfield 1-1, down and left", hiRt, 331);
        k1.needlesAsked = true;
        var ndBad = S(k1, "Enfield 1-1", "Enfield 1-1, up and on", hiRt, 332);
        var ndNo = S(k1, "Enfield 1-1", "Enfield 1-1, up and on", hiRt, 333);   // not asked: no needle answer
        check(ndOk == "Approach: Enfield one one, concur." && ndBad == "Approach: Enfield one one, disregard needles, above glidepath, right of course." && !ndNo.Contains("needles") && !ndNo.Contains("concur"),
              $"Träger A129: Nadeln -> {ndOk} | {ndBad} | {ndNo}");
        // R226: readback of the Case III Marshal instruction -> "readback correct", error only the wrong element; no new check-in (EAT stays)
        var rbk = new Carrier();
        S(rbk, "Enfield 1-9", "Marshal, Enfield 1-9, checking in", far, 3100);
        var (rbEat, rbAng) = (rbk.charlieAt, rbk.angels);
        var rbOk = S(rbk, "Enfield 1-9", $"Enfield 1-9, Marshal 165 radial, {rbAng + 15} DME, angels {rbAng}, time 07", far, 3110);
        var rbBad = S(rbk, "Enfield 1-9", $"Marshal, Enfield 1-9, 165 radial, {rbAng + 15} DME, angels {rbAng + 3}", far, 3120);
        rbk.Leave();
        check(rbOk == "Marshal: Enfield one niner, readback correct." && rbBad == $"Marshal: Enfield one niner, negative, angels {rbAng}." && rbk.charlieAt == rbEat, $"Träger R226: Readback -> {rbOk} | {rbBad}");
        // R368: "Marshal, …, say again" in holding repeats the assignment (EAT, angels), no new check-in (EAT and stack slot stay)
        var c368 = new Carrier();
        var a368 = S(c368, "Enfield 1-9", "Marshal, Enfield 1-9, checking in", far, 3100);
        var (e368, i368) = (c368.charlieAt, Stack("CVN").IndexOf(c368));
        var r368 = S(c368, "Enfield 1-9", "Marshal, Enfield 1-9, say again", far, 3200);
        var k368 = (c368.charlieAt == e368, Stack("CVN").IndexOf(c368) == i368, c368.Stage);
        c368.Leave();
        check(r368 == a368 && r368.Contains("Expected approach time ") && k368 == (true, true, 1), $"Träger R368: say again im Holding -> {r368} | {k368}");
        // R228: Final Bearing changes (ship without wind to 010) -> "final bearing" once, not again afterwards
        var bFb = b with { Hdg = 10 * Math.PI / 180, NoTurn = true };
        var cfb = new Carrier { Stage = 2, caseNo = 3, boat = "CVN", cs = "Enfield one one", radar = true, fbSaid = b.ExpFb };
        Boats = new() { bFb };
        var fbc = Tk(cfb, "Enfield 1-1", far, 3200) + "/" + Tk(cfb, "Enfield 1-1", far, 3210) + "/" + Tk(cfb, "Enfield 1-1", far, 3211);   // only after 10 s without turning (NoTurn ship in the turn: no call per 3°)
        Boats = new() { b };
        check(fbc.Split('/')[1] == $"Approach: Enfield one one, final bearing {Mag(bFb.ExpFb)}." && fbc.Split("final bearing").Length == 2, "Träger R228: neues Final Bearing -> " + fbc);
        // R229: Case I in holding, clouds lowering (Case III) -> once "99, Marshal, case three …" to all per ship, everyone in holding gets a new assignment
        var skySv = Sky;
        Sky = (false, 0, 80000);
        var (r9a, r9b) = (new Carrier(), new Carrier());
        said[b.Group] = 3; said["GONE"] = 1; NewMission(); said["GONE"] = 1;   // Review l4: state of the previous mission (Case III already announced) gone; ship that no longer exists drops out at the next change
        S(r9a, "Enfield 6-1", "Marshal, Enfield 6-1, checking in", hold, 3300); S(r9b, "Enfield 6-2", "Marshal, Enfield 6-2, checking in", hold, 3300);
        Sky = (true, 200, 20000);
        var (r9x, r9y) = (r9a.Tick(M("Enfield 6-1", hold), 3310), r9b.Tick(M("Enfield 6-2", hold), 3310));
        r9a.Leave(); r9b.Leave(); Sky = skySv;
        check(r9x.Count == 3 && r9x[0].All && r9x[0].Text == "99, Marshal, case three recovery, CV-1 approach, expected final bearing three four five, altimeter two niner niner two."
              && r9x[1].Text.StartsWith("Enfield six one, Marshal, case three recovery") && r9y.Count == 2 && r9y[0].Text.StartsWith("Enfield six two, Marshal, case three recovery") && r9a.caseNo == 3 && !said.ContainsKey("GONE"),
              "Träger R229: Case-Wechsel -> " + string.Join(" | ", r9x.Concat(r9y).Select(x => x.Text)));
        // A74: ball/Clara only in the groove; in holding, from 35 NM, too high, too far lateral or ahead of the ship no radio and no stage
        var cb = new Carrier { Stage = 1 };
        string Bl(string w, Telemetry t) => S(cb, "Enfield 1-1", $"Enfield 1-1, Hornet {w}, 5.1", t, 700);
        var bOut = new[] { Bl("ball", far), Bl("Clara", hold), Bl("ball", At(-0.7 * NM, 0, 2000)), Bl("ball", At(-0.7 * NM, NM, 330)), Bl("ball", At(-1.8 * NM, 0, 330)), Bl("ball", At(0.5 * NM, 0, 330)) };
        var bClara = Bl("Clara", At(-0.7 * NM, 0, 330));
        int stClara = cb.Stage;
        var bBall = Bl("ball", At(-0.7 * NM, 0, 330));
        check(bOut.All(x => x == "") && stClara == 1 && bClara == "Paddles: Enfield one one, roger Clara, keep it coming." && bBall == "Paddles: Roger ball." && cb.Stage == 4,
              $"Träger A74: Ball nur im Groove -> {string.Join("/", bOut)} | {bClara} | {bBall}");
        // N29: pigeons without check-in and in holding: bearing, distance, BRC, TACAN, ICLS; stage, Charlie and stack stay; also without Marshal in the call
        var (cg, cq) = (new Carrier(), new Carrier());
        S(cg, "Enfield 1-5", "Marshal, Enfield 1-5, checking in", far, 800);
        var (sg0, ca0, n0) = (cg.Stage, cg.charlieAt, Stack("CVN").Count);
        var pg = S(cg, "Enfield 1-5", "Enfield 1-5, say pigeons", far, 810);
        var pq = S(cq, "Enfield 1-6", "Marshal, Enfield 1-6, request TACAN", At(-45 * NM, 0, 18000), 820);
        check(pg == "Marshal: Enfield one five, mother bears three five four, 35 miles, expected BRC three five four, TACAN seven three X-ray, ICLS channel one one."
              && pq.Contains("mother bears three five four, 45 miles, expected BRC") && cg.Wants(Normalize("Enfield 1-5, say pigeons"))
              && (cg.Stage, cg.charlieAt, Stack("CVN").Count) == (sg0, ca0, n0) && cq.Stage == 0 && !Stack("CVN").Contains(cq),
              $"Träger N29: Pigeons -> {pg} | {pq}");
        cg.Leave();
        // R23: "state 4.5" in holding is acknowledged, no new check-in (stage, Charlie, stack, angels stay)
        var cs1 = new Carrier();
        S(cs1, "Enfield 1-7", "Marshal, Enfield 1-7, checking in", far, 830);
        var (ss0, sa0, sn0, sang0) = (cs1.Stage, cs1.charlieAt, Stack("CVN").Count, cs1.angels);
        var st1 = S(cs1, "Enfield 1-7", "Marshal, Enfield 1-7, state 4.5", far, 840);
        var st2 = S(cs1, "Enfield 1-7", "Marshal, Enfield 1-7, state 3", far, 850);
        check(st1 == "Marshal: Enfield one seven, Marshal, roger, state four point five." && st2 == "Marshal: Enfield one seven, Marshal, roger, state three."
              && (cs1.Stage, cs1.charlieAt, Stack("CVN").Count, cs1.angels) == (ss0, sa0, sn0, sang0),
              $"Träger R23: state quittiert -> {st1} | {st2}");
        cs1.Leave();
        // R22: bolter/waveoff in CV-1 -> radar vectoring (downwind, stage 2), second approach with ACLS call; Case I stays in the pattern
        var cbR = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one", caseNo = 3, ladder = 2 };
        var bol = Tk(cbR, "Enfield 1-1", At(0.6 * NM, 0, 500), 900);
        // Downwind (heading FB+180, 1.5 NM left): abeam no "three miles"/ACLS, 5 NM aft vector back to final, then ACLS
        Telemetry Dw(double a, double l) => new(1200 * Ft, 0, 80, fb + Math.PI, a * Math.Cos(fb) - l * Math.Sin(fb), a * Math.Sin(fb) + l * Math.Cos(fb), 0, 0, 0);
        var dw = Tk(cbR, "Enfield 1-1", Dw(-0.1 * NM, -1.5 * NM), 950) + "/" + Tk(cbR, "Enfield 1-1", Dw(-4.5 * NM, -1.5 * NM), 960);
        var (dwSt, dwLad, dwStn) = (cbR.Stage, cbR.ladder, cbR.Station);
        var turn = Tk(cbR, "Enfield 1-1", Dw(-5.5 * NM, -1.5 * NM), 990) + "/" + Tk(cbR, "Enfield 1-1", Dw(-5.8 * NM, -1.5 * NM), 995);
        var again = Tk(cbR, "Enfield 1-1", G(-6 * NM, 0, 70), 1000);
        var cbR1 = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one", caseNo = 1 };
        var bol1 = Tk(cbR1, "Enfield 1-1", At(0.6 * NM, 0, 500), 900);
        check(bol == $"Departure: Enfield one one, Departure, radar contact, turn left heading {Mag(b.Fb + 180)}, maintain 1200, downwind. | : Träger: Geradeaus auf 1200 ft, links auf Kurs {MagNum(b.Fb + 180):000} (Downwind); Departure führt dich in den Downwind, Approach zurück auf den Endanflug."
              && cbR.Stage == 2 && again.StartsWith("Approach: Enfield one one, Approach, ACLS lock on") && bol1 == "" && cbR1.Stage == 3
              && dw == "/" && (dwSt, dwLad, dwStn) == (2, -1, "Departure") && cbR.Station == "Approach" && turn == $"Approach: Enfield one one, Approach, turn left heading {Mag(b.Fb)}, intercept final./",
              $"Träger R22: Bolter CV-1 -> {bol} | Downwind {dw} | {turn} | {again} | Case I {bol1}");
        // R20: CV-1 guidance: commencing -> radar contact with distance, platform without commencing -> radar contact (then roger); offset > 0.3 NM -> fly heading
        // (at most every 30 s, not when turning back); mile calls 2 and 1½, below that silent
        Telemetry Fin(double a, double l, double off = 0) => new(3000 * Ft, 3000 * Ft, 80, fb + off * Math.PI / 180, a * Math.Cos(fb) - l * Math.Sin(fb), a * Math.Sin(fb) + l * Math.Cos(fb), 0, 0, 0);
        var cr20 = new Carrier { Stage = 1, caseNo = 3, boat = "CVN" };
        var rc = S(cr20, "Enfield 1-1", "Enfield 1-1, commencing, angels 6, state 5.8", Fin(-18 * NM, 0), 100) + "/" + S(cr20, "Enfield 1-1", "Enfield 1-1, platform", Fin(-15 * NM, 0), 150);
        var pf20 = S(new Carrier { Stage = 2, caseNo = 3, boat = "CVN" }, "Enfield 1-2", "Enfield 1-2, platform", Fin(-17 * NM, 0), 150);
        var cm20 = new Carrier { Stage = 2, caseNo = 3, boat = "CVN" };   // Marshal said commence (Stage 2), pilot answers "commencing" -> radar contact, then roger
        pf20 += "/" + S(cm20, "Enfield 1-3", "Enfield 1-3, commencing", Fin(-17 * NM, 0), 150) + "/" + S(cm20, "Enfield 1-3", "Enfield 1-3, commencing", Fin(-16 * NM, 0), 160);
        var st20 = Tk(cr20, "Enfield 1-1", Fin(-10 * NM, -0.6 * NM), 200) + "/" + Tk(cr20, "Enfield 1-1", Fin(-10 * NM, -0.6 * NM), 210) + "/" + Tk(cr20, "Enfield 1-1", Fin(-9.5 * NM, -0.5 * NM, 20), 240)
                   + "/" + Tk(cr20, "Enfield 1-1", Fin(-9 * NM, 1.2 * NM), 280);
        var mi20 = Tk(cr20, "Enfield 1-1", G(-1.9 * NM, 0, 70), 300) + "/" + Tk(cr20, "Enfield 1-1", G(-1.4 * NM, 0, 70), 310) + "/" + Tk(cr20, "Enfield 1-1", G(-NM, 0, 70), 320);
        check(rc.StartsWith("Marshal: Enfield one one, radar contact, final bearing three four five, switch Approach. | : Träger: 4000") && rc.EndsWith("/Approach: Enfield one one, Approach, radar contact, 15 miles.") && pf20.StartsWith("Approach: Enfield one two, Approach, radar contact, 17 miles./Approach: Enfield one three, Approach, radar contact, 17 miles./Approach: Enfield one three, roger.") && pf20.EndsWith("/Approach: Enfield one three, roger.")
              && st20 == $"Approach: Enfield one one, left of course, fly heading {Mag(b.Fb + 20)}.///Approach: Enfield one one, right of course, fly heading {Mag(b.Fb - 30)}."
              && mi20 == "Approach: Enfield one one, two miles, on glidepath, on course./Approach: Enfield one one, one and a half miles, on glidepath, on course./" && cr20.Stage == 3,
              $"Träger R20: CV-1-Führung -> {rc} | {pf20} | {st20} | {mi20}");
        cr20.Leave();
        // R282: after "commencing" (switch Approach) without report to Approach: above the platform no heading call; below it Approach identifies by itself, then heading
        var g282 = new Carrier { Stage = 2, caseNo = 3, boat = "CVN", cs = "Enfield one one" };
        var st282 = Tk(g282, "Enfield 1-1", Fin(-15 * NM, -0.6 * NM) with { AltMsl = 6000 * Ft }, 400) + "/" + Tk(g282, "Enfield 1-1", Fin(-15 * NM, -0.6 * NM) with { AltMsl = 4000 * Ft }, 410)
                    + "/" + Tk(g282, "Enfield 1-1", Fin(-14.5 * NM, -0.6 * NM) with { AltMsl = 3800 * Ft }, 420);
        check(st282 == $"/Approach: Enfield one one, Approach, radar contact, 15 miles./Approach: Enfield one one, left of course, fly heading {Mag(b.Fb + 20)}.", "Träger R282: Approach identifiziert vor der ersten Kursansage -> " + st282);
        // R23: bingo (reported fuel below distance to alternate + reserve) or third bolter -> signal with bearing/distance, recovery ends; fuel from the fuel gauge before the reported
        var kob = new Airfield { Name = "Kobuleti", X = 0, Z = 42 * NM };
        var cb23 = new Carrier();
        S(cb23, "Enfield 1-8", "Marshal, Enfield 1-8, checking in, state 1.2", far, 900);   // without alternate no signal, normal check-in
        int st23 = cb23.Stage;
        Alternate = (_, _) => kob;
        var bg = S(cb23, "Enfield 1-8", "Marshal, Enfield 1-8, state 2.6", hold, 905) + "/" + S(cb23, "Enfield 1-8", "Marshal, Enfield 1-8, state 1.8", hold, 910);
        var bgDone = cb23.Stage == 0 && cb23.Gone && !Stack("CVN").Contains(cb23);
        var cdv = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one", balled = true };   // ball reported per pass (#16)
        var (dvl, fdl) = (new List<string>(), new List<string>());
        double tdv = 2000;
        void Bolters(List<string> into)
        {
            for (int k = 0; k < 3; k++, cdv.balled = true)
                for (double d = -NM; d < 1200; d += 17.5, tdv += 0.25)
                {
                    var pt = d < 150 ? G(d, 0, 70) : G(d, 0, 70) with { AltMsl = 60 };
                    cdv.Sample(pt, tdv);
                    if (tdv % 1 == 0) into.Add(Tk(cdv, "Enfield 1-1", pt, tdv));
                }
        }
        Decks = new[] { Ai(95, G(120, 0, 5)) };   // R363: three passes at foul deck first -> no failed attempt, no divert
        Bolters(fdl);
        Decks = Array.Empty<Traffic>();
        var fd363 = (cdv.passes, fdl.Count(x => x.Contains("LSO: WOFD")), fdl.Any(x => x.Contains("your signal")));
        Bolters(dvl);
        dvl.RemoveAll(x => x == "");
        Alternate = (_, _) => null;
        var lb23 = (cb23.Lb(new Ops.Me("x", null, "FA-18C_hornet", 2, 1, 0.15)), new Carrier { state = 2.5 }.Lb(M("x", far)), new Carrier().Lb(M("x", far)));
        check(st23 == 1 && bg == $"Marshal: Enfield one eight, Marshal, roger, state two point six./Marshal: Enfield one eight, your signal is bingo, pigeons Kobuleti {Mag(90)}, 44 miles. | : Träger: Bingo: Kobuleti, Kurs {MagNum(90):000}, 44 NM. Recovery beendet."
              && bgDone && dvl.Count(x => x.Contains("LSO: B,")) == 3 && dvl[^1].Contains("Tower: Enfield one one, your signal is divert, pigeons Kobuleti ") && dvl.Count(x => x.Contains("your signal")) == 1 && cdv.Stage == 0 && cdv.Gone
              && Math.Abs(lb23.Item1 - 1620) < 1 && lb23.Item2 == 2500 && double.IsNaN(lb23.Item3) && fd363 == (0, 3, false),
              $"Träger R23/R363: Bingo/Divert -> {st23} {bg} | Foul Deck {fd363} | {string.Join(" / ", dvl)} | {lb23}");
        // P3-T2: bolter call already on sliding through on the deck (180 m behind the stern, still fast), not in the rollout after the catch (slow)
        string Roll(double ias)
        {
            var c = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" };
            double tt = 1500;
            for (double d = -NM; d < 140; d += 17.5, tt += 0.25) c.Sample(G(d, 0, 70), tt);
            return Tk(c, "Enfield 1-1", G(180, 0, ias) with { AltMsl = DeckH + 3 }, tt);
        }
        var (slide, trapped) = (Roll(70), Roll(30));
        check(slide == "Paddles: Bolter, bolter, bolter." && !trapped.Contains("Bolter") && bo.Contains("Paddles: Bolter"), $"Träger P3-T2: Bolter-Call beim Durchrutschen -> {slide} | gefangen {trapped}");
        // #18/R21/R227: Case II (cloud ceiling 1970 ft) like Case III; at 10 DME once "report see you at ten", no automatic switch; on "see you at ten"
        // "roger, switch Tower" and from the initial Case I (zip lip, A130); without a report CV-1 stays until "call the ball"
        Sky = (true, 600, 20000);
        var c2c = new Carrier();
        S(c2c, "Enfield 1-1", "Marshal, Enfield 1-1, checking in", far, 0);
        c2c.charlieAt = 310;   // EAT reached (review l4: previously "negative")
        S(c2c, "Enfield 1-1", "Enfield 1-1, commencing, angels 6", far, 310);
        var cl2 = Tk(c2c, "Enfield 1-1", At(-12 * NM, 0, 5000), 320) + "/" + Tk(c2c, "Enfield 1-1", At(-8 * NM, 0, 3000), 330) + "/" + Tk(c2c, "Enfield 1-1", At(-8 * NM, 0, 1200), 340) + "/" + c2c.caseNo
                  + "/" + c2c.Suggest(At(-8 * NM, 0, 1200))?.Text;
        var pti = S(c2c, "Enfield 1-1", "Enfield 1-1, see you at ten", At(-8 * NM, 0, 1200), 345);
        var ci = Tk(c2c, "Enfield 1-1", At(-3 * NM, 0, 800), 400);
        var c2n = new Carrier { Stage = 2, caseNo = 2, boat = "CVN", cs = "Enfield one two", radar = true, delta = true };   // without "see you at ten": CV-1 until the ¾ mile
        var cvn = Tk(c2n, "Enfield 1-2", G(-0.63 * NM, 0, 70), 500);
        check(c2c.caseNo == 1 && cl2 == "/Approach: Enfield one one, Approach, radar contact, 8 miles./Approach: Enfield one one, report see you at ten./2/see you at ten" && pti.StartsWith("Approach: Enfield one one, roger, switch Tower. | : Träger: Sinken zum Initial")
              && ci.StartsWith(": Träger: Initial: zip lip") && c2c.Stage == 3 && cvn == "Approach: Enfield one two, three quarter mile, call the ball.",
              $"Träger #18/R227: Case II -> {cl2} | {pti} | {ci} | {cvn}");
        // R232: Case I in holding "see you at" -> "update state, switch Tower", answer with number only counts as fuel report
        var c32 = new Carrier();
        Sky = (false, 0, 80000);
        S(c32, "Enfield 1-3", "Marshal, Enfield 1-3, checking in", hold, 3400);
        var see = S(c32, "Enfield 1-3", "Enfield 1-3, see you at 10", hold, 3410) + "/" + S(c32, "Enfield 1-3", "Enfield 1-3, 5.9", hold, 3415);
        c32.Leave();
        check(see == "Marshal: Enfield one three, update state, switch Tower./Marshal: Enfield one three, Marshal, roger, state five point niner.", "Träger R232: see you at -> " + see);
        // R280: "see you at" proposed only once, afterwards the Tower speaks (say again, second "see you at"); fuel in the call -> no "update state", below bingo signal;
        // "platform" only until contact with Approach, not again after a bolter
        var c80 = new Carrier();
        S(c80, "Enfield 8-1", "Marshal, Enfield 8-1, checking in", hold, 3500);
        var s80 = c80.Suggest(hold)?.Text + "/" + S(c80, "Enfield 8-1", "Enfield 8-1, see you at angels 2", hold, 3510) + "/" + (c80.Suggest(hold)?.Text ?? "-")
                  + "/" + S(c80, "Enfield 8-1", "Enfield 8-1, see you at angels 2", hold, 3520) + "/" + string.Join("", c80.OnTranscript("Enfield 8-1, banana", M("Enfield 8-1", hold), 3530, true).Select(x => x.Station + ": " + x.Text));
        c80.Leave();
        var c80b = new Carrier();
        S(c80b, "Enfield 8-2", "Marshal, Enfield 8-2, checking in", hold, 3600);
        var f80 = S(c80b, "Enfield 8-2", "Enfield 8-2, see you at angels 3, state 5.8", hold, 3610);
        c80b.Leave();
        Alternate = (_, _) => kob;
        var c80c = new Carrier();
        S(c80c, "Enfield 8-3", "Marshal, Enfield 8-3, checking in", hold, 3700);
        var bg80 = S(c80c, "Enfield 8-3", "Enfield 8-3, see you at angels 2, state 1.2", hold, 3710) + "/" + c80c.Stage;
        Alternate = (_, _) => null;
        c80c.Leave();
        var p80 = new Carrier { Stage = 2, caseNo = 3, boat = "CVN", cs = "Enfield one one" };
        var pl80 = p80.Suggest(far)?.Text + "/" + S(p80, "Enfield 1-1", "Enfield 1-1, platform", Fin(-15 * NM, 0), 3800) + "/" + (p80.Suggest(far)?.Text ?? "-");
        (p80.Stage, p80.ladder) = (3, 2);
        Tk(p80, "Enfield 1-1", At(0.6 * NM, 0, 500), 3900);   // Bolter: radar vectoring, stage 2
        pl80 += "/" + p80.Stage + (p80.Suggest(far)?.Text ?? "-");
        check(s80 == "see you at angels/Marshal: Enfield eight one, update state, switch Tower./-/Tower: Enfield eight one, roger./Tower: Enfield eight one, Tower, say again."
              && f80 == "Marshal: Enfield eight two, roger, switch Tower." && bg80.StartsWith("Marshal: Enfield eight three, your signal is bingo, pigeons Kobuleti") && bg80.EndsWith("/0")
              && pl80 == "platform/Approach: Enfield one one, Approach, radar contact, 15 miles./-/2-", $"Träger R280: erledigte Meldungen -> {s80} | {f80} | {bg80} | {pl80}");
        Sky = (false, 0, 80000);
        // R24: takeoff from the deck: there no suggestion and no radio (advisory only), ship into the wind (BoatGroup); Case I after takeoff text only;
        // Case III "airborne" -> radar contact, "on top" -> frequency change, without a report at 20 NM
        var dkT = new Telemetry(DeckH + 1, 0, 10, 0, 50, 0, 0, 0, 0);
        var upT = At(NM, 0, 500);
        var l1 = new Carrier();
        var dk1 = Tk(l1, "Enfield 3-1", dkT, 100) + "/" + (l1.Suggest(dkT)?.Text ?? "-") + "/" + l1.BoatGroup + "/" + S(l1, "Enfield 3-1", "Ground, Enfield 3-1, request startup", dkT, 101);
        var up1 = Tk(l1, "Enfield 3-1", upT, 130) + "/" + l1.Launching;
        Sky = (true, 200, 20000);
        var (l3, l3b) = (new Carrier(), new Carrier());
        Tk(l3, "Enfield 3-2", dkT, 100); Tk(l3b, "Enfield 3-3", dkT, 100);
        var top = At(10 * NM, 0, 8000);
        var up3 = Tk(l3, "Enfield 3-2", upT, 130) + "/" + l3.Suggest(upT)?.Text + "/" + S(l3, "Enfield 3-2", "Enfield 3-2, airborne", upT, 131) + "/" + l3.Suggest(top)?.Text
                  + "/" + S(l3, "Enfield 3-2", "Enfield 3-2, passing 2.5", top, 200) + "/" + S(l3, "Enfield 3-2", "Enfield 3-2, on top, angels 8", top, 300) + "/" + l3.Launching;   // R233: mandatory report acknowledged
        Tk(l3b, "Enfield 3-3", upT, 130);
        var far3 = Tk(l3b, "Enfield 3-3", At(15 * NM, 0, 8000), 300) + "/" + Tk(l3b, "Enfield 3-3", At(21 * NM, 0, 8000), 400) + "/" + l3b.BoatGroup;
        Sky = (true, 600, 20000);   // R283: Case II own title and procedure
        var l2 = new Carrier();
        Tk(l2, "Enfield 3-4", dkT, 100);
        var up2 = Tk(l2, "Enfield 3-4", upT, 130);
        Sky = (false, 0, 80000);
        check(dk1 == "/-/CVN/: Träger: Auf dem Deck kein Funk (kein Land-Tower): Anlassen, Rollen und Katapult nach Zeichen der Deckcrew." && up1.StartsWith(": Träger: Case-I-Abflug: geradeaus parallel zum BRC") && up1.EndsWith("/False")
              && up3.StartsWith(": Träger: Case-III-Abflug: „airborne“ melden, geradeaus steigen, bei 7 DME auf den 10-DME-Bogen zum Abflugradial 024,") && up3.EndsWith("/airborne/Departure: Enfield three two, Departure, radar contact, departure radial zero two four./on top, angels 8/Departure: Enfield three two, roger./Departure: Enfield three two, Departure, cleared to switch./False")
              && up2.StartsWith(": Träger: Case-II-Abflug: „airborne“ melden, geradeaus parallel zum BRC 354 in Sicht bleiben, bei 7 DME auf den 10-DME-Bogen zum Abflugradial 024") && far3 == "/Departure: Enfield three three, Departure, cleared to switch./", $"Träger R24: Start vom Deck -> {dk1} | {up1} | {up3} | {up2} | {far3}");
        // R364: parked on the deck -> ship into the wind only up to 10 min after spawn or the last taxi move; after a trap none until airborne again
        var (l364, t364) = (new Carrier(), new Carrier { Stage = 3, boat = "CVN", cs = "Enfield three six" });
        var bg364 = new List<string>();
        foreach (var (tel, now) in new[] { (dkT, 100.0), (dkT, 701), (dkT with { X = dkT.X + 60 }, 702) }) { Tk(l364, "Enfield 3-5", tel, now); bg364.Add(l364.BoatGroup); }
        var trT = new Telemetry(DeckH + 2, 0, 10, 0, -50, 0, 0, 0, 0);
        bg364.Add(Tk(t364, "Enfield 3-6", trT, 800) + "/" + Tk(t364, "Enfield 3-6", trT, 801) + "/" + t364.BoatGroup + "/" + t364.OnDeck);
        check(string.Join("|", bg364) == "CVN||CVN|: Träger: gelandet auf CVN-73.///True", "Träger R364: Deck geparkt/rollend/nach Trap -> " + string.Join("|", bg364));
        // R26: wingman in formation takes over check-in (not in the stack, no own Delta) and Charlie of the lead, then flies himself (initial, LSO);
        // leaving formation in holding -> own slot in the stack; without check-in in the groove (gear down, on the FB) -> LSO ("call the ball"), gear up not
        stack.Clear();
        var (ld, wg, w2) = (new Carrier(), new Carrier(), new Carrier());
        S(ld, "Enfield 4-1", "Marshal, Enfield 4-1, checking in", hold, 2000);
        (wg.Lead, w2.Lead) = (ld, ld);
        var r26 = Tk(wg, "Enfield 4-2", hold, 2001) + "/" + Tk(w2, "Enfield 4-3", hold, 2001) + "/" + wg.Stage + (Stack("CVN").Contains(wg) ? "S" : "") + (wg.angels == ld.angels ? "A" : "");
        w2.Lead = null;
        r26 += "/" + Tk(w2, "Enfield 4-3", hold, 2002) + (Stack("CVN").IndexOf(w2) == Stack("CVN").IndexOf(ld) + 1 ? "S" : "");
        w2.Leave();
        r26 += "/" + Tk(wg, "Enfield 4-2", hold, 2190) + "/" + Tk(ld, "Enfield 4-1", hold, 2190).Split(" | ")[0] + "/" + Tk(wg, "Enfield 4-2", hold, 2191) + wg.Stage + "/" + Tk(wg, "Enfield 4-2", At(-3 * NM, 0, 800), 2300).Split(" | ")[0] + wg.Stage;
        ld.Leave(); wg.Leave();
        // Fix: wingman on the deck (lead circles above in holding) takes over nothing (otherwise "landed" every second);
        // taken over after Case III takeoff (Departure radar contact) -> Approach identifies anyway at "platform"
        var (fl5, fw5) = (new Carrier(), new Carrier());
        S(fl5, "Enfield 5-1", "Marshal, Enfield 5-1, checking in", hold, 2400);
        fw5.Lead = fl5;
        var dwk = Tk(fw5, "Enfield 5-2", dkT, 2401) + fw5.Stage + fw5.OnDeck;
        fw5.Lead = null; fl5.Leave(); fw5.Leave();
        Sky = (true, 200, 20000);
        Tk(fw5, "Enfield 5-2", dkT, 2500); Tk(fw5, "Enfield 5-2", upT, 2501); S(fw5, "Enfield 5-2", "Enfield 5-2, airborne", upT, 2502);
        S(fl5, "Enfield 5-1", "Marshal, Enfield 5-1, checking in", hold, 2503);
        fw5.Lead = fl5;
        Tk(fw5, "Enfield 5-2", hold, 2504);
        var dwp = fw5.Stage + S(fw5, "Enfield 5-2", "Enfield 5-2, platform", hold, 2505);
        Sky = (false, 0, 80000);
        fl5.Leave(); fw5.Leave();
        check(dwk == "0True" && dwp.StartsWith("1Approach: Enfield five two, Approach, radar contact"), $"Träger R26: Rottenflieger auf dem Deck -> {dwk} | nach Departure übernommen -> {dwp}");
        var gp = new Carrier();
        var gpu = Tk(gp, "Enfield 4-4", G(-0.8 * NM, 0, 70) with { Gear = 0 }, 3000) + gp.Stage + "/" + Tk(gp, "Enfield 4-4", G(-0.8 * NM, 0, 70) with { Gear = 1, Hdg = fb + 0.5 }, 3001) + gp.Stage;
        var gpd = Tk(gp, "Enfield 4-4", G(-0.8 * NM, 0, 70) with { Gear = 1 }, 3002) + gp.Stage + "/" + Tk(gp, "Enfield 4-4", G(-0.4 * NM, 0, 70) with { Gear = 1 }, 3008);
        gp.Leave();
        check(r26 == "//1A/S//Tower: Enfield four one, signal Charlie./2/: Träger: Initial: zip lip, kein Funk bis zum eigenen Ball-Call (ca. 3/4 NM: „Hornet ball, 5.2“).3"
              && gpu == "0/0" && gpd == "3/Paddles: Enfield four four, call the ball.", $"Träger R26: Rotte -> {r26} | Groove ohne Check-in -> {gpu} | {gpd}");
        // R279: Case III in formation: dash gets own check-in (own angels, EAT one minute behind the lead, in the stack), lead's "commencing" does not apply to him;
        // Case I in formation, then switch to Case III (R229): dash detaches and gets own assignment
        Sky = (true, 200, 20000);
        stack.Clear();
        var (l79, w79) = (new Carrier(), new Carrier());
        S(l79, "Enfield 7-1", "Marshal, Enfield 7-1, checking in", far, 6000);
        w79.Lead = l79;
        var m79 = Tk(w79, "Enfield 7-2", far, 6001);
        var r79 = (w79.angels - l79.angels, w79.charlieAt - l79.charlieAt, w79.follow, Stack("CVN").Contains(w79));
        S(l79, "Enfield 7-1", "Enfield 7-1, commencing", far, l79.charlieAt);
        var a79 = Tk(w79, "Enfield 7-2", far, l79.charlieAt + 1) + "/" + w79.Stage + "/" + l79.Stage;
        l79.Leave(); w79.Leave();
        Sky = (false, 0, 80000);
        var (l79b, w79b) = (new Carrier(), new Carrier());
        S(l79b, "Enfield 7-3", "Marshal, Enfield 7-3, checking in", hold, 6100);
        w79b.Lead = l79b;
        Tk(w79b, "Enfield 7-4", hold, 6101);
        bool f79 = w79b.follow;
        Sky = (true, 200, 20000);
        Tk(l79b, "Enfield 7-3", hold, 6110);
        var s79 = Tk(w79b, "Enfield 7-4", hold, 6111) + "/" + w79b.caseNo + (w79b.follow ? "F" : "") + (w79b.angels != l79b.angels ? "A" : "");
        Sky = (false, 0, 80000);
        l79b.Leave(); w79b.Leave(); stack.Clear();
        check(m79.StartsWith("Marshal: Enfield seven two, Marshal, case three recovery, CV-1 approach") && r79 == (1, 60, false, true) && a79 == "/1/2"
              && f79 && s79.StartsWith("Marshal: Enfield seven four, Marshal, case three recovery") && s79.EndsWith("/3A"),
              $"Träger R279: Case III Rotte einzeln -> {m79} | {r79} | {a79} | Case I->III {f79} {s79}");
        // R27: wire from the DCS event instead of estimated (3); grade waits up to 5 s after touchdown for it, without event the own estimate
        string Wire(int w, double late)
        {
            var c = new Carrier { Stage = 3, boat = "CVN", cs = "Enfield one one" };
            double tt = 100;
            for (double d = -NM; d < Aim + 20; d += 15, tt += 0.25) c.Sample(G(d, 0, 70), tt, 8.1, "FA-18C_hornet");
            WireEvents = true;
            var r = Tk(c, "Enfield 1-1", G(Aim, 0, 10), tt + 1);
            if (w > 0) c.OnWire(w);
            r += "/" + Tk(c, "Enfield 1-1", G(Aim, 0, 10), tt + late);
            WireEvents = false;
            return r;
        }
        var (wEv, wNo) = (Wire(2, 2), Wire(0, 6));
        check(wEv.StartsWith("/: LSO: ") && wEv.Contains(" 2-wire, ") && wNo.StartsWith("/: LSO: ") && wNo.Contains(" 3-wire, "), $"Träger R27: Wire aus DCS -> {wEv} | ohne Ereignis {wNo}");
        Boats = new(); Clock = -1; stack.Clear(); said.Clear(); deckDelta.Clear(); Alternate = altOld;
    }
}
