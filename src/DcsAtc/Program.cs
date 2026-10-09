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
    public Dictionary<string, double> Frequencies { get; set; } = new(Tower.Freqs) { ["Range"] = 267.5, ["AWACS"] = 251.5, ["Tanker"] = 255.5, ["Carrier"] = 127.5 };   // AWACS/tanker on .5: not on an airfield frequency (Radio.lua whole MHz, K3)
    public bool AwacsBullseye { get; set; } = true;    // LK4: picture and situation updates to all as bullseye; false = everything BRAA from the player (fallback)
    public double AwacsNewGroupNm { get; set; } = 200;  // AWACS reports new groups (and picture clean) within this radius, 40-200 NM
    public Dictionary<string, double> OnSpeedAoa { get; set; } = new() { ["FA-18"] = 8.1 };   // LSO: on-speed AoA in degrees per type prefix (calibrate with the log "[LSO] AoA im Mittel"), unknown type = no speed calls
    public string Modulation { get; set; } = "AM";
    public int Coalition { get; set; } = 2;   // SRS coalition for stations without their own (A133: airfields/AWACS/AI transmit with their side)
    /// Voice per controller: Piper model, optional "@pitch" (0.9 = lower)
    public Dictionary<string, string> Voices { get; set; } = new()
    {
        ["Tower"] = "en_US-ryan-medium", ["Ground"] = "en_US-bryce-medium",
        ["Approach"] = "en_US-ryan-medium@1.05", ["ATIS"] = "en_US-ryan-medium@0.94",
        ["Range"] = "en_US-ryan-medium@1.06", ["AWACS"] = "en_GB-alan-medium", ["Tanker"] = "en_US-bryce-medium", ["Crew"] = "en_US-ryan-medium",
        ["Carrier"] = "en_US-ryan-medium@0.97|en_GB-alan-medium@1.02",   // one per station (Marshal, Tower, Paddles); AWACS/carrier without the pilot model (joe)
    };
    public string PilotVoice { get; set; } = "en_US-joe-medium";   // speaks your menu/wheel requests, "" = off
    public bool RadioFx { get; set; } = true;          // Radio sound: bandpass, noise, squelch
    public double Volume { get; set; } = 0.6;          // controller volume in SRS (0–1)
    public string RunwayLights { get; set; } = "silent";   // runway lighting via the DCS-ATC menu: silent (airfield stays mute), dcs (DCS controller audible 15 s), off
    public string AltimeterUnit { get; set; } = "auto";   // altimeter setting on the radio: auto (by own type: western inHg/altimeter, otherwise hPa/QNH; ATIS both), hpa, inhg
    public bool Atis { get; set; } = true;             // continuous ATIS broadcast for airfields with players nearby
    public bool AirspaceWatch { get; set; } = true;    // monitor control zones (N2) and announce violations ("possible pilot deviation") and into the debrief (N4); off: safety warnings only
    public bool DebugLog { get; set; } = true;         // atc-debug.txt for bug reports (speech recognition, airfield selection, phases, telemetry every 10 s), at most 2 × 20 MB
    public Dictionary<string, double> AtisFreqs { get; set; } = new();   // A35: own ATIS frequency per airfield (airfield name -> MHz), if the airfield is missing: Frequencies["ATIS"]; only after restart
    public int WheelKey { get; set; } = 0xDE;          // selection wheel: key (0xDE = Ä), 0 = off
    public int WheelJoystick { get; set; } = -1;       // selection wheel on the HOTAS: button opens, coolie hat selects
    public int WheelButton { get; set; } = 1;
    public string WheelInGame { get; set; } = "auto";   // radio wheel additionally as in-game text: auto (when DCS is in VR mode), on, off
    public double WheelSize { get; set; } = 0.45;       // radio wheel: fraction of the DCS window height, adjustable in the wheel with + / -
    public int CommsKey { get; set; } = 0x2B;          // DCS radio menu key as scancode (0x2B = backslash US, # DE). With it the app presses the tanker menu (basket/boom) and, for lighting at dusk/night/poor visibility, "Inbound" or on the ground "Request startup", 0 = off
    public bool SrsListen { get; set; } = true;        // listen to all players' radio calls via SRS (instead of own PTT key)
    public double SpeechRate { get; set; } = 0.85;     // controller speaking rate: 1 = Piper default (too slow for radio), larger = slower
    public double ReplyPause { get; set; } = 1.5;      // seconds of pause between two radio calls on a frequency
    public bool AiChatter { get; set; } = true;        // AI flights also transmit audibly (request, reply, readback)
    public string AiFlightComms { get; set; } = "voll"; // AI flights in combat: full (Fox, Splash, Tally …), tactical (only Fox/Splash/Bingo/Defending/Guard), off
    public double AiFlightRadiusNm { get; set; } = 60; // AI radio only from flights this close to a player of the same side
    public bool TankerVectors { get; set; } = true;    // tanker guides the receiver with join vectors until "visual" (N38)
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
    public bool Heli;                                 // Helicopter (mission: P line, N50)
    public int Gid;                                   // DCS group ID for in-game text, 0 = to all
    public Telemetry? Tel;
    public DateTime Seen;
    public readonly Dictionary<string, Tower> Towers = new();   // one separate sequence per airfield
    public Tower? Active;
    public Pilot? Lead;                               // in formation with the group lead: its sequence applies
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
          int Prio = 1, Tx? Standby = null, double MaxAge = 0, bool Stress = false, int Side = 0, Func<string?>? Live = null);   // MaxAge: afterwards text only (0 = per prio); Stress: combat voice; Side: SRS coalition of the sender (A133), 0 = Cfg.Coalition

class AtisInfo { public char Letter = 'A'; public string Key = "", Text = "", Mp3 = "", Call = ""; public double Sec; }   // Call: announcement on new identifier

static partial class Program
{
    const double NM = 1852;
    static readonly string Root = FindRoot();
    static Config Cfg = new();
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
        var lang = Path.Combine(AppContext.BaseDirectory, "lang.txt");   // Installer: UI language
        Ui.En = File.Exists(lang) && File.ReadAllText(lang).Trim() == "en";
        Tower.FreeLevel = FreeLevel;
        Ops.Callsigns = AiCallsign;
        TestRun = args.Any(a => a is "--selftest" or "--anflugtest" or "--mptest" or "--asr-test" or "--srs-test");
        if (args.Contains("--selftest")) { RadioTest(); QueueTest(); SideTest(); FreqTest(); CaptureTest(); ControlAiTest(); FunkTest(); ConfigTest(); AtisTest(); CrewTest(); ExpectTest(); return Tower.SelfTest(); }
        LoadConfig();
        if (args.Contains("--settings")) { Dedicated = DedicatedNow(); Settings.Show(Cfg); return 0; }   // language then also in the server's Saved Games
        if (args.Contains("--buttons")) { ShowButtons(); return 0; }
        if (args.Contains("--keys")) { SetupKeys(); return 0; }
        if (args.Contains("--frequencies")) { LoadFields(); Console.WriteLine(FreqPath); return 0; }   // create/extend frequencies.jsonc without DCS (e.g. server admin)
        if (args.Length >= 1 && args[0] == "--asr-test") return AsrTest(args.Length > 1 ? args[1] : Path.Combine(Root, "debrief", "asr"));
        if (args.Length >= 2 && args[0] == "--wheel-png") { Wheel.Snapshot(Cfg, args[1], args.ElementAtOrDefault(2)); return 0; }   // Preview: --wheel-png file.png ["role: suggestion"]
        if (args.Length >= 2 && args[0] == "--settings-png") { if (args.Length > 2) Ui.En = args[2] == "en"; Settings.Snapshot(Cfg, args[1]); return 0; }   // Preview: --settings-png file.png [de|en]
        if (args.Length == 2 && args[0] == "--wheel-live") { Wheel.LiveShot(Cfg, args[1]); return 0; }
        if (args.Length >= 1 && args[0] == "--kneeboard")   // DcsAtc --kneeboard <folder>
        {
            LoadFields();
            Kneeboard.Write(args.Length > 1 ? args[1] : Path.Combine(Root, "kneeboard"), Fields, Cfg);
            return 0;
        }
        if (args.Length >= 1 && args[0] == "--anflugtest") { LoadFields(); LoadTerrain(); return Tower.AnflugTest(Fields, args[1..]); }   // airfields from the repo airfields.txt, terrain of the last mission (%TEMP%)
        if (args.Length >= 1 && args[0] == "--mptest") { LoadFields(); LoadTerrain(); return MpTest(args[1..]); }   // multiple players + AI via the app logic

