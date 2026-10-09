using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DcsAtc;

/// Landing direction of a runway. Hdg = true landing heading (degrees), CX/CZ = runway center.
public record RwyEnd(string Name, double ThrX, double ThrZ, double Hdg, double Len, double CX, double CZ)
{
    public double Dx => Math.Cos(Hdg * Math.PI / 180);
    public double Dz => Math.Sin(Hdg * Math.PI / 180);
    /// Point a meters before the threshold (approach side > 0), l meters left of the approach line.
    public (double X, double Z) At(double a, double l) => (ThrX - a * Dx + l * Dz, ThrZ - a * Dz - l * Dx);
}

/// Apron point from the chart data (charts/caucasus.jsonc): per runway taxiway to it (To), vacate instruction after landing (Vacate), taxiway back (In, "*" = any runway).
public record Ramp(string Name, double X, double Z, Dictionary<string, string> To, Dictionary<string, string> Hold, Dictionary<string, string> Vacate, Dictionary<string, string> In);

public class Airfield
{
    const double Ft = 0.3048;

    public string Name = "";
    public int Id;                            // DCS airdrome ID
    public int Side;                          // Coalition (0 neutral, 1 red, 2 blue; mission)
    public double Uhf, Vhf;                   // Airfield frequency from DCS (Ground/Tower/Approach shared), 0 = unknown
    public bool HasFreq => Uhf > 0 || Vhf > 0;   // Radio.lua knows an airfield frequency (Gulf: often VHF only, e.g. OMAA 119.2, or UHF only)
    public string Callsign = "";              // Call name from Radio.lua ("Kolkhi" for Senaki-Kolkhi), "" = name without hyphen
    public Dictionary<string, (double Mhz, string Id)> Ils = new();   // Runway -> localizer (Beacons.lua)
    public int Tacan, Prmg, Rsbn;             // Channels (TACAN X), 0 = none; PRMG/RSBN only for the kneeboard
    public double Vor;                        // MHz, 0 = none
    public List<(string Id, int Khz)> Ndb = new();   // NDB/homer in file order (K16): kneeboard/NavText only, no NDB approach
    public string TacanId = "", VorId = "";
    public static string Theatre = "Caucasus";
    /// Local time of the map vs. UTC in hours (DCS without daylight saving); null = unknown map.
    public static double? UtcOffset => Theatre switch
    {
        "Caucasus" or "PersianGulf" => 4, "Syria" or "Kola" or "Iraq" => 3, "Nevada" => -8, "MarianaIslands" => 10, "Normandy" or "TheChannel" or "GermanyCW" => 1,
        "SinaiMap" => 2, "Afghanistan" => 4.5, "Falklands" => -3, _ => null
    };
    /// Forum request: own frequencies from frequencies.jsonc (Program.ApplyFreqs): role (ATIS, Ground, Tower, Departure, Approach) -> MHz, override the map.
    public Dictionary<string, double[]> Own = new();
    public static readonly string[] Roles = { "Ground", "Tower", "Approach", "Departure" };
    /// Frequencies of a controller: own (Own), otherwise the map frequencies (UHF, VHF; Ground/Tower/Approach shared), Departure without own like Approach;
    /// "*" = all controllers of the airfield (locked?). null = none (without Radio.lua: config per controller).
    public double[]? FreqsOf(string role) =>
        role == "*" ? (Roles.SelectMany(r => FreqsOf(r) ?? Array.Empty<double>()).Distinct().ToArray() is { Length: > 0 } all ? all : null)
        : Own.TryGetValue(role, out var o) ? o
        : role == "Departure" ? FreqsOf("Approach")
        : role is "Ground" or "Tower" or "Approach" && HasFreq ? new[] { Uhf, Vhf }.Where(x => x > 0).ToArray() : null;
    public string Station => StationOf("Tower");
    public string StationOf(string role) => (Callsign != "" ? Callsign : Name.Replace('-', ' ')) + " " + role;
    public double X, Z, Elev;                 // Airfield reference point, elevation in m
    public double MagVar;                     // Degrees east
    public double PatternFt, MaxFt;           // Traffic pattern / control zone max. (MSL)
    public List<RwyEnd> Ends = new();
    public Dictionary<string, (double X, double Z)> Crp = new();   // Compulsory reporting points (map only)
    public Ramp[] Ramps = Array.Empty<Ramp>();
    public Dictionary<string, (double X, double Z, string[] Rwy)> Holds = new();   // Named holding points (map)
    public Dictionary<string, string> HandOf = new();                          // Runway -> "right hand"/"left hand" (map)
    public Dictionary<string, List<(string Crp, string Via)>> Entry = new();   // Runway -> entry CRPs (last = default without position), via "overhead"/"initial"
    public Dictionary<string, List<string>> Exit = new();                      // Runway -> exit CRPs (first = default)
    public List<(double X, double Z, int Num, int Type)> Spots = new();   // Parking positions from DCS (Airbase:getParking): Term_Index, Term_Type (16 runway, 40 helicopter)
    public bool Charted => Crp.Count > 0;     // procedures per DCS kneeboard chart (charts/caucasus.jsonc)
    /// A100: control zone (ICAO Annex 11): with map, farthest CRP + 0.5 NM (Kutaisi about 9 NM), otherwise 5 NM; up to 3000 ft above the airfield.
    public double CtrNm => Charted ? Crp.Values.Max(p => Math.Sqrt((p.X - X) * (p.X - X) + (p.Z - Z) * (p.Z - Z))) / 1852 + 0.5 : 5;
    public double CtrTopFt => Elev / Ft + 3000;

