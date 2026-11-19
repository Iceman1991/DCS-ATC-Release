using System.Drawing.Imaging;
using System.Text.Json;

namespace DcsAtc;

/// Kneeboard pages (PNG 768×1024) for Saved Games\DCS\Kneeboard or the .miz (KNEEBOARD/IMAGES): every map, in the language of the interface.
static class Kneeboard
{
    const double Ft = 0.3048;
    const int FieldMax = 10, FieldSheets = 3, SheetLines = 40, Cols = 56;   // airfield pages: at most 10 airfields per map, 3 pages each (rest is cut), 40 lines/56 characters per page
    const int FieldPages = 5, NavPages = 3, TableRows = 40;   // ponytail: a table longer than its pages is cut (5 × 40 airfield rows); more pages = raise the count

    /// All file names DCS-ATC may write (Program.WriteKneeboard: the ones not written get a note and go when the mission ends). DCS shows the pages in
    /// name order: a table continues on "ATC-3a2-…", "ATC-3a3-…", which sort between "ATC-3-" and "ATC-3b-".
    public static readonly string[] Names = new[] { "ATC-1-Funk" }.Concat(PageNames("ATC-3-Plaetze", FieldPages)).Append("ATC-3b-Platzrunden").Append("ATC-3c-Routen")
        .Concat(PageNames("ATC-4-Navigation", NavPages)).Append("ATC-5-AWACS").Append("ATC-5b-Funksprueche").Append("ATC-5c-Funksprueche").Append("ATC-6-Hinweise").ToArray();
    /// Airfield pages "ATC-2-<airfield>[_2|_3]" depend on the map and are not in Names: everything of ours in dir that this mission does not write again
    /// (Program.WriteKneeboard) gets the note and goes when the mission ends.
    public static List<string> Owned(string dir) => Names.Select(p => Path.Combine(dir, p + ".png"))
        .Concat(Directory.Exists(dir) ? Directory.GetFiles(dir, "ATC-2-*.png") : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    static IEnumerable<string> PageNames(string name, int n) => Enumerable.Range(1, n).Select(i => i == 1 ? name : name.Insert(name.IndexOf('-', 4), "a" + i));

    /// Writes the pages of this map and returns their files: radio, airfields (frequencies of the mission), one page per charted airfield (ATC-2-*, built in or charts*.jsonc),
    /// patterns/CRPs and named routes of the charted airfields, navaids, AWACS/tanker. The VAD chart notes only on the Caucasus (elsewhere Kutaisi is the placeholder of Airfield.Load).
    public static List<string> Write(string dir, List<Airfield> fields, Config cfg)
    {
        Directory.CreateDirectory(dir);
        bool cauc = Airfield.Theatre == "Caucasus";
        fields = fields.Where(x => x.OnMap).ToList();
        var done = new List<string>();
        void One(string name, string title, string[] lines, bool mono = false) => done.Add(Page(Path.Combine(dir, name + ".png"), title, lines, mono));
        void Table(string name, int n, string title, string[] head, List<string> rows)   // head repeated on every page
        {
            var files = PageNames(name, n).ToArray();
            int pages = Math.Clamp((rows.Count + TableRows - 1) / TableRows, 1, n);
            for (int i = 0; i < pages; i++)
                One(files[i], title + (pages > 1 ? $" {i + 1}/{pages}" : ""), head.Concat(rows.Skip(i * TableRows).Take(TableRows)).ToArray(), true);
        }
        var f = cfg.Frequencies;
        bool known = fields.Any(x => x.HasFreq);   // K1: with Radio.lua one frequency per airfield (list ATC-3), the config values only without
        One("ATC-1-Funk", L("ATC – FUNK", "ATC – RADIO"), (known ? new[]
        {
            L("LOTSEN (je Platz eine Frequenz, AM)", "CONTROLLERS (one frequency per airfield, AM)"),
            L("  Ground / Tower / Approach: Platzfrequenz,", "  Ground / Tower / Approach: airfield frequency,"),
            L("    TWR (VHF, wie Karte) oder UHF, Seite ATC-3", "    TWR (VHF, as on the chart) or UHF, page ATC-3"),
            $"  ATIS {f["ATIS"]:0.000} " + L("(DCS-ATC, nicht auf der Karte)", "(DCS-ATC, not on the chart)"),
        } : new[]
        {
            L("LOTSEN (alle Plätze, AM)", "CONTROLLERS (all airfields, AM)"),
            $"  ATIS      {f["ATIS"]:0.0}   " + L("Wetter, Bahn, Kennung", "weather, runway, letter"),
            $"  Ground    {f["Ground"]:0.0}   " + L("Anlassen, Rollen", "startup, taxi"),
            $"  Tower     {f["Tower"]:0.0}   " + L("Start, Platzrunde, Landung", "takeoff, pattern, landing"),
            $"  Approach  {f["Approach"]:0.0}   " + L("Anflug bis CRP/Initial", "arrival to CRP/initial"),
        }).Concat(new[]
        {
            "",
            L("ANFRAGEN", "REQUESTS"),
            L($"  Sprache: PTT ({Wheel.KeyName(cfg.PttKey)}), englisch", $"  Voice: PTT ({Wheel.KeyName(cfg.PttKey)}), English"),
            L($"  Funkrad: Taste {Wheel.KeyName(cfg.WheelKey)} – 1-9 wählen,", $"  Radio wheel: key {Wheel.KeyName(cfg.WheelKey)} – 1-9 select,"),
            L("     0/Enter = Vorschlag, Rücktaste, Esc", "     0/Enter = suggestion, Backspace, Esc"),
            "  F10 -> DCS-ATC -> Ground / Tower / Approach",
            "",
            L("ABLAUF", "SEQUENCE"),
            "  Ground: request startup",
            "  Ground: request taxi (, departure north)",
            "  -> contact Tower at holding point",
            "  Tower: ready for departure (, closed pattern)",
            "  Approach: inbound for landing",
            "  -> CRP / initial: contact Tower",
            "  Tower: initial / overhead / downwind",
            "  Tower: final, full stop | touch and go |",
            "         low approach",
            "  -> vacate: contact Ground, taxi to parking",
            "",
            L("NOTFALL: mayday -> Vektoren zum nächsten", "EMERGENCY: mayday -> vectors to the nearest"),
            L("  Platz, andere warten; pan pan -> Vorrang.", "  airfield, others wait; pan pan -> priority."),
            L("  Beenden: cancel emergency", "  End it: cancel emergency"),
            L("Falscher Lotse -> \"contact ... on ...\"", "Wrong controller -> \"contact ... on ...\""),
            L("Landebewertung nach jedem Aufsetzen (Text)", "Landing grade after every touchdown (text)"),
        }).ToArray());
        // One page (up to FieldSheets) per charted airfield from the user's own charts files (generated from the chart data), plus Kutaisi on the Caucasus (fields is already
        // filtered by OnMap); the other built-in Caucasus airfields get no page (they would push the later pages back).
        bool OwnChart(Airfield x) => Airfield.OwnChart?.TryGetProperty(Airfield.OwnAlias.GetValueOrDefault(x.Name, x.Name), out _) == true;
        var ids = new HashSet<string>();
        foreach (var x in fields.Where(x => x.Charted && (OwnChart(x) || x.Name == "Kutaisi")).OrderBy(x => OwnChart(x) ? 0 : 1).ThenBy(x => x.Name == "Kutaisi" ? 0 : 1).ThenBy(x => x.Name, StringComparer.Ordinal).Take(FieldMax))
        {
            var id = FileSafe(x.Name);
            if (!ids.Add(id)) ids.Add(id += ids.Count);
            var sheets = FieldLines(x, f);
            for (int i = 0; i < sheets.Count; i++)   // "_2" sorts after "ATC-2-<name>.png" ("-" would sort before the "."); DCS lists the files by name
                One(i == 0 ? "ATC-2-" + id : $"ATC-2-{id}_{i + 1}", Trim(x.Name.ToUpperInvariant(), 22) + (x.Icao != "" ? $" ({x.Icao})" : "") + (sheets.Count > 1 ? $" {i + 1}/{sheets.Count}" : ""), sheets[i], true);
        }
        string Fq(double v) => v > 0 ? $"{v,7:0.000}" : "      -";   // K1: TWR = VHF as on the map, UHF only DCS
        double Band(Airfield x, bool uhf) => x.FreqsOf("Tower")?.FirstOrDefault(v => v >= 200 == uhf) ?? 0;   // own tower frequency (AirfieldFrequencies) instead of the map's
        var rows = fields.OrderBy(x => x.Name).Select(x =>
            $"{Trim(x.Name, 18),-18} {Trim(string.Join(" ", x.Ends.Select(e => e.Name)), 12),-12} {x.Elev / Ft,5:0} {x.PatternFt,5:0} {Fq(Band(x, false))} {Fq(Band(x, true))}").ToList();
        var own = fields.Where(x => x.Own.Count > 0).OrderBy(x => x.Name).ToList();   // Forum request: own frequencies per controller
        if (own.Count > 0) rows.AddRange(new[] { "", L("EIGENE FREQUENZEN (frequencies.jsonc)", "OWN FREQUENCIES (frequencies.jsonc)") });
        rows.AddRange(own.SelectMany(x => x.Own.Select(o => $"  {Trim(x.Name, 18),-18} {o.Key,-9} {string.Join(" / ", o.Value.Select(v => v.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)))}")));
        var kurz = fields.Where(x => x.Callsign != "" && x.Callsign != x.Name).OrderBy(x => x.Name).Select(x => $"  {x.Name,-21} {x.StationOf("Tower")}").ToList();   // K6: call name from Radio.lua
        rows.AddRange(new[]
        {
            "",
            L($"Plätze ohne Verfahren: Einflug über Initial (ca. {Tower.InitialDist / 1852:0.0} NM),", $"Airfields without procedures: entry via initial ({Tower.InitialDist / 1852:0.0} NM),"),
            L("Platzrunde je Bahn links/rechts (Gelände),", "pattern left/right per runway (terrain),"),
            L("Kontrollzone 5 NM bis 3000 ft über Platz:", "control zone 5 NM up to 3000 ft above the field:"),   // = Airfield.CtrNm/CtrTopFt without map
            L("ohne Anruf nicht einfliegen (Durchflug: request", "do not enter without a call (transit: request"),
            "zone transit),",
            L("Rufname \"<Platz> Tower\" usw.", "callsign \"<airfield> Tower\" etc.") + (kurz.Count > 0 ? L(", kurz:", ", short:") : ""),
        });
        rows.AddRange(kurz);
        Table("ATC-3-Plaetze", FieldPages, L("FLUGPLÄTZE ", "AIRFIELDS ") + (cauc ? L("KAUKASUS", "CAUCASUS") : Airfield.Theatre.ToUpperInvariant()),
              new[] { L($"{"PLATZ",-18} {"BAHNEN",-12} {"ELEV",5} {"RUNDE",5} {"TWR",7} {"UHF",7}", $"{"AIRFIELD",-18} {"RUNWAYS",-12} {"ELEV",5} {"PATT",5} {"TWR",7} {"UHF",7}") }, rows);
        if (fields.Any(x => x.Charted)) One("ATC-3b-Platzrunden", L("PLATZRUNDEN UND CRP", "PATTERNS AND CRP"), PatternLines(fields), true);   // PLATZVERFAHREN-PLAN Etappe 5
        if (fields.Any(x => x.Routes.Count + x.Departures.Count > 0)) One("ATC-3c-Routen", L("AN- UND ABFLUGROUTEN", "ARRIVAL AND DEPARTURE ROUTES"), RouteLines(fields), true);
        // Navaids from Beacons.lua: one line per ILS; Russian airfields PRMG/RSBN instead of ILS/TACAN
        var nav = new List<string>();
        foreach (var x in fields.OrderBy(x => x.Name).Where(x => x.Ils.Count > 0 || x.Tacan + x.Prmg + x.Rsbn > 0 || x.Vor > 0))
        {
            var ils = x.Ils.OrderBy(i => i.Key).Select(i => FormattableString.Invariant($"{i.Key} {i.Value.Mhz:0.00} {i.Value.Id}")).ToList();
            if (x.Prmg > 0) ils.Add($"PRMG {x.Prmg}");
            if (ils.Count == 0) ils.Add("-");
            var tac = x.Tacan > 0 ? $"{x.Tacan}X {x.TacanId}" : x.Rsbn > 0 ? $"RSBN {x.Rsbn}" : "-";
            var vor = x.Vor > 0 ? FormattableString.Invariant($"{x.Vor:0.00}") : "-";
            nav.Add($"{Trim(x.Name, 18),-18} {ils[0],-16} {tac,-8} {vor}");
            nav.AddRange(ils.Skip(1).Select(i => $"{"",-18} {i}"));
        }
        var ndbs = fields.OrderBy(x => x.Name).Where(x => x.Ndb.Count > 0).ToList();   // K16: NDB (ident, kHz) one line per airfield
        if (ndbs.Count > 0)
        {
            nav.Add("");
            nav.Add(L("NDB (Kennung kHz, DCS-Zusatz, kein NDB-Anflug)", "NDB (ident kHz, DCS extra, no NDB approach)"));
            nav.AddRange(ndbs.Select(x => $"{Trim(x.Name, 18),-18} {string.Join("  ", x.Ndb.Select(n => $"{n.Id} {n.Khz}"))}"));
        }
        Table("ATC-4-Navigation", NavPages, L("NAVIGATIONSANLAGEN", "NAVAIDS"), new[] { L("PLATZ", "AIRFIELD").PadRight(18) + " ILS              TACAN    VOR" }, nav);
        One("ATC-5-AWACS", "AWACS / TANKER", AwacsLines(cfg));   // N35
        One("ATC-5b-Funksprueche", L("FUNKSPRÜCHE 1/2", "PHRASES 1/2"), PhraseLines(1), true);   // forum: phrases on the kneeboard
        One("ATC-5c-Funksprueche", L("FUNKSPRÜCHE 2/2", "PHRASES 2/2"), PhraseLines(2), true);
        if (cauc) One("ATC-6-Hinweise", L("HINWEISE (KARTE)", "NOTES (CHART)"), NotesLines());   // K27/K40
        Console.WriteLine(L($"Kneeboard-Seiten in {dir}", $"Kneeboard pages in {dir}"));
        return done;
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n];

    /// K37: taxiways apron -> threshold from the data (Airfield.Ramps: To per runway, Tower states the same), identical aprons (North has two airfields) once.
    static IEnumerable<string> RampLines(Airfield k)
    {
        int w = k.Ramps.Select(r => r.Name.Length).DefaultIfEmpty(0).Max();
        return k.Ramps.DistinctBy(r => r.Name).SelectMany(r => r.To.Select((p, i) => $"  {(i == 0 ? r.Name : "").PadRight(w)} -> {p.Key}: {p.Value}"));
    }

    /// File-safe airfield name: ASCII letters and digits only ("Nellis AFB" -> "NellisAFB", "Büchel" -> "Buchel"), so "_" stays free for the continuation pages.
    internal static string FileSafe(string name)
    {
        var s = string.Concat(name.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsAsciiLetterOrDigit));
        return s == "" ? "Airfield" : s;
    }

