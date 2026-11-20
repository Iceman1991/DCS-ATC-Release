using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace DcsAtc;

/// Optional ElevenLabs text-to-speech for the controller voices (own account and API key in the settings). Without a key, with a rejected key,
/// used-up quota or no network the voice falls back to its Piper voice ("el:voiceId#piperVoice"). Raw PCM 22050 Hz like Piper medium.
static class ElevenLabs
{
    internal static string BaseUrl = "https://api.elevenlabs.io";   // the selftest points it to a local fake server
    internal const string Model = "eleven_flash_v2_5";
    internal static bool Disabled;   // key rejected or quota used up: Piper for the rest of the session (reset when the settings are saved)
    internal static Action<string> Hint = Program.ElevenHint;
    [ThreadStatic] internal static string? PreviewKey;   // settings window: the key typed there, before it is saved (also tries again after a rejection)
    [ThreadStatic] internal static long PreviewAllowed;   // settings window: characters the sample may use (remaining minus floor, from the quota read there); 0 = none
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    internal record Quota(long Count, long Limit, long Reset) { public long Remaining => Limit - Count; }

    // Hard stop at the included characters: ElevenLabs is only used while the remaining quota is known (-1 = unknown/unreadable).
    internal static long Remaining = -1, Limit, ReadAt;
    internal static bool Low => Limit > 0 && Remaining < Math.Max(500, Limit / 100);
    internal static bool Usable => !Disabled && Remaining >= 0;

    internal static void Apply(Quota q)
    {
        (Remaining, Limit, ReadAt) = (q.Remaining, q.Limit, Environment.TickCount64);
        if (Low) Disable(L("inklusive Zeichen aufgebraucht", "included characters used up"));
    }

    /// Re-read the quota in the background (every 5 minutes while in use); a failed read keeps the running estimate.
    static void Refresh()
    {
        ReadAt = Environment.TickCount64;
        var key = Program.Cfg.ElevenLabsKey;
        _ = Task.Run(async () =>
        {
            try { Apply(await GetQuota(key)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { Program.Trace("FEHLER", $"ElevenLabs Kontingent: {e.GetType().Name}: {e.Message}"); }
        });
    }

    static HttpRequestMessage Req(HttpMethod m, string path, string key)
    {
        var r = new HttpRequestMessage(m, BaseUrl + path);
        r.Headers.Add("xi-api-key", key);
        return r;
    }

    /// Text -> WAV (16 bit mono 22050 Hz). false = use Piper for this call.
    internal static bool Wav(string voiceId, string text, double speed, string wav)
    {
        var key = PreviewKey ?? Program.Cfg.ElevenLabsKey;
        if (PreviewKey == null && Disabled || key == "") return false;
        if (text.Length > (PreviewKey == null ? Remaining : PreviewAllowed)) return false;   // quota unknown (-1) or too little left: never pay for extra characters
        try
        {
            using var req = Req(HttpMethod.Post, $"/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}?output_format=pcm_22050", key);
            req.Content = new StringContent(JsonSerializer.Serialize(new { text, model_id = Model, voice_settings = new { stability = 0.5, similarity_boost = 0.75, speed } }), Encoding.UTF8, "application/json");
            using var res = Http.Send(req);
            if (!res.IsSuccessStatusCode)
            {
                var body = res.Content.ReadAsStringAsync().Result;
                int code = (int)res.StatusCode;
                if (code is 401 or 403) Disable(L("Schlüssel abgelehnt", "key rejected"));
                else if (code == 402 || code == 429 && body.Contains("quota", StringComparison.OrdinalIgnoreCase) || body.Contains("quota_exceeded")) Disable(L("Kontingent aufgebraucht", "quota used up"));
                Program.Trace("FEHLER", $"ElevenLabs {code} {body[..Math.Min(200, body.Length)]}");
                return false;
            }
            var pcm = res.Content.ReadAsByteArrayAsync().Result;
            if (pcm.Length < 2) return false;
            using var w = new WaveFileWriter(wav, new WaveFormat(22050, 16, 1));
            w.Write(pcm, 0, pcm.Length & ~1);
            if (PreviewKey == null)
            {
                Remaining -= text.Length;   // conservative: the model may bill less per character
                if (Low) Disable(L("inklusive Zeichen aufgebraucht", "included characters used up"));
                if (Environment.TickCount64 - ReadAt > 300_000) Refresh();
            }
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or AggregateException or IOException)
        {
            Program.Trace("FEHLER", $"ElevenLabs: {e.GetType().Name}: {e.Message}");   // only this call falls back
            return false;
        }
    }

    static void Disable(string why)
    {
        if (Disabled || PreviewKey != null) return;   // a test in the settings window does not switch the session off
        Disabled = true;
        Hint(L($"ElevenLabs: {why}, ab jetzt Piper-Stimmen.", $"ElevenLabs: {why}, Piper voices from now on."));
    }

    static async Task<string> Get(string path, string key)
    {
        using var req = Req(HttpMethod.Get, path, key);
        using var res = await Http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}", null, res.StatusCode);
        return body;
    }

    internal static Quota ParseQuota(string json)
    {
        using var d = JsonDocument.Parse(json);
        long N(string k) => d.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        return new(N("character_count"), N("character_limit"), N("next_character_count_reset_unix"));
    }

    internal static List<(string Id, string Name)> ParseVoices(string json)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.GetProperty("voices").EnumerateArray()
                .Select(v => (v.GetProperty("voice_id").GetString() ?? "", v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""))
                .Where(v => v.Item1 != "").ToList();
    }

