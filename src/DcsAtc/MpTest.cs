namespace DcsAtc;

/// Simulated AI (--mptest) like DcsAtcMission.lua + DCS: arrival directly to the 8-NM final of the DCS runway, straight in, land, 60 s after
/// off the runway; WAIT/STACK: orbit at the location, RESUME: continue. Departure: START -> to the holding point, 20 s, takeoff, away after 10 NM.
class SimAi
{
    const double NM = 1852, Ft = 0.3048, Kt = 0.514444;
    public string Group = "", State = "", Field = "";
    public double X, Z, Alt, Hdg, Spd, Vs, HoldFt, HoldX, HoldZ, Until = -1, LandedAt = -1;   // Hdg degrees true, Spd/Vs m/s
    public bool InAir = true, Gone, Final;
    public int Side = 2;   // Side of the test airfield (N40: AI at an enemy airfield is diverted)
    public Traffic Tr() => new(Group.GetHashCode(), "FA-18C_hornet", X, Z, Alt, Hdg * Math.PI / 180, Spd, Group, $"{State}:{Field}", InAir, Side, Vs);

    public void Cmd(string[] c, double now)   // WAIT;g;sec;ft  STACK;g;ft  RESUME;g  START;g
    {
        if (c[0] == "WAIT" && State == "arr") { State = "arr-hold"; (HoldX, HoldZ, HoldFt) = (X, Z, double.Parse(c[3])); }
        if (c[0] == "STACK" && State == "arr-hold") HoldFt = double.Parse(c[2]);
        if (c[0] == "RESUME" && State == "arr-hold") State = "arr";
        if (c[0] == "START" && State == "dep") State = "dep-go";
    }

    void Fly(double hdg, double alt, double spd, double rate = 3)
    {
        Hdg = (Hdg + Math.Clamp(((hdg - Hdg) % 360 + 540) % 360 - 180, -rate, rate) + 360) % 360;
        Alt += Vs = Math.Clamp(alt - Alt, -10, 10);
        Spd += Math.Clamp(spd - Spd, -2, 2);
        X += Spd * Math.Cos(Hdg * Math.PI / 180);
        Z += Spd * Math.Sin(Hdg * Math.PI / 180);
    }
    static double Brg(double x1, double z1, double x2, double z2) => (Math.Atan2(z2 - z1, x2 - x1) * 180 / Math.PI + 360) % 360;

    public void Step(Airfield f, RwyEnd e, double now)
    {
        double a = -((X - e.ThrX) * e.Dx + (Z - e.ThrZ) * e.Dz), l = (X - e.ThrX) * e.Dz - (Z - e.ThrZ) * e.Dx;
        double Track() => e.Hdg + Math.Clamp(l / NM * 60, -45, 45);
        switch (State)
        {
            case "arr" when InAir:
                var p8 = e.At(8 * NM, 0);
                if (!Final && Math.Sqrt((X - p8.X) * (X - p8.X) + (Z - p8.Z) * (Z - p8.Z)) < NM) Final = true;
                if (!Final) Fly(Brg(X, Z, p8.X, p8.Z), f.Elev + 2500 * Ft, 250 * Kt);
                else
                {
                    Fly(Track(), f.Elev + Math.Max(a, 0) * Math.Tan(3 * Math.PI / 180), 150 * Kt);
                    if (a < 0 && Alt <= f.Elev + 1) { InAir = false; LandedAt = now; Alt = f.Elev; }
                }
                break;
            case "arr":   // roll out, then off the runway (taxiway), parked after 60 s
                Spd = Math.Max(0, Spd - 3);
                X += Spd * Math.Cos(Hdg * Math.PI / 180); Z += Spd * Math.Sin(Hdg * Math.PI / 180);
                if (Spd == 0 && Math.Abs(l) < 100) { X += e.Dz * 150; Z -= e.Dx * 150; }
                if (now - LandedAt > 60) Gone = true;
                break;
            case "arr-hold":
                double r = Math.Sqrt((X - HoldX) * (X - HoldX) + (Z - HoldZ) * (Z - HoldZ));
                Fly(r > 3 * NM ? Brg(X, Z, HoldX, HoldZ) : Hdg - 90, HoldFt * Ft, 230 * Kt, 2.75);   // Left-hand circle around the holding point
                break;
            case "dep-go" when !InAir:
                var hs = e.At(0, 150);   // Holding point next to the threshold
                if (Until < 0)
                {
                    double d = Math.Sqrt((X - hs.X) * (X - hs.X) + (Z - hs.Z) * (Z - hs.Z));
                    if (d > 10) { Hdg = Brg(X, Z, hs.X, hs.Z); Spd = 8; double s = Math.Min(8, d); X += s * Math.Cos(Hdg * Math.PI / 180); Z += s * Math.Sin(Hdg * Math.PI / 180); }
                    else { Spd = 0; Until = now + 20; }
                }
                else if (now > Until)   // Line-up and takeoff roll (DCS does not wait for players)
                {
                    if (Spd == 0) (X, Z) = (e.ThrX, e.ThrZ);
                    Hdg = e.Hdg; Spd += 3;
                    X += Spd * e.Dx; Z += Spd * e.Dz;
                    if (Spd > 75) InAir = true;
                }
                break;
            case "dep-go":
                Fly(e.Hdg, f.Elev + 5000 * Ft, 150);
                if (Math.Sqrt((X - f.X) * (X - f.X) + (Z - f.Z) * (Z - f.Z)) > 10 * NM) Gone = true;
                break;
        }
    }
}

