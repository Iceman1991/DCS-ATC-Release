using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace DcsAtc;
static partial class Program
{
    // ------------------------------------------------------------ Mission data (KutaisiATC.lua -> %TMP%\DcsAtc-State.txt)
    static readonly string StateFile = Path.Combine(Path.GetTempPath(), "DcsAtc-State.txt");
    static DateTime stateStamp, stateTime = DateTime.MinValue;
    static bool lsoWarm;
    static List<Traffic> Ai = new();
    static double aiAt;   // Time of the last AI state (climb rate)
    static Dictionary<string, (double Wx, double Wz, double TempC, double Pa)> Wx = new();
    static (bool Clouds, double BaseM, double VisM) Sky = (false, 0, 80000);
    static bool MissionData => (Clock - stateTime).TotalSeconds < 5;

    static double missionSec = -1;   // Mission run time (timer.getTime) of the last state
    /// R120: mission run time jumps back = new mission or restart: old identifiers gone, the first ATIS with fresh weather gets exactly one identifier.
    /// Not after a wall-clock gap: pause (ESC) halts the mission clock and writing, the identifier stays.
    static void MissionGap(double sec)
    {
        if (sec < missionSec && Atis.Count > 0) { Atis.Clear(); Log("[ATIS] neue Mission (Laufzeit zurückgesprungen) -> Kennungen neu"); }
        if (sec < missionSec) Carrier.NewMission();   // Review l4: R229 "99" state reset per ship
        missionSec = sec;
    }

