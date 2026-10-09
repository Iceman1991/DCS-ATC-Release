using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

public partial class Tower
{
    // ======================================================================= Pilot speaks
    /// Understood with low confidence (Whisper confidence): ask back instead of acting; responsible controller as in OnTranscript.
    public List<Msg> SayAgain(string text)
    {
        role = Addressed(Normalize(text)) ?? Owner();
        return new() { new Msg(Dep(role), $"{Cs()}, {Station}, say again.") };
    }
    /// Low confidence on a bare acknowledgement or "say again": the phrase is clear anyway -> process normally instead of asking back ("Copied." -> "say again")
    public bool Plain(string text) => Normalize(text) is var n && (AckOnly(n) || Ops.BareSayAgain(n, callsign));
    /// Program: the transmission has actually gone out (radio queue) -> nag windows count from its end, not from queueing (vector repeated before the first call aired)
    public void Aired(double now, string text) => lastVecSaid = Math.Max(lastVecSaid, now + text.Split(' ').Length / 2.5);

    /// Called another airfield on this airfield's frequency: "Enfield 1-1, this is Senaki Tower, you are on Senaki frequency, contact Kutaisi Tower 134.0."
    public List<Msg> OtherField(string text, Airfield o)
    {
        role = Regex.Match(Normalize(text), @"\b(ground|tower|approach|departure|radar)\b").Value switch { "ground" => "Ground", "tower" => "Tower", "" => Owner(), _ => "Approach" };
        return Neg($"{Cs()}, this is {Station}, you are on {F.Name.Replace('-', ' ')} frequency, contact {o.StationOf(role)} {FreqSay(FreqOf(o, role))}.");
    }

