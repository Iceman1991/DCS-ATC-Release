using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DcsAtc;

/// Update from the GitHub releases: check at start (Config.UpdateCheck), download and silent setup only on the button in the settings window.
static class Update
{
    const string Api = "https://api.github.com/repos/Iceman1991/DCS-ATC-Release/releases/latest";
    const string Assets = "https://github.com/Iceman1991/DCS-ATC-Release/releases/download/";   // the zip is only taken from the release repo
    const string Zip = "DCS-ATC.zip", SmallZip = "DCS-ATC-Update.zip", Manifest = "manifest.json";   // each zip holds its setup: DCS-ATC.zip -> DCS-ATC-Setup.exe
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "User-Agent", "DCS-ATC" } } };   // GitHub rejects requests without a user agent

    /// Sha256: lowercase hex from the release ("" = GitHub gave none, then only the size is checked).
    public record Release(string Version, string Url, long Size, string Sha256);

    public static string Own => System.Windows.Forms.Application.ProductVersion.Split('+')[0];   // "0.22.0-alpha+commit" -> "0.22.0-alpha"

    static Version? Num(string s) => Regex.Match(s, @"(\d+)\.(\d+)\.(\d+)") is { Success: true } m ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    /// "v0.22.1-alpha" is newer than "0.22.0-alpha": the three numbers decide, anything unreadable is not newer.
    internal static bool Newer(string tag, string own) => Num(tag) is { } a && Num(own) is { } b && a > b;

    /// Answer of releases/latest -> version (tag without "v"), asset (default: the full zip), size, checksum; null = no such asset from the release repo in it.
    internal static Release? Parse(string json, string asset = Zip)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("tag_name", out var tag) || !r.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "", url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (name != asset || !url.StartsWith(Assets, StringComparison.Ordinal)) continue;
            var sha = a.TryGetProperty("digest", out var d) && d.GetString() is { } ds && ds.StartsWith("sha256:") ? ds[7..].ToLowerInvariant() : "";
            return new((tag.GetString() ?? "").TrimStart('v'), url, a.GetProperty("size").GetInt64(), sha);
        }
        return null;
    }

    /// manifest.json of a release -> the big files the small setup leaves out (path below the app folder, size, SHA-256); null = other version, empty or unreadable.
    internal static (string Path, long Size, string Sha256)[]? Big(string json, string version)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'));
            var r = doc.RootElement;
            if (r.GetProperty("version").GetString() != version) return null;
            var big = r.GetProperty("files").EnumerateArray().Select(f => (f.GetProperty("path").GetString() ?? "", f.GetProperty("size").GetInt64(), (f.GetProperty("sha256").GetString() ?? "").ToLowerInvariant())).ToArray();
            return big.Length > 0 ? big : null;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    /// All big files are in the app folder exactly as the release has them: the small setup is enough.
    internal static bool Has(string root, IEnumerable<(string Path, long Size, string Sha256)> big) => big.All(f =>
    {
        try
        {
            var p = Path.Combine(root, f.Path);
            if (!File.Exists(p) || new FileInfo(p).Length != f.Size) return false;
            using var s = File.OpenRead(p);
            return Hex(SHA256.HashData(s)) == f.Sha256;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    });

    /// What to download: the small zip (app without models and tools) if the release has it with a manifest and every big file is already here, else the full zip.
    /// load fetches the manifest. null = release without the full zip. No manifest or small zip, or the manifest cannot be loaded: full zip.
    internal static Release? Pick(string json, string root, Func<string, byte[]> load)
    {
        var full = Parse(json);
        if (full == null || Parse(json, SmallZip) is not { } small || Parse(json, Manifest) is not { } m) return full;
        try
        {
            var b = load(m.Url);
            bool ok = b.Length == m.Size && (m.Sha256 == "" || Hex(SHA256.HashData(b)) == m.Sha256);
            return ok && Big(System.Text.Encoding.UTF8.GetString(b), full.Version) is { } big && Has(root, big) ? small : full;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return full; }
    }

    // ponytail: hashes the ~780 MB of models and tools on every check that finds a release with a manifest (some seconds, background thread); cache the result if that hurts
    static Release? Latest() => Pick(Http.GetStringAsync(Api).GetAwaiter().GetResult(), Path.GetDirectoryName(Environment.ProcessPath) ?? "", u => Http.GetByteArrayAsync(u).GetAwaiter().GetResult());

    /// The latest release if it is newer than this build (small or full zip, see Pick); null = up to date, offline, GitHub not reachable.
    public static Release? Check()
    {
        try { return Latest() is { } r && Newer(r.Version, Own) ? r : null; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    static string Dir => Path.Combine(Path.GetTempPath(), "DcsAtc-Update");
    /// Remove the setup of the last update (up to 850 MB). At the next start of the app, right after the setup the file is still locked for some seconds.
    public static void Clean()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// Load the zip into %TEMP%\DcsAtc-Update (progress 0–1), check size and SHA-256, unpack the setup; returns its path.
    public static string Download(Release r, Action<double> progress)
    {
        var dir = Dir;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string name = Path.GetFileName(new Uri(r.Url).LocalPath), exe = Path.GetFileNameWithoutExtension(name) + "-Setup.exe";
        string zip = Path.Combine(dir, name), setup = Path.Combine(dir, exe);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long got = 0;
        using (var resp = Http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())   // timeout counts up to the headers only, the body may take minutes
        {
            resp.EnsureSuccessStatusCode();
            using var src = resp.Content.ReadAsStream();
            using var dst = File.Create(zip);
            var buf = new byte[1 << 20];
            for (int n; (n = src.Read(buf, 0, buf.Length)) > 0;)
            {
                dst.Write(buf, 0, n); sha.AppendData(buf, 0, n);
                got += n;
                progress(r.Size > 0 ? Math.Min(1, (double)got / r.Size) : 0);
            }
        }
        if (got != r.Size) throw new IOException($"size {got} instead of {r.Size}");
        if (r.Sha256 != "" && Hex(sha.GetHashAndReset()) != r.Sha256) throw new IOException("checksum does not match");
        using (var z = ZipFile.OpenRead(zip)) (z.GetEntry(exe) ?? throw new IOException(exe + " not in the zip")).ExtractToFile(setup, true);
        File.Delete(zip);
        return setup;
    }

    /// Close the running app, run the setup (progress window, no questions), then the settings window again. The caller exits right after (its exe is replaced).
    public static void Install(string setup)
    {
        foreach (var p in Process.GetProcessesByName("DcsAtc").Where(p => p.Id != Environment.ProcessId))
            try { p.Kill(); p.WaitForExit(5000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"start \"\" /wait \"{setup}\" /SILENT /SUPPRESSMSGBOXES /NORESTART & start \"\" \"{Environment.ProcessPath}\" --settings\"") { CreateNoWindow = true, UseShellExecute = false });
    }

    /// --updatetest: the real thing against GitHub without installing (latest release, whatever its version: small or full zip, download, size, checksum, unpack).
    public static int NetTest()
    {
        var r = Latest() ?? throw new Exception("no zip in the latest release");
        Console.WriteLine($"{r.Version} {Path.GetFileName(r.Url)} {r.Size} sha256 {r.Sha256}, newer than {Own}: {Newer(r.Version, Own)}");
        int last = -1;
        var setup = Download(r, p => { if ((int)(p * 10) is var d && d != last) Console.WriteLine($"{(last = d) * 10} %"); });
        Console.WriteLine($"OK   {setup} {new FileInfo(setup).Length}");
        return 0;
    }

    public static void Test()
    {
        const string json = """
            {"tag_name":"v0.22.1-alpha","assets":[
              {"name":"DCS-ATC.zip","size":5,"browser_download_url":"https://example.com/DCS-ATC.zip"},
              {"name":"DCS-ATC.zip","size":853048292,"digest":"sha256:00AB","browser_download_url":"https://github.com/Iceman1991/DCS-ATC-Release/releases/download/v0.22.1-alpha/DCS-ATC.zip"}]}
            """;
        var r = Parse(json);
        bool ok = r == new Release("0.22.1-alpha", "https://github.com/Iceman1991/DCS-ATC-Release/releases/download/v0.22.1-alpha/DCS-ATC.zip", 853048292, "00ab")
                  && Parse("""{"tag_name":"v9.0.0","assets":[{"name":"DCS-ATC.zip","size":5,"browser_download_url":"https://example.com/DCS-ATC.zip"}]}""") == null
                  && Newer("v0.22.1-alpha", "0.22.0-alpha") && Newer("0.23.0", "0.22.9-alpha") && Newer("v1.0.0", "0.99.99")
                  && !Newer("v0.22.0-alpha", "0.22.0-alpha") && !Newer("v0.21.3-alpha", "0.22.0-alpha") && !Newer("latest", "0.22.0-alpha") && Num(Own) != null;
        if (!ok) throw new Exception($"Update: {r} eigene Version {Own}");

        // small or full: a temp folder plays the app folder, the manifest lists one big file
        const string rel = "https://github.com/Iceman1991/DCS-ATC-Release/releases/download/v0.23.0-alpha/";
        var root = Path.Combine(Path.GetTempPath(), "DcsAtc-UpdateTest");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        try
        {
            File.WriteAllText(Path.Combine(root, "models", "a.bin"), "abc");
            const string abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";   // SHA-256 of "abc"
            byte[] Man(string version, string sha) => System.Text.Encoding.UTF8.GetBytes($$"""{"version":"{{version}}","files":[{"path":"models/a.bin","size":3,"sha256":"{{sha}}"}]}""");
            string Rel(byte[] man, bool small = true) => $$"""{"tag_name":"v0.23.0-alpha","assets":[{"name":"DCS-ATC.zip","size":800,"browser_download_url":"{{rel}}DCS-ATC.zip"},{"name":"manifest.json","size":{{man.Length}},"digest":"sha256:{{Hex(SHA256.HashData(man))}}","browser_download_url":"{{rel}}manifest.json"}"""
                + (small ? $$""",{"name":"DCS-ATC-Update.zip","size":70,"browser_download_url":"{{rel}}DCS-ATC-Update.zip"}""" : "") + "]}";
            byte[] good = Man("0.23.0-alpha", abc.ToUpperInvariant());
            long Size(byte[] man, bool small = true, byte[]? got = null) => Pick(Rel(man, small), root, _ => got ?? man)!.Size;
            bool pick = Size(good) == 70                                               // all there: small
                        && Size(good, small: false) == 800                             // release without the small zip
                        && Size(Man("0.23.0-alpha", new string('0', 64))) == 800       // file differs
                        && Size(Man("0.22.0-alpha", abc)) == 800                       // manifest of another version
                        && Size(good, got: Man("0.23.0-alpha", abc)) == 800            // not the manifest of the release (checksum)
                        && Pick(Rel(good), root, _ => throw new HttpRequestException()) is { Size: 800 }
                        && Pick(Rel(good), Path.Combine(root, "x"), _ => good) is { Size: 800 }   // file missing
                        && Big("﻿{\"version\":\"1\",\"files\":[]}", "1") == null && Big("{}", "1") == null;
            if (!pick) throw new Exception("Update: Auswahl kleines/volles Zip");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("OK   Update: neuere Version erkannt, Zip nur aus dem Release-Repo, Prüfsumme gelesen, kleines Zip nur mit Manifest und vorhandenen Modellen");
    }
}
