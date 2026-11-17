using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace DcsAtc;

static partial class Program
{
    static Config RadioCheckConfig(string root, Action<string> warn)
    {
        try
        {
            using var file = File.OpenRead(Path.Combine(root, "config.jsonc"));
            return JsonSerializer.Deserialize<Config>(file, new JsonSerializerOptions
                { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? throw new JsonException();
        }
        catch (FileNotFoundException) { }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            warn(L("config.jsonc ungültig oder nicht lesbar. Der Funk-Selbsttest nutzt die Standardwerte.",
                "Cannot read config.jsonc. The radio self-test is using the defaults."));
        }
        return new();
    }

    // The WAV entry point exercises the same recognition and synthesis without opening an audio device.
    internal static async Task<bool> RadioSelfTest(string root, int mic, string? wav, string srs, string towerVoice,
        bool gpu, Action<string> report, CancellationToken token)
    {
        var dir = Directory.CreateTempSubdirectory("DcsAtc-RadioCheck-");
        bool good = true;
        async Task<bool> Step(string name, Func<Task<string>> run)
        {
            token.ThrowIfCancellationRequested();
            try { report($"{name}: OK – {await run()}"); return true; }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                report($"{name}: {L("Fehler", "Failed")} – " + (e is InvalidDataException or TimeoutException ? e.Message : e.GetType().Name + ": " + e.Message));
                good = false;
                return false;
            }
        }
        string Have(string relative)
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path)) throw new InvalidDataException(L("Datei fehlt: ", "Missing file: ") + Path.GetFileName(relative));
            return path;
        }
        try
        {
            var input = wav ?? Path.Combine(dir.FullName, "microphone.wav");
            var micOk = await Step(wav == null ? L("Mikrofon", "Microphone") : L("WAV-Eingang", "WAV input"), async () =>
            {
                if (wav == null) await RecordRadioCheck(input, mic, token, report);
                var peak = RadioCheckLevel(input);
                if (peak < -50) throw new InvalidDataException(L("Kein ausreichender Pegel. Mikrofon und Eingangslautstärke prüfen.",
                    "Signal too quiet. Check the microphone and input level.") + $" ({peak:0} dBFS)");
                return L("Spitzenpegel", "Peak") + $" {(int)Math.Round(peak)} dBFS";
            });
            var asr = L("Lokales Whisper", "Local Whisper");
            if (micOk) await Step(asr, async () =>
            {
                string model = Path.Combine(root, "models", "ggml-small.en.bin");
                if (!File.Exists(model)) model = Path.Combine(root, "models", "ggml-base.en.bin");
                if (!File.Exists(model)) throw new InvalidDataException(L("Whisper-Modell fehlt (ggml-small.en.bin oder ggml-base.en.bin).",
                    "Whisper model missing (ggml-small.en.bin or ggml-base.en.bin)."));
                var exe = Have(@"tools\whisper\Release\whisper-cli.exe");
                report(L("Whisper prüft die Testphrase …", "Whisper is checking the test phrase …"));
                string[] args = ["-m", model, "-f", Path.GetFullPath(input), "-l", "en", "-nt", "-np", "-t", Math.Clamp(Environment.ProcessorCount / 2 - 2, 2, 8).ToString()];
                var (code, words) = await RadioCheckTool(exe, null, gpu ? args : [..args, "-ng"], token);
                if (gpu && code != 0) (code, words) = await RadioCheckTool(exe, null, [..args, "-ng"], token);
                if (code != 0) throw new InvalidDataException($"whisper-cli.exe: {L("Fehlercode", "exit code")} {code}");
                var heard = Regex.Replace(words.Trim(), @"\s+", " ");
                if (heard.Length > 120) heard = heard[..120] + "…";
                if (!Regex.IsMatch(heard, @"\bradio[\s-]+check\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidDataException(L("Erwartet „Radio check“. Erkannt: ", "Expected “Radio check”. Heard: ") + (heard == "" ? "–" : heard));
                return heard;
            });
            else report(asr + L(": übersprungen (kein Eingangssignal)", ": skipped (no input signal)"));

            await Step(L("Sprachausgabe", "Speech synthesis"), async () =>
            {
                var models = Path.Combine(root, "models");
                var installed = Directory.Exists(models) ? Directory.GetFiles(models, "*.onnx").Select(m => Path.GetFileNameWithoutExtension(m)).ToArray() : [];
                var voice = VoicePool(towerVoice, installed, (Cfg.PilotVoice ?? "").Split('@')[0])[0].Split('@')[0];
                var model = Have(Path.Combine("models", voice + ".onnx"));
                var reply = Path.Combine(dir.FullName, "reply.wav");
                report(L("Piper erzeugt die Testantwort …", "Piper is generating the reply …"));
                var (pc, _) = await RadioCheckTool(Have(@"tools\piper\piper.exe"), "Radio check, read you five.",
                    ["--model", model, "--output_file", reply], token);
                if (pc != 0 || !File.Exists(reply)) throw new InvalidDataException($"Piper: {L("keine Sprachdatei", "no voice file")} ({pc})");
                using (var reader = new WaveFileReader(reply))
                    if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16 ||
                        reader.TotalTime.TotalMilliseconds < 100 || PeakDb(reader) < -50)
                        throw new InvalidDataException(L("Piper erzeugte keine brauchbare Sprachantwort.", "Piper produced no usable voice signal."));
                var mp3 = Path.Combine(dir.FullName, "reply.mp3");
                var (fc, _) = await RadioCheckTool(Have(@"tools\ffmpeg\ffmpeg.exe"), null,
                    ["-nostdin", "-v", "error", "-i", reply, "-ac", "1", "-codec:a", "libmp3lame", mp3], token);
                if (fc != 0 || !File.Exists(mp3) || new FileInfo(mp3).Length == 0)
                    throw new InvalidDataException($"FFmpeg: {L("keine MP3-Datei", "no MP3 file")} ({fc})");
                return L("Testantwort als MP3 erzeugt", "Test reply encoded to MP3") + $" ({voice})";
            });
            await Step("SRS", () =>
            {
                var srsRoot = string.IsNullOrWhiteSpace(srs) ? FindSrs("") : SrsRoot(srs);
                if (!SrsOk(srsRoot)) throw new InvalidDataException(L("ExternalAudio fehlt. SRS-Ordner prüfen.", "ExternalAudio missing. Check the SRS folder."));
                var exe = SrsFile(srsRoot, "Server", "SRS-Server.exe", "SR-Server.exe");
                var port = exe == null ? 5002 : SrsCfgInt(Path.Combine(Path.GetDirectoryName(exe)!, "server.cfg"), "SERVER_PORT", 5002);
                if (!PortOpen(port)) throw new InvalidDataException(L($"ExternalAudio gefunden. Lokaler TCP-Port {port} nicht erreichbar. SRS-Server prüfen.",
                    $"ExternalAudio found. Local TCP port {port} unreachable. Check the SRS server."));
                return Task.FromResult(L($"ExternalAudio gefunden, lokaler TCP-Port {port} erreichbar", $"ExternalAudio found, local TCP port {port} reachable"));
            });
            return good;
        }
        finally { dir.Delete(true); }
    }

    static async Task RecordRadioCheck(string wav, int mic, CancellationToken token, Action<string> report)
    {
        using var rec = new WaveInEvent { DeviceNumber = mic, WaveFormat = new WaveFormat(16000, 16, 1) };
        using var writer = new WaveFileWriter(wav, rec.WaveFormat);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rec.DataAvailable += (_, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);
        rec.RecordingStopped += (_, e) => stopped.TrySetResult(e.Exception);
        rec.StartRecording();
        report(L("Mikrofon nimmt 4 Sekunden auf. Jetzt „Radio check“ sagen.", "Recording for 4 seconds. Say “Radio check” now."));
        await Task.WhenAny(stopped.Task, Task.Delay(4000, token));
        rec.StopRecording();
        if (await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)) is { } err) throw err;
        token.ThrowIfCancellationRequested();
    }

    static double RadioCheckLevel(string wav)
    {
        using var r = new WaveFileReader(wav);
        if (r.WaveFormat.Encoding != WaveFormatEncoding.Pcm || r.WaveFormat.BitsPerSample != 16 || r.WaveFormat.SampleRate != 16000 ||
            r.WaveFormat.Channels != 1 || r.TotalTime.TotalSeconds > 30)
            throw new InvalidDataException(L("WAV muss 16 kHz, PCM 16 Bit, Mono und höchstens 30 Sekunden lang sein.",
                                            "Use a 16 kHz mono PCM16 WAV of at most 30 seconds."));
        return PeakDb(r);
    }

    static double PeakDb(WaveFileReader r)
    {
        int peak = 0;
        var buf = new byte[4096];
        for (int n; (n = r.Read(buf, 0, buf.Length)) > 0;)
            for (int i = 0; i + 1 < n; i += 2)
                peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(i, 2))));
        return peak == 0 ? double.NegativeInfinity : 20 * Math.Log10(peak / 32768.0);
    }

    static async Task<(int Code, string Text)> RadioCheckTool(string exe, string? stdin, string[] args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var psi = StartInfo(exe, stdin != null);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        Background(p);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var output = p.StandardOutput.ReadToEndAsync(deadline.Token);
        var drain = p.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
        try
        {
            if (stdin != null) { await p.StandardInput.WriteLineAsync(stdin); p.StandardInput.Close(); }
            await p.WaitForExitAsync(deadline.Token);
            await drain;
            return (p.ExitCode, await output);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException(Path.GetFileName(exe) + L(" antwortet länger als 45 Sekunden nicht.", " did not finish within 45 seconds.")); }
        finally
        {
            if (!p.HasExited)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                p.WaitForExit(3000);
            }
        }
    }
}
