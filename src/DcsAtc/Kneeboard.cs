using System.Drawing.Imaging;

namespace DcsAtc;

/// Kneeboard pages (PNG 768×1024) for Saved Games\DCS\Kneeboard or the .miz (KNEEBOARD/IMAGES): every map, in the language of the interface.
static class Kneeboard
{
    const double Ft = 0.3048;
    const int FieldPages = 5, NavPages = 3, TableRows = 40;   // ponytail: a table longer than its pages is cut (5 × 40 airfield rows); more pages = raise the count

    /// All file names DCS-ATC may write (Program.WriteKneeboard: the ones not written get a note and go when the mission ends). DCS shows the pages in
    /// name order: a table continues on "ATC-3a2-…", "ATC-3a3-…", which sort between "ATC-3-" and "ATC-3b-".
    public static readonly string[] Names = new[] { "ATC-1-Funk", "ATC-2-Kutaisi" }.Concat(PageNames("ATC-3-Plaetze", FieldPages)).Append("ATC-3b-Platzrunden").Append("ATC-3c-Routen")
        .Concat(PageNames("ATC-4-Navigation", NavPages)).Append("ATC-5-AWACS").Append("ATC-6-Hinweise").ToArray();
    static IEnumerable<string> PageNames(string name, int n) => Enumerable.Range(1, n).Select(i => i == 1 ? name : name.Insert(name.IndexOf('-', 4), "a" + i));

    /// Writes the pages of this map and returns their files: radio, airfields (frequencies of the mission), patterns/CRPs and named routes of the charted
    /// airfields (built in or charts.jsonc), navaids, AWACS/tanker. Kutaisi and the chart notes only on the Caucasus (elsewhere Kutaisi is the placeholder of Airfield.Load).
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
            "  F10 -> ATC -> Ground / Tower / Approach",
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
        if (cauc && fields.FirstOrDefault(x => x.Name == "Kutaisi") is { } k)
            One("ATC-2-Kutaisi", "KUTAISI (UGKO)", new[]
            {
                L($"Bahn 07/25, 2500 m, Elevation {k.Elev / Ft:0} ft", $"Runway 07/25, 2500 m, elevation {k.Elev / Ft:0} ft"),
            }.Concat(k.NavText().Replace(", NDB", "\nNDB").Split('\n')).Concat(new[]   // NDB on its own line (otherwise wider than the page)
            {
                L($"Platzrunde {k.PatternFt:0} ft, IMMER SÜDLICH:", $"Pattern {k.PatternFt:0} ft, ALWAYS SOUTH:"),
                L("  Bahn 07 rechts, Bahn 25 links", "  runway 07 right, runway 25 left"),
                L($"An-/Abflug max. {k.MaxFt:0} ft", $"Arrival/departure max. {k.MaxFt:0} ft"),
                L($"Kontrollzone {k.CtrNm:0.0} NM bis {k.CtrTopFt:0} ft", $"Control zone {k.CtrNm:0.0} NM up to {k.CtrTopFt:0} ft"),   // A100/N2
                L("  ohne Anruf nicht einfliegen,", "  do not enter without a call,"),
                L("  Durchflug: request zone transit", "  transit: request zone transit"),
                "",
                L("PFLICHTMELDEPUNKTE (Einflug)", "COMPULSORY REPORTING POINTS (entry)"),
                L("  CRP North  immer   -> overhead", "  CRP North  always  -> overhead"),
                L("  CRP South  immer   -> initial", "  CRP South  always  -> initial"),
                L("  CRP West   nur Bahn 07", "  CRP West   runway 07 only"),
                L("  CRP East   nur Bahn 25", "  CRP East   runway 25 only"),
                L($"  Initial: ca. {Tower.Initial(k, "07") / 1852:0.0} NM vor der Schwelle", $"  Initial: about {Tower.Initial(k, "07") / 1852:0.0} NM before the threshold"),
                "",
                L("ROLLWEGE", "TAXIWAYS"),
            }).Concat(RampLines(k)).Concat(new[]
            {
                L("  Abrollen 25: Alpha/Bravo (N), Whiskey (S)", "  Vacate 25: Alpha/Bravo (N), Whiskey (S)"),
                L("  Abrollen 07: Charlie/Delta (N), Echo (S)", "  Vacate 07: Charlie/Delta (N), Echo (S)"),
                "",
                L("Warteschleife am CRP, wenn die Runde voll ist", "Holding at the CRP when the pattern is full"),
            }).ToArray());
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
                foreach (var p in r.Pts.Select(p => $" {p.Fix.Replace('_', ' ')}{(p.Ft > 0 ? $" {p.Ft:0}{(p.Cross == "at or above" ? "+" : p.Cross == "at or below" ? "-" : "")}" : "")},"))
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
        var route = new Airfield { Name = "Nellis", Routes = { ["MINTT"] = new("MINTT", new[] { "03L", "03R" }, "CRAIG", new[] { ("MINTT", 0.0, 0.0, 15000.0, "at or above"), ("GASS_PEAK", 0.0, 0.0, 9000.0, "at or above"), ("SIMNS", 0.0, 0.0, 5500.0, "at") }) },
                                   Departures = { ["FLEX"] = new("FLEX", new[] { "03L" }, "", new[] { ("FLEX", 0.0, 0.0, 5000.0, "at or below"), ("APEX", 0.0, 0.0, 0.0, "") }) } };
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
            var w = Write(tmp, new() { kut, ndb }, cfg);
            if (!Has(w, "ATC-2-Kutaisi") || !Has(w, "ATC-3b-Platzrunden") || !Has(w, "ATC-6-Hinweise") || !Has(w, "ATC-5-AWACS") || Has(w, "ATC-3c-Routen") || Has(w, "ATC-3a2-Plaetze"))
                throw new Exception("Kneeboard: Seiten Kaukasus: " + string.Join(" ", w.Select(Path.GetFileName)));
            var old = new FileInfo(Path.Combine(tmp, "ATC-3-Plaetze.png")).Length;
            Note(new[] { Path.Combine(tmp, "ATC-3-Plaetze.png") });
            if (new FileInfo(Path.Combine(tmp, "ATC-3-Plaetze.png")).Length is var n && (n == old || n < 1000)) throw new Exception("Kneeboard: Hinweis statt alter Seite fehlt");
            (Airfield.Theatre, Ui.En) = ("Nevada", true);
            var many = Enumerable.Range(0, 90).Select(i => new Airfield { Name = "Field " + i }).Prepend(route).Prepend(kut).ToList();
            w = Write(tmp, many, cfg);
            if (Has(w, "ATC-2-Kutaisi") || Has(w, "ATC-6-Hinweise") || !Has(w, "ATC-3c-Routen") || !Has(w, "ATC-3a2-Plaetze") || !Has(w, "ATC-3a3-Plaetze") || Has(w, "ATC-3a4-Plaetze") || !Has(w, "ATC-1-Funk"))
                throw new Exception("Kneeboard: Seiten andere Karte: " + string.Join(" ", w.Select(Path.GetFileName)));
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
        Console.WriteLine("OK   Kneeboard: Rollwege aus Airfield.Ramps, NDB in NavText, AWACS-Seite mit Config-Frequenz, Hinweisseite, Hinweis statt alter Seite; andere Karte ohne Kutaisi, Routen-Seite, Plätze auf 3 Seiten, englisch");
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
