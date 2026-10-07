# DCS-ATC (Alpha)

**Voice ATC for DCS World – all 21 Caucasus airfields, fully offline.**
Talk to Ground, Tower, Approach, ATIS, AWACS and tankers over SRS in English; the controllers answer with their own voices. Speech recognition (Whisper) and voices (Piper) run on your PC – no cloud, no API keys. Works in any Caucasus mission, also multiplayer (host).

**Sprechende Flugsicherung für DCS World – alle 21 Kaukasus-Plätze, komplett offline.**
Funk über SRS auf Englisch mit Ground, Tower, Approach, ATIS, AWACS und Tankern; die Lotsen antworten mit eigener Stimme. Spracherkennung (Whisper) und Stimmen (Piper) laufen auf deinem PC – keine Cloud, keine API-Schlüssel. Jede Kaukasus-Mission, auch Mehrspieler (Host).

> ⚠️ **ALPHA** – early test version. Expect bugs (wrong or duplicate calls, odd headings, hangs). Use at your own risk, no warranty. Feedback and logs welcome.
> ⚠️ **ALPHA** – frühe Testversion. Es wird Fehler geben. Nutzung auf eigene Gefahr, ohne Gewähr. Rückmeldungen und Logs willkommen.

## Video

[![DCS-ATC video](https://img.youtube.com/vi/JDQSN2WmkFo/hqdefault.jpg)](https://youtu.be/JDQSN2WmkFo)

## Download

➡️ **[Download (latest release)](../../releases/latest)** – click the ZIP under "Assets" / unter „Assets“ die ZIP anklicken.

## Install / Installation

1. Close DCS, unzip, run `DCS-ATC-Setup.exe` (no admin rights needed, one Windows prompt for `MissionScripting.lua`).
2. SmartScreen may warn because the setup is new and unsigned: *More info → Run anyway*. Windows Defender finds nothing; verify the file with the SHA256 checksum (`Get-FileHash`).
3. At the end: set keys for the radio wheel and push-to-talk.
4. Done – DCS-ATC starts with every mission.

---

1. DCS beenden, ZIP entpacken, `DCS-ATC-Setup.exe` starten (keine Adminrechte, eine Windows-Abfrage für `MissionScripting.lua`).
2. SmartScreen kann warnen (neu, nicht signiert): *Weitere Informationen → Trotzdem ausführen*. Defender findet nichts; Prüfsumme mit `Get-FileHash` prüfen.
3. Am Ende Tasten für Funkrad und Push-to-Talk festlegen.
4. Fertig – DCS-ATC startet mit jeder Mission.

## Requirements / Voraussetzungen

- Windows 10/11 64-bit, DCS World with the Caucasus map
- [SRS](https://github.com/ciribob/DCS-SimpleRadio-Standalone) in its default folder `C:\Program Files\DCS-SimpleRadio-Standalone`
- Microphone (radio calls in English), ~1.2 GB disk; speech recognition runs on the CPU
- Program menus and readme are German for now – English UI comes with the next version / Menüs und Anleitung vorerst auf Deutsch, Englisch folgt

## Bug reports / Fehler melden

Open an [issue](../../issues) with time, airfield, a short description and `atc-log.txt` from `%LOCALAPPDATA%\Programs\DCS-ATC`. A short video with SRS audio helps most.

## Licenses / Lizenzen

Free, non-commercial (one bundled voice is CC BY-NC-SA 4.0). Bundles Whisper/whisper.cpp, Piper and voices from rhasspy/piper-voices, FFmpeg, espeak-ng, NAudio – see `THIRD-PARTY-NOTICES.txt` in the install folder.
Unofficial fan project, not affiliated with Eagle Dynamics or the SRS project.
