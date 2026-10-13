using System.Text.RegularExpressions;

namespace DcsAtc;

/// Center sector (config CenterSectors): the nearest center point inside RadiusNm wins. Lat/Lon only on Caucasus (Airfield.Ll2Xz), other maps X/Z.
public class CenterSector
{
    public string Name { get; set; } = "";
    public double Freq { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    public double RadiusNm { get; set; } = 150;
    public string Side { get; set; } = "";   // "blue", "red", "" = both
    internal (double X, double Z) Pos => Lat != 0 || Lon != 0 ? Airfield.Ll2Xz(Lat, Lon) : (X, Z);
    internal bool Fits(int side) => Side.ToLowerInvariant() switch { "blue" => side != 1, "red" => side != 2, _ => true };
}

/// Center (AGENTUREN-PLAN, stage 1): en-route controller = flight following under its own name and frequency. Following stays true, cenName makes it Center.
public partial class Tower
{
    static readonly Dictionary<string, (string Blue, string? Red)> CenterTable = new()
    {
        ["Nevada"] = ("Los Angeles Center", null), ["Caucasus"] = ("Tbilisi Control", "Rostov Control"), ["PersianGulf"] = ("Emirates Control", "Tehran Control"),
        ["Syria"] = ("Nicosia Control", "Damascus Control"), ["SinaiMap"] = ("Tel Aviv Control", "Cairo Control"), ["MarianaIslands"] = ("Guam Center", null),
        ["Kola"] = ("Bodo Control", "Murmansk Control"), ["Afghanistan"] = ("Kabul Center", null), ["Iraq"] = ("Baghdad Control", null),
        ["GermanyCW"] = ("Langen Radar", "Berlin Control"), ["Falklands"] = ("Mount Pleasant Control", "Comodoro Control"),
    };
    public static Dictionary<string, string> CenterNames = new();   // config: map -> name, "map:red" -> red name
    public static List<CenterSector> CenterSectors = new();
    public static Func<bool> CenterOn = () => true;   // Program: module "center"
    public static Func<Airfield, double> QnhOf = _ => 0;   // Program: QNH of another airfield (0 = unknown)
    public const double CenterAwacsNm = 100;   // own AWACS/GCI closer than this: no Center (E4/E5)
    const double CenterIfrNm = 50, CenterDestNm = 40, CenterFfNm = 20;   // IFR destination closer: its Approach right away; descent/handover before the destination; flight following leaves the approach area (below Program.Pick limit 25 NM)

    /// Single Center of the map for the side (2 blue, 1 red); null = none (Normandy/Channel/WWII, module off). Unknown map "Center", red without own name: the blue one.
    public static string? CenterName(int side)
    {
        var th = Airfield.Theatre;
        if (!CenterOn() || th is "Normandy" or "TheChannel" || th.Contains("WWII")) return null;
        CenterTable.TryGetValue(th, out var d);
        var blue = CenterNames.GetValueOrDefault(th) ?? d.Blue ?? "Center";
        return side == 1 ? CenterNames.GetValueOrDefault(th + ":red") ?? d.Red ?? blue : blue;
    }

    /// Center at a position: a sector (nearest center point inside its radius) wins, otherwise the single Center of the map; null = none.
    public static (string Name, double Freq)? CenterFor(int side, double x, double z)
    {
        if (!CenterOn()) return null;
        var sec = CenterSectors.Where(s => s.Fits(side) && Dist(x, z, s.Pos.X, s.Pos.Z) < s.RadiusNm * NM).MinBy(s => Dist(x, z, s.Pos.X, s.Pos.Z));
        if (sec != null) return (sec.Name, sec.Freq > 0 ? sec.Freq : Freqs["Center"]);
        return CenterName(side) is { } n ? (n, Freqs["Center"]) : null;
    }

