using System.Drawing.Imaging;

namespace DcsAtc;

/// Kneeboard pages (PNG 768×1024) for the .miz (KNEEBOARD/IMAGES) – multiplayer clients then have them too.
static class Kneeboard
{
    const double Ft = 0.3048;

    public static void Write(string dir, List<Airfield> fields, Config cfg)
    {
        Directory.CreateDirectory(dir);
        var f = cfg.Frequencies;
        bool known = fields.Any(x => x.HasFreq);   // K1: with Radio.lua one frequency per airfield (list ATC-3), the config values only without
        Page(Path.Combine(dir, "ATC-1-Funk.png"), "ATC – FUNK", (known ? new[]
        {
            "LOTSEN (je Platz eine Frequenz, AM)",
            "  Ground / Tower / Approach: Platzfrequenz,",
            "    TWR (VHF, wie Karte) oder UHF, Seite ATC-3",
            $"  ATIS {f["ATIS"]:0.000} (DCS-ATC, nicht auf der Karte)",
        } : new[]
        {
            "LOTSEN (alle Plätze, AM)",
            $"  ATIS      {f["ATIS"]:0.0}   Wetter, Bahn, Kennung",
            $"  Ground    {f["Ground"]:0.0}   Anlassen, Rollen",
            $"  Tower     {f["Tower"]:0.0}   Start, Platzrunde, Landung",
            $"  Approach  {f["Approach"]:0.0}   Anflug bis CRP/Initial",
        }).Concat(new[]
        {
            "",
            "ANFRAGEN",
            $"  Sprache: PTT ({Wheel.KeyName(cfg.PttKey)}), englisch",
            $"  Funkrad: Taste {Wheel.KeyName(cfg.WheelKey)} – 1-9 wählen,",
            "     0/Enter = Vorschlag, Rücktaste, Esc",
            "  F10 -> ATC -> Ground / Tower / Approach",
            "",
            "ABLAUF",
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
            "NOTFALL: mayday -> Vektoren zum nächsten",
            "  Platz, andere warten; pan pan -> Vorrang.",
            "  Beenden: cancel emergency",
            "Falscher Lotse -> \"contact ... on ...\"",
            "Landebewertung nach jedem Aufsetzen (Text)",
        }).ToArray());
        var k = fields.First(x => x.Name == "Kutaisi");
        Page(Path.Combine(dir, "ATC-2-Kutaisi.png"), "KUTAISI (UGKO)", new[]
        {
            $"Bahn 07/25, 2500 m, Elevation {k.Elev / Ft:0} ft",
        }.Concat(k.NavText().Replace(", NDB", "\nNDB").Split('\n')).Concat(new[]   // NDB on its own line (otherwise wider than the page)
        {
            $"Platzrunde {k.PatternFt:0} ft, IMMER SÜDLICH:",
            "  Bahn 07 rechts, Bahn 25 links",
            $"An-/Abflug max. {k.MaxFt:0} ft",
            $"Kontrollzone {k.CtrNm:0.0} NM bis {k.CtrTopFt:0} ft",   // A100/N2
            "  ohne Anruf nicht einfliegen,",
            "  Durchflug: request zone transit",
            "",
            "PFLICHTMELDEPUNKTE (Einflug)",
            "  CRP North  immer   -> overhead",
            "  CRP South  immer   -> initial",
            "  CRP West   nur Bahn 07",
            "  CRP East   nur Bahn 25",
            $"  Initial: ca. {Tower.InitialDist / 1852:0.0} NM vor der Schwelle",
            "",
            "ROLLWEGE",
        }).Concat(RampLines(k)).Concat(new[]
        {
            "  Abrollen 25: Alpha/Bravo (N), Whiskey (S)",
            "  Abrollen 07: Charlie/Delta (N), Echo (S)",
            "",
            "Warteschleife am CRP, wenn die Runde voll ist",
        }).ToArray());
        string Fq(double v) => v > 0 ? $"{v,7:0.000}" : "      -";   // K1: TWR = VHF as on the map, UHF only DCS
        var rows = new List<string> { $"{"PLATZ",-18} {"BAHNEN",-12} {"ELEV",5} {"RUNDE",5} {"TWR",7} {"UHF",7}" };
        double Band(Airfield x, bool uhf) => x.FreqsOf("Tower")?.FirstOrDefault(v => v >= 200 == uhf) ?? 0;   // own tower frequency (AirfieldFrequencies) instead of the map's
        rows.AddRange(fields.OrderBy(x => x.Name).Select(x =>
            $"{Trim(x.Name, 18),-18} {Trim(string.Join(" ", x.Ends.Select(e => e.Name)), 12),-12} {x.Elev / Ft,5:0} {x.PatternFt,5:0} {Fq(Band(x, false))} {Fq(Band(x, true))}"));
        var own = fields.Where(x => x.Own.Count > 0).OrderBy(x => x.Name).ToList();   // Forum request: own frequencies per controller
        if (own.Count > 0) rows.AddRange(new[] { "", "EIGENE FREQUENZEN (frequencies.jsonc)" });
        rows.AddRange(own.SelectMany(x => x.Own.Select(o => $"  {Trim(x.Name, 18),-18} {o.Key,-9} {string.Join(" / ", o.Value.Select(v => v.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)))}")));
        rows.Add("");
        rows.Add($"Andere Plätze: Einflug über Initial (ca. {Tower.InitialDist / 1852:0.0} NM),");
        var kurz = fields.Where(x => x.Callsign != "" && x.Callsign != x.Name).OrderBy(x => x.Name).Select(x => $"  {x.Name,-21} {x.StationOf("Tower")}").ToList();   // K6: call name from Radio.lua
        rows.Add("Platzrunde je Bahn links/rechts (Gelände),");
        rows.Add("Kontrollzone 5 NM bis 3000 ft über Platz:");   // = Airfield.CtrNm/CtrTopFt without map
        rows.Add("ohne Anruf nicht einfliegen (Durchflug: request");
        rows.Add("zone transit),");
        rows.Add("Rufname \"<Platz> Tower\" usw." + (kurz.Count > 0 ? ", kurz:" : ""));
        rows.AddRange(kurz);
        Page(Path.Combine(dir, "ATC-3-Plaetze.png"), "FLUGPLÄTZE KAUKASUS", rows.ToArray(), mono: true);
        // Navaids from Beacons.lua: one line per ILS; Russian airfields PRMG/RSBN instead of ILS/TACAN
        var nav = new List<string> { "PLATZ              ILS              TACAN    VOR" };
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
            nav.Add("NDB (Kennung kHz, DCS-Zusatz, kein NDB-Anflug)");
            nav.AddRange(ndbs.Select(x => $"{Trim(x.Name, 18),-18} {string.Join("  ", x.Ndb.Select(n => $"{n.Id} {n.Khz}"))}"));
        }
        Page(Path.Combine(dir, "ATC-4-Navigation.png"), "NAVIGATIONSANLAGEN", nav.ToArray(), mono: true);
        Page(Path.Combine(dir, "ATC-5-AWACS.png"), "AWACS / TANKER", AwacsLines(cfg));   // N35
        Page(Path.Combine(dir, "ATC-6-Hinweise.png"), "HINWEISE (KARTE)", NotesLines());   // K27/K40
        Console.WriteLine($"Kneeboard-Seiten in {dir}");
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n];

    /// K37: taxiways apron -> threshold from the data (Airfield.Ramps: To07/To25, Tower states the same), identical aprons (North has two airfields) once.
    static IEnumerable<string> RampLines(Airfield k)
    {
        int w = k.Ramps.Select(r => r.Name.Length).DefaultIfEmpty(0).Max();
        return k.Ramps.DistinctBy(r => (r.Name, r.To07, r.To25)).SelectMany(r => new[]
        {
            $"  {r.Name.PadRight(w)} -> 07: {r.To07}",
            $"  {"".PadRight(w)} -> 25: {r.To25}",
        });
    }

    /// N35: AWACS/tanker page only from the config (Write knows no mission data); the sentences are those Ops.AwacsCall understands.
    internal static string[] AwacsLines(Config cfg) => new[]
    {
        "FREQUENZEN (AM)",
        FormattableString.Invariant($"  AWACS   {cfg.Frequencies.GetValueOrDefault("AWACS", 251.5):0.0}   Rufname aus Mission, sonst Overlord"),
        "                  (GCI: Magic blau, Moscow rot)",
        FormattableString.Invariant($"  Tanker  {cfg.Frequencies.GetValueOrDefault("Tanker", 255.5):0.0}   Rufname aus der Mission (z. B. Texaco)"),
        "  Hat der AWACS in der Mission eine eigene",
        "  Frequenz, gilt diese (Funkrad zeigt sie an).",
        "  Beim Tanker stellt DCS die Frequenz nach dem",   // R391: tanker frequency from the mission is not read from the config
        "  Kontakt selbst ein.",
        "",
        "CHECK-IN (nur mit eigenem AWACS/GCI in der Luft)",
        "  \"Overlord, Enfield 1-1, checking in\"",
        "  -> radar contact, picture. Danach meldet er von",
        "     selbst: merged, threat, new group, picture clean",
        "  Abmelden: \"checking out\"",
        "",
        "ANFRAGEN (PTT oder Funkrad)",
        "  picture                 Gruppen, Bullseye/BRAA",
        "  bogey dope              nächste Gruppe, BRAA",
        "  declare bullseye 030 45 Ort prüfen: hostile, bogey,",
        "                          friendly, clean",
        "  spiked 270              Gruppe in dieser Peilung",
        "  request sort            Ziele auf den Flug verteilen",
        "  nearest tanker          Peilung, Entfernung, Angels",
        "  bingo / vector home     Kurs zum nächsten Platz",
        "",
        "ANTWORT-STICHWORTE",
        "  hostile, bogey, neutral | single, two contacts,",
        "  heavy | hot, flank, beam, drag + Himmelsrichtung",
    };

    /// K27/K40: fixed list of notes from the VADs (restricted areas and neighbor references are not rebuilt, DCS-ATC does not know them), without coordinates.
    internal static string[] NotesLines() => new[]
    {
        "Nur Hinweis: DCS-ATC kennt diese Flächen nicht und",
        "warnt nicht davor. Karte (VAD) lesen.",
        "",
        "KREUZSCHRAFFIERTE ORTE (alle Plätze)",
        "  Nicht überfliegen ohne Freigabe des TWR oder",
        "  Erlaubnis der Militärbehörde.",
        "  Dicht besiedelte Gebiete nicht überfliegen.",
        "  HIRTA-Zonen beachten.",
        "",
        "KRYMSK",
        "  UR-R152: 5000 ft / GND, H24",
        "",
        "GELENDZHIK <-> NOVOROSSIYSK",
        "  Beide VADs verweisen mit Hinweis 3 aufeinander:",
        "  Nachbarplatz und seine Zone beachten.",
        "",
        "SOGANLUG / VAZIANI",
        "  Lochini-IFR-Sektor (Approach/Departure) liegt als",
        "  Band auf der Lochini-Achse zwischen beiden Plätzen,",
        "  die CTRs grenzen an die Lochini-CTR. Dort weder",
        "  Warteschleife noch VFR-Ausflug hinein.",
        "",
        "BATUMI",
        "  Staatsgrenze Georgien/Türkei direkt südlich von",
        "  CRP South und an der Südkante der CTR.",
        "",
        "SOCHI",
        "  Staatsgrenze Russland/Georgien östlich des Platzes.",
    };

    /// Selftest (Programm.RadioTest): taxiways from Airfield.Ramps, NDB in NavText, AWACS page with the config frequency, all pages are written.
    internal static void SelfTest()
    {
        var kut = Airfield.Kutaisi();
        var taxi = string.Join("|", RampLines(kut));
        if (!taxi.Contains("Ramp West  -> 07: Alpha|") || !taxi.Contains("-> 25: November, Delta|") || !taxi.Contains("Ramp East  -> 07: Echo, Sierra, Whiskey") ||
            taxi.Split("Ramp North").Length != 2)
            throw new Exception("Kneeboard: Rollwege aus Airfield.Ramps: " + taxi);
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
        var tmp = Path.Combine(Path.GetTempPath(), "DcsAtc-KneeboardTest-" + Environment.ProcessId);
        try
        {
            var tw = Console.Out;
            Console.SetOut(TextWriter.Null);   // Write reports the folder
            try { Write(tmp, new() { kut, ndb }, cfg); } finally { Console.SetOut(tw); }
            if (!File.Exists(Path.Combine(tmp, "ATC-6-Hinweise.png"))) throw new Exception("Kneeboard: ATC-6-Hinweise.png fehlt");
            if (!File.Exists(Path.Combine(tmp, "ATC-5-AWACS.png")) || new FileInfo(Path.Combine(tmp, "ATC-5-AWACS.png")).Length < 1000)
                throw new Exception("Kneeboard: ATC-5-AWACS.png fehlt");
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
        Console.WriteLine("OK   Kneeboard: Rollwege aus Airfield.Ramps, NDB in NavText, AWACS-Seite mit Config-Frequenz, Hinweisseite");
    }

    static void Page(string file, string title, string[] lines, bool mono = false)
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
    }
}
