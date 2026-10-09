DCS-ATC 0.9.9 ALPHA – talking air traffic control for DCS World (all maps, tested on Caucasus)
========================================================================

!! ALPHA VERSION !!
  Early test version: there will be bugs (wrong or duplicate radio calls, odd headings, hangs).
  Use at your own risk, no warranty. Best back up Saved Games\DCS\Scripts first.
  Please report bugs with time, airfield, a short description and the files atc-log.txt and atc-debug.txt
  (in the installation folder: %LOCALAPPDATA%\Programs\DCS-ATC).
  Unofficial fan project, not affiliated with Eagle Dynamics or the SRS project.

What it does
  Tower, Ground, Approach, ATIS, AWACS, tanker and crew chief talk to you over SRS – at every airfield
  of every DCS map, completely offline (speech recognition and voices run on your PC).
  Plus ground crew with fuel/ammo trucks and carrier radio (Marshal, Tower, LSO) for carriers in the mission.
  DCS-ATC spawns no aircraft: it only does the radio for the AWACS, tankers and traffic of your mission (also when spawned later).
  Works in any mission on any map, multiplayer too. In VR the radio wheel shows as text in the game.

Requirements
  - Windows 10/11 (64-bit), DCS World (any map; tested so far on Caucasus)
  - SRS (DCS-SimpleRadio-Standalone) in the default folder C:\Program Files\DCS-SimpleRadio-Standalone.
    DCS-ATC starts the SRS server and client itself (different folder: "SrsPath" in the settings).
    An SRS client that is not yet connected is connected to the local SRS server automatically
    (via SRS's own auto-connect; to turn it off: "SrsAutoConnect": false in config.jsonc).
  - Microphone; radio calls are in English
  - approx. 1.2 GB disk space. Speech recognition runs on the CPU (a few seconds per radio call).

Installation
  1. Close DCS, run DCS-ATC-Setup.exe. No admin rights needed – only one Windows prompt for
     MissionScripting.lua (see "What setup changes in DCS").
  2. At the end, "Set keys": key for the radio wheel and push-to-talk (keyboard or HOTAS).
  3. Done. From now on DCS-ATC starts with every mission by itself (small window in the taskbar).
  Modules: "Custom" lets you untick parts you don't need (ATC, Range, AWACS, Tanker, Carrier, AI radio,
  ground crew), e.g. when a real player runs the AWACS. You can change this later in the settings window.
  Language: the setup asks for the language at the start (German or English). To change the language
  of the program and menus later, simply run the setup again and choose the other language.

Usage
  1. Start DCS, fly any mission.
  2. Radios in SRS (AM): each airfield on its DCS frequency (as on the F10 map), Ground, Tower and Approach
     shared, UHF or VHF – e.g. Kutaisi 263.0 / 134.0, Senaki 261.0 / 132.0, Batumi 260.0 / 131.0.
     The radio wheel shows the frequency of the current airfield. Also: ATIS 263.5 · AWACS 251.5 · Tanker 255.5
  3. Talk: SRS push-to-talk on the frequency, in English, e.g.
       "Kutaisi Ground, Enfield 1-1, request startup"  ·  "Overlord, Enfield 1-1, request picture"
     Or without talking: radio wheel with the ' (apostrophe) key or F10 menu -> ATC.
     Radio menu: 1–9 to select, Enter = suggested radio call, Backspace back, Esc close.
     While the radio wheel is open, Print Screen saves a screenshot with the wheel to Pictures\DCS-ATC.
     In VR the radio wheel is shown as text in the game (Settings -> "Radio wheel in game (VR)").
     AWACS and tanker appear in the radio wheel only when one of your side is airborne.
     Radio wheel and F10 talk to the airfield whose frequency you have tuned in SRS (260.0 -> Batumi), otherwise
     to the nearest one. Radio wheel -> General -> "Select airfield": call a different airfield (until landing).
  Approach: report "inbound" to Approach -> radar vectors with headings, descent steps and speed.
     Visual: overhead break (initial -> break over the threshold -> downwind -> final); report "initial"
     to Tower as soon as you are inbound on the extended runway centerline. Prefer straight in: "request straight in".
     Bad weather: ILS or straight-in approach. On final the controller only monitors (like an ILS) and
     calls you if you deviate clearly from the course or glide path or are too low.
     Anyone who grossly ignores instructions (heading/altitude far off, three times) is dropped from the sequence.
  After landing, Ground guides you back to your start position with direction and distance.

