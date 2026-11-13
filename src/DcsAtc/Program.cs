global using static DcsAtc.Ui;
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

public class Config
{
    public int PttKey { get; set; } = 0xA3;          // Virtual-key code, 0xA3 = right Ctrl
    public int PttJoystick { get; set; } = -1;       // joystick ID (DcsAtc --buttons), -1 = off
    public int PttButton { get; set; } = 1;
    public string MicName { get; set; } = "";        // part of the microphone name, empty = Windows default
    public string Callsign { get; set; } = "Enfield 1-1";   // only without mission data (otherwise call sign from DCS)
    public string MyCallsign { get; set; } = "";       // own call sign instead of the one from the DCS slot, "" = from DCS (Settings window)
    public bool CallsignFromUnit { get; set; }         // forum 0.13: call sign from the mission unit name ("VICTORY 9-1", "WOLF-2-1") instead of the DCS slot (Pontiac); other names stay DCS
    public Dictionary<string, double> Frequencies { get; set; } = new(Tower.Freqs) { ["Range"] = 267.5, ["AWACS"] = 251.5, ["Tanker"] = 255.5, ["Carrier"] = 127.5, ["RedCrown"] = Carrier.RedCrownDefault, ["JTAC"] = 133.5 };   // AWACS/tanker on .5: not on an airfield frequency (Radio.lua whole MHz, K3)
    public Dictionary<string, string> CenterNames { get; set; } = new();   // Center name per map ("Caucasus": "Tbilisi Control", "Caucasus:red": red side), without entry the table in Tower.CenterName
    public List<CenterSector> CenterSectors { get; set; } = new();   // several Center sectors per map (nearest center point inside RadiusNm): Name, Freq, Lat/Lon (Caucasus only) or X/Z, RadiusNm, Side blue/red
    public bool AwacsBullseye { get; set; } = true;    // LK4: picture and situation updates to all as bullseye; false = everything BRAA from the player (fallback)
    public double AwacsNewGroupNm { get; set; } = 200;  // AWACS reports new groups (and picture clean) within this radius, 40-200 NM
    public Dictionary<string, double> OnSpeedAoa { get; set; } = new();   // LSO: on-speed AoA in degrees per type prefix, added to the built-in table (Carrier.OnSpeedDefault) or replacing a value there (calibrate with the log "[LSO] AoA im Mittel")
    public string Modulation { get; set; } = "AM";
    public int Coalition { get; set; } = 2;   // SRS coalition for stations without their own (A133: airfields/AWACS/AI transmit with their side)
    /// Voice per controller: Piper model, optional "@pitch" (0.9 = lower)
    public Dictionary<string, string> Voices { get; set; } = new()
    {
        ["Tower"] = "en_US-ryan-medium", ["Ground"] = "en_US-bryce-medium",
        ["Approach"] = "en_US-ryan-medium@1.05", ["Center"] = "en_US-ryan-medium@1.02", ["ATIS"] = "en_US-ryan-medium@0.94",
        ["Range"] = "en_US-ryan-medium@1.06", ["AWACS"] = "en_GB-alan-medium", ["Tanker"] = "en_US-bryce-medium", ["Crew"] = "en_US-ryan-medium", ["JTAC"] = "en_US-bryce-medium@0.94", ["FACA"] = "en_GB-alan-medium@1.04",
        ["Carrier"] = "en_US-ryan-medium@0.97|en_GB-alan-medium@1.02",   // one per station (Marshal, Tower, Paddles); AWACS/carrier without the pilot model (joe)
    };
    public string PilotVoice { get; set; } = "en_US-joe-medium";   // speaks your menu/wheel requests, "" = off
    public bool RadioFx { get; set; } = true;          // Radio sound: bandpass, noise, squelch
    public bool HintText { get; set; } = true;         // #27: notes of DCS-ATC (hints, errors, landing grade, deviations, "ATC: now ...") as text in the game; false = only in atc-log.txt and the debriefing
    public bool RadioText { get; set; } = true;       // #6 (issue): radio calls also as DCS text on screen; false = speech only (notes and calls without SRS unit ID stay text)
    public double Volume { get; set; } = 0.6;          // controller volume in SRS (0–1)
    public string RunwayLights { get; set; } = "silent";   // runway lighting via the DCS-ATC menu: silent (airfield stays mute), dcs (DCS controller audible 15 s), off
    public string AltimeterUnit { get; set; } = "auto";   // altimeter setting on the radio: auto (by own type: western inHg/altimeter, otherwise hPa/QNH; ATIS both), hpa, inhg
    public bool Atis { get; set; } = true;             // continuous ATIS broadcast for airfields with players nearby
    public int MapPort { get; set; } = 18600;          // FI7: situation map in the browser at http://localhost:18600/, 0 = off; only after restart
    public bool MapLan { get; set; } = false;          // FI7: map also from other devices in the network (tablet): http://<PC>:18600/, Windows asks about the firewall
    public bool StationPositions { get; set; } = true;   // FI2: controllers transmit with their position (SRS range and line of sight, if the server has them on)
    public bool RandomNotams { get; set; } = false;   // FI5: at most one NOTAM per airfield and day (ILS out, closed parallel runway, bird activity); ATIS reads it, runway choice and approach follow it
    public Dictionary<string, List<string>> Notams { get; set; } = new();   // FI5: own NOTAMs per airfield ("Kutaisi": ["ILS 25 unserviceable", "runway 07 closed", free text read as written])
    public bool IffCheck { get; set; } = false;       // FI1 (issue #2): controllers check the transponder from SRS (code, ident, 7600, AWACS "parrot sour"); 7700 is always noticed
    public bool AirspaceWatch { get; set; } = true;    // monitor control zones (N2) and announce violations ("possible pilot deviation") and into the debrief (N4); off: safety warnings only
    public bool DebugLog { get; set; } = true;         // atc-debug.txt for bug reports (speech recognition, airfield selection, phases, telemetry every 10 s), at most 2 × 20 MB
    public bool AsrGpu { get; set; } = true;           // GPU1: speech recognition on a dedicated graphics card (Vulkan), CPU without one or if it fails; false = always CPU
    public string AsrApiUrl { get; set; } = "";        // #11: speech recognition over an OpenAI-compatible endpoint (.../v1/audio/transcriptions: cloud or a whisper server on another PC), "" = local whisper; on errors local
    public string AsrApiKey { get; set; } = "";        // #11: Bearer key for AsrApiUrl ("" = none, e.g. own server)
    public string AsrApiModel { get; set; } = "whisper-1";   // #11: model name at AsrApiUrl
    public bool UpdateCheck { get; set; } = true;      // ask GitHub at start whether a newer release exists (log line, button in the settings window); nothing is downloaded without the button
    public bool KneeboardPages { get; set; } = true;   // kneeboard pages into Saved Games\DCS\Kneeboard on mission start (Caucasus, German)
    public Dictionary<string, double> AtisFreqs { get; set; } = new();   // A35: own ATIS frequency per airfield (airfield name -> MHz), if the airfield is missing: Frequencies["ATIS"]; only after restart
    public int WheelKey { get; set; } = 0xDE;          // selection wheel: key (0xDE = Ä), 0 = off
    public int WheelJoystick { get; set; } = -1;       // selection wheel on the HOTAS: button opens, coolie hat selects
    public int WheelButton { get; set; } = 1;
    public string WheelInGame { get; set; } = "auto";   // radio wheel additionally as in-game text: auto (when DCS is in VR mode), on, off; window = as a picture in a DCS window (#30, also in the headset)
    public double WheelSize { get; set; } = 0.45;       // radio wheel: fraction of the DCS window height, adjustable in the wheel with + / -
    public int CommsKey { get; set; } = 0x2B;          // DCS radio menu key as scancode (0x2B = backslash US, # DE). With it the app presses the tanker menu (basket/boom) and, for lighting at dusk/night/poor visibility, "Inbound" or on the ground "Request startup", 0 = off
    public bool SrsListen { get; set; } = true;        // listen to all players' radio calls via SRS (instead of own PTT key)
    public double SpeechRate { get; set; } = 0.85;     // controller speaking rate: 1 = Piper default (too slow for radio), larger = slower
    public double ReplyPause { get; set; } = 1.5;      // seconds of pause between two radio calls on a frequency
    public bool AiChatter { get; set; } = true;        // AI flights also transmit audibly (request, reply, readback)
    public string AiFlightComms { get; set; } = "voll"; // AI flights in combat: full (Fox, Splash, Tally …), tactical (only Fox/Splash/Bingo/Defending/Guard), off
    public double AiFlightRadiusNm { get; set; } = 60; // AI radio only from flights this close to a player of the same side
    public bool TankerVectors { get; set; }    // tanker itself guides the receiver with join vectors until "visual" (N38); off: AWACS vectors or "request vector" only
    public bool AiOwnWingmen { get; set; } = false;    // players' AI wingmen also transmit (DCS already speaks for them)
    public bool AiFollowAwacs { get; set; } = true;    // LD1: AI fighters fly the AWACS assignment (AttackGroup in the mission script), off = radio only
    public string Mode { get; set; } = "host";         // "client" = radio wheel only for fellow players (requests via DCS chat to the host)
    public bool Dedicated { get; set; } = false;       // dedicated server without a player at the PC: no radio wheel/overlay/keys/microphone, only mission data + SRS; automatic when DCS_server runs without DCS
    public string SrsPath { get; set; } = @"C:\Program Files\DCS-SimpleRadio-Standalone";
    public bool SrsAutoConnect { get; set; } = true;   // connect SRS client to the local SRS server after start (like the SRS DCS hook), only if not yet connected
    public int TelemetryPort { get; set; } = 18500;
}

