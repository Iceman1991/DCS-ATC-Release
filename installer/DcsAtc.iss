; DCS-ATC Installer (Inno Setup 6). Bauen: installer\build.cmd -> dist\DCS-ATC-Setup.exe
#define AppVer "0.10.0-alpha"
#define Src ".."

[Setup]
AppId={{6B7E2C1A-4D2F-4E8B-9A11-DC5A7C0A7C01}
AppName=DCS-ATC
AppVersion={#AppVer}
AppPublisher=DCS-ATC
DefaultDirName={localappdata}\Programs\DCS-ATC
DefaultGroupName=DCS-ATC
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir={#Src}\dist
OutputBaseFilename=DCS-ATC-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayName=DCS-ATC
; The user picks the language at start (preselected: Windows language); it also sets the language of the app and the F10 menu (lang.txt, DcsAtcLang.txt)
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.IconStart=DCS-ATC starten
en.IconStart=Start DCS-ATC
de.IconKeys=Tasten festlegen (Funkrad, Push-to-Talk)
en.IconKeys=Set keys (radio wheel, push-to-talk)
de.IconConfig=Einstellungen (Rufzeichen, Lautstärke, Funk)
en.IconConfig=Settings (callsign, volume, radio)
de.IconOptions=Bodenpersonal (Standard)
en.IconOptions=Ground crew (default)
de.IconRepair=Nach DCS-Update reparieren
en.IconRepair=Repair after DCS update
de.IconReadme=Liesmich
en.IconReadme=Readme
de.IconUninstall=DCS-ATC deinstallieren
en.IconUninstall=Uninstall DCS-ATC
de.RunPatch=DCS: MissionScripting.lua anpassen (Admin-Abfrage) ...
en.RunPatch=DCS: patching MissionScripting.lua (admin prompt) ...
de.RunKeys=Tasten festlegen (Funkrad, Push-to-Talk – Tastatur oder HOTAS)
en.RunKeys=Set keys (radio wheel, push-to-talk – keyboard or HOTAS)
de.RunSettings=Einstellungen öffnen (Rufzeichen, Stimmen, Funk, VR, Höhenmesser)
en.RunSettings=Open settings (callsign, voices, radio, VR, altimeter)
de.NoDcs=Saved Games\DCS wurde nicht gefunden. Bitte deinen DCS-Ordner in Saved Games wählen (enthält Config und Scripts):
en.NoDcs=Saved Games\DCS was not found. Please select your DCS folder in Saved Games (contains Config and Scripts):
de.TaskServer=Dedicated Server (DCS_server.exe auf diesem PC): Hook und Missionsskript auch in dessen Saved-Games-Ordner, MissionScripting.lua des Servers anpassen
en.TaskServer=Dedicated server (DCS_server.exe on this PC): hook and mission script also into its Saved Games folder, patch the server's MissionScripting.lua
de.ServerSaved=Saved-Games-Ordner des Dedicated Servers (z. B. DCS.server, DCS.dcs_serverrelease oder der Name nach -w):
en.ServerSaved=Saved Games folder of the dedicated server (e.g. DCS.server, DCS.dcs_serverrelease or the name after -w):
de.ServerDcs=Installationsordner des Dedicated Servers (enthält bin\DCS_server.exe):
en.ServerDcs=Installation folder of the dedicated server (contains bin\DCS_server.exe):

[Files]
Source: "{#Src}\tmp\sc\DcsAtc.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\installer\config.jsonc"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#Src}\installer\LIESMICH.txt"; DestDir: "{app}"; Languages: de; Flags: ignoreversion isreadme
Source: "{#Src}\installer\README.txt"; DestDir: "{app}"; Languages: en; Flags: ignoreversion isreadme
Source: "{#Src}\installer\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#Src}\mod\DcsAtc-Patch.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\airfields.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\models\ggml-small.en.bin"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "{#Src}\models\en_US-ryan-medium.onnx*"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "{#Src}\models\en_US-joe-medium.onnx*"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "{#Src}\models\en_GB-alan-medium.onnx*"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "{#Src}\models\en_US-ljspeech-medium.onnx*"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "{#Src}\models\en_US-bryce-medium.onnx*"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "{#Src}\tools\piper\*"; DestDir: "{app}\tools\piper"; Flags: ignoreversion recursesubdirs
Source: "{#Src}\tools\whisper\Release\*"; DestDir: "{app}\tools\whisper\Release"; Flags: ignoreversion recursesubdirs
Source: "{#Src}\tmp\ffmpeg\ffmpeg.exe"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion
; DCS part: hook (autostart, radio wheel for other players) and mission script (if only a dedicated server is on this PC: only there)
Source: "{#Src}\mod\Scripts\Hooks\DcsAtcHook.lua"; DestDir: "{code:SavedGames}\Scripts\Hooks"; Check: HasClient; Flags: ignoreversion
Source: "{#Src}\mod\Scripts\DcsAtc\DcsAtcMission.lua"; DestDir: "{code:SavedGames}\Scripts\DcsAtc"; Check: HasClient; Flags: ignoreversion
Source: "{#Src}\mod\Scripts\DcsAtc\DcsAtcOptions.lua"; DestDir: "{code:SavedGames}\Scripts\DcsAtc"; Check: HasClient; Flags: onlyifdoesntexist uninsneveruninstall
; Own-aircraft telemetry (AoA for the LSO); Export.lua includes it via pcall
Source: "{#Src}\export\DcsAtcExport.lua"; DestDir: "{code:SavedGames}\Scripts"; Check: HasClient; Flags: ignoreversion
; Dedicated server: hook and mission script in its Saved Games (no Export.lua: no own aircraft)
Source: "{#Src}\mod\Scripts\Hooks\DcsAtcHook.lua"; DestDir: "{code:ServerSaved}\Scripts\Hooks"; Check: WantServer; Flags: ignoreversion
Source: "{#Src}\mod\Scripts\DcsAtc\DcsAtcMission.lua"; DestDir: "{code:ServerSaved}\Scripts\DcsAtc"; Check: WantServer; Flags: ignoreversion
Source: "{#Src}\mod\Scripts\DcsAtc\DcsAtcOptions.lua"; DestDir: "{code:ServerSaved}\Scripts\DcsAtc"; Check: WantServer; Flags: onlyifdoesntexist uninsneveruninstall

[Tasks]
Name: "server"; Description: "{cm:TaskServer}"; Flags: unchecked

[Registry]
; Location of Saved Games\DCS for the app (language switch), reinstall and uninstall
Root: HKCU; Subkey: "Software\DCS-ATC"; ValueType: string; ValueName: "SavedGames"; ValueData: "{code:SavedGames}"; Flags: uninsdeletekey
; Dedicated server: Saved Games (app, uninstall) and install folder (DcsAtc-Patch.ps1, also "Nach DCS-Update reparieren")
Root: HKCU; Subkey: "Software\DCS-ATC"; ValueType: string; ValueName: "ServerSavedGames"; ValueData: "{code:ServerSaved}"; Check: WantServer
Root: HKCU; Subkey: "Software\DCS-ATC"; ValueType: string; ValueName: "ServerDcs"; ValueData: "{code:ServerDcs}"; Check: WantServer

[Icons]
Name: "{group}\{cm:IconStart}"; Filename: "{app}\DcsAtc.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:IconKeys}"; Filename: "{app}\DcsAtc.exe"; Parameters: "--keys"; WorkingDir: "{app}"
Name: "{group}\{cm:IconConfig}"; Filename: "{app}\DcsAtc.exe"; Parameters: "--settings"; WorkingDir: "{app}"
Name: "{group}\{cm:IconOptions}"; Filename: "notepad.exe"; Parameters: """{code:SavedGames}\Scripts\DcsAtc\DcsAtcOptions.lua"""
Name: "{group}\{cm:IconRepair}"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\DcsAtc-Patch.ps1"""
Name: "{group}\{cm:IconReadme}"; Filename: "{app}\LIESMICH.txt"; Languages: de
Name: "{group}\{cm:IconReadme}"; Filename: "{app}\README.txt"; Languages: en
Name: "{group}\{cm:IconUninstall}"; Filename: "{uninstallexe}"

[InstallDelete]
; Language switch via reinstall: remove shortcuts of the other language
Type: files; Name: "{group}\*.lnk"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\DcsAtc-Patch.ps1"""; Verb: "runas"; Flags: shellexec waituntilterminated runhidden; StatusMsg: "{cm:RunPatch}"
Filename: "{app}\DcsAtc.exe"; Parameters: "--keys"; WorkingDir: "{app}"; Description: "{cm:RunKeys}"; Check: HasClient; Flags: postinstall waituntilterminated skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\DcsAtc-Patch.ps1"" -Remove"; Verb: "runas"; Flags: shellexec waituntilterminated runhidden; RunOnceId: "DcsAtcUnpatch"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tmp"
Type: files; Name: "{code:SavedGames}\Scripts\DcsAtc\DcsAtcPath.txt"
Type: files; Name: "{code:SavedGames}\Scripts\DcsAtc\DcsAtcLang.txt"
Type: files; Name: "{code:ServerSaved}\Scripts\DcsAtc\DcsAtcPath.txt"
Type: files; Name: "{code:ServerSaved}\Scripts\DcsAtc\DcsAtcLang.txt"
Type: files; Name: "{app}\lang.txt"

[Code]
const ExportLine = 'pcall(function() local l=require(''lfs''); dofile(l.writedir()..[[Scripts\DcsAtcExport.lua]]) end)   -- DCS-ATC';
var Saved, ServerSave, ServerDir: String;
    ServerOnly, TaskSet: Boolean;   { ServerOnly: no Saved Games\DCS but a dedicated server -> install there only }

{ Saved Games\DCS (bzw. DCS.openbeta, falls nur das existiert): früher gewählter Ordner (HKCU\Software\DCS-ATC, liest auch die App),
  sonst der echte Ort von Saved Games (Konstante usersavedgames = Known Folder, in Windows verschiebbar) }
function SavedGames(Param: String): String;
var Base: String;
begin
  if Saved = '' then
    if not (RegQueryStringValue(HKCU, 'Software\DCS-ATC', 'SavedGames', Saved) and DirExists(Saved)) then begin
      Base := ExpandConstant('{usersavedgames}');
      if not DirExists(Base + '\DCS') and DirExists(Base + '\DCS.openbeta') then Saved := Base + '\DCS.openbeta'
      else Saved := Base + '\DCS';
    end;
  Result := Saved;
end;

{ Dedicated server: previously chosen folder, else DCS.server or (newer server installers) DCS.dcs_serverrelease, DCS.release_server, DCS.openbeta_server }
function ServerSaved(Param: String): String;
var Base: String;
begin
  if ServerSave = '' then
    if not (RegQueryStringValue(HKCU, 'Software\DCS-ATC', 'ServerSavedGames', ServerSave) and DirExists(ServerSave)) then begin
      Base := ExpandConstant('{usersavedgames}');
      if DirExists(Base + '\DCS.server') then ServerSave := Base + '\DCS.server'
      else if DirExists(Base + '\DCS.dcs_serverrelease') then ServerSave := Base + '\DCS.dcs_serverrelease'
      else if DirExists(Base + '\DCS.release_server') then ServerSave := Base + '\DCS.release_server'
      else if DirExists(Base + '\DCS.openbeta_server') then ServerSave := Base + '\DCS.openbeta_server'
      else ServerSave := Base + '\DCS.server';
    end;
  Result := ServerSave;
end;

{ Dedicated server install folder (MissionScripting.lua): previously chosen, registry, else picked on the tasks page }
function ServerDcs(Param: String): String;
begin
  if ServerDir = '' then RegQueryStringValue(HKCU, 'Software\DCS-ATC', 'ServerDcs', ServerDir);
  if ServerDir = '' then RegQueryStringValue(HKCU, 'Software\Eagle Dynamics\DCS World Server', 'Path', ServerDir);
  if ServerDir = '' then RegQueryStringValue(HKCU, 'Software\Eagle Dynamics\DCS World OpenBeta Server', 'Path', ServerDir);
  Result := ServerDir;
end;

function WantServer(): Boolean;
begin
  Result := ServerOnly or WizardIsTaskSelected('server');
end;

function HasClient(): Boolean;
begin
  Result := not ServerOnly;
end;

{ Tasks page: preselect only on a server-only PC (player PC with a server folder: default path as before, user ticks the box) }
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and not TaskSet then begin
    TaskSet := True;
    if ServerOnly then WizardSelectTasks('server');
  end;
end;

{ Dedicated server chosen: confirm the Saved Games folder (different name with -w), pick the install folder if not in the registry }
function NextButtonClick(CurPageID: Integer): Boolean;
var Dir: String;
begin
  Result := True;
  if (CurPageID = wpSelectTasks) and WantServer() then begin
    Dir := ServerSaved('');
    if BrowseForFolder(CustomMessage('ServerSaved'), Dir, True) then ServerSave := RemoveBackslashUnlessRoot(Dir);
    if not FileExists(ServerDcs('') + '\bin\DCS_server.exe') then begin
      Dir := ExpandConstant('{commonpf64}\Eagle Dynamics');
      if BrowseForFolder(CustomMessage('ServerDcs'), Dir, False) then ServerDir := RemoveBackslashUnlessRoot(Dir);
    end;
  end;
end;

{ nicht gefunden: Ordner wählen lassen (Abbrechen = Setup abbrechen); Saved Games selbst gewählt -> DCS darunter (auch wenn DCS dort noch nie lief).
  Nur ein Dedicated Server auf dem PC (Saved Games\DCS.server o. ä.): nicht fragen, nur dorthin installieren }
function InitializeSetup(): Boolean;
var Dir: String;
begin
  Result := True;
  ServerOnly := not DirExists(SavedGames('')) and DirExists(ServerSaved(''));
  if not DirExists(SavedGames('')) and not ServerOnly then begin
    Dir := ExpandConstant('{usersavedgames}');
    Result := BrowseForFolder(CustomMessage('NoDcs'), Dir, False);
    if Result then begin
      Dir := RemoveBackslashUnlessRoot(Dir);
      if not DirExists(Dir + '\Config') and (Pos('DCS', Uppercase(ExtractFileName(Dir))) <> 1) then Dir := Dir + '\DCS';
      Saved := Dir;
    end;
  end;
end;

{ Export.lua: own aircraft (position, AoA for the LSO) to the app. Append if missing; uninstall: remove the line again }
procedure ExportLua(Add: Boolean);
var F: String; S: AnsiString; L, Keep: TArrayOfString; i, n: Integer;
begin
  F := SavedGames('') + '\Scripts\Export.lua';
  if Add then begin
    if not (LoadStringFromFile(F, S) and (Pos('DcsAtcExport', S) > 0)) then
      SaveStringToFile(F, #13#10 + ExportLine + #13#10, True);
  end else if LoadStringsFromFile(F, L) then begin
    n := 0;
    SetArrayLength(Keep, GetArrayLength(L));
    for i := 0 to GetArrayLength(L) - 1 do
      if Pos('DcsAtcExport', L[i]) = 0 then begin Keep[n] := L[i]; n := n + 1; end;
    if n < GetArrayLength(L) then begin
      SetArrayLength(Keep, n);
      SaveStringsToUTF8FileWithoutBOM(F, Keep, False);   { UTF-8 without BOM: Lua cannot handle a BOM }
    end;
  end;
end;

{ App path for the DCS hook (starts DcsAtc.exe with every mission); language for the app (lang.txt) and F10 menu (DcsAtcLang.txt) }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if HasClient() then begin
      ForceDirectories(SavedGames('') + '\Scripts\DcsAtc');
      SaveStringToFile(SavedGames('') + '\Scripts\DcsAtc\DcsAtcPath.txt', ExpandConstant('{app}\DcsAtc.exe'), False);
      SaveStringToFile(SavedGames('') + '\Scripts\DcsAtc\DcsAtcLang.txt', ActiveLanguage, False);
      ExportLua(True);
    end;
    if WantServer() then begin   { Dedicated server: the hook starts the app with every server mission }
      ForceDirectories(ServerSaved('') + '\Scripts\DcsAtc');
      SaveStringToFile(ServerSaved('') + '\Scripts\DcsAtc\DcsAtcPath.txt', ExpandConstant('{app}\DcsAtc.exe'), False);
      SaveStringToFile(ServerSaved('') + '\Scripts\DcsAtc\DcsAtcLang.txt', ActiveLanguage, False);
    end;
    SaveStringToFile(ExpandConstant('{app}\lang.txt'), ActiveLanguage, False);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then ExportLua(False);
end;
