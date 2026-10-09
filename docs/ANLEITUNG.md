# DCS-ATC Handbuch

DCS-ATC ist eine Sprach-Flugsicherung für DCS World. Über SRS übernimmt es Ground, Tower, Approach/Departure, ATIS, AWACS, Tanker, Range, Träger und Crew Chief an jedem Flugplatz der Karte. Du sprichst auf Englisch über SRS, nutzt das Funkrad, das F10-Menü oder den Multiplayer-Chat, und DCS-ATC antwortet mit Stimmen über SRS und mit Text im Spiel. Spracherkennung und Stimmen laufen offline auf deinem PC.

DCS-ATC spawnt keine Flugzeuge. Es übernimmt nur den Funk für Einheiten, die schon in deiner Mission sind, auch für solche, die die Mission später spawnt.

Funk ist Englisch: Alle Funksprüche, die du sagst oder hörst, stehen in diesem Handbuch auf Englisch. Die Erklärungen sind deutsch, Funkrad- und Menüeinträge stehen so da, wie sie in der deutschen Oberfläche heißen.

*English version: [MANUAL.md](MANUAL.md).*

## Inhalt

1. [Erste Schritte](#erste-schritte)
   - [Was du brauchst](#was-du-brauchst) · [Installation](#installation) · [SRS einrichten](#srs-einrichten) · [Tasten im Überblick](#tasten-im-überblick) · [Dein erster Flug](#dein-erster-flug-kaltstart-in-kutaisi)
2. [Wege zu funken](#wege-zu-funken)
   - [Sprechen über SRS](#sprechen-über-srs) · [Grundlegende Phrasen](#grundlegende-phrasen) · [Funkrad](#funkrad) · [Funkrad in VR](#funkrad-als-text-im-spiel-vr) · [F10-Menü](#f10-menü) · [Chat](#tippen-im-chat-multiplayer) · [Text im Spiel](#text-im-spiel) · [Stimmen](#stimmen-funkklang-und-sprechtempo) · [Höhenmesser-Einheit](#höhenmesser-einstellung-hpa-oder-inhg) · [Bahnbefeuerung](#bahnbefeuerung)
3. [Flugplatz-ATC](#flugplatz-atc)
   - [Plätze, Rufnamen und Frequenzen](#plätze-rufnamen-und-frequenzen) · [ATIS](#atis) · [Bahnwahl](#bahnwahl) · [Wetter: VFR und IFR](#wetter-vfr-und-ifr) · [Ground](#ground) · [Tower: Abflug](#tower-abflug) · [Tower: Ankunft](#tower-ankunft) · [Approach](#approach) · [Luftraum](#luftraum) · [Notfälle](#notfälle) · [Landebewertung und Debriefing](#landebewertung-und-debriefing) · [Meldepflicht](#meldepflicht) · [Phrasentabelle](#phrasentabelle)
4. [Carrier-Betrieb](#carrier-betrieb)
5. [AWACS, Tanker und Range](#awacs-tanker-und-range)
   - [AWACS](#awacs) · [Tanker](#tanker) · [Range](#range) · [KI-Flüge im Funk](#ki-flüge-im-funk) · [Bodenpersonal und Crew Chief](#bodenpersonal-und-crew-chief)
6. [Multiplayer, Dedicated Server, eigene Frequenzen und andere Karten](#multiplayer-dedicated-server-eigene-frequenzen-und-andere-karten)
   - [So funktioniert Multiplayer](#so-funktioniert-multiplayer) · [Dedicated Server einrichten](#dedicated-server-einrichten) · [Eigene Frequenzen je Platz](#eigene-frequenzen-je-platz) · [Andere Karten als Kaukasus](#andere-karten-als-kaukasus)
7. [Anhang: Einstellungen, Logs und Fehlersuche](#anhang-einstellungen-logs-und-fehlersuche)
   - [Das Einstellungsfenster](#das-einstellungsfenster) · [config.jsonc-Referenz](#configjsonc-referenz) · [Logdateien](#logdateien) · [Fehlersuche](#fehlersuche)

Die Beispiele verwenden das Rufzeichen Enfield 1-1 und schreiben viele Zahlen als Ziffern. Im Funk sprechen die Lotsen sie Ziffer für Ziffer (zum Beispiel „two five one decimal five“).

## Erste Schritte

### Was du brauchst

- Windows 10 oder 11, 64 Bit.
- DCS World. Alle Karten funktionieren, Kaukasus ist am besten getestet.
- **SRS (DCS-SimpleRadio-Standalone).** Standardordner ist `C:\Program Files\DCS-SimpleRadio-Standalone`. Liegt deins woanders, siehe [SRS einrichten](#srs-einrichten).
- Ein Mikrofon. Alle Funksprüche sind Englisch.
- Etwa 1,2 GB Speicherplatz. Die Spracherkennung läuft auf der CPU und braucht ein paar Sekunden pro Spruch.

### Installation

1. **DCS schließen** und `DCS-ATC-Setup.exe` starten.
   - **Windows SmartScreen** meldet eventuell „Der Computer wurde durch Windows geschützt“, weil das Setup neu und nicht signiert ist. Auf **Weitere Informationen** und dann **Trotzdem ausführen** klicken.
2. **Sprache wählen** (Deutsch oder Englisch). Sie gilt für das Programmfenster, das Einstellungsfenster und das F10-Menü im Spiel. Der Funk ist immer Englisch. Später ändern: Setup erneut ausführen und die andere Sprache wählen, oder im Einstellungsfenster.
3. **Admin-Abfrage für `MissionScripting.lua`.** Setup braucht Adminrechte nur für diese eine Windows-Abfrage (UAC). Damit fügt es eine Zeile in `<DCS-Ordner>\Scripts\MissionScripting.lua` ein. Diese Zeile lädt nur das DCS-ATC-Missionsskript. Missionen selbst bleiben gesperrt und haben weiter keinen Dateizugriff. Eine Sicherung liegt daneben als `MissionScripting.lua.vor-dcsatc`. Lehnst du ab, bekommt DCS-ATC keine Missionsdaten: kein F10-Menü, kein Rufzeichen aus dem Slot, kein KI-Verkehr. Nachholen mit **Startmenü → DCS-ATC → Nach DCS-Update reparieren**.
4. **Tasten festlegen.** Auf der letzten Setup-Seite ist das Kästchen „Tasten festlegen (Funkrad, Push-to-Talk – Tastatur oder HOTAS)“ angehakt. Ein kleines Fenster fragt dann nacheinander zwei Tasten ab:
   - **Funkrad öffnen/schließen.** Taste oder HOTAS-Knopf drücken.
   - **Push-to-Talk.** Nur nötig, wenn du nicht über SRS sprichst. Taste oder HOTAS-Knopf drücken.

   **Esc** behält jeweils den bisherigen Wert. Die neuen Tasten gelten ab dem nächsten Start von DCS-ATC. Jederzeit wiederholbar über **Startmenü → DCS-ATC → Tasten festlegen** oder das Einstellungsfenster.
5. **Fertig.** Ab jetzt startet DCS-ATC mit jeder Mission von selbst und erscheint als kleines Konsolenfenster in der Taskleiste. Es schließt sich, wenn du die Mission verlässt oder DCS beendest. Lass das Fenster beim Fliegen offen.

Setup installiert nach `%LOCALAPPDATA%\Programs\DCS-ATC` und legt diese Startmenü-Einträge an:

| Startmenü-Eintrag | Funktion |
|---|---|
| DCS-ATC starten | Startet die App von Hand. Normalerweise nicht nötig. |
| Tasten festlegen (Funkrad, Push-to-Talk) | Öffnet die Tastenabfrage aus Schritt 4. |
| Einstellungen (Rufzeichen, Lautstärke, Funk) | Öffnet das Einstellungsfenster. |
| Bodenpersonal (Standard) | Öffnet `DcsAtcOptions.lua` im Editor. |
| Nach DCS-Update reparieren | Passt `MissionScripting.lua` erneut an (Admin-Abfrage). |
| Liesmich | Öffnet die kurze Liesmich-Datei. |
| DCS-ATC deinstallieren | Entfernt DCS-ATC, auch die Zeilen, die es in `MissionScripting.lua` und `Export.lua` eingefügt hat. |

#### Was Setup an DCS ändert

- `Saved Games\DCS\Scripts\Hooks\DcsAtcHook.lua` startet DCS-ATC mit jeder Mission und reicht Funkrad-Anfragen anderer Spieler an den Host weiter.
- `Saved Games\DCS\Scripts\DcsAtc\` enthält das Missionsskript, die Standardoptionen (`DcsAtcOptions.lua`) und die Sprachdatei.
- `Saved Games\DCS\Scripts\DcsAtcExport.lua` plus eine Zeile am Ende von `Export.lua` schickt die Daten deines eigenen Flugzeugs an DCS-ATC. Andere Export-Skripte wie SRS oder Tacview bleiben unberührt.
- Eine Zeile in `<DCS-Ordner>\Scripts\MissionScripting.lua` (siehe Schritt 3).

Ist dein Saved-Games-Ordner verschoben, etwa auf ein anderes Laufwerk, findet Setup ihn trotzdem. Findet Setup `Saved Games\DCS` gar nicht, fragt es nach deinem DCS-Ordner in Saved Games, also dem Ordner mit `Config` und `Scripts`. Gibt es nur `Saved Games\DCS.openbeta`, nimmt Setup diesen.

#### Installation auf einem Dedicated Server

DCS-ATC läuft auch auf einem PC mit DCS Dedicated Server (`DCS_server.exe`), an dem niemand sitzt. Im Setup die Aufgabe **„Dedicated Server“** ankreuzen. Auf einem PC nur mit Dedicated Server wählt Setup sie selbst. Alle Schritte stehen unter [Dedicated Server einrichten](#dedicated-server-einrichten).

### SRS einrichten

**SRS muss auf dem PC installiert sein, auf dem DCS-ATC läuft, und dessen SRS-Server muss auf diesem PC laufen.** DCS-ATC hört alle Spieler über diesen lokalen SRS-Server und sendet seine Lotsen darüber.

- **SRS-Ordner finden.** DCS-ATC nimmt `SrsPath` aus `config.jsonc`, Standard `C:\Program Files\DCS-SimpleRadio-Standalone`. Ist das kein gültiger SRS-Ordner, versucht DCS-ATC der Reihe nach:
  1. die SRS-Installation aus der Registry,
  2. den Ordner eines laufenden SRS-Clients oder -Servers.

  Selbst wählen: **Einstellungen → SRS-Ordner → „…“**. Ein gültiger SRS-Ordner enthält `ExternalAudio\DCS-SR-ExternalAudio.exe`. Du kannst auch den Unterordner `Client` oder `Server` oder eine der SRS-`.exe` angeben und den Pfad mit Anführungszeichen einfügen. DCS-ATC erkennt den Aufbau ab SRS 2.2 (Unterordner `Client\`, `Server\`, `ExternalAudio\`) und den älteren mit allem in einem Ordner.
- **Server und Client starten.** Startet DCS-ATC als Host, startet es den SRS-Server und dann den SRS-Client, sofern sie nicht schon laufen. Fehlt einer davon im SRS-Ordner, meldet die Konsole: `SRS-Server nicht gefunden in "…" – SrsPath in config.jsonc bzw. Einstellungen prüfen.`
- **Automatisch verbinden.** Ist der SRS-Client noch nicht verbunden, lässt DCS-ATC ihn mit dem lokalen Server verbinden (`127.0.0.1`, Port aus der `server.cfg` des Servers, normalerweise 5002). Dazu nutzt es denselben Auto-Connect-Weg wie der SRS-DCS-Hook, bis zu drei Versuche. Einen schon verbundenen Client fasst es nie an, auch wenn er mit einem anderen Server verbunden ist.
  - Erfolg: `SRS-Client mit 127.0.0.1:5002 verbunden.`
  - Fehlschlag: `SRS-Client hat sich nicht verbunden – im Client 127.0.0.1:5002 eintragen und Connect drücken.`
  - Abschalten mit `"SrsAutoConnect": false`.
- **Multiplayer.** Alle verbinden ihren SRS-Client mit dem SRS-Server auf dem PC des Hosts, also dem PC mit DCS-ATC.
- **Frequenzen rasten.** Alle Frequenzen sind AM.
  - **Flugplätze** nutzen ihre eigene DCS-Frequenz, die auf der F10-Karte. Ground, Tower und Approach teilen sie sich, UHF oder VHF geht beides. Beispiele Kaukasus: Kutaisi 263.0 / 134.0, Senaki 261.0 / 132.0, Batumi 260.0 / 131.0.
  - **Andere Stationen** haben Standardwerte: ATIS 263.5, AWACS 251.5, Tanker 255.5, Range 267.5, Carrier 127.5. Hat ein AWACS oder Träger in der Mission eine eigene Frequenz, gilt diese.
  - **Das Funkrad** zeigt in der obersten Ebene die Frequenz jeder Station. Rot mit ✗ heißt: keines deiner SRS-Funkgeräte ist darauf gerastet.
- **Du hörst nur, worauf du gerastet bist.** Lotsen senden auf ihrer Frequenz. Ein Lotsenspruch erscheint zusätzlich als Text für deine Gruppe, aber nur, wenn eines deiner Funkgeräte auf dieser Frequenz steht. Kennt SRS deine Funkgeräte nicht, siehst du allen Text.

### Tasten im Überblick

| Taste | Funktion |
|---|---|
| `'` (US-Tastatur) / `Ä` (deutsche Tastatur), Standard | Funkrad öffnen/schließen. Nur, wenn DCS das aktive Fenster ist. |
| `1`–`9` (auch Ziffernblock) | Eintrag wählen. |
| `Enter` oder `0` | Vorgeschlagenen Spruch senden. Hat ein Lotse dich gerade etwas gefragt oder eine Meldung verlangt, ist der Vorschlag die Antwort (nach „say airspeed“ etwa `<deine IAS> knots`). Sonst ist es der nächste normale Schritt deines Flugs. Die Radmitte zeigt ihn als `ENTER ▸ <Lotse>: <Spruch>`. |
| `Rücktaste` | Eine Ebene zurück. |
| `Esc` | Rad schließen. |
| `+` / `-` | Rad größer oder kleiner (wird gespeichert). |
| `Druck` (Rad offen) | Screenshot mit Rad, gespeichert in `Bilder\DCS-ATC`. |
| HOTAS (falls belegt) | Rad-Knopf öffnet und schließt das Rad. Hat hoch/runter bewegt die Markierung, rechts wählt, links geht zurück. |
| Push-to-Talk, Standard rechte `Strg` | Nur, wenn SRS nicht verbunden ist. Mit SRS sprichst du mit deiner normalen SRS-Sprechtaste. |

Alle Funkrad-Einträge stehen unter [Funkrad](#funkrad). Das F10-Funkmenü **ATC** bietet die meisten davon im Spiel. In VR siehst du das Desktop-Rad nicht, deshalb erscheint es zusätzlich als Text im Spiel (siehe [Funkrad als Text im Spiel](#funkrad-als-text-im-spiel-vr)).

### Dein erster Flug: Kaltstart in Kutaisi

Nimm eine beliebige Kaukasus-Mission mit Kaltstart auf dem Vorfeld in Kutaisi. Mit laufender Mission kommt dein Rufzeichen aus deinem DCS-Slot.

So funkst du:

- **Anrufen.** SRS-Sprechtaste auf der Lotsenfrequenz drücken und Station, dann Rufzeichen, dann Anliegen sagen, zum Beispiel: „Kutaisi Ground, Enfield 1-1, request startup“.
- **Ohne Sprechen.** Jeden Spruch unten kannst du auch aus dem Funkrad senden. Oft reicht **Enter**, weil es den nächsten Schritt vorschlägt.
- **Rufzeichen.** Du kannst „Enfield 1-1“ oder „Enfield one one“ sagen.

In Kutaisi teilen sich Ground, Tower und Approach die Platzfrequenz. Du kannst den ganzen Flug auf 263.0 (UHF) oder 134.0 (VHF) bleiben. Sagt ein Lotse „contact Kutaisi Tower“ ohne Frequenz, übernimmt ein anderer Lotse auf derselben Frequenz.

**1. Funkgeräte rasten.** Ein Gerät auf die ATIS 263.5, eins auf Kutaisi 263.0 oder 134.0, beide AM.

**2. ATIS hören.** Die ATIS sendet auf 263.5 dauerhaft für jeden Platz mit einem Spieler im Umkreis von 40 NM. Teilen sich mehrere Plätze die Frequenz, wechseln sie sich ab. Jede Sendung beginnt mit Platzname und Kennbuchstaben:

> Kutaisi information Alpha, time … Zulu. Runway two five in use, left hand pattern, … feet. Wind …. Visibility one zero kilometers or more, sky clear. Temperature …. QNH …, altimeter …. Tower one three four decimal zero, UHF two six three decimal zero. Advise on initial contact you have information Alpha.

Auf Wunsch spielt Funkrad → Allgemein → ATIS hören sie ab. Ändert sich der Kennbuchstabe, sagt der Platz es allen an („Attention all aircraft, Kutaisi information Bravo now current, …“).

**3. Anlassen (Ground).**

```
Du:     Kutaisi Ground, Enfield 1-1, request startup, information Alpha.
Ground: Enfield one one, Kutaisi Ground, start up approved. Report ready to taxi.
```

Nennst du den aktuellen ATIS-Buchstaben nicht, ergänzt Ground ihn mit Bahn und Höhenmessereinstellung. Die Antwort endet dann so: „…start up approved. Information Alpha is current, runway two five in use, altimeter …. Report ready to taxi.“

**4. Rollen (Ground).**

```
Du:     Kutaisi Ground, Enfield 1-1, ready to taxi.
Ground: Enfield one one, Kutaisi Ground, taxi to holding point runway two five via …. Squawk ….
        Hold short runway two five, report ready for departure.
```

- **Erkannte Formulierungen.** „request taxi“, „ready to taxi“ und „request startup and taxi“ gehen alle. Letzteres gibt Anlassen und Rollen zusammen frei.
- **Progressive Taxi.** Während du rollst, bringt dich „request progressive taxi“ mit Richtungsangaben zum Ziel.

**5. Start (Tower).** Am Rollhalt:

```
Du:    Kutaisi Tower, Enfield 1-1, holding short runway 25, ready for departure.
Tower: Enfield one one, after departure exit via C R P west, not above … until leaving the control zone,
       runway two five, wind …, cleared for takeoff.
```

- **Ausflugrichtung wählen.** Mit Richtung bestimmst du den Meldepunkt: „ready for departure, north“.
- **Bahn nicht frei.** Mit Verkehr im Endanflug oder auf der Bahn kommt „hold short runway two five, traffic …“ oder „line up and wait“. Tower gibt dich von selbst frei, sobald die Bahn frei ist.
- **Zu früh.** Meldest du „ready“ vor dem Rollhalt, kommt „negative, you are not at the holding point“.
- **Parallelbahn kreuzen.** Liegt zwischen Vorfeld und Startbahn eine Parallelbahn (etwa 03L vor 03R), endet die Rollfreigabe mit „hold short runway zero three left“. Am Haltepunkt dort meldest du „holding short runway 03L, request crossing“ (das Funkrad schlägt es vor; „ready for departure“ dort bekommt dieselbe Antwort): „cross runway zero three left, hold short runway zero three right“. An der Parallelbahn gibt es keine Startfreigabe. Ist sie belegt, kommt „hold short runway zero three left, traffic …“ und die Freigabe zum Kreuzen folgt von selbst, sobald sie frei ist. Wer ohne Freigabe kreuzt, bekommt „You are not cleared onto runway …“.

**6. Abflug (Approach).** Beim Steigen durch etwa 500 ft über Grund:

```
Tower:    Enfield one one, contact Kutaisi Approach.
Du:       Kutaisi Approach, Enfield 1-1, airborne, climbing.
Approach: Enfield one one, Kutaisi Approach, radar contact, … miles … of the field.
          Continue as cleared, report leaving the control zone.
Du:       Kutaisi Approach, Enfield 1-1, leaving the control zone.
Approach: Enfield one one, roger, resume own navigation, … frequency change approved. Good day.
```

Bist du beim Melden schon außerhalb der Kontrollzone, kommt stattdessen „radar contact, …. Resume own navigation, maintain VFR.“

Den Check-in machst du selbst. Approach identifiziert dich nicht von sich aus. Hast du dich 60 s nach der Übergabe (oder am Rand der Kontrollzone) nicht gemeldet, erinnert dich Tower einmal: „Enfield one one, contact Kutaisi Approach now.“ Meldest du dich dann immer noch nicht, gibt es 60 s später keine Radarbetreuung: „no contact with Kutaisi Approach, radar service not provided, squawk VFR, frequency change approved.“ Unter IFR ist das zusätzlich ein Verstoß.

**7. Rückkehr (Approach).** Ruf Approach früh genug:

```
Du:       Kutaisi Approach, Enfield 1-1, 25 miles west, 6000 feet, inbound for landing, information Alpha.
Approach: Enfield one one, Kutaisi Approach, identified, 25 miles west. Runway two five in use, wind …, altimeter ….
          Expect vectors to initial runway two five for overhead break. Turn … heading …, descend and maintain ….
```

- **Radarführung.** Approach gibt dir nun Kurse, Höhen und bei Bedarf eine Geschwindigkeit. Du musst nicht alles zurücklesen, aber fliegen.
- **Warteschleife.** Ist die Platzrunde voll, kommst du in ein Holding mit erwarteter Anflugzeit („I will call you“).
- **Geradeaus-Anflug.** Sag „request straight in“ (oder „request ILS“). Bei schlechtem Wetter gibt es ohnehin einen Instrumenten- oder Geradeaus-Anflug.
- **Übergabe an Tower.** Sobald du auf Kurs zum Initial bist:

```
Approach: Enfield one one, on course, … miles from initial, maintain …, contact Kutaisi Tower,
          report initial for overhead break.
```

**8. Overhead Break und Landung (Tower).** **Das Initial meldest du selbst.** Tower gibt die Break-Freigabe nur als Antwort auf deinen Spruch:

```
Du:    Kutaisi Tower, Enfield 1-1, initial runway 25.
Tower: Enfield one one, runway two five, cleared break left hand. Report final.
```

Erreichst du das Initial ohne Meldung, fragt Tower einmal: „Enfield one one, Kutaisi Tower, report initial.“ Fliegst du ohne Freigabe weiter Richtung Break, kommt 1 NM später „no break clearance, continue straight through, re-enter initial runway two five, report initial.“

Nach dem Break meldest du Base (oder Final) mit ausgefahrenem Fahrwerk:

```
Du:    Kutaisi Tower, Enfield 1-1, base, gear down, full stop.
Tower: Enfield one one, runway two five, wind …, cleared to land.
```

- **Fahrwerksprüfung.** Ist das Fahrwerk noch oben und du hast nicht „gear down“ gesagt, antwortet Tower „check wheels down“.
- **Falsche Bahn.** Bist du auf die falsche Bahn ausgerichtet, ruft Tower „check runway!“.
- **Andere Optionen.** Statt „full stop“ geht „touch and go“, „low approach“ oder „the option“ (Funkrad → Tower → „Base, touch and go“).
- **Durchstarten.** Startest du durch, sag „going around“.

Base oder Final musst du selbst melden; ohne diese Meldung gibt Tower nie eine Landefreigabe. Meldest du nicht, fragt Tower bei etwa 2,5 NM (oder im Queranflug) einmal nach: „Enfield one one, report final.“ Hast du innerhalb 0,8 NM oder unter 300 ft immer noch keine Landefreigabe, wirst du zum Durchstarten geschickt: „go around, no landing clearance, climb and maintain 2000 feet, join left hand downwind runway two five, report base.“ Das ist auch ein Verstoß im Debriefing.

**9. Nach der Landung (Tower, dann Ground).** Tower sagt dir, wo du abrollst, etwa „Vacate left via Whiskey. Report vacated.“ Dann Ground rufen:

```
Du:     Kutaisi Ground, Enfield 1-1, runway vacated, request taxi to parking.
Ground: Enfield one one, taxi to … via …, parking spot …, ….
```

Ground führt dich mit Richtung und Entfernung zurück zu deiner Startposition. Sag hier nicht nur „request taxi“, denn vor der Rollfreigabe zum Parkplatz gilt das als Rollanfrage für einen Abflug.

Rollst du ohne Rollfreigabe weiter (schneller als 5 kt und mehr als 150 m von der Bahn), bekommst du „hold position, you are not cleared to taxi. Request taxi.“ und einen Verstoß.

**10. Debriefing.** Funkrad → Allgemein → Debriefing listet deinen Flug, auch Luftraumverstöße.

## Wege zu funken

Du kannst DCS-ATC auf vier Wegen ansprechen und sie im Flug beliebig mischen:

| Weg | Was du tust | Für wen |
|---|---|---|
| **Sprechen über SRS** | SRS-Sprechtaste auf der Lotsenfrequenz drücken und Englisch sprechen | Alle in der Mission, auch Multiplayer-Mitspieler |
| **Funkrad** | Rad-Taste drücken und einen Spruch mit Zifferntasten oder HOTAS wählen | Missions-Host und Einzelspieler (Mitspieler bekommen ein eingeschränktes Rad, siehe unten) |
| **F10-Menü** | DCS-Funkmenü → **F10 Andere → ATC** | Jeder Spieler-Slot der Mission |
| **Chat-Text** | Im Multiplayer-Chat `ATC>` und dann den Spruch tippen | Nur Multiplayer |

Egal welcher Weg: Die Lotsen antworten gleich, per Stimme über SRS auf ihrer Frequenz und als Text im Spiel.

### Sprechen über SRS

#### So funktioniert es

DCS-ATC hört über SRS mit. Funkgerät auf eine Lotsenfrequenz rasten, die normale **SRS-Sprechtaste** halten und sprechen. Eine eigene Taste brauchst du nicht. Die Spracherkennung läuft offline auf deiner CPU und braucht ein paar Sekunden pro Spruch.

- **Sprache:** nur Englisch.
- **Modulation:** AM (`"Modulation": "AM"`).
- **SRS wird für dich gestartet.** DCS-ATC startet SRS-Server und -Client, falls sie nicht laufen, und verbindet einen noch nicht verbundenen Client (siehe [SRS einrichten](#srs-einrichten)).
- `"SrsListen": true` schaltet das Mithören ein. Mit `false` geht nur die Sprechtaste unten.

#### Die Frequenz wählt den Lotsen

Die Frequenz, auf der du sendest, entscheidet, wer antwortet:

| Du sendest auf | Es antwortet |
|---|---|
| Einer **Platzfrequenz von der DCS-Karte** (UHF oder VHF, etwa Kutaisi 263.0 / 134.0) | Dieser Platz. Ground, Tower und Approach teilen sich die Frequenz, der Inhalt deines Spruchs wählt den Lotsen („request taxi“ → Ground, „ready for departure“ → Tower, „inbound for landing“ → Approach). Beginnst du mit einem Stationsnamen („Kutaisi Tower, …“), antwortet dieser Lotse. |
| Einer Frequenz aus `"Frequencies"` in config.jsonc | Der Lotse dieser Rolle an deinem aktuellen Platz. Standard: Ground 264.5, Tower 265.0, Approach 266.5, Range 267.5, AWACS 251.5, Tanker 255.5 (MHz). |
| Der Frequenz eines **AWACS** oder **Trägers** der Mission | Dieses AWACS bzw. dieser Träger |
| Einer **eigenen Platzfrequenz** aus `frequencies.jsonc` | Genau dieser Lotse (eine Ground-Frequenz erreicht Ground). Siehe [Eigene Frequenzen je Platz](#eigene-frequenzen-je-platz). |

Ohne passende Frequenz leitet ein Stationsname im Spruch trotzdem weiter:

- **AWACS:** „Overlord“, „Magic“, „Darkstar“, „Wizard“, „Focus“, „Moscow“, „AWACS“, dazu „picture“, „bogey dope“, „request sort“, „declare“, „spike“
- **Tanker:** „Texaco“, „Shell“, „Arco“, „tanker“, „refuel“
- **Range:** „Range …“ (nur mit Range in der Mission)
- **Träger:** „Marshal“, „Mother“, „Paddles“, „ball“, „Clara“, „pigeons“, „carrier“ (nur mit Träger in der Mission)

In manchen Fällen kommt statt einer Antwort eine Korrektur:

- **Anderer Platz auf dieser Frequenz.** Rufst du auf einer Platzfrequenz einen anderen Platz, wirst du zum Wechseln aufgefordert:
  > *Enfield one one, this is Kolkhi Tower, you are on Senaki Kolkhi frequency, contact Kutaisi Tower one three four decimal zero.*
- **Platzanfrage auf einer Stationsfrequenz.** Fragst du auf der AWACS-, Tanker-, Range- oder Trägerfrequenz nach Anlassen, Rollen, Start oder Landung, kommt ein Texthinweis im Spiel („Du funkst auf Tanker 255.5. Kutaisi Ground: 263.0 / 134.0“). Über Funk antwortet niemand.
- **Feindliche Plätze.** Feindliche Plätze antworten nicht. Ein Texthinweis nennt den nächsten eigenen Platz. MAYDAY oder PAN PAN übernimmt trotzdem ein eigener Platz.

Du hörst (und siehst) nur, worauf du gerastet bist. Lotsensprüche erscheinen als Text nur für Spieler, deren SRS-Funkgeräte auf dieser Frequenz stehen. Ausnahme Guard: den bekommen alle.

#### Dein Rufzeichen

- **Herkunft:** DCS-ATC nimmt dein Rufzeichen aus deinem **DCS-Slot**, aus „Enfield11“ wird **Enfield 1-1**. Lotsen sprechen es „Enfield one one“, in einer Rotte „Enfield one one flight“.
- **Slots ohne westliches Rufzeichen:** der erste solche Spieler bekommt `"Callsign"` aus config.jsonc (Standard `"Enfield 1-1"`). Weitere werden mit ihrem Spielernamen angesprochen.
- **Eigenes Rufzeichen:** `"MyCallsign"` setzen (Standard `""` = aus DCS) oder Einstellungsfenster → *Eigenes Rufzeichen*. Format: Name und zwei Ziffern, etwa `Viper 1-1`. Es ersetzt das Slot-Rufzeichen des Spielers an diesem PC (Host); andere Spieler behalten ihres.
- **Verhörer werden toleriert.** Dein Rufzeichen wird erkannt, wenn die Ziffern stimmen und das Wort nur leicht abweicht („Enfeld 1-1“ zählt als „Enfield 1-1“).
- **Was du sagst, ändert dein Rufzeichen nicht.** Liefert die Mission dein Rufzeichen (oder hast du eins gesetzt), benennt dich ein anderes Rufzeichen im Gesprochenen nicht um. Ein Spruch, der mit einem anderen Rufzeichen oder einer Rottennummer beginnt und deins nicht enthält („Enfield 1-2, check fuel“, „Two, go trail“), gilt als Rottenfunk, der Lotse ignoriert ihn.
- **Wer gesendet hat:** dein Spruch wird dir über deine SRS-/DCS-Einheit zugeordnet. Klappt das nicht, über das gesprochene Rufzeichen.
- **Ähnliches Rufzeichen:** hat ein KI-Flugzeug im Umkreis von 30 NM ein Rufzeichen mit gleichem Wort, aber anderer erster Ziffer, kommt einmal: „Caution similar callsign, Enfield five one is also on this frequency.“

#### Grundlegende Phrasen

Sprich im Standardformat: **Station, Rufzeichen, Nachricht.**

```
Kutaisi Ground, Enfield 1-1, request startup.
Kutaisi Tower, Enfield 1-1, ready for departure.
Overlord, Enfield 1-1, request picture.
```

Diese Sprüche gehen bei jedem Platzlotsen:

| Du sagst | Lotse |
|---|---|
| „… radio check“ / „how do you read“ | *„Enfield one one, Kutaisi Tower, read you five.“* |
| „say again“, „come again“, „repeat“, „say last“, „didn't copy“, „missed“, „pardon“ | Wiederholt die letzte Anweisung: *„Enfield one one, I say again, …“*. Unter Radarführung, im Holding oder auf dem Weg zum Initial gibt er neuen Kurs, Höhe und Fahrt ab deiner aktuellen Position. Gibt es nichts zu wiederholen: *„…, go ahead.“* |
| „confirm …“ / „verify …“ (etwa „confirm heading 045“, „verify runway 25“) | Prüft den Wert gegen die letzte Anweisung: *„affirm, heading zero four five“* oder *„negative, heading zero five five“*. Eine Frage zu einer Freigabe („confirm cleared to land“) wiederholt den ganzen Spruch. |
| Nur Station und Rufzeichen („Kutaisi Tower, Enfield 1-1“) | *„Enfield one one, Kutaisi Tower.“* Dann sagst du dein Anliegen. Direkt nach einer Übergabe kommen gleich die Anweisungen. |
| „request weather“, „QNH“, „wind“, „altimeter“, „runway in use“, „which runway“ | *„Enfield one one, Kutaisi Tower, wind two five zero degrees, eight knots, altimeter two niner niner two, runway two five in use.“* |
| „unable“ (in der Luft) | *„…, roger, say intentions.“* Am Boden: *„…, roger, hold position.“* |
| „good day“, „thanks“, „bye“ | *„…, good day.“* |

Worauf der Lotse nicht antwortet:

- **Readbacks bleiben unbeantwortet.** Wie im echten Funk heißt Stille: Readback in Ordnung. Als Readback gilt ein Spruch mit „wilco“, „roger“, „copy“, „cleared“, „approved“ oder „report …“, einer, der die Werte des Lotsen wiederholt (hold short, contact, squawk, QNH bzw. altimeter, heading oder nur die Zahlen: „Tower 134.0, Enfield 1-1“), oder nur dein Rufzeichen, auch mit „starting up“, „taxiing“, „lining up“, „rolling“ oder „switching“ („Start up approved, Enfield 1-1“, „Enfield 1-1“). Ein Readback einer IFR-Freigabe mit richtigem Squawk bekommt *„readback correct.“*
- **Dieselbe Anfrage innerhalb von 30 Sekunden nach dem Ende der Lotsenantwort noch einmal gilt als Readback.**

Willst du eine Antwort, beginne mit dem Stationsnamen („Kutaisi Tower, …“) oder nimm „request“, „confirm“, „verify“, „say“ oder „again“ in den Spruch.

Wenn der Lotse dich nicht versteht:

| Lage | Antwort |
|---|---|
| Undeutliche Aufnahme (Erkennung unsicher) | *„Enfield one one, Kutaisi Tower, say again.“* Der Lotse tut nichts, bis du wiederholst. |
| Nachricht nicht verstanden, Rufzeichen gehört | *„Enfield one one, say again.“* |
| Kein Rufzeichen gehört | *„Station calling Kutaisi Tower, say again your callsign.“* |

**Wenn ein Lotse beschäftigt ist:** stehen vor deiner Antwort mehr als etwa 8 Sekunden andere Sprüche an, hörst du zuerst *„Enfield one one, stand by.“*, die Antwort folgt. Notfälle und Sicherheitssprüche (go around, traffic alert, threat) gehen vor.

#### Tipps für gute Erkennung

- Sprechtaste drücken, kurz warten, dann sprechen. Erst nach dem letzten Wort loslassen.
- Normal schnell, Mikrofon nah, ruhiger Raum.
- Mit dem Stationsnamen beginnen, dann das Rufzeichen. Die Erkennung kennt dein Rufzeichen, die Stationsnamen des aktuellen und der anderen Plätze der Karte. Nimm den Stationsnamen wie in DCS („Kutaisi Tower“, „Minvody Approach“).
- ICAO-Zahlen werden verstanden: „niner“, „tree“, „fower“, „fife“, „decimal“. Ziffernweise („two five“) und ganze Zahlen („25“) gehen beide, ebenso „6,000“, „flight level one five zero“, „angels 15“ und „cherubs 5“.
- Eine Nachricht pro Spruch. Kurz und Standard schlägt lang und kreativ.
- Klappt ein Spruch immer wieder nicht, nimm Funkrad oder F10-Menü. Sie senden genau den Text, den DCS-ATC erwartet.

#### Push-to-Talk ohne SRS

Ist SRS nicht verbunden, nimmt DCS-ATC mit eigener Taste vom Mikrofon auf:

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `"PttKey"` | `163` | Virtual-Key-Code. 163 = rechte Strg, 165 = rechte Alt, 161 = rechte Umschalt, 112–123 = F1–F12. Tipp: dieselbe Taste wie deine SRS-Sprechtaste. |
| `"PttJoystick"` / `"PttButton"` | `-1` / `1` | HOTAS-Knopf. -1 = aus. Nummern herausfinden: `DcsAtc.exe --buttons` starten und den Knopf drücken. |
| `"MicName"` | `""` | Teil des Mikrofonnamens (etwa `"FDUCE"`). Leer = Windows-Standard. Auch im Einstellungsfenster → *Mikrofon (nur ohne SRS)*. |

Aufnahmen unter einer halben Sekunde werden ignoriert. Ist SRS verbunden, wird diese Taste ignoriert, damit dein Spruch nicht doppelt verarbeitet wird.

Rad-Taste und Sprechtaste legst du per Tastendruck fest: Startmenü → **DCS-ATC → Tasten festlegen (Funkrad, Push-to-Talk)** oder Einstellungsfenster → *Tasten festlegen …*. Esc behält die bisherige Taste. Neue Tasten gelten ab dem nächsten Start von DCS-ATC.

### Funkrad

Das Funkrad ist ein Ringmenü über dem DCS-Fenster. Es sendet den exakten Spruchtext, die Erkennung kann also nicht danebenliegen. Standardmäßig spricht eine Pilotenstimme deinen Spruch auf der Frequenz, bevor der Lotse antwortet, so hören alle auf der Frequenz beide Seiten:

> **Enfield 1-1 (Pilotenstimme):** Kutaisi Tower, Enfield 1 1, ready for departure.
> **Kutaisi Tower:** Enfield one one, Kutaisi Tower, …

Das Rad ist ein eigenes Fenster, das immer im Vordergrund über dem DCS-Fenster liegt. Mit DCS im exklusiven Vollbild ist es nicht getestet. Siehst du es nicht, lass DCS im Fenster oder randlos laufen oder schalte die Textfassung im Spiel ein (siehe [Funkrad als Text im Spiel (VR)](#funkrad-als-text-im-spiel-vr), `"WheelInGame": "on"`).

#### Tasten

| Taste | Aktion |
|---|---|
| Rad-Taste: `"WheelKey": 222` (die Taste `'` auf US-Tastaturen, `Ä` auf deutschen) | Öffnen / schließen. Nur, wenn DCS das aktive Fenster ist; in anderen Programmen tippt die Taste normal. `0` = aus. |
| `1`–`9` (obere Reihe oder Ziffernblock) | Eintrag wählen |
| `Enter`, `0`, Ziffernblock `0` | Den in der Mitte gezeigten **vorgeschlagenen Spruch** senden (ENTER) |
| `Rücktaste` | Eine Ebene zurück. In der obersten Ebene schließt sie das Rad. |
| `Esc` | Schließen |
| `+` / `-` (Haupttasten oder Ziffernblock) | Radgröße in Schritten von 0,05 zwischen 0,25 und 0,95 der DCS-Fensterhöhe. Die neue Größe wird als `"WheelSize"` gespeichert (Standard `0.45`). |
| `Druck` | Screenshot mit Rad, gespeichert als `Bilder\DCS-ATC\DCS-ATC-<Datum>-<Uhrzeit>.png`. Das Rad bleibt offen. |

Solange das Rad offen ist, gehen diese Tasten nur ans Rad, nicht an DCS. Wechselst du weg von DCS (Alt+Tab), schließt sich das Rad.

**HOTAS:** `"WheelJoystick"` (Standard `-1` = aus) und `"WheelButton"` (Standard `1`) setzen. Der Knopf öffnet und schließt das Rad. Am Coolie-Hat bewegt **hoch/runter** die Markierung, **rechts** wählt, **links** geht zurück.

#### Was du siehst

- **Oberste Ebene:** ein Segment je Lotse. Unter dem Namen steht die Frequenz für deinen aktuellen Platz: die Kartenfrequenz für Ground/Tower/Approach (nach dem Start die Departure-Frequenz, wenn der Platz eine eigene hat), die AWACS-Frequenz der Mission und die Frequenz des nächsten Trägers.
- **Rote Frequenz mit ✗:** keines deiner SRS-Funkgeräte ist darauf gerastet. Rasten, sonst hört dich niemand.
- **Mitte:** der Name des Platzes (oder Trägers), mit dem das Rad spricht. Gibt es einen Vorschlag, stehen dort außerdem ein bernsteinfarbenes **ENTER**, der Name des Lotsen und der vorgeschlagene Spruch in großer Schrift.
- **Markierung:** beim Öffnen ist der Lotse des Vorschlags markiert, im Untermenü der vorgeschlagene Eintrag.
- **Mit welchem Platz das Rad spricht:** mit dem Platz, dessen Frequenz du in SRS gerastet hast (etwa 260.0 → Batumi). Sonst mit dem Platz, mit dem du schon arbeitest, sonst mit dem nächsten. Allgemein → *Platz wählen* übersteuert das bis zur Landung.

#### ENTER: die erwartete Antwort

ENTER bietet immer den Spruch an, der als Nächstes von dir erwartet wird.

**1. Die Antwort auf die letzte Frage des Lotsen.** Fragt dich ein Lotse etwas oder verlangt eine Meldung, bietet ENTER die Antwort an. Eine Frage bleibt 90 Sekunden offen, eine „report …“-Aufforderung 5 Minuten, weil du erst meldest, wenn du dort bist. Sobald du selbst irgendeinen Spruch machst, ist die offene Frage erledigt. In einer Rotte bekommen die Rottenflieger auch die offene Frage ihres Leads.

| Lotse sagt | ENTER sendet (Beispiel) |
|---|---|
| „say airspeed“ | `Approach: 250 knots` (deine angezeigte Fahrt, auf 10 kt gerundet) |
| „say altitude“ | `3400 feet` |
| „say heading“ / „verify heading“ | `heading 275` |
| „say position“ | `12 miles west of Kutaisi` |
| „say intentions“ | In der Luft: `full stop`; unter IFR: `request vectors to Kutaisi, full stop`. Am Boden: dein nächster normaler Spruch. |
| „say state“ (Trägerflugzeuge) | `state 5.4` |
| „say needles“ | Etwa `down and left` |
| „say again.“ (der Lotse hat dich nicht verstanden) | Deine letzte Anfrage noch einmal |
| „call the ball“ | `Hornet ball, 5.4` |
| „report initial …“, „report overhead“, „report in hot or checking out“ … | `initial`, `overhead`, `in hot` … Werden mehrere Meldungen genannt, gilt die erste. Enthält der nächste Schritt deines Flugs (siehe 2.) die Meldung, sendet ENTER stattdessen diesen ganzen Spruch, etwa `four mile final, gear down`. |
| „report base“ / „report final“ / „report four miles final“ | `base, gear down` / `final, gear down` / `four mile final, gear down`. Ohne „gear down“ würde Tower „check wheels down“ antworten. |
| „report C R P north“ | `Approach: C R P north` |
| „report high key“ / „report low key“ | `high key` / `low key, gear down` |
| „I show you going around, confirm? … report base.“ | `Tower: going around` (die Base-Meldung kommt danach aus dem normalen Ablauf) |
| „report leaving the control zone“ / „report clear of the zone“ | `leaving the control zone` / `clear of the zone` |
| „contact Kutaisi Approach now“ (oder Tower / Departure) | Der versäumte Check-in aus dem normalen Ablauf, etwa `airborne, climbing`, `missed approach` oder `inbound` |
| „cleared pre-contact“ | `Tanker: pre contact` |
| „report visual“ (Tanker) | `Tanker: visual` |
| „report commencing“ | `Carrier: commencing, angels 6, state 5.4` |
| „report see me“ (Case I) | `Carrier: see you at angels 2` |
| „update state“ (Case I, Marshal) | `Carrier: state 5.4` |
| „report see you at ten“ (Case II) | `Carrier: see you at ten` |
| „report off“ / „Report IP.“ (Range) | `Range: off` / `Range: IP inbound` |

Kam die Frage mit einer Übergabe („contact Kutaisi Tower …, report initial for overhead break“), sendet ENTER die Antwort an den neuen Lotsen. Nach der Übergabe an eine eigene Departure-Frequenz des Platzes heißt der Vorschlag `Departure: …`, und ENTER sendet ihn an Departure.

**2. Sonst der nächste Schritt deines Flugs:**

- Geparkt: `request startup` (bei IFR-Wetter: `request IFR clearance`), dann `request taxi`
- Am Rollhalt: `ready for departure`
- Nach dem Start: `airborne, climbing`; nach „report leaving the control zone“ noch in der Zone: `leaving the control zone`
- Im Anflug: `inbound for landing`; nahe am CRP (Kutaisi): `C R P`; nach der Übergabe an Tower: ausgerichtet im langen Endanflug (bis 12 NM, auf der Mittellinie) `9 mile final, gear down, full stop` (mit deiner Entfernung; Tower antwortet mit der Landefreigabe bzw. mit Vordermann „number 2, …“); sonst `inbound`, solange du noch weit weg bist, dann `initial`, `overhead` oder `four mile final, gear down`. Die Übergabe von Approach an Tower im Geradeaus-Anflug nennt keinen Meldepunkt mehr („contact Kutaisi Tower.“), den gibt Tower; eine „final“-Meldung auf der verlängerten Mittellinie innerhalb 12 NM gilt als Endanflug
- In der Platzrunde: `base, gear down`, dann im Endanflug `final, gear down, full stop` (bzw. `final, touch and go` bei Platzrunden)
- Nach einem Fehlanflug: `missed approach`
- Nach der Landung: `runway vacated, request taxi to parking`
- Am Träger: `Marshal, checking in`, dann `see you at angels` (Case I) oder `commencing` (Case II/III), `platform`, `see you at ten` (Case II, innerhalb 12 NM), `ball` (nach „call the ball“)

Solange du eine Start- oder Landefreigabe hast, gibt es keinen Vorschlag, denn „request startup“ oder „inbound“ würden die Freigabe aufheben.

Das folgt der Meldepflicht (siehe [Meldepflicht](#meldepflicht)): Du meldest jede Position selbst, und ENTER bietet diese Meldung im richtigen Moment an. Meldest du nicht, fordert der Lotse sie zuerst an; meldest du dann immer noch nicht, folgt die echte Konsequenz: Durchstarten, Wave-off oder keine Freigabe.

#### Alle Funkrad-Einträge

Das Rad zeigt nur die Einträge, die gerade zu deiner Lage passen: „Fahrt melden“ erst nach „say airspeed“, „Airborne“ nur kurz nach dem Start. Die Nummern 1–9 zählen die sichtbaren Einträge. Wann ein Eintrag erscheint, steht unten in der Spalte „Sichtbar“. Hat ein Lotse keinen passenden Eintrag, fehlt er in der Oberebene (etwa Ground in der Luft, Tower nach der Landung). Ohne Flugzustand (noch nicht im Flugzeug, Vorschau) zeigt das Rad alles. Allgemein, Notfall und Platz wählen sind immer da.

Ganze Lotsen fehlen außerdem, wenn sie nicht passen:

- **Range:** nur mit Range in der Mission.
- **AWACS:** nur, wenn ein AWACS deiner Seite in der Luft ist oder deine Seite ein GCI-Radar hat.
- **Tanker:** nur, wenn ein Tanker deiner Seite in der Luft ist, bei dem dein Flugzeug tanken kann.
- **Carrier:** nur mit Träger in der Mission.

Jede Ebene zeigt höchstens 9 Einträge.

**Ground**

| Eintrag | Sendet | Sichtbar |
|---|---|---|
| Request startup | `request startup` | Am Boden, geparkt |
| IFR clearance → *Nächster in Abflugrichtung* / *Platz …* | `request IFR clearance` / `request IFR clearance to <Platz>` | Am Boden vor dem Start |
| Request taxi | `request taxi` | Am Boden vor dem Rollhalt |
| Vacated, taxi to parking | `runway vacated, request taxi to parking` | Am Boden nach der Landung |
| Progressive taxi | `request progressive taxi` | Am Boden |
| Hot brakes | `hot brakes` | Am Boden |

Die IFR-Liste zeigt nahe Plätze; Träger und dein aktueller Platz stehen nicht darin.

**Tower**

| Eintrag | Sendet | Sichtbar |
|---|---|---|
| Ready for departure | `ready for departure` | Am Boden vor dem Start |
| Ready, closed pattern | `ready for departure, closed pattern` | Am Boden vor dem Start |
| Initial | `initial` | In der Luft |
| Overhead | `overhead` | In der Luft |
| Base, gear down | `base, gear down` (der Enter-Vorschlag ergänzt deine Absicht: `base, gear down, full stop` / `touch and go`) | In der Luft |
| Final, gear down, full stop | `final, gear down, full stop` | In der Luft |
| Base, touch and go | `base, gear down, touch and go` | In der Luft |
| Going around | `going around` | In der Luft |
| Closed / SFO → *Request closed / Request SFO* | `request closed traffic` / `request S F O` | In der Luft |
| Closed / SFO → *High key / Low key, gear down* | `high key` / `low key, gear down` | Nach „request S F O“ (oder wenn danach gefragt) |
| Closed / SFO → *Ready, practice approach* | `ready for departure, practice approach` | Am Boden vor dem Start |

**Approach**

| Eintrag | Sendet | Sichtbar |
|---|---|---|
| Airborne (nach Start) | `airborne, climbing` | Kurz nach dem Start, bis zum Check-in |
| Inbound for landing | `inbound for landing` | In der Luft |
| Inbound, pattern work | `inbound for pattern work, touch and go` | In der Luft |
| Request ILS / straight in | `request straight in` | In der Luft |
| Flight following | `request flight following` | In der Luft |
| Abmelden (cancel approach) | `cancel approach` | In der Luft, nicht vor dem Check-in nach dem Start |
| Verkehr → Traffic in sight | `traffic in sight` | Bis 2 Minuten nach einem Verkehrshinweis an dich |
| Verkehr → Negative contact | `negative contact` | Bis 2 Minuten nach einem Verkehrshinweis an dich |
| C R P | `C R P` | Nach „report C R P“ bzw. nahe dem CRP beim CRP-Anflug |
| Fahrt melden | Deine IAS auf 10 kt gerundet, etwa `250 knots` | Nach „say airspeed“ |
| Say again | `say again` (an Approach) | Immer, wenn Platz ist (max. 9) |
| Request higher | `request higher` | In der Luft, wenn Platz ist (max. 9) |

**Range:** Check in (`checking in`) in der Luft bis zum Check-in; danach IP inbound, In hot, Off safe, Check out (`checking out`), Check out, hung ordnance (`checking out, hung ordnance`).

**AWACS**

| Eintrag | Sendet | Sichtbar |
|---|---|---|
| Check in | `checking in` | In der Luft, nicht eingecheckt |
| Picture | `request picture` | In der Luft |
| Bogey dope | `bogey dope` | In der Luft |
| Sort | `request sort` | In der Luft |
| Nächster Tanker | `vector to tanker` (die Antwort nennt Tanker und Frequenz) | In der Luft |
| Vektor nächster Platz | `vector to nearest airfield` | In der Luft |
| Check out | `checking out` | Eingecheckt |

**Tanker:** Request rejoin (in der Luft, vor dem Rejoin), Visual (im Rejoin), Observation (im Rejoin und in der Beobachtung), Pre-contact (`pre contact`) und Refuel complete (nach „cleared to join“).

**Carrier:** Marshal check in (in der Luft, DCS-ATC setzt deinen vollständigen Check-in ein) und Pigeons (in der Luft); nach dem Marshal-Check-in: See you at (`see you at angels`), Initial, Commencing, Platform, Ball, Clara. Einzelheiten unter [Carrier-Betrieb](#carrier-betrieb).

**Notfall**

| Eintrag | Sendet |
|---|---|
| MAYDAY → *Triebwerksausfall / Treibstoff / Hydraulik / Gefechtsschaden / Medizinisch / Vogelschlag* | `mayday mayday mayday, <Art>, request immediate landing` (Treibstoff: `mayday mayday mayday fuel, emergency fuel, request immediate landing`) |
| PAN PAN → *dieselben Arten ohne Treibstoff* | `pan pan, pan pan, pan pan, <Art>, request priority landing` |
| Spritmangel | `minimum fuel` (kein Notfall, kein Vorrang) |
| Hung ordnance | `hung ordnance` (an Approach) |
| Flameout, high key | `flameout, high key` (an Tower; echter Flameout, gilt als Notfall; danach Enter: `low key, gear down`) |
| Notfall beenden | `cancel emergency` |

Die Pilotenstimme ergänzt Position, Höhe und Kurs, und DCS-ATC wählt den nächsten geeigneten eigenen Platz (möglichst mit mindestens 1800 m Bahn):

> *MAYDAY MAYDAY MAYDAY, Kutaisi Approach, Enfield 1 1, engine failure, 20 miles west of Kutaisi, 6000 feet, heading 090, request immediate landing.*

**Allgemein**

| Eintrag | Funktion |
|---|---|
| Platz wählen | Listet die nächsten Plätze und Träger. Das Rad spricht dann bis zur Landung mit dem gewählten (Text: „ATC: jetzt Batumi“). Ein Träger bringt dich zu seinem Marshal. Feindliche Plätze werden mit Texthinweis abgelehnt. |
| Radio check | `radio check` |
| Say again | `say again` |
| QNH / weather | `request weather` |
| ATIS hören | Liest die aktuelle ATIS deines Platzes. Ohne Wetterdaten in der Mission kommt stattdessen ein Texthinweis. |
| Request zone transit | `request zone transit` (an Approach) |
| Debriefing | Zeigt dein bisheriges Debriefing als Text im Spiel |
| Einstellungen | Öffnet das Einstellungsfenster |

#### Funkrad im Multiplayer (Mitspieler)

Mitspieler mit installiertem DCS-ATC bekommen das Rad auch (gleiche Taste). Ihre Anfragen gehen über den DCS-Chat an den Host, niemand sieht diese Chatzeilen. Das geht auch auf einem Dedicated Server. Das Mitspieler-Rad hat keinen ENTER-Vorschlag, zeigt keine Frequenzen (nur der Host kennt die Platz- und `frequencies.jsonc`-Frequenzen), hat keine Range- und Carrier-Einträge und keinen VR-Text im Spiel. Es kennt auch deinen Flugzustand nicht und zeigt deshalb alle Einträge wie bisher (Approach ohne Say again und Request higher, höchstens 9). Siehe [Client-Modus](#client-modus-mitspieler-mit-eigenem-dcs-atc).

### Funkrad als Text im Spiel (VR)

Eine VR-Brille zeigt nur DCS, das Overlay siehst du dort nicht. DCS-ATC kann das Rad als Text oben im Spielbild spiegeln, nur für dein Flugzeug sichtbar:

```
ENTER ▸ Tower: ready for departure
DCS-ATC – Tower
   1 Ready for departure
> 2 Ready, closed pattern
   3 Initial
   ...
```

- Die erste Zeile ist der ENTER-Vorschlag.
- `>` markiert den gewählten Eintrag. In der obersten Ebene stehen die Frequenzen neben den Einträgen.
- Der Text folgt jedem Tastendruck und verschwindet beim Schließen des Rads.
- Bedienung mit denselben Tasten bzw. HOTAS wie das normale Rad. Für VR Rad-Taste und Hat auf das HOTAS legen.

| Einstellung | Standard | Werte |
|---|---|---|
| `"WheelInGame"` | `"auto"` | `"auto"` = wenn DCS im VR-Modus läuft (VR in den DCS-Optionen oder Startparameter `--force_enable_VR`), `"on"` = immer, `"off"` |

Dieselbe Wahl im Einstellungsfenster → *Funkrad im Spiel (VR)*: automatisch / immer / aus.

### F10-Menü

In jeder Mission mit DCS-ATC bekommt jede Spielergruppe **F10 Andere → ATC**. Teilen sich mehrere Spieler eine Gruppe, bekommt jeder ein eigenes Untermenü mit seinem Rufzeichen. Die Menüsprache folgt deiner Wahl im Setup (Deutsch oder Englisch).

| Untermenü | Einträge |
|---|---|
| Ground | Request startup · Request taxi · Runway vacated, taxi to parking · Progressive taxi · Hot brakes · IFR clearance → *Nächster in Abflugrichtung* / bis zu 8 eigene oder neutrale Plätze, nächster zuerst |
| Tower | Ready for departure · Ready, closed pattern · Initial · Overhead · Base, gear down · Final, gear down, full stop · Base, touch and go · Going around · Request closed |
| Approach | Airborne (nach Start) · Inbound for landing · Inbound, pattern work · Request straight in · C R P |
| AWACS / Tanker | AWACS: picture · AWACS: bogey dope · AWACS: nearest tanker · Tanker: request rejoin · Tanker: pre-contact |
| Notfall | MAYDAY → Art · PAN PAN → Art · Spritmangel · Hung ordnance · Flameout, high key · Notfall beenden (Arten: Triebwerksausfall, Treibstoff (nur MAYDAY), Hydraulik, Gefechtsschaden, Medizinisch, Vogelschlag) |
| Allgemein | Radio check · Say again · QNH / weather · ATIS hören · Request zone transit · Debriefing |
| Einstellungen | Bodenpersonal an/aus · Anzeigen |

F10-Sprüche werden wie Rad-Sprüche behandelt, mit Pilotenstimme und Platzwahl. Das F10-Menü hat weniger Einträge als das Rad: keine Range, kein Carrier, kein AWACS-Check-in/-out oder Sort und kein Flight Following. Dafür Rad oder Stimme nehmen.

Läuft DCS-ATC nicht, kommt *„DCS-ATC läuft nicht – Startmenü -> DCS-ATC starten, dann erneut senden.“*

### Tippen im Chat (Multiplayer)

In einer Multiplayer-Mission geht eine Chatnachricht, die mit `ATC>` beginnt, an DCS-ATC beim Host statt in den Chat. Es gehen dieselben Texte wie im Rad:

```
ATC>Ground: request taxi
ATC>Tower: ready for departure
ATC>radio check
ATC>debrief
```

Der Teil vor dem Doppelpunkt (`Ground:`, `Tower:`, `Approach:`, `AWACS:`, `Tanker:`, `Range:`, `Carrier:`) wählt den Lotsen. Ohne ihn entscheidet wie beim Sprechen der Text selbst. Der Chat ist für die Mitspieler gedacht; am Host-PC nimm das Rad oder das F10-Menü. `ATC>` im Chat als Host zu tippen ist nicht getestet.

### Text im Spiel

Jeder Lotsenspruch erscheint zusätzlich als DCS-Text:

```
>>> Kutaisi Tower (263.0): Enfield one one, Kutaisi Tower, runway two five, cleared for takeoff, wind calm.
Enfield 1-1: Kutaisi Tower, Enfield 1 1, ready for departure.
```

- **Format:** Station und Frequenz zuerst. `>>>` markiert Sprüche an deine eigene Gruppe. Deine eigenen Pilotenstimmen-Sprüche stehen mit deinem Rufzeichen da.
- **Wer ihn sieht:** nur Gruppen mit einem Spieler, der in SRS auf diese Frequenz gerastet ist. Kennt SRS deine Funkgeräte nicht, siehst du alles. Guard erreicht alle.
- **Wie lange:** mindestens 20 Sekunden, bei langen Texten länger. KI-Rottenfunk nur 6–8 Sekunden.
- **Hinweise sind nur Text, nie gesprochen:** falsche Frequenz, feindlicher Platz, „ATC: jetzt …“, Debriefing, Tanken über die DCS-Bodencrew und Ähnliches.
- **Verspätete Sprüche werden nur Text:** käme ein Spruch zu spät (mehr als 45 Sekunden in der Warteschlange, bei AWACS 20 Sekunden; bei AWACS-threat, merged, leaker und furball 12 Sekunden), siehst du ihn nur als Text. Ein AWACS-Spruch mit BRAA, der warten musste, nennt Richtung und Entfernung vom Moment des Sendens; nach einem *fox* oder *defensive* gehen 5 Sekunden lang nur dringende Sprüche raus.
- **Reihenfolge auf einer vollen Frequenz:** Notfälle und *defending* zuerst, dann die kurzen Gefechtssprüche der Flüge (*fox*, *pitbull*, *splash* …), dann AWACS-*threat*/*merged*/*leaker*/*furball*/*pop-up*, dann *commit*/*targeting* und Antworten, dann Picture, new group, faded und on station, dann übriger KI-Funk. Ein langes Picture in der Warteschlange hält ein *pitbull* nicht mehr auf.

### Stimmen, Funkklang und Sprechtempo

Die Stimmen sind Piper-Modelle im Ordner `models` der Installation (`%LOCALAPPDATA%\Programs\DCS-ATC\models`). Ändern in config.jsonc:

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

So funktionieren die Werte:

- **`@` setzt die Tonhöhe:** `@0.88` = tiefer, `@1.05` = höher.
- **`|` nennt mehrere Stimmen:** jede Station (etwa „Kolkhi Tower“) behält eine davon, benachbarte Plätze klingen verschieden.
- **Fehlende Stimmen:** fehlt eine Stimmdatei, wird eine vorhandene genommen (sonst `en_US-ryan-medium`). Mehr Stimmen: huggingface.co/rhasspy/piper-voices.
- **Crew:** der Crew Chief am Parkplatz, der über deine Bordsprechanlage spricht.
- **Carrier:** nicht in der Vorlage; der Standard `"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02"` wird automatisch ergänzt.
- **`PilotVoice`:** spricht deine Rad-, F10- und Chat-Sprüche vor der Antwort. Jeder Spieler bekommt eine leicht andere Tonhöhe. `""` = aus, wie das Abwählen von Einstellungsfenster → *Eigene Anfragen vorsprechen (Pilotenstimme)*.

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `"RadioFx"` | `true` | Funkklang: Bandpass, leises Rauschen, Squelch-Ende. Im Gefecht kommen Sprüche schneller und klingen wie ein übersteuertes Maskenmikrofon. |
| `"Volume"` | `0.6` | Lautstärke der Lotsen in SRS (0–1). Senken, wenn die Stimmen zu laut sind. |
| `"SpeechRate"` | `0.85` | Sprechtempo: 1 = Piper-Standard, kleiner = schneller, größer = langsamer. Der Regler im Einstellungsfenster geht von 0,70 bis 1,50. Alle Stimmen sind auf dasselbe Tempo abgeglichen. |
| `"ReplyPause"` | `1.5` | Sekunden Pause zwischen zwei Sprüchen auf einer Frequenz (Pilot → Lotse → Readback). Regler im Einstellungsfenster: 0–5 s. |

Lautstärke, Tempo, Pause, Funkklang und Pilotenstimme gelten sofort nach dem Speichern im Einstellungsfenster (Startmenü → **DCS-ATC → Einstellungen**).

### Höhenmesser-Einstellung: hPa oder inHg

Lotsen nennen die Höhenmessereinstellung in der Einheit deines Flugzeugs:

- **inHg:** *„altimeter two niner niner two“* für westliche Typen: F-14, F/A-18, F-16, F-15, F-4E, F-5, F-86, F-117, A-10, AV-8B, A-4, A-6, T-45, S-3, E-2, E-3, C-130, C-17, KC-135, B-1, B-52, AH-64, UH-1, UH-60, OH-58, CH-47, P-51/TF-51, P-47, F4U, Christen Eagle.
- **hPa:** *„QNH one zero one three“* für alle anderen (MiG, Su, Mirage, Viggen, JF-17, Ka, Mi, britische und deutsche Warbirds …).
- **ATIS** geht an alle und nennt deshalb beides: *„QNH one zero one three, altimeter two niner niner two“*.

| Einstellung | Standard | Werte |
|---|---|---|
| `"AltimeterUnit"` | `"auto"` | `"auto"` (nach Flugzeugtyp; ATIS nennt beides), `"hpa"`, `"inhg"` (alle Sprüche und die ATIS nur in dieser Einheit) |

Dieselbe Wahl im Einstellungsfenster → *Höhenmesser-Einstellung*: automatisch (nach Flugzeugtyp) / hPa (QNH) / inHg (altimeter).

### Bahnbefeuerung

DCS schaltet die Befeuerung eines Platzes erst ein, wenn jemand dessen eingebauten DCS-Lotsen anfunkt. Das erledigt DCS-ATC im Hintergrund, indem es kurz das DCS-Funkmenü bedient. Du musst den DCS-Lotsen nicht selbst rufen.

**Wann es passiert:**

- Du bist Missions-Host (oder fliegst allein) und arbeitest in DCS-ATC mit einem Platz.
- Du bist innerhalb von 20 NM um diesen Platz und im Anflug (Approach, Initial, Platzrunde, Landefreigabe) oder am Boden beim Rollen zum Start oder am Rollhalt.
- Es ist Dämmerung oder Nacht (Sonne tiefer als 6° über dem Horizont) oder IFR-Wetter.
- Höchstens einmal alle 15 Minuten je Platz und Lage.

**Wann nicht:**

- Ein DCS-Lotsendialog ist schon offen, etwa nach einem Heißstart auf der Bahn.
- DCS „Easy Communication“ ist aus und keines deiner Funkgeräte ist auf die Kartenfrequenz des Platzes (UHF oder VHF) gerastet.
- Der Platz ist nicht unter den 10 Einträgen, die das DCS-Menü zeigt.

| Einstellung | Standard | Werte |
|---|---|---|
| `"RunwayLights"` | `"silent"` | `"silent"` = Licht an, der DCS-Lotse bleibt stumm. `"dcs"` = die Antwort des DCS-Lotsen ist 15 Sekunden hörbar. `"off"` = nie. |
| `"CommsKey"` | `43` | DCS-Funkmenü-Taste als Scancode (43 = `\` auf US-Tastaturen, `#` auf deutschen). `0` = aus. Diese Taste bedient auch das DCS-Tankermenü. |

Die Befeuerung gibt es auch im Einstellungsfenster → *Bahnbefeuerung (nachts)*: stumm / mit DCS-Lotse (hörbar) / aus. Sie gilt ab dem nächsten Start von DCS-ATC.

## Flugplatz-ATC

DCS-ATC stellt an jedem Platz der Karte ATIS, Ground, Tower, Approach und Departure. Es nutzt nur Einheiten, die schon in der Mission sind, und spawnt nie Flugzeuge. Dieses Kapitel deckt den Platzbetrieb vom Anlassen bis zum Parken ab.

### Plätze, Rufnamen und Frequenzen

**Welche Plätze ATC haben**

- **Jede Karte, jeder Platz mit Bahn.** Bahnen, Platzhöhe und Parkplätze kommen live aus DCS. Kutaisi nutzt zusätzlich die DCS-Kneeboard-Karten: CRPs, Rollwegnamen, Vorfelder und eine Platzrunde, die immer südlich der Bahn liegt. Alle anderen Plätze nutzen ein generisches Verfahren aus dem Gelände.
- **Nur eigene und neutrale Plätze antworten.** Ein feindlicher Platz schweigt. Stattdessen kommt ein Texthinweis mit dem nächsten eigenen Platz als missweisende Peilung und Entfernung, etwa `Senaki-Kolkhi 270/35 NM`. Wird ein Platz während deines Anflugs erobert, schickt Approach dich zum nächsten eigenen Platz („Kutaisi is closed, divert Senaki …“).
- **Stationsnamen** kommen aus den Funkdaten der Karte. Senaki-Kolkhi ist zum Beispiel „Kolkhi Tower“, Kutaisi „Kutaisi Tower“. Bindestriche in Platznamen werden als Leerzeichen gesprochen.

**Frequenzen**

- **Jeder Platz hat seine eigene Frequenz.** DCS-ATC liest UHF und VHF jedes Platzes von der DCS-Karte. Ground, Tower und Approach teilen sich diese Frequenzen, wie in DCS. Plätze, die in DCS nur eine VHF-Frequenz haben (fast die ganze Karte Persischer Golf, etwa Abu Dhabi Intl 119.2), nutzen diese. Plätze ganz ohne DCS-Frequenz nutzen die Lotsenfrequenzen aus `config.jsonc` (siehe unten).
  - Weil sie sich die Frequenz teilen, nennt eine Übergabe keine neue Frequenz: „contact Kutaisi Tower“.
  - Rufst du den falschen Lotsen, antwortet der richtige auf derselben Frequenz.
- **ATIS** sendet auf eigener Frequenz, Standard 263.5 (siehe [ATIS](#atis)).

| Lotse | Frequenz | Zuständig für |
|---|---|---|
| ATIS | `Frequencies.ATIS` (263.5) oder je Platz | Bahn, Wind, Sicht, Wolken, Temperatur, QNH und Altimeter, Kennbuchstabe. Dauersendung |
| Ground | Platzfrequenz | Anlassen, IFR-Freigabe, Rollen zum Start, Rollen zum Parkplatz |
| Tower | Platzfrequenz | Aufrollen, Start, Platzrunde, Landung, Zonenwache |
| Approach | Platzfrequenz | Radarführung, Warteschleife, Instrumentenanflüge, Abflug-Check-in, Flight Following, Zonendurchflug |
| Departure | Wie Approach, sofern nicht eigens gesetzt | Approach nach der Übergabe vom Tower (siehe unten) |

**Wenn die Karte keine Frequenzdaten hat**

Findet DCS-ATC deine DCS-Installation oder die Funkdaten der Karte nicht, nimmt es die Lotsenfrequenzen aus `config.jsonc`:

```jsonc
"Frequencies": { "ATIS": 263.5, "Ground": 264.5, "Tower": 265.0, "Approach": 266.5, ... }
```

Diese gelten dann für jeden Platz gleich. Übergaben nennen dann die Frequenz: „contact Kutaisi Tower two six five decimal zero“.

**Eigene Frequenzen je Platz**

In `frequencies.jsonc` kannst du einem Platz für jeden Lotsen (ATIS, Ground, Tower, Departure, Approach) eine eigene Frequenz geben, zum Beispiel die echten. Ein Ruf auf so einer Frequenz geht immer an diesen Lotsen, egal was du sagst. Auf der Kartenfrequenz entscheidet weiter der Inhalt. Mit einem `Departure`-Eintrag übergibt Tower mit „contact Nellis Departure two seven three decimal five five“. Änderungen gelten sofort nach dem Speichern. Format und alle Regeln unter [Eigene Frequenzen je Platz](#eigene-frequenzen-je-platz).

**Mit welchem Platz du sprichst**

DCS-ATC wählt den Platz in dieser Reihenfolge:

1. Der Platz, den du nennst: „Batumi Tower, Enfield 1-1, …“. Ein Name nach „contact …“ zählt nicht.
2. Der Platz, den du im Funkrad unter **Allgemein → Platz wählen** gewählt hast. Die Wahl gilt bis zur Landung.
3. Der Platz, auf dessen Frequenz dein SRS-Funkgerät gerastet ist. Ein gerade neu gerasteter Platz hat Vorrang.
4. Der Platz, mit dem du schon arbeitest. Nach dem Abflug bis zur Abmeldung, höchstens 25 NM.
5. Der nächste eigene oder neutrale Platz.

Über SRS hört DCS-ATC auf den Frequenzen deines aktuellen Platzes und der zwei nächsten mit.

**Falsche Station gerufen**

- **Andere Frequenz** (nur, wenn die Lotsen getrennte Frequenzen haben, siehe oben): die Station sagt dir, wohin. „Enfield one one, Kutaisi Tower, contact Kutaisi Ground two six four decimal five.“
- **Anderer Platz auf dieser Frequenz:** „Enfield one one, this is Kolkhi Tower, you are on Senaki Kolkhi frequency, contact Kutaisi Tower one three four decimal zero.“

### Funk-Grundlagen

- **Format:** Station, dein Rufzeichen, Nachricht. „Kutaisi Ground, Enfield 1-1, request startup.“
- **Dein Rufzeichen** kommt aus deinem DCS-Slot oder aus `MyCallsign` in den Einstellungen.
- **Rotten:** Spieler derselben Gruppe im Verband werden als Rotte angesprochen („Enfield one one flight“). Freigaben gelten für die ganze Rotte.
- **Readbacks, „say again“, „confirm“, Radio Check und „unable“** gehen bei jedem Lotsen gleich: siehe [Grundlegende Phrasen](#grundlegende-phrasen).
- **Wie du sendest** (SRS, Sprechtaste, Funkrad, F10, Chat): siehe [Wege zu funken](#wege-zu-funken).

### ATIS

Die ATIS sendet dauerhaft für jeden Platz mit einem Spieler im Umkreis von 40 NM. Teilen sich mehrere Plätze eine ATIS-Frequenz, wechseln sie sich ab. Das Wetter kommt aus der Mission.

> **ATIS:** Kutaisi information Alpha, time zero eight zero zero Zulu. Runway two five in use, left hand pattern, 2000 feet. Wind two four zero degrees, eight knots. Visibility one zero kilometers or more, sky clear. Temperature one five. QNH one zero one three, altimeter two niner niner two. Tower one three four decimal zero, UHF two six three decimal zero. Advise on initial contact you have information Alpha.

- **Wolken** werden als Wolkenuntergrenze über dem Platz genannt („clouds 2500 feet“). **Wind** ist missweisend, unter 3 kt „wind calm“.
- **Höhenmessereinstellung:** die ATIS nennt QNH (hPa) und Altimeter (inHg). Lotsen nennen die passende für dein Flugzeug (siehe [Höhenmesser-Einstellung](#höhenmesser-einstellung-hpa-oder-inhg)).
- **Instrumentenwetter:** die ATIS ergänzt „Instrument conditions, expect vectors for ILS approach runway two five, localizer …“. Ohne ILS heißt es „P A R approach“. Wenn das Gelände es verlangt, kann es die Gegenbahn oder ein Circling sein.
- **Neuer Buchstabe:** der Buchstabe wechselt, wenn sich Bahn, Wind, QNH oder Sicht um eine Stufe ändern, oder zur vollen Missionsstunde. Alle am Platz (am Boden oder innerhalb 20 NM) hören: „Attention all aircraft, Kutaisi information Bravo now current, QNH …“.
- **ATIS melden:** den Buchstaben beim Erstanruf nennen („information Alpha“, „with Alpha“, „have Alpha“). Ohne oder mit altem Buchstaben ergänzt der Lotse „Information Bravo is current“ plus Bahn und QNH.
- **Auf Abruf:** Rad/F10 **Allgemein → ATIS hören** spielt die ATIS sofort ab.
- **Abschalten:** `"Atis": false`.

### Bahnwahl

- **Wind bis 3,5 m/s (etwa 7 kt):** die DCS-Hauptbahn ist aktiv. Nutzen KI-Flugzeuge bei schwachem Wind die Gegenrichtung, folgt DCS-ATC ihnen.
- **Stärkerer Wind:** die Bahn mit dem meisten Gegenwind. So landet auch die DCS-KI.
- **Parallelbahnen** heißen nach Lage L/R/C, etwa 30L/30R. Landungen gehen auf die Seite der Hauptbahn.
- **Wann die Bahn wechselt:** nur, solange du parkst oder nicht mit dem Platz arbeitest. Ein laufendes Verfahren behält seine Bahn.
- **Bahn anfordern:** „request runway 07“ oder „runway 30 left“. Am Boden mit Anlass-, Roll- oder Freigabeanfrage, in der Luft im Anflug. Du bekommst sie bei höchstens 10 kt Rückenwind:
  - Ground: „runway zero seven approved, …“
  - In der Luft: „runway zero seven approved“, Approach plant die Führung neu.
  - Mehr Rückenwind: „Unable runway zero seven, tailwind one two knots, runway two five in use.“
  - Eine Bahnanfrage für den Abflug gilt nicht für die Landung.
- **Platzrundenrichtung:**
  - Kutaisi: immer südlich der Bahn, also Bahn 07 Rechts-, Bahn 25 Linksplatzrunde.
  - Sonst: links, außer das Gelände links ist mehr als 300 ft höher als rechts.
  - Lässt das Gelände gar keine Platzrunde zu (etwa Khasab), gibt es nur Geradeaus-Anflüge.

### Wetter: VFR und IFR

**IFR-Bedingungen**

Der Platz ist **IFR**, wenn die Missionssicht unter 5 km oder die Wolkenuntergrenze unter 600 m (etwa 2000 ft) über NN liegt. Unter IFR:

- Kein Initial und kein Overhead. „initial“ bekommt „negative, field is I F R. Expect vectors for ILS approach runway two five, localizer …, report inbound to Kutaisi Approach.“ „overhead“ bekommt „negative, field is I F R, no overhead.“
- Approach führt dich auf ein **ILS**, wenn die Bahn eins hat, sonst auf einen **PAR**-Anflug mit Gleitweg-Ansagen.
- „cancel IFR“, „VFR“ und „request visual approach“ werden abgelehnt.
- Touch-and-go und Low Approach werden radargeführte Instrumentenrunden.

**Schlechtes Wetter beim Erstanruf**

Liegt die Sicht unter 5 km oder die Wolkenuntergrenze unter 1000 ft, ergänzt der Lotse beim Erstanruf: „Visibility 3 kilometers, ceiling 800 feet.“ Unter 550 m Sicht oder 200 ft Untergrenze zusätzlich „Say intentions.“

**Standardmäßig geradeaus**

**Nachts** (Sonne mehr als 6° unter dem Horizont) und für Typen ohne Overhead Break gibt „inbound“ einen Geradeaus- oder Instrumentenanflug. Das sind Hubschrauber, C-130/C-17, C-47, An-/Il-/Tu-, B-1/B-52, KC-Tanker, E-2/E-3 und S-3. Mit „overhead“, „VFR“ oder „cancel IFR“ gibt es trotzdem den Overhead Break. („request visual approach“ gibt einen Sichtanflug geradeaus.)

### Ground

Alle Ground-Anfragen gehen nur innerhalb von 5 km um den Platz. Weiter weg kommt der Hinweis, nach dem Start Approach zu rufen.

#### Anlassen

> **Du:** Kutaisi Ground, Enfield 1-1, request startup, information Alpha.  
> **Ground:** Enfield one one, Kutaisi Ground, start up approved. Report ready to taxi.

Nennst du beim ersten Anruf den aktuellen ATIS-Buchstaben nicht, ergänzt Ground: „Information Alpha is current, runway two five in use, QNH one zero one three.“

Als Anlassanfrage zählen: „request startup“, „start up“, „request engine start“, „ready to start“. „Engines started“ nicht.

#### IFR-Freigabe

> **Du:** Kutaisi Ground, Enfield 1-1, request IFR clearance to Batumi.  
> **Ground:** Enfield one one, Kutaisi Ground, cleared to Batumi airport via radar vectors, after departure fly runway heading, climb and maintain 3000 feet, expect flight level one five zero ten minutes after departure, departure Kutaisi Approach, squawk four two one six.  
> **Du:** Cleared to Batumi, runway heading, 3000 feet, squawk 4216, Enfield 1-1.  
> **Ground:** Enfield one one, readback correct.

- **Freigabegrenze:** ohne Ziel der nächste eigene Platz in Abflugrichtung. Das F10-Menü **Ground → IFR clearance** listet Plätze nach Entfernung.
- **Reiseflughöhe** nach Halbkreisregel: FL150 ostwärts, 14.000 ft westwärts. Ein Ziel näher als 40 NM bekommt eine niedrige Höhe. Nie unter Gelände plus 2000 ft.
- **Feindliches Ziel:** „unable clearance to …, say alternate destination.“
- **Readback-Prüfung:** geprüft wird nur der Squawk im Readback („squawk 4216“).
- **Eigene Departure-Frequenz:** hat der Platz eine in `frequencies.jsonc`, sagt die Freigabe „departure frequency two seven three decimal five five“ statt „departure Kutaisi Approach“.
- **In der Luft:** „request IFR to Senaki“ gibt eine IFR-Freigabe von Approach.

#### Rollen zum Start

> **Du:** Kutaisi Ground, Enfield 1-1, request taxi, departure west.  
> **Ground:** Enfield one one, Kutaisi Ground, taxi to holding point runway two five via November, Delta. Squawk four two one six. Hold short runway two five, report ready for departure.

- **Route:** nur Kutaisi nennt Rollwege. Andere Plätze sagen „taxi to holding point runway two five“.
- **QNH:** nur beim ersten Anruf und nur ohne ATIS-Buchstaben. „request startup and taxi“ beantwortet beides auf einmal.
- **Squawk:** ein VFR-Squawk kommt mit der Rollfreigabe. Mit IFR-Freigabe hast du schon einen.
- **Eigene Tower-Frequenz:** „Hold short runway two five, contact Kutaisi Tower … when ready for departure.“
- **Progressive Taxi:** beim Rollen gibt „request progressive taxi“ Richtungsangaben zum Rollhalt, etwa „holding point runway two five at your 2 o'clock, 450 meters“. Sie werden jedes Mal erneuert, wenn sich die Entfernung halbiert, und enden mit „…, hold short of runway two five“.

**Warnungen am Boden**

| Lage | Ground bzw. Tower sagt |
|---|---|
| Losrollen vom Parkplatz ohne Rollfreigabe (schneller als 5 kt, mehr als 100 m) | „hold position, say intentions.“ |
| Schneller als 30 kt auf dem Rollweg | „reduce taxi speed.“ |
| Kreuzender oder entgegenkommender Verkehr mit Vorfahrt (rechts vor links; der KI lässt du immer den Vortritt) | „hold position, give way to the Hornet on your right.“ … später: „continue taxi.“ |
| Verkehr voraus in gleicher Richtung | „follow the Viper ahead.“ |
| „ready“ vor der Rollfreigabe | „negative, you are not cleared to taxi. Request taxi.“ |
| „ready“ noch während des Rollens | „negative, you are not at the holding point. Continue taxi to holding point runway two five …, report ready for departure.“ |

#### Nach der Landung: Rollen zum Parkplatz

> **Tower:** Enfield one one, welcome to Kutaisi. Vacate right via Alpha or Bravo. Report vacated.  
> **Du:** Kutaisi Ground, Enfield 1-1, runway vacated, request taxi to parking.  
> **Ground:** Enfield one one, taxi to Ramp North via November, parking spot one two, at your 2 o'clock, 350 meters.

- **Abrollanweisung:** an Plätzen ohne Karte „Vacate runway when able“. Mit eigener Ground-Frequenz: „Contact Kutaisi Ground … when vacated.“
- **Verkehr hinter dir:** ist innerhalb 3 NM jemand im Endanflug hinter dir, ergänzt Tower „Expedite vacating, traffic F-16 on 2 mile final.“
- **Zu früh gefragt:** fragst du noch auf der Bahn nach Rollen, kommt „report runway vacated.“
- **Kein Anruf:** rufst du 30 s nach dem Abrollen nicht, erinnert Tower einmal: „contact Kutaisi Ground.“
- **Parkplatz:** dein eigener, wenn frei, sonst der nächste freie. Plätze anderer Spieler werden übersprungen. Ground gibt die Richtung einmal und erneut nur, wenn du abdriftest (oder mit „progressive“ bei jeder Halbierung der Entfernung).
- **Auf deinem Platz:** „you are on your parking position, shut down at your discretion.“
- **Wann du als geparkt giltst:** nach 30 s Stillstand auf dem Vorfeld oder wenn du auf einem Parkplatz die Triebwerke abstellst. Das beendet den Flug und schreibt das Debriefing.

### Tower: Abflug

#### Startbereit

> **Du:** Kutaisi Tower, Enfield 1-1, holding short runway 25, ready for departure.  
> **Tower:** Enfield one one, after departure exit via C R P west, not above 2200 feet until leaving the control zone, runway two five, wind two four zero degrees, eight knots, cleared for takeoff.

Du kannst „ready“, „ready for departure“, „holding short“ oder „holding point“ sagen. Du musst innerhalb von 700 m vor der Schwelle oder auf einer Bahn sein.

**Varianten der Startfreigabe**

| Dein Plan | Teil nach dem Start |
|---|---|
| VFR, Kutaisi | „exit via C R P west, not above 2200 feet until leaving the control zone“. Der CRP ist die Richtung, die du genannt hast; ohne Angabe Bahn 25 → West, 07 → Ost |
| VFR mit Richtung, andere Plätze („request taxi, departure north“) | „leave the control zone northbound, not above …“ |
| VFR ohne Richtung, andere Plätze | „own navigation, not above … until leaving the control zone“ |
| IFR-Freigabe | „fly runway heading, climb and maintain 3000 feet“ |
| Platzrunde („ready for departure, closed pattern“) | „runway two five, wind …, cleared for takeoff, left closed traffic approved, report base.“ |
| Übungsanflüge bei Instrumentenwetter („ready for departure, practice approach“) | „after departure fly runway heading, climb and maintain 3000 feet, …, cleared for takeoff“, nach dem Abheben „contact Approach“, nach „airborne, climbing“ Radarführung. Reines „closed pattern“: „closed traffic not approved, I F R conditions.“ |

**Wenn Tower dich warten lässt**

| Lage | Tower sagt | Was dann passiert |
|---|---|---|
| Landender Verkehr innerhalb 4 NM | „hold short runway two five, traffic Hornet on 3 mile final.“ | Tower gibt dich **automatisch** frei, sobald der Verkehr weiter als 4 NM ist, oder nach 3 min, wenn er weiter als 2,5 NM ist |
| Flugzeug rollt auf der Bahn, niemand im Endanflug | „runway two five, line up and wait, traffic on the runway.“ | Automatische Freigabe, wenn die Bahn frei ist. Mit Verkehr im Endanflug 2,5–6 NM: „cleared for immediate takeoff, traffic …“ |
| Landender Gegenverkehr | „hold short runway two five, opposite direction traffic, … runway zero seven.“ | Automatische Freigabe, wenn frei |
| Anderes Flugzeug schon freigegeben oder aufgerollt | „hold short runway two five, number 2 for departure.“ | Automatische Freigabe, wenn frei |
| MAYDAY-Verkehr innerhalb 15 NM | „hold short runway two five, emergency traffic inbound.“ | Automatische Freigabe, wenn frei |

Du musst nicht erneut rufen. Tower ruft dich.

**Was einen Start aufhebt oder stoppt**

| Lage | Tower sagt |
|---|---|
| Vor dem Startlauf (unter 30 kt): Verkehr rollt auf die Bahn, ein Landender ist innerhalb 1,5 NM oder Gegenverkehr innerhalb 4 NM | „hold position, cancel takeoff clearance, traffic on the runway.“ |
| Du rollst 30 s vom Rollhalt weg | „takeoff clearance cancelled, contact Kutaisi Ground.“ |
| Du sagst „abort“, „rejecting“, „stopping“ oder „cancel takeoff“ | „roger. Vacate … Report ready for departure.“ |
| Du rollst ohne Freigabe auf eine Bahn | „hold position! You are not cleared onto runway two five.“ (Verstoß) |
| Startlauf ohne Freigabe (über 30 kt; nach „line up and wait“ über 40 kt) | „stop immediately, I say again, stop immediately! You are not cleared for takeoff.“ (Verstoß) |
| In der Luft ohne Freigabe | „you departed without takeoff clearance. Report intentions.“ (Verstoß) |

**Spawn auf der Bahn**

Spawnst du auf der Bahn, giltst du als aufgerollt und wartend. Mit „ready for departure“ bekommst du die Freigabe; ohne diese Meldung gibt Tower sie nie, egal wie lange du wartest. Beginnst du den Startlauf ohne Freigabe, kommt über 40 kt „stop immediately, … You are not cleared for takeoff.“ und ein Verstoß (siehe [Meldepflicht](#meldepflicht)).

**Wirbelschleppen**

Ist ein schweres Flugzeug (KC-135, KC-10, E-3, A-50, Il-76/78, C-17, C-5, B-52, B-1B, Tu-95/142/160, An-124, KJ-2000) gerade auf derselben Bahnachse gestartet oder gelandet oder vor dir im Endanflug, ergänzt Tower „Caution wake turbulence.“

#### Übergabe und Departure-Check-in

Bei etwa 500 ft über dem Platz übergibt Tower an Departure (Approach oder die eigene Departure-Frequenz des Platzes):

> **Tower:** Enfield one one, contact Kutaisi Approach.  
> **Du:** Kutaisi Approach, Enfield 1-1, airborne, passing 1500.  
> **Approach:** Enfield one one, Kutaisi Approach, radar contact, 3 miles west of the field. Resume own navigation, maintain VFR.

- **Check-in-Sprüche:** „airborne“, „passing …“, „climbing …“, „departed“, „out of …“, „with you“ oder nur Station und Rufzeichen.
- **Noch kein Squawk:** Approach gibt erst „squawk …“, ein paar Sekunden später „radar contact“.
- **IFR:** „radar contact, … Fly heading 280, climb and maintain flight level one five zero.“
- **VFR noch in der Zone:** „Continue as cleared, report leaving the control zone.“ Sagst du dann „leaving the control zone“, kommt „roger, resume own navigation, squawk VFR, frequency change approved. Good day.“
- **Ohne Check-in:** Approach identifiziert dich nicht von selbst. 60 s nach der Übergabe, am Zonenrand oder am Ausflug-CRP erinnert dich Tower einmal: „contact Kutaisi Approach now“ (mit Frequenz, wenn sie sich von deiner unterscheidet; mit eigener Departure-Frequenz „contact Kutaisi Departure now, two seven three decimal five five“). 60 s später: „no contact with Kutaisi Approach, radar service not provided“ (VFR: dazu „squawk VFR, frequency change approved“; IFR: Verstoß). Siehe [Meldepflicht](#meldepflicht).
- **Zone vor der Übergabe verlassen** (etwa bei einem tiefen Abflug): „leaving control zone, squawk VFR, frequency change approved. Good day.“
- **VFR, nach „report leaving the control zone“:** meldest du nicht, kommt am Zonenrand oder am Ausflug-CRP „report leaving the control zone.“ 1 NM außerhalb der Zone (frühestens 30 s später): „I show you clear of the zone, radar service terminated, resume own navigation, squawk VFR, frequency change approved. Good day.“ und ein Verstoß.
- **15 NM vom Platz:** Departure meldet dich ab: „radar service terminated, squawk VFR, frequency change approved.“
  - IFR zu einem anderen Platz: stattdessen Übergabe an dessen Approach: „contact Batumi Approach …“
  - Mit eigenem AWACS in der Mission: „contact Overlord …“

**Anfragen im Abflug**

- **Höhe:** „request higher“ oder „request 8000 feet“. Approach: „climb and maintain …“ oder „unable, traffic“ / „unable, minimum altitude …“.
- **Kurs:** „request heading 270“ bekommt „fly heading two seven zero, approved.“
- **Direkt:** „request direct Batumi“ bekommt „cleared direct Batumi, turn left heading …, 45 miles.“

### Tower: Ankunft

#### Overhead Break: Initial und Break

Approach führt dich zum **Initial**: etwa 2 NM (4000 m) vor der Schwelle auf der verlängerten Mittellinie, in Platzrundenhöhe. Die Platzrundenhöhe ist Platzhöhe plus 1500 ft, auf 100 ft gerundet; in Kutaisi 2000 ft. Dann übergibt Approach an Tower:

> **Approach:** Enfield one one, turn left heading two five zero, on course, 6 miles from initial, maintain 2000 feet, contact Kutaisi Tower, report initial for overhead break.  
> **Du:** Kutaisi Tower, Enfield 1-1, initial runway 25.  
> **Tower:** Enfield one one, runway two five, cleared break left hand. Report final.  
> **Du:** Enfield 1-1, base, gear down, full stop.  
> **Tower:** Enfield one one, runway two five, wind two four zero degrees, eight knots, cleared to land.

- **Wetter beim Erstanruf:** ist Tower dein erster Kontakt (keine Übergabe) und du hast keinen ATIS-Buchstaben genannt, enthält die Break-Freigabe auch Wind und QNH.
- **Reihenfolge:** „number 2, follow the F-16C on downwind, runway two five, cleared break left hand.“
- **Rotten:** „initial, 2 second break“ bekommt „2 second break approved.“
- **Zu weit weg:** „initial“ zählt nur auf der Mittellinie (innerhalb 0,5 NM) und nahe am Initial. Sonst: „negative, you are 8 miles from initial. Fly heading …, 2000 feet, report initial runway two five.“
- **Base-Meldung:** „base“ (oder „final“) nach dem Break ist deine Landemeldung. Zeigt die Telemetrie das Fahrwerk oben und du hast nicht „gear down“ gesagt, sagt Tower erst „check wheels down.“
- **„final“ am Initial** bekommt „roger, report base.“ Vor dem Break gibt Tower nie eine Landefreigabe.
- **Keine Initial-Meldung:** am Initial fragt Tower einmal „Enfield one one, Kutaisi Tower, report initial.“ Fliegst du ohne Meldung weiter, kommt 1 NM später „no break clearance, continue straight through, re-enter initial runway two five, report initial.“

**Prüfungen in der Platzrunde**

- Falsche Seite: „pattern is south of the runway, left hand pattern runway two five.“ An anderen Plätzen: „wrong side, …“
- Mehr als 500 ft neben der Platzrundenhöhe im Gegenanflug: „check altitude, pattern altitude 2000 feet.“
- Über der Zonengrenze (Platzrundenhöhe plus 500 ft): „check altitude, pattern altitude …“
- Verkehr vor dir im Endanflug: „extend downwind, number two, traffic …, I will call your base.“ Dann: „turn base now, number two, follow the traffic, report final.“

#### Overhead-Einflug

- **Kutaisi über CRP North:** „proceed overhead, 2000 feet, contact Kutaisi Tower, report overhead.“
- **„overhead“** innerhalb 2,5 NM um die Bahnmitte bekommt „join left hand downwind runway two five, 2000 feet, report final.“ Weiter weg: „negative, you are 4 miles north of the field. Fly heading …, report overhead.“
- **Gelände:** verbietet das Gelände den Anflug auf die aktive Bahn, kann Approach dich „for overhead join runway 31 via the extended centerline runway 13“ führen.
- **Andere Platzrunden-Meldungen:** „downwind“ oder „base“ in der Platzrunde bekommt „number one, report final“ (oder „number 2, follow the …“).

#### Geradeaus-Anflug

> **Du:** Kutaisi Approach, Enfield 1-1, request straight in.  
> **Approach:** Enfield one one, Kutaisi Approach, straight in approach runway two five approved, QNH one zero one three. Fly heading two four zero for six mile final. Maintain 2500 feet. Contact Kutaisi Tower.  
> **Du:** Kutaisi Tower, Enfield 1-1, four miles final, gear down.  
> **Tower:** Enfield one one, runway two five, wind …, cleared to land.

- **Führung:** innerhalb 8 NM und bei freiem Gelände direkt auf einen 6-NM-Endanflug. Sonst Radarführung zum Endanflug.
- **Höhe:** auf oder unter dem Gleitweg, aber nie unter der Geländefreiheit.
- **ILS-Bahnen:** „maintain 2500 feet until established, cleared ILS approach runway two five, localizer …“
- **„initial“ im Geradeaus-Anflug** bekommt „make straight in runway two five, report four miles final.“

#### Platzrundentraining

| Du sagst | Wirkung |
|---|---|
| „touch and go“, „closed pattern“, „pattern work“, „circuits“, „practice“ (allein oder mit „ready“ bzw. „inbound“) | Touch-and-go-Runden: „cleared touch and go, left closed traffic approved, report base.“ Du gehst direkt wieder in den Gegenanflug; die Runden werden gezählt (Text: Platzrunde 2 (Touch-and-go), beenden mit Tower → final, full stop) |
| „low approach“, „practice approach“, „practice ILS/TACAN/VOR/instrument“ | „cleared low approach“. Nach IFR- oder Geradeaus-Übung gibt Approach sofort neue Führung |
| „the option“ | „cleared for the option“: Touch-and-go oder Full Stop, nach deiner Wahl |
| „low pass“, „fly by“ | „cleared low pass, not below 500 feet, after the pass climb runway heading 3000 feet“. Danach keine Platzrunde. Von außerhalb der Platzrunde: „report five miles final runway two five“ |
| „full stop“ | Beendet das Training; der nächste Anflug ist eine Landung |

#### Landefreigabe und Reihenfolge

- **Freigabe:** „Enfield one one, runway two five, wind …, cleared to land.“ (oder cleared touch and go, low approach, …)
- **Reihenfolge:** mit Verkehr voraus: „number 2, follow the Hornet on 3 mile final, continue approach.“
- **Bahn belegt:** „continue approach, traffic on runway.“
- **„final“, obwohl du nicht im Endanflug bist:** „negative, I don't have you on final. You are 6 miles north of the field. Report final runway two five when established.“
- **Auf andere Bahn ausgerichtet:** „check runway! You are lined up for runway zero seven, runway two five in use. Join left hand downwind runway two five, 2000 feet.“
- **Parallelbahnen:** bist du klar auf die Parallelbahn ausgerichtet (Kurs innerhalb 10°, im inneren Drittel des Abstands, innerhalb 5 NM), wechselt die Freigabe auf diese Bahn.
- **Fahrwerk im Endanflug noch oben:** zwischen 2,5 und 0,3 NM und unter etwa 800 ft (250 m) über Grund ruft Tower „check wheels down!“
- **Zu tief im Endanflug:** mehr als 200 ft unter dem 3°-Gleitweg innerhalb 2,5 NM gibt „low altitude alert, check your altitude immediately.“ Einmal je Anflug.
- **Ohne Landemeldung:** keine Landefreigabe. Tower fragt bei etwa 2,5 NM im Endanflug (oder im Queranflug) einmal „report final“. Immer noch ohne Meldung innerhalb 0,8 NM oder unter 300 ft: „go around, no landing clearance, …, report base“ (IFR oder geradeaus: Fehlanflug) und ein Verstoß. Nach deiner Meldung kommt die Freigabe, sobald Bahn und Vordermann es zulassen (siehe [Meldepflicht](#meldepflicht)).

#### Durchstarten

| Lage | Lotse |
|---|---|
| Du: „going around“ / „go around“ (VFR) | „roger, climb and maintain 2000 feet, join left hand downwind runway two five, report base.“ (mit Platzrunden-Wunsch: „roger, left closed traffic approved, report base.“) |
| Du: „going around“ / „missed approach“ (IFR oder geradeaus) | „roger, go around. Fly runway heading, climb and maintain 3000 feet, contact Kutaisi Approach.“ (siehe Fehlanflug unten) |
| Bahn blockiert oder MAYDAY-Verkehr innerhalb 4 NM, und du bist innerhalb 0,8 NM | „go around, I say again, go around, traffic on runway, climb and maintain 2000 feet, join left hand downwind runway two five, report base.“ |
| Durchstarten ohne Meldung mit Landefreigabe (300 m hinter der Schwelle, noch über 200 ft) | „I show you going around, confirm? Climb and maintain 2000 feet, join left hand downwind runway two five, report base.“ Die Landefreigabe ist aufgehoben, dazu ein Verstoß. IFR oder geradeaus: dieselbe Frage, dann der Fehlanflug. |
| Landung, nachdem Tower dich durchstarten ließ, oder ohne Freigabe | Verstoß im Debriefing |

„Go around“ wird nur während eines Anflugs auf diesen Platz angenommen. Sonst kommt „say intentions.“

### Approach

#### Check-in und Radarführung

> **Du:** Kutaisi Approach, Enfield 1-1, 25 miles west, 8000 feet, inbound for landing, information Alpha.  
> **Approach:** Enfield one one, Kutaisi Approach, identified, 25 miles west. Runway two five in use, wind two four zero degrees, eight knots, QNH one zero one three. Expect vectors to initial runway two five for overhead break. Turn left heading zero nine zero, 20 miles from initial, descend and maintain 5000 feet, speed 300 knots, downwind.

- **Check-in-Sprüche:** „inbound“, „landing“, „recovery“, „rejoin“, „full stop“, „with you“ oder Station und Rufzeichen.
- **Innerhalb 4 NM um den Platz** antwortet stattdessen Tower: „runway two five, wind …, QNH …. Join left hand downwind, 2000 feet, report final.“
- **Der Führungsplan:** Approach führt dich über Gegen- und Queranflug mit 30° auf die Mittellinie.
  - VFR: zum Initial.
  - Geradeaus oder IFR: auf den Endanflugkurs, mit Gate bei 10, 8, 6 oder 4 NM.
- **Gelände:** der Plan nutzt die Geländekarte und sinkt dich nie unter die Mindestführungshöhe: Gelände plus 1000 ft, im Gebirge plus 2000 ft. Blockiert Gelände die aktive Bahn, kann der Plan die Gegenbahn (bis 10 kt Rückenwind) oder ein Circling nehmen: „Due to terrain, expect …“.
- **Wann Approach spricht:** jeder Spruch gibt einen Kurs. Entfernung, Höhe, Fahrt und Abschnitt nur, wenn sie sich ändern. Sinkflüge kommen stufenweise entlang eines 2,5°-Profils. Ändert sich die Reihenfolge, sagt Approach „No traffic ahead, you are number one“ oder „You are number 2, 1 aircraft ahead“.
- **Übergabe an Tower:** erst kurz vor dem Gate (bis 2 NM davor, höchstens 1000 ft über der Gate-Höhe) bzw. spätestens am Gate. Bist du schon weiter draußen auf der Mittellinie, behält dich Approach, gibt den Sinkflug weiter nach Profil („descend and maintain 4000 feet“, die Gate-Höhe erst, wenn das Profil an deiner Position dort ist) und übergibt nahe am Gate. Auf der Mittellinie bekommt VFR-Verkehr „on course, 6 miles from initial, maintain 2000 feet, contact Kutaisi Tower, report initial for overhead break.“ IFR-Verkehr bekommt die Anflugfreigabe (unten).
- **CRPs in Kutaisi:** North und South gehen immer, West nur für Bahn 07, East nur für Bahn 25. Fliegst du selbst einen an, melde ihn („C R P south“). Approach schickt dich dann zum Initial oder Overhead und übergibt an Tower. Ein nicht erlaubter CRP bekommt „negative, C R P east is not available for runway zero seven. Enter via C R P …“. Andere Plätze haben keine CRPs: „negative, no reporting points at Senaki Kolkhi“, dann Führung.

#### Höhen- und Fahrtanweisungen

- **Höhe:** „descend and maintain …“, „climb and maintain …“ oder „maintain …“ für die gleiche Höhe. Alle Höhen sind Höhenmesseranzeige mit dem QNH des Platzes.
- **Fahrt:** nur innerhalb 25 NM.

  | Flugzeug | Fahrt |
  |---|---|
  | Jets | VFR 300 kt. Geradeaus nach Reststrecke: 300 kt, unter 20 NM 250 kt, unter 10 NM 200 kt |
  | Propeller und Warbirds | 200 / 170 / 140 kt |
  | A-10, L-39, C-101 | 250 / 200 / 160 kt |
  | Hubschrauber | Keine Fahrtvorgaben |

  Bist du zu dicht hinter dem Vordermann (3 NM, hinter Heavy 5 NM): „reduce speed 170 knots for spacing.“ Nahe am Gate: „reduce to final approach speed“.
- **Say airspeed:** nach einer Fahrtvorgabe hast du 90 s Zeit. Bist du dann noch mehr als 60 kt daneben und wirst nicht langsamer, fragt Approach „say airspeed.“ Antworte mit deiner Fahrt: „Kutaisi Approach, Enfield 1-1, 340 knots“, oder Rad **Approach → Fahrt melden**, oder Enter.

  | Deine Antwort | Approach |
  |---|---|
  | Innerhalb 30 kt der Vorgabe | „roger.“ |
  | Weiter daneben | „roger, reduce speed 300 knots.“ (einmal) |
  | „unable“ | „roger, resume normal speed.“ |
  | Keine Antwort in 30 s | „resume normal speed.“ |

- **Say altitude / heading / position:** fragt ein Lotse danach, sendet Enter deinen aktuellen Wert.
- **Abweichungen** (mehr als 7°, 500 ft oder 30 kt daneben und keine Korrektur):

  | Abweichung | Approach sagt |
  |---|---|
  | Groß (mehr als 30° oder 1000 ft) | Wiederholt den Kurs oder sagt „check altitude, climb and maintain …“ bzw. bei zu hoch: ohne Freigabe gestiegen „verify altitude, maintain …“, beim zweiten Mal „climb not authorized, descend and maintain …“; aus einem Sinkflug noch nicht unten noch einmal „descend and maintain …“; „expedite descent, maintain …“ nur, wenn Verkehr auf deiner Höhe ist |
  | Dieselbe Korrektur dreimal oder öfter ignoriert | „verify heading/altitude“ → neue Führung oder „… immediately“ → „say intentions“ → „you are not following instructions, you are removed from the sequence. Approach cancelled …“ (Verstoß) |

#### Anfragen unter Radarführung

| Du sagst | Approach |
|---|---|
| „request direct“ | Nur, wenn du Nummer eins bist, das Gelände frei ist und du nicht zu hoch bist: „direct approved, turn left heading …“. Sonst „unable, …“ |
| „request heading 270“ unter Führung | „unable, …“ (Führung läuft weiter) |
| „request higher“ / „request lower“ / „request 6000 feet“ / „request flight level 200“ | „climb/descend and maintain …“. Höher unter Radarführung: weit draußen (mehr als 25 NM Weg bis zum Gate) in einem Block genehmigt – bist du schon darüber, deine jetzige Höhe („maintain 14000 feet“), sonst bis FL200 (Props 10 000 ft), aber nur so hoch, dass du das Gate noch auf dem normalen Profil erreichst; die Höhe bleibt, bis das Profil den Sinkflug braucht, der kommt dann als „descend at pilot's discretion, maintain …“. Näher dran: „unable higher, expect lower shortly, maintain …“ (Verkehr: „unable higher, traffic, maintain …“); auf „höher“ kommt nie „descend“. „request descent at pilot's discretion“: „descend at pilot's discretion, maintain …“ (bis 1000 ft über die Gate-Höhe); bis 10 NM vor dem Gate sinkst du, wann du willst, ohne Mahnung. Oder „unable, minimum altitude …“ / „unable, traffic“. Im Holding: „unable, maintain … in the hold, I will call you.“ Nach der Übergabe an Tower: „negative, continue as cleared.“ |
| „request runway 07“ | „runway zero seven approved.“ Der Plan wird neu gebaut |
| „VFR“, „cancel IFR“, „visual recovery“ (VFR-Wetter) | „roger“ (mit „cancel IFR“: „roger, I F R cancelled“), dann „Expect vectors to initial …“ |
| „request straight in“ unter Führung | „roger, straight in approach approved.“ Der Plan wird neu gebaut |

#### ILS-, PAR- und Sichtanflüge

**ILS** (die Bahn hat in den Kartendaten einen Localizer):

> **Approach:** Enfield one one, 12 miles from touchdown, maintain 3000 feet until established, cleared ILS approach runway two five, QNH one zero one three, contact Kutaisi Tower.

Ein ILS-Anflug wird nur überwacht: keine Gleitweg-Ansagen, nur die eine Tiefflugwarnung.

**PAR** (IFR ohne ILS oder Notfall ohne ILS). Nach der Übergabe gibt Tower Präzisionsradar-Korrekturen höchstens alle 20 s und nur bei deutlicher Abweichung:

> **Tower:** Enfield one one, three miles, slightly above glidepath, reduce rate of descent, slightly left of course, turn right heading two five two.

Gleitweg-Begriffe: „well/slightly above/below glidepath“. Korrekturen: „increase rate of descent“, „reduce rate of descent“, „level off“.

**Sichtanflug** (nur VFR-Wetter):

> **Du:** Kutaisi Approach, Enfield 1-1, request visual approach.  
> **Approach:** Enfield one one, Kutaisi Approach, radar contact, 15 miles west of the field, QNH …. Expect visual approach runway two five, report field in sight.  
> **Du:** Field in sight.  
> **Approach:** Enfield one one, Kutaisi Approach, cleared visual approach runway two five, QNH …. Contact Kutaisi Tower.

- Hast du in der Anfrage schon „field in sight“ gesagt, kommt die Freigabe sofort.
- „negative field in sight“ bekommt „roger, report field in sight.“
- Hast du den Platz 4 NM vor der Schwelle noch nicht gemeldet, bekommst du den normalen Geradeaus- oder Instrumentenanflug.

#### Warteschleife und erwartete Anflugzeit

Approach schickt dich ins Holding, wenn eines davon zutrifft:

- Zwei oder mehr Flugzeuge sind in der Platzrunde (innerhalb 4 NM, unter etwa 3000 ft über dem Platz).
- Spieler kommen hintereinander und du bist nicht der nächste.
- Ein anderes Flugzeug hat MAYDAY erklärt.

> **Approach:** Enfield one one, Kutaisi Approach, identified, 30 miles west. Runway two five in use, … Fly heading zero eight five, descend and maintain 5000 feet, speed 230 knots, hold on the two six zero radial, 15 DME, 14 miles to go, I will call you there. Expected approach time three five, you are number 3.  
> *(am Fix)* **Approach:** Enfield one one, hold on the two six zero radial, 15 DME, orbit left hand, maintain 5000 feet, speed 230 knots.  
> *(später)* **Approach:** Enfield one one, leave the hold, turn right heading …

- **Holding-Fix:** 15 NM vom Platz, abseits von End- und Abflugweg, mit niedrigem Gelände. Als TACAN-Radial/DME, wenn der Platz TACAN hat. Sonst „hold at present position“ oder „… miles to the holding point“.
- **Höhe:** die unterste Stufe ist die Zonengrenze, auf volle 1000 ft aufgerundet, plus 1000 ft je Platz in der Reihenfolge. Zwei Flugzeuge nahe beieinander bekommen nie dieselbe Höhe. Verlassen Vordermänner das Holding, wirst du herabgestuft: „maintain 4000 feet, you are number 2.“
- **Fahrt:** 230 kt, Propeller 200 kt, Hubschrauber keine.
- **Erwartete Anflugzeit:** einmal als Minute der Missionsuhr („expected approach time three five“). Erneut nur bei einer Änderung um 5 min oder mehr („revised expected approach time …“). Ohne Missionsuhr: „expect approach in 6 minutes“.
- **Prüfungen im Holding:** driftest du ab oder bist mehr als 30 kt neben der Fahrt, kommt eine Kurs- bzw. Fahrterinnerung. Mehr als 500 ft neben der Höhe: „check altitude, maintain 5000 feet.“
- **Holding verlassen:** bist du dran, wirst du innerhalb 2 min mit einem Kurs grob Richtung Anflug entlassen.

#### Verkehrsinformationen und Verkehrswarnungen

| Wann | Spruch |
|---|---|
| Erstanruf, Radarführung | „Traffic, Viper, 3 miles south of the field, 1500 feet.“ |
| Konflikt innerhalb 60 s (näher als 1,5 NM und 700 ft) unter Führung oder im Anflug | „traffic alert, traffic 2 o'clock, 3 miles, same altitude. Turn right heading one two zero immediately, climb and maintain 5000 feet.“ Die nächsten 30 s keine anderen Anweisungen |
| KI-Abflug steigt in deinen Weg | „turn right heading …, traffic 11 o'clock, 4 miles, departing aircraft, climbing through 3000 feet.“ |
| In der Platzrunde: Verkehr ohne Kontakt zum Tower, innerhalb 30 s näher als 0,5 NM | „traffic 2 o'clock, 1 mile, FA-18C, 1500 feet, no contact with Tower.“ |

- **Antworten:** „traffic in sight“ bzw. „tally“, „looking“, „negative contact“ bzw. „no joy“. Approach antwortet „roger.“
- **Wessen Verkehr:** Radarhinweise nur für die eigene Seite und Neutrale; Feinde sind Sache des AWACS. Tower meldet alle in der Platzrunde.

#### Geländewarnung

Diese Warnung kommt, wenn du Radarbetreuung hast (Führung, Holding, Abflug nach der Übergabe oder Flight Following), mehr als 5 NM vom Platz bist und jetzt oder in den nächsten 60 s weniger als 500 ft über Gelände bist:

> **Approach:** Enfield one one, low altitude alert, check your altitude immediately. The minimum altitude in your area is 4500 feet.

Einmal je Flug. Mit „terrain in sight“ quittierst du („roger“); danach keine Geländewarnungen mehr.

#### Fehlanflug und Übungsanflüge

Im IFR- oder Geradeaus-Anflug wird ein Durchstarten in zwei Schritten zum Fehlanflug:

> **Tower:** Enfield one one, roger, go around. Fly runway heading, climb and maintain 3000 feet, contact Kutaisi Approach.  
> **Du:** Kutaisi Approach, Enfield 1-1, missed approach.  
> **Approach:** Enfield one one, Kutaisi Approach, identified. Turn right heading three four zero, climb and maintain 3000 feet, … Say intentions.

- **Fehlanflughöhe:** Zonengrenze oder Gelände 10 NM voraus, auf volle Tausender gerundet.
- **Ohne Check-in:** Approach führt dich nicht ohne deinen Check-in. Nach 45 s erinnert Tower: „contact Kutaisi Approach now“. 60 s später: „no contact with Kutaisi Approach, continue runway heading, maintain 3000 feet, contact Kutaisi Approach now“, dazu ein Verstoß. Ein später Check-in bekommt trotzdem noch Führung (siehe [Meldepflicht](#meldepflicht)).
- **Übungsanflüge** (Low Approach oder Touch-and-go unter IFR oder geradeaus) gehen ohne „say intentions“ direkt wieder in die Führung. Jeder Anflug wird gezählt (Text: Anflug 2 (Low Approach), beenden mit Approach → full stop).

#### Anflug abbrechen

- **Du:** „cancel approach“, „abort approach“, „cancel landing“, „leaving the pattern“.
- **Approach:** „roger, approach cancelled, resume own navigation, QNH …. Call me when ready for another approach.“
- **Ohne Meldung weg:** fliegst du aus einem Anflug weg, kommt zuerst „I show you leaving the control zone, say intentions.“ 3 NM später: „approach cancelled, frequency change approved.“

### Luftraum

#### Kontrollzone

- **Größe:** jeder Platz hat eine Kontrollzone mit 5 NM Radius. In Kutaisi reicht sie bis zu den CRPs, etwa 9 NM.
- **Höhe:** bis 3000 ft über dem Platz.
- **VFR-Grenze darin:** Platzrundenhöhe plus 500 ft.

#### Zonendurchflug

> **Du:** Kutaisi Approach, Enfield 1-1, 12 miles west, 3000 feet, request zone transit, eastbound.  
> **Approach:** Enfield one one, Kutaisi Approach, cleared to cross the Kutaisi control zone overhead, eastbound, not below 3500 feet, QNH …, runway two five in use, report clear of the zone.  
> **Du:** Enfield 1-1, clear of the zone.  
> **Approach:** Enfield one one, frequency change approved.

- **Sprüche:** „transit“, „crossing“, „cross the …“, „overflight“, „pass through“. Innerhalb 5 NM antwortet Tower.
- **Durchflughöhe:** 1000 ft über der Platzrunde und über dem Gelände in der Zone, auf 500 ft aufgerundet.
- **Darunter:** „check altitude, not below 3500 feet, pattern traffic below.“
- **Spieler in der Platzrunde** hören: „traffic, Viper crossing overhead eastbound, 3500 feet.“
- **Voller Platz:** „remain outside the control zone, expect transit in 4 minutes.“
- **Keine Meldung:** 1 NM hinter dem Zonenrand: „I show you clear of the zone, frequency change approved.“

#### Flight Following

> **Du:** Kutaisi Approach, Enfield 1-1, request flight following.  
> **Approach:** Enfield one one, Kutaisi Approach, squawk five three two one.  
> **Approach:** Enfield one one, radar contact, 12 miles west of the field, QNH …. Flight following, report leaving frequency.

- **Was du bekommst:** Verkehrshinweise innerhalb 5 NM und ±1500 ft („traffic, 10 o'clock, 4 miles, southbound, 500 feet above, Hornet“) und „traffic no longer a factor“, wenn der Verkehr weg ist. Hast du „negative contact“ gesagt, kommt nach 30 s ein Update. Verkehrs- und Geländewarnungen gelten auch.
- **Es bleibt erhalten**, wenn du den Platz wechselst.
- **Beenden:** „cancel flight following“ oder „leaving frequency“ bekommt „radar service terminated, squawk VFR, frequency change approved.“

#### Zonenwache

Mit `"AirspaceWatch": true` (Standard) überwacht Tower die Kontrollzone.

**Wer gerufen wird:** du, wenn du ohne Kontakt in die Zone fliegst und der Platzrunde, dem End- oder Abflugweg oder tatsächlich vorhandenem Verkehr in die Quere kommen kannst.

1. Tower sagt dir, wo du bist, und dreht dich weg oder lässt dich steigen:

   > **Tower:** Enfield one one, Kutaisi Tower, you are entering the Kutaisi control zone without clearance, 4 miles north of the field, crossing the final approach course runway two five, turn left heading zero one zero, remain clear of the control zone.

   Ein Texttipp zeigt den richtigen Weg hinein: Rad **Allgemein → Request zone transit** oder **Approach → Inbound for landing**.
2. Nach 30 s ohne Antwort, wenn du noch nah bist: „turn right heading three six zero, leave the control zone immediately, traffic in the pattern.“
3. Beim Verlassen: „leaving the control zone. Possible pilot deviation, advise you contact Kutaisi Tower after landing.“ Das kommt ins Debriefing.

**Weitere Fälle**

- **Aktive Bahn tief überflogen** ohne Freigabe: „you crossed the active runway without clearance.“ (Verstoß)
- **Auf diesem Flug gar kein ATC-Kontakt:** du wirst als „Unidentified aircraft 4 miles north of Kutaisi, heading 180, 2000 feet, …“ gerufen.
- **Nicht auf die Platzfrequenz gerastet (SRS):** der Ruf geht auf **Guard** (243.0 / 121.5): „…, Kutaisi Tower on guard, … Contact Kutaisi Tower one three four decimal zero.“
- **Landung:** landest du erkennbar (Fahrwerk unten oder langsam und sinkend im Endanflug), fragt Tower nur „say intentions“.

**Die Zonenwache schweigt:**

- bei Hubschraubern
- innerhalb 5 min nach irgendeinem ATC-Kontakt
- mit Flight Following oder während ein anderes Verfahren läuft
- in den ersten 60 s nach dem Start
- mit Feinden innerhalb 20 NM
- über der Zonenobergrenze

Bist du beim AWACS eingecheckt, werden nur Platzrunde und Bahn überwacht.

Mit `"AirspaceWatch": false` bleiben nur die Sicherheitssprüche. Keine „possible pilot deviation“-Rufe und keine Debriefing-Einträge.

### Notfälle

| Du sagst | Reaktion |
|---|---|
| „mayday mayday mayday, engine failure, request immediate landing“ innerhalb 5 NM oder im Endanflug innerhalb 6 NM | **Tower:** „roger mayday, runway two five, wind …, QNH …, cleared to land, emergency services standing by.“ Du darfst auf jeder Bahn landen, auf die du ausgerichtet bist |
| Dasselbe weiter draußen | **Approach:** „roger mayday, all traffic is holding. Turn …, descend at your discretion, maintain … Runway two five available, QNH …, say souls on board and fuel remaining.“ (nicht nach einem Funkrad-/F10-Notruf, der nennt beides schon). Tower gibt bei etwa 5 NM die Landefreigabe. Jede Antwort auf die Frage bekommt „roger“; Enter schlägt „25 minutes fuel, one soul on board“ vor |
| Notruf am Boden | Der Funkrad-/F10-Notruf lautet „MAYDAY MAYDAY MAYDAY, Kutaisi Ground, Enfield 1 1, engine failure, shutting down.“ Antwort: „roger mayday, emergency services are on the way. Hold position.“ Ohne Art des Notfalls: „…, say nature of emergency.“ |
| „pan pan, pan pan, pan pan, …“ | Gleiches Verfahren: du hast Vorrang, aber anderer Verkehr wartet nicht |
| Zweiter Notruf während dein Notfall läuft | „roger mayday, continue as cleared.“ |
| „high key“ / „flameout, high key“ im eigenen Notfall (echter Flameout, auch im IMC) | „report low key.“ Bei „low key, gear down“: „runway two five, wind …, cleared to land.“ (SFO-Übung: nur VMC und ohne anderen Notfall) |
| „cancel emergency“ | Unter Radarführung: „roger, emergency cancelled, continue as cleared.“ Mit Landefreigabe: „roger, emergency cancelled, runway two five, cleared to land.“ Aus Initial oder Platzrunde: „Join left hand downwind, 2000 feet, report base.“ Sonst (weit draußen, kein Notfall) nur „roger.“ |
| „minimum fuel“, „low fuel“, „bingo“ | „roger minimum fuel, number 2, expect approach in 3 minutes.“ oder „…, expect no delay.“ Kein Vorrang |

- **Rad und F10:** **Notfall → MAYDAY / PAN PAN →** Triebwerksausfall, Treibstoff (nur MAYDAY), Hydraulik, Gefechtsschaden, Medizinisch, Vogelschlag.
- **Welcher Platz:** hast du keinen Platz genannt oder gewählt und bist auf keinen gerastet, geht der Notfall an den nächsten geeigneten eigenen oder neutralen Platz. Bahnen ab 1800 m werden bevorzugt.

**Was ein MAYDAY für alle anderen am Platz bedeutet**

| Wer | Was passiert |
|---|---|
| Flugzeuge innerhalb 15 NM, nicht im kurzen Endanflug | Platzrunde bzw. Führung verlassen und warten: „emergency in progress, leave the pattern, fly heading …, hold …, I will call you.“ |
| Abflüge | „hold short runway two five, emergency traffic inbound.“ |
| Landender Verkehr | Innerhalb 8 NM keine neuen Landefreigaben („continue approach, emergency traffic“). Innerhalb 4 NM: Durchstarten |
| Flugzeuge, die vor dem Notfall landen | „expedite vacating, emergency traffic 6 miles“ |

### Landebewertung und Debriefing

**Landebewertung**

Jedes Aufsetzen auf einer Bahn wird bewertet und als Text im Spiel gezeigt. Vier Teile bringen je bis zu 25 Punkte:

| Teil | 25 Punkte | Weniger |
|---|---|---|
| Gleitweg (mittlere Abweichung, Endanflug 0,3–3 NM) | ≤ 50 ft | ≤ 100 ft: 18, ≤ 200 ft: 10 |
| Mittellinie (Mittel, gleicher Abschnitt) | ≤ 15 m | ≤ 35 m: 18, ≤ 70 m: 10 |
| Aufsetzpunkt | 100–500 m hinter der Schwelle | ≤ 800 m: 15, weiter: 5, vor der Schwelle: 0 |
| Sinkrate | ≤ 300 ft/min | ≤ 500: 20, ≤ 700: 12 |

- Gleitweg und Mittellinie zählen nur, wenn du lang genug einen geraden Endanflug geflogen bist.
- **Ergebnis:** **OK** ab 85, **Fair** ab 70, **No grade** ab 50, sonst **Cut**. Die Zeile beginnt mit „Landebewertung Kutaisi Bahn 25: OK (88/100) – …“ und nennt Gleitweg, Mittellinie, Aufsetzpunkt und Sinkrate.

**Debriefing**

Das Debriefing sammelt:

- Landebewertungen und gezählte Platzrunden bzw. Übungsanflüge
- Anflüge ohne Initial-Meldung
- Verstöße mit Missionszeit, etwa „Verstoß: Start ohne Freigabe Kutaisi 25 (09:14)“
- die Zahl der vom Lotsen abgelehnten Sprüche
- Einträge von Range, Tanker, Träger und AWACS

Parkst du nach einem Flug, wird es automatisch gezeigt und unter `%LOCALAPPDATA%\Programs\DCS-ATC\debrief\<Datum>_<Uhrzeit>_<Rufzeichen>.txt` gespeichert. Jederzeit abrufbar über Rad oder F10 **Allgemein → Debriefing**.

**Arten von Verstößen:** Start ohne Freigabe; Bahn ohne Freigabe betreten; Landung ohne Freigabe; Landung nach Durchstartanweisung; Luftraumverletzung; Bahn ohne Freigabe überquert; aus der Reihenfolge genommen; Rollen ohne Freigabe; Anflug ohne Kontakt; keine Landemeldung; Durchstarten ohne Meldung; kein Check-in bei Departure (IFR); kein Check-in bei Approach nach dem Fehlanflug; keine Meldung „leaving the control zone“; keine Meldung „clear of the zone“. Träger, Tanker und Range tragen eigene ein (Approach-Zeit ohne „commencing“ verstrichen, Kontakt bzw. Annäherung an einen Tanker ohne Freigabe, kein „off“ auf der Range).

### Meldepflicht

**Grundsatz:** Du meldest deine Position selbst, wie im echten Militärflugbetrieb. Der Lotse meldet sie nicht für dich. Meldest du nicht, kommt zuerst eine Aufforderung. Meldest du dann immer noch nicht, folgt eine echte Konsequenz: keine Freigabe, Durchstarten, Wave-off oder keine Radarbetreuung. Verstöße stehen im Debriefing.

| Pflichtmeldung (was du sagst) | Aufforderung ohne Meldung | Konsequenz, wenn weiter keine kommt |
|---|---|---|
| **Initial:** „Kutaisi Tower, Enfield 1-1, initial runway 25“ | Am Initial: „report initial“ | 1 NM hinter dem Initial: „no break clearance, continue straight through, re-enter initial runway two five, report initial.“ |
| **Overhead:** „…, overhead“ | Über dem Platz (0,7 NM): „report overhead“ | 1,5 NM später: „no clearance to join, remain outside the pattern, say intentions.“ |
| **CRP** (Kutaisi): „…, C R P north“ | Innerhalb 1 NM vom CRP, Approach: „report C R P north“ | 2 NM danach: „say position, remain outside the control zone“ (keine Übergabe) |
| **Landemeldung:** Platzrunde „base, gear down (full stop)“; geradeaus oder ILS „four mile final, gear down“ | Bei etwa 2,5 NM im Endanflug oder im Queranflug: „report final“ | Innerhalb 0,8 NM bzw. unter 300 ft: „go around, no landing clearance, …, report base“ (IFR oder geradeaus: Fehlanflug), dazu ein Verstoß |
| **Erstanruf vor dem Einflug in die Zone** (Approach oder Tower) | Zonenwache: „…, say intentions“ | Innerhalb 1 NM im Endanflug: „go around, you are not cleared to land“, dazu ein Verstoß |
| **Tower-Check-in nach „contact Tower“** („…Tower, Enfield 1-1, initial / overhead / four mile final“) | Nur wenn Tower eine eigene Frequenz hat: nach 60 s oder innerhalb 5 NM vom Platz sagt Approach „contact Kutaisi Tower now, <Frequenz>“ (auf gemeinsamer Frequenz gelten die Aufforderungen oben) | Keine Landefreigabe; es gilt die Regel zur Landemeldung oben |
| **Durchstarten:** „…, going around“ | „I show you going around, confirm?“ | Landefreigabe aufgehoben, „…, report base“, dazu ein Verstoß |
| **Departure-Check-in:** „…Departure, Enfield 1-1, airborne, passing 2000“ | 60 s nach der Übergabe oder am Zonenrand, Tower: „contact Kutaisi Approach now“ (eigene Departure-Frequenz: „contact Kutaisi Departure now, <Frequenz>“) | 60 s später: „no contact with Kutaisi Approach, radar service not provided“. IFR: Verstoß. VFR: „…, squawk VFR, frequency change approved“ |
| **Check-in nach Fehlanflug:** „…Approach, missed approach, climbing 3000“ | Nach 45 s, Tower: „contact Kutaisi Approach now“ | 60 s später: „no contact with Kutaisi Approach, continue runway heading, maintain 3000 feet, …“, keine Führung bis zu deinem Check-in, dazu ein Verstoß |
| **Verlassen der Kontrollzone** (nach „report leaving the control zone“): „…, leaving the control zone“ | Am Rand oder am Ausflug-CRP: „report leaving the control zone“ | 1 NM draußen: „I show you clear of the zone, radar service terminated, resume own navigation, squawk VFR, …“, dazu ein Verstoß |
| **Durchflug beendet:** „…, clear of the zone“ | 0,5 NM draußen: „report clear of the zone“ | 1 NM draußen: „I show you clear of the zone, frequency change approved“, dazu ein Verstoß |
| **Rollen zum Parkplatz:** „…Ground, runway vacated, request taxi to parking“ | „contact Kutaisi Ground“ (30 s nach dem Abrollen) | Schneller als 5 kt und mehr als 150 m von der Bahn ohne Freigabe: „hold position, you are not cleared to taxi. Request taxi.“, dazu ein Verstoß |
| **Startbereit** (auch nach Spawn auf der Bahn): „…, ready for departure“ | keine | Losrollen ohne Freigabe: „stop immediately, … You are not cleared for takeoff.“, dazu ein Verstoß |

In einem Holding am Platz meldest du „established“ nicht. Approach ruft dich zu deiner erwarteten Anflugzeit.

**Diese Sprüche bleiben Sache des Lotsen:** Radarführung, Verkehrsinformationen und Verkehrswarnungen, Tiefflugwarnungen, „check wheels down“, Durchstarten wegen belegter Bahn oder Notfall, die Startfreigabe nach „ready“, Stopp-Rufe, die Übergabe Tower → Departure bei etwa 500 ft, PAR-Korrekturen, Rollkonflikte, „extend downwind“ und „turn base“, Notlandefreigaben, die Anflugfreigabe, sobald du eingerichtet bist, und die Zonenwache.

Derselbe Grundsatz gilt am Träger (Ball-Call, commencing, see you at ten), am Tanker (visual, pre-contact) und auf der Range (in hot, off); siehe die jeweiligen Kapitel.

### Phrasentabelle

Schlüsselwörter werden überall im Spruch erkannt. Beginne mit „<Platz> <Lotse>, <Rufzeichen>, …“.

| Du sagst (Schlüsselwörter) | Wo | Was passiert |
|---|---|---|
| „request startup“, „start up“, „request engine start“, „ready to start“ | Ground | Anlassen frei (der erste Anruf ergänzt ATIS, Bahn und QNH) |
| „request taxi“ (+ „departure north“, + „runway 07“) | Ground | Rollen zum Rollhalt, Squawk, hold short |
| „request startup and taxi“ | Ground | Beides auf einmal |
| „request progressive taxi“ | Ground, beim Rollen | Richtungsangaben zum Rollhalt bzw. Parkplatz |
| „request IFR clearance (to Batumi)“, „clearance“, „IFR to“ | Ground (in der Luft: Approach) | IFR-Freigabe mit Squawk; Code zurücklesen → „readback correct“ |
| „ready“, „ready for departure“, „holding short“ (+ „closed pattern“) | Tower | Startfreigabe, line up and wait oder hold short |
| „abort“, „rejecting“, „stopping“, „cancel takeoff“ | Tower, mit Freigabe oder aufgerollt | Freigabe aufgehoben, „vacate …, report ready for departure“ |
| „airborne“, „passing 2000“, „climbing“, „departed“, „out of“, „with you“ | Departure/Approach nach dem Start | Radar contact (bei Bedarf erst Squawk) |
| „leaving the control zone“ | Approach, nach Aufforderung | Abgemeldet, squawk VFR |
| „inbound (for landing)“, „landing“, „recovery“, „rejoin“, „full stop“ | Approach (Tower innerhalb 4 NM) | Radarführung, Holding oder Einflug in die Platzrunde |
| „inbound for pattern work, touch and go“ | Approach | Führung, dann Platzrunden |
| „request straight in“, „ILS“, „instrument“, „TACAN/VOR/PAR approach“, „radar approach“, „IFR“ | Approach | Geradeaus- oder Instrumentenanflug |
| „request visual approach“ / „field in sight“, „runway in sight“, „have the field“ | Approach | Sichtanflug, sobald der Platz gemeldet ist |
| „VFR“, „cancel IFR“, „visual recovery“ | Approach | Overhead bzw. Initial statt Instrumentenanflug (nur VFR-Wetter) |
| „request runway 07“ / „runway 30 left“ | Alle | Bahnwunsch (bis 10 kt Rückenwind) |
| „request higher/lower“, „request 6000 feet“, „request flight level 200“ | Approach | Neue Höhe oder „unable“ |
| „request heading 270“, „request direct (Batumi)“ | Approach | Kurs genehmigt bzw. direkt / „unable“ |
| „<n> knots“, „airspeed 340“, „slowing“, „unable speed“ | Approach, nach „say airspeed“ | roger / eine Korrektur / resume normal speed |
| „C R P north/south/east/west“ | Approach (Kutaisi) | Zum Initial oder Overhead geschickt, Übergabe an Tower |
| „initial (runway 25)“ (+ „2 second break“) | Tower | Break-Freigabe oder „negative, … report initial“ |
| „overhead“ | Tower | Einflug in den Gegenanflug oder Führung zum Overhead |
| „downwind“, „base“, „break“ | Tower | Reihenfolge; „base“ nach dem Break = Landemeldung |
| „final“ (+ „full stop“ / „touch and go“ / „low approach“ / „the option“) | Tower | Lande- bzw. Optionsfreigabe, Reihenfolge oder „negative“ |
| „touch and go“, „closed pattern“, „pattern work“, „circuits“, „practice“ | Tower | Platzrunden genehmigt, Runden gezählt |
| „low approach“, „practice ILS“, „low pass“, „fly by“, „the option“, „full stop“ | Tower/Approach | Setzt die Option für den nächsten Anflug |
| „going around“, „go around“, „missed approach“, „going missed“ | Tower/Approach | Durchstarten in die Platzrunde oder Fehlanflug (IFR) |
| „cancel approach“, „abort landing“, „leaving the pattern“ | Approach/Tower | Anflug abgebrochen, eigene Navigation |
| „request zone transit“, „crossing“, „overflight“, „pass through“ / „clear of the zone“ | Approach (Tower innerhalb 5 NM) | Durchflugfreigabe / abgemeldet |
| „request flight following“, „radar service“, „traffic advisories“ / „cancel flight following“, „leaving frequency“ | Approach | Flight Following an / aus |
| „traffic in sight“, „tally“, „looking“, „negative contact“, „no joy“ | Approach/Tower | „roger“, Hinweisstatus vermerkt |
| „terrain in sight“ | Approach | Geländewarnung quittiert |
| „request separate landing“, „split“, „radar trail“ | Approach (Rotten) | Jedes Mitglied meldet einzeln |
| „runway vacated“, „clear of runway“, „taxi to parking“ | Ground nach der Landung | Rollen zum Parkplatz |
| „mayday“, „pan pan“ / „cancel emergency“ | Alle | Notfallverfahren / Notfall beendet |
| „minimum fuel“, „low fuel“, „bingo“ | Approach/Tower | Quittiert, Reihenfolge und Verzögerung |
| „unable“ | Alle | „roger, say intentions“ (am Boden: „hold position“) |
| „say again“, „repeat“, „say last“, „didn't copy“ | Alle | Letzter Spruch wiederholt bzw. aktualisiert |
| „confirm …“, „verify …“ | Alle | „affirm …“ / „negative …“ oder Wiederholung |
| „radio check“, „how do you read“ | Alle | „read you five“ |
| „request weather“, „QNH“, „wind“, „which runway“, „runway in use“ | Alle | Wind, QNH, aktive Bahn |
| „good day“, „thanks“, „bye“ | Alle | „good day“ |
| „roger“, „wilco“, „copy“, „start up approved“, nur das Rufzeichen, Readback von Zahlen | Alle | Keine Antwort |

Enter im Rad sendet immer den vorgeschlagenen nächsten Spruch oder die Antwort auf die letzte Frage des Lotsen.

## Carrier-Betrieb

DCS-ATC übernimmt den Funk für jeden eigenen Träger in der Mission: Marshal, Tower (Air Boss), Approach, Departure und Paddles (LSO). Der eingebaute DCS-Trägerfunk (Marshal, Air Boss, LSO) ist abgeschaltet, solange DCS-ATC läuft. DCS-ATC spawnt keine Schiffe oder Flugzeuge, es arbeitet nur mit Trägern und Flugzeugen, die schon in der Mission sind.

### Unterstützte Träger

Erkannt wird jede Schiffsgruppe, deren erste Einheit ein Supercarrier oder Träger ist: Nimitz-Klasse / CVN, Stennis, Forrestal, Kuznetsov und Tarawa. Bei Schrägdeck-Trägern liegt das Final Bearing etwa 9° links vom Schiffskurs (BRC). Die Tarawa hat ein gerades Deck, ihr Final Bearing ist gleich dem BRC.

Die Seite **Carrier** im Funkrad erscheint nur, wenn die Mission einen Träger enthält.

### Frequenzen

Alle Trägerstationen teilen sich eine Frequenz: Marshal, Tower, Approach, Departure und Paddles.

| Quelle | Frequenz |
|---|---|
| Funkfrequenz der Trägereinheit im Missionseditor | Gilt, wenn gesetzt |
| Eintrag `"Carrier"` in `"Frequencies"` in `config.jsonc` | Gilt, wenn die Mission keine setzt. Standard **127.5** |

Der Schlüssel `"Carrier"` steht nicht in der installierten `config.jsonc`-Vorlage, DCS-ATC ergänzt den Standard aber automatisch. Zum Ändern den Schlüssel selbst eintragen:

```jsonc
"Frequencies": { "ATIS": 263.5, "Ground": 264.5, "Tower": 265.0, "Approach": 266.5,
                 "Range": 267.5, "AWACS": 251.5, "Tanker": 255.5, "Carrier": 127.5 },
```

Mit SRS geht alles, was du auf einer Trägerfrequenz sendest, an den Träger. Ohne SRS (nur Sprechtaste) schickt DCS-ATC einen Spruch an den Träger, wenn:

- er *Marshal*, *mother*, *Paddles*, *ball*, *Clara*, *carrier* oder *pigeons* enthält, oder
- du schon eingecheckt bist und er *initial*, *see you at*, *commencing*, *Charlie*, *platform*, *needles*, *up and …*, *down and …* oder *on and on* enthält.

Du kannst den Träger auch unter Funkrad → Allgemein → **Platz wählen** auswählen. Sprüche ohne Stationsnamen gehen dann bis zur Landung an den Träger.

Die Trägerstimmen kommen aus `"Voices"` → `"Carrier"` in `config.jsonc`. Standard ist `"en_US-ryan-medium@0.97|en_GB-alan-medium@1.02"`, jede Station bekommt eine der beiden Stimmen.

### Welcher Case geflogen wird

DCS-ATC wählt den Case beim Check-in nach Wetter und Tageszeit:

| Case | Bedingungen |
|---|---|
| **Case I** | Tag, Wolkenuntergrenze mindestens 3000 ft, Sicht mindestens 5 NM |
| **Case II** | Tag, Wolkenuntergrenze 1000–3000 ft |
| **Case III** | Nacht (Sonne mehr als 6° unter dem Horizont), Wolkenuntergrenze unter 1000 ft oder Sicht unter 5 NM |

Der Case bleibt für deine ganze Recovery gleich.

### Das Schiff dreht in den Wind

Solange ein Spieler in der Recovery ist oder startet oder Missions-KI auf dem Schiff landet, steuert DCS-ATC die Trägergruppe in den Wind. Die Fahrt wird auf etwa 27 kt Wind über Deck gesetzt, das Schiff läuft 5–30 kt. Liegt innerhalb etwa 5 NM voraus Land, dreht das Schiff nicht, und Marshal nennt dir den tatsächlich gefahrenen Kurs. Etwa 3 Minuten nach der letzten Landung bzw. dem letzten Start kehrt das Schiff auf seine Missionsroute zurück.

### Funkrad: Seite Carrier

| Rad-Eintrag | Gesendet wird |
|---|---|
| Marshal check in | `Marshal, Enfield 1 1, checking in, mother's 180 for 35, angels 18, state 6.2.` (Peilung und Entfernung vom Schiff, Höhe und Sprit werden automatisch eingesetzt) |
| See you at | `Enfield 1 1, see you at angels.` |
| Initial | `Enfield 1 1, initial.` (Case I: nur ein Texthinweis, siehe unten) |
| Commencing | `Enfield 1 1, commencing.` |
| Platform | `Enfield 1 1, platform.` |
| Ball | `Enfield 1 1, Hornet ball, 5.2.` (Typ und Sprit werden eingesetzt) |
| Clara | `Enfield 1 1, Hornet Clara, 5.2.` |
| Pigeons | `Enfield 1 1, pigeons.` |

**ENTER** (oder **0**) am offenen Rad sendet den Spruch, der zur Lage passt. Hat dich ein Lotse gerade etwas gefragt, sendet ENTER die Antwort:

- nach „call the ball“ deinen Ball-Call mit Typ und Sprit;
- nach „say needles“ das, was deine ACLS-Nadeln zeigen sollten;
- nach „report see me“ *see you at angels N*, nach „report commencing“ *commencing, angels N, state X*, nach „report see you at ten“ *see you at ten*.

Sonst folgt ENTER der Recovery: *Marshal, checking in* → *see you at angels* (Case I) bzw. *commencing* (Case II/III) → *platform* → *see you at ten* (Case II, innerhalb 12 NM vom Schiff) → *ball* (nach „call the ball“). Jede Meldung wird nur einmal angeboten: nach „see you at“ bietet ENTER im Holding nichts mehr an, *platform* nur bis zum Radarkontakt bei Approach (nach einem Bolter nicht wieder). Im Case I gibt es nach Charlie nichts zu senden (zip lip). Nach einem Katapultstart in Case II/III sendet ENTER *airborne*, dann *on top, angels N*. Auf dem Deck sendet es nichts.

Der Sprit in Check-in und Ball-Call wird nur für Trägertypen mit bekannter Tankgröße eingesetzt: Hornet, Tomcat, Viking, Hawkeye und Harrier.

### Check-in bei Marshal (alle Cases)

Check in etwa 50 NM vorher. Nenne deine Position vom Schiff, Höhe und Sprit:

> **Du:** "Marshal, Enfield 1-1, mother's 360 for 35, angels 18, state 6.2."

Wörter, die einen Check-in auslösen: *Marshal*, *mother*, *check in* / *checking in*, *inbound*, *recovery* oder *state*. Der gemeldete Sprit wird gegen Bingo geprüft (siehe unten).

**Antwort Case I:**

> **Marshal:** "Enfield one one, Marshal, case one recovery, expected BRC three five four, altimeter two niner niner two, hold angels 2, expected Charlie zero seven, report see me."

Dazu ein Texthinweis: *Holding über dem Schiff in Angels 2, Linkskreise (ca. 5 NM Ø). Warten auf „signal Charlie“.*

**Antwort Case II/III:**

> **Marshal:** "Enfield one one, Marshal, case three recovery, CV-1 approach, expected final bearing three four five, altimeter two niner niner two. Marshal mother's one six five radial, 21 DME, angels 6. Expected approach time zero seven."

Dazu ein Texthinweis mit dem Weg zum Marshal-Punkt (Radial 165, 21 DME, TACAN 73X, Angels 6; dort Holding, zur Anflugzeit „commencing“ melden).

- **Holding-Höhen:** Case I ab Angels 2 aufwärts. Case II/III ab Angels 6 aufwärts, der Marshal-Punkt liegt bei (Angels + 15) DME auf dem Gegenkurs des Final Bearing. Jedes Flugzeug bekommt die nächste freie Höhe.
- **Zeiten:** als Minuten nach der vollen Stunde („zero seven“ heißt hh:07). Der Stack wird der Reihe nach abgearbeitet:
  - Case I: Charlie-Zeiten im Abstand von 90 s, die erste mindestens 2 Minuten nach dem Check-in.
  - Case II/III: Anflugzeiten im Abstand von 1 Minute, die erste mindestens 5 Minuten nach dem Check-in, oder später, wenn du noch weit vom Marshal-Punkt bist.
- **Höhenmesser** nach `"AltimeterUnit"` (Standard `"auto"`): westliche Typen hören inHg („altimeter two niner niner two“), andere hPa („QNH one zero one three“).

Sprüche im Holding:

| Du sagst | Antwort |
|---|---|
| Case I: "Enfield 1-1, see you at angels 2" (Antwort auf „report see me“) | "Enfield one one, update state, switch Tower." Dann deinen Sprit nennen: "Enfield 1-1, 5.9" → "Enfield one one, Marshal, roger, state five point niner." (ENTER bietet `state 5.9` an). Mit Sprit schon im Spruch („see you at angels 2, state 5.9“): "Enfield one one, roger, switch Tower." Ab dann antwortet dir der Tower (ein zweites „see you at“ bekommt „roger“). |
| Case II/III: "see you at angels 6", oder *established*, *holding*, *in the stack* | "Enfield one one, roger." |
| Case II/III, Rücklesen: "Enfield 1-1, 165 radial, 21 DME, angels 6" | "Enfield one one, readback correct." Ein falscher Wert wird korrigiert: "Enfield one one, negative, angels 6." Das Rücklesen ist kein neuer Check-in; deine EAT bleibt. |
| "Enfield 1-1, state 4.5" | "Enfield one one, Marshal, roger, state four point five." (unter Bingo: dein Bingo-Signal, siehe unten) |
| "Marshal, Enfield 1-1, pigeons" (auch *TACAN*, *say position*) | "Enfield one one, mother bears 180, 35 miles, expected BRC 354, TACAN seven three X-ray, ICLS channel one one." |

### Case I

**1. Holding und Charlie.** Kreise in deinen zugewiesenen Angels über dem Schiff links herum. Tower gibt „signal Charlie“, wenn alles zutrifft:

- du bist Erster im Stack;
- du bist innerhalb 10 NM vom Schiff;
- deine Charlie-Zeit ist erreicht;
- das Schiff steht im Wind, oder seit deiner Charlie-Zeit sind 5 Minuten vergangen;
- weniger als zwei Missions-KI-Flugzeuge sind in der Landerunde.

> **Tower:** "Enfield one one, signal Charlie."
> *(Text: Sinkflug zum Initial: 3 NM achteraus, 800 ft, Kurs 354, 350 kt; Break links über dem Bug, Gegenanflug 600 ft.)*

Ist deine Charlie-Zeit 1 Minute vorbei, während du nicht im Holding bist (mehr als 10 NM weg), bekommt der Nächste Charlie und du rückst ans Ende des Stacks:

> **Marshal:** "Enfield one one, signal Delta, expected Charlie two zero."

Verzögert sich das Deck (das Schiff dreht noch in den Wind, oder zwei oder mehr KI-Flugzeuge sind im Muster), sagt Marshal es einmal allen: „99, signal Delta.“ Weiter halten: Reihenfolge im Stack und deine Charlie-Zeit bleiben.

**2. Initial und Break: zip lip.** Case I wird in Funkstille geflogen. Du meldest kein „initial“. DCS-ATC erkennt das Initial, wenn du 1,5–6 NM achteraus, innerhalb 1 NM der Schiffsachse und unter 1500 ft bist, und zeigt nur einen Texthinweis: *Initial: zip lip, kein Funk bis zum eigenen Ball-Call (ca. 3/4 NM: „Hornet ball, 5.2“).* Sagst oder wählst du nach Charlie „initial“, kommt derselbe Text und keine Funkantwort. Vor Charlie (noch im Holding) wird es abgelehnt, und du behältst deinen Platz: „Enfield one one, negative, not Charlie, hold angels 2, expected Charlie zero seven.“

**3. Groove und Ball-Call.** Roll in den Groove und melde den Ball selbst bei etwa ¾ NM:

> **Du:** "Enfield 1-1, Hornet ball, 5.2."
> **Paddles:** "Roger ball."

Hast du bis etwa ½ NM (im Groove, unter 600 ft, langsamer als 195 kt, Fahrwerk unten) keinen Ball gemeldet, fordert Paddles ihn an:

> **Paddles:** "Enfield one one, call the ball."

Meldest du „Clara“ (Ball nicht in Sicht), antwortet Paddles „Enfield one one, roger Clara, keep it coming.“

Ein Ball-Call zählt nur im Groove: bis 1,5 NM achteraus, innerhalb 0,3 NM der Mittellinie und unter 1500 ft. Anderswo gibt es keine Antwort. Im Groove zählt auch ein undeutlich verstandener Ball-Call.

**Ohne Ball-Call keine Landung.** Vor deinem Ball-Call (oder „Clara“) ruft Paddles nichts. Hast du nach „call the ball“ immer noch nicht gemeldet, kommt bei etwa 0,3 NM im Groove „Wave off, wave off.“ und die Note `WO (kein Ball-Call)`.

### Case II und Case III (CV-1-Anflug)

**1. Marshal-Punkt.** Flieg zu Radial, DME und Angels von Marshal und warte dort bis zu deiner erwarteten Anflugzeit (EAT). Bist du zur EAT nicht innerhalb 5 NM vom Marshal-Punkt, bekommst du eine neue Zeit:

> **Marshal:** "Enfield one one, Marshal, new expected approach time one two."

**2. Commencing.** Marshal ruft dich zur EAT nicht. Zur EAT verlässt du den Marshal-Punkt und meldest:

> **Du:** "Marshal, Enfield 1-1, commencing, angels 6, altimeter 29.92, state 5.2."
> **Marshal:** "Enfield one one, radar contact, final bearing three four five, switch Approach."
> *(Text: 4000 ft/min bis 5000 ft, dort „platform“ melden; dann 2000 ft/min bis 1200 ft; bei 10 DME Fahrwerk, Klappen, Haken.)*

Bist du am Marshal-Punkt, hast aber 60 s nach deiner EAT nicht gemeldet, fragt Marshal einmal: "Enfield one one, Marshal, report commencing." 2 Minuten nach deiner EAT bekommst du eine neue Zeit hinten im Stack („Enfield one one, Marshal, new expected approach time …“) und einen Verstoß. ENTER bietet *commencing* ab 15 s vor deiner EAT an, sobald Marshal es annimmt. Früher antwortet Marshal „Enfield one one, negative, your expected approach time is zero seven.“ und du hältst weiter.

**3. Platform.** Bei 5000 ft:

> **Du:** "Enfield 1-1, platform."
> **Approach:** "Enfield one one, Approach, radar contact, 19 miles." (spätere „platform“-Meldungen, z. B. nach einem Bolter: "Enfield one one, roger.")

Meldest du dich nicht bei Approach, identifiziert Approach dich von selbst, sobald du unter 4500 ft oder innerhalb 12 NM bist. Vor dem Radarkontakt gibt Approach keine Kurskorrekturen.

**4. Endanflug und ACLS.** Auf dem Final Bearing (innerhalb 2 NM davon und Richtung Schiff) führt dich Approach:

- **Mehr als 0,3 NM neben dem Final Bearing:** ein Kurs zum Einfangen, höchstens alle 30 s und nicht, wenn du schon zurückdrehst. Zum Beispiel: "Enfield one one, left of course, fly heading zero zero five."
- **Bei etwa 6 DME:** "Enfield one one, Approach, ACLS lock on, say needles."

  > **Du:** "Enfield 1-1, up and on." (je eins aus *up / down / on* **und** *left / right / on*)
  > **Approach:** "Enfield one one, concur."

  Die Nadeln zeigen, wohin du fliegen musst: Nadel oben heißt, du bist zu tief, Nadel links heißt, du bist rechts vom Kurs. Passt deine Meldung nicht zu deiner echten Lage, korrigiert Approach, etwa: "Enfield one one, disregard needles, below glidepath, on course."
- **Bei 3, 2 und 1½ Meilen:** "Enfield one one, three miles, on glidepath, left of course." (ebenso two miles und one and a half miles)
- **Bei ¾ Meile:** "Enfield one one, three quarter mile, call the ball." Ball melden wie im Case I: "Enfield 1-1, Hornet ball, 5.2." → "Roger ball." (Case III nennt dazu den Wind über Deck: "Roger ball, 25 knots, axial.")

**Case II: see you at ten.** Im Case II meldest du das Schiff selbst in Sicht, meist bei 10 DME:

> **Du:** "Enfield 1-1, see you at ten."
> **Approach:** "Enfield one one, roger, switch Tower."
> *(Text: Sinken zum Initial: 3 NM hinter dem Heck, 800 ft, Kurs 354, 350 kt; Break über dem Bug nach links, Downwind 600 ft.)*

Ab dem Initial fliegst du das Case-I-Muster (zip lip). Hast du innerhalb 10 NM nicht gemeldet, fragt Approach einmal: "Enfield one one, report see you at ten." Ohne die Meldung bleibst du im CV-1-Anflug bis „three quarter mile, call the ball“.

### LSO-Rufe (Paddles)

Paddles beobachtet deinen Groove ab etwa ¾ NM, solange du mit waagerechten Flächen, innerhalb 15° vom Final Bearing und langsamer als 195 kt fliegst. Paddles ruft höchstens etwa alle 3 s und nennt immer den größten Fehler zuerst. Vor deinem Ball-Call (oder „Clara“) schweigt Paddles, außer bei einem Wave-off.

| Ruf | Bedeutung |
|---|---|
| "Roger ball." | Ball-Call quittiert |
| "A little power." / "Power." / "Power, power." | Zu tief (leicht, deutlich, weit unter dem Gleitpfad) |
| "Don't settle." | Leicht zu tief und sinkt weiter |
| "Easy with it." | Kommt schnell wieder hoch |
| "You're high." | Über dem Gleitpfad |
| "Right for lineup." / "Come left." | Links / rechts der Mittellinie |
| "You're slow." / "You're fast." | Anstellwinkel neben On-Speed (siehe unten) |
| "Wave off, wave off." | Pflicht-Durchstarten |
| "Wave off, wave off, foul deck." | Landebereich nicht frei |
| "Bolter, bolter, bolter." | Haken hat die Seile verpasst: volle Leistung, abfliegen |

**Fahrt-Rufe** („slow“/„fast“) brauchen den On-Speed-AoA deines Typs und Anstellwinkeldaten, die es nur für das Flugzeug auf dem PC mit DCS-ATC gibt. Einstellung in `config.jsonc`:

```jsonc
"OnSpeedAoa": { "FA-18": 8.1 },   // Grad je Typ-Präfix; andere Typen bekommen keine Fahrt-Rufe
```

Für einen anderen Typ ein paar Anflüge fliegen und den Wert aus der Logzeile `[LSO] … AoA im Mittel …` ablesen.

### Wave-off und Bolter

Paddles schickt dich weg, wenn:

- **Foul Deck:** ein Flugzeug steht im Landebereich.
- **Fahrwerk oben:** dein Fahrwerk ist bekanntermaßen oben, irgendwo ab 0,6 NM bis zum Heck.
- **Kein Ball-Call:** du hast nach „call the ball“ bis etwa 0,3 NM nicht gemeldet.
- **Nah dran zu weit daneben** (0,04–0,25 NM): mehr als etwa 1,8° zu hoch, 1,2° zu tief oder 3° neben der Mittellinie.

Ein Wave-off ist Pflicht. Ignorierst du ihn, wiederholt Paddles ihn bis zum Heck höchstens zweimal, und ein Trap danach wird als **Cut Pass** bewertet (C, 0 Punkte).

Nach Bolter oder Wave-off:

- **Case I:** kein Funk. Steigen, wieder in die Landerunde und neu anfliegen. „Call the ball“ kommt wieder.
- **Case II/III:** Departure: "Enfield one one, Departure, radar contact, turn left heading 165, maintain 1200, downwind." Etwa 5 NM achteraus Approach: "Enfield one one, Approach, turn left heading 345, intercept final." ACLS und Meilen-Ansagen beginnen dann von vorn.

### Bingo und Ausweichen

Du bekommst ein Signal zum Ausweichen, und deine Recovery endet, wenn:

- dein Sprit unter Bingo liegt oder
- du den dritten Bolter oder Wave-off hast.

Bingo wird gegen deine Tankanzeige oder den gemeldeten State geprüft. Es ist etwa 1500 lb plus 20 lb je NM bis zum nächsten eigenen oder neutralen Platz mit langer Bahn.

> **Tower/Approach:** "Enfield one one, your signal is bingo, pigeons Kobuleti 084, 44 miles." / "…, your signal is divert, pigeons …"

Die Recovery endet auch, wenn du mehr als 50 NM vom Schiff wegfliegst (bzw. 10 NM über deinen nächsten Punkt hinaus, wenn du von weiter draußen eingecheckt hast) oder an Land landest. Mit eigenem AWACS in der Luft übergibt Marshal: "Enfield one one, Marshal, switch Overlord 251.5."

### Benotung

Nach jedem Anflug erscheint die Note als Text im Spiel, zum Beispiel:

`LSO: (OK) 3-wire, groove 14 s, HIM SLOIC NESA (3 Punkte)`

| Note | Punkte | Bedeutung |
|---|---|---|
| `_OK_` | 5 | Perfekter Anflug: nur winzige Abweichungen und richtige Groove-Zeit |
| `OK` | 4 | Vertretbare Abweichungen |
| `(OK)` | 3 | Ordentlich. Ein 1-Wire-Trap wird nie besser bewertet |
| `--` | 2 | No grade (große Abweichungen) |
| `B` | 2.5 | Bolter |
| `OWO` | 2 | Eigener Wave-off (du bist selbst durchgestartet) |
| `WO` | 1 | Weggeschickt (mit Zusatz für eingefahrenes Fahrwerk, wenn das der Grund war; `WO (kein Ball-Call)` bei fehlendem Ball-Call) |
| `C` | 0 | Cut Pass (Wave-off missachtet) |
| `WOFD` | – | Wave-off wegen Foul Deck, nicht bewertet |

**Kommentare:**

- **Fehler:** `H` hoch, `LO` tief, `LUL` / `LUR` links / rechts ausgerichtet, `SLO` langsam, `F` schnell.
- **Wo:** `X` am Anfang (¾–½ NM), `IM` in der Mitte, `IC` nah dran, `AR` am Heck.
- **Wie stark:** `(…)` leicht, ohne Zeichen = deutlich, `_…_` stark.
- **Case-I-Groove-Zeit** (ab ½ NM): `NESA` unter 15 s, `LIG` (long in groove) über 18 s.

Das Seil kommt aus dem DCS-Landeereignis, wenn die Mission es meldet. Sonst schätzt DCS-ATC es aus deinem Aufsetzpunkt.

### Start vom Träger

Auf dem Deck gibt es **keinen Funk**. Jeder Spruch auf dem Deck zeigt nur einen Hinweis: kein Land-Tower, Anlassen, Rollen und Katapult nach den Zeichen der Deckcrew. Keine Platz-ATIS und kein Ground antwortet dir auf dem Deck. Das Schiff dreht zum Start in den Wind.

**Case-I-Abflug:** nach dem Katapult nur Text: geradeaus parallel zum BRC, 500 ft bis 7 NM, dann frei steigen, kein Funk.

**Case-II/III-Abflug:**

> *(Text Case III: „airborne“ melden, geradeaus steigen, bei 7 DME auf den 10-DME-Bogen zum Abflugradial 024, darauf auswärts, über den Wolken „on top“ melden.)*
> *(Text Case II: „airborne“ melden, geradeaus parallel zum BRC 354 in Sicht bleiben, bei 7 DME auf den 10-DME-Bogen zum Abflugradial 024, erst darauf durch die Wolken steigen, über den Wolken „on top“ melden.)*
> **Du:** "Enfield 1-1, airborne."
> **Departure:** "Enfield one one, Departure, radar contact, departure radial zero two four."

Das Abflugradial ist bei DCS-ATC immer BRC + 30°, damit der Bogen nicht zu dem Radial führt, auf dem du nach dem Start ohnehin schon bist.
> **Du:** "Enfield 1-1, on top, angels 8."
> **Departure:** "Enfield one one, Departure, switch Overlord 251.5." (ohne AWACS: „Enfield one one, Departure, cleared to switch.“)

Andere Abflugmeldungen unterwegs („passing 2.5“, „arcing“, „established outbound“, „Kilo“) quittiert Departure mit „Enfield one one, roger.“ Meldest du kein „on top“, kommt die Übergabe bei 20 NM automatisch. Nach einem Trägerstart erreicht auch der Approach-Eintrag **Airborne (nach Start)** im Rad Departure.

### Rotten und anderer Verkehr

- **Rottenflieger:** im Verband mit dem Lead (in der Luft, nicht auf dem Deck) teilen sie im Case I Marshal-Check-in und Charlie-Zeit des Leads. Danach fliegt jeder Rottenflieger Break und Groove selbst und bekommt eigene LSO-Rufe und Note. Verlässt ein Rottenflieger im Holding den Verband, bekommt er einen eigenen Platz im Stack. Case II/III wird einzeln angeflogen: Checkt der Lead ein, bekommt jeder Rottenflieger eine eigene Marshal-Zuweisung (nächste Angels, Marshal-Punkt 1 DME weiter draußen, Anflugzeit 1 Minute nach dem Lead) und meldet „commencing“ selbst.
- **Ohne Check-in:** jedes eigene Flugzeug im Groove mit Fahrwerk unten bekommt den LSO („Enfield one two, call the ball.“, Rufe und Note), auch ohne Check-in.
- **Missions-KI:** KI-Flugzeuge der Mission mit Landewegpunkt auf dem Träger machen ihren Ball-Call selbst („Hornet ball, 5.4.“ → „Roger ball.“). Sie schweigen, solange ein Spieler im Groove ist. Ein KI-Flugzeug, das dreimal durchstartet, wird zum nächsten Platz geschickt. Dieser Funk folgt `"AiChatter"` (Standard `true`).
- **Luftraumwache:** fliegst du ohne Check-in auf einen eigenen Träger zu, warnt Marshal dich. Das passiert unter 2500 ft innerhalb 5 NM mit Kurs aufs Schiff oder unter 1500 ft im Anflugsektor achteraus (±30° vom Final Bearing, bis 10 NM). Zum Beispiel: "Enfield one one, Marshal, you are approaching a warship, turn left heading 260, remain clear of the carrier by 5 miles." Hat in deiner Rotte noch niemand mit einem Lotsen gesprochen, beginnt der Ruf mit „Unidentified aircraft …“ und fordert dich auf, dich zu identifizieren. Bist du nicht auf den Träger gerastet, geht der Ruf auf Guard. Schalter: `"AirspaceWatch"` (Standard `true`).

### Phrasen am Träger

| Phase | Du sagst (erkannte Wörter) | Station antwortet |
|---|---|---|
| Check-in | "Marshal, Enfield 1-1, mother's 360 for 35, angels 18, state 6.2" (*Marshal / mother / checking in / inbound / recovery / state*) | Marshal: Case, BRC bzw. Final Bearing, Höhenmesser, Angels, Charlie-Zeit bzw. EAT (Case I: "…, report see me") |
| Holding Case I | "see you at angels 2" | Marshal: "update state, switch Tower" |
| Holding Case II/III | "see you at angels 6" (*see you at / established / holding / in the stack*) | Marshal: "roger" |
| Holding Case II/III | Rücklesen "165 radial, 21 DME, angels 6" | Marshal: "readback correct" (oder "negative, …" mit dem richtigen Wert) |
| Holding | "state 4.5" | Marshal: "roger, state four point five" (oder Bingo-Signal) |
| Jederzeit | "pigeons" / "TACAN" / "say position" | Marshal: Peilung und Entfernung zum Schiff, BRC, TACAN, ICLS |
| Case I | (nichts, warten) | Tower: "signal Charlie" (oder Marshal: "signal Delta, expected Charlie …"; Deck verzögert: "99, signal Delta") |
| Case-I-Initial | (zip lip; „initial“ gibt nur einen Texthinweis) | – (vor Charlie: "negative, not Charlie, hold angels 2, expected Charlie …") |
| Case II/III | "commencing, angels 6, state 5.2" | Marshal: "radar contact, final bearing …, switch Approach" |
| Case II/III | "platform" | Approach: "Approach, radar contact, N miles" (danach: "roger") |
| Case II/III | "up and on" / "down and left" / "on and on" … | Approach: "concur" oder "disregard needles, …" |
| Case II | "see you at ten" | Approach: "roger, switch Tower" |
| Groove | "Hornet ball, 5.2" (*ball*) | Paddles: "Roger ball." (Case III: "Roger ball, 25 knots, axial.") |
| Groove | "Clara" (*Clara / Clarence*) | Paddles: "roger Clara, keep it coming." |
| Start II/III | "airborne" | Departure: "radar contact, departure radial …" |
| Start II/III | "passing 2.5" / "arcing" / "established outbound" / "Kilo" | Departure: "roger" |
| Start II/III | "on top, angels 8" | Departure: "switch Overlord 251.5" (ohne AWACS: "cleared to switch") |
| Unklarer Spruch | – | Marshal: "Enfield one one, say again." (im Groove: keine Antwort, außer bei einem undeutlichen Ball-Call, der zählt) |

### Beispiel: Case-I-Recovery

```
Du:       Marshal, Enfield 1 1, checking in, mother's 352 for 38, angels 18, state 6.4.
Marshal:  Enfield one one, Marshal, case one recovery, expected BRC three five four,
          altimeter two niner niner two, hold angels 2, expected Charlie one five, report see me.
          (Text: Holding über dem Schiff in Angels 2, Linkskreise)
Du:       Enfield 1 1, see you at angels 2.
Marshal:  Enfield one one, update state, switch Tower.
Du:       Enfield 1 1, 6.1.
Marshal:  Enfield one one, Marshal, roger, state six point one.
   ... Holding über dem Schiff ...
Tower:    Enfield one one, signal Charlie.
          (Text: Sinkflug zum Initial: 3 NM achteraus, 800 ft, Kurs 354, 350 kt)
   ... Initial, Break, Gegenanflug, 180 – kein Funk (zip lip) ...
Du:       Enfield 1 1, Hornet ball, 5.1.
Paddles:  Roger ball.
Paddles:  A little power.
Paddles:  Right for lineup.
   ... Trap ...
          (Text: LSO: OK 3-wire, groove 16 s, (LUL)IC (4 Punkte))
```

### Beispiel: Case-III-Recovery

```
Du:       Marshal, Enfield 1 1, checking in, mother's 170 for 45, angels 20, state 6.0.
Marshal:  Enfield one one, Marshal, case three recovery, CV-1 approach, expected final
          bearing three four five, altimeter two niner niner two. Marshal mother's
          one six five radial, 21 DME, angels 6. Expected approach time two two.
   ... Warten am Marshal-Punkt ...
Du:       Marshal, Enfield 1 1, commencing, angels 6, state 5.2.
Marshal:  Enfield one one, radar contact, final bearing three four five, switch Approach.
Du:       Enfield 1 1, platform.
Approach: Enfield one one, Approach, radar contact, 19 miles.
Approach: Enfield one one, left of course, fly heading three five five.
Approach: Enfield one one, Approach, ACLS lock on, say needles.
Du:       Enfield 1 1, up and on.
Approach: Enfield one one, concur.
Approach: Enfield one one, three miles, on glidepath, on course.
Approach: Enfield one one, two miles, below glidepath, on course.
Approach: Enfield one one, one and a half miles, on glidepath, on course.
Approach: Enfield one one, three quarter mile, call the ball.
Du:       Enfield 1 1, Hornet ball, 4.6.
Paddles:  Roger ball, 25 knots, axial.
   ... Aufsetzen, Haken springt ...
Paddles:  Bolter, bolter, bolter.
          (Text: LSO: B, groove 12 s (2.5 Punkte))
Departure: Enfield one one, Departure, radar contact, turn left heading one six five,
          maintain 1200, downwind.
Approach: Enfield one one, Approach, turn left heading three four five, intercept final.
```

## AWACS, Tanker und Range

Neben den Platzlotsen spricht DCS-ATC auch für das **AWACS** der Mission (oder ein Bodenradar als GCI), ihre **Tanker** und eine **Bombing Range**. Dazu kommen ein **Crew Chief** auf der Bordsprechanlage und **Funksprüche von KI-Flügen** im Gefecht. DCS-ATC spawnt keine Flugzeuge, es spricht nur für AWACS und Tanker, die der Missionsbauer gesetzt hat. Fehlt eine Station in der Mission, antwortet niemand und ihr Eintrag im Funkrad bleibt ausgeblendet.

### So erreichst du diese Stationen

| Station | Wer antwortet | Standardfrequenz (`config.jsonc` → `Frequencies`) | Wann es sie gibt |
|---|---|---|---|
| AWACS | Nächstes eigenes E-3, E-2, A-50 oder KJ-2000 der Mission in der Luft | Die AWACS-Frequenz der Mission, wenn die AWACS-Gruppe eine hat, sonst `"AWACS": 251.5` | Ein AWACS deiner Seite ist in der Luft oder deine Seite hat ein Frühwarnradar (GCI) |
| Tanker | Nächster passender eigener Tanker (KC-135, KC-135MPRS, KC-10, IL-78 …) | `"Tanker": 255.5` | Ein Tanker deiner Seite ist in der Luft |
| Range | „Range Alpha“ | `"Range": 267.5` | Die Mission hat eine Triggerzone, deren Name mit `Range` beginnt |

Du erreichst eine Station auf drei Wegen:

- **SRS (Stimme):** Frequenz der Station rasten und senden. DCS-ATC leitet jeden Spruch nach deiner Frequenz.
- **Stationsname im Spruch:** auf einer Platzfrequenz oder mit Sprechtaste ohne SRS erreicht ein Spruch mit Stationsnamen sie trotzdem: *Overlord / AWACS / Magic / Moscow / Darkstar / Wizard / Focus*, *Texaco / Shell / Arco / Tanker* (auch *refuel*), *Range Alpha / Range control*. Manche Wörter gehen direkt ans AWACS: *picture*, *bogey dope*, *declare*, *spike*, *request sort*.
- **Funkrad** oder F10-Menü **ATC → AWACS / Tanker**. Ist die Pilotenstimme an (`"PilotVoice"`), wird deine Anfrage zuerst vorgesprochen, etwa *„Overlord, Enfield 1-1, request picture.“*

Funkrad-Einträge:

| Untermenü | Einträge |
|---|---|
| Range | Check in · In hot · Off safe · Check out |
| AWACS | Check in · Picture · Bogey dope · Sort · Nächster Tanker · Vektor nächster Platz · Check out · Gefecht (Committing · Fox three · Splash · Defending · Request support · Unable · Winchester · Bingo, RTB · Say again) |
| Tanker | Request rejoin · Visual · Observation · Pre-contact · Refuel complete |

Die Radmitte schlägt die Antwort auf das vor, was der Lotse gerade verlangt hat. Nach *„Report in hot“* etwa **Range: in hot**, nach *„cleared pre-contact“* **Tanker: pre contact**, nach *„report complete or request more“* **Tanker: complete**.

Sagst du *„radio check“* oder *„how do you read“* zu einer dieser Stationen, kommt etwa *„Enfield one one, Overlord, read you five.“* War die Erkennung unsicher, antwortet die Station *„say again“* und tut nichts. Ein genuscheltes „in hot“ bringt nie ein „cleared hot“.

### AWACS

#### Name und Frequenz

- **Rufname:** aus dem DCS-Rufzeichen des Missions-AWACS (Magic, Overlord, Darkstar, Wizard, Focus …). Ein Zahlen-Rufzeichen wird ziffernweise gesprochen („two four seven“). Ohne Rufzeichen heißt das AWACS **Overlord**.
- **GCI:** hat deine Seite kein AWACS, aber ein Frühwarnradar, antwortet ein GCI: **Magic** für Blau, **Moscow** für Rot. Wird das AWACS abgeschossen oder landet es, übernimmt das GCI und gibt jedem Eingecheckten *„radar contact“* und ein Picture.
- **Radarumlauf:** neue und verschwundene Gruppen meldet das AWACS mit dem nächsten Umlauf seines Radars (alle 10 s); eine pop-up group nahe bei dir sofort.
- **Was es sieht:** nur Flugzeuge, die die Sensoren deiner Seite tatsächlich erfasst haben, innerhalb 250 NM um dich. Ein erfasster, aber nicht identifizierter Kontakt heißt **bogey** und hat keinen Typ.
- **Erst bogey, dann hostile:** Jeder neue Kontakt ist zuerst **bogey**. Ist sein Typ erkannt und verfolgt ihn das AWACS etwa 40 s (die ID-Kriterien), oder sofort, wenn er auf deine Seite schießt, erklärt das AWACS ihn für alle auf der Frequenz: *„Overlord, north group, hostile.“* Ein *declare* antwortet mit dem aktuellen Stand.
- **Übergabe nach dem Abflug:** ist ein AWACS oder GCI aktiv, übergibt Tower bzw. Departure mit *„leaving control zone, contact Overlord 251.5.“*

#### Check-in und Check-out

Das AWACS ruft dich von sich aus erst nach dem Check-in. Ein Check-in am Boden bekommt *„check in airborne“* und zählt nicht. Stellst du nach der Landung ab, ist dein Check-in gelöscht. Ein Touch-and-go behält ihn.

| Du sagst | AWACS antwortet |
|---|---|
| „Overlord, Enfield 1-1, **checking in**“ (auch *check in*, *with you*, *on station*; ein Sprit-State darf folgen) | *„Enfield one one, Overlord, radar contact, picture, …“*. Ist eine heiße Gruppe innerhalb 30 NM, kommt die Bedrohung zuerst: *„threat, group BRAA …“* |
| „Overlord, Enfield 1-1“ (nur Rufzeichen) | Noch nicht eingecheckt: Check-in mit Picture. Schon eingecheckt: *„go ahead.“* |
| „**checking out**“ / „check out“ | *„copy checking out, good day.“* Danach keine Rufe mehr von sich aus. |

Checkst du direkt nach dem Start tief ein, hörst du „radar contact“ genau einmal. Bist du eingecheckt, aber das AWACS hat noch keinen Radarkontakt gemeldet, holt es das mit Picture nach, sobald du durch etwa 3000 ft über Grund steigst.

Solange du beim AWACS eingecheckt bist, überwacht die Zonenwache des Platzes nur Platzrunde und Bahn.

#### Deine Sprüche

| Du sagst (erkannte Wörter) | Antwort |
|---|---|
| **picture** | Bild der drei nächsten Gruppen (Format unten). *„picture clean“*, wenn nichts da ist. |
| **bogey dope**, **braa**, **threat**, **nearest bandit**, **nearest group**, **snap** | Nächste Gruppe: *„Enfield one one, Overlord, group BRAA 354, 20, 20 thousand, hot, hostile, single, Fulcrum.“* Ohne etwas: *„clean.“* |
| **declare** + Position: *„declare bullseye 030 45“* oder *„declare 0 3 0 for 4 5“* (Peilung/Entfernung von dir) | Sucht im Umkreis von 5 NM um diesen Punkt und antwortet *„bullseye 030, 45, hostile, single, Fulcrum“*, *„friendly“*, *„furball“* (Feind und Freund gemischt), *„neutral …“* oder *„clean“*. |
| **declare** ohne Position, mit Radar-Lock | Dein aufgeschaltetes Ziel, innerhalb 3 NM und ±5000 ft. Kann auch *„bogey“* liefern (da ist ein Flugzeug, aber das AWACS hat es nicht erfasst). Nur für den Spieler am PC mit DCS-ATC. |
| **declare** ohne Position und ohne Lock | Nächste Gruppe, auch neutrale Flugzeuge. |
| **spike** / **spiked 270** (missweisende Peilung; Ziffern einzeln sprechen geht) | *„spike 270, group BRAA …, hostile …“* oder *„spike 270, nothing known.“* Ein eigener Jäger in der Richtung: *„spike 270, buddy spike, BRAA 265, 15, 25 thousand, hot.“* Ohne Peilung: *„say spike bearing.“* |
| **sort**, **request sort**, **targeting**, **request target** | Ziele je Element von links nach rechts: *„Enfield one one, Overlord, target lead group BRAA … Enfield one three, target trail group BRAA …“*. Eine Zweierrotte bekommt eine Gruppe, eine Viererrotte zwei. |
| **committing**, **targeting**, **targeted**, **engaged**, **engaging** (optional mit Gruppenname wie *„north group“* oder Richtung *north/south/east/west*) | Bestätigt diese Gruppe mit BRAA, sonst die nächste: *„Enfield one one, Overlord, north group BRAA 354, 20, 20 thousand, hot.“* Benennst du eine namenlose Gruppe zuerst nach ihrer Richtung, gilt dieser Name. Ab dann ist die Gruppe deine: keine Threat-Rufe mehr zu ihr, nur kurze Ziel-Updates bei 20 und 10 NM. Hat ein anderer eigener Flug (Spieler oder KI) die Gruppe schon, lehnt das AWACS ab: *„Enfield one one, Overlord, skip it, north group Ford one targeted.“* |
| **splash** / **splash 2** / **kill** | *„Overlord copies splash two, west group. East group remains.“* (die abgeschossene Gruppe, dann was bleibt: ein oder zwei Gruppennamen oder *„3 groups remain“*), oder *„Overlord copies splash two, picture clean.“*, wenn nichts mehr da ist. Das *picture clean* wiederholt danach niemand, auch nicht nach dem *„Overlord copies, splash, picture clean.“* eines eigenen KI-Jägers. |
| **winchester**, **RTB**, **tally** | *„Overlord copies.“* | winchester gibt außerdem deine Zuweisung frei.
| **unable** (mit Grund: *„unable, winchester / bingo / defensive“*) | *„copy.“* Die Gruppe geht an den nächsten freien Flug; bei winchester oder bingo wirst du nicht mehr verplant. Antwortest du auf ein commit nicht, fragt das AWACS nach 10 s einmal nach (*„commit north group, how copy?“*) und gibt die Gruppe nach 25 s dem nächsten Flug; nach zwei unbeantworteten commits wirst du nicht mehr verplant, bis du wieder sprichst. Eine kurze Quittung (*„Enfield one one.“*, *„resetting“*, *„copy“*) auf eine Weisung bekommt keine Antwort, *„committing“* direkt nach dem commit auch nicht (kein Echo). |
| **fox one/two/three** (optional mit Gruppe) | Keine Antwort. Die Gruppe gehört dir: Ein eigener KI-Flug darauf hört *„Ford one, Overlord, skip it, north group Enfield one targeted.“* |
| **defensive** / **defending** / **missile** | Keine Antwort; deine Gruppe bekommt einen zweiten Flug. |
| **request support** (optional mit Gruppe) | *„copy, north group.“* und ein zweiter Flug wird angesetzt. |
| **judy** (oder *„targeted north group“*) | *judy*: keine Antwort. Danach keine Entfernungs-Updates mehr zu dieser Gruppe – du führst den Abfang selbst, das AWACS hält die Frequenz frei. |
| **merged** | keine Antwort, und das AWACS sagt für diese Gruppe nicht mehr selbst *„merged“*. |
| **fox …** / **defensive** | 5 s lang gehen auf der Frequenz nur dringende Sprüche (Threat, defending) raus; Lagebild, Antworten und Geplauder warten. |
| **say again** | Der letzte AWACS-Spruch an dich noch einmal. |
| **tanker**, **texaco**, **shell**, **arco** (etwa *„request nearest tanker“*) | *„nearest tanker Texaco, bearing 084, 30 miles, angels 22, TACAN 52 X-ray, contact Texaco 255.5.“* Weitere mögliche Antworten: *„no tanker airborne“* oder *„negative, no compatible tanker airborne“*, wenn kein Tanker zu deinem Flugzeug passt (Boom oder Korb). |
| **vector**, **home plate**, **bingo**, **nearest airfield**, **recovery**, **divert** (Platzname optional) | *„Kutaisi bears 245, 38 miles.“* Nur eigene oder neutrale Plätze. Mit Platzname dieser, sonst der nächste. |
| **mayday** / **pan pan** | *„roger mayday. Nearest friendly field Kutaisi, bearing 245, 38 miles, contact Kutaisi Approach two six three decimal zero. Say intentions.“* DCS-ATC wählt dann diesen Platz für dich, dessen Approach übernimmt den Notfall. |
| alles andere | *„say again.“* |

#### Format des Picture

- **BRAA** ab deiner Position: missweisende Peilung ziffernweise, Entfernung in NM, Höhe („20 thousand“, „5 hundred“), dann der Aspekt: **hot**, **flank** + Richtung, **beam** + Richtung, **drag** + Richtung.
- **Bullseye für alle, BRAA für dich:** Picture und Lage-Updates (new group, split, combined, picture clean) gehen an alle auf der Frequenz im Bullseye-Format mit Flugrichtung, *„Overlord, new group, bullseye 030, 45, 20 thousand, track south, hostile, single.“* Sprüche für einen Flug (threat, bogey dope, snap, spike, commit, pop-up) bleiben BRAA von dir aus. Braucht einen Bullseye in der Mission, sonst ist alles BRAA. `"AwacsBullseye": false` (Standard `true`) stellt alles auf BRAA von dir zurück.
- **Gruppen:** Kontakte innerhalb 3 NM zueinander bilden eine Gruppe. Das Picture nennt bis zu drei Gruppen:
  - 1 Gruppe: *single group*
  - 2 Gruppen: *azimuth* (nebeneinander) oder *range* (hintereinander)
  - 3 Gruppen: *wall*, *ladder*, *vic* oder *champagne*
  - mehr als 3: *„5 groups“*, dann die drei nächsten
- **Gruppennamen** (north group, lead group, trail group, east lead group …) gelten in späteren Sprüchen bis zum nächsten Picture. Sie gehören deiner Seite, nicht dir: Alle Spieler und alle eigenen KI-Flüge an diesem AWACS nennen dieselbe Gruppe gleich. Die einzige Gruppe im Radarbild ist die *single group*; eine Gruppe, die in keinem Picture benannt wurde, bekommt beim ersten Ansprechen einen Namen nach ihrer Lage zu den anderen (*„north group“*) und behält ihn.
- **Zusätze:** *hostile / bogey / neutral*; *single / two contacts / heavy, N contacts*; *fast* (ab 600 kt) / *very fast* (über 900 kt); *high* (über 40.000 ft) / *low* (unter 1000 ft über Grund). Der Typ kommt zuletzt: NATO-Namen für östliche Typen (Flanker, Fulcrum, Fishbed …), Spitznamen für westliche (Hornet, Viper, Eagle …).
- **Wer welche Gruppe hat:** Hat ein anderer eigener Flug eine Gruppe, sagen Picture, bogey dope und snap das am Ende: *„…, hostile, single, Fishbed, Ford one targeted.“*
- **Kurze Folgemeldungen:** Alle Zusätze hörst du beim ersten Ruf zu einer Gruppe, in jedem Picture und in jeder Antwort auf bogey dope, declare, snap und spike. Folgemeldungen von selbst (threat, commit, split …) nennen nur Name, Ort, Höhe und Aspekt, *„threat, north group BRAA 354, 15, 20 thousand, hot.“*, dazu nur, was sich geändert hat (*„single“* nach einem Abschuss, ein neuer Typ, *„hostile“* nach der Erklärung). Die AWACS-Stimme spricht zügig; eine Folgemeldung dauert etwa 5 Sekunden.

#### Rufe des AWACS von sich aus (nur nach Check-in)

| Ruf | Wann |
|---|---|
| *„threat, group BRAA …, hot, hostile …“* | Eine **nicht vergebene** Gruppe zeigt innerhalb 30 NM auf dich (hot oder flank). Erneut bei jeder Annäherungsstufe (20/10/5 NM) und alle 45 s, solange sie innerhalb 10 NM hot bleibt. Keine Threat-Rufe zu einer Gruppe, auf die du oder ein anderer eigener Flug committed hat oder die er targeted. |
| *„commit east group, BRAA …“* / *„maintain CAP.“* | Das AWACS führt als Air Battle Manager den Luftkampf deiner Seite: Es verteilt die Feindgruppen auf die Jäger auf Station, also eigene KI-Jäger und Spieler, die **on station** gemeldet haben (CAP-Auftrag, z. B. *„Overlord, Enfield 1-1, on station“*). Jede Gruppe, die innerhalb 60 NM hot wird (40 NM bei einem Flug nur mit Fox 2), bekommt einen Flug, ein Leaker unabhÃ¤ngig vom Aspekt, eine große Gruppe (ab 3 Kontakten) zwei; das AWACS nimmt den nächsten Flug mit Raketen (Fox 3 zuerst) und Sprit. Die anderen Flüge hören *„maintain CAP.“* Die Zuweisung gilt, bis die Gruppe abgeschossen oder verschwunden ist oder der Flug RTB, bingo oder winchester ist. Ohne „on station“ gibt es nur Threat-Rufe, und du hörst die Zuweisungen der anderen. „RTB“, „bingo“, „off station“ oder „checking out“ beenden den Auftrag. |
| *„east group BRAA 012, 19, 26 thousand, hot.“* | Kurzes Ziel-Update zu **deiner** Gruppe (committed oder targeted) bei 20 und 10 NM, ohne Zusätze. |
| *„reset.“* / *„picture clean, reset.“* | Deine Gruppe ist abgeschossen oder verschwunden – oder zieht seit 2 Minuten jenseits deiner Commit-Reichweite + 10 NM ab: zurück auf deine CAP. Etwa eine Minute später kannst du die nächste offene Gruppe bekommen: *„recommit west group, BRAA …“* |
| *„north group, flank west.“* | Deine Gruppe hat um 45° oder mehr gedreht: ihr neuer Aspekt zu dir (hot, flank, beam, drag und ihr Kurs). |
| *„Overlord, north group, 45 miles from Overlord, hot, Overlord moving south.“* (an alle) | Eine Gruppe fliegt unter 50 NM auf das AWACS zu: sie wird zuerst verplant (wie ein leaker), und das AWACS weicht 10 Minuten lang 40 NM aus. |
| *„picture, 2 groups …“* (statt *„new group“*) | Eine zweite Gruppe ist aufgetaucht und deine *„single group“* hat einen neuen Namen (z. B. *„west group“*); höchstens einmal pro Minute. |
| *„Overlord, new group, bullseye …“* (an alle) / *„pop-up group, BRAA …“* (an dich) | Eine Gruppe wird erstmals innerhalb `"AwacsNewGroupNm"` erfasst (Standard 200, Bereich 40–200). Pop-up, wenn sie erstmals innerhalb 30 NM um dich auftaucht. |
| *„Overlord, west group split, 2 groups azimuth 6, east group bullseye …, west group bullseye …“* / *„Overlord, north and south groups combined, bullseye …“* (an alle) | Wenn die Änderung 12 s stabil ist, höchstens einmal pro Minute je Gruppe. |
| *„north group merged.“* | Eine Gruppe innerhalb 3 NM um dich und weniger als 5000 ft über oder unter dir. Mit dem Gruppennamen aus dem Picture; eine Gruppe ohne Namen nur *„merged.“* |
| *„Overlord, west group faded, last known bullseye 336, 63.“* (an alle) | Eine gemeldete Gruppe ist seit 30 s nicht mehr erfasst und wurde nicht abgeschossen. Taucht sie wieder auf, ist sie wieder *new group* (bzw. *pop-up*). |
| *„Overlord, furball, bullseye 354, 20.“* (an alle; ohne Bullseye an dich in BRAA) | Ein eigener Jäger und eine Feindgruppe sind innerhalb 5 NM beieinander. Je Gruppe einmal in 2 Minuten; solange es dauert, gibt es zu dieser Gruppe keine einzelnen Threat-Rufe. |
| *„Overlord, leaker, north group, bullseye 040, 20, 15 thousand, track east, hostile.“* (an alle) | Eine Feindgruppe, die niemand targeted hat, ist an der CAP vorbei (näher am nächsten eigenen Platz als jeder Jäger auf Station) und hält auf diesen Platz zu. Je Gruppe einmal; nur, wenn es eine CAP gibt. |
| *„Overlord, picture clean.“* (an alle) | Seit 2 Minuten keine Gruppe auf dem Radar. Danach zählt jede Gruppe wieder als „new“. Nicht, wenn *picture clean* eben nach einem splash gesagt wurde. |

- **Dringendes auf Guard:** bist du nicht auf die AWACS-Frequenz gerastet, werden Threat- und Merged-Rufe auf Guard (243.0 / 121.5) wiederholt, höchstens einmal pro Minute: *„Enfield one one, Overlord on guard, threat, … Contact Overlord 251.5.“*
- **Mehrere Spieler auf derselben AWACS-Frequenz:** einen „new group“- oder „picture clean“-Ruf, den ein anderer Spieler gerade gehört hat, bekommst du nicht noch einmal.
- **Rotten mit mehreren Spielern:** Rufe von sich aus gehen an den Lead.
- **Debriefing:** AWACS-Check-in, Check-out, Threat- und Merged-Rufe kommen ins Debriefing, bis zu 10 Zeilen.

#### Beispiel

```
Du:       Overlord, Enfield 1-1, on station, angels 22.
Overlord: Enfield one one, Overlord, radar contact, picture, 2 groups azimuth 10.
          East group, bullseye 040, 60, 26 thousand, track west, hostile, two contacts, Flanker.
          West group, bullseye 020, 58, 25 thousand, track south, hostile, single, Fulcrum.
...
Overlord: Ford one, Overlord, commit west group, BRAA 290, 35, 25 thousand, hot.
Overlord: Enfield one one, Overlord, commit east group, BRAA 012, 38, 26 thousand, hot.
Du:       Overlord, Enfield 1-1, committing east group.
Overlord: Enfield one one, Overlord, east group BRAA 012, 36, 26 thousand, hot.
Overlord: Enfield one one, Overlord, east group BRAA 013, 19, 26 thousand, hot.
Du:       Overlord, Enfield 1-1, splash two.
Overlord: Enfield one one, Overlord copies splash two.
Overlord: Enfield one one, Overlord, reset.
Du:       Overlord, Enfield 1-1, bingo, request vectors to Kutaisi.
Overlord: Enfield one one, Overlord, Kutaisi bears 245, 62 miles.
```

### Tanker

#### Welcher Tanker antwortet

- **Name:** das DCS-Rufzeichen des Tankers (Texaco, Shell, Arco …), nicht der Gruppenname.
- **Zwei Tanker gleichen Namens:** sind zwei in der Luft (etwa Texaco 1-1 und Texaco 2-1), wird jeder mit Nummer angesprochen, *„Texaco two one“*. Jeder hat eigene Warteschlange und eigene Kurvenrufe. Mit *„Texaco 2-1, …“* wählst du einen.
- **Auswahl:** der Tanker, den du nennst. Sonst der, mit dem du schon arbeitest, sonst der nächste passende.
- **Boom oder Korb:** Boom-Tanker (KC-135, KC-10) bedienen F-16, F-15, F-4, A-10, B-1, B-52, C-17 und E-3. Korb-Tanker (KC-135MPRS, KC-10 mit Drogue und andere) alle anderen.
- **Keine Luftbetankung:** Flugzeuge ohne Tanksonde bzw. Aufnahme bekommen *„negative, no compatible tanker“*. Dazu zählen Hubschrauber, MiG-15/19/21/29, F-5, F-86, Su-27, Su-25 (nicht die Su-25T), Trainer und Warbirds.

#### Wer spricht, Korb und Boom

Es spricht immer der **DCS-ATC-Tanker**, auch wenn die Mission einen eigenen DCS-Tanker hat. Er führt den ganzen Ablauf wie unten beschrieben. Die DCS-eigenen Tanker bleiben stumm.

DCS bedient Korb bzw. Boom aber nur über seinen eigenen Tankerdialog im F-Menü. Für **den Spieler am PC mit DCS-ATC** drückt DCS-ATC deshalb im Hintergrund das DCS-Funkmenü für dich: *Intent to refuel* bei deiner Anfrage, *Ready pre-contact* bei deinem „pre-contact“. Der DCS-Tanker antwortet darauf nicht hörbar.

- Mit Easy Communication rastet DCS dabei dein Funkgerät selbst auf die Missionsfrequenz des Tankers. Der DCS-ATC-Tanker spricht dann auch auf dieser Frequenz. Teilt sie sich der Tanker mit einem Platz (etwa 261.0 = Senaki UHF), wechselt DCS-ATC deshalb nicht den Platz.
- Das nutzt `"CommsKey"` (DCS-Funkmenü-Taste als Scancode, Standard `43`, also `\` auf US- und `#` auf deutschen Tastaturen). Mit `0` drückt DCS-ATC das Menü nicht.
- **Andere Spieler** im Multiplayer bedienen das DCS-Tankermenü selbst.

#### Deine Sprüche

| Du sagst (erkannte Wörter) | Tanker antwortet |
|---|---|
| **request rejoin**, *join*, *rendezvous*, *refuel*, *fuel*, *tank*, *gas*, *AAR*, *checking in* | Treffpunkt: *„Enfield one one, Texaco, bearing 084, 30 miles, angels 22, track north, 280 knots, TACAN 52 X-ray. Expect the join in 5 minutes, 1000 feet below, report visual.“* |
| **visual** (*tally*, *in sight*, *judy*), mehr als 8 NM weg | *„continue, 20 miles, report visual.“* |
| **visual**, innerhalb 8 NM | *„cleared to join, observation left wing, number 1.“* Warten mehrere: *„number 2. Enfield one two first, then Enfield one one.“* |
| **observation** / **left wing** / **joined** | Nach dem Join: *„cleared pre-contact.“* Sind andere vor dir: *„stabilize observation left wing, number 2.“* Noch ohne Join-Freigabe (innerhalb 2 NM): *„cleared to join, left observation, number 1.“* |
| **pre-contact** (*precontact*, *in position*, *ready for contact*, *stabilized*, *ready to refuel*) | *„Enfield one one, cleared contact, basket ready.“* bzw. *„boom ready“*. Mehr als 2 NM weg: *„negative, bearing 084, 12 miles, report visual.“* Noch nicht dran: *„negative, hold observation left wing, number 2.“* Noch ohne Join-Freigabe: *„negative, not cleared pre-contact, cleared to join, left observation, number 1.“* |
| **disconnect** | *„copy disconnect, move to the right wing.“* |
| **right wing** | *„roger, report complete or request more.“* |
| **complete** / *thanks* / *good day* / *departing* / *leaving* | *„total offload 5.4, cleared to depart, contact Overlord 251.5.“* (Abgabe in tausend lb). Ohne AWACS: *„… frequency change approved.“* |
| alles andere | *„say again.“* Anderes Gerede am Tanker, etwa ein Sprit-State, setzt deinen Kontakt nie zurück. |

#### Was am Boom bzw. Korb passiert

DCS-ATC reagiert auf den echten DCS-Tankkontakt:

- **Kontakt:** *„Contact.“*
- **Trennung vor vollem Tank** (vom Korb gefallen): *„disconnect, return to pre-contact.“* Du behältst deinen Platz, und koppelst du innerhalb 2 Minuten wieder an, geht dieselbe Betankung weiter.
- **Trennung mit vollem Tank:** *„Enfield one one, disconnect, you received 6200 pounds. Clear right wing. Report complete, or pre-contact for more.“* Der Nächste in der Reihe bekommt dann sein eigenes *„Enfield one two, Texaco, cleared pre-contact.“*
- **Reihenfolge:** wer zuerst am Tanker war, nicht wer zuerst gefragt hat.

#### Join-Führung und Coaching

- **Join-Führung** (`"TankerVectors": true`, Standard): nach deiner Anfrage führt dich der Tanker auf einen Abfangkurs 1000 ft unter ihm. Zum Beispiel: *„Enfield one one, Texaco, turn left heading 040 for the join, Texaco 11 o'clock, 25 miles, maintain angels 21.“* Der erste Vektor kommt 30 s nach der Anfrage. Danach ein neuer bei 20, 10 und 5 NM oder wenn du mehr als 20° daneben bist, höchstens alle 30 s. Bei 5 NM: *„Enfield one one, Texaco 12 o'clock, 5 miles, report visual.“* Drehst du weg, endet die Führung. Mit `false` nur die erste Peilung.
- **Coaching** hinter dem Tanker:
  - *„slow closure.“*
  - *„stabilize.“*
  - Nur Boom: *„forward 80.“* (Fuß)
- **Tankerkurven:** einmal an alle Empfänger: *„Texaco, turning right.“*
- **Breakaway:** *„breakaway, breakaway, breakaway.“*, wenn du zu nah kommst, zu schnell aufschließt oder im Kontakt den Tankbereich verlässt. Alle 15 s wiederholt und im Debriefing gezählt. Danach bist du zurück in der Beobachtung: kein *„return to pre-contact“*, für ein neues *„cleared contact“* wieder *pre-contact* melden.
- **Annäherung ohne Freigabe:** hast du keine Betankung angefragt und kommst innerhalb 3 NM und ±1000 ft an einen eigenen Tanker, hörst du *„Enfield one one, Texaco, you are not cleared to join, remain 1000 feet below, report ready for rejoin.“* Höchstens einmal alle 3 Minuten, als Verstoß im Debriefing. Nicht in diesen Fällen:
  - Du schließt von hinten auf Tankerkurs auf.
  - Dein Rottenführer ist schon freigegeben.
  - Du bleibst direkt nach „complete“ im Block.
- **Tanker verlassen:** fliegst du mehr als 10 NM weg, endet deine Betankung.

**Kontakt und Anschluss nur mit Freigabe** (Tanker-Stimme von DCS-ATC, siehe oben):

- **Kontakt ohne „cleared contact“:** *„Enfield one one, Texaco, you are not cleared contact, disconnect, return to left observation.“* und ein Verstoß. Hängt schon ein anderer Empfänger an diesem Tanker, kommt stattdessen *„breakaway, breakaway, breakaway.“*
- **Zu nah vor „cleared to join“:** hast du ums Tanken gebeten, aber noch keine Anschlussfreigabe, und kommst innerhalb 1 NM und ±500 ft an den Tanker: *„Enfield one one, Texaco, not cleared to join, remain 1000 feet below, report visual.“* Bist du 30 s später noch dort: Verstoß.

#### Beispiel (Multiplayer-Mitspieler, DCS-ATC-Tankerstimme)

```
Du:      Texaco, Enfield 1-1, request rejoin.
Texaco:  Enfield one one, Texaco, bearing 084, 30 miles, angels 22, track north, 280 knots, TACAN 52 X-ray.
         Expect the join in 5 minutes, 1000 feet below, report visual.
Texaco:  Enfield one one, Texaco, turn right heading 095 for the join, Texaco 1 o'clock, 20 miles, maintain angels 21.
Texaco:  Enfield one one, Texaco 12 o'clock, 5 miles, report visual.
Du:      Texaco, Enfield 1-1, visual.
Texaco:  Enfield one one, Texaco, cleared to join, observation left wing, number 1.
Du:      Texaco, Enfield 1-1, observation left wing.
Texaco:  Enfield one one, Texaco, cleared pre-contact.
Du:      Texaco, Enfield 1-1, pre-contact.
Texaco:  Enfield one one, cleared contact, basket ready.
Texaco:  Contact.
Texaco:  Enfield one one, disconnect, you received 6200 pounds. Clear right wing. Report complete, or pre-contact for more.
Du:      Texaco, Enfield 1-1, refuel complete.
Texaco:  Enfield one one, Texaco, total offload 6.2, cleared to depart, contact Overlord 251.5.
```

### Range

Einen Range-Lotsen gibt es nur, wenn die Mission eine Triggerzone hat, deren Name mit **Range** beginnt. Er heißt **Range Alpha**.

- **Ziele:** feindliche Bodeneinheiten in der Zone. DCS-ATC misst deine Treffer, spawnt aber nichts.
- **Gewertete Waffen:** deine Bomben, Raketen und Luft-Boden-Flugkörper werden gegen die nächste feindliche Bodeneinheit in der Zone gemessen. Ein Einschlag innerhalb 500 m zählt.
- **Uhrzeit-Angabe:** 12 Uhr ist dein Kurs beim Auslösen, 6 Uhr heißt also zu kurz.

#### Deine Sprüche

| Du sagst (erkannte Wörter) | Range antwortet |
|---|---|
| **checking in** (*check in*, *inbound*, *request entry*, *entering*, *with you*, *request range*) | *„Enfield one one, Range Alpha, altimeter two niner niner two, wind on target two seven zero at one zero, range is cold. Range bears one eight zero, 12 miles. Enter from the north, run-in headings one six zero to two zero zero, minimum altitude 1500 feet. Report IP.“* Ist ein anderes Flugzeug hot: *„…, one aircraft in.“* statt *„range is cold“*. |
| **IP inbound** (nach dem Check-in) | *„Enfield one one, continue.“* Vor dem Check-in: *„negative, you are not checked in. Report check in.“* |
| **in hot** (*rolling in*, *inbound hot*, *in from the north*, *in heading 360*, *in now*, *ready in* oder ein Spruch, der auf „in“ endet) | *„cleared hot.“* (Bedingungen unten) |
| **in dry** | *„cleared dry.“* Gleiche Bedingungen wie für „cleared hot“ (unten). Ein trockener Anflug belegt die Range bis *off* (oder bis sie wie bei einem heißen Anflug von selbst frei wird), wird aber nicht gewertet. |
| **off** (*off safe*, *off west* …) | *„Enfield one one, pass 2, 3 impacts, best 12 meters at 6 o'clock, 2 gun hits. Report in hot or checking out.“* Warst du nicht hot (oder trocken): *„roger, range is cold.“* Mit *hung ordnance* oder *hot gun* in diesem oder einem früheren Spruch: *„pass 2, copy hung ordnance, check switches safe, no further hot passes, report checking out.“* |
| **winchester** | *„pass 2, …, check switches safe, report checking out.“* |
| **checking out** (*check out*, *leaving*, *departing*, *good day*) | *„check switches safe. 2 passes, best impact 12 meters at 6 o'clock. Cleared off the range to the north, contact Overlord 251.5, good day.“* Mit *hung ordnance* oder *hot gun* im Spruch (oder früher in diesem Besuch gemeldet): *„copy hung ordnance, check switches safe. … Cleared off the range to the north via the hung ordnance route, advise Approach, good day.“* |
| alles andere | *„say again. Report check in, in hot, off, or checking out.“* |

Zur Check-in-Antwort:

- Die Höhenmessereinstellung kommt vom nächsten Platz mit Wetterdaten, in inHg für westliche Typen und in hPa (*„QNH one zero one three“*) für andere (siehe `"AltimeterUnit"`).
- *Wind on target* kommt vom selben Platz. Ohne Wetterdaten fehlt er, unter 3 kt heißt es *„calm“*.
- Peilung, Entfernung und Einflugrichtung nur, wenn du mehr als 3 NM weg bist. Die Anflugkurse sind die Peilung zur Range ±20°; näher dran heißt es „run-in headings any“.

**Bedingungen für „cleared hot“:**

| Lage | Antwort |
|---|---|
| Nicht eingecheckt | *„negative, you are not checked in. Report check in.“* |
| Hung ordnance oder hot gun gemeldet | *„negative, hung ordnance, report checking out.“* |
| Am Boden oder mehr als 10 NM weg | *„continue, report in inside 10 miles.“* |
| Außerhalb der Zone und nicht auf sie zu | *„continue dry.“* |
| Ein anderes Flugzeug ist hot oder trocken drin | *„continue, one aircraft in.“* Sobald die Range frei ist, kommt *„cleared hot“* (bzw. *„cleared dry“*) ohne neuen Ruf, wenn Lage und Kurs noch passen. |

#### Wertung und Durchsetzung

- **Treffermeldung:** etwa 3 s nach dem letzten Einschlag einer Auslösung kommt ein Spruch:
  - *„Enfield one one, 12 meters at 6 o'clock.“*
  - *„rockets, best 30 meters at 7 o'clock.“*
  - *„direct hit.“*
  - *„no score.“* (kein Ziel innerhalb 500 m)
- **Zerstört:** ein zerstörtes Ziel bekommt einmal je Anflug *„good hits, target destroyed.“*
- **Auslösen ohne Freigabe:** *„check fire, check fire, check fire. Release without clearance, switches safe.“* Höchstens alle 30 s, der Anflug wird nicht gewertet, ein Verstoß kommt ins Debriefing. Entscheidend ist die Freigabe beim Auslösen, nicht beim Einschlag. Lang fallende Waffen, die mit Freigabe ausgelöst wurden, zählen.
- **Automatisches Ende eines Anflugs:**
  - Mehr als 10 NM aus der Zone, während du hot bist: *„Range Alpha, leaving the range, range is cold, pass 1. Report in hot or checking out.“*
  - Kein „off“ 60 s nach dem letzten Einschlag (180 s ohne jeden Einschlag) oder 60 s mehr als 3 NM draußen und abfliegend: zuerst *„Enfield one one, Range Alpha, report off.“* Kommt 30 s später immer noch kein „off“: *„Range Alpha, assuming you are off, range is cold, pass 1. …“* und ein Verstoß.
- **Neuer Besuch:** jeder Check-in beginnt wieder bei Pass 1. Landen und Abstellen beendet den Check-in; ein Touch-and-go behält ihn.
- **Debriefing:** jeder Anflug und eine Gesamtsumme über alle Besuche.

#### Beispiel

```
Du:     Range Alpha, Enfield 1-1, checking in from the north, angels 10.
Range:  Enfield one one, Range Alpha, altimeter two niner niner two, wind on target two seven zero
        at one zero, range is cold. Range bears one eight zero, 9 miles. Enter from the north,
        run-in headings one six zero to two zero zero, minimum altitude 1500 feet. Report IP.
Du:     Range Alpha, Enfield 1-1, IP inbound.
Range:  Enfield one one, continue.
Du:     Range Alpha, Enfield 1-1, in hot.
Range:  Enfield one one, cleared hot.
Range:  Enfield one one, 12 meters at 6 o'clock.
Du:     Enfield 1-1, off safe.
Range:  Enfield one one, pass 1, impact 12 meters at 6 o'clock. Report in hot or checking out.
Du:     Range Alpha, Enfield 1-1, checking out.
Range:  Enfield one one, Range Alpha, check switches safe. 1 pass, best impact 12 meters at 6 o'clock.
        Cleared off the range to the north, contact Overlord 251.5, good day.
```

### KI-Flüge im Funk

Eigene KI-Flüge innerhalb 60 NM um einen Spieler ihrer Seite funken im Gefecht. Das sind die Flüge der Mission, nicht Platzverkehr und nicht deine eigenen KI-Rottenflieger. Sie sprechen im taktischen Netz, also auf der AWACS-Frequenz (aus Mission oder Config). Sprüche innerhalb der eigenen Rotte (defending, spike, naked, hit, joker, ops check) gehen nur auf der Rottenfrequenz aus dem Missionseditor raus, und nur, wenn sie sich von der AWACS-Frequenz unterscheidet. Ist AWACS oder GCI der Mission aktiv, quittiert es.

| Ereignis | KI-Spruch |
|---|---|
| Jägerflug hat eingependelt (erstmals 60 s auf ±500 ft über 10.000 ft; nur mit AWACS/GCI) | *„Overlord, Ford one, on station, bullseye 040, 35, angels 25.“* → AWACS: *„Ford one, Overlord, picture clean.“* (oder *„picture, single group BRAA …“*); vorher sagt der Lead auf der Rottenfrequenz *„Ford, fence in.“* (heimwärts *„fence out.“*) |
| Feindgruppe hot unter 40 NM (das AWACS weist zu: jede Gruppe an den passendsten freien Flug auf Station, eine große an zwei, kein Hin und Her) | AWACS: *„Ford one, Overlord, commit north group, bullseye 040, 65, 23 thousand, hostile, two contacts, Flanker.“* → *„Ford one, committing.“* → *„Ford one, targeted north group.“* (den Ort nannte schon das commit); mit zwei oder mehr Kontakten in der Gruppe danach *„Ford one, sorted.“* (jedes Element hat ein eigenes Ziel). Ist eine weitere freie Gruppe hot unter 40 NM, nimmt das zweite Element sie: *„Ford one one, targeted north group.“* / *„Ford one two, targeted south group.“* (im Vierer *„Ford one three“*). Andere Flüge nahe einer vergebenen Gruppe: *„Dodge one, Overlord, maintain CAP.“* → *„Dodge one.“*; ein Flug, der eine vergebene Gruppe will: *„skip it, north group Ford one targeted.“* → *„Dodge one.“*; Gruppe weg: *„Ford one, Overlord, reset.“* (oder *„picture clean, reset.“*) → *„Ford one, resetting.“*, später zurück auf Station *„recommit …“*. Die Gruppennamen sind die AWACS-Namen deiner Seite (wie im Picture); ohne Namen *„commit group, …“* und *„Ford one, targeted.“*. Ohne AWACS committet der Flug selbst: *„Ford one, committing.“* → *„Ford one, targeted group bullseye …“*. Committing/targeted entfallen, wenn sie vorher schießen; bingo, winchester und RTB geben die Zuweisung frei. |
| Luft-Luft-Start | *„Viper one one, fox three, north group.“* (fox one / fox two / fox three; der AWACS-Gruppenname; bei nur einer Gruppe nur *„fox three.“*; ohne Namen aus dem Picture die Richtung, *„fox three, north.“*). Vor dem ersten Schuss auf eine Gruppe, die der Flug nicht schon mit targeted übernommen hat: *„Viper one, engaged north group.“* als eigener Spruch (je Gruppe einmal in 3 min) · *„maddog.“* (aktiver Flugkörper ohne Ziel) |
| Luft-Boden-Start | *„magnum“* (Anti-Radar), *„bruiser“* (Anti-Schiff), *„rifle“* (andere Flugkörper), *„bombs away“* · *„guns.“* · *„shack.“* (Bombentreffer auf Bodenziel) |
| Flugkörper wird aktiv / ausmanövriert / Zeit um | *„pitbull.“* · *„trashed.“* · *„timeout.“* (erwartete Flugzeit ohne Treffer um, grob 5 s + 3 s je NM Entfernung beim Schuss; danach kein „trashed“ für diese Rakete) · Lead dreht binnen 2 min nach dem Schuss mehr als 120° vom Ziel weg: *„Ford one, pump south.“* (Sprit über joker und Fox 3 übrig: er kommt wieder) bzw. *„Ford one, out south.“* |
| Feindradar schaltet sie auf (Radarwarner, Rottenfrequenz) | Luft: *„Viper one two, spike two six four, Flanker.“* (missweisende Peilung, Typ wenn bekannt) · Boden: *„Viper one two, mud spike, SA-6, east.“* · Lock weg: *„naked.“* (ist der Lock weg, bevor der Spruch gesagt ist, kommt keiner von beiden) |
| Auf sie geschossen | *„Viper one two, SAM west, defending.“* / *„launch north, defending.“* |
| Abschuss | *„Viper one one, splash two, Flanker.“* → AWACS: *„Overlord copies, splash, picture clean.“* |
| Treffer | *„I'm hit.“* · schwer getroffen, auf Guard: *„Mayday, mayday, mayday, Viper one two, hit, RTB, bullseye 084, 12.“* |
| Abgeschossen / Ausstieg | *„one two is down, good chute, bullseye …“* oder *„no chute“* · *„ejecting, ejecting.“* · Notsender auf Guard · 90 s später auf Guard: *„Mayday …, on the ground, bullseye …, uninjured.“* → AWACS: *„copy, SAR notified, monitor guard.“* (passiert auch, wenn **du** aussteigst) |
| Sprit / Waffen | *„joker.“* (unter 35 %) · *„bingo.“* eines Elements auf der Rottenfrequenz, dann der Lead für den ganzen Flug *„Ford one, bingo, RTB.“* (unter 22 %) → AWACS: *„Ford one, Overlord, copy bingo.“* · *„winchester.“* |
| Alle 15 min in der Luft (Rottenfrequenz; nicht direkt nach eigenem Schuss) | *„Ford, ops check, one, six point two, four and two.“* – *„two, five point nine, four and two.“* (Sprit in Tausend lb, dann Fox-3 und Fox-2) |
| Du fragst: *„Ford one, say status“* (oder *„Ford one two, say status“* für ein Element; im Funk an AWACS/taktisch) | taktische Frequenz: *„Ford one, engaged north group, six point two, four and two.“* (engaged / targeted / on station, niedrigster Sprit des Flugs, Fox-3 und Fox-2 zusammen) |
| Du fragst: *„Ford one, Dagger one, say position“* | *„Ford one, bullseye 260, 25, angels 25.“* |
| Lead an Lead: *„Ford one, Dagger one, targeted south group.“* | *„Ford one.“* – die south group gehört dir, Ford gibt sie ab (*„skip it“*) und nimmt eine andere; hat Ford schon eine andere Gruppe: *„Ford one, targeted north group.“* |
| Heimweg (nach „on station“: 60 s lang über 40 NM von der Station, Kurs auf den nächsten eigenen Platz und ihm 20 NM näher; nicht nach Bingo oder einem Schuss in den letzten 5 min) | *„Overlord, Ford one, RTB, checking out.“* → AWACS: *„Ford one, Overlord, check out approved.“* Danach landet der Flug als Anflug unter DCS-ATC am nächsten eigenen Platz (Approach, Tower; auch nach Bingo). |
| Steigflug durch 10.000 ft (mit AWACS) | *„Overlord, Ford one, checking in as fragged, flight of two, angels 20, playtime 55.“* (playtime: Minuten bis Bingo) → AWACS: *„Ford one, Overlord, radar contact.“* |
| Merge | *„merged.“* (innerhalb 3 NM und weniger als 5000 ft Höhenunterschied) |

**Einstellungen** (`config.jsonc` oder „KI-Flüge im Gefecht“ im Einstellungsfenster):

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `"AiFlightComms"` | `"voll"` | `"voll"` = alle Sprüche; `"taktisch"` = nur fox, engaged, timeout, out, splash, trashed, bingo, defending, merged, down und Guard-Sprüche; `"aus"` = aus |
| `"AiFlightRadiusNm"` | `60` | Nur KI-Flüge in diesem Abstand zu einem Spieler ihrer Seite funken |
| `"AiOwnWingmen"` | `false` | Auch deine eigenen KI-Rottenflieger funken (DCS vertont sie schon) |
| `"AiFollowAwacs"` | `true` | Eigene KI-Jäger fliegen die Gruppe wirklich an, auf die das AWACS sie ansetzt (AttackGroup im Missionsskript); *skip it* / *reset* nimmt sie wieder weg. Außerdem weicht das AWACS einer Gruppe, die auf es zufliegt, 40 NM aus (10 min). Ein beschossener Jäger greift sofort die Gruppe des Schützen an. `false`: die KI fliegt ihre Mission; das AWACS setzt KI-Flüge nicht an, ein Flug meldet selbst „Overlord, Ford one, committing.“, wenn eine Gruppe unter 40 NM hot ist |

Damit die Frequenz brauchbar bleibt, gehen je Frequenz höchstens 10 niedrig priorisierte und 16 normale Sprüche pro Minute raus. Überzählige niedrig priorisierte fallen weg.

### Bodenpersonal und Crew Chief

Das Bodenpersonal ist **standardmäßig aus**. Einschalten im F10-Menü **ATC → Einstellungen → Bodenpersonal an/aus** (*Anzeigen* zeigt den aktuellen Stand). Als Standard für alle Missionen `crew = true` in `Saved Games\DCS\Scripts\DcsAtc\DcsAtcOptions.lua` setzen. Eine einzelne Mission kann das per Trigger *Mission Start* mit Aktion *DO SCRIPT* und dem Code `DCSATC_OPTIONS = { crew = true }` überschreiben. Das Bodenpersonal besteht nur aus Soldaten und Fahrzeugen; DCS-ATC spawnt weiterhin keine Flugzeuge.

Was passiert:

- **Geparkt:** stehst du 10 s still auf einem Parkplatz, erscheinen vorne links ein Crew Chief und vorne rechts ein Brandposten. Der Crew Chief spricht über SRS auf der **Bordsprechanlage** deines Flugzeugs. Ohne SRS siehst du nur den Text.
- **Nach der Landung:** ein Tank- und ein Munitionswagen kommen von einem freien Platz in der Nähe und bleiben 4 Minuten. Sie sind nur Kulisse. Tanken und Aufmunitionieren laufen weiter über die DCS-Bodencrew (`\` → F8), ein Texthinweis erinnert daran.
- **Losrollen:** die Soldaten treten zurück, die Wagen fahren weg, alles verschwindet.
- **Absturz oder schwer beschädigte Landung:** ein Feuerwehrwagen kommt und fährt nach 5 Minuten wieder.

| Wann | Crew Chief (Bordsprechanlage) |
|---|---|
| Geparkt vor dem Flug | *„Crew chief on the headset, ready when you are.“* |
| Geparkt nach der Landung | *„Welcome back! Chocks in, clear to shut down.“* |
| Wagen kommen | *„Fuel and ammo trucks standing by.“* |
| Ground gibt Anlassen frei | *„Clear on the left, clear on the right, fire guard posted. Cleared to start engines.“* |
| Losrollen | *„Chocks removed, pins pulled. You're clear of the area, have a good flight!“* |

Die Stimme des Crew Chiefs steht in `config.jsonc` → `"Voices"` → `"Crew"` (Standard `"en_US-ryan-medium"`).

### Kurzübersicht: Sprüche

| Station | Sagen | Ergebnis |
|---|---|---|
| AWACS | checking in · picture · bogey dope · declare [bullseye 030 45] · spiked 270 · request sort · committing north group · splash one · request nearest tanker · bingo, vectors to Kutaisi · mayday · checking out · Ford one, say status (KI-Flug) | Check-in mit Picture · Picture · nächste Gruppe BRAA · Identifizierung · Quelle des Spikes · Ziele je Element · Bestätigung · copy · Tankerposition und TACAN · Peilung und Entfernung zum Platz · nächster Platz und Übergabe · keine Rufe mehr von sich aus · der KI-Flug meldet seinen Status |
| Tanker | request rejoin · visual · observation · pre-contact · disconnect · right wing · refuel complete | Treffpunkt · cleared to join / Nummer · cleared pre-contact · cleared contact · zum rechten Flügel · report complete or request more · total offload, cleared to depart |
| Range | checking in · in hot · in dry · off safe · winchester · checking out | Range-Briefing · cleared hot · cleared dry · Anflugergebnis · switches safe · Zusammenfassung und Abflug |

Die Einstellungen dieser Stationen (`AwacsBullseye`, `AwacsNewGroupNm`, `TankerVectors`, `CommsKey`, Stimmen) stehen in der [config.jsonc-Referenz](#configjsonc-referenz).

## Multiplayer, Dedicated Server, eigene Frequenzen und andere Karten

### So funktioniert Multiplayer

DCS-ATC läuft auf **einem** PC je Sitzung: dem PC dessen, der die Mission hostet. Dort laufen alle Lotsen: Ground, Tower, Approach, ATIS, AWACS, Tanker, Range und Träger. Alle Spieler der Mission, Mensch oder KI, sind Verkehr für diese Lotsen.

Wer Host und wer Mitspieler ist, entscheidet sich beim Missionsstart automatisch:

| Du bist … | DCS-ATC läuft als | Was es tut |
|---|---|---|
| Einzelspieler | Host | Alles, für dich |
| Host einer Multiplayer-Mission aus dem Spiel | Host | Alles, für dich und alle anderen |
| Mitspieler auf fremdem Server, mit installiertem DCS-ATC | Client | Nur Funkrad. Deine Anfragen gehen an den Host. |
| Betreiber eines Dedicated Servers (`DCS_server.exe`) | Host, im Hintergrund | Alles, für alle Spieler. Am Server-PC sitzt niemand. |

Dafür musst du nichts einstellen. DCS-ATC startet mit jeder Mission und wählt seine Rolle.

### Host-Modus: jeder Spieler hat seine eigene Lotsenfolge

Als Host behandelt DCS-ATC jeden menschlichen Spieler der Mission getrennt:

- **Jeder hat seine eigene Folge.** Einer rollt, ein anderer ist unter Radarführung, ein dritter im Overhead. Jeder spricht mit den Lotsen des Platzes, dessen Frequenz er in SRS gerastet hat. Ohne gerastete Platzfrequenz mit dem nächsten Platz.
- **Spieler sind füreinander Verkehr.** Verkehrshinweise, Rollkonflikte („give way to the FA-18C on your right“), Landereihenfolge und Abstände in der Platzrunde berücksichtigen andere Spieler und die KI der Mission.
- **Rotten bilden sich automatisch.** Spieler derselben DCS-Gruppe innerhalb 1 NM zueinander fliegen als eine Rotte mit der Folge des Leads. Lead ist der Spieler mit dem niedrigsten Rufzeichen. Ground, Tower und Approach behandeln die Rotte dann als Einheit. Ein Rottenflieger, der sich mehr als 3 NM entfernt, bekommt wieder seine eigene Folge.
- **Rufzeichen kommen aus dem DCS-Slot jedes Spielers** (aus `Enfield11` wird „Enfield 1-1“). „MyCallsign“ in den Einstellungen ändert nur das Rufzeichen des Hosts. Sitzen mehrere Spieler in Slots ohne westliches Rufzeichen, bekommt der erste „Callsign“ aus config.jsonc und die anderen werden mit ihrem Spielernamen gerufen.
- **Textmeldungen gehen an die eigene Gruppe jedes Spielers**, du siehst also nur Lotsentext für deine Gruppe (und nur, wenn du auf die Frequenz gerastet bist). Die Stimme geht über SRS auf der Lotsenfrequenz, du hörst also genau, was du auf dieser Frequenz in echt hören würdest, auch die Sprüche an andere Spieler.
- **Lotsen arbeiten nur für ihre Seite.** Ein Platz der Gegenseite hat für dich keinen Lotsen.

Zwei Voraussetzungen:

1. **Alle nutzen den SRS-Server des Hosts.** DCS-ATC startet den SRS-Server auf dem Host-PC selbst und hört lokal mit. Jeder Spieler verbindet seinen SRS-Client mit diesem Server über die IP des Hosts und den SRS-Port (Standard 5002).
2. **Nur das DCS-ATC des Hosts steuert.** Einstellungen in config.jsonc, die das Lotsenverhalten ändern, etwa Frequenzen, Stimmen und AWACS-Optionen, und die eigenen Platzfrequenzen in `frequencies.jsonc` gelten vom Host-PC.

Auch im Multiplayer spawnt DCS-ATC keine Flugzeuge. Es übernimmt nur den Funk für Einheiten der Mission, auch für AWACS, Tanker und KI-Flüge, die die Mission später spawnt.

### Client-Modus: Mitspieler mit eigenem DCS-ATC

Trittst du einer fremden Multiplayer-Mission bei und hast DCS-ATC installiert, erkennt es das beim Missionsstart und wechselt in den Client-Modus. Ein kleines Fenster zeigt:

```
DCS-ATC Mitspieler: Funkrad mit Taste ' (Apostroph, rechts neben Ö/;) (DCS vorn). Anfragen gehen per DCS an den Host.
Sprechen: einfach über SRS auf der Lotsen-Frequenz. Fenster offen lassen.
```

Im Client-Modus:

- **Das Funkrad geht.** Öffnen mit deiner Rad-Taste (Standard `'`, der Apostroph; DCS muss das aktive Fenster sein). Wählen mit `1`–`9`, zurück mit `Rücktaste`, schließen mit `Esc`. Jede Anfrage geht als DCS-Chatnachricht mit `ATC>` an das DCS-ATC des Hosts. Der Host fängt sie ab, niemand sieht sie im Chat. Der Host antwortet, als hättest du selbst gefunkt, über SRS und als Text an deine Gruppe.
- **Sprechen geht wie gewohnt.** SRS-Sprechtaste auf der Lotsenfrequenz drücken und sprechen.
- **Was Mitspieler nicht bekommen.** Dein lokales DCS-ATC startet kein SRS, macht keine Spracherkennung und betreibt keine Lotsen. Der ENTER-Vorschlag in der Radmitte geht nur beim Host, weil der Mitspieler keinen Flugzustand hat. Das Mitspieler-Rad zeigt keine Frequenzen, weil nur der Host sie kennt (Karte und `frequencies.jsonc`). Auch den Rad-Text im Spiel (VR) bekommt der Mitspieler nicht; den schreibt das DCS-ATC des Hosts nur für sein eigenes Flugzeug.
- **Der Host muss DCS-ATC laufen haben.** Sonst fängt niemand deine `ATC>`-Anfragen ab und nichts antwortet.

### Spieler ohne DCS-ATC

Spieler müssen DCS-ATC gar nicht installieren. Läuft es beim Host (oder auf dem Dedicated Server), kann jeder Spieler der Mission es so nutzen:

- **Sprechen über SRS.** Mit dem SRS-Server des Hosts verbinden, Lotsenfrequenz rasten und auf Englisch sprechen. Beispiel:
  > "Kutaisi Ground, Enfield 1-2, request startup"
- **F10-Menü.** `F10 -> ATC` hat die meisten Anfragen des Funkrads (Ground, Tower, Approach, Allgemein, Notfall …). Jeder Spieler bekommt sein eigenes `ATC`-Menü. Teilen sich mehrere Spieler eine Gruppe, hat das `ATC`-Menü je Spieler ein Untermenü mit seinem Rufzeichen.
- **Lotsentext** erscheint im Spiel für deine Gruppe, die Stimme kommt über SRS.

Funkrad und ENTER-Vorschlag bekommen sie nicht. Ihre F10-Anfragen spricht trotzdem die Pilotenstimme, wie bei allen anderen.

Läuft DCS-ATC beim Host nicht, antwortet das F10-Menü:

> DCS-ATC läuft nicht – Startmenü -> DCS-ATC starten, dann erneut senden.

### Dedicated Server einrichten

DCS-ATC kann auf dem PC eines DCS Dedicated Servers (`DCS_server.exe`) laufen, ohne dass dort ein Spieler sitzt.

> **Noch nicht auf einem echten Dedicated Server getestet.** Setup und Hintergrundbetrieb sind eingebaut, aber noch von niemandem auf einem laufenden Server bestätigt. Bitte melde, wie es bei dir läuft.

**Vor der Installation**

1. DCS Dedicated Server installieren und **einmal** starten, damit er seinen Saved-Games-Ordner anlegt: `Saved Games\DCS.server`, `DCS.dcs_serverrelease`, `DCS.release_server`, `DCS.openbeta_server` oder der Name nach `-w <Name>`.
2. SRS auf **demselben PC** installieren. DCS-ATC startet den SRS-Server, wenn er nicht schon läuft. Ein Kommandozeilen-SRS-Server, der schon auf seinem Port lauscht, zählt als laufend.
3. DCS-ATC **unter demselben Windows-Benutzer installieren, der `DCS_server.exe` ausführt**. DCS-ATC und der Server tauschen Daten über Temp-Ordner, Einstellungen und Registry dieses Benutzers aus. Unter verschiedenen Benutzern sehen sie sich nicht.

**Setup**

4. `DCS-ATC-Setup.exe` starten. Auf der Aufgabenseite **„Dedicated Server“** ankreuzen:
   - Auf einem PC **nur** mit Dedicated Server (kein `Saved Games\DCS`) ist das Kästchen schon angekreuzt, und Setup installiert nur für den Server.
   - Auf einem PC, der auch das Spiel hat, selbst ankreuzen. Die Spielinstallation wird wie gewohnt mit eingerichtet.
5. Den **Saved-Games-Ordner** des Servers bestätigen. Startest du den Server mit `-w <Name>`, `Saved Games\<Name>` wählen.
6. Findet Setup den Installationsordner des Servers nicht (den mit `bin\DCS_server.exe`), fragt es danach.
7. Setup legt Hook und Missionsskript in den Saved-Games-Ordner des Servers, unter `Scripts\Hooks` und `Scripts\DcsAtc`, und fügt eine Zeile in `Scripts\MissionScripting.lua` des Servers ein, die nur das DCS-ATC-Skript lädt. Die Tastenabfrage für Rad und Sprechtaste entfällt, wenn kein Spiel installiert ist.

**Betrieb**

8. Server starten und eine Mission laden. DCS-ATC startet mit jeder Servermission minimiert und läuft im Hintergrund:
   - kein Funkrad, kein Overlay, keine Tasten, kein Mikrofon und kein SRS-Client (nur der SRS-Server)
   - kein eigenes Flugzeug: alle Spieler sind Verkehr
   - die Lotsen hören die Spieler über SRS und bekommen ihre Positionen aus der Mission
   - Text geht an die Gruppe jedes Spielers im Spiel
9. Spieler verbinden SRS mit dem SRS-Server auf dem Server-PC und nutzen DCS-ATC wie oben: Stimme, F10-Menü und, wer DCS-ATC installiert hat, das Funkrad (Client-Modus).

Der Hintergrundbetrieb schaltet sich automatisch ein, wenn `DCS_server.exe` **ohne** `DCS.exe` auf demselben PC läuft. Läuft auf dem Server-PC auch das Spiel, kannst du ihn in config.jsonc erzwingen:

```jsonc
"Dedicated": true,   // Standard false
```

Auf einem Dedicated Server gelten diese Dateien für alle Spieler:

- `%LOCALAPPDATA%\Programs\DCS-ATC\config.jsonc` auf dem Server-PC: Frequenzen, Stimmen, AWACS-Optionen
- `%LOCALAPPDATA%\Programs\DCS-ATC\frequencies.jsonc` auf dem Server-PC: eigene Platzfrequenzen
- `<Server-Saved-Games>\Scripts\DcsAtc\DcsAtcOptions.lua`: Standard-Missionsoptionen (Bodenpersonal)

**Nach einem DCS-Server-Update**

DCS setzt `MissionScripting.lua` zurück. Reparieren mit **Startmenü -> DCS-ATC -> „Nach DCS-Update reparieren“**. Das repariert auch die im Setup gewählte Serverinstallation.

**Einschränkungen**

- Nicht auf einem echten Dedicated Server getestet (siehe oben).
- Der SRS-Server muss auf demselben PC wie DCS-ATC laufen. DCS-ATC verbindet sich lokal und kann keinen SRS-Server auf einem anderen Rechner nutzen.
- Auf dem Server ist niemand „du“. Einstellungen, die nur das eigene Flugzeug des Hosts betreffen, wirken nicht, etwa „MyCallsign“, die DCS-Funkmenü-Taste und die Bahnbefeuerung über das Menü.

### Eigene Frequenzen je Platz

Standardmäßig nutzt jeder Platz die Frequenz, die DCS ihm auf der Karte gibt, die auf der F10-Karte. Ground, Tower und Approach teilen sie sich, auf UHF und VHF. In der Datei **`frequencies.jsonc`** im DCS-ATC-Ordner (`%LOCALAPPDATA%\Programs\DCS-ATC\frequencies.jsonc`, neben `config.jsonc`) kannst du einem Platz für jeden Lotsen eine eigene Frequenz geben.

**Die Datei**

- **DCS-ATC legt sie selbst an**, wenn es die Plätze lädt (beim ersten Start mit einer Mission und bei jedem Kartenwechsel). Setup installiert sie nicht, und beim Deinstallieren bleibt sie wie `config.jsonc` erhalten. Auf einem Server kannst du sie auch ohne laufendes DCS mit `DcsAtc.exe --frequencies` anlegen oder ergänzen.
- **Sie enthält schon jeden Platz jeder installierten Karte**, je Karte ein Abschnitt mit einer auskommentierten Zeile je Platz, die zeigt, was DCS-ATC heute nutzt (DCS-Karte, ATIS und config.jsonc):

  ```jsonc
  {
    // ===== Caucasus =====
    // "Kutaisi": { "ATIS": 263.5, "Ground": [263.0, 134.0], "Tower": [263.0, 134.0], "Departure": [263.0, 134.0], "Approach": [263.0, 134.0] },   // id …
    // ===== Nevada =====
    "Nellis": { "ATIS": 270.1, "Ground": 275.8, "Tower": 327.0, "Departure": 273.55, "Approach": 379.0 },   // id …
  }
  ```

  Für eigene Frequenzen die `//` vor einer Platzzeile entfernen und die Zahlen ändern, wie oben bei Nellis. Den Kommentar `// id …` am Zeilenende stehen lassen; daran erkennt DCS-ATC den Platz, wenn es neue ergänzt.
- **Plätze mit nur einer VHF-Frequenz** in DCS (fast die ganze Karte Persischer Golf) stehen mit einer Liste aus einem Wert da, etwa `"Tower": [119.2]`. Plätze ganz ohne DCS-Frequenz zeigen die Frequenzen aus `config.jsonc` (Standard `"Ground": 264.5, "Tower": 265.0, "Departure": 266.5, "Approach": 266.5`).
- **Neue Karten und Plätze** werden als auskommentierte Zeilen ergänzt. Deine eigenen Zeilen bleiben unverändert.

**Regeln**

- **Lotsen:** `ATIS`, `Ground`, `Tower`, `Departure`, `Approach`. Groß-/Kleinschreibung egal.
- **Werte:** eine Frequenz in MHz oder eine Liste wie `"Ground": [275.8, 121.8]` (UHF und VHF). Jeder Wert muss zwischen 30 und 400 MHz liegen.
- **Platzname:** wie in DCS. Groß-/Kleinschreibung, Bindestriche, Leerzeichen und Satzzeichen sind egal, `"senaki kolkhi"` passt also zu `Senaki-Kolkhi`. Ein eindeutiger Teil des Namens reicht. Passt der Teil zu mehreren Plätzen, wird der Eintrag ignoriert.
- **Kartenabschnitte:** Zeilen unter `// ===== <Karte> =====` gelten nur auf dieser Karte. Zeilen vor dem ersten Abschnitt gelten auf jeder Karte.
- **Nicht genannte Lotsen bleiben auf der Kartenfrequenz.** Ohne Departure übernimmt Approach die Abflüge wie bisher.
- **Änderungen gelten sofort nach dem Speichern** der Datei, ohne Neustart.
- **Ungültige Einträge werden ignoriert**, jeder mit einer Zeile in `atc-log.txt`, zum Beispiel:
  ```
  [ATC] frequencies.jsonc: Platz "Groom Lake" nicht auf dieser Karte – ignoriert
  [ATC] frequencies.jsonc: Nellis "Clearance": … – unbekannter Lotse (ATIS, Ground, Tower, Departure, Approach), ignoriert
  ```
  Ist die Datei gar kein gültiges JSON, gelten keine eigenen Frequenzen, und `atc-log.txt` meldet `frequencies.jsonc: Fehler (…) – keine eigenen Frequenzen`.
- **Dieselbe Frequenz an zwei Plätzen:** wird eine deiner Lotsenfrequenzen auch an einem anderen Platz genutzt (als eigene oder als Kartenfrequenz), warnt `atc-log.txt`, etwa `360.6 MHz bei Nellis und Creech – ein Ruf darauf erreicht nur Nellis, Creech auf dieser Frequenz nicht erreichbar`.

**Was sich mit eigenen Frequenzen ändert**

- **Ein Ruf auf einer Frequenz erreicht diesen Lotsen.** Ein Ruf auf der Ground-Frequenz geht an Ground, einer auf der Tower-Frequenz an Tower, egal was du sagst. Auf einer gemeinsamen Kartenfrequenz bestimmt DCS-ATC den Lotsen weiter aus dem Inhalt.
- **Übergaben nennen die richtige Frequenz.** Die Rollfreigabe von Ground endet etwa mit „Hold short runway zero three right, contact Nellis Tower three two seven decimal zero when ready for departure.“ Auch Lotsen, die dir sagen, dass du die falsche Station gerufen hast, nennen die richtige Frequenz.
- **Departure wird ein eigener Lotse.** Nach dem Start sagt Tower „…contact Nellis Departure two seven three decimal five five“. Departure betreut dich dann auf 273.55, gibt „radar contact“ und entlässt dich am Rand seines Luftraums. Departure spricht mit der Approach-Stimme. Eine IFR-Freigabe nennt „departure frequency two seven three decimal five five“.
- **ATIS** sendet auf eigener Frequenz und nennt die Tower-Frequenz.
- **Funkrad, Kneeboard und SRS-Mithören** nutzen die neuen Frequenzen. Im Abflug zeigt das Rad die Departure-Frequenz, und der ENTER-Vorschlag heißt dann `Departure: …`.

Beispiel in Nellis mit der Datei oben:

```
(auf 275.8)  Nellis Ground, Enfield 1-1, radio check
             Enfield one one, Nellis Ground, read you five.          ← Ground antwortet auf 275.8
             …
             Ground: … Hold short runway zero three right, contact Nellis Tower three two seven decimal zero when ready for departure.
(auf 327.0)  Nellis Tower, Enfield 1-1, holding short runway …, ready for departure
             …
             Tower: … contact Nellis Departure two seven three decimal five five
(auf 273.55) Nellis Departure, Enfield 1-1, airborne …
             Enfield one one, Nellis Departure, radar contact …
```

**Im Multiplayer und auf einem Dedicated Server** zählt nur die `frequencies.jsonc` des Hosts (bzw. Servers), und sie gilt für alle Spieler. Die Datei eines Mitspielers wirkt nicht auf die Lotsen.

**Die eingebaute DCS-Flugsicherung** kennt eigene Frequenzen nicht. DCS-ATC bringt sie an jedem Platz zum Schweigen, es betrifft also nur Funktionen, die auf DCS selbst bauen: die Anforderung der Bahnbefeuerung prüft weiter, ob du auf die Kartenfrequenz des Platzes gerastet bist.

Die älteren Einstellungen „Frequencies“ (gemeinsame Lotsenfrequenzen) und „AtisFreqs“ (eine ATIS-Frequenz je Platz, braucht Neustart) in config.jsonc gelten weiter. Für einen einzelnen Platz hat ein Eintrag in `frequencies.jsonc` Vorrang vor beiden.

### Andere Karten als Kaukasus

DCS-ATC läuft auf jeder DCS-Karte. Es hat keine eingebaute Platzliste, sondern liest beim Missionsstart aus der Mission und deiner DCS-Installation:

- **Plätze und Bahnen:** jeder DCS-Platz der Karte mit Bahnen. FARPs und Hubschrauberlandeplätze nicht, ebenso keine Plätze, für die DCS keine Bahnen meldet. Parkplätze und Seite kommen auch aus DCS.
- **Frequenzen und Stationsnamen:** aus den Funkdaten der Karte in deiner DCS-Installation, dieselben Frequenzen wie auf der F10-Karte.
- **ILS, TACAN, VOR und NDB:** aus den Funkfeuerdaten der Karte in deiner DCS-Installation.
- **Gelände, Missweisung und Ortszeit** (für die Zulu-Zeit in der ATIS): aus DCS und der Karte. Auf Karten, die DCS-ATC nicht kennt, hat die ATIS keine Uhrzeit.

DCS-ATC fügt auf keiner Karte eigenen Verkehr hinzu.

#### Den genauen Platznamen finden

Die erzeugten Zeilen in `frequencies.jsonc` enthalten die Platznamen schon. Sonst zeigen sie:

- **Das DCS-ATC-Fenster** (Taskleiste) listet beim Missionsstart alle Plätze, etwa:
  ```
  Airfields (12): Kutaisi, Nellis, North Las Vegas, Creech, …
  ```
- **`airfields.txt`** in `%LOCALAPPDATA%\Programs\DCS-ATC` enthält die Plätze der zuletzt geflogenen Karte. Jeder Platz ist eine Zeile mit `A;` am Anfang, etwa `A;Anapa-Vityazevo;…`. Das zweite Feld ist der genaue Name.
- **Das Funkrad:** Allgemein -> „Platz wählen“ listet die 8 nächsten Plätze.
- **Das F10-Menü:** `ATC -> Ground -> IFR clearance` listet bis zu 8 Plätze deiner Seite mit Namen.
- **Die F10-Karte** zeigt die DCS-Platznamen.
- **atc-log.txt:** passt ein Name in `frequencies.jsonc` zu mehreren Plätzen, nennt die Logzeile die getroffenen Plätze.

#### Einschränkungen auf anderen Karten

- **Nur Kaukasus ist getestet.** Jede andere Karte sollte gehen, aber rechne mit Ecken und Kanten und melde sie bitte.
- **Detaillierte Verfahren gibt es nur für Kutaisi.** Meldepunkte, Rollwege mit Namen („taxi to holding point runway two five via November, Delta“) sowie An- und Abflugwege aus der Karte. Jeder andere Platz auf jeder Karte ist generisch:
  - Rollfreigaben nennen die Bahn, aber keine Rollwege, etwa „taxi to holding point runway one three“.
  - Die Kontrollzone ist 5 NM um den Platz, bis 3000 ft darüber.
  - Die Platzrunde liegt links, außer das Gelände erzwingt rechts.
- **Stationsnamen klingen auf manchen Karten seltsam.** Auf Karten wie dem Persischen Golf haben die DCS-Funkdaten nur ICAO-Codes oder zusammengeschriebene Namen. DCS-ATC bildet den Stationsnamen dann aus dem DCS-Platznamen, etwa „Al Dhafra AB Tower“. Die Spracherkennung erwartet, dass du auch diesen Namen sagst.
- **Spracherkennung und ungewöhnliche Namen:** lange oder nicht-englische Platznamen werden schlechter erkannt. Im Zweifel Funkrad oder F10-Menü nehmen.
- **Deine DCS-Installation muss gefunden werden.** Frequenzen, Stationsnamen und Funkfeuer kommen daher. Findet DCS-ATC sie nicht, meldet `atc-log.txt` „DCS-Installation nicht gefunden: Frequenzen aus Ersatzdaten“, und es gelten die gemeinsamen Ersatzfrequenzen aus config.jsonc.

## Anhang: Einstellungen, Logs und Fehlersuche

### Das Einstellungsfenster

Öffnen über **Startmenü → DCS-ATC → Einstellungen** oder **Funkrad → Allgemein → Einstellungen**. **Speichern** schreibt deine Änderungen in `config.jsonc` und behält die Kommentare der Datei. Im Fenster speichert `Enter`, `Esc` bricht ab. Mit `*` markierte Felder gelten ab dem nächsten Start von DCS-ATC, alle anderen schon während es läuft.

| Feld | Werte | config.jsonc-Schlüssel |
|---|---|---|
| **Eigenes Rufzeichen** | Leer = Rufzeichen aus dem DCS-Slot. Sonst Name und zwei Ziffern, etwa `Viper 1-1` (`viper11` wird angenommen und vereinheitlicht). | `MyCallsign` |
| **Sprache der Oberfläche** `*` | Deutsch / English. Schaltet auch die Sprache des F10-Menüs um. | (Sprachdatei) |
| **Lautstärke Lotsen** | 0–100 %, in 5er-Schritten | `Volume` |
| **Sprechtempo** (1 = normal, größer = langsamer) | 0,70–1,50 | `SpeechRate` |
| **Pause zwischen Funksprüchen** | 0–5 s | `ReplyPause` |
| **Funkklang (Rauschen, Squelch)** | an/aus | `RadioFx` |
| **Eigene Anfragen vorsprechen (Pilotenstimme)** | an/aus. Aus setzt die Pilotenstimme auf leer. | `PilotVoice` |
| **AWACS-Picture als Bullseye (statt BRAA)** | an/aus | `AwacsBullseye` |
| **AWACS meldet neue Gruppen bis** | 40–200 NM | `AwacsNewGroupNm` |
| **KI-Verkehr funkt hörbar mit** | an/aus | `AiChatter` |
| **KI-Flüge im Gefecht** | voll / taktisch / aus | `AiFlightComms` |
| **ATIS-Dauersendung** | an/aus | `Atis` |
| **Luftraum überwachen (Kontrollzonen, Verstöße, Debriefing)** | an/aus | `AirspaceWatch` |
| **Debug-Log für Fehlerberichte (atc-debug.txt)** | an/aus | `DebugLog` |
| **Bahnbefeuerung (nachts)** | stumm / mit DCS-Lotse (hörbar) / aus. Gilt ab dem nächsten Start. | `RunwayLights` |
| **Höhenmesser-Einstellung** | automatisch (nach Flugzeugtyp) / hPa (QNH) / inHg (altimeter) | `AltimeterUnit` |
| **Funkrad im Spiel (VR)** | automatisch (wenn DCS im VR-Modus) / immer / aus | `WheelInGame` |
| **Mikrofon (nur ohne SRS)** `*` | Windows-Standard oder ein bestimmtes Gerät | `MicName` |
| **SRS-Ordner** `*` | Der Pfad, mit „…“ zum Durchsuchen. Geprüft wird auf `ExternalAudio\DCS-SR-ExternalAudio.exe`. | `SrsPath` |
| **Tasten festlegen …** | Öffnet die Tastenabfrage (Funkrad, HOTAS, Sprechtaste). | `WheelKey`, `PttKey`, … |

**Alle Optionen (config.jsonc) …** öffnet die Datei im Editor.

### config.jsonc-Referenz

Die Datei liegt im Installationsordner: `%LOCALAPPDATA%\Programs\DCS-ATC\config.jsonc`.

- **Format.** JSON, `//`-Kommentare erlaubt.
- **Updates.** Setup überschreibt eine vorhandene `config.jsonc` nie, deine Werte überleben Updates.
- **Fehlende Schlüssel** nehmen die Standardwerte unten.
- **Wann Änderungen gelten.** DCS-ATC merkt, wenn die Datei im laufenden Betrieb gespeichert wird. Was das Einstellungsfenster nicht mit `*` markiert, gilt sofort (außer `RunwayLights`). Tasten, Joystick-Knöpfe, Mikrofon, SRS und die Frequenzen unter `Frequencies` gelten ab dem nächsten Start.

**Tasten und Funkrad**

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `PttKey` | `163` (rechte Strg) | Sprechtaste als Windows-Virtual-Key-Code, etwa 165 = rechte Alt, 161 = rechte Umschalt, 112–123 = F1–F12. Nur ohne SRS-Verbindung. |
| `PttJoystick` | `-1` (aus) | Joystick-ID für die Sprechtaste. Herausfinden mit `DcsAtc.exe --buttons` oder **Tasten festlegen**. |
| `PttButton` | `1` | Knopfnummer an diesem Joystick. |
| `WheelKey` | `222` (`'` US / `Ä` deutsch) | Öffnet und schließt das Funkrad. `0` = keine Taste. |
| `WheelJoystick` | `-1` (aus) | Joystick-ID für den Rad-Knopf. Ist sie gesetzt, bedient der Hat das Rad. |
| `WheelButton` | `1` | Knopfnummer, die das Rad öffnet. |
| `WheelSize` | `0.45` | Radgröße als Anteil der DCS-Fensterhöhe (0,25–0,95). `+`/`-` im offenen Rad ändern und speichern sie. |
| `WheelInGame` | `"auto"` | Rad zusätzlich als Text im Spiel: `"auto"`, wenn DCS in VR läuft (VR-Option oder `--force_enable_VR`), `"on"`, `"off"`. |
| `CommsKey` | `43` (`\` US / `#` deutsch) | Deine DCS-Taste für das **Kommunikationsmenü** als Scancode. DCS-ATC drückt sie, um das DCS-Tankermenü (Korb/Boom) zu öffnen und in Dämmerung, Nacht oder schlechter Sicht die Bahnbefeuerung anzufordern. `0` = aus. |
| `MicName` | `""` | Teil des Mikrofonnamens, etwa `"FDUCE"`. Leer = Windows-Standard. Nur ohne SRS. |

**Rufzeichen und Modus**

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `Callsign` | `"Enfield 1-1"` | Nur ohne Missionsdaten oder für einen Slot ohne westliches Rufzeichen. Normalerweise kommt das Rufzeichen aus deinem DCS-Slot. |
| `MyCallsign` | `""` | Eigenes Rufzeichen statt des Slot-Rufzeichens. `""` = aus DCS. |
| `Mode` | `"host"` | `"client"` = nur Funkrad, Anfragen gehen über DCS an den Host. DCS-ATC schaltet von selbst in den Client-Modus, wenn du einem fremden Server beitrittst. |
| `Dedicated` | `false` | Dedicated Server ohne jemanden am PC: kein Rad, keine Tasten, kein Mikrofon, kein SRS-Client. Schaltet sich automatisch ein, wenn `DCS_server.exe` ohne `DCS.exe` läuft. |

**Frequenzen und SRS**

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `Frequencies` | `{ "ATIS": 263.5, "Ground": 264.5, "Tower": 265.0, "Approach": 266.5, "Range": 267.5, "AWACS": 251.5, "Tanker": 255.5, "Carrier": 127.5 }` | Stationsfrequenzen in MHz. **ATIS** ist die gemeinsame ATIS-Frequenz. **Range** ist der Range-Lotse. **Tanker** ist der Tankerfunk von DCS-ATC. **AWACS** und **Carrier** gelten nur, wenn die Missionseinheit keine eigene Frequenz hat. **Ground / Tower / Approach** sind Ersatz für Plätze ohne DCS-Frequenzdaten; normalerweise nutzt jeder Platz seine Kartenfrequenz. DCS-ATC warnt im Fenster, wenn eine Stationsfrequenz gleich einer Platzfrequenz ist. |
| `AtisFreqs` | `{}` | Eigene ATIS-Frequenz je Platz, etwa `{ "Kutaisi": 262.0 }`. Nicht genannte Plätze nutzen `Frequencies.ATIS`. Gilt nach Neustart. |
| `Modulation` | `"AM"` | Modulation aller DCS-ATC-Sendungen. |
| `Coalition` | `2` | SRS-Seite (1 rot, 2 blau) für Stationen ohne eigene Seite. Plätze, AWACS, Tanker und KI nutzen ihre eigene Seite. |
| `SrsPath` | `"C:\\Program Files\\DCS-SimpleRadio-Standalone"` | SRS-Ordner. Stimmt er nicht, nimmt DCS-ATC den Registry-Eintrag oder das laufende SRS. |
| `SrsListen` | `true` | Sprüche aller Spieler über SRS hören. Mit SRS-Verbindung wird deine eigene Sprechtaste ignoriert. |
| `SrsAutoConnect` | `true` | Einen noch nicht verbundenen SRS-Client automatisch mit dem lokalen SRS-Server verbinden. |
| `TelemetryPort` | `18500` | Lokaler UDP-Port für die Daten deines Flugzeugs. Das Export-Skript sendet an 18500, also so lassen. |

Eigene Frequenzen je Platz und Lotse stehen nicht in `config.jsonc`, sondern in der eigenen Datei `frequencies.jsonc`; sie gelten sofort nach dem Speichern. Siehe [Eigene Frequenzen je Platz](#eigene-frequenzen-je-platz).

**Stimmen und Klang**

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `Voices` | Wie installiert: Tower `"en_US-ryan-medium\|en_US-ljspeech-medium"`, Ground `"en_US-bryce-medium"`, Approach `"en_US-ryan-medium@1.05"`, ATIS `"en_US-ryan-medium@0.94"`, Range `"en_US-ryan-medium@1.06"`, AWACS `"en_GB-alan-medium\|en_US-ljspeech-medium"`, Tanker `"en_US-bryce-medium"`, Crew `"en_US-ryan-medium"`. Carrier (automatisch ergänzt) `"en_US-ryan-medium@0.97\|en_GB-alan-medium@1.02"` | Piper-Stimme je Lotse aus dem Ordner `models`. `@0.9` = tiefer. Mehrere Stimmen mit `\|` getrennt: jeder Platz bzw. jede Station bekommt eine davon. Mehr Stimmen: huggingface.co/rhasspy/piper-voices. |
| `PilotVoice` | `"en_US-joe-medium"` | Spricht deine Rad- und F10-Anfragen vor der Antwort. `""` = aus. |
| `RadioFx` | `true` | Funkklang: Bandpass, leichtes Rauschen, Squelch-Ende. |
| `Volume` | `0.6` | Lautstärke der Lotsen in SRS (0–1). |
| `SpeechRate` | `0.85` | Sprechtempo: 1 = Piper-Standard, kleiner = schneller. |
| `ReplyPause` | `1.5` | Sekunden zwischen zwei Sprüchen auf derselben Frequenz. |

**Verkehr, AWACS, Tanker, Träger**

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `AiChatter` | `true` | KI-Flüge der Mission funken hörbar mit dem Platz (Anfrage, Antwort, Readback). |
| `AiFlightComms` | `"voll"` | KI-Flüge im Gefecht: `"voll"` (alles: Fox, Splash, Tally …), `"taktisch"` (nur fox, engaged, timeout, out, splash, trashed, bingo, defending, merged, down und Guard-Sprüche), `"aus"`. |
| `AiFlightRadiusNm` | `60` | KI-Gefechtsfunk nur von Flügen in diesem Abstand zu einem Spieler derselben Seite. |
| `AiOwnWingmen` | `false` | Auch deine eigenen KI-Rottenflieger funken. DCS vertont sie schon. |
| `AiFollowAwacs` | `true` | Eigene KI-Jäger fliegen die Gruppe an, auf die das AWACS sie ansetzt. Das AWACS weicht Bedrohungen aus. `false`: die KI fliegt ihre Mission, der Funk folgt ihr. |
| `AwacsBullseye` | `true` | Picture und Updates an alle im Bullseye-Format; `false` = alles BRAA von dir. |
| `AwacsNewGroupNm` | `200` | AWACS meldet neue Gruppen (und „picture clean“) bis zu dieser Entfernung, 40–200 NM. |
| `TankerVectors` | `true` | Der Tanker führt dich zum Join (Kurs, Entfernung, Höhe), bis du „visual“ meldest. `false` = nur die erste Peilung. |
| `OnSpeedAoa` | `{ "FA-18": 8.1 }` | LSO: On-Speed-Anstellwinkel in Grad je Typ-Präfix für „you're fast/slow“. Andere Typen bekommen keine Fahrt-Rufe. Werte aus der Logzeile `[LSO] AoA im Mittel` ableiten. |

**Platzverhalten und Logs**

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `Atis` | `true` | ATIS-Dauersendung für Plätze mit einem Spieler innerhalb 40 NM. |
| `AirspaceWatch` | `true` | Kontrollzonen überwachen: Einflug ohne Kontakt wird angesprochen, Verstöße wie ein Start ohne Freigabe werden angesagt („possible pilot deviation“) und fürs Debriefing vermerkt. `false` = nur Sicherheitswarnungen. |
| `RunwayLights` | `"silent"` | Bahnbefeuerung in Dämmerung, Nacht oder schlechter Sicht über das DCS-eigene ATC-Menü: `"silent"` (DCS-Lotse bleibt stumm), `"dcs"` (DCS-Lotse 15 s hörbar), `"off"`. |
| `AltimeterUnit` | `"auto"` | Höhenmessereinstellung in Sprüchen. `"auto"`: westliche Typen „altimeter two niner niner two“ (inHg), andere „QNH one zero one three“ (hPa); die ATIS nennt beides. Oder `"hpa"`, `"inhg"`. |
| `DebugLog` | `true` | `atc-debug.txt` schreiben. |

**Optionen je Mission.** Bodenpersonal (Tank-/Munitionswagen, Feuerwehr) ist eine Missionsoption, nicht Teil von `config.jsonc`:

- **Im Spiel:** F10 → ATC → Einstellungen.
- **Standard für alle Missionen:** `Saved Games\DCS\Scripts\DcsAtc\DcsAtcOptions.lua` (`crew = false`).
- **Andere Werte für eine Mission:** im Missionseditor einen Trigger „Mission Start“ → DO SCRIPT: `DCSATC_OPTIONS = { crew = true }`.

### Logdateien

Alle Logs liegen in `%LOCALAPPDATA%\Programs\DCS-ATC`:

| Datei | Inhalt |
|---|---|
| `atc-log.txt` | Alles, was das DCS-ATC-Fenster zeigt, mit Zeitstempel: Funksprüche, erkannte Sprache, Warnungen, ignorierte Config-Einträge. |
| `atc-debug.txt` | Ausführliches Log für Fehlerberichte: Spracherkennung, Platzwahl, Flugphasen, Flugzeugdaten alle 10 s. Nur mit `DebugLog`. Bei 20 MB wird sie zu `atc-debug.old.txt`, belegt also höchstens 2 × 20 MB. |
| `crash-log.txt` | Fehlerdetails bei einem Absturz von DCS-ATC. DCS-ATC startet sich dann neu, außer es ist in seiner ersten Minute abgestürzt. |

Für einen Fehlerbericht: Uhrzeit, Platz, kurze Beschreibung, `atc-log.txt` und `atc-debug.txt`.

### Fehlersuche

**Gar keine Antwort**
- **Ist das DCS-ATC-Fenster offen?** Es startet mit der Mission. Wenn nicht: Startmenü → DCS-ATC starten. Die Meldung „DCS ATC läuft bereits in einem anderen Fenster“ heißt, dass schon eine Kopie läuft.
- **Ist dein SRS-Client** mit dem SRS-Server auf dem PC mit DCS-ATC verbunden, und steht dein Funkgerät in AM auf der Lotsenfrequenz? Das Funkrad zeigt eine Station rot mit ✗, wenn keines deiner Funkgeräte darauf gerastet ist.
- **Wurde dein Spruch verstanden?** In `atc-log.txt` steht, was erkannt wurde.
  - Station, dann Rufzeichen, dann Anliegen sagen.
  - Nach dem Drücken der Sprechtaste kurz warten, dann sprechen.
  - Bei „say again“ wiederholen oder das Funkrad nehmen.
- **Text im Spiel, aber keine Stimme?** Zeigt das DCS-ATC-Fenster „(SRS-Server nicht erreichbar)“, läuft der SRS-Server nicht lokal oder auf einem anderen Port.
- **Kein F10 → ATC und falsches Rufzeichen?** DCS-ATC hat keine Missionsdaten, `MissionScripting.lua` ist also nicht angepasst. Siehe „Nach einem DCS-Update“ unten.

**Falsche Frequenz**
- **Platzanfrage auf einer Stationsfrequenz.** Fragst du auf der AWACS-, Tanker-, Range- oder Trägerfrequenz nach Rollen oder Landen, kommt ein Texthinweis statt einer Antwort, etwa „Du funkst auf AWACS 251.5. Kutaisi Ground: 263.0 / 134.0“.
- **Frequenz eines anderen Platzes.** Ein Ruf auf der Frequenz eines anderen Platzes geht an diesen Platz.
- **Einen Platz rufen, bei dem du nicht bist.** Funkrad → Allgemein → Platz wählen.
- **Frequenzkonflikt in deiner Config.** Ist eine Stationsfrequenz in `config.jsonc` gleich einer Platzfrequenz, warnt das DCS-ATC-Fenster beim Start („⚠ config.jsonc: … – Frequenz ändern“). Frequenz ändern, sonst gehen Rufe an den Platz.
- **Eigene Platzfrequenzen wirken nicht.** In `atc-log.txt` nach Zeilen mit `frequencies.jsonc` suchen (ignorierte Einträge, ungültiges JSON, Frequenz an zwei Plätzen).

**Easy Communication**
- Der Funk von DCS-ATC hängt nicht von der DCS-Option Easy Communication ab.
- Die Option zählt nur für die Bahnbefeuerung nachts. Mit Easy Communication aus antwortet DCS nur, wenn dein Funkgerät auf die Platzfrequenz gerastet ist, deshalb fordert DCS-ATC das Licht nur dann an.
- Geht das Licht nie an, prüfen, ob `CommsKey` deiner DCS-Taste für das Kommunikationsmenü entspricht.

**SRS nicht gefunden**
- **SRS startet nicht, oder das Fenster meldet „SRS-Server/SRS-Client nicht gefunden in …“:** den SRS-Ordner im Einstellungsfenster setzen. Er muss `ExternalAudio\DCS-SR-ExternalAudio.exe` enthalten. Jedes Laufwerk und jeder Ordner geht.
- **SRS-Client bleibt getrennt:** im Client `127.0.0.1` und den Server-Port (normalerweise 5002) eintragen und Connect drücken.

**Windows SmartScreen blockiert das Setup**
- Auf **Weitere Informationen**, dann **Trotzdem ausführen** klicken. Das Setup ist nicht signiert.

**Nach einem DCS-Update**
- **Warum es nicht mehr geht.** DCS-Updates setzen `Scripts\MissionScripting.lua` zurück.
- **Automatische Reparatur.** DCS-ATC prüft die Datei im Betrieb und fragt einmal nach Adminrechten („DCS-Update hat die DCS-ATC-Zeile in MissionScripting.lua entfernt -> Admin-Abfrage (UAC). Mission danach neu starten.“).
- **Von Hand.** Startmenü → DCS-ATC → **Nach DCS-Update reparieren**.
- **Dann die Mission neu starten.**

**Funkrad öffnet nicht**
- Es öffnet nur, wenn DCS das aktive Fenster ist.
- In VR das Textrad im Spiel nutzen (`WheelInGame`).
- Taste mit **Tasten festlegen** prüfen oder ändern.

**Antworten kommen langsam**
- Die Spracherkennung läuft auf der CPU und braucht ein paar Sekunden pro Spruch. Das Funkrad ist sofort da.
