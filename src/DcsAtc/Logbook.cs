using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DcsAtc;

/// FI3: logbook over all flights (logbook.json next to the app, per player name): flight time per type, landings per airfield (night), patterns,
/// traps (night, Case III) with boarding rate and GPA, bolters, tanker contacts, range passes, deviations. Fed from the final debriefing (its lines
/// survive an app restart), milestones and the totals go to its end. Delete logbook.json to start over.
class Logbook
{
    public int Flights, NightLandings, Patterns, Traps, NightTraps, Case3Traps, Bolters, LsoPasses, Tanker, Range, Deviations, Checkrides, CheckridesPassed, Rescues;
    public double LsoPoints;
    public Dictionary<string, double> Minutes = new();   // type -> flight minutes
    public Dictionary<string, int> Landings = new();     // airfield -> landings

    static readonly JsonSerializerOptions Opts = new() { IncludeFields = true, WriteIndented = true };

    /// One flight into the logbook of `who`, saved at once; returns the debrief lines (milestones, then the totals).
    /// An unreadable file stays untouched (no overwrite = no lost logbook).
    public static List<string> Add(string file, string who, string type, double minutes, IEnumerable<string> debrief, int tanker, int range)
    {
        Dictionary<string, Logbook> all;
        try
        {
            all = JsonSerializer.Deserialize<Dictionary<string, Logbook>>(File.ReadAllText(file), Opts) ?? throw new JsonException();
            if (all.Values.Any(entry => entry?.Minutes == null || entry.Landings == null)) throw new JsonException();
        }
        catch (FileNotFoundException) { all = new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new() { L("Logbuch: logbook.json nicht lesbar, Flug nicht eingetragen", "Logbook: logbook.json unreadable, flight not logged") };
        }
        var b = all.TryGetValue(who, out var o) ? o : all[who] = new();
        var was = JsonSerializer.Deserialize<Logbook>(JsonSerializer.Serialize(b, Opts), Opts)!;
        b.Record(type, minutes, debrief, tanker, range);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(all, Opts));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return new() { L("Logbuch: Speichern fehlgeschlagen, Flug nicht eingetragen", "Logbook: save failed, flight not logged") };
        }
        return b.Miles(was, type).Append(b.Summary()).ToList();
    }

    void Record(string type, double min, IEnumerable<string> lines, int tanker, int range)
    {
        Flights++;
        if (min > 0) Minutes[type] = Minutes.GetValueOrDefault(type) + min;
        (Tanker, Range) = (Tanker + tanker, Range + range);
        string land = L("Landebewertung ", "Landing grade "), rwy = L(" Bahn ", " runway "), night = L("Nacht", "night");
        foreach (var d in lines)
        {
            var head = d.Split(':')[0];   // tags (night, Case III) stand before the first colon (Program.Tag)
            if (d.StartsWith(land) && d.IndexOf(rwy) is var i and > 0)
            {
                var f = d[land.Length..i];
                Landings[f] = Landings.GetValueOrDefault(f) + 1;
                if (head.Contains(night)) NightLandings++;
            }
            else if (d.StartsWith(L("Platzrunde ", "Pattern "))) Patterns++;
            else if (d.StartsWith(L("Prüfflug ", "Checkride "))) { Checkrides++; if (!d.Contains(L("nicht bestanden", "failed"))) CheckridesPassed++; }   // FI4
            else if (d.StartsWith("CSAR: ") && d.Contains(L(" gerettet", " rescued"))) Rescues++;   // FI6
            else if (d.StartsWith(L("Träger LSO", "Carrier LSO")))
            {
                var g = d[(d.IndexOf(':') + 1)..].TrimStart();   // "(OK) 3-wire, groove 14 s, … (3 points)", "B, groove …", "WO, …", "C, waveoff ignored, 2-wire …"
                var grade = g.Split(' ', ',')[0];
                if (Regex.Match(g, @"\(([\d.,]+) (?:Punkte?|points?)\)$") is { Success: true } m) { LsoPasses++; LsoPoints += double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture); }
                if (grade == "B") Bolters++;
                else if (grade is "_OK_" or "OK" or "(OK)" or "--" || g.Contains("-wire"))
                {
                    Traps++;
                    if (head.Contains(night)) NightTraps++;
                    if (head.Contains("Case III")) Case3Traps++;
                }
            }
            if (d.Contains(L("Verstoß:", "Deviation:"))) Deviations++;
        }
    }

    static string Hm(double min) => $"{(int)(min / 60)}:{(int)(min % 60):00}";

    /// Milestones reached with this flight: first/10th/… landing and trap, first night landing, night trap, Case III trap, tanker contact, first flight in a type, flight hours.
    IEnumerable<string> Miles(Logbook w, string type)
    {
        int[] steps = { 1, 10, 50, 100, 250, 500, 1000 };
        string? M(int before, int now, string deFirst, string de, string en, int[] at) =>
            at.FirstOrDefault(n => before < n && now >= n) is var n and > 0 ? L("Meilenstein: " + (n == 1 ? deFirst : $"{n}. {de}"), "Milestone: " + (n == 1 ? "first " : $"{n}th ") + en) : null;
        var l = new List<string?>
        {
            !w.Minutes.ContainsKey(type) && Minutes.ContainsKey(type) ? L($"Meilenstein: erster Flug mit {type}", $"Milestone: first flight in the {type}") : null,
            M(w.Landings.Values.Sum(), Landings.Values.Sum(), "erste Landung", "Landung", "landing", steps),
            M(w.NightLandings, NightLandings, "erste Nachtlandung", "", "night landing", new[] { 1 }),
            M(w.Traps, Traps, "erster Trap", "Trap", "trap", steps),
            M(w.NightTraps, NightTraps, "erster Nacht-Trap", "", "night trap", new[] { 1 }),
            M(w.Case3Traps, Case3Traps, "erster Case-III-Trap", "", "Case III trap", new[] { 1 }),
            M(w.Tanker, Tanker, "erster Tankerkontakt", "", "tanker contact", new[] { 1 }),
            M(w.CheckridesPassed, CheckridesPassed, "erster bestandener Prüfflug", "", "checkride passed", new[] { 1 }),
            M(w.Rescues, Rescues, "erste Rettung", "Rettung", "rescue", new[] { 1, 10, 50, 100 }),
            new[] { 10, 50, 100, 250, 500, 1000 }.FirstOrDefault(h => w.Minutes.Values.Sum() < h * 60 && Minutes.Values.Sum() >= h * 60) is var hr and > 0
                ? L($"Meilenstein: {hr} Flugstunden", $"Milestone: {hr} flight hours") : null,
        };
        return l.OfType<string>();
    }

    /// "Logbook (3 flights): 2:15 h (F-16C_50 1:30, FA-18C_hornet 0:45), 4 landings (1 at night), …"; zero counts are left out.
    internal string Summary()
    {
        int ln = Landings.Values.Sum(), att = Traps + Bolters;
        var parts = new List<string> { $"{Hm(Minutes.Values.Sum())} h ({string.Join(", ", Minutes.OrderByDescending(m => m.Value).Select(m => $"{m.Key} {Hm(m.Value)}"))})" };
        if (ln > 0) parts.Add(N(ln, "Landung", "Landungen", "landing", "landings") + (NightLandings > 0 ? L($" ({NightLandings} nachts)", $" ({NightLandings} at night)") : "") +
                              " " + L("an", "at") + " " + N(Landings.Count, "Platz", "Plätzen", "airfield", "airfields"));
        if (Patterns > 0) parts.Add(N(Patterns, "Platzrunde", "Platzrunden", "pattern", "patterns"));
        if (Traps > 0) parts.Add(N(Traps, "Trap", "Traps", "trap", "traps") + (NightTraps + Case3Traps > 0 ? $" ({string.Join(", ", new[] { NightTraps > 0 ? L($"{NightTraps} nachts", $"{NightTraps} at night") : "", Case3Traps > 0 ? $"{Case3Traps} Case III" : "" }.Where(s => s != ""))})" : ""));
        if (att > 0) parts.Add($"Boarding Rate {100.0 * Traps / att:0} %");
        if (LsoPasses > 0) parts.Add(FormattableString.Invariant($"GPA {LsoPoints / LsoPasses:0.00}"));
        if (Bolters > 0) parts.Add(N(Bolters, "Bolter", "Bolter", "bolter", "bolters"));
        if (Tanker > 0) parts.Add(N(Tanker, "Tankerkontakt", "Tankerkontakte", "tanker contact", "tanker contacts"));
        if (Range > 0) parts.Add(N(Range, "Range-Anflug", "Range-Anflüge", "range pass", "range passes"));
        if (Checkrides > 0) parts.Add(N(Checkrides, "Prüfflug", "Prüfflüge", "checkride", "checkrides") + L($" ({CheckridesPassed} bestanden)", $" ({CheckridesPassed} passed)"));
        if (Rescues > 0) parts.Add(N(Rescues, "Rettung", "Rettungen", "rescue", "rescues"));
        if (Deviations > 0) parts.Add(N(Deviations, "Verstoß", "Verstöße", "deviation", "deviations"));
        return L("Logbuch", "Logbook") + $" ({N(Flights, "Flug", "Flüge", "flight", "flights")}): " + string.Join(", ", parts);
    }
    static string N(int n, string de1, string de, string en1, string en) => $"{n} " + L(n == 1 ? de1 : de, n == 1 ? en1 : en);

    /// Self-test: two flights into a fresh file, totals, milestones, a broken file is not overwritten.
    internal static void SelfTest()
    {
        var file = Path.Combine(Path.GetTempPath(), $"dcsatc-logbook-{Environment.ProcessId}.json");
        try
        {
            File.Delete(file);
            var f1 = Add(file, "Ice", "FA-18C_hornet", 75, new[] { L("Landebewertung Kutaisi Bahn 25 (Nacht): B (78/100) – lang.", "Landing grade Kutaisi runway 25 (night): B (78/100) – long."),
                L("Platzrunde 1 (touch and go). Beenden", "Pattern 1 (touch and go). To end"), L("Verstoß: Start ohne Freigabe", "Deviation: takeoff without clearance") }, 1, 0);
            var f2 = Add(file, "Ice", "FA-18C_hornet", 50, new[] { L("Träger LSO: B, groove 16 s, NESA (2.5 Punkte)", "Carrier LSO: B, groove 16 s, NESA (2.5 points)"),
                L("Träger LSO (Case III, Nacht): (OK) 3-wire, groove 14 s, HIM (3 Punkte)", "Carrier LSO (Case III, night): (OK) 3-wire, groove 14 s, HIM (3 points)"),
                L("Landebewertung Batumi Bahn 13: A (95/100) – gut.", "Landing grade Batumi runway 13: A (95/100) – good."), L("Prüfflug Batumi: bestanden", "Checkride Batumi: passed"), L("CSAR: Enfield one two gerettet, 25 min nach dem Ausstieg", "CSAR: Enfield one two rescued, 25 min after the ejection") }, 0, 4);
            var b = JsonSerializer.Deserialize<Dictionary<string, Logbook>>(File.ReadAllText(file), Opts)!["Ice"];
            var s2 = string.Join(" | ", f2);
            if (b.Flights != 2 || b.Minutes["FA-18C_hornet"] != 125 || b.Landings["Kutaisi"] != 1 || b.Landings["Batumi"] != 1 || b.NightLandings != 1 || b.Patterns != 1 || b.Traps != 1 ||
                b.NightTraps != 1 || b.Case3Traps != 1 || b.Bolters != 1 || b.LsoPasses != 2 || b.Tanker != 1 || b.Range != 4 || b.Deviations != 1 ||
                !f1.Contains(L("Meilenstein: erster Flug mit FA-18C_hornet", "Milestone: first flight in the FA-18C_hornet")) || !f1.Contains(L("Meilenstein: erste Nachtlandung", "Milestone: first night landing")) ||
                !f1.Contains(L("Meilenstein: erster Tankerkontakt", "Milestone: first tanker contact")) || f2.Any(x => x.Contains(L("Flug mit", "flight in"))) ||
                !f2.Contains(L("Meilenstein: erster Case-III-Trap", "Milestone: first Case III trap")) || !f2.Contains(L("Meilenstein: erster bestandener Prüfflug", "Milestone: first checkride passed")) ||
                b.Checkrides != 1 || b.CheckridesPassed != 1 || b.Rescues != 1 || !f2.Contains(L("Meilenstein: erste Rettung", "Milestone: first rescue")) || !f2[^1].Contains(L("1 Prüfflug (1 bestanden)", "1 checkride (1 passed)")) || f2.Any(x => x.Contains(L("erste Landung", "first landing"))) ||
                !f2[^1].Contains("2:05 h") || !f2[^1].Contains("Boarding Rate 50 %") || !f2[^1].Contains("GPA 2.75") || !f2[^1].Contains(L("2 Landungen (1 nachts) an 2 Plätzen", "2 landings (1 at night) at 2 airfields")))
                throw new Exception("Logbuch: Summen/Meilensteine: " + string.Join(" | ", f1) + " || " + s2);
            var before = File.ReadAllText(file);
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Add(file, "Ice", "F-16C_50", 30, Array.Empty<string>(), 0, 0).Single();
                if (!error.Contains(L("Speichern fehlgeschlagen", "save failed")) || File.ReadAllText(file) != before)
                    throw new Exception("Logbuch: gesperrtes Ziel als gespeichert gemeldet oder verändert");
            }
            foreach (var invalid in new[] { "{ kaputt", "null", """{ "Ice": null }""", """{ "Ice": { "Minutes": null } }""" })
            {
                File.WriteAllText(file, invalid);
                var error = Add(file, "Ice", "F-16C_50", 30, Array.Empty<string>(), 0, 0).Single();
                if (!error.Contains(L("nicht lesbar", "unreadable")) || File.ReadAllText(file) != invalid)
                    throw new Exception("Logbuch: kaputte Datei überschrieben oder falsche Rückmeldung");
            }
        }
        finally { File.Delete(file); }
        Console.WriteLine("OK   FI3 Logbuch: zwei Flüge, Summen, Meilensteine, kaputte Datei bleibt");
    }
}
