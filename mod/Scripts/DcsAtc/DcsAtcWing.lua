-- DCS-ATC (mod): AI wingmen by voice. A line in <DCS>\Scripts\autoexec.lua (DcsAtc-Patch.ps1) loads this file into the main Lua state of the
-- simulation, where the DCS radio menu (RadioCommandDialogsPanel) lives; hooks run in the GUI state and never reach it.
-- The app writes "par;wMsgLeader..." to %TMP%\DcsAtc-Wing.txt (par as in the DCS radio menu: 1-3 wingman 2-4, 5 flight). Performs the menu item
-- that sends this message to that recipient, with the menu's own parameters: independent of the module's numbering and of the DCS language, no keys.
-- Reply as event file %TMP%\DcsAtc-Menu\*.evt "WING;result;par;msg".
local tmp = os.getenv('TMP') or os.getenv('TEMP') or ''
local wingFile, inDir, n = tmp .. '\\DcsAtc-Wing.txt', tmp .. '\\DcsAtc-Menu', 0   -- '\\' required: Lua 5.1 turns '\D' into just 'D'

local function wing(par, name)
  local P, id = package.loaded['RadioCommandDialogsPanel'] or rawget(_G, 'RadioCommandDialogsPanel'), rawget(_G, 'Message') and Message[name]
  local root = P and P.getDataParameter and P.getDataParameter('rootItem')
  if not root then return 'no radio menu' end
  if not id then return 'unknown message ' .. name end
  local function only()   -- the addressed wingman is the only one: "Flight" reaches just him (F-16C/FA-18C list "Wingman n" only if unit n+1 exists, so never in a two-ship)
    local u = P.getDataParameter('pUnit')
    local g = u and u:getGroup()
    if not g or par > 3 then return false end
    for i = 2, 4 do if (g:getUnit(i) ~= nil) ~= (i == par + 1) then return false end end
    return true
  end
  local function sub(it) return it.submenu or it.getSubmenu and it:getSubmenu() end
  local path = {}
  local function find(m, d)
    for _, it in pairs(m and m.items or {}) do
      path[d] = it
      local c = it.command
      if c and (c.command == id or c.sendMessage and c.sendMessage.command == id) or d < 5 and sub(it) and find(sub(it), d + 1) then return true end
    end
    path[d] = nil
  end
  for _, it in pairs(sub(root).items or {}) do
    if it.parameter == par and (not it.condition or it.condition:check()) then
      path = { it }
      if find(sub(it), 2) then
        local p = {}
        for _, x in ipairs(path) do   -- as CommandMenu.performCommands: each command gets the parameters collected before it (recipient first tunes the radio)
          if x.command then x.command:perform(p) p = {} end
          if x.parameter then p[#p + 1] = x.parameter elseif x.parameters then for _, v in ipairs(x.parameters) do p[#p + 1] = v end end
        end
        return 'ok'
      end
    end
  end
  if only() then return wing(5, name) end
  return 'not in the radio menu (recipient ' .. par .. ')'
end

local function wingPoll()
  local h = io.open(wingFile, 'r')
  if not h then return end
  local text = h:read('*a') or ''
  h:close()
  os.remove(wingFile)
  local par, name = text:match('^(%d+);([%w_]+)')
  local ok, r = pcall(wing, tonumber(par), name or '')
  r = ok and r or 'error ' .. tostring(r)
  lfs.mkdir(inDir)
  n = n + 1
  local f = string.format('%s\\%010d-g%04d', inDir, os.time(), n)
  h = io.open(f .. '.tmp', 'w')
  if h then h:write('WING;' .. tostring(r):gsub('[;\r\n]', ' ') .. ';' .. text:gsub('[\r\n]', '')) h:close() os.rename(f .. '.tmp', f .. '.evt') end
end

-- every 0.3 s; armed at load and again at every mission start (afterStart from autoexec.lua), the previous timer removed
local ref
local function arm()
  pcall(timer.removeFunction, ref)
  ref = timer.scheduleFunction(function() pcall(wingPoll) return timer.getTime() + 0.3 end, nil, timer.getTime() + 1)
end
local after = afterStart
function afterStart(...)
  if after then after(...) end
  pcall(arm)
end
pcall(arm)
