using static DcsAtc.Tower;

namespace DcsAtc;

/// LK9: Air Battle Manager per side – the AWACS distributes the enemy groups among the fighters on station instead of letting each flight commit on its own.
/// Brevity per ATP 1-02.1: COMMIT, RECOMMIT, SKIP IT, RESET, MAINTAIN (CAP). Pure logic: candidates (Update) and groups (Tick) in, events per flight (Take) out.
/// Assignment stays fixed (no flip-flopping) until the group is gone (splash/faded -> reset) or the flight can no longer continue (RTB, bingo, winchester -> silently free).
/// Used by Ops (player with CAP tasking, LK5) and Flights (AI fighters); LK7 "north group, Ford one targeted" reads the same table (TargetedBy).
static class Abm
{
    const double NM = 1852;
    public static double CommitNm = 60;    // LD2: group hot within this range of the Fox 3 flight -> assignment (sorted ahead of the enemy's firing range; rule of thumb)
    public static double CommitNm2 = 40;   // LD2: Fox 2 only
    public static double TargetSec = 300;  // Assignment expires without a sign of life from the flight (Update/Want)
    public static double ResetSec = 60;    // after "reset" back on station, only then planned again ("recommit")
    const double StaleSec = 10, EventSec = 30;

    /// Candidate: a flight on CAP. Flight = spoken flight name ("Ford one", "Dagger one"), the key everywhere; Fuel 0–1 (-1 unknown); Fox3/Fox2: air-to-air missiles on board.
    public record Fighter(string Flight, double X, double Z, double Fuel = -1, int Fox3 = 1, int Fox2 = 0, bool OnStation = true);
    /// Enemy group in the side's radar picture: units (Traffic.Id), name (LK3), lead position and heading (rad), count.
    public record Group(int[] Ids, string? Label, double X, double Z, double Hdg, int Count);
    /// Event for a flight: commit, recommit, maintain, reset (label "picture clean" when nothing is left), skip (By: who has the group).
    public record Event(string Kind, string Flight, int[] Ids, string Label, string? By = null);

    sealed class Asg { public string Flight = ""; public HashSet<int> Ids = new(); public string Label = ""; public double Touch; public double Hdg = double.NaN, DragAt = double.NaN; }   // LD8: last reported heading, since when drag
    sealed class Side
    {
        public readonly Dictionary<string, (Fighter F, double At)> Cand = new();
        public readonly List<Asg> Asg = new();
        public readonly Dictionary<string, double> ResetAt = new();
        public readonly HashSet<string> Maintained = new();
        public readonly Dictionary<string, List<(Event E, double At)>> Inbox = new();
        public readonly HashSet<int> Leak = new(), Help = new();   // LD2: leakers (Ops, LK8) get a flight regardless of aspect; LD16: reinforcement requested (second flight)
        public readonly Dictionary<string, double> Hold = new();   // LD14: "unable"/no answer -> not re-planned for 60 s
    }
    static readonly Dictionary<int, Side> sides = new();
    static Side S(int side) => sides.TryGetValue(side, out var s) ? s : sides[side] = new();

    /// Sign of life of a flight on CAP (every tick, Flights for AI leads, Ops for players with "on station"). Without an update for 10 s it is no longer a candidate.
    public static void Update(int side, Fighter f, double now)
    {
        var s = S(side);
        s.Cand[f.Flight] = (f, now);
        foreach (var a in s.Asg.Where(a => a.Flight == f.Flight)) a.Touch = now;
    }

    /// Flight leaves (RTB, check out, off station): assignments freed, no longer a candidate.
    public static void Release(int side, string flight)
    {
        var s = S(side);
        s.Cand.Remove(flight); s.Asg.RemoveAll(a => a.Flight == flight);
    }

