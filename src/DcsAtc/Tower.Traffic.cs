using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= Procedures
    /// Traffic pattern always south of the runway: 07 right-hand pattern, 25 left-hand pattern.
    string Hand(string rw) => F.Hand(rw);

    /// Entry point: North/South always, West only for 07, East only for 25. Otherwise the nearest.
    static List<string> Allowed(string rw) => new() { "north", "south", rw == "07" ? "west" : "east" };

    string AssignEntry(Telemetry? t, string rw, string? wanted)
    {
        var allowed = Allowed(rw);
        if (wanted != null && allowed.Contains(wanted)) return wanted;
        if (t == null) return allowed[2];
        return allowed.OrderBy(k => Dist(t.X, t.Z, Crp[k].X, Crp[k].Z)).First();
    }

    string Route(string rw) => home == null ? "" : " via " + (rw == "07" ? home.To07 : home.To25);

    bool HomeNorth => home?.Name is "Ramp North" or "Ramp West";

    /// Tower after landing: vacate runway (Kutaisi: taxiway towards the apron side).
    string Vacate(string rw)
    {
        landedRw = rw;
        if (!F.Charted || home == null) return "Vacate runway when able.";
        if (rw == "25") return HomeNorth ? "Vacate right via Alpha or Bravo." : "Vacate left via Whiskey.";
        return HomeNorth ? "Vacate left via Charlie or Delta." : "Vacate right via Echo.";
    }
    string landedRw = "";

    /// Ground: taxiway to the apron after vacating the runway.
    string ParkRoute() =>
        HomeNorth ? " via November"
        : (landedRw == "25" && home?.Name == "Ramp East") || (landedRw == "07" && home?.Name == "Ramp South") ? " via Sierra" : "";

    /// Landing evaluation at touchdown: glidepath and centreline on final (0.3–3 NM), touchdown point, sink rate.
    string Grade(Telemetry t)
    {
        var e = F.End(RwyOf(t));
        double past = -Approach(t.X, t.Z, e.Name).along, fpm = Math.Max(0, -lastVs) / Ft * 60;
        var parts = new List<(int Score, string Text)>();
        if (glide.Count >= 5)
        {
            double dev = glide.Average(g => g.Dev), abs = glide.Average(g => Math.Abs(g.Dev)), lat = glide.Average(g => g.Lat);
            parts.Add((abs <= 50 ? 25 : abs <= 100 ? 18 : abs <= 200 ? 10 : 3, L("Gleitpfad", "Glide path") + $" Ø {dev:+0;-0;0} ft"));
            parts.Add((lat <= 15 ? 25 : lat <= 35 ? 18 : lat <= 70 ? 10 : 3, L("Mittellinie", "Centerline") + $" Ø {lat:0} m"));
        }
        parts.Add((past < 0 ? 0 : past is >= 100 and <= 500 ? 25 : past <= 800 ? 15 : 5,
                   past < 0 ? L($"Aufsetzpunkt {-past:0} m VOR der Schwelle", $"Touchdown {-past:0} m SHORT of the threshold") : L($"Aufsetzpunkt {past:0} m hinter der Schwelle", $"Touchdown {past:0} m past the threshold")));
        parts.Add((fpm <= 300 ? 25 : fpm <= 500 ? 20 : fpm <= 700 ? 12 : 3, L("Sinkrate", "Sink rate") + $" {fpm:0} ft/min"));
        glide.Clear();
        int score = (int)Math.Round(parts.Sum(p => p.Score) * 100.0 / (parts.Count * 25));
        var grade = score >= 85 ? "OK" : score >= 70 ? "Fair" : score >= 50 ? "No grade" : "Cut";
        return L($"Landebewertung {F.Name} Bahn {e.Name}", $"Landing grade {F.Name} runway {e.Name}") + $": {grade} ({score}/100) – {string.Join(", ", parts.Select(p => p.Text))}.";
    }

    Ramp? NearestRamp(Telemetry? t) =>
        t == null ? F.Ramps.FirstOrDefault() : F.Ramps.OrderBy(r => Dist(t.X, t.Z, r.X, r.Z)).FirstOrDefault();

    // ======================================================================= Traffic
    /// Runway occupied: traffic on the active runway (A8) or another takeoff clearance before the takeoff roll (A9).
    bool TrafficOnRunway(IReadOnlyList<Traffic> traffic) => RunwayClaimed > 0 || RwyTraffic(traffic) != null;
    Traffic? RwyTraffic(IReadOnlyList<Traffic> traffic) => traffic.FirstOrDefault(a => Blocks(F, Runway, a.X, a.Z, a.AltMsl - FieldElev, a.Hdg, a.Speed));
    string BusyWhy(IReadOnlyList<Traffic> traffic) => RwyTraffic(traffic) == null ? $"traffic departing runway {RwSay(Runway)}" : "traffic on runway";

    /// A8: occupies only runway rw including reciprocal: on the ground taxiing up to 100 m beyond the ends (45 m lateral), between the thresholds (30 m) also stationary.
    /// Airborne or in direction rw more than 6000 ft past the threshold (takeoff roll, rollout) counts as clear (FAA JO 7110.65 3-10-3). hdg in radians, speed m/s.
    public static bool Blocks(Airfield f, string rw, double x, double z, double agl, double hdg, double speed)
    {
        var e = f.End(rw);
        double al = Math.Abs((x - e.CX) * e.Dx + (z - e.CZ) * e.Dz), lat = Math.Abs((x - e.CX) * e.Dz - (z - e.CZ) * e.Dx);
        if (agl >= 15 || !(al < e.Len / 2 + 100 && lat < 45 && speed > 3 || al < e.Len / 2 && lat < 30)) return false;
        return !(speed > 3 && HdgDiff(hdg * 180 / Math.PI, e.Hdg) < 30 && (x - e.ThrX) * e.Dx + (z - e.ThrZ) * e.Dz > 6000 * Ft);
    }

    /// Program: player on final up to maxDist (AI takeoff clearance waits, A9).
    public bool OnFinalWithin(Telemetry t, double maxDist) => OnFinal(t.X, t.Z, t.Hdg, t.Agl, Runway, maxDist);

    bool TrafficOnFinal(IReadOnlyList<Traffic> traffic, string rw, double maxDist) =>
        traffic.Any(a => a.Speed > 30 && OnFinal(a.X, a.Z, a.Hdg, a.AltMsl - FieldElev, rw, maxDist));

    /// Traffic ahead of me with remaining distance to threshold, sorted by remaining distance: final (8 NM) and traffic pattern (A45: airborne, up to 4 NM from the airfield,
    /// up to pattern altitude + 500 ft, on the pattern side; downwind adds base and final, departure/upwind does not count).
    IEnumerable<(Traffic a, double rest)> Ahead(IReadOnlyList<Traffic> traffic, string rw)
    {
        var res = new List<(Traffic a, double rest)>();
        foreach (var a in traffic.Where(a => a.Speed > 30))
        {
            var (along, lat) = Approach(a.X, a.Z, rw);
            double hd = HdgDiff(a.Hdg * 180 / Math.PI, LandHdg(rw));
            if (OnFinalTr(a, rw)) res.Add((a, along));   // R332: also long final up to 12 NM
            else if (a.InAir && Dist(a.X, a.Z, CX, CZ) < 4 * NM && a.AltMsl / Ft <= PatternFt + 500 && PatternOffset(a.X, a.Z, rw) > 200 && !(hd < 60 && along <= 0))
                res.Add((a, lat + (hd > 120 ? Math.Max(along, 2.5 * NM) + Math.Max(2.5 * NM - along, 0)
                                 : along <= 0 ? 6 * NM - along   // Base: rest of base (~1 NM) + downwind, base, final
                                 : along)));
        }
        return res.OrderBy(r => r.rest);
    }

    /// Traffic ahead of me in the landing sequence (final and traffic pattern), sorted by remaining distance. brk: still on initial before the break, the pattern still lies ahead of me.
    List<(Traffic a, double rest)> AheadOfMe(Telemetry? t, IReadOnlyList<Traffic> traffic, string rw, bool brk = false)
    {
        double mine = t == null ? double.MaxValue : Math.Max(Approach(t.X, t.Z, rw).along, 0.5 * NM);
        if (t != null && !OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 8 * NM))   // still in the pattern; already turning in (heading to runway): remaining distance
            mine = HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 90 && Approach(t.X, t.Z, rw) is var (ma, ml) ? Math.Max(ma, 0) + Math.Abs(ml) : 6 * NM;
        if (brk && t != null) mine += 6 * NM;
        // brk: final traffic behind me (e.g. on the same initial) stays behind me, the break surcharge applies only versus the pattern
        double own = t == null ? double.MaxValue : Approach(t.X, t.Z, rw).along;
        return Ahead(traffic, rw).Where(r => r.rest < mine && !(brk && r.rest > own && OnFinal(r.a.X, r.a.Z, r.a.Hdg, r.a.AltMsl - FieldElev, rw, 8 * NM))).ToList();
    }

    IEnumerable<Traffic> FinalTraffic(IReadOnlyList<Traffic> traffic, string rw, double maxDist) =>
        traffic.Where(a => a.Speed > 30 && OnFinal(a.X, a.Z, a.Hdg, a.AltMsl - FieldElev, rw, maxDist))
               .OrderBy(a => Approach(a.X, a.Z, rw).along);

    /// Sequence + preceding traffic (the traffic directly ahead of me, final or pattern).
    (int no, Traffic? lead) Sequence(Telemetry? t, IReadOnlyList<Traffic> traffic, string rw, bool brk = false)
    {
        var ahead = AheadOfMe(t, traffic, rw, brk);
        return (1 + ahead.Count, ahead.LastOrDefault().a);
    }

    /// A45: preceding traffic for "follow the ...": pattern traffic with leg ("Viper on downwind"), otherwise Describe.
    string Follow(Traffic a, string rw)
    {
        if (OnFinalTr(a, rw) || PatternOffset(a.X, a.Z, rw) <= 200) return Describe(a, rw);
        double deg = a.Hdg * 180 / Math.PI, s = Hand(rw) == "left hand" ? 1 : -1;
        string leg = HdgDiff(deg, LandHdg(rw)) > 120 ? "downwind" : s * Math.Sin((deg - LandHdg(rw)) * Math.PI / 180) > 0.5 ? $"{Hand(rw).Split(' ')[0]} base" : "crosswind";
        return $"{Ops.TypeSay(a.Type)} on {leg}";   // R257: preceding traffic by radio name (Viper, Hornet), not DCS raw name
    }

    /// "C 130 on 3 mile final" / "Viper on the runway" / "Su 25T, 3 miles south of the field, 1500 feet" (spoken name: Ops.TypeSay)
    string Describe(Traffic a, string rw)
    {
        var type = Ops.TypeSay(a.Type);
        if (OnFinalTr(a, rw))
            return $"{type} on {Miles(Approach(a.X, a.Z, rw).along)} mile final";
        if (OnRunwayPos(a.X, a.Z) && a.AltMsl - FieldElev < 15) return $"{type} on the runway";
        return $"{type}, {Miles(Dist(a.X, a.Z, CX, CZ))} miles {Dir8(Bearing(CX, CZ, a.X, a.Z))} of the field, " +
               $"{Math.Round(a.AltMsl / Ft / 100) * 100:0} feet";
    }

    /// Traffic advisory for the nearest traffic in the airfield area (empty if none).
    string TrafficNote(IReadOnlyList<Traffic> traffic, string rw)
    {
        if (role == "Approach") traffic = Own(traffic);   // A52: Approach names no enemies, the tower (pattern) all
        var a = traffic.Where(a => a.Speed > 30 && Dist(a.X, a.Z, CX, CZ) < 6 * NM && a.AltMsl - FieldElev < 1500)
                       .OrderBy(a => Dist(a.X, a.Z, CX, CZ)).FirstOrDefault();
        return a == null ? "" : $" Traffic, {Describe(a, rw)}.";
    }
}
