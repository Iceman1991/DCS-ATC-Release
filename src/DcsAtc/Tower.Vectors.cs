using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= Radar vectoring (like Falcon BMS)
    // Approach vectors with headings via downwind and base (turn-in point 3 NM lateral) onto the extended
    // centreline, turning in at 30°, and releases the descent in steps (~300 ft/NM down to the threshold).
    // Goal: initial (visual) or final (straight in / IFR). Aligned -> handover to the tower.
    List<(double X, double Z)>? vec;     // Waypoints up to the turn-in point, empty = turning in
    static readonly string[] FieldSight = { "field in sight", "airfield in sight", "airport in sight", "runway in sight", "have the field", "have the airport", "have the runway", "field not in sight", "airport not in sight", "runway not in sight" };
    bool visAsk, visSeen;   // R115: visual approach waits for "field in sight" / airfield is reported
    bool vecFinal, wantStraight, wantVisual, optedOut;   // wantStraight: instrument approach wanted (also after holding), wantVisual: of which visual approach (P3-AP7)
    bool apprClr;   // R353: approach clearance already given with the turn-in vector (handover only)
    double vecFt, lastVecSaid = -999, vecHdg, prevHdg, prevIas, lastSeqSaid = -999, lastHoldInfo = -999;
    int lastAhead = -1;
    bool holdArrived;

    double gate = InitialDist + 4 * NM;   // aligned there at the latest (plan)
    string vrw = "";                      // Centreline of radar vectoring: active runway or reciprocal (overhead/circling)
    public string ApproachRwy => Phase is Phase.Inbound or Phase.Entering && vrw != "" ? vrw : Runway;   // Approach via this centreline (Program: AI takeoff/approach against it?)
    bool vecOver;                         // Overhead via the reciprocal, then pattern (terrain ahead of the active runway)
    bool Circling => vecOver && (Ifr || wantStraight);   // R212: instead of overhead, instrument approach to the reciprocal runway and circling
    /// Review R212: circling only with an instrument procedure (ILS/PAR); without a procedure or visual approach (VMC) there is no "circle to", but the downwind of the active runway
    /// (FAA JO 7110.65 3-10-? "join/enter … downwind"; no contradictory "cleared straight in approach runway 13, circle to runway 31").
    bool CircleIfr => Proc(vrw) != "" && !wantVisual;
    /// R217: practice approach (low approach, touch and go, option): climb-out with the approach clearance (FAA JO 7110.65 4-8-12), afterwards the tower only gives the frequency.
    double goFt;   // stated climb-out altitude, 0 = none
    string OnTheGo(string rw)
    {
        goFt = option is "low approach" or "touch and go" or "the option" ? ClimbFt(rw) : 0;
        return goFt > 0 ? $", on the go fly runway heading, climb and maintain {Alt(goFt)}" : "";
    }
    double HandSign(string rw) => Hand(rw) == "left hand" ? 1 : -1;

    /// Position relative to runway: A = distance before threshold (approach side > 0), L = lateral (sign as Pt).
    (double A, double L) AL(double x, double z, string rw)
    {
        var e = F.End(rw);
        double dx = x - e.ThrX, dz = z - e.ThrZ;
        return (-(dx * e.Dx + dz * e.Dz), dx * e.Dz - dz * e.Dx);
    }
    (double X, double Z) Pt(string rw, double a, double l) => F.End(rw).At(a, l);

    /// "Expect vectors ..." for the plan; was: active runway before the plan.
    string PlanText(string was) =>
        (vecOver || Runway != was ? "Due to terrain, expect " : "Expect ") +
        (vecOver ? (Ifr || wantStraight ? (CircleIfr ? $"vectors for {ProcSay(vrw)}, circle to runway {RwSay(Runway)}. " : $"vectors for {Hand(Runway)} downwind runway {RwSay(Runway)} via the extended centerline runway {RwSay(vrw)}. ")
                                        : $"vectors for overhead join runway {RwSay(Runway)} via the extended centerline runway {RwSay(vrw)}. ")
                 : vecFinal ? $"vectors for {(wantVisual ? $"visual approach runway {RwSay(Runway)}" : ProcSay(Runway))}. " : $"vectors to initial runway {RwSay(Runway)} for overhead break. ");

    /// K9: instrument approach as on the charts (minima only for ILS/PAR/SRA, no TACAN or VOR procedure): ILS of the runway (Beacons.lua),
    /// otherwise with IFR precision radar (PAR, final callouts from the controller); "" = in visual conditions without ILS "straight in approach". TACAN/VOR only navigation aid.
    string Proc(string rw) => F.Ils.ContainsKey(rw) ? "ILS" : Ifr ? "P A R" : "";
    /// "ILS approach runway one three, localizer one one zero decimal three", "P A R approach runway two five", otherwise "straight in approach runway one three".
    string ProcSay(string rw) => F.Ils.TryGetValue(rw, out var i) ? $"ILS approach runway {RwSay(rw)}, localizer {FreqSay(i.Mhz)}"
        : $"{(Proc(rw) is var p && p != "" ? p : "straight in")} approach runway {RwSay(rw)}";

    /// R352: replanning under radar; other runway or route than before: "due to terrain, expect …" before the vector (FAA JO 7110.65 4-8-1, 5-9-2)
    string Replan(Telemetry t, double now, string? why = null)
    {
        var (was, wasV) = (Runway, vrw);
        var sv = StartVectors(t, Ifr || wantStraight, now, why: why);
        if (Runway == was && vrw == wasV) return sv;
        var pt = PlanText(was);
        return char.ToLower(pt[0]) + pt[1..] + Cap(sv);
    }
    /// Plan waypoints and give first heading.
    string StartVectors(Telemetry t, bool final, double now, double minFt = 0, string? why = null)   // minFt: missed approach altitude (R44), not below it; why: reason for replanning (R222)
    {
        // current altitude, rounded = first "maintain" altitude (missed approach: at least pattern altitude); replanned: not above the one already cleared (no 3300 -> 3600)
        vecFt = Math.Max(Math.Min(Math.Round((holding ? holdFt : IndFt(t)) / 500) * 500, vec != null ? vecFt : double.MaxValue), Math.Max(PatternFt, minFt));
        nav = null; vecWhy = why;
        bool fresh = vec == null;
        if (fresh) { pdDescent = false; keepHighR = 0; }   // R303: new approach: the previous one's request no longer applies
        (vrw, gate, vecOver, vec) = Plan(t, final);
        vecFinal = final && !vecOver;   // Circling as overhead
        if (fresh) altFree = !holding && minFt == 0 && !Emergency && !Ifr && (!vecFinal || Proc(vrw) == "" || wantVisual) && vecFt == Math.Round(IndFt(t) / 500) * 500 && PathMva(t) <= vecFt && FreeNm(t) >= 10;   // R394: VFR far out (descent 10 NM or more away): altitude at his discretion; IFR (IMC or instrument approach) always an assigned altitude
        while (vec.Count > 0 && Dist(t.X, t.Z, vec[0].X, vec[0].Z) < Lead(t).Lim) vec.RemoveAt(0);   // R351: first waypoint already within the turn lead: heading for the next leg now, not "fly heading …" and 1 s later "turn right …"
        if (!vecOver) Runway = vrw;     // on the reciprocal runway (terrain, plan): applies to this approach (Program holds the AI)
        // Traffic close by (e.g. holding of a neighbouring airfield): immediately the next altitude with 1000 ft separation and name the traffic, not "traffic alert" 2 s later
        bool Clear(double ft) => !near.Any(a => a.InAir && Dist(t.X, t.Z, a.X, a.Z) < 3 * NM && Math.Abs(a.AltMsl / Ft - ft) < 1000);
        if (near.Where(a => a.InAir && Dist(t.X, t.Z, a.X, a.Z) < 3 * NM && Math.Abs(a.AltMsl / Ft - vecFt) < 1000).MinBy(a => Dist(t.X, t.Z, a.X, a.Z)) is not { } a) return VecCall(t, now);
        double floor = Math.Max(PathMva(t), PatternFt);
        altFree = false;
        vecFt = Enumerable.Range(1, 8).SelectMany(k => new[] { vecFt + 500 * k, vecFt - 500 * k }).FirstOrDefault(f => f >= floor && Clear(f), vecFt);
        foreach (var c in near.Where(c => Dist(t.X, t.Z, c.X, c.Z) < 3 * NM)) alerted[c.Id] = now;
        if (now - followAt > 60) advised.Clear();   // R216: named = traffic advisory (TrafficInfo not again right away)
        (advised[a.Id], followAt) = (new Advice { At = now }, now);
        int clock = (int)Math.Round(((Bearing(t.X, t.Z, a.X, a.Z) - t.Hdg * 180 / Math.PI) % 360 + 360) % 360 / 30) % 12;
        double dAlt = (a.AltMsl - t.AltMsl) / Ft;
        return VecCall(t, now) + $" Traffic {(clock == 0 ? 12 : clock)} o'clock, {MilesTxt(Dist(t.X, t.Z, a.X, a.Z))}, " +
               $"{(Math.Abs(dAlt) < 300 ? "same altitude" : $"{Math.Round(Math.Abs(dAlt) / 100) * 100:0} feet {(dAlt > 0 ? "above" : "below")}")}, {Ops.TypeSay(a.Type)}.";
    }

    /// Approach plan by terrain. In order: active runway (IFR gate 10/8 NM, 6/4 NM only with terrain or much shorter path), the reciprocal runway with at most 10 kt tailwind
    /// (as in reality Batumi: jets land only on 13 from the sea, not over the mountains on 31; Sochi 06, Gelendzhik 01),
    /// finally overhead or circling via the reciprocal; per plan both sides, base 6 or 3 NM behind the gate.
    /// Feasible: the 2.5° path is nowhere below minimum altitude/terrain. Of the feasible ones in the first group the shortest (Cost),
    /// otherwise the one with the least terrain above it.
    (string Rw, double Gate, bool Over, List<(double X, double Z)> Vec) Plan(Telemetry t, bool final)
    {
        var rw = Runway;
        var opp = Airfield.Opposite(rw);
        var groups = new List<(string R, bool Over)> { (rw, false) };
        if (TailKt(t, opp) <= 10) groups.Add((opp, false));
        groups.Add((opp, true));
        // Downwind so far lateral that after the 90° turn onto base there is still room before turning in (turn radius at target speed)
        double kt = final ? 250 : 300, tr = Sq(kt * Kt * 1.06) / (9.81 * Math.Tan(Math.PI / 6)), w = 3.5 * NM + tr;
        (string, double, bool, List<(double X, double Z)>) best = default;
        double bestEx = double.MaxValue;
        foreach (var (r, over) in groups)
        {
            var (a, l) = AL(t.X, t.Z, r);
            double s0 = Math.Abs(l) > 0.5 * NM ? Math.Sign(l) : HandSign(rw) * (r == rw ? 1 : -1), bestCost = double.MaxValue;
            (string, double, bool, List<(double X, double Z)>)? pick = null;
            foreach (var g in final && !over ? new[] { 10 * NM, 8 * NM, 6 * NM, 4 * NM } : new[] { InitialDist + 4 * NM })
            {
                double gmin = final && !over ? g : Math.Min(g, InitialDist + NM);
                var cands = (from s in new[] { s0, -s0 } from e in AheadR >= 0 ? new[] { 6 * NM, 3 * NM, 10 * NM } : new[] { 6 * NM, 3 * NM }   // R46: behind the preceding traffic also a longer downwind
                             from diag in new[] { false, true } select Legs(t.X, t.Z, r, g, s, e, w, tr, diag)).ToList();
                if (a - Math.Abs(l) * 1.73 > gmin) cands.Add(new());   // already outside on the line (e.g. from holding): turn in before initial
                foreach (var v in cands)
                {
                    double ex = Excess(t, r, g, v, final && !over), cost = ex <= 0 ? Cost(t, r, g, gmin, v, final && !over) : double.MaxValue;
                    if (ex < bestEx) { bestEx = ex; best = (r, g, over, v); }
                    if (cost < bestCost) { bestCost = cost; pick = (r, g, over, v); }
                }
            }
            if (pick is { } p) return p;
        }
        return best;
    }

    /// Waypoints up to turn-in: downwind w lateral (s = 1 left of the approach line), e behind the gate 90° onto base,
    /// 1.5 NM before the centreline 60° onto the turn-in heading (30°). Entering downwind from the side: it is then at least two turn radii (tr) + 2 NM long (rollout);
    /// on the wrong side already farther out: cross there, then parallel in – no U-turn back across the centreline.
    /// If he reaches the centreline with 30° before the gate: turn in right away.
    List<(double X, double Z)> Legs(double x, double z, string rw, double g, double s, double e, double w, double tr, bool diag)
    {
        var (a, l) = AL(x, z, rw);
        double b = g + e;
        var v = new List<(double X, double Z)>();
        if (a - Math.Abs(l) * 1.73 > g) return v;
        bool cross = l * s < -0.5 * NM;
        if (a < b - NM || !cross && Math.Abs(l) < w - 1.5 * NM)   // inside: across out into downwind (diag and already farther lateral: diagonally to base)
        {
            if (Math.Abs(l - s * w) > 1.5 * NM && !(diag && l * s > w)) { b = Math.Max(b, Math.Max(a, 0) + 2 * tr + 2 * NM); v.Add(Pt(rw, Math.Clamp(a, 0, b), s * w)); }
        }
        else if (cross) v.Add(Pt(rw, Math.Max(a, b + 2 * tr + 2 * NM), s * w));   // wrong side, far out: cross, parallel in
        v.Add(Pt(rw, b, s * w));                                         // Base
        v.Add(Pt(rw, b, s * 1.5 * NM));                                  // Intercept heading
        return v;
    }

    /// Cost (NM): flight path to the threshold, turns (30° = 1 NM), gate below 8 NM (R42: last vector a good 2 NM before the approach gate, FAA JO 7110.65 5-9-1) 4 NM per NM, too high at the gate threefold, too close behind the preceding traffic (R46)
    /// (descent 300 ft/NM, on each leg not below the minimum altitude of the remaining path like StepDown/PathMva).
    double Cost(Telemetry t, string r, double g, double gmin, List<(double X, double Z)> v, bool final)
    {
        var pts = new List<(double X, double Z)> { (t.X, t.Z) };
        pts.AddRange(v);
        var (a, l) = AL(pts[^1].X, pts[^1].Z, r);
        double ai = Math.Max(a - Math.Max(Math.Abs(l) * 1.73, 2 * NM), gmin), path = ai / NM, turns = 0, h = t.Hdg * 180 / Math.PI, alt = IndFt(t);
        pts.Add(Pt(r, ai, 0));
        pts.Add(Pt(r, Math.Min(g, ai), 0));   // Centreline up to the gate (altitude only)
        var mva = new double[pts.Count + 1];
        for (int i = pts.Count - 1; i >= 1; i--) mva[i] = Math.Max(mva[i + 1], F.MvaLeg(pts[i - 1].X, pts[i - 1].Z, pts[i].X, pts[i].Z));
        for (int i = 1; i < pts.Count; i++)
        {
            double d = Dist(pts[i - 1].X, pts[i - 1].Z, pts[i].X, pts[i].Z) / NM, b = Bearing(pts[i - 1].X, pts[i - 1].Z, pts[i].X, pts[i].Z);
            alt = Math.Max(alt - d * 300, mva[i]);
            if (i == pts.Count - 1) break;
            path += d;
            turns += HdgDiff(h, b);
            h = b;
        }
        turns += HdgDiff(h, LandHdg(r));
        double gateFt = final ? FieldElev / Ft + g / NM * 318 : PatternFt, high = (alt - gateFt) / 300;
        // Climb because of terrain (e.g. from holding toward the mountains) doubled: rather the other side
        return path + turns / 30 + (final ? 4 * Math.Max(0, 8 * NM - g) / NM : 0) + 3 * Math.Max(0, high) + 2 * Math.Max(0, mva[1] - IndFt(t)) / 300 + (final && AheadR >= 0 && !Behind(path * NM, AheadR, AheadHeavy) ? 100 : 0);   // R46: too close behind the preceding traffic only if nothing else works
    }

    /// Terrain above the plan (ft, <= 0: feasible): up to the gate minimum altitude above the 2.5° path (visual: not below pattern altitude),
    /// afterwards on the centreline terrain + 250 ft above the glidepath (from 2 NM) or terrain + 500 ft above the pattern altitude (from initial).
    double Excess(Telemetry t, string rw, double g, List<(double X, double Z)> v, bool final)
    {
        var pts = new List<(double X, double Z)> { (t.X, t.Z) };
        pts.AddRange(v);
        pts.Add(Pt(rw, g, 0));
        double rest = 0, ex = double.MinValue;
        for (int i = 1; i < pts.Count; i++) rest += Dist(pts[i - 1].X, pts[i - 1].Z, pts[i].X, pts[i].Z);
        // Visual: minimum altitude at the gate up to 600 ft above the pattern (it descends leisurely over the 4 NM to initial) – otherwise no initial at hill airfields (360° overhead)
        double Prof(double r) => Math.Max(Math.Max(final ? 0 : PatternFt, FieldElev / Ft + r / NM * 265), final || Ifr ? 0 : PatternFt + (r - InitialDist) / NM * 150);
        for (int i = 1; i < pts.Count; i++)
        {
            var (x1, z1) = pts[i - 1];
            var (x2, z2) = pts[i];
            double d = Dist(x1, z1, x2, z2);
            int k = Math.Max(1, (int)Math.Ceiling(d / NM));
            for (int j = 0; j <= k; j++)
                ex = Math.Max(ex, F.MvaFt(x1 + (x2 - x1) * j / k, z1 + (z2 - z1) * j / k) - Prof(g + rest - d * j / k));
            rest -= d;
        }
        for (double a = final ? 2 * NM : InitialDist; a <= g; a += NM / 2)
        {
            var p = Pt(rw, a, 0);
            ex = Math.Max(ex, F.TerrainFt(p.X, p.Z, 0.25) + (final ? 250 - (FieldElev / Ft + 50 + a / NM * 265) : 500 - PatternFt));
        }
        return ex;
    }

    /// Straight-in approach from 6 NM with no terrain in the way (final and minimum altitude at the final approach point).
    bool FinalOk(string rw)
    {
        var p = Pt(rw, 6 * NM, 0);
        return Excess(new Telemetry(0, 0, 0, 0, p.X, p.Z, 0, 0, 0), rw, 6 * NM, new(), true) <= 0;
    }

    /// Tailwind (kt) on the runway, wind at the airfield.
    double TailKt(Telemetry t, string rw)
    {
        var (wx, wz) = FieldWind ?? (t.WindX, t.WindZ);
        var e = F.End(rw);
        return (wx * e.Dx + wz * e.Dz) / Kt;
    }

    /// Turn radius at 30° bank (TAS ~ IAS + 2 %/1000 ft).
    double TurnR(Telemetry t) { double v = t.Ias * (1 + 0.02 * IndFt(t) / 1000); return v * v / (9.81 * Math.Tan(Math.PI / 6)); }

    /// Turn onto brg (degrees, + right): the shorter way; from 120° the side whose turn circle (30° bank) lies over lower terrain
    /// (jet south of Kobuleti onto north: right over the sea, not left through the mountains).
    double Turn(Telemetry t, double brg)
    {
        double h = t.Hdg * 180 / Math.PI, d = ((brg - h) % 360 + 540) % 360 - 180, o = d - Math.Sign(d) * 360, r = TurnR(t);
        if (Math.Abs(d) < 120) return d;
        double Arc(double turn)   // highest MVA on the turn circle, every 30°
        {
            double s = Math.Sign(turn), m = 0;
            for (double a = 30; a <= Math.Abs(turn); a += 30)
            {
                double q = (h + s * a - s * 90) * Math.PI / 180, c = (h + s * 90) * Math.PI / 180;
                m = Math.Max(m, F.MvaFt(t.X + r * (Math.Cos(c) + Math.Cos(q)), t.Z + r * (Math.Sin(c) + Math.Sin(q))));
            }
            return m;
        }
        return Arc(o) < Arc(d) ? o : d;
    }

    /// "turn left heading 305" / "fly heading 305" (heading true, announcement magnetic).
    string TurnTo(Telemetry t, double brg)
    {
        double turn = Turn(t, brg);
        int mag = (int)Math.Round(((brg - MagVar) % 360 + 360) % 360 / 5) * 5;
        return (Math.Abs(turn) < 10 ? "fly" : $"turn {(turn > 0 ? "right" : "left")}") + $" heading {Digits((mag == 0 ? 360 : mag).ToString("000"))}";
    }

    /// Target heading (true): to the next waypoint or 30° to the centreline, flatter when close.
    double VecBrg(Telemetry t)
    {
        if (vec!.Count > 0) return Bearing(t.X, t.Z, vec[0].X, vec[0].Z);
        var tp = Icpt(t.X, t.Z);
        return Bearing(t.X, t.Z, tp.X, tp.Z);
    }

    /// Turn-in point on the centreline: 30° intercept angle, flatter when close, not behind the gate (visual: up to 1 NM before initial),
    /// the gate at most at 45° and at least 2 NM ahead: otherwise the target near the gate would lie across/behind him, headings left/right alternating (zigzag at 6-9 NM).
    (double X, double Z) Icpt(double x, double z)
    {
        var (a, l) = AL(x, z, vrw);
        return Pt(vrw, Math.Max(IcptA(a, l), a > 2 * NM ? Math.Min(IcptMin, a - Math.Max(Math.Abs(l), 2 * NM)) : IcptMin), 0);
    }
    static double IcptA(double a, double l) => a - Math.Max(Math.Abs(l) * 1.73, 2 * NM);
    /// R306: heading h (true) leads onto the centreline: at most 45° to it and meets it before the minimum turn-in point (- 1 NM), or he is up to 0.3 NM off.
    /// Then no new heading – correction only if he misses it (FAA JO 7110.65 5-9-2).
    bool Intercepts(Telemetry t, double h)
    {
        if (HdgDiff(h, LandHdg(vrw)) > 45) return false;
        var (a, l) = AL(t.X, t.Z, vrw);
        if (Math.Abs(l) < 0.3 * NM) return true;
        var (a1, l1) = AL(t.X + NM * Math.Cos(h * Math.PI / 180), t.Z + NM * Math.Sin(h * Math.PI / 180), vrw);
        return Math.Abs(l1) < Math.Abs(l) && a + l / (l - l1) * (a1 - a) > IcptMin - NM;   // approaching; intersection at l / (l - l1) NM
    }
    double IcptMin => vecFinal ? gate : Math.Min(gate, InitialDist + NM);

    /// Last stated altitude/speed/leg of radar vectoring (applies per waypoint list: new planning = first contact, everything complete).
    List<(double X, double Z)>? vecMemList;
    double vecMemFt; int vecMemKt; string vecMemLeg = "";

    /// Heading to the next waypoint or intercept heading; distance, altitude, speed and leg only if new or changed (full: everything, e.g. "say again").
    string? vecWhy;   // R222: reason for replanning for the first call (FAA JO 7110.65 5-9-3), set by VecTick
    string VecCall(Telemetry t, double now, double? stepFt = null, double? leg = null, bool full = false, bool again = false)
    {
        var rw = vrw;
        lastVecSaid = now;
        double brg = leg ?? VecBrg(t);   // leg: heading of the new leg (turn anticipated), otherwise to the waypoint
        if (!ReferenceEquals(vec, vecMemList)) { vecMemList = vec; intentsAsked = false; vecMemLeg = ""; vecMemKt = 0; vecMemFt = -1; full = true; lastAltFt = -1; spdTold = 0; spdFree = false; apprClr = false; }   // Start of guidance (StartVectors, direct): first contact
        double? sd = null;
        KeepLow(t);
        double mva = PathMva(t);
        if (mva > vecFt) vecFt = mva;   // Terrain on the way: high
        else sd = stepFt ?? StepDown(t);
        string tail;
        if (vec!.Count > 0)
            tail = vec.Count == 1 || HdgDiff(brg, LandHdg(Runway) + 180) > 45 ? $", vectors for {(vecFinal ? "final approach" : Circling ? (CircleIfr ? "circling approach" : $"{Hand(Runway)} downwind") : vecOver ? "overhead join" : "initial")} runway {RwSay(Runway)}"
                                  : ", downwind";
        else   // Review R212: circling without procedure (VMC): centreline of the reciprocal runway, then downwind of the active runway
            tail = $", intercept {(!vecFinal && !(Circling && CircleIfr) ? "the extended centerline" : Proc(rw) == "ILS" && !wantVisual ? "the localizer" : "final approach course")} runway {RwSay(rw)}" +
                   $"{(!vecOver ? "" : Circling ? (CircleIfr ? $", circle to runway {RwSay(Runway)}" : $" for {Hand(Runway)} downwind runway {RwSay(Runway)}") : $" for overhead join runway {RwSay(Runway)}")}";   // Reciprocal direction only as a way to the overhead: say so, otherwise he thinks of a landing there
        // R353: last turn-in vector at most 30°, meets the centreline 2 NM or more before the gate and within 18 NM (localizer service volume), not above the glide path there (at most gate + 1000 ft):
        // "maintain … until established" and the approach clearance in the same call (FAA JO 7110.65 5-9-1, 5-9-2)
        var ip = Icpt(t.X, t.Z);
        double ca = AL(ip.X, ip.Z, rw).A;
        var p = Proc(rw);
        bool clr = vec.Count == 0 && vecFinal && p != "" && !wantVisual && HdgDiff(brg, LandHdg(rw)) <= 30.5 && ca >= gate + 2 * NM && ca <= Math.Max(18 * NM, gate + 2 * NM) && vecFt <= Math.Min(GateFt + 1000, FieldElev / Ft + ca / NM * 318);
        if (clr) { tail = $", maintain {Alt(vecFt)} until established{(p == "ILS" ? " on the localizer" : "")}, cleared {p} approach runway {RwSay(rw)}" + OnTheGo(rw); apprClr = true; altFree = false; }
        var tailSaid = vecWhy != null && vec.Count > 0 ? ", " + vecWhy : tail;   // R222: led through the approach line / replanned: reason instead of first-contact wording (vecMemLeg remains the plan leg)
        vecWhy = null;
        bool newLeg = tail != vecMemLeg;
        vecMemLeg = tail;
        vecHdg = brg;
        double turn = Turn(t, brg);
        int mag = (int)Math.Round(((brg - MagVar) % 360 + 360) % 360 / 5) * 5;
        var hdg = $"heading {Digits((mag == 0 ? 360 : mag).ToString("000"))}";
        var to = vecFinal ? Thr(rw) : vecOver ? Ovh : OnCenterline(rw, InitialDist);
        var range = $"{MilesTxt(Dist(t.X, t.Z, to.X, to.Z))} from {(vecFinal ? "touchdown" : vecOver ? "the field" : "initial")}";
        // Heading always; distance at first contact and with the last vector; altitude/speed only on change (or far off); leg only on change
        var head = Math.Abs(turn) < 10 ? $"fly {hdg}" : $"turn {(turn > 0 ? "right" : "left")} {hdg}";
        bool sayRange = full || newLeg && vec.Count == 0;
        var sb = new System.Text.StringBuilder(sayRange && vec.Count == 0 ? $"{range}, {head}" : head);   // R316: turn-in vector with position before the heading (FAA JO 7110.65 5-9-4, R220)
        if (sayRange && vec.Count > 0) sb.Append($", {range}");
        double dz = IndFt(t) - vecFt;
        bool altDev = again || dz < -300 && t.Vs < 2.5 || !Emergency && !Pd(t) && dz > 300 && t.Vs > -2.5;   // R292: emergency on top: descent at own discretion   // off and not correcting: name direction, not only "maintain" (R130 same-altitude rule applies only while he is flying toward it)
        if (clr) lastAltFt = vecFt;   // R353: the altitude is in the clearance
        else if (altFree) { if (vecMemFt < 0) sb.Append(", altitude at your discretion" + ExpectLower(t)); }   // R394: first contact only, then nothing until StepDown
        else if (full || vecFt != vecMemFt || Math.Abs(IndFt(t) - vecFt) > 1000) sb.Append(", " + (sd is { } ft && ft < IndFt(t) ? Descend(lastAltFt = ft, t) : AltTo(t, vecFt, altDev) + (vecMemFt < 0 && FreeNm(t) >= 10 ? ExpectLower(t) : "")));   // R130: AltTo only if the altitude is actually stated (remembers it); R394 IFR: "maintain …, expect lower in …" far out (FAA JO 7110.65 4-5-7)
        vecMemFt = vecFt;
        if (VecKt(t) is > 0 and var kts && (full || kts != vecMemKt || Math.Abs(t.Ias / Kt - kts) > 60)) { if ((again ? Spd(t, kts) : SpdSay(t, kts, now, kts != vecMemKt)) is { } ss) sb.Append(", " + ss); vecMemKt = kts; }   // say again: repeat verbatim, does not count
        if (full || newLeg) sb.Append(tailSaid);
        return sb.Append('.').ToString();
    }

    /// Prescribed speed: holding (also AI orbit, DcsAtcMission.lua), radar vectoring to initial or downwind/base/turn-in.
    public const int HoldKt = 230;
    /// Target speed under radar vectoring (from 25 NM): visual 300 kt; straight-in by flight path to the threshold 300, below 20 NM 250, below 10 NM 200 kt
    /// (do not brake right at the start of a long downwind: 25 NM straight-line there are often 35 NM of path).
    /// Slow types get lower values or 0 (= no speed instruction, no speed reminder); jets as above.
    int VecKt(Telemetry t)
    {
        if (spdFree || altFree || Dist(t.X, t.Z, CX, CZ) > 25 * NM) return 0;   // VFR far out (altitude at his discretion): speed too, the restriction comes with the descent
        int tier = !vecFinal || Path(t).R > 20 * NM ? 0 : Path(t).R > 10 * NM ? 1 : 2;
        if (fromHold) tier = Math.Max(tier, 1);   // R319: from holding do not accelerate to 300 kt (FAA JO 7110.65 5-7-1)
        int kt = (Slow ?? new[] { 300, 250, 200 })[tier];
        return Tight(t) ? (int)Math.Round(kt * 0.85 / 10) * 10 : kt;   // R46: too close behind the preceding traffic: 15 % slower (200 -> 170 kt)
    }
    int[]? Slow => SlowTypes.FirstOrDefault(s => Regex.IsMatch(AcType, s.Rx)).Kts;
    /// Speed in holding by type: at most HoldKt, 0 = no speed instruction (helicopters, aerobatic).
    int HoldKtFor() => Slow is { } s ? Math.Min(HoldKt, s[0]) : HoldKt;
    string HoldSpd(Telemetry? t) => HoldKtFor() is > 0 and var k ? ", " + Spd(t, k) : "";
    /// Own aircraft type (DCS name, set by Program.Prep).
    public string AcType = "";
    /// Night (sun below -6°, Y line of the mission; unknown = day) or type without overhead break: inbound gets straight-in/instrument approach right away.
    static bool Night => Carrier.Sun < -6;
    static readonly Regex NoBreak = new(@"^(UH-|Mi-|Ka-|AH-|CH-|OH|SA342|AS532|C-1[37]|C-47|An-|Il-|Tu-|B-1|B-52|KC|E-[23]|S-3)");
    bool wantOverhead;   // explicitly overhead/visual requested: applies at night too
    static readonly (string Rx, int[] Kts)[] SlowTypes =
    {
        (@"^(UH-|Mi-|Ka-|AH-|CH-|OH|SA342|AS532|Yak-52|Christen)", new[] { 0, 0, 0 }),           // Helicopters, Yak-52, Christen Eagle II
        (@"^(P-51|TF-51|P-47|F4U|Bf-109|FW-190|Spitfire|I-16|Mosquito|C-47)", new[] { 200, 170, 140 }),   // Propeller/warbirds
        (@"^(A-10|L-39|C-101)", new[] { 250, 200, 160 }),                                         // slow jets/ground attack
    };

    /// Remaining waypoints to turn-in and flight path (m) to the threshold.
    (List<(double X, double Z)> Pts, double R) Path(Telemetry t)
    {
        var pts = new List<(double X, double Z)> { (t.X, t.Z) };
        pts.AddRange(vec ?? new());
        pts.Add(Icpt(pts[^1].X, pts[^1].Z));
        double r = AL(pts[^1].X, pts[^1].Z, vrw).A;
        for (int i = 1; i < pts.Count; i++) r += Dist(pts[i - 1].X, pts[i - 1].Z, pts[i].X, pts[i].Z);
        return (pts, r);
    }
    /// R129: speed that he misses by more than 60 kt is not repeated endlessly (FAA JO 7110.65 5-7-1/5-7-2): 1st time the speed, then 90 s to comply (intermediate vectors without speed),
    /// 2nd time "say airspeed" (reply: OnTranscript/IasSlot; none in 30 s: "resume normal speed"), 3rd time "resume normal speed" (spdFree: no speed restriction anymore for this approach). null = nothing about speed now.
    int spdTold; bool spdFree, slewing;   // slewing: VecTick, he is currently braking/accelerating toward target
    double spdAt;   // last counted speed callout or "say airspeed"
    string? SpdSay(Telemetry t, int kt, double now, bool fresh = false)
    {
        if (slewing || Math.Abs(t.Ias / Kt - kt) <= 60) return Spd(t, kt);   // one who is already braking toward target complies: do not count
        if (spdTold == 0 || fresh && spdTold < 2) { spdTold = Math.Max(spdTold, 1); spdAt = now; return Spd(t, kt); }   // new instruction (also new value)
        if (now - spdAt < 90) return null;   // time to comply: do not repeat, do not count
        spdAt = now;
        if (++spdTold == 2) return "say airspeed";
        spdFree = true;
        return "resume normal speed";
    }
    /// R129: reply to "say airspeed" or "unable speed": reported speed ("340 knots", "airspeed 3 4 0", "slowing to 300"; 0 = "slowing" without number), -1 = unable, null = no speed report.
    internal static int? IasSlot(string n)
    {
        if (Has(n, "unable")) return -1;
        if (Regex.Match(n, @"\b(\d+(?: \d+)*) (?:knots|kts|kt)\b") is { Success: true } k) { var d = k.Groups[1].Value.Replace(" ", ""); return int.Parse(d.Length > 3 ? d[^3..] : d); }   // "enfield 1 1 340 knots": callsign digits in front
        if (Run(n, @"(?:air ?speed|indicated|slowing down|slowing|reducing|decelerating|speed)(?: to| through)?", 3) is { } s) return int.Parse(s);
        return Has(n, "slowing", "reducing", "decelerating") ? 0 : null;
    }
    /// Radio menu "Fahrt melden": displayed speed rounded to 10 kt ("340 knots").
    internal static string IasSay(Telemetry t) => FormattableString.Invariant($"{Math.Round(t.Ias / Kt / 10) * 10:0} knots");
    static string Spd(Telemetry? t, int kt) => t == null || Math.Abs(t.Ias / Kt - kt) <= 30 ? $"speed {kt} knots"
                                             : $"{(t.Ias / Kt > kt ? "reduce" : "increase")} speed {kt} knots";

    /// Landing sequence: aircraft near the airfield that are closer to the airfield. Only on change or every 90 s.
    string SeqNote(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        double mine = Dist(t.X, t.Z, CX, CZ);
        int ahead = QueueAhead >= 0 ? QueueAhead
                  : traffic.Count(a => a.InAir && a.Speed > 30 && a.AltMsl - FieldElev < 2500 && Dist(a.X, a.Z, CX, CZ) < Math.Min(mine, 20 * NM));
        if (ahead == lastAhead) return "";   // R315: only on change (not the same every 90 s)
        lastAhead = ahead; lastSeqSaid = now;
        return ahead == 0 ? " No traffic ahead, you are number one." : $" You are number {ahead + 1}, {ahead} aircraft ahead.";
    }

    /// Minimum altitude (terrain) on the remaining path: waypoints up to the turn-in point. Already on the intercept heading in the final approach area: like the
    /// straight-in approach (R42) terrain + 250 ft on the centreline, not the MVA (3 NM radius + 1000 ft would lie above the glidepath there: "climb" at 8 NM).
    double PathMva(Telemetry t)
    {
        if (vecFinal && vec is { Count: 0 } && OnFinalLoose(t, vrw))
        {
            // plus terrain + 250 ft on the way to the centreline (up to a good 2 NM off: otherwise "maintain" just above a hill below him)
            var ip = Icpt(t.X, t.Z);
            double d = Dist(t.X, t.Z, ip.X, ip.Z), fl = FinalFloor(vrw, AL(t.X, t.Z, vrw).A);
            for (double f = 0; f <= 1; f += NM / 2 / Math.Max(d, NM)) fl = Math.Max(fl, Math.Ceiling((F.TerrainFt(t.X + (ip.X - t.X) * f, t.Z + (ip.Z - t.Z) * f, 0.25) + 250) / 100) * 100);
            return fl;
        }
        return LegsMva((t.X, t.Z), vec ?? new(), vrw, gate);
    }
    /// Minimum altitude (MVA) on the path from -> waypoints v -> gate g of runway rw.
    double LegsMva((double X, double Z) from, List<(double X, double Z)> v, string rw, double g)
    {
        var pts = new List<(double X, double Z)> { from };
        pts.AddRange(v);
        pts.Add(Pt(rw, g, 0));
        double m = 0;
        for (int i = 0; i + 1 < pts.Count; i++) m = Math.Max(m, F.MvaLeg(pts[i].X, pts[i].Z, pts[i + 1].X, pts[i + 1].Z));
        return m;
    }

    /// Minimum altitude on final (like Excess): terrain + 250 ft on the centreline from from to 2 NM, rounded up to 100 ft.
    double FinalFloor(string rw, double from)
    {
        double fl = 0;
        for (double a = from; a >= 2 * NM; a -= NM / 2) { var p = Pt(rw, a, 0); fl = Math.Max(fl, Math.Ceiling((F.TerrainFt(p.X, p.Z, 0.25) + 250) / 100) * 100); }
        return fl;
    }

    /// Turning in to final: already lower than instructed but above minimum altitude -> this altitude applies, altitude only descending (never "climb" on approach).
    void KeepLow(Telemetry t)
    {
        if (vecFinal && vec is { Count: 0 } && IndFt(t) < vecFt - 200 && PathMva(t) is var m && IndFt(t) >= m) vecFt = Math.Max(m, Math.Round(IndFt(t) / 100) * 100);
    }

    /// Altitude at the handover point (gate): glidepath or pattern altitude to initial/overhead. K10: capture glidepath from below,
    /// glidepath altitude 1 NM before the gate, rounded down to 100 ft (Kutaisi 07: 8 NM -> 2300 ft instead of 3000, then "well above glidepath").
    double GateFt => vecFinal ? Math.Floor((FieldElev / Ft + (gate / NM - 1) * 318) / 100) * 100 : PatternFt;
    /// R305: handover to the tower on the centreline only shortly before the gate (up to 2 NM before, low enough) or at the latest at the gate, not 25 NM out:
    /// Approach vectors to the gate/FAF (FAA JO 7110.65 5-9-1/5-9-4, AIM 5-4-3); a: distance before the threshold.
    bool HandoffOk(Telemetry t, double a) => a < gate || a < gate + 2 * NM && IndFt(t) <= GateFt + 1000;

    static double SegDist(double x, double z, (double X, double Z) a, (double X, double Z) b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z, u = Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / Math.Max(dx * dx + dz * dz, 1), 0, 1);
        return Dist(x, z, a.X + u * dx, a.Z + u * dz);
    }

    /// Descent instruction; emergency: at own discretion (FAA JO 7110.65 4-5-7).
    string Descend(double ft, Telemetry? t = null) => Emergency || t != null && Pd(t) ? $"descend at pilot's discretion, maintain {Alt(ft)}" : $"descend and maintain {Alt(ft)}";
    /// R303: descent at own discretion requested or stayed high on request ("request higher"): up to 10 NM remaining path before the gate "at pilot's discretion",
    /// no "descend" reminders as long as he is above; afterwards normal (AIM 4-4-10, FAA JO 7110.65 4-5-7 e).
    bool pdDescent;
    double keepHighR;   // R303: higher approved – up to this remaining path (m) no profile descent, 0 = none
    bool Pd(Telemetry t) => pdDescent && vec != null && Path(t).R - gate > 10 * NM;
    /// R394: altitude at his discretion (climb and descend) until the approach needs it: StepDown (profile), minimum altitude or the approach clearance ends it; vecFt follows him meanwhile
    bool altFree;
    /// R394: path (NM) until StepDown asks for the descent from his altitude – about 4 NM before he meets the 2.5° path (265 ft/NM)
    double FreeNm(Telemetry t) => Path(t).R / NM - (IndFt(t) - FieldElev / Ft) / 265 - 4;
    string ExpectLower(Telemetry t) => IndFt(t) >= GateFt + 1000 ? $", expect lower in {MilesTxt(Math.Round(FreeNm(t) / 5) * 5 * NM)}" : "";

    /// Next descent step (1000 ft) if the pilot is still above; never below pattern or glidepath capture.
    double? StepDown(Telemetry t)
    {
        var (pts, r) = Path(t);
        // 2.5° path (265 ft/NM of track to the threshold) from the start: step = path at the end of the leg (2–12 NM ahead), rounded down,
        // so just below it (IFR: glidepath from below), 2000 ft steps instead of many small ones (terrain limits: 1000 ft, otherwise he arrives too high); VFR not below pattern altitude
        double ahead = Math.Clamp(vec is { Count: > 0 } ? Dist(t.X, t.Z, vec[0].X, vec[0].Z) : r - gate, 2 * NM, 12 * NM);   // Final heading: up to the gate (handover)
        double prof = Math.Max(vecFinal ? 0 : PatternFt, Math.Floor((FieldElev / Ft + Math.Max(0, r - ahead) / NM * 265) / 500) * 500);
        var (c0, c1) = (Pt(vrw, Math.Max(AL(t.X, t.Z, vrw).A, gate), 0), Pt(vrw, gate, 0));
        double floor = Math.Max(Math.Max(vecFinal ? 0 : PatternFt, PathMva(t)), F.MvaLeg(c0.X, c0.Z, c1.X, c1.Z)), target = Emergency ? Math.Max(floor, GateFt) : Math.Max(prof, floor);   // Emergency: immediately the handover altitude, descent at own discretion; never below the minimum altitude (terrain), nor below that of the centreline from here (turn in directly: otherwise up again)
        // Holding on the further path (5 NM): 1000 ft separation, preferably pass below (descend earlier, just not pass through their altitude close beside), otherwise stay above until past
        var holds = near.Where(a => a.InAir && a.Flag.Contains("hold") && Enumerable.Range(1, pts.Count - 1).Any(i => SegDist(a.X, a.Z, pts[i - 1], pts[i]) < 5 * NM))
                        .Select(a => (a, Ft: HoldLvl(a))).OrderBy(h => h.Ft).ToList();   // R270: only those circling at the holding point ("hold:" only there, Program.TrafficFor; AI circles in place), assigned altitude
        foreach (var (h, hf) in Enumerable.Reverse(holds))
            if (Math.Abs(target - hf) < 1000) target = hf - 1000 >= floor && (IndFt(t) < hf || Dist(t.X, t.Z, h.X, h.Z) > 6 * NM) ? hf - 1000 : hf + 1000;
        // himself at the altitude of a holding on the path (e.g. just stepped down to it) and not passing below: before crossing 1000 ft above (FAA JO 7110.65 5-5-4);
        // R270: only from their altitude, never climb up through their altitude from below (below: hold altitude, e.g. 3100 below 4000 from the holding)
        double up = vecFt;
        foreach (var (_, hf) in holds) if (Math.Abs(up - hf) < 1000 && Math.Max(IndFt(t), up) > hf - 500) up = hf + 1000;
        if (up > vecFt && target > vecFt && LevelFree(t, near, up)) { altFree = false; return vecFt = up; }
        // at the end a step right to handover altitude (not 2200, then 2000); R305: only when the 2.5° path here (not 12 NM ahead) lies below, otherwise
        // he descended to gate altitude at 25 NM and VecTick handed over immediately (FAA JO 7110.65 5-9-4, profile descent); until then at most gate + 1000 ft
        if (target < GateFt + 1000 && floor <= GateFt) target = Emergency || !vecFinal || FieldElev / Ft + r / NM * 265 < GateFt + 1000 ? GateFt : GateFt + 1000;
        bool big = vecFt - target >= (target > prof ? 1000 : 2000), last =target <= GateFt + 1000 && vecFt - target >= 500;   // outside big steps, at the end to handover altitude
        if (!(big || last) || IndFt(t) < target + 500 || lastSeen < stepAfter || keepHighR > 0 && r > keepHighR || !LevelFree(t, near, target)) return null;   // Margin: up to 500 ft above the 2.5° path no new step; traffic there: stay up
        vecFt = target; altFree = false;
        return target;
    }

    /// Anticipate turn (radius at 300 kt ~2 NM): give the next heading early enough that he rolls out on the new leg. Lim: lead before vec[0], Nb: heading of the next leg.
    (double Lim, double Nb) Lead(Telemetry t)
    {
        var nx = vec!.Count >= 2 ? vec[1] : Icpt(vec[0].X, vec[0].Z);   // Next leg: next waypoint or turn-in point
        double nb = Bearing(vec[0].X, vec[0].Z, nx.X, nx.Z);   // Corner at waypoint: heading there -> next leg (not the current heading, otherwise much too early)
        return (Math.Clamp(TurnR(t) * Math.Tan(Math.Min(HdgDiff(Bearing(t.X, t.Z, vec[0].X, vec[0].Z), nb), 150) * Math.PI / 360), 1.5 * NM, 5 * NM), nb);
    }

    /// Every second during radar vectoring.
    List<Msg>? VecTick(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        if (Phase != Phase.Inbound || vec == null) return null;
        var c = Cs();
        var rw = vrw;
        double hdg = t.Hdg * 180 / Math.PI;
        KeepLow(t);
        if (altFree)   // R394: follow his altitude (no deviation calls); below the minimum altitude the freedom ends with a climb
        {
            double min = Math.Max(PathMva(t), vecFinal ? 0 : PatternFt);
            vecFt = Math.Max(Math.Round(IndFt(t) / 500) * 500, min);
            if (IndFt(t) < min - 200 && t.Vs < 2.5 && now - lastVecSaid >= 15) { altFree = false; lastVecSaid = now; vecMemFt = vecFt; return Say($"{c}, {AltTo(t, vecFt)}.", "Approach"); }
        }
        bool soon = false;   // R355: VFR on the centreline, handover zone (gate + 2 NM) within ~15 s: no profile step any more, the handover states the altitude
        if (vec.Count > 0)
        {
            var (lim, nb) = Lead(t);
            double wb = Bearing(t.X, t.Z, vec[0].X, vec[0].Z), wd = Dist(t.X, t.Z, vec[0].X, vec[0].Z);
            vecLim = lim;   // R315: correction/StepDown not shortly before this turn
            // Senaki 0.9.5: wide turn radius (fast/flat) -> passes the waypoint to the side; counts as reached (up to 5 NM off), otherwise U-turn back to the point behind him
            bool abeam = HdgDiff(hdg, vecHdg) < 30 && wd < 5 * NM && wd * Math.Cos(HdgDiff(hdg, wb) * Math.PI / 180) < lim;
            // R351: the next vector only once the previous one is being flown (15 s); the waypoint stays until the call goes out
            if (((HdgDiff(hdg, vecHdg) < 30 || HdgDiff(hdg, wb) > 90) && wd < lim || abeam) && now - lastVecSaid >= 15) { vec.RemoveAt(0);
                // last turn already close to the centreline and low enough: hand over right away (not intercept heading and 1 s later "cleared approach")
                if (vec.Count == 0 && AL(t.X, t.Z, rw) is var (ea, el) && ea > 0 && Math.Abs(el) < 2 * NM && HandoffOk(t, ea)) return Established(t);
                return Say($"{c}, {VecCall(t, now, leg: nb)}", "Approach"); }   // equal to the leg heading: rolls out on it, no follow-up correction
        }
        else
        {
            var (a, l) = AL(t.X, t.Z, rw);
            // Turning in with turn radius: turn to runway heading now, then he rolls out on the centreline
            double dh = HdgDiff(hdg, LandHdg(rw));
            double lead = TurnR(t) * (1 - Math.Cos(Math.Min(dh, 90) * Math.PI / 180)) + 0.1 * NM;
            bool closing = dh < 5 || Math.Abs(AL(t.X + 300 * Math.Cos(t.Hdg), t.Z + 300 * Math.Sin(t.Hdg), rw).L) < Math.Abs(l);
            soon = !vecFinal && a > 0 && dh < 60 && closing && Math.Abs(l) - Math.Max(lead, 0.3 * NM) < 15 * t.Ias * Math.Sin(dh * Math.PI / 180) && a - 15 * t.Ias * Math.Cos(dh * Math.PI / 180) < gate + 2 * NM;
            if (a > 0 && Math.Abs(l) < Math.Max(lead, 0.3 * NM) && dh < 60 && closing)   // also far out: otherwise he flies through the centreline
            {
                if (HandoffOk(t, a)) return Established(t);
                // too high or too far out for handover: first on heading, keep descending (StepDown), hand over at the gate at the latest. R306: only if he otherwise flies through –
                // after "intercept …" he turns in himself (already turning to runway heading or not yet at his own turn-in point): no "join" seconds after the intercept heading
                bool capturing = dh < HdgDiff(prevHdg, LandHdg(rw)) - 0.01 || Math.Abs(l) > (lead - 0.1 * NM) * 0.6;   // turns in or is not yet clearly beyond his turn-in point
                if (dh > 10 && HdgDiff(vecHdg, LandHdg(rw)) > 1 && !capturing && !apprClr)
                {                    vecHdg = LandHdg(rw); lastVecSaid = now; altFree = false;
                    return Say($"{c}, {TurnTo(t, vecHdg)}, join the extended centerline runway {RwSay(rw)}, {AltTo(t, vecFt)}.", "Approach");
                }
            }
            bool late = a - Math.Abs(l) * 1.73 < (vecFinal ? Math.Min(IcptMin - NM, 3 * NM) : IcptMin - NM);   // 30° turn-in no longer possible (target across/behind him); flown through (gate 8/10 NM, R42): lead back up to 3 NM instead of a full circle
            if (late && vecFinal && a > 2 * NM && Math.Abs(l) < NM && dh < 45 && now - lastVecSaid > 30)   // almost on the centreline (e.g. flown through): hand over instead of full circle
            {
                if (a >= gate - NM) return Established(t);
                if (!intentsAsked)   // R318: within the gate no clearance with gate altitude, state position and ask intentions (FAA JO 7110.65 5-9-2 b); then replan
                {
                    intentsAsked = true; lastVecSaid = now;
                    double side = ((Bearing(Thr(rw).X, Thr(rw).Z, t.X, t.Z) - LandHdg(rw) - 180) % 360 + 540) % 360 - 180;
                    return Say($"{c}, {MilesTxt(a)} from the airport, {(Math.Abs(l) < 0.75 * NM ? "one half mile" : MilesTxt(Math.Abs(l)))} {(side > 0 ? "left" : "right")} of course, say intentions.", "Approach");
                }
            }
            if ((late || Math.Abs(l) > 10 * NM) && now - lastVecSaid > 30)   // or too far away: replan
                // Visual to initial as number 1, not far across: rather direct (up to 60° bend at initial) than a full circle
                return late && !vecFinal && !vecOver && QueueAhead <= 0 && a - InitialDist > NM && Math.Abs(l) < Math.Min((a - InitialDist) * 1.73, 10 * NM) ? Established(t, true)
                     : Say($"{c}, {Replan(t, now, late && vecFinal ? "vectors across final for re-sequencing" : null)}", "Approach");
        }
        bool joining = vec.Count == 0 && Math.Abs(AL(t.X, t.Z, rw).L) < NM && HdgDiff(hdg, LandHdg(rw)) < 35;   // is just turning in
        double ft = IndFt(t);
        bool turning = HdgDiff(hdg, vecHdg) < HdgDiff(prevHdg, vecHdg) - 0.2;   // is just turning onto the heading: leave alone
        prevHdg = hdg;
        slewing = VecKt(t) > 0 && Math.Abs(t.Ias - VecKt(t) * Kt) < Math.Abs(prevIas - VecKt(t) * Kt) - 0.2;   // is just braking/accelerating toward target: leave alone
        prevIas = t.Ias;
        if (apprClr)   // R353: cleared: he intercepts himself, no corrections or descents any more; flown through the centreline: vectors again
        {
            if (Intercepts(t, hdg) || Math.Abs(AL(t.X, t.Z, rw).L) < NM) return null;
            apprClr = false;
        }
        if (HdgDiff(hdg, vecHdg) <= 7 && Math.Abs(ft - vecFt) <= 500) { devAt.Clear(); ignored = 0; }   // complied with
        if (Math.Abs(ft - vecFt) <= 300) { reachedFt = vecFt; highTold = 0; }   // R302: arrived at the assigned altitude (above it afterwards = climbed unasked)
        if (VecKt(t) > 0 && Math.Abs(t.Ias / Kt - VecKt(t)) <= 30) spdTold = 0;   // Speed complied: counter back
        if (spdTold == 2 && now - spdAt >= 30 && !slewing && VecKt(t) > 0)   // R129: no reply to "say airspeed" -> cancel instruction
        {
            spdFree = true; lastVecSaid = now;
            return Say($"{c}, resume normal speed.", "Approach");
        }
        // Deviation outside ±7° / ±500 ft / ±30 kt and not already correcting
        double dHdg = HdgDiff(hdg, vecHdg), dAlt = ft - vecFt, dKt = VecKt(t) > 0 ? t.Ias / Kt - VecKt(t) : 0;
        bool hdgOff = !joining && !turning && dHdg > 7, lowOff = dAlt < -500 && t.Vs < 2.5, highOff = !Emergency && !Pd(t) && dAlt > 500 && t.Vs > -2.5,   // R292: emergency descends at own discretion, no "expedite descent"
             spdOff = !slewing && Math.Abs(dKt) > 30;
        // Grossly off (> 30°, > 1000 ft, > 60 kt): immediately, at most every 30 s, counts toward the kick
        if (now - lastVecSaid >= 30 && hdgOff && dHdg > 30)
            return Ignored("hdg", dHdg, 10) ? Kick(t) : Say($"{c}, {VecCall(t, now)}", "Approach");
        if (now - lastVecSaid >= 30 && lowOff && dAlt < -1000)
        {
            if (Ignored("alt", -dAlt, 200)) return Kick(t);
            lastVecSaid = now;
            return Say($"{c}, check altitude, climb and maintain {Alt(vecFt)}.", "Approach");
        }
        if (now - lastVecSaid >= 30 && highOff && dAlt > 1000)
        {
            if (Ignored("alt", dAlt, 200)) return Kick(t);
            lastVecSaid = now;
            // R302 (FAA JO 7110.65 5-2-17, P/CG EXPEDITE): "expedite" only if traffic at his altitude demands it; climbed unasked: first "verify altitude",
            // then "climb not authorized"; not yet down (not descending): repeat the descent clearance
            lastAltFt = vecFt;
            return Say($"{c}, " + (!LevelFree(t, traffic, ft) ? $"expedite descent, maintain {Alt(vecFt)}"
                                 : reachedFt != vecFt ? Descend(vecFt)
                                 : highTold++ == 0 ? $"verify altitude, maintain {Alt(vecFt)}" : $"climb not authorized, descend and maintain {Alt(vecFt)}") + ".", "Approach");
        }
        // new heading needed (wind/offset, through the centreline), not mid-turn and not shortly before (the turn corrects too); before the descent, the call includes it
        // R306: when turning in only if he misses the centreline – the turn-in point (VecBrg) slides along close to it and gets flatter (300 -> 285 -> 270), the given heading stays
        if (now - lastVecSaid >= 30 && !turning && HdgDiff(VecBrg(t), vecHdg) > 7 && (vec.Count == 0 ? !Intercepts(t, vecHdg) && !(joining && Intercepts(t, hdg)) : Dist(t.X, t.Z, vec[0].X, vec[0].Z) > Math.Max(2.5 * NM, vecLim + NM))) return Say($"{c}, {VecCall(t, now)}{SeqNote(t, traffic, now)}", "Approach");
        if (now - lastVecSaid >= 30 && Tight(t) && VecKt(t) is > 0 and var sk && sk < vecMemKt && t.Ias / Kt > sk + 10)   // R46: separation by speed (FAA JO 7110.65 5-7-1, ICAO Doc 4444 4.6)
        {
            lastVecSaid = now; vecMemKt = sk;
            return Say($"{c}, reduce speed {sk} knots for spacing.", "Approach");
        }
        if (now - lastVecSaid >= 30 && !soon && !(vec.Count > 0 && Dist(t.X, t.Z, vec[0].X, vec[0].Z) < vecLim + NM) && StepDown(t) is { } sd)   // Profile descent: gradually to handover altitude
        {
            lastVecSaid = now;
            var say = sd > IndFt(t) ? $"climb and maintain {Alt(sd)}" : Descend(sd, t);   // Holding on the path: above it (StepDown)
            lastAltFt = sd;
            vecMemFt = vecFt;   // R315: the next vector call does not repeat this altitude
            return Say($"{c}, {say}.", "Approach");
        }
        if (now - lastVecSaid >= 30 && spdOff && Math.Abs(dKt) > 60 && SpdSay(t, VecKt(t), now) is { } spd)
        {
            lastVecSaid = now;
            return Say($"{c}, {spd}.", "Approach");
        }
        // Slightly off: no own reminder, the next call (step, turn, new heading) corrects too. If none in 90 s: quiet overall call
        if (now - lastVecSaid >= 90 && (hdgOff || lowOff || highOff || spdOff)) return Say($"{c}, {VecCall(t, now, full: lowOff || highOff || spdOff)}", "Approach");
        return null;
    }

    // ----- Sign-off / kick: whoever fails to comply with a correction three times (deviation not reduced) is dropped from the sequence
    int ignored;
    readonly Dictionary<string, double> devAt = new();
    string ignKind = "hdg";
    bool Ignored(string kind, double dev, double tol)
    {
        ignKind = kind;
        bool ign = devAt.TryGetValue(kind, out var was) && dev > was - tol;
        devAt[kind] = dev;
        ignored = ign ? ignored + 1 : 0;
        return ignored >= 3 && !(Emergency && emgAsked);   // R292: emergency only once "say intentions" (kick), afterwards normal corrections
    }
    bool emgAsked;   // R292: in emergency "say intentions" instead of kick already said
    double reachedFt = -1; int highTold;   // R302: last reached assigned altitude; how often unasked climbing was addressed
    /// Missed approach / practice approach over on instrument approach (R12 · R44, ICAO Doc 4444 12.3.4) in two steps: the tower gives only runway heading, altitude and frequency,
    /// Approach vectors only after check-in (MissedVec, without a call after 45 s). 60 s no final calls (lastGa, like A11).
    List<Msg> Missed(Telemetry t, double now, string pre, bool practice = false)   // practice: practice approach over; climb-out was already in the approach clearance (R217): only the frequency
    {
        Phase = Phase.Inbound;
        holding = false; vec = null; missDep = false;
        PatternReset();
        bool told = practice && goFt > 0;
        (missAt, missFt, lastGa) = (now, told ? goFt : Math.Max(ClimbFt(Runway), Math.Ceiling(IndFt(t) / 1000) * 1000), now);
        goFt = 0; dueAt = -1;   // Reporting duty: check-in with Approach due from now (#9)
        return Say(told ? $"{Cs()}, contact {Contact("Approach")}." : $"{Cs()}, {pre} Fly runway heading, climb and maintain {Alt(missFt)}, contact {Contact("Approach")}.", "Tower");
    }
    double dueAt = -1;   // Reporting duty (#8–#12): since when reminded of the mandatory report, -1 = not yet, MaxValue = consequence drawn
    /// Reporting duty (MELDEPFLICHT-PLAN #8–#12, "Check-in überfällig"): check-in or mandatory report missing. due: remind once (warn); afterwards, as soon as late
    /// and at the earliest wait s after the reminder, the real consequence once (fail). Every new duty (handover, missed approach, clearance) sets dueAt = -1.
    List<Msg>? Overdue(bool due, bool late, double wait, double now, Func<List<Msg>> warn, Func<List<Msg>> fail)
    {
        if (dueAt < 0) { if (!due) return null; dueAt = now; return warn(); }
        if (dueAt == double.MaxValue || !late || now - dueAt < wait) return null;
        dueAt = double.MaxValue;
        return fail();
    }
    /// Second step: "Enfield 1-1, Kutaisi Approach, identified, turn right heading 340, climb and maintain 4000 feet, say intentions." (practice approach without "say intentions")
    List<Msg> MissedVec(Telemetry t, double now)
    {
        missAt = -1;
        var v = Cap(StartVectors(t, true, now, missFt));
        return Say($"{Cs()}, {F.StationOf("Approach")}, identified. {v}{(stayPattern ? "" : " Say intentions.")}", "Approach");
    }
    /// Climb after takeoff or missed approach on runway heading: zone altitude, at least the terrain 10 NM ahead (full 1000 ft).
    double ClimbFt(string rw) { var (ux, uz) = OnCenterline(rw, -10 * NM); return Math.Ceiling(Math.Max(MaxFt, F.MvaFt(ux, uz)) / 1000) * 1000; }
    /// R8: clearance limit without stated destination: nearest own/neutral airfield in departure direction (±90°), otherwise the nearest at all; null = none.
    Airfield? Limit(string rw)
    {
        var l = All.Where(f => f.Name != F.Name && !Hostile(f) && Dist(f.X, f.Z, CX, CZ) > NM).OrderBy(f => Dist(f.X, f.Z, CX, CZ)).ToList();
        return l.FirstOrDefault(f => HdgDiff(Bearing(CX, CZ, f.X, f.Z), F.End(rw).Hdg) <= 90) ?? l.FirstOrDefault();
    }
    /// At the exit CRP (1.5 NM): the zone departure clearance ends there.
    bool AtExit(Telemetry t) => exitDir != null && Crp.TryGetValue(exitDir, out var ex) && Dist(t.X, t.Z, ex.X, ex.Z) < 1.5 * NM;
    /// R7: VFR departure leaves the zone after check-in: "leaving control zone, resume own navigation, squawk VFR, frequency change approved."
    List<Msg> LeaveZone(string c, string head)
    {
        var bye = Say($"{c}, {head}, resume own navigation, " + (AwacsContact is { } aw ? $"contact {aw}." : SquawkVfr() + "frequency change approved. Good day."), "Approach");   // still as Departure (Dep)
        zoneReport = handedOff = false; Released = true;   // signed off: no second sign-off at 15 NM, pin released (R47)
        return bye;
    }

    /// "request direct" under radar vectoring: without downwind straight to the intercept heading or final – only as number 1,
    /// without terrain above the 2.5° path and not too high for the shorter path (3° + 1000 ft). null = unable.
    List<(double X, double Z)>? Direct(Telemetry t)
    {
        if (vec!.Count == 0 || QueueAhead > 0) return null;
        var v = vec.Count >= 2 ? new List<(double X, double Z)> { vec[^1] } : new();
        if (Excess(t, vrw, gate, v, vecFinal) > 0) return null;
        double r = gate;
        var p = (X: t.X, Z: t.Z);
        foreach (var q in v.Append(Pt(vrw, gate, 0))) { r += Dist(p.X, p.Z, q.X, q.Z); p = q; }
        return IndFt(t) - FieldElev / Ft > r / NM * 320 + 1000 ? null : v;
    }

    /// Kick in stages (ICAO Doc 9432), per ignored correction from the third: ask, re-vector, "say intentions", only then sign off.
    /// If he complies in between, Ignored resets the counter. Spacing of stages = rhythm of the normal calls (lastVecSaid/lastHoldInfo).
    List<Msg> Kick(Telemetry t)
    {
        var c = Cs();
        double now = lastSeen;
        lastVecSaid = lastHoldInfo = now;
        // R292: emergency is not pressed and not taken out of the sequence, no violation (FAA JO 7110.65 10-1-3, 14 CFR 91.3(b))
        if (Emergency) { emgAsked = true; return Say($"{c}, say intentions.", "Approach"); }
        switch (ignored - 2)
        {
            case 1: return Say($"{c}, verify {(ignKind == "alt" ? "altitude" : "heading")}.", "Approach");
            case 2:
                if (ignKind == "alt") return Say($"{c}, {AltTo(t, vecFt, true)} immediately.", "Approach");   // Re-assign altitude, do not adopt his deviation (StartVectors would take the current altitude)
                if (holding && nav is { } hf)
                {
                    holdArrived = false;
                    return Say($"{c}, {Steer(t, hf, navName)}, {MilesTxt(Dist(t.X, t.Z, hf.X, hf.Z))} to the hold, {AltTo(t, holdFt)}.", "Approach");
                }
                return Say($"{c}, {Replan(t, now)}", "Approach");
            case 3: return Say($"{c}, say intentions.", "Approach");
            default: return Dev($"{c}, you are not following instructions, you are removed from the sequence. {Cap(CancelApproach(t))}",
                                $"aus der Reihenfolge entfernt (Anweisungen nicht befolgt) {F.Name}", $"removed from the sequence (instructions not followed) {F.Name}", "Approach");
        }
    }
    string CancelApproach(Telemetry? t)
    {
        Phase = Phase.Away;
        needWx = holding = false; vec = null; nav = null; entry = null; option = wantRw = null; stayPattern = wantStraight = wantVisual = wantOverhead = visAsk = visSeen = fromHold = false;
        PatternReset(); devAt.Clear(); ignored = 0; optedOut = true; wxTold = false;   // A145: next approach states the weather again
        return $"approach cancelled, resume own navigation, {Qnh(t)}. Call me when ready for another approach.";
    }

    /// On the centreline: handover to the tower.
    List<Msg> Established(Telemetry t, bool direct = false)
    {
        var c = Cs();
        var rw = Runway;
        vec = null;
        bool clr = apprClr; apprClr = false;
        Phase = Phase.Entering;
        handoffAt = lastVector = lastSeen; towerDue = true; towerNag = false;   // Tower only controls once he has turned in (otherwise "fly heading … to initial" mid-turn)
        if (vecFinal)
        {
            nav = null; navName = "six mile final";
            var thr = Thr(rw);
            if (wantVisual && !visSeen)   // R115: visual approach only with airfield in sight, stay with Approach (Tick: from 4 NM fallback to straight-in/instrument)
            {
                Phase = Phase.Inbound; visAsk = true;
                return Say($"{c}, {(HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) > 5 ? TurnTo(t, LandHdg(rw)) + ", " : "")}{MilesTxt(Dist(t.X, t.Z, thr.X, thr.Z))} from touchdown, expect visual approach runway {RwSay(rw)}, report field in sight.", "Approach");
            }
            if (clr) return Say($"{c}, {(Dist(t.X, t.Z, thr.X, thr.Z) < 9 * NM || AheadR >= 0 ? "reduce to final approach speed, " : "")}{Qnh(t)}, contact {Contact("Tower")}.", "Approach");   // R353: cleared with the turn-in vector: handover only
            // R220: position before heading, "until established on the localizer" (FAA JO 7110.65 5-9-4); R271: PAR without lost-comm instruction (R221) – it promises calls every 5 s on final that do not exist without talk-down (N13); R217: practice approach with climb-out
            return Say($"{c}, {MilesTxt(Dist(t.X, t.Z, thr.X, thr.Z))} from touchdown, {(HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) > 5 ? TurnTo(t, LandHdg(rw)) + ", " : "")}" +
                       (Proc(rw) is var p && p != "" && !wantVisual ? $"{(HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) > 5 || Math.Abs(AL(t.X, t.Z, rw).L) > 0.3 * NM ? $"maintain {Alt(vecFt)} until established{(p == "ILS" ? " on the localizer" : "")}, " : "")}cleared {p} approach runway {RwSay(rw)}" : $"on course, {(wantVisual ? "cleared visual approach" : "proceed straight in")} runway {RwSay(rw)}") + OnTheGo(rw) + (Dist(t.X, t.Z, thr.X, thr.Z) < 9 * NM || AheadR >= 0 ? ", reduce to final approach speed, " : ", ") +   // R46: with preceding traffic also at the 10 NM gate (otherwise he catches up with the speed from radar vectoring)
                       $"{Qnh(t)}, contact {Contact("Tower")}.", "Approach");   // R314: "report four mile final" is given by the tower (first call), not Approach
        }
        // Visual: to initial or overhead (reciprocal), never lower than 500 ft above terrain on the rest of the path
        var to = vecOver ? Ovh : OnCenterline(rw, InitialDist);
        nav = to; navName = vecOver ? "overhead" : "initial";
        double d = Dist(t.X, t.Z, to.X, to.Z), ft = PatternFt;
        for (double f = 0; f <= 1; f += NM / 2 / Math.Max(d, NM)) ft = Math.Max(ft, Math.Ceiling((F.TerrainFt(t.X + (to.X - t.X) * f, t.Z + (to.Z - t.Z) * f, 0.5) + 500) / 100) * 100);
        lastAltFt = ft;   // R256: remember stated altitude (the tower does not repeat it in the first call)
        if (Circling)   // R212: instrument approach to the reciprocal runway, circling at pattern altitude (FAA JO 7110.65 4-8-1, 4-8-6); no overhead in IMC
        {
            nav = null; navName = "circle";
            return Say($"{c}, {MilesTxt(d)} from the field, {(HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(vrw)) > 5 ? TurnTo(t, LandHdg(vrw)) + ", " : "")}" +
                       (CircleIfr ? $"maintain {Alt(ft)} until established{(Proc(vrw) == "ILS" ? " on the localizer" : "")}, cleared {Proc(vrw)} approach runway {RwSay(vrw)}, circle to runway {RwSay(rw)}"
                                  : $"on course, maintain {Alt(ft)}, expect {Hand(rw)} downwind runway {RwSay(rw)}") +   // Review R212: without procedure no approach clearance to the reciprocal runway, the tower gives the pattern
                       $", {Qnh(t)}, contact {Contact("Tower")}.", "Approach");
        }
        return Say($"{c}, " + (direct ? $"proceed direct initial runway {RwSay(rw)}, {MilesTxt(d)}, "
                   : $"{(HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(vrw)) > 5 ? TurnTo(t, LandHdg(vrw)) + ", " : "")}on course, {MilesTxt(d)} from {(vecOver ? "the field" : "initial")}, ") +
                   $"{AltTo(t, ft)}, " +   // R323: same altitude not "descend and" again (R130)
                   (vecOver ? $"proceed overhead, {Hand(rw)} pattern runway {RwSay(rw)}, " : "") +
                   $"contact {Contact("Tower")}, report {navName}{(vecOver ? "" : " for overhead break")}.", "Approach");
    }
}