    /// Lines longer than the page are broken at a space, the rest is indented.
    static IEnumerable<string> Wrap(string s)
    {
        int ind = Math.Min(8, s.Length - s.TrimStart().Length + (s.TrimStart().StartsWith("- ") ? 2 : 4));
        while (s.Length > Cols)
        {
            int cut = s.LastIndexOf(' ', Cols);
            if (cut <= ind) cut = Cols;
            yield return s[..cut].TrimEnd();
            s = new string(' ', ind) + s[cut..].TrimStart();
        }
        yield return s;
    }

    /// Forum (Heling): the page of one charted airfield, the same for every airfield (Kutaisi included) from its data: runways, elevation, navaids, frequencies,
    /// pattern per runway, arrival/departure ceiling, control zone, reporting points with the runways and entry they serve, initial, taxi routes, vacate, chart notes.
    /// Split into pages of SheetLines lines (at most FieldSheets, ponytail: what is longer, the chart notes at the end, is cut).
    internal static List<string[]> FieldLines(Airfield k, Dictionary<string, double> cfgFreq)
    {
        var r = new List<string>();
        var done = new HashSet<string>();
        foreach (var e in k.Ends.Where(e => done.Add(e.Name)))
        {
            var o = k.Ends.FirstOrDefault(y => y.Name != e.Name && y.Name == Airfield.Opposite(e.Name));
            if (o != null) done.Add(o.Name);
            var rw = o == null ? e.Name : e.Name + "/" + o.Name;
            r.Add(L($"Bahn {rw}, {e.Len:0} m", $"Runway {rw}, {e.Len:0} m"));
        }
        r.Add($"Elevation {k.Elev / Ft:0} ft");
        r.AddRange(k.NavText().Replace(", NDB", "\nNDB").Split('\n').Where(l => l != ""));
        r.Add("");
        r.Add(L("FREQUENZEN (AM)", "FREQUENCIES (AM)"));
        string Mhz(double v) => (v >= 200 ? "UHF " : "VHF ") + v.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
        var fr = new[] { "ATIS" }.Concat(Airfield.Roles).Where(x => x != "Departure" || k.Own.ContainsKey(x)).Select(x => (x, v: k.FreqsOf(x))).Where(t => t.v != null)
            .GroupBy(t => string.Join(" / ", t.v!.Select(Mhz))).Select(g => $"  {string.Join("/", g.Select(t => t.x))}: {g.Key}").ToList();
        if (fr.Count == 0)   // no map frequencies: config values, the same for every airfield
            fr.AddRange(new[] { "ATIS", "Ground", "Tower", "Approach" }.Where(cfgFreq.ContainsKey).Select(x => $"  {x}: {cfgFreq[x]:0.0}"));
        r.AddRange(fr);
        r.Add("");
        r.Add(L($"Platzrunde {k.PatternFt:0} ft:", $"Pattern {k.PatternFt:0} ft:"));
        string Cardinal(string s) => Ui.En ? s : s.Replace("north", "Nord").Replace("south", "Süd").Replace("east", "Ost").Replace("west", "West");
        foreach (var (rw, hand) in k.HandOf)
            r.Add("  " + L($"Bahn {rw}: {(hand == "right hand" ? "rechts" : "links")}", $"Runway {rw}: {(hand == "right hand" ? "right" : "left")}") + (k.SideOf.TryGetValue(rw, out var sd) ? ", " + Cardinal(sd) : ""));
        if (k.LandRwy != "" || k.DepRwy != "")
            r.Add("  " + L($"Bevorzugt: Landung {k.LandRwy}, Abflug {k.DepRwy}", $"Preferred: landing {k.LandRwy}, departure {k.DepRwy}"));
        if (k.MaxFt > 0) r.Add(L($"An-/Abflug max. {k.MaxFt:0} ft", $"Arrival/departure max. {k.MaxFt:0} ft"));
        r.Add(L($"Kontrollzone {k.CtrNm:0.0} NM bis {k.CtrTopFt:0} ft", $"Control zone {k.CtrNm:0.0} NM up to {k.CtrTopFt:0} ft"));   // A100/N2
        r.Add(L("  ohne Anruf nicht einfliegen,", "  do not enter without a call,"));
        r.Add(L("  Durchflug: request zone transit", "  transit: request zone transit"));
        r.Add("");
        r.Add(L("PFLICHTMELDEPUNKTE (Einflug)", "COMPULSORY REPORTING POINTS (entry)"));
        int lw = Math.Min(14, k.Crp.Keys.Select(c => Airfield.Label(c).Length).DefaultIfEmpty(0).Max());
        foreach (var c in k.Crp.Keys)
        {
            var via = k.Entry.Where(e => e.Value.Any(x => x.Crp == c)).Select(e => (Rw: e.Key, e.Value.First(x => x.Crp == c).Via)).ToList();
            var when = via.Count == 0 ? (k.Exit.Values.Any(x => x.Contains(c)) || k.ExitOnly.Contains(c) ? L("nur Ausflug", "departures only") : "")
                : via.Count == k.Entry.Count ? L("immer", "always") : L($"nur Bahn {string.Join(" ", via.Select(v => v.Rw))}", $"runway {string.Join(" ", via.Select(v => v.Rw))} only");
            var to = via.Select(v => v.Via).Distinct().Count() == 1 ? "-> " + via[0].Via : string.Join(", ", via.Select(v => $"{v.Rw} -> {v.Via}"));
            r.Add($"  {Airfield.Label(c).PadRight(lw)}  {when}" + (to.Length > 3 && via.Count > 0 ? "  " + to : ""));
        }
        var ini = k.Ends.Where(e => k.InitialNm.ContainsKey(e.Name)).Select(e => (e.Name, Nm: k.InitialNm[e.Name])).ToList();
        if (ini.Count > 0)
            r.Add(ini.Select(i => i.Nm).Distinct().Count() == 1 ? L($"  Initial: ca. {ini[0].Nm:0.0} NM vor der Schwelle", $"  Initial: about {ini[0].Nm:0.0} NM before the threshold")
                : "  Initial (NM): " + string.Join(", ", ini.Select(i => $"{i.Name} {i.Nm:0.0}")));
        if (k.Routes.Count + k.Departures.Count > 0) r.Add(L("  Namen der An-/Abflugrouten: Seite ATC-3c", "  Arrival/departure route names: page ATC-3c"));
        if (k.Ramps.Length > 0)
        {
            r.Add("");
            r.Add(L("ROLLWEGE", "TAXIWAYS"));
            r.AddRange(RampLines(k));
            foreach (var g in k.Ramps.DistinctBy(x => x.Name).SelectMany(x => x.Vacate).GroupBy(p => p.Key))
                r.Add("  " + L("Abrollen ", "Vacate ") + $"{g.Key}: {string.Join("; ", g.Select(p => p.Value).Distinct())}");
        }
        r.Add("");
        r.Add(L("Warteschleife am CRP, wenn die Runde voll ist", "Holding at the CRP when the pattern is full"));
        if (k.Notes.Count > 0)
        {
            r.Add("");
            r.Add(L("HINWEISE (Karte, englisch)", "NOTES (chart)"));
            r.AddRange(k.Notes.Select(n => "- " + n));
        }
        var lines = r.SelectMany(l => Wrap(l)).ToList();
        return Enumerable.Range(0, FieldSheets).Select(i => lines.Skip(i * SheetLines).Take(SheetLines).SkipWhile(l => l == "").ToArray()).Where(x => x.Length > 0).ToList();
    }

