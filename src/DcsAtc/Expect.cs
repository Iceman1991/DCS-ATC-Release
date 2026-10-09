using System.Globalization;
using System.Text.RegularExpressions;

namespace DcsAtc;

/// Radio wheel Enter = what is currently expected: every spoken controller call to the player (SpeakLoop -> Heard) with a question or required report
/// ("say airspeed", "report initial", "call the ball", "say again." …) sets Pilot.Expect, his next own radio call (Request) clears it.
/// Suggestion: first the answer to it, otherwise the procedure (Tower/Carrier.Suggest). Controllers can also set an expectation directly: Expect(p, "Tower", "report initial").
static partial class Program
{
    const double AskSec = 90, ReportSec = 300;   // Question counts 90 s; "report …" he reports only later (position reached): 5 min

    internal static void Expect(Pilot p, string role, string ask, double sec = AskSec) => p.Expect = (role, ask, Now() + sec);

    /// Question/report in the controller call (the last one counts); "contact … Tower" before it: to that controller. "say again," (= I repeat) is not a question.
    static readonly Regex AskRe = new(@"\b(?:say (?:airspeed|altitude|heading|position|intentions|state|needles|souls on board and fuel remaining)\b|update state\b|verify (?:altitude|heading)\b|say again(?= your callsign\.|\.)|call the ball\b|cleared pre-contact\b|report [^.]+|going around, confirm(?=\?)|(?<=\b[Cc]ontact (?:[\w'-]+ )*?(?:Tower|Ground|Approach|Departure) )now\b)", RegexOptions.IgnoreCase);
    internal static (string Role, string Ask)? Ask(string text, string role)
    {
        var ms = AskRe.Matches(text);
        if ((ms.FirstOrDefault(x => x.Value.EndsWith("confirm")) ?? ms.LastOrDefault()) is not { } m) return null;   // Reporting obligation #7: first confirm "going around", "report base" afterwards comes from the procedure
        if (Regex.Matches(text[..m.Index], @"\b[Cc]ontact (?:[\w'-]+ )*?(Tower|Ground|Approach|Departure)\b").LastOrDefault() is { } c) role = c.Groups[1].Value;
        var a = m.Value.ToLowerInvariant();
        // Departure (own frequency, AirfieldFrequencies) like the procedure as "Approach": radio wheel (FreqOf) and Request route it via Tower.Dep to the Departure frequency, answer from Departure
        return (role == "Departure" ? "Approach" : role, a.StartsWith("verify ") ? "say " + a[7..] : a == "cleared pre-contact" ? "report pre contact" : a == "update state" ? "say state" : a);   // R280: Marshal "update state" -> report fuel
    }

    /// Controller call spoken: expectation at the addressed player (callsign first, own group).
    static void Heard(Tx tx)
    {
        if (tx.Pilot || tx.Role is "Info" or "Crew" or "Flight" or "ATIS") return;
        var a = Ask(tx.Text, tx.Role);
        bool tr = TrafficRe.IsMatch(tx.Text);   // R307: traffic advisory -> radio wheel "Traffic"
        if (a == null && !tr) return;
        lock (TowerLock)
            foreach (var p in Pilots.Values.Append(Solo).Where(p => p.Callsign != "" && (tx.Gid <= 0 || p.Gid == tx.Gid)
                                                                 && Regex.IsMatch(tx.Text, $"^{Regex.Escape(Tower.SpokenCallsign(p.Callsign))}(?: flight)?,", RegexOptions.IgnoreCase)))
            {
                if (a is { } x) Expect(p, x.Role, x.Ask, x.Ask.StartsWith("report ") ? ReportSec : AskSec);
                if (tr) p.TrafficAt = Now();
            }
    }
    static readonly Regex TrafficRe = new(@"(?<!closed |pattern |emergency )\btraffic\b(?! no longer)", RegexOptions.IgnoreCase);

