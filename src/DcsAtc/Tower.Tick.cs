using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= every second
    /// New flight / respawn: gap > 10 s AND position jump or ground/air changed (pause: aircraft stands still -> continue as before).
    /// Also on the radio call: an airfield he calls anew or again after a while is known afterwards (otherwise the next tick resets it).
    void Seen(Telemetry t, double now)
    {
        if (now - lastSeen > 10 && (lastTel == null || Dist(t.X, t.Z, lastTel.X, lastTel.Z) > 3000 || OnGround(t) != OnGround(lastTel))) Reset(t, now);
        lastTel = t;
        lastSeen = now;
    }

    public List<Msg> Tick(Telemetry? t, IReadOnlyList<Traffic> traffic, double now)
    {
        if (t == null) return new();
        near = traffic; callAt = now; WatchSaid = false;
        Seen(t, now);

        var c = Cs();
        bool ground = OnGround(t);
        double kt = t.Ias / Kt;
        if (!ground && t.Agl > 30) { airborne = true; graded = false; }
        role = Owner();

        // Touchdown on a runway -> landing evaluation (text only in the game)
        bool touchdown = wasAir && ground && !graded && OnRunwayPos(t.X, t.Z, 45, 300);
        if (wasAir && ground) touchAt = now;
        wasAir = !ground;
        if (!ground) lastVs = t.Vs;
        if (touchdown) { graded = true; return new() { new Msg("Info", Grade(t)) }; }

        // R247: noted notice "possible pilot deviation" separate from the instruction (JO 7110.65 2-1-26, workload permitting): 10 s afterwards when stationary off the runway or outside pattern and final
        if (devPend && now - devNoteAt >= 10 && (ground ? kt < 1 + WindSpeed(t) / Kt && !OnRunwayPos(t.X, t.Z) : holding || Phase is Phase.Away or Phase.Departing or Phase.Inbound))
        {
            devPend = false;
            WatchSaid = !called;   // Violation from the watch without any call: like its calls (no controller contact, Guard)
            return Say($"{c}, possible pilot deviation, advise you contact {F.StationOf(devFac)} at D S N {Digits("314")}, {Digits($"{(F.Name + (devFac == "Tower" ? "" : devFac)).Aggregate(17, (h, ch) => (h * 31 + ch) % 100000) % 9000 + 1000}")}.", devBy);   // fixed fictional number per airfield and facility; speaks whoever is now controlling him (after handoff Approach), without check-in the Tower
        }

        // N40: destination of the route clearance captured -> ask for new destination (answer: on the ground case "clearance", in the air "alternate")
        if (depClr && Hostile(dest))
        {
            var dn = dest!.Name.Replace('-', ' ');
            if (navName == dn) nav = null;
            (dest, altAsk, cruiseFt) = (null, !ground, 0);
            return Say($"{c}, {dn} is closed, say alternate destination.");
        }

        // Change active runway only outside ongoing procedures
        if (Phase is Phase.Parked or Phase.Away) Runway = wantRw ?? WindRunway(t);
        var rw = Runway;

        // Runway occupied (also other takeoff clearance, A9) or opposite traffic on final: never, not even after 3 min. A10: on the runway (LUAW) with approach at 2.5-6 NM "immediate",
        // the 180-s exception only with approach over 2.5 NM. R110: only after a request (holdSince >= 0), never after spawn/reset on the runway (-1) or rejected takeoff (-2, R100)
        // R205: heavy in takeoff roll/departure on the active runway (up to 500 ft): 2-min interval counts from the last time
        if (traffic.Any(a => Heavy(a.Type) && a.Speed > 30 && a.AltMsl - FieldElev < 500 * Ft && HdgDiff(a.Hdg * 180 / Math.PI, LandHdg(rw)) < 30 &&
                             Approach(a.X, a.Z, rw).lateral < 0.5 * NM && HeavyDep(a, rw))) heavyDepAt = now;
        if (Phase == Phase.HoldShort && holdSince >= 0 && now - heavyDepAt >= 120 && !EmgWithin(15) && !VfrBlocked && HoldAhead == 0 && !TrafficOnRunway(traffic) && !TrafficOnFinal(traffic, Airfield.Opposite(rw), 4 * NM))   // R339: no VFR takeoff in IMC; R341: earliest report first
        {
            var fin = FinalTraffic(traffic, rw, 6 * NM).FirstOrDefault();
            double fa = fin == null ? double.MaxValue : Approach(fin.X, fin.Z, rw).along;
            if (lineUp && fin != null && fa >= 2.5 * NM) return Say(TakeoffClearance(t, traffic, fin));
            if (fa >= 4 * NM || now - holdSince > 180 && fa > 2.5 * NM) return Say(TakeoffClearance(t, traffic));
        }

        // A42: withdraw takeoff clearance as long as the takeoff roll has not begun: traffic on the runway or landing aircraft under 1.5 NM
        if (Phase == Phase.ClearedTakeoff && ground && kt < 30)
        {
            bool onRwy = OnRunwayPos(t.X, t.Z);   // already on the runway: landing aircraft under 1.5 NM withdraws nothing, AI does not go around, continuing the takeoff clears the runway
            var low = (onRwy ? Enumerable.Empty<Traffic>() : FinalTraffic(traffic, rw, 1.5 * NM)).Concat(FinalTraffic(traffic, Airfield.Opposite(rw), 4 * NM)).FirstOrDefault();   // Opposite traffic from 4 NM already
            bool busy = RwyTraffic(traffic) != null;   // other takeoff clearance (A9) withdraws nothing, only traffic on the runway
            if (busy || low != null)
            {
                Phase = Phase.HoldShort;
                holdSince = now;
                lineUp = onRwy;   // already on the runway: no "not cleared onto runway", the takeoff roll without clearance is stopped anyway
                return Say($"{c}, hold position, cancel takeoff clearance, traffic " + (busy ? "on the runway." : Describe(low!, rw) + "."), "Tower");
            }
        }

        // R348: cleared for takeoff but not rolling: query after 60 s, cancel after 120 s (FAA JO 7110.65 3-9-1); on the runway he may stand like after a spawn there (holdSince -1: only on "ready")
        if (Phase == Phase.ClearedTakeoff && ground && Gs(t) / Kt < 2)
        {
            if (clrStill < 0) clrStill = now;
            else if (now - clrStill >= 120)
            {
                bool onRwy = OnRunwayPos(t.X, t.Z);
                (Phase, holdSince, lineUp, clrStill, clrAsked) = (Phase.HoldShort, -1, onRwy, -1, false);
                return Say($"{c}, cancel takeoff clearance, " + (onRwy ? "exit the runway" : $"hold short runway {RwSay(rw)}") + ", report ready for departure.", "Tower");
            }
            else if (now - clrStill >= 60 && !clrAsked)
            {
                clrAsked = true;
                return Say($"{c}, verify rolling, runway {RwSay(rw)}, cleared for takeoff.", "Tower");
            }
        }
        else (clrStill, clrAsked) = (-1, false);

        // A12: taxied back from the holding point (30 s over 700 m from the threshold, not on the runway): clearance lapses, back to Ground
        if (ground && Phase is Phase.HoldShort or Phase.ClearedTakeoff && !OnRunwayPos(t.X, t.Z) && Dist(t.X, t.Z, Thr(rw).X, Thr(rw).Z) > 700)
        {
            if (rollBack < 0) rollBack = now;
            else if (now - rollBack > 30)
            {
                bool clr = Phase == Phase.ClearedTakeoff, rto = Phase == Phase.HoldShort && holdSince == -2;
                (Phase, lineUp, rollBack) = (Phase.TaxiOut, false, -1);
                if (rto) return new();   // R267: after rejected takeoff already "contact Ground when vacated" or "say intentions"
                return Say($"{c}, {(clr ? "takeoff clearance cancelled, " : "")}{(SameFreq("Ground") ? "say intentions" : $"contact {Contact("Ground")}")}.", "Tower");
            }
        }
        else rollBack = -1;

        // Runway entered without clearance / takeoff roll without clearance; after "line up and wait" he may stand (hold position until 40 kt dropped), not taxi
        if (kt <= 40) rejecting = false;
        // Forum 0.9.7: waits at the holding point of the parallel runway, which is now free -> clear to cross (like the takeoff clearance after "hold short")
        if (ground && Phase == Phase.TaxiOut && crossWait is { } cw && CrossNext(t) == cw && CrossBlock(traffic, cw) == null) { role = crossRole; return Say(Cross(cw, traffic)); }
        if (ground && RunwayAt(t.X, t.Z, t.Hdg * 180 / Math.PI, rw) is { } hitRw && !(Phase == Phase.TaxiOut && kt <= 30 && crossOk is { } co && (hitRw == co || hitRw == Airfield.Opposite(co)))   // cleared to cross
            && Phase is Phase.Parked or Phase.StartupApproved or Phase.TaxiOut or Phase.HoldShort)
        {
            // Reporting obligation #14: spawned on the runway (holdSince -1) or after rejected takeoff (-2, R100) there is no silent takeoff clearance any more, only on "ready for departure"
            if (kt > (Phase == Phase.HoldShort && lineUp ? 40 : 30) && !rejecting && now - lastAbort > 20)   // R105: without lineUp from 30 kt (on the runway only the Tower speaks, no "reduce taxi speed")
            {
                lastAbort = now;
                return Dev($"{c}, stop immediately, I say again, stop immediately! You are not cleared for takeoff.", $"Start ohne Freigabe {F.Name} {hitRw}", $"takeoff without clearance {F.Name} {hitRw}", "Tower");
            }
            if (kt <= 40 && !(Phase == Phase.HoldShort && lineUp) && now - lastIncursion > 45)
            {
                lastIncursion = now;
                return Dev($"{c}, hold position! You are not cleared onto runway {RwSay(hitRw)}.", $"Bahn ohne Freigabe betreten {F.Name} {hitRw}", $"runway entered without clearance {F.Name} {hitRw}", "Tower");   // A38: the entered runway, not the active one
            }
        }

        if (idAt > 0)   // R128: code set (6 s), now identify; phase left/landed: lapses
        {
            if (ground || Phase is not (Phase.Away or Phase.Departing)) { idAt = -1; popDf = null; }   // R219: do not attach a lapsed pop-up clearance to a later identification
            else if (now >= idAt)
            {
                idAt = -1;
                var pdf = popDf; popDf = null;
                return Say($"{c}, {(idFf ? FfRc(t) : pdf != null ? $"radar contact, {Where(t)}. {Cap(PopClr(t, pdf))}" : AirRc(t))}", "Approach");
            }
        }

        if (!ground && t.Agl > 100)
        {
            if (Phase == Phase.ClearedTakeoff && stayPattern && goFt > 0) { wantRw = null; var md = Missed(t, now, "", true); missDep = true; return md; }   // R253: radar pattern, climb-out was in the takeoff clearance
            if (Phase == Phase.ClearedTakeoff)
            {
                Phase = stayPattern ? Phase.Pattern : Phase.Departing;   // Patterns: straight into the circuit
                wantRw = null;   // A41: the runway request for departure does not bind the landing runway
                if (stayPattern) PatternReset();
            }
            else if (Phase is Phase.Parked or Phase.StartupApproved or Phase.TaxiOut or Phase.HoldShort && Dist(t.X, t.Z, CX, CZ) <= LocalM)
            {
                exitDir = wantRw = null;
                if (!Watch && !called) Phase = Phase.Away;   // N4: without monitoring and without call at this airfield silent (deliberately flies without ATC)
                else
                {
                    Phase = Phase.Departing;
                    var to = RwyOf(t);
                    return Dev($"{c}, {F.StationOf("Tower")}, you departed without takeoff clearance. Report intentions.", $"Start ohne Freigabe {F.Name} {to}", $"takeoff without clearance {F.Name} {to}", "Tower");
                }
            }
        }

        if (Phase == Phase.Departing)
        {
            if (!handedOff && !ground && t.Agl > 150)   // approx. 500 ft: Tower hands over to Departure
            {
                handedOff = true; handedAt = now; dueAt = -1;
                return Say($"{c}, contact {Contact("Approach")}.", "Tower");
            }
            bool atExit = AtExit(t), outside = atExit || Dist(t.X, t.Z, CX, CZ) > CtrM;
            // Reporting obligation #8/#11 (instead of A105): handed over, but no check-in with Departure: no identification on its own. After 60 s or at the zone edge (5 NM, A100)
            // or departure CRP (R7) the Tower reminds ("contact Kutaisi Approach now"), 60 s later no radar service: IFR violation, VFR "radar service not provided, squawk VFR"
            if (handedOff && handedAt >= 0 && !ground && !Following)
            {
                if (Overdue(now - handedAt > 60 || outside, true, 60, now, () => { role = "Tower"; return Say($"{c}, {ContactNow("Approach")}.", "Tower"); }, () =>
                    {
                        role = "Tower";
                        var dfac = Dep("Approach");   // own Departure frequency: violation at Departure (determine before the phase change)
                        var ns = $"{c}, no contact with {F.StationOf(dfac)}, radar service not provided";
                        Phase = Phase.Away; nav = null; handedOff = false; Released = true;
                        return depClr ? Dev(ns + ".", $"kein Check-in bei Departure {F.Name}", $"no check-in with departure {F.Name}", "Tower", fac: dfac, by: "Tower")   // Review l2: violation at Departure (Approach or own Departure), notice from the Tower
                                      : Say($"{ns}, {SquawkVfr()}frequency change approved.", "Tower");
                    }) is { } dm) return dm;
            }
            else if (outside)
            {
                Phase = Phase.Away;
                nav = null;
                if (!Following)   // with Flight Following no release (A51), radar service continues
                {
                    var by = handedOff ? "Approach" : "Tower";
                    var bye = Say($"{c}, leaving control zone, " + (AwacsContact is { } aw ? $"contact {aw}." : SquawkVfr() + "frequency change approved. Good day."), by);
                    handedOff = false; Released = true;   // released: no second release at 15 NM, pin released (R47); only after Say (Dep)
                    return bye;
                }
            }
        }

        // Touch-and-go / low approach finished -> next pattern
        if (Phase == Phase.ClearedLand && option is "touch and go" or "low approach" or "low pass" or "the option" && !ground && t.Agl > 60 &&
            Approach(t.X, t.Z, rw).along < -300)
        {
            if (option == "low pass") { Phase = Phase.Away; option = null; PatternReset(); Released = true; return new(); }   // A47: climb was in the clearance, no pattern
            Laps++;
            if (Ifr || wantStraight)   // Practice instrument approach: radar vectoring again right away
            {
                var mv = Missed(t, now, $"{option} complete.", true);
                mv.Add(new Msg("Info", L($"Anflug {Laps} ({option}). Beenden: Approach -> full stop.", $"Approach {Laps} ({option}). To end: Approach -> full stop.")));
                return mv;
            }
            Phase = Phase.Pattern;
            PatternReset();
            var m = new List<Msg>();   // Pattern was already in the clearance (ClearText): no second instruction after liftoff
            m.Add(new Msg("Info", L($"Platzrunde {Laps} ({option}). Beenden: Tower -> Final, full stop.", $"Pattern {Laps} ({option}). To end: Tower -> final, full stop.")));
            return m;
        }

        bool radarDep = Phase is Phase.Departing or Phase.Away && handedOff && !Following && idAt <= 0;   // R213: handed-over departure or en route with Approach (before handoff/release); Flight Following: FollowTick
        if (!ground && (Phase is Phase.Inbound or Phase.Entering || radarDep) && TrafficAlert(t, traffic, now) is { } ta) return ta;   // Collision warning before everything else
        if (!ground && Phase is Phase.Initial or Phase.Pattern or Phase.ClearedLand && PatternTraffic(t, traffic) is { } pt) return pt;   // A99: crossing traffic in the pattern
        if (!ground && Phase == Phase.Away && Following && idAt <= 0 && FollowTick(t, traffic, now) is { } fm) return fm;
        if (!ground && idAt <= 0 && (Phase == Phase.Inbound || Phase is Phase.Departing or Phase.Away && (handedOff || Following)) && TerrainAlert(t, now) is { } lm) return lm;   // R114: also radar vectoring, holding, handed-over departure; R128: only after identification
        if (!ground && (Phase is Phase.Inbound or Phase.Entering || radarDep) && now < alertUntil) return new();   // let it give way, no interjecting

        // R115: airfield not reported in sight up to 4 NM before the threshold: no visual approach, procedure or straight-in approach as requested
        if (!ground && visAsk && Phase == Phase.Inbound && Dist(t.X, t.Z, Thr(rw).X, Thr(rw).Z) < 4 * NM)
        {
            visAsk = wantVisual = false;
            return OnTranscript("request straight in approach", t, traffic, now, true);
        }

        // Holding: next (landing sequence position 1–2) or pattern free again -> continue to the airfield
        bool free = holding && !ground && (QueueAhead >= 0 ? Spaced : PatternCount(traffic) < 2) && !OtherEmergency;
        holdFree = free ? (holdFree < 0 ? now : holdFree) : -1;
        // out as soon as the circle roughly points toward the first heading (no reversal turn drifting to the centerline), after 2 min at the latest
        if (free && (now - holdFree > 120 || Plan(t, Ifr || wantStraight) is var pl && AL(t.X, t.Z, pl.Rw) is var (pa, pq) &&
                     (pl.Vec.Count > 0 ? pl.Vec[0] : Pt(pl.Rw, IcptA(pa, pq), 0)) is var p0 && HdgDiff(t.Hdg * 180 / Math.PI, Bearing(t.X, t.Z, p0.X, p0.Z)) < 60))   // direct: toward the intercept point
        {
            Phase = Phase.Inbound;
            fromHold = true;
            var was = Runway;
            var sv = StartVectors(t, Ifr || wantStraight, now);   // R270: still holding: altitude is the assigned level (may be descending there right now), not the current one
            holding = false;
            return Say($"{c}, leave the hold{(vecOver || Runway != was ? ". " + PlanText(was) + Cap(sv) : ", " + sv)}", "Approach");   // R352: other runway than announced: say so before the vector
        }

        // Holding: lead there, let circle at the point, every minute situation (sequence, waiting time) or heading back
        if (holding && !ground && nav is { } hf)
        {
            double d = Dist(t.X, t.Z, hf.X, hf.Z);
            int busy = PatternCount(traffic);
            var seq = QueueAhead >= 0 ? $"you are number {QueueAhead + 1}" : $"{busy} aircraft in the pattern";
            bool byAlert = holdFt == alertFt;   // Altitude came from the traffic warning: back to the level as soon as free there
            double lower = holdArrived ? HoldFt : RouteFt(t, traffic, hf, HoldFt);
            bool Crossed(double ft) => lower < holdFt ? ft < holdFt - 1 && ft > lower - 1000 : ft > holdFt + 1 && ft < lower + 1000;   // Target altitude and everything he crosses on the way there (mptest Sochi IFR: 5000 -> 3000 through a radar-vectored one at 4000, "traffic alert")
            if ((lower < holdFt - 1 || byAlert && lower > holdFt + 1) && LevelFree(t, traffic, lower) &&   // Preceding traffic is out: one level lower
                !traffic.Any(a => a.InAir && !a.Flag.Contains("hold") && Crossed(a.AltMsl / Ft) && SegDist(a.X, a.Z, (t.X, t.Z), hf) < 15 * NM) &&   // not into the altitude of approaching traffic nearby (also on the way to the holding point and there: preceding traffic just out, is being vectored there)
                !traffic.Any(a => a.InAir && a.AsgFt > 0 && Crossed(a.AsgFt) && Dist(a.X, a.Z, CX, CZ) < 25 * NM))   // R270: also not into the assigned altitude of radar-vectored aircraft at the airfield (their path to final leads out to ~20 NM, possibly past the holding: StepDown would otherwise climb over them), R272: also not into that of another holder at the airfield (will soon fly past here at that altitude)
            {
                holdFt = lower; lastHoldInfo = now; alertFt = double.NaN;
                return Say($"{c}, {(byAlert ? "clear of traffic, " : "")}{AltTo(t, holdFt)}, {seq}{EatNote(busy, now, ", ", holdArrived ? 0 : d / (HoldKt * Kt))}.", "Approach");
            }
            if (!holdArrived && d < 1.5 * NM)
            {
                holdArrived = true; lastHoldInfo = now; nav = (t.X, t.Z); devAt.Clear(); ignored = 0;   // Circle around this point (up to 1.5 NM beside the holding point); complied with
                var arrEat = EatNote(busy, now);   // Sequence/waiting time was in the call: here only if the EAT has changed
                return Say($"{c}, {(HoldAt(hf) is { } hr ? $"hold {hr}" : "hold here")}, orbit left hand, {AltTo(t, holdFt)}{HoldSpd(t)}{(arrEat == "" ? "" : $", {seq}, {arrEat}")}.", "Approach");
            }
            // Situation only when needed: en route every 2 min (off course: after 30 s), in the circle only on drift, speed off (every minute) or changed EAT (A50), otherwise radio silence
            double room = Math.Max(3 * NM, 2 * TurnR(t) + NM);   // Circle at 30° bank (diameter 2r, true airspeed) + margin
            bool off = holdArrived ? HoldKtFor() > 0 && Math.Abs(t.Ias / Kt - HoldKtFor()) > 30 : HdgDiff(t.Hdg * 180 / Math.PI, Bearing(t.X, t.Z, hf.X, hf.Z)) > 15;   // in the circle: speed off
            if (now - lastHoldInfo > (!holdArrived ? (off ? 30 : 120) : 60))
            {
                bool drift = holdArrived && d > room;
                if ((drift || off) && d > room && Ignored("hold", d, 0.3 * NM)) return Kick(t);   // does not fly to the holding point. R350: only with a due call on a real deviation (not every second while flying there), counts only if the distance did not shrink
                var eat = EatNote(busy, now, fixS: holdArrived ? 0 : d / (HoldKt * Kt));   // "" = unchanged; sets lastEat, hence only here, where speech also happens below
                if (drift || off || eat != "")   // R321: unchanged en route not the same instruction every 120 s
                {
                    lastHoldInfo = now;
                    if (drift) holdArrived = false;   // drifted: lead back, there again "hold here"
                    var head = !holdArrived ? $", {Steer(t, hf, navName)}, {MilesTxt(d)} to the hold{(Math.Abs(IndFt(t) - holdFt) > 300 ? ", " + AltTo(t, holdFt) : "")}"
                             : off ? HoldSpd(t) : "";   // in the circle on speed deviation only the speed
                    return Say($"{c}{head}{(eat == "" ? "" : $", {seq}, {eat}")}.{(!holdArrived && QueueAhead < 0 ? SeqNote(t, traffic, now) : "")}", "Approach");
                }
            }
            double dh = IndFt(t) - holdFt;   // Left altitude (stack 1000 ft): > 500 ft off and no correction
            if (Math.Abs(dh) > 500 && t.Vs * Math.Sign(dh) > -2.5 && now - Math.Max(lastHoldInfo, lastAltWarn) > 30)
            {
                lastAltWarn = now;
                return Say($"{c}, check altitude, {AltTo(t, holdFt, true)}.", "Approach");
            }
        }

        // Monitor altitude in the control zone (not VFR departure: over 500 ft the Tower has already handed over to Approach, forum log 0.9.4)
        bool ifrDep = Phase == Phase.Departing && depClr;   // R7: IFR departure against the cleared altitude, not against the pattern/zone altitude
        if ((Phase is Phase.Entering or Phase.Initial or Phase.Pattern && option != "low pass" && !sfo || ifrDep) && IndFt(t) > (ifrDep ? depFt : MaxFt) + 400 && t.Vs > -2.5 &&   // A47: low pass from outside; R204: SFO from high key
            Dist(t.X, t.Z, CX, CZ) < CtrM && now - lastAltWarn > 90 && now - lastVecSaid > 30 && now - handoffAt > 60)   // after handoff (with descent) allow 60 s
        {
            lastAltWarn = now;
            return Say(ifrDep ? $"{c}, check altitude, maintain {Alt(depFt)}." : $"{c}, check altitude, pattern altitude {Alt(PatternFt)}.");
        }

        // R7 · Reporting obligation #10: VFR departure after "report leaving the control zone": the pilot reports himself. At the zone edge or departure CRP without report remind once,
        // 1 NM outside (at the earliest 30 s later) "I show you clear of the zone, radar service terminated" and into the debriefing
        if (zoneReport && (ground || Phase != Phase.Away || !handedOff || Following)) zoneReport = false;
        else if (zoneReport && Overdue(Dist(t.X, t.Z, CX, CZ) > CtrM || AtExit(t), Dist(t.X, t.Z, CX, CZ) > CtrM + NM, 30, now, () => Say($"{c}, report leaving the control zone.", "Approach"), () =>
                 {
                     var lm = LeaveZone(c, "I show you clear of the zone, radar service terminated");
                     return Note(Say(lm[0].Text, "Approach"), $"keine Meldung „leaving the control zone“ {F.Name}", $"no report \"leaving the control zone\" {F.Name}");
                 }) is { } zm) return zm;

        // R44 · Reporting obligation #9: missed approach without check-in with Approach: no identification on its own. After 45 s the Tower reminds ("contact Approach now"),
        // 60 s later violation; he continues flying runway heading and altitude (no vectors), a later check-in still gets them (missAt remains)
        if (missAt >= 0 && (ground || Phase != Phase.Inbound || holding)) missAt = -1;
        else if (missAt >= 0 && Overdue(now - missAt > 45, true, 60, now, () => { role = "Tower"; return Say($"{c}, {ContactNow("Approach")}.", "Tower"); },
                     () => { role = "Tower"; return Dev($"{c}, no contact with {F.StationOf("Approach")}, continue runway heading, maintain {Alt(missFt)}, {ContactNow("Approach")}.",
                                                       $"kein Check-in bei Approach nach dem Fehlanflug {F.Name}", $"no check-in with approach after missed approach {F.Name}", "Tower", fac: "Approach", by: "Tower"); }) is { } om) return om;

        // MAYDAY of another within 15 NM: whoever is not already on short final leaves radar vectoring or pattern and holds (runway free)
        if (!ground && EmgWithin(15) && !holding && !Emergency && Phase is Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern &&
            !OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 3 * NM))
            return EmergencyHold(t, traffic, now);

        // A13: approach aborted without release (flying away): from the zone once "say intentions", 3 NM further release (order free, pin released)
        if (!ground && !Emergency && Phase is Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand)
        {
            double fd = Dist(t.X, t.Z, CX, CZ);
            if (Phase != minPh || minDist < 0) (minPh, minDist, leaveAt) = (Phase, fd, -1);
            minDist = Math.Min(minDist, fd);
            if (leaveAt >= 0 && fd > leaveAt + 3 * NM)
            {
                var what = Phase == Phase.ClearedLand ? "landing clearance" : "approach";
                CancelApproach(t);
                Released = true; Following = false;   // Radar service ends (Flight Following from en route came along on "inbound")
                return Say($"{c}, {what} cancelled, frequency change approved.");
            }
            var np = navName == "six mile final" ? OnCenterline(Runway, 6 * NM) : nav;   // instructed point outside (six mile final lies beyond the 5-NM zone without map): up to there + 3 NM
            if (leaveAt < 0 && !extended && fd > Math.Max(CtrM, Math.Max(minDist, np is { } ng ? Dist(CX, CZ, ng.X, ng.Z) : 0) + 3 * NM))
            {
                leaveAt = fd;
                return Say($"{c}, I show you leaving the control zone, say intentions.");
            }
        }
        else (minDist, leaveAt) = (-1, -1);

        if (!ground && VecTick(t, traffic, now) is { } vm) return vm;   // Radar vectoring
        // R216: traffic advisories under radar vectoring, in holding, on approach to the C R P and after the departure check-in (not before the check-in after the missed approach)
        if (!ground && (Phase == Phase.Inbound && missAt < 0 || Phase == Phase.Away && handedOff && !Following && idAt <= 0) && TrafficInfo(t, traffic, now) is { } ti) return ti;

        // Reporting obligation: handed over, but not reported to the Tower: after 60 s or 5 NM before the airfield once Approach "contact Tower now" (landing clearance only on report, otherwise go around)
        // shared map frequency (K2): no frequency change, so no "contact Tower now" (there "report initial/overhead" and the landing report apply)
        if (!ground && towerDue && !towerNag && Phase == Phase.Entering && Math.Abs(FreqOf(F, "Tower") - FreqOf(F, "Approach")) >= 0.005 && (now - handoffAt > 60 || now - handoffAt > 20 && Dist(t.X, t.Z, CX, CZ) < 5 * NM))
        {
            towerNag = true;
            return Say($"{c}, contact {F.StationOf("Tower")} now, {FreqSay(FreqOf(F, "Tower"))}.", "Approach");
        }

        // Leaving airfield airspace (15 NM): Departure releases (V14: end of the Departure vector), with AWACS in the mission handoff there
        if (popM > 0) popM = Math.Min(popM, Dist(t.X, t.Z, CX, CZ) + 5 * NM);   // R113
        if (!ground && Phase == Phase.Away && handedOff && !Following && Dist(t.X, t.Z, CX, CZ) > Math.Max(15 * NM, popM))
        {
            bool ifr = depClr && nav != null;
            var end = AwacsContact is { } aw ? $"contact {aw}." : depClr ? "frequency change approved." : SquawkVfr() + "frequency change approved.";
            var head = ifr ? $"proceed direct {navName}, resume own navigation. Radar service terminated" : "radar service terminated";
            var ho = IfrHandoff();
            var bye = Say(ho != null ? $"{c}, {ho}" : $"{c}, {head}, {end}", "Approach");   // still as Departure (Dep)
            depClr = false; nav = null; handedOff = false; Released = true;
            return bye;
        }

        // Navigation: target reached -> next instruction, otherwise heading on large deviation
        if (!ground && nav is { } goal)
        {
            double d = Dist(t.X, t.Z, goal.X, goal.Z);
            // Reporting obligation C R P: handoff only on the call "C R P north" (case crp); at the point without call once "report C R P", 2 NM later once "say position", no handoff
            if (!holding && Phase == Phase.Inbound && entry != null && d < NM && !crpAsked) { crpAsked = true; return Say($"{c}, report C R P {entry}.", "Approach"); }
            if (!holding && Phase == Phase.Inbound && entry != null && crpAsked && !crpWaved && d > 2 * NM)
            {
                crpWaved = true; nav = null; (keepAt, keepRef, keepTold) = (now, Dist(goal.X, goal.Z, CX, CZ), false);
                return Say($"{c}, say position, remain outside the control zone.", "Approach");
            }
            // Break clearance only on the call "initial" (as in reality: "report initial" -> pilot reports -> break clearance "runway 25, report base"); at Initial arrived without call ask once (R31: only on the axis)
            if (Phase == Phase.Entering && navName == "initial" && d < NM && Approach(t.X, t.Z, rw) is var (ia, il) && ia <= InitialDist && il < InitialLat && !iniAsked) { iniAsked = true; return Say($"{c}, {F.StationOf("Tower")}, report initial."); }
            // still without call further toward the break (1 NM past the Initial): let go around once, new via the Initial (heading afterwards as usual)
            if (Phase == Phase.Entering && navName == "initial" && iniAsked && !iniWaved && Approach(t.X, t.Z, rw).along < InitialDist - NM)
            {
                iniWaved = true; lastVector = now;
                return Say($"{c}, no break clearance, continue straight through, re-enter initial runway {RwSay(rw)}, report initial.");
            }
            // Reporting obligation Overhead: join only on the call "overhead" (case overhead); over the airfield without call once "report overhead", 1.5 NM further once "no clearance to join"
            if (Phase == Phase.Entering && navName == "overhead" && d < 0.7 * NM && !ovhAsked) { ovhAsked = true; return Say($"{c}, {F.StationOf("Tower")}, report overhead."); }
            if (Phase == Phase.Entering && navName == "overhead" && ovhAsked && !ovhWaved && d > 1.5 * NM)
            {
                ovhWaved = true; nav = null; (keepAt, keepTold) = (now, false);
                return Say($"{c}, no clearance to join, remain outside the pattern, say intentions.");
            }
            if (navName == "six mile final" && (d < 1.5 * NM || OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 7 * NM))) nav = null;
            else if (!holding && Phase != Phase.ClearedLand && leaveAt < 0 && now - lastVector > 45 && d > 1.5 * NM && HdgDiff(t.Hdg * 180 / Math.PI, Bearing(t.X, t.Z, goal.X, goal.Z)) > 30 &&
                     !(Phase is Phase.ClearedTakeoff or Phase.Departing && (Dist(t.X, t.Z, CX, CZ) < 2 * NM || handedOff)))   // Reporting obligation #8: handed over, no check-in yet: Departure does not vector; landing clearance (e.g. emergency on the way to the initial), A13 after "say intentions": no heading any more
                return Say($"{c}, {Steer(t, goal, navName)}, {MilesTxt(d)} to {navName}.");
        }
        // Review l1: after "no clearance to join, remain outside the pattern" or "say position, remain outside the control zone" nevertheless (45 s later still or again) in the pattern
        // (3 NM, under pattern + 1000 ft) or further in the zone than the C R P: once redirect with violation (debriefing, "possible pilot deviation", FAA JO 7110.65 2-1-26) like the zone watch
        if (!ground && !keepTold && now - keepAt > 45 && Dist(t.X, t.Z, CX, CZ) is var kd &&
            (ovhWaved && Phase == Phase.Entering && navName == "overhead" && kd < 3 * NM && IndFt(t) < PatternFt + 1000 ||
             crpWaved && !holding && Phase == Phase.Inbound && entry != null && kd < Math.Min(CtrM, keepRef - NM)))
        {
            keepTold = true;
            return ovhWaved ? Dev($"{c}, you are not cleared to join, leave the pattern immediately, say intentions.", $"Platzrunde ohne Freigabe {F.Name}", $"pattern entry without clearance {F.Name}", "Tower")
                            : Dev($"{c}, you are inside the {F.Name.Replace('-', ' ')} control zone without clearance, leave the control zone immediately, say intentions.",
                                  $"Kontrollzone ohne Freigabe {F.Name}", $"control zone entered without clearance {F.Name}", "Approach");
        }

        // Pattern: side, altitude, extend downwind / base
        if (!ground && Phase is Phase.Initial or Phase.Pattern && (Dist(t.X, t.Z, CX, CZ) < 5 * NM || extended))   // R324: extended downwind also over 5 NM (base call promised)
        {
            var (al, _) = Approach(t.X, t.Z, rw);
            double side = PatternOffset(t.X, t.Z, rw);
            bool downwind = side > 700 && side < 5000 && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw) + 180) < 35;
            if (downwind || StraightIn(t, rw)) Phase = Phase.Pattern;   // Break flown (or without break on short final with landing cues): only now landing clearance on final (Initial lies in the 3-NM zone)
            if (side < -900 && (Phase == Phase.Pattern || HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw) + 180) < 60) && now - lastPatternWarn > 60)   // Initial: only on downwind
            {
                lastPatternWarn = now;
                return Say(F.Charted
                    ? $"{c}, pattern is south of the runway, {Hand(rw)} pattern runway {RwSay(rw)}."
                    : $"{c}, wrong side, {Hand(rw)} pattern runway {RwSay(rw)}.");
            }
            if (downwind && Math.Abs(IndFt(t) - PatternFt) > 500 && t.Vs * Math.Sign(IndFt(t) - PatternFt) > -2.5 && now - lastAltWarn > 60)
            {
                lastAltWarn = now;
                return Say($"{c}, check altitude, pattern altitude {Alt(PatternFt)}.");
            }
            // R330: on base too close behind traffic on final (it would still be on the runway at our arrival): once full circle instead of a late go-around (FAA JO 7110.65 3-8-1)
            if (Phase == Phase.Pattern && !Ifr && !s360 && OnBase(t, rw) && traffic.Where(a => OnFinalTr(a, rw) && Approach(a.X, a.Z, rw).along < al).MaxBy(a => Approach(a.X, a.Z, rw).along) is { } b360 &&
                Approach(b360.X, b360.Z, rw).along / b360.Speed + 50 > (Math.Max(al, 0) + Math.Abs(side)) / Math.Max(t.Ias, 40))
            { s360 = true; return Say($"{c}, make {Hand(rw).Split(' ')[0]} three-sixty, number 2, follow the {Describe(b360, rw)}, report final."); }
            if (downwind && !baseCalled && al > 1500)
            {
                // Preceding traffic on final: base only when we (base + final) arrive after its landing + 60 s runway occupancy
                var lead = traffic.FirstOrDefault(a => a.Speed > 30 && OnFinal(a.X, a.Z, a.Hdg, a.AltMsl - FieldElev, rw, 4 * NM) &&
                                                       Approach(a.X, a.Z, rw).along / a.Speed + 60 > (al + side) / Math.Max(t.Ias, 40));
                if (lead != null)
                {
                    if (!extended) { extended = true; var fin = traffic.Where(a => OnFinalTr(a, rw)).ToList(); return Say($"{c}, extend downwind, number {fin.Count + 1}, traffic {Describe(fin.MaxBy(a => Approach(a.X, a.Z, rw).along) ?? lead, rw)}, I will call your base."); }   // R324: number and last preceding traffic
                }
                else if (extended || al > 2800 + 80 * Math.Max(0, TailKt(t, rw)))   // Tailwind (opposite runway due to terrain): final pushes toward the threshold, turn in later
                {
                    baseCalled = true;   // without extension the pilot turns by himself (no "turn base" in every circuit)
                    bool ext = extended;
                    extended = false;
                    // R207: describe preceding traffic (the last one ahead of me on final) (FAA JO 7110.65 3-8-1), if he is already down or gone, without "number"
                    var ahead = traffic.Where(a => a.Speed > 30 && OnFinal(a.X, a.Z, a.Hdg, a.AltMsl - FieldElev, rw, 4 * NM) && Approach(a.X, a.Z, rw).along < al).ToList();
                    finAsked |= ext;   // "report final" is already in the base call (reporting obligation: not again right away)
                    if (ext) return Say(ahead.Count > 0 ? $"{c}, turn base, number {ahead.Count + 1}, follow the {Describe(ahead.MaxBy(a => Approach(a.X, a.Z, rw).along)!, rw)}, report final." : $"{c}, turn base, report final.");
                }
            }
        }

        // N2: Airspace watch (Away without contact, up to CtrTopFt; silent with enemy under 20 NM and in the first 60 s after liftoff); armed again only after zone + 1 NM
        // N3: Transit: monitor altitude (at most every 60 s), release without report outward at zone + 1 NM
        if (Phase != Phase.Away) transitFt = null;
        if (!ground && transitFt is { } tf)
        {
            double td = Dist(t.X, t.Z, CX, CZ);
            transitIn |= td < CtrM;
            // Reporting obligation #12: "report clear of the zone" at zone + 0.5 NM, without report from zone + 1 NM (at the earliest 30 s later) release and into the debriefing
            if (transitIn && Overdue(td > CtrM + 0.5 * NM, td > CtrM + NM, 30, now, () => Say($"{c}, report clear of the zone.", transitBy), () =>
                {
                    transitFt = null; Released = true;
                    return Note(Say($"{c}, I show you clear of the zone, frequency change approved.", transitBy), $"keine Meldung „clear of the zone“ (Durchflug {F.Name})", $"no report \"clear of the zone\" (transit {F.Name})");
                }) is { } xm) return xm;
            if (td < CtrM && IndFt(t) < tf - 300 && t.Vs < 2.5 && now - lastAltWarn > 60) { lastAltWarn = now; return Say($"{c}, check altitude, not below {Alt(tf)}, pattern traffic below.", transitBy); }
        }

        if (Phase != Phase.Away || Dist(t.X, t.Z, CX, CZ) > CtrM + NM) intr = 0;
        if (!ground && Phase == Phase.Away && transitFt == null && Watch && !Exempt && !Emergency && HostileNm > 20 && Aloft > 60 && IndFt(t) < F.CtrTopFt && WatchTick(t, traffic, now) is { } wm) { WatchSaid = true; return wm; }   // R294: announced emergency (AWACS, Announce) is not sent away

        // Final: landing clearance after the landing report, go-around with occupied runway or without report, glide path calls
        // R11: without initial call (Away) only with landing cues (go around), released and in transit (N3) never; Tower names itself (first contact)
        bool unannounced = Phase == Phase.Away;
        if (!ground && Phase is not (Phase.ClearedTakeoff or Phase.Departing) && !(unannounced && !Emergency && (optedOut || transitFt != null || !LandingCues(t))))
        {
            if (unannounced) { role = "Tower"; c += ", " + F.StationOf("Tower"); }
            if (Phase != Phase.Inbound) rw = Aligned(t);   // Parallel runway approached: this one (radar vectoring stays with its runway)
            if (rwSwitched)   // R334: with landing clearance aligned to the parallel runway (FAA JO 7110.65 3-10-5 c); occupied: go-around check below
            {
                rwSwitched = false;
                if (Phase == Phase.ClearedLand && !TrafficOnRunway(traffic)) return Say($"{c}, you appear to be aligned with runway {RwSay(rw)}, runway {RwSay(rw)}, {Wind(t)}, {ClearText()}.", "Tower");
            }
            var (along, alat) = Approach(t.X, t.Z, rw);
            // A12/Reporting obligation: silent go-around with landing clearance (300 m past the threshold still over 60 m): ask, the clearance lapses, debriefing
            if (Phase == Phase.ClearedLand && !Emergency && option is not ("touch and go" or "low approach") && along < -300 && alat < 0.5 * NM && t.Agl > 60)
            {
                lastGa = now;
                if (Ifr || wantStraight) return Note(Missed(t, now, "I show you going around, confirm?"), $"Durchstarten ohne Meldung {F.Name} {rw}", $"go-around without report {F.Name} {rw}");
                Phase = Phase.Pattern; PatternReset();
                return Note(Say($"{c}, I show you going around, confirm? Climb and maintain {Alt(PatternFt)}, join {Hand(rw)} downwind runway {RwSay(rw)}, report base."),
                            $"Durchstarten ohne Meldung {F.Name} {rw}", $"go-around without report {F.Name} {rw}");
            }
            var other = F.Ends.Select(e => e.Name).FirstOrDefault(e => HdgDiff(LandHdg(e), LandHdg(rw)) > 10 && OnFinal(t.X, t.Z, t.Hdg, t.Agl, e, 5 * NM));   // Parallel runway: Aligned
            if (!wrongRwyTold && Phase != Phase.ClearedLand && other != null && !ViaOther(other) && IndFt(t) < PatternFt - 300)   // at pattern altitude (overhead via the opposite direction): no approach yet
            {
                if (!unannounced) { Phase = Phase.Pattern; PatternReset(); }   // also the navigation to the overhead/Initial: otherwise forever "fly heading … to overhead"; Away remains (R11)
                wrongRwyTold = true;
                return Say($"{c}, check runway! You are lined up for runway {RwSay(other)}, runway {RwSay(rw)} in use. " +
                           $"Join {Hand(rw)} downwind runway {RwSay(rw)}, {Alt(PatternFt)}.");
            }
            if (along > 9 * NM) { glide.Clear(); gearTold = false; }
            // Gear (only own aircraft known): still up shortly before landing -> like a real Tower "check wheels down"
            if (!gearTold && t.Gear is >= 0 and < 0.5 && option is not ("low approach" or "low pass") && along > 0.3 * NM && along < 2.5 * NM && t.Agl < 250 &&
                OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 3 * NM))
            {
                gearTold = true;
                return Say($"{c}, check wheels down!");
            }
            // own emergency: landing clearance from the Tower already at approx. 5 NM (after radar vectoring or from the pattern)
            if (Emergency && Phase is Phase.Entering or Phase.Initial or Phase.Pattern or Phase.Away && OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 5.5 * NM) && !TrafficOnRunway(traffic))
                return Landing(t, traffic, c);
            bool onRwy = TrafficOnRunway(traffic), busy = onRwy || EmgWithin(8);   // R37: foreign MAYDAY from 8 NM blocks new landing clearances, go-around only under 4 NM
            var why = onRwy ? BusyWhy(traffic) : "emergency traffic";
            // A11: go-around with occupied runway also without landing clearance (Entering, Initial deep in final, Pattern), not Away (R11)
            // afterwards 60 s quiet on final: otherwise per tick again "go around" or in the climb right away "continue approach"/"cleared to land"
            bool gaQuiet = now - lastGa < 60;
            if (!gaQuiet && !unannounced && (onRwy || !Emergency && EmgWithin(4)) && along < 0.8 * NM && (Phase != Phase.Initial || t.Agl < 150) && OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 3 * NM))
            {
                lastGa = now;
                wentAround |= Phase == Phase.ClearedLand;   // N4: landing clearance withdrawn; new approach: too-low warning possible again (A97)
                return Ifr || wantStraight ? Missed(t, now, $"go around, I say again, go around, {why}.")   // R12: instrument approach -> missed approach instead of traffic pattern
                                           : GoAround(c, $"go around, I say again, go around, {why}");
            }
            bool fin3 = OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 3 * NM);
            // Review l1: still with Approach (Inbound, without handoff) on final under 3 NM: handoff to the Tower as in normal procedure, landing clearance only on the report
            // (without report "contact Tower now", "report final", under 0.8 NM go around)
            if (Phase == Phase.Inbound && !holding && !Emergency && !visAsk && missAt < 0 && !gaQuiet && fin3)
            {
                Phase = Phase.Entering; nav = null; vec = null; navName = "six mile final";
                handoffAt = now; towerDue = true; towerNag = landCall = finAsked = false;
                return Say($"{c}, contact {Contact("Tower")}, report final.", "Approach");
            }
            // Reporting obligation (R11): without any contact (Away with landing cues) never a landing clearance, under 1 NM go around and violation
            if (unannounced && !Emergency && !gaQuiet && fin3 && along < NM)
            {
                lastGa = now;
                return Dev($"{(Unknown ? $"Aircraft on final runway {RwSay(rw)}" : Cs())}, {F.StationOf("Tower")}, go around, you are not cleared to land.",
                           $"Anflug ohne Kontakt {F.Name} {rw}", $"approach without contact {F.Name} {rw}", "Tower");
            }
            // Reporting duty: landing clearance only on the landing report (base, final, four mile final), then automatically once runway and preceding traffic are clear.
            // Without a report once "report final" (final 2.5 NM or base), below 0.8 NM or 300 ft "go around, no landing clearance" and debriefing
            bool appr = !unannounced && !Emergency && Phase is Phase.Entering or Phase.Pattern && !(Phase == Phase.Entering && navName == "initial");   // on the way to the break: the "initial" call comes first
            if (appr && !landCall && !gaQuiet && fin3 && (along < 0.8 * NM || t.Agl < 300 * Ft))
            {
                lastGa = now;
                return Note(Ifr || wantStraight ? Missed(t, now, "go around, no landing clearance.") : GoAround(c, "go around, no landing clearance"),
                            $"keine Landemeldung {F.Name} {rw}", $"no landing report {F.Name} {rw}");
            }
            if (appr && !landCall && !finAsked && !gaQuiet && (fin3 && along < 2.5 * NM || Phase == Phase.Pattern && OnBase(t, rw) && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 100))
            {
                finAsked = true;
                return Say($"{c}, report final.");
            }
            if (!unannounced && landCall && Phase != Phase.Initial && !gaQuiet && fin3 && Phase != Phase.ClearedLand)
            {
                if (busy)
                {
                    if (!rwyBusyTold) { rwyBusyTold = true; return Say($"{c}, continue approach, {why}."); }
                }
                else if (AheadOfMe(t, traffic, rw).Count == 0)
                {
                    Phase = Phase.ClearedLand;
                    rwyBusyTold = false;
                    return Say($"{c}, {Wheels()}{Wake(t, traffic, rw)}runway {RwSay(rw)}, {Wind(t)}, {ClearText()}{EmgNote()}.");
                }
            }
            // Precision approach like GCA/PAR: only for the straight-in approach from radar vectoring with PAR clearance. ILS, visual/straight-in (visual) and
            // visual traffic pattern (break) get no glidepath/course callouts, only the low-altitude warning once
            bool par = navName == "six mile final" && Phase != Phase.Pattern && (Proc(rw) == "P A R" || Emergency && Proc(rw) == "") && !wantVisual && !visAsk;   // Emergency without ILS: distance/glidepath like PAR; R115: no approach callouts before the visual report
            var lat = Approach(t.X, t.Z, rw).lateral;
            if (along > 0.3 * NM && along < 8.5 * NM && lat < 150 + along * 0.18 && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 30)
            {
                // 3° glidepath, threshold crossing height 15 m
                double dev = (t.AltMsl - FieldElev - (along * Math.Tan(3 * Math.PI / 180) + 15)) / Ft;
                if (along < 3 * NM) glide.Add((dev, lat));
                // FAA JO 7110.65 2-1-6: low-altitude warning only for checked-in aircraft (not Away/Initial), once per approach (finalLowTold, PatternReset)
                if (along < 2.5 * NM && dev < -200 && !finalLowTold && option != "low pass" && Phase is Phase.Entering or Phase.Pattern or Phase.ClearedLand)
                {
                    finalLowTold = true;
                    lastLow = lastGuide = now;
                    return Say($"Low altitude alert, {c}, check your altitude immediately.");   // R214: wake word first
                }
                // as with ILS the controller only monitors: callout only when well off, at most every 20 s. Course more than 2.5° off;
                // glidepath more than 300 ft off only within 5 NM (before that it descends to the initial altitude below the glidepath), not in level flight below it (ILS from below)
                var course = CourseText(t, rw, along);
                bool glideOff = Math.Abs(dev) > 300 && along < 5 * NM && !(dev < 0 && t.Vs > -1);
                if (par && (glideOff || !course.StartsWith("on") && !course.StartsWith("slightly")) && along > 0.6 * NM && now - lastGuide > 20)
                {
                    lastGuide = now;
                    int mile = (int)Math.Round(along / NM);
                    return Say($"{c}, {DigitWords[mile]} mile{(mile > 1 ? "s" : "")}, {(glideOff ? $"{GlideText(dev)}{GlideFix(dev)}, " : "")}{course}.");
                }
            }
        }

        // Landing: during rollout (2 s on ground, below 100 kt) announce the vacating taxiway; touch-and-go only once it is really braking
        if (airborne && ground && now - touchAt >= 2 && kt < (option is "touch and go" or "the option" ? 50 : 100) && Dist(t.X, t.Z, CX, CZ) < 3000)
        {
            airborne = false;
            bool unclr = Phase != Phase.ClearedLand && !Emergency, ga = wentAround;   // N4: without landing clearance (emergency allowed)
            Phase = Phase.TaxiIn;
            landedAt = now;
            rwyBusyTold = Emergency = stayPattern = taxiInTold = wxTold = wentAround = false;
            wantStraight = wantVisual = wantOverhead = visAsk = visSeen = fromHold = false;   // R325: approach requests apply only to this landing
            option = wantRw = null;
            PatternReset();
            devTold.Clear();   // new flight: violations individually again
            var landed = landRw = RwyOf(t);
            // Following traffic within 3 NM on final for the same runway: vacate promptly (ICAO Doc 4444 12.3.4), once per landing
            var follow = Follower(traffic, landed, Gs(t), now);   // R331: over ground (headwind shows IAS at standstill)
            if (follow != null) expFor = now;
            if (follow == null && !unclr && Gs(t) >= 40 * Kt) { vacDue = true; return new(); }   // R333: vacate instruction not right after touchdown (FAA JO 7110.65 3-10-9 Note), only when slow
            var vac = Vacate(landed);   // R210: no "welcome to ...", straight to the vacate instruction
            var end = SameFreq("Ground") ? "report vacated." : $"contact {Contact("Ground")} when vacated.";   // R104
            vacAt = now;   // R354
            var wel = follow == null ? $"{c}, {char.ToLower(vac[0])}{vac[1..]} {Cap(end)}"
                    : $"{c}, expedite vacating, traffic {Describe(follow, landed)}, {(vac.EndsWith("when able.") ? end : $"{char.ToLower(vac[0])}{vac[1..]} {Cap(end)}")}";   // R266: without a taxiway no "vacate runway when able" after "expedite vacating"
            return !unclr ? Say(wel, "Tower") : ga ? Dev(wel, $"Landung nach Go-around {F.Name} {landed}", $"landing after go-around {F.Name} {landed}", "Tower")
                : Dev(wel, $"Landung ohne Freigabe {F.Name} {landed}", $"landing without clearance {F.Name} {landed}", "Tower");
        }

        // A48/A83: still on the runway (rolled out slowly, standing) and the following traffic only now comes within 3 NM: vacate promptly, once per landing
        if (Phase == Phase.TaxiIn && ground && expFor != landedAt && OnRunwayPos(t.X, t.Z) && Follower(traffic, landRw, Gs(t), now) is { } fo)
        {
            expFor = landedAt; vacAt = now;
            return Say($"{c}, expedite vacating, traffic {Describe(fo, landRw)}.", "Tower");
        }
        // R357: MAYDAY of another inside 5 NM while still on the runway: vacate (FAA JO 7110.65 3-10-5), once per landing
        if (Phase == Phase.TaxiIn && ground && expFor != landedAt && OnRunwayPos(t.X, t.Z) && EmgWithin(5))
        {
            expFor = landedAt;
            return Say($"{c}, vacate runway immediately, emergency traffic {MilesTxt(EmergencyNm * NM)}.", "Tower");
        }
        if (vacDue && Phase == Phase.TaxiIn && ground && Gs(t) < 40 * Kt)
        {
            vacDue = false; vacAt = now;
            var vac = Vacate(landRw);
            return Say($"{c}, {char.ToLower(vac[0])}{vac[1..]} {Cap(SameFreq("Ground") ? "report vacated." : $"contact {Contact("Ground")} when vacated.")}", "Tower");
        }

        double gkt = Gs(t) / Kt;   // R263: taxi speed/standstill over ground, not IAS
        // Taxiing without taxi clearance (Parked/StartupApproved, over 5 kt and over 100 m from the parking position): Ground holds. Say, not Neg: no denial; on the runway it is the tower's business
        if (ground && !OnRunwayPos(t.X, t.Z) && (Phase is Phase.Parked or Phase.StartupApproved) && gkt > 5 && spot is { } rs && Dist(t.X, t.Z, rs.X, rs.Z) > 100 && Dist(t.X, t.Z, CX, CZ) <= LocalM && now - lastRollWarn > 120)
        {
            lastRollWarn = now;
            return Say($"{c}, hold position, say intentions.", "Ground");
        }

        // Taxi traffic: crossing/oncoming ground traffic -> stop, then resume taxiing.
        // Not on the runway (rollout: the pilot cannot stop, runway traffic is the tower's business) and not because of traffic on the runway.
        // R262: only with taxi clearance ("continue taxi" presupposes one); not after "start up approved" or before the Ground call after landing (ICAO Doc 4444 7.6)
        if (ground && !OnRunwayPos(t.X, t.Z) && (Phase == Phase.TaxiOut || Phase == Phase.TaxiIn && taxiInTold))
        {
            var taxiTraffic = traffic.Where(a => !OnRunwayPos(a.X, a.Z)).ToList();
            if (taxiFollow is { } fid && taxiTraffic.Where(a => a.Id == fid).ToList() is var ft && TaxiConflict(t, ft) == null && StandConflict(t, ft) == null) taxiFollow = null;   // passed: silent, he was not stopped
            var others = taxiTraffic.Where(a => a.Id != taxiFollow).ToList();
            var conflict = TaxiConflict(t, others);
            var stand = conflict == null ? StandConflict(t, others) : null;   // R342: stationary traffic in the taxi path
            conflict ??= stand;
            if (holdFor is { } hid)
            {
                // R106: only after 8 s halt and when the traffic is gone or (over 60 m or no longer in his own path, computed with taxi movement) and not approaching (regardless of speed);
                // a stationary one in the taxiway remains an obstacle, one beside/behind does not; never while any traffic is in conflict (otherwise "continue taxi" and immediately "hold position" again)
                var h = taxiTraffic.FirstOrDefault(a => a.Id == hid);
                double hd = h == null ? double.MaxValue : Dist(t.X, t.Z, h.X, h.Z);
                bool gone = h == null || (hd > 60 || !(TaxiMiss(t, h, Math.Max(Gs(t), 15 * Kt)) is var (hm, htc) && hm < 40 && htc > 0)) && hd >= holdDist - 0.5;
                holdDist = hd;
                if (now - holdAt >= 8 && gone && conflict == null)
                {
                    holdFor = null;
                    return Say($"{c}, continue taxi.", "Ground");
                }
            }
            else if (conflict != null && gkt > 3)
            {
                double rel = ((Bearing(t.X, t.Z, conflict.X, conflict.Z) - t.Hdg * 180 / Math.PI) % 360 + 360) % 360;
                var side = rel < 30 || rel > 330 ? "ahead" : rel < 180 ? "on your right" : "on your left";
                if (stand != null)   // R342: holding short ahead -> follow (queue at the holding point), otherwise stop behind it
                {
                    if (AtHoldPt(stand, t))
                    {
                        taxiFollow = stand.Id;
                        return Say($"{c}, follow the {Ops.TypeSay(stand.Type)} holding short runway {RwSay(Runway)}.", "Ground");
                    }
                    (holdFor, holdAt, holdDist) = (stand.Id, now, Dist(t.X, t.Z, stand.X, stand.Z));
                    return Say($"{c}, hold position, traffic ahead, {Ops.TypeSay(stand.Type)}.", "Ground");
                }
                if (side == "ahead" && HdgDiff(t.Hdg * 180 / Math.PI, conflict.Hdg * 180 / Math.PI) < 45)   // same direction: follow, no halt
                {
                    taxiFollow = conflict.Id;
                    return Say($"{c}, follow the {Ops.TypeSay(conflict.Type)} ahead.", "Ground");
                }
                (holdFor, holdAt, holdDist) = (conflict.Id, now, Dist(t.X, t.Z, conflict.X, conflict.Z));
                return Say($"{c}, hold position, give way to the {Ops.TypeSay(conflict.Type)} {side}.", "Ground");
            }
        }
        else { holdFor = null; taxiFollow = null; }

        // A91: left the runway but no Ground call: remind once 30 s after vacating (ICAO Doc 4444 12.3.4)
        if (Phase == Phase.TaxiIn && ground && !OnRunwayPos(t.X, t.Z) && vacatedAt <= landedAt) vacatedAt = now;   // 30 s from vacating, not from landing (long rollout)
        // Reporting duty #13: parking without taxi clearance over 5 kt and 150 m off the runway: have him stop, violation (once per landing); R354: only 10 s after the vacate instruction (FAA JO 7110.65 3-10-9)
        if (Phase == Phase.TaxiIn && !taxiInTold && ground && gkt > 5 && !OnRunwayPos(t.X, t.Z, 180, 150) && taxiDevFor != landedAt && !vacDue && now - vacAt >= 10)
        {
            taxiDevFor = vacRemFor = landedAt;   // no A91 reminder afterwards
            return Dev($"{c}, hold position, you are not cleared to taxi. {(SameFreq("Ground") ? "Request taxi." : $"Contact {Contact("Ground")} for taxi.")}",
                       $"Rollen ohne Freigabe {F.Name}", $"taxi without clearance {F.Name}", SameFreq("Ground") ? "Ground" : "Tower");
        }
        if (Phase == Phase.TaxiIn && !taxiInTold && ground && !OnRunwayPos(t.X, t.Z) && now - vacatedAt > 30 && vacRemFor != landedAt)
        {
            vacRemFor = landedAt;
            return Say($"{c}, {(SameFreq("Ground") ? "request taxi" : $"contact {Contact("Ground")}")}.", "Tower");
        }

        if (ground && gkt > 30 && now - lastTaxiWarn > 60 && !OnRunwayPos(t.X, t.Z) &&
            (Phase is Phase.TaxiOut or Phase.HoldShort || (Phase == Phase.TaxiIn && now - landedAt > 45)))
        {
            lastTaxiWarn = now;
            return Say($"{c}, reduce taxi speed.");
        }

        // Taxi-in: Ground guides to own parking position (direction + distance; DCS-ATC does not know the taxiway network).
        // A36: route once, afterwards only on request (progressive, N26: at each halving of the distance) or on deviation (distance growing for 20 s)
        if (ground && lastParkCall > 0 && (Phase == Phase.TaxiIn && taxiInTold || Phase == Phase.TaxiOut && progressive) && ProgTarget(t) is { } tg)
        {
            double d = Dist(t.X, t.Z, tg.X, tg.Z);
            bool inn = Phase == Phase.TaxiIn;
            if (progLast > 1e8) progLast = d;   // first distance = that of the reply
            if (inn && d < 20 && gkt < 2)
            {
                lastParkCall = -1;   // R211: once and silent – Ground does not report the stand, that is the crew chief's business (N25)
            }
            if (d >= 20 && gkt > 2 && now - lastParkCall > 10 && ParkDir(t, tg) is { } pd)
            {
                if (d < progMin) { progMin = d; growSince = -1; }
                else if (d <= progMin + 15) growSince = -1;
                else if (growSince < 0) growSince = now;
                bool drift = growSince > 0 && now - growSince >= 20;
                bool near = progressive && !progNear && d < 150, half = progressive && d < progLast * 0.55 && d >= 150;
                if (drift || near || half)
                {
                    lastParkCall = now; progLast = progMin = d; growSince = -1;
                    progNear |= near;
                    bool hold = near && !inn;   // Holding point reached: final announcement, then silent
                    if (hold) progressive = false;
                    return Say($"{c}, {ProgName(Runway)} {(drift ? "is now " : hold ? "" : "now ")}{pd}{(hold ? $", hold short of runway {RwSay(Runway)}" : "")}.", "Ground");
                }
            }
        }

        // R6/N25: 30 s standstill on the apron (within 100 m of a parking position) = parked, not when waiting on the taxiway
        // and not when waiting for "continue taxi" (holdFor, R106; the 30 s only count afterwards); engine off at the stand: EngineOff
        if (Phase == Phase.TaxiIn && ground && gkt < 1)
        {
            if (stoppedSince < 0 || holdFor != null) stoppedSince = now;
            else if (now - stoppedSince > 30 && AtSpot(t, 100)) Park();
        }
        else stoppedSince = -1;
        return new();
    }

    bool wxTold;   // Visibility/ceiling stated at first contact (A145), once per approach
    /// Bad weather at first contact: visibility below 5 km or ceiling over the airfield below 1000 ft, below 550 m / 200 ft additionally "say intentions".
    string WxNote()
    {
        var (clouds, baseM, visM) = Carrier.Sky;
        double ceil = clouds ? (baseM - F.Elev) / Ft : double.MaxValue;
        if (wxTold || (visM >= 5000 && ceil >= 1000)) return "";
        wxTold = true;
        var vis = visM >= 5000 ? "" : visM >= 1000 ? $" Visibility {Math.Round(visM / 1000)} kilometers" : $" Visibility {Math.Round(visM / 50) * 50} meters";
        var cl = ceil >= 1000 ? "" : $"{(vis == "" ? " Ceiling" : ", ceiling")} {Alt(Math.Max(0, Math.Round(ceil / 100) * 100))}";
        return $"{vis}{cl}.{(visM < 550 || ceil < 200 ? " Say intentions." : "")}";
    }
    void Park() { wxTold = false; special = null; Phase = Phase.Parked; exitDir = null; entry = null; Separate = false; depClr = false; dest = null; Following = false; lowTold = lowAck = false; Squawk = null; cruiseFt = 0; }

    /// R128: identification by code (FAA JO 7110.65 5-3-3): "radar contact" only once the code is observed; idAt = time, idFf = flight following instead of departure report.
    double idAt = -1; bool idFf;
    Airfield? popDf;   // R219: target of the pop-up IFR clearance that follows identification
    string PopClr(Telemetry t, Airfield df) { var dn = df.Name.Replace('-', ' '); return $"cleared to {dn} via radar vectors, {Steer(t, (df.X, df.Z), dn)}, {AltTo(t, cruiseFt)}."; }
    string FfRc(Telemetry? t) => $"radar contact{(t != null ? $", {Where(t)}" : "")}, {Qnh(t)}. Flight following, report leaving frequency.";
    string AirRc(Telemetry? t)
    {
        if (zoneReport && t != null) return $"radar contact, {Where(t)}. Continue as cleared, report leaving the control zone.";   // R7: VFR still in the zone
        if (depClr && t != null && (dest != null ? (dest.X, dest.Z) : exitDir != null && Crp.TryGetValue(exitDir, out var xp) ? xp : ((double, double)?)null) is { } dg)
            return $"radar contact, {Where(t)}. {Cap(Steer(t, dg, dest?.Name.Replace('-', ' ') ?? $"C R P {exitDir}"))}, climb and maintain {Alt(Cruise)}.";
        return depClr ? $"radar contact{(t != null ? $", {Where(t)}" : "")}. Climb and maintain {Alt(Cruise)}, resume own navigation."
                      : $"radar contact{(t != null ? $", {Where(t)}" : "")}. Resume own navigation, maintain VFR.";   // Visual: do not prescribe an altitude
    }

    /// A90: code once per flight (digits 0-7, not 1200/7500/7600/7700), unchanged afterwards.
    string TakeSquawk()
    {
        while (Squawk == null || Squawk is "1200" or "7500" or "7600" or "7700")
            Squawk = string.Concat(Enumerable.Range(0, 4).Select(_ => (char)('0' + Random.Shared.Next(0, 8))));
        return Squawk;
    }

    /// "squawk VFR, " only en route after a code was assigned (releases it); IFR keeps the code.
    string SquawkVfr()
    {
        if (depClr || Squawk == null || Phase != Phase.Away) return "";
        Squawk = null;
        return "squawk VFR, ";
    }

    /// A102: cruise altitude by semicircular rule (magnetic course 000-179 odd, 180-359 even thousands): FL150 or 14000 ft;
    /// low for an airfield under 40 NM (at least 1000 ft above departure altitude); always at least terrain of the route + 2000 ft.
    double CruiseLevel()
    {
        (double X, double Z)? to = dest != null ? (dest.X, dest.Z) : exitDir != null && Crp.TryGetValue(exitDir, out var xp) ? xp : null;
        bool east = ((to is { } g ? Bearing(CX, CZ, g.X, g.Z) : LandHdg(Runway)) - MagVar + 360) % 360 < 180;
        double mva = to is { } m ? F.MvaLeg(CX, CZ, m.X, m.Z) + 2000 : 0;   // even long routes never below terrain + 2000 ft
        bool far = dest == null || Dist(CX, CZ, dest.X, dest.Z) >= 40 * NM;
        double k = Math.Ceiling(Math.Max(mva, far ? (east ? 15000 : 14000) : depFt + 1000) / 1000);
        return (k % 2 == 1) == east ? k * 1000 : (k + 1) * 1000;
    }
    double Cruise => cruiseFt > 0 ? cruiseFt : cruiseFt = CruiseLevel();
    /// Engine off after landing (DCS event "C;Einheit;stopped", Program): parked only at the stand, not on the taxiway (R6/N25, user decision).
    /// R107: also one who taxis back to the stand before departure and shuts down (t = position): the old taxi clearance is done, "request startup" is answered normally again.
    public void EngineOff(Telemetry? t = null)
    {
        if (Phase == Phase.TaxiIn) { if (taxiInTold && specArea || lastTel is { } lt && AtSpot(lt, 50)) Park(); }   // R265: also in the de-arm/hot-brake area (otherwise the sequence would hang in TaxiIn)
        else if (Phase is Phase.StartupApproved or Phase.TaxiOut or Phase.HoldShort && t != null && OnGround(t) && !OnRunwayPos(t.X, t.Z) &&
                 (spot is { } ps && Dist(t.X, t.Z, ps.X, ps.Z) < 50 || F.Spots.Any(s => s.Type != 16 && Dist(t.X, t.Z, s.X, s.Z) < 50))) Park();   // R107: only at the stand (AtSpot would hold everywhere without DCS parking positions)
    }
    /// Within r metres of own or any parking position (excluding runway spots), not on the runway: 50 m stand, 100 m apron.
    /// Without parking positions from DCS, anywhere beside the runway (otherwise the sequence would hang).
    bool AtSpot(Telemetry t, double r) => !OnRunwayPos(t.X, t.Z) && (F.Spots.Count == 0 || spot is { } ps && Dist(t.X, t.Z, ps.X, ps.Z) < r ||
                                                                    F.Spots.Any(s => s.Type != 16 && Dist(t.X, t.Z, s.X, s.Z) < r));

    /// Who must wait at a taxi conflict? Same direction: the one behind. Otherwise right before left (the one who has the other on the right must give way);
    /// if both or neither have the other on the right (oncoming), a fixed coordinate comparison decides, evaluated identically by both (otherwise both hold).
    /// AI does not give way: against AI the player always waits. Other players are recognized by Id = hash of the unit name = group (TrafficFor).
    static bool TaxiGiveWay(Telemetry t, Traffic a)
    {
        double me = t.Hdg * 180 / Math.PI, he = a.Hdg * 180 / Math.PI;
        double rel = ((Bearing(t.X, t.Z, a.X, a.Z) - me) % 360 + 360) % 360, relHe = ((Bearing(a.X, a.Z, t.X, t.Z) - he) % 360 + 360) % 360;
        if (HdgDiff(me, he) < 45)   // front/rear along the mean direction of both: both evaluate identically (otherwise both or neither hold when merging)
        {
            double d = (a.X - t.X) * (Math.Cos(t.Hdg) + Math.Cos(a.Hdg)) + (a.Z - t.Z) * (Math.Sin(t.Hdg) + Math.Sin(a.Hdg));
            return d != 0 ? d > 0 : (t.X, t.Z).CompareTo((a.X, a.Z)) < 0;
        }
        if (a.Group.Length == 0 || a.Id != a.Group.GetHashCode()) return true;
        bool mine = rel < 180, his = relHe < 180;
        return mine != his ? mine : (t.X, t.Z).CompareTo((a.X, a.Z)) < 0;
    }

    /// Taxiing ground traffic that comes closer than 40 m in the next 25 s (straight-line projection) and to which the player must give way.
    static Traffic? TaxiConflict(Telemetry t, IReadOnlyList<Traffic> traffic)
    {
        foreach (var a in traffic)
        {
            if (a.InAir || a.Speed < 2 || Dist(t.X, t.Z, a.X, a.Z) > 300 || !TaxiGiveWay(t, a)) continue;
            var (miss, tc) = TaxiMiss(t, a, Gs(t));
            if (miss < 40 && (tc > 0 || Dist(t.X, t.Z, a.X, a.Z) < 60)) return a;
        }
        return null;
    }

    /// R342: stationary ground traffic in the own taxi path (ahead within 30°, under 150 m, track passes within 40 m); parked ones at a stand do not count
    /// (without DCS parking positions only at the holding point, otherwise every parked aircraft along the taxiway would hold him).
    Traffic? StandConflict(Telemetry t, IReadOnlyList<Traffic> traffic) => traffic.FirstOrDefault(a =>
        !a.InAir && a.Speed < 2 && Dist(t.X, t.Z, a.X, a.Z) is var d && d < 150 &&
        ((Bearing(t.X, t.Z, a.X, a.Z) - t.Hdg * 180 / Math.PI) % 360 + 360) % 360 is var rel && (rel < 30 || rel > 330) && Math.Abs(d * Math.Sin(rel * Math.PI / 180)) < 40 &&
        (F.Spots.Count == 0 ? AtHoldPt(a, t) : !F.Spots.Any(s => s.Type != 16 && Dist(a.X, a.Z, s.X, s.Z) < 30)));

    bool AtHoldPt(Traffic a, Telemetry t) => Phase == Phase.TaxiOut && HoldPt(Runway, t) is var h && Dist(a.X, a.Z, h.X, h.Z) < 150;

    /// Smallest distance to a in the next 25 s (straight-line projection, own speed my) and when (0: now or diverging)
    static (double Miss, double Tc) TaxiMiss(Telemetry t, Traffic a, double my)
    {
        double rx = a.X - t.X, rz = a.Z - t.Z, vx = Math.Cos(a.Hdg) * a.Speed - Math.Cos(t.Hdg) * my, vz = Math.Sin(a.Hdg) * a.Speed - Math.Sin(t.Hdg) * my;
        double vv = vx * vx + vz * vz, tc = vv < 0.01 ? 0 : Math.Clamp(-(rx * vx + rz * vz) / vv, 0, 25);
        return (Math.Sqrt(Sq(rx + vx * tc) + Sq(rz + vz * tc)), tc);
    }

    /// Air traffic (now within 700 ft) that comes closer than sep (1.5 NM) and 700 ft in the next 60 s (horizon): straight-line projection, with climb/descent rates
    /// (own only up to the assigned altitude toFt, without it: hold altitude). Already close but diverging (or equal speed in trail): no conflict.
    /// 1000 ft separation (holding) does not trigger; neither does one who quickly climbs/descends through the altitude beforehand.
    internal static (Traffic A, double Tc)? AirConflict(Telemetry t, IReadOnlyList<Traffic> traffic, double sep = 1.5 * NM, double? toFt = null, double horizon = 60)
    {
        double mx = Math.Cos(t.Hdg) * t.Ias, mz = Math.Sin(t.Hdg) * t.Ias;
        double My(double s) => toFt is { } g ? t.AltMsl + Math.Clamp(t.Vs * s, Math.Min(0, g * Ft - t.AltMsl), Math.Max(0, g * Ft - t.AltMsl)) : t.AltMsl;
        foreach (var a in traffic.Where(a => a.InAir && a.Speed > 30 && Math.Abs(a.AltMsl - t.AltMsl) < 700 * Ft && Dist(t.X, t.Z, a.X, a.Z) < 10 * NM)
                                 .OrderBy(a => Dist(t.X, t.Z, a.X, a.Z)))
        {
            double rx = a.X - t.X, rz = a.Z - t.Z, vx = Math.Cos(a.Hdg) * a.Speed - mx, vz = Math.Sin(a.Hdg) * a.Speed - mz;
            double vv = vx * vx + vz * vz, closing = -(rx * vx + rz * vz);
            if (vv < 25 || closing <= 0) continue;   // same speed or diverging
            for (double s = 0; s <= horizon; s += 2)
                if (Math.Sqrt(Sq(rx + vx * s) + Sq(rz + vz * s)) < sep && Math.Abs(a.AltMsl + a.Vs * s - My(s)) < 700 * Ft) return (a, s);
        }
        return null;
    }

    /// AI departure that comes closer than 2 NM in the next 2 min and can climb from below up to my altitude (nobody knows where it levels off).
    internal static (Traffic A, double Tc)? DepConflict(Telemetry t, IReadOnlyList<Traffic> traffic)
    {
        double mx = Math.Cos(t.Hdg) * t.Ias, mz = Math.Sin(t.Hdg) * t.Ias;
        foreach (var a in traffic.Where(a => a.InAir && a.Flag.StartsWith("dep") && a.Vs > 1 && t.AltMsl > a.AltMsl - 500 * Ft && t.AltMsl < a.AltMsl + a.Vs * 120 + 700 * Ft && Dist(t.X, t.Z, a.X, a.Z) < 10 * NM))
        {
            double rx = a.X - t.X, rz = a.Z - t.Z, vx = Math.Cos(a.Hdg) * a.Speed - mx, vz = Math.Sin(a.Hdg) * a.Speed - mz;
            if (rx * vx + rz * vz >= 0) continue;   // diverging
            for (double s = 0; s <= 120; s += 2)
                if (Math.Sqrt(Sq(rx + vx * s) + Sq(rz + vz * s)) < 2 * NM) return (a, s);
        }
        return null;
    }

    /// Traffic alert with avoidance heading (60° away from the traffic, head-on: right) and avoidance altitude (1000 ft away, never below terrain,
    /// never through the traffic). Afterwards 30 s no other instructions (alertUntil).
    readonly Dictionary<int, double> alerted = new();
    double alertUntil = -1, alertFt = double.NaN;
    IReadOnlyList<Traffic> near = Array.Empty<Traffic>();   // traffic from the last tick (StepDown)

    /// Altitude free (radar separation): on the way there no traffic closer than 3 NM and 1000 ft, no conflict in the next 60 s.
    static bool LevelFree(Telemetry t, IReadOnlyList<Traffic> traffic, double ft)
    {
        double from = t.AltMsl / Ft;
        if (traffic.Any(a => a.InAir && a.AltMsl / Ft > Math.Min(from, ft) - 1000 && a.AltMsl / Ft < Math.Max(from, ft) + 1000 && Dist(t.X, t.Z, a.X, a.Z) < 3 * NM)) return false;
        for (double f = ft; Math.Abs(f - from) > 1; f += Math.Clamp(from - f, -500, 500))   // target altitude and altitudes flown through
            if (AirConflict(t with { AltMsl = f * Ft }, traffic) != null) return false;
        return true;
    }
    /// A99: traffic pattern (initial, downwind to landing): traffic without tower contact that comes closer than 0.5 NM in 30 s, once per aircraft
    /// as traffic information without avoidance heading (the pilot flies visually, unlike on approach).
    /// With contact: AI approach ("arr…"), AI departure from here ("dep…:airfield", old mission "dep" = Kutaisi), other player at this airfield ("twr:"/"hold:airfield", Program.TrafficFor).
    readonly HashSet<int> patTold = new();
    bool InContact(Traffic a) => a.Flag.StartsWith("arr") || a.Flag.Split(':', 2) switch
    {
        [var k, var n] => n == F.Name && (k is "twr" or "hold" || k.StartsWith("dep")),
        [var k] => k.StartsWith("dep") && F.Name == "Kutaisi",
        _ => false,
    };
    List<Msg>? PatternTraffic(Telemetry t, IReadOnlyList<Traffic> traffic)
    {
        if (AirConflict(t, traffic.Where(a => !InContact(a) && !patTold.Contains(a.Id)).ToList(), 0.5 * NM, null, 30) is not { } cf) return null;
        var a = cf.A;
        patTold.Add(a.Id);
        int clock = (int)Math.Round(((Bearing(t.X, t.Z, a.X, a.Z) - t.Hdg * 180 / Math.PI) % 360 + 360) % 360 / 30) % 12;
        // R206: flight direction, spoken type name and lateral movement (FAA JO 7110.65 2-1-21)
        double rel = ((a.Hdg - t.Hdg) * 180 / Math.PI % 360 + 360) % 360;
        var move = a.Speed < 5 ? "" : $", {Dir8(a.Hdg * 180 / Math.PI)}bound";
        var cross = a.Speed < 5 ? "" : rel is > 45 and < 135 ? $", crossing {(Phase == Phase.ClearedLand ? "final " : "")}left to right" : rel is > 225 and < 315 ? $", crossing {(Phase == Phase.ClearedLand ? "final " : "")}right to left" : "";
        int mi = Miles(Dist(t.X, t.Z, a.X, a.Z));
        return Say($"{Cs()}, traffic {(clock == 0 ? 12 : clock)} o'clock, {mi} mile{(mi == 1 ? "" : "s")}{move}, {Ops.TypeSay(a.Type)}, {Math.Round(a.AltMsl / Ft / 100) * 100:0} feet{cross}, no contact with Tower.");
    }
    /// A52: radar traffic advisories (warning, flight following, approach) only for own and neutral traffic in the air, the AWACS reports enemies;
    /// runway, final, traffic pattern and sequence know all traffic (hence do not filter in Program.TrafficFor).
    IReadOnlyList<Traffic> Own(IReadOnlyList<Traffic> traffic) => Side == 0 ? traffic : traffic.Where(a => !a.InAir || a.Coalition == 0 || a.Coalition == Side).ToList();
    /// Altitude assigned by ATC (holding, radar vectoring, IFR departure/en route), null = none (Program.TrafficFor passes it on to the others as Traffic.AsgFt).
    public double? AsgFt => holding ? holdFt : vec != null ? vecFt : depClr && Phase is Phase.Departing or Phase.Away ? (Phase == Phase.Departing ? depFt : Cruise) : null;
    List<Msg>? TrafficAlert(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        traffic = Own(traffic);
        bool ctl = holding || vec != null || depClr;   // R214: controlled flight (IFR departure, radar vectoring, holding) gets a fixed avoidance altitude, VFR only "climb/descend"
        double? have = AsgFt;   // assigned altitude (climb/descent towards it counts)
        // Traffic pattern: visual separation 0.5 NM in 30 s (downwind beside initial/final; turning there, continuing to project straight is misleading)
        bool Pat(double x, double z, double ft) => Dist(x, z, CX, CZ) < 8 * NM && ft < PatternFt + 1000;
        var hit = AirConflict(t, Pat(t.X, t.Z, IndFt(t)) ? Array.Empty<Traffic>() : traffic.Where(a => !Pat(a.X, a.Z, a.AltMsl / Ft)).ToList(), 1.5 * NM, have) ?? AirConflict(t, traffic, 0.5 * NM, have, 30);
        bool early = hit == null;   // otherwise only: AI departure climbs into my path within 2 min -> now calmly heading away with traffic advisory, altitude stays
        if ((hit ?? DepConflict(t, traffic)) is not { } cf || now - alerted.GetValueOrDefault(cf.A.Id, -999) < (early ? 120 : 30)) return null;
        var a = cf.A;
        alerted[a.Id] = now; alertUntil = now + (early ? 45 : 30);
        double my = t.Hdg * 180 / Math.PI, rel = ((Bearing(t.X, t.Z, a.X, a.Z) - my) % 360 + 360) % 360;
        int clock = (int)Math.Round(rel / 30) % 12;
        double turn = clock is 0 or 6 || rel >= 180 ? 60 : -60;   // traffic left, head-on, from behind -> right; right -> left
        int mag = (int)Math.Round(((my + turn - MagVar) % 360 + 360) % 360 / 5) * 5;
        double myFt = t.AltMsl / Ft, itFt = a.AltMsl / Ft, dAlt = itFt - myFt;
        double floor = Math.Max(F.MvaFt(t.X, t.Z), FieldElev / Ft + 1000);
        double down = Math.Floor((Math.Min(myFt, itFt) - 1000) / 500) * 500, up = Math.Ceiling((Math.Max(myFt, itFt) + 1000) / 500) * 500;
        // traffic (up to the meeting point) above, e.g. climbing departure: down if terrain allows, otherwise hold altitude (only evade); below/same: up
        double? ft = early ? null : dAlt + a.Vs * cf.Tc / Ft > 300 ? (down >= floor ? down : null) : up;
        // already assigned altitude separates 1000 ft on the same side: keep it (no new number at every warning)
        if (have is { } hv && ft is { } nf && Math.Abs(hv - itFt) >= 1000 && hv >= floor && hv > itFt == nf > itFt) ft = hv;
        if (vec != null) { if (ft is { } f) (vecFt, altFree) = (f, false); vecHdg = (my + turn + 360) % 360; lastVecSaid = now; }
        if (holding && ft is { } hf) holdFt = alertFt = hf;
        if (depClr && Phase == Phase.Departing && ft is { } df) depFt = df;   // R213: avoidance altitude applies (otherwise afterwards "check altitude, maintain" with the old departure altitude)
        lastHoldInfo = lastVector = now;
        var alt = Math.Abs(dAlt) < 300 ? "same altitude" : $"{Math.Round(Math.Abs(dAlt) / 100) * 100:0} feet {(dAlt > 0 ? "above" : "below")}";
        var hdg = $"turn {(turn > 0 ? "right" : "left")} heading {Digits((mag == 0 ? 360 : mag).ToString("000"))}";
        if (early) return Say($"{Cs()}, {hdg}, traffic {(clock == 0 ? 12 : clock)} o'clock, " +
                              $"{MilesTxt(Dist(t.X, t.Z, a.X, a.Z))}, departing aircraft, climbing through {Alt(Math.Round(itFt / 100) * 100)}.", "Approach");
        // R214: attention word before the callsign, "advise you turn … and climb/descend … immediately" (FAA JO 7110.65 2-1-6 b)
        var vert = ft is { } g ? $" and {(g > myFt ? "climb" : "descend")}{(ctl ? $" to {Alt(g)}" : "")}" : "";
        return Say($"Traffic alert, {Cs()}, {(clock == 0 ? 12 : clock)} o'clock, {MilesTxt(Dist(t.X, t.Z, a.X, a.Z))}, {Dir8(a.Hdg * 180 / Math.PI)}bound, {alt}, " +
                   $"advise you {hdg}{vert} immediately.", "Approach");
    }


    /// R114 terrain warning (MSAW): now and 60 s ahead below terrain + 500 ft, not near the airfield. R215: once per warning situation (lowTold/lowAck), re-armed
    /// as soon as he is safely above terrain + 1000 ft for 10 s (warning threshold + 500 ft margin, FAA JO 7110.65 2-1-6 a).
    /// For every controlled flight (flight following, radar vectoring/holding, handed-over departure), not only FollowTick.
    double lowSafe = -1;   // R215: since when safely above terrain again
    List<Msg>? TerrainAlert(Telemetry t, double now)
    {
        if (Dist(t.X, t.Z, CX, CZ) <= 5 * NM) return null;
        double ax = t.X + Math.Cos(t.Hdg) * t.Ias * 60, az = t.Z + Math.Sin(t.Hdg) * t.Ias * 60;
        double terr = Math.Max(F.TerrainFt(t.X, t.Z, 1), F.TerrainFt(ax, az, 1));
        if (lowTold || lowAck)
        {
            if (terr <= -1e6 || t.AltMsl / Ft < terr + 1000) lowSafe = -1;
            else if (lowSafe < 0) lowSafe = now;
            else if (now - lowSafe >= 10) (lowTold, lowAck, lowSafe) = (false, false, -1);
            return null;
        }
        if (Phase != Phase.Inbound && !Following && t.Vs > 2.5) return null;   // Departure in climb (ODP): terrain proximity is normal there, warn only in level flight/descent
        // Radar vectoring/holding: assigned altitude (MVA-protected, holding circles, look-ahead point says nothing) applies; warning only below it (> 300 ft) and 20 s after the end of a new instruction (reaction time; first contact with vectors ~25 s of speech, ~2.5 words/s as the readback window)
        if (Phase == Phase.Inbound && (holding ? holdFt : vec != null ? vecFt : 0) is > 0 and var asg && (IndFt(t) >= asg - 300 || now - lastVecSaid < 20 + (last?.Text.Split(' ').Length ?? 0) / 2.5)) return null;
        if (terr <= -1e6 || t.AltMsl / Ft >= terr + 500) return null;
        lowTold = true; lowSafe = -1;
        // R214: wake word first, "The MVA in your area is …" (FAA JO 7110.65 2-1-6 a)
        return Say($"Low altitude alert, {Cs()}, check your altitude immediately. The M V A in your area is {Alt(Math.Max(F.MvaFt(t.X, t.Z), F.MvaFt(ax, az)))}.", "Approach");
    }

    /// V15 flight following: collision warning, terrain warning (now and 60 s ahead below terrain + 500 ft), traffic advisory (TrafficInfo).
    List<Msg>? FollowTick(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        traffic = Own(traffic);   // A52
        if (TrafficAlert(t, traffic, now) is { } ta) return ta;
        if (TerrainAlert(t, now) is { } la) return la;
        return TrafficInfo(t, traffic, now);
    }
    /// V15 · R216: traffic advisory (≤ 5 NM, ±1500 ft, per aircraft every 2 min) for every flight under radar service: flight following, radar vectoring/holding,
    /// departure after check-in (FAA JO 7110.65 2-1-21, ICAO Doc 4444 8.8.2).
    List<Msg>? TrafficInfo(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        traffic = Own(traffic);   // A52
        if (now - followAt > 60) advised.Clear();   // N24: do not conclude advisories from an earlier service ("no longer a factor" for long-forgotten traffic)
        followAt = now;
        // N24: reported traffic diverges (> 6 NM or gone): once "no longer a factor", only without "in sight"; after "negative contact" an update after 30 s
        foreach (var (id, v) in advised)
        {
            if (v.Done || v.St == 1) continue;
            var o = traffic.FirstOrDefault(x => x.Id == id && x.InAir);
            if (o == null || Dist(t.X, t.Z, o.X, o.Z) > 6 * NM) { v.Done = true; return Say($"{Cs()}, traffic no longer a factor.", "Approach"); }
            if (v.St == 2 && now - v.At >= 30) { v.St = 0; v.At = now; return Say($"{Cs()}, traffic update, {TrafficLine(t, o)}.", "Approach"); }
        }
        // R216: no advisory on controlled traffic separated by assigned altitudes (≥ 1000 ft) with both at their altitude (7110.65 2-1-21: only if separation can be lost)
        bool Sep(Traffic a) => AsgFt is { } my && a.AsgFt > 0 && Math.Abs(a.AsgFt - my) >= 1000 && Math.Abs(IndFt(t) - my) < 300 &&
                               Math.Abs((a.AltMsl - t.AltMsl) / Ft - (a.AsgFt - my)) < 300;
        var a = traffic.Where(a => a.InAir && Dist(t.X, t.Z, a.X, a.Z) < 5 * NM && Math.Abs(a.AltMsl - t.AltMsl) / Ft < 1500 && !Sep(a) && now - (advised.TryGetValue(a.Id, out var av) ? av.At : -999) > 120)
                       .MinBy(a => Dist(t.X, t.Z, a.X, a.Z));
        if (a == null) return null;
        advised[a.Id] = new Advice { At = now };
        return Say($"{Cs()}, traffic, {TrafficLine(t, a)}.", "Approach");
    }
    string TrafficLine(Telemetry t, Traffic a)
    {
        int clock = (int)Math.Round(((Bearing(t.X, t.Z, a.X, a.Z) - t.Hdg * 180 / Math.PI) % 360 + 360) % 360 / 30) % 12;
        double dAlt = (a.AltMsl - t.AltMsl) / Ft;
        return $"{(clock == 0 ? 12 : clock)} o'clock, {MilesTxt(Dist(t.X, t.Z, a.X, a.Z))}, {Dir8(a.Hdg * 180 / Math.PI)}bound, " +
               $"{(Math.Abs(dAlt) < 300 ? "same altitude" : $"{Math.Round(Math.Abs(dAlt) / 100) * 100:0} feet {(dAlt > 0 ? "above" : "below")}")}, {Ops.TypeSay(a.Type)}";
    }
    /// "at your 2 o'clock, 350 meters" to own parking position; null = unknown or other airfield.
    /// Parking position for taxi-in: own from the start if free, otherwise the nearest free one (no aircraft on the ground closer than 30 m,
    /// no runway/helicopter spots).
    (double X, double Z, int Num, int Type)? FreeSpot(Telemetry t, IReadOnlyList<Traffic> traffic)
    {
        bool Free((double X, double Z, int Num, int Type) s) => !traffic.Any(a => !a.InAir && Dist(a.X, a.Z, s.X, s.Z) < 30) && !Reserved.Any(r => Dist(r.X, r.Z, s.X, s.Z) < 10);   // R108: also assigned stands of other players
        var free = F.Spots.Where(s => s.Type is not (16 or 40) && Free(s)).ToList();
        if (spot is { } sp && free.Where(s => Dist(s.X, s.Z, sp.X, sp.Z) < 10).ToList() is [var own, ..]) return own;
        return free.Count == 0 ? null : free.MinBy(s => Dist(t.X, t.Z, s.X, s.Z));
    }

    /// N26: taxi aid target: TaxiIn = own parking position, TaxiOut = holding point
    (double X, double Z)? ProgTarget(Telemetry t) => Phase == Phase.TaxiIn ? spot : Phase == Phase.TaxiOut ? HoldPt(Runway, t) : null;
    string ProgName(string rw) => Phase == Phase.TaxiIn ? "parking position" : $"holding point runway {RwSay(rw)}";
    void ProgStart(bool on, double now) { lastParkCall = now; progressive = on; progNear = false; progLast = progMin = 1e9; growSince = -1; }   // new taxi order

    /// Holding point: about 150 m before the threshold, 100 m lateral on the aircraft's side (DCS-ATC does not know the taxiway network)
    (double X, double Z) HoldPt(string rw, Telemetry t)
    {
        var e = F.End(rw);
        double side = Math.Sign((t.Z - e.ThrZ) * e.Dx - (t.X - e.ThrX) * e.Dz);
        if (side == 0) side = 1;
        return (e.ThrX - 150 * e.Dx - side * 100 * e.Dz, e.ThrZ - 150 * e.Dz + side * 100 * e.Dx);
    }

    string? ParkDir(Telemetry t, (double X, double Z)? tgt = null)
    {
        if ((tgt ?? spot) is not { } sp) return null;
        double d = Dist(t.X, t.Z, sp.X, sp.Z);
        if (d > 5000) return null;
        double rel = ((Bearing(t.X, t.Z, sp.X, sp.Z) - t.Hdg * 180 / Math.PI) % 360 + 360) % 360;
        int clock = (int)Math.Round(rel / 30) % 12;
        var m = $"{Math.Round(d / (d > 300 ? 50 : 10)) * (d > 300 ? 50 : 10):0} meters";
        if (d < 80) return $"{(clock is 0 or 11 or 1 ? "ahead" : rel < 180 ? "on your right" : "on your left")}, {m}";
        return $"at your {(clock == 0 ? 12 : clock)} o'clock, {m}";
    }

    void Reset(Telemetry t, double now)
    {
        bool landing = (airborne || Phase is Phase.ClearedLand or Phase.TaxiIn) && lastTel != null && Dist(t.X, t.Z, lastTel.X, lastTel.Z) < 3000;   // came from the air without a jump (gap in approach/rollout), no respawn
        airborne = !OnGround(t);
        Phase = airborne ? Phase.Away : Phase.Parked; wantStraight = wantVisual = wantOverhead = visAsk = visSeen = false;
        if (!airborne) home = NearestRamp(t);   // in the air (return after gap): ramp and own parking position from departure remain
        if (!airborne) spot = F.Spots.Where(s => Dist(t.X, t.Z, s.X, s.Z) < 40).OrderBy(s => Dist(t.X, t.Z, s.X, s.Z)).Select(s => ((double, double)?)(s.X, s.Z)).FirstOrDefault() ?? (t.X, t.Z);
        parkNum = 0;
        idAt = -1; popDf = null; lastAltFt = -1; spdTold = 0; spdFree = false;
        taxiInTold = progressive = false;
        lowTold = lowAck = false;
        exitDir = entry = null;
        vec = null; missAt = -1; zoneReport = false;
        stoppedSince = -1;
        lineUp = stayPattern = holding = graded = called = wentAround = false;
        devTold.Clear(); devPend = false; special = null; intr = 0; transitFt = null;
        Emergency = false;
        option = wantRw = null;
        Laps = 0;
        Rejected = 0;
        wasAir = airborne;
        glide.Clear();
        PatternReset();
        Runway = WindRunway(t);
        // R110: on the runway with speed or after landing (airfield change, app restart during rollout): rollout, no takeoff clearance.
        // At standstill (spawn on runway): line up and wait without clearance (no "hold position"), takeoff clearance only on "ready for departure" (holdSince -1)
        if (!airborne && OnRunwayPos(t.X, t.Z))
        {
            if (landing || t.Ias > 5 * Kt + WindSpeed(t)) (Phase, landedAt, landRw) = (Phase.TaxiIn, now, RwyOf(t));   // Export.lua delivers IAS: at standstill it shows the headwind; landing runway for A48
            else (Phase, lineUp, holdSince) = (Phase.HoldShort, true, -1);
        }
    }

    List<Msg> Say(string s, string? r = null) { last = new Msg(Dep(r ?? role), Regex.Replace(s + sayTail, @"\b1 miles\b", "1 mile")); sayTail = ""; if (simPair != null) { similarSaid.Add(simPair); simPair = null; } lastAt = callAt; return new() { last }; }   // Miles() from 1: "1 mile"
    string? simPair; readonly HashSet<string> similarSaid = new();   // A139: group of the appended similar callsign (only noted on spoken reply) / already announced
    string sayTail = "";               // addition for the next reply (rejected runway request, OnTranscript)
    string? wantRw;                      // pilot's runway request, valid until landing
    double stepAfter;                    // "request higher" under radar vectoring: no profile descent until then

    /// Rejected: do not count the same report afterwards as a readback.
    List<Msg> Neg(string s) { lastIntent = ""; Rejected++; return Say(s); }

    /// N4: violation (FAA JO 7110.65 2-1-26) once per kind: note "possible pilot deviation", info "Verstoß: …" with mission clock (Program: into debriefing). Watch off: only the call.
    /// R247: the Brasher notice is not tied to the safety instruction, it is noted and spoken at the next quiet moment (Tick, devPend).
    /// R248: ident false (guard, callsign unknown): no call to an unknown (already outside or already over, without instruction), no notice either, only into debriefing.
    /// Missing mandatory report without "possible pilot deviation" on the radio (reporting duty #10/#12): Note(Say(…)).
    /// fac: facility where the violation occurred (its phone number, 7110.65 2-1-26); by: who speaks the notice (null = whoever controls him then) – without check-in with Approach he is still with the tower.
    List<Msg> Dev(string s, string de, string en, string r, bool ident = true, string fac = "Tower", string? by = null)
    {
        if (ident && !Unknown && Watch && !devTold.Contains(en)) (devPend, devNoteAt, devFac, devBy) = (true, callAt, fac, by);   // Unknown (N51): nobody the notice could go to
        return Note(!ident ? new List<Msg>() : Say(s, r), de, en);
    }
    /// N2: entry without contact (ICAO Annex 11, FAA JO 7110.65 2-1-26): 1. "you are entering the Kutaisi control zone without clearance, …, say intentions",
    /// 2. after 30 s without reply below 2.5 NM or across the final "leave the control zone immediately" (not with landing features on final: without a call, below 1 NM "go around, you are not cleared to land"),
    /// 3. outside again: violation. Runway crossed low: violation. Tactical (AWACS): only traffic pattern (below 2.5 NM and pattern altitude + 500 ft) and runway.
    /// N51b: stage 1 and 2 only if he can get in the way of airfield traffic (Conflict or traffic there), stage 1 immediately with redirect ("turn left heading …, remain clear of the control zone").
    List<Msg>? WatchTick(Telemetry t, IReadOnlyList<Traffic> traffic, double now)
    {
        double d = Dist(t.X, t.Z, CX, CZ), hdg = t.Hdg * 180 / Math.PI;
        var zone = F.Name.Replace('-', ' ');
        var c = $"{(Unknown ? $"Unidentified aircraft {Where(t, zone)}, {HdgSay(hdg)}, {Math.Round(IndFt(t) / 100) * 100:0} feet" : Cs())}, {F.StationOf("Tower")}";   // N51: callsign unknown -> position, heading, altitude (FAA JO 7110.65 10-2-x)
        if (t.Agl < 300 && OnRunwayPos(t.X, t.Z, 300, 0) && HdgDiff(hdg, LandHdg(Runway)) is > 30 and < 150 && now - crossAt > 60)
        {
            crossAt = now;
            return Dev($"{c}, you crossed the active runway without clearance.", $"Bahn ohne Freigabe überflogen {F.Name}", $"runway crossed without clearance {F.Name}", "Tower", !Unknown);
        }
        if (d > CtrM)
        {
            if (intr is not (1 or 2)) return null;
            intr = 3;
            var ft = Math.Round(IndFt(t) / 100) * 100;
            return Dev($"{c}, leaving the control zone.", $"Luftraumverletzung {F.Name}, {ft:0} ft, keine Antwort", $"airspace violation {F.Name}, {ft:0} ft, no reply", "Tower", !Unknown);
        }
        var (al, lat) = Approach(t.X, t.Z, Runway);
        bool close = d < 2.5 * NM || al > 0 && al < 5 * NM && lat < 0.5 * NM;   // close to the airfield or across the final
        string Lr(double b) => ((b - hdg) % 360 + 540) % 360 - 180 >= 0 ? "right" : "left";
        // N51b: only one who can get in the way of airfield traffic (pattern, final, departure path) or real traffic there (3 NM, ±1000 ft); otherwise silent
        var rel = Conflict(t.X, t.Z, IndFt(t), hdg);
        var near = rel != null ? null : traffic.Where(a => a.InAir && a.Speed > 30 && Math.Abs(a.AltMsl - t.AltMsl) < 1000 * Ft && Dist(a.X, a.Z, t.X, t.Z) < 3 * NM && Conflict(a.X, a.Z, a.AltMsl / Ft, a.Hdg * 180 / Math.PI) != null)
                                               .MinBy(a => Dist(a.X, a.Z, t.X, t.Z));
        if (near != null) { int n = Miles(Dist(near.X, near.Z, t.X, t.Z)); rel = ($"traffic {n} mile{(n == 1 ? "" : "s")} {Dir8(Bearing(t.X, t.Z, near.X, near.Z))}, {Alt(Math.Round(near.AltMsl / Ft / 100) * 100)}", Bearing(near.X, near.Z, t.X, t.Z)); }
        if (Tactical ? intr == 0 && d < 2.5 * NM && IndFt(t) < PatternFt + 500 : intr == 1 && now - intrAt > 30 && close && rel != null)
        {
            if (LandingCues(t) && OnFinal(t.X, t.Z, t.Hdg, t.Agl, Runway, 5 * NM)) return null;   // lands: without a call go around on final (Tick, reporting duty)
            intr = 2;
            double brg = rel?.Away ?? Bearing(CX, CZ, t.X, t.Z);   // by shortest way out; from final/departure path perpendicular away as in stage 1 (N51b: not out along the approach line into the traffic)
            return Say($"{c}, turn {Lr(brg)} {HdgSay(brg)}, leave the control zone immediately{(PatternCount(traffic) > 0 ? ", traffic in the pattern" : "")}.", "Tower");
        }
        if (Tactical || intr != 0 || rel is not { } rv) return null;
        (intr, intrAt) = (1, now);
        var (why, away) = rv;
        // N51b: immediately with redirect away from the conflict; if he visibly lands (landing features on final), only ask (without a call go around on final); R248: callsign unknown -> "identify yourself" (FAA JO 7110.65 2-1-26, AIM 5-6-13)
        if (why == "" && PatternCount(traffic) > 0) why = "traffic in the pattern";
        var go = LandingCues(t) && OnFinal(t.X, t.Z, t.Hdg, t.Agl, Runway, CtrM) ? Unknown ? ", identify yourself and state your intentions" : ", say intentions"
               : (Unknown ? ", identify yourself" : "") + (why == "" ? "" : ", " + why) + (away is { } aw ? $", {(HdgDiff(aw, hdg) < 15 ? "fly" : $"turn {Lr(aw)}")} {HdgSay(aw, 10)}{(why.Contains("runway") ? " immediately" : "")}, remain clear of the control zone"
                                                              : $", climb and maintain at or above {Alt(Math.Floor((PatternFt + 1500) / 1000) * 1000 + 1000)}");
        return Say($"{c}, you are entering the {zone} control zone{(Unknown ? "" : $" without clearance, {Where(t)}")}{go}.", "Tower");   // Review l7: no radio-menu hint (in reality only the call)
    }
    /// N51b: conflict with airfield traffic at (x, z) in ft (displayed) on heading hdg (true °): final of the active runway (to the zone boundary, ±1.5 NM, ±1000 ft around the 3° glidepath
    /// or on the initial route below pattern + 500 ft), departure path (behind the runway end to the zone boundary, ±1.5 NM, below MaxFt + 1000 ft), traffic pattern (below 3 NM, below pattern + 1000 ft).
    /// Why: reason in the call; Away: heading (true) by shortest way out (perpendicular away from the axis or away from the airfield), null = over the airfield just below the boundary: climb. Result null = silent.
    (string Why, double? Away)? Conflict(double x, double z, double ft, double hdg)
    {
        var e = F.End(Runway);
        var (al, lat) = Approach(x, z, Runway);
        double side = (x - e.ThrX) * e.Dz - (z - e.ThrZ) * e.Dx, dep = (x - e.CX) * e.Dx + (z - e.CZ) * e.Dz, d = Dist(x, z, CX, CZ);
        if (Math.Abs(side) < 100) side = Math.Cos(hdg * Math.PI / 180) * e.Dz - Math.Sin(hdg * Math.PI / 180) * e.Dx;   // on the axis: to the side he already points to
        double s = side >= 0 ? 1 : -1, off = Bearing(0, 0, s * e.Dz, -s * e.Dx);
        var how = HdgDiff(hdg, e.Hdg) is > 30 and < 150 ? "crossing" : "on";   // along (even opposite) he does not cross
        if (lat < 1.5 * NM && al > 0 && al < CtrM && (Math.Abs(ft - (FieldElev / Ft + al / NM * 318)) < 1000 || al < InitialDist + 2 * NM && ft < PatternFt + 500))
            return ($"{how} the final approach course runway {RwSay(Runway)}", off);
        if (lat < 1.5 * NM && dep > e.Len / 2 && dep < CtrM && ft < MaxFt + 1000) return ($"{how} the departure path runway {RwSay(Runway)}", off);
        if (d < 3 * NM && ft < PatternFt + 1000) return ("", d < 1.5 * NM && ft > PatternFt + 500 ? null : Bearing(CX, CZ, x, z));
        return null;
    }
    readonly HashSet<string> devTold = new();   // N4: violations already reported (until landing or reset)
    bool devPend; double devNoteAt;             // R247: noted notice "possible pilot deviation", time of violation
    string devFac = "Tower"; string? devBy;     // Review l2: facility of the violation (phone number) and speaker of the notice
    string? special;                            // R251: taxi target after landing instead of parking position ("de-arm area"), once
    bool specArea;                              // R265: taxi clearance to the de-arm/hot-brake area (instead of parking position): shutting down there = parked
    bool called, wentAround;                    // N4: pilot has called this airfield (reset: new) / landing clearance withdrawn by "go around"
    /// A48: following traffic within 3 NM on final of the runway; below 1100 ft above airfield (3° glidepath at 3 NM ≈ 950 ft), higher is initial to the overhead break.
    /// Only if he would be at the threshold before clearing: flight time there under remaining occupancy of the lander (v m/s: decelerate at 2 m/s² to 8 m/s, then 25 s to the taxiway,
    /// from touchdown ≈ 55 s like the 50 s occupancy in FAA JO 7110.65 5-5-4) plus 15 s – at 3 NM separation (R46, ~75 s) no "expedite", only at 2 NM or when he is stationary.
    /// R255: the 25 s run from the first time below 8 m/s (slowAt); expired or he stands (below 3 m/s): every follower below 3 NM gets "expedite" (A48).
    Traffic? Follower(IReadOnlyList<Traffic> traffic, string rw, double v, double now)
    {
        if (rw == "") return null;   // landing runway unknown (restored session during rollout)
        if (v < 8 && slowAt < landedAt) slowAt = now;
        double rest = slowAt >= landedAt ? 25 - (now - slowAt) : (v - 8) / 2 + 25;
        bool now3 = rest <= 0 || v < 3;
        return traffic.Where(a => a.Speed > 30 && a.AltMsl - FieldElev < 1100 * Ft && OnFinal(a.X, a.Z, a.Hdg, a.AltMsl - FieldElev, rw, 3 * NM) &&
                                  (now3 || Approach(a.X, a.Z, rw).along / a.Speed < rest + 15)).OrderBy(a => Approach(a.X, a.Z, rw).along).FirstOrDefault();
    }
    bool rwSwitched;      // R334: Aligned changed the runway with landing clearance
    bool vacDue;          // R333: vacate instruction pending (without following traffic only below 40 kt)
    double vacAt = -999;  // R354: time of the last vacate instruction
    double slowAt = -1;   // R255: lander first below 8 m/s after landedAt
    string RwyOf(Telemetry t)   // Runway by heading (takeoff/landing), parallel runways by position; crossing runways only by heading (near the intersection the other centreline would be closer)
    {
        var b = F.Ends.MinBy(e => HdgDiff(t.Hdg * 180 / Math.PI, e.Hdg))!;
        return F.Ends.Where(e => HdgDiff(e.Hdg, b.Hdg) < 10).MinBy(e => Approach(t.X, t.Z, e.Name).lateral)!.Name;
    }
}