    public RwyEnd End(string name) => Ends.First(e => e.Name == name);

    /// Emergency (R37, A15 AWACS): nearest own/neutral airfield with a runway, preferably from 1800 m; null = none.
    public static Airfield? Emergency(IEnumerable<Airfield> fields, double x, double z, int side)
    {
        var own = fields.Where(f => f.Ends.Count > 0 && (f.Side == 0 || f.Side == side)).ToList();
        return (own.Any(f => f.Ends.Max(e => e.Len) >= 1800) ? own.Where(f => f.Ends.Max(e => e.Len) >= 1800) : own).MinBy(f => (f.X - x) * (f.X - x) + (f.Z - z) * (f.Z - z));
    }

    // ------------------------------------------------------------------ Terrain (mission: DcsAtc-Terrain-<Id>.txt)
    float[,]? terrain;                        // highest point per cell (m)
    double tx0, tz0, tCell;

    /// Header line "Name;x0;z0;Zelle;n", then n lines with n heights (m), line = x index.
    public void LoadTerrain(string file)
    {
        var lines = File.ReadAllLines(file);
        var h = lines[0].Split(';');
        double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        int n = int.Parse(h[4]);
        var g = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            var row = lines[i + 1].Split(',');
            for (int j = 0; j < n; j++) g[i, j] = float.Parse(row[j], CultureInfo.InvariantCulture);
        }
        (tx0, tz0, tCell, terrain) = (D(h[1]), D(h[2]), D(h[3]), g);
    }
    public bool HasTerrain => terrain != null;
    /// Coarse grid over the whole map (mission, V15): fallback outside the airfield grid.
    public static readonly Airfield Map = new() { Name = "Map" };
    public void SetTerrain(float[,] g, double x0, double z0, double cell) => (terrain, tx0, tz0, tCell) = (g, x0, z0, cell);   // Tests

    /// Landing runway as in DCS-ATC, because that is the only one the AI lands on: up to 3.5 m/s wind the main runway, otherwise the one with the most headwind
    /// (forum tests Anapa/Krymsk/Maykop/Mozdok: 3 m/s main runway, 4 m/s headwind; Senaki 3.1 m/s crosswind -> AI observed on 27).
    /// wx, wz: wind vector at the airfield (toward, m/s).
    /// Parallel runways: landing is on the runway matching the main runway (Main or its reciprocal, Program learns it from the AI), otherwise the first from getRunways.
    public string BestRunway(double wx, double wz)
    {
        var m = Ends.FirstOrDefault(e => e.Name == Main);
        var b = Math.Sqrt(wx * wx + wz * wz) < 3.5 && m != null ? m : Ends.MaxBy(e => -(wx * e.Dx + wz * e.Dz))!;
        return Ends.Where(e => Diff(e.Hdg, b.Hdg) < 10).OrderBy(e => m != null && e.CX == m.CX && e.CZ == m.CZ ? 0 : 1).First().Name;
    }
    /// DCS main runway = named direction from Airbase:getRunways (correct at all test airfields). Program corrects it
    /// when an AI takes off/lands differently in light wind.
    public string Main = "";

    /// Highest (low: lowest) terrain (ft) in the cells touching the circle of rNm around (x, z). Without terrain data/outside: very negative.
    public double TerrainFt(double x, double z, double rNm, bool low = false)
    {
        if (terrain is not { } g) return this == Map ? float.MinValue : Map.TerrainFt(x, z, rNm, low);
        double r = rNm * 1852;
        int n = g.GetLength(0), k = (int)Math.Ceiling(r / tCell);
        int ci = (int)Math.Floor((x - tx0) / tCell), cj = (int)Math.Floor((z - tz0) / tCell);
        float none = low ? float.MaxValue : float.MinValue, max = none;
        for (int i = Math.Max(0, ci - k); i <= Math.Min(n - 1, ci + k); i++)
            for (int j = Math.Max(0, cj - k); j <= Math.Min(n - 1, cj + k); j++)
            {
                double dx = Math.Clamp(x, tx0 + i * tCell, tx0 + (i + 1) * tCell) - x, dz = Math.Clamp(z, tz0 + j * tCell, tz0 + (j + 1) * tCell) - z;
                if (dx * dx + dz * dz <= r * r) max = low ? Math.Min(max, g[i, j]) : Math.Max(max, g[i, j]);
            }
        return max != none ? max / Ft : this != Map ? Map.TerrainFt(x, z, rNm, low) : float.MinValue;
    }

    /// Minimum altitude (ft MSL) for radar vectoring: highest terrain within 3 NM + 1000 ft (in mountains + 2000 ft as in ICAO PANS-OPS:
    /// more than 3000 ft elevation difference within 10 NM, i.e. not on a flat plateau), rounded up to 100 ft.
    /// Without terrain data or outside the grid: 0 (= no restriction).
    public double MvaFt(double x, double z) => TerrainFt(x, z, 3) is var t && t < -1e6 ? 0
        : Math.Ceiling((t + (t - TerrainFt(x, z, 10, true) > 3000 ? 2000 : 1000)) / 100) * 100;

    /// Minimum altitude along a route (samples every 1 NM).
    public double MvaLeg(double x1, double z1, double x2, double z2)
    {
        double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1)), m = 0;
        int k = (int)Math.Ceiling(len / 1852);
        for (int i = 0; i <= k; i++) m = Math.Max(m, MvaFt(x1 + (x2 - x1) * i / Math.Max(k, 1), z1 + (z2 - z1) * i / Math.Max(k, 1)));
        return m;
    }

    /// Map: as charted (Kutaisi always south: 07 right, 25 left). Otherwise left, unless on the right the terrain in the pattern is more than 300 ft lower
    /// (downwind 1.5 NM abeam to 2 NM before the threshold, base).
    public string Hand(string rw) =>
        HandOf.TryGetValue(rw, out var h) ? h : SideFt(rw, -1) < SideFt(rw, 1) - 300 ? "right hand" : "left hand";

    /// Highest terrain (ft) of the pattern on one side (1 left, -1 right): downwind 1.5 NM abeam to 2 NM before the threshold, base.
    double SideFt(string rw, int s)
    {
        var e = End(rw);
        return new[] { (-e.Len - 1852, 1.5), (-e.Len / 2, 1.5), (0, 1.5), (1852, 1.5), (2 * 1852, 1.5), (2 * 1852, 0.75) }
            .Max(p => e.At(p.Item1, s * p.Item2 * 1852) is var (x, z) ? TerrainFt(x, z, 0.5) : 0);
    }

    /// Pattern flyable: on the hand side the terrain is at least 500 ft below pattern altitude (as at the initial, Tower.Excess).
    /// Khasab (fjord, over 1800 ft on both sides at 1500 ft pattern): no -> straight-in instead of overhead and pattern.
    public bool PatternOk(string rw) => Charted || SideFt(rw, Hand(rw) == "right hand" ? -1 : 1) <= PatternFt - 500;

    // ------------------------------------------------------------------ Kutaisi (UGKO), kneeboard charts
    public static Airfield Kutaisi()
    {
        double t07x = -285234.1, t07z = 682655.1, t25x = -284545.3, t25z = 685049.4;
        double len = Math.Sqrt((t25x - t07x) * (t25x - t07x) + (t25z - t07z) * (t25z - t07z));
        double h07 = Math.Atan2(t25z - t07z, t25x - t07x) * 180 / Math.PI;
        double cx = (t07x + t25x) / 2, cz = (t07z + t25z) / 2;
        return new Airfield
        {
            Name = "Kutaisi", X = cx, Z = cz, Elev = 45, MagVar = 6, Main = "25",   // DCS getRunways: "25"
            Ends = { new("07", t07x, t07z, h07, len, cx, cz), new("25", t25x, t25z, h07 + 180, len, cx, cz) },
        }.ApplyChart();
    }

    // ------------------------------------------------------------------ Kneeboard chart data (charts/caucasus.jsonc, PLATZVERFAHREN-PLAN.md)
    static JsonDocument? chart;
    internal static JsonElement Chart => (chart ??= JsonDocument.Parse(typeof(Airfield).Assembly.GetManifestResourceStream("caucasus.jsonc")!,
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })).RootElement;

    /// Procedures from the chart data if the airfield is in there; without entry everything stays as before (E5).
    /// Until Etappe 3/4 arm them, only Kutaisi is applied; ChartAll (ChartTest) loads all entries.
    public static bool ChartAll;
    public Airfield ApplyChart()
    {
        if (Name != "Kutaisi" && !ChartAll || !Chart.TryGetProperty(Name, out var a)) return this;
        static Dictionary<string, string> Strs(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) ? v.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!) : new();
        if (a.TryGetProperty("pattern_ft", out var p)) PatternFt = p.GetDouble();
        if (a.TryGetProperty("arrdep_max_ft", out var m)) MaxFt = m.GetDouble();
        if (a.TryGetProperty("crp", out var crp)) foreach (var c in crp.EnumerateObject()) Crp[c.Name] = Pos(c.Value);
        if (a.TryGetProperty("rwy", out var rwy))
            foreach (var r in rwy.EnumerateObject())
            {
                if (r.Value.TryGetProperty("hand", out var h)) HandOf[r.Name] = h.GetString() + " hand";
                if (r.Value.TryGetProperty("entry", out var en)) Entry[r.Name] = en.EnumerateObject().Select(e => (e.Name, e.Value.GetProperty("via").GetString()!)).ToList();
                if (r.Value.TryGetProperty("exit", out var ex)) Exit[r.Name] = ex.EnumerateObject().Select(e => e.Name).ToList();
            }
        if (a.TryGetProperty("ground", out var g) && g.TryGetProperty("holds", out var holds))
            foreach (var h in holds.EnumerateObject()) { var (x, z) = Pos(h.Value.GetProperty("pos")); Holds[h.Name] = (x, z, h.Value.GetProperty("rwy").EnumerateArray().Select(v => v.GetString()!).ToArray()); }
        if (a.TryGetProperty("ground", out g) && g.TryGetProperty("ramps", out var ramps))
            Ramps = ramps.EnumerateObject().SelectMany(r =>
            {
                var (to, hold, vac, inb) = (Strs(r.Value, "to"), Strs(r.Value, "hold"), Strs(r.Value, "vacate"), Strs(r.Value, "in"));
                return r.Value.GetProperty("pts").EnumerateArray().Select(q => { var (x, z) = Pos(q); return new Ramp(r.Name, x, z, to, hold, vac, inb); });
            }).ToArray();
        return this;
    }

    /// Point as printed ("42°15.924'N 042°30.086'E") or DCS [x, z].
    static (double X, double Z) Pos(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Array) return (v[0].GetDouble(), v[1].GetDouble());
        var m = Regex.Match(v.GetString()!, @"(\d+)°([\d.]+)'([NS])\s+(\d+)°([\d.]+)'([EW])");
        if (!m.Success) throw new FormatException("Kartenkoordinate: " + v.GetString());
        double D(int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        return Ll2Xz((D(1) + D(2) / 60) * (m.Groups[3].Value == "S" ? -1 : 1), (D(4) + D(5) / 60) * (m.Groups[6].Value == "W" ? -1 : 1));
    }

    /// WGS84 -> DCS Caucasus (Transverse Mercator lon_0 33°, k0 0.9996, Snyder series; selftest against Beacons.lua: 164 beacons under 1 m).
    public static (double X, double Z) Ll2Xz(double lat, double lon)
    {
        const double a = 6378137, f = 1 / 298.257223563, k0 = 0.9996, e2 = f * (2 - f), ep2 = e2 / (1 - e2);
        double p = lat * Math.PI / 180, s = Math.Sin(p), c = Math.Cos(p), t = Math.Tan(p);
        double n = a / Math.Sqrt(1 - e2 * s * s), T = t * t, C = ep2 * c * c, A = (lon - 33) * Math.PI / 180 * c;
        double M = a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * p - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(2 * p)
                        + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(4 * p) - 35 * e2 * e2 * e2 / 3072 * Math.Sin(6 * p));
        double east = k0 * n * (A + (1 - T + C) * Math.Pow(A, 3) / 6 + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * Math.Pow(A, 5) / 120);
        double north = k0 * (M + n * t * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24 + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * Math.Pow(A, 6) / 720));
        return (north - 4998115, east - 99517);
    }

    // ------------------------------------------------------------------ all others: data from DCS (Airbase:getRunways)
    /// rwys: (name, course rad, center x, z, length). DCS course is the negative heading of the named direction.
    public static Airfield FromDcs(string name, double x, double z, double elev,
                                   IEnumerable<(string Name, double Course, double X, double Z, double Len)> rwys)
    {
        var f = new Airfield { Name = name, X = x, Z = z, Elev = elev };
        var vars = new List<double>();
        foreach (var r in rwys)
        {
            var n = Pad(r.Name.Split('-')[0].Trim());
            if (!int.TryParse(new string(n.TakeWhile(char.IsDigit).ToArray()), out int num)) continue;
            double h = ((-r.Course * 180 / Math.PI) % 360 + 360) % 360;
            if (Diff(h, num * 10) > 90) h = (h + 180) % 360;
            double dx = Math.Cos(h * Math.PI / 180), dz = Math.Sin(h * Math.PI / 180);
            // Duplicate runway (Abu Dhabi: "31" 3907 m and "31" 3032 m on the same centerline): same heading, under 50 m lateral, overlapping -> one, the longer
            int d = f.Ends.FindIndex(e => Diff(e.Hdg, h) < 10 && Math.Abs((r.X - e.CX) * e.Dz - (r.Z - e.CZ) * e.Dx) < 50 && Math.Abs((r.X - e.CX) * e.Dx + (r.Z - e.CZ) * e.Dz) < (e.Len + r.Len) / 2);
            if (d >= 0)
            {
                if (r.Len > f.Ends[d].Len)
                    (f.Ends[d], f.Ends[d ^ 1]) = (new(f.Ends[d].Name, r.X - r.Len / 2 * dx, r.Z - r.Len / 2 * dz, h, r.Len, r.X, r.Z),
                                                  new(f.Ends[d ^ 1].Name, r.X + r.Len / 2 * dx, r.Z + r.Len / 2 * dz, (h + 180) % 360, r.Len, r.X, r.Z));
                continue;
            }
            f.Ends.Add(new(n, r.X - r.Len / 2 * dx, r.Z - r.Len / 2 * dz, h, r.Len, r.X, r.Z));
            f.Ends.Add(new(Opposite(n), r.X + r.Len / 2 * dx, r.Z + r.Len / 2 * dz, (h + 180) % 360, r.Len, r.X, r.Z));
            vars.Add(((h - num * 10) % 360 + 540) % 360 - 180);
        }
        // Parallel runways: DCS names them without side, one direction per runway (Dubai "30" and "12", Al Dhafra "13" and "31"). Name per landing direction by the
        // lateral position of the other runways with the same heading (±10°, over 50 m apart): L left in landing direction, R right, C between.
        // The opposite direction follows as a mirror image (30R/12L). Sides from DCS stay.
        f.Ends = f.Ends.Select(e => char.IsDigit(e.Name[^1]) && f.Ends.Where(o => Diff(o.Hdg, e.Hdg) < 10).Select(o => (o.CX - e.CX) * e.Dz - (o.CZ - e.CZ) * e.Dx)
            .Where(l => Math.Abs(l) > 50).ToList() is { Count: > 0 } left ? e with { Name = e.Name + (left.All(l => l < 0) ? "L" : left.All(l => l > 0) ? "R" : "C") } : e).ToList();
        f.Main = f.Ends.FirstOrDefault()?.Name ?? "";
        // K26: reference point = mean of the runway centers (the DCS airfield point is up to 1 NM off and shifts all radii)
        if (f.Ends.Count > 0) { f.X = f.Ends.Average(e => e.CX); f.Z = f.Ends.Average(e => e.CZ); }
        // Runway numbers are often old (Batumi "31" -> -4°, real +6°): Caucasus fixed like Kutaisi; live from DCS overrides (Program, export telemetry)
        f.MagVar = Theatre == "Caucasus" ? 6 : vars.Count > 0 ? Math.Clamp(Math.Round(vars.Average()), -20, 20) : 0;
        f.PatternFt = Math.Round((elev / Ft + 1500) / 100) * 100;
        f.MaxFt = f.PatternFt + 500;
        return f;
    }

    /// File from the mission script: "A;Name;x;z;elev" followed by "R;Name;course;x;z;length". Kutaisi always from the map, Caucasus airfields with chart data get it (ApplyChart).
    public static List<Airfield> Load(string? file)
    {
        var list = new List<Airfield> { Kutaisi() };
        if (file == null || !File.Exists(file)) return list;
        string? name = null;
        double ax = 0, az = 0, el = 0;
        int id = 0, side = 0;
        var rw = new List<(string, double, double, double, double)>();
        var spots = new List<(double X, double Z, int Num, int Type)>();
        void Flush()
        {
            if (name == "Kutaisi") list[0].Spots = spots.ToList();
            else if (name != null && rw.Count > 0) { var f = FromDcs(name, ax, az, el, rw); if (Theatre == "Caucasus") f.ApplyChart(); f.Id = id; f.Side = side; f.Spots = spots.ToList(); list.Add(f); }
            spots.Clear();
            rw.Clear();
        }
        foreach (var line in File.ReadAllLines(file))
        {
            var p = line.Split(';');
            double D(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
            if (p[0] == "M" && p.Length >= 2) Theatre = p[1];
            else if (p[0] == "A" && p.Length >= 5)
            {
                Flush(); name = p[1]; ax = D(2); az = D(3); el = D(4);
                id = p.Length >= 6 && int.TryParse(p[5], out var i) ? i : 0;
                side = p.Length >= 7 && int.TryParse(p[6], out var sd) ? sd : 0;
                if (name == "Kutaisi") (list[0].Id, list[0].Side) = (id, side);
            }
            else if (p[0] == "R" && p.Length >= 6) rw.Add((p[1], D(2), D(3), D(4), D(5)));
            else if (p[0] == "P" && p.Length >= 3) spots.Add((D(1), D(2), p.Length >= 5 ? (int)D(3) : 0, p.Length >= 5 ? (int)D(4) : 0));
        }
        Flush();
        return list;
    }

    /// Airfield frequencies from <DCS>\Mods\terrains\<map>\Radio.lua (radioId 'airfieldNN_0', UHF and VHF_HI, AM) and call name (nato/common, also for Red:
    /// ussr "Krasnodar" is Pashkovsky, nato "Krasnodar" Center). Words only, no ICAO/capitals (Gulf: "OMAA", "KERMAN", "SharjahIntl").
    public static void LoadRadio(IEnumerable<Airfield> fields, string dcsDir)
    {
        var byId = RadioTable(dcsDir, Theatre);
        foreach (var f in fields) if (byId.TryGetValue(f.Id, out var r)) (f.Uhf, f.Vhf, f.Callsign, f.RadioName) = r;
    }

    /// Name from Radio.lua/Beacons.lua (frequencies.jsonc without mission data): DCS comment before radioId, otherwise display_name of the airfield beacons, otherwise call name; "_" -> " ".
    public string RadioName = "";

    /// Radio.lua of a map: airfields by Id with UHF, VHF_HI, call name (words) and name (RadioName). Empty without file.
    public static Dictionary<int, (double Uhf, double Vhf, string Cs, string Name)> RadioTable(string dcsDir, string map)
    {
        var dir = Path.Combine(dcsDir, "Mods", "terrains", map);
        var byId = new Dictionary<int, (double Uhf, double Vhf, string Cs, string Name)>();
        if (!File.Exists(Path.Combine(dir, "Radio.lua"))) return byId;
        var beacons = File.Exists(Path.Combine(dir, "Beacons.lua")) ? File.ReadAllText(Path.Combine(dir, "Beacons.lua")) : "";
        var parts = File.ReadAllText(Path.Combine(dir, "Radio.lua")).Split("radioId");
        for (int i = 1; i < parts.Length; i++)
        {
            var block = parts[i];
            var id = Regex.Match(block, @"^\s*=\s*'airfield(\d+)_0'");
            if (!id.Success) continue;
            double F(string band) => Regex.Match(block, $@"\[{band}\]\s*=\s*\{{\s*MODULATIONTYPE_AM,\s*([\d.]+)") is { Success: true } m
                ? Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1e6, 3) : 0;
            var cs = Regex.Match(block, @"\[""(?:nato|common)""\]\s*=\s*\{\s*_\(""([A-Z][a-z]{3,}(?: [A-Z][a-z]{3,})*)""\)").Groups[1].Value;
            var name = new[] { Regex.Match(parts[i - 1], @"--[ \t]*([^\r\n]+?)[ \t]*\r?\n\s*$").Groups[1].Value,
                               Regex.Match(beacons, $@"display_name\s*=\s*_\('([^']+)'\);\s*beaconId\s*=\s*'airfield{id.Groups[1].Value}_").Groups[1].Value,
                               Regex.Match(block, @"\[""(?:nato|common)""\]\s*=\s*\{\s*_\(""([^""]+)""\)").Groups[1].Value, $"airfield {id.Groups[1].Value}" }
                       .First(s => s != "").Replace('_', ' ');
            byId[int.Parse(id.Groups[1].Value)] = (F("UHF"), F("VHF_HI"), cs, name);
        }
        return byId;
    }

    /// Navigation aids from <DCS>\Mods\terrains\<map>\Beacons.lua (beaconId 'airfieldNN_k'): ILS per runway, TACAN/VOR/NDB/PRMG/RSBN per airfield.
    /// Localizer stands beyond the runway end, direction points toward the approach: landing heading = direction + 180, runway with the smallest lateral distance (parallel runways).
    public static void LoadBeacons(IEnumerable<Airfield> fields, string dcsDir)
    {
        var file = Path.Combine(dcsDir, "Mods", "terrains", Theatre, "Beacons.lua");
        if (!File.Exists(file)) return;
        var byId = fields.Where(f => f.Id > 0).ToDictionary(f => f.Id);
        foreach (var block in File.ReadAllText(file).Split("beaconId").Skip(1))
        {
            var id = Regex.Match(block, @"^\s*=\s*'airfield(\d+)_\d+'");
            if (!id.Success || !byId.TryGetValue(int.Parse(id.Groups[1].Value), out var f)) continue;
            string S(string re) => Regex.Match(block, re).Groups[1].Value;
            double D(string re) => double.TryParse(S(re), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            string type = S(@"type\s*=\s*BEACON_TYPE_(\w+)"), cs = S(@"callsign\s*=\s*'([^']*)'");
            double mhz = Math.Round(D(@"frequency\s*=\s*([\d.]+)") / 1e6, 3);
            int ch = (int)D(@"channel\s*=\s*(\d+)");
            switch (type)
            {
                case "ILS_LOCALIZER":
                    double lx = D(@"position\s*=\s*\{\s*([-\d.]+)"), lz = D(@"position\s*=\s*\{\s*[-\d.]+\s*,\s*[-\d.]+\s*,\s*([-\d.]+)");
                    double h = D(@"direction\s*=\s*([-\d.]+)") + 180;
                    var end = f.Ends.Where(r => Diff(r.Hdg, h) < 30).MinBy(r => Math.Abs((lx - r.ThrX) * r.Dz - (lz - r.ThrZ) * r.Dx));
                    if (end != null) f.Ils[end.Name] = (mhz, cs);
                    break;
                case "TACAN": (f.Tacan, f.TacanId) = (ch, cs); break;
                case "VOR": (f.Vor, f.VorId) = (mhz, cs); break;
                case "PRMG_LOCALIZER": f.Prmg = ch; break;
                case "RSBN": f.Rsbn = ch; break;
                case "ILS_FAR_HOMER" or "ILS_NEAR_HOMER" or "AIRPORT_HOMER" or "AIRPORT_HOMER_WITH_MARKER":   // K16: kHz; not doubled on reload
                    var ndb = (cs, (int)Math.Round(mhz * 1000));
                    if (ndb.Item2 > 0 && !f.Ndb.Contains(ndb)) f.Ndb.Add(ndb);
                    break;
            }
        }
    }

    /// "ILS 07 109.75 IKS, TACAN 44X KTS, VOR 113.60 KT, NDB AP 443 / P 215" (kneeboard), "" without aids.
    public string NavText() => string.Join(", ", Ils.OrderBy(i => i.Key).Select(i => FormattableString.Invariant($"ILS {i.Key} {i.Value.Mhz:0.00} {i.Value.Id}"))
        .Concat(Tacan > 0 ? new[] { $"TACAN {Tacan}X {TacanId}" } : Array.Empty<string>())
        .Concat(Vor > 0 ? new[] { FormattableString.Invariant($"VOR {Vor:0.00} {VorId}") } : Array.Empty<string>())
        .Concat(Ndb.Count > 0 ? new[] { "NDB " + NdbText() } : Array.Empty<string>()));
    /// "AP 443 / P 215" (ident, kHz)
    public string NdbText() => string.Join(" / ", Ndb.Select(n => $"{n.Id} {n.Khz}"));

    static double Diff(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);
    static string Pad(string n) => n.Length > 0 && char.IsDigit(n[0]) && (n.Length == 1 || !char.IsDigit(n[1])) ? "0" + n : n;

    /// "07" -> "25", "13L" -> "31R"
    internal static string Opposite(string n)
    {
        int num = int.Parse(new string(n.TakeWhile(char.IsDigit).ToArray()));
        var suffix = n[n.TakeWhile(char.IsDigit).Count()..].Replace("L", "x").Replace("R", "L").Replace("x", "R");
        return ((num + 17) % 36 + 1).ToString("00") + suffix;
    }
}
