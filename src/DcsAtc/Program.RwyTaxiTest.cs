namespace DcsAtc;
static partial class Program
{
    /// Developer request 2026-11-27: airfield without taxiway (taxi route over the runway): no taxi clearance / no AI release while the runway is busy (taxiing, takeoff, landing),
    /// called back when free, and landings wait for flights taxiing on the runway. Airfield with taxiway: unchanged.
    static void RwyTaxiTest()
    {
        var (fs, ws, ss, sst, sl, ai0) = (Fields, Wx, SimSec, stateTime, SimLog, Ai);
        var cmds = new List<string>();
        var outq = new List<Tx>();
        var fails = new List<string>();
        void Chk(bool ok, string what) { if (ok) Console.WriteLine("OK   " + what); else { fails.Add(what); Console.WriteLine("FAIL " + what); } }
        try
        {
            Fields = new(); Wx = new(); SimSec = 5000; stateTime = Clock; SimLog = new(); AiHook = cmds.Add;
            var rf = Airfield.Kutaisi();   // runway taxi: chart "backtrack" (like Batumi 13)
            var tf = Airfield.Kutaisi();   // with taxiway
            var rw = FieldRunway(rf);
            rf.Backtrack.Add(rw);
            var e = rf.End(rw);
            double Hd = e.Hdg * Math.PI / 180;
            var ap = e.At(0, 400);   // apron
            var none = Array.Empty<Traffic>();
            Traffic Plane(string g, string flag, (double X, double Z) p, bool air, double spd, double alt = 0) =>
                new(g.GetHashCode(), "FA-18C_hornet", p.X, p.Z, rf.Elev + alt, Hd, spd, g, flag, air);
            Pilot Pl(Airfield f, string unit, string cs, Phase ph, (double X, double Z) pos, double ias, double agl = 0)
            {
                var tel = new Telemetry(f.Elev + agl, agl, ias, Hd, pos.X, pos.Z, 0, 0, 0);
                var p = new Pilot { Unit = unit, Callsign = cs, Type = "FA-18C_hornet", Tel = tel, Active = new Tower(f, cs) };
                p.Active.Load(new Tower.State(ph, rw, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, false, false, null, null, 0, false), tel, 0);
                return p;
            }
            string Ask(Airfield f, Pilot p, List<Pilot> all, IReadOnlyList<Traffic> tr, double now, string text = "request taxi", string st = "Ground")
            {
                Prep(p, p.Active!, all);
                return string.Join(" | ", p.Active!.OnTranscript(f.StationOf(st) + ", " + p.Callsign.Replace("-", "") + ", " + text, p.Tel, tr, now).Select(m => m.Text));
            }
            string Tick(Pilot p, List<Pilot> all, IReadOnlyList<Traffic> tr, double now) { Prep(p, p.Active!, all); return string.Join(" | ", p.Active!.Tick(p.Tel, tr, now).Select(m => m.Text)); }

            // (1) player asks for taxi while a flight taxis on the runway: hold, then the normal clearance by callback; (4) airfield with taxiway: clearance as before
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-300, 0), false, 6) };
            aiStage["DEP1"] = "taxi";
            var p1 = Pl(rf, "U1", "Enfield 1-1", Phase.Parked, ap, 0);
            var l1 = new List<Pilot> { p1 };
            var h1 = Ask(rf, p1, l1, Ai, 5);
            var w1 = Tick(p1, l1, Ai, 10);
            Chk(h1.Contains("hold position, traffic on runway, I'll call you") && !h1.Contains("taxi to holding point") && p1.Active!.Phase == Phase.Parked && w1 == "",
                $"runway taxi: AI on the runway -> hold position, no clearance (\"{h1}\" / \"{w1}\")");
            Ai = new();
            var c1 = Tick(p1, l1, Ai, 20);
            Chk(c1.Contains("taxi to holding point runway") && p1.Active.Phase == Phase.TaxiOut, $"runway taxi: runway free -> callback with the taxi clearance (\"{c1}\")");
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-300, 0), false, 6) };
            var t1 = Pl(tf, "U1", "Enfield 1-1", Phase.Parked, ap, 0);
            var k1 = Ask(tf, t1, new() { t1 }, Ai, 5);
            Chk(k1.Contains("taxi to holding point") && !k1.Contains("hold position") && t1.Active!.Phase == Phase.TaxiOut, $"taxiway field: unchanged, clearance with the AI on the runway (\"{k1}\")");

            // takeoff clearance given (AI waits at the holding point), landing within 4 NM, another player taxiing ahead: also hold
            Ai = new() { Plane("DEP2", "dep-go:Kutaisi", e.At(0, 150), false, 0) };
            aiStage["DEP2"] = "ready";
            var p2 = Pl(rf, "U1", "Enfield 1-1", Phase.Parked, ap, 0);
            var h2 = Ask(rf, p2, new() { p2 }, Ai, 5);
            Ai = new() { Plane("ARR2", "arr:Kutaisi", e.At(3 * NM, 0), true, 75, 300) };
            var p3 = Pl(rf, "U1", "Enfield 1-1", Phase.Parked, ap, 0);
            var h3 = Ask(rf, p3, new() { p3 }, Ai, 5);
            Ai = new();
            var p4 = Pl(rf, "U1", "Enfield 1-1", Phase.Parked, ap, 0);
            var oth = Pl(rf, "U2", "Colt 1-1", Phase.TaxiOut, e.At(300, 300), 6);
            var h4 = Ask(rf, p4, new() { p4, oth }, none, 5);
            Chk(h2.Contains("hold position, traffic") && h3.Contains("hold position, traffic landing") && h4.Contains("hold position, traffic taxiing to the runway"),
                $"runway taxi: takeoff clearance / landing 3 NM / other flight taxiing -> hold (\"{h2}\" / \"{h3}\" / \"{h4}\")");

            // one at a time: two held, runway free -> only the earlier one gets the clearance, the next one only after it has left
            Ai = new() { Plane("DEP1", "dep-go:Kutaisi", e.At(-300, 0), false, 6) };
            var q1 = Pl(rf, "U1", "Enfield 1-1", Phase.Parked, ap, 0);
            var q2 = Pl(rf, "U2", "Colt 1-1", Phase.Parked, e.At(0, 430), 0);
            var lq = new List<Pilot> { q2, q1 };
            var a1 = Ask(rf, q1, lq, Ai, 5); var a2 = Ask(rf, q2, lq, Ai, 6);
            Ai = new();
            var b2 = Tick(q2, lq, Ai, 20); var b1 = Tick(q1, lq, Ai, 20); var b2b = Tick(q2, lq, Ai, 21);
            q1.Tel = q1.Tel! with { AltMsl = rf.Elev + 60, Agl = 60, Ias = 80 };   // first one airborne
            var b2c = Tick(q2, lq, Ai, 30);
            Chk(a1.Contains("hold position") && a2.Contains("hold position") && b2 == "" && b1.Contains("taxi to holding point") && b2b == "" && b2c.Contains("taxi to holding point"),
                $"runway taxi: one at a time (\"{b2}\" / \"{b1}\" / \"{b2b}\" / \"{b2c}\")");

            // (2) AI departure: not released while a player taxis / an AI taxis / an AI lands; free -> START; airfield with taxiway: unchanged
            bool Start(Airfield f, Action set)
            {
                Fields = new() { f }; aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear(); cmds.Clear(); outq.Clear();
                Ai = new() { Plane("DEPX", "dep:Kutaisi", e.At(2000, 400), false, 0) };
                pls = new();
                set();
                ControlAi(pls, outq);
                return cmds.Contains("START;DEPX");
            }
            bool s0 = Start(rf, () => pls.Add(Pl(rf, "U1", "Enfield 1-1", Phase.Parked, ap, 0)));
            bool s1 = Start(rf, () => pls.Add(Pl(rf, "U1", "Enfield 1-1", Phase.TaxiOut, e.At(300, 300), 6)));
            bool s2 = Start(rf, () => { Ai.Add(Plane("ARR1", "arr:Kutaisi", e.At(3 * NM, 0), true, 75, 300)); aiStage["ARR1"] = "final"; });
            bool s3 = Start(rf, () => { Ai.Add(Plane("DEP9", "dep-go:Kutaisi", e.At(1000, 300), false, 6)); aiStage["DEP9"] = "taxi"; });
            bool s4 = Start(rf, () => pls.Add(Pl(rf, "U1", "Enfield 1-1", Phase.ClearedLand, e.At(3 * NM, 0), 70, 300)));
            bool s5 = Start(tf, () => pls.Add(Pl(tf, "U1", "Enfield 1-1", Phase.TaxiOut, e.At(300, 300), 6)));
            bool s6 = Start(tf, () => { Ai.Add(Plane("DEP9", "dep-go:Kutaisi", e.At(1000, 300), false, 6)); aiStage["DEP9"] = "taxi"; });
            Chk(s0 && !s1 && !s2 && !s3 && !s4, $"runway taxi: AI departure waits for player taxiing / AI on final / AI taxiing / player landing, otherwise START ({s0}/{s1}/{s2}/{s3}/{s4})");
            Chk(s5 && s6, $"taxiway field: AI departure unchanged ({s5}/{s6})");

            // (3) landing waits for flights taxiing on the runway: AI landing vs player taxiing, player landing vs AI taxiing
            string Arr(Airfield f, Action set)
            {
                Fields = new() { f }; aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear(); cmds.Clear(); outq.Clear();
                Ai = new() { Plane("ARR1", "arr:Kutaisi", e.At(5 * NM, 0), true, 75, 485) };
                aiStage["ARR1"] = "inbound";
                pls = new();
                set();
                ControlAi(pls, outq);
                return string.Join(" | ", outq.Where(t => !t.Pilot && t.Role == "Tower").Select(t => t.Text));
            }
            var r1 = Arr(rf, () => pls.Add(Pl(rf, "U1", "Enfield 1-1", Phase.TaxiOut, e.At(-300, 60), 6)));
            var r2 = Arr(rf, () => pls.Add(Pl(rf, "U1", "Enfield 1-1", Phase.TaxiOut, ap, 6)));
            var r3 = Arr(tf, () => pls.Add(Pl(tf, "U1", "Enfield 1-1", Phase.TaxiOut, e.At(-300, 60), 6)));
            Chk(r1.Contains("continue approach, traffic on runway") && !r1.Contains("cleared to land") && r2.Contains("cleared to land") && r3.Contains("cleared to land"),
                $"AI landing: waits for the player taxiing on the runway, otherwise cleared (\"{r1}\" / \"{r2}\" / \"{r3}\")");

            string Land(Airfield f, (double X, double Z) aiPos, out Pilot pp, out List<Pilot> lst)
            {
                Fields = new() { f }; aiStage.Clear(); cmds.Clear();
                Ai = new() { Plane("DEP1", "dep-go:Kutaisi", aiPos, false, 6) };
                aiStage["DEP1"] = "taxi";
                var fin = e.At(2 * NM, 0);
                pp = Pl(f, "U1", "Enfield 1-1", Phase.Pattern, fin, 70, 2 * NM * Math.Tan(3 * Math.PI / 180) + 15);
                pp.Tel = pp.Tel! with { Gear = 1 };
                lst = new() { pp };
                return Ask(f, pp, lst, Ai, 5, "final", "Tower");
            }
            var m1 = Land(rf, e.At(-300, 60), out var lp, out var ll);
            Ai = new();
            var m1b = Tick(lp, ll, Ai, 10);
            var m2 = Land(rf, e.At(0, 900), out _, out _);
            var m3 = Land(tf, e.At(-300, 60), out _, out _);
            Chk(m1.Contains("continue approach, traffic on runway") && !m1.Contains("cleared to land") && m1b.Contains("cleared to land") && m2.Contains("cleared to land") && m3.Contains("cleared to land"),
                $"player landing: waits for the AI taxiing on the runway, then clearance; far away / taxiway field: cleared (\"{m1}\" / \"{m1b}\" / \"{m2}\" / \"{m3}\")");
            // synthetic network: two parallel runways, the taxi route of the apron behind the second one uses it as taxiway (along it) -> NO runway taxi for the first; one runway without taxiway -> runway taxi
            {
                (double X, double Z) P(double x, double z) => (x, z);
                Airfield Syn(bool two, out Airfield f2)
                {
                    var f = new Airfield { Name = "Synth", X = 0, Z = 0, Elev = 0 };
                    f.Ends.Add(new RwyEnd("09L", -1000, 0, 0, 2000, 0, 0)); f.Ends.Add(new RwyEnd("27R", 1000, 0, 180, 2000, 0, 0));
                    if (two) { f.Ends.Add(new RwyEnd("09R", -1000, 300, 0, 2000, 0, 300)); f.Ends.Add(new RwyEnd("27L", 1000, 300, 180, 2000, 0, 300)); }
                    f.Spots.Add((0, 600, 1, 16));
                    var nd = new List<(double X, double Z)>(); var ed = new List<(int A, int B, (double X, double Z)[] Poly, double Len)>();
                    int N(double x, double z) { nd.Add(P(x, z)); return nd.Count - 1; }
                    void E(int a, int b) => ed.Add((a, b, new[] { nd[a], nd[b] }, Dist(nd[a].X, nd[a].Z, nd[b].X, nd[b].Z)));
                    int sp = N(0, 600), u = N(0, two ? 300 : 0), v = N(-1000, two ? 300 : 0);
                    E(sp, u); E(u, v);                       // stub to the runway centre line, then ALONG that runway (the other one when there are two)
                    if (two) { int w = N(-1000, 0); E(v, w); }   // across to the threshold of 09L
                    f.TaxiNet = new Rollnetz { Nodes = nd.ToArray(), Edges = ed.ToArray() };
                    f2 = f;
                    return f;
                }
                var two = Syn(true, out _); var one = Syn(false, out _);
                Chk(!two.RunwayTaxi("09L", 0, 600) && !two.RunwayTaxiField("09L"), "synthetic network: two parallel runways, route uses/crosses the other one -> not runway taxi");
                Chk(one.RunwayTaxi("09L", 0, 600) && one.RunwayTaxiField("09L"), "synthetic network: one runway without taxiway -> runway taxi");
            }

        }
        finally
        {
            (Fields, Wx, SimSec, stateTime, SimLog, AiHook, Ai) = (fs, ws, ss, sst, sl, null, ai0);
            aiStage.Clear(); aiSent.Clear(); lastAiStart.Clear();
        }
        if (fails.Count > 0) throw new Exception("Rollen ueber die Bahn: " + fails.Count + " Pruefungen fehlgeschlagen");
    }
    static List<Pilot> pls = new();
}