    /// Call to the Center ("Center: …", "Tbilisi Control, Enfield 1-1, …"), not "contact Tbilisi Control" and not "Krasnodar Center Tower": Program.Called must not take the name for an airfield.
    public static bool IsCenterCall(string text) =>
        Regex.IsMatch(Regex.Replace(text.ToLowerInvariant(), "[^a-z ]", " "), $@"^(?:(?!contact\b)\w+\W+){{0,3}}(?:{CenterRx}|cent(?:er|re)\b(?!\W+(?:ground|tower|approach|departure|radar)\b))");
    /// Text without Center names ("Tbilisi Control, request direct Batumi": Tbilisi is not a destination).
    internal static string NoCenter(string n) => Regex.Replace(n, $@"\b(?:{CenterRx})\b", " ");
    static string CenterRx => string.Join("|", new[] { CenterName(1), CenterName(2) }.Concat(CenterSectors.Select(s => s.Name)).Where(s => !string.IsNullOrEmpty(s)).Select(s => Regex.Escape(s!.ToLowerInvariant())).Distinct().DefaultIfEmpty("(?!)"));

    string? cenName; double cenFreq; Airfield? cenTo; bool ffIn;   // Center service: name/frequency (sector at check-in), destination named by the pilot; ffIn = flight following started at the field
    /// Center service is running (en route, Phase.Away): flight following under the Center's name.
    public bool Center => Following && cenName != null && Phase == Phase.Away;
    string CenName => cenName ?? (lastTel != null ? CenterFor(Side, lastTel.X, lastTel.Z)?.Name : null) ?? CenterName(Side) ?? "Center";
    internal double CenFreq => cenFreq > 0 ? cenFreq : (lastTel != null ? CenterFor(Side, lastTel.X, lastTel.Z)?.Freq : null) ?? Freqs["Center"];
    /// Station name of a controller on this airfield; Center speaks with its own name.
    public string StationOf(string r) => r == "Center" ? CenName : F.StationOf(r);
    Airfield? CenDest => cenTo ?? dest;

    /// "contact Tbilisi Control two six eight decimal five." for this position, null = no Center there.
    string? CenterContact(Telemetry t, string more = "") => CenterFor(Side, t.X, t.Z) is { } c ? $"contact {c.Name} {FreqSay(c.Freq)}{more}." : null;

    /// Flight following moves to the next tower with the flight (Program.Switch), the Center service with it.
    internal void TakeCenter(Tower o)
    {
        Following |= o.Following; (cenName, cenFreq, cenTo) = (o.cenName, o.cenFreq, o.cenTo);
        o.Following = false; o.cenName = null;
    }

    /// Service over (handover, sign-off): the next call goes to whoever the pilot calls (Program.Pick).
    void CenterEnd() { Following = false; cenName = null; cenTo = null; depClr = false; nav = null; handedOff = false; popM = 0; transitFt = null; Released = true; }

    /// End of the Center service (E5), every second while it runs: own AWACS in range, next sector, 40 NM before the destination (descent, altimeter, its Approach). The pilot's "leaving frequency" is ffoff.
    List<Msg>? CenterTick(Telemetry t, double now)
    {
        string? end = null;
        if (AwacsContact is { } aw) end = $"contact {aw}.";
        else if (CenterFor(Side, t.X, t.Z) is { } nx && nx.Name != cenName) end = $"contact {nx.Name} {FreqSay(nx.Freq)}.";
        else if (CenDest is { } d && Dist(t.X, t.Z, d.X, d.Z) < CenterDestNm * NM)
        {
            var ft = Math.Ceiling(Math.Max(F.MvaLeg(t.X, t.Z, d.X, d.Z), d.PatternFt + 1000) / 100) * 100;
            var qnh = QnhOf(d) is > 0 and var q ? $" {d.Name.Replace('-', ' ')} altimeter {QnhSay(q, InHg(AcType))}." : "";
            end = (IndFt(t) - ft > 300 ? $"{AltTo(t, ft)}.{qnh} " : qnh.TrimStart() is { Length: > 0 } a ? a + " " : "") + $"contact {d.StationOf("Approach")} {FreqSay(FreqOf(d, "Approach"))}.";
        }
        if (end == null) return null;
        var bye = Say($"{Cs()}, {end}", "Approach");   // still as Center (Dep)
        CenterEnd();
        return bye;
    }
}
