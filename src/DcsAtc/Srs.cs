using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DcsAtc;

/// Listens as an invisible SRS client to the controller frequencies: every radio call of a player (also teammates)
/// comes out as 16 kHz PCM with frequency and DCS unit ID. Protocol SRS 2.4 (TCP-JSON sync + UDP Opus).
sealed class SrsListener
{
    public record Transmission(short[] Pcm, double FreqMHz, uint UnitId, string Name);

    readonly IPEndPoint server;
    volatile double[] freqsMHz;   // max. 20: 10 SRS radios, plus a second frequency each (secFreq, A22); SetFreqs changes them continuously
    internal const int Max = 20;
    volatile int resync;   // counter: every connection sends RADIO_UPDATE when it changes
    volatile bool security;   // K4: COALITION_AUDIO_SECURITY of the server (SYNC/SERVER_SETTINGS) - it forwards voice only within the coalition
    readonly Action<Transmission> onTx;
    readonly Func<string, bool> ignoreName;   // do not monitor own controller voices (ExternalAudio)
    readonly string[] guids = { NewGuid(), NewGuid() };   // K4: connection 0 (spectator, blue with security), 1 (red, only with security)
    static string NewGuid() => Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('/', '_').Replace('+', '-')[..22];
    int Side(int conn) => conn == 1 ? 2 : security ? 1 : 0;
    readonly Dictionary<string, (string Name, uint UnitId, double[]? Freqs, Iff? Iff)> clients = new();
    readonly Dictionary<string, Rx> open = new();
    public bool Connected { get; private set; }
    public bool HasPlayers { get { lock (clients) return clients.Values.Any(c => c.Name != "DCS-ATC" && c.UnitId != 100000 && !ignoreName(c.Name)); } }   // Forum (Tony86): a real SRS client on this server (not we, not ExternalAudio)
    static readonly bool Debug = Environment.GetEnvironmentVariable("DCSATC_SRSDEBUG") == "1";

    sealed class Rx { public readonly SortedDictionary<ulong, byte[]> Frames = new(); public double Freq; public uint Unit; public string Name = ""; public DateTime Last; }

    public SrsListener(int port, IEnumerable<double> freqsMHz, Func<string, bool> ignoreName, Action<Transmission> onTx)
    {
        server = new IPEndPoint(IPAddress.Loopback, port);
        this.freqsMHz = freqsMHz.Distinct().Take(Max).ToArray();
        this.ignoreName = ignoreName;
        this.onTx = onTx;
    }