    public List<Msg> OnTranscript(string text, Telemetry? t, IReadOnlyList<Traffic> traffic, double now, bool fromMenu = false)
    {
        callAt = now;
        if (t != null) Seen(t, now);
        var n = Normalize(text);
        if (n.Length == 0) return new();
        called = true;
        if (!CallsignFixed && FlightSize == 1 && ExtractCallsign(n) is { } cs)   // Flight: lead's callsign is kept; misheard: known one is kept
            if (SameCallsign(cs, callsign)) heardCs = null; else if (cs == heardCs) callsign = cs; else heardCs = cs;   // foreign one only after two in a row ("Enfield 1-2, check fuel" to the wingman does not count)
        var c = Cs();
        var addressed = Addressed(n);
        role = addressed ?? Owner();
        bool ground = t == null || OnGround(t);
        var dir = Direction(n);
        var rw = Runway;
        if (Has(n, "say again", "come again", "repeat", "say last", "last transmission", "didn t copy", "did not copy",
                "not copy", "unable to copy", "missed", "garbled", "didn t get", "not understand", "pardon") && !Has(n, "going missed", "missed approach"))
        {
            // Under radar vectoring/in holding/to initial: re-announce from the current position (heading, altitude, speed)
            if (t != null && Phase == Phase.Inbound && vec != null) return Say($"{c}, {VecCall(t, now, full: true, again: true)}", "Approach");
            if (t != null && holding && nav is { } hn) return Say($"{c}, {Steer(t, hn, "the hold")}, {AltTo(t, holdFt, true)}{HoldSpd(t)}.", "Approach");
            if (t != null && Phase == Phase.Entering && nav is { } en) return Say($"{c}, {Steer(t, en, navName)}, {AltTo(t, PatternFt, true)}, report {navName}.");
            if (last == null) return Say($"{c}, {Station}, go ahead.");
            lastAt = now;   // "Enfield 1-1, I say again, taxi to …" (last is kept, otherwise "I say again" doubles)
            return new() { last with { Text = Regex.Replace(last.Text, $@"^(?:{Regex.Escape(c)}, (?:{Regex.Escape(F.StationOf(last.Role))}, )?)?", $"{c}, I say again, ") } };
        }
        if (Has(n, "radio check", "how do you read")) return Say($"{c}, {Station}, read you five.");   // A108: standard phrase (CAP 413)
        if (Has(n, "terrain in sight", "low level")) { lowAck = true; return Say($"{c}, roger.", "Approach"); }   // A106: terrain warning acknowledged
        // R129: reply to "say airspeed" (or "unable speed"): within 30 kt "roger", otherwise the speed once, unable -> cancel the restriction
        if (t != null && Phase == Phase.Inbound && vec != null && VecKt(t) is > 0 and var sk && (spdTold == 2 || Has(n, @"unable (?:the )?speed")) && IasSlot(n) is { } rep)
        {
            lastIntent = "speed";
            if (rep < 0) { spdFree = true; return Say($"{c}, roger, resume normal speed.", "Approach"); }
            (spdTold, spdAt, lastVecSaid) = (3, now, now);   // still off after another 90 s: "resume normal speed"
            return Say(rep == 0 || Math.Abs(rep - sk) <= 30 ? $"{c}, roger." : $"{c}, roger, {(rep > sk ? "reduce" : "increase")} speed {sk} knots.", "Approach");
        }

        // Readback / acknowledgement -> stay silent. Readbacks contain "report ..."/"cleared ..." or read back the controller's last transmission (ReadsBack),
        // own reports do not; the same report within 30 s after the controller's last transmission is usually a readback too.
        // New request: anyone who calls the controller at the start of the transmission ("Kutaisi Tower, ...") or says "request/again/confirm/say" always gets a reply.
        var intent = Intent(n, ground);
        var spoken = Normalize(Regex.Replace(text, @"^\s*[A-Za-z]+:", ""));   // "Ground: " (config frequency/menu) is not a call
        bool request = Addressed(spoken) != null || Has(n, "request", "requesting", "again", "confirm", "verify", "still", "say");
        bool readback = intent != "fieldinsight" && (Has(n, "report", "wilco", "roger", "copy", "cleared", "will do", "affirm", "approved") || ReadsBack(n, intent) || AckOnly(spoken));   // R115: "field in sight" is a report, not a readback; forum 0.9.7: "start up approved, Enfield 1-1" or just the callsign (AIM 4-2-3) is an acknowledgement
        // Readback window from the end of the controller's transmission: lastAt is the call, the transmission lasts ~2.5 words/s (taxi clearance ~15 s)
        bool repeat = intent == lastIntent && now - lastAt < 30 + (last?.Text.Split(' ').Length ?? 0) / 2.5 && !Has(n, "ready") && !(intent == "fieldinsight" && visAsk);   // R115: after "negative field in sight", "field in sight" is the requested report, not a repeat
        // R8: route clearance read back with the assigned code -> "readback correct" (FAA JO 7110.65 4-2-1, ICAO Doc 4444 4.5.7.5)
        if (depClr && lastIntent == "clearance" && Squawk != null && !Has(n, "request") && Regex.Match(n, @"\bsquawk((?: \d+)+)").Groups[1].Value.Replace(" ", "") == Squawk)
        {
            lastIntent = "readback";
            return new() { new Msg(Dep(role), $"{c}, readback correct.") };   // last stays the clearance ("say again" repeats it, further readbacks silent)
        }
        if (!fromMenu && !request && intent is not ("emergency" or "unable") && (readback || repeat)) return new();   // "Unable heading 045" with the controller's heading is not a readback (A28)
        lastIntent = intent;
        if (intent != "cancelapproach") optedOut = false;
        if (leaveAt >= 0) (minDist, leaveAt) = (-1, -1);   // A13: reply to "say intentions": no sign-off, measure again (heading again)
        // "confirm cleared to land?" and the like -> repeat the last instruction ("confirm runway" is answered by weather)
        // A107: compare the value from the question with the last transmission -> "affirm, heading 045" / "negative, heading 055" (otherwise the whole transmission)
        if (Has(n, "confirm", "verify") && intent != "weather" && last != null)
        {
            // Only the question after "confirm"; compare all values in it, any one different -> negative. If it asks about a clearance
            // ("confirm cleared for takeoff runway 25") or a value the last transmission does not have -> repeat the whole transmission.
            var q = Regex.Match(n, @"\b(?:confirm|verify)\b(.*)").Groups[1].Value;
            var l = Normalize(last.Text);
            var parts = new List<string>();
            bool neg = false, miss = false;
            void Cmp<T>(T? qv, T? lv, Func<T, string> say) where T : struct
            {
                if (qv is not { } a) return;
                if (lv is not { } b) { miss = true; return; }
                neg |= !a.Equals(b); parts.Add(say(b));
            }
            Cmp(HdgSlot(q), HdgSlot(l), h => $"heading {Digits(h.ToString("000"))}");
            Cmp(AltSlot(q), AltSlot(l), Alt);
            Cmp(RwySlot(q) is { } qr ? int.Parse(qr) : (int?)null, RwySlot(l) is { } lr ? int.Parse(lr) : (int?)null, r => $"runway {Digits(r.ToString("00"))}");
            bool clr = Has(q, "clear", "takeoff", "take off", "land", "approach", "ils", "line up", "lineup", "hold", "cross", "depart", "visual", "option", "go around", "taxi");
            return new() { miss || clr || parts.Count == 0 ? last : last with { Text = $"{c}, {(neg ? "negative" : "affirm")}, {string.Join(", ", parts)}." } };   // last stays (no Say)
        }

        // A39: ground intents only at the airfield (farthest parking position in the Caucasus 4.2 km): at the FARP/far away there is no airfield Ground, only a notice, phase stays
        if (ground && t != null && intent is "startup" or "taxi" or "clearance" or "taxiin" or "ready" && Dist(t.X, t.Z, CX, CZ) > LocalM)
        {
            lastIntent = "";
            return new() { new Msg("Info", L($"Kein Platz-Ground hier ({F.Name} ist zu weit weg) – nach dem Start {F.Name} Approach rufen.", $"No airfield ground control here ({F.Name} is too far away) - call {F.Name} Approach after takeoff.")) };
        }
        // R44: after the missed approach, check-in with Approach ("Kutaisi Approach, Enfield 1-1, missed approach") -> only now vectors; other request: continue normally
        if (missAt >= 0)
        {
            if (t != null && !ground && Phase == Phase.Inbound && addressed != "Tower" && (intent is "goaround" or "inbound" or "callup" || addressed == "Approach" && intent == "unknown"))
                return MissedVec(t, now);
            if (t != null && !ground && Phase == Phase.Inbound && vec == null) StartVectors(t, true, now, missFt);   // Plan as before, the case below handles the request
            missAt = -1;
        }
        else if (intent == "goaround" && t != null && !ground && Phase == Phase.Inbound && vec != null && now - lastGa < 180 && addressed != "Tower")   // Check-in only after the vectors (45 s): repeat heading/altitude, not "contact Tower" or a new missed approach
            return Say($"{c}, {F.StationOf("Approach")}, {VecCall(t, now, full: true)}", "Approach");
        // Wrong controller called -> refer to the responsible one (as in BMS); R104: on the same frequency the responsible one answers directly
        var resp = intent is "transit" or "transitoff" && addressed is "Approach" or "Tower" ? addressed : Responsible(intent, t);   // N3: transit is answered by the station called
        if (addressed != null && resp != null && addressed != resp && !SameFreq(resp))
            return Neg($"{c}, {Station}, contact {Contact(resp)}.");
        role = resp ?? role;
        bool firstTower = role == "Tower" && towerDue;   // R314: first call to Tower after handover (reply with station name)
        if (role == "Tower" && !ground) towerDue = false;   // Reporting obligation: reported to Tower after "contact Tower"
        if (!ground && Has(n, "gear down", "wheels down", "three green")) gearDown = true;   // R201: gear reported, no wheels-down check
        var hdr = $"{c}, {Station}";

        // Traffic pattern request, also applies together with ready / inbound / final
        // Practice instrument approach = low approach, then straight back to radar vectoring (tick)
        var opt = Has(n, "the option", "for the option") ? "the option"   // R127: touch-and-go or full stop, the pilot decides
                : Has(n, "touch and go", "touch go") ? "touch and go"
                : Has(n, "low pass", "fly by", "flyby") ? "low pass"   // A47: low pass, no pattern afterwards
                : Has(n, "low approach", "practice approach", "practice ils", "practice instrument", "practice tacan", "practice vor") ? "low approach"
                : Has(n, "closed pattern", "pattern work", "circuits", "practice") ? "touch and go" : Has(n, "full stop") ? "full stop" : null;
        if (opt != null) { option = opt; stayPattern = opt is not ("full stop" or "low pass"); }

        // Runway request (V13): up to 10 kt tailwind, valid until landing; rejected -> addition at the end of the reply
        // A41: also on the ground at startup/taxi/clearance ("request taxi, runway 07"): changed runway -> "runway zero seven approved, …", rejected -> only the rejection
        string rwNo = "", rwAck = "";
        bool rwNew = false;   // Runway request changed the runway (A46: re-plan under radar vectoring)
        sayTail = "";
        bool gwish = ground && intent is "taxi" or "startup" or "clearance" && Phase is Phase.Parked or Phase.StartupApproved or Phase.TaxiOut;
        if (t != null && (gwish || !ground && intent is "inbound" or "straightin" or "runway" && Phase is Phase.Away or Phase.Departing or Phase.Inbound) && RwySlot(n) is { } ws && ws + RwySide(n) is var wr)
        {
            // "runway 30 left" -> 30L; without a (matching) side: the runway with that number, the active one first
            var we = F.Ends.FirstOrDefault(e => e.Name == wr) ?? F.Ends.Where(e => e.Name.StartsWith(ws)).OrderBy(e => e.Name != rw).FirstOrDefault();
            double tail = we == null ? 0 : TailKt(t, we.Name);
            if (we == null) rwNo = $"Unable runway {RwSay(wr)}, runway {RwSay(rw)} in use.";
            else if (tail > 10) rwNo = $"Unable runway {RwSay(we.Name)}, tailwind {Digits(((int)Math.Round(tail)).ToString())} knots{(gwish ? $", runway {RwSay(rw)} in use" : "")}.";
            else
            {
                rwNew = we.Name != rw;
                if (gwish && we.Name != rw) rwAck = $"runway {RwSay(we.Name)} approved, ";
                rw = Runway = wantRw = we.Name;
            }
            if (gwish && rwNo != "") return Neg($"{hdr}, {rwNo}");
            if (intent != "runway" && rwNo != "") sayTail = " " + rwNo;
        }
        // A139: similar AI callsign at the airfield (same word, different first digit: "Enfield 5-1" next to "Enfield 1-1"), append once per pair (player callsigns are not in Traffic)
        simPair = null;
        var simDig = new string(callsign.Where(char.IsDigit).ToArray());
        if (FlightSize == 1 && simDig != "")
            foreach (var tr in traffic)
            {
                if (similarSaid.Contains(tr.Group) || !Ops.Callsigns.TryGetValue(tr.Group, out var ac) || Dist(tr.X, tr.Z, CX, CZ) > 30 * NM) continue;
                var am = Regex.Match(ac, @"^([A-Za-z]{3,})\s*(\d)\s*-?\s*(\d)");
                if (!am.Success || am.Groups[2].Value[0] == simDig[0] || !SameCallsign($"{am.Groups[1].Value} {simDig}", callsign)) continue;
                simPair = tr.Group;
                sayTail += $" Caution similar callsign, {SpokenCallsign($"{am.Groups[1].Value} {am.Groups[2].Value}-{am.Groups[3].Value}")} is also on this frequency.";
                break;
            }

        switch (intent)
        {
            case "goaround":
                if (role == "Approach" && Phase == Phase.Inbound && vec != null && t != null)   // R102: first call to Approach after the missed approach ("… contact Approach"), no second missed approach
                    return Say($"{hdr}, identified, {VecCall(t, now, full: true)}", "Approach");
                // R112: go-around only from an approach to here (pattern/final, radar final, or unannounced below 5 NM and 3000 ft), otherwise ask back
                if (t != null && !(Phase is Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand || Phase == Phase.Inbound && vecFinal ||
                                   !ground && Dist(t.X, t.Z, CX, CZ) < 5 * NM && t.Agl < 3000 * Ft))
                {
                    var no = Say($"{c}, say intentions.");
                    no.Add(new Msg("Info", L($"Go-around abgelehnt: Phase {Phase}, {Miles(Dist(t.X, t.Z, CX, CZ))} NM, {Math.Round(t.Agl / Ft / 100) * 100} ft",
                                             $"Go-around rejected: phase {Phase}, {Miles(Dist(t.X, t.Z, CX, CZ))} NM, {Math.Round(t.Agl / Ft / 100) * 100} ft")));
                    return no;
                }
                if (t != null && !ground && (Ifr || wantStraight)) return Missed(t, now, "roger, go around.");   // Instrument approach: missed approach with radar vectoring
                // R258: acknowledge go-around, no second go-around instruction; pattern announced: "left closed traffic approved" (FAA JO 7110.65 3-10-11)
                if (Closed() is var gc && gc != "") { Phase = Phase.Pattern; PatternReset(); return Say($"{c}, roger{gc}."); }
                return GoAround(c, "roger");

            case "emergency":
            {
                // R242: "declaring emergency"/"emergency fuel" without a signal word counts as MAYDAY (JO 7110.65 10-1-1), acknowledgement then without signal word
                mayday = !Has(n, "pan pan") || Has(n, "mayday");   // also on the second emergency call (R116): downgrade MAYDAY -> PAN lets the others taxi again (R37)
                var kind = Has(n, "mayday") ? "roger mayday" : Has(n, "pan pan") ? "roger pan pan" : "roger";
                // R116: further emergency call during an ongoing emergency in the air -> acknowledge, clearance stays valid (JO 7110.65 10-2-5);
                // not in holding (e.g. after "inbound" on a second emergency): then out of holding like the first emergency call, vectors
                if (Emergency && !ground && !holding && Phase is Phase.ClearedLand or Phase.Entering or Phase.Inbound)
                    return Say(Phase == Phase.ClearedLand
                        ? $"{hdr}, {kind}, runway {RwSay(EmergencyRunway(t))}, cleared to land. Emergency services standing by."
                        : $"{hdr}, {kind}, continue as cleared.");
                Emergency = true; emgAsked = false;
                holding = stayPattern = false;
                option = "full stop";
                if (ground)   // R300: nature stated (radio wheel: always) -> no query
                    return Say($"{hdr}, {kind}, emergency services are on the way. Hold position{(Has(n, "fail", "fire", "smoke", "fuel", "hydraulic", "brake", "gear", "medical", "damage", "bird", "electric", "flame ?out", "leak", "engine", "hung", "hot gun") ? "" : ", say nature of emergency")}.");
                var er = EmergencyRunway(t);
                // R37: close to the airfield (pattern, aligned within 6 NM): Tower gives the landing clearance right away; otherwise Approach leads with vectors
                // to final (descent at own discretion), landing clearance from Tower at about 5 NM (tick). PAN: others do not hold
                if (t == null || Dist(t.X, t.Z, CX, CZ) < 5 * NM || F.Ends.Any(e => OnFinal(t.X, t.Z, t.Hdg, t.Agl, e.Name, 6 * NM)))
                {
                    if (TrafficOnRunway(traffic)) return EmgBusy($"{c}, {F.StationOf("Tower")}, {kind}, runway {RwSay(er)}, {Wind(t)}, {Qnh(t)}", traffic);   // R357
                    Phase = Phase.ClearedLand;
                    return Say($"{c}, {F.StationOf("Tower")}, {kind}, runway {RwSay(er)}, {Wind(t)}, {Qnh(t)}, cleared to land, emergency services standing by.", "Tower");
                }
                Phase = Phase.Inbound;
                Runway = er; entry = null; wantStraight = true;
                var v = Cap(StartVectors(t, true, now));
                bool told = fromMenu && t != null || Has(n, "souls? on board") && Has(n, "fuel");   // R244: radio-wheel emergency call states fuel and persons (Program.PilotCall), then no query
                return Say($"{c}, {F.StationOf("Approach")}, {kind}{(mayday ? ", all traffic is holding" : "")}. {v} Runway {RwSay(Runway)} available, {Qnh(t)}" +
                           (told ? "." : ", say souls on board and fuel remaining."), "Approach");   // R243
            }

            case "minfuel":   // A29: acknowledge only (AIM 5-5-15), no priority (only after an emergency)
                if (ground) goto case "ground_say";
                return Say($"{hdr}, roger{(Has(n, "minimum fuel", "min fuel") ? " minimum fuel" : "")}{(QueueAhead >= 0 ? $", number {QueueAhead + 1}" : "")}, " +
                           (QueueAhead > 0 && !Spaced ? $"expect approach in {Expect(PatternCount(traffic))} minutes." : "expect no delay."));

            // R251 (AFI 13-212, AFI 91-212, base local procedures): hung ordnance/hot gun -> straight-in, after landing to the de-arm area; bird strike -> ask intention;
            // hot brakes -> hot-brake area, fire service comes
            case "hung":
                special = "de-arm area";
                if (ground) { if (Phase == Phase.TaxiIn) goto case "taxiin"; return Say($"{c}, roger."); }
                wantStraight = true;
                return Say($"{hdr}, roger {(Has(n, "hot gun") ? "hot gun" : "hung ordnance")}, expect straight-in runway {RwSay(rw)}.");

            case "birdstrike":
                // Review R251: intention already stated ("request return to field", "RTB", landing): no query, straight-in/instrument approach right away like "request straight in"
                if (!ground && t != null && Has(n, "return", "r t b", "rtb", "recover", "land", "straight in", "inbound"))
                {
                    var bs = OnTranscript("request straight in", t, traffic, now, true);
                    if (bs.Count == 0) return bs;
                    var rest = Regex.Replace(bs[0].Text, $"^(?:{Regex.Escape(hdr)}|{Regex.Escape(c)}),\\s*(?:roger,\\s*)?", "");
                    bs[0] = this.last = bs[0] with { Text = $"{hdr}, roger bird strike. {Cap(rest)}" };
                    return bs;
                }
                return Say($"{hdr}, roger bird strike, say intentions.");

            case "hotbrakes":
                if (!ground) goto default;
                Phase = Phase.TaxiIn;
                taxiInTold = specArea = true;
                lastParkCall = -1;   // no taxi guidance to the old parking position (A36); afterwards "request taxi to parking" normal
                return Say($"{c}, roger, taxi to the hot brake area, fire department responding.");

            case "cancelapproach":
                return Say($"{hdr}, roger, {CancelApproach(t)}");

            case "cancelemergency":
                // R295: clearances remain valid (FAA JO 7110.65 3-10-5, 4-8-1); downwind only from initial/pattern, without emergency or far out only "roger"
                if (!Emergency) return Say($"{c}, roger.");
                Emergency = false;
                if (ground) return Say($"{c}, roger, emergency cancelled.");
                if (Phase == Phase.ClearedLand) return Say($"{c}, roger, emergency cancelled, runway {RwSay(EmergencyRunway(t))}, cleared to land.");
                if (Phase is Phase.Inbound or Phase.Entering) return Say($"{c}, roger, emergency cancelled, continue as cleared.");   // Radar vectoring or approach continues
                if (Phase is not (Phase.Initial or Phase.Pattern)) return Say($"{c}, roger.");
                Phase = Phase.Pattern;
                PatternReset();
                return Say($"{c}, roger, emergency cancelled. Join {Hand(rw)} downwind, {Alt(PatternFt)}, report base.");

            case "startup":
            {
                bool first = Phase == Phase.Parked;   // A33: runway and QNH only in the first call and only if the pilot did not give the current ATIS identifier
                Phase = Phase.StartupApproved;
                home = NearestRamp(t);
                var info = first && !AtisHeard(n) ? " " + Cap($"{(AtisLetter == "" ? "" : $"information {Phonetic(AtisLetter)} is current, ")}runway {RwSay(rw)} in use, {Wind(t)}, {Qnh(t)}.") : "";   // R209: without ATIS also the surface wind (ICAO Doc 4444)
                return Say($"{hdr}, {rwAck}start up approved.{info} Report ready to taxi.");
            }

            case "taxiin":
                if (t != null && OnRunwayPos(t.X, t.Z)) { lastIntent = ""; return Say($"{c}, report runway vacated."); }   // A91: taxi clearance to parking only off the runway (no Rejected, phase stays; lastIntent empty, otherwise the requested report "runway vacated" counts as a repeat)
                Phase = Phase.TaxiIn;
                taxiInTold = true;
                specArea = special != null;
                ProgStart(Has(n, "progressive"), now);
                if (special is { } sp) { special = null; lastParkCall = -1; return Say($"{c}, taxi to the {sp}."); }   // R251: once de-arm area instead of parking position (afterwards "request taxi to parking" normal), no taxi guidance to the old parking spot
                if (t != null && FreeSpot(t, traffic) is { } fs) { spot = (fs.X, fs.Z); parkNum = fs.Num; home = NearestRamp(t with { X = fs.X, Z = fs.Z }); }   // named ramp = that of the parking spot
                var park = parkNum > 0 ? $"parking spot {Digits(parkNum.ToString())}" : "parking";
                var where = t != null && ParkDir(t) is { } pd ? (parkNum > 0 ? $", {pd}" : $", your parking position {pd}") : "";
                return Say(F.Charted && home != null ? $"{c}, taxi to {home.Name}{ParkRoute()}{(parkNum > 0 ? ", " + park : "")}{where}." : $"{c}, taxi to {park}{where}.");

            case "clearance":   // V14: route clearance; destination = named airfield, otherwise out of the control zone
            {
                var df = ClrDest(n);
                if (Hostile(df)) return Neg($"{c}, unable clearance to {df!.Name.Replace('-', ' ')}, say alternate destination.");   // N1: hostile target
                dest = df ?? Limit(rw);   // R8: clearance limit always an airfield, without destination the next one in departure direction
                depClr = true;
                depFt = ClimbFt(rw);
                cruiseFt = CruiseLevel();
                if (t != null && !ground && df != null)   // R113: pop-up IFR in the air (named destination; R8 clearance limit only on the ground): route from here, no approach; handover to the destination airfield at 15 NM (R43)
                {
                    bool known = Phase is Phase.Inbound or Phase.Entering || Following;   // already identified
                    var sq = Squawk == null ? $"squawk {Digits(TakeSquawk())}, " : "";
                    CancelApproach(t);
                    Phase = Phase.Away; handedOff = true; Following = false;
                    popM = Dist(t.X, t.Z, CX, CZ) + 5 * NM;   // already far out: do not hand over on the very next tick
                    if (!known && sq != "") { idAt = now + 6; idFf = false; popDf = df; return Say($"{hdr}, {sq.TrimEnd(' ', ',')}.", "Approach"); }   // R219/R128: first the code, "radar contact" and clearance after observing (tick)
                    return Say($"{hdr}, {sq}" + (known ? PopClr(t, df) : $"radar contact, {Where(t)}. {Cap(PopClr(t, df))}"), "Approach");
                }
                return Say($"{hdr}, {rwAck}cleared {(dest != null ? $"to {dest.Name.Replace('-', ' ')} airport" : "out of the control zone")} via radar vectors, " +
                           $"after departure fly runway heading, climb and maintain {Alt(depFt)}, expect {Alt(cruiseFt)} ten minutes after departure, " +
                           $"{(F.Own.ContainsKey("Departure") ? $"departure frequency {FreqSay(FreqOf(F, "Departure"))}" : $"departure {Contact("Approach")}")}, squawk {Digits(TakeSquawk())}.");   // own departure frequency (AirfieldFrequencies): the one in the clearance
            }

            case "alternate":   // N40: new IFR destination in the air, Departure leads there (handover at 15 NM to its Approach, R43)
            {
                var af = Named(n)!;
                if (Hostile(af)) return Neg($"{c}, unable clearance to {af.Name.Replace('-', ' ')}, say alternate destination.");
                (dest, altAsk, cruiseFt) = (af, false, 0);
                var an = af.Name.Replace('-', ' ');
                return Say($"{c}, cleared to {an} airport{(t != null ? $", {Steer(t, (af.X, af.Z), an)}" : "")}, climb and maintain {Alt(Cruise)}.", "Approach");
            }

            case "taxi":
            {
                if (VfrBlocked) return Neg(VfrUnable(hdr));
                bool first = Phase == Phase.Parked;   // First call: check ATIS identifier, "request startup and taxi" answers both; A33: QNH only here and only without reported ATIS, not again after "start up approved"
                Phase = Phase.TaxiOut;
                home = NearestRamp(t);
                exitDir = dir ?? exitDir;
                ProgStart(Has(n, "progressive"), now);
                var hp = t != null && progressive ? ParkDir(t, HoldPt(rw, t)) : null;   // N26: "request progressive taxi" right in the taxi request
                (crossOk, crossWait) = (null, null);
                var hx = t != null && CrossNext(t) is { } x0 ? $", hold short runway {RwSay(x0)}" : "";   // Forum 0.9.7 (FAA JO 7110.65 3-7-2): parallel runway on the way, crossing only on report at the holding point
                var go = IsHeli ? $"air taxi{Route(rw)} to holding point runway {RwSay(rw)}" : $"taxi to holding point runway {RwSay(rw)}{Route(rw)}";   // R347: helicopter air taxi (FAA JO 7110.65 3-11-1)
                return Say($"{hdr}, {rwAck}{(first && StartReq(n) ? "start up approved, " : "")}{go}{hx}{(hp != null ? ", " + hp : "")}" +
                           $"{(first && !AtisHeard(n) ? $", {Wind(t)}, {Qnh(t)}.{AtisNote(n)}" : ".")}{(depClr ? "" : $" Squawk {Digits(TakeSquawk())}.")}" +
                           (SameFreq("Tower") ? $" Hold short runway {RwSay(rw)}, report ready for departure." : $" Hold short runway {RwSay(rw)}, contact {Contact("Tower")} when ready for departure."));   // R104: no change to the same frequency
            }

            case "progressive":   // N26: only TaxiOut/TaxiIn (intent), otherwise the request runs within the taxi request
            {
                ProgStart(true, now);
                if (t == null || ProgTarget(t) is not { } tg || ParkDir(t, tg) is not { } pdir) return Say($"{c}, roger.");
                return Say($"{c}, {ProgName(rw)} {pdir}.", "Ground");
            }

            case "ready":
            {
                exitDir = F.Charted ? dir ?? exitDir ?? F.Exit[rw][0] : dir ?? exitDir;   // A92: without a map the named direction stays as the departure direction (no CRP)
                bool onRwy = t != null && OnRunwayPos(t.X, t.Z);
                if (Phase is Phase.Parked or Phase.StartupApproved && !onRwy)
                    return Neg($"{hdr}, negative, you are not cleared to taxi. {(SameFreq("Ground") ? "Request taxi." : $"Contact {Contact("Ground")} for taxi.")}");   // R104
                if (!AtHold(t))
                    return Neg($"{c}, negative, you are not at the holding point. Continue taxi to holding point runway {RwSay(rw)}" +
                               $"{Route(rw)}, report ready for departure.");
                if (Ifr && stayPattern && Has(n, "closed pattern", "pattern work", "circuits") && !Has(n, "approach"))   // R253: no visual pattern in IMC (FAA JO 7110.65 3-10-11), practice approach yes
                { option = null; stayPattern = false; return Neg($"{c}, closed traffic not approved, I F R conditions."); }
                if (VfrBlocked) return Neg(VfrUnable(c));
                return Say(DepartureClearance(t, traffic, now));
            }

            case "cross":   // Forum 0.9.7: at the parallel runway holding point (also "ready for departure" there): no takeoff clearance, crossing of the parallel runway, holding point of the assigned one
                return Say(Cross(CrossNext(t!)!, traffic));

            case "rto":   // R100: takeoff clearance expired; HoldShort with lineUp (no "not cleared onto runway" when vacating), holdSince -2 (no auto-clearance, reporting obligation #14)
                Phase = Phase.HoldShort; lineUp = true; holdSince = -2; rejecting = true;   // R267: vacate and to Ground as after landing, not "report ready for departure" (FAA JO 7110.65 3-10-9)
                return Say($"{c}, roger. {Vacate(rw)} {(SameFreq("Ground") ? "Report vacated, say intentions." : $"Contact {Contact("Ground")} when vacated.")}", "Tower");

            case "ff":   // V15
                if (Phase is Phase.Inbound or Phase.Entering) return Say($"{c}, roger, continue as cleared.", "Approach");   // A103: already on radar (radar vectoring/approach), no restart
                Following = true;
                if (Phase is Phase.ClearedTakeoff or Phase.Departing) { Phase = Phase.Away; nav = null; handedOff = true; }   // A51: counts as check-in like "airborne", otherwise tick signs off at the zone edge anyway
                if (Squawk == null && t != null && Phase == Phase.Away && !OnGround(t)) { idAt = now + 6; idFf = true; return Say($"{hdr}, squawk {Digits(TakeSquawk())}.", "Approach"); }   // R128: first the code, "radar contact" after observing (tick)
                return Say($"{hdr}, {(Squawk == null ? $"squawk {Digits(TakeSquawk())}, " : "")}{FfRc(t)}", "Approach");

            case "ffoff":
            {
                Following = false; transitFt = null;
                bool depOff = handedOff && Phase == Phase.Away;   // A51: this is the departure sign-off (at 15 NM a second one would follow), same ending as there
                var head = depOff && depClr && nav != null ? $"proceed direct {navName}, resume own navigation. Radar service terminated" : "radar service terminated";
                var end = depOff && AwacsContact is { } awc ? $"contact {awc}." : SquawkVfr() + "frequency change approved.";   // A90: squawk VFR only after an assigned code, IFR never
                var ho = depOff ? IfrHandoff() : null;   // R43: IFR route to another airfield -> handover to its Approach, as at 15 NM
                var bye = Say(ho != null ? $"{c}, {ho}" : $"{c}, {head}, {end}", "Approach");   // before the reset: sign-off still from Departure (Dep)
                if (depOff) { depClr = false; nav = null; handedOff = false; popM = 0; }
                Released |= Phase == Phase.Away;   // R47: signed off, pin released
                return bye;
            }

            case "traffic":   // "traffic in sight", "looking", "negative contact": acknowledge, remember the state of the last advisory (N24), never radar vectoring
                if (advised.Count > 0 && advised.Values.MaxBy(v => v.At) is { } last)
                    last.St = Has(n, "negative", "not in sight", "no joy") ? 2 : Has(n, "in sight", "tally") ? 1 : 0;
                return Say($"{c}, roger.");

            case "airborne_ready":
                return Say($"{c}, you are airborne. Report intentions.");

            case "airborne":   // Report to Departure (Approach) after handover
                // R7: VFR still in the zone (not at the departure CRP): departure clearance remains valid, sign-off at the zone edge
                zoneReport = !depClr && Phase is Phase.ClearedTakeoff or Phase.Departing && t != null && Dist(t.X, t.Z, CX, CZ) < CtrM && !AtExit(t);
                dueAt = -1;   // Reporting obligation: checked in; #10 "leaving the control zone" is due from now on
                Phase = Phase.Away;
                nav = null; handedOff = true;
                if (Squawk == null && t != null && !OnGround(t)) { idAt = now + 6; idFf = false; return Say($"{hdr}, squawk {Digits(TakeSquawk())}.", "Approach"); }   // R128: flight without a prior code (no taxi call): assign first, "radar contact" after observing (tick)
                return Say($"{hdr}, {AirRc(t)}", "Approach");   // R7 (zoneReport) in AirRc

            case "leavezone":   // R7: "leaving the control zone" after "report leaving the control zone"
                return LeaveZone(c, "roger");

            case "air_ground":
                return Neg($"{c}, negative, you are airborne. Say intentions.");

            case "closed":   // R202: closed pull-up from low approach, after touch-and-go/go-around, from upwind or initial (FAA JO 7110.65 3-10-11)
                if (Phase is not (Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand)) return Neg($"{c}, negative, you are not in the pattern, say intentions.");
                if (Ifr || Emergency || OtherEmergency) return Neg($"{c}, closed traffic not approved, {(Ifr ? "I F R conditions" : "emergency in progress")}.");
                stayPattern = true; wantStraight = false;
                if (Phase == Phase.ClearedLand) { if (option is null or "full stop") option = "low approach"; }   // from the landing clearance: pull up instead of landing (tick: next circuit)
                else { Phase = Phase.Pattern; PatternReset(); }
                return Say(Phase == Phase.ClearedLand ? $"{c}, {ClearText()}." : $"{c}{Closed()}.");   // R259: state the changed clearance ("cleared low approach, left closed traffic approved")

            case "sfo":   // R204: simulated flameout, reporting points high key -> low key, clearance there (FAA JO 7110.65 3-10-13); visual only, no foreign emergency
            case "highkey":
            case "lowkey":
            {
                // R296: real flameout ("flameout, high key" or after own emergency call): reporting points also in IMC, always "cleared to land" (F-16 flameout landing)
                if (!Emergency && Has(n, "flame ?out") && !Has(n, "simulated", "practice")) { Emergency = true; mayday = true; emgAsked = false; }
                if (OtherEmergency || Ifr && !Emergency) return Neg($"{c}, unable S F O, {(Ifr ? "I F R conditions" : "emergency in progress")}.");
                if (t != null && Dist(t.X, t.Z, CX, CZ) > 10 * NM) return Neg($"{c}, negative, you are {Where(t)}. Report inbound to {F.StationOf("Approach")}.");
                var sw = WxDue(n) ? $", {Wind(t)}, {Qnh(t)}" : "";
                if (!sfo)
                {
                    PatternReset(); sfo = true;
                    option = Emergency ? "full stop" : opt ?? "low approach"; stayPattern = option is not ("full stop" or "low pass");
                    Phase = Phase.Entering; vec = null; holding = false; entry = null; needWx = false;
                }
                if (intent == "sfo") { navName = "high key"; return Say($"{c}, runway {RwSay(rw)}{sw}, report high key."); }
                if (intent == "highkey") { navName = "low key"; return Say($"{c}, report low key."); }
                navName = ""; landCall = true;
                if (TrafficOnRunway(traffic) || EmgWithin(8)) return Say($"{c}, continue, {(EmgWithin(8) ? "emergency traffic" : BusyWhy(traffic))}.");   // Tick gives the clearance as soon as clear
                Phase = Phase.ClearedLand;
                return Say($"{c}, {Wheels()}runway {RwSay(rw)}{(sw == "" ? $", {Wind(t)}" : sw)}, {ClearText()}.");
            }

            case "option":
                if (option == "low pass" && Phase == Phase.Away)   // A47: low pass from outside over final (pattern procedure without circuit, Pattern gives the clearance on final)
                {
                    Phase = Phase.Pattern; PatternReset(); transitFt = null;
                    return Say($"{c}, report five mile final runway {RwSay(rw)}.");
                }
                if (Phase is Phase.Away or Phase.Departing || Phase == Phase.Inbound && vec == null)   // R254: from outside first call like "inbound" (weather, entry/vectors, far out Approach), option stays stored
                {
                    var ir = Responsible("inbound", t) ?? role;
                    if (ir != role && !SameFreq(ir)) return Neg($"{hdr}, contact {Contact(ir)}.");
                    role = ir; hdr = $"{c}, {Station}";
                    goto case "inbound";
                }
                if (Phase == Phase.ClearedLand) return Say($"{c}, {Wheels()}runway {RwSay(rw)}, {Wind(t)}, {ClearText()}.");   // R327: new clearance instead of "approved, report base" (FAA JO 7110.65 3-8-1)
                return Say($"{c}, {(option == "the option" ? "option" : option)} approved, {(t != null && OnFinalLoose(t, rw) ? "continue" : $"report {RepPt()}")}.");

            case "transit":   // N3: transit over the pattern; phase stays Away (no landing sequence, no landing clearance)
            {
                if (t == null) return Say($"{c}, say position.");
                int busy = PatternCount(traffic);
                if (busy >= 2 || OtherEmergency || QueueAhead >= 0 && !Spaced) return Say($"{hdr}, remain outside the control zone, expect transit in {Math.Max(2, Expect(busy))} minutes.");
                if (Phase == Phase.Departing) { Phase = Phase.Away; nav = null; }
                if (transitFt == null) { transitIn = false; TransitTold.Clear(); dueAt = -1; }   // repeated clearance (readback with callsign): no traffic info again, entry stays
                (transitFt, transitBy) = (TransitFt, role);
                transitDir = Regex.Match(n, @"\b((?:north|south)?(?:east|west)?)bound\b") is { Success: true } bm && bm.Groups[1].Value != "" ? bm.Groups[1].Value : Dir8(t.Hdg * 180 / Math.PI);
                return Say($"{hdr}, cleared to cross the {F.Name.Replace('-', ' ')} control zone overhead, {transitDir}bound, not below {Alt(transitFt.Value)}, {Qnh(t)}, " +
                           $"runway {RwSay(rw)} in use, report clear of the zone.");
            }

            case "transitno":   // R389: wheel/F10 "request zone transit" while inbound or in the pattern
                return Say($"{hdr}, unable zone transit, say intentions.");

            case "transitoff":   // N3: "clear of the zone"
                transitFt = null;
                Released |= Phase == Phase.Away;   // R47: pin released
                return Say($"{c}, frequency change approved.");

            case "separate":   // V17: flight lands individually, Program dissolves the section
            {
                if (FlightSize < 2) return Say($"{c}, roger.");
                Separate = true;
                if (!Has(n, "trail")) return Say($"{c}, separate landing approved, flight members report final individually.");
                var res = OnTranscript("request instrument approach", t, traffic, now, true);
                return res.Select((m, i) => i == 0 ? m with { Text = m.Text + " Radar trail approved, flight members maintain two miles in trail, report final individually." } : m).ToList();
            }

            case "straightin":
            {
                if (role == "Tower" && Phase == Phase.Entering && t != null && navName == "circle") return CircleJoin(t, traffic, hdr);   // R212: "ILS runway 13, circling runway 31"
                if (role == "Tower" && Phase == Phase.Entering && t != null)   // R102: first call to Tower after "cleared ILS …, contact Tower" (Responsible), not back to Approach
                    return Say($"{hdr}, {Wind(t)}, {Qnh(t)}, continue approach, report four mile final.{TrafficNote(traffic, rw)}");
                wantStraight = true;
                if (intent != "fieldinsight") { wantVisual = Has(n, "visual approach"); visSeen = wantVisual && Has(n, FieldSight) && !Has(n, "negative", "not in sight", "no joy"); visAsk = false; }   // P3-AP7: also after radar vectoring/holding "cleared visual approach" instead of ILS; R115: airfield already reported in the request
                if (QueueAhead >= 0 && !Spaced || OtherEmergency) goto case "inbound";   // Sequence also applies to instrument approaches: holding first
                if (t != null && !OnFinalLoose(t, rw) && (Dist(t.X, t.Z, CX, CZ) > 8 * NM || !FinalOk(rw) || Proc(rw) != "" && !wantVisual))   // R317: instrument approach from the side: vectors (≤ 30°, position before heading), not across the 6-NM point   // terrain on final: plan instead of straight-in
                {
                    var head = Phase is Phase.Inbound or Phase.Entering ? $"{c}, roger, {(wantVisual ? "expect visual approach" : "straight in approach approved")}. " : $"{hdr}, radar contact, {Where(t)}, {Qnh(t)}. ";   // already announced (inbound): no new first contact
                    Phase = Phase.Inbound;
                    entry = null;
                    var v = Cap(StartVectors(t, true, now));
                    return Say(head + (vecOver || Runway != rw ? PlanText(rw) : "") + v, "Approach");
                }
                if (wantVisual && !visSeen && t != null)   // R115: visual approach only with airfield in sight (AIM 5-4-23, JO 7110.65 7-4-2): stay with Approach, wait for "field in sight" (tick: fallback from 4 NM)
                {
                    var ask = Phase is Phase.Inbound or Phase.Entering ? $"{c}, roger, " : $"{hdr}, radar contact, {Where(t)}, {Qnh(t)}. ";
                    Phase = Phase.Inbound; entry = null; nav = null; visAsk = true;
                    return Say($"{ask}expect visual approach runway {RwSay(rw)}, report field in sight.", "Approach");
                }
                Phase = Phase.Entering;
                handoffAt = now; towerDue = true; towerNag = false;   // R32; reporting obligation: call to Tower expected
                entry = null;
                var faf = OnCenterline(rw, 6 * NM);
                nav = null; navName = "six mile final";   // also already on final (no heading): straight-in approach (PAR calls, "final"), no old heading
                string vec = t != null && Dist(t.X, t.Z, faf.X, faf.Z) > 1.5 * NM && !OnFinalLoose(t, rw)
                    ? $" {Cap(Steer(t, faf, "six mile final"))} for six mile final." : "";
                // R42: state altitude at 2-8 NM, at or below the glide path (3°, rounded down to 500 ft, never higher than he flies), never below the minimum altitude (on final like Excess terrain + 250 ft on the centerline, otherwise MVA up to the final approach point); IFR "until established"; visual approach without altitude
                bool vis = wantVisual;
                string alt = "";
                if (!vis && t != null && AL(t.X, t.Z, rw).A is var ra && OnFinalLoose(t, rw) is var fin && (fin ? ra : 6 * NM) is var refA && refA <= 8 * NM && refA >= 2 * NM)
                {
                    var rp = Pt(rw, refA, 0);
                    double gsFt = Math.Floor((FieldElev / Ft + refA / NM * 318) / 500) * 500, now100 = Math.Round(IndFt(t) / 100) * 100;
                    double fl = fin ? FinalFloor(rw, refA) : F.MvaLeg(t.X, t.Z, rp.X, rp.Z);   // MVA (terrain + 1000 ft) would lie above the glide path on a short final
                    double af = Math.Max(fl, Math.Min(gsFt, now100));
                    alt = Proc(rw) != "" ? $"{AltTo(t, af)} until established{(Proc(rw) == "ILS" ? " on the localizer" : "")}, " : $" {Cap(AltTo(t, af))}.";
                }
                // R220: clearance without localizer frequency (it is in the "expect"); R217: climb-out on practice approach; R271: PAR without lost-comm instruction (R221) as long as there is no talk-down
                return Say((vis ? $"{hdr}, cleared visual approach runway {RwSay(rw)}, {Qnh(t)}."
                    : Proc(rw) != "" ? $"{hdr}, {alt}cleared {Proc(rw)} approach runway {RwSay(rw)}{OnTheGo(rw)}, {Qnh(t)}."
                    : $"{hdr}, straight in approach runway {RwSay(rw)} approved{OnTheGo(rw)}, {Qnh(t)}.") + vec + (Proc(rw) != "" ? "" : alt) +
                    $" Contact {Contact("Tower")}.");   // R314: Tower gives the reporting point
            }

            case "fieldinsight":   // R115: reply to "report field in sight" -> visual approach clear; otherwise acknowledge only (remember visibility in case radar vectoring to the visual is running)
                if (Has(n, "negative", "not in sight", "no joy")) return Say($"{c}, roger, report field in sight.");
                if (!visAsk) { visSeen |= wantVisual; return Say($"{c}, roger."); }
                visAsk = false; visSeen = true;
                goto case "straightin";

            case "final":
                return Landing(t, traffic, firstTower ? hdr : c);

            case "initial":
            {
                if (role == "Approach") goto case "inbound";   // Approach leads to initial (radar vectoring or holding)
                if (Ifr) return Neg($"{c}, negative, field is I F R. Expect vectors for {ProcSay(rw)}, report inbound to {F.StationOf("Approach")}.");
                // Straight-in approach from Approach (cleared ... approach): no break, continue straight in (otherwise no monitoring on final)
                if (navName == "six mile final" && Phase == Phase.Entering)
                    return Say($"{c}, make straight in runway {RwSay(rw)}, report four mile final.");   // R274: VFR without procedure: no "approach clearance" (FAA JO 7110.65 7-4-2)
                if (t != null)
                {
                    var ip = OnCenterline(rw, InitialDist);
                    var (al, lat) = Approach(t.X, t.Z, rw);
                    // Initial applies only on the axis (up to 0.5 NM lateral, R31): at the IP (2.5 NM) or on the extended centerline on approach ("7-mile initial", e.g. right after handover from Approach)
                    if (lat > InitialLat || Dist(t.X, t.Z, ip.X, ip.Z) > 2.5 * NM && !(al > NM && al < gate + 4 * NM && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 45))
                    {
                        needWx |= WxDue(n);
                        Phase = Phase.Entering; lastAltFt = PatternFt;   // R256
                        return Neg($"{c}, negative, you are {MilesTxt(Dist(t.X, t.Z, ip.X, ip.Z))} from initial. " +
                                   $"{Cap(Steer(t, ip, "initial"))}, {Alt(PatternFt)}, report initial runway {RwSay(rw)}.");
                    }
                }
                return InitialClear(t, traffic, FlightSize > 1 && Regex.Match(n, @"\b(\d+) second") is { Success: true } bi ? $"{bi.Groups[1].Value} second break approved, " : "", WxDue(n));
            }

            case "overhead":
                if (role == "Approach") { wantOverhead = true; goto case "inbound"; }
                if (Ifr && !(Phase == Phase.Entering && navName == "overhead")) return Neg($"{c}, negative, field is I F R, no overhead. Report inbound to {F.StationOf("Approach")}.");   // Circling (tower itself sent overhead): call counts
                if (t != null && Dist(t.X, t.Z, Ovh.X, Ovh.Z) > 2.5 * NM)
                { ovhAsked = ovhWaved = false; return Neg($"{c}, negative, you are {Where(t)}. {Cap(Steer(t, Ovh, "overhead"))}, report overhead."); }   // new attempt to overhead: reporting obligation from the start
                return OverheadJoin(t, WxDue(n));

            case "pattern":
            {
                if (t != null && Dist(t.X, t.Z, CX, CZ) > 5 * NM)
                    return Neg($"{c}, negative, you are {Where(t)}, not in the pattern. Report {(F.Charted ? "C R P or " : "")}initial.");
                if (t != null && PatternOffset(t.X, t.Z, rw) < -900)
                    return Say($"{c}, wrong side, {Hand(rw)} pattern runway {RwSay(rw)}, report base.");
                // A44: "base" after the break or in the pattern in base position (or already on final) is, like "final", the landing report; gear up without "gear down": first "check wheels down"
                // R200: "base" also valid when turning in from downwind (behind the threshold, pattern side) (otherwise loop "report base")
                if (t != null && Has(n, "base") && !Has(n, "downwind", "break") &&
                    (Phase is Phase.Initial or Phase.Pattern or Phase.ClearedLand || Phase is Phase.Away or Phase.Entering && !Ifr && OnBase(t, rw)) &&   // R328: first call on base = landing report
                    (OnBase(t, rw) || Phase != Phase.Initial && OnFinalLoose(t, rw) ||
                     Phase == Phase.Pattern && PatternOffset(t.X, t.Z, rw) > 200 && Approach(t.X, t.Z, rw).along is > 0 and < 4 * NM && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) < 160))
                {
                    if (t.Gear is >= 0 and < 0.5 && option is not ("low approach" or "low pass") && !Has(n, "gear down", "wheels down")) return Say($"{c}, check wheels down.");
                    return Landing(t, traffic, c, true);
                }
                // Review l1: "base" on downwind (away from the airfield): no base, sequence and "report base" when he is there
                if (t != null && Has(n, "base") && !Has(n, "downwind", "break") && Phase is Phase.Initial or Phase.Pattern && PatternOffset(t.X, t.Z, rw) > 200 && HdgDiff(t.Hdg * 180 / Math.PI, LandHdg(rw)) > 135)
                {
                    var (bn, bl) = Sequence(t, traffic, rw);
                    return Say($"{c}, negative, I show you on downwind, {(bn > 1 ? $"number {bn}, follow the {Follow(bl!, rw)}" : "number one")}, report base.");
                }
                var pw = WxDue(n) ? $"runway {RwSay(rw)}, {Wind(t)}, {Qnh(t)}, " : "";   // R126
                needWx = false;
                if (Phase != Phase.Initial || t != null && !OnFinal(t.X, t.Z, t.Hdg, t.Agl, rw, 8 * NM)) Phase = Phase.Pattern;   // "base" still on initial (also before the 3-NM zone): initial stays (otherwise landing clearance before the break), tick switches on downwind
                nav = null;
                var (no, lead) = Sequence(t, traffic, rw);
                return Say(no > 1 ? $"{c}, {pw}number {no}, follow the {Follow(lead!, rw)}, report base." : $"{c}, {pw}number one, report base.");
            }

            case "crp":
            {
                vec = null; crpAsked = crpWaved = false;   // new attempt to C R P: reporting obligation from the start
                if (!F.Charted)   // without map: radar vectoring by terrain
                {
                    var no = $"{c}, negative, no reporting points at {F.Name.Replace('-', ' ')}. ";
                    if (holding) return Neg(no + "Continue holding.");
                    if (t == null) { Phase = Phase.Entering; return Neg(no + $"Proceed to initial runway {RwSay(rw)}, report initial."); }
                    Phase = Phase.Inbound;
                    var v = Cap(StartVectors(t, Ifr || wantStraight, now));
                    return Neg(no + PlanText(rw) + v);
                }
                var at = dir ?? entry ?? Allowed(rw)[0];
                if (!Allowed(rw).Contains(at))
                {
                    entry = AssignEntry(t, rw, null);
                    Phase = Phase.Inbound;
                    return Neg($"{c}, negative, C R P {at} is not available for runway {RwSay(rw)}. " +
                               $"Enter via C R P {entry}{SteerTo(t, Crp[entry], $"C R P {entry}")}, report C R P {entry}.");
                }
                if (t != null && Dist(t.X, t.Z, Crp[at].X, Crp[at].Z) > 2.5 * NM)
                {
                    entry = at;
                    Phase = Phase.Inbound;
                    return Neg($"{c}, negative, you are {MilesTxt(Dist(t.X, t.Z, Crp[at].X, Crp[at].Z))} from C R P {at}. " +
                               $"{Cap(Steer(t, Crp[at], $"C R P {at}"))}, report C R P {at}.");
                }
                entry = at;
                if (holding) { holdFt = HoldFtAt(Crp[at]); holdArrived = false; lastHoldInfo = now; Phase = Phase.Inbound; nav = Crp[at]; navName = $"C R P {at}"; return Say($"{c}, hold at C R P {at}, {Alt(holdFt)}{HoldSpd(null)}, I will call you."); }
                return AtCrp(at);
            }

            case "inbound":
            {
                Expected = false;
                if (Phase == Phase.Entering && role == "Tower" && t != null && navName == "circle") return CircleJoin(t, traffic, hdr);   // R212
                if (Phase == Phase.Entering && role == "Tower" && t != null)   // First call after handover from Approach (also close to the airfield: continue as handed over)
                    return Say($"{hdr}, {Wind(t)}, {Qnh(t)}, " + (navName == "six mile final" ? "continue approach, report four mile final."
                               : $"{(lastAltFt >= 0 ? "" : AltTo(t, PatternFt) + ", ")}report {(navName == "overhead" ? "overhead" : $"initial runway {RwSay(rw)}")}.") + TrafficNote(traffic, rw));   // R256: altitude already instructed by Approach (lastAltFt), Tower does not repeat it
                if (Phase is Phase.Initial or Phase.Pattern && role == "Tower")   // already in (call crosses e.g. with the break clearance): repeat, do not re-sequence
                    return Say($"{hdr}, runway {RwSay(rw)}, {(Phase == Phase.Initial ? "" : "continue, ")}report base.");
                if (t != null && Dist(t.X, t.Z, CX, CZ) < 4 * NM && !(Phase == Phase.Inbound && vec != null && !holding))   // under radar vectoring (e.g. altitude report after "verify altitude", over the field): Approach continues, no pattern join
                {
                    Phase = Phase.Pattern;
                    PatternReset();
                    return Say($"{hdr}, runway {RwSay(rw)}, {Wind(t)}, {Qnh(t)}. Join {Hand(rw)} downwind, {Alt(PatternFt)}, report base.{TrafficNote(traffic, rw)}");
                }
                int busy = PatternCount(traffic);
                bool vectored = Phase == Phase.Inbound && vec != null && !holding;   // already under radar vectoring
                holding = (QueueAhead >= 0 ? !Spaced : busy >= 2) || OtherEmergency;
                if (vectored && !holding && t != null)   // second "inbound"/"initial"/"overhead" (A46): no new first contact, repeat heading and altitude
                {
                    if (rwNew) goto case "runway";   // "inbound ... runway 07" under radar vectoring: confirm runway change and re-plan like "request runway"
                    var ip = OnCenterline(rw, InitialDist);
                    // Visual, already turned in and within 5 NM of initial: direct there right away and to Tower (instrument approach/overhead over the opposite direction: Approach continues to lead, only as number 1 like VecTick)
                    if (!vecFinal && !vecOver && QueueAhead <= 0 && vec!.Count == 0 && Dist(t.X, t.Z, ip.X, ip.Z) < 5 * NM) return Established(t, true);
                    return Say($"{c}, roger, {VecCall(t, now)}", "Approach");
                }
                if (!wantOverhead && (Night || NoBreak.IsMatch(AcType) || !F.PatternOk(rw))) wantStraight = true;   // User request: at night or without break straight-in/instrument approach right away; terrain in the pattern (Khasab) too
                holdFt = HoldFt;
                holdArrived = false; lastHoldInfo = now; lastEat = -1;   // new holding: state EAT once (tick then reports only changes)
                var hold = holding ? $"{EatNote(busy, now, fixS: t != null && HoldFix(t) is var hf0 ? Dist(t.X, t.Z, hf0.X, hf0.Z) / (HoldKt * Kt) : 0)}, " +   // R273: no EAT before arrival at the holding fix
                                     (OtherEmergency ? "emergency in progress." : QueueAhead >= 0 ? $"you are number {QueueAhead + 1}." : $"{busy} aircraft in the pattern.") : "";
                string pos = $"{hdr}, {Position(t)}", wx = $"{Qnh(t)}.{AtisNote(n)}{WxNote()} ";   // WxNote once per approach
                var info = $"{pos}. Runway {RwSay(rw)} in use, {Wind(t)}, {wx}";
                if (!holding && t != null)   // Radar vectoring as in BMS: headings and altitudes up to the centerline
                {
                    Phase = Phase.Inbound;
                    entry = null;
                    var v = Cap(StartVectors(t, Ifr || wantStraight, now));   // no "runway in use, wind": PlanText names the runway (first call >20 s, Batumi 31 vs vectors to 13)
                    return Say($"{pos}, {wx}" + PlanText(rw) + v + TrafficNote(traffic, Runway));
                }
                if (holding)
                {
                    entry = null;
                    Phase = Phase.Inbound;
                    // First heading + altitude to the holding fix, there "hold here" (VecTick/holding); already close: right here
                    var fix = HoldFix(t);
                    double hd = t == null ? 0 : Dist(t.X, t.Z, fix.X, fix.Z);
                    holdFt = HoldFtAt(fix);
                    if (t == null || hd < 2.5 * NM)
                    {
                        if (t != null) { fix = (t.X, t.Z); holdFt = HoldFtAt(fix); }
                        nav = fix; navName = "the hold"; holdArrived = true;
                        return Say(info + $"{(HoldAt(fix) is { } hr ? $"Hold {hr}" : "Hold at present position")}, orbit left hand, {AltTo(t, holdFt)}{HoldSpd(t)}, {hold}");
                    }
                    holdFt = RouteFt(t, traffic, fix, holdFt);
                    return Say(info + $"{Cap(Steer(t, fix, "the hold"))}, {AltTo(t, holdFt)}{HoldSpd(t)}, {(HoldAt(fix) is { } hr2 ? $"hold {hr2}, {MilesTxt(hd)} to go" : $"{MilesTxt(hd)} to the holding point")}, " +
                               $"I will call you there. {Cap(hold)}");
                }
                if (!F.Charted)
                {
                    entry = null;
                    Phase = Phase.Entering; lastAltFt = PatternFt;   // R256: altitude already given
                    handoffAt = now; towerDue = true; towerNag = false;   // R32; reporting obligation: call to Tower expected
                    return Say(info + $"Proceed to initial runway {RwSay(rw)}{SteerTo(t, OnCenterline(rw, InitialDist), "initial")}, " +
                               $"{Alt(PatternFt)}, contact {Contact("Tower")}, report initial for overhead break.{TrafficNote(traffic, rw)}");
                }
                entry = AssignEntry(t, rw, dir); crpAsked = crpWaved = false;
                Phase = Phase.Inbound;
                return Say(info + $"Enter via C R P {entry}{SteerTo(t, Crp[entry], $"C R P {entry}")}, not above {Alt(MaxFt)}, report C R P {entry}.{TrafficNote(traffic, rw)}");
            }

            case "runway":   // "request runway 31" (request checked above)
                if (rwNo != "") return Neg($"{hdr}, {rwNo}");
                if (Phase == Phase.Inbound && vec != null && t != null)
                {
                    var v = Cap(StartVectors(t, Ifr || wantStraight, now));
                    return Say($"{hdr}, runway {RwSay(rw)} approved. {PlanText(rw)}{v}", "Approach");
                }
                return Say($"{hdr}, roger, expect runway {RwSay(rw)}{(holding ? ", continue holding" : "")}.");

            case "visual":   // "cancel IFR", "request VFR / visual approach": via initial/overhead instead of instrument approach
                if (Ifr) return Neg($"{hdr}, negative, field is I F R, expect vectors for {ProcSay(rw)}.");
                wantStraight = wantVisual = visAsk = visSeen = false; wantOverhead = true;
                if (Phase == Phase.Inbound && vec != null && t != null)
                {
                    var v = Cap(StartVectors(t, false, now));
                    return Say($"{hdr}, roger{(Has(n, "cancel") ? ", I F R cancelled" : "")}. {PlanText(rw)}{v}", "Approach");
                }
                goto case "inbound";

            case "altitude":   // "request higher / lower", "request flight level 200", "request 6000 feet"
            {
                if (t == null) return Say($"{c}, say altitude.");
                if (holding) return Neg($"{c}, unable, maintain {Alt(holdFt)} in the hold, I will call you.");
                if (Phase is Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand) return Neg($"{c}, negative, continue as cleared.");
                bool vectors = Phase == Phase.Inbound && vec != null, up = Has(n, "higher", "climb"), pdAsk = Has(n, "discretion");
                double cur = IndFt(t), floor = vectors ? Math.Max(PathMva(t), GateFt) : F.MvaFt(t.X, t.Z);
                double? slot = AltSlot(n);
                if (pdAsk && slot > cur - 300) slot = null;   // "flight level 200, request descent at pilot's discretion": the number is his current altitude
                if (vectors && !pdAsk && (up && slot == null || slot > vecFt + 300))   // R303: higher under radar vectoring (radio wheel "request higher" or with number)
                {
                    // Approved far out, rejected close in (approach needs the descent) – never "descend" on "request higher", no 1000-ft ladder:
                    // without a number a block – already above: his altitude (next full 1000 ft), otherwise up to FL200 (props 10000), at most so high that the 2.5° path
                    // with 15 NM margin hits the gate; the altitude is held (StepDown only when the profile needs it), then descent at own discretion
                    double toGate = Math.Max(0, Path(t).R - gate) / NM, lim = GateFt + (toGate - 15) * 265;
                    double w = slot ?? (cur > vecFt + 300 ? Math.Round(cur / 1000) * 1000 + (Math.Round(cur / 1000) * 1000 < cur - 300 ? 1000 : 0) : Math.Min(Slow != null ? 10000 : 20000, Math.Floor(lim / 1000) * 1000));
                    if (toGate < 25 || w > lim || w <= vecFt) return Neg($"{c}, unable higher, expect lower shortly, maintain {Alt(vecFt)}.");
                    if (!LevelFree(t, traffic, w)) return Neg($"{c}, unable higher, traffic, maintain {Alt(vecFt)}.");
                    vecFt = lastAltFt = w; lastVecSaid = now; pdDescent = true; altFree = false;
                    keepHighR = gate + ((w - GateFt) / 265 + 5) * NM;   // up to here (remaining distance) no descent by profile
                    return Say($"{c}, {(Math.Abs(w - cur) <= 300 ? "maintain" : "climb and maintain")} {Alt(w)}.");
                }
                if (vectors && pdAsk && (slot == null || slot < cur - 300))   // R303: "request descent at pilot's discretion" (FAA JO 7110.65 4-5-7 e, AIM 4-4-10)
                {
                    double w = Math.Max(slot ?? Math.Max(floor, GateFt + 1000), floor);   // without number: up to 1000 ft above the gate altitude (R305: no gate altitude before)
                    if (w < cur - 300 && !LevelFree(t, traffic, w)) return Neg($"{c}, unable, traffic, maintain {Alt(vecFt)}.");
                    vecFt = lastAltFt = w; lastVecSaid = now; pdDescent = true; keepHighR = 0; altFree = false;
                    return Say($"{c}, {(w < cur - 300 ? $"descend at pilot's discretion, maintain {Alt(w)}" : $"maintain {Alt(w)}")}.");
                }
                double want = slot ?? (vectors ? (up ? vecFt + 1000 : Math.Max(vecFt - 1000, floor)) : Math.Round(cur / 1000) * 1000 + (up ? 2000 : -2000));
                var keep = vectors ? $"maintain {Alt(vecFt)}" : "maintain present altitude";
                if (want < floor) return Neg($"{c}, unable, minimum altitude {Alt(floor)}, {keep}.");
                if (!LevelFree(t, traffic, want)) return Neg($"{c}, unable, traffic, {keep}.");
                if (vectors) { vecFt = want; lastVecSaid = now; altFree = false; if (want > cur) stepAfter = now + 120; }   // higher: 2 min, then descent by profile again
                if (Phase == Phase.Departing) { Phase = Phase.Away; nav = null; }   // released from the control zone
                return Say($"{c}, {(Math.Abs(want - cur) <= 300 ? "maintain" : want > cur ? "climb and maintain" : "descend and maintain")} {Alt(want)}.");
            }

            case "heading":   // "request heading 270", "request direct"
            {
                if (t == null) return Say($"{c}, say position.");
                if (Phase == Phase.Inbound && vec != null && !holding)   // Radar vectoring: direct = shortcut if terrain/altitude/sequence fit
                {
                    if (HdgSlot(n) == null && Direct(t) is { } dv) { vec = dv; return Say($"{c}, direct approved, {VecCall(t, now)}", "Approach"); }
                    return Neg($"{c}, unable, {VecCall(t, now)}");
                }
                if (Phase is not (Phase.Away or Phase.Departing)) return Neg($"{c}, unable, {(holding ? "continue holding, I will call you" : "continue as cleared")}.");
                if (Phase == Phase.Departing) Phase = Phase.Away;   // like "altitude": released from the control zone, counts as check-in (A105 would otherwise still call "radar contact" with the old heading)
                if (HdgSlot(n) is { } h) { nav = null; return Say($"{c}, fly heading {Digits(h.ToString("000"))}, approved."); }   // own heading replaces the departure vector
                // R41: the named airfield ("request direct Batumi"), otherwise the route clearance destination, otherwise his own
                var to = All.FirstOrDefault(f => f.Name != F.Name && f.Name.ToLowerInvariant().Split('-', ' ').Any(w => w.Length >= 4 && n.Contains(w))) ?? dest ?? F;
                if (nav != null) { nav = depClr ? (to.X, to.Z) : null; navName = to.Name.Replace('-', ' '); }   // R41: departure vector now leads there (VFR: CRP departure dropped)
                return Say($"{c}, cleared direct {to.Name.Replace('-', ' ')}, {TurnTo(t, Bearing(t.X, t.Z, to.X, to.Z))}, {MilesTxt(Dist(t.X, t.Z, to.X, to.Z))}.");
            }

            case "callup":   // A27: only station + callsign (+ altitude, "information X"): expected after handover -> instructions right away, otherwise report (last stays)
                if ((Phase == Phase.Entering && role == "Tower" || Expected && !ground) && t != null) goto case "inbound";   // N40: diverted, is expected
                return new() { new Msg(Dep(role), $"{c}, {Station}.") };
            case "unable":   // A28: pilot cannot comply with an instruction (terrain, weather, fuel): acknowledge and ask for intentions
                if (addressed == null && Chatter(n)) return new();   // A140: "Two, unable" is flight radio
                if (ground) return Say($"{c}, roger, hold position.");
                return Say($"{c}, roger, {(Phase == Phase.Inbound && vec != null ? $"maintain {Alt(vecFt)}, " : "")}say intentions.");
            case "weather":
                return Say($"{hdr}, {Wind(t)}, {Qnh(t)}, runway {RwSay(rw)} in use.");
            case "bye":
                return Say($"{c}, good day.");
            case "ground_say":
                return Say($"{c}, you are on the ground. Say request.");
            default:   // not understood: ask back, keep the last transmission ("say again" then repeats it), the repeat does not count as a readback
                lastIntent = "";
                if (addressed == null && Chatter(n)) return new();   // A140: flight radio does not go to the station
                if (Emergency) return Say($"{c}, roger.");   // A30: reply to "say souls on board and fuel remaining" or nature of the emergency
                return new() { new Msg(Dep(role), ExtractCallsign(n) != null ? $"{c}, say again." : $"Station calling {Station}, say again your callsign.") };
        }
    }