    /// R307: radio wheel shows only what fits the situation: procedure phase (own or flight lead's procedure), ground/air, open question (Expect),
    /// traffic advisory (2 min), Range/AWACS checked in, tanker stage, carrier stage. Without player/flight state and for everything else (Allgemein, Notfall, Say again): visible.
    internal static Func<string, bool> WheelFits(Pilot? h)
    {
        if (h?.Tel is not { } t) return _ => true;
        var a = (h.Lead ?? h).Active;
        var ph = a?.Phase;
        bool gnd = Tower.OnGround(t), air = !gnd, before = ph is null or Phase.Parked or Phase.StartupApproved or Phase.TaxiOut or Phase.HoldShort;   // before takeoff
        var flow = a?.Suggest((h.Lead ?? h).Tel)?.Text ?? "";
        var ask = new[] { h, h.Lead }.Select(x => x?.Expect).FirstOrDefault(x => x is { } v && v.Until > Now())?.Ask ?? "";
        bool airb = air && (a == null ? h.Airborne is { } at && (Clock - at).TotalMinutes < 3 : ph is Phase.ClearedTakeoff or Phase.Departing || flow.StartsWith("airborne"));   // shortly after takeoff, not yet checked in
        int tk = h.Ops.Tank;   // 0 nothing, 1 approach, 2 observation, 3 pre-contact, 4 contact
        return x => x switch
        {
            "Ground: request startup" => gnd && ph is null or Phase.Parked,
            "Ground: request taxi" => gnd && ph is null or Phase.Parked or Phase.StartupApproved or Phase.TaxiOut,
            "Ground: request IFR clearance" or "Tower: ready for departure" or "Tower: ready for departure, closed pattern" or "Tower: ready for departure, practice approach" => gnd && before,
            "Ground: runway vacated, request taxi to parking" => gnd && !before,   // after landing (or aborted takeoff back)
            "Tower: high key" or "Tower: low key, gear down" => air && (a?.Sfo == true || ask.Contains(" key") || flow.Contains(" key")),   // after "request S F O"
            "Approach: airborne, climbing" => airb,
            "Approach: cancel approach" => air && !airb,
            "Approach: inbound for landing" or "Approach: inbound for pattern work, touch and go" or "Approach: request straight in" or "Approach: request flight following" or "Approach: request higher" => air,
            "Approach: request zone transit" => air && ph is null or Phase.Away or Phase.Departing,   // R389: not in the landing flow, not on the ground
            "Approach: traffic in sight" or "Approach: negative contact" => Now() - h.TrafficAt < 120,
            "Approach: C R P" => ask.Contains("c r p") || flow == "crp",   // Reporting obligation: only on call or at the CRP
            "Approach: leaving the control zone" => flow == "leaving the control zone" || ask.Contains("leaving the control zone") || ask.Contains("clear of the zone"),   // R7 / N3: only when the report is due
            "Approach: report airspeed" => ask == "say airspeed" || flow == "report airspeed",
            "Range: checking in" => air && !h.Ops.RangeIn,
            "JTAC: checking in" => air && h.Ops.Cas == 0,
            "JTAC: ready to copy" => h.Ops.Cas is 1 or 6,
            "JTAC: readback" => h.Ops.Cas == 2,
            "JTAC: request mark" or "JTAC: tally target" or "JTAC: in" or "JTAC: laser on" => h.Ops.Cas is >= 3 and <= 5,
            "JTAC: off" => h.Ops.Cas >= 4,
            "JTAC: checking out" => h.Ops.Cas > 0,
            "AWACS: checking in" => air && !h.Ops.AwacsIn,
            "AWACS: checking out" => h.Ops.AwacsIn,
            "Tanker: request rejoin" => air && tk == 0,
            "Tanker: visual" => tk == 1,
            "Tanker: say position" => air,
            "Tanker: observation" => tk is 1 or 2,
            "Tanker: pre contact" or "Tanker: refuel complete" => tk >= 2,   // after the join
            "Carrier: Marshal, checking in" => air && h.Boat.Stage == 0,
            "Carrier: pigeons" => air,
            _ when x.StartsWith("Ground: ") => gnd,   // Progressive taxi, Hot brakes
            _ when x.StartsWith("Tower: ") || x.StartsWith("AWACS: ") => air,   // Initial … Going around, Closed/SFO; Picture, Bogey dope …
            _ when x.StartsWith("Range: ") => h.Ops.RangeIn,   // IP inbound, In hot, Off, Check out
            _ when x.StartsWith("Carrier: ") => h.Boat.Stage >= 1,   // after the Marshal check-in
            _ => true,
        };
    }

    /// Suggestion for the radio wheel: answer to the open expectation (own, otherwise the flight lead's), otherwise the procedure.
    static (string Role, string Text)? Expected(Pilot? p, (string Role, string Text)? flow)
    {
        if (p == null) return flow;
        var e = new[] { p, p.Lead }.Select(x => x?.Expect).FirstOrDefault(x => x is { } v && v.Until > Now());
        var r = e is { } x && Answer(p, x.Role, x.Ask, flow) is { } s ? (x.Role, s) : flow;
        return r is ("Approach", var tx) && (p.Lead ?? p).Active is { } a ? (a.Dep("Approach"), tx) : r;   // after the handoff in departure "Departure: …" (own frequency), goes to Departure anyway
    }

