using System.Globalization;
using System.Text.RegularExpressions;
using static DcsAtc.Tower;

namespace DcsAtc;

/// AI flights on the radio (KI-FUNK-PLAN.md): combat brevity (Fox, Pitbull, Splash, Trashed, Defending, Merged …), state (Joker, Bingo,
/// Winchester) and emergencies on Guard (eject, beacon, pilot on the ground, Mayday). Mission events (X lines) and AI situation
/// (T lines) in, radio calls out; Program speaks them via SRS. Only AI flights on a player's side near that player talk.
static class Flights
{
    const double NM = 1852;
    public const double Guard = 243.0, GuardVhf = 121.5;

    /// AI aircraft from the T line. Ammo: Fox 3, Fox 1, Fox 2, gun, air-to-ground. Freq: flight frequency from the editor (0 = none).
    public record Unit(string Name, string Group, string Type, string Callsign, int Side, double X, double Z, double Alt, bool InAir,
                       double Fuel, double Freq, int[] Ammo, string Flag = "", int Gid = 0, double Hdg = double.NaN, double FuelKg = 0,
                       string Task = "", bool Heli = false);   // Hdg: heading (rad), NaN = unknown; FuelKg: internal tank (0 = unknown); Task: editor task (R376); Heli: helicopter
    /// Radio call. Who: speaking unit (voice), "" = AWACS. Freq2: second frequency (Guard VHF).
    public record Call(string Kind, string Who, string Station, string Text, double Freq, int Prio, double MaxAge, bool Stress = false, double Freq2 = 0, int Side = 0);   // Side: SRS coalition (A133)
    /// Players (side, location, DCS group, unit, callsign); AWACS per side and location (name, frequency; null = none); tactical frequency without AWACS.
    public record World(IReadOnlyList<(int Side, double X, double Z, int Gid, string Unit, string Callsign)> Players,
                        Func<int, double, double, (string Name, double Freq)?> Awacs, double Tactical);

    public static string Mode = "voll";        // full / tactical / off
    public static Action<string>? Cmd;          // LD1: command to the mission script (ENGAGE/DISENGAGE, LD7: RTB), null = AI flies without us
    public static bool Follow = true;            // LD1 AiFollowAwacs: send ENGAGE/DISENGAGE
    public static double RadiusNm = 60;        // a player of the same side must be this close
    public static bool OwnWingmen;             // AI wingmen of a player also talk (otherwise DCS speaks for them itself)
    public static Func<double> Rnd = Random.Shared.NextDouble;
    public static Dictionary<string, Unit> Units = new();   // Unit -> last state (Program, refreshed with every state file)

    static readonly List<(double Due, Call C)> pending = new();
    static readonly Dictionary<string, double> told = new();               // key -> time: block repeats
    static readonly Dictionary<string, (int N, double At)> kills = new();  // unit -> kills in quick succession (splash two)
    static readonly Dictionary<string, double> shotAt = new();             // group/unit -> last shot (engaged, winchester, shack)
    static readonly Dictionary<(double, int), List<double>> sent = new();  // frequency, coalition -> transmit times of the last minute (budget)
    static readonly Dictionary<string, bool> ordered = new();              // R384: "side:flight" -> last AWACS tasking sent (true) or dropped (false)
    static readonly Dictionary<string, (Unit U, double At)> downAt = new(); // shot down, ejection still pending
    static readonly List<(Unit U, double At, int Step, string Bulls, bool Water)> down = new();   // Ejections: beacon, pilot on the ground (R361: or in the water)
    static readonly Dictionary<string, (double Alt, double At)> level = new();   // group -> altitude and since when held (on station)
    static readonly Dictionary<string, (double X, double Z)> station = new();      // group -> where "on station" was reported (RTB)
    static readonly Dictionary<string, double> rtbSince = new();                    // group -> since when it has been heading home
    static readonly Dictionary<string, string> tgt = new();                         // own unit -> target (enemy DCS group), targeted
    static readonly Dictionary<string, (string Unit, string Grp, double At)> fight = new();   // own group -> last air-to-air shot (target, target group, time): out, status
    static readonly Dictionary<string, double> opsAt = new();                       // group -> last ops check (LK15)
    static readonly HashSet<string> spiked = new();                                  // own units with reported spike (LK14: naked)
    static readonly HashSet<string> dead = new();                                    // R381: air kills still in the T lines (falling), not counted as contacts
    static readonly Dictionary<(string S, string T), double> flying = new();        // shooter/target -> expected end of time of flight (timeout); infinity = timeout called
    /// LK3: group name at the side's AWACS (side, Traffic.Id of the enemy unit) -> "north group"/"single group"/null; the selftest replaces it
    public static Func<int, int, string?> LabelOf = Ops.GroupLabel;
    static double lastScan;

    // ------------------------------------------------------------ Speech phrasing
    static Match Cs2(Unit u) => Regex.Match(u.Callsign, @"^([A-Za-z]+)\s*(\d)\s*-?\s*(\d)");
    /// "Enfield11" -> "Enfield one one"; Russian "201" -> "two zero one"; without callsign the type.
    static string Cs(Unit u) => Cs2(u) is { Success: true } m ? SpokenCallsign($"{m.Groups[1].Value} {m.Groups[2].Value}-{m.Groups[3].Value}")
                              : Regex.IsMatch(u.Callsign, @"^\d+$") ? Digits(u.Callsign)
                              : u.Type != "" ? Ops.TypeSay(u.Type) : u.Callsign;   // Player pseudo-unit has no type: free-text callsign/player name
    /// Display in game: "Enfield 1-1"
    static string Disp(Unit u) => Cs2(u) is { Success: true } m ? $"{m.Groups[1].Value} {m.Groups[2].Value}-{m.Groups[3].Value}" : u.Callsign != "" ? u.Callsign : u.Type;
    /// In own flight: "one two"
    static string Elem(Unit u) => Cs2(u) is { Success: true } m ? SpokenCallsign($"{m.Groups[2].Value}-{m.Groups[3].Value}") : Cs(u);
    static string Count(int n) => n is >= 1 and <= 9 ? DigitWords[n] : n.ToString(CultureInfo.InvariantCulture);
    /// Flight: "Ford one" (lead and wing are called "Ford one one/one two")
    static string Fl(Unit u) => Cs2(u) is { Success: true } m ? SpokenCallsign($"{m.Groups[1].Value} {m.Groups[2].Value}") : Cs(u);
    static string Bulls(Unit u, int side = 0) => Ops.Bulls.TryGetValue(side > 0 ? side : u.Side, out var b) || Ops.Bulls.TryGetValue(0, out b)
        ? $"bullseye {Ops.Brg(Bearing(b.X, b.Z, u.X, u.Z))}, {Miles(Dist(b.X, b.Z, u.X, u.Z))}" : "";

    /// NATO names (Splash, later Threat): DCS type -> name.
    static readonly (string Re, string Name)[] NatoNames =
    {
        (@"^(Su-27|Su-30|Su-33|J-11)", "Flanker"), (@"^MiG-29", "Fulcrum"), (@"^MiG-31", "Foxhound"), (@"^MiG-25", "Foxbat"), (@"^MiG-23", "Flogger"),
        (@"^MiG-21", "Fishbed"), (@"^MiG-19", "Farmer"), (@"^MiG-15", "Fagot"), (@"^Su-24", "Fencer"), (@"^Su-25", "Frogfoot"), (@"^Su-34", "Fullback"),
        (@"^Su-17", "Fitter"), (@"^Tu-22", "Backfire"), (@"^Tu-(95|142)", "Bear"), (@"^Tu-160", "Blackjack"), (@"^A-50", "Mainstay"), (@"^IL-76", "Candid"),
        (@"^IL-78", "Midas"), (@"^Mi-(24|35)", "Hind"), (@"^Mi-(8|17)", "Hip"), (@"^Mi-28", "Havoc"), (@"^Ka-5[02]", "Hokum"), (@"^Mi-26", "Halo"),
    };
    internal static string? Nato(string type) => NatoNames.FirstOrDefault(n => Regex.IsMatch(type, n.Re, RegexOptions.IgnoreCase)).Name;
    /// LK14 (KF32): ground/sea radar (DCS type) -> NATO designation of the system for "mud spike, SA-6"; AAA radar "triple A"; unknown null.
    static readonly (string Re, string Name)[] SamNames =
    {
        (@"^Kub", "SA-6"), (@"Buk", "SA-11"), (@"^Osa", "SA-8"), (@"^Tor", "SA-15"), (@"^S-300|40B6|64H6", "SA-10"), (@"s-125", "SA-3"), (@"SNR_75|^S_75", "SA-2"),
        (@"5N62|S-200", "SA-5"), (@"Tunguska", "SA-19"), (@"ZSU-23-4|Shilka|Gepard|ZSU_57", "triple A"), (@"Patriot", "Patriot"), (@"Hawk", "Hawk"), (@"HQ-7", "HQ-7"),
        (@"rapier", "Rapier"), (@"Roland", "Roland"), (@"NASAMS", "NASAMS"),
    };
    static string? Sam(string type) => SamNames.FirstOrDefault(n => Regex.IsMatch(type, n.Re, RegexOptions.IgnoreCase)).Name;

    // ------------------------------------------------------------ Who talks, on which frequency
    static bool Armed(Unit u) => u.Ammo.Sum() > 0;
    static int Aam(Unit u) => u.Ammo.Length >= 3 ? u.Ammo[0] + u.Ammo[1] + u.Ammo[2] : 0;
    /// R376: only counter-air flights check in, commit and defend themselves (SEAD/CAS/strike keep their task); unknown task = as before.
    static bool CounterAir(Unit u) => u.Task is "" or "Nothing" or "CAP" or "Fighter Sweep" or "Escort" or "Intercept";
    /// AI flight allowed to transmit: no airfield traffic, no AI wingman of a player (except option), player of the same side nearby.
    static bool Ours(Unit u, World w, double radiusNm = 0) => Mode != "aus" && !Regex.IsMatch(u.Flag, "^(dep|arr)") &&
        (OwnWingmen || !w.Players.Any(p => p.Gid > 0 && p.Gid == u.Gid)) &&
        w.Players.Any(p => p.Side == u.Side && Dist(p.X, p.Z, u.X, u.Z) < (radiusNm > 0 ? radiusNm : RadiusNm) * NM);
    static double Tac(Unit u, World w) => w.Awacs(u.Side, u.X, u.Z) is { Freq: > 0 } a ? a.Freq : w.Tactical;
    /// Flight frequency for internal calls; 0 = none of its own (empty or equal to the tactical/AWACS frequency): then no audio on the others' net (KF13).
    static double Intra(Unit u, World w) => u.Freq > 0 && Math.Abs(u.Freq - Tac(u, w)) > 0.005 ? u.Freq : 0;
    static (double X, double Z)? Pos(string unit, World w) =>
        Units.TryGetValue(unit, out var t) ? (t.X, t.Z) : w.Players.FirstOrDefault(p => p.Unit == unit) is { Unit: { Length: > 0 } } p ? (p.X, p.Z) : ((double, double)?)null;
    static Unit? Wingman(Unit u) => Units.Values.Where(x => x.Group == u.Group && x.Name != u.Name && x.InAir).OrderBy(x => x.Callsign, StringComparer.Ordinal).FirstOrDefault();
    /// Enemies with altitude (m) and detection id (Traffic.Id = hash of the unit name); players have no altitude (NaN) and no id (0).
    static IEnumerable<(double X, double Z, double Alt, int Id)> Hostiles(Unit u, World w) =>
        Units.Values.Where(x => x.InAir && x.Side != u.Side && x.Side != 0).Select(x => (x.X, x.Z, x.Alt, x.Name.GetHashCode()))
             .Concat(w.Players.Where(p => p.Side != u.Side && p.Side != 0).Select(p => (p.X, p.Z, double.NaN, 0)));

