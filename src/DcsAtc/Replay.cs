using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

static partial class Program
{
    /// DcsAtc --replay <atc-debug.txt>: a reporter's debug log through this build's app logic (TickAll/Request, as --mptest). The player's telemetry comes from
    /// the [TEL] lines (every 10 s, interpolated per second), his calls from the [ANFRAGE] lines with their confidence, wind and QNH from the logged ATIS.
    /// Prints the controller calls of the log (ALT) next to the ones this build makes (NEU), "=" where both are the same.
    /// Data of the reporter's map next to the log file, if present: airfields.txt, charts.jsonc, DcsAtc-Terrain-<map>-<id>.txt (from his %TEMP%).
    /// The airfield of each call is the one the log picked ([PICK]); a [START] line (app started anew) empties the state.
    /// Limits: no other traffic, no AWACS/tanker/carrier in the air, positions only every 10 s, random wordings ("roger"/"copy", squawk) differ.
    public static int ReplayCli(string path)
    {
        if (!File.Exists(path)) { Console.WriteLine($"Datei fehlt: {path}"); return 1; }
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string At(string f) => Path.Combine(dir, f);
        if (File.Exists(At("charts.jsonc"))) Airfield.OwnChart = OwnChart(At("charts.jsonc"));
        if (File.Exists(At("airfields.txt"))) FieldsFile = At("airfields.txt");
        LoadFields();
        int terr = 0;
        foreach (var f in Fields.Where(f => f.Id > 0).Append(Airfield.Map))
            if (At($"DcsAtc-Terrain-{Airfield.Theatre}-{(f == Airfield.Map ? "map" : f.Id)}.txt") is var tf && File.Exists(tf)) { f.LoadTerrain(tf); terr++; }
        LoadTerrain();   // own logs: terrain of the last mission (%TEMP%)
        Console.WriteLine($"Replay {Path.GetFileName(path)}: Karte {Airfield.Theatre}, {Fields.Count} Plätze ({(FieldsFile != null ? "airfields.txt neben dem Log" : "airfields.txt des Repos")}), " +
                          $"Gelände {(terr > 0 ? $"{terr} Dateien neben dem Log" : Fields.Any(f => f.HasTerrain) ? "aus %TEMP%" : "keins")}, charts.jsonc {(Airfield.OwnChart != null ? "neben dem Log" : "keine")}");
        Replay(File.ReadAllLines(path)).ForEach(Console.WriteLine);
        return 0;
    }

