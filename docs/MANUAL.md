# DCS-ATC Manual

DCS-ATC is a voice air traffic control for DCS World. Over SRS it acts as Ground, Tower, Approach/Departure, ATIS, AWACS, tanker, range, carrier and crew chief at every airfield on the map. You talk to it in English over SRS, with a radio wheel, with the F10 menu or in the multiplayer chat, and it answers with voices over SRS and with text in the game. Speech recognition and the voices run offline on your PC.

DCS-ATC never spawns aircraft. It only provides the radio for units that are already in your mission, including units the mission spawns later.

*Deutsche Fassung: [ANLEITUNG.md](ANLEITUNG.md).*

## Contents

1. [Getting started](#getting-started)
   - [What you need](#what-you-need) · [Installing](#installing) · [Setting up SRS](#setting-up-srs) · [Keys at a glance](#keys-at-a-glance) · [Your first flight](#your-first-flight-ramp-start-at-kutaisi)
2. [Ways to talk](#ways-to-talk)
   - [Voice over SRS](#voice-over-srs) · [Phrasing that is always understood](#phrasing-that-is-always-understood) · [Radio wheel](#radio-wheel) · [Radio wheel in VR](#radio-wheel-as-text-in-the-game-vr) · [F10 menu](#f10-menu) · [Chat](#typing-in-the-chat-multiplayer) · [Text in the game](#text-in-the-game) · [Voices](#voices-radio-effect-and-speech-pace) · [Altimeter unit](#altimeter-setting-hpa-or-inhg) · [Runway lights](#runway-lights)
3. [Airfield ATC](#airfield-atc)
   - [Airfields, callsigns and frequencies](#airfields-callsigns-and-frequencies) · [ATIS](#atis) · [Runway selection](#runway-selection) · [Weather: VFR and IFR](#weather-vfr-and-ifr) · [Ground](#ground) · [Tower: departure](#tower-departure) · [Tower: arrival](#tower-arrival) · [Approach](#approach) · [Airspace](#airspace) · [Emergencies](#emergencies) · [Landing grade and debriefing](#landing-grade-and-debriefing) · [Mandatory reports](#mandatory-reports) · [Phrase table](#phrase-table)
4. [Carrier operations](#carrier-operations)
5. [AWACS, tankers and ranges](#awacs-tankers-and-ranges)
   - [AWACS](#awacs) · [Tankers](#tankers) · [Range](#range) · [AI flights on the radio](#ai-flights-on-the-radio) · [Ground crew and crew chief](#ground-crew-and-crew-chief)
6. [Multiplayer, dedicated server, custom frequencies and other maps](#multiplayer-dedicated-server-custom-frequencies-and-other-maps)
   - [How multiplayer works](#how-multiplayer-works) · [Dedicated server, step by step](#dedicated-server-step-by-step) · [Custom frequencies per airfield](#custom-frequencies-per-airfield) · [Other maps than Caucasus](#other-maps-than-caucasus)
7. [Appendix: settings, logs and troubleshooting](#appendix-settings-logs-and-troubleshooting)
   - [The settings window](#the-settings-window) · [config.jsonc reference](#configjsonc-reference) · [Log files](#log-files) · [Troubleshooting](#troubleshooting)

The examples use the callsign Enfield 1-1 and write many numbers as digits. On the radio, controllers say them digit by digit (for example "two five one decimal five").

## Getting started

### What you need

- Windows 10 or 11, 64-bit.
- DCS World. All maps work. Caucasus is the best tested.
- **SRS (DCS-SimpleRadio-Standalone).** The default folder is `C:\Program Files\DCS-SimpleRadio-Standalone`. If yours is somewhere else, see [Setting up SRS](#setting-up-srs).
- A microphone. All radio calls are in English.
- About 1.2 GB of disk space. Speech recognition runs on the CPU and takes a few seconds per call.

### Installing

1. **Close DCS** and run `DCS-ATC-Setup.exe`.
   - **Windows SmartScreen** may say "Windows protected your PC" because the setup is new and not code-signed. Click **More info**, then **Run anyway**.
2. **Choose the language** (English or German). This sets the language of the program window, the settings window and the in-game F10 menu. The radio is always in English. To change the language later, run setup again and pick the other language, or use the settings window.
3. **Admin prompt for `MissionScripting.lua`.** Setup needs admin rights for this one Windows (UAC) prompt only. The prompt lets setup add one line to `<DCS folder>\Scripts\MissionScripting.lua`. That line loads only the DCS-ATC mission script. Missions themselves stay locked and still have no file access. A backup is saved next to the file as `MissionScripting.lua.vor-dcsatc`. If you decline, DCS-ATC gets no mission data: no F10 menu, no callsign from your slot and no AI traffic. You can apply the patch later with **Start menu → DCS-ATC → Repair after DCS update**.
4. **Set keys.** The last setup page has a ticked box, "Set keys (radio wheel, push-to-talk – keyboard or HOTAS)". A small window then asks for two keys, one after the other:
   - **Open/close radio wheel.** Press a key or a HOTAS button.
   - **Push-to-talk.** You only need this if you don't talk through SRS. Press a key or a HOTAS button.

   Press **Esc** at either prompt to keep the current value. The new keys apply the next time DCS-ATC starts. You can run this again at any time from **Start menu → DCS-ATC → Set keys** or from the settings window.
5. **Done.** From now on DCS-ATC starts by itself with every mission and appears in the taskbar as a small console window. It closes when you leave the mission or quit DCS. Keep that window open while you fly.

Setup installs to `%LOCALAPPDATA%\Programs\DCS-ATC` and creates these Start-menu entries:

| Start-menu entry | What it does |
|---|---|
| Start DCS-ATC | Starts the app manually. Normally not needed. |
| Set keys (radio wheel, push-to-talk) | Opens the key prompt from step 4. |
| Settings (callsign, volume, radio) | Opens the settings window. |
| Ground crew (default) | Opens `DcsAtcOptions.lua` in Notepad. |
| Repair after DCS update | Patches `MissionScripting.lua` again (admin prompt). |
| Readme | Opens the short readme. |
| Uninstall DCS-ATC | Removes DCS-ATC, including the lines it added to `MissionScripting.lua` and `Export.lua`. |

#### What setup changes in DCS

- `Saved Games\DCS\Scripts\Hooks\DcsAtcHook.lua` starts DCS-ATC with each mission and passes radio-wheel requests from other players to the host.
- `Saved Games\DCS\Scripts\DcsAtc\` holds the mission script, the default options (`DcsAtcOptions.lua`) and the language file.
- `Saved Games\DCS\Scripts\DcsAtcExport.lua`, plus one line at the end of `Export.lua`, sends your own aircraft's data to DCS-ATC. Other export scripts such as SRS or Tacview are not touched.
- One line is added to `<DCS folder>\Scripts\MissionScripting.lua` (see step 3 above).

If your Saved Games folder was moved, for example to another drive, setup still finds it. If setup can't find `Saved Games\DCS` at all, it asks you to pick your DCS folder in Saved Games, which is the folder that contains `Config` and `Scripts`. If only `Saved Games\DCS.openbeta` exists, setup uses that one.

#### Installing on a dedicated server

DCS-ATC can also run on a PC that hosts a DCS dedicated server (`DCS_server.exe`) with nobody sitting at it. Tick the setup task **"Dedicated server"**. On a PC with only a dedicated server, setup selects it by itself. For the full steps, see [Dedicated server, step by step](#dedicated-server-step-by-step).

### Setting up SRS

**SRS must be installed on the PC that runs DCS-ATC, and its SRS server must run on that PC.** DCS-ATC listens to all players and transmits its controllers through that local SRS server.

- **Finding the SRS folder.** DCS-ATC uses `SrsPath` from `config.jsonc`, which is `C:\Program Files\DCS-SimpleRadio-Standalone` by default. If that folder isn't a valid SRS folder, DCS-ATC tries these in order:
  1. The SRS installation recorded in the registry.
  2. The folder of a running SRS client or server.

  To choose the folder yourself, use **Settings → SRS folder → "…"**. A valid SRS folder contains `ExternalAudio\DCS-SR-ExternalAudio.exe`. You can also point the setting at the `Client` or `Server` subfolder or at one of the SRS `.exe` files, and you can paste the path with quotes. DCS-ATC recognises both the SRS 2.2+ layout (`Client\`, `Server\` and `ExternalAudio\` subfolders) and the older layout with everything in one folder.
- **Starting the server and client.** When DCS-ATC starts as host, it starts the SRS server and then the SRS client, unless they are already running. If one of them is not in the SRS folder, the console says so: `SRS-Server not found in "…" – check SrsPath in config.jsonc or the settings.`
- **Auto-connect.** If the SRS client is not connected yet, DCS-ATC asks it to connect to the local server (`127.0.0.1`, with the port from the server's `server.cfg`, normally 5002). It uses the same auto-connect mechanism as the SRS DCS hook. It tries up to three times. It never touches a client that is already connected, even if it is connected to another server.
  - Success shows `SRS client connected to 127.0.0.1:5002.`
  - Failure shows `SRS client did not connect – enter 127.0.0.1:5002 in the client and press Connect.`
  - To turn this off, set `"SrsAutoConnect": false`.
- **Multiplayer.** Everyone connects their SRS client to the SRS server on the host's PC, which is the PC running DCS-ATC.
- **Tuning frequencies.** All frequencies are AM.
  - **Airfields** use their own DCS frequency, the one shown on the F10 map. Ground, Tower and Approach share it, and you can use either the UHF or the VHF frequency. Examples on Caucasus: Kutaisi 263.0 / 134.0, Senaki 261.0 / 132.0, Batumi 260.0 / 131.0.
  - **Other stations** have defaults: ATIS 263.5, AWACS 251.5, Tanker 255.5, Range 267.5, Carrier 127.5. An AWACS or carrier that has its own frequency in the mission uses that frequency instead.
  - **The radio wheel** shows the frequency of every station on its top level. A station is shown in red with ✗ when none of your SRS radios is tuned to it.
- **You only hear what you are tuned to.** Controllers transmit on their own frequency. A controller's call also appears as text in the game for your group, but only if one of your radios is on that frequency. If SRS can't tell which radios you have, you see all text.

### Keys at a glance

| Key | Function |
|---|---|
| `'` (US keyboard) / `Ä` (German keyboard), default | Opens or closes the radio wheel. Works only while DCS is the active window. |
| `1`–`9` (also on the numpad) | Choose an entry. |
| `Enter` or `0` | Send the suggested call. If a controller has just asked you something or asked for a report, the suggestion is the answer (for example, after "say airspeed" it offers `<your IAS> knots`). Otherwise it is the next normal step of your flight. The centre of the wheel shows it as `ENTER ▸ <controller>: <call>`. |
| `Backspace` | Back one level. |
| `Esc` | Close the wheel. |
| `+` / `-` | Make the wheel bigger or smaller (the size is saved). |
| `Print Screen` (wheel open) | Take a screenshot including the wheel, saved to `Pictures\DCS-ATC`. |
| HOTAS (if set) | The wheel button opens and closes the wheel. Hat up/down moves the highlight, right selects, left goes back. |
| Push-to-talk, default right `Ctrl` | Used only when SRS is not connected. With SRS you talk with your normal SRS PTT. |

All wheel entries are listed under [Radio wheel](#radio-wheel). The F10 radio menu **ATC** offers most of them in the game. In VR you can't see the desktop wheel, so it is also shown as text in the game (see [Radio wheel as text in the game](#radio-wheel-as-text-in-the-game-vr)).

### Your first flight: ramp start at Kutaisi

Use any Caucasus mission in which you start cold on the ramp at Kutaisi. With a mission running, your callsign comes from your DCS slot.

How to make calls:

- **Calling.** Press your SRS PTT on the controller's frequency and say the station, then your callsign, then your request. For example: "Kutaisi Ground, Enfield 1-1, request startup".
- **Without speaking.** You can also send every call below from the radio wheel. Often **Enter** is all you need, because it suggests the next step.
- **Callsigns.** You can say your callsign as "Enfield 1-1" or "Enfield one one".

At Kutaisi, Ground, Tower and Approach all share the airfield frequency. You can stay on 263.0 (UHF) or 134.0 (VHF) for the whole flight. When a controller says "contact Kutaisi Tower" without a frequency, a different controller takes over on the same frequency.

**1. Tune your radios.** Set one radio to the ATIS on 263.5 and one to Kutaisi on 263.0 or 134.0, both AM.

**2. Listen to ATIS.** ATIS broadcasts continuously on 263.5 for every airfield that has a player within 40 NM. If several airfields share the frequency, the broadcast rotates between them. Each broadcast starts with the airfield name and the information letter:

> Kutaisi information Alpha, time … Zulu. Runway two five in use, left hand pattern, … feet. Wind …. Visibility one zero kilometers or more, sky clear. Temperature …. QNH …, altimeter …. Tower one three four decimal zero, UHF two six three decimal zero. Advise on initial contact you have information Alpha.

You can also play it on request with Radio wheel → General → Listen to ATIS. When the information letter changes, the field announces it to everyone ("Attention all aircraft, Kutaisi information Bravo now current, …").

**3. Start-up (Ground).**

```
You:    Kutaisi Ground, Enfield 1-1, request startup, information Alpha.
Ground: Enfield one one, Kutaisi Ground, start up approved. Report ready to taxi.
```

If you don't mention the current ATIS letter, Ground adds it, together with the runway and the altimeter setting. In that case the reply above would end like this: "…start up approved. Information Alpha is current, runway two five in use, altimeter …. Report ready to taxi."

**4. Taxi (Ground).**

```
You:    Kutaisi Ground, Enfield 1-1, ready to taxi.
Ground: Enfield one one, Kutaisi Ground, taxi to holding point runway two five via …. Squawk ….
        Hold short runway two five, report ready for departure.
```

- **Accepted phrasing.** "request taxi", "ready to taxi" and "request startup and taxi" all work. The last one approves start-up and taxi together.
- **Progressive taxi.** Once you are taxiing, say "request progressive taxi" to have Ground guide you turn by turn.

**5. Take-off (Tower).** At the holding point:

```
You:   Kutaisi Tower, Enfield 1-1, holding short runway 25, ready for departure.
Tower: Enfield one one, after departure exit via C R P west, not above … until leaving the control zone,
       runway two five, wind …, cleared for takeoff.
```

- **Choosing your exit.** Add a direction to pick the reporting point: "ready for departure, north".
- **If the runway isn't free.** With traffic on final or on the runway you get "hold short runway two five, traffic …" or "line up and wait". Tower clears you by itself as soon as the runway is free.
- **Too early.** If you call "ready" before you reach the holding point, you get "negative, you are not at the holding point".
- **Crossing a parallel runway.** If a parallel runway lies between the ramp and your departure runway (e.g. 03L before 03R), the taxi clearance ends with "hold short runway zero three left". At that holding point report "holding short runway 03L, request crossing" (the radio wheel suggests it; "ready for departure" there gets the same answer): "cross runway zero three left, hold short runway zero three right". There is no takeoff clearance at the parallel runway. If it is busy you get "hold short runway zero three left, traffic …" and the crossing clearance follows by itself once it is clear. Cross without clearance and you get "You are not cleared onto runway …".

**6. Departure (Approach).** When you climb through about 500 ft above the ground:

```
Tower:    Enfield one one, contact Kutaisi Approach.
You:      Kutaisi Approach, Enfield 1-1, airborne, climbing.
Approach: Enfield one one, Kutaisi Approach, radar contact, … miles … of the field.
          Continue as cleared, report leaving the control zone.
You:      Kutaisi Approach, Enfield 1-1, leaving the control zone.
Approach: Enfield one one, roger, resume own navigation, … frequency change approved. Good day.
```

If you are already outside the control zone when you check in, you get "radar contact, …. Resume own navigation, maintain VFR." instead.

Make the check-in call yourself. Approach does not identify you on its own. If you haven't checked in 60 s after the handoff (or when you reach the edge of the control zone), Tower reminds you once: "Enfield one one, contact Kutaisi Approach now." If you still don't check in, 60 s later you get no radar service: "no contact with Kutaisi Approach, radar service not provided, squawk VFR, frequency change approved." Under IFR this is also a deviation.

**7. Coming back (Approach).** Call Approach while you are still well out:

```
You:      Kutaisi Approach, Enfield 1-1, 25 miles west, 6000 feet, inbound for landing, information Alpha.
Approach: Enfield one one, Kutaisi Approach, identified, 25 miles west. Runway two five in use, wind …, altimeter ….
          Expect vectors to initial runway two five for overhead break. Turn … heading …, descend and maintain ….
```

- **Radar vectors.** Approach now gives you headings, altitudes and, if needed, a speed. You don't have to read every instruction back, but you must fly them.
- **Holding.** If the pattern is busy, you are sent to a hold and given an expected approach time ("I will call you").
- **Straight-in.** To fly a straight-in approach, say "request straight in" (or "request ILS"). In bad weather you get an instrument or straight-in approach anyway.
- **Handoff to Tower.** Once you are established on the course to the initial point:

```
Approach: Enfield one one, on course, … miles from initial, maintain …, contact Kutaisi Tower,
          report initial for overhead break.
```

**8. Overhead break and landing (Tower).** **You must report initial yourself.** Tower gives the break clearance only in answer to your call:

```
You:   Kutaisi Tower, Enfield 1-1, initial runway 25.
Tower: Enfield one one, runway two five, cleared break left hand. Report final.
```

If you reach the initial point without calling, Tower asks once: "Enfield one one, Kutaisi Tower, report initial." If you still fly on toward the break without a clearance, 1 NM later you get "no break clearance, continue straight through, re-enter initial runway two five, report initial."

After the break, report base (or final) with your gear down:

```
You:   Kutaisi Tower, Enfield 1-1, base, gear down, full stop.
Tower: Enfield one one, runway two five, wind …, cleared to land.
```

- **Gear check.** If your gear is still up and you didn't say "gear down", Tower answers "check wheels down".
- **Wrong runway.** If you are lined up for the wrong runway, Tower calls "check runway!".
- **Other options.** Say "touch and go", "low approach" or "the option" instead of "full stop" (Radio wheel → Tower → "Base, touch and go").
- **Going around.** If you go around, say "going around".

You must report base or final yourself; Tower never clears you to land without that call. If you don't call, Tower asks once at about 2.5 NM (or on base): "Enfield one one, report final." If you still have no landing clearance inside 0.8 NM or below 300 ft, you are sent around: "go around, no landing clearance, climb and maintain 2000 feet, join left hand downwind runway two five, report base." This is also a deviation in the debriefing.

**9. After landing (Tower, then Ground).** Tower tells you where to vacate, for example "Vacate left via Whiskey. Report vacated." Then call Ground:

```
You:    Kutaisi Ground, Enfield 1-1, runway vacated, request taxi to parking.
Ground: Enfield one one, taxi to … via …, parking spot …, ….
```

Ground guides you back toward your start position with direction and distance. Don't call just "request taxi" here, because before your taxi-in clearance that counts as a taxi request for departure.

If you taxi on without a taxi clearance (faster than 5 kt and more than 150 m from the runway), you get "hold position, you are not cleared to taxi. Request taxi." and a deviation.

**10. Debriefing.** Radio wheel → General → Debriefing lists your flight, including any airspace deviations.

## Ways to talk

You can talk to DCS-ATC in four ways, and you can mix them freely during a flight:

| Way | What you do | Works for |
|---|---|---|
| **Voice over SRS** | Press your SRS push-to-talk on the controller's frequency and speak English | Everyone in the mission, including multiplayer clients |
| **Radio wheel** | Press the wheel key and pick a call with the number keys or your HOTAS | Mission host and single player (clients get a reduced wheel, see below) |
| **F10 menu** | DCS radio menu → **F10 Other → ATC** | Every player slot in the mission |
| **Chat text** | Type `ATC>` followed by the call in the multiplayer chat | Multiplayer only |

Whichever way you use, the controllers answer the same way: by voice over SRS on their frequency, and as text in the game.

### Voice over SRS

#### How it works

DCS-ATC listens on SRS. Tune a radio to a controller frequency, hold your normal **SRS push-to-talk** and speak. You don't need a separate key. Speech recognition runs offline on your CPU and takes a few seconds per call.

- **Language:** English only.
- **Modulation:** AM (`"Modulation": "AM"`).
- **SRS is started for you.** DCS-ATC starts the SRS server and client if they are not running, and connects a client that isn't connected yet (see [Setting up SRS](#setting-up-srs)).
- `"SrsListen": true` turns listening on. With `false`, only the push-to-talk key below works.

#### The frequency picks the controller

The frequency you transmit on decides who answers:

| You transmit on | Who answers |
|---|---|
| An **airfield frequency from the DCS map** (UHF or VHF, for example Kutaisi 263.0 / 134.0) | That airfield. Ground, Tower and Approach share the frequency, and the content of your call picks the controller ("request taxi" → Ground, "ready for departure" → Tower, "inbound for landing" → Approach). If you start the call with a station name ("Kutaisi Tower, …"), that controller answers. |
| A frequency from `"Frequencies"` in config.jsonc | The controller of that role at your current airfield. Defaults: Ground 264.5, Tower 265.0, Approach 266.5, Range 267.5, AWACS 251.5, Tanker 255.5 (MHz). |
| The frequency of a mission **AWACS** or **carrier** | That AWACS or carrier |
| An **own airfield frequency** from `frequencies.jsonc` | Exactly that controller (a Ground frequency reaches Ground). See [Custom frequencies per airfield](#custom-frequencies-per-airfield). |

Without a matching frequency, a station name in your call can still route it:

- **AWACS:** "Overlord", "Magic", "Darkstar", "Wizard", "Focus", "Moscow", "AWACS", plus "picture", "bogey dope", "request sort", "declare", "spike"
- **Tanker:** "Texaco", "Shell", "Arco", "tanker", "refuel"
- **Range:** "Range …" (only when the mission has a range)
- **Carrier:** "Marshal", "Mother", "Paddles", "ball", "Clara", "pigeons", "carrier" (only when the mission has a carrier)

In some situations you get a correction instead of an answer:

- **Another airfield on this frequency.** If you call another airfield's name on an airfield frequency, you are told to switch:
  > *Enfield one one, this is Kolkhi Tower, you are on Senaki Kolkhi frequency, contact Kutaisi Tower one three four decimal zero.*
- **Airfield request on a station frequency.** If you ask for startup, taxi, takeoff or landing on the AWACS, tanker, range or carrier frequency, you get a text hint in the game ("You are transmitting on Tanker 255.5. Kutaisi Ground: 263.0 / 134.0"). Nobody answers over the radio.
- **Hostile airfields.** Hostile airfields don't answer. You get a text hint with the nearest friendly airfield. A MAYDAY or PAN PAN is still handled by a friendly airfield.

You only hear (and see) what you are tuned to. Controller calls appear as text only for players whose SRS radios are on that frequency. Guard is the exception: everyone gets it.

#### Your callsign

- **Where it comes from:** DCS-ATC takes your callsign from your **DCS slot**, so "Enfield11" becomes **Enfield 1-1**. Controllers say it as "Enfield one one". In a flight they say "Enfield one one flight".
- **Slots without a western callsign:** the first such player gets `"Callsign"` from config.jsonc (default `"Enfield 1-1"`). Other players are addressed by their player name.
- **Your own callsign:** set `"MyCallsign"` (default `""` = take it from DCS), or use the settings window → *Your callsign*. The format is a name and two digits, for example `Viper 1-1`. It replaces the slot callsign of the player at this PC (the host); other players keep theirs.
- **Mishearings are tolerated.** Your callsign is still recognised when the digits match and the word is only slightly off ("Enfeld 1-1" counts as "Enfield 1-1").
- **What you say doesn't change your callsign.** When the mission provides your callsign (or you set one), a different callsign in your speech doesn't rename you. A call that starts with another callsign or a wingman number and doesn't contain yours ("Enfield 1-2, check fuel", "Two, go trail") counts as flight chatter, and the controller ignores it.
- **Who sent a call:** your call is matched to you by your SRS/DCS unit. If that fails, DCS-ATC matches the callsign you speak.
- **Similar callsign:** if an AI aircraft within 30 NM has a callsign with the same word but a different first digit, you get one warning: "Caution similar callsign, Enfield five one is also on this frequency."

#### Phrasing that is always understood

Use standard radio phrasing: **station, callsign, message.**

```
Kutaisi Ground, Enfield 1-1, request startup.
Kutaisi Tower, Enfield 1-1, ready for departure.
Overlord, Enfield 1-1, request picture.
```

These phrases work with every airfield controller:

| You say | Controller |
|---|---|
| "… radio check" / "how do you read" | *"Enfield one one, Kutaisi Tower, read you five."* |
| "say again", "come again", "repeat", "say last", "didn't copy", "missed", "pardon" | Repeats the last instruction: *"Enfield one one, I say again, …"*. Under radar vectors, in the hold or on the way to initial, he gives a fresh heading, altitude and speed from your current position. If there is nothing to repeat: *"…, go ahead."* |
| "confirm …" / "verify …" (e.g. "confirm heading 045", "verify runway 25") | Checks the value against the last instruction: *"affirm, heading zero four five"* or *"negative, heading zero five five"*. A question about a clearance ("confirm cleared to land") repeats the whole call. |
| Station and callsign only ("Kutaisi Tower, Enfield 1-1") | *"Enfield one one, Kutaisi Tower."* You then state your request. Right after a handoff, he goes straight to the instructions. |
| "request weather", "QNH", "wind", "altimeter", "runway in use", "which runway" | *"Enfield one one, Kutaisi Tower, wind two five zero degrees, eight knots, altimeter two niner niner two, runway two five in use."* |
| "unable" (in the air) | *"…, roger, say intentions."* On the ground: *"…, roger, hold position."* |
| "good day", "thanks", "bye" | *"…, good day."* |

Calls the controller doesn't answer:

- **Readbacks get no reply.** As in real radio, silence means your readback was fine. A call counts as a readback when it contains "wilco", "roger", "copy", "cleared", "approved" or "report …", when it repeats the controller's values (hold short, contact, squawk, QNH or altimeter, heading, or just the numbers: "Tower 134.0, Enfield 1-1"), or when it is only your callsign, optionally with "starting up", "taxiing", "lining up", "rolling" or "switching" ("Start up approved, Enfield 1-1", "Enfield 1-1"). A readback of an IFR clearance with the correct squawk gets *"readback correct."*
- **Repeating the same request within 30 seconds after the controller's reply has been spoken counts as a readback.**

If you need an answer, start the call with the station name ("Kutaisi Tower, …") or include "request", "confirm", "verify", "say" or "again".

When the controller can't understand you:

| Situation | Reply |
|---|---|
| Unclear recording (low recognition confidence) | *"Enfield one one, Kutaisi Tower, say again."* The controller does nothing until you repeat the call. |
| Message not understood, callsign heard | *"Enfield one one, say again."* |
| No callsign heard | *"Station calling Kutaisi Tower, say again your callsign."* |

**When a controller is busy:** if a controller has more than about 8 seconds of other calls queued before your answer, you first hear *"Enfield one one, stand by."*, and the answer follows. Emergencies and safety calls (go around, traffic alert, threat) skip the queue.

#### Tips for good recognition

- Press PTT, wait a moment, then speak. Release only after the last word.
- Speak at a normal pace with the microphone close, in a quiet room.
- Start with the station name, then your callsign. The recogniser is primed with your callsign, the station names of the current airfield and the other airfields on the map. Use the station name as DCS gives it ("Kutaisi Tower", "Minvody Approach").
- ICAO numbers are understood: "niner", "tree", "fower", "fife", "decimal". Digit by digit ("two five") and whole numbers ("25") both work. "6,000", "flight level one five zero", "angels 15" and "cherubs 5" are also understood.
- One message per call. Short and standard beats long and creative.
- If a phrase keeps failing, use the radio wheel or the F10 menu. They send the exact text that DCS-ATC expects.

#### Push-to-talk without SRS

If SRS isn't connected, DCS-ATC records from your microphone with its own key:

| Setting | Default | Meaning |
|---|---|---|
| `"PttKey"` | `163` | Virtual-key code. 163 = Right Ctrl, 165 = Right Alt, 161 = Right Shift, 112–123 = F1–F12. Tip: use the same key as your SRS PTT. |
| `"PttJoystick"` / `"PttButton"` | `-1` / `1` | HOTAS button. -1 = off. To find the numbers, run `DcsAtc.exe --buttons` and press the button. |
| `"MicName"` | `""` | Part of the microphone name (for example `"FDUCE"`). Empty = Windows default. Also in the settings window → *Microphone (only without SRS)*. |

Recordings shorter than half a second are ignored. When SRS is connected, this key is ignored so that your call isn't processed twice.

You can set both the wheel key and the PTT key by pressing them: Start menu → **DCS-ATC → Set keys (radio wheel, push-to-talk)**, or the settings window → *Set keys …*. Esc keeps the current key. The new keys take effect the next time DCS-ATC starts.

### Radio wheel

The radio wheel is a ring menu drawn over the DCS window. It sends the exact call text, so recognition can't go wrong. By default a pilot voice also speaks your call on the frequency before the controller answers, so everyone tuned in hears both sides:

> **Enfield 1-1 (pilot voice):** Kutaisi Tower, Enfield 1 1, ready for departure.
> **Kutaisi Tower:** Enfield one one, Kutaisi Tower, …

The wheel is a separate always-on-top window placed over the DCS window. It has not been tested with DCS in exclusive fullscreen. If you can't see it, run DCS windowed or borderless, or switch on the text version in the game (see [Radio wheel as text in the game (VR)](#radio-wheel-as-text-in-the-game-vr), `"WheelInGame": "on"`).

#### Keys

| Key | Action |
|---|---|
| Wheel key: `"WheelKey": 222` (the `'` key on US layouts, `Ä` on German layouts) | Open / close. Works only while DCS is the active window; in other programs the key types normally. `0` = off. |
| `1`–`9` (top row or numpad) | Select the entry |
| `Enter`, `0`, numpad `0` | Send the **suggested call** shown in the centre (ENTER) |
| `Backspace` | Back one level. On the top level it closes the wheel. |
| `Esc` | Close |
| `+` / `-` (main keys or numpad) | Wheel size, in steps of 0.05 between 0.25 and 0.95 of the DCS window height. The new size is saved as `"WheelSize"` (default `0.45`). |
| `Print Screen` | Screenshot of the screen including the wheel, saved to `Pictures\DCS-ATC\DCS-ATC-<date>-<time>.png`. The wheel stays open. |

While the wheel is open, these keys go only to the wheel, not to DCS. If you switch away from DCS (Alt+Tab), the wheel closes.

**HOTAS:** set `"WheelJoystick"` (default `-1` = off) and `"WheelButton"` (default `1`). The button opens and closes the wheel. On the coolie hat, **up/down** moves the highlight, **right** selects and **left** goes back.

#### What you see

- **Top level:** one segment per controller. Below each name you see the frequency for your current airfield: its map frequency for Ground/Tower/Approach (the Departure frequency after takeoff if the airfield has its own), the mission AWACS frequency and the nearest carrier's frequency.
- **Red frequency with ✗:** none of your SRS radios is tuned to that frequency. Tune it, or nobody will hear you.
- **Centre:** the name of the airfield (or carrier) the wheel is talking to. When there is a suggestion, you also see an amber **ENTER** label, the controller's name and the suggested call in large amber text.
- **Highlight:** when the wheel opens, the controller of the suggestion is highlighted; inside a submenu, the suggested entry is highlighted.
- **Which airfield the wheel talks to:** the airfield whose frequency you have tuned in SRS (for example 260.0 → Batumi). Otherwise the airfield you are already working with, otherwise the nearest one. General → *Select airfield* overrides this until you land.

#### ENTER: the expected answer

ENTER always offers the call you are expected to make next.

**1. The answer to the controller's last question.** When a controller asks you something or asks you to report something, ENTER offers the answer. A question stays open for 90 seconds; a "report …" request stays open for 5 minutes, because you report when you get there. As soon as you make any call yourself, the open question is cleared. In a flight, wingmen also get their lead's open question.

| Controller says | ENTER sends (example) |
|---|---|
| "say airspeed" | `Approach: 250 knots` (your indicated airspeed, rounded to 10 kt) |
| "say altitude" | `3400 feet` |
| "say heading" / "verify heading" | `heading 275` |
| "say position" | `12 miles west of Kutaisi` |
| "say intentions" | In the air: `full stop`; under IFR: `request vectors to Kutaisi, full stop`. On the ground: your next normal call. |
| "say state" (carrier aircraft) | `state 5.4` |
| "say needles" | For example `down and left` |
| "say again." (the controller didn't understand you) | Your last request again |
| "call the ball" | `Hornet ball, 5.4` |
| "report initial …", "report overhead", "report in hot or checking out" … | `initial`, `overhead`, `in hot` … If several reports are offered, the first one is used. If the next step of your flight (see 2.) contains the report, ENTER sends that full call instead, for example `four mile final, gear down`. |
| "report base" / "report final" / "report four miles final" | `base, gear down` / `final, gear down` / `four mile final, gear down`. Without "gear down", Tower would answer "check wheels down". |
| "report C R P north" | `Approach: C R P north` |
| "report high key" / "report low key" | `high key` / `low key, gear down` |
| "I show you going around, confirm? … report base." | `Tower: going around` (the base call follows from the normal flow afterwards) |
| "report leaving the control zone" / "report clear of the zone" | `leaving the control zone` / `clear of the zone` |
| "contact Kutaisi Approach now" (or Tower / Departure) | The check-in you missed, from the normal flow, for example `airborne, climbing`, `missed approach` or `inbound` |
| "cleared pre-contact" | `Tanker: pre contact` |
| "report visual" (tanker) | `Tanker: visual` |
| "report commencing" | `Carrier: commencing, angels 6, state 5.4` |
| "report see me" (Case I) | `Carrier: see you at angels 2` |
| "update state" (Case I, Marshal) | `Carrier: state 5.4` |
| "report see you at ten" (Case II) | `Carrier: see you at ten` |
| "report off" / "Report IP." (range) | `Range: off` / `Range: IP inbound` |

If the question came with a handover ("contact Kutaisi Tower …, report initial for overhead break"), ENTER sends the answer to the new controller. After the handover to an airfield's own Departure frequency, the suggestion is labelled `Departure: …`, and ENTER sends it to Departure.

**2. Otherwise, the next step of your flight:**

- Parked: `request startup` (in IFR weather: `request IFR clearance`), then `request taxi`
- At the holding point: `ready for departure`
- After takeoff: `airborne, climbing`; still inside the zone after "report leaving the control zone": `leaving the control zone`
- Inbound: `inbound for landing`; near the CRP (Kutaisi): `C R P`; after the handoff to Tower: established on a long final (up to 12 NM, on the centreline) `9 mile final, gear down, full stop` (with your distance; Tower answers with the landing clearance, or "number 2, …" with traffic ahead); otherwise `inbound` while still far out, then `initial`, `overhead` or `four mile final, gear down`. Approach's handoff to Tower on a straight-in no longer contains a reporting point ("contact Kutaisi Tower."), Tower gives it; a "final" call on the extended centreline within 12 NM counts as final
- In the pattern: `base, gear down`, then on final `final, gear down, full stop` (or `final, touch and go` during pattern work)
- After a missed approach: `missed approach`
- After landing: `runway vacated, request taxi to parking`
- At the carrier: `Marshal, checking in`, then `see you at angels` (Case I) or `commencing` (Case II/III), `platform`, `see you at ten` (Case II, within 12 NM), `ball` (after "call the ball")

There is no suggestion while you hold a takeoff or landing clearance, because "request startup" or "inbound" at that point would cancel the clearance.

This follows the mandatory position reports (see [Mandatory reports](#mandatory-reports)): you report every position yourself, and ENTER offers that report at the right moment. If you don't report, the controller first asks for it; if you still don't, the real consequence follows: a go-around, a wave-off, or no clearance.

#### All wheel entries

The wheel shows only the entries that fit your situation right now, so "Report airspeed" appears only after "say airspeed" and "Airborne" only just after takeoff. The numbers 1–9 count the entries you see. The "Shown" column below says when an entry appears. If a controller has no fitting entry, it is not on the top level (for example Ground in the air, Tower after landing). Without a flight state (not in an aircraft yet, preview) the wheel shows everything. General, Emergency and Select airfield are always shown.

Whole controllers are also hidden when they don't apply:

- **Range:** only when the mission has a range.
- **AWACS:** only when an AWACS of your side is airborne or your side has a GCI radar.
- **Tanker:** only when a tanker of your side is airborne that your aircraft can refuel from.
- **Carrier:** only when the mission has a carrier.

Each level shows at most 9 entries.

**Ground**

| Entry | Sends | Shown |
|---|---|---|
| Request startup | `request startup` | On the ground, parked |
| IFR clearance → *Next in departure direction* / *airfield …* | `request IFR clearance` / `request IFR clearance to <airfield>` | On the ground before takeoff |
| Request taxi | `request taxi` | On the ground before the holding point |
| Vacated, taxi to parking | `runway vacated, request taxi to parking` | On the ground after landing |
| Progressive taxi | `request progressive taxi` | On the ground |
| Hot brakes | `hot brakes` | On the ground |

The IFR clearance list shows nearby airfields; carriers and your current airfield are not listed.

**Tower**

| Entry | Sends | Shown |
|---|---|---|
| Ready for departure | `ready for departure` | On the ground before takeoff |
| Ready, closed pattern | `ready for departure, closed pattern` | On the ground before takeoff |
| Initial | `initial` | In the air |
| Overhead | `overhead` | In the air |
| Base, gear down | `base, gear down` (Enter suggestion adds your intention: `base, gear down, full stop` / `touch and go`) | In the air |
| Final, gear down, full stop | `final, gear down, full stop` | In the air |
| Base, touch and go | `base, gear down, touch and go` | In the air |
| Going around | `going around` | In the air |
| Closed / SFO → *Request closed / Request SFO* | `request closed traffic` / `request S F O` | In the air |
| Closed / SFO → *High key / Low key, gear down* | `high key` / `low key, gear down` | After "request S F O" (or when asked for it) |
| Closed / SFO → *Ready, practice approach* | `ready for departure, practice approach` | On the ground before takeoff |

**Approach**

| Entry | Sends | Shown |
|---|---|---|
| Airborne (after takeoff) | `airborne, climbing` | Just after takeoff, until you have checked in |
| Inbound for landing | `inbound for landing` | In the air |
| Inbound, pattern work | `inbound for pattern work, touch and go` | In the air |
| Request ILS / straight in | `request straight in` | In the air |
| Flight following | `request flight following` | In the air |
| Cancel approach | `cancel approach` | In the air, not before the check-in after takeoff |
| Traffic → Traffic in sight | `traffic in sight` | Up to 2 minutes after a traffic advisory to you |
| Traffic → Negative contact | `negative contact` | Up to 2 minutes after a traffic advisory to you |
| C R P | `C R P` | After "report C R P", or near the CRP on a CRP arrival |
| Report airspeed | Your IAS rounded to 10 kt, for example `250 knots` | After "say airspeed" |
| Say again | `say again` (to Approach) | Always, when there is room (max. 9) |
| Request higher | `request higher` | In the air, when there is room (max. 9) |

**Range:** Check in (`checking in`) in the air until you are checked in; then IP inbound, In hot, Off safe, Check out (`checking out`), Check out, hung ordnance (`checking out, hung ordnance`).

**AWACS**

| Entry | Sends | Shown |
|---|---|---|
| Check in | `checking in` | In the air, not checked in |
| Picture | `request picture` | In the air |
| Bogey dope | `bogey dope` | In the air |
| Sort | `request sort` | In the air |
| Nearest tanker | `vector to tanker` (the answer names the tanker and its frequency) | In the air |
| Vector nearest airfield | `vector to nearest airfield` | In the air |
| Check out | `checking out` | Checked in |

**Tanker:** Request rejoin (in the air, before the rejoin), Visual (during the rejoin), Observation (during the rejoin and in observation), Pre-contact (`pre contact`) and Refuel complete (after "cleared to join").

**Carrier:** Marshal check in (in the air, DCS-ATC fills in your full check-in) and Pigeons (in the air); after the Marshal check-in: See you at (`see you at angels`), Initial, Commencing, Platform, Ball, Clara. Details in [Carrier operations](#carrier-operations).

**Emergency**

| Entry | Sends |
|---|---|
| MAYDAY → *Engine failure / Fuel / Hydraulic failure / Battle damage / Medical / Bird strike* | `mayday mayday mayday, <kind>, request immediate landing` (Fuel: `mayday mayday mayday fuel, emergency fuel, request immediate landing`) |
| PAN PAN → *the same kinds without Fuel* | `pan pan, pan pan, pan pan, <kind>, request priority landing` |
| Minimum fuel | `minimum fuel` (no emergency, no priority) |
| Hung ordnance | `hung ordnance` (to Approach) |
| Flameout, high key | `flameout, high key` (to Tower; real flameout, counts as an emergency; then Enter: `low key, gear down`) |
| Cancel emergency | `cancel emergency` |

The pilot voice adds your position, altitude and heading, and DCS-ATC picks the nearest suitable friendly airfield (with a runway of at least 1800 m if possible):

> *MAYDAY MAYDAY MAYDAY, Kutaisi Approach, Enfield 1 1, engine failure, 20 miles west of Kutaisi, 6000 feet, heading 090, request immediate landing.*

**General**

| Entry | Does |
|---|---|
| Select airfield | Lists the nearest airfields and carriers. The wheel then talks to the one you pick until you land (text: "ATC: now Batumi"). Picking a carrier puts you with its Marshal. Hostile airfields are refused with a text hint. |
| Radio check | `radio check` |
| Say again | `say again` |
| QNH / weather | `request weather` |
| Listen to ATIS | Reads the current ATIS of your airfield. If the mission has no weather data, you get a text hint instead. |
| Request zone transit | `request zone transit` (to Approach) |
| Debriefing | Shows your debriefing so far as text in the game |
| Settings | Opens the settings window |

#### Radio wheel in multiplayer (clients)

Other players who have DCS-ATC installed also get the wheel (same key). Their requests go to the host through the DCS chat, and nobody sees those chat lines. This also works on a dedicated server. The client wheel has no ENTER suggestion, shows no frequencies (only the host knows the airfield and `frequencies.jsonc` frequencies), has no Range or Carrier entries and no in-game VR text. It doesn't know your flight state either, so it shows all entries as before (Approach without Say again and Request higher, max. 9). See [Client mode](#client-mode-other-players-with-their-own-dcs-atc).

### Radio wheel as text in the game (VR)

A VR headset only shows DCS, so you can't see the overlay. DCS-ATC can mirror the wheel as text in the top area of the game screen, visible only to your aircraft:

```
ENTER ▸ Tower: ready for departure
DCS-ATC – Tower
   1 Ready for departure
> 2 Ready, closed pattern
   3 Initial
   ...
```

- The first line is the ENTER suggestion.
- `>` marks the highlighted entry. On the top level, the frequencies are shown next to the entries.
- The text updates with every key press and disappears when you close the wheel.
- You operate it with the same keys or HOTAS controls as the normal wheel. For VR, bind the wheel key and the hat to your HOTAS.

| Setting | Default | Values |
|---|---|---|
| `"WheelInGame"` | `"auto"` | `"auto"` = when DCS runs in VR mode (VR enabled in the DCS options or the `--force_enable_VR` start parameter), `"on"` = always, `"off"` |

The same choice is in the settings window → *Radio wheel in game (VR)*: automatic / always / off.

### F10 menu

In every mission with DCS-ATC, each player group gets **F10 Other → ATC**. If several players share a group, each of them gets their own submenu named after their callsign. The menu language follows your installer choice (German or English).

| Submenu | Entries |
|---|---|
| Ground | Request startup · Request taxi · Runway vacated, taxi to parking · Progressive taxi · Hot brakes · IFR clearance → *Next in departure direction* / up to 8 friendly or neutral airfields, nearest first |
| Tower | Ready for departure · Ready, closed pattern · Initial · Overhead · Base, gear down · Final, gear down, full stop · Base, touch and go · Going around · Request closed |
| Approach | Airborne (after takeoff) · Inbound for landing · Inbound, pattern work · Request straight in · C R P |
| AWACS / Tanker | AWACS: picture · AWACS: bogey dope · AWACS: nearest tanker · Tanker: request rejoin · Tanker: pre-contact |
| Emergency | MAYDAY → kind · PAN PAN → kind · Minimum fuel · Hung ordnance · Flameout, high key · Cancel emergency (kinds: engine failure, fuel (MAYDAY only), hydraulic failure, battle damage, medical, bird strike) |
| General | Radio check · Say again · QNH / weather · Listen to ATIS · Request zone transit · Debriefing |
| Settings | Ground crew on/off · Show |

F10 calls are handled like wheel calls, including the pilot voice and the choice of airfield. The F10 menu has fewer entries than the wheel: there is no Range, no Carrier, no AWACS check-in/out or sort, and no flight following. Use the wheel or your voice for those.

If DCS-ATC isn't running, you get the message *"DCS-ATC is not running – Start menu -> Start DCS-ATC, then send again."*

### Typing in the chat (multiplayer)

In a multiplayer mission, a chat message that starts with `ATC>` goes to DCS-ATC on the host instead of the chat. You can use the same texts as the wheel:

```
ATC>Ground: request taxi
ATC>Tower: ready for departure
ATC>radio check
ATC>debrief
```

The part before the colon (`Ground:`, `Tower:`, `Approach:`, `AWACS:`, `Tanker:`, `Range:`, `Carrier:`) picks the controller. Without it, the text itself decides, as with voice. The chat is meant for the other players; on the host PC use the wheel or the F10 menu. Typing `ATC>` in the chat as the host has not been tested.

### Text in the game

Every controller call also appears as DCS text:

```
>>> Kutaisi Tower (263.0): Enfield one one, Kutaisi Tower, runway two five, cleared for takeoff, wind calm.
Enfield 1-1: Kutaisi Tower, Enfield 1 1, ready for departure.
```

- **Format:** station and frequency first. `>>>` marks calls addressed to your own group. Your own pilot-voice calls appear with your callsign.
- **Who sees it:** only groups with a player tuned to that frequency in SRS. If SRS doesn't know your radios, you see everything. Guard reaches everyone.
- **How long it stays:** at least 20 seconds, longer for long texts. AI flight chatter stays only 6–8 seconds.
- **Hints are text only, never spoken:** wrong frequency, hostile airfield, "ATC: now …", debriefing, refuelling via the DCS ground crew, and similar.
- **Late calls become text only:** if a call would come too late (more than 45 seconds in the queue, or 20 seconds for an AWACS call; 12 seconds for AWACS threat, merged, leaker and furball calls), you only see it as text.
- **Order on a busy frequency:** emergencies and *defending* first, then the short combat calls of the flights (*fox*, *pitbull*, *splash* …), then AWACS *threat*/*merged*/*leaker*/*furball*/*pop-up*, then *commit*/*targeting* and answers, then picture, new group, faded and on-station calls, then other AI chatter. A long picture waiting in the queue doesn't hold up a *pitbull* any more.

### Voices, radio effect and speech pace

Voices are Piper models in the `models` folder of the installation (`%LOCALAPPDATA%\Programs\DCS-ATC\models`). You can change them in config.jsonc:

```jsonc
"Voices": {
  "Tower": "en_US-ryan-medium|en_US-ljspeech-medium",
  "Ground": "en_US-bryce-medium",
  "Approach": "en_US-ryan-medium@1.05",
  "ATIS": "en_US-ryan-medium@0.94",
  "Range": "en_US-ryan-medium@1.06",
  "AWACS": "en_GB-alan-medium|en_US-ljspeech-medium",
  "Tanker": "en_US-bryce-medium",
  "Crew": "en_US-ryan-medium"
},
"PilotVoice": "en_US-joe-medium"
```

How the voice values work:

- **`@` sets the pitch:** `@0.88` = lower, `@1.05` = higher.
- **`|` lists several voices:** each station (for example "Kolkhi Tower") keeps one of them, so neighbouring airfields sound different.
- **Missing voices:** if a voice file is missing, an available voice is used instead (otherwise `en_US-ryan-medium`). More voices: huggingface.co/rhasspy/piper-voices.
- **Crew:** the crew chief at your parking spot, who speaks on your intercom.
- **Carrier:** not in the template; the default `"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02"` is added automatically.
- **`PilotVoice`:** speaks your wheel, F10 and chat calls before the answer. Each player gets a slightly different pitch. `""` = off, which is the same as unticking the settings window → *Speak your wheel requests (pilot voice)*.

| Setting | Default | Meaning |
|---|---|---|
| `"RadioFx"` | `true` | Radio sound: band-pass, quiet static, squelch tail. In combat, calls are faster and sound like an overdriven mask microphone. |
| `"Volume"` | `0.6` | Controller volume in SRS (0–1). Lower it if the voices are too loud. |
| `"SpeechRate"` | `0.85` | Speech pace: 1 = Piper standard, lower = faster, higher = slower. The slider in the settings window ranges from 0.70 to 1.50. All voices are balanced to the same pace. |
| `"ReplyPause"` | `1.5` | Seconds of pause between two calls on one frequency (pilot → controller → readback). Slider in the settings window: 0–5 s. |

Volume, pace, pause, radio effect and pilot voice take effect immediately when you save in the settings window (Start menu → **DCS-ATC → Settings**).

### Altimeter setting: hPa or inHg

Controllers give the altimeter setting in the unit your aircraft uses:

- **inHg:** *"altimeter two niner niner two"* for western types: F-14, F/A-18, F-16, F-15, F-4E, F-5, F-86, F-117, A-10, AV-8B, A-4, A-6, T-45, S-3, E-2, E-3, C-130, C-17, KC-135, B-1, B-52, AH-64, UH-1, UH-60, OH-58, CH-47, P-51/TF-51, P-47, F4U, Christen Eagle.
- **hPa:** *"QNH one zero one three"* for every other aircraft (MiG, Su, Mirage, Viggen, JF-17, Ka, Mi, British and German warbirds …).
- **ATIS** goes to everyone, so it gives both: *"QNH one zero one three, altimeter two niner niner two"*.

| Setting | Default | Values |
|---|---|---|
| `"AltimeterUnit"` | `"auto"` | `"auto"` (by aircraft type; ATIS gives both), `"hpa"`, `"inhg"` (all calls and the ATIS in that unit only) |

The same choice is in the settings window → *Altimeter setting*: automatic (by aircraft type) / hPa (QNH) / inHg (altimeter).

### Runway lights

DCS switches on an airfield's lights only after someone contacts its built-in DCS controller. DCS-ATC does this for you in the background by briefly operating the DCS radio menu. You don't need to call the DCS controller yourself.

**When it happens:**

- You are the mission host (or flying single player) and you are working with an airfield in DCS-ATC.
- You are within 20 NM of that airfield and either inbound (approach, initial, pattern, cleared to land) or on the ground taxiing out or holding short.
- It is dusk or night (sun lower than 6° above the horizon), or the weather is IFR.
- It happens at most once every 15 minutes per airfield and situation.

**When it doesn't happen:**

- A DCS controller dialog is already open, for example after a hot start on the runway.
- DCS "Easy Communication" is off and none of your radios is tuned to the airfield's map frequency (UHF or VHF).
- The airfield isn't among the 10 entries that the DCS menu lists.

| Setting | Default | Values |
|---|---|---|
| `"RunwayLights"` | `"silent"` | `"silent"` = lights on, the DCS controller stays quiet. `"dcs"` = the DCS controller's answer is audible for 15 seconds. `"off"` = never. |
| `"CommsKey"` | `43` | DCS radio-menu key as a keyboard scancode (43 = `\` on US layouts, `#` on German). `0` = off. This key also operates the DCS tanker menu. |

The runway-lights choice is also in the settings window → *Runway lights (at night)*: silent / with DCS controller (audible) / off. It takes effect the next time DCS-ATC starts.

## Airfield ATC

DCS-ATC provides ATIS, Ground, Tower, Approach and Departure at every airfield on the map. It uses only units that are already in the mission and never spawns aircraft. This chapter covers airfield operations from start-up to parking.

### Airfields, callsigns and frequencies

**Which airfields have ATC**

- **Every map, every airfield with a runway.** Runways, elevation and parking spots come live from DCS. Kutaisi also uses the DCS kneeboard charts: CRPs, taxiway names, ramps, and a traffic pattern that is always south of the runway. Every other airfield uses a generic procedure built from the terrain.
- **Only friendly and neutral airfields answer.** An enemy airfield stays silent. Instead you get a text note with the nearest friendly airfield as magnetic bearing and distance, for example `Senaki-Kolkhi 270/35 NM`. If an airfield is captured while you are inbound, Approach sends you to the nearest friendly airfield ("Kutaisi is closed, divert Senaki …").
- **Station names** come from the map's radio data. For example, Senaki-Kolkhi is "Kolkhi Tower" and Kutaisi is "Kutaisi Tower". Hyphens in airfield names are spoken as spaces.

**Frequencies**

- **Each airfield has its own frequency.** DCS-ATC reads the UHF and VHF frequencies of every airfield from the DCS map. Ground, Tower and Approach all share these frequencies, as they do in DCS. Airfields that have only a VHF frequency in DCS (most of the Persian Gulf map, for example Abu Dhabi Intl 119.2) use that one. Airfields without any DCS frequency use the controller frequencies from `config.jsonc` (see below).
  - Because they share a frequency, a handoff names no new frequency: "contact Kutaisi Tower".
  - If you call the wrong controller, the right one answers on the same frequency.
- **ATIS** runs on its own frequency, 263.5 by default (see [ATIS](#atis)).

| Controller | Frequency | Handles |
|---|---|---|
| ATIS | `Frequencies.ATIS` (263.5), or per airfield | Runway, wind, visibility, clouds, temperature, QNH and altimeter, information letter. Continuous broadcast |
| Ground | Airfield frequency | Start-up, IFR clearance, taxi out, taxi to parking |
| Tower | Airfield frequency | Line-up, takeoff, pattern, landing, zone guard |
| Approach | Airfield frequency | Radar vectors, holding, instrument approaches, departure check-in, flight following, zone transit |
| Departure | Same as Approach, unless set separately | Approach after the Tower handoff (see below) |

**When the map has no frequency data**

If DCS-ATC cannot find your DCS installation or the map's radio data, it falls back to the controller frequencies in `config.jsonc`:

```jsonc
"Frequencies": { "ATIS": 263.5, "Ground": 264.5, "Tower": 265.0, "Approach": 266.5, ... }
```

These frequencies are the same for every airfield. Handoffs then name the frequency: "contact Kutaisi Tower two six five decimal zero".

**Your own frequencies per airfield**

In `frequencies.jsonc` you can give an airfield its own frequency for each controller (ATIS, Ground, Tower, Departure, Approach), for example real-world frequencies. A call on one of these frequencies always goes to that controller, whatever you say. On the map frequency, the content of your call still decides. With a `Departure` entry, Tower hands you off as "contact Nellis Departure two seven three decimal five five". Changes apply as soon as you save. For the format and all rules, see [Custom frequencies per airfield](#custom-frequencies-per-airfield).

**Which airfield you are talking to**

DCS-ATC picks the airfield in this order:

1. The airfield you name: "Batumi Tower, Enfield 1-1, …". A name after "contact …" doesn't count.
2. The airfield you chose in the radio wheel under **General → Select airfield**. This choice holds until you land.
3. The airfield whose frequency your SRS radio is tuned to. An airfield you have just tuned takes priority.
4. The airfield you are already working with. After departure this holds until you are signed off, up to 25 NM.
5. The nearest friendly or neutral airfield.

Over SRS, DCS-ATC listens on the frequencies of your current airfield and of the two nearest ones.

**Calling the wrong station**

- **Different frequency** (only when the controllers have separate frequencies, see above): the station tells you where to go. "Enfield one one, Kutaisi Tower, contact Kutaisi Ground two six four decimal five."
- **Another airfield on this frequency:** "Enfield one one, this is Kolkhi Tower, you are on Senaki Kolkhi frequency, contact Kutaisi Tower one three four decimal zero."

### Radio basics

- **Format:** station, your callsign, message. "Kutaisi Ground, Enfield 1-1, request startup."
- **Your callsign** comes from your DCS slot, or from `MyCallsign` in the settings.
- **Formation flights:** players in the same group who fly in formation are answered as a flight ("Enfield one one flight"). Clearances apply to the whole flight.
- **Readbacks, "say again", "confirm", radio checks and "unable"** work the same with every controller: see [Phrasing that is always understood](#phrasing-that-is-always-understood).
- **How to transmit** (SRS, push-to-talk, radio wheel, F10, chat): see [Ways to talk](#ways-to-talk).

### ATIS

The ATIS broadcasts continuously for every airfield that has a player within 40 NM. If several airfields share an ATIS frequency, they take turns. The weather comes from the mission.

> **ATIS:** Kutaisi information Alpha, time zero eight zero zero Zulu. Runway two five in use, left hand pattern, 2000 feet. Wind two four zero degrees, eight knots. Visibility one zero kilometers or more, sky clear. Temperature one five. QNH one zero one three, altimeter two niner niner two. Tower one three four decimal zero, UHF two six three decimal zero. Advise on initial contact you have information Alpha.

- **Clouds** are given as the cloud base above the airfield ("clouds 2500 feet"). **Wind** is magnetic, and "wind calm" below 3 kt.
- **Altimeter setting:** the ATIS gives both QNH (hPa) and altimeter (inHg). Controllers give the one that matches your aircraft (see [Altimeter setting](#altimeter-setting-hpa-or-inhg)).
- **Instrument weather:** the ATIS adds "Instrument conditions, expect vectors for ILS approach runway two five, localizer …". Without an ILS it says "P A R approach". If terrain requires it, it may give the opposite runway or a circling approach.
- **New letter:** the letter changes when the runway, the wind, the QNH or the visibility changes by a step, or on the full mission hour. Everyone at the airfield (on the ground or within 20 NM) hears: "Attention all aircraft, Kutaisi information Bravo now current, QNH …".
- **Reporting the ATIS:** say the letter on first contact ("information Alpha", "with Alpha", "have Alpha"). If you don't, or you give an old letter, the controller adds "Information Bravo is current" plus the runway and the QNH.
- **On demand:** wheel/F10 **General → Listen to ATIS** plays the ATIS at once.
- **Switching off:** `"Atis": false` turns the ATIS off.

### Runway selection

- **Wind up to 3.5 m/s (about 7 kt):** DCS's main runway is in use. If AI aircraft are seen using the other direction in light wind, DCS-ATC follows them.
- **Stronger wind:** the runway with the most headwind is used. This matches where DCS lands its AI.
- **Parallel runways** are named L/R/C by their position, for example 30L/30R. Landings go to the main runway's side.
- **When the runway changes:** the active runway changes only while you are parked or not working the airfield. A procedure in progress keeps its runway.
- **Asking for a runway:** say "request runway 07" or "runway 30 left". On the ground, add it to your start-up, taxi or clearance request; in the air, ask while inbound. You get it if the tailwind is 10 kt or less:
  - Ground: "runway zero seven approved, …"
  - In the air: "runway zero seven approved", and Approach replans your vectors.
  - More tailwind: "Unable runway zero seven, tailwind one two knots, runway two five in use."
  - A runway request for departure doesn't carry over to your landing.
- **Pattern direction:**
  - Kutaisi: always south of the runway, so runway 07 has a right-hand pattern and runway 25 a left-hand pattern.
  - Elsewhere: left-hand, unless the terrain on the left is more than 300 ft higher than on the right.
  - If the terrain doesn't allow a pattern at all (for example at Khasab), you only get straight-in approaches.

### Weather: VFR and IFR

**IFR conditions**

The airfield is **IFR** when the mission visibility is below 5 km or the cloud base is below 600 m (about 2,000 ft) above sea level. Under IFR:

- There is no initial or overhead. "initial" gets "negative, field is I F R. Expect vectors for ILS approach runway two five, localizer …, report inbound to Kutaisi Approach." "overhead" gets "negative, field is I F R, no overhead."
- Approach vectors you onto an **ILS** if the runway has one, otherwise onto a **PAR** approach with glide-path calls.
- "cancel IFR", "VFR" and "request visual approach" are refused.
- Touch-and-go and low-approach practice become radar-vectored instrument circuits.

**Bad weather on first contact**

If the visibility is below 5 km or the ceiling is below 1,000 ft, the controller adds this to the first contact: "Visibility 3 kilometers, ceiling 800 feet." Below 550 m visibility or a 200 ft ceiling, the controller also adds "Say intentions."

**Straight-in by default**

At **night** (sun more than 6° below the horizon), and for types that don't fly an overhead break, "inbound" gets a straight-in or instrument approach. These types are helicopters, C-130/C-17, C-47, An-/Il-/Tu-, B-1/B-52, KC tankers, E-2/E-3 and S-3. Say "overhead", "VFR" or "cancel IFR" to get the overhead break anyway. ("request visual approach" gives you a visual straight-in approach.)

### Ground

All Ground requests work only within 5 km of the airfield. Farther away you get a note to call Approach after takeoff.

#### Start-up

> **You:** Kutaisi Ground, Enfield 1-1, request startup, information Alpha.  
> **Ground:** Enfield one one, Kutaisi Ground, start up approved. Report ready to taxi.

On the first call, if you don't give the current ATIS letter, Ground adds: "Information Alpha is current, runway two five in use, QNH one zero one three."

These phrases count as a start-up request: "request startup", "start up", "request engine start", "ready to start". "Engines started" does not.

#### IFR clearance

> **You:** Kutaisi Ground, Enfield 1-1, request IFR clearance to Batumi.  
> **Ground:** Enfield one one, Kutaisi Ground, cleared to Batumi airport via radar vectors, after departure fly runway heading, climb and maintain 3000 feet, expect flight level one five zero ten minutes after departure, departure Kutaisi Approach, squawk four two one six.  
> **You:** Cleared to Batumi, runway heading, 3000 feet, squawk 4216, Enfield 1-1.  
> **Ground:** Enfield one one, readback correct.

- **Clearance limit:** without a destination, the clearance limit is the nearest friendly airfield in the departure direction. The F10 menu **Ground → IFR clearance** lists airfields by distance.
- **Cruise level** follows the semicircular rule: FL150 eastbound, 14,000 ft westbound. A destination closer than 40 NM gets a low level. The level is never less than terrain plus 2,000 ft.
- **Enemy destination:** "unable clearance to …, say alternate destination."
- **Readback check:** only the squawk code in your readback is checked ("squawk 4216").
- **Own Departure frequency:** if the airfield has one in `frequencies.jsonc`, the clearance says "departure frequency two seven three decimal five five" instead of "departure Kutaisi Approach".
- **In the air:** "request IFR to Senaki" gets a pop-up IFR clearance from Approach.

#### Taxi out

> **You:** Kutaisi Ground, Enfield 1-1, request taxi, departure west.  
> **Ground:** Enfield one one, Kutaisi Ground, taxi to holding point runway two five via November, Delta. Squawk four two one six. Hold short runway two five, report ready for departure.

- **Route:** only Kutaisi gives taxiway names. Other airfields say "taxi to holding point runway two five".
- **QNH:** given only on the first call, and only if you didn't give the ATIS letter. "request startup and taxi" answers both requests at once.
- **Squawk:** a VFR squawk comes with the taxi clearance. If you have an IFR clearance, you already have a squawk.
- **Separate Tower frequency:** "Hold short runway two five, contact Kutaisi Tower … when ready for departure."
- **Progressive taxi:** while taxiing, "request progressive taxi" gets directions to the holding point, such as "holding point runway two five at your 2 o'clock, 450 meters". The directions are updated each time the distance halves and end with "…, hold short of runway two five".

**Ground warnings while you taxi**

| Situation | Ground or Tower says |
|---|---|
| Rolling from your spot without a taxi clearance (faster than 5 kt, more than 100 m) | "hold position, say intentions." |
| Faster than 30 kt on a taxiway | "reduce taxi speed." |
| Crossing or oncoming traffic you must give way to (right before left; you always give way to AI) | "hold position, give way to the Hornet on your right." … later: "continue taxi." |
| Traffic ahead in the same direction | "follow the Viper ahead." |
| "ready" called before you have a taxi clearance | "negative, you are not cleared to taxi. Request taxi." |
| "ready" called while still taxiing | "negative, you are not at the holding point. Continue taxi to holding point runway two five …, report ready for departure." |

#### After landing: taxi to parking

> **Tower:** Enfield one one, welcome to Kutaisi. Vacate right via Alpha or Bravo. Report vacated.  
> **You:** Kutaisi Ground, Enfield 1-1, runway vacated, request taxi to parking.  
> **Ground:** Enfield one one, taxi to Ramp North via November, parking spot one two, at your 2 o'clock, 350 meters.

- **Exit instruction:** at airfields without a chart, "Vacate runway when able". With a separate Ground frequency: "Contact Kutaisi Ground … when vacated."
- **Traffic behind you:** if there is traffic within 3 NM on final behind you, Tower adds "Expedite vacating, traffic F-16 on 2 mile final."
- **Asking too early:** asking for taxi while still on the runway gets "report runway vacated."
- **No call:** if you don't call within 30 s of leaving the runway, Tower reminds you once: "contact Kutaisi Ground."
- **Parking spot:** you get your own spot if it is free, otherwise the nearest free one. Spots already assigned to other players are skipped. Ground gives the direction once, and again only if you drift away (or each time the distance halves with "progressive").
- **On your spot:** "you are on your parking position, shut down at your discretion."
- **When you count as parked:** after 30 s stopped on the apron, or when you shut down your engines at a spot. This ends the flight and writes the debriefing.

### Tower: departure

#### Ready for departure

> **You:** Kutaisi Tower, Enfield 1-1, holding short runway 25, ready for departure.  
> **Tower:** Enfield one one, after departure exit via C R P west, not above 2200 feet until leaving the control zone, runway two five, wind two four zero degrees, eight knots, cleared for takeoff.

You can say "ready", "ready for departure", "holding short" or "holding point". You must be within 700 m of the threshold, or on a runway.

**Takeoff clearance variants**

| Your plan | After-departure part |
|---|---|
| VFR, Kutaisi | "exit via C R P west, not above 2200 feet until leaving the control zone". The CRP is the direction you gave; by default runway 25 → west, 07 → east |
| VFR with direction, other airfields ("request taxi, departure north") | "leave the control zone northbound, not above …" |
| VFR without direction, other airfields | "own navigation, not above … until leaving the control zone" |
| IFR clearance | "fly runway heading, climb and maintain 3000 feet" |
| Closed pattern ("ready for departure, closed pattern") | "runway two five, wind …, cleared for takeoff, left closed traffic approved, report base." |
| Practice approaches in instrument conditions ("ready for departure, practice approach") | "after departure fly runway heading, climb and maintain 3000 feet, …, cleared for takeoff", after liftoff "contact Approach", after "airborne, climbing" radar vectors. A plain "closed pattern": "closed traffic not approved, I F R conditions." |

**When Tower holds you**

| Situation | Tower says | What happens next |
|---|---|---|
| Landing traffic within 4 NM | "hold short runway two five, traffic Hornet on 3 mile final." | Tower clears you **automatically** once the traffic is beyond 4 NM, or after 3 min if it is beyond 2.5 NM |
| Aircraft rolling on the runway, nothing on final | "runway two five, line up and wait, traffic on the runway." | Automatic clearance when the runway is clear. With traffic on a 2.5–6 NM final: "cleared for immediate takeoff, traffic …" |
| Opposite-direction landing traffic | "hold short runway two five, opposite direction traffic, … runway zero seven." | Automatic clearance when clear |
| Another aircraft already cleared or lined up | "hold short runway two five, number 2 for departure." | Automatic clearance when clear |
| MAYDAY traffic within 15 NM | "hold short runway two five, emergency traffic inbound." | Automatic clearance when clear |

You don't need to call again. Tower calls you.

**Things that cancel or stop a takeoff**

| Situation | Tower says |
|---|---|
| Before your takeoff roll (below 30 kt): traffic enters the runway, a landing aircraft is inside 1.5 NM, or opposite traffic is inside 4 NM | "hold position, cancel takeoff clearance, traffic on the runway." |
| You taxi away from the holding point for 30 s | "takeoff clearance cancelled, contact Kutaisi Ground." |
| You say "abort", "rejecting", "stopping" or "cancel takeoff" | "roger. Vacate … Report ready for departure." |
| You enter a runway without clearance | "hold position! You are not cleared onto runway two five." (deviation) |
| Takeoff roll without clearance (above 30 kt; above 40 kt after "line up and wait") | "stop immediately, I say again, stop immediately! You are not cleared for takeoff." (deviation) |
| Airborne without clearance | "you departed without takeoff clearance. Report intentions." (deviation) |

**Spawning on the runway**

If you spawn on the runway, you are treated as lined up and waiting. Call "ready for departure" to get your clearance; Tower never clears you without that call, however long you wait. If you start the takeoff roll without a clearance, you get "stop immediately, … You are not cleared for takeoff." above 40 kt, and a deviation (see [Mandatory reports](#mandatory-reports)).

**Wake turbulence**

If a heavy aircraft (KC-135, KC-10, E-3, A-50, Il-76/78, C-17, C-5, B-52, B-1B, Tu-95/142/160, An-124, KJ-2000) has just departed or landed on the same runway axis, or is on final ahead of you, Tower adds "Caution wake turbulence."

#### Handoff and Departure check-in

At about 500 ft above the airfield, Tower hands you to Departure (Approach, or the airfield's own Departure frequency):

> **Tower:** Enfield one one, contact Kutaisi Approach.  
> **You:** Kutaisi Approach, Enfield 1-1, airborne, passing 1500.  
> **Approach:** Enfield one one, Kutaisi Approach, radar contact, 3 miles west of the field. Resume own navigation, maintain VFR.

- **Check-in phrases:** "airborne", "passing …", "climbing …", "departed", "out of …", "with you", or just the station and your callsign.
- **No squawk yet:** Approach first gives "squawk …", then "radar contact" a few seconds later.
- **IFR:** "radar contact, … Fly heading 280, climb and maintain flight level one five zero."
- **VFR still inside the zone:** "Continue as cleared, report leaving the control zone." When you then say "leaving the control zone", you get "roger, resume own navigation, squawk VFR, frequency change approved. Good day."
- **If you don't check in:** Approach doesn't identify you by itself. 60 s after the handoff, or at the zone edge or the exit CRP, Tower reminds you once: "contact Kutaisi Approach now" (with the frequency if it differs from the one you are on; with an own Departure frequency "contact Kutaisi Departure now, two seven three decimal five five"). 60 s later: "no contact with Kutaisi Approach, radar service not provided" (VFR: plus "squawk VFR, frequency change approved"; IFR: deviation). See [Mandatory reports](#mandatory-reports).
- **Leaving the zone before the handoff** (for example a low departure): "leaving control zone, squawk VFR, frequency change approved. Good day."
- **VFR, after "report leaving the control zone":** if you don't report, you get "report leaving the control zone." at the zone edge or the exit CRP. 1 NM outside the zone (at least 30 s later): "I show you clear of the zone, radar service terminated, resume own navigation, squawk VFR, frequency change approved. Good day." and a deviation.
- **15 NM from the airfield:** Departure signs you off: "radar service terminated, squawk VFR, frequency change approved."
  - IFR to another airfield: instead you are handed to that airfield's Approach: "contact Batumi Approach …"
  - If the mission has a friendly AWACS: "contact Overlord …"

**Requests while departing**

- **Altitude:** "request higher" or "request 8000 feet". Approach replies "climb and maintain …", or "unable, traffic" / "unable, minimum altitude …".
- **Heading:** "request heading 270" gets "fly heading two seven zero, approved."
- **Direct:** "request direct Batumi" gets "cleared direct Batumi, turn left heading …, 45 miles."

### Tower: arrival

#### Overhead break: initial and break

Approach vectors you to the **initial point**: about 2 NM (4,000 m) before the threshold on the extended centreline, at pattern altitude. Pattern altitude is the airfield elevation plus 1,500 ft, rounded to 100 ft; at Kutaisi it is 2,000 ft. Approach then hands you to Tower:

> **Approach:** Enfield one one, turn left heading two five zero, on course, 6 miles from initial, maintain 2000 feet, contact Kutaisi Tower, report initial for overhead break.  
> **You:** Kutaisi Tower, Enfield 1-1, initial runway 25.  
> **Tower:** Enfield one one, runway two five, cleared break left hand. Report final.  
> **You:** Enfield 1-1, base, gear down, full stop.  
> **Tower:** Enfield one one, runway two five, wind two four zero degrees, eight knots, cleared to land.

- **Weather on first contact:** if Tower is your first contact (you were not handed off) and you gave no ATIS letter, the break clearance also includes the wind and the QNH.
- **Sequence:** "number 2, follow the F-16C on downwind, runway two five, cleared break left hand."
- **Flights:** "initial, 2 second break" gets "2 second break approved."
- **Too far away:** "initial" counts only on the centreline (within 0.5 NM) and near the initial point. Otherwise you get: "negative, you are 8 miles from initial. Fly heading …, 2000 feet, report initial runway two five."
- **Base call:** "base" (or "final") after the break is your landing call. If the telemetry shows your gear up and you didn't say "gear down", Tower first says "check wheels down."
- **Calling "final" on the initial** gets "roger, report base." Tower never clears you to land before the break.
- **No initial call:** at the initial point Tower asks once, "Enfield one one, Kutaisi Tower, report initial." If you continue past it without calling, 1 NM later you get: "no break clearance, continue straight through, re-enter initial runway two five, report initial."

**Pattern checks after the break**

- On the wrong side: "pattern is south of the runway, left hand pattern runway two five." At other airfields: "wrong side, …"
- More than 500 ft off pattern altitude on downwind: "check altitude, pattern altitude 2000 feet."
- Above the zone limit (pattern altitude plus 500 ft): "check altitude, pattern altitude …"
- Traffic on final ahead: "extend downwind, number two, traffic …, I will call your base." Then: "turn base now, number two, follow the traffic, report final."

#### Overhead join

- **Kutaisi via CRP North:** "proceed overhead, 2000 feet, contact Kutaisi Tower, report overhead."
- **Calling "overhead"** within 2.5 NM of the runway centre gets "join left hand downwind runway two five, 2000 feet, report final." Farther away you get: "negative, you are 4 miles north of the field. Fly heading …, report overhead."
- **Terrain:** if terrain rules out the approach to the active runway, Approach can route you "for overhead join runway 31 via the extended centerline runway 13".
- **Other pattern calls:** "downwind" or "base" in the pattern gets "number one, report final" (or "number 2, follow the …").

#### Straight-in

> **You:** Kutaisi Approach, Enfield 1-1, request straight in.  
> **Approach:** Enfield one one, Kutaisi Approach, straight in approach runway two five approved, QNH one zero one three. Fly heading two four zero for six mile final. Maintain 2500 feet. Contact Kutaisi Tower.  
> **You:** Kutaisi Tower, Enfield 1-1, four miles final, gear down.  
> **Tower:** Enfield one one, runway two five, wind …, cleared to land.

- **How you are routed:** within 8 NM, if the terrain is clear, you go direct to a 6-mile final. Otherwise Approach gives you radar vectors to final.
- **Altitude:** on or below the glide path, but never below terrain clearance.
- **ILS runways:** "maintain 2500 feet until established, cleared ILS approach runway two five, localizer …"
- **Calling "initial" on a straight-in** gets "make straight in runway two five, report four miles final."

#### Pattern work

| You say | Effect |
|---|---|
| "touch and go", "closed pattern", "pattern work", "circuits", "practice" (alone, or with "ready" or "inbound") | Touch-and-go circuits: "cleared touch and go, left closed traffic approved, report base." You go straight back to downwind; circuits are counted ("Pattern 2 (touch and go). To end: Tower → final, full stop.") |
| "low approach", "practice approach", "practice ILS/TACAN/VOR/instrument" | "cleared low approach". After IFR or straight-in practice, Approach gives you new vectors at once |
| "the option" | "cleared for the option": touch-and-go or full stop, your choice |
| "low pass", "fly by" | "cleared low pass, not below 500 feet, after the pass climb runway heading 3000 feet". No pattern afterwards. From outside the pattern: "report five miles final runway two five" |
| "full stop" | Ends pattern work; the next approach is a landing |

#### Landing clearance and sequence

- **Clearance:** "Enfield one one, runway two five, wind …, cleared to land." (or cleared touch and go, low approach, …)
- **Sequence:** if there is traffic ahead: "number 2, follow the Hornet on 3 mile final, continue approach."
- **Runway occupied:** "continue approach, traffic on runway."
- **Calling "final" when you are not on final:** "negative, I don't have you on final. You are 6 miles north of the field. Report final runway two five when established."
- **Lined up on another runway:** "check runway! You are lined up for runway zero seven, runway two five in use. Join left hand downwind runway two five, 2000 feet."
- **Parallel runways:** if you are clearly lined up on the parallel runway (heading within 10°, within the inner third of the spacing, inside 5 NM), the clearance switches to that runway.
- **Gear still up on final:** if your gear is still up between 2.5 and 0.3 NM and you are below about 800 ft (250 m) above the ground, Tower calls "check wheels down!"
- **Low on final:** more than 200 ft below the 3° glide path inside 2.5 NM gets "low altitude alert, check your altitude immediately." This is said once per approach.
- **Without a landing call:** there is no landing clearance. Tower asks once, "report final", at about 2.5 NM on final (or on base). Still without a call inside 0.8 NM or below 300 ft: "go around, no landing clearance, …, report base" (IFR or straight-in: missed approach) and a deviation. After your call, the clearance comes as soon as the runway and the traffic ahead allow it (see [Mandatory reports](#mandatory-reports)).

#### Go-around

| Situation | Controller |
|---|---|
| You: "going around" / "go around" (VFR) | "roger, climb and maintain 2000 feet, join left hand downwind runway two five, report base." (with pattern work: "roger, left closed traffic approved, report base.") |
| You: "going around" / "missed approach" (IFR or straight-in) | "roger, go around. Fly runway heading, climb and maintain 3000 feet, contact Kutaisi Approach." (see Missed approach below) |
| Runway blocked or MAYDAY traffic inside 4 NM, and you are inside 0.8 NM | "go around, I say again, go around, traffic on runway, climb and maintain 2000 feet, join left hand downwind runway two five, report base." |
| Going around without a call while you hold a landing clearance (300 m past the threshold, still above 200 ft) | "I show you going around, confirm? Climb and maintain 2000 feet, join left hand downwind runway two five, report base." The landing clearance is cancelled, plus a deviation. IFR or straight-in: the same question, then the missed approach. |
| Landing after Tower sent you around, or without a clearance | Deviation in the debriefing |

"Go around" is accepted only during an approach to this airfield. Otherwise you get: "say intentions."

### Approach

#### Check-in and radar vectors

> **You:** Kutaisi Approach, Enfield 1-1, 25 miles west, 8000 feet, inbound for landing, information Alpha.  
> **Approach:** Enfield one one, Kutaisi Approach, identified, 25 miles west. Runway two five in use, wind two four zero degrees, eight knots, QNH one zero one three. Expect vectors to initial runway two five for overhead break. Turn left heading zero nine zero, 20 miles from initial, descend and maintain 5000 feet, speed 300 knots, downwind.

- **Check-in phrases:** "inbound", "landing", "recovery", "rejoin", "full stop", "with you", or the station and your callsign.
- **Within 4 NM of the airfield,** Tower answers instead: "runway two five, wind …, QNH …. Join left hand downwind, 2000 feet, report final."
- **The vector plan:** Approach takes you via downwind and base, with a 30° intercept onto the centreline.
  - VFR: to initial.
  - Straight-in or IFR: to the final approach course, with a gate at 10, 8, 6 or 4 NM.
- **Terrain:** the plan uses the terrain map and never descends you below the minimum vectoring altitude: terrain plus 1,000 ft, or plus 2,000 ft in mountains. If terrain blocks the active runway, the plan may use the opposite runway (up to 10 kt tailwind) or a circling approach: "Due to terrain, expect …".
- **When Approach talks:** every call gives a heading. Distance, altitude, speed and leg are mentioned only when they change. Descents come in steps along a 2.5° profile. When the sequence changes, Approach says "No traffic ahead, you are number one" or "You are number 2, 1 aircraft ahead".
- **Handoff to Tower:** only shortly before the gate (within 2 NM of it, at most 1,000 ft above the gate altitude) or at the gate at the latest. If you join the centreline farther out, Approach keeps you, steps you down along the profile ("descend and maintain 4000 feet", the gate altitude only once the profile at your position gets there) and hands you off near the gate. VFR traffic gets "on course, 6 miles from initial, maintain 2000 feet, contact Kutaisi Tower, report initial for overhead break." IFR traffic gets the approach clearance (below).
- **Kutaisi CRPs:** North and South are always allowed, West only for runway 07 and East only for runway 25. If you fly to one yourself, report it ("C R P south"). Approach then sends you to initial or overhead and hands you to Tower. A CRP that isn't allowed gets "negative, C R P east is not available for runway zero seven. Enter via C R P …". Other airfields have no CRPs: "negative, no reporting points at Senaki Kolkhi", followed by vectors.

#### Altitude and speed instructions

- **Altitude:** "descend and maintain …", "climb and maintain …", or "maintain …" for the same altitude. All altitudes are what your altimeter reads when set to the airfield QNH.
- **Speed:** given only within 25 NM.

  | Aircraft | Speeds |
  |---|---|
  | Jets | VFR 300 kt. Straight-in, by remaining track: 300 kt, below 20 NM 250 kt, below 10 NM 200 kt |
  | Props and warbirds | 200 / 170 / 140 kt |
  | A-10, L-39, C-101 | 250 / 200 / 160 kt |
  | Helicopters | No speed assignments |

  If you are too close behind the aircraft ahead (3 NM, or 5 NM behind a heavy): "reduce speed 170 knots for spacing." Near the gate: "reduce to final approach speed".
- **Say airspeed:** after a speed assignment you have 90 s to comply. If you are still more than 60 kt off and not slowing down, Approach asks "say airspeed." Answer with your speed: "Kutaisi Approach, Enfield 1-1, 340 knots", or use wheel **Approach → Report airspeed**, or press Enter.

  | Your answer | Approach |
  |---|---|
  | Within 30 kt of the assignment | "roger." |
  | Further off | "roger, reduce speed 300 knots." (once) |
  | "unable" | "roger, resume normal speed." |
  | No answer within 30 s | "resume normal speed." |

- **Say altitude / heading / position:** if a controller asks for one of these, Enter sends your current value.
- **Deviations** (more than 7°, 500 ft or 30 kt off, and not correcting):

  | Deviation | Approach says |
  |---|---|
  | Large (more than 30° or 1,000 ft) | Repeats the heading, or says "check altitude, climb and maintain …" or, if you are too high: climbed without clearance "verify altitude, maintain …", the second time "climb not authorized, descend and maintain …"; not yet down from a descent "descend and maintain …" again; "expedite descent, maintain …" only when traffic is at your altitude |
  | The same correction ignored three or more times | "verify heading/altitude" → new vectors or "… immediately" → "say intentions" → "you are not following instructions, you are removed from the sequence. Approach cancelled …" (deviation) |

#### Requests under vectors

| You say | Approach |
|---|---|
| "request direct" | Approved only if you are number one, the terrain is clear and you are not too high: "direct approved, turn left heading …". Otherwise "unable, …" |
| "request heading 270" under vectors | "unable, …" (vectors continue) |
| "request higher" / "request lower" / "request 6000 feet" / "request flight level 200" | "climb/descend and maintain …". Higher under radar vectors: far out (more than 25 NM track to the gate) approved in one block – if you are already above, your present altitude ("maintain 14000 feet"), otherwise up to FL200 (props 10,000 ft), but only as high as still lets you make the gate on the normal profile; the altitude stays until the profile needs the descent, which then comes as "descend at pilot's discretion, maintain …". Closer in: "unable higher, expect lower shortly, maintain …" (traffic: "unable higher, traffic, maintain …"); a request for higher never gets "descend". "request descent at pilot's discretion": "descend at pilot's discretion, maintain …" (down to 1,000 ft above the gate altitude); until 10 NM before the gate you descend when you like, no reminders. Or "unable, minimum altitude …" / "unable, traffic". In the hold: "unable, maintain … in the hold, I will call you." Once you have been handed to Tower: "negative, continue as cleared." |
| "request runway 07" | "runway zero seven approved." The plan is rebuilt |
| "VFR", "cancel IFR", "visual recovery" (VFR weather) | "roger" (with "cancel IFR": "roger, I F R cancelled"), then "Expect vectors to initial …" |
| "request straight in" while being vectored | "roger, straight in approach approved." The plan is rebuilt |

#### ILS, PAR and visual approaches

**ILS** (the runway has a localizer in the map data):

> **Approach:** Enfield one one, 12 miles from touchdown, maintain 3000 feet until established, cleared ILS approach runway two five, QNH one zero one three, contact Kutaisi Tower.

The controller only monitors an ILS approach: no glide-path calls, just the one low-altitude alert.

**PAR** (IFR without an ILS, or an emergency without an ILS). After the handoff, Tower gives precision-radar corrections at most every 20 s, and only for clear deviations:

> **Tower:** Enfield one one, three miles, slightly above glidepath, reduce rate of descent, slightly left of course, turn right heading two five two.

Glide-path terms: "well/slightly above/below glidepath". Corrections: "increase rate of descent", "reduce rate of descent", "level off".

**Visual approach** (VFR weather only):

> **You:** Kutaisi Approach, Enfield 1-1, request visual approach.  
> **Approach:** Enfield one one, Kutaisi Approach, radar contact, 15 miles west of the field, QNH …. Expect visual approach runway two five, report field in sight.  
> **You:** Field in sight.  
> **Approach:** Enfield one one, Kutaisi Approach, cleared visual approach runway two five, QNH …. Contact Kutaisi Tower.

- If you already said "field in sight" in the request, you are cleared at once.
- "negative field in sight" gets "roger, report field in sight."
- If you haven't reported the field 4 NM before the threshold, you get the normal straight-in or instrument approach instead.

#### Holding and expected approach time

Approach puts you in a hold if any of these apply:

- Two or more aircraft are in the pattern (within 4 NM, below about 3,000 ft above the airfield).
- Players arrive in sequence and you are not next.
- Another aircraft has declared MAYDAY.

> **Approach:** Enfield one one, Kutaisi Approach, identified, 30 miles west. Runway two five in use, … Fly heading zero eight five, descend and maintain 5000 feet, speed 230 knots, hold on the two six zero radial, 15 DME, 14 miles to go, I will call you there. Expected approach time three five, you are number 3.  
> *(at the fix)* **Approach:** Enfield one one, hold on the two six zero radial, 15 DME, orbit left hand, maintain 5000 feet, speed 230 knots.  
> *(later)* **Approach:** Enfield one one, leave the hold, turn right heading …

- **Holding fix:** 15 NM from the airfield, away from the final and departure paths, chosen for low terrain. It is given as a TACAN radial/DME if the airfield has TACAN. Otherwise you get "hold at present position" or "… miles to the holding point".
- **Altitude:** the lowest level is the zone limit rounded up to the next 1,000 ft, plus 1,000 ft per place in the sequence. Two aircraft near each other never get the same level. You are stepped down as aircraft ahead leave: "maintain 4000 feet, you are number 2."
- **Speed:** 230 kt, 200 kt for props, none for helicopters.
- **Expected approach time:** given once as the minute of the mission clock ("expected approach time three five"). It is repeated only if it changes by 5 min or more ("revised expected approach time …"). Without a mission clock: "expect approach in 6 minutes".
- **Checks in the hold:** if you drift off, or are more than 30 kt off speed, you get a heading or speed reminder. More than 500 ft off altitude gets "check altitude, maintain 5000 feet."
- **Leaving the hold:** when it's your turn, you are released with a heading that points roughly toward the approach, within 2 min.

#### Traffic information and traffic alerts

| When | Call |
|---|---|
| First contact, vectors | "Traffic, Viper, 3 miles south of the field, 1500 feet." |
| Conflict within 60 s (closer than 1.5 NM and 700 ft) under vectors or on approach | "traffic alert, traffic 2 o'clock, 3 miles, same altitude. Turn right heading one two zero immediately, climb and maintain 5000 feet." No other instructions for the next 30 s |
| AI departure climbing into your path | "turn right heading …, traffic 11 o'clock, 4 miles, departing aircraft, climbing through 3000 feet." |
| In the pattern: traffic not talking to Tower, closer than 0.5 NM within 30 s | "traffic 2 o'clock, 1 mile, FA-18C, 1500 feet, no contact with Tower." |

- **Answering:** "traffic in sight" or "tally", "looking", "negative contact" or "no joy". Approach answers "roger."
- **Whose traffic:** radar advisories cover only your own side and neutrals; enemy aircraft are the AWACS's job. Tower reports everyone in the pattern.

#### Terrain warning

You get this warning if you are under radar service (vectors, hold, departure after the handoff, or flight following), more than 5 NM from the airfield, and less than 500 ft above terrain now or within the next 60 s:

> **Approach:** Enfield one one, low altitude alert, check your altitude immediately. The minimum altitude in your area is 4500 feet.

It is said once per flight. Say "terrain in sight" to acknowledge it ("roger"); after that there are no more terrain warnings.

#### Missed approach and practice approaches

On an IFR or straight-in approach, a go-around becomes a missed approach in two steps:

> **Tower:** Enfield one one, roger, go around. Fly runway heading, climb and maintain 3000 feet, contact Kutaisi Approach.  
> **You:** Kutaisi Approach, Enfield 1-1, missed approach.  
> **Approach:** Enfield one one, Kutaisi Approach, identified. Turn right heading three four zero, climb and maintain 3000 feet, … Say intentions.

- **Missed-approach altitude:** the zone limit or the terrain 10 NM ahead, rounded to full thousands.
- **No check-in:** Approach doesn't vector you without your check-in. After 45 s Tower reminds you: "contact Kutaisi Approach now". 60 s later: "no contact with Kutaisi Approach, continue runway heading, maintain 3000 feet, contact Kutaisi Approach now", plus a deviation. A late check-in still gets you vectors (see [Mandatory reports](#mandatory-reports)).
- **Practice approaches** (low approach or touch and go under IFR or straight-in) go straight back into vectors, without "say intentions". Each approach is counted ("Approach 2 (low approach). To end: Approach → full stop.").

#### Cancel approach

- **You:** "cancel approach", "abort approach", "cancel landing", "leaving the pattern".
- **Approach:** "roger, approach cancelled, resume own navigation, QNH …. Call me when ready for another approach."
- **Leaving without a call:** if you fly away from an approach, you first get "I show you leaving the control zone, say intentions." 3 NM later: "approach cancelled, frequency change approved."

### Airspace

#### Control zone

- **Size:** every airfield has a control zone with a 5 NM radius. At Kutaisi it reaches out to the CRPs, about 9 NM.
- **Height:** it extends up to 3,000 ft above the airfield.
- **VFR limit inside:** pattern altitude plus 500 ft.

#### Zone transit

> **You:** Kutaisi Approach, Enfield 1-1, 12 miles west, 3000 feet, request zone transit, eastbound.  
> **Approach:** Enfield one one, Kutaisi Approach, cleared to cross the Kutaisi control zone overhead, eastbound, not below 3500 feet, QNH …, runway two five in use, report clear of the zone.  
> **You:** Enfield 1-1, clear of the zone.  
> **Approach:** Enfield one one, frequency change approved.

- **Phrases:** "transit", "crossing", "cross the …", "overflight", "pass through". Within 5 NM, Tower answers.
- **Transit altitude:** 1,000 ft above the pattern and above the terrain in the zone, rounded up to 500 ft.
- **Below it:** "check altitude, not below 3500 feet, pattern traffic below."
- **Players in the pattern** are told: "traffic, Viper crossing overhead eastbound, 3500 feet."
- **Busy airfield:** "remain outside the control zone, expect transit in 4 minutes."
- **No report:** 1 NM past the zone edge: "I show you clear of the zone, frequency change approved."

#### Flight following

> **You:** Kutaisi Approach, Enfield 1-1, request flight following.  
> **Approach:** Enfield one one, Kutaisi Approach, squawk five three two one.  
> **Approach:** Enfield one one, radar contact, 12 miles west of the field, QNH …. Flight following, report leaving frequency.

- **What you get:** traffic advisories within 5 NM and ±1,500 ft ("traffic, 10 o'clock, 4 miles, southbound, 500 feet above, Hornet"), and "traffic no longer a factor" when the traffic is gone. If you said "negative contact", you get an update after 30 s. Traffic alerts and terrain warnings also apply.
- **It stays with you** when you change airfield.
- **To end it:** "cancel flight following" or "leaving frequency" gets "radar service terminated, squawk VFR, frequency change approved."

#### Zone guard

With `"AirspaceWatch": true` (the default), Tower watches the control zone.

**Who is called:** you, if you enter the zone without contact and could get in the way of the pattern, the final or departure path, or traffic that is actually there.

1. Tower tells you where you are and turns you away, or asks you to climb:

   > **Tower:** Enfield one one, Kutaisi Tower, you are entering the Kutaisi control zone without clearance, 4 miles north of the field, crossing the final approach course runway two five, turn left heading zero one zero, remain clear of the control zone.

   A text tip shows the legal way in: wheel **General → Request zone transit**, or **Approach → Inbound for landing**.
2. After 30 s without a reply, if you are still close: "turn right heading three six zero, leave the control zone immediately, traffic in the pattern."
3. When you leave: "leaving the control zone. Possible pilot deviation, advise you contact Kutaisi Tower after landing." This goes into the debriefing.

**Other cases**

- **Crossing the active runway low** without clearance: "you crossed the active runway without clearance." (deviation)
- **No ATC contact at all on this flight:** you are called as "Unidentified aircraft 4 miles north of Kutaisi, heading 180, 2000 feet, …".
- **Not tuned to the airfield frequency (SRS):** the call goes out on **Guard** (243.0 / 121.5): "…, Kutaisi Tower on guard, … Contact Kutaisi Tower one three four decimal zero."
- **Landing:** if you are clearly landing (gear down, or slow and descending on final), Tower only asks "say intentions".

**The guard stays silent:**

- for helicopters
- within 5 min of any ATC contact
- with flight following, or while another procedure is running
- for the first 60 s after takeoff
- with enemy aircraft within 20 NM
- above the top of the zone

If you are checked in with AWACS, only the pattern and the runway are watched.

With `"AirspaceWatch": false`, only the safety calls remain. There are no "possible pilot deviation" calls and no debriefing entries.

### Emergencies

| You say | Response |
|---|---|
| "mayday mayday mayday, engine failure, request immediate landing" within 5 NM, or on a final inside 6 NM | **Tower:** "roger mayday, runway two five, wind …, QNH …, cleared to land, emergency services standing by." You may land on any runway you are lined up on |
| Same, farther out | **Approach:** "roger mayday, all traffic is holding. Turn …, descend at your discretion, maintain … Runway two five available, QNH …, say souls on board and fuel remaining." (not after a wheel/F10 call, it already names both). Tower clears you to land at about 5 NM. Any answer to the question gets "roger"; Enter suggests "25 minutes fuel, one soul on board" |
| Emergency call on the ground | The wheel/F10 call is "MAYDAY MAYDAY MAYDAY, Kutaisi Ground, Enfield 1 1, engine failure, shutting down." Answer: "roger mayday, emergency services are on the way. Hold position." Without the kind of emergency: "…, say nature of emergency." |
| "pan pan, pan pan, pan pan, …" | Same procedure: you get priority, but other traffic doesn't hold |
| A second emergency call while your emergency is running | "roger mayday, continue as cleared." |
| "high key" / "flameout, high key" during your own emergency (real flameout, also in IMC) | "report low key." At "low key, gear down": "runway two five, wind …, cleared to land." (SFO practice: only VMC and without another emergency) |
| "cancel emergency" | Under radar vectors: "roger, emergency cancelled, continue as cleared." With landing clearance: "roger, emergency cancelled, runway two five, cleared to land." From the initial or the pattern: "Join left hand downwind, 2000 feet, report base." Otherwise (far out, no emergency running) just "roger." |
| "minimum fuel", "low fuel", "bingo" | "roger minimum fuel, number 2, expect approach in 3 minutes." or "…, expect no delay." No priority |

- **Wheel and F10:** **Emergency → MAYDAY / PAN PAN →** engine failure, fuel (MAYDAY only), hydraulic failure, battle damage, medical, bird strike.
- **Which airfield:** if you haven't named or selected an airfield and aren't tuned to one, the emergency goes to the nearest suitable friendly or neutral airfield. Runways of 1,800 m or longer are preferred.

**What a MAYDAY means for everyone else at the airfield**

| Who | What happens |
|---|---|
| Aircraft within 15 NM, not on short final | Leave the pattern or the vectors and hold: "emergency in progress, leave the pattern, fly heading …, hold …, I will call you." |
| Departures | "hold short runway two five, emergency traffic inbound." |
| Landing traffic | Within 8 NM, no new landing clearances ("continue approach, emergency traffic"). Within 4 NM: go-around |
| Aircraft landing ahead of the emergency | "expedite vacating, emergency traffic 6 miles" |

### Landing grade and debriefing

**Landing grade**

Every touchdown on a runway gets a grade, shown as game text. Four parts score up to 25 points each:

| Part | 25 points | Lower |
|---|---|---|
| Glide path (average deviation, final 0.3–3 NM) | ≤ 50 ft | ≤ 100 ft: 18, ≤ 200 ft: 10 |
| Centreline (average, same section) | ≤ 15 m | ≤ 35 m: 18, ≤ 70 m: 10 |
| Touchdown point | 100–500 m past the threshold | ≤ 800 m: 15, longer: 5, short of the threshold: 0 |
| Sink rate | ≤ 300 ft/min | ≤ 500: 20, ≤ 700: 12 |

- Glide path and centreline are scored only if you flew a straight final long enough.
- **Result:** **OK** at 85 or more, **Fair** at 70 or more, **No grade** at 50 or more, otherwise **Cut**. Example: `Landing grade Kutaisi runway 25: OK (88/100) – Glide path Ø +40 ft, Centerline Ø 12 m, Touchdown 320 m past the threshold, Sink rate 280 ft/min.`

**Debriefing**

The debriefing collects:

- landing grades and pattern or practice-approach counts
- approaches without an initial call
- deviations with mission time, for example `Deviation: takeoff without clearance Kutaisi 25 (09:14)`
- the number of calls the controller rejected
- range, tanker, carrier and AWACS entries

When you park after a flight, it is shown automatically and saved to `%LOCALAPPDATA%\Programs\DCS-ATC\debrief\<date>_<time>_<callsign>.txt`. You can open it at any time with wheel or F10 **General → Debriefing**.

**Deviation types:** takeoff without clearance; runway entered without clearance; landing without clearance; landing after go-around; airspace violation; runway crossed without clearance; removed from the sequence; taxi without clearance; approach without contact; no landing report; go-around without report; no check-in with departure (IFR); no check-in with approach after missed approach; no report "leaving the control zone"; no report "clear of the zone". Carrier, tanker and range add their own (approach time passed without "commencing", contact or approach to a tanker without clearance, no "off" call on the range).

### Mandatory reports

**Principle:** you report your position yourself, as in real military flying. The controller does not report it for you. If you don't report, you first get a prompt. If you still don't report, a real consequence follows: no clearance, a go-around, a wave-off, or no radar service. Deviations go into the debriefing.

| Required report (what you say) | Prompt if you don't | Consequence if you still don't |
|---|---|---|
| **Initial:** "Kutaisi Tower, Enfield 1-1, initial runway 25" | At the initial point: "report initial" | 1 NM past initial: "no break clearance, continue straight through, re-enter initial runway two five, report initial." |
| **Overhead:** "…, overhead" | Over the field (0.7 NM): "report overhead" | 1.5 NM later: "no clearance to join, remain outside the pattern, say intentions." |
| **CRP** (Kutaisi): "…, C R P north" | Within 1 NM of the CRP, Approach: "report C R P north" | 2 NM past it: "say position, remain outside the control zone" (no handoff) |
| **Landing call:** pattern "base, gear down (full stop)"; straight-in or ILS "four mile final, gear down" | At about 2.5 NM on final, or on base: "report final" | Inside 0.8 NM or below 300 ft: "go around, no landing clearance, …, report base" (IFR or straight-in: missed approach), plus a deviation |
| **First call before entering the zone** (Approach or Tower) | Zone guard: "…, say intentions" | Inside 1 NM on final: "go around, you are not cleared to land", plus a deviation |
| **Tower check-in after "contact Tower"** ("…Tower, Enfield 1-1, initial / overhead / four mile final") | Only if Tower has its own frequency: after 60 s, or within 5 NM of the field, Approach says "contact Kutaisi Tower now, <freq>" (on a shared frequency the prompts above apply) | No landing clearance; the landing-call rule above applies |
| **Going around:** "…, going around" | "I show you going around, confirm?" | Landing clearance cancelled, "…, report base", plus a deviation |
| **Departure check-in:** "…Departure, Enfield 1-1, airborne, passing 2000" | 60 s after the handoff or at the zone edge, Tower: "contact Kutaisi Approach now" (own Departure frequency: "contact Kutaisi Departure now, <freq>") | 60 s later: "no contact with Kutaisi Approach, radar service not provided". IFR: deviation. VFR: "…, squawk VFR, frequency change approved" |
| **Missed-approach check-in:** "…Approach, missed approach, climbing 3000" | After 45 s, Tower: "contact Kutaisi Approach now" | 60 s later: "no contact with Kutaisi Approach, continue runway heading, maintain 3000 feet, …", no vectors until you check in, plus a deviation |
| **Leaving the control zone** (after "report leaving the control zone"): "…, leaving the control zone" | At the edge or the exit CRP: "report leaving the control zone" | 1 NM outside: "I show you clear of the zone, radar service terminated, resume own navigation, squawk VFR, …", plus a deviation |
| **Transit complete:** "…, clear of the zone" | 0.5 NM outside: "report clear of the zone" | 1 NM outside: "I show you clear of the zone, frequency change approved", plus a deviation |
| **Taxi to parking:** "…Ground, runway vacated, request taxi to parking" | "contact Kutaisi Ground" (30 s after vacating) | Faster than 5 kt and more than 150 m from the runway without clearance: "hold position, you are not cleared to taxi. Request taxi.", plus a deviation |
| **Ready for departure** (also after spawning on the runway): "…, ready for departure" | none | Rolling without clearance: "stop immediately, … You are not cleared for takeoff.", plus a deviation |

In an airfield hold you don't need to report "established". Approach calls you at your expected approach time.

**These calls stay controller-initiated:** radar vectors, traffic information and traffic alerts, low-altitude alerts, "check wheels down", go-arounds for an occupied runway or an emergency, the takeoff clearance after "ready", stop calls, the Tower-to-Departure handoff at about 500 ft, PAR corrections, taxi conflicts, "extend downwind" and "turn base", emergency landing clearances, the approach clearance once you are established, and the zone guard.

The same principle applies at the carrier (ball call, commencing, see you at ten), at the tanker (visual, pre-contact) and at the range (in hot, off); see those chapters.

### Phrase table

Keywords are recognised anywhere in your call. Start with "<Airfield> <Controller>, <callsign>, …".

| You say (keywords) | Where | What happens |
|---|---|---|
| "request startup", "start up", "request engine start", "ready to start" | Ground | Start-up approved (the first call adds ATIS, runway and QNH) |
| "request taxi" (+ "departure north", + "runway 07") | Ground | Taxi to the holding point, squawk, hold short |
| "request startup and taxi" | Ground | Both at once |
| "request progressive taxi" | Ground, while taxiing | Directions to the holding point or to parking |
| "request IFR clearance (to Batumi)", "clearance", "IFR to" | Ground (in the air: Approach) | IFR clearance with squawk; read back the code → "readback correct" |
| "ready", "ready for departure", "holding short" (+ "closed pattern") | Tower | Takeoff clearance, line up and wait, or hold short |
| "abort", "rejecting", "stopping", "cancel takeoff" | Tower, when cleared or lined up | Clearance cancelled, "vacate …, report ready for departure" |
| "airborne", "passing 2000", "climbing", "departed", "out of", "with you" | Departure/Approach after takeoff | Radar contact (a squawk first if needed) |
| "leaving the control zone" | Approach, after it was requested | Signed off, squawk VFR |
| "inbound (for landing)", "landing", "recovery", "rejoin", "full stop" | Approach (Tower within 4 NM) | Radar vectors, holding, or pattern join |
| "inbound for pattern work, touch and go" | Approach | Vectors, then circuits |
| "request straight in", "ILS", "instrument", "TACAN/VOR/PAR approach", "radar approach", "IFR" | Approach | Straight-in or instrument approach |
| "request visual approach" / "field in sight", "runway in sight", "have the field" | Approach | Visual approach once the field is reported |
| "VFR", "cancel IFR", "visual recovery" | Approach | Overhead or initial instead of an instrument approach (VFR weather only) |
| "request runway 07" / "runway 30 left" | Any | Runway request (up to 10 kt tailwind) |
| "request higher/lower", "request 6000 feet", "request flight level 200" | Approach | New altitude or "unable" |
| "request heading 270", "request direct (Batumi)" | Approach | Heading approved or direct / "unable" |
| "<n> knots", "airspeed 340", "slowing", "unable speed" | Approach, after "say airspeed" | roger / one correction / resume normal speed |
| "C R P north/south/east/west" | Approach (Kutaisi) | Sent to initial or overhead, handed to Tower |
| "initial (runway 25)" (+ "2 second break") | Tower | Break clearance, or "negative, … report initial" |
| "overhead" | Tower | Join downwind, or steered to the overhead |
| "downwind", "base", "break" | Tower | Sequence number; "base" after the break = landing call |
| "final" (+ "full stop" / "touch and go" / "low approach" / "the option") | Tower | Landing or option clearance, sequence, or "negative" |
| "touch and go", "closed pattern", "pattern work", "circuits", "practice" | Tower | Circuits approved, laps counted |
| "low approach", "practice ILS", "low pass", "fly by", "the option", "full stop" | Tower/Approach | Sets the option for the next approach |
| "going around", "go around", "missed approach", "going missed" | Tower/Approach | Pattern go-around, or missed approach (IFR) |
| "cancel approach", "abort landing", "leaving the pattern" | Approach/Tower | Approach cancelled, resume own navigation |
| "request zone transit", "crossing", "overflight", "pass through" / "clear of the zone" | Approach (Tower within 5 NM) | Transit clearance / signed off |
| "request flight following", "radar service", "traffic advisories" / "cancel flight following", "leaving frequency" | Approach | Flight following on / off |
| "traffic in sight", "tally", "looking", "negative contact", "no joy" | Approach/Tower | "roger", advisory status noted |
| "terrain in sight" | Approach | Terrain warning acknowledged |
| "request separate landing", "split", "radar trail" | Approach (flights) | Each member reports individually |
| "runway vacated", "clear of runway", "taxi to parking" | Ground after landing | Taxi to a parking spot |
| "mayday", "pan pan" / "cancel emergency" | Any | Emergency handling / emergency cancelled |
| "minimum fuel", "low fuel", "bingo" | Approach/Tower | Acknowledged, sequence and delay |
| "unable" | Any | "roger, say intentions" (on the ground: "hold position") |
| "say again", "repeat", "say last", "didn't copy" | Any | Last call repeated or updated |
| "confirm …", "verify …" | Any | "affirm …" / "negative …" or repeat |
| "radio check", "how do you read" | Any | "read you five" |
| "request weather", "QNH", "wind", "which runway", "runway in use" | Any | Wind, QNH, runway in use |
| "good day", "thanks", "bye" | Any | "good day" |
| "roger", "wilco", "copy", "start up approved", just your callsign, readback of numbers | Any | No answer |

Pressing Enter in the wheel always sends the suggested next call, or the answer to the controller's last question.

## Carrier operations

DCS-ATC handles the radio for every friendly carrier placed in the mission. It provides Marshal, Tower (Air Boss), Approach, Departure and Paddles (the LSO). DCS's own carrier radio (Marshal, Air Boss, LSO) is switched off while DCS-ATC runs. DCS-ATC never spawns ships or aircraft. It works only with carriers and aircraft that are already in the mission.

### Which carriers are supported

Any ship group whose first unit is a supercarrier or carrier is recognised: Nimitz class / CVN, Stennis, Forrestal, Kuznetsov and Tarawa. On angled-deck carriers, the final bearing is about 9° left of the ship's heading (the BRC). The Tarawa has a straight deck, so its final bearing equals the BRC.

The **Carrier** page of the radio wheel appears only if the mission contains a carrier.

### Frequencies

All carrier stations share one frequency: Marshal, Tower, Approach, Departure and Paddles.

| Source | Frequency |
|---|---|
| Radio frequency of the carrier unit, set in the Mission Editor | Used if set |
| `"Carrier"` entry in `"Frequencies"` in `config.jsonc` | Used if the mission sets none. Default **127.5** |

The `"Carrier"` key is not in the installed `config.jsonc` template, but DCS-ATC adds the default automatically. To change it, add the key yourself:

```jsonc
"Frequencies": { "ATIS": 263.5, "Ground": 264.5, "Tower": 265.0, "Approach": 266.5,
                 "Range": 267.5, "AWACS": 251.5, "Tanker": 255.5, "Carrier": 127.5 },
```

With SRS, everything you transmit on a carrier frequency goes to the carrier. Without SRS (push-to-talk key only), DCS-ATC sends a call to the carrier if:

- it contains *Marshal*, *mother*, *Paddles*, *ball*, *Clara*, *carrier* or *pigeons*, or
- you are already checked in and it contains *initial*, *see you at*, *commencing*, *Charlie*, *platform*, *needles*, *up and …*, *down and …* or *on and on*.

You can also select the carrier under radio wheel → General → **Select airfield**. Calls without a station name then go to the carrier until you land.

Carrier voices come from `"Voices"` → `"Carrier"` in `config.jsonc`. The default is `"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02"`, which gives each station one of the two voices.

### Which Case is flown

DCS-ATC chooses the Case when you check in, based on the weather and the time of day:

| Case | Conditions |
|---|---|
| **Case I** | Day, cloud base at least 3000 ft, visibility at least 5 NM |
| **Case II** | Day, cloud base 1000–3000 ft |
| **Case III** | Night (sun more than 6° below the horizon), cloud base below 1000 ft, or visibility below 5 NM |

The Case stays the same for your whole recovery.

### The ship turns into the wind

While a player is in the recovery or launching, or mission AI is landing on the ship, DCS-ATC steers the carrier group into the wind. The speed is set for about 27 kt of wind over the deck, and the ship steams at 5–30 kt. If there is land within about 5 NM ahead, the ship does not turn, and Marshal gives you the course it is actually steaming. About 3 minutes after the last recovery or launch, the ship returns to its mission route.

### Radio wheel: Carrier page

| Wheel entry | What is sent |
|---|---|
| Marshal check in | `Marshal, Enfield 1 1, checking in, mother's 180 for 35, angels 18, state 6.2.` (bearing and distance from the ship, altitude and fuel are filled in automatically) |
| See you at | `Enfield 1 1, see you at angels.` |
| Initial | `Enfield 1 1, initial.` (Case I: only a text hint, see below) |
| Commencing | `Enfield 1 1, commencing.` |
| Platform | `Enfield 1 1, platform.` |
| Ball | `Enfield 1 1, Hornet ball, 5.2.` (type and fuel are filled in) |
| Clara | `Enfield 1 1, Hornet Clara, 5.2.` |
| Pigeons | `Enfield 1 1, pigeons.` |

**ENTER** (or **0**) on the open wheel sends the call that fits the current situation. If a controller has just asked you something, ENTER sends the answer:

- after "call the ball" it sends your ball call with type and fuel;
- after "say needles" it sends what your ACLS needles should show;
- after "report see me" it sends *see you at angels N*, after "report commencing" *commencing, angels N, state X*, after "report see you at ten" *see you at ten*.

Otherwise ENTER follows the recovery: *Marshal, checking in* → *see you at angels* (Case I) or *commencing* (Case II/III) → *platform* → *see you at ten* (Case II, within 12 NM of the ship) → *ball* (after "call the ball"). Each report is offered only once: after "see you at" ENTER offers nothing more in the holding, and *platform* only until Approach has radar contact (not again after a bolter). In Case I there is nothing to send after Charlie (zip lip). After a Case II/III cat shot, ENTER sends *airborne*, then *on top, angels N*. On the deck it sends nothing.

The fuel state in the check-in and the ball call is filled in only for carrier types with a known fuel capacity: Hornet, Tomcat, Viking, Hawkeye and Harrier.

### Checking in with Marshal (all Cases)

Check in about 50 NM out. Give your position from the ship, your altitude and your fuel:

> **You:** "Marshal, Enfield 1-1, mother's 360 for 35, angels 18, state 6.2."

Words that start a check-in: *Marshal*, *mother*, *check in* / *checking in*, *inbound*, *recovery* or *state*. The reported state is used to check your fuel against bingo (see below).

**Case I reply:**

> **Marshal:** "Enfield one one, Marshal, case one recovery, expected BRC three five four, altimeter two niner niner two, hold angels 2, expected Charlie zero seven, report see me."

A text hint also appears: *Hold overhead the ship at angels 2, left-hand circles (about 5 NM wide). Wait for "signal Charlie".*

**Case II/III reply:**

> **Marshal:** "Enfield one one, Marshal, case three recovery, CV-1 approach, expected final bearing three four five, altimeter two niner niner two. Marshal mother's one six five radial, 21 DME, angels 6. Expected approach time zero seven."

A text hint also appears: *To the marshal point: radial 165, 21 DME (TACAN 73X), angels 6; hold there and call "commencing" at your approach time.*

- **Holding altitudes:** Case I from angels 2 upward. Case II/III from angels 6 upward, with the marshal point at (angels + 15) DME on the reciprocal of the final bearing. Each aircraft gets the next free altitude.
- **Times:** given as minutes past the hour ("zero seven" means hh:07). The stack is worked first come, first served:
  - Case I: Charlie times are 90 s apart, the first at least 2 minutes after check-in.
  - Case II/III: approach times are 1 minute apart, the first at least 5 minutes after check-in, or later if you are still far from the marshal point.
- **Altimeter** follows `"AltimeterUnit"` (default `"auto"`): western types hear inHg ("altimeter two niner niner two"), others hear hPa ("QNH one zero one three").

Calls you can make while holding:

| You say | Reply |
|---|---|
| Case I: "Enfield 1-1, see you at angels 2" (answer to "report see me") | "Enfield one one, update state, switch Tower." Then give your fuel: "Enfield 1-1, 5.9" → "Enfield one one, Marshal, roger, state five point niner." (ENTER offers `state 5.9`). With your fuel already in the call ("see you at angels 2, state 5.9"): "Enfield one one, roger, switch Tower." From then on Tower answers you (a second "see you at" gets "roger"). |
| Case II/III: "see you at angels 6", or *established*, *holding*, *in the stack* | "Enfield one one, roger." |
| Case II/III readback: "Enfield 1-1, 165 radial, 21 DME, angels 6" | "Enfield one one, readback correct." A wrong value is corrected: "Enfield one one, negative, angels 6." A readback is not a new check-in; your EAT stays. |
| "Enfield 1-1, state 4.5" | "Enfield one one, Marshal, roger, state four point five." (below bingo: your bingo signal, see below) |
| "Marshal, Enfield 1-1, pigeons" (also *TACAN*, *say position*) | "Enfield one one, mother bears 180, 35 miles, expected BRC 354, TACAN seven three X-ray, ICLS channel one one." |

### Case I

**1. Holding and Charlie.** Hold overhead the ship at your assigned angels in left-hand circles. Tower gives "signal Charlie" when all of these are true:

- you are first in the stack;
- you are within 10 NM of the ship;
- your Charlie time has arrived;
- the ship is in the wind, or 5 minutes have passed since your Charlie time;
- fewer than two mission AI aircraft are in the landing pattern.

> **Tower:** "Enfield one one, signal Charlie."
> *(Text: Descend to the initial: 3 NM astern, 800 ft, heading 354, 350 kt; break left over the bow, downwind 600 ft.)*

If your Charlie time is 1 minute past while you are not in the holding (more than 10 NM out), the next aircraft gets Charlie instead and you go to the back of the stack:

> **Marshal:** "Enfield one one, signal Delta, expected Charlie two zero."

If the deck is the reason for the delay (the ship is still turning into the wind, or two or more AI aircraft are in the pattern), Marshal tells everyone once: "99, signal Delta." Keep holding: the order of the stack and your Charlie time stay the same.

**2. Initial and break: zip lip.** Case I is flown in radio silence. You do not call "initial". DCS-ATC recognises the initial when you are 1.5–6 NM astern, within 1 NM of the ship's axis and below 1500 ft. It then shows only a text hint: *Initial: zip lip, no radio until your own ball call (about 3/4 NM: "Hornet ball, 5.2").* If you say or select "initial" after Charlie, you get the same text and no radio reply. Before Charlie (still holding) it is refused and you keep your place: "Enfield one one, negative, not Charlie, hold angels 2, expected Charlie zero seven."

**3. Groove and ball call.** Roll into the groove and call the ball yourself at about ¾ NM:

> **You:** "Enfield 1-1, Hornet ball, 5.2."
> **Paddles:** "Roger ball."

If you haven't called the ball by about ½ NM (in the groove, below 600 ft, slower than 195 kt, gear down), Paddles asks for it:

> **Paddles:** "Enfield one one, call the ball."

If you call "Clara" (you can't see the ball), Paddles answers "Enfield one one, roger Clara, keep it coming."

A ball call counts only in the groove: up to 1.5 NM astern, within 0.3 NM of the centreline and below 1500 ft. Anywhere else it gets no reply. In the groove, a ball call that was heard unclearly still counts.

**No ball call, no pass.** Paddles makes no calls before your ball call (or "Clara"). If you still haven't called the ball after "call the ball", at about 0.3 NM in the groove you get "Wave off, wave off." and the grade `WO (no ball call)`.

### Case II and Case III (CV-1 approach)

**1. Marshal point.** Fly to the radial, DME and angels Marshal gave you, and hold there until your expected approach time (EAT). If you are not within 5 NM of the marshal point at your EAT, Marshal gives you a new time:

> **Marshal:** "Enfield one one, Marshal, new expected approach time one two."

**2. Commencing.** Marshal doesn't call you at your EAT. Leave the marshal point at your EAT and report:

> **You:** "Marshal, Enfield 1-1, commencing, angels 6, altimeter 29.92, state 5.2."
> **Marshal:** "Enfield one one, radar contact, final bearing three four five, switch Approach."
> *(Text: 4000 ft/min to 5000 ft, call "platform" there; then 2000 ft/min to 1200 ft; at 10 DME gear/flaps/hook down.)*

If you are at the marshal point but haven't reported 60 s after your EAT, Marshal asks once: "Enfield one one, Marshal, report commencing." 2 minutes after your EAT you get a new time at the back of the stack ("Enfield one one, Marshal, new expected approach time …") and a deviation. ENTER offers *commencing* from 15 s before your EAT, when Marshal accepts it. Earlier, Marshal answers "Enfield one one, negative, your expected approach time is zero seven." and you keep holding.

**3. Platform.** At 5000 ft:

> **You:** "Enfield 1-1, platform."
> **Approach:** "Enfield one one, Approach, radar contact, 19 miles." (later "platform" calls, e.g. after a bolter: "Enfield one one, roger.")

If you don't call Approach, Approach identifies you by itself once you are below 4500 ft or within 12 NM. Approach gives no course corrections before radar contact.

**4. Final and ACLS.** On the final bearing (within 2 NM of it and heading toward the ship), Approach guides you:

- **More than 0.3 NM off the final bearing:** you get an intercept heading, at most every 30 s and not while you are already turning back. For example: "Enfield one one, left of course, fly heading zero zero five."
- **At about 6 DME:** "Enfield one one, Approach, ACLS lock on, say needles."

  > **You:** "Enfield 1-1, up and on." (any of *up / down / on* **and** *left / right / on*)
  > **Approach:** "Enfield one one, concur."

  The needles are fly-to: needle up means you are low, needle left means you are right of course. If your report doesn't match your real position, Approach corrects you, for example: "Enfield one one, disregard needles, below glidepath, on course."
- **At 3, 2 and 1½ miles:** "Enfield one one, three miles, on glidepath, left of course." (and the same for two miles and one and a half miles)
- **At ¾ mile:** "Enfield one one, three quarter mile, call the ball." Call the ball as in Case I: "Enfield 1-1, Hornet ball, 5.2." → "Roger ball." (Case III adds the wind over the deck: "Roger ball, 25 knots, axial.")

**Case II: see you at ten.** In Case II you report the ship in sight yourself, usually at 10 DME:

> **You:** "Enfield 1-1, see you at ten."
> **Approach:** "Enfield one one, roger, switch Tower."
> *(Text: descend to the initial: 3 NM astern, 800 ft, heading 354, 350 kt; break left over the bow, downwind 600 ft.)*

From the initial you fly the Case I pattern (zip lip). If you haven't called inside 10 NM, Approach asks once: "Enfield one one, report see you at ten." Without the call you stay on the CV-1 approach down to "three quarter mile, call the ball".

### LSO (Paddles) calls

Paddles watches your groove from about ¾ NM while you are wings level, within 15° of the final bearing and slower than 195 kt. Paddles makes at most one call about every 3 s and always names the biggest error first. Before your ball call (or "Clara"), Paddles is silent, except for a wave-off.

| Call | Meaning |
|---|---|
| "Roger ball." | Ball call acknowledged |
| "A little power." / "Power." / "Power, power." | Low (slightly, clearly, well below the glidepath) |
| "Don't settle." | Slightly low and still sinking |
| "Easy with it." | Coming back up fast |
| "You're high." | Above the glidepath |
| "Right for lineup." / "Come left." | Left / right of the centreline |
| "You're slow." / "You're fast." | Angle of attack off on-speed (see below) |
| "Wave off, wave off." | Mandatory go-around |
| "Wave off, wave off, foul deck." | Landing area not clear |
| "Bolter, bolter, bolter." | Hook missed the wires: full power, fly off |

**Speed calls** ("slow"/"fast") need the on-speed AoA for your aircraft type. They also need angle-of-attack data, which is available only for the aircraft flown on the PC running DCS-ATC. The setting in `config.jsonc`:

```jsonc
"OnSpeedAoa": { "FA-18": 8.1 },   // degrees per type prefix; other types get no speed calls
```

For another type, fly a few passes and read the line `[LSO] … AoA im Mittel …` in the log to find the value.

### Wave-off and bolter

Paddles waves you off if:

- **Foul deck:** an aircraft is on the landing area.
- **Gear up:** your gear is known to be up anywhere from 0.6 NM to the ramp.
- **No ball call:** you haven't called the ball by about 0.3 NM after "call the ball".
- **Too far off close in** (0.04–0.25 NM): more than about 1.8° high, 1.2° low, or 3° off the centreline.

A wave-off is mandatory. If you ignore it, Paddles repeats it up to twice before the ramp, and a trap after it is graded as a **cut pass** (C, 0 points).

After a bolter or wave-off:

- **Case I:** no radio. Climb out, re-enter the pattern and fly another pass. "Call the ball" comes again.
- **Case II/III:** Departure: "Enfield one one, Departure, radar contact, turn left heading 165, maintain 1200, downwind." About 5 NM astern, Approach: "Enfield one one, Approach, turn left heading 345, intercept final." The ACLS and mile calls then start again.

### Bingo and divert

You get a signal to divert, and your recovery ends, if:

- your fuel is below bingo, or
- you make a third bolter or wave-off.

Bingo is checked against your fuel gauge or the state you reported. It is roughly 1500 lb plus 20 lb per NM to the nearest friendly or neutral airfield with a long runway.

> **Tower/Approach:** "Enfield one one, your signal is bingo, pigeons Kobuleti 084, 44 miles." / "…, your signal is divert, pigeons …"

The recovery also ends if you fly more than 50 NM from the ship (or 10 NM beyond your closest point if you checked in from farther out), or if you land ashore. With a friendly AWACS airborne, Marshal hands you over: "Enfield one one, Marshal, switch Overlord 251.5."

### Grading

After each pass the grade appears as text in the game, for example:

`LSO: (OK) 3-wire, groove 14 s, HIM SLOIC NESA (3 points)`

| Grade | Points | Meaning |
|---|---|---|
| `_OK_` | 5 | Perfect pass: only tiny deviations and the correct groove time |
| `OK` | 4 | Reasonable deviations |
| `(OK)` | 3 | Fair. A 1-wire trap is never graded better than this |
| `--` | 2 | No grade (large deviations) |
| `B` | 2.5 | Bolter |
| `OWO` | 2 | Own wave-off (you went around by yourself) |
| `WO` | 1 | Waved off (", gear up" is added if that was the reason; `WO (no ball call)` for a missing ball call) |
| `C` | 0 | Cut pass (wave-off ignored) |
| `WOFD` | – | Wave-off because of a foul deck, not graded |

**Comments:**

- **Error:** `H` high, `LO` low, `LUL` / `LUR` lined up left / right, `SLO` slow, `F` fast.
- **Where:** `X` at the start (¾–½ NM), `IM` in the middle, `IC` in close, `AR` at the ramp.
- **How much:** `(…)` a little, no mark = clearly, `_…_` a lot.
- **Case I groove time** (from ½ NM): `NESA` below 15 s, `LIG` (long in groove) above 18 s.

The wire comes from DCS's own landing event if the mission reports it. Otherwise DCS-ATC estimates it from your touchdown point.

### Departing from the carrier

There is **no radio on the deck**. Any call on the deck only shows the hint *No radio on deck (no land tower): start, taxi and catapult on the deck crew's signals.* No airfield ATIS or Ground answers you on the deck. The ship turns into the wind for the launch.

**Case I departure:** text only after the cat shot: *straight ahead parallel to BRC 354, 500 ft until 7 NM, then climb unrestricted; no radio.*

**Case II/III departure:**

> *(Text Case III: call "airborne", climb straight ahead, at 7 DME arc at 10 DME to the 024 departure radial, then outbound on it, call "on top" above the clouds.)*
> *(Text Case II: call "airborne", straight ahead parallel to BRC 354 staying visual, at 7 DME arc at 10 DME to the 024 departure radial, climb through the clouds only on it, call "on top" above the clouds.)*
> **You:** "Enfield 1-1, airborne."
> **Departure:** "Enfield one one, Departure, radar contact, departure radial zero two four."

DCS-ATC always uses BRC + 30° as the departure radial, so the arc never leads to the radial you are already on after the launch.
> **You:** "Enfield 1-1, on top, angels 8."
> **Departure:** "Enfield one one, Departure, switch Overlord 251.5." (without an AWACS: "Enfield one one, Departure, cleared to switch.")

Other departure reports on the way ("passing 2.5", "arcing", "established outbound", "Kilo") get "Enfield one one, roger." from Departure. If you don't call "on top", the handover comes automatically at 20 NM. After a carrier launch, the Approach wheel entry **Airborne (after takeoff)** also reaches Departure.

### Formation flights and other traffic

- **Wingmen:** while flying in formation with their lead (airborne, not on the deck), in Case I wingmen share the lead's Marshal check-in and Charlie time. After that, each wingman flies their own break and groove and gets their own LSO calls and grade. If a wingman leaves the formation while holding, they get their own place in the stack. Case II/III approaches are flown one by one: when the lead checks in, each wingman gets their own Marshal assignment (next angels, marshal point 1 DME farther out, approach time 1 minute after the lead) and reports "commencing" themselves.
- **No check-in:** any friendly aircraft in the groove with its gear down gets the LSO ("Enfield one two, call the ball.", calls and grade), even without a check-in.
- **Mission AI:** mission AI aircraft that have a landing waypoint on the carrier make their own ball call ("Hornet ball, 5.4." → "Roger ball."). They stay silent while a player is in the groove. An AI aircraft that goes around three times is sent to the nearest airfield. This chatter follows `"AiChatter"` (default `true`).
- **Airspace watch:** if you fly toward a friendly carrier without checking in, Marshal warns you. This happens below 2500 ft within 5 NM while heading for the ship, or below 1500 ft in the recovery sector astern (±30° of the final bearing, up to 10 NM). For example: "Enfield one one, Marshal, you are approaching a warship, turn left heading 260, remain clear of the carrier by 5 miles." If nobody in your flight has talked to a controller yet, the call starts with "Unidentified aircraft …" and asks you to identify yourself. The call goes out on Guard if you aren't tuned to the carrier. Switch: `"AirspaceWatch"` (default `true`).

### Carrier phrase reference

| Phase | You say (recognised words) | Station replies |
|---|---|---|
| Check-in | "Marshal, Enfield 1-1, mother's 360 for 35, angels 18, state 6.2" (*Marshal / mother / checking in / inbound / recovery / state*) | Marshal: Case, BRC or final bearing, altimeter, angels, Charlie time or EAT (Case I: "…, report see me") |
| Case I holding | "see you at angels 2" | Marshal: "update state, switch Tower" |
| Case II/III holding | "see you at angels 6" (*see you at / established / holding / in the stack*) | Marshal: "roger" |
| Case II/III holding | readback "165 radial, 21 DME, angels 6" | Marshal: "readback correct" (or "negative, …" with the right value) |
| Holding | "state 4.5" | Marshal: "roger, state four point five" (or bingo signal) |
| Any time | "pigeons" / "TACAN" / "say position" | Marshal: bearing and distance to mother, BRC, TACAN, ICLS |
| Case I | (nothing, wait) | Tower: "signal Charlie" (or Marshal: "signal Delta, expected Charlie …"; deck delayed: "99, signal Delta") |
| Case I initial | (zip lip; "initial" only gives a text hint) | – (before Charlie: "negative, not Charlie, hold angels 2, expected Charlie …") |
| Case II/III | "commencing, angels 6, state 5.2" | Marshal: "radar contact, final bearing …, switch Approach" |
| Case II/III | "platform" | Approach: "Approach, radar contact, N miles" (later: "roger") |
| Case II/III | "up and on" / "down and left" / "on and on" … | Approach: "concur" or "disregard needles, …" |
| Case II | "see you at ten" | Approach: "roger, switch Tower" |
| Groove | "Hornet ball, 5.2" (*ball*) | Paddles: "Roger ball." (Case III: "Roger ball, 25 knots, axial.") |
| Groove | "Clara" (*Clara / Clarence*) | Paddles: "roger Clara, keep it coming." |
| Launch II/III | "airborne" | Departure: "radar contact, departure radial …" |
| Launch II/III | "passing 2.5" / "arcing" / "established outbound" / "Kilo" | Departure: "roger" |
| Launch II/III | "on top, angels 8" | Departure: "switch Overlord 251.5" (without AWACS: "cleared to switch") |
| Unclear call | – | Marshal: "Enfield one one, say again." (in the groove: no reply, except an unclear ball call, which counts) |

### Example: Case I recovery

```
You:      Marshal, Enfield 1 1, checking in, mother's 352 for 38, angels 18, state 6.4.
Marshal:  Enfield one one, Marshal, case one recovery, expected BRC three five four,
          altimeter two niner niner two, hold angels 2, expected Charlie one five, report see me.
          (text: hold overhead the ship at angels 2, left-hand circles)
You:      Enfield 1 1, see you at angels 2.
Marshal:  Enfield one one, update state, switch Tower.
You:      Enfield 1 1, 6.1.
Marshal:  Enfield one one, Marshal, roger, state six point one.
   ... holding overhead ...
Tower:    Enfield one one, signal Charlie.
          (text: descend to the initial: 3 NM astern, 800 ft, heading 354, 350 kt)
   ... initial, break, downwind, 180 – no radio (zip lip) ...
You:      Enfield 1 1, Hornet ball, 5.1.
Paddles:  Roger ball.
Paddles:  A little power.
Paddles:  Right for lineup.
   ... trap ...
          (text: LSO: OK 3-wire, groove 16 s, (LUL)IC (4 points))
```

### Example: Case III recovery

```
You:      Marshal, Enfield 1 1, checking in, mother's 170 for 45, angels 20, state 6.0.
Marshal:  Enfield one one, Marshal, case three recovery, CV-1 approach, expected final
          bearing three four five, altimeter two niner niner two. Marshal mother's
          one six five radial, 21 DME, angels 6. Expected approach time two two.
   ... holding at the marshal point ...
You:      Marshal, Enfield 1 1, commencing, angels 6, state 5.2.
Marshal:  Enfield one one, radar contact, final bearing three four five, switch Approach.
You:      Enfield 1 1, platform.
Approach: Enfield one one, Approach, radar contact, 19 miles.
Approach: Enfield one one, left of course, fly heading three five five.
Approach: Enfield one one, Approach, ACLS lock on, say needles.
You:      Enfield 1 1, up and on.
Approach: Enfield one one, concur.
Approach: Enfield one one, three miles, on glidepath, on course.
Approach: Enfield one one, two miles, below glidepath, on course.
Approach: Enfield one one, one and a half miles, on glidepath, on course.
Approach: Enfield one one, three quarter mile, call the ball.
You:      Enfield 1 1, Hornet ball, 4.6.
Paddles:  Roger ball, 25 knots, axial.
   ... touchdown, hook skips ...
Paddles:  Bolter, bolter, bolter.
          (text: LSO: B, groove 12 s (2.5 points))
Departure: Enfield one one, Departure, radar contact, turn left heading one six five,
          maintain 1200, downwind.
Approach: Enfield one one, Approach, turn left heading three four five, intercept final.
```

## AWACS, tankers and ranges

Besides the airfield controllers, DCS-ATC also provides the voice of the mission's **AWACS** (or of a ground radar acting as GCI), its **tankers** and a **bombing range**. It also provides a **crew chief** on the intercom and **radio calls from AI flights** during combat. DCS-ATC never spawns aircraft. It only talks for the AWACS and tankers that the mission designer placed. If a station isn't in the mission, nobody answers, and its entry stays hidden in the radio wheel.

### How to reach these stations

| Station | Who answers | Default frequency (`config.jsonc` → `Frequencies`) | When it exists |
|---|---|---|---|
| AWACS | Nearest friendly airborne E-3, E-2, A-50 or KJ-2000 in the mission | The mission's AWACS frequency if the AWACS group has one, otherwise `"AWACS": 251.5` | An AWACS of your side is airborne, or your side has an early-warning radar (GCI) |
| Tanker | Nearest compatible friendly tanker (KC-135, KC-135MPRS, KC-10, IL-78 …) | `"Tanker": 255.5` | A tanker of your side is airborne |
| Range | "Range Alpha" | `"Range": 267.5` | The mission has a trigger zone whose name starts with `Range` |

You can call a station in three ways:

- **SRS (voice):** tune the station's frequency and transmit. DCS-ATC routes each call by the frequency you are on.
- **Station name in your call:** on an airfield frequency, or with push-to-talk without SRS, a call that names the station still reaches it: *Overlord / AWACS / Magic / Moscow / Darkstar / Wizard / Focus*, *Texaco / Shell / Arco / Tanker* (also *refuel*), *Range Alpha / Range control*. Some words also go straight to AWACS: *picture*, *bogey dope*, *declare*, *spike*, *request sort*.
- **Radio wheel** or the F10 menu **ATC → AWACS / Tanker**. If the pilot voice is on (`"PilotVoice"`), your request is read out first, in the form *"Overlord, Enfield 1-1, request picture."*

Radio wheel entries:

| Submenu | Entries |
|---|---|
| Range | Check in · In hot · Off safe · Check out |
| AWACS | Check in · Picture · Bogey dope · Sort · Nearest tanker · Vector nearest airfield · Check out |
| Tanker | Request rejoin · Visual · Observation · Pre-contact · Refuel complete |

The centre of the wheel suggests the answer to whatever the controller just asked for. For example, after *"Report in hot"* it suggests **Range: in hot**, after *"cleared pre-contact"* it suggests **Tanker: pre contact**, and after *"report complete or request more"* it suggests **Tanker: complete**.

If you say *"radio check"* or *"how do you read"* to any of these stations, the reply is, for example, *"Enfield one one, Overlord, read you five."* If speech recognition was unsure what you said, the station answers *"say again"* and takes no action. A mumbled "in hot" never gets you "cleared hot".

### AWACS

#### Name and frequency

- **Callsign:** taken from the DCS callsign of the mission's AWACS (Magic, Overlord, Darkstar, Wizard, Focus …). A numeric callsign is read digit by digit ("two four seven"). If there is none, the AWACS is called **Overlord**.
- **GCI:** if your side has no AWACS but has an early-warning radar, a GCI answers instead: **Magic** for blue, **Moscow** for red.
- **What it can see:** only aircraft that your side's sensors have actually detected, within 250 NM of you. A contact that is detected but not identified is called a **bogey** and has no type.
- **Bogey, then hostile:** every new contact starts as a **bogey**. Once its type is known and the AWACS has tracked it for about 40 s (the ID criteria), or at once if it fires on your side, the AWACS declares it to everyone on the frequency: *"Overlord, north group, hostile."* A *declare* answers with the current state.
- **Handover after departure:** when an AWACS or GCI is up, the tower or departure controller hands you over with *"leaving control zone, contact Overlord 251.5."*

#### Check-in and check-out

The AWACS makes unprompted calls to you only after you have checked in. A check-in on the ground gets *"check in airborne"* and doesn't count. When you shut down after landing, your check-in is cleared. A touch-and-go keeps it.

| You say | AWACS answers |
|---|---|
| "Overlord, Enfield 1-1, **checking in**" (also *check in*, *with you*, *on station*; a fuel state may follow) | *"Enfield one one, Overlord, radar contact, picture, …"*. If a hot group is within 30 NM, the threat comes first: *"threat, group BRAA …"* |
| "Overlord, Enfield 1-1" (callsign only) | Not checked in yet: check-in with picture. Already checked in: *"go ahead."* |
| "**checking out**" / "check out" | *"copy checking out, good day."* No more unprompted calls after that. |

If you check in at low altitude right after takeoff, you hear "radar contact" exactly once. If you are checked in but the AWACS hasn't reported radar contact yet, it does so, with a picture, once you climb through about 3,000 ft above ground.

While you are checked in with AWACS, the airfield's zone guard only watches the traffic pattern and the runway.

#### Calls you can make

| You say (recognised words) | Answer |
|---|---|
| **picture** | Picture of the three nearest groups (format below). *"picture clean"* if there is nothing. |
| **bogey dope**, **braa**, **threat**, **nearest bandit**, **nearest group**, **snap** | Nearest group: *"Enfield one one, Overlord, group BRAA 354, 20, 20 thousand, hot, hostile, single, Fulcrum."* If there is nothing: *"clean."* |
| **declare** + position: *"declare bullseye 030 45"*, or *"declare 0 3 0 for 4 5"* (bearing/range from you) | Looks within 5 NM of that point and answers *"bullseye 030, 45, hostile, single, Fulcrum"*, *"friendly"*, *"furball"* (hostile and friendly mixed), *"neutral …"* or *"clean"*. |
| **declare** with no position, with a radar lock | Your locked target, within 3 NM and ±5,000 ft. Can also return *"bogey"* (an aircraft is there but the AWACS hasn't detected it). Works only for the player on the PC that runs DCS-ATC. |
| **declare** with no position and no lock | Nearest group, including neutral aircraft. |
| **spike** / **spiked 270** (magnetic bearing; you can speak the digits one by one) | *"spike 270, group BRAA …, hostile …"* or *"spike 270, nothing known."* If you give no bearing: *"say spike bearing."* |
| **sort**, **request sort**, **targeting**, **request target** | Targets per element, from left to right: *"Enfield one one, Overlord, target lead group BRAA … Enfield one three, target trail group BRAA …"*. A 2-ship gets one group and a 4-ship gets two. |
| **committing**, **targeting**, **targeted**, **engaged**, **engaging** (optionally with a group name such as *"north group"*, or a direction *north/south/east/west*) | Confirms that group with its BRAA, otherwise the nearest group: *"Enfield one one, Overlord, north group BRAA 354, 20, 20 thousand, hot."* If you name an unnamed group by its direction first, that name sticks. From then on the group is yours: no more threat calls for it, only short target updates at 20 and 10 NM. If another friendly flight (player or AI) already has that group, the AWACS refuses: *"Enfield one one, Overlord, skip it, north group Ford one targeted."* |
| **splash** / **splash 2** / **kill** | *"Overlord copies splash two, west group. East group remains."* (the group that was shot down, then what is left: one or two group names, or *"3 groups remain"*), or *"Overlord copies splash two, picture clean."* if nothing is left. Nobody repeats the *picture clean* later, also not after a friendly AI fighter's *"Overlord copies, splash, picture clean."* |
| **winchester**, **RTB**, **tally** | *"Overlord copies."* |
| **tanker**, **texaco**, **shell**, **arco** (e.g. *"request nearest tanker"*) | *"nearest tanker Texaco, bearing 084, 30 miles, angels 22, TACAN 52 X-ray, contact Texaco 255.5."* Other possible answers: *"no tanker airborne"*, or *"negative, no compatible tanker airborne"* if no tanker matches your aircraft (boom vs. basket). |
| **vector**, **home plate**, **bingo**, **nearest airfield**, **recovery**, **divert** (you can add an airfield name) | *"Kutaisi bears 245, 38 miles."* Only friendly or neutral airfields. If you name an airfield, you get that one, otherwise the nearest. |
| **mayday** / **pan pan** | *"roger mayday. Nearest friendly field Kutaisi, bearing 245, 38 miles, contact Kutaisi Approach two six three decimal zero. Say intentions."* DCS-ATC then selects that airfield for you, and its Approach controller takes over the emergency. |
| anything else | *"say again."* |

#### Picture format

- **BRAA** is given from your position: the magnetic bearing read digit by digit, the range in NM, the altitude ("20 thousand", "5 hundred"), then the aspect: **hot**, **flank** + direction, **beam** + direction, **drag** + direction.
- **Bullseye for everyone, BRAA for you:** the picture and situation updates (new group, split, combined, picture clean) go to everyone on the frequency in bullseye format with the track direction, *"Overlord, new group, bullseye 030, 45, 20 thousand, track south, hostile, single."* Calls meant for one flight (threat, bogey dope, snap, spike, commit, pop-up) stay in BRAA from you. Needs a bullseye in the mission, otherwise everything is BRAA. `"AwacsBullseye": false` (default `true`) switches back to BRAA from you for everything.
- **Groups:** contacts within 3 NM of each other form one group. The picture names up to three groups:
  - 1 group: *single group*
  - 2 groups: *azimuth* (side by side) or *range* (one behind the other)
  - 3 groups: *wall*, *ladder*, *vic* or *champagne*
  - more than 3: *"5 groups"*, followed by the three nearest
- **Group names** (north group, lead group, trail group, east lead group …) are used again in later calls until the next picture. They belong to your side, not to you: every player and every friendly AI flight on that AWACS uses the same name for the same group. The only group on the scope is the *single group*; a group that wasn't named in a picture gets a name from where it is relative to the others (*"north group"*) the first time it is called, and keeps it.
- **Fill-ins:** *hostile / bogey / neutral*; *single / two contacts / heavy, N contacts*; *fast* (600 kt or more) / *very fast* (over 900 kt); *high* (above 40,000 ft) / *low* (below 1,000 ft above ground). The type comes last: NATO names for eastern types (Flanker, Fulcrum, Fishbed …) and nicknames for western types (Hornet, Viper, Eagle …).
- **Who has which group:** if another friendly flight has a group, the picture, bogey dope and snap say so at the end, *"…, hostile, single, Fishbed, Ford one targeted."*
- **Short follow-up calls:** you hear the full fill-ins the first time a group is called to you, and in every picture and every answer to bogey dope, declare, snap and spike. Unprompted follow-ups (threat, commit, split …) give only name, position, altitude and aspect, *"threat, north group BRAA 354, 15, 20 thousand, hot."*, plus whatever has changed since (*"single"* after a kill, a new type, *"hostile"* after the declaration). The AWACS voice speaks briskly, so a follow-up call takes about 5 seconds.

#### Calls the AWACS makes on its own (only after check-in)

| Call | When |
|---|---|
| *"threat, group BRAA …, hot, hostile …"* | An **untargeted** group pointing at you (hot or flank) within 30 NM. Called again at each closer step (20/10/5 NM), and every 45 s while it stays hot within 10 NM. No threat calls for a group that you or another friendly flight has committed on or targeted. |
| *"commit east group, BRAA …"* / *"maintain CAP."* | The AWACS acts as air battle manager for your side: it shares out the hostile groups between the fighters on station, i.e. friendly AI fighters and players who have reported **on station** (a CAP tasking, e.g. *"Overlord, Enfield 1-1, on station"*). Each group that comes hot within 40 NM gets one flight, a heavy group (3 or more contacts) gets two; the AWACS picks the nearest flight with missiles (Fox 3 first) and fuel. The other flights hear *"maintain CAP."* An assignment stays until the group is shot down or fades, or the flight goes RTB, bingo or winchester. Without "on station" you get threat calls only and hear the assignments of the others. "RTB", "bingo", "off station" or "checking out" end the tasking. |
| *"east group BRAA 012, 19, 26 thousand, hot."* | Short target update on **your** group (committed or targeted) at 20 and 10 NM, without fill-ins. |
| *"reset."* / *"picture clean, reset."* | Your group is shot down or has faded: back to your CAP. About a minute later you can be given the next open group: *"recommit west group, BRAA …"* |
| *"Overlord, new group, bullseye …"* (to everyone) / *"pop-up group, BRAA …"* (to you) | A group is detected for the first time within `"AwacsNewGroupNm"` (default 200, range 40–200). It is a pop-up if it first appears within 30 NM of you. |
| *"Overlord, west group split, 2 groups azimuth 6, east group bullseye …, west group bullseye …"* / *"Overlord, north and south groups combined, bullseye …"* (to everyone) | After the change has been stable for 12 s, at most once a minute per group. |
| *"north group merged."* | A group within 3 NM of you and less than 5,000 ft above or below you. With the group name from the picture; an unnamed group is just *"merged."* |
| *"Overlord, west group faded, last known bullseye 336, 63."* (to everyone) | A group that was called is no longer detected for 30 s and wasn't shot down. If it comes back it is a *new group* (or *pop-up*) again. |
| *"Overlord, furball, bullseye 354, 20."* (to everyone; without a bullseye to you in BRAA) | A friendly fighter and a hostile group are within 5 NM of each other. Once per group every 2 minutes; while it lasts there are no single threat calls for that group. |
| *"Overlord, leaker, north group, bullseye 040, 20, 15 thousand, track east, hostile."* (to everyone) | A hostile group that nobody has targeted is past the CAP (closer to the nearest friendly airfield than every fighter on station) and is heading for that airfield. Once per group; only when there is a CAP. |
| *"Overlord, picture clean."* (to everyone) | There has been no group on the radar for 2 minutes. After that, every group counts as "new" again. Not if *picture clean* was just said after a splash. |

- **Urgent calls on Guard:** if you aren't tuned to the AWACS frequency, threat and merged calls are repeated on Guard (243.0 / 121.5), at most once a minute: *"Enfield one one, Overlord on guard, threat, … Contact Overlord 251.5."*
- **Several players on the same AWACS frequency:** a "new group" or "picture clean" call that another player just heard isn't repeated for you.
- **Flights with several players:** unprompted calls go to the flight lead.
- **Debriefing:** AWACS check-in, check-out, threat and merged calls are added to your debriefing, up to 10 lines.

#### Example

```
You:      Overlord, Enfield 1-1, on station, angels 22.
Overlord: Enfield one one, Overlord, radar contact, picture, 2 groups azimuth 10.
          East group, bullseye 040, 60, 26 thousand, track west, hostile, two contacts, Flanker.
          West group, bullseye 020, 58, 25 thousand, track south, hostile, single, Fulcrum.
...
Overlord: Ford one, Overlord, commit west group, BRAA 290, 35, 25 thousand, hot.
Overlord: Enfield one one, Overlord, commit east group, BRAA 012, 38, 26 thousand, hot.
You:      Overlord, Enfield 1-1, committing east group.
Overlord: Enfield one one, Overlord, east group BRAA 012, 36, 26 thousand, hot.
Overlord: Enfield one one, Overlord, east group BRAA 013, 19, 26 thousand, hot.
You:      Overlord, Enfield 1-1, splash two.
Overlord: Enfield one one, Overlord copies splash two.
Overlord: Enfield one one, Overlord, reset.
You:      Overlord, Enfield 1-1, bingo, request vectors to Kutaisi.
Overlord: Enfield one one, Overlord, Kutaisi bears 245, 62 miles.
```

### Tankers

#### Which tanker answers

- **Name:** the tanker's DCS callsign (Texaco, Shell, Arco …), not its group name.
- **Two tankers with the same name:** if two are airborne (e.g. Texaco 1-1 and Texaco 2-1), each is addressed with its number, *"Texaco two one"*. Each has its own queue and its own turn calls. Say *"Texaco 2-1, …"* to pick one.
- **Choice:** the tanker you name. If you don't name one, the tanker you are already working with, otherwise the nearest compatible one.
- **Boom or basket:** boom tankers (KC-135, KC-10) serve the F-16, F-15, F-4, A-10, B-1, B-52, C-17 and E-3. Basket tankers (KC-135MPRS, KC-10 with drogue and others) serve everything else.
- **No air refueling:** aircraft without a refueling probe or receptacle get *"negative, no compatible tanker"*. That includes helicopters, the MiG-15/19/21/29, F-5, F-86, Su-27, Su-25 (not the Su-25T), trainers and warbirds.

#### Who does the talking, basket and boom

**DCS-ATC's tanker** always does the talking, even when the mission has its own DCS tanker. It runs the whole sequence as described below. DCS's own tankers stay silent.

DCS still only operates the basket or boom through its own F-menu tanker dialog. So for **the player on the PC that runs DCS-ATC**, DCS-ATC presses the DCS radio menu for you in the background: *Intent to refuel* when you make your request, *Ready pre-contact* when you call "pre-contact". The DCS tanker doesn't answer out loud.

- With Easy Communication, DCS then tunes your radio to the tanker's mission frequency by itself. DCS-ATC's tanker also talks on that frequency. If the tanker shares it with an airfield (e.g. 261.0 = Senaki UHF), DCS-ATC doesn't switch you to that airfield.
- This uses `"CommsKey"` (the DCS radio menu key as a scan code, default `43`, which is `\` on US and `#` on German keyboards). With `0`, DCS-ATC doesn't press the menu.
- **Other players** in multiplayer operate the DCS tanker menu themselves.

#### Calls you can make

| You say (recognised words) | Tanker answers |
|---|---|
| **request rejoin**, *join*, *rendezvous*, *refuel*, *fuel*, *tank*, *gas*, *AAR*, *checking in* | Rendezvous: *"Enfield one one, Texaco, bearing 084, 30 miles, angels 22, track north, 280 knots, TACAN 52 X-ray. Expect the join in 5 minutes, 1000 feet below, report visual."* |
| **visual** (*tally*, *in sight*, *judy*), more than 8 NM away | *"continue, 20 miles, report visual."* |
| **visual**, within 8 NM | *"cleared to join, observation left wing, number 1."* If several aircraft are waiting: *"number 2. Enfield one two first, then Enfield one one."* |
| **observation** / **left wing** / **joined** | Once joined: *"cleared pre-contact."* If others are ahead: *"stabilize observation left wing, number 2."* Not yet cleared to join (within 2 NM): *"cleared to join, left observation, number 1."* |
| **pre-contact** (*precontact*, *in position*, *ready for contact*, *stabilized*, *ready to refuel*) | *"Enfield one one, cleared contact, basket ready."* or *"boom ready"*. More than 2 NM away: *"negative, bearing 084, 12 miles, report visual."* Not your turn yet: *"negative, hold observation left wing, number 2."* Not yet cleared to join: *"negative, not cleared pre-contact, cleared to join, left observation, number 1."* |
| **disconnect** | *"copy disconnect, move to the right wing."* |
| **right wing** | *"roger, report complete or request more."* |
| **complete** / *thanks* / *good day* / *departing* / *leaving* | *"total offload 5.4, cleared to depart, contact Overlord 251.5."* (offload in thousands of lb). Without AWACS: *"… frequency change approved."* |
| anything else | *"say again."* Other talk at the tanker, such as a fuel state, never resets your contact. |

#### What happens on the boom or basket

DCS-ATC reacts to the real DCS refueling contact:

- **Contact:** *"Contact."*
- **Disconnect before the tank is full** (falling off the basket): *"disconnect, return to pre-contact."* You keep your place in line, and if you reconnect within 2 minutes, the same refueling continues.
- **Disconnect with a full tank:** *"Enfield one one, disconnect, you received 6200 pounds. Clear right wing. Report complete, or pre-contact for more."* The next aircraft in line then gets its own *"Enfield one two, Texaco, cleared pre-contact."*
- **Order in line:** set by who reached the tanker first, not by who asked first.

#### Join vectors and coaching

- **Join vectors** (`"TankerVectors": true`, default): after your request, the tanker steers you onto an intercept course aimed 1,000 ft below it. For example: *"Enfield one one, Texaco, turn left heading 040 for the join, Texaco 11 o'clock, 25 miles, maintain angels 21."* The first vector comes 30 s after the request. After that you get a new one at 20, 10 and 5 NM, or when you are more than 20° off, at most every 30 s. At 5 NM: *"Enfield one one, Texaco 12 o'clock, 5 miles, report visual."* If you turn away, the vectors stop. With `false`, you only get the first bearing.
- **Coaching** behind the tanker:
  - *"slow closure."*
  - *"stabilize."*
  - Boom only: *"forward 80."* (feet)
- **Tanker turns:** sent once to all receivers: *"Texaco, turning right."*
- **Breakaway:** *"breakaway, breakaway, breakaway."* if you get too close, close too fast, or leave the refueling envelope while in contact. It is repeated every 15 s and counted in the debriefing. After a breakaway you are back in observation: no *"return to pre-contact"*, report *pre-contact* again for a new *"cleared contact"*.
- **Joining without clearance:** if you haven't asked for refueling and come within 3 NM and ±1,000 ft of a friendly tanker, you hear *"Enfield one one, Texaco, you are not cleared to join, remain 1000 feet below, report ready for rejoin."* This happens at most once every 3 minutes and goes into the debriefing as a deviation. It doesn't happen in these cases:
  - You join from behind on the tanker's heading.
  - Your flight lead is already cleared.
  - You stay in the block right after "complete".
- **Leaving the tanker:** if you fly more than 10 NM away, your refueling session ends.

**Contact and join only when cleared** (DCS-ATC tanker voice, see above):

- **Contact without "cleared contact":** *"Enfield one one, Texaco, you are not cleared contact, disconnect, return to left observation."* and a deviation. If another receiver is already in contact on that tanker, you get *"breakaway, breakaway, breakaway."* instead.
- **Too close before "cleared to join":** if you have asked for refueling but haven't been cleared to join yet, and you come within 1 NM and ±500 ft of the tanker: *"Enfield one one, Texaco, not cleared to join, remain 1000 feet below, report visual."* Still there 30 s later: deviation.

#### Example (multiplayer client, DCS-ATC tanker voice)

```
You:     Texaco, Enfield 1-1, request rejoin.
Texaco:  Enfield one one, Texaco, bearing 084, 30 miles, angels 22, track north, 280 knots, TACAN 52 X-ray.
         Expect the join in 5 minutes, 1000 feet below, report visual.
Texaco:  Enfield one one, Texaco, turn right heading 095 for the join, Texaco 1 o'clock, 20 miles, maintain angels 21.
Texaco:  Enfield one one, Texaco 12 o'clock, 5 miles, report visual.
You:     Texaco, Enfield 1-1, visual.
Texaco:  Enfield one one, Texaco, cleared to join, observation left wing, number 1.
You:     Texaco, Enfield 1-1, observation left wing.
Texaco:  Enfield one one, Texaco, cleared pre-contact.
You:     Texaco, Enfield 1-1, pre-contact.
Texaco:  Enfield one one, cleared contact, basket ready.
Texaco:  Contact.
Texaco:  Enfield one one, disconnect, you received 6200 pounds. Clear right wing. Report complete, or pre-contact for more.
You:     Texaco, Enfield 1-1, refuel complete.
Texaco:  Enfield one one, Texaco, total offload 6.2, cleared to depart, contact Overlord 251.5.
```

### Range

A range controller exists only when the mission has a trigger zone whose name starts with **Range**. It is called **Range Alpha**.

- **Targets:** enemy ground units inside the zone. DCS-ATC measures your hits but spawns nothing.
- **Scored weapons:** your bombs, rockets and air-to-ground missiles are measured against the nearest enemy ground unit in the zone. An impact within 500 m counts.
- **Clock position:** 12 o'clock is your heading at release, so 6 o'clock means short.

#### Calls you can make

| You say (recognised words) | Range answers |
|---|---|
| **checking in** (*check in*, *inbound*, *request entry*, *entering*, *with you*, *request range*) | *"Enfield one one, Range Alpha, altimeter two niner niner two, wind on target two seven zero at one zero, range is cold. Range bears one eight zero, 12 miles. Enter from the north, run-in headings one six zero to two zero zero, minimum altitude 1500 feet. Report IP."* If another aircraft is hot: *"…, one aircraft in."* instead of *"range is cold"*. |
| **IP inbound** (after the check-in) | *"Enfield one one, continue."* Before the check-in: *"negative, you are not checked in. Report check in."* |
| **in hot** (*rolling in*, *inbound hot*, *in from the north*, *in heading 360*, *in now*, *ready in*, or a call ending in "in") | *"cleared hot."* (see conditions below) |
| **in dry** | *"cleared dry."* Same conditions as "cleared hot" (below). A dry pass holds the range until you call *off* (or until it is released automatically like a hot pass), but it isn't scored. |
| **off** (*off safe*, *off west* …) | *"Enfield one one, pass 2, 3 impacts, best 12 meters at 6 o'clock, 2 gun hits. Report in hot or checking out."* If you weren't hot (or were dry): *"roger, range is cold."* With *hung ordnance* or *hot gun* in this or an earlier call: *"pass 2, copy hung ordnance, check switches safe, no further hot passes, report checking out."* |
| **winchester** | *"pass 2, …, check switches safe, report checking out."* |
| **checking out** (*check out*, *leaving*, *departing*, *good day*) | *"check switches safe. 2 passes, best impact 12 meters at 6 o'clock. Cleared off the range to the north, contact Overlord 251.5, good day."* With *hung ordnance* or *hot gun* in the call (or reported earlier on this visit): *"copy hung ordnance, check switches safe. … Cleared off the range to the north via the hung ordnance route, advise Approach, good day."* |
| anything else | *"say again. Report check in, in hot, off, or checking out."* |

Notes on the check-in answer:

- The altimeter setting comes from the nearest airfield with weather data. It is given in inHg for western types and in hPa (*"QNH one zero one three"*) for others (see `"AltimeterUnit"`).
- *Wind on target* comes from the same airfield. It is left out if there is no weather data, and below 3 kt you hear *"calm"*.
- Bearing, distance and entry direction are given only if you are more than 3 NM out. The run-in headings are the bearing to the range ±20°; closer in you get "run-in headings any".

**Conditions for "cleared hot":**

| Situation | Answer |
|---|---|
| Not checked in | *"negative, you are not checked in. Report check in."* |
| Hung ordnance or hot gun reported | *"negative, hung ordnance, report checking out."* |
| On the ground or more than 10 NM out | *"continue, report in inside 10 miles."* |
| Outside the zone and not pointing at it | *"continue dry."* |
| Another aircraft is hot or dry | *"continue, one aircraft in."* As soon as the range is free, you get *"cleared hot"* (or *"cleared dry"*) without calling again, if your position and heading still fit. |

#### Scoring and enforcement

- **Score call:** about 3 s after the last impact of a release you get one call:
  - *"Enfield one one, 12 meters at 6 o'clock."*
  - *"rockets, best 30 meters at 7 o'clock."*
  - *"direct hit."*
  - *"no score."* (no target within 500 m)
- **Kill:** a destroyed target gets *"good hits, target destroyed."* once per pass.
- **Release without clearance:** *"check fire, check fire, check fire. Release without clearance, switches safe."* This comes at most every 30 s, the pass isn't scored, and a deviation goes into the debriefing. What counts is whether you were cleared at the moment of release, not at impact. Long-falling weapons released while you were cleared still count.
- **Automatic end of a pass:**
  - Leaving the zone by more than 10 NM while hot: *"Range Alpha, leaving the range, range is cold, pass 1. Report in hot or checking out."*
  - No "off" for 60 s after the last impact (180 s without any impact), or 60 s while more than 3 NM out and heading away: first *"Enfield one one, Range Alpha, report off."* Still no "off" 30 s later: *"Range Alpha, assuming you are off, range is cold, pass 1. …"* and a deviation.
- **New visit:** each check-in starts again at pass 1. Landing and shutting down ends your check-in; a touch-and-go keeps it.
- **Debriefing:** each pass, and an overall total over all your visits.

#### Example

```
You:    Range Alpha, Enfield 1-1, checking in from the north, angels 10.
Range:  Enfield one one, Range Alpha, altimeter two niner niner two, wind on target two seven zero
        at one zero, range is cold. Range bears one eight zero, 9 miles. Enter from the north,
        run-in headings one six zero to two zero zero, minimum altitude 1500 feet. Report IP.
You:    Range Alpha, Enfield 1-1, IP inbound.
Range:  Enfield one one, continue.
You:    Range Alpha, Enfield 1-1, in hot.
Range:  Enfield one one, cleared hot.
Range:  Enfield one one, 12 meters at 6 o'clock.
You:    Enfield 1-1, off safe.
Range:  Enfield one one, pass 1, impact 12 meters at 6 o'clock. Report in hot or checking out.
You:    Range Alpha, Enfield 1-1, checking out.
Range:  Enfield one one, Range Alpha, check switches safe. 1 pass, best impact 12 meters at 6 o'clock.
        Cleared off the range to the north, contact Overlord 251.5, good day.
```

### AI flights on the radio

Friendly AI flights within 60 NM of a player on their side talk on the radio during combat. These are the mission's own flights, not airfield traffic and not your own AI wingmen. They speak on the tactical net, which is the AWACS frequency (from the mission or the config). Calls within their own flight (defending, spike, naked, hit, joker, ops check) go out only on that flight's frequency from the Mission Editor, and only if it differs from the AWACS frequency. If the mission's AWACS or GCI is up, it acknowledges.

| Event | AI call |
|---|---|
| Fighter flight levelled off (first time 60 s within ±500 ft above 10,000 ft; only with AWACS/GCI) | *"Overlord, Ford one, on station, bullseye 040, 35, angels 25."* → AWACS: *"Ford one, Overlord, picture clean."* (or *"picture, single group BRAA …"*) |
| Hostile group hot within 40 NM (the AWACS assigns: each group to the best free flight on station, a heavy group to two, no back-and-forth) | AWACS: *"Ford one, Overlord, commit north group, bullseye 040, 65, 23 thousand, hostile, two contacts, Flanker."* → *"Ford one, committing."* → *"Ford one, targeted north group."* (the location was already in the commit); with two or more contacts in that group then *"Ford one, sorted."* (each element has its own target). If another free group is hot within 40 NM, the second element takes it: *"Ford one one, targeted north group."* / *"Ford one two, targeted south group."* (in a four-ship *"Ford one three"*). Other flights near an assigned group: *"Dodge one, Overlord, maintain CAP."* → *"Dodge one."*; a flight that wants a taken group: *"skip it, north group Ford one targeted."* → *"Dodge one."*; group gone: *"Ford one, Overlord, reset."* (or *"picture clean, reset."*) → *"Ford one, resetting."*, back on station later *"recommit …"*. Group names are the AWACS names of your side (as in the picture); without one *"commit group, …"* and *"Ford one, targeted."*. Without AWACS the flight commits on its own: *"Ford one, committing."* → *"Ford one, targeted group bullseye …"*. Committing/targeted are dropped if they shoot first; bingo, winchester and RTB free the assignment. |
| Air-to-air missile launch | *"Viper one one, fox three, north group."* (fox one / fox two / fox three; the AWACS group name; with only one group just *"fox three."*; without a picture name the direction, *"fox three, north."*). Before the first shot at a group the flight has not already called targeted: *"Viper one, engaged north group."* as a call of its own (once per group in 3 min) · *"maddog."* (active missile with no target) |
| Air-to-ground launch | *"magnum"* (anti-radiation), *"bruiser"* (anti-ship), *"rifle"* (other missiles), *"bombs away"* · *"guns."* · *"shack."* (bomb hit on a ground target) |
| Missile goes active / is defeated / time is up | *"pitbull."* · *"trashed."* · *"timeout."* (expected time of flight over without a hit, roughly 5 s + 3 s per NM range at launch; then no "trashed" for that missile) · lead turns more than 120° away from the target within 2 min after the shot: *"Ford one, out south."* |
| Enemy radar locks them (radar warning, flight frequency) | air: *"Viper one two, spike two six four, Flanker."* (magnetic bearing, type if known) · surface: *"Viper one two, mud spike, SA-6, east."* · lock gone: *"naked."* (a lock that is gone before the call is said gives neither) |
| Missile fired at them | *"Viper one two, SAM west, defending."* / *"launch north, defending."* |
| Kill | *"Viper one one, splash two, Flanker."* → AWACS: *"Overlord copies, splash, picture clean."* |
| Hit | *"I'm hit."* · badly hit, on Guard: *"Mayday, mayday, mayday, Viper one two, hit, RTB, bullseye 084, 12."* |
| Shot down / ejection | *"one two is down, good chute, bullseye …"* or *"no chute"* · *"ejecting, ejecting."* · emergency beacon on Guard · 90 s later on Guard: *"Mayday …, on the ground, bullseye …, uninjured."* → AWACS: *"copy, SAR notified, monitor guard."* (this also happens when **you** eject) |
| Fuel / weapons | *"joker."* (below 35 %) · *"bingo, RTB."* (below 22 %) → AWACS: *"copy bingo."* · *"winchester."* |
| Every 15 min in the air (flight frequency; not right after their own shot) | *"Ford, ops check, one, six point two, four and two."* – *"two, five point nine, four and two."* (fuel in thousand lb, then Fox-3 and Fox-2) |
| You ask: *"Ford one, say status"* (or *"Ford one two, say status"* for one element; on the AWACS/tactical net) | tactical frequency: *"Ford one, engaged north group, six point two, four and two."* (engaged / targeted / on station, lowest fuel in the flight, Fox-3 and Fox-2 summed) |
| Heading home (after "on station": for 60 s more than 40 NM from the station, heading for the nearest friendly airfield and 20 NM closer to it; not after bingo or a shot in the last 5 min) | *"Overlord, Ford one, RTB."* → AWACS: *"Ford one, Overlord, copy."* |
| Merge | *"merged."* (within 3 NM and less than 5,000 ft altitude difference) |

**Settings** (`config.jsonc`, or "AI flights in combat" in the settings window):

| Key | Default | Meaning |
|---|---|---|
| `"AiFlightComms"` | `"voll"` | `"voll"` = all calls; `"taktisch"` = only fox, engaged, timeout, out, splash, trashed, bingo, defending, merged, down and Guard calls; `"aus"` = off |
| `"AiFlightRadiusNm"` | `60` | Only AI flights within this distance of a player on their side talk |
| `"AiOwnWingmen"` | `false` | Your own AI wingmen also talk (DCS already voices them) |

To keep the frequency usable, at most 10 low-priority and 16 normal calls are sent per minute on each frequency. Extra low-priority calls are dropped.

### Ground crew and crew chief

The ground crew is **off by default**. Turn it on in the F10 menu **ATC → Settings → Ground crew on/off** (*Show* displays the current state). To make it the default for all missions, set `crew = true` in `Saved Games\DCS\Scripts\DcsAtc\DcsAtcOptions.lua`. A single mission can override this in a *Mission Start* trigger with the action *DO SCRIPT* and the code `DCSATC_OPTIONS = { crew = true }`. The ground crew consists only of soldiers and vehicles; DCS-ATC still spawns no aircraft.

What happens:

- **Parked:** after you have stood still on a parking spot for 10 s, a crew chief appears front left and a fire guard front right. The crew chief talks to you **on the intercom** of your aircraft through SRS. Without SRS you only see the text.
- **After landing:** a fuel truck and an ammo truck drive up from a nearby free spot and stay for 4 minutes. They are scenery only. Refueling and rearming still go through the DCS ground crew (`\` → F8), and a text reminder tells you so.
- **Taxiing out:** the soldiers step back, the trucks drive off, and everything disappears.
- **Crash or heavily damaged landing:** a fire truck comes and leaves after 5 minutes.

| When | Crew chief (intercom) |
|---|---|
| Parked before the flight | *"Crew chief on the headset, ready when you are."* |
| Parked after landing | *"Welcome back! Chocks in, clear to shut down."* |
| Trucks arrive | *"Fuel and ammo trucks standing by."* |
| Ground approves startup | *"Clear on the left, clear on the right, fire guard posted. Cleared to start engines."* |
| Taxiing out | *"Chocks removed, pins pulled. You're clear of the area, have a good flight!"* |

The crew chief's voice is set in `config.jsonc` → `"Voices"` → `"Crew"` (default `"en_US-ryan-medium"`).

### Quick reference: phrases

| Station | Say | Gets you |
|---|---|---|
| AWACS | checking in · picture · bogey dope · declare [bullseye 030 45] · spiked 270 · request sort · committing north group · splash one · request nearest tanker · bingo, vectors to Kutaisi · mayday · checking out · Ford one, say status (AI flight) | check-in with picture · picture · nearest group BRAA · ID · spike source · targets per element · confirmation · copy · tanker position and TACAN · bearing and distance to the field · nearest field and handover · no more unprompted calls · the AI flight answers with its status |
| Tanker | request rejoin · visual · observation · pre-contact · disconnect · right wing · refuel complete | rendezvous · cleared to join / number · cleared pre-contact · cleared contact · move to the right wing · report complete or request more · total offload, cleared to depart |
| Range | checking in · in hot · in dry · off safe · winchester · checking out | range briefing · cleared hot · cleared dry · pass result · switches safe · summary and exit |

The settings for these stations (`AwacsBullseye`, `AwacsNewGroupNm`, `TankerVectors`, `CommsKey`, voices) are listed in the [config.jsonc reference](#configjsonc-reference).

## Multiplayer, dedicated server, custom frequencies and other maps

### How multiplayer works

DCS-ATC runs on **one** PC per session: the PC of whoever hosts the mission. Every controller runs there: Ground, Tower, Approach, ATIS, AWACS, tankers, range and carrier. All players in the mission, human or AI, are traffic for those controllers.

Who is host and who is client is decided automatically when the mission starts:

| You are… | DCS-ATC runs as | What it does |
|---|---|---|
| Single player | Host | Everything, for you |
| Hosting a multiplayer mission from the game | Host | Everything, for you and every other player |
| Joining someone else's server, with DCS-ATC installed | Client | Radio wheel only. Your requests go to the host. |
| Running a dedicated server (`DCS_server.exe`) | Host, in the background | Everything, for all players. Nobody sits at the server PC. |

You don't need to configure anything for this. DCS-ATC starts with every mission and picks its role.

### Host mode: every player gets their own controller sequence

When you host, DCS-ATC handles every human player in the mission separately:

- **Each player has their own sequence.** One player can be taxiing while another is on radar vectors and a third is in the overhead. Each player talks to the controllers of the airfield whose frequency they have tuned in SRS. If they have no airfield frequency tuned, they talk to the nearest airfield.
- **Players are traffic for each other.** Traffic calls, taxi conflicts ("give way to the FA-18C on your right"), landing sequence and pattern spacing include both other players and the mission's AI aircraft.
- **Flights form automatically.** Players in the same DCS group who are within 1 NM of each other fly as one flight, with the lead's sequence. The lead is the player with the lowest callsign. Ground, Tower and Approach then handle the flight as a unit. A wingman who moves more than 3 NM away gets their own sequence again.
- **Callsigns come from each player's DCS slot** (e.g. `Enfield11` becomes "Enfield 1-1"). "MyCallsign" in the settings only changes the host's own callsign. If several players sit in slots without a western callsign, the first one gets "Callsign" from config.jsonc and the others are called by their player name.
- **Text messages go to each player's own group**, so you only see controller text meant for your group (and only if you are tuned to that frequency). Voice goes out over SRS on the controller frequency, so you hear exactly what you would hear on that frequency in real life. That includes the calls to other players.
- **Controllers work only for their own side.** An airfield held by the enemy coalition has no controller for you.

Two requirements:

1. **Everyone uses the host's SRS server.** DCS-ATC starts the SRS server on the host PC itself and listens to it locally. Every player connects their SRS client to that server, using the host's IP address and the SRS port (default 5002).
2. **Only the host's DCS-ATC controls anything.** Settings in config.jsonc that change controller behaviour, such as frequencies, voices and AWACS options, and the own airfield frequencies in `frequencies.jsonc` are taken from the host PC.

DCS-ATC spawns no aircraft in multiplayer either. It only provides the radio for units that are in the mission, including AWACS, tankers and AI flights that the mission spawns later.

### Client mode: other players with their own DCS-ATC

If you join someone else's multiplayer mission and have DCS-ATC installed, DCS-ATC detects this at mission start and switches to client mode. A small window opens with this message:

```
DCS-ATC client: radio wheel with key ' (apostrophe, right of ;) (DCS in front). Requests go to the host via DCS.
Talk: just use SRS on the controller frequency. Keep this window open.
```

In client mode:

- **The radio wheel works.** Open it with your wheel key (default `'`, the apostrophe; DCS must be the active window). Select with `1`–`9`, go back with `Backspace` and close with `Esc`. Each request is sent to the host's DCS-ATC as a DCS chat message starting with `ATC>`. The host intercepts it, so nobody sees it in the chat. The host answers as if you had made the call yourself, over SRS and as text to your group.
- **Voice works as usual.** Press your SRS push-to-talk on the controller frequency and speak.
- **What clients don't get.** Your local DCS-ATC does not start SRS, does no speech recognition and runs no controllers. The ENTER suggestion in the wheel centre works only on the host, because the client has no flight state to base it on. The client wheel shows no frequencies, because only the host knows them (map and `frequencies.jsonc`). The client also gets no wheel text in the game (VR); that text comes from the host's DCS-ATC for its own aircraft only.
- **The host must run DCS-ATC.** If the host doesn't run it, nobody intercepts your `ATC>` requests and nothing answers.

### Players without DCS-ATC installed

Players don't need to install DCS-ATC at all. If the host (or the dedicated server) runs it, every player in the mission can use it like this:

- **Voice over SRS.** Connect to the host's SRS server, tune the controller frequency and talk, in English. Example:
  > "Kutaisi Ground, Enfield 1-2, request startup"
- **F10 menu.** `F10 -> ATC` has most of the requests of the radio wheel (Ground, Tower, Approach, General, emergency…). Every player gets their own `ATC` menu. If several players share one group, the `ATC` menu has a submenu for each player, named by callsign.
- **Controller text** appears in the game for your group, and the voice comes over SRS.

They don't get the radio wheel or the ENTER suggestion. Their F10 requests are still spoken by the pilot voice, like everyone else's.

If DCS-ATC isn't running on the host, the F10 menu replies:

> DCS-ATC is not running – Start menu -> Start DCS-ATC, then send again.

### Dedicated server, step by step

DCS-ATC can run on the PC of a DCS dedicated server (`DCS_server.exe`) without a player sitting at that PC.

> **Not tested on a real dedicated server yet.** The setup and the background mode are implemented, but nobody has confirmed them on a running server yet. Please report how it works for you.

**Before you install**

1. Install the DCS dedicated server. Start it **once** so that it creates its Saved Games folder: `Saved Games\DCS.server`, `DCS.dcs_serverrelease`, `DCS.release_server`, `DCS.openbeta_server`, or the name you pass with `-w <name>`.
2. Install SRS on the **same PC**. DCS-ATC starts the SRS server if it isn't running already. A command-line SRS server that is already listening on its port counts as running.
3. Install DCS-ATC **under the same Windows user account that runs `DCS_server.exe`**. DCS-ATC and the server exchange data through that user's temp folder, settings and registry. Under different user accounts they can't see each other.

**Setup**

4. Run `DCS-ATC-Setup.exe`. On the task page, tick **"Dedicated server"**:
   - On a PC with **only** a dedicated server (no `Saved Games\DCS`), the box is ticked for you, and setup installs only for the server.
   - On a PC that also has the game, tick the box yourself. The game installation is set up as usual as well.
5. Confirm the server's **Saved Games folder**. If you start the server with `-w <name>`, choose `Saved Games\<name>`.
6. If setup can't find the server's installation folder (the one that contains `bin\DCS_server.exe`), it asks you for it.
7. Setup puts the hook and the mission script into the server's Saved Games folder, under `Scripts\Hooks` and `Scripts\DcsAtc`. It also adds one line to the server's `Scripts\MissionScripting.lua`, which loads only the DCS-ATC script. The step that asks for the wheel and push-to-talk keys is skipped when no game is installed.

**Running**

8. Start the server and load any mission. DCS-ATC starts minimised with every server mission and runs in the background:
   - no radio wheel, overlay, keys or microphone, and no SRS client (only the SRS server)
   - no own aircraft: all players are traffic
   - controllers hear players over SRS and get their positions from the mission
   - text goes to each player's group in the game
9. Players connect SRS to the SRS server on the server PC and use DCS-ATC as described above: voice, the F10 menu, and the radio wheel for players who have DCS-ATC installed (client mode).

Background mode switches on automatically when `DCS_server.exe` runs **without** `DCS.exe` on the same PC. If you also run the game on the server PC, you can force background mode in config.jsonc:

```jsonc
"Dedicated": true,   // default false
```

On a dedicated server, these files apply to all players:

- `%LOCALAPPDATA%\Programs\DCS-ATC\config.jsonc` on the server PC: frequencies, voices, AWACS options
- `%LOCALAPPDATA%\Programs\DCS-ATC\frequencies.jsonc` on the server PC: own airfield frequencies
- `<server Saved Games>\Scripts\DcsAtc\DcsAtcOptions.lua`: default mission options (ground crew)

**After a DCS server update**

DCS resets `MissionScripting.lua`. To fix it, run **Start menu -> DCS-ATC -> "Repair after DCS update"**. This also repairs the server installation you chose during setup.

**Limitations**

- Not tested on a real dedicated server (see above).
- The SRS server must run on the same PC as DCS-ATC. DCS-ATC connects to it locally and can't use an SRS server on another machine.
- On the server, nobody is "you". Settings that only affect the host's own aircraft do nothing, such as "MyCallsign", the DCS radio menu key and runway lights via the menu.

### Custom frequencies per airfield

By default each airfield uses the frequency that DCS gives it on the map, the one shown on the F10 map. Ground, Tower and Approach share it, on UHF and VHF. You can give an airfield its own frequency for each controller in the file **`frequencies.jsonc`** in the DCS-ATC folder (`%LOCALAPPDATA%\Programs\DCS-ATC\frequencies.jsonc`, next to `config.jsonc`).

**The file**

- **DCS-ATC creates it for you** when it loads the airfields (at the first start with a mission, and on every map change). Setup doesn't install it, and uninstalling keeps it, like `config.jsonc`. On a server you can also create or update it without DCS running with `DcsAtc.exe --frequencies`.
- **It already lists every airfield of every installed map**, one section per map, with one commented-out line per airfield that shows the values DCS-ATC uses today (DCS map, ATIS and config.jsonc):

  ```jsonc
  {
    // ===== Caucasus =====
    // "Kutaisi": { "ATIS": 263.5, "Ground": [263.0, 134.0], "Tower": [263.0, 134.0], "Departure": [263.0, 134.0], "Approach": [263.0, 134.0] },   // id …
    // ===== Nevada =====
    "Nellis": { "ATIS": 270.1, "Ground": 275.8, "Tower": 327.0, "Departure": 273.55, "Approach": 379.0 },   // id …
  }
  ```

  To set your own frequencies, remove the `//` in front of an airfield line and change the numbers, as for Nellis above. The `// id …` comment at the end of a line is how DCS-ATC recognises the airfield when it adds new ones; leave it in place.
- **Airfields with only a VHF frequency** in DCS (most of the Persian Gulf map) show a list with one value, for example `"Tower": [119.2]`. Airfields without any DCS frequency show the `config.jsonc` frequencies (`"Ground": 264.5, "Tower": 265.0, "Departure": 266.5, "Approach": 266.5` by default).
- **New maps and airfields** are added as commented lines when they appear. Your own lines are never changed.

**Rules**

- **Controllers:** `ATIS`, `Ground`, `Tower`, `Departure`, `Approach`. Upper or lower case doesn't matter.
- **Values:** one frequency in MHz, or a list such as `"Ground": [275.8, 121.8]` (UHF and VHF). Every value must be between 30 and 400 MHz.
- **Airfield name:** as DCS names it. Case, hyphens, spaces and punctuation are ignored, so `"senaki kolkhi"` matches `Senaki-Kolkhi`. A unique part of the name is enough. If the part matches more than one airfield, the entry is ignored.
- **Map sections:** lines under `// ===== <map> =====` apply only on that map. Lines above the first section apply on every map.
- **Controllers you don't list stay on the map frequency.** If you don't list Departure, Approach handles departures as before.
- **Changes apply as soon as you save** the file. No restart is needed.
- **Invalid entries are ignored**, and each one gets a line in `atc-log.txt`, for example:
  ```
  [ATC] frequencies.jsonc: airfield "Groom Lake" not on this map – ignored
  [ATC] frequencies.jsonc: Nellis "Clearance": … – unknown controller (ATIS, Ground, Tower, Departure, Approach), ignored
  ```
  If the file isn't valid JSON at all, no own frequencies are used and `atc-log.txt` says `frequencies.jsonc: error (…) – no own frequencies`.
- **The same frequency at two airfields:** if one of your controller frequencies is also used at another airfield (as its own frequency or its map frequency), `atc-log.txt` warns you, for example `360.6 MHz at Nellis and Creech – a call on it reaches only Nellis, Creech not reachable on this frequency`.

**What changes with custom frequencies**

- **Calling a frequency reaches that controller.** A call on the Ground frequency goes to Ground and a call on the Tower frequency goes to Tower, whatever you say. On a shared map frequency, DCS-ATC still works out the controller from what you say.
- **Handoffs name the right frequency.** For example, Ground's taxi clearance ends with "Hold short runway zero three right, contact Nellis Tower three two seven decimal zero when ready for departure." Controllers who tell you that you called the wrong station also name the right frequency.
- **Departure becomes its own controller.** After takeoff Tower says "…contact Nellis Departure two seven three decimal five five". Departure then works you on 273.55, gives you "radar contact" and releases you at the edge of its airspace. Departure uses the Approach voice. An IFR clearance names "departure frequency two seven three decimal five five".
- **ATIS** transmits on its own frequency and names the Tower frequency.
- **The radio wheel, kneeboard and SRS listening** use the new frequencies. The wheel shows the Departure frequency while you are departing, and the ENTER suggestion is then labelled `Departure: …`.

Example exchange at Nellis with the file above:

```
(on 275.8)  Nellis Ground, Enfield 1-1, radio check
            Enfield one one, Nellis Ground, read you five.          ← Ground answers on 275.8
            …
            Ground: … Hold short runway zero three right, contact Nellis Tower three two seven decimal zero when ready for departure.
(on 327.0)  Nellis Tower, Enfield 1-1, holding short runway …, ready for departure
            …
            Tower: … contact Nellis Departure two seven three decimal five five
(on 273.55) Nellis Departure, Enfield 1-1, airborne …
            Enfield one one, Nellis Departure, radar contact …
```

**In multiplayer and on a dedicated server**, only the host's (or server's) `frequencies.jsonc` counts, and it applies to every player. A client's own file has no effect on the controllers.

**DCS's own built-in ATC** doesn't know about custom frequencies. DCS-ATC silences it at every airfield, so this matters only for features that rely on DCS itself: the runway-light request still checks whether you are tuned to the airfield's map frequency.

The older "Frequencies" (shared controller frequencies) and "AtisFreqs" (one ATIS frequency per airfield, needs a restart) settings in config.jsonc still work. For a single airfield, a `frequencies.jsonc` entry takes priority over both.

### Other maps than Caucasus

DCS-ATC works on any DCS map. It doesn't use a built-in airfield list. At mission start it reads the following from the mission and from your DCS installation:

- **Airfields and runways:** every DCS airfield on the map that has runways. FARPs and helipads are not included, and neither are airfields for which DCS reports no runways. Parking spots and coalition also come from DCS.
- **Frequencies and station names:** from the map's radio data in your DCS installation, the same frequencies as on the F10 map.
- **ILS, TACAN, VOR and NDB:** from the map's beacon data in your DCS installation.
- **Terrain, magnetic variation and local time** (for the Zulu time in the ATIS): from DCS and the map. On maps DCS-ATC doesn't know, the ATIS has no time.

DCS-ATC doesn't add any traffic of its own on any map.

#### Finding the exact airfield name

The generated lines in `frequencies.jsonc` already contain the airfield names. To recognise airfield names elsewhere, any of these show them:

- **The DCS-ATC window** (taskbar) lists all airfields when the mission starts:
  ```
  Airfields (12): Kutaisi, Nellis, North Las Vegas, Creech, …
  ```
- **`airfields.txt`** in `%LOCALAPPDATA%\Programs\DCS-ATC` holds the airfields of the last map you flew. Each airfield is a line starting with `A;`, for example `A;Anapa-Vityazevo;…`. The second field is the exact name.
- **The radio wheel:** General -> "Select airfield" lists the 8 nearest airfields.
- **The F10 menu:** `ATC -> Ground -> IFR clearance` lists up to 8 airfields of your side by name.
- **The F10 map** shows DCS's airfield names.
- **atc-log.txt:** if a `frequencies.jsonc` name matches more than one airfield, the log line lists the airfields it matched.

#### Limitations on other maps

- **Only Caucasus has been tested.** Every other map should work, but expect rough edges, and please report them.
- **Detailed procedures exist only for Kutaisi.** They cover reporting points, taxi routes with taxiway names ("taxi to holding point runway two five via November, Delta"), and arrival and departure routes from the chart. Every other airfield, on every map, is generic:
  - Taxi clearances name the runway but no taxiways, for example "taxi to holding point runway one three".
  - The control zone is 5 NM around the airfield, up to 3000 ft above it.
  - The traffic pattern is on the left, unless terrain forces it to the right.
- **Station names on some maps sound odd.** On maps such as the Persian Gulf, the DCS radio data only has ICAO codes or run-together names. DCS-ATC then builds the station name from the DCS airfield name, for example "Al Dhafra AB Tower". Speech recognition expects you to say that name too.
- **Speech recognition and unusual names:** long or non-English airfield names are harder to recognise. If in doubt, use the radio wheel or the F10 menu.
- **Your DCS installation must be found.** Frequencies, station names and navaids come from it. If DCS-ATC can't find it, `atc-log.txt` says "DCS installation not found: frequencies from fallback data", and the shared fallback frequencies from config.jsonc are used.

## Appendix: settings, logs and troubleshooting

### The settings window

Open it from **Start menu → DCS-ATC → Settings** or from **Radio wheel → General → Settings**. **Save** writes your changes to `config.jsonc` and keeps the comments in that file. In the window, `Enter` saves and `Esc` cancels. Fields marked `*` take effect the next time DCS-ATC starts. The other fields take effect while DCS-ATC is running.

| Field | Values | config.jsonc key |
|---|---|---|
| **Your callsign** | Empty uses the callsign from your DCS slot. Otherwise a name and two digits, e.g. `Viper 1-1` (`viper11` is accepted and normalised). | `MyCallsign` |
| **Interface language** `*` | Deutsch / English. Also switches the F10 menu language. | (language file) |
| **Controller volume** | 0–100 %, steps of 5 | `Volume` |
| **Speech pace** (1 = normal, higher = slower) | 0.70–1.50 | `SpeechRate` |
| **Pause between calls** | 0–5 s | `ReplyPause` |
| **Radio effect (noise, squelch)** | on/off | `RadioFx` |
| **Speak your wheel requests (pilot voice)** | on/off. Off sets the pilot voice to empty. | `PilotVoice` |
| **AWACS picture as bullseye (instead of BRAA)** | on/off | `AwacsBullseye` |
| **AWACS calls new groups within** | 40–200 NM | `AwacsNewGroupNm` |
| **Hear AI traffic on frequency** | on/off | `AiChatter` |
| **AI flights in combat** | full / tactical / off | `AiFlightComms` |
| **ATIS broadcast** | on/off | `Atis` |
| **Watch airspace (control zones, deviations, debriefing)** | on/off | `AirspaceWatch` |
| **Debug log for bug reports (atc-debug.txt)** | on/off | `DebugLog` |
| **Runway lights (at night)** | silent / with DCS controller (audible) / off. Takes effect the next time DCS-ATC starts. | `RunwayLights` |
| **Altimeter setting** | automatic (by aircraft type) / hPa (QNH) / inHg (altimeter) | `AltimeterUnit` |
| **Radio wheel in game (VR)** | automatic (when DCS is in VR mode) / always / off | `WheelInGame` |
| **Microphone (only without SRS)** `*` | Windows default or a specific device | `MicName` |
| **SRS folder** `*` | The path, with "…" to browse. It is checked for `ExternalAudio\DCS-SR-ExternalAudio.exe`. | `SrsPath` |
| **Set keys …** | Opens the key prompt (radio wheel, HOTAS, push-to-talk). | `WheelKey`, `PttKey`, … |

**All options (config.jsonc) …** opens the file in Notepad.

### config.jsonc reference

The file is in the installation folder: `%LOCALAPPDATA%\Programs\DCS-ATC\config.jsonc`.

- **Format.** It is JSON, with `//` comments allowed.
- **Updates.** Setup never overwrites an existing `config.jsonc`, so your values survive updates.
- **Missing keys** use the defaults below.
- **When changes apply.** DCS-ATC notices when the file is saved while it is running. Settings that the settings window does not mark with `*` apply at once (except `RunwayLights`). Keys, joystick buttons, microphone, SRS and frequencies under `Frequencies` apply at the next start.

**Keys and radio wheel**

| Key | Default | Meaning |
|---|---|---|
| `PttKey` | `163` (right Ctrl) | Push-to-talk key as a Windows virtual-key code, e.g. 165 = right Alt, 161 = right Shift, 112–123 = F1–F12. Used only when SRS is not connected. |
| `PttJoystick` | `-1` (off) | Joystick ID for push-to-talk. Find it with `DcsAtc.exe --buttons` or **Set keys**. |
| `PttButton` | `1` | Button number on that joystick. |
| `WheelKey` | `222` (`'` US / `Ä` German) | Opens and closes the radio wheel. `0` = no key. |
| `WheelJoystick` | `-1` (off) | Joystick ID for the wheel button. When it is set, the hat navigates the wheel. |
| `WheelButton` | `1` | Button number that opens the wheel. |
| `WheelSize` | `0.45` | Wheel size as a fraction of the DCS window height (0.25–0.95). `+`/`-` in the open wheel change it and save it. |
| `WheelInGame` | `"auto"` | Also show the wheel as text in the game: `"auto"` when DCS runs in VR (VR option or `--force_enable_VR`), `"on"`, `"off"`. |
| `CommsKey` | `43` (`\` US / `#` German) | Your DCS **communication menu** key as a keyboard scancode. DCS-ATC presses it to open DCS's own tanker menu (basket/boom) and, at dusk, at night or in poor visibility, to request runway lighting. `0` = off. |
| `MicName` | `""` | Part of the microphone name, e.g. `"FDUCE"`. Empty = Windows default. Used only without SRS. |

**Callsign and mode**

| Key | Default | Meaning |
|---|---|---|
| `Callsign` | `"Enfield 1-1"` | Used only without mission data, or for a slot without a western callsign. Normally the callsign comes from your DCS slot. |
| `MyCallsign` | `""` | Your own callsign instead of the slot callsign. `""` = from DCS. |
| `Mode` | `"host"` | `"client"` = radio wheel only, with requests sent to the host through DCS. DCS-ATC switches to client mode by itself when you join someone else's server. |
| `Dedicated` | `false` | Dedicated server with nobody at the PC: no wheel, keys, microphone or SRS client. Switches on automatically when `DCS_server.exe` runs without `DCS.exe`. |

**Frequencies and SRS**

| Key | Default | Meaning |
|---|---|---|
| `Frequencies` | `{ "ATIS": 263.5, "Ground": 264.5, "Tower": 265.0, "Approach": 266.5, "Range": 267.5, "AWACS": 251.5, "Tanker": 255.5, "Carrier": 127.5 }` | Station frequencies in MHz. **ATIS** is the shared ATIS frequency. **Range** is the range controller. **Tanker** is DCS-ATC's tanker radio. **AWACS** and **Carrier** are used only when the mission unit has no frequency of its own. **Ground / Tower / Approach** are fallbacks for airfields without DCS frequency data; normally every airfield uses its own map frequency. DCS-ATC warns in its window if a station frequency is the same as an airfield frequency. |
| `AtisFreqs` | `{}` | Own ATIS frequency per airfield, e.g. `{ "Kutaisi": 262.0 }`. Airfields not listed use `Frequencies.ATIS`. Applies after a restart. |
| `Modulation` | `"AM"` | Modulation of all DCS-ATC transmissions. |
| `Coalition` | `2` | SRS side (1 red, 2 blue) for stations without a side of their own. Airfields, AWACS, tankers and AI use their own side. |
| `SrsPath` | `"C:\\Program Files\\DCS-SimpleRadio-Standalone"` | SRS folder. If it is wrong, DCS-ATC uses the registry entry or the running SRS. |
| `SrsListen` | `true` | Hear all players' calls through SRS. With SRS connected, your own push-to-talk key is ignored. |
| `SrsAutoConnect` | `true` | Automatically connect an SRS client that isn't connected yet to the local SRS server. |
| `TelemetryPort` | `18500` | Local UDP port for your own aircraft's data. The export script sends to 18500, so leave this as it is. |

Own frequencies per airfield and controller are not in `config.jsonc` but in the separate file `frequencies.jsonc`; they apply as soon as you save it. See [Custom frequencies per airfield](#custom-frequencies-per-airfield).

**Voices and sound**

| Key | Default | Meaning |
|---|---|---|
| `Voices` | As installed: Tower `"en_US-ryan-medium\|en_US-ljspeech-medium"`, Ground `"en_US-bryce-medium"`, Approach `"en_US-ryan-medium@1.05"`, ATIS `"en_US-ryan-medium@0.94"`, Range `"en_US-ryan-medium@1.06"`, AWACS `"en_GB-alan-medium\|en_US-ljspeech-medium"`, Tanker `"en_US-bryce-medium"`, Crew `"en_US-ryan-medium"`. Carrier (added automatically) `"en_US-ryan-medium@0.97\|en_GB-alan-medium@1.02"` | Piper voice per controller, from the `models` folder. `@0.9` = lower pitch. If you give several voices separated by `\|`, each airfield or station is assigned one of them. More voices: huggingface.co/rhasspy/piper-voices. |
| `PilotVoice` | `"en_US-joe-medium"` | Speaks your wheel and F10 requests before the answer. `""` = off. |
| `RadioFx` | `true` | Radio sound: band-pass, light noise, squelch tail. |
| `Volume` | `0.6` | Controller volume in SRS (0–1). |
| `SpeechRate` | `0.85` | Speech pace: 1 = Piper standard, smaller = faster. |
| `ReplyPause` | `1.5` | Seconds between two calls on the same frequency. |

**Traffic, AWACS, tanker, carrier**

| Key | Default | Meaning |
|---|---|---|
| `AiChatter` | `true` | The mission's AI flights talk audibly with the airfield (request, answer, readback). |
| `AiFlightComms` | `"voll"` | AI flights in combat: `"voll"` (full: Fox, Splash, Tally …), `"taktisch"` (only fox, engaged, timeout, out, splash, trashed, bingo, defending, merged, down and Guard calls), `"aus"` (off). The values are German words; use them exactly as written. |
| `AiFlightRadiusNm` | `60` | AI combat chatter comes only from flights within this distance of a player of the same side. |
| `AiOwnWingmen` | `false` | Your own AI wingmen also talk. DCS already voices them. |
| `AwacsBullseye` | `true` | Picture and updates to everyone in bullseye format; `false` = everything in BRAA from you. |
| `AwacsNewGroupNm` | `200` | AWACS calls new groups (and "picture clean") within this range, 40–200 NM. |
| `TankerVectors` | `true` | The tanker gives join vectors (heading, distance, altitude) until you call "visual". `false` = only the first bearing. |
| `OnSpeedAoa` | `{ "FA-18": 8.1 }` | LSO: on-speed angle of attack in degrees per aircraft type prefix, used for "you're fast/slow". Other types get no speed calls. Derive values from the log line `[LSO] AoA im Mittel`. |

**Airfield behaviour and logging**

| Key | Default | Meaning |
|---|---|---|
| `Atis` | `true` | Continuous ATIS for airfields with a player within 40 NM. |
| `AirspaceWatch` | `true` | Watch control zones: entering without contact gets a call, and deviations such as a take-off without clearance are announced ("possible pilot deviation") and recorded for the debriefing. `false` = only safety warnings. |
| `RunwayLights` | `"silent"` | Runway lighting at dusk, at night or in poor visibility, through DCS's own ATC menu: `"silent"` (DCS controller stays muted), `"dcs"` (DCS controller audible for 15 s), `"off"`. |
| `AltimeterUnit` | `"auto"` | Altimeter setting in calls. `"auto"`: western types get "altimeter two niner niner two" (inHg), others "QNH one zero one three" (hPa); ATIS gives both. Or `"hpa"`, `"inhg"`. |
| `DebugLog` | `true` | Write `atc-debug.txt`. |

**Per-mission options.** Ground crew (fuel/ammo trucks, fire service) is a mission option, not part of `config.jsonc`:

- **In the game:** F10 → ATC → Settings.
- **Default for all missions:** `Saved Games\DCS\Scripts\DcsAtc\DcsAtcOptions.lua` (`crew = false`).
- **Different values for one mission:** in the Mission Editor, add a trigger "Mission Start" → DO SCRIPT: `DCSATC_OPTIONS = { crew = true }`.

### Log files

All logs are in `%LOCALAPPDATA%\Programs\DCS-ATC`:

| File | Content |
|---|---|
| `atc-log.txt` | Everything the DCS-ATC window shows, with timestamps: radio calls, recognised speech, warnings, ignored config entries. |
| `atc-debug.txt` | Detailed log for bug reports: speech recognition, airfield choice, flight phases, aircraft data every 10 s. Written only when `DebugLog` is on. At 20 MB it is moved to `atc-debug.old.txt`, so it uses at most 2 × 20 MB. |
| `crash-log.txt` | Error details if DCS-ATC crashes. DCS-ATC then restarts itself, unless it crashed within its first minute. |

When you report a bug, include the time, the airfield, a short description, `atc-log.txt` and `atc-debug.txt`.

### Troubleshooting

**No answer at all**
- **Is the DCS-ATC window open?** It starts with the mission. If it didn't, use Start menu → Start DCS-ATC. The message "DCS ATC is already running in another window" means a copy is already running.
- **Is your SRS client connected** to the SRS server on the PC running DCS-ATC, and is your radio set to the controller's frequency in AM? The radio wheel shows a station in red with ✗ when none of your radios is tuned to it.
- **Was your call understood?** Check `atc-log.txt` to see what was recognised.
  - Say the station, then your callsign, then the request.
  - Wait a moment after pressing PTT before you speak.
  - If you get "say again", repeat your call or use the radio wheel.
- **Text in the game but no voice?** If the DCS-ATC window shows "(SRS server not reachable)", the SRS server isn't running locally or is on another port.
- **No F10 → ATC menu and the wrong callsign?** DCS-ATC has no mission data, which means `MissionScripting.lua` isn't patched. See "After a DCS update" below.

**Wrong frequency**
- **Airfield request on a station frequency.** If you ask for taxi or landing on the AWACS, tanker, range or carrier frequency, you get a text hint instead of an answer. Example: "You are transmitting on AWACS 251.5. Kutaisi Ground: 263.0 / 134.0".
- **Another airfield's frequency.** A call on a different airfield's frequency goes to that airfield.
- **Talking to an airfield you are not near.** Use Radio wheel → General → Select airfield.
- **Frequency clash in your config.** If a station frequency in `config.jsonc` is the same as an airfield frequency, the DCS-ATC window shows a warning at start ("⚠ config.jsonc: … change the frequency"). Change the frequency, otherwise calls go to the airfield.
- **Own airfield frequencies don't work.** Check `atc-log.txt` for `frequencies.jsonc` lines (ignored entries, invalid JSON, a frequency used at two airfields).

**Easy Communication**
- DCS-ATC's radio doesn't depend on DCS's Easy Communication option.
- The option matters only for night runway lighting. With Easy Communication off, DCS answers only if your radio is tuned to the airfield frequency, so DCS-ATC requests the lights only in that case.
- If the lights never come on, check that `CommsKey` matches your DCS communication-menu key.

**SRS not found**
- **SRS doesn't start, or the window says "SRS-Server/SRS-Client not found in …":** set the SRS folder in the settings window. It must contain `ExternalAudio\DCS-SR-ExternalAudio.exe`. Any drive or folder works.
- **SRS client stays disconnected:** enter `127.0.0.1` and the server port (normally 5002) in the client and press Connect.

**Windows SmartScreen blocks the setup**
- Click **More info**, then **Run anyway**. The setup is not code-signed.

**After a DCS update**
- **Why it breaks.** DCS updates reset `Scripts\MissionScripting.lua`.
- **Automatic fix.** DCS-ATC checks the file while running and asks once for admin rights to fix it ("A DCS update removed the DCS-ATC line from MissionScripting.lua -> admin prompt (UAC). Restart the mission afterwards.").
- **Manual fix.** Use Start menu → DCS-ATC → **Repair after DCS update**.
- **Then restart the mission.**

**Radio wheel doesn't open**
- It opens only while DCS is the active window.
- In VR, use the in-game text wheel (`WheelInGame`).
- Check or change the key with **Set keys**.

**Answers are slow**
- Speech recognition runs on the CPU and needs a few seconds per call. The radio wheel is instant.