Settings
  In the game: F10 -> ATC -> Settings (ground crew).
  Default for all missions: Saved Games\DCS\Scripts\DcsAtc\DcsAtcOptions.lua
  Callsign, volume, radio, radio wheel, language: Start menu -> DCS-ATC -> Settings (window, applies immediately)
  Voices, frequencies and everything else: "All options (config.jsonc)" in the settings window
  Own airfield frequencies (e.g. like real life, one per controller) in frequencies.jsonc in the DCS-ATC folder, used
     instead of the DCS map. DCS-ATC creates it on first start with one commented line per airfield of all installed
     maps and today's values; remove the // and change the numbers, e.g.
       "Nellis": { "ATIS": 270.1, "Ground": 275.8, "Tower": 327.0, "Departure": 273.55, "Approach": 379.0 },
     One frequency or a list [UHF, VHF] per controller. On a server it applies to all players. Applies after saving;
     invalid entries are ignored and listed in atc-log.txt. New maps/airfields are added, your lines stay unchanged.
  A single mission different (Mission Editor, trigger "Mission Start" -> DO SCRIPT):
       DCSATC_OPTIONS = { crew = true }

Multiplayer
  Everyone installs DCS-ATC. Whoever hosts the mission is automatically air traffic control; other players simply
  talk over SRS (same SRS server) or use their radio wheel.

Dedicated server
  DCS-ATC can run on the PC of a DCS dedicated server (DCS_server.exe), without a player at that PC.
  - Requirements: SRS server on the same PC (DCS-ATC starts it if needed), all players with SRS. Start the server once
    before installing so its Saved Games folder exists (DCS.server, DCS.dcs_serverrelease or the name after -w).
  - Setup: tick "Dedicated server", confirm its Saved Games folder and, if asked, its installation folder.
    Setup puts hook and mission script there and patches the server's MissionScripting.lua.
  - Then DCS-ATC starts with every server mission and runs in the background: no radio wheel, no keys, no microphone
    (automatic when DCS_server.exe runs without DCS.exe; or "Dedicated": true in config.jsonc). Players talk over SRS
    on the controller frequencies; text goes to each player's group in the game.
  - Optional: players with DCS-ATC installed use their radio wheel (client mode, requests go to the server via the DCS chat).
  Not tested on a real dedicated server yet – please report how it works.

What setup changes in DCS
  - Saved Games\DCS\Scripts\Hooks\DcsAtcHook.lua: starts DCS-ATC with the mission, radio wheel for other players
  - Saved Games\DCS\Scripts\DcsAtc\: mission script, default settings and language
  - Saved Games\DCS\Scripts\DcsAtcExport.lua and one line at the end of Export.lua (own aircraft to DCS-ATC;
    other export scripts such as SRS or Tacview are left untouched)
  - <DCS folder>\Scripts\MissionScripting.lua: one line that loads only the DCS-ATC script
    (backup next to it: MissionScripting.lua.vor-dcsatc). Missions themselves stay locked (no file access).
  A moved Saved Games folder (other drive) is found automatically; otherwise setup asks for your DCS folder in Saved Games.
  Uninstalling (Windows "Apps" or Start menu -> Uninstall DCS-ATC) removes everything again.

After a DCS update
  DCS resets Scripts\MissionScripting.lua. DCS-ATC notices this and asks once for admin rights,
  or: Start menu -> DCS-ATC -> "Repair after DCS update". Then restart the mission.

Known limitations
  - All maps supported, but only Caucasus has been tested so far. Radio calls in English.
  - Detailed arrival and departure procedures only for Kutaisi; all other airfields generic (runways, altitude, terrain from DCS).
  - Multiplayer only if you host the mission.
  - Speech recognition does not understand everything – if in doubt, use the radio wheel or F10 menu.

Licenses
  DCS-ATC: GNU GPL 3.0 (LICENSE.txt), source code: https://github.com/Iceman1991/DCS-ATC-Release
  Bundled third-party software (Whisper, Piper and voices, FFmpeg, espeak-ng …): THIRD-PARTY-NOTICES.txt