        TraceOn = Cfg.DebugLog;   // live from here: debug log (tests above without file)
        Dedicated = DedicatedNow();
        Trace("START", $"DCS-ATC {typeof(Program).Assembly.GetName().Version} {string.Join(" ", args)} | {JsonSerializer.Serialize(Cfg)}");
        bool auto = args.Contains("--auto");   // started by the DCS hook at mission start
        using var single = new Mutex(true, "DcsAtc", out bool first);
        if (!first && !args.Contains("--say") && !args.Contains("--srs-test"))
        {
            if (auto) return 0;   // already running
            Console.WriteLine(L("DCS ATC läuft bereits in einem anderen Fenster. Taste drücken zum Schließen.", "DCS ATC is already running in another window. Press a key to close."));
            Console.ReadKey(true);
            return 1;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);   // radio wheel error: no WinForms dialog, but crash -> restart
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception, args);
        if (auto)
        {
            if (Session()[0] == "client") Cfg.Mode = "client";
            new Thread(() =>   // left mission or DCS closed: app too
            {
                SetThreadExecutionState(0x80000000 | 0x1 | 0x2);   // ES_CONTINUOUS | SYSTEM | DISPLAY: no standby while DCS runs (HOTAS input does not count as activity for Windows); ends with the thread
                while (Session()[0] != "stop" && RunningDcs().Length > 0) Thread.Sleep(2000);
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
        StartAtis();
        if (Cfg.SrsListen)
        {
            Srs = new SrsListener(SrsPort, Cfg.Frequencies.Where(f => f.Key != "ATIS").Select(f => f.Value), IsOwnStation, OnSrs);
            Srs.Start();
        }
        if (!Dedicated) Wheel.Start(Cfg, text => { lock (TowerLock) Request(Host(), text, "RAD", true); }, () =>   // dedicated server: no radio wheel, no key hook
        {
            lock (TowerLock) { var h = Host(); var q = h?.Lead ?? h; Wheel.Field = q?.OnBoat ?? q?.Active?.F.Name ?? "ATC"; return Expected(h, q != null && (q.OnBoat != null || h!.Boat.Stage > 0 || h.Boat.Launching) ? h!.Boat.Suggest(q.Tel) : q?.Active?.Suggest(q.Tel)); }   // A76: at the carrier the suggestion of the carrier stage, not from the land tower; R24: on the deck nothing, on departure Departure; open question first (Expect.cs)
        });
        Wheel.Fits = () => { lock (TowerLock) return WheelFits(Host()); };   // R307: only matching entries
        Wheel.FreqOf = role =>
        {
            lock (TowerLock)
            {
                var h = Host(); var f = (h?.Lead ?? h)?.Active?.F;
                if (role == "AWACS" && h != null && Ops.AwacsFreqFor(TrafficFor(h), MeOf(h)) is > 0 and var af) return af;   // mission AWACS
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
        Wheel.Log = Log;   // screenshot (press with wheel open) into atc-log
        Wheel.InGame = text =>   // VR: headset shows only DCS, wheel additionally as in-game text (mission reads .cmd every 0.25 s)
        {
            if (Cfg.WheelInGame == "off" || Cfg.WheelInGame != "on" && !VrOn()) return;
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
                if (Clock >= nextScriptCheck) { nextScriptCheck = Clock.AddSeconds(15); CheckMissionScripting(); }
                if (File.GetLastWriteTimeUtc(ConfigPath) != cfgAt) ReloadLive();
                if (File.GetLastWriteTimeUtc(FreqPath) != freqAt) lock (TowerLock) { ApplyOwnFreqs(); Log("[ATC] frequencies.jsonc übernommen"); }   // own airfield frequencies immediately (airfield selection, monitoring, handovers, ATIS)
                TickAll();
            }
            Thread.Sleep(20);
        }
    }

    // follow along: console + atc-log.txt in the project folder
    static void Log(string s) { if (SimLog != null) { SimLog.Add(s); return; } Console.WriteLine(s); Trace("LOG", s); try { File.AppendAllText(Path.Combine(Root, "atc-log.txt"), $"{Clock:HH:mm:ss} {s}\r\n"); } catch (IOException) { } }

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
                + FormattableString.Invariant($"| nächster {nf?.Name} {(nf == null ? 0 : Dist(t.X, t.Z, nf.X, nf.Z) / NM):0.0} NM | aktiv {(p.Lead ?? p).Active?.F.Name ?? "-"} {(p.Lead ?? p).Active?.Phase} | SRS {Fq(Srs?.TunedFreqs(p.Id))} gerufen {p.CalledOn:0.0##} | Sprit {p.Fuel:0.00}"));
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

    static double Now() => SimSec >= 0 ? SimSec : Environment.TickCount64 / 1000.0;
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

    // ------------------------------------------------------------ Speaking via SRS (all players)
    static SrsListener? Srs;
    static readonly HashSet<string> OwnStations = new();   // SRS names of our own senders (ExternalAudio)
    static bool IsOwnStation(string name) { lock (OwnStations) return OwnStations.Contains(name); }

    /// Radio call via SRS: sender via DCS unit ID (otherwise player name), called controller via the frequency.
    static void OnSrs(SrsListener.Transmission tx)
    {
        var role = Cfg.Frequencies.FirstOrDefault(f => f.Key != "ATIS" && Math.Abs(f.Value - tx.FreqMHz) < 0.01).Key
                   ?? (Ops.AwacsFreq.Values.Any(f => Math.Abs(f - tx.FreqMHz) < 0.01) ? "AWACS" : null)   // mission AWACS
                   ?? (Carrier.Boats.Any(b => b.Freq > 0 && Math.Abs(b.Freq - tx.FreqMHz) < 0.01) ? "Carrier" : null);   // mission carrier
        Airfield? via = null;   // airfield frequency: this airfield, controller by content (Ground/Tower/Approach) or the one with its own frequency
        if (role is not ("AWACS" or "Tanker" or "Range" or "Carrier")) lock (TowerLock) (via, role) = FreqRoute(tx.FreqMHz, role);
        role ??= via == null ? "Tower" : null;
        Pilot? p;   // A141: sender first, so the Whisper prompt uses its call sign and airfield
        lock (TowerLock)
            p = !MissionData ? Host()
              : Pilots.Values.FirstOrDefault(x => tx.UnitId != 0 && x.Id == tx.UnitId)
                ?? Pilots.Values.FirstOrDefault(x => x.Name == tx.Name)
                ?? (Pilots.Count == 1 ? Pilots.Values.First() : null);
        var (text, conf) = Transcribe(SaveWav(tx.Pcm), p);
        var who = p == null ? "Rufzeichen im Spruch" : !MissionData ? "Host" : tx.UnitId != 0 && p.Id == tx.UnitId ? "Unit-ID" : p.Name == tx.Name ? "SRS-Name" : "einziger Spieler";   // Debug log
        if (Environment.GetEnvironmentVariable("DCSATC_SRSDEBUG") == "1") Console.WriteLine($"OnSrs {role} unit {tx.UnitId} '{tx.Name}': {text} ({conf:0.00})");
        if (Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)", "").Trim().Length < 2) { Trace("SRS", $"{tx.FreqMHz:0.000} MHz unit {tx.UnitId} '{tx.Name}' {tx.Pcm.Length / 16000.0:0.0} s: leer/Rauschen verworfen ({text})"); return; }   // [BLANK_AUDIO], noise
        lock (TowerLock)
        {
            // A26: sender unknown -> match the spoken call sign to a player (otherwise Request(null): "say again your callsign")
            p ??= ByCallsign(text);
            if (p != null) p.CalledOn = tx.FreqMHz;   // K2: reply names this frequency
        }
        Trace("SRS", $"{tx.FreqMHz:0.000} MHz unit {tx.UnitId} '{tx.Name}' {tx.Pcm.Length / 16000.0:0.0} s -> {p?.Callsign ?? "unbekannt"} ({(p == null ? "-" : who)}), Lotse {role ?? "nach Inhalt"}, Platzfrequenz {via?.Name ?? "-"}, conf {conf:0.00}: {text}");
        Request(p, role == null ? text : $"{role}: {text}", "SRS", false, via, conf);
    }

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
            && (StationWords.Contains(m.Groups[1].Value) || Fields.Any(f => $"{f.Name}-{f.Callsign}".ToLowerInvariant().Split('-', ' ').Contains(m.Groups[1].Value)))) return null;
        return Pilots.Values.FirstOrDefault(x => Tower.SameCallsign(cs, x.Callsign) || x.SlotCallsign != "" && Tower.SameCallsign(cs, x.SlotCallsign));
    }
    static readonly HashSet<string> StationWords = new() { "tower", "ground", "approach", "departure", "radar", "control", "center", "centre", "marshal", "paddles", "mother", "overlord", "awacs", "magic", "moscow", "darkstar", "wizard", "focus", "texaco", "shell", "arco", "tanker", "range" };

    static Ops.Me MeOf(Pilot p) => new(p.Callsign, p.Tel, p.Type, p.Coalition, p.FlightSize + (p.Gid == 0 ? 0 : Ai.Count(a => AiGid.GetValueOrDefault(a.Group) == p.Gid)), p.Fuel);
    static Tx OpsTx(Pilot p, Ops.Call c)
    {
        if (c.To != null) lock (TowerLock) p = Pilots.Values.FirstOrDefault(x => x.Ops == c.To) ?? p;   // call to another player (tanker: the next one) goes to that player's group
        return new(c.Text, c.Role, c.Station, c.All ? 0 : p.Gid, Freqs: c.Freq > 0 ? new[] { c.Freq }
                   : c.Role == "Tanker" && p.TankFq > 0 ? new[] { Cfg.Frequencies.GetValueOrDefault("Tanker", 255.5), p.TankFq } : null,   // R304: also on the tanker frequency tuned by DCS
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
        var v = voices.Split('|').Where(x => File.Exists(Path.Combine(Root, "models", x.Split('@')[0] + ".onnx"))).DefaultIfEmpty("en_US-ryan-medium").ToArray();
        return v[station.Sum(ch => ch) % v.Length];
    }

    /// Debrief: landings, traffic patterns, range, tanker, rejected radio calls. final -> file in debrief\ and reset.
    static Tx DebriefTx(Pilot p, bool final)
    {
        var lines = new List<string> { $"DEBRIEFING {p.Callsign} ({p.Type})" + (p.Airborne is { } a ? L(" – Flugzeit ", " – flight time ") + $"{Clock - a:h\\:mm}" : "") };
        lines.AddRange(p.Ops.Debrief.Count > 0 ? p.Ops.Debrief : new List<string> { L("Noch keine Wertungen (Landung, Range, Tanker).", "No grades yet (landing, range, tanker).") });
        lines.AddRange(p.Ops.DebriefSummary());
        int rej = p.Towers.Values.Sum(t => t.Rejected) + (p.Lead?.Towers.Values.Sum(t => t.Rejected) ?? 0);
        if (rej > 0) lines.Add(L("Vom Lotsen abgelehnte Funksprüche: ", "Calls rejected by the controller: ") + rej);
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

    /// Range events of the mission: R;unit;waffe;abstand;uhr;ziel;flugzeit  H;unit;ziel (gun hit)  K;unit;ziel (destroyed)
    static void OnEvent(string line)
    {
        var e = line.Split(';');
        if (e.Length < 2) return;
        if (e[0] == "X") { lock (TowerLock) try { Ops.OnEvent(e, Now()); Flights.OnEvent(e, Now(), FlightWorld()); } catch (FormatException x) { Trace("FEHLER", $"Ereignis {line}: {x.Message}"); } return; }   // AI radio
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
            else if (e[0] == "K") calls = p.Ops.OnKill(MeOf(p), Now());
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
                else Log($"[DCS] Funkmenü {e[2]} nicht gedrückt: {(mi == 0 ? e[4] : mi > 10 ? $"Nr. {mi}, DCS listet nur 10" : "keine Funkmenü-Taste (CommsKey 0)")}");
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
            if (Regex.IsMatch(spoken, @"^\s*debrief", RegexOptions.IgnoreCase)) { SayQueue.Add(DebriefTx(p, false)); return; }
            if (Regex.IsMatch(spoken, @"\b(?:fox (?:one|two|three)|defensive|defending)\b", RegexOptions.IgnoreCase)) HotUntil = Clock.AddSeconds(5);   // LD3: radio pause for picture/replies
            if (!fromMenu && MissionData && Flights.Ask(Tower.Normalize(spoken), p.Coalition, Now(), FlightWorld())) return;   // LK15: "Ford one, say status" to an AI flight (reply comes via FlightComms)
            p.AtcAt = Now();   // N2: call to a controller (airfield, carrier, AWACS, tanker, range)
            p.Expect = (p.Lead ?? p).Expect = null;   // answered (radio wheel Enter)
            bool distress = Regex.IsMatch(spoken, @"\b(?:mayday|pan pan|declaring emergency|emergency fuel)\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(spoken, @"\bcancel\b", RegexOptions.IgnoreCase);
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
            var sn = Tower.Normalize(spoken);
            if (!fromMenu && to is "Tanker" or "AWACS" or "Carrier" or "Range" && Fields.Count > 0 && FieldRole(sn) is { } fr && !(to == "Carrier" && fr == "Approach") && !p.Boat.OnDeck   // R24: no airfield on the deck, the carrier gives the deck hint
                && Ops.RoleOf(sn) == null && !p.Boat.Wants(sn) && !Tower.Has(sn, "vector", "home plate", "bingo", "nearest airfield", "recovery", "divert"))   // AWACS: "bingo, vectors … full stop" stays with the AWACS
            {
                var wf = (p.Lead ?? p).Active?.F ?? Pick(p.Lead ?? p, spoken).F;
                string Mhz(double x) => x.ToString("0.0##", CultureInfo.InvariantCulture);
                var on = Mhz(p.CalledOn > 0 ? p.CalledOn : Cfg.Frequencies.GetValueOrDefault(to));
                var ok = $"{wf.StationOf(fr)}: {string.Join(" / ", (FieldFreqs(wf, fr) ?? new[] { Cfg.Frequencies.GetValueOrDefault(fr) }).Select(Mhz))}";
                SayQueue.Add(new Tx(L($"Du funkst auf {to} {on}. {ok}", $"You are transmitting on {to} {on}. {ok}"), "Info", "", p.Gid));
                return;
            }
            if (to == "Carrier" || p.Boat.OnDeck && via == null && to is null or "Ground" or "Tower" or "Approach" && Ops.RoleOf(Tower.Normalize(text)) == null
                || p.Boat.Launching && via == null && to == "Approach" && p.Boat.Wants(Tower.Normalize(text))   // R24: radio wheel "Approach: airborne" after the catapult -> carrier Departure
                || to == null && via == null && (p.Boat.Wants(Tower.Normalize(text)) || (p.Lead ?? p).OnBoat != null && Ops.RoleOf(Tower.Normalize(text)) == null))
            {
                if (fromMenu && Regex.IsMatch(spoken, @"^marshal, checking in$", RegexOptions.IgnoreCase) && CheckIn(p) is { } ci) (spoken, text) = (ci, "Carrier: Marshal, " + ci);
                if (fromMenu && Regex.Match(spoken, @"^(ball|clara)$", RegexOptions.IgnoreCase) is { Success: true } bm) (spoken, text) = (BallCall(p, bm.Value), "Carrier: " + BallCall(p, bm.Value));   // R28
                var bc = p.Boat.OnTranscript(text, MeOf(p), Now(), conf < MinConf);   // uncertain recognition: ask back (R16)
                if (fromMenu && Cfg.PilotVoice != "" && bc.Count > 0 && bc[0].Role != "Info")   // R134: hint without station (zip lip) is not a radio call
                    outq.Add(new Tx(CarrierCall(bc[0].Station, p.Callsign, spoken), "Carrier", p.Callsign, p.Gid, true, PilotVoiceFor(p.Unit), Freqs: OpsTx(p, bc[0]).Freqs, Prio: 0, Side: p.Coalition));
                outq.AddRange(bc.Select(c => OpsTx(p, c)));
                Send(p, outq);
                return;
            }
            // Range / AWACS / tanker: frequency or menu ("Range: ...") or name in the radio call
            var opsRole = to is "Range" or "AWACS" or "Tanker" ? to : to == null ? Ops.RoleOf(Tower.Normalize(text)) : null;
            if (opsRole != null)
            {
                var calls = p.Ops.OnTranscript(opsRole, text, MeOf(p), TrafficFor(p), Fields, Now(), conf < MinConf, Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5), FieldWxOf);
                if (p.Ops.Menu is { } mn && Cfg.CommsKey != 0 && MissionData && p == Host())   // basket/boom only via the DCS menu: "Intent to refuel" (mission looks it up in the list) or "Ready pre-contact"
                {
                    if (mn.Step == "precontact") { Wheel.PressDcs((ushort)Cfg.CommsKey, 0x3B); p.TankMenuAt = Now(); }
                    else AiCmd($"MENU;{p.Unit};tanker;{mn.Group};s");   // R304: s = DCS tanker stays mute, our controller speaks (without s: audible 15 s like the airfields)
                }
                p.Ops.Menu = null;
                if (fromMenu && Cfg.PilotVoice != "" && calls.Count > 0)
                    outq.Add(new Tx($"{calls[0].Station}, {p.Callsign.Replace('-', ' ')}, {spoken}.", opsRole, p.Callsign, p.Gid, true, PilotVoiceFor(p.Unit), Freqs: OpsTx(p, calls[0]).Freqs, Prio: 0, Side: p.Coalition));
                foreach (var c in calls) AwacsNote(p, c);
                outq.AddRange(calls.Select(c => OpsTx(p, c)));
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
            var intent = tw.IntentOf(text, q.Tel);   // Debug log
            if (Regex.IsMatch(text, @"^\s*atis\s*$", RegexOptions.IgnoreCase))
            {
                if (MissionData) UpdateAtis(tw.F);   // R120: without fresh mission data no weather from the previous mission
                msgs = new() { Atis.TryGetValue(tw.F.Name, out var a) && a.Text != "" ? new Msg("ATIS", a.Text)
                                                                                     : new Msg("Info", L($"Kein ATIS für {tw.F.Name} (keine Wetterdaten aus der Mission).", $"No ATIS for {tw.F.Name} (no weather data from the mission).")) };
                fromMenu = false;
            }
            else msgs = conf < MinConf ? tw.SayAgain(text) : other != null ? tw.OtherField(text, other) : tw.OnTranscript(text, q.Tel, TrafficFor(q), Now(), fromMenu);   // uncertain recognition: ask back instead of acting
            TraceState($"Spruch an {tw.F.Name}, Intent {intent}" + (conf < MinConf ? ", unsicher -> say again" : other != null ? $", anderer Platz {other.Name}" : ""));
            if (fromMenu && Cfg.PilotVoice != "")
            {
                var role = to ?? msgs.FirstOrDefault(m => m.Role != "Info")?.Role ?? "Tower";
                if (role == "Approach" && (msgs.Any(m => m.Role == "Departure") || tw.Dep(role) == "Departure")) role = "Departure";   // radio wheel "Approach: …" after handover on departure
                if (!Cfg.Frequencies.ContainsKey(role) && role != "Departure") role = "Tower";   // Departure: own frequency (AirfieldFrequencies)
                outq.Add(new Tx(PilotCall(tw.F, role, p, spoken), role, p.Callsign, p.Gid, true, PilotVoiceFor(p.Unit), Freqs: FieldFreqs(tw.F, role), Prio: 0, Side: p.Coalition));
            }
            outq.AddRange(msgs.Select(m => ToTx(q, m)));
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
    internal static string PilotCall(Airfield f, string role, Pilot p, string spoken)
    {
        var (station, cs) = (f.StationOf(role), p.Callsign.Replace('-', ' '));
        if (p.Tel is { } tl && spoken is "airborne, climbing" or "inbound for landing")   // R275: first call with altitude (FAA JO 7110.65 5-2-17, AIM 4-1-13): "passing 1800" or "30 miles north, 11000 feet"; Tower.Intent understands both as before
        {
            var ft = Math.Round(tl.AltMsl / 0.3048 / 100) * 100;
            spoken = spoken == "airborne, climbing" ? $"airborne, passing {ft:0} feet, climbing"
                : $"{Tower.Miles(Dist(tl.X, tl.Z, f.X, f.Z))} miles {Tower.Dir8(Tower.Bearing(f.X, f.Z, tl.X, tl.Z))}, {ft:0} feet, inbound for landing";
        }
        if (Regex.Match(spoken, @"^(mayday mayday mayday(?: fuel)?|pan pan, pan pan, pan pan), (.+?)(, request \w+ landing)?$", RegexOptions.IgnoreCase) is not { Success: true } m) return $"{station}, {cs}, {spoken}.";
        if (p.Tel is { } gt && Tower.OnGround(gt)) return $"{m.Groups[1].Value.ToUpperInvariant()}, {station}, {cs}, {m.Groups[2].Value}, shutting down.";   // R300: on the ground without landing, position, altitude
        var pos = "";
        if (p.Tel is { } t)
        {
            int h = (int)Math.Round(((t.Hdg * 180 / Math.PI - f.MagVar) % 360 + 360) % 360) % 360;
            pos = FormattableString.Invariant($", {Tower.Miles(Dist(t.X, t.Z, f.X, f.Z))} miles {Tower.Dir8(Tower.Bearing(f.X, f.Z, t.X, t.Z))} of {f.Name.Replace('-', ' ')}, {Math.Round(t.AltMsl / 0.3048 / 100) * 100:0} feet, heading {(h == 0 ? 360 : h):000}")
                + ", " + FuelSouls(p);
        }
        return $"{m.Groups[1].Value.ToUpperInvariant()}, {station}, {cs}, {(p.Type != "" ? Ops.TypeSay(p.Type) + ", " : "")}{m.Groups[2].Value}{pos}{m.Groups[3].Value}.";
    }
    /// R244/R300: fuel in minutes and souls on board ("25 minutes fuel, one soul on board"): in the emergency call and in reply to "say souls on board and fuel remaining".
    internal static string FuelSouls(Pilot p)
    {
        int souls = Regex.IsMatch(p.Type, @"^(Mi-8|CH-47)") ? 3 : Regex.IsMatch(p.Type, @"^(F-14|F-15E|F-4E|AH-64|Mi-24|L-39|C-101|Tornado|Su-24|UH-1H|OH-58)") ? 2 : 1;
        int min = Math.Max(5, (int)Math.Round(p.Fuel * 90 / 5) * 5);   // ponytail: roughly 90 min at full tank for all types, per-type consumption if it becomes noticeable
        return $"{min} minutes fuel, {new[] { "one soul", "two souls", "three souls" }[souls - 1]} on board";
    }

    /// Whisper word confidence, below it "say again" (measurement: clear calls ≥ 0.82, gibberish/German/backwards ≤ 0.55)
    const double MinConf = 0.65;

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
        var roles = Cfg.Frequencies.Where(f => f.Key is "AWACS" or "Tanker" || (f.Key == "Range" && Ops.Range != null)
                                               || (f.Key == "Carrier" && Carrier.Boats.Count > 0)
                                               || (!known && f.Key is "Ground" or "Tower" or "Approach")).Select(f => f.Value)
                        .Concat(Carrier.Boats.Where(b => b.Freq > 0).Select(b => b.Freq));
        // A22: order active airfields of all players, then taxiing, then neighboring airfields (SrsListener takes at most Max, duplicates count once)
        static IEnumerable<double> Fq(IEnumerable<Airfield> fs) => fs.SelectMany(f => f.FreqsOf("*") ?? Array.Empty<double>());   // UHF, VHF or own per controller
        var act = pilots.Where(p => p.Active != null && Served(p.Coalition, p.Active.F)).Select(p => p.Active!.F);   // N1: own and neutral airfields only
        var near = pilots.Where(p => p.Tel != null).SelectMany(p => Fields.Where(f => Served(p.Coalition, f)).OrderBy(f => Dist(p.Tel!.X, p.Tel.Z, f.X, f.Z)).Take(2));
        return Fq(act).Concat(roles).Concat(Ops.AwacsFreq.Values).Concat(Fq(near));
    }

    static Tx ToTx(Pilot p, Msg m) => new(m.Text, m.Role, p.Active!.F.StationOf(m.Role), p.Gid, Freqs: FieldFreqs(p.Active.F, m.Role), Side: SideOf(p.Active.F, p));

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
        if (p.FlightSize > 1) tw.SetCallsign(p.Callsign);
        tw.CallsignFixed = MissionData || Cfg.MyCallsign != "";   // call sign from the slot or the settings: not from numbers in the radio call
        tw.AtisLetter = Atis.TryGetValue(tw.F.Name, out var a) && a.Text != "" ? a.Letter.ToString() : "";
        if (tw.Phase is Phase.Departing or Phase.Away) tw.AwacsContact = Ops.AwacsContact(TrafficFor(p), MeOf(p), Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5));   // sign-off after departure
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
            if (MissionFields && File.GetLastWriteTime(AirfieldFile) != airfieldStamp) LoadFields();
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
                var msgs = tw.Tick(p.Tel, TrafficFor(p), Now());
                if (tw.HoldInfo is { } hi) Holds["p:" + p.Unit] = hi; else Holds.Remove("p:" + p.Unit);   // current for the AI squadron
                if (MissionData && p == host && Cfg.RunwayLights != "off" && p.Tel is { } lt && LightKind(tw.Phase, lt, tw.F) is { } lk && Now() - p.LitAt.GetValueOrDefault((tw.F.Id, lk), -1e9) > 900)
                {
                    p.LitAt[(tw.F.Id, lk)] = Now();   // lighting: only after DCS-ATC contact. Mission checks sun/weather (w) and open DCS dialog, switches the airfield on audibly for a short time
                    bool untuned = Srs?.TunedFreqs(p.Id) is { } fq && new[] { tw.F.Uhf, tw.F.Vhf }.Where(x => x > 0).ToArray() is { Length: > 0 } fs && !fs.Any(x => fq.Any(m => Math.Abs(m - x) < 0.005));   // u: counts only without Easy Communication; DCS knows only the map frequency, not AirfieldFrequencies
                    if (Cfg.CommsKey == 0) Log($"[DCS] Befeuerung {tw.F.Name}: keine Funkmenü-Taste (CommsKey 0)");
                    else AiCmd($"MENU;{p.Unit};{lk};{tw.F.Id};{(Tower.Ifr ? "w" : "")}{(untuned ? "u" : "")}{(Cfg.RunwayLights == "dcs" ? "" : "s")}");
                }
                foreach (var m in msgs.Where(m => m.Role == "Info" && (m.Text.StartsWith(L("Landebewertung", "Landing grade")) || m.Text.StartsWith(L("Platzrunde", "Pattern")) || m.Text.StartsWith(L("Anflug ohne", "Approach without")) || m.Text.StartsWith(L("Verstoß:", "Deviation:")))))   // R11, N4
                    foreach (var o in ActivePilots().Where(o => o == p || o.Lead == p)) o.Ops.Debrief.Add(m.Text.Split(L(". Beenden", ". To end"))[0]);
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
            Tower.Ifr = MissionData && (Sky.VisM < 5000 || (Sky.Clouds && Sky.BaseM < 600));
            foreach (var p in ActivePilots())   // range, AWACS, tanker; debrief after shutdown
            {
                var oc = p.Ops.Tick(MeOf(p), TrafficFor(p), Now(), awacs: p.Lead == null, lead: p.Lead?.Ops).ToList();
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
                var bt = p.Boat.Tick(MeOf(p), Now());
                if (p.Boat.Gone) { p.Boat.Gone = false; (p.Lead ?? p).OnBoat = null; }   // A21: out of the recovery (divert, landed): radio without target goes to the tower again
                if (p.Boat.LatLog is { } ll) { Log(ll); p.Boat.LatLog = null; }   // R4: calibrate SternLat
                foreach (var c in bt.Where(c => c.Role == "Info" && (c.Text.StartsWith("LSO") || c.Text.StartsWith(L("Verstoß:", "Deviation:")))))   // #17: violation at the carrier
                {
                    p.Ops.Debrief.Add(L("Träger ", "Carrier ") + c.Text);
                    if (c.Text.StartsWith("LSO") && p.Boat.AoaLog(p.Callsign) is { } al) Log(al);   // calibrate (Export may deliver a different AoA than the HUD)
                }
                outq.AddRange(bt.Select(c => OpsTx(p, c)));
                outq.AddRange(BoatWatch(p));
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
            if (MissionData) { SteerBoats(); outq.AddRange(CarrierChat()); }
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
        if (!Cfg.AiChatter || Carrier.Boats.Count == 0) return Array.Empty<Tx>();
        var calls = Carrier.AiCalls(b => Pilots.Values.Any(p => p.Boat.BoatGroup == b.Group && p.Tel != null && p.Boat.InGroove(p.Tel)));
        return calls.Where(c => Pilots.Values.Any(p => p.Tel != null && Dist(p.Tel.X, p.Tel.Z, c.B.X, c.B.Z) < 50 * NM))   // like the airfield radio: players ≤ 50 NM
                    .SelectMany(c => new[] { c.Pilot, c.Lso }.Where(s => s != "").Select(s => new Tx(s, "Carrier", s == c.Lso ? "Paddles" : c.Cs != "" ? c.Cs : c.Group, 0, s != c.Lso,
                                                                                                    s == c.Lso ? null : PilotVoiceFor(c.Group), Freqs: c.B.Freq > 0 ? new[] { c.B.Freq } : null, Prio: 2, Side: c.B.Coalition)));
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
    static string DcsDir() => DcsDirs().Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.TrimEndingDirectorySeparator(d!.Trim()))
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
        if (!Dedicated && Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\DCS-ATC", "SavedGames", null) is string chosen && Directory.Exists(chosen)) return chosen;
        var sg = ShellFolder(Microsoft.Win32.Registry.GetValue(sf + "User Shell Folders", id, null) as string)
              ?? ShellFolder(Microsoft.Win32.Registry.GetValue(sf + "Shell Folders", id, null) as string)
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        if (Dedicated)
        {
            string? hook = null;
            try { hook = File.ReadAllText(Path.Combine(Path.GetTempPath(), "DcsAtc-WriteDir.txt")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return ServerSavedGames(sg, hook, Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\DCS-ATC", "ServerSavedGames", null) as string);
        }
        return Path.Combine(sg, !Directory.Exists(Path.Combine(sg, "DCS")) && Directory.Exists(Path.Combine(sg, "DCS.openbeta")) ? "DCS.openbeta" : "DCS");
    }

    /// Saved Games of the dedicated server: reported by the hook (lfs.writedir(), also valid with start parameter -w <name>), otherwise chosen in the installer,
    /// otherwise the first existing default folder (DCS.server; newer server installers DCS.dcs_serverrelease/DCS.release_server), otherwise DCS.server.
    internal static string ServerSavedGames(string sg, params string?[] known) =>
        known.Select(d => d?.Trim()).FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)) is { } k ? Path.TrimEndingDirectorySeparator(k)
        : ServerDirs.Select(n => Path.Combine(sg, n)).FirstOrDefault(Directory.Exists) ?? Path.Combine(sg, ServerDirs[0]);
    internal static readonly string[] ServerDirs = { "DCS.server", "DCS.dcs_serverrelease", "DCS.release_server", "DCS.openbeta_server" };

    internal static string? ShellFolder(string? v) => string.IsNullOrWhiteSpace(v) ? null : Environment.ExpandEnvironmentVariables(v);   // REG_EXPAND_SZ: %USERPROFILE%\…

    static void CheckMissionScripting()
    {
        var f = Path.Combine(DcsDir(), "Scripts", "MissionScripting.lua");
        var fix = Path.Combine(Root, "DcsAtc-Patch.ps1");
        try
        {
            if (fixAsked || !File.Exists(f) || !File.Exists(fix) || File.ReadAllText(f).Contains("DcsAtcMission")) return;
            fixAsked = true;   // ask only once per app start
            Log(L("[ATC] DCS-Update hat die DCS-ATC-Zeile in MissionScripting.lua entfernt -> Admin-Abfrage (UAC). Mission danach neu starten.",
                  "[ATC] A DCS update removed the DCS-ATC line from MissionScripting.lua -> admin prompt (UAC). Restart the mission afterwards."));
            Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{fix}\" -Dcs \"{Path.GetDirectoryName(Path.GetDirectoryName(f))}\"")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (Exception e)
        {
            Log(L($"[ATC] MissionScripting.lua nicht angepasst ({e.Message}) -> Startmenü: DCS-ATC -> \"Nach DCS-Update reparieren\".",
                  $"[ATC] MissionScripting.lua not patched ({e.Message}) -> Start menu: DCS-ATC -> \"Repair after DCS update\"."));
        }
    }

    // ------------------------------------------------------------ Airfields (data from the mission script, Kutaisi from the map)
    static readonly string AirfieldFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Airfields.txt");
    static DateTime airfieldStamp;
    /// Test modes (--selftest, --anflugtest, --mptest, --asr-test, --srs-test): only the repo airfields.txt. Otherwise a test read the airfield data of the
    /// running mission (Persian Gulf instead of Caucasus, different results depending on the last mission) and thereby overwrote the checked-in airfields.txt.
    static bool TestRun;
    static bool MissionFields => !TestRun && File.Exists(AirfieldFile);

    static void LoadFields()
    {
        var copy = Path.Combine(Root, "airfields.txt");   // last known data, in case DCS has not run yet
        if (MissionFields)
        {
            airfieldStamp = File.GetLastWriteTime(AirfieldFile);
            try { File.Copy(AirfieldFile, copy, true); } catch (IOException) { }
        }
        Tower.Freqs = Cfg.Frequencies;
        Fields = Airfield.Load(MissionFields ? AirfieldFile : copy);
        Tower.All = Fields;
        Ops.MagVar = MapMagVar(Fields);
        var dcs = DcsDir();
        if (!DcsExe(dcs))
            Log(L("[DCS] DCS-Installation nicht gefunden: Frequenzen aus Ersatzdaten", "[DCS] DCS installation not found: frequencies from fallback data"));
        Airfield.LoadRadio(Fields, dcs);
        Airfield.LoadBeacons(Fields, dcs);
        if (!TestRun) { WriteFreqFile(FreqPath); ApplyOwnFreqs(); }   // new map: extend frequencies.jsonc, own frequencies (also ATIS transmitters) of these airfields
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

    /// Forum request: apply frequencies.jsonc (OwnFreqs) to the airfields (Airfield.Own). Airfield name as in DCS or Radio.lua name (RadioName), case, hyphen/space irrelevant, a unique partial name suffices;
    /// per controller (ATIS, Ground, Tower, Departure, Approach) one frequency or a list (UHF, VHF), 30–400 MHz. Invalid: entry dropped, one line per error (return value, for the log);
    /// duplicate controller frequency (two airfields, also the map frequency of another): stays, one warning line.
    internal static List<string> ApplyFreqs(List<Airfield> fields, Dictionary<string, JsonElement>? cfg)
    {
        var bad = new List<string>();
        static string N(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
        foreach (var f in fields) f.Own.Clear();
        foreach (var (name, roles) in cfg ?? new())
        {
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

    /// Read frequencies.jsonc and apply to the map's airfields (under TowerLock or when loading the airfields), update ATIS transmitters. File currently locked: next second.
    static void ApplyOwnFreqs()
    {
        string text;
        try { text = File.Exists(FreqPath) ? File.ReadAllText(FreqPath) : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        freqAt = File.GetLastWriteTimeUtc(FreqPath);
        var (own, err) = OwnFreqs(text, Airfield.Theatre);
        if (err != null) Log(err);
        foreach (var w in ApplyFreqs(Fields, own)) Log(w);
        if (atisOn.Count > 0) StartAtis();
    }

    /// Entries of frequencies.jsonc (comments, trailing comma allowed): under "// ===== Karte =====" only for this map, without a section for all. Invalid: none, with message.
    internal static (Dictionary<string, JsonElement> Own, string? Error) OwnFreqs(string text, string map)
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
                if (sec == null || sec.Equals(map, StringComparison.OrdinalIgnoreCase)) own[name] = v;
            }
        }
        catch (JsonException e)
        {
            return (new(), L($"[ATC] frequencies.jsonc: Fehler ({e.Message}) – keine eigenen Frequenzen", $"[ATC] frequencies.jsonc: error ({e.Message}) – no own frequencies"));
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
            var text = FreqFile(old, maps, Airfield.Theatre, Fields.Where(f => f.Id > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First().Name));
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
        $"{(spoken.StartsWith("checking in") ? station + ", " : "")}{callsign.Replace('-', ' ')}, {spoken}.";

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
        bool On(Airfield f) => fq != null && Has(FieldFreqs(f, "*"), fq);
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
        var run = p.Active != null && Ok(p.Active.F) && (tel == null || p.Active.Phase is not (Phase.Parked or Phase.Away) || p.Active.Phase == Phase.Away && p.Active.Radar && D(p.Active.F) < 25 * NM);
        var tuned = Fields.Where(f => Ok(f) && On(f)).ToList();
        if (tuned.Count > 0) return p.Fresh is { } fr && tuned.Contains(fr) ? TowerFor(p, fr) : run && tuned.Contains(p.Active!.F) ? p.Active! : TowerFor(p, tuned.MinBy(D)!);
        if (run) return p.Active!;
        return Fields.Where(Ok).MinBy(D) is { } nf ? TowerFor(p, nf) : null;
    }

    /// Called airfield: name word at the start of the call or before ground/tower/approach/departure/radar ("Kutaisi Tower, …", "Enfield 1-1, Kutaisi Tower"),
    /// not "clearance to Batumi" or "contact Kutaisi Approach" (also "contact Ras Al Khaimah Intl Approach": up to three name words before it).
    /// Same number of name words: own (airfield frequency), otherwise the nearest. null = none.
    static Airfield? Called(string text, Telemetry? tel, Airfield? own)
    {
        var n = Regex.Replace(text.ToLowerInvariant(), "[^a-z ]", " ");
        return Tower.ByName(Fields.OrderBy(f => f != own).ThenBy(f => tel == null ? 0 : Dist(tel.X, tel.Z, f.X, f.Z)),
            w => Regex.IsMatch(n, $@"^\W*(?!contact\b)(?:\w+\W+)?{w}\b|(?<!\bcontact\W+(?:\w+\W+){{0,3}})\b{w}\W+(?:ground|tower|approach|departure|radar)\b"));
    }

    // ------------------------------------------------------------ Save session: app crash/restart mid-flight (phase, check-ins, debriefing are kept)
    record PilotSave(double X, double Z, string? Pinned, string? OnBoat, DateTime? Airborne, string? Field, Tower.State? Tower, Carrier.State Boat, Ops.State Ops);
    static string SessionFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Session.json");
    static Dictionary<string, PilotSave>? saved;   // loaded at the first player, applied once per unit
    static double savedAt = -99;

    static void SaveSession()
    {
        if (SimSec >= 0 || Now() - savedAt < 5) return;
        savedAt = Now();
        var d = Pilots.Values.Where(p => p.Tel != null).ToDictionary(p => p.Unit, p => new PilotSave(p.Tel!.X, p.Tel.Z, p.Pinned?.F.Name, p.OnBoat, p.Airborne,
                    p.Active?.F.Name, p.Active?.Save(), p.Boat.Save(), p.Ops.Save()));
        try { File.WriteAllText(SessionFile + ".tmp", JsonSerializer.Serialize(d)); File.Move(SessionFile + ".tmp", SessionFile, true); } catch (IOException e) { Trace("FEHLER", $"Sitzung sichern: {e.Message}"); }
    }

    /// New player: take over the pre-crash state if the file is younger than 10 min and he is still flying there (otherwise mission restart).
    static void RestoreSession(Pilot p)
    {
        if (saved == null)
        {
            saved = new();
            try { if (Clock - File.GetLastWriteTime(SessionFile) < TimeSpan.FromMinutes(10)) saved = JsonSerializer.Deserialize<Dictionary<string, PilotSave>>(File.ReadAllText(SessionFile)) ?? new(); }
            catch (Exception e) when (e is IOException or JsonException) { Trace("FEHLER", $"Sitzung laden: {e.GetType().Name}: {e.Message}"); }
        }
        if (p.Tel is not { } t || !saved.Remove(p.Unit, out var s) || Dist(t.X, t.Z, s.X, s.Z) > 3 * NM) return;
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
        if (p.Active != null) { t.Following |= p.Active.Following; p.Active.Following = false; t.Squawk = p.Active.Squawk ?? t.Squawk; p.Active.Squawk = null; }   // V15: flight following moves along, A90: transponder code per flight too
        Trace("PLATZ", $"{p.Callsign}: {p.Active?.F.Name ?? "-"} -> {t.F.Name} ({pickWhy ?? PickWhy(p, null, t)})");
        pickWhy = null;
        p.Active = t;
        Log($"[ATC] {p.Callsign}: jetzt {t.F.Name}");
    }

    // ------------------------------------------------------------ Mission data (KutaisiATC.lua -> %TMP%\DcsAtc-State.txt)
    static readonly string StateFile = Path.Combine(Path.GetTempPath(), "DcsAtc-State.txt");
    static DateTime stateStamp, stateTime = DateTime.MinValue;
    static bool lsoWarm;
    static List<Traffic> Ai = new();
    static double aiAt;   // Time of the last AI state (climb rate)
    static Dictionary<string, (double Wx, double Wz, double TempC, double Pa)> Wx = new();
    static (bool Clouds, double BaseM, double VisM) Sky = (false, 0, 80000);
    static bool MissionData => (Clock - stateTime).TotalSeconds < 5;

    static double missionSec = -1;   // Mission run time (timer.getTime) of the last state
    /// R120: mission run time jumps back = new mission or restart: old identifiers gone, the first ATIS with fresh weather gets exactly one identifier.
    /// Not after a wall-clock gap: pause (ESC) halts the mission clock and writing, the identifier stays.
    static void MissionGap(double sec)
    {
        if (sec < missionSec && Atis.Count > 0) { Atis.Clear(); Log("[ATIS] neue Mission (Laufzeit zurückgesprungen) -> Kennungen neu"); }
        if (sec < missionSec) Carrier.NewMission();   // Review l4: R229 "99" state reset per ship
        missionSec = sec;
    }

    static void ReadState()
    {
        if (!File.Exists(StateFile)) return;
        var stamp = File.GetLastWriteTimeUtc(StateFile);
        if (stamp == stateStamp || DateTime.UtcNow - stamp > TimeSpan.FromSeconds(5)) return;
        string[] lines;
        try { lines = File.ReadAllLines(StateFile); } catch (IOException) { return; }
        stateStamp = stamp;
        stateTime = Clock;
        var ai = new List<Traffic>();
        var wx = new Dictionary<string, (double, double, double, double)>();
        var det = new Dictionary<int, Dictionary<int, bool>>();   // Radar picture per side (V9)
        var gci = new HashSet<int>();
        var boats = new List<Carrier.Boat>();
        var fu = new Dictionary<string, Flights.Unit>();
        (double, double, double)? rz = null;   // A17: mission without range zone (mission change) -> no range controller any more
        foreach (var line in lines)
        {
            var p = line.Split(';');
            double D(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
            try
            {
                switch (p[0])
                {
                    case "G" when p.Length >= 4: Sky = (D(1) > 0, D(2), D(3)); break;
                    case "S" when p.Length >= 2: MissionGap(D(1)); break;   // R120: mission runtime
                    case "Z" when p.Length >= 4: rz = (D(1), D(2), D(3)); break;     // Range Alpha
                    case "B" when p.Length >= 3: Ops.Bulls[p.Length >= 4 && int.TryParse(p[3], out var bs) ? bs : 0] = (D(1), D(2)); break;   // Bullseye per side
                    case "K" when p.Length >= 3:                                                 // Tanker TACAN[;track]
                        Ops.Beacons[p[1]] = p[2];
                        if (p.Length > 3 && p[3] != "") Ops.Tracks[p[1]] = p[3];
                        break;
                    case "F" when p.Length >= 3: Ops.AwacsFreq[p[1]] = D(2); break;            // AWACS radio frequency (mission)
                    case "C" when p.Length >= 12:   // C;unit;group;type;x;z;hdg;speed;side;freq;tacan;icls;wx;wz;pa;noTurn (carrier)
                        boats.Add(new(p[1], p[2], p[3], D(4), D(5), D(6), D(7), int.Parse(p[8]), D(9), p[10], p[11], p.Length > 13 ? D(12) : 0, p.Length > 13 ? D(13) : 0, p.Length > 14 ? D(14) : 0, p.Length > 15 && p[15] == "1"));
                        break;
                    case "Y" when p.Length >= 2: Carrier.Clock = D(1); Carrier.Sun = p.Length >= 3 && p[2] != "" ? D(2) : double.NaN; break;   // Mission clock[;sun elevation at the carrier]
                    case "R" when p.Length >= 2: gci.Add((int)D(1)); break;                    // Side with ground radar (GCI, V19)
                    case "D" when p.Length >= 2:                                                 // D;side[;unit;type known]
                        if (!det.TryGetValue((int)D(1), out var ds)) det[(int)D(1)] = ds = new();
                        if (p.Length >= 4) ds[p[2].GetHashCode()] = p[3] == "1";
                        break;
                    case "W" when p.Length >= 6:   // W;name;wx;wz;t;p[;side]
                        wx[p[1]] = (D(2), D(3), D(4), D(5));
                        if (p.Length > 6) FieldSide(p[1], (int)D(6));
                        break;
                    case "T" when p.Length >= 11:   // T;unit;group;type;x;z;alt;hdg;speed;inAir;flag;coalition;callsign;fuel;gid;freq;ammo;kind;fuelkg
                        ai.Add(new Traffic(p[1].GetHashCode(), p[3], D(4), D(5), D(6), D(7), D(8), p[2], p[10], p[9] == "1", p.Length > 11 ? int.Parse(p[11]) : 0,
                            Cs: p.Length > 12 ? p[12] : "", Fuel: p.Length > 13 ? D(13) : 1, Heli: p.Length > 17 && p[17] == "h"));   // Callsign and fuel per unit (ball call), AiCallsign/AiFuel per group only
                        if (p.Length > 12) AiCallsign[p[2]] = p[12];
                        if (p.Length > 13) AiFuel[p[2]] = D(13);
                        if (p.Length > 14) AiGid[p[2]] = (int)D(14);
                        if (p.Length > 16)   // AI radio: flight frequency, ammunition fox3/fox1/fox2/gun/air-to-ground
                            fu[p[1]] = new Flights.Unit(p[1], p[2], p[3], p[12], int.Parse(p[11]), D(4), D(5), D(6), p[9] == "1", D(13), D(15),
                                p[16].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(), p[10], (int)D(14), D(7), p.Length > 18 ? D(18) : 0);   // D(7): heading (rad) for commit/RTB; D(18): internal tank kg (LK15 ops check)
                        break;
                    case "P" when p.Length >= 17:   // P;unit;gid;callsign;player;type;x;z;alt;agl;speed;hdg;windx;windz;pressure;vy;inAir;id;coalition;fuel;gear;kind (h/a)
                    {
                        bool fresh = !Pilots.TryGetValue(p[1], out var pl);
                        if (fresh)
                        {
                            Pilots[p[1]] = pl = new Pilot { Unit = p[1], Callsign = CallsignFrom(p[3], p[4]) };
                            pl.SlotCallsign = pl.Callsign;
                            Log($"[ATC] Spieler {p[4]} ({p[5]}) als {pl.Callsign}");
                        }
                        pl.Gid = int.Parse(p[2]);
                        pl.Name = p[4];
                        pl.Type = p[5];
                        if (p.Length > 18) { pl.Id = uint.Parse(p[17]); pl.Coalition = int.Parse(p[18]); }
                        if (p.Length > 19) pl.Fuel = D(19);
                        pl.Heli = p.Length > 21 && p[21] == "h";   // N50: category h/a
                        bool air = p[16] == "1";
                        pl!.Tel = new Telemetry(D(8), air ? Math.Max(D(9), 3.5) : 0, D(10), D(11), D(6), D(7), D(12), D(13), D(14), D(15), p.Length > 20 ? D(20) : -1);
                        pl.Seen = Clock;
                        if ((Cfg.MyCallsign != "" && Host() == pl ? Cfg.MyCallsign : pl.SlotCallsign) is var want && want != pl.Callsign) Rename(pl, want);   // own callsign (settings)
                        if (fresh) RestoreSession(pl);
                        break;
                    }
                }
            }
            catch (FormatException e) { if (Now() - tracedBadState >= 10) { tracedBadState = Now(); Trace("FEHLER", $"State-Zeile {line}: {e.Message}"); } }
        }
        LearnMain(Ai, ai, wx);
        double dt = Now() - aiAt; aiAt = Now();
        Ai = ai.Select(a => dt is > 0.2 and < 30 && Ai.FirstOrDefault(o => o.Id == a.Id) is { } o ? a with { Vs = (a.AltMsl - o.AltMsl) / dt } : a).ToList();   // Climb rate from the last state
        Wx = wx;
        Ops.Detected = det;
        Ops.Gci = gci;
        Ops.Range = rz;
        Carrier.Boats = boats;
        Flights.Units = fu;
        Carrier.Air = ai;
        Carrier.Decks = Pilots.Values.Where(o => o.Tel != null && (Clock - o.Seen).TotalSeconds <= 3).Select(o => new Traffic(o.Unit.GetHashCode(), o.Type, o.Tel!.X, o.Tel.Z, o.Tel.AltMsl, o.Tel.Hdg, o.Tel.Ias, o.Unit, "", o.Tel.Agl > 0, o.Coalition)).ToList();   // Player for foul deck only
        Carrier.Sky = Sky;
        if (boats.Count > 0 && !lsoWarm)   // Pre-render LSO phrases: groove calls without Piper pause (SynthCache)
        {
            lsoWarm = true;
            var lv = PickVoice(Cfg.Voices.GetValueOrDefault("Carrier", "en_US-ryan-medium"), "Paddles");
            Task.Run(() => { int i = 0; foreach (var s in Carrier.LsoLines) try { Synth(s, lv, Path.Combine(Root, "tmp", $"warm{i++}.mp3")); } catch (Exception e) { Trace("FEHLER", $"LSO vorab: {e.GetType().Name}: {e.Message}"); } });
        }
        var host = Host();
        if (host?.Tel != null && CurrentTel()?.Lock is { } lk) host.Tel = host.Tel with { Lock = lk };   // locked target (Export.lua) for own aircraft only: declare
        foreach (var pl in Pilots.Values) pl.Boat.Sample(pl.Tel, Now(), pl == host && CurrentTel() != null ? HostAoa : double.NaN, pl.Type);   // LSO: groove at ~2 Hz
        foreach (var k in Pilots.Where(kv => (Clock - kv.Value.Seen).TotalSeconds > 30).Select(kv => kv.Key).ToList())
        {
            Log($"[ATC] Spieler {Pilots[k].Name} weg");
            Pilots[k].Ops.Leave();   // Range free again; before the debriefing, a pass without "off" is still in it then (R58)
            if (Pilots[k].Ops.Debrief.Count > 0) DebriefTx(Pilots[k], true);
            Pilots[k].Boat.Leave();
            Pilots.Remove(k);
        }
        foreach (var pl in Pilots.Values.Where(pl => (Clock - pl.Seen).TotalSeconds > 3)) pl.Tel = null;
    }

    /// N1: side of an airfield from the W line (every 10 s, capture). Only the field: rewriting the Airfields file triggered LoadFields and cleared Towers/Active/Pinned of all players.
    static void FieldSide(string name, int side)
    {
        if (Fields.FirstOrDefault(f => f.Name == name) is not { } f || f.Side == side) return;
        Log($"[ATC] {name}: Seite {f.Side} -> {side}");
        f.Side = side;
    }

    /// "Enfield11" -> "Enfield 1-1"; without a DCS callsign the player name.
    static string CallsignFrom(string dcs, string player)
    {
        if (NormCallsign(dcs) is { } cs) return cs;
        // Slot without a western callsign (e.g. single player): callsign from config.jsonc, for further players the name
        return Pilots.Values.Any(p => p.Callsign == Cfg.Callsign) ? player : Cfg.Callsign;
    }

    static List<Pilot> ActivePilots()
    {
        if (MissionData) return Pilots.Values.Where(p => p.Tel != null).ToList();
        // Mission data stalls (over 5 s, e.g. pause during rollout): the own mission player keeps flying with Export.lua. Solo, a second flow would be
        // with its own tower ("jetzt Dubai Intl", reset on the runway, takeoff clearance during rollout)
        if (Pilots.Values.Where(p => (Clock - p.Seen).TotalMinutes < 10).ToList() is var mp && (mp.FirstOrDefault(p => p.Name != "" && p.Name == Session()[1]) ?? (mp.Count == 1 ? mp[0] : null)) is { } me)
        {
            me.Tel = CurrentTel();
            return new() { me };
        }
        Solo.Tel = CurrentTel();
        if ((Cfg.MyCallsign != "" ? Cfg.MyCallsign : Cfg.Callsign) is var want && want != Solo.Callsign) Rename(Solo, want);
        return new() { Solo };
    }

    /// Flight: players of the same group close to the lead (lowest callsign) fly as one flight with its flow
    /// (Ground -> Tower -> Approach together). Joins under 1 NM, separated over 3 NM -> own flow again.
    static void FormFlights(List<Pilot> pilots)
    {
        foreach (var g in pilots.Where(p => p.Gid > 0 && p.Tel != null).GroupBy(p => p.Gid))
        {
            var members = g.OrderBy(p => p.Callsign, StringComparer.Ordinal).ToList();
            var lead = members[0];
            lead.Lead = null;
            bool split = lead.Active?.Separate == true;   // V17: separate landing / radar trail
            foreach (var m in members.Skip(1))
            {
                double d = Dist(m.Tel!.X, m.Tel.Z, lead.Tel!.X, lead.Tel.Z);
                bool own = m.Lead == null && m.Active?.Emergency == true;   // R293: own emergency (separate) remains its own sequence
                var nl = !split && !own && d < (m.Lead == lead ? 3 : 1) * NM ? lead : null;
                if (nl == m.Lead) continue;
                Log(nl != null ? $"[ATC] {m.Callsign} fliegt im Flug von {lead.Callsign}"
                               : $"[ATC] {m.Callsign} hat den Flug von {lead.Callsign} verlassen -> eigener Ablauf");
                if (nl == null) { m.Towers.Clear(); m.Active = null; }
                m.Lead = nl;
            }
            lead.FlightSize = 1 + members.Count(m => m.Lead == lead);
        }
    }

    /// You: the player at the position from Export.lua, otherwise the only player. Dedicated server: nobody (no DCS keys, no own callsign, no lighting via the menu).
    static Pilot? Host()
    {
        if (Dedicated) return null;
        if (!MissionData) return ActivePilots()[0];
        var players = Pilots.Values.Where(p => p.Tel != null).ToList();
        var me = Session()[1];   // own player name from the DCS hook
        if (me != "" && players.FirstOrDefault(p => p.Name == me) is { } mine) return mine;
        var tel = CurrentTel();
        if (tel != null)
        {
            var best = players.MinBy(p => Dist(p.Tel!.X, p.Tel.Z, tel.X, tel.Z));
            if (best != null && Dist(best.Tel!.X, best.Tel.Z, tel.X, tel.Z) < 300) return best;
        }
        return players.Count == 1 ? players[0] : null;
    }

    /// Traffic for a player: AI + all other players.
    static IReadOnlyList<Traffic> TrafficFor(Pilot p)
    {
        if (!MissionData) return CurrentTraffic();
        var list = Ai.Where(a => p.Gid == 0 || AiGid.GetValueOrDefault(a.Group) != p.Gid)   // not own AI flight members
                     .Select(a => a.Flag.StartsWith("arr-hold") && aiStackFt.TryGetValue(a.Group, out var hf) ? a with { AsgFt = hf } : a).ToList();   // R216: AI in holding at assigned altitude
        foreach (var o in Pilots.Values.Where(o => o != p && o.Lead != p && o.Tel != null))
            list.Add(new Traffic(o.Unit.GetHashCode(), o.Type, o.Tel!.X, o.Tel.Z, o.Tel.AltMsl, o.Tel.Hdg, o.Tel.Agl < 15 ? Tower.Gs(o.Tel) : o.Tel.Ias, o.Unit,   // R331: on the ground over ground
                o.Active is { HoldArrived: true } ? "hold:" + o.Active.F.Name :
                o.Active is { Phase: not Phase.Away } tw ? "twr:" + tw.F.Name : "", o.Tel.Agl > 0, o.Coalition, o.Tel.Vs, Heli: o.Heli,   // R270: "hold:" only at the holding point (on the way there like approach)
                AsgFt: o.Active?.AsgFt ?? 0));
        return list;
    }

    // ------------------------------------------------------------ Control AI traffic Kutaisi (KutaisiTraffic.lua)
    static readonly Dictionary<string, DateTime> aiSent = new();
    static readonly Dictionary<string, DateTime> lastAiStart = new();   // per airfield

    static readonly Dictionary<string, string> AiCallsign = new(), aiStage = new();
    static readonly Dictionary<string, int> AiGid = new();   // AI group -> DCS group ID (the player's AI flight members are not traffic)

    /// Let departures start only when no player needs the runway; approaches into holding while players are in the traffic pattern.
    /// The AI talks audibly on the radio (request, controller, readback) – speech only, no text in the game.
    /// Landing sequence per airfield ("ai:Gruppe" / "p:Unit"): AI approaches and players on approach in the order
    /// in which they join (simultaneous: by distance). Positions 1–2 approach, the rest wait staggered (Tower.StackFt).
    static readonly Dictionary<string, List<string>> ArrQueue = new();
    static readonly Dictionary<string, double> AiFuel = new();   // AI group -> tank 0–1

    /// Terrain grid from the mission (%TMP%DcsAtc-Terrain-<Karte>-<Id>.txt) for airfields without terrain, at most every 10 s.
    static DateTime terrainCheck;
    static void LoadTerrain()
    {
        if ((Clock - terrainCheck).TotalSeconds < 10) return;
        terrainCheck = Clock;
        foreach (var fld in Fields.Where(f => !f.HasTerrain && f.Id > 0).Append(Airfield.Map).Where(f => f != Airfield.Map ? !f.HasTerrain : f.Name != Airfield.Theatre))
        {
            var file = Path.Combine(Path.GetTempPath(), $"DcsAtc-Terrain-{Airfield.Theatre}-{(fld == Airfield.Map ? "map" : fld.Id)}.txt");   // map: coarse grid of the whole map (V15)
            try { if (File.Exists(file)) { fld.LoadTerrain(file); if (fld == Airfield.Map) fld.Name = Airfield.Theatre; } } catch (Exception e) when (e is IOException or FormatException or IndexOutOfRangeException) { }
        }
    }

    static void UpdateQueues(List<Pilot> pilots)
    {
        foreach (var fld in Fields)
        {
            var cur = new Dictionary<string, double>();
            // Distance to the airfield; in holding that of the holding point (circling should not upset the sequence)
            double D(string k, double x, double z) => Holds.TryGetValue(k, out var h) ? Dist(h.X, h.Z, fld.X, fld.Z) : Dist(x, z, fld.X, fld.Z);
            foreach (var a in Ai.Where(a => a.InAir && a.Group != ""))
            {
                var fl = a.Flag.Split(':', 2);
                if (fl[0] is "arr" or "arr-hold" && (fl.Length > 1 ? fl[1] : "Kutaisi") == fld.Name && (a.Coalition == 0 || Served(a.Coalition, fld))) cur.TryAdd("ai:" + a.Group, D("ai:" + a.Group, a.X, a.Z));   // N40: captured -> out (DIVERT)
            }
            foreach (var p in pilots.Where(p => p.Active?.F == fld && p.Tel is { Agl: > 30 } &&
                                                p.Active.Phase is Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand))
                cur.TryAdd("p:" + p.Unit, D("p:" + p.Unit, p.Tel!.X, p.Tel.Z));
            var q = ArrQueue.GetValueOrDefault(fld.Name) ?? new();
            foreach (var k in cur.Keys.Where(k => cur[k] > SeqNm * NM && !q.Contains(k)).ToList()) cur.Remove(k);   // further out: not yet in the sequence (does not hold up nearer ones)
            if (cur.Count == 0) { ArrQueue.Remove(fld.Name); continue; }
            ArrQueue[fld.Name] = q;
            q.RemoveAll(k => !cur.ContainsKey(k));
            // Sort newcomers in by distance, then fixed order (otherwise waiting and cleared aircraft overtake each other, clearance in a bunch)
            foreach (var k in cur.Keys.Where(k => !q.Contains(k)).OrderBy(k => cur[k]).ToList()) q.Insert(Slot(q, fld, cur[k]), k);
            // Priority: AI only with MAYDAY FUEL (tank 0–1 from the mission below LowFuel, R298), players only after a reported emergency, not silently by fuel state and not after "minimum fuel" (A29, AIM 5-5-15)
            bool Low(string k) => k.StartsWith("ai:") ? AiFuel.GetValueOrDefault(k[3..], 1) < LowFuel
                : pilots.FirstOrDefault(p => "p:" + p.Unit == k)?.Active is { Emergency: true };
            // Low fuel to the front, but not ahead of those on final
            foreach (var k in q.Where(Low).ToList())
            {
                // ahead of all without low fuel, but behind others with low fuel and behind the one already on final (no back and forth)
                int to = q.TakeWhile(x => x != k && (Low(x) || x == q[0] && cur[x] < 6 * NM)).Count();
                if (q.IndexOf(k) > to) { q.Remove(k); q.Insert(to, k); }
            }
        }
    }
    const double MinFuel = 0.20, LowFuel = 0.10, SeqNm = 40;   // AI tank: "minimum fuel" (no priority) / MAYDAY FUEL (priority, R298); landing sequence only from 40 NM from the airfield
    static readonly Dictionary<string, double> aiStackFt = new();   // AI group -> altitude in holding
    static readonly HashSet<string> fuelTold = new();

    /// Learn the DCS main runway: if an AI aircraft lands/takes off in light wind (> 40 m/s, so not a helicopter),
    /// that is the direction DCS-ATC assigns there. Parallel runways: the one on whose centerline it is.
    static void LearnMain(List<Traffic> was, List<Traffic> now, Dictionary<string, (double, double, double, double)> wx)
    {
        foreach (var a in now)
            if (was.FirstOrDefault(o => o.Id == a.Id) is { } w0 && w0.InAir != a.InAir && a.Speed > 40 &&
                Fields.FirstOrDefault(f => Dist(f.X, f.Z, a.X, a.Z) < 3000) is { } f && wx.TryGetValue(f.Name, out var w) &&
                Math.Sqrt(w.Item1 * w.Item1 + w.Item2 * w.Item2) < 3.5 &&
                f.Ends.Where(e => Tower.HdgDiff(e.Hdg, a.Hdg * 180 / Math.PI) < 20).MinBy(e => Math.Abs((a.X - e.CX) * e.Dz - (a.Z - e.CZ) * e.Dx)) is { } e && e.Name != f.Main)
            {
                Log($"[KI ] {f.Name}: DCS-Hauptbahn {e.Name} statt {f.Main} ({(a.InAir ? "Start" : "Landung")} {a.Group})");
                f.Main = e.Name;
            }
    }

    /// Runway at the airfield like DCS-ATC (wind from the mission weather, Airfield.BestRunway): the AI lands accordingly.
    static string FieldRunway(Airfield f)
    {
        var w = Wx.GetValueOrDefault(f.Name);
        return f.BestRunway(w.Wx, w.Wz);
    }

    /// All holdings of all airfields ("ai:Gruppe" / "p:Unit" -> point, altitude ft).
    static readonly Dictionary<string, (double X, double Z, double Ft)> Holds = new();
    /// Next free altitude from ft: no other holding within 14 NM (circling, traffic warning) closer than 1000 ft (regardless of airfield).
    static double FreeLevel(string key, double x, double z, double ft)
    {
        for (int i = 0; i < 30 && Holds.Any(h => h.Key != key && Dist(h.Value.X, h.Value.Z, x, z) < 14 * NM && Math.Abs(h.Value.Ft - ft) < 1000); i++) ft += 1000;
        return ft;
    }

    /// Place of a newcomer (distance d) in the landing sequence: before the first waiting aircraft whose holding point is further out, never before one already on approach.
    static int Slot(List<string> q, Airfield f, double d) => q.FindIndex(k => Holds.TryGetValue(k, out var h) && Dist(h.X, h.Z, f.X, f.Z) > d) is var i and >= 0 ? i : q.Count;

    /// Landing sequence: number 1 approaches, each further one only when its predecessor has been fully handled: player at the tower in the break,
    /// in the pattern or with landing clearance, AI on final (up to 9 NM on the line, heading to the runway) or landed. This way only one at a time is
    /// brought in (no conflicts en route; the AI first flies to its point outside on the line, also straight across the airfield).
    /// R46: player predecessor vectored to final or on final: staggered as soon as the follower (me, otherwise q[pos]) by straight-line distance to the threshold
    /// would be at least 3 NM (behind Heavy 5 NM) behind (Tower.Behind), also for the AI sequence.
    static bool SpacedAt(List<string> q, int pos, Airfield f, List<Pilot> pilots, (double X, double Z)? me = null) =>
        pos <= 0 || pos > q.Count || !Holds.ContainsKey(q[pos - 1]) && (q[pos - 1].StartsWith("p:")
            ? pilots.FirstOrDefault(p => "p:" + p.Unit == q[pos - 1])?.Active?.Phase is not (Phase.Inbound or Phase.Entering)   // Player: only in the break/pattern or with landing clearance (straight in ~3 NM), not already at the handover 8–10 NM out
              || (me ?? (pos < q.Count ? PosOf(q[pos], pilots) : null)) is { } m && AheadOf(q[pos - 1], f, pilots) is var (r, hv) && r >= 0 &&
                 f.End(FieldRunway(f)) is var te && Tower.Behind(Dist(m.X, m.Z, te.ThrX, te.ThrZ), r, hv)
            : Ai.FirstOrDefault(x => "ai:" + x.Group == q[pos - 1]) is not { } a || !a.InAir || f.End(FieldRunway(f)) is var e && Dist(a.X, a.Z, e.ThrX, e.ThrZ) < 9 * NM &&
              Tower.HdgDiff(a.Hdg * 180 / Math.PI, e.Hdg) < 30 && Tower.HdgDiff(Tower.Bearing(a.X, a.Z, e.ThrX, e.ThrZ), e.Hdg) < 20);

    /// R46: remaining distance (m) of a landing sequence entry to the threshold (player: Tower.FinalPath, AI in the air and not in holding: straight line), -1 = unknown; Heavy.
    static (double R, bool Heavy) AheadOf(string k, Airfield f, List<Pilot> pilots) =>
        k.StartsWith("p:") ? pilots.FirstOrDefault(p => "p:" + p.Unit == k) is { Active: { } tw, Tel: { } pt } pl && tw.F == f ? (tw.FinalPath(pt), Tower.Heavy(pl.Type)) : (-1, false)
        : Ai.FirstOrDefault(x => "ai:" + x.Group == k) is { InAir: true } a && !Holds.ContainsKey(k) && f.End(FieldRunway(f)) is var e ? (Dist(a.X, a.Z, e.ThrX, e.ThrZ), Tower.Heavy(a.Type)) : (-1, false);

    /// Position of a landing sequence entry ("ai:Gruppe" / "p:Unit").
    static (double X, double Z)? PosOf(string k, List<Pilot> pilots) =>
        k.StartsWith("ai:") ? Ai.FirstOrDefault(x => x.Group == k[3..]) is { } t0 ? (t0.X, t0.Z) : null
                            : pilots.FirstOrDefault(p => "p:" + p.Unit == k)?.Tel is { } pt ? (pt.X, pt.Z) : null;

    static void ControlAi(List<Pilot> pilots, List<Tx> outq)
    {
        if (!MissionData) return;
        bool Once(string key)
        {
            if (aiSent.TryGetValue(key, out var t0) && (Clock - t0).TotalSeconds < 120) return false;   // Mission sets the flag; otherwise do not repeat constantly
            aiSent[key] = Clock;
            return true;
        }
        foreach (var k in Holds.Keys.Where(k => k.StartsWith("ai:") && !Ai.Any(a => a.Group == k[3..] && a.InAir && a.Flag.StartsWith("arr-hold"))).ToList())
            Holds.Remove(k);   // landed / gone
        foreach (var g in Ai.Where(a => a.Flag != "" && a.Group != "").GroupBy(a => a.Group))
        {
            var a = g.First();
            var fl = a.Flag.Split(':', 2);   // "dep:Senaki-Kolkhi" (mod), old mission: "dep" = Kutaisi
            var flag = fl[0];
            if (Fields.FirstOrDefault(f => f.Name == (fl.Length > 1 ? fl[1] : "Kutaisi")) is not { } fld || fld.Ends.Count == 0) continue;
            if (a.Coalition > 0 && !Served(a.Coalition, fld))   // N40: airfield captured: no radio from the hostile airfield, divert approaches once to the nearest own (they check in anew there)
            {
                if (flag is "arr" or "arr-hold" && a.InAir && Once("DIVERT;" + g.Key)) { AiCmd($"DIVERT;{g.Key}"); aiStage.Remove(g.Key); Holds.Remove("ai:" + g.Key); Log($"[KI ] {g.Key}: {fld.Name} erobert -> umgeleitet"); }
                continue;
            }
            var herePl = pilots.Where(p => p.Active?.F == fld && p.Tel != null && Dist(p.Tel.X, p.Tel.Z, fld.X, fld.Z) < 30 * NM).ToList();
            var here = herePl.Select(p => p.Active!).ToList();
            var q = ArrQueue.GetValueOrDefault(fld.Name);
            int pos = q?.IndexOf("ai:" + g.Key) ?? -1;
            // Radio audible/visible only if it concerns the player: AI ≤ 10 NM from him or directly ahead/behind him in the landing sequence
            bool concerns = herePl.Any(p => Dist(p.Tel!.X, p.Tel.Z, a.X, a.Z) < 10 * NM ||
                                            pos >= 0 && q!.IndexOf("p:" + p.Unit) is var pp && pp >= 0 && Math.Abs(pp - pos) == 1);
            bool emergency = here.Any(t => t.Mayday);   // MAYDAY: AI holds, runway clear, AI radio at the airfield silent (A24); PAN priority only (UpdateQueues)
            // other AI at the airfield (A82): departure with takeoff clearance still on the ground ("ready") or rolling on the runway (takeoff, rollout)
            string FieldOf(Traffic o) => o.Flag.Split(':', 2) is [_, var n] ? n : "Kutaisi";
            var rw = FieldRunway(fld);   // DCS assigns the runway, then the AI lands/takes off
            bool aiDep = Ai.Any(o => o.Group != g.Key && !o.InAir && o.Flag.StartsWith("dep-go") && FieldOf(o) == fld.Name && aiStage.GetValueOrDefault(o.Group) == "ready");
            bool luaw = here.Any(t => t.Phase == Phase.HoldShort && t.LineUp);   // A9/A10: waiting at the holding point blocks nothing, waiting on the runway does
            bool blocked = herePl.Any(p => Tower.Blocks(fld, rw, p.Tel!.X, p.Tel.Z, p.Tel.Agl, p.Tel.Hdg, Tower.Gs(p.Tel))) ||   // Player on the runway (A8: also standing, rollout above 6000 ft free)
                          Ai.Any(o => o.Group != g.Key && Tower.Blocks(fld, rw, o.X, o.Z, o.AltMsl - fld.Elev, o.Hdg, o.Speed));
            bool rwyBusy = emergency || aiDep || luaw || here.Any(t => t.Phase is Phase.ClearedTakeoff or Phase.ClearedLand) || blocked;
            bool finalNear = herePl.Any(p => p.Active!.Phase is Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand && p.Active.OnFinalWithin(p.Tel!, 4 * NM));   // A9: player on final up to 4 NM -> no AI takeoff clearance
            // Player on approach or at departure (A41) on the opposite direction (terrain, Tower.Plan, runway request): AI waits and does not take off
            bool contra = here.Any(t => Tower.HdgDiff(fld.End(t.ApproachRwy).Hdg, fld.End(rw).Hdg) > 90 &&
                                        t.Phase is Phase.TaxiOut or Phase.HoldShort or Phase.ClearedTakeoff or Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand);
            bool arrNear = herePl.Any(p => p.Active!.Phase is Phase.Inbound or Phase.Entering or Phase.Initial && p.Active.HoldInfo == null && Dist(p.Tel!.X, p.Tel.Z, fld.X, fld.Z) < 12 * NM);   // Player is being vectored in: AI departure waits (climb would cross otherwise)
            var rws = Tower.RwSay(rw);
            var thr = fld.End(rw);
            // R338: a player's landing clearance blocks the AI landing only if he is down or ahead of it; the reason is in the call (FAA JO 7110.65 3-8-1, 3-10-6)
            double Al(double x, double z) => -((x - thr.ThrX) * thr.Dx + (z - thr.ThrZ) * thr.Dz);
            var leadPl = herePl.Where(p => p.Active!.Phase == Phase.ClearedLand && p.Tel!.Agl >= 15 && Al(p.Tel.X, p.Tel.Z) < Al(a.X, a.Z)).MaxBy(p => Al(p.Tel!.X, p.Tel.Z));
            bool depNow = aiDep || here.Any(t => t.Phase == Phase.ClearedTakeoff);
            bool rwyBusyArr = emergency || depNow || luaw || leadPl != null || blocked || herePl.Any(p => p.Active!.Phase == Phase.ClearedLand && p.Tel!.Agl < 15);
            string busyWhy = leadPl != null ? $"number two, follow the {Ops.TypeSay(leadPl.Type)} on {Math.Max(1, (int)Math.Round(Al(leadPl.Tel!.X, leadPl.Tel.Z) / NM))} mile final, continue"
                           : $"continue approach, {(emergency ? "emergency in progress" : depNow ? $"traffic departing runway {rws}" : "traffic on runway")}";
            var m = Regex.Match(AiCallsign.GetValueOrDefault(g.Key, ""), @"^([A-Za-z]+)\s*(\d)\s*-?\s*(\d)");
            var cs = m.Success ? Tower.SpokenCallsign($"{m.Groups[1].Value} {m.Groups[2].Value}-{m.Groups[3].Value}") : a.Type.Split('_')[0] + " flight";
            var stage = aiStage.GetValueOrDefault(g.Key, "");
            // Like real radio: audible to players ≤ 50 NM who have the airfield frequency tuned in SRS (frequency unknown: players at this airfield)
            bool Heard(string role) => concerns || pilots.Any(p => p.Tel != null && Dist(p.Tel.X, p.Tel.Z, a.X, a.Z) < 50 * NM &&
                (Srs?.TunedFreqs(p.Id) is { } fq ? (FieldFreqs(fld, role) ?? new[] { Cfg.Frequencies.GetValueOrDefault(role, 265.0) }).Any(f => fq.Any(m => Math.Abs(m - f) < 0.005))
                                                 : p.Active?.F == fld));
            void Chat(string role, string pilot, string atc, string? readback)   // Text to everyone on the frequency (ShowTo)
            {
                if (!Heard(role) || emergency) return;
                var voice = PilotVoiceFor(g.Key);
                if (Cfg.AiChatter && pilot != "") outq.Add(new Tx(pilot, role, cs, 0, true, voice, Freqs: FieldFreqs(fld, role), Prio: 2, Side: a.Coalition));
                outq.Add(new Tx(atc, role, fld.StationOf(role), 0, Freqs: FieldFreqs(fld, role), Prio: 2, Side: fld.Side > 0 ? fld.Side : a.Coalition));   // A133: AI pilot with his side, controller with that of the airfield
                if (Cfg.AiChatter && readback != null) outq.Add(new Tx(readback, role, cs, 0, true, voice, Freqs: FieldFreqs(fld, role), Prio: 2, Side: a.Coalition));
            }
            double stack = Math.Max(Tower.StackFt(fld, pos), Math.Ceiling(fld.MvaFt(a.X, a.Z) / 1000) * 1000 + Math.Max(0, pos - 2) * 1000);   // above the terrain
            var hp = Holds.TryGetValue("ai:" + g.Key, out var h0) ? (X: h0.X, Z: h0.Z) : (X: a.X, Z: a.Z);
            stack = FreeLevel("ai:" + g.Key, hp.X, hp.Z, stack);   // Holdings of other airfields: altitude free?
            bool spaced = q == null || SpacedAt(q, pos, fld, pilots);
            // DCS AI taxis after START without stopping into the takeoff roll (does not wait for takeoff clearance), the tower cannot stop it: AI approaches of the landing sequence
            // wait in or go into holding while their landing would fall into the takeoff window of a taxiing AI departure (also in the same tick as START; not yet sequenced ones).
            // Takeoff roll ≈ distance to threshold / 8 m/s + 20 s, landing earliest straight line / speed (min. 60 m/s), latest 120 s after that (detour via the 8-NM final point), plus 90 s separation each.
            // Large airfields (Golf, 3500-m runways, parking spot far from the threshold) otherwise taxi for minutes and hit exactly the next landing. Players stagger Approach/Tower themselves (arrNear, takeoff clearance).
            double RollAt(double x, double z) => Dist(x, z, thr.ThrX, thr.ThrZ) / 8 + 20;
            bool Clash(double roll, double x, double z, double v) => Dist(x, z, thr.ThrX, thr.ThrZ) / Math.Max(v, 60) is var eta && roll > eta - 90 && roll < eta + 120 + 90;
            bool depRoll = flag is "arr" or "arr-hold" && a.InAir && pos >= 0 &&
                           Ai.Any(o => o.Group != g.Key && FieldOf(o) == fld.Name && !o.InAir && aiStage.GetValueOrDefault(o.Group) is "taxi" or "ready" && Clash(RollAt(o.X, o.Z), a.X, a.Z, a.Speed));
            bool wait = emergency || !spaced || contra || luaw || depRoll;   // A10: player waiting on the runway; landing sequence: one after another (DCS flies the approach itself, own waypoints disturb it)
            // R298: two thresholds (AIM 5-5-15): "minimum fuel" advisory only without priority, below that MAYDAY FUEL with priority (UpdateQueues); clearance is the RESUME branch
            double fuel = AiFuel.GetValueOrDefault(g.Key, 1);
            if (flag is "arr" or "arr-hold" && a.InAir && Heard("Approach"))
            {
                if (fuel < LowFuel && fuelTold.Add("may:" + g.Key))
                {
                    fuelTold.Add(g.Key);
                    Chat("Approach", $"MAYDAY MAYDAY MAYDAY FUEL, {fld.StationOf("Approach")}, {cs}, emergency fuel.",
                         $"{cs}, roger mayday, you are number {pos + 1}.", $"Number {pos + 1}, {cs}.");
                }
                else if (fuel < MinFuel && fuelTold.Add(g.Key))
                    Chat("Approach", $"{fld.StationOf("Approach")}, {cs}, minimum fuel.",
                         $"{cs}, roger minimum fuel, you are number {pos + 1}.", $"Number {pos + 1}, {cs}.");
            }
            if (flag == "dep" && !rwyBusy && !contra && !arrNear && (Clock - lastAiStart.GetValueOrDefault(fld.Name)).TotalSeconds > 120 && Once("START;" + g.Key))
            {
                lastAiStart[fld.Name] = Clock;
                AiCmd($"START;{g.Key}");
                aiStage[g.Key] = "taxi";
                Chat("Ground", $"{fld.StationOf("Ground")}, {cs}, request startup and taxi.",
                     $"{cs}, start up approved, taxi to holding point runway {rws}.", $"Taxi holding point runway {rws}, {cs}.");
            }
            // Takeoff clearance only after taxiing (parking spot close to the threshold: not right after engine start) and with a clear runway (player rolling out, other AI taking off),
            // already on approach from 600 m instead of only when stationary under 450 m (A82): DCS often taxis the AI onto the runway without stopping, the clearance otherwise came after the takeoff roll
            else if (flag == "dep-go" && !a.InAir && stage == "taxi" && Dist(a.X, a.Z, thr.ThrX, thr.ThrZ) < 600 && !rwyBusy && !finalNear &&
                     !Ai.Any(o => o.Group != g.Key && o.InAir && FieldOf(o) == fld.Name && aiStage.GetValueOrDefault(o.Group) is "final" or "final-wait" && Dist(o.X, o.Z, thr.ThrX, thr.ThrZ) < 4 * NM) &&   // AI with landing clearance under 4 NM (like TrafficOnFinal for the player)
                     (Clock - lastAiStart.GetValueOrDefault(fld.Name)).TotalSeconds > 90)
            {
                aiStage[g.Key] = "ready";
                Chat("Tower", $"{fld.StationOf("Tower")}, {cs}, holding short runway {rws}, ready for departure.",
                     $"{cs}, runway {rws}, cleared for takeoff.", $"Cleared for takeoff runway {rws}, {cs}.");
            }
            else if (flag == "dep-go" && a.InAir && stage is "taxi" or "ready" && Dist(a.X, a.Z, fld.X, fld.Z) > 5000)
            {
                aiStage[g.Key] = "gone";
                Chat("Tower", "", $"{cs}, contact departure, good day.", $"Good day, {cs}.");
            }
            else if (flag == "arr" && wait && (stage == "" || emergency || contra && !aiSent.ContainsKey("RESUME;" + g.Key)) && a.InAir && Dist(a.X, a.Z, fld.X, fld.Z) > 8 * NM && Once("WAIT;" + g.Key))   // cleared: not into holding again
            {
                AiCmd($"WAIT;{g.Key};1200;{stack:0}");
                aiStackFt[g.Key] = stack;
                Holds["ai:" + g.Key] = (a.X, a.Z, stack);
                aiStage[g.Key] = "inbound";
                Chat("Approach", "", $"{cs}, {(aiSent.ContainsKey("RESUME;" + g.Key) ? "cancel approach clearance, " : "")}hold at present position, {(a.AltMsl / 0.3048 > stack + 300 ? "descend and " : a.AltMsl / 0.3048 < stack - 300 ? "climb and " : "")}maintain {stack:0} feet, speed {Tower.HoldKt} knots, number {pos + 1}, expect approach in {Math.Max(1, pos * 2 - 1)} minutes.",
                     $"Holding, {stack:0} feet, {cs}.");
            }
            else if (flag == "arr-hold" && !wait && Once("RESUME;" + g.Key))
            {
                AiCmd($"RESUME;{g.Key}");
                Holds.Remove("ai:" + g.Key);
                Chat("Approach", "", $"{cs}, leave the hold, number {pos + 1}, cleared for the approach.", $"Leaving the hold, {cs}.");
            }
            else if (flag == "arr-hold" && wait && aiStackFt.TryGetValue(g.Key, out var was) && stack < was - 1 && Once("STACK;" + g.Key + stack))
            {
                AiCmd($"STACK;{g.Key};{stack:0}");   // Predecessor out: one level lower
                aiStackFt[g.Key] = stack;
                Holds["ai:" + g.Key] = (hp.X, hp.Z, stack);
                Chat("Approach", "", $"{cs}, descend and maintain {stack:0} feet, number {pos + 1}.", $"Down to {stack:0}, {cs}.");
            }
            else if (flag == "arr" && a.InAir && stage == "" && pos >= 0)   // only in the sequence (holding or approach)
            {
                aiStage[g.Key] = "inbound";
                Chat("Approach", $"{fld.StationOf("Approach")}, {cs}, inbound for landing.",
                     $"{cs}, {fld.StationOf("Approach")}, radar contact, expect runway {rws}.", $"Runway {rws}, {cs}.");
            }
            else if (flag == "arr" && a.InAir && stage == "inbound" && a.AltMsl - fld.Elev < 700 && Tower.HdgDiff(a.Hdg * 180 / Math.PI, thr.Hdg) < 30 &&
                     -((a.X - thr.ThrX) * thr.Dx + (a.Z - thr.ThrZ) * thr.Dz) is > 0 and < 7 * NM)   // really on final, not in the DCS downwind
            {
                aiStage[g.Key] = rwyBusyArr ? "final-wait" : "final";   // Runway occupied: landing clearance follows as soon as it is free (A83)
                // R301: handover Approach -> Tower with frequency and readback (FAA JO 7110.65 2-1-17, 5-9-4); same frequency: no handover (R104)
                if ((FieldFreqs(fld, "Tower") ?? new[] { Cfg.Frequencies.GetValueOrDefault("Tower", 265.0) })[^1] is var tfq &&
                    Math.Abs(tfq - (FieldFreqs(fld, "Approach") ?? new[] { Cfg.Frequencies.GetValueOrDefault("Approach", 266.5) })[^1]) >= 0.005)
                    Chat("Approach", "", $"{cs}, contact {fld.StationOf("Tower")} {Tower.FreqSay(tfq)}.", $"Tower {Tower.FreqSay(tfq)}, {cs}.");
                Chat("Tower", $"{fld.StationOf("Tower")}, {cs}, final runway {rws}.",
                     rwyBusyArr ? $"{cs}, {busyWhy}." : $"{cs}, runway {rws}, cleared to land.",
                     rwyBusyArr ? $"Continue, {cs}." : $"Cleared to land runway {rws}, {cs}.");
            }
            else if (flag == "arr" && a.InAir && stage == "final-wait" && !rwyBusyArr)
            {
                aiStage[g.Key] = "final";
                Chat("Tower", "", $"{cs}, runway {rws}, cleared to land.", $"Cleared to land runway {rws}, {cs}.");
            }
            else if (stage is "final" or "final-wait" && !a.InAir && a.Speed < 15)   // final-wait: DCS lands the AI even without clearance
            {
                aiStage[g.Key] = "landed";
                Chat("Ground", $"{fld.StationOf("Ground")}, {cs}, runway vacated, request taxi to parking.", $"{cs}, taxi to parking.", $"Taxi to parking, {cs}.");
            }
        }
    }

    /// Self-test AI runway clearances (A41, A82, A83): takeoff clearance already on approach, landing clearance waits for other AI on the runway,
    /// player departing on the opposite runway holds the AI.
    static void ControlAiTest()
    {
        var fld = Airfield.Kutaisi();
        var (fs, ws, ss, sst, sl) = (Fields, Wx, SimSec, stateTime, SimLog);
        var cmds = new List<string>();
        var outq = new List<Tx>();
        try
        {
            Fields = new() { fld }; Wx = new(); SimSec = 5000; stateTime = Clock; SimLog = new(); AiHook = cmds.Add;
            aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
            var rw = FieldRunway(fld);
            var e = fld.End(rw);
            var opp = fld.Ends.First(x => x.Name != rw).Name;
            var (px, pz) = e.At(0, 300);   // Player on the apron, not on the runway
            var pl = new Pilot { Unit = "U1", Callsign = "Enfield 1-1", Tel = new Telemetry(fld.Elev, 0, 0, 0, px, pz, 0, 0, 0) };
            void Player(Phase ph, string r)
            {
                pl.Active = new Tower(fld, "Enfield 1-1");
                pl.Active.Load(new Tower.State(ph, r, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, false, false, null, null, 0, false), pl.Tel!, 0);
            }
            Traffic Plane(string g, string flag, (double X, double Z) p, bool air, double spd, double alt = 0) =>
                new(g.GetHashCode(), "FA-18C_hornet", p.X, p.Z, fld.Elev + alt, e.Hdg * Math.PI / 180, spd, g, flag, air);
            string Said() { var s = string.Join(" | ", outq.Where(t => !t.Pilot && t.Role == "Tower").Select(t => t.Text)); outq.Clear(); return s; }
            void Run() => ControlAi(new() { pl }, outq);

            // A82: taxiing AI 520 m before the threshold gets the takeoff clearance (before only when stationary under 450 m), the second waits until the first has lifted off
            Player(Phase.Parked, rw);
            aiStage["DEP1"] = aiStage["DEP2"] = "taxi";
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(500, 150), false, 6), Plane("DEP2", "dep-go:Kutaisi", e.At(550, 150), false, 6) };
            Run();
            var s1 = Said();
            if (aiStage["DEP1"] != "ready" || aiStage["DEP2"] != "taxi" || !s1.Contains("cleared for takeoff") || s1.Split("cleared for takeoff").Length != 2)
                throw new Exception($"KI-Startfreigabe: {aiStage["DEP1"]}/{aiStage["DEP2"]} \"{s1}\"");
            Console.WriteLine("OK   KI-Startfreigabe schon bei der Annäherung, zweite KI wartet auf die Bahn");

            // A82 the other way round: AI with landing clearance on final (3 NM) -> no takeoff clearance, only after landing
            aiStage["DEP2"] = "taxi"; aiStage["ARR9"] = "final";
            Ai = new() { Plane("DEP2", "dep-go:Kutaisi", e.At(500, 150), false, 6), Plane("ARR9", "arr:Kutaisi", e.At(3 * NM, 0), true, 75, 300) };
            Run();
            var s0 = Said();
            Ai = new() { Ai[0] };
            Run();
            if (s0.Contains("cleared for takeoff") || aiStage["DEP2"] != "ready" || !Said().Contains("cleared for takeoff"))
                throw new Exception($"KI-Startfreigabe bei KI im Endanflug: \"{s0}\" / {aiStage["DEP2"]}");
            Console.WriteLine("OK   KI-Startfreigabe wartet auf KI mit Landefreigabe im Endanflug");


            // A82/A83: approach with departing AI on the runway -> "continue approach, traffic departing", stays until clear, then landing clearance
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-300, 0), false, 40), Plane("ARR1", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
            aiStage["ARR1"] = "inbound";
            Run();
            var s2 = Said();
            Run();
            var s3 = Said();
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-1500, 0), true, 80, 60), Ai[1] };
            Run();
            var s4 = Said();
            if (!s2.Contains("continue approach, traffic departing runway") || s3 != "" || aiStage["ARR1"] != "final" || !s4.Contains("cleared to land") || s4.Contains("continue"))
                throw new Exception($"KI-Landefreigabe: \"{s2}\" / \"{s3}\" / {aiStage["ARR1"]} \"{s4}\"");
            Console.WriteLine("OK   KI-Anflug: \"continue approach, traffic departing\", Landefreigabe erst nach dem Abheben");

            // other AI rolls out on the runway above 3 m/s (not taking off): likewise; landed without clearance still counts as landed
            Ai = new() { Plane("ARR0", "arr:Kutaisi", e.At(-800, 0), false, 20), Plane("ARR2", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
            aiStage["ARR2"] = "inbound";
            Run();
            var s5 = Said();
            Ai = new() { Plane("ARR2", "arr:Kutaisi", e.At(-500, 0), false, 10) };
            Run();
            if (!s5.Contains("continue approach, traffic on runway") || aiStage["ARR2"] != "landed")
                throw new Exception($"KI-Anflug, Bahn belegt: \"{s5}\" / {aiStage["ARR2"]}");
            Console.WriteLine("OK   KI-Anflug: andere KI rollt auf der Bahn -> warten; Landung ohne Freigabe -> gelandet");

            // R301: AI on final is handed from Approach to Tower ("contact Kutaisi Tower …", readback), only then it calls the Tower; Approach no longer says "report final"
            aiStage.Clear(); aiSent.Clear(); Ai = new() { Plane("ARR9", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
            aiStage["ARR9"] = "inbound"; outq.Clear(); Run();
            var hand = string.Join(" | ", outq.Where(t => t.Role == "Approach").Select(t => t.Text)); var seq = string.Join(" | ", outq.Select(t => t.Role));
            if (!Regex.IsMatch(hand, @"contact Kutaisi Tower two six five decimal zero\..*\| Tower two six five decimal zero, ") || hand.Contains("report final") || !seq.StartsWith("Approach") || !seq.Contains("Tower"))
                throw new Exception($"KI-Übergabe an den Tower: \"{hand}\" / {seq}");
            Console.WriteLine($"OK   KI-Anflug: Übergabe an den Tower mit Frequenz und Rücklesung (\"{hand}\")");

            // R338: player with landing clearance behind the AI (8 NM) does not block its landing; ahead of it (2 NM): "number two, follow …" instead of "traffic on runway"
            var tel338 = pl.Tel; pl.Type = "FA-18C_hornet";
            var r338 = new List<string>();
            foreach (var d338 in new[] { 8.0, 2.0 })
            {
                Player(Phase.ClearedLand, rw);
                var (qx, qz) = e.At(d338 * NM, 0);
                pl.Tel = new Telemetry(fld.Elev + 600, 600, 70, e.Hdg * Math.PI / 180, qx, qz, 0, 0, 0);
                aiStage.Clear(); aiSent.Clear(); Ai = new() { Plane("ARR9", "arr:Kutaisi", e.At(4 * NM, 0), true, 75, 400) };
                aiStage["ARR9"] = "inbound"; outq.Clear(); Run();
                r338.Add(Said());
            }
            pl.Tel = tel338;
            if (!r338[0].Contains("cleared to land") || !r338[1].Contains("number two, follow the Hornet on 2 mile final, continue"))
                throw new Exception($"R338 KI-Landefreigabe bei Spieler-Freigabe: {string.Join(" / ", r338)}");
            Console.WriteLine($"OK   R338 KI-Landefreigabe: Spieler dahinter -> frei, davor -> number two ({string.Join(" / ", r338)})");

            // A41: player taxis to the opposite runway (TaxiOut): the AI does not take off, on the active runway it does
            aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear(); cmds.Clear();
            Ai = new() { Plane("DEP3", "dep:Kutaisi", e.At(2000, 400), false, 0) };
            Player(Phase.TaxiOut, opp);
            Run();
            int blocked = cmds.Count;
            Player(Phase.TaxiOut, rw);
            Run();
            if (blocked != 0 || !cmds.Contains("START;DEP3")) throw new Exception($"KI-Abflug, Spieler auf der Gegenbahn: {blocked} Kommandos, danach {string.Join(",", cmds)}");
            Console.WriteLine("OK   KI-Abflug wartet auf Spieler beim Abflug auf der Gegenbahn");

            // A9: one runway, one clearance: two players report "ready" shortly after each other -> only one "cleared for takeoff", the second only after the first lifts off
            {
                Ai = new();
                var hs = e.At(0, 150);
                var tA = new Tower(fld, "Enfield 1-1"); var tB = new Tower(fld, "Colt 1-1");
                var pA = new Pilot { Unit = "PA", Callsign = "Enfield 1-1", Tel = new Telemetry(fld.Elev, 0, 0, e.Hdg * Math.PI / 180, hs.X, hs.Z, 0, 0, 0), Active = tA };
                var pB = new Pilot { Unit = "PB", Callsign = "Colt 1-1", Tel = pA.Tel with { X = hs.X + 30 }, Active = tB };
                foreach (var pp in new[] { pA, pB })
                    pp.Active!.Load(new Tower.State(Phase.TaxiOut, rw, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, false, false, null, null, 0, false), pp.Tel!, 0);
                var both = new List<Pilot> { pA, pB };
                var none = Array.Empty<Traffic>();
                Prep(pA, tA, both); var rA = tA.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", pA.Tel, none, 1);
                Prep(pB, tB, both); var rB = tB.OnTranscript("Kutaisi Tower, Colt 1-1, ready for departure", pB.Tel, none, 1);
                Prep(pB, tB, both); var rB2 = tB.Tick(pB.Tel, none, 2);
                pA.Tel = pA.Tel! with { AltMsl = fld.Elev + 60, Agl = 60, Ias = 80 };   // first one lifts off
                Prep(pB, tB, both); var rB3 = tB.Tick(pB.Tel, none, 3);
                if (!rA[0].Text.Contains("cleared for takeoff") || !rB[0].Text.Contains("number 2 for departure") || rB2.Count != 0 || !(rB3.FirstOrDefault()?.Text ?? "").Contains("cleared for takeoff"))
                    throw new Exception($"A9 zwei Abflüge: \"{rA[0].Text}\" / \"{rB[0].Text}\" / {rB2.Count} / \"{rB3.FirstOrDefault()?.Text}\"");
                Console.WriteLine("OK   A9 zwei Spieler am Rollhalt: nur einer \"cleared for takeoff\", der zweite \"number 2 for departure\" bis zum Abheben");

                // AI: player waits at the holding point (hold short) -> AI takeoff clearance; waiting on the runway (LUAW) or on final 3 NM -> not
                string Ready(Action set)
                {
                    aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
                    aiStage["DEP5"] = "taxi";
                    Ai = new() { Plane("DEP5", "dep-go:Kutaisi", e.At(500, 150), false, 6) };
                    set();
                    Run(); Said();
                    return aiStage["DEP5"];
                }
                var tel0 = pl.Tel;
                var k1 = Ready(() => Player(Phase.HoldShort, rw));
                var k2 = Ready(() => { Player(Phase.TaxiOut, rw); pl.Active!.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", pl.Tel, new[] { Plane("X1", "", e.At(-300, 0), false, 30) }, 1); });
                bool lu = pl.Active!.LineUp;
                var f3 = e.At(3 * NM, 0);
                pl.Tel = new Telemetry(fld.Elev + 300, 300, 70, e.Hdg * Math.PI / 180, f3.X, f3.Z, 0, 0, 0);
                var k3 = Ready(() => Player(Phase.Pattern, rw));
                pl.Tel = tel0;
                if (k1 != "ready" || !lu || k2 != "taxi" || k3 != "taxi")
                    throw new Exception($"A9/A10 KI-Startfreigabe: hold short {k1}, LUAW {lu}/{k2}, Endanflug {k3}");
                Console.WriteLine("OK   A9/A10 KI-Startfreigabe: Spieler am Rollhalt hält nicht, auf der Bahn wartend und im Endanflug 3 NM schon");
            }

            // Golf (mptest Al Ain/Bandar Abbas): DCS AI taxis blindly after START into the takeoff roll (parking spot 1800 m away: ~245 s); held AI 14 NM out (~220 s) stays
            // in holding instead of landing right into the takeoff roll, once the departure is airborne, RESUME; a landing long before the takeoff roll does not wait
            aiStage.Clear(); aiSent.Clear(); outq.Clear();
            aiStage["DEP3"] = "taxi"; aiStage["ARR8"] = "inbound";
            ArrQueue[fld.Name] = new() { "ai:ARR8" };
            Holds["ai:ARR8"] = (e.At(14 * NM, 0).X, e.At(14 * NM, 0).Z, 3000);
            Ai = new() { Plane("DEP3", "dep-go:Kutaisi", e.At(-1700, 600), false, 8), Plane("ARR8", "arr-hold:Kutaisi", e.At(14 * NM, 0), true, 118, 900) };
            Run();
            var held = cmds.Contains("RESUME;ARR8");
            Ai = new() { Plane("DEP3", "dep-go:Kutaisi", e.At(-1700, 600), false, 8), Plane("ARR8", "arr-hold:Kutaisi", e.At(30 * NM, 0), true, 118, 900) };   // far out: lands long after takeoff
            Run();
            var far = cmds.Contains("RESUME;ARR8");
            ArrQueue.Remove(fld.Name); Holds.Remove("ai:ARR8");
            if (held || !far) throw new Exception($"KI-Anflug ins Startfenster eines rollenden KI-Abflugs: gehalten {!held}, 30 NM frei {far} / {string.Join(",", cmds)}");
            Console.WriteLine("OK   KI-Anflug bleibt in der Warteschleife, solange er in den Startlauf eines rollenden KI-Abflugs fiele (Stand weit von der Schwelle), weit draußen RESUME");

            // R298: AI fuel under 0.20: "minimum fuel" (advisory, no priority); under 0.10: MAYDAY FUEL (priority in UpdateQueues)
            aiStage.Clear(); aiSent.Clear(); outq.Clear(); fuelTold.Clear();
            Ai = new() { Plane("ARR7", "arr:Kutaisi", e.At(20 * NM, 0), true, 120, 1500) };
            UpdateQueues(new() { pl });
            AiFuel["ARR7"] = 0.18; Run();
            var mf = string.Join(" | ", outq.Where(t => t.Role == "Approach" && !t.Pilot).Select(t => t.Text)); outq.Clear();
            AiFuel["ARR7"] = 0.08; Run();
            var mm = string.Join(" | ", outq.Where(t => t.Role == "Approach" && !t.Pilot).Select(t => t.Text)); outq.Clear();
            AiFuel.Remove("ARR7"); fuelTold.Clear(); ArrQueue.Clear();
            if (!mf.Contains("roger minimum fuel, you are number 1") || mf.Contains("mayday") || !mm.Contains("roger mayday, you are number 1") || mm.Contains("minimum fuel"))
                throw new Exception($"KI-Sprit R298: \"{mf}\" / \"{mm}\"");
            Console.WriteLine($"OK   R298 KI-Sprit: \"{mf}\" (kein Vorrang), dann \"{mm}\"");

            // Carrier into the wind: mission does not turn (land ahead) -> Marshal states the BRC being steered, after 5 min new CVTURN; leg finished (ship stationary) likewise
            cmds.Clear();
            var cv = new Carrier.Boat("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 127.5, "73X", "11", Wx: 10);   // heading north, wind from south -> BRC 189.1
            var (cvBrc, cvKt) = cv.IntoWind;
            Ai = new() { new Traffic(77, "FA-18C_hornet", 5 * NM, 0, 1000, 0, 150, "CV1", "cv", true, 2) };
            int Turns() => cmds.Count(c => c.StartsWith("CVTURN;CVN;"));
            Carrier.Boats = new() { cv }; SteerBoats();
            Carrier.Boats = new() { cv with { NoTurn = true } }; SimSec += 120; SteerBoats();
            int t1 = Turns(); double exp = Carrier.Boats[0].ExpBrc;
            SimSec += 200; SteerBoats();
            int t2 = Turns();
            Carrier.Boats = new() { cv with { Hdg = cvBrc * Math.PI / 180, Speed = 0 } }; SimSec += 310; SteerBoats();
            int t3 = Turns();
            Carrier.Boats = new() { cv with { Hdg = cvBrc * Math.PI / 180, Speed = cvKt * 0.514444 } }; SimSec += 310; SteerBoats();
            int t4 = Turns();
            Ai = new(); SimSec += 200; SteerBoats();
            if (t1 != 1 || exp != 0 || t2 != 2 || t3 != 3 || t4 != 3 || !cmds.Contains("CVRESUME;CVN"))
                throw new Exception($"Träger in den Wind, neu drehen: {t1}/{t2}/{t3}/{t4}, ExpBrc {exp}, {string.Join(",", cmds)}");
            Console.WriteLine("OK   Träger nicht im Wind (Land voraus, Schenkel zu Ende) -> nach 5 min neues CVTURN, Marshal-BRC wie gefahren");
        }
        finally
        {
            (Fields, Wx, SimSec, stateTime, SimLog, AiHook) = (fs, ws, ss, sst, sl, null);
            Ai = new(); aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
            Carrier.Boats = new(); boatInWind.Clear(); boatTurnAt.Clear();
        }
    }

    // ------------------------------------------------------------ AI flights in combat (Flights, KI-FUNK-PLAN)
    static Flights.World FlightWorld() => new(
        ActivePilots().Where(p => p.Tel != null).Select(p => (p.Coalition, p.Tel!.X, p.Tel.Z, p.Gid, p.Unit, p.Callsign)).ToList(),
        (side, x, z) =>
        {
            var me = new Ops.Me("", new Telemetry(0, 0, 0, 0, x, z, 0, 0, 0), "", side);
            var a = Ops.AwacsOf(Ai, me);
            if (a == null && !Ops.Gci.Contains(side)) return null;   // no AWACS, no ground radar: nobody acknowledges
            return (Ops.AwacsName(a, me), Ops.AwacsFreqOf(a) is > 0 and var f ? f : Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5));
        },
        Cfg.Frequencies.GetValueOrDefault("AWACS", 251.5));

    static void FlightComms()
    {
        if (!MissionData || Cfg.AiFlightComms == "aus") return;
        List<Flights.Call> calls;
        lock (TowerLock) calls = Flights.Tick(Now(), FlightWorld());
        foreach (var c in calls)
        {
            var fs = c.Freq2 > 0 ? new[] { c.Freq, c.Freq2 } : new[] { c.Freq };
            SayQueue.Add(c.Who == "" ? new Tx(c.Text, "AWACS", c.Station, 0, Freqs: fs, Prio: c.Prio, MaxAge: c.MaxAge, Side: c.Side)
                                     : new Tx(c.Text, "Flight", c.Station, 0, true, AiVoiceFor(c.Who), Freqs: fs,
                                              Prio: c.Prio, MaxAge: c.MaxAge, Stress: c.Stress, Side: c.Side));
        }
    }

    /// One fixed voice per AI flight member (without pitch shift, which sounds artificial): all available Piper models except ljspeech and alan (AWACS/carrier, A142).
    static string[]? aiVoices;
    static string AiVoiceFor(string unit)
    {
        aiVoices ??= new[] { "en_US-joe-medium", "en_US-bryce-medium", "en_US-ryan-medium", "en_US-kristin-medium", "en_US-amy-medium", "en_US-lessac-medium", "en_US-hfc_female-medium" }
                     .Where(m => File.Exists(Path.Combine(Root, "models", m + ".onnx"))).DefaultIfEmpty(Cfg.PilotVoice).ToArray();
        return aiVoices[(unit.Sum(ch => ch) * 7 + unit.Length) % aiVoices.Length];
    }

    static void AiCmd(string line, bool log = true)   // log: false for the VR wheel text (every wheel movement)
    {
        if (log) Log($"[KI ] {line}");
        if (AiHook != null) { AiHook(line); return; }
        try
        {
            var f = Path.Combine(MenuDir, Clock.Ticks.ToString());
            File.WriteAllText(f + ".tmp", line);
            File.Move(f + ".tmp", f + ".cmd");
        }
        catch (IOException e) { Trace("FEHLER", $"Befehl an die Mission {line}: {e.Message}"); }
    }

    // ------------------------------------------------------------ ATIS per airfield (weather from the mission)
    static readonly Dictionary<string, AtisInfo> Atis = new();

    /// Rebuild text; if runway, wind, QNH, weather or the full hour of the mission clock change, there is a new identifier.
    /// true = new identifier (not at the first text of an airfield).
    static bool UpdateAtis(Airfield f)
    {
        if (!Wx.TryGetValue(f.Name, out var w)) return false;
        if (!Atis.TryGetValue(f.Name, out var a)) Atis[f.Name] = a = new AtisInfo { Letter = (char)('A' + Clock.Minute % 26) };
        var tw = new Tower(f, "");
        var tel = new Telemetry(f.Elev + 2, 0, 0, 0, f.X, f.Z, w.Wx, w.Wz, w.Pa);
        double clk = Carrier.Clock;
        var key = tw.AtisText("A", tel, w.TempC, Sky.Clouds, Sky.BaseM, 0, clk < 0 ? clk : clk - clk % 3600)   // Key only with the full hour, otherwise the identifier would change every minute
                + new[] { 800, 1500, 3000, 5000, 9500 }.Count(b => Sky.VisM >= b)
                + (int)(clk / 3600);   // full hour also with unknown map (R125, then without time in the text); visibility only in steps like SPECI, otherwise the identifier would change every kilometer as fog rolls in
        if (key == a.Key) return false;
        bool neu = a.Key != "";
        if (neu) a.Letter = (char)('A' + (a.Letter - 'A' + 1) % 26);
        a.Key = key;
        a.Text = tw.AtisText(a.Letter.ToString(), tel, w.TempC, Sky.Clouds, Sky.BaseM, Sky.VisM, clk);
        a.Call = tw.AtisCall(a.Letter.ToString(), tel);
        a.Mp3 = "";
        Log($"[ATIS] {a.Text}");
        return neu;
    }

    /// Announcement of the new identifier on the airfield frequency to everyone listening: players at the airfield, on the ground or under 20 NM. Empty = nobody there or ATIS off.
    /// A133: with the side of the airfield, at a neutral airfield once per side of the listeners.
    static List<Tx> AtisCall(Airfield f, List<Pilot> pilots) =>
        !Cfg.Atis || !Atis.TryGetValue(f.Name, out var a) ? new()
        : pilots.Where(p => p.Active?.F == f && p.Tel is { } t && (t.Agl < 5 || Dist(t.X, t.Z, f.X, f.Z) < 20 * NM)).Select(p => SideOf(f, p)).Distinct()
                .Select(s => new Tx(a.Call, "Tower", f.Station, 0, Freqs: FieldFreqs(f, "Tower"), Prio: 2, Side: s)).ToList();

    /// Self-test N27: new identifier only on changed ATIS or at the full hour (mission clock), time in Zulu, announcement only to players at the airfield.
    static void AtisTest()
    {
        var (wx0, sky0, clk0, cfg0, tel0) = (Wx, Sky, Carrier.Clock, Cfg.Atis, Atis.ToDictionary(x => x.Key, x => x.Value));
        var fld = Airfield.Kutaisi();
        try
        {
            Atis.Clear();
            Cfg.Atis = true;
            Sky = (false, 0, 80000);
            double pa = 101325 * Math.Pow(1 - 2.25577e-5 * (fld.Elev + 2), 5.25588);   // Standard atmosphere at the airfield: QNH 1013
            Wx = new() { [fld.Name] = (-5, 0, 15, pa) };
            Carrier.Clock = 10 * 3600 + 20 * 60;   // 10:20 local time = 06:20 Zulu
            bool first = UpdateAtis(fld);
            var a = Atis[fld.Name];
            char l0 = a.Letter;
            if (first || !a.Text.Contains("information " + Tower.Phonetic(l0.ToString()) + ", time zero six two zero Zulu.")) throw new Exception($"ATIS erste Kennung {first}: {a.Text}");
            Carrier.Clock = 10 * 3600 + 50 * 60;
            if (UpdateAtis(fld) || a.Letter != l0) throw new Exception("ATIS: gleiche Stunde, gleiches Wetter = keine neue Kennung");
            Wx[fld.Name] = (-9, 4, 15, pa);   // Wind veers and increases
            bool wind = UpdateAtis(fld);
            char l1 = a.Letter;
            if (!wind || l1 != (char)('A' + (l0 - 'A' + 1) % 26) || !a.Text.Contains("time zero six five zero Zulu") || UpdateAtis(fld))
                throw new Exception($"ATIS Windänderung: neu={wind} {l0}->{l1}, danach nichts mehr: {a.Text}");
            if (!a.Call.StartsWith("Attention all aircraft, Kutaisi information " + Tower.Phonetic(l1.ToString()) + " now current, QNH one zero one three")) throw new Exception("ATIS Ansage: " + a.Call);
            Carrier.Clock = 11 * 3600 + 5;   // full hour: new identifier without weather change
            bool hour = UpdateAtis(fld);
            if (!hour || a.Letter != (char)('A' + (l1 - 'A' + 1) % 26) || !a.Text.Contains("time zero seven zero zero Zulu")) throw new Exception($"ATIS volle Stunde: neu={hour} {a.Text}");
            Carrier.Clock = 11 * 3600 + 59 * 60;
            if (UpdateAtis(fld)) throw new Exception("ATIS: Minuten ändern die Kennung nicht");
            Sky = (false, 0, 2000);   // Fog rolls in: new identifier only when crossing a visibility step (800/1500/3000/5000/9500 m)
            bool fog = UpdateAtis(fld);
            Sky = (false, 0, 2900);
            bool step = UpdateAtis(fld);
            Sky = (false, 0, 3100);
            if (!fog || step || !UpdateAtis(fld) || !a.Text.Contains("Visibility three kilometers")) throw new Exception($"ATIS Sichtstufen: {fog} {step} {a.Text}");
            // Announcement only to players of this airfield, on the ground or under 20 NM
            Pilot P(double agl, double nm) => new() { Active = new Tower(fld, "x"), Tel = new Telemetry(fld.Elev + agl, agl, 50, 0, fld.X + nm * NM, fld.Z, 0, 0, 0) };
            var (ground, near, far) = (P(0, 0), P(500, 15), P(3000, 30));
            var (other, stray) = (new Pilot { Active = new Tower(Airfield.Kutaisi(), "x"), Tel = near.Tel }, new Pilot { Tel = near.Tel });   // other airfield (own instance), without flow
            if (AtisCall(fld, new() { far, other, stray }).Count != 0) throw new Exception("ATIS Ansage: 30 NM weit weg, anderer Platz, ohne Ablauf = niemand");
            var tx = AtisCall(fld, new() { far, ground }).SingleOrDefault();
            var red = P(500, 15); red.Coalition = 1;   // A133: neutral airfield -> once per side
            var sides = string.Join(",", AtisCall(fld, new() { ground, near, red }).Select(x => x.Side));
            if (tx == null || AtisCall(fld, new() { near }).Count != 1 || tx.Text != a.Call || tx.Gid != 0 || tx.Station != "Kutaisi Tower" || tx.Side != 2 || sides != "2,1")
                throw new Exception($"ATIS Ansage: Spieler am Platz hört sie (Seiten {sides})");
            Cfg.Atis = false;
            if (AtisCall(fld, new() { ground }).Count != 0) throw new Exception("ATIS Ansage: mit Atis=false keine");
            // A35: own ATIS frequency per airfield, collision with airfield and function frequencies
            var atis0 = Cfg.AtisFreqs;
            try
            {
                Cfg.AtisFreqs = new() { ["Kutaisi"] = 262.0 };
                var kutF = new Airfield { Name = "Kutaisi", Uhf = 263.0, Vhf = 134.0 };
                var ac = AtisClashes(new[] { kutF }, new() { ["Kutaisi"] = 263.0, ["Senaki"] = 251.5, ["Batumi"] = 262.5 }, new Config().Frequencies).ToList();
                if (AtisFreq("Kutaisi") != 262.0 || AtisFreq("Senaki") != Cfg.Frequencies["ATIS"] || ac.Count != 2 || ac[0] != "ATIS Kutaisi 263.0 = Kutaisi UHF" || ac[1] != "ATIS Senaki 251.5 = AWACS")
                    throw new Exception($"ATIS-Frequenz: {AtisFreq("Kutaisi")} / {AtisFreq("Senaki")} / {string.Join(" | ", ac)}");
                if (FieldFreqs(kutF, "ATIS") is not [262.0] || FieldFreqs(new Airfield { Name = "Senaki", Uhf = 261.0 }, "ATIS") is not [var sf] || sf != Cfg.Frequencies["ATIS"])   // Request (F10/radio wheel) on the same frequency as the cycle
                    throw new Exception("ATIS-Abruf: " + string.Join(",", FieldFreqs(kutF, "ATIS") ?? Array.Empty<double>()));
            }
            finally { Cfg.AtisFreqs = atis0; }
            // R120: mission run time jumps back (new mission/restart): identifiers gone; afterwards exactly one first identifier without announcement. ESC pause (run time stands, wall clock runs) clears nothing.
            var (st0, ms0) = (stateTime, missionSec);
            try
            {
                missionSec = -1; MissionGap(500); stateTime = Clock - TimeSpan.FromSeconds(60); MissionGap(500.5);
                if (Atis.Count != 1) throw new Exception("ATIS R120: 60 s Pause darf die Kennung nicht löschen");
                MissionGap(2);
                if (Atis.Count != 0) throw new Exception("ATIS R120: Missionslaufzeit zurück (neue Mission) muss die Kennungen löschen");
                Wx[fld.Name] = (3, 3, 10, pa);
                if (UpdateAtis(fld) || Atis[fld.Name].Text == "") throw new Exception("ATIS R120: neue Mission = erste Kennung ohne Ansage");
            }
            finally { (stateTime, missionSec) = (st0, ms0); }
            // R125: Zulu per map (Nevada UTC-8, Afghanistan UTC+4:30), unknown map without time
            var th0 = Airfield.Theatre;
            try
            {
                var tel = new Telemetry(fld.Elev + 2, 0, 0, 0, fld.X, fld.Z, 0, 0, pa);
                string Z(string th, double clk) { Airfield.Theatre = th; return new Tower(fld, "").AtisText("A", tel, 15, false, 0, 80000, clk); }
                if (!Z("Nevada", 10 * 3600 + 20 * 60).Contains("time one eight two zero Zulu") || !Z("Afghanistan", 10 * 3600).Contains("time zero five three zero Zulu")
                    || !Z("Caucasus", 2 * 3600).Contains("time two two zero zero Zulu") || Z("Atlantis", 36000).Contains("Zulu"))
                    throw new Exception("ATIS R125 Zulu je Karte: " + Z("Nevada", 10 * 3600 + 20 * 60));
            }
            finally { Airfield.Theatre = th0; }
            Console.WriteLine($"OK   ATIS: Windänderung = genau eine Ansage und Kennung {l0}->{l1}, volle Stunde -> {a.Letter} mit Zulu-Zeit, Minuten ändern nichts, Ansage nur an Spieler am Platz");
        }
        finally { (Wx, Sky, Carrier.Clock, Cfg.Atis) = (wx0, sky0, clk0, cfg0); Atis.Clear(); foreach (var x in tel0) Atis[x.Key] = x.Value; }
    }

    /// Alternately transmits on an ATIS frequency the ATIS of airfields where a player is closer than 40 NM (A35: one cycle per frequency, parallel).
    /// N1/A133: only for players for whom the airfield is own or neutral, with the side of the airfield (neutral: once per side of these players).
    /// A35: one own transmitter per ATIS frequency (shared, AtisFreqs, AirfieldFrequencies); ReloadLive starts newly added ones, orphaned ones transmit nothing.
    static readonly HashSet<double> atisOn = new();
    static void StartAtis()
    {
        List<double> fs;
        lock (TowerLock) fs = Cfg.AtisFreqs.Values.Append(Cfg.Frequencies["ATIS"]).Concat(Fields.SelectMany(f => f.Own.GetValueOrDefault("ATIS") ?? Array.Empty<double>())).Where(atisOn.Add).ToList();   // LoadFields/ReloadLive: never twice
        foreach (var af in fs) new Thread(() => AtisLoop(af)) { IsBackground = true }.Start();
    }

    static void AtisLoop(double freq)
    {
        int i = 0;
        while (true)
        {
            Thread.Sleep(3000);
            if (!Cfg.Atis) continue;
            string name;
            int side;
            AtisInfo? a;
            lock (TowerLock)
            {
                var near = Fields.Where(f => Atis.ContainsKey(f.Name) && FieldFreqs(f, "ATIS")!.Any(x => Math.Abs(x - freq) < 0.0005))
                                 .SelectMany(f => ActivePilots().Where(p => p.Tel != null && Carrier.Deck(p.Tel, p.Coalition) == null && Served(p.Coalition, f) && Dist(p.Tel.X, p.Tel.Z, f.X, f.Z) < 40 * NM)
                                                                .Select(p => (f.Name, Side: SideOf(f, p))).Distinct()).ToList();
                if (near.Count == 0) continue;
                (name, side) = near[i++ % near.Count];
                a = Atis[name];
            }
            if (a.Mp3 == "")
            {
                var mp3 = Path.Combine(Root, "tmp", "atis-" + Regex.Replace(name, "[^A-Za-z]", "") + ".mp3");
                a.Sec = Synth(a.Text, Cfg.Voices.GetValueOrDefault("ATIS", "en_US-ryan-medium@0.94"), mp3);
                a.Mp3 = mp3;
            }
            var sw = Stopwatch.StartNew();
            Transmit(a.Mp3, freq, name + "ATIS", side: side);
            var rest = a.Sec - sw.Elapsed.TotalSeconds;
            if (rest > 0) Thread.Sleep(TimeSpan.FromSeconds(rest));
        }
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
        var wav = Path.Combine(Root, "tmp", $"srs{Interlocked.Increment(ref wavNo) % 8}.wav");
        using var w = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1));
        w.WriteSamples(pcm, 0, pcm.Length);
        return wav;
    }

    // ------------------------------------------------------------ Speech recognition (whisper.cpp)
    /// Text and mean word confidence (0–1, Whisper token probabilities from -ojf).
    static (string Text, double Conf) Transcribe(string wav, Pilot? p = null, bool keep = true)
    {
        string stations, cs;
        lock (TowerLock)
        {
            var h = p ?? Host();   // A141: callsign and airfield of the sender (SRS), otherwise of the host
            var f = (h?.Lead ?? h)?.Active?.F ?? Fields[0];
            stations = $"{f.StationOf("Ground")}, {f.StationOf("Tower")}, {f.StationOf("Approach")}. " +
                       string.Join(", ", Fields.Where(x => x != f).Select(x => x.Name.Replace('-', ' ') + (x.Name.Contains(x.Callsign) ? "" : $" ({x.Callsign})")));   // "Mineralnye Vody (Minvody)"
            cs = h?.Callsign ?? Cfg.Callsign;
        }
        var prompt = $"{stations}. {cs}, request startup. Request taxi, departure north. Ready for departure runway 25. " +
                     "Inbound for landing. CRP South. Initial 07. Overhead. Break. Downwind. Final runway 25, touch and go. " +
                     "Request ILS approach. Going around. Mayday. Runway vacated. Radio check. Information Alpha. " +
                     $"Wilco, {cs}. Cleared to land runway 25, {cs}. Cleared for takeoff. Hold short, line up and wait. Roger. " +
                     "Range Alpha, checking in, IP inbound, in hot, off safe. Overlord, request picture, bogey dope. Texaco, Shell, request rejoin, pre-contact.";
        var model = Path.Combine(Root, "models", "ggml-small.en.bin");   // small: significantly fewer mishearings than base, ~1.7 s instead of 0.5 s
        if (!File.Exists(model)) model = Path.Combine(Root, "models", "ggml-base.en.bin");
        var json = Path.ChangeExtension(wav, null);   // -of: <wav without extension>.json
        try { File.Delete(json + ".json"); } catch (IOException) { }   // do not read old confidence
        var sw = Stopwatch.StartNew();
        var (output, _) = Run(Path.Combine(Root, "tools", "whisper", "Release", "whisper-cli.exe"), null,
            "-m", model, "-t", Math.Min(8, Environment.ProcessorCount).ToString(), "-f", wav, "-nt", "-np", "-l", "en", "--prompt", prompt, "-ojf", "-of", json);
        double conf = 1;
        try { conf = Confidence(File.ReadAllText(json + ".json")); File.Delete(json + ".json"); } catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { Trace("FEHLER", $"Whisper-Sicherheit: {e.GetType().Name}: {e.Message}"); }
        Trace("WHISPER", $"{Path.GetFileName(wav)} {Path.GetFileName(model)} {sw.ElapsedMilliseconds} ms conf {conf:0.00} (Sender {cs}): {output.Trim()}");
        if (keep) Keep(wav, output.Trim());
        return (output.Trim(), conf);
    }

    /// ASR corpus (V3): every radio call as WAV + recognized text in debrief\asr, the newest 400.
    /// Correct the text in the .txt = reference for --asr-test; moved to debrief\asr\fest\ it stays forever.
    static void Keep(string wav, string text)
    {
        if (Tower.Normalize(text).Length < 2) return;   // [BLANK_AUDIO], noise
        try
        {
            var dir = Path.Combine(Root, "debrief", "asr");
            Directory.CreateDirectory(dir);
            var b = Path.Combine(dir, $"{Clock:yyyyMMdd_HHmmss_fff}");
            File.Copy(wav, b + ".wav", true);
            File.WriteAllText(b + ".txt", text);
            foreach (var old in Directory.GetFiles(dir, "*.wav").OrderByDescending(f => f).Skip(400)) { File.Delete(old); File.Delete(Path.ChangeExtension(old, ".txt")); }
        }
        catch (IOException) { }
    }

    /// --asr-test [Ordner]: re-recognize the corpus, word errors against the .txt (normalized: "6,000" = "6000", "one" = "1").
    static int AsrTest(string dir)
    {
        if (!Directory.Exists(dir)) { Console.WriteLine($"Kein Korpus: {dir}"); return 1; }
        LoadFields();
        var files = Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories).Where(f => File.Exists(Path.ChangeExtension(f, ".txt"))).Order().ToList();
        int errs = 0, words = 0; double conf = 0; var sw = Stopwatch.StartNew();
        string[] W(string s) => Tower.Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var f in files)
        {
            var want = W(File.ReadAllText(Path.ChangeExtension(f, ".txt")));
            var (text, c) = Transcribe(f, keep: false);
            var got = W(text);
            int e = Tower.Lev(want, got);
            errs += e; words += want.Length; conf += c;
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(f)}  {e}/{want.Length}  ({c:0.00})  {text}" + (e > 0 ? $"   soll: {string.Join(' ', want)}" : ""));
        }
        int n = Math.Max(1, files.Count);
        Console.WriteLine($"{files.Count} Sprüche, Wortfehler {100.0 * errs / Math.Max(1, words):0.0} %, Sicherheit {conf / n:0.00}, {sw.Elapsed.TotalSeconds / n:0.0} s je Spruch");
        return 0;
    }

    /// Mean of word confidence: word = tokens up to the next space, counts with its weakest token; [_BEG_] etc. and punctuation do not.
    internal static double Confidence(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var words = new List<double>();
        foreach (var seg in doc.RootElement.GetProperty("transcription").EnumerateArray())
            foreach (var tok in seg.GetProperty("tokens").EnumerateArray())
            {
                var s = tok.GetProperty("text").GetString() ?? "";
                if (s.TrimStart().StartsWith('[') || !s.Any(char.IsLetterOrDigit)) continue;
                double p = tok.GetProperty("p").GetDouble();
                if (s.StartsWith(' ') || words.Count == 0) words.Add(p); else words[^1] = Math.Min(words[^1], p);
            }
        return words.Count == 0 ? 1 : words.Average();
    }

    // ------------------------------------------------------------ Speech output (Piper -> ffmpeg -> SRS)
    /// One transmitter per frequency: different controllers speak simultaneously, on one frequency one after another –
    /// LK16: by rank (RankOf): emergency/defending before flights' combat brevity before AWACS threat before commit/answers before picture before AI chatter, within the same rank in order.
    record Item(string Mp3, string Name, Action Show, DateTime At, double Freq2, int Prio, double Sec, double MaxAge, int Side = 0, int Rank = 3, Func<double>? Redo = null);   // Rank: order on the frequency (LK16, RankOf)
    sealed class Chan { public readonly List<Item> Q = new(); public DateTime FreeAt; }
    static bool Lso(Item it) => it.Prio == 0 && it.Name == "Paddles";   // LSO call to a player (A19), not AI radio
    /// LD3: after fox/defending (AI brevity rank 1 or player) 5 s urgent only (rank ≤ 2) – picture, answers and chatter wait. ponytail: global, not per frequency
    static DateTime HotUntil;
    static bool Hot(IEnumerable<Item> q) => Clock < HotUntil && !q.Any(x => x.Rank <= 2);
    static readonly ConcurrentDictionary<double, Chan> Channels = new();

    /// show: text in the game only when transmitting, so text and sound match. maxAge: after that text only (backlog).
    static void Play(string mp3, double freq, string name, Action show, double freq2 = 0, int prio = 1, double sec = 0, double maxAge = 45, int side = 0, int rank = 3, Func<double>? redo = null)
    {
        var ch = Channels.GetOrAdd(freq, f =>
        {
            var c = new Chan();
            new Thread(() =>
            {
                var lastEnd = DateTime.MinValue;
                while (true)
                {
                    lock (c) while (c.Q.Count == 0) Monitor.Wait(c);
                    var gap = Cfg.ReplyPause - (Clock - lastEnd).TotalSeconds;   // Pause like real radio, not blow by blow
                    long until = Environment.TickCount64 + (long)(Math.Max(0, gap) * (0.8 + Random.Shared.NextDouble() * 0.5) * 1000);
                    lock (c) for (long ms; ((ms = until - Environment.TickCount64) > 0 || (ms = (long)(HotUntil - Clock).TotalMilliseconds) > 0 && Hot(c.Q)) && !c.Q.Any(Lso);) Monitor.Wait(c, (int)Math.Min(ms, 500));   // A19: Paddles does not wait for a pause, not even in the middle of one
                    Item it;
                    lock (c) { it = c.Q.MinBy(x => x.Rank)!; c.Q.Remove(it); c.FreeAt = Clock.AddSeconds(it.Sec); }   // choose only after the pause: urgent calls still overtake
                    if (it.Redo?.Invoke() is double s2 && s2 > 0) { it = it with { Sec = s2, At = Clock }; lock (c) c.FreeAt = Clock.AddSeconds(s2); }   // LD4: BRAA changed -> spoken with the current state
                    bool fresh = (Clock - it.At).TotalSeconds <= it.MaxAge;
                    if (fresh || !Lso(it)) it.Show();   // A19: outdated LSO call not even as text
                    Trace("SENDEN", FormattableString.Invariant($"{Path.GetFileName(it.Mp3)} {it.Name} {f:0.0##} prio {it.Prio} nach {(Clock - it.At).TotalSeconds:0.0} s in der Warteschlange, {it.Sec:0.0} s") + (fresh ? "" : $" -> verworfen, älter als {it.MaxAge:0} s" + (Lso(it) ? "" : " (nur Text)")));
                    if (fresh) { Transmit(it.Mp3, f, it.Name, it.Freq2, it.Side); lastEnd = Clock; if (it.Rank == 1) HotUntil = Clock.AddSeconds(5); }
                    lock (c) c.FreeAt = Clock;
                    try { File.Delete(it.Mp3); } catch (IOException) { }
                }
            }) { IsBackground = true }.Start();
            return c;
        });
        lock (ch) { ch.Q.Add(new Item(mp3, name, show, Clock, freq2, prio, sec, maxAge, side, rank, redo)); Monitor.Pulse(ch); }
    }

    /// Seconds still to be transmitted on the frequency before a normal reply: waiting calls (prio ≤ 1) with pauses.
    /// The running call and AI radio do not count (a single AI call over 5 s otherwise gave "stand by" every time).
    static double Backlog(double freq)
    {
        if (!Channels.TryGetValue(freq, out var c)) return 0;
        lock (c) return c.Q.Where(x => x.Prio <= 1).Sum(x => x.Sec + Cfg.ReplyPause);
    }

    /// From this much waiting time the controller first says "stand by".
    const double StandbySec = 8;
    static readonly Regex Urgent = new(@"\bgo around\b|traffic alert|\bthreat\b|\bmerged\b|cancel takeoff|stop immediately|\broger (?:mayday|pan pan)\b|emergency services|" +
                                       @"\bhold position\b|wheels down|low altitude|\btoo low\b|expedite vacating", RegexOptions.IgnoreCase);   // A24: emergency and safety warnings before everything else

    /// Who sees the radio call as text: only groups whose player has tuned the frequency in SRS
    /// (frequencies unknown = sees it). Intercom/info (freq ≤ 0) and without SRS: as before.
    static IEnumerable<int> ShowTo(int gid, double freq, double freq2)
    {
        if (freq <= 0 || Srs is not { } srs || freq == Flights.Guard) return new[] { gid };   // Guard is heard by everyone
        bool On(double m, double f) => f > 0 && Math.Abs(m - f) < 0.005;
        bool Tuned(Pilot p) => srs.TunedFreqs(p.Id) is not { } fq || fq.Any(m => On(m, freq) || On(m, freq2));
        Pilot[] ps;
        try { ps = Pilots.Values.ToArray(); } catch (InvalidOperationException) { return new[] { gid }; }
        if (gid > 0) { var g = ps.Where(p => p.Gid == gid).ToArray(); return g.Length == 0 || g.Any(Tuned) ? new[] { gid } : Array.Empty<int>(); }
        if (ps.Any(p => p.Gid == 0 && Tuned(p))) return new[] { 0 };   // Group unknown
        return ps.Where(p => p.Gid > 0 && Tuned(p)).Select(p => p.Gid).Distinct().ToArray();   // "to all" individually to each group on the frequency
    }

    static double FreqOf(Tx tx) => tx.Role == "Crew" ? -(double)tx.Unit   // < 0: intercom of this aircraft (transmit)
                                 : tx.Freqs is { Length: > 0 } fs ? fs[0]   // Airfield frequency (UHF, plus VHF)
                                 : Cfg.Frequencies.GetValueOrDefault(tx.Role, Cfg.Frequencies.GetValueOrDefault("Tower", 265.0));

    static void SpeakLoop()
    {
        int n = 0;
        foreach (var tx in SayQueue.GetConsumingEnumerable())
        {
            bool busy = tx.Standby != null && Backlog(FreqOf(tx.Standby)) > StandbySec;   // Controller still has other things to send before the reply: first "stand by", reply afterwards
            if (busy && !tx.Pilot) Say(tx.Standby!);   // Voice request (no own call before): "stand by" before the reply
            Say(tx);
            if (busy && tx.Pilot) Say(tx.Standby!);   // Menu/wheel: after the own call, before the reply
        }

        void Say(Tx tx)
        {
            Heard(tx);   // Question to a player: radio wheel Enter suggests the answer
            bool info = tx.Role == "Info";
            double freq = FreqOf(tx);
            var label = info ? "" : tx.Pilot || freq < 0 ? $"{tx.Station}: " : $"{tx.Station} ({freq:0.0}): ";
            if (!info && !tx.Pilot && tx.Gid > 0) label = ">>> " + label;   // to me (own group): DCS text knows no colors
            if (!tx.Pilot) Log(info ? $"[INF] {tx.Text}" : $"[{tx.Role[..3].ToUpperInvariant()}] {tx.Station}: {tx.Text}");
            double freq2 = tx.Freqs is { Length: > 1 } f2 ? f2[1] : 0;
            var text = tx.Text;   // LD4: may change until sending (live)
            void Show()   // Text for the mission (*.out in the game, to the player's group; Gid -1 = speech only)
            {
                if (tx.Gid < 0) return;
                var dur = ShowSec(tx, label);
                foreach (var gid in ShowTo(tx.Gid, info ? 0 : freq, freq2))
                    try
                    {
                        var f = Path.Combine(MenuDir, Clock.Ticks + "-" + gid);
                        File.WriteAllText(f + ".tmp", $"{gid}|{dur}|{label}{text}");
                        File.Move(f + ".tmp", f + ".out");
                    }
                    catch (IOException e) { Trace("FEHLER", $"Text an die Mission: {e.Message}"); }
            }
            if (info || freq == 0) { Show(); return; }   // Crew without SRS unit ID: text only
            if (tx.Role == "Flight") Log($"[KI-FUNK] {tx.Station} ({freq:0.0}): {tx.Text}");
            if ((tx.Prio == 2 || tx.Role == "Flight") && freq != Flights.Guard && !ShowTo(tx.Gid, freq, freq2).Any()) { Trace("TTS", $"{tx.Station} {freq:0.0##}: niemand gerastet, nicht gesprochen: {tx.Text}"); return; }   // nobody tuned: no Piper (KF14)
            var mp3 = Path.Combine(Root, "tmp", $"say{++n}.mp3");   // unambiguous: do not overwrite waiting calls
            double sec;
            var voice = tx.Voice ?? (tx.Pilot ? Cfg.PilotVoice : PickVoice(Cfg.Voices.GetValueOrDefault(tx.Role, "en_US-ryan-medium"), tx.Station));
            var sw = Stopwatch.StartNew();
            sec = Synth(tx.Text, voice, mp3, tx.Pilot, tx.Stress, PaceOf(tx));
            Trace("TTS", FormattableString.Invariant($"{Path.GetFileName(mp3)} {tx.Role} {tx.Station} {freq:0.0##}{(freq2 > 0 ? $"/{freq2:0.0##}" : "")} gid {tx.Gid} prio {PrioOf(tx)} Seite {tx.Side} Stimme {voice} Tempo {(tx.Pilot ? 1.0 : Cfg.SpeechRate) * (tx.Stress ? 0.85 : 1) * PaceOf(tx):0.00} {sec:0.0} s, Synth {sw.ElapsedMilliseconds} ms, Rückstau {Backlog(freq):0} s: {tx.Text}"));
            if (SayQueue.IsAddingCompleted) { Show(); Transmit(mp3, freq, tx.Station, freq2, tx.Side); }   // --say: wait until sent
            else Play(mp3, freq, tx.Station, Show, freq2, PrioOf(tx), sec, tx.MaxAge > 0 ? tx.MaxAge : tx.Prio == 2 || tx.Role == "AWACS" ? 20 : 45, tx.Side, RankOf(tx),   // AI radio, picture: becomes outdated quickly
                      tx.Live == null ? null : () => tx.Live() is { } t2 && t2 != text ? Synth(text = t2, voice, mp3, tx.Pilot, tx.Stress, PaceOf(tx)) : -1);   // LD4: re-spoken if the BRAA has changed
        }
    }

    static int PrioOf(Tx tx) => tx.Prio == 1 && (tx.Station == "Paddles" || Urgent.IsMatch(tx.Text)) ? 0 : tx.Prio;   // A19: LSO calls (not AI radio, prio 2) before everything else, without "stand by"

    /// LK16: rank on a frequency, only for the queue order (prio remains for stand by, backlog, LSO): 0 emergency, defending, LSO, safety warning;
    /// 1 flights' combat brevity (fox, pitbull, splash …: short, otherwise immediately outdated); 2 AWACS threat/merged/leaker/furball/pop-up; 3 commit/targeting, answers;
    /// 4 picture (picture, new group, faded, clean, on station, acknowledgements); 5 other AI radio. A long AWACS call in the queue does not hold up short brevity.
    static readonly Regex Brevity = new(@"\b(?:fox (?:one|two|three)|pitbull|splash|trashed|mad ?dog|timeout|engaged|winchester|bingo)\b", RegexOptions.IgnoreCase);
    static readonly Regex Tasking = new(@"\b(?:commit|recommit|committing|skip it|reset|resetting|maintain CAP|target|targeting|targeted|sorted)\b", RegexOptions.IgnoreCase);
    static readonly Regex Scope = new(@"\b(?:picture|new group|faded|combined|split|on station|copies)\b|, hostile\.$|, copy\.$", RegexOptions.IgnoreCase);
    internal static int RankOf(Tx tx)
    {
        if (tx.Role == "AWACS")
            return AwacsRow(tx.Text) is { Fresh: true } || tx.Text.Contains("pop-up group") ? 2 : Tasking.IsMatch(tx.Text) ? 3 : Scope.IsMatch(tx.Text) ? 4 : 3;
        int p = PrioOf(tx);
        if (p == 0) return 0;
        if (tx.Role == "Flight") return Brevity.IsMatch(tx.Text) ? 1 : p <= 1 || Tasking.IsMatch(tx.Text) ? 3 : 4;
        return p <= 1 ? 3 : 5;
    }

    /// Display duration of the text in the game (seconds, part of the .out line "gid|sek|text"): other AI radio is heard, not read (6–8 s),
    /// otherwise reading time at least 20 s, longer for long texts.
    internal static int ShowSec(Tx tx, string label) =>
        (int)Math.Ceiling(tx.Prio == 2 || tx.Role == "Flight" ? Math.Clamp(tx.Text.Length / 8.0, 6, 8) : Math.Max(20, (label + tx.Text).Length / 8.0));

    static readonly string Ffmpeg = File.Exists(Path.Combine(Root, "tools", "ffmpeg", "ffmpeg.exe")) ? Path.Combine(Root, "tools", "ffmpeg", "ffmpeg.exe") : "ffmpeg";   // bundled, otherwise PATH

    /// Text -> mp3 with radio sound. voice = "model[@pitch]". Returns the duration in seconds.
    static readonly Dictionary<string, (string Mp3, double Sec)> SynthCache = new();   // Text|voice -> finished mp3
    /// "two four zero" -> "two-four-zero": Piper otherwise stresses each digit individually and speaks numbers slowly
    static readonly Regex DigitRun = new($@"\b(?:{string.Join("|", Tower.DigitWords)})(?: (?:{string.Join("|", Tower.DigitWords)})\b)+");
    internal static string ForPiper(string text) => DigitRun.Replace(text.Replace("Kutaisi", "Koo-tie-see"), m => m.Value.Replace(' ', '-'));   // Pronunciation

    /// Equalize the natural pace per Piper voice: the same radio call takes 10.0 s (ryan) to 14.1 s (bryce) at length_scale 1.
    /// Factor = 11.5 s / measured duration, so that SpeechRate sounds equally fast for every voice (0.85 -> about 9.8 s); unknown voice 1.
    internal static readonly Dictionary<string, double> VoicePace = new()
    {
        ["en_US-ryan-medium"] = 1.14, ["en_US-bryce-medium"] = 0.82, ["en_US-ljspeech-medium"] = 0.95, ["en_GB-alan-medium"] = 0.88, ["en_US-joe-medium"] = 1.06,
        ["en_US-amy-medium"] = 0.90, ["en_US-hfc_female-medium"] = 1.03, ["en_US-kristin-medium"] = 0.93, ["en_US-lessac-medium"] = 1.06,
    };

    /// LK2: the AWACS speaks tersely and briskly as in combat (length_scale × 0.93; 0.85 sounded too fast in game, smaller = faster), without the overdriven combat voice; target ≤ 5 s per follow-up report.
    internal static double PaceOf(Tx tx) => tx.Role == "AWACS" && !tx.Pilot && !tx.Stress ? 0.93 : 1;

    static double Synth(string text, string voice, string mp3, bool pilot = false, bool stress = false, double pace = 1)
    {
        var key = $"{text}|{voice}|{pilot}|{stress}|{pace}";
        lock (SynthCache)
            if (SynthCache.TryGetValue(key, out var hit) && File.Exists(hit.Mp3))
                try { File.Copy(hit.Mp3, mp3, true); return hit.Sec; } catch (IOException) { }
        var wav = Path.ChangeExtension(mp3, ".wav");
        var parts = voice.Split('@');
        double pitch = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
        string say = ForPiper(text), model = Path.Combine(Root, "models", parts[0] + ".onnx"),   // Pronunciation; digits bound: brisk as on the radio
               scale = ((pilot ? 0.95 : Cfg.SpeechRate) * VoicePace.GetValueOrDefault(parts[0], 1) * (stress ? 0.85 : 1) * pace).ToString("0.###", CultureInfo.InvariantCulture);   // Combat: faster
        if (!PiperWarm(model, scale, say, wav))
            Run(Path.Combine(Root, "tools", "piper", "piper.exe"), say, "--model", model, "--length_scale", scale, "--output_file", wav);
        double sec = 0;
        try { using var r = new WaveFileReader(wav); sec = r.TotalTime.TotalSeconds; } catch (Exception e) { Trace("FEHLER", $"Piper-WAV {voice}: {e.GetType().Name}: {e.Message}"); }
        // ponytail: Piper medium models deliver 22050 Hz; other rate -> pitch slightly different
        var pre = Math.Abs(pitch - 1) > 0.01 ? FormattableString.Invariant($"asetrate={22050 * pitch:0},atempo={1 / pitch:0.###},") : "";
        var graph = Cfg.RadioFx
            ? $"[0:a]{pre}aresample=48000," + (stress ? "volume=6dB,asoftclip=type=tanh,highpass=f=400,lowpass=f=2800," : "highpass=f=300,lowpass=f=3400,") +   // Combat: mask microphone, overdriven
              "acompressor=threshold=0.125:ratio=3:makeup=1.5," +
              "loudnorm=I=-22:TP=-7:LRA=7,aresample=48000[v];" +   // all voices equally loud

              "anoisesrc=r=48000:c=pink:a=0.008[n];[v][n]amix=inputs=2:duration=first:normalize=0[m];" +
              "anoisesrc=r=48000:c=pink:a=0.03:d=0.1,highpass=f=500,lowpass=f=3000,afade=t=out:d=0.1[sq];" +   // quiet squelch tail instead of a bang
              "[m][sq]concat=n=2:v=0:a=1,alimiter=limit=0.5:level=disabled[out]"                              // hard-limit peaks
            : $"[0:a]{pre}aresample=48000,alimiter=limit=0.5:level=disabled[out]";
        Run(Ffmpeg, null, "-y", "-loglevel", "error", "-i", wav, "-filter_complex", graph, "-map", "[out]", "-ac", "1",
            "-codec:a", "libmp3lame", "-q:a", "3", mp3);
        if (text.Length <= 30)   // remember short fixed phrases (LSO, "Roger ball."): no Piper/ffmpeg the next time
            try
            {
                lock (SynthCache)
                {
                    var keep = Path.Combine(Root, "tmp", $"cache{SynthCache.Count}.mp3");
                    File.Copy(mp3, keep, true);
                    SynthCache[key] = (keep, sec + 0.2);
                }
            }
            catch (IOException) { }
        return sec + 0.2;
    }

    /// Piper warm (V7): one process per voice and pace, model stays loaded (215 instead of 470 ms per call, ~100 MB per process,
    /// at most 4, the longest unused one goes). If one jams -> false, then individually as before.
    static readonly Dictionary<string, (Process P, DateTime Used)> Pipers = new();
    static bool PiperWarm(string model, string scale, string text, string wav)
    {
        var key = model + "|" + scale;
        lock (Pipers)   // ponytail: one lock for all voices (synth runs in the SpeakLoop, ATIS rarely), one lock per voice if needed
        {
            try
            {
                if (!Pipers.TryGetValue(key, out var e) || e.P.HasExited)
                {
                    if (Pipers.Count >= 4 && Pipers.MinBy(x => x.Value.Used) is var old) { Stop(old.Value.P); Pipers.Remove(old.Key); }
                    var psi = new ProcessStartInfo(Path.Combine(Root, "tools", "piper", "piper.exe"))
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                        RedirectStandardError = true, StandardInputEncoding = new System.Text.UTF8Encoding(false),
                    };
                    foreach (var a in new[] { "--model", model, "--length_scale", scale, "--json-input", "-q" }) psi.ArgumentList.Add(a);
                    e.P = Process.Start(psi)!;
                    e.P.ErrorDataReceived += (_, _) => { };
                    e.P.BeginErrorReadLine();
                }
                Pipers[key] = (e.P, Clock);
                e.P.StandardInput.WriteLine(JsonSerializer.Serialize(new { text, output_file = wav }));
                e.P.StandardInput.Flush();
                var done = e.P.StandardOutput.ReadLineAsync();   // Piper reports the finished file
                if (done.Wait(20000) && done.Result != null) return true;
                Stop(e.P);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { Trace("FEHLER", $"Piper warm {Path.GetFileName(model)}: {ex.GetType().Name}: {ex.Message}"); }
            Pipers.Remove(key);
            return false;
        }
        static void Stop(Process p) { try { p.Kill(); } catch (Exception) { } }
    }

    static void Transmit(string mp3, double freq, string name, double freq2 = 0, int side = 0)   // side: SRS coalition (A133), 0 = Cfg.Coalition
    {
        lock (OwnStations) OwnStations.Add(name.Replace(" ", ""));
        bool ic = freq < 0;   // Intercom: SRS delivers it if the unitId is the aircraft's
        var (o, _) = Run(SrsFile(Cfg.SrsPath, "ExternalAudio", "DCS-SR-ExternalAudio.exe") ?? Path.Combine(Cfg.SrsPath, "ExternalAudio", "DCS-SR-ExternalAudio.exe"), null,
            $"--file={mp3}", $"--freqs={(ic ? "100" : freq.ToString("0.000", CultureInfo.InvariantCulture) + (freq2 > 0 ? "," + freq2.ToString("0.000", CultureInfo.InvariantCulture) : ""))}",
            $"--modulations={(ic ? "INTERCOM" : Cfg.Modulation + (freq2 > 0 ? "," + Cfg.Modulation : ""))}", $"--coalition={(side > 0 ? side : Cfg.Coalition)}", $"--port={SrsPort}", "--name=" + name.Replace(" ", ""),
            "--volume=" + Cfg.Volume.ToString(CultureInfo.InvariantCulture),
            $"--unitId={(ic ? -freq : 100000)}");   // 100000: SrsListener does not overhear own controllers
        if (o.Contains("Could not connect")) Console.WriteLine(L("      (SRS-Server nicht erreichbar)", "      (SRS server not reachable)"));
    }

    static (string stdout, int exit) Run(string exe, string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            if (exe.Contains("ExternalAudio")) try { p.PriorityClass = ProcessPriorityClass.High; } catch (Exception) { }   // steady rhythm, otherwise choppy when DCS maxes out the CPU
            if (stdin != null) { p.StandardInput.WriteLine(stdin); p.StandardInput.Close(); }
            var err = p.StandardError.ReadToEndAsync();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 && exe == Ffmpeg) Console.WriteLine($"      ffmpeg: {err.Result.Trim()}");
            if (p.ExitCode != 0 || o.Contains("Could not connect")) Trace("FEHLER", $"{Path.GetFileName(exe)} Exit {p.ExitCode}: {err.Result.Trim()} {o.Trim()}");
            return (o, p.ExitCode);
        }
        catch (Exception e)
        {
            Console.WriteLine(L("      Fehler beim Start von ", "      Error starting ") + $"{Path.GetFileName(exe)}: {e.Message}");
            Trace("FEHLER", $"Start {exe}: {e.GetType().Name}: {e.Message}");
            return ("", -1);
        }
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
    [DllImport("winmm.dll")] static extern int joyGetPosEx(int id, ref JOYINFOEX info);

    [StructLayout(LayoutKind.Sequential)]
    public struct JOYINFOEX
    {
        public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }

    /// Buttons (bitmask) and coolie hat (hundredths of a degree, 0 = front, 65535 = center) of a joystick.
    public static (int Buttons, int Pov) Joy(int id)
    {
        var j = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = 0x80 | 0x40 };   // JOY_RETURNBUTTONS | JOY_RETURNPOV
        return joyGetPosEx(id, ref j) == 0 ? (j.dwButtons, j.dwPOV) : (0, 65535);
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

    internal static string ConfigPath => Path.Combine(Root, "config.jsonc");
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
        (Cfg.MyCallsign, Cfg.Volume, Cfg.SpeechRate, Cfg.ReplyPause, Cfg.RadioFx, Cfg.PilotVoice, Cfg.AiChatter, Cfg.AiFlightComms, Cfg.Atis, Cfg.AltimeterUnit, Cfg.WheelInGame, Cfg.AirspaceWatch) =
            (n.MyCallsign, n.Volume, n.SpeechRate, n.ReplyPause, n.RadioFx, n.PilotVoice, n.AiChatter, n.AiFlightComms, n.Atis, n.AltimeterUnit, n.WheelInGame, n.AirspaceWatch);
        Flights.Mode = Cfg.AiFlightComms;   // Tower.AltimeterUnit is set by LoadConfig
        (Flights.Cmd, Flights.Follow) = (l => AiCmd(l), Cfg.AiFollowAwacs);   // LD1 ENGAGE only with AiFollowAwacs, LD7 RTB always
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

    /// Self-test: settings window writes config.jsonc (comments stay, missing keys added), callsign normalized.
    static void ConfigTest()
    {
        var c = "{\n  \"Callsign\": \"Enfield 1-1\",\n  \"Volume\": 0.6,   // leiser\n  \"Atis\": true\n}";
        c = WithConfigValue(WithConfigValue(WithConfigValue(c, "Volume", "0.8", ""), "Atis", "false", ""), "MyCallsign", "\"Viper 1-1\"", "Callsign");
        c = WithConfigValue(c, "MyCallsign", "\"\"", "Callsign");
        if (!c.Contains("\"Volume\": 0.8,   // leiser") || !c.Contains("\"Atis\": false") || !c.Contains("\"Callsign\": \"Enfield 1-1\",\n  \"MyCallsign\": \"\",") ||
            WithConfigValue("{\n}", "RunwayLights", "\"dcs\"", "Fehlt") != "{\n  \"RunwayLights\": \"dcs\",\n}" || JsonSerializer.Deserialize<Config>("{ \"WheelStyle\": \"hud\" }") == null || NormCallsign("viper11") != "Viper 1-1" || NormCallsign("Viper") != null)
            throw new Exception("Einstellungen: " + c);
        Console.WriteLine("OK   Einstellungen: config.jsonc-Werte setzen (Kommentare bleiben), alte Option WheelStyle stört nicht, Rufzeichen \"viper11\" -> \"Viper 1-1\"");
        var srsDir = Path.Combine(Path.GetTempPath(), $"DcsAtc-SrsTest-{Environment.ProcessId}");   // SRS on another drive/directory (forum 0.9.5)
        Directory.CreateDirectory(Path.Combine(srsDir, "ExternalAudio"));
        File.WriteAllText(Path.Combine(srsDir, "ExternalAudio", "DCS-SR-ExternalAudio.exe"), "");
        bool srsOk = FindSrs(srsDir) == srsDir && !SrsOk(@"Z:\gibt-es-nicht") && !SrsOk("") && SrsOk(FindSrs(@"Z:\gibt-es-nicht")) == SrsOk(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\DCS-SR-Standalone", "SRPathStandalone", null) as string);
        Directory.Delete(srsDir, true);
        if (!srsOk) throw new Exception("SRS-Ordner");
        Console.WriteLine("OK   SRS-Ordner: eingestellter Pfad gilt, falscher Pfad -> SRS-Installation aus der Registry");
        string sNew = srsDir + "-neu", sOld = srsDir + "-alt";   // SRS layouts: from 2.2 subfolders, up to 2.1 everything in the folder (forum: client did not start automatically)
        foreach (var f in new[] { @"Client\SR-ClientRadio.exe", @"Server\SRS-Server.exe", @"ExternalAudio\DCS-SR-ExternalAudio.exe", @"Client\opus.dll" }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(sNew, f))!); File.WriteAllText(Path.Combine(sNew, f), ""); }
        foreach (var f in new[] { "SR-ClientRadio.exe", "SR-Server.exe", "DCS-SR-ExternalAudio.exe" }) { Directory.CreateDirectory(sOld); File.WriteAllText(Path.Combine(sOld, f), ""); }
        File.WriteAllText(Path.Combine(sNew, "Server", "server.cfg"), "[General Settings]\nLOS_ENABLED=false\n\n[Server Settings]\nSERVER_PORT = 5010\nUPNP_ENABLED=true\n");
        string Srv(string d) => SrsFile(d, "Server", "SRS-Server.exe", "SR-Server.exe") ?? "-";
        var lay = new[] {
            Srv(sNew) == Path.Combine(sNew, "Server", "SRS-Server.exe"), Srv(sOld) == Path.Combine(sOld, "SR-Server.exe"),
            SrsFile(sNew, "Client", "SR-ClientRadio.exe") == Path.Combine(sNew, "Client", "SR-ClientRadio.exe"), SrsFile(sOld, "Client", "SR-ClientRadio.exe") == Path.Combine(sOld, "SR-ClientRadio.exe"),
            SrsFile(sNew, "Client", "opus.dll") == Path.Combine(sNew, "Client", "opus.dll"), SrsFile(sOld, "Client", "opus.dll") == null, SrsFile(null, "Client", "x") == null,
            SrsOk(sNew), SrsOk(sOld), FindSrs(sOld) == sOld,   // configured: subfolder, exe, with quotation marks
            SrsRoot(Path.Combine(sNew, "Client")) == sNew, SrsRoot(Path.Combine(sNew, "Server", "SRS-Server.exe")) == sNew, SrsRoot($"  \"{sNew}\\\" ") == sNew, SrsRoot(Path.Combine(sOld, "SR-ClientRadio.exe")) == sOld, SrsRoot(" ") == null,
            SrsCfgInt(Path.Combine(sNew, "Server", "server.cfg"), "SERVER_PORT", 5002) == 5010, SrsCfgInt(Path.Combine(sNew, "Server", "server.cfg"), "DCSAutoConnectUDP", 5069) == 5069, SrsCfgInt(Path.Combine(sOld, "server.cfg"), "SERVER_PORT", 5002) == 5002,
        };
        Directory.Delete(sNew, true); Directory.Delete(sOld, true);
        if (lay.Contains(false)) throw new Exception("SRS-Layout " + string.Join(",", lay.Select((ok, i) => ok ? "" : i.ToString()).Where(x => x != "")));
        using (var rx = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))   // Auto-connect like DCS-SRSGameGUI.lua: "host:port\n" via UDP to the client
        {
            rx.Client.ReceiveTimeout = 2000;
            SendSrsConnect(((IPEndPoint)rx.Client.LocalEndPoint!).Port, 5010);
            IPEndPoint? from = null;
            if (System.Text.Encoding.UTF8.GetString(rx.Receive(ref from)) != "127.0.0.1:5010\n") throw new Exception("SRS-Auto-Connect-Nachricht");
        }
        using (var ls = new TcpListener(IPAddress.Loopback, 0))   // connected? existing TCP connection of the process to the server port
        {
            ls.Start();
            int lp = ((IPEndPoint)ls.LocalEndpoint).Port;
            using var tc = new TcpClient(); tc.Connect(IPAddress.Loopback, lp);
            using var acc = ls.AcceptTcpClient();
            if (!TcpPeers(new[] { Environment.ProcessId }).Contains(lp) || TcpPeers(new[] { -1 }).Count > 0) throw new Exception("SRS-Client-Verbindung (TCP-Tabelle)");
        }
        Console.WriteLine("OK   SRS-Layouts: Client\\/Server\\ (2.2+) und alles im Ordner (bis 2.1, SR-Server.exe), Pfad auf Unterordner/exe/mit Anführungszeichen, SERVER_PORT aus server.cfg, Auto-Connect \"127.0.0.1:5010\\n\" per UDP, Verbindung aus der TCP-Tabelle");
        // Dedicated server: process DCS_server without DCS, installation with bin\DCS_server.exe, server's Saved Games (hook, installer, default names)
        var sv = Directory.CreateTempSubdirectory("dcsatc-server").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(sv, "inst", "bin"));
            File.WriteAllText(Path.Combine(sv, "inst", "bin", "DCS_server.exe"), "");
            var none = ServerSavedGames(sv, null, "");
            Directory.CreateDirectory(Path.Combine(sv, "DCS.dcs_serverrelease"));
            Directory.CreateDirectory(Path.Combine(sv, "Inst1"));   // -w Inst1
            if (!ServerOnly(new[] { "DCS_server" }) || !ServerOnly(new[] { "dcs_server" }) || ServerOnly(new[] { "DCS", "DCS_server" }) || ServerOnly(new[] { "DCS" }) || ServerOnly(Array.Empty<string>())
                || !DcsExe(Path.Combine(sv, "inst")) || DcsExe(sv) || none != Path.Combine(sv, "DCS.server")
                || ServerSavedGames(sv, null, null) != Path.Combine(sv, "DCS.dcs_serverrelease") || ServerSavedGames(sv, Path.Combine(sv, "fehlt"), " ") != Path.Combine(sv, "DCS.dcs_serverrelease")
                || ServerSavedGames(sv, Path.Combine(sv, "Inst1") + "\\\r\n", Path.Combine(sv, "DCS.dcs_serverrelease")) != Path.Combine(sv, "Inst1")
                || ServerSavedGames(sv, null, Path.Combine(sv, "Inst1")) != Path.Combine(sv, "Inst1"))
                throw new Exception("Dedicated Server: Prozess-/Ordnererkennung");
        }
        finally { try { Directory.Delete(sv, true); } catch (IOException) { } }
        Console.WriteLine("OK   Dedicated Server: nur DCS_server läuft -> Hintergrundbetrieb (mit DCS nicht), bin\\DCS_server.exe zählt als DCS-Ordner, Saved Games: Hook (-w) vor Installer vor DCS.server/DCS.dcs_serverrelease");
        string Vr(string on) => $"options = {{\n\t[\"VR\"] = {{\n\t\t[\"custom_IPD_enable\"] = true,\n\t\t[\"enable\"] = {on},\n\t\t[\"pixel_density\"] = 1,\n\t}},\n\t[\"graphics\"] = {{\n\t\t[\"enable\"] = true,\n\t}},\n}}";
        const string Cl = "2026-10-08 11:44:51.233 INFO    APP (Main): Command line: \"D:\\DCS World\\bin/DCS.exe\" ";
        if (!VrOn(Vr("true"), "") || VrOn(Vr("false"), "") || VrOn("options = { [\"VR\"] = { [\"bloom\"] = true }, [\"x\"] = { [\"enable\"] = true } }", "")
            || !VrOn(Vr("false"), Cl + "--force_enable_VR --force_OpenXR\n") || VrOn(Vr("true"), Cl + "--force_disable_VR\n") || !VrOn(Vr("true"), Cl + "--restarted\n"))
            throw new Exception("VR-Erkennung (options.lua, dcs.log)");
        Console.WriteLine("OK   VR-Erkennung: options.lua VR.enable = true -> Funkrad im Spiel, false bzw. enable nur außerhalb von VR -> nicht; Startparameter --force_enable_VR/--force_disable_VR (dcs.log) gehen vor");
        var (sl, sa) = ParseSelf("S;6000.0;5800.0;180.00;1.5708;100.0;200.0;1.00;2.00;50000.000;0.1000;37040;-500;5500");
        var (so, _) = ParseSelf("S;6000.0;5800.0;180.00;1.5708;100.0;200.0;1.00;2.00;50000.000;0.0000");
        if (sl.Lock != (37040, -500, 5500) || sl.Z != 200 || sa != 0.1 || so.Lock != null) throw new Exception($"Telemetrie: {sl} / {so}");
        Console.WriteLine("OK   Telemetrie: S-Zeile mit aufgeschaltetem Ziel, ohne Lock wie bisher");
        // LIGHT-P1: lighting in every approach phase in the air (emergency jumps to ClearedLand) and from the taxi clearance, keys on the ground F3 "Request startup"
        var kf = Airfield.Kutaisi();
        Telemetry At(double nm, double agl) => new(kf.Elev + agl, agl, 0, 0, kf.X + nm * NM, kf.Z, 0, 0, 101325);
        var lk = string.Join(",", new[] { LightKind(Phase.ClearedLand, At(8, 300), kf), LightKind(Phase.Pattern, At(2, 300), kf), LightKind(Phase.Inbound, At(25, 1500), kf),
            LightKind(Phase.TaxiOut, At(0.5, 0), kf), LightKind(Phase.Parked, At(0.5, 0), kf), LightKind(Phase.ClearedLand, At(0.2, 0), kf) }.Select(k => k ?? "-"));
        var mk = string.Join(",", MenuKeys("dep", 2, 6).Select(k => k.ToString("X2")));
        if (lk != "atc,atc,-,dep,-,-" || mk != $"{Cfg.CommsKey:X2},3F,3C,3D" || MenuKeys("tanker", 1, 1).Length != 3) throw new Exception($"Befeuerung: {lk} / {mk}");
        Console.WriteLine($"OK   Befeuerung: Notfall/Platzrunde in der Luft -> Inbound, Rollfreigabe -> Request startup (Tasten {mk}), 25 NM/geparkt/Ausrollen nichts");
        // Debug log: format "HH:mm:ss.fff [KAT] Text", via TraceMax to .old.txt, off/tests (SimLog) without a line
        var (tp, tm, to, sl0) = (TracePath, TraceMax, TraceOn, SimLog);
        var dir = Directory.CreateTempSubdirectory("dcsatc-trace").FullName;
        try
        {
            (TracePath, TraceMax, TraceOn, SimLog) = (Path.Combine(dir, "atc-debug.txt"), 60, true, null);
            Trace("A", "eins"); Trace("B", "zwei");   // 2 × 23 bytes, below the limit
            Trace("C", "drei");                       // previously 46 -> stays
            Trace("D", "vier");                       // previously 69 > 60 -> rotated
            using (File.Open(Path.Combine(dir, "atc-debug.old.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { TraceMax = 0; Trace("G", "offen"); }   // .old locked: append instead of losing
            TraceOn = false; Trace("E", "aus");
            (TraceOn, SimLog) = (true, new()); Trace("F", "test");
            var cur = File.ReadAllLines(TracePath);
            var old = File.ReadAllLines(Path.Combine(dir, "atc-debug.old.txt"));
            if (cur.Length != 2 || !Regex.IsMatch(cur[0], @"^\d\d:\d\d:\d\d\.\d{3} \[D\] vier$") || !cur[1].EndsWith(" [G] offen") || old.Length != 3 || !old[2].EndsWith(" [C] drei"))
                throw new Exception($"Debug-Log: {string.Join(" | ", cur)} / {string.Join(" | ", old)}");
        }
        finally { (TracePath, TraceMax, TraceOn, SimLog) = (tp, tm, to, sl0); try { Directory.Delete(dir, true); } catch (IOException) { } }   // Virus scanner holds the file: do not fail the test because of that
        Console.WriteLine("OK   Debug-Log: Zeile \"HH:mm:ss.fff [KAT] Text\", über der Grenze nach atc-debug.old.txt, aus bzw. im Test keine Zeile");
    }

    /// Self-test: airfield frequencies from Radio.lua (only if DCS is installed).
    static void RadioTest()
    {
        Kneeboard.SelfTest();
        // Paths: Known Folder Saved Games with environment variable, Steam libraries from libraryfolders.vdf
        var home = Environment.GetEnvironmentVariable("USERPROFILE");
        var libs = SteamLibraries("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"label\"\t\t\"\"\n\t}\n}").ToList();
        if (ShellFolder(@"%USERPROFILE%\Saved Games") != home + @"\Saved Games" || ShellFolder("") != null
            || string.Join("|", libs) != @"C:\Program Files (x86)\Steam|D:\SteamLibrary")
            throw new Exception($"Pfade: {ShellFolder(@"%USERPROFILE%\Saved Games")} / {string.Join("|", libs)}");
        Console.WriteLine($"OK   Pfade: %USERPROFILE%\\Saved Games -> {home}\\Saved Games, Steam-Bibliotheken {string.Join(", ", libs)}");
        var kut = new Airfield { Name = "Kutaisi", Id = 25 };
        Airfield.LoadRadio(new[] { kut }, DcsDir());
        if (!File.Exists(Path.Combine(DcsDir(), "Mods", "terrains", "Caucasus", "Radio.lua"))) { Console.WriteLine("--   Radio.lua: DCS nicht gefunden"); return; }
        if (kut.Uhf != 263.0 || kut.Vhf != 134.0) throw new Exception($"Radio.lua: Kutaisi {kut.Uhf}/{kut.Vhf}, erwartet 263/134");
        Console.WriteLine($"OK   Radio.lua: Kutaisi {kut.Uhf}/{kut.Vhf}");
        // K3/K6: all airfields (Id 12–32): no default function frequency on an airfield frequency (old AWACS 251.0; Tower 265.0 = Nalchik does not count), call name nato/common
        var all = Enumerable.Range(12, 21).Select(i => new Airfield { Name = i switch { 13 => "Krasnodar-Center", 19 => "Krasnodar-Pashkovsky", 23 => "Senaki-Kolkhi", _ => $"#{i}" }, Id = i }).ToList();
        Airfield.LoadRadio(all, DcsDir());
        var clash = FreqClashes(all, new Config().Frequencies).Concat(FreqClashes(all, new() { ["AWACS"] = 251.0, ["Tower"] = 265.0 })).ToList();
        if (clash.Count != 1 || clash[0] != "AWACS 251.0 = Krasnodar-Center UHF" || all[1].Callsign != "Krasnodar" || all[7].Callsign != "Pashkovsky"
            || all[11].StationOf("Tower") != "Kolkhi Tower" || all[14].Callsign != "Minvody")
            throw new Exception($"Radio.lua: {string.Join(" | ", clash)} / {string.Join(",", all.Select(f => f.Callsign))}");
        Console.WriteLine($"OK   Radio.lua: Standardfrequenzen frei (alt: {clash[0]}), Rufname Kolkhi/Pashkovsky/Minvody statt ussr");
        // Beacons.lua: Batumi (Id 22) ILS 110.30 on runway 13 (localizer behind the end of 31), TACAN 16X
        var bat = Airfield.FromDcs("Batumi", -356437.2, 618210.9, 10, new[] { ("31", 0.95013, -355810.7, 617386.2, 2070.0) });
        bat.Id = 22;
        Airfield.LoadBeacons(new[] { bat }, DcsDir());
        if (bat.NavText() != "ILS 13 110.30 ILU, TACAN 16X BTM, NDB LU 430") throw new Exception($"Beacons.lua: Batumi \"{bat.NavText()}\"");
        Console.WriteLine($"OK   Beacons.lua: Batumi {bat.NavText()}");
        // K16: NDB (far/near homer, airfield homer): Anapa AP 443 / P 215 and AN 443 / N 215 (far and near homer at each runway end); loading again duplicates nothing
        var ana = new Airfield { Name = "Anapa", Id = 12 };
        Airfield.LoadBeacons(new[] { ana }, DcsDir());
        Airfield.LoadBeacons(new[] { ana }, DcsDir());
        if (ana.NdbText() != "AP 443 / P 215 / AN 443 / N 215") throw new Exception($"Beacons.lua: Anapa NDB \"{ana.NdbText()}\"");
        Console.WriteLine($"OK   Beacons.lua: Anapa NDB {ana.NdbText()}");
    }

    /// Radio: confidence from Whisper JSON, priorities, "stand by" only on an occupied frequency (AI radio does not count).
    static void FunkTest()
    {
        var js = """{"transcription":[{"tokens":[{"text":"[_BEG_]","p":0.9},{"text":" bottom","p":0.07},{"text":"y","p":0.86},{"text":" approach","p":0.98},{"text":",","p":0.1}]}]}""";
        double c = Confidence(js);   // "bottomy" = weakest token 0.07, "approach" 0.98, comma does not count
        if (Math.Abs(c - 0.525) > 0.001) throw new Exception($"Sicherheit {c}");
        if (PrioOf(new Tx("Enfield one one, go around, I say again, go around", "Tower", "", 0)) != 0 || PrioOf(new Tx("cleared to land", "Tower", "", 0, Prio: 2)) != 2 ||
            new[] { "runway two five, wind calm, cleared to land, emergency services standing by", "check wheels down!", "low altitude alert, check your altitude immediately", "hold position!" }   // A24: safety warnings
                .Any(s => PrioOf(new Tx("Enfield one one, " + s, "Tower", "", 0)) != 0))
            throw new Exception("Prioritäten");
        // LK16: rank on the frequency – defending > fox/pitbull/splash > threat/leaker > commit > picture > AI chatter; a long picture no longer holds up pitbull (AI radio prio 2)
        Tx Fl(string s, int pr) => new(s, "Flight", "Ford one one", 0, true, Prio: pr);
        Tx Aw(string s, int pr = 1) => new(s, "AWACS", "Overlord", 1, Prio: pr);
        var picLk = Aw("Enfield one one, Overlord, picture, 2 groups azimuth 10. East group, bullseye 040, 60, 26 thousand, track west, hostile, two contacts, Flanker.");
        var rk = new[] { Fl("Ford one one, launch north, defending.", 0), Fl("Ford one one, fox three, north group.", 1), Fl("Ford one one, pitbull.", 2), Fl("Ford one one, splash one.", 1),
                         Aw("Enfield one one, Overlord, threat, north group BRAA three five four, 20, 20 thousand, hot."), Aw("Overlord, leaker, north group, bullseye zero four zero, 20, 15 thousand, track east, hostile."),
                         Aw("Ford one, Overlord, commit west group, BRAA two nine zero, 35, 25 thousand, hot.", 2), picLk, Aw("Overlord, new group, bullseye zero three zero, 45, 20 thousand, track south, hostile, single."),
                         Fl("Overlord, Ford one, on station, angels 25.", 2), Fl("Ford one one, joker.", 2), Fl("Ford one, committing.", 2) }.Select(RankOf).ToArray();
        if (!rk.SequenceEqual(new[] { 0, 1, 1, 1, 2, 2, 3, 4, 4, 4, 4, 3 }) || new[] { picLk, Fl("Ford one one, pitbull.", 2) }.MinBy(RankOf)!.Role != "Flight"
            || AwacsRow("Enfield one one, Overlord, north group merged.") is not { Fresh: true } || AwacsRow("Overlord, furball, bullseye three five four, 20.") is not { Fresh: true })
            throw new Exception("LK16 Rang: " + string.Join(",", rk));
        Item Ld3(int r) => new("", "", () => { }, Clock, 0, 1, 1, 45, 0, r);   // LD3: after fox/defending rank 3/4 wait, urgent (≤ 2) does not
        HotUntil = Clock.AddSeconds(5);
        bool hot3 = Hot(new[] { Ld3(3), Ld3(4) }), hot2 = Hot(new[] { Ld3(4), Ld3(2) });
        HotUntil = default;
        if (!hot3 || hot2) throw new Exception($"LD3 Funkpause: {hot3} {hot2}");
        // A19: LSO calls to players prio 0 (no "stand by"), valid 2 s or wave off/bolter 4 s; AI radio from Paddles stays prio 2, Marshal normal
        var lsoP = new Pilot { Unit = "LSO1", Gid = 7 };
        Tx Lx(string st, string text) => OpsTx(lsoP, new Ops.Call("Carrier", st, text, 127.5));
        var lsoQ = new List<Tx> { Lx("Paddles", "Power.") };
        AddStandby(lsoP, lsoQ);
        if (PrioOf(Lx("Paddles", "Power.")) != 0 || Lx("Paddles", "Power.").MaxAge != 2 || Lx("Paddles", "Wave off, wave off.").MaxAge != 4 || Lx("Paddles", "Bolter, bolter, bolter.").MaxAge != 4
            || Lx("Paddles", "Roger ball.").MaxAge < 5 || Lx("Paddles", "Enfield one one, call the ball.").MaxAge < 5   // Acknowledgement/prompt: do not discard behind the own ball call (pilot voice)
            || PrioOf(new Tx("Roger ball.", "Carrier", "Paddles", 0, Prio: 2)) != 2 || PrioOf(Lx("Marshal", "Enfield one one, Marshal, roger.")) != 1 || Lx("Marshal", "x").MaxAge != 0 || lsoQ[0].Standby != null)
            throw new Exception("A19: LSO-Prio/MaxAge");
        lock (TowerLock)   // A26: unknown SRS sender -> player via the spoken callsign
        {
            Pilots["t1"] = new Pilot { Callsign = "Enfield 1-1" }; Pilots["t2"] = new Pilot { Callsign = "Colt 2-1", SlotCallsign = "Uzi 3-1" };
            var a = ByCallsign("Enfeld 1-1, request taxi"); var b = ByCallsign("Kutaisi Tower, Uzi 3-1, inbound"); var n = ByCallsign("Station, say again");
            var g = ByCallsign("Enfield 1-1, Magic, BRAA 270/30, 20 thousand, hostile");   // Addressee, not sender
            Fields.Add(new Airfield { Name = "Kutaisi" }); var k = ByCallsign("Enfield one one, Kutaisi Tower, cleared to land"); Fields.RemoveAt(Fields.Count - 1);
            Pilots.Remove("t1"); Pilots.Remove("t2");
            if (a?.Callsign != "Enfield 1-1" || b?.Callsign != "Colt 2-1" || n != null || g != null || k != null) throw new Exception($"Rufzeichen -> Spieler (Adressat zählt nicht): {g?.Callsign} {k?.Callsign}");
            // Mission data stalls (6 s, log Dubai 17:00:53 "jetzt Dubai Intl" during rollout): the mission player keeps flying, no solo with a new tower
            var (st0, ses0, sr0, tel0, tt0) = (stateTime, session, sessionRead, Tel, TelTime);
            var me = Pilots["t1"] = new Pilot { Name = "Me", Callsign = "Dagger 1-1", Seen = Clock };
            (stateTime, session, sessionRead, Tel, TelTime) = (Clock.AddSeconds(-6), new[] { "", "Me" }, Clock, new Telemetry(50, 0, 20, 0, 1, 2, 0, 0, 760), Clock);
            var (ap, host) = (ActivePilots(), Host());
            (stateTime, session, sessionRead, Tel, TelTime) = (st0, ses0, sr0, tel0, tt0);
            Pilots.Remove("t1");
            if (ap.Count != 1 || ap[0] != me || host != me || me.Tel?.X != 1) throw new Exception($"Missionsdaten stocken: {ap.FirstOrDefault()?.Unit} statt Missions-Spieler");
        }
        var ch = Channels[999] = new Chan();   // without sender thread
        ch.FreeAt = Clock.AddSeconds(30);   // running call does not count (A86)
        double running = Backlog(999);
        ch.Q.Add(new Item("", "", () => { }, Clock, 0, 2, 8, 45));
        double ai = Backlog(999);
        ch.Q.Add(new Item("", "", () => { }, Clock, 0, 1, 8, 45));
        double busy = Backlog(999);
        Channels.TryRemove(999, out _);
        if (running != 0 || ai != 0 || busy < StandbySec) throw new Exception($"Wartezeit {running} / {ai} / {busy}");
        // A86: "stand by" at the first Tx, only before a normal reply, with the callsign of the reply (flight), otherwise that of the player
        var sp = new Pilot { Callsign = "Enfield 1-2" };
        var rep = new Tx("Enfield one one flight, runway two five, cleared for takeoff.", "Tower", "Kutaisi Tower", 0, Freqs: new[] { 998.0, 997.0 });
        var withCall = new List<Tx> { new("Kutaisi Tower, Enfield one two, ready.", "Tower", "Enfield 1-2", 0, true, Prio: 0), rep };
        var onlyRep = new List<Tx> { rep };
        var urgent = new List<Tx> { rep with { Text = "Enfield one one, roger, go around." } };
        var mayday = new List<Tx> { rep with { Text = "Enfield one one, roger mayday, emergency services are on the way." } };
        var atis = new List<Tx> { rep with { Text = "Kutaisi information Alpha, wind calm." } };
        var infoOnly = new List<Tx> { new("Hinweis", "Info", "", 0) };
        foreach (var l in new[] { withCall, onlyRep, urgent, mayday, atis, infoOnly }) AddStandby(sp, l);
        if (withCall[0].Standby is not { Text: "Enfield one one flight, stand by.", Prio: 0 } sb || FreqOf(sb) != 998 || withCall[1].Standby != null
            || onlyRep[0].Standby?.Text != "Enfield one one flight, stand by."
            || urgent[0].Standby != null || mayday[0].Standby != null
            || atis[0].Standby?.Text != "Enfield one two, stand by." || infoOnly[0].Standby != null)
            throw new Exception("stand by: Rufzeichen/Reihenfolge/dringend");
        string mig = "\"AWACS\": \"en_US-joe-medium@1.1|en_US-ljspeech-medium\", \"Carrier\": \"en_US-ryan-medium@0.97|en_US-joe-medium@1.02\", \"SpeechRate\": 1.0,", own = "\"AWACS\": \"en_US-joe-medium@0.9\", \"SpeechRate\": 1.1,", own2 = own;   // 1.1 = pace from the settings, stays
        foreach (var (o, n) in ConfigMigrations) { mig = mig.Replace(o, n); own2 = own2.Replace(o, n); }
        if (mig != "\"AWACS\": \"en_GB-alan-medium|en_US-ljspeech-medium\", \"Carrier\": \"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02\", \"SpeechRate\": 0.85," || own2 != own || PickVoice("en_GB-gibtsnicht-medium", "X") != "en_US-ryan-medium"
            || Enumerable.Range(0, 64).Any(i => AiVoiceFor("Viper " + i).StartsWith("en_GB-alan")))
            throw new Exception($"AWACS-Stimme: Migration {mig} / eigener Wert {own2} / fehlendes Modell / KI-Pilot mit alan");
        Tx ki(string t, string role = "Tower", int prio = 2) => new(t, role, "Kutaisi Tower", 0, Prio: prio);   // A134: display duration in the .out line
        int d1 = ShowSec(ki("Enfield one one, cleared to land."), ""), d2 = ShowSec(ki(new string('x', 100)), ""), d3 = ShowSec(ki(new string('x', 52), "Flight", 1), ""),
            d4 = ShowSec(ki("Enfield one one, roger.", "Tower", 1), "Kutaisi Tower (265.0): "), d5 = ShowSec(ki(new string('x', 400), "Tower", 1), "");
        if (d1 != 6 || d2 != 8 || d3 != 7 || d4 != 20 || d5 != 50) throw new Exception($"Anzeigedauer {d1} {d2} {d3} {d4} {d5}");
        Console.WriteLine($"OK   Funk: Sicherheit {c:0.00}, go around dringend, LSO-Calls Prio 0 ohne stand by (2/4 s), KI-Funk und laufender Spruch zählen nicht, Antwort hinter 8 s Spruch -> stand by (Rufzeichen der Antwort, nicht vor dringender/Notfall-Antwort), AWACS-Stimme alan (kein KI-Pilot)");
        var model = Path.Combine(Root, "models", "en_US-ryan-medium.onnx");
        if (!File.Exists(model)) { Console.WriteLine("--   Piper: Stimme fehlt"); return; }
        var wav = Path.Combine(Path.GetTempPath(), "DcsAtc-piper.wav");
        var sw = Stopwatch.StartNew();
        bool ok = PiperWarm(model, "1", "radio check", wav) && PiperWarm(model, "1", "Enfield one one, Kutaisi Tower, cleared for takeoff.", wav);
        if (!ok || new FileInfo(wav).Length < 20000 || Pipers.Count != 1) throw new Exception("Piper warm");
        Console.WriteLine($"OK   Piper warm: 2 Sprüche, ein Prozess, {sw.ElapsedMilliseconds} ms");
        // Synth cache: the same short phrase (LSO) the second time without Piper/ffmpeg
        Directory.CreateDirectory(Path.Combine(Root, "tmp"));
        string m1 = Path.Combine(Root, "tmp", "cachetest1.mp3"), m2 = Path.Combine(Root, "tmp", "cachetest2.mp3");
        sw.Restart(); double s1 = Synth("Roger ball.", "en_US-ryan-medium", m1); long t1 = sw.ElapsedMilliseconds;
        sw.Restart(); double s2 = Synth("Roger ball.", "en_US-ryan-medium", m2); long t2 = sw.ElapsedMilliseconds;
        if (!File.Exists(m2) || new FileInfo(m2).Length != new FileInfo(m1).Length || s1 != s2 || t2 > 100) throw new Exception($"Synth-Cache {t1}/{t2} ms");
        Console.WriteLine($"OK   Synth-Cache: \"Roger ball.\" {t1} ms, danach {t2} ms");
        File.Delete(wav);
    }

    /// Self-test N1/A133: hostile airfield without air traffic control (select airfield, pick, switch, call, clearance, all red, capture via W line), controllers transmit with the side of the airfield.
    static void SideTest()
    {
        var (fs0, log0, all0) = (Fields, SimLog, Tower.All);
        var kut = Airfield.Kutaisi();
        Airfield Fs(string name, double nm, int side) { var a = Airfield.FromDcs(name, kut.X + nm * NM, kut.Z, 20, new[] { ("13", 0.0, kut.X + nm * NM, kut.Z, 2500.0) }); a.Side = side; return a; }
        var (red, blue, neu) = (Fs("Maykop", 5, 1), Fs("Krymsk", 30, 2), Fs("Senaki", 60, 0));
        List<Tx> Take() { var l = new List<Tx>(); while (SayQueue.TryTake(out var t)) l.Add(t); return l; }
        try
        {
            (SimLog, Fields) = (new(), new() { red, blue, neu });
            Tower.All = Fields;
            Take();
            var p = new Pilot { Unit = "S1", Callsign = "Enfield 1-1", Gid = 3, Tel = new Telemetry(2000, 1500, 150, 0, kut.X, kut.Z, 0, 0, 0) };
            var (np, pick) = (string.Join(",", NearPlaces(p.Tel, 2)), Pick(p, null)?.F.Name);
            Request(p, "switch Maykop", "RAD", true);
            Request(p, "Maykop Approach, Enfield 1-1, inbound for landing", "T", false);   // second attempt: no second notice, no reply
            var hint = Take();
            var pg = new Pilot { Unit = "S2", Callsign = "Enfield 1-2", Gid = 4, Tel = new Telemetry(blue.Elev, 0, 0, 0, blue.X, blue.Z, 0, 0, 0) };
            Request(pg, "Krymsk Ground, Enfield 1-2, request IFR clearance to Maykop", "T", false);
            var clr = Take().Where(t => t.Role != "Info").ToList();
            var rp = new Pilot { Unit = "S3", Callsign = "Dodge 1-1", Gid = 5, Coalition = 1, Tel = p.Tel };
            Request(rp, "Maykop Tower, Dodge 1-1, radio check", "T", false);
            var rt = Take().Where(t => t.Role != "Info").ToList();
            pg.Active = TowerFor(pg, blue);
            FieldSide("Krymsk", 1);   // captured (W line): side new, tower stays, only Senaki left
            var (conq, pickC) = (blue.Side == 1 && pg.Towers.ContainsKey("Krymsk") && pg.Active != null, Pick(pg, null)?.F.Name);
            Fields = new() { red, blue };   // all red: no airfield, no crash
            Request(p, "Tower: radio check", "RAD", true);
            var none = Take();
            if (np != "Krymsk,Senaki" || pick != "Krymsk" || p.Pinned != null || hint.Count != 1 || hint[0].Role != "Info" || !hint[0].Text.Contains("Maykop") || !hint[0].Text.Contains("Krymsk") || !hint[0].Text.Contains("/30 NM")
                || clr.Count == 0 || !clr[^1].Text.EndsWith("unable clearance to Maykop, say alternate destination.") || clr.Any(t => t.Side != 2)
                || rt.Count == 0 || rt.Any(t => t.Side != 1) || !conq || pickC != "Senaki" || none.Count != 1 || none[0].Role != "Info")
                throw new Exception($"Koalition: {np} / {pick} / {p.Pinned?.F.Name} / {string.Join(" | ", hint.Select(t => t.Text))} / {string.Join(" | ", clr.Select(t => $"{t.Side}:{t.Text}"))} / " +
                                    $"{string.Join(" | ", rt.Select(t => $"{t.Side}:{t.Text}"))} / {conq} {pickC} / {string.Join(" | ", none.Select(t => t.Text))}");
            Console.WriteLine($"OK   Koalition (N1/A133): Platz wählen {np}, Pick Krymsk statt Maykop, switch/Ruf Maykop nur ein Hinweis ({hint[0].Text}), Clearance nach Maykop \"unable\", roter Lotse sendet als Rot, Eroberung per W-Zeile, alle rot ohne Absturz");
        }
        finally { (SimLog, Fields, Tower.All) = (log0, fs0, all0); Take(); }
    }

    /// Self-test forum request AirfieldFrequencies: config.jsonc (name tolerant, number or list), invalid ignored, airfield and controller by called frequency,
    /// Ground replies on its frequency, ATIS names the own tower, unchanged without an entry. Handovers (Tower, Departure): Tower.SelfTest.
    static void FreqTest()
    {
        var (fs0, log0, all0) = (Fields, SimLog, Tower.All);
        var kut = Airfield.Kutaisi();
        Airfield Fs(string name, double nm, double uhf, double vhf) { var a = Airfield.FromDcs(name, kut.X + nm * NM, kut.Z, 570, new[] { ("21", 0.0, kut.X + nm * NM, kut.Z, 3000.0) }); (a.Uhf, a.Vhf) = (uhf, vhf); return a; }
        var (nel, nlv, cre) = (Fs("Nellis", 0, 327.0, 132.55), Fs("North Las Vegas", 10, 360.75, 125.7), Fs("Creech", 30, 360.6, 118.3));
        List<Tx> Take() { var l = new List<Tx>(); while (SayQueue.TryTake(out var t)) l.Add(t); return l; }
        var (cfg, cfgErr) = OwnFreqs("""
            // Kopf
            {
              "nellis": { "ATIS": 270.1, "Ground": 275.8, "Tower": 327.0, "Departure": 273.55, "Approach": 379.0 },   // Groß/Klein egal, ohne Abschnitt: jede Karte
              // ===== Caucasus =====
              "Kutaisi": { "Tower": 999 },   // andere Karte: nicht gelesen, keine Meldung
              // ===== Nevada =====
              "north-las vegas": { "tower": [125.7, 257.95] },   // id 4
              // "Creech": { "Tower": 1.0 },   // id 2
              "Creech": { "Tower": 3270, "Clearance": 251.0 },
              "Groom Lake": { "Tower": 250.0 },
            }
            """, "Nevada");
        try
        {
            (SimLog, Fields) = (new(), new() { nel, nlv, cre });
            Tower.All = Fields;
            Take();
            var bad = ApplyFreqs(Fields, cfg);
            var (gnd, dep, map) = (FreqRoute(275.8, null), FreqRoute(273.55, null), FreqRoute(360.6, null));
            var p = new Pilot { Unit = "F1", Callsign = "Enfield 1-1", Gid = 3, CalledOn = 275.8, Tel = new Telemetry(nel.Elev, 0, 0, 0, nel.X, nel.Z, 0, 0, 0) };
            Request(p, "Ground: Nellis Ground, Enfield 1-1, radio check", "SRS", false, gnd.Via);
            var rc = Take().Where(t => t.Role != "Info").ToList();
            p.Tel = p.Tel with { AltMsl = nel.Elev + 600, Agl = 600, Ias = 130 };
            p.Active!.Load(p.Active.Save() with { Phase = Phase.Away, HandedOff = true }, p.Tel, 1);   // after the handover in departure: radio wheel "Approach: …" calls Departure on its frequency
            Request(p, "Approach: request flight following", "Menu", true);
            var ff = Take().Where(t => t.Role != "Info").ToList();
            var atis = new Tower(nel, "x").AtisText("K", new Telemetry(nel.Elev + 2, 0, 0, 0, nel.X, nel.Z, 0, 0, 760), 15, false, 0, 9999);
            if (cfgErr != null || bad.Count != 3 || !bad.Any(b => b.Contains("Groom Lake")) || !bad.Any(b => b.Contains("3270")) || !bad.Any(b => b.Contains("Clearance")) || nel.Own.Count != 5 || cre.Own.Count != 0
                || nlv.FreqsOf("Tower") is not [125.7, 257.95] || nlv.FreqsOf("Ground") is not [360.75, 125.7] || FieldFreqs(nel, "ATIS") is not [270.1] || FieldFreqs(cre, "Departure") is not [360.6, 118.3]
                || gnd != (nel, "Ground") || dep != (nel, "Departure") || map != (cre, null)
                || rc.Count != 1 || rc[0].Role != "Ground" || rc[0].Station != "Nellis Ground" || rc[0].Freqs is not [275.8] || !atis.Contains("Tower three two seven decimal zero. ")
                || ff.Count != 2 || ff.Any(t => t.Role != "Departure" || t.Freqs is not [273.55]) || !ff[0].Text.StartsWith("Nellis Departure, Enfield 1 1"))
                throw new Exception($"AirfieldFrequencies: {string.Join(" | ", bad)} / Own {nel.Own.Count} {cre.Own.Count} / {gnd} {dep} {map} / {string.Join(" | ", rc.Concat(ff).Select(t => $"{t.Role} {t.Station} {Fq(t.Freqs)}: {t.Text}"))} / {atis}");
            Console.WriteLine($"OK   AirfieldFrequencies: frequencies.jsonc mit Abschnitten (Kutaisi unter Caucasus nicht gelesen), Komma am Ende, Nellis 5 Lotsen, North Las Vegas Liste, Creech 3270/Clearance und Groom Lake ignoriert, 275.8 -> Ground ({rc[0].Text}), 273.55 -> Departure, Funkrad nach der Übergabe an Departure 273.55, ATIS nennt Tower 327.0, Creech wie Karte");
            // duplicate: Nellis Ground = map frequency Creech, North Las Vegas Tower = Creech Ground (both own) -> one warning line each, call reaches the first (FreqRoute)
            var dup = ApplyFreqs(Fields, OwnFreqs("""{ "Nellis": { "Ground": 360.6 }, "North Las Vegas": { "Tower": 275.0 }, "Creech": { "Ground": 275.0, "ATIS": 327.0 } }""", "Nevada").Own);
            if (dup.Count != 2 || !dup[0].Contains("360.6 MHz") || !dup[0].Contains("Nellis") || !dup[0].Contains(", Creech ") || !dup[1].Contains("275.0 MHz") || !dup[1].Contains(", Creech ")
                || FreqRoute(360.6, null).Via != nel || FreqRoute(275.0, null).Via != nlv)
                throw new Exception("AirfieldFrequencies doppelt: " + string.Join(" | ", dup));
            Console.WriteLine($"OK   AirfieldFrequencies doppelt: {string.Join(" | ", dup)}");
            // create frequencies.jsonc (test Radio.lua: name from comment or beacon, config without UHF), uncomment a line, add without changing user lines
            var dir = Path.Combine(Path.GetTempPath(), "DcsAtc-FreqTest");
            Directory.CreateDirectory(Path.Combine(dir, "Mods", "terrains", "TestMap"));
            File.WriteAllText(Path.Combine(dir, "Mods", "terrains", "TestMap", "Radio.lua"), "radio = {\r\n\t{\r\n\t\t-- Alpha_Field\r\n\t\tradioId = 'airfield1_0';\r\n" +
                "\t\tcallsign = {{[\"common\"] = {_(\"Alpha\"), \"Alpha\"}}};\r\n\t\tfrequency = {[UHF] = {MODULATIONTYPE_AM, 250000000.000000}, [VHF_HI] = {MODULATIONTYPE_AM, 121000000.000000}};\r\n\t};\r\n" +
                "\t{\r\n\t\tradioId = 'airfield2_0';\r\n\t\tcallsign = {{[\"common\"] = {_(\"OMXX\"), \"OMXX\"}}};\r\n\t\tfrequency = {[VHF_HI] = {MODULATIONTYPE_AM, 118750000.000000}};\r\n\t};\r\n};\r\n");
            File.WriteAllText(Path.Combine(dir, "Mods", "terrains", "TestMap", "Beacons.lua"), "beacons = {\r\n\t{\r\n\t\tdisplay_name = _('Bravo-Intl');\r\n\t\tbeaconId = 'airfield2_0';\r\n\t};\r\n};\r\n");
            var tab = Airfield.RadioTable(dir, "TestMap");
            Directory.Delete(dir, true);
            var maps = new SortedDictionary<string, Dictionary<int, (double Uhf, double Vhf, string Cs, string Name)>> { ["TestMap"] = tab };
            var gen = FreqFile(null, maps, "Caucasus", new());
            var atisMhz = Cfg.Frequencies["ATIS"].ToString("0.0##", CultureInfo.InvariantCulture);
            var alpha = $"  // \"Alpha Field\": {{ \"ATIS\": {atisMhz}, \"Ground\": [250.0, 121.0], \"Tower\": [250.0, 121.0], \"Departure\": [250.0, 121.0], \"Approach\": [250.0, 121.0] }},   // id 1";
            var user = gen.Replace(alpha, alpha.Replace("  // \"Alpha Field\"", "  \"Alpha Field\"").Replace("\"Tower\": [250.0, 121.0]", "\"Tower\": 255.5"));
            var (rd, rdErr) = OwnFreqs(user, "TestMap");
            (maps["TestMap"] = new(tab))[3] = (260.0, 0, "", "Charlie");
            maps["NewMap"] = new() { [5] = (270.0, 0, "", "Delta") };
            var upd = FreqFile(user, maps, "TestMap", new() { [2] = "Bravo International" });
            var ul = upd.Split("\r\n").ToList();
            if (tab.Count != 2 || tab[1] != (250.0, 121.0, "Alpha", "Alpha Field") || tab[2] != (0, 118.75, "", "Bravo-Intl")
                || !gen.StartsWith("// DCS-ATC") || !gen.Contains("\r\n{\r\n  // ===== TestMap =====\r\n" + alpha + "\r\n  // \"Bravo-Intl\": { \"ATIS\": " + atisMhz + ", \"Ground\": [118.75], \"Tower\": [118.75],") || !gen.EndsWith("\r\n}\r\n")
                || rdErr != null || rd.Count != 1 || rd["Alpha Field"].GetProperty("Tower").GetDouble() != 255.5 || OwnFreqs(user, "NewMap").Own.Count != 0
                || !ul.Contains(user.Split("\r\n").First(l => l.Contains("255.5"))) || !upd.Contains("  // \"Bravo International\": ") || upd.Contains("Bravo-Intl")
                || ul.FindIndex(l => l.Contains("// id 3")) is not (> 0 and var i3) || i3 > ul.FindIndex(l => l.Contains("===== NewMap")) || ul.FindIndex(l => l.Contains("// id 5")) < 0
                || FreqFile(upd, maps, "TestMap", new() { [2] = "Bravo International" }) != upd || OwnFreqs(upd, "TestMap").Own.Count != 1)
                throw new Exception($"frequencies.jsonc: {string.Join(";", tab)} / {rdErr} {rd.Count}\r\n{gen}\r\n{upd}");
            // Airfield with VHF only in Radio.lua (Golf, OMAA 119.2): transmits/listens on its VHF (Ground/Tower/Approach together), not on the general config frequencies
            var vhf = Fs("Abu Dhabi Intl", 60, 0, 119.2);
            Fields.Add(vhf);
            ApplyFreqs(Fields, new());
            var vp = new Pilot { Unit = "V1", Tel = new Telemetry(vhf.Elev + 600, 0, 0, 0, vhf.X, vhf.Z, 0, 0, 0) };
            var vAtis = new Tower(vhf, "x").AtisText("K", new Telemetry(vhf.Elev + 2, 0, 0, 0, vhf.X, vhf.Z, 0, 0, 760), 15, false, 0, 9999);
            if (vhf.FreqsOf("Tower") is not [119.2] || vhf.FreqsOf("Departure") is not [119.2] || FreqRoute(119.2, null) != (vhf, null) || !ListenFreqs(new() { vp }).Contains(119.2)
                || !vAtis.Contains("Tower one one niner decimal two. ") || SpokenFreq(vhf, vp) != 119.2)
                throw new Exception($"VHF-Platz: {Fq(vhf.FreqsOf("*"))} / {FreqRoute(119.2, null)} / {string.Join(",", ListenFreqs(new() { vp }))} / {vAtis}");
            Console.WriteLine("OK   Platz nur mit VHF (Abu Dhabi 119.2): Lotsen auf 119.2, Ruf darauf erreicht ihn, SRS hört mit, ATIS nennt Tower 119.2");
            Console.WriteLine("OK   frequencies.jsonc: aus Radio.lua erzeugt (Name aus Kommentar/Funkfeuer, nur VHF als [VHF]), entkommentierte Zeile mit Komma am Ende gelesen, Ergänzen: neue Karte und neuer Platz, Nutzerzeile unverändert, unveränderte Zeile bekommt den Missionsnamen");        }
        finally { (SimLog, Fields, Tower.All) = (log0, fs0, all0); Take(); }
    }

    /// Self-test R47 (Departure/Flight Following keeps the airfield until logout, then pin released) and N40 (airfield captured: diversion on approach, AI DIVERT).
    static void CaptureTest()
    {
        var (fs0, log0, all0, ai0, ss0, st0) = (Fields, SimLog, Tower.All, Ai, SimSec, stateTime);
        var kut = Airfield.Kutaisi();
        kut.Side = 2;
        var sen = Airfield.FromDcs("Senaki", kut.X - 20 * NM, kut.Z, 20, new[] { ("09", 0.0, kut.X - 20 * NM, kut.Z, 2500.0) });
        sen.Side = 2;
        Telemetry At(double nm, double hdg = 0) => new(1500, 1400, 150, hdg, kut.X + nm * NM, kut.Z, 0, 0, 0);
        List<Tx> Take() { var l = new List<Tx>(); while (SayQueue.TryTake(out var t)) l.Add(t); return l; }
        var cmds = new List<string>();
        try
        {
            (SimLog, Fields) = (new(), new() { kut, sen });
            Tower.All = Fields;
            Take();
            // R47: Flight Following at Kutaisi, 12 NM away and Senaki closer -> Kutaisi stays; from 25 NM not; after logout pin released, then Senaki
            var p = new Pilot { Unit = "C1", Callsign = "Enfield 1-1", Gid = 6, Tel = At(-12, Math.PI) };
            var kt = TowerFor(p, kut);
            p.Active = kt;
            kt.OnTranscript("Kutaisi Approach, Enfield 1-1, request flight following", p.Tel, Array.Empty<Traffic>(), 1);
            var keep = Pick(p, null)?.F.Name;
            p.Tel = At(-26, Math.PI);
            var far = Pick(p, null)?.F.Name;
            (p.Tel, p.Pinned) = (At(-12, Math.PI), kt);
            kt.OnTranscript("Kutaisi Approach, Enfield 1-1, cancel flight following", p.Tel, Array.Empty<Traffic>(), 2);
            var rel = Pick(p, null)?.F.Name;
            if (keep != "Kutaisi" || far != "Senaki" || rel != "Senaki" || p.Pinned != null)
                throw new Exception($"R47 Pick: Flight Following {keep}, 26 NM {far}, nach der Abmeldung {rel} (Pin {p.Pinned?.F.Name})");
            Console.WriteLine("OK   R47: Flight Following behält Kutaisi bis zur Abmeldung (Senaki näher), höchstens 25 NM, Abmeldung löst den Pin");

            // N2: watcher per DCS group: lead with controller contact 2 min ago -> wingman with own flow silent; armed after 5 min without contact; helicopters silent; seconds since takeoff at the player
            SimSec = 6000;
            var ld = new Pilot { Unit = "W1", Callsign = "Enfield 2-1", Gid = 9, Tel = At(8, Math.PI), Airborne = Clock.AddMinutes(-5), AtcAt = Now() - 120 };
            var wg = new Pilot { Unit = "W2", Callsign = "Enfield 2-2", Gid = 9, Tel = At(4, Math.PI), Airborne = Clock.AddMinutes(-5) };
            var hp = new Pilot { Unit = "W3", Callsign = "Hawk 1-1", Gid = 10, Tel = At(4, Math.PI), Airborne = Clock.AddMinutes(-5), Heli = true };
            (Pilots["W1"], Pilots["W2"], Pilots["W3"]) = (ld, wg, hp);
            var wt = wg.Active = TowerFor(wg, kut);
            Prep(wg, wt, new() { ld, wg });
            bool lead2 = wt.Exempt;
            ld.AtcAt = Now() - 301;
            Prep(wg, wt, new() { ld, wg });
            bool lead5 = wt.Exempt;
            TowerFor(ld, kut).Following = true;   // Flow from before the reconnect: in formation it runs via the lead (not ticked), does not count
            ld.Lead = wg; Prep(wg, wt, new() { wg });
            bool joined = wt.Exempt;
            ld.Lead = null; Prep(wg, wt, new() { ld, wg });
            bool own = wt.Exempt;
            var ht = hp.Active = TowerFor(hp, kut);
            Prep(hp, ht, new() { hp });
            foreach (var u in new[] { "W1", "W2", "W3" }) Pilots.Remove(u);
            if (!lead2 || lead5 || joined || !own || !ht.Exempt || Math.Abs(wt.Aloft - 300) > 1)
                throw new Exception($"N2 Gruppe: Lead-Kontakt vor 2 min {lead2}, vor 5 min {lead5}, alter Ablauf im Verband {joined}, eigener {own}, Hubschrauber {ht.Exempt}, Aloft {wt.Aloft}");
            Console.WriteLine("OK   N2: Lotsenkontakt des Leads gilt für die Gruppe (5 min), alter Ablauf eines Wingman im Verband nicht, Hubschrauber still, Abheben je Spieler");

            // N3: pass-through of the lead (contact over 5 min ago) keeps the wingman's watcher silent, keeps Kutaisi (Senaki closer), players in the pattern hear it once
            var tl = new Pilot { Unit = "T1", Callsign = "Enfield 3-1", Gid = 11, Tel = At(-12), Airborne = Clock.AddMinutes(-5), AtcAt = Now() - 400, Type = "F-16C_50" };
            var tg = new Pilot { Unit = "T2", Callsign = "Enfield 3-2", Gid = 11, Tel = At(-8), Airborne = Clock.AddMinutes(-5), AtcAt = Now() - 400 };
            var tp = new Pilot { Unit = "T3", Callsign = "Enfield 4-1", Gid = 12, Tel = At(1, Math.PI) };
            (Pilots["T1"], Pilots["T2"], Pilots["T3"]) = (tl, tg, tp);
            var ttw = tl.Active = TowerFor(tl, kut);
            ttw.OnTranscript("Kutaisi Approach, Enfield 3-1, request zone transit northbound", tl.Tel, Array.Empty<Traffic>(), Now());
            var tgw = tg.Active = TowerFor(tg, kut);
            Prep(tg, tgw, new() { tl, tg });
            var keepT = Pick(tl, null)?.F.Name;
            (tp.Active = TowerFor(tp, kut)).OnTranscript("Kutaisi Tower, Enfield 4-1, inbound for landing", tp.Tel, Array.Empty<Traffic>(), Now());
            var notes = TransitNotes(tl, ttw, new() { tl, tg, tp }).Concat(TransitNotes(tl, ttw, new() { tl, tg, tp })).ToList();
            foreach (var u in new[] { "T1", "T2", "T3" }) Pilots.Remove(u);
            if (!tgw.Exempt || keepT != "Kutaisi" || notes.Count != 1 || notes[0].Gid != 12 || notes[0].Role != "Tower" || notes[0].Text != "Enfield four one, traffic, Viper crossing overhead northbound, 3000 feet.")
                throw new Exception($"N3 Durchflug: Wingman still {tgw.Exempt}, Platz {keepT}, Verkehrsinfo {string.Join(" | ", notes.Select(x => $"{x.Gid}:{x.Text}"))}");
            Console.WriteLine("OK   N3: Durchflug des Leads gilt für die Gruppe als Kontakt, Kutaisi bleibt aktiv, Platzrunde hört ihn einmal");

            // N51: unknown until the first request (tower watcher); carrier without check-in: call once, after 60 s under 2 NM a second time, then silent; departing, checked in,
            // hostile ship, AirspaceWatch off: silent; armed again only over 10 NM; after a request with callsign
            var (boats0, watch0, sky0, sun0, clock0) = (Carrier.Boats, Cfg.AirspaceWatch, Carrier.Sky, Carrier.Sun, Carrier.Clock);
            (Carrier.Sky, Carrier.Sun, Carrier.Clock) = ((false, 0, 80000), double.NaN, 10 * 3600);   // R249: Case I by day
            try
            {
                double bx = kut.X + 60 * NM;
                Carrier.Boats = new() { new("CVN-75", "CV", "CVN_75", bx, kut.Z, 0, 0, 2, 127.5, "75X", "11"), new("Kuznetsov", "RU", "KUZNECOW", bx, kut.Z + 0.5 * NM, 0, 0, 1, 0, "", "") };
                Telemetry B(double nm, double hdg = 0) => new(457, 450, 150, hdg, bx + nm * NM, kut.Z, 0, 0, 0);   // 1500 ft
                var bp = new Pilot { Unit = "B1", Callsign = "Enfield 6-1", Gid = 13, Tel = At(4, Math.PI), Airborne = Clock.AddMinutes(-5) };
                Pilots["B1"] = bp;
                var uw = bp.Active = TowerFor(bp, kut);
                Prep(bp, uw, new() { bp });
                bool unk = uw.Unknown;
                bp.Tel = B(-4);
                var b1 = BoatWatch(bp);
                var b1b = BoatWatch(bp);
                var bq = new Pilot { Unit = "B2", Callsign = "Enfield 6-2", Gid = 13, Tel = B(-20), Airborne = Clock.AddMinutes(-5) };   // separated flight over 10 NM: no new call per tick
                Pilots["B2"] = bq;
                BoatWatch(bq);
                var b1c = BoatWatch(bp);
                Pilots.Remove("B2");
                SimSec += 61; bp.Tel = B(-1.5);
                var b2 = BoatWatch(bp);
                SimSec += 61;
                var b3 = BoatWatch(bp);
                bp.Tel = B(-11); BoatWatch(bp);
                bp.Tel = new Telemetry(610, 610, 150, Math.PI / 2, bx, kut.Z + 4 * NM, 0, 0, 0);   // N51b: lateral 4 NM, 2000 ft, flying away (astern low now counts as recovery sector)
                var away = BoatWatch(bp);
                bp.Tel = new Telemetry(305, 305, 150, Math.PI / 2, bx - 8 * NM, kut.Z, 0, 0, 0);   // N51b: 8 NM astern, 1000 ft, abeam: recovery sector
                var ast = BoatWatch(bp);
                bp.Tel = B(-11); BoatWatch(bp);
                bp.Boat.Stage = 1; bp.Tel = B(-4);
                var inn = BoatWatch(bp);
                bp.Boat.Stage = 0; Cfg.AirspaceWatch = false;
                var off = BoatWatch(bp);
                Cfg.AirspaceWatch = true;
                uw.Following = true;   // Flight following: contact
                var ff = BoatWatch(bp);
                uw.Following = false;
                Request(bp, "Kutaisi Tower, Enfield 6-1, request zone transit", "T", false);
                Take();
                Prep(bp, uw, new() { bp });
                bool unk2 = uw.Unknown;
                SimSec += 301;
                var b4 = BoatWatch(bp);
                SimSec += 61; bp.Tel = B(0) with { Agl = 0 };   // landed on deck, catapult launch: no second call
                var deck = BoatWatch(bp);
                bp.Tel = B(1);
                deck.AddRange(BoatWatch(bp));
                Pilots.Remove("B1");
                var g1 = b1.Count == 1 ? Flights.OnGuard(b1[0].Text, b1[0].Station, 127.5) : "";
                // R249: Case I/II Tower (Air Boss), Case III Approach; unknowns with the warship's warning formula, own "carrier control zone … say intentions"
                // Review l7: warning formula on Guard without appended "Contact Tower …" (station without airfield, reply on Guard)
                Carrier.Sky = (true, 200, 20000);
                var c3 = Carrier.WarshipCall(Carrier.Boats[0], B(-4), "Enfield six one", false, 0);
                if (!unk || unk2 || b1.Count != 1 || !Regex.IsMatch(b1[0].Text, @"^Unidentified aircraft 4 miles south of the carrier, heading \w+ \w+ \w+, 1500 feet, this is US Navy warship, you are approaching a US Navy warship, turn left heading \w+ \w+ zero, remain clear 5 miles, identify yourself and state your intentions\.$")
                    || ast.Count != 1 || !Regex.IsMatch(ast[0].Text, @"^Unidentified aircraft 8 miles south of the carrier, heading \w+ \w+ \w+, 1000 feet, this is US Navy warship, you are approaching a US Navy warship, turn right heading \w+ \w+ zero, remain clear 5 miles, identify yourself and state your intentions\.$")
                    || b1[0].Freqs?[0] != 127.5 || b1[0].Station != "Tower" || !g1.Contains("this is US Navy warship, you are approaching") || g1.Contains("Contact") || b1b.Count != 0 || b1c.Count != 0 || b2.Count != 1 || !b2[0].Text.EndsWith(", this is US Navy warship, remain clear 5 miles, identify yourself and state your intentions.")
                    || b3.Count != 0 || away.Count != 0 || inn.Count != 0 || off.Count != 0 || ff.Count != 0 || deck.Count != 0 || b4.Count != 1 || !Regex.IsMatch(b4[0].Text, @"^Enfield six one, Tower, you are inside the carrier control zone, turn left heading \w+ \w+ zero, remain clear 5 miles, say intentions\.$")
                    || c3.Station != "Approach" || !c3.Text.StartsWith("Enfield six one, Approach, you are inside the carrier control zone, "))
                    throw new Exception($"N51: unbekannt {unk}/{unk2}, {string.Join(" | ", b1.Concat(b1b).Concat(ast).Concat(b1c).Concat(b2).Concat(b3).Concat(away).Concat(inn).Concat(off).Concat(ff).Concat(b4).Concat(deck).Select(x => x.Text))} / {g1} / {c3.Text}");
            }
            finally { (Carrier.Boats, Cfg.AirspaceWatch, Carrier.Sky, Carrier.Sun, Carrier.Clock) = (boats0, watch0, sky0, sun0, clock0); boatWarn.Clear(); Pilots.Remove("B1"); Pilots.Remove("B2"); }
            Console.WriteLine("OK   N51: unbekannt bis zur ersten Anfrage, Träger ohne Check-in einmal mit Umleitung (zweiter Ruf unter 2 NM nach 60 s), achtern tief im Recovery-Sektor, still seitlich abfliegend/eingecheckt/Flight Following/feindlich/Wächter aus/getrennte Rotte/Katapultstart, Rearm über 10 NM, danach mit Rufzeichen");

            // R293: MAYDAY of the flight member in formation: own flow, reply to his callsign, emergency at him (not at the lead), FormFlights does not attach him again
            Take();
            var rl = new Pilot { Unit = "R1", Callsign = "Enfield 5-1", Gid = 13, Tel = At(12, Math.PI) };
            var rg = new Pilot { Unit = "R2", Callsign = "Enfield 5-2", Gid = 13, Tel = At(12.2, Math.PI) };
            (Pilots["R1"], Pilots["R2"]) = (rl, rg);
            FormFlights(new() { rl, rg });
            bool inFlight = rg.Lead == rl;
            Request(rl, "Kutaisi Approach, Enfield 5-1 flight, inbound for landing", "T", false);
            Take();
            Request(rg, "Mayday mayday mayday, Kutaisi Approach, Enfield 5-2, engine failure", "T", false);
            var rmay = Take().Where(t => t.Role != "Info").ToList();
            FormFlights(new() { rl, rg });
            Pilots.Remove("R1"); Pilots.Remove("R2");
            if (!inFlight || rg.Lead != null || rmay.Count == 0 || !rmay[0].Text.StartsWith("Enfield five two, Kutaisi Approach, roger mayday") || rg.Active?.Emergency != true || rl.Active?.Emergency == true || rl.FlightSize != 1)
                throw new Exception($"R293 Notruf Rottenflieger: im Flug {inFlight}, Lead {rg.Lead?.Callsign}, {string.Join(" | ", rmay.Select(t => t.Text))}, Notfall 5-2 {rg.Active?.Emergency} 5-1 {rl.Active?.Emergency}");
            Console.WriteLine($"OK   R293: MAYDAY des Rottenfliegers -> eigener Ablauf ({rmay[0].Text[..48]}…), Lead ohne Notfall, bleibt getrennt");

            // N40: Kutaisi goes red on approach -> diversion to Senaki on the old frequency (side Blue), order free, Senaki expects him
            Take();
            var q = new Pilot { Unit = "C2", Callsign = "Enfield 1-2", Gid = 7, Tel = At(10, Math.PI) };
            Request(q, "Kutaisi Approach, Enfield 1-2, inbound for landing", "T", false);
            UpdateQueues(new() { q });
            var inQ = ArrQueue.TryGetValue("Kutaisi", out var kq) && kq.Contains("p:C2");
            Take();
            FieldSide("Kutaisi", 1);
            var cap = Captured(q).ToList();
            if (Pick(q, null) is { } pt) Switch(q, pt);
            UpdateQueues(new() { q });
            var outQ = !ArrQueue.TryGetValue("Kutaisi", out var kq2) || !kq2.Contains("p:C2");
            Request(q, "Senaki Approach, Enfield 1-2", "T", false);
            var call = Take().Where(t => t.Role != "Info").ToList();
            if (!inQ || cap.Count != 1 || !cap[0].Text.StartsWith("Enfield one two, Kutaisi Approach, Kutaisi is closed, divert Senaki, fly heading") || !cap[0].Text.Contains("contact Senaki Approach")
                || cap[0].Side != 2 || q.Active?.F != sen || q.Pinned?.F != sen || !outQ || call.Count == 0 || !call[^1].Text.Contains("identified") || call[^1].Text.Contains("Station calling"))
                throw new Exception($"N40 Umleitung: {inQ}/{outQ} {string.Join(" | ", cap.Select(t => $"{t.Side}:{t.Text}"))} / {q.Active?.F.Name} / {string.Join(" | ", call.Select(t => t.Text))}");
            Console.WriteLine($"OK   N40: Platz erobert im Anflug -> \"{cap[0].Text}\", Senaki erwartet den Flug ({call[^1].Text[..40]}…)");

            // N40 AI: approach to captured Kutaisi -> not in the sequence, DIVERT once, no radio from Kutaisi
            (SimSec, stateTime, AiHook) = (5000, Clock, cmds.Add);
            aiStage.Clear(); aiSent.Clear(); ArrQueue.Clear();
            aiStage["D1"] = "inbound";
            Ai = new() { new Traffic("D1".GetHashCode(), "F-16C_50", kut.X + 6 * NM, kut.Z, 1000, Math.PI, 120, "D1", "arr:Kutaisi", true, 2) };
            var aq = new List<Tx>();
            UpdateQueues(new());
            ControlAi(new(), aq);
            ControlAi(new(), aq);
            if (ArrQueue.ContainsKey("Kutaisi") || cmds.Count(c => c == "DIVERT;D1") != 1 || aq.Count != 0 || aiStage.ContainsKey("D1"))
                throw new Exception($"N40 KI: {string.Join(",", cmds)} / {string.Join(" | ", aq.Select(t => t.Text))}");
            Console.WriteLine("OK   N40 KI: Anflug zum eroberten Platz nicht in der Reihenfolge, einmal DIVERT, kein Funk des feindlichen Platzes");
        }
        finally { (SimLog, Fields, Tower.All, Ai, SimSec, stateTime, AiHook) = (log0, fs0, all0, ai0, ss0, st0, null); aiStage.Clear(); aiSent.Clear(); ArrQueue.Clear(); Take(); }
    }

    /// Landing sequence: three AI approaches by distance, low fuel moves to the front.
    static void QueueTest()
    {
        var fld = Airfield.Kutaisi();
        Fields = new() { fld };
        Traffic Arr(string g, double nm) => new(g.GetHashCode(), "F-16C_50", fld.X + nm * NM, fld.Z, 1500, 0, 150, g, "arr:Kutaisi");
        Ai = new() { Arr("A3", 18), Arr("A1", 10), Arr("A2", 14) };
        UpdateQueues(new());
        var q = string.Join(",", ArrQueue["Kutaisi"]);
        if (q != "ai:A1,ai:A2,ai:A3") throw new Exception("Reihenfolge: " + q);
        AiFuel["A3"] = 0.15;   // R298: minimum fuel, no priority
        UpdateQueues(new());
        if (string.Join(",", ArrQueue["Kutaisi"]) != "ai:A1,ai:A2,ai:A3") throw new Exception("KI mit minimum fuel vorgezogen (R298): " + string.Join(",", ArrQueue["Kutaisi"]));
        AiFuel["A3"] = 0.08;   // MAYDAY FUEL
        UpdateQueues(new());
        q = string.Join(",", ArrQueue["Kutaisi"]);
        if (q != "ai:A3,ai:A1,ai:A2") throw new Exception("Spritmangel vorziehen: " + q);
        Console.WriteLine($"OK   Landereihenfolge {q} (A3 Spritmangel vorgezogen)");
        // A29: player with empty tank does not move up silently, not even after "minimum fuel" (emergency only)
        var pel = new Telemetry(1500, 1500, 100, 0, fld.X + 12 * NM, fld.Z, 0, 0, 0);
        var twP = new Tower(fld, "Enfield 1-1");
        twP.Load(new Tower.State(Phase.Inbound, "25", null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, true, false, null, null, 0, true), pel, 0);
        var plP = new Pilot { Unit = "P1", Fuel = 0.05, Active = twP, Tel = pel };
        UpdateQueues(new() { plP });
        var qp = ArrQueue["Kutaisi"];
        if (qp.IndexOf("p:P1") < qp.IndexOf("ai:A1")) throw new Exception("Spieler ohne Meldung vorgezogen: " + string.Join(",", qp));
        twP.OnTranscript("Kutaisi Approach, Enfield 1-1, minimum fuel", pel, Array.Empty<Traffic>(), 1);
        UpdateQueues(new() { plP });
        qp = ArrQueue["Kutaisi"];
        if (qp.IndexOf("p:P1") < qp.IndexOf("ai:A1")) throw new Exception("Spieler mit minimum fuel vorgezogen (kein Vorrang, AIM 5-5-15): " + string.Join(",", qp));
        twP.OnTranscript("Kutaisi Approach, Enfield 1-1, mayday mayday mayday fuel", pel, Array.Empty<Traffic>(), 2);
        UpdateQueues(new() { plP });
        qp = ArrQueue["Kutaisi"];
        if (qp.IndexOf("p:P1") > qp.IndexOf("ai:A1")) throw new Exception("Spieler mit Notfall nicht vorgezogen: " + string.Join(",", qp));
        // R37: MAYDAY in the air -> other players at the airfield hold (with the emergency's distance), PAN priority only in the sequence
        {
            var twO = new Tower(fld, "Enfield 1-2");
            var plO = new Pilot { Unit = "P2", Active = twO, Tel = pel with { X = fld.X - 20 * NM } };
            Prep(plO, twO, new() { plP, plO });
            var may = (twO.OtherEmergency, Math.Round(twO.EmergencyNm));
            twP.OnTranscript("Kutaisi Approach, Enfield 1-1, pan pan, pan pan, pan pan, hydraulic failure", pel, Array.Empty<Traffic>(), 3);
            Prep(plO, twO, new() { plP, plO });
            UpdateQueues(new() { plP });
            qp = ArrQueue["Kutaisi"];
            if (may != (true, 12) || twO.OtherEmergency || qp.IndexOf("p:P1") > qp.IndexOf("ai:A1"))
                throw new Exception($"Mayday/Pan für andere: Mayday {may}, Pan hält andere {twO.OtherEmergency}, Reihenfolge {string.Join(",", qp)}");
            // Emergency: nearest own airfield with runway from 1800 m (foreign closer and own with short runway not), call with distress signal, position, altitude, heading (A30)
            var fs0 = Fields;
            Airfield Fld(string name, double nm, int side, double len)
            {
                var f = Airfield.Kutaisi();
                (f.Name, f.X, f.Side, f.Ends) = (name, fld.X + nm * NM, side, f.Ends.Select(e => e with { Len = len }).ToList());
                return f;
            }
            Fields = new() { Fld("Red", 5, 1, 2500), Fld("Short", 10, 2, 1000), Fld("Blue", 30, 2, 2500), Fld("Neutral", 40, 0, 2500) };
            var ef = EmergencyField(pel with { X = fld.X }, 2)?.Name;
            Fields = fs0;
            var call = PilotCall(fld, "Approach", new Pilot { Callsign = "Enfield 1-1", Tel = plP.Tel, Type = "F-16C_50", Fuel = 0.3 }, "mayday mayday mayday, engine failure, request immediate landing");   // R244: type, fuel, persons
            if (ef != "Blue" || !Regex.IsMatch(call, @"^MAYDAY MAYDAY MAYDAY, Kutaisi Approach, Enfield 1 1, Viper, engine failure, 12 miles north of Kutaisi, \d+ feet, heading \d{3}, 25 minutes fuel, one soul on board, request immediate landing\.$"))
                throw new Exception($"Notfall-Platz {ef} / Spruch {call}");
            var gcall = PilotCall(fld, "Ground", new Pilot { Callsign = "Enfield 1-1", Tel = new Telemetry(fld.Elev, 0, 0, 0, fld.X, fld.Z, 0, 0, 0), Type = "F-16C_50" }, "mayday mayday mayday, engine failure, request immediate landing");
            if (gcall != "MAYDAY MAYDAY MAYDAY, Kutaisi Ground, Enfield 1 1, engine failure, shutting down.") throw new Exception("R300 Boden-Notruf: " + gcall);
            Console.WriteLine($"OK   Notfall: MAYDAY hält andere ({may.Item2} NM), PAN nur Vorrang, nächster eigener Platz {ef}, \"{call}\"");
            // R275: first call to Departure/Approach with altitude or position and altitude (FAA JO 7110.65 5-2-17, AIM 4-1-13)
            var pc1 = PilotCall(fld, "Departure", new Pilot { Callsign = "Enfield 1-1", Tel = plP.Tel }, "airborne, climbing"); var pc2 = PilotCall(fld, "Approach", new Pilot { Callsign = "Enfield 1-1", Tel = plP.Tel }, "inbound for landing");
            if (!Regex.IsMatch(pc1, @"^Kutaisi Departure, Enfield 1 1, airborne, passing \d+ feet, climbing\.$") || !Regex.IsMatch(pc2, @"^Kutaisi Approach, Enfield 1 1, 12 miles north, \d+ feet, inbound for landing\.$")) throw new Exception($"Erstanruf mit Höhe: {pc1} / {pc2}");
            Console.WriteLine($"OK   Erstanruf mit Höhe: \"{pc1}\" / \"{pc2}\"");
        }
        // Newcomers by distance before waiting aircraft with holding point further out, never before approaching ones; then fixed order
        AiFuel.Clear(); ArrQueue.Clear();
        Ai = new() { Arr("A1", 10), Arr("A2", 14), Arr("A3", 18) };
        UpdateQueues(new());
        Holds["ai:A3"] = (fld.X + 18 * NM, fld.Z, 4000);   // A3 circles
        Ai.Add(Arr("A4", 16));
        UpdateQueues(new());
        var q1 = string.Join(",", ArrQueue["Kutaisi"]);
        Holds["ai:A4"] = (fld.X + 20 * NM, fld.Z, 3000);   // A4 circles further out than A3: stays ahead anyway
        Ai.Add(Arr("A5", 5));   // closer than A1, A2 (already approaching): behind them
        UpdateQueues(new());
        var q2 = string.Join(",", ArrQueue["Kutaisi"]);
        Holds.Clear();
        if (q1 != "ai:A1,ai:A2,ai:A4,ai:A3" || q2 != "ai:A1,ai:A2,ai:A5,ai:A4,ai:A3") throw new Exception($"Reihenfolge: {q1} / {q2}");
        Console.WriteLine($"OK   Reihenfolge: {q1}, dann {q2}");
        // R46: player predecessor vectored to final: no. 2 far enough behind -> radar vectoring instead of holding, close behind stays in holding; Heavy detected
        {
            Ai = new(); ArrQueue.Clear();
            Telemetry At(double nm) => new(1500, 1500 - fld.Elev, 128, 0, fld.X + nm * NM, fld.Z, 0, 0, 0);
            var twL = new Tower(fld, "Enfield 1-1");
            var plL = new Pilot { Unit = "L1", Type = "KC-135", Active = twL, Tel = At(12) };   // without break: instrument approach
            var twF = new Tower(fld, "Enfield 1-2");
            var plF = new Pilot { Unit = "F1", Type = "FA-18C_hornet", Active = twF, Tel = At(45) };
            var two = new List<Pilot> { plL, plF };
            Prep(plL, twL, two); twL.Tick(plL.Tel, Array.Empty<Traffic>(), 0);
            twL.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", plL.Tel, Array.Empty<Traffic>(), 1);
            UpdateQueues(two);
            Prep(plF, twF, two);
            var (spFar, ahead, hv) = (twF.Spaced, twF.AheadR, twF.AheadHeavy);
            plF.Tel = At(13);
            Prep(plF, twF, two);
            var spNear = twF.Spaced;
            plF.Tel = At(45);
            Prep(plF, twF, two); twF.Tick(plF.Tel, Array.Empty<Traffic>(), 0);
            var inb = twF.OnTranscript("Kutaisi Approach, Enfield 1-2, inbound for landing", plF.Tel, Array.Empty<Traffic>(), 1)[0].Text;
            ArrQueue.Clear();
            if (!spFar || spNear || ahead < 10 * NM || !hv || inb.Contains("hold") || !twF.Vectoring)
                throw new Exception($"R46 Staffelung hinter Spieler: 45 NM gestaffelt {spFar}, 13 NM {spNear}, Vordermann {ahead / NM:0.0} NM Heavy {hv}: {inb}");
            Console.WriteLine($"OK   R46 Nr. 2 hinter Spieler im Anflug ({ahead / NM:0} NM Weg, Heavy): aus 45 NM Radarführung, aus 13 NM Warteschleife");
        }
        // own AI flight member (same DCS group ID) is not traffic ("give way to the FA-18C" while taxiing)
        var st = stateTime; stateTime = Clock;
        Ai = new() { Arr("Ford", 0.01), Arr("Other", 0.02) }; AiGid["Ford"] = 7;
        var tf = TrafficFor(new Pilot { Gid = 7 });
        stateTime = st; AiGid.Clear();
        if (tf.Count != 1 || tf[0].Group != "Other") throw new Exception("KI-Rottenflieger als Verkehr: " + string.Join(",", tf.Select(a => a.Group)));
        Console.WriteLine("OK   eigener KI-Rottenflieger ist kein Verkehr");
        // A121: tanker turn to everyone (Gid 0), otherwise to the player's group
        var (txAll, txOwn) = (OpsTx(new Pilot { Gid = 7 }, new("Tanker", "Texaco", "Texaco, turning right.", All: true)), OpsTx(new Pilot { Gid = 7 }, new("Tanker", "Texaco", "x")));
        if (txAll.Gid != 0 || txOwn.Gid != 7) throw new Exception($"Tanker-Kurve an alle: Gid {txAll.Gid} / {txOwn.Gid}");
        Console.WriteLine("OK   Tanker-Kurve an alle");
        // A114/N46: threat behind a threat (~8 s) still spoken; "radar contact" after check-in (takeoff, "splash one") no second check-in, after check-out again
        {
            var ap = new Pilot();
            Ops.Call Aw(string s) => new("AWACS", "Overlord", $"Enfield one one, Overlord, {s}.");
            foreach (var s in new[] { "radar contact, picture clean", "radar contact, picture clean", "threat, group BRAA zero nine zero, 20 miles, hot, Flanker", "radar contact, picture clean", "copy checking out, good day", "radar contact, picture clean" }) AwacsNote(ap, Aw(s));
            var rows = string.Join(" | ", ap.Ops.Debrief.Select(d => d[16..]));
            if (OpsTx(ap, Aw("threat, group BRAA zero nine zero, 20 miles, hot, Flanker")).MaxAge < 10 || rows != "check-in | threat BRAA zero nine zero, 20 miles, hot, Flanker | check-out | check-in")
                throw new Exception($"AWACS MaxAge {OpsTx(ap, Aw("threat, group BRAA zero nine zero, 20 miles, hot, Flanker")).MaxAge} / Debriefing {rows}");
        }
        Console.WriteLine("OK   AWACS Threat-Alter und ein Check-in");
        // R15: Ops.MagVar follows the map (Caucasus 6, otherwise mean of the airfields without the Kutaisi placeholder)
        {
            var th = Airfield.Theatre;
            var rw13 = new[] { ("13", -131 * Math.PI / 180, 0.0, 0.0, 2400.0) };
            Airfield.Theatre = "Caucasus"; var mc = new[] { Airfield.Kutaisi(), Airfield.FromDcs("Batumi", 0, 0, 10, rw13) };
            Airfield.Theatre = "Syria"; var ms = new[] { Airfield.Kutaisi(), Airfield.FromDcs("Hatay", 0, 0, 10, rw13), Airfield.FromDcs("Aleppo", 0, 0, 10, rw13) };
            ms[1].MagVar = 2; ms[2].MagVar = 4; // with the Kutaisi placeholder it would be (6+2+4)/3 = 4
            var (vc, vs, v0) = (MapMagVar(mc), MapMagVar(ms), MapMagVar(new[] { Airfield.Kutaisi() }));
            Airfield.Theatre = th;
            if (vc != 6 || vs != 3 || v0 != 6) throw new Exception($"Missweisung je Karte: {vc} {vs} {v0}");
            Console.WriteLine("OK   Ops.MagVar je Karte: Kaukasus 6, Syrien 3 (Mittel 2/4, Kutaisi-Platzhalter nicht mitgezählt)");
        }
        {   // Pace per voice: every bundled voice is calibrated (new voice: measure duration, enter factor 11.5 s / duration)
            var dir = Path.Combine(Root, "models");
            var miss = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.onnx").Select(Path.GetFileNameWithoutExtension).Where(m => !VoicePace.ContainsKey(m!)).ToList() : new();
            if (miss.Count > 0) throw new Exception("Stimme ohne Tempo-Faktor (VoicePace): " + string.Join(", ", miss));
            Console.WriteLine("OK   Tempo je Stimme eingemessen");
        }
        Carrier.Boats = new() { new("CVN-75", "CV", "CVN_75", 0, 0, 0, 0, 2, 127.5, "75X", "11"), new("Kuznetsov", "RU", "KUZNECOW", 0, 0, 0, 0, 1, 0, "", "") };
        var np = string.Join(",", NearPlaces(null, 2));
        var ci = CheckIn(new Pilot { Type = "FA-18C_hornet", Fuel = 0.6, Tel = new Telemetry(18000 * 0.3048, 0, 150, 0, 0, 35 * NM, 0, 0, 0) });
        // R24: no land tower on the deck: "Ground: request startup" from the wheel -> hint from the carrier only, no airfield, no Ground; Enter without suggestion
        var dp = new Pilot { Unit = "U9", Callsign = "Enfield 3-1", Type = "FA-18C_hornet", Tel = new Telemetry(20, 0, 10, 0, 50, 0, 0, 0, 0) };
        dp.Boat.Tick(MeOf(dp), 0);
        while (SayQueue.TryTake(out _)) { }
        Request(dp, "Ground: request startup", "RAD", true);
        var dtx = new List<Tx>();
        while (SayQueue.TryTake(out var dt)) dtx.Add(dt);
        var dsl = dp.Boat.Suggest(dp.Tel);
        // Case III after the catapult: radio wheel "Approach: airborne, climbing" goes to the carrier's Departure, not to the land Approach
        Carrier.Sky = (true, 200, 20000);
        dp.Tel = new Telemetry(150, 150, 120, 0, 2 * NM, 0, 0, 0, 0);
        dp.Boat.Tick(MeOf(dp), 1);
        Request(dp, "Approach: airborne, climbing", "RAD", true);
        Carrier.Sky = (false, 0, 80000);
        var atx = new List<Tx>();
        while (SayQueue.TryTake(out var at)) atx.Add(at);
        dp.Boat.Leave();
        Carrier.Boats = new();
        if (dp.Active != null || dsl != null || !dtx.Any(t => t.Role == "Info" && t.Text.Contains("Deckcrew")) || dtx.Any(t => !t.Pilot && t.Role != "Info")
            || !atx.Any(t => t.Role == "Carrier" && t.Text.Contains("Departure, radar contact")))
            throw new Exception($"R24 Deck: {dp.Active?.F.Name} {string.Join(" | ", dtx.Concat(atx).Select(t => $"{t.Role}:{t.Text}"))}");
        Console.WriteLine("OK   R24: auf dem Deck kein Land-Tower (Funkrad \"Ground: request startup\" -> Hinweis Deckcrew)");
        if (ci != $"checking in, mother's {90 - Ops.MagVar:000} for 35, angels 18, state 6.5") throw new Exception("Check-in Funkrad: " + ci);
        Console.WriteLine("OK   Funkrad-Check-in: " + ci);
        // R28: ball call of the pilot voice with type and fuel, without station in front; station only at check-in
        var bcs = CarrierCall("Paddles", "Enfield 1-1", BallCall(new Pilot { Type = "FA-18C_hornet", Fuel = 0.48 }, "ball")) + " | " + CarrierCall("Marshal", "Enfield 1-1", "checking in, angels 18")
                  + " | " + BallCall(new Pilot { Type = "F-16C_50" }, "Clara");
        if (bcs != "Enfield 1 1, Hornet ball, 5.2. | Marshal, Enfield 1 1, checking in, angels 18. | F-16C Clara") throw new Exception("Ball-Call Pilotenstimme: " + bcs);
        Console.WriteLine("OK   R28 Ball-Call: " + bcs);
        // Save session: holding at the airfield, carrier stack, AWACS, debriefing -> new app takes over; 5 NM away (mission restart) not
        var (sf, sp) = (SessionFile, new Pilot { Unit = "U1", Callsign = "Ford 5-1", Tel = new Telemetry(1500, 1500, 100, 0, 1000, 2000, 0, 0, 0) });
        SessionFile = Path.Combine(Path.GetTempPath(), "DcsAtc-Session-test.json");
        sp.Active = TowerFor(sp, Fields[0]);
        sp.Active.Load(new Tower.State(Phase.Inbound, "25", null, false, true, 4000, new[] { 1.0, 2.0 }, "CRP", false, true, false, false, null, 0, 0, null, true, false, null, null, 1, true), sp.Tel, 0);
        sp.Boat.Load(new Carrier.State(1, 1, 3, 500, "CVX", "Ford five one", 0, false, false));
        sp.Ops.Debrief.Add("Träger LSO: (OK) 3-wire");
        Pilots["U1"] = sp; savedAt = -99; SaveSession(); Pilots.Remove("U1"); sp.Boat.Leave(); saved = null;
        var (rp, far) = (new Pilot { Unit = "U1", Callsign = "Ford 5-1", Tel = sp.Tel with { X = 1500 } }, new Pilot { Unit = "U1", Tel = sp.Tel with { X = 5 * NM } });
        RestoreSession(far); saved = null;
        RestoreSession(rp); saved = null;
        File.Delete(SessionFile); SessionFile = sf;
        bool restored = rp.Active?.Phase == Phase.Inbound && rp.Active.HoldInfo?.Ft == 4000 && rp.Active.Following && rp.Boat.Stage == 1 && rp.Ops.Debrief.Count == 1;
        var vt = new Tower(Fields[0], "Ford 5-1");   // Radar vectoring was running (not in holding): re-plan instead of radio silence
        vt.Load(new Tower.State(Phase.Inbound, "25", null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, true, false, null, null, 0, true, Vectoring: true),
                new Telemetry(1500, 1200, 120, Math.PI, Fields[0].X + 15 * NM, Fields[0].Z, 0, 0, 0), 0);
        restored &= vt.Vectoring;
        rp.Boat.Leave();
        if (!restored || far.Active != null || far.Boat.Stage != 0) throw new Exception($"Sitzung: {rp.Active?.Phase}/{rp.Boat.Stage}/{rp.Ops.Debrief.Count}, weit weg {far.Active?.Phase}");
        Console.WriteLine("OK   Sitzung: Phase, Holding, Träger-Stack, Debriefing nach Neustart übernommen; Missions-Neustart nicht");
        if (np != "CVN-75,Kutaisi") throw new Exception("Platz wählen mit Träger: " + np);
        Console.WriteLine("OK   Platz wählen: eigener Träger in der Liste");
        if (AoaDeg(8.1) != 8.1 || Math.Abs(AoaDeg(0.1414) - 8.1) > 0.01) throw new Exception("AoA Grad/rad");
        Console.WriteLine("OK   AoA: 8.1 bleibt Grad, 0.1414 rad -> 8.1°");
        Ai = new(); AiFuel.Clear(); ArrQueue.Clear(); Fields = new();
        // A3: called airfield only as station (start of call, before Tower …), not "clearance to Batumi"/"contact …"; on an airfield frequency this airfield applies
        var kut = Airfield.Kutaisi();
        Airfield Af(string name, double nm) => new() { Name = name, X = kut.X + nm * NM, Z = kut.Z };
        var (bat, kc, kp) = (Af("Batumi", 50), Af("Krasnodar-Center", 300), Af("Krasnodar-Pashkovsky", 310));
        Fields = new() { kut, bat, kc, kp };
        var nearKp = new Telemetry(0, 0, 0, 0, kut.X + 320 * NM, kut.Z, 0, 0, 0);
        var called = new[] { Called("Ground, Enfield 1-1, request IFR clearance to Batumi", nearKp, null), Called("Contact Kutaisi Approach two six six decimal five, Enfield 1-1", nearKp, null),
                             Called("Kutaisi Tower, Enfield 1-1, ready for departure", nearKp, bat), Called("Tower: Batumi Tower, Enfield 1-1", nearKp, null),
                             Called("Enfield 1-1, Krasnodar Tower, inbound", nearKp, kc), Called("Krasnodar Tower, Enfield 1-1", nearKp, null), Called("Krasnodar Center Approach, Enfield 1-1", nearKp, null) };
        SimLog = new();   // Request without log file
        var pk = new Pilot { Unit = "U9", Callsign = "Enfield 1-1", Tel = new Telemetry(kut.Elev, 0, 0, 0, kut.X, kut.Z, 0, 0, 0) };
        Request(pk, "Ground, Enfield 1-1, request IFR clearance to Batumi", "T", false);
        var clr = SayQueue.TryTake(out var tc) ? tc.Text : "";
        bool stays = pk.Active?.F == kut;
        Request(pk, "Batumi Tower, Enfield 1-1, ready for departure", "T", false, kut);   // on the Kutaisi frequency
        var hint = SayQueue.TryTake(out tc) ? tc.Text : "";
        stays &= pk.Active?.F == kut && pk.Pinned?.F == kut;
        while (SayQueue.TryTake(out _)) { }
        (SimLog, Fields) = (null, new());
        if (called[0] != null || called[1] != null || called[2] != kut || called[3] != bat || called[4] != kc || called[5] != kp || called[6] != kc || !stays
            || !clr.StartsWith("Enfield one one, Kutaisi Ground, cleared") || hint != "Enfield one one, this is Kutaisi Tower, you are on Kutaisi frequency, contact Batumi Tower two six five decimal zero.")
            throw new Exception($"Gerufener Platz: {string.Join(",", called.Select(f => f?.Name ?? "-"))} / {pk.Active?.F.Name} / {clr} / {hint}");
        Console.WriteLine("OK   Gerufener Platz: nur als Station; Clearance nach Batumi antwortet Kutaisi, Batumi auf Kutaisi-Frequenz -> Frequenzhinweis");
        // Whole multi-part name (Golf, real data, --mptest crash): "Ras Al Khaimah Intl Approach" matched only "intl", the nearer Fujairah Intl replied with its runway 29
        var rak = Airfield.FromDcs("Ras Al Khaimah Intl", -62870.9, -30497.6, 21.6, new[] { ("35", 0.25816, -61624.5, -30795.6, 2562.0) });
        var fuj = Airfield.FromDcs("Fujairah Intl", -118114.8, 9254.5, 18.5, new[] { ("29", 1.17453, -117532.0, 7939.3, 2877.0) });
        Fields = new() { rak, fuj };
        var nearFuj = new Telemetry(3000, 3000, 150, 0, rak.X + 30 * NM * Math.Cos(110 * Math.PI / 180), rak.Z + 30 * NM * Math.Sin(110 * Math.PI / 180), 0, 0, 0);   // 21 NM from Fujairah
        var gulf = new[] { Called("Ras Al Khaimah Intl Approach, Colt 1-1, inbound for landing", nearFuj, null), Called("Ras Al Khaimah Approach, Colt 1-1", nearFuj, null),
                           Called("Fujairah Intl Approach, Colt 1-1", nearFuj, null), Called("Colt 1-1, contact Ras Al Khaimah Intl Approach", nearFuj, null) };
        if (gulf[0] != rak || gulf[1] != rak || gulf[2] != fuj || gulf[3] != null)
            throw new Exception("Gerufener Platz mit mehrteiligem Namen: " + string.Join(",", gulf.Select(f => f?.Name ?? "-")));
        Console.WriteLine("OK   Gerufener Platz: \"Ras Al Khaimah Intl Approach\" ist Ras Al Khaimah, nicht das nähere Fujairah Intl");
        // K6: whole call name (Radio.lua) beats name parts, "Krasnodar Tower" -> Center, although Pashkovsky is nearer; "Minvody Approach" (Called and Addressed)
        var mv = Airfield.FromDcs("Mineralnye Vody", kut.X + 330 * NM, kut.Z, 320, new[] { ("12", 0.0, kut.X + 330 * NM, kut.Z, 3000.0) });
        (kc.Callsign, kp.Callsign, mv.Callsign) = ("Krasnodar", "Pashkovsky", "Minvody");
        Fields = new() { kut, bat, kc, kp, mv };
        var k6 = new[] { Called("Krasnodar Tower, Enfield 1-1", nearKp, null), Called("Pashkovsky Tower, Enfield 1-1", nearKp, null), Called("Minvody Approach, Enfield 1-1", nearKp, null) };
        var sa = new Tower(mv, "Enfield 1-1").SayAgain("Minvody Approach, Enfield 1-1, inbound for landing")[0];
        Fields = new();
        if (k6[0] != kc || k6[1] != kp || k6[2] != mv || sa.Role != "Approach" || sa.Text != "Enfield one one, Minvody Approach, say again.")
            throw new Exception($"Rufname: {string.Join(",", k6.Select(f => f?.Name ?? "-"))} / {sa.Role}: {sa.Text}");
        Console.WriteLine("OK   Rufname: \"Krasnodar Tower\" -> Center, \"Pashkovsky Tower\" -> Pashkovsky, \"Minvody Approach\" -> Mineralnye Vody");
        // K2/K4: spoken airfield frequency: called (without SRS data), tuned (last called first, deselected does not count), otherwise VHF; request names it
        var kr = Airfield.Kutaisi(); (kr.Uhf, kr.Vhf) = (263, 134);
        var pf = new Pilot { Unit = "U8", Callsign = "Enfield 1-1", Id = 9, CalledOn = 263, Tel = new Telemetry(kr.Elev, 0, 0, 0, kr.Ramps[0].X, kr.Ramps[0].Z, 0, 0, 0) };
        var spk = new List<double> { SpokenFreq(kr, pf), SpokenFreq(kr, new Pilot()), SpokenFreq(new Airfield { Uhf = 263 }, new Pilot()), SpokenFreq(kut, pf), SpokenFreq(kr, pf, "AWACS") };
        Srs = new SrsListener(5002, Array.Empty<double>(), _ => false, _ => { });
        string Radios(string a, string b) => $$$"""{"MsgType":1,"Clients":[{"ClientGuid":"g","Name":"x","RadioInfo":{"unitId":9,"radios":[{"freq":{{{a}}}},{"freq":{{{b}}}}]}}]}""";
        Srs.OnServerMessage(Radios("134000000", "263000000"));
        spk.Add(SpokenFreq(kr, pf));
        pf.CalledOn = 251.5; spk.Add(SpokenFreq(kr, pf));
        Srs.OnServerMessage(Radios("251500000", "134000000"));
        pf.CalledOn = 263; spk.Add(SpokenFreq(kr, pf));
        Srs.OnServerMessage(Radios("261000000", "251500000")); spk.Add(SpokenFreq(kr, pf));   // SRS state still from the old airfield: the called one
        Srs = null;
        (SimLog, Fields) = (new(), new() { kr });
        Request(pf, "Kutaisi Tower, Enfield 1-1, request startup", "T", false, kr);
        var k2 = SayQueue.TryTake(out tc) ? tc.Text : "";
        while (SayQueue.TryTake(out _)) { }
        (SimLog, Fields) = (null, new());
        // R104: Ground and Tower on the same map frequency -> Ground replies directly (earlier "contact Kutaisi Ground two six three decimal zero"), the heard frequency arrives at the Tower
        if (!spk.SequenceEqual(new double[] { 263, 134, 263, 0, 0, 263, 134, 134, 263 }) || pf.Active?.HeardOn != 263 || !k2.Contains("Kutaisi Ground, start up approved") || k2.Contains("contact"))
            throw new Exception($"Gesprochene Frequenz: {string.Join(" ", spk)} / {pf.Active?.HeardOn} / {k2}");
        Console.WriteLine("OK   Gesprochene Frequenz: gerufen 263.0, sonst VHF 134.0, gerastet (abgewählte UHF zählt nicht, ohne gerastete Platzfrequenz die gerufene); Tower angerufen, Ground antwortet auf derselben Frequenz");
        // wrongfreq (forum log 0.9.4): ground requests on tanker/AWACS/carrier frequency (mission presets) -> no DCS tanker menu, no "say again", hint to Kutaisi Ground; real tanker request continues
        {
            var (wCk, wAi, wSs, wSt, wAh, wPb) = (Cfg.CommsKey, Ai, SimSec, stateTime, AiHook, Pilots.ToList());
            var menu = new List<string>();
            var wp = new Pilot { Unit = "U6", Callsign = "Enfield 1-1", Id = 6, Type = "F-14B", Tel = new Telemetry(kr.Elev, 0, 0, 0, kr.Ramps[0].X, kr.Ramps[0].Z, 0, 0, 0) };
            var wr = new List<string>(); var wTel = wp.Tel;
            try
            {
                Pilots.Clear(); Pilots[wp.Unit] = wp;
                (Cfg.CommsKey, SimSec, SimLog, Fields, AiHook) = (0x2B, 6000, new(), new() { kr }, menu.Add);
                stateTime = Clock;
                Ai = new() { new Traffic(77, "KC135MPRS", kr.X + 30 * NM, kr.Z, 6700, 0, 140, "Aerial-1", "", true, 2), new Traffic(78, "E-3A", kr.X - 60 * NM, kr.Z, 9000, 0, 150, "Overlord", "", true, 2) };
                foreach (var (to, f, said) in new[] { ("Tanker", 255.5, "Request startup."), ("AWACS", 251.5, "Liquid startup."), ("AWACS", 251.5, "Request taxi to runway."), ("Carrier", 127.5, "Request taxi to active."),
                                                      ("Tanker", 255.5, "Request radar vectors."), ("AWACS", 251.5, "Enfield 1-1, bingo fuel, request vectors to Kutaisi for full stop."),
                                                      ("Tanker", 255.5, "Enfield 1-1, request tanking."), ("Tanker", 255.5, "Texaco, Enfield 1-1, request rejoin") })
                {
                    while (SayQueue.TryTake(out _)) { }
                    wp.Tel = said.Contains("bingo") ? wTel with { AltMsl = kr.Elev + 900, Agl = 900, Ias = 150 } : wTel;   // R51: on the ground it would say "check in airborne"
                    wp.CalledOn = f;
                    Request(wp, $"{to}: {said}", "SRS", false, null, 0.84);
                    wr.Add($"{string.Join(" ", menu)}#{string.Join(" | ", SayQueue.Select(x => x.Role + ":" + x.Text))}");
                    menu.Clear();
                }
            }
            finally
            {
                (Cfg.CommsKey, Ai, SimSec, stateTime, AiHook, SimLog, Fields) = (wCk, wAi, wSs, wSt, wAh, null, new());
                Pilots.Clear(); foreach (var kv in wPb) Pilots[kv.Key] = kv.Value;
                while (SayQueue.TryTake(out _)) { }
            }
            string Hint(string on, string mhz) => $"#Info:{L($"Du funkst auf {on} {mhz}. ", $"You are transmitting on {on} {mhz}. ")}Kutaisi Ground: 263.0 / 134.0";
            if (wr[0] != Hint("Tanker", "255.5") || wr[1] != Hint("AWACS", "251.5") || wr[2] != Hint("AWACS", "251.5") || wr[3] != Hint("Carrier", "127.5") || !wr[4].StartsWith("#Tanker:") || !wr[4].EndsWith("say again.")
                || !wr[5].StartsWith("#AWACS:") || !wr[5].Contains("Kutaisi bears") || !wr[6].StartsWith("MENU;U6;tanker;Aerial-1;s#Tanker:") || !wr[7].StartsWith("MENU;U6;tanker;Aerial-1;s#Tanker:")
                || !wr[7].EndsWith("report visual."))   // R304: DCS tanker silent (s), our tanker replies anyway
                throw new Exception("Falsche Frequenz: " + string.Join(" || ", wr));
            Console.WriteLine("OK   Falsche Frequenz: \"Request startup\" auf Tanker 255.5 (AWACS/Träger ebenso) -> kein DCS-Tankermenü, kein \"say again\", Hinweis \"Kutaisi Ground: 263.0 / 134.0\"; unbekanntes \"request …\" -> \"say again\" ohne Tankermenü; \"request rejoin\" drückt es weiter (s: DCS-Tanker stumm), unser Tanker antwortet (R304)");
        }
        // Frequency selects the airfield (radio wheel/F10, without via): without SRS the nearest; only Batumi tuned -> Batumi despite nearer Kobuleti; two tuned -> running flow (not parked/logged out), otherwise the nearest;
        // selecting an airfield overrides that; called on Batumi (SRS state still old) applies until tuned away from the airfield
        Airfield Fq(string name, double nm, double u, double v) { var a = Airfield.FromDcs(name, kut.X + nm * NM, kut.Z, 20, new[] { ("13", 0.0, kut.X + nm * NM, kut.Z, 2500.0) }); (a.Uhf, a.Vhf) = (u, v); return a; }
        var (kob, ba, sen) = (Fq("Kobuleti", 5, 262, 133), Fq("Batumi", 20, 260, 131), Fq("Senaki", 40, 261, 132));
        (SimLog, Fields) = (new(), new() { kob, ba, sen });
        var pr = new Pilot { Unit = "U7", Callsign = "Enfield 1-1", Id = 9, Tel = new Telemetry(2000, 1500, 150, 0, kut.X, kut.Z, 0, 0, 0) };
        var fw = new List<string> { Pick(pr, null)!.F.Name };
        Srs = new SrsListener(5002, Array.Empty<double>(), _ => false, _ => { });
        Srs.OnServerMessage(Radios("260000000", "251500000"));
        Request(pr, "radio check", "RAD", true); fw.Add(pr.Active!.F.Name);
        Srs.OnServerMessage(Radios("262000000", "261000000")); fw.Add(Pick(pr, null)!.F.Name);   // Batumi flow not tuned: the nearest
        pr.Active = TowerFor(pr, sen); fw.Add(Pick(pr, null)!.F.Name);   // Senaki only parked (no flow): the nearest
        Request(pr, "Senaki Approach, Enfield 1-1, inbound for landing", "T", false); fw.Add(Pick(pr, null)!.F.Name + pr.Active!.Phase);
        pr.Boat.Stage = 1;   // in the Marshal stack, then bingo: selecting an airfield leaves the carrier (otherwise Enter would stay with the carrier)
        Request(pr, "switch Batumi", "RAD", true); fw.Add(Pick(pr, null)!.F.Name + (pr.Boat.Stage > 0 ? "Träger" : ""));
        pr.Pinned = null;   // landed
        Request(pr, "Batumi Tower, Enfield 1-1, radio check", "T", false, ba); fw.Add(pr.Active!.F.Name);
        Srs.OnServerMessage(Radios("262000000", "261000000")); fw.Add(Pick(pr, null)!.F.Name);
        Srs.OnServerMessage(Radios("260000000", "262000000")); fw.Add(Pick(pr, null)!.F.Name);   // SRS state arrives later: Batumi tuned, stays despite nearer Kobuleti
        Srs.OnServerMessage(Radios("262000000", "251500000")); fw.Add(Pick(pr, null)!.F.Name);
        Request(pr, "Kobuleti Approach, Enfield 1-1, inbound for landing", "T", false); fw.Add(Pick(pr, null)!.F.Name + pr.Active!.Phase);
        Srs.OnServerMessage(Radios("262000000", "260000000")); fw.Add(Pick(pr, null)!.F.Name);   // Approach Kobuleti, second radio newly on Batumi: the new one wins
        // R304: after the tanker menu DCS tunes the tanker frequency itself (here 261 = Senaki): no airfield change, our tanker talks there too; tuned in later applies again
        pr.TankMenuAt = Now();
        Srs.OnServerMessage(Radios("262000000", "261000000"));
        var r304 = $"{Pick(pr, null)!.F.Name} {pr.TankFq} {Program.Fq(OpsTx(pr, new("Tanker", "Texaco", "x")).Freqs)}";
        pr.TankMenuAt = Now() - 10;
        Srs.OnServerMessage(Radios("262000000", "260000000")); Pick(pr, null);
        Srs.OnServerMessage(Radios("262000000", "261000000")); r304 += " " + Pick(pr, null)!.F.Name;
        if (r304 != "Kobuleti 261 255.5/261.0 Senaki") throw new Exception("R304 Tankerfrequenz: " + r304);
        Console.WriteLine("OK   R304: Frequenzwechsel durch DCS binnen 3 s nach dem Tankermenü wählt keinen Platz, Tanker spricht auch dort; später eingedreht wieder");
        Srs = null;
        // Emergency without called airfield: nearest suitable (Kobuleti); with "Platz wählen" (e.g. bingo alternate) the selected one stays
        const string May = "mayday mayday mayday, fuel emergency, request immediate landing";
        var pm = new Pilot { Unit = "U6", Callsign = "Enfield 1-2", Id = 10, Tel = pr.Tel };
        Request(pm, May, "RAD", true); fw.Add(pm.Active!.F.Name);
        Request(pr, "switch Batumi", "RAD", true); Request(pr, May, "RAD", true); fw.Add(pr.Active!.F.Name);
        // A15: MAYDAY on the AWACS frequency (here GCI) -> AWACS names the nearest suitable airfield, it is selected and its Approach handles the emergency
        var pa = new Pilot { Unit = "U5", Callsign = "Enfield 1-3", Id = 11, Coalition = 2, Tel = pr.Tel, OnBoat = "CVN-71" };   // from the carrier: afterwards the airfield applies, no longer Marshal
        Ops.Gci.Add(2);
        while (SayQueue.TryTake(out _)) { }
        Request(pa, "AWACS: mayday mayday mayday, Magic, Enfield 1-3, engine fire", "SRS", false);
        Ops.Gci.Remove(2);
        var a15 = new List<Tx>(); while (SayQueue.TryTake(out var t15)) a15.Add(t15);
        fw.Add(pa.Pinned?.F.Name + (pa.Active?.Emergency == true ? "!" : "") + string.Join("/", a15.Select(t => t.Role)) + pa.OnBoat);
        while (SayQueue.TryTake(out _)) { }
        (SimLog, Fields) = (null, new());
        if (!fw.SequenceEqual(new[] { "Kobuleti", "Batumi", "Kobuleti", "Kobuleti", "SenakiInbound", "Batumi", "Batumi", "Batumi", "Batumi", "Kobuleti", "KobuletiInbound", "Batumi", "Kobuleti", "Batumi", "Kobuleti!AWACS" }))
            throw new Exception("Frequenz wählt den Platz: " + string.Join(",", fw));
        Console.WriteLine("OK   Frequenz wählt den Platz: ohne SRS Kobuleti (nächster), gerastet Batumi trotz näherem Kobuleti, zwei gerastet -> Ablauf bzw. nächster (geparkter Senaki zählt nicht), Platz wählen schlägt das (und verlässt den Träger-Stack), auf Batumi gerufen gilt bis zum Wegdrehen, neu eingedrehter Platz schlägt den laufenden Ablauf auf dem anderen Gerät; A15 MAYDAY beim AWACS -> Kobuleti gewählt (auch vom Träger), Approach hat den Notfall vorgemerkt und schweigt bis zum Erstanruf (R250)");
        // R136: SRS packet with two frequencies (simultaneous transmit): an unmonitored one in first position must not discard the packet
        byte[] VoicePkt(string sender, params double[] mhz)
        {
            var pk = new byte[6 + 3 + 10 * mhz.Length + 13 + 44];
            BitConverter.GetBytes((ushort)pk.Length).CopyTo(pk, 0); BitConverter.GetBytes((ushort)3).CopyTo(pk, 2); BitConverter.GetBytes((ushort)(10 * mhz.Length)).CopyTo(pk, 4);
            for (int i = 0; i < mhz.Length; i++) BitConverter.GetBytes(mhz[i] * 1e6).CopyTo(pk, 9 + 10 * i);
            System.Text.Encoding.ASCII.GetBytes(sender.PadRight(22, 'x')).CopyTo(pk, pk.Length - 44);
            return pk;
        }
        var sv = new SrsListener(5002, new[] { 263.0 }, _ => false, _ => { });
        sv.OnVoice(VoicePkt("a", 243.0, 263.0)); sv.OnVoice(VoicePkt("b", 263.0, 243.0)); sv.OnVoice(VoicePkt("c", 243.0, 251.0));
        var svf = sv.OpenFreqs();
        if (svf.Length != 2 || svf.Any(f => f != 263.0)) throw new Exception("SRS-Paket mit zwei Frequenzen: " + string.Join(",", svf));
        Console.WriteLine("OK   SRS-Simultansenden: Paket [243.0, 263.0] mit Abhörfrequenz 263.0 -> Rx auf 263.0, [263.0, 243.0] ebenso, [243.0, 251.0] verworfen");
        {   // A22: SRS monitoring: active airfield before taxi and mission AWACS (9 of them), over 10 frequencies as secFreq of the radios
            var aw0 = Ops.AwacsFreq;
            (Ops.AwacsFreq, Fields) = (Enumerable.Range(0, 9).ToDictionary(i => "AW" + i, i => 240.0 + i), new() { sen, kob, ba });
            var pl = new Pilot { Unit = "U5", Callsign = "Colt 1-1", Id = 11, Tel = new Telemetry(2000, 1500, 150, 0, kut.X, kut.Z, 0, 0, 0) };
            pl.Active = TowerFor(pl, sen);
            var lf = ListenFreqs(new() { pl }).Distinct().ToList();
            var msg = new SrsListener(5002, lf, _ => false, _ => { }).Message(3);
            (Ops.AwacsFreq, Fields) = (aw0, new());
            if (lf.Count <= 10 || lf[0] != 261 || lf[1] != 132 || !lf.Contains(262) || !msg.Contains("\"freq\":261000000,") || !msg.Contains($"\"secFreq\":{lf[10] * 1e6},") || !msg.Contains($"\"secFreq\":{lf[^1] * 1e6},"))
                throw new Exception($"A22 SRS-Abhören: {string.Join(",", lf)}");
            Console.WriteLine($"OK   SRS-Abhören (A22): aktiver Platz zuerst, {lf.Count} Frequenzen (über 10 als secFreq): {string.Join(",", lf)}");
        }
        // Neighboring airfield: 4000 ft at the point, 5000 ft 1 km beside -> 5 NM away next free 6000; 15 NM away free; own does not count
        Holds["ai:X"] = (0, 0, 4000);
        Holds["p:Y"] = (0, 1000, 5000);
        double f1 = FreeLevel("p:Z", 5 * NM, 0, 4000), f2 = FreeLevel("p:Z", 15 * NM, 0, 4000), f3 = FreeLevel("ai:X", 0, 0, 4000);
        if (f1 != 6000 || f2 != 4000 || f3 != 4000) throw new Exception($"Höhenstaffel: {f1} {f2} {f3}");
        Console.WriteLine("OK   Warteschleifen platzübergreifend gestaffelt (4000/5000 belegt -> 6000)");
        Holds.Clear();
        // Runway like DCS: up to 3.5 m/s main runway (also with tailwind), above that headwind; AI landing in light wind corrects the main runway
        var kb = Airfield.Kutaisi();
        var e25 = kb.End("25");
        string calm = kb.BestRunway(3 * e25.Dx, 3 * e25.Dz), windy = kb.BestRunway(4 * e25.Dx, 4 * e25.Dz);
        Fields = new() { kb };
        var lander = new Traffic(1, "F-16C_50", kb.X, kb.Z, 100, kb.End("07").Hdg * Math.PI / 180, 70, "L1", "arr:Kutaisi");
        LearnMain(new() { lander }, new() { lander with { InAir = false } }, new() { ["Kutaisi"] = (1.0, 0.0, 15.0, 100000.0) });
        Fields = new();
        if (calm != "25" || windy != "07" || kb.Main != "07") throw new Exception($"Bahnwahl wie DCS: {calm} {windy} gelernt {kb.Main}");
        Console.WriteLine("OK   Bahn wie DCS (3 m/s Rückenwind -> Hauptbahn 25, 4 m/s -> 07, KI-Landung lehrt 07)");
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

    static void LoadConfig()
    {
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
        Cfg.Voices.TryAdd("Departure", Cfg.Voices["Approach"]);   // AirfieldFrequencies: Departure speaks with the Approach voice
        Cfg.SrsPath = FindSrs(Cfg.SrsPath);
        Tower.AltimeterUnit = Cfg.AltimeterUnit.ToLowerInvariant();
        (Ops.AwacsBullseye, Ops.NewGroupNm) = (Cfg.AwacsBullseye, Math.Clamp(Cfg.AwacsNewGroupNm, 40, 200));
        Carrier.OnSpeedAoa = Cfg.OnSpeedAoa;
        Ops.Vectors = Cfg.TankerVectors;
        Tower.Watch = Cfg.AirspaceWatch;
        (Flights.Mode, Flights.RadiusNm, Flights.OwnWingmen) = (Cfg.AiFlightComms, Cfg.AiFlightRadiusNm, Cfg.AiOwnWingmen);
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
}