    /// Change monitored frequencies (e.g. airfields near the players); goes to the server on the next pass.
    public void SetFreqs(IEnumerable<double> f)
    {
        var a = f.Distinct().Take(Max).ToArray();
        if (a.SequenceEqual(freqsMHz)) return;
        Program.Trace("SRS", $"Abhören: {string.Join(", ", freqsMHz.Select(x => x.ToString("0.0##")))} -> {string.Join(", ", a.Select(x => x.ToString("0.0##")))}");
        freqsMHz = a;
        resync++;
        if (Connected) Console.WriteLine(L("      SRS-Abhören: ", "      SRS listening: ") + $"{string.Join(", ", a.Select(x => x.ToString("0.0##")))}");
    }

    /// Frequencies (MHz) tuned in SRS by the player with this DCS unit ID; null = unknown.
    public double[]? TunedFreqs(uint unit)
    {
        if (unit == 0) return null;
        lock (clients) return clients.Values.FirstOrDefault(c => c.UnitId == unit).Freqs;
    }

    /// FI1: transponder of the player with this DCS unit ID (first SRS client that reports one); null = unknown.
    public Iff? IffOf(uint unit)
    {
        if (unit == 0) return null;
        lock (clients) return clients.Values.FirstOrDefault(c => c.UnitId == unit && c.Iff != null).Iff;
    }

    /// FI1: "iff" of a SRS RadioInfo ({"control":0,"mode3":4521,"mode4":true,"status":1}); null = missing, control 2 (not fitted) or unreadable.
    internal static Iff? IffFrom(JsonElement ri)
    {
        if (ri.ValueKind != JsonValueKind.Object || !ri.TryGetProperty("iff", out var x) || x.ValueKind != JsonValueKind.Object) return null;
        int? I(string k) => x.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        if (I("control") == 2 || I("status") is not { } st || I("mode3") is not { } m3) return null;
        bool m4 = x.TryGetProperty("mode4", out var v4) && (v4.ValueKind == JsonValueKind.True || I("mode4") == 1);   // bool, older clients 0/1
        return new Iff(m3, st, m4);
    }

    public void Start() { for (int c = 0; c < 2; c++) { int conn = c; new Thread(() => Run(conn)) { IsBackground = true }.Start(); } }

    void Run(int conn)
    {
        while (true)
        {
            try { if (conn == 0 || security) Session(conn); }   // K4: red connection only with coalition security
            catch (Exception e) { if (Connected) { if (conn == 0) Console.WriteLine(L("      SRS-Abhören getrennt: ", "      SRS listening disconnected: ") + e.Message); Program.Trace("SRS", $"getrennt ({conn}): {e.GetType().Name}: {e.Message}"); } }
            if (conn == 0) Connected = false;
            Thread.Sleep(5000);   // SRS server not there yet / restarted
        }
    }

    void Session(int conn)
    {
        using var tcp = new TcpClient();
        tcp.Connect(server);
        var stream = tcp.GetStream();
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        int synced = resync;
        writer.WriteLine(Message(2, conn));   // SYNC
        writer.WriteLine(Message(3, conn));   // RADIO_UPDATE: server forwards only frequencies we have "tuned"
        using var udp = new UdpClient(0);
        udp.Connect(server);
        var ping = Encoding.ASCII.GetBytes(guids[conn]);
        udp.Send(ping);
        var fl = string.Join(", ", freqsMHz.Select(f => f.ToString("0.0#")));
        Program.Trace("SRS", $"verbunden mit {server} (Koalition {Side(conn)}), Abhören: {fl}");
        if (conn == 0)
        {
            Connected = true;
            Console.WriteLine(L($"      SRS-Abhören aktiv: {fl} – Spieler können direkt über SRS sprechen.", $"      SRS listening on: {fl} – players can talk directly over SRS."));
        }

        var reader = new Thread(() =>
        {
            try
            {
                using var r = new StreamReader(stream, Encoding.UTF8);
                for (string? line; (line = r.ReadLine()) != null;) OnServerMessage(line);
            }
            catch (Exception e) { Program.Trace("SRS", $"TCP-Lesen beendet: {e.GetType().Name}: {e.Message}"); }
            try { udp.Close(); } catch (Exception) { }
        }) { IsBackground = true };
        reader.Start();

        var nextPing = DateTime.Now.AddSeconds(1);
        bool pong = false;
        var nextSync = DateTime.Now.AddSeconds(30);
        udp.Client.ReceiveTimeout = 100;
        while (reader.IsAlive && (conn == 0 || security))
        {
            try { var any = new IPEndPoint(IPAddress.Any, 0); var b = udp.Receive(ref any); if (Debug) Console.WriteLine($"udp {b.Length}"); if (b.Length == 22) pong = true; else OnVoice(b); }   // K4: duplicate packets (global frequencies) land on the same packet ID
            catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut) { }
            if (DateTime.Now >= nextPing) { nextPing = DateTime.Now.AddSeconds(pong ? 15 : 1); udp.Send(ping); }   // every second until the first reply
            if (synced != resync) { synced = resync; writer.WriteLine(Message(3, conn)); }   // also K4: coalition changed
            if (DateTime.Now >= nextSync) { nextSync = DateTime.Now.AddSeconds(30); writer.WriteLine(Message(1, conn)); writer.WriteLine(Message(3, conn)); }   // PING + radios
            FlushFinished(DateTime.Now);
        }
        throw new IOException("Verbindung zum SRS-Server beendet");
    }


    internal string Message(int type, int conn = 0)   // internal: self-test A22, K4
    {
        var fq = freqsMHz;
        var radios = Enumerable.Range(0, 11).Select(i => new Dictionary<string, object>
        {
            ["freq"] = i > 0 && i <= fq.Length ? fq[i - 1] * 1e6 : 1.0,
            ["modulation"] = i > 0 && i <= fq.Length ? 0 : 3,   // AM, otherwise DISABLED
            ["enc"] = false, ["encKey"] = 0, ["secFreq"] = i > 0 && i + 9 < fq.Length ? fq[i + 9] * 1e6 : 1.0, ["retransmit"] = false,   // A22: SRS server also forwards on secFreq (like Guard, without modulation check)
        }).ToArray();
        var msg = new Dictionary<string, object>
        {
            ["MsgType"] = type,
            ["Version"] = "2.4.1.0",
        };
        msg["Client"] = new Dictionary<string, object>
            {
                ["ClientGuid"] = guids[conn], ["Name"] = "DCS-ATC", ["Coalition"] = Side(conn), ["AllowRecord"] = false, ["Seat"] = 0,
                ["LatLngPosition"] = new Dictionary<string, double> { ["lat"] = 0, ["lng"] = 0, ["alt"] = 0 },
                ["RadioInfo"] = new Dictionary<string, object> { ["radios"] = radios, ["unit"] = "DCS-ATC", ["unitId"] = 0 },
            };
        return JsonSerializer.Serialize(msg);
    }

    internal void OnServerMessage(string line)   // internal: self-test K2 (latched frequencies)
    {
        if (Debug) Console.WriteLine("tcp " + line[..Math.Min(300, line.Length)]);
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            void Add(JsonElement c)
            {
                if (!c.TryGetProperty("ClientGuid", out var g) || g.GetString() is not { } id) return;
                var name = c.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                uint unit = c.TryGetProperty("RadioInfo", out var ri) && ri.ValueKind == JsonValueKind.Object &&
                            ri.TryGetProperty("unitId", out var u) && u.TryGetUInt32(out var v) ? v
                          : clients.TryGetValue(id, out var old) ? old.UnitId : 0;
                double[]? fq = c.TryGetProperty("RadioInfo", out var ri2) && ri2.ValueKind == JsonValueKind.Object &&
                               ri2.TryGetProperty("radios", out var rs) && rs.ValueKind == JsonValueKind.Array
                    ? rs.EnumerateArray().Select(x => x.TryGetProperty("freq", out var fr) && fr.TryGetDouble(out var hz) ? hz / 1e6 : 0).Where(m => m > 1).ToArray()
                    : clients.TryGetValue(id, out var o2) ? o2.Freqs : null;
                var iff = c.TryGetProperty("RadioInfo", out var ri3) && ri3.ValueKind == JsonValueKind.Object ? IffFrom(ri3) : clients.TryGetValue(id, out var o4) ? o4.Iff : null;
                lock (clients)
                {
                    var was = clients.TryGetValue(id, out var o3) ? o3 : default;
                    if (was.Name != name || was.UnitId != unit || was.Iff != iff || !(was.Freqs ?? Array.Empty<double>()).SequenceEqual(fq ?? Array.Empty<double>()))   // Debug log: tuned frequencies and transponder per player
                        Program.Trace("SRS", $"Client '{name}' unit {unit}: {Fl(was.Freqs)} -> {Fl(fq)}{(iff == null ? "" : $", IFF {iff.Mode3} Status {iff.Status}{(iff.Mode4 ? " M4" : "")}")}");
                    clients[id] = (name, unit, fq, iff);
                }
            }
            static string Fl(double[]? f) => f == null ? "?" : string.Join("/", f.Select(x => x.ToString("0.0##")));
            int type = root.GetProperty("MsgType").GetInt32();
            if (root.TryGetProperty("ServerSettings", out var ss) && ss.ValueKind == JsonValueKind.Object && ss.TryGetProperty("COALITION_AUDIO_SECURITY", out var cs)
                && (bool.TryParse(cs.ToString(), out var sec) && sec) != security)   // K4: "true"/"false" (SYNC, SERVER_SETTINGS)
            {
                security = !security;
                resync++;   // connection 0 becomes blue or spectator again
                Program.Trace("SRS", $"Coalition Security {(security ? "an: zweite Verbindung als Rot" : "aus")}");
            }
            if (root.TryGetProperty("Clients", out var list) && list.ValueKind == JsonValueKind.Array) foreach (var c in list.EnumerateArray()) Add(c);
            if (root.TryGetProperty("Client", out var one) && one.ValueKind == JsonValueKind.Object)
            {
                if (type == 5) lock (clients) { if (clients.Remove(one.GetProperty("ClientGuid").GetString() ?? "", out var gone)) Program.Trace("SRS", $"Client '{gone.Name}' unit {gone.UnitId} getrennt"); }   // CLIENT_DISCONNECT
                else Add(one);
            }
        }
        catch (Exception e) { Program.Trace("SRS", $"Nachricht nicht gelesen ({e.GetType().Name}: {e.Message}): {line[..Math.Min(200, line.Length)]}"); }   // ignore unknown messages
    }