    internal static async Task<Quota> GetQuota(string key) => ParseQuota(await Get("/v1/user/subscription", key));
    internal static async Task<List<(string Id, string Name)>> GetVoices(string key) => ParseVoices(await Get("/v1/voices", key));

    /// "49,056 of 121,000 characters left, reset 2026-12-05" (log) / localized for the settings window
    internal static string QuotaText(Quota q, bool log)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(q.Reset).LocalDateTime;
        if (log) return string.Create(CultureInfo.InvariantCulture, $"ElevenLabs: {q.Remaining:N0} of {q.Limit:N0} characters left, reset {date:yyyy-MM-dd}");
        var c = CultureInfo.InvariantCulture;   // InvariantGlobalization: no other culture exists
        string N(long v) { var s = v.ToString("N0", c); return Ui.En ? s : s.Replace(',', '.'); }
        return Ui.En ? string.Create(c, $"Left: {N(q.Remaining)} of {N(q.Limit)} characters, resets on {date:MMM d}")
                          : string.Create(c, $"Verbleibend: {N(q.Remaining)} von {N(q.Limit)} Zeichen, zurückgesetzt am {date:dd.MM.}");
    }

    /// Start: one log line with the remaining characters (asynchronous), key problems are noted too.
    internal static async Task LogQuota()
    {
        try { var q = await GetQuota(Program.Cfg.ElevenLabsKey); Program.Log(QuotaText(q, true)); Apply(q); }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { Remaining = -1; Program.Log("ElevenLabs: key rejected or without the permission User (read), Piper voices only"); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { Remaining = -1; Program.Trace("FEHLER", $"ElevenLabs Kontingent: {e.GetType().Name}: {e.Message}"); }
    }

    static string L(string de, string en) => Ui.L(de, en);

    /// Self-test: fake server answers PCM (200), then 401, then the subscription and voice list.
    internal static void SelfTest()
    {
        var (key, voices, url, dis, hint) = (Program.Cfg.ElevenLabsKey, Program.Cfg.ElevenVoices, BaseUrl, Disabled, Hint);
        var (rem, lim, rat) = (Remaining, Limit, ReadAt);
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        var reqs = new List<string>();
        var srv = new Thread(() =>
        {
            for (int n = 0; n < 8; n++)
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
                byte[] body = n switch
                {
                    0 => new byte[22050 * 2],   // 1 s of silence
                    1 => """{"detail":{"status":"invalid_api_key"}}"""u8.ToArray(),
                    2 => """{"character_count":1944,"character_limit":10000,"next_character_count_reset_unix":1796428800}"""u8.ToArray(),
                    3 => """{"voices":[{"voice_id":"v1","name":"Rachel"},{"voice_id":"v2","name":"Adam"}]}"""u8.ToArray(),
                    4 => """{"detail":{"status":"missing_permissions"}}"""u8.ToArray(),   // subscription not readable
                    5 => """{"character_count":1944,"character_limit":10000,"next_character_count_reset_unix":1796428800}"""u8.ToArray(),
                    6 => new byte[22050 * 2],
                    _ => """{"character_count":9990,"character_limit":10000,"next_character_count_reset_unix":1796428800}"""u8.ToArray(),   // 10 left
                };
                s.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {(n is 1 or 4 ? "401 Unauthorized" : "200 OK")}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                s.Write(body);
            }
        }) { IsBackground = true };
        srv.Start();
        var wav = Path.Combine(Path.GetTempPath(), "dcsatc-eleven.wav");
        var hints = new List<string>();
        try
        {
            (Program.Cfg.ElevenLabsKey, BaseUrl, Disabled, Hint) = ("xi-test", $"http://127.0.0.1:{port}", false, hints.Add);
            Apply(new Quota(0, 100000, 0));
            bool ok = Wav("v1", "Kutaisi Tower, radio check.", 1.2, wav);
            double sec = 0;
            if (ok) { using var r = new WaveFileReader(wav); sec = r.TotalTime.TotalSeconds; }
            bool afterOk = !Disabled;
            bool bad = Wav("v1", "x", 1, wav);
            bool off = Disabled && hints.Count == 1 && hints[0].Contains("ElevenLabs");
            bool again = Wav("v1", "x", 1, wav);   // disabled: no request
            var q = GetQuota("xi-test").GetAwaiter().GetResult();
            var vl = GetVoices("xi-test").GetAwaiter().GetResult();
            if (!ok || Math.Abs(sec - 1) > 0.01 || !afterOk || bad || !off || again || reqs.Count != 4
                || !reqs[0].StartsWith("POST /v1/text-to-speech/v1?output_format=pcm_22050") || !reqs[0].Contains("xi-api-key: xi-test") || !reqs[0].Contains("\"model_id\":\"eleven_flash_v2_5\"")
                || !reqs[0].Contains("\"speed\":1.2") || !reqs[0].Contains("Kutaisi Tower, radio check.") || !reqs[2].StartsWith("GET /v1/user/subscription") || !reqs[3].StartsWith("GET /v1/voices")
                || q != new Quota(1944, 10000, 1796428800) || q.Remaining != 8056 || vl.Count != 2 || vl[0] != ("v1", "Rachel")
                || !QuotaText(q, true).StartsWith("ElevenLabs: 8,056 of 10,000 characters left, reset 20"))
                throw new Exception($"ElevenLabs: ok {ok} {sec:0.00} s, 401 {bad}, aus {off}, {reqs.Count} Anfragen, {QuotaText(q, true)}");
            // settings-window text must not need a non-invariant culture (InvariantGlobalization)
            bool en0 = Ui.En;
            try
            {
                Ui.En = false; var de = QuotaText(q, false);
                Ui.En = true; var en = QuotaText(q, false);
                if (!de.StartsWith("Verbleibend: 8.056 von 10.000 Zeichen, zurückgesetzt am ") || !en.StartsWith("Left: 8,056 of 10,000 characters, resets on "))
                    throw new Exception($"ElevenLabs Kontingenttext: {de} / {en}");
            }
            finally { Ui.En = en0; }
            // VoiceFor: without key Piper, with key + mapping the token, "a|b" fixed per station
            Program.Cfg.ElevenLabsKey = "";
            Disabled = false;
            Apply(new Quota(0, 100000, 0));
            var plain = Program.VoiceFor("Tower", "Kutaisi Tower", "en_US-ryan-medium");
            Program.Cfg.ElevenLabsKey = "xi-test";
            Program.Cfg.ElevenVoices = new() { ["Tower"] = "idA", ["Ground"] = "idA|idB", ["ATIS"] = "" };
            var tok = Program.VoiceFor("Tower", "Kutaisi Tower", "en_US-ryan-medium");
            var g1 = Program.VoiceFor("Ground", "A", "en_US-bryce-medium");   // 65 and 66: the two stations get different ids
            var g2 = Program.VoiceFor("Ground", "B", "en_US-bryce-medium");
            var empty = Program.VoiceFor("ATIS", "Kutaisi", "en_US-ryan-medium");
            Disabled = true;
            var offTok = Program.VoiceFor("Tower", "Kutaisi Tower", "en_US-ryan-medium");
            Disabled = false;
            if (plain != "en_US-ryan-medium" || tok != "el:idA#en_US-ryan-medium" || g1 == g2 || !g1.StartsWith("el:id") || !g2.StartsWith("el:id") || g1 != Program.VoiceFor("Ground", "A", "en_US-bryce-medium")
                || empty.StartsWith("el:") || offTok != plain)
                throw new Exception($"ElevenLabs VoiceFor: {plain} | {tok} | {g1} | {g2} | {empty} | {offTok}");

            // hard stop at the included characters (the earlier calls used requests 0-3)
            Disabled = false;
            LogQuota().GetAwaiter().GetResult();   // request 4: subscription not readable -> no ElevenLabs
            bool unk = Remaining == -1 && !Program.VoiceFor("Tower", "Kutaisi Tower").StartsWith("el:") && !Wav("v1", "Radio check.", 1, wav) && reqs.Count == 5;
            LogQuota().GetAwaiter().GetResult();   // request 5: 8056 left
            bool known = Remaining == 8056 && Program.VoiceFor("Tower", "Kutaisi Tower").StartsWith("el:");
            bool tooLong = !Wav("v1", new string('a', 9000), 1, wav) && reqs.Count == 6 && !Disabled;   // more than is left: Piper for this call, no request
            PreviewKey = "xi-test";   // settings sample: quota not known there (allowed 0) or sample longer than allowed -> no request
            bool pv = !Wav("v1", "Radio check.", 1, wav);
            PreviewAllowed = 5;
            pv &= !Wav("v1", "Radio check.", 1, wav) && reqs.Count == 6;
            (PreviewKey, PreviewAllowed) = (null, 0);
            bool paid =Wav("v1", "Radio check.", 1, wav) && reqs.Count == 7 && Remaining == 8056 - 12;   // request 6: remaining drops by the text length
            int hintsBefore = hints.Count;
            LogQuota().GetAwaiter().GetResult();   // request 7: 10 of 10000 left -> below 1 % (at least 500): off with one hint
            bool low = Disabled && hints.Count == hintsBefore + 1;
            bool lowPiper = !Program.VoiceFor("Tower", "Kutaisi Tower").StartsWith("el:") && !Wav("v1", "x", 1, wav) && reqs.Count == 8;
            if (!srv.Join(3000) || !unk || !known || !tooLong || !pv || !paid || !low || !lowPiper)
                throw new Exception($"ElevenLabs Kontingent-Stopp: unbekannt {unk}, bekannt {known}, zu lang {tooLong}, Probe {pv}, Abzug {paid}, knapp {low}, Piper {lowPiper}, Anfragen {reqs.Count}");
        }
        finally
        {
            l.Stop();
            (Program.Cfg.ElevenLabsKey, Program.Cfg.ElevenVoices, BaseUrl, Disabled, Hint) = (key, voices, url, dis, hint);
            (Remaining, Limit, ReadAt) = (rem, lim, rat);
            File.Delete(wav);
        }
        Console.WriteLine("OK   ElevenLabs: Stimme je Rolle (\"a|b\" je Station fest), PCM -> WAV mit richtiger Dauer, Anfrage mit Schlüssel/Modell/Tempo, 401 -> aus mit einem Hinweis, Kontingent und Stimmenliste, harter Stopp bei den enthaltenen Zeichen (Kontingent unlesbar/zu knapp -> Piper ohne Anfrage, Abzug je Text, Einstellungs-Probe ohne bekanntes Kontingent ohne Anfrage)");
    }
}

static partial class Program
{
    /// Controller voice for a role: "el:voiceId#piperVoice" when ElevenLabs is on for it, otherwise the Piper voice (after # = fallback).
    internal static string VoiceFor(string role, string station, string piperDefault = "en_US-ryan-medium")
    {
        var piper = PickVoice(Cfg.Voices.GetValueOrDefault(role, piperDefault), station);
        if (Cfg.ElevenLabsKey == "" || !ElevenLabs.Usable) return piper;
        var ids = (Cfg.ElevenVoices.GetValueOrDefault(role) ?? (role == "Departure" ? Cfg.ElevenVoices.GetValueOrDefault("Approach") : null) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ids.Length == 0 ? piper : $"el:{ids[station.Sum(ch => ch) % ids.Length]}#{piper}";
    }

    /// Key rejected / quota used up: once as text in the game (Gid 0 = all) and in the log.
    internal static void ElevenHint(string text)
    {
        SayQueue.Add(new Tx(text, "Info", "", 0));
    }
}
