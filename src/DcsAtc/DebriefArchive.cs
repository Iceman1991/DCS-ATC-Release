using System.Text.RegularExpressions;

namespace DcsAtc;

/// A completed report is independent of the pilot and of the logbook entry made at flight end.
sealed class DebriefArchive(string root, string recovery)
{
    public readonly string Root = root;
    readonly Dictionary<string, string> volatileReports = new();
    readonly string directory = Path.Combine(root, "debrief");
    static bool StorageError(Exception e) => e is IOException or UnauthorizedAccessException;

    // Write beside the destination first. The rename publishes only a complete file and cannot replace an older report.
    static void Publish(string dir, string stem, string extension, string text, bool recovering = false)
    {
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
        var owned = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                using var writer = new StreamWriter(stream);
                writer.Write(text);
            }
            for (int i = 1; ; i++)
            {
                var path = Path.Combine(dir, stem + (i == 1 ? "" : $"-{i}") + extension);
                try { File.Move(temp, path); owned = false; return; }
                catch (IOException e) when ((e.HResult & 0xffff) is 80 or 183)
                {
                    // Only pending names have a per-flight ID. A previous retry may already have published this report.
                    if (recovering && File.ReadAllText(path) == text) return;
                }
            }
        }
        finally { if (owned) try { File.Delete(temp); } catch (Exception e) when (StorageError(e)) { } }
    }

    public string? Save(DateTime end, string callsign, string text)
    {
        var name = Regex.Replace(callsign, "[^A-Za-z0-9-]", "");
        var stem = $"{end:yyyy-MM-dd_HHmm}_" + name[..Math.Min(name.Length, 180)];
        try { Publish(directory, stem, ".txt", text); return null; }
        catch (Exception e) when (StorageError(e))
        {
            var id = stem + "_" + Guid.NewGuid().ToString("N");
            try
            {
                Publish(recovery, id, ".pending", text);
                return L($"Debriefing zwischengespeichert. Export wird wiederholt: {e.Message}", $"Debriefing queued. Export will retry: {e.Message}");
            }
            catch (Exception backup) when (StorageError(backup))
            {
                volatileReports[id] = text;
                return L($"Debriefing nur im Arbeitsspeicher. Speicherzugriff vor dem Beenden wiederherstellen: {backup.Message}",
                         $"Debriefing held in memory. Restore write access before closing: {backup.Message}");
            }
        }
    }

    public int Retry()
    {
        int count = 0;
        foreach (var (id, text) in volatileReports.ToArray())
        {
            try { Publish(directory, id, ".txt", text, recovering: true); count++; }
            catch (Exception e) when (StorageError(e))
            {
                try { Publish(recovery, id, ".pending", text); }
                catch (Exception backup) when (StorageError(backup)) { continue; }
            }
            volatileReports.Remove(id);
        }
        if (!Directory.Exists(recovery)) return count;
        string[] files;
        try { files = Directory.GetFiles(recovery, "*.pending"); }
        catch (Exception e) when (StorageError(e)) { return count; }
        foreach (var file in files)
        {
            try
            {
                Publish(directory, Path.GetFileNameWithoutExtension(file), ".txt", File.ReadAllText(file), recovering: true);
                File.Delete(file);
                count++;
            }
            catch (Exception e) when (StorageError(e)) { }   // Keep the pending file for the next run.
        }
        return count;
    }
}