    /// N35: AWACS/tanker page only from the config (Write knows no mission data); the sentences are those Ops.AwacsCall understands.
    internal static string[] AwacsLines(Config cfg) => new[]
    {
        L("FREQUENZEN (AM)", "FREQUENCIES (AM)"),
        FormattableString.Invariant($"  AWACS   {cfg.Frequencies.GetValueOrDefault("AWACS", 251.5):0.0}   ") + L("Rufname aus Mission, sonst Overlord", "callsign from mission, else Overlord"),
        "                  " + L("(GCI: Magic blau, Moscow rot)", "(GCI: Magic blue, Moscow red)"),
        FormattableString.Invariant($"  Center  {cfg.Frequencies.GetValueOrDefault("Center", 268.5):0.0}   ") + L("Streckenkontrolle (Name je Karte)", "en-route control (name per map)"),
        FormattableString.Invariant($"  Red Crown {cfg.Frequencies.GetValueOrDefault("RedCrown", 252.5):0.0}   ") + L("Kreuzer der Gruppe (Frequenz aus Mission); Strike = Bordfrequenz", "cruiser of the group (frequency from mission); Strike = ship frequency"),
        FormattableString.Invariant($"  Tanker  {cfg.Frequencies.GetValueOrDefault("Tanker", 255.5):0.0}   ") + L("Rufname aus der Mission (z. B. Texaco)", "callsign from the mission (e.g. Texaco)"),
        L("  Hat der AWACS in der Mission eine eigene", "  If the AWACS has its own frequency in the"),
        L("  Frequenz, gilt diese (Funkrad zeigt sie an).", "  mission, that one applies (the wheel shows it)."),
        L("  Beim Tanker stellt DCS die Frequenz nach dem", "  For the tanker DCS sets the frequency itself"),   // R391: tanker frequency from the mission is not read from the config
        L("  Kontakt selbst ein.", "  after the contact."),
        "",
        L("CHECK-IN (nur mit eigenem AWACS/GCI in der Luft)", "CHECK-IN (only with an AWACS/GCI of your side)"),
        "  \"Overlord, Enfield 1-1, checking in\"",
        L("  -> radar contact, picture. Danach meldet er von", "  -> radar contact, picture. After that it calls"),
        L("     selbst: merged, threat, new group, picture clean", "     by itself: merged, threat, new group, picture clean"),
        L("  Abmelden: \"checking out\"", "  Check out: \"checking out\""),
        "",
        L("ANFRAGEN (PTT oder Funkrad)", "REQUESTS (PTT or radio wheel)"),
        "  picture                 " + L("Gruppen, Bullseye/BRAA", "groups, bullseye/BRAA"),
        "  bogey dope              " + L("nächste Gruppe, BRAA", "nearest group, BRAA"),
        "  declare bullseye 030 45 " + L("Ort prüfen: hostile, bogey,", "check a spot: hostile, bogey,"),
        "                          friendly, clean",
        "  spiked 270              " + L("Gruppe in dieser Peilung", "group on that bearing"),
        "  request sort            " + L("Ziele auf den Flug verteilen", "split targets across the flight"),
        "  nearest tanker          " + L("Peilung, Entfernung, Angels", "bearing, range, angels"),
        "  bingo / vector home     " + L("Kurs zum nächsten Platz", "heading to the nearest field"),
        "",
        L("ANTWORT-STICHWORTE", "REPLY KEYWORDS"),
        "  hostile, bogey, neutral | single, two contacts,",
        "  heavy | hot, flank, beam, drag + " + L("Himmelsrichtung", "compass direction"),
    };

