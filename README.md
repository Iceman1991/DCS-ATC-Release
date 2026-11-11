# DCS-ATC (Alpha)

[![Downloads](https://img.shields.io/github/downloads/Iceman1991/DCS-ATC-Release/total?label=Downloads&color=2ea44f)](https://github.com/Iceman1991/DCS-ATC-Release/releases) [![Latest release](https://img.shields.io/github/v/release/Iceman1991/DCS-ATC-Release?label=Version)](https://github.com/Iceman1991/DCS-ATC-Release/releases/latest) [![License](https://img.shields.io/badge/License-GPL--3.0-blue)](LICENSE)

Voice ATC for DCS World. You talk to Ground, Tower, Approach, ATIS, AWACS, tankers and JTACs over SRS in English and they answer with their own voices. Speech recognition (Whisper) and the voices (Piper) run locally on your PC, nothing goes to the internet. Works on any map in any mission, single player and multiplayer (host or dedicated server). So far only tested on Caucasus.

**Alpha:** early test version. Expect bugs (wrong or double calls, odd headings, hangs). Use at your own risk. Feedback and logs are welcome.

The goal is to replace the built-in DCS radio (airfield ATC, AWACS, tankers, Supercarrier) with controllers you can actually talk to.

Manual: [English](docs/MANUAL.md) · [Deutsch](docs/ANLEITUNG.md) (installation, first flight, all radio calls, carrier, AWACS/tankers, multiplayer, dedicated server, own frequencies, other maps)

## Supporters

Special thanks to my supporters:

| **Heling** | **buur** | **Pascali Alessandro** |
|:---:|:---:|:---:|

DCS-ATC is free and stays free. If you want to support it too:

[![Buy me a coffee on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/christianwagner)

More under [Support](#support).

## Features

- Every airfield on the map, each on its own frequency. Tune it and that airfield answers (ATIS, Ground, Tower, Departure, Approach). Runway by wind and terrain, ILS where available. Own frequencies per airfield go in `frequencies.jsonc`, which is created automatically.
- Military radio procedures: you report (initial, overhead, base, final, ball, commencing ...), the controller reacts. If you don't report, he asks once and then the real consequence follows (go around, wave off, no clearance).
- Ground: startup, taxi, taxi conflicts ("hold position, give way"), taxi back to parking.
- Tower: takeoff and landing clearances, line up and wait, overhead break, straight-in, parallel runways L/R, pattern work (touch and go, low approach), landing sequence, wake turbulence.
- Approach: radar vectors with headings, step-downs and speeds around terrain, holding with altitude stack and EAT, traffic advisories, ILS/straight-in in bad weather, final approach monitoring.
- Airspace: control zone per airfield, unknown aircraft are called on guard, zone transit, VFR flight following, deviations show up in the debriefing.
- Center and GCI: en-route control with radar contact, altitude clearances and handoff to Approach, own sectors and GCI stations in `frequencies.jsonc`.
- Own procedures per airfield in `charts.jsonc`, on every map: control zone, pattern altitude, reporting points, arrival and departure routes with named fixes, preferred parallel runway.
- Helicopters: direct to the field and landing at the ramp, air taxi, no runway pattern unless you ask for it. CSAR: downed pilots become survivors, the AWACS calls the helicopters.
- Transponder from SRS (squawk, ident, 7700), NOTAMs in the ATIS, runway selection and Approach.
- Emergencies: mayday and pan-pan get priority, everyone else waits. Landing grade after every touchdown.
- Debriefing after every flight, logbook with flight time and landings, checkride with an instructor on the intercom.
- Carrier: Marshal, Tower and LSO for carriers in the mission. Case I and Case III, marshal stack, final bearing, TACAN/ICLS, LSO calls, wave-off, bolter, grade.
- AWACS of your mission: check in/out, picture, bogey dope, declare, sort, spiked, threat calls, nearest tanker, vector to the nearest field.
- Air combat with the AWACS (ATP 1-02.1 brevity): it commits flights, assigns targets, skip it / reset / recommit, leakers, splash. AI fighters fly what it says, check in and out and land under DCS-ATC afterwards. You can answer with unable, fox, defensive, request support, winchester or say again, by voice or radio wheel.
- Tankers of your mission: rendezvous, sequence, pre-contact, contact/disconnect, refueling help.
- JTAC or FAC(A) of your mission: check-in, 9-line with readback, target mark (smoke, white phosphorus, laser), cleared hot with type 1, 2 or 3 control, BDA, check fire.
- AI traffic of the mission talks to ATC (DCS-ATC doesn't spawn aircraft itself). AI flights call Fox, Splash, Defending, Bingo, Winchester and emergencies on Guard.
- Your AI wingmen take orders by voice: "Two, go trail", "Flight, engage bandits", rejoin, cover me, RTB.
- Crew chief on the SRS intercom, ground crew with fuel/ammo trucks and fire service.
- Three ways to talk: voice over SRS, radio wheel (keyboard or HOTAS) and F10 menu. The radio wheel also works in VR (as in-game text). The middle of the wheel shows what's expected next, ENTER sends it.
- Multiplayer (you host): every player gets their own flow, the others count as traffic. Players who have DCS-ATC installed get the radio wheel too (client mode).
- Dedicated server (DCS_server.exe): runs in the background, players talk over SRS. Not tested on a real server yet.
- Situation map in the browser (`http://localhost:18600/`) for a second monitor: airfields, traffic and your controller phase.
- Settings window with one page per topic and a search field: voices, radio, AWACS, frequencies, keys, SRS, modules.
- Update from the app: the settings window shows a newer release and installs it on a button. Only the app is downloaded (about 70 MB) when models and voices are already there.
- Works next to DCS Retribution and DCS Liberation, which replace `MissionScripting.lua` while they run.

## Video

[![DCS-ATC video](https://img.youtube.com/vi/JDQSN2WmkFo/hqdefault.jpg)](https://youtu.be/JDQSN2WmkFo)

## Download

[Latest release](../../releases/latest), `DCS-ATC.zip` under "Assets" (about 850 MB). `DCS-ATC-Update.zip` and `manifest.json` are for the update from the app, not for a new install.

## Install

1. Close DCS, unzip, run `DCS-ATC-Setup.exe` and pick English or German. No admin rights needed, only one Windows prompt for `MissionScripting.lua`.
2. SmartScreen may complain because the setup is new and not signed: More info, then Run anyway. You can check the file against the SHA256 in the release (`Get-FileHash`).
3. At the end you set the keys for the radio wheel and push-to-talk, then the settings window opens (voices, speech speed, altimeter hPa/inHg, runway lights, AWACS range).
4. That's it, DCS-ATC starts with every mission. For a dedicated server tick "Dedicated server" in the setup (see manual). With "Custom" you can leave out modules you don't need (air traffic control, Center, Range, AWACS, Tanker, JTAC, Carrier, AI radio, ground crew), for example when a real player runs AWACS or Tower.

Update: settings window, page General, group "Update": "Check for updates", then "Update now". Not while DCS is running. Your `config.jsonc` and keys stay.

## Requirements

- Windows 10/11 64-bit, DCS World
- [SRS](https://github.com/ciribob/DCS-SimpleRadio-Standalone), any folder or drive. It's found automatically or you pick it in the settings.
- Microphone (radio calls in English), about 1.2 GB disk space. Speech recognition runs on a dedicated graphics card (Vulkan: NVIDIA, AMD or Intel) when there is one, otherwise on the CPU.
- User interface in English or German, chosen at the start of the setup

## Bug reports

Open an [issue](../../issues) with time, airfield, a short description and `atc-log.txt` plus `atc-debug.txt` from `%LOCALAPPDATA%\Programs\DCS-ATC`. A short video with SRS audio helps a lot.

## Technology

Two models are used while running, both locally on your PC:

| Purpose | Software | Model | License |
|---|---|---|---|
| Speech recognition | [whisper.cpp](https://github.com/ggml-org/whisper.cpp) | OpenAI [Whisper](https://github.com/openai/whisper) `small.en` | MIT |
| Voices | [Piper](https://github.com/rhasspy/piper) with ONNX Runtime and espeak-ng | [piper-voices](https://huggingface.co/rhasspy/piper-voices): ryan, joe, alan, ljspeech, bryce | per voice, ryan is CC BY-NC-SA 4.0 (non-commercial) |
| Radio sound, audio | FFmpeg, NAudio | | GPL-3.0, MIT |

The controllers are not a chatbot and there is no LLM. Every reply comes from fixed rules in the code, so the same situation always gets the same answer. The rules follow real phraseology and procedures (FAA JO 7110.65, AIM, ICAO Doc 4444, CV NATOPS, ATP 1-02.1, ATP-56 and others). Speech recognition only turns your call into text.

Development: DCS-ATC is developed with an AI coding assistant ([Claude](https://claude.ai) by Anthropic) that helps with code, tests and documentation. Ideas, decisions and testing in DCS are mine.

## Source code

The app is C# (.NET 8, `src/DcsAtc`), the DCS hook and mission scripts are Lua (`mod/`, `hooks/`, `mission/`, `export/`), plus tests (`tests/`) and the installer (`installer/`).

- Build: .NET 8 SDK, `dotnet build src/DcsAtc -c Release`
- Self-tests without DCS: `DcsAtc.exe --selftest`, `--anflugtest`, `--mptest`
- Installer: `installer\build.cmd` (Inno Setup 6, FFmpeg in PATH) builds the full setup, the small update setup and `manifest.json`. It also needs the Whisper model and Piper voices in `models\` and whisper.cpp/Piper in `tools\`. They are not in this repository because of their size, sources are listed in `installer/THIRD-PARTY-NOTICES.txt`, the layout is in `installer/DcsAtc.iss`.

## License

GNU GPL 3.0, see [LICENSE](LICENSE). Bundled third-party software keeps its own license (Whisper/whisper.cpp, Piper and the piper-voices, FFmpeg, espeak-ng, NAudio), see `installer/THIRD-PARTY-NOTICES.txt`.
Unofficial fan project, not affiliated with Eagle Dynamics or the SRS project.

## Support

DCS-ATC is free and will always stay free. No paid version, no features behind a paywall.

If you want to support the project anyway, there is a small Ko-fi page:

[![Buy me a coffee on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/christianwagner)

**Heling**, **buur** and **Pascali Alessandro** have already done that. Special thanks to you.

There is no obligation. That said, I appreciate every tip, big or small. Bug reports, logs and ideas help the project just as much. Either way, I will keep working on making our shared hobby a bit better.