    /// UDP packet: header (length, audio length, frequency length), Opus, 10 bytes per frequency, UnitId, PacketId, Hops, 2× GUID.
    internal void OnVoice(byte[] b)
    {
        if (b.Length <= 22 + 6) return;   // Ping reply
        int audio = BitConverter.ToUInt16(b, 2), freqLen = BitConverter.ToUInt16(b, 4);
        if (6 + audio + freqLen + 13 + 44 > b.Length || freqLen < 10) return;
        int fixedAt = 6 + audio + freqLen;
        if (b[b.Length - 45] > 0) return;   // forwarded copy
        // Simultaneous transmit on several radios: check all frequencies of the packet, the first monitored one counts (Annex 10 Vol II 5.2.2.1.1)
        int n = freqLen / 10;
        double freq = 0;
        bool hit = false;
        for (int i = 0; i < n && !hit; i++)
        {
            freq = BitConverter.ToDouble(b, 6 + audio + 10 * i) / 1e6;
            hit = freqsMHz.Any(f => Math.Abs(f - freq) < 0.001);
        }
        if (!hit) return;
        uint unit = BitConverter.ToUInt32(b, fixedAt);
        ulong packet = BitConverter.ToUInt64(b, fixedAt + 4);
        var sender = Encoding.ASCII.GetString(b, b.Length - 44, 22);
        string name;
        lock (clients) name = clients.TryGetValue(sender, out var c) ? c.Name : "";
        if (unit == 100000 || ignoreName(name)) return;   // ExternalAudio (own controllers)
        if (Debug && !open.ContainsKey(sender)) Console.WriteLine($"rx start {freq} ({n} Frequenzen im Paket) unit {unit} '{name}'");
        lock (open)
        {
            if (!open.TryGetValue(sender, out var rx))
            {
                open[sender] = rx = new Rx { Freq = freq, Unit = unit, Name = name };
                // Issue #7: the radio SRS really transmits on (selected radio first; in-cockpit modules choose it by the cockpit PTT, not the SRS selection)
                Program.Trace("SRS", $"Sendung unit {unit} '{name}' auf {string.Join("/", Enumerable.Range(0, n).Select(i => (BitConverter.ToDouble(b, 6 + audio + 10 * i) / 1e6).ToString("0.0##")))}, gehört {freq:0.0##}");
            }
            rx.Frames[packet] = b[6..(6 + audio)];
            rx.Last = DateTime.Now;
        }
    }

    internal double[] OpenFreqs() { lock (open) return open.Values.Select(r => r.Freq).ToArray(); }   // Self-test

    void FlushFinished(DateTime now)
    {
        List<(string Id, Rx Rx)> done;
        lock (open)
        {
            done = open.Where(kv => (now - kv.Value.Last).TotalMilliseconds > 500).Select(kv => (kv.Key, kv.Value)).ToList();
            foreach (var d in done) open.Remove(d.Id);
        }
        foreach (var (id, rx) in done)
        {
            var pcm = Decode(rx.Frames.Values);
            if (Debug) Console.WriteLine($"rx end {rx.Freq} {rx.Frames.Count} frames {pcm.Length} samples");
            if (pcm.Length < 16000 / 2) continue;   // under 0.5 s: clicks
            var tx = new Transmission(pcm, rx.Freq, rx.Unit, rx.Name);
            Task.Run(() => onTx(tx));
        }
    }

    // ------------------------------------------------------------ Opus (opus.dll from the SRS installation)
    [DllImport("opus", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr opus_decoder_create(int fs, int channels, out int error);
    [DllImport("opus", CallingConvention = CallingConvention.Cdecl)] static extern int opus_decode(IntPtr st, byte[] data, int len, short[] pcm, int frameSize, int fec);
    [DllImport("opus", CallingConvention = CallingConvention.Cdecl)] static extern void opus_decoder_destroy(IntPtr st);

    public static void UseOpusFrom(string srsPath)
    {
        NativeLibrary.SetDllImportResolver(typeof(SrsListener).Assembly, (name, _, _) =>
            name == "opus" && NativeLibrary.TryLoad(Program.SrsFile(srsPath, "Client", "opus.dll") ?? Path.Combine(srsPath, "Client", "opus.dll"), out var h) ? h : IntPtr.Zero);
    }

    static short[] Decode(IEnumerable<byte[]> frames)
    {
        var dec = opus_decoder_create(16000, 1, out int err);
        if (err != 0) return Array.Empty<short>();
        var all = new List<short>();
        var buf = new short[1920];   // max. 120 ms at 16 kHz
        try
        {
            foreach (var f in frames)
            {
                int n = opus_decode(dec, f, f.Length, buf, buf.Length, 0);
                if (n > 0) all.AddRange(buf.AsSpan(0, n).ToArray());
            }
        }
        finally { opus_decoder_destroy(dec); }
        return all.ToArray();
    }
}