    /// Readback of the controller's last transmission: the same instruction with the same digits behind it ("hold short", "contact …", "squawk 7171", "QNH 1019"),
    /// the same heading ("Left heading 045, maintain 2000, downwind"), without a report of its own also the same altitude or runway. "initial runway 25" is a report, "QNH?" a question.
    bool ReadsBack(string n, string intent)
    {
        if (last == null || Has(n, @"ready\b") && !Has(n, "when ready")) return false;   // "holding short runway 25, ready" is the report (R111)
        var l = Normalize(last.Text);
        if (RwySlot(n) is { } r && RwySlot(l) is { } lr && r != lr) return false;   // other runway: no readback (reply corrects)
        static string? After(string s, string p) => Regex.Match(s, $@"\b{p}\b((?: \d+{(p == "altimeter" ? "| point(?= \\d)" : "")})*)") is { Success: true } m ? Regex.Replace(m.Groups[1].Value, @"\D", "") : null;   // "altimeter 29.92" = "2 9 9 2"
        return new[] { Phase == Phase.TaxiOut ? "hold short" : "hold(?:ing)? short", "hold(?:ing)? position", "giv(?:e|ing) way", "line up", "contact", "vacate", "when vacated", "when ready", "expect", "squawk(?:ing)?", "qnh", "altimeter", "until established", "frequency change", "own navigation", "maintain vfr" }   // TaxiOut: "holding short runway 25" reports at the holding point, Ground's "Hold short, contact Tower" is read back only as "hold short" (R111)
                   .Any(p => After(n, p) is { } v && (v == After(l, p) || p is "qnh" or "altimeter" && v == After(l, p == "qnh" ? "altimeter" : "qnh")))   // "altimeter 1013" reads back "QNH 1013"
            || HdgSlot(n) is { } h && h == HdgSlot(l)   // the pilot states a heading only when reading back ("request heading" is a request)
            || intent == "unknown" && (AltSlot(n) is { } a && a == AltSlot(l) || RwySlot(n) is { } rn && rn == RwySlot(l)
                                       || Regex.Replace(NoCs(n), @"\D", "") is { Length: >= 3 } d && Regex.Replace(l, @"\D", "").Contains(d));   // values only: "4321, 1013, Enfield 1-1", "Tower 134.0, Enfield 1-1"
    }

