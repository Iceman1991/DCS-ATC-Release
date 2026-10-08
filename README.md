# DCS-ATC (Alpha)

[![Downloads](https://img.shields.io/github/downloads/Iceman1991/DCS-ATC-Release/total?label=Downloads&color=2ea44f)](https://github.com/Iceman1991/DCS-ATC-Release/releases) [![Latest release](https://img.shields.io/github/v/release/Iceman1991/DCS-ATC-Release?label=Version)](https://github.com/Iceman1991/DCS-ATC-Release/releases/latest)

**Voice ATC for DCS World – every airfield on every DCS map (tested on Caucasus), fully offline.**
Talk to Ground, Tower, Approach, ATIS, AWACS and tankers over SRS in English; the controllers answer with their own voices. Speech recognition (Whisper) and voices (Piper) run on your PC – no cloud, no API keys. Works in any mission on any map (only Caucasus is tested so far), also multiplayer (host).

**Sprechende Flugsicherung für DCS World – jeder Platz auf jeder DCS-Karte (getestet im Kaukasus), komplett offline.**
Funk über SRS auf Englisch mit Ground, Tower, Approach, ATIS, AWACS und Tankern; die Lotsen antworten mit eigener Stimme. Spracherkennung (Whisper) und Stimmen (Piper) laufen auf deinem PC – keine Cloud, keine API-Schlüssel. Jede Mission auf jeder Karte (bisher nur Kaukasus getestet), auch Mehrspieler (Host).

> ⚠️ **ALPHA** – early test version. Expect bugs (wrong or duplicate calls, odd headings, hangs). Use at your own risk, no warranty. Feedback and logs welcome.
> ⚠️ **ALPHA** – frühe Testversion. Es wird Fehler geben. Nutzung auf eigene Gefahr, ohne Gewähr. Rückmeldungen und Logs willkommen.

**Goal: replace DCS's built-in radio** (airfield ATC, AWACS, tankers, Supercarrier comms) with controllers you can actually talk to. / **Ziel: ersetzt den Standard-Funk von DCS.**

## Features

- **Every airfield on every map** (tested on Caucasus), each on its own frequency – tune it and that airfield answers: ATIS, Ground, Tower, Approach. Runway by wind and terrain, ILS where available.
- **Ground:** startup, taxi, taxi conflicts ("hold position, give way"), taxi back to parking.
- **Tower:** takeoff/landing clearances, line up and wait, overhead break, straight-in, pattern work (touch-and-go, low approach), landing sequence, wake turbulence.
- **Approach:** radar vectors with headings, step-downs and speeds around terrain, holding with altitude stack and EAT, traffic advisories and alerts, ILS/straight-in in bad weather, final approach monitoring.
- **Emergencies:** mayday / pan-pan get priority, everyone else waits. **Landing grade** after every touchdown.
- **Carrier:** Marshal, Tower and LSO (Paddles) for carriers in the mission – Case I and Case III, marshal stack, final bearing, TACAN/ICLS, LSO calls, wave-off, bolter, grade.
- **AWACS** (of your mission, also when spawned later): check in/out, picture, bogey dope (BRAA), declare, sort, spiked, threat calls, nearest tanker, vector to nearest field.
- **Tankers** of your mission: rendezvous, sequence, pre-contact, contact/disconnect, refueling coaching.
- **AI on the radio:** the mission's AI traffic talks to ATC (DCS-ATC spawns no aircraft itself); AI flights call Fox, Splash, Defending, Bingo, Winchester and Guard emergencies.
- **Crew chief** on your SRS intercom, ground crew with fuel/ammo trucks and fire service.
- **Three ways to talk:** voice over SRS, radio wheel (keyboard or HOTAS), F10 menu. Own voice per controller, radio effects, text in game. Radio wheel also in VR (in-game text menu).
- **Multiplayer** (you host): every player has their own flow, other players count as traffic.

## Video

[![DCS-ATC video](https://img.youtube.com/vi/JDQSN2WmkFo/hqdefault.jpg)](https://youtu.be/JDQSN2WmkFo)

## Download

➡️ **[Download (latest release)](../../releases/latest)** – click the ZIP under "Assets" / unter „Assets“ die ZIP anklicken.

## Install / Installation

1. Close DCS, unzip, run `DCS-ATC-Setup.exe` and pick English or German (no admin rights needed, one Windows prompt for `MissionScripting.lua`).
2. SmartScreen may warn because the setup is new and unsigned: *More info → Run anyway*. Windows Defender finds nothing; verify the file with the SHA256 checksum (`Get-FileHash`).
3. At the end: set keys for the radio wheel and push-to-talk, then the settings window opens (voices, speech speed, altimeter hPa/inHg, runway lights, AWACS range).
4. Done – DCS-ATC starts with every mission.

---

1. DCS beenden, ZIP entpacken, `DCS-ATC-Setup.exe` starten und Deutsch oder Englisch wählen (keine Adminrechte, eine Windows-Abfrage für `MissionScripting.lua`).
2. SmartScreen kann warnen (neu, nicht signiert): *Weitere Informationen → Trotzdem ausführen*. Defender findet nichts; Prüfsumme mit `Get-FileHash` prüfen.
3. Am Ende Tasten für Funkrad und Push-to-Talk festlegen, danach öffnen sich die Einstellungen (Stimmen, Sprechtempo, Höhenmesser hPa/inHg, Platzbefeuerung, AWACS-Reichweite).
4. Fertig – DCS-ATC startet mit jeder Mission.

## Requirements / Voraussetzungen

- Windows 10/11 64-bit, DCS World (any map; tested on Caucasus)
- [SRS](https://github.com/ciribob/DCS-SimpleRadio-Standalone) in its default folder `C:\Program Files\DCS-SimpleRadio-Standalone`
- Microphone (radio calls in English), ~1.2 GB disk; speech recognition runs on the CPU
- English or German user interface – choose at the start of the setup / Oberfläche Englisch oder Deutsch – Auswahl am Anfang des Setups

## Bug reports / Fehler melden

Open an [issue](../../issues) with time, airfield, a short description and `atc-log.txt` from `%LOCALAPPDATA%\Programs\DCS-ATC`. A short video with SRS audio helps most.

## Licenses / Lizenzen

Free, non-commercial (one bundled voice is CC BY-NC-SA 4.0). Bundles Whisper/whisper.cpp, Piper and voices from rhasspy/piper-voices, FFmpeg, espeak-ng, NAudio – see `THIRD-PARTY-NOTICES.txt` in the install folder.
Unofficial fan project, not affiliated with Eagle Dynamics or the SRS project.
