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
    // ------------------------------------------------------------ Speech recognition (whisper.cpp)
    /// Text and mean word confidence (0–1, Whisper token probabilities from -ojf).
    /// role: controller of the receiving frequency (SrsRole/FreqRoute, null = unknown, local push-to-talk), via: airfield of that frequency.
    static (string Text, double Conf) Transcribe(string wav, Pilot? p = null, bool keep = true, string? role = null, Airfield? via = null)
    {
        string cs, prompt;
        List<string> names;   // expected names: a word one letter off becomes the name (Tower.FixNames)
        lock (TowerLock)
        {
            var h = p ?? Host();   // A141: callsign and airfield of the sender (SRS), otherwise of the host
            cs = h?.Callsign ?? Cfg.Callsign;
            names = Fields.SelectMany(f => new[] { f.Name, f.Callsign }).Concat(PromptNames(role, h)).Concat(PromptNames(null, h)).Concat(Pilots.Values.Select(x => x.Callsign)).Append(cs).ToList();
            prompt = WhisperPrompt(role, via ?? (h?.Lead ?? h)?.Active?.F ?? Fields[0], Fields, PromptNames(role, h),
                                   (h?.SlotCallsign is { Length: > 0 } sc && sc != cs ? $"{sc}, {cs}" : cs)   // slot call sign too ("Trident 5-1" with own "Victory 9-1")
                                   + (role == "Carrier" && h?.Modex is { Length: > 0 } mx ? $", {mx}" : ""));   // #12: board number on the carrier frequencies ("Marshal, 103, ...")
        }
        var model = Path.Combine(Root, "models", "ggml-small.en.bin");   // small: significantly fewer mishearings than base, ~1.7 s instead of 0.5 s
        if (!File.Exists(model)) model = Path.Combine(Root, "models", "ggml-base.en.bin");
        var sw = Stopwatch.StartNew();
        if (Cfg.AsrApiUrl != "" && AsrApi.Transcribe(wav, prompt) is { } api)   // #11: endpoint first, local whisper if it fails
        {
            Trace("WHISPER", $"{Path.GetFileName(wav)} API {Cfg.AsrApiModel} {sw.ElapsedMilliseconds} ms conf {api.Conf:0.00} (Sender {cs}, Prompt {role ?? "allgemein"}): {api.Text}");
            if (keep) Keep(wav, api.Text);
            return (Tower.FixNames(api.Text, names), api.Conf);
        }
        var json = Path.ChangeExtension(wav, null);   // -of: <wav without extension>.json
        try { File.Delete(json + ".json"); } catch (IOException) { }   // do not read old confidence
        var exe = Path.Combine(Root, "tools", "whisper", "Release", "whisper-cli.exe");
        string[] args = { "-m", model, "-t", Math.Clamp(Environment.ProcessorCount / 2 - 2, 2, 8).ToString(), "-f", wav, "-nt", "-l", "en", "--prompt", prompt, "-ojf", "-of", json };   // ponytail: -t leaves two cores to DCS (main/render thread), 16 threads -> 6; raise if replies lag, lower if DCS stutters while talking
        string[] On(int d) => args.Concat(d == -2 ? Array.Empty<string>() : d < 0 ? new[] { "-np", "-ng" } : new[] { "-np", "-dev", d.ToString() }).ToArray();   // -2: without -np, whisper lists its devices
        string output;
        AsrGate.Wait();   // #11 (log: 15 at once on a CPU server, minutes each): one whisper at a time, each already uses half the cores; one after the other is faster for all
        long queued = sw.ElapsedMilliseconds;
        int dev = Cfg.AsrGpu ? asrDev : -1;
        try
        {
            (output, var exit, var err) = RunErr(exe, null, On(dev));
            if (exit != 0 && dev != -1)   // GPU1: GPU failed (memory full, driver): this call again and the rest of the session on the CPU
            {
                asrDev = dev = -1;
                Log(L("[ASR] Grafikkarte fehlgeschlagen, Spracherkennung ab jetzt auf dem Prozessor", "[ASR] Graphics card failed, speech recognition on the CPU from now on"));
                (output, _, _) = RunErr(exe, null, On(-1));
            }
            else if (dev == -2)
            {
                (asrDev, var name) = GpuOf(err);
                Log(asrDev < 0 ? L("[ASR] Keine dedizierte Grafikkarte: Spracherkennung auf dem Prozessor", "[ASR] No dedicated graphics card: speech recognition on the CPU")
                               : L($"[ASR] Spracherkennung auf der Grafikkarte: {name}", $"[ASR] Speech recognition on the graphics card: {name}"));
            }
        }
        finally { AsrGate.Release(); }
        double conf = 1;
        try { conf = Confidence(File.ReadAllText(json + ".json")); File.Delete(json + ".json"); } catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { Trace("FEHLER", $"Whisper-Sicherheit: {e.GetType().Name}: {e.Message}"); }
        Trace("WHISPER", $"{Path.GetFileName(wav)} {Path.GetFileName(model)} {(dev == -2 ? "Gerätewahl" : dev < 0 ? "CPU" : $"GPU {dev}")} {sw.ElapsedMilliseconds} ms{(queued > 100 ? $" (davon {queued} ms gewartet)" : "")} conf {conf:0.00} (Sender {cs}, Prompt {role ?? "allgemein"}): {output.Trim()}");
        if (keep) Keep(wav, output.Trim());   // corpus keeps what Whisper wrote
        return (Tower.FixNames(output.Trim(), names), conf);
    }

    /// GPU1: whisper device: -2 = not yet known (the first call lists the devices), -1 = CPU, otherwise Vulkan device of the dedicated GPU.
    static volatile int asrDev = -2;
    static readonly SemaphoreSlim AsrGate = new(1);   // #11: whisper processes one after the other

    /// GPU1: whisper's Vulkan list ("ggml_vulkan: 0 = NVIDIA GeForce RTX 5070 Ti (NVIDIA) | uma: 0 | …") -> first dedicated GPU, -1 = none (no Vulkan or iGPU only:
    /// an iGPU is slower than the CPU, GPU-MESSUNG.md 3.8 s instead of 2.1 s). ponytail: the first call runs on whisper's device 0, which may be the iGPU (one slow call)
    internal static (int Dev, string Name) GpuOf(string stderr) =>
        Regex.Match(stderr, @"ggml_vulkan: (\d+) = (.+?)(?: \([^)]*\))? \| uma: 0 ") is { Success: true } m ? (int.Parse(m.Groups[1].Value), m.Groups[2].Value) : (-1, "");

    /// LF1: phrases per frequency role; airfield roles (Ground/Tower/Approach/Departure) and unknown use the airfield phrases.
    static readonly Dictionary<string, string> PromptPhrases = new()
    {
        ["AWACS"] = "Checking in as fragged, angels 25, playtime 30 minutes. Request picture. Bogey dope. Bullseye 270, 30. Committing. Fox 3. Splash one. Cranking. Picture clean. Checking out.",
        ["Tanker"] = "Request rejoin. Visual. Pre-contact. Ready pre-contact. Contact. Disconnect. Abort. Request 10,000 pounds. Fuel state. Clear of the tanker.",
        ["Center"] = "Checking in, flight level 250. Request flight following. Request direct. Request descent. Leaving frequency. Switching to approach.",
        ["Range"] = "Checking in. IP inbound. In hot. Off safe. Cleared hot. Off dry. Range Alpha.",
        ["Carrier"] = "Request case 1 recovery. See you at 10. Commencing. Platform. Hornet ball, 4.5. Clara. Marshal, Paddles, Strike, Mother.",
        ["JTAC"] = "Checking in, ready for nine line. IP inbound. In hot. Contact. Tally. Capture. Laser on. Spot. Type 2 control.",
    };

    /// LF1: Whisper prompt for the role of the receiving frequency (null = unknown: airfield plus AWACS/tanker). whisper.cpp keeps only the last
    /// n_text_ctx/2 = 224 prompt tokens, so phrases first, then the five nearest other airfields, mission call names, own stations, the sender's callsign last.
    internal static string WhisperPrompt(string? role, Airfield f, IEnumerable<Airfield> fields, IEnumerable<string> names, string cs)
    {
        if (PromptPhrases.TryGetValue(role ?? "", out var ph)) return $"{ph} {string.Join(", ", names.Append(cs).Distinct())}.";
        string Fn(Airfield x) => x.Name.Replace('-', ' ') + (x.Name.Contains(x.Callsign) ? "" : $" ({x.Callsign})");   // "Mineralnye Vody (Minvody)"
        var crps = f.Charted ? string.Join(", ", f.Crp.Keys.Select(k => Airfield.Say(k).Replace("C R P", "CRP"))   // chart CRP names ("black sea", "river west"), named fixes ("Craig")
            .Concat(f.Routes.Values.SelectMany(r => r.Pts.Select(p => Airfield.Say(p.Fix)).Prepend(Airfield.Say(r.Name) + " recovery")))
            .Concat(f.Departures.Values.Select(r => Airfield.Say(r.Name) + " departure")).Distinct()) : "CRP South";
        return $"Request startup. Request taxi. Ready for departure runway 25. Airborne, switching to departure. Inbound, {crps}. " +
               "Initial 07, overhead, break, downwind. Final, touch and go. Request ILS approach. Runway vacated. Wilco. " +
               (role == null ? "Request picture. Request rejoin. " : "") +
               string.Join(", ", fields.OrderBy(x => Dist(x.X, x.Z, f.X, f.Z)).Take(6).Reverse().Select(Fn).Where(n => !f.StationOf("Tower").StartsWith(n))   // the five nearest and the own airfield (unless its stations already say it)
                   .Concat(names).Concat(Airfield.Roles.Select(f.StationOf)).Append(cs).Distinct()) + ".";
    }

    /// LF1: call names of the mission for the role (under TowerLock): AWACS (own as Ops names it, plus all with a mission frequency), tankers, Center, JTAC;
    /// then the call names of the four nearest other players ("Springfield").
    static IEnumerable<string> PromptNames(string? role, Pilot? h)
    {
        static string Word(string cs) => Regex.Match(cs, "^[A-Za-z]{3,}").Value is { Length: > 0 } w ? Tower.Cap(w.ToLowerInvariant()) : "";   // "Darkstar11" -> Darkstar
        var mission = Ops.AwacsFreq.Keys.Select(g => Word(AiCallsign.GetValueOrDefault(g, ""))).Where(w => w != "").ToList();
        var own = h == null ? "Overlord" : Ops.AwacsName(h.Ops.MyAwacs(TrafficFor(h), MeOf(h)), MeOf(h));
        var awacs = own == "Overlord" && mission.Count > 0 ? mission : mission.Prepend(own);   // Overlord is the fallback name: not next to a mission AWACS
        var tankers = Ops.TankerFreq.Keys.Select(g => Word(AiCallsign.GetValueOrDefault(g, ""))).Where(w => w != "").Concat(new[] { "Texaco", "Arco", "Shell" });   // DCS tanker call names
        var others = Pilots.Values.Where(x => x != h && x.Tel != null).OrderBy(x => h?.Tel is { } t ? Dist(t.X, t.Z, x.Tel!.X, x.Tel.Z) : 0)
                           .SelectMany(x => new[] { Word(x.Callsign), Word(x.SlotCallsign) }).Where(w => w != "").Distinct().Take(4);
        return (role switch
        {
            "AWACS" => awacs,
            "Tanker" => tankers,
            "Center" => new[] { (h?.Tel is { } t ? Tower.CenterFor(h.Coalition, t.X, t.Z)?.Name : null) ?? Tower.CenterName(h?.Coalition ?? 2) ?? "Center" },
            "JTAC" => Ops.Leaders.Where(l => l.Alive && l.Side == (h?.Coalition ?? 2)).Select(l => l.Cs).Take(4),
            null => awacs.Concat(tankers),
            _ => Array.Empty<string>(),
        }).Concat(others);
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

    /// LF1: every airfield of airfields.txt and every role under 200 prompt tokens (with four other players and a slot call sign), own stations and callsign at the end,
    /// AWACS prompt with the mission AWACS and without the fallback Overlord.
    static void PromptTest()
    {
        static int Tokens(string s) => (s.Length + 2) / 3;   // ponytail: chars/3, measured 3.2-3.6 for these prompts (airfield names alone 2.5); whisper tokenizer if it gets tight
        var fields = Airfield.Load(Path.Combine(Root, "airfields.txt"));
        var keep = Pilots.ToList();
        AiCallsign["LF1-AWACS"] = "Darkstar11"; Ops.AwacsFreq["LF1-AWACS"] = 324.4;
        try
        {
            Pilots.Clear();
            foreach (var (u, cs, slot) in new[] { ("LF1a", "Springfield 1-1", ""), ("LF1b", "Dodge 1-1", ""), ("LF1c", "Colt 1-1", "Trident 5-1"), ("LF1d", "Pontiac 1-1", "") })
                Pilots[u] = new Pilot { Unit = u, Callsign = cs, SlotCallsign = slot, Tel = new Telemetry(0, 0, 0, 0, 0, 0, 0, 0, 0) };
            var all = fields.SelectMany(f => new string?[] { null, "Ground", "Tower", "Approach", "Departure", "AWACS", "Tanker", "Center", "Range", "Carrier", "JTAC" }
                                                .Select(r => (F: f.Name, R: r ?? "allgemein", P: WhisperPrompt(r, f, fields, PromptNames(r, null), "Trident 5-1, Victory 9-1")))).ToList();
            var max = all.MaxBy(x => x.P.Length);
            var kut = all.First(x => x.F == "Kutaisi" && x.R == "Tower").P;
            var aw = all.First(x => x.R == "AWACS").P;
            if (Tokens(max.P) >= 200 || !all.All(x => x.P.EndsWith(" Trident 5-1, Victory 9-1.")) || !kut.EndsWith("Kutaisi Tower, Kutaisi Approach, Kutaisi Departure, Trident 5-1, Victory 9-1.")
                || !kut.Contains("Senaki") || kut.Contains("Anapa") || !aw.EndsWith("Darkstar, Springfield, Dodge, Colt, Trident, Trident 5-1, Victory 9-1.") || !aw.Contains("Bullseye") || aw.Contains("Overlord"))
                throw new Exception($"LF1 Whisper-Prompt: längster {Tokens(max.P)} Token ({max.F} {max.R}): {max.P} / Kutaisi Tower: {kut} / AWACS: {aw}");
            Console.WriteLine($"OK   LF1 Whisper-Prompt: {fields.Count} Plätze x 11 Rollen unter 200 Token (längster {Tokens(max.P)}, {max.F} {max.R}), Kutaisi nur 5 Nachbarn, " +
                              "Stationen, Slot- und eigenes Rufzeichen am Ende, 4 andere Spieler, AWACS mit Darkstar ohne Overlord");
        }
        finally { Pilots.Clear(); foreach (var kv in keep) Pilots[kv.Key] = kv.Value; AiCallsign.Remove("LF1-AWACS"); Ops.AwacsFreq.Remove("LF1-AWACS"); }
    }

    /// GPU1: first dedicated GPU of whisper's Vulkan list (also behind an iGPU), iGPU only or no Vulkan -> CPU.
    static void GpuTest()
    {
        const string nv = "ggml_vulkan: 0 = NVIDIA GeForce RTX 5070 Ti (NVIDIA) | uma: 0 | fp16: 1 | bf16: 1\n", amd = "ggml_vulkan: 1 = AMD Radeon(TM) Graphics (AMD proprietary driver) | uma: 1 | fp16: 1\n";
        var (a, b, c, d) = (GpuOf("ggml_vulkan: Found 2 Vulkan devices:\n" + nv + amd), GpuOf(amd.Replace(": 1 =", ": 0 =") + nv.Replace(": 0 =", ": 1 =")), GpuOf(amd), GpuOf("load_backend: loaded CPU backend\n"));
        if (a != (0, "NVIDIA GeForce RTX 5070 Ti") || b.Dev != 1 || c.Dev != -1 || d.Dev != -1) throw new Exception($"GPU1 Whisper-Gerät: {a} {b} {c} {d}");
        Console.WriteLine("OK   GPU1 Whisper-Gerät: erste dedizierte GPU (auch hinter einer iGPU), nur iGPU oder kein Vulkan -> CPU");
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
    /// R356: "to all" (gid 0) with side only to that coalition (like the audio, A133); gid 0 then means the whole coalition (Show).
    static IEnumerable<int> ShowTo(int gid, double freq, double freq2, int side = 0)
    {
        if (freq <= 0 || Srs is not { } srs || freq == Flights.Guard && side <= 0) return new[] { gid };
        bool On(double m, double f) => f > 0 && Math.Abs(m - f) < 0.005;
        bool Tuned(Pilot p) => freq == Flights.Guard || srs.TunedFreqs(p.Id) is not { } fq || fq.Any(m => On(m, freq) || On(m, freq2));   // Guard is heard by everyone (of the side)
        Pilot[] ps;
        try { ps = Pilots.Values.ToArray(); } catch (InvalidOperationException) { return new[] { gid }; }
        if (gid > 0) { var g = ps.Where(p => p.Gid == gid).ToArray(); return g.Length == 0 || g.Any(Tuned) ? new[] { gid } : Array.Empty<int>(); }
        ps = ps.Where(p => side <= 0 || p.Coalition == side).ToArray();
        if (ps.Any(p => p.Gid == 0 && Tuned(p))) return new[] { 0 };   // Group unknown
        return ps.Where(p => p.Gid > 0 && Tuned(p)).Select(p => p.Gid).Distinct().ToArray();   // "to all" individually to each group on the frequency
    }

    static double FreqOf(Tx tx) => tx.Role == "Crew" ? -(double)tx.Unit   // < 0: intercom of this aircraft (transmit)
                                 : tx.Freqs is { Length: > 0 } fs ? fs[0]   // Airfield frequency (UHF, plus VHF)
                                 : Cfg.Frequencies.GetValueOrDefault(tx.Role, Cfg.Frequencies.GetValueOrDefault("Tower", 265.0));

    static readonly List<(DateTime At, string Who, string Text, int Gid, bool Pilot)> RadioLog = new();   // FI7: the last 30 calls on the air

    /// FI7: snapshot for the map (Lagebild /state): own aircraft (host, on a server the first player), airfields and traffic within 150 NM,
    /// controller with phase, runway, ATIS letter and frequencies, the last 15 calls, the last controller call to you.
    static object MapState()
    {
        lock (TowerLock)
        {
            var h = ActivePilots().Count > 0 ? Host() ?? ActivePilots()[0] : null;   // Host() without mission data takes [0]
            var t = h?.Tel;
            var tw = (h?.Lead ?? h)?.Active;
            double x0 = t?.X ?? tw?.F.X ?? Fields.FirstOrDefault()?.X ?? 0, z0 = t?.Z ?? tw?.F.Z ?? Fields.FirstOrDefault()?.Z ?? 0;
            var cs = h == null ? "" : Tower.SpokenCallsign(h.Callsign);
            List<(DateTime At, string Who, string Text, int Gid, bool Pilot)> radio;
            lock (RadioLog) radio = RadioLog.ToList();
            bool Mine((DateTime At, string Who, string Text, int Gid, bool Pilot) r) => h != null && (r.Gid == h.Gid && h.Gid > 0 || cs != "" && r.Text.StartsWith(cs + ",", StringComparison.OrdinalIgnoreCase));
            return new
            {
                me = h == null || t == null ? null : new { cs = h.Callsign, type = h.Type, x = t.X, z = t.Z, hdg = t.Hdg, ft = t.AltMsl / 0.3048, kt = t.Ias / 0.514444, side = h.Coalition },
                center = new { x = x0, z = z0 },
                fields = Fields.Where(f => Dist(f.X, f.Z, x0, z0) < 150 * NM).Select(f => new
                {
                    name = f.Name, x = f.X, z = f.Z,
                    rw = f.Ends.Select(e => new[] { e.ThrX, e.ThrZ, e.ThrX + e.Len * e.Dx, e.ThrZ + e.Len * e.Dz }).ToList(),
                }).ToList(),
                traffic = h == null ? new List<object>() : TrafficFor(h).Where(a => Dist(a.X, a.Z, x0, z0) < 150 * NM)
                    .Select(a => (object)new { cs = a.Cs != "" ? a.Cs : a.Type, x = a.X, z = a.Z, hdg = a.Hdg, ft = a.AltMsl / 0.3048, kt = a.Speed / 0.514444, side = a.Coalition, air = a.InAir }).ToList(),
                atc = tw == null ? null : new
                {
                    field = tw.F.Name, phase = tw.Phase.ToString(), rw = tw.Runway, atis = Atis.TryGetValue(tw.F.Name, out var ai) && ai.Text != "" ? ai.Letter.ToString() : "",
                    freqs = new[] { "ATIS", "Ground", "Tower", "Approach" }.Select(r => new[] { r, string.Join(" / ", (r == "ATIS" ? new[] { AtisFreq(tw.F.Name) } : FieldFreqs(tw.F, r) ?? Array.Empty<double>()).Select(f => f.ToString("0.0##", CultureInfo.InvariantCulture))) })
                             .Where(f => f[1] != "").ToList(),
                },
                radio = radio.TakeLast(15).Select(r => new { t = r.At.ToString("HH:mm:ss"), who = r.Who, text = r.Text, mine = Mine(r) }).ToList(),
                last = radio.LastOrDefault(r => !r.Pilot && Mine(r)).Text,
            };
        }
    }

    static void SpeakLoop()
    {
        int n = 0;
        foreach (var tx in SayQueue.GetConsumingEnumerable())
        {
            if (!RoleOn(tx.Role) && !(tx.Role == "Crew" && tx.Station is "Instructor" or "Crew")) continue;   // module switched off (settings): also catches AI pilots on its frequency; FI4/FI6 intercom is not the crew chief
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
                tx.Aired?.Invoke();
                if (!info) lock (RadioLog) { RadioLog.Add((Clock, label.TrimStart('>', ' ').TrimEnd(' ', ':'), text, tx.Gid, tx.Pilot)); if (RadioLog.Count > 30) RadioLog.RemoveAt(0); }   // FI7 map
                if (tx.Gid < 0 || !Cfg.RadioText && !info && freq != 0) return;   // #6: RadioText off = speech only
                var dur = ShowSec(tx, label);
                foreach (var gid in ShowTo(tx.Gid, info ? 0 : freq, freq2, tx.Side))
                    try
                    {
                        var f = Path.Combine(MenuDir, Clock.Ticks + "-" + gid + (gid == 0 && tx.Side > 0 ? "-c" + tx.Side : ""));   // R356: Lua shows "-c{side}" to that coalition only
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
            return tx.Text.Contains("merged") ? 1 : AwacsRow(tx.Text) is { Fresh: true } || tx.Text.Contains("pop-up group") ? 2 : Tasking.IsMatch(tx.Text) ? 3 : Scope.IsMatch(tx.Text) ? 4 : 3;
        int p = PrioOf(tx);
        if (p == 0) return 0;
        if (tx.Role == "Flight") return Brevity.IsMatch(tx.Text) ? 1 : p <= 1 || Tasking.IsMatch(tx.Text) ? 3 : 4;
        return p <= 1 ? 3 : 5;
    }

    /// Display duration of the text in the game (seconds, part of the .out line "gid|sek|text"): other AI radio is heard, not read (6–8 s),
    /// otherwise reading time at least 20 s, longer for long texts.
    internal static int ShowSec(Tx tx, string label) =>
        (int)Math.Ceiling(tx.Prio == 2 || tx.Role == "Flight" ? Math.Clamp(tx.Text.Length / 8.0, 6, 8) : Math.Max(20, (label + tx.Text).Length / 8.0));


    /// Text -> mp3 with radio sound. voice = "model[@pitch]". Returns the duration in seconds.
    static readonly Dictionary<string, (string Mp3, double Sec)> SynthCache = new();   // Text|voice -> finished mp3
    /// "two four zero" -> "two-four-zero": Piper otherwise stresses each digit individually and speaks numbers slowly
    static readonly Regex DigitRun = new($@"\b(?:{string.Join("|", Tower.DigitWords)})(?: (?:{string.Join("|", Tower.DigitWords)})\b)+");
    internal static string ForPiper(string text, bool slow = false) => DigitRun.Replace(text.Replace("Kutaisi", "Koo-tie-see"), slow ? m => m.Value : m => m.Value.Replace(" ", "-"));   // Pronunciation; slow (9-line): digits as single words for writing down, otherwise bound (brisk as on the radio)

    /// Equalize the natural pace per Piper voice: the same radio call takes 10.0 s (ryan) to 14.1 s (bryce) at length_scale 1.
    /// Factor = 11.5 s / measured duration, so that SpeechRate sounds equally fast for every voice (0.85 -> about 9.8 s); unknown voice 1.
    internal static readonly Dictionary<string, double> VoicePace = new()
    {
        ["en_US-ryan-medium"] = 1.14, ["en_US-bryce-medium"] = 0.82, ["en_US-ljspeech-medium"] = 0.95, ["en_GB-alan-medium"] = 0.88, ["en_US-joe-medium"] = 1.06,
        ["en_US-amy-medium"] = 0.90, ["en_US-hfc_female-medium"] = 1.03, ["en_US-kristin-medium"] = 0.93, ["en_US-lessac-medium"] = 1.06,
    };

    /// LK2: the AWACS speaks tersely and briskly as in combat (length_scale × 0.93; 0.85 sounded too fast in game, smaller = faster), without the overdriven combat voice; target ≤ 5 s per follow-up report.
    internal static double PaceOf(Tx tx) => tx.Pilot || tx.Stress ? 1 : tx.Role == "AWACS" ? 0.93 : 1;

    static double Synth(string text, string voice, string mp3, bool pilot = false, bool stress = false, double pace = 1)
    {
        var key = $"{text}|{voice}|{pilot}|{stress}|{pace}";
        lock (SynthCache)
            if (SynthCache.TryGetValue(key, out var hit) && File.Exists(hit.Mp3))
                try { File.Copy(hit.Mp3, mp3, true); return hit.Sec; } catch (IOException) { }
        var wav = Path.ChangeExtension(mp3, ".wav");
        var parts = voice.Split('@');
        double pitch = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
        bool nine = Regex.IsMatch(text, @"\b[Ll]ine \d,");   // 9-line: natural pace, digits single, longer pause after each line (the pilot writes it down)
        string sil = nine ? "0.7" : "0.2", say = ForPiper(text, nine), model = Path.Combine(Root, "models", parts[0] + ".onnx"),   // Pronunciation; digits bound: brisk as on the radio
               scale = ((pilot ? 0.95 : Cfg.SpeechRate) * VoicePace.GetValueOrDefault(parts[0], 1) * (stress ? 0.85 : 1) * pace).ToString("0.###", CultureInfo.InvariantCulture);   // Combat: faster
        if (!PiperWarm(model, scale, sil, say, wav))
            Run(Path.Combine(Root, "tools", "piper", "piper.exe"), say, "--model", model, "--length_scale", scale, "--sentence_silence", sil, "--output_file", wav);
        double sec = 0;
        for (int i = 0; ; i++)   // warm Piper reports the file before it closes it: otherwise 0.2 s transmission, call lost
            try { using var r = new WaveFileReader(wav); sec = r.TotalTime.TotalSeconds; break; }
            catch (IOException) when (i < 40) { Thread.Sleep(25); }
            catch (Exception e) { Trace("FEHLER", $"Piper-WAV {voice}: {e.GetType().Name}: {e.Message}"); break; }
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
    static bool PiperWarm(string model, string scale, string sil, string text, string wav)
    {
        var key = model + "|" + scale + "|" + sil;
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
                    foreach (var a in new[] { "--model", model, "--length_scale", scale, "--sentence_silence", sil, "--json-input", "-q" }) psi.ArgumentList.Add(a);
                    e.P = Process.Start(psi)!;
                    Background(e.P);   // onnxruntime takes all cores: DCS frames first
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
        Run(SrsFile(Cfg.SrsPath, "ExternalAudio", "DCS-SR-ExternalAudio.exe") ?? Path.Combine(Cfg.SrsPath, "ExternalAudio", "DCS-SR-ExternalAudio.exe"), null, new[] {
            $"--file={mp3}", $"--freqs={(ic ? "100" : freq.ToString("0.000", CultureInfo.InvariantCulture) + (freq2 > 0 ? "," + freq2.ToString("0.000", CultureInfo.InvariantCulture) : ""))}",
            $"--modulations={(ic ? "INTERCOM" : Cfg.Modulation + (freq2 > 0 ? "," + Cfg.Modulation : ""))}", $"--coalition={(side > 0 ? side : Cfg.Coalition)}", $"--port={SrsPort}", "--name=" + name.Replace(" ", ""),
            "--volume=" + Cfg.Volume.ToString(CultureInfo.InvariantCulture),
            $"--unitId={(ic ? -freq : 100000)}" }.Concat(PosArgs(name, freq)).ToArray());   // 100000: SrsListener does not overhear own controllers; FI2: position
    }

    /// FI2: lat/lon grid of the mission (L lines, coord.LOtoLL), x-major; empty = none yet.
    internal static (double X, double Z, double Lat, double Lon)[] LlGrid = Array.Empty<(double, double, double, double)>();

    /// FI2: DCS x/z -> lat/lon, bilinear in the grid (about 100 m off at most), outside linear from the edge cell; null = no grid.
    internal static (double Lat, double Lon)? LatLonOf(double x, double z)
    {
        var g = LlGrid;
        var (xs, zs) = (g.Select(q => q.X).Distinct().Order().ToArray(), g.Select(q => q.Z).Distinct().Order().ToArray());
        if (xs.Length < 2 || zs.Length < 2 || xs.Length * zs.Length != g.Length) return null;
        int i = Math.Clamp(Array.FindLastIndex(xs, v => v <= x), 0, xs.Length - 2), j = Math.Clamp(Array.FindLastIndex(zs, v => v <= z), 0, zs.Length - 2);
        double u = (x - xs[i]) / (xs[i + 1] - xs[i]), w = (z - zs[j]) / (zs[j + 1] - zs[j]);
        var (a, b, c, d) = (g[i * zs.Length + j], g[i * zs.Length + j + 1], g[(i + 1) * zs.Length + j], g[(i + 1) * zs.Length + j + 1]);
        return (a.Lat * (1 - u) * (1 - w) + b.Lat * (1 - u) * w + c.Lat * u * (1 - w) + d.Lat * u * w,
                a.Lon * (1 - u) * (1 - w) + b.Lon * (1 - u) * w + c.Lon * u * (1 - w) + d.Lon * u * w);
    }

    /// #15: lat/lon -> DCS x/z on any map, inverse of LatLonOf (Newton steps in the grid, converges in a few); null = no grid yet (before the mission data).
    internal static (double X, double Z)? XzOf(double lat, double lon)
    {
        var g = LlGrid;
        if (g.Length == 0 || LatLonOf(g[0].X, g[0].Z) == null) return null;
        double x = g.Average(q => q.X), z = g.Average(q => q.Z);
        for (int k = 0; k < 8; k++)
        {
            var (p, px, pz) = (LatLonOf(x, z)!.Value, LatLonOf(x + 1000, z)!.Value, LatLonOf(x, z + 1000)!.Value);
            double a = (px.Lat - p.Lat) / 1000, b = (pz.Lat - p.Lat) / 1000, c = (px.Lon - p.Lon) / 1000, d = (pz.Lon - p.Lon) / 1000, det = a * d - b * c;
            if (Math.Abs(det) < 1e-18) return null;
            (x, z) = (x + (d * (lat - p.Lat) - b * (lon - p.Lon)) / det, z + (a * (lon - p.Lon) - c * (lat - p.Lat)) / det);
        }
        return (x, z);
    }

    /// FI2: position of the transmitting station (x, z, m MSL) by its SRS name: player, airfield (antenna 30 m above the field), JTAC/FAC(A), AI aircraft (AWACS, tanker, flights).
    /// null = Center (antenna network), intercom, unknown -> without position as before. ponytail: carrier stations have none (close by over water), add with Carrier.Boats if LOS complaints come.
    static (double X, double Z, double Alt)? StationAt(string name)
    {
        var key = name.Split(' ')[0];
        lock (TowerLock)
        {
            if (Pilots.Values.FirstOrDefault(p => p.Callsign == name && p.Tel != null)?.Tel is { } pt) return (pt.X, pt.Z, pt.AltMsl);
            if (Fields.FirstOrDefault(f => name == f.Name + "ATIS" || f.StationOf("x") is var s && name.StartsWith(s[..^1]) && !name[(s.Length - 1)..].Contains(' ')) is { } fd) return (fd.X, fd.Z, fd.Elev + 30);
            if (Ops.Leaders.FirstOrDefault(l => l.Alive && l.Cs.Split(' ')[0] == key) is { } l) return (l.X, l.Z, l.Alt);
            var h = Host()?.Tel;
            if ((Ai.FirstOrDefault(a => a.Cs == name) ?? Ai.Where(a => a.Cs.Split(' ')[0] == key).MinBy(a => h == null ? 0 : Dist(a.X, a.Z, h.X, h.Z))) is { } ai) return (ai.X, ai.Z, ai.AltMsl);
        }
        return null;
    }

    /// FI2: --latitude/--longitude/--altitude for ExternalAudio. SRS uses them for range and line of sight if the server has that on (StationPositions).
    internal static string[] PosArgs(string name, double freq) => Cfg.StationPositions && freq > 0 && StationAt(name) is { } s && LatLonOf(s.X, s.Z) is { } ll
        ? new[] { FormattableString.Invariant($"--latitude={ll.Lat:0.000000}"), FormattableString.Invariant($"--longitude={ll.Lon:0.000000}"), FormattableString.Invariant($"--altitude={s.Alt:0}") }
        : Array.Empty<string>();

    /// whisper/piper/ffmpeg below normal: DCS frames first. Not limited to half the cores (0.16.2 tried that: voices choppy, answers slow).
    static void Background(Process p)
    {
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
    }

    static bool srsDown;   // LA24: ExternalAudio last could not connect
    static bool srsHinted;
    static double? noSrsClientAt;
    /// Forum (Tony86): with DCS in full screen nobody reads the console: SRS problem once per session as text in the game (Gid 0 = all).
    static void SrsHint(string text)
    {
        if (srsHinted) return;
        srsHinted = true;
        Log("[SRS] " + text);
        SayQueue.Add(new Tx(text, "Info", "", 0));
    }
    static string SrsDownText() => L($"Kein Lotsen-Ton: SRS-Server auf diesem PC (Port {SrsPort}) nicht erreichbar. SRS-Ordner in den Einstellungen prüfen, Server starten.",
                                     $"No controller voice: SRS server on this PC (port {SrsPort}) not reachable. Check the SRS folder in the settings, start the server.");
    static (string stdout, int exit) Run(string exe, string? stdin, params string[] args) { var (o, x, _) = RunErr(exe, stdin, args); return (o, x); }

    /// #20: SRS ExternalAudio (.NET) crashes when a GC collects its multimedia timer delegate (MMTimerProc) mid-transmission. That is a fail-fast, whose
    /// "Unknown Hard Error" box SetErrorMode cannot suppress; with a 128 MB gen0 budget no GC runs during a transmission, so the crash does not happen.
    static ProcessStartInfo StartInfo(string exe, bool stdin)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin };
        if (exe.Contains("ExternalAudio")) psi.Environment["DOTNET_GCgen0size"] = "0x8000000";
        return psi;
    }

    /// Run with stderr (GPU1: whisper's device list).
    static (string stdout, int exit, string stderr) RunErr(string exe, string? stdin, params string[] args)
    {
        var psi = StartInfo(exe, stdin != null);
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            if (!exe.Contains("ExternalAudio")) Background(p);
            else try { p.PriorityClass = ProcessPriorityClass.High; } catch (Exception) { }   // ExternalAudio: steady rhythm, otherwise choppy when DCS maxes out the CPU; whisper/piper/ffmpeg: DCS frames first (stutter)
            if (stdin != null) { p.StandardInput.WriteLine(stdin); p.StandardInput.Close(); }
            var err = p.StandardError.ReadToEndAsync();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 && exe == Ffmpeg) Console.WriteLine($"      ffmpeg: {err.Result.Trim()}");
            // LA24 (log: "Could not connect" every 33 s as FEHLER, crash dump of ExternalAudio itself): SRS unreachable once until the next success, own crash as one line
            bool down = o.Contains("Could not connect"), first = down && !srsDown;
            if (exe.Contains("ExternalAudio")) srsDown = down;
            if (first) Console.WriteLine(L("      (SRS-Server nicht erreichbar)", "      (SRS server not reachable)"));
            if (down && (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds > 60) SrsHint(SrsDownText());   // right after the start the server may still be coming up
            if (p.ExitCode != 0 && !down || first)
                Trace("FEHLER", $"{Path.GetFileName(exe)} Exit {p.ExitCode}: " + (err.Result.Contains("garbage collected delegate") ? "Absturz in SRS ExternalAudio selbst (garbage collected delegate)" : $"{err.Result.Trim()} {o.Trim()}"));
            return (o, p.ExitCode, err.Result);
        }
        catch (Exception e)
        {
            Console.WriteLine(L("      Fehler beim Start von ", "      Error starting ") + $"{Path.GetFileName(exe)}: {e.Message}");
            Trace("FEHLER", $"Start {exe}: {e.GetType().Name}: {e.Message}");
            if (exe.Contains("ExternalAudio")) SrsHint(SrsDownText());
            return ("", -1, "");
        }
    }

}
