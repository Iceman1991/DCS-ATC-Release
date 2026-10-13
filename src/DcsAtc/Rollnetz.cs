namespace DcsAtc;

/// DCS taxi network of one airfield: Mods\terrains\<map>\AirfieldsTaxiways\*.rn4 (landscape4::lRoadNetwork), read at runtime from the user's own
/// DCS installation; nothing of it is part of DCS-ATC. Reverse-engineered, not an official format: nodes (x, z) and edges (polylines), no taxiway names.
/// Stands are the nodes 0..n-1 (airfields.txt P number = node id), then the taxi nodes. The runway is a single edge only at some airfields.
public sealed class Rollnetz
{
    public (double X, double Z)[] Nodes = Array.Empty<(double, double)>();
    public (int A, int B, (double X, double Z)[] Poly, double Len)[] Edges = Array.Empty<(int, int, (double, double)[], double)>();

    /// Taxi corridor: the polylines of all edges that lie on the way (or on a not much longer way) from start to goal.
    public sealed class Route
    {
        public readonly List<(double X, double Z)[]> Lines = new();
        public double Dist(double x, double z)
        {
            double d = double.MaxValue;
            foreach (var l in Lines) for (int i = 1; i < l.Length; i++) d = Math.Min(d, Tower.SegDist(x, z, l[i - 1], l[i]));
            return d;
        }
    }

    /// Corridor from start to goal (positions within `near` m of a node; stand and holding point need not be exact nodes). Edges for which skip(x, z) holds at both ends
    /// are left out (runway edge: the shortest way must not lead along the runway). Detour allowance 20 % + 150 m: the corridor shall include the way the chart names, not only the shortest.
    /// null: no node near start or goal, or no connection.
    public Route? RouteTo((double X, double Z) from, (double X, double Z) to, Func<double, double, bool> skip, double near = 100)
    {
        if (Spread(from, skip, near) is not { } ds || Spread(to, skip, near) is not { } dg) return null;
        double best = Enumerable.Range(0, Nodes.Length).Min(i => ds[i] + dg[i]);
        if (best > 1e9) return null;
        var r = new Route();
        foreach (var e in Edges.Where(e => !Skipped(e, skip)))
            if (Math.Min(ds[e.A] + e.Len + dg[e.B], ds[e.B] + e.Len + dg[e.A]) <= 1.2 * best + 150) r.Lines.Add(e.Poly);
        return r;
    }

    /// Distance of every node to p along the network (nodes within `near` of p start with their straight distance); null: no node near p.
    public double[]? Spread((double X, double Z) p, Func<double, double, bool> skip, double near = 100)
    {
        var d = Enumerable.Repeat(double.MaxValue, Nodes.Length).ToArray();
        bool any = false;
        for (int i = 0; i < Nodes.Length; i++) if (Tower.Dist(p.X, p.Z, Nodes[i].X, Nodes[i].Z) is var c && c <= near) (d[i], any) = (c, true);
        if (!any) return null;
        for (bool ch = true; ch;)   // ponytail: relaxation until nothing changes, 100-500 edges; Dijkstra with a queue only if networks get large
        {
            ch = false;
            foreach (var (a, b, _, len) in Edges.Where(e => !Skipped(e, skip)))
            {
                if (d[a] + len < d[b]) (d[b], ch) = (d[a] + len, true);
                if (d[b] + len < d[a]) (d[a], ch) = (d[b] + len, true);
            }
        }
        return d;
    }

