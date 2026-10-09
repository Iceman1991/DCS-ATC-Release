-- DCS-ATC default settings for all missions. Changeable in game: F10 -> ATC -> Settings.
-- To set a single mission differently: trigger "Mission Start" -> DO SCRIPT, e.g.  DCSATC_OPTIONS = { crew = true }
-- DCS-ATC does not spawn aircraft (no AI traffic, tanker, AWACS): it only does the radio for what the mission has.
return {
  crew    = false, -- Ground crew, fuel/ammo trucks, fire truck
}
