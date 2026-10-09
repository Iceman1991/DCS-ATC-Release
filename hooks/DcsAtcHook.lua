-- DCS-ATC radio wheel for fellow players: to Saved Games\DCS\Scripts\Hooks (on the host AND on fellow players).
--  Fellow players: DcsAtc.exe in "client" mode writes wheel requests to %TMP%\DcsAtc-Chat\*.txt,
--              this hook sends them as chat "ATC>..." to the host.
--  Host:       intercepts "ATC>..." (nobody sees it) and files the request for the DCS-ATC app
--              (%TMP%\DcsAtc-Menu\*.txt, "@playername|text").
local lfs = require('lfs')
local tmp = os.getenv('TMP') or os.getenv('TEMP') or ''
local outDir, inDir = tmp .. '\\DcsAtc-Chat', tmp .. '\\DcsAtc-Menu'
local n, nextPoll = 0, 0
local cb = {}

function cb.onPlayerTrySendChat(id, msg, all)
  if type(msg) ~= 'string' or msg:sub(1, 4) ~= 'ATC>' then return end
  local name = (net.get_player_info(id, 'name') or ''):gsub('[|\r\n]', ' ')
  n = n + 1
  local f = string.format('%s\\%010d-c%04d', inDir, os.time(), n)
  local h = io.open(f .. '.tmp', 'w')
  if h then
    h:write('@' .. name .. '|' .. msg:sub(5))
    h:close()
    os.rename(f .. '.tmp', f .. '.txt')
  end
  return ''   -- do not show in chat
end

function cb.onSimulationFrame()
  local now = os.clock()
  if now < nextPoll then return end
  nextPoll = now + 0.3
  if DCS.isServer() or not lfs.attributes(outDir) then return end
  for f in lfs.dir(outDir) do
    if f:sub(-4) == '.txt' then
      local path = outDir .. '\\' .. f
      local h = io.open(path, 'r')
      if h then
        local text = h:read('*a')
        h:close()
        os.remove(path)
        if text and #text > 0 then net.send_chat('ATC>' .. text, false) end
      end
    end
  end
end

DCS.setUserCallbacks(cb)
net.log('DcsAtcHook geladen')
