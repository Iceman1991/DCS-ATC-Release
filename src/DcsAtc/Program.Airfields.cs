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
    // ------------------------------------------------------------ Airfields (data from the mission script, Kutaisi from the map)
    static readonly string AirfieldFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Airfields.txt");
    static DateTime airfieldStamp;
    static readonly string StandFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Stands.txt");   // stand names (DcsAtcHook.lua)
    static DateTime standStamp;
    /// Test modes (--selftest, --anflugtest, --mptest, --asr-test, --srs-test): only the repo airfields.txt. Otherwise a test read the airfield data of the
    /// running mission (Persian Gulf instead of Caucasus, different results depending on the last mission) and thereby overwrote the checked-in airfields.txt.
    internal static bool TestRun;
    static bool MissionFields => !TestRun && File.Exists(AirfieldFile);
    static string? FieldsFile;   // --replay: airfields.txt of the reporter's map next to his log

    /// #28: charts.jsonc name -> DCS airfield of this map: same name (case, spaces, hyphens irrelevant), else the only airfield whose name contains it or is contained in it
    /// ("Nellis AFB" = "Nellis"). Result DCS name -> charts.jsonc name; notes: renamed, ambiguous, not on this map (for the log).
    internal static Dictionary<string, string> ChartAlias(IEnumerable<string> keys, IEnumerable<string> names, List<string> notes)
    {
        // #28 (log): "Buechel AB" is the DCS airfield "Buchel": ü/ue, ö/oe, ä/ae and ß/ss count as u, o, a, s on both sides
        static string N(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant().Replace("ü", "u").Replace("ö", "o").Replace("ä", "a").Replace("ß", "s"), "([uoa])e", "$1").Replace("ss", "s"), "[^a-z0-9]", "");
        var (map, all, off) = (new Dictionary<string, string>(), names.Distinct().ToList(), new List<string>());
        foreach (var k in keys)
        {
            var hit = all.Where(n => N(n) == N(k)).ToList();
            if (hit.Count == 0) hit = all.Where(n => N(n).Length >= 4 && N(k).Length >= 4 && (N(n).Contains(N(k)) || N(k).Contains(N(n)))).ToList();
            if (hit.Count == 1) { map[hit[0]] = k; if (hit[0] != k) notes.Add(L($"[Karten] charts.jsonc: \"{k}\" = DCS-Platz \"{hit[0]}\"", $"[Charts] charts.jsonc: \"{k}\" = DCS airfield \"{hit[0]}\"")); }
            else if (hit.Count > 1) notes.Add(L($"[Karten] charts.jsonc: \"{k}\" nicht eindeutig ({string.Join(", ", hit)}), Namen wie in DCS schreiben – ignoriert",
                                                $"[Charts] charts.jsonc: \"{k}\" is ambiguous ({string.Join(", ", hit)}), write the name as in DCS – ignored"));
            else off.Add(k);
        }
        if (off.Count > 0) notes.Add(L($"[Karten] charts.jsonc: nicht auf dieser Karte ({Airfield.Theatre}): {string.Join(", ", off)}", $"[Charts] charts.jsonc: not on this map ({Airfield.Theatre}): {string.Join(", ", off)}"));
        return map;
    }

    /// Forum 0.15: own procedures in charts.jsonc (DCS-ATC folder), format like the built-in charts/caucasus.jsonc; an airfield in there replaces the built-in entry.
    /// Faulty file: message, the built-in data stay.
    internal static JsonElement? OwnChart(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var own = JsonDocument.Parse(File.ReadAllText(path), Airfield.ChartOpts).RootElement;
            Airfield.OwnChart = own;
            foreach (var a in own.EnumerateObject()) new Airfield { Name = a.Name }.ApplyChart();   // checks every entry once
            Log(L($"[Karten] eigene Verfahren aus charts.jsonc: {string.Join(", ", own.EnumerateObject().Select(a => a.Name))}", $"[Charts] own procedures from charts.jsonc: {string.Join(", ", own.EnumerateObject().Select(a => a.Name))}"));
            return own;
        }
        catch (Exception e)
        {
            Log(L($"[Karten] charts.jsonc fehlerhaft, es gelten die eingebauten Verfahren: {e.Message}", $"[Charts] charts.jsonc is faulty, the built-in procedures apply: {e.Message}"));
            return null;
        }
        finally { Airfield.OwnChart = null; }
    }

    static readonly string[] BoardPages = { "ATC-1-Funk", "ATC-2-Kutaisi", "ATC-3-Plaetze", "ATC-3b-Platzrunden", "ATC-4-Navigation", "ATC-5-AWACS", "ATC-6-Hinweise" };

    /// Forum 0.15: kneeboard pages into Saved Games\DCS\Kneeboard (shown in every aircraft) on a new mission, with its frequencies. The pages exist only
    /// for Caucasus and in German. DCS reads the page list when the mission loads, before the mission data reach us: pages deleted now show
    /// MISSINGTEXTURE, pages written now only appear from the next mission. So the files stay; another map gets a note in them (the next Caucasus
    /// mission lists them again). English or KneeboardPages off: note now, deleted when the mission ends (app exit).
    static void WriteKneeboard()
    {
        var dir = Path.Combine(SavedGamesDcs(), "Kneeboard");
        try
        {
            dropBoard = !Cfg.KneeboardPages || En;
            if (!dropBoard && Airfield.Theatre == "Caucasus" && Fields.Any(f => f.Name == "Kutaisi")) Kneeboard.Write(dir, Fields, Cfg);
            else Kneeboard.Note(BoardPages.Select(p => Path.Combine(dir, p + ".png")).Where(File.Exists));
        }
        catch (Exception e) { Log(L($"[Kneeboard] Seiten nicht geschrieben: {e.Message}", $"[Kneeboard] pages not written: {e.Message}")); }
    }
    static bool dropBoard;

    static void DeleteBoardPages()
    {
        try { foreach (var p in BoardPages) File.Delete(Path.Combine(SavedGamesDcs(), "Kneeboard", p + ".png")); }
        catch (Exception) { }
    }

    static void LoadFields()
    {
        var copy = Path.Combine(Root, "airfields.txt");   // last known data, in case DCS has not run yet
        if (MissionFields)
        {
            airfieldStamp = File.GetLastWriteTime(AirfieldFile);
            try { File.Copy(AirfieldFile, copy, true); } catch (IOException) { }
        }
        Tower.Freqs = Cfg.Frequencies;
        var file = FieldsFile ?? (MissionFields ? AirfieldFile : copy);
        if (File.Exists(file) && File.ReadLines(file).FirstOrDefault() is { } m && m.StartsWith("M;")) Airfield.Theatre = m[2..];   // charts.jsonc check with the projection of this map
        if (!TestRun) Airfield.OwnChart = OwnChart(Path.Combine(Root, "charts.jsonc"));
        if (Airfield.OwnChart is { } oc && File.Exists(file))   // #28: charts.jsonc names like "Nellis AFB" for the DCS airfield "Nellis"
        {
            var notes = new List<string>();
            Airfield.OwnAlias = ChartAlias(oc.EnumerateObject().Select(a => a.Name), File.ReadLines(file).Where(l => l.StartsWith("A;")).Select(l => l.Split(';')[1]), notes);
            foreach (var n in notes) Log(n);
        }
        Fields = Airfield.Load(file);
        standStamp = default;   // new airfield objects: stand names again (TickAll)
        Tower.All = Fields;
        Ops.MagVar = MapMagVar(Fields);
        var dcs = DcsDir();
        if (!DcsExe(dcs))
            Log(L("[DCS] DCS-Installation nicht gefunden: Frequenzen aus Ersatzdaten", "[DCS] DCS installation not found: frequencies from fallback data"));
        Airfield.LoadRadio(Fields, dcs);
        Airfield.LoadBeacons(Fields, dcs);
        Rollnetz.LoadAll(Fields, dcs);
        SetNotams();
        if (!TestRun) { WriteFreqFile(FreqPath); ApplyOwnFreqs(); }   // new map: extend frequencies.jsonc, own frequencies (also ATIS transmitters) of these airfields
        if (!TestRun && !Dedicated) WriteKneeboard();
        foreach (var p in Pilots.Values.Append(Solo)) { p.Towers.Clear(); p.Active = null; p.Pinned = null; p.OnBoat = null; }
        Console.WriteLine(L("Plätze", "Airfields") + $" ({Fields.Count}): {string.Join(", ", Fields.Select(f => f.Name))}");
        foreach (var c in FreqClashes(Fields, Cfg.Frequencies))   // also old config.jsonc (the installer does not overwrite it)
            Console.WriteLine(L($"⚠ config.jsonc: {c} – Anrufe an den Platz gehen dorthin, Frequenz ändern", $"⚠ config.jsonc: {c} – calls to the airfield go there, change the frequency"));
        foreach (var c in AtisClashes(Fields, Cfg.AtisFreqs, Cfg.Frequencies))   // A35
            Console.WriteLine(L($"⚠ config.jsonc: {c} – Frequenz ändern", $"⚠ config.jsonc: {c} – change the frequency"));
    }

    /// R15: magnetic variation of the map for Ops (bearings mag): mean of the airfields (Kutaisi appears on other maps only as a placeholder), Caucasus fixed 6 (FromDcs).
    internal static double MapMagVar(IEnumerable<Airfield> fields) =>
        Math.Round(fields.Where(f => f.Name != "Kutaisi" || Airfield.Theatre == "Caucasus").Select(f => f.MagVar).DefaultIfEmpty(6).Average());

    /// Forum request: apply frequencies.jsonc (OwnFreqs) to the airfields (Airfield.OwnChart). Airfield name as in DCS or Radio.lua name (RadioName), case, hyphen/space irrelevant, a unique partial name suffices;
    /// per controller (ATIS, Ground, Tower, Departure, Approach) one frequency or a list (UHF, VHF), 30–400 MHz. Invalid: entry dropped, one line per error (return value, for the log);
    /// duplicate controller frequency (two airfields, also the map frequency of another): stays, one warning line.
    internal static List<string> ApplyFreqs(List<Airfield> fields, Dictionary<string, JsonElement>? cfg)
    {
        var bad = new List<string>();
        // #28 (log): "Buechel AB" is the DCS airfield "Buchel": ü/ue, ö/oe, ä/ae and ß/ss count as u, o, a, s on both sides
        static string N(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant().Replace("ü", "u").Replace("ö", "o").Replace("ä", "a").Replace("ß", "s"), "([uoa])e", "$1").Replace("ss", "s"), "[^a-z0-9]", "");
        foreach (var f in fields) f.Own.Clear();
        Carrier.StationFreqs.Clear();
        OwnSectors = new();
        foreach (var (name, roles) in cfg ?? new())
        {
            if (N(name) == "centersectors")   // #18: Center/GCI sectors as in config.jsonc, in addition to those
            {
                List<CenterSector>? cs = null;
                try { cs = roles.Deserialize<List<CenterSector>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch (JsonException) { }
                foreach (var s in cs ?? new())
                    if (s.Name.Trim() != "" && s.Freq is >= 30 and <= 400) OwnSectors.Add(s);
                    else bad.Add(L($"[ATC] frequencies.jsonc: CenterSectors \"{s.Name}\" – {(s.Name.Trim() == "" ? "ohne \"Name\"" : "keine Frequenz 30–400 MHz")}, ignoriert",
                                   $"[ATC] frequencies.jsonc: CenterSectors \"{s.Name}\" – {(s.Name.Trim() == "" ? "without \"Name\"" : "no frequency 30–400 MHz")}, ignored"));
                if (cs == null) bad.Add(L("[ATC] frequencies.jsonc: CenterSectors ist keine Liste [ { \"Name\": …, \"Freq\": … } ] – ignoriert", "[ATC] frequencies.jsonc: CenterSectors is not a list [ { \"Name\": …, \"Freq\": … } ] – ignored"));
                continue;
            }
            if (N(name) == "carrier" && roles.ValueKind == JsonValueKind.Object)   // #13: own frequency per carrier station (all carriers)
            {
                foreach (var x in roles.EnumerateObject())
                {
                    var st = Carrier.Stations.FirstOrDefault(s => s.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
                    var mhz = x.Value.ValueKind == JsonValueKind.Number ? x.Value.GetDouble() : x.Value.ValueKind == JsonValueKind.Array && x.Value.GetArrayLength() > 0 && x.Value[0].ValueKind == JsonValueKind.Number ? x.Value[0].GetDouble() : 0;
                    if (st != null && mhz is >= 30 and <= 400) Carrier.StationFreqs[st] = mhz;
                    else bad.Add(L($"[ATC] frequencies.jsonc: Carrier \"{x.Name}\": {x.Value} – {(st == null ? "unbekannte Station (" + string.Join(", ", Carrier.Stations) + ")" : "keine Frequenz 30–400 MHz")}, ignoriert",
                                   $"[ATC] frequencies.jsonc: Carrier \"{x.Name}\": {x.Value} – {(st == null ? "unknown station (" + string.Join(", ", Carrier.Stations) + ")" : "no frequency 30–400 MHz")}, ignored"));
                }
                continue;
            }
            var hit = fields.Where(f => N(f.Name) == N(name) || f.RadioName != "" && N(f.RadioName) == N(name)).ToList();   // RadioName: generated line without mission data
            if (hit.Count == 0 && N(name) != "") hit = fields.Where(f => N(f.Name).Contains(N(name))).ToList();
            if (hit.Count != 1 || roles.ValueKind != JsonValueKind.Object)
            {
                bad.Add(L($"[ATC] frequencies.jsonc: Platz \"{name}\" {(hit.Count == 0 ? "nicht auf dieser Karte" : hit.Count > 1 ? "nicht eindeutig (" + string.Join(", ", hit.Select(f => f.Name)) + ")" : "ohne { \"Tower\": … }")} – ignoriert",
                          $"[ATC] frequencies.jsonc: airfield \"{name}\" {(hit.Count == 0 ? "not on this map" : hit.Count > 1 ? "ambiguous (" + string.Join(", ", hit.Select(f => f.Name)) + ")" : "without { \"Tower\": … }")} – ignored"));
                continue;
            }
            foreach (var (role, v) in roles.EnumerateObject().Select(x => (x.Name, x.Value)))
            {
                var r = Airfield.Roles.Append("ATIS").FirstOrDefault(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));
                var mhz = v.ValueKind == JsonValueKind.Number ? new[] { v.GetDouble() }
                        : v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number) ? v.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null;
                if (r != null && mhz is { Length: > 0 } && mhz.All(x => x is >= 30 and <= 400)) { hit[0].Own[r] = mhz; continue; }
                bad.Add(L($"[ATC] frequencies.jsonc: {hit[0].Name} \"{role}\": {v} – {(r == null ? "unbekannter Lotse (ATIS, Ground, Tower, Departure, Approach)" : "keine Frequenz 30–400 MHz")}, ignoriert",
                          $"[ATC] frequencies.jsonc: {hit[0].Name} \"{role}\": {v} – {(r == null ? "unknown controller (ATIS, Ground, Tower, Departure, Approach)" : "no frequency 30–400 MHz")}, ignored"));
            }
        }
        Tower.CenterSectors = Cfg.CenterSectors.Concat(OwnSectors).ToList();
        // Own controller frequency at two airfields or = map frequency of another: FreqRoute takes the first airfield with own frequencies, the others cannot be reached on it (warning only)
        var order = fields.OrderBy(f => f.Own.Count == 0).ToList();
        foreach (var x in fields.SelectMany(f => f.Own.Where(o => o.Key != "ATIS").SelectMany(o => o.Value)).Distinct())
            if (order.Where(g => g.FreqsOf("*")?.Any(y => Math.Abs(y - x) < 0.01) == true).Select(g => g.Name).ToList() is { Count: > 1 } on)
            {
                var (mhz, rest) = (x.ToString("0.0##", CultureInfo.InvariantCulture), string.Join(", ", on.Skip(1)));
                bad.Add(L($"[ATC] frequencies.jsonc: {mhz} MHz bei {string.Join(" und ", on)} – ein Ruf darauf erreicht nur {on[0]}, {rest} auf dieser Frequenz nicht erreichbar",
                          $"[ATC] frequencies.jsonc: {mhz} MHz at {string.Join(" and ", on)} – a call on it reaches only {on[0]}, {rest} not reachable on this frequency"));
            }
        return bad;
    }

    // ------------------------------------------------------------ frequencies.jsonc: own airfield frequencies (server setting, separate from config.jsonc)
    internal static string FreqPath => Path.Combine(Root, "frequencies.jsonc");
    static DateTime freqAt;   // State of frequencies.jsonc (live reload)
    static List<CenterSector> OwnSectors = new();   // #18: "CenterSectors" from frequencies.jsonc (this map), added to config.jsonc

    /// Read frequencies.jsonc and apply to the map's airfields (under TowerLock or when loading the airfields), update ATIS transmitters. File currently locked: next second.
    static void ApplyOwnFreqs()
    {
        string text;
        try { text = File.Exists(FreqPath) ? File.ReadAllText(FreqPath) : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        freqAt = File.GetLastWriteTimeUtc(FreqPath);
        var (own, err) = OwnFreqs(text, Airfield.Theatre, Path.GetFileName(Airfield.TerrainDir(DcsDir(), Airfield.Theatre)));   // #19: heading = folder of the map (Sinai for SinaiMap)
        if (err != null) Log(err);
        foreach (var w in ApplyFreqs(Fields, own)) Log(w);
        if (atisOn.Count > 0) StartAtis();
    }

    /// Entries of frequencies.jsonc (comments, trailing comma allowed): under "// ===== Karte =====" only for this map, without a section for all. Invalid: none, with message.
    internal static (Dictionary<string, JsonElement> Own, string? Error) OwnFreqs(string text, params string[] map)
    {
        var own = new Dictionary<string, JsonElement>();
        if (text.Trim() == "") return (own, null);
        var heads = Regex.Matches(text, @"^[ \t]*// ===== (.+?) =====", RegexOptions.Multiline)
                         .Select(m => (At: (long)System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, m.Index)), Map: m.Groups[1].Value)).ToList();
        var r = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(text), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) throw new JsonException("{ … }");
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                var (name, at) = (r.GetString()!, r.TokenStartIndex);
                r.Read();
                var v = JsonElement.ParseValue(ref r);
                var sec = heads.LastOrDefault(h => h.At < at).Map;
                if (sec == null || map.Contains(sec, StringComparer.OrdinalIgnoreCase)) own[name] = v;
                else if (name.Equals("Carrier", StringComparison.OrdinalIgnoreCase)) own.TryAdd(name, v);   // #18: carrier stations on every map, also under an own heading
            }
        }
        catch (JsonException e)
        {
            return (new(), L($"[ATC] frequencies.jsonc: Fehler ({e.Message}) – keine eigenen Frequenzen, Beispiele oben in der Datei", $"[ATC] frequencies.jsonc: error ({e.Message}) – no own frequencies, see the examples at the top of the file"));
        }
        return (own, null);
    }

    /// Create or extend frequencies.jsonc: all installed maps (Radio.lua, also server installation), one commented-out line per airfield with today's values.
    static void WriteFreqFile(string path)
    {
        try
        {
            var maps = new SortedDictionary<string, Dictionary<int, (double Uhf, double Vhf, string Cs, string Name)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in DcsDirs().Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!.Trim()).Where(d => Directory.Exists(Path.Combine(d, "Mods", "terrains"))))
                foreach (var m in Directory.GetDirectories(Path.Combine(d, "Mods", "terrains")).Select(x => Path.GetFileName(x)))
                    if (!maps.ContainsKey(m) && Airfield.RadioTable(d, m) is { Count: > 0 } t) maps[m] = t;
            var old = File.Exists(path) ? File.ReadAllText(path) : null;
            var text = FreqFile(old, maps, Path.GetFileName(Airfield.TerrainDir(DcsDir(), Airfield.Theatre)), Fields.Where(f => f.Id > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First().Name));
            if (text != old) File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log($"[ATC] frequencies.jsonc: {e.Message}"); }
    }

    static readonly string FreqHeader = """
        // DCS-ATC: own airfield frequencies / eigene Platzfrequenzen
        // EN: Remove the // in front of an airfield line and change the numbers; the lines show what DCS-ATC uses today (DCS map, ATIS and config.jsonc).
        //     Name as in DCS (upper/lower case, hyphens, spaces do not matter); controllers ATIS, Ground, Tower, Departure, Approach; one frequency in MHz or a list [UHF, VHF].
        //     Lines under "// ===== Map =====" apply on that map only. On a server they apply to all players. Applied right after saving; errors are listed in atc-log.txt.
        //     New maps and airfields are added as commented lines, your lines are never changed.
        // DE: Vor einer Platzzeile die // entfernen und die Zahlen ändern; die Zeilen zeigen, was DCS-ATC heute nutzt (DCS-Karte, ATIS und config.jsonc).
        //     Name wie in DCS (Groß/Klein, Bindestrich, Leerzeichen egal); Lotsen ATIS, Ground, Tower, Departure, Approach; eine Frequenz in MHz oder eine Liste [UHF, VHF].
        //     Zeilen unter "// ===== Karte =====" gelten nur auf dieser Karte. Auf einem Server gelten sie für alle Spieler. Gilt sofort nach dem Speichern; Fehler stehen in atc-log.txt.
        //     Neue Karten und Plätze werden als auskommentierte Zeilen ergänzt, deine Zeilen bleiben unverändert.
        {
        }
        """;

    /// #18: commented examples for carrier stations, Red Crown and Center/GCI sectors, after the opening {
    static readonly string[] FreqExamples =
    {
        "  // EN: Carrier stations (all carriers) and Red Crown: remove the // and keep only the stations you need. Works under any heading.",
        "  // DE: Trägerstationen (alle Träger) und Red Crown: // entfernen, nur die gewünschten Stationen behalten. Gilt unter jeder Überschrift.",
        "  // \"Carrier\": { \"Marshal\": 264.0, \"Approach\": 265.0, \"Tower\": 266.0, \"Paddles\": 267.0, \"Departure\": 268.0, \"Strike\": 269.0, \"RedCrown\": 252.5 },",
        "  // EN: Own Center and GCI/CRC (same as config.jsonc CenterSectors): Lat/Lon of the centre, nearest centre within RadiusNm wins, Side blue/red/empty,",
        "  //     \"Gci\": true = radar controller like the AWACS instead of a Center. Above all map headings it applies on every map, under \"// ===== Map =====\" on that map only.",
        "  // DE: Eigenes Center und GCI/CRC (wie CenterSectors in config.jsonc): Lat/Lon der Mitte, die nächste Mitte im RadiusNm gewinnt, Side blue/red/leer,",
        "  //     \"Gci\": true = Radarlotse wie das AWACS statt Center. Über allen Karten-Überschriften gilt es auf jeder Karte, unter \"// ===== Karte =====\" nur dort.",
        "  // \"CenterSectors\": [ { \"Name\": \"Langen Radar\", \"Freq\": 270.0, \"Lat\": 50.03, \"Lon\": 8.57, \"RadiusNm\": 120, \"Side\": \"blue\" },",
        "  //                    { \"Name\": \"Loneship\", \"Freq\": 276.75, \"Lat\": 50.58, \"Lon\": 8.16, \"RadiusNm\": 150, \"Side\": \"blue\", \"Gci\": true } ],",
        "",
    };

    /// Content of frequencies.jsonc: add missing maps (section) and airfields (line with "// id N"), change nothing else; only an unchanged generated line
    /// of the running map gets the exact DCS name from the mission (exact), Radio.lua does not always know it.
    internal static string FreqFile(string? old, IDictionary<string, Dictionary<int, (double Uhf, double Vhf, string Cs, string Name)>> maps, string theatre, Dictionary<int, string> exact)
    {
        string F(double x) => x.ToString("0.0##", CultureInfo.InvariantCulture);
        string Line(string map, int id, string? name = null)
        {
            var r = maps[map][id];
            name ??= map.Equals(theatre, StringComparison.OrdinalIgnoreCase) && exact.TryGetValue(id, out var n) ? n : r.Name;
            string Lf(string role) => r.Uhf > 0 || r.Vhf > 0 ? $"[{string.Join(", ", new[] { r.Uhf, r.Vhf }.Where(x => x > 0).Select(F))}]" : F(Cfg.Frequencies[role]);   // without airfield frequency: config per controller (Airfield.FreqsOf)
            return $"  // \"{name}\": {{ \"ATIS\": {F(AtisFreq(name))}, \"Ground\": {Lf("Ground")}, \"Tower\": {Lf("Tower")}, \"Departure\": {Lf("Approach")}, \"Approach\": {Lf("Approach")} }},   // id {id}";
        }
        var lines = (old ?? FreqHeader).Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
        if (!lines.Any(l => l.Contains("\"CenterSectors\"")) && lines.FindIndex(l => l.Trim() == "{") is >= 0 and var o) lines.InsertRange(o + 1, FreqExamples);   // #18: also into older files
        bool Head(string l) => Regex.IsMatch(l, @"^\s*// ===== .+ =====");
        int Close() => lines.FindLastIndex(l => l.TrimStart().StartsWith('}')) is >= 0 and var c ? c : lines.Count;
        foreach (var (map, ids) in maps)
        {
            int h = lines.FindIndex(l => l.Trim().Equals($"// ===== {map} =====", StringComparison.OrdinalIgnoreCase));
            if (h < 0)
            {
                int c = Close();
                lines.InsertRange(c, (c > 0 && lines[c - 1].Trim() is "{" or "" ? Array.Empty<string>() : new[] { "" }).Append($"  // ===== {map} =====")
                                     .Concat(ids.OrderBy(x => Line(map, x.Key)).Select(x => Line(map, x.Key))).Append(""));
                continue;
            }
            int end = lines.FindIndex(h + 1, Head) is >= 0 and var e ? e : Close();
            var have = new HashSet<int>();
            for (int i = h + 1; i < end; i++)
                if (Regex.Match(lines[i], @"//\s*id\s+(\d+)\s*$") is { Success: true } m && int.TryParse(m.Groups[1].Value, out var id))
                {
                    have.Add(id);
                    if (maps[map].ContainsKey(id) && lines[i] == Line(map, id, maps[map][id].Name)) lines[i] = Line(map, id);   // generated unchanged: exact name
                }
            while (end - 1 > h && lines[end - 1].Trim() == "") end--;
            lines.InsertRange(end, ids.Keys.Where(id => !have.Contains(id)).OrderBy(id => Line(map, id)).Select(id => Line(map, id)));
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// A35: ATIS frequency of the airfield (AtisFreqs), otherwise the shared one.
    static double AtisFreq(string field) => Cfg.AtisFreqs.GetValueOrDefault(field, Cfg.Frequencies["ATIS"]);

    /// A35: own ATIS frequency on an airfield frequency or on a function frequency (range, AWACS, tanker, carrier, Ground/Tower/Approach).
    internal static IEnumerable<string> AtisClashes(IEnumerable<Airfield> fields, Dictionary<string, double> atis, Dictionary<string, double> freqs) =>
        FreqClashes(fields, atis.ToDictionary(kv => "ATIS " + kv.Key, kv => kv.Value)).Concat(
            from kv in atis from r in freqs where r.Key != "ATIS" && Math.Abs(kv.Value - r.Value) < 0.005
            select FormattableString.Invariant($"ATIS {kv.Key} {kv.Value:0.0##} = {r.Key}"));

    /// K3: function frequency on an airfield frequency ("AWACS 251.0 = Krasnodar-Center UHF"): OnSrs checks the config first, the airfield never answers.
    /// Not Ground/Tower/Approach, there the airfield frequency wins.
    internal static IEnumerable<string> FreqClashes(IEnumerable<Airfield> fields, Dictionary<string, double> freqs) =>
        from kv in freqs where kv.Key is not ("Ground" or "Tower" or "Approach")
        from f in fields
        from b in new[] { ("UHF", f.Uhf), ("VHF", f.Vhf) }
        where b.Item2 > 0 && Math.Abs(kv.Value - b.Item2) < 0.005
        select FormattableString.Invariant($"{kv.Key} {kv.Value:0.0##} = {f.Name} {b.Item1}");

    static Tower TowerFor(Pilot p, Airfield f)
    {
        if (!p.Towers.TryGetValue(f.Name, out var t)) p.Towers[f.Name] = t = new Tower(f, p.Callsign);
        return t;
    }

    /// LoGetAngleOfAttack: per docs rad, in the game (Hornet) degrees -> unambiguous on approach (about 8°): above 0.6 (34° in rad) it is degrees.
    static double AoaDeg(double v) => Math.Abs(v) > 0.6 ? v : v * 180 / Math.PI;

    /// Radio wheel "Marshal check in" as in reality: position to the carrier (mag), angels, fuel in 1000 lbs.
    static string? CheckIn(Pilot p)
    {
        if (p.Tel is not { } t || Carrier.Boats.Where(b => b.Coalition == p.Coalition).MinBy(b => Dist(t.X, t.Z, b.X, b.Z)) is not { } b) return null;
        int brg = (int)Math.Round((Math.Atan2(t.Z - b.Z, t.X - b.X) * 180 / Math.PI - Ops.MagVar + 720) % 360);
        double lbs = Carrier.CvType(p.Type).Lb;   // internal fuel only for carrier types, otherwise without state
        return FormattableString.Invariant($"checking in, mother's {(brg == 0 ? 360 : brg):000} for {Dist(t.X, t.Z, b.X, b.Z) / NM:0}, angels {t.AltMsl / 0.3048 / 1000:0}")
               + (lbs > 0 ? FormattableString.Invariant($", state {p.Fuel * lbs / 1000:0.0}") : "");
    }

    /// R28: ball call as in reality with type and fuel in 1000 lb ("Hornet ball, 5.2"), type without fuel figure without fuel.
    static string BallCall(Pilot p, string w)
    {
        var (kind, lb) = Carrier.CvType(p.Type);
        return $"{kind} {w}" + (lb > 0 ? FormattableString.Invariant($", {p.Fuel * lb / 1000:0.0}") : "");
    }
    /// R28: pilot voice at the carrier: station only on check-in, otherwise call sign first ("Enfield 1 1, Hornet ball, 5.2.").
    static string CarrierCall(string station, string callsign, string spoken) =>
        $"{(spoken.StartsWith("checking ") ? station + ", " : "")}{callsign.Replace('-', ' ')}, {spoken}.";

    /// Radio wheel "Platz wählen": the 8 nearest airfields and own carrier.
    static string[] NearPlaces(Telemetry? t, int coalition)
    {
        double D(double x, double z) => t == null ? 0 : Dist(t.X, t.Z, x, z);
        return Carrier.Boats.Where(b => b.Coalition == coalition).Select(b => (b.Unit, D: D(b.X, b.Z)))
                     .Concat(Fields.Where(f => Served(coalition, f)).Select(f => (Unit: f.Name, D: D(f.X, f.Z)))).OrderBy(x => x.D).Take(8).Select(x => x.Unit).ToArray();   // N1: without enemy airfields
    }

    /// Airfield for the radio call: the called (Called), otherwise the selected, the tuned (SRS: the current, otherwise the next), the current, the nearest.
    /// Without via (radio wheel, F10, microphone) and every second (TickAll): the radio wheel title and Untuned thus show the airfield the wheel is talking to.
    /// N1: own and neutral airfields only; none (e.g. all red for Blue) -> null.
    static Tower? Pick(Pilot p, string? text)
    {
        var tel = p.Tel;
        double D(Airfield f) => tel == null ? 0 : Dist(tel.X, tel.Z, f.X, f.Z);
        bool Ok(Airfield f) => Served(p.Coalition, f);
        var fq = Srs?.TunedFreqs(p.Id);
        bool Has(double[]? fs, double[] ms) => fs != null && fs.Any(x => ms.Any(m => Math.Abs(m - x) < 0.005));
        var fa = fq?.Where(m => SrsRole(null, m) is not ("AWACS" or "Tanker" or "Range" or "Carrier" or "JTAC" or "Center") || p.SharedPick is { Field: true } sp && Math.Abs(sp.Mhz - m) < 0.01).ToArray();   // AWACS/tanker/carrier/Center on the other radio is no airfield, even on an airfield frequency (Krasnodar 251.0 = AWACS 251.0), unless the airfield was called there; R304: the DCS tanker frequency (TankFq) still counts
        bool On(Airfield f) => fa != null && Has(FieldFreqs(f, "*"), fa);
        // SRS does not know the selected radio: a newly tuned airfield wins over the running flow on the other radio
        // R304: within 3 s of the DCS tanker menu, DCS (Easy Comms) itself tunes the tanker's mission frequency (log: 255.5 -> 261.0 = also Senaki UHF): no new airfield, our tanker talks there too
        if (fq != null && p.LastFq is { } lf && !fq.SequenceEqual(lf))
            if (Now() - p.TankMenuAt < 3) p.TankFq = fq.Except(lf).FirstOrDefault(p.TankFq);
            else p.Fresh = Fields.Where(f => Ok(f) && On(f) && !Has(FieldFreqs(f, "*"), lf)).MinBy(D) ?? p.Fresh;
        p.LastFq = fq ?? p.LastFq;
        if (text != null && Called(text, tel, null) is { } cf && Ok(cf)) { p.Fresh = null; return TowerFor(p, cf); }
        if (p.PinFreqs != null && fq != null && !fq.SequenceEqual(p.PinFreqs) && !(p.Pinned != null && On(p.Pinned.F))) (p.Pinned, p.PinFreqs) = (null, null);   // chosen by call: tuned away from the airfield -> the frequency applies again
        if (p.Active is { Released: true } ra) { ra.Released = false; if (p.Pinned == ra) (p.Pinned, p.PinFreqs) = (null, null); }   // R47: signed off -> pin released
        if (p.Pinned != null && Ok(p.Pinned.F)) return p.Pinned;
        // SRS lacks the selected radio (PlayerRadioInfoBase without "selected"): several tuned -> running flow, otherwise the next one
        // Logged out/parked does not run (otherwise the start airfield on radio 2 would stay forever); R47: Departure/Flight Following keeps the flight until logout, at most 25 NM
        var run = p.Active != null && Ok(p.Active.F) && (tel == null || p.Active.Phase is not (Phase.Parked or Phase.Away) || p.Active.Phase == Phase.Away && (p.Active.Radar && D(p.Active.F) < 25 * NM || p.Active.Center));   // Center: en route, no distance limit
        var tuned = Fields.Where(f => Ok(f) && On(f)).ToList();
        if (tuned.Count > 0) return p.Fresh is { } fr && tuned.Contains(fr) ? TowerFor(p, fr) : run && tuned.Contains(p.Active!.F) ? p.Active! : TowerFor(p, Near(tuned)!);
        if (run) return p.Active!;
        return Near(Fields.Where(Ok)) is { } nf ? TowerFor(p, nf) : null;

        // LA18 (log: F-14 "now Kandahar Heliport", Nevada flight switching Creech/Groom Lake/Lincoln County/Mesquite every minute): fixed-wing only airfields
        // with a runway of at least 800 m (none: all); in the air the nearest changes only once the new one is 2 NM closer
        Airfield? Near(IEnumerable<Airfield> fs)
        {
            var c = fs.Where(f => p.Heli || f.Ends.Any(e => e.Len >= 800)).ToList() is { Count: > 0 } l ? l : fs.ToList();
            var (best, cur) = (c.MinBy(D), p.Active?.F);
            if (best == null || cur == null || !c.Contains(cur) || tel == null || Tower.OnGround(tel)) return best;
            return D(best) > D(cur) - 2 * NM ? cur : best;
        }
    }

    /// Called airfield: name word at the start of the call or before ground/tower/approach/departure/radar ("Kutaisi Tower, …", "Enfield 1-1, Kutaisi Tower"),
    /// not "clearance to Batumi" or "contact Kutaisi Approach" (also "contact Ras Al Khaimah Intl Approach": up to three name words before it).
    /// Same number of name words: own (airfield frequency), otherwise the nearest. null = none.
    static Airfield? Called(string text, Telemetry? tel, Airfield? own)
    {
        if (Tower.IsCenterCall(text)) return null;   // "Tbilisi Control, …" is the Center, not the airfield Tbilisi
        var n = Regex.Replace(text.ToLowerInvariant(), "[^a-z ]", " ");
        return Tower.ByName(Fields.OrderBy(f => f != own).ThenBy(f => tel == null ? 0 : Dist(tel.X, tel.Z, f.X, f.Z)),
            w => Regex.IsMatch(n, $@"^\W*(?!contact\b)(?:\w+\W+)?{w}\b|(?<!\bcontact\W+(?:\w+\W+){{0,3}})\b{w}\W+(?:ground|tower|approach|departure|radar)\b"));
    }

}
