-- Kutaisi ATIS (MOOSE) – voice announcement over DCS radio with sound files from the .miz (no SRS needed)
-- Frequency 131.500 MHz AM. Sound files are in the .miz under "ATIS Soundfiles/".
if os and io and lfs then env.info("KutaisiATIS: DCS-ATC-App macht ATIS (263.5), MOOSE-ATIS aus."); return end
KutaisiATIS = ATIS:New("Kutaisi", 131.5)
KutaisiATIS:SetTowerFrequencies({ 134.0, 263.0 })
KutaisiATIS:SetTACAN(44)
KutaisiATIS:SetVOR(113.6)
KutaisiATIS:ReportQNHOnly()
KutaisiATIS:SetReportmBar(true)
KutaisiATIS:SetMapMarks(true)
KutaisiATIS:SetTransmitOnlyWithPlayers(true)
KutaisiATIS:Start()