    /// J/Q/N lines of the state file (JTAC-PLAN, interface mission-app): J;id;kind;cs;side;x;z;alt;freq;mod;code;alive  Q;id;group;type;count;x;z;alt;moving;flak;mgrs;lat;lon;fx;fz  N;side;name;x;z
    internal static void JtacLine(string[] p, List<Ops.Leader> jl, List<Ops.Trg> jt, List<(int, string, double, double)> jn)
    {
        double D(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
        double F(int i) => p[i] == "" ? double.NaN : D(i);
        if (p[0] == "J" && p.Length >= 12) jl.Add(new(p[1], p[2], p[3], (int)D(4), D(5), D(6), D(7), p[8] == "" ? 0 : D(8), p[9], p[10], p[11] == "1"));
        else if (p[0] == "Q" && p.Length >= 15) jt.Add(new(p[1], p[2], p[3], (int)D(4), D(5), D(6), D(7), p[8] == "1", p[9] == "1", p[10], D(11), D(12), F(13), F(14)));
        else if (p[0] == "N" && p.Length >= 5) jn.Add(((int)D(1), p[2], D(3), D(4)));
    }

    static void ReadState()
    {
        if (!File.Exists(StateFile)) return;
        var stamp = File.GetLastWriteTimeUtc(StateFile);
        if (stamp == stateStamp || DateTime.UtcNow - stamp > TimeSpan.FromSeconds(5)) return;
        string[] lines;
        try { lines = File.ReadAllLines(StateFile); } catch (IOException) { return; }
        stateStamp = stamp;
        stateTime = Clock;
        var ai = new List<Traffic>();
        var wx = new Dictionary<string, (double, double, double, double)>();
        var det = new Dictionary<int, Dictionary<int, bool>>();   // Radar picture per side (V9)
        var gci = new HashSet<int>();
        var boats = new List<Carrier.Boat>();
        var radars = new List<Carrier.RadarShip>();
        var fu = new Dictionary<string, Flights.Unit>();
        var llg = new List<(double, double, double, double)>();   // FI2: L lines
        var jl = new List<Ops.Leader>(); var jt = new List<Ops.Trg>(); var jn = new List<(int, string, double, double)>();   // J/Q/N: JTAC leaders, their targets, navigation points
        (double, double, double)? rz = null;   // A17: mission without range zone (mission change) -> no range controller any more
        foreach (var line in lines)
        {
            var p = line.Split(';');
            double D(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
            try
            {
                switch (p[0])
                {
                    case "G" when p.Length >= 4: Sky = (D(1) > 0, D(2), D(3)); Carrier.Ceiling = p.Length < 5 || D(4) > 0; break;
                    case "S" when p.Length >= 2: MissionGap(D(1)); break;   // R120: mission runtime
                    case "J" or "Q" or "N": JtacLine(p, jl, jt, jn); break;   // J1-J3: JTAC leaders, their targets, navigation points
                    case "L" when p.Length >= 5: llg.Add((D(1), D(2), D(3), D(4))); break;   // FI2: lat/lon grid (x;z;lat;lon)
                    case "Z" when p.Length >= 4: rz = (D(1), D(2), D(3)); break;     // Range Alpha
                    case "B" when p.Length >= 3: Ops.Bulls[p.Length >= 4 && int.TryParse(p[3], out var bs) ? bs : 0] = (D(1), D(2)); break;   // Bullseye per side
                    case "K" when p.Length >= 3:                                                 // Tanker TACAN[;track]
                        Ops.Beacons[p[1]] = p[2];
                        if (p.Length > 3 && p[3] != "") Ops.Tracks[p[1]] = p[3];
                        break;
                    case "F" when p.Length >= 3: (p.Length > 3 && p[3] == "t" ? Ops.TankerFreq : Ops.AwacsFreq)[p[1]] = D(2); break;   // AWACS radio frequency (mission), "t": tanker (TP7)
                    case "C" when p.Length >= 12:   // C;unit;group;type;x;z;hdg;speed;side;freq;tacan;icls;wx;wz;pa;noTurn (carrier)
                        boats.Add(new(p[1], p[2], p[3], D(4), D(5), D(6), D(7), int.Parse(p[8]), D(9), p[10], p[11], p.Length > 13 ? D(12) : 0, p.Length > 13 ? D(13) : 0, p.Length > 14 ? D(14) : 0, p.Length > 15 && p[15] == "1"));
                        break;
                    case "Y" when p.Length >= 2: Carrier.Clock = D(1); Carrier.Skew = Carrier.Clock - Now(); Carrier.Sun = p.Length >= 3 && p[2] != "" ? D(2) : double.NaN; break;   // Mission clock[;sun elevation at the carrier]
                    case "R" when p.Length >= 8: radars.Add(new(p[1], p[2], p[3], D(4), D(5), int.Parse(p[6]), D(7))); break;   // R;unit;group;type;x;z;side;freq (Red Crown ship, Carrier.RadarShips); the GCI line below has two fields
                    case "R" when p.Length >= 2: gci.Add((int)D(1)); break;                    // Side with ground radar (GCI, V19)
                    case "D" when p.Length >= 2:                                                 // D;side[;unit;type known]
                        if (!det.TryGetValue((int)D(1), out var ds)) det[(int)D(1)] = ds = new();
                        if (p.Length >= 4) ds[p[2].GetHashCode()] = p[3] == "1";
                        break;
                    case "W" when p.Length >= 6:   // W;name;wx;wz;t;p[;side]
                        wx[p[1]] = (D(2), D(3), D(4), D(5));
                        if (p.Length > 6) FieldSide(p[1], (int)D(6));
                        break;
                    case "T" when p.Length >= 11:   // T;unit;group;type;x;z;alt;hdg;speed;inAir;flag;coalition;callsign;fuel;gid;freq;ammo;kind;fuelkg;task[;m]
                        ai.Add(new Traffic(p[1].GetHashCode(), p[3], D(4), D(5), D(6), D(7), D(8), p[2], p[10], p[9] == "1", p.Length > 11 ? int.Parse(p[11]) : 0,
                            Cs: p.Length > 12 ? AiCs(p[1], p[12]) : "", Fuel: p.Length > 13 ? D(13) : 1, Heli: p.Length > 17 && p[17] == "h", Mission: p.Length > 20 && p[20] == "m"));   // Callsign and fuel per unit (ball call), AiCallsign/AiFuel per group only
                        if (p.Length > 12) AiCallsign[p[2]] = AiCs(p[1], p[12]);
                        if (p.Length > 13) AiFuel[p[2]] = D(13);
                        if (p.Length > 14) AiGid[p[2]] = (int)D(14);
                        if (p.Length > 16)   // AI radio: flight frequency, ammunition fox3/fox1/fox2/gun/air-to-ground
                            fu[p[1]] = new Flights.Unit(p[1], p[2], p[3], p[12], int.Parse(p[11]), D(4), D(5), D(6), p[9] == "1", D(13), D(15),
                                p[16].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(), p[10], (int)D(14), D(7), p.Length > 18 ? D(18) : 0,
                                p.Length > 19 ? p[19] : "", p.Length > 17 && p[17] == "h");   // D(7): heading (rad) for commit/RTB; D(18): internal tank kg (LK15 ops check); p[19]: editor task (R376)
                        break;
                    case "P" when p.Length >= 17:   // P;unit;gid;callsign;player;type;x;z;alt;agl;speed;hdg;windx;windz;pressure;vy;inAir;id;coalition;fuel;gear;kind (h/a)
                    {
                        bool fresh = !Pilots.TryGetValue(p[1], out var pl);
                        if (fresh)
                        {
                            Pilots[p[1]] = pl = new Pilot { Unit = p[1], Callsign = CallsignFrom(p[3], p[4]) };
                            pl.SlotCallsign = pl.Callsign;
                        }
                        pl.Gid = int.Parse(p[2]);
                        pl.Name = p[4];
                        pl.Type = p[5];
                        if (p.Length > 18) { pl.Id = uint.Parse(p[17]); pl.Coalition = int.Parse(p[18]); }
                        if (p.Length > 19) pl.Fuel = D(19);
                        pl.Heli = p.Length > 21 && p[21] == "h";   // N50: category h/a
                        pl.Modex = p.Length > 22 ? p[22].Trim() : "";   // #12: board number
                        bool air = p[16] == "1";
                        pl!.Tel = new Telemetry(D(8), air ? Math.Max(D(9), 3.5) : 0, D(10), D(11), D(6), D(7), D(12), D(13), D(14), D(15), p.Length > 20 ? D(20) : -1);
                        pl.Seen = Clock;
                        if ((Cfg.MyCallsign != "" && Host() == pl ? Cfg.MyCallsign : Cfg.CallsignFromUnit && UnitCallsign(pl.Unit) is { } uc ? uc : pl.SlotCallsign) is var want && want != pl.Callsign) { if (fresh) pl.Callsign = want; else Rename(pl, want); }   // own callsign (settings)
                        if (fresh) Log($"[ATC] Spieler {p[4]} ({p[5]}) als {pl.Callsign}");
                        if (fresh) RestoreSession(pl);
                        break;
                    }
                }
            }
            catch (FormatException e) { if (Now() - tracedBadState >= 10) { tracedBadState = Now(); Trace("FEHLER", $"State-Zeile {line}: {e.Message}"); } }
        }
        LearnMain(Ai, ai, wx);
        double dt = Now() - aiAt; aiAt = Now();
        Ai = ai.Select(a => dt is > 0.2 and < 30 && Ai.FirstOrDefault(o => o.Id == a.Id) is { } o ? a with { Vs = (a.AltMsl - o.AltMsl) / dt } : a).ToList();   // Climb rate from the last state
        Wx = wx;
        Ops.Detected = det;
        Ops.Gci = gci;
        Ops.Range = rz;
        (Ops.Leaders, Ops.Targets, Ops.NavPts) = (jl, jt, jn);
        if (llg.Count > 0) LlGrid = llg.ToArray();
        Ops.DefFreq = Cfg.Frequencies.GetValueOrDefault("JTAC", 133.5);
        Carrier.Boats = boats;
        Carrier.RadarShips = radars;
        Flights.Units = fu;
        Carrier.Air = ai;
        Carrier.Decks = Pilots.Values.Where(o => o.Tel != null && (Clock - o.Seen).TotalSeconds <= 3).Select(o => new Traffic(o.Unit.GetHashCode(), o.Type, o.Tel!.X, o.Tel.Z, o.Tel.AltMsl, o.Tel.Hdg, o.Tel.Ias, o.Unit, "", o.Tel.Agl > 0, o.Coalition)).ToList();   // Player for foul deck only
        Carrier.Sky = Sky;
        if (boats.Count > 0 && !lsoWarm)   // Pre-render LSO phrases: groove calls without Piper pause (SynthCache)
        {
            lsoWarm = true;
            var lv = PickVoice(Cfg.Voices.GetValueOrDefault("Carrier", "en_US-ryan-medium"), "Paddles");
            Task.Run(() => { int i = 0; foreach (var s in Carrier.LsoLines) try { Synth(s, lv, Path.Combine(Root, "tmp", $"warm{i++}.mp3")); } catch (Exception e) { Trace("FEHLER", $"LSO vorab: {e.GetType().Name}: {e.Message}"); } });
        }
        var host = Host();
        if (host?.Tel != null && CurrentTel()?.Lock is { } lk) host.Tel = host.Tel with { Lock = lk };   // locked target (Export.lua) for own aircraft only: declare
        foreach (var pl in Pilots.Values) pl.Boat.Sample(pl.Tel, Now(), pl == host && CurrentTel() != null ? HostAoa : double.NaN, pl.Type);   // LSO: groove at ~2 Hz
        foreach (var k in Pilots.Where(kv => (Clock - kv.Value.Seen).TotalSeconds > 30).Select(kv => kv.Key).ToList())
        {
            Log($"[ATC] Spieler {Pilots[k].Name} weg");
            Pilots[k].Ops.Leave();   // Range free again; before the debriefing, a pass without "off" is still in it then (R58)
            if (Pilots[k].Ops.Debrief.Count > 0) DebriefTx(Pilots[k], true);
            Pilots[k].Boat.Leave();
            Pilots.Remove(k);
        }
        foreach (var pl in Pilots.Values.Where(pl => (Clock - pl.Seen).TotalSeconds > 3)) pl.Tel = null;
    }

    /// N1: side of an airfield from the W line (every 10 s, capture). Only the field: rewriting the Airfields file triggered LoadFields and cleared Towers/Active/Pinned of all players.
    static void FieldSide(string name, int side)
    {
        if (Fields.FirstOrDefault(f => f.Name == name) is not { } f || f.Side == side) return;
        Log($"[ATC] {name}: Seite {f.Side} -> {side}");
        f.Side = side;
    }

    /// "Enfield11" -> "Enfield 1-1"; without a DCS callsign the player name.
    static string CallsignFrom(string dcs, string player)
    {
        if (NormCallsign(dcs) is { } cs) return cs;
        // Slot without a western callsign (e.g. single player): callsign from config.jsonc, for further players the name
        return Pilots.Values.Any(p => p.Callsign == Cfg.Callsign) ? player : Cfg.Callsign;
    }

    static List<Pilot> ActivePilots()
    {
        if (MissionData) return Pilots.Values.Where(p => p.Tel != null).ToList();
        // Mission data stalls (over 5 s, e.g. pause during rollout): the own mission player keeps flying with Export.lua. Solo, a second flow would be
        // with its own tower ("jetzt Dubai Intl", reset on the runway, takeoff clearance during rollout)
        if (Pilots.Values.Where(p => (Clock - p.Seen).TotalMinutes < 10).ToList() is var mp && (mp.FirstOrDefault(p => p.Name != "" && p.Name == Session()[1]) ?? (mp.Count == 1 ? mp[0] : null)) is { } me)
        {
            me.Tel = CurrentTel();
            return new() { me };
        }
        Solo.Tel = CurrentTel();
        if ((Cfg.MyCallsign != "" ? Cfg.MyCallsign : Cfg.Callsign) is var want && want != Solo.Callsign) Rename(Solo, want);
        return new() { Solo };
    }

    /// Flight: players of the same group close to the lead (lowest callsign) fly as one flight with its flow
    /// (Ground -> Tower -> Approach together). Joins under 1 NM, separated over 3 NM -> own flow again.
    static void FormFlights(List<Pilot> pilots)
    {
        foreach (var g in pilots.Where(p => p.Gid > 0 && p.Tel != null).GroupBy(p => p.Gid))
        {
            var members = g.OrderBy(p => p.Callsign, StringComparer.Ordinal).ToList();
            var lead = members[0];
            lead.Lead = null;
            bool split = lead.Active?.Separate == true;   // V17: separate landing / radar trail
            foreach (var m in members.Skip(1))
            {
                double d = Dist(m.Tel!.X, m.Tel.Z, lead.Tel!.X, lead.Tel.Z);
                bool own = m.Lead == null && m.Active?.Emergency == true;   // R293: own emergency (separate) remains its own sequence
                var nl = !split && !own && d < (m.Lead == lead ? 3 : 1) * NM ? lead : null;
                if (nl == m.Lead) continue;
                Log(nl != null ? $"[ATC] {m.Callsign} fliegt im Flug von {lead.Callsign}"
                               : $"[ATC] {m.Callsign} hat den Flug von {lead.Callsign} verlassen -> eigener Ablauf");
                if (nl == null) { m.Towers.Clear(); m.Active = null; }
                m.Lead = nl;
            }
            lead.FlightSize = 1 + members.Count(m => m.Lead == lead);
        }
    }

    /// You: the player at the position from Export.lua, otherwise the only player. Dedicated server: nobody (no DCS keys, no own callsign, no lighting via the menu).
    static Pilot? Host()
    {
        if (Dedicated) return null;
        if (!MissionData) return ActivePilots()[0];
        var players = Pilots.Values.Where(p => p.Tel != null).ToList();
        var me = Session()[1];   // own player name from the DCS hook
        if (me != "" && players.FirstOrDefault(p => p.Name == me) is { } mine) return mine;
        var tel = CurrentTel();
        if (tel != null)
        {
            var best = players.MinBy(p => Dist(p.Tel!.X, p.Tel.Z, tel.X, tel.Z));
            if (best != null && Dist(best.Tel!.X, best.Tel.Z, tel.X, tel.Z) < 300) return best;
        }
        return players.Count == 1 ? players[0] : null;
    }

    /// Traffic for a player: AI + all other players.
    static IReadOnlyList<Traffic> TrafficFor(Pilot p)
    {
        if (!MissionData) return CurrentTraffic();
        var list = Ai.Where(a => p.Gid == 0 || AiGid.GetValueOrDefault(a.Group) != p.Gid)   // not own AI flight members
                     .Select(a => a.Flag.StartsWith("arr-hold") && aiStackFt.TryGetValue(a.Group, out var hf) ? a with { AsgFt = hf } : a).ToList();   // R216: AI in holding at assigned altitude
        foreach (var o in Pilots.Values.Where(o => o != p && o.Lead != p && o.Tel != null))
            list.Add(new Traffic(o.Unit.GetHashCode(), o.Type, o.Tel!.X, o.Tel.Z, o.Tel.AltMsl, o.Tel.Hdg, o.Tel.Agl < 15 ? Tower.Gs(o.Tel) : o.Tel.Ias, o.Unit,   // R331: on the ground over ground
                o.Active is { HoldArrived: true } ? "hold:" + o.Active.F.Name :
                o.Active is { Phase: not Phase.Away } tw ? "twr:" + tw.F.Name : "", o.Tel.Agl > 0, o.Coalition, o.Tel.Vs, Heli: o.Heli,   // R270: "hold:" only at the holding point (on the way there like approach)
                AsgFt: o.Active?.AsgFt ?? 0));
        return list;
    }

}