    /// Enemy groups (DCS group) that u's side has detected, within nm of u, nearest first. Players do not count here (no heading).
    static List<(string Key, Unit Lead, int N, bool Known)> Groups(Unit u, double nm)
    {
        var det = Ops.Detected.GetValueOrDefault(u.Side);
        return Units.Values.Where(x => x.InAir && x.Side != u.Side && x.Side != 0 && (det == null || det.ContainsKey(x.Name.GetHashCode())) && Dist(u.X, u.Z, x.X, x.Z) < nm * NM)
            .GroupBy(x => x.Group).Select(g => (g.Key, g.MinBy(x => Dist(u.X, u.Z, x.X, x.Z))!, g.Count(), det == null || g.Any(x => det[x.Name.GetHashCode()])))
            .OrderBy(g => Dist(u.X, u.Z, g.Item2.X, g.Item2.Z)).ToList();
    }
    /// hot or flank on u (like Ops.Hot)
    static bool Hot(Unit h, Unit u) => !double.IsNaN(h.Hdg) && HdgDiff(h.Hdg * 180 / Math.PI, Bearing(h.X, h.Z, u.X, u.Z)) <= 70;
    /// "BRAA 360, 30, 23 thousand, hot" from u; with config AwacsBullseye "bullseye 040, 35, 23 thousand" (like Ops.Describe)
    static string Where(Unit u, Unit h)
    {
        var alt = Ops.Thousand(h.Alt);
        if (Ops.AwacsBullseye && Bulls(h, u.Side) is { Length: > 0 } b) return $"{b}, {alt}";
        double off = double.IsNaN(h.Hdg) ? double.NaN : HdgDiff(h.Hdg * 180 / Math.PI, Bearing(h.X, h.Z, u.X, u.Z));
        var aspect = double.IsNaN(off) ? "" : off <= 30 ? ", hot" : $", {(off <= 70 ? "flank" : off <= 120 ? "beam" : "drag")} {Ops.Card(h.Hdg * 180 / Math.PI)}";
        return $"BRAA {Ops.Brg(Bearing(u.X, u.Z, h.X, h.Z))}, {Miles(Dist(u.X, u.Z, h.X, h.Z))}, {alt}{aspect}";
    }
    /// LK10: name of enemy DCS group grp at the AWACS of side ("north group", "single group"), null = none (no picture)
    static string? Named(int side, string grp) => Units.Values.Where(x => x.Group == grp && x.Side != side).Select(x => LabelOf(side, x.Name.GetHashCode())).FirstOrDefault(n => n != null);
    static string Tail((string Key, Unit Lead, int N, bool Known) g) =>
        $"{(g.Known ? "hostile" : "bogey")}, {(g.N == 1 ? "single" : g.N == 2 ? "two contacts" : $"heavy, {g.N} contacts")}{(g.Known ? Opt(Nato(g.Lead.Type)) : "")}";
    /// like Ops: "clean" only without contacts in the side's radar picture (A113), same radius Ops.RadarNm (R53); side missing = everything visible
    static bool Clean(Unit u, World w)
    {
        var det = Ops.Detected.GetValueOrDefault(u.Side);
        return !Hostiles(u, w).Any(h => Dist(h.X, h.Z, u.X, u.Z) < Ops.RadarNm * NM && !dead.Any(d => d.GetHashCode() == h.Id) && (det == null || h.Id == 0 || det.ContainsKey(h.Id)));
    }

    static bool Once(string key, double now, double sec)
    {
        if (now - told.GetValueOrDefault(key, -1e9) < sec) return false;
        told[key] = now;
        return true;
    }

    /// tactical: only what the other flights really need
    static readonly HashSet<string> TacticalKinds = new() { "status", "fox", "engaged", "timeout", "out", "splash", "trashed", "bingo", "defending", "merged", "down", "guard" };
    static void Say(double now, double delay, string kind, Unit u, string text, double freq, int prio, double maxAge, bool stress = false, double freq2 = 0)
    {
        if (freq <= 0 || Mode == "aus" || Mode == "taktisch" && !TacticalKinds.Contains(kind)) return;
        pending.Add((now + delay, new Call(kind, u.Name, Disp(u), text, freq, prio, maxAge, stress, freq2, u.Side)));
    }
    static void SayAwacs(double now, double delay, string kind, int side, string name, string text, double freq, int prio, double freq2 = 0)
    {
        if (freq <= 0 || Mode == "aus" || Mode == "taktisch" && !TacticalKinds.Contains(kind)) return;
        pending.Add((now + delay, new Call(kind, "", name, text, freq, prio, 15, false, freq2, side)));
    }
    static double Reaction() => 0.4 + 0.8 * Rnd();   // event -> radio call like a human

    /// AWACS warning to a player who has not tuned the AWACS frequency (KF84): same call "on guard".
    /// Review l7: the station does not name itself in the call (warship warning formula "this is US Navy warship …" to unknowns), no "Contact …" (reply on Guard).
    internal static string OnGuard(string text, string station, double freq) =>
        !text.Contains($", {station},") ? text
        : new Regex(Regex.Escape($", {station},")).Replace(text, $", {station} on guard,", 1).TrimEnd('.') + $". Contact {station} {FreqSay(freq)}.";

    // ------------------------------------------------------------ Mission events
    /// X;shot;unit;group;side;weapon;category;missile type;guidance;target;target group;x;z · X;guns;… · X;hit;… · X;kill;… · X;eject;…
    /// X;pitbull;unit · X;maddog;unit · X;trashed;unit;target
    public static void OnEvent(string[] e, double now, World w)
    {
        if (e.Length < 3) return;
        double D(int i) => double.Parse(e[i], CultureInfo.InvariantCulture);
        Unit? U(int i) => e.Length > i ? Units.GetValueOrDefault(e[i]) : null;
        switch (e[1])
        {
            case "shot" when e.Length >= 13:
            {
                Unit? s = U(2), t = U(9);
                string cat = e[6], mcat = e[7], guid = e[8];
                if (s != null && Ours(s, w))
                {
                    var call = cat == "missile" && mcat == "aam" ? guid switch { "radar_active" => "fox three", "radar_semi_active" => "fox one", "ir" => "fox two", _ => null }
                             : cat == "missile" && guid == "radar_passive" ? "magnum"
                             : cat == "missile" && mcat == "anti_ship" ? "bruiser"
                             : cat == "missile" ? "rifle"
                             : cat == "bomb" ? "pickle" : null;   // R385: PICKLE (ATP 1-02.1) instead of "bombs away"
                    bool aa = call?.StartsWith("fox") == true;
                    if (call != null && Once("shot:" + s.Name, now, 4))   // salvo: one call
                    {
                        double delay = Reaction();
                        var dir = Pos(e[9], w) is { } tp ? ", " + Ops.Card(Bearing(s.X, s.Z, tp.X, tp.Z)) : "";
                        if (aa && e[10] != "")
                        {
                            // LK12: group instead of compass direction ("fox three, north group"); single group: only "fox three"; without picture names the direction as before
                            var lbl = Named(s.Side, e[10]);
                            if (lbl != null) dir = lbl == "single group" ? "" : ", " + lbl;
                            // ATP 1-02.1 ENGAGED: flight's own call before the first shot at the group, if it has not already taken it with targeted
                            bool targeted = Units.Values.Any(x => x.Group == s.Group && tgt.GetValueOrDefault(x.Name) == e[10]);
                            if (!targeted && Once($"engaged:{s.Group}>{e[10]}", now, 180)) { Say(now, delay, "engaged", s, $"{Fl(s)}, engaged{(lbl != null ? " " + lbl : "")}.", Tac(s, w), 1, 10); delay += 1.5; }
                        }
                        Say(now, delay, aa ? "fox" : "ag", s, $"{Cs(s)}, {call}{dir}.", Tac(s, w), aa ? 1 : 2, 6);
                    }
                    if (aa && e[9] != "" && Pos(e[9], w) is { } tq)
                    {
                        fight[s.Group] = (e[9], e[10], now);
                        // LK13: time of flight roughly by range at the shot (ponytail: 5 s + 3 s per NM, ~1200 kt average; more precise per weapon/altitude if needed); salvo = one time
                        if (!flying.TryGetValue((s.Name, e[9]), out var due) || double.IsInfinity(due)) flying[(s.Name, e[9])] = now + 5 + 3 * Dist(s.X, s.Z, tq.X, tq.Z) / NM;
                    }
                    if (aa) { shotAt[s.Name] = now; pending.RemoveAll(p => p.C.Kind is "targeted" or "commit" or "sorted" && Units.TryGetValue(p.C.Who, out var x) && x.Group == s.Group); }   // KF61/62: committing/targeted/sorted only before the first shot
                    if (cat == "bomb") shotAt["bomb:" + s.Name] = now;
                }
                // LD1 self-defense comes first: air-to-air missile at an own AI fighter -> its flight attacks the shooter's group (mode awacs)
                if (Follow && t != null && s != null && mcat == "aam" && s.Side != t.Side && Ours(t, w) && Armed(t) && CounterAir(t) && Once($"selfdef:{t.Group}>{s.Group}", now, 120)) Cmd?.Invoke($"ENGAGE;{t.Group};{s.Group}");
                // Missile at an own AI aircraft: RWR/MAWS -> Defending toward the threat (gun, bomb: no). R379: only once it is a threat – launch within 15 NM at once,
                // further out when the missile has closed to about 15 NM (3 s per NM as LK13) or earlier at its pitbull; tactical frequency (LUFTKAMPF-DREHBUCH)
                if (t != null && cat == "missile" && Ours(t, w) && (s == null || s.Side != t.Side) && Once("def:" + t.Name, now, 10))
                    Say(now, 1.2 + Rnd() + Math.Max(0, 3 * (Dist(t.X, t.Z, D(11), D(12)) / NM - 15)), "defending", t,
                        $"{Cs(t)}, {(mcat == "sam" ? "SAM" : "launch")} {Ops.Card(Bearing(t.X, t.Z, D(11), D(12)))}, defending.", Tac(t, w), 0, 4, stress: true);
                break;
            }
            case "maddog":   // active missile without target: instead of "fox three"
            {
                int i = pending.FindLastIndex(p => p.C.Who == e[2] && p.C.Text.Contains("fox three"));
                if (i >= 0) pending[i] = (pending[i].Due, pending[i].C with { Text = Regex.Replace(pending[i].C.Text, "fox three.*", "maddog.") });
                break;
            }
            case "pitbull":
            {
                // R379: the missile goes active -> its target defends now (pending "defending" pulled forward)
                int j = e.Length > 3 ? pending.FindIndex(p => p.C.Kind == "defending" && p.C.Who == e[3]) : -1;
                if (j >= 0 && pending[j].Due > now + 1) pending[j] = (now + Reaction(), pending[j].C);
                // R380: salvo = one call
                if (U(2) is { } u && Ours(u, w) && Once("pitbull:" + u.Name, now, 6))
                    Say(now, Reaction(), "pitbull", u, $"{Cs(u)}, pitbull.", Tac(u, w), 2, 15);   // R310: often waits behind a long AWACS call; valid until impact (~15 s)
                break;
            }
            case "trashed":
            {
                string tg = e.Length > 3 ? e[3] : "";
                pending.RemoveAll(p => p.C.Kind == "defending" && p.C.Who == tg);   // R379: missile gone before its target had to defend
                // LK13: after "timeout" (time of flight over) no "trashed" for the same missile; R380: salvo = one call
                if (U(2) is { } u && Ours(u, w) && !(flying.Remove((u.Name, tg), out var fd) && double.IsInfinity(fd)) && Once($"trashed:{u.Name}>{tg}", now, 6))
                    Say(now, Reaction(), "trashed", u, $"{Cs(u)}, trashed.", Tac(u, w), 1, 15);
                break;
            }
            // LK14 (KF6, KF31/32): an enemy's radar tracks the AI aircraft (RWR) -> ATP 1-02.1 SPIKE [direction, type] or MUD (ground threat), end -> NAKED; flight frequency
            case "spike" when e.Length >= 7 && U(2) is { } u && Ours(u, w) && Once($"spike:{u.Name}>{e[3]}", now, 10):
            {
                double brg = Bearing(u.X, u.Z, D(5), D(6));
                spiked.Add(u.Name);
                Say(now, Reaction(), "spike", u, e[4] == "air" ? $"{Cs(u)}, spike {Ops.Brg(brg)}{Opt(Nato(e[3]))}." : $"{Cs(u)}, mud spike{Opt(Sam(e[3]))}, {Ops.Card(brg)}.", Intra(u, w), 1, 8);
                break;
            }
            case "naked" when U(2) is { } u && spiked.Remove(u.Name):
                // locked briefly and already gone (call not yet sent): both dropped
                if (pending.RemoveAll(p => p.C.Who == u.Name && p.C.Kind == "spike") == 0 && Ours(u, w)) Say(now, Reaction(), "naked", u, $"{Cs(u)}, naked.", Intra(u, w), 2, 8);
                break;
            case "guns" when e.Length >= 7 && U(2) is { } u && Ours(u, w) && Once("guns:" + u.Name, now, 20):
                if (e[6] == "air") Say(now, 0.3, "guns", u, $"{Cs(u)}, guns.", Intra(u, w), 2, 4);   // Gun in air combat: flight frequency
                else Say(now, 0.3, "ag", u, $"{Cs(u)}, guns.", Tac(u, w), 2, 5);
                break;
            case "hit" when e.Length >= 8:
            {
                flying.Remove((e[2], e[4]));   // LK13: missile arrived, no timeout
                pending.RemoveAll(p => p.C.Kind == "defending" && p.C.Who == e[4]);   // R379: hit before the deferred "defending"
                if (U(4) is { } t && Ours(t, w, RadiusNm * 2))
                {
                    double life = D(7);
                    if (life < 0.5 && Once("mayday:" + t.Name, now, 1e9))
                    {
                        Say(now, 2 + Rnd(), "guard", t, $"Mayday, mayday, mayday, {Cs(t)}, hit, RTB{Opt(Bulls(t))}.", Guard, 0, 10, true, GuardVhf);
                        // R252: AWACS acknowledges on Guard with the nearest own airfield (ICAO Annex 10 Vol II 5.3.2, JO 7110.65 10-1-3); without AWACS silent
                        if (w.Awacs(t.Side, t.X, t.Z) is { } aw)
                            SayAwacs(now, 9 + Rnd(), "guard", t.Side, aw.Name, $"{Cs(t)}, {aw.Name}, copy mayday" + (Airfield.Emergency(Tower.All, t.X, t.Z, t.Side) is { } ef
                                ? $", {ef.Name.Replace('-', ' ')} bears {Ops.Brg(Bearing(t.X, t.Z, ef.X, ef.Z))}, {Miles(Dist(t.X, t.Z, ef.X, ef.Z))}." : "."), Guard, 0, GuardVhf);
                    }
                    else if (life < 0.95 && Once("hit:" + t.Name, now, 1e9))
                        Say(now, 1 + Rnd(), "hit", t, $"{Cs(t)}, I'm hit.", Intra(t, w), 0, 6, stress: true);
                }
                // own bomb hits a ground target
                if (U(2) is { } s && Ours(s, w) && !Units.ContainsKey(e[4]) && Pos(e[4], w) == null &&
                    now - shotAt.GetValueOrDefault("bomb:" + s.Name, -1e9) < 90 && Once("shack:" + s.Name, now, 30))
                    Say(now, 1 + Rnd(), "ag", s, $"{Cs(s)}, shack.", Tac(s, w), 2, 8);
                break;
            }
            case "kill" when e.Length >= 8:
            {
                bool air = e[7] == "1";
                var v = U(4);
                foreach (var key in flying.Keys.Where(f => f.T == e[4]).ToList()) flying.Remove(key);   // LK13: target dead, no timeout
                if (air) dead.Add(e[4]);
                if (U(2) is { } k && air && Ours(k, w) && v?.Side != k.Side)
                {
                    var (n, at) = kills.GetValueOrDefault(k.Name);
                    n = now - at < 15 ? n + 1 : 1;
                    kills[k.Name] = (n, now);
                    var text = $"{Cs(k)}, splash {Count(n)}{Opt(Nato(e[5]))}.";
                    int i = pending.FindIndex(p => p.C.Who == k.Name && p.C.Kind == "splash");   // not yet sent: count up instead of a second call
                    if (i >= 0) pending[i] = (pending[i].Due, pending[i].C with { Text = text });
                    else Say(now, 1.5 + Rnd(), "splash", k, text, Tac(k, w), 1, 10);
                    if (w.Awacs(k.Side, k.X, k.Z) is { } aw)
                    {
                        // R381 (ATP 3-52.4, LUFTKAMPF-DREHBUCH): count of the flight, remaining contacts of the group with its picture name, otherwise picture clean
                        var (ng, gat) = kills.GetValueOrDefault("grp:" + k.Group);
                        kills["grp:" + k.Group] = (ng = now - gat < 15 ? ng + 1 : 1, now);
                        var det = Ops.Detected.GetValueOrDefault(k.Side);   // only what the radar picture shows
                        int rest = v == null ? 0 : Units.Values.Count(x => x.Group == v.Group && x.InAir && !dead.Contains(x.Name) && (det == null || det.ContainsKey(x.Name.GetHashCode())));
                        var ack = $"{aw.Name} copies splash {Count(ng)}" + (rest > 0 ? $"{Opt(Named(k.Side, v!.Group))}, {Count(rest)} contact{(rest > 1 ? "s" : "")}."
                                  : Clean(k, w) ? ", picture clean." : ".");
                        // not yet sent: count up. ponytail: two flights splashing within the same 5 s share one acknowledgment
                        int j = pending.FindLastIndex(p => p.C.Kind == "splash" && p.C.Who == "" && p.C.Side == k.Side && p.C.Text.StartsWith(aw.Name + " copies splash"));
                        if (j >= 0) pending[j] = (pending[j].Due, pending[j].C with { Text = ack });
                        else SayAwacs(now, 5 + Rnd(), "splash", k.Side, aw.Name, ack, Tac(k, w), 2);
                        told["awsplash:" + k.Group] = now;   // LD13: reset after the splash
                    }
                }
                if (v != null) Down(v);
                if (v != null && air && Ours(v, w)) downAt[v.Name] = (v, now);   // Wait for the ejection: good chute / no chute
                break;
            }
            case "eject" when U(2) is { } u && Ours(u, w, RadiusNm * 2):
            {
                downAt.Remove(u.Name);
                Down(u);
                var bulls = Bulls(u);
                if (Wingman(u) is { } wing) Say(now, 2 + Rnd(), "down", wing, $"{Cs(wing)}, {Elem(u)} is down, good chute{Opt(bulls)}.", Tac(wing, w), 1, 15);
                else Say(now, 0.2, "down", u, $"{Cs(u)}, ejecting, ejecting.", Tac(u, w), 0, 5, stress: true);
                down.Add((u, now, 0, bulls, e.Length > 6 && e[6] == "1"));
                break;
            }
            case "eject" when w.Players.FirstOrDefault(p => p.Unit == e[2]) is { Unit: { Length: > 0 } } pl:   // Player (N45): no T entry, pseudo-unit for the down procedure
            {
                var pu = new Unit(pl.Unit, pl.Gid > 0 ? Units.Values.FirstOrDefault(x => x.Gid == pl.Gid)?.Group ?? "" : "", "", pl.Callsign, pl.Side, pl.X, pl.Z, 0, false, 0, 0, Array.Empty<int>(), "", pl.Gid);
                var bulls = Bulls(pu);   // "ejecting" is said by the player himself; only an AI wingman can report him (principle 8: with OwnWingmen)
                if (Wingman(pu) is { } wing && Ours(wing, w, RadiusNm * 2)) Say(now, 2 + Rnd(), "down", wing, $"{Cs(wing)}, {Elem(pu)} is down, good chute{Opt(bulls)}.", Tac(wing, w), 1, 15);
                down.Add((pu, now, 0, bulls, e.Length > 6 && e[6] == "1"));
                break;
            }
            case "pilotdead":   // R361: ejected pilot dead -> no SAR call
                down.RemoveAll(d => d.U.Name == e[2]);
                break;
        }
    }