    /// Forum 0.9.7: acknowledgement without readback of a controller transmission: only the own callsign (AIM 4-2-3) and/or "approved", "starting up", "rolling", "taxiing", "lining up" …
    bool AckOnly(string n) => last != null &&
        Regex.Replace(NoCs(n), @"\b(?:roger|copy|copied|wilco|affirm(?:ative)?|approved|understood|starting(?: up| engines?)?|rolling|taxiing|lining up|switching(?: over)?)\b", " ").Trim() == "";
    /// Transmission without the own callsign (also misheard)
    string NoCs(string n) => Regex.Replace(n, @"\b([a-z]{3,}) \d(?: ?\d)?\b", m => !NotCallsign.Contains(m.Groups[1].Value) && (SameCallsign(m.Value, callsign) || Digits(m.Value) == Digits(callsign)) ? " " : m.Value);

    internal string IntentOf(string text, Telemetry? t) => Intent(Normalize(text), t == null || OnGround(t));   // Debug log (Program)
    /// Destination of the route clearance after "clearance"/"ifr", not the station called; most name words (Krasnodar Center/Pashkovsky)
    Airfield? ClrDest(string n) => Named(Regex.Match(n, @"\b(?:clearance|ifr)\b.*").Value);

    string Intent(string n, bool ground)
    {
        if (Has(n, "cancel emergency", "cancel mayday", "cancel pan", "emergency cancel")) return "cancelemergency";
        // R101: emergency call only with MAYDAY/PAN PAN or own "emergency"; readbacks ("emergency traffic/in progress", "hold short, …") not
        if (Has(n, "mayday", "pan pan") || Regex.IsMatch(n, @"\bemergency\b(?! traffic| in progress| services)") && !Has(n, "hold short", "holding short", "continue approach", "expedite vacating")) return "emergency";
        if (Has(n, "minimum fuel", "min fuel", "low fuel", "bingo")) return "minfuel";   // before "inbound"/rest, otherwise first contact or on the ground "say again"
        // R100 before R251: "aborting, bird strike" is an aborted takeoff (takeoff clearance expires)
        if (ground && (Phase == Phase.ClearedTakeoff || Phase == Phase.HoldShort && lineUp) && Has(n, "abort", "reject", "stopping", @"cancel(?:ling)? (?:my )?take ?off")) return "rto";
        if (Has(n, "hung", "hot gun")) return "hung";   // R251
        if (Has(n, "bird ?strike")) return "birdstrike";
        if (Has(n, "hot brakes?")) return "hotbrakes";
        if (Has(n, "go around", "going around") || Has(n, "going missed", "missed approach") && !Has(n, "request")) return "goaround";
        if (Has(n, "unable") && !Has(n, "request")) return "unable";   // A28: "unable to copy" is caught earlier by the say-again branch, "unable runway 25, request 07" stays the request
        if (ground)
        {
            if (Has(n, "clearance", "ifr to") && !Has(n, "takeoff", "take off", "landing")) return "clearance";
            if (Has(n, "progressive") && (Phase == Phase.TaxiOut || Phase == Phase.TaxiIn && taxiInTold)) return "progressive";   // N26: otherwise part of the taxi request (taxi/taxiin)
            // A7: parking spot/apron before departure only as destination ("taxi to parking"), "parking spot 12, information Charlie, request taxi" states the location
            if (Has(n, "vacated", "clear of") && !Has(n, "holding point", "departure", "take off", "takeoff") || Regex.IsMatch(n, (Phase is Phase.ClearedLand or Phase.TaxiIn ? @"\b" : @"\bto (?:the )?") + "(?:parking|ramp|apron)") ||   // R267: "runway vacated, request taxi to holding point" (after aborted takeoff) = back to the start
                (Has(n, "taxi") && !Has(n, "departure", "runway", "holding", "take off", "takeoff") &&
                 (Phase == Phase.ClearedLand || Phase == Phase.TaxiIn && !taxiInTold))) return "taxiin";   // after taxi clearance to parking: "request taxi" = start again
            if (Has(n, "taxi")) return "taxi";   // A6: before "start" ("engines started, request taxi"); "request startup and taxi" is answered by case "taxi" too
            if (StartReq(n) && Phase is Phase.Parked or Phase.StartupApproved) return "startup";   // otherwise e.g. the takeoff clearance would be lost
            if (Phase == Phase.TaxiOut && lastTel is { } ct && CrossNext(ct) is { } cx && NearRwy(ct, cx) && Has(n, "cross", "ready", "departure", "take off", "takeoff", "holding short", "holding point")) return "cross";   // Forum 0.9.7: at the parallel runway holding point
            if (Has(n, "ready", "departure", "take off", "takeoff", "holding short", "holding point")) return "ready";
            if (Has(n, "inbound", "landing", "final", "initial")) return "ground_say";
        }
        else
        {
            if (altAsk && Named(n) != null) return "alternate";   // N40: reply to "say alternate destination"
            if (zoneReport && Has(n, "leaving (?:the )?(?:control )?zone", "clear of (?:the )?(?:control )?zone")) return "leavezone";   // R7
            if (Has(n, "clearance", "ifr to") && !Has(n, "approach clearance", "landing") && ClrDest(n) != null) return "clearance";   // R113: pop-up IFR to another airfield, no landing here
            if (Has(n, "cancel approach", "cancel the approach", "cancel landing", "cancel inbound", "abort approach", "abort landing",
                    "leave the pattern", "leaving the pattern", "cancel my approach")) return "cancelapproach";
            // A54: flight following and transit before "vfr" ("request VFR flight following" is not a visual approach)
            if (Has(n, "flight following", "radar service", "radar advisor", "traffic advisor")) return Has(n, "cancel", "terminat") && !Has(n, "cancel ifr", "cancelling ifr") ? "ffoff" : "ff";
            if (transitFt != null && Has(n, "clear of the", "outside the", @"leaving the (?:control )?zone") && !Has(n, "report")) return "transitoff";   // before transit ("transit complete, clear of the zone"), readback "report clear of the zone" not
            if (Phase is Phase.Away or Phase.Departing && Has(n, "transit", "crossing", "cross the", "overfl", "pass through") && !Has(n, "inbound", "landing")) return "transit";   // N3 ("transition" too; "crossing the coast, inbound for landing" is a first call)
            if (Phase is Phase.Inbound or Phase.Entering or Phase.Initial or Phase.Pattern or Phase.ClearedLand && Has(n, "zone transit", "request transit") && !Has(n, "inbound", "landing")) return "transitno";   // R389: in the landing flow no transit, not a new check-in
            if (Has(n, "visual approach") && !Ifr && !Has(n, "cancel ifr", "cancelling ifr", "vfr")) return "straightin";   // P3-AP7: straight-in approach without procedure; IFR weather: "visual" rejects
            if (Has(n, "cancel ifr", "cancelling ifr", "vfr", "visual approach", "visual recovery")) return "visual";
            if (Has(n, FieldSight) && !Has(n, "report", "request", "inbound", "landing", "final", "initial", "overhead")) return "fieldinsight";   // R115: reply to "report field in sight" (airfield/runway in sight); after cancel/flight following/visual ("field in sight, cancel IFR" stays a sign-off)
            if (Has(n, "leaving frequency", "leaving your frequency")) return "ffoff";   // itself requires "report leaving frequency"
            if (Has(n, "ready", "takeoff", "take off")) return "airborne_ready";
            if (Has(n, @"(?:request|requesting) (?:left |right )?closed\b", @"closed traffic\b") && !Has(n, "approved")) return "closed";   // R202: closed pull-up ("request closed")
            if (Has(n, "high key")) return "highkey";   // R204: SFO reporting points
            if (Has(n, "low key")) return "lowkey";
            if (Has(n, "sfo", "s f o", "simulated flame", "e l p")) return "sfo";
            // Requests with numbers (V13): altitude, heading/direct – only with "request", otherwise it is a report ("climbing 6000")
            bool req = Has(n, "request", "requesting");
            if (req && (Has(n, "higher", "lower", "climb", "descen") || AltSlot(n) != null && !Has(n, "landing", "inbound", "ils", "runway", "straight in"))) return "altitude";
            if (req && (HdgSlot(n) != null || Has(n, "direct"))) return "heading";
            if (Has(n, "airborne", "passing", "climbing", "departed", "out of") && Phase is Phase.ClearedTakeoff or Phase.Departing or Phase.Away) return "airborne";   // R40: also below 100 m
            if (Has(n, "start up", "startup", "taxi", "parking")) return "air_ground";
            if (Has(n, "separate", "split", "radar trail", "trail recovery")) return "separate";
            if (Has(n, "ils", "straight in", "instrument", "tacan approach", "vor approach", "par approach", "p a r", "radar approach", "ifr", "practice approach")) return "straightin";
            if (Has(n, "final")) return "final";
            if (Has(n, "initial")) return "initial";
            if (Has(n, "overhead")) return "overhead";
            if (Has(n, "downwind", "base", "break")) return "pattern";
            if (Has(n, "crp", "c r p", "reporting point", "entry point") ||
                (Phase == Phase.Inbound && Direction(n) != null && !Has(n, "inbound", "landing"))) return "crp";
            if (req && RwySlot(n) != null && !Has(n, "inbound", "landing", "recovery", "full stop", "rejoin")) return "runway";   // before "approach" (call name)
            if (Has(n, "in sight", "looking", "negative contact", "no joy") && !Has(n, "field", "runway", "airport")) return "traffic";   // Reply to traffic advisory
            if (Has(n, "inbound", "landing", "recovery", "full stop", "rejoin")) return "inbound";
            // First call to Departure after takeoff ("Kutaisi Approach, Enfield 1-1", "…, with you"): check-in, not a landing announcement
            if ((Phase is Phase.ClearedTakeoff or Phase.Departing || Phase == Phase.Away && handedOff) && (Has(n, "with you") || Addressed(n) == "Approach" && Rest(n) == "unknown")) return "airborne";
            if (Has(n, "airborne", "climbing") && Phase is Phase.Pattern or Phase.Entering or Phase.Initial or Phase.Inbound) return Rest(n);   // R112: no departure, so no first contact ("Approach: airborne, climbing")
            if (Has(n, "with you") || Has(n, "approach") && !Following && !handedOff) return "inbound";
            if (Has(n, "touch and go", "touch go", "low approach", "low pass", "fly by", "flyby", "closed pattern", "pattern work", "the option")) return "option";
        }
        return CallOnly(n) ? "callup" : Rest(n);   // "Kutaisi Tower, Enfield 1-1, information Charlie" is a first call, not a weather question (A27)
        // Question/farewell, also before check-in with Departure ("Kutaisi Approach, Enfield 1-1, good day" is not a check-in)
        static string Rest(string n) => Has(n, "qnh", "weather", "wind", "altimeter", "information", "atis",
                "runway in use", "active runway", "which runway", "confirm runway", "say runway") ? "weather" : Has(n, "good day", "thank", "bye") ? "bye" : "unknown";
    }