    /// Phrases of tanker, carrier, wingmen (page 1) and JTAC, range (page 2); the words are those of docs/MANUAL.md (Ops.OnTranscript, Carrier, WingCmds).
    internal static string[] PhraseLines(int page) => page == 1 ? new[]
    {
        L("TANKER (Rufname der Mission, z. B. Texaco)", "TANKER (callsign from the mission, e.g. Texaco)"),
        "  request rejoin",
        "  visual               " + L("(ab 8 NM: cleared to join)", "(within 8 NM: cleared to join)"),
        "  observation left wing",
        "  pre-contact          (-> cleared contact)",
        L("  Kontakt und Abreißen meldet DCS-ATC selbst", "  Contact and disconnect are called by DCS-ATC"),
        "  disconnect, right wing",
        "  refuel complete",
        "  say position / altitude / speed / TACAN",
        "",
        L("TRÄGER (Case I/II/III)", "CARRIER (Case I/II/III)"),
        "  Marshal, Enfield 1-1, checking in,",
        "    mother's 360 for 35, angels 18, state 6.2",
        "  see you at angels 2 (I) / angels 6 (II, III)",
        "  commencing, angels 6, state 5.2   (II, III)",
        "  platform | up and on | down and left",
        "  Hornet ball, 5.2",
        "  pigeons | state 4.5",
        "  airborne | on top, angels 8       (" + L("Start", "launch") + ")",
        "",
        L("WINGMEN (Flug-Frequenz, Nummer zuerst)", "WINGMEN (flight frequency, number first)"),
        "  Two, go trail | Flight, engage bandits",
        "  rejoin | cover me | RTB | hold position",
        "  wedge | echelon right | finger four",
        "  radar off | ECM on | smoke on | jettison",
        "  Flight, check in",
    } : new[]
    {
        L("JTAC / FAC(A) (Rufname der Mission, z. B. Axeman)", "JTAC / FAC(A) (callsign from the mission, e.g. Axeman)"),
        "  Axeman, Enfield 1-1, checking in, 4 GBU-12,",
        "    playtime 30 minutes",
        "  ready to copy",
        "  readback, line 4 ..., line 6 ...,",
        "    restrictions final attack heading ...",
        "  request mark | laser on",
        "  tally target",
        "  in from the west     (-> cleared hot)",
        "  bombs away | off",
        "  ready for next target | request re-attack",
        "  say again line 6 | laser code | threats",
        "  checking out",
        "",
        L("RANGE (Zone \"Range…\" in der Mission)", "RANGE (trigger zone \"Range…\" in the mission)"),
        "  Range Alpha, Enfield 1-1, checking in",
        "  IP inbound",
        "  in hot | in dry      " + L("(erst nach cleared hot werfen)", "(release only after cleared hot)"),
        "  off safe | winchester",
        "  checking out",
    };

