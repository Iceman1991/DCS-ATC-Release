-- DcsAtcHook.lua, radio wheel window (#30): show.txt -> window created once, picture and size set, hidden when show.txt is gone; a GUI error switches it off.
-- luae.exe tests\hooktest.lua mod\Scripts\Hooks\DcsAtcHook.lua
local hook = arg[1]
local tmp = os.getenv('TEMP') .. '\\DcsAtcHookTest2'
local dir = tmp .. '\\DcsAtc-Wheel'
os.execute('mkdir "' .. dir .. '" 2>nul')
local getenv, clock = os.getenv, 0
os.getenv = function(k) if k == 'TMP' or k == 'TEMP' then return tmp end return getenv(k) end
os.clock = function() clock = clock + 1 return clock end
lfs = { mkdir = function() end, attributes = function() return nil end, writedir = function() return tmp .. '\\' end, currentdir = function() return tmp end, dir = function() return function() end end }
package.loaded.lfs = lfs
local logs = {}
net = { log = function(s) logs[#logs + 1] = s end }
local cb
DCS = { setUserCallbacks = function(c) cb = c end, isServer = function() return true end }
local spawned, fail = 0, false
local win = { visible = false, pic = { skin = { skinData = { states = { released = { {} } } } } } }
function win:setHasCursor(v) self.cursor = v end
function win:setVisible(v) self.visible = v end
function win:setBounds(x, y, w, h) self.bounds = { x, y, w, h } end
function win.pic:getSkin() return self.skin end
function win.pic:setSkin(s) self.set = s.skinData.states.released[1].picture end
function win.pic:setBounds(x, y, w, h) self.bounds = { x, y, w, h } end
package.loaded.DialogLoader = { spawnDialogFromFile = function(f) if fail then error('no gui') end spawned = spawned + 1 assert(loadfile(f)) return win end }
package.loaded.dxgui = { GetScreenSize = function() return 1920, 1080 end }
package.loaded.SkinUtils = { setStaticPicture = function(file, skin) skin.skinData.states.released[1].picture = { file = file } return skin end }
dofile(hook)
local function show(s) local h = assert(io.open(dir .. '\\show.txt', 'w')) h:write(s) h:close() end
local function check(ok, what) if not ok then print('FAIL ' .. what) os.exit(1) end end

os.remove(dir .. '\\show.txt')
cb.onSimulationFrame()
check(spawned == 0 and not win.visible, 'ohne show.txt kein Fenster')
show('0123456789abcdef.png|0.50')
cb.onSimulationFrame()
local p = win.pic.set
check(spawned == 1 and win.visible and win.cursor == false and p and p.file == dir .. '\\0123456789abcdef.png' and p.size.horz == 540 and p.size.vert == 540, 'Bild und Groesse gesetzt')
check(win.bounds[1] == 690 and win.bounds[2] == 270 and win.bounds[3] == 540 and win.pic.bounds[3] == 540, 'Fenster mittig 540 px')
show('fedcba9876543210.png|0.50')
cb.onSimulationFrame()
check(spawned == 1 and win.pic.set.file == dir .. '\\fedcba9876543210.png', 'neues Bild, selbes Fenster')
show('..\\evil.png|0.50')
cb.onSimulationFrame()
check(win.pic.set.file == dir .. '\\fedcba9876543210.png', 'fremder Pfad in show.txt ignoriert')
os.remove(dir .. '\\show.txt')
cb.onSimulationFrame()
check(not win.visible, 'show.txt weg -> versteckt')

-- no GUI (dedicated server): one log line, then quiet
dofile(hook)
fail = true
show('0123456789abcdef.png|0.50')
local n = #logs
cb.onSimulationFrame() cb.onSimulationFrame()
show('fedcba9876543210.png|0.50')
cb.onSimulationFrame()
os.remove(dir .. '\\show.txt')
local off = 0
for i = n + 1, #logs do if logs[i]:find('abgeschaltet') then off = off + 1 end end
check(off == 1, 'GUI-Fehler schaltet das Rad-Fenster einmal ab (' .. off .. ')')
print('OK   #30 Hook Rad-Fenster: anlegen, Bild/Groesse, wechseln, verstecken, fremder Pfad, GUI-Fehler')

-- Version check in multiplayer: server names its version once per player and mission; client compares, hints, reports "ATC>~version"
local sdir = tmp .. '\\Scripts\\DcsAtc'
os.execute('mkdir "' .. sdir .. '" 2>nul')
local function put(name, text) local f = assert(io.open(sdir .. '\\' .. name, 'w')) if text then f:write(text) end f:close() if not text then os.remove(sdir .. '\\' .. name) end end
local isServer, sent, chatTo, shown, showFail, serverId = true, {}, {}, {}, false, 1
DCS.isServer = function() return isServer end
DCS.isMultiplayer = function() return true end
net.get_my_player_id = function() return isServer and 1 or 7 end
net.get_server_id = function() return serverId end
net.send_chat_to = function(t, id) chatTo[#chatTo + 1] = { t, id } end
net.send_chat = function(t, all) sent[#sent + 1] = t end
net.recv_chat = function(t) if showFail then error('no chat') end shown[#shown + 1] = t end
local LINE = 'This server runs DCS-ATC 0.23.20-alpha. Talk to its controllers over SRS. If you have DCS-ATC installed, use the same version.'
local function reset() chatTo, sent, shown = {}, {}, {} end

dofile(hook)
put('DcsAtcVersion.txt', '0.23.20-alpha')
cb.onPlayerChangeSlot(2) cb.onPlayerChangeSlot(2) cb.onPlayerChangeSlot(3) cb.onPlayerChangeSlot(1)
check(#chatTo == 2 and chatTo[1][1] == LINE and chatTo[1][2] == 2 and chatTo[2][2] == 3, 'Server nennt Version je Spieler einmal, nicht sich selbst (' .. #chatTo .. ')')
cb.onSimulationStop() cb.onPlayerChangeSlot(2)
check(#chatTo == 3, 'neue Mission, neuer Hinweis')
reset() put('DcsAtcVersion.txt', nil) cb.onSimulationStop() cb.onPlayerChangeSlot(5)
check(#chatTo == 0, 'ohne Versionsdatei nichts gesendet')

isServer = false
put('DcsAtcVersion.txt', '0.23.18-alpha') put('DcsAtcLang.txt', 'en')
cb.onChatMessage(LINE, 4) cb.onChatMessage('hello', 1)
check(#sent == 0 and #shown == 0, 'fremde Absender/anderer Text ignoriert')
cb.onChatMessage(LINE, 1)
check(sent[1] == 'ATC>~version 0.23.18-alpha' and #shown == 1 and shown[1]:find('Please update') and shown[1]:find('0.23.18%-alpha') and shown[1]:find('0.23.20%-alpha'), 'aelter: Hinweis und Meldung')
cb.onChatMessage(LINE, 1)
check(#sent == 1 and #shown == 1, 'nur einmal pro Mission')
reset() cb.onSimulationStop() put('DcsAtcVersion.txt', '0.23.20-alpha') cb.onChatMessage(LINE, 1)
check(#sent == 1 and #shown == 0, 'gleiche Version: Meldung, kein Hinweis')
reset() cb.onSimulationStop() put('DcsAtcVersion.txt', '0.24.0-alpha') put('DcsAtcLang.txt', 'de') cb.onChatMessage(LINE, 1)
check(#shown == 1 and shown[1]:find('Funkrad') and shown[1]:find('Du hast 0.24.0'), 'neuer: deutscher Hinweis')
reset() cb.onSimulationStop() put('DcsAtcVersion.txt', '0.23.18-alpha') showFail = true
cb.onChatMessage(LINE, 1)
check(#sent == 1, 'recv_chat-Fehler: Meldung trotzdem')
showFail = false reset() cb.onSimulationStop() serverId = 9 cb.onChatMessage(LINE, 1)
check(#sent == 0, 'Server-ID aus net.get_server_id')
cb.onChatMessage(LINE, 9)
check(#sent == 1, 'Server-ID 9 akzeptiert')
reset() cb.onSimulationStop() put('DcsAtcVersion.txt', nil) cb.onChatMessage(LINE, 9)
check(#sent == 0 and #shown == 0, 'ohne Versionsdatei nichts')
print('OK   Mehrspieler-Versionsabgleich: Server nennt Version, Client Hinweis aelter/neuer + Meldung')
