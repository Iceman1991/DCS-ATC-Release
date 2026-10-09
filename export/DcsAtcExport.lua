-- DCS-ATC: sends the state of the own aircraft (2x/s) and the air traffic in the vicinity (1x/s, only without mission script)
-- to the ATC app (UDP 127.0.0.1:18500). Loaded at the end of Saved Games\DCS\Scripts\Export.lua.
local ok, err = pcall(function()
  package.path  = package.path  .. ";.\\LuaSocket\\?.lua"
  package.cpath = package.cpath .. ";.\\LuaSocket\\?.dll"
  local socket = require("socket")
  local udp = socket.udp()
  udp:settimeout(0)
  local RANGE = 40000   -- Traffic within 40 km of the own aircraft
  local nextSelf, nextTraffic = 0, 0
  local lfs = require("lfs")
  local stateFile = (os.getenv("TMP") or os.getenv("TEMP") or "") .. "\\DcsAtc-State.txt"

  local function sendTraffic()
    local me = LoGetPlayerPlaneId and LoGetPlayerPlaneId()
    local s = LoGetSelfData()
    if not s then return end
    local parts = { "T" }
    for id, o in pairs(LoGetWorldObjects() or {}) do
      if id ~= me and o.Type and o.Type.level1 == 1 and o.Position then
        local dx, dz = o.Position.x - s.Position.x, o.Position.z - s.Position.z
        if dx * dx + dz * dz < RANGE * RANGE then
          parts[#parts + 1] = string.format("%d,%s,%.0f,%.0f,%.0f,%.3f", id, (o.Name or "?"):gsub("[,;]", " "),
            o.Position.x, o.Position.z, o.LatLongAlt and o.LatLongAlt.Alt or o.Position.y, o.Heading or 0)
        end
      end
    end
    udp:sendto(table.concat(parts, ";"), "127.0.0.1", 18500)
  end

  local prev = LuaExportAfterNextFrame
  LuaExportAfterNextFrame = function()
    if prev then prev() end
    local t = LoGetModelTime()
    if t >= nextSelf then
      nextSelf = t + 0.5
      local s = LoGetSelfData()
      if s then
        local w = LoGetVectorWindVelocity and LoGetVectorWindVelocity() or { x = 0, z = 0 }
        local p = LoGetBasicAtmospherePressure and LoGetBasicAtmospherePressure() or 0
        -- first locked target (declare): world position x;z;altitude, missing without a lock or when the server blocks it
        local okL, lk = pcall(function()
          local _, g = next(LoGetLockedTargetInformation() or {})
          local q = g.position.p
          return string.format(";%.0f;%.0f;%.0f", q.x, q.z, q.y)
        end)
        if not okL then lk = "" end
        udp:sendto(string.format("S;%.1f;%.1f;%.2f;%.4f;%.1f;%.1f;%.2f;%.2f;%.3f;%.4f",
          s.LatLongAlt.Alt, LoGetAltitudeAboveGroundLevel() or 0, LoGetIndicatedAirSpeed() or 0, s.Heading,
          s.Position.x, s.Position.z, w.x, w.z, p, LoGetAngleOfAttack and LoGetAngleOfAttack() or 0) .. lk, "127.0.0.1", 18500)   -- AoA rad (LSO)
      end
    end
    if t >= nextTraffic then
      nextTraffic = t + 1
      -- mission script running (state file fresh): the app takes traffic from there; LoGetWorldObjects (every object in the world) only as fallback, it costs frame time
      local m = lfs.attributes(stateFile, "modification")
      if not (m and os.time() - m < 5) then pcall(sendTraffic) end
    end
  end
end)
if not ok then log.write("DCS-ATC", log.ERROR, tostring(err)) else log.write("DCS-ATC", log.INFO, "Telemetrie-Export aktiv") end
