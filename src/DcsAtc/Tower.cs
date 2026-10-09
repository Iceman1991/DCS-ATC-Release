using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

/// Own aircraft (mission script or Export.lua). X = north, Z = east (DCS coordinates, metres), Hdg true in rad, Vs in m/s.
public record Telemetry(double AltMsl, double Agl, double Ias, double Hdg, double X, double Z,
                        double WindX, double WindZ, double Pressure, double Vs = 0, double Gear = -1,   // Gear: nose gear 0 up – 1 down, -1 unknown
                        (double X, double Z, double AltMsl)? Lock = null);   // target locked in the cockpit (Export.lua, host/solo only)

/// Other aircraft nearby (AI/players). Speed, Vs (climb/descent rate) in m/s. Flag: AI control (dep/dep-go/arr/arr-hold).
public record Traffic(int Id, string Type, double X, double Z, double AltMsl, double Hdg, double Speed,
                      string Group = "", string Flag = "", bool InAir = true, int Coalition = 0, double Vs = 0, string Cs = "", double Fuel = 1, bool Heli = false,   // Heli: helicopter (mission, N50)
                      double AsgFt = 0);   // R216: altitude assigned by ATC (holding/radar vectoring/IFR departure), 0 = none (VFR/unknown)

/// Radio call of a controller. Role: Ground / Tower / Approach / ATIS, "Info" = text only in the game (not spoken).
public record Msg(string Role, string Text)
{
    public static implicit operator string(Msg m) => m.Text;
    public bool Contains(string s) => Text.Contains(s);
    public bool StartsWith(string s) => Text.StartsWith(s);
    public override string ToString() => Text;
}

public enum Phase
{
    Parked, StartupApproved, TaxiOut, HoldShort, ClearedTakeoff, Departing, Away,
    Inbound, Entering, Initial, Pattern, ClearedLand, TaxiIn,
}

/// Rule-based tower for Kutaisi (UGKO) per the DCS kneeboard "ARR/DEP JET RWY 07/25" and the aerodrome chart.
/// Pure logic: text/telemetry in, speakable radio calls out.
public partial class Tower
{
    // ======================================================================= Airfield
    const double NM = 1852, Kt = 0.514444, Ft = 0.3048;
    public const double InitialDist = 4000;                         // Initial point before the threshold (Kutaisi chart ~2 NM)
    const double InitialLat = 0.5 * NM;                       // "initial" only up to this lateral distance from the runway centerline (call and tick)
    public readonly Airfield F;
    /// Controller frequencies (MHz AM), same for all airfields. Program sets them from config.jsonc.
    public static Dictionary<string, double> Freqs = new() { ["Ground"] = 264.5, ["Tower"] = 265.0, ["Approach"] = 266.5, ["ATIS"] = 263.5 };
    string role = "Tower";                                    // who is currently replying
    string Station => F.StationOf(Dep(role));
    /// Forum request: after the handoff on departure, Approach speaks as Departure if the airfield has its own departure frequency (AirfieldFrequencies); otherwise Approach as before.
    internal string Dep(string r) => r == "Approach" && handedOff && Phase is Phase.Departing or Phase.Away && lastIntent is not ("inbound" or "initial" or "overhead" or "straightin" or "crp")
                                     && F.Own.ContainsKey("Departure") ? "Departure" : r;   // back to landing (call and then inbound): Approach again
    double FieldElev => F.Elev;
    double MagVar => F.MagVar;
    double PatternFt => F.PatternFt;
    double MaxFt => F.MaxFt;
    double CX => F.X;
    double CZ => F.Z;
    (double X, double Z) Ovh => (F.End(Runway).CX, F.End(Runway).CZ);   // Overhead: runway midpoint (the DCS airfield point is often beside the runway)
    Dictionary<string, (double X, double Z)> Crp => F.Crp;
    double CtrM => F.CtrNm * NM;   // Control zone radius (m, A100)