    /// Startup requested ("request startup", "start up", "request engine start", "ready to start"), not "engines started".
    internal static bool StartReq(string n) => Has(n, "start up", "startup", @"(?:request|requesting|ready (?:to|for)) (?:engines? )?start\b");

    // ======================================================================= Controllers
    /// Who is responsible: Ground (startup, taxi), Tower (takeoff, pattern, landing), Approach (approach up to CRP/initial).
    string? Responsible(string intent, Telemetry? t) => intent switch
    {
        "clearance" when t != null && !OnGround(t) => "Approach",   // R113: pop-up IFR in the air
        "startup" or "taxi" or "taxiin" or "progressive" or "ground_say" or "clearance" => "Ground",
        // Initial/overhead not yet handed over and far away: Approach leads there first, Tower only after handover
        "initial" or "overhead" when Phase is Phase.Inbound or Phase.Away && t != null && !OnGround(t) &&
                                     Dist(t.X, t.Z, OnCenterline(Runway, InitialDist).X, OnCenterline(Runway, InitialDist).Z) > 3 * NM => "Approach",
        "goaround" => Phase == Phase.Inbound && vec != null ? "Approach" : "Tower",   // R102: already handed over to Approach after the missed approach
        "straightin" when Phase == Phase.Entering && (wantStraight || navName is "six mile final" or "circle") => "Tower",   // R102: straight-in approach already handed over to Tower
        "ready" or "airborne_ready" or "final" or "initial" or "overhead" or "pattern" or "option" or "closed" or "sfo" or "highkey" or "lowkey" => "Tower",
        "fieldinsight" => visAsk ? "Approach" : null,   // R115: only the requested visual report belongs to Approach, otherwise the station called acknowledges (on final the Tower)
        "straightin" or "crp" or "airborne" or "ff" or "ffoff" or "leavezone" => "Approach",
        "transit" => t != null && Dist(t.X, t.Z, CX, CZ) < 5 * NM ? "Tower" : "Approach",   // N3: without a station called (OnTranscript otherwise takes the one called)
        "transitoff" => transitBy,
        "inbound" when Phase == Phase.Inbound && vec != null && !holding => "Approach",   // under radar vectoring, also close to the field
        "inbound" => Phase == Phase.Entering || t == null && Phase is Phase.Initial or Phase.Pattern || t != null && (Dist(t.X, t.Z, CX, CZ) < 4 * NM ||
                     Phase == Phase.Initial && Dist(t.X, t.Z, OnCenterline(Runway, InitialDist).X, OnCenterline(Runway, InitialDist).Z) < 5 * NM ||
                     Phase == Phase.Pattern && Dist(t.X, t.Z, CX, CZ) < 5 * NM) ? "Tower" : "Approach",   // Entering: already handed over to Tower; R102: initial/pattern (call crosses with the break clearance) stays with Tower, but only within the airfield area (initial up to 5 NM from the initial point like the handover, circuit up to 5 NM like tick), otherwise new approach via Approach
        _ => null,
    };