    static bool Ok(Fighter f) => f.OnStation && (f.Fuel < 0 || f.Fuel >= 0.22) && f.Fox3 + f.Fox2 > 0;   // bingo 0.22 as in Flights
    static bool Hot(Group g, Fighter f) => HdgDiff(g.Hdg * 180 / Math.PI, Bearing(g.X, g.Z, f.X, f.Z)) <= 70;   // hot or flank on the flight
    static double Reach(Fighter f) => (f.Fox3 > 0 ? CommitNm : CommitNm2) * NM;
    static double Score(Group g, Fighter f) => Dist(f.X, f.Z, g.X, g.Z) * (f.Fox3 > 0 ? 1 : 1.5) * (f.Fuel is >= 0 and < 0.35 ? 1.5 : 1);   // Situation, weapons, fuel
    static void Post(Side s, Event e, double now)
    {
        if (!s.Inbox.TryGetValue(e.Flight, out var l)) s.Inbox[e.Flight] = l = new();
        l.RemoveAll(x => now - x.At > EventSec);
        l.Add((e, now));
    }

    /// LD8 (ATP 1-02.1 FLANK/BEAM/DRAG, RESET): the assigned group turns 45° or more -> "Ford one, Overlord, north group, flank west." to the flight;
    /// if it pulls away for over 2 min (drag) and is farther than the commit range + 10 NM (not a leaker) -> reset. true = assignment gone.
    static bool Change(Side s, Asg a, Group g, Fighter f, double now)
    {
        double hdg = g.Hdg * 180 / Math.PI, off = HdgDiff(hdg, Bearing(g.X, g.Z, f.X, f.Z));
        bool drag = off > 120 && Dist(f.X, f.Z, g.X, g.Z) > Reach(f) + 10 * NM && !s.Leak.Overlaps(a.Ids);
        a.DragAt = !drag ? double.NaN : double.IsNaN(a.DragAt) ? now : a.DragAt;
        if (now - a.DragAt > 120)
        {
            s.Asg.Remove(a); s.ResetAt[a.Flight] = now;
            Post(s, new("reset", a.Flight, a.Ids.ToArray(), a.Label), now);
            return true;
        }
        if (double.IsNaN(a.Hdg)) a.Hdg = hdg;
        else if (HdgDiff(hdg, a.Hdg) >= 45)
        {
            a.Hdg = hdg;
            Post(s, new("aspect", a.Flight, a.Ids.ToArray(), a.Label, off <= 30 ? "hot" : $"{(off <= 70 ? "flank" : off <= 120 ? "beam" : "drag")} {Ops.Card(hdg)}"), now);
        }
        return false;
    }

    /// Assignment per tick (whoever calls first computes; repeated calls per tick do no harm): finished groups -> reset, then each open group (most threatening first)
    /// to the best-suited free flight, large groups (3 or more) to two; free flights within range of an assigned group get "maintain CAP" once.
    public static void Tick(int side, IReadOnlyList<Group> groups, double now)
    {
        var s = S(side);
        foreach (var k in s.Cand.Where(c => now - c.Value.At > StaleSec).Select(c => c.Key).ToList()) s.Cand.Remove(k);
        foreach (var a in s.Asg.ToList())
        {
            var live = groups.Where(g => g.Ids.Any(a.Ids.Contains)).ToList();
            if (live.Count == 0)   // shot down or faded: back on station
            {
                s.Asg.Remove(a);
                if (s.Cand.TryGetValue(a.Flight, out var c) && Ok(c.F) && !s.Asg.Any(o => o.Flight == a.Flight))
                {
                    s.ResetAt[a.Flight] = now;
                    Post(s, new("reset", a.Flight, a.Ids.ToArray(), groups.Count == 0 ? "picture clean" : a.Label), now);
                }
                continue;
            }
            foreach (var g in live) a.Ids.UnionWith(g.Ids);   // Stragglers belong to it; ponytail: after a split the flight keeps both parts
            if (s.Cand.TryGetValue(a.Flight, out var cf) && Change(s, a, live[0], cf.F, now)) continue;
            if (s.Cand.TryGetValue(a.Flight, out var f) && !Ok(f.F) || now - a.Touch > TargetSec) s.Asg.Remove(a);
        }
        var free = s.Cand.Values.Select(c => c.F).Where(f => Ok(f) && !s.Asg.Any(a => a.Flight == f.Flight) && now - s.ResetAt.GetValueOrDefault(f.Flight, -1e9) >= ResetSec && now - s.Hold.GetValueOrDefault(f.Flight, -1e9) >= ResetSec).ToList();
        var fighters = s.Cand.Values.Select(c => c.F).ToList();
        if (fighters.Count == 0) return;
        bool Near(Group g, Fighter f) => Dist(f.X, f.Z, g.X, g.Z) < Reach(f) && (Hot(g, f) || s.Leak.Overlaps(g.Ids));
        var order = groups.OrderBy(g => s.Leak.Overlaps(g.Ids) ? 0 : 1).ThenBy(g => fighters.Min(f => Dist(f.X, f.Z, g.X, g.Z))).ToList();
        foreach (var g in order)
        {
            int need = (g.Count >= 3 || s.Help.Overlaps(g.Ids) ? 2 : 1) - s.Asg.Where(a => g.Ids.Any(a.Ids.Contains)).Select(a => a.Flight).Distinct().Count();
            foreach (var f in free.Where(f => Near(g, f)).OrderBy(f => Score(g, f)).Take(Math.Max(0, need)).ToList())
            {
                var label = g.Label ?? Ops.GroupLabel(side, g.Ids[0]) ?? "group";   // name only at assignment (LK3)
                s.Asg.Add(new Asg { Flight = f.Flight, Ids = g.Ids.ToHashSet(), Label = label, Touch = now, Hdg = g.Hdg * 180 / Math.PI });
                free.Remove(f);
                Post(s, new(s.ResetAt.Remove(f.Flight) ? "recommit" : "commit", f.Flight, g.Ids, label), now);
            }
        }
        foreach (var g in order)   // first all assignments, then hold the rest on station
            if (s.Asg.FirstOrDefault(a => g.Ids.Any(a.Ids.Contains)) is { } on)
                foreach (var f in free.Where(f => Near(g, f) && s.Maintained.Add($"{f.Flight}>{g.Ids.Min()}")))
                    Post(s, new("maintain", f.Flight, g.Ids, on.Label, on.Flight), now);
    }