static partial class Program
{
    const double SimFt = 0.3048;

    /// DcsAtc --mptest [airfield|*] [vfr|ifr|*] [players] [AI arr] [AI dep]: several players (SimPilot, staggered first calls from 4 directions) and AI traffic
    /// simultaneously through the real app logic (TickAll/Request: order, holding, AI control). Checks: all land or take off (no hang),
    /// no close approach (< 0.5 NM and 300 ft), no landing on an occupied runway, no go-arounds/warnings. A single run shows the radio traffic.
    public static int MpTest(string[] args)
    {
        string A(int i) => args.Length > i ? args[i] : "*";
        int np = int.TryParse(A(2), out var n2) ? n2 : 3, na = int.TryParse(A(3), out var n3) ? n3 : 3, nd = int.TryParse(A(4), out var n4) ? n4 : 1;
        var runs = (from f in Fields where A(0) == "*" || f.Name.Contains(A(0), StringComparison.OrdinalIgnoreCase)
                    from ifr in new[] { false, true } where A(1) == "*" || A(1) == (ifr ? "ifr" : "vfr")
                    select (f, ifr)).ToList();
        SimLog = new();
        SimSec = 0;
        int bad = 0;
        foreach (var (f, ifr) in runs)
        {
            var (ok, info, log) = MpRun(f, ifr, np, na, nd);
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {f.Name} {(ifr ? "IFR" : "VFR")}: {info}");
            if (runs.Count == 1 || Environment.GetEnvironmentVariable("MPLOG") == "1" && !ok) log.ForEach(l => Console.WriteLine("    " + l));
        }
        Console.WriteLine($"{runs.Count - bad}/{runs.Count} OK");
        return bad == 0 ? 0 : 1;
    }

    static readonly string[] MpBad = { "check runway", "check altitude", "low altitude alert", "expedite", "wrong side", "negative", "not following", "go around", "too low", "unable", "say again", "traffic alert", "no transmissions" };   // R271: PAR lost-comm only with talk-down

