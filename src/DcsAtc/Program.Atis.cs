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
    // ------------------------------------------------------------ ATIS per airfield (weather from the mission)
    static readonly Dictionary<string, AtisInfo> Atis = new();

    /// Rebuild text; if runway, wind, QNH, weather or the full hour of the mission clock change, there is a new identifier.
    /// R344: wind only on a significant change (speed 5 kt, direction 30° at 5 kt or more), temperature not at all (FAA JO 7110.65 2-9-2); the text stays current.
    /// true = new identifier (not at the first text of an airfield).
    static bool UpdateAtis(Airfield f)
    {
        if (!Wx.TryGetValue(f.Name, out var w) || !AtisReady(f)) return false;   // LA22: not yet without terrain
        if (!Atis.TryGetValue(f.Name, out var a)) Atis[f.Name] = a = new AtisInfo { Letter = (char)('A' + Clock.Minute % 26) };
        var tw = new Tower(f, "");
        var tel = new Telemetry(f.Elev + 2, 0, 0, 0, f.X, f.Z, w.Wx, w.Wz, w.Pa);
        double clk = Carrier.Clock, kt5 = 5 * 0.514444;
        double v0 = Math.Sqrt(a.W.X * a.W.X + a.W.Z * a.W.Z), v1 = Math.Sqrt(w.Wx * w.Wx + w.Wz * w.Wz), dd = Math.Abs(Math.Atan2(w.Wz, w.Wx) - Math.Atan2(a.W.Z, a.W.X)) * 180 / Math.PI;
        bool sig = a.Key == "" || Math.Abs(v1 - v0) >= kt5 || Math.Max(v0, v1) >= kt5 && Math.Min(dd, 360 - dd) >= 30;
        var key = tw.AtisText("A", sig ? tel : tel with { WindX = a.W.X, WindZ = a.W.Z }, a.Key == "" ? w.TempC : a.TempC, Sky.Clouds, Sky.BaseM, 0, clk < 0 ? clk : clk - clk % 3600)   // Key only with the full hour, otherwise the identifier would change every minute
                + new[] { 800, 1500, 3000, 5000, 9500 }.Count(b => Sky.VisM >= b)
                + (int)(clk / 3600) + f.BestRunway(w.Wx, w.Wz);   // full hour also with unknown map (R125, then without time in the text); visibility only in steps like SPECI, otherwise the identifier would change every kilometer as fog rolls in
        bool neu = false;
        if (key != a.Key)
        {
            neu = a.Key != "";
            if (neu) a.Letter = (char)('A' + (a.Letter - 'A' + 1) % 26);
            (a.Key, a.W, a.TempC, a.Clk) = (key, (w.Wx, w.Wz), w.TempC, clk);
            a.Call = tw.AtisCall(a.Letter.ToString(), tel);
        }
        var text = tw.AtisText(a.Letter.ToString(), tel, w.TempC, Sky.Clouds, Sky.BaseM, Sky.VisM, a.Clk);   // R344: current values, time of the identifier
        if (text != a.Text) { a.Text = text; a.Mp3 = ""; Log($"[ATIS] {a.Text}"); }
        return neu;
    }

    /// Announcement of the new identifier on the airfield frequency to everyone listening: players at the airfield, on the ground or under 20 NM. Empty = nobody there or ATIS off.
    /// A133: with the side of the airfield, at a neutral airfield once per side of the listeners.
    static List<Tx> AtisCall(Airfield f, List<Pilot> pilots) =>
        !Cfg.Atis || !Atis.TryGetValue(f.Name, out var a) ? new()
        : pilots.Where(p => p.Active?.F == f && p.Tel is { } t && (t.Agl < 5 || Dist(t.X, t.Z, f.X, f.Z) < 20 * NM)).Select(p => SideOf(f, p)).Distinct()
                .Select(s => new Tx(a.Call, "Tower", f.Station, 0, Freqs: FieldFreqs(f, "Tower"), Prio: 2, Side: s)).ToList();

    /// Self-test N27: new identifier only on changed ATIS or at the full hour (mission clock), time in Zulu, announcement only to players at the airfield.
    static void AtisTest()
    {
        var (wx0, sky0, clk0, cfg0, tel0) = (Wx, Sky, Carrier.Clock, Cfg.Atis, Atis.ToDictionary(x => x.Key, x => x.Value));
        var fld = Airfield.Kutaisi();
        try
        {
            Atis.Clear();
            Cfg.Atis = true;
            Sky = (false, 0, 80000);
            double pa = 101325 * Math.Pow(1 - 2.25577e-5 * (fld.Elev + 2), 5.25588);   // Standard atmosphere at the airfield: QNH 1013
            Wx = new() { [fld.Name] = (-5, 0, 15, pa) };
            Carrier.Clock = 10 * 3600 + 20 * 60;   // 10:20 local time = 06:20 Zulu
            bool first = UpdateAtis(fld);
            var a = Atis[fld.Name];
            char l0 = a.Letter;
            if (first || !a.Text.Contains("information " + Tower.Phonetic(l0.ToString()) + ", time zero six two zero Zulu.")) throw new Exception($"ATIS erste Kennung {first}: {a.Text}");
            Carrier.Clock = 10 * 3600 + 50 * 60;
            if (UpdateAtis(fld) || a.Letter != l0) throw new Exception("ATIS: gleiche Stunde, gleiches Wetter = keine neue Kennung");
            Wx[fld.Name] = (-9, 4, 15, pa);   // Wind veers and increases
            bool wind = UpdateAtis(fld);
            char l1 = a.Letter;
            if (!wind || l1 != (char)('A' + (l0 - 'A' + 1) % 26) || !a.Text.Contains("time zero six five zero Zulu") || UpdateAtis(fld))
                throw new Exception($"ATIS Windänderung: neu={wind} {l0}->{l1}, danach nichts mehr: {a.Text}");
            if (!a.Call.StartsWith("Attention all aircraft, Kutaisi information " + Tower.Phonetic(l1.ToString()) + " now current, QNH one zero one three")) throw new Exception("ATIS Ansage: " + a.Call);
            Carrier.Clock = 11 * 3600 + 5;   // full hour: new identifier without weather change
            bool hour = UpdateAtis(fld);
            if (!hour || a.Letter != (char)('A' + (l1 - 'A' + 1) % 26) || !a.Text.Contains("time zero seven zero zero Zulu")) throw new Exception($"ATIS volle Stunde: neu={hour} {a.Text}");
            Carrier.Clock = 11 * 3600 + 59 * 60;
            if (UpdateAtis(fld)) throw new Exception("ATIS: Minuten ändern die Kennung nicht");
            Sky = (false, 0, 2000);   // Fog rolls in: new identifier only when crossing a visibility step (800/1500/3000/5000/9500 m)
            bool fog = UpdateAtis(fld);
            Sky = (false, 0, 2900);
            bool step = UpdateAtis(fld);
            Sky = (false, 0, 3100);
            if (!fog || step || !UpdateAtis(fld) || !a.Text.Contains("Visibility three kilometers")) throw new Exception($"ATIS Sichtstufen: {fog} {step} {a.Text}");
            // R344: wind +1 kt and 1 °C warmer -> same identifier, text current; +10 kt -> new identifier
            char l2 = a.Letter;
            double k1 = 1 + 0.514444 / Math.Sqrt(97), k10 = 1 + 10 * 0.514444 / Math.Sqrt(97);
            Wx[fld.Name] = (-9 * k1, 4 * k1, 16, pa);
            bool small = UpdateAtis(fld);
            string smallTxt = a.Text;
            Wx[fld.Name] = (-9 * k10, 4 * k10, 16, pa);
            if (small || !smallTxt.Contains("Temperature one six") || !smallTxt.Contains("information " + Tower.Phonetic(l2.ToString())) || !UpdateAtis(fld) || a.Letter != (char)('A' + (l2 - 'A' + 1) % 26))
                throw new Exception($"R344 ATIS kleine Windänderung: {small} {smallTxt} / {a.Text}");
            Console.WriteLine("OK   R344 ATIS: Wind +1 kt gleiche Kennung, +10 kt neue Kennung");
            // Announcement only to players of this airfield, on the ground or under 20 NM
            Pilot P(double agl, double nm) => new() { Active = new Tower(fld, "x"), Tel = new Telemetry(fld.Elev + agl, agl, 50, 0, fld.X + nm * NM, fld.Z, 0, 0, 0) };
            var (ground, near, far) = (P(0, 0), P(500, 15), P(3000, 30));
            var (other, stray) = (new Pilot { Active = new Tower(Airfield.Kutaisi(), "x"), Tel = near.Tel }, new Pilot { Tel = near.Tel });   // other airfield (own instance), without flow
            if (AtisCall(fld, new() { far, other, stray }).Count != 0) throw new Exception("ATIS Ansage: 30 NM weit weg, anderer Platz, ohne Ablauf = niemand");
            var tx = AtisCall(fld, new() { far, ground }).SingleOrDefault();
            var red = P(500, 15); red.Coalition = 1;   // A133: neutral airfield -> once per side
            var sides = string.Join(",", AtisCall(fld, new() { ground, near, red }).Select(x => x.Side));
            if (tx == null || AtisCall(fld, new() { near }).Count != 1 || tx.Text != a.Call || tx.Gid != 0 || tx.Station != "Kutaisi Tower" || tx.Side != 2 || sides != "2,1")
                throw new Exception($"ATIS Ansage: Spieler am Platz hört sie (Seiten {sides})");
            Cfg.Atis = false;
            if (AtisCall(fld, new() { ground }).Count != 0) throw new Exception("ATIS Ansage: mit Atis=false keine");
            // A35: own ATIS frequency per airfield, collision with airfield and function frequencies
            var atis0 = Cfg.AtisFreqs;
            try
            {
                Cfg.AtisFreqs = new() { ["Kutaisi"] = 262.0 };
                var kutF = new Airfield { Name = "Kutaisi", Uhf = 263.0, Vhf = 134.0 };
                var ac = AtisClashes(new[] { kutF }, new() { ["Kutaisi"] = 263.0, ["Senaki"] = 251.5, ["Batumi"] = 262.5 }, new Config().Frequencies).ToList();
                if (AtisFreq("Kutaisi") != 262.0 || AtisFreq("Senaki") != Cfg.Frequencies["ATIS"] || ac.Count != 2 || ac[0] != "ATIS Kutaisi 263.0 = Kutaisi UHF" || ac[1] != "ATIS Senaki 251.5 = AWACS")
                    throw new Exception($"ATIS-Frequenz: {AtisFreq("Kutaisi")} / {AtisFreq("Senaki")} / {string.Join(" | ", ac)}");
                if (FieldFreqs(kutF, "ATIS") is not [262.0] || FieldFreqs(new Airfield { Name = "Senaki", Uhf = 261.0 }, "ATIS") is not [var sf] || sf != Cfg.Frequencies["ATIS"])   // Request (F10/radio wheel) on the same frequency as the cycle
                    throw new Exception("ATIS-Abruf: " + string.Join(",", FieldFreqs(kutF, "ATIS") ?? Array.Empty<double>()));
            }
            finally { Cfg.AtisFreqs = atis0; }
            // R120: mission run time jumps back (new mission/restart): identifiers gone; afterwards exactly one first identifier without announcement. ESC pause (run time stands, wall clock runs) clears nothing.
            var (st0, ms0) = (stateTime, missionSec);
            try
            {
                missionSec = -1; MissionGap(500); stateTime = Clock - TimeSpan.FromSeconds(60); MissionGap(500.5);
                if (Atis.Count != 1) throw new Exception("ATIS R120: 60 s Pause darf die Kennung nicht löschen");
                MissionGap(2);
                if (Atis.Count != 0) throw new Exception("ATIS R120: Missionslaufzeit zurück (neue Mission) muss die Kennungen löschen");
                Wx[fld.Name] = (3, 3, 10, pa);
                if (UpdateAtis(fld) || Atis[fld.Name].Text == "") throw new Exception("ATIS R120: neue Mission = erste Kennung ohne Ansage");
                // LA22: airfield with terrain file (Id) and side from the terrain: no ATIS without terrain at 10 s mission time; still none at 3 min -> without
                var nf22 = Airfield.FromDcs("Testfeld LA22", fld.X, fld.Z, 20, new[] { ("13", 0.0, fld.X, fld.Z, 2500.0) }); nf22.Id = 987654;
                Wx[nf22.Name] = (3, 3, 10, pa);
                missionSec = 10; UpdateAtis(nf22);
                bool early = Atis.ContainsKey(nf22.Name);
                missionSec = 180; UpdateAtis(nf22);
                if (early || !Atis.ContainsKey(nf22.Name)) throw new Exception($"ATIS LA22: ohne Gelände {early}, nach 3 min {Atis.ContainsKey(nf22.Name)}");
                Atis.Remove(nf22.Name); Wx.Remove(nf22.Name);
            }
            finally { (stateTime, missionSec) = (st0, ms0); }
            // LA17: own aircraft frozen in the air at speed (game paused) -> Now() stands from the 3rd identical tick, afterwards it runs on without the pause
            var (sim17, po17, pa17, fr17, ft17) = (SimSec, pauseOff, pausedAt, frozen, frozenTel);
            try
            {
                var tel17 = new Telemetry(3000, 3000, 270, 0, 1000, 2000, 0, 0, 0);
                var n17 = new List<double>();
                for (int s = 0; s < 70; s++) { SimSec = 1000 + s; PauseCheck(s is < 5 or > 65 ? tel17 with { X = 1000 + s * 270 } : tel17); n17.Add(Now()); }
                if (n17[7] != 1007 || n17.Skip(8).Take(58).Any(x => x != 1008) || n17[69] != 1011) throw new Exception("LA17 Pause: " + string.Join(",", n17));
            }
            finally { (SimSec, pauseOff, pausedAt, frozen, frozenTel) = (sim17, po17, pa17, fr17, ft17); }
            // LA18: jet next to two heliports -> the airfield with a runway; in the air no switch for 1 NM, to a field 2 NM closer yes; helicopter keeps the heliport
            var (fs18, all18) = (Fields, Tower.All);
            try
            {
                Airfield Af(string name, double nm, double len) => Airfield.FromDcs(name, fld.X + nm * NM, fld.Z, 20, new[] { ("13", 0.0, fld.X + nm * NM, fld.Z, len) });
                var (kdh, bas) = (Af("Kandahar", 8, 3000), Af("Bastion", -9, 3000));
                Fields = new() { Af("Kandahar Heliport", 1, 25), Af("Camp Bastion Heliport", -1, 30), kdh, bas };
                Tower.All = Fields;
                var jet = new Pilot { Unit = "L18", Callsign = "Enfield 1-1", Gid = 3, Tel = new Telemetry(2000, 1500, 150, 0, fld.X, fld.Z, 0, 0, 0) };
                var j18 = new List<string?> { Pick(jet, null)?.F.Name };
                jet.Active = TowerFor(jet, bas); j18.Add(Pick(jet, null)?.F.Name);   // Kandahar only 1 NM closer: stays
                jet.Tel = jet.Tel with { X = fld.X + 3 * NM };   // Kandahar 5 NM, Bastion 12 NM
                j18.Add(Pick(jet, null)?.F.Name);
                var heli = new Pilot { Unit = "L18h", Callsign = "Dodge 1-1", Gid = 4, Heli = true, Tel = new Telemetry(500, 300, 50, 0, fld.X, fld.Z, 0, 0, 0) };
                j18.Add(Pick(heli, null)?.F.Name);
                if (string.Join(",", j18) != "Kandahar,Bastion,Kandahar,Kandahar Heliport") throw new Exception("LA18 Platzwahl: " + string.Join(",", j18));
            }
            finally { (Fields, Tower.All) = (fs18, all18); }
            // R125: Zulu per map (Nevada UTC-8, Afghanistan UTC+4:30), unknown map without time
            var th0 = Airfield.Theatre;
            try
            {
                var tel = new Telemetry(fld.Elev + 2, 0, 0, 0, fld.X, fld.Z, 0, 0, pa);
                string Z(string th, double clk) { Airfield.Theatre = th; return new Tower(fld, "").AtisText("A", tel, 15, false, 0, 80000, clk); }
                if (!Z("Nevada", 10 * 3600 + 20 * 60).Contains("time one eight two zero Zulu") || !Z("Afghanistan", 10 * 3600).Contains("time zero five three zero Zulu")
                    || !Z("Caucasus", 2 * 3600).Contains("time two two zero zero Zulu") || Z("Atlantis", 36000).Contains("Zulu"))
                    throw new Exception("ATIS R125 Zulu je Karte: " + Z("Nevada", 10 * 3600 + 20 * 60));
            }
            finally { Airfield.Theatre = th0; }
            Console.WriteLine($"OK   ATIS: Windänderung = genau eine Ansage und Kennung {l0}->{l1}, volle Stunde -> {a.Letter} mit Zulu-Zeit, Minuten ändern nichts, Ansage nur an Spieler am Platz");
        }
        finally { (Wx, Sky, Carrier.Clock, Cfg.Atis) = (wx0, sky0, clk0, cfg0); Atis.Clear(); foreach (var x in tel0) Atis[x.Key] = x.Value; }
    }

    /// Alternately transmits on an ATIS frequency the ATIS of airfields where a player is closer than 40 NM (A35: one cycle per frequency, parallel).
    /// N1/A133: only for players for whom the airfield is own or neutral, with the side of the airfield (neutral: once per side of these players).
    /// A35: one own transmitter per ATIS frequency (shared, AtisFreqs, AirfieldFrequencies); ReloadLive starts newly added ones, orphaned ones transmit nothing.
    static readonly HashSet<double> atisOn = new();
    static void StartAtis()
    {
        List<double> fs;
        lock (TowerLock) fs = Cfg.AtisFreqs.Values.Append(Cfg.Frequencies["ATIS"]).Concat(Fields.SelectMany(f => f.Own.GetValueOrDefault("ATIS") ?? Array.Empty<double>())).Where(atisOn.Add).ToList();   // LoadFields/ReloadLive: never twice
        foreach (var af in fs) new Thread(() => AtisLoop(af)) { IsBackground = true }.Start();
    }

    static void AtisLoop(double freq)
    {
        int i = 0;
        while (true)
        {
            Thread.Sleep(3000);
            if (!Cfg.Atis || !On("atc")) continue;
            string name;
            int side;
            AtisInfo? a;
            lock (TowerLock)
            {
                var near = Fields.Where(f => Atis.ContainsKey(f.Name) && FieldFreqs(f, "ATIS")!.Any(x => Math.Abs(x - freq) < 0.0005))
                                 .SelectMany(f => ActivePilots().Where(p => p.Tel != null && Carrier.Deck(p.Tel, p.Coalition) == null && Served(p.Coalition, f) && Dist(p.Tel.X, p.Tel.Z, f.X, f.Z) < 40 * NM)
                                                                .Select(p => (f.Name, Side: SideOf(f, p))).Distinct()).ToList();
                if (near.Count == 0) continue;
                (name, side) = near[i++ % near.Count];
                a = Atis[name];
            }
            if (a.Mp3 == "")
            {
                var mp3 = Path.Combine(Root, "tmp", "atis-" + Regex.Replace(name, "[^A-Za-z]", "") + ".mp3");
                a.Sec = Synth(a.Text, PickVoice(Cfg.Voices.GetValueOrDefault("ATIS", "en_US-ryan-medium@0.94"), name), mp3);   // "a|b" and "random" as for the other stations
                a.Mp3 = mp3;
            }
            var sw = Stopwatch.StartNew();
            Transmit(a.Mp3, freq, name + "ATIS", side: side);
            var rest = a.Sec - sw.Elapsed.TotalSeconds;
            if (rest > 0) Thread.Sleep(TimeSpan.FromSeconds(rest));
        }
    }

}