/// One player. With mission data every client (multiplayer), otherwise only you (Export.lua).
class Pilot
{
    public string Unit = "", Callsign = "", Name = "", Type = "";
    public string SlotCallsign = "";                  // call sign from the DCS slot (Callsign = own from the settings if set)
    public string Modex = "";                         // #12: board number from the mission editor (onboard_num), carrier stations call it instead of the call sign
    public bool Heli;                                 // Helicopter (mission: P line, N50)
    public int Gid;                                   // DCS group ID for in-game text, 0 = to all
    public Telemetry? Tel;
    public DateTime Seen;
    public readonly Dictionary<string, Tower> Towers = new();   // one separate sequence per airfield
    public Tower? Active;
    public Pilot? Lead;                               // in formation with the group lead: its sequence applies
    public Checkride? Ride;                           // FI4: checkride in progress
    public int FlightSize = 1;
    public uint Id;                                   // DCS unit ID (to match SRS radio calls)
    public double CalledOn;                           // K2: frequency of the last SRS radio call (MHz), 0 = none
    public int Coalition = 2;
    public readonly Ops Ops = new();                  // Range, AWACS, tanker, debriefing
    public readonly Carrier Boat = new();             // carrier: Marshal, Paddles (M5)
    public Phase LastPhase;
    public DateTime? Airborne;                        // start of the flight (debrief)
    public readonly Dictionary<(int, string), double> LitAt = new();   // (airfield ID, atc|dep) -> time of the last DCS-ATC request (lighting)
    public Tower? Pinned;                             // airfield selected in the radio wheel, valid until the next landing
    public double[]? PinFreqs;                        // Pinned by a call on the airfield frequency: SRS frequencies included, valid until turning away from the airfield (null = selected in the radio wheel)
    public double[]? LastFq; public Airfield? Fresh;   // last reported SRS frequencies; airfield most recently tuned in (wins when several are tuned)
    public double TankMenuAt = -1e9, TankFq;         // R304: last press in the DCS tanker menu; frequency DCS (Easy Comms) tuned itself afterwards (mission frequency of the tanker, 0 = none)
    public (double Mhz, bool Field)? SharedPick;      // shared frequency (airfield = AWACS/tanker/...): last choice by station name, unnamed calls go there
    public (double Mhz, string Role)? OpsPick;        // issue #3: AWACS and tanker on one frequency: last station named there
    public (string Role, double At)? LastOps;         // R393: Range/AWACS/tanker spoke last (an airfield call clears it): "say again" without a station goes there
    public string? OnBoat;                            // carrier selected in the radio wheel: radio calls without a target go to Marshal/Paddles
    public bool Crew;                                // ground crew stands at the aircraft (mission: C;unit;crew|back)
    public double Fuel = 1;                          // tank 0–1 (mission), low fuel -> priority in the landing order
    public readonly HashSet<string> HostileTold = new();   // N1: enemy airfields with a "no air traffic control" notice (per flight, new from takeoff)
    public double AtcAt = -1e9;                       // N2: last radio contact with a controller (call or controller call, guard not)
    public (string Role, string Ask, double Until)? Expect;   // open question/report from a controller to the player (radio wheel Enter, Expect.cs)
    public double TrafficAt = -1e9;                   // R307: last traffic advisory to the player (radio wheel Traffic, Expect.cs)
}

/// Output: in-game text (to group Gid) and speech via SRS. Role "Info" = text only.
/// Prio: 0 urgent (before everything else), 1 normal, 2 AI radio. Standby: "…, stand by" beforehand if the frequency is still busy.
record Tx(string Text, string Role, string Station, int Gid, bool Pilot = false, string? Voice = null, uint Unit = 0, double[]? Freqs = null,   // Unit: intercom target (Role "Crew")
          int Prio = 1, Tx? Standby = null, double MaxAge = 0, bool Stress = false, int Side = 0, Func<string?>? Live = null, Action? Aired = null);   // MaxAge: afterwards text only (0 = per prio); Stress: combat voice; Side: SRS coalition of the sender (A133), 0 = Cfg.Coalition; Aired: when it goes out (Approach vectoring)

class AtisInfo { public char Letter = 'A'; public string Key = "", Text = "", Mp3 = "", Call = ""; public double Sec, Clk, TempC; public (double X, double Z) W; }   // Call: announcement on new identifier; Clk/TempC/W: time, temperature and wind of the identifier (R344)

static partial class Program
{
    const double NM = 1852;
    static readonly string Root = FindRoot();
    static readonly string Ffmpeg = File.Exists(Path.Combine(Root, "tools", "ffmpeg", "ffmpeg.exe")) ? Path.Combine(Root, "tools", "ffmpeg", "ffmpeg.exe") : "ffmpeg";   // bundled, otherwise PATH
    internal static Config Cfg = new();
    static List<Airfield> Fields = new();
    static readonly Dictionary<string, Pilot> Pilots = new();
    static readonly Pilot Solo = new() { Unit = "#solo" };   // without mission data
    static readonly object TowerLock = new();
    static readonly BlockingCollection<Tx> SayQueue = new();
    static Telemetry? Tel;
    static DateTime TelTime = DateTime.MinValue;
    static double HostAoa = double.NaN;   // angle of attack in degrees (Export.lua, own aircraft only) for the LSO
    static IReadOnlyList<Traffic> TrafficList = Array.Empty<Traffic>();
    static DateTime TrafficTime = DateTime.MinValue;

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        SetErrorMode(0x0003);   // #8 (issue): SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX, inherited by child processes - a crashing ExternalAudio exits quietly instead of an "Unknown Hard Error" box over DCS
        var lang = Path.Combine(AppContext.BaseDirectory, "lang.txt");   // Installer: UI language
        Ui.En = File.Exists(lang) && File.ReadAllText(lang).Trim() == "en";
        Tower.FreeLevel = FreeLevel;
        Ops.Callsigns = AiCallsign;
        TestRun = args.Any(a => a is "--selftest" or "--anflugtest" or "--mptest" or "--asr-test" or "--srs-test" or "--platztest" or "--replay");
        if (args.Length >= 1 && args[0] == "--charttest") { if (args.Length > 1) Airfield.ChartFile = args[1]; ChartTest(); return 0; }   // chart data check, optionally a file instead of the embedded one
        if (args.Contains("--platztest")) { Tower.PlatzTest(Airfield.Load(Path.Combine(Root, "airfields.txt"))); return 0; }
        if (args.Contains("--selftest")) { RadioTest(); ChartTest(); Tower.PlatzTest(Airfield.Load(Path.Combine(Root, "airfields.txt"))); QueueTest(); SideTest(); FreqTest(); CaptureTest(); ControlAiTest(); FunkTest(); ConfigTest(); Update.Test(); AtisTest(); CrewTest(); ExpectTest(); PromptTest(); GpuTest(); int rc = Tower.SelfTest(); ReplayTest(); return rc; }   // ReplayTest last: it runs the app loop (TickAll) and leaves static state behind
        LoadConfig();
        if (args.Length == 2 && args[0] == "--preview") { Preview(args[1]); return 0; }   // Test: DcsAtc --preview random (PC speakers, as the play button in the settings)
        if (args.Contains("--settings")) { Dedicated = DedicatedNow(); Settings.Show(Cfg); return 0; }   // language then also in the server's Saved Games
        if (args.Contains("--buttons")) { ShowButtons(); return 0; }
        if (args.Contains("--keys")) { SetupKeys(); return 0; }
        if (args.Contains("--updatetest")) return Update.NetTest();
        if (args.Contains("--frequencies")) { LoadFields(); Console.WriteLine(FreqPath); return 0; }   // create/extend frequencies.jsonc without DCS (e.g. server admin)
        if (args.Length >= 1 && args[0] == "--asr-test") return AsrTest(args.Length > 1 ? args[1] : Path.Combine(Root, "debrief", "asr"));
        if (args.Length >= 2 && args[0] == "--wheel-png") { Wheel.Snapshot(Cfg, args[1], args.ElementAtOrDefault(2)); return 0; }   // Preview: --wheel-png file.png ["role: suggestion"]
        if (args.Length >= 2 && args[0] == "--settings-png") { if (args.Length > 2) Ui.En = args[2] == "en"; Settings.Snapshot(Cfg, args[1], args.Length > 3 ? int.Parse(args[3]) : 0); return 0; }   // Preview: --settings-png file.png [de|en]
        if (args.Length == 2 && args[0] == "--wheel-live") { Wheel.LiveShot(Cfg, args[1]); return 0; }
        if (args.Length >= 1 && args[0] == "--kneeboard")   // DcsAtc --kneeboard <folder>
        {
            LoadFields();
            Kneeboard.Write(args.Length > 1 ? args[1] : Path.Combine(Root, "kneeboard"), Fields, Cfg);
            return 0;
        }
        if (args.Length >= 1 && args[0] == "--anflugtest") { LoadFields(); LoadTerrain(); return Tower.AnflugTest(Fields, args[1..]); }   // airfields from the repo airfields.txt, terrain of the last mission (%TEMP%)
        if (args.Length == 2 && args[0] == "--replay") return ReplayCli(args[1]);   // a reporter's atc-debug.txt through this build
        if (args.Length >= 1 && args[0] == "--mptest") { LoadFields(); LoadTerrain(); return MpTest(args[1..]); }   // multiple players + AI via the app logic

