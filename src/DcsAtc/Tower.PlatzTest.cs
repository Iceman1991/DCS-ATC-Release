namespace DcsAtc;

public partial class Tower
{
    /// PlatzTest (PLATZVERFAHREN-PLAN.md Etappe 3/4, part of --selftest, alone: --platztest): the chart data decides side, altitude, CRPs, initial point, exit text and taxi text
    /// of every charted airfield; the same code for all of them, expected values come from the data (and, for Batumi/Kobuleti, from fixed words).
    public static void PlatzTest(IReadOnlyList<Airfield> all)
    {
        var err = new List<string>();
        void Chk(bool ok, string what) { if (!ok) err.Add(what); }
        var none = Array.Empty<Traffic>();
        string J(IEnumerable<Msg> ms) => string.Join(" / ", ms.Select(m => m.Text));
        int n = 0;
        foreach (var f in all.Where(a => a.Charted))
            foreach (var e in f.Ends.Where(e => f.Entry.ContainsKey(e.Name)))
            {
                var rw = e.Name;
                var wind = (X: -e.Dx * 5, Z: -e.Dz * 5);   // 10 kt headwind on rw
                Telemetry At(double x, double z, double agl, double hdg) => new(f.Elev + agl, agl, 150 * Kt, hdg * Math.PI / 180, x, z, 0, 0, 760);
                double patAgl = f.PatternFt * Ft - f.Elev;
                Tower New(Telemetry at, Phase ph) { var w = new Tower(f, "Enfield 1-1") { FieldWind = wind }; w.Tick(at, none, 0); (w.Phase, w.Runway) = (ph, rw); return w; }
                var ip = e.At(f.InitialNm[rw] * NM, 0);   // initial point of the chart: initial_nm before the threshold

                // K18/K20/K23: every entry CRP of the runway (multi-word names spoken with a space) leads to initial/overhead at the pattern altitude of the chart
                foreach (var (crp, via, wp) in f.Entry[rw])
                {
                    var c = f.Crp[crp];
                    var at = At(c.X, c.Z, patAgl, Bearing(c.X, c.Z, f.X, f.Z));
                    var w = New(at, Phase.Inbound);
                    (w.entry, w.nav, w.navName) = (crp, c, $"C R P {crp}");
                    var r = w.OnTranscript($"{f.StationOf("Approach")}, Enfield 11, C R P {crp.Replace('-', ' ')}", at, none, 2);
                    bool ovh = via == "overhead";
                    var goal = ovh ? (e.CX, e.CZ) : wp ?? ip;   // with knee point (wp) the first point is the knee, then the initial
                    Chk(r.Count == 1 && r[0].Text.Contains(ovh ? "proceed overhead" : $"proceed to initial runway {RwSay(rw)}") && r[0].Text.Contains(Alt(f.PatternFt)) &&
                        w.nav is { } nv && Dist(nv.X, nv.Z, goal.Item1, goal.Item2) < 2, $"{f.Name} {rw} CRP {crp}: {J(r)}");
                    n++;
                }
                // K20: a CRP outside the entry list of the runway (entry-only for the opposite direction, exit-only) is not accepted
                foreach (var crp in f.Crp.Keys.Where(k => !f.Entry[rw].Any(x => x.Crp == k)))
                {
                    var c = f.Crp[crp];
                    var at = At(c.X, c.Z, patAgl, Bearing(c.X, c.Z, f.X, f.Z));
                    var w = New(at, Phase.Inbound);
                    var r = w.OnTranscript($"{f.StationOf("Approach")}, Enfield 11, C R P {crp.Replace('-', ' ')}", at, none, 2);
                    Chk(r.Count == 1 && r[0].Text.Contains($"is not available for runway {RwSay(rw)}") && f.Entry[rw].All(x => !r[0].Text.Contains($"Enter via C R P {crp},")), $"{f.Name} {rw} CRP {crp} nicht im Einflug: {J(r)}");
                    n++;
                }
                // K17/K18: pattern side from the chart on the wrong side, pattern altitude from the chart on the right side
                var wrong = new[] { 1.2 * NM, -1.2 * NM }.Select(l => e.At(-0.5 * NM, l)).First(p => new Tower(f, "x").PatternOffset(p.X, p.Z, rw) < -900);
                var right = new[] { 1.2 * NM, -1.2 * NM }.Select(l => e.At(-0.5 * NM, l)).First(p => new Tower(f, "x").PatternOffset(p.X, p.Z, rw) > 700);
                var ws = New(At(wrong.X, wrong.Z, patAgl, e.Hdg + 180), Phase.Pattern);
                var rs = ws.Tick(At(wrong.X, wrong.Z, patAgl, e.Hdg + 180), none, 100);
                var side = f.SideOf.TryGetValue(rw, out var sd) ? $"pattern is {sd} of the runway, {f.Hand(rw)} pattern runway {RwSay(rw)}." : $"wrong side, {f.Hand(rw)} pattern runway {RwSay(rw)}.";
                Chk(rs.Count == 1 && rs[0].Text.EndsWith(side), $"{f.Name} {rw} Seite: {J(rs)}");
                var wa = New(At(right.X, right.Z, patAgl, e.Hdg + 180), Phase.Pattern);
                var ra = wa.Tick(At(right.X, right.Z, (f.PatternFt + 800) * Ft - f.Elev, e.Hdg + 180), none, 100);
                Chk(ra.Any(m => m.Text.EndsWith($"check altitude, pattern altitude {Alt(f.PatternFt)}.")), $"{f.Name} {rw} Platzrundenhöhe: {J(ra)}");
                // K19/K24: departure text with the standard exit and ARR/DEP maximum; note of the exit in the text
                foreach (var x in f.Exit[rw])
                {
                    var wd = New(At(f.X, f.Z, 0, e.Hdg), Phase.ClearedTakeoff);
                    wd.exitDir = x;
                    var td = wd.TakeoffClearance(At(e.ThrX, e.ThrZ, 0, e.Hdg), none);
                    Chk(td.Contains($"exit via C R P {x}") && td.Contains($"not above {Alt(f.MaxFt)} until leaving the control zone") &&
                        (!f.ExitNote.TryGetValue((rw, x), out var note) || td.Contains(SayChart(note))), $"{f.Name} {rw} Ausflug {x}: {td}");
                    n++;
                }
                n += 4;
            }

        // Batumi / Kobuleti with fixed words (chart: side, hand, pattern altitude, CRPs with several words, exit note)
        if (all.FirstOrDefault(a => a.Name == "Batumi") is { } bat && all.FirstOrDefault(a => a.Name == "Kobuleti") is { } kob)
        {
            Tower W(Airfield f, string rw, Phase ph) { var e = f.End(rw); var w = new Tower(f, "Enfield 1-1") { FieldWind = (-e.Dx * 5, -e.Dz * 5) }; w.Tick(new(f.Elev + 500, 500, 150 * Kt, 0, f.X, f.Z, 0, 0, 760), none, 0); (w.Phase, w.Runway) = (ph, rw); return w; }
            Telemetry At(Airfield f, double x, double z, double agl, double hdg) => new(f.Elev + agl, agl, 150 * Kt, hdg * Math.PI / 180, x, z, 0, 0, 760);
            foreach (var (f, rw, say, side, hand, ft) in new[] { (bat, "13", "one three", "southwest", "right", 1600), (bat, "31", "three one", "southwest", "left", 1600), (kob, "07", "zero seven", "south", "right", 1500), (kob, "25", "two five", "south", "left", 1500) })
            {
                var e = f.End(rw);
                var p = new[] { 1.2 * NM, -1.2 * NM }.Select(l => e.At(-0.5 * NM, l)).First(q => new Tower(f, "x").PatternOffset(q.X, q.Z, rw) < -900);
                var at = At(f, p.X, p.Z, ft * Ft - f.Elev, e.Hdg + 180);
                var r = W(f, rw, Phase.Pattern).Tick(at, none, 100);
                Chk(r.Count == 1 && r[0].Text.EndsWith($"pattern is {side} of the runway, {hand} hand pattern runway {say}."), $"{f.Name} {rw} Seite: {J(r)}");
                var q2 = new[] { 1.2 * NM, -1.2 * NM }.Select(l => e.At(-0.5 * NM, l)).First(q => new Tower(f, "x").PatternOffset(q.X, q.Z, rw) > 700);
                var wa = W(f, rw, Phase.Pattern);
                var ra = wa.Tick(At(f, q2.X, q2.Z, (ft + 800) * Ft - f.Elev, e.Hdg + 180), none, 100);
                Chk(ra.Any(m => m.Text.EndsWith($"check altitude, pattern altitude {ft} feet.")), $"{f.Name} {rw} Höhe: {J(ra)}");
                n += 2;
            }
            var wk = W(kob, "07", Phase.Inbound);
            var ck = kob.Crp["black sea"];
            var ak = At(kob, ck.X, ck.Z, 1500 * Ft - kob.Elev, 100);
            (wk.entry, wk.nav, wk.navName) = ("black sea", ck, "C R P black sea");
            var rk = wk.OnTranscript("Kobuleti Approach, Enfield 11, C R P black sea", ak, none, 2);
            var wn = W(kob, "07", Phase.Inbound);
            var rn = wn.OnTranscript("Kobuleti Approach, Enfield 11, C R P north east", ak, none, 2);
            var wb = W(bat, "13", Phase.Away);
            wb.exitDir = "north";
            var tb = wb.TakeoffClearance(At(bat, bat.X, bat.Z, 0, 130), none);
            Chk(rk.Count == 1 && rk[0].Text.Contains("proceed to initial runway zero seven, 1500 feet") && wk.entry == "black sea", "Kobuleti black sea: " + J(rk));
            Chk(rn.Count == 1 && rn[0].Text.Contains("C R P north-east is not available for runway zero seven. Enter via C R P ") && !rn[0].Text.Contains("Enter via C R P north-east"), "Kobuleti north east (nur Ausflug): " + J(rn));
            Chk(tb.Contains("after departure exit via C R P north, then three five four to the C R P, not above 3000 feet until leaving the control zone"), "Batumi 13 Ausflug north: " + tb);
            n += 3;
        }

        // K20: exit_only CRP is never offered or accepted as entry (Kutaisi with east as exit-only)
        {
            var k = Airfield.Kutaisi();
            k.ExitOnly.Add("east");
            k.Entry["25"] = k.Entry["25"].Where(x => x.Crp != "east").ToList();
            var c = k.Crp["east"];
            var at = new Telemetry(k.Elev + 1000, 1000, 150 * Kt, 270 * Math.PI / 180, c.X, c.Z, 0, 0, 760);
            var w = new Tower(k, "Enfield 1-1");
            w.Tick(at, none, 0);
            (w.Phase, w.Runway) = (Phase.Inbound, "25");
            var r = w.OnTranscript("Kutaisi Approach, Enfield 11, C R P east", at, none, 2);
            Chk(r.Count == 1 && r[0].Text.Contains("C R P east is not available for runway two five") && !r[0].Text.Contains("Enter via C R P east"), "exit_only: " + J(r));
            Chk(w.AssignEntry(at, "25", "east") != "east" && w.AssignEntry(at, "25", null) != "east", "exit_only AssignEntry");
            n += 2;
        }

        // K21: knee point (wp) first, then initial
        {
            var k = Airfield.Kutaisi();
            var e = k.End("25");
            var ip = e.At(k.InitialNm["25"] * NM, 0);
            var crp = k.Crp["south"];
            var wp = (X: (ip.X + crp.X) / 2, Z: (ip.Z + crp.Z) / 2);
            k.Entry["25"] = k.Entry["25"].Select(x => x.Crp == "south" ? (x.Crp, x.Via, (ValueTuple<double, double>?)wp) : x).ToList();
            Telemetry At(double x, double z, double hdg = 0) => new(k.Elev + k.PatternFt * Ft, k.PatternFt * Ft, 150 * Kt, hdg * Math.PI / 180, x, z, 0, 0, 760);
            var w = new Tower(k, "Enfield 1-1");
            w.Tick(At(crp.X, crp.Z), none, 0);
            (w.Phase, w.Runway, w.entry, w.nav, w.navName) = (Phase.Inbound, "25", "south", crp, "C R P south");
            var r1 = w.OnTranscript("Kutaisi Approach, Enfield 11, C R P south", At(crp.X, crp.Z), none, 2);
            bool first = w.navName == "the turning point" && w.nav is { } n1 && Dist(n1.X, n1.Z, wp.X, wp.Z) < 2 && r1.Count == 1 && r1[0].Text.Contains("proceed to initial runway two five via the turning point");
            var r2 = new List<Msg>();
            double hd = Bearing(crp.X, crp.Z, wp.X, wp.Z), len = Dist(crp.X, crp.Z, wp.X, wp.Z);
            for (double s = 5; s * 77 < len; s += 5) r2.AddRange(w.Tick(At(crp.X + s * 77 * Math.Cos(hd * Math.PI / 180), crp.Z + s * 77 * Math.Sin(hd * Math.PI / 180), hd), none, 2 + s));
            Chk(first && w.navName == "initial" && w.nav is { } n2 && Dist(n2.X, n2.Z, ip.X, ip.Z) < 2 && r2.Count(m => m.Text.Contains(" to initial.")) == 1, $"Knickpunkt ({w.Phase}, {w.navName}): " + J(r1) + " / " + J(r2));
            n += 2;
        }

        // K22: handover to the Tower about 2 minutes before the C R P; the point is reported to the Tower, who leads on without "contact Tower"
        {
            var k = Airfield.Kutaisi();
            var c = k.Crp["north"];
            Telemetry At(double x, double z) => new(k.Elev + k.PatternFt * Ft, k.PatternFt * Ft, 150 * Kt, 180 * Math.PI / 180, x, z, 0, 0, 760);
            var w = new Tower(k, "Enfield 1-1");
            w.Tick(At(c.X + 10 * NM, c.Z), none, 0);
            (w.Phase, w.Runway, w.entry, w.nav, w.navName) = (Phase.Inbound, "25", "north", c, "C R P north");
            var msgs = new List<Msg>();
            double x = c.X + 8 * NM, s = 1;
            for (; s < 400 && w.Phase == Phase.Inbound; s += 5, x -= 77) msgs.AddRange(w.Tick(At(x, c.Z), none, s));
            double dH = (x - c.X) / NM;
            var ho = msgs.FirstOrDefault(m => m.Role == "Approach");
            var r1 = w.OnTranscript("Kutaisi Tower, Enfield 11, inbound", At(x, c.Z), none, s);
            for (s += 5, x -= 77; x > c.X + 0.2 * NM; s += 5, x -= 77) msgs.AddRange(w.Tick(At(x, c.Z), none, s));   // on to the point
            var r2 = w.OnTranscript("Kutaisi Tower, Enfield 11, C R P north", At(x, c.Z), none, s);
            Chk(ho != null && ho.Text.StartsWith("Enfield one one, contact Kutaisi Tower ") && ho.Text.EndsWith("report C R P north.") && dH is > 3 and < 6.5 &&
                r1.Count == 1 && r1[0].Role == "Tower" && r1[0].Text.EndsWith("report C R P north.") && r2.Count == 1 && r2[0].Role == "Tower" && r2[0].Text.StartsWith("Enfield one one, proceed overhead") && !r2[0].Text.Contains("contact"),
                $"Übergabe vor dem CRP ({dH:0.0} NM, {w.Phase}): " + ho?.Text + " / " + J(r1) + " / " + J(r2));
            n += 3;
        }

        // Etappe 4 (K29/K33/K34/K36): taxi, holding point, vacate and return route from the data, every apron x every runway of all airfields (and via radio from the apron)
        int nTaxi = 0;
        foreach (var f in all.Where(a => a.Ramps.Length > 0))
            foreach (var e in f.Ends)
                foreach (var rp in f.Ramps)
                {
                    var (rw, say) = (e.Name, RwSay(e.Name));
                    var tx = TaxiOut(rp, rw, say);
                    bool hasH = rp.Hold.TryGetValue(rw, out var hn), hasTo = rp.To.TryGetValue(rw, out var tv);
                    Chk(tx.StartsWith("taxi to holding point ") && tx.Contains($"runway {say}") && (!hasH || tx.Contains($"holding point {hn}, runway {say}")) && (!hasTo || tv == hn || tx.Contains(tv!)),
                        $"{f.Name} {rp.Name} {rw} Rollweg: {tx}");
                    var tw = new Tower(f, "Enfield 1-1") { home = rp };
                    var vc = tw.Vacate(rw);
                    Chk(vc.Length > 0 && (!rp.Vacate.TryGetValue(rw, out var vv) || vc == $"Vacate {vv}."), $"{f.Name} {rp.Name} {rw} Abrollen: {vc}");
                    bool hasIn = rp.In.TryGetValue(rw, out var iv) || rp.In.TryGetValue("*", out iv);
                    Chk(tw.ParkRoute() == (hasIn ? " via " + iv : ""), $"{f.Name} {rp.Name} {rw} Rückweg: {tw.ParkRoute()}");
                    var g = new Telemetry(f.Elev, 0, 0, e.Hdg * Math.PI / 180, rp.X, rp.Z, 0, 0, 760);
                    var w = new Tower(f, "Enfield 1-1") { FieldWind = (-e.Dx * 5, -e.Dz * 5) };
                    w.Tick(g, none, 0);
                    (w.Phase, w.Runway) = (Phase.Parked, rw);
                    var r = w.OnTranscript($"{f.StationOf("Ground")}, Enfield 11, request taxi", g, none, 5);
                    Chk(r.Count >= 1 && r[0].Text.Contains(tx) && (!hasH || w.HoldPt(rw, g) is var hp && f.Holds.TryGetValue(hn!, out var ho) && Dist(hp.X, hp.Z, ho.X, ho.Z) < 1),
                        $"{f.Name} {rp.Name} {rw} Funk: {J(r)}");
                    nTaxi++;
                }
        n += nTaxi * 4;
        // fixed words: Batumi/Kobuleti (named holding points, stop-bar hint), stand names from the chart
        if (all.FirstOrDefault(a => a.Name == "Batumi") is { } bt && all.FirstOrDefault(a => a.Name == "Kobuleti") is { } ko)
        {
            Telemetry G(Airfield f, string rw, (double X, double Z) p) => new(f.Elev, 0, 0, f.End(rw).Hdg * Math.PI / 180, p.X, p.Z, 0, 0, 760);
            Tower T(Airfield f, string rw, Ramp rp, Phase ph) { var e = f.End(rw); var w = new Tower(f, "Enfield 1-1") { FieldWind = (-e.Dx * 5, -e.Dz * 5) }; w.Tick(G(f, rw, (rp.X, rp.Z)), none, 0); (w.Phase, w.Runway, w.home) = (ph, rw, rp); return w; }
            var a1 = bt.Ramps.First(q => q.Name == "APRON 1");
            var r31 = T(bt, "31", a1, Phase.Parked).OnTranscript("Batumi Ground, Enfield 11, request taxi", G(bt, "31", (a1.X, a1.Z)), none, 5);
            Chk(r31.Count == 1 && r31[0].Text.Contains("taxi to holding point Delta, runway three one") && !r31[0].Text.Contains("Stop bars"), "Batumi 31 benannter Warteplatz: " + J(r31));
            var r13 = T(bt, "13", a1, Phase.Parked).OnTranscript("Batumi Ground, Enfield 11, request taxi", G(bt, "13", (a1.X, a1.Z)), none, 5);
            Chk(r13.Count == 1 && r13[0].Text.Contains("taxi to holding point runway one three via Whiskey, Alpha") && r13[0].Text.EndsWith("Stop bars W one and W two on Whiskey, hold at the stop bar until cleared to cross."), "Batumi 13 Rollweg und Haltebalken: " + J(r13));
            var rp1 = Tower.TaxiNote(a1, "31", true);
            Chk(rp1.Contains("Stop bars W one and W two"), "Batumi Rückweg-Hinweis: " + rp1);
            var kn = ko.Ramps.First(q => q.Name == "North Stands");
            var rk = T(ko, "07", kn, Phase.Parked).OnTranscript("Kobuleti Ground, Enfield 11, request taxi", G(ko, "07", (kn.X, kn.Z)), none, 5);
            Chk(rk.Count == 1 && rk[0].Text.Contains("taxi to holding point November, runway zero seven"), "Kobuleti North Stands 07: " + J(rk));
            var wh = T(ko, "07", kn, Phase.TaxiOut);
            var nov = ko.Holds["November"];
            Chk(wh.AtHold(G(ko, "07", (nov.X, nov.Z))) && Dist(nov.X, nov.Z, wh.HoldPt("07", G(ko, "07", (kn.X, kn.Z))).X, wh.HoldPt("07", G(ko, "07", (kn.X, kn.Z))).Z) < 1, "Kobuleti: Bereitstellung am benannten Warteplatz");
            var sn = new Tower(ko, "Enfield 1-1");
            Chk(sn.StandName(2) == "November four two" && ko.StandChart["2"] == "N42" && !bt.StandChart.ContainsKey("2") && sn.StandName(99) == "niner niner", "Standname aus der Karte: " + sn.StandName(2) + " / " + sn.StandName(99));
            foreach (var f in all.Where(a => a.StandChart.Count > 0)) { var t2 = new Tower(f, "Enfield 1-1"); foreach (var (d, c) in f.StandChart) Chk(t2.StandName(int.Parse(d)).Split(' ').Length == c.Count(char.IsAsciiLetterOrDigit), $"{f.Name} Stand {d}: {t2.StandName(int.Parse(d))}"); }

            // K30/K31: Batumi 13 backtrack: holding point only leads into the middle of the runway -> "backtrack approved, line up", takeoff clearance only after the next "ready"
            var e13 = bt.End("13");
            var hp13 = e13.At(150, 100);
            var wb = T(bt, "13", a1, Phase.TaxiOut);
            var tb = G(bt, "13", hp13);
            wb.Tick(tb, none, 5);
            var b1 = wb.OnTranscript("Batumi Tower, Enfield 11, ready for departure", tb, none, 10);
            bool lined = wb.Phase == Phase.HoldShort && wb.LineUp;
            var run = new List<Msg>();
            double t0 = 15;
            for (int i = 0; i < 12; i++) run.AddRange(wb.Tick(G(bt, "13", e13.At(-100 - i * 40, 0)), none, t0 + 5 * i));
            var b2 = wb.OnTranscript("Batumi Tower, Enfield 11, ready for departure", G(bt, "13", e13.At(-540, 0)), none, t0 + 70);
            Chk(b1.Count == 1 && b1[0].Text.Contains("backtrack approved, line up runway one three") && lined && run.Count == 0 && b2.Count == 1 && b2[0].Text.Contains("cleared for takeoff") && wb.Phase == Phase.ClearedTakeoff,
                $"Batumi 13 Backtrack ({lined}, {wb.Phase}): " + J(b1) + " / " + J(run) + " / " + J(b2));
            var w31 = T(bt, "31", a1, Phase.TaxiOut);
            var hp31 = bt.Holds["Delta"];
            var b31 = w31.OnTranscript("Batumi Tower, Enfield 11, ready for departure", G(bt, "31", (hp31.X, hp31.Z)), none, 10);
            Chk(b31.Count == 1 && !b31[0].Text.Contains("backtrack"), "Batumi 31 ohne Backtrack: " + J(b31));
            n += 11;
            // Krymsk (exit Abinsk with restricted area), Sochi (shelters via Juliet), Beslan (backtrack both ways), Anapa (entry "south" next to exit "exit south")
            if (all.FirstOrDefault(a => a.Name == "Krymsk") is { } kr && all.FirstOrDefault(a => a.Name == "Sochi-Adler") is { } so && all.FirstOrDefault(a => a.Name == "Beslan") is { } be && all.FirstOrDefault(a => a.Name == "Anapa-Vityazevo") is { } an)
            {
                Tower W(Airfield f, string rw, Phase ph) { var e = f.End(rw); var w = new Tower(f, "Enfield 1-1") { FieldWind = (-e.Dx * 5, -e.Dz * 5) }; w.Tick(new(f.Elev + 500, 500, 150 * Kt, 0, f.X, f.Z, 0, 0, 760), none, 0); (w.Phase, w.Runway) = (ph, rw); return w; }
                Telemetry At(Airfield f, double x, double z, double agl, double hdg) => new(f.Elev + agl, agl, 150 * Kt, hdg * Math.PI / 180, x, z, 0, 0, 760);
                var wkr = T(kr, "22", kr.Ramps[0], Phase.Away);
                wkr.exitDir = "abinsk";
                var tk = wkr.TakeoffClearance(G(kr, "22", (kr.X, kr.Z)), none);
                Chk(tk.Contains("after departure exit via C R P abinsk, then zero four five, stay exactly on track, restricted area UR-R one five two, not above 2100 feet"), "Krymsk 22 Abinsk: " + tk);
                var sh = so.Ramps.First(q => q.Name == "SHELTERS");
                var rs = T(so, "06", sh, Phase.Parked).OnTranscript(so.StationOf("Ground") + ", Enfield 11, request taxi", G(so, "06", (sh.X, sh.Z)), none, 5);
                var r24 = T(so, "24", sh, Phase.Parked).OnTranscript(so.StationOf("Ground") + ", Enfield 11, request taxi", G(so, "24", (sh.X, sh.Z)), none, 5);
                Chk(rs.Count == 1 && rs[0].Text.Contains("taxi to holding point runway zero six via Juliet, cross the runway, Mike, Alpha") && rs[0].Text.Contains("Hold short of the runway at Juliet, cross only when cleared.")
                    && r24.Count == 1 && r24[0].Text.Contains("taxi to holding point Juliet, runway two four") && !r24[0].Text.Contains("cross only"), "Sochi Shelters: " + J(rs) + " // " + J(r24));
                var ra = be.Ramps[0];
                var hb = be.Holds.Values.First();
                var wbe = T(be, "10", ra, Phase.TaxiOut);
                var rb = wbe.OnTranscript(be.StationOf("Tower") + ", Enfield 11, ready for departure", G(be, "10", (hb.X, hb.Z)), none, 10);
                Chk(rb.Count == 1 && rb[0].Text.Contains("backtrack approved, line up runway one zero"), "Beslan 10 Backtrack: " + J(rb));
                var wan = W(an, "04", Phase.Inbound);
                var cs = an.Crp["south"];
                (wan.entry, wan.nav, wan.navName) = ("south", cs, "C R P south");
                var rn2 = wan.OnTranscript(an.StationOf("Approach") + ", Enfield 11, C R P south", At(an, cs.X, cs.Z, 1700 * Ft - an.Elev, 20), none, 2);
                var wx = W(an, "04", Phase.Inbound);
                var rx = wx.OnTranscript(an.StationOf("Approach") + ", Enfield 11, C R P exit south", At(an, cs.X, cs.Z, 1700 * Ft - an.Elev, 20), none, 2);
                Chk(rn2.Count == 1 && rn2[0].Text.Contains("proceed to initial runway zero four") && rx.Count == 1 && rx[0].Text.Contains("exit south is not available for runway zero four"), "Anapa south: " + J(rn2) + " // " + J(rx));
                n += 5;
            }
        }

        // Etappe 6: taxi control with the DCS taxi network of Kutaisi (read from the DCS installation; without it the test is skipped)
        if (all.FirstOrDefault(a => a.Name == "Kutaisi" && a.Charted) is { } ku)
        {
            Rollnetz.LoadAll(new[] { ku }, Program.DcsDir());
            var e7 = ku.End("07");
            var hpos = e7.At(-15, -63);   // taxi node at the start of runway 07, 63 m beside the axis
            var hnode = ku.Net?.Nodes.Length > 0 ? ku.Net.Nodes.MinBy(p => Dist(p.X, p.Z, hpos.Item1, hpos.Item2)) : ((double X, double Z)?)null;
            var rn = ku.Ramps.FirstOrDefault(q => q.Name == "Ramp North");
            if (ku.Net == null) Console.WriteLine("--   Rollnetz Taxikontrolle: DCS fehlt (Test übersprungen)");
            else if (hnode is not { } hd || Dist(hd.X, hd.Z, hpos.Item1, hpos.Item2) > 30 || rn == null) Console.WriteLine("--   Rollnetz Taxikontrolle: Kutaisi-Netz weicht ab (Test übersprungen)");
            else
            {
                ku.Holds["RnTest"] = (hd.X, hd.Z, new[] { "07" });
                try
                {
                    var ramp = new Ramp("Ramp North", rn.X, rn.Z, new(), new() { ["07"] = "RnTest" }, new(), new(), new(), new());
                    Telemetry K(double x, double z, double kt) => new(ku.Elev, 0, kt * Kt, e7.Hdg * Math.PI / 180, x, z, 0, 0, 760);
                    Tower W(double x, double z) { var w = new Tower(ku, "Enfield 1-1") { FieldWind = (-e7.Dx * 5, -e7.Dz * 5) }; w.Tick(K(x, z, 0), none, 0); (w.Phase, w.Runway, w.home) = (Phase.TaxiOut, "07", ramp); return w; }
                    var rt = ku.Net.RouteTo((rn.X, rn.Z), hd, (x, z) => ku.OnRunway(x, z));
                    Chk(rt != null && rt.Lines.Count > 0 && rt.Dist(rn.X, rn.Z) < 100 && rt.Dist(hd.X, hd.Z) < 1, "Rollnetz Kutaisi: Korridor Rampe Nord - Warteplatz");
                    if (rt != null)
                    {
                        // on the route (every point of the corridor away from the holding point and the runway): silent
                        var on = W(rn.X, rn.Z); double s = 1; var quiet = true;
                        on.Tick(K(rn.X, rn.Z, 8), none, s++);
                        foreach (var l in rt.Lines) foreach (var p in l) if (Dist(p.X, p.Z, hd.X, hd.Z) > 120 && !ku.OnRunway(p.X, p.Z)) quiet &= on.Tick(K(p.X, p.Z, 8), none, s++).Count == 0;
                        Chk(quiet && s > 20, $"Rollnetz Kutaisi: auf dem Rollweg keine Meldung ({s} Punkte)");
                        // away from the corridor (300 m beside the runway, the side away from the apron): once after 10 s, then silent
                        var far = new[] { e7.At(0, 300), e7.At(0, -300) }.MaxBy(p => rt.Dist(p.Item1, p.Item2));
                        var off = W(rn.X, rn.Z); off.Tick(K(rn.X, rn.Z, 8), none, 1);
                        var got = new List<(double T, Msg M)>();
                        for (double q = 2; q <= 40; q++) got.AddRange(off.Tick(K(far.Item1, far.Item2, 8), none, q).Select(m => (q, m)));
                        Chk(rt.Dist(far.Item1, far.Item2) > 100 && got.Count == 1 && got[0].T == 12 && got[0].M.Role == "Ground" && got[0].M.Text == "Enfield one one, hold position, say intentions.",
                            $"Rollnetz Kutaisi: Abweichung -> einmal hold position nach 10 s: {rt.Dist(far.Item1, far.Item2):0} m, " + string.Join(" | ", got.Select(g => $"{g.T} {g.M.Text}")));
                        // too slow (standing off the route): nothing
                        var st = W(rn.X, rn.Z); st.Tick(K(rn.X, rn.Z, 8), none, 1);
                        Chk(Enumerable.Range(2, 30).All(q => st.Tick(K(far.Item1, far.Item2, 2), none, q).Count == 0), "Rollnetz Kutaisi: Stillstand abseits des Rollwegs bleibt still");
                        // taxi guidance on the network: progressive along the shortest way -> silent (no left/right, no clock); backwards -> "wrong way, turn around" with the route after 8 s; "say again taxi" -> the route; 90 s stopped on the taxiway -> asked once
                        bool Rw(double x, double z) => ku.OnRunway(x, z);
                        var pth = ku.Net.PathTo((rn.X, rn.Z), hd, ku.Net.Spread(hd, Rw)!, Rw);
                        Telemetry KH(double x, double z, double kt, (double X, double Z) to) => new(ku.Elev, 0, kt * Kt, Math.Atan2(to.Z - z, to.X - x), x, z, 0, 0, 760);
                        Chk(pth is { Count: > 5 }, $"Rollnetz Kutaisi: Weg Rampe Nord - Warteplatz ({pth?.Count} Punkte)");
                        if (pth is { Count: > 5 })
                        {
                            var pg = W(rn.X, rn.Z); pg.ProgStart(true, 0.5); pg.taxiSaid = "taxi to RnTest via Alpha";
                            var said = new List<string>(); double tq = 0;
                            for (int i = 0; i + 1 < pth.Count; i++) if (Dist(pth[i].X, pth[i].Z, hd.X, hd.Z) > 150 && !Rw(pth[i].X, pth[i].Z)) said.AddRange(pg.Tick(KH(pth[i].X, pth[i].Z, 8, pth[i + 1]), none, tq += 11).Select(m => m.Text));
                            Chk(!said.Any(x => x.Contains("turn") || x.Contains("wrong way") || x.Contains("o'clock") && !x.Contains("hold short")), "Rollnetz Kutaisi: keine Richtungsansagen auf dem Weg: " + string.Join(" | ", said));
                            var ag = pg.OnTranscript("Kutaisi Ground, Enfield 11, say again taxi route", KH(pth[2].X, pth[2].Z, 0, pth[3]), none, tq + 1);
                            Chk(ag.Count == 1 && ag[0].Text == "Enfield one one, I say again, taxi to RnTest via Alpha.", "Rollnetz Kutaisi: say again taxi: " + J(ag));
                            int mi = pth.Count / 3;
                            var bk = W(pth[mi].X, pth[mi].Z); bk.ProgStart(true, 0.5); bk.taxiSaid = "taxi to RnTest via Alpha";
                            var bs = Enumerable.Range(1, 20).SelectMany(q => bk.Tick(KH(pth[mi].X, pth[mi].Z, 8, pth[mi - 1]), none, q).Select(m => (q, m.Text))).ToList();
                            Chk(bs.Count == 1 && bs[0].q >= 9 && bs[0].Text.EndsWith("wrong way, turn around, taxi to RnTest via Alpha."), "Rollnetz Kutaisi: falsche Richtung: " + string.Join(" | ", bs.Select(b => $"{b.q} {b.Text}")));
                            if (pth.Skip(1).FirstOrDefault(p => ku.Spots.All(sp => Dist(sp.X, sp.Z, p.X, p.Z) > 150) && Dist(p.X, p.Z, rn.X, rn.Z) > 150 && Dist(p.X, p.Z, hd.X, hd.Z) > 200 && !Rw(p.X, p.Z)) is { } sp)
                            {
                                var sk = W(rn.X, rn.Z); var spots0 = ku.Spots.ToList(); if (ku.Spots.Count == 0) ku.Spots.Add((rn.X, rn.Z, 1, 68));   // without mission data no stands: every position counts as stand
                                var ss = Enumerable.Range(1, 200).SelectMany(q => sk.Tick(K(sp.X, sp.Z, 0), none, q).Select(m => (q, m.Text))).ToList();
                                ku.Spots.Clear(); ku.Spots.AddRange(spots0);
                                Chk(ss.Count == 1 && ss[0].q > 90 && ss[0].Text.Contains("stopped on the taxiway"), "Rollnetz Kutaisi: Stillstand auf dem Rollweg: " + string.Join(" | ", ss.Select(b => $"{b.q} {b.Text}")));
                            }
                        }
                    }
                    // over the holding point towards the runway: once; at the holding point itself: silent
                    double lh = (hd.Z - e7.ThrZ) * e7.Dx - (hd.X - e7.ThrX) * e7.Dz, sg = Math.Sign(lh);
                    var over = W(hd.X, hd.Z);
                    var qh = over.Tick(K(hd.X, hd.Z, 5), none, 1);
                    var qo = over.Tick(K(hd.X + sg * 18 * e7.Dz, hd.Z - sg * 18 * e7.Dx, 5), none, 2);
                    var q2 = over.Tick(K(hd.X + sg * 19 * e7.Dz, hd.Z - sg * 19 * e7.Dx, 5), none, 3);
                    Chk(qh.Count == 0 && qo.Count == 1 && qo[0].Text.Contains("hold position") && q2.Count == 0, "Rollnetz Kutaisi: über den Warteplatz -> einmal hold position: " + J(qh) + " / " + J(qo) + " / " + J(q2));
                    n += 6;
                }
                finally { ku.Holds.Remove("RnTest"); }
            }
        }

        if (err.Count > 0) throw new Exception("PlatzTest: " + string.Join(" | ", err));
        Console.WriteLine($"OK   PlatzTest: {all.Count(a => a.Charted)} Plätze mit Platzverfahren, {n} Prüfungen (Seite, Höhe, CRP, Initial, Ausflug, exit_only, Knickpunkt, Übergabe, Rollweg {nTaxi} Rampe x Bahn, Warteplatz, Backtrack, Standname)");
    }
}