    // ======================================================================= State
    public Phase Phase { get => phase; private set { if (value != phase) lastAltFt = -1; phase = value; } }   // R130: new phase = new altitude announcements
    Phase phase = Phase.Parked;
    public string Runway { get; private set; }
    string callsign;
    Msg? last;
    string lastIntent = "";
    double lastAt = -999, callAt;   // lastAt: time of the last controller call (readback window), callAt: time of the current call (Tick/OnTranscript)
    string? exitDir, entry;
    public static IReadOnlyList<Airfield> All = new List<Airfield>();   // all airfields (Program): destination of the en-route clearance
    Airfield? dest; bool depClr; double depFt;   // V14: route clearance (Clearance Delivery) -> departure vector
    public string? Squawk;   // A90: transponder code, assigned once per flight (en-route clearance, VFR taxi clearance, flight following), moves along on airfield change (Program)
    double cruiseFt;         // A102: cruise altitude of the en-route clearance (semicircular rule), 0 = not yet calculated
    Ramp? home;
    (double X, double Z)? spot;          // own parking position (noted at spawn on the ground, kept for the flight), destination when taxiing in
    int parkNum;                         // assigned parking position (DCS Term_Index), 0 = no number
    bool taxiInTold;
    double expFor = -1; string landRw = "";   // A48/A83: landedAt for which "expedite vacating" was already given (once per landing); runway
    double vacRemFor = -1, vacatedAt = -1000, taxiDevFor = -1;   // #13: landedAt for which "not cleared to taxi" was already given; A91: landedAt for which the "contact Ground" reminder was already given (once per landing); first tick off the runway after landing
    double lastParkCall = -999;
    bool progressive, progNear;          // N26: on request Ground guides with direction/distance to the holding point or parking position; progNear: final announcement spoken
    double progLast, progMin, growSince; // distance at the last announcement, smallest since then, since when it has been growing (A36: deviation)
    bool airborne, rwyBusyTold, handedOff;   // handedOff: handed over to Approach (Departure) after takeoff
    double handedAt = -1;                    // when (tower handoff in Tick), -1 = unknown (FF check-in, restored session)
    bool zoneReport;                         // R7: VFR departure identified in the zone ("report leaving the control zone"), sign-off at the zone boundary
    double missAt = -1, missFt;              // R44: missed approach from the tower (runway heading, altitude), vectors only at check-in with Approach; -1 = none open
    bool missDep;                            // R253: the open "missed approach" is the departure to the radar pattern (check-in "airborne, climbing")
    double popM;                            // R113: handoff distance after pop-up IFR (smallest distance since then + 5 NM, at least 15 NM), 0 = 15 NM
    Telemetry? lastTel;
    double lastSeen = -999, lastTaxiWarn = -999, lastAltWarn = -999, landedAt = -999, stoppedSince = -1, holdSince, touchAt = -999, rollBack = -1;   // rollBack: since when away from the holding point (A12)
    // Navigation / coordination
    (double X, double Z)? nav;          // target for headings (CRP, initial, airfield, final, departure CRP)
    string navName = "";
    double handoffAt = -999, lastVector = -999, lastGuide = -999, lastLow = -999, lastIncursion = -999, lastAbort = -999, lastPatternWarn = -999, lastGa = -999;   // lastGa: go-around from the tower (A11)
    bool baseCalled, extended, wrongRwyTold, lineUp, gearTold, finalLowTold, rejecting;   // rejecting: rejected takeoff (R100) above 40 kt, the deceleration is not a takeoff roll
    bool needWx;                        // R126: initial rejection was the first contact, wind and QNH only come with the break clearance
    public bool LineUp => lineUp;       // "line up and wait" (Program: AI waits, A9/A10)
    public int RunwayClaimed;           // A9: others with takeoff clearance or LUAW before the takeoff roll (player) or AI with takeoff clearance on the ground (Program)
    // Traffic patterns, emergency, holding, landing assessment
    string? option;                     // "touch and go" / "low approach" / "full stop"
    bool stayPattern, holding, wasAir, graded;   // graded: landing graded, free again only above 30 m (bounce = one landing)
    public int Laps { get; private set; }
    public bool Emergency { get; private set; }
    public bool OtherEmergency;         // MAYDAY of another player in the air at the same airfield (Program)
    public double EmergencyNm;          // its distance to the airfield (NM, Program), 0 = unknown (counts as near)
    bool mayday;                        // own emergency is MAYDAY (PAN: only priority in the sequence, others do not hold)
    public bool Mayday => Emergency && mayday && airborne;   // others hold, runway clear (Program: OtherEmergency, AI waits)
    bool EmgWithin(double nm) => OtherEmergency && EmergencyNm < nm;
    string EmgNote() => OtherEmergency ? $", expedite vacating, emergency traffic {Miles(EmergencyNm * NM)} miles" : "";
    public int QueueAhead = -1;         // aircraft ahead of me in the airfield's landing sequence (Program), -1 = unknown
    public bool Spaced = true;          // preceding aircraft is out of the holding and near the airfield or separated (Program) -> may approach
    public double AheadR = -1;          // R46: remaining distance (m) of the preceding aircraft to the threshold (Program, FinalPath), -1 = none/unknown
    public bool AheadHeavy;             // preceding aircraft is Heavy: 5 NM instead of 3 NM
    /// R46: remaining distance (m) to the threshold for separation on final: under radar vectoring to final or on final up to 10 NM, otherwise -1 (visual/overhead: only in the break as before).
    public double FinalPath(Telemetry t) => Phase == Phase.Inbound && vec != null && vecFinal ? Path(t).R
        : Phase == Phase.Entering && navName == "six mile final" && AL(t.X, t.Z, Runway) is var (fa, fl) && fa > 0 ? fa + Math.Abs(fl)   // handed over (Established at gate 10 NM: still outside or turning in), straight-in approach
        : Phase is Phase.Entering or Phase.Pattern or Phase.ClearedLand && OnFinalWithin(t, 10 * NM) ? AL(t.X, t.Z, Runway).A : -1;
    /// Flight time (s) over the remaining distance r (m) per the speed profile of radar vectoring (VecKt, jets), the last 5 NM at 150 kt approach speed.
    static double Secs(double r)
    {
        double s = 0, lo = 0;
        foreach (var (to, kt) in new[] { (5 * NM, 150), (10 * NM, 200), (20 * NM, 250), (double.MaxValue, 300) }) { s += Math.Max(0, Math.Min(r, to) - lo) / (kt * Kt); lo = to; }
        return s;
    }
    /// R46: separated behind the preceding aircraft (remaining distance ahead): at the threshold at least 3 NM, behind Heavy 5 NM (ICAO Doc 4444 8.7.3, FAA JO 7110.65 5-5-4), plus marginS seconds.
    public static bool Behind(double mine, double ahead, bool heavy, double marginS = 0) => Secs(mine) - Secs(ahead) >= (heavy ? 5 : 3) * NM / (150 * Kt) + marginS;
    /// Too close behind the preceding aircraft (less than separation + 30 s): slower, longer base.
    bool Tight(Telemetry t) => vecFinal && AheadR >= 0 && !Behind(Path(t).R, AheadR, AheadHeavy, 30);
    double holdFt;                      // assigned altitude in the holding
    double holdFree = -1;               // since when it has been pending (clearance waits for a suitable heading in the circle)
    /// Holding stack: position 3 of the sequence is the lowest level, each position 1000 ft higher.
    public static double StackFt(Airfield f, int pos) => Math.Ceiling(f.MaxFt / 1000) * 1000 + Math.Max(0, pos - 2) * 1000;
    /// Altitude in the holding: stack by sequence, lowest level above the terrain from the holding fix to the airfield and on the approach path from there (R272, do not climb when leaving).
    /// Then the next free level across all airfields (FreeLevel, Program): no two holding aircraft at the same altitude close to each other.
    double HoldFtAt((double X, double Z)? fix)
    {
        double ft = Math.Max(StackFt(F, 2), fix is { } p ? Math.Ceiling(Math.Max(F.MvaLeg(p.X, p.Z, CX, CZ), PlanMva(p)) / 1000) * 1000 : 0) + Math.Max(0, QueueAhead - 2) * 1000;
        return fix is { } q ? FreeLevel(Key, q.X, q.Z, ft) : ft;
    }
    (double X, double Z, string Rw, double Ft) planMva = (double.NaN, 0, "", 0);
    /// R272: minimum altitude on the approach path chosen by the plan from the holding fix (as when leaving the holding; Gudauta: holding 3000 over the sea,
    /// every route to 33 above 6200 ft MVA -> "leave the hold, climb and maintain 6200"), calculated once per holding fix and runway.
    double PlanMva((double X, double Z) p)
    {
        string key = Runway + (Ifr || wantStraight);   // runway, instrument/visual
        if (planMva.X == p.X && planMva.Z == p.Z && planMva.Rw == key) return planMva.Ft;
        double ft = Math.Max(StackFt(F, 2), Math.Ceiling(F.MvaLeg(p.X, p.Z, CX, CZ) / 1000) * 1000), m = 0;   // plan altitude as when leaving: climbing because of terrain costs (Cost)
        // exit is possible anywhere in the circle (up to ~3.5 NM from the holding fix, which still moves up to 1.5 NM on arrival; Gudauta/Senaki: from there already over the mountains): plan from the center and eight points 4 NM around
        foreach (var k in Enumerable.Range(-1, 9))
        {
            var q = k < 0 ? p : (X: p.X + 4 * NM * Math.Cos(k * Math.PI / 4), Z: p.Z + 4 * NM * Math.Sin(k * Math.PI / 4));
            var (rw, g, _, v) = Plan(new Telemetry(ft * Ft, 0, HoldKt * Kt, Bearing(q.X, q.Z, CX, CZ) * Math.PI / 180, q.X, q.Z, 0, 0, 0), Ifr || wantStraight);
            m = Math.Max(m, LegsMva(q, v, rw, g));
        }
        planMva = (p.X, p.Z, key, m);
        return planMva.Ft;
    }
    public string Key = "";              // Program: "p:<Unit>" (holding occupancy)
    public static Func<string, double, double, double, double> FreeLevel = (k, x, z, ft) => ft;
    public (double X, double Z, double Ft)? HoldInfo => holding && nav is { } n ? (n.X, n.Z, holdFt) : null;
    public bool HoldArrived => holding && holdArrived;   // R270: circling at the holding fix (Program.TrafficFor: only then "hold:", on the way there "twr:")
    double HoldFt => HoldFtAt(nav);
    /// R270: altitude of a holding: the assigned one (Traffic.AsgFt), not the current one (currently descending one level lower), otherwise rounded to 500 ft.
    static double HoldLvl(Traffic a) => a.AsgFt > 0 ? a.AsgFt : Math.Round(a.AltMsl / Ft / 500) * 500;
    /// Altitude on the way to the holding fix: another holding on the way (5 NM) below -> stay above it (do not descend through its altitude), in the circle then step down.
    double RouteFt(Telemetry t, IReadOnlyList<Traffic> traffic, (double X, double Z) fix, double ft)
    {
        foreach (var hl in traffic.Where(a => a.InAir && a.Flag.Contains("hold") && HoldLvl(a) < IndFt(t) - 500 && SegDist(a.X, a.Z, (t.X, t.Z), fix) < 5 * NM).Select(HoldLvl).Order())
            if (ft < hl + 1000) ft = FreeLevel(Key, fix.X, fix.Z, hl + 1000);
        return ft;
    }
    /// R273: wait time (min) until approach clearance, never before arrival at the holding fix (fixS: flight time there); 5 min per position in the sequence
    /// (mptest: 4–6 min per approach from the holding), without a sequence after the traffic pattern.
    int Expect(int busy, double fixS = 0) => Math.Max((int)Math.Ceiling(fixS / 60), OtherEmergency ? 5 : QueueAhead >= 0 ? Math.Max(1, QueueAhead * 5 - 1) : busy * 2 + 2);
    double lastEat = -1;   // last announced expected approach time (sim time s), -1 = not yet announced in the holding (case inbound resets)
    int lastMin = -1;      // Expect() without flight time on the last call: unchanged -> announced EAT is fixed (the estimate per position does not count down)
    /// Expected approach time (EAT) as per ICAO: once as mission clock minute ("expected approach time three five"), afterwards only on change
    /// by at least 5 min "revised expected approach time ...", otherwise "" (radio silence). With an unchanged situation the EAT stays fixed until reached;
    /// R273: then a new one right away (not only 5 min later with the same number). Without clock (Carrier.Clock < 0) "expect approach in N minutes".
    /// Sets lastEat, so only call when the result is also spoken. pre is only prepended to a non-empty result. fixS: flight time to the holding fix (s).
    string EatNote(int busy, double now, string pre = "", double fixS = 0)
    {
        int q = Expect(busy), min = Expect(busy, fixS);
        bool first = lastEat < 0, same = q == lastMin;
        lastMin = q;
        if (!first && now < lastEat && (same || Math.Abs(now + min * 60 - lastEat) < 300)) return "";
        lastEat = now + min * 60;
        return pre + (Carrier.Clock >= 0 ? $"{(first ? "" : "revised ")}expected approach time {Digits((((int)((Carrier.Clock + min * 60) / 60)) % 60).ToString("00"))}"
                                         : $"{(first ? "" : "revised, ")}expect approach in {min} minute{(min == 1 ? "" : "s")}");
    }
    public string AtisLetter = "";      // current ATIS identifier of the airfield (Program), "" = no ATIS
    public static bool Ifr;             // visibility < 5 km or low clouds (Program, from the mission) -> ILS with radar vectoring
    public static bool Watch = true;    // Config.AirspaceWatch (Program): announce violations and put them in the debriefing (N4)
    public int Rejected { get; private set; }   // rejected radio calls (debriefing)
    int? holdFor;                       // taxi traffic: stopped because of this traffic (Id), waits for "continue taxi" (R106)
    double holdAt, holdDist;            // ... since when and last distance to it (distance must grow)
    public List<(double X, double Z)> Reserved = new();   // assigned, not yet reached parking spots of other players at the airfield (Program.Prep, R108)
    internal bool TaxiInTold => taxiInTold;
    internal (double X, double Z)? Spot => spot;
    int? taxiFollow;                    // taxi traffic: "follow the X ahead" said (its Id); no stop, so no "continue taxi" either
    double lastRollWarn = -999;         // taxiing without clearance: last announcement
    readonly List<(double Dev, double Lat)> glide = new();
    double lastVs;