    /// K27/K40: fixed list of notes from the VADs (restricted areas and neighbor references are not rebuilt, DCS-ATC does not know them), without coordinates.
    internal static string[] NotesLines() => new[]
    {
        L("Nur Hinweis: DCS-ATC kennt diese Flächen nicht und", "Note only: DCS-ATC does not know these areas and"),
        L("warnt nicht davor. Karte (VAD) lesen.", "does not warn about them. Read the chart (VAD)."),
        "",
        L("KREUZSCHRAFFIERTE ORTE (alle Plätze)", "CROSS-HATCHED PLACES (all airfields)"),
        L("  Nicht überfliegen ohne Freigabe des TWR oder", "  Do not overfly without TWR clearance or"),
        L("  Erlaubnis der Militärbehörde.", "  permission of the military authority."),
        L("  Dicht besiedelte Gebiete nicht überfliegen.", "  Do not overfly densely populated areas."),
        L("  HIRTA-Zonen beachten.", "  Mind the HIRTA zones."),
        "",
        "KRYMSK",
        "  UR-R152: 5000 ft / GND, H24",
        "",
        "GELENDZHIK <-> NOVOROSSIYSK",
        L("  Beide VADs verweisen mit Hinweis 3 aufeinander:", "  Both VADs refer to each other with note 3:"),
        L("  Nachbarplatz und seine Zone beachten.", "  mind the neighbouring airfield and its zone."),
        "",
        "SOGANLUG / VAZIANI",
        L("  Lochini-IFR-Sektor (Approach/Departure) liegt als", "  The Lochini IFR sector (Approach/Departure) is a"),
        L("  Band auf der Lochini-Achse zwischen beiden Plätzen,", "  band on the Lochini axis between both airfields,"),
        L("  die CTRs grenzen an die Lochini-CTR. Dort weder", "  the CTRs border the Lochini CTR. No holding and"),
        L("  Warteschleife noch VFR-Ausflug hinein.", "  no VFR departure into it."),
        "",
        "BATUMI",
        L("  Staatsgrenze Georgien/Türkei direkt südlich von", "  State border Georgia/Turkey directly south of"),
        L("  CRP South und an der Südkante der CTR.", "  CRP South and at the southern edge of the CTR."),
        "",
        "SOCHI",
        L("  Staatsgrenze Russland/Georgien östlich des Platzes.", "  State border Russia/Georgia east of the airfield."),
    };

    /// Etappe 5: per charted airfield two lines, pattern altitude and side per runway (R/L), then the CRPs ("-" = departures only).
    internal static string[] PatternLines(IEnumerable<Airfield> fields)
    {
        string Short(string c) => c.Split('-', ' ').Length > 0 && c.Split('-', ' ').All(w => w is "north" or "south" or "east" or "west") ? string.Concat(c.Split('-', ' ').Select(w => char.ToUpperInvariant(w[0]))) : c;
        var rows = new List<string> { L("PLATZ              RUNDE  BAHN (R/L = Rechts/Links)", "AIRFIELD           PATT   RWY (R/L = right/left)"), L("                   CRP (- = nur Ausflug)", "                   CRP (- = departures only)") };
        foreach (var x in fields.Where(x => x.Charted).OrderBy(x => x.Name))
        {
            var rw = string.Join(" ", x.Ends.Where(e => x.HandOf.ContainsKey(e.Name)).Select(e => e.Name + (x.HandOf[e.Name] == "right hand" ? "R" : "L")));
            rows.Add(Trim($"{Trim(x.Name, 18),-18} {x.PatternFt,5:0}  {rw}", 56));
            rows.Add(Trim("    " + string.Join(" ", x.Crp.Keys.Select(c => (x.ExitOnly.Contains(c) ? "-" : "") + Short(c))), 56));
        }
        return rows.ToArray();
    }