    /// Shortest way along the network from `from` to `to` (dg = Spread(to)): ordered points from the best node near `from`, ending at `to`; null: no node near `from` or no connection.
    public List<(double X, double Z)>? PathTo((double X, double Z) from, (double X, double Z) to, double[] dg, Func<double, double, bool> skip, double near = 100)
    {
        // start on the nearest edge, not the nearest node: nodes can lie several hundred meters apart on long taxiways
        int cur = -1;
        double best = 1e9;
        List<(double X, double Z)> pts = new();
        foreach (var e in Edges.Where(e => !Skipped(e, skip)))
        {
            var p = e.Poly;
            int seg = 0; double t = 0, c = 1e9;
            for (int k = 0; k + 1 < p.Length; k++)
            {
                double dx = p[k + 1].X - p[k].X, dz = p[k + 1].Z - p[k].Z, l2 = dx * dx + dz * dz;
                double u = l2 < 1e-6 ? 0 : Math.Clamp(((from.X - p[k].X) * dx + (from.Z - p[k].Z) * dz) / l2, 0, 1);
                if (Tower.Dist(from.X, from.Z, p[k].X + u * dx, p[k].Z + u * dz) is var ck && ck < c) (seg, t, c) = (k, u, ck);
            }
            if (c > near) continue;
            var on = (X: p[seg].X + t * (p[seg + 1].X - p[seg].X), Z: p[seg].Z + t * (p[seg + 1].Z - p[seg].Z));
            double arc = Enumerable.Range(0, seg).Sum(k => Tower.Dist(p[k].X, p[k].Z, p[k + 1].X, p[k + 1].Z)) + Tower.Dist(p[seg].X, p[seg].Z, on.X, on.Z);
            double total = Enumerable.Range(0, p.Length - 1).Sum(k => Tower.Dist(p[k].X, p[k].Z, p[k + 1].X, p[k + 1].Z));
            bool aFirst = Tower.Dist(p[0].X, p[0].Z, Nodes[e.A].X, Nodes[e.A].Z) <= Tower.Dist(p[0].X, p[0].Z, Nodes[e.B].X, Nodes[e.B].Z);
            int n0 = aFirst ? e.A : e.B, n1 = aFirst ? e.B : e.A;
            if (c + arc + dg[n0] < best) { (cur, best) = (n0, c + arc + dg[n0]); pts = p.Take(seg + 1).Reverse().Prepend(on).ToList(); }
            if (c + total - arc + dg[n1] < best) { (cur, best) = (n1, c + total - arc + dg[n1]); pts = p.Skip(seg + 1).Prepend(on).ToList(); }
        }
        if (cur < 0) return null;
        for (int k = 0; k < Nodes.Length; k++)
        {
            int at = cur;
            var e = Edges.FirstOrDefault(e => (e.A == at || e.B == at) && !Skipped(e, skip) && Math.Abs(dg[e.A == at ? e.B : e.A] + e.Len - dg[at]) < 0.01);
            if (e.Poly == null) break;   // goal node reached (starts with its straight distance)
            bool fwd = Tower.Dist(e.Poly[0].X, e.Poly[0].Z, Nodes[at].X, Nodes[at].Z) <= Tower.Dist(e.Poly[^1].X, e.Poly[^1].Z, Nodes[at].X, Nodes[at].Z);
            pts.AddRange((fwd ? e.Poly : e.Poly.Reverse()).Skip(1));
            cur = e.A == at ? e.B : e.A;
        }
        pts.Add(to);
        return pts;
    }

    bool Skipped((int A, int B, (double X, double Z)[] Poly, double Len) e, Func<double, double, bool> skip) =>
        skip(Nodes[e.A].X, Nodes[e.A].Z) && skip(Nodes[e.B].X, Nodes[e.B].Z);

    /// Tolerance chart holding point to the nearest taxi edge: taxiway half width up to 25 m (code E) plus 15 m for reading the position from the map. Measured on 21 airfields:
    /// 17 of 42 holds lie within 30 m, all others beyond 63 m, so every value between 30 and 60 m gives the same result.
    public const double HoldTol = 40;

    /// Distance from a point to the nearest edge (edges inside the skip area do not count).
    public double EdgeDist(double x, double z, Func<double, double, bool> skip) =>
        Edges.Where(e => !Skipped(e, skip)).SelectMany(e => e.Poly.Zip(e.Poly.Skip(1), (p, q) => Tower.SegDist(x, z, p, q))).DefaultIfEmpty(double.MaxValue).Min();

