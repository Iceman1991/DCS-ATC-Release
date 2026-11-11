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
    const string Zip = "DCS-ATC.zip", Setup = "DCS-ATC-Setup.exe";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "User-Agent", "DCS-ATC" } } };   // GitHub rejects requests without a user agent

    /// Sha256: lowercase hex from the release ("" = GitHub gave none, then only the size is checked).
    public record Release(string Version, string Url, long Size, string Sha256);

    public static string Own => System.Windows.Forms.Application.ProductVersion.Split('+')[0];   // "0.22.0-alpha+commit" -> "0.22.0-alpha"

    static Version? Num(string s) => Regex.Match(s, @"(\d+)\.(\d+)\.(\d+)") is { Success: true } m ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    /// "v0.22.1-alpha" is newer than "0.22.0-alpha": the three numbers decide, anything unreadable is not newer.
    internal static bool Newer(string tag, string own) => Num(tag) is { } a && Num(own) is { } b && a > b;

    /// Answer of releases/latest -> version (tag without "v"), zip, size, checksum; null = no zip from the release repo in it.
    internal static Release? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("tag_name", out var tag) || !r.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "", url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (name != Zip || !url.StartsWith(Assets, StringComparison.Ordinal)) continue;
            var sha = a.TryGetProperty("digest", out var d) && d.GetString() is { } ds && ds.StartsWith("sha256:") ? ds[7..].ToLowerInvariant() : "";
            return new((tag.GetString() ?? "").TrimStart('v'), url, a.GetProperty("size").GetInt64(), sha);
        }
        return null;
    }

    /// The latest release if it is newer than this build; null = up to date, offline, GitHub not reachable.
    public static Release? Check()
    {
        try { return Parse(Http.GetStringAsync(Api).GetAwaiter().GetResult()) is { } r && Newer(r.Version, Own) ? r : null; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    static string Dir => Path.Combine(Path.GetTempPath(), "DcsAtc-Update");
    /// Remove the setup of the last update (850 MB). At the next start of the app, right after the setup the file is still locked for some seconds.
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
        string zip = Path.Combine(dir, Zip), setup = Path.Combine(dir, Setup);
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
        if (r.Sha256 != "" && Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() != r.Sha256) throw new IOException("checksum does not match");
        using (var z = ZipFile.OpenRead(zip)) (z.GetEntry(Setup) ?? throw new IOException(Setup + " not in the zip")).ExtractToFile(setup, true);
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

    /// --updatetest: the real thing against GitHub without installing (latest release, whatever its version: download, size, checksum, unpack).
    public static int NetTest()
    {
        var r = Parse(Http.GetStringAsync(Api).GetAwaiter().GetResult()) ?? throw new Exception("no zip in the latest release");
        Console.WriteLine($"{r.Version} {r.Size} sha256 {r.Sha256}, newer than {Own}: {Newer(r.Version, Own)}");
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
        Console.WriteLine("OK   Update: neuere Version erkannt, Zip nur aus dem Release-Repo, Prüfsumme gelesen");
    }
}