    /// As above with the groups from the side's radar picture (Ops, LK3 names): this is how Flights calls it each scan.
    public static void Tick(int side, double now) => Tick(side, Ops.AbmGroups(side), now);

    /// The flight wants to take the group itself ("targeting north group", AI sorting): if another flight already has it (large group: two others), result = that flight's name
    /// and (post) a "skip it" for the flight (Ops answers itself: post false); otherwise it belongs to this flight from now on (also), result null.
    public static string? Want(int side, string flight, int[] ids, string? label, int count, double now, bool post = true)
    {
        var s = S(side);
        var others = s.Asg.Where(a => a.Flight != flight && ids.Any(a.Ids.Contains)).Select(a => a.Flight).Distinct().ToList();
        if (others.Count >= (count >= 3 ? 2 : 1))
        {
            if (post) Post(s, new("skip", flight, ids, label ?? "group", others[0]), now);
            return others[0];
        }
        if (s.Asg.FirstOrDefault(a => a.Flight == flight && ids.Any(a.Ids.Contains)) is { } mine) mine.Touch = now;
        else s.Asg.Add(new Asg { Flight = flight, Ids = ids.ToHashSet(), Label = label ?? "group", Touch = now });
        return null;
    }

    /// LD14 (ATP 1-02.1 UNABLE): flight declines or does not answer – assignment freed, not re-planned for 60 s; the group goes to the next flight on the next tick.
    public static void Unable(int side, string flight, double now) { var s = S(side); s.Asg.RemoveAll(a => a.Flight == flight); s.Hold[flight] = now; }
    /// LD16: The player fires on the group: it belongs to him (he wins, LD14), other flights on it (small group) get "skip it, … Dagger one targeted".
    public static void Claim(int side, string flight, int[] ids, string label, int count, double now)
    {
        var s = S(side);
        if (count < 3)
            foreach (var a in s.Asg.Where(a => a.Flight != flight && ids.Any(a.Ids.Contains)).ToList()) { s.Asg.Remove(a); Post(s, new("skip", a.Flight, ids, label, flight), now); }
        Want(side, flight, ids, label, 3, now, post: false);
    }
    /// LD16/LD15: Flight is defensive or requests reinforcement -> the group gets a second flight. LD2: leaker (Ops, LK8) -> assignment regardless of aspect, first.
    public static void Assist(int side, int[] ids) => S(side).Help.UnionWith(ids);
    public static void Leaker(int side, int[] ids) => S(side).Leak.UnionWith(ids);