    /// Answer to a question/report (null = no sensible one, then the procedure).
    static string? Answer(Pilot p, string role, string ask, (string Role, string Text)? flow)
    {
        var t = p.Tel;
        var f = (p.Lead ?? p).Active?.F;
        var (kind, lb) = Carrier.CvType(p.Type);
        var state = lb > 0 ? (p.Fuel * lb / 1000).ToString("0.0", CultureInfo.InvariantCulture) : null;   // Fuel in 1000 lbs (carrier types)
        string Ft(Telemetry x) => $"{Math.Round(x.AltMsl / 0.3048 / 100) * 100:0} feet";
        switch (ask)
        {
            case "say airspeed": return t == null ? null : Tower.IasSay(t);   // like radio wheel "Fahrt melden" (R129), Tower.IasSlot understands it
            case "say altitude": return t == null ? null : Ft(t);
            case "say heading":
                if (t == null) return null;
                int h = ((int)Math.Round(t.Hdg * 180 / Math.PI - (f?.MagVar ?? 0)) % 360 + 360) % 360;
                return $"heading {(h == 0 ? 360 : h):000}";
            case "say position": return t == null || f == null ? null : $"{Tower.MilesTxt(Dist(t.X, t.Z, f.X, f.Z))} {Tower.Dir8(Tower.Bearing(f.X, f.Z, t.X, t.Z))} of {f.Name.Replace('-', ' ')}";
            case "say intentions": return t != null && !Tower.OnGround(t) ? (f != null && Tower.IfrAt(f) ? $"request vectors to {f.Name.Replace('-', ' ')}, full stop" : "full stop") : flow?.Text;   // "full stop": Tower understands it (inbound), AWACS the "vectors"
            case "say state": return state == null ? null : $"state {state}";
            case "say souls on board and fuel remaining": return FuelSouls(p);   // R300 (R243 callback); "say nature of emergency" is known only to the pilot: procedure
            case "say needles": return p.Boat.NeedlesSay(t);
            case "say again": return tracedReq.TryGetValue(p.Unit, out var r) ? Regex.Replace(r.Text, @"^\w+: ", "") : null;   // own last request
            case "going around, confirm": return "going around";   // #7 "I show you going around, confirm?"
            case "now": return null;   // #6/#8/#9 "contact … now": the procedure knows the check-in (Departure "airborne, climbing", missed approach "missed approach", Tower "inbound")
            case "call the ball": return $"{(lb > 0 ? kind : Ops.TypeSay(p.Type))} ball{(state != null ? ", " + state : "")}";
        }
        if (!ask.StartsWith("report ")) return null;
        var rep = Regex.Split(ask[7..], @",? or |, ")[0].Trim();   // "in hot or checking out" -> first opportunity
        if (rep.StartsWith("initial")) rep = "initial";   // "initial runway two five", "initial for overhead break"
        else if (Regex.IsMatch(rep, @"^(?:\w+ miles? )?final\b")) rep = "final";   // "four miles final", "final individually"
        else if (rep == "check in") rep = "checking in";
        else if (rep.StartsWith("in inside")) rep = "in hot";   // R284: "continue, report in inside 10 miles" comes after the check-in, is not a new one
        var angels = t == null ? "" : $"angels {Math.Round(t.AltMsl / 0.3048 / 1000):0}";
        if (rep == "commencing")   // Review l4/R277: only from EAT - 15 s (as Carrier.Suggest, then Marshal accepts it), otherwise "negative, your expected approach time is …" (e.g. after new EAT)
            return t != null && flow is ("Carrier", var ct) && ct.StartsWith("commencing") ? $"commencing, {angels}{(state != null ? ", state " + state : "")}" : null;
        rep = rep switch   // Reporting obligation: as the controller demands and understands it (gear included, otherwise "check wheels down")
        {
            "base" or "final" or "low key" => rep + ", gear down",
            "see me" when t != null => "see you at " + angels,   // R232 Case I
            "ip" => "IP inbound",   // R235: "IP" alone is not understood by the Range
            _ => Regex.Replace(rep, @"\bc r p\b", "C R P"),
        };
        // R264: "ready for departure" only if the procedure suggests it (Tower.Suggest: at the holding point); while taxiing nothing ("negative, not at the holding point"), forum 0.9.7: at the holding point of the parallel runway cross first
        if (rep.StartsWith("ready for departure")) return flow is { Text: var ft } && ft.StartsWith("ready") ? ft : null;
        var w = rep.Split(' ')[0];
        return flow is { } fl && fl.Role == role && (fl.Text.StartsWith(w) || fl.Text.Contains(" " + w)) ? fl.Text : rep;   // procedure knows the state ("final, full stop", "four mile final, gear down", "inbound for landing")
    }

