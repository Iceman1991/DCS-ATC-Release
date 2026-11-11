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
    // ------------------------------------------------------------ Control AI traffic Kutaisi (KutaisiTraffic.lua)
    static readonly Dictionary<string, DateTime> aiSent = new();
    static readonly Dictionary<string, DateTime> lastAiStart = new();   // per airfield

    static readonly Dictionary<string, string> AiCallsign = new(), aiStage = new();
    static readonly Dictionary<string, int> AiGid = new();   // AI group -> DCS group ID (the player's AI flight members are not traffic)

    /// Let departures start only when no player needs the runway; approaches into holding while players are in the traffic pattern.
    /// The AI talks audibly on the radio (request, controller, readback) – speech only, no text in the game.
    /// Landing sequence per airfield ("ai:Gruppe" / "p:Unit"): AI approaches and players on approach in the order
    /// in which they join (simultaneous: by distance). Positions 1–2 approach, the rest wait staggered (Tower.StackFt).
    static readonly Dictionary<string, List<string>> ArrQueue = new();
    static readonly Dictionary<string, double> AiFuel = new();   // AI group -> tank 0–1

    /// Terrain grid from the mission (%TMP%DcsAtc-Terrain-<Karte>-<Id>.txt) for airfields without terrain, at most every 10 s.
    static DateTime terrainCheck;
    static void LoadTerrain()
    {
        if ((Clock - terrainCheck).TotalSeconds < 10) return;
        terrainCheck = Clock;
        foreach (var fld in Fields.Where(f => !f.HasTerrain && f.Id > 0).Append(Airfield.Map).Where(f => f != Airfield.Map ? !f.HasTerrain : f.Name != Airfield.Theatre)) TerrainOf(fld);
    }
    static bool TerrainOf(Airfield fld)
    {
        var file = Path.Combine(Path.GetTempPath(), $"DcsAtc-Terrain-{Airfield.Theatre}-{(fld == Airfield.Map ? "map" : fld.Id)}.txt");   // map: coarse grid of the whole map (V15)
        try { if (File.Exists(file)) { fld.LoadTerrain(file); if (fld == Airfield.Map) fld.Name = Airfield.Theatre; } } catch (Exception e) when (e is IOException or FormatException or IndexOutOfRangeException) { }
        return fld.HasTerrain;
    }
    /// LA22 (log: Nellis "Uniform … left hand", 4 s later "Victor … right hand"): the pattern side comes from the terrain -> ATIS only with terrain (loaded right away, not in the 10 s round);
    /// charted side, airfield without terrain file or still none at 2 min mission time (first flight on the map: the mission is still computing) -> without
    static bool AtisReady(Airfield f) => f.HasTerrain || f.Id <= 0 || f.HandOf.Count > 0 || TerrainOf(f) || missionSec > 120;

    static void UpdateQueues(List<Pilot> pilots)
    {
        foreach (var fld in Fields)
        {
            var cur = new Dictionary<string, double>();
            // Distance to the airfield; in holding that of the holding point (circling should not upset the sequence)
            double D(string k, double x, double z) => Holds.TryGetValue(k, out var h) ? Dist(h.X, h.Z, fld.X, fld.Z) : Dist(x, z, fld.X, fld.Z);
            foreach (var a in Ai.Where(a => a.InAir && a.Group != "" && !a.Heli))   // helicopters land beside the runway: not in the landing sequence
            {
                var fl = a.Flag.Split(':', 2);
                if (fl[0] is "arr" or "arr-hold" && (fl.Length > 1 ? fl[1] : "Kutaisi") == fld.Name && (a.Coalition == 0 || Served(a.Coalition, fld))) cur.TryAdd("ai:" + a.Group, D("ai:" + a.Group, a.X, a.Z));   // N40: captured -> out (DIVERT)
            }
            foreach (var p in pilots.Where(p => p.Active?.F == fld && p.Tel is { Agl: > 30 } &&
                                                p.Active.Phase is Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand))
                cur.TryAdd("p:" + p.Unit, D("p:" + p.Unit, p.Tel!.X, p.Tel.Z));
            var q = ArrQueue.GetValueOrDefault(fld.Name) ?? new();
            foreach (var k in cur.Keys.Where(k => cur[k] > SeqNm * NM && !q.Contains(k)).ToList()) cur.Remove(k);   // further out: not yet in the sequence (does not hold up nearer ones)
            if (cur.Count == 0) { ArrQueue.Remove(fld.Name); continue; }
            ArrQueue[fld.Name] = q;
            q.RemoveAll(k => !cur.ContainsKey(k));
            // Sort newcomers in by distance, then fixed order (otherwise waiting and cleared aircraft overtake each other, clearance in a bunch)
            foreach (var k in cur.Keys.Where(k => !q.Contains(k)).OrderBy(k => cur[k]).ToList()) q.Insert(Slot(q, fld, cur[k]), k);
            // Priority: AI only with MAYDAY FUEL (tank 0–1 from the mission below LowFuel, R298), players only after a reported emergency, not silently by fuel state and not after "minimum fuel" (A29, AIM 5-5-15)
            bool Low(string k) => k.StartsWith("ai:") ? AiFuel.GetValueOrDefault(k[3..], 1) < LowFuel
                : pilots.FirstOrDefault(p => "p:" + p.Unit == k)?.Active is { Emergency: true };
            // Low fuel to the front, but not ahead of those on final
            foreach (var k in q.Where(Low).ToList())
            {
                // ahead of all without low fuel, but behind others with low fuel and behind the one already on final (no back and forth)
                int to = q.TakeWhile(x => x != k && (Low(x) || x == q[0] && cur[x] < 6 * NM)).Count();
                if (q.IndexOf(k) > to) { q.Remove(k); q.Insert(to, k); }
            }
        }
    }
    const double MinFuel = 0.20, LowFuel = 0.10, SeqNm = 40;   // AI tank: "minimum fuel" (no priority) / MAYDAY FUEL (priority, R298); landing sequence only from 40 NM from the airfield
    static readonly Dictionary<string, double> aiStackFt = new();   // AI group -> altitude in holding
    static readonly HashSet<string> fuelTold = new();

    /// Learn the DCS main runway: if an AI aircraft lands/takes off in light wind (> 40 m/s, so not a helicopter),
    /// that is the direction DCS-ATC assigns there. Parallel runways: the one on whose centerline it is.
    static void LearnMain(List<Traffic> was, List<Traffic> now, Dictionary<string, (double, double, double, double)> wx)
    {
        foreach (var a in now)
            if (was.FirstOrDefault(o => o.Id == a.Id) is { } w0 && w0.InAir != a.InAir && a.Speed > 40 &&
                Fields.FirstOrDefault(f => Dist(f.X, f.Z, a.X, a.Z) < 3000) is { } f && wx.TryGetValue(f.Name, out var w) &&
                Math.Sqrt(w.Item1 * w.Item1 + w.Item2 * w.Item2) < 3.5 &&
                f.Ends.Where(e => Tower.HdgDiff(e.Hdg, a.Hdg * 180 / Math.PI) < 20).MinBy(e => Math.Abs((a.X - e.CX) * e.Dz - (a.Z - e.CZ) * e.Dx)) is { } e && e.Name != f.Main)
            {
                Log($"[KI ] {f.Name}: DCS-Hauptbahn {e.Name} statt {f.Main} ({(a.InAir ? "Start" : "Landung")} {a.Group})");
                f.Main = e.Name;
            }
    }

    /// FI5: NOTAMs of today (UTC) for all airfields; a change also gives the ATIS a new identifier (its text is the key).
    static void SetNotams()
    {
        Airfield.SetNotams(Fields, Cfg.Notams, Cfg.RandomNotams, DateTime.UtcNow);
        foreach (var f in Fields.Where(f => f.Notams.Count > 0)) Log($"[NOTAM] {f.Name}: {string.Join("; ", f.Notams)}");
    }

    /// Runway at the airfield like DCS-ATC (wind from the mission weather, Airfield.BestRunway): the AI lands accordingly.
    static string FieldRunway(Airfield f)
    {
        var w = Wx.GetValueOrDefault(f.Name);
        return f.BestRunway(w.Wx, w.Wz, dcs: true);   // FI5: the AI ignores NOTAMs
    }

    /// All holdings of all airfields ("ai:Gruppe" / "p:Unit" -> point, altitude ft).
    static readonly Dictionary<string, (double X, double Z, double Ft)> Holds = new();
    /// Next free altitude from ft: no other holding within 14 NM (circling, traffic warning) closer than 1000 ft (regardless of airfield).
    static double FreeLevel(string key, double x, double z, double ft)
    {
        for (int i = 0; i < 30 && Holds.Any(h => h.Key != key && Dist(h.Value.X, h.Value.Z, x, z) < 14 * NM && Math.Abs(h.Value.Ft - ft) < 1000); i++) ft += 1000;
        return ft;
    }

    /// Place of a newcomer (distance d) in the landing sequence: before the first waiting aircraft whose holding point is further out, never before one already on approach.
    static int Slot(List<string> q, Airfield f, double d) => q.FindIndex(k => Holds.TryGetValue(k, out var h) && Dist(h.X, h.Z, f.X, f.Z) > d) is var i and >= 0 ? i : q.Count;

    /// Landing sequence: number 1 approaches, each further one only when its predecessor has been fully handled: player at the tower in the break,
    /// in the pattern or with landing clearance, AI on final (up to 9 NM on the line, heading to the runway) or landed. This way only one at a time is
    /// brought in (no conflicts en route; the AI first flies to its point outside on the line, also straight across the airfield).
    /// R46: player predecessor vectored to final or on final: staggered as soon as the follower (me, otherwise q[pos]) by straight-line distance to the threshold
    /// would be at least 3 NM (behind Heavy 5 NM) behind (Tower.Behind), also for the AI sequence.
    static bool SpacedAt(List<string> q, int pos, Airfield f, List<Pilot> pilots, (double X, double Z)? me = null) =>
        pos <= 0 || pos > q.Count || !Holds.ContainsKey(q[pos - 1]) && (q[pos - 1].StartsWith("p:")
            ? pilots.FirstOrDefault(p => "p:" + p.Unit == q[pos - 1])?.Active?.Phase is not (Phase.Inbound or Phase.Entering)   // Player: only in the break/pattern or with landing clearance (straight in ~3 NM), not already at the handover 8–10 NM out
              || (me ?? (pos < q.Count ? PosOf(q[pos], pilots) : null)) is { } m && AheadOf(q[pos - 1], f, pilots) is var (r, hv) && r >= 0 &&
                 f.End(FieldRunway(f)) is var te && Tower.Behind(Dist(m.X, m.Z, te.ThrX, te.ThrZ), r, hv)
            : Ai.FirstOrDefault(x => "ai:" + x.Group == q[pos - 1]) is not { } a || !a.InAir || f.End(FieldRunway(f)) is var e && Dist(a.X, a.Z, e.ThrX, e.ThrZ) < 9 * NM &&
              Tower.HdgDiff(a.Hdg * 180 / Math.PI, e.Hdg) < 30 && Tower.HdgDiff(Tower.Bearing(a.X, a.Z, e.ThrX, e.ThrZ), e.Hdg) < 20);

    /// R46: remaining distance (m) of a landing sequence entry to the threshold (player: Tower.FinalPath, AI in the air and not in holding: straight line), -1 = unknown; Heavy.
    static (double R, bool Heavy) AheadOf(string k, Airfield f, List<Pilot> pilots) =>
        k.StartsWith("p:") ? pilots.FirstOrDefault(p => "p:" + p.Unit == k) is { Active: { } tw, Tel: { } pt } pl && tw.F == f ? (tw.FinalPath(pt), Tower.Heavy(pl.Type)) : (-1, false)
        : Ai.FirstOrDefault(x => "ai:" + x.Group == k) is { InAir: true } a && !Holds.ContainsKey(k) && f.End(FieldRunway(f)) is var e ? (Dist(a.X, a.Z, e.ThrX, e.ThrZ), Tower.Heavy(a.Type)) : (-1, false);

    /// Position of a landing sequence entry ("ai:Gruppe" / "p:Unit").
    static (double X, double Z)? PosOf(string k, List<Pilot> pilots) =>
        k.StartsWith("ai:") ? Ai.FirstOrDefault(x => x.Group == k[3..]) is { } t0 ? (t0.X, t0.Z) : null
                            : pilots.FirstOrDefault(p => "p:" + p.Unit == k)?.Tel is { } pt ? (pt.X, pt.Z) : null;

    static bool Chatter => Cfg.AiChatter && On("ai");
    static void ControlAi(List<Pilot> pilots, List<Tx> outq)
    {
        if (!MissionData || !On("atc")) return;   // without atc: the AI flies the DCS ATC
        bool Once(string key)
        {
            if (aiSent.TryGetValue(key, out var t0) && (Clock - t0).TotalSeconds < 120) return false;   // Mission sets the flag; otherwise do not repeat constantly
            aiSent[key] = Clock;
            return true;
        }
        foreach (var k in Holds.Keys.Where(k => k.StartsWith("ai:") && !Ai.Any(a => a.Group == k[3..] && a.InAir && a.Flag.StartsWith("arr-hold"))).ToList())
            Holds.Remove(k);   // landed / gone
        foreach (var g in Ai.Where(a => a.Flag != "" && a.Group != "").GroupBy(a => a.Group))
        {
            var a = g.First();
            var fl = a.Flag.Split(':', 2);   // "dep:Senaki-Kolkhi" (mod), old mission: "dep" = Kutaisi
            var flag = fl[0];
            if (Fields.FirstOrDefault(f => f.Name == (fl.Length > 1 ? fl[1] : "Kutaisi")) is not { } fld || fld.Ends.Count == 0) continue;
            if (a.Coalition > 0 && !Served(a.Coalition, fld))   // N40: airfield captured: no radio from the hostile airfield, divert approaches once to the nearest own (they check in anew there)
            {
                if (flag is "arr" or "arr-hold" && a.InAir && !a.Mission && Once("DIVERT;" + g.Key)) { AiCmd($"DIVERT;{g.Key}"); aiStage.Remove(g.Key); Holds.Remove("ai:" + g.Key); Log($"[KI ] {g.Key}: {fld.Name} erobert -> umgeleitet"); }
                continue;
            }
            var herePl = pilots.Where(p => p.Active?.F == fld && p.Tel != null && Dist(p.Tel.X, p.Tel.Z, fld.X, fld.Z) < 30 * NM).ToList();
            var here = herePl.Select(p => p.Active!).ToList();
            var q = ArrQueue.GetValueOrDefault(fld.Name);
            int pos = q?.IndexOf("ai:" + g.Key) ?? -1;
            // Radio audible/visible only if it concerns the player: AI ≤ 10 NM from him or directly ahead/behind him in the landing sequence
            bool concerns = herePl.Any(p => Dist(p.Tel!.X, p.Tel.Z, a.X, a.Z) < 10 * NM ||
                                            pos >= 0 && q!.IndexOf("p:" + p.Unit) is var pp && pp >= 0 && Math.Abs(pp - pos) == 1);
            bool emergency = here.Any(t => t.Mayday);   // MAYDAY: AI holds, runway clear, AI radio at the airfield silent (A24); PAN priority only (UpdateQueues)
            // other AI at the airfield (A82): departure with takeoff clearance still on the ground ("ready") or rolling on the runway (takeoff, rollout)
            string FieldOf(Traffic o) => o.Flag.Split(':', 2) is [_, var n] ? n : "Kutaisi";
            var rw = FieldRunway(fld);   // DCS assigns the runway, then the AI lands/takes off
            bool aiDep = Ai.Any(o => o.Group != g.Key && !o.InAir && o.Flag.StartsWith("dep-go") && FieldOf(o) == fld.Name && aiStage.GetValueOrDefault(o.Group) == "ready");
            bool luaw = here.Any(t => t.Phase == Phase.HoldShort && t.LineUp);   // A9/A10: waiting at the holding point blocks nothing, waiting on the runway does
            bool blocked = herePl.Any(p => Tower.Blocks(fld, rw, p.Tel!.X, p.Tel.Z, p.Tel.Agl, p.Tel.Hdg, Tower.Gs(p.Tel))) ||   // Player on the runway (A8: also standing, rollout above 6000 ft free)
                          Ai.Any(o => o.Group != g.Key && Tower.Blocks(fld, rw, o.X, o.Z, o.AltMsl - fld.Elev, o.Hdg, o.Speed));
            bool rwyBusy = emergency || aiDep || luaw || here.Any(t => t.Phase is Phase.ClearedTakeoff or Phase.ClearedLand) || blocked;
            bool finalNear = herePl.Any(p => p.Active!.Phase is Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand && p.Active.OnFinalWithin(p.Tel!, 4 * NM));   // A9: player on final up to 4 NM -> no AI takeoff clearance
            // Player on approach or at departure (A41) on the opposite direction (terrain, Tower.Plan, runway request): AI waits and does not take off
            bool contra = here.Any(t => Tower.HdgDiff(fld.End(t.ApproachRwy).Hdg, fld.End(rw).Hdg) > 90 &&
                                        t.Phase is Phase.TaxiOut or Phase.HoldShort or Phase.ClearedTakeoff or Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand);
            bool arrNear = herePl.Any(p => p.Active!.Phase is Phase.Inbound or Phase.Entering or Phase.Initial && p.Active.HoldInfo == null && Dist(p.Tel!.X, p.Tel.Z, fld.X, fld.Z) < 12 * NM);   // Player is being vectored in: AI departure waits (climb would cross otherwise)
            var rws = Tower.RwSay(rw);
            var thr = fld.End(rw);
            // R338: a player's landing clearance blocks the AI landing only if he is down or ahead of it; the reason is in the call (FAA JO 7110.65 3-8-1, 3-10-6)
            double Al(double x, double z) => -((x - thr.ThrX) * thr.Dx + (z - thr.ThrZ) * thr.Dz);
            var leadPl = herePl.Where(p => p.Active!.Phase == Phase.ClearedLand && p.Tel!.Agl >= 15 && Al(p.Tel.X, p.Tel.Z) < Al(a.X, a.Z)).MaxBy(p => Al(p.Tel!.X, p.Tel.Z));
            bool depNow = aiDep || here.Any(t => t.Phase == Phase.ClearedTakeoff);
            bool rwyBusyArr = emergency || !a.Heli && (depNow || luaw || leadPl != null || blocked || herePl.Any(p => p.Active!.Phase == Phase.ClearedLand && p.Tel!.Agl < 15));
            string busyWhy = leadPl != null ? $"number two, follow the {Ops.TypeSay(leadPl.Type)} on {Math.Max(1, (int)Math.Round(Al(leadPl.Tel!.X, leadPl.Tel.Z) / NM))} mile final, continue"
                           : $"continue approach, {(emergency ? "emergency in progress" : depNow ? $"traffic departing runway {rws}" : "traffic on runway")}";
            var m = Regex.Match(AiCallsign.GetValueOrDefault(g.Key, ""), @"^([A-Za-z]+)\s*(\d)\s*-?\s*(\d)");
            var cs = m.Success ? Tower.SpokenCallsign($"{m.Groups[1].Value} {m.Groups[2].Value}-{m.Groups[3].Value}") : a.Type.Split('_')[0] + " flight";
            var stage = aiStage.GetValueOrDefault(g.Key, "");
            if (flag == "arr" && stage == "gone") stage = aiStage[g.Key] = "";   // mission AI back from its departure: approach calls anew
            var rwTo = a.Heli ? "" : $" runway {rws}";   // helicopter: no runway in the calls (R347)
            string landClr = $"{cs}{(a.Heli ? "" : $", runway {rws}")}, cleared to land.", landRb = $"Cleared to land{rwTo}, {cs}.";
            // Like real radio: audible to players ≤ 50 NM who have the airfield frequency tuned in SRS (frequency unknown: players at this airfield)
            bool Heard(string role) => concerns || pilots.Any(p => p.Tel != null && Dist(p.Tel.X, p.Tel.Z, a.X, a.Z) < 50 * NM &&
                (Srs?.TunedFreqs(p.Id) is { } fq ? (FieldFreqs(fld, role) ?? new[] { Cfg.Frequencies.GetValueOrDefault(role, 265.0) }).Any(f => fq.Any(m => Math.Abs(m - f) < 0.005))
                                                 : p.Active?.F == fld));
            void Chat(string role, string pilot, string atc, string? readback)   // Text to everyone on the frequency (ShowTo)
            {
                if (!Heard(role) || emergency) return;
                var voice = PilotVoiceFor(g.Key);
                if (Chatter && pilot != "") outq.Add(new Tx(pilot, role, cs, 0, true, voice, Freqs: FieldFreqs(fld, role), Prio: 2, Side: a.Coalition));
                outq.Add(new Tx(atc, role, fld.StationOf(role), 0, Freqs: FieldFreqs(fld, role), Prio: 2, Side: fld.Side > 0 ? fld.Side : a.Coalition));   // A133: AI pilot with his side, controller with that of the airfield
                if (Chatter && readback != null) outq.Add(new Tx(readback, role, cs, 0, true, voice, Freqs: FieldFreqs(fld, role), Prio: 2, Side: a.Coalition));
            }
            double stack = Math.Max(Tower.StackFt(fld, pos), Math.Ceiling(fld.MvaFt(a.X, a.Z) / 1000) * 1000 + Math.Max(0, pos - 2) * 1000);   // above the terrain
            var hp = Holds.TryGetValue("ai:" + g.Key, out var h0) ? (X: h0.X, Z: h0.Z) : (X: a.X, Z: a.Z);
            stack = FreeLevel("ai:" + g.Key, hp.X, hp.Z, stack);   // Holdings of other airfields: altitude free?
            bool spaced = q == null || SpacedAt(q, pos, fld, pilots);
            // DCS AI taxis after START without stopping into the takeoff roll (does not wait for takeoff clearance), the tower cannot stop it: AI approaches of the landing sequence
            // wait in or go into holding while their landing would fall into the takeoff window of a taxiing AI departure (also in the same tick as START; not yet sequenced ones).
            // Takeoff roll ≈ distance to threshold / 8 m/s + 20 s, landing earliest straight line / speed (min. 60 m/s), latest 120 s after that (detour via the 8-NM final point), plus 90 s separation each.
            // Large airfields (Golf, 3500-m runways, parking spot far from the threshold) otherwise taxi for minutes and hit exactly the next landing. Players stagger Approach/Tower themselves (arrNear, takeoff clearance).
            double RollAt(double x, double z) => Dist(x, z, thr.ThrX, thr.ThrZ) / 8 + 20;
            bool Clash(double roll, double x, double z, double v) => Dist(x, z, thr.ThrX, thr.ThrZ) / Math.Max(v, 60) is var eta && roll > eta - 90 && roll < eta + 120 + 90;
            bool depRoll = flag is "arr" or "arr-hold" && a.InAir && pos >= 0 &&
                           Ai.Any(o => o.Group != g.Key && FieldOf(o) == fld.Name && !o.InAir && aiStage.GetValueOrDefault(o.Group) is "taxi" or "ready" && Clash(RollAt(o.X, o.Z), a.X, a.Z, a.Speed));
            bool wait = emergency || !spaced || contra || luaw || depRoll;   // A10: player waiting on the runway; landing sequence: one after another (DCS flies the approach itself, own waypoints disturb it)
            // R298: two thresholds (AIM 5-5-15): "minimum fuel" advisory only without priority, below that MAYDAY FUEL with priority (UpdateQueues); clearance is the RESUME branch
            double fuel = AiFuel.GetValueOrDefault(g.Key, 1);
            if (flag is "arr" or "arr-hold" && a.InAir && pos >= 0 && Heard("Approach"))
            {
                if (fuel < LowFuel && fuelTold.Add("may:" + g.Key))
                {
                    fuelTold.Add(g.Key);
                    Chat("Approach", $"MAYDAY MAYDAY MAYDAY FUEL, {fld.StationOf("Approach")}, {cs}, emergency fuel.",
                         $"{cs}, roger mayday, you are number {pos + 1}.", $"Number {pos + 1}, {cs}.");
                }
                else if (fuel < MinFuel && fuelTold.Add(g.Key))
                    Chat("Approach", $"{fld.StationOf("Approach")}, {cs}, minimum fuel.",
                         $"{cs}, {OneOf("roger", "copy")} minimum fuel, you are number {pos + 1}.", $"Number {pos + 1}, {cs}.");
            }
            if (flag == "dep" && !rwyBusy && !contra && !arrNear && (Clock - lastAiStart.GetValueOrDefault(fld.Name)).TotalSeconds > 120 && Once("START;" + g.Key))
            {
                lastAiStart[fld.Name] = Clock;
                AiCmd($"START;{g.Key}");
                aiStage[g.Key] = "taxi";
                Chat("Ground", $"{fld.StationOf("Ground")}, {cs}, request startup and taxi.",
                     $"{cs}, start up approved, {Tower.TaxiOut(fld.RampAt(a.X, a.Z), rw, rws)}.", $"Taxi holding point runway {rws}, {cs}.");   // K36: route and holding point from the chart like the player
            }
            // Takeoff clearance only after taxiing (parking spot close to the threshold: not right after engine start) and with a clear runway (player rolling out, other AI taking off),
            // already on approach from 600 m instead of only when stationary under 450 m (A82): DCS often taxis the AI onto the runway without stopping, the clearance otherwise came after the takeoff roll
            // Helicopter: lifts off from the spot (R347 "from present position"); airplane started by itself (mission AI) rolling near the threshold without a taxi call: also
            else if (flag == "dep-go" && (a.Heli ? a.InAir && stage == "taxi" : !a.InAir && (stage == "taxi" || stage == "" && a.Speed > 2) && Dist(a.X, a.Z, thr.ThrX, thr.ThrZ) < 600) && !rwyBusy && !finalNear &&
                     !Ai.Any(o => o.Group != g.Key && o.InAir && FieldOf(o) == fld.Name && aiStage.GetValueOrDefault(o.Group) is "final" or "final-wait" && Dist(o.X, o.Z, thr.ThrX, thr.ThrZ) < 4 * NM) &&   // AI with landing clearance under 4 NM (like TrafficOnFinal for the player)
                     (Clock - lastAiStart.GetValueOrDefault(fld.Name)).TotalSeconds > 90)
            {
                aiStage[g.Key] = "ready";
                if (a.Heli)
                    Chat("Tower", $"{fld.StationOf("Tower")}, {cs}, ready for departure.", $"{cs}{WindSay(fld)}, cleared for takeoff from present position.", $"Cleared for takeoff, {cs}.");
                else
                    Chat("Tower", $"{fld.StationOf("Tower")}, {cs}, holding short runway {rws}, ready for departure.",
                         $"{cs}, runway {rws}{WindSay(fld)}, cleared for takeoff.", $"Cleared for takeoff runway {rws}, {cs}.");   // R386: wind like the player clearance
            }
            // Mission AI starts and taxis by itself (no START): taxi call when it starts moving (helicopter: lifts off), K36 route like after START
            else if (flag == "dep-go" && stage == "" && (a.Heli ? a.InAir || a.Speed > 1 : !a.InAir && a.Speed > 2 && Dist(a.X, a.Z, thr.ThrX, thr.ThrZ) >= 600) && Dist(a.X, a.Z, fld.X, fld.Z) < 3 * NM)
            {
                aiStage[g.Key] = "taxi";
                Chat("Ground", $"{fld.StationOf("Ground")}, {cs}, request {(a.Heli ? "air " : "")}taxi.",
                     $"{cs}, {Tower.TaxiOut(fld.RampAt(a.X, a.Z), rw, rws, a.Heli)}.", $"{(a.Heli ? "Air taxi" : "Taxi")} holding point runway {rws}, {cs}.");
            }
            else if (flag == "dep-go" && a.InAir && stage is "taxi" or "ready" && Dist(a.X, a.Z, fld.X, fld.Z) > 5000)
            {
                aiStage[g.Key] = "gone";
                Chat("Tower", "", $"{cs}, contact departure, good day.", OneOf($"Good day, {cs}.", $"Contact departure, {cs}."));
            }
            else if (flag == "arr" && wait && !a.Mission && (stage == "" || emergency || contra && !aiSent.ContainsKey("RESUME;" + g.Key)) && a.InAir && Dist(a.X, a.Z, fld.X, fld.Z) > 8 * NM && Once("WAIT;" + g.Key))   // cleared: not into holding again
            {
                AiCmd($"WAIT;{g.Key};1200;{stack:0}");
                aiStackFt[g.Key] = stack;
                Holds["ai:" + g.Key] = (a.X, a.Z, stack);
                aiStage[g.Key] = "inbound";
                Chat("Approach", "", $"{cs}, {(aiSent.ContainsKey("RESUME;" + g.Key) ? "cancel approach clearance, " : "")}hold at present position, {(a.AltMsl / 0.3048 > stack + 300 ? "descend and " : a.AltMsl / 0.3048 < stack - 300 ? "climb and " : "")}maintain {stack:0} feet, speed {Tower.HoldKt} knots, number {pos + 1}, expect approach in {Math.Max(1, pos * 2 - 1)} minutes.",
                     $"Holding, {stack:0} feet, {cs}.");
            }
            else if (flag == "arr-hold" && !wait && Once("RESUME;" + g.Key))
            {
                AiCmd($"RESUME;{g.Key}");
                Holds.Remove("ai:" + g.Key);
                Chat("Approach", "", $"{cs}, leave the hold, number {pos + 1}, cleared for the approach.", $"Leaving the hold, {cs}.");
            }
            else if (flag == "arr-hold" && wait && aiStackFt.TryGetValue(g.Key, out var was) && stack < was - 1 && Once("STACK;" + g.Key + stack))
            {
                AiCmd($"STACK;{g.Key};{stack:0}");   // Predecessor out: one level lower
                aiStackFt[g.Key] = stack;
                Holds["ai:" + g.Key] = (hp.X, hp.Z, stack);
                Chat("Approach", "", $"{cs}, descend and maintain {stack:0} feet, number {pos + 1}.", $"Down to {stack:0}, {cs}.");
            }
            else if (flag == "arr" && a.InAir && stage == "" && (pos >= 0 || a.Heli))   // only in the sequence (holding or approach); helicopters outside it
            {
                aiStage[g.Key] = "inbound";
                Chat("Approach", $"{fld.StationOf("Approach")}, {cs}, inbound for landing.",
                     $"{cs}, {fld.StationOf("Approach")}, radar contact{(a.Heli ? "" : $", expect runway {rws}")}.", a.Heli ? $"Wilco, {cs}." : $"Runway {rws}, {cs}.");
            }
            else if (flag == "arr" && a.InAir && stage == "inbound" && (a.Heli ? a.AltMsl - fld.Elev < 300 && Dist(a.X, a.Z, fld.X, fld.Z) < 2 * NM :
                     a.AltMsl - fld.Elev < 700 && Tower.HdgDiff(a.Hdg * 180 / Math.PI, thr.Hdg) < 30 &&
                     -((a.X - thr.ThrX) * thr.Dx + (a.Z - thr.ThrZ) * thr.Dz) is > 0 and < 7 * NM))   // really on final, not in the DCS downwind
            {
                aiStage[g.Key] = rwyBusyArr ? "final-wait" : "final";   // Runway occupied: landing clearance follows as soon as it is free (A83)
                // R301: handover Approach -> Tower with frequency and readback (FAA JO 7110.65 2-1-17, 5-9-4); same frequency: no handover (R104)
                if ((FieldFreqs(fld, "Tower") ?? new[] { Cfg.Frequencies.GetValueOrDefault("Tower", 265.0) })[^1] is var tfq &&
                    Math.Abs(tfq - (FieldFreqs(fld, "Approach") ?? new[] { Cfg.Frequencies.GetValueOrDefault("Approach", 266.5) })[^1]) >= 0.005)
                    Chat("Approach", "", $"{cs}, contact {fld.StationOf("Tower")} {Tower.FreqSay(tfq)}.", $"Tower {Tower.FreqSay(tfq)}, {cs}.");
                Chat("Tower", $"{fld.StationOf("Tower")}, {cs}, final{rwTo}.",
                     rwyBusyArr ? $"{cs}, {busyWhy}." : landClr,
                     rwyBusyArr ? $"Continue, {cs}." : landRb);
            }
            else if (flag == "arr" && a.InAir && stage == "final-wait" && !rwyBusyArr)
            {
                aiStage[g.Key] = "final";
                Chat("Tower", "", landClr, landRb);
            }
            else if (stage is "final" or "final-wait" && !a.InAir && a.Speed < 15)   // final-wait: DCS lands the AI even without clearance
            {
                aiStage[g.Key] = "landed";
                var air = a.Heli ? "air " : "";
                Chat("Ground", $"{fld.StationOf("Ground")}, {cs}, {(a.Heli ? "on the ground" : "runway vacated")}, request {air}taxi to parking.", $"{cs}, {air}taxi to parking{Tower.ParkVia(fld.RampAt(a.X, a.Z), rw)}.", $"{(a.Heli ? "Air taxi" : "Taxi")} to parking, {cs}.");   // K36: vacate route from the chart (the apron nearest the AI)
            }
        }
    }

    /// Self-test AI runway clearances (A41, A82, A83): takeoff clearance already on approach, landing clearance waits for other AI on the runway,
    /// player departing on the opposite runway holds the AI.
    static void ControlAiTest()
    {
        var fld = Airfield.Kutaisi();
        var (fs, ws, ss, sst, sl) = (Fields, Wx, SimSec, stateTime, SimLog);
        var cmds = new List<string>();
        var outq = new List<Tx>();
        try
        {
            Fields = new() { fld }; Wx = new(); SimSec = 5000; stateTime = Clock; SimLog = new(); AiHook = cmds.Add;
            aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
            var rw = FieldRunway(fld);
            var e = fld.End(rw);
            var opp = fld.Ends.First(x => x.Name != rw).Name;
            var (px, pz) = e.At(0, 300);   // Player on the apron, not on the runway
            var pl = new Pilot { Unit = "U1", Callsign = "Enfield 1-1", Tel = new Telemetry(fld.Elev, 0, 0, 0, px, pz, 0, 0, 0) };
            void Player(Phase ph, string r)
            {
                pl.Active = new Tower(fld, "Enfield 1-1");
                pl.Active.Load(new Tower.State(ph, r, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, false, false, null, null, 0, false), pl.Tel!, 0);
            }
            Traffic Plane(string g, string flag, (double X, double Z) p, bool air, double spd, double alt = 0) =>
                new(g.GetHashCode(), "FA-18C_hornet", p.X, p.Z, fld.Elev + alt, e.Hdg * Math.PI / 180, spd, g, flag, air);
            string Said() { var s = string.Join(" | ", outq.Where(t => !t.Pilot && t.Role == "Tower").Select(t => t.Text)); outq.Clear(); return s; }
            void Run() => ControlAi(new() { pl }, outq);

            // A82: taxiing AI 520 m before the threshold gets the takeoff clearance (before only when stationary under 450 m), the second waits until the first has lifted off
            Player(Phase.Parked, rw);
            aiStage["DEP1"] = aiStage["DEP2"] = "taxi";
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(500, 150), false, 6), Plane("DEP2", "dep-go:Kutaisi", e.At(550, 150), false, 6) };
            Wx[fld.Name] = (-e.Dx * 5, -e.Dz * 5, 15, 101325);   // R386: 10 kt headwind on the runway in use
            Run();
            var s1 = Said();
            Wx = new();
            if (aiStage["DEP1"] != "ready" || aiStage["DEP2"] != "taxi" || !s1.Contains("cleared for takeoff") || s1.Split("cleared for takeoff").Length != 2 || !s1.Contains(", wind ") || !s1.Contains("one zero knots"))
                throw new Exception($"KI-Startfreigabe: {aiStage["DEP1"]}/{aiStage["DEP2"]} \"{s1}\"");
            Console.WriteLine("OK   KI-Startfreigabe schon bei der Annäherung (mit Wind, R386), zweite KI wartet auf die Bahn");

            // A82 the other way round: AI with landing clearance on final (3 NM) -> no takeoff clearance, only after landing
            aiStage["DEP2"] = "taxi"; aiStage["ARR9"] = "final";
            Ai = new() { Plane("DEP2", "dep-go:Kutaisi", e.At(500, 150), false, 6), Plane("ARR9", "arr:Kutaisi", e.At(3 * NM, 0), true, 75, 300) };
            Run();
            var s0 = Said();
            Ai = new() { Ai[0] };
            Run();
            if (s0.Contains("cleared for takeoff") || aiStage["DEP2"] != "ready" || !Said().Contains("cleared for takeoff"))
                throw new Exception($"KI-Startfreigabe bei KI im Endanflug: \"{s0}\" / {aiStage["DEP2"]}");
            Console.WriteLine("OK   KI-Startfreigabe wartet auf KI mit Landefreigabe im Endanflug");


            // A82/A83: approach with departing AI on the runway -> "continue approach, traffic departing", stays until clear, then landing clearance
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-300, 0), false, 40), Plane("ARR1", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
            aiStage["ARR1"] = "inbound";
            Run();
            var s2 = Said();
            Run();
            var s3 = Said();
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-1500, 0), true, 80, 60), Ai[1] };
            Run();
            var s4 = Said();
            if (!s2.Contains("continue approach, traffic departing runway") || s3 != "" || aiStage["ARR1"] != "final" || !s4.Contains("cleared to land") || s4.Contains("continue"))
                throw new Exception($"KI-Landefreigabe: \"{s2}\" / \"{s3}\" / {aiStage["ARR1"]} \"{s4}\"");
            Console.WriteLine("OK   KI-Anflug: \"continue approach, traffic departing\", Landefreigabe erst nach dem Abheben");

            // other AI rolls out on the runway above 3 m/s (not taking off): likewise; landed without clearance still counts as landed
            Ai = new() { Plane("ARR0", "arr:Kutaisi", e.At(-800, 0), false, 20), Plane("ARR2", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
            aiStage["ARR2"] = "inbound";
            Run();
            var s5 = Said();
            Ai = new() { Plane("ARR2", "arr:Kutaisi", e.At(-500, 0), false, 10) };
            Run();
            if (!s5.Contains("continue approach, traffic on runway") || aiStage["ARR2"] != "landed")
                throw new Exception($"KI-Anflug, Bahn belegt: \"{s5}\" / {aiStage["ARR2"]}");
            Console.WriteLine("OK   KI-Anflug: andere KI rollt auf der Bahn -> warten; Landung ohne Freigabe -> gelandet");

            // R301: AI on final is handed from Approach to Tower ("contact Kutaisi Tower …", readback), only then it calls the Tower; Approach no longer says "report final"
            aiStage.Clear(); aiSent.Clear(); Ai = new() { Plane("ARR9", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
            aiStage["ARR9"] = "inbound"; outq.Clear(); Run();
            var hand = string.Join(" | ", outq.Where(t => t.Role == "Approach").Select(t => t.Text)); var seq = string.Join(" | ", outq.Select(t => t.Role));
            if (!Regex.IsMatch(hand, @"contact Kutaisi Tower two six five decimal zero\..*\| Tower two six five decimal zero, ") || hand.Contains("report final") || !seq.StartsWith("Approach") || !seq.Contains("Tower"))
                throw new Exception($"KI-Übergabe an den Tower: \"{hand}\" / {seq}");
            Console.WriteLine($"OK   KI-Anflug: Übergabe an den Tower mit Frequenz und Rücklesung (\"{hand}\")");

            // R338: player with landing clearance behind the AI (8 NM) does not block its landing; ahead of it (2 NM): "number two, follow …" instead of "traffic on runway"
            var tel338 = pl.Tel; pl.Type = "FA-18C_hornet";
            var r338 = new List<string>();
            foreach (var d338 in new[] { 8.0, 2.0 })
            {
                Player(Phase.ClearedLand, rw);
                var (qx, qz) = e.At(d338 * NM, 0);
                pl.Tel = new Telemetry(fld.Elev + 600, 600, 70, e.Hdg * Math.PI / 180, qx, qz, 0, 0, 0);
                aiStage.Clear(); aiSent.Clear(); Ai = new() { Plane("ARR9", "arr:Kutaisi", e.At(4 * NM, 0), true, 75, 400) };
                aiStage["ARR9"] = "inbound"; outq.Clear(); Run();
                r338.Add(Said());
            }
            pl.Tel = tel338;
            if (!r338[0].Contains("cleared to land") || !r338[1].Contains("number two, follow the Hornet on 2 mile final, continue"))
                throw new Exception($"R338 KI-Landefreigabe bei Spieler-Freigabe: {string.Join(" / ", r338)}");
            Console.WriteLine($"OK   R338 KI-Landefreigabe: Spieler dahinter -> frei, davor -> number two ({string.Join(" / ", r338)})");

            // A41: player taxis to the opposite runway (TaxiOut): the AI does not take off, on the active runway it does
            aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear(); cmds.Clear();
            Ai = new() { Plane("DEP3", "dep:Kutaisi", e.At(2000, 400), false, 0) };
            Player(Phase.TaxiOut, opp);
            Run();
            int blocked = cmds.Count;
            Player(Phase.TaxiOut, rw);
            Run();
            if (blocked != 0 || !cmds.Contains("START;DEP3")) throw new Exception($"KI-Abflug, Spieler auf der Gegenbahn: {blocked} Kommandos, danach {string.Join(",", cmds)}");
            Console.WriteLine("OK   KI-Abflug wartet auf Spieler beim Abflug auf der Gegenbahn");

            // A9: one runway, one clearance: two players report "ready" shortly after each other -> only one "cleared for takeoff", the second only after the first lifts off
            {
                Ai = new();
                var hs = e.At(0, 150);
                var tA = new Tower(fld, "Enfield 1-1"); var tB = new Tower(fld, "Colt 1-1");
                var pA = new Pilot { Unit = "PA", Callsign = "Enfield 1-1", Tel = new Telemetry(fld.Elev, 0, 0, e.Hdg * Math.PI / 180, hs.X, hs.Z, 0, 0, 0), Active = tA };
                var pB = new Pilot { Unit = "PB", Callsign = "Colt 1-1", Tel = pA.Tel with { X = hs.X + 30 }, Active = tB };
                foreach (var pp in new[] { pA, pB })
                    pp.Active!.Load(new Tower.State(Phase.TaxiOut, rw, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, false, false, null, null, 0, false), pp.Tel!, 0);
                var both = new List<Pilot> { pA, pB };
                var none = Array.Empty<Traffic>();
                Prep(pA, tA, both); var rA = tA.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", pA.Tel, none, 1);
                Prep(pB, tB, both); var rB = tB.OnTranscript("Kutaisi Tower, Colt 1-1, ready for departure", pB.Tel, none, 1);
                Prep(pB, tB, both); var rB2 = tB.Tick(pB.Tel, none, 2);
                pA.Tel = pA.Tel! with { AltMsl = fld.Elev + 60, Agl = 60, Ias = 80 };   // first one lifts off
                Prep(pB, tB, both); var rB3 = tB.Tick(pB.Tel, none, 3);
                if (!rA[0].Text.Contains("cleared for takeoff") || !rB[0].Text.Contains("number 2 for departure") || rB2.Count != 0 || !(rB3.FirstOrDefault()?.Text ?? "").Contains("cleared for takeoff"))
                    throw new Exception($"A9 zwei Abflüge: \"{rA[0].Text}\" / \"{rB[0].Text}\" / {rB2.Count} / \"{rB3.FirstOrDefault()?.Text}\"");
                Console.WriteLine("OK   A9 zwei Spieler am Rollhalt: nur einer \"cleared for takeoff\", der zweite \"number 2 for departure\" bis zum Abheben");

                // R341: three players report ready one after another -> number 1/2/3; after the first lifts off the second (earlier report) is cleared, not the third
                {
                    var tq1 = new Tower(fld, "Enfield 1-1"); var tq2 = new Tower(fld, "Colt 1-1"); var tq3 = new Tower(fld, "Uzi 1-1");
                    var q1 = new Pilot { Unit = "Q1", Callsign = "Enfield 1-1", Tel = new Telemetry(fld.Elev, 0, 0, e.Hdg * Math.PI / 180, hs.X, hs.Z, 0, 0, 0), Active = tq1 };
                    var q2 = new Pilot { Unit = "Q2", Callsign = "Colt 1-1", Tel = q1.Tel with { X = hs.X + 30 }, Active = tq2 };
                    var q3 = new Pilot { Unit = "Q0", Callsign = "Uzi 1-1", Tel = q1.Tel with { X = hs.X + 60 }, Active = tq3 };   // "Q0": sorts first, the order must come from the report time
                    var all3 = new List<Pilot> { q3, q2, q1 };
                    foreach (var pp in all3)
                        pp.Active!.Load(new Tower.State(Phase.TaxiOut, rw, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, false, false, null, null, 0, false), pp.Tel!, 0);
                    Prep(q1, tq1, all3); var sq1 = tq1.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", q1.Tel, none, 1)[0].Text;
                    Prep(q2, tq2, all3); var sq2 = tq2.OnTranscript("Kutaisi Tower, Colt 1-1, ready for departure", q2.Tel, none, 2)[0].Text;
                    Prep(q3, tq3, all3); var sq3 = tq3.OnTranscript("Kutaisi Tower, Uzi 1-1, ready for departure", q3.Tel, none, 3)[0].Text;
                    q1.Tel = q1.Tel! with { AltMsl = fld.Elev + 60, Agl = 60, Ias = 80 };   // first one lifts off
                    Prep(q3, tq3, all3); var kq3 = tq3.Tick(q3.Tel, none, 4);   // the third ticks first: still waits
                    Prep(q2, tq2, all3); var kq2 = tq2.Tick(q2.Tel, none, 4);
                    Prep(q3, tq3, all3); var kq3b = tq3.Tick(q3.Tel, none, 5);
                    if (!sq1.Contains("cleared for takeoff") || !sq2.Contains("number 2 for departure") || !sq3.Contains("number 3 for departure") || kq3.Count != 0
                        || !(kq2.FirstOrDefault()?.Text ?? "").Contains("cleared for takeoff") || kq3b.Count != 0 || tq3.Phase != Phase.HoldShort)
                        throw new Exception($"R341 drei Abflüge: \"{sq1}\" / \"{sq2}\" / \"{sq3}\" / {kq3.Count} / \"{kq2.FirstOrDefault()?.Text}\" / {kq3b.Count}");
                    Console.WriteLine("OK   R341 drei Spieler am Rollhalt: number 1/2/3, nach dem Abheben des ersten wird der zweite (frühere Meldung) freigegeben");
                }

                // AI: player waits at the holding point (hold short) -> AI takeoff clearance; waiting on the runway (LUAW) or on final 3 NM -> not
                string Ready(Action set)
                {
                    aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
                    aiStage["DEP5"] = "taxi";
                    Ai = new() { Plane("DEP5", "dep-go:Kutaisi", e.At(500, 150), false, 6) };
                    set();
                    Run(); Said();
                    return aiStage["DEP5"];
                }
                var tel0 = pl.Tel;
                var k1 = Ready(() => Player(Phase.HoldShort, rw));
                var k2 = Ready(() => { Player(Phase.TaxiOut, rw); pl.Active!.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", pl.Tel, new[] { Plane("X1", "", e.At(-300, 0), false, 30) }, 1); });
                bool lu = pl.Active!.LineUp;
                var f3 = e.At(3 * NM, 0);
                pl.Tel = new Telemetry(fld.Elev + 300, 300, 70, e.Hdg * Math.PI / 180, f3.X, f3.Z, 0, 0, 0);
                var k3 = Ready(() => Player(Phase.Pattern, rw));
                pl.Tel = tel0;
                if (k1 != "ready" || !lu || k2 != "taxi" || k3 != "taxi")
                    throw new Exception($"A9/A10 KI-Startfreigabe: hold short {k1}, LUAW {lu}/{k2}, Endanflug {k3}");
                Console.WriteLine("OK   A9/A10 KI-Startfreigabe: Spieler am Rollhalt hält nicht, auf der Bahn wartend und im Endanflug 3 NM schon");
            }

            // Golf (mptest Al Ain/Bandar Abbas): DCS AI taxis blindly after START into the takeoff roll (parking spot 1800 m away: ~245 s); held AI 14 NM out (~220 s) stays
            // in holding instead of landing right into the takeoff roll, once the departure is airborne, RESUME; a landing long before the takeoff roll does not wait
            aiStage.Clear(); aiSent.Clear(); outq.Clear();
            aiStage["DEP3"] = "taxi"; aiStage["ARR8"] = "inbound";
            ArrQueue[fld.Name] = new() { "ai:ARR8" };
            Holds["ai:ARR8"] = (e.At(14 * NM, 0).X, e.At(14 * NM, 0).Z, 3000);
            Ai = new() { Plane("DEP3", "dep-go:Kutaisi", e.At(-1700, 600), false, 8), Plane("ARR8", "arr-hold:Kutaisi", e.At(14 * NM, 0), true, 118, 900) };
            Run();
            var held = cmds.Contains("RESUME;ARR8");
            Ai = new() { Plane("DEP3", "dep-go:Kutaisi", e.At(-1700, 600), false, 8), Plane("ARR8", "arr-hold:Kutaisi", e.At(30 * NM, 0), true, 118, 900) };   // far out: lands long after takeoff
            Run();
            var far = cmds.Contains("RESUME;ARR8");
            ArrQueue.Remove(fld.Name); Holds.Remove("ai:ARR8");
            if (held || !far) throw new Exception($"KI-Anflug ins Startfenster eines rollenden KI-Abflugs: gehalten {!held}, 30 NM frei {far} / {string.Join(",", cmds)}");
            Console.WriteLine("OK   KI-Anflug bleibt in der Warteschleife, solange er in den Startlauf eines rollenden KI-Abflugs fiele (Stand weit von der Schwelle), weit draußen RESUME");

            // R298: AI fuel under 0.20: "minimum fuel" (advisory, no priority); under 0.10: MAYDAY FUEL (priority in UpdateQueues)
            aiStage.Clear(); aiSent.Clear(); outq.Clear(); fuelTold.Clear();
            Ai = new() { Plane("ARR7", "arr:Kutaisi", e.At(20 * NM, 0), true, 120, 1500) };
            UpdateQueues(new() { pl });
            AiFuel["ARR7"] = 0.18; Run();
            var mf = string.Join(" | ", outq.Where(t => t.Role == "Approach" && !t.Pilot).Select(t => t.Text)); outq.Clear();
            AiFuel["ARR7"] = 0.08; Run();
            var mm = string.Join(" | ", outq.Where(t => t.Role == "Approach" && !t.Pilot).Select(t => t.Text)); outq.Clear();
            AiFuel.Remove("ARR7"); fuelTold.Clear(); ArrQueue.Clear();
            if (!mf.Contains("roger minimum fuel, you are number 1") || mf.Contains("mayday") || !mm.Contains("roger mayday, you are number 1") || mm.Contains("minimum fuel"))
                throw new Exception($"KI-Sprit R298: \"{mf}\" / \"{mm}\"");
            Console.WriteLine($"OK   R298 KI-Sprit: \"{mf}\" (kein Vorrang), dann \"{mm}\"");

            // Mission AI on its own route (forum: AI helicopter silent on the airfield frequency): no START/WAIT, taxi call when it starts moving;
            // helicopter: air taxi, takeoff from present position, landing without runway, not in the landing sequence
            {
                string All() { var s = string.Join(" | ", outq.Select(t => t.Text)); outq.Clear(); return s; }
                aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear(); cmds.Clear(); outq.Clear();
                Player(Phase.Parked, rw);
                Ai = new() { Plane("MIS1", "dep-go:Kutaisi", e.At(-1200, 300), false, 0) with { Mission = true } };
                Run(); var m0 = All();
                Ai = new() { Ai[0] with { Speed = 5 } }; Run(); var m1 = All();
                var (tx, tz) = e.At(500, 150);
                Ai = new() { Ai[0] with { X = tx, Z = tz } }; Run(); var m2 = All();
                Ai = new() { Plane("HEL1", "dep-go:Kutaisi", e.At(-1200, 300), true, 3, 5) with { Mission = true, Heli = true } };
                Run(); var h1 = All(); Run(); var h2 = All();
                Player(Phase.TaxiOut, opp);   // A41: AI arrivals wait -> exercise traffic into the hold, mission AI not
                Ai = new() { Plane("MIS2", "arr:Kutaisi", e.At(12 * NM, 0), true, 120, 1200) with { Mission = true }, Plane("ARR6", "arr:Kutaisi", e.At(14 * NM, 0), true, 120, 1200) };
                UpdateQueues(new() { pl }); Run(); var a1 = All();
                ArrQueue.Clear(); Holds.Remove("ai:ARR6");
                Player(Phase.Parked, rw);
                var hl = Plane("HEL2", "arr:Kutaisi", (fld.X + 5 * NM, fld.Z), true, 40, 400) with { Mission = true, Heli = true };
                Ai = new() { hl }; UpdateQueues(new() { pl }); var hq = ArrQueue.ContainsKey(fld.Name); Run(); var l1 = All();
                Ai = new() { hl with { X = fld.X + NM, AltMsl = fld.Elev + 100 } }; Run(); var l2 = All();
                Ai = new() { hl with { X = fld.X + NM, AltMsl = fld.Elev, InAir = false, Speed = 0 } }; Run(); var l3 = All();
                aiStage["MIS3"] = "gone";   // round trip: back at the departure field
                Ai = new() { Plane("MIS3", "arr:Kutaisi", e.At(12 * NM, 0), true, 120, 1200) with { Mission = true } }; UpdateQueues(new() { pl }); Run(); var r1 = All();
                ArrQueue.Clear();
                if (m0 != "" || !m1.Contains("request taxi") || !m1.Contains("taxi to holding point") || aiStage["MIS1"] != "ready" || !m2.Contains("cleared for takeoff") || cmds.Any(c => c.Contains("MIS1"))
                    || !h1.Contains("request air taxi") || !h1.Contains("air taxi") || !h2.Contains("cleared for takeoff from present position") || h2.Contains("runway")
                    || !a1.Contains("inbound for landing") || !cmds.Any(c => c.StartsWith("WAIT;ARR6;")) || cmds.Any(c => c.Contains("MIS2"))
                    || !l1.Contains("radar contact.") || hq || !l2.Contains("cleared to land.") || l2.Contains("runway") || !l3.Contains("request air taxi to parking") || !r1.Contains("inbound for landing"))
                    throw new Exception($"Missions-KI: \"{m0}\" / \"{m1}\" / \"{m2}\" / \"{h1}\" / \"{h2}\" / \"{a1}\" / \"{l1}\" / \"{l2}\" / \"{l3}\" / \"{r1}\" / {string.Join(",", cmds)}");
                Console.WriteLine($"OK   Missions-KI funkt ohne START/WAIT (\"{m1}\"), Hubschrauber: \"{h2}\" / \"{l2}\"");
            }

            // Carrier into the wind: mission does not turn (land ahead) -> Marshal states the BRC being steered, after 5 min new CVTURN; leg finished (ship stationary) likewise
            cmds.Clear();
            var cv = new Carrier.Boat("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 127.5, "73X", "11", Wx: 10);   // heading north, wind from south -> BRC 189.1
            var (cvBrc, cvKt) = cv.IntoWind;
            Ai = new() { new Traffic(77, "FA-18C_hornet", 5 * NM, 0, 1000, 0, 150, "CV1", "cv", true, 2) };
            int Turns() => cmds.Count(c => c.StartsWith("CVTURN;CVN;"));
            Carrier.Boats = new() { cv }; SteerBoats();
            Carrier.Boats = new() { cv with { NoTurn = true } }; SimSec += 120; SteerBoats();
            int t1 = Turns(); double exp = Carrier.Boats[0].ExpBrc;
            SimSec += 200; SteerBoats();
            int t2 = Turns();
            Carrier.Boats = new() { cv with { Hdg = cvBrc * Math.PI / 180, Speed = 0 } }; SimSec += 310; SteerBoats();
            int t3 = Turns();
            Carrier.Boats = new() { cv with { Hdg = cvBrc * Math.PI / 180, Speed = cvKt * 0.514444 } }; SimSec += 310; SteerBoats();
            int t4 = Turns();
            Ai = new(); SimSec += 200; SteerBoats();
            if (t1 != 1 || exp != 0 || t2 != 2 || t3 != 3 || t4 != 3 || !cmds.Contains("CVRESUME;CVN"))
                throw new Exception($"Träger in den Wind, neu drehen: {t1}/{t2}/{t3}/{t4}, ExpBrc {exp}, {string.Join(",", cmds)}");
            Console.WriteLine("OK   Träger nicht im Wind (Land voraus, Schenkel zu Ende) -> nach 5 min neues CVTURN, Marshal-BRC wie gefahren");
        }
        finally
        {
            (Fields, Wx, SimSec, stateTime, SimLog, AiHook) = (fs, ws, ss, sst, sl, null);
            Ai = new(); aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
            Carrier.Boats = new(); boatInWind.Clear(); boatTurnAt.Clear();
        }
    }

    // ------------------------------------------------------------ AI flights in combat (Flights, KI-FUNK-PLAN)
    static Flights.World FlightWorld() => new(
        ActivePilots().Where(p => p.Tel != null).Select(p => (p.Coalition, p.Tel!.X, p.Tel.Z, p.Gid, p.Unit, p.Callsign)).ToList(),
        (side, x, z) =>
        {
            var me = new Ops.Me("", new Telemetry(0, 0, 0, 0, x, z, 0, 0, 0), "", side);
            var a = Ops.AwacsOf(Ai, me);
            if (a == null && !Ops.Gci.Contains(side) && Ops.GciOf(me) == null) return null;   // no AWACS, no ground radar: nobody acknowledges
            return (Ops.AwacsName(a, me), Ops.AwacsFreqOf(a, me) is > 0 and var f ? f : Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5));
        },
        Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5));

    static void FlightComms()
    {
        if (!MissionData || Cfg.AiFlightComms == "aus" || !On("ai")) return;
        List<Flights.Call> calls;
        lock (TowerLock) calls = Flights.Tick(Now(), FlightWorld());
        foreach (var c in calls)
        {
            var fs = c.Freq2 > 0 ? new[] { c.Freq, c.Freq2 } : new[] { c.Freq };
            SayQueue.Add(c.Who == "" ? new Tx(c.Text, "AWACS", c.Station, 0, Freqs: fs, Prio: c.Prio, MaxAge: c.MaxAge, Side: c.Side)
                                     : new Tx(c.Text, "Flight", c.Station, 0, true, AiVoiceFor(c.Who), Freqs: fs,
                                              Prio: c.Prio, MaxAge: c.MaxAge, Stress: c.Stress, Side: c.Side));
        }
    }

    /// One fixed voice per AI flight, no pitch shift (it sounds artificial): the Piper models the installer ships, least used by the other roles first,
    /// the AWACS voice left out (A142), the player's pilot voice last. Flights take the voices in turn; the lead keeps the flight's voice, wingmen get the next ones.
    static string[]? aiVoices;
    static readonly Dictionary<string, int> aiSlot = new();   // DCS group -> index of its voice
    static string[] AiPool(Func<string, bool> has)
    {
        string M(string v) => v.Split('@')[0];
        var awacs = Cfg.Voices.GetValueOrDefault("AWACS", "").Split('|').Select(M).ToList();
        int Used(string m) => Cfg.Voices.Values.Count(v => v.Split('|').Select(M).Contains(m)) + (M(Cfg.PilotVoice) == m ? 10 : 0);
        return new[] { "en_US-ljspeech-medium", "en_US-bryce-medium", "en_US-ryan-medium", "en_US-joe-medium" }
               .Where(m => !awacs.Contains(m) && has(m)).OrderBy(Used).DefaultIfEmpty(Cfg.PilotVoice).ToArray();
    }
    static string AiVoiceFor(string unit)
    {
        aiVoices ??= AiPool(m => File.Exists(Path.Combine(Root, "models", m + ".onnx")));
        Flights.Units.TryGetValue(unit, out var u);
        string grp = u?.Group ?? unit;
        int slot, el = u != null && char.IsDigit(u.Callsign.LastOrDefault()) ? u.Callsign[^1] - '0' : 1;   // element number: Enfield12 -> 2
        lock (aiSlot) if (!aiSlot.TryGetValue(grp, out slot)) aiSlot[grp] = slot = aiSlot.Count;
        return aiVoices[(slot + Math.Max(el, 1) - 1) % aiVoices.Length];
    }

    static void AiCmd(string line, bool log = true)   // log: false for the VR wheel text (every wheel movement)
    {
        if (log && line.StartsWith("MENU;")) Trace("KI", line);   // radio menu entries: debug file only, not the console
        else if (log) Log($"[KI ] {line}");
        if (AiHook != null) { AiHook(line); return; }
        try
        {
            var f = Path.Combine(MenuDir, Clock.Ticks.ToString());
            File.WriteAllText(f + ".tmp", line);
            File.Move(f + ".tmp", f + ".cmd");
        }
        catch (IOException e) { Trace("FEHLER", $"Befehl an die Mission {line}: {e.Message}"); }
    }

}