    static string Opt(string? s) => string.IsNullOrEmpty(s) ? "" : ", " + s;
    /// R299/R378: shot down or ejected: nothing more from him except "ejecting" and the Guard calls from the ground (unsent Mayday "hit, RTB" dropped too);
    /// AWACS calls to him are dropped, to his flight only when nobody of it is left.
    static void Down(Unit u) => pending.RemoveAll(p => p.C.Who == u.Name ? p.C.Kind is not ("guard" or "down") || p.C.Text.Contains(", hit, RTB")
        : p.C.Who == "" && (p.C.Text.StartsWith(Cs(u) + ", ") || p.C.Text.StartsWith(Fl(u) + ", ") && !Units.Values.Any(x => x.Group == u.Group && x.Name != u.Name)));

    // ------------------------------------------------------------ Tick (Program, every 250 ms)
    public static List<Call> Tick(double now, World w)
    {
        dead.IntersectWith(Units.Keys);   // R381: gone from the T lines
        for (int i = down.Count - 1; i >= 0; i--)   // Ejection: after 90 s the pilot on the ground on Guard, AWACS answers
        {
            var d = down[i];
            if (w.Players.Any(p => p.Unit == d.U.Name && Dist(p.X, p.Z, d.U.X, d.U.Z) > NM)) down.RemoveAt(i);   // R361: player flying again (respawn) -> no SAR
            else if (now - d.At >= 90)
            {
                Say(now, 0, "guard", d.U, $"Mayday, mayday, mayday, {Cs(d.U)}, {(d.Water ? "in the water" : "on the ground")}{Opt(d.Bulls)}.", Guard, 1, 30, freq2: GuardVhf);   // R361: only known facts
                if (w.Awacs(d.U.Side, d.U.X, d.U.Z) is { } aw)
                    SayAwacs(now, 7, "guard", d.U.Side, aw.Name, $"{Cs(d.U)}, {aw.Name}, copy, SAR notified, monitor guard.", Guard, 1, GuardVhf);
                down.RemoveAt(i);
            }
        }
        foreach (var (name, (v, _)) in downAt.Where(kv => now - kv.Value.At > 6).ToList())   // shot down, no ejection
        {
            downAt.Remove(name);
            if (Wingman(v) is { } wing) Say(now, 0, "down", wing, $"{Cs(wing)}, {Elem(v)} is down, no chute{Opt(Bulls(v))}.", Tac(wing, w), 1, 15);
        }
        if (now - lastScan >= 2) { lastScan = now; Scan(now, w); }

        var outl = new List<Call>();
        foreach (var (due, c) in pending.Where(p => p.Due <= now).OrderBy(p => p.Due).ToList())
        {
            pending.Remove((due, c));
            if (c.Who != "" && c.Kind is not ("guard" or "down") && !Units.ContainsKey(c.Who) && !w.Players.Any(p => p.Unit == c.Who)) continue;   // R378: speaker gone (dead, despawned)
            if (!sent.TryGetValue((c.Freq, c.Side), out var times)) sent[(c.Freq, c.Side)] = times = new();   // R384: the coalitions do not hear each other
            times.RemoveAll(x => now - x > 60);
            bool full = c.Prio == 2 && times.Count >= 10 || c.Prio == 1 && times.Count >= 16;   // Radio discipline: frequency full -> unimportant calls are dropped
            // R384: AWACS tasking (commit, skip it, reset ...) and the flight's acknowledgment go together: sent -> acknowledged in any case, dropped -> no acknowledgment
            string to = c.Kind is "commit" or "abm" ? $"{c.Side}:{Regex.Match(c.Text, "^[^,.]+").Value}" : "";
            if (to != "" && c.Who != "" && ordered.Remove(to, out bool told0)) full = !told0;
            else if (to != "" && c.Who == "") ordered[to] = !full;
            if (full) continue;
            times.Add(now);
            outl.Add(c);
        }
        return outl;
    }

    /// Every 2 s: Joker/Bingo per flight, Winchester, Merged; fighters: on station, commit/targeted, RTB (replace the DCS radio that R308 mutes).
    static void Scan(double now, World w)
    {
        // LK13 TIMEOUT (ATP 1-02.1: firing parameters met, missile at the end of its time of flight): expected time of flight over, no hit/kill/trashed; salvo = one call
        foreach (var (k, due) in flying.Where(f => f.Value <= now).ToList())
        {
            flying[k] = double.PositiveInfinity;
            if (Units.TryGetValue(k.S, out var u) && Ours(u, w) && Once("timeout:" + u.Name, now, 10)) Say(now, 0, "timeout", u, $"{Cs(u)}, timeout.", Tac(u, w), 1, 10);
        }
        var fighters = new List<(Unit Lead, Unit? Wing)>();
        foreach (var g in Units.Values.Where(u => u.InAir && Armed(u) && Ours(u, w)).GroupBy(u => u.Group))
        {
            var low = g.MinBy(u => u.Fuel)!;
            var lead = g.OrderBy(u => u.Callsign, StringComparer.Ordinal).First();
            if (low.Fuel > 0.4) { told.Remove("joker:" + g.Key); told.Remove("bingo:" + g.Key); }   // R385: refuelled (tanker, landing) -> joker/bingo armed again, ABM takes it again
            if (low.Fuel < 0.22 && Once("bingo:" + g.Key, now, 1e9))
            {
                Abm.Release(lead.Side, Fl(lead));   // LK9: bingo -> assignment free
                // LD6: the flight stays together – one element reports bingo on the element frequency, the lead for the flight on tactical "Ford one, bingo, RTB."
                if (low != lead) Say(now, 0, "bingo", low, $"{Cs(low)}, bingo.", Intra(low, w), 1, 20);
                Say(now, low != lead ? 2 : 0, "bingo", lead, $"{Fl(lead)}, bingo, RTB.", Tac(lead, w), 1, 20);
                Home(lead);
                if (w.Awacs(low.Side, low.X, low.Z) is { } aw) SayAwacs(now, 4 + Rnd(), "bingo", lead.Side, aw.Name, $"{Fl(lead)}, {aw.Name}, copy bingo.", Tac(lead, w), 2);
            }
            else if (low.Fuel is < 0.35 and >= 0.22 && Once("joker:" + g.Key, now, 1e9)) Say(now, 0, "joker", low, $"{Cs(low)}, joker.", Intra(low, w), 2, 20);
            foreach (var u in g.Where(u => Aam(u) == 0 && now - shotAt.GetValueOrDefault(u.Name, -1e9) < 60))
                if (Once("winchester:" + u.Name, now, 1e9)) Say(now, 1, "winchester", u, $"{Cs(u)}, winchester.", Tac(u, w), 1, 15);
            // R383 MERGED (ATP 1-02.1): enemy fighter/combat aircraft (no helicopter, AWACS/tanker/transport) within 2 NM and 5000 ft; players without altitude are not merged
            if (Units.Values.Any(h => h.InAir && h.Side != lead.Side && h.Side != 0 && !h.Heli && !Ops.Support.IsMatch(h.Type) && Dist(h.X, h.Z, lead.X, lead.Z) < 2 * NM &&
                                      Math.Abs(h.Alt - lead.Alt) < 5000 * 0.3048) && Once("merged:" + g.Key, now, 120))
                Say(now, 0.3, "merged", lead, $"{Cs(lead)}, merged.", Tac(lead, w), 0, 4, stress: true);
            // LK13 OUT (ATP 1-02.1: turn to cold aspect from the threat, with direction): up to 2 min after the shot the lead turns more than 120° away from the target; once per shot
            if (fight.TryGetValue(g.Key, out var fg) && now - fg.At < 120 && !double.IsNaN(lead.Hdg) && Pos(fg.Unit, w) is { } fp &&
                HdgDiff(lead.Hdg * 180 / Math.PI, Bearing(lead.X, lead.Z, fp.X, fp.Z)) > 120 && Once($"out:{g.Key}:{fg.At}", now, 1e9))
                Say(now, 0.5, "out", lead, $"{Fl(lead)}, {(low.Fuel >= 0.35 && g.Sum(Fox3) > 0 ? "pump" : "out")} {Ops.Card(lead.Hdg * 180 / Math.PI)}.", Tac(lead, w), 1, 10);   // LD12 PUMP: with fuel above joker and Fox 3 he comes back
            // LK11: second element = number 3 in a four-ship (11/12 and 13/14), otherwise the wingman
            var rest = g.Where(u => u != lead).OrderBy(u => u.Callsign, StringComparer.Ordinal).ToList();
            var wing = rest.FirstOrDefault(u => u.Callsign.EndsWith("3")) ?? rest.FirstOrDefault();
            if (w.Players.Any(p => p.Gid > 0 && p.Gid == lead.Gid)) continue;   // AI wingmen of a player (OwnWingmen): the player checks in and gets the tasking
            if (g.Any(u => Aam(u) > 0) && CounterAir(lead)) { Fighter(now, w, g.Key, lead, wing); fighters.Add((lead, wing)); }
            else Abm.Release(lead.Side, Fl(lead));   // LK9: winchester (or no counter-air task) -> assignment free
        }
        // LK9: the side's Air Battle Manager distributes the groups (after the signs of life from Fighter), then each flight picks up its tasking
        foreach (var side in fighters.Select(f => f.Lead.Side).Distinct()) Abm.Tick(side, AbmGroups(side), now);
        double off = 0;   // Taskings in sequence: AWACS, reply, then the next flight
        foreach (var (lead, wing) in fighters) off = Orders(now, w, lead, wing, off);
    }