    /// Self-test radio wheel suggestion: answer to the last question before the procedure, procedure without or after an expired question.
    static void ExpectTest()
    {
        var p = new Pilot { Unit = "EXP1", Callsign = "Colt 3-2", Type = "FA-18C_hornet", Fuel = 0.5, Gid = 9, Tel = new Telemetry(300, 300, 251 * 0.514444, 0, 0, 0, 0, 0, 0) };
        var o = new Pilot { Unit = "EXP2", Callsign = "Colt 3-1", Gid = 9, Tel = p.Tel };
        (Pilots[p.Unit], Pilots[o.Unit]) = (p, o);
        (string, string)? flow = ("Approach", "inbound for landing");
        string S((string Role, string Text)? s) => s is { } v ? $"{v.Role}: {v.Text}" : "-";
        string H(string text, string role) { Heard(new Tx(text, role, "", 9)); return S(Expected(p, flow)); }
        var res = new[]
        {
            S(Expected(p, flow)),
            H("Colt three two, turn left heading two seven zero, say airspeed.", "Approach"),
            H("Colt three one, say altitude.", "Approach"),   // to the other: stays
            H("Colt three two, call the ball.", "Carrier"),
            H("Colt three two, proceed to initial runway two five, 2000 feet, contact Kutaisi Tower two six three decimal zero, report initial for overhead break.", "Approach"),
            H("Colt three two, Range Alpha, leaving the range, range is cold. Report in hot or checking out.", "Range"),
            H("Colt three two, say again, go around, traffic on runway.", "Tower"),   // "I repeat": no question
            H("Colt three two, Texaco, cleared pre-contact.", "Tanker"),
        };
        tracedReq[p.Unit] = ("Tower: ready for departure", Now());
        var again = H("Colt three two, say again.", "Tower");
        var cap = H("Colt three two, Kutaisi Approach, cleared ILS runway two five. Contact Kutaisi Tower two six three decimal zero, report four miles final.", "Approach");   // "Contact" capitalized: to the Tower
        var ifr = Tower.ForceIfr;
        (p.Active, Tower.ForceIfr) = (new Tower("Colt 3-2"), true);
        var intent = H("Colt three two, roger, say intentions.", "Tower");
        intent += " / " + p.Active.IntentOf(intent[7..], p.Tel);   // the Tower must understand
        (p.Active, Tower.ForceIfr) = (null, ifr);
        // AirfieldFrequencies: after the handoff to Departure (own frequency) Enter shows and sends "Departure: …" (expectation as Approach, Tower.Dep in departure -> Departure; radio wheel FreqOf, Request)
        var kd = Airfield.Kutaisi();
        (kd.Own["Departure"], kd.Own["Approach"]) = (new[] { 273.55 }, new[] { 379.0 });
        p.Active = new Tower(kd, "Colt 3-2");
        p.Active.Load(p.Active.Save() with { Phase = Phase.Away, HandedOff = true }, p.Tel!, Now());
        var dep = H("Colt three two, contact Kutaisi Departure two seven three decimal five five, report airborne.", "Tower") + " / "
                + H("Colt three two, Kutaisi Departure, radar contact, 1 mile west of the field. Continue as cleared, report leaving the control zone.", "Departure");
        dep += $" / {p.Active.Dep("Approach")} {string.Join(",", FieldFreqs(kd, p.Active.Dep("Approach"))!)}";
        // Reporting obligation (main): every request -> Enter suggestion that the controller understands as exactly this report
        p.Active.Load(p.Active.Save() with { Phase = Phase.Departing, HandedOff = true }, p.Tel!, Now());
        flow = p.Active.Suggest(p.Tel);   // #8: handed over, no check-in
        var mp = new List<string> { H("Colt three two, contact Kutaisi Departure now, two seven three decimal five five.", "Tower") };
        p.Active = new Tower("Colt 3-2");
        flow = null;
        foreach (var (tx, want) in new[]
                 {
                     ("Colt three two, I show you going around, confirm? Climb and maintain 2000 feet, join left hand downwind runway two five, report base.", "goaround"),   // #7
                     ("Colt three two, report base.", "pattern"), ("Colt three two, negative, I show you on downwind, number one, report base.", "pattern"), ("Colt three two, report final.", "final"), ("Colt three two, report overhead.", "overhead"),   // #4/#2, Review l1: base in downwind
                     ("Colt three two, Kutaisi Approach, proceed to C R P north, report C R P north.", "crp"), ("Colt three two, report high key.", "highkey"), ("Colt three two, report low key.", "lowkey"),   // #3, R204
                 })
        {
            var s = H(tx, tx.Contains("Approach") ? "Approach" : "Tower");
            mp.Add(s + (p.Active.IntentOf(s[(s.IndexOf(": ") + 2)..], p.Tel) == want ? "" : " (nicht " + want + ")"));
        }
        flow = ("Tower", "four mile final, gear down");   // Straight-in: sequence knows "four mile final"
        mp.Add(H("Colt three two, Kutaisi Approach, cleared ILS runway two five. Contact Kutaisi Tower two six three decimal zero, report four miles final.", "Approach"));
        flow = null;
        mp.Add(H("Colt three two, Kutaisi Approach, radar contact. Continue as cleared, report leaving the control zone.", "Approach") + " / " + H("Colt three two, report clear of the zone.", "Approach"));   // #10/#12 (Tower test R7/N3)
        var (bs0, cv) = (Carrier.Boats, new Carrier { Stage = 1 });
        Carrier.Boats = new() { new("CVN-73", "CVN", "CVN_73", 0, 0, 0, 10, 2, 127.5, "73X", "11") };
        var early = H("Colt three two, Marshal, report commencing.", "Carrier");   // Review l4/R277: before EAT - 15 s (Carrier.Suggest without "commencing") no suggestion
        flow = ("Carrier", "commencing");
        foreach (var tx in new[] { "Colt three two, Marshal, report commencing.", "Colt three two, report see you at ten.", "Colt three two, Marshal, case one recovery, hold angels 2, report see me.", "Colt three two, update state, switch Tower.", "Colt three two, three quarter mile, call the ball." })
        {
            var s = H(tx, "Carrier");
            mp.Add(s + (cv.Wants(Tower.Normalize(s[9..])) ? "" : " (Träger versteht es nicht)"));   // #17, #18, R232, R280, #16
        }
        Carrier.Boats = bs0;
        flow = null;
        if (early != "-") mp.Add("vor EAT: " + early);
        var (rg0, ome) = (Ops.Range, new Ops.Me("Colt 3-2", p.Tel, "FA-18C_hornet", 2));
        Ops.Range = (0, 0, 5000);
        foreach (var tx in new[] { "Colt three two, Range Alpha, report off.", "Colt three two, Range Alpha, range is cold. Report IP.", "Colt three two, continue, report in inside 10 miles." })   // #21, R235, R284
        {
            var s = H(tx, "Range");
            var a = new Ops().OnTranscript("Range", s[7..], ome, new List<Traffic>(), new List<Airfield>(), 0).FirstOrDefault()?.Text ?? "";
            mp.Add(s + (a != "" && !a.Contains("say again") ? "" : " (Range: " + a + ")"));
        }
        Ops.Range = rg0;
        var tkAir = new List<Traffic> { new(2, "KC135MPRS", 0, 30 * 1852, 6700, 0, 140, "ATC Texaco #1", "", true, 2) };
        var vis = H("Colt three two, Texaco, not cleared to join, remain 1000 feet below, report visual.", "Tanker");   // #20
        var tkA = new Ops().OnTranscript("Tanker", vis[8..], ome with { Tel = p.Tel! with { X = 0, Z = 29 * 1852 } }, tkAir, new List<Airfield>(), 0).FirstOrDefault()?.Text ?? "";
        mp.Add(vis + (tkA.Contains("cleared to join") ? "" : " (Tanker: " + tkA + ")"));
        mp.Add(H("Colt three two, Texaco, disconnect, you received 5.4, move to the right wing. Report complete, or pre-contact for more.", "Tanker"));   // R288: full -> complete
        var mpAll = string.Join(" | ", mp);
        if (mpAll != "Departure: airborne, climbing | Tower: going around | Tower: base, gear down | Tower: base, gear down | Tower: final, gear down | Tower: overhead | Approach: C R P north | Tower: high key | Tower: low key, gear down | " +
                     "Tower: four mile final, gear down | Approach: leaving the control zone / Approach: clear of the zone | Carrier: commencing, angels 1, state 5.4 | Carrier: see you at ten | Carrier: see you at angels 1 | Carrier: state 5.4 | Carrier: Hornet ball, 5.4 | " +
                     "Range: off | Range: IP inbound | Range: in hot | Tanker: visual | Tanker: complete")
            throw new Exception("Funkrad-Vorschlag Meldepflicht: " + mpAll);
        (p.Active, flow) = (null, ("Approach", "inbound for landing"));
        Expect(p, "Approach", "say airspeed", -1);
        var old = S(Expected(p, flow));
        Expect(p, "Ground", "report ready for departure", ReportSec);   // Forum 0.9.7: taxi clearance requires "ready for departure", at the holding point of the parallel runway cross first
        if (S(Expected(p, ("Ground", "holding short runway zero three left, request crossing"))) is var xs && xs != "Ground: holding short runway zero three left, request crossing")
            throw new Exception("Funkrad-Vorschlag Parallelbahn: " + xs);
        // R264: shared frequency "Hold short runway two five, report ready for departure": no suggestion 2 km before the holding point (otherwise "negative, not at the holding point" and the same expectation again)
        var kr = Airfield.Kutaisi();
        var (ux, uz) = (kr.End("07").Dx, kr.End("07").Dz);   // Direction 07 -> 25
        var taxi = new Telemetry(kr.Elev, 0, 5, 0, kr.X - 800 * ux - 400 * uz, kr.Z - 800 * uz + 400 * ux, 0, 0, 760);
        p.Active = new Tower(kr, "Colt 3-2");
        p.Active.Load(p.Active.Save() with { Phase = Phase.TaxiOut }, taxi, Now());
        if (S(Expected(p, p.Active.Suggest(taxi))) is var rt && rt != "-") throw new Exception("Funkrad-Vorschlag beim Rollen: " + rt);
        p.Active = null;
        var souls = H("Colt three two, Kutaisi Approach, roger mayday, all traffic is holding. Turn left heading two seven zero. Runway two five available, QNH one zero one three, say souls on board and fuel remaining.", "Approach");
        if (souls != "Approach: 45 minutes fuel, one soul on board") throw new Exception("Funkrad-Vorschlag R300 souls/fuel: " + souls);
        Pilots.Remove(p.Unit); Pilots.Remove(o.Unit); tracedReq.Remove(p.Unit);
        var all = string.Join(" | ", res) + $" | {again} | {cap} | {intent} | {dep} | {old}";
        if (all != "Approach: inbound for landing | Approach: 250 knots | Approach: 250 knots | Carrier: Hornet ball, 5.4 | Tower: initial | Range: in hot | Range: in hot | Tanker: pre contact | Tower: ready for departure | " +
                   "Tower: final, gear down | Tower: request vectors to Kutaisi, full stop / inbound | Departure: airborne / Departure: leaving the control zone / Departure 273.55 | Approach: inbound for landing"
            || Tower.IasSlot(res[1].Split(": ")[1]) != 250)   // Suggestion "250 knots" must be understood by Approach as a speed report (R129)
            throw new Exception("Funkrad-Vorschlag (Erwartung): " + all);
        Console.WriteLine("OK   Funkrad Enter: say airspeed -> IAS (Approach versteht es als Fahrtmeldung), call the ball -> Hornet ball mit Sprit, report initial (nach contact Tower) -> Tower initial, nach contact Departure bzw. von Departure -> Departure: … auf der Departure-Frequenz, say again -> letzte Anfrage, sonst/abgelaufen Ablauf; Meldepflicht: contact Departure now -> Departure: airborne, going around confirm -> going around, report base/final/low key -> mit gear down, overhead, C R P, high key, four miles final, leaving the control zone, clear of the zone, commencing (angels, state), see you at ten, see me, update state -> state, call the ball, off, IP -> IP inbound, in inside 10 miles -> in hot (R284), visual; jeder Vorschlag vom Lotsen verstanden");
        WheelFitsTest();
        ModulesTest();
        F10ParityTest();
        JtacProgramTest();
    }