        TraceOn = Cfg.DebugLog;   // live from here: debug log (tests above without file)
        Dedicated = DedicatedNow();
        Trace("START", $"{DateTime.Now:yyyy-MM-dd} DCS-ATC {System.Windows.Forms.Application.ProductVersion} {string.Join(" ", args)} | {JsonSerializer.Serialize(Cfg)}");
        bool auto = args.Contains("--auto");   // started by the DCS hook at mission start
        using var single = new Mutex(true, "DcsAtc", out bool first);
        if (!first && !args.Contains("--say") && !args.Contains("--srs-test"))
        {
            if (auto) return 0;   // already running
            Console.WriteLine(L("DCS ATC läuft bereits in einem anderen Fenster. Taste drücken zum Schließen.", "DCS ATC is already running in another window. Press a key to close."));
            Console.ReadKey(true);
            return 1;
        }
        new Thread(() =>   // in the background: GitHub may be slow or unreachable
            {
                Update.Clean();
                if (Cfg.UpdateCheck && Update.Check() is { } up)
                    Log(L($"[ATC] Neue Version {up.Version} verfügbar (installiert: {Update.Own}). Einstellungen -> Allgemein -> \"Jetzt aktualisieren\".",
                          $"[ATC] New version {up.Version} available (installed: {Update.Own}). Settings -> General -> \"Update now\"."));
            }) { IsBackground = true }.Start();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);   // radio wheel error: no WinForms dialog, but crash -> restart
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception, args);
        if (auto)
        {
            if (Session()[0] == "client") Cfg.Mode = "client";
            new Thread(() =>   // left mission or DCS closed: app too
            {
                SetThreadExecutionState(0x80000000 | 0x1 | 0x2);   // ES_CONTINUOUS | SYSTEM | DISPLAY: no standby while DCS runs (HOTAS input does not count as activity for Windows); ends with the thread
                while (Session()[0] != "stop" && RunningDcs().Length > 0) Thread.Sleep(2000);
                if (dropBoard) DeleteBoardPages();   // English or pages off: gone from the next mission on (not earlier, DCS already listed them)
                Environment.Exit(0);
            }) { IsBackground = true }.Start();
        }
        if (Cfg.Mode == "client") return ClientMode();

        StartSrs();
        Directory.CreateDirectory(Path.Combine(Root, "tmp"));
        SrsListener.UseOpusFrom(Cfg.SrsPath);
        if (args.Contains("--srs-test"))   // log radio calls via SRS (without DCS)
        {
            LoadFields();
            new SrsListener(SrsPort, Cfg.Frequencies.Where(f => f.Key != "ATIS").Select(f => f.Value).Append(249.95), _ => false, tx =>
                Console.WriteLine($"{tx.FreqMHz:0.0} unit {tx.UnitId} '{tx.Name}' {tx.Pcm.Length / 16000.0:0.0}s: {Transcribe(SaveWav(tx.Pcm))}")).Start();   // (text, confidence)
            Thread.Sleep(Timeout.Infinite);
        }
        if (args.Length == 2 && args[0] == "--say")   // Test: DcsAtc --say "Kutaisi Tower, radio check"
        {
            SayQueue.Add(new Tx(args[1], "Tower", "Kutaisi Tower", 0)); SayQueue.CompleteAdding(); SpeakLoop();
            return 0;
        }
        LoadFields();
        new Thread(TelemetryLoop) { IsBackground = true }.Start();
        new Thread(SpeakLoop) { IsBackground = true }.Start();
        if (Cfg.MapPort > 0)   // FI7
            try { Lagebild.State = MapState; Lagebild.Start(Cfg.MapPort, Cfg.MapLan); Log(L($"[Lagebild] im Browser: http://localhost:{Cfg.MapPort}/", $"[Map] in the browser: http://localhost:{Cfg.MapPort}/")); }
            catch (SocketException e) { Log(L($"[Lagebild] Port {Cfg.MapPort} belegt, kein Lagebild: {e.Message}", $"[Map] port {Cfg.MapPort} in use, no map: {e.Message}")); }
        StartAtis();
        if (Cfg.SrsListen)
        {
            Srs = new SrsListener(SrsPort, Cfg.Frequencies.Where(f => f.Key != "ATIS").Select(f => f.Value), IsOwnStation, OnSrs);
            Srs.Start();
            new Thread(AsrWarm) { IsBackground = true }.Start();
        }
        if (!Dedicated) Wheel.Start(Cfg, text => { lock (TowerLock) Request(Host(), text, "RAD", true); }, () =>   // dedicated server: no radio wheel, no key hook
        {
            lock (TowerLock) { var h = Host(); var q = h?.Lead ?? h; Wheel.Field = q?.OnBoat ?? q?.Active?.F.Name ?? "ATC"; return Expected(h, h?.Ops.CasSuggest() ?? (q != null && (q.OnBoat != null || h!.Boat.Stage > 0 || h.Boat.Launching) ? h!.Boat.Suggest(q.Tel) : q?.Active?.Suggest(q.Tel))); }   // CAS contact: the next JTAC step first   // A76: at the carrier the suggestion of the carrier stage, not from the land tower; R24: on the deck nothing, on departure Departure; open question first (Expect.cs)
        });
        Wheel.Fits = () => { lock (TowerLock) return WheelFits(Host()); };   // R307: only matching entries
        Wheel.FreqOf = role =>
        {
            lock (TowerLock)
            {
                var h = Host(); var f = (h?.Lead ?? h)?.Active?.F;
                if (role == "AWACS" && h != null && Ops.AwacsFreqOf(h.Ops.MyAwacs(TrafficFor(h), MeOf(h)), MeOf(h)) is > 0 and var af) return af;   // mission AWACS (LA15: the one he checked in with)
                if (role == "Tanker" && h != null && h.Ops.TankerFreqFor(TrafficFor(h), MeOf(h)) is > 0 and var tf) return tf;   // TP7: mission tanker
                if (role == "JTAC" && h != null && h.Ops.JtacFreqFor(MeOf(h)) is > 0 and var jf) return jf;   // K3 (#7): mission JTAC instead of the config value
                if (role == "Center") return h?.Tel is { } ct && Tower.CenterFor(h.Coalition, ct.X, ct.Z) is { } cc ? cc.Freq : null;   // Center: sector or map frequency
                if (role == "Carrier" && h?.Tel is { } ht && Carrier.Boats.Where(b => b.Freq > 0 && b.Coalition == h.Coalition).MinBy(b => Dist(ht.X, ht.Z, b.X, b.Z)) is { } cb) return cb.Freq;
                return f != null && SpokenFreq(f, h!, (h!.Lead ?? h).Active!.Dep(role)) is > 0 and var sf ? sf : null;   // K4: tuned airfield frequency, otherwise VHF (= map); approach on departure: Departure (AirfieldFrequencies)
            }
        };
        Wheel.Untuned = (role, f) =>   // SRS knows the player's frequencies and none fits (airfield: UHF or VHF)
        {
            lock (TowerLock)
            {
                var h = Host(); var fld = (h?.Lead ?? h)?.Active?.F;
                var fs = (fld != null ? FieldFreqs(fld, (h?.Lead ?? h)!.Active!.Dep(role)) : null) ?? new[] { f };
                return h != null && Srs?.TunedFreqs(h.Id) is { } fq && !fs.Any(x => fq.Any(m => Math.Abs(m - x) < 0.005));
            }
        };
        Wheel.Near = () =>
        {
            lock (TowerLock) { var h = Host(); return NearPlaces(h?.Tel, h?.Coalition ?? 2); }
        };
        Wheel.Up = () =>
        {
            lock (TowerLock) { var h = Host(); return h == null ? (false, false) : Ops.WheelUp(TrafficFor(h), MeOf(h)); }
        };
        Wheel.Cv = () => Carrier.Boats.Count > 0;
        Wheel.Cen = () => { lock (TowerLock) { var h = Host(); return h != null && Tower.CenterName(h.Coalition) != null; } };   // Center: the map has one for the own side (module on)
        Wheel.Jtac = () => { lock (TowerLock) { var h = Host(); return h != null && Ops.Leaders.Any(l => l.Alive && l.Side == h.Coalition); } };   // J11
        Wheel.Jtacs = () => { lock (TowerLock) { var h = Host(); return h == null ? Array.Empty<string>() : Ops.Leaders.Where(l => l.Alive && l.Side == h.Coalition).Select(l => l.Cs).ToArray(); } };   // several JTAC / FAC(A): check in picks one
        Wheel.Log = Log;   // screenshot (press with wheel open) into atc-log
        Wheel.InGame = text =>   // VR: headset shows only DCS, wheel additionally as in-game text (mission reads .cmd every 0.25 s)
        {
            if (Cfg.WheelInGame is "off" or "window" || Cfg.WheelInGame != "on" && !VrOn()) return;   // "window": the picture in the game instead of the text (Wheel.ToGame)
            string? u;
            lock (TowerLock) u = Host()?.Unit;
            if (!string.IsNullOrEmpty(u)) AiCmd($"WHEEL;{u};{text}", false);
        };

        Console.WriteLine("DCS-ATC  |  " + string.Join("  ", Cfg.Frequencies.Select(f => $"{f.Key} {f.Value:0.0}")) + $" {Cfg.Modulation}");
        if (Dedicated) Console.WriteLine(L("Dedicated Server: kein Funkrad, keine Tasten, kein Mikrofon. Mitspieler sprechen über SRS oder nutzen ihr Funkrad (Client-Modus).",
                                           "Dedicated server: no radio wheel, no keys, no microphone. Players talk over SRS or use their radio wheel (client mode)."));
        else Console.WriteLine((Cfg.SrsListen ? L("Sprechen: einfach über SRS auf der Lotsen-Frequenz (alle Spieler). Ohne SRS-Verbindung: ", "Talk: just use SRS on the controller frequency (all players). Without SRS: ") : "") +
                          L("Push-to-Talk: Taste ", "Push-to-talk: key ") + $"0x{Cfg.PttKey:X2}" + (Cfg.PttJoystick >= 0 ? L($" oder Joystick {Cfg.PttJoystick} Knopf {Cfg.PttButton}", $" or joystick {Cfg.PttJoystick} button {Cfg.PttButton}") : ""));
        if (Cfg.WheelKey != 0 && !Dedicated) Console.WriteLine(L("Auswahlrad: Taste ", "Radio wheel: key ") + Wheel.KeyName(Cfg.WheelKey) +
                                                 (Cfg.WheelJoystick >= 0 ? L($" oder Joystick {Cfg.WheelJoystick} Knopf {Cfg.WheelButton}", $" or joystick {Cfg.WheelJoystick} button {Cfg.WheelButton}") : ""));
        Console.WriteLine(L("Fenster offen lassen. Beenden mit Strg+C.\n", "Keep this window open. Quit with Ctrl+C.\n"));

        WaveInEvent? rec = null;
        WaveFileWriter? writer = null;
        var stopped = new ManualResetEventSlim();
        var pressed = DateTime.MinValue;
        var nextTick = Clock;
        var nextMenu = Clock;
        if (Directory.Exists(MenuDir)) foreach (var f in Directory.GetFiles(MenuDir)) File.Delete(f);   // Legacy items
        bool down = false;
        int n = 0;

        while (true)
        {
            bool p = !Dedicated && PttDown() && Srs?.Connected != true;   // SRS listens -> do not evaluate own key twice; dedicated server: no microphone
            if (p && !down)
            {
                var wav = Path.Combine(Root, "tmp", $"ptt{++n % 4}.wav");
                rec = new WaveInEvent { DeviceNumber = MicDevice(), WaveFormat = new WaveFormat(16000, 16, 1) };
                writer = new WaveFileWriter(wav, rec.WaveFormat);
                var w = writer;
                rec.DataAvailable += (_, e) => w.Write(e.Buffer, 0, e.BytesRecorded);
                stopped.Reset();
                rec.RecordingStopped += (_, _) => stopped.Set();
                rec.StartRecording();
                pressed = Clock;
                Console.Write("[PTT] ...");
            }
            else if (!p && down && rec != null && writer != null)
            {
                Thread.Sleep(150);   // do not cut off the end of the sentence
                rec.StopRecording();
                stopped.Wait(1000);
                var file = writer.Filename;
                writer.Dispose(); rec.Dispose();
                Console.Write("\r");
                if ((Clock - pressed).TotalSeconds > 0.5)
                    Task.Run(() => Handle(file));
            }
            down = p;

            if (Clock >= nextMenu)
            {
                nextMenu = Clock.AddMilliseconds(250);
                lock (TowerLock) ReadState();
                PollMenu();
                FlightComms();
                try { Directory.CreateDirectory(MenuDir); File.WriteAllText(Path.Combine(MenuDir, "alive"), ""); } catch (IOException) { }   // heartbeat for the mission
            }
            if (Clock >= nextTick)
            {
                nextTick = Clock.AddSeconds(1);
                if (!auto && Session()[0] == "client" && File.GetLastWriteTime(Path.Combine(Path.GetTempPath(), "DcsAtc-Session.txt")) > Process.GetCurrentProcess().StartTime)
                {   // started by hand, then joined someone else's server: the host has the mission, this one would be a second ATC without AWACS/carrier -> client mode
                    Log(L("[ATC] Mitspieler auf fremdem Server: Neustart im Client-Modus", "[ATC] Joined another server: restart in client mode"));
                    single.ReleaseMutex();
                    Process.Start(Environment.ProcessPath!, "--auto");
                    Environment.Exit(0);
                }
                if (Clock >= nextScriptCheck) { nextScriptCheck = Clock.AddSeconds(15); CheckMissionScripting(); }
                if (File.GetLastWriteTimeUtc(ConfigPath) != cfgAt) ReloadLive();
                if (File.GetLastWriteTimeUtc(FreqPath) != freqAt) lock (TowerLock) { ApplyOwnFreqs(); Log("[ATC] frequencies.jsonc übernommen"); }   // own airfield frequencies immediately (airfield selection, monitoring, handovers, ATIS)
                TickAll();
            }
            Thread.Sleep(20);
        }
    }

    // follow along: console + atc-log.txt in the project folder
    static bool logDated;   // issue #7: date once per start, so a flight can be found
    internal static void Log(string s) { if (SimLog != null) { SimLog.Add(s); return; } Console.WriteLine(s); Trace("LOG", s); try { File.AppendAllText(Path.Combine(Root, "atc-log.txt"), (logDated ? "" : $"===== {DateTime.Now:yyyy-MM-dd} =====\r\n") + $"{Clock:HH:mm:ss} {s}\r\n"); logDated = true; } catch (IOException) { } }

    // Debug log for bug reports: atc-debug.txt "HH:mm:ss.fff [KAT] Text", rolls over to atc-debug.old.txt via TraceMax. Live only with Config.DebugLog (TraceOn), tests without file.
    internal static bool TraceOn;
    internal static string TracePath = Path.Combine(Root, "atc-debug.txt");
    internal static long TraceMax = 20_000_000;
    static readonly object traceLock = new();
    internal static void Trace(string kat, string text)
    {
        if (!TraceOn || SimLog != null) return;
        lock (traceLock)
            try
            {
                if (new FileInfo(TracePath) is { Exists: true } fi && fi.Length > TraceMax)
                    try { File.Move(TracePath, Path.ChangeExtension(TracePath, ".old.txt"), true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // .old open (editor): keep appending instead of losing everything
                File.AppendAllText(TracePath, $"{Clock:HH:mm:ss.fff} [{kat}] {text}\r\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    static string Fq(IEnumerable<double>? f) => f == null ? "?" : string.Join("/", f.Select(x => x.ToString("0.0##", CultureInfo.InvariantCulture)));

    /// Debug log per second (TickAll): state changes per player (airfield, phase per airfield, pin, carrier stage, AWACS, flight, new debrief lines) and telemetry every 10 s.
    /// why: trigger ("Tick" or the radio call with recognized intent); a call less than 2 s before is named.
    static readonly Dictionary<string, string> traced = new();
    static readonly Dictionary<string, (string Text, double At)> tracedReq = new();
    static double tracedTel = -1e9;
    static bool tracedMission;
    static double tracedBadState = -1e9;   // broken state line at most every 10 s (state 4×/s)
    static void TraceState(string why)
    {
        if (!TraceOn || SimLog != null) return;
        if (MissionData != tracedMission) { tracedMission = MissionData; Trace("MISSION", MissionData ? "Missionsdaten da" : "Missionsdaten weg (nur Export.lua)"); }
        foreach (var p in MissionData ? Pilots.Values.ToList() : new() { Solo })
        {
            var req = why == "Tick" && tracedReq.TryGetValue(p.Unit, out var r) && Now() - r.At < 2 ? FormattableString.Invariant($", Spruch vor {Now() - r.At:0.0} s: \"{r.Text}\"") : "";
            var now = new Dictionary<string, string>
            {
                ["Platz"] = p.Active?.F.Name ?? "-", ["Pin"] = p.Pinned == null ? "-" : p.Pinned.F.Name + (p.PinFreqs != null ? " (per Ruf, " + Fq(p.PinFreqs) + ")" : " (Funkrad/Notfall)"),
                ["Träger"] = $"{p.OnBoat ?? "-"} Stufe {p.Boat.Stage}", ["AWACS"] = p.Ops.AwacsIn ? "eingecheckt" : "-", ["Flug"] = p.Lead?.Callsign ?? "-",
                ["Tanker"] = p.Ops.TankerGroup == "" ? "-" : $"{p.Ops.TankerGroup} ({TrafficFor(p).FirstOrDefault(a => a.Group == p.Ops.TankerGroup)?.Type ?? "?"})",   // LA24: type (boom/basket) checkable in the log
            };
            foreach (var t in p.Towers.Values) now["Phase " + t.F.Name] = t.Phase.ToString();
            foreach (var (k, v) in now)
                if (traced.TryGetValue(p.Unit + "|" + k, out var o) ? o != v : v != "-" && v != "Parked" && !v.StartsWith("- Stufe 0"))
                {
                    Trace(k.StartsWith("Phase") ? "PHASE" : "ZUSTAND", $"{p.Callsign} {k}: {o ?? "-"} -> {v} ({why}{req})");
                    traced[p.Unit + "|" + k] = v;
                }
            int dn = traced.TryGetValue(p.Unit + "|#deb", out var ds) ? int.Parse(ds) : 0;
            if (dn > p.Ops.Debrief.Count) dn = 0;   // ResetDebrief (new sortie): from the start again
            foreach (var d in p.Ops.Debrief.Skip(dn)) Trace("DEBRIEF", $"{p.Callsign}: {d}");
            traced[p.Unit + "|#deb"] = p.Ops.Debrief.Count.ToString();
        }
        if (why != "Tick" || Now() - tracedTel < 10) return;
        tracedTel = Now();
        foreach (var p in ActivePilots())
        {
            if (p.Tel is not { } t) continue;
            var nf = Fields.MinBy(f => Dist(t.X, t.Z, f.X, f.Z));
            Trace("TEL", FormattableString.Invariant($"{p.Callsign} ({p.Type}, unit {p.Id}, gid {p.Gid}, Seite {p.Coalition}) X {t.X:0} Z {t.Z:0} {t.AltMsl / 0.3048:0} ft MSL {t.Agl / 0.3048:0} ft AGL {t.Ias / 0.514444:0} kt Kurs {((t.Hdg * 180 / Math.PI) % 360 + 360) % 360:000} ")
                + FormattableString.Invariant($"| nächster {nf?.Name} {(nf == null ? 0 : Dist(t.X, t.Z, nf.X, nf.Z) / NM):0.0} NM | aktiv {(p.Lead ?? p).Active?.F.Name ?? "-"} {(p.Lead ?? p).Active?.Phase} | SRS {Fq(Srs?.TunedFreqs(p.Id))} gerufen {p.CalledOn:0.0##} | Sprit {p.Fuel:0.00}")
                + (t.Agl < 3 && nf != null ? " | " + nf.RwyDiag(t.X, t.Z, ((t.Hdg * 180 / Math.PI) % 360 + 360) % 360) : ""));
        }
    }

    /// Debug log: why Pick chose this airfield (after Pick, same order as there).
    static string PickWhy(Pilot p, string? text, Tower t)
    {
        var fq = Srs?.TunedFreqs(p.Id);
        bool on = fq != null && FieldFreqs(t.F, "*") is { } fs && fs.Any(x => fq.Any(m => Math.Abs(m - x) < 0.005));
        return (text != null && Called(text, p.Tel, null) == t.F ? "gerufen"
              : p.Pinned == t ? (p.PinFreqs != null ? "Pin (auf Platzfrequenz gerufen)" : "Pin (Funkrad/Notfall)")
              : on ? (t == p.Active ? "Frequenz gerastet, laufender Ablauf" : "Frequenz neu gerastet")
              : t == p.Active ? "laufender Ablauf" : "nächster Platz") + $", SRS {Fq(fq)}";
    }
    static string? pickWhy;   // Request -> switch

    static double Now() => (double.IsNaN(pausedAt) ? Wall : pausedAt) - pauseOff;
    static double Wall => SimSec >= 0 ? SimSec : Environment.TickCount64 / 1000.0;
    /// LA17 (log 14.10.: 5 min identical telemetry at 536 kt, "turn right heading 175" every 35 s): own aircraft in the air at speed, same position 3 ticks in a row = game paused
    /// (Export keeps sending, the mission clock stands) -> Now() stands too: no reminders, all timers (tower, AWACS, tanker) continue afterwards as before.
    static void PauseCheck(Telemetry? t)
    {
        frozen = t is { Ias: > 50 * 0.514444, Agl: >= 3 } && frozenTel is { } f && (t.X, t.Z, t.AltMsl) == (f.X, f.Z, f.AltMsl) ? frozen + 1 : 0;
        frozenTel = t;
        if (frozen >= 3 && double.IsNaN(pausedAt)) pausedAt = Wall;
        else if (frozen == 0 && !double.IsNaN(pausedAt)) (pauseOff, pausedAt) = (pauseOff + Wall - pausedAt, double.NaN);
    }
    static double pauseOff, pausedAt = double.NaN;
    static int frozen;
    static Telemetry? frozenTel;
    static double SimSec = -1;   // --mptest: simulation clock (s), otherwise real time
    static DateTime Clock => SimSec >= 0 ? new DateTime(2000, 1, 1).AddSeconds(SimSec) : DateTime.Now;
    static List<string>? SimLog;   // --mptest: collect log instead of console/file
    static Action<string>? AiHook;   // --mptest: AI commands to the simulated AI instead of the mission

    static Telemetry? CurrentTel() => (Clock - TelTime).TotalSeconds < 3 ? Tel : null;
    static IReadOnlyList<Traffic> CurrentTraffic() => (Clock - TrafficTime).TotalSeconds < 5 ? TrafficList : Array.Empty<Traffic>();
    static double Dist(double x1, double z1, double x2, double z2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));

    static void Handle(string wav)
    {
        var (text, conf) = Transcribe(wav);
        if (!string.IsNullOrWhiteSpace(text)) lock (TowerLock) Request(Host(), text, "DU ", false, conf: conf);
    }

    // ------------------------------------------------------------ session from the DCS hook (%TMP%\DcsAtc-Session.txt: role, own name)
    static string[] session = { "", "" };
    static DateTime sessionRead;

    static string[] Session()
    {
        if ((Clock - sessionRead).TotalSeconds < 1) return session;
        sessionRead = Clock;
        try { var l = File.ReadAllLines(Path.Combine(Path.GetTempPath(), "DcsAtc-Session.txt")); session = new[] { l.ElementAtOrDefault(0) ?? "", l.ElementAtOrDefault(1) ?? "" }; }
        catch (IOException) { }
        return session;
    }

    // ------------------------------------------------------------ DCS updates reset MissionScripting.lua (the mod's line is missing)
    static DateTime nextScriptCheck = DateTime.MinValue;
    static bool fixAsked;

    // DCS installation: reported by the hook (lfs.currentdir), registry (also OpenBeta, dedicated server), Steam libraries; first with bin\DCS.exe or bin\DCS_server.exe
    const string DefaultDcs = @"C:\Program Files\Eagle Dynamics\DCS World";
    internal static string DcsDir() => DcsDirs().Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.TrimEndingDirectorySeparator(d!.Trim()))
                                       .FirstOrDefault(DcsExe) ?? DefaultDcs;
    internal static bool DcsExe(string dir) => DcsProcs.Any(n => File.Exists(Path.Combine(dir, "bin", n + ".exe")));

    // ------------------------------------------------------------ DCS process: game (DCS.exe) or dedicated server (DCS_server.exe)
    internal static readonly string[] DcsProcs = { "DCS", "DCS_server" };
    static string[] RunningDcs() => DcsProcs.Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
    /// Dedicated server without a player at the PC: DCS_server runs, the game does not (both on one PC = player present, as before).
    internal static bool ServerOnly(IEnumerable<string> running) =>
        running.Contains("DCS_server", StringComparer.OrdinalIgnoreCase) && !running.Contains("DCS", StringComparer.OrdinalIgnoreCase);
    static bool Dedicated;   // background operation: no radio wheel/overlay/key hook/microphone, Host() = nobody
    static bool DedicatedNow() => Cfg.Dedicated || ServerOnly(RunningDcs());

    static IEnumerable<string?> DcsDirs()
    {
        string? Reg(string key, string name) => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\" + key, name, null) as string;
        string Text(string f) { try { return File.ReadAllText(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } }
        yield return Text(Path.Combine(Path.GetTempPath(), "DcsAtc-DcsDir.txt"));
        yield return Reg(@"Eagle Dynamics\DCS World", "Path");
        yield return Reg(@"Eagle Dynamics\DCS World OpenBeta", "Path");
        if (Reg(@"Valve\Steam", "SteamPath") is string steam)
            foreach (var lib in SteamLibraries(Text(Path.Combine(steam, "steamapps", "libraryfolders.vdf"))).Prepend(steam))
                yield return Path.Combine(lib, "steamapps", "common", "DCSWorld");
        yield return DefaultDcs + " OpenBeta";
        yield return DefaultDcs;
        yield return Reg("DCS-ATC", "ServerDcs");   // dedicated server (chosen in the installer) only after all game installations
        yield return Reg(@"Eagle Dynamics\DCS World Server", "Path");
    }

    /// Steam libraries from libraryfolders.vdf ("path"  "D:\\SteamLibrary").
    internal static IEnumerable<string> SteamLibraries(string vdf) => Regex.Matches(vdf, @"""path""\s+""([^""]+)""").Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    /// Saved Games\DCS (or DCS.openbeta): chosen by the installer (HKCU\Software\DCS-ATC), otherwise Known Folder (movable in Windows), otherwise %USERPROFILE%\Saved Games.
    /// Dedicated server: the server's Saved Games (ServerSavedGames).
    internal static string SavedGamesDcs()
    {
        const string sf = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\", id = "{4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4}";
        var sg =ShellFolder(Microsoft.Win32.Registry.GetValue(sf + "User Shell Folders", id, null) as string)
              ?? ShellFolder(Microsoft.Win32.Registry.GetValue(sf + "Shell Folders", id, null) as string)
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        if (Dedicated)
        {
            string? hook = null;
            try { hook = File.ReadAllText(Path.Combine(Path.GetTempPath(), "DcsAtc-WriteDir.txt")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return ServerSavedGames(sg, hook, Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\DCS-ATC", "ServerSavedGames", null) as string);
        }
        return ClientSavedGames(sg, Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\DCS-ATC", "SavedGames", null) as string);
    }

    /// Forum 0.16: Saved Games\DCS (DCS.openbeta if only that is there) of the Windows Saved Games folder, the one DCS uses (also when moved);
    /// the folder chosen in setup only when that is missing (a stale choice after moving Saved Games used to win until uninstall).
    internal static string ClientSavedGames(string sg, string? chosen)
    {
        var own = Path.Combine(sg, !Directory.Exists(Path.Combine(sg, "DCS")) && Directory.Exists(Path.Combine(sg, "DCS.openbeta")) ? "DCS.openbeta" : "DCS");
        return !Directory.Exists(own) && !string.IsNullOrWhiteSpace(chosen) && Directory.Exists(chosen) ? chosen : own;
    }

    /// Saved Games of the dedicated server: reported by the hook (lfs.writedir(), also valid with start parameter -w <name>), otherwise chosen in the installer,
    /// otherwise the first existing default folder (DCS.server; newer server installers DCS.dcs_serverrelease/DCS.release_server), otherwise DCS.server.
    internal static string ServerSavedGames(string sg, params string?[] known) =>
        known.Select(d => d?.Trim()).FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)) is { } k ? Path.TrimEndingDirectorySeparator(k)
        : ServerDirs.Select(n => Path.Combine(sg, n)).FirstOrDefault(Directory.Exists) ?? Path.Combine(sg, ServerDirs[0]);
    internal static readonly string[] ServerDirs = { "DCS.server", "DCS.dcs_serverrelease", "DCS.release_server", "DCS.openbeta_server" };

    internal static string? ShellFolder(string? v) => string.IsNullOrWhiteSpace(v) ? null : Environment.ExpandEnvironmentVariables(v);   // REG_EXPAND_SZ: %USERPROFILE%\…

    // Retribution/Liberation copy their own MissionScripting.lua (resources\scripts) over DCS's while their app is open, the original comes back on exit:
    // add the line to their copy too (no admin), so the mission keeps DCS-ATC. Their copy has sanitizing commented out -> line at the end.
    const string MissionLine = "pcall(function() local f = lfs.writedir() .. 'Scripts/DcsAtc/DcsAtcMission.lua' if lfs.attributes(f) then dofile(f) end end)   -- DCS-ATC (Mod)";
    internal static string? WithMissionLine(string t) => t.Contains("DcsAtcMission") ? null : t.TrimEnd() + $"\r\n\r\n{MissionLine}\r\n";
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    /// Path of a running exe. Process.MainModule fails with "access denied" when the other program runs as admin (Retribution/Liberation often do,
    /// they write into the DCS folder); the limited query right is enough for the path.
    internal static string ExePath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : throw new System.ComponentModel.Win32Exception();
        }
        finally { CloseHandle(h); }
    }

    static bool campaignErr;
    static void PatchCampaignTool()
    {
        foreach (var p in Process.GetProcessesByName("retribution_main").Concat(Process.GetProcessesByName("liberation_main")))
            try
            {
                var f = Path.Combine(Path.GetDirectoryName(ExePath(p.Id))!, "resources", "scripts", "MissionScripting.lua");
                if (File.Exists(f) && WithMissionLine(File.ReadAllText(f)) is { } n)
                {
                    File.WriteAllText(f, n);
                    Log(L($"[ATC] DCS-ATC-Zeile in {f} eingetragen (Retribution/Liberation ersetzt damit MissionScripting.lua)",
                          $"[ATC] DCS-ATC line added to {f} (Retribution/Liberation replaces MissionScripting.lua with it)"));
                }
            }
            catch (Exception e) when (!campaignErr)
            {
                campaignErr = true;
                Log(L($"[ATC] Retribution/Liberation: Vorlage nicht angepasst ({e.Message})", $"[ATC] Retribution/Liberation: template not patched ({e.Message})"));
            }
            catch (Exception) { }
    }

    static void CheckMissionScripting()
    {
        PatchCampaignTool();
        var f = Path.Combine(DcsDir(), "Scripts", "MissionScripting.lua");
        var a = Path.Combine(DcsDir(), "Scripts", "autoexec.lua");   // wingmen by voice (DcsAtcWing.lua), game only
        var fix = Path.Combine(Root, "DcsAtc-Patch.ps1");
        try
        {
            if (fixAsked || !File.Exists(f) || !File.Exists(fix) || File.ReadAllText(f).Contains("DcsAtcMission")
                && (!File.Exists(a) || !File.Exists(Path.Combine(DcsDir(), "bin", "DCS.exe")) || File.ReadAllText(a).Contains("DcsAtcWing"))) return;
            fixAsked = true;   // ask only once per app start
            Log(L("[ATC] DCS-ATC-Zeile in MissionScripting.lua oder autoexec.lua fehlt (DCS-Update, neue Version) -> Admin-Abfrage (UAC). DCS danach neu starten.",
                  "[ATC] DCS-ATC line missing in MissionScripting.lua or autoexec.lua (DCS update, new version) -> admin prompt (UAC). Restart DCS afterwards."));
            Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{fix}\" -Dcs \"{Path.GetDirectoryName(Path.GetDirectoryName(f))}\"")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (Exception e)
        {
            Log(L($"[ATC] MissionScripting.lua nicht angepasst ({e.Message}) -> Startmenü: DCS-ATC -> \"Nach DCS-Update reparieren\".",
                  $"[ATC] MissionScripting.lua not patched ({e.Message}) -> Start menu: DCS-ATC -> \"Repair after DCS update\"."));
        }
    }

    // ------------------------------------------------------------ Save session: app crash/restart mid-flight (phase, check-ins, debriefing are kept)
    record PilotSave(double X, double Z, string? Pinned, string? OnBoat, DateTime? Airborne, string? Field, Tower.State? Tower, Carrier.State Boat, Ops.State Ops,
                     double Sec = -1, Dictionary<string, char>? Atis = null);   // LA21: mission run time and ATIS identifiers at saving
    static string SessionFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Session.json");
    static Dictionary<string, PilotSave>? saved;   // loaded at the first player, applied once per unit
    static double savedAt = -99, savedAge;   // savedAge: s between the last save and the restart

    static void SaveSession()
    {
        if (SimSec >= 0 || saved == null || Now() - savedAt < 5) return;   // not before the first player read the old state: the empty start would overwrite it
        savedAt = Now();
        var d = Pilots.Values.Where(p => p.Tel != null).ToDictionary(p => p.Unit, p => new PilotSave(p.Tel!.X, p.Tel.Z, p.Pinned?.F.Name, p.OnBoat, p.Airborne,
                    p.Active?.F.Name, p.Active?.Save(), p.Boat.Save(), p.Ops.Save(), missionSec, Atis.ToDictionary(a => a.Key, a => a.Value.Letter)));
        try { File.WriteAllText(SessionFile + ".tmp", JsonSerializer.Serialize(d)); File.Move(SessionFile + ".tmp", SessionFile, true); } catch (IOException e) { Trace("FEHLER", $"Sitzung sichern: {e.Message}"); }
    }

    /// New player: take over the pre-crash state if the file is younger than 10 min and he is still flying there (otherwise mission restart).
    static void RestoreSession(Pilot p)
    {
        if (saved == null)
        {
            saved = new();
            try { if ((savedAge = (Clock - File.GetLastWriteTime(SessionFile)).TotalSeconds) < 600) saved = JsonSerializer.Deserialize<Dictionary<string, PilotSave>>(File.ReadAllText(SessionFile)) ?? new(); }
            catch (Exception e) when (e is IOException or JsonException) { Trace("FEHLER", $"Sitzung laden: {e.GetType().Name}: {e.Message}"); }
        }
        // LA21 (log: "ClearedTakeoff" and 2 debrief entries taken into the restarted mission): mission run time smaller than at saving = new mission, nothing to take over
        if (p.Tel is not { } t || !saved.Remove(p.Unit, out var s) || missionSec < s.Sec) return;
        foreach (var (fn, l) in s.Atis ?? new()) Atis.TryAdd(fn, new AtisInfo { Letter = l });   // LA21 (log: November -> Victor in the same mission): identifier stays, no announcement
        if (Dist(t.X, t.Z, s.X, s.Z) > (3 + savedAge * 0.2) * NM) return;   // 0.2 NM/s (720 kt): a jet flies on during the restart
        Airfield? F(string? name) => Fields.FirstOrDefault(f => f.Name == name && Served(p.Coalition, f));   // N1: do not take over hostile (captured)
        if (s.Tower != null && F(s.Field) is { } af) { p.Active = TowerFor(p, af); p.Active.Load(s.Tower, t, Now()); }
        p.Pinned = F(s.Pinned) is { } pf ? TowerFor(p, pf) : null;
        (p.OnBoat, p.Airborne) = (s.OnBoat, s.Airborne);
        p.Boat.Load(s.Boat);
        p.Ops.Load(s.Ops);
        Log($"[ATC] {p.Callsign}: Stand von vor dem Neustart übernommen ({s.Field} {s.Tower?.Phase}, Träger-Stufe {s.Boat.Stage}, {s.Ops.Debrief.Count} Wertungen)");
    }

    /// Crash: crash log; session is saved (every 5 s), app restarts after 3 s – not if it already crashes at startup.
    static void Crash(Exception? e, string[] args)
    {
        try { File.AppendAllText(Path.Combine(Root, "crash-log.txt"), $"{Clock:yyyy-MM-dd HH:mm:ss} {e}\r\n\r\n"); } catch (IOException) { }
        if ((Clock - Process.GetCurrentProcess().StartTime).TotalSeconds < 60 || Environment.ProcessPath is not { } exe) return;
        Process.Start(new ProcessStartInfo("cmd", $"/c timeout /t 3 /nobreak >nul & start \"\" \"{exe}\" {string.Join(" ", args.Select(a => $"\"{a}\""))}")
            { CreateNoWindow = true, UseShellExecute = false });
    }

    static void Switch(Pilot p, Tower t)
    {
        if (t == p.Active) return;
        t.SetCallsign(p.Active?.Callsign ?? p.Callsign);
        if (p.Active != null) { t.TakeCenter(p.Active); t.Squawk = p.Active.Squawk ?? t.Squawk; p.Active.Squawk = null; }   // V15: flight following moves along, A90: transponder code per flight too
        Trace("PLATZ", $"{p.Callsign}: {p.Active?.F.Name ?? "-"} -> {t.F.Name} ({pickWhy ?? PickWhy(p, null, t)})");
        pickWhy = null;
        p.Active = t;
        Log($"[ATC] {p.Callsign}: jetzt {t.F.Name}");
    }

    // ------------------------------------------------------------ F10 menu of the mission (KutaisiATC.lua): "unit|text"
    static readonly string MenuDir = Path.Combine(Path.GetTempPath(), "DcsAtc-Menu");

    static void PollMenu()
    {
        if (!Directory.Exists(MenuDir)) return;
        foreach (var f in Directory.GetFiles(MenuDir, "*.txt").Order())
        {
            string text;
            try { text = File.ReadAllText(f).Trim(); File.Delete(f); } catch (IOException) { continue; }
            int bar = text.IndexOf('|');
            Trace("MISSION", "Menü " + text);
            Pilot? p;
            var key = bar > 0 ? text[..bar] : "";
            lock (TowerLock)   // "unit|..." (F10), "@spielername|..." (radio wheel of a teammate via DcsAtcHook.lua)
                p = bar <= 0 || !MissionData ? Host()
                  : key.StartsWith("@") ? Pilots.Values.FirstOrDefault(x => x.Name == key[1..]) : Pilots.GetValueOrDefault(key);
            Request(p, bar > 0 ? text[(bar + 1)..] : text, key.StartsWith("@") ? "RAD" : "F10", true);
        }
        foreach (var f in Directory.GetFiles(MenuDir, "*.evt").Order())
        {
            string line;
            try { line = File.ReadAllText(f).Trim(); File.Delete(f); } catch (IOException) { continue; }
            Trace("MISSION", "Ereignis " + line);
            OnEvent(line);
        }
    }

    static int wavNo;
    static string SaveWav(short[] pcm)
    {
        var wav = Path.Combine(Root, "tmp", $"srs{Interlocked.Increment(ref wavNo) % 32}.wav");   // #11: 32, calls now queue for whisper
        using var w = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1));
        w.WriteSamples(pcm, 0, pcm.Length);
        return wav;
    }

    // ------------------------------------------------------------ Telemetry from DCS (DcsAtcExport.lua): own aircraft
    /// S;alt;agl;ias;hdg;x;z;windx;windz;pressure[;aoa[;lockx;lockz;lockalt]] -> telemetry (lock: first locked target) and AoA in rad.
    static (Telemetry Tel, double Aoa) ParseSelf(string msg)
    {
        var v = msg.Split(';').Skip(1).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return (new Telemetry(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], Lock: v.Length >= 13 ? (v[10], v[11], v[12]) : null), v.Length > 9 ? v[9] : 0);
    }

    static void TelemetryLoop()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, Cfg.TelemetryPort));
        var ep = new IPEndPoint(IPAddress.Any, 0);
        bool first = true;
        var prev = new Dictionary<int, (double X, double Z, DateTime T)>();
        double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        while (true)
        {
            var msg = System.Text.Encoding.UTF8.GetString(udp.Receive(ref ep));
            try
            {
                if (msg.StartsWith("S;"))
                {
                    (Tel, var aoa) = ParseSelf(msg);
                    HostAoa = aoa != 0 ? AoaDeg(aoa) : double.NaN;
                    TelTime = Clock;
                    if (first) { Console.WriteLine(L("      Telemetrie von DCS empfangen.", "      Telemetry from DCS received.")); first = false; }
                }
                else if (msg.StartsWith("T;"))   // T;id,type,x,z,alt,hdg;id,... (used only without mission data)
                {
                    var now = Clock;
                    var list = new List<Traffic>();
                    foreach (var item in msg.Split(';').Skip(1).Where(s => s.Length > 0))
                    {
                        var f = item.Split(',');
                        int id = int.Parse(f[0]);
                        double x = D(f[2]), z = D(f[3]), speed = 0;
                        if (prev.TryGetValue(id, out var p) && (now - p.T).TotalSeconds > 0.2)
                            speed = Math.Sqrt((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z)) / (now - p.T).TotalSeconds;
                        prev[id] = (x, z, now);
                        list.Add(new Traffic(id, f[1], x, z, D(f[4]), D(f[5]), speed));
                    }
                    TrafficList = list;
                    TrafficTime = now;
                }
            }
            catch (Exception e) { Console.WriteLine(L("      Telemetrie unlesbar: ", "      Telemetry unreadable: ") + e.Message); }
        }
    }

    // ------------------------------------------------------------ Push-to-talk
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [DllImport("winmm.dll")] static extern int joyGetPosEx(int id, ref JOYINFOEX info);

    [StructLayout(LayoutKind.Sequential)]
    public struct JOYINFOEX
    {
        public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }

    /// Buttons (bitmask) and coolie hat (hundredths of a degree, 0 = front, 65535 = center) of a joystick.
    public static (int Buttons, int Pov) Joy(int id) => JoyState(id) ?? (0, 65535);
    /// null = the device did not answer (not ready yet, unplugged): not the same as "no button pressed".
    public static (int Buttons, int Pov)? JoyState(int id)
    {
        var j = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = 0x80 | 0x40 };   // JOY_RETURNBUTTONS | JOY_RETURNPOV
        return joyGetPosEx(id, ref j) == 0 ? (j.dwButtons, j.dwPOV) : null;
    }

    static bool PttDown() =>
        (GetAsyncKeyState(Cfg.PttKey) & 0x8000) != 0 ||
        (Cfg.PttJoystick >= 0 && (Joy(Cfg.PttJoystick).Buttons & (1 << (Cfg.PttButton - 1))) != 0);

    /// Setup/start menu: set radio wheel and PTT key by pressing (keyboard or HOTAS), writes config.jsonc.
    static void SetupKeys()
    {
        var path = Path.Combine(Root, "config.jsonc");
        var cfg = File.ReadAllText(path);
        Console.WriteLine(L("DCS-ATC – Tasten festlegen. Gewünschte Taste oder HOTAS-Knopf drücken, Esc = unverändert.\n", "DCS-ATC – set keys. Press the key or HOTAS button you want, Esc = keep.\n"));
        cfg = Bind(cfg, L($"Funkrad öffnen/schließen (jetzt {Wheel.KeyName(Cfg.WheelKey)})", $"Open/close radio wheel (now {Wheel.KeyName(Cfg.WheelKey)})"), "Wheel");
        cfg = Bind(cfg, L($"Push-to-Talk – nur nötig ohne SRS-Sprechtaste (jetzt {Wheel.KeyName(Cfg.PttKey)})", $"Push-to-talk – only needed without the SRS PTT key (now {Wheel.KeyName(Cfg.PttKey)})"), "Ptt");
        File.WriteAllText(path, cfg);
        Console.WriteLine(L("\nGespeichert. Gilt ab dem nächsten Start von DCS-ATC. Taste drücken zum Schließen.", "\nSaved. Takes effect the next time DCS-ATC starts. Press a key to close."));
        Thread.Sleep(500);
        Console.ReadKey(true);
    }

    internal static string ConfigPath => cfgFile ?? Path.Combine(Root, "config.jsonc");
    static string? cfgFile;   // selftest only (R392): another config for ReloadLive
    /// Saved Games\DCS (or DCS.openbeta if only that is there)
    internal static string DcsSaved => SavedGamesDcs();
    /// DCS in VR mode: start parameters --force_enable_VR / --force_disable_VR (Logs\dcs.log "Command line:") before Config\options.lua, VR = { … ["enable"] = true … }
    internal static bool VrOn(string? options = null, string? log = null)
    {
        static string Head(string f, int n)   // DCS keeps dcs.log open
        {
            try
            {
                using var r = new StreamReader(new FileStream(Path.Combine(SavedGamesDcs(), f), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                var b = new char[n];
                return new string(b, 0, r.ReadBlock(b, 0, n));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        }
        var cmd = Regex.Match(log ?? Head(@"Logs\dcs.log", 8192), "Command line:.*").Value;
        if (cmd.Contains("--force_disable_VR", StringComparison.OrdinalIgnoreCase)) return false;
        if (cmd.Contains("--force_enable_VR", StringComparison.OrdinalIgnoreCase)) return true;
        return Regex.IsMatch(options ?? Head(@"Config\options.lua", 1 << 20), @"\[""VR""\]\s*=\s*\{[^}]*?\[""enable""\]\s*=\s*true\b");
    }

    /// Set a value (JSON: number, true/false, "text") in config.jsonc (comments stay); if the key is missing, insert after "after".
    internal static void SetConfigValue(string key, string value, string after)
    {
        try { File.WriteAllText(ConfigPath, WithConfigValue(File.ReadAllText(ConfigPath), key, value, after)); }
        catch (IOException) { }
    }

    internal static string WithConfigValue(string c, string key, string value, string after)
    {
        var re = new Regex($@"(""{key}""\s*:\s*)(-?[\d.]+|true|false|""(?:[^""\\]|\\.)*"")");
        if (re.IsMatch(c)) return re.Replace(c, m => m.Groups[1].Value + value, 1);
        var at = new Regex($@"""{after}""\s*:\s*(-?[\d.]+|true|false|""(?:[^""\\]|\\.)*"")\s*,");   // insert after it, otherwise right after "{"
        var m = at.Match(c) is { Success: true } a ? a : Regex.Match(c, @"\{");
        return c.Insert(m.Index + m.Length, $"\n  \"{key}\": {value},");
    }

    /// "Enfield11", "viper 1 1" -> "Enfield 1-1", "Viper 1-1"; no callsign -> null.
    internal static string? NormCallsign(string s) => Regex.Match(s.Trim(), @"^([A-Za-z]+)\s*(\d)\s*-?\s*(\d)$") is { Success: true } m
        ? $"{char.ToUpperInvariant(m.Groups[1].Value[0])}{m.Groups[1].Value[1..]} {m.Groups[2].Value}-{m.Groups[3].Value}" : null;

    /// Forum 0.13: mission unit name as call sign, "VICTORY 9-1", "WOLF-2-1" -> "Victory 9-1", "Wolf 2-1"; ME default (Aerial-1-1, Rotary-1-1) and other names -> null
    internal static string? UnitCallsign(string unit) => Regex.Match(unit.Trim(), @"^(?!(?i:aerial|rotary)\b)([A-Za-z]+)[\s-]*(\d)\s*-\s*(\d)$") is { Success: true } m
        ? $"{char.ToUpperInvariant(m.Groups[1].Value[0])}{m.Groups[1].Value[1..].ToLowerInvariant()} {m.Groups[2].Value}-{m.Groups[3].Value}" : null;

    /// Forum (Abr): CallsignFromUnit for AI too: a flight, AWACS or tanker whose mission unit is named like a call sign ("SENTRY 1-1") is called that, otherwise the DCS call sign
    internal static string AiCs(string unit, string dcs) => Cfg.CallsignFromUnit && UnitCallsign(unit) is { } uc ? uc : dcs;

    static void Rename(Pilot p, string cs)
    {
        if (p.Callsign != "") Log($"[ATC] {p.Callsign}: Rufzeichen jetzt {cs}");
        p.Callsign = cs;
        foreach (var t in p.Towers.Values) t.SetCallsign(cs);
    }

    /// Settings window has saved: apply immediately what works without restart (microphone, keys, language, SRS only at the next start).
    static void ReloadLive()
    {
        var old = Cfg;
        LoadConfig();
        var n = Cfg;
        Cfg = old;
        (Cfg.MyCallsign, Cfg.Volume, Cfg.SpeechRate, Cfg.ReplyPause, Cfg.RadioFx, Cfg.RadioText, Cfg.PilotVoice, Cfg.AiChatter, Cfg.AiFlightComms, Cfg.Atis, Cfg.AltimeterUnit, Cfg.WheelInGame, Cfg.AirspaceWatch, Cfg.RunwayLights, Cfg.CallsignFromUnit) =   // R392: RunwayLights is read per call (line ~995), applies immediately
            (n.MyCallsign, n.Volume, n.SpeechRate, n.ReplyPause, n.RadioFx, n.RadioText, n.PilotVoice, n.AiChatter, n.AiFlightComms, n.Atis, n.AltimeterUnit, n.WheelInGame, n.AirspaceWatch, n.RunwayLights, n.CallsignFromUnit);
        Cfg.HintText = n.HintText;
        (Cfg.IffCheck, Cfg.StationPositions) = (n.IffCheck, n.StationPositions);   // Tower.IffCheck is set by LoadConfig, StationPositions is read per transmission
        (Cfg.RandomNotams, Cfg.Notams) = (n.RandomNotams, n.Notams);
        SetNotams();
        Flights.Mode = Cfg.AiFlightComms;   // Tower.AltimeterUnit, Flights.Cmd/Follow and the modules are set by LoadConfig
        (Cfg.DebugLog, TraceOn) = (n.DebugLog, n.DebugLog);
        Log("[ATC] Einstellungen übernommen");
    }

    static string Bind(string cfg, string what, string name)
    {
        static string Set(string c, string key, int v) => Regex.Replace(c, $@"(""{key}""\s*:\s*)-?\d+",m => m.Groups[1].Value + v);
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        Console.Write(what + ": ");
        while (Enumerable.Range(8, 247).Any(Down)) Thread.Sleep(20);   // release the previous key first
        var prev = Enumerable.Range(0, 16).Select(id => Joy(id).Buttons).ToArray();
        while (true)
        {
            foreach (var vk in Enumerable.Range(8, 247).Where(vk => vk is not (0x10 or 0x11 or 0x12) && Down(vk)))   // Shift/Ctrl/Alt left/right only
            {
                if (vk == 0x1B) { Console.WriteLine(L("unverändert", "unchanged")); return cfg; }
                Console.WriteLine(Wheel.KeyName(vk));
                return Set(cfg, name + "Key", vk);
            }
            for (int id = 0; id < 16; id++)
            {
                int b = Joy(id).Buttons, now = b & ~prev[id];
                prev[id] = b;
                if (now == 0) continue;
                int k = System.Numerics.BitOperations.TrailingZeroCount(now) + 1;
                Console.WriteLine(L($"Joystick {id}, Knopf {k}", $"Joystick {id}, button {k}"));
                return Set(Set(cfg, name + "Joystick", id), name + "Button", k);
            }
            Thread.Sleep(20);
        }
    }

    static void ShowButtons()
    {
        Console.WriteLine(L("Drück Knöpfe an Joystick/HOTAS – ID und Knopfnummer werden angezeigt. Beenden mit Strg+C.", "Press joystick/HOTAS buttons – ID and button number are shown. Quit with Ctrl+C."));
        var prev = new int[16];
        while (true)
        {
            for (int id = 0; id < 16; id++)
            {
                int b = Joy(id).Buttons;
                for (int k = 0; k < 32; k++)
                    if ((b & ~prev[id] & (1 << k)) != 0) Console.WriteLine($"Joystick = {id}, Button = {k + 1}");
                prev[id] = b;
            }
            Thread.Sleep(30);
        }
    }

    static int MicDevice()
    {
        if (Cfg.MicName == "") return -1;   // WAVE_MAPPER = Windows default
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            if (WaveInEvent.GetCapabilities(i).ProductName.Contains(Cfg.MicName, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // ------------------------------------------------------------ Start
    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "models"))) return d.FullName;
        return AppContext.BaseDirectory;
    }

    /// Old defaults in installed configs (the installer does not overwrite them): low pitch voices, slow pace, AWACS/tanker frequency. Own values stay.
    internal static readonly (string Old, string New)[] ConfigMigrations =
    {
        ("\"Ground\": \"en_US-ryan-medium@0.88\"", "\"Ground\": \"en_US-bryce-medium\""),
        ("\"Tanker\": \"en_US-joe-medium@0.9\"", "\"Tanker\": \"en_US-bryce-medium\""),
        ("\"Crew\": \"en_US-joe-medium@0.82\"", "\"Crew\": \"en_US-ryan-medium\""),
        ("\"AWACS\": 251.0, \"Tanker\": 255.0", "\"AWACS\": 251.5, \"Tanker\": 255.5"),   // K3: were on Krasnodar-Center/Gelendzhik UHF
        ("\"SpeechRate\": 1.0,", "\"SpeechRate\": 0.85,"),   // old default spoke too slowly (not 1.1: that is how Settings writes a chosen pace 1.10)
        ("\"AWACS\": \"en_US-joe-medium@1.1|en_US-ljspeech-medium\"", "\"AWACS\": \"en_GB-alan-medium|en_US-ljspeech-medium\""),   // A142: AWACS/carrier not with the pilot voice (joe)
        ("\"AWACS\": \"en_US-joe-medium@1.1\"", "\"AWACS\": \"en_GB-alan-medium\""),
        ("\"Carrier\": \"en_US-ryan-medium@0.97|en_US-joe-medium@1.02\"", "\"Carrier\": \"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02\""),
        ("\"AwacsBullseye\": false,    // AWACS-Picture als Bullseye statt BRAA vom Spieler (false = BRAA)", "\"AwacsBullseye\": true,     // AWACS-Picture und Lage-Updates an alle als Bullseye (Mission mit Bullseye); false = alles BRAA vom Spieler"),   // LK4: old default BRAA
    };

    static DateTime cfgAt;   // State of config.jsonc (ReloadLive)

    /// Modules picked in the installer or the settings window: modules.txt next to the exe ("atc,awacs,…"), mission script: DcsAtcModules.txt. Missing file = all on.
    internal static readonly (string Id, string De, string En)[] Modules =
    {
        ("atc", "Flugsicherung", "Air traffic control"), ("range", "Schießplatz", "Range"), ("awacs", "AWACS", "AWACS"), ("tanker", "Tanker", "Tanker"),
        ("carrier", "Flugzeugträger", "Carrier"), ("ai", "KI-Funk", "AI radio"), ("crew", "Bodenpersonal", "Ground crew"), ("jtac", "JTAC / FAC(A)", "JTAC / FAC(A)"),
        ("center", "Center (Streckenkontrolle)", "Center (en-route control)"),
    };
    static HashSet<string>? Mods;
    internal static bool On(string m) => Mods == null || Mods.Contains(m);
    static bool Off(string m, string text) { if (On(m)) return false; Log($"[ATC] Modul {m} aus: {text}"); return true; }
    static bool RoleOn(string role) => role switch
    {
        "Ground" or "Tower" or "Approach" or "Departure" or "ATIS" => On("atc"),
        "AWACS" or "Tanker" or "Range" or "Crew" => On(role.ToLowerInvariant()),
        "JTAC" or "FACA" => On("jtac"), "Center" => On("center"),
        "Carrier" or "RedCrown" => On("carrier"), "Flight" => On("ai"), _ => true,
    };
    /// Radio wheel entry by its text: prefix = module, unprefixed = ATC (emergency, weather, ATIS); radio check, say again, debrief, settings always.
    internal static bool TextOn(string t) =>
        t.StartsWith("Range:") ? On("range") : t.StartsWith("AWACS:") ? On("awacs") && (t != "AWACS: vector to tanker" || On("tanker"))
        : t.StartsWith("Tanker:") ? On("tanker") : t.StartsWith("Carrier:") ? On("carrier") : t.StartsWith("JTAC:") ? On("jtac") : t.StartsWith("Center:") ? On("center")
        : t is "radio check" or "say again" or "debrief" or "settings" || On("atc");
    internal static void LoadModules(string? text) =>
        Mods = text?.ToLowerInvariant().Split(new[] { ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    static void LoadConfig()
    {
        var mf = Path.Combine(AppContext.BaseDirectory, "modules.txt");
        LoadModules(File.Exists(mf) && !TestRun ? File.ReadAllText(mf) : null);
        var path = ConfigPath;
        cfgAt = File.GetLastWriteTimeUtc(path);
        if (File.Exists(path))
        {
            string text = File.ReadAllText(path), mig = text;
            foreach (var (o, n) in ConfigMigrations) mig = mig.Replace(o, n);
            if (mig != text && !TestRun) try { File.WriteAllText(path, mig); } catch (IOException) { }   // Tests (--anflugtest/--mptest from a worktree: root is the checkout with models) do not write config
            Cfg = JsonSerializer.Deserialize<Config>(mig,
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new();
        }
        foreach (var kv in new Config().Frequencies) Cfg.Frequencies.TryAdd(kv.Key, kv.Value);
        foreach (var kv in new Config().Voices) Cfg.Voices.TryAdd(kv.Key, kv.Value);
        (Tower.CenterNames, Tower.CenterSectors) = (new(Cfg.CenterNames, StringComparer.OrdinalIgnoreCase), Cfg.CenterSectors.Concat(OwnSectors).ToList());
        (Tower.CenterOn, Tower.QnhOf) = (() => On("center"), FieldQnhOf);
        Cfg.Voices.TryAdd("Departure", Cfg.Voices["Approach"]);   // AirfieldFrequencies: Departure speaks with the Approach voice
        Cfg.SrsPath = FindSrs(Cfg.SrsPath);
        Tower.AltimeterUnit = Cfg.AltimeterUnit.ToLowerInvariant();
        (Ops.AwacsBullseye, Ops.NewGroupNm) = (Cfg.AwacsBullseye, Math.Clamp(Cfg.AwacsNewGroupNm, 40, 200));
        Carrier.OnSpeedAoa = new(Carrier.OnSpeedDefault);
        foreach (var kv in Cfg.OnSpeedAoa) Carrier.OnSpeedAoa[kv.Key] = kv.Value;
        Ops.Vectors = Cfg.TankerVectors;
        Tower.Watch = Cfg.AirspaceWatch && On("atc");
        Tower.IffCheck = Cfg.IffCheck;
        (Flights.Mode, Flights.RadiusNm, Flights.OwnWingmen) = (Cfg.AiFlightComms, Cfg.AiFlightRadiusNm, Cfg.AiOwnWingmen);
        if (!TestRun) Flights.Cmd = l => AiCmd(l);   // was only set by ReloadLive: without a config change the AI never got ENGAGE/RTB
        Flights.Follow = Cfg.AiFollowAwacs && On("awacs");   // LD1 ENGAGE only with AiFollowAwacs, LD7 RTB always
        Wheel.SaveSize = v => SetConfigValue("WheelSize", v.ToString(CultureInfo.InvariantCulture), "WheelButton");
    }

    /// SRS folder: configured path, otherwise from the SRS installation (registry) or the running SRS (other drive/directory, forum 0.9.5).
    internal static string FindSrs(string path) =>
        SrsRoot(path) ?? SrsRoot(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\DCS-SR-Standalone", "SRPathStandalone", null) as string) ??
        new[] { "SR-ClientRadio", "SRS-Server", "SR-Server" }.SelectMany(Process.GetProcessesByName)
            .Select(p => { try { return SrsRoot(p.MainModule!.FileName); } catch (Exception) { return null; } }).FirstOrDefault(d => d != null) ?? path;
    internal static bool SrsOk(string? dir) => SrsFile(dir, "ExternalAudio", "DCS-SR-ExternalAudio.exe") != null;
    /// SRS folder for a path that may also point to Client\, Server\ or an SRS exe (users like to enter it that way, also with quotation marks from "Als Pfad kopieren").
    internal static string? SrsRoot(string? p)
    {
        if (string.IsNullOrWhiteSpace(p = p?.Trim().Trim('"'))) return null;
        p = Path.TrimEndingDirectorySeparator(p);
        return new[] { p, Path.GetDirectoryName(p), Path.GetDirectoryName(Path.GetDirectoryName(p)) }.FirstOrDefault(SrsOk);
    }
    /// SRS file in both layouts: from SRS 2.2 in subfolders (Client\SR-ClientRadio.exe, Server\SRS-Server.exe, ExternalAudio\DCS-SR-ExternalAudio.exe), up to 2.1 everything in the SRS folder (server there SR-Server.exe).
    internal static string? SrsFile(string? dir, string sub, params string[] names) =>
        string.IsNullOrWhiteSpace(dir) ? null : names.SelectMany(n => new[] { Path.Combine(dir, sub, n), Path.Combine(dir, n) }).FirstOrDefault(File.Exists);
    /// Number from an SRS .cfg (INI, "KEY=value"), otherwise def.
    internal static int SrsCfgInt(string file, string key, int def)
    {
        try { foreach (var l in File.ReadLines(file)) if (l.Split('=', 2) is [var k, var v] && k.Trim() == key && int.TryParse(v.Trim(), out int n)) return n; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return def;
    }

    static int SrsPort = 5002;   // SERVER_PORT from the server.cfg next to the server exe, otherwise SRS default

    static void StartSrs()
    {
        string? server = SrsFile(Cfg.SrsPath, "Server", "SRS-Server.exe", "SR-Server.exe"), client = SrsFile(Cfg.SrsPath, "Client", "SR-ClientRadio.exe");
        if (server != null) SrsPort = SrsCfgInt(Path.Combine(Path.GetDirectoryName(server)!, "server.cfg"), "SERVER_PORT", 5002);
        foreach (var (name, full) in new[] { ("SRS-Server", server), ("SRS-Client", client) }.Take(Dedicated ? 1 : 2))   // Dedicated server: no SRS client (nobody listens at the PC)
        {
            if (full == null) { Console.WriteLine("      " + L($"{name} nicht gefunden in \"{Cfg.SrsPath}\" – SrsPath in config.jsonc bzw. Einstellungen prüfen.", $"{name} not found in \"{Cfg.SrsPath}\" – check SrsPath in config.jsonc or the settings.")); continue; }
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)).Length > 0 || (full == server && PortOpen(SrsPort))) continue;   // already running (also command-line server)
            Process.Start(new ProcessStartInfo(full) { WorkingDirectory = Path.GetDirectoryName(full)!, UseShellExecute = true });
            Console.WriteLine($"      {Path.GetFileName(full)} " + L("gestartet.", "started."));
            if (full == server)   // wait until the server accepts connections
                for (int i = 0; i < 30 && !PortOpen(SrsPort); i++) Thread.Sleep(500);
            else Thread.Sleep(2000);
        }
        if (Cfg.SrsAutoConnect && !Dedicated && client != null) new Thread(() => SrsAutoConnect(client)) { IsBackground = true }.Start();
    }

    /// Connect client to the local server the way of the SRS DCS hook (Scripts\DCS-SRS\Scripts\DCS-SRSGameGUI.lua, SRS.sendConnect):
    /// UDP "host:port\n" to 127.0.0.1:DCSAutoConnectUDP (global.cfg next to the client exe, default 5069). Not connected: SRS 2.2+ connects without prompt
    /// (up to 2.1 with "Auto Connect Prompt" a yes/no question). Already connected (even to a foreign server): send nothing, otherwise the mismatch prompt would appear.
    static void SrsAutoConnect(string client)
    {
        int udp = SrsCfgInt(Path.Combine(Path.GetDirectoryName(client)!, "global.cfg"), "DCSAutoConnectUDP", 5069);
        int[] Pids() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(client)).Select(p => p.Id).ToArray();
        for (int i = 0; i < 60 && !(PortOpen(SrsPort) && Pids().Length > 0 && System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(e => e.Port == udp)); i++)
            Thread.Sleep(500);   // until server open, client running and listening (30 s at most)
        if (!PortOpen(SrsPort)) return;   // no local server
        for (int n = 0; n < 4; n++)   // the client listens before its window accepts messages: repeat up to three times
        {
            var pids = Pids();
            if (pids.Length == 0) return;
            if (TcpPeers(pids).Any(port => port is not (80 or 443)))   // connected (80/443: update check)
            {
                if (n > 0) Console.WriteLine("      " + L($"SRS-Client mit 127.0.0.1:{SrsPort} verbunden.", $"SRS client connected to 127.0.0.1:{SrsPort}."));
                return;
            }
            SendSrsConnect(udp, SrsPort);
            Thread.Sleep(3000);
        }
        Console.WriteLine("      " + L($"SRS-Client hat sich nicht verbunden – im Client 127.0.0.1:{SrsPort} eintragen und Connect drücken.", $"SRS client did not connect – enter 127.0.0.1:{SrsPort} in the client and press Connect."));
    }

    internal static void SendSrsConnect(int udpPort, int serverPort)
    {
        using var u = new UdpClient();
        var b = System.Text.Encoding.UTF8.GetBytes($"127.0.0.1:{serverPort}\n");
        u.Send(b, b.Length, "127.0.0.1", udpPort);
    }

    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, int reserved);
    /// Remote ports of the existing IPv4 TCP connections of these processes (TCP_TABLE_OWNER_PID_CONNECTIONS; row MIB_TCPROW_OWNER_PID: state, local addr/port, remote addr/port, pid).
    // ponytail: IPv4 only – a client via IPv6 to a foreign server would count as unconnected, SRS then asks (mismatch prompt) instead of switching silently
    internal static List<int> TcpPeers(int[] pids)
    {
        var res = new List<int>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 4, 0);
        size += 4096;   // Table can grow between calls
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 4, 0) != 0) return res;
            for (int i = 0, n = Marshal.ReadInt32(buf); i < n; i++)
            {
                var row = buf + 4 + i * 24;
                if (Marshal.ReadInt32(row) == 5 && pids.Contains(Marshal.ReadInt32(row, 20)))   // 5 = ESTABLISHED
                    res.Add((ushort)IPAddress.NetworkToHostOrder(Marshal.ReadInt16(row, 16)));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return res;
    }

    static bool PortOpen(int port)
    {
        try { using var c = new TcpClient(); return c.ConnectAsync("127.0.0.1", port).Wait(300) && c.Connected; }
        catch { return false; }
    }
}

/// UI language (installer writes lang.txt next to the exe); radio is always English.
static class Ui
{
    public static bool En;
    public static string L(string de, string en) => En ? en : de;
    /// Radio wording variants with the same content; tests always get the first (canonical) one.
    public static string OneOf(params string[] a) => Program.TestRun ? a[0] : a[Random.Shared.Next(a.Length)];
}
