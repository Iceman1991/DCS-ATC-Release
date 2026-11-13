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
    /// Self-test: settings window writes config.jsonc (comments stay, missing keys added), callsign normalized.
    static void ConfigTest()
    {
        var c = "{\n  \"Callsign\": \"Enfield 1-1\",\n  \"Volume\": 0.6,   // leiser\n  \"Atis\": true\n}";
        c = WithConfigValue(WithConfigValue(WithConfigValue(c, "Volume", "0.8", ""), "Atis", "false", ""), "MyCallsign", "\"Viper 1-1\"", "Callsign");
        c = WithConfigValue(c, "MyCallsign", "\"\"", "Callsign");
        if (!c.Contains("\"Volume\": 0.8,   // leiser") || !c.Contains("\"Atis\": false") || !c.Contains("\"Callsign\": \"Enfield 1-1\",\n  \"MyCallsign\": \"\",") ||
            WithConfigValue("{\n}", "RunwayLights", "\"dcs\"", "Fehlt") != "{\n  \"RunwayLights\": \"dcs\",\n}" || JsonSerializer.Deserialize<Config>("{ \"WheelStyle\": \"hud\" }") == null || NormCallsign("viper11") != "Viper 1-1" || NormCallsign("Viper") != null ||
            UnitCallsign("VICTORY 9-1") != "Victory 9-1" || UnitCallsign("WOLF-2-1") != "Wolf 2-1" || UnitCallsign("Aerial-1-1") != null || UnitCallsign("F-4E ADVERSARIES NELLIS 1-1") != null)
            throw new Exception("Einstellungen: " + c);
        {   // forum (Abr): CallsignFromUnit for AI flights, AWACS and tanker too; the own name of a mission AWACS/tanker is a station
            var (cu, kc, ka, kt) = (Cfg.CallsignFromUnit, Ops.Callsigns, Ops.AwacsFreq, Ops.TankerFreq);
            Cfg.CallsignFromUnit = true;
            var on = (AiCs("SENTRY 1-1", "Overlord11"), AiCs("AWACS Blue", "Overlord11"));
            Cfg.CallsignFromUnit = false;
            var off = AiCs("SENTRY 1-1", "Overlord11");
            var before = Ops.RoleOf("sentry enfield 1 1 radio check");
            Ops.Callsigns = new Dictionary<string, string> { ["A"] = "Sentry 1-1", ["T"] = "Petrol 2-1" };
            (Ops.AwacsFreq, Ops.TankerFreq) = (new() { ["A"] = 251 }, new() { ["T"] = 252 });
            var roles = new[] { Ops.RoleOf("sentry enfield 1 1 radio check"), Ops.RoleOf("petrol enfield 1 1 radio check"), Ops.RoleOf("kutaisi tower enfield 1 1 radio check") };
            (Cfg.CallsignFromUnit, Ops.Callsigns, Ops.AwacsFreq, Ops.TankerFreq) = (cu, kc, ka, kt);
            if (on != ("Sentry 1-1", "Overlord11") || off != "Overlord11" || before != null || !roles.SequenceEqual(new[] { "AWACS", "Tanker", null }))
                throw new Exception($"KI-Rufzeichen aus dem Unitnamen: {on}, aus {off}, vorher {before}, Stationen {string.Join(",", roles.Select(r => r ?? "-"))}");
        }
        var nest = "{\n  \"Frequencies\": { \"Tower\": 265.0, \"ATIS\": 263.5 },\n  \"Voices\": {\n    \"Tower\": \"ryan\",   // fest\n    \"ATIS\": \"amy\"\n  }\n}";   // Tower in both blocks: only the named one changes
        var nested = Settings.WithNested(Settings.WithNested(nest, "Voices", new[] { ("Tower", "\"alan|joe\"") }, ""), "Frequencies", new[] { ("Tower", "251.5"), ("Range", "267.5") }, "");
        if (!nested.Contains("\"Range\": 267.5, \"Tower\": 251.5, \"ATIS\": 263.5 }") || !nested.Contains("\"Tower\": \"alan|joe\",   // fest") || Settings.WithNested(nest, "Voices", Array.Empty<(string, string)>(), "") != nest ||
            Settings.WithNested("{\n}", "Voices", new[] { ("Tower", "\"x\"") }, "{\"Tower\":\"x\"}") != "{\n  \"Voices\": {\"Tower\":\"x\"},\n}")
            throw new Exception("Einstellungen (Block): " + nested);
        Console.WriteLine("OK   Einstellungen: config.jsonc-Werte setzen (Kommentare bleiben), alte Option WheelStyle stört nicht, Rufzeichen \"viper11\" -> \"Viper 1-1\", Unitname \"VICTORY 9-1\" -> \"Victory 9-1\"");
        var live = Path.Combine(Path.GetTempPath(), $"DcsAtc-LiveTest-{Environment.ProcessId}.jsonc");   // R392: Bahnbefeuerung gilt nach dem Speichern sofort
        File.WriteAllText(live, "{ \"RunwayLights\": \"off\" }");
        var kept = Cfg;   // ReloadLive writes into the running Cfg: work on a copy
        try
        {
            Cfg = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(kept))!;
            Cfg.RunwayLights = "silent";
            cfgFile = live; ReloadLive();
            if (Cfg.RunwayLights != "off") throw new Exception("Einstellungen: RunwayLights nach ReloadLive " + Cfg.RunwayLights);
        }
        finally { cfgFile = null; File.Delete(live); LoadConfig(); Cfg = kept; }
        Console.WriteLine("OK   Einstellungen: RunwayLights \"off\" gilt nach ReloadLive sofort (R392)");
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
        if (StartInfo(@"C:\SRS\ExternalAudio\DCS-SR-ExternalAudio.exe", false).Environment["DOTNET_GCgen0size"] != "0x8000000" || StartInfo(Ffmpeg, true).Environment.ContainsKey("DOTNET_GCgen0size"))
            throw new Exception("#20 ExternalAudio ohne DOTNET_GCgen0size");
        Console.WriteLine("OK   #20 ExternalAudio startet mit 128 MB Gen0-Budget (kein GC während der Sendung, kein MMTimerProc-Absturz), andere Programme ohne");
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
            // client: Saved Games\DCS of Windows before a stale setup choice, the choice only when DCS is missing
            if (ClientSavedGames(sv, Path.Combine(sv, "Inst1")) != Path.Combine(sv, "Inst1") || ClientSavedGames(sv, null) != Path.Combine(sv, "DCS")) throw new Exception("Saved Games: Setup-Wahl ohne DCS-Ordner");
            Directory.CreateDirectory(Path.Combine(sv, "DCS.openbeta"));
            if (ClientSavedGames(sv, Path.Combine(sv, "Inst1")) != Path.Combine(sv, "DCS.openbeta")) throw new Exception("Saved Games: nur DCS.openbeta");
            Directory.CreateDirectory(Path.Combine(sv, "DCS"));
            if (ClientSavedGames(sv, Path.Combine(sv, "Inst1")) != Path.Combine(sv, "DCS")) throw new Exception("Saved Games: alte Setup-Wahl gewinnt");
        }
        finally { try { Directory.Delete(sv, true); } catch (IOException) { } }
        Console.WriteLine("OK   Dedicated Server: nur DCS_server läuft -> Hintergrundbetrieb (mit DCS nicht), bin\\DCS_server.exe zählt als DCS-Ordner, Saved Games: Hook (-w) vor Installer vor DCS.server/DCS.dcs_serverrelease; Client: Windows-Ordner DCS vor alter Setup-Wahl");
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
        // Forum (jojojung): wingman commands by voice -> recipient as in the DCS menu + message; not to a foreign callsign, not without a command
        string Wc(string s) => WingCmd(Tower.Normalize(s), "Enfield 1-1") is { } w ? $"{w.Par}:{w.Msg[10..]}" : "-";
        var wcs = string.Join(",", new[] { "Two, go trail.", "Enfield 1-3, rejoin", "Flight, engage bandits", "Four, mission and RTB", "flight, check in", "Two, attack my target",
            "Texaco 1-2, Enfield 1-1, request rejoin", "Two, unable", "flight level 240", "Kutaisi Tower, Enfield 1-1, ready for departure", "Two, echelon left", "Three, radar off" }.Select(Wc));
        if (wcs != "1:GoTrail,2:JoinUp,5:EngageBandits,3:FulfilTheTaskAndReturnToBase,5:FlightCheckIn,1:EngageMyTarget,-,-,-,-,1:EchelonLeft,2:RadarOff") throw new Exception("Wingman-Befehle: " + wcs);
        Console.WriteLine("OK   Wingman-Befehle: Two/Enfield 1-3/Flight + Befehl -> Empfaenger wie im DCS-Menue, fremdes Rufzeichen und Rottenfunk ohne Befehl nicht");
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
        // FI2: lat/lon grid bilinear (linear data exactly, outside linear from the edge cell); position only with grid, setting, radio and a known station
        var (llg0, sp0, fl0) = (LlGrid, Cfg.StationPositions, Fields);
        try
        {
            LlGrid = (from i in Enumerable.Range(0, 3) from j in Enumerable.Range(0, 3) select (i * 100000.0, j * 100000.0, 40.0 + i, 40.0 + 2 * j)).ToArray();
            var (pIn, pOut) = (LatLonOf(150000, 50000), LatLonOf(-50000, 250000));
            bool Near((double Lat, double Lon)? v, double la, double lo) => v is { } q && Math.Abs(q.Lat - la) < 1e-9 && Math.Abs(q.Lon - lo) < 1e-9;
            Cfg.StationPositions = true;
            Fields = Airfield.Load(Path.Combine(Root, "airfields.txt"));
            var f0 = Fields.First();
            var (aTw, aAtis, aCen, aIc) = (PosArgs(f0.StationOf("Tower"), 265), PosArgs(f0.Name + "ATIS", 263.5), PosArgs("Nowhere Control", 268.5), PosArgs(f0.StationOf("Tower"), -1));
            Cfg.StationPositions = false;
            var aOff = PosArgs(f0.StationOf("Tower"), 265);
            Cfg.StationPositions = true; LlGrid = Array.Empty<(double, double, double, double)>();
            var aNoGrid = PosArgs(f0.StationOf("Tower"), 265);
            if (!Near(pIn, 41.5, 41.0) || !Near(pOut, 39.5, 45.0) || aTw.Length != 3 || !aTw[2].StartsWith("--altitude=") || aAtis.Length != 3 || aCen.Length + aIc.Length + aOff.Length + aNoGrid.Length != 0)
                throw new Exception($"FI2 Senderposition: {pIn} {pOut} | {string.Join(" ", aTw)} | {aAtis.Length} {aCen.Length} {aIc.Length} {aOff.Length} {aNoGrid.Length}");
        }
        finally { (LlGrid, Cfg.StationPositions, Fields) = (llg0, sp0, fl0); }
        Console.WriteLine("OK   FI2: Lat/Lon aus dem Missionsraster, Platz und ATIS mit Position, Center, Intercom, ohne Raster oder Einstellung ohne");
        // #15: lat/lon -> x/z on any map (inverse of the grid), sector by Lat/Lon outside Caucasus; GCI sector: name/frequency like the AWACS, not a Center, own frequency -> AWACS
        var (th0, cs0) = (Airfield.Theatre, Tower.CenterSectors);
        try
        {
            LlGrid = (from i in Enumerable.Range(0, 3) from j in Enumerable.Range(0, 3) select (i * 100000.0, j * 100000.0, 40.0 + i, 40.0 + 2 * j)).ToArray();
            Airfield.Theatre = "GermanyCW";
            var xz = XzOf(41.5, 43.0);
            var crc = new CenterSector { Name = "Backlight", Freq = 262.1, Lat = 40.5, Lon = 41.0, RadiusNm = 60, Gci = true, Side = "blue" };
            var cen = new CenterSector { Name = "Langen Radar", Freq = 269.0, X = 50000, Z = 50000, RadiusNm = 60 };
            Tower.CenterSectors = new() { crc, cen };
            var me = new Ops.Me("Enfield 1-1", new Telemetry(5000, 0, 0, 0, 60000, 60000, 0, 0, 0), "F-16C", 2);
            var (gName, gFreq, cFor, role, red) = (Ops.AwacsName(null, me), Ops.AwacsFreqOf(null, me), Tower.CenterFor(2, 60000, 60000), SrsRole(null, 262.1), Ops.GciOf(me with { Coalition = 1 }));
            var tuned = new Ops().MyAwacs(new List<Traffic>(), me with { Freq = 262.1 });
            if (xz is not { } p || Math.Abs(p.X - 150000) > 1 || Math.Abs(p.Z - 150000) > 1 || Math.Abs(crc.Pos.X - 50000) > 1 || Math.Abs(crc.Pos.Z - 50000) > 1
                || gName != "Backlight" || gFreq != 262.1 || cFor?.Name != "Langen Radar" || role != "AWACS" || red != null || tuned != null || Tower.IsCenterCall("Backlight, Enfield 1-1, checking in"))
                throw new Exception($"#15: {xz} {crc.Pos} | {gName} {gFreq} {cFor} {role} {red?.Name}");
        }
        finally { (LlGrid, Airfield.Theatre, Tower.CenterSectors) = (llg0, th0, cs0); }
        Console.WriteLine("OK   #15: Lat/Lon auf jeder Karte aus dem Raster, GCI-Sektor mit Name und Frequenz wie das AWACS, kein Center");
    }

    /// Self-test: airfield frequencies from Radio.lua (only if DCS is installed).
    static void RadioTest()
    {
        Kneeboard.SelfTest();
        Logbook.SelfTest();
        Checkride.SelfTest();
        Csar.SelfTest();
        Lagebild.SelfTest();
        AsrApi.SelfTest();
        if (!JsonSerializer.Serialize(MapState()).Contains("\"fields\":")) throw new Exception("FI7: MapState");
        if (Tag("Träger LSO: (OK) 3-wire", "Case III", null, "Nacht") != "Träger LSO (Case III, Nacht): (OK) 3-wire" || Tag("Verstoß: x", null) != "Verstoß: x") throw new Exception("FI3: Debrief-Kennzeichen");
        // Etappe 1 (PLATZVERFAHREN-PLAN.md): Kutaisi from charts/caucasus.jsonc, CRPs as printed within 5 m of the old hand values, entries/exits/hand as before
        var kc = Airfield.Kutaisi();
        var oldCrp = new Dictionary<string, (double X, double Z)> { ["north"] = (-274941.1, 684425.5), ["south"] = (-292546.7, 688832.8), ["west"] = (-291835.5, 669472.3), ["east"] = (-280476.7, 697687.8) };
        double crpErr = oldCrp.Max(o => Math.Sqrt(Math.Pow(kc.Crp[o.Key].X - o.Value.X, 2) + Math.Pow(kc.Crp[o.Key].Z - o.Value.Z, 2)));
        if (crpErr > 5 || kc.PatternFt != 2000 || kc.MaxFt != 2200 || kc.Hand("07") != "right hand" || kc.Hand("25") != "left hand" || kc.Ramps.Length != 6 ||
            string.Join(",", kc.Entry["07"].Select(e => e.Crp + "/" + e.Via)) != "north/overhead,south/initial,west/initial" || string.Join(",", kc.Exit["25"]) != "west,north,south")
            throw new Exception($"Kartendaten Kutaisi: CRP {crpErr:F1} m, {kc.PatternFt}/{kc.MaxFt}, {kc.Ramps.Length} Vorfeldpunkte");
        Console.WriteLine($"OK   Kartendaten Kutaisi: CRPs aus caucasus.jsonc ({crpErr:F1} m), Einflug/Ausflug/Platzrunde wie bisher");
        // Paths: Known Folder Saved Games with environment variable, Steam libraries from libraryfolders.vdf
        var home = Environment.GetEnvironmentVariable("USERPROFILE");
        var libs = SteamLibraries("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"label\"\t\t\"\"\n\t}\n}").ToList();
        if (ShellFolder(@"%USERPROFILE%\Saved Games") != home + @"\Saved Games" || ShellFolder("") != null
            || string.Join("|", libs) != @"C:\Program Files (x86)\Steam|D:\SteamLibrary")
            throw new Exception($"Pfade: {ShellFolder(@"%USERPROFILE%\Saved Games")} / {string.Join("|", libs)}");
        Console.WriteLine($"OK   Pfade: %USERPROFILE%\\Saved Games -> {home}\\Saved Games, Steam-Bibliotheken {string.Join(", ", libs)}");
        var ret = "dofile('Scripts/ScriptingSystem.lua')\n\n-- local function sanitizeModule(name)\n-- do\n-- \tsanitizeModule('os')\n-- end\n";
        if (WithMissionLine(ret) is not { } rp || !rp.EndsWith(MissionLine + "\r\n") || !rp.StartsWith(ret.TrimEnd()) || WithMissionLine(rp) != null)
            throw new Exception("Retribution-Vorlage: Zeile nicht am Ende oder doppelt");
        if (!string.Equals(ExePath(Environment.ProcessId), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new Exception($"ExePath: {ExePath(Environment.ProcessId)}");
        Console.WriteLine("OK   Retribution/Liberation-Vorlage: DCS-ATC-Zeile am Ende, kein zweites Mal; Exe-Pfad über die eingeschränkte Abfrage");
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
        // Projection (charts/caucasus.jsonc): all Caucasus beacons, position vs. positionGeo
        var geo = Regex.Matches(File.ReadAllText(Path.Combine(DcsDir(), "Mods", "terrains", "Caucasus", "Beacons.lua")),
            @"position = \{ ([-\d.]+), [-\d.]+, ([-\d.]+) \};[\s\S]*?positionGeo = \{ latitude = ([-\d.]+), longitude = ([-\d.]+) \}");
        double G(Match m, int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        double projErr = geo.Max(m => Airfield.Ll2Xz(G(m, 3), G(m, 4)) is var (x, z) ? Math.Sqrt(Math.Pow(x - G(m, 1), 2) + Math.Pow(z - G(m, 2), 2)) : 0);
        if (geo.Count < 100 || projErr > 5) throw new Exception($"Projektion: {geo.Count} Baken, max. {projErr:F1} m");
        Console.WriteLine($"OK   Projektion Kaukasus: {geo.Count} Baken aus Beacons.lua, max. {projErr:F1} m");
        // Forum 0.20: fitted projection of every installed map (charts.jsonc on any map); Caucasus must hit the fixed values
        foreach (var dir in Directory.GetDirectories(Path.Combine(DcsDir(), "Mods", "terrains")))
        {
            var fit = Airfield.FitProj(Path.Combine(dir, "Beacons.lua"));
            var map = Path.GetFileName(dir);
            if (fit is not { } q || q.Err > 5 || map == "Caucasus" && (q.Lon0 != 33 || Math.Abs(q.Dx + 4998115) > 2 || Math.Abs(q.Dz + 99517) > 2))
                throw new Exception($"Projektion {map}: {fit}");
            Console.WriteLine($"OK   Projektion {map}: Mittelmeridian {q.Lon0}°, max. {q.Err:F1} m");
        }
        var th = Airfield.Theatre;
        try
        {
            Airfield.Theatre = "Syria";
            var (sx, sz) = Airfield.Ll2Xz(35.228286, 35.957919);   // Beacons.lua Syria: BANIAS homer
            if (Math.Abs(sx - 22735.7) > 5 || Math.Abs(sz - 5935.4) > 5) throw new Exception($"Ll2Xz Syria: {sx:F0}, {sz:F0}");
        }
        finally { Airfield.Theatre = th; }
    }

    /// ChartTest (PLATZVERFAHREN-PLAN.md Etappe 2): every entry of charts/caucasus.jsonc fits the DCS data in airfields.txt.
    static void ChartTest()
    {
        var file = Path.Combine(Root, "airfields.txt");
        if (!File.Exists(file)) { Console.WriteLine("--   ChartTest: airfields.txt fehlt"); return; }
        var all = Airfield.Load(file);
        var err = new List<string>();
        var hints = new List<string>();
        foreach (var c in Airfield.Chart.EnumerateObject())
        {
            var f = all.FirstOrDefault(f => f.Name == c.Name);
            if (f == null) { err.Add($"{c.Name}: nicht in airfields.txt"); continue; }
            var ends = f.Ends.Select(e => e.Name).ToHashSet();
            void Chk(bool ok, string what) { if (!ok) err.Add($"{f.Name}: {what}"); }
            double Nm(double x, double z) => Math.Sqrt((x - f.X) * (x - f.X) + (z - f.Z) * (z - f.Z)) / 1852;
            double ctrTop = c.Value.TryGetProperty("ctr_top_ft", out var ct) ? ct.GetDouble() : double.MaxValue;
            Chk(f.PatternFt < f.MaxFt && f.MaxFt <= ctrTop, $"Höhen {f.PatternFt}/{f.MaxFt}/{ctrTop}");
            if (c.Value.TryGetProperty("rwy", out var rwy))
                foreach (var r in rwy.EnumerateObject())
                {
                    Chk(ends.Contains(r.Name), $"Bahn {r.Name} nicht in DCS ({string.Join(",", ends)})");
                    if (r.Value.TryGetProperty("initial_nm", out var ini)) Chk(ini.GetDouble() is >= 1.5 and <= 6, $"initial_nm {r.Name}");
                    foreach (var leg in new[] { "entry", "exit" })
                        if (r.Value.TryGetProperty(leg, out var l))
                            foreach (var x in l.EnumerateObject()) Chk(x.Value.TryGetProperty("crs", out var crs) && crs.GetDouble() is >= 1 and <= 360, $"{leg} {r.Name} {x.Name}: crs");
                }
            Chk(f.HandOf.Values.All(h => h is "left hand" or "right hand"), "hand");
            foreach (var (k, p) in f.Crp) Chk(Nm(p.X, p.Z) is >= 3 and <= 11, $"CRP {k} {Nm(p.X, p.Z):F1} NM vom Platz");
            if (f.Charted) foreach (var e in ends) Chk(f.Entry.ContainsKey(e) && f.Exit.ContainsKey(e) && f.InitialNm.ContainsKey(e), $"Bahn {e} ohne entry/exit/initial_nm");
            foreach (var (rw, l) in f.Entry)
                foreach (var (crp, via, wp) in l)
                {
                    Chk(f.Crp.ContainsKey(crp) && via is "initial" or "overhead", $"entry {rw} {crp}/{via}");
                    Chk(!f.ExitOnly.Contains(crp), $"entry {rw}: {crp} ist exit_only");
                    if (wp is { } w) Chk(Nm(w.X, w.Z) is >= 0.5 and <= 11, $"entry {rw} {crp}: wp {Nm(w.X, w.Z):F1} NM vom Platz");
                }
            foreach (var x in f.ExitOnly) Chk(f.Crp.ContainsKey(x), $"exit_only {x} ohne crp");
            foreach (var (rw, l) in f.Exit) foreach (var crp in l) Chk(f.Crp.ContainsKey(crp), $"exit {rw} {crp}");
            // holding points beside a runway (DCS runways can be shorter than charted: Batumi 2070 m vs. 2455 m)
            foreach (var (k, h) in f.Holds)
                Chk(h.Rwy.Length > 0 && h.Rwy.All(ends.Contains) && f.Ends.Any(e => Math.Abs((h.X - e.CX) * e.Dx + (h.Z - e.CZ) * e.Dz) < e.Len / 2 + 400 && Math.Abs((h.X - e.CX) * e.Dz - (h.Z - e.CZ) * e.Dx) < 400), $"Rollhalt {k}");
            foreach (var r in f.Ramps)
            {
                Chk(Nm(r.X, r.Z) < 2, $"Vorfeld {r.Name} {Nm(r.X, r.Z):F1} NM vom Platz");
                Chk(ends.All(r.To.ContainsKey) && r.To.Keys.Concat(r.Hold.Keys).Concat(r.Vacate.Keys).Concat(r.In.Keys).All(k => k == "*" || ends.Contains(k)), $"Vorfeld {r.Name}: Bahnschlüssel");
                Chk(r.Hold.Values.All(f.Holds.ContainsKey), $"Vorfeld {r.Name}: Rollhalt unbekannt");
                Chk(r.Note.Keys.Concat(r.InNote.Keys).All(k => k == "*" || ends.Contains(k)), $"Vorfeld {r.Name}: Hinweis-Bahnschlüssel");
                // K33/K34: Vacate and return route per runway; missing one is only a hint (the text then falls back to the general wording)
                foreach (var e in ends)
                {
                    if (!r.Vacate.ContainsKey(e)) hints.Add($"{f.Name} {r.Name} {e}: kein vacate");
                    if (!r.In.ContainsKey(e) && !r.In.ContainsKey("*")) hints.Add($"{f.Name} {r.Name} {e}: kein in");
                }
            }
            foreach (var (st, nm) in f.StandChart) Chk(nm.Any(char.IsAsciiLetterOrDigit), $"Stand {st}: Name {nm}");
        }
        // Etappe 6: every holding point of the chart lies near an edge of the DCS taxi network (needs the DCS installation; outliers only reported, the data stays).
        // Runway edges do not count: a holding point is about 50 m from the runway anyway.
        if (!Directory.Exists(Path.Combine(DcsDir(), "Mods", "terrains", Airfield.Theatre, "AirfieldsTaxiways"))) Console.WriteLine("--   Rollnetz: DCS fehlt");
        else
        {
            Rollnetz.LoadAll(all, DcsDir());
            var rn = all.Where(f => f.Net != null).SelectMany(f => f.Holds.Select(h => (f, h.Key, d: f.Net!.EdgeDist(h.Value.X, h.Value.Z, (x, z) => f.OnRunway(x, z))))).ToList();
            var far = rn.Where(x => x.d > Rollnetz.HoldTol).ToList();
            Console.WriteLine($"OK   Rollnetz: {all.Count(f => f.Net != null)} Plätze mit DCS-Rollnetz, {rn.Count - far.Count} von {rn.Count} Warteplätzen liegen höchstens {Rollnetz.HoldTol:0} m von einer Rollwegkante");
            if (far.Count > 0) Console.WriteLine("--   Rollnetz Hinweis (Warteplatz weit von der DCS-Rollwegkante, Daten prüfen): " + string.Join("; ", far.Select(x => $"{x.f.Name} {x.Key} {x.d:0} m")));
        }
        if (err.Count > 0) throw new Exception("ChartTest: " + string.Join(" | ", err));
        Console.WriteLine($"OK   ChartTest: {Airfield.Chart.EnumerateObject().Count()} Plätze aus caucasus.jsonc passen zu airfields.txt");
        if (hints.Count > 0) Console.WriteLine($"--   ChartTest Hinweis (Daten ergänzen): {string.Join("; ", hints)}");
        // Forum 0.15: own charts.jsonc replaces the built-in airfield; a faulty one is ignored (built-in data stay)
        var ownF = Path.Combine(Path.GetTempPath(), "dcsatc-charts-test.jsonc");
        File.WriteAllText(ownF, "// eigene\n{ \"Kutaisi\": { \"pattern_ft\": 1234, \"crp\": { \"x\": [1, 2] }, }, }");
        var ownOk = OwnChart(ownF);
        Airfield.OwnChart = ownOk;
        var ownK = Airfield.Kutaisi();
        File.WriteAllText(ownF, "{ \"Kutaisi\": { \"crp\": { \"x\": \"kaputt\" } } }");
        var ownBad = OwnChart(ownF);
        Airfield.OwnChart = null;
        File.Delete(ownF);
        if (ownOk == null || ownK.PatternFt != 1234 || !ownK.Crp.ContainsKey("x") || ownK.Crp.ContainsKey("north") || ownBad != null || Airfield.Kutaisi().PatternFt == 1234)
            throw new Exception($"Eigene charts.jsonc: {ownOk != null} {ownK.PatternFt} {string.Join(",", ownK.Crp.Keys)} {ownBad != null}");
        Console.WriteLine("OK   Eigene charts.jsonc ersetzt den Platz, fehlerhafte wird ignoriert");
        // Forum 0.20 (Lt_Jaeger): charts.jsonc on another map, lat/lon over the fitted projection; the built-in Caucasus data stay off there
        var th = Airfield.Theatre;
        try
        {
            Airfield.Theatre = "Syria";
            File.WriteAllText(ownF, "{ \"Banias\": { \"ctr_top_ft\": 3500, \"crp\": { \"north\": \"35°18.000'N 035°57.475'E\" } } }");
            Airfield.OwnChart = OwnChart(ownF);
            var rw = new[] { ("09", 0.0, 22735.7, 5935.4, 2000.0) };
            var (sy, bat) = (Airfield.FromDcs("Banias", 22735.7, 5935.4, 8, rw).ApplyChart(), Airfield.FromDcs("Batumi", 0, 0, 10, rw).ApplyChart());
            if (!sy.Charted || sy.CtrTopFt != 3500 || Math.Abs(sy.CtrNm - 4.8) > 0.1 || bat.Charted)
                throw new Exception($"charts.jsonc Syrien: {sy.Charted} {sy.CtrTopFt} {sy.CtrNm:F2} {bat.Charted}");
            Console.WriteLine($"OK   charts.jsonc auf Syrien: Kontrollzone {sy.CtrNm:F1} NM bis {sy.CtrTopFt} ft aus eigenem CRP, Kaukasus-Daten nicht übernommen");
            // #28: "Nellis AFB" = DCS "Nellis", exact name before partial ("Tonopah Test Range" not also "Tonopah"), other maps listed; typo in "via" = faulty file
            var notes = new List<string>();
            var al = ChartAlias(new[] { "Nellis AFB", "Tonopah Test Range", "Buechel AB" }, new[] { "Nellis", "North Las Vegas", "Tonopah", "Tonopah Test Range", "Creech" }, notes);
            File.WriteAllText(ownF, "{ \"Banias\": { \"crp\": { \"n\": [1, 2] }, \"rwy\": { \"09\": { \"entry\": { \"n\": { \"crs\": 90, \"via\": \"inital\" } } } } } }");
            var typo = OwnChart(ownF);
            var de = ChartAlias(new[] { "Buechel AB", "Nörvenich", "Fassberg" }, new[] { "Buchel", "Norvenich", "Faßberg", "Bruggen" }, new());
            if (al.Count != 2 || al.GetValueOrDefault("Nellis") != "Nellis AFB" || al.GetValueOrDefault("Tonopah Test Range") != "Tonopah Test Range" || !notes.Any(x => x.Contains("Buechel AB")) || typo != null
                || de.Count != 3 || de.GetValueOrDefault("Buchel") != "Buechel AB" || de.GetValueOrDefault("Norvenich") != "Nörvenich" || de.GetValueOrDefault("Faßberg") != "Fassberg")
                throw new Exception($"charts.jsonc Namen/Prüfung: {string.Join(", ", al)} | {string.Join(" | ", notes)} | {typo != null} | {string.Join(", ", de)}");
            Console.WriteLine("OK   charts.jsonc: \"Nellis AFB\" = DCS \"Nellis\", \"Buechel AB\" = DCS \"Buchel\", fremde Karten gemeldet, Tippfehler in \"via\" erkannt");
            // Forum (Lt_Jaeger): one route name with a variant per runway ("STRYK": [ {...}, {...} ])
            File.WriteAllText(ownF, """
                { "Banias": { "land_rwy": "27", "dep_rwy": "09", "crp": { "north": [1, 2], "APEX": [3, 4] }, "rwy": { "09": { "entry": { "north": { "via": "overhead" } } }, "27": { "entry": { "APEX": { "via": "initial" } } } },
                  "routes": { "STRYK": [ { "rwy": ["09"], "to": "north", "pts": [ { "fix": "STRYK", "pos": [5, 6], "above": 9500 } ] },
                                         { "rwy": ["27"], "to": "APEX", "pts": [ { "fix": "STRYK", "pos": [5, 6] }, { "fix": "GASS_PEAK", "pos": [7, 8], "above": 8500 } ] } ] } } }
                """);
            Airfield.OwnChart = OwnChart(ownF);
            var bn = Airfield.FromDcs("Banias", 22735.7, 5935.4, 8, rw).ApplyChart();
            var st = bn.Routes.Values.Where(r => r.Name == "STRYK").ToList();
            if (st.Count != 2 || st[0].Rwy[0] != "09" || st[0].To != "north" || st[1].Rwy[0] != "27" || st[1].To != "APEX" || st[1].Pts.Length != 2 || bn.LandRwy != "27" || bn.DepRwy != "09")
                throw new Exception($"charts.jsonc Routenvarianten: {string.Join(" | ", st.Select(r => $"{r.Name} {string.Join(",", r.Rwy)} {r.To} {r.Pts.Length}"))} land {bn.LandRwy} dep {bn.DepRwy}");
            File.WriteAllText(ownF, """{ "Banias": { "land_rwy": "28" } }""");
            Airfield.OwnChart = OwnChart(ownF);
            try { Airfield.FromDcs("Banias", 22735.7, 5935.4, 8, rw).ApplyChart(); throw new Exception("charts.jsonc: land_rwy 28 auf Banias nicht erkannt"); }
            catch (FormatException fe) when (fe.Message.Contains("\"land_rwy\": \"28\"")) { }
            Console.WriteLine("OK   charts.jsonc: Route STRYK mit je einer Variante für 09 und 27, land_rwy/dep_rwy gelesen, unbekannte Bahn gemeldet");
        }
        finally { (Airfield.OwnChart, Airfield.Theatre) = (null, th); File.Delete(ownF); }
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
            // R358: only one player: a call from an unknown unit is not his (call sign decides, A26), an SRS client without unit is
            var (pb358, st358) = (Pilots.ToList(), stateTime);
            Pilots.Clear(); var one = Pilots["U1"] = new Pilot { Id = 1, Callsign = "Enfield 1-1" }; stateTime = Clock;
            SrsListener.Transmission Sx(uint unit) => new(Array.Empty<short>(), 251, unit, "x");
            var (x99, x0, x1, xf) = (PilotFor(Sx(99)), PilotFor(Sx(0)), PilotFor(Sx(1)), ByCallsign("Kutaisi Tower, Colt 2-1, inbound"));
            Pilots.Clear(); foreach (var kv in pb358) Pilots[kv.Key] = kv.Value; stateTime = st358;
            if (x99 != null || xf != null || x0 != one || x1 != one) throw new Exception($"R358: einziger Spieler nur ohne Unit-ID: {x99?.Callsign} {xf?.Callsign} {x0?.Callsign} {x1?.Callsign}");
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
        var emgNoWord = new List<Tx> { rep with { Text = "Enfield one one, Kutaisi Approach, roger, all traffic is holding.", Prio = 0 } };   // R359: Request marks emergency replies Prio 0
        foreach (var l in new[] { withCall, onlyRep, urgent, mayday, atis, infoOnly, emgNoWord }) AddStandby(sp, l);
        if (withCall[0].Standby is not { Text: "Enfield one one flight, stand by.", Prio: 0 } sb || FreqOf(sb) != 998 || withCall[1].Standby != null
            || onlyRep[0].Standby?.Text != "Enfield one one flight, stand by."
            || urgent[0].Standby != null || mayday[0].Standby != null || emgNoWord[0].Standby != null || RankOf(emgNoWord[0]) != 0 || RankOf(rep) != 3
            || atis[0].Standby?.Text != "Enfield one two, stand by." || infoOnly[0].Standby != null)
            throw new Exception("stand by: Rufzeichen/Reihenfolge/dringend");
        string mig = "\"AWACS\": \"en_US-joe-medium@1.1|en_US-ljspeech-medium\", \"Carrier\": \"en_US-ryan-medium@0.97|en_US-joe-medium@1.02\", \"SpeechRate\": 1.0,", own = "\"AWACS\": \"en_US-joe-medium@0.9\", \"SpeechRate\": 1.1,", own2 = own;   // 1.1 = pace from the settings, stays
        foreach (var (o, n) in ConfigMigrations) { mig = mig.Replace(o, n); own2 = own2.Replace(o, n); }
        if (mig != "\"AWACS\": \"en_GB-alan-medium|en_US-ljspeech-medium\", \"Carrier\": \"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02\", \"SpeechRate\": 0.85," || own2 != own || PickVoice("en_GB-gibtsnicht-medium", "X") != "en_US-ryan-medium"
            || Enumerable.Range(0, 64).Any(i => AiVoiceFor("Viper " + i).StartsWith("en_GB-alan")))
            throw new Exception($"AWACS-Stimme: Migration {mig} / eigener Wert {own2} / fehlendes Modell / KI-Pilot mit alan");
        var (keepVoices, keepUnits) = (aiVoices, Flights.Units);   // KF71: one voice per AI flight
        aiVoices = AiPool(_ => true); aiSlot.Clear();
        Flights.Unit fu(string n, string g, string cs) => new(n, g, "FA-18C_hornet", cs, 2, 0, 0, 6000, true, 0.8, 251, new[] { 0, 0, 0, 0, 0 });
        Flights.Units = new[] { fu("a1", "A", "Enfield11"), fu("a2", "A", "Enfield12"), fu("b1", "B", "Uzi11"), fu("c1", "C", "Ford11") }.ToDictionary(x => x.Name);
        string va1 = AiVoiceFor("a1"), va2 = AiVoiceFor("a2"), vb1 = AiVoiceFor("b1"), vc1 = AiVoiceFor("c1");
        if (aiVoices.Length < 2 || va1 == vb1 || va1 == va2 || va1 != AiVoiceFor("a1") || vb1 != AiVoiceFor("b1") || (aiVoices.Length > 2 && new[] { va1, vb1, vc1 }.Distinct().Count() < 3)
            || new[] { va1, va2, vb1, vc1 }.Any(v => v.Contains('@') || v.StartsWith("en_GB-alan")) || aiVoices[0] == Cfg.PilotVoice.Split('@')[0])
            throw new Exception($"KI-Stimmen je Flight: {string.Join(", ", aiVoices)} / {va1} {va2} {vb1} {vc1}");
        var inst = new[] { "a", "b", "joe" };   // "random": all installed without the pilot's voice, pitch kept; unknown models drop out; settings window writes voices back as they were
        if (string.Join(",", VoicePool("random@0.9", inst, "joe")) != "a@0.9,b@0.9" || string.Join(",", VoicePool("b|x|random", inst, "")) != "b,a,b,joe" || VoicePool("x", inst, "")[0] != "en_US-ryan-medium"
            || new Config().Voices.Values.Append("random").Append("a@1.1|b@0.9").Any(v => Settings.VoiceText(Settings.VoiceParts(v)) != v))
            throw new Exception("Stimmen: random / Hin- und Rueckweg");
        (aiVoices, Flights.Units) = (keepVoices, keepUnits); aiSlot.Clear();
        Tx ki(string t, string role = "Tower", int prio = 2) => new(t, role, "Kutaisi Tower", 0, Prio: prio);   // A134: display duration in the .out line
        int d1 = ShowSec(ki("Enfield one one, cleared to land."), ""), d2 = ShowSec(ki(new string('x', 100)), ""), d3 = ShowSec(ki(new string('x', 52), "Flight", 1), ""),
            d4 = ShowSec(ki("Enfield one one, roger.", "Tower", 1), "Kutaisi Tower (265.0): "), d5 = ShowSec(ki(new string('x', 400), "Tower", 1), "");
        if (d1 != 6 || d2 != 8 || d3 != 7 || d4 != 20 || d5 != 50) throw new Exception($"Anzeigedauer {d1} {d2} {d3} {d4} {d5}");
        Console.WriteLine($"OK   Funk: Sicherheit {c:0.00}, go around dringend, LSO-Calls Prio 0 ohne stand by (2/4 s), KI-Funk und laufender Spruch zählen nicht, Antwort hinter 8 s Spruch -> stand by (Rufzeichen der Antwort, nicht vor dringender/Notfall-Antwort), AWACS-Stimme alan (kein KI-Pilot), KI-Stimme je Flight (Lead fest, Flügelmann anders, ohne Tonhöhe)");
        var model = Path.Combine(Root, "models", "en_US-ryan-medium.onnx");
        if (!File.Exists(model)) { Console.WriteLine("--   Piper: Stimme fehlt"); return; }
        var wav = Path.Combine(Path.GetTempPath(), "DcsAtc-piper.wav");
        var sw = Stopwatch.StartNew();
        bool ok = PiperWarm(model, "1", "0.2", "radio check", wav) && PiperWarm(model, "1", "0.2", "Enfield one one, Kutaisi Tower, cleared for takeoff.", wav);
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
            // R356: text "to all" only for the coalition that hears the audio: blue Gid 3 and red Gid 4 both on Guard and 251.0
            var (pb356, srs356) = (Pilots.ToList(), Srs);
            try
            {
                Pilots.Clear();
                (Pilots["B"], Pilots["R"]) = (new Pilot { Unit = "B", Gid = 3, Id = 9, Coalition = 2 }, new Pilot { Unit = "R", Gid = 4, Id = 8, Coalition = 1 });
                Srs = null;
                var noSrs = string.Join(",", ShowTo(0, 251.0, 0, 2));   // without SRS: gid 0, Show -> coalition text
                Srs = new SrsListener(5002, Array.Empty<double>(), _ => false, _ => { });
                string Cl(int unit) => $$$"""{"ClientGuid":"g{{{unit}}}","Name":"x{{{unit}}}","RadioInfo":{"unitId":{{{unit}}},"radios":[{"freq":243000000},{"freq":251000000}]}}""";
                Srs.OnServerMessage($$$"""{"MsgType":1,"Clients":[{{{Cl(9)}}},{{{Cl(8)}}}]}""");
                var (gRed, aBlue, all, own) = (string.Join(",", ShowTo(0, Flights.Guard, Flights.GuardVhf, 1)), string.Join(",", ShowTo(0, 251.0, 0, 2)),
                                               string.Join(",", ShowTo(0, 251.0, 0).OrderBy(g => g)), string.Join(",", ShowTo(3, Flights.Guard, Flights.GuardVhf, 1)));
                if (noSrs != "0" || gRed != "4" || aBlue != "3" || all != "3,4" || own != "3")
                    throw new Exception($"R356 Text nach Koalition: ohne SRS {noSrs}, rote Mayday auf Guard {gRed}, blaue AWACS-Zeile {aBlue}, ohne Seite {all}, an die eigene Gruppe {own}");
                Console.WriteLine("OK   R356: Text an alle nur für die Koalition (rote Mayday auf Guard nur Gid 4, blaue AWACS-Zeile nicht an Rot, ohne SRS Koalitionstext)");
            }
            finally { Srs = srs356; Pilots.Clear(); foreach (var kv in pb356) Pilots[kv.Key] = kv.Value; }
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
            // #13: "Carrier" = own frequency per carrier station (all carriers), unknown station / no frequency -> one line each; listened to and routed as Carrier (with a carrier)
            var cvBad = ApplyFreqs(Fields, OwnFreqs("""{ "Carrier": { "Marshal": 285.0, "tower": [308.0], "CATCC": 282.0, "Paddles": 900 } }""", "Nevada").Own);
            var (cvSf, boats0) = (new Dictionary<string, double>(Carrier.StationFreqs), Carrier.Boats);
            Carrier.Boats = new() { new Carrier.Boat("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 127.5, "73X", "11") };
            var (cvRole, cvListen) = (SrsRole(null, 285.0), ListenFreqs(new()).Contains(308.0));
            Carrier.Boats = boats0;
            ApplyFreqs(Fields, new());
            if (cvSf.Count != 2 || cvSf["Marshal"] != 285.0 || cvSf["Tower"] != 308.0 || cvBad.Count != 2 || !cvBad[0].Contains("\"CATCC\"") || !cvBad[1].Contains("900") || cvRole != "Carrier" || !cvListen || Carrier.StationFreqs.Count != 0)
                throw new Exception($"#13 Trägerfrequenzen: {string.Join(",", cvSf)} | {string.Join(" | ", cvBad)} | {cvRole} {cvListen}");
            Console.WriteLine("OK   #13: frequencies.jsonc \"Carrier\" je Station, Fehler je Zeile, abgehört und als Träger erkannt");
            // #18: the examples work uncommented (above all headings = every map), "Carrier" also under an own heading, Red Crown before the mission frequency,
            // CenterSectors added to config.jsonc and errors per entry, invalid file points to the examples
            var ex = Regex.Replace(FreqFile(null, new SortedDictionary<string, Dictionary<int, (double Uhf, double Vhf, string Cs, string Name)>>(), "Nevada", new()), @"(?m)^(\s*)// (?=\s*(\{ ""Name""|""Carrier""|""CenterSectors""))", "$1");
            var (exOwn, exErr) = OwnFreqs(ex, "Nevada");
            var exBad = ApplyFreqs(Fields, exOwn);
            var (exSf, exCs, rs0) = (new Dictionary<string, double>(Carrier.StationFreqs), Tower.CenterSectors.ToList(), Carrier.RadarShips);
            Carrier.RadarShips = new() { new("CG-60", "CVN", "TICONDEROG", 0, 0, 2, 300.0) };
            var (exRc, exRole) = (Carrier.RedCrownFor(2, null)?.Freq, SrsRole(null, 252.5));
            Carrier.RadarShips = rs0;
            var hd = OwnFreqs("{\n  // ===== Nevada =====\n  \"Nellis\": { \"Tower\": 327.0 },\n  // ===== Carrier CVN =====\n  \"Carrier\": { \"Marshal\": 285.0 },\n  // ===== Sinai =====\n  \"Ramon\": { \"Tower\": 251.0 },\n}", "Nevada").Own;
            var csBad = ApplyFreqs(Fields, OwnFreqs("""{ "CenterSectors": [ { "Erndtebrück": "Loneship", "Freq": 276.75 }, { "Name": "X", "Freq": 1000 }, { "name": "Y", "freq": 270.0, "x": 1000, "z": 2000 } ] }""", "Nevada").Own);
            var csY = Tower.CenterSectors.LastOrDefault();
            var csBad2 = ApplyFreqs(Fields, OwnFreqs("""{ "CenterSectors": { "Name": "X" } }""", "Nevada").Own);
            var bad18 = OwnFreqs("{\n  \"Nellis\": { \"Tower\": 327.0 },\n  {\"Erndtebrück\": \"Loneship\", \"Freq\": 276.75 }\n}", "Nevada");
            ApplyFreqs(Fields, new());
            if (exErr != null || exBad.Count != 0 || exSf.Count != 7 || exSf["RedCrown"] != 252.5 || exCs.Count != Cfg.CenterSectors.Count + 2 || !exCs[^1].Gci || exCs[^1].Lat != 50.58 || exRc != 252.5 || exRole != "Carrier"
                || hd.Count != 2 || !hd.ContainsKey("Nellis") || !hd.ContainsKey("Carrier") || csBad.Count != 2 || !csBad[0].Contains("\"Name\"") || !csBad[1].Contains("30–400") || csY is not { Name: "Y", Freq: 270.0, X: 1000, Z: 2000 }
                || csBad2.Count != 1 || bad18.Error == null || bad18.Own.Count != 0 || Tower.CenterSectors.Count != Cfg.CenterSectors.Count || Carrier.StationFreqs.Count != 0)
                throw new Exception($"#18 Beispiele: {exErr} | {string.Join(" | ", exBad)} | {string.Join(",", exSf)} | {exCs.Count} {exRc} {exRole} | {string.Join(",", hd.Keys)} | {string.Join(" | ", csBad.Concat(csBad2))}\r\n{ex}");
            Console.WriteLine("OK   #18: Beispiele in frequencies.jsonc gehen ohne // (7 Trägerstationen mit Red Crown, Center und GCI), \"Carrier\" unter eigener Überschrift, CenterSectors-Fehler je Eintrag, kaputte Datei nennt die Beispiele");
            // create frequencies.jsonc (test Radio.lua: name from comment or beacon, config without UHF), uncomment a line, add without changing user lines
            var dir = Path.Combine(Path.GetTempPath(), "DcsAtc-FreqTest");
            Directory.CreateDirectory(Path.Combine(dir, "Mods", "terrains", "TestMap"));
            File.WriteAllText(Path.Combine(dir, "Mods", "terrains", "TestMap", "Radio.lua"), "radio = {\r\n\t{\r\n\t\t-- Alpha_Field\r\n\t\tradioId = 'airfield1_0';\r\n" +
                "\t\tcallsign = {{[\"common\"] = {_(\"Alpha\"), \"Alpha\"}}};\r\n\t\tfrequency = {[UHF] = {MODULATIONTYPE_AM, 250000000.000000}, [VHF_HI] = {MODULATIONTYPE_AM, 121000000.000000}};\r\n\t};\r\n" +
                "\t{\r\n\t\tradioId = 'airfield2_0';\r\n\t\tcallsign = {{[\"common\"] = {_(\"OMXX\"), \"OMXX\"}}};\r\n\t\tfrequency = {[VHF_HI] = {MODULATIONTYPE_AM, 118750000.000000}};\r\n\t};\r\n};\r\n");
            File.WriteAllText(Path.Combine(dir, "Mods", "terrains", "TestMap", "Beacons.lua"), "beacons = {\r\n\t{\r\n\t\tdisplay_name = _('Bravo-Intl');\r\n\t\tbeaconId = 'airfield2_0';\r\n\t};\r\n};\r\n");
            var tab = Airfield.RadioTable(dir, "TestMap");
            File.WriteAllText(Path.Combine(dir, "Mods", "terrains", "TestMap", "entry.lua"), "local self_ID = \"TestMapTheatre\";\r\ndeclare_plugin(self_ID, theatre);\r\n");   // #19: mission SinaiMap, folder Sinai
            var (tabTh, dirTh) = (Airfield.RadioTable(dir, "TestMapTheatre").Count, Path.GetFileName(Airfield.TerrainDir(dir, "TestMapTheatre")));
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
            if (tab.Count != 2 || tabTh != 2 || dirTh != "TestMap" || OwnFreqs(user, "TestMapTheatre", dirTh).Own.Count != 1 || tab[1] != (250.0, 121.0, "Alpha", "Alpha Field") || tab[2] != (0, 118.75, "", "Bravo-Intl")
                || !gen.StartsWith("// DCS-ATC") || !gen.Contains("\r\n{\r\n" + string.Join("\r\n", FreqExamples) + "\r\n  // ===== TestMap =====\r\n" + alpha + "\r\n  // \"Bravo-Intl\": { \"ATIS\": " + atisMhz + ", \"Ground\": [118.75], \"Tower\": [118.75],") || !gen.EndsWith("\r\n}\r\n")
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
            Console.WriteLine("OK   frequencies.jsonc: aus Radio.lua erzeugt (Name aus Kommentar/Funkfeuer, nur VHF als [VHF]), entkommentierte Zeile mit Komma am Ende gelesen, Ergänzen: neue Karte und neuer Platz, Nutzerzeile unverändert, unveränderte Zeile bekommt den Missionsnamen, #19 Missionskarte über entry.lua im Kartenordner (SinaiMap in Sinai)");        }
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

            // Forum: Funkgerät 2 auf die AWACS-Frequenz, die zugleich Platzfrequenz ist (Krasnodar 251.0) -> kein Platzwechsel
            var uhf0 = (kut.Uhf, sen.Uhf);
            (kut.Uhf, sen.Uhf) = (263, 251);
            var pa = new Pilot { Unit = "C2", Callsign = "Enfield 2-1", Gid = 7, Id = 77, Tel = At(-12, Math.PI) };
            Srs = new SrsListener(5002, Array.Empty<double>(), _ => false, _ => { });
            void Tune(string b) => Srs.OnServerMessage($$$"""{"MsgType":1,"Clients":[{"ClientGuid":"g","Name":"x","RadioInfo":{"unitId":77,"radios":[{"freq":263000000},{"freq":{{{b}}}}]}}]}""");
            Tune("0"); Pick(pa, null);
            Ops.AwacsFreq["AwacsTest"] = 251;
            Tune("251000000");
            var awacs = Pick(pa, null)?.F.Name;
            Ops.AwacsFreq.Remove("AwacsTest");
            (pa.LastFq, pa.Fresh) = (null, null);
            Tune("0"); Pick(pa, null); Tune("251000000");
            var field = Pick(pa, null)?.F.Name;
            (Srs, kut.Uhf, sen.Uhf) = (null, uhf0.Item1, uhf0.Item2);
            if (awacs != "Kutaisi" || field != "Senaki") throw new Exception($"Pick AWACS auf Platzfrequenz: {awacs}, ohne AWACS {field}");
            Console.WriteLine("OK   Pick: AWACS-Frequenz auf Funkgerät 2 wechselt den Platz nicht, auch wenn ein Platz dieselbe Frequenz hat");

            // geteilte Frequenz (Platz = AWACS): ohne Stationsnamen Rückfrage, Name entscheidet und gilt für weitere Sprüche ohne Namen
            Take();
            var ps = new Pilot { Unit = "C3", Callsign = "Enfield 3-1", Gid = 7, Id = 78, Tel = At(-12, Math.PI) };
            var s1 = SharedChoice(ps, 251, "Enfield 3-1, radio check", sen, "AWACS");
            var ask = Take().FirstOrDefault()?.Text ?? "-";
            var s2 = SharedChoice(ps, 251, "Overlord, Enfield 3-1, picture", sen, "AWACS");
            var s3 = SharedChoice(ps, 251, "Enfield 3-1, say again", sen, "AWACS");
            var s4 = SharedChoice(ps, 251, "Senaki Approach, Enfield 3-1, inbound for landing", sen, "AWACS");
            var s5 = SharedChoice(ps, 251, "Enfield 3-1, say again", sen, "AWACS");
            var s6 = SharedChoice(ps, 252, "Enfield 3-1, say again", sen, "AWACS");
            Take();
            if (s1 != null || !ask.Contains("Senaki") || s2 != false || s3 != false || s4 != true || s5 != true || s6 != null)
                throw new Exception($"Geteilte Frequenz: {s1} '{ask}' {s2} {s3} {s4} {s5} {s6}");
            // #23: Platz auf seiner Frequenz beim Namen gerufen -> Platz, auch mit Tankernamen im Spruch (ARCOE-Recovery); ohne Platznamen oder ohne Platzfrequenz wie bisher nach Inhalt
            var o23 = new[] { OpsByContent(ps, "Senaki Approach, Enfield 3-1 inbound. Full landing. Request Arco.", sen), OpsByContent(ps, "Senaki Approach, Enfield 3-1 inbound. Full landing. Request Arco.", null),
                              OpsByContent(ps, "Arco, Enfield 3-1, request rejoin", sen), OpsByContent(ps, "Enfield 3-1, request picture", sen),
                              OpsByContent(ps, "324 decimal 4, Darkstar for Enfield 3-1. Have a good one. Good day.", sen), OpsByContent(ps, "324 decimal 4, Darkstar for Enfield 3-1. Have a good one. Good day.", null),
                              OpsByContent(ps, "Darkstar, Enfield 3-1, checking in on 324 decimal 4", sen) };
            if (!o23.SequenceEqual(new[] { null, "Tanker", "Tanker", "AWACS", null, "AWACS", "AWACS" })) throw new Exception("#23 Station nach Inhalt auf Platzfrequenz: " + string.Join(",", o23.Select(x => x ?? "Platz")));
            Console.WriteLine("OK   Geteilte Frequenz (Platz = AWACS): Rückfrage ohne Stationsnamen, Name entscheidet und bleibt");
            var boats13 = Carrier.Boats;   // #13: carrier frequency = airfield frequency (Nevatim 225.0): "Marshal" names the carrier
            Carrier.Boats = new() { new Carrier.Boat("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 225, "73X", "11", Wx: 10) };
            var pc = new Pilot { Unit = "C4", Callsign = "Uzi 1-1", Gid = 8, Id = 79, Tel = At(-12, Math.PI) };
            var c13 = SharedChoice(pc, 225, "Marshal 010, marking MOMS 160443, Angel 31, state 14.7.", sen, "Carrier");
            Carrier.Boats = boats13;
            Take();
            if (c13 != false) throw new Exception($"#13 geteilte Trägerfrequenz: Marshal -> {c13}");
            Console.WriteLine("OK   #13 Geteilte Frequenz (Platz = Träger): \"Marshal ...\" geht an den Träger");

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
            // R360: "May day" as transcribed from the wingman -> own flow as well; R359: "declaring emergency" without signal word -> reply with priority 0 (no "stand by")
            Take();
            var ml = new Pilot { Unit = "R3", Callsign = "Enfield 6-1", Gid = 14, Tel = At(12, Math.PI) };
            var mg = new Pilot { Unit = "R4", Callsign = "Enfield 6-2", Gid = 14, Tel = At(12.2, Math.PI) };
            var de = new Pilot { Unit = "R5", Callsign = "Enfield 7-1", Gid = 15, Tel = At(14, Math.PI) };
            (Pilots["R3"], Pilots["R4"]) = (ml, mg);
            FormFlights(new() { ml, mg });
            bool mIn = mg.Lead == ml;
            Request(mg, "May day, may day, may day, Kutaisi Approach, Enfield 6-2, engine failure", "T", false);
            var rmd = Take().Where(t => t.Role != "Info").ToList();
            Pilots.Remove("R3"); Pilots.Remove("R4");
            Request(de, "Kutaisi Approach, Enfield 7-1, declaring emergency, engine failure", "T", false);
            var rde = Take().Where(t => t.Role != "Info").ToList();
            if (!mIn || mg.Lead != null || mg.Active?.Emergency != true || rmd.Count == 0 || !rmd[0].Text.StartsWith("Enfield six two, Kutaisi Approach, roger mayday")
                || rde.Count == 0 || !rde[0].Text.Contains("roger, all traffic is holding") || rde[0].Prio != 0 || RankOf(rde[0]) != 0)
                throw new Exception($"R360/R359 May day Rottenflieger / declaring emergency: im Flug {mIn}, Lead {mg.Lead?.Callsign}, {string.Join(" | ", rmd.Concat(rde).Select(t => $"{t.Prio}:{t.Text}"))}");
            Console.WriteLine($"OK   R360/R359: \"May day\" des Rottenfliegers -> eigener Ablauf, \"declaring emergency\" mit Vorrang ({rde[0].Text[..48]}…)");

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
            // R360: "Pan-pan" as transcribed (hyphens) on the frequency of the now hostile Kutaisi: emergency to an own airfield, not only the hint
            var q360 = new Pilot { Unit = "C3", Callsign = "Enfield 1-3", Gid = 8, Tel = At(10, Math.PI) };
            Request(q360, "Pan-pan, pan-pan, pan-pan, Kutaisi Approach, Enfield 1-3, hydraulic failure", "SRS", false, kut);
            var r360 = Take().Where(t => t.Role != "Info").ToList();
            if (q360.Active?.F != sen || q360.Active?.Emergency != true || r360.Count == 0 || !r360[0].Text.Contains("roger pan pan"))
                throw new Exception($"R360 Pan-pan am Feindplatz: {q360.Active?.F.Name}, {string.Join(" | ", r360.Select(t => t.Text))}");
            Console.WriteLine($"OK   R360: \"Pan-pan\" auf der Frequenz des Feindplatzes -> {sen.Name} ({r360[0].Text[..40]}…)");

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
        var (ms21, at21) = (missionSec, Atis.ToDictionary(a => a.Key, a => a.Value));
        (missionSec, Atis[Fields[0].Name]) = (500, new AtisInfo { Letter = 'N' });   // LA21: mission run time and identifier at saving
        Pilots["U1"] = sp; (savedAt, saved) = (-99, new()); SaveSession(); Pilots.Remove("U1"); sp.Boat.Leave(); saved = null;
        var (rp, far) = (new Pilot { Unit = "U1", Callsign = "Ford 5-1", Tel = sp.Tel with { X = 1500 } }, new Pilot { Unit = "U1", Tel = sp.Tel with { X = 5 * NM } });
        Atis.Clear(); missionSec = 20;   // LA21: restarted mission (run time smaller), same spot: nothing taken over
        var rs = new Pilot { Unit = "U1", Callsign = "Ford 5-1", Tel = sp.Tel with { X = 1500 } }; RestoreSession(rs); saved = null;
        bool restart = rs.Active == null && rs.Ops.Debrief.Count == 0 && Atis.Count == 0;
        missionSec = 510;
        RestoreSession(far); saved = null;
        RestoreSession(rp); saved = null;
        File.Delete(SessionFile); SessionFile = sf;
        bool restored = rp.Active?.Phase == Phase.Inbound && rp.Active.HoldInfo?.Ft == 4000 && rp.Active.Following && rp.Boat.Stage == 1 && rp.Ops.Debrief.Count == 1;
        restored &= restart && Atis.TryGetValue(Fields[0].Name, out var n21) && n21.Letter == 'N';   // LA21: identifier N stays after the app restart
        missionSec = ms21; Atis.Clear(); foreach (var (k21, v21) in at21) Atis[k21] = v21;
        var vt = new Tower(Fields[0], "Ford 5-1");   // Radar vectoring was running (not in holding): re-plan instead of radio silence
        vt.Load(new Tower.State(Phase.Inbound, "25", null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, true, false, null, null, 0, true, Vectoring: true),
                new Telemetry(1500, 1200, 120, Math.PI, Fields[0].X + 15 * NM, Fields[0].Z, 0, 0, 0), 0);
        restored &= vt.Vectoring;
        rp.Boat.Leave();
        if (!restored || far.Active != null || far.Boat.Stage != 0) throw new Exception($"Sitzung: {rp.Active?.Phase}/{rp.Boat.Stage}/{rp.Ops.Debrief.Count}, weit weg {far.Active?.Phase}, Neustart {restart}");
        Console.WriteLine("OK   Sitzung: Phase, Holding, Träger-Stack, Debriefing, ATIS-Kennung nach Neustart übernommen; Missions-Neustart (Ort oder Laufzeit) nicht");
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
        // Center: "Tbilisi Control, ..." is a call to the Center, not to an airfield Tbilisi (its Approach stays one); the name changes with the map
        var tbi = Af("Tbilisi", 60);
        Fields = new() { kut, tbi, bat };
        var nearTbi = new Telemetry(0, 0, 0, 0, tbi.X, tbi.Z, 0, 0, 0);
        var cenCalls = new[] { Called("Tbilisi Control, Enfield 1-1, checking in", nearTbi, null), Called("Rostov Control, Enfield 1-1, request direct Batumi", nearTbi, null), Called("Tbilisi Approach, Enfield 1-1", nearTbi, null),
                               Called("Enfield 1-1, contact Tbilisi Control two six eight decimal five", nearTbi, null) };
        var thCen = Airfield.Theatre;
        Airfield.Theatre = "Nevada";
        var cenNv = Called("Los Angeles Center, Enfield 1-1, checking in", nearTbi, null);
        Airfield.Theatre = thCen;
        Fields = new();
        if (cenCalls[0] != null || cenCalls[1] != null || cenCalls[2] != tbi || cenCalls[3] != null || cenNv != null)
            throw new Exception($"Center-Anruf: {string.Join(",", cenCalls.Append(cenNv).Select(f => f?.Name ?? "-"))}");
        Console.WriteLine("OK   Center: \"Tbilisi Control\" / \"Rostov Control\" / \"Los Angeles Center\" rufen keinen Platz, \"Tbilisi Approach\" schon");
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
        var sa = new Tower(mv, "Enfield 1-1").SayAgain("Minvody Approach, Enfield 1-1, inbound for landing", 0)[0];
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
        {   // LA10 (log Nellis): expected next call below 0.65 is accepted from 0.50 ("Overhead." 0.60 after "report overhead" -> join), below 0.50 or unexpected -> say again;
            // LA11: the SRS frequency of the call picks the controller ("Join fire." 0.23 on 385.4 after the handover to the Tower -> Approach)
            var k10 = Airfield.Kutaisi();
            (k10.Own["Ground"], k10.Own["Tower"], k10.Own["Approach"], k10.Own["Departure"]) = (new[] { 275.8 }, new[] { 327.0 }, new[] { 385.4 }, new[] { 385.4 });
            var p10 = new Pilot { Unit = "U10", Callsign = "Trident 5-1", Id = 10, Tel = new Telemetry(k10.Elev + 900, 900, 150, 254 * Math.PI / 180, k10.X, k10.Z, 0, 0, 0) };
            (SimLog, Fields) = (new(), new() { k10 });
            var r10 = new List<string>();
            foreach (var (said, c10, fq) in new[] { ("Tower: Kutaisi Tower, Trident 5-1. Overhead.", 0.60, 327.0), ("Tower: Kutaisi Tower, Trident 5-1. Overhead.", 0.45, 327.0),
                                                    ("Tower: Trident 5-1, request startup.", 0.60, 327.0), ("Join fire.", 0.23, 385.4) })
            {
                var t10 = TowerFor(p10, k10);
                t10.Load(t10.Save() with { Phase = Phase.Entering, NavName = "overhead", Nav = new[] { k10.X, k10.Z } }, p10.Tel, Now());
                (p10.Expect, p10.CalledOn) = (("Tower", "report overhead", Now() + 300), fq);
                Request(p10, said, "SRS", false, k10, c10);
                r10.Add(string.Join(" | ", SayQueue.Select(x => $"{x.Role}: {x.Text}")));
                while (SayQueue.TryTake(out _)) { }
            }
            (SimLog, Fields) = (null, new());
            if (!r10[0].Contains("join") || !r10[0].Contains("downwind") || r10[0].Contains("say again") || !r10[1].EndsWith("say again.") || !r10[2].EndsWith("say again.")
                || r10[3] != "Approach: Trident five one, Kutaisi Approach, say again.")
                throw new Exception("LA10/LA11 Schwelle/Frequenz: " + string.Join(" || ", r10));
            Console.WriteLine("OK   LA10 erwarteter Spruch ab 0.50 (\"Overhead.\" 0.60 -> Pattern), darunter oder unerwartet say again; LA11 \"Join fire.\" auf 385.4 -> Approach");
        }
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
                || !wr[5].StartsWith("#AWACS:") || !wr[5].Contains("Kutaisi bears") || !wr[6].StartsWith("#Tanker:") || !wr[7].StartsWith("#Tanker:") || !wr[7].Contains("continue")
                || !wr[7].EndsWith("report visual."))   // TP6: the request presses no DCS menu (only the join), TP1: the second request does not restart
                throw new Exception("Falsche Frequenz: " + string.Join(" || ", wr));
            Console.WriteLine("OK   Falsche Frequenz: \"Request startup\" auf Tanker 255.5 (AWACS/Träger ebenso) -> kein DCS-Tankermenü, kein \"say again\", Hinweis \"Kutaisi Ground: 263.0 / 134.0\"; unbekanntes \"request …\" -> \"say again\" ohne Tankermenü; \"request rejoin\" ohne Menüdruck (erst beim Join, TP6), zweiter Request -> \"continue\" (TP1)");
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
        // R387: tanker frequency tuned by DCS is monitored and belongs to the Tanker role of its sender (before the airfield frequency, which is equal here); "complete" resets it
        pr.TankFq = 261.0;
        var r387 = $"{SrsRole(pr, 261.0)} {SrsRole(null, 261.0) ?? "-"}";
        pr.TankFq = 263.0;
        var r387l = ListenFreqs(new() { pr }).Contains(263.0);
        Request(pr, "Tanker: Texaco, Enfield 1-1, complete", "RAD", true);
        if (r387 != "Tanker -" || !r387l || pr.TankFq != 0 || ListenFreqs(new() { pr }).Contains(263.0)) throw new Exception($"R387 Tanker-Missionsfrequenz: {r387} / hört {r387l} / nach complete {pr.TankFq}");
        Console.WriteLine("OK   R387: vom DCS eingestellte Tankerfrequenz wird abgehört und dem Tanker des Senders zugeordnet, nach 'complete' zurückgesetzt");
        // TP7: tanker frequency from the mission (F line with "t") is monitored, belongs to the Tanker role, the tanker answers there too (also "Name#002" spawned from the template)
        Ops.TankerFreq["Aerial-1"] = 225.0; pr.Ops.TankerGroup = "Aerial-1#002";
        var tp7 = $"{SrsRole(null, 225.0)} {ListenFreqs(new() { pr }).Contains(225.0)} {Program.Fq(OpsTx(pr, new("Tanker", "Bloodhound", "x")).Freqs)}";
        Ops.TankerFreq.Clear(); pr.Ops.TankerGroup = "";
        if (tp7 != "Tanker True 255.5/225.0") throw new Exception("TP7 Tanker-Missionsfrequenz: " + tp7);
        Console.WriteLine("OK   TP7: Tankerfrequenz aus der Mission wird abgehört, dem Tanker zugeordnet, Antwort auch dort");
        // Issue #3: AWACS and tanker on one frequency (Instant Action): the station name decides and is remembered for unnamed calls; a pure AWACS frequency stays AWACS
        (Ops.AwacsFreq["I3-AW"], Ops.TankerFreq["I3-TK"], Ops.AwacsFreq["I3-AW2"]) = (230.0, 230.0, 240.0);
        var i3 = string.Join(" ", new[] { "Enfield 1-1, radio check", "Texaco, Enfield 1-1, request rejoin", "Enfield 1-1, ready pre-contact", "Overlord, Enfield 1-1, request picture", "Enfield 1-1, say again" }
                                  .Select(s => OpsPick(pr, 230.0, s, SrsRole(pr, 230.0)!)).Append(OpsPick(pr, 240.0, "Texaco, Enfield 1-1, request rejoin", "AWACS")));
        Ops.AwacsFreq.Remove("I3-AW"); Ops.AwacsFreq.Remove("I3-AW2"); Ops.TankerFreq.Clear(); pr.OpsPick = null;
        if (i3 != "AWACS Tanker Tanker AWACS AWACS AWACS") throw new Exception("Issue #3 AWACS und Tanker auf einer Frequenz: " + i3);
        Console.WriteLine("OK   Issue #3: AWACS und Tanker auf einer Frequenz, der Stationsname entscheidet und gilt für Rufe ohne Namen");
        // Issue #17 (log): recovery tanker on the carrier frequency (Instant Action) -> the Marshal call went to the tanker ("no tanker airborne"); carrier words decide like station names
        var boats17 = Carrier.Boats;
        Carrier.Boats = new() { new Carrier.Boat("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 225.0, "73X", "11") };
        Ops.TankerFreq["I17-TK"] = 225.0;
        var i17 = string.Join(" ", new[] { "Marshall 103, marking Moms 145 for 32, engines 18, state 9.7.", "103, see you at 2", "Arco, Enfield 1-1, request rejoin", "Enfield 1-1, ready pre-contact", "Marshal, 103, checking in" }
                                   .Select(s => OpsPick(pr, 225.0, s, SrsRole(pr, 225.0)!)));
        Carrier.Boats = boats17; Ops.TankerFreq.Clear(); pr.OpsPick = null;
        if (i17 != "Carrier Carrier Tanker Tanker Carrier") throw new Exception("Issue #17 Träger und Tanker auf einer Frequenz: " + i17);
        Console.WriteLine("OK   Issue #17: Träger und Tanker auf einer Frequenz, Marshal/Mother/Ball entscheiden für den Träger, Tankername für den Tanker");
        // R393: "say again" without a station goes to the controller who spoke last (ops role within 5 minutes), otherwise to the airfield (which clears it)
        pr.LastOps = ("Tanker", Now());
        Request(pr, "Enfield 1-1, say again", "RAD", false);
        var r393 = pr.LastOps?.Role ?? "-";
        pr.LastOps = ("Tanker", Now() - 400);
        Request(pr, "Enfield 1-1, say again", "RAD", false);
        r393 += " " + (pr.LastOps?.Role ?? "-");
        if (r393 != "Tanker -") throw new Exception("R393 say again ohne Station: " + r393);
        Console.WriteLine("OK   R393: say again ohne Stationsnamen geht an den zuletzt sprechenden Tanker/Range/AWACS (binnen 5 min), sonst an den Platz");
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
        string SrsClient(string id, string name, int unit) => $"{{\"MsgType\":1,\"Client\":{{\"ClientGuid\":\"{id}\",\"Name\":\"{name}\",\"RadioInfo\":{{\"unitId\":{unit}}}}}}}";
        sv.OnServerMessage(SrsClient("a", "DCS-ATC", 0)); sv.OnServerMessage(SrsClient("b", "Tower", 100000));
        bool svAlone = !sv.HasPlayers;
        sv.OnServerMessage(SrsClient("c", "Tony", 16778752));
        if (!svAlone || !sv.HasPlayers) throw new Exception("SRS HasPlayers: DCS-ATC/ExternalAudio zählen nicht, ein Spieler schon");
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
            // K4: SRS server with COALITION_AUDIO_SECURITY forwards voice only within the coalition -> listen as blue and (second connection) red
            var cs = new SrsListener(5002, lf, _ => false, _ => { });
            string F(int conn, string k) => System.Text.RegularExpressions.Regex.Match(cs.Message(3, conn), $"\"{k}\":\"?([^\",]*)").Groups[1].Value;
            string c0 = F(0, "Coalition");
            cs.OnServerMessage("""{"MsgType":2,"Clients":[],"ServerSettings":{"COALITION_AUDIO_SECURITY":"true","SPECTATORS_AUDIO_DISABLED":"false"}}""");
            string c1 = F(0, "Coalition") + F(1, "Coalition");
            cs.OnServerMessage("""{"MsgType":4,"ServerSettings":{"COALITION_AUDIO_SECURITY":"False"}}""");
            string c2 = F(0, "Coalition");
            if (c0 != "0" || c1 != "12" || c2 != "0" || F(0, "ClientGuid").Length != 22 || F(0, "ClientGuid") == F(1, "ClientGuid"))
                throw new Exception($"K4 Coalition Security: {c0} {c1} {c2}");
            Console.WriteLine("OK   SRS Coalition Security (K4): ohne Koalition 0 (Zuschauer), mit Security Blau + zweite Verbindung Rot (eigene GUID), aus -> wieder 0");
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

}