    /// LK9: enemy groups for the Air Battle Manager from the T lines (DCS group, in the side's radar picture), names via LabelOf (LK3). ponytail: not Ops.AbmGroups –
    /// its radar picture fills only with checked-in players; the ids (Traffic.Id = hash of the unit name) are the same, assignments match Ops.
    static List<Abm.Group> AbmGroups(int side)
    {
        var det = Ops.Detected.GetValueOrDefault(side);
        return Units.Values.Where(x => x.InAir && x.Side != side && x.Side != 0 && (det == null || det.ContainsKey(x.Name.GetHashCode()))).GroupBy(x => x.Group)
            .Select(g => new Abm.Group(g.Select(x => x.Name.GetHashCode()).ToArray(), Named(side, g.Key), g.First().X, g.First().Z, g.First().Hdg, g.Count())).ToList();
    }

    /// LK9: AWACS tasking to the AI flight (Abm): commit/recommit with location ("Ford one, Overlord, commit north group, bullseye …") -> "Ford one, committing." -> targeted
    /// (without second location); maintain CAP and skip it -> "Ford one."; reset -> "Ford one, resetting.". Prio 2, MaxAge 20 s (R310).
    static double Orders(double now, World w, Unit lead, Unit? wing, double off)
    {
        if (w.Awacs(lead.Side, lead.X, lead.Z) is not { } aw) return off;
        string fl = Fl(lead);
        double tac = Tac(lead, w);
        foreach (var e in Abm.Take(lead.Side, fl, now))
        {
            var g = Groups(lead, Ops.RadarNm).FirstOrDefault(x => Units.Values.Any(u => u.Group == x.Key && e.Ids.Contains(u.Name.GetHashCode())));
            bool commit = e.Kind is "commit" or "recommit";
            // LD13: after a splash first „copies splash“ (5 s), then reset; „picture clean“ the AWACS has already said there
            bool afterSplash = e.Kind == "reset" && now - told.GetValueOrDefault("awsplash:" + lead.Group, -1e9) < 30;
            var text = Abm.Say(afterSplash ? e with { Label = "group" } : e, aw.Name, commit && g.Lead != null ? $", {Where(lead, g.Lead)}, {Tail(g)}" : "");
            double rs = afterSplash ? 7 : 0;
            if (text == "") continue;
            SayAwacs(now, off + rs + 0.5, commit ? "commit" : "abm", lead.Side, aw.Name, text, tac, 2);
            if (e.Kind == "reset") foreach (var u in Units.Values.Where(u => u.Group == lead.Group)) tgt.Remove(u.Name);   // no target anymore (say status)
            // LD1: tasking and flight fit together – commit: AttackGroup on the group, skip it/reset: away from it (maintain CAP: stays as it is)
            if (Follow && commit && g.Lead != null) Cmd?.Invoke($"ENGAGE;{lead.Group};{g.Key}");
            else if (Follow && e.Kind is "skip" or "reset") Cmd?.Invoke($"DISENGAGE;{lead.Group}");
            if (!commit) { if (e.Kind != "aspect") Say(now, off + rs + 1.5, "abm", lead, e.Kind == "reset" ? $"{fl}, resetting." : $"{fl}.", tac, 2, 20); }   // LD8: aspect update without acknowledgment
            else
            {
                Say(now, off + 1, "commit", lead, $"{fl}, committing.", tac, 2, 20);
                if (g.Lead != null) Target(now, lead, wing, g, tac, true, off);
            }
            off += 3;
        }
        return off;
    }

    /// Without AWACS (nobody assigning) the flight commits itself (KF61, R311): detected groups hot/flank within 40 NM, nearest first, once per group per flight;
    /// if the flight has already fired, it is long engaged: no more commit.
    static List<(string Key, Unit Lead, int N, bool Known)> CommitOn(double now, string grp, Unit lead, bool fired) =>
        fired ? new() : Groups(lead, 40).Where(h => Hot(h.Lead, lead) && Once($"commit:{grp}>{h.Key}", now, 1e9)).ToList();

    /// TARGETED (KF62, LK10, LK11): group g for the flight. With a second element and a second free group hot under 40 NM the element takes that (registered with the AWACS,
    /// Abm.Want; name from the AWACS, otherwise by separation axis), otherwise both g and with ≥ 2 contacts "sorted". targeted gives the location only without AWACS.
    static void Target(double now, Unit lead, Unit? wing, (string Key, Unit Lead, int N, bool Known) g, double tac, bool abm, double off = 0)
    {
        string fl = Fl(lead);
        int side = lead.Side;
        int[] Ids(string key) => Units.Values.Where(u => u.Group == key).Select(u => u.Name.GetHashCode()).ToArray();
        (string Key, Unit Lead, int N, bool Known) g2 = default;
        if (wing != null)
            foreach (var h in Groups(lead, 40).Where(h => h.Key != g.Key && Hot(h.Lead, lead)))
                if (!abm || Ids(h.Key).All(id => Abm.TargetedBy(side, id, fl) == null) && Abm.Want(side, fl, Ids(h.Key), Named(side, h.Key), h.N, now) == null) { g2 = h; break; }
        var n1 = Named(side, g.Key);
        if (wing != null && g2.Lead != null)
        {
            var n2 = Named(side, g2.Key);
            if (n1 == null || n2 == null || n1 == n2)
            {
                var (p, q) = (g.Lead, g2.Lead);
                bool ns = Math.Abs(p.X - q.X) >= Math.Abs(p.Z - q.Z);   // x = north, z = east
                string Name(Unit a, Unit b) => (ns ? (a.X > b.X ? "north" : "south") : (a.Z > b.Z ? "east" : "west")) + " group";
                (n1, n2) = (Name(p, q), Name(q, p));
            }
            (tgt[lead.Name], tgt[wing.Name]) = (g.Key, g2.Key);
            Say(now, off + 1.5, "targeted", lead, $"{Cs(lead)}, targeted {n1}.", tac, 2, 20);
            Say(now, off + 2, "targeted", wing, $"{Cs(wing)}, targeted {n2}.", tac, 2, 20);
            return;
        }
        tgt[lead.Name] = g.Key;
        if (wing != null) tgt[wing.Name] = g.Key;
        // ATP 1-02.1 TARGETED [group]: the AWACS already gave the location in the commit -> only the name (without picture names just "targeted"); without AWACS the location once
        Say(now, off + 1.5, "targeted", lead, $"{fl}, targeted{(n1 != null ? " " + n1 : !abm ? $" group {Where(lead, g.Lead)}" : "")}.", tac, 2, 20);
        // LK11 (ATP 1-02.1 SORTED: sorting within the group done): at least as many contacts as elements -> each has its own target.
        // ponytail: without AI sensors (KF5) this applies immediately; with radar lock per element only at lock
        if (wing != null && g.N >= 2) Say(now, off + 2.5, "sorted", lead, $"{fl}, sorted.", tac, 2, 20);
    }

    /// Fighter flight in the AWACS net (ATP 1-02.1): ON STATION after the first settling, COMMIT/COMMITTING and TARGETED (KF61/KF62), RTB.
    /// All Prio 2 (below Fox/Defending), MaxAge 20 s: often waits behind a long AWACS call (R310).
    static string Fence(Unit lead) => Cs2(lead) is { Success: true } c ? c.Groups[1].Value : Fl(lead);   // "Ford" (flight name without number)
    /// LD7: home (RTB/bingo) -> the mission script registers the flight as an approach to the nearest own airfield, where DCS-ATC (ControlAi) takes over.
    static void Home(Unit lead)
    {
        if (Airfield.Emergency(Tower.All, lead.X, lead.Z, lead.Side) is { } af) Cmd?.Invoke($"RTB;{lead.Group};{af.Name}");
    }

    static void Fighter(double now, World w, string grp, Unit lead, Unit? wing)
    {
        var aw = w.Awacs(lead.Side, lead.X, lead.Z);
        double tac = Tac(lead, w);
        string fl = Fl(lead);
        bool fired = shotAt.Any(kv => now - kv.Value < 300 && Units.TryGetValue(kv.Key, out var x) && x.Group == grp);
        // LK15 OPS CHECK (KF55): every 15 min airborne, not within 5 min after a shot (then afterwards)
        if (!opsAt.TryAdd(grp, now) && now - opsAt[grp] >= 900 && !fired) { opsAt[grp] = now; OpsCheck(now, w, grp, lead); }
        // ON STATION: after the check-in, first time 60 s within ±500 ft (station altitude reached, any altitude); without AWACS nobody is there to report to
        (double Alt, double At) lv = level.GetValueOrDefault(grp, (lead.Alt, now));
        // LD7/R377 CHECK-IN on first contact in AWACS coverage once the flight is ours (no longer with ATC) (ATP 1-02.1 AS FRAGGED, PLAYTIME; ATP 3-52.4):
        // count, altitude, time to bingo -> AWACS "radar contact"
        if (aw is { } ac && !station.ContainsKey(grp) && Once("checkin:" + grp, now, 1e9))
        {
            var fl1 = Units.Values.Where(u => u.Group == grp && u.InAir).ToList();
            int play = (int)Math.Max(0, (fl1.Min(u => u.Fuel) - 0.22) * 100) / 5 * 5;   // ponytail: 1 % fuel per minute (cruise), real burn rate if needed
            Say(now, Reaction(), "checkin", lead, $"{ac.Name}, {fl}, checking in as fragged, flight of {Count(fl1.Count)}, {Ops.Angels(lead.Alt)}, playtime {play}.", tac, 2, 20);
            SayAwacs(now, 4 + Rnd(), "checkin", lead.Side, ac.Name, $"{fl}, {ac.Name}, radar contact.", tac, 2);
        }
        if (Math.Abs(lead.Alt - lv.Alt) > 500 * 0.3048) lv = (lead.Alt, now);
        level[grp] = lv;
        if (aw is { } a0 && !station.ContainsKey(grp) && told.ContainsKey("checkin:" + grp) && now - lv.At >= 60)
        {
            station[grp] = (lead.X, lead.Z);
            Say(now, 0.5, "fence", lead, $"{Fence(lead)}, fence in.", Intra(lead, w), 2, 20);   // LD12 FENCE IN (ATP 1-02.1): switch for combat, on the element frequency
            Say(now, Reaction(), "station", lead, $"{a0.Name}, {fl}, on station{Opt(Bulls(lead))}, {Ops.Angels(lead.Alt)}.", tac, 2, 20);
            var gs = Groups(lead, Ops.RadarNm);
            var pic = gs.Count > 0 ? $"picture, {(gs.Count == 1 ? "single group" : $"{Count(gs.Count)} groups, closest group")} {Where(lead, gs[0].Lead)}, {Tail(gs[0])}"
                    : Clean(lead, w) ? "picture clean" : "copy";
            SayAwacs(now, 4 + Rnd(), "station", lead.Side, a0.Name, $"{fl}, {a0.Name}, {pic}.", tac, 2);
        }
        // LK9: with AWACS the Air Battle Manager assigns (signs of life here, tasking in Orders); without AWACS the flight commits itself
        if (aw != null && Follow)
        {
            if (!told.ContainsKey("bingo:" + grp) && !told.ContainsKey("rtb:" + grp))
            {
                var fl3 = Units.Values.Where(u => u.Group == grp && u.InAir && u.Ammo.Length >= 3).ToList();
                Abm.Update(lead.Side, new Abm.Fighter(fl, lead.X, lead.Z, fl3.Min(u => u.Fuel), fl3.Sum(u => u.Ammo[0] + u.Ammo[1]), fl3.Sum(u => u.Ammo[2]), station.ContainsKey(grp)), now);
            }
        }
        else if (CommitOn(now, grp, lead, fired) is { Count: > 0 } fresh)   // without AWACS, LD1 mode funk (AiFollowAwacs off): the flight commits itself, the AWACS gets to know it
        {
            Say(now, 1, "commit", lead, aw is { } a1 ? $"{a1.Name}, {fl}, committing." : $"{fl}, committing.", tac, 2, 20);
            if (aw != null) Abm.Want(lead.Side, fl, Units.Values.Where(u => u.Group == fresh[0].Key).Select(u => u.Name.GetHashCode()).ToArray(), Named(lead.Side, fresh[0].Key) ?? "group", fresh[0].N, now, post: false);
            Target(now, lead, wing, fresh[0], tac, false);
        }
        // RTB: after "on station" for 60 s more than 40 NM from the station point, heading (±20°) to the nearest own airfield and 20 NM closer to it than the station;
        // not after Bingo (that already says "bingo, RTB") and not within 5 min after a shot. ponytail: rule of thumb, a very long CAP leg toward home can trigger it.
        bool home = aw != null && station.TryGetValue(grp, out var st) && !fired && !told.ContainsKey("bingo:" + grp) && !double.IsNaN(lead.Hdg) &&
                    Airfield.Emergency(Tower.All, lead.X, lead.Z, lead.Side) is { } af && Dist(st.X, st.Z, lead.X, lead.Z) > 40 * NM &&
                    Dist(lead.X, lead.Z, af.X, af.Z) < Dist(st.X, st.Z, af.X, af.Z) - 20 * NM && HdgDiff(lead.Hdg * 180 / Math.PI, Bearing(lead.X, lead.Z, af.X, af.Z)) < 20;
        if (!home) { rtbSince.Remove(grp); return; }
        rtbSince.TryAdd(grp, now);
        if (now - rtbSince[grp] >= 60 && Once("rtb:" + grp, now, 1e9))
        {
            Abm.Release(lead.Side, fl);   // LK9: home -> assignment free
            Say(now, 0.5, "fence", lead, $"{Fence(lead)}, fence out.", Intra(lead, w), 2, 20);
            Say(now, Reaction(), "rtb", lead, $"{aw!.Value.Name}, {fl}, RTB, checking out.", tac, 2, 20);   // LD7: check-out, then Approach (Home)
            Home(lead);
            SayAwacs(now, 3 + Rnd(), "rtb", lead.Side, aw.Value.Name, $"{fl}, {aw.Value.Name}, check out approved.", tac, 2);
        }
    }

