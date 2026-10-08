# DCS-ATC (Alpha)

[![Downloads](https://img.shields.io/github/downloads/Iceman1991/DCS-ATC-Release/total?label=Downloads&color=2ea44f)](https://github.com/Iceman1991/DCS-ATC-Release/releases) [![Latest release](https://img.shields.io/github/v/release/Iceman1991/DCS-ATC-Release?label=Version)](https://github.com/Iceman1991/DCS-ATC-Release/releases/latest)

**Voice ATC for DCS World – every airfield on every DCS map (tested on Caucasus), fully offline.**
Talk to Ground, Tower, Approach, ATIS, AWACS and tankers over SRS in English; the controllers answer with their own voices. Speech recognition (Whisper) and voices (Piper) run on your PC – no cloud, no API keys. Works in any mission on any map (only Caucasus is tested so far), also multiplayer (host or dedicated server).

> ⚠️ **ALPHA** – early test version. Expect bugs (wrong or duplicate calls, odd headings, hangs). Use at your own risk, no warranty. Feedback and logs welcome.

**Goal: replace DCS's built-in radio** (airfield ATC, AWACS, tankers, Supercarrier comms) with controllers you can actually talk to.

📖 **[Manual (English)](docs/MANUAL.md)** · **[Handbuch (Deutsch)](docs/ANLEITUNG.md)** – installation, first flight, every radio call, carrier, AWACS/tankers, multiplayer, dedicated server, own frequencies, other maps.

## Features

- **Every airfield on every map** (tested on Caucasus), each on its own frequency – tune it and that airfield answers: ATIS, Ground, Tower, Departure, Approach. Runway by wind and terrain, ILS where available. Own frequencies per airfield in `frequencies.jsonc` (created automatically with every airfield of your maps).
- **Realistic military radio – you report, the controller reacts:** initial, overhead, base, final, ball call, commencing … If you don't report, the controller asks once, then the real consequence follows (go around, wave off, no clearance).
- **Ground:** startup, taxi, taxi conflicts ("hold position, give way"), taxi back to parking.
- **Tower:** takeoff/landing clearances, line up and wait, overhead break, straight-in, parallel runways with L/R (incl. crossing the parallel runway), pattern work (touch-and-go, low approach), landing sequence, wake turbulence.
- **Approach:** radar vectors with headings, step-downs and speeds around terrain, holding with altitude stack and EAT, traffic advisories and alerts, ILS/straight-in in bad weather, final approach monitoring.
- **Airspace:** control zone per airfield, calls only when you could get in the way of traffic, unknown aircraft on guard, zone transit, VFR flight following, deviations in the debriefing.
- **Emergencies:** mayday / pan-pan get priority, everyone else waits. **Landing grade** after every touchdown.
- **Carrier:** Marshal, Tower and LSO (Paddles) for carriers in the mission – Case I and Case III, marshal stack, final bearing, TACAN/ICLS, LSO calls, wave-off, bolter, grade.
- **AWACS** (of your mission, also when spawned later): check in/out, picture, bogey dope (BRAA), declare, sort, spiked, threat calls, nearest tanker, vector to nearest field.
- **Tankers** of your mission: rendezvous, sequence, pre-contact, contact/disconnect, refueling coaching.
- **AI on the radio:** the mission's AI traffic talks to ATC (DCS-ATC spawns no aircraft itself); AI flights call Fox, Splash, Defending, Bingo, Winchester and Guard emergencies.
- **Crew chief** on your SRS intercom, ground crew with fuel/ammo trucks and fire service.
- **Three ways to talk:** voice over SRS, radio wheel (keyboard or HOTAS), F10 menu. Own voice per controller, radio effects, text in game. Radio wheel also in VR (in-game text menu). The centre of the wheel shows what is expected now – ENTER sends it.
- **Multiplayer** (you host): every player has their own flow, other players count as traffic. Players with their own DCS-ATC get the radio wheel (client mode).
- **Dedicated server** (DCS_server.exe): runs in the background, players talk over SRS (not yet tested on a real server).

## Video

[![DCS-ATC video](https://img.youtube.com/vi/JDQSN2WmkFo/hqdefault.jpg)](https://youtu.be/JDQSN2WmkFo)

## Download

➡️ **[Download (latest release)](../../releases/latest)** – click the ZIP under "Assets".

## Install

1. Close DCS, unzip, run `DCS-ATC-Setup.exe` and pick English or German (no admin rights needed, one Windows prompt for `MissionScripting.lua`).
2. SmartScreen may warn because the setup is new and unsigned: *More info → Run anyway*. Windows Defender finds nothing; verify the file with the SHA256 checksum (`Get-FileHash`).
3. At the end: set keys for the radio wheel and push-to-talk, then the settings window opens (voices, speech speed, altimeter hPa/inHg, runway lights, AWACS range).
4. Done – DCS-ATC starts with every mission. Dedicated server: tick "Dedicated server" in the setup (see the manual).


## Requirements

- Windows 10/11 64-bit, DCS World (any map; tested on Caucasus)
- [SRS](https://github.com/ciribob/DCS-SimpleRadio-Standalone) in any folder or drive – found automatically, or choose it in the settings
- Microphone (radio calls in English), ~1.2 GB disk; speech recognition runs on the CPU
- English or German user interface – choose at the start of the setup

## Bug reports

Open an [issue](../../issues) with time, airfield, a short description and `atc-log.txt` plus `atc-debug.txt` from `%LOCALAPPDATA%\Programs\DCS-ATC`. A short video with SRS audio helps most.

## Technology and AI

**At runtime DCS-ATC uses exactly two AI models. Both run locally on your CPU, so no cloud, no API key, and nothing leaves your PC:**

| Purpose | Software | Model | License |
|---|---|---|---|
| Speech recognition (your voice → text) | [whisper.cpp](https://github.com/ggml-org/whisper.cpp) | OpenAI [Whisper](https://github.com/openai/whisper) `small.en` | MIT |
| Voices (text → speech) | [Piper](https://github.com/rhasspy/piper) with ONNX Runtime and espeak-ng | [piper-voices](https://huggingface.co/rhasspy/piper-voices): ryan, joe, alan, ljspeech, bryce | per voice; ryan is CC BY-NC-SA 4.0 (non-commercial) |
| Radio sound, audio | FFmpeg, NAudio | – | GPL-3.0, MIT |

**The controllers are not an AI chatbot (no LLM).** Every reply comes from fixed rules in the code, so the same situation always gets the same answer. The rules are built from real phraseology and procedures: FAA JO 7110.65, AIM, ICAO Doc 4444, CV NATOPS, ATP 1-02.1 brevity, ATP-56 and others. Speech recognition only turns your call into text; the rules then decide what the controller says.

**Development:** DCS-ATC is developed with an AI coding assistant ([Claude](https://claude.ai) by Anthropic), which helps write the code, tests and documentation. Ideas, decisions and the testing in DCS are mine.

## Licenses

Free, non-commercial (one bundled voice is CC BY-NC-SA 4.0). Bundles Whisper/whisper.cpp, Piper and voices from rhasspy/piper-voices, FFmpeg, espeak-ng, NAudio – see `THIRD-PARTY-NOTICES.txt` in the install folder.
Unofficial fan project, not affiliated with Eagle Dynamics or the SRS project.