    public Tower(string defaultCallsign) : this(Airfield.Kutaisi(), defaultCallsign) { }

    public Tower(Airfield field, string defaultCallsign)
    {
        F = field;
        callsign = defaultCallsign;
        Runway = F.Ends.Any(e => e.Name == "25") ? "25" : F.Ends[0].Name;
        home = F.Ramps.FirstOrDefault();
    }

    public void SetCallsign(string cs) => callsign = cs;
    public string Callsign => callsign;
    public bool CallsignFixed;   // callsign from the mission or the settings (Program): do not adopt from the radio call
    string? heardCs;             // without a fixed callsign: last heard other callsign, adopted the second time

    /// Flight (players of the same group in formation): clearances apply to the whole flight.
    public int FlightSize = 1;
    public int Side;   // N1: player's side (Program.Prep), en-route clearance only to own/neutral destinations; 0 = all
    public double HeardOn;   // K2: airfield frequency on which the pilot calls or which he has tuned (Program.SpokenFreq), 0 = unknown
    public string? AwacsContact;   // "Overlord two five one decimal zero": own AWACS/GCI of the mission, handoff after departure (Program), null = none
    public bool Following;   // V15: flight following (radar service en route), moves along on airfield change (Program)
    public bool Radar => handedOff || depClr || Following || transitFt != null;   // R47: Departure, Flight Following or transit (N3) still looks after the flight (Program.Pick keeps the airfield)
    double? transitFt; bool transitIn; string transitDir = "", transitBy = "Approach";   // N3: transit cleared (minimum altitude) / already in the zone / direction / releasing controller
    public readonly HashSet<string> TransitTold = new();   // N3: players in the pattern who have been informed of the transit (Program)
    public string? TransitInfo => transitFt is { } f ? $"crossing overhead {transitDir}bound, {Alt(f)}" : null;   // N3: transit in progress (Program: traffic info, guard silent)
    /// N3/A47: transit altitude or climb altitude after the low pass: 1000 ft above the pattern and above the terrain of the zone, rounded up to 500 ft.
    double TransitFt => Math.Ceiling(Math.Max(PatternFt + 1000, F.TerrainFt(CX, CZ, F.CtrNm) + 1000) / 500) * 500;
    public bool Released;    // R47: signed off ("frequency change approved", handoff): Program.Pick releases the pin
    public bool Expected;    // N40: flight diverted here, first call as after a handoff (A27)
    /// R250: MAYDAY/PAN at AWACS: GCI reports the emergency by landline, Approach keeps priority ready and only answers the first call on its frequency.
    public void Announce(Telemetry? t, double now, bool may) { if (t != null) Seen(t, now); Emergency = true; mayday = may; }
    public bool Exempt, Tactical;   // N2 (Program.Prep): guard silent (controller contact under 5 min, Flight Following, other procedure running, helicopter; per DCS group) / checked in with AWACS (pattern and runway only)
    public double HostileNm = 999, Aloft;   // N2: nearest enemy in the air (NM from the airfield) / s since takeoff or air start (0 = unknown: silent)
    public bool Unknown;     // N51 (Program.Prep): group has not yet spoken to any controller in this flight: guard calls "unidentified aircraft" with position instead of callsign
    public bool WatchSaid;   // N2: the last tick call came from the guard (Program: no controller contact, not tuned, on Guard)
    int intr; double intrAt = -999, crossAt = -999;   // N2: stage on entry (0 armed, 1 called, 2 sent away, 3 violation), time of stage 1 / last runway overflight
    const double LocalM = 5000;   // A39: radius of airfield Ground (ground radio, taxi warning A40, takeoff without clearance; farthest parking position in the Caucasus 4.2 km)
    double minDist = -1, leaveAt = -1; Phase minPh;   // A13: smallest distance since phase start, distance at "say intentions" (-1 = not said)
    bool altAsk;             // N40: IFR destination captured, "say alternate destination" asked in the air
    /// N24: reported traffic per aircraft: time of the advisory, state (0 reported/looking, 1 in sight, 2 no contact), completion/update already said
    sealed class Advice { public double At; public int St; public bool Done; }
    readonly Dictionary<int, Advice> advised = new();
    double followAt = -1e9;   // N24: last FollowTick; after a pause (service ended, landing, airfield change) old advisories no longer apply
    bool lowTold, lowAck;   // A106: terrain warning once per flight, not at all after "terrain in sight"
    public bool Separate;   // V17: separate landing / radar trail -> each pilot with their own procedure
    string Cs() => SpokenCallsign(callsign) + (FlightSize > 1 ? " flight" : "");