    /// Named routes of the airfields (charts.jsonc "routes"/"departures"): name, runways and for arrivals the reporting point at the end; below it the fixes
    /// in order with their crossing altitude ("+" at or above, "-" at or below, plain = at).
    internal static string[] RouteLines(IEnumerable<Airfield> fields)
    {
        var rows = new List<string> { L("+ = oder höher, - = oder tiefer (ft)", "+ = at or above, - = at or below (ft)") };
        foreach (var x in fields.Where(x => x.Routes.Count + x.Departures.Count > 0).OrderBy(x => x.Name))
        {
            rows.Add("");
            rows.Add(x.Name.ToUpperInvariant());
            foreach (var (dep, r) in x.Routes.Values.Select(r => (false, r)).Concat(x.Departures.Values.Select(r => (true, r))))
            {
                rows.Add(Trim($"  {r.Name.Replace('_', ' ')} {(dep ? "departure" : "recovery")}  RWY {string.Join(" ", r.Rwy)}{(dep ? "" : " -> " + Airfield.Label(r.To))}", 56));
                var line = "   ";
                foreach (var p in r.Pts.Select(p => $" {p.Fix.Replace('_', ' ')}{(p.Ft > 0 ? $" {p.Ft:0}{(p.Cross == "between" ? $"-{p.Ft2:0}" : p.Cross == "at or above" ? "+" : p.Cross == "at or below" ? "-" : "")}" : "")},"))
                {
                    if (line.Length > 3 && line.Length + p.Length > 56) { rows.Add(line); line = "   "; }
                    line += p;
                }
                rows.Add(line.TrimEnd(','));
            }
        }
        return rows.ToArray();
    }