    // ------------------------------------------------------------ LK15: state (ops check, say status)
    static string Num(int n) => n is >= 0 and <= 9 ? DigitWords[n] : Digits(n.ToString(CultureInfo.InvariantCulture));
    /// Fuel in 1000 lb as on the radio: "six point two" (internal tank from the mission, ponytail: without data 3200 kg like an F-16)
    static string Lbs(Unit u)
    {
        int t = (int)Math.Round(u.Fuel * (u.FuelKg > 0 ? u.FuelKg : 3200) * 2.20462 / 100);   // Tenths of 1000 lb
        return $"{Num(t / 10)} point {DigitWords[t % 10]}";
    }
    static int Fox3(Unit u) => u.Ammo.Length >= 3 ? u.Ammo[0] + u.Ammo[1] : 0;
    static int Fox2(Unit u) => u.Ammo.Length >= 3 ? u.Ammo[2] : 0;
    static string State(IReadOnlyCollection<Unit> m) => $"{Lbs(m.MinBy(u => u.Fuel)!)}, {Num(m.Sum(Fox3))} and {Num(m.Sum(Fox2))}";

    /// KF55 OPS CHECK: Lead "Ford, ops check, one, six point four, four and two." – "two, six point two, four and two." (fuel in 1000 lb, Fox-3/Fox-1 and Fox-2) on the flight frequency.
    static void OpsCheck(double now, World w, string grp, Unit lead)
    {
        if (Cs2(lead) is not { Success: true } c) return;   // Russian digit callsigns: no element numbers
        double d = 0;
        foreach (var u in Units.Values.Where(u => u.Group == grp && u.InAir).OrderBy(u => u.Callsign, StringComparer.Ordinal))
        {
            var n = Cs2(u) is { Success: true } e ? DigitWords[e.Groups[3].Value[0] - '0'] : "one";
            Say(now, d, "ops", u, $"{(u == lead ? $"{c.Groups[1].Value}, ops check, " : "")}{n}, {State(new[] { u })}.", Intra(u, w), 2, 20);
            d += u == lead ? 4 : 2.5;
        }
    }

    /// KF64: player asks an AI flight "Ford one, say status" / "Ford one two, say status" (ATP 1-02.1 STATUS: tactical situation) -> reply on the tactical frequency:
    /// "Ford one, engaged north group, six point two, four and two." (engaged/targeted/on station, lowest fuel, Fox-3 and Fox-2). false = no such AI flight.
    public static bool Ask(string normalized, int side, double now, World w)
    {
        if (Mode == "aus") return false;
        // LD15: coordination among leads "Ford one, Dagger one, targeted south group" -> the group belongs to the speaker (Abm.Claim, Ford gets skip it), Ford acknowledges
        if (Regex.Match(normalized, @"^([a-z]{3,}) (\d)(?: \d)? ([a-z]{3,}) (\d)\b.*\btarget(?:ed|ing) (\w+ group)\b") is { Success: true } lm && lm.Groups[3].Value != "overlord" &&
            Units.Values.FirstOrDefault(u => u.InAir && u.Side == side && Cs2(u) is { Success: true } c && c.Groups[1].Value.ToLowerInvariant() == lm.Groups[1].Value && c.Groups[2].Value == lm.Groups[2].Value) is { } kl &&
            Units.Values.Where(x => x.InAir && x.Side != side && x.Side != 0).GroupBy(x => x.Group).FirstOrDefault(g => Named(side, g.Key) == lm.Groups[5].Value) is { } lg)
        {
            var me = SpokenCallsign($"{char.ToUpperInvariant(lm.Groups[3].Value[0])}{lm.Groups[3].Value[1..]} {lm.Groups[4].Value}");
            Abm.Claim(side, me, lg.Select(x => x.Name.GetHashCode()).ToArray(), lm.Groups[5].Value, lg.Count(), now);
            var cur = fight.TryGetValue(kl.Group, out var f0) && now - f0.At < 300 ? Named(side, f0.Grp) : tgt.TryGetValue(kl.Name, out var t0) ? Named(side, t0) : null;
            Say(now, 1.5 + Rnd(), "status", kl, cur != null && cur != lm.Groups[5].Value ? $"{Fl(kl)}, targeted {cur}." : $"{Fl(kl)}.", Tac(kl, w), 1, 20);   // if Ford already has another group, he says which
            return true;
        }
        if (Regex.Match(normalized, @"\b([a-z]{3,}) (\d)(?: (\d))? (?:.* )?say (status|position)\b") is not { Success: true } m) return false;
        var fl = Units.Values.Where(u => u.InAir && u.Side == side && Cs2(u) is { Success: true } c && c.Groups[1].Value.ToLowerInvariant() == m.Groups[1].Value && c.Groups[2].Value == m.Groups[2].Value)
                     .OrderBy(u => u.Callsign, StringComparer.Ordinal).ToList();
        var who = m.Groups[3].Success ? fl.Where(u => Cs2(u).Groups[3].Value == m.Groups[3].Value).ToList() : fl;
        if (who.Count == 0) return false;
        var sp = who[0];
        string grp = sp.Group;
        var st = fight.TryGetValue(grp, out var f) && now - f.At < 300 ? "engaged" + (Named(side, f.Grp) is { } n1 ? " " + n1 : "")
               : tgt.TryGetValue(sp.Name, out var tg) ? "targeted" + (Named(side, tg) is { } n2 ? " " + n2 : "")
               : station.ContainsKey(grp) ? "on station" : null;
        var name = m.Groups[3].Success ? Cs(sp) : Fl(sp);
        Say(now, 1.5 + Rnd(), "status", sp, m.Groups[4].Value == "position" ? $"{name}{Opt(Bulls(sp))}, {Ops.Angels(sp.Alt)}." : $"{name}{Opt(st)}, {State(who)}.", Tac(sp, w), 1, 20);   // LD15: say position
        return true;
    }

    /// New mission: forget everything.
    public static void Reset() { pending.Clear(); told.Clear(); kills.Clear(); shotAt.Clear(); sent.Clear(); ordered.Clear(); downAt.Clear(); down.Clear(); level.Clear(); station.Clear(); rtbSince.Clear(); tgt.Clear(); fight.Clear(); flying.Clear(); spiked.Clear(); dead.Clear(); opsAt.Clear(); Abm.Reset(); lastScan = 0; }