    /// Save session (app crash/restart): procedure state; timestamps and warnings start anew.
    public record State(Phase Phase, string Runway, string? Option, bool StayPattern, bool Holding, double HoldFt, double[]? Nav, string NavName,
                        bool Emergency, bool Following, bool Separate, bool DepClr, string? Dest, double DepFt, int ParkNum, double[]? Spot,
                        bool Airborne, bool HandedOff, string? ExitDir, string? Entry, int Laps, bool WasAir, bool Vectoring = false, bool Straight = false, bool VecFinal = false,
                        string? Squawk = null, bool Visual = false);
    public State Save() => new(Phase, Runway, option, stayPattern, holding, holdFt, nav is { } n ? new[] { n.X, n.Z } : null, navName,
                               Emergency, Following, Separate, depClr, dest?.Name, depFt, parkNum, spot is { } s ? new[] { s.X, s.Z } : null,
                               airborne, handedOff, exitDir, entry, Laps, wasAir, Vectoring, wantStraight, vecFinal, Squawk, wantVisual);
    public bool Vectoring => vec != null;   // radar vectoring running
    public void Load(State s, Telemetry t, double now)
    {
        (Phase, Runway, option, stayPattern, holding, holdFt, navName) = (s.Phase, F.Ends.Any(e => e.Name == s.Runway) ? s.Runway : Runway, s.Option, s.StayPattern, s.Holding, s.HoldFt, s.NavName);   // runway from old data (duplicate removed): default runway instead of a crash in End()
        nav = s.Nav is [var x, var z] ? (x, z) : null;
        spot = s.Spot is [var sx, var sz] ? (sx, sz) : null;
        (Emergency, Following, Separate, depClr, depFt, parkNum) = (s.Emergency, s.Following, s.Separate, s.DepClr, s.DepFt, s.ParkNum);
        mayday = Emergency;   // type not saved: treated like MAYDAY after the restart
        dest = All.FirstOrDefault(f => f.Name == s.Dest);
        Squawk = s.Squawk;
        (airborne, handedOff, exitDir, entry, Laps, wasAir) = (s.Airborne, s.HandedOff, s.ExitDir, s.Entry, s.Laps, s.WasAir);
        (lastTel, lastSeen, wantStraight, wantVisual) = (t, now, s.Straight, s.Visual);   // otherwise the next tick resets everything (gap > 10 s)
        if (s.Vectoring && Phase == Phase.Inbound && !holding) StartVectors(t, s.VecFinal, now);   // re-plan radar vectoring, continue with headings from here
    }
}
