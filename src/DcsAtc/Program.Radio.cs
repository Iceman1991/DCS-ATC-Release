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
    // ------------------------------------------------------------ Speaking via SRS (all players)
    static SrsListener? Srs;
    static readonly HashSet<string> OwnStations = new();   // SRS names of our own senders (ExternalAudio)
    static bool IsOwnStation(string name) { lock (OwnStations) return OwnStations.Contains(name); }

    /// Radio call via SRS: sender via DCS unit ID (otherwise player name), called controller via the frequency.
    static void OnSrs(SrsListener.Transmission tx)
    {
        Pilot? p;   // A26/A141: sender first (R387: its tanker frequency decides the role; the Whisper prompt uses its call sign and airfield)
        lock (TowerLock) p = PilotFor(tx);
        var role = SrsRole(p, tx.FreqMHz);
        Airfield? via = null;   // airfield frequency: this airfield, controller by content (Ground/Tower/Approach) or the one with its own frequency
        (Airfield? Via, string? Role) shared = (null, null);   // station frequency an airfield also uses (Krasnodar 251.0 = AWACS 251.0): the call decides, otherwise ask
        if (role is not ("AWACS" or "Tanker" or "Range" or "Carrier" or "JTAC")) lock (TowerLock) (via, role) = FreqRoute(tx.FreqMHz, role);
        else lock (TowerLock) shared = FreqRoute(tx.FreqMHz, null);
        role ??= via == null ? "Tower" : null;
        var (text, conf) = Transcribe(SaveWav(tx.Pcm), p, role: shared.Via != null ? null : role ?? "Tower", via: via ?? shared.Via);   // LF1: prompt of the frequency (shared: airfield plus AWACS/tanker)
        var who = p == null ? "Rufzeichen im Spruch" : !MissionData ? "Host" : tx.UnitId != 0 && p.Id == tx.UnitId ? "Unit-ID" : p.Name == tx.Name ? "SRS-Name" : "einziger Spieler";   // Debug log
        if (Environment.GetEnvironmentVariable("DCSATC_SRSDEBUG") == "1") Console.WriteLine($"OnSrs {role} unit {tx.UnitId} '{tx.Name}': {text} ({conf:0.00})");
        if (Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)", "").Trim().Length < 2) { Trace("SRS", $"{tx.FreqMHz:0.000} MHz unit {tx.UnitId} '{tx.Name}' {tx.Pcm.Length / 16000.0:0.0} s: leer/Rauschen verworfen ({text})"); return; }   // [BLANK_AUDIO], noise
        lock (TowerLock)
        {
            // A26: sender unknown -> match the spoken call sign to a player (otherwise Request(null): "say again your callsign")
            p ??= ByCallsign(text);
            if (p != null) p.CalledOn = tx.FreqMHz;   // K2: reply names this frequency
            if (p != null && role is "AWACS" or "Tanker" or "Carrier") role = OpsPick(p, tx.FreqMHz, text, role);   // #3, #17: shared AWACS/tanker/carrier frequency
            if (p != null && shared.Via is { } sv)
            {
                if (SharedChoice(p, tx.FreqMHz, text, sv, role!) is not { } toField) return;   // unclear: asked back
                if (toField) (via, role) = shared;
            }
        }
        Trace("SRS", $"{tx.FreqMHz:0.000} MHz unit {tx.UnitId} '{tx.Name}' {tx.Pcm.Length / 16000.0:0.0} s -> {p?.Callsign ?? "unbekannt"} ({(p == null ? "-" : who)}), Lotse {role ?? "nach Inhalt"}, Platzfrequenz {via?.Name ?? "-"}, conf {conf:0.00}: {text}");
        Request(p, role == null ? text : $"{role}: {text}", "SRS", false, via, conf);
    }

    /// Shared frequency (under TowerLock): station name in the call decides and is remembered for unnamed calls; never chosen -> null, the player is asked.
    static bool? SharedChoice(Pilot p, double mhz, string text, Airfield field, string station)
    {
        var sn = Tower.Normalize(text);
        bool toField = Called(text, p.Tel, null) == field || Tower.Has(sn, "ground", "tower", "approach", "departure") || FieldRole(sn) != null,
             toStation = station == "Carrier" ? p.Boat.Wants(sn) : Ops.RoleOf(sn, p.Callsign) == station;   // #13: RoleOf knows no carrier stations (Marshal, Paddles …)
        if (toField != toStation) p.SharedPick = (mhz, toField);
        if (p.SharedPick is { } sp && Math.Abs(sp.Mhz - mhz) < 0.01) return sp.Field;
        Trace("SRS", $"{mhz:0.000} MHz {field.Name} und {station}: Station unklar, Rueckfrage: {text}");
        var f = mhz.ToString("0.0##", CultureInfo.InvariantCulture);
        SayQueue.Add(new Tx(L($"{f} MHz: {field.Name} oder {station}? Bitte mit Stationsnamen rufen.", $"{f} MHz: {field.Name} or {station}? Call again with the station name."), "Info", "", p.Gid));
        return null;
    }

    /// Station by a name or keyword in a call without prefix. #23 (log 12.11.: "Nellis Approach, … inbound. Request Arco." on the Approach frequency was answered by
    /// the tanker Arco, ARCOE is a recovery there): a call on an airfield's own frequency that names this airfield goes to the airfield, whatever word follows.
    internal static string? OpsByContent(Pilot p, string text, Airfield? via) =>
        via != null && Called(text, p.Tel, via) == via ? null : Ops.RoleOf(Tower.Normalize(text), p.Callsign);

    /// Issue #3: AWACS and tanker on one frequency (Instant Action, mission or config): the station name in the call decides and is remembered for unnamed calls; never named -> role of the frequency.
    internal static string OpsPick(Pilot p, double mhz, string text, string role)
    {
        bool On(IEnumerable<double> fs) => fs.Any(f => Math.Abs(f - mhz) < 0.01);
        bool cv = Carrier.Boats.Count > 0 && On(Carrier.Boats.Select(b => b.Freq).Concat(Carrier.StationFreqs.Values).Append(Cfg.Frequencies.GetValueOrDefault("Carrier")));   // #17: recovery tanker on the carrier frequency (Instant Action)
        if ((On(Ops.AwacsFreq.Values.Append(Cfg.Frequencies.GetValueOrDefault("AWACS"))) ? 1 : 0) + (On(Ops.TankerFreq.Values.Append(Cfg.Frequencies.GetValueOrDefault("Tanker")).Append(p.TankFq)) ? 1 : 0) + (cv ? 1 : 0) < 2) return role;
        var n = Tower.Normalize(text);
        if ((Ops.RoleOf(n, p.Callsign) is ("AWACS" or "Tanker") and var r0 ? r0 : cv && p.Boat.Wants(n) ? "Carrier" : null) is { } r) p.OpsPick = (mhz, r);
        return p.OpsPick is { } op && Math.Abs(op.Mhz - mhz) < 0.01 ? op.Role : role;
    }

    /// Role of a called frequency: the tanker frequency DCS tuned for the sender (R387, also if it equals an airfield frequency), config, mission AWACS/carrier; null = airfield frequency or unknown.
    internal static string? SrsRole(Pilot? p, double mhz) =>
        (p is { TankFq: > 0 } && Math.Abs(p.TankFq - mhz) < 0.01 ? "Tanker" : null)
        ?? (Cfg.Frequencies.FirstOrDefault(f => f.Key != "ATIS" && Math.Abs(f.Value - mhz) < 0.01).Key is var cr && cr == "RedCrown" ? "Carrier" : cr)   // Red Crown is a carrier station (module carrier)
        ?? (Carrier.RadarShips.Any(s => s.Freq > 0 && Math.Abs(s.Freq - mhz) < 0.01) ? "Carrier" : null)   // Red Crown on the mission frequency of the cruiser
        ?? (Tower.CenterSectors.FirstOrDefault(s => s.Freq > 0 && Math.Abs(s.Freq - mhz) < 0.01) is { } cs ? (cs.Gci ? "AWACS" : "Center") : null)   // Center sector frequency, #15 GCI sector
        ?? (Ops.AwacsFreq.Values.Any(f => Math.Abs(f - mhz) < 0.01) ? "AWACS" : null)   // mission AWACS
        ?? (Ops.TankerFreq.Values.Any(f => Math.Abs(f - mhz) < 0.01) ? "Tanker" : null)   // TP7: mission tanker
        ?? (Ops.Leaders.Any(l => l.Freq > 0 && Math.Abs(l.Freq - mhz) < 0.01) ? "JTAC" : null)   // J5: mission JTAC / FAC(A)
        ?? (Carrier.Boats.Any(b => b.Freq > 0 && Math.Abs(b.Freq - mhz) < 0.01) ? "Carrier" : null)   // mission carrier
        ?? (Carrier.Boats.Count + Carrier.RadarShips.Count > 0 && Carrier.StationFreqs.Values.Any(f => Math.Abs(f - mhz) < 0.01) ? "Carrier" : null);   // #13: own carrier station frequency, #18 Red Crown

    /// Sender of an SRS call (under TowerLock). R358: the only player only for an SRS client without unit (otherwise slot change, spectator, second client: call sign, A26)
    static Pilot? PilotFor(SrsListener.Transmission tx) =>
        !MissionData ? Host()
        : Pilots.Values.FirstOrDefault(x => tx.UnitId != 0 && x.Id == tx.UnitId)
          ?? Pilots.Values.FirstOrDefault(x => x.Name == tx.Name)
          ?? (Pilots.Count == 1 && tx.UnitId == 0 ? Pilots.Values.First() : null);

    /// Airfield and controller for the called frequency (under TowerLock): airfield with own frequency first; if exactly one controller owns it (AirfieldFrequencies), that one, otherwise by content (null).
    /// No airfield frequency: (null, role) unchanged.
    static (Airfield? Via, string? Role) FreqRoute(double mhz, string? role)
    {
        bool On(Airfield f, string r) => f.FreqsOf(r)?.Any(x => Math.Abs(x - mhz) < 0.01) == true;
        if (Fields.OrderBy(f => f.Own.Count == 0).FirstOrDefault(f => On(f, "*")) is not { } via) return (null, role);
        var rs = Airfield.Roles.Where(r => On(via, r) && (r != "Departure" || via.Own.ContainsKey(r))).ToList();   // Departure without its own = Approach
        return (via, rs.Count == 1 ? rs[0] : null);
    }

    /// A26: player whose call sign (or slot call sign) appears in the radio call, also misheard ("Enfeld 1-1"); under TowerLock.
    /// If a station name follows the call sign at the start of the call ("Enfield 1-1, Magic, ..." / "Enfield 1-1, Kutaisi Tower, ..."), the player is the addressee, not the sender (controller/GCI/bot).
    static Pilot? ByCallsign(string text)
    {
        var n = Tower.Normalize(text);
        if (Tower.ExtractCallsign(n) is not { } cs) return null;
        if (Regex.Match(n, $@"^{Regex.Escape(cs.ToLowerInvariant()).Replace("-", " ?")} ([a-z]+)") is { Success: true } m
            && (StationWords.Contains(m.Groups[1].Value) || Ops.Leaders.Any(l => Tower.Normalize(l.Cs).Split(' ')[0] == m.Groups[1].Value) || Fields.Any(f => $"{f.Name}-{f.Callsign}".ToLowerInvariant().Split('-', ' ').Contains(m.Groups[1].Value)))) return null;
        return Pilots.Values.FirstOrDefault(x => Tower.SameCallsign(cs, x.Callsign) || x.SlotCallsign != "" && Tower.SameCallsign(cs, x.SlotCallsign));
    }
    static readonly HashSet<string> StationWords = new() { "tower", "ground", "approach", "departure", "radar", "control", "center", "centre", "marshal", "paddles", "strike", "red", "mother", "overlord", "awacs", "magic", "moscow", "darkstar", "wizard", "focus", "texaco", "shell", "arco", "tanker", "range", "jtac", "faca" };

    static Ops.Me MeOf(Pilot p) => new(p.Callsign, p.Tel, p.Type, p.Coalition, p.FlightSize + (p.Gid == 0 ? 0 : Ai.Count(a => AiGid.GetValueOrDefault(a.Group) == p.Gid)), p.Fuel, Iff: Srs?.IffOf(p.Id), Modex: p.Modex);
    static Tx OpsTx(Pilot p, Ops.Call c)
    {
        if (c.To != null) lock (TowerLock) p = Pilots.Values.FirstOrDefault(x => x.Ops == c.To) ?? p;   // call to another player (tanker: the next one) goes to that player's group
        return new(c.Text, c.Role, c.Station, c.All ? 0 : p.Gid, Freqs: c.Freq > 0 ? new[] { c.Freq }
                   : c.Role == "Tanker" && new[] { p.TankFq, Ops.TankerFreqOf(p.Ops.TankerGroup) }.Where(f => f > 0).ToArray() is { Length: > 0 } tf
                     ? tf.Prepend(Cfg.Frequencies.GetValueOrDefault("Tanker", 255.5)).Distinct().ToArray() : null,   // R304: also on the tanker frequency tuned by DCS; TP7: and on the mission frequency of the tanker
                   MaxAge: c.Station == "Paddles" ? (Regex.IsMatch(c.Text, "^(?:Wave off|Bolter)") ? 4 : Regex.IsMatch(c.Text, "ball|Clara") ? 6 : 2) : c.Role == "AWACS" && AwacsRow(c.Text) is { Fresh: true } ? 12 : 0, Side: p.Coalition,
                   Live: c.Lead == 0 ? null : () => { lock (TowerLock) return p.Tel is { } tel && TrafficFor(p).FirstOrDefault(a => a.Id == c.Lead) is { } a ? Ops.Rebraa(c.Text, tel, a) : null; });   // LD4: BRAA at transmit time   // A19: LSO call later than 2 s is wrong ("Power." after correcting), "Roger ball."/"call the ball" not (waits behind the own ball call from the wheel, ~3 s); A133: AWACS/tanker/range of own side; A114: threat/merged with stale BRAA better as text only; 12 s: an urgent call before it (threat ~7 s + ReplyPause) is still spoken
    }

    /// A114/N46: kind of an AWACS call by its text (Ops untouched): threat/merged must be fresh, all four go into the debrief (at most 10 lines).
    internal static (string Row, bool Fresh)? AwacsRow(string text)
    {
        var m = Regex.Match(text, @", threat, (?:[a-z]+ )*?group (.*)");   // R54: also with group name ("threat, north lead group BRAA …")
        if (m.Success) return ("threat " + m.Groups[1].Value.TrimEnd('.'), true);
        if (Regex.IsMatch(text, @"\bmerged\.$")) return ("merged", true);   // R291: also "north group merged."
        if (text.Contains(", furball, ", StringComparison.Ordinal)) return ("furball", true);   // LK8/LK16: furball and leaker go stale like threat
        if (Regex.Match(text, @", leaker, (.*)") is { Success: true } lk) return ("leaker " + lk.Groups[1].Value.TrimEnd('.'), true);
        if (text.Contains(", radar contact, ", StringComparison.Ordinal)) return ("check-in", false);
        if (text.Contains(", copy checking out", StringComparison.Ordinal)) return ("check-out", false);
        return null;
    }

    static void AwacsNote(Pilot p, Ops.Call c)
    {
        if (c.Role != "AWACS" || AwacsRow(c.Text) is not { } r || p.Ops.Debrief.Count(d => d.StartsWith("AWACS ")) >= 10) return;
        if (r.Row == "check-in" && p.Ops.Debrief.LastOrDefault(d => d.StartsWith("AWACS ") && (d.EndsWith(": check-in") || d.EndsWith(": check-out"))) is { } last && last.EndsWith(": check-in")) return;   // "radar contact" after takeoff, on "splash"/"committing": no second check-in until check-out
        p.Ops.Debrief.Add($"AWACS {Clock:HH:mm:ss}: {r.Row}");
    }

    /// Pilot voice slightly different per player/AI flight (pitch) so they can be told apart.
    static string PilotVoiceFor(string key)
    {
        var model = Cfg.PilotVoice.Split('@')[0];
        double[] pitch = { 1.0, 0.92, 1.08, 0.96, 1.04 };
        return FormattableString.Invariant($"{model}@{pitch[key.Sum(ch => ch) % pitch.Length]}");
    }

    /// "a|b" in Voices: per station (e.g. "Senaki Tower") one of them fixed, airfields alternate.
    /// If the model is missing (install without en_GB-alan), only those present apply, otherwise ryan: better another voice than a mute AWACS.
    static string PickVoice(string voices, string station)
    {
        installed ??= Directory.Exists(Path.Combine(Root, "models")) ? Directory.GetFiles(Path.Combine(Root, "models"), "*.onnx").Select(m => Path.GetFileNameWithoutExtension(m)).ToArray() : Array.Empty<string>();
        var v = VoicePool(voices, installed, Cfg.PilotVoice.Split('@')[0]);
        return v[(station.Sum(ch => ch) + (voices.Contains("random") ? VoiceSeed : 0)) % v.Length];
    }
    static string[]? installed;
    static readonly int VoiceSeed = Random.Shared.Next(1000);   // "random": another draw each start, fixed per station while the app runs
    /// "random" (also "random@0.95") stands for every installed voice except the pilot's, so controller and pilot stay apart.
    internal static string[] VoicePool(string voices, string[] installed, string pilot) =>
        voices.Split('|').SelectMany(x => x.Split('@')[0] == "random" ? installed.Where(m => m != pilot || installed.Length == 1).Select(m => m + x[6..]) : new[] { x })
              .Where(x => installed.Contains(x.Split('@')[0], StringComparer.OrdinalIgnoreCase)).DefaultIfEmpty("en_US-ryan-medium").ToArray();

    /// Settings window: speak a sample with this voice ("a|b@0.9", "random") on the PC speakers, without SRS.
    internal static void Preview(string voices)
    {
        Directory.CreateDirectory(Path.Combine(Root, "tmp"));
        var mp3 = Path.Combine(Root, "tmp", "probe.mp3");
        Synth("Kutaisi Tower, radio check, read you five by five.", PickVoice(voices, Random.Shared.Next(99).ToString()), mp3);
        lock (Pipers) { foreach (var p in Pipers.Values) try { p.P.Kill(); } catch (Exception) { } Pipers.Clear(); }   // the settings window leaves no warm Piper behind
        using var r = new Mp3FileReaderBase(mp3, wf => new AcmMp3FrameDecompressor(wf));
        using var o = new WaveOutEvent();
        o.Init(r); o.Play();
        while (o.PlaybackState == PlaybackState.Playing) Thread.Sleep(50);
    }

    /// FI4: "request checkride" (speech; wheel/F10 "checkride" toggles) starts it at the active airfield within 10 NM, "cancel checkride" ends it; instructor on the intercom.
    static Tx CheckrideReq(Pilot p, string spoken)
    {
        if (Regex.IsMatch(spoken, @"\b(?:cancel|stop|abort|terminate|end)\b", RegexOptions.IgnoreCase) || spoken.Trim() == "checkride" && p.Ride != null)
        { p.Ride = null; return InstructorTx(p, "Checkride cancelled."); }
        if (p.Lead != null) return InstructorTx(p, "Checkride only flying solo, not in formation.");
        if (p.Active is not { } tw || tw.F.Ends.Count == 0 || p.Tel is not { } t || Dist(t.X, t.Z, tw.F.X, tw.F.Z) > 10 * NM) return InstructorTx(p, "For a checkride, select an airfield within ten miles.");
        p.Ride = new Checkride(tw.F, tw.Runway);
        return InstructorTx(p, p.Ride.Intro());
    }
    static Tx InstructorTx(Pilot p, string text) => new(text, "Crew", "Instructor", p.Gid, Unit: p.Id, Side: p.Coalition);

    /// FI6: helicopter "will pick up" / "request CSAR" (wheel CSAR: will pick up) takes the nearest survivor of the side; "smoke" / "flare" to the survivor
    /// (his call sign, "survivor" or wheel CSAR:) gets orange smoke by day, a red flare at night. null = no CSAR call.
    static List<Tx>? CsarReq(Pilot p, string? to, string spoken)
    {
        var n = Tower.Normalize(spoken);
        var mine = Csar.Of(p.Unit);
        bool sig = mine != null && Regex.IsMatch(n, @"\b(?:smoke|flare)\b") && (to == "CSAR" || n.Contains("survivor") || n.Contains(mine.Cs.Split(' ')[0].ToLowerInvariant()));
        if (!sig && !Regex.IsMatch(n, @"\b(?:will pick ?up|request csar|csar pick ?up|will rescue|pick (?:him|her|them) up|picking up (?:the )?(?:survivor|downed pilot|pilot)|(?:proceeding|en ?route|inbound) to (?:the )?(?:survivor|downed pilot|pick ?up))\b")) return null;
        if (p.Tel is not { } t) return new();
        var hc = Tower.SpokenCallsign(p.Callsign);
        if (sig)
        {
            if (Csar.Signal(mine!, t, Now(), Night() != null) is not { } k)
                return new() { SurvivorTx(p, mine!, $"{hc}, {mine!.Cs}, " + (Dist(t.X, t.Z, mine.X, mine.Z) > 5 * NM ? "negative, I don't hear you yet." : "signal is out, look for it.")) };
            AiCmd(FormattableString.Invariant($"SIGNAL;{mine!.X:0};{mine.Z:0};{k}"));
            return new() { SurvivorTx(p, mine, k == "flare" ? $"{hc}, {mine.Cs}, flare out." : $"{hc}, {mine.Cs}, popping smoke.") };
        }
        if (!p.Heli) return new() { CsarCrewTx(p, "CSAR pickup needs a helicopter.") };
        if (Csar.Take(p.Unit, p.Coalition, t.X, t.Z, Now()) is not { } s) return new() { CsarCrewTx(p, "No downed pilot known.") };
        s.Called = true;
        return new() { FlightWorld().Awacs(p.Coalition, t.X, t.Z) is { } aw
            ? new Tx($"{hc}, {aw.Name}, copy, downed pilot {s.Cs}, {Csar.Bra(s, t.X, t.Z)}. Contact him on guard.", "AWACS", aw.Name, p.Gid, Freqs: Guards, Side: p.Coalition)
            : SurvivorTx(p, s, $"{hc}, {s.Cs}, read you, waiting for pickup, {Csar.Bra(s, t.X, t.Z)} from you.") };
    }
    static readonly double[] Guards = { Flights.Guard, Flights.GuardVhf };
    static Tx CsarCrewTx(Pilot p, string text) => new(text, "Crew", "Crew", p.Gid, Unit: p.Id, Side: p.Coalition);   // intercom, not the crew chief module
    static Tx SurvivorTx(Pilot p, Csar.Survivor s, string text) => new(text, "Survivor", s.Cs, p.Gid, true, AiVoiceFor(s.Unit), Freqs: Guards, Side: s.Side);

    /// FI6 per tick: helicopters gone release their survivor, a player survivor flying again is dropped, AWACS calls the helicopters once per survivor,
    /// the survivor and the crew chief talk to the helicopter that took him; rescued -> debriefing (logbook).
    static IEnumerable<Tx> CsarTick(List<Pilot> active)
    {
        var helis = active.Where(p => p.Heli).ToList();
        Csar.Release(helis.Select(p => p.Unit).ToHashSet(), Now());
        Csar.All.RemoveAll(s => !s.Aboard && active.Any(q => q.Unit == s.Unit && q.Tel != null && Dist(q.Tel.X, q.Tel.Z, s.X, s.Z) > NM));   // like R361: respawned
        foreach (var side in helis.Select(p => p.Coalition).Distinct())
            foreach (var s in Csar.ToCall(side, Now()).ToList())
            {
                s.Called = true;
                if (FlightWorld().Awacs(side, s.X, s.Z) is { } aw)
                    yield return new Tx($"Any helicopter, {aw.Name}, downed pilot {s.Cs}{(s.Bulls != "" ? ", " + s.Bulls : "")}.", "AWACS", aw.Name, 0, Freqs: Guards, Side: side);
            }
        foreach (var p in helis)
        {
            if (Csar.Of(p.Unit) is not { } s || p.Tel is not { } t) continue;
            foreach (var (who, text) in Csar.Tick(s, Tower.SpokenCallsign(p.Callsign), t, Now())) yield return who == "S" ? SurvivorTx(p, s, text) : CsarCrewTx(p, text);
            if (s.Rescued) p.Ops.Debrief.Add(L($"CSAR: {s.Cs} gerettet, {(Now() - s.At) / 60:0} min nach dem Ausstieg", $"CSAR: {s.Cs} rescued, {(Now() - s.At) / 60:0} min after the ejection"));
        }
    }

    /// FI3: tags before the first colon of a debrief line for the logbook: "Landing grade Kutaisi runway 25 (night): …", "Carrier LSO (Case III, night): …".
    internal static string Tag(string d, params string?[] tags) =>
        (tags ?? Array.Empty<string?>()).OfType<string>().ToList() is { Count: > 0 } t && d.IndexOf(':') is var i and > 0 ? d.Insert(i, $" ({string.Join(", ", t)})") : d;   // Tag(d, null): params array itself null
    /// Night after the end of civil twilight (sun below -6°, at the carrier or the first airfield, Y line); unknown sun = day.
    static string? Night() => Carrier.Sun < -6 ? L("Nacht", "night") : null;

    /// Debrief: landings, traffic patterns, range, tanker, rejected radio calls. final -> file in debrief\ and reset.
    static Tx DebriefTx(Pilot p, bool final)
    {
        var lines = new List<string> { $"DEBRIEFING {p.Callsign} ({p.Type})" + (p.Airborne is { } a ? L(" – Flugzeit ", " – flight time ") + $"{Clock - a:h\\:mm}" : "") };
        lines.AddRange(p.Ops.Debrief.Count > 0 ? p.Ops.Debrief : new List<string> { L("Noch keine Wertungen (Landung, Range, Tanker).", "No grades yet (landing, range, tanker).") });
        lines.AddRange(p.Ops.DebriefSummary());
        int rej = p.Towers.Values.Sum(t => t.Rejected) + (p.Lead?.Towers.Values.Sum(t => t.Rejected) ?? 0);
        if (rej > 0) lines.Add(L("Vom Lotsen abgelehnte Funksprüche: ", "Calls rejected by the controller: ") + rej);
        if (final && !TestRun && p.Airborne is { } fa)   // FI3: the flight into the logbook, milestones and totals to the end
            lines.AddRange(Logbook.Add(Path.Combine(Root, "logbook.json"), p.Name != "" ? p.Name : p.Callsign, p.Type, (Clock - fa).TotalMinutes, p.Ops.Debrief, p.Ops.TankerContacts, p.Ops.RangePasses));
        var text = string.Join("\n", lines);
        if (final)
        {
            try
            {
                var dir = Path.Combine(Root, "debrief");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{Clock:yyyy-MM-dd_HHmm}_{Regex.Replace(p.Callsign, "[^A-Za-z0-9-]", "")}.txt"), text);
            }
            catch (IOException) { }
            p.Ops.ResetDebrief();
            (p.Airborne, p.AtcAt) = (null, -1e9);   // N51: next flight unknown again until the first call
        }
        return new Tx(text, "Info", "", p.Gid);
    }

    /// Fellow player without own mission: radio wheel only. Requests go via the DCS hook (DcsAtcHook.lua) as chat to the host.
    static int ClientMode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DcsAtc-Chat");
        Directory.CreateDirectory(dir);
        int n = 0;
        Wheel.Jtac = () => false;   // the wheel shows 9 entries: without mission data Carrier instead of JTAC (JTAC by voice over SRS)
        Wheel.Start(Cfg, text =>
        {
            try
            {
                var f = Path.Combine(dir, $"{Clock.Ticks}-{Interlocked.Increment(ref n)}");
                File.WriteAllText(f + ".tmp", text);
                File.Move(f + ".tmp", f + ".txt");
                Console.WriteLine($"-> {text}");
            }
            catch (IOException) { }
        }, () => null);
        Console.WriteLine(L($"DCS-ATC Mitspieler: Funkrad mit Taste {Wheel.KeyName(Cfg.WheelKey)} (DCS vorn). Anfragen gehen per DCS an den Host.",
                            $"DCS-ATC client: radio wheel with key {Wheel.KeyName(Cfg.WheelKey)} (DCS in front). Requests go to the host via DCS."));
        Console.WriteLine(L("Sprechen: einfach über SRS auf der Lotsen-Frequenz. Fenster offen lassen.", "Talk: just use SRS on the controller frequency. Keep this window open."));
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// Range events of the mission: R;unit;waffe;abstand;uhr;ziel;flugzeit  H;unit;ziel (gun hit)  K;unit;ziel;koalition (destroyed)
    static void OnEvent(string line)
    {
        var e = line.Split(';');
        if (e.Length < 2) return;
        if (e[0] == "WING")   // DcsAtcHook.lua: wingman command performed ("ok") or why not
        {
            if (e[1] == "ok") return;
            Log($"[DCS] Wingman-Befehl nicht gesendet: {e[1]} ({string.Join(";", e.Skip(2))})");
            lock (TowerLock) SayQueue.Add(new Tx(L($"Wingman: Befehl nicht gesendet ({e[1]}). Nimm das DCS-Funkmenü.", $"Wingman: command not sent ({e[1]}). Use the DCS radio menu."), "Info", "", Host()?.Gid ?? 0));
            return;
        }
        if (e[0] == "X") { lock (TowerLock) try { Ops.OnEvent(e, Now()); foreach (var q in Pilots.Values) q.Ops.OnCasEvent(e, q.Unit, Now()); Flights.OnEvent(e, Now(), FlightWorld()); } catch (FormatException x) { Trace("FEHLER", $"Ereignis {line}: {x.Message}"); } return; }   // AI radio
        List<Ops.Call> calls = new();
        Pilot? p;
        lock (TowerLock)
        {
            p = Pilots.GetValueOrDefault(e[1]);
            if (p == null) return;
            if (e[0] == "R" && e.Length >= 5) calls = p.Ops.OnImpact(MeOf(p), double.Parse(e[3], CultureInfo.InvariantCulture), int.Parse(e[4]), Now(), e[2],
                e.Length > 6 && double.TryParse(e[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var fl) ? fl : -1);   // flight time (older mission: no seventh field)
            else if (e[0] == "H") p.Ops.OnGunHit();
            else if (e[0] == "W" && e.Length >= 3 && int.TryParse(e[2], out var wire)) { p.Boat.OnWire(wire); Log($"[LSO] {p.Callsign}: DCS-Wertung {(e.Length > 3 ? e[3] : "")} (Wire {wire})"); }   // R27: LANDING_QUALITY_MARK
            else if (e[0] == "K") calls = p.Ops.OnKill(MeOf(p), Now(), e.Length > 3 && int.TryParse(e[3], out var kc) ? kc : -1);   // R369: fourth field = coalition of the destroyed object (older mission: none)
            else if (e[0] == "F" && e.Length >= 4)   // Aerial refueling
            {
                var kg = e.Length > 4 && double.TryParse(e[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var k) ? k : 0;   // internal tank in kg (older mission: no fifth field)
                calls = p.Ops.OnRefuel(MeOf(p), e[2] == "start", double.Parse(e[3], CultureInfo.InvariantCulture), TrafficFor(p), Now(), kg);
            }
            else if (e[0] == "M" && e.Length >= 5 && p == Host())   // No 0: mission gives the reason (daylight, airfield not in the DCS list, DCS dialog open …)
            {
                int mi = int.Parse(e[3]);
                if (e[2] == "tanker") p.Ops.DcsIntent = mi is >= 1 and <= 10 && Cfg.CommsKey != 0;   // rejected: "pre contact" asks the mission again instead of F1 directly
                if (mi is >= 1 and <= 10 && Cfg.CommsKey != 0)
                {
                    Log($"[DCS] Funkmenü {e[2]} {mi}/{e[4]}");
                    Wheel.PressDcs(MenuKeys(e[2], mi, int.Parse(e[4])));
                    if (e[2] == "tanker") p.TankMenuAt = Now();   // R304: DCS then tunes the tanker frequency itself (Pick)
                }
                else if (mi == 0)
                {
                    Trace("DCS", $"Funkmenü {e[2]} nicht gedrückt: {e[4]}");
                    if (e[2] == "tanker")   // Forum (fabio.dangelo): the controller says "boom ready", without the menu DCS never extends it: say so in the game
                    {
                        Log($"[DCS] Funkmenü tanker nicht gedrückt: {e[4]}");
                        SayQueue.Add(new Tx(L("Tanker: DCS-Funkmenü nicht gedrückt, Korb/Boom bleiben aus. \"Intent to refuel\" und \"Ready pre-contact\" selbst senden: Menütaste deines Funkgeräts -> Tanker.",
                                              "Tanker: DCS radio menu not pressed, basket/boom stay off. Send \"Intent to refuel\" and \"Ready pre-contact\" yourself: your radio's menu key -> Tanker."), "Info", "", p.Gid));
                    }
                }
                else Log($"[DCS] Funkmenü {e[2]} nicht gedrückt: {(mi > 10 ? $"Nr. {mi}, DCS listet nur 10" : "keine Funkmenü-Taste (CommsKey 0)")}");
            }
            else if (e[0] == "C" && e.Length >= 3 && e[2] == "stopped") p.Active?.EngineOff(p.Tel);   // R6/N25: engine off at the parking spot after taxi-in = parked (debrief in TickAll), not on the taxiway; R107: also before departure at the parking spot
            else if (e[0] == "C" && e.Length >= 3 && CrewLines.TryGetValue(e[2], out var crewLine))
            {
                p.Crew = true;
                SayQueue.Add(CrewTx(p, crewLine));
                if (e[2] == "fuel") SayQueue.Add(new Tx(L("Tanken/Bewaffnen über das DCS-Bodenpersonal (\\ -> F8).", "Refuel/rearm via the DCS ground crew (\\ -> F8)."), "Info", "", p.Gid));   // A37: text only, not spoken
            }
        }
        foreach (var c in calls) SayQueue.Add(OpsTx(p, c));
    }

    /// Crew chief (intercom): ground crew events from the mission and phase changes.
    static readonly Dictionary<string, string> CrewLines = new()
    {
        ["crew"] = "Crew chief on the headset, ready when you are.",   // "fire guard posted" only once (A93): at the start-up clearance
        ["back"] = "Welcome back! Chocks in, clear to shut down.",
        ["fuel"] = "Fuel and ammo trucks standing by.",   // A37: the trucks are scenery, refueling goes through the DCS ground crew (hint as text in the event)
        ["StartupApproved"] = "Clear on the left, clear on the right, fire guard posted. Cleared to start engines.",
        ["TaxiOut"] = "Chocks removed, pins pulled. You're clear of the area, have a good flight!",   // taxi clearance is given by the controller, not the crew chief (A93)
    };

    static Tx CrewTx(Pilot p, string text) => new(text, "Crew", "Crew Chief", p.Gid, Unit: p.Id, Side: p.Coalition);

    /// Self-test: crew chief (A37, A93) – no scenery refueling on the radio, "fire guard" only once, "clear of the area" instead of "clear to taxi".
    static void CrewTest()
    {
        var p = new Pilot { Unit = "CREW1", Gid = 7, Id = 3 };
        Pilots[p.Unit] = p;
        while (SayQueue.TryTake(out _)) { }
        foreach (var ev in new[] { "crew", "back", "fuel", "done" }) OnEvent($"C;{p.Unit};{ev}");   // the app no longer knows "done"
        Pilots.Remove(p.Unit);
        var tx = new List<Tx>();
        while (SayQueue.TryTake(out var t)) tx.Add(t);
        var crew = tx.Where(t => t.Role == "Crew").ToList();
        var info = tx.Where(t => t.Role == "Info").ToList();
        var all = string.Join(" ", CrewLines.Values);
        if (crew.Count != 3 || crew.Any(t => t.Unit != 3 || t.Gid != 7) || info.Count != 1 || info[0].Gid != 7 || !info[0].Text.Contains("\\ -> F8")
            || CrewLines.ContainsKey("done") || Regex.IsMatch(all, "on the way|Refuel and|Refueling|rearming|clear to taxi") || Regex.Matches(all, "fire guard").Count != 1
            || !CrewLines["TaxiOut"].Contains("clear of the area") || CrewLines["fuel"] != "Fuel and ammo trucks standing by.")
            throw new Exception($"Crew Chief: {string.Join(" | ", tx.Select(t => $"{t.Role}:{t.Text}"))}");
        Console.WriteLine("OK   Crew Chief: Lkw \"standing by\" mit Text \"\\ -> F8\" statt Tanken im Funk, kein \"done\", \"fire guard\" einmal, \"clear of the area\"");
    }

    /// Request from a player (speech, F10 menu, selection wheel). Menu texts: "Ground: request startup".
    static void Request(Pilot? p, string text, string src, bool fromMenu, Airfield? via = null, double conf = 1)
    {
        if (p == null) { Log($"[{src}] {text}   (eigenes Flugzeug nicht erkannt)"); return; }
        var outq = new List<Tx>();
        lock (TowerLock)
        {
            Log($"[{src}] {p.Callsign}: {text}" + (conf < 1 ? $"   ({conf:0.00})" : "") + (p.Tel == null ? "   (keine Telemetrie von DCS)" : ""));
            Trace("ANFRAGE", $"{src} {p.Callsign} (unit {p.Id}) via {via?.Name ?? "-"} menu {fromMenu} conf {conf:0.00} aktiv {(p.Lead ?? p).Active?.F.Name ?? "-"} {(p.Lead ?? p).Active?.Phase} Pin {(p.Lead ?? p).Pinned?.F.Name ?? "-"}: {text}");
            tracedReq[p.Unit] = (text, Now());
            int colon = text.IndexOf(':');
            string? to = colon > 0 ? text[..colon].Trim() : null;
            var spoken = colon > 0 ? text[(colon + 1)..].Trim() : text;
            if (fromMenu && spoken == "report airspeed" && p.Tel is { } it) (spoken, text) = (Tower.IasSay(it), $"{to ?? "Approach"}: {Tower.IasSay(it)}");   // R129: radio wheel "Fahrt melden" -> "340 knots"
            if (fromMenu && to == "JTAC" && p.Ops.CasWheel(spoken) is { } cw) (spoken, text) = (cw, "JTAC: " + cw);   // J11: readback with lines 4/6, IN / OFF with direction
            if (Regex.IsMatch(spoken, @"^\s*debrief", RegexOptions.IgnoreCase)) { SayQueue.Add(DebriefTx(p, false)); return; }
            if (Regex.IsMatch(spoken, @"\bcheck ?ride\b|\bcheck flight\b", RegexOptions.IgnoreCase)) { SayQueue.Add(CheckrideReq(p, spoken)); return; }
            if (CsarReq(p, to, spoken) is { } cr) { foreach (var x in cr) SayQueue.Add(x); return; }
            if (Regex.IsMatch(spoken, @"\b(?:fox (?:one|two|three)|defensive|defending)\b", RegexOptions.IgnoreCase)) HotUntil = Clock.AddSeconds(5);   // LD3: radio pause for picture/replies
            if (!fromMenu && p == Host() && WingCmd(Tower.Normalize(spoken), p.Callsign) is { } wc)   // forum (jojojung): own AI wingmen, only on this PC (its DCS radio menu)
            {
                Log($"[DCS] Wingman {(wc.Par == 5 ? "Flight" : (wc.Par + 1).ToString())}: {wc.Msg}");
                try
                {
                    File.WriteAllText(WingFile + ".tmp", $"{wc.Par};{wc.Msg}"); File.Move(WingFile + ".tmp", WingFile, true);
                    var gid = p.Gid;
                    _ = Task.Delay(3000).ContinueWith(_ =>   // DcsAtcWing.lua polls every 0.3 s; still there = autoexec.lua not patched or DCS not restarted since
                    {
                        try { if (!File.Exists(WingFile)) return; File.Delete(WingFile); } catch (IOException) { return; }
                        Log($"[DCS] Wingman-Befehl nicht abgeholt: DcsAtcWing.lua nicht geladen (autoexec.lua)");
                        lock (TowerLock) SayQueue.Add(new Tx(L("Wingman: Befehl nicht angekommen. DCS nach der Installation neu starten, sonst Startmenü: DCS-ATC, Nach DCS-Update reparieren.",
                                                               "Wingman: command not received. Restart DCS after installing, otherwise Start menu: DCS-ATC, Repair after DCS update."), "Info", "", gid));
                    });
                }
                catch (IOException e) { Trace("FEHLER", $"Wingman-Befehl {wc.Msg}: {e.Message}"); }
                return;
            }
            if (!fromMenu && MissionData && Flights.Ask(Tower.Normalize(spoken), p.Coalition, Now(), FlightWorld())) return;   // LK15: "Ford one, say status" to an AI flight (reply comes via FlightComms)
            p.AtcAt = Now();   // N2: call to a controller (airfield, carrier, AWACS, tanker, range)
            var ask = new[] { p, p.Lead }.Select(x => x?.Expect).FirstOrDefault(x => x is { } v && v.Until > Now());   // LA10: open question (as Expected)
            p.Expect = (p.Lead ?? p).Expect = null;   // answered (radio wheel Enter)
            var sn = Tower.Normalize(spoken);   // R360: "Pan-pan", "May day" as transcribed
            bool distress = Regex.IsMatch(sn, @"\b(?:mayday|pan pan|declaring emergency|emergency fuel)\b") && !Regex.IsMatch(sn, @"\bcancel\b");
            // R293: emergency of a wingman: own sequence (reply to his call sign, emergency stays with him, FAA JO 7110.65 2-1-13); FormFlights does not reattach him until the emergency ends
            if (distress && p.Lead is { } dl)
            {
                Log($"[ATC] {p.Callsign}: Notruf im Flug von {dl.Callsign} -> eigener Ablauf");
                p.Lead = null; p.Towers.Clear(); p.Active = null;
                (p.Pinned, p.PinFreqs) = dl.Pinned is { } lp ? (TowerFor(p, lp.F), dl.PinFreqs) : (null, null);   // the flight's selected airfield still applies
                dl.FlightSize = Math.Max(1, dl.FlightSize - 1);
            }
            // carrier: frequency/menu ("Carrier: ..."), Marshal/Paddles/Ball in the radio call or selected in the radio wheel
            if (Regex.Match(spoken, @"^switch (.+)$") is { Success: true } sb && Carrier.Boats.Any(b => b.Unit == sb.Groups[1].Value))
            {
                (p.Lead ?? p).OnBoat = sb.Groups[1].Value;
                SayQueue.Add(new Tx(L("ATC: jetzt ", "ATC: now ") + $"{sb.Groups[1].Value} (Marshal)", "Info", "", p.Gid));
                return;
            }
            // ground/tower/approach request on a station frequency (mission preset): no station action (no DCS tanker menu), no "say again", hint to the airfield frequency
            if (!fromMenu && to is "Tanker" or "AWACS" or "Carrier" or "Range" or "JTAC" && On("atc") && Fields.Count > 0 && FieldRole(sn) is { } fr && !(to == "Carrier" && fr == "Approach") && !p.Boat.OnDeck   // R24: no airfield on the deck, the carrier gives the deck hint
                && Ops.RoleOf(sn, p.Callsign) == null && !p.Boat.Wants(sn) && !Tower.Has(sn, "vector", "home plate", "bingo", "nearest airfield", "recovery", "divert"))   // AWACS: "bingo, vectors … full stop" stays with the AWACS
            {
                var wf = (p.Lead ?? p).Active?.F ?? Pick(p.Lead ?? p, spoken).F;
                string Mhz(double x) => x.ToString("0.0##", CultureInfo.InvariantCulture);
                var on = Mhz(p.CalledOn > 0 ? p.CalledOn : Cfg.Frequencies.GetValueOrDefault(to));
                var ok = $"{wf.StationOf(fr)}: {string.Join(" / ", (FieldFreqs(wf, fr) ?? new[] { Cfg.Frequencies.GetValueOrDefault(fr) }).Select(Mhz))}";
                SayQueue.Add(new Tx(L($"Du funkst auf {to} {on}. {ok}", $"You are transmitting on {to} {on}. {ok}"), "Info", "", p.Gid));
                return;
            }
            if (On("carrier") && (to == "Carrier" || p.Boat.OnDeck && via == null && to is null or "Ground" or "Tower" or "Approach" && Ops.RoleOf(Tower.Normalize(text), p.Callsign) == null
                || p.Boat.Launching && via == null && to == "Approach" && p.Boat.Wants(Tower.Normalize(text))   // R24: radio wheel "Approach: airborne" after the catapult -> carrier Departure
                || to == null && via == null && (p.Boat.Wants(Tower.Normalize(text)) || (p.Lead ?? p).OnBoat != null && Ops.RoleOf(Tower.Normalize(text), p.Callsign) == null)))
            {
                if (fromMenu && Regex.Match(spoken, @"^(marshal|strike|red crown), checking (in|out)$", RegexOptions.IgnoreCase) is { Success: true } km && (km.Groups[2].Value == "in" ? CheckIn(p) : "checking out") is { } ci) (spoken, text) = (ci, $"Carrier: {km.Groups[1].Value}, {ci}");   // Strike / Red Crown: position, angels, state as at Marshal (check-in); checking out without figures
                if (fromMenu && Regex.Match(spoken, @"^(ball|clara)$", RegexOptions.IgnoreCase) is { Success: true } bm) (spoken, text) = (BallCall(p, bm.Value), "Carrier: " + BallCall(p, bm.Value));   // R28
                var bc = p.Boat.OnTranscript(text, MeOf(p), Now(), conf < MinConf);   // uncertain recognition: ask back (R16)
                if (fromMenu && Cfg.PilotVoice != "" && bc.Count > 0 && bc[0].Role != "Info")   // R134: hint without station (zip lip) is not a radio call
                    outq.Add(new Tx(CarrierCall(bc[0].Station, p.Callsign, spoken), "Carrier", p.Callsign, p.Gid, true, PilotVoiceFor(p.Unit), Freqs: OpsTx(p, bc[0]).Freqs, Prio: 0, Side: p.Coalition));
                outq.AddRange(bc.Select(c => OpsTx(p, c)));
                Send(p, outq);
                return;
            }
            // Range / AWACS / tanker: frequency or menu ("Range: ...") or name in the radio call
            var opsRole = to is "Range" or "AWACS" or "Tanker" or "JTAC" ? to : to == null ? OpsByContent(p, text, via) : null;
            if (opsRole == null && to == null && p.LastOps is { } lo && Now() - lo.At < 300 && Ops.BareSayAgain(sn, p.Callsign)) opsRole = lo.Role;   // R393: the controller who spoke last repeats
            if (opsRole != null && Off(opsRole.ToLowerInvariant(), text)) return;
            if (opsRole != null)
            {
                var calls = p.Ops.OnTranscript(opsRole, text, MeOf(p) with { Freq = src == "SRS" ? p.CalledOn : 0 }, TrafficFor(p), Fields, Now(), conf < MinConf, Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5), FieldWxOf);
                if (p.Ops.Menu is { } mn && Cfg.CommsKey != 0 && MissionData && p == Host())   // basket/boom only via the DCS menu: "Intent to refuel" (mission looks it up in the list) or "Ready pre-contact"
                {
                    if (mn.Step == "precontact") { Log("[DCS] Funkmenü tanker Ready pre-contact"); Wheel.PressDcs((ushort)Cfg.CommsKey, 0x3B); p.TankMenuAt = Now(); }
                    else AiCmd($"MENU;{p.Unit};tanker;{mn.Group};s");   // R304: s = DCS tanker stays mute, our controller speaks (without s: audible 15 s like the airfields); mission refuses without Easy Communication (issue #5)
                }
                p.Ops.Menu = null;
                foreach (var cm in p.Ops.Cmds.ToList()) AiCmd(cm);   // J4/J6: MARK / LASE / LASEOFF
                p.Ops.Cmds.Clear();
                if (fromMenu && Cfg.PilotVoice != "" && calls.Count > 0)
                    outq.Add(new Tx($"{calls[0].Station}, {p.Callsign.Replace('-', ' ')}, {spoken}.", opsRole, p.Callsign, p.Gid, true, PilotVoiceFor(p.Unit), Freqs: OpsTx(p, calls[0]).Freqs, Prio: 0, Side: p.Coalition));
                foreach (var c in calls) AwacsNote(p, c);
                outq.AddRange(calls.Select(c => OpsTx(p, c)));
                if (calls.Count > 0) p.LastOps = (opsRole, Now());
                if (opsRole == "Tanker" && Tower.Has(Tower.Normalize(spoken), "complete", "departing", "thank", "good day", "leaving")) p.TankFq = 0;   // R387: departed (the reply above still goes out on the tanker frequency)
                if (p.Ops.Divert is { } dv)   // A15: MAYDAY/PAN to the AWACS -> its airfield like "Platz wählen", Approach takes over the emergency
                {
                    p.Ops.Divert = null;
                    var dq = p.Lead ?? p;
                    var dt = TowerFor(dq, dv);
                    (dq.Pinned, dq.PinFreqs, dq.OnBoat) = (dt, null, null);
                    p.Boat.Leave();   // like "Platz wählen": out of the Marshal stack, otherwise the next calls would go to Marshal
                    Switch(dq, dt);
                    Prep(dq, dt, ActivePilots().Where(x => x.Lead == null).ToList());
                    dt.Announce(dq.Tel, Now(), !Tower.Has(Tower.Normalize(spoken), "pan pan") || Tower.Has(Tower.Normalize(spoken), "mayday"));   // R250: Approach speaks only on the first call
                }
                Send(p, outq);
                return;
            }
            if (Off(to == "Center" ? "center" : "atc", text)) return;
            var q = p.Lead ?? p;   // flight: request applies to the flight
            if (Regex.Match(spoken, @"^switch (.+)$") is { Success: true } sw && Fields.FirstOrDefault(f => f.Name == sw.Groups[1].Value) is { } pf)
            {
                if (!Served(q.Coalition, pf)) { Hostile(p, pf); return; }
                (q.Pinned, q.PinFreqs) = (TowerFor(q, pf), null);   // radio wheel "Platz wählen": beats the tuned frequency until landing
                q.OnBoat = null;
                p.Boat.Leave();   // landing airfield selected (e.g. bingo alternate): out of the Marshal stack, Enter from the tower again
                Switch(q, q.Pinned);
                SayQueue.Add(new Tx(L("ATC: jetzt ", "ATC: now ") + pf.Name, "Info", "", p.Gid));
                return;
            }
            // N1: enemy airfield (called on its frequency or by name) does not answer, only a hint; MAYDAY/PAN there goes to an own airfield
            if ((via != null && !Served(q.Coalition, via) ? via : Called(text, q.Tel, via) is { } hc && !Served(q.Coalition, hc) ? hc : null) is { } hf)
            {
                if (!distress) { Hostile(p, hf); return; }
                via = null;
            }
            if (via != null) (q.Pinned, q.PinFreqs) = (TowerFor(q, via), Srs?.TunedFreqs(q.Id));   // called on its frequency: this airfield (SRS state may be older than the call)
            var other = via != null && Called(text, q.Tel, via) is { } cf && cf != via ? cf : null;   // called a different airfield there: frequency hint, no change
            // emergency without called or selected airfield and without tuned airfield frequency: nearest suitable own airfield, valid until landing (enemy ones do not count, N1)
            if (distress && via == null && q.Pinned == null && q.Tel is { Agl: > 30 } et && q.Active?.Emergency != true && !(Called(text, et, null) is { } dc && Served(q.Coalition, dc)) &&
                !(Srs?.TunedFreqs(q.Id) is { } tf && Fields.Any(f => Served(q.Coalition, f) && FieldFreqs(f, "*") is { } fs && fs.Any(x => tf.Any(m => Math.Abs(m - x) < 0.005)))) &&
                EmergencyField(et, q.Coalition) is { } ef)
                (q.Pinned, q.PinFreqs) = (TowerFor(q, ef), null);
            if (Pick(q, via == null ? text : null) is not { } pt) { Trace("PICK", $"{q.Callsign}: kein eigener/neutraler Platz"); SayQueue.Add(new Tx(L("Keine Flugsicherung: kein eigener oder neutraler Platz.", "No ATC: no friendly or neutral airfield."), "Info", "", p.Gid)); return; }
            Trace("PICK", $"{q.Callsign}: {pt.F.Name} ({pickWhy = PickWhy(q, via == null ? text : null, pt) + (via != null ? $", auf Platzfrequenz {via.Name}" : "")})");
            Switch(q, pt);
            pickWhy = null;
            Prep(q, q.Active!, ActivePilots().Where(x => x.Lead == null).ToList());   // newly called airfield: weather/sequence already for the reply
            var tw = q.Active!;
            List<Msg> msgs;
            p.LastOps = null;   // R393: the airfield spoke last
            tw.CallFreq = src == "SRS" ? p.CalledOn : 0;   // LA11: the controller of this frequency answers
            var intent = tw.IntentOf(text, q.Tel);   // Debug log
            if (conf < MinConf && (tw.Plain(text) || conf >= ExpConf && tw.ReadbackOnly(text, q.Tel))) conf = MinConf;   // "copied"/"say again": closed phrase, no ask-back; #23: a readback needs no action, no ask-back either
            // LA10 (log Nellis): intent = the expected next call (radio wheel Enter: answer to the open question, otherwise the procedure) -> accepted from 0.50 ("Overhead." 0.60)
            if (conf is < MinConf and >= ExpConf && intent != "unknown")
            {
                var flow = tw.Suggest(q.Tel);
                if ((ask is { } ak && Answer(p, ak.Role, ak.Ask, flow) is { } ans ? ans : flow?.Text) is { } ex && tw.IntentOf(ex, q.Tel) == intent) conf = MinConf;
            }
            if (Regex.IsMatch(text, @"^\s*atis\s*$", RegexOptions.IgnoreCase))
            {
                if (MissionData) UpdateAtis(tw.F);   // R120: without fresh mission data no weather from the previous mission
                msgs = new() { Atis.TryGetValue(tw.F.Name, out var a) && a.Text != "" ? new Msg("ATIS", a.Text)
                                                                                     : new Msg("Info", L($"Kein ATIS für {tw.F.Name} (keine Wetterdaten aus der Mission).", $"No ATIS for {tw.F.Name} (no weather data from the mission).")) };
                fromMenu = false;
            }
            else msgs = conf < MinConf ? tw.SayAgain(text, Now()) : other != null ? tw.OtherField(text, other) : tw.OnTranscript(text, q.Tel, TrafficFor(q), Now(), fromMenu);   // uncertain recognition: ask back instead of acting
            TraceState($"Spruch an {tw.F.Name}, Intent {intent}" + (conf < MinConf ? ", unsicher -> say again" : other != null ? $", anderer Platz {other.Name}" : ""));
            if (fromMenu && Cfg.PilotVoice != "")
            {
                var role = to ?? msgs.FirstOrDefault(m => m.Role != "Info")?.Role ?? "Tower";
                if (role == "Approach" && (msgs.Any(m => m.Role == "Departure") || tw.Dep(role) == "Departure")) role = "Departure";   // radio wheel "Approach: …" after handover on departure
                if (!Cfg.Frequencies.ContainsKey(role) && role != "Departure") role = "Tower";   // Departure: own frequency (AirfieldFrequencies)
                outq.Add(new Tx(PilotCall(tw.F, role, p, spoken, role == "Center" ? tw.StationOf(role) : null), role, p.Callsign, p.Gid, true, PilotVoiceFor(p.Unit), Freqs: role == "Center" ? new[] { tw.CenFreq } : FieldFreqs(tw.F, role), Prio: 0, Side: p.Coalition));
            }
            bool emg = intent == "emergency" && conf >= MinConf && other == null && tw.Emergency;   // R359: reply to an emergency call (also without signal word) goes first, no "stand by"
            outq.AddRange(msgs.Select(m => emg ? ToTx(q, m) with { Prio = 0 } : ToTx(q, m)));
        }
        Send(p, outq);
    }

    /// N1: ATC only at own and neutral airfields (like AWACS, AI targets and the DCS-ATC menu).
    static bool Served(int side, Airfield f) => f.Side == 0 || f.Side == side;
    /// A133: SRS side of an airfield controller: that of the airfield, at a neutral airfield that of the player.
    static int SideOf(Airfield f, Pilot p) => f.Side > 0 ? f.Side : p.Coalition;

    /// N1: enemy airfield called/selected: no reply, once per flight and airfield a text with the nearest own airfield ("Krasnodar-Center 270/35 NM", mag).
    static void Hostile(Pilot p, Airfield f)
    {
        Log($"[ATC] {p.Callsign}: {f.Name} feindlich, keine Flugsicherung");
        if (!p.HostileTold.Add(f.Name)) return;
        var n = p.Tel is { } t ? Fields.Where(o => o.Ends.Count > 0 && Served(p.Coalition, o)).MinBy(o => Dist(t.X, t.Z, o.X, o.Z)) : null;
        var near = n == null ? "" : L(" Nächster eigener Platz: ", " Nearest friendly airfield: ") + FormattableString.Invariant(
            $"{n.Name} {((Tower.Bearing(p.Tel!.X, p.Tel.Z, n.X, n.Z) - n.MagVar) % 360 + 360) % 360:000}/{Dist(p.Tel.X, p.Tel.Z, n.X, n.Z) / NM:0} NM.");
        SayQueue.Add(new Tx(L($"{f.Name} ist feindlich: keine Flugsicherung.", $"{f.Name} is hostile: no ATC.") + near, "Info", "", p.Gid));
    }

    /// N40: active airfield captured. On approach (airborne): diversion to the nearest own airfield, sent one last time on the old airfield frequency
    /// with the player's side (he is tuned there), the new airfield is selected and expects him. Otherwise once per flight a text.
    static IEnumerable<Tx> Captured(Pilot p)
    {
        var tw = p.Active!;
        if (p.Pinned == tw) (p.Pinned, p.PinFreqs) = (null, null);
        if (p.Tel is { Agl: > 30 } t && tw.Phase is Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand)
        {
            var dv = Fields.Where(f => f.Ends.Count > 0 && Served(p.Coalition, f)).MinBy(f => Dist(t.X, t.Z, f.X, f.Z));
            Log($"[ATC] {p.Callsign}: {tw.F.Name} erobert -> {dv?.Name ?? "kein eigener Platz"}");
            var m = tw.Closed(t, dv);
            if (dv != null) { (p.Pinned, p.PinFreqs) = (TowerFor(p, dv), null); p.Pinned.Expected = true; }
            return m.Select(x => ToTx(p, x) with { Side = p.Coalition });
        }
        if (!p.HostileTold.Add(tw.F.Name)) return Array.Empty<Tx>();
        var side = tw.F.Side == 1 ? L("rot", "red") : L("blau", "blue");
        return new[] { new Tx(L($"{tw.F.Name} wurde erobert ({side}): keine Flugsicherung mehr.", $"{tw.F.Name} was captured ({side}): no ATC anymore."), "Info", "", p.Gid) };
    }

    /// Emergency (radio wheel, F10): nearest own airfield (own side or neutral) with runway from 1800 m, otherwise the nearest own; the tower picks the runway by wind.
    internal static Airfield? EmergencyField(Telemetry t, int side) => Airfield.Emergency(Fields, t.X, t.Z, side);

    /// Pilot's call for the menu entry. A30: emergency with the distress call before the station, plus position, altitude and heading
    /// ("MAYDAY MAYDAY MAYDAY, Kutaisi Approach, Enfield 1 1, Viper, engine failure, 20 miles west of Kutaisi, 6000 feet, heading 090, 40 minutes fuel, one soul on board, request immediate landing.").
    /// R244: with type, fuel in minutes and souls on board (AIM 6-3-1), then the controller does not ask any more.
    internal static string PilotCall(Airfield f, string role, Pilot p, string spoken, string? st = null)
    {
        var (station, cs) = (st ?? f.StationOf(role), p.Callsign.Replace('-', ' '));
        if (p.Tel is { } tl && spoken is "airborne, climbing" or "inbound for landing")   // R275: first call with altitude (FAA JO 7110.65 5-2-17, AIM 4-1-13): "passing 1800" or "30 miles north, 11000 feet"; Tower.Intent understands both as before
        {
            var ft = Math.Round(tl.AltMsl / 0.3048 / 100) * 100;
            spoken = spoken == "airborne, climbing" ? $"airborne, passing {ft:0} feet, climbing"
                : $"{Tower.MilesTxt(Dist(tl.X, tl.Z, f.X, f.Z))} {Tower.Dir8(Tower.Bearing(f.X, f.Z, tl.X, tl.Z))}, {ft:0} feet, inbound for landing";
        }
        if (Regex.Match(spoken, @"^(mayday mayday mayday(?: fuel)?|pan pan, pan pan, pan pan), (.+?)(, request \w+ landing)?$", RegexOptions.IgnoreCase) is not { Success: true } m) return $"{station}, {cs}, {spoken}.";
        if (p.Tel is { } gt && Tower.OnGround(gt)) return $"{m.Groups[1].Value.ToUpperInvariant()}, {station}, {cs}, {m.Groups[2].Value}, shutting down.";   // R300: on the ground without landing, position, altitude
        var pos = "";
        if (p.Tel is { } t)
        {
            int h = (int)Math.Round(((t.Hdg * 180 / Math.PI - f.MagVar) % 360 + 360) % 360) % 360;
            pos = FormattableString.Invariant($", {Tower.MilesTxt(Dist(t.X, t.Z, f.X, f.Z))} {Tower.Dir8(Tower.Bearing(f.X, f.Z, t.X, t.Z))} of {f.Name.Replace('-', ' ')}, {Math.Round(t.AltMsl / 0.3048 / 100) * 100:0} feet, heading {(h == 0 ? 360 : h):000}")
                + ", " + FuelSouls(p);
        }
        return $"{m.Groups[1].Value.ToUpperInvariant()}, {station}, {cs}, {(p.Type != "" ? Ops.TypeSay(p.Type) + ", " : "")}{m.Groups[2].Value}{pos}{m.Groups[3].Value}.";
    }
    /// R244/R300: fuel in minutes and souls on board ("25 minutes fuel, one soul on board"): in the emergency call and in reply to "say souls on board and fuel remaining".
    internal static string FuelSouls(Pilot p)
    {
        int souls = Regex.IsMatch(p.Type, @"^(Mi-8|CH-47)") ? 3 : Regex.IsMatch(p.Type, @"^(F-14|F-15E|F-4E|AH-64|Mi-24|L-39|C-101|Tornado|Su-24|UH-1H|OH-58)") ? 2 : 1;
        int min = Math.Max(5, (int)Math.Round(p.Fuel * Ops.Endurance(p.Type) / 5) * 5);   // minutes on a full tank per type (Ops.FullTankMin)
        return $"{min} minutes fuel, {new[] { "one soul", "two souls", "three souls" }[souls - 1]} on board";
    }

    /// Whisper word confidence, below it "say again" (measurement: clear calls ≥ 0.82, gibberish/German/backwards ≤ 0.55)
    const double MinConf = 0.65, ExpConf = 0.50;   // LA10: expected next call from 0.50

    /// Unambiguous airfield request (start-up/taxi -> Ground, takeoff -> Tower, landing -> Approach), otherwise null.
    static string? FieldRole(string n) =>
        Tower.StartReq(n) || Tower.Has(n, "taxi") ? "Ground"
      : Tower.Has(n, "ready for departure", "ready for take ?off", "request take ?off", "holding short") ? "Tower"
      : Tower.Has(n, "inbound for landing", "request landing", "full stop") ? "Approach" : null;

    /// Reply to a request: if the controller has other calls before the reply, he says "…, stand by" first (SpeakLoop decides).
    static void Send(Pilot p, List<Tx> outq)
    {
        AddStandby(p, outq);
        outq.ForEach(SayQueue.Add);
    }

    /// "stand by" is attached to the first Tx (SpeakLoop says it after the own call, otherwise before the reply), only before a normal reply
    /// (never before an urgent/emergency reply) and with the call sign the reply starts with (flight, flight lead), otherwise the player's.
    internal static void AddStandby(Pilot p, List<Tx> outq)
    {
        if (outq.FirstOrDefault(x => !x.Pilot && x.Role != "Info") is not { } rep || PrioOf(rep) < 1) return;
        var cs = Regex.Match(rep.Text, $@"^[A-Za-z]+(?: (?:{string.Join("|", Tower.DigitWords)}))+(?: flight)?(?=,)") is { Success: true } m ? m.Value : Tower.SpokenCallsign(p.Callsign);
        outq[0] = outq[0] with { Standby = rep with { Text = $"{cs}, stand by.", Prio = 0, Standby = null } };
    }

    /// Monitor SRS: AWACS/tanker (range, if the mission has one) and the airfield frequencies of the players.
    static IEnumerable<double> ListenFreqs(List<Pilot> pilots)
    {
        bool known = Fields.Any(f => f.HasFreq);
        var roles = Cfg.Frequencies.Where(f => RoleOn(f.Key)).Where(f => f.Key is "AWACS" or "Tanker" or "Center" || (f.Key == "JTAC" && Ops.Leaders.Count > 0) || (f.Key == "Range" && Ops.Range != null)
                                               || (f.Key == "Carrier" && Carrier.Boats.Count > 0)
                                               || (f.Key == "RedCrown" && Carrier.RadarShips.Count > 0)
                                               || (!known && f.Key is "Ground" or "Tower" or "Approach")).Select(f => f.Value)
                        .Concat(Carrier.Boats.Where(b => b.Freq > 0 && On("carrier")).Select(b => b.Freq))
                        .Concat(Carrier.StationFreqs.Values.Where(_ => Carrier.Boats.Count + Carrier.RadarShips.Count > 0 && On("carrier")))   // #13: own carrier station frequencies, #18 Red Crown
                        .Concat(Carrier.RadarShips.Where(s => s.Freq > 0 && On("carrier")).Select(s => s.Freq))   // Red Crown (cruiser frequency from the mission)
                        .Concat(pilots.Where(p => p.TankFq > 0 && RoleOn("Tanker")).Select(p => p.TankFq))   // R387: tanker frequency tuned by DCS
                        .Concat(Ops.TankerFreq.Values.Where(_ => RoleOn("Tanker")))   // TP7: mission tanker
                        .Concat(Ops.Leaders.Where(l => l.Freq > 0 && RoleOn("JTAC")).Select(l => l.Freq))   // J5: mission JTAC / FAC(A)
                        .Concat(Tower.CenterSectors.Where(s => s.Freq > 0 && RoleOn(s.Gci ? "AWACS" : "Center")).Select(s => s.Freq));   // Center sectors, #15 GCI sectors
        // A22: order active airfields of all players, then taxiing, then neighboring airfields (SrsListener takes at most Max, duplicates count once)
        static IEnumerable<double> Fq(IEnumerable<Airfield> fs) => fs.SelectMany(f => f.FreqsOf("*") ?? Array.Empty<double>());   // UHF, VHF or own per controller
        var act = pilots.Where(p => p.Active != null && Served(p.Coalition, p.Active.F)).Select(p => p.Active!.F);   // N1: own and neutral airfields only
        var near = pilots.Where(p => p.Tel != null).SelectMany(p => Fields.Where(f => Served(p.Coalition, f)).OrderBy(f => Dist(p.Tel!.X, p.Tel.Z, f.X, f.Z)).Take(2));
        bool atc = On("atc");   // modules switched off: not monitored (a real AWACS/tower player answers there)
        return Fq(act).Where(_ => atc).Concat(roles).Concat(Ops.AwacsFreq.Values.Where(_ => On("awacs"))).Concat(Fq(near).Where(_ => atc));
    }

    static Tx ToTx(Pilot p, Msg m) => new(m.Text, m.Role, p.Active!.StationOf(m.Role), p.Gid, Freqs: m.Role == "Center" ? new[] { p.Active.CenFreq } : FieldFreqs(p.Active.F, m.Role), Side: m.Role == "Center" ? p.Coalition : SideOf(p.Active.F, p),
        Aired: m.Role == "Approach" && p.Active is { } tw ? () => tw.Aired(Now(), m.Text) : null);

    /// Ground/Tower/Approach/Departure of an airfield (Airfield.FreqsOf): own from AirfieldFrequencies, otherwise its DCS frequency (UHF, VHF), null = the one from the config;
    /// ATIS: the own, otherwise the airfield's ATIS frequency (A35, like AtisLoop).
    static double[]? FieldFreqs(Airfield f, string role) =>
        role == "ATIS" ? f.Own.GetValueOrDefault("ATIS") ?? new[] { AtisFreq(f.Name) } : f.FreqsOf(role);

    /// Lighting via DCS-ATC (LIGHT-P1), below 20 NM: "atc" (Inbound) in any approach phase airborne (also radar vectoring, straight-in, emergency),
    /// "dep" (Request startup) from taxi clearance on the ground, otherwise null.
    static string? LightKind(Phase ph, Telemetry t, Airfield f) =>
        Dist(t.X, t.Z, f.X, f.Z) >= 20 * NM ? null
        : t.Agl > 30 ? (ph is Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand ? "atc" : null)
        : t.Agl < 5 && ph is Phase.TaxiOut or Phase.HoldShort ? "dep" : null;

    /// DCS radio menu (entries keep their number, also hidden): \ -> F6 tanker / F5 ATC -> (list: F<n>) -> F1 "Intent to refuel" / "Inbound", on the ground F3 "Request startup".
    static ushort[] MenuKeys(string kind, int nr, int count) =>
        new[] { (ushort)Cfg.CommsKey, (ushort)(kind == "tanker" ? 0x40 : 0x3F) }.Concat(count > 1 ? new[] { (ushort)(0x3A + nr) } : Array.Empty<ushort>())
            .Append((ushort)(kind == "dep" ? 0x3D : 0x3B)).ToArray();

    /// Forum (jojojung): AI wingmen by voice, as the DCS radio menu. Phrase (normalised) -> message the menu sends; first match wins (longer phrases first).
    static readonly (string Rx, string Msg)[] WingCmds =
    {
        (@"(?:mission|primary)(?: and)? (?:rejoin|join up)", "FulfilTheTaskAndJoinUp"), (@"(?:mission|primary)(?: and)? (?:rtb|r t b|return to base)", "FulfilTheTaskAndReturnToBase"),
        (@"(?:engage|attack) my target", "EngageMyTarget"), (@"(?:engage|attack) my (?:enemy|attacker)", "MyEnemyAttack"),
        (@"engage (?:air defen[cs]es?|sams?|s a m|triple a|aaa)", "EngageAirDefenses"), (@"engage (?:ground|armor|tanks?)", "EngageGroundTargets"),
        (@"engage (?:ships?|naval|boats?)", "EngageNavalTargets"), (@"engage (?:bandits?|air|fighters?)", "EngageBandits"),
        (@"cover me", "CoverMe"), (@"rejoin|re join|join up|form up", "JoinUp"),
        (@"line abreast", "LineAbreast"), (@"trail", "GoTrail"), (@"wedge", "Wedge"), (@"echelon left", "EchelonLeft"), (@"echelon(?: right)?", "EchelonRight"),
        (@"finger four", "FingerFour"), (@"spread four", "SpreadFour"), (@"close (?:formation|up)|tighten up", "CloseFormation"), (@"open (?:formation|up)|loosen up|spread out", "OpenFormation"),
        (@"rtb|r t b|return to base|go home", "ReturnToBase"), (@"(?:resume|fly) (?:route|mission)|continue mission", "FlyRoute"), (@"hold position|anchor here|orbit here|hold here", "AnchorHere"),
        (@"(?:go|fly) to (?:the )?tanker|go refuel", "GoRefueling"), (@"(?:radar|music) on", "RadarOn"), (@"(?:radar|music) off", "RadarOff"),
        (@"(?:ecm|jammer|jamming) on", "DisturberOn"), (@"(?:ecm|jammer|jamming) off", "DisturberOff"), (@"smoke on", "SmokeOn"), (@"smoke off", "SmokeOff"),
        (@"jettison", "JettisonWeapons"), (@"pinc?h?er left", "PincerLeft"), (@"pinc?h?er right", "PincerRight"), (@"pinc?h?er high", "PincerHigh"), (@"pinc?h?er low", "PincerLow"),
        (@"check in", "FlightCheckIn"),
    };

    /// "Two, go trail", "Enfield 1-3, rejoin", "Flight, engage bandits" -> recipient as in the DCS radio menu (1–3 = wingman 2–4, 5 = flight) and wMsgLeader…, else null.
    /// A callsign before the number counts only if it is the own flight's ("Texaco 1-2, … request rejoin" is the tanker).
    internal static (int Par, string Msg)? WingCmd(string n, string callsign)
    {
        var own = Regex.Match(Tower.Normalize(callsign), @"^[a-z]+ \d").Value;
        var m = Regex.Match(n, $@"^(?:{Regex.Escape(own)} )?([234]|flight)\b(.*)$");
        if (!m.Success) return null;
        var c = WingCmds.FirstOrDefault(w => Regex.IsMatch(m.Groups[2].Value, $@"\b(?:{w.Rx})\b"));
        return c.Msg == null ? null : (m.Groups[1].Value == "flight" ? 5 : m.Groups[1].Value[0] - '1', "wMsgLeader" + c.Msg);
    }

    static readonly string WingFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Wing.txt");   // DcsAtcHook.lua performs the radio menu item, reply as event "WING;result;par;msg"

    /// K2/K4: airfield frequency on which p hears the controller: tuned (SRS, most recently called first), otherwise the called (SRS state may be older than the call), otherwise VHF (= map), otherwise UHF. 0 = without Radio.lua.
    /// any: any controller frequency of the airfield (Tower.HeardOn, own per controller from AirfieldFrequencies), otherwise that of the tower.
    static double SpokenFreq(Airfield f, Pilot p, string role = "Tower", bool any = false)
    {
        if (FieldFreqs(f, role) is not { } fs) return 0;
        var on = any ? FieldFreqs(f, "*")! : fs;
        var heard = (Srs?.TunedFreqs(p.Id) ?? Array.Empty<double>()).OrderBy(m => Math.Abs(m - p.CalledOn) < 0.005 ? 0 : 1).Append(p.CalledOn);
        return heard.SelectMany(m => on.Where(x => Math.Abs(m - x) < 0.005)).DefaultIfEmpty(fs[^1]).First();
    }

    static double FieldQnhOf(Airfield f) => Wx.TryGetValue(f.Name, out var w) && w.Pa > 0 ? w.Pa / 100 / Math.Pow(1 - 2.25577e-5 * (f.Elev + 2), 5.25588) : 0;   // QNH = pressure at the airfield reduced to sea level, 0 = unknown
    /// R386: ", wind 240 degrees, 8 knots" from the mission weather at the airfield (magnetic like Tower.Wind), "" without weather data
    static string WindSay(Airfield f)
    {
        if (!Wx.TryGetValue(f.Name, out var w)) return "";
        double kt = Math.Sqrt(w.Wx * w.Wx + w.Wz * w.Wz) / 0.514444;
        if (kt < 3) return ", wind calm";
        int dir = (int)Math.Round(((Math.Atan2(-w.Wz, -w.Wx) * 180 / Math.PI - f.MagVar) % 360 + 720) % 360 / 10) * 10;
        return $", wind {Tower.Digits((dir == 0 ? 360 : dir).ToString("000"))} degrees, {Tower.Digits(((int)Math.Round(kt)).ToString())} knots";
    }
    static (double Qnh, double Wx, double Wz) FieldWxOf(Airfield f) => (FieldQnhOf(f), Wx.GetValueOrDefault(f.Name).Wx, Wx.GetValueOrDefault(f.Name).Wz);   // R132: QNH and surface wind at the airfield for the range check-in

    /// Situation at the airfield for the player's tower: others' emergencies, weather, landing order, ATIS identifier (every second and before the reply to a radio call).
    static void Prep(Pilot p, Tower tw, List<Pilot> pilots)
    {
        // R37: only MAYDAY in the air (PAN: priority only, ground emergency blocks nothing), with distance to the airfield
        var em = pilots.Where(o => o != p && o.Active?.F == tw.F && o.Active.Mayday && o.Tel != null).Select(o => Dist(o.Tel!.X, o.Tel.Z, tw.F.X, tw.F.Z) / NM).DefaultIfEmpty(-1).Min();
        (tw.OtherEmergency, tw.EmergencyNm) = (em >= 0, Math.Max(em, 0));
        tw.FlightSize = p.FlightSize;
        tw.Reserved = pilots.Where(o => o != p && o.Active?.F == tw.F && o.Active.Phase == Phase.TaxiIn && o.Active.TaxiInTold && o.Active.Spot is { }).Select(o => o.Active!.Spot!.Value).ToList();   // R108: assigned parking spots of other players
        tw.AcType = p.Type;
        tw.Iff = Srs?.IffOf(p.Id);   // FI1
        tw.Heli = p.Heli;   // R347
        tw.Side = p.Coalition;   // N1: clearance only to own/neutral targets
        tw.HeardOn = SpokenFreq(tw.F, p, any: true);
        tw.Key = "p:" + p.Unit;
        tw.FieldQnh = FieldQnhOf(tw.F);
        tw.FieldWind = Wx.TryGetValue(tw.F.Name, out var fw) ? (fw.Wx, fw.Wz) : null;
        tw.QueueAhead = ArrQueue.TryGetValue(tw.F.Name, out var aq) ? (aq.IndexOf("p:" + p.Unit) is var qi and >= 0 ? qi : p.Tel is { } pt ? Slot(aq, tw.F, Dist(pt.X, pt.Z, tw.F.X, tw.F.Z)) : aq.Count) : -1;
        tw.Spaced = aq == null || SpacedAt(aq, tw.QueueAhead, tw.F, pilots, p.Tel is { } me ? (me.X, me.Z) : null);
        (tw.AheadR, tw.AheadHeavy) = aq != null && tw.QueueAhead > 0 && tw.QueueAhead <= aq.Count ? AheadOf(aq[tw.QueueAhead - 1], tw.F, pilots) : (-1, false);   // R46: separation on final
        // A9: one runway, one clearance: other players at the airfield with takeoff clearance or "line up and wait" before the takeoff roll (below 30 kt), AI with takeoff clearance still on the ground
        tw.RunwayClaimed = pilots.Count(o => o != p && o.Active?.F == tw.F && o.Tel is { Agl: < 15, Ias: < 15 } && (o.Active.Phase == Phase.ClearedTakeoff || o.Active.Phase == Phase.HoldShort && o.Active.LineUp))
            + Ai.Where(a => !a.InAir && a.Speed < 15 && a.Flag.StartsWith("dep-go") && (a.Flag.Split(':', 2) is [_, var n] ? n : "Kutaisi") == tw.F.Name && aiStage.GetValueOrDefault(a.Group) == "ready").Select(a => a.Group).Distinct().Count();
        // R341: departure order by the report at the holding point (FAA JO 7110.65 3-9-1): others holding short who reported earlier; not yet holding short myself -> all of them
        bool queued = tw.Phase == Phase.HoldShort && tw.HoldSince >= 0;
        tw.HoldAhead = pilots.Count(o => o != p && o.Active?.F == tw.F && o.Active.Phase == Phase.HoldShort && !o.Active.LineUp && o.Active.HoldSince >= 0
                                         && (!queued || o.Active.HoldSince < tw.HoldSince || o.Active.HoldSince == tw.HoldSince && string.CompareOrdinal(o.Unit, p.Unit) < 0));
        if (p.FlightSize > 1) tw.SetCallsign(p.Callsign);
        tw.CallsignFixed = MissionData || Cfg.MyCallsign != "";   // call sign from the slot or the settings: not from numbers in the radio call
        tw.AtisLetter = Atis.TryGetValue(tw.F.Name, out var a) && a.Text != "" ? a.Letter.ToString() : "";
        if (tw.Phase is Phase.Departing or Phase.Away) tw.AwacsContact = Ops.AwacsContact(TrafficFor(p), MeOf(p), Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5), Tower.CenterName(p.Coalition) != null ? Tower.CenterAwacsNm : 0);   // sign-off after departure; with a Center only an AWACS in range
        // N2: guard per DCS group (formation = one aircraft, FAA JO 7110.65 2-1-13): silent after controller contact under 5 min, with flight following, while another sequence is running
        // (Tbilisi/Vaziani overlap), for helicopters; checked in with the AWACS only traffic pattern and runway; the 60 s after liftoff belong to the player, not the tower (reset per airfield change)
        var g = p.Gid > 0 ? Pilots.Values.Where(o => o.Gid == p.Gid && o.Tel != null).Append(p).Distinct().ToList() : new() { p };
        tw.Exempt = p.Heli || g.Any(o => Now() - o.AtcAt < 300 || o.Lead == null && o.Towers.Values.Any(x => x.Following || x == o.Active && x.TransitInfo != null || x != tw && x.Phase is not (Phase.Parked or Phase.Away)));   // N3: ongoing transit
        tw.Unknown = g.All(o => o.AtcAt < 0);   // N51: no request and no controller call yet in this flight: call sign unknown
        tw.Tactical = g.Any(o => o.Ops.AwacsIn);
        tw.HostileNm = TrafficFor(p).Where(a => a.InAir && a.Coalition > 0 && a.Coalition != p.Coalition).Select(a => Dist(a.X, a.Z, tw.F.X, tw.F.Z) / NM).DefaultIfEmpty(999).Min();
        tw.Aloft = p.Airborne is { } ab ? (Clock - ab).TotalSeconds : 0;
    }

    /// N3: overflight to the players in the traffic pattern of this airfield, once per overflight: "Enfield 2-1, traffic, Viper crossing overhead southbound, 3000 feet."
    static List<Tx> TransitNotes(Pilot p, Tower tw, List<Pilot> pilots) => tw.TransitInfo is not { } ti ? new()
        : pilots.Where(o => o != p && o.Active?.F == tw.F && o.Active.Phase is Phase.Initial or Phase.Pattern or Phase.ClearedLand && tw.TransitTold.Add(o.Unit))
                .Select(o => ToTx(o, new Msg("Tower", $"{Tower.SpokenCallsign(o.Callsign)}, traffic, {(p.Type != "" ? Ops.TypeSay(p.Type) : "aircraft")} {ti}."))).ToList();

    /// Every second: airfield selection, others' emergencies, ATIS identifier, tower tick per player, AI control.
    static void TickAll()
    {
        var outq = new List<Tx>();
        lock (TowerLock)
        {
            PauseCheck(CurrentTel());   // LA17
            if (Srs is { Connected: true, HasPlayers: false } && Host()?.Tel != null)   // Forum (Tony86): SRS client on another server: SrsAutoConnect leaves it alone, nothing goes either way
            {
                if (Now() - (noSrsClientAt ??= Now()) > 30)
                    SrsHint(L($"Kein Funk: dein SRS-Client ist nicht mit dem SRS-Server auf diesem PC verbunden. Im Client 127.0.0.1:{SrsPort} eintragen und Connect drücken, kein anderer Server.",
                              $"No voice: your SRS client is not connected to the SRS server on this PC. Enter 127.0.0.1:{SrsPort} in the client and press Connect, no other server."));
            }
            else noSrsClientAt = null;
            if (MissionFields && File.GetLastWriteTime(AirfieldFile) != airfieldStamp) LoadFields();
            if (MissionFields && File.GetLastWriteTime(StandFile) is var ss && ss != standStamp)
                try { Airfield.LoadStands(Fields, StandFile); standStamp = ss; Trace("PLATZ", $"DCS-Standnamen: {Fields.Sum(f => f.Stands.Count)}"); }
                catch (IOException) { }   // hook is still writing: next second
            FormFlights(ActivePilots());
            var pilots = ActivePilots().Where(p => p.Lead == null).ToList();   // wingmen in formation run via the lead
            foreach (var p in pilots.Where(p => p.Tel is { Agl: < 5, Ias: < 2 })) (p.Pinned, p.OnBoat) = (null, null);   // shut down: automatic again
            foreach (var p in pilots.Where(p => p.Tel != null && p.Active != null && !Served(p.Coalition, p.Active.F))) outq.AddRange(Captured(p));   // N40: before Pick (last call of the old airfield)
            foreach (var p in pilots.Where(p => p.Tel != null))
                if (Carrier.Deck(p.Tel, p.Coalition) != null) (p.Active, p.Pinned) = (null, null);   // R24: no land tower on the deck (no Ground suggestion, no airfield ATIS)
                else if (Pick(p, null) is { } pt) Switch(p, pt);   // no own/neutral airfield: stays (N1)
            SaveSession();
            Srs?.SetFreqs(ListenFreqs(pilots));
            foreach (var f in pilots.Where(p => p.Active != null).Select(p => p.Active!.F).Distinct())
                if (MissionData && UpdateAtis(f)) outq.AddRange(AtisCall(f, pilots));   // new identifier: announcement to all at the airfield; R120: only with fresh mission data
            LoadTerrain();
            UpdateQueues(pilots);
            foreach (var k in Holds.Keys.Where(k => k.StartsWith("p:")).ToList()) Holds.Remove(k);
            foreach (var p in pilots) if (p.Active?.HoldInfo is { } h) Holds["p:" + p.Unit] = h;
            var host = Host();
            foreach (var p in pilots.Where(p => p.Active != null && Served(p.Coalition, p.Active.F)))   // N40: an enemy (captured) airfield does not transmit
            {
                var tw = p.Active!;
                Prep(p, tw, pilots);
                var msgs = tw.Tick(p.Tel, TrafficFor(p), Now());   // also without the atc module: phases for debrief and crew
                if (!On("atc")) msgs.Clear();
                if (p.Ride is { } ride && p.Tel is { } rt)   // FI4
                {
                    if (tw.F == ride.F) ride.Rw = tw.Runway;
                    outq.AddRange(ride.Tick(rt, Now()).Select(s => InstructorTx(p, s)));
                    if (ride.Result is { } res) p.Ops.Debrief.Add(res);
                    if (ride.Done) p.Ride = null;
                }
                if (tw.HoldInfo is { } hi) Holds["p:" + p.Unit] = hi; else Holds.Remove("p:" + p.Unit);   // current for the AI squadron
                if (MissionData && p == host && Cfg.RunwayLights != "off" && On("atc") && p.Tel is { } lt && LightKind(tw.Phase, lt, tw.F) is { } lk && Now() - p.LitAt.GetValueOrDefault((tw.F.Id, lk), -1e9) > 900)
                {
                    p.LitAt[(tw.F.Id, lk)] = Now();   // lighting: only after DCS-ATC contact. Mission checks sun/weather (w) and open DCS dialog, switches the airfield on audibly for a short time
                    if (Cfg.CommsKey == 0) Log($"[DCS] Befeuerung {tw.F.Name}: keine Funkmenü-Taste (CommsKey 0)");
                    else AiCmd($"MENU;{p.Unit};{lk};{tw.F.Id};{(tw.Ifr ? "w" : "")}{(Cfg.RunwayLights == "dcs" ? "" : "s")}");
                }
                foreach (var m in msgs.Where(m => m.Role == "Info" && (m.Text.StartsWith(L("Landebewertung", "Landing grade")) || m.Text.StartsWith(L("Platzrunde", "Pattern")) || m.Text.StartsWith(L("Anflug ohne", "Approach without")) || m.Text.StartsWith(L("Verstoß:", "Deviation:")))))   // R11, N4
                    foreach (var o in ActivePilots().Where(o => o == p || o.Lead == p)) o.Ops.Debrief.Add(Tag(m.Text.Split(L(". Beenden", ". To end"))[0], m.Text.StartsWith(L("Landebewertung", "Landing grade")) ? Night() : null));
                if (msgs.Any(m => m.Role != "Info") && !tw.WatchSaid) p.AtcAt = Now();   // N2: controller call = contact, guard not
                var txs = msgs.Select(m => ToTx(p, m)).ToList();
                if (tw.WatchSaid && FieldFreqs(tw.F, "Tower") is { } wf && !ShowTo(p.Gid, wf[0], wf.ElementAtOrDefault(1)).Any() && Now() - guardAt.GetValueOrDefault("tw:" + p.Unit, -1e9) >= 60)
                {
                    guardAt["tw:" + p.Unit] = Now();   // N2: airfield frequency not tuned -> guard on Guard like KF84 ("Kutaisi Tower on guard, …, contact Kutaisi Tower 263.0")
                    txs = txs.Select(x => x.Role == "Info" ? x : x with { Text = Flights.OnGuard(x.Text, x.Station, SpokenFreq(tw.F, p)), Freqs = new[] { Flights.Guard, Flights.GuardVhf } }).ToList();
                }
                outq.AddRange(txs);
                outq.AddRange(TransitNotes(p, tw, pilots));
            }
            Carrier.WireEvents = MissionData;   // R27: wire comes from the DCS event
            Tower.Sky = MissionData ? (Sky.Clouds && Carrier.Ceiling, Sky.BaseM, Sky.VisM) : null;   // R340: IFR per airfield (Tower.IfrAt), FEW/SCT is no ceiling (R367)
            outq.AddRange(CsarTick(ActivePilots().ToList()).ToList());   // FI6
            foreach (var p in ActivePilots())   // range, AWACS, tanker; debrief after shutdown
            {
                var oc = p.Ops.Tick(MeOf(p), TrafficFor(p), Now(), awacs: p.Lead == null && On("awacs"), lead: p.Lead?.Ops).ToList();
                if (p.Ops.Cmds.Count > 0) { foreach (var cm in p.Ops.Cmds.ToList()) AiCmd(cm); p.Ops.Cmds.Clear(); }   // J4: MARK / LASE / LASEOFF
                foreach (var c in oc) AwacsNote(p, c);
                outq.AddRange(oc.Select(c => OpsTx(p, c)));
                foreach (var c in oc.Where(c => c.Role == "AWACS" && Urgent.IsMatch(c.Text)))   // KF84: not tuned to AWACS -> repeat on Guard
                {
                    var f = c.Freq > 0 ? c.Freq : Cfg.Frequencies.GetValueOrDefault("AWACS");
                    if (f <= 0 || ShowTo(p.Gid, f, 0).Any() || Now() - guardAt.GetValueOrDefault(p.Unit, -1e9) < 60) continue;
                    guardAt[p.Unit] = Now();
                    outq.Add(new Tx(Flights.OnGuard(c.Text, c.Station, f), "AWACS", c.Station, p.Gid, Freqs: new[] { Flights.Guard, Flights.GuardVhf }, Prio: 0, Side: p.Coalition));
                }
                p.Boat.Lead = p.Lead?.Boat;   // R26: lead's check-in/Charlie applies to the flight in formation
                var bt = On("carrier") ? p.Boat.Tick(MeOf(p), Now()) : new();
                if (p.Boat.Gone) { p.Boat.Gone = false; (p.Lead ?? p).OnBoat = null; }   // A21: out of the recovery (divert, landed): radio without target goes to the tower again
                if (p.Boat.LatLog is { } ll) { Log(ll); p.Boat.LatLog = null; }   // R4: calibrate SternLat
                foreach (var c in bt.Where(c => c.Role == "Info" && (c.Text.StartsWith("LSO") || c.Text.StartsWith(L("Verstoß:", "Deviation:")))))   // #17: violation at the carrier
                {
                    p.Ops.Debrief.Add(c.Text.StartsWith("LSO") ? Tag(L("Träger ", "Carrier ") + c.Text, p.Boat.CaseNo == 3 ? "Case III" : null, Night()) : L("Träger ", "Carrier ") + c.Text);
                    if (c.Text.StartsWith("LSO") && p.Boat.AoaLog(p.Callsign) is { } al) Log(al);   // calibrate (Export may deliver a different AoA than the HUD)
                }
                outq.AddRange(bt.Select(c => OpsTx(p, c)));
                if (On("carrier")) outq.AddRange(BoatWatch(p));
                if (p.Tel is { Agl: > 30 } && p.Airborne == null) { p.Airborne = Clock; p.HostileTold.Clear(); }   // N1: "enemy" hint reset for each flight
                var ph = (p.Lead ?? p).Active?.Phase;
                if (p.LastPhase == Phase.TaxiIn && ph == Phase.Parked && p.Airborne != null) outq.Add(DebriefTx(p, true));   // A7: only after a flight
                if (p.Crew && ph != p.LastPhase && CrewLines.TryGetValue(ph.ToString()!, out var crewLine))
                {
                    outq.Add(CrewTx(p, crewLine));
                    if (ph == Phase.TaxiOut) p.Crew = false;
                }
                if (ph != null) p.LastPhase = ph.Value;
            }
            ControlAi(pilots, outq);
            if (MissionData && On("carrier")) { SteerBoats(); outq.AddRange(CarrierChat()); }
            TraceState("Tick");
        }
        outq.ForEach(SayQueue.Add);
    }

    /// Carrier into the wind (M5) while a player or AI is in the recovery; 3 min afterwards back to the mission route.
    /// Not in the wind (land ahead, mission does not turn, or leg ended, ship stationary): turn again every 5 min.
    /// AI with three waveoffs/bolters -> to the nearest own airfield (M6).
    static readonly Dictionary<string, DateTime> boatInWind = new(), boatTurnAt = new();   // group -> last recovery or last CVTURN
    static readonly Dictionary<string, double> guardAt = new();   // player -> last AWACS warning on Guard (KF84)
    static readonly Dictionary<string, double> boatWarn = new();   // N51: group|ship -> time of the first call, -1 = second said

    /// N51: carrier vicinity without check-in, per DCS group: below 2500 ft and below 5 NM toward an own/neutral ship or in the recovery sector (N51b) -> once "…, Tower, you are inside the carrier control zone, turn … heading …, remain clear 5 miles, say intentions" (R249: Case III Approach, unknowns "this is US Navy warship …, identify yourself and state your intentions"),
    /// after 60 s still below 2 NM "remain clear of the carrier control zone"; re-armed only above 10 NM. Tuned to the carrier frequency there, otherwise on Guard (like KF84).
    /// Silent: AirspaceWatch off, checked in with Marshal, controller contact under 5 min, flight following/running sequence, the first 60 s after takeoff; rearm only when the whole group is gone or landed; AWACS tactics do not count here, helicopters are called too.
    static List<Tx> BoatWatch(Pilot p)
    {
        var outq = new List<Tx>();
        if (!Cfg.AirspaceWatch || p.Lead != null || p.Tel is not { } t) return outq;
        var g = p.Gid > 0 ? Pilots.Values.Where(o => o.Gid == p.Gid && o.Tel != null).Append(p).Distinct().ToList() : new() { p };
        bool still = Tower.OnGround(t) || p.Airborne is not { } ab || (Clock - ab).TotalSeconds < 60
                     || g.Any(o => o.Boat.Stage > 0 || Now() - o.AtcAt < 300 || o.Lead == null && o.Towers.Values.Any(x => x.Following || x.Phase is not (Phase.Parked or Phase.Away)));   // like N2: flight following, running sequence
        foreach (var b in Carrier.Boats.Where(b => b.Coalition == 0 || b.Coalition == p.Coalition))   // enemy ships do not transmit
        {
            var key = $"{(p.Gid > 0 ? p.Gid.ToString() : p.Unit)}|{b.Group}";
            double d = Dist(t.X, t.Z, b.X, b.Z);
            // armed only when the whole group is beyond 10 NM or landed (split flight: otherwise called anew each tick; after landing/catapult launch no second call)
            if (g.All(o => o.Tel is not { } ot || Tower.OnGround(ot) || Dist(ot.X, ot.Z, b.X, b.Z) > 10 * NM)) boatWarn.Remove(key);
            if (d > 10 * NM) continue;
            bool first = !boatWarn.TryGetValue(key, out var at);
            // N51b: only below 2500 ft and below 5 NM toward the ship or in the recovery sector (astern ±30° to the final bearing, up to 10 NM, below 1500 ft)
            double from = Tower.Bearing(b.X, b.Z, t.X, t.Z), aft = (b.Fb + 180) % 360;
            bool astern = Tower.HdgDiff(from, aft) <= 30 && t.AltMsl < 457.2;   // 1500 ft
            if (still || t.AltMsl > 762 || (first ? !astern && (d > 5 * NM || Tower.HdgDiff(t.Hdg * 180 / Math.PI, Tower.Bearing(t.X, t.Z, b.X, b.Z)) > 90)   // 2500 ft, not departing
                                                  : at < 0 || Now() - at < 60 || d > 2 * NM)) continue;
            boatWarn[key] = first ? Now() : -1;
            // diversion: astern perpendicular out of the sector (to the side where he is), otherwise away from the ship
            double away = astern ? (aft + (((from - aft) % 360 + 540) % 360 - 180 >= 0 ? 90 : -90) + 360) % 360 : from;
            var c = Carrier.WarshipCall(b, t, g.All(o => o.AtcAt < 0) ? null : Tower.SpokenCallsign(p.Callsign), !first, away);
            var f = c.Freq > 0 ? c.Freq : Cfg.Frequencies.GetValueOrDefault("Carrier");
            outq.Add(f <= 0 || ShowTo(p.Gid, f, 0).Any() ? OpsTx(p, c)
                : new Tx(Flights.OnGuard(c.Text, c.Station, f), "Carrier", c.Station, p.Gid, Freqs: new[] { Flights.Guard, Flights.GuardVhf }, Prio: 0, Side: p.Coalition));
        }
        return outq;
    }
    static void SteerBoats()
    {
        foreach (var g in Carrier.Divert().Distinct()) { Log($"[ATC] {g}: dreimal Waveoff am Träger -> umgeleitet"); AiCmd($"DIVERT;{g}"); }
        foreach (var b in Carrier.Boats)
        {
            bool rec = ActivePilots().Any(p => p.Boat.BoatGroup == b.Group) ||
                       Ai.Any(a => a.Flag == "cv" && a.InAir && a.Coalition == b.Coalition && Dist(a.X, a.Z, b.X, b.Z) < 20 * NM);   // AI recovery (M6)
            if (rec)
            {
                var (brc, kt) = b.IntoWind;
                if (boatInWind.ContainsKey(b.Group)) boatInWind[b.Group] = Clock;
                if ((Tower.HdgDiff(brc, b.Brc) > 5 || Math.Abs(b.Speed / 0.514444 - kt) > 3) &&
                    (!boatTurnAt.TryGetValue(b.Group, out var turned) || (Clock - turned).TotalMinutes > 5))
                {
                    AiCmd(FormattableString.Invariant($"CVTURN;{b.Group};{brc * Math.PI / 180:0.0000};{kt:0}"));
                    boatInWind[b.Group] = boatTurnAt[b.Group] = Clock;
                }
            }
            else if (boatInWind.TryGetValue(b.Group, out var last) && (Clock - last).TotalMinutes > 3)
            {
                boatInWind.Remove(b.Group); boatTurnAt.Remove(b.Group);
                AiCmd($"CVRESUME;{b.Group}");
            }
        }
    }

    /// Ball call and LSO reply of the mission AI at the carrier (N30): prio 2, without a tuned player no Piper (Say); silent while a player is in the groove (Carrier.AiCalls).
    static IEnumerable<Tx> CarrierChat()
    {
        if (!Chatter || Carrier.Boats.Count == 0) return Array.Empty<Tx>();
        var calls = Carrier.AiCalls(b => Pilots.Values.Any(p => p.Boat.BoatGroup == b.Group && p.Tel != null && p.Boat.InGroove(p.Tel)));
        return calls.Where(c => Pilots.Values.Any(p => p.Tel != null && Dist(p.Tel.X, p.Tel.Z, c.B.X, c.B.Z) < 50 * NM))   // like the airfield radio: players ≤ 50 NM
                    .SelectMany(c => new[] { c.Pilot, c.Lso }.Where(s => s != "").Select(s => new Tx(s, "Carrier", s == c.Lso ? "Paddles" : c.Cs != "" ? c.Cs : c.Group, 0, s != c.Lso,
                                                                                                    s == c.Lso ? null : PilotVoiceFor(c.Group), Freqs: c.B.Freq > 0 ? new[] { c.B.Freq } : null, Prio: 2, Side: c.B.Coalition)));
    }

}