    // ------------------------------------------------------------ Self-test
    public static void SelfTest(Action<bool, string> check)
    {
        var (bulls, rnd, mode, lbl) = (new Dictionary<int, (double, double)>(Ops.Bulls), Rnd, Mode, LabelOf);
        LabelOf = (_, _) => null;   // without picture names until a test sets them
        Ops.Bulls = new() { [2] = (0, 0) };
        Rnd = () => 0;
        Reset();
        station["Viper"] = (0, 0);   // Viper is already on station for the combat tests (otherwise she reports after 60 s in the middle of it)
        int[] full = { 4, 0, 2, 500, 0 };
        Unit V(string n, string cs, double x, double z, double fuel = 0.8, int[]? ammo = null) => new(n, "Viper", "F-16C_50", cs, 2, x, z, 6000, true, fuel, 141, ammo ?? full);
        var bandit = new Unit("B1", "Red", "Su-27", "201", 1, 20 * NM, 0, 7000, true, 1, 0, full);
        Units = new() { ["V11"] = V("V11", "Viper11", 0, 0), ["V12"] = V("V12", "Viper12", 0, 1000), ["B1"] = bandit };
        var w = new World(new List<(int, double, double, int, string, string)> { (2, 0, 5 * NM, 9, "P1", "Enfield 1-1") }, (_, _, _) => ("Overlord", 251.0), 251);
        bool own = OwnWingmen;
        string[] X(string s) => s.Split(';');
        List<Call> Run(double t) => Tick(t, w);
        string All(List<Call> l) => string.Join(" | ", l.Select(c => $"{c.Freq:0.0}:{c.Text}"));

        OnEvent(X("X;shot;V11;Viper;2;AIM_120C;missile;aam;radar_active;B1;Red;0;0"), 100, w);
        var a = Run(103);
        check(All(a) == "251.0:Viper one, engaged. | 251.0:Viper one one, fox three, north.", "KI-Funk Fox 3, engaged als eigener Spruch des Flugs (LK12), ohne Picture-Namen die Richtung: " + All(a));
        OnEvent(X("X;shot;V11;Viper;2;AIM_120C;missile;aam;radar_active;;;0;0"), 110, w);
        OnEvent(X("X;maddog;V11"), 110, w);
        a = Run(113);
        check(All(a) == "251.0:Viper one one, maddog.", "KI-Funk Maddog: " + All(a));
        OnEvent(X("X;kill;V11;Viper;B1;Su-27;Red;1"), 120, w);
        OnEvent(X("X;kill;V11;Viper;B2;Su-27;Red;1"), 121, w);
        a = Run(130);
        check(All(a) == "251.0:Viper one one, splash two, Flanker. | 251.0:Overlord copies splash two, picture clean.", "KI-Funk Splash zwei, AWACS zählt mit (R381): " + All(a));
        OnEvent(X("X;shot;B1;Red;1;SA-11;missile;sam;radar_semi_active;V12;Viper;0;-10000"), 140, w);
        a = Run(143);
        check(a.Count == 1 && a[0].Text == "Viper one two, SAM west, defending." && a[0].Freq == 251 && a[0].Prio == 0 && a[0].Stress, "KI-Funk Defending auf der taktischen Frequenz (R379): " + All(a));
        // Flight frequency missing (0) or is the AWACS frequency: internal calls ("I'm hit", Defending) do not go on the AWACS net (A137)
        Units["C1"] = new Unit("C1", "Cobra", "F-16C_50", "Cobra11", 2, 0, 500, 6000, true, 0.8, 251, full);
        Units["C2"] = new Unit("C2", "Cobra", "F-16C_50", "Cobra12", 2, 0, 600, 6000, true, 0.8, 0, full);
        station["Cobra"] = (0, 0);   // on station: no check-in (R377) in the middle of the test
        foreach (var c in new[] { "C1", "C2" })
        {
            OnEvent(X($"X;shot;B1;Red;1;SA-11;missile;sam;radar_semi_active;{c};Cobra;0;-10000"), 144, w);
            OnEvent(X($"X;hit;B1;Red;{c};Cobra;SA-11;0.7"), 144, w);
        }
        check(Run(148).Count == 0, "KI-Funk: ohne eigene Flight-Frequenz kein Intra-Funk auf dem AWACS-Netz");
        // R252: AI Mayday after hit -> AWACS acknowledges on Guard with the nearest own airfield
        var all0 = Tower.All;
        Tower.All = new List<Airfield> { Airfield.Kutaisi() };
        OnEvent(X("X;hit;B1;Red;C1;Cobra;SA-11;0.3"), 149, w);
        a = Run(151); a.AddRange(Run(158));
        Tower.All = all0;
        check(a.Count == 2 && a[0].Text.StartsWith("Mayday, mayday, mayday, Cobra one one, hit, RTB") && a[1].Freq == Guard && a[1].Freq2 == GuardVhf &&
              Regex.IsMatch(a[1].Text, @"^Cobra one one, Overlord, copy mayday, Kutaisi bears \w+ \w+ \w+, \d+\.$"), "R252 KI-Mayday mit AWACS-Antwort: " + All(a));
        Units.Remove("C1"); Units.Remove("C2");
        OnEvent(X("X;eject;V12;Viper;0;1000"), 150, w);
        Units.Remove("V12");
        a = Run(153);
        check(All(a) == "251.0:Viper one one, one two is down, good chute, bullseye zero eight four, 1.", "KI-Funk Ausstieg: " + All(a));
        a = Run(156);
        check(a.Count == 0, "KI-Funk kein Notsender-Ton: " + All(a));
        a = Run(241);
        check(All(a) == "243.0:Mayday, mayday, mayday, Viper one two, on the ground, bullseye zero eight four, 1.", "KI-Funk Pilot am Boden: " + All(a));
        a = Run(249);
        check(All(a) == "243.0:Viper one two, Overlord, copy, SAR notified, monitor guard.", "KI-Funk AWACS auf Guard: " + All(a));
        OnEvent(X("X;shot;V11;Viper;2;AIM_9X;missile;aam;ir;B1;Red;0;0"), 258, w);   // last missile
        Units["V11"] = V("V11", "Viper11", 0, 0, fuel: 0.2, ammo: new[] { 0, 0, 0, 500, 0 });
        a = Run(260);
        a.AddRange(Run(266));
        check(All(a) == "251.0:Viper one one, fox two, north. | 251.0:Viper one, bingo, RTB. | 251.0:Viper one one, winchester. | 251.0:Viper one, Overlord, copy bingo.",
              "KI-Funk Bingo/Winchester: " + All(a));
        Units["C11"] = new Unit("C11", "Colt", "F-16C_50", "Colt11", 2, 0, 3 * NM, 6000, true, 0.8, 141, full); Units["C12"] = new Unit("C12", "Colt", "F-16C_50", "Colt12", 2, 0, 3 * NM, 6000, true, 0.2, 141, full);
        a = Run(270); a.AddRange(Run(276));
        check(All(a).Contains("141.0:Colt one two, bingo.") && All(a).Contains("251.0:Colt one, bingo, RTB.") && !All(a).Contains("Colt one two, bingo, RTB"), "LD6 Bingo des Wingman: Rottenfrequenz, Lead für den Flug taktisch: " + All(a));
        Units.Remove("C11"); Units.Remove("C12");
        // R385: refuelled above 40 % -> joker armed again
        station["Hawk"] = (0, 0);
        int Jokers(double t0, double fuel)
        {
            Units["H11"] = new Unit("H11", "Hawk", "F-16C_50", "Hawk11", 2, 0, 3 * NM, 6000, true, fuel, 141, full);
            return Run(t0).Concat(Run(t0 + 2)).Count(c => c.Text == "Hawk one one, joker.");
        }
        check(Jokers(280, 0.3) == 1 && Jokers(284, 0.8) == 0 && Jokers(288, 0.3) == 1, "R385 Joker nach dem Tanken wieder scharf");
        Units.Remove("H11");
        // far away (100 NM from the player): silent; tactical: no joker; budget: at most 10 Prio 2 calls per minute
        Units["V11"] = V("V11", "Viper11", 100 * NM, 0);
        OnEvent(X("X;shot;V11;Viper;2;AIM_120C;missile;aam;radar_active;B1;Red;0;0"), 300, w);
        check(Run(305).Count == 0, "KI-Funk: weit weg still");
        Units["V11"] = V("V11", "Viper11", 0, 0, fuel: 0.3);
        Mode = "taktisch";
        check(Run(310).Count == 0, "KI-Funk taktisch: kein Joker");
        Mode = "voll";
        for (int i = 0; i < 14; i++) OnEvent(X($"X;pitbull;V11"), 400 + 6 * i, w);
        check(Run(490).Count(c => c.Text.EndsWith("pitbull.")) == 10, "KI-Funk Budget 10 je Minute");   // plus the timeout of the shot from 258 (LK13, Prio 1)
        OnEvent(X("X;pitbull;V11"), 600, w); OnEvent(X("X;pitbull;V11"), 601.5, w);
        a = Run(610);
        check(a.Count(c => c.Text.EndsWith("pitbull.")) == 1, "R380 zwei Pitbull binnen 2 s: ein Spruch: " + All(a));
        // R379: missile from 50 NM: no "defending" while it flies out, then once when it goes active (two pitbull events)
        OnEvent(X(FormattableString.Invariant($"X;shot;B1;Red;1;R-77;missile;aam;radar_active;V11;Viper;0;{50 * NM:0}")), 620, w);
        a = Run(630); a.AddRange(Run(640));
        bool early = a.Any(c => c.Text.Contains("defending"));
        OnEvent(X("X;pitbull;B1;V11"), 641, w); OnEvent(X("X;pitbull;B1;V11"), 642, w);
        a = Run(645); a.AddRange(Run(650));
        check(!early && a.Count(c => c.Text.Contains("defending")) == 1, $"R379 Schuss aus 50 NM: defending erst bei Pitbull, einmal (früh {early}): " + All(a));
        // R384: AWACS tasking and its acknowledgment are one exchange: after 9 calls in the minute the tasking (10th) goes and the ack still comes;
        // the other side has its own budget; a dropped tasking drops its ack
        Call P(string kind, string who, string text, int side = 2) => new(kind, who, "", text, 251, 2, 15, Side: side);
        for (int i = 0; i < 9; i++) pending.Add((800, P("pitbull", "V11", $"Viper one one, pitbull {i}.")));
        pending.Add((800.1, P("commit", "", "Viper one, Overlord, commit group.")));
        pending.Add((800.2, P("commit", "V11", "Viper one, committing.")));
        pending.Add((800.3, P("pitbull", "V11", "Viper one one, pitbull extra.")));
        pending.Add((800.4, P("pitbull", "B1", "Red one, pitbull.", 1)));
        a = Run(801);
        check(a.Any(c => c.Text.Contains("commit group")) && a.Any(c => c.Text == "Viper one, committing.") && !a.Any(c => c.Text.Contains("extra")) && a.Any(c => c.Side == 1),
              "R384 Zuweisung und Quittung trotz Budget, andere Seite eigenes Budget: " + All(a));
        pending.Add((820, P("commit", "", "Viper one, Overlord, commit group.")));
        a = Run(821);
        pending.Add((865, P("commit", "V11", "Viper one, committing.")));
        a.AddRange(Run(866));
        check(!a.Any(c => c.Kind == "commit"), "R384 verworfene Zuweisung: keine Quittung: " + All(a));
        // Player ejects (N45): no T entry, still beacon, Mayday and AWACS reply on Guard; an AI wingman reports him only with OwnWingmen
        Units["W1"] = new Unit("W1", "Enfield", "F-16C_50", "Enfield12", 2, 0, 4 * NM, 6000, true, 0.8, 141, full, Gid: 9);
        OnEvent(X("X;eject;Niemand;Enfield;0;0"), 1000, w);
        check(Run(1100).All(c => c.Kind == "ops"), "KI-Funk Spieler unbekannt: still");   // ops check of the Viper (LK15) after 15 min does not count
        OnEvent(X("X;eject;P1;Enfield;0;0"), 1200, w);
        check(Run(1204).Count == 0, "KI-Funk Spieler-Ausstieg: der Spieler meldet sich selbst, Rottenflieger ohne OwnWingmen still");
        a = Run(1206);
        check(a.Count == 0, "KI-Funk kein Spieler-Notsender-Ton: " + All(a));
        a = Run(1291);
        check(All(a) == "243.0:Mayday, mayday, mayday, Enfield one one, on the ground, bullseye zero eight four, 5.", "KI-Funk Spieler am Boden: " + All(a));
        a = Run(1299);
        check(All(a) == "243.0:Enfield one one, Overlord, copy, SAR notified, monitor guard.", "KI-Funk AWACS-SAR für Spieler: " + All(a));
        OwnWingmen = true;
        OnEvent(X("X;eject;P1;Enfield;0;0"), 1400, w);
        a = Run(1403);
        check(All(a) == "251.0:Enfield one two, one one is down, good chute, bullseye zero eight four, 5.", "KI-Funk Spieler-Ausstieg, KI-Rottenflieger meldet: " + All(a));
        Run(1406); Run(1491); Run(1499);
        OwnWingmen = own;
        Units.Remove("W1");
        // R361: over water "in the water"; player flying again after a respawn or pilot dead -> no SAR call
        OnEvent(X("X;eject;P1;Enfield;0;0;1"), 1500, w);
        a = Run(1591); Run(1599);
        check(All(a) == "243.0:Mayday, mayday, mayday, Enfield one one, in the water, bullseye zero eight four, 5.", "R361 Ausstieg über Wasser: " + All(a));
        OnEvent(X("X;eject;P1;Enfield;0;0;0"), 1600, w);
        Tick(1630, w with { Players = new List<(int, double, double, int, string, string)> { (2, 0, 20 * NM, 9, "P1", "Enfield 1-1") } });   // airborne again, 15 NM away
        OnEvent(X("X;eject;P1;Enfield;0;0;0"), 1700, w);
        OnEvent(X("X;pilotdead;P1"), 1730, w);
        a = Run(1800);
        check(!a.Any(c => c.Kind == "guard"), "R361 Spieler fliegt wieder / Pilot tot: keine SAR-Meldung: " + All(a));
        // merged only with altitude difference under 5000 ft (A113); picture clean only without detected contacts in the radar picture
        Units["V11"] = V("V11", "Viper11", 0, 0);
        Units["B1"] = bandit with { X = NM, Z = 0, Alt = 6000 + 3000 };   // 1 NM away, 3000 m (9800 ft) higher
        check(!Run(2000).Any(c => c.Text.EndsWith("merged.")), "KI-Funk: 1 NM, aber 9800 ft höher: kein merged");
        Units["B1"] = bandit with { X = NM, Z = 0, Alt = 6000 + 1000 };   // 3300 ft higher
        a = Run(2004); a.AddRange(Run(2006));
        check(a.Any(c => c.Text == "Viper one one, merged."), "KI-Funk: 1 NM, 3300 ft höher: merged: " + All(a));
        // R383: a helicopter 1 NM away is no merge, a fighter at 1.5 NM is
        told.Remove("merged:Viper");
        Units["B1"] = bandit with { Type = "Mi-24P", X = NM, Heli = true };
        a = Run(2010); a.AddRange(Run(2012));
        check(!a.Any(c => c.Text.EndsWith("merged.")), "R383 Hubschrauber in 1 NM: kein merged: " + All(a));
        Units["B1"] = bandit with { X = 1.5 * NM };
        a = Run(2014); a.AddRange(Run(2016));
        check(a.Any(c => c.Text == "Viper one one, merged."), "R383 Jäger in 1,5 NM: merged: " + All(a));
        Units["B1"] = bandit; Units["B3"] = bandit with { Name = "B3", Group = "Red2", X = 10 * NM };   // other group: no remaining contact of the splashed group (R381)
        Ops.Detected[2] = new() { [999] = true };   // B3 is not in the side's radar picture
        OnEvent(X("X;kill;V11;Viper;B1;Su-27;Red;1"), 2100, w);
        a = Run(2108);
        check(All(a).Contains("copies splash one, picture clean."), "KI-Funk Splash: nicht erfasster Kontakt zählt nicht (picture clean): " + All(a));
        Ops.Detected[2] = new() { ["B3".GetHashCode()] = true };
        OnEvent(X("X;kill;V11;Viper;B1;Su-27;Red;1"), 2200, w);
        a = Run(2208);
        check(All(a).Contains("copies splash one.") && !All(a).Contains("clean"), "KI-Funk Splash: erfasster Kontakt in 10 NM: kein picture clean: " + All(a));
        Units["B3"] = Units["B3"] with { X = 220 * NM };   // R53: same radius as Ops (whole radar picture, RadarNm): 220 NM beyond NewGroupNm still counts
        OnEvent(X("X;kill;V11;Viper;B1;Su-27;Red;1"), 2300, w);
        a = Run(2308);
        check(All(a).Contains("copies splash one.") && !All(a).Contains("clean"), "KI-Funk Splash R53: erfasster Kontakt in 220 NM: kein picture clean: " + All(a));
        // R381: remaining detected contact of the same group with its picture name
        Units["B3"] = bandit with { Name = "B3", X = 10 * NM };
        LabelOf = (_, id) => id == "B3".GetHashCode() ? "north group" : null;
        OnEvent(X("X;kill;V11;Viper;B1;Su-27;Red;1"), 2400, w);
        a = Run(2408);
        LabelOf = (_, _) => null;
        check(All(a).Contains("Overlord copies splash one, north group, one contact."), "R381 Splash mit Restkontakt der Gruppe: " + All(a));
        Ops.Detected.Clear(); Units.Remove("B3"); Units["B1"] = bandit; dead.Clear();
        var fp = new Unit("P2", "", "", "Iceman", 2, 0, 0, 0, false, 0, 0, Array.Empty<int>());   // Player with free-text callsign
        check(Cs(fp) == "Iceman" && Elem(fp) == "Iceman", "KI-Funk Spieler-Rufzeichen ohne Muster: " + Cs(fp));
        var g = OnGuard("Enfield one one, Overlord, threat, group BRAA 354, 20, hot.", "Overlord", 251);
        check(g == "Enfield one one, Overlord on guard, threat, group BRAA 354, 20, hot. Contact Overlord two five one decimal zero.", "Guard-Warnung: " + g);
        check(Nato("MiG-29S") == "Fulcrum" && Nato("IL-78M") == "Midas" && Nato("F-16C_50") == null, "NATO-Namen");
        // R299: hit, then kill or ejection before sending: no "hit, RTB", no AWACS acknowledgment with heading to the airfield
        Units["V13"] = V("V13", "Viper13", 0, 2000); Units["V14"] = V("V14", "Viper14", 0, 3000);
        OnEvent(X("X;hit;B1;Red;V13;Viper;SA-11;0.3"), 3000, w);
        OnEvent(X("X;kill;B1;Red;V13;Viper;F-16C_50;1"), 3001, w);
        OnEvent(X("X;hit;B1;Red;V14;Viper;SA-11;0.3"), 3000, w);
        a = Run(3002.5);
        OnEvent(X("X;eject;V14;Viper;0;3000"), 3003, w);   // Mayday already sent, acknowledgment still pending
        a.AddRange(Run(3015));
        check(a.Count(c => c.Text.Contains("RTB")) == 1 && a.Single(c => c.Text.Contains("RTB")).Text.Contains("Viper one four") && !a.Any(c => c.Text.Contains("copy mayday")),
              "R299 kein Mayday/AWACS-Kurs nach Abschuss/Ausstieg: " + All(a));
        Units.Remove("V13"); Units.Remove("V14");
        // R378: fox, shooter shot down 1 s later -> no "fox"/"engaged" from him any more, his "ejecting" still goes
        station["Snake"] = (0, 0);
        Units["S13"] = V("S13", "Snake13", 0, 2000) with { Group = "Snake" };
        OnEvent(X("X;shot;S13;Snake;2;AIM_120C;missile;aam;radar_active;B1;Red;0;0"), 3100, w);
        OnEvent(X("X;kill;B1;Red;S13;Snake;F-16C_50;1"), 3101, w);
        OnEvent(X("X;eject;S13;Snake;0;2000;0"), 3102, w);
        Units.Remove("S13");
        a = Run(3110);
        check(!a.Any(c => c.Text.Contains("fox") || c.Text.Contains("engaged")) && a.Any(c => c.Text.Contains("ejecting")), "R378 abgeschossener Schütze schweigt: " + All(a));
        down.Clear();
        // R311: on station, commit/committing, targeted, RTB replace the DCS radio of the AI fighters (R308 mutes it)
        Reset(); Ops.Detected.Clear();
        const double ft = 0.3048;
        Unit F(string n, string cs, double x, double z, double alt, double hdg = 0) => new(n, "Ford", "F-16C_50", cs, 2, x, z, alt, true, 0.8, 141, full, Hdg: hdg);
        Unit R(string n, string grp, double x, double z) => new(n, grp, "Su-27", "201", 1, x, z, 7000, true, 1, 0, full, Hdg: Math.PI);   // Heading south = hot on Ford
        Units = new() { ["F11"] = F("F11", "Ford11", 35 * NM, 0, 8000 * ft), ["F12"] = F("F12", "Ford12", 35 * NM, 1000, 8000 * ft) };
        Run(4990);
        Units = new() { ["F11"] = F("F11", "Ford11", 35 * NM, 0, 20000 * ft), ["F12"] = F("F12", "Ford12", 35 * NM, 1000, 20000 * ft) };
        a = Run(5000); a.AddRange(Run(5010));   // LD7/R377: check-in on first contact once ours, no altitude gate (8000 ft)
        check(All(a) == "251.0:Overlord, Ford one, checking in as fragged, flight of two, angels 8, playtime 55. | 251.0:Ford one, Overlord, radar contact.", "LD7/R377 check-in beim ersten Kontakt, auch unter 10000 ft: " + All(a));
        Units["F11"] = F("F11", "Ford11", 35 * NM, 0, 25000 * ft); Units["F12"] = F("F12", "Ford12", 35 * NM, 1000, 25000 * ft);   // still climbing
        a = Run(5030); a.AddRange(Run(5062));
        check(a.Count == 0, "KI-Funk on station: erst nach 60 s auf derselben Höhe: " + All(a));
        a = Run(5092); a.AddRange(Run(5100));
        check(All(a) == $"251.0:Overlord, Ford one, on station, bullseye {Ops.Brg(0)}, 35, angels 25. | 141.0:Ford, fence in. | 251.0:Ford one, Overlord, picture clean.", "KI-Funk on station, LD12 fence in: " + All(a));
        var cmds = new List<string>(); var cmd0 = Cmd; Cmd = cmds.Add;   // LD1
        Units["R1"] = R("R1", "Red", 65 * NM, 0); Units["R2"] = R("R2", "Red", 65 * NM, 500);
        a = Run(5110); a.AddRange(Run(5115));
        check(All(a) == $"251.0:Ford one, Overlord, commit group, bullseye {Ops.Brg(0)}, 65, 23 thousand, hostile, two contacts, Flanker. | 251.0:Ford one, committing. | " +
                        "251.0:Ford one, targeted. | 251.0:Ford one, sorted.", "LK9 KI-Flug: AWACS weist zu (commit mit Ort, LK4 Bullseye), committing, targeted ohne zweiten Ort (LK10), zwei Kontakte: sorted (LK11): " + All(a));
        check(Run(5130).Count == 0 && Abm.TargetedBy(2, "R1".GetHashCode()) == "Ford one", "LK9 commit je Gruppe nur einmal, Zuweisung in der Tabelle des AWACS");
        check(string.Join(" ", cmds) == "ENGAGE;Ford;Red", "LD1: commit -> Missionsskript ENGAGE: " + string.Join(" ", cmds));
        Cmd = cmd0;
        Units["R3"] = R("R3", "Red2", 55 * NM, 0);   // second group, 10 NM south of the first
        a = Run(5140); a.AddRange(Run(5145));
        check(a.Count == 0, "LK9 zweite Gruppe: Ford hat schon eine, kein anderer Flug -> kein eigenes commit mehr: " + All(a));
        // second flight on station: the AWACS gives it the free group; shot right after -> committing/targeted dropped
        Units["D1"] = new Unit("D1", "Dodge", "F-16C_50", "Dodge11", 2, 35 * NM, 3 * NM, 25000 * ft, true, 0.8, 141, full, Hdg: 0); station["Dodge"] = (35 * NM, 3 * NM);
        a = Run(5150);
        OnEvent(X("X;shot;D1;Dodge;2;AIM_120C;missile;aam;radar_active;R3;Red2;0;0"), 5150.2, w);
        a.AddRange(Run(5156));
        check(All(a).StartsWith("251.0:Dodge one, Overlord, commit group, bullseye") && All(a).Contains("fox three") && !All(a).Contains("committing") && !All(a).Contains("targeted"),
              "LK9 zweiter Flug bekommt die freie Gruppe; committing/targeted nur vor dem ersten Schuss: " + All(a));
        Units.Remove("R1"); Units.Remove("R2");   // Ford's group gone (splash): reset
        a = Run(5160); a.AddRange(Run(5165));
        check(All(a) == "251.0:Ford one, Overlord, reset. | 251.0:Ford one, resetting.", "LK9 Gruppe weg -> reset, resetting: " + All(a));
        // R376/R377: CAP flight at 6000 ft checks in, goes on station and commits; a SEAD flight next to it stays silent and gets no ENGAGE
        Reset(); Ops.Detected.Clear();
        var cmds376 = new List<string>(); Cmd = cmds376.Add;
        Units = new() { ["F11"] = F("F11", "Ford11", 35 * NM, 0, 6000 * ft) with { Task = "CAP" }, ["R1"] = R("R1", "Red", 65 * NM, 0),
                        ["S11"] = new Unit("S11", "Weasel", "F-16C_50", "Weasel11", 2, 35 * NM, 2 * NM, 6000 * ft, true, 0.8, 141, full, Hdg: 0, Task: "SEAD") };
        a = new();
        foreach (var t in new[] { 5200.0, 5202, 5210, 5262, 5264, 5270, 5272, 5280, 5290 }) a.AddRange(Run(t));
        Cmd = cmd0;
        check(All(a).Contains("Ford one, checking in") && All(a).Contains("Ford one, on station") && All(a).Contains("Ford one, Overlord, commit") && !All(a).Contains("Weasel") &&
              string.Join(" ", cmds376) == "ENGAGE;Ford;Red", "R376/R377 CAP meldet sich und committet, SEAD still: " + All(a) + " / " + string.Join(" ", cmds376));
        // LK10: group names from the AWACS (Ops.GroupLabel): commit and targeted with names, location only in the commit; LK9: the second flight holds the CAP (maintain), skip it
        Reset(); (station["Ford"], station["Dodge"]) = ((0, 0), (0, 0));
        LabelOf = (s, id) => s != 2 ? null : id == "R1".GetHashCode() ? "west group" : id == "R3".GetHashCode() ? "east group" : null;
        Units = new() { ["F11"] = F("F11", "Ford11", 35 * NM, 0, 25000 * ft), ["F12"] = F("F12", "Ford12", 35 * NM, 1000, 25000 * ft), ["R1"] = R("R1", "Red", 65 * NM, 0),
                        ["D1"] = new Unit("D1", "Dodge", "F-16C_50", "Dodge11", 2, 35 * NM, 3 * NM, 25000 * ft, true, 0.8, 141, full, Hdg: 0) };
        a = Run(5200); a.AddRange(Run(5205));
        check(All(a) == $"251.0:Ford one, Overlord, commit west group, bullseye {Ops.Brg(0)}, 65, 23 thousand, hostile, single, Flanker. | 251.0:Ford one, committing. | 251.0:Ford one, targeted west group. | " +
                        "251.0:Dodge one, Overlord, maintain CAP. | 251.0:Dodge one.", "LK10 commit/targeted mit Gruppennamen, LK9 maintain CAP: " + All(a));
        check(Abm.Want(2, "Dodge one", new[] { "R1".GetHashCode() }, "west group", 1, 5206) == "Ford one", "LK9 Want: Gruppe hat schon Ford");
        a = Run(5208); a.AddRange(Run(5212));
        check(All(a) == "251.0:Dodge one, Overlord, skip it, west group Ford one targeted. | 251.0:Dodge one.", "LK9 skip it -> Dodge one: " + All(a));
        Units.Remove("D1");
        // LK11: four-ship: the second element (Ford one three) takes the second group, no sorted (each group one contact)
        Reset(); station["Ford"] = (0, 0);
        Units = new() { ["F11"] = F("F11", "Ford11", 35 * NM, 0, 25000 * ft), ["F12"] = F("F12", "Ford12", 35 * NM, 1000, 25000 * ft), ["F13"] = F("F13", "Ford13", 35 * NM, 2000, 25000 * ft),
                        ["F14"] = F("F14", "Ford14", 35 * NM, 3000, 25000 * ft), ["R1"] = R("R1", "Red", 65 * NM, 0), ["R3"] = R("R3", "Red2", 55 * NM, 0) };
        a = Run(5300); a.AddRange(Run(5305));
        check(All(a).EndsWith("251.0:Ford one, committing. | 251.0:Ford one one, targeted east group. | 251.0:Ford one three, targeted west group.") && !All(a).Contains("sorted"),
              "LK11 Vierer: Elemente nehmen verschiedene Gruppen: " + All(a));
        // LK12: fox with group name; after targeted no engaged; otherwise "engaged north group" as its own call before; single group: only "fox three"
        OnEvent(X("X;shot;F13;Ford;2;AIM_120C;missile;aam;radar_active;R1;Red;0;0"), 5310, w);
        a = Run(5313);
        check(All(a) == "251.0:Ford one three, fox three, west group.", "LK12 fox mit Gruppenname, nach targeted kein engaged: " + All(a));
        Units["D11"] = new Unit("D11", "Dodge", "F-16C_50", "Dodge11", 2, 0, 0, 25000 * ft, true, 0.8, 141, full, Hdg: 0); station["Dodge"] = (0, 0);   // 55 NM away: no commit (LD2: within 60 NM of an assigned group "maintain CAP")
        OnEvent(X("X;shot;D11;Dodge;2;AIM_120C;missile;aam;radar_active;R3;Red2;0;0"), 5320, w);
        a = Run(5324);
        check(All(a) == "251.0:Dodge one, engaged east group. | 251.0:Dodge one one, fox three, east group.", "LK12 engaged als eigener Spruch mit Gruppe: " + All(a));
        LabelOf = (_, _) => "single group";
        OnEvent(X("X;shot;D11;Dodge;2;AIM_9X;missile;aam;ir;R1;Red;0;0"), 5330, w);
        a = Run(5334);
        check(All(a).EndsWith("251.0:Dodge one, engaged single group. | 251.0:Dodge one one, fox two."), "LK12 einzige Gruppe: fox ohne Zusatz: " + All(a));
        // LK13: lead turns more than 120° away from the target after the shot -> "out south" (once); time of flight 5 s + 3 s per NM over without hit -> "timeout", afterwards no "trashed"
        Units["D11"] = Units["D11"] with { Hdg = Math.PI };
        a = Run(5340); a.AddRange(Run(5344));
        check(All(a) == "251.0:Dodge one, pump south.", "LK13 out nach dem Schuss, LD12 pump (Sprit und Fox 3 übrig): " + All(a));
        a = Run(5480);
        check(!All(a).Contains("Dodge"), "LK13 vor Ablauf der Flugzeit kein timeout: " + All(a));
        a = Run(5492);   // Shot 5320 at R3 from 55 NM: 5320 + 5 + 165
        check(a.Count(c => c.Text.StartsWith("Dodge")) == 1 && a.Any(c => c.Text == "Dodge one one, timeout."), "LK13 timeout: " + All(a));
        OnEvent(X("X;trashed;D11;R3"), 5495, w);
        OnEvent(X("X;trashed;D11;R1"), 5496, w);   // second missile (at R1, time until 5530) still underway: trashed as before
        a = Run(5499); a.AddRange(Run(5540));
        check(a.Count(c => c.Text.StartsWith("Dodge")) == 1 && a.Any(c => c.Text == "Dodge one one, trashed."), "LK13 nach timeout kein trashed, sonst trashed und kein timeout: " + All(a));
        // LK14: radar lock (KF6) -> spike with bearing and type or mud spike with system and direction on the flight frequency, then naked; brief lock before sending: both dropped
        List<Call> Intra141(double t) => Run(t).Where(c => c.Freq == 141).ToList();
        OnEvent(X($"X;spike;D11;Su-27;air;0;{(int)(-20 * NM)}"), 5600, w);
        OnEvent(X($"X;spike;D11;Kub 1S91 str;sam;0;{(int)(20 * NM)}"), 5600, w);
        a = Intra141(5603);
        check(All(a) == $"141.0:Dodge one one, spike {Ops.Brg(270)}, Flanker. | 141.0:Dodge one one, mud spike, SA-6, east.", "LK14 spike/mud spike: " + All(a));
        OnEvent(X("X;naked;D11"), 5610, w);
        a = Intra141(5613);
        check(All(a) == "141.0:Dodge one one, naked.", "LK14 naked: " + All(a));
        OnEvent(X($"X;spike;D11;Su-27;air;0;{(int)(-20 * NM)}"), 5620, w);
        OnEvent(X("X;naked;D11"), 5620.1, w);
        OnEvent(X("X;naked;D11"), 5621, w);
        check(Intra141(5625).Count == 0, "LK14 kurzer Lock: weder spike noch naked");
        // LK15: ops check every 15 min on the flight frequency (fuel in 1000 lb, Fox 3 and Fox 2); "say status" to an AI flight
        check(!Intra141(6199).Any(c => c.Text.Contains("ops check")), "LK15 ops check nicht vor 15 min");
        a = Intra141(6201); a.AddRange(Intra141(6211));
        check(All(a) == "141.0:Ford, ops check, one, five point six, four and two. | 141.0:two, five point six, four and two. | 141.0:three, five point six, four and two. | 141.0:four, five point six, four and two.",
              "LK15 ops check: " + All(a));
        LabelOf = (s, id) => s != 2 ? null : id == "R1".GetHashCode() ? "west group" : id == "R3".GetHashCode() ? "east group" : null;
        Units["D11"] = Units["D11"] with { FuelKg = 3249 };   // F-16 internal tank from the mission (7163 lb)
        check(Ask("ford 1 say status", 2, 6220, w) && Ask("overlord dodge 1 1 say status", 2, 6220, w) && !Ask("enfield 1 say status", 2, 6220, w) && !Ask("ford 1 picture", 2, 6220, w),
              "LK15 say status: nur an bekannte KI-Flüge");
        OnEvent(X("X;shot;D11;Dodge;2;AIM_120C;missile;aam;radar_active;R1;Red;0;0"), 6221, w);
        Ask("dodge 1 say status", 2, 6222, w);
        a = Run(6225).Where(c => c.Kind == "status").ToList();
        check(All(a) == "251.0:Ford one, targeted east group, five point six, one six and eight. | 251.0:Dodge one one, on station, five point seven, four and two. | " +
                        "251.0:Dodge one, engaged west group, five point seven, four and two.", "LK15 say status: " + All(a));
        check(Ask("ford 1 say position", 2, 6230, w) && Run(6233).Where(c => c.Kind == "status").Select(c => c.Text).FirstOrDefault() is { } pos15 && pos15.StartsWith("Ford one, bullseye ") && pos15.Contains("angels"), "LD15 say position");
        check(Ask("ford 1 dagger 1 targeted east group", 2, 6240, w) && Abm.TargetedBy(2, "R3".GetHashCode()) == "Dagger one" && Run(6243).Any(c => c.Text == "Ford one."), "LD15 Absprache unter Leads");
        check(Ask("dodge 1 dagger 1 targeted east group", 2, 6250, w) && Run(6253).Any(c => c.Text == "Dodge one, targeted west group."), "LD15 Absprache: KI hat schon eine andere Gruppe");
        Units.Remove("D11");
        LabelOf = (_, _) => null;
        // RTB: 50 NM from the station point, heading to Kutaisi and 50 NM closer to it -> after 60 s; away from the airfield: silent
        Reset();
        Tower.All = new List<Airfield> { Airfield.Kutaisi() };
        var kf = Tower.All[0];
        var w2 = w with { Players = new List<(int, double, double, int, string, string)> { (2, kf.X + 30 * NM, kf.Z, 9, "P1", "Enfield 1-1") } };
        Units = new() { ["F11"] = F("F11", "Ford11", kf.X + 30 * NM, kf.Z, 25000 * ft), ["F12"] = F("F12", "Ford12", kf.X + 30 * NM, kf.Z + 1000, 25000 * ft) };
        station["Ford"] = (kf.X + 80 * NM, kf.Z);
        a = Tick(6000, w2); a.AddRange(Tick(6030, w2)); a.AddRange(Tick(6062, w2)); a.AddRange(Tick(6070, w2));
        check(a.Count == 0, "KI-Funk RTB: Kurs weg vom Platz: still: " + All(a));
        Units["F11"] = Units["F11"] with { Hdg = Math.PI };   // Heading south to the airfield
        a = Tick(6072, w2); a.AddRange(Tick(6100, w2));
        check(a.Count == 0, "KI-Funk RTB: erst nach 60 s: " + All(a));
        var c7 = new List<string>(); var cmd7 = Cmd; Cmd = c7.Add;
        a = Tick(6134, w2); a.AddRange(Tick(6140, w2));
        check(All(a) == "251.0:Overlord, Ford one, RTB, checking out. | 141.0:Ford, fence out. | 251.0:Ford one, Overlord, check out approved." && string.Join(" ", c7) == "RTB;Ford;Kutaisi", "KI-Funk RTB, LD7 check-out und Übergabe: " + All(a) + " | " + string.Join(" ", c7));
        Cmd = cmd7;
        // LD1 mode funk (AiFollowAwacs off): the AWACS assigns nothing to AI flights, the flight commits itself (group hot under 40 NM) and reports it to the AWACS;
        // Self-defense (mode awacs): missile at Ford -> ENGAGE on the shooter's group
        Reset(); Abm.Reset(); Follow = false;
        Units = new() { ["F11"] = F("F11", "Ford11", 35 * NM, 0, 25000 * ft), ["F12"] = F("F12", "Ford12", 35 * NM, 1000, 25000 * ft), ["R1"] = R("R1", "Red", 65 * NM, 0) };
        station["Ford"] = (35 * NM, 0);
        a = Run(7000); a.AddRange(Run(7005));
        Follow = true;
        check(All(a).StartsWith("251.0:Overlord, Ford one, committing. | 251.0:Ford one, targeted") && !All(a).Contains("commit group") && Abm.TargetedBy(2, "R1".GetHashCode()) == "Ford one", "LD1 Modus funk: " + All(a));
        var c1 = new List<string>(); var cmd1 = Cmd; Cmd = c1.Add;
        OnEvent(X("X;shot;R1;Red;1;R-27ER;missile;aam;radar_semi_active;F11;Ford;0;0"), 7010, w);
        Cmd = cmd1;
        check(string.Join(" ", c1) == "ENGAGE;Ford;Red", "LD1 Selbstverteidigung: " + string.Join(" ", c1));
        // LD13 script ch. 4, AI part: Ford checks in, Su-27 pair from the north, commit, exchange of fire, two splash, reset, bingo.
        // Expected: order of the calls, no Fox/Defending/Splash lost, per frequency at most 12 calls per minute, none over 6 s (3 words/s; Picture 10 s)
        Reset(); Abm.Reset();
        var dz = new List<(double T, Call C)>();
        void Step(double t) { foreach (var c in Run(t)) dz.Add((t, c)); }
        Unit Su(string n, double x) => R(n, "Red", x, -10 * NM + (n == "R2" ? 1000 : 0));
        Units = new() { ["F11"] = F("F11", "Ford11", 0, -10 * NM, 8000 * ft), ["F12"] = F("F12", "Ford12", 0, -10 * NM + 1000, 8000 * ft) };
        Step(9000);
        double sx = 62 * NM;
        for (double t = 9002; t <= 9400; t += 2)
        {
            Units["F11"] = Units["F11"] with { Alt = 25000 * ft }; Units["F12"] = Units["F12"] with { Alt = 25000 * ft, Fuel = t >= 9300 ? 0.2 : 0.8 };
            if (t >= 9080 && t < 9190) Units["R1"] = Su("R1", sx);
            if (t >= 9080 && t < 9230) Units["R2"] = Su("R2", sx);
            if (t >= 9080) sx -= 0.27 * NM;
            if (t == 9160) OnEvent(X("X;shot;F11;Ford;2;AIM_120C;missile;aam;radar_active;R1;Red;0;0"), t, w);
            if (t == 9166) OnEvent(X(FormattableString.Invariant($"X;shot;R1;Red;1;R-77;missile;aam;radar_active;F12;Ford;{sx:0};{-10 * NM:0}")), t, w);
            if (t == 9174) OnEvent(X("X;pitbull;R1;F12"), t, w);   // R379: F12 defends when the R-77 goes active
            if (t == 9176) OnEvent(X("X;pitbull;F11"), t, w);
            if (t == 9190) { Units.Remove("R1"); OnEvent(X("X;kill;F11;Ford;R1;Su-27;Red;1"), t, w); }
            if (t == 9210) OnEvent(X("X;shot;F11;Ford;2;AIM_120C;missile;aam;radar_active;R2;Red;0;0"), t, w);
            if (t == 9230) { Units.Remove("R2"); OnEvent(X("X;kill;F11;Ford;R2;Su-27;Red;1"), t, w); }
            Step(t);
        }
        var seq = dz.Select(x => x.C.Text).ToList();
        var miss = new List<string>();
        int at = 0;
        foreach (var k in new[] { "checking in as fragged", "radar contact", "on station", "fence in", "Overlord, commit", "Ford one, committing", "Ford one, targeted", "sorted",
                                  "Ford one one, fox three", "defending", "pitbull", "splash one", "copies splash one.", "fox three", "splash one", "copies splash one, picture clean", "Ford one, Overlord, reset.", "Ford one, resetting", "bingo", "copy bingo" })
            if (seq.FindIndex(at, s => s.Contains(k)) is var i && i >= 0) at = i; else miss.Add(k);
        var busy = dz.GroupBy(x => x.C.Freq).Select(g => (F: g.Key, N: g.Max(x => g.Count(y => y.T >= x.T && y.T < x.T + 60)))).Where(x => x.N > 12).ToList();
        var longs = seq.Where(s => s.Split(' ').Length > (s.Contains("picture") ? 30 : 18)).ToList();
        check(miss.Count == 0 && busy.Count == 0 && longs.Count == 0 && seq.Count(s => s.Contains("fox three")) == 2 && seq.Count(s => s.Contains("splash one")) >= 2 && seq.Count(s => s.Contains("defending")) == 1 && seq.Count(s => s.Contains("picture clean")) == 2,
              $"LD13 Drehbuch: fehlt {string.Join(", ", miss)} | voll {string.Join(", ", busy)} | lang {string.Join(" / ", longs)} || {string.Join(" | ", dz.Select(x => $"{x.T - 9000:0}:{x.C.Freq:0.0}:{x.C.Text}"))}");
        Tower.All = all0;
        Console.WriteLine("OK   KI-Funk: Fox/Maddog/Splash/Defending/Ausstieg/Guard/Bingo/Winchester, On station/Commit/Targeted/RTB, Budget, Reichweite");
        (Ops.Bulls, Rnd, Mode, LabelOf) = (bulls, rnd, mode, lbl);
        Units = new();
        Reset();
    }
}
