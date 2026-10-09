using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= Self-test (DcsAtc --selftest)
    public static int SelfTest()
    {
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }
        var none = Array.Empty<Traffic>();
        var K = Airfield.Kutaisi();
        var kTower = new Tower(K, "x");
        double Thr25X = K.End("25").ThrX, Thr25Z = K.End("25").ThrZ, CX = K.X, CZ = K.Z, FieldElev = K.Elev, PatternFt = K.PatternFt;
        double Ux = K.End("07").Dx, Uz = K.End("07").Dz;   // Direction 07 -> 25
        var Crp = K.Crp;
        double LandHdg(string rw) => K.End(rw).Hdg;
        string Wind(Telemetry t) => kTower.Wind(t);
        Telemetry At(double x, double z, double agl = 0, double hdgDeg = 0, double ias = 0) =>
            new(FieldElev + agl, agl, ias, hdgDeg * Math.PI / 180, x, z, 0, 0, 760);
        (double, double) P(string rw, double along) => kTower.OnCenterline(rw, along);

        // --- Departure from Ramp North, runway 25
        var parked = At(-284246.6, 683966.5);
        var tw = new Tower("Enfield 1-1");
        tw.Tick(parked, none, 0);
        var r = tw.OnTranscript("Kutaisi Ground, Springfield 1-2, request startup.", parked, none, 1);
        Check(r[0].StartsWith("Enfield one one, Kutaisi Ground, start up approved. Runway two five") && r[0].Role == "Ground", r[0]);   // A4: foreign callsign only the second time
        r = tw.OnTranscript("Springfield 12 request taxi, departure north", parked, none, 2);
        Check(r[0].StartsWith("Springfield one two, Kutaisi Ground, taxi") && r[0].Contains("holding point runway two five via November, Delta"), r[0]);
        r = tw.OnTranscript("Taxi holding point runway 25 via November Delta, will report ready, Springfield 12", parked, none, 3);
        Check(r.Count == 0, "Rücklesen -> keine Antwort");
        r = tw.OnTranscript("Springfield one two ready for departure", parked, none, 3.5);
        Check(r[0].Contains("negative, you are not at the holding point"), r[0]);
        {   // R349: "ready for departure" only at the holding point (250 m), 500 m before it -> continue taxi
            var tw349 = new Tower("Enfield 1-1");
            tw349.Tick(parked, none, 0);
            tw349.OnTranscript("Kutaisi Ground, Enfield 11, request taxi", parked, none, 1);
            var e25 = K.End("25"); var hp25 = tw349.HoldPt("25", At(Thr25X, Thr25Z + 120));
            var early349 = At(hp25.X - 500 * e25.Dx, hp25.Z - 500 * e25.Dz);
            var r349 = tw349.OnTranscript("Kutaisi Tower, Enfield 11, ready for departure", early349, none, 2)[0].Text;
            Check(r349.Contains("Continue taxi to holding point runway two five") && tw349.Phase == Phase.TaxiOut && Dist(early349.X, early349.Z, Thr25X, Thr25Z) < 700, "R349 ready 500 m vor dem Rollhalt: " + r349);
        }
        {   // Forum log 0.9.4: readback of the taxi clearance (Whisper "CY" instead of "taxi") with "contact tower when ready for departure" is not takeoff readiness
            var trb = new Tower("Enfield 1-1") { HeardOn = 263 };   // F-14 on Kutaisi UHF: one frequency for Ground/Tower, the controller follows the intent
            trb.Tick(parked, none, 0);
            trb.OnTranscript("Ground: request startup", parked, none, 1);
            var rb0 = trb.OnTranscript("Enfield 1-1, request taxi to active runway.", parked, none, 100);
            var rbk = trb.OnTranscript("Check CY on November Delta. Hold short. Contact tower when ready for departure. Enfield 1-1.", parked, none, 130);
            Check(rb0[0].Contains("Hold short runway two five, contact Kutaisi Tower") && rbk.Count == 0 && trb.Phase == Phase.TaxiOut, "Rollfreigabe-Rücklesung mit \"when ready for departure\" -> keine Antwort: " + string.Join(" | ", rbk.Select(x => x.Text)));
            // Same on the config Ground frequency: Program.OnSrs prepends "Ground: ", that is not a call from the pilot
            var trg = new Tower("Enfield 1-1");
            trg.Tick(parked, none, 0);
            trg.OnTranscript("Ground: Enfield 1-1, request startup.", parked, none, 1);
            trg.OnTranscript("Ground: Enfield 1-1, request taxi to active runway.", parked, none, 100);
            var rgk = trg.OnTranscript("Ground: Check CY on November Delta. Hold short. Contact tower when ready for departure. Enfield 1-1.", parked, none, 130);
            var rgr = trg.OnTranscript("Ground: Kutaisi Ground, Enfield 1-1, request taxi.", parked, none, 140);   // real call with station stays a request
            Check(rgk.Count == 0 && trg.Phase == Phase.TaxiOut && rgr.Count == 1 && rgr[0].Text.Contains("taxi to holding point"),
                  "Rollfreigabe-Rücklesung auf Config-Ground -> keine Antwort: " + string.Join(" | ", rgk.Concat(rgr).Select(x => x.Text)));
        }
        var hold = At(Thr25X - 50, Thr25Z + 120);
        r = tw.OnTranscript("Springfield one two ready for departure", hold, none, 4);
        Check(tw.Phase == Phase.ClearedTakeoff && r[0].Contains("cleared for takeoff") && r[0].Contains("C R P north"), r[0]);
        r = tw.OnTranscript("Springfield 1-2, say again", parked, none, 5);
        Check(r[0].Contains("cleared for takeoff"), "say again wiederholt");
        r = tw.OnTranscript("Come again?", parked, none, 6);
        Check(r[0].Contains("cleared for takeoff"), "come again wiederholt");
        r = tw.OnTranscript("Springfield 12, didn't copy", parked, none, 7);
        Check(r.Count == 1 && r[0].Contains("cleared for takeoff"), "didn't copy wiederholt");
        r = tw.OnTranscript("Springfield 12, confirm cleared for takeoff", parked, none, 8);
        Check(r.Count == 1 && r[0].Contains("cleared for takeoff"), "confirm wiederholt");
        r = tw.OnTranscript("Kutaisi Ground, Springfield 12, request taxi", parked, none, 9);
        r = tw.OnTranscript("Kutaisi Ground, Springfield 12, request taxi", parked, none, 12);
        Check(r.Count == 1 && r[0].Contains("holding point"), "neu gefragt -> neue Antwort");
        r = tw.OnTranscript("Springfield 12, confirm runway in use", parked, none, 13);
        Check(r.Count == 1 && r[0].Contains("runway two five in use"), r.FirstOrDefault()?.Text ?? "runway?");
        // A6, A7: "engines started", location and ATIS in the taxi call = taxi clearance to the holding point; combined request answers both; startup not after the takeoff clearance
        foreach (var call in new[] { "Kutaisi Ground, Enfield 1-1, engines started, request taxi", "Kutaisi Ground, Enfield 1-1, parking spot 12, information Charlie, request taxi",
                                     "Kutaisi Ground, Enfield 1-1, request startup and taxi" })
        {
            var t6 = new Tower("Enfield 1-1") { AtisLetter = "C" };
            t6.Tick(parked, none, 0);
            r = t6.OnTranscript(call, parked, none, 1);
            Check(t6.Phase == Phase.TaxiOut && r[0].Contains("taxi to holding point runway two five via November, Delta") && r[0].Contains("start up approved") == call.Contains("startup") &&
                  r[0].Contains("Information Charlie is current") == !call.Contains("Charlie") && r[0].Contains("QNH") == !call.Contains("Charlie"), $"A6/A7/A33 \"{call}\": " + r[0]);   // A33: ATIS reported -> no QNH either
            t6.OnTranscript("Enfield 11 ready for departure", hold, none, 2);
            r = t6.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", hold, none, 3);
            Check(t6.Phase == Phase.ClearedTakeoff && !r[0].Contains("start up approved"), "A6 Anlassen nach der Startfreigabe: " + r[0]);
        }
        foreach (var call in new[] { "request engine start", "requesting start", "ready to start" })   // other engine-start phrasings remain engine start
        {
            var t6 = new Tower("Enfield 1-1");
            t6.Tick(parked, none, 0);
            r = t6.OnTranscript("Kutaisi Ground, Enfield 1-1, " + call, parked, none, 1);
            Check(t6.Phase == Phase.StartupApproved, $"A6 \"{call}\": " + r[0]);
        }

        // --- After takeoff: Tower hands over to Departure (Approach), "airborne" -> radar contact
        var twD = new Tower("Enfield 1-1");
        twD.Tick(parked, none, 0);
        twD.OnTranscript("Enfield 11 request startup", parked, none, 1);
        twD.OnTranscript("Enfield 11 request taxi", parked, none, 2);
        twD.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
        var (dx, dz) = P("25", -1.5 * NM);
        twD.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
        r = twD.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
        Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Contains("contact Kutaisi Approach"), "Übergabe an Departure: " + (r.FirstOrDefault()?.Text ?? "nichts"));
        r = twD.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        // R7: VFR in the zone: departure clearance remains valid, sign-off at the zone edge with "resume own navigation" (before, directly "resume own navigation, maintain VFR")
        Check(r[0].Role == "Approach" && r[0].Text.EndsWith("radar contact, 1 mile west of the field. Continue as cleared, report leaving the control zone.") && twD.Phase == Phase.Away, "R7 Airborne-Meldung in der Zone: " + r[0]);
        // Forum request AirfieldFrequencies: own frequency per controller -> handovers name it, Approach leads the departure as Departure on its frequency, sign-off too
        var kO = Airfield.Kutaisi();
        (kO.Own["Ground"], kO.Own["Tower"], kO.Own["Departure"], kO.Own["Approach"]) = (new[] { 275.8 }, new[] { 327.0 }, new[] { 273.55 }, new[] { 379.0 });
        var twAf = new Tower(kO, "Enfield 1-1") { HeardOn = 275.8 };
        twAf.Tick(parked, none, 0);
        twAf.OnTranscript("Kutaisi Ground, Enfield 11 request startup", parked, none, 1);
        var oTx = twAf.OnTranscript("Kutaisi Ground, Enfield 11 request taxi", parked, none, 2);
        var oTk = twAf.OnTranscript("Kutaisi Ground, Enfield 11 ready for departure", hold, none, 3);
        twAf.HeardOn = 327.0;
        twAf.OnTranscript("Kutaisi Tower, Enfield 11 ready for departure", hold, none, 4);
        twAf.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
        var oHo = twAf.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
        twAf.HeardOn = 273.55;
        var oRc = twAf.OnTranscript("Kutaisi Departure, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        var twBk = new Tower(kO, "Enfield 1-1") { HeardOn = 379.0 };   // Flight following after departure, then back to landing: Approach again, not Departure
        twBk.Load(twAf.Save() with { Following = true }, At(CX + 20 * NM, CZ, 1500, 270, 200), 21);
        var oIn = twBk.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", At(CX + 20 * NM, CZ, 1500, 270, 200), none, 22);
        // R275: the longer first call (radio-wheel transmission with position and altitude, Program.PilotCall) is understood like "inbound for landing"
        var twLi = new Tower("Enfield 1-1"); var farLi = At(CX + 20 * NM, CZ, 1500, 270, 200); twLi.Tick(farLi, none, 0);
        var rLi = twLi.OnTranscript("Kutaisi Approach, Enfield 1 1, 20 miles east, 4900 feet, inbound for landing.", farLi, none, 1);
        Check(rLi.Count > 0 && rLi[0].Text.StartsWith("Enfield one one, Kutaisi Approach, ") && rLi[0].Text.Contains("xpect vectors") && twLi.Phase == Phase.Inbound, "R275 Erstanruf mit Position und Höhe: " + string.Join(" | ", rLi.Select(m => m.Text)));
        var oBye = twAf.Tick(At(CX - 9.5 * NM, CZ, 2000, 270, 200), none, 25);   // Zone edge without report (#10): reminder from Departure
        oBye.AddRange(twAf.OnTranscript("Kutaisi Departure, Enfield 11, leaving the control zone", At(CX - 9.6 * NM, CZ, 2000, 270, 200), none, 26));   // report -> sign-off from Departure
        var twOc = new Tower(kO, "Enfield 1-1") { HeardOn = 275.8 };   // IFR clearance on the ground: Departure frequency in the clearance
        twOc.Tick(parked, none, 0);
        var oCl = twOc.OnTranscript("Kutaisi Ground, Enfield 11, request IFR clearance to Batumi", parked, none, 1);
        Check(oTx[0].Role == "Ground" && oTx[0].Contains("contact Kutaisi Tower three two seven decimal zero") && oTk[0].Role == "Ground" && oTk[0].Contains("contact Kutaisi Tower three two seven decimal zero")
              && oHo.Count == 1 && oHo[0].Role == "Tower" && oHo[0].Contains("contact Kutaisi Departure two seven three decimal five five")
              && oRc[0].Role == "Departure" && oRc[0].Contains("Kutaisi Departure, radar contact") && oBye.Count == 2 && oBye.All(m => m.Role == "Departure") && oBye[0].Text.EndsWith("report leaving the control zone.") && oBye[1].Contains("resume own navigation")
              && twBk.Phase == Phase.Inbound && oIn[0].Role == "Approach" && oIn[0].Contains("Kutaisi Approach") && !oIn[0].Contains("Departure")
              && oCl[0].Contains("departure frequency two seven three decimal five five, squawk") && !oCl[0].Contains("Approach"),
              $"AirfieldFrequencies Übergaben: {oTx[0]} | {oTk[0]} | {string.Join(" / ", oHo)} | {oRc[0].Role}: {oRc[0]} | {string.Join(" / ", oBye.Select(m => m.Role + ": " + m.Text))} | {twBk.Phase} {oIn[0].Role}: {oIn[0]} | {oCl[0]}");
        // R128: flight without code in the air: first "squawk", after 6 s "radar contact" (not in one transmission); on the ground/phase left: expires
        var airId = At(dx, dz, 600, LandHdg("25"), 250);
        var twId = new Tower("Enfield 1-1");
        twId.Tick(airId, none, 0);
        r = twId.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", airId, none, 20);
        var idA = twId.Tick(airId, none, 25).Count == 0 && twId.Tick(airId, none, 26) is [{ } idR] && idR.Role == "Approach" && idR.Contains("radar contact") && !idR.Contains("squawk") ? "ok" : "falsch";
        Check(r.Count == 1 && r[0].Contains("squawk") && !r[0].Contains("radar contact") && idA == "ok" && twId.Tick(airId, none, 40).Count == 0, "R128 airborne ohne Code: " + string.Join(" | ", r.Select(m => m.Text)) + " / " + idA);
        twD.AwacsContact = "Overlord two five one decimal zero";
        // Reporting obligation #10: at the zone edge without "leaving the control zone" remind once, sign off 1 NM outside (30 s later) and into the debriefing, no second one at 15 NM
        r = twD.Tick(At(CX - 9.5 * NM, CZ, 2000, 270, 200), none, 25);
        var zr2 = twD.Tick(At(CX - 10.5 * NM, CZ, 2000, 270, 200), none, 40);
        var zr3 = twD.Tick(At(CX - 11 * NM, CZ, 2000, 270, 200), none, 56);
        Check(r.Count == 1 && r[0].Role == "Approach" && r[0].Text == "Enfield one one, report leaving the control zone." && zr2.Count == 0 &&
              zr3.Count == 2 && zr3[0].Role == "Approach" && zr3[0].Text == "Enfield one one, I show you clear of the zone, radar service terminated, resume own navigation, contact Overlord two five one decimal zero." &&
              zr3[1].Text.StartsWith(L("Verstoß: keine Meldung", "Deviation: no report")) && twD.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 60).Count == 0,
              "#10 Abmeldung am Zonenrand ohne Meldung, keine zweite bei 15 NM: " + string.Join(" | ", r.Concat(zr2).Concat(zr3)));
        // VFR, check-in outside the zone: "resume own navigation, maintain VFR", sign-off at 15 NM, with AWACS handover there (once)
        var twDo = new Tower("Enfield 1-1");
        twDo.Tick(parked, none, 0);
        twDo.OnTranscript("Enfield 11 request taxi", parked, none, 2);
        twDo.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
        twDo.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
        twDo.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
        r = twDo.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(CX - 9.5 * NM, CZ, 600, 270, 250), none, 20);
        Check(r[0].Contains("radar contact") && r[0].Contains("maintain VFR") && !r[0].Contains("flight level") && twDo.Phase == Phase.Away, "Airborne-Meldung außerhalb der Zone: " + r[0]);
        twDo.AwacsContact = "Overlord two five one decimal zero";
        r = twDo.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 60);
        Check(r.Count == 1 && r[0].Contains("radar service terminated, contact Overlord two five one decimal zero") && twDo.Tick(At(CX - 17 * NM, CZ, 4000, 270, 300), none, 70).Count == 0,
              "Abmeldung 15 NM: " + (r.FirstOrDefault()?.Text ?? "nichts"));
        {   // R7: pilot reports "leaving the control zone" himself -> sign-off, no second one at the zone edge
            var twDr = new Tower("Enfield 1-1");
            twDr.Tick(parked, none, 0);
            twDr.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            twDr.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
            twDr.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
            twDr.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
            twDr.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
            var sgR7 = twDr.Suggest(At(CX - 5 * NM, CZ, 600, 270, 250));
            var lz = twDr.OnTranscript("Kutaisi Approach, Enfield 11, leaving the control zone", At(CX - 8 * NM, CZ, 600, 270, 250), none, 28);
            var lz2 = twDr.Tick(At(CX - 10 * NM, CZ, 600, 270, 250), none, 30);
            Check(sgR7?.Text == "leaving the control zone" && lz.Count == 1 && lz[0].Role == "Approach" && lz[0].Text == "Enfield one one, roger, resume own navigation, squawk VFR, frequency change approved. Good day." && lz2.Count == 0,
                  "R7 leaving the control zone: " + string.Join(" | ", lz.Concat(lz2)));
        }

        // A5 · R40: first call to Departure ("Kutaisi Approach, Enfield 1-1", "…, with you") and "airborne" below 100 m = check-in, not a landing announcement
        foreach (var (call, agl) in new[] { ("Kutaisi Approach, Enfield 1-1", 250.0), ("Approach, Enfield 1-1, with you", 250.0),
                                            ("Kutaisi Departure, Enfield 1-1, passing 1500 for 2200", 250.0), ("Kutaisi Approach, Enfield 1-1, airborne", 50.0) })
        {
            var ta = new Tower("Enfield 1-1");
            ta.Tick(parked, none, 0);
            ta.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            ta.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
            if (agl > 100) { ta.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10); ta.Tick(At(dx, dz, agl, LandHdg("25"), 200), none, 11); }
            r = ta.OnTranscript(call, At(dx, dz, agl, LandHdg("25"), 200), none, 12);
            Check(r.Count == 1 && r[0].Role == "Approach" && r[0].Contains("radar contact") && ta.Phase == Phase.Away, $"A5 \"{call}\": " + (r.FirstOrDefault()?.Text ?? "nichts"));
            // Farewell is not a check-in; flight following, "leaving frequency" -> one sign-off, no second at 15 NM
            r = ta.OnTranscript("Kutaisi Approach, Enfield 1-1, good day", At(dx, dz, agl, LandHdg("25"), 200), none, 13);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, good day.", $"A5 good day nach \"{call}\": " + (r.FirstOrDefault()?.Text ?? "nichts"));
            ta.OnTranscript("Kutaisi Approach, Enfield 1-1, request flight following", At(dx, dz, agl, LandHdg("25"), 200), none, 14);
            r = ta.OnTranscript("Kutaisi Approach, Enfield 1-1, leaving frequency", At(dx, dz, agl, LandHdg("25"), 200), none, 15);
            var r2 = ta.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 16);
            Check(r.Count == 1 && r[0].Contains("radar service terminated") && r2.Count == 0, $"A5 leaving frequency nach \"{call}\": " + string.Join(" | ", r.Concat(r2).Select(m => m.Text)));
        }

        {   // Forum log 0.9.4: departure via C R P west, Approach check-in after "contact Approach" -> no "contact Tower" back, exactly one handover
            var tp = new Tower("Enfield 1-1");
            tp.Tick(parked, none, 0);
            tp.OnTranscript("Enfield 1-1, request taxi to active runway.", parked, none, 2);
            r = tp.OnTranscript("Kutaisi Tower, Enfield 1-1 holding short 27. Ready for departure.", hold, none, 3);
            bool clrOk = r[0].Contains("exit via C R P west") && r[0].Contains("cleared for takeoff");
            var log = new List<Msg>();
            var ci = new List<Msg>();
            double tt = 10;
            for (double a = -1.0; a > -17; a -= 0.25, tt += 4)   // Climb across the zone boundary to beyond 15 NM, already above the zone altitude before check-in
            {
                var (qx, qz) = P("25", a * NM);
                var at = At(qx, qz, Math.Min(1200, 60 + (a + 1) * -1000), LandHdg("25"), 250);   // like the F-14 in the log above 2600 ft before check-in (Tower said "check altitude")
                var tk = tp.Tick(at, none, tt);
                log.AddRange(tk);
                if (tt == 30) ci = tp.OnTranscript("Kutaisi Approach, checking in.", at, none, tt + 1);   // 17 s after "contact Kutaisi Approach" (log: 13 s)
                if (tk.Any(m => m.Text.EndsWith("report leaving the control zone."))) log.AddRange(tp.OnTranscript("Kutaisi Approach, Enfield 1-1, leaving the control zone", at, none, tt + 1));   // #10: pilot reports on reminder
            }
            Check(clrOk && log.Count(m => m.Text.Contains("contact Kutaisi Approach")) == 1 && ci.Count == 1 && ci[0].Role == "Approach" && ci[0].Contains("radar contact") &&
                  !ci[0].Contains("contact Kutaisi Tower") && log.Count(m => m.Role == "Tower") == 1 && log.Count(m => m.Role == "Approach") == 2 && log.Last().Contains("roger, resume own navigation"),   // R7: VFR sign-off at the zone edge instead of at 15 NM
                  "Abflug C R P west: eine Übergabe, Approach-Check-in angenommen: " + string.Join(" | ", log.Concat(ci).Select(m => $"{m.Role}: {m.Text}")));
        }

        // --- V14: route clearance to Batumi, departure vector, end after 15 NM
        All = new List<Airfield> { K, new() { Name = "Krasnodar-Center" }, new() { Name = "Batumi", X = -356000, Z = 618000 }, new() { Name = "Krasnodar-Pashkovsky" } };
        var twCl = new Tower("Enfield 1-1");
        twCl.Tick(parked, none, 0);
        r = twCl.OnTranscript("Kutaisi Ground, Enfield 11, request IFR clearance to Batumi", parked, none, 1);
        Check(r[0].Role == "Ground" && r[0].Contains("cleared to Batumi airport via radar vectors") && r[0].Contains("squawk") && r[0].Contains("departure Kutaisi Approach"), r[0]);   // without own Departure frequency as before
        r = new Tower("Enfield 1-1").OnTranscript("Kutaisi Ground, Enfield 11, request IFR clearance to Krasnodar Pashkovsky", parked, none, 1);
        Check(r[0].Contains("cleared to Krasnodar Pashkovsky airport via"), "A3 Ziel nach Namenswörtern: " + r[0]);
        // R113: pop-up IFR in the air = route clearance to the destination (squawk, heading, altitude), no landing here
        var twPop = new Tower("Enfield 1-1");
        var w30 = At(CX - 30 * NM, CZ, 3000, 90, 150);
        twPop.Tick(w30, none, 0);
        r = twPop.OnTranscript("Kutaisi Approach, Enfield 1-1, 30 miles west, request IFR clearance to Batumi", w30, none, 1);
        // R219: first only the code (without "radar contact"), after observing (6 s) "radar contact" and the clearance
        Check(r.Count == 1 && r[0].Role == "Approach" && r[0].Contains("squawk") && !r[0].Contains("radar contact") && !r[0].Contains("leared") && twPop.Phase == Phase.Away, "R219 Pop-up IFR: erst der Code: " + r[0]);
        r = twPop.Tick(w30, none, 8);
        Check(r.Count == 1 && r[0].Role == "Approach" && r[0].Contains("radar contact") && r[0].Contains("Cleared to Batumi via radar vectors, fly heading") &&
              r[0].Contains("climb and maintain") && !r[0].Contains("ILS") && !r[0].Contains("squawk") && twPop.Phase == Phase.Away, "R113 Pop-up IFR: " + r.FirstOrDefault()?.Text);
        double hoNm = 0; var rp = new List<Msg>();   // no second transmission right after, handover only 5 NM further (0.4 NM per 5 s)
        for (int k = 1; k <= 20 && hoNm == 0; k++)
            foreach (var m in twPop.Tick(At(CX - (30 + 0.4 * k) * NM, CZ, 3000, 250, 150), none, 10 + 5 * k)) { rp.Add(m); if (m.Text.Contains("contact") || m.Text.Contains("terminated")) hoNm = 30 + 0.4 * k; }
        Check(hoNm > 34.9 && hoNm < 35.5 && rp.Last().Text.Contains("contact Batumi Approach"), $"R113 Übergabe erst 5 NM weiter ({hoNm:0.0} NM): " + string.Join(" | ", rp.Select(m => m.Text)));
        var twPd = new Tower("Enfield 1-1");   // R219: phase changed before identification -> pop-up clearance expires too (no clearance on a later identification)
        twPd.Tick(w30, none, 0);
        twPd.OnTranscript("Kutaisi Approach, Enfield 1-1, 30 miles west, request IFR clearance to Batumi", w30, none, 1);
        twPd.Phase = Phase.Inbound;
        twPd.Tick(w30, none, 3);
        Check(twPd.idAt < 0 && twPd.popDf == null, "R219 verfallene Pop-up-Freigabe");
        {   // R222: led through the approach line (turn-in no longer possible) -> "vectors across final for re-sequencing", not the first-contact wording
            var (rx22, rz22) = (-291000.0, 660000.0);
            var tw222 = new Tower("Enfield 1-1");
            tw222.Tick(At(rx22, rz22, 600, 70, 120), none, 0);
            tw222.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing, straight in", At(rx22, rz22, 600, 70, 120), none, 1);
            var (lx22, lz22) = tw222.Pt(tw222.vrw, 4 * NM, 5 * NM);
            tw222.vec = new(); tw222.lastVecSaid = -100;
            var r222 = tw222.Tick(At(lx22, lz22, 900, 254, 130 * Kt), none, 2);
            Check(r222.Count == 1 && r222[0].Contains("vectors across final for re-sequencing") && !r222[0].Contains("vectors for final approach"), $"R222 Eindrehen nicht mehr möglich ({tw222.Phase} {tw222.vecFinal} {tw222.vrw}): " + string.Join(" | ", r222.Select(m => m.Text)));
            var (ix22, iz22) = tw222.Pt(tw222.vrw, 2.5 * NM, 0.4 * NM);   // R318: within the gate almost on the centerline -> position and "say intentions", no clearance with gate altitude
            tw222.vec = new(); tw222.lastVecSaid = -100;
            var r318 = tw222.Tick(At(ix22, iz22, 400, tw222.LandHdg(tw222.vrw), 130 * Kt), none, 3);
            Check(r318.Count == 1 && r318[0].Contains("miles from the airport, one half mile") && r318[0].Contains("of course, say intentions") && !r318[0].Contains("cleared"), $"R318 innerhalb des Gates ({tw222.Phase} {tw222.vecFinal}): " + string.Join(" | ", r318.Select(m => m.Text)));
        }
        {   // R8: clearance limit without destination = nearest airfield in departure direction (25: west, not the nearer one in the east); readback with the code -> "readback correct", then silent
            var allR8 = All;
            All = new List<Airfield> { K, new() { Name = "Nahost", X = CX, Z = CZ + 20 * NM }, new() { Name = "Nahwest", X = CX, Z = CZ - 30 * NM } };
            var twR8 = new Tower("Enfield 1-1");
            twR8.Tick(parked, none, 0);
            var c8 = twR8.OnTranscript("Kutaisi Ground, Enfield 11, request IFR clearance", parked, none, 1);
            var rbR8 = twR8.OnTranscript($"Cleared to Nahwest airport, runway heading, 3000, squawk {Digits(twR8.Squawk!)}, Enfield 1-1", parked, none, 5);
            var rb2 = twR8.OnTranscript($"Squawk {twR8.Squawk}, Enfield 1-1", parked, none, 8);
            All = allR8;
            Check(c8[0].Contains("cleared to Nahwest airport via radar vectors") && rbR8.Count == 1 && rbR8[0].Role == "Ground" && rbR8[0].Text == "Enfield one one, readback correct." && rb2.Count == 0,
                  "R8 Ziel in Abflugrichtung, readback correct: " + string.Join(" | ", c8.Concat(rbR8).Concat(rb2)));
        }
        r = new Tower(K, "Enfield 1-1").OtherField("Senaki Tower, Enfield 1-1, ready for departure", new Airfield { Name = "Senaki-Kolkhi", Uhf = 261 });
        Check(r[0].Role == "Tower" && r[0].Text == "Enfield one one, this is Kutaisi Tower, you are on Kutaisi frequency, contact Senaki Kolkhi Tower two six one decimal zero.", "A3 andere Platzfrequenz: " + r[0]);
        // A4: callsign only from the sender (start/end of transmission, twice the same), not from digits in the readback; fixed from the mission
        foreach (var s in new[] { "Contact Kutaisi Approach two six six decimal five, Enfield 1-1", "Squawk four three two one, Enfield 1-1", "parking spot seven, Enfield 1-1",
                                  "Continue approach, report four mile final, Enfield 1-1" })
            Check(ExtractCallsign(Normalize(s)) == "Enfield 1-1", $"A4 Rufzeichen aus \"{s}\": {ExtractCallsign(Normalize(s))}");
        var twA4 = new Tower("Enfield 1-1");
        twA4.OnTranscript("Enfield 1-2, check fuel", parked, none, 1);
        twA4.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", parked, none, 1.2);
        twA4.OnTranscript("Enfield 1-2, check fuel", parked, none, 1.5);   // flight call again: not twice in a row
        twA4.CallsignFixed = true;
        twA4.OnTranscript("Kutaisi Ground, Colt 1-1, request startup", parked, none, 2);
        twA4.OnTranscript("Kutaisi Ground, Colt 1-1, request taxi", parked, none, 3);
        Check(twA4.Callsign == "Enfield 1-1" && ExtractCallsign(Normalize("Squawk four three two one")) == null, "A4 Rottenruf einmal, Missions-Rufzeichen fest: " + twA4.Callsign);
        // Forum log 0.9.4: after takeoff with Approach, then back to Tower; Whisper hears "outfit 1-1" -> same reply as with Enfield, never renamed; without a station at the start no flight radio (same number)
        foreach (var (fixd, say) in new[] { true, false }.SelectMany(b => new[] { "Kutaisi Tower, {0} 1-1, checking in.", "{0} 1-1, checking in.", "{0} 1-1, unable." }.Select(s => (b, s))))
        {
            var rs = new[] { "Enfield", "outfit" }.Select(w =>
            {
                var tO = new Tower("Enfield 1-1") { CallsignFixed = fixd };
                tO.Tick(parked, none, 0);
                tO.OnTranscript("Enfield 1-1, request taxi to active runway.", parked, none, 2);
                tO.OnTranscript("Kutaisi Tower, Enfield 1-1 holding short 27. Ready for departure.", hold, none, 3);
                tO.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
                for (int i = 0; i < 40; i++) tO.Tick(At(dx - i * 0.1 * NM, dz, 250, LandHdg("25"), 200), none, 11 + i);
                tO.OnTranscript("Kutaisi Approach, checking in.", At(dx - 4 * NM, dz, 250, LandHdg("25"), 200), none, 60);
                var rt = tO.OnTranscript(string.Format(say, w), At(dx - 4.2 * NM, dz, 250, LandHdg("25"), 200), none, 72)
                    .Concat(tO.Tick(At(dx - 4.4 * NM, dz, 250, LandHdg("25"), 200), none, 90));
                return (tO.Callsign, Text: string.Join(" | ", rt.Select(m => m.Text)));
            }).ToList();
            Check(rs.All(x => x.Callsign == "Enfield 1-1") && rs[1].Text == rs[0].Text && rs[1].Text.StartsWith("Enfield one one") && !rs[1].Text.Contains("Station calling") && !rs[1].Text.Contains("Outfit"),
                  $"Verhörtes Rufzeichen \"{string.Format(say, "outfit")}\" (fest {fixd}) wie Enfield: {rs[1].Callsign}: {rs[1].Text}");
        }
        twCl.OnTranscript("Enfield 11 request taxi", parked, none, 2);
        r = twCl.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
        Check(r[0].Contains("after departure fly runway heading, climb and maintain"), r[0]);
        twCl.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
        twCl.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
        r = twCl.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        Check(r[0].Contains("radar contact") && r[0].Contains("Fly heading two") && r[0].Contains("climb and maintain 14000 feet") && !r[0].Contains("squawk"), "A102 Batumi (Kurs 215): gerade Höhe: " + r[0]);
        twCl.Tick(At(dx, dz, 900, 215, 250), none, 25);
        r = twCl.Tick(At(CX - 12 * NM, CZ - 11 * NM, 4000, 215, 250), none, 30);
        Check(r.Count == 1 && r[0].Contains(", contact Batumi Approach ") && !r[0].Contains("radar service terminated"), r.FirstOrDefault()?.Text ?? "Departure-Vektor Ende: -");

        // A90 · A102 · A103: code once per flight (route clearance, VFR taxi clearance, flight following), "squawk VFR" only after; cruising altitude by semicircular rule
        All = new List<Airfield> { K, new() { Name = "Nahost", X = CX, Z = CZ + 30 * NM }, new() { Name = "Nahwest", X = CX, Z = CZ - 30 * NM }, new() { Name = "Fernost", X = CX, Z = CZ + 60 * NM } };
        string Sq(string text) => Regex.Match(text, @"(?<=squawk )(?:(?:zero|one|two|three|four|five|six|seven|niner) ?)+", RegexOptions.IgnoreCase).Value.Trim();
        foreach (var (dn, ft) in new[] { ("Nahost", "5000 feet"), ("Nahwest", "4000 feet"), ("Fernost", "15000 feet") })
        {
            var tcl = new Tower("Enfield 1-1");
            tcl.Tick(parked, none, 0);
            r = tcl.OnTranscript($"Kutaisi Ground, Enfield 11, request IFR clearance to {dn}", parked, none, 1);
            Check(r[0].Contains($"expect {ft} ten minutes") && tcl.Squawk is { Length: 4 } sqc && Sq(r[0]) == Digits(sqc) && sqc.All(ch => ch is >= '0' and <= '7') && sqc is not ("7500" or "7600" or "7700" or "1200"),
                  $"A102/A90 Streckenfreigabe {dn}: " + r[0].Text);
            var code = tcl.Squawk;
            Check(tcl.OnTranscript($"Kutaisi Ground, Enfield 11, request IFR clearance to {dn}", parked, none, 1.5)[0].Text.Contains($"squawk {Digits(code!)}") && tcl.Squawk == code, "A90 gleicher Code bei Wiederholung");
            tcl.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            tcl.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
            tcl.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
            tcl.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
            r = tcl.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
            Check(r[0].Contains($"climb and maintain {ft}") && !r[0].Contains("squawk") && tcl.Squawk == code, $"A102 Höhe im Airborne wie in der Freigabe {dn}: " + r[0].Text);
        }
        {   // A102: also beyond 40 NM never below the terrain of the route (3500-m ridge between 30 and 40 NM east: 13500 + 2000 -> odd 17000)
            var ridge = new float[8, 8];
            for (int i = 0; i < 8; i++) ridge[i, 4] = 3500;
            Airfield.Map.SetTerrain(ridge, CX - 40 * NM, CZ - 10 * NM, 10 * NM);
            var tcl = new Tower("Enfield 1-1");
            tcl.Tick(parked, none, 0);
            r = tcl.OnTranscript("Kutaisi Ground, Enfield 11, request IFR clearance to Fernost", parked, none, 1);
            Airfield.Map.SetTerrain(null!, 0, 0, 0);
            Check(r[0].Contains("expect 17000 feet ten minutes"), "A102 lange Strecke über Gebirge: " + r[0].Text);
        }
        var ffQ = At(CX + 40 * NM, CZ, 1500, 0, 200);
        var twSq = new Tower("Enfield 1-1");
        twSq.Tick(parked, none, 0);
        r = twSq.OnTranscript("Kutaisi Ground, Enfield 11, request taxi", parked, none, 1);
        var sqVfr = twSq.Squawk;
        Check(sqVfr is { Length: 4 } && Sq(r[0]) == Digits(sqVfr), "A90 VFR-Rollfreigabe vergibt Code: " + r[0].Text);
        twSq.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
        twSq.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
        twSq.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
        r = twSq.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        Check(!r[0].Contains("squawk") && twSq.Squawk == sqVfr, "A90 Airborne wiederholt den Code nicht: " + r[0].Text);
        r = twSq.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffQ, none, 30);
        Check(!r[0].Contains("squawk") && twSq.Squawk == sqVfr && twSq.Following, "A90 Flight Following behält den Code: " + r[0].Text);
        r = twSq.OnTranscript("Enfield 11, cancel flight following", ffQ, none, 31);
        Check(r[0].Contains("radar service terminated, squawk VFR, frequency change approved") && twSq.Squawk == null, "A90 squawk VFR nach vergebenem Code: " + r[0].Text);
        r = twSq.OnTranscript("Enfield 11, cancel flight following", ffQ, none, 32);
        Check(r.Count == 0 || !r[0].Contains("squawk"), "A90 kein squawk VFR ohne Code: " + r.FirstOrDefault()?.Text);
        var twSfq = new Tower("Enfield 1-1");
        twSfq.Tick(ffQ, none, 0);
        r = twSfq.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffQ, none, 1);
        Check(Sq(r[0]) is { Length: > 0 } && twSfq.Squawk is { Length: 4 } sqf && Sq(r[0]) == Digits(sqf) && r.Count == 1 && !r[0].Contains("radar contact"), "A90/R128 Flight Following ohne Code: erst nur der Code: " + r[0].Text);
        var twIn = new Tower("Enfield 1-1");
        twIn.Tick(ffQ, none, 0);
        twIn.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", ffQ, none, 1);
        r = twIn.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffQ, none, 2);
        Check(r.Count == 1 && r[0].Text == "Enfield one one, roger, continue as cleared." && !twIn.Following && twIn.Phase is Phase.Inbound or Phase.Entering, "A103 FF in der Radarführung: " + r[0].Text);
        All = new List<Airfield>();

        // --- V15: flight following – traffic advisory, terrain warning via map grid, sign-off
        var ffP = At(CX + 40 * NM, CZ, 1500, 0, 200);
        var twFf = new Tower("Enfield 1-1");
        twFf.Tick(ffP, none, 0);
        r = twFf.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 1);
        Check(twFf.Following && r[0].Role == "Approach" && r[0].Contains("squawk") && !r[0].Contains("radar contact") && twFf.Tick(ffP, none, 3).Count == 0 && twFf.Tick(ffP, none, 8) is [{ } idm] && idm.Contains("radar contact") && idm.Contains("Flight following"),
              "R128 FF: Code, nach 6 s radar contact: " + r[0]);
        var par = new[] { new Traffic(31, "F-16C_50", ffP.X, ffP.Z + 4 * NM, ffP.AltMsl, 0, 200) };
        r = twFf.Tick(ffP, par, 9);
        Check(r.Count == 1 && r[0].Contains("traffic, 3 o'clock, 4 miles, northbound, same altitude, Viper"), r.FirstOrDefault()?.Text ?? "Verkehrshinweis: -");
        Check(twFf.Tick(ffP, par, 3).Count == 0, "Verkehrshinweis nur einmal");
        var twFe = new Tower("Enfield 1-1") { Side = 2, Following = true };   // A52: hostile Viper with flight following -> no traffic advisory
        twFe.Tick(ffP, none, 0);
        Check(twFe.Tick(ffP, new[] { par[0] with { Id = 32, Coalition = 1 } }, 1).Count == 0, "A52 Flight Following: kein Hinweis auf Feind");
        var hills = new float[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) hills[i, j] = (float)(ffP.AltMsl + 300);
        Airfield.Map.SetTerrain(hills, ffP.X - 15000, ffP.Z - 15000, 10000);
        r = twFf.Tick(ffP, none, 4);
        Airfield.Map.SetTerrain(null!, 0, 0, 0);
        Check(r.Count == 1 && r[0].Text.StartsWith("Low altitude alert, Enfield one one, check your altitude immediately. The M V A in your area is"), r.FirstOrDefault()?.Text ?? "Geländewarnung: -");
        // R114: terrain warning also without flight following: radar vectoring (inbound, holding) and handed-over departure; Away without handover/FF not; once per flight
        foreach (var (ph, hand, expect, holdAt) in new[] { (Phase.Inbound, false, true, 7000), (Phase.Inbound, false, false, 5000), (Phase.Away, true, true, 0), (Phase.Away, false, false, 0) })
        {
            var twLow = new Tower("Enfield 1-1");
            twLow.Tick(ffP, none, 0);
            twLow.Phase = ph; twLow.handedOff = hand; twLow.holding = holdAt > 0; twLow.holdFt = holdAt;
            Airfield.Map.SetTerrain(hills, ffP.X - 15000, ffP.Z - 15000, 10000);
            r = twLow.Tick(ffP, none, 4);
            var r2 = twLow.Tick(ffP, none, 8);
            Airfield.Map.SetTerrain(null!, 0, 0, 0);
            Check(expect ? r.Count == 1 && r[0].Text.StartsWith("Low altitude alert, Enfield one one, check your altitude immediately. The M V A in your area is") && !r2.Any(m => m.Contains("altitude alert"))
                         : !r.Any(m => m.Contains("altitude alert")), $"R114 Geländewarnung {ph}/{hand}/{holdAt}: " + string.Join(" | ", r.Select(m => m.Text)));
        }
        foreach (var call in new[] { "Kutaisi Approach, Enfield 11, traffic in sight", "Kutaisi Approach, Enfield 11, looking", "Kutaisi Approach, Enfield 11, negative contact" })
        {
            r = twFf.OnTranscript(call, ffP, none, 4.5);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, roger." && twFf.Following, $"A5 \"{call}\": " + (r.FirstOrDefault()?.Text ?? "nichts"));
        }
        // N24: replies to traffic advisories: "negative contact" -> update after 30 s; diverging -> once "no longer a factor", after "in sight" never; never radar vectoring
        {
            var twTr = new Tower("Enfield 1-1");
            twTr.Tick(ffP, none, 0);
            twTr.Squawk = "4021";   // R128: code already assigned -> radar contact right away (otherwise first "squawk", identification after 6 s, traffic advisories only after)
            twTr.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 1);
            var nearTr = new[] { new Traffic(31, "F-16C_50", ffP.X, ffP.Z + 4 * NM, ffP.AltMsl, 0, 200) };
            twTr.Tick(ffP, nearTr, 2);
            r = twTr.OnTranscript("Kutaisi Approach, Enfield 11, negative contact", ffP, nearTr, 3);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, roger." && twTr.Phase == Phase.Away, "N24 negative contact -> roger, keine Radarführung");
            Check(twTr.Tick(ffP, nearTr, 20).Count == 0, "N24 vor 30 s kein Update");
            r = twTr.Tick(ffP, nearTr, 33);
            Check(r.Count == 1 && r[0].Contains("traffic update, 3 o'clock, 4 miles"), "N24 Update nach 30 s: " + r.FirstOrDefault()?.Text);
            Check(twTr.Tick(ffP, nearTr, 34).Count == 0, "N24 Update nur einmal");
            var farTr = new[] { new Traffic(31, "F-16C_50", ffP.X, ffP.Z + 8 * NM, ffP.AltMsl, 0, 200) };
            r = twTr.Tick(ffP, farTr, 40);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, traffic no longer a factor.", "N24 auseinander: " + r.FirstOrDefault()?.Text);
            Check(twTr.Tick(ffP, farTr, 41).Count == 0, "N24 no longer a factor nur einmal");
            // reported in sight: never "no longer a factor"
            var twTrS = new Tower("Enfield 1-1");
            twTrS.Tick(ffP, none, 0);
            twTrS.Squawk = "4021";   // R128: code already assigned -> radar contact right away (otherwise first "squawk", identification after 6 s, traffic advisories only after)
            twTrS.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 1);
            twTrS.Tick(ffP, nearTr, 2);
            r = twTrS.OnTranscript("Kutaisi Approach, Enfield 11, traffic in sight", ffP, nearTr, 3);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, roger.", "N24 in sight -> roger");
            Check(twTrS.Tick(ffP, farTr, 40).Count == 0, "N24 nach in sight kein no longer a factor");
            // Advisory from a terminated service: after a new "request flight following" no "no longer a factor" for the old traffic
            var twTrO = new Tower("Enfield 1-1");
            twTrO.Tick(ffP, none, 0);
            twTrO.Squawk = "4021";
            twTrO.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 1);
            Check(twTrO.Tick(ffP, nearTr, 2) is [{ } adv] && adv.Contains("traffic, 3 o'clock"), "N24 alter Dienst: Hinweis");
            twTrO.OnTranscript("Kutaisi Approach, Enfield 11, cancel flight following", ffP, none, 5);
            twTrO.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 300);
            var oldTr = new[] { 302, 310, 320 }.SelectMany(s => twTrO.Tick(ffP, none, s)).Select(m => m.Text).ToList();
            Check(twTrO.Following && !oldTr.Any(x => x.Contains("no longer a factor")), "N24 alter Dienst: kein no longer a factor: " + string.Join(" | ", oldTr));
        }
        r = twFf.OnTranscript("Enfield 11, cancel flight following", ffP, none, 5);
        Check(!twFf.Following && r[0].Contains("radar service terminated"), r[0]);
        twFf.Following = true;
        r = twFf.OnTranscript("Kutaisi Approach, Enfield 11, leaving frequency", ffP, none, 6);
        Check(!twFf.Following && r.Count == 1 && r[0].Contains("radar service terminated, frequency change approved"), "A5 leaving frequency: " + (r.FirstOrDefault()?.Text ?? "nichts"));

        // A106: terrain warning once per flight, "terrain in sight" acknowledged (and suppressed), none near the airfield; A107: confirm with affirm/negative; A108: radio check
        {
            Airfield.Map.SetTerrain(hills, ffP.X - 15000, ffP.Z - 15000, 10000);
            var twLoA = new Tower("Enfield 1-1");
            twLoA.Tick(ffP, none, 0); twLoA.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 1);
            Check(twLoA.Tick(ffP, none, 4).Count == 0 && twLoA.Tick(ffP, none, 8).Count == 1 && twLoA.Tick(ffP, none, 9).Count == 1 && twLoA.Tick(ffP, none, 70).Count == 0, "A106: Geländewarnung nur einmal, erst nach radar contact (R128)");
            var twTis = new Tower("Enfield 1-1");
            twTis.Tick(ffP, none, 0); twTis.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", ffP, none, 1);
            twTis.Tick(ffP, none, 8);
            r = twTis.OnTranscript("Enfield 11, terrain in sight", ffP, none, 9);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, roger." && twTis.Tick(ffP, none, 10).Count == 0, "A106: terrain in sight -> roger, keine Warnung");
            var near = At(CX + 3 * NM, CZ, 1500, 0, 200);
            var twNe = new Tower("Enfield 1-1");
            twNe.Tick(near, none, 0); twNe.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", near, none, 1);
            Check(twNe.Tick(near, none, 8).Count == 1 && twNe.Tick(near, none, 9).Count == 0, "A106: nahe am Platz keine Geländewarnung");
            // R215: once per warning situation: 10 s safely above terrain + 1000 ft (terrain here 300 m above him), then low again -> new warning, also after "terrain in sight"
            var safe = ffP with { AltMsl = ffP.AltMsl + 650 };
            var again = new[] { (twLoA, 80), (twTis, 20) }.Select(x => (x.Item1.Tick(safe, none, x.Item2).Count, x.Item1.Tick(safe, none, x.Item2 + 5).Count, x.Item1.Tick(safe, none, x.Item2 + 11).Count, x.Item1.Tick(ffP, none, x.Item2 + 12))).ToList();
            Check(again.All(x => x.Item1 + x.Item2 + x.Item3 == 0 && x.Item4 is [{ } lw] && lw.Text.StartsWith("Low altitude alert, Enfield one one")), "R215 Geländewarnung je Warnlage: " + string.Join(" | ", again.Select(x => x.Item4.FirstOrDefault()?.Text ?? "-")));
            Airfield.Map.SetTerrain(null!, 0, 0, 0);
            var twCfm = new Tower("Enfield 1-1");
            twCfm.Tick(ffP, none, 0);
            r = twCfm.OnTranscript("Kutaisi Approach, Enfield 11, request heading 270", ffP, none, 1);
            var saidA = r[0].Text;
            r = twCfm.OnTranscript("Enfield 11, confirm heading 270", ffP, none, 2);
            Check(r.Count == 1 && r[0].Contains("affirm, heading two seven zero"), "A107 affirm: " + saidA + " | " + r.FirstOrDefault()?.Text);
            r = twCfm.OnTranscript("Enfield 11, confirm heading 260", ffP, none, 3);
            Check(r.Count == 1 && r[0].Contains("negative, heading two seven zero"), "A107 negative: " + r.FirstOrDefault()?.Text);
            r = twCfm.OnTranscript("Enfield 11, confirm", ffP, none, 4);
            Check(r.Count == 1 && r[0].Text == saidA, "A107: ohne Wert der ganze Spruch");
            twCfm.last = new Msg("Approach", "Enfield one one, turn left heading two seven zero, descend and maintain 3000 feet.");
            r = twCfm.OnTranscript("Enfield 11, confirm heading 270, 4000 feet", ffP, none, 4.5);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, negative, heading two seven zero, 3000 feet.", "A107: Kurs und Höhe verglichen: " + r.FirstOrDefault()?.Text);
            var luw = new Msg("Tower", "Enfield one one, runway two five, line up and wait, traffic on the runway.");
            twCfm.last = luw;
            r = twCfm.OnTranscript("Kutaisi Tower, Enfield 11, confirm cleared for takeoff runway 25", ffP, none, 4.7);
            Check(r.Count == 1 && r[0].Text == luw.Text, "A107: Freigabe-Frage -> ganzer Spruch, kein affirm: " + r.FirstOrDefault()?.Text);
            r = twCfm.OnTranscript("Kutaisi Approach, Enfield 11, radio check", ffP, none, 5);
            Check(r.Count == 1 && r[0].Contains("read you five") && !r[0].Contains("by five"), "A108: " + r.FirstOrDefault()?.Text);
        }
        // --- Departure: flight following counts as check-in (A51), Approach identifies without check-in after 60 s (A105), "request direct" with destination (R41)
        var senK = new Airfield { Name = "Senaki-Kolkhi", X = -281000, Z = 647000 };
        All = new List<Airfield> { K, new() { Name = "Batumi", X = -356000, Z = 618000 }, senK };
        Tower Dep(string? clr = null, Airfield? af = null)   // like twD/twCl: taken off, handed over from Tower to Approach (t = 11 s); af: airfield with own frequencies
        {
            var x = af == null ? new Tower("Enfield 1-1") : new Tower(af, "Enfield 1-1");
            x.Tick(parked, none, 0);
            if (clr != null) x.OnTranscript($"Kutaisi Ground, Enfield 11, request IFR clearance to {clr}", parked, none, 1);
            x.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            x.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
            x.Tick(At(dx, dz, 120, LandHdg("25"), 180), none, 10);
            x.Tick(At(dx, dz, 250, LandHdg("25"), 200), none, 11);
            return x;
        }
        var dpA = Dep();
        r = dpA.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        Check(dpA.Following && dpA.Phase == Phase.Away && r[0].Contains("radar contact"), "Departing + Following: Check-in -> Away: " + dpA.Phase);
        Check(dpA.Tick(At(CX - 9 * NM, CZ, 4000, 270, 300), none, 21).Count == 0 && dpA.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 22).Count == 0,
              "Following: keine Abmeldung am Rand der Zone und bei 15 NM");
        r = dpA.OnTranscript("Enfield 11, cancel flight following", At(CX - 16 * NM, CZ, 4000, 270, 300), none, 23);
        Check(r.Count == 1 && r[0].Contains("radar service terminated, squawk VFR, frequency change approved") && dpA.Tick(At(CX - 17 * NM, CZ, 4000, 270, 300), none, 24).Count == 0,
              "Following beendet: genau eine Abmeldung: " + (r.FirstOrDefault()?.Text ?? "-"));
        var dpB = Dep();
        dpB.Following = true;   // Following came along from another airfield: no "leaving control zone" (Kutaisi zone 9.1 NM, A100)
        Check(dpB.Tick(At(CX - 10 * NM, CZ, 3000, 270, 300), none, 12).Count == 0 && dpB.Phase == Phase.Away, "Departing mit Following: still Away");
        // A100: one control zone per airfield (altitude check and sign-off at the same boundary): Kutaisi farthest CRP + 0.5 NM, otherwise 5 NM
        var gf5 = Airfield.FromDcs("Test-Field", 0, 0, 10, new[] { ("13", -131 * Math.PI / 180, 0.0, 0.0, 2400.0) });
        Check(Math.Abs(K.CtrNm - 9.1) < 0.2 && gf5.CtrNm == 5 && Math.Abs(gf5.CtrTopFt - (10 / Ft + 3000)) < 1, $"A100: Zone Kutaisi {K.CtrNm:0.0} NM, sonst {gf5.CtrNm} NM");
        var dpZ = Dep();
        dpZ.handedAt = -1;   // Handover time unknown (restored session): no A105, which would otherwise identify first at the zone edge
        r = dpZ.Tick(At(CX - 7 * NM, CZ, 1000, 180, 150), none, 20);   // 7 NM too high in the zone: no altitude admonition after handover (forum log 0.9.4)
        Check(!r.Any(m => m.Contains("check altitude")), "A100: keine Höhenmahnung nach der Übergabe: " + (r.FirstOrDefault()?.Text ?? "-"));
        {   // R7: IFR departure: altitude admonition against the cleared altitude (3000), not against 2200 ft
            var dpI = Dep("Batumi");
            var i1 = dpI.Tick(At(CX - 4 * NM, CZ, 2800 * Ft - FieldElev, 250, 200), none, 20);
            var i2 = dpI.Tick(At(CX - 4.5 * NM, CZ, 3600 * Ft - FieldElev, 250, 200), none, 25);
            Check(i1.Count == 0 && i2.Count == 1 && i2[0].Role == "Approach" && i2[0].Text == "Enfield one one, check altitude, maintain 3000 feet.", "R7 IFR-Höhenmahnung: " + string.Join(" | ", i1.Concat(i2)));
        }
        r = dpZ.Tick(At(CX - 8.5 * NM, CZ, 500, 180, 150), none, 21);
        Check(!r.Any(m => m.Contains("leaving")) && dpZ.Phase == Phase.Departing, "A100: 8,5 NM noch in der Zone (früher Abmeldung ab 8 NM): " + (r.FirstOrDefault()?.Text ?? "-"));
        r = dpZ.Tick(At(CX - 9.5 * NM, CZ, 500, 180, 150), none, 22);
        Check(r.Count == 1 && r[0].Contains("leaving control zone") && dpZ.Phase == Phase.Away, "A100: Abmeldung am Zonenrand: " + (r.FirstOrDefault()?.Text ?? "-"));
        // Reporting obligation #8 (instead of A105): no identification by itself without check-in; after 60 s Tower reminds, 60 s later no radar service
        // (VFR "radar service not provided, squawk VFR", IFR violation); a late check-in still gets it
        foreach (var dIfr in new[] { false, true })
        {
            var dpC = dIfr ? Dep("Batumi") : Dep();
            var cAt = At(dx, dz, 250, LandHdg("25"), 200);
            for (int s = 15; s <= 70; s += 5) if (dpC.Tick(cAt, none, s).Count > 0) Check(false, "#8: zu früh gemeldet bei " + s);
            var c1 = dpC.Tick(cAt, none, 72);
            var c2 = dpC.Tick(cAt, none, 120);
            var c3 = dpC.Tick(cAt, none, 133);
            var c4 = dpC.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 138);
            Check(c1.Count == 1 && c1[0].Role == "Tower" && c1[0].Text == "Enfield one one, contact Kutaisi Approach now, two six six decimal five." && !c1[0].Contains("radar contact") && c2.Count == 0 &&
                  c3.Count == (dIfr ? 2 : 1) && c3[0].Role == "Tower" && c3[0].Text.StartsWith("Enfield one one, no contact with Kutaisi Approach, radar service not provided") &&
                  (dIfr ? !c3[0].Contains("deviation") && c3[1].Text.StartsWith(L("Verstoß: kein Check-in bei Departure", "Deviation: no check-in with departure")) : c3[0].Text.EndsWith("radar service not provided, squawk VFR, frequency change approved.")) &&
                  dpC.Phase == Phase.Away && !dpC.handedOff && c4.Count == 0, $"#8 Departure ohne Check-in ({(dIfr ? "IFR" : "VFR")}): " + string.Join(" | ", c1.Concat(c2).Concat(c3).Concat(c4)));
            // Review l2: IFR notice "possible pilot deviation" names the place of the violation (Departure = Kutaisi Approach), spoken by Tower (pilot never checked in with Approach)
            var c4n = dpC.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 143);
            Check(dIfr ? c4n.Count == 1 && c4n[0].Role == "Tower" && c4n[0].Text.StartsWith("Enfield one one, possible pilot deviation, advise you contact Kutaisi Approach at D S N three one four, ") : c4n.Count == 0,
                  $"Review l2 #8 Hinweis ({(dIfr ? "IFR" : "VFR")}): " + string.Join(" | ", c4n.Select(m => m.Role + ": " + m.Text)));
            var c5 = dpC.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 4000", cAt, none, 145);
            if (c5.Count == 1 && c5[0].Contains("squawk") && !c5[0].Contains("radar contact")) c5 = dpC.Tick(cAt, none, 151);   // R128: code gone (squawk VFR) -> first new code, then radar contact
            Check(c5.Count == 1 && c5[0].Role == "Approach" && c5[0].Contains("radar contact"), "#8 später Check-in: " + string.Join(" | ", c5));
        }
        {   // #8 with own Departure frequency (frequencies.jsonc): reminder "contact Departure now" with its frequency, consequence names Departure
            var kN = Airfield.Kutaisi();
            kN.Own["Departure"] = new[] { 273.55 };
            var dpN = Dep(null, kN);
            var nAt = At(dx, dz, 250, LandHdg("25"), 200);
            var n1 = dpN.Tick(nAt, none, 72);
            var n3 = dpN.Tick(nAt, none, 133);
            Check(n1.Count == 1 && n1[0].Role == "Tower" && n1[0].Text == "Enfield one one, contact Kutaisi Departure now, two seven three decimal five five." &&
                  n3.Count == 1 && n3[0].Text.StartsWith("Enfield one one, no contact with Kutaisi Departure, radar service not provided"),
                  "#8 eigene Departure-Frequenz: " + string.Join(" | ", n1.Concat(n3)));
            // Review l2 with own Departure frequency (IFR): the notice names Departure as the place of the violation
            var dpNi = Dep("Batumi", kN);
            dpNi.Tick(nAt, none, 72); dpNi.Tick(nAt, none, 133); dpNi.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 138);
            var n4 = dpNi.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 143);
            Check(n4.Count == 1 && n4[0].Role == "Tower" && n4[0].Text.StartsWith("Enfield one one, possible pilot deviation, advise you contact Kutaisi Departure at D S N three one four, "),
                  "Review l2 #8 Hinweis eigene Departure-Frequenz: " + string.Join(" | ", n4.Select(m => m.Role + ": " + m.Text)));
        }
        {   // R7 · #11: at the departure CRP (in the zone, before the 60 s) without check-in: Tower reminds, no "radar contact" without check-in, no "leaving control zone"
            var dpX = Dep();
            dpX.Tick(At((dx + Crp["west"].X) / 2, (dz + Crp["west"].Z) / 2, 500, 270, 200), none, 20);   // Gap under 10 s: no new flight
            var rx = dpX.Tick(At(Crp["west"].X, Crp["west"].Z, 500, 270, 200), none, 29);
            Check(rx.Count == 1 && rx[0].Role == "Tower" && rx[0].Contains("contact Kutaisi Approach now") && !rx[0].Contains("radar contact") && !rx[0].Contains("leaving") && dpX.Phase == Phase.Departing,
                  "#11 am Ausflug-CRP ohne Check-in: " + (rx.FirstOrDefault()?.Text ?? "-"));
        }
        var dpD = Dep();
        dpD.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        r = dpD.OnTranscript("Kutaisi Approach, Enfield 11, request direct Batumi", At(dx, dz, 900, 215, 250), none, 25);
        Check(r.Count == 1 && r[0].Contains("cleared direct Batumi"), "Direct nach dem Abflug: genannter Platz: " + (r.FirstOrDefault()?.Text ?? "-"));
        var dpE = Dep("Batumi");
        dpE.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        r = dpE.OnTranscript("Kutaisi Approach, Enfield 11, request direct", At(dx, dz, 900, 215, 250), none, 25);
        Check(r.Count == 1 && r[0].Contains("cleared direct Batumi"), "Direct nach dem Abflug: Ziel der Streckenfreigabe: " + (r.FirstOrDefault()?.Text ?? "-"));
        // N40: IFR destination captured en route -> once "say alternate destination", reply with airfield -> new destination, handover there (R43)
        var dpN40 = Dep("Batumi");
        dpN40.Side = 2;
        dpN40.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        All[1].Side = 1;
        r = dpN40.Tick(At(dx, dz, 900, 215, 250), none, 26);
        var rN2 = dpN40.Tick(At(dx, dz, 900, 215, 250), none, 27);
        var destGone = dpN40.dest == null;
        var altN = dpN40.OnTranscript("Kutaisi Approach, Enfield 11, request Senaki", At(dx, dz, 900, 215, 250), none, 30);
        All[1].Side = 0;
        Check(r.Count == 1 && r[0].Text == "Enfield one one, Batumi is closed, say alternate destination." && rN2.Count == 0 && destGone &&
              altN.Count == 1 && altN[0].Contains("cleared to Senaki Kolkhi airport, fly heading") && dpN40.dest == senK && dpN40.IfrHandoff()?.Contains("Senaki Kolkhi Approach") == true,
              "N40 IFR-Ziel erobert: " + (r.FirstOrDefault()?.Text ?? "-") + " / " + (altN.FirstOrDefault()?.Text ?? "-"));
        {   // R218: IFR route with own heading ("request heading", departure vector gone): at 15 NM handover to destination Approach, no "radar service terminated"
            var dp218 = Dep("Batumi");
            dp218.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
            var hh218 = dp218.OnTranscript("Kutaisi Approach, Enfield 11, request heading 270", At(dx, dz, 900, 250, 250), none, 21);
            var ho218 = dp218.Tick(At(CX - 16 * NM, CZ, 4000, 270, 300), none, 22);
            Check(hh218.Count == 1 && hh218[0].Contains("fly heading two seven zero, approved") && ho218.Count == 1 && ho218[0].Role == "Approach" && ho218[0].Text.StartsWith("Enfield one one, contact Batumi Approach") &&
                  !ho218[0].Contains("radar service terminated") && dp218.Released, "R218 IFR-Strecke mit eigenem Kurs: " + string.Join(" | ", hh218.Concat(ho218)));
        }
        // A13: approach aborted without sign-off, he flies away: from the zone once "say intentions" (afterwards no heading), signed off 3 NM further
        foreach (var (aph, what) in new[] { (Phase.Entering, "approach"), (Phase.ClearedLand, "landing clearance") })
        {
            var twA13 = new Tower(K, "Enfield 1-1");
            double ox = K.End("25").Dz, oz = -K.End("25").Dx, ohdg = Math.Atan2(oz, ox) * 180 / Math.PI;   // away from the airfield on the pattern side
            Telemetry Out(double nm) => At(CX + ox * nm * NM, CZ + oz * nm * NM, 600, ohdg, 200);
            var ip = kTower.OnCenterline("25", InitialDist);
            twA13.Load(new State(aph, "25", null, false, false, 0, new[] { ip.X, ip.Z }, "initial", false, true, false, false, null, 0, 0, null, true, false, null, null, 0, true), Out(2), 0);   // Flight following from en-route flight
            var saidA13 = new List<string>();
            for (int s = 1; s <= 40; s++) saidA13.AddRange(twA13.Tick(Out(2 + s * 0.5), none, s * 5).Select(m => m.Text));
            int si = saidA13.FindIndex(x => x.Contains("I show you leaving the control zone, say intentions"));
            Check(si >= 0 && saidA13.Count(x => x.Contains("say intentions")) == 1 && !saidA13.Skip(si).Any(x => x.Contains("fly heading")) &&
                  saidA13.Count(x => x == $"Enfield one one, {what} cancelled, frequency change approved.") == 1 && twA13.Phase == Phase.Away && twA13.Released && !twA13.Following,
                  $"A13 {aph}: weggeflogen: " + string.Join(" | ", saidA13));
        }
        {   // A13: reply to "say intentions" -> no sign-off 3 NM further (measured again)
            var twAn = new Tower(K, "Enfield 1-1");
            double a13x = K.End("25").Dz, a13z = -K.End("25").Dx, ahdg = Math.Atan2(a13z, a13x) * 180 / Math.PI;
            Telemetry AOut(double nm) => At(CX + a13x * nm * NM, CZ + a13z * nm * NM, 600, ahdg, 200);
            var aip = kTower.OnCenterline("25", InitialDist);
            twAn.Load(new State(Phase.Entering, "25", null, false, false, 0, new[] { aip.X, aip.Z }, "initial", false, false, false, false, null, 0, 0, null, true, false, null, null, 0, true), AOut(2), 0);
            double anm = 2; int k = 0;
            var saidAn = new List<string>();
            while (!saidAn.Any(x => x.Contains("say intentions")) && anm < 20) { anm += 0.5; saidAn.AddRange(twAn.Tick(AOut(anm), none, ++k * 5).Select(m => m.Text)); }
            var ans = twAn.OnTranscript("Kutaisi Tower, Enfield 11, returning to initial", AOut(anm), none, ++k * 5);
            saidAn.Clear();
            for (int s = 1; s <= 7; s++) saidAn.AddRange(twAn.Tick(AOut(anm + s * 0.5), none, (k + s) * 5).Select(m => m.Text));
            Check(anm < 20 && ans.Count == 1 && !saidAn.Any(x => x.Contains("cancelled")) && twAn.Phase == Phase.Entering, $"A13 Antwort auf say intentions: {ans.FirstOrDefault()?.Text} / " + string.Join(" | ", saidAn));
        }
        var dpF = Dep("Batumi");   // IFR departure vector, flight following, end: like the 15-NM sign-off, no more headings afterwards
        dpF.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        dpF.OnTranscript("Kutaisi Approach, Enfield 11, request flight following", At(dx, dz, 900, 215, 250), none, 25);
        r = dpF.OnTranscript("Enfield 11, cancel flight following", At(CX - 5 * NM, CZ, 4000, 90, 300), none, 30);
        Check(r.Count == 1 && r[0].Contains(", contact Batumi Approach ") && !r[0].Contains("radar service terminated") && dpF.nav == null && dpF.Tick(At(CX - 6 * NM, CZ, 4000, 90, 300), none, 90).Count == 0,
              "Following-Ende nach IFR-Abflugvektor: " + (r.FirstOrDefault()?.Text ?? "-"));
        var dpAw = Dep("Batumi");   // R43: with mission AWACS "radar service terminated, contact Overlord" stays (no ATC unit)
        dpAw.AwacsContact = "Overlord two five one decimal zero";
        dpAw.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        dpAw.Tick(At(dx, dz, 900, 215, 250), none, 25);   // Departure vector to Batumi
        r = dpAw.Tick(At(CX - 12 * NM, CZ - 11 * NM, 4000, 215, 250), none, 30);
        Check(r.Count == 1 && r[0].Contains("Radar service terminated, contact Overlord") && !r[0].Contains("Batumi Approach"), "R43 AWACS statt Platz-Übergabe: " + (r.FirstOrDefault()?.Text ?? "-"));
        var dpG = Dep("Batumi");   // "request direct" before check-in counts as check-in, no "radar contact ... fly heading" after 60 s
        r = dpG.OnTranscript("Kutaisi Approach, Enfield 11, request direct Senaki", At(dx, dz, 900, 215, 250), none, 20);
        Check(r[0].Contains("cleared direct Senaki") && dpG.Phase == Phase.Away && dpG.Tick(At(dx, dz, 900, 215, 250), none, 75).Count == 0, "A105 nach request direct: " + dpG.Phase);
        var dpH = Dep("Batumi");   // R41: direct replaces the departure vector
        dpH.OnTranscript("Kutaisi Approach, Enfield 11, airborne, passing 2000", At(dx, dz, 600, LandHdg("25"), 250), none, 20);
        dpH.OnTranscript("Kutaisi Approach, Enfield 11, request direct Senaki", At(dx, dz, 900, 215, 250), none, 25);
        var navH = dpH.nav;
        r = dpH.Tick(At(CX - 16 * NM, CZ, 6000, 270, 300), none, 26);
        Check(navH == (senK.X, senK.Z) && r.Count == 1 && r[0].Contains(", contact Senaki Kolkhi Approach "), "Direct nach IFR-Abflugvektor: " + (r.FirstOrDefault()?.Text ?? "-"));
        All = new List<Airfield>();

        // --- Radar vectoring like BMS: pilot flies the headings/altitudes, lands aligned on the centerline (four directions)
        foreach (var (sx, sz, name) in new[] { (-291000.0, 660000.0, "West"), (CX + 15 * NM, CZ, "Ost"), (CX, CZ + 20 * NM, "Nord"), (CX - 5 * NM, CZ - 25 * NM, "Süd") })
        {
            var tv = new Tower("Enfield 1-1");
            double x = sx, z = sz, alt = 15000 * Ft + FieldElev, h = 90, steps = 0, minFt = 99999;
            tv.Tick(At(x, z, alt - FieldElev, h, 140), none, 0);
            var rr = tv.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", At(x, z, alt - FieldElev, h, 140), none, 1);
            double target = tv.vecHdg, says = 1; steps = rr.Any(m => m.Text.Contains("descend")) ? 1 : 0;   // R323: first contact can already give the handover altitude
            int sec = 2;
            for (; sec < 2400 && tv.Phase == Phase.Inbound; sec++)
            {
                if (tv.vec is { Count: 0 } && tv.AL(x, z, "25") is var (pa, pl) && Math.Abs(pl) < NM) { var q = tv.Pt("25", pa - 1.5 * NM, 0); target = Bearing(x, z, q.X, q.Z); }   // Pilot flies onto the centerline himself
                double d = ((target - h) % 360 + 540) % 360 - 180;   // turn 3°/s, 140 m/s, descend 15 m/s
                h = (h + Math.Clamp(d, -3, 3) + 360) % 360;
                x += 140 * Math.Cos(h * Math.PI / 180); z += 140 * Math.Sin(h * Math.PI / 180);
                double alt0 = alt;
                alt = Math.Max(alt - 15, Math.Min(alt, tv.vecFt * Ft));
                minFt = Math.Min(minFt, tv.vecFt);
                var m = tv.Tick(At(x, z, alt - FieldElev, h, 140) with { Vs = alt - alt0 }, none, sec);
                if (m.Count > 0) { says++; target = tv.vecHdg; if (m[0].Text.Contains("descend")) steps++; if (Environment.GetEnvironmentVariable("VECDBG") == name) { var (da, dl) = tv.AL(x, z, "25"); Console.WriteLine($"  {sec}s a={da / NM:0.0} l={dl / NM:0.0} h={h:0} alt={(alt - FieldElev) / Ft:0}: {m[0].Text}"); } }
            }
            for (int k = 0; k < 25; k++)   // Handover while turning in: pilot turns onto the centerline himself
            {
                var (pa, _) = tv.AL(x, z, "25");
                var q = tv.Pt("25", pa - 1.5 * NM, 0);
                h = (h + Math.Clamp(((Bearing(x, z, q.X, q.Z) - h) % 360 + 540) % 360 - 180, -3, 3) + 360) % 360;
                x += 140 * Math.Cos(h * Math.PI / 180); z += 140 * Math.Sin(h * Math.PI / 180);
            }
            var (fa, fl) = tv.AL(x, z, "25");
            if (Environment.GetEnvironmentVariable("VECDBG") == name) foreach (var ph in new[] { "Kutaisi Tower Enfield 11 with you", "Kutaisi Tower Enfield 11 inbound", "Kutaisi Tower Enfield 11 initial" }) Console.WriteLine("  TWR: " + string.Join(" | ", tv.OnTranscript(ph, At(x, z, alt - FieldElev, h, 140), none, sec++).Select(m => m.Role + ": " + m.Text)));
            Check(tv.Phase == Phase.Entering && Math.Abs(fl) < 0.5 * NM && fa > InitialDist && steps >= 1 && minFt >= tv.PatternFt && alt / Ft <= tv.PatternFt + 2000,
                  $"Radarführung von {name}: {sec} s, {says} Funksprüche, {steps} Sinkflugstufen, Übergabe {alt / Ft:0} ft, seitlich {fl:0} m, {fa / NM:0.0} NM vor der Schwelle");
        }

        // --- V16 wake turbulence: heavy took off before me -> warning; C-130 or heavy behind me on final -> none
        var (wx, wz) = P("25", -2000);
        var (bx, bz) = P("25", 3 * NM);
        Traffic Hv(string ty, double x, double z) => new(8, ty, x, z, FieldElev + 150, 0, 90);
        Check(kTower.Wake(hold, new[] { Hv("KC-135", wx, wz) }, "25") != "" && kTower.Wake(hold, new[] { Hv("C-130", wx, wz) }, "25") == "" &&
              kTower.Wake(hold, new[] { Hv("KC-135", bx, bz) }, "25") == "", "V16 Wirbelschleppen");

        // --- Takeoff clearance only after landing traffic
        var (fx, fz) = P("25", 2.5 * NM);
        var landing = new Traffic(7, "C-130", fx, fz, FieldElev + 200, (Math.Atan2(Uz, Ux) * 180 / Math.PI + 180) * Math.PI / 180, 70);
        var tw2 = new Tower("Enfield 1-1");
        tw2.Tick(parked, none, 0);
        r = tw2.OnTranscript("Enfield 11 request takeoff", parked, none, 0.5);
        Check(r[0].Contains("negative, you are not cleared to taxi") && r[0].Contains("Contact Kutaisi Ground two six four decimal five"), "Start aus der Parkposition: " + r[0]);
        tw2.OnTranscript("Enfield 11 request taxi", parked, none, 0.7);
        r = tw2.OnTranscript("Enfield 11 ready for departure", hold, new[] { landing }, 1);
        Check(tw2.Phase == Phase.HoldShort && r[0].Contains("hold short") && r[0].Contains("C 130 on"), r[0]);
        r = tw2.Tick(hold, none, 2);
        Check(tw2.Phase == Phase.ClearedTakeoff && r[0].Contains("cleared for takeoff"), "Freigabe nach Landung: " + (r.FirstOrDefault()?.Text ?? "-"));

        // --- A38 runway entry names the runway hit: crossing runway 16/34 perpendicular to 25, 700 m lateral of the runway center
        {
            var kx = Airfield.Kutaisi();
            double h16 = Math.Atan2(Ux, -Uz) * 180 / Math.PI, qx = CX - 700 * Uz, qz = CZ + 700 * Ux, qdx = -Uz, qdz = Ux;
            kx.Ends.Add(new("16", qx - 500 * qdx, qz - 500 * qdz, (h16 + 360) % 360, 1000, qx, qz));
            kx.Ends.Add(new("34", qx + 500 * qdx, qz + 500 * qdz, (h16 + 180) % 360, 1000, qx, qz));
            var twX = new Tower(kx, "Enfield 1-1");
            twX.Tick(parked, none, 0);
            twX.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 1);
            r = twX.Tick(At(qx, qz, 0, h16, 5), none, 100);
            Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("not cleared onto runway one six"), "A38 Querbahn genannt: " + (r.FirstOrDefault()?.Text ?? "-"));
            r = twX.Tick(At(qx, qz, 0, h16 + 180, 5), none, 200);
            Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("not cleared onto runway three four"), "A38 Querbahn Gegenrichtung: " + (r.FirstOrDefault()?.Text ?? "-"));
            var (a38x, a38z) = P("25", -300);
            r = twX.Tick(At(a38x, a38z, 0, LandHdg("25"), 5), none, 300);
            Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("not cleared onto runway two five"), "A38 aktive Bahn bleibt: " + (r.FirstOrDefault()?.Text ?? "-"));
        }

        // --- Approach from the west with runway 25 -> not via west (only for 07)
        var west = At(-291000, 660000, 600, 70, 120);
        var tw3 = new Tower("Enfield 1-1");
        tw3.Tick(west, none, 0);
        r = tw3.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        Check(tw3.Phase == Phase.Inbound && r[0].Contains("heading") && r[0].Contains("downwind"), "Radarführung von Westen: " + r[0]);

        // --- Terrain: 1500 m (4921 ft) with valleys down to 0 m every 6 NM (mountains: +2000 ft) -> minimum altitude 7000 ft for radar vectoring and holding
        var mtn = Airfield.Kutaisi();
        var g1500 = new float[61, 61];
        for (int i = 0; i < 61; i++) for (int j = 0; j < 61; j++) g1500[i, j] = i % 6 == 0 || j % 6 == 0 ? 0 : 1500;
        mtn.SetTerrain(g1500, mtn.X - 30 * NM, mtn.Z - 30 * NM, NM);
        var twT = new Tower(mtn, "Enfield 1-1");
        twT.Tick(west, none, 0);
        r = twT.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        Check(r[0].Contains("climb and maintain 7000 feet"), "Gelände Radarführung: " + r[0]);
        var twT2 = new Tower(mtn, "Enfield 1-1") { QueueAhead = 2, Spaced = false };
        twT2.Tick(west, none, 0);
        r = twT2.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        Check(r[0].Contains("7000 feet"), "Gelände Warteschleife: " + r[0]);
        Check(mtn.MvaFt(west.X, west.Z) == 7000, "MVA Gebirge (4921 ft) +2000: " + mtn.MvaFt(west.X, west.Z));
        for (int i = 0; i < 61; i++) for (int j = 0; j < 61; j++) g1500[i, j] = 1500;
        Check(mtn.MvaFt(west.X, west.Z) == 6000, "MVA flache Hochebene (4921 ft) +1000: " + mtn.MvaFt(west.X, west.Z));
        for (int i = 0; i < 61; i++) for (int j = 0; j < 61; j++) g1500[i, j] = 800;
        Check(mtn.MvaFt(west.X, west.Z) == 3700, "MVA Flachland (2625 ft) +1000: " + mtn.MvaFt(west.X, west.Z));
        // Khasab (fjord, Gulf): pattern on both sides in terrain (700 m from 0.5 NM beside the runway) -> VFR straight-in approach instead of overhead; flat valley: overhead
        var khs = Airfield.FromDcs("Khasab", 0, 0, 14.5, new[] { ("19", -Math.PI, 0.0, 0.0, 2290.0) });
        var fj = new float[61, 61];
        for (int i = 0; i < 61; i++) for (int j = 0; j < 61; j++) fj[i, j] = j == 30 ? 0 : 700;
        khs.SetTerrain(fj, -30 * NM, -30.5 * NM, NM);
        var khN = new Telemetry(3000, 3000, 150, Math.PI, 20 * NM, 0, 0, 0, 0);
        bool KhStraight() { var tk = new Tower(khs, "Enfield 1-1"); tk.Tick(khN, none, 0); tk.OnTranscript("Khasab Approach, Enfield 1-1, inbound for landing", khN, none, 1); return tk.wantStraight; }
        var (khOk, khSt) = (khs.PatternOk("19"), KhStraight());
        for (int i = 0; i < 61; i++) for (int j = 0; j < 61; j++) fj[i, j] = 0;
        Check(!khOk && khSt && khs.PatternOk("19") && !KhStraight(), $"Platzrunde im Gelände (Khasab) -> Geradeausanflug: {khOk} {khSt}");

        // --- Sign-off and ejection (heading not followed three times)
        var twC = new Tower("Enfield 1-1");
        twC.Tick(west, none, 0);
        twC.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        r = twC.OnTranscript("Kutaisi Approach Enfield 11 cancel approach", west, none, 2);
        Check(twC.Phase == Phase.Away && r[0].Contains("approach cancelled"), "Abmelden: " + r[0]);
        var twK = new Tower("Enfield 1-1");
        twK.Tick(west, none, 0);
        twK.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);   // Heading ~080, stubbornly flies 250
        var stubborn = At(-291000, 660000, 600, 250, 120);
        string? kicked = null;
        var kmsg = new List<string>();
        double kickT = 0;
        for (double tt = 2; tt < 600 && kicked == null; tt += 1)
            foreach (var m in twK.Tick(stubborn, none, kickT = tt)) { kmsg.Add(m.Text); if (m.Text.Contains("removed from the sequence")) kicked = $"{tt:0} s: {m.Text}"; }
        var kNote = Enumerable.Range(1, 15).SelectMany(s => twK.Tick(stubborn, none, kickT + s)).Select(m => m.Text).ToList();   // R247: notice separate, 10 s later
        Check(kicked != null && twK.Phase == Phase.Away && !kicked.Contains("deviation") && kmsg.Any(m => m.StartsWith(L("Verstoß: aus der Reihenfolge entfernt", "Deviation: removed from the sequence"))) &&
              kNote.Count == 1 && kNote[0].StartsWith("Enfield one one, possible pilot deviation, advise you contact Kutaisi Tower at D S N"), "Rauswurf mit Verstoß (N4, R247): " + (kicked ?? "-") + " / " + string.Join(" | ", kNote));
        // the steps before: ask back, re-vector, "say intentions", not thrown out immediately
        int iv = kmsg.FindIndex(m => m.Contains("verify heading")), ii = kmsg.FindIndex(m => m.Contains("say intentions")), ik = kmsg.FindIndex(m => m.Contains("removed from the sequence"));
        Check(iv >= 0 && iv < ii && ii < ik && kmsg.Skip(iv + 1).Take(ii - iv - 1).Any(m => m.Contains("heading")), "Rauswurf Stufen: " + string.Join(" | ", kmsg));
        // Climb ignored: step 2 re-instructs the altitude ("immediately"), does not adopt his too-low altitude; he still flies out
        var twKA = new Tower("Enfield 1-1");
        twKA.Tick(west, none, 0);
        twKA.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        double kaFt = twKA.vecFt + 2000;
        var lowP = At(-291000, 660000, twKA.vecFt * Ft - FieldElev, twKA.vecHdg, twKA.VecKt(west) is > 0 and var kv ? kv * Kt : 150);
        twKA.vecFt = kaFt; twKA.altFree = false;   // R394: assigned altitude (not at his discretion)
        string? kickA = null;
        var kamsg = new List<string>();
        for (double tt = 2; tt < 600 && kickA == null; tt += 1)
            foreach (var m in twKA.Tick(lowP, none, tt)) { kamsg.Add(m.Text); if (m.Text.Contains("removed from the sequence")) kickA = m.Text; }
        Check(kickA != null && kamsg.Any(m => m.Contains("verify altitude")) && kamsg.Any(m => m.EndsWith($"climb and maintain {Alt(kaFt)} immediately.")), "Rauswurf Höhe Stufen: " + string.Join(" | ", kamsg));
        // within ±7° / ±500 ft / ±30 kt: no reminder
        var twV = new Tower("Enfield 1-1");
        twV.Tick(west, none, 0);
        twV.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        twV.altFree = false;   // R394: assigned altitude (not at his discretion)
        var calm = At(-291000, 660000, twV.vecFt * Ft - FieldElev + 120, twV.vecHdg + 6, twV.VecKt(west) is > 0 and var vk ? (vk + 25) * Kt : 150);
        var nag = Enumerable.Range(2, 120).SelectMany(s => twV.Tick(calm, none, s)).Select(m => m.Text).ToList();
        Check(nag.Count == 0, "Toleranz: " + string.Join(" | ", nag));
        // just decelerating from 600 kt: no speed reminder
        foreach (var s in Enumerable.Range(122, 3)) twV.Tick(calm with { Ias = 600 * Kt }, none, s);
        var brake = Enumerable.Range(125, 60).SelectMany(s => twV.Tick(calm with { Ias = (600 - (s - 125) * 2) * Kt }, none, s)).Select(m => m.Text).ToList();
        Check(!brake.Any(m => m.Contains("speed")), "Bremsen: " + string.Join(" | ", brake));
        // Helicopter: no speed given under radar vectoring, no admonition at 140 kt either; A-10 slower than jet
        var twHeli = new Tower("Enfield 1-1") { AcType = "UH-1H" };
        twHeli.Tick(west, none, 0);
        var heliV = twHeli.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        var heliCalm = At(-291000, 660000, twHeli.vecFt * Ft - FieldElev + 120, twHeli.vecHdg + 6, 140 * Kt);
        var heliNag = Enumerable.Range(2, 120).SelectMany(s => twHeli.Tick(heliCalm, none, s)).Select(m => m.Text).ToList();
        Check(twHeli.VecKt(west) == 0 && heliV.All(m => !m.Text.Contains("knots")) && heliNag.All(m => !m.Contains("speed")), "Hubschrauber ohne Fahrtangabe: " + string.Join(" | ", heliV.Select(m => m.Text).Concat(heliNag)));
        Check(new Tower("Enfield 1-1") { AcType = "A-10C_2" }.VecKt(west) == 250 && new Tower("Enfield 1-1") { AcType = "F-16C_50" }.VecKt(west) == 300 &&
              new Tower("Enfield 1-1") { AcType = "Christen Eagle II" }.VecKt(west) == 0 && new Tower("Enfield 1-1") { AcType = "F4U-1D" }.VecKt(west) == 200, "VecKt nach Typ");
        // User request: at night or type without break straight-in approach right away, "overhead" requested stays overhead; "request straight in" after inbound without new first contact
        string Run(Tower tw, params string[] says) { tw.Tick(west, none, 0); return string.Join(" | ", says.SelectMany((s, i) => tw.OnTranscript("Kutaisi Approach Enfield one one " + s, west, none, i + 1)).Select(m => m.Text)); }
        Carrier.Sun = -10;
        Tower twNt = new("Enfield 1-1"), twNtO = new("Enfield 1-1");
        var ntIn = Run(twNt, "inbound for landing"); var ntOvh = Run(twNtO, "request overhead");
        Carrier.Sun = double.NaN;
        Tower twDy = new("Enfield 1-1"), twC130 = new("Enfield 1-1") { AcType = "C-130" };
        var dyIn = Run(twDy, "inbound for landing"); var c130In = Run(twC130, "inbound for landing");
        Check(twNt.wantStraight && !twNtO.wantStraight && !twDy.wantStraight && twC130.wantStraight, $"Nacht/Typ -> Geradeaus: {ntIn} || {ntOvh} || {dyIn} || {c130In}");
        var dySi = string.Join(" | ", twDy.OnTranscript("Kutaisi Approach Enfield one one request straight in", west, none, 5).Select(m => m.Text));
        Check(twDy.wantStraight && !dySi.Contains("radar contact") && dySi.Contains("roger, straight in approach approved"), "straight in nach inbound: " + dySi);
        // slightly off (15°, 700 ft too high, not descending): only after 90 s one calm overall announcement, no admonition
        var twM = new Tower("Enfield 1-1");
        twM.Tick(west, none, 0);
        twM.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1);
        twM.altFree = false;   // R394: assigned altitude (not at his discretion)
        var mild = At(-291000, 660000, twM.vecFt * Ft - FieldElev + 700 * Ft, twM.vecHdg + 15, (twM.VecKt(west) is > 0 and var mk ? mk : 300) * Kt);
        var early = Enumerable.Range(2, 85).SelectMany(s => twM.Tick(mild, none, s)).Select(m => m.Text).ToList();
        var later = Enumerable.Range(87, 10).SelectMany(s => twM.Tick(mild, none, s)).Select(m => m.Text).ToList();
        Check(early.Count == 0 && later.Count == 1 && !later[0].Contains("expedite") && later[0].Contains("heading") && later[0].Contains("descend and maintain"),
              $"Leicht daneben: {string.Join(" | ", early)} / {string.Join(" | ", later)}");
        var sa = twV.OnTranscript("Kutaisi Approach Enfield 11 say again", calm with { Ias = 500 * Kt, AltMsl = calm.AltMsl + 2000 * Ft }, none, 200);
        Check(sa.Count == 1 && sa[0].Contains("heading") && sa[0].Contains("descend and maintain") && sa[0].Contains("reduce speed"), "Say again neu: " + (sa.FirstOrDefault()?.Text ?? "-"));
        // R129: 80 kt too fast, 6 min: speed once, then "say airspeed" (only 90 s after), without reply 30 s later "resume normal speed" and quiet (not six times "reduce speed")
        var twSp = new Tower("Enfield 1-1");
        twSp.Tick(west, none, 0);
        var spM = twSp.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west, none, 1).Select(m => m.Text).ToList();
        Check(!spM[0].Contains("speed") && spM[0].Contains("altitude at your discretion"), "VFR weit draußen ohne Fahrtvorgabe (wie Höhe): " + spM[0]);
        twSp.altFree = false;   // R129: assigned altitude and speed (not at his discretion)
        for (int s = 2; s < 400; s++) spM.AddRange(twSp.Tick(At(-291000, 660000, twSp.vecFt * Ft - FieldElev, twSp.vecHdg, (twSp.VecKt(west) is > 0 and var sk ? sk + 80 : 380) * Kt), none, s).Select(m => m.Text));
        var spK = spM.Where(m => m.Contains("speed")).ToList();
        Check(spK.Count == 3 && spK[0].Contains("speed 300 knots") && spK[1].Contains("say airspeed") && spK[2].Contains("resume normal speed") && twSp.VecKt(west) == 0,
              $"R129 Fahrtvorgabe nicht endlos: {spK.Count}x: " + string.Join(" | ", spK));
        // R129: into the vectoring at 450 kt, decelerates 2 kt/s toward 300: intermediate announcements (leg, heading) repeat the speed, no "say airspeed"/"resume normal speed"
        var twSl = new Tower("Enfield 1-1");
        twSl.Tick(west with { Ias = 450 * Kt }, none, 0);
        var slM = twSl.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west with { Ias = 450 * Kt }, none, 1).Select(m => m.Text).ToList();
        twSl.altFree = false;   // R129: assigned speed
        for (int s = 2; s < 60; s++)
        {
            var ts = At(-291000, 660000, twSl.vecFt * Ft - FieldElev, twSl.vecHdg, (450 - 2 * s) * Kt);
            slM.AddRange(twSl.Tick(ts, none, s).Select(m => m.Text));
            if (s % 15 == 0) slM.Add(twSl.VecCall(ts, s));
        }
        Check(!slM.Any(m => m.Contains("say airspeed") || m.Contains("resume normal speed")) && twSl.VecKt(west) == 300, "R129 bremst: " + string.Join(" | ", slM));
        // R129 forum Senaki (F/A-18): "reduce speed 300 knots", 32 s later next vector -> no "say airspeed" (time to comply), only after 90 s without decelerating;
        // reply "340 knots" -> speed once, "310" (radio wheel) -> roger, "unable" -> resume normal speed, no reply 30 s -> resume normal speed
        (Tower Tw, List<(double S, string M)> L, Func<double, Telemetry> T, double Sa) SpdAsk()
        {
            var tw = new Tower("Enfield 1-1");
            tw.Tick(west with { Ias = 380 * Kt }, none, 0);
            var l = tw.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", west with { Ias = 380 * Kt }, none, 1).Select(m => (1.0, m.Text)).ToList();
            tw.altFree = false;   // R129: assigned speed
            Telemetry T(double kt) => At(-291000, 660000, tw.vecFt * Ft - FieldElev, tw.vecHdg, kt * Kt);
            double s = 2;
            for (; s < 33; s++) l.AddRange(tw.Tick(T(380), none, s).Select(m => (s, m.Text)));
            l.Add((33, tw.VecCall(T(380), 33)));   // next vector 32 s after the speed (forum: 18:59:07 -> 18:59:39)
            for (s = 34; s < 200 && !l.Any(x => x.Item2.Contains("say airspeed")); s++) l.AddRange(tw.Tick(T(380), none, s).Select(m => (s, m.Text)));
            return (tw, l, T, s);
        }
        var (twQa, laQa, tQa, saQa) = SpdAsk();
        var spdQa = laQa.Where(x => x.M.Contains("reduce speed 300 knots")).Select(x => x.S).DefaultIfEmpty(-1).Max();
        var askQa = laQa.Where(x => x.M.Contains("say airspeed")).Select(x => x.S).ToList();
        var sugQa = twQa.Suggest(tQa(380));
        var rQa = string.Join(" | ", twQa.OnTranscript("Kutaisi Approach Enfield one one 340 knots", tQa(380), none, saQa + 5).Select(m => m.Text));
        Check(spdQa > 0 && askQa.Count == 1 && askQa[0] >= spdQa + 90 && sugQa == ("Approach", "report airspeed") && rQa == "Enfield one one, roger, reduce speed 300 knots.",
              $"R129 Zeit zur Umsetzung, Antwort 340: {string.Join(" | ", laQa.Select(x => $"{x.S}: {x.M}"))} -> {sugQa} -> {rQa}");
        var (twQb, _, tQb, saQb) = SpdAsk();
        var rQb = string.Join(" | ", twQb.OnTranscript("Approach: " + IasSay(tQb(312)), tQb(312), none, saQb + 5, true).Select(m => m.Text));
        var (twQc, _, tQc, saQc) = SpdAsk();
        var rQc = string.Join(" | ", twQc.OnTranscript("Kutaisi Approach Enfield one one unable", tQc(380), none, saQc + 5).Select(m => m.Text));
        var (twQd, _, tQd, saQd) = SpdAsk();
        var rQd = Enumerable.Range((int)saQd, 40).SelectMany(s => twQd.Tick(tQd(380), none, s)).Select(m => m.Text).ToList();
        Check(IasSay(tQb(312)) == "310 knots" && rQb == "Enfield one one, roger." && rQc == "Enfield one one, roger, resume normal speed." && twQc.VecKt(west) == 0
              && rQd.Count(m => m.Contains("speed")) == 1 && rQd.Any(m => m == "Enfield one one, resume normal speed.") && twQd.VecKt(west) == 0,
              $"R129 Antwort auf say airspeed: Funkrad 310 -> {rQb} / unable -> {rQc} / keine -> {string.Join(" | ", rQd)}");
        // R130: same altitude does not switch between climb and descend: only new altitude with direction, otherwise maintain (±300 ft Mode C)
        var twR130 = new Tower("Enfield 1-1");
        string AtFt(double pilotFt, double ft, bool dev = false) => twR130.AltTo(At(-291000, 660000, pilotFt * Ft - FieldElev, 0, 150), ft, dev);
        var al = new[] { AtFt(4000, 5000), AtFt(6000, 5000), AtFt(6000, 5000, true), AtFt(6000, 3000), AtFt(2400, 2700), AtFt(3000, 2700) };
        Check(al[0] == "climb and maintain 5000 feet" && al[1] == "maintain 5000 feet" && al[2] == "descend and maintain 5000 feet" && al[3] == "descend and maintain 3000 feet" && al[4] == "maintain 2700 feet" && al[5] == "maintain 2700 feet",
              "R130 AltTo: " + string.Join(" | ", al));
        // Traffic head-on 3 NM ahead, same altitude -> warning, evade right, altitude omitted (not into terrain)
        var twA = new Tower("Enfield 1-1");
        var me = At(-291000, 660000, 900, 90, 120);
        twA.Tick(me, none, 0);
        twA.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", me, none, 1);
        var bogey = new[] { new Traffic(77, "Su-25T", me.X, me.Z + 3 * NM, me.AltMsl, 270 * Math.PI / 180, 120) };
        r = twA.Tick(me, bogey, 2);
        // R214: wake word before the callsign, "advise you turn … and climb to … immediately" (FAA JO 7110.65 2-1-6 b), no "traffic alert, traffic"
        Check(r.Count == 1 && r[0].Text.StartsWith("Traffic alert, Enfield one one, 12 o'clock, 3 miles, westbound, same altitude, advise you turn right heading ") &&
              Regex.IsMatch(r[0].Text, @" and climb to \d+ feet immediately\.$") && !r[0].Contains("maintain"),
              "Verkehrswarnung: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = twA.Tick(me, new[] { bogey[0] with { Id = 78, Z = me.Z - 3 * NM, Hdg = 90 * Math.PI / 180, Speed = 100 } }, 3);   // behind me, same direction, slower
        Check(r.All(m => !m.Text.Contains("Traffic alert")), "Kein Konflikt: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twE52 = new Tower("Enfield 1-1") { Side = 2 };   // A52: hostile Su-25T head-on -> no radar advisory (AWACS reports it), own yes
        twE52.Tick(me, none, 0);
        twE52.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", me, none, 1);
        r = twE52.Tick(me, new[] { bogey[0] with { Coalition = 1 } }, 2);
        var rOwn = twE52.Tick(me, new[] { bogey[0] with { Id = 79, Coalition = 2 } }, 3);
        Check(r.All(m => !m.Text.Contains("Traffic alert")) && rOwn.Any(m => m.Text.Contains("Traffic alert")), "A52 Verkehrswarnung nur eigene/neutrale: " + string.Join(" | ", r.Concat(rOwn)));
        // R213: safety alert also after handover to Departure and on the IFR route (until sign-off); R214: VFR without fixed altitude ("and climb immediately")
        foreach (var (dPh, dIfr) in new[] { (Phase.Away, true), (Phase.Away, false), (Phase.Departing, true) })
        {
            var dme = At(CX - 3 * NM, CZ, 1200, 90, 120);
            var twDa = new Tower("Enfield 1-1");
            twDa.Tick(dme, none, 0);
            (twDa.Phase, twDa.handedOff, twDa.handedAt, twDa.depClr, twDa.depFt, twDa.cruiseFt) = (dPh, true, 1, dIfr, 3000, 15000);
            var da = twDa.Tick(dme, new[] { bogey[0] with { X = dme.X, Z = dme.Z + 3 * NM, AltMsl = dme.AltMsl } }, 2);
            Check(da.Count == 1 && da[0].Text.StartsWith("Traffic alert, Enfield one one, 12 o'clock, 3 miles") && da[0].Role == "Approach" &&
                  (dIfr ? Regex.IsMatch(da[0].Text, @"and (?:climb|descend) to \d+ feet immediately\.$") : da[0].Text.EndsWith("and climb immediately.")) &&
                  (dPh != Phase.Departing || da[0].Text.Contains($"to {twDa.depFt:0} feet")), $"R213 Safety Alert {dPh}/{(dIfr ? "IFR" : "VFR")}: " + (da.FirstOrDefault()?.Text ?? "-") + $" (Abflughöhe {twDa.depFt:0})");   // Evasion altitude replaces the departure altitude
        }
        {   // R216: ongoing traffic advisories (≤ 5 NM, ±1500 ft) also under radar vectoring and after departure check-in, not only with flight following
            var abeam = new[] { new Traffic(90, "F-16C_50", me.X - 4 * NM, me.Z, me.AltMsl, me.Hdg, me.Ias) };   // 4 NM right, same heading: no conflict
            var twR216a = new Tower("Enfield 1-1");
            twR216a.Tick(me, none, 0);
            twR216a.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", me, none, 1);
            var tv1 = twR216a.Tick(me, abeam, 2);
            var twR216b = new Tower("Enfield 1-1");
            twR216b.Tick(me, none, 0);
            (twR216b.Phase, twR216b.handedOff, twR216b.Squawk) = (Phase.Departing, true, "4021");
            twR216b.OnTranscript("Kutaisi Approach, Enfield 1-1, airborne, passing 3000", me, none, 1);
            var tv2 = twR216b.Tick(me, abeam, 2);
            Check(new[] { tv1, tv2 }.All(v => v.Count == 1 && v[0].Text.StartsWith("Enfield one one, traffic, 3 o'clock, 4 miles, eastbound, same altitude, Viper") && v[0].Role == "Approach") && twR216a.Tick(me, abeam, 3).Count == 0,
                  "R216 Verkehrshinweis Radarführung/Abflug: " + string.Join(" | ", tv1.Concat(tv2).Select(m => m.Text)));
            // Review R216: in holding no advisory on controlled traffic separated by 1000 ft via assigned altitudes; VFR/unknown on the same level yes,
            // likewise a controlled one that is not (yet) holding its altitude
            var twR216h = new Tower("Enfield 1-1");
            twR216h.Tick(me, none, 0);
            (twR216h.Phase, twR216h.holding, twR216h.holdFt) = (Phase.Inbound, true, Math.Round(twR216h.IndFt(me)));
            var above = abeam[0] with { AltMsl = me.AltMsl + 1000 * Ft, Flag = "hold:Kutaisi", AsgFt = twR216h.holdFt + 1000 };
            var th1 = twR216h.TrafficInfo(me, new[] { above }, 2);
            var th2 = twR216h.TrafficInfo(me, new[] { above with { AsgFt = 0, Flag = "" } }, 3);
            var th3 = new Tower("Enfield 1-1") { Phase = Phase.Inbound, holding = true, holdFt = twR216h.holdFt }.TrafficInfo(me, new[] { above with { AltMsl = me.AltMsl + 600 * Ft } }, 2);
            Check(th1 == null && th2 is [var h2] && h2.Text.Contains("1000 feet above") && th3 is [_],
                  "R216 Warteschleife gestaffelt still, VFR/abweichend mit Hinweis: " + (th1?[0].Text ?? "-") + " | " + (th2?[0].Text ?? "-") + " | " + (th3?[0].Text ?? "-"));
        }
        // First call next to a holding pattern (600 ft below, just over 1 NM): state 1000 ft separation + traffic right away, not "Traffic alert" 2 s later (game 07.10.)
        var twH = new Tower("Enfield 1-1");
        var meH = At(-291000, 660000, 900, 90, 140);
        var hld = new[] { new Traffic(79, "FA-18C_hornet", meH.X + 0.5 * NM, meH.Z - NM, meH.AltMsl - 600 * Ft, 0, 115, "", "hold") };
        twH.Tick(meH, hld, 0);
        var fH = twH.OnTranscript("Kutaisi Approach Enfield one one inbound for landing", meH, hld, 1)[0].Text;
        r = twH.Tick(meH, hld, 3);
        Check(fH.Contains("climb and maintain") && fH.Contains("600 feet below, Hornet") && twH.vecFt * Ft >= hld[0].AltMsl + 999 * Ft && r.All(m => !m.Text.Contains("Traffic alert")),
              $"Erstanruf neben Warteschleife: {fH} | {r.FirstOrDefault()?.Text}");
        Check(AirConflict(me, new[] { bogey[0] with { AltMsl = me.AltMsl + 1000 * Ft } }) == null, "1000 ft Staffel: kein Alarm");
        r = tw3.OnTranscript("Enfield 11 C R P south", west, none, 1.5);
        Check(r[0].Contains("negative, you are") && r[0].Contains("Fly heading"), "CRP gemeldet, aber weit weg: " + r[0]);
        var atSouth = At(Crp["south"].X, Crp["south"].Z, 600, 330, 150);
        r = tw3.OnTranscript("Enfield 11 C R P south", atSouth, none, 2);
        Check(r[0].Contains("proceed to initial runway two five") && r[0].Contains("contact Kutaisi Tower two six five decimal zero") && r[0].Role == "Approach", "Übergabe am CRP: " + r[0]);
        // R256: Tower does not repeat the altitude that Approach just instructed in the handover ("proceed to initial, 2000 feet, contact Tower")
        r = tw3.OnTranscript("Kutaisi Tower, Enfield 11, inbound", At(Crp["south"].X, Crp["south"].Z, 1500, 330, 150), none, 2.5);
        Check(r.Count == 1 && r[0].Contains("report initial runway two five") && !r[0].Contains("maintain"), "R256 Erstanruf Tower ohne wiederholte Höhe: " + string.Join(" | ", r.Select(m => m.Text)));
        var ipPos = P("25", InitialDist);
        r = tw3.OnTranscript("Enfield 11 initial", At(ipPos.Item1, ipPos.Item2, 600, 254, 150), none, 3);
        Check(r[0].Contains("runway two five, report base"), r[0]);
        // "7-mile initial" right after handover (on the centerline on approach): break clearance instead of "continue, … report initial"
        var twI = new Tower("Enfield 1-1");
        var p7 = P("25", 7 * NM);
        var at7 = At(p7.Item1, p7.Item2, 600, 254, 150);
        twI.Tick(at7, none, 0);
        twI.Phase = Phase.Entering;   // handed over from Approach
        r = twI.OnTranscript("Kutaisi Tower Enfield 11 initial", at7, none, 1);
        Check(r[0].Contains("runway two five, report base"), "7-mile initial: " + r[0]);
        // Visual: 30° turn-in no longer works (across, close to initial) -> direct to initial instead of full circle
        var twDi = new Tower("Enfield 1-1");
        var di0 = At(P("25", 14 * NM).Item1, P("25", 14 * NM).Item2, 900, 254, 150);
        twDi.Tick(di0, none, 0);
        twDi.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing", di0, none, 1);
        twDi.vec?.Clear();
        var di1 = twDi.Pt(twDi.vrw, 4.5 * NM, 2 * NM);
        r = new();
        for (int s = 10; s <= 40 && r.Count == 0; s += 5) r = twDi.Tick(At(di1.X, di1.Z, 600, twDi.LandHdg(twDi.vrw), 150), none, s);
        Check(!twDi.vecFinal && r.Count == 1 && r[0].Contains("proceed direct initial runway") && twDi.Phase == Phase.Entering && twDi.navName == "initial", "Direkt zum Initial: " + (r.FirstOrDefault()?.Text ?? "-"));
        // A145: first contact states poor visibility and low clouds, below 550 m / 200 ft "say intentions", once per approach
        var sky0 = Carrier.Sky;
        try
        {
            var wxPos = At(P("25", 14 * NM).Item1, P("25", 14 * NM).Item2, 900, 254, 150);
            string WxIn(Tower w) => w.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing", wxPos, none, 1).FirstOrDefault()?.Text ?? "-";
            var twWx = new Tower("Enfield 1-1"); twWx.Tick(wxPos, none, 0);
            Carrier.Sky = (false, 0, 80000);
            var wx0 = WxIn(twWx);
            Check(!wx0.Contains("isibility") && !wx0.Contains("eiling"), "A145 gutes Wetter: nichts: " + wx0);
            twWx = new Tower("Enfield 1-1"); twWx.Tick(wxPos, none, 0);
            Carrier.Sky = (false, 0, 600);
            var wx1 = WxIn(twWx);
            Check(wx1.Contains("Visibility 600 meters") && !wx1.Contains("eiling") && !wx1.Contains("Say intentions"), "A145 Sicht 600 m: " + wx1);
            twWx = new Tower("Enfield 1-1"); twWx.Tick(wxPos, none, 0);
            Carrier.Sky = (false, 0, 3000);
            var wx2 = WxIn(twWx);
            Check(wx2.Contains("Visibility 3 kilometers") && !wx2.Contains("Say intentions"), "A145 Sicht 3 km: " + wx2);
            twWx = new Tower("Enfield 1-1"); twWx.Tick(wxPos, none, 0);
            Carrier.Sky = (false, 0, 300);
            var wx3 = WxIn(twWx);
            Check(wx3.Contains("Visibility 300 meters") && wx3.Contains("Say intentions"), "A145 Sicht 300 m: " + wx3);
            twWx = new Tower("Enfield 1-1"); twWx.Tick(wxPos, none, 0);
            Carrier.Sky = (true, twWx.F.Elev + 150 * Ft, 80000);
            var wx4 = WxIn(twWx);
            Check(wx4.Contains("Ceiling 200 feet") && wx4.Contains("Say intentions") && !wx4.Contains("isibility"), "A145 Untergrenze 150 ft: " + wx4);
            Check(!WxIn(twWx).Contains("eiling"), "A145 nur einmal je Anflug");
            twWx.OnTranscript("Kutaisi Approach Enfield 11 cancel approach", wxPos, none, 2);
            Check(WxIn(twWx).Contains("Ceiling 200 feet"), "A145 nach cancel approach neuer Anflug: Wetter wieder");
        }
        finally { Carrier.Sky = sky0; }
        // A46: second "inbound"/"initial" under radar vectoring is not a new first contact: only repeat heading/altitude, within 5 NM of initial (turned in) direct there right away and to Tower
        var twRe = new Tower("Enfield 1-1");
        var re0 = At(P("25", 14 * NM).Item1, P("25", 14 * NM).Item2, 900, 254, 150);
        twRe.Tick(re0, none, 0);
        twRe.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing", re0, none, 1);
        var re1 = At(P("25", 12 * NM).Item1, P("25", 12 * NM).Item2, 900, 254, 150);
        r = twRe.OnTranscript("Kutaisi Approach Enfield 11 inbound", re1, none, 6);
        Check(r.Count == 1 && r[0].Contains("roger, ") && r[0].Contains("heading") && !r[0].Contains("identified") && !r[0].Contains("Runway two five in use") && twRe.Phase == Phase.Inbound && twRe.Vectoring, "Zweites inbound unter Radarführung: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = twRe.OnTranscript("Kutaisi Approach Enfield 11 initial", re1, none, 11);
        Check(r.Count == 1 && r[0].Contains("roger, ") && !r[0].Contains("identified") && twRe.Phase == Phase.Inbound, "initial unter Radarführung, weit vom IP: " + (r.FirstOrDefault()?.Text ?? "-"));
        // Altitude report ("verify altitude") under radar vectoring over the field: Approach continues, Tower does not take over with "join downwind"
        var twAlt = new Tower("Enfield 1-1");
        twAlt.Tick(re0, none, 0);
        twAlt.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing", re0, none, 1);
        r = twAlt.OnTranscript("Kutaisi Approach Enfield 11 6900 feet", At(P("25", -2 * NM).Item1, P("25", -2 * NM).Item2, 2000, 254, 300), none, 6);
        Check(r.Count == 1 && r[0].Role == "Approach" && (r[0].Contains("heading") || r[0].Contains("direct initial")), "Höhenmeldung unter Radarführung über dem Platz: " + (r.FirstOrDefault()?.Text ?? "-"));
        twRe.vec!.Clear();
        var re2 = At(P("25", InitialDist + 3.5 * NM).Item1, P("25", InitialDist + 3.5 * NM).Item2, 900, 254, 150);
        r = twRe.OnTranscript("Kutaisi Approach Enfield 11 initial", re2, none, 16);
        Check(r.Count == 1 && r[0].Contains("proceed direct initial runway") && r[0].Contains("contact") && r[0].Role == "Approach" && twRe.Phase == Phase.Entering && twRe.navName == "initial", "initial eingedreht, unter 5 NM vom IP: " + (r.FirstOrDefault()?.Text ?? "-"));
        // A46: number 2 (leader close, Spaced) gets no direct on "initial", radar vectoring continues as in VecTick
        var twN2 = new Tower("Enfield 1-1") { QueueAhead = 1, Spaced = true };
        twN2.Tick(re0, none, 0);
        twN2.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing", re0, none, 1);
        twN2.vec!.Clear();
        r = twN2.OnTranscript("Kutaisi Approach Enfield 11 initial", re2, none, 6);
        Check(r.Count == 1 && r[0].Contains("roger, ") && !r[0].Contains("proceed direct") && twN2.Phase == Phase.Inbound, "A46 Nummer 2 eingedreht, kein direct: " + (r.FirstOrDefault()?.Text ?? "-"));
        // A46: second "inbound ... runway 07" under radar vectoring confirms the runway change and re-plans the headings
        var twRw = new Tower("Enfield 1-1");
        twRw.Tick(re0, none, 0);
        twRw.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing", re0, none, 1);
        r = twRw.OnTranscript("Kutaisi Approach Enfield 11 inbound for landing runway 07", re1, none, 6);
        Check(r.Count == 1 && r[0].Contains("runway zero seven approved") && twRw.Runway == "07" && twRw.Phase == Phase.Inbound && twRw.Vectoring, $"A46 inbound mit Bahnwechsel unter Radarführung: {twRw.Runway}/{twRw.vrw} " + (r.FirstOrDefault()?.Text ?? "-"));
        // R31: initial only up to 0.5 NM lateral of the axis (transmission and tick)
        var twLa = new Tower("Enfield 1-1");
        var laOff = kTower.Pt("25", InitialDist, 0.8 * NM);
        var laOn = kTower.Pt("25", InitialDist - 100, 0.3 * NM);
        twLa.Tick(At(laOff.X, laOff.Z, 600, 254, 150), none, 0);
        r = twLa.OnTranscript("Kutaisi Tower Enfield 11 initial", At(laOff.X, laOff.Z, 600, 254, 150), none, 1);
        Check(r.Count == 1 && r[0].Contains("negative, you are") && r[0].Contains("report initial") && twLa.Phase == Phase.Entering, "Initial 0,8 NM neben der Achse: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = twLa.Tick(At(laOff.X, laOff.Z, 600, 254, 150), none, 2);
        Check(twLa.Phase == Phase.Entering && r.All(m => !m.Text.Contains("report base")), "Tick: 0,8 NM neben der Achse noch kein Initial");
        // Forum: break clearance only on the call "initial"; at initial without a call exactly once "report initial", no loop
        r = twLa.Tick(At(laOn.X, laOn.Z, 600, 254, 150), none, 3);
        var laAsk = string.Join(" | ", r.Select(m => m.Text)) + " / " + string.Join(" | ", Enumerable.Range(4, 40).SelectMany(s => twLa.Tick(At(laOn.X, laOn.Z, 600, 254, 150), none, s)).Select(m => m.Text));
        Check(laAsk.StartsWith("Enfield one one, Kutaisi Tower, report initial. / ") && Regex.Matches(laAsk, "report initial").Count == 1 && !laAsk.Contains("report base"), "Tick: 0,3 NM neben der Achse ohne Ruf: einmal report initial, keine Break-Freigabe: " + laAsk);
        // without a call continues toward the break: once "no break clearance, continue straight through, re-enter initial", no break/landing clearance
        var twWo = new Tower("Enfield 1-1");
        twWo.Tick(At(laOff.X, laOff.Z, 600, 254, 150), none, 0);
        twWo.OnTranscript("Kutaisi Tower Enfield 11 initial", At(laOff.X, laOff.Z, 600, 254, 150), none, 1);
        var woSaid = Enumerable.Range(2, 60).SelectMany(s => { var p = kTower.Pt("25", InitialDist - 100 - (s - 2) * 150, 0); return twWo.Tick(At(p.X, p.Z, 600, 254, 150), none, s); }).Select(m => m.Text).ToList();
        Check(woSaid.Count(s => s.Contains("Kutaisi Tower, report initial.")) == 1 && woSaid.Count(s => s.Contains("no break clearance, continue straight through, re-enter initial runway two five")) == 1 &&
              !woSaid.Any(s => s.Contains("cleared")) && twWo.Phase == Phase.Entering, "Initial ohne Ruf überflogen: einmal report initial, einmal no break clearance: " + string.Join(" | ", woSaid));
        r = twLa.OnTranscript("Kutaisi Tower Enfield 11 initial", At(laOn.X, laOn.Z, 600, 254, 150), none, 45);
        Check(twLa.Phase == Phase.Initial && r.Any(m => m.Text.Contains("report base")), "Ruf initial 0,3 NM neben der Achse = Break-Freigabe: " + string.Join(" | ", r));
        Check(r.Any(m => m.Text.Contains("QNH") && m.Text.Contains("wind")), "R126 Erstkontakt über die Abweisung: Break-Freigabe mit Wind und QNH: " + string.Join(" | ", r));
        // R126: first contact with Tower (Away) gets wind and QNH before entry, not after handover from Approach and with confirmed ATIS
        var wIp = At(ipPos.Item1, ipPos.Item2, 600, 254, 150);
        Tower WxTower(Phase ph, string atis = "") { var w = new Tower("Enfield 1-1") { AtisLetter = atis }; w.Tick(wIp, none, 0); w.Phase = ph; return w; }
        r = WxTower(Phase.Away).OnTranscript("Kutaisi Tower Enfield 11 initial", wIp, none, 1);
        Check(r[0].Contains("report base") && r[0].Contains("wind") && r[0].Contains("QNH"), "R126 Away, initial: " + r[0]);
        r = WxTower(Phase.Entering).OnTranscript("Kutaisi Tower Enfield 11 initial", wIp, none, 1);
        Check(r[0].Contains("report base") && !r[0].Contains("QNH"), "R126 nach Übergabe unverändert: " + r[0]);
        r = WxTower(Phase.Away, "C").OnTranscript("Kutaisi Tower Enfield 11 information Charlie initial", wIp, none, 1);
        Check(r[0].Contains("report base") && !r[0].Contains("QNH"), "R126 ATIS bestätigt: " + r[0]);
        var wOv = At(CX, CZ, 900, 254, 150);
        r = WxTower(Phase.Away).OnTranscript("Kutaisi Tower Enfield 11 overhead", wOv, none, 1);
        Check(r[0].Contains("join") && r[0].Contains("QNH"), "R126 Away, overhead: " + r[0]);
        r = WxTower(Phase.Away).OnTranscript("Kutaisi Tower Enfield 11 downwind", At(CX, CZ, 600, 74, 150), none, 1);
        Check(r[0].Contains("number one") && r[0].Contains("QNH"), "R126 Away, downwind: " + r[0]);
        r = WxTower(Phase.Pattern).OnTranscript("Kutaisi Tower Enfield 11 downwind", At(CX, CZ, 600, 74, 150), none, 1);
        Check(r[0].Contains("number one") && !r[0].Contains("QNH"), "R126 in der Platzrunde unverändert: " + r[0]);
        var (ax, az) = P("25", 2.5 * NM);
        var onFinal = At(ax, az, 250, Math.Atan2(Uz, Ux) * 180 / Math.PI + 180, 70);
        r = tw3.OnTranscript("Enfield 11 final", onFinal, none, 4);   // A43: no landing clearance on initial
        Check(tw3.Phase == Phase.Initial && r[0].Contains("roger, report base") && !r[0].Contains("cleared"), "A43 final auf dem Initial: " + r[0]);
        tw3.Phase = Phase.Pattern;   // Break flown (tick switches to Pattern on downwind)
        r = tw3.OnTranscript("Kutaisi Tower, Enfield 11, final", onFinal, none, 4.5);
        Check(tw3.Phase == Phase.ClearedLand && r[0].Contains("cleared to land"), r[0]);
        r = tw3.Tick(At(CX, CZ, 0, 254, 20), none, 5);
        Check(r.Count == 1 && r[0].Role == "Info" && r[0].StartsWith("Landebewertung"), r.FirstOrDefault()?.Text ?? "Bewertung: -");
        r = tw3.Tick(At(CX, CZ, 0, 254, 20), none, 7);
        Check(r.Count == 1 && !r[0].Contains("elcome") && r[0].Contains("vacate right via Alpha or Bravo"), r.FirstOrDefault()?.Text ?? "-");
        // Exit taxiway already in rollout (2 s on the ground, below 100 kt), not touch-and-go
        var twR = new Tower("Enfield 1-1");
        twR.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
        twR.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1);
        r = twR.Tick(At(CX, CZ, 0, 254, 95 * Kt), none, 2);
        Check(r.Count == 0, "Ausrollen 1 s: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = twR.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3);
        Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("vacate"), "Ausrollen 90 kt: " + (r.FirstOrDefault()?.Text ?? "-"));
        // A48: following traffic under 3 NM on final -> "expedite vacating" in the welcome call, at 5 NM not, at 2 NM at 1500 ft (initial to the break, mptest Batumi) not;
        // at 2.8 NM (R46 3 NM separation, ~75 s to the threshold, mptest Golf Jiroft IFR) not: vacates by then
        foreach (var (fNm, fAgl, want) in new[] { (2.0, 300.0, true), (5.0, 300.0, false), (2.0, 1500 * Ft, false), (2.8, 300.0, false) })
        {
            var (fx48, fz48) = P("25", fNm * NM);
            var fol = new[] { new Traffic(7, "C-130", fx48, fz48, FieldElev + fAgl, (Math.Atan2(Uz, Ux) * 180 / Math.PI + 180) * Math.PI / 180, 70) };
            var twE48 = new Tower("Enfield 1-1");
            twE48.Tick(At(CX, CZ, 300, 254, 140 * Kt), fol, 0);
            twE48.Tick(At(CX, CZ, 0, 254, 130 * Kt), fol, 1);
            r = twE48.Tick(At(CX, CZ, 0, 254, 90 * Kt), fol, 3);
            Check(r.Count(m => m.Role != "Info") == 1 && !r[0].Contains("elcome") && r[0].Contains("expedite vacating, traffic C 130 on 2 mile final") == want && (want ? r[0].Contains("vacate") : !r[0].Contains("xpedite")),
                  $"A48 Folgeverkehr {fNm} NM {fAgl / Ft:0} ft: " + (r.FirstOrDefault()?.Text ?? "-"));
        }
        // A48/A83: at welcome following traffic still at 5 NM, player stays standing on the runway, traffic comes to 1 NM -> once "expedite vacating"
        {
            var tr = (Math.Atan2(Uz, Ux) * 180 / Math.PI + 180) * Math.PI / 180;
            var (f5x, f5z) = P("25", 5 * NM); var (f1x, f1z) = P("25", 1 * NM);
            var twX = new Tower("Enfield 1-1");
            twX.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
            twX.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1);
            var x1 = twX.Tick(At(CX, CZ, 0, 254, 90 * Kt), new[] { new Traffic(7, "C-130", f5x, f5z, FieldElev + 300, tr, 70) }, 3).Where(m => m.Role != "Info").ToList();
            var near = new[] { new Traffic(7, "C-130", f1x, f1z, FieldElev + 100, tr, 70) };
            var x2 = twX.Tick(At(CX, CZ, 0, 254, 0), near, 60);
            var x3 = twX.Tick(At(CX, CZ, 0, 254, 0), near, 61);
            Check(x1.Count == 1 && !x1[0].Contains("xpedite") && x2.Count == 1 && x2[0].Contains("expedite vacating, traffic C 130 on 1 mile final") && x3.Count == 0,
                  "A48/A83 Folgeverkehr nach dem Willkommen: " + string.Join(" | ", x1.Concat(x2).Concat(x3)));
            // R255: lander is standing on the runway, follower 2.5 NM -> expedite immediately; taxiing slowly (5 m/s): after 25 s expedite, not before
            var (f25x, f25z) = P("25", 2.5 * NM);
            var at25 = new[] { new Traffic(7, "C-130", f25x, f25z, FieldElev + 250, tr, 70) };
            var x255 = new List<string>();
            foreach (var (v255, t1, t2) in new[] { (0.0, 10.0, 11.0), (5.0, 10.0, 40.0) })
            {
                var tw255 = new Tower("Enfield 1-1");
                tw255.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
                tw255.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1);
                tw255.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3);
                x255.Add(string.Join(" ", tw255.Tick(At(CX, CZ, 0, 254, v255), at25, t1).Select(m => m.Text)) + "/" + string.Join(" ", tw255.Tick(At(CX, CZ, 0, 254, v255), at25, t2).Select(m => m.Text)));
            }
            Check(x255[0].StartsWith("Enfield one one, expedite vacating, traffic C 130 on ") && x255[0].EndsWith("final./") &&
                  x255[1].StartsWith("/Enfield one one, expedite vacating, traffic C 130 on "), "R255 stehender/langsamer Lander: " + string.Join(" | ", x255));
            // R331: stands at 9 m/s headwind (IAS 9): like standing, expedite immediately (before, IAS counted as taxiing); R333: without following traffic exit taxiway only slowly
            var tw331 = new Tower("Enfield 1-1");
            tw331.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0); tw331.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1); tw331.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3);
            var x331 = tw331.Tick(At(CX, CZ, 0, 254, 9) with { WindX = 9 }, at25, 10);
            var tw333 = new Tower("Enfield 1-1");
            tw333.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0); tw333.Phase = Phase.ClearedLand; tw333.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1);
            var x333a = tw333.Tick(At(CX, CZ, 0, 254, 95 * Kt), none, 3).Where(m => m.Role != "Info").ToList();
            var x333b = tw333.Tick(At(CX, CZ, 0, 254, 35 * Kt), none, 8).Where(m => m.Role != "Info").ToList();
            Check(x331.Any(m => m.Contains("expedite vacating")) && x333a.Count == 0 && x333b.Count == 1 && x333b[0].Contains("vacate"),
                  "R331 Stand bei Gegenwind / R333 Abrollweg erst langsam: " + string.Join(" | ", x331.Concat(x333a).Concat(x333b).Select(m => m.Text)));
            // R266: following traffic without exit taxiway: no "vacate runway when able" after "expedite vacating"
            var tw266 = new Tower("Enfield 1-1");
            tw266.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0); tw266.home = null;   // without exit taxiway (like airfields without map)
            tw266.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1);
            var (f2x, f2z) = P("25", 2 * NM);
            var x266 = tw266.Tick(At(CX, CZ, 0, 254, 90 * Kt), new[] { new Traffic(7, "C-130", f2x, f2z, FieldElev + 250, tr, 70) }, 3).FirstOrDefault()?.Text ?? "-";
            Check(x266 == "Enfield one one, expedite vacating, traffic C 130 on 2 mile final, contact Kutaisi Ground two six four decimal five when vacated.", "R266 expedite ohne when able: " + x266);
        }
        var twG = new Tower("Enfield 1-1");
        twG.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
        twG.option = "touch and go";
        var tg = Enumerable.Range(1, 4).SelectMany(s => twG.Tick(At(CX, CZ, 0, 254, 95 * Kt), none, s)).Where(m => m.Role != "Info").ToList();
        Check(tg.Count == 0, "Touch-and-go: " + string.Join(" | ", tg));

        // --- Taxiing in to own parking position, then start again
        var twP = new Tower("Enfield 1-1");
        twP.Tick(parked, none, 0);
        twP.Tick(At(CX, CZ, 300, 254, 80), none, 5);
        twP.Tick(At(CX, CZ, 0, 254, 20), none, 10);
        twP.Tick(At(CX, CZ, 0, 254, 20), none, 11);
        twP.Tick(At(CX, CZ, 0, 254, 5), none, 19);
        // A91: on the runway no taxi clearance to parking yet; off the runway 30 s after vacating once "contact Ground"
        var tw91 = new Tower("Enfield 1-1");
        tw91.Tick(At(CX, CZ, 300, 254, 80), none, 0); tw91.Tick(At(CX, CZ, 0, 254, 20), none, 5); tw91.Tick(At(CX, CZ, 0, 254, 5), none, 8);
        r = tw91.OnTranscript("Kutaisi Ground, Enfield 11, runway vacated, request taxi to parking", At(CX, CZ, 0, 254, 5), none, 10);
        Check(r[0].Contains("report runway vacated") && tw91.Rejected == 0 && tw91.Phase == Phase.TaxiIn && !tw91.taxiInTold, "A91 auf der Bahn: " + r[0]);
        var offRwy = At(CX - 400 * Uz, CZ + 400 * Ux, 0, 254, 5);
        var offSlow = offRwy with { Ias = 1 };   // waiting off the runway (below 5 kt, #13)
        Check(tw91.Tick(offSlow, none, 30).Count == 0 && tw91.Tick(offSlow, none, 50).Count == 0, "A91 vor 30 s abseits der Bahn still (42 s nach der Landung)");
        r = tw91.Tick(offSlow, none, 61);
        Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Text.Contains("contact Kutaisi Ground"), "A91 Erinnerung: " + r.FirstOrDefault()?.Text);
        Check(tw91.Tick(offSlow, none, 100).Count == 0, "A91 nur einmal");
        // Reporting obligation #13: without taxi clearance above 5 kt, 400 m off the runway: "hold position, you are not cleared to taxi" + violation, once, afterwards no A91 reminder;
        // R247: the notice "possible pilot deviation" comes separately, only when stopped
        var tw13g = new Tower("Enfield 1-1");
        tw13g.Tick(At(CX, CZ, 300, 254, 80), none, 0); tw13g.Tick(At(CX, CZ, 0, 254, 20), none, 5); tw13g.Tick(At(CX, CZ, 0, 254, 5), none, 8);
        r = tw13g.Tick(offRwy, none, 20);
        var r13g = Enumerable.Range(21, 60).SelectMany(s => tw13g.Tick(s < 40 ? offRwy : offRwy with { Ias = 0 }, none, s)).ToList();
        Check(r.Count == 2 && r[0].Text == "Enfield one one, hold position, you are not cleared to taxi. Contact Kutaisi Ground two six four decimal five for taxi." && r[0].Role == "Tower" &&
              r[1].Text.StartsWith(L("Verstoß: Rollen ohne Freigabe", "Deviation: taxi without clearance")) && r13g.Count == 1 && r13g[0].Text.StartsWith("Enfield one one, possible pilot deviation, advise you contact Kutaisi Tower at D S N three one four, "),
              "#13 Rollen ohne Freigabe: " + string.Join(" | ", r.Concat(r13g).Select(m => m.Text)));
        // R251: hung ordnance in the air -> straight-in, after landing "taxi to the de-arm area"; bird strike -> say intentions; hot brakes -> hot-brake area
        var tw251 = new Tower("Enfield 1-1");
        tw251.Tick(west, none, 0);
        var r251 = tw251.OnTranscript("Kutaisi Approach, Enfield 1-1, hung ordnance", west, none, 1)
            .Concat(tw251.OnTranscript("Kutaisi Approach, Enfield 1-1, bird strike", west, none, 2)).ToList();
        bool ws251 = tw251.wantStraight;
        tw251.Tick(At(CX, CZ, 300, 254, 80), none, 3); tw251.Tick(At(CX, CZ, 0, 254, 20), none, 5); tw251.Tick(At(CX, CZ, 0, 254, 5), none, 8);
        r251.AddRange(tw251.OnTranscript("Kutaisi Ground, Enfield 1-1, runway vacated, request taxi to parking", offRwy with { Ias = 0 }, none, 12));
        var tw251b = new Tower("Enfield 1-1");
        tw251b.Tick(At(CX, CZ, 300, 254, 80), none, 0); tw251b.Tick(At(CX, CZ, 0, 254, 20), none, 5); tw251b.Tick(At(CX, CZ, 0, 254, 5), none, 8);
        r251.AddRange(tw251b.OnTranscript("Kutaisi Ground, Enfield 1-1, hot brakes", offRwy with { Ias = 0 }, none, 20));
        Check(r251.Count == 4 && r251[0].Text == "Enfield one one, Kutaisi Approach, roger hung ordnance, expect straight-in runway two five." && ws251 &&
              r251[1].Text == "Enfield one one, Kutaisi Approach, roger bird strike, say intentions." && r251[2].Text == "Enfield one one, taxi to the de-arm area." &&
              r251[3].Text == "Enfield one one, roger, taxi to the hot brake area, fire department responding." && tw251b.taxiInTold, "R251: " + string.Join(" | ", r251.Select(m => m.Text)));
        // Review R251: "bird strike, request return to field" states the intention already: no "say intentions", straight-in/instrument approach right away
        var tw251c = new Tower("Enfield 1-1");
        tw251c.Tick(west, none, 0);
        var b251 = tw251c.OnTranscript("Kutaisi Approach, Enfield 1-1, bird strike, request return to field", west, none, 1);
        Check(b251.Count >= 1 && b251[0].Text.StartsWith("Enfield one one, Kutaisi Approach, roger bird strike. ") && !b251[0].Text.Contains("say intentions") && b251[0].Role == "Approach" &&
              tw251c.wantStraight && tw251c.Phase is Phase.Inbound or Phase.Entering, "Review R251 bird strike mit Absicht: " + string.Join(" | ", b251.Select(m => m.Text)));
        // afterwards (disarmed or brakes cold) normal to the parking position, not again to the de-arm/hot-brake area
        var p251 = tw251.OnTranscript("Kutaisi Ground, Enfield 1-1, de-armed, request taxi to parking", offRwy with { Ias = 0 }, none, 90)
            .Concat(tw251b.OnTranscript("Kutaisi Ground, Enfield 1-1, brakes cool, request taxi to parking", offRwy with { Ias = 0 }, none, 90)).ToList();
        Check(p251.Count == 2 && p251.All(m => m.Text.StartsWith("Enfield one one, taxi to ") && !m.Text.Contains("de-arm") && !m.Text.Contains("hot brake")), "R251 danach Parken: " + string.Join(" | ", p251.Select(m => m.Text)));
        // R265: stopped in the hot-brake/de-arm area: Enter "request taxi to parking"; parked there (engine off, far from the spot) = parked, "request startup" normal
        var tw265 = Landed();
        tw265.F.Spots.Add((CX - 2000 * Uz, CZ + 2000 * Ux, 4, 72));
        tw265.OnTranscript("Kutaisi Ground, Enfield 1-1, hot brakes", offRwy with { Ias = 0 }, none, 20);
        var sg265 = tw265.Suggest(offRwy) + " / " + tw265.Suggest(offRwy with { Ias = 0 });
        tw265.Tick(offRwy with { Ias = 0 }, none, 21);
        tw265.EngineOff();
        var ph265 = tw265.Phase;
        r = tw265.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", offRwy with { Ias = 0 }, none, 200);
        var tw265d = Landed();   // de-arm area after "hung ordnance": likewise
        tw265d.special = "de-arm area";
        tw265d.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi to parking", offRwy with { Ias = 0 }, none, 20);
        Check(sg265 == " / (Ground, request taxi to parking)" && ph265 == Phase.Parked && r.Count == 1 && r[0].Text.Contains("start up approved") &&
              tw265d.Suggest(offRwy with { Ias = 0 })?.Text == "request taxi to parking", $"R265 Sonderbereich: {sg265}, {ph265}, {r.FirstOrDefault()?.Text}, {tw265d.Suggest(offRwy with { Ias = 0 })}");
        // #13: standing off the runway with headwind (export IAS shows the wind) is not taxiing
        var tw13w = new Tower("Enfield 1-1");
        tw13w.Tick(At(CX, CZ, 300, 254, 80), none, 0); tw13w.Tick(At(CX, CZ, 0, 254, 20), none, 5); tw13w.Tick(At(CX, CZ, 0, 254, 5), none, 8);
        var r13w = Enumerable.Range(20, 15).SelectMany(s => tw13w.Tick(offRwy with { Ias = 5, WindX = 5 }, none, s)).ToList();
        Check(!r13w.Any(m => m.Text.Contains("not cleared to taxi")) && r13w.Count(m => m.Text.Contains("possible pilot deviation")) == 1, "#13 Stand mit Gegenwind (Hinweis zur Landung ohne Freigabe kommt im Stand): " + string.Join(" | ", r13w.Select(m => m.Text)));
        // A91: requested report "runway vacated" without "request" within 30 s is not a repeat -> taxi clearance
        var tw91v = new Tower("Enfield 1-1");
        tw91v.Tick(At(CX, CZ, 300, 254, 80), none, 0); tw91v.Tick(At(CX, CZ, 0, 254, 20), none, 5); tw91v.Tick(At(CX, CZ, 0, 254, 5), none, 8);
        tw91v.OnTranscript("Kutaisi Ground, Enfield 11, runway vacated, request taxi to parking", At(CX, CZ, 0, 254, 5), none, 10);
        r = tw91v.OnTranscript("Enfield 1-1, runway vacated", offRwy, none, 20);
        Check(r.Count == 1 && r[0].Text.Contains("taxi to") && tw91v.taxiInTold, "A91 Meldung nach report runway vacated: " + r.FirstOrDefault()?.Text);
        r = twP.OnTranscript("Kutaisi Ground, Enfield 11, runway vacated, request taxi to parking", offRwy, none, 20);
        Check(r[0].Contains("your parking position at your") && r[0].Contains("meters"), "Einrollen: " + r[0]);
        // Parking position with number: own occupied -> next free, never the helicopter pad
        var twS = new Tower("Enfield 1-1");
        twS.Tick(parked, none, 0);
        twS.F.Spots.AddRange(new[] { (parked.X, parked.Z, 3, 72), (CX + 300, CZ, 9, 40), (CX + 400, CZ, 7, 72) });
        twS.spot = (parked.X, parked.Z);
        var busy = new[] { new Traffic(5, "F-16C_50", parked.X + 5, parked.Z, FieldElev, 0, 0, "Viper", "", false, 2) };
        r = twS.OnTranscript("Kutaisi Ground, Enfield 11, runway vacated, request taxi to parking", At(CX - 400 * Uz, CZ + 400 * Ux, 0, 254, 5), busy, 20);
        twS.F.Spots.RemoveRange(twS.F.Spots.Count - 3, 3);
        Check(r[0].Contains("parking spot seven") && !r[0].Contains("your parking position"), "Parkposition: " + r[0]);
        // PARK-P1: back at the departure airfield (gap in the air -> reset) own spot; occupied -> next free, ramp that of the spot
        // (before, spot forgotten and ramp from departure: "taxi to Ramp West via November, parking spot three eight")
        string Back(Traffic[] tr)
        {
            var b = new Tower("Enfield 1-1");
            var w1 = At(-284566.3, 682401.0);   // DCS 1, Ramp West
            b.F.Spots.AddRange(new[] { (w1.X, w1.Z, 1, 104), (-284741.0, 683150.0, 38, 68) });
            b.Tick(w1, none, 0);
            b.Tick(At(CX + 20 * NM, CZ, 3000, 0, 200), none, 100);
            b.Tick(At(CX, CZ, 300, 254, 80), none, 200);
            b.Tick(At(CX, CZ, 0, 254, 20), none, 205);
            b.Tick(At(CX, CZ, 0, 254, 20), none, 208);
            return b.OnTranscript("Kutaisi Ground, Enfield 11, runway vacated, request taxi to parking", At(-284900, 683050, 0, 254, 5), tr, 215)[0].Text;
        }
        var own = Back(none);
        var taken = Back(new[] { new Traffic(5, "F-16C_50", -284566.3, 682401.0, FieldElev, 0, 0, "Viper", "", false, 2) });
        Check(own.Contains("taxi to Ramp West via November, parking spot one, at your 1 o'clock, 750 meters") &&
              taken.Contains("taxi to Ramp North via November, parking spot three eight,"), $"Eigener Stand: {own} | belegt: {taken}");
        r = twP.Tick(At(parked.X + 10, parked.Z, 0, 0, 1), none, 25);
        Check(r.Count == 0, "R211 Parkposition erreicht: Ground schweigt: " + r.FirstOrDefault()?.Text);
        r = twP.OnTranscript("Kutaisi Ground, Enfield 11, request taxi", At(parked.X + 10, parked.Z), none, 30);
        Check(r[0].Contains("taxi to holding point"), "Neustart nach Landung: " + r[0]);
        // A36/N26: taxiing in without a request is silent after the route, with growing distance a correction comes for 20 s, with "progressive" announcements per halving; holding point with hold short
        List<string> Roll(Tower w, double fx, double fz, double tx, double tz, double t0, int secs, double dir = 1)
        {
            var o = new List<string>();
            double len = Dist(fx, fz, tx, tz), rx = (tx - fx) / len, rz = (tz - fz) / len, hd = Bearing(fx, fz, tx, tz);
            for (int s = 1; s <= secs; s++) o.AddRange(w.Tick(At(fx + dir * rx * 10 * s, fz + dir * rz * 10 * s, 0, hd, 10), none, t0 + s).Select(m => m.Text));
            return o;
        }
        Tower Inbound(string req)
        {
            var w = new Tower("Enfield 1-1");
            w.Tick(parked, none, 0);
            w.Tick(At(CX, CZ, 300, 254, 80), none, 5);
            w.Tick(At(CX, CZ, 0, 254, 20), none, 10);
            w.Tick(At(CX, CZ, 0, 254, 20), none, 11);
            w.Tick(At(CX, CZ, 0, 254, 5), none, 19);
            var first = w.OnTranscript($"Kutaisi Ground, Enfield 11, runway vacated, request {req} to parking", offRwy, none, 20);
            return first[0].Contains("parking position at your") ? w : throw new Exception(first[0]);
        }
        double d0 = Dist(offRwy.X, offRwy.Z, parked.X, parked.Z);   // A91: taxi clearance to parking off the runway
        int toPark = (int)(d0 - 60) / 10;
        var quiet = Roll(Inbound("taxi"), offRwy.X, offRwy.Z, parked.X, parked.Z, 20, toPark);
        Check(d0 > 400 && quiet.Count == 0, "Einrollen ohne Anfrage: keine Folgeansagen " + string.Join(" | ", quiet));
        var drift = Roll(Inbound("taxi"), offRwy.X, offRwy.Z, parked.X, parked.Z, 20, 60, -1);
        Check(drift.Count == 1 && drift[0].Contains("parking position is now"), "Einrollen: Abstand wächst -> eine Korrektur " + string.Join(" | ", drift));
        var twPr = Inbound("progressive taxi");
        var prog = Roll(twPr, offRwy.X, offRwy.Z, parked.X, parked.Z, 20, toPark);
        Check(prog.Count >= 2 && prog.All(m => m.Contains("parking position now")), "progressive taxi zur Parkposition: " + string.Join(" | ", prog));
        var twPh = new Tower("Enfield 1-1");
        twPh.Tick(parked, none, 0);
        r = twPh.OnTranscript("Kutaisi Ground, Enfield 11, request progressive taxi", parked, none, 2);
        Check(twPh.Phase == Phase.TaxiOut && r[0].Contains("taxi to holding point runway two five") && r[0].Contains("o'clock"), "progressive taxi aus Parked: " + r[0]);
        var hpt = twPh.HoldPt("25", parked);
        double dh = Dist(parked.X, parked.Z, hpt.X, hpt.Z);
        var toHold = Roll(twPh, parked.X, parked.Z, hpt.X, hpt.Z, 2, (int)(dh / 10) + 10);
        Check(toHold.Count >= 2 && toHold[^1].Contains("holding point runway two five") && toHold[^1].Contains("hold short of runway two five") &&
              toHold.Take(toHold.Count - 1).All(m => m.Contains("now")), "progressive taxi zum Haltepunkt: " + string.Join(" | ", toHold));
        r = twPh.OnTranscript("Kutaisi Ground, Enfield 11, request progressive taxi", At(hpt.X - 300, hpt.Z), none, 400);
        Check(r.Count == 1 && r[0].Contains("holding point runway two five at your"), "progressive taxi auf Wunsch im Rollen: " + r.FirstOrDefault()?.Text);
        // R6: after landing standing 40 s on the taxiway -> not parked (taxi guidance continues); 30 s within 100 m of a parking position (apron) -> parked
        Tower Landed() { var l = new Tower("Enfield 1-1"); l.Tick(At(CX, CZ, 300, 254, 80), none, 0); l.Tick(At(CX, CZ, 0, 254, 20), none, 5); l.Tick(At(CX, CZ, 0, 254, 5), none, 8); return l; }
        var twPk = Landed();
        var (twyX, twyZ) = (CX - 400 * Uz, CZ + 400 * Ux);
        twPk.F.Spots.Add((twyX - 900 * Uz, twyZ + 900 * Ux, 4, 72));
        for (int s = 10; s <= 60; s += 5) twPk.Tick(At(twyX, twyZ), none, s);
        bool pkTwy = twPk.Phase == Phase.TaxiIn;
        twPk.Tick(At(twyX - 500 * Uz, twyZ + 500 * Ux, 0, 0, 5), none, 62);
        for (int s = 65; s <= 100; s += 5) twPk.Tick(At(twyX - 860 * Uz, twyZ + 860 * Ux), none, s);
        twPk.F.Spots.RemoveAt(twPk.F.Spots.Count - 1);
        // N25 (user decision): engine off on the taxiway -> not parked, at the spot -> parked; 30 s standstill on the apron (80 m from the spot) -> parked
        var twEo = Landed();
        twEo.F.Spots.Add((twyX - 900 * Uz, twyZ + 900 * Ux, 4, 72));
        twEo.Tick(At(twyX, twyZ), none, 10);
        twEo.EngineOff();
        bool eoTwy = twEo.Phase == Phase.TaxiIn;
        twEo.Tick(At(twyX - 880 * Uz, twyZ + 880 * Ux, 0, 0, 3), none, 15);
        twEo.Tick(At(twyX - 880 * Uz, twyZ + 880 * Ux), none, 17);
        twEo.EngineOff();
        var twAp = Landed();
        twAp.F.Spots.Add((twyX - 900 * Uz, twyZ + 900 * Ux, 4, 72));
        twAp.Tick(At(twyX - 820 * Uz, twyZ + 820 * Ux), none, 10);
        twAp.EngineOff();
        bool apEo = twAp.Phase == Phase.TaxiIn;
        for (int s = 15; s <= 50; s += 5) twAp.Tick(At(twyX - 820 * Uz, twyZ + 820 * Ux), none, s);
        Check(pkTwy && twPk.Phase == Phase.Parked && eoTwy && twEo.Phase == Phase.Parked && apEo && twAp.Phase == Phase.Parked,
              $"R6/N25 geparkt: Rollweg {pkTwy}, Parkposition {twPk.Phase}, Triebwerk aus Rollweg {eoTwy}/Stand {twEo.Phase}, Vorfeld sofort {apEo}/30 s {twAp.Phase}");
        // R263: 30 s standstill at the spot with 10 kt headwind (export IAS shows the wind) = parked
        var tw263 = Landed();
        tw263.F.Spots.Add((twyX - 900 * Uz, twyZ + 900 * Ux, 4, 72));
        for (int s = 10; s <= 50; s += 5) tw263.Tick(At(twyX - 880 * Uz, twyZ + 880 * Ux, 0, 0, 10 * Kt) with { WindX = 10 * Kt }, none, s);
        Check(tw263.Phase == Phase.Parked, $"R263 Stand mit Gegenwind geparkt: {tw263.Phase}");
        // R6: on the apron 40 s waiting for "continue taxi" (give way) -> not parked, afterwards "continue taxi" (before parked, no "continue taxi")
        var twGw = Landed();
        twGw.F.Spots.Add((twyX - 900 * Uz, twyZ + 900 * Ux, 4, 72));
        var apron = At(twyX - 820 * Uz, twyZ + 820 * Ux, 0, 0, 6);
        var gwCross = new Traffic(9, "FA-18C_hornet", apron.X + 80, apron.Z + 80, FieldElev, 270 * Math.PI / 180, 8, "AI", "arr", false);
        twGw.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi to parking", apron, none, 9);   // R262: give way only with taxi clearance
        r = twGw.Tick(apron, new[] { gwCross }, 10);
        bool gwHold = r.Count == 1 && r[0].Contains("give way");
        for (int s = 12; s <= 52; s += 5) twGw.Tick(apron with { Ias = 0 }, new[] { gwCross with { X = apron.X } }, s);
        var gwPh = twGw.Phase;
        r = twGw.Tick(apron with { Ias = 0 }, none, 55);
        Check(gwHold && gwPh == Phase.TaxiIn && r.Count == 1 && r[0].Contains("continue taxi"), $"R6 give way auf dem Vorfeld: Halt {gwHold}, {gwPh}, {r.FirstOrDefault()?.Text ?? "-"}");

        // --- Final without first call. R11: without first call (Away) only with landing features (before, clearance also in level flight
        // with gear up), Tower introduces itself, text for the debriefing
        var tw4 = new Tower("Enfield 1-1");
        var onRwy = new Traffic(9, "F-16", CX, CZ, FieldElev + 2, 0, 25);
        r = tw4.Tick(onFinal, new[] { onRwy }, 0);
        Check(r.Count == 0 && tw4.Phase == Phase.Away, "R11: Endanflug ohne Landemerkmale -> still: " + (r.FirstOrDefault()?.Text ?? "-"));
        // Reporting obligation #5: with landing features without any contact never a landing clearance (before, "cleared to land" as soon as the runway was free), below 1 NM go around and violation
        var onFinalGd = onFinal with { Gear = 1 };
        r = tw4.Tick(onFinalGd, new[] { onRwy }, 1).Concat(tw4.Tick(onFinalGd, none, 2)).ToList();
        Check(r.Count == 0 && tw4.Phase == Phase.Away, "Meldepflicht ohne Kontakt 2,5 NM: keine Landefreigabe: " + string.Join(" | ", r.Select(m => m.Text)));
        var (u08x, u08z) = P("25", 0.8 * NM);
        var shortGd = At(u08x, u08z, 0.8 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 1 };
        r = tw4.Tick(shortGd, none, 3);
        var tw4u = new Tower("Enfield 1-1") { Unknown = true };   // N51: callsign unknown
        tw4u.Tick(onFinal, none, 0);
        var r4u = tw4u.Tick(shortGd, none, 1);
        Check(tw4.Phase == Phase.Away && r.Count == 2 && r[0].Role == "Tower" && r[0].Text == "Enfield one one, Kutaisi Tower, go around, you are not cleared to land." &&
              r[1].Role == "Info" && r[1].Text.StartsWith(L("Verstoß: Anflug ohne Kontakt Kutaisi 25", "Deviation: approach without contact Kutaisi 25")) &&
              r4u.FirstOrDefault()?.Text.StartsWith("Aircraft on final runway two five, Kutaisi Tower, go around, you are not cleared to land.") == true,
              "Meldepflicht ohne Kontakt unter 1 NM: go around, Verstoß: " + string.Join(" | ", r.Concat(r4u).Select(m => m.Text)));

        // --- Wind turns runway to 07 (east wind)
        var tw5 = new Tower("Enfield 1-1");
        tw5.Tick(parked with { WindZ = -6 }, none, 0);
        Check(tw5.Runway == "07", "Ostwind -> Bahn 07");
        r = tw5.OnTranscript("Enfield 11 request taxi", parked with { WindZ = -6 }, none, 1);
        Check(r[0].Contains("runway zero seven via November, Alpha"), r[0]);
        Check(Wind(parked with { WindZ = 10 }).StartsWith("wind two six zero degrees, one niner knots"), "Windansage magnetisch");

        Check(new Tower("Enfield 1-1").OnTranscript("blah blah", parked, none, 0)[0].StartsWith("Station calling"), "Unbekanntes");
        Check(new Tower("Enfield 1-1").SayAgain("Kutaisi Tower, blorf garble") is [{ Role: "Tower", Text: "Enfield one one, Kutaisi Tower, say again." }], "Unsicher verstanden -> say again");
        var twPl = new Tower("Enfield 1-1") { last = new Msg("Approach", "Enfield one one, turn left heading two seven zero.") };
        Check(twPl.Plain("Copied.") && twPl.Plain("Enfield 1-1, say again") && !twPl.Plain("Kutaisi Tower, blorf garble"), "Unsicher, aber Quittung/say again -> kein Rückfragen");

        // R39 · R48: Enter suggestion only if it fits (null = none): IMC first IFR clearance, "ready" only at the holding point, nothing after takeoff/landing/taxi-in clearance, no CRP in holding
        var twSg = new Tower("Enfield 1-1");
        twSg.Tick(parked, none, 0);
        ForceIfr = true; var sg = new List<string?> { twSg.Suggest(parked)?.Text }; ForceIfr = false;
        twSg.OnTranscript("Enfield 11 request taxi", parked, none, 1);
        sg.Add(twSg.Suggest(parked)?.Text); sg.Add(twSg.Suggest(hold)?.Text);
        twSg.OnTranscript("Enfield 11 ready for departure", hold, none, 2);
        sg.Add(twSg.Suggest(hold)?.Text + twSg.Phase);
        (twSg.Phase, twSg.taxiInTold) = (Phase.TaxiIn, true); sg.Add(twSg.Suggest(At(CX, CZ))?.Text);
        twSg.Phase = Phase.ClearedLand; sg.Add(twSg.Suggest(onFinal)?.Text);
        (twSg.Phase, twSg.holding) = (Phase.Inbound, true); sg.Add(twSg.Suggest(west)?.Text);
        (twSg.holding, twSg.entry) = (false, "south"); sg.Add(twSg.Suggest(west)?.Text); sg.Add(twSg.Suggest(atSouth)?.Text);
        (twSg.Phase, twSg.nav, twSg.navName) = (Phase.Entering, P("25", InitialDist), "initial"); sg.Add(twSg.Suggest(west)?.Text);
        (twSg.Phase, twSg.Following) = (Phase.Away, true); sg.Add(twSg.Suggest(west)?.Text);
        Check(string.Join(" | ", sg) == "request IFR clearance |  | ready for departure | ClearedTakeoff |  |  | say again | say again | crp | inbound | cancel flight following", "R39/R48 Vorschläge: " + string.Join(" | ", sg));
        // Initial: no "final" (otherwise landing clearance before the break), nothing on initial (also 7 NM), "base" after the break; "base" already 4 NM on initial results in
        // no landing clearance in the 3-NM zone; pattern: "final" only on final
        var twB = new Tower("Enfield 1-1");
        var atIp = At(ipPos.Item1, ipPos.Item2, 600, 254, 150);
        var (i4, i7) = (P("25", 4 * NM), P("25", 7 * NM));
        var (at4, at7i) = (At(i4.Item1, i4.Item2, 600, 254, 150), At(i7.Item1, i7.Item2, 600, 254, 150));
        twB.Tick(at4, none, 0);
        twB.Phase = Phase.Entering;
        twB.OnTranscript("Kutaisi Tower Enfield 11 initial", at4, none, 1);
        var sgB = $"{twB.Suggest(at7i)?.Text}/{twB.Suggest(at4)?.Text}/{twB.Suggest(atIp)?.Text}/{twB.Suggest(At(CX, CZ, 600, 74, 150))?.Text}";
        twB.OnTranscript("Kutaisi Tower, Enfield 11, base", at4, none, 2);
        var tB = twB.Tick(atIp, none, 10);
        bool initial = twB.Phase == Phase.Initial;
        twB.Phase = Phase.Pattern;
        Check(sgB == "///base, gear down, full stop" && initial && tB.All(m => !m.Text.Contains("cleared to land")) && twB.Suggest(At(CX, CZ, 600, 74, 150))?.Text == "base, gear down, full stop" && twB.Suggest(onFinal)?.Text == "final, gear down, full stop",
              $"R39 Initial -> {sgB}, {(initial ? "Initial" : "Pattern")}: " + string.Join(" | ", tB));
        twB.option = "touch and go";   // R260: the suggested base report carries the intention
        Check(twB.Suggest(At(CX, CZ, 600, 74, 150))?.Text == "base, gear down, touch and go", "R260 Base-Vorschlag mit Absicht: " + twB.Suggest(At(CX, CZ, 600, 74, 150))?.Text);
        twB.option = null;
        // A43/A44: "final" on initial -> "roger, report base"; "base" after the break (still initial) or in the pattern in base position = landing clearance;
        // gear up without "gear down" -> "check wheels down"; downwind/diverging heading stays "number one, report base"
        var (bsX, bsZ) = P("25", 1.2 * NM);
        var basePos = At(bsX - NM * Uz, bsZ + NM * Ux, 400, 344, 100);   // left of runway 25, across toward the centerline
        var twBs1 = new Tower("Enfield 1-1");
        twBs1.Tick(at4, none, 0);
        twBs1.Phase = Phase.Entering;
        twBs1.OnTranscript("Kutaisi Tower Enfield 11 initial", at4, none, 1);
        var rC = twBs1.OnTranscript("Kutaisi Tower, Enfield 11, final", at4, none, 2);
        bool cInit = twBs1.Phase == Phase.Initial && rC[0].Contains("roger, report base") && !rC[0].Contains("cleared");
        rC = twBs1.OnTranscript("Kutaisi Tower, Enfield 11, base, gear down, full stop", basePos with { Gear = 1 }, none, 3);
        Check(cInit && twBs1.Phase == Phase.ClearedLand && rC[0].Contains("runway two five") && rC[0].Contains("cleared to land"), $"A43/A44 final auf dem Initial, base nach dem Break (Initial: {cInit}): " + rC[0]);
        // A43: without break straight in on short final (gear down): "final" gives the landing clearance (before, loop "report base"/"report final"); reporting obligation: without call only "report final"
        var (sfX, sfZ) = P("25", NM);
        var shortFin = At(sfX, sfZ, 100, Math.Atan2(Uz, Ux) * 180 / Math.PI + 180, 70) with { Gear = 1 };
        Tower Init() { var w = new Tower("Enfield 1-1"); w.Tick(at4, none, 0); w.Phase = Phase.Entering; w.OnTranscript("Kutaisi Tower Enfield 11 initial", at4, none, 1); return w; }
        var twSf = Init();
        bool siInit = twSf.Phase == Phase.Initial;
        var rSi = twSf.OnTranscript("Kutaisi Tower, Enfield 11, final", shortFin, none, 2);
        var twSt = Init();
        var rSt = twSt.Tick(shortFin, none, 2);
        Check(siInit && twSf.Phase == Phase.ClearedLand && rSi[0].Contains("cleared to land") && twSt.Phase == Phase.Pattern && rSt.Count == 1 && rSt[0].Text == "Enfield one one, report final.",
              $"A43 geradeaus ohne Break im kurzen Endanflug: {rSi.FirstOrDefault()?.Text} | {string.Join(" | ", rSt)}");
        Tower Pat(Telemetry at) { var w = new Tower("Enfield 1-1"); w.Tick(at, none, 0); w.Phase = Phase.Pattern; return w; }
        // A99: traffic without "arr" flag crosses the pattern (0.3 NM ahead, opposite): traffic info once, "arr" traffic and the same aircraft not
        var (pvX, pvZ) = P("25", 3 * NM);
        var pvMe = At(pvX, pvZ, 500 * Ft, 0, 70);
        Traffic PvT(int id, string flag) => new(id, "MiG-29A", pvX + 0.3 * NM, pvZ, FieldElev + 500 * Ft, Math.PI, 100, "AI", flag, true);
        var twPv = Pat(pvMe);
        var pv1 = twPv.Tick(pvMe, new[] { PvT(70, "") }, 5);
        var pv2 = twPv.Tick(pvMe, new[] { PvT(70, "") }, 6);
        var pv3 = Pat(pvMe).Tick(pvMe, new[] { PvT(71, "arr") }, 5);
        Check(pv1.Count == 1 && pv1[0].Contains("traffic 12 o'clock") && pv1[0].Contains("no contact with Tower") && !pv2.Any(m => m.Text.Contains("traffic 12")) && !pv3.Any(m => m.Text.Contains("traffic 12")),
              $"A99 Platzrunde Verkehrsinfo: {pv1.FirstOrDefault()?.Text} | nochmal: {pv2.FirstOrDefault()?.Text} | arr: {pv3.FirstOrDefault()?.Text}");
        // R206: flight direction and speaking name instead of raw name; crossing from left to right (own heading 0, traffic toward +Z = heading 90)
        var pvX2 = Pat(pvMe).Tick(pvMe, new[] { new Traffic(73, "FA-18C_hornet", pvX + 0.4 * NM, pvZ - 0.3 * NM, FieldElev + 500 * Ft, Math.PI / 2, 100, "AI", "", true) }, 5).FirstOrDefault(m => m.Text.Contains("traffic"))?.Text;
        Check(pv1[0].Contains("mile, southbound, MiG 29A, ") && pvX2 != null && pvX2.Contains("eastbound, Hornet") && pvX2.Contains("crossing left to right, no contact"), $"R206 Verkehrshinweis Platzrunde: {pv1[0]} | {pvX2}");
        // A99: AI departure from here and other players at this tower have contact (silent), player at another airfield / AI departure from there not
        string Pv(string flag) => string.Join(" | ", Pat(pvMe).Tick(pvMe, new[] { PvT(72, flag) }, 5).Where(m => m.Text.Contains("traffic 12")));
        var pvIn = new[] { "dep-go:Kutaisi", "dep", "twr:Kutaisi", "hold:Kutaisi" }.Select(Pv).ToList();
        var pvOut = new[] { "twr:Senaki-Kolkhi", "dep-go:Senaki-Kolkhi" }.Select(Pv).ToList();
        Check(pvIn.All(s => s == "") && pvOut.All(s => s.Contains("no contact with Tower")), $"A99 Kontakt-Verkehr still: {string.Join(" / ", pvIn)} | fremd: {string.Join(" / ", pvOut)}");
        var twBs2 = Pat(basePos);
        var gD =twBs2.OnTranscript("Kutaisi Tower, Enfield 11, base, full stop", basePos with { Gear = 0 }, none, 1);
        bool gUp = twBs2.Phase == Phase.Pattern && gD[0].Contains("check wheels down") && !gD[0].Contains("cleared");
        gD = twBs2.OnTranscript("Kutaisi Tower, Enfield 11, base, gear down, full stop", basePos with { Gear = 0 }, none, 2);
        Check(gUp && twBs2.Phase == Phase.ClearedLand && gD[0].Contains("cleared to land"), $"A44 Fahrwerk oben ohne gear down -> check wheels down ({gUp}), mit gear down: " + gD[0]);
        var twBs3 = Pat(basePos);   // Gear unknown (-1): no check
        var twBs4 = Pat(basePos with { Hdg = 74 * Math.PI / 180 });   // Downwind (no heading toward the centerline): as before
        var fE = twBs3.OnTranscript("Kutaisi Tower, Enfield 11, base", basePos, none, 1);
        var fF = twBs4.OnTranscript("Kutaisi Tower, Enfield 11, base", basePos with { Hdg = 74 * Math.PI / 180 }, none, 1);
        Check(twBs3.Phase == Phase.ClearedLand && fE[0].Contains("cleared to land") && twBs4.Phase == Phase.Pattern && fF[0].Contains("number one, report base"),
              $"A44 base ohne Fahrwerkdaten: {fE[0]} | im Gegenanflug: {fF[0]}");
        var twBs5 = Pat(basePos);   // Runway occupied: base report gets "continue approach", no clearance
        var fG = twBs5.OnTranscript("Kutaisi Tower, Enfield 11, base, gear down", basePos with { Gear = 1 }, new[] { onRwy }, 1);
        Check(twBs5.Phase == Phase.Pattern && fG[0].Contains("continue approach, traffic on runway"), "A44 base bei belegter Bahn: " + fG[0]);

        // --- A1: readback (instruction/values of the last transmission) -> silent, also with "contact Kutaisi Tower" in the middle of the transmission and after 40 s
        string Rb(Tower w, Telemetry p, double at, params string[] ss) => string.Join(" | ", ss.SelectMany(s => w.OnTranscript(s, p, none, at)).Select(m => m.Text));
        var twRb = new Tower("Enfield 1-1");
        twRb.Tick(parked, none, 0);
        twRb.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 1);
        var rb = Rb(twRb, parked, 45, "Taxi to holding point runway 25 via November, Delta, hold short, contact Kutaisi Tower when ready for departure, Enfield 1-1",
                    "Holding point runway 25 via November, Delta, hold short runway 25, Enfield 1-1", "Hold short runway 25, Enfield 1-1");
        Check(rb == "" && twRb.Phase == Phase.TaxiOut && twRb.Rejected == 0, "Rollfreigabe zurückgelesen -> still " + rb);
        var twRa = new Tower("Enfield 1-1");
        twRa.Tick(west, none, 0);
        twRa.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", west, none, 1);
        var vecPh = twRa.Phase;
        twRa.last = new Msg("Approach", "Enfield one one, turn left heading zero four five, descend and maintain 2000 feet, squawk seven one seven one, QNH one zero one niner, " +
                                        "proceed overhead, contact Kutaisi Tower, report overhead, cleared I L S approach runway two five.");
        rb = Rb(twRa, west, 2, "Left heading 045, maintain 2000, Enfield 1-1", "Squawk 7171, Enfield 1-1", "QNH one zero one niner, Enfield 1-1",
                "Cleared ILS approach runway 25, Enfield 1-1", "Proceed overhead, 2000, contact Tower, report overhead, Enfield 1-1");
        Check(rb == "" && twRa.Phase == vecPh && twRa.vec != null && twRa.Rejected == 0, "Rücklesungen in der Luft -> still " + rb);
        Check(Rb(twRa, west, 3, "Enfield 1-1, QNH").Contains("QNH") && Rb(twRa, west, 4, "Enfield 1-1, initial runway 25") != "", "Frage/Meldung mit Wert -> Antwort");
        twRa.last = new Msg("Approach", "Enfield one one, radar service terminated, squawk VFR, frequency change approved.");
        Check(Rb(twRa, west, 5, "Radar service terminated, squawk VFR, frequency change approved, Enfield 1-1") == "" && !twRa.Following, "Abmeldung zurückgelesen -> still");
        var twRv = new Tower("Enfield 1-1");   // real vector transmission ("…, downwind"): readback with "downwind" is not a pattern report
        twRv.Tick(west, none, 0);
        var vRb = HdgSlot(Normalize(twRv.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", west, none, 1)[0].Text));
        Check(Rb(twRv, west, 5, $"Left heading {vRb:000}, maintain 2000, downwind, Enfield 1-1") == "" && twRv.Rejected == 0, "Kurs zurückgelesen (mit downwind) -> still");
        twRv.last = new Msg("Approach", "Enfield one one, Kutaisi Approach, radar contact, 1 mile west of the field. Resume own navigation, maintain VFR.");
        Check(Rb(twRv, west, 6, "Maintain VFR, Enfield 1-1") == "", "maintain VFR zurückgelesen -> still");
        {   // Forum 0.9.7: acknowledgement "start up approved, <callsign>" / just the callsign (AIM 4-2-3) / "starting up" … -> silent, no "say again", no second clearance (chain startup -> taxi -> takeoff, readback also after 40 s)
            var tq = new Tower("Enfield 1-1");
            tq.Tick(parked, none, 0);
            tq.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", parked, none, 1);
            var q1 = Rb(tq, parked, 5, "Enfield 1-1") + Rb(tq, parked, 45, "Startup approved, Enfield 1-1") + Rb(tq, parked, 46, "Approved, Enfield 1-1") + Rb(tq, parked, 47, "Starting up, Enfield 1-1") + Rb(tq, parked, 48, "Ground: Enfield 1-1");
            var q2 = tq.OnTranscript("Enfield 1-1, ready to taxi", parked, none, 60);
            var sq0 = tq.Squawk;
            var q3 = Rb(tq, parked, 95, "Taxi to holding point runway 25 via November Delta, Enfield 1-1") + Rb(tq, parked, 96, "Taxiing, Enfield 1-1");   // 35 s: taxi clearance (~12 s spoken) plus readback
            tq.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", hold, none, 200);
            var q4 = Rb(tq, hold, 205, "Rolling, Enfield 1-1");
            Check(q1 == "" && q2 is [{ } qtc] && qtc.Text.Contains("taxi to holding point") && q3 == "" && tq.Squawk == sq0 && q4 == "" && tq.Phase == Phase.ClearedTakeoff && tq.Rejected == 0,
                  $"Forum 0.9.7 Quittungen am Boden still: {q1} / {string.Join(" | ", q2.Select(x => x.Text))} / {q3} / {q4}");
            var tf = new Tower("Enfield 1-1");
            tf.Tick(west, none, 0);
            tf.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", west, none, 1);
            tf.last = new Msg("Approach", "Enfield one one, squawk four three two one, QNH one zero one three, contact Kutaisi Tower one three four decimal zero.");
            var q5 = Rb(tf, west, 3, "Tower 134.0, Enfield 1-1", "Switching, Enfield 1-1", "One three four decimal zero, Enfield 1-1", "4321, 1013, Enfield 1-1", "Squawking 4321, Enfield 1-1", "Altimeter 1013, Enfield 1-1");
            Check(q5 == "" && Rb(tf, west, 4, "Squawk 4322, Enfield 1-1") != "" && tf.Rejected == 0, "Forum 0.9.7 Frequenz/Code/QNH nur als Werte bzw. \"switching\" zurückgelesen -> still, falscher Code -> Antwort: " + q5);
        }
        {   // Forum 0.9.7 (Nellis): parallel runways 300 m apart, apron left of 03L, assigned 03R: taxi clearance "hold short runway 03L", no takeoff clearance at holding point 03L
            // (also not on "ready for departure"), radio wheel suggests crossing; runway occupied -> hold short, free -> crosses on its own; crossing without "not cleared onto runway"; at holding point 03R takeoff clearance
            double h = 30 * Math.PI / 180, ndx = Math.Cos(h), ndz = Math.Sin(h), x0 = -300000, z0 = 600000;
            var nel = Airfield.FromDcs("Nellis", x0, z0, 570, new[] { ("03", -h, x0, z0, 3000.0), ("03", -h, x0 - 150, z0 + 259.8, 3000.0) });
            (double X, double Z) Pt(double along, double left) => (x0 + along * ndx + left * 0.5, z0 + along * ndz - left * 0.866);   // left of 03L: (0.5, -0.866)
            Telemetry NT((double X, double Z) p, double ias = 0) => new(nel.Elev, 0, ias, h, p.X, p.Z, 0, 0, 760);
            var (np, nh03L, non03L, nh03R) = (NT(Pt(-1000, 600)), NT(Pt(-1400, 90)), NT(Pt(-1400, 0), 5), NT(Pt(-1400, -210)));
            var tn = new Tower(nel, "Enfield 1-1");
            tn.Tick(np, none, 0);
            tn.OnTranscript("Nellis Ground, Enfield 1-1, request startup", np, none, 1);
            var n1 = tn.OnTranscript("Nellis Ground, Enfield 1-1, request taxi, runway 03 right", np, none, 20);
            tn.Tick(nh03L, none, 100);
            var nsg = tn.Suggest(nh03L);
            var nroll = new Traffic(7, "F-16C_50", Pt(-1000, 0).X, Pt(-1000, 0).Z, nel.Elev + 1, h, 40, InAir: false);
            var n2 = tn.OnTranscript("Nellis Tower, Enfield 1-1, ready for departure", nh03L, new[] { nroll }, 101);
            var n3 = tn.Tick(nh03L, none, 110);
            var n4 = tn.Tick(non03L, none, 130);
            var ph4 = tn.Phase;
            var n5 = tn.OnTranscript("Nellis Tower, Enfield 1-1, ready for departure", nh03R, none, 160);
            Check(nel.Ends.Select(e => e.Name).OrderBy(s => s).SequenceEqual(new[] { "03L", "03R", "21L", "21R" }) && n1 is [{ } t1] && t1.Text.Contains("taxi to holding point runway zero three right, hold short runway zero three left") &&
                  nsg is ("Ground", "holding short runway zero three left, request crossing") && n2 is [{ } t2] && t2.Text.Contains("hold short runway zero three left, traffic") &&
                  n3 is [{ } t3] && t3.Text.Contains("cross runway zero three left, hold short runway zero three right") && n4.Count == 0 && ph4 == Phase.TaxiOut &&
                  n5 is [{ } t5] && t5.Text.Contains("runway zero three right") && t5.Text.Contains("cleared for takeoff") && tn.Phase == Phase.ClearedTakeoff,
                  $"Forum 0.9.7 Parallelbahn kreuzen: {string.Join(",", nel.Ends.Select(e => e.Name))} / {string.Join(" | ", n1.Select(m => m.Text))} / {nsg} / {string.Join(" | ", n2.Concat(n3).Concat(n4).Concat(n5).Select(m => m.Text))}");
            var tn2 = new Tower(nel, "Enfield 1-1");   // ENTER with "ready for departure" (expectation from the taxi clearance) at holding point 03L: cross instead of takeoff clearance
            tn2.Tick(np, none, 0);
            tn2.OnTranscript("Nellis Ground, Enfield 1-1, request taxi, runway 03 right", np, none, 1);
            tn2.Tick(nh03L, none, 100);
            var n6 = tn2.OnTranscript("Ground: ready for departure", nh03L, none, 101);
            Check(n6 is [{ } t6] && t6.Text.Contains("cross runway zero three left, hold short runway zero three right") && tn2.Phase == Phase.TaxiOut, "Forum 0.9.7 'ready for departure' am Haltepunkt der Parallelbahn: " + string.Join(" | ", n6.Select(m => m.Text)));
        }
        // --- A2: Unintelligible -> ask again without overwriting the last call, "say again" repeats the taxi clearance
        r = twRb.OnTranscript("Blorf garble, Enfield 1-1", parked, none, 50);
        Check(r is [{ Role: "Ground", Text: "Enfield one one, say again." }], "Unverständlich mit Rufzeichen: " + r[0]);
        r = twRb.OnTranscript("blorf garble", parked, none, 51);
        Check(r is [{ Text: "Station calling Kutaisi Ground, say again your callsign." }], "Unverständlich ohne Rufzeichen: " + r[0]);
        r = twRb.OnTranscript("Say again", parked, none, 52);
        Check(r.Count == 1 && r[0].StartsWith("Enfield one one, I say again, taxi to holding point runway two five via November, Delta"), "say again danach: " + r[0]);
        twRb.last = new Msg("Tower", "Enfield one one, runway two five, line up and wait, traffic on the runway.");
        Check(Rb(twRb, hold, 60, "Line up and wait runway 25, Enfield 1-1") == "" && Rb(twRb, hold, 61, "Line up and wait runway 07, Enfield 1-1") != "", "line up and wait zurückgelesen -> still, andere Bahn -> Antwort");

        // --- A27: Initial call with only station + callsign (+ altitude, "information X") -> "Enfield 1-1, Kutaisi Tower." instead of weather report/"say again"; after handoff (Entering) the instructions follow right away
        var twCu = new Tower("Enfield 1-1");
        twCu.Tick(parked, none, 0);
        r = twCu.OnTranscript("Kutaisi Ground, Enfield 1-1", parked, none, 1);
        Check(r is [{ Role: "Ground", Text: "Enfield one one, Kutaisi Ground." }], "A27 Erstanruf Ground: " + string.Join(" | ", r));
        var twCw = new Tower("Enfield 1-1");
        twCw.Tick(west, none, 0);
        r = twCw.OnTranscript("Switching to tower, Enfield 1-1, 2000 feet, information Charlie", west, none, 2);
        Check(r is [{ Role: "Tower", Text: "Enfield one one, Kutaisi Tower." }], "A27 Erstanruf Tower mit Höhe und Kennung (keine Wetteransage): " + string.Join(" | ", r));
        Check(twCw.OnTranscript("Kutaisi Tower, Enfield 1-1, request QNH", west, none, 3) is [{ } qr] && qr.Contains("QNH") && !qr.Contains("Station calling"), "A27 mit Anliegen weiter beantwortet");
        Check(twCw.OnTranscript("Kutaisi Tower, Enfield 1-1, good day", west, none, 4) is [{ Text: "Enfield one one, good day." }], "A27 Abschied bleibt Abschied");
        var twCe = new Tower("Enfield 1-1");
        twCe.Tick(at7, none, 0);
        twCe.Phase = Phase.Entering;   // handed over by Approach
        r = twCe.OnTranscript("Kutaisi Tower, Enfield 1-1", at7, none, 1);
        Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Contains("Kutaisi Tower, wind") && r[0].Contains("report initial runway two five") && twCe.Phase == Phase.Entering,
              "A27 Erstanruf nach der Übergabe -> Anweisungen: " + string.Join(" | ", r));
        r = twCe.OnTranscript("Kutaisi Tower, Enfield 1-1, 2000 feet, information Charlie", at7, none, 2);
        Check(r.Count == 1 && r[0].Contains("report initial runway two five") && !r[0].Contains("Information"), "A27 mit Höhe/Kennung nach der Übergabe: " + string.Join(" | ", r));

        // --- A28: "unable" (not "unable to copy") -> acknowledge even if the heading matches the readback; radar vectoring: hold altitude; ground: hold position
        var twUn = new Tower("Enfield 1-1");
        twUn.Tick(west, none, 0);
        var uh = HdgSlot(Normalize(twUn.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", west, none, 1)[0].Text));
        r = twUn.OnTranscript($"Unable heading {uh:000}, terrain, Enfield 1-1", west, none, 3);
        Check(twUn.Vectoring && r is [{ Role: "Approach" }] && r[0].Text.StartsWith("Enfield one one, roger, maintain ") && r[0].Text.EndsWith(", say intentions."), "A28 unable unter Radarführung (Kurs wie der Lotse): " + string.Join(" | ", r));
        r = twUn.OnTranscript("Unable, Enfield 1-1", west, none, 4);
        Check(r.Count == 1 && r[0].Text.EndsWith(", say intentions."), "A28 unable gleich nochmal wird nicht als Wiederholung geschluckt: " + string.Join(" | ", r));
        var twUg = new Tower("Enfield 1-1");
        twUg.Tick(parked, none, 0);
        r = twUg.OnTranscript("Unable, Enfield 1-1", parked, none, 1);
        Check(r is [{ Role: "Ground", Text: "Enfield one one, roger, hold position." }], "A28 unable am Boden: " + string.Join(" | ", r));
        var twUa = new Tower("Enfield 1-1");
        twUa.Tick(west, none, 0);
        r = twUa.OnTranscript("Unable, Enfield 1-1", west, none, 1);
        Check(r is [{ Text: "Enfield one one, roger, say intentions." }], "A28 unable in der Luft ohne Radarführung: " + string.Join(" | ", r));
        r = twUa.OnTranscript("Unable to copy, Enfield 1-1", west, none, 40);
        Check(r.Count == 1 && r[0].Contains("I say again"), "A28 'unable to copy' bleibt say again (wiederholt den letzten Spruch): " + string.Join(" | ", r));

        // --- A140: flight-internal radio ("Two, go trail", "Enfield 1-2, check fuel") without station and own callsign -> silent; unintelligible with own callsign/without callsign as before
        var twCh = new Tower("Enfield 1-1");
        twCh.Tick(parked, none, 0);
        Check(twCh.OnTranscript("Two, go trail", parked, none, 1).Count == 0 && twCh.OnTranscript("Three, check fuel", parked, none, 2).Count == 0 &&
              twCh.OnTranscript("Enfield 1-2, check fuel", parked, none, 3).Count == 0, "A140 Rottenfunk still");
        r = twCh.OnTranscript("Two, go trail, Enfield 1-1", parked, none, 4);
        Check(r is [{ Text: "Enfield one one, say again." }], "A140 eigenes Rufzeichen im Spruch -> Antwort: " + string.Join(" | ", r));
        r = twCh.OnTranscript("Kutaisi Ground, two, go trail", parked, none, 5);
        Check(r.Count == 1, "A140 mit Station -> Antwort: " + string.Join(" | ", r));
        r = twCh.OnTranscript("Two thousand feet, blorf", parked, none, 6);
        Check(r.Count == 1, "A140 'two thousand' ist keine Rottennummer: " + string.Join(" | ", r));
        var twCf = new Tower("Enfield 1-1") { CallsignFixed = true };
        twCf.Tick(parked, none, 0);
        Check(Enumerable.Range(1, 3).All(i => twCf.OnTranscript("Enfield 1-2, check fuel", parked, none, i).Count == 0), "A140 festes Rufzeichen: fremdes Rufzeichen bleibt fremd");
        Check(twCh.OnTranscript("Two, unable", parked, none, 7).Count == 0 && twUa.OnTranscript("Two, unable, tally lost", west, none, 60).Count == 0, "A140 Rottenfunk mit unable still");

        // --- Numbers and callsigns (V2)
        double? A(string s) => AltSlot(Normalize(s));
        Check(A("request flight level one five zero") == 15000 && A("FL150") == 15000 && A("angels 15") == 15000 && A("cherubs five") == 500
              && A("climbing 6,500") == 6500 && A("4,000 feet") == 4000 && A("six thousand five hundred") == 6500 && A("Enfield 1-1, request taxi") == null, "Zahlen: Höhe");
        Check(HdgSlot(Normalize("request heading two seven zero")) == 270 && HdgSlot(Normalize("heading 090")) == 90, "Zahlen: Kurs");
        Check(FuelSlot(Normalize("Texaco, Enfield 1-1, state 6.2")) == 6.2 && FuelSlot(Normalize("fuel state four point two")) == 4.2
              && FuelSlot(Normalize("201, Hornet ball, 5.3")) == 5.3 && FuelSlot(Normalize("state tree decimal fife")) == 3.5 && FuelSlot(Normalize("state 6200")) == 6.2, "Zahlen: Sprit");
        Check(RwySlot(Normalize("request runway three one")) == "31" && RwySlot(Normalize("runway 7, 4 miles")) == "07", "Zahlen: Bahn");
        Check(SameCallsign("Enfeld 1-1", "Enfield 1-1") && SameCallsign("Field 1-1", "Enfield 1-1") && !SameCallsign("Colt 1-1", "Enfield 1-1"), "Rufzeichen unscharf");
        Check(Normalize("Batumi Approach, Enfield 1-1, Enbound.") == "batumi approach enfield 1 1 inbound", "Verhörer: Enbound -> inbound");
        var twCs = new Tower("Enfield 1-1");
        twCs.OnTranscript("Kutaisi Ground, Enfeld 1-1, request startup", parked, none, 0);
        Check(twCs.Callsign == "Enfield 1-1", "Verhörtes Rufzeichen bleibt -> " + twCs.Callsign);

        // --- F10 menu: texts without callsign, no readback lock
        var tw6 = new Tower("Enfield 1-1");
        tw6.Tick(west, none, 0);
        r = tw6.OnTranscript("inbound for landing", west, none, 1, true);
        Check(r[0].StartsWith("Enfield one one") && r[0].Contains("heading"), "Menü inbound -> Radarführung: " + r[0]);
        var atCrp = At(Crp["north"].X, Crp["north"].Z, 600, 90, 150);
        r = tw6.OnTranscript("crp", atCrp, none, 2, true);
        Check(r[0].StartsWith("Enfield one one") && r[0].Contains("overhead"), "Menü CRP: " + r[0]);
        Check(tw6.OnTranscript("crp", atCrp, none, 3, true).Count == 1, "Menü zweimal gedrückt -> antwortet erneut");

        // --- Nonsense reports
        var tw7 = new Tower("Enfield 1-1");
        var farWest = At(-286000, 660000, 1500, 80, 150);
        tw7.Tick(farWest, none, 0);
        r = tw7.OnTranscript("Enfield 11 final", farWest, none, 1);
        Check(r[0].Contains("negative, I don't have you on final"), r[0]);
        r = tw7.OnTranscript("Enfield 11 overhead", farWest, none, 2);
        Check(r[0].Contains("Approach") && r[0].Contains("heading") && tw7.Phase == Phase.Inbound, "Overhead weit draußen -> Approach führt: " + r[0]);
        r = tw7.OnTranscript("Enfield 11 request taxi", farWest, none, 3);
        Check(r[0].Contains("you are airborne"), r[0]);
        var (f7x, f7z) = P("07", 3 * NM);
        r = tw7.OnTranscript("Kutaisi Tower, Enfield 11, final", At(f7x, f7z, 300, LandHdg("07"), 70), none, 4);
        Check(r[0].Contains("lined up for runway zero seven"), r[0]);

        // --- Runway without clearance
        var tw8 = new Tower("Enfield 1-1");
        tw8.Tick(parked, none, 0);
        r = tw8.Tick(At(CX, CZ, 0, 254, 5), none, 1);
        Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("hold position"), r.FirstOrDefault()?.Text ?? "Bahn betreten: -");
        r = tw8.OnTranscript("Holding position, Enfield 1-1", At(CX, CZ, 0, 254, 0), none, 1.5);   // R111: readback, no reply
        Check(r.Count == 0 && tw8.Rejected == 0, "R111 holding position nach Bahnbetretung: " + string.Join(" | ", r.Select(m => m.Text)));
        r = tw8.Tick(At(CX, CZ, 0, 254, 60), none, 2);
        Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("stop immediately"), r.FirstOrDefault()?.Text ?? "Startlauf: -");
        r = tw8.Tick(At(CX, CZ, 0, 254, 70), none, 3);   // Tower warnings during the lockout time: Ground (A40, taxiing without clearance) does not follow up
        Check(r.Count == 0, "Startlauf ohne Freigabe, kein Ground hinterher: " + string.Join(" | ", r));

        // --- N4: Violations: once "possible pilot deviation" and info "Verstoß: …" with mission clock (Program: Debriefing); AirspaceWatch off: silent
        {
            Carrier.Clock = 14 * 3600 + 2 * 60;
            var twN4 = new Tower("Enfield 1-1");
            twN4.Tick(parked, none, 0);
            r = twN4.Tick(At(CX, CZ, 150, 254, 80), none, 1);   // departed without a call
            Check(r.Count == 2 && r[0].Text.EndsWith("departed without takeoff clearance. Report intentions.") &&
                  r[1].Role == "Info" && r[1].Text == L("Verstoß: Start ohne Freigabe Kutaisi 25 (14:02)", "Deviation: takeoff without clearance Kutaisi 25 (14:02)"), "N4 Start ohne Freigabe: " + string.Join(" | ", r));
            r = twN4.Tick(At(CX, CZ + 2 * NM, 900, 254, 120), none, 5).Concat(twN4.Tick(At(CX, CZ + 3 * NM, 1200, 254, 120), none, 12)).ToList();   // R247: advisory separate, 10 s later in departure
            Check(r.Count(m => m.Text.StartsWith("Enfield one one, possible pilot deviation, advise you contact Kutaisi Tower at D S N three one four, ")) == 1 && r.Last().Role == "Approach", "R247 Hinweis im Abflug (nach Übergabe von Approach): " + string.Join(" | ", r));
            var twV2 = new Tower("Enfield 1-1");   // Takeoff roll and liftoff: one violation
            twV2.Tick(parked, none, 0);
            int n1 = twV2.Tick(At(CX, CZ, 0, 254, 60), none, 1).Count;
            r = twV2.Tick(At(CX, CZ, 150, 254, 80), none, 2);
            Check(n1 == 2 && r.Count == 1 && !r[0].Contains("deviation"), "N4 Startlauf + Abheben nur einmal: " + string.Join(" | ", r));
            var twV3 = new Tower("Enfield 1-1");   // Landing in the pattern without landing clearance
            twV3.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
            twV3.Phase = Phase.Pattern;
            twV3.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1); twV3.Tick(At(CX, CZ, 0, 254, 95 * Kt), none, 2);
            r = twV3.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3);
            Check(r.Count == 2 && r[0].Contains("vacate") && !r[0].Contains("deviation") &&
                  r[1].Text.StartsWith(L("Verstoß: Landung ohne Freigabe Kutaisi 25", "Deviation: landing without clearance Kutaisi 25")), "N4 Landung ohne Freigabe: " + string.Join(" | ", r));
            var twV4 = new Tower("Enfield 1-1");   // landed after "go around"
            twV4.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
            (twV4.Phase, twV4.wentAround) = (Phase.Pattern, true);
            twV4.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1); twV4.Tick(At(CX, CZ, 0, 254, 95 * Kt), none, 2);
            r = twV4.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3);
            Check(r.Count == 2 && r[1].Text.StartsWith(L("Verstoß: Landung nach Go-around", "Deviation: landing after go-around")), "N4 Landung nach Go-around: " + string.Join(" | ", r));
            Tower.Watch = false;
            var twN4w = new Tower("Enfield 1-1");   // without any call: no "departed without takeoff clearance", no line
            twN4w.Tick(parked, none, 0);
            r = twN4w.Tick(At(CX, CZ, 150, 254, 80), none, 1);
            Check(r.Count == 0 && twN4w.Phase == Phase.Away, "N4 aus, ohne Anruf gestartet: still: " + string.Join(" | ", r));
            var twW2 = new Tower("Enfield 1-1");   // with call: transmission, but no violation
            twW2.Tick(parked, none, 0);
            twW2.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 1);
            r = twW2.Tick(At(CX, CZ, 150, 254, 80), none, 2);
            Check(r.Count == 1 && r[0].Contains("departed without takeoff clearance") && !r[0].Contains("deviation"), "N4 aus, nach Anruf: nur der Spruch: " + string.Join(" | ", r));
            Tower.Watch = true;
            Carrier.Clock = -1;
        }

        // --- N2: Airspace watch: into the zone without contact (Kutaisi 9.1 NM) -> say intentions, after 30 s near the airfield send away, outside violation; silent with contact,
        // enemy under 20 NM, above CtrTopFt, in the first 60 s; AWACS: only pattern and runway; with landing cues on final landing clearance (R11) instead of redirect
        {
            Telemetry N(double nm, double hdg, double ft = 1500) => At(CX + nm * NM, CZ, ft * Ft - FieldElev, hdg, 250 * Kt);
            Telemetry F25(double nm, bool gear, double kt) { var (x, z) = P("25", nm * NM); return At(x, z, nm * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), kt * Kt) with { Gear = gear ? 1 : 0, Vs = gear ? -3 : 0 }; }
            var wa = new Tower("Enfield 1-1") { Aloft = 120 };   // new tower = reset (airfield change): still stage 1
            // N51b: 2.8 instead of 3 NM (pattern: under 3 NM; 3 NM north abeam the runway is now uninteresting), first call with redirect instead of "say intentions"
            r = wa.Tick(N(2.8, 180), none, 0);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, Kutaisi Tower, you are entering the Kutaisi control zone without clearance, 3 miles north of the field, turn left heading three five zero, remain clear of the control zone.",
                  "N2 Stufe 1 (Review l7: ohne Funkrad-Hinweis): " + string.Join(" | ", r));
            var wu = new Tower("Enfield 1-1") { Aloft = 120, Unknown = true };   // N51: never spoken to a controller yet -> situation instead of callsign, on guard "Kutaisi Tower on guard"
            var ru = wu.Tick(N(2.8, 180), none, 0);
            var gu = ru.Count > 0 ? Flights.OnGuard(ru[0].Text, "Kutaisi Tower", 263) : "";
            Check(ru.Count == 1 && Regex.IsMatch(ru[0].Text, @"^Unidentified aircraft 3 miles north of Kutaisi, heading \w+ \w+ \w+, 1500 feet, Kutaisi Tower, you are entering the Kutaisi control zone, identify yourself, turn left heading three five zero, remain clear of the control zone\.$") &&
                  gu.Contains(", Kutaisi Tower on guard, you are entering") && gu.Contains(". Contact Kutaisi Tower "), "N51 unbekannt: " + string.Join(" | ", ru) + " / " + gu);
            // R248: unknown again outside: no call ("leaving the control zone", "possible pilot deviation") to an unknown, violation only into the debriefing
            ru = Enumerable.Range(0, 9).SelectMany(i => wu.Tick(N(2.5 + i, 0), none, 35 + 5 * i)).ToList();
            Check(!ru.Any(m => m.Contains("leaving the control zone") || m.Contains("pilot deviation")) && ru.Any(m => m.Role == "Info" && m.Text.StartsWith(L("Verstoß: Luftraumverletzung", "Deviation: airspace violation"))), "R248 unbekannt draußen: " + string.Join(" | ", ru));
            r = wa.Tick(N(2.2, 180), none, 10);
            var r2 = wa.Tick(N(1.5, 190), none, 31);   // Heading 190: right turn to the north
            Check(r.Count == 0 && r2.Count == 1 && r2[0].Text.StartsWith("Enfield one one, Kutaisi Tower, turn right heading") && r2[0].Contains("leave the control zone immediately"),
                  "N2 Stufe 2 nach 30 s: " + string.Join(" | ", r.Concat(r2)));
            r = Enumerable.Range(0, 9).SelectMany(i => wa.Tick(N(2.5 + i, 0), none, 35 + 5 * i)).ToList();   // northbound out to 10.5 NM
            Check(r.Count == 2 && r[0].Text == "Enfield one one, Kutaisi Tower, leaving the control zone." &&
                  r[1].Text == L("Verstoß: Luftraumverletzung Kutaisi, 1500 ft, keine Antwort", "Deviation: airspace violation Kutaisi, 1500 ft, no reply"), "N2 Stufe 3 draußen: " + string.Join(" | ", r));
            foreach (var (why, w, tel) in new (string, Tower, Telemetry)[] { ("Kontakt", new("Enfield 1-1") { Aloft = 120, Exempt = true }, N(2.8, 180)), ("Feind 15 NM", new("Enfield 1-1") { Aloft = 120, HostileNm = 15 }, N(2.8, 180)),
                                                                            ("erste 60 s", new("Enfield 1-1") { Aloft = 30 }, N(2.8, 180)), ("über CtrTopFt", new("Enfield 1-1") { Aloft = 120 }, N(2.8, 180, 3500)),
                                                                            ("N51b seitlich 6 NM, 2800 ft", new("Enfield 1-1") { Aloft = 120 }, N(6, 180, 2800)), ("N51b über dem Platz 3100 ft", new("Enfield 1-1") { Aloft = 120 }, N(0, 180, 3100)),
                                                                            ("N51b 4,5 NM ohne Verkehr", new("Enfield 1-1") { Aloft = 120 }, N(4.5, 180)) })
                Check(w.Tick(tel, none, 0).Count == 0, "N2 still: " + why);
            var wt = new Tower("Enfield 1-1") { Aloft = 120, Tactical = true };   // checked in with AWACS
            r = wt.Tick(N(3, 180, 2000), none, 0).Concat(wt.Tick(N(4, 180, 2000), none, 40)).ToList();
            r2 = wt.Tick(At(CX, CZ, 500 * Ft, LandHdg("25") + 90, 250 * Kt), none, 45);
            Check(r.Count == 0 && r2.Count == 2 && r2[0].Text == "Enfield one one, Kutaisi Tower, you crossed the active runway without clearance." &&
                  r2[1].Text.StartsWith(L("Verstoß: Bahn ohne Freigabe überflogen Kutaisi", "Deviation: runway crossed without clearance Kutaisi")), "N2 AWACS: Zone frei, Bahnüberflug: " + string.Join(" | ", r.Concat(r2)));
            var wl = new Tower("Enfield 1-1") { Aloft = 120 };   // Gear down on final: level 1 ("say intentions") instead of redirect; reporting obligation: no landing clearance without a call, under 1 NM go around
            r = Enumerable.Range(0, 18).SelectMany(i => wl.Tick(F25(9 - i * 0.5, true, 160), none, i * 3)).ToList();
            Check(r.Any(m => m.Contains("without clearance") && m.Contains("say intentions")) && !r.Any(m => m.Contains(", cleared to land")) && !r.Any(m => m.Contains("leave the control zone")) &&
                  r.Count(m => m.Text == "Enfield one one, Kutaisi Tower, go around, you are not cleared to land.") == 1 &&
                  r.Any(m => m.Role == "Info" && m.Text.StartsWith(L("Verstoß: Anflug ohne Kontakt Kutaisi 25", "Deviation: approach without contact Kutaisi 25"))) && wl.Phase == Phase.Away,
                  "N2 Endanflug mit Fahrwerk: " + string.Join(" | ", r));
            // R294: MAYDAY announced by AWACS (Announce) without a call: no redirect in the zone, on final landing clearance instead of go around and violation
            var we = new Tower("Enfield 1-1") { Aloft = 120 };
            we.Announce(F25(9.5, true, 160), 0, true);
            r = Enumerable.Range(0, 18).SelectMany(i => we.Tick(F25(9 - i * 0.5, true, 160), none, 3 + i * 3)).ToList();
            Check(!r.Any(m => m.Contains("without clearance") || m.Contains("go around") || m.Role == "Info") && we.Phase == Phase.ClearedLand &&
                  r.Count(m => m.Text.StartsWith("Enfield one one, Kutaisi Tower, runway two five, wind") && m.Text.EndsWith("cleared to land, emergency services standing by.")) == 1,
                  "R294 angekündigter Notfall ohne Anruf: " + string.Join(" | ", r));
            var wf = new Tower("Enfield 1-1") { Aloft = 120 };   // 400 kt, gear up: no clearance, redirect
            r = Enumerable.Range(0, 9).SelectMany(i => wf.Tick(F25(9 - i, false, 400), none, i * 6)).ToList();
            // N51b follow-up: along final "on" instead of "crossing"; level 2 perpendicular away like level 1, not the approach line outward toward the traffic
            Check(r.Any(m => m.Contains("without clearance") && m.Contains(", on the final approach course runway two five, turn ")) && !r.Any(m => m.Contains("crossing")) &&
                  r.Any(m => m.Contains("leave the control zone immediately") && (m.Contains(wf.HdgSay(LandHdg("25") - 90) + ",") || m.Contains(wf.HdgSay(LandHdg("25") + 90) + ","))) && !r.Any(m => m.Contains("cleared to land")) && wf.Phase == Phase.Away,
                  "N2 Endanflug schnell ohne Fahrwerk: " + string.Join(" | ", r));
            // N51b: across the final (5 NM, on the glide path, 60° to the axis) -> perpendicular away, right turn; traffic in the pattern 2 NM ahead same altitude -> away from traffic; over the airfield just under the limit -> climb
            r = new Tower("Enfield 1-1") { Aloft = 120 }.Tick(F25(5, false, 250) with { Hdg = (LandHdg("25") + 60) * Math.PI / 180 }, none, 0);
            Check(r.Count == 1 && Regex.IsMatch(r[0].Text, @", you are entering the Kutaisi control zone without clearance, \d miles \w+ of the field, crossing the final approach course runway two five, turn right heading \w+ \w+ \w+ immediately, remain clear of the control zone\.$"),
                  "N51b Endanflug quer: " + string.Join(" | ", r));
            r = new Tower("Enfield 1-1") { Aloft = 120 }.Tick(N(4.5, 180), new[] { new Traffic(1, "F-16C_50", CX + 2.5 * NM, CZ, 1500 * Ft, Math.PI, 120) }, 0);
            Check(r.Count == 1 && r[0].Text.EndsWith("4 miles north of the field, traffic 2 miles south, 1500 feet, turn left heading three five zero, remain clear of the control zone."), "N51b Verkehr: " + string.Join(" | ", r));
            r = new Tower("Enfield 1-1") { Aloft = 120 }.Tick(N(1, 180, 2700), none, 0);
            Check(r.Count == 1 && r[0].Text.EndsWith("1 mile north of the field, climb and maintain at or above 4000 feet."), "N51b über dem Platz steigen: " + string.Join(" | ", r));
        }

        // --- N3: Transit: Approach from 12 NM (called position, no "contact Tower"), monitor altitude, no landing clearance along final, release outside;
        // Tower from 3 NM, "clear of the zone", full pattern -> wait; A54: "request VFR flight following" remains Flight Following
        {
            Telemetry N(double nm, double hdg, double ft) => At(CX + nm * NM, CZ, ft * Ft - FieldElev, hdg, 250 * Kt);
            var tz = new Tower("Enfield 1-1") { Aloft = 120 };
            tz.Tick(N(12, 180, 2500), none, 0);
            r = tz.OnTranscript("Kutaisi Approach, Enfield 1-1, 12 miles north, 2500 feet, request zone transit southbound", N(12, 180, 2500), none, 1);
            Check(r.Count == 1 && r[0].Role == "Approach" && r[0].Text.StartsWith("Enfield one one, Kutaisi Approach, cleared to cross the Kutaisi control zone overhead, southbound, not below 3000 feet, QNH") &&
                  r[0].Text.EndsWith("runway two five in use, report clear of the zone.") && !r[0].Contains("contact") && tz.Rejected == 0 && tz.Phase == Phase.Away && tz.Radar,
                  "N3 Approach aus 12 NM: " + string.Join(" | ", r));
            var z1 = tz.Tick(N(10, 180, 2500), none, 10);   // still before the zone (9.1 NM): no release
            var z2 = tz.Tick(N(5, 180, 2500), none, 20);
            var z3 = tz.Tick(N(4, 180, 2500), none, 30);   // at most every 60 s
            Check(z1.Count == 0 && z2.Count == 1 && z2[0].Text == "Enfield one one, check altitude, not below 3000 feet, pattern traffic below." && z2[0].Role == "Approach" && z3.Count == 0,
                  "N3 Höhe: " + string.Join(" | ", z1.Concat(z2).Concat(z3)));
            var (n3x, n3z) = P("25", 2.5 * NM);   // along final with landing cues: transit, no landing clearance (R11), no watch
            r = Enumerable.Range(0, 3).SelectMany(i => tz.Tick(At(n3x, n3z, 2.5 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 160 * Kt) with { Gear = 1, Vs = -3 }, none, 40 + i)).ToList();
            Check(r.All(m => !m.Contains("cleared to land") && !m.Contains("without clearance")) && tz.Phase == Phase.Away, "N3 im Endanflug: " + string.Join(" | ", r));
            // Reporting obligation #12: at zone + 0.5 NM "report clear of the zone", without report from zone + 1 NM (30 s later) release and into the debriefing
            var x1 = tz.Tick(N(-9.8, 180, 3000), none, 45);
            var x2 = tz.Tick(N(-10.5, 180, 3000), none, 60);
            r = tz.Tick(N(-11, 180, 3000), none, 76);
            Check(x1.Count == 1 && x1[0].Text == "Enfield one one, report clear of the zone." && x1[0].Role == "Approach" && x2.Count == 0 &&
                  r.Count == 2 && r[0].Text == "Enfield one one, I show you clear of the zone, frequency change approved." && r[0].Role == "Approach" && r[1].Text.StartsWith(L("Verstoß: keine Meldung", "Deviation: no report")) &&
                  tz.Released && tz.TransitInfo == null && !tz.Radar, "#12 N3 draußen: " + string.Join(" | ", x1.Concat(x2).Concat(r)));
            var tt = new Tower("Enfield 1-1") { Aloft = 120 };   // first watch level 1 (suggestion "request zone transit"), no longer during transit
            tt.Tick(N(2.8, 90, 1500), none, 0);   // N51b: watch only where it conflicts with airfield traffic (pattern under 3 NM), 3 NM/2500 ft is silent
            var sg0 = tt.Suggest(N(2.8, 90, 1500))?.Text;
            r = tt.OnTranscript("Kutaisi Tower, Enfield 1-1, request zone transit", N(3, 90, 3000), none, 1);
            Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Text.StartsWith("Enfield one one, Kutaisi Tower, cleared to cross the Kutaisi control zone overhead, eastbound") && tt.Rejected == 0 &&
                  tt.TransitInfo == "crossing overhead eastbound, 3000 feet" && sg0 == "request zone transit" && tt.Suggest(N(3, 90, 3000))?.Text != sg0, "N3 Tower aus 3 NM: " + string.Join(" | ", r) + $" ({sg0})");
            tt.TransitTold.Add("X");   // Readback with callsign = new request: same clearance, traffic info not again
            r = tt.OnTranscript("Kutaisi Tower, cleared to cross the Kutaisi control zone overhead eastbound, report clear of the zone, Enfield 1-1", N(3, 90, 3000), none, 3);
            Check(r.Count == 1 && r[0].Contains("cleared to cross") && tt.TransitTold.Contains("X"), "N3 Rücklesung: " + string.Join(" | ", r));
            var tci = new Tower("Enfield 1-1");   // "crossing" in the initial call for landing is not a transit
            tci.Tick(N(12, 180, 2500), none, 0);
            r = tci.OnTranscript("Kutaisi Approach, Enfield 1-1, 12 miles north, crossing the coast, inbound for landing", N(12, 180, 2500), none, 1);
            Check(tci.TransitInfo == null && tci.Phase != Phase.Away && !r.Any(m => m.Contains("cleared to cross")), "N3 crossing, inbound: " + string.Join(" | ", r));
            r = tt.OnTranscript("Enfield 1-1, transit complete, clear of the zone", N(3, 90, 3000), none, 5);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, frequency change approved." && r[0].Role == "Tower" && tt.TransitInfo == null && tt.Released, "N3 clear of the zone: " + string.Join(" | ", r));
            var pat = new[] { new Traffic(41, "C-130", CX + NM, CZ, FieldElev + 300, 0, 70), new Traffic(42, "F-16C_50", CX - NM, CZ, FieldElev + 300, Math.PI, 120) };
            var tq = new Tower("Enfield 1-1");
            tq.Tick(N(12, 180, 2500), pat, 0);
            r = tq.OnTranscript("Kutaisi Approach, Enfield 1-1, request zone transit", N(12, 180, 2500), pat, 1);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, Kutaisi Approach, remain outside the control zone, expect transit in 6 minutes." && tq.TransitInfo == null, "N3 volle Runde: " + string.Join(" | ", r));
            // R389: "request zone transit" (radio wheel/F10) inbound is no transit and no new check-in
            var tn = new Tower("Enfield 1-1");
            tn.Tick(N(12, 180, 2500), none, 0);
            tn.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", N(12, 180, 2500), none, 1);
            var tnPh = tn.Phase;
            r = tn.OnTranscript("Approach: request zone transit", N(12, 180, 2500), none, 2);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, Kutaisi Approach, unable zone transit, say intentions." && tn.Phase == tnPh && tnPh != Phase.Away && tn.TransitInfo == null, "R389 Durchflug im Anflug: " + string.Join(" | ", r));
            var tv = new Tower("Enfield 1-1");
            tv.Tick(N(30, 180, 6000), none, 0);
            r = tv.OnTranscript("Kutaisi Approach, Enfield 1-1, request VFR flight following", N(30, 180, 6000), none, 1);
            bool tvSq = r.Count == 1 && r[0].Text.Contains("squawk") && !r[0].Text.Contains("radar contact");   // R128: first the code, radar contact after 6 s
            r = tv.Tick(N(30, 180, 6000), none, 7);
            Check(tvSq && r.Count == 1 && tv.Following && tv.Phase == Phase.Away && r[0].Role == "Approach" && r[0].Contains("radar contact, 30 miles north of the field") &&
                  r[0].Text.EndsWith("Flight following, report leaving frequency.") && !r[0].Contains("heading"), "A54 request VFR flight following: " + string.Join(" | ", r));
        }

        // --- A47: "request low pass" from outside -> "report five mile final", on final clearance with minimum altitude and climb, afterwards no pattern
        {
            Telemetry F25(double nm) { var (x, z) = P("25", nm * NM); return At(x, z, Math.Max(150, nm * NM * Math.Tan(3 * Math.PI / 180) + 15), LandHdg("25"), 250 * Kt); }
            var tl = new Tower("Enfield 1-1");
            tl.Tick(F25(10), none, 0);
            r = tl.OnTranscript("Kutaisi Tower, Enfield 1-1, request low pass", F25(10), none, 1);
            Check(r.Count == 1 && r[0].Text == "Enfield one one, report five mile final runway two five." && tl.Phase == Phase.Pattern && tl.Suggest(F25(6))?.Text == "final, low pass" && tl.Suggest(F25(10)) == null,
                  "A47 request low pass: " + string.Join(" | ", r));
            r = tl.OnTranscript("Kutaisi Tower, Enfield 1-1, final, low pass", F25(4), none, 5);
            Check(r.Count == 1 && r[0].Text.StartsWith("Enfield one one, runway two five, ") && r[0].Text.EndsWith("cleared low pass, not below 700 feet, after the pass fly runway heading, climb and maintain 3000 feet.") &&
                  tl.Phase == Phase.ClearedLand, "A47 Freigabe: " + string.Join(" | ", r));
            var (a47fx, a47fz) = P("25", 2.4 * NM);   // 500 ft above airfield at 2.4 NM (glide path 810 ft): allowed, no too-low warning
            r = tl.Tick(At(a47fx, a47fz, 500 * Ft, LandHdg("25"), 250 * Kt), none, 5.5);
            Check(r.All(m => !m.Contains("low altitude")), "A47 500 ft im Endanflug: " + string.Join(" | ", r));
            var (a47x, a47z) = P("25", -1000);
            r = Enumerable.Range(0, 3).SelectMany(i => tl.Tick(At(a47x, a47z, 150 + 50 * i, LandHdg("25"), 250 * Kt), none, 6 + i)).ToList();
            Check(r.All(m => !m.Contains("downwind") && !m.Contains("report final")) && tl.Phase == Phase.Away && tl.Laps == 0 && tl.Released, "A47 nach dem Überflug: " + string.Join(" | ", r));
        }

        // --- A52: hostile C-130 (red) on 2 NM final at the neutral airfield: runway knows all traffic -> no takeoff clearance
        {
            var tH = new Tower("Enfield 1-1") { Side = 2 };
            tH.Tick(parked, none, 0);
            tH.OnTranscript("Enfield 11 request startup", parked, none, 1);
            tH.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            var (a52x, a52z) = P("25", 2 * NM);
            var redFin = new[] { new Traffic(80, "C-130", a52x, a52z, FieldElev + 2 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25") * Math.PI / 180, 70, Coalition: 1) };
            r = tH.OnTranscript("Enfield 11 ready for departure", hold, redFin, 3);
            Check(r.Count == 1 && tH.Phase == Phase.HoldShort && r[0].Contains("hold short runway two five, traffic"), "A52 feindlicher Endanflug: " + string.Join(" | ", r));
        }

        // --- A42: after "line up and wait" he may stand, not taxi; takeoff clearance is withdrawn before the takeoff roll if there is traffic
        var rwyAi = new Traffic(9, "FA-18C_hornet", CX + 600 * Ux, CZ + 600 * Uz, FieldElev, 254 * Math.PI / 180, 20, "AI", "dep-go", false);
        var tw8b = new Tower("Enfield 1-1");
        r = tw8b.Tick(At(CX, CZ, 0, 254, 0), new[] { rwyAi }, 0);   // Start on the runway, runway occupied: line up and wait
        r = r.Concat(tw8b.Tick(At(CX, CZ, 0, 254, 5), new[] { rwyAi }, 1)).ToList();
        Check(tw8b.Phase == Phase.HoldShort && r.Count == 0, "line up: stehen/langsam ohne hold position: " + string.Join(" | ", r.Select(m => m.Text)));
        r = tw8b.Tick(At(CX, CZ, 0, 254, 60), new[] { rwyAi }, 2);
        Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("stop immediately") && !r[0].Contains("abort"), r.FirstOrDefault()?.Text ?? "line up + Startlauf: -");
        Tower Cleared()
        {
            var x = new Tower("Enfield 1-1");
            x.Tick(parked, none, 0);
            x.OnTranscript("Enfield 11 request startup", parked, none, 1);
            x.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            r = x.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
            return x;
        }
        var tw8c = Cleared();
        string clrTxt = r[0].Text;
        Check(tw8c.Phase == Phase.ClearedTakeoff && clrTxt.Contains("until leaving the control zone") &&
              clrTxt.IndexOf("after departure") is >= 0 and var iDep && iDep < clrTxt.IndexOf("runway two five") && clrTxt.IndexOf("runway two five") < clrTxt.IndexOf("cleared for takeoff"),
              "Abflugteil vor der Freigabe: " + clrTxt);
        r = tw8c.Tick(At(CX, CZ, 0, 254, 20), new[] { rwyAi }, 4);   // 39 kt: takeoff roll in progress, no withdrawal
        Check(tw8c.Phase == Phase.ClearedTakeoff && r.Count == 0, "Startlauf ab 30 kt: Freigabe bleibt: " + string.Join(" | ", r.Select(m => m.Text)));
        r = tw8c.Tick(At(CX, CZ, 0, 254, 5), new[] { rwyAi }, 5);   // 10 kt: traffic on the runway
        Check(tw8c.Phase == Phase.HoldShort && r.Count == 1 && r[0].Role == "Tower" && r[0].Contains("hold position, cancel takeoff clearance, traffic on the runway"),
              r.FirstOrDefault()?.Text ?? "Rücknahme Bahn belegt: -");
        r = tw8c.Tick(At(CX, CZ, 0, 254, 5), new[] { rwyAi }, 6);
        Check(r.Count == 0 && tw8c.Phase == Phase.HoldShort, "nach Rücknahme auf der Bahn: still, keine neue Freigabe: " + string.Join(" | ", r.Select(m => m.Text)));
        r = tw8c.Tick(At(CX, CZ, 0, 254, 60), new[] { rwyAi }, 7);
        Check(r.Count(m => m.Role != "Info") == 1 && r[0].Contains("stop immediately"), r.FirstOrDefault()?.Text ?? "Rücknahme + Startlauf: -");
        r = tw8c.Tick(At(CX, CZ, 0, 254, 0), none, 40);
        Check(tw8c.Phase == Phase.ClearedTakeoff && r.Count == 1 && r[0].Contains("cleared for takeoff"), r.FirstOrDefault()?.Text ?? "Bahn frei -> erneut frei: -");
        var tw8d = Cleared();
        var (l1x, l1z) = P("25", 1 * NM);
        var lander = new Traffic(8, "F-16C", l1x, l1z, FieldElev + 60, LandHdg("25") * Math.PI / 180, 70, "AI", "arr", true);
        r = tw8d.Tick(hold, new[] { lander }, 4);
        Check(tw8d.Phase == Phase.HoldShort && r.Count == 1 && r[0].Contains("hold position, cancel takeoff clearance, traffic Viper on"), r.FirstOrDefault()?.Text ?? "Rücknahme Landender: -");
        Check(tw8d.Tick(hold, new[] { lander }, 5).Count == 0, "Landender noch da: keine neue Freigabe");
        var tw8e = Cleared();   // already on the runway: landing aircraft under 1.5 NM does not withdraw the clearance (AI does not go around), he continues the takeoff
        r = tw8e.Tick(At(CX, CZ, 0, 254, 5), new[] { lander }, 4);
        Check(tw8e.Phase == Phase.ClearedTakeoff && r.Count == 0, "auf der Bahn, Landender 1 NM: Freigabe bleibt: " + string.Join(" | ", r.Select(m => m.Text)));
        // R348: cleared but standing: query after 60 s, cancel after 120 s (RunwayClaimed: neither ClearedTakeoff nor line up), no silent new clearance; on the runway he may stand
        foreach (var onRwy348 in new[] { false, true })
        {
            var tw348 = Cleared();
            var at348 = onRwy348 ? At(CX, CZ, 0, 254, 0) : hold;
            var r348 = new[] { 4.0, 50, 64, 100, 124, 130 }.Select(s => string.Join(" ", tw348.Tick(at348, none, s).Select(m => m.Text))).ToList();
            Check(r348[0] == "" && r348[1] == "" && r348[2].Contains("verify rolling, runway two five, cleared for takeoff") && r348[3] == "" && r348[5] == "" && tw348.Phase == Phase.HoldShort && tw348.LineUp == onRwy348 &&
                  r348[4].Contains("cancel takeoff clearance, " + (onRwy348 ? "exit the runway" : "hold short runway two five") + ", report ready for departure"),
                  $"R348 Freigabe ohne Rollen (Bahn {onRwy348}): " + string.Join(" | ", r348));
        }
        // R347: helicopter: air taxi, takeoff clearance without runway number
        var tw347 = new Tower("Enfield 1-1") { AcType = "UH-1H" };
        tw347.Tick(parked, none, 0);
        tw347.OnTranscript("Enfield 11 request startup", parked, none, 1);
        var taxi347 = tw347.OnTranscript("Enfield 11 request taxi", parked, none, 2);
        var clr347 = tw347.OnTranscript("Enfield 11 ready for departure", hold, none, 3);
        Check(taxi347.Count == 1 && taxi347[0].Contains("air taxi") && taxi347[0].Contains("to holding point runway two five") && clr347.Count == 1 &&
              clr347[0].Contains("cleared for takeoff from present position") && !clr347[0].Contains("runway"), "R347 Hubschrauber: " + string.Join(" | ", taxi347.Concat(clr347).Select(m => m.Text)));
        // R100: rejected takeoff clears the takeoff clearance; vacate the runway without "hold position", new clearance only on "ready for departure"
        foreach (var call in new[] { "Kutaisi Tower, Enfield 1-1, aborting takeoff", "Enfield 1-1, stopping", "Enfield 1-1, rejecting takeoff", "Kutaisi Tower, Enfield 1-1, cancel my takeoff", "Kutaisi Tower, Enfield 1-1, aborting, bird strike" })
        {
            var tw8f = Cleared();
            tw8f.Tick(At(CX, CZ, 0, 254, 62), none, 4);   // 120 kt
            tw8f.Tick(At(CX, CZ, 0, 254, 2.6), none, 8);   // 5 kt
            r = tw8f.OnTranscript(call, At(CX, CZ, 0, 254, 2.6), none, 9);
            var rr = tw8f.Tick(At(CX, CZ, 0, 254, 3), none, 10).Concat(tw8f.Tick(At(CX, CZ, 0, 300, 3), none, 60)).ToList();   // slowly continues on the runway
            Check(r.Count == 1 && !r[0].Contains("cleared for takeoff") && r[0].Text.EndsWith("Contact Kutaisi Ground two six four decimal five when vacated.") && tw8f.Phase == Phase.HoldShort && rr.Count == 0,
                  $"R100 \"{call}\": " + string.Join(" | ", r.Concat(rr).Select(m => m.Text)));
            r = tw8f.OnTranscript("Enfield 1-1, ready for departure", hold, none, 70);
            Check(tw8f.Phase == Phase.ClearedTakeoff && r[0].Contains("cleared for takeoff"), "R100 danach ready: " + r[0]);
        }
        // R267: after vacating (off the runway, over 700 m from the threshold) no unsolicited "contact Ground" after 30 s; "runway vacated, request taxi to holding point" = taxi clearance to the holding point, not to parking
        var tw267 = Cleared();
        tw267.Tick(At(CX, CZ, 0, 254, 62), none, 4);
        tw267.Tick(At(CX, CZ, 0, 254, 2.6), none, 8);
        var r267 = tw267.OnTranscript("Kutaisi Tower, Enfield 1-1, aborting takeoff", At(CX, CZ, 0, 254, 2.6), none, 9);
        r267.AddRange(Enumerable.Range(20, 40).SelectMany(s => tw267.Tick(offRwy with { Ias = 0 }, none, s)));
        var ph267 = tw267.Phase;
        r267.AddRange(tw267.OnTranscript("Kutaisi Ground, Enfield 1-1, runway vacated, request taxi to holding point runway 25", offRwy with { Ias = 0 }, none, 70));
        Check(r267.Count == 2 && !r267[0].Text.Contains("ready for departure") && ph267 == Phase.TaxiOut && r267[1].Text.Contains("taxi to holding point runway two five") && tw267.Phase == Phase.TaxiOut,
              $"R267 nach Startabbruch: {ph267} " + string.Join(" | ", r267.Select(m => m.Text)));
        var tw8g = Cleared();   // R100: abort reported at 97 kt, the deceleration is not a takeoff roll without clearance; accelerating again is
        tw8g.Tick(At(CX, CZ, 0, 254, 62), none, 4);
        r = tw8g.OnTranscript("Kutaisi Tower, Enfield 1-1, aborting takeoff", At(CX, CZ, 0, 254, 50), none, 5);
        var rg = tw8g.Tick(At(CX, CZ, 0, 254, 40), none, 6).Concat(tw8g.Tick(At(CX, CZ, 0, 254, 25), none, 7)).Concat(tw8g.Tick(At(CX, CZ, 0, 254, 5), none, 9)).ToList();
        Check(r.Count == 1 && rg.Count == 0, "R100 Abbruch bei 97 kt: " + string.Join(" | ", r.Concat(rg).Select(m => m.Text)));
        rg = tw8g.Tick(At(CX, CZ, 0, 254, 30), none, 40);
        Check(rg.Count(m => m.Role != "Info") == 1 && rg[0].Contains("stop immediately"), "R100 danach Startlauf ohne Freigabe: " + (rg.FirstOrDefault()?.Text ?? "-"));

        // --- Final (PAR): silent on the glide path, call only when clearly off; too low. Without initial call (Away) with gear down (R11)
        ForceIfr = true;   // K9: without ILS in IFR PAR (visual: straight in without calls)
        var tw9 = new Tower("Enfield 1-1");
        tw9.navName = "six mile final";   // Straight-in approach (PAR); visual pattern gets no mile calls
        double along4 = 3.9 * NM;
        var (g4x, g4z) = P("25", along4);
        r = tw9.Tick(At(g4x, g4z, along4 * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 1 }, none, 0);
        Check(r.Count == 0, "4 NM auf dem Gleitpfad: still: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw9.Tick(At(g4x, g4z, along4 * Math.Tan(3 * Math.PI / 180) + 15 + 400 * Ft, LandHdg("25"), 70) with { Gear = 1 }, none, 1);
        Check(r.Count == 1 && r[0].Contains("four miles, well above glidepath, increase rate of descent, on course"), r.FirstOrDefault()?.Text ?? "4 NM zu hoch: -");
        var (g2x, g2z) = P("25", 2 * NM);
        tw9.OnTranscript("Kutaisi Tower, Enfield 11, final", At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 1);   // Landing clearance (reporting obligation: on report)
        r = tw9.Tick(At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 2);
        Check(r.Count == 1 && r[0].Contains("Low altitude alert"), r.FirstOrDefault()?.Text ?? "zu tief: -");
        r = tw9.Tick(At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 40);   // A97: only once per approach
        Check(!r.Any(m => m.Text.Contains("low altitude")), "Tiefenwarnung zweimal: " + string.Join(" | ", r.Select(m => m.Text)));
        // User report: after "cleared ILS approach" no PAR calls (glide path, heading), only the too-low warning once
        var twIls = new Tower("Enfield 1-1");
        twIls.F.Ils["25"] = (109.75, "KTS");
        twIls.navName = "six mile final";
        r = twIls.Tick(At(g4x, g4z, along4 * Math.Tan(3 * Math.PI / 180) + 15 + 400 * Ft, LandHdg("25") + 10, 70) with { Gear = 1 }, none, 1);
        twIls.OnTranscript("Kutaisi Tower, Enfield 11, final", At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 30);   // Landing clearance (reporting obligation: on report)
        var rIls = twIls.Tick(At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 31).Concat(twIls.Tick(At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 70)).ToList();
        Check(r.Count == 0 && rIls.Count(m => m.Text.Contains("Low altitude alert")) == 1 && !rIls.Any(m => m.Text.Contains("glidepath") || m.Text.Contains("course")),
              "ILS: keine PAR-Ansagen, einmal Tiefenwarnung: " + string.Join(" | ", r.Concat(rIls).Select(m => m.Text)));
        // Go-around from the Tower (runway occupied) starts a new approach: too-low warning again there
        var (g07x, g07z) = P("25", 0.7 * NM);
        r = tw9.Tick(At(g07x, g07z, 0.7 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 1 }, new[] { new Traffic(9, "F-16", CX, CZ, FieldElev + 2, 0, 25) }, 50);
        // R12: IFR -> missed approach (runway heading, altitude, Approach) instead of "join left hand downwind"
        Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Text.StartsWith("Enfield one one, go around, I say again, go around, traffic on runway. Fly runway heading, climb and maintain") &&
              r[0].Contains("contact Kutaisi Approach") && !r[0].Contains("downwind") && tw9.Phase == Phase.Inbound, "R12 Durchstarten Bahn belegt bei IFR: " + (r.FirstOrDefault()?.Text ?? "-"));
        tw9.Phase = Phase.Entering;   // new approach: radar vectoring shortened to final
        r = tw9.Tick(At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 90).Concat(tw9.Tick(At(g2x, g2z, 50, LandHdg("25"), 70) with { Gear = 1 }, none, 91)).ToList();
        Check(r.Any(m => m.Text.Contains("Low altitude alert")), "Tiefenwarnung nach Durchstarten vom Tower: " + string.Join(" | ", r.Select(m => m.Text)));
        ForceIfr = false;
        // Gear up shortly before landing (descending: landing cue, R11): "check wheels down" (once)
        var twWh = new Tower("Enfield 1-1");
        var (g1x, g1z) = P("25", 1.5 * NM);
        var gearUp = At(g1x, g1z, 1.5 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 0, Vs = -3.5 };
        r = twWh.Tick(gearUp, none, 0).Concat(twWh.Tick(gearUp, none, 1)).ToList();
        Check(r.Any(m => m.Text.Contains("check wheels down")) && !twWh.Tick(gearUp, none, 2).Any(m => m.Text.Contains("wheels")), "Fahrwerk oben: " + string.Join(" | ", r.Select(m => m.Text)));
        // R11: without initial call fast with gear up low through the final -> nothing, phase stays Away; opposite direction with gear: "check runway", no pattern
        var twLo = new Tower("Enfield 1-1");
        var fast = At(g1x, g1z, 60, LandHdg("25"), 130) with { Gear = 0, Vs = -3 };
        r = Enumerable.Range(0, 3).SelectMany(s => twLo.Tick(fast, none, s)).ToList();
        Check(r.Count == 0 && twLo.Phase == Phase.Away, "R11 Tiefflug: " + string.Join(" | ", r.Select(m => m.Text)));
        var (o2x, o2z) = P("07", 2 * NM);
        r = twLo.Tick(At(o2x, o2z, 150, LandHdg("07"), 70) with { Gear = 1 }, none, 4);
        Check(r.Count == 1 && r[0].Contains("Kutaisi Tower, check runway") && twLo.Phase == Phase.Away, "R11 Gegenrichtung: " + (r.FirstOrDefault()?.Text ?? "-") + " " + twLo.Phase);
        // Straight-in approach from Approach, pilot reports "initial": no break, continues straight in
        var twSi = new Tower("Enfield 1-1") { Phase = Phase.Entering };
        twSi.navName = "six mile final";
        var (s6x, s6z) = P("25", 6 * NM);
        var s6 = At(s6x, s6z, 6 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 120);
        twSi.Tick(s6, none, 0);
        twSi.Phase = Phase.Entering;
        r = twSi.OnTranscript("Kutaisi Tower, Enfield 11, initial", s6, none, 1);
        Check(r[0].Contains("make straight in runway two five, report four mile final") && !r[0].Contains("cleared") && twSi.Phase == Phase.Entering, "Straight-in, 'initial': " + r[0]);
        // R42: Straight-in under 8 NM states the altitude: too high on final (6 NM, 3500 ft) -> "descend and maintain" to the glide path (2000 ft), not below; lower than the glide path -> no climb instruction
        var twSa = new Tower("Enfield 1-1");
        r = twSa.OnTranscript("Kutaisi Approach, Enfield 11, request straight in", s6 with { AltMsl = 3500 * Ft }, none, 1);
        Check(r[0].Contains("straight in approach runway two five approved") && r[0].Contains("Descend and maintain 2000 feet"), "R42 Straight-in 6 NM zu hoch: " + r[0]);
        var twSb = new Tower("Enfield 1-1");
        r = twSb.OnTranscript("Kutaisi Approach, Enfield 11, request straight in", s6 with { AltMsl = 1500 * Ft }, none, 1);
        Check(r[0].Contains("Maintain 1500 feet") && !r[0].Contains("limb"), "R42 Straight-in unter dem Gleitweg: " + r[0]);
        // K9: IFR without ILS (Kutaisi 25, TACAN at the airfield): PAR as on the map, no TACAN procedure; in visual conditions continue straight in (above)
        ForceIfr = true;
        var kp = Airfield.Kutaisi(); kp.Tacan = 44;
        var atisP = new Tower(kp, "x").AtisText("K", new Telemetry(kp.Elev + 2, 0, 0, 0, kp.X, kp.Z, 0, 0, 760), 15, true, 200, 3000);
        var twPar = new Tower(kp, "Enfield 1-1");
        r = twPar.OnTranscript("Kutaisi Approach, Enfield 11, request P A R approach", s6, none, 1);
        var rP = twPar.Tick(At(g4x, g4z, along4 * Math.Tan(3 * Math.PI / 180) + 15 + 400 * Ft, LandHdg("25"), 70) with { Gear = 1 }, none, 30);   // cleared on final: PAR calls anyway
        ForceIfr = false;
        Check(atisP.Contains("expect vectors for P A R approach runway two five") && !atisP.Contains("TACAN approach") && r[0].Contains("until established, cleared P A R approach runway two five")
              && !r[0].Contains("no transmissions")   // R271: without talk-down no lost-comm instruction (R221 promised calls every 5 s on final)
              && rP.Any(m => m.Text.Contains("well above glidepath")), $"K9 PAR Kutaisi 25: {atisP} | {r[0]} | {string.Join(" | ", rP.Select(m => m.Text))}");
        // R42: on short final (2.5 NM, on the glide path) terrain + 250 ft as lower limit, not the MVA (flat terrain 0 m: 1000 ft, above the glide path)
        var flat = Airfield.Kutaisi();
        flat.SetTerrain(new float[61, 61], flat.X - 30 * NM, flat.Z - 30 * NM, NM);
        var (s2x, s2z) = P("25", 2.5 * NM);
        var s2 = At(s2x, s2z, 2.5 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 120);
        r = new Tower(flat, "Enfield 1-1").OnTranscript("Kutaisi Approach, Enfield 11, request straight in", s2, none, 1);
        Check(r[0].Contains("500 feet") && !r[0].Contains("limb") && !r[0].Contains("1000 feet"), "R42 Straight-in 2,5 NM mit Gelände nicht über den Gleitweg: " + r[0]);

        // --- Pattern: extend downwind due to traffic, then base
        var tw10 = new Tower("Enfield 1-1");
        var dwPt = P("25", 2000);
        var dw = At(dwPt.Item1 - 2000 * Uz, dwPt.Item2 + 2000 * Ux, PatternFt * Ft - FieldElev, LandHdg("25") - 180, 120);
        tw10.Tick(dw, none, 0);
        r = tw10.OnTranscript("Enfield 11 downwind", dw, none, 1);
        Check(tw10.Phase == Phase.Pattern, "downwind: " + r[0]);
        var (t3x, t3z) = P("25", 3 * NM);
        var c130 = new Traffic(5, "C-130", t3x, t3z, FieldElev + 300, LandHdg("25") * Math.PI / 180, 70);
        r = tw10.Tick(dw, new[] { c130 }, 2);
        Check(r.Count == 1 && r[0].Contains("extend downwind") && r[0].Contains("C 130"), r.FirstOrDefault()?.Text ?? "extend: -");
        // R207: Base described with preceding traffic ("follow the C 130 on 1 mile final"), if he is down or gone, only "turn base" (before always "turn base now, number two, follow the traffic")
        var tw10b = new Tower("Enfield 1-1");
        tw10b.Tick(dw, none, 0);
        tw10b.OnTranscript("Enfield 11 downwind", dw, none, 1);
        tw10b.Tick(dw, new[] { c130 }, 2);
        var (t05x, t05z) = P("25", 0.5 * NM);
        r = tw10b.Tick(dw with { Ias = 40 }, new[] { c130 with { X = t05x, Z = t05z, AltMsl = FieldElev + 50 } }, 3);
        Check(r.Count == 1 && r[0].Text == "Enfield one one, turn base, number 2, follow the C 130 on 1 mile final, report final.", "R207 turn base mit Vordermann: " + (r.FirstOrDefault()?.Text ?? "-"));
        // no second "report final" in base afterwards (already in the base call)
        r = tw10b.Tick(dw with { Ias = 40, Hdg = (LandHdg("25") + 90) * Math.PI / 180 }, none, 4);
        Check(r.All(m => !m.Text.Contains("report final")), "R207 nach turn base kein zweites report final: " + string.Join(" | ", r));
        // two ahead of me on final: "number 3", preceding traffic is the last of them (not the foremost)
        var tw10c = new Tower("Enfield 1-1");
        tw10c.Tick(dw, none, 0);
        tw10c.OnTranscript("Enfield 11 downwind", dw, none, 1);
        tw10c.Tick(dw, new[] { c130 }, 2);
        var (t09x, t09z) = P("25", 0.9 * NM);
        r = tw10c.Tick(dw with { Ias = 40 }, new[] { c130 with { X = t05x, Z = t05z, AltMsl = FieldElev + 50 }, new Traffic(6, "F-16C_50", t09x, t09z, FieldElev + 90, LandHdg("25") * Math.PI / 180, 70) }, 3);
        Check(r.Count == 1 && r[0].Text == "Enfield one one, turn base, number 3, follow the Viper on 1 mile final, report final.", "R207 zwei vor mir: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw10.Tick(dw, none, 3);
        Check(r.Count == 1 && r[0].Text == "Enfield one one, turn base, report final.", "R207 turn base, Vordermann weg: " + (r.FirstOrDefault()?.Text ?? "-"));

        // A45: pattern traffic counts for the sequence; Initial: "number 2, follow the Viper on downwind", Downwind: "number 2" on base traffic; departure/upwind does not count
        (double X, double Z) PatXY(double along, double lat) => kTower.Pt("25", along, lat);
        Traffic PatAc(int id, double along, double lat, double hdgDeg, double ft = 0) { var p = PatXY(along, lat); return new(id, "F-16C_50", p.X, p.Z, ft == 0 ? PatternFt * Ft : ft, hdgDeg * Math.PI / 180, 120); }
        var pDw = PatAc(41, 0.5 * NM, 1.2 * NM, LandHdg("25") + 180);
        var pBase = PatAc(42, 1.5 * NM, 1.0 * NM, LandHdg("25") + 90);
        var pUp = PatAc(43, -0.5 * NM, 1.0 * NM, LandHdg("25"));
        var ipP = P("25", InitialDist);
        var ipA = At(ipP.Item1, ipP.Item2, 450, LandHdg("25"), 150);
        Tower Ini(Traffic[] tr) { var w = new Tower("Enfield 1-1") { Phase = Phase.Entering, navName = "initial" }; w.Tick(ipA, tr, 0); w.Phase = Phase.Entering; w.navName = "initial"; return w; }
        r = Ini(none).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, none, 1);
        Check(r[0].Contains("runway two five, report base") && !r[0].Contains("number"), "A45 ohne Verkehr: " + r[0]);
        r = Ini(new[] { pDw }).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, new[] { pDw }, 1);
        Check(r[0].Contains("number 2, follow the Viper on downwind, runway two five, report base"), "A45 Initial, Verkehr im Gegenanflug: " + r[0]);
        r = Ini(new[] { pBase }).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, new[] { pBase }, 1);
        Check(r[0].Contains("number 2, follow the Viper on left base"), "A45 Initial, Verkehr auf Base: " + r[0]);
        r = Ini(new[] { pUp }).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, new[] { pUp }, 1);
        Check(r[0].Contains("runway two five, report base") && !r[0].Contains("number"), "A45 Aufwind/Abflug zählt nicht: " + r[0]);
        r = Ini(new[] { PatAc(44, 0.5 * NM, 1.2 * NM, LandHdg("25") + 180, PatternFt * Ft + 2000 * Ft) }).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, none, 1);
        Check(!r[0].Contains("number"), "A45 hoch über der Platzrunde zählt nicht: " + r[0]);
        var twPb = new Tower("Enfield 1-1");
        twPb.Tick(dw, new[] { pBase }, 0);
        r = twPb.OnTranscript("Enfield 11 downwind", dw, new[] { pBase }, 1);
        Check(r[0].Contains("number 2"), "A45 Gegenanflug, Verkehr auf Base: " + r[0]);
        // A45: base (also departure while turning in) lies behind final and downwind (before, remaining distance only the offset -> "number 2, follow ...")
        var pCw = PatAc(45, -3400, 0.6 * NM, LandHdg("25") - 90);
        var (cfx, cfz) = P("25", 2.5 * NM);
        var cwFin = At(cfx, cfz, 250, LandHdg("25"), 70);
        var twXw = new Tower("Enfield 1-1"); twXw.Tick(cwFin, new[] { pCw }, 0); twXw.Phase = Phase.Pattern;
        r = twXw.OnTranscript("Kutaisi Tower, Enfield 11, final", cwFin, new[] { pCw }, 1);
        Check(r[0].Contains("cleared to land") && !r[0].Contains("number"), "A45 Endanflug, Verkehr im Queranflug dahinter: " + r[0]);
        var twCd = new Tower("Enfield 1-1"); twCd.Tick(dw, new[] { pCw }, 0);
        r = twCd.OnTranscript("Enfield 11 downwind", dw, new[] { pCw }, 1);
        Check(r[0].Contains("number one"), "A45 Gegenanflug, Verkehr im Queranflug dahinter: " + r[0]);
        // A45: traffic behind me on the same initial is not ahead of me (before, break allowance also against final traffic), final ahead of me yes
        var pIniB = PatAc(46, InitialDist + 1.5 * NM, 0, LandHdg("25"));
        r = Ini(new[] { pIniB }).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, new[] { pIniB }, 1);
        Check(r[0].Contains("runway two five, report base") && !r[0].Contains("number"), "A45 Initial, Verkehr dahinter auf dem Initial: " + r[0]);
        var pFinA = PatAc(47, NM, 0, LandHdg("25"), FieldElev + 100);
        r = Ini(new[] { pFinA }).OnTranscript("Kutaisi Tower, Enfield 11, initial", ipA, new[] { pFinA }, 1);
        Check(r[0].Contains("number 2, follow the"), "A45 Initial, Verkehr vor mir im Endanflug: " + r[0]);

        // --- Airfield from DCS data (without map): runway 13/31, left-hand pattern, entry via Initial
        var gf = Airfield.FromDcs("Test-Field", 0, 0, 10, new[] { ("13", -131 * Math.PI / 180, 0.0, 0.0, 2400.0) });
        Check(gf.Ends.Select(e => e.Name).SequenceEqual(new[] { "13", "31" }) && gf.MagVar == 6 && gf.PatternFt == 1500 &&
              gf.Station == "Test Field Tower", $"FromDcs: {string.Join(",", gf.Ends.Select(e => e.Name))} var {gf.MagVar} pattern {gf.PatternFt}");
        Check(Airfield.FromDcs("X", 0, 0, 0, new[] { ("13R", -131 * Math.PI / 180, 0.0, 0.0, 2000.0) }).Ends[1].Name == "31L", "Parallelbahn 13R -> 31L");
        var gk = Airfield.FromDcs("K26", 900, -900, 10, new[] { ("13", -131 * Math.PI / 180, 100.0, 50.0, 2400.0), ("13R", -131 * Math.PI / 180, 300.0, -50.0, 2400.0) });
        Check(Math.Sqrt((gk.X - 200) * (gk.X - 200) + (gk.Z - 0) * (gk.Z - 0)) < 100, $"K26 Platzpunkt = Bahnmitte: {gk.X:F0}/{gk.Z:F0}");
        // Parallel runways without side from DCS (Persian Gulf, real coordinates): Dubai "30"/"12", Al Dhafra "13"/"31", one runway each. Northern runway Dubai = 30R/12L, Al Dhafra 13L/31R
        var dxb = Airfield.FromDcs("Dubai Intl", -101509.8, -87472.6, 5, new[] { ("30", 1.01050, -100594.4, -88875.4, 3350.0), ("12", 1.01037, -101962.3, -87414.4, 3350.0) });
        var dhf = Airfield.FromDcs("Al Dhafra AFB", -209935.5, -174592.1, 16, new[] { ("13", -2.23306, -211027.8, -173240.0, 3476.0), ("31", -2.23313, -212946.3, -172936.5, 3476.0) });
        double Lat(RwyEnd e, RwyEnd o) => (o.CX - e.CX) * e.Dz - (o.CZ - e.CZ) * e.Dx;   // > 0: o lies left of e in landing direction
        Check(string.Join(",", dxb.Ends.Select(e => e.Name)) == "30R,12L,12R,30L" && string.Join(",", dhf.Ends.Select(e => e.Name)) == "13L,31R,31L,13R" &&
              Lat(dxb.Ends[0], dxb.Ends[3]) > 300 && Lat(dhf.Ends[0], dhf.Ends[3]) < -1000 && dxb.Main == "30R" && RwSay("30L") == "three zero left" && RwSay("13R") == "one three right" &&
              dxb.BestRunway(0, 0) == "30R" && dxb.BestRunway(5 * dxb.Ends[3].Dx, 5 * dxb.Ends[3].Dz) == "12L" && dxb.BestRunway(-5 * dxb.Ends[0].Dx, -5 * dxb.Ends[0].Dz) == "30R",
              $"Parallelbahnen L/R: {string.Join(",", dxb.Ends.Select(e => e.Name))} / {string.Join(",", dhf.Ends.Select(e => e.Name))} Main {dxb.Main}");
        // Duplicate runway from DCS (Abu Dhabi, real data): second "31" with 3032 m on the centerline of the "31" with 3907 m -> one runway (the longer, also in reverse
        // order), the parallel 2 km beside it remains; before: six ends, twice 31L/13R
        (string, double, double, double, double) a13 = ("13", -2.24720, -188457.5, -162031.0, 3907.0), a31 = ("31", 0.89450, -190960.8, -162107.0, 3907.0), a31d = ("31", -2.24700, -189781.3, -163576.8, 3032.0);
        var (auh, auh2) = (Airfield.FromDcs("Abu Dhabi Intl", -187211.2, -163535.5, 28, new[] { a13, a31, a31d }), Airfield.FromDcs("Abu Dhabi Intl", -187211.2, -163535.5, 28, new[] { a13, a31d, a31 }));
        Check(string.Join(",", auh.Ends.Select(e => e.Name)) == "13L,31R,31L,13R" && string.Join(",", auh2.Ends.Select(e => e.Name)) == "13L,31R,31L,13R" &&
              auh.Ends.Concat(auh2.Ends).All(e => e.Len == 3907) && auh2.End("31L").CX == a31.Item3 && auh2.End("13R").CX == a31.Item3,
              $"Doppelte Bahn zusammengefasst: {string.Join(",", auh.Ends.Select(e => $"{e.Name}/{e.Len}"))} | {string.Join(",", auh2.Ends.Select(e => $"{e.Name}/{e.Len}"))}");
        Check(RwySide(Normalize("request runway three zero left")) == "L" && RwySide(Normalize("runway 30R")) == "R" && RwySide(Normalize("runway 2 5 left hand downwind")) == "" &&
              RwySide(Normalize("runway 25, 4 miles")) == "", "Bahnwunsch mit Seite");
        var dxw = new Tower(dxb, "Enfield 1-1");
        var dxIn = new Telemetry(5 + 1500, 1500, 120, 120 * Math.PI / 180, dxb.X - 15 * NM, dxb.Z, 0, 0, 760);
        dxw.Tick(dxIn, none, 0);
        r = dxw.OnTranscript("Dubai Approach, Enfield 1-1, inbound for landing, request runway three zero left", dxIn, none, 1);
        Check(dxw.Runway == "30L" && r[0].Contains("three zero left"), $"Bahnwunsch 30 left: {dxw.Runway} {r.FirstOrDefault()?.Text}");
        // Approached the parallel runway 30L, active is 30R (log Dubai 16:59): landing clearance, glide path and scoring against 30L, no too-low warning
        var dxt = new Tower(dxb, "Enfield 1-1");
        var l30 = dxb.Ends[3];   // 30L (order of the ends is the same even without sides)
        Telemetry Dx(double a, double agl) => new(5 + agl, agl, agl > 0 ? 70 : 60, l30.Hdg * Math.PI / 180, l30.ThrX - a * l30.Dx, l30.ThrZ - a * l30.Dz, 0, 0, 760, agl > 0 ? -3.5 : 0, 1);
        double Gp(double a) => a * Math.Tan(3 * Math.PI / 180) + 15;
        dxt.Load(new State(Phase.Entering, dxb.Main, null, false, false, 0, null, "", false, false, false, false, null, 0, 0, null, true, false, null, null, 0, true), Dx(3.2 * NM, Gp(3.2 * NM)), 0);
        var dxr = new List<Msg>();
        int ds = 1;
        foreach (var a in new[] { 2.9, 2.6, 2.2, 1.8, 1.4, 1.0, 0.6, 0.35 })
        {
            dxr.AddRange(dxt.Tick(Dx(a * NM, Gp(a * NM)), none, ds++));
            if (a == 2.6) dxr.AddRange(dxt.OnTranscript("Enfield 1-1, final", Dx(a * NM, Gp(a * NM)), none, ds));   // Reporting obligation: landing clearance on report
        }
        dxr.AddRange(dxt.Tick(Dx(-400, 0), none, ds++));
        var dxg = dxr.FirstOrDefault(m => m.Role == "Info")?.Text ?? "";
        Check(dxr.Any(m => m.Text.Contains("runway three zero left") && m.Text.Contains("cleared to land")) && dxg.Contains("30L") && !dxg.Contains("SHORT") && !dxg.Contains("VOR der Schwelle") &&
              Regex.IsMatch(dxg, @"(Centerline|Mittellinie) Ø [0-9] m") && !dxr.Any(m => m.Text.Contains("low altitude") || m.Text.Contains("check runway")),
              "Parallelbahn angeflogen: " + string.Join(" | ", dxr.Select(m => m.Text)));
        // Crossing runways (26/31, shared center): touching down at the intersection 10 m beside the 26 it stays the 26, not the closer centerline of the 31 there
        var xf = Airfield.FromDcs("X", 0, 0, 0, new[] { ("26", -260 * Math.PI / 180, 0.0, 0.0, 3000.0), ("31", -310 * Math.PI / 180, 0.0, 0.0, 2500.0) });
        var x26 = xf.End("26");
        var xr = new Tower(xf, "Enfield 1-1").RwyOf(new Telemetry(0, 0, 60, 260 * Math.PI / 180, 10 * x26.Dz, -10 * x26.Dx, 0, 0, 760));
        Check(xr == "26" && string.Join(",", xf.Ends.Select(e => e.Name)) == "26,08,31,13", $"Kreuzbahn: Bahn {xr}, {string.Join(",", xf.Ends.Select(e => e.Name))}");
        var g = new Tower(gf, "Enfield 1-1");
        Telemetry G(double x, double z, double agl = 0, double hdg = 0, double ias = 0) => new(10 + agl, agl, ias, hdg * Math.PI / 180, x, z, 0, 0, 760);
        {   // A13 without map (zone 5 NM): on the way to six mile final (6.7 NM from the airfield) no "leaving the control zone"
            var gA = new Tower(gf, "Enfield 1-1");
            var gFaf = gA.OnCenterline("13", 6 * NM);
            double g13x = gf.End("13").Dz * 2.5 * NM, g13z = -gf.End("13").Dx * 2.5 * NM, gh = Bearing(g13x, g13z, gFaf.X, gFaf.Z);
            gA.Load(new State(Phase.Entering, "13", null, false, false, 0, new[] { gFaf.X, gFaf.Z }, "six mile final", false, false, false, false, null, 0, 0, null, true, false, null, null, 0, true), G(g13x, g13z, 600, gh, 200), 0);
            var saidG = new List<string>();
            for (int s = 1; s <= 24; s++) { double f = s / 20.0; saidG.AddRange(gA.Tick(G(g13x + (gFaf.X - g13x) * f, g13z + (gFaf.Z - g13z) * f, 600, gh, 200), none, s * 5).Select(m => m.Text)); }
            Check(!saidG.Any(x => x.Contains("say intentions") || x.Contains("cancelled")), "A13 ohne Karte zum six mile final: " + string.Join(" | ", saidG));
        }
        var gPark = G(300, 300);
        g.Tick(gPark with { WindX = 4, WindZ = -4 }, none, 0);   // Wind from southeast (≈135°) -> runway 13
        Check(g.Runway == "13", "Wind -> Bahn " + g.Runway);
        r = g.OnTranscript("Test Field Ground, Enfield 1-1, request taxi", gPark, none, 1);
        Check(r[0].StartsWith("Enfield one one, Test Field Ground, taxi to holding point runway one three, wind calm, QNH"), r[0]);
        var gThr = g.Thr("13");
        r = g.OnTranscript("Enfield 11 ready for departure", G(gThr.X + 60, gThr.Z - 60), none, 2);
        Check(r[0].Contains("cleared for takeoff") && r[0].Contains("own navigation"), r[0]);
        var g3 = new Tower(gf, "Enfield 1-1");   // A92: direction without map -> "leave the control zone northbound"
        g3.Tick(gPark with { WindX = 4, WindZ = -4 }, none, 0);
        g3.OnTranscript("Test Field Ground, Enfield 1-1, request taxi, departure north", gPark, none, 1);
        r = g3.OnTranscript("Enfield 11 ready for departure", G(gThr.X + 60, gThr.Z - 60), none, 2);
        Check(r[0].Contains("leave the control zone northbound") && !r[0].Contains("C R P") && !r[0].Contains("own navigation"), r[0]);
        {   // A100/A105: zone 5 NM: out before 60 s without check-in -> Approach identifies first (no "leaving control zone" without radar contact)
            var gDz = new Tower(gf, "Enfield 1-1");
            gDz.Tick(gPark with { WindX = 4, WindZ = -4 }, none, 0);
            gDz.OnTranscript("Test Field Ground, Enfield 1-1, request taxi", gPark, none, 1);
            gDz.OnTranscript("Enfield 11 ready for departure", G(gThr.X + 60, gThr.Z - 60), none, 2);
            gDz.Tick(G(500, 0, 120, 90, 80), none, 10);
            r = gDz.Tick(G(1000, 0, 250, 90, 120), none, 11);
            var rz = new List<Msg>();
            for (int s = 15; s <= 45; s += 5) rz.AddRange(gDz.Tick(G(1000 + (5.5 * NM - 1000) * (s - 11) / 34.0, 0, 300, 90, 150), none, s));   // 5.5 NM after 34 s
            Check(r.Count == 1 && r[0].Contains("contact") && rz.Count == 1 && rz[0].Role == "Tower" && rz[0].Contains("contact Test Field Approach now") && !rz[0].Contains("leaving") && gDz.Phase == Phase.Departing,
                  "#11 vor dem Zonenrand ohne Check-in: " + string.Join(" | ", r.Concat(rz)));
        }
        var g2 = new Tower(gf, "Enfield 1-1");
        var gFar = G(30000, -2000, 1000, 180, 150);
        g2.Tick(gFar, none, 0);
        r = g2.OnTranscript("Test Field Approach, Enfield 11, inbound for landing", gFar, none, 1);
        Check(r[0].Contains("Expect vectors to initial runway") && r[0].Contains("heading"), r[0]);
        var gH = new Tower(gf, "Enfield 1-1") { QueueAhead = 3, Spaced = false };
        var gFar30 = G(55000, -2000, 1000, 180, 150);   // 30 NM: holding point 15 NM from the airfield
        gH.Tick(gFar30, none, 0);
        r = gH.OnTranscript("Test Field Approach, Enfield 11, inbound for landing", gFar30, none, 1);
        Check(r[0].Contains("heading") && r[0].Contains("maintain 3000 feet") && r[0].Contains("miles to the holding point") && r[0].Contains("number 4"),
              "Hinführen zum Haltepunkt: " + r[0]);
        // N23: airfield with TACAN -> holding as radial/DME instead of "miles to the holding point"/"present position"
        gf.Tacan = 44;
        var gT = new Tower(gf, "Enfield 1-1") { QueueAhead = 3, Spaced = false };
        gT.Tick(gFar30, none, 0);
        r = gT.OnTranscript("Test Field Approach, Enfield 11, inbound for landing", gFar30, none, 1);
        Check(Regex.IsMatch(r[0].Text, @"hold on the (\w+ ){3}radial, 15 DME, \d+ miles") && !r[0].Contains("holding point"), "N23 Holding am Radial: " + r[0]);
        var gNear = G(27780, -1000, 1000, 180, 150);   // already at the holding point (15 NM)
        var gT2 = new Tower(gf, "Enfield 1-1") { QueueAhead = 3, Spaced = false };
        gT2.Tick(gNear, none, 0);
        r = gT2.OnTranscript("Test Field Approach, Enfield 11, inbound for landing", gNear, none, 1);
        Check(Regex.IsMatch(r[0].Text, @"Hold on the (\w+ ){3}radial, 15 DME, orbit left hand"), "N23 Holding am Radial, am Punkt: " + r[0]);
        gf.Tacan = 0;
        r = gH.OnTranscript("Approach: cancel approach", gFar30, none, 40, true);
        Check(r[0].Contains("approach cancelled"), "Abmelden im Holding: " + r[0]);
        r = g2.OnTranscript("Enfield 11 C R P north", gFar, none, 2);
        Check(r[0].Contains("no reporting points") && r[0].Contains("heading"), r[0]);
        var gIp = g2.OnCenterline(g2.Runway, InitialDist);
        r = g2.OnTranscript("Test Field Tower, Enfield 11, initial", G(gIp.X, gIp.Z, 450, g2.LandHdg(g2.Runway), 150), none, 3);
        Check(r.Count == 1 && r[0].Contains("runway one three, wind calm, QNH one zero one three, report base"), r.FirstOrDefault()?.Text ?? "Initial erreicht: -");

        // --- Controllers: wrong one called -> referral, check ATIS identifier
        var tw11 = new Tower("Enfield 1-1");
        tw11.Tick(parked, none, 0);
        r = tw11.OnTranscript("Kutaisi Tower, Enfield 11, request startup", parked, none, 1);
        Check(r[0].Role == "Tower" && r[0].Contains("contact Kutaisi Ground two six four decimal five") && tw11.Phase == Phase.Parked, r[0]);
        tw11.AtisLetter = "C";
        r = tw11.OnTranscript("Kutaisi Ground, Enfield 11, request startup", parked, none, 2);
        Check(r[0].Role == "Ground" && r[0].Contains("Information Charlie is current"), r[0]);
        Check(r[0].Contains("start up approved. Information Charlie is current, runway two five in use, wind calm, QNH"), "A33/R209 Erstanruf ohne ATIS-Kennung: Kennung, Bahn, Wind, QNH: " + r[0]);
        var wP = At(parked.X, parked.Z) with { WindX = 5 };
        var twW209 = new Tower("Enfield 1-1");
        twW209.Tick(wP, none, 0);
        r = twW209.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", wP, none, 1);
        Check(r[0].Contains("taxi to holding point") && r[0].Contains("degrees,") && r[0].Contains("knots, QNH"), "R209 Rollfreigabe ohne ATIS nennt den Wind vor dem QNH: " + r[0]);
        r = tw11.OnTranscript("Kutaisi Ground, Enfield 11, with information Charlie, request startup", parked, none, 3);
        Check(!r[0].Contains("Information Charlie"), "richtige ATIS-Kennung: " + r[0]);
        // A33: "with/have/information Charlie" in the initial call -> neither runway nor QNH; the taxi clearance afterwards does not repeat the QNH
        foreach (var call in new[] { "with information Charlie, request startup", "have Charlie, request startup", "information Charlie, request startup", "with Charlie, request startup" })
        {
            var twA33a = new Tower("Enfield 1-1") { AtisLetter = "C" };
            twA33a.Tick(parked, none, 0);
            r = twA33a.OnTranscript("Kutaisi Ground, Enfield 1-1, " + call, parked, none, 1);
            Check(r[0].Text == "Enfield one one, Kutaisi Ground, start up approved. Report ready to taxi.", $"A33 \"{call}\": " + r[0]);
        }
        // A39: 10 km from the airfield on the ground (FARP) -> no Ground radio, info only, phase stays
        var twA39 = new Tower("Enfield 1-1");
        var farp = At(-284246.6 + 10000, 683966.5);
        twA39.Tick(farp, none, 0);
        foreach (var call in new[] { "request startup", "request taxi" })
        {
            r = twA39.OnTranscript("Kutaisi Ground, Enfield 1-1, " + call, farp, none, 1);
            Check(r.Count == 1 && r[0].Role == "Info" && r[0].Contains("Approach") && twA39.Phase == Phase.Parked, $"A39 FARP \"{call}\": " + r.FirstOrDefault()?.Text);
        }
        r = twA39.OnTranscript("Kutaisi Tower, Enfield 1-1, mayday", farp, none, 2);
        Check(r.Count > 0 && r[0].Role != "Info", "A39 Notfall bleibt: " + r.FirstOrDefault()?.Text);
        // Review b-gnd-local-only: at the FARP also no "ready" to the airfield Ground, no A40 taxi warning, no "departed without takeoff clearance" (5-6 km)
        var twA39b = new Tower("Enfield 1-1");
        var farp55 = At(-284246.6 + 5500, 683966.5);
        twA39b.Tick(farp55, none, 0);
        r = twA39b.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", farp55, none, 1);
        Check(r.Count == 1 && r[0].Role == "Info" && twA39b.Rejected == 0, "A39 FARP ready: " + r.FirstOrDefault()?.Text);
        r = twA39b.Tick(At(-284246.6 + 5500 + 200, 683966.5, 0, 90, 10 * Kt), none, 2);
        Check(r.Count == 0, "A39 FARP Rollen ohne A40: " + string.Join(" | ", r.Select(m => m.Text)));
        r = twA39b.Tick(At(-284246.6 + 5500 + 200, 683966.5, 60, 90, 60 * Kt), none, 3);
        Check(!r.Any(m => m.Text.Contains("without takeoff clearance")), "A39 FARP Abheben ohne Verstoß: " + string.Join(" | ", r.Select(m => m.Text)));
        var twA33b = new Tower("Enfield 1-1") { AtisLetter = "C" };
        twA33b.Tick(parked, none, 0);
        twA33b.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", parked, none, 1);   // without identifier: gets runway and QNH once ...
        r = twA33b.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 5);
        Check(r[0].Contains("taxi to holding point runway two five") && !r[0].Contains("QNH") && !r[0].Contains("Information"), "A33 Rollfreigabe nach dem Anlassen ohne QNH: " + r[0]);   // ... the taxi clearance does not repeat it
        var atis = tw11.AtisText("C", At(CX, CZ, 2) with { WindX = 5, Pressure = 101325 }, -5, false, 0, 50000);
        Check(atis.StartsWith("Kutaisi information Charlie. Runway two five in use") && atis.Contains("Temperature minus five") &&
              atis.Contains("QNH one zero one eight"), atis);   // R345: 1018.9 hPa truncated
        Check(QnhSay(1012.9, false) == "QNH one zero one two" && QnhSay(29.879 / 0.02953, true) == "altimeter two niner eight seven" && QnhSay(1013.25, null) == "QNH one zero one three, altimeter two niner niner two",
              "R345 QNH/Altimeter abgeschnitten: " + QnhSay(1012.9, null) + " | " + QnhSay(29.879 / 0.02953, true));
        Check(tw11.AtisText("C", At(CX, CZ, 2), 5, true, 1045, 9000).Contains("clouds 3300 feet"), "A34 ATIS-Wolken über Platz (Basis 1045 m NN, Platz 45 m)");
        {   // R340: IFR per airfield from the ceiling above the field; FEW/SCT (R367) is no ceiling
            var skyR340 = Sky;
            var fHi = Airfield.Kutaisi(); fHi.Elev = 524;   // Beslan elevation
            var fLo = Airfield.Kutaisi(); fLo.Elev = 10;    // Batumi elevation
            var (twIfrHi, twIfrLo) = (new Tower(fHi, "Enfield 1-1"), new Tower(fLo, "Enfield 1-1"));
            Sky = (true, 700, 20000);
            bool hiIfr = twIfrHi.Ifr, loVfr = !twIfrLo.Ifr;
            string aHi = twIfrHi.AtisText("C", At(CX, CZ, 2), 5, true, 700, 20000), aLo = twIfrLo.AtisText("C", At(CX, CZ, 2), 5, true, 700, 20000);
            Sky = (true, 550, 20000); bool lo550 = !twIfrLo.Ifr;
            Sky = (false, 700, 20000); bool fewSct = !twIfrHi.Ifr;
            Sky = (false, 700, 3000); bool vis = twIfrLo.Ifr;
            Sky = skyR340;
            Check(hiIfr && loVfr && lo550 && fewSct && vis && aHi.Contains("Instrument conditions") && !aLo.Contains("Instrument conditions"),
                  $"R340 IFR je Platz (Basis 700 m: Beslan {hiIfr}, Batumi VFR {loVfr}; 550 m Batumi VFR {lo550}; FEW/SCT VFR {fewSct}; Sicht 3 km {vis}): {aHi} | {aLo}");
        }
        var atFog = tw11.AtisText("C", At(CX, CZ, 2), 5, false, 0, 820);
        var atMist = tw11.AtisText("C", At(CX, CZ, 2), 5, false, 0, 3000);
        Check(atFog.Contains("Visibility 800 meters, fog, sky clear") && atMist.Contains("Visibility three kilometers, mist, sky clear") &&
              !tw11.AtisText("C", At(CX, CZ, 2), 5, false, 0, 6000).Contains("mist"), "R208 ATIS Sichtbehinderung: " + atFog + " | " + atMist);
        // Forum finding: altimeter unit by type (auto) or setting; ATIS in auto mode with both; readback "altimeter 29.92"
        string Alti(string type, string unit, double at = 1)
        {
            AltimeterUnit = unit;
            var w = new Tower("Enfield 1-1") { AcType = type, FieldQnh = 1013.25 };
            w.Tick(parked, none, 0);
            return w.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", parked, none, at)[0].Text + " | " + w.AtisCall("C", parked);
        }
        string[] alti = { Alti("F-14B", "auto"), Alti("Su-27", "auto"), Alti("Su-27", "inhg"), Alti("F-14B", "hpa"), Alti("F-14B", "qnh") };
        var twAl = new Tower("Enfield 1-1") { AcType = "F-14B" };
        twAl.Tick(west, none, 0);
        twAl.last = new Msg("Approach", "Enfield one one, radar contact, altimeter two niner niner two.");
        var alRb = Rb(twAl, west, 2, "Altimeter 29.92, Enfield 1-1");
        AltimeterUnit = "auto";
        Check(alti[0].StartsWith("Enfield one one, Kutaisi Ground, start up approved. Runway two five in use, wind calm, altimeter two niner niner two. Report") &&
              alti[0].EndsWith("now current, QNH one zero one three, altimeter two niner niner two.") &&
              alti[1].Contains("in use, wind calm, QNH one zero one three. Report") && alti[2].Contains("in use, wind calm, altimeter two niner niner two. Report") && alti[2].EndsWith("now current, altimeter two niner niner two.") &&
              alti[3].Contains("in use, wind calm, QNH one zero one three. Report") && alti[3].EndsWith("now current, QNH one zero one three.") && alti[4] == alti[0] && alRb == "",
              "Höhenmesser F-14B auto/Su-27 auto/Su-27 inhg/F-14B hpa/unbekannter Wert wie auto, ATIS: " + string.Join(" || ", alti) + " | Rücklesung: " + alRb);
        // K2: with Radio.lua every handoff with frequency, otherwise VHF (= map), UHF aircraft UHF, other airfield in the same band; K5: ATIS names Tower VHF and UHF
        var kr = Airfield.Kutaisi(); (kr.Uhf, kr.Vhf) = (263, 134);
        var senK2 = new Airfield { Name = "Senaki-Kolkhi", Uhf = 261, Vhf = 132 };
        Tower twK2v = new(kr, "Enfield 1-1"), twK2u = new(kr, "Enfield 1-1") { HeardOn = 263 };
        twK2v.Tick(parked, none, 0); twK2u.Tick(parked, none, 0);
        var k2 = new[] { twK2v.OnTranscript("Kutaisi Tower, Enfield 11, request startup", parked, none, 1)[0].Text, twK2u.OnTranscript("Kutaisi Tower, Enfield 11, request startup", parked, none, 1)[0].Text,
                         twK2v.OtherField("Senaki Tower, Enfield 11", senK2)[0].Text, twK2u.OtherField("Senaki Tower, Enfield 11", senK2)[0].Text,
                         twK2v.AtisText("C", At(CX, CZ, 2) with { WindX = 5, Pressure = 101325 }, -5, false, 0, 50000) };
        // R104: own airfield, one map frequency for Ground/Tower/Approach -> no more "contact Kutaisi Ground …", Ground replies right away (frequency only to the other airfield)
        Check(k2[0].Contains("Kutaisi Ground, start up approved") && k2[1].Contains("Kutaisi Ground, start up approved") && !k2[0].Contains("contact") && !k2[1].Contains("contact")
              && k2[2].EndsWith("contact Senaki Kolkhi Tower one three two decimal zero.") && k2[3].EndsWith("contact Senaki Kolkhi Tower two six one decimal zero.")
              && k2[4].Contains("Tower one three four decimal zero, UHF two six three decimal zero."), "K2/K5 Frequenz im Spruch: " + string.Join(" | ", k2));
        // R104: Ground/Tower on one map frequency: no "contact" without frequency change, the wrongly called facility replies as the responsible one (no rejection)
        var twR104 = new Tower(kr, "Enfield 1-1");
        twR104.Tick(parked, none, 0);
        var r104 = new List<string> { twR104.OnTranscript("Kutaisi Ground, Enfield 1-1, ready for departure", parked, none, 1)[0].Text,
                                      twR104.OnTranscript("Kutaisi Tower, Enfield 1-1, request taxi", parked, none, 2)[0].Text };
        int rej104 = twR104.Rejected;
        r104.Add(twR104.OnTranscript("Kutaisi Ground, Enfield 1-1, ready for departure", hold, none, 3)[0].Text);
        var twR104l = new Tower(kr, "Enfield 1-1");
        twR104l.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0); twR104l.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1); twR104l.Tick(At(CX, CZ, 0, 254, 95 * Kt), none, 2);
        r104.Add((twR104l.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3).FirstOrDefault()?.Text ?? "-").Split(" Possible pilot deviation")[0]);   // Landing without clearance: violation addendum (main) with "contact … after parking" does not belong to R104
        Check(r104[0].EndsWith("negative, you are not cleared to taxi. Request taxi.") && r104[1].Contains("Hold short runway two five, report ready for departure.") &&
              r104[2].Contains("cleared for takeoff") && rej104 == 1 && twR104.Rejected == 1 && r104[3].Contains("vacate") && r104[3].EndsWith("Report vacated.") && r104.All(s => !s.Contains("ontact")),
              "R104 dieselbe Frequenz: " + string.Join(" | ", r104));
        // R269: A12 (rolled back) and A91 (no Ground call after vacating) on one frequency: no "contact Kutaisi Ground", but "say intentions" or "request taxi"
        var tw269 = new Tower(kr, "Enfield 1-1");
        var off269 = At(CX - 400 * Uz, CZ + 400 * Ux, 0, 254, 0);
        tw269.Tick(off269, none, 0);
        tw269.Phase = Phase.ClearedTakeoff;
        var back269 = At(Thr25X - 1000 * Uz, Thr25Z + 1000 * Ux, 0, 0, 0);
        tw269.Tick(back269, none, 4); var a12r = tw269.Tick(back269, none, 40);
        tw269.Phase = Phase.TaxiIn; tw269.Tick(off269, none, 50); var a91r = tw269.Tick(off269, none, 90);
        Check(a12r.Count == 1 && a12r[0].Text.EndsWith("takeoff clearance cancelled, say intentions.") && a91r.Count == 1 && a91r[0].Text.EndsWith(", request taxi."),
              "R269 dieselbe Frequenz ohne contact Ground: " + string.Join("|", a12r.Select(x => x.Text)) + " / " + string.Join("|", a91r.Select(x => x.Text)));

        // --- Touch-and-go: clearance, after the go-around next pattern
        var tw12 = new Tower("Enfield 1-1");
        tw12.Tick(dw, none, 0);
        r = tw12.OnTranscript("Enfield 11 final, touch and go", onFinal, none, 1);
        Check(r[0].Contains("cleared touch and go, left closed traffic approved, report base"), "A96: " + r[0]);
        var (px, pz) = P("25", -1500);
        r = tw12.Tick(At(px, pz, 150, LandHdg("25"), 130), none, 2);
        Check(tw12.Laps == 1 && tw12.Phase == Phase.Pattern && r.Count == 1 && r[0].Role == "Info",   // A96: quiet after liftoff, the pattern was in the clearance
              r.FirstOrDefault()?.Text ?? "Platzrunde: -");
        // R258: reported go-around (VFR): acknowledge instead of a second go-around instruction, with pattern request "closed traffic approved"
        var tw258 = new Tower("Enfield 1-1");
        tw258.Tick(dw, none, 0);
        var r258 = new List<string> { tw258.OnTranscript("Enfield 11 going around", onFinal, none, 1)[0].Text };
        var tw258c = new Tower("Enfield 1-1");
        tw258c.Tick(dw, none, 0);
        tw258c.OnTranscript("Enfield 11 final, touch and go", onFinal, none, 1);
        r258.Add(tw258c.OnTranscript("Enfield 11 going around", onFinal, none, 3)[0].Text);
        Check(r258[0] == "Enfield one one, roger, climb and maintain 2000 feet, join left hand downwind runway two five, report base." &&
              r258[1] == "Enfield one one, roger, left closed traffic approved, report base." && tw258c.Phase == Phase.Pattern, "R258 Durchstarten gemeldet: " + string.Join(" | ", r258));
        // R259: "request closed" with landing clearance states the changed clearance (low approach)
        var tw259 = new Tower("Enfield 1-1");
        tw259.Tick(dw, none, 0);
        var r259 = new List<string> { tw259.OnTranscript("Enfield 11 final, gear down, full stop", onFinal, none, 1)[0].Text };
        r259.Add(tw259.OnTranscript("Kutaisi Tower, Enfield 11, request closed traffic", onFinal, none, 3)[0].Text);
        Check(r259[0].EndsWith("cleared to land.") && r259[1] == "Enfield one one, cleared low approach, left closed traffic approved, report base." && tw259.option == "low approach",
              "R259 request closed aus der Landefreigabe: " + string.Join(" | ", r259));
        // R261: wheels-down check also on low approach (FAA JO 7110.65 2-1-24), not on low pass
        var tw261 = new Tower("Enfield 1-1");
        tw261.Tick(dw, none, 0);
        var r261 = tw261.OnTranscript("Enfield 11 final, low approach", onFinal, none, 1)[0].Text;
        Check(r261.StartsWith("Enfield one one, check wheels down, runway two five, ") && r261.Contains("cleared low approach"), "R261 Low approach mit Wheels-down-Check: " + r261);
        // R127: "the option" -> "cleared for the option", touch-and-go (over 50 kt) = next pattern, full stop (under 50 kt) = vacate; "request the option" is understood
        var twOp = new Tower("Enfield 1-1");
        twOp.Tick(dw, none, 0);
        r = twOp.OnTranscript("Enfield 11 final, the option", onFinal, none, 1);
        Check(twOp.Phase == Phase.ClearedLand && r[0].Contains("cleared for the option, left closed traffic approved"), "R127 final, the option: " + r[0]);
        r = twOp.Tick(At(px, pz, 150, LandHdg("25"), 130), none, 2);
        Check(twOp.Laps == 1 && twOp.Phase == Phase.Pattern, "R127 Touch-and-go aus the option: " + string.Join(" | ", r));
        var twOs = new Tower("Enfield 1-1");
        twOs.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
        twOs.option = "the option";
        var os = Enumerable.Range(1, 4).SelectMany(s => twOs.Tick(At(CX, CZ, 0, 254, 75 * Kt), none, s)).Where(m => m.Role != "Info").ToList();
        Check(twOs.Phase != Phase.TaxiIn && os.Count == 0, "R127 the option bei 75 kt noch kein Full stop: " + string.Join(" | ", os));
        os = Enumerable.Range(5, 3).SelectMany(s => twOs.Tick(At(CX, CZ, 0, 254, 45 * Kt), none, s)).Where(m => m.Role != "Info").ToList();
        Check(twOs.Phase == Phase.TaxiIn && os.Any(m => m.Text.Contains("vacate")), "R127 the option unter 50 kt = Full stop: " + string.Join(" | ", os));
        var twOr = new Tower("Enfield 1-1");
        twOr.Tick(dw, none, 0);
        r = twOr.OnTranscript("Kutaisi Tower, Enfield 11, request the option", dw, none, 1);
        Check(twOr.option == "the option" && r.Count == 1 && !r[0].Contains("say again"), "R127 request the option: " + r.FirstOrDefault()?.Text);
        // R254: "request touch and go" from far out: at Tower "contact Approach", at Approach initial call (weather, entry), option remains
        var tw254 = new Tower("Enfield 1-1");
        tw254.Tick(west, none, 0);
        var r254 = new List<string> { tw254.OnTranscript("Kutaisi Tower, Enfield 11, request touch and go", west, none, 1)[0].Text,
                                      tw254.OnTranscript("Kutaisi Approach, Enfield 11, request touch and go", west, none, 2)[0].Text };
        Check(r254[0] == "Enfield one one, Kutaisi Tower, contact Kutaisi Approach two six six decimal five." && r254[1].Contains("QNH") && !r254[1].Contains("approved") &&
              tw254.Phase == Phase.Inbound && tw254.option == "touch and go", "R254 Option von außen: " + string.Join(" | ", r254));
        var tw12b = new Tower("Enfield 1-1");
        tw12b.Tick(parked, none, 0);
        tw12b.OnTranscript("Enfield 11 request taxi", parked, none, 1);
        r = tw12b.OnTranscript("Enfield 11 ready for departure, closed pattern", hold, none, 2);
        Check(r[0].Text == "Enfield one one, runway two five, wind calm, cleared for takeoff, left closed traffic approved, report base.", "R200 Start zur Platzrunde: " + r[0]);   // FAA JO 7110.65 3-10-11
        // R253: IMC: "closed pattern" rejected, "practice approach" = radar pattern (runway heading/altitude, after liftoff contact Approach, check-in -> vectors)
        ForceIfr = true;
        var tw253 = new Tower("Enfield 1-1");
        tw253.Tick(parked, none, 0);
        tw253.OnTranscript("Enfield 11 request taxi, pattern work", parked, none, 1);
        var r253 = new List<string> { tw253.Suggest(hold)?.Text ?? "-", tw253.OnTranscript("Enfield 11 ready for departure, closed pattern", hold, none, 2)[0].Text,
                                      tw253.OnTranscript("Enfield 11 ready for departure, practice approach", hold, none, 3)[0].Text };
        var up253 = At(px, pz, 150, LandHdg("25"), 130);
        r253.Add(string.Join(" ", tw253.Tick(up253, none, 5).Select(m => m.Text)));
        r253.Add(tw253.Suggest(up253)?.Text ?? "-");
        r253.Add(tw253.OnTranscript("Kutaisi Approach, Enfield 11, airborne, climbing", up253, none, 8)[0].Text);
        ForceIfr = false;
        Check(r253[0] == "ready for departure, practice approach" && r253[1] == "Enfield one one, closed traffic not approved, I F R conditions." &&
              r253[2].StartsWith("Enfield one one, after departure fly runway heading, climb and maintain ") && r253[2].EndsWith("cleared for takeoff.") && !r253[2].Contains("downwind") &&
              r253[3].Contains("contact Kutaisi Approach") && r253[4] == "airborne, climbing" && r253[5].Contains("identified") && r253[5].Contains("heading") && !r253[5].Contains("say intentions") &&
              tw253.Phase == Phase.Inbound && tw253.stayPattern, "R253 Radarplatzrunde IMC: " + string.Join(" | ", r253));
        {   // R339: IMC without IFR clearance: no VFR taxi and no VFR takeoff; after the clearance the normal flow
            ForceIfr = true;
            var tw339 = new Tower("Enfield 1-1");
            tw339.Tick(parked, none, 0);
            var tx339 = tw339.OnTranscript("Kutaisi Ground, Enfield 11, request taxi", parked, none, 1)[0].Text;
            bool parked339 = tw339.Phase == Phase.Parked;
            ForceIfr = false;
            tw339.OnTranscript("Kutaisi Ground, Enfield 11, request taxi", parked, none, 2);
            ForceIfr = true;   // weather drops while taxiing
            var rd339 = tw339.OnTranscript("Kutaisi Tower, Enfield 11, ready for departure", hold, none, 3)[0].Text;
            var tk339 = tw339.Tick(hold, none, 200);
            tw339.OnTranscript("Kutaisi Ground, Enfield 11, request IFR clearance", hold, none, 210);
            var ok339 = tw339.OnTranscript("Kutaisi Tower, Enfield 11, ready for departure", hold, none, 220)[0].Text;
            ForceIfr = false;
            Check(tx339 == "Enfield one one, Kutaisi Ground, field is I F R, unable VFR departure, advise ready to copy IFR clearance." && parked339 &&
                  rd339 == "Enfield one one, field is I F R, unable VFR departure, advise ready to copy IFR clearance." && tk339.Count == 0 && ok339.Contains("cleared for takeoff"),
                  $"R339 IMC ohne IFR-Freigabe: {tx339} | {rd339} | {string.Join(" ", tk339.Select(m => m.Text))} | {ok339}");
        }

        // R32: "check altitude" only 60 s after handoff to the Tower (no spam before), then on real deviation
        var twA32 = new Tower("Enfield 1-1");
        twA32.Tick(At(CX + 3 * NM, CZ, 2500, 254, 150 * Kt), none, 0);
        twA32.Phase = Phase.Entering; twA32.handoffAt = 100;
        var hi = At(CX + 3 * NM, CZ, 2500, 254, 150 * Kt);   // far above control zone altitude, not descending
        bool Alt32(double now) => twA32.Tick(hi, none, now).Any(m => m.Text.Contains("check altitude"));
        Check(!Alt32(130) && !Alt32(155), "R32: check altitude 30-55 s nach der Übergabe");
        Check(Alt32(170), "R32: check altitude 70 s nach der Übergabe");
        var twC32 = new Tower("Enfield 1-1");   // also after the handoff at the C R P (AtCrp) not right in the next tick
        twC32.Tick(hi, none, 0); twC32.AtCrp("south");
        Check(!twC32.Tick(hi, none, 1).Any(m => m.Text.Contains("check altitude")), "R32: check altitude gleich nach Übergabe am C R P");

        // --- Emergency: priority, others wait
        var tw13 = new Tower("Enfield 1-1");
        tw13.Tick(farWest, none, 0);
        r = tw13.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", farWest, none, 1);
        // R37: Approach vectors to final (descent at own discretion), no landing clearance from 13 NM
        Check(tw13.Mayday && tw13.Phase == Phase.Inbound && tw13.Vectoring && r[0].Role == "Approach" && r[0].Text.StartsWith("Enfield one one, Kutaisi Approach, roger mayday, all traffic is holding.") &&
              r[0].Contains("heading") && r[0].Contains("descend at pilot's discretion") && r[0].Contains("say souls on board and fuel remaining") && !r[0].Contains("cleared to land"), "Mayday: " + r[0]);
        r = tw13.OnTranscript("Enfield 11, 30 minutes of fuel, one person on board", farWest, none, 2);
        Check(r.Count == 1 && r[0].Text == "Enfield one one, roger.", "Mayday: Antwort auf die Rückfrage (A30): " + (r.FirstOrDefault()?.Text ?? "-"));
        tw13.vec = null; tw13.Phase = Phase.Entering; tw13.navName = "six mile final";   // handed over (Established)
        var fin5 = At(P("25", 5 * NM).Item1, P("25", 5 * NM).Item2, 450, LandHdg("25"), 80);
        r = tw13.Tick(fin5, none, 3);
        Check(tw13.Phase == Phase.ClearedLand && r.Count == 1 && r[0].Role == "Tower" && r[0].Contains("runway two five") && r[0].Contains("cleared to land, emergency services standing by"),
              "Mayday: Landefreigabe bei 5 NM: " + (r.FirstOrDefault()?.Text ?? "-"));
        // Mayday close to the airfield on the way to the initial: the landing clearance ends navigation (no "fly heading … to initial" on short final)
        var twN = new Tower("Enfield 1-1");
        Telemetry OnFin(double nm) => At(P("25", nm * NM).Item1, P("25", nm * NM).Item2, nm * 95, LandHdg("25"), 120);
        twN.Tick(OnFin(5.8), none, 0); twN.Phase = Phase.Entering; (twN.nav, twN.navName) = (P("25", InitialDist), "initial");
        r = twN.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", OnFin(5.8), none, 1);
        var nInit = Enumerable.Range(1, 50).SelectMany(s => twN.Tick(OnFin(5.8 - 5.3 * s / 50), none, 1 + 2 * s)).Where(m => m.Text.Contains("initial")).ToList();
        Check(r[0].Contains("cleared to land, emergency services standing by") && nInit.Count == 0, "Mayday nah am Platz: Navigation zum Initial bleibt: " + (nInit.FirstOrDefault()?.Text ?? r[0].Text));
        // R295: "cancel emergency" with landing clearance: clearance remains; without emergency or far out (Away) only "roger", no downwind
        var tw295 = new Tower("Enfield 1-1");
        tw295.Tick(OnFin(3), none, 0);
        tw295.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", OnFin(3), none, 1);
        var c295 = tw295.OnTranscript("Kutaisi Tower, Enfield 11, cancel emergency", OnFin(2.8), none, 5);
        var tw295b = new Tower("Enfield 1-1");
        tw295b.Tick(farWest, none, 0);
        var n295 = tw295b.OnTranscript("Kutaisi Approach, Enfield 11, cancel emergency", farWest, none, 1);
        tw295b.Announce(farWest, 2, true);
        var a295 = tw295b.OnTranscript("Kutaisi Approach, Enfield 11, cancel mayday", farWest, none, 3);
        Check(tw295.Phase == Phase.ClearedLand && !tw295.Emergency && c295[0].Text == "Enfield one one, roger, emergency cancelled, runway two five, cleared to land." &&
              n295[0].Text == "Enfield one one, roger." && a295[0].Text == "Enfield one one, roger." && tw295b.Phase == Phase.Away && !tw295b.Emergency,
              "R295 cancel emergency: " + string.Join(" | ", c295.Concat(n295).Concat(a295).Select(m => m.Text)));
        // R357: MAYDAY close in with the runway occupied: "continue approach", no clearance; runway clear -> clearance (tick); occupied again on short final -> go around
        var tw357 = new Tower("Enfield 1-1");
        tw357.Tick(OnFin(3), none, 0); tw357.RunwayClaimed = 1;
        var m357 = tw357.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", OnFin(3), none, 1);
        tw357.RunwayClaimed = 0;
        var c357 = tw357.Tick(OnFin(2.5), none, 3);
        tw357.RunwayClaimed = 1;
        var g357 = tw357.Tick(OnFin(0.5), none, 5);
        Check(m357[0].Contains("roger mayday") && m357[0].Contains("continue approach, traffic departing runway two five, will advise") && !m357[0].Contains("cleared to land") &&
              c357.Any(m => m.Contains("cleared to land, emergency services standing by")) && g357.Any(m => m.Contains("go around")),
              "R357 Notfall bei belegter Bahn: " + string.Join(" | ", m357.Concat(c357).Concat(g357).Select(m => m.Text)));
        // R357: still on the runway after landing, MAYDAY of another at 4 NM -> once "vacate runway immediately"
        var tw357v = new Tower("Enfield 1-1");
        tw357v.Tick(At(CX, CZ, 300, 254, 140 * Kt), none, 0);
        tw357v.Tick(At(CX, CZ, 0, 254, 130 * Kt), none, 1);
        tw357v.Tick(At(CX, CZ, 0, 254, 90 * Kt), none, 3);
        (tw357v.OtherEmergency, tw357v.EmergencyNm) = (true, 4);
        var v357 = tw357v.Tick(At(CX, CZ, 0, 254, 0), none, 5);
        var v357b = tw357v.Tick(At(CX, CZ, 0, 254, 0), none, 6);
        Check(v357.Count == 1 && v357[0].Contains("vacate runway immediately, emergency traffic 4 miles") && !v357b.Any(m => m.Contains("immediately")),
              "R357 Bahn räumen bei Mayday: " + string.Join(" | ", v357.Concat(v357b).Select(m => m.Text)));
        // PAN: priority only, others do not hold
        var twPan = new Tower("Enfield 1-1");
        twPan.Tick(farWest, none, 0);
        r = twPan.OnTranscript("Pan pan, pan pan, pan pan, Enfield 11, hydraulic failure, request priority landing", farWest, none, 1);
        Check(twPan.Emergency && !twPan.Mayday && twPan.Vectoring && r[0].Contains("roger pan pan.") && !r[0].Contains("all traffic is holding"), "Pan: " + r[0]);
        // R362: PAN survives the session restore; older session files without the type: MAYDAY
        var twPanR = new Tower("Enfield 1-1"); twPanR.Load(twPan.Save(), farWest, 2);
        var twPanO = new Tower("Enfield 1-1"); twPanO.Load(twPan.Save() with { Mayday = null }, farWest, 2);
        Check(twPanR.Emergency && !twPanR.Mayday && twPanO.Mayday, $"R362 PAN nach Neustart: Mayday {twPanR.Mayday}, alte Datei {twPanO.Mayday}");
        // R244: radio-wheel emergency call states fuel and persons (PilotCall) -> no query
        var tw244 = new Tower("Enfield 1-1");
        tw244.Tick(farWest, none, 0);
        r = tw244.OnTranscript("mayday mayday mayday, engine failure, request immediate landing", farWest, none, 1, true);
        Check(tw244.Mayday && r[0].Contains("roger mayday") && !r[0].Contains("fuel remaining"), "R244 Funkrad-Notruf ohne Rückfrage: " + r[0]);
        // R300: ground MAYDAY with nature (radio wheel: "…, engine failure, shutting down") without query, without nature "say nature of emergency"
        var tw300 = new Tower("Enfield 1-1");
        tw300.Tick(parked, none, 0);
        var g300 = tw300.OnTranscript("MAYDAY MAYDAY MAYDAY, Kutaisi Ground, Enfield 1 1, engine failure, shutting down", parked, none, 1, true);
        var tw300b = new Tower("Enfield 1-1");
        tw300b.Tick(parked, none, 0);
        var g300b = tw300b.OnTranscript("Mayday mayday mayday, Kutaisi Ground, Enfield 11", parked, none, 1);
        Check(g300[0].Text.EndsWith("roger mayday, emergency services are on the way. Hold position.") && g300b[0].Text.EndsWith("Hold position, say nature of emergency."),
              "R300 Boden-Notruf: " + g300[0].Text + " / " + g300b[0].Text);
        // R250: emergency announced by AWACS: priority immediately, Approach silent until the initial call, then full emergency handling
        var tw250 = new Tower("Enfield 1-1");
        tw250.Announce(farWest, 0, true);
        var q250 = Enumerable.Range(1, 30).SelectMany(s => tw250.Tick(farWest, none, s)).ToList();
        r = tw250.OnTranscript("Kutaisi Approach, Enfield 11, mayday, engine fire", farWest, none, 31);
        Check(tw250.Mayday && q250.Count == 0 && r[0].Contains("roger mayday, all traffic is holding") && tw250.Vectoring, "R250 angekündigter Notfall: " + string.Join(" | ", q250.Concat(r).Select(m => m.Text)));
        // R292: emergency under radar vectoring, flies stubbornly 250 and stays high: no "expedite descent", no removal/violation, once "say intentions"
        var tw292 = new Tower("Enfield 1-1");
        tw292.Tick(west, none, 0);
        tw292.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", west, none, 1);
        var st292 = At(-291000, 660000, (tw292.vecFt + 1500) * Ft - FieldElev, 250, 120);
        var m292 = Enumerable.Range(2, 600).SelectMany(s => tw292.Tick(st292, none, s)).Select(m => m.Text).ToList();
        Check(tw292.Emergency && tw292.Phase == Phase.Inbound && tw292.Vectoring && m292.Count(m => m.EndsWith("say intentions.")) == 1 &&
              !m292.Any(m => m.Contains("expedite") || m.Contains("removed from the sequence") || m.Contains("deviation") || m.Contains("verify") || m.Contains("maintain") && !m.Contains("pilot's discretion, maintain")), "R292 Notfall nicht gedrängt: " + string.Join(" | ", m292));
        // Others on MAYDAY within 15 NM: leave the pattern and hold; on short final landing clearance with "expedite vacating" if the emergency is farther than 8 NM
        var twO = new Tower("Enfield 1-1");
        var dwO = At(CX - NM, CZ + 1.5 * NM, 300, LandHdg("25") + 180, 80);
        twO.Tick(dwO, none, 0); twO.Phase = Phase.Pattern;
        (twO.OtherEmergency, twO.EmergencyNm) = (true, 20);
        Check(!twO.Tick(dwO, none, 1).Any(m => m.Text.Contains("emergency in progress")), "Mayday 20 NM: andere bleiben in der Platzrunde");
        twO.EmergencyNm = 12;
        r = twO.Tick(dwO, none, 2);
        Check(twO.HoldInfo != null && twO.Phase == Phase.Inbound && r.Count == 1 && r[0].Text.StartsWith("Enfield one one, emergency in progress, leave the pattern, fly heading") && r[0].Contains("I will call you"),
              "Mayday 12 NM: andere halten: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twF = new Tower("Enfield 1-1");
        var fin2 = At(P("25", 2 * NM).Item1, P("25", 2 * NM).Item2, 190, LandHdg("25"), 70);
        twF.Tick(fin2, none, 0); twF.Phase = Phase.Pattern;
        (twF.OtherEmergency, twF.EmergencyNm) = (true, 10);
        r = twF.OnTranscript("Kutaisi Tower, Enfield 11, final, gear down", fin2, none, 1);   // Reporting obligation: landing clearance on report
        Check(twF.Phase == Phase.ClearedLand && r.Count == 1 && r[0].Contains("cleared to land, expedite vacating, emergency traffic 10 miles"), "Mayday 10 NM, anderer im Endanflug: " + (r.FirstOrDefault()?.Text ?? "-"));
        twF.EmergencyNm = 6;   // Emergency approaching, runway becomes free before it: no go-around (only under 4 NM)
        Check(!twF.Tick(At(P("25", 0.7 * NM).Item1, P("25", 0.7 * NM).Item2, 70, LandHdg("25"), 70), none, 2).Any(m => m.Text.Contains("go around")), "Mayday 6 NM: kein Go-around");
        // R101: "emergency" read back from the controller call is not an emergency call, own "emergency" is
        var tw13b = new Tower("Enfield 1-1") { OtherEmergency = true };
        tw13b.Tick(farWest, none, 0);
        tw13b.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", farWest, none, 1);
        r = tw13b.OnTranscript("Roger, emergency in progress, Enfield 1-1", farWest, none, 2);
        Check(r.Count == 0 && !tw13b.Emergency, "R101 Rücklesung emergency in progress: " + string.Join(" | ", r.Select(m => m.Text)));
        r = tw13b.OnTranscript("Kutaisi Approach, Enfield 1-1, declaring emergency, engine failure", farWest, none, 3);   // R242: like MAYDAY, acknowledgement without keyword
        Check(tw13b.Mayday && r[0].Contains("Approach, roger, all traffic is holding.") && !r[0].Contains("pan pan") && !r[0].Contains("mayday"), "R101/R242 eigenes emergency: " + r[0]);
        // R112: go-around only from an approach here; "airborne, climbing" in the pattern is not a first contact
        var tw13c = new Tower("Enfield 1-1");
        var far35 = At(CX - 35 * NM, CZ, 2400, 90, 200);
        tw13c.Tick(far35, none, 0);
        r = tw13c.OnTranscript("Tower: going around", far35, none, 1, true);
        Check(tw13c.Phase == Phase.Away && r[0].Text == "Enfield one one, say intentions." && r.Any(m => m.Role == "Info"), "R112 go around 35 NM: " + string.Join(" | ", r.Select(m => m.Text)));
        var tw13d = new Tower("Enfield 1-1");
        var ipR = At(ipPos.Item1, ipPos.Item2, 600, 254, 150);
        tw13d.Tick(ipR, none, 0);
        tw13d.OnTranscript("Kutaisi Tower, Enfield 11, initial", ipR, none, 1);
        var ph13 = tw13d.Phase;
        r = tw13d.OnTranscript("Approach: airborne, climbing", ipR, none, 2, true);
        Check(ph13 == Phase.Initial && tw13d.Phase == ph13 && tw13d.Rejected == 0 && !r.Any(m => m.Text.Contains("radar contact") || m.Text.Contains("descend")), "R112 airborne im Initial: " + string.Join(" | ", r.Select(m => m.Text)));
        var tw14 = new Tower("Enfield 1-1");
        tw14.Tick(parked, none, 0);
        tw14.OnTranscript("Enfield 11 request taxi", parked, none, 1);
        tw14.OtherEmergency = true;
        r = tw14.OnTranscript("Enfield 11 ready for departure", hold, none, 2);
        Check(tw14.Phase == Phase.HoldShort && r[0].Contains("emergency traffic inbound"), r[0]);
        r = tw14.OnTranscript("Holding short two five, emergency traffic, Enfield 1-1", hold, none, 2.5);   // R101: readback, no own emergency
        Check(r.Count == 0 && !tw14.Emergency, "R101 Rücklesung emergency traffic: " + string.Join(" | ", r.Select(m => m.Text)));
        var tw14b = new Tower("Enfield 1-1");   // R111: "holding short …" reads back "hold short", with "ready" it is the departure report
        tw14b.Tick(parked, none, 0);
        tw14b.OnTranscript("Enfield 11 request taxi", parked, none, 1);
        r = tw14b.OnTranscript("Enfield 11, holding short runway two five, ready for departure", hold, none, 2);
        Check(tw14b.Phase == Phase.ClearedTakeoff && r[0].Contains("cleared for takeoff"), "R111 holding short + ready: " + string.Join(" | ", r.Select(m => m.Text)));
        var tw14c = new Tower("Enfield 1-1");   // R111: "holding short runway 25" at the holding point is the report, not a readback of Ground's "Hold short, contact Tower when ready"
        tw14c.Tick(parked, none, 0);
        var g14 = tw14c.OnTranscript("Enfield 11 request taxi", parked, none, 1);
        r = tw14c.OnTranscript("Enfield 11, holding short runway two five", hold, none, 2);
        Check(g14[0].Text.Contains("Hold short") && tw14c.Phase == Phase.ClearedTakeoff && r.Count == 1 && r[0].Contains("cleared for takeoff"), "R111 holding short ohne ready: " + string.Join(" | ", g14.Concat(r).Select(m => m.Text)));
        Check(tw14.Tick(hold, none, 3).Count == 0, "Notfall: keine Startfreigabe");
        tw14.OtherEmergency = false;
        r = tw14.Tick(hold, none, 4);
        Check(r.Count == 1 && r[0].Contains("cleared for takeoff"), r.FirstOrDefault()?.Text ?? "nach Notfall: -");

        // R116: second emergency call (PAN after MAYDAY, tw13 meanwhile on 5 NM final with landing clearance) confirms the landing clearance, no new clearance with wind/QNH, no vectors
        r = tw13.OnTranscript("Pan pan, Enfield 11, still engine trouble", fin5, none, 4);
        Check(tw13.Phase == Phase.ClearedLand && tw13.Emergency && r[0].Contains("roger pan pan") && r[0].Contains("cleared to land") && !r[0].Contains("wind")
              && !r[0].Contains("Fly heading") && !r[0].Contains("all traffic is holding"), r[0]);
        // R116: in holding (after "inbound" on second emergency) a renewed MAYDAY gets out of the holding, no "continue as cleared"
        var tw13e = new Tower("Enfield 1-1") { OtherEmergency = true };
        tw13e.Tick(farWest, none, 0);
        tw13e.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", farWest, none, 1);
        tw13e.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", farWest, none, 5);
        var held13e = tw13e.HoldInfo != null;
        r = tw13e.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", farWest, none, 40);
        Check(held13e && tw13e.HoldInfo == null && tw13e.Vectoring && r[0].Contains("all traffic is holding") && !r[0].Contains("continue as cleared"),
              "R116 Mayday im Holding: " + string.Join(" | ", r.Select(m => m.Text)));

        // --- Holding with full pattern, then free
        var busyPattern = new[]
        {
            new Traffic(21, "F-16C", CX + 1500, CZ, FieldElev + 300, 0, 80),
            new Traffic(22, "A-10C", CX - 1500, CZ, FieldElev + 300, 0, 80),
        };
        var tw15 = new Tower("Enfield 1-1");
        tw15.Tick(west, busyPattern, 0);
        r = tw15.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, busyPattern, 1);
        Check(r[0].Contains("miles to the holding point") && r[0].Text.Contains("expect approach in 6 minutes", StringComparison.OrdinalIgnoreCase), r[0]);
        for (double tt = 2; tt < 32; tt += 5) tw15.Tick(west, busyPattern, tt);
        r = tw15.Tick(west, busyPattern, 32);
        Check(r.Count == 1 && r[0].Contains("miles to the hold") && !r[0].Contains("expect") && !r[0].Contains("aircraft in the pattern"), "Hinführen zur Warteschleife (EAT steht schon im Anruf): " + (r.FirstOrDefault()?.Text ?? "-"));
        var (hx, hz, _) = tw15.HoldInfo!.Value;   // Holding point 15 NM out, off the runway axis
        double ha = tw15.HoldInfo!.Value.Ft * Ft - FieldElev;   // Holding altitude above ground (m)
        r = tw15.Tick(At(hx + 500, hz, ha, 0, 120), busyPattern, 33);
        Check(r.Count == 1 && r[0].Contains("hold here"), "Am Haltepunkt: " + (r.FirstOrDefault()?.Text ?? "-"));
        for (double tt = 38; tt < 92; tt += 5) tw15.Tick(At(hx + 500, hz, ha, 90, 120), busyPattern, tt);
        r = tw15.Tick(At(hx + 5 * NM, hz, ha, 90, 180), busyPattern, 93);   // Circle: no second heading correction on the side
        Check(r.Count == 0, "Warteschleife ohne Extra-Kurs: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw15.Tick(At(hx + 5 * NM, hz, ha, 90, 180), busyPattern, 95);   // 350 kt: circle a good 6 NM, 5 NM away is still in the holding
        Check(r.Count == 1 && r[0].Contains("reduce speed 230 knots") && !r[0].Contains("continue holding") && !r[0].Contains("expect"), "Warteschleife Fahrt daneben: nur die Fahrt: " + (r.FirstOrDefault()?.Text ?? "-"));
        for (double tt = 100; tt < 126; tt += 5) tw15.Tick(At(hx + 500, hz, ha, 90, 120), busyPattern, tt);
        r = tw15.Tick(At(hx + 500, hz, ha + 250, 90, 120) with { Vs = -5 }, busyPattern, 126);   // 800 ft above, already descending back
        Check(r.Count == 0, "Warteschleife: korrigiert -> still: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw15.Tick(At(hx + 500, hz, ha + 250, 90, 120), busyPattern, 127);
        Check(r.Count == 1 && r[0].Contains("check altitude, descend and maintain"), "Warteschleife Höhe verlassen: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw15.Tick(At(hx + 500, hz, ha, 90, 120), none, 128);
        for (double tt = 129; tt < 250 && !r.Any(m => m.Text.Contains("leave the hold")); tt++) r = tw15.Tick(At(hx + 500, hz, ha, 90, 120), none, tt);   // Heading does not fit: at most 2 min
        Check(r.Count == 1 && r[0].Contains("leave the hold") && r[0].Contains("heading"), r.FirstOrDefault()?.Text ?? "Hold frei: -");
        // R350: flying to the hold with shrinking distance -> no kick for 600 s; fixed course away -> stages 1-4, at least 30 s apart
        var tw350 = new Tower("Enfield 1-1");
        tw350.Tick(west, busyPattern, 0);
        tw350.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, busyPattern, 1);
        var (h350x, h350z, _) = tw350.HoldInfo!.Value;
        double h350d = Dist(west.X, west.Z, h350x, h350z), h350v = (h350d - 2 * NM) / 600;
        var k350 = new List<string>();
        for (int s = 2; s <= 600; s++)
        {
            double f = 1 - h350v * s / h350d;
            double qx = h350x + (west.X - h350x) * f, qz = h350z + (west.Z - h350z) * f;
            k350.AddRange(tw350.Tick(At(qx, qz, tw350.HoldInfo!.Value.Ft * Ft - FieldElev, Bearing(qx, qz, h350x, h350z), 120), busyPattern, s).Select(m => m.Text));
        }
        Check(!k350.Any(m => m.Contains("verify") || m.Contains("say intentions") || m.Contains("removed from the sequence")), "R350 Anflug zur Warteschleife ohne Kick: " + string.Join(" | ", k350));
        var tw350b = new Tower("Enfield 1-1");
        tw350b.Tick(west, busyPattern, 0);
        tw350b.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, busyPattern, 1);
        var (h350bx, h350bz, h350bFt) = tw350b.HoldInfo!.Value;
        double away = Bearing(h350bx, h350bz, west.X, west.Z);
        var st350 = new List<(int S, string T)>();
        for (int s = 2; s <= 500 && !st350.Any(m => m.T.Contains("removed from the sequence")); s++)
        {
            double qx = west.X + Math.Cos(away * Math.PI / 180) * 120 * s, qz = west.Z + Math.Sin(away * Math.PI / 180) * 120 * s;
            st350.AddRange(tw350b.Tick(At(qx, qz, h350bFt * Ft - FieldElev, away, 120), busyPattern, s).Select(m => (s, m.Text)));
        }
        int I350(string w) => st350.FindIndex(m => m.T.Contains(w));
        int v350 = I350("verify heading"), i350 = I350("say intentions"), x350 = I350("removed from the sequence");
        int r350 = v350 >= 0 ? st350.FindIndex(v350 + 1, m => m.T.Contains("miles to the hold")) : -1;
        Check(v350 >= 0 && r350 == v350 + 1 && i350 == r350 + 1 && x350 == i350 + 1 && st350[r350].S - st350[v350].S >= 30 && st350[i350].S - st350[r350].S >= 30 && st350[x350].S - st350[i350].S >= 30,
              "R350 Kurs weg von der Warteschleife: Stufen 1-4 im 30-s-Takt: " + string.Join(" | ", st350.Select(m => $"{m.S}s {m.T}")));
        // Helicopter in holding: no speed instruction, no speed reminder at 100 kt (R45)
        var twHh = new Tower("Enfield 1-1") { AcType = "UH-1H" };
        twHh.Tick(west, busyPattern, 0);
        var hh = twHh.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, busyPattern, 1).Select(m => m.Text).ToList();
        var (hhx, hhz, hhFt) = twHh.HoldInfo!.Value;
        var hhOrbit = At(hhx + 500, hhz, hhFt * Ft - FieldElev, 90, 50);
        hh.AddRange(Enumerable.Range(2, 400).SelectMany(s => twHh.Tick(hhOrbit, busyPattern, s)).Select(m => m.Text));
        Check(hh.Any(m => m.Contains("hold here")) && hh.All(m => !m.Contains("knots")) && new Tower("Enfield 1-1") { AcType = "P-47D-30" }.HoldKtFor() == 200, "Warteschleife Hubschrauber ohne Fahrt: " + string.Join(" | ", hh));

        // --- A50: EAT once as mission-clock minute, silence in the circle, only a change of >= 5 min is reported
        Carrier.Clock = 10 * 3600 + 30 * 60;   // 10:30 -> 6 min wait = EAT 36
        var twE = new Tower("Enfield 1-1");
        twE.Tick(west, busyPattern, 0);
        r = twE.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, busyPattern, 1);
        Check(r[0].Text.Contains("expected approach time three six", StringComparison.OrdinalIgnoreCase) && !r[0].Contains("expect approach in") && r[0].Contains("2 aircraft in the pattern"), "EAT einmal als Uhrzeit: " + r[0]);
        for (double tt = 2; tt < 32; tt += 5) twE.Tick(west, busyPattern, tt);
        r = twE.Tick(west, busyPattern, 32);
        Check(r.Count == 1 && r[0].Contains("miles to the hold") && !r[0].Contains("expect"), "Hinführen ohne EAT: " + (r.FirstOrDefault()?.Text ?? "-"));
        var (ex, ez, _) = twE.HoldInfo!.Value;
        double ea = twE.HoldInfo!.Value.Ft * Ft - FieldElev;
        r = twE.Tick(At(ex + 500, ez, ea, 0, 120), busyPattern, 33);
        Check(r.Count == 1 && r[0].Contains("hold here") && !r[0].Contains("expect"), "Am Haltepunkt ohne EAT: " + (r.FirstOrDefault()?.Text ?? "-"));
        int eatMsgs = 0;
        for (double tt = 36; tt < 358; tt += 3) eatMsgs += twE.Tick(At(ex + 500, ez, ea, 90, 120), busyPattern, tt).Count;   // over 5 min: EAT (361) is fixed, does not slip
        Check(eatMsgs == 0, $"Kreis auf Kurs: Funkstille ({eatMsgs} Ansagen)");
        var busy5 = Enumerable.Range(0, 5).Select(i => new Traffic(30 + i, "F-16C", CX + 1500 + 300 * i, CZ, FieldElev + 300, 0, 80)).ToArray();
        r = twE.Tick(At(ex + 500, ez, ea, 90, 120), busy5, 358);   // 12 instead of 6 min: EAT 42
        Check(r.Count == 1 && r[0].Contains("revised expected approach time four two") && r[0].Contains("5 aircraft in the pattern"), "EAT geändert: " + (r.FirstOrDefault()?.Text ?? "-"));
        var busy6 = busy5.Append(new Traffic(40, "F-16C", CX - 1500, CZ, FieldElev + 300, 0, 80)).ToArray();
        eatMsgs = 0;
        for (double tt = 361; tt < 1070; tt += 3) eatMsgs += twE.Tick(At(ex + 500, ez, ea, 90, 120), busy6, tt).Count;   // +2 min: change under 5 min, silent until the EAT (1078)
        Check(eatMsgs == 0, $"EAT nur 2 min später: Funkstille ({eatMsgs} Ansagen)");
        eatMsgs = 0;
        for (double tt = 1070; tt < 1100; tt += 3) eatMsgs += twE.Tick(At(ex + 500, ez, ea, 90, 120), busy6, tt).Count(m => m.Text.Contains("revised expected approach time"));
        Check(eatMsgs == 1, $"EAT erreicht, noch belegt: einmal revised ({eatMsgs} Ansagen)");
        Carrier.Clock = -1;
        // R273 (mptest Kutaisi IFR "34 miles to go, … expect approach in 3 minutes, you are number 3", out only after 11 min; 364 s "revised, … in 3 minutes"):
        // EAT not before arrival at the holding point, 5 min per position in the sequence; with unchanged situation silent until the EAT, then a new one right away
        var twEa = new Tower("Enfield 1-1") { QueueAhead = 1, Spaced = false };
        var far40 = At(CX + 40 * NM, CZ, 3000, 180, 118);
        twEa.Tick(far40, none, 0);
        r = twEa.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", far40, none, 1);
        var (eax, eaz, eaFt) = twEa.HoldInfo!.Value;
        int eaMin = (int)Math.Ceiling(Dist(far40.X, far40.Z, eax, eaz) / (HoldKt * Kt) / 60);
        Check(eaMin >= 6 && r[0].Text.Contains($"expect approach in {eaMin} minutes", StringComparison.OrdinalIgnoreCase), $"R273 EAT mit Flugzeit zum Haltepunkt ({eaMin} min): " + r[0]);
        var eaPos = At(eax, eaz, eaFt * Ft - FieldElev, 90, 118);
        var eaSaid = Enumerable.Range(2, eaMin * 60 + 10).SelectMany(s => twEa.Tick(eaPos, none, s).Select(m => (S: s, m.Text))).Where(m => m.Text.Contains("expect approach")).ToList();
        Check(eaSaid.Count == 1 && eaSaid[0].S >= eaMin * 60 && eaSaid[0].Text.Contains("revised, expect approach in 4 minutes"), "R273 EAT erreicht: gleich neue Zeit: " + string.Join(" | ", eaSaid.Select(m => $"{m.S}s {m.Text}")));

        // --- Landing sequence with altitude stack: number 5 -> 5000 ft, move down, out at number 2
        var twQ = new Tower("Enfield 1-1") { QueueAhead = 4, Spaced = false };
        twQ.Tick(west, none, 0);
        r = twQ.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, none, 1);
        Check(r[0].Contains("5000 feet") && r[0].Contains("you are number 5"), "Staffel: " + r[0]);
        twQ.QueueAhead = 3;
        // R270 (mptest Sukhumi IFR): radar-vectored aircraft assigned 4000, 10 NM east of the airfield (over 15 NM from me) -> do not descend to it (his path may lead past the holding)
        r = twQ.Tick(west with { AltMsl = 5000 * Ft }, new[] { new Traffic(89, "F-16C", CX, CZ + 10 * NM, 4000 * Ft, 0, 130, "U8", "twr:Kutaisi", true, 2, AsgFt: 4000) }, 1.5);
        Check(r.Count == 0, "R270 Warteschleife: nicht in die zugewiesene Höhe eines Radargeführten am Platz: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = twQ.Tick(west with { AltMsl = 5000 * Ft }, none, 2);   // on the level
        Check(r.Count == 1 && r[0].Contains("descend and maintain 4000 feet") && r[0].Contains("number 4"), "Nachrutschen: " + (r.FirstOrDefault()?.Text ?? "-"));
        twQ.QueueAhead = 1; twQ.Spaced = true;   // preceding traffic close to the airfield
        r = twQ.Tick(west, none, 3);
        for (double tt = 4; tt < 130 && !r.Any(m => m.Text.Contains("leave the hold")); tt++) r = twQ.Tick(west, none, tt);
        Check(r.Count == 1 && r[0].Contains("leave the hold"), "Dran: " + (r.FirstOrDefault()?.Text ?? "-"));
        // R272 (mptest Gudauta IFR "leave the hold, … climb and maintain 6200 feet" from 3000, Senaki 4000 -> 7300): mountain 5 NM beside the holding point, off the path
        // holding point -> airfield; out 3 NM in the circle toward the mountain -> holding altitude already above, no climb when leaving
        {
            var hk = Airfield.Kutaisi();
            var hg = new float[61, 61];
            hk.SetTerrain(hg, hk.X - 30 * NM, hk.Z - 30 * NM, NM);
            var probe = new Tower(hk, "Enfield 1-1") { QueueAhead = 2, Spaced = false };
            probe.Tick(west, none, 0);
            probe.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, none, 1);
            var (hfx, hfz, _) = probe.HoldInfo!.Value;   // Holding point over flat terrain
            double hpb = (Bearing(hfx, hfz, CX, CZ) + 90) * Math.PI / 180;   // across the path holding point -> airfield
            int hbi = (int)Math.Round((hfx + 5 * NM * Math.Cos(hpb) - (hk.X - 30 * NM)) / NM), hbj = (int)Math.Round((hfz + 5 * NM * Math.Sin(hpb) - (hk.Z - 30 * NM)) / NM);
            for (int i = hbi - 1; i <= hbi + 1; i++) for (int j = hbj - 1; j <= hbj + 1; j++) hg[i, j] = 1500;   // 4921 ft
            var twHm = new Tower(hk, "Enfield 1-1") { QueueAhead = 2, Spaced = false };
            twHm.Tick(west, none, 0);
            var hr = twHm.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", west, none, 1)[0].Text;
            var (hgx, hgz, hgFt) = twHm.HoldInfo!.Value;
            double hgAgl = hgFt * Ft - FieldElev;
            twHm.Tick(At(hgx, hgz, hgAgl, 0, 120), none, 2);   // "hold here"
            twHm.QueueAhead = 1; twHm.Spaced = true;
            var hl = new List<Msg>();
            for (double tt = 3; tt < 140 && !hl.Any(m => m.Text.Contains("leave the hold")); tt++)   // in the circle 3 NM toward the mountain
                hl = twHm.Tick(At(hgx + 3 * NM * Math.Cos(hpb), hgz + 3 * NM * Math.Sin(hpb), hgAgl, 90, 120), none, tt);
            Check(Dist(hfx, hfz, hgx, hgz) < NM && hl.Any(m => m.Text.Contains("leave the hold")) && !hl.Any(m => m.Text.Contains("climb")),
                  $"R272 Verlassen der Warteschleife ohne Steigen (Gelände neben dem Haltepunkt): {hgFt} ft, Haltepunkt {Dist(hfx, hfz, hgx, hgz) / NM:0.0} NM daneben | {hr} | " + string.Join(" | ", hl.Select(m => m.Text)));
        }

        // --- Traffic alert in holding: avoidance altitude stays, no "descend" into traffic, afterwards "clear of traffic"
        var twW = new Tower("Enfield 1-1") { QueueAhead = 4, Spaced = false };
        var w5 = west with { AltMsl = 5000 * Ft };
        twW.Tick(w5, none, 0);
        twW.OnTranscript("Kutaisi Approach, Enfield 11, inbound for landing", w5, none, 1);
        var intruder = new[] { new Traffic(88, "Su-25T", w5.X, w5.Z + 3 * NM, w5.AltMsl, 250 * Math.PI / 180, 120) };
        r = twW.Tick(w5, intruder, 2);
        Check(r.Count == 1 && r[0].Contains("Traffic alert") && r[0].Contains("and climb to 6000 feet immediately"), "Warteschleife Warnung: " + (r.FirstOrDefault()?.Text ?? "-"));
        twW.QueueAhead = 3;   // preceding traffic out, traffic still there (close, at 5000)
        var said = new List<string>();
        for (double tt = 3; tt < 70; tt++) said.AddRange(twW.Tick(w5, new[] { intruder[0] with { Z = w5.Z + 2 * NM } }, tt).Select(m => m.Text));
        Check(said.Count > 0 && !said.Any(s => s.Contains("descend")) && said.All(s => !s.Contains("feet") || s.Contains("6000 feet")),
              "Warteschleife: keine Gegenanweisung -> " + string.Join(" | ", said));
        r = twW.Tick(w5 with { AltMsl = 6000 * Ft }, none, 93);   // last warning at 62 -> 30 s quiet
        Check(r.Count == 1 && r[0].Contains("clear of traffic, descend and maintain 4000") && r[0].Contains("number 4"), "Warteschleife nach Warnung: " + (r.FirstOrDefault()?.Text ?? "-"));

        // --- Landing evaluation: stable approach, touchdown 300 m past the threshold
        var tw16 = new Tower("Enfield 1-1");
        double ti = 0;
        foreach (var nm in new[] { 2.8, 2.5, 2.0, 1.5, 1.0, 0.5 })
        {
            var (gx, gz) = P("25", nm * NM);
            tw16.Tick(At(gx, gz, nm * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Vs = -1.5 }, none, ti++);
        }
        var (tdx, tdz) = P("25", -300);
        r = tw16.Tick(At(tdx, tdz, 0, LandHdg("25"), 65), none, ti);
        Check(r.Count == 1 && r[0].Contains("OK (100/100)") && r[0].Contains("Aufsetzpunkt 300 m"), r.FirstOrDefault()?.Text ?? "Bewertung: -");
        // PARK-P1: bounce (back on the ground without climbing over 30 m) -> no second evaluation; after climbing (touch-and-go) one again
        var twHop = new Tower("Enfield 1-1");
        var grades = new[] { (0, 300.0), (5, 0), (6, 4), (7, 0), (12, 60), (17, 0) }
            .SelectMany(p => twHop.Tick(At(CX, CZ, p.Item2, 254, 70), none, p.Item1)).Where(m => m.StartsWith("Landebewertung")).ToList();
        Check(grades.Count == 2, "Hopser -> eine Bewertung je Landung: " + string.Join(" | ", grades));

        // --- Flight: clearances for the flight, wingman's callsign changes nothing, handoff to the Tower
        var tw17 = new Tower("Enfield 1-1") { FlightSize = 2 };
        tw17.Tick(parked, none, 0);
        r = tw17.OnTranscript("Kutaisi Ground, Enfield 1-2, request startup", parked, none, 1);
        Check(r[0].StartsWith("Enfield one one flight, Kutaisi Ground, start up approved"), r[0]);
        r = tw17.OnTranscript("Ground: request taxi", parked, none, 2, true);
        Check(r[0].StartsWith("Enfield one one flight") && r[0].Contains("contact Kutaisi Tower two six five decimal zero"), r[0]);

        // --- V17 Formation: break interval, separate landing, radar trail recovery
        var ipT = At(ipPos.Item1, ipPos.Item2, 600, 254, 150);
        var tw17b = new Tower("Enfield 1-1") { FlightSize = 2 };
        tw17b.Tick(ipT, none, 0);
        r = tw17b.OnTranscript("Kutaisi Tower, Enfield 11 flight, initial, 5 second break", ipT, none, 1);
        Check(r[0].Contains("5 second break approved, report base"), r[0]);
        r = tw17b.OnTranscript("Enfield 11, request separate landing", ipT, none, 2);
        Check(tw17b.Separate && r[0].Contains("separate landing approved"), r[0]);
        var tw17c = new Tower("Enfield 1-1") { FlightSize = 2 };
        var farT = At(CX + 20 * NM, CZ, 1500, 180, 130);
        tw17c.Tick(farT, none, 0);
        r = tw17c.OnTranscript("Kutaisi Approach, Enfield 11 flight, request radar trail recovery", farT, none, 1);
        Check(tw17c.Separate && r[0].Contains("heading") && r[0].Contains("Radar trail approved"), r[0]);

        // --- R110 Spawn on the runway (slot): line up and wait without clearance (no "hold position"), not even after 6 min; takeoff clearance only on "ready for departure"
        // (previously the first tick gave an unsolicited "cleared for takeoff"; deliberately reversed)
        var tw18 = new Tower("Enfield 1-1");
        var rwy18 = At(CX, CZ, 0, 254, 0);
        r = tw18.Tick(rwy18, none, 0).Concat(tw18.Tick(rwy18, none, 1)).Concat(tw18.Tick(rwy18, none, 400)).ToList();
        var r18 = tw18.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", rwy18, none, 401);
        Check(r.Count == 0 && tw18.Phase == Phase.ClearedTakeoff && r18.Count == 1 && r18[0].Contains("cleared for takeoff"),
              $"R110 Bahnstart: 6 min still, dann auf Anfrage: {string.Join(" | ", r)} / {r18.FirstOrDefault()?.Text}");
        var tw18s = new Tower("Enfield 1-1");   // Reporting obligation #14: takeoff roll without call even with free runway: "stop immediately" + violation, no silent clearance
        r = tw18s.Tick(rwy18, none, 0).Concat(tw18s.Tick(At(CX, CZ, 0, 254, 60 * Kt), none, 20)).ToList();
        Check(r.Count == 2 && r[0].Contains("stop immediately") && r[0].Contains("not cleared for takeoff") && r[1].Role == "Info" && tw18s.Phase == Phase.HoldShort,
              $"#14 Bahnstart ohne Anruf: {tw18s.Phase} {string.Join(" | ", r)}");
        // after rejected takeoff (holdSince -2, R100) likewise
        r = tw18s.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", rwy18, none, 30).Concat(tw18s.OnTranscript("Enfield 1-1, aborting takeoff", rwy18, none, 31))
                 .Concat(tw18s.Tick(rwy18, none, 60)).Concat(tw18s.Tick(At(CX, CZ, 0, 254, 60 * Kt), none, 90)).ToList();
        Check(r.Any(m => m.Text.Contains("stop immediately")) && tw18s.Phase == Phase.HoldShort, $"#14 nach Startabbruch: {tw18s.Phase} {string.Join(" | ", r)}");
        // Airfield change/app restart during rollout (new Tower, 40 kt on the runway) or gap across the landing: rollout, no takeoff clearance (log Dubai 17:00:53)
        var tw18r = new Tower("Enfield 1-1");
        r = Enumerable.Range(0, 400).SelectMany(s => tw18r.Tick(At(CX - Ux * Math.Max(0, 40 - s) * 2, CZ - Uz * Math.Max(0, 40 - s) * 2, 0, 254, Math.Max(0, 40 - s) * Kt), none, s)).ToList();
        var tw18l = new Tower("Enfield 1-1");
        var (f18x, f18z) = P("25", 300);
        tw18l.Tick(At(f18x, f18z, 20, 254, 70), none, 0);
        r = r.Concat(tw18l.Tick(At(CX, CZ, 0, 254, 0), none, 15)).Concat(tw18l.Tick(At(CX, CZ, 0, 254, 0), none, 400)).ToList();
        Check(!r.Any(m => m.Text.Contains("cleared for takeoff")) && tw18r.Phase == Phase.TaxiIn && tw18l.Phase == Phase.TaxiIn,
              $"R110 Reset im Ausrollen: {tw18r.Phase}/{tw18l.Phase} {string.Join(" | ", r)}");
        // Without mission data (Export.lua: IAS) the airspeed indicator shows the headwind when stationary: 10 kt at 10 kt wind is spawn on the runway, not rollout
        var tw18w = new Tower("Enfield 1-1");
        var w18 = At(CX, CZ, 0, 254, 10 * Kt) with { WindX = -10 * Kt * Math.Cos(254 * Math.PI / 180), WindZ = -10 * Kt * Math.Sin(254 * Math.PI / 180) };
        r = tw18w.Tick(w18, none, 0).Concat(tw18w.Tick(w18, none, 400)).ToList();
        var ph18w = tw18w.Phase;
        var r18w = tw18w.OnTranscript("Kutaisi Tower, Enfield 1-1, ready for departure", w18, none, 401);
        Check(r.Count == 0 && ph18w == Phase.HoldShort && r18w.Count == 1 && r18w[0].Contains("cleared for takeoff"), $"R110 Bahnstart im Gegenwind (IAS): {ph18w} {string.Join(" | ", r)} / {r18w.FirstOrDefault()?.Text}");

        // --- Taxi traffic: AI crosses from the right -> hold position, then continue taxi
        var tw19 = new Tower("Enfield 1-1");
        var taxiing = At(-284246.6, 683966.5, 0, 0, 6);   // taxiing north
        tw19.Tick(taxiing, none, 0);
        tw19.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        var cross = new Traffic(9, "FA-18C_hornet", taxiing.X + 80, taxiing.Z + 80, FieldElev, 270 * Math.PI / 180, 8, "AI", "dep-go", false);
        r = tw19.Tick(taxiing, new[] { cross }, 2);
        Check(r.Count == 1 && r[0].Contains("hold position, give way to the Hornet on your right"), r.FirstOrDefault()?.Text ?? "Rollverkehr: -");
        r = tw19.OnTranscript("Holding position, Enfield 1-1", taxiing with { Ias = 0 }, new[] { cross }, 2.5);   // R111: readback, no reply
        Check(r.Count == 0 && tw19.Rejected == 0, "R111 holding position nach Rollkonflikt: " + string.Join(" | ", r.Select(m => m.Text)));
        r = tw19.Tick(taxiing with { Ias = 0 }, new[] { cross with { Z = taxiing.Z - 400 } }, 3);
        Check(r.Count == 0, "R106 Halt unter 8 s: " + string.Join(" | ", r));
        r = tw19.Tick(taxiing with { Ias = 0 }, new[] { cross with { Z = taxiing.Z - 400 } }, 10);
        Check(r.Count == 1 && r[0].Contains("continue taxi"), r.FirstOrDefault()?.Text ?? "Rollverkehr frei: -");
        // R263: groundspeed (IAS minus wind): stationary in headwind no taxi traffic ("give way"), 20 kt taxi at 15 kt headwind no "reduce taxi speed"
        var tw263g = new Tower("Enfield 1-1");
        tw263g.Tick(taxiing, none, 0);
        tw263g.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        var standW = taxiing with { Ias = 6, WindX = 6 };
        r = tw263g.Tick(standW, new[] { cross }, 2).Concat(tw263g.Tick(taxiing with { Ias = 35 * Kt, WindX = 15 * Kt }, none, 3)).ToList();
        Check(r.Count == 0 && TaxiConflict(standW, new[] { cross }) == null, "R263 Gegenwind: " + string.Join(" | ", r));
        // R262: without taxi clearance (after "start up approved", after landing before the Ground call) no "give way"/"continue taxi" – that would apply only with taxi clearance
        var tw262 = new Tower("Enfield 1-1");
        tw262.Tick(taxiing, none, 0);
        tw262.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", taxiing, none, 1);
        var r262 = tw262.Tick(taxiing, new[] { cross }, 2).Concat(tw262.Tick(taxiing with { Ias = 0 }, new[] { cross with { Z = taxiing.Z - 400 } }, 12)).ToList();
        var tw262i = Landed();
        r262.AddRange(tw262i.Tick(taxiing with { Ias = 2 }, new[] { cross with { X = taxiing.X + 20 } }, 20));
        Check(tw262.Phase == Phase.StartupApproved && tw262i.Phase == Phase.TaxiIn && !r262.Any(m => m.Text.Contains("give way") || m.Text.Contains("continue taxi")), "R262 ohne Rollfreigabe: " + string.Join(" | ", r262));

        // --- A31 Right of way: other player from the left has to wait (not I), oncoming: exactly one, same direction: the rear one; AI: always the player
        Traffic Mate(double dx, double dz, double hdgDeg, double v = 8) => new("Mate".GetHashCode(), "F-16C_50", taxiing.X + dx, taxiing.Z + dz, FieldElev, hdgDeg * Math.PI / 180, v, "Mate", "", false);
        var fromLeft = Mate(80, -80, 90);
        Check(TaxiConflict(taxiing, new[] { fromLeft }) == null && TaxiConflict(taxiing, new[] { fromLeft with { Group = "AI" } }) != null, "A31 Mitspieler von links: ich fahre, KI: ich warte");
        Check(TaxiConflict(taxiing, new[] { Mate(80, 80, 270) }) != null, "A31 Mitspieler von rechts: ich warte");
        var headOn = Mate(100, 5, 180);
        var theirs = taxiing with { X = headOn.X, Z = headOn.Z, Hdg = Math.PI };
        var mine = new Traffic("Me".GetHashCode(), "FA-18C_hornet", taxiing.X, taxiing.Z, FieldElev, taxiing.Hdg, taxiing.Ias, "Me", "", false);
        Check((TaxiConflict(taxiing, new[] { headOn }) != null) != (TaxiConflict(theirs, new[] { mine }) != null), "A31 Gegenverkehr: genau einer wartet");
        Check(TaxiConflict(taxiing, new[] { Mate(-20, 0, 0, 3) }) == null, "A31 Mitspieler hinter mir: ich warte nicht");
        var tw19b = new Tower("Enfield 1-1");
        tw19b.Tick(taxiing, none, 0);
        tw19b.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        r = tw19b.Tick(taxiing, new[] { Mate(30, 0, 0, 3) }, 2);
        Check(r.Count == 1 && r[0].Contains("follow the Viper ahead"), r.FirstOrDefault()?.Text ?? "A31 follow: -");
        r = tw19b.Tick(taxiing, new[] { Mate(200, 0, 0, 8) }, 3);   // taxied away: no "continue taxi", he was not stopped
        Check(r.Count == 0, r.FirstOrDefault()?.Text ?? "A31 follow vorbei: still");
        tw19b.Tick(taxiing, new[] { Mate(30, 0, 0, 3) }, 4);
        r = tw19b.Tick(taxiing, new[] { Mate(30, 0, 0, 3), cross }, 5);   // while following, one crosses from the right: stop anyway
        Check(r.Count == 1 && r[0].Contains("hold position, give way to the Hornet on your right"), r.FirstOrDefault()?.Text ?? "A31 follow + kreuzend: -");
        // R342: stationary traffic in the taxi path: holding short ahead -> follow; standing in the taxiway -> hold position, continue taxi once it has left
        var tw342 = new Tower("Enfield 1-1");
        tw342.Tick(taxiing, none, 0);
        tw342.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        var hp342 = tw342.HoldPt(tw342.Runway, taxiing);
        double b342 = Bearing(taxiing.X, taxiing.Z, hp342.X, hp342.Z) * Math.PI / 180;
        var me342 = At(hp342.X - 80 * Math.Cos(b342), hp342.Z - 80 * Math.Sin(b342), 0, b342 * 180 / Math.PI, 6);
        var held342 = new Traffic(342, "F-16C_50", hp342.X, hp342.Z, FieldElev, b342, 0, "AI", "dep", false);
        r = tw342.Tick(me342, new[] { held342 }, 2).Concat(tw342.Tick(me342, new[] { held342 }, 3)).ToList();
        Check(r.Count == 1 && r[0].Contains($"follow the Viper holding short runway {RwSay(tw342.Runway)}"), "R342 follow am Rollhalt: " + string.Join(" | ", r));
        var tw342b = new Tower("Enfield 1-1");
        tw342b.F.Spots.Add((taxiing.X - 900, taxiing.Z, 4, 72));
        tw342b.Tick(taxiing, none, 0);
        tw342b.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        var r342 = tw342b.Tick(taxiing, new[] { Mate(80, 0, 0, 0) }, 2).Select(m => m.Text).ToList();
        r342.Add("|" + string.Join(" ", tw342b.Tick(taxiing with { Ias = 0 }, new[] { Mate(80, 0, 0, 0) }, 11)));
        r342.Add("|" + string.Join(" ", tw342b.Tick(taxiing with { Ias = 0 }, new[] { Mate(200, 0, 0, 8) }, 12)));
        Check(r342.Count == 3 && r342[0].Contains("hold position, traffic ahead, Viper") && r342[1] == "|" && r342[2].Contains("continue taxi"), "R342 hold position: " + string.Join(" ", r342));
        // Merging under 44° (from front left or front right, collision course): exactly one waits, not both
        foreach (var (relDeg, hdgDeg) in new[] { (300.0, 44.0), (60.0, 316.0) })
        {
            var merge = Mate(100 * Math.Cos(relDeg * Math.PI / 180), 100 * Math.Sin(relDeg * Math.PI / 180), hdgDeg);
            var mergeMe = taxiing with { X = merge.X, Z = merge.Z, Hdg = merge.Hdg, Ias = 8 };
            var meT = mine with { Speed = 8 };
            bool iWait = TaxiConflict(taxiing with { Ias = 8 }, new[] { merge }) != null, heWaits = TaxiConflict(mergeMe, new[] { meT }) != null;
            Check(iWait != heWaits, $"A31 Zusammenführen rel {relDeg}: ich {iWait}, er {heWaits}");
        }

        // --- Taxiing without clearance (A40): Parked/StartupApproved, over 5 kt and over 100 m from the parking position -> Ground "hold position, say intentions" (Say, no rejection), at most every 2 min
        var twRoll1 = new Tower("Enfield 1-1");
        twRoll1.Tick(parked, none, 0);
        r = twRoll1.Tick(At(parked.X + 30, parked.Z, 0, 0, 8), none, 1);
        Check(r.Count == 0, "Rollen nahe der Parkposition: " + string.Join(" | ", r));
        r = twRoll1.Tick(At(parked.X + 200, parked.Z, 0, 0, 2), none, 2);
        Check(r.Count == 0, "Rollen unter 5 kt: " + string.Join(" | ", r));
        r = twRoll1.Tick(At(parked.X + 200, parked.Z, 0, 0, 8), none, 3);
        Check(r.Count == 1 && r[0].Role == "Ground" && r[0].Contains("hold position, say intentions") && twRoll1.Rejected == 0, r.FirstOrDefault()?.Text ?? "Rollen ohne Freigabe: -");
        r = twRoll1.Tick(At(parked.X + 210, parked.Z, 0, 0, 8), none, 60);
        Check(r.Count == 0, "Rollen ohne Freigabe: höchstens alle 2 min: " + string.Join(" | ", r));
        r = twRoll1.Tick(At(parked.X + 220, parked.Z, 0, 0, 8), none, 130);
        Check(r.Count == 1 && r[0].Contains("hold position, say intentions"), r.FirstOrDefault()?.Text ?? "Rollen ohne Freigabe nach 2 min: -");
        var twRoll2 = new Tower("Enfield 1-1");
        twRoll2.Tick(parked, none, 0);
        twRoll2.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", parked, none, 1);
        r = twRoll2.Tick(At(parked.X + 200, parked.Z, 0, 0, 8), none, 2);
        Check(twRoll2.Phase == Phase.StartupApproved && r.Count == 1 && r[0].Contains("hold position, say intentions"), "Rollen nach Anlassfreigabe ohne Rollfreigabe: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twRoll3 = new Tower("Enfield 1-1");
        twRoll3.Tick(parked, none, 0);
        twRoll3.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 1);
        r = twRoll3.Tick(At(parked.X + 200, parked.Z, 0, 0, 8), none, 2);
        Check(twRoll3.Phase == Phase.TaxiOut && r.Count == 0, "Rollen mit Rollfreigabe: " + string.Join(" | ", r));

        // --- Taxi traffic only off the runway (R38): on the runway (rollout) and because of traffic on the runway no "hold position, give way"
        double rwH = LandHdg("25") * Math.PI / 180, rwUx = Math.Cos(rwH), rwUz = Math.Sin(rwH), rwPx = -rwUz, rwPz = rwUx;   // runway direction, across it
        (double X, double Z) RwQ(double along, double lat) => (CX + along * rwUx + lat * rwPx, CZ + along * rwUz + lat * rwPz);
        double toRwy = Math.Atan2(-rwPz, -rwPx);   // heading across the runway, toward it
        var twRoll4 = new Tower("Enfield 1-1");
        var (rbx, rbz) = RwQ(0, 45);
        var towards = At(rbx, rbz, 0, toRwy * 180 / Math.PI, 4);   // taxiing beside the runway toward it
        twRoll4.Tick(towards, none, 0);
        twRoll4.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", towards, none, 1);
        var (rax, raz) = RwQ(-100, 0);
        var rolling = new Traffic(9, "F-16C_50", rax, raz, FieldElev, rwH, 20, "AI", "dep-go", false);   // Takeoff roll on the runway
        r = twRoll4.Tick(towards, new[] { rolling }, 2);
        Check(r.Count == 0, "Rollverkehr: Verkehr auf der Bahn zählt nicht: " + string.Join(" | ", r));
        var (rox, roz) = RwQ(-100, 40);
        r = twRoll4.Tick(towards, new[] { rolling with { X = rox, Z = roz } }, 3);   // same traffic beside the runway
        Check(r.Count == 1 && r[0].Contains("hold position, give way to the Viper"), "Rollverkehr neben der Bahn: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twRoll5 = new Tower("Enfield 1-1");
        twRoll5.Tick(parked, none, 0);
        twRoll5.Tick(At(CX, CZ, 300, 254, 80), none, 5);
        twRoll5.Tick(At(CX, CZ, 0, 254, 20), none, 10);
        twRoll5.Tick(At(CX, CZ, 0, 254, 20), none, 11);
        twRoll5.Tick(At(CX, CZ, 0, 254, 5), none, 19);
        var (rcx, rcz) = RwQ(0, 40);
        var crossing = new Traffic(9, "FA-18C_hornet", rcx, rcz, FieldElev, toRwy, 6, "AI", "dep-go", false);
        r = twRoll5.Tick(At(CX, CZ, 0, LandHdg("25"), 5), new[] { crossing }, 20);
        Check(twRoll5.Phase == Phase.TaxiIn && r.Count == 0, "Ausrollen auf der Bahn: kein Rollkonflikt: " + string.Join(" | ", r));

        // --- R105: without lineUp from 30 kt "stop immediately" from the Tower; on the runway no "reduce taxi speed" from Ground
        var twR105 = new Tower("Enfield 1-1");
        twR105.Tick(parked, none, 0);
        twR105.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 1);
        r = twR105.Tick(At(CX, CZ, 0, 254, 18), none, 5);   // 35 kt in takeoff roll without clearance
        r = r.Where(m => m.Role != "Info").ToList();   // Violation notice (main)
        Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Contains("stop immediately"), "R105 35 kt ohne lineUp: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twR105b = new Tower("Enfield 1-1");
        twR105b.Tick(parked, none, 0);
        twR105b.Tick(At(CX, CZ, 300, 254, 80), none, 5);
        twR105b.Tick(At(CX, CZ, 0, 254, 20), none, 10);
        twR105b.Tick(At(CX, CZ, 0, 254, 20), none, 11);
        twR105b.Tick(At(CX, CZ, 0, 254, 5), none, 19);
        r = twR105b.Tick(At(CX, CZ, 0, 254, 18), none, 70);   // 35 kt on the runway, TaxiIn after 45 s
        Check(r.Count == 0, "R105 Bahn: kein \"reduce taxi speed\": " + string.Join(" | ", r));
        r = twR105b.Tick(At(CX, CZ + 400, 0, 254, 18), none, 140);   // off the runway (A91 reminder only 30 s after vacating)
        Check(r.Count == 1 && r[0].Contains("reduce taxi speed"), "R105 abseits der Bahn: " + (r.FirstOrDefault()?.Text ?? "-"));

        // --- R106: hold at least 8 s, stationary traffic on the taxiway remains an obstacle, never "continue taxi" and right away "hold position" again for the same group
        var cross2 = new Traffic(10, "FA-18C_hornet", taxiing.X + 30, taxiing.Z + 40, FieldElev, 270 * Math.PI / 180, 8, "AI", "dep-go", false);
        var twR106 = new Tower("Enfield 1-1");
        twR106.Tick(taxiing, none, 0);
        twR106.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        twR106.Tick(taxiing, new[] { cross }, 2);   // hold position
        r = twR106.Tick(taxiing with { Ias = 0 }, new[] { cross with { Speed = 0 } }, 3);   // AI stops briefly: conflict gone, still no "continue taxi" after 1 s
        Check(r.Count == 0, "R106 kein Flattern nach 1 s: " + string.Join(" | ", r));
        r = twR106.Tick(taxiing with { Ias = 0 }, new[] { cross with { Speed = 0, X = taxiing.X + 30, Z = taxiing.Z + 30 } }, 12);   // is 42 m in the taxiway
        Check(r.Count == 0, "R106 stehender Verkehr bleibt Hindernis: " + string.Join(" | ", r));
        r = twR106.Tick(taxiing with { Ias = 0 }, new[] { cross with { Speed = 0, X = taxiing.X + 200, Z = taxiing.Z + 200 } }, 13);   // far away and larger distance
        Check(r.Count == 1 && r[0].Contains("continue taxi"), "R106 frei: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twR106b = new Tower("Enfield 1-1");
        twR106b.Tick(taxiing, none, 0);
        twR106b.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        twR106b.Tick(taxiing, new[] { cross }, 2);
        r = twR106b.Tick(taxiing with { Ias = 0 }, new[] { cross2 }, 10);   // the first has passed, the second of the same group still crosses
        Check(r.Count == 0, "R106 Gruppe: kein \"continue taxi\" mit dem nächsten im Konflikt: " + string.Join(" | ", r));
        r = twR106b.Tick(taxiing with { Ias = 0 }, none, 11);
        Check(r.Count == 1 && r[0].Contains("continue taxi"), "R106 Gruppe vorbei: " + (r.FirstOrDefault()?.Text ?? "-"));
        var twR106c = new Tower("Enfield 1-1");   // held for one, another (other group) still crosses: no "continue taxi"
        twR106c.Tick(taxiing, none, 0);
        twR106c.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        twR106c.Tick(taxiing, new[] { cross }, 2);
        r = twR106c.Tick(taxiing with { Ias = 0 }, new[] { cross with { Z = taxiing.Z - 400 }, cross2 with { Id = 11, Group = "Other" } }, 10);
        Check(r.Count == 0, "R106 anderer Verkehr im Konflikt: kein \"continue taxi\": " + string.Join(" | ", r));
        var twR106d = new Tower("Enfield 1-1");   // Traffic stays 45 m beside me (not in the way): "continue taxi" anyway
        twR106d.Tick(taxiing, none, 0);
        twR106d.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", taxiing, none, 1);
        twR106d.Tick(taxiing, new[] { cross }, 2);
        twR106d.Tick(taxiing with { Ias = 0 }, new[] { cross with { Speed = 0, X = taxiing.X, Z = taxiing.Z + 45 } }, 10);
        r = twR106d.Tick(taxiing with { Ias = 0 }, new[] { cross with { Speed = 0, X = taxiing.X, Z = taxiing.Z + 45 } }, 11);
        Check(r.Count == 1 && r[0].Contains("continue taxi"), "R106 stehender Verkehr neben mir: " + (r.FirstOrDefault()?.Text ?? "-"));

        // --- R107: parked at the stand (engine off) from TaxiOut -> parked, new "request startup" is answered normally; not when far from the stand
        var twR107 = new Tower("Enfield 1-1");
        twR107.Tick(parked, none, 0);
        twR107.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 1);
        twR107.EngineOff(At(parked.X + 800, parked.Z));
        bool r107far = twR107.Phase == Phase.TaxiOut;
        twR107.EngineOff(At(parked.X + 20, parked.Z));
        r = twR107.OnTranscript("Kutaisi Ground, Enfield 1-1, request startup", parked, none, 5);
        Check(r107far && twR107.Phase == Phase.StartupApproved && r.Count == 1 && r[0].Contains("start up approved"), $"R107 Abstellen am Stand: weit {r107far}, " + (r.FirstOrDefault()?.Text ?? "-"));

        // --- R108: stands assigned by other players (Reserved) are not assigned again
        var twR108 = new Tower("Enfield 1-1");
        var s91 = At(parked.X, parked.Z + 4000);
        twR108.Tick(s91, none, 0);
        twR108.F.Spots.AddRange(new[] { (s91.X, s91.Z, 91, 72), (s91.X + 60, s91.Z, 92, 72) });
        r = twR108.OnTranscript("Kutaisi Ground, Enfield 11, runway vacated, request taxi to parking", s91, none, 20);
        string own108 = r.FirstOrDefault()?.Text ?? "-";
        var twR108b = new Tower("Enfield 2-1");
        twR108b.Tick(s91, none, 0);
        twR108b.F.Spots.AddRange(new[] { (s91.X, s91.Z, 91, 72), (s91.X + 60, s91.Z, 92, 72) });
        twR108b.Reserved = new() { (s91.X + 3, s91.Z) };
        r = twR108b.OnTranscript("Kutaisi Ground, Enfield 21, runway vacated, request taxi to parking", s91, none, 20);
        Check(own108.Contains("parking spot niner one") && r[0].Contains("parking spot niner two"), $"R108 Reservierung: {own108} | {r[0]}");

        // --- IFR: approach -> radar vectoring to final instead of CRP, ATIS announces it
        ForceIfr = true;
        var tw20 = new Tower("Enfield 1-1");
        var far = At(CX + 20 * NM, CZ, 1500, 180, 130);
        tw20.Tick(far, none, 0);
        r = tw20.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", far, none, 1);
        Check(r[0].Contains("approach runway") && r[0].Contains("heading") && !r[0].Contains("C R P"), r[0]);
        Check(kTower.AtisText("A", At(CX, CZ), 5, false, 0, 2000).Contains("expect vectors"), "IFR im ATIS");

        // --- V13 Pilot requests: altitude, direct, missed approach (IFR), practice approach, runway, VFR
        var tw21 = new Tower("Enfield 1-1");
        var north = At(CX + 15 * NM, CZ, 8000 * Ft, 180, 130);
        tw21.Tick(north, none, 0);
        tw21.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", north, none, 1);
        double vf = tw21.vecFt;
        r = tw21.OnTranscript("Kutaisi Approach, Enfield 1-1, request lower", north, none, 40);
        Check(r[0].Contains($"descend and maintain {vf - 1000} feet") && tw21.vecFt == vf - 1000, "Wunsch lower: " + r[0]);
        r = tw21.OnTranscript("Kutaisi Approach, Enfield 1-1, request flight level 200", north, none, 80);
        Check(r[0].Contains($"unable higher, expect lower shortly, maintain {vf - 1000} feet") && tw21.vecFt == vf - 1000, "Wunsch FL200 15 NM vor dem Platz (R303: Anflug braucht den Sinkflug): " + r[0]);
        r = tw21.OnTranscript("Kutaisi Approach, Enfield 1-1, request 1000 feet", north, none, 120);
        Check(r[0].Contains("unable, minimum altitude") && tw21.vecFt == vf - 1000, "Wunsch zu tief: " + r[0]);
        int legs = tw21.vec!.Count;
        r = tw21.OnTranscript("Kutaisi Approach, Enfield 1-1, request direct", north, none, 160);
        Check(r[0].Contains("direct approved") && tw21.vec!.Count < legs, $"Wunsch direct ({legs} Schenkel): " + r[0]);
        var past = At(Thr25X - 1500 * Ux, Thr25Z - 1500 * Uz, 100, LandHdg("25"), 80);   // past threshold 25, 330 ft
        r = tw21.OnTranscript("Kutaisi Tower, Enfield 1-1, going missed", past, none, 200);
        // R44: two steps: Tower only runway heading, altitude, frequency (before: turn and vectors at once); vectors after check-in with Approach, without check-in after 45 s
        // R102: the check-in with Approach is responsible (no "contact Kutaisi Tower" -> ping-pong, no second missed approach, no rejection)
        var t21 = tw21.Tick(past with { AltMsl = past.AltMsl + 100 }, none, 210);
        var ci21 = tw21.OnTranscript("Kutaisi Approach, Enfield 1-1, missed approach, climbing 3000", past with { AltMsl = past.AltMsl + 200 }, none, 220);
        Check(r.Count == 1 && r[0].Role == "Tower" && r[0].Text == "Enfield one one, roger, go around. Fly runway heading, climb and maintain 3000 feet, contact Kutaisi Approach two six six decimal five." &&
              tw21.Phase == Phase.Inbound && t21.Count == 0 && ci21.Count == 1 && ci21[0].Role == "Approach" && ci21[0].Text.StartsWith("Enfield one one, Kutaisi Approach, identified. Turn ") &&
              ci21[0].Contains("climb and maintain 3000 feet") && ci21[0].Text.EndsWith(" Say intentions.") && !ci21[0].Contains("ontact") && tw21.Vectoring && tw21.Rejected == 0,
              "R44/R102 Fehlanflug IFR: " + string.Join(" | ", r.Concat(t21).Concat(ci21)));
        var tw22 = new Tower("Enfield 1-1");
        var fin = At(P("25", 3 * NM).Item1, P("25", 3 * NM).Item2, 300, LandHdg("25"), 75);
        tw22.Tick(fin, none, 0);
        var r22 = tw22.OnTranscript("Kutaisi Approach, Enfield 1-1, request practice ILS approach", fin, none, 1);
        r = tw22.OnTranscript("Kutaisi Tower, Enfield 1-1, final", fin, none, 2);
        Check(r[0].Contains("cleared low approach") && !r[0].Contains("closed traffic"), "A96: Übungsanflug ohne Platzrunde in der Freigabe: " + r[0]);   // afterwards radar vectoring, no pattern
        Check(tw22.option == "low approach" && tw22.wantStraight && tw22.Phase == Phase.ClearedLand, $"Übungsanflug: {tw22.option} {tw22.Phase}");
        // R217: practice approach: climb-out already in the approach clearance ("on the go fly runway heading, climb and maintain …"), on go-around only the frequency
        r = tw22.Tick(past, none, 3);
        Check(r22.Count == 1 && r22[0].Contains("on the go fly runway heading, climb and maintain") && r.Count == 2 && r[0].Role == "Tower" && r[0].Text == "Enfield one one, contact Kutaisi Approach two six six decimal five." &&
              tw22.Phase == Phase.Inbound, "R217 Übungsanflug -> Radarführung: " + string.Join(" | ", r22.Concat(r)));
        {   // R44 · Reporting obligation #9: practice approach without check-in: no identification on its own; after 45 s the Tower reminds, 60 s later violation; later check-in gets vectors
            var twPx = new Tower("Enfield 1-1");
            twPx.Tick(fin, none, 0);
            twPx.OnTranscript("Kutaisi Approach, Enfield 1-1, request practice ILS approach", fin, none, 1);
            twPx.OnTranscript("Kutaisi Tower, Enfield 1-1, final", fin, none, 2);
            var p1 = twPx.Tick(past, none, 3);
            var p2 = twPx.Tick(past with { AltMsl = past.AltMsl + 300 }, none, 30);
            var p3 = twPx.Tick(past with { AltMsl = past.AltMsl + 500 }, none, 50);
            var p3b = twPx.Tick(past with { AltMsl = past.AltMsl + 500 }, none, 100);
            Check(p1.Count == 2 && p1[0].Role == "Tower" && p1[0].Text == "Enfield one one, contact Kutaisi Approach two six six decimal five." && p2.Count == 0 &&
                  p3.Count == 1 && p3[0].Role == "Tower" && p3[0].Text == "Enfield one one, contact Kutaisi Approach now, two six six decimal five." && !p3[0].Contains("identified") && p3b.Count == 0 && !twPx.Vectoring,
                  "#9 Übungsanflug ohne Check-in: " + string.Join(" | ", p1.Concat(p2).Concat(p3).Concat(p3b)));
            var p5 = twPx.Tick(past with { AltMsl = past.AltMsl + 600 }, none, 111);
            Check(p5.Count == 2 && p5[0].Role == "Tower" && p5[0].Text.StartsWith("Enfield one one, no contact with Kutaisi Approach, continue runway heading, maintain ") &&
                  p5[0].Contains("contact Kutaisi Approach now") && p5[1].Text.StartsWith(L("Verstoß: kein Check-in bei Approach", "Deviation: no check-in with approach")) && twPx.Tick(past, none, 140).All(m => m.Text.Contains("possible pilot deviation")),   // R247: advisory separate
                  "#9 Fehlanflug ohne Check-in, Konsequenz: " + string.Join(" | ", p5));
            // Check-in only after the consequence: vectors (heading/altitude)
            var p4 = twPx.OnTranscript("Kutaisi Approach, Enfield 1-1, missed approach", past with { AltMsl = past.AltMsl + 600 }, none, 145);
            Check(p4.Count == 1 && p4[0].Role == "Approach" && p4[0].Text.StartsWith("Enfield one one, Kutaisi Approach, ") && p4[0].Contains("heading") && !p4[0].Contains("contact") && twPx.Vectoring,
                  "R44 später Check-in: " + string.Join(" | ", p4));
        }
        r = tw22.OnTranscript("Kutaisi Approach, Enfield 1-1, cancel IFR", north, none, 4);   // without gap > 10 s: otherwise new flight (position jump)
        Check(r[0].Contains("negative, field is I F R"), "IFR: cancel IFR abgelehnt: " + r[0]);
        ForceIfr = false;
        r = tw22.OnTranscript("Kutaisi Approach, Enfield 1-1, cancel IFR", north, none, 5);
        Check(r[0].Contains("I F R cancelled") && !tw22.wantStraight, "VFR: cancel IFR: " + r[0]);
        // P3-AP7: Visual approach = straight-in approach without procedure (no "cleared ILS")
        // R115: clearance only when the field is reported in sight ("report field in sight"); up to 4 NM without report fallback
        var fin6 = At(P("25", 6 * NM).Item1, P("25", 6 * NM).Item2, 300, LandHdg("25"), 75);
        var twVa = new Tower("Enfield 1-1");
        twVa.Tick(fin6, none, 0);
        r = twVa.OnTranscript("Kutaisi Approach, Enfield 1-1, request visual approach", fin6, none, 1);
        Check(r.Count == 1 && r[0].Text.Contains("report field in sight") && !r[0].Text.Contains("cleared") && !r[0].Text.Contains("Contact") && twVa.visAsk && twVa.Phase == Phase.Inbound, "R115 Visual Approach: erst Platz in Sicht: " + r[0]);
        r = twVa.OnTranscript("Enfield 1-1, wilco, report field in sight", fin6, none, 2);
        Check(r.Count == 0 && twVa.visAsk, "R115 Rücklesung \"report field in sight\" schweigt, keine Freigabe");
        r = twVa.OnTranscript("Kutaisi Approach, Enfield 1-1, negative field in sight", fin6, none, 3);
        Check(r.Count == 1 && r[0].Text.Contains("report field in sight") && twVa.visAsk && twVa.Phase == Phase.Inbound, "R115 \"negative\": weiter melden lassen: " + r.FirstOrDefault()?.Text);
        r = twVa.OnTranscript("Kutaisi Approach, Enfield 1-1, field in sight", fin6, none, 4);
        Check(r[0].Text.Contains("cleared visual approach runway") && r[0].Text.Contains("Contact") && !r[0].Text.Contains("I L S") && twVa.wantStraight && twVa.Phase == Phase.Entering && !twVa.visAsk, "Visual Approach nach \"field in sight\": " + r[0]);
        var twVb = new Tower("Enfield 1-1");
        twVb.Tick(fin6, none, 0);
        r = twVb.OnTranscript("Kutaisi Approach, Enfield 1-1, field in sight", fin6, none, 1);
        Check(r.Count == 1 && r[0].Text.Contains("roger") && !r[0].Text.Contains("cleared") && twVb.Phase == Phase.Away, "R115 \"field in sight\" ohne Frage: nur roger: " + r.FirstOrDefault()?.Text);
        r = twVb.OnTranscript("Kutaisi Approach, Enfield 1-1, request visual approach, field in sight", fin6, none, 2);
        Check(r[0].Text.Contains("cleared visual approach runway") && twVb.Phase == Phase.Entering, "R115 Visual Approach mit Platz schon in der Anfrage: " + r[0]);
        var twVc = new Tower("Enfield 1-1");
        twVc.Tick(fin6, none, 0);
        twVc.OnTranscript("Kutaisi Approach, Enfield 1-1, request visual approach", fin6, none, 1);
        var fin35 = At(P("25", 3.5 * NM).Item1, P("25", 3.5 * NM).Item2, 300, LandHdg("25"), 75);
        r = twVc.Tick(fin35, none, 2);
        Check(r.Count == 1 && r[0].Text.Contains("straight in approach") && !r[0].Text.Contains("visual") && twVc.Phase == Phase.Entering && !twVc.visAsk, "R115 Platz bis 4 NM nicht gemeldet: Rückfall ohne Visual: " + r.FirstOrDefault()?.Text);
        var twVd = new Tower("Enfield 1-1");   // R115: visual report without station call after "negative" is not a repetition
        twVd.Tick(fin6, none, 0);
        twVd.OnTranscript("Kutaisi Approach, Enfield 1-1, request visual approach", fin6, none, 1);
        twVd.OnTranscript("Enfield 1-1, negative field in sight", fin6, none, 10);
        r = twVd.OnTranscript("Enfield 1-1, field in sight", fin6, none, 25);
        Check(r.Count == 1 && r[0].Text.Contains("cleared visual approach runway") && twVd.Phase == Phase.Entering, "R115 \"field in sight\" nach \"negative\": " + r.FirstOrDefault()?.Text);
        var twVe = new Tower("Enfield 1-1");   // R115: "field in sight, cancel IFR" remains cancellation (also during "report field in sight")
        twVe.Tick(fin6, none, 0);
        twVe.OnTranscript("Kutaisi Approach, Enfield 1-1, request visual approach", fin6, none, 1);
        r = twVe.OnTranscript("Kutaisi Approach, Enfield 1-1, field in sight, cancel IFR", fin6, none, 2);
        Check(r.Count == 1 && !r[0].Text.Contains("cleared visual") && !twVe.wantStraight && !twVe.visAsk, "R115 \"field in sight, cancel IFR\": " + r.FirstOrDefault()?.Text);
        var twVg = new Tower("Enfield 1-1");
        twVg.Tick(fin6, none, 0);
        twVg.OnTranscript("Kutaisi Approach, Enfield 1-1, request straight in", fin6, none, 1);
        r = twVg.OnTranscript("Kutaisi Approach, Enfield 1-1, field in sight, cancel IFR", fin6, none, 2);
        Check(r.Count == 1 && !twVg.wantStraight && r[0].Text != "Enfield one one, roger.", "R115 Geradeaus-Anflug, \"field in sight, cancel IFR\": " + r.FirstOrDefault()?.Text);
        var twVf = new Tower("Enfield 1-1");   // R115: "runway in sight" at the Tower (Entering) is acknowledged by the Tower, no "contact Approach"
        twVf.Tick(fin6, none, 0);
        twVf.OnTranscript("Kutaisi Approach, Enfield 1-1, request straight in", fin6, none, 1);
        r = twVf.OnTranscript("Kutaisi Tower, Enfield 1-1, runway in sight", fin6, none, 3);
        Check(twVf.Phase == Phase.Entering && r.Count == 1 && r[0].Role == "Tower" && r[0].Text.Contains("roger") && !r[0].Text.Contains("ontact"), "R115 \"runway in sight\" beim Tower: " + r.FirstOrDefault()?.Text);
        r = twVf.OnTranscript("Enfield 1-1, runway in sight", fin6, none, 40);
        Check(r.Count == 1 && r[0].Role == "Tower", "R115 \"runway in sight\" ohne Anruf auf Final: Tower: " + r.FirstOrDefault()?.Role);
        // R102: initial call at the Tower after "cleared ILS …, contact Tower" (Entering): Tower accepts it (previously "contact Kutaisi Approach" -> Approach handed back to the Tower)
        var twR102 = new Tower("Enfield 1-1");
        twR102.Tick(fin, none, 0);
        twR102.OnTranscript("Kutaisi Approach, Enfield 1-1, request ILS approach", fin, none, 1);
        r = twR102.OnTranscript("Kutaisi Tower, Enfield 1-1, ILS runway 25", fin, none, 3);
        Check(twR102.Phase == Phase.Entering && r[0].Role == "Tower" && r[0].Contains("Kutaisi Tower") && r[0].Contains("continue approach, report four mile final") &&
              !r[0].Contains("ontact") && twR102.Rejected == 0, "R102 Erstanruf beim Tower nach der Übergabe: " + r[0]);
        var twR102i = new Tower("Enfield 1-1");   // "inbound" crosses with the break clearance (Initial, 5 NM from the airfield): Tower repeats, no "contact Approach"
        var ini6 = At(P("25", 6 * NM).Item1, P("25", 6 * NM).Item2, 1500 * Ft, LandHdg("25"), 150);
        twR102i.Tick(ini6, none, 0);
        twR102i.Phase = Phase.Initial;
        r = twR102i.OnTranscript("Kutaisi Tower, Enfield 1-1, inbound", ini6, none, 1);
        Check(r[0].Role == "Tower" && r[0].Contains("report base") && twR102i.Phase == Phase.Initial && twR102i.Rejected == 0, "R102 inbound auf dem Initial: " + r[0]);
        var twR102p = new Tower("Enfield 1-1");   // Left the pattern without report, 15 NM away back: new approach via Approach, no "continue" from the Tower
        twR102p.Tick(north, none, 0);
        twR102p.Phase = Phase.Pattern;
        r = twR102p.OnTranscript("Kutaisi Approach, Enfield 1-1, 15 miles north, inbound for landing", north, none, 1);
        Check(r[0].Role == "Approach" && !r[0].Contains("ontact") && !r[0].Contains("report final") && twR102p.Phase == Phase.Inbound && twR102p.Rejected == 0, "R102 inbound weit weg aus der Platzrunde: " + r[0]);
        ForceIfr = true;   // Visual approach needs visibility: IFR weather -> rejection like "cancel IFR"
        var twVi = new Tower("Enfield 1-1");
        twVi.Tick(fin, none, 0);
        r = twVi.OnTranscript("Kutaisi Approach, Enfield 1-1, request visual approach", fin, none, 1);
        Check(r[0].Contains("negative, field is I F R") && !r[0].Contains("cleared visual"), "IFR: visual approach abgelehnt: " + r[0]);
        ForceIfr = false;
        // A29: minimum fuel is acknowledged (position, no priority in the text), on the ground no "say again"
        var twMf = new Tower("Enfield 1-1") { QueueAhead = 1, Spaced = true };
        twMf.Tick(north, none, 0);
        r = twMf.OnTranscript("Kutaisi Approach, Enfield 1-1, minimum fuel", north, none, 1);
        Check(r[0].Contains("roger minimum fuel, number 2, expect no delay") && twMf.Phase == Phase.Away, "minimum fuel: " + r[0]);
        twMf.QueueAhead = 2; twMf.Spaced = false;
        r = twMf.OnTranscript("Kutaisi Approach, Enfield 1-1, request low fuel", north, none, 2);
        Check(r[0].Contains("number 3, expect approach in 9 minutes"), "low fuel mit Wartenden: " + r[0]);
        r = twMf.OnTranscript("Kutaisi Approach, Enfield 1-1, bingo", north, none, 3);   // R246: "roger minimum fuel" only if the pilot said it
        Check(r[0].Contains(", roger, number 3") && !r[0].Contains("minimum fuel"), "R246: bingo ohne minimum fuel: " + r[0]);
        r = new Tower("Enfield 1-1").OnTranscript("Kutaisi Tower, Enfield 1-1, minimum fuel", null, none, 1);
        Check(r[0].Contains("on the ground") && !r[0].Contains("say again"), "minimum fuel am Boden: " + r[0]);
        var tw23 = new Tower("Enfield 1-1");
        var east = At(CX, CZ + 20 * NM, 8000 * Ft, 270, 130);
        tw23.Tick(east, none, 0);
        r = tw23.OnTranscript("Kutaisi Approach, Enfield 1-1, request heading 270", east, none, 1);
        Check(r[0].Contains("fly heading two seven zero, approved"), "Wunsch Kurs: " + r[0]);
        r = tw23.OnTranscript("Kutaisi Approach, Enfield 1-1, request direct", east, none, 2);
        Check(r[0].Contains("cleared direct Kutaisi"), "Wunsch direct (Strecke): " + r[0]);
        r = tw23.OnTranscript("Kutaisi Approach, Enfield 1-1, request higher", east, none, 3);
        Check(r[0].Contains("climb and maintain 10000 feet"), "Wunsch higher (Strecke): " + r[0]);
        r = tw23.OnTranscript("Kutaisi Approach, Enfield 1-1, request runway 07", east, none, 4);
        tw23.Tick(east, none, 5);
        Check(r[0].Contains("expect runway zero seven") && tw23.Runway == "07", $"Wunsch Bahn: {tw23.Runway} " + r[0]);
        var tw24 = new Tower("Enfield 1-1") { FieldWind = (Ux * 7.7, Uz * 7.7) };   // 15 kt tailwind on 07
        tw24.Tick(east, none, 0);
        r = tw24.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing, request runway 07", east, none, 1);
        Check(r[0].Text.EndsWith("Unable runway zero seven, tailwind one five knots.") && tw24.Runway == "25", "Wunsch Bahn Rückenwind: " + r[0]);
        // A139: similar AI callsign (Enfield 5-1 next to Enfield 1-1) announce once, flight partner (1-2), different word and distant aircraft not
        var keepCs = Ops.Callsigns;
        Ops.Callsigns = new Dictionary<string, string> { ["A"] = "Enfield 5-1", ["B"] = "Enfield 1-2", ["C"] = "Colt 5-1", ["D"] = "Enfield 7-1" };
        var simTr = new[] { new Traffic(1, "F-16C", CX, CZ + 5 * NM, 2000, 0, 100, "A"), new Traffic(2, "F-16C", CX, CZ, 2000, 0, 100, "B"), new Traffic(3, "F-16C", CX, CZ, 2000, 0, 100, "C"),
                            new Traffic(4, "F-16C", CX, CZ + 80 * NM, 2000, 0, 100, "D") };
        var tw139 = new Tower("Enfield 1-1");
        tw139.Tick(east, none, 0);
        r = tw139.OnTranscript("Kutaisi Tower, Enfield 1-1, request wind", east, simTr, 1);
        Check(r[0].Text.EndsWith("Caution similar callsign, Enfield five one is also on this frequency.") && !r[0].Text.Contains("one two") && !r[0].Text.Contains("Colt") && !r[0].Text.Contains("seven"), "A139 ähnliches Rufzeichen: " + r[0]);
        r = tw139.OnTranscript("Kutaisi Tower, Enfield 1-1, request wind", east, simTr, 100);
        Check(!r[0].Text.Contains("similar callsign"), "A139 nur einmal: " + r[0]);
        Ops.Callsigns = keepCs;
        // A41: runway request on the ground – accepted: "runway zero seven approved, taxi …", stays until takeoff (tick in Parked/taxiing), afterwards the wind applies again
        var tw41c = new Tower("Enfield 1-1");
        tw41c.Tick(parked, none, 0);
        r = tw41c.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi, request runway 07", parked, none, 1);
        Check(tw41c.Runway == "07" && tw41c.Phase == Phase.TaxiOut && r[0].Contains("Kutaisi Ground, runway zero seven approved, taxi to holding point runway zero seven"), "A41 Bahnwunsch beim Rollen: " + r[0]);
        tw41c.Tick(parked, none, 2);
        Check(tw41c.Runway == "07", "A41 Bahnwunsch bleibt beim Rollen: " + tw41c.Runway);
        tw41c.Phase = Phase.ClearedTakeoff;
        tw41c.Tick(At(Thr25X, Thr25Z, 150, 70, 100), none, 3);
        Check(tw41c.Phase == Phase.Departing && tw41c.wantRw == null, $"A41 Bahnwunsch des Abflugs bindet nicht die Landung: {tw41c.Phase} {tw41c.wantRw}");
        // during startup and with clearance (phase stays Parked, the tick must not reset the runway)
        var tw41c2 = new Tower("Enfield 1-1") { AtisLetter = "C" };
        tw41c2.Tick(parked, none, 0);
        r = tw41c2.OnTranscript("Kutaisi Ground, Enfield 1-1, with information Charlie, request startup, runway 07", parked, none, 1);
        Check(tw41c2.Runway == "07" && r[0].Text == "Enfield one one, Kutaisi Ground, runway zero seven approved, start up approved. Report ready to taxi.", "A41 Bahnwunsch beim Anlassen: " + r[0]);
        var tw41c3 = new Tower("Enfield 1-1");
        tw41c3.Tick(parked, none, 0);
        r = tw41c3.OnTranscript("Kutaisi Ground, Enfield 1-1, request IFR clearance, runway 07", parked, none, 1);
        tw41c3.Tick(parked, none, 2);
        Check(tw41c3.Phase == Phase.Parked && tw41c3.Runway == "07" && r[0].Contains("runway zero seven approved, cleared"), "A41 Bahnwunsch mit Clearance: " + r[0]);
        // same runway as in use anyway -> no confirmation; rejected (tailwind, no such runway) -> only the rejection, no taxi clearance
        r = new Tower("Enfield 1-1").OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi, runway 25", parked, none, 1);
        Check(r[0].Contains("Kutaisi Ground, taxi to holding point runway two five") && !r[0].Contains("approved"), "A41 gleiche Bahn: " + r[0]);
        var tw41d = new Tower("Enfield 1-1") { FieldWind = (Ux * 7.7, Uz * 7.7) };   // 15 kt tailwind on 07
        tw41d.Tick(parked, none, 0);
        r = tw41d.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi, request runway 07", parked, none, 1);
        Check(r.Count == 1 && r[0].Text.EndsWith("Unable runway zero seven, tailwind one five knots, runway two five in use.") && tw41d.Phase == Phase.Parked && tw41d.Runway == "25", "A41 Wunsch Rückenwind am Boden: " + r[0]);
        r = tw41d.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi runway 13", parked, none, 5);
        Check(r[0].Text.EndsWith("Unable runway one three, runway two five in use.") && tw41d.Phase == Phase.Parked, "A41 Wunsch fremde Bahn am Boden: " + r[0]);
        r = tw41d.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi", parked, none, 9);
        Check(tw41d.Phase == Phase.TaxiOut && r[0].Contains("taxi to holding point runway two five"), "A41 danach normal rollen: " + r[0]);
        // A41: requested 07 against the traffic flow, C-130 on final 25 -> hold short (opposite traffic, also after 3 min), takeoff clearance only without him, withdrawn if he returns
        var tw41e = new Tower("Enfield 1-1");
        tw41e.Tick(parked, none, 0);
        tw41e.OnTranscript("Kutaisi Ground, Enfield 1-1, request taxi, request runway 07", parked, none, 1);
        var hold07 = At(K.End("07").ThrX, K.End("07").ThrZ + 120);
        r = tw41e.OnTranscript("Enfield 11 ready for departure", hold07, new[] { landing }, 2);
        Check(tw41e.Phase == Phase.HoldShort && r[0].Text.Contains("hold short runway zero seven, opposite direction traffic, C 130 on") && r[0].Text.EndsWith("runway two five."), "A41 Gegenverkehr beim Abflug: " + r[0]);
        r = tw41e.Tick(hold07, new[] { landing }, 200);
        Check(tw41e.Phase == Phase.HoldShort && r.Count == 0, "A41 Gegenverkehr auch nach 3 min: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw41e.Tick(hold07, none, 201);
        Check(tw41e.Phase == Phase.ClearedTakeoff && r[0].Text.Contains("runway zero seven") && r[0].Text.Contains("cleared for takeoff"), "A41 Freigabe ohne Gegenverkehr: " + (r.FirstOrDefault()?.Text ?? "-"));
        r = tw41e.Tick(hold07, new[] { landing }, 202);
        Check(tw41e.Phase == Phase.HoldShort && r[0].Text.Contains("cancel takeoff clearance"), "A41 Freigabe zurück bei Gegenverkehr: " + (r.FirstOrDefault()?.Text ?? "-"));

        // --- Terrain like Batumi: lake in the northwest, mountains (1200 m) from 3.5 NM southeast. Runway 31 (main runway) from the southeast impossible.
        // BATUMI_GRID=<DcsAtc-Terrain-Caucasus-22.txt>: the same with the real grid from DCS.
        var bat = Airfield.FromDcs("Batumi", -356437.2, 618210.9, 10, new[] { ("31", 0.95013, -355810.7, 617386.2, 2070.0) });
        var e13 = bat.End("13");
        var grid = Environment.GetEnvironmentVariable("BATUMI_GRID");
        if (grid != null) bat.LoadTerrain(grid);
        else
        {
            var gB = new float[61, 61];
            double bx0 = bat.X - 30 * NM, bz0 = bat.Z - 30 * NM;
            for (int i = 0; i < 61; i++)
                for (int j = 0; j < 61; j++)
                    gB[i, j] = (bx0 + (i + 0.5) * NM - e13.CX) * e13.Dx + (bz0 + (j + 0.5) * NM - e13.CZ) * e13.Dz > 3.5 * NM ? 1200 : 0;
            bat.SetTerrain(gB, bx0, bz0, NM);
        }
        // Simulated pilot (as above, also climbs): flies headings/altitudes, straight ahead on the centerline by itself. Log distance to terrain.
        string lastMsg = "";   // last call (handoff)
        List<string> vecSaid = new();   // A49: all calls of the last flight, plus the reply to "say again" (after 150 s)
        string sayAg = "";
        Telemetry flyT = null!; double flySec = 0;   // R212: last state of the flight (Tower call afterwards)
        (Tower Tv, string First, double Alt, double MinClr) Fly(double x, double z, double ft, double h, string call, (double, double)? wind = null, bool sayAgain = false, string? sight = null)
        {
            vecSaid.Clear();
            var tv = new Tower(bat, "Enfield 1-1") { FieldWind = wind };
            double alt = ft * Ft, clr = 99999;
            Telemetry T(double vs = 0) => new(alt, alt - bat.Elev, 140, h * Math.PI / 180, x, z, 0, 0, 760, vs);
            tv.Tick(T(), none, 0);
            var first = tv.OnTranscript(call, T(), none, 1).FirstOrDefault()?.Text ?? "-";
            double target = tv.vecHdg;
            for (int sec = 2; sec < 2400 && tv.Phase == Phase.Inbound; sec++)
            {
                if (tv.vec is { Count: 0 } && tv.AL(x, z, tv.vrw) is var (pa, pl) && Math.Abs(pl) < NM) { var q = tv.Pt(tv.vrw, pa - 1.5 * NM, 0); target = Bearing(x, z, q.X, q.Z); }
                double d = ((target - h) % 360 + 540) % 360 - 180;
                h = (h + Math.Clamp(d, -3, 3) + 360) % 360;
                x += 140 * Math.Cos(h * Math.PI / 180); z += 140 * Math.Sin(h * Math.PI / 180);
                double alt0 = alt, want = tv.vecFt * Ft;
                alt = want > alt ? Math.Min(alt + 15, want) : Math.Max(alt - 15, want);
                clr = Math.Min(clr, alt / Ft - bat.TerrainFt(x, z, 0));
                var m = tv.Tick(flyT = T(alt - alt0), none, flySec = sec);
                if (m.Count > 0) { target = tv.vecHdg; lastMsg = m[0].Text; vecSaid.Add(lastMsg); }
                if (tv.visAsk) target = tv.LandHdg(tv.Runway);   // R115: waits for the visual report, the pilot flies the runway direction (vecHdg is stale)
                if (sight != null && tv.visAsk && tv.OnTranscript(sight, T(), none, sec) is [{ } sm, ..]) { lastMsg = sm.Text; vecSaid.Add(lastMsg); }   // R115: pilot reports the airfield
                if (sayAgain && sec == 150 && tv.OnTranscript("Enfield 1-1, say again", T(), none, sec) is [{ } ag, ..]) sayAg = ag.Text;
                if (m.Count > 0 && Environment.GetEnvironmentVariable("VECDBG") == "Batumi") Console.WriteLine($"  {sec}s {(alt - bat.Elev) / Ft:0} ft: {m[0].Text}");
            }
            return (tv, first, alt / Ft, clr);
        }
        Check(bat.Hand("31") == (grid == null ? "left hand" : "right hand"), "Batumi Platzrunde 31: " + bat.Hand("31"));
        // as in reality (AIP: jets land only on 13 from the sea): up to 10 kt tailwind the 13, no overhead with 180° onto the 31
        var (t13, f13, _, c13) = Fly(bat.X + 20 * NM, bat.Z + 5 * NM, 8000, 180, "Batumi Approach, Enfield 1-1, inbound for landing", null, true);
        // A49: radar instructions only with changed elements (distance at first contact and last vector, leg only on change), "say sayAg" complete
        Check(f13.Contains("20 miles from initial") && f13.Contains("descend and maintain") && f13.Contains("heading") && f13.Contains("vectors to initial") &&
              vecSaid.Any(s => s.Contains("fly heading") && !s.Contains("miles from") && !s.Contains("maintain") && !s.Contains("vectors for")) &&
              vecSaid.Append(f13).Count(s => s.Contains("vectors for initial")) == 1 && vecSaid.Count(s => s.Contains("intercept the extended centerline")) == 1 &&
              !vecSaid.Concat(new[] { f13, sayAg }).Any(s => s.Contains("next turn") || s.Contains("intercept turn")), "Radarführung: nur Änderungen, kein Komplettpaket: " + string.Join(" | ", vecSaid));
        Check(sayAg.Contains("miles from initial") && sayAg.Contains("maintain") && sayAg.Contains("speed 300 knots") && (sayAg.Contains("downwind") || sayAg.Contains("vectors for")) && sayAg.Contains("heading"), "say again: volles Paket: " + sayAg);
        Check(f13.Contains("Due to terrain, expect vectors to initial runway one three") && t13.Runway == "13" && t13.navName == "initial" && c13 >= 900, "Batumi Sicht windstill -> 13: " + f13);
        var nw = (e13.Dx * 7.7, e13.Dz * 7.7);   // 15 kt from northwest: 31 against the wind, too much tailwind on 13 -> overhead or circling over the 13 line
        foreach (var (sx, sz, sft, sh, name) in new[] { (bat.X, bat.Z + 25 * NM, 15000.0, 270.0, "Ost"), (bat.X + 20 * NM, bat.Z + 5 * NM, 8000.0, 180.0, "Nord"), (bat.X - 15 * NM, bat.Z, 9000.0, 0.0, "Süd") })
        {
            var (tv, first, ft, clr) = Fly(sx, sz, sft, sh, "Batumi Approach, Enfield 1-1, inbound for landing", nw);
            Check(first.Contains("Due to terrain, expect vectors for overhead join runway three one") && tv.Phase == Phase.Entering && tv.navName == "overhead" &&
                  clr >= 900 && ft <= bat.PatternFt + 1100, $"Batumi Sicht von {name}: Übergabe {ft:0} ft, Geländeabstand min {clr:0} ft, {first}");
        }
        ForceIfr = true;
        bat.Ils["13"] = (110.3, "ILU");   // as from Beacons.lua (Program.RadioTest checks the parsing)
        foreach (var (sx, sz, sft, sh, name) in new[] { (bat.X + 20 * NM, bat.Z + 5 * NM, 8000.0, 180.0, "Nord"), (bat.X, bat.Z + 25 * NM, 15000.0, 270.0, "Ost") })
        {
            var (tv, first, ft, clr) = Fly(sx, sz, sft, sh, "Batumi Approach, Enfield 1-1, inbound for landing");
            Check(first.Contains("Due to terrain, expect vectors for ILS approach runway one three, localizer one one zero decimal three") && tv.Runway == "13" && tv.navName == "six mile final" &&
                  clr >= 900 && ft <= tv.GateFt + 1100, $"Batumi IFR von {name}: Übergabe {ft:0} ft, Geländeabstand min {clr:0} ft, {first}");
            Check(Regex.IsMatch(lastMsg, @"^Enfield one one, \d+ miles from touchdown, turn (left|right) heading \w+ \w+ \w+, maintain \d+ feet until established on the localizer, cleared ILS approach runway one three, "), $"R220 Batumi ILS-Freigabe von {name}: {lastMsg}");
        }
        var (tc, fc, fcFt, cc) = Fly(bat.X + 20 * NM, bat.Z + 5 * NM, 8000, 180, "Batumi Approach, Enfield 1-1, inbound for landing", nw);
        // R212: IFR with tailwind on the instrument runway: ILS 13, pattern to the 31 (circling, no overhead, IFR stays), Tower: "circle to runway three one, … traffic, report base"
        var ci31 = tc.OnTranscript("Batumi Tower, Enfield 1-1, inbound", flyT, none, flySec + 2);
        Check(fc.Contains("circle to runway three one") && tc.Runway == "31" && tc.navName == "circle" && cc >= 900 && lastMsg.Contains("cleared ILS approach runway one three, circle to runway three one") &&
              ci31.Count == 1 && ci31[0].Role == "Tower" && Regex.IsMatch(ci31[0].Text, @"circle to runway three one, (left|right) traffic, report base") && tc.Phase == Phase.Pattern,
              $"R212 Batumi Circling: {fcFt:0} ft, {cc:0} ft, {fc} | {lastMsg} | {string.Join(" | ", ci31)}");
        Check(new Tower(bat, "x").AtisText("B", new Telemetry(bat.Elev + 2, 0, 0, 0, bat.X, bat.Z, 0, 0, 760), 15, true, 200, 3000).Contains("expect vectors for ILS approach runway one three, localizer one one zero decimal three"),
              "Batumi ATIS IFR -> 13");
        ForceIfr = false;
        var atV = new Tower(bat, "x").AtisText("B", new Telemetry(bat.Elev + 2, 0, 0, 0, bat.X, bat.Z, 0, 0, 760), 15, false, 0, 9999);
        Check(atV.Contains("Due to terrain, straight in and instrument approaches runway one three"), "Batumi ATIS Sicht: Approach fuehrt auf die 13: " + atV);
        // Review R212: visual, straight-in request, without procedure on the 13: no contradictory "cleared straight in approach runway 13, circle to runway 31", but downwind 31;
        // after taking over the pattern "circle" remains (report point base), go-around (PatternReset) clears it
        bat.Ils.Remove("13");
        var (tvs, fvs, _, _) = Fly(bat.X + 20 * NM, bat.Z + 5 * NM, 8000, 180, "Batumi Approach, Enfield 1-1, inbound for landing, request straight in", nw);
        var vfrHand = bat.Hand("31");
        var hoVfr = lastMsg;
        var cvs = tvs.OnTranscript("Batumi Tower, Enfield 1-1, inbound", flyT, none, flySec + 2);
        bool circKept = tvs.navName == "circle";
        tvs.GoAround("Enfield one one", "go around");
        Check(!fvs.Contains("circle") && !vecSaid.Concat(new[] { hoVfr }).Any(s => s.Contains("circle") || s.Contains("cleared straight in approach")) && hoVfr.Contains($"expect {vfrHand} downwind runway three one") &&
              cvs.Count == 1 && cvs[0].Text.Contains($"join {vfrHand} downwind runway three one") && cvs[0].Text.EndsWith("report base.") && circKept && tvs.navName == "",
              $"Review R212 Sicht ohne Verfahren: {fvs} | {hoVfr} | {string.Join(" | ", cvs)} | navName {tvs.navName}");
        bat.Ils["13"] = (110.3, "ILU");
        // P3-AP7: Visual approach from 20 NM: radar vectoring "for visual approach", clearance "cleared visual approach", never ILS/localizer
        var (tva, fva, _, _) = Fly(bat.X + 20 * NM, bat.Z + 5 * NM, 8000, 180, "Batumi Approach, Enfield 1-1, request visual approach", sight: "Batumi Approach, Enfield 1-1, field in sight");
        Check(fva.Contains("vectors for visual approach runway one three") && !fva.Contains("ILS") && tva.navName == "six mile final" && lastMsg.Contains("cleared visual approach runway one three") &&
              vecSaid.Any(s => s.Contains("report field in sight") && !s.Contains("cleared")) && !vecSaid.Concat(new[] { fva }).Any(s => s.Contains("ILS") || s.Contains("localizer")), $"Visual Approach aus 20 NM: {fva} | {string.Join(" | ", vecSaid)}");
        // R115: without report up to 4 NM fallback: clearance without Visual (Batumi has no ILS clearance in the visual case, so straight-in/instrument approach)
        var (tvb, _, _, _) = Fly(bat.X + 20 * NM, bat.Z + 5 * NM, 8000, 180, "Batumi Approach, Enfield 1-1, request visual approach");
        Check(vecSaid.Any(s => s.Contains("report field in sight")) && lastMsg.Contains("cleared") && !lastMsg.Contains("visual") && tvb.Phase == Phase.Entering && !tvb.wantVisual, $"Visual Approach aus 20 NM ohne Meldung: Rückfall: {lastMsg}");
        // R274: visual without procedure (no ILS): Approach says "proceed straight in runway 13", no "approach clearance" (FAA JO 7110.65 4-8-1, 7-4-2)
        bat.Ils.Remove("13");
        var (tsi, fsi, _, _) = Fly(bat.X + 20 * NM, bat.Z + 5 * NM, 8000, 180, "Batumi Approach, Enfield 1-1, inbound for landing, request straight in");
        Check(lastMsg.Contains("on course, proceed straight in runway one three") && !lastMsg.Contains("cleared") && tsi.Phase == Phase.Entering, $"R274 Straight-in ohne Verfahren: {fsi} | {lastMsg}");
        bat.Ils["13"] = (110.3, "ILU");

        // As in the game (07.10., 42 NM northeast, 4500 ft, 300 kt): pilot flies only the announced headings (2.1°/s), no own correction.
        // Expected (15 kt from NW, 13 too much tailwind): cleanly onto the 13 centerline (overhead for 31), no 90°/180° reversals, afterwards no "check runway" until the overhead.
        // late: reacts only after so many seconds (flies through the centerline in the meantime).
        void Strict(string name, int late)
        {
            double x = bat.X + 42 * NM * Math.Cos(Math.PI / 4), z = bat.Z + 42 * NM * Math.Sin(Math.PI / 4), h = 216, alt = 4500 * Ft, bTarget = h, bTurn = 0, pend = h;
            var tv = new Tower(bat, "Enfield 1-1") { FieldWind = nw };
            Telemetry T(double vs = 0) => new(alt, alt - bat.Elev, 154, h * Math.PI / 180, x, z, 0, 0, 760, vs);
            tv.Tick(T(), none, 0);
            var bLog = new List<string> { tv.OnTranscript("Batumi Approach, Enfield 1-1, inbound for landing", T(), none, 1)[0].Text };
            pend = bTarget = tv.vecHdg;
            string bWrong = "", bJoin = "", neg = "";
            int bSec = 2, pendAt = 0, vecCalls = 0;
            for (; bSec < 3000 && bJoin == ""; bSec++)
            {
                if (bSec >= pendAt) bTarget = pend;
                h = (h + Math.Clamp(((bTarget - h) % 360 + 540) % 360 - 180, -2.1, 2.1) + 360) % 360;
                x += 154 * Math.Cos(h * Math.PI / 180); z += 154 * Math.Sin(h * Math.PI / 180);
                double a0 = alt, want = tv.Phase == Phase.Inbound ? tv.vecFt * Ft : bat.PatternFt * Ft;
                alt = want > alt ? Math.Min(alt + 7.6, want) : Math.Max(alt - 7.6, want);   // 1500 ft/min
                var m = tv.Tick(T(alt - a0), none, bSec);
                if (tv.Phase == Phase.Entering && tv.navName == "overhead" && tv.nav is { } oh && Dist(x, z, oh.X, oh.Z) < NM)   // Reporting obligation: calls "overhead" itself
                    m = tv.OnTranscript("Batumi Tower, Enfield 1-1, overhead", T(), none, bSec).Concat(m).ToList();
                if (late == 2 && neg == "" && tv.Phase == Phase.Entering && tv.AL(x, z, tv.vrw).A < 4 * NM)   // reports "final" on the 13 line
                    neg = tv.OnTranscript("Batumi Tower, Enfield 1-1, final, full stop", T(), none, bSec)[0].Text;
                if (tv.Phase != Phase.Inbound && tv.nav is { } ov) pend = Bearing(x, z, ov.X, ov.Z);   // Tower: to the overhead
                if (m.Count == 0) continue;
                var (pa, pl) = tv.AL(x, z, tv.vrw);
                bLog.Add($"{bSec}s a={pa / NM:0.0} l={pl / NM:0.0} h={h:0}: {m[0].Text}");
                if (m[0].Text.Contains("check runway")) bWrong = m[0].Text;
                if (m[0].Text.Contains($"join {bat.Hand("31")} downwind runway three one")) bJoin = m[0].Text;
                if (tv.Phase == Phase.Inbound) { vecCalls++; bTurn = Math.Max(bTurn, HdgDiff(tv.vecHdg, h)); pend = tv.vecHdg; pendAt = bSec + late; }
            }
            if (Environment.GetEnvironmentVariable("VECDBG") == "BatumiSpiel") bLog.ForEach(l => Console.WriteLine("  " + l));
            Check(bJoin != "" && bWrong == "" && bTurn <= 100 && vecCalls <= 12 && bLog[0].Contains("overhead join runway three one via the extended centerline runway one three")
                  && bLog.Any(l => l.Contains("intercept the extended centerline runway one three for overhead join runway three one"))
                  && (late != 2 || neg.Contains("negative, runway three one in use, continue overhead")),
                  $"Batumi {name}: {bSec} s, {vecCalls} Radar-Ansagen, größte Kurve {bTurn:0}°, {(bWrong != "" ? bWrong : bJoin)} | final unterwegs: {neg}");
        }
        Strict("wie im Spiel", 2);
        Strict("Pilot spät", 15);

        // User report (game): radar vectoring to final, pilot flies only the headings (2.1°/s), reacts late: at 6-9 NM no alternating left/right headings
        foreach (var (za, zl, zh, zlate) in new[] { (14.0, 5.0, 8.0, 3), (12.0, 3.0, 6.0, 15), (11.0, -2.5, 5.0, 15), (10.0, 2.0, 6.0, 25), (9.0, 4.0, 6.0, 3), (9.5, 1.5, 4.0, 20), (9.0, -3.0, 6.0, 10), (10.0, 4.0, 7.0, 20) })
        {
            ForceIfr = true;
            var kz = Airfield.Kutaisi();
            var tz = new Tower(kz, "Enfield 1-1");
            var (zx, zz) = tz.Pt("25", za * NM, zl * NM);
            double zhd = Bearing(zx, zz, tz.Pt("25", zh * NM, 0).X, tz.Pt("25", zh * NM, 0).Z), zAlt = 4000 * Ft, zTarget = zhd, zPend = zhd;
            Telemetry Z(double vs = 0) => new(zAlt, zAlt - kz.Elev, 128, zhd * Math.PI / 180, zx, zz, 0, 0, 760, vs);
            tz.Tick(Z(), none, 0);
            var zLog = new List<string> { tz.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", Z(), none, 1)[0].Text };
            zPend = zTarget = tz.vecHdg;
            int zSec = 2, zPendAt = 0, bad = 0;
            bool icpt = false;
            for (; zSec < 2400 && tz.Phase == Phase.Inbound; zSec++)
            {
                if (zSec >= zPendAt) zTarget = zPend;
                zhd = (zhd + Math.Clamp(((zTarget - zhd) % 360 + 540) % 360 - 180, -2.1, 2.1) + 360) % 360;
                zx += 128 * Math.Cos(zhd * Math.PI / 180); zz += 128 * Math.Sin(zhd * Math.PI / 180);
                double a0 = zAlt, want = tz.vecFt * Ft;
                zAlt = want > zAlt ? Math.Min(zAlt + 7.6, want) : Math.Max(zAlt - 7.6, want);
                var m = tz.Tick(Z(zAlt - a0), none, zSec);
                if (m.Count == 0 || tz.Phase != Phase.Inbound) continue;
                var (pa, pl) = tz.AL(zx, zz, tz.vrw);
                zLog.Add($"{zSec}s a={pa / NM:0.0} l={pl / NM:0.0} h={zhd:0}: {m[0].Text}");
                // after "intercept": no climb, no turn over 60° (target crosswise/behind him), except with a new plan (30° no longer works)
                if (icpt && (m[0].Text.Contains("climb") || !m[0].Text.Contains("downwind") && !m[0].Text.Contains("vectors for") && HdgDiff(tz.vecHdg, zhd) > 60)) bad++;
                icpt |= m[0].Text.Contains("intercept");
                zPend = tz.vecHdg; zPendAt = zSec + zlate;
            }
            ForceIfr = false;
            if (Environment.GetEnvironmentVariable("VECDBG") == "Zick") zLog.ForEach(l => Console.WriteLine("  " + l));
            Check(tz.Phase == Phase.Entering && bad == 0, $"Zickzack Kutaisi 25 ab {za} NM/{zl} NM, {zlate} s spät: {bad} falsche Ansagen, {tz.Phase} | {string.Join(" | ", zLog.Skip(1))}");
        }

        // R42: IFR gate 10/8 NM (last vector a good 2 NM before the Approach gate), 6/4 NM only with terrain or already close; Kutaisi both directions from 8 directions, 30 NM
        {
            ForceIfr = true;
            var kg = Airfield.Kutaisi();
            var gs = new List<string>();
            foreach (var grw in new[] { "07", "25" })
                for (int d = 0; d < 360; d += 45)
                {
                    var ge = kg.End(grw);
                    var tr42 = new Tower(kg, "Enfield 1-1") { FieldWind = (-ge.Dx * 5, -ge.Dz * 5) };
                    double gx = kg.X + 30 * NM * Math.Cos(d * Math.PI / 180), gz = kg.Z + 30 * NM * Math.Sin(d * Math.PI / 180);
                    var gt = new Telemetry(8000 * Ft, 8000 * Ft - kg.Elev, 150, Bearing(gx, gz, kg.X, kg.Z) * Math.PI / 180, gx, gz, 0, 0, 760);
                    tr42.Tick(gt, none, 0);
                    tr42.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", gt, none, 1);
                    if (tr42.gate < 8 * NM) gs.Add($"{grw}/{d}°: {tr42.gate / NM:0} NM");
                }
            ForceIfr = false;
            Check(gs.Count == 0, "R42 Gate 10/8 NM aus 30 NM: " + (gs.Count == 0 ? "alle" : string.Join(", ", gs)));
        }

        // R46: Separation on final: 3 NM at the threshold (150 kt), behind heavy 5 NM; too close -> "reduce speed ... for spacing" (15 % slower)
        {
            Check(Behind(16 * NM, 10 * NM, false) && !Behind(14 * NM, 10 * NM, false) && !Behind(16 * NM, 10 * NM, true) && Behind(19 * NM, 10 * NM, true), "R46 Behind 3/5 NM");
            ForceIfr = true;
            var ks = Airfield.Kutaisi();
            var ts = new Tower(ks, "Enfield 1-2") { AcType = "FA-18C_hornet" };
            var (sx, sz) = ts.Pt("25", 16 * NM, 3 * NM);
            Telemetry Sp(double hdg, double ft, double kt) => new(ft * Ft, ft * Ft - ks.Elev, kt * Kt, hdg * Math.PI / 180, sx, sz, 0, 0, 760);
            ts.Tick(Sp(250, 4000, 250), none, 0);
            ts.OnTranscript("Kutaisi Approach, Enfield 1-2, inbound for landing", Sp(250, 4000, 250), none, 1);
            var sp = ts.Path(Sp(ts.vecHdg, ts.vecFt, 250)).R;
            int free = ts.VecKt(Sp(ts.vecHdg, ts.vecFt, 250));
            ts.AheadR = sp - 3 * NM;   // Preceding traffic only 3 NM of track ahead (too close at 250 kt)
            var sm = ts.Tick(Sp(ts.vecHdg, ts.vecFt, free), none, 40);
            ForceIfr = false;
            Check(free > 0 && ts.VecKt(Sp(ts.vecHdg, ts.vecFt, free)) == (int)Math.Round(free * 0.85 / 10) * 10 && sm.Any(m => m.Text.Contains($"reduce speed {(int)Math.Round(free * 0.85 / 10) * 10} knots for spacing")),
                  $"R46 zu dicht hinter dem Vordermann: {free} kt -> " + string.Join(" | ", sm.Select(m => m.Text)));
            // Preceding traffic just handed over (Established, gate 10 NM): 11 NM out turning in, 3000 ft -> remaining distance known (otherwise no separation up to 10 NM on the course)
            var te = new Tower(ks, "Enfield 1-1") { Phase = Phase.Entering, navName = "six mile final" };
            var ep = te.Pt(te.Runway, 11 * NM, 0.5 * NM);
            double efp = te.FinalPath(new(3000 * Ft, 3000 * Ft - ks.Elev, 180 * Kt, (te.LandHdg(te.Runway) + 25) * Math.PI / 180, ep.X, ep.Z, 0, 0, 760));
            Check(efp > 11 * NM && efp < 12 * NM, $"R46 Restweg Vordermann nach der Übergabe: {efp / NM:0.0} NM");
            // mptest Golf (Ras Al Khaimah IFR, Bandar-e-Jask VFR): another's holding 4 NM ahead on the path at my altitude (just stepped down there) -> 1000 ft above beforehand
            ForceIfr = true;
            var th = new Tower(ks, "Enfield 1-3") { AcType = "FA-18C_hornet" };
            th.Tick(Sp(250, 2000, 250), none, 0);
            th.OnTranscript("Kutaisi Approach, Enfield 1-3, inbound for landing", Sp(250, 2000, 250), none, 1);
            var hm = Sp(th.vecHdg, 2000, 250);
            th.vecFt = 2000;
            var hp = th.Path(hm).Pts[1];
            double hb = Bearing(hm.X, hm.Z, hp.X, hp.Z) * Math.PI / 180;
            var hTr = new[] { new Traffic(81, "FA-18C_hornet", hm.X + 4 * NM * Math.Cos(hb), hm.Z + 4 * NM * Math.Sin(hb), 2000 * Ft, 0, 115, "U9", "hold:Kutaisi", true, 2) };
            var hs = th.Tick(hm, hTr, 40);
            ForceIfr = false;
            Check(th.vecFt == 3000 && hs.Any(m => m.Text.Contains("climb and maintain 3000 feet")), $"Warteschleife am Weg auf gleicher Höhe: {th.vecFt} ft | " + string.Join(" | ", hs.Select(m => m.Text)));
            // R270 (mptest Kobuleti VFR "maintain 3100" -> "climb and maintain 5000" through the holding at 4000): holding 900 ft above, terrain (MVA 2000) allows
            // no passing below -> hold altitude, do not climb through its altitude; Sukhumi IFR: holder is descending right through my altitude to its new level (assigned 1000 ft
            // lower) -> reference is the assigned altitude, no "climb"
            ForceIfr = true;
            var terr300 = new float[61, 61];
            for (int i = 0; i < 61; i++) for (int j = 0; j < 61; j++) terr300[i, j] = 335;   // 1099 ft -> MVA 2100 (like Kobuleti 3100)
            ks.SetTerrain(terr300, ks.X - 30 * NM, ks.Z - 30 * NM, NM);
            foreach (var (myFt, hFt, hAsg, t0, what) in new[] { (2100.0, 3000.0, 3000.0, 100.0, "900 ft darüber, MVA 2100"), (2100.0, 2100.0, 1000.0, 200.0, "sinkt durch meine Höhe, zugewiesen 1000") })
            {
                th.vecFt = myFt; th.lastVecSaid = 0;
                var hm2 = hm with { AltMsl = myFt * Ft, Agl = myFt * Ft - ks.Elev };
                var hTr2 = hTr[0] with { X = hm.X + 4 * NM * Math.Cos(hb + Math.PI / 2), Z = hm.Z + 4 * NM * Math.Sin(hb + Math.PI / 2), Speed = 0, AltMsl = hFt * Ft, AsgFt = hAsg, Vs = hFt > hAsg ? -10 : 0 };   // 4 NM beside the path, circling
                var hs2 = Enumerable.Range(0, 60).SelectMany(k => th.Tick(hm2, new[] { hTr2 }, t0 + k)).ToList();   // one minute: traffic advisory if applicable, then level
                Check(th.vecFt <= myFt && !hs2.Any(m => m.Text.Contains("climb")), $"R270 Warteschleife am Weg {what}: kein Steigen ({th.vecFt} ft) | " + string.Join(" | ", hs2.Select(m => m.Text)));
            }
            ks.SetTerrain(null!, 0, 0, 0);
            ForceIfr = false;
        }

        // R302 (log 22:44, Kutaisi 55 NM, assigned 4000, climbs unasked to 4800+): not "expedite descent", but "verify altitude, maintain …",
        // on the second time "climb not authorized, descend and maintain …"; "expedite" only with traffic at his altitude
        {
            ForceIfr = true;
            var k302 = Airfield.Kutaisi();
            var e302 = k302.End("07");
            var fw302 = (X: -e302.Dx * 5, Z: -e302.Dz * 5);
            var p302 = e302.At(55 * NM, 8 * NM);
            List<string> Run302(Traffic[] tr)
            {
                var t302 = new Tower(k302, "Enfield 1-1") { FieldWind = fw302 };
                Telemetry T302(double f, double vs = 0) => new(f * Ft, f * Ft - k302.Elev, 300 * Kt, t302.vecHdg * Math.PI / 180, p302.X, p302.Z, fw302.X, fw302.Z, 1013, vs);
                t302.Tick(T302(4000), none, 0);
                t302.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", T302(4000), none, 1);
                t302.altFree = false;   // R394: assigned altitude (not at his discretion)
                for (int s = 2; s < 40; s++) t302.Tick(T302(t302.vecFt), none, s);   // at the assigned altitude
                double hi = t302.vecFt + 1200;
                var said = new List<string>();
                for (int s = 40; s < 140; s++) said.AddRange(t302.Tick(T302(hi), tr.Select(a => a with { AltMsl = hi * Ft }).ToArray(), s).Select(m => m.Text));   // unasked 1200 ft higher, stays there
                return said;
            }
            var hs302 = Run302(Array.Empty<Traffic>());
            int v302 = hs302.FindIndex(m => m.Contains("verify altitude, maintain")), n302 = hs302.FindIndex(m => m.Contains("climb not authorized, descend and maintain"));
            Check(v302 >= 0 && n302 > v302 && !hs302.Any(m => m.Contains("expedite")), "R302 ungefragt gestiegen: verify altitude, dann climb not authorized | " + string.Join(" | ", hs302));
            var tr302 = new[] { new Traffic(91, "FA-18C_hornet", p302.X + 2.5 * NM, p302.Z, 0, 0, 0, "U9", "", true, 2) };   // is 2.5 NM beside at his new altitude
            var hx302 = Run302(tr302);
            Check(hx302.Any(m => m.Contains("expedite descent, maintain")), "R302 Verkehr auf seiner Höhe: expedite descent | " + string.Join(" | ", hx302));
            ForceIfr = false;
        }

        // R303 (game Senaki): under radar vectoring at ~14000, assigned 11500, radio wheel "request higher" -> "descend and maintain 12500", again -> "13500",
        // 1 s later "descend and maintain 9500" by itself. Now: far out his altitude approved (never "descend", no 1000-ft ladder), it stays;
        // close to the gate "unable higher, expect lower shortly"; "request descent at pilot's discretion" -> "descend at pilot's discretion, maintain …", no nagging
        {
            ForceIfr = true;
            var k3 = Airfield.Kutaisi();
            var e3 = k3.End("07");
            var fw303 = (X: -e3.Dx * 5, Z: -e3.Dz * 5);
            (Tower T, Func<double, Telemetry> At) Start303(double a, double ft)
            {
                var tw = new Tower(k3, "Dagger 1-1") { FieldWind = fw303 };
                var p = e3.At(a, 0);
                Telemetry Tl3(double f) => new(f * Ft, f * Ft - k3.Elev, 300 * Kt, e3.Hdg * Math.PI / 180, p.X, p.Z, fw303.X, fw303.Z, 1013);
                tw.Tick(Tl3(ft), none, 0);
                tw.OnTranscript("Kutaisi Approach, Dagger 1-1, inbound for landing", Tl3(ft), none, 1);
                return (tw, Tl3);
            }
            var (tf3, tlf3) = Start303(75 * NM, 14000);
            tf3.vecFt = 11500;   // stepped down before, pilot is above
            var hf31 = tf3.OnTranscript("Kutaisi Approach, Dagger 1-1, request higher", tlf3(14000), none, 10)[0].Text;
            var hTick3 = Enumerable.Range(11, 120).SelectMany(s => tf3.Tick(tlf3(14000), none, s)).Select(m => m.Text).ToList();
            var hf32 = tf3.OnTranscript("Kutaisi Approach, Dagger 1-1, request higher", tlf3(14000), none, 140)[0].Text;
            double hf32Ft = tf3.vecFt;
            var hf33 = tf3.OnTranscript("Kutaisi Approach, Dagger 1-1, request higher", tlf3(hf32Ft), none, 150)[0].Text;
            Check(hf31.Contains("maintain 14000 feet") && !hf31.Contains("descend") && !hTick3.Any(m => m.Contains("descend")) && !hf32.Contains("descend")
                  && (hf32Ft >= 15000 || hf32.Contains("unable higher")) && hf33.Contains("unable higher") && !hf33.Contains("descend"),
                  $"R303 request higher weit draußen (75 NM): genehmigt, bleibt, keine Leiter | {hf31} | {string.Join(" / ", hTick3)} | {hf32} | {hf33}");
            var p60 = e3.At(45 * NM, 0);   // later, when the profile needs the descent: step at own discretion
            var hLate = Enumerable.Range(151, 360).SelectMany(s => { var q = e3.At(75 * NM - (s - 150) * 300 * Kt, 0); return tf3.Tick(new(hf32Ft * Ft, hf32Ft * Ft - k3.Elev, 300 * Kt, e3.Hdg * Math.PI / 180, q.X, q.Z, fw303.X, fw303.Z, 1013), none, s); }).Select(m => m.Text).ToList();   // flies on 30 NM, stays up
            Check(hLate.Any(m => m.Contains("descend at pilot's discretion, maintain")) && !hLate.Any(m => m.Contains("descend and maintain")), $"R303 nach request higher: Sinkflug später nach eigenem Ermessen ({tf3.Phase} {tf3.vecFt} {tf3.vec?.Count} {tf3.keepHighR / NM:0} {tf3.Path(new(hf32Ft * Ft, 0, 300 * Kt, 0, p60.X, p60.Z, 0, 0, 1013)).R / NM:0}) | " + string.Join(" / ", hLate));
            var (tc3, tlc3) = Start303(30 * NM, 6000);
            var hc3 = tc3.OnTranscript("Kutaisi Approach, Dagger 1-1, request higher", tlc3(6000), none, 10)[0].Text;
            Check(hc3.Contains($"unable higher, expect lower shortly, maintain {tc3.vecFt:0} feet") && !hc3.Contains("descend"), "R303 request higher 20 NM vor dem Gate: unable | " + hc3);
            var (tp3, tlp3) = Start303(75 * NM, 20000);
            var pd31 = tp3.OnTranscript("Kutaisi Approach, Dagger 1-1, flight level 200, request descent at pilot's discretion", tlp3(20000), none, 10)[0].Text;
            var pdTick3 = Enumerable.Range(11, 150).SelectMany(s => tp3.Tick(tlp3(20000), none, s)).Select(m => m.Text).ToList();   // not descending yet
            Check(pd31.Contains($"descend at pilot's discretion, maintain {tp3.GateFt + 1000:0} feet") && !pdTick3.Any(m => m.Contains("descend and maintain") || m.Contains("expedite") || m.Contains("verify")),
                  $"R303 descent at pilot's discretion: {pd31} | " + string.Join(" / ", pdTick3));
            // R394 (game 09.10., Kutaisi 54 NM, climbs unasked 4500 -> 9000): VFR altitude at his discretion (no call), descent with the profile; IFR (instrument approach) assigned altitude "maintain 4500 feet, expect lower in …", climbing is a deviation
            (string First, List<string> Ticks, int Qd) R394(bool ifr)
            {
                ForceIfr = ifr;
                var tq = new Tower(k3, "Dagger 1-1") { FieldWind = fw303 };
                Telemetry Tq(double a, double f) { var q = e3.At(a, 0); return new(f * Ft, f * Ft - k3.Elev, 300 * Kt, e3.Hdg * Math.PI / 180, q.X, q.Z, fw303.X, fw303.Z, 1013); }
                tq.Tick(Tq(54 * NM, 4500), none, 0);
                var first = string.Join(" / ", tq.OnTranscript("Kutaisi Approach, Dagger 1-1, inbound for landing", Tq(54 * NM, 4500), none, 1).Select(m => m.Text));
                var ticks = Enumerable.Range(2, 600).SelectMany(s => { double a = 54 * NM - (s - 1) * 300 * Kt; return tq.Tick(Tq(a, Math.Min(4500 + (s - 1) * 50, 9000)), none, s).Select(m => $"{a / NM:0}:{m.Text}"); }).ToList();
                return (first, ticks, ticks.FindIndex(m => m.Contains("descend")));
            }
            var (q4v, q4vt, qdv) = R394(false);
            Check(q4v.Contains("altitude at your discretion, expect lower in") && !q4v.Contains("maintain") && qdv >= 0 && !q4vt.Take(qdv).Any(m => Regex.IsMatch(m, "maintain|altitude|verify|climb")),
                  $"R394 VFR Höhe nach eigenem Ermessen bis zum Profil: {q4v} | " + string.Join(" / ", q4vt.Take(qdv + 1)));
            var (q4i, q4it, _) = R394(true);
            Check(q4i.Contains("maintain 4500 feet, expect lower in") && !q4i.Contains("discretion") && q4it.Take(20).Any(m => m.Contains("4500")),
                  $"R394 IFR zugewiesene Höhe, expect lower: {q4i} | " + string.Join(" / ", q4it.Take(5)));
            ForceIfr = false;
        }

        // R305 (game 08.10., Kutaisi): from far out on the centerline – "descend and maintain" gate altitude came at ~25 NM (profile 12 NM ahead), immediately afterwards
        // "cleared … approach, contact Tower". Now: gate altitude only after the profile at the point, handoff only up to 2 NM before the gate
        {
            ForceIfr = true;
            var k5 = Airfield.Kutaisi();
            var e5 = k5.End("07");
            var fw305 = (X: -e5.Dx * 5, Z: -e5.Dz * 5);
            var t5 = new Tower(k5, "Enfield 1-1") { FieldWind = fw305 };
            double a5 = 30 * NM, ft5 = 6000;
            Telemetry T5(double vs = 0) { var p = t5.Pt("07", a5, 0); return new(ft5 * Ft, ft5 * Ft - k5.Elev, 250 * Kt, e5.Hdg * Math.PI / 180, p.X, p.Z, fw305.X, fw305.Z, 1013, vs); }
            t5.Tick(T5(), none, 0);
            var m5 = t5.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", T5(), none, 1).Select(m => (A: a5 / NM, m.Text)).ToList();
            for (int s = 2; s < 900 && t5.Phase == Phase.Inbound; s++)
            {
                double f0 = ft5; a5 -= 250 * Kt; ft5 = Math.Max(t5.vecFt, ft5 - 25);   // flies the centerline, descends 1500 ft/min to the assigned altitude
                m5.AddRange(t5.Tick(T5((ft5 - f0) * Ft), none, s).Select(m => (A: a5 / NM, m.Text)));
            }
            ForceIfr = false;
            double g5 = t5.gate / NM, hoA5 = m5.FirstOrDefault(m => m.Text.Contains("contact Kutaisi Tower")).A, stepA5 = m5.FirstOrDefault(m => m.Text.Contains($"descend and maintain {t5.GateFt:0} feet")).A;
            Check(t5.Phase == Phase.Entering && hoA5 > g5 - 0.5 && hoA5 <= g5 + 2.1 && stepA5 <= g5 + 6,
                  $"R305 auf der Mittellinie: Gate-Höhe bei {stepA5:0.0} NM, Übergabe bei {hoA5:0.0} NM (Gate {g5:0}) | " + string.Join(" | ", m5.Select(m => $"{m.A:0.0} {m.Text}")));
        }

        // R314 (game Senaki 27): handoff "…, contact Kolkhi Tower, report four mile final", then "final, gear down, full stop" at 9 NM on the centerline ->
        // "negative, I don't have you on final". Now: Approach without reporting point, initial call "9 mile final, full stop" (ENTER suggestion) -> landing clearance with station name; 3 NM beside still negative
        {
            ForceIfr = true;
            var k14 = Airfield.Kutaisi();
            var e14 = k14.End("07");
            var fw314 = (X: -e14.Dx * 5, Z: -e14.Dz * 5);
            Telemetry T14(double a, double l, double ft) { var p = e14.At(a, l); return new(ft * Ft, ft * Ft - k14.Elev, 160 * Kt, e14.Hdg * Math.PI / 180, p.X, p.Z, fw314.X, fw314.Z, 1013); }
            var t14 = new Tower(k14, "Dagger 1-1") { FieldWind = fw314 };
            t14.Tick(T14(14, 0, 3000), none, 0);
            t14.OnTranscript("Kutaisi Approach, Dagger 1-1, inbound for landing", T14(14 * NM, 0, 3000), none, 1);
            var ho14 = new List<string>();
            int s14 = 2;
            for (; s14 < 300 && t14.Phase == Phase.Inbound; s14++) ho14.AddRange(t14.Tick(T14((14 - s14 * 0.044) * NM, 0, Math.Min(3000, t14.vecFt)), none, s14).Select(m => m.Text));
            t14.Tick(T14(9 * NM, 0, 2700), none, s14 + 1);
            var sug14 = t14.Suggest(T14(9 * NM, 0, 2700));
            var fin14 = t14.OnTranscript("Kutaisi Tower, Dagger 1-1, " + (sug14?.Text ?? "-"), T14(9 * NM, 0, 2700), none, s14 + 2);
            var off14 = new Tower(k14, "Dagger 1-2") { FieldWind = fw314, Phase = Phase.Entering, navName = "six mile final" };
            var neg14 = off14.OnTranscript("Kutaisi Tower, Dagger 1-2, 9 mile final, full stop", T14(9 * NM, 3 * NM, 2700), none, 300);
            ForceIfr = false;
            var hoMsg = ho14.LastOrDefault(m => m.Contains("contact Kutaisi Tower")) ?? "-";
            Check(!hoMsg.Contains("report four mile final") && sug14?.Text == "9 mile final, gear down, full stop" && fin14.Count > 0 && fin14[0].Text.StartsWith("Dagger one one, Kutaisi Tower") && fin14[0].Text.Contains("cleared to land")
                  && neg14.Count > 0 && neg14[0].Text.Contains("negative, I don't have you on final"),
                  $"R314 langer Endanflug: {hoMsg} | {sug14?.Text} -> {fin14.FirstOrDefault()?.Text} | 3 NM daneben: {neg14.FirstOrDefault()?.Text}");
            // R332: preceding traffic on long final (9 NM, 800 m) counts in the sequence (before only up to 8 NM/900 m: "cleared to land")
            var lf332 = new Tower(k14, "Dagger 1-3") { FieldWind = fw314, Phase = Phase.Entering, navName = "six mile final" };
            var hp332 = e14.At(9 * NM, 0);
            var r332 = lf332.OnTranscript("Kutaisi Tower, Dagger 1-3, 11 mile final, full stop", T14(11 * NM, 0, 3300), new[] { new Traffic(9, "FA-18C_hornet", hp332.X, hp332.Z, k14.Elev + 800, e14.Hdg * Math.PI / 180, 80) }, 400);
            Check(r332.Count > 0 && r332[0].Text.Contains("number 2, follow the Hornet on 9 mile final"), "R332 Vordermann im langen Endanflug: " + (r332.FirstOrDefault()?.Text ?? "-"));
            // R324: extended downwind over 5 NM from the airfield: base call comes (before silent or "leaving the control zone"); R330: on base too close behind traffic on final -> full circle
            {
                var e25 = K.End("25");
                (double X, double Z) Side(double a, double l) { var p = e25.At(a, l); return kTower.PatternOffset(p.X, p.Z, "25") > 0 ? p : e25.At(a, -l); }
                var tw324 = new Tower("Enfield 1-1");
                var d324 = Side(4.5 * NM, 1.5 * NM);
                tw324.Tick(At(d324.X, d324.Z, 600, (e25.Hdg + 180) % 360, 100), none, 9); tw324.Phase = Phase.Pattern; tw324.extended = true;
                var r324 = tw324.Tick(At(d324.X, d324.Z, 600, (e25.Hdg + 180) % 360, 100), none, 10);
                var tw330 = new Tower("Enfield 1-1");
                var b330 = Side(1.5 * NM, 1.2 * NM); var f330 = e25.At(1 * NM, 0);
                var tb330 = new[] { (e25.Hdg + 90) % 360, (e25.Hdg + 270) % 360 }.Select(h => At(b330.X, b330.Z, 300, h, 75)).First(tb => tw330.OnBase(tb, "25"));
                var tr330 = new[] { new Traffic(5, "F-16C_50", f330.X, f330.Z, FieldElev + 100, e25.Hdg * Math.PI / 180, 70) };
                tw330.Tick(tb330, none, 9); tw330.Phase = Phase.Pattern;
                var r330 = tw330.Tick(tb330, tr330, 10);
                Check(r324.Any(m => m.Contains("turn base")) && r330.Any(m => m.Contains("three-sixty")),
                      "R324 Base-Ruf im verlängerten Gegenanflug / R330 Vollkreis: " + string.Join(" | ", r324.Concat(r330).Select(m => m.Text)) + $" [{tw324.Phase}/{tw330.Phase} side {kTower.PatternOffset(d324.X, d324.Z, "25"):0}]");
            }
        }

        // R351 (Anflugtest Kutaisi 25 IFR 045°): replanning at 17 NM, then within 10 s no next vector ("fly heading two five zero …" -> 1 s later "turn right heading three four zero"):
        // first waypoint already within the turn lead is skipped, the next one only once the vector is flown (FAA JO 7110.65 5-6-1)
        {
            ForceIfr = true;
            var k51 = Airfield.Kutaisi();
            var t51 = new Tower(k51, "Enfield 1-1");
            var p51 = t51.Pt("25", 15.7 * NM, 5.3 * NM);
            double x51 = p51.X, z51 = p51.Z, h51 = t51.LandHdg("25") + 2;
            Telemetry T51() => new(4700 * Ft, 4700 * Ft - k51.Elev, 250 * Kt, h51 * Math.PI / 180, x51, z51, 0, 0, 1013);
            t51.Tick(T51(), none, 0);
            t51.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", T51(), none, 1);
            var said51 = new List<string> { t51.StartVectors(T51(), true, 2) };
            t51.lastVecSaid = 2;
            for (int s = 3; s <= 12; s++)
            {
                x51 += 250 * Kt * Math.Cos(h51 * Math.PI / 180); z51 += 250 * Kt * Math.Sin(h51 * Math.PI / 180);
                said51.AddRange(t51.Tick(T51(), none, s).Select(m => $"{s}s {m.Text}"));
            }
            ForceIfr = false;
            Check(said51.Count == 1, "R351 Neuplanung bei 17 NM: ein Ruf in 10 s | " + string.Join(" | ", said51));
        }
        // R306 (game 08.10.): oscillation when turning in 300 -> 270 "join" -> 285 -> 270. Intercept heading 30°, pilot captures the centerline himself (as after "intercept"):
        // afterwards no new heading (the intercept point slipped along close and became shallower), no "join" seconds later, handoff shortly before the gate
        {
            ForceIfr = true;
            var k6 = Airfield.Kutaisi();
            var e6 = k6.End("07");
            var fw306 = (X: -e6.Dx * 5, Z: -e6.Dz * 5);
            var t6 = new Tower(k6, "Enfield 1-1") { FieldWind = fw306 };
            var p6 = t6.Pt("07", 17 * NM, 2.5 * NM);
            double x6 = p6.X, z6 = p6.Z, h6 = 0, ft6 = 4000, v6 = 250 * Kt, land6 = e6.Hdg;
            Telemetry T6() => new(ft6 * Ft, ft6 * Ft - k6.Elev, v6, h6 * Math.PI / 180, x6, z6, fw306.X, fw306.Z, 1013);
            t6.vrw = "07";
            var ic6 = t6.Icpt(x6, z6);
            h6 = Bearing(x6, z6, ic6.X, ic6.Z);
            t6.Tick(T6(), none, 0);
            var said6 = t6.OnTranscript("Kutaisi Approach, Enfield 1-1, inbound for landing", T6(), none, 1).Select(m => m.Text).ToList();
            ft6 = t6.GateFt + 500;
            t6.vecFt = ft6;
            double tgt6 = t6.vecHdg;
            bool cap6 = false;
            for (int s = 2; s < 600 && t6.Phase == Phase.Inbound; s++)
            {
                var (a6, l6) = t6.AL(x6, z6, "07");
                double dh6 = HdgDiff(h6, land6), rr6 = v6 * v6 / (9.81 * Math.Tan(Math.PI / 6));
                if (!cap6 && a6 > 0 && Math.Abs(l6) < rr6 * (1 - Math.Cos(dh6 * Math.PI / 180)) + 100) cap6 = true;   // captures like SimPilot ("intercept …")
                if (cap6) { double hr6 = land6 * Math.PI / 180, left6 = (x6 - e6.ThrX) * Math.Sin(hr6) - (z6 - e6.ThrZ) * Math.Cos(hr6); tgt6 = land6 + Math.Clamp(left6 / NM * 60, -90, 90); }
                h6 = (h6 + Math.Clamp(((tgt6 - h6) % 360 + 540) % 360 - 180, -3, 3) + 360) % 360;
                x6 += v6 * Math.Cos(h6 * Math.PI / 180) + fw306.X; z6 += v6 * Math.Sin(h6 * Math.PI / 180) + fw306.Z;
                foreach (var m6 in t6.Tick(T6(), none, s)) { said6.Add($"{s}s a={t6.AL(x6, z6, "07").A / NM:0.0} {m6.Text}"); if (!cap6 && t6.Phase == Phase.Inbound) tgt6 = t6.vecHdg; }
            }
            ForceIfr = false;
            var hdgCalls6 = said6.Skip(1).Where(m => m.Contains("heading") && !m.Contains("contact")).ToList();
            Check(t6.Phase == Phase.Entering && hdgCalls6.Count == 0 && Regex.IsMatch(said6[0], "intercept|until established, cleared"),   // R353: turn-in vector with the clearance
                  $"R306 Eindrehen: ein Eindrehkurs, kein Nachsteuern/\"join\" ({hdgCalls6.Count}) | " + string.Join(" | ", said6));
            // R353: 30° turn-in vector, meets the centreline 2 NM or more before the gate, altitude fits: vector, "maintain … until established" and clearance in one call;
            // afterwards no descent or second clearance, the handover only says "contact Tower" (FAA JO 7110.65 5-9-1, 5-9-2)
            Check(Regex.IsMatch(said6[0], @"heading [a-z ]+, .*maintain \d+ feet until established, cleared P A R approach runway zero seven\.$") && !said6.Skip(1).Any(m => m.Contains("cleared") || m.Contains("descend"))
                  && said6.Skip(1).Any(m => m.Contains("contact Kutaisi Tower")), "R353 Eindrehvektor und Freigabe in einem Ruf | " + string.Join(" | ", said6));
        }

        // User report (game): "climb and maintain 2700 feet" at 8 NM. Intercept heading at 7.5 NM, already lower than instructed (2300 instead of 3000 ft),
        // hill 2.5 NM beside the final (MVA 2700): take over the altitude, no climb; below the minimum altitude on final still "climb"
        {
            ForceIfr = true;
            var kh = Airfield.Kutaisi();
            var tk = new Tower(kh, "Enfield 1-1") { Phase = Phase.Inbound };
            var hg = new float[61, 61];
            var hill = tk.Pt("25", 7 * NM, 2.5 * NM);
            hg[(int)Math.Floor((hill.X - kh.X) / NM) + 30, (int)Math.Floor((hill.Z - kh.Z) / NM) + 30] = (float)(1700 * Ft);
            kh.SetTerrain(hg, kh.X - 30 * NM, kh.Z - 30 * NM, NM);
            (tk.vrw, tk.vecFinal, tk.vec, tk.gate, tk.vecFt) = ("25", true, new(), 6 * NM, 3000);
            var (qx, qz) = tk.Pt("25", 7.5 * NM, -0.8 * NM);
            var g6 = tk.Pt("25", 6 * NM, 0);
            Telemetry H(double ft) => new(ft * Ft, ft * Ft - kh.Elev, 128, (tk.LandHdg("25") + 30) * Math.PI / 180, qx, qz, 0, 0, 760);
            var hs = tk.VecCall(H(2300), 10);
            Check(kh.MvaLeg(qx, qz, g6.X, g6.Z) >= 2700 && !hs.Contains("climb") && hs.Contains("maintain 2300 feet") && tk.vecFt == 2300, "Eindrehen tiefer als angewiesen, MVA 2700 daneben: kein climb: " + hs);
            tk.vecFt = 3000;
            hs = tk.VecCall(H(200), 50);
            Check(hs.Contains("climb and maintain") && tk.vecFt >= 300, "Eindrehen unter Gelände + 250 ft: climb: " + hs);
            // hill below him (1.8 NM beside the final, 2000 ft) counts: no "maintain 2100"
            var ku = Airfield.Kutaisi();
            var tu = new Tower(ku, "Enfield 1-1") { Phase = Phase.Inbound };
            var (ux, uz) = tu.Pt("25", 7.5 * NM, -1.8 * NM);
            var ug = new float[61, 61];
            ug[(int)Math.Floor((ux - ku.X) / NM) + 30, (int)Math.Floor((uz - ku.Z) / NM) + 30] = (float)(2000 * Ft);
            ku.SetTerrain(ug, ku.X - 30 * NM, ku.Z - 30 * NM, NM);
            (tu.vrw, tu.vecFinal, tu.vec, tu.gate, tu.vecFt) = ("25", true, new(), 6 * NM, 3000);
            hs = tu.VecCall(new(2100 * Ft, 2100 * Ft - ku.Elev, 128, (tu.LandHdg("25") + 30) * Math.PI / 180, ux, uz, 0, 0, 760), 10);
            Check(hs.Contains("climb and maintain") && tu.vecFt >= 2300, "Eindrehen über einem Hügel neben dem Endanflug: climb: " + hs);
            ForceIfr = false;
        }

        // --- Senaki 27 (without map): holding 8 NM southeast, as number 2 out only with spacing, radar vectoring without reversal turns
        // (pilot as in the game: 300 kt, 30° bank = 2.1°/s)
        var sen = Airfield.FromDcs("Senaki-Kolkhi", -281903.1, 648379.1, 13.2, new[] { ("27", 1.48864, -281782.5, 647279.5, 2212.0) });
        var tsn = new Tower(sen, "Enfield 1-1") { QueueAhead = 2, Spaced = false };
        double snX = sen.X - 5.7 * NM, snZ = sen.Z + 5.7 * NM, shd = 300, sAlt = 2000 * Ft;
        Telemetry S(double vs = 0) => new(sAlt, sAlt - sen.Elev, 154, shd * Math.PI / 180, snX, snZ, 0, 0, 760, vs);
        tsn.Tick(S(), none, 0);
        r = tsn.OnTranscript("Senaki Approach, Enfield 1-1, inbound for landing", S(), none, 1);
        Check(r[0].Contains("number 3") && tsn.HoldInfo != null, "Senaki Holding: " + r[0]);
        tsn.QueueAhead = 1; tsn.Spaced = false;
        Check(!tsn.Tick(S(), none, 2).Any(m => m.Text.Contains("leave the hold")), "Nummer 2, Nummer 1 noch weit draußen: bleibt im Holding");
        tsn.Spaced = true;
        double sTarget = shd, maxTurn = 0, lastToIni = double.MaxValue, grow = 0;
        int sSec = 3, sCalls = 0;
        bool orbit = true;   // circles left (230 kt, 2.75°/s) until "leave the hold"
        for (; sSec < 2400 && tsn.Phase == Phase.Inbound; sSec++)
        {
            if (tsn.vec is { Count: 0 } && tsn.AL(snX, snZ, tsn.vrw) is var (pa, pl) && Math.Abs(pl) < NM) { var q = tsn.Pt(tsn.vrw, pa - 1.5 * NM, 0); sTarget = Bearing(snX, snZ, q.X, q.Z); }
            shd = (shd + (orbit ? -2.75 : Math.Clamp(((sTarget - shd) % 360 + 540) % 360 - 180, -2.1, 2.1)) + 360) % 360;
            snX += (orbit ? 118 : 154) * Math.Cos(shd * Math.PI / 180); snZ += (orbit ? 118 : 154) * Math.Sin(shd * Math.PI / 180);
            double a0 = sAlt, want = orbit ? sAlt : tsn.vecFt * Ft;
            sAlt = want > sAlt ? Math.Min(sAlt + 15, want) : Math.Max(sAlt - 15, want);
            var m = tsn.Tick(S(sAlt - a0), none, sSec);
            if (m.Count > 0 && tsn.vec == null && Environment.GetEnvironmentVariable("VECDBG") == "Senaki") Console.WriteLine($"  {sSec}s h={shd:0} Holding: {m[0].Text}");
            if (m.Count == 0 || tsn.vec == null) continue;
            orbit = false;
            sCalls++;
            maxTurn = Math.Max(maxTurn, HdgDiff(tsn.vecHdg, shd));
            sTarget = tsn.vecHdg;
            var ini = tsn.Pt(tsn.vrw, InitialDist, 0);
            double toIni = Dist(snX, snZ, ini.X, ini.Z);
            if (tsn.vec.Count == 0) { grow = Math.Max(grow, toIni - lastToIni); lastToIni = toIni; }
            if (Environment.GetEnvironmentVariable("VECDBG") == "Senaki") { var (da, dl) = tsn.AL(snX, snZ, tsn.vrw); Console.WriteLine($"  {sSec}s a={da / NM:0.0} l={dl / NM:0.0} h={shd:0}: {m[0].Text}"); }
        }
        var (snA, snL) = tsn.AL(snX, snZ, tsn.vrw);
        Check(tsn.Phase == Phase.Entering && maxTurn <= 100 && grow < 0.3 * NM && Math.Abs(snL) < 0.5 * NM && snA > InitialDist,
              $"Senaki Radarführung: {sSec} s, {sCalls} Ansagen, größte Kurve {maxTurn:0}°, Initial-Entfernung wächst {grow / NM:0.0} NM, seitlich {snL:0} m, {snA / NM:0.0} NM vor der Schwelle");
        // Forum 0.9.5 (Senaki 27 from 38 NM southwest, Hornet): fast and flat (350 kt, 20° bank, 10 s reaction) -> after "turn left heading 360" swung wide,
        // 2 NM to the side past the base point; it stayed in place, Approach sent him back with "turn left heading 230" (reversal turn north of the axis)
        var swE = sen.End("27"); var swW = (X: -swE.Dx * 5, Z: -swE.Dz * 5);   // 10 kt headwind on the 27: drifts him to the east in base
        var tsw = new Tower(sen, "Enfield 1-1") { FieldWind = swW };
        double swX = sen.X - 26.9 * NM, swZ = sen.Z - 26.9 * NM, swH = 45, swAlt = 3000 * Ft, swT = 45, swMax = 0;
        Telemetry SW(double vs = 0) => new(swAlt, swAlt - sen.Elev, 180, swH * Math.PI / 180, swX, swZ, 0, 0, 760, vs);
        tsw.Tick(SW(), none, 0);
        var swSaid = new List<string> { tsw.OnTranscript("Senaki Approach, Enfield 1-1, inbound for landing", SW(), none, 1)[0].Text };
        var swDue = new List<(int At, double Hdg)> { (11, tsw.vecHdg) };
        int swSec = 2;
        for (; swSec < 2400 && tsw.Phase == Phase.Inbound; swSec++)
        {
            foreach (var d in swDue.Where(d => d.At == swSec)) swT = d.Hdg;   // turns 10 s after the call
            if (tsw.vec is { Count: 0 } && tsw.AL(swX, swZ, tsw.vrw) is var (pa, pl) && Math.Abs(pl) < NM) { var q = tsw.Pt(tsw.vrw, pa - 1.5 * NM, 0); swT = Bearing(swX, swZ, q.X, q.Z); }
            swH = (swH + Math.Clamp(((swT - swH) % 360 + 540) % 360 - 180, -1.1, 1.1) + 360) % 360;
            swX += 190 * Math.Cos(swH * Math.PI / 180) + swW.X; swZ += 190 * Math.Sin(swH * Math.PI / 180) + swW.Z;
            double a0 = swAlt, want = tsw.vecFt * Ft;
            swAlt = want > swAlt ? Math.Min(swAlt + 15, want) : Math.Max(swAlt - 15, want);
            var m = tsw.Tick(SW(swAlt - a0), none, swSec);
            if (m.Count == 0 || !m[0].Text.Contains("heading")) continue;
            swSaid.Add(m[0].Text);
            swDue.Add((swSec + 10, tsw.vecHdg));
            swMax = Math.Max(swMax, HdgDiff(tsw.vecHdg, swH));
        }
        Check(tsw.Phase == Phase.Entering && swMax <= 100, $"Senaki 27 aus Südwest, 350 kt/20°: am Queranflug-Punkt vorbei ohne Kehrtkurve (größte Kurve {swMax:0}°, {swSec} s): " + string.Join(" | ", swSaid));

        Ops.SelfTest(Check);
        Abm.SelfTest(Check);
        Carrier.SelfTest(Check);
        Flights.SelfTest(Check);
        Wheel.SelfTest(Check);
        // A114/N46: Threat/Merged become stale after 12 s (OpsTx), AWACS call -> debriefing line
        var ar = new[] { "Enfield one one, Overlord, threat, group BRAA zero nine zero, 20 miles, hot, Flanker.", "Enfield one one, Overlord, merged.", "Enfield one one, Overlord, radar contact, picture clean.", "Enfield one one, Overlord, new group, BRAA 090, 40 miles.",
            "Enfield one one, Overlord, threat, north lead group BRAA zero nine zero, 20 miles, hot." }.Select(Program.AwacsRow).ToArray();
        Check(ar[0] is { Fresh: true } && ar[0]!.Value.Row.StartsWith("threat BRAA zero nine zero, 20 miles") && ar[1] is { Row: "merged", Fresh: true } && ar[2] is { Row: "check-in", Fresh: false } && ar[3] == null &&
              ar[4] is { Row: "threat BRAA zero nine zero, 20 miles, hot", Fresh: true }, "A114/N46/R54: AWACS-Spruch -> Debriefing-Art, Threat/Merged frisch, Threat mit Gruppennamen");
        var dr = Program.ForPiper("Enfield one one, wind two four zero at eight, splash one, two five one decimal zero");
        Check(dr == "Enfield one-one, wind two-four-zero at eight, splash one, two-five-one decimal zero", "Piper: Ziffern gebunden: " + dr);

        // --- RW1 (A8-A12): runway occupied, takeoff clearances, go-around
        {
            string T(List<Msg> m) => string.Join(" | ", m.Select(x => x.Text));
            double h25 = LandHdg("25") * Math.PI / 180;
            var (f2x, f2z) = P("25", 2 * NM);
            var rwF2 = At(f2x, f2z, 2 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 1 };
            var (s0x, s0z) = P("25", -50);
            var luawAc = new Traffic(31, "F-16C_50", s0x, s0z, FieldElev, h25, 0, "", "", false);   // stands on the threshold after "line up and wait"
            var (s1x, s1z) = P("25", -2000);
            var rollAc = luawAc with { X = s1x, Z = s1z, Speed = 20 };   // Preceding traffic rolls out 2000 m (over 6000 ft) past the threshold
            var a8a = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, final", rwF2, new[] { luawAc }, 1);   // Reporting obligation: reply to the landing report
            var a8b = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, final", rwF2, new[] { rollAc }, 1);
            Check(a8a.Count == 1 && a8a[0].Contains("continue approach, traffic on runway") && a8b.Count == 1 && a8b[0].Contains("cleared to land"),
                  "A8 stehend auf der Schwelle belegt, Ausrollen über 6000 ft frei: " + T(a8a) + " / " + T(a8b));
            var rw8 = Cleared();
            rw8.Phase = Phase.TaxiOut;
            r = rw8.OnTranscript("Enfield 11 ready for departure", hold, new[] { luawAc }, 5);
            Check(rw8.Phase == Phase.HoldShort && !rw8.lineUp && r[0].Contains("hold short runway two five, traffic Viper on the runway"), "A8 Abflug, Bahn mit stehendem Verkehr: " + T(r));

            // A9: other takeoff clearance -> "number 2 for departure", no clearance until it is gone; landing aircraft hears "traffic departing"
            var rw9a = Cleared();
            rw9a.Phase = Phase.TaxiOut; rw9a.RunwayClaimed = 1;
            r = rw9a.OnTranscript("Enfield 11 ready for departure", hold, none, 5).Concat(rw9a.Tick(hold, none, 6)).ToList();
            rw9a.RunwayClaimed = 0;
            var r9 = rw9a.Tick(hold, none, 7);
            var rw9b = Pat(rwF2);
            rw9b.RunwayClaimed = 1;
            var r9b = rw9b.OnTranscript("Kutaisi Tower, Enfield 11, final", rwF2, none, 1);
            Check(r.Count == 1 && r[0].Contains("hold short runway two five, number 2 for departure") && r9.Count == 1 && r9[0].Contains("cleared for takeoff") &&
                  r9b.Count == 1 && r9b[0].Contains("continue approach, traffic departing runway two five"), "A9 andere Startfreigabe: " + T(r) + " / " + T(r9) + " / " + T(r9b));

            // A10: behind a taxiing aircraft with approach at 5 NM no "line up and wait"; waiting on the runway, runway free, approach 5 NM -> "immediate"; 180 s only over 2.5 NM
            var (f5x, f5z) = P("25", 5 * NM);
            var rwC130 = new Traffic(32, "C-130", f5x, f5z, FieldElev + 450, h25, 70, "AI", "arr", true);
            var depAc = luawAc with { X = s0x - 300 * Ux, Z = s0z - 300 * Uz, Speed = 30 };   // is just taking off (300 m past the threshold)
            var rw10 = Cleared();
            rw10.Phase = Phase.TaxiOut;
            r = rw10.OnTranscript("Enfield 11 ready for departure", hold, new[] { depAc, rwC130 }, 5);
            Check(rw10.Phase == Phase.HoldShort && !rw10.lineUp && r[0].Contains("hold short runway two five, traffic C 130 on 5 mile final"), "A10 kein LUAW bei Anflug 5 NM: " + T(r));
            rw10.Phase = Phase.TaxiOut;
            r = rw10.OnTranscript("Enfield 11 ready for departure", hold, new[] { depAc }, 6);
            var r10 = rw10.Tick(At(s0x, s0z, 0, LandHdg("25")), new[] { rwC130 }, 7);
            Check(rw10.lineUp == false && r[0].Contains("line up and wait") && r10.Count == 1 && r10[0].Contains("cleared for immediate takeoff, traffic C 130 on 5 mile final"),
                  "A10 LUAW, dann immediate: " + T(r) + " / " + T(r10));
            {   // R343: LUAW only behind a takeoff roll in departure direction, with traffic description; opposite direction -> hold short
                var rw343 = Cleared(); rw343.Phase = Phase.TaxiOut;
                var opp343 = rw343.OnTranscript("Enfield 11 ready for departure", hold, new[] { depAc with { Hdg = h25 + Math.PI } }, 5)[0].Text;
                bool hs343 = rw343.Phase == Phase.HoldShort && !rw343.lineUp;
                rw343.Phase = Phase.TaxiOut;
                var dep343 = rw343.OnTranscript("Enfield 11 ready for departure", hold, new[] { depAc }, 6)[0].Text;
                Check(hs343 && opp343.Contains("hold short runway two five, traffic Viper") && dep343 == "Enfield one one, runway two five, line up and wait, traffic Viper departing runway two five." && rw343.lineUp,
                      $"R343 LUAW nur hinter Startlauf: {opp343} | {dep343}");
            }
            var (f2bx, f2bz) = P("25", 2 * NM);
            var near2 = rwC130 with { X = f2bx, Z = f2bz, AltMsl = FieldElev + 180 };
            var rw10b = Cleared();
            rw10b.Phase = Phase.TaxiOut;
            rw10b.OnTranscript("Enfield 11 ready for departure", hold, new[] { near2 }, 5);
            var r180 = rw10b.Tick(hold, new[] { near2 }, 190);
            var (f3x, f3z) = P("25", 3 * NM);
            var r180b = rw10b.Tick(hold, new[] { near2 with { X = f3x, Z = f3z } }, 191);
            Check(rw10b.Phase == Phase.ClearedTakeoff && r180.Count == 0 && r180b.Count == 1 && r180b[0].Contains("cleared for takeoff"),
                  "A10 180 s: bei 2 NM nicht, bei 3 NM schon: " + T(r180) + " / " + T(r180b));

            // A11: without landing clearance, runway occupied: at 0.7 NM go-around (not Away, R11)
            var (g7x, g7z) = P("25", 0.7 * NM);
            var fin07 = At(g7x, g7z, 0.7 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 1 };
            var rw11 = Pat(rwF2);
            var r11 = rw11.Tick(rwF2, new[] { luawAc }, 1).Concat(rw11.Tick(fin07, new[] { luawAc }, 20)).ToList();
            var rw11b = new Tower("Enfield 1-1");
            rw11b.Tick(fin07, new[] { luawAc }, 0);
            var r11b = rw11b.Tick(fin07, new[] { luawAc }, 1);
            Check(r11.Count == 2 && r11[1].Contains("go around, I say again, go around, traffic on runway, climb and maintain") && r11[1].Contains("join left hand downwind runway two five, report base") &&
                  rw11.Phase == Phase.Pattern && !rw11.wentAround && rw11b.Phase == Phase.Away && !r11b.Any(m => m.Text.Contains("go around")),
                  "A11 Go-around ohne Landefreigabe: " + T(r11) + " / Away: " + T(r11b));
            var r11c = rw11.Tick(fin07, new[] { luawAc }, 21).Concat(rw11.Tick(fin07, none, 22)).ToList();   // right after, still on final: not again, no "cleared to land"
            Check(r11c.Count == 0, "A11 nach dem Go-around still: " + T(r11c));

            // A12/Reporting obligation #7: silently went around (500 m past the threshold, 100 m high) -> "I show you going around, confirm?", pattern, clearance gone, debriefing; taxied back from the holding point -> clearance lapses
            var (p5x, p5z) = P("25", -500);
            var rw12 = Pat(rwF2);
            rw12.Phase = Phase.ClearedLand;
            var r12 = rw12.Tick(At(p5x, p5z, 100, LandHdg("25"), 80), none, 1);
            var rw12b = Cleared();
            var back = At(Thr25X - 1000 * Uz, Thr25Z + 1000 * Ux, 0, 0, 5);
            var r12b = rw12b.Tick(back, none, 4).Concat(rw12b.Tick(back, none, 14)).Concat(rw12b.Tick(back, none, 24)).ToList();
            var r12c = rw12b.Tick(back, none, 36);
            Check(rw12.Phase == Phase.Pattern && r12.Count == 2 && r12[0].Text == "Enfield one one, I show you going around, confirm? Climb and maintain 2000 feet, join left hand downwind runway two five, report base." &&
                  r12[1].Text.StartsWith(L("Verstoß: Durchstarten ohne Meldung Kutaisi 25", "Deviation: go-around without report Kutaisi 25")) &&
                  r12b.Count == 0 && rw12b.Phase == Phase.TaxiOut && r12c.Count == 1 && r12c[0].Contains("takeoff clearance cancelled, contact Kutaisi Ground"),
                  "A12 stilles Durchstarten / zurückgerollt: " + T(r12) + " / " + T(r12b) + " / " + T(r12c));

            // R201: landing clearance without "gear down" in the report with "check wheels down", with report without; R205: departing heavy ahead in the clearance
            var (k1x, k1z) = P("25", -1500);
            var kc = new Traffic(41, "KC-135", k1x, k1z, FieldElev + 150, h25, 90, "AI", "dep", true);
            var w201a = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, final", rwF2, none, 1);
            var w201b = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, final, gear down", rwF2, none, 1);
            var w205 = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, final, gear down", rwF2, new[] { kc }, 1);
            var w201h = Pat(rwF2);
            w201h.AcType = "UH-1H";   // Skids: no wheels-down check
            Check(T(w201h.OnTranscript("Kutaisi Tower, Enfield 11, final", rwF2, none, 1)) == "Enfield one one, runway two five, wind calm, cleared to land.", "R201 Hubschrauber ohne check wheels down");
            Check(T(w201a) == "Enfield one one, check wheels down, runway two five, wind calm, cleared to land." && T(w201b) == "Enfield one one, runway two five, wind calm, cleared to land." &&
                  T(w205) == "Enfield one one, caution wake turbulence, departing KC 135, runway two five, wind calm, cleared to land.", "R201/R205 Landefreigabe: " + T(w201a) + " / " + T(w201b) + " / " + T(w205));
            // R205: "ready for departure" behind departing heavy -> holding point, 2-min interval, afterwards clearance on its own (tick)
            var wd = new Tower("Enfield 1-1");
            wd.Tick(parked, none, 0);
            wd.OnTranscript("Enfield 11 request startup", parked, none, 1);
            wd.OnTranscript("Enfield 11 request taxi", parked, none, 2);
            var (k0x, k0z) = P("25", -500);
            wd.Tick(hold, new[] { kc with { X = k0x, Z = k0z, AltMsl = FieldElev, InAir = false, Speed = 60 } }, 3);
            var d205 = wd.OnTranscript("Enfield 11 ready for departure", hold, none, 4);
            var d205b = wd.Tick(hold, none, 100);
            var d205c = wd.Tick(hold, none, 124);
            Check(T(d205) == "Enfield one one, hold short runway two five, wake turbulence, 2 minute interval." && !d205b.Any(m => m.Contains("cleared for takeoff")) && d205c.Any(m => m.Contains("cleared for takeoff")),
                  "R205 2-min-Intervall hinter Heavy: " + T(d205) + " / " + T(d205b) + " / " + T(d205c));
            // R202: "request closed" in the pattern -> closed traffic approved, report base; not for IFR
            var w202 = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, request closed traffic", rwF2, none, 1);
            ForceIfr = true;
            var w202i = Pat(rwF2).OnTranscript("Kutaisi Tower, Enfield 11, request closed traffic", rwF2, none, 1);
            ForceIfr = false;
            Check(T(w202) == "Enfield one one, left closed traffic approved, report base." && T(w202i) == "Enfield one one, closed traffic not approved, I F R conditions.", "R202 request closed: " + T(w202) + " / " + T(w202i));
            // R204: SFO high key -> low key -> low approach clearance with pattern; for IFR unable
            var sfoAt = At(CX + 3 * NM, CZ, 2500 * Ft, LandHdg("25"), 120);
            var ws = new Tower("Enfield 1-1");
            ws.Tick(sfoAt, none, 0);
            var sf1 = ws.OnTranscript("Kutaisi Tower, Enfield 11, request S F O", sfoAt, none, 1);
            var sf2 = ws.OnTranscript("Kutaisi Tower, Enfield 11, high key", sfoAt, none, 2);
            var sf3 = ws.OnTranscript("Kutaisi Tower, Enfield 11, low key, gear down", sfoAt, none, 3);
            ForceIfr = true;
            var sf4 = new Tower("Enfield 1-1").OnTranscript("Kutaisi Tower, Enfield 11, request S F O", sfoAt, none, 1);
            ForceIfr = false;
            Check(T(sf1).StartsWith("Enfield one one, runway two five, wind calm, QNH") && T(sf1).EndsWith("report high key.") && T(sf2) == "Enfield one one, report low key." && ws.Phase == Phase.ClearedLand &&
                  T(sf3).StartsWith("Enfield one one, runway two five, ") && T(sf3).EndsWith("cleared low approach, left closed traffic approved, report base.") && T(sf4).Contains("unable S F O, I F R conditions"),
                  "R204 SFO: " + T(sf1) + " / " + T(sf2) + " / " + T(sf3) + " / " + T(sf4));
            // R296: real flameout after own MAYDAY: high key -> low key -> cleared to land; "flameout, high key" without emergency call also in IMC; foreign emergency still unable
            var wfo = new Tower("Enfield 1-1");
            wfo.Tick(sfoAt, none, 0);
            wfo.OnTranscript("Mayday mayday mayday, Enfield 11, engine failure", sfoAt, none, 1);
            var fo1 = wfo.OnTranscript("Kutaisi Tower, Enfield 11, high key", sfoAt, none, 2);
            var fo2 = wfo.OnTranscript("Kutaisi Tower, Enfield 11, low key, gear down", sfoAt, none, 3);
            ForceIfr = true;
            var wfi = new Tower("Enfield 1-1");
            wfi.Tick(sfoAt, none, 0);
            var fo3 = wfi.OnTranscript("Kutaisi Tower, Enfield 11, flameout, high key", sfoAt, none, 1);
            ForceIfr = false;
            var fo4 = new Tower("Enfield 1-1") { OtherEmergency = true }.OnTranscript("Kutaisi Tower, Enfield 11, request S F O", sfoAt, none, 1);
            Check(T(fo1) == "Enfield one one, report low key." && wfo.Phase == Phase.ClearedLand &&
                  T(fo2).StartsWith("Enfield one one, runway two five, ") && T(fo2).EndsWith(", cleared to land.") && wfi.Emergency && T(fo3).EndsWith("report low key.") && T(fo4).Contains("unable S F O, emergency in progress"),
                  "R296 Flameout: " + T(fo1) + " / " + T(fo2) + " / " + T(fo3) + " / " + T(fo4));
            // Reporting obligation #3/#6/#2: at the C R P without call "report C R P", 2 NM further "say position" without handoff; after the call handoff, without call at the Tower "contact Tower now"; over the airfield without call "report overhead", afterwards "no clearance to join"
            List<Msg> FlyP(Tower w, (double X, double Z) a, (double X, double Z) b, double t0, double t1)   // every 5 s a tick on the leg a -> b
            {
                var m = new List<Msg>();
                double hd = Bearing(a.X, a.Z, b.X, b.Z);
                for (double s = t0; s <= t1; s += 5) { double f = (s - t0) / (t1 - t0); m.AddRange(w.Tick(At(a.X + f * (b.X - a.X), a.Z + f * (b.Z - a.Z), PatternFt * Ft - FieldElev, hd, 150 * Kt), none, s)); }
                return m;
            }
            var wc = new Tower("Enfield 1-1");
            var cn = Crp["north"];
            var atC = At(cn.X, cn.Z, PatternFt * Ft - FieldElev, 180, 150 * Kt);
            wc.Tick(atC, none, 0);
            (wc.Phase, wc.entry, wc.nav, wc.navName) = (Phase.Inbound, "north", cn, "C R P north");
            var c1 = wc.Tick(atC, none, 1);
            var c2 = wc.Tick(atC with { X = cn.X + 1.2 * NM }, none, 10).Concat(wc.Tick(atC with { X = cn.X + 2.2 * NM }, none, 20)).ToList();
            var c3 = wc.OnTranscript("Kutaisi Approach, Enfield 11, C R P north", atC, none, 25);
            var ovh = wc.Ovh;
            var c4 = FlyP(wc, cn, ovh, 30, 30 + Dist(cn.X, cn.Z, ovh.X, ovh.Z) / (150 * Kt));
            var c6 = FlyP(wc, ovh, (ovh.X + 1.6 * NM, ovh.Z), 400, 440);
            var c7 = wc.OnTranscript("Kutaisi Tower, Enfield 11, overhead", At(ovh.X + 1.6 * NM, ovh.Z, PatternFt * Ft - FieldElev, 90, 150 * Kt), none, 441);
            int nagI = c4.FindIndex(m => m.Role == "Approach" && m.Text.StartsWith("Enfield one one, contact Kutaisi Tower now, ")), rep = c4.FindIndex(m => m.Text == "Enfield one one, Kutaisi Tower, report overhead.");
            Check(T(c1) == "Enfield one one, report C R P north." && c1[0].Role == "Approach" && T(c2) == "Enfield one one, say position, remain outside the control zone." &&
                  c3[0].Contains("contact Kutaisi Tower") && nagI >= 0 && rep > nagI && c4.Count(m => m.Text.Contains("now,")) == 1 && c4.Count(m => m.Text.Contains("report overhead")) == 1 &&
                  T(c6) == "Enfield one one, no clearance to join, remain outside the pattern, say intentions." && c7[0].Contains("join"),
                  "Meldepflicht C R P / contact Tower now / overhead: " + string.Join(" / ", new[] { c1, c2, c3, c4, c6, c7 }.Select(T)));
            // shared map frequency (Approach = Tower): no "contact Tower now"
            var wq = new Tower("Enfield 1-1");
            wq.Tick(atC, none, 0);
            (wq.Phase, wq.towerDue, wq.handoffAt) = (Phase.Entering, true, 0);
            double uhf0 = wq.F.Uhf;
            wq.F.Uhf = 263;
            var cq = Enumerable.Range(1, 15).SelectMany(i => wq.Tick(atC, none, i * 5)).ToList();
            wq.F.Uhf = uhf0;
            var wq2 = new Tower("Enfield 1-1");
            wq2.Tick(atC, none, 0);
            (wq2.Phase, wq2.towerDue, wq2.handoffAt) = (Phase.Entering, true, 0);
            var cq2 = Enumerable.Range(1, 15).SelectMany(i => wq2.Tick(atC, none, i * 5)).ToList();
            Check(!cq.Any(m => m.Text.Contains(" now,")) && cq2.Count(m => m.Text.Contains("contact Kutaisi Tower now,")) == 1, "Meldepflicht gemeinsame Frequenz ohne contact Tower now: " + T(cq) + " / getrennt: " + T(cq2));
            // Reporting obligation #4: pattern without landing report -> "report final", under 0.8 NM go-around with debriefing
            var w4 = Pat(rwF2);
            var f4a = w4.Tick(rwF2, none, 1);
            var (f07x, f07z) = P("25", 0.7 * NM);
            var f4b = w4.Tick(At(f07x, f07z, 0.7 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 70) with { Gear = 1 }, none, 30);
            Check(T(f4a) == "Enfield one one, report final." && f4b.Any(m => m.Text.StartsWith("Enfield one one, go around, no landing clearance")) &&
                  f4b.Any(m => m.Role == "Info" && m.Text.StartsWith(L("Verstoß: keine Landemeldung Kutaisi 25", "Deviation: no landing report Kutaisi 25"))) && w4.Phase != Phase.ClearedLand,
                  "Meldepflicht #4 Platzrunde ohne Landemeldung: " + T(f4a) + " / " + T(f4b));
            // Review l1: after "no clearance to join, remain outside the pattern" nevertheless in the pattern or after "remain outside the control zone" further into the zone:
            // once redirect with violation (debriefing), the notice "possible pilot deviation" comes separately
            var kow = new Tower("Enfield 1-1");
            kow.Tick(atC, none, 0);
            (kow.Phase, kow.nav, kow.navName) = (Phase.Entering, ovh, "overhead");
            var ko1 = FlyP(kow, (ovh.X - NM, ovh.Z), (ovh.X + 1.6 * NM, ovh.Z), 1, 40);
            var ko2 = FlyP(kow, (ovh.X + 1.6 * NM, ovh.Z), (ovh.X + 1.6 * NM, ovh.Z + 1.5 * NM), 45, 100);   // stays in the pattern
            var kzw = new Tower("Enfield 1-1");
            kzw.Tick(atC, none, 0);
            (kzw.Phase, kzw.entry, kzw.nav, kzw.navName) = (Phase.Inbound, "north", cn, "C R P north");
            kzw.Tick(atC, none, 1);
            var kz2 = kzw.Tick(atC with { X = cn.X + 1.2 * NM }, none, 10).Concat(kzw.Tick(atC with { X = cn.X + 2.2 * NM }, none, 20)).ToList();
            var kz3 = FlyP(kzw, (cn.X + 2.2 * NM, cn.Z), ovh, 25, 160);   // flies to the airfield anyway
            Check(ko1.Concat(ko2).Count(m => m.Text.Contains("no clearance to join")) == 1 && ko2.Count(m => m.Text == "Enfield one one, you are not cleared to join, leave the pattern immediately, say intentions.") == 1 &&
                  ko2.Any(m => m.Role == "Info" && m.Text.StartsWith(L("Verstoß: Platzrunde ohne Freigabe Kutaisi", "Deviation: pattern entry without clearance Kutaisi"))) &&
                  T(kz2) == "Enfield one one, say position, remain outside the control zone." &&
                  kz3.Count(m => m.Role == "Approach" && m.Text == "Enfield one one, you are inside the Kutaisi control zone without clearance, leave the control zone immediately, say intentions.") == 1 &&
                  kz3.Any(m => m.Role == "Info" && m.Text.StartsWith(L("Verstoß: Kontrollzone ohne Freigabe Kutaisi", "Deviation: control zone entered without clearance Kutaisi"))),
                  "Review l1 trotz remain outside hinein: " + T(ko1) + " / " + T(ko2) + " / " + T(kz2) + " / " + T(kz3));
            // Review l1: "base" on downwind (away from the airfield) -> "negative, I show you on downwind, number one, report base", no landing clearance
            var dwp = new[] { 1.2 * NM, -1.2 * NM }.Select(l => kow.Pt("25", -0.5 * NM, l)).First(p => kow.PatternOffset(p.X, p.Z, "25") > 700);
            var dwT = At(dwp.X, dwp.Z, PatternFt * Ft - FieldElev, LandHdg("25") + 180, 150 * Kt);
            var wb = Pat(dwT);
            var b1 = wb.OnTranscript("Kutaisi Tower, Enfield 11, base, gear down", dwT, none, 1);
            Check(T(b1) == "Enfield one one, negative, I show you on downwind, number one, report base." && wb.Phase == Phase.Pattern, "Review l1 base im Gegenanflug: " + T(b1));
            // Review l1: still with Approach (Inbound, without handoff) on final: "contact Tower, report final", landing clearance only on the call
            var wi = new Tower("Enfield 1-1");
            var (i3x, i3z) = P("25", 2.8 * NM);
            var i3 = At(i3x, i3z, 2.8 * NM * Math.Tan(3 * Math.PI / 180) + 15, LandHdg("25"), 75) with { Gear = 1 };
            wi.Tick(i3, none, 0);
            wi.Phase = Phase.Inbound;
            var i1 = wi.Tick(i3, none, 1);
            var i2 = wi.OnTranscript("Kutaisi Tower, Enfield 11, final, gear down, full stop", i3, none, 3);
            Check(i1.Count == 1 && i1[0].Role == "Approach" && i1[0].Text.StartsWith("Enfield one one, contact Kutaisi Tower") && i1[0].Text.EndsWith(", report final.") && wi.Phase != Phase.Inbound &&
                  i2.Count == 1 && i2[0].Text.Contains("cleared to land") && wi.Phase == Phase.ClearedLand, "Review l1 Inbound im Endanflug: " + T(i1) + " / " + T(i2));
        }

        Console.WriteLine(fails == 0 ? "Alle Tests OK" : $"{fails} Test(s) fehlgeschlagen");
        return fails == 0 ? 0 : 1;
    }
}