    /// Selftest R390: F10 menu of the mission script (Lua MENU) against the radio wheel – same names and texts, deliberate omissions on a list
    /// (wheel: Settings window; Select airfield has no text; F10 has its own Settings below ATC). Skipped without the mod folder (installed app).
    static void F10ParityTest()
    {
        string? lua = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && lua == null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "mod", "Scripts", "DcsAtc", "DcsAtcMission.lua"))) lua = Path.Combine(d.FullName, "mod", "Scripts", "DcsAtc", "DcsAtcMission.lua");
        if (lua == null) { Console.WriteLine("SKIP F10-Paritaet (R390): DcsAtcMission.lua nicht gefunden"); return; }
        var src = File.ReadAllText(lua);
        var a = src.IndexOf("local MENU = {", StringComparison.Ordinal);
        var menu = src[a..src.IndexOf("local function prune", a, StringComparison.Ordinal)];
        // entries { "label", "text" } and { L("de", "en"), "text" }; groups ({ "label", nil, { … } }) have no text
        var f10 = Regex.Matches(menu, "\\{\\s*(?:L\\(\"([^\"]*)\",\\s*\"([^\"]*)\"\\)|\"([^\"]*)\")\\s*,\\s*\"([^\"]*)\"")
            .Select(m => (Label: m.Groups[3].Success ? m.Groups[3].Value : L(m.Groups[1].Value, m.Groups[2].Value), Text: m.Groups[4].Value)).ToList();
        var wheel = new List<(string Label, string Text)>();
        void Walk(IEnumerable<Wheel.Item> xs) { foreach (var i in xs) { if (i.Text != null) wheel.Add((i.Label, i.Text)); if (i.Sub != null) Walk(i.Sub); } }
        Walk(Wheel.All);
        var err = new List<string>();
        var kind = new Regex(@"^(?:mayday mayday mayday|pan pan, pan pan, pan pan)(?: fuel)?, (.+), request (?:immediate|priority) landing$");
        string[] sig = { "mayday mayday mayday", "pan pan, pan pan, pan pan" }, omitted = { "settings" };   // F10: the emergency type comes as a submenu (KINDS), built from the signal text
        foreach (var f in f10)
            if (!sig.Contains(f.Text) && !wheel.Contains(f)) err.Add($"F10 '{f.Label}' -> '{f.Text}' nicht im Funkrad");
        foreach (var w in wheel)
        {
            if (kind.Match(w.Text) is { Success: true } k) { if (!src.Contains($"\"{k.Groups[1].Value}\"")) err.Add($"F10 Notfallart '{k.Groups[1].Value}' fehlt"); }
            else if (!omitted.Contains(w.Text) && !f10.Contains(w)) err.Add($"Funkrad '{w.Label}' -> '{w.Text}' fehlt im F10");
        }
        if (f10.Count < 70) err.Add("F10-Einträge nicht gelesen: " + f10.Count);
        if (err.Count > 0) throw new Exception("R390 F10-Menü: " + string.Join(" | ", err));
        Console.WriteLine($"OK   R390 F10-Menü wie Funkrad: {f10.Count} Einträge mit gleichem Namen und Text, nur Einstellungen (eigenes F10-Menü) und Platz wählen ausgelassen");
    }

    /// Selftest R307: radio wheel shows only matching entries (Wheel.Vis with WheelFits), each level at most 9.
    static void WheelFitsTest()
    {
        var kd = Airfield.Kutaisi();
        Telemetry gnd = new(kd.Elev, 0, 0, 0, kd.X, kd.Z, 0, 0, 0), air = new(kd.Elev + 900, 900, 130, 0, kd.X + 5 * NM, kd.Z, 0, 0, 0);
        var p = new Pilot { Unit = "WF1", Callsign = "Colt 3-2", Gid = 9, Tel = gnd };
        Pilots[p.Unit] = p;
        p.Active = new Tower(kd, "Colt 3-2");
        void Ph(Phase ph, Telemetry t) { p.Tel = t; p.Active.Load(p.Active.Save() with { Phase = ph }, t, Now()); }
        string[] Sub(params string[] path)
        {
            var xs = Wheel.All;
            foreach (var s in path) xs = Wheel.Vis(xs, WheelFits(p)).First(i => i.Label == s).Sub!;
            var v = Wheel.Vis(xs, WheelFits(p));
            if (v.Length > 9) throw new Exception($"R307 Funkrad {string.Join("/", path)}: {v.Length} Einträge");
            return v.Select(i => i.Text ?? i.Label).ToArray();
        }
        string[] Top() => Wheel.Vis(Wheel.All, WheelFits(p)).Select(i => i.Label).ToArray();
        var err = new List<string>();
        void Want(bool ok, string what, IEnumerable<string> got) { if (!ok) err.Add($"{what}: {string.Join(",", got)}"); }
        // parked: startup/taxi/ready, no Vacated, no Airborne, no speed report, no AWACS/tanker in the air
        Ph(Phase.Parked, gnd);
        Want(Sub("Ground") is var g1 && g1.Contains("Ground: request startup") && g1.Contains("Ground: request taxi") && !g1.Contains("Ground: runway vacated, request taxi to parking"), "geparkt Ground", g1);
        Want(Sub("Tower") is var t1 && t1.Contains("Tower: ready for departure") && !t1.Contains("Tower: initial"), "geparkt Tower", t1);
        Want(Sub("Approach") is var a1 && a1.SequenceEqual(new[] { "Approach: say again" }), "geparkt Approach", a1);
        Want(Top() is var top1 && !top1.Contains("AWACS") && !top1.Contains("Tanker"), "geparkt Oberebene", top1);
        // shortly after takeoff: Airborne, Request higher, no Ground, no Cancel approach
        Ph(Phase.Departing, air);
        Want(Sub("Approach") is var a2 && a2.Contains("Approach: airborne, climbing") && a2.Contains("Approach: request higher") && !a2.Contains("Approach: cancel approach") && !a2.Contains("Approach: report airspeed"), "Abflug Approach", a2);
        Want(Top() is var top2 && !top2.Contains("Ground") && top2.Contains("Tower"), "Abflug Oberebene", top2);
        // on approach: without a question no speed report and no traffic; after "say airspeed" and traffic advisory both, no Airborne
        Ph(Phase.Inbound, air);
        Want(Sub("Approach") is var a3 && !a3.Contains("Approach: report airspeed") && !a3.Contains(L("Verkehr", "Traffic")) && !a3.Contains("Approach: airborne, climbing") && !a3.Contains("Approach: leaving the control zone"), "Anflug Approach ohne Frage", a3);
        Heard(new Tx("Colt three two, traffic, 2 o'clock, 3 miles, westbound, same altitude, Hornet.", "Approach", "", 9));
        Heard(new Tx("Colt three two, turn left heading two seven zero, say airspeed.", "Approach", "", 9));
        Want(Sub("Approach") is var a4 && a4.Contains("Approach: report airspeed") && a4.Contains(L("Verkehr", "Traffic")) && a4.Contains("Approach: say again") && a4.Contains("Approach: request higher"), "Anflug Approach nach Frage/Verkehr", a4);
        Want(Sub("Approach", L("Verkehr", "Traffic")) is var tr && tr.SequenceEqual(new[] { "Approach: traffic in sight", "Approach: negative contact" }), "Verkehr", tr);
        // R389: zone transit only in the air outside the landing flow (not parked, not inbound/pattern, but away/departing)
        string[] zt = Sub(L("Allgemein", "General"));
        Want(!zt.Contains("Approach: request zone transit"), "Anflug ohne zone transit", zt);
        Ph(Phase.Away, air);
        Want(Sub(L("Allgemein", "General")) is var zt2 && zt2.Contains("Approach: request zone transit"), "Away mit zone transit", zt2);
        Heard(new Tx("Colt three two, radar contact, 1 mile east of the field. Continue as cleared, report leaving the control zone.", "Approach", "", 9));   // R7: report due -> entry visible
        Want(Sub("Approach") is var lz && lz.Contains("Approach: leaving the control zone"), "Away nach report leaving the control zone", lz);
        Ph(Phase.Parked, gnd);
        Want(Sub(L("Allgemein", "General")) is var zt3 && !zt3.Contains("Approach: request zone transit"), "geparkt ohne zone transit", zt3);
        Ph(Phase.Inbound, air);
        // SFO: High/Low key only after "request S F O"
        Want(Sub("Tower", "Closed / SFO") is var s1 && s1.Contains("Tower: request S F O") && !s1.Contains("Tower: high key"), "Closed/SFO ohne SFO", s1);
        p.Active.OnTranscript("Kutaisi Tower, Colt 3-2, request S F O", air, new List<Traffic>(), Now());
        Want(Sub("Tower", "Closed / SFO") is var s2 && s2.Contains("Tower: high key") && s2.Contains("Tower: low key, gear down"), "Closed/SFO nach SFO", s2);
        // landed: Vacated, no Startup, Tower without entry (not in the top level)
        Ph(Phase.TaxiIn, gnd);
        Want(Sub("Ground") is var g2 && g2.Contains("Ground: runway vacated, request taxi to parking") && !g2.Contains("Ground: request startup"), "gelandet Ground", g2);
        Want(Top() is var top3 && !top3.Contains("Tower") && top3.Contains("Ground"), "gelandet Oberebene", top3);
        // without flight state (other player): everything
        Want(Wheel.Vis(Wheel.All, WheelFits(null)).Length == Math.Min(9, Wheel.All.Length), "ohne Zustand Oberebene", Top());
        Pilots.Remove(p.Unit);
        if (err.Count > 0) throw new Exception("R307 Funkrad: " + string.Join(" | ", err));
        Console.WriteLine("OK   R307 Funkrad nur passende Einträge: geparkt Startup/Taxi/Ready, kein Airborne/Fahrt; nach dem Start Airborne + Request higher, kein Ground; Fahrt melden nur nach say airspeed, Traffic nur nach Verkehrshinweis; High/Low key nach SFO; gelandet Vacated, kein Tower; ohne Zustand alles; je Ebene max. 9");
    }

    /// Selftest J1-J3/J5/J11: J/Q/N state lines, radio wheel rules per CAS state, module "jtac".
    static void JtacProgramTest()
    {
        var (jl, jt, jn) = (new List<Ops.Leader>(), new List<Ops.Trg>(), new List<(int, string, double, double)>());
        foreach (var s in new[] { "J;JTAC-1;jtac;Axeman 1-1;2;1000.5;2000.5;150;;AM;;1", "J;FAC-1;faca;Rover 2-1;2;1;2;3;251.5;FM;1514;0",
                                  "Q;JTAC-1;T-1;T-72B;4;3000;0;200;0;1;37T GG 12345 67890;42.1234;42.5678;;", "Q;FAC-1;T-2;BTR-80;2;5;6;7;1;0;37T GG 1 2;42;43;-500;12", "N;2;IP Hammer;-8000;0", "J;kurz" })
            JtacLine(s.Split(';'), jl, jt, jn);
        var err = new List<string>();
        void Want(bool ok, string what) { if (!ok) err.Add(what); }
        Want(jl.Count == 2 && jl[0] is { Freq: 0, Code: "", Alive: true, Kind: "jtac", Side: 2, X: 1000.5 } && jl[1] is { Freq: 251.5, Code: "1514", Alive: false, Mod: "FM" }, "J-Zeilen");
        Want(jt.Count == 2 && jt[0] is { Count: 4, Flak: true, Moving: false, Mgrs: "37T GG 12345 67890", Lat: 42.1234 } && double.IsNaN(jt[0].Fx) && jt[1] is { Fx: -500, Fz: 12, Moving: true }, "Q-Zeilen");
        Want(jn.Count == 1 && jn[0] == (2, "IP Hammer", -8000.0, 0.0), "N-Zeilen");
        var (ol, ot, on) = (Ops.Leaders, Ops.Targets, Ops.NavPts);
        (Ops.Leaders, Ops.Targets) = (jl.Select(l => l with { Alive = true }).ToList(), jt);
        var p = new Pilot { Unit = "JT1", Callsign = "Colt 4-1", Gid = 9, Coalition = 2, Type = "FA-18C_hornet", Tel = new(3000, 3000, 200, 0, -15000, 0, 0, 0, 0) };
        string Fit() => string.Join(",", new[] { "checking in", "ready to copy", "readback", "request mark", "tally target", "in", "off", "laser on", "checking out" }.Where(x => WheelFits(p)("JTAC: " + x)));
        Want(Fit() == "checking in", "Funkrad JTAC vor Check-in: " + Fit());
        string Say(string t) => string.Join(" | ", p.Ops.OnTranscript("JTAC", t, MeOf(p), Array.Empty<Traffic>(), Fields, 0).Select(c => c.Text));
        Say("JTAC: checking in");
        Want(Fit() == "ready to copy,checking out", "Funkrad JTAC nach Check-in: " + Fit());
        Say("JTAC: ready to copy");
        Want(Fit() == "readback,checking out", "Funkrad JTAC nach 9-Liner: " + Fit());
        Say("JTAC: " + p.Ops.CasWheel("readback"));
        Want(Fit() == "request mark,tally target,in,laser on,checking out", "Funkrad JTAC nach Readback: " + Fit());
        Say("JTAC: tally target"); Say("JTAC: " + p.Ops.CasWheel("in"));
        Want(Fit() == "request mark,tally target,in,off,laser on,checking out" && p.Ops.Cas == 5, "Funkrad JTAC nach IN: " + Fit());
        Say("JTAC: checking out");
        Want(Fit() == "checking in" && p.Ops.Cas == 0, "Funkrad JTAC nach Check-out: " + Fit());
        var (aw, jf, ff) = (Cfg.Frequencies["AWACS"], Cfg.Frequencies["JTAC"], 251.5);
        LoadModules("core,atc,tanker");
        Want(!RoleOn("JTAC") && !RoleOn("FACA") && !TextOn("JTAC: checking in") && !ListenFreqs(new()).Contains(jf) && !Wheel.Mod(Wheel.All).Any(i => i.Label == "JTAC"), "jtac aus: Rolle/Funkrad/Frequenz");
        LoadModules("jtac");
        Want(RoleOn("JTAC") && RoleOn("FACA") && TextOn("JTAC: in") && ListenFreqs(new()).Contains(jf) && ListenFreqs(new()).Contains(ff) && string.Join(",", Wheel.Mod(Wheel.All).Select(i => i.Label)) is "JTAC,Allgemein" or "JTAC,General",
             "nur jtac: Rolle/Funkrad/Frequenzen " + string.Join(",", Wheel.Mod(Wheel.All).Select(i => i.Label)));
        LoadModules(null);
        (Ops.Leaders, Ops.Targets, Ops.NavPts) = (ol, ot, on);
        p.Ops.Leave();
        if (err.Count > 0) throw new Exception("JTAC (Programm): " + string.Join(" | ", err));
        Console.WriteLine("OK   JTAC: J/Q/N-Zeilen, Funkrad-Regeln je Zustand, Modul jtac (Rolle, Funkrad, Frequenzen)");
    }

    /// Selftest modules (installer/settings, modules.txt): deselected = no wheel entry, frequency not monitored, request dropped; missing file = all on.
    static void ModulesTest()
    {
        var kd = Airfield.Kutaisi();
        var p = new Pilot { Unit = "MOD1", Callsign = "Colt 3-2", Gid = 9, Tel = new(kd.Elev, 0, 0, 0, kd.X, kd.Z, 0, 0, 0) };
        p.Active = new Tower(kd, "Colt 3-2");
        Pilots[p.Unit] = p;
        int Said(string text) { while (SayQueue.TryTake(out _)) { } Request(p, text, "wheel", true); int n = 0; while (SayQueue.TryTake(out _)) n++; return n; }
        string Top() => string.Join(",", Wheel.Mod(Wheel.All).Select(i => i.Label));
        string Sub(string l) => string.Join(",", Wheel.Mod(Wheel.All.First(i => i.Label == l).Sub!).Select(i => i.Text ?? i.Label));
        double aw = Cfg.Frequencies["AWACS"], tk = Cfg.Frequencies["Tanker"];
        var err = new List<string>();
        void Want(bool ok, string what) { if (!ok) err.Add(what); }
        var all = (Top(), ListenFreqs(new()).ToArray(), Said("Ground: request startup"));
        Want(all.Item1 == string.Join(",", Wheel.All.Select(i => i.Label)) && all.Item2.Contains(aw) && all.Item3 > 0, $"ohne Datei alles an: {all.Item1}, {all.Item3} Sprüche");
        LoadModules("core,atc,tanker");
        Want(!Top().Contains("AWACS") && Top().Contains("Tanker") && !ListenFreqs(new()).Contains(aw) && ListenFreqs(new()).Contains(tk), "awacs aus: Funkrad/Frequenz " + Top());
        Want(!RoleOn("AWACS") && !RoleOn("Flight") && RoleOn("Tower") && !On("crew"), "awacs aus: Rollen");
        LoadModules("awacs");
        Want(Top() == "AWACS,Allgemein" || Top() == "AWACS,General", "nur awacs: Oberebene " + Top());
        Want(Sub("AWACS") is var a && a.Contains("AWACS: request picture") && !a.Contains("AWACS: vector to tanker"), "nur awacs: kein nearest tanker " + Sub("AWACS"));
        Want(Sub(L("Allgemein", "General")) == "radio check,say again,debrief,settings", "nur awacs: Allgemein " + Sub(L("Allgemein", "General")));
        Want(Said("Ground: request startup") == 0 && !RoleOn("Tower"), "atc aus: Ground antwortet");
        LoadModules(null);
        Pilots.Remove(p.Unit);
        if (err.Count > 0) throw new Exception("Module: " + string.Join(" | ", err));
        Console.WriteLine("OK   Module: ohne modules.txt alles an; awacs aus -> kein AWACS im Funkrad, Frequenz nicht abgehört; nur awacs -> Oberebene AWACS/Allgemein, kein nearest tanker, Allgemein nur radio check/say again/debrief/settings; atc aus -> Ground schweigt");
    }
}