    /// Who is engaging the group with this hostile unit (LK7 deconfliction "north group, Ford one targeted"); except: do not name the own flight. null = nobody.
    public static string? TargetedBy(int side, int unitId, string? except = null) =>
        sides.GetValueOrDefault(side)?.Asg.FirstOrDefault(a => a.Flight != except && a.Ids.Contains(unitId))?.Flight;
    /// Is the unit assigned to this flight (committed/targeted)? Then no more THREAT calls to it (ATP 1-02.1 THREAT: untargeted).
    public static bool Mine(int side, string flight, int unitId) => sides.GetValueOrDefault(side)?.Asg.Any(a => a.Flight == flight && a.Ids.Contains(unitId)) == true;
    /// LK8: positions of the flights on station (the defense line for LEAKER), empty = no CAP.
    public static List<(double X, double Z)> Caps(int side) =>
        sides.GetValueOrDefault(side)?.Cand.Values.Where(c => c.F.OnStation).Select(c => (c.F.X, c.F.Z)).ToList() ?? new();
    /// Collect the events for the flight (at most 30 s old).
    public static List<Event> Take(int side, string flight, double now)
    {
        if (sides.GetValueOrDefault(side)?.Inbox.Remove(flight, out var l) != true) return new();
        return l!.Where(x => now - x.At <= EventSec).Select(x => x.E).ToList();
    }
    /// AWACS call for the event; rest: ", BRAA …" for commit/recommit (attached to the listener, supplied by the caller).
    public static string Say(Event e, string aw, string rest = "") => e.Kind switch
    {
        "commit" or "recommit" => $"{e.Flight}, {aw}, {e.Kind} {e.Label}{rest}.",
        "maintain" => $"{e.Flight}, {aw}, maintain CAP.",
        "skip" => $"{e.Flight}, {aw}, skip it, {e.Label} {e.By} targeted.",
        "reset" => e.Label == "picture clean" ? $"{e.Flight}, {aw}, picture clean, reset." : $"{e.Flight}, {aw}, reset.",
        "aspect" => $"{e.Flight}, {aw}, {e.Label}, {e.By}.",
        _ => "",
    };
    /// New mission / test: forget everything.
    public static void Reset() => sides.Clear();