    /// Controller for automatic reports by flight phase.
    string Owner() => Phase switch
    {
        Phase.Parked or Phase.StartupApproved or Phase.TaxiOut or Phase.TaxiIn => "Ground",
        Phase.Inbound or Phase.Away => "Approach",
        Phase.Departing when handedOff => "Approach",   // R7: after "contact Departure" only Departure speaks
        _ => "Tower",
    };

    /// Controller called at the start of the transmission (first three words, long airfield names correspondingly more): "Kutaisi Approach, ..." / "Minvody Approach, ..." (call name) / "Ground: ..." (menu) / "Tower, ...".
    /// Station further back, "contact Tower" or "radar contact/service" is a readback, not a call.
    string? Addressed(string n)
    {
        var w = $"{F.Name}-{F.Callsign}".ToLowerInvariant().Split('-', ' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var names = string.Join("|", w.Select(Regex.Escape));
        n = string.Join(" ", n.Split(' ').Take(w.Length + 2));
        var nc = $@"(?<!\bcontact (?:(?:{names}) )?)";
        var m = Regex.Match(n, $@"{nc}(?:^|\b(?:{names}) )(ground|tower|approach|departure|radar)\b(?! contact| service| \d)");   // "Tower 134.0, Enfield 1-1" reads back the frequency
        var r = m.Success ? m.Groups[1].Value
              : Regex.IsMatch(n, $@"{nc}\btower\b(?! \d)") ? "tower" : Regex.IsMatch(n, $@"{nc}(?<!the )\bground\b(?! \d)") ? "ground" : null;
        return r switch { "ground" => "Ground", "tower" => "Tower", null => null, _ => "Approach" };
    }

    /// A27: first call without a request: station and callsign, plus at most altitude and "information X" ("Kutaisi Tower, Enfield 1-1, 2000 feet, information Charlie").
    bool CallOnly(string n)
    {
        if (Addressed(n) == null || ExtractCallsign(n) is not { } cs) return false;
        var names = string.Join("|", $"{F.Name}-{F.Callsign}".ToLowerInvariant().Split('-', ' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
        return Regex.Replace(n, $@"\b{cs.ToLowerInvariant().Replace("-", " ?")}\b|\binformation [a-z]+\b|\b(?:{names}|ground|tower|approach|departure|radar|switching|to|this|is|with|you|over|at|passing|level|angels|feet|ft|thousand|hundred|\d+)\b", " ").Trim() == "";
    }

    /// A140: radio not going to the station (flight radio): foreign callsign or flight number at the start of the transmission ("Enfield 1-2, check fuel", "Two, go trail"), the own callsign does not occur.
    /// Same number with a different word ("Outfit 1-1" instead of "Enfield 1-1") counts as the own, just misheard (forum log 0.9.4).
    bool Chatter(string n) =>
        (Regex.Match(n, @"^([a-z]{3,}) \d(?: ?\d)?\b") is { Success: true } m && !NotCallsign.Contains(m.Groups[1].Value) || Regex.IsMatch(n, @"^[234]\b(?! (?:thousand|hundred|miles?|minutes?|feet|ft|\d))")) &&
        !Regex.Matches(n, @"\b[a-z]{3,} \d(?: ?\d)?\b").Any(x => SameCallsign(x.Value, callsign) || Digits(x.Value) == Digits(callsign));

    /// K2: every handover with frequency, "Kutaisi Tower one three four decimal zero"; R104: on the same frequency without (no change, ICAO Doc 4444 12.3).
    string Contact(string r) { r = Dep(r); return SameFreq(r) ? F.StationOf(r) : $"{F.StationOf(r)} {FreqSay(FreqOf(F, r))}"; }
    /// Reporting obligation: reminder of the missing check-in, "contact Kutaisi Approach now, two six six decimal five"; after the departure handover with own Departure frequency Departure.
    string ContactNow(string r) { r = Dep(r); return SameFreq(r) ? $"contact {F.StationOf(r)} now" : $"contact {F.StationOf(r)} now, {FreqSay(FreqOf(F, r))}"; }

    /// R104: controller r on the speaker's frequency (map frequency, K2: Ground/Tower/Approach shared; own per controller); config frequencies (Uhf 0) stay separate.
    bool SameFreq(string r) => (F.HasFreq || F.Own.Count > 0) && Math.Abs(FreqOf(F, Dep(r)) - FreqOf(F, Dep(role))) < 0.005;

    /// Other airfield named in the transmission (most name words), null = none.
    Airfield? Named(string n) => ByName(All.Where(f => f.Name != F.Name), w => Regex.IsMatch(n, $@"\b{w}\b"));
    /// N1: airfield of the opposing side (neutral and without player's side: never).
    bool Hostile(Airfield? f) => f is { Side: > 0 } && Side > 0 && f.Side != Side;

    /// N40: this airfield is captured: diversion to the nearest own one (null = none), last transmission on the old frequency, then silently signed off.
    /// "Enfield 1-1, Kutaisi Approach, Kutaisi is closed, divert Senaki, fly heading 270, 25 miles, climb and maintain 3000 feet, contact Senaki Approach 261.0."
    public List<Msg> Closed(Telemetry t, Airfield? to)
    {
        var head = $"{Cs()}, {F.StationOf("Approach")}, {F.Name.Replace('-', ' ')} is closed, ";
        var s = to == null ? head + "resume own navigation."
            : head + $"divert {to.Name.Replace('-', ' ')}, {Steer(t, (to.X, to.Z), to.Name.Replace('-', ' '))}, {MilesTxt(Dist(t.X, t.Z, to.X, to.Z))}, " +
              $"{AltTo(t, Math.Ceiling(Math.Max(F.MvaLeg(t.X, t.Z, to.X, to.Z), to.PatternFt + 1000) / 100) * 100)}, contact {to.StationOf("Approach")} {FreqSay(FreqOf(to, "Approach"))}.";
        CancelApproach(t);
        Released = true;
        return Say(s, "Approach");
    }

    /// R43: end of the IFR route to another airfield = handover to its Approach instead of "radar service terminated"; null = other sign-off (mission AWACS, CRP, VFR)
    /// R218: also without a departure vector (own heading after "request heading"): handover to the route clearance destination, "radar service terminated" only VFR, after "cancel IFR" or to AWACS.
    string? IfrHandoff() => depClr && AwacsContact == null && ((nav != null ? All.FirstOrDefault(f => f.Name != F.Name && f.Name.Replace('-', ' ') == navName) : null)   // Destination = airfield flown to (also after "request direct")
                                                                ?? (dest != null && dest.Name != F.Name ? dest : null)) is { } to   // otherwise the route clearance destination
        ? $"contact {to.StationOf("Approach")} {FreqSay(FreqOf(to, "Approach"))}." : null;

    /// K2: frequency of controller r at airfield a in the transmission: the one heard, if it is his, otherwise the one in the band of the heard one (UHF aircraft: UHF, from 200 MHz), otherwise the last (VHF = map).
    /// Own frequencies per controller (AirfieldFrequencies) like the map. Without Radio.lua the config per controller.
    double FreqOf(Airfield a, string r) => a.FreqsOf(r) is not { } fs ? Freqs[r == "Departure" ? "Approach" : r]
        : fs.FirstOrDefault(x => Math.Abs(x - HeardOn) < 0.005, fs.FirstOrDefault(x => HeardOn > 0 && x >= 200 == HeardOn >= 200, fs[^1]));

    /// 265.0 -> "two six five decimal zero"
    internal static string FreqSay(double f) =>
        string.Join(" decimal ", f.ToString("0.0##", CultureInfo.InvariantCulture).Split('.').Select(Digits));

    /// Next sensible radio call (selection wheel: key 0 / Enter), null = none. R39 · R48: only what cancels no clearance here and brings no rejection.
    public (string Role, string Text)? Suggest(Telemetry? t) => Phase switch
    {
        Phase.Parked => Ifr && !depClr ? ("Ground", "request IFR clearance") : ("Ground", "request startup"),
        Phase.StartupApproved => ("Ground", "request taxi"),
        Phase.TaxiOut when t != null && CrossNext(t) is { } cx && NearRwy(t, cx) => cx == crossOk || cx == crossWait ? null : ("Ground", $"holding short runway {RwSay(cx)}, request crossing"),   // Forum 0.9.7: parallel runway on the way
        Phase.TaxiOut or Phase.HoldShort => AtHold(t) ? ("Tower", !stayPattern ? "ready for departure" : Ifr ? "ready for departure, practice approach" : "ready for departure, closed pattern") : null,   // R253: IMC practice approach   // while taxiing: "not at the holding point"
        Phase.Inbound when missAt >= 0 => ("Approach", missDep ? "airborne, climbing" : "missed approach"),   // R44: check-in after the missed approach
        Phase.Inbound when visAsk => ("Approach", "field in sight"),   // R115
        Phase.Inbound when spdTold == 2 && vec != null => ("Approach", "report airspeed"),   // R129: reply to "say airspeed" (Program inserts the speed)
        Phase.Away when zoneReport => ("Approach", "leaving the control zone"),   // R7
        Phase.Inbound => !holding && vec == null && Crp.TryGetValue(entry ?? "", out var cp) && (t == null || Dist(t.X, t.Z, cp.X, cp.Z) <= 2.5 * NM)
            ? ("Approach", "crp") : ("Approach", "say again"),   // Radar vectoring/holding: listen only; CRP only nearby
        Phase.Entering when towerDue && navName == "six mile final" && t != null && OnLongFinal(t, Runway)
            => ("Tower", $"{Miles(Approach(t.X, t.Z, Runway).along)} mile final, gear down, {option ?? "full stop"}"),   // R314: first call on the long final with distance ("9 mile final, gear down, full stop")
        Phase.Entering when towerDue || t != null && (navName == "six mile final" ? !OnFinalLoose(t, Runway) : nav is { } np && Dist(t.X, t.Z, np.X, np.Z) > 2.5 * NM)
            => ("Tower", "inbound"),   // handed over or still far away: first call to Tower instead of "negative, you are 8 miles from initial"
        Phase.Entering => ("Tower", navName switch { "overhead" => "overhead", "six mile final" => "four mile final, gear down", "high key" => "high key", "low key" => "low key, gear down", "circle" => "inbound", _ => "initial" }),   // R212: Circling: first call
        Phase.Initial => t == null || !OnFinalLoose(t, Runway) && Dist(t.X, t.Z, CX, CZ) < 5 * NM ? ("Tower", $"base, gear down, {option ?? "full stop"}") : null,   // on initial nothing (wait for break, "final" would give the landing clearance), then "base"
        Phase.Pattern => t == null || OnFinalLoose(t, Runway) ? ("Tower", option is "touch and go" or "low approach" or "low pass" or "the option" ? $"final, {option}" : "final, gear down, full stop")
                       : option == "low pass" ? null : ("Tower", $"base, gear down, {option ?? "full stop"}"),   // A47: low pass without pattern; R200: landing report base with gear
        Phase.Away when intr is 1 or 2 && transitFt == null && t != null && !OnGround(t) => ("Tower", "request zone transit"),   // N2: called in the zone without contact, legal way (N3)
        Phase.Departing => ("Approach", "airborne, climbing"),
        Phase.TaxiIn => taxiInTold ? (specArea && (t == null || Gs(t) < Kt) ? ("Ground", "request taxi to parking") : null) : ("Ground", "runway vacated, request taxi to parking"),   // R265: stopped in the de-arm/hot-brake area   // "request taxi" would be the taxi clearance to the holding point
        Phase.ClearedTakeoff or Phase.ClearedLand => null,   // "request startup"/"inbound" would cancel the clearance
        _ => t != null && !OnGround(t) ? ("Approach", Following ? "cancel flight following" : "inbound for landing") : ("Ground", "request startup"),   // end FF: no longer in the wheel (R17)
    };

    /// At the runway holding point of the active runway (R349: 250 m around the holding point or the threshold) or already on a runway: "ready for departure" fits.
    /// Forum 0.9.7: not while a parallel runway still lies in between (holding point 03L with assigned 03R, close together like Nellis).
    bool AtHold(Telemetry? t) => t == null || CrossNext(t) == null && (OnRunwayPos(t.X, t.Z) || Dist(t.X, t.Z, Thr(Runway).X, Thr(Runway).Z) <= 250 ||
                                                                       HoldPt(Runway, t) is var h && Dist(t.X, t.Z, h.X, h.Z) <= 250);

    /// R339: instrument conditions without IFR clearance: no VFR taxi/departure (14 CFR 91.155, SERA.5005(b)); practice approaches fly the radar pattern (R253)
    bool VfrBlocked => Ifr && !depClr && !stayPattern;
    static string VfrUnable(string hdr) => $"{hdr}, field is I F R, unable VFR departure, advise ready to copy IFR clearance.";

    /// Forum 0.9.7: parallel runway between aircraft and assigned runway, not yet fully crossed (the nearest first), name in takeoff direction ("03L" before 03R), otherwise null.
    /// DCS-ATC does not know taxiways: only if the holding point is next to it (otherwise it goes around its end).
    string? CrossNext(Telemetry t)
    {
        var e = F.End(Runway);
        double Lat(double x, double z) => (x - e.CX) * e.Dz - (z - e.CZ) * e.Dx;
        double lt = Lat(t.X, t.Z);
        var hp = HoldPt(Runway, t);
        return F.Ends.Where(o => HdgDiff(o.Hdg, e.Hdg) < 10 && Lat(o.CX, o.CZ) is var lo && Math.Abs(lo) > 50 && lo * lt > 0 && Math.Abs(lt) > Math.Abs(lo) - 30
                                 && Math.Abs((hp.X - o.CX) * o.Dx + (hp.Z - o.CZ) * o.Dz) < o.Len / 2 + 300)   // Holding point 150 m before the threshold
                     .OrderByDescending(o => Math.Abs(Lat(o.CX, o.CZ))).FirstOrDefault()?.Name;
    }

    /// Forum 0.9.7 (FAA JO 7110.65 3-7-2): crossing parallel runway x only clear if nobody is taxiing/standing on it and nobody approaches it within 2 NM (both directions).
    Traffic? CrossBlock(IReadOnlyList<Traffic> traffic, string x) =>
        traffic.FirstOrDefault(a => Blocks(F, x, a.X, a.Z, a.AltMsl - FieldElev, a.Hdg, a.Speed)) ?? FinalTraffic(traffic, x, 2 * NM).Concat(FinalTraffic(traffic, Airfield.Opposite(x), 2 * NM)).FirstOrDefault();

    /// Clearance to cross, then holding point of the assigned runway; runway occupied: hold, the clearance comes by itself (tick).
    string Cross(string x, IReadOnlyList<Traffic> traffic)
    {
        var c = Cs();
        if (CrossBlock(traffic, x) is { } b) { (crossWait, crossRole) = (x, role); return $"{c}, hold short runway {RwSay(x)}, traffic {Describe(b, x)}."; }
        (crossOk, crossWait) = (x, null);
        return $"{c}, cross runway {RwSay(x)}, hold short runway {RwSay(Runway)}" +
               (role == "Tower" || SameFreq("Tower") ? ", report ready for departure." : $", contact {Contact("Tower")} when ready for departure.");
    }
    string? crossOk, crossWait;   // may be crossed / waiting to cross
    string crossRole = "Ground";   // whoever said "hold short" releases the crossing
    /// At the holding point of runway x or on it: up to 250 m lateral of the centerline, longitudinally up to 300 m beyond the ends
    bool NearRwy(Telemetry t, string x) => F.End(x) is var o && Math.Abs((t.X - o.CX) * o.Dz - (t.Z - o.CZ) * o.Dx) < 250 && Math.Abs((t.X - o.CX) * o.Dx + (t.Z - o.CZ) * o.Dz) < o.Len / 2 + 300;
}