    /// Selftest (Programm.RadioTest): taxiways from Airfield.Ramps, NDB in NavText, AWACS page with the config frequency, all pages are written;
    /// another map: no Kutaisi and no chart notes, routes page from the chart, long tables on several pages; English pages.
    internal static void SelfTest()
    {
        var kut = Airfield.Kutaisi();
        var taxi = string.Join("|", RampLines(kut));
        if (!taxi.Contains("Ramp West  -> 07: Alpha|") || !taxi.Contains("-> 25: November, Delta|") || !taxi.Contains("Ramp East  -> 07: Sierra, Whiskey") ||
            taxi.Split("Ramp North").Length != 2)
            throw new Exception("Kneeboard: Rollwege aus Airfield.Ramps: " + taxi);
        var pl = PatternLines(new[] { kut });
        if (pl.Length != 4 || !pl[2].StartsWith("Kutaisi") || !pl[2].Contains("07") || kut.Crp.Keys.Any(c => !pl[3].Contains(c.Split('-', ' ')[0][..1].ToUpperInvariant()) && !pl[3].Contains(c)) || pl.Any(l => l.Length > 56))
            throw new Exception("Kneeboard: Platzrunden-Seite: " + string.Join("|", pl));
        var ndb = new Airfield { Ndb = { ("AP", 443), ("P", 215) } };
        if (ndb.NavText() != "NDB AP 443 / P 215") throw new Exception($"Kneeboard: NDB \"{ndb.NavText()}\"");
        var cfg = new Config(); cfg.Frequencies["AWACS"] = 252.5; cfg.Frequencies["Tanker"] = 256.5;
        var aw = string.Join("\n", AwacsLines(cfg));
        if (!aw.Contains("AWACS   252.5") || !aw.Contains("Tanker  256.5")) throw new Exception("Kneeboard: AWACS-Seite ohne Config-Frequenz: " + aw);
        if (aw.Contains("AWACS/Tanker in der Mission") || !aw.Contains("Beim Tanker stellt DCS die Frequenz")) throw new Exception("Kneeboard: Tanker-Frequenz aus der Mission behauptet (R391): " + aw);
        var nt = NotesLines();
        if (!nt.Any(l => l.Contains("UR-R152")) || !nt.Any(l => l.Contains("Lochini")) || !nt.Any(l => l.StartsWith("BATUMI")) || !nt.Any(l => l.StartsWith("SOCHI")) ||
            nt.Any(l => l.Length > 54))   // wider than the page (Consolas 22 px, 708 px)
            throw new Exception("Kneeboard: Hinweisseite unvollständig oder zu breit: " + string.Join("|", nt));
        if (!Names.OrderBy(n => n + ".png", StringComparer.Ordinal).SequenceEqual(Names) || Names.Distinct().Count() != Names.Length)
            throw new Exception("Kneeboard: Dateinamen nicht in Seitenreihenfolge: " + string.Join(" ", Names));
        var route = new Airfield { Name = "Nellis", Routes = { ["MINTT"] = new("MINTT", new[] { "03L", "03R" }, "CRAIG", new[] { ("MINTT", 0.0, 0.0, 15000.0, "at or above", 0.0), ("GASS_PEAK", 0.0, 0.0, 9000.0, "at or above", 0.0), ("SIMNS", 0.0, 0.0, 5500.0, "at", 0.0) }) },
                                   Departures = { ["FLEX"] = new("FLEX", new[] { "03L" }, "", new[] { ("FLEX", 0.0, 0.0, 5000.0, "at or below", 0.0), ("APEX", 0.0, 0.0, 0.0, "", 0.0) }) } };
        var rl = RouteLines(new[] { kut, route });
        if (rl.Length != 7 || rl[2] != "NELLIS" || rl[3] != "  MINTT recovery  RWY 03L 03R -> CRAIG" || rl[4] != "    MINTT 15000+, GASS PEAK 9000+, SIMNS 5500" || rl[5] != "  FLEX departure  RWY 03L" || rl[6] != "    FLEX 5000-, APEX")
            throw new Exception("Kneeboard: Routen-Seite: " + string.Join("|", rl));
        var tmp = Path.Combine(Path.GetTempPath(), "DcsAtc-KneeboardTest-" + Environment.ProcessId);
        var (th, en, tw) = (Airfield.Theatre, Ui.En, Console.Out);
        try
        {
            Console.SetOut(TextWriter.Null);   // Write reports the folder
            bool Has(List<string> w, string n) => w.Contains(Path.Combine(tmp, n + ".png")) && new FileInfo(Path.Combine(tmp, n + ".png")).Length > 1000;
            Airfield.Theatre = "Caucasus";
            var bat = new Airfield { Name = "Batumi", Crp = { ["north"] = (0, 0) }, Ends = { new("13", 0, 0, 130, 2000, 0, 0) } };   // charted built-in airfield: no page without an own chart
            var w = Write(tmp, new() { kut, bat, ndb }, cfg);
            if (w.Any(p => Path.GetFileName(p).StartsWith("ATC-2-") && !Path.GetFileName(p).StartsWith("ATC-2-Kutaisi")) ||!Has(w, "ATC-2-Kutaisi") ||!Has(w, "ATC-3b-Platzrunden") || !Has(w, "ATC-6-Hinweise") || !Has(w, "ATC-5-AWACS") || Has(w, "ATC-3c-Routen") || Has(w, "ATC-3a2-Plaetze"))
                throw new Exception("Kneeboard: Seiten Kaukasus: " + string.Join(" ", w.Select(Path.GetFileName)));
            var old = new FileInfo(Path.Combine(tmp, "ATC-3-Plaetze.png")).Length;
            Note(new[] { Path.Combine(tmp, "ATC-3-Plaetze.png") });
            if (new FileInfo(Path.Combine(tmp, "ATC-3-Plaetze.png")).Length is var n && (n == old || n < 1000)) throw new Exception("Kneeboard: Hinweis statt alter Seite fehlt");
            (Airfield.Theatre, Ui.En) = ("Nevada", true);
            var many = Enumerable.Range(0, 90).Select(i => new Airfield { Name = "Field " + i }).Prepend(route).Prepend(kut).ToList();
            w = Write(tmp, many, cfg);
            if (Has(w, "ATC-2-Kutaisi") || Has(w, "ATC-6-Hinweise") || !Has(w, "ATC-3c-Routen") || !Has(w, "ATC-3a2-Plaetze") || !Has(w, "ATC-3a3-Plaetze") || Has(w, "ATC-3a4-Plaetze") || !Has(w, "ATC-1-Funk"))
                throw new Exception("Kneeboard: Seiten andere Karte: " + string.Join(" ", w.Select(Path.GetFileName)));
            // Forum (Heling): airfield page from the chart data of any map. Kutaisi first: the generic page has what the old fixed one had
            var kl = string.Join("\n", FieldLines(kut, cfg.Frequencies).SelectMany(s => s));
            if (!kl.Contains("Runway 07/25, 24") || !kl.Contains("CRP west") || !kl.Contains("runway 07 only") || !kl.Contains("CRP east") || !kl.Contains("runway 25 only") || !kl.Contains("-> overhead") || !kl.Contains("Pattern 2000 ft") ||
                !kl.Contains("Runway 07: right, south") || !kl.Contains("Runway 25: left, south") || !kl.Contains("Vacate 25: right via Alpha or Bravo") || !kl.Contains("Ramp West") || !kl.Contains("Control zone"))
                throw new Exception("Kneeboard: Kutaisi-Seite aus den Kartendaten: " + kl);
            var (oc, oa) = (Airfield.OwnChart, Airfield.OwnAlias);
            try
            {
                var ramps = string.Join(",", Enumerable.Range(1, 13).Select(i => $"\"Ramp {i}\": {{ \"pts\": [[{i * 10}, {i * 10}]], \"to\": {{ \"03L\": \"Alpha, Bravo\", \"03R\": \"Alpha\", \"21L\": \"Charlie, Delta, Echo, Foxtrot\", \"21R\": \"Charlie\" }}, \"vacate\": {{ \"03L\": \"left via Hotel\", \"21R\": \"right via Juliet\" }} }}"));
                Airfield.OwnChart = JsonDocument.Parse("{ \"Nellis AFB\": { \"icao\": \"KLSV\", \"pattern_ft\": 3500, \"arrdep_max_ft\": 5000, \"ctr_top_ft\": 6500, \"land_rwy\": \"21R\", \"dep_rwy\": \"21L\", " +
                    "\"crp\": { \"CRAIG\": [1000, 2000], \"ONDAE\": [3000, 4000], \"FLEX\": [5000, 6000], \"L_S_V_Radial_3_4_9\": [7000, 8000] }, " +
                    "\"rwy\": { \"03L\": { \"hand\": \"left\", \"side\": \"west\", \"initial_nm\": 3, \"entry\": { \"CRAIG\": { \"crs\": 120, \"via\": \"overhead\" }, \"ONDAE\": { \"crs\": 180, \"via\": \"initial\" } }, \"exit\": { \"FLEX\": { \"crs\": 28 } } }, " +
                    "\"03R\": { \"hand\": \"right\", \"side\": \"east\", \"initial_nm\": 3.3, \"entry\": { \"CRAIG\": { \"crs\": 120, \"via\": \"overhead\" }, \"ONDAE\": { \"crs\": 180, \"via\": \"initial\" } }, \"exit\": { \"FLEX\": { \"crs\": 28 } } }, " +
                    "\"21L\": { \"hand\": \"left\", \"side\": \"east\", \"initial_nm\": 5, \"entry\": { \"CRAIG\": { \"crs\": 300, \"via\": \"overhead\" } }, \"exit\": { \"FLEX\": { \"crs\": 28 } } } }, " +
                    "\"ground\": { \"ramps\": { " + ramps + " } }, \"notes\": [\"Test note: hold short of runway 21L when the tower tells you to and read back every instruction\"] } }", Airfield.ChartOpts).RootElement;
                Airfield.OwnAlias = new();
                var nel = new Airfield { Name = "Nellis AFB", Id = 7, X = 0, Z = 0, Elev = 570 * Ft, Uhf = 327, Vhf = 132.35, Tacan = 12, TacanId = "LSV", Ils = { ["21R"] = (111.1, "ILSV") },
                    Ends = { new("03L", 0, 0, 30, 3000, 0, 0), new("21R", 0, 0, 210, 3000, 0, 0), new("03R", 0, 0, 30, 3000, 0, 0), new("21L", 0, 0, 210, 3000, 0, 0) } }.ApplyChart();
                var sheets = FieldLines(nel, cfg.Frequencies);
                var nl = string.Join("\n", sheets.SelectMany(s => s));
                var xr = new Airfield { Name = "Xray Field", Ends = { new("09", 0, 0, 90, 1000, 0, 0) } };   // no chart: no page
                var stale = Path.Combine(tmp, "ATC-2-OldField.png"); File.WriteAllBytes(stale, new byte[] { 1 });   // page of another map
                w = Write(tmp, new() { kut, xr, nel }, cfg);
                if (sheets.Count != 3 || !Has(w, "ATC-2-NellisAFB") || !Has(w, "ATC-2-NellisAFB_2") || !Has(w, "ATC-2-NellisAFB_3") || Has(w, "ATC-2-NellisAFB_4") || Has(w, "ATC-2-XrayField") || Has(w, "ATC-2-Kutaisi") ||
                    string.CompareOrdinal("ATC-2-NellisAFB.png", "ATC-2-NellisAFB_2.png") >= 0 || string.CompareOrdinal("ATC-2-NellisAFB_2.png", "ATC-3-Plaetze.png") >= 0 || string.CompareOrdinal("ATC-1-Funk.png", "ATC-2-NellisAFB.png") >= 0)
                    throw new Exception("Kneeboard: Platzseiten aus eigener Karte: " + string.Join(" ", w.Select(Path.GetFileName)) + " Blätter " + sheets.Count);
                foreach (var need in new[] { "Runway 03L/21R, 3000 m", "Runway 03R/21L", "Elevation 570 ft", "TACAN 12X LSV", "ILS 21R 111.10", "Ground/Tower/Approach: UHF 327.0 / VHF 132.35", "Pattern 3500 ft", "Runway 03R: right, east",
                    "Preferred: landing 21R, departure 21L", "max. 5000 ft", "Control zone", "6500 ft", "CRP", "CRAIG", "always  -> overhead", "runway 03L 03R only  -> initial", "departures only", "Radial 3 4 9", "Initial (NM): 03L 3.0, 03R 3.3, 21L 5.0",
                    "TAXIWAYS", "Ramp 1 ", "Ramp 13", "-> 21L: Charlie, Delta, Echo, Foxtrot", "Vacate 03L: left via Hotel", "Vacate 21R: right via Juliet", "NOTES (chart)", "- Test note" })
                    if (!nl.Contains(need)) throw new Exception($"Kneeboard: Platzseite aus eigener Karte ohne \"{need}\": " + nl);
                if (sheets.SelectMany(s => s).Any(l => l.Length > Cols) || sheets.Any(s => s.Length > SheetLines)) throw new Exception("Kneeboard: Platzseite zu breit oder zu lang: " + nl);
                var gone = Owned(tmp).Except(w, StringComparer.OrdinalIgnoreCase).ToList();   // what Program.WriteKneeboard notes and deletes at the end
                if (!gone.Contains(stale) || gone.Any(g => g.Contains("NellisAFB")) || !gone.Any(g => g.EndsWith("ATC-6-Hinweise.png"))) throw new Exception("Kneeboard: alte Platzseite anderer Karte nicht zum Ersetzen/Entfernen: " + string.Join(" ", gone.Select(Path.GetFileName)));
                Note(gone);
                if (new FileInfo(stale).Length < 1000) throw new Exception("Kneeboard: alte Platzseite nicht durch den Hinweis ersetzt");
                if (Environment.GetEnvironmentVariable("DCSATC_KNEEBOARD_PREVIEW") is { Length: > 0 } pv) Write(pv, new() { nel }, cfg);   // pages to look at
                // Caucasus with an own chart for Batumi: Batumi and Kutaisi, nothing else
                Airfield.Theatre = "Caucasus";
                Airfield.OwnChart = JsonDocument.Parse("{ \"Batumi\": { } }").RootElement;
                w = Write(tmp, new() { kut, bat, ndb }, cfg);
                if (w.Any(p => Path.GetFileName(p).StartsWith("ATC-2-") && !Path.GetFileName(p).StartsWith("ATC-2-Kutaisi") && !Path.GetFileName(p).StartsWith("ATC-2-Batumi")) || !Has(w, "ATC-2-Batumi") || !Has(w, "ATC-2-Kutaisi"))
                    throw new Exception("Kneeboard: Kaukasus mit eigener Karte für Batumi: " + string.Join(" ", w.Select(Path.GetFileName)));
                Airfield.Theatre = "Nevada";
            }
            finally { (Airfield.OwnChart, Airfield.OwnAlias) = (oc, oa); }
                foreach (var en2 in new[] { false, true })   // phrases pages: written, block headings, nothing wider than the page (Consolas 21 px, 56 chars)
            {
                Ui.En = en2;
                var ph1 = PhraseLines(1); var ph2 = PhraseLines(2);
                if (!Has(w, "ATC-5b-Funksprueche") || !Has(w, "ATC-5c-Funksprueche") || !ph1.Any(l => l.StartsWith("TANKER")) || !ph1.Any(l => l.StartsWith("CARRIER") || l.StartsWith("TRÄGER")) ||
                    !ph1.Any(l => l.StartsWith("WINGMEN")) || !ph2.Any(l => l.StartsWith("JTAC")) || !ph2.Any(l => l.StartsWith("RANGE")) || ph1.Concat(ph2).Any(l => l.Length > 56))
                    throw new Exception("Kneeboard: Funkspruch-Seiten: " + string.Join("|", ph1.Concat(ph2).Where(l => l.Length > 56)));
            }
            Ui.En = true;
            var awEn = AwacsLines(cfg);
            if (!awEn[0].StartsWith("FREQUENCIES") || awEn.Concat(NotesLines()).Any(l => l.Contains("Rufname") || l.Contains("Platz")) || NotesLines().Any(l => l.Length > 54) || RouteLines(many)[0] != "+ = at or above, - = at or below (ft)")
                throw new Exception("Kneeboard: englische Seiten: " + string.Join("|", awEn));
        }
        finally
        {
            Console.SetOut(tw);
            (Airfield.Theatre, Ui.En) = (th, en);
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
        Console.WriteLine("OK   Kneeboard: Rollwege aus Airfield.Ramps, NDB in NavText, AWACS-Seite mit Config-Frequenz, Hinweisseite, Hinweis statt alter Seite; andere Karte ohne Kutaisi, Routen-Seite, Plätze auf 3 Seiten, englisch; Platzseite je Platz mit Karte (Kutaisi und eigene Karte, 2. und 3. Seite, Breite, alte Seite wird ersetzt)");
    }

    /// Pages DCS already listed that this mission does not use (another map, pages switched off): a note in place of the old content.
    public static void Note(IEnumerable<string> files)
    {
        foreach (var f in files) Page(f, "DCS-ATC", new[] { "Diese Seite wird in dieser Mission nicht", "benutzt.", "", "This page is not used in this mission." });
    }

    static string Page(string file, string title, string[] lines, bool mono = false)
    {
        using var bmp = new Bitmap(768, 1024);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.FromArgb(250, 248, 240));
        using var head = new Font("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var body = mono ? new Font("Consolas", 21, GraphicsUnit.Pixel) : new Font("Consolas", 22, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(20, 20, 30));
        using var line = new Pen(Color.FromArgb(20, 20, 30), 2);
        g.DrawString(title, head, ink, 30, 24);
        g.DrawLine(line, 30, 66, 738, 66);
        float y = 84, step = Math.Min(34, (1024 - 100f) / Math.Max(1, lines.Length));
        foreach (var l in lines) { g.DrawString(l, body, ink, 30, y); y += step; }
        bmp.Save(file, ImageFormat.Png);
        return file;
    }
}