    static readonly string[] DigitWord = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "niner" };
    static int? Spoken(string words) => words.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => Array.IndexOf(DigitWord, w == "nine" ? "niner" : w.ToLowerInvariant())).ToList() is { Count: > 0 } d && d.All(x => x >= 0)
        ? d.Aggregate(0, (a, x) => a * 10 + x) : null;

    internal static List<string> Replay(string[] lines)
    {
        var line = new Regex(@"^(\d\d):(\d\d):(\d\d)\.(\d{3}) \[(\w+)\] (.*)$");
        var telRe = new Regex(@"^(?<cs>.+?) \((?<type>[^,]*), unit (?<id>\d+), gid (?<gid>\d+), Seite (?<side>\d+)\) X (?<x>-?\d+) Z (?<z>-?\d+) (?<msl>-?\d+) ft MSL (?<agl>-?\d+) ft AGL (?<kt>\d+) kt Kurs (?<hdg>\d+)");
        var reqRe = new Regex(@"^(?<src>\S+) (?<cs>.+?) \(unit (?<id>\d+)\) via (?<via>.+?) menu (?<menu>True|False) conf (?<conf>[\d.,]+) aktiv (?<akt>.+?) \w* Pin (?<pin>.+?): (?<text>.*)$");
        var said = new Regex(@"^\[(?!SRS|RAD|SIM|ATIS|ASR|ATC|INF|DCS)[A-Z]{3}\] (?<t>.+?: .*)$");
        double D(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
        var tels = new Dictionary<uint, List<(double T, string Cs, string Type, int Gid, int Side, Telemetry Tel)>>();
        var reqs = new List<(double T, uint Id, string Src, string Via, bool Menu, double Conf, string Akt, string Pin, string Text, string Pick)>();
        var starts = new List<double>();   // [START]: the app was started anew, its state was empty
        var old = new List<(double T, string Text)>();
        var wx = new List<(double T, double Dir, double Kt, double Qnh)>();
        double day = 0, lastT = -1, t0 = -1, tEnd = 0;
        foreach (var l in lines)
        {
            if (line.Match(l) is not { Success: true } m) continue;
            double t = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value) + int.Parse(m.Groups[4].Value) / 1000.0 + day;
            if (t < lastT - 43200) { day += 86400; t += 86400; }   // past midnight
            lastT = t;
            var (kat, body) = (m.Groups[5].Value, m.Groups[6].Value);
            if (kat == "TEL" && telRe.Match(body) is { Success: true } tm)
            {
                if (t0 < 0) t0 = t;
                var id = uint.Parse(tm.Groups["id"].Value);
                if (!tels.TryGetValue(id, out var list)) tels[id] = list = new();
                list.Add((t, tm.Groups["cs"].Value, tm.Groups["type"].Value, int.Parse(tm.Groups["gid"].Value), int.Parse(tm.Groups["side"].Value),
                          new Telemetry(D(tm.Groups["msl"].Value) * 0.3048, D(tm.Groups["agl"].Value) * 0.3048, D(tm.Groups["kt"].Value) * 0.514444, D(tm.Groups["hdg"].Value) * Math.PI / 180,
                                        D(tm.Groups["x"].Value), D(tm.Groups["z"].Value), 0, 0, 0)));
            }
            else if (kat == "ANFRAGE" && reqRe.Match(body) is { Success: true } rm)
                reqs.Add((t, uint.Parse(rm.Groups["id"].Value), rm.Groups["src"].Value, rm.Groups["via"].Value, rm.Groups["menu"].Value == "True", D(rm.Groups["conf"].Value), rm.Groups["akt"].Value, rm.Groups["pin"].Value, rm.Groups["text"].Value, "-"));
            else if (kat == "PICK" && reqs.Count > 0 && t - reqs[^1].T < 1 && Regex.Match(body, @"^.+?: (.+?) \(") is { Success: true } pm) reqs[^1] = reqs[^1] with { Pick = pm.Groups[1].Value };
            else if (kat == "START") starts.Add(t);
            else if (kat == "LOG" && body.StartsWith("[ATIS]"))
            {
                var w = Regex.Match(body, @"[Ww]ind ((?:\w+ ){3})degrees, ((?:\w+ )+?)knots");
                var q = Regex.Match(body, @"QNH ((?:(?:zero|one|two|three|four|five|six|seven|eight|niner) ?)+)");
                wx.Add((t, w.Success ? Spoken(w.Groups[1].Value) ?? 0 : 0, w.Success ? Spoken(w.Groups[2].Value) ?? 0 : 0, q.Success ? Spoken(q.Groups[1].Value) ?? 1013 : 1013));
            }
            else if (kat == "LOG" && said.Match(body) is { Success: true } sm) old.Add((t, sm.Groups["t"].Value));
            if (t0 >= 0) tEnd = t;
        }
        var outp = new List<(double T, int Ord, string Tag, string Text)>();
        if (t0 < 0) return new() { "Keine [TEL]-Zeilen im Log (Debug-Log war aus?)." };
        if (reqs.Select(r => r.Via).Where(v => v != "-" && Fields.All(f => f.Name != v)).Distinct().ToList() is { Count: > 0 } foreign)   // another map: every reply would come from a wrong airfield
            return new() { $"Das Log ist von einer anderen Karte: {string.Join(", ", foreign)} fehlt in airfields.txt ({Airfield.Theatre}). Die airfields.txt des Melders (aus seinem DCS-ATC-Ordner) neben das Log legen, " +
                           $"dazu sein Gelände (%TEMP%\\DcsAtc-Terrain-<Karte>-*.txt) und seine charts.jsonc." };

        void Reset()   // app state (static), as MpRun
        {
            Pilots.Clear(); ArrQueue.Clear(); Holds.Clear(); aiSent.Clear(); lastAiStart.Clear(); AiCallsign.Clear(); aiStage.Clear(); AiGid.Clear();
            AiFuel.Clear(); aiStackFt.Clear(); fuelTold.Clear(); Atis.Clear();
            while (SayQueue.TryTake(out _)) { }
        }
        Reset();
        var (simLog0, simSec0) = (SimLog, SimSec);
        (SimLog, Ai, Sky, Wx) = (new(), new(), (false, 0, 80000), new());
        int ord = 0;
        (double Wx, double Wz, double Qnh) wind = (0, 0, 1013);
        try
        {
            void Drain(double t)
            {
                SimLog!.Clear();
                while (SayQueue.TryTake(out var tx))
                    if (!tx.Pilot && tx.Role != "Info") outp.Add((t, ord++, "NEU", $"{tx.Station}: {tx.Text}"));
            }
            Telemetry? TelAt(List<(double T, string Cs, string Type, int Gid, int Side, Telemetry Tel)> s, double t)
            {
                int i = s.FindLastIndex(x => x.T <= t);
                if (i < 0) return null;
                var a = s[i].Tel;
                if (i + 1 >= s.Count || s[i + 1].T - s[i].T > 30) return t - s[i].T > 30 ? null : a with { WindX = wind.Wx, WindZ = wind.Wz, Pressure = wind.Qnh * 0.750062 };   // gap: DCS paused or closed
                var b = s[i + 1].Tel;
                double k = (t - s[i].T) / (s[i + 1].T - s[i].T), dh = Math.IEEERemainder(b.Hdg - a.Hdg, 2 * Math.PI);
                return new Telemetry(a.AltMsl + (b.AltMsl - a.AltMsl) * k, a.Agl + (b.Agl - a.Agl) * k, a.Ias + (b.Ias - a.Ias) * k, a.Hdg + dh * k, a.X + (b.X - a.X) * k, a.Z + (b.Z - a.Z) * k,
                                     wind.Wx, wind.Wz, wind.Qnh * 0.750062, (b.AltMsl - a.AltMsl) / (s[i + 1].T - s[i].T));
            }
            for (double s = Math.Floor(t0); s <= tEnd + 1; s++)
            {
                SimSec = s;
                if (starts.Any(x => x > s - 1 && x <= s)) Reset();
                if (wx.FindLast(x => x.T <= s) is { T: > 0 } w)
                {
                    double to = (w.Dir + Ops.MagVar + 180) * Math.PI / 180, ms = w.Kt * 0.514444;
                    wind = (ms * Math.Cos(to), ms * Math.Sin(to), w.Qnh);
                    foreach (var f in Fields) Wx[f.Name] = (wind.Wx, wind.Wz, 15, w.Qnh * 100 * Math.Pow(1 - 2.25577e-5 * (f.Elev + 2), 5.25588));
                }
                foreach (var (id, list) in tels)
                {
                    if (TelAt(list, s) is not { } tel) continue;
                    var cur = list[list.FindLastIndex(x => x.T <= s)];
                    if (!Pilots.TryGetValue("U" + id, out var p)) Pilots["U" + id] = p = new Pilot { Unit = "U" + id, Id = id };
                    (p.Callsign, p.Type, p.Gid, p.Coalition, p.Tel, p.Seen) = (cur.Cs, cur.Type, cur.Gid, cur.Side, tel, Clock);
                }
                stateTime = Clock;
                TickAll();
                Drain(s);
                foreach (var r in reqs.Where(r => r.T > s - 1 && r.T <= s))
                {
                    outp.Add((r.T, ord++, "PILOT", $"{r.Text}   ({r.Conf:0.00})"));
                    if (!Pilots.TryGetValue("U" + r.Id, out var p)) continue;
                    // ponytail: airfield as logged ([PICK], as a pin for this call): the tuned SRS frequencies it came from are not in the log. A changed airfield choice does not show in the replay; own check if that matters
                    Tower? Tw(string name) => Fields.FirstOrDefault(f => f.Name == name) is { } f ? TowerFor(p, f) : null;
                    if (Tw(r.Akt) is { } at && at != p.Active) Switch(p, at);
                    var forced = r.Pin == "-" ? Tw(r.Pick) : null;
                    p.Pinned = forced ?? Tw(r.Pin);
                    Request(p, r.Text, r.Src, r.Menu, Fields.FirstOrDefault(f => f.Name == r.Via), r.Conf);
                    if (forced != null && p.Pinned == forced) p.Pinned = null;
                    Drain(r.T);
                }
            }
        }
        finally { (SimLog, SimSec) = (simLog0, simSec0); Pilots.Clear(); while (SayQueue.TryTake(out _)) { } }

        // same call in the log within 30 s: "="
        int same = 0, onlyNew = 0;
        var left = old.ToList();
        for (int i = 0; i < outp.Count; i++)
        {
            if (outp[i].Tag != "NEU") continue;
            int j = left.FindIndex(o => o.Text == outp[i].Text && Math.Abs(o.T - outp[i].T) <= 30);
            if (j >= 0) { outp[i] = outp[i] with { Tag = "=" }; left.RemoveAt(j); same++; } else onlyNew++;
        }
        outp.AddRange(left.Select(o => (o.T, ord++, "ALT", o.Text)));
        static string Hms(double t) => TimeSpan.FromSeconds(t % 86400).ToString(@"hh\:mm\:ss");
        var res = outp.OrderBy(o => o.T).ThenBy(o => o.Ord).Select(o => $"{Hms(o.T)} {o.Tag,-5} {o.Text}").ToList();
        res.Add($"Replay: {reqs.Count} Funksprüche, Lotse gleich {same}, nur im Log {left.Count}, nur neu {onlyNew}");
        return res;
    }

    /// Self-test: a small log (Kutaisi, parked, taxi request) runs through; the logged reply and the new one are both listed.
    internal static void ReplayTest()
    {
        LoadFields();
        var k = Fields.First(f => f.Name == "Kutaisi");
        var (x, z) = k.Spots.Count > 0 ? (k.Spots[0].X, k.Spots[0].Z) : (k.X, k.Z);
        string Tel(string t) => FormattableString.Invariant($"{t} [TEL] Enfield 1-1 (FA-18C_hornet, unit 5, gid 7, Seite 2) X {x:0} Z {z:0} {k.Elev / 0.3048:0} ft MSL 0 ft AGL 0 kt Kurs 250 | aktiv Kutaisi Parked");
        var r = Replay(new[]
        {
            "10:00:00.000 [LOG] [ATIS] Kutaisi information Alpha. Wind two five zero degrees, one zero knots. QNH one zero one three, altimeter two niner niner two.",
            Tel("10:00:01.000"), Tel("10:00:11.000"),
            "10:00:12.000 [ANFRAGE] SRS Enfield 1-1 (unit 5) via Kutaisi menu False conf 0,90 aktiv Kutaisi Parked Pin -: Ground: Kutaisi Ground, Enfield 1-1, request taxi",
            "10:00:12.100 [LOG] [GRO] Kutaisi Ground: Enfield one one, an older answer.",
            Tel("10:00:21.000"),
        });
        if (!r.Any(l => l.StartsWith("10:00:12 PILOT Ground: Kutaisi Ground, Enfield 1-1, request taxi")) || !r.Any(l => l.StartsWith("10:00:12 ALT   Kutaisi Ground: Enfield one one, an older answer."))
            || !r.Any(l => l.StartsWith("10:00:12 NEU   Kutaisi Ground: Enfield one one") && l.Contains("taxi to holding point")) || !r[^1].EndsWith("1 Funksprüche, Lotse gleich 0, nur im Log 1, nur neu 1"))
            throw new Exception("Replay: " + string.Join(" | ", r));
        var other = Replay(new[] { Tel("10:00:01.000"), "10:00:12.000 [ANFRAGE] SRS Enfield 1-1 (unit 5) via Nellis menu False conf 0.90 aktiv Nellis Parked Pin -: Ground: Nellis Ground, Enfield 1-1, request taxi" });
        if (other.Count != 1 || !other[0].Contains("anderen Karte: Nellis")) throw new Exception("Replay fremde Karte: " + string.Join(" | ", other));
        Console.WriteLine("OK   Replay: Log mit Telemetrie, ATIS-Wind und Funkspruch läuft durch die App-Logik, alte und neue Lotsenantwort nebeneinander");
    }
}
