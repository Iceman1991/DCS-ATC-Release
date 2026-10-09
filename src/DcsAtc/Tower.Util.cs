using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= Navigation
    (double X, double Z) Thr(string rw) { var e = F.End(rw); return (e.ThrX, e.ThrZ); }
    double LandHdg(string rw) => F.End(rw).Hdg;

    /// Point on the extended centreline, "along" metres before the threshold.
    (double X, double Z) OnCenterline(string rw, double along)
    {
        var e = F.End(rw);
        return (e.ThrX - along * e.Dx, e.ThrZ - along * e.Dz);
    }

    /// > 0 = on the pattern side of the runway (left for left-hand pattern)
    double PatternOffset(double x, double z, string rw)
    {
        var e = F.End(rw);
        double s = Hand(rw) == "left hand" ? 1 : -1;
        return s * ((x - e.CX) * e.Dz - (z - e.CZ) * e.Dx);
    }

    public bool OnRunwayPos(double x, double z, double halfWidth = 30, double beyond = 50) => F.Ends.Any(e =>
    {
        double along = (x - e.CX) * e.Dx + (z - e.CZ) * e.Dz, lat = Math.Abs((x - e.CX) * e.Dz - (z - e.CZ) * e.Dx);
        return Math.Abs(along) < e.Len / 2 + beyond && lat < halfWidth;
    });

    /// The occupied runway (name of the landing direction) or null: the active one if occupied, otherwise the direction by heading (A38).
    string? RunwayAt(double x, double z, double hdgDeg, string active)
    {
        var hit = F.Ends.Where(e => Math.Abs((x - e.CX) * e.Dx + (z - e.CZ) * e.Dz) < e.Len / 2 + 50 && Math.Abs((x - e.CX) * e.Dz - (z - e.CZ) * e.Dx) < 30).ToList();
        return hit.Count == 0 ? null : (hit.FirstOrDefault(e => e.Name == active) ?? hit.MinBy(e => HdgDiff(e.Hdg, hdgDeg))!).Name;
    }

    /// More generous than OnFinal: for checking a "final" report (turn onto final counts).
    bool OnFinalLoose(Telemetry t, string rw)
    {
        var (along, lateral) = Approach(t.X, t.Z, rw);
        return along > 200 && along < 8 * NM && lateral < 300 + along * 0.25 && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 60;
    }

    /// A44: base position = on the pattern side, below 3 NM from the airfield, before the threshold, heading across the runway toward the centreline.
    bool OnBase(Telemetry t, string rw)
    {
        double rel = (t.Hdg * 180 / Math.PI - LandHdg(rw)) * Math.PI / 180, s = Hand(rw) == "left hand" ? 1 : -1;
        return Dist(t.X, t.Z, CX, CZ) < 3 * NM && PatternOffset(t.X, t.Z, rw) > 200 && Approach(t.X, t.Z, rw).along > 0 && s * Math.Sin(rel) > 0.5;
    }

    internal static double Bearing(double fx, double fz, double tx, double tz) => (Math.Atan2(tz - fz, tx - fx) * 180 / Math.PI + 360) % 360;
    internal static double HdgDiff(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);
    internal static int Miles(double m) => Math.Max(1, (int)Math.Round(m / NM));
    internal static string MilesTxt(double m) { int n = Miles(m); return n == 1 ? "1 mile" : $"{n} miles"; }   // R373: singular for 1
    internal static string Dir8(double brg) =>
        new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" }[(int)Math.Round(brg / 45) % 8];
    string Where(Telemetry t, string of = "the field") => $"{MilesTxt(Dist(t.X, t.Z, CX, CZ))} {Dir8(Bearing(CX, CZ, t.X, t.Z))} of {of}";

    /// Remember "fly heading one two zero" (magnetic, 5° steps) and target for further headings.
    string Steer(Telemetry t, (double X, double Z) goal, string name)
    {
        nav = goal; navName = name; lastVector = lastSeen;
        return "fly " + HdgSay(Bearing(t.X, t.Z, goal.X, goal.Z));
    }
    /// "heading one two zero" (magnetic, 5° steps, N51b redirect 10°) for the true bearing brg.
    string HdgSay(double brg, int step = 5) { int mag = (int)Math.Round(((brg - MagVar) % 360 + 360) % 360 / step) * step; return $"heading {Digits((mag == 0 ? 360 : mag).ToString("000"))}"; }

    /// ", fly heading ..." only if the target is farther than 3 NM away.
    string SteerTo(Telemetry? t, (double X, double Z) goal, string name)
    {
        nav = goal; navName = name;
        return t != null && Dist(t.X, t.Z, goal.X, goal.Z) > 3 * NM ? ", " + Steer(t, goal, name) : "";
    }

    static string GlideText(double devFt) => devFt switch
    {
        > 150 => "well above glidepath",
        > 60 => "slightly above glidepath",
        < -150 => "well below glidepath",
        < -60 => "slightly below glidepath",
        _ => "on glidepath",
    };

    string CourseText(Telemetry t, string rw, double along)
    {
        var (tx, tz) = Thr(rw);
        double h = LandHdg(rw) * Math.PI / 180, dx = Math.Cos(h), dz = Math.Sin(h);
        double right = (t.X - tx) * -dz + (t.Z - tz) * dx;                 // > 0 = right of the approach baseline
        double tol = along * Math.Tan(Math.PI / 180) + 20;
        if (Math.Abs(right) < tol) return "on course";
        // Heading to a point on the centreline ahead, to the degree as with PAR
        var aim = Pt(rw, Math.Max(along - Math.Max(NM, along * 0.4), 0), 0);
        int mag = (int)Math.Round(((Bearing(t.X, t.Z, aim.X, aim.Z) - MagVar) % 360 + 360) % 360);
        return $"{(Math.Abs(right) < 2.5 * tol ? "slightly " : "")}{(right > 0 ? "right" : "left")} of course, " +
               $"turn {(right > 0 ? "left" : "right")} heading {Digits((mag == 0 ? 360 : mag).ToString("000"))}";
    }

    static string GlideFix(double devFt) => devFt switch
    {
        > 150 => ", increase rate of descent",
        < -150 => ", level off",
        < -60 => ", reduce rate of descent",
        _ => "",
    };

    // ======================================================================= Geometry & weather
    internal static double Sq(double v) => v * v;
    internal static double Dist(double x1, double z1, double x2, double z2) => Math.Sqrt(Sq(x1 - x2) + Sq(z1 - z2));
    internal static bool OnGround(Telemetry t) => t.Agl < 3;
    /// Landing features (R11, basis for N2): gear down or below 200 kt and descending (over 200 ft/min, not altitude fluctuation in a flyby).
    internal static bool LandingCues(Telemetry t) => t.Gear >= 0.5 || t.Ias < 200 * Kt && t.Vs < -1;

    /// A43: without break straight ahead on short final (gear down or slow in descent) is no longer initial, otherwise there would never be a landing clearance.
    bool StraightIn(Telemetry t, string rw) => LandingCues(t) && OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 3 * NM);

    /// Distance before the threshold (along > 0 = on approach) and lateral offset to the extended centreline.
    (double along, double lateral) Approach(double x, double z, string rw)
    {
        var e = F.End(rw);
        double vx = x - e.ThrX, vz = z - e.ThrZ;
        return (-(vx * e.Dx + vz * e.Dz), Math.Abs(vx * e.Dz - vz * e.Dx));
    }

    /// R314: long final – aligned on the extended centreline up to 12 NM (lateral up to 0.1 × distance + 300 m, heading ±30°): "9 mile final" is a final (AIM 4-3-2).
    bool OnLongFinal(Telemetry t, string rw)
    {
        var (along, lateral) = Approach(t.X, t.Z, rw);
        return along > 200 && along <= 12 * NM && lateral < 300 + along * 0.1 && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 30;
    }

    /// R332: traffic on final up to 8 NM or on long final up to 12 NM (geometry as OnLongFinal, up to 1500 m) – counts in sequence and description.
    bool OnFinalTr(Traffic a, string rw) => a.Speed > 30 && (OnFinal(a.X, a.Z, a.Hdg, a.AltMsl - FieldElev, rw, 8 * NM) ||
        Approach(a.X, a.Z, rw) is var (al, la) && al > 8 * NM && al <= 12 * NM && la < 300 + al * 0.1 && HdgDiff(a.Hdg * 180 / Math.PI, LandHdg(rw)) < 30 && a.AltMsl - FieldElev < 1500);

    bool OnFinal(double x, double z, double hdg, double agl, string rw, double maxDist)
    {
        var (along, lateral) = Approach(x, z, rw);
        return along > 200 && along < maxDist && lateral < 150 + along * 0.18 && HdgDiff(hdg * 180 / Math.PI, LandHdg(rw)) < 30 && agl < 900;
    }

    string Position(Telemetry? t)
    {
        if (t == null) return "go ahead";
        double brg = (Math.Atan2(t.Z - CZ, t.X - CX) * 180 / Math.PI + 360) % 360;
        string[] dirs = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
        int d = (int)Math.Round(Dist(t.X, t.Z, CX, CZ) / NM);
        return $"identified, {MilesTxt(d * NM)} {dirs[(int)Math.Round(brg / 45) % 8]} of {F.Name.Replace('-', ' ')}";   // R322: reference point (FAA JO 7110.65 5-3-7)
    }

    static double WindFromTrue(Telemetry t) => (Math.Atan2(-t.WindZ, -t.WindX) * 180 / Math.PI + 360) % 360;
    static double WindSpeed(Telemetry t) => Math.Sqrt(Sq(t.WindX) + Sq(t.WindZ));
    /// R263: ground speed on the ground (m/s): Export.lua delivers IAS, at standstill it shows the headwind
    internal static double Gs(Telemetry t) => Math.Max(0, t.Ias - WindSpeed(t));   // Ground speed (on the ground; R263/R331)

    /// Runway as DCS-ATC (Airfield.BestRunway) with the wind at the airfield, not the one at flight level.
    public (double X, double Z)? FieldWind;   // Program: mission weather at the airfield (10 m)
    string WindRunway(Telemetry t) => FieldWind is { } w ? F.BestRunway(w.X, w.Z) : F.BestRunway(t.WindX, t.WindZ);

    /// "25" -> "two five", "13L" -> "one three left"
    public static string RwSay(string rw) => Digits(rw) + (rw.EndsWith('L') ? " left" : rw.EndsWith('R') ? " right" : rw.EndsWith('C') ? " center" : "");

    string Wind(Telemetry? t)
    {
        if (t == null) return "wind not available";
        if (FieldWind is { } fw) t = t with { WindX = fw.X, WindZ = fw.Z };   // Surface wind at the airfield as ATIS, not the one at flight level
        double kt = WindSpeed(t) / Kt;
        if (kt < 3) return "wind calm";
        int dir = (int)Math.Round(((WindFromTrue(t) - MagVar) % 360 + 360) % 360 / 10) * 10;
        if (dir == 0) dir = 360;
        return $"wind {Digits(dir.ToString("000"))} degrees, {Digits(((int)Math.Round(kt)).ToString())} knots";
    }

    /// QNH from pressure at the aircraft + altitude. Unit of LoGetBasicAtmospherePressure unclear
    /// (mmHg/hPa/Pa) -> take the variant that gives a plausible QNH.
    /// Air pressure at the aircraft in hPa (mission: Pa, export: mmHg) and the QNH reduced from it to sea level (standard atmosphere).
    internal static (double Hpa, double Qnh)? Baro(Telemetry t)
    {
        if (t.Pressure <= 0) return null;
        foreach (var hpa in new[] { t.Pressure * 1.33322, t.Pressure, t.Pressure / 100 })
        {
            double v = hpa / Math.Pow(1 - 2.25577e-5 * t.AltMsl, 5.25588);
            if (v is > 940 and < 1070) return (hpa, v);
        }
        return null;
    }

    /// QNH of the airfield (hPa, Program from mission weather at the airfield); 0 = unknown -> reduced from the aircraft.
    public double FieldQnh;
    double QnhHpa(Telemetry? t) => FieldQnh > 0 ? FieldQnh : t != null && Baro(t) is { } b ? b.Qnh : 1013;
    string Qnh(Telemetry? t) => QnhSay(QnhHpa(t), InHg(AcType));
    /// ATIS goes to all: in auto mode both units, otherwise the selected one.
    string AtisQnh(Telemetry w) => QnhSay(QnhHpa(w), AltimeterUnit switch { "hpa" => false, "inhg" => true, _ => null });   // unknown value like auto (InHg likewise)

    /// Altimeter unit (config.jsonc "AltimeterUnit"): auto (by own type), hpa, inhg
    public static string AltimeterUnit = "auto";
    /// Western types with inHg altimeter; rest (MiG/Su/Mirage/Viggen/JF-17/Ka/Mi, British and German warbirds) in hPa/mb.
    static readonly Regex InHgTypes = new(@"^(F-14|FA-18|F-16|F-15|F-4E|F-5|F-86|F-117|A-10|AV8B|A-4|A-6|T-45|S-3|E-2|E-3|C-130|C-17|KC-1|B-1|B-52|AH-64|UH-1|UH-60|OH-?58|CH-47|P-51|TF-51|P-47|F4U|Christen)");
    internal static bool InHg(string type) => AltimeterUnit == "inhg" || AltimeterUnit != "hpa" && InHgTypes.IsMatch(type);
    /// "QNH one zero one three" or "altimeter two niner niner two" (1 hPa = 0.02953 inHg); inHg null = both.
    /// R345: truncated, not rounded (QNH to whole hPa down, inHg to 0.01 down; ICAO Annex 3 App. 3 4.7).
    internal static string QnhSay(double hpa, bool? inHg)
    {
        string q = $"QNH {Digits(((int)Math.Floor(hpa + 1e-6)).ToString())}", a = $"altimeter {Digits((Math.Floor(hpa * 0.02953 * 100 + 1e-6) / 100).ToString("0.00", CultureInfo.InvariantCulture))}";
        return inHg is { } i ? (i ? a : q) : $"{q}, {a}";
    }

    /// Altitude as on the altimeter with airfield QNH (Mode C), not the true altitude: in cold air the altimeter reads higher.
    /// All assigned altitudes are indicated altitudes.
    double IndFt(Telemetry t) => FieldQnh > 0 && Baro(t) is { } b
        ? (1 - Math.Pow(b.Hpa / FieldQnh, 0.190263)) * 44330.77 / Ft
        : t.AltMsl / Ft;

    static string Alt(double ft) => ft >= 18000 ? $"flight level {Digits(Math.Round(ft / 100).ToString("0"))}" : $"{ft:0} feet";
    /// R130: "climb/descend and maintain" only for a new altitude; same altitude as last time or Mode-C tolerance ±300 ft: "maintain". dev: deviation (check altitude, immediately) names the direction.
    double lastAltFt = -1;
    string AltTo(Telemetry? t, double ft, bool dev = false)
    {
        bool same = !dev && ft == lastAltFt;
        lastAltFt = ft;
        if ((Emergency || t != null && Pd(t)) && !dev && t != null && IndFt(t) - ft > 300) return Descend(ft, t);   // R292: emergency on top: also in repeats at own discretion
        return t == null || same || Math.Abs(IndFt(t) - ft) <= (dev ? 200 : 300) ? $"maintain {Alt(ft)}"
            : $"{(IndFt(t) < ft ? "climb" : "descend")} and maintain {Alt(ft)}";
    }
    internal static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ======================================================================= Speech
    internal static readonly string[] DigitWords = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "niner" };
    static readonly Dictionary<string, string> Words = new()   // Number words, ICAO, known mishearings (corpus, V3)
    {
        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5",
        ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9", ["niner"] = "9",
        ["tree"] = "3", ["fower"] = "4", ["fife"] = "5", ["decimal"] = "point",   // ICAO pronunciation
        ["enbound"] = "inbound", ["embound"] = "inbound",
    };
    static readonly HashSet<string> NotCallsign = new()
    {
        "runway", "qnh", "information", "point", "tower", "kutaisi", "request", "requesting", "with", "for",
        "ready", "holding", "final", "miles", "mile", "heading", "altitude", "angels", "level", "check",
        "the", "and", "this", "is", "at", "to", "inbound", "feet", "knots", "degrees", "wind", "initial",
        "number", "crp", "north", "south", "east", "west", "via", "left", "right", "short",
        "state", "ball", "fuel", "flight", "cherubs", "thousand", "climb", "climbing", "descend", "descending", "passing", "leaving", "maintain",
        "spot", "stand", "gate", "parking", "squawk", "frequency", "departure", "contact", "approach", "ground", "altimeter", "track", "speed", "radial", "report",
    };

    internal static string Digits(string s) => string.Join(" ", s.Where(char.IsDigit).Select(ch => DigitWords[ch - '0']));

    /// "Enfield 1-1" -> "Enfield one one"
    internal static string SpokenCallsign(string cs) =>
        Regex.Replace(Regex.Replace(cs, @"\d", m => " " + DigitWords[m.Value[0] - '0'] + " ").Replace("-", " "), " +", " ").Trim();

    internal static string Normalize(string text)
    {
        var s = Regex.Replace(text.ToLowerInvariant(), @"\[[^\]]*\]|\([^)]*\)", " ");   // [BLANK_AUDIO] etc.
        s = Regex.Replace(s, @"(?<=\d),(?=\d{3}\b)", "");      // 6,000 -> 6000
        s = Regex.Replace(s, @"(?<=\d)\.(?=\d)", " point ");    // 6.2 -> 6 point 2
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        s = Regex.Replace(s, @"\bmay +day\b", "mayday");     // R360: transcription splits the word
        return string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(w => Words.TryGetValue(w, out var d) ? d : w));
    }

    /// Callsign at the start of the call (at most 4 words of station before it) or at the end ("…, Enfield 1-1"); digits in the middle of the call are values ("squawk 4 3 2 1").
    internal static string? ExtractCallsign(string n)
    {
        foreach (Match m in Regex.Matches(n, @"\b([a-z]{3,}) (\d)(?: ?(\d))?\b"))
        {
            var word = m.Groups[1].Value;
            if (NotCallsign.Contains(word) || n[..m.Index].Count(ch => ch == ' ') > 4 && m.Index + m.Length < n.Length) continue;
            var cs = char.ToUpper(word[0]) + word[1..] + " " + m.Groups[2].Value;
            return m.Groups[3].Success ? cs + "-" + m.Groups[3].Value : cs;
        }
        return null;
    }

    static string? Direction(string n) =>
        new[] { "north", "south", "east", "west" }.FirstOrDefault(d => Regex.IsMatch(n, $@"\b{d}\b"));

    internal static bool Has(string n, params string[] keys) => keys.Any(k => Regex.IsMatch(n, $@"\b{k}"));

    /// Airfield with the most name words (from 4 letters, hit gets them regex-safe), on a tie the first; null = none.
    /// The whole callsign (Radio.lua) beats any name word: "Krasnodar Tower" = Center, not Pashkovsky (K6). Likewise the whole multi-part name:
    /// "Ras Al Khaimah Intl Approach" otherwise matched only "intl" (Khaimah is neither at the front nor before the station), on a tie the nearer Fujairah Intl answered.
    internal static Airfield? ByName(IEnumerable<Airfield> fields, Func<string, bool> hit) =>
        fields.Select(f => (f, s: f.Name.ToLowerInvariant().Split('-', ' ').Count(w => w.Length >= 4 && hit(Regex.Escape(w)))
                                  + (f.Callsign != "" && hit(Regex.Escape(f.Callsign.ToLowerInvariant())) ? 10 : 0)
                                  + (Regex.Split(f.Name.ToLowerInvariant(), "[^a-z]+").Where(w => w != "").ToArray() is { Length: > 1 } nw && hit(string.Join(@"\W+", nw.Select(Regex.Escape))) ? 10 : 0)))
              .Where(h => h.s > 0).OrderByDescending(h => h.s).FirstOrDefault().f;

    // ======================================================================= Numbers (V2), n = Normalize(...)
    /// Digits after a keyword, single (ICAO) or together, at most max: "heading 2 7 0" -> "270", "runway 25 4 miles" -> "25"
    static string? Run(string n, string key, int max) =>
        Regex.Match(n, $@"\b(?:{key}) ?(\d+(?: \d+)*)") is { Success: true } m ? m.Groups[1].Value.Replace(" ", "") is var d && d.Length > max ? d[..max] : d : null;
    static double Dbl(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// Altitude in ft: "flight level 1 5 0", "FL150", "angels 15", "cherubs 5", "6 thousand 5 hundred", "4000 feet", "climbing 6500"
    internal static double? AltSlot(string n) =>
        Run(n, "flight level|fl|level", 3) is { } fl ? Dbl(fl) * 100
      : Run(n, "angels", 2) is { } an ? Dbl(an) * 1000
      : Run(n, "cherubs", 2) is { } ch ? Dbl(ch) * 100
      : Regex.Match(n, @"\b(\d+) thousand(?: (\d) hundred)?") is { Success: true } th ? Dbl(th.Groups[1].Value) * 1000 + (th.Groups[2].Success ? Dbl(th.Groups[2].Value) * 100 : 0)
      : Regex.Match(n, @"\b(\d{3,5}) (?:feet|ft)\b|\b(?:altitude|climb|climbing|descend|descending|maintain|passing|leaving)(?: to| for)? (\d{3,5})\b") is { Success: true } f
          ? Dbl(f.Groups[1].Success ? f.Groups[1].Value : f.Groups[2].Value) : null;

    /// Heading: "heading 2 7 0" -> 270
    internal static int? HdgSlot(string n) => Run(n, "heading", 3) is { } h && int.Parse(h) is var v and >= 1 and <= 360 ? v : null;

    /// Fuel in 1000 lb: "state 6 point 2", "fuel state 4 point 2", "hornet ball 5 point 3", "state 6200" -> 6.2
    internal static double? FuelSlot(string n) =>
        Regex.Match(n, @"\b(?:state|ball) (\d+)(?: (?:point )?(\d)\b)?") is { Success: true } m
            ? Dbl(m.Groups[1].Value + (m.Groups[2].Success ? "." + m.Groups[2].Value : "")) is var v && v >= 100 ? v / 1000 : v : null;

    /// Runway: "runway 3 1" -> "31", "runway 7" -> "07", "runway 7, 4 miles" -> "07" (runways only go up to 36)
    internal static string? RwySlot(string n) => Run(n, "runway", 2) is { } r ? (int.Parse(r) > 36 ? r[..1] : r).PadLeft(2, '0') : null;
    /// Side of a parallel runway: "runway 3 0 left", "runway 30l" -> "L" (not "left hand downwind"), otherwise ""
    internal static string RwySide(string n) => Regex.Match(n, @"\brunway ?\d+(?: \d+)* ?([lrc])(?:eft|ight|enter|entre)?\b(?! hand)") is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : "";

    /// The same callsign, just misheard: digits equal (or truncated), word up to 1/3 of the letters different ("Enfeld 1-1" = "Enfield 1-1").
    internal static bool SameCallsign(string heard, string known)
    {
        var (hw, hd) = Split(heard); var (kw, kd) = Split(known);
        return hd.Length > 0 && kd.StartsWith(hd) && Lev(hw.ToCharArray(), kw.ToCharArray()) <= Math.Max(1, kw.Length / 3);
        static (string W, string D) Split(string cs) => (new string(cs.ToLowerInvariant().Where(char.IsLetter).ToArray()), new string(cs.Where(char.IsDigit).ToArray()));
    }

    /// Levenshtein distance (letters of a callsign, words of a radio call)
    internal static int Lev<T>(T[] a, T[] b)
    {
        var d = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int diag = d[0]; d[0] = i;
            for (int j = 1; j <= b.Length; j++)
                (diag, d[j]) = (d[j], Math.Min(Math.Min(d[j], d[j - 1]) + 1, diag + (EqualityComparer<T>.Default.Equals(a[i - 1], b[j - 1]) ? 0 : 1)));
        }
        return d[b.Length];
    }
}