    // ------------------------------------------------------------ Self-test
    public static void SelfTest(Action<bool, string> check)
    {
        Reset();
        double N = NM;
        // Enemy 60 NM north, heading south (hot on the CAP at 0/0); x = north
        Group G(int id, double x, double z = 0, int n = 2, string l = "north group") => new(new[] { id, id + 1, id + 2 }.Take(n).ToArray(), l, x, z, Math.PI, n);
        string All(int side, string fl, double now) => string.Join(" | ", Take(side, fl, now).Select(e => Say(e, "Overlord", e.Kind.EndsWith("commit") ? ", BRAA" : "")));
        Fighter Ford(double x = 0, double fuel = 0.8) => new("Ford one", x, 0, fuel, 4, 2);
        Fighter Dodge(double x = 0, double z = 5 * NM) => new("Dodge one", x, z, 0.8, 4, 2);

        Update(2, Ford(), 0); Update(2, Dodge(), 0);
        Tick(2, new[] { G(10, 60 * N) }, 0);
        check(All(2, "Ford one", 0) == "" && All(2, "Dodge one", 0) == "", "LK9: Gruppe 60 NM: noch keine Zuweisung");
        Update(2, Ford(), 2); Update(2, Dodge(), 2);
        Tick(2, new[] { G(10, 35 * N) }, 2);
        check(All(2, "Ford one", 2) == "Ford one, Overlord, commit north group, BRAA." && All(2, "Dodge one", 2) == "Dodge one, Overlord, maintain CAP.",
              "LK9: eine Gruppe -> ein Flug commit (der nächste), der andere maintain CAP");
        check(TargetedBy(2, 11) == "Ford one" && TargetedBy(2, 11, "Ford one") == null && Mine(2, "Ford one", 10) && !Mine(2, "Dodge one", 10), "LK9: Tabelle wer welche Gruppe hat (LK7)");
        Update(2, Ford(), 4); Update(2, Dodge(0, 2 * NM), 4);   // Dodge is closer now: no switching
        Tick(2, new[] { G(10, 30 * N) }, 4);
        check(All(2, "Ford one", 4) == "" && All(2, "Dodge one", 4) == "" && TargetedBy(2, 10) == "Ford one", "LK9: Zuweisung bleibt stehen (kein Flip-Flop, kein zweites maintain)");
        check(Want(2, "Dodge one", new[] { 10, 11 }, "north group", 2, 5) == "Ford one" && All(2, "Dodge one", 5) == "Dodge one, Overlord, skip it, north group Ford one targeted.",
              "LK9: zweiter Flug will die vergebene Gruppe -> skip it");
        // second group appears -> to the free flight
        Update(2, Ford(), 6); Update(2, Dodge(), 6);
        Tick(2, new[] { G(10, 25 * N), G(20, 30 * N, 10 * N, 2, "east group") }, 6);
        check(All(2, "Dodge one", 6) == "Dodge one, Overlord, commit east group, BRAA." && TargetedBy(2, 20) == "Dodge one", "LK9: neue Gruppe -> nächster freier Flug");
        // splash of the north group: Ford reset, east group still there
        Update(2, Ford(), 8); Update(2, Dodge(), 8);
        Tick(2, new[] { G(20, 28 * N, 10 * N, 2, "east group") }, 8);
        check(All(2, "Ford one", 8) == "Ford one, Overlord, reset." && TargetedBy(2, 10) == null, "LK9: Gruppe weg -> reset");
        // new group right after: Ford is still on the way back (ResetSec), recommit only afterwards
        Update(2, Ford(), 10); Update(2, Dodge(), 10);
        Tick(2, new[] { G(20, 28 * N, 10 * N, 2, "east group"), G(30, 35 * N, -10 * N, 2, "west group") }, 10);
        check(All(2, "Ford one", 10) == "" && TargetedBy(2, 30) == null, "LK9: nach reset erst zurück auf Station");
        Update(2, Ford(), 75); Update(2, Dodge(), 75);
        Tick(2, new[] { G(20, 28 * N, 10 * N, 2, "east group"), G(30, 35 * N, -10 * N, 2, "west group") }, 75);
        check(All(2, "Ford one", 75) == "Ford one, Overlord, recommit west group, BRAA.", "LK9: zurück auf Station, Gruppe offen -> recommit");
        // bingo: assignment freed (silent), the group goes to the next free flight
        Update(2, Ford(0, 0.2), 77); Update(2, Dodge(), 77); Update(2, new Fighter("Chevy one", 0, -5 * NM, 0.9, 2, 2), 77);
        Tick(2, new[] { G(20, 28 * N, 10 * N, 2, "east group"), G(30, 35 * N, -10 * N, 2, "west group") }, 77);
        check(All(2, "Ford one", 77) == "" && All(2, "Chevy one", 77) == "Chevy one, Overlord, commit west group, BRAA." && TargetedBy(2, 30) == "Chevy one", "LK9: bingo -> frei, an den nächsten");
        // large group: two flights; picture clean -> reset with clean
        Reset();
        Update(2, Ford(), 100); Update(2, Dodge(), 100); Update(2, new Fighter("Chevy one", 0, -5 * NM, 0.9, 0, 2), 100);
        Tick(2, new[] { G(40, 30 * N, 0, 3) }, 100);
        check(All(2, "Ford one", 100).Contains("commit north group") && All(2, "Dodge one", 100).Contains("commit north group") && All(2, "Chevy one", 100) == "Chevy one, Overlord, maintain CAP.",
              "LK9: heavy -> zwei Flüge (Fox 3 zuerst), dritter maintain CAP");
        Update(2, Ford(), 102); Update(2, Dodge(), 102);
        Tick(2, Array.Empty<Group>(), 102);
        check(All(2, "Ford one", 102) == "Ford one, Overlord, picture clean, reset.", "LK9: nichts mehr da -> picture clean, reset");
        // Player without target sign of life: Want assignment expires after TargetSec
        Want(2, "Enfield one", new[] { 50 }, "single group", 1, 200);
        Tick(2, new[] { G(50, 80 * N, 0, 1, "single group") }, 300);
        check(TargetedBy(2, 50) == "Enfield one", "LK9: targeting ohne CAP steht in der Tabelle");
        Tick(2, new[] { G(50, 80 * N, 0, 1, "single group") }, 501);
        check(TargetedBy(2, 50) == null, "LK9: ohne Lebenszeichen nach TargetSec frei");
        // LD2: Fox 3 commit at 55 NM, Fox 2 only not at 50; a leaker in drag gets a flight (otherwise not)
        Reset();
        Update(2, Ford(), 600);
        Tick(2, new[] { G(60, 55 * N) }, 600);
        check(TargetedBy(2, 60) == "Ford one", "LD2: Fox 3 commit bei 55 NM");
        Reset();
        Update(2, new Fighter("Chevy one", 0, 0, 0.9, 0, 2), 610);
        Tick(2, new[] { G(60, 50 * N) }, 610);
        check(TargetedBy(2, 60) == null, "LD2: nur Fox 2 nicht bei 50 NM");
        Group Drag(int id) => new(new[] { id }, "south group", -20 * N, 0, Math.PI, 1);   // 20 NM south, heading south: drag
        Update(2, Ford(), 612);
        Tick(2, new[] { Drag(70) }, 612);
        check(TargetedBy(2, 70) == null, "LD2: drag ohne leaker keine Zuweisung");
        Leaker(2, new[] { 70 });
        Update(2, Ford(), 614);
        Tick(2, new[] { Drag(70) }, 614);
        check(TargetedBy(2, 70) == "Ford one", "LD2: leaker im drag -> Zuweisung");
        // LD8: assigned group turns 90° -> "north group, beam east" to Ford; pulls away (drag, farther than range + 10 NM) for over 2 min -> reset
        Reset();
        Group G8(double x, double hdg) => G(80, x) with { Hdg = hdg };
        foreach (var (t8, x8, h8) in new[] { (700.0, 40.0, Math.PI), (701, 40, Math.PI / 2), (702, 80, 0) }) { Update(2, Ford(), t8); Tick(2, new[] { G8(x8 * N, h8) }, t8); if (t8 == 700) Take(2, "Ford one", t8); }
        var a8 = All(2, "Ford one", 702);
        Update(2, Ford(), 800); Tick(2, new[] { G8(80 * N, 0) }, 800);
        bool held8 = TargetedBy(2, 80) == "Ford one";
        Update(2, Ford(), 823); Tick(2, new[] { G8(80 * N, 0) }, 823);
        var r8 = All(2, "Ford one", 823);
        check(a8 == "Ford one, Overlord, north group, beam east. | Ford one, Overlord, north group, drag north." && held8 && r8 == "Ford one, Overlord, reset." && TargetedBy(2, 80) == null, $"LD8 Aspekt-Update, drag -> reset: {a8} | {held8} | {r8}");
        // LD14: unable -> next flight; LD16: player's fox -> skip it to the AI flight, defensive -> second flight
        Reset();
        Update(2, Ford(), 700); Update(2, Dodge(), 700);
        Tick(2, new[] { G(80, 35 * N) }, 700);
        Take(2, "Ford one", 700); Take(2, "Dodge one", 700);
        Unable(2, "Ford one", 701);
        Update(2, Ford(), 702); Update(2, Dodge(), 702);
        Tick(2, new[] { G(80, 34 * N) }, 702);
        check(TargetedBy(2, 80) == "Dodge one" && All(2, "Dodge one", 702).Contains("commit north group"), "LD14: unable -> nächster Flug");
        Claim(2, "Dagger one", new[] { 80, 81 }, "north group", 2, 704);
        check(TargetedBy(2, 80) == "Dagger one" && All(2, "Dodge one", 704) == "Dodge one, Overlord, skip it, north group Dagger one targeted.", "LD16: fox des Spielers -> skip it an den KI-Flug");
        Assist(2, new[] { 80 });
        Update(2, Ford(), 770); Update(2, Dodge(), 770);
        Tick(2, new[] { G(80, 30 * N) }, 770);
        check(TargetedBy(2, 80, "Dagger one") == "Ford one" && All(2, "Ford one", 770).Contains("commit north group"), "LD16: Verstärkung -> zweiter Flug");
        Reset();
    }
}