    /// Assigns every charted airfield its network: found by the stands (P lines) and runway ends against the nodes, not by file name (the names differ: Nalchick, TbilisiMilitary).
    /// Without DCS installation or file: silently no network.
    public static void LoadAll(IEnumerable<Airfield> fields, string dcsDir)
    {
        var charted = fields.Where(f => f.Charted).ToList();
        var dir = Path.Combine(dcsDir, "Mods", "terrains", Airfield.Theatre, "AirfieldsTaxiways");
        if (charted.Count == 0 || !Directory.Exists(dir)) return;
        var nets = new List<Rollnetz>();
        try { foreach (var file in Directory.EnumerateFiles(dir, "*.rn4")) if (Read(file) is { } n) nets.Add(n); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var f in charted)
            f.Net = nets.Select(n => (n, s: n.Score(f))).Where(x => x.s >= 2).OrderByDescending(x => x.s).Select(x => x.n).FirstOrDefault();
    }

    /// Stands (within 1 m of a node) plus runway ends (within 5 m of a node) of the airfield found in this network.
    internal int Score(Airfield f) =>
        f.Spots.Count(s => Nodes.Any(p => Tower.Dist(p.X, p.Z, s.X, s.Z) < 1)) + f.Ends.Count(e => Nodes.Any(p => Tower.Dist(p.X, p.Z, e.ThrX, e.ThrZ) < 5));

    /// null: file unreadable or no node/edge found (other DCS version).
    public static Rollnetz? Read(string path)
    {
        byte[] b;
        try { b = File.ReadAllBytes(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        double D(int o) => BitConverter.ToDouble(b, o);
        uint U(int o) => BitConverter.ToUInt32(b, o);
        // Nodes: 4x4 matrix per node, row 3 = (x, y, z, 1.0), row 2 = (sin, 0, cos, 0), row 0 = (cos, 0, -sin, 0), row 1 = (0, 1, 0, 0); 100 bytes of node data follow
        var nodes = new List<(double X, double Z)>();
        int firstNode = b.Length;
        for (int p = 64; p + 32 <= b.Length; p++)
        {
            if (D(p + 24) != 1.0 || D(p - 8) != 0.0 || D(p - 24) != 0.0 || Math.Abs(D(p - 56) - 1.0) > 1e-9) continue;
            double s = D(p - 32), c = D(p - 16), x = D(p), y = D(p + 8), z = D(p + 16);
            if (Math.Abs(s * s + c * c - 1) > 1e-3 || !(Math.Abs(x) > 1e3 && Math.Abs(z) > 1e3 && Math.Abs(y) < 1e5)) continue;
            if (Math.Abs(D(p - 96) - c) > 1e-6 || Math.Abs(D(p - 80) + s) > 1e-6) continue;
            if (nodes.Count > 0 && (Math.Abs(x - nodes[0].X) > 50000 || Math.Abs(z - nodes[0].Z) > 50000)) continue;
            if (nodes.Count == 0) firstNode = p;
            nodes.Add((x, z));
            p += 100;
        }
        if (nodes.Count < 2) return null;
        // Edges lie before the first node. Header directly before the point array: u16 class, u32 a, u32 b, u32 0, u32 n, then n * (x, y, z) as double
        bool Near(int o) => o + 24 <= b.Length && Math.Abs(D(o) - nodes[0].X) < 20000 && Math.Abs(D(o + 16) - nodes[0].Z) < 20000 && Math.Abs(D(o + 8)) < 1e5;
        var edges = new List<(int, int, (double, double)[], double)>();
        for (int p = 24; p < firstNode - 24; p++)
        {
            uint n = U(p - 4);
            if (n < 2 || n > 200 || p + 24 * (int)n > firstNode || !Near(p)) continue;
            int a = (int)U(p - 16), bb = (int)U(p - 12);
            if (a >= nodes.Count || bb >= nodes.Count || !Enumerable.Range(1, (int)n - 1).All(k => Near(p + 24 * k))) continue;
            var poly = Enumerable.Range(0, (int)n).Select(k => (D(p + 24 * k), D(p + 24 * k + 16))).ToArray();
            edges.Add((a, bb, poly, Enumerable.Range(1, poly.Length - 1).Sum(k => Tower.Dist(poly[k - 1].Item1, poly[k - 1].Item2, poly[k].Item1, poly[k].Item2))));
            p += 24 * (int)n - 1;
        }
        return edges.Count == 0 ? null : new Rollnetz { Nodes = nodes.ToArray(), Edges = edges.ToArray() };
    }
}