    static (bool Ok, string Info, List<string> Log) MpRun(Airfield f, bool ifr, int np, int na, int nd)
    {
        // reset app state (static)
        Pilots.Clear(); ArrQueue.Clear(); Holds.Clear(); aiSent.Clear(); lastAiStart.Clear(); AiCallsign.Clear(); aiStage.Clear(); AiGid.Clear();
        AiFuel.Clear(); aiStackFt.Clear(); fuelTold.Clear(); Atis.Clear();
        while (SayQueue.TryTake(out _)) { }
        SimLog!.Clear();
        double t0 = SimSec += 100;
        // Wind 10 kt from the main direction (DCS runway = main in light wind, otherwise headwind)
        var main = f.End(f.Main);
        (double X, double Z) wind = (-5 * main.Dx, -5 * main.Dz);
        Wx = new() { [f.Name] = (wind.X, wind.Z, 15, 101325 * Math.Pow(1 - 2.25577e-5 * (f.Elev + 2), 5.25588)) };   // Standard atmosphere: QNH 1013
        Sky = ifr ? (true, 300, 3000) : (false, 0, 80000);
        var rw = FieldRunway(f);
        var e = f.End(rw);
        var log = new List<string>();
        var errs = new List<string>();
        var ais = new List<SimAi>();
        for (int i = 1; i <= na; i++)   // like spawnArr: mission start one after another from one direction 10, 14 NM (1200/1500 m), plus continuously from 50 NM (4000–6500 m)
        {
            bool late = i > 2;
            // +30: approach direction of the simulation; K26 shifted the reference point by up to 1 NM, which re-rolls chance hits (departure rolls blind into the landing)
            double b = ((late ? i * 137 : 0) + f.Name.Length * 31 + 30) % 360 * Math.PI / 180, r = late ? 50 * NM : (6 + 4 * i) * NM;
            ais.Add(new SimAi { Group = $"ARR{i}", State = "arr", Field = f.Name, Side = f.Side > 0 ? f.Side : 2, X = f.X + r * Math.Cos(b), Z = f.Z + r * Math.Sin(b),
                                Alt = late ? 5000 : f.Elev + 900 + 300 * i, Hdg = (b * 180 / Math.PI + 180) % 360, Spd = 250 * 0.514444 });
            AiCallsign[$"ARR{i}"] = $"Dodge {i}1";
        }
        for (int i = 1; i <= nd; i++)
        {
            var pk = e.At(-e.Len / 2, 600 + 50 * i);
            ais.Add(new SimAi { Group = $"DEP{i}", State = "dep", Field = f.Name, Side = f.Side > 0 ? f.Side : 2, X = pk.X, Z = pk.Z, Alt = f.Elev, InAir = false });
            AiCallsign[$"DEP{i}"] = $"Ford {i}1";
        }
        AiHook = line =>
        {
            var c = line.Split(';');
            if (c.Length >= 2 && ais.FirstOrDefault(a => a.Group == c[1]) is { } ai) ai.Cmd(c, SimSec);
        };
        string[] css = { "Enfield 1-1", "Colt 1-1", "Uzi 1-1", "Springfield 1-1", "Pontiac 1-1", "Chevy 1-1" };
        var sims = new List<(Pilot P, SimPilot S, double CallAt)>();
        for (int i = 0; i < np; i++)
        {
            double dir = (i * 90 + 20) % 360, x = f.X + 30 * NM * Math.Cos(dir * Math.PI / 180), z = f.Z + 30 * NM * Math.Sin(dir * Math.PI / 180);
            double ft = Math.Max(8000 + 1000 * i, Math.Ceiling(f.MvaLeg(x, z, f.X, f.Z) / 1000) * 1000 + 1000 * i);
            var sp = new SimPilot(f, x, z, ft * SimFt, Tower.Bearing(x, z, f.X, f.Z), 300 * 0.514444, wind, false) { Cs = css[i % css.Length] };
            var p = new Pilot { Unit = $"U{i + 1}", Callsign = sp.Cs, Gid = 100 + i, Type = "FA-18C_hornet", Coalition = f.Side > 0 ? f.Side : 2, Id = (uint)(i + 1) };
            Pilots[p.Unit] = p;
            sims.Add((p, sp, t0 + 1 + 15 * i));
        }
        var landedAt = new Dictionary<string, double>();
        var conflict = new HashSet<string>();
        int goArounds = 0;
        void Route(double now)
        {
            log.AddRange(SimLog!.Where(s => !s.StartsWith("[ATIS]")).Select(s => $"{now - t0,5:0}s   {s}"));   // App log (airfield change, AI commands)
            SimLog!.Clear();
            while (SayQueue.TryTake(out var tx))
            {
                if (tx.Role == "Info") continue;
                var who = tx.Pilot ? tx.Station : tx.Gid == 0 ? "(alle)" : sims.FirstOrDefault(s => s.P.Gid == tx.Gid).P?.Callsign ?? "?";
                var s = sims.FirstOrDefault(s => s.P.Gid == tx.Gid && !tx.Pilot);
                log.Add($"{now - t0,5:0}s {(tx.Pilot ? "" : tx.Role + " -> ")}{who}{(s.S is { } ss ? $" [{ss.Alt / SimFt:0} ft, {Dist(ss.X, ss.Z, f.X, f.Z) / NM:0.0} NM, {ss.Mode}]" : "")}: {tx.Text}");
                if (tx.Pilot || tx.Gid == 0) continue;
                if (s.S == null) continue;
                if (tx.Text.Contains("traffic alert", StringComparison.OrdinalIgnoreCase))   // who: nearest aircraft (R214: "Traffic alert, …")
                    log.Add($"{now - t0,5:0}s   Verkehr (selbst {s.S.Hdg:000}°): " + string.Join(", ", sims.Where(o => o.S != s.S && Pilots.ContainsKey(o.P.Unit)).Select(o => (N: $"{o.S.Cs} {o.P.Active?.Phase} {o.S.Mode} {o.S.Hdg:000}°", o.S.X, o.S.Z, o.S.Alt))
                        .Concat(ais.Where(a => !a.Gone && a.InAir).Select(a => (N: $"{a.Group} {a.State}{(a.Final ? " final" : "")} {a.Hdg:000}°", a.X, a.Z, a.Alt))).OrderBy(o => Dist(o.X, o.Z, s.S.X, s.S.Z)).Take(2)
                        .Select(o => $"{o.N} {Dist(o.X, o.Z, s.S.X, s.S.Z) / NM:0.0} NM {(o.Alt - s.S.Alt) / SimFt:+0;-0} ft")));
                if (MpBad.FirstOrDefault(b => tx.Text.Contains(b, StringComparison.OrdinalIgnoreCase)) is { } bw) errs.Add($"{s.P.Callsign} \"{bw}\" @{now - t0:0}s");
                if (tx.Text.Contains("leave the hold") && tx.Text.Contains("climb and maintain")) errs.Add($"{s.P.Callsign} \"leave the hold … climb\" @{now - t0:0}s");   // R272: holding altitude above the terrain of the approach path
                if (tx.Text.Contains("go around")) goArounds++;
                s.S.Hear(tx.Text, now);
            }
        }
        string? Busy(double x, double z, string self)   // Runway occupied: another aircraft on the ground on the runway (who, where), otherwise null
        {
            bool On(double ox, double oz) { double a = -((ox - e.ThrX) * e.Dx + (oz - e.ThrZ) * e.Dz), l = (ox - e.ThrX) * e.Dz - (oz - e.ThrZ) * e.Dx; return a < 100 && a > -e.Len - 100 && Math.Abs(l) < 60; }
            var who = ais.Where(a => !a.Gone && !a.InAir && a.Group != self && On(a.X, a.Z) && (a.State != "dep" && a.State != "dep-go" || a.Until > 0 && SimSec > a.Until))
                         .Select(a => $"{a.Group} {a.State} a={-((a.X - e.ThrX) * e.Dx + (a.Z - e.ThrZ) * e.Dz):0} l={(a.X - e.ThrX) * e.Dz - (a.Z - e.ThrZ) * e.Dx:0} spd={a.Spd:0}")
                         .Concat(sims.Where(s => s.P.Unit != self && Pilots.ContainsKey(s.P.Unit) && s.S.Landed && On(s.S.X, s.S.Z)).Select(s => s.S.Cs)).ToList();
            return who.Count > 0 ? string.Join(" | ", who) : null;
        }
        for (SimSec = t0; SimSec < t0 + 3600; SimSec++)
        {
            double now = SimSec;
            foreach (var (p, s, at) in sims.Where(s => Pilots.ContainsKey(s.P.Unit)))
            {
                p.Tel = s.Tel() with { Gear = s.Mode is "fin" or "dw" || s.Glide ? 1 : 0 };
                p.Seen = Clock;
            }
            Ai = ais.Where(a => !a.Gone).Select(a => a.Tr()).ToList();
            foreach (var a in ais) AiFuel[a.Group] = 0.8;
            stateTime = Clock;
            TickAll();
            Route(now);
            if ((now - t0) % 30 == 0 && ArrQueue.TryGetValue(f.Name, out var dq))   // Situation: order, distance/holding per entry
                log.Add($"{now - t0,5:0}s   Reihenfolge: " + string.Join(", ", dq.Select((k, i) => $"{k}{(PosOf(k, Pilots.Values.ToList()) is { } dp ? $" {Dist(dp.X, dp.Z, f.X, f.Z) / NM:0}NM" : "")}{(Holds.ContainsKey(k) ? " H" : "")}{(SpacedAt(dq, i, f, Pilots.Values.ToList()) ? "" : " wartet")}")));
            foreach (var (p, s, at) in sims.Where(s => Pilots.ContainsKey(s.P.Unit)))
            {
                string? call = now == at ? $"{f.StationOf("Approach")}, {s.Cs}, inbound for landing" : s.Call(now);
                if (call == null) continue;
                log.Add($"{now - t0,5:0}s {s.Cs}: {call}");
                Request(p, call, "SIM", false);
                Route(now);
            }
            foreach (var (p, s, at) in sims.Where(s => Pilots.ContainsKey(s.P.Unit)))
            {
                bool was = s.Landed;
                if (now > at) s.Step(now);
                if (s.Landed && !was)
                {
                    landedAt[s.Cs] = now - t0;
                    log.Add($"{now - t0,5:0}s {s.Cs} aufgesetzt");
                    if (Busy(s.X, s.Z, p.Unit) is { } bw) errs.Add($"{s.Cs} landet auf belegter Bahn ({bw}) @{now - t0:0}s");
                }
                if (s.Landed && s.Ias < 15) Pilots.Remove(p.Unit);   // Left the runway, parked
            }
            foreach (var a in ais.Where(a => !a.Gone))
            {
                bool up = a.InAir;
                a.Step(f, e, now);
                if (up && !a.InAir)
                {
                    log.Add($"{now - t0,5:0}s KI {a.Group} aufgesetzt");
                    if (Busy(a.X, a.Z, a.Group) is { } bw) errs.Add($"KI {a.Group} landet auf belegter Bahn ({bw}) @{now - t0:0}s");
                }
                if (a.Gone) log.Add($"{now - t0,5:0}s KI {a.Group} {(a.State == "dep-go" ? "weg (Abflug)" : "abgestellt")}");
            }
            // Close approach: all airborne pairwise
            var air = sims.Where(s => Pilots.ContainsKey(s.P.Unit) && !s.S.Landed).Select(s => (N: s.S.Cs, s.S.X, s.S.Z, s.S.Alt))
                .Concat(ais.Where(a => !a.Gone && a.InAir).Select(a => (N: a.Group, a.X, a.Z, a.Alt))).ToList();
            for (int i = 0; i < air.Count; i++)
                for (int j = i + 1; j < air.Count; j++)
                    if (Dist(air[i].X, air[i].Z, air[j].X, air[j].Z) < 0.5 * NM && Math.Abs(air[i].Alt - air[j].Alt) < 300 * SimFt && conflict.Add(air[i].N + "/" + air[j].N))
                        errs.Add($"Annäherung {air[i].N}/{air[j].N} @{now - t0:0}s");
            if (Pilots.Count == 0 && ais.All(a => a.Gone)) break;
        }
        foreach (var (p, s, _) in sims.Where(s => !landedAt.ContainsKey(s.S.Cs))) errs.Add($"{s.Cs} nicht gelandet ({p.Active?.Phase}, {s.Mode})");
        foreach (var a in ais.Where(a => !a.Gone)) errs.Add($"KI {a.Group} hängt ({a.State}{(a.InAir ? ", Luft" : "")})");
        AiHook = null;
        var info = $"{SimSec - t0:0} s, Bahn {rw}, gelandet {string.Join(" ", landedAt.Select(kv => $"{kv.Key.Split(' ')[0]}@{kv.Value:0}"))}, Durchstarts {goArounds}";
        return (errs.Count == 0, errs.Count == 0 ? info : string.Join(", ", errs.Distinct()) + " | " + info, log);
    }
}
