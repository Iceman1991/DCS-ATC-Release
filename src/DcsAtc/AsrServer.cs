using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace DcsAtc;

/// One resident whisper-server.exe per session instead of a whisper-cli per radio call (forum oz555, VR: stutter on every call; the CLI reloads the model and
/// rebuilds the Vulkan pipelines each time, ~1 s on the GPU, ~50 ms on a loaded server). Started in the background once the device is known; until it
/// answers, whisper-cli does the calls. Any failure: server stopped, whisper-cli for that call, a GPU failure means CPU from now on (one CPU server, then none).
static class AsrServer
{
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };   // per call via CancellationToken
    static readonly object Lk = new();
    static Process? proc;
    static bool starting, gaveUp;
    internal static volatile string? Url;   // null = not (yet) answering
    internal static int Dev = -3;           // device of the running/starting server: -1 CPU, otherwise the Vulkan device
    internal static bool Up => Url != null;
    static string Exe => Path.Combine(Program.Root, "tools", "whisper", "Release", "whisper-server.exe");

    /// Start (in the background) if wanted and not running; another device than the running server (AsrGpu switched live): restart. dev -2 = not yet known.
    internal static void Ensure(string model, int dev)
    {
        if (!Program.Cfg.AsrServer) { Stop(); return; }
        if (Program.Cfg.AsrApiUrl != "" || dev < -1) return;
        lock (Lk)
        {
            if (starting && Dev == dev || Url != null && Dev == dev || gaveUp) return;
            if (proc != null) Stop();
            (starting, Dev) = (true, dev);
        }
        Task.Run(() => Start(model, dev));
    }

    static void Start(string model, int dev)
    {
        Process? p = null;
        try
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            var psi = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-m", model, "-t", Program.AsrThreads.ToString(), "--host", "127.0.0.1", "--port", port.ToString(), "-nt", "-l", "en" }.Concat(dev < 0 ? new[] { "-ng" } : new[] { "-dev", dev.ToString() })) psi.ArgumentList.Add(a);
            var sw = Stopwatch.StartNew();
            p = Process.Start(psi)!;
            lock (Lk) proc = p;
            Program.Background(p);
            p.OutputDataReceived += (_, _) => { };   // unread pipes would block the server
            p.ErrorDataReceived += (_, _) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            for (bool open = false; !open;)   // the port opens once the model is loaded
            {
                if (p.HasExited) throw new IOException($"Exit {p.ExitCode}");
                if (sw.Elapsed.TotalSeconds > 90) throw new TimeoutException("Modell nicht geladen");
                try { using var c = new TcpClient(); c.Connect(IPAddress.Loopback, port); open = true; } catch (SocketException) { Thread.Sleep(200); }
            }
            var wav = Path.Combine(Program.Root, "tmp", "asrserver-warm.wav");   // the first request builds the Vulkan pipelines (seconds): one second of silence now, before the first radio call
            using (var w = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1))) w.WriteSamples(new short[16000], 0, 16000);
            Post($"http://127.0.0.1:{port}/inference", File.ReadAllBytes(wav), "", 60);
            lock (Lk) { if (proc != p) return; (Url, starting) = ($"http://127.0.0.1:{port}/inference", false); }
            Program.Log(Ui.L($"[ASR] Spracherkennung bleibt geladen ({(dev < 0 ? "CPU" : $"GPU {dev}")}, {sw.Elapsed.TotalSeconds:0.0} s)", $"[ASR] Speech recognition stays loaded ({(dev < 0 ? "CPU" : $"GPU {dev}")}, {sw.Elapsed.TotalSeconds:0.0} s)"));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or Win32Exception or TimeoutException or HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException)
        {
            bool stopped; lock (Lk) stopped = proc != p;   // Stop() killed it on purpose: no failure
            if (stopped) return;
            Fail(dev, $"Start: {e.GetType().Name}: {e.Message}");
        }
    }

    /// One radio call. null = use whisper-cli for this call (no server, HTTP error, timeout, server gone): the server is stopped then.
    internal static (string Text, double Conf)? Transcribe(string wav, string prompt)
    {
        var url = Url;
        int dev = Dev;
        if (url == null) return null;
        try { return Post(url, File.ReadAllBytes(wav), prompt, dev >= 0 ? 10 : 20); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            Fail(dev, $"{e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    static (string Text, double Conf) Post(string url, byte[] wav, string prompt, int seconds)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "call.wav");
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("verbose_json"), "response_format");
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var res = Http.Send(req, cts.Token);
        var body = res.Content.ReadAsStringAsync(cts.Token).Result;
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode} {body[..Math.Min(200, body.Length)]}");
        return Parse(body);
    }

    /// verbose_json of whisper-server: "text" and segments[].words[] = tokens {word, probability} (a leading space starts a word); confidence like Program.Confidence.
    internal static (string Text, double Conf) Parse(string json)
    {
        using var d = JsonDocument.Parse(json);
        var text = (d.RootElement.GetProperty("text").GetString() ?? "").Trim();
        var toks = d.RootElement.TryGetProperty("segments", out var segs)
            ? segs.EnumerateArray().SelectMany(s => s.GetProperty("words").EnumerateArray()).Select(w => (w.GetProperty("word").GetString() ?? "", w.GetProperty("probability").GetDouble())).ToList()
            : new List<(string, double)>();
        return (text, Program.MeanWord(toks));
    }

    /// Server failed: stop it; on the GPU the rest of the session runs on the CPU (the next call starts a CPU server), after a CPU failure no server any more.
    static void Fail(int dev, string why)
    {
        Stop();
        Program.Trace("FEHLER", $"whisper-server ({(dev < 0 ? "CPU" : $"GPU {dev}")}) {why}");
        if (dev >= 0)
        {
            Program.asrDev = -1;
            Program.Log(Ui.L("[ASR] Grafikkarte fehlgeschlagen, Spracherkennung ab jetzt auf dem Prozessor", "[ASR] Graphics card failed, speech recognition on the CPU from now on"));
        }
        else { lock (Lk) gaveUp = true; }
    }

    internal static void Stop()
    {
        Process? p;
        lock (Lk) { p = proc; (proc, Url, starting) = (null, null, false); }
        try { p?.Kill(true); p?.Dispose(); } catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
    }

    /// After a crash of the app a server may be left over: kill the ones of our own folder.
    internal static void KillOrphans()
    {
        foreach (var p in Process.GetProcessesByName("whisper-server"))
            try { if (string.Equals(p.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase)) p.Kill(); }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { }
            finally { p.Dispose(); }
    }

    /// Self-test (fake server, no GPU/model): request fields, text/confidence from a real verbose_json, Transcribe takes the server, 500 -> null + server off + GPU -> CPU.
    internal static void SelfTest()
    {
        var (url, dev, gave, adev, api, srvOn) = (Url, Dev, gaveUp, Program.asrDev, Program.Cfg.AsrApiUrl, Program.Cfg.AsrServer);
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        var reqs = new List<string>();
        var srv = new Thread(() =>
        {
            for (int n = 0; n < 4; n++)
            {
                using var c = l.AcceptTcpClient();
                var s = c.GetStream();
                var buf = new MemoryStream();
                var b = new byte[65536];
                int len = -1;
                while (len < 0 || buf.Length < len)
                {
                    int k = s.Read(b, 0, b.Length);
                    if (k == 0) break;
                    buf.Write(b, 0, k);
                    var txt = Encoding.Latin1.GetString(buf.ToArray());
                    int h = txt.IndexOf("\r\n\r\n");
                    if (h >= 0 && len < 0) len = h + 4 + (System.Text.RegularExpressions.Regex.Match(txt, @"Content-Length: (\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0);
                }
                reqs.Add(Encoding.Latin1.GetString(buf.ToArray()));
                var body = n < 2 ? RealJson : "Internal Server Error";
                s.Write(Encoding.UTF8.GetBytes($"HTTP/1.1 {(n < 2 ? "200 OK" : "500 Internal Server Error")}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}"));
            }
        }) { IsBackground = true };
        srv.Start();
        var wav = Path.Combine(Path.GetTempPath(), "dcsatc-asrserver.wav");
        using (var w = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1))) w.WriteSamples(new short[1600], 0, 1600);
        try
        {
            (Url, Dev, Program.asrDev, Program.Cfg.AsrApiUrl, Program.Cfg.AsrServer) = ($"http://127.0.0.1:{port}/inference", -1, -1, "", true);
            var direct = Transcribe(wav, "Kutaisi Tower, Enfield 1-1");   // request 0
            var viaProgram = Program.TranscribeForTest(wav);              // request 1: Transcribe takes the server, no whisper-cli (would need the model)
            Dev = 0; Program.asrDev = 0;
            var bad = Transcribe(wav, "x");                               // request 2: HTTP 500 -> null, server off, GPU -> CPU
            bool off = !Up && Program.asrDev == -1;
            Url = $"http://127.0.0.1:{port}/inference"; Dev = -1;
            var bad2 = Transcribe(wav, "x");                              // request 3: CPU server fails -> off, no restart
            srv.Join(3000);
            if (direct is not { Text: "We press 21,000 feet.", Conf: > 0.747 and < 0.749 } || viaProgram is not { Text: "We press 21,000 feet.", Conf: > 0.747 and < 0.749 }
                || bad != null || !off || bad2 != null || Up || !gaveUp || reqs.Count != 4
                || !reqs[0].StartsWith("POST /inference") || !reqs[0].Contains("name=prompt") || !reqs[0].Contains("Kutaisi Tower, Enfield 1-1") || !reqs[0].Contains("verbose_json") || !reqs[0].Contains("name=file"))
                throw new Exception($"AsrServer: {direct} | {viaProgram} | {bad} | aus {off} | {bad2} | {reqs.Count} Anfragen | gaveUp {gaveUp}");
        }
        finally
        {
            l.Stop();
            Stop();
            (Url, Dev, gaveUp, Program.asrDev, Program.Cfg.AsrApiUrl, Program.Cfg.AsrServer) = (url, dev, gave, adev, api, srvOn);
            File.Delete(wav);
        }
        Console.WriteLine("OK   Spracherkennung als Server: verbose_json -> Text und Sicherheit (wie whisper-cli), Funkspruch geht an den Server (Prompt im Feld), Fehler 500 -> whisper-cli, Server aus, GPU -> CPU, CPU-Fehler -> kein Server mehr");
    }

    // verbose_json of the real whisper-server.exe (v1.8, small.en, 2.5 s of radio), language_probabilities cut
    const string RealJson = """{"task":"transcribe","language":"english","duration":2.5199999809265137,"text":" We press 21,000 feet.\n","segments":[{"id":0,"text":" We press 21,000 feet.","tokens":[775,1803,2310,11,830,3625,13],"words":[{"word":" We","probability":0.4445372521877289},{"word":" press","probability":0.6148735880851746},{"word":" 21","probability":0.9897003173828125},{"word":",","probability":0.4183594882488251},{"word":"000","probability":0.9974626302719116},{"word":" feet","probability":0.9436719417572021},{"word":".","probability":0.9025291204452515}],"temperature":0.0,"avg_logprob":-0.29273736476898193,"no_speech_prob":1.929011159518268e-05}],"detected_language":"lithuanian","detected_language_probability":0.010000571608543396,"language_probabilities":{"en":0.010000189766287804,"zh":0.010000075213611126}}""";
}
