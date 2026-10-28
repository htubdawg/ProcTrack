100% AI generated code
============================================================
PROCEDURAL MAPS  (ProcTrack 1.0.11, for Half Grip Demo 0.418)
============================================================

video showcase
https://youtu.be/2Q-mIE49L5M

============================================================

Race maps that build a brand new track from a seed every race. Every map is
dealt into normal runs; each one reuses the game's own scene, props and hazards.

  Procedural Circuit   3.2 km tarmac lap built inside the Stadium.
  Procedural Forest    Wide dirt road through Forest Field, ramps, a hairpin
                       with trees, fields and the game's tornado.
  Procedural Mountain  Road across the Winter Mountain crater floor with an
                       ice tube, a hairpin, ramps.
  Procedural Factory   A long lap that starts straight into the Fire Factory
                       building (the U-turn through the hall, hammers, barrel
                       machine), then runs out to a big loop with the giant
                       spinner. A wall out of the hall stops the U-turn being
                       cut, a wall opposite the spinner stops it being dodged,
                       and a skyline of buildings stands on the outer ground.
  Procedural Tokyo     Tight walled city course with themed districts, torii
                       gates with sign billboards, a hairpin and rooftop
                       machine guns.

Each map uses its source map's own icon, turned upside down and tinted (circuit yellow, forest green, mountain blue, factory orange, Tokyo pink).

Each race gets a weather dealt from the weathers of that scene. The bots follow
a racing line (the road pulled taut, cutting the inside of bends).

SEEDS
-----
Solo: a new random seed every race. Put a number in SeedOverride (settings) to
replay a track; the seed is on the sign over the start line and in the log.
Multiplayer room: everybody gets the host's seed, so the same track and weather.
Everyone in a room needs the same ProcTrack version and the same map settings.


1. WHAT YOU NEED FIRST
----------------------
BepInEx and HalfGripTools (the HalfGripMods installer).

https://github.com/4l3x4nd3r0/HalfGripTools
https://github.com/4l3x4nd3r0/HalfGripMods

2. INSTALL
----------
Put the ProcTrack folder in   <game folder>\BepInEx\HalfGripMods\
so that this file exists:    <game folder>\BepInEx\HalfGripMods\ProcTrack\mod.json
(Delete an older ProcTrack folder first. Not ProcTrack\ProcTrack\mod.json.)

3. FIRST START
--------------
Start the game, wait for the main menu, then quit. The mod compiles on the first
start. This creates   <game folder>\BepInEx\config\halfgrip.proctrack.cfg

4. SETTINGS (halfgrip.proctrack.cfg, open in Notepad)
-----------------------------------------------------
  Forest / Mountain / Factory / Tokyo = true/false   Whether that map is dealt
        in normal runs (restart the game; room players need the same values).
  SeedOverride = 0      A number here replays that track (solo only).
  RandomWeather = true  Deal a weather from the scene's weathers.
  BenchmarkFactor       Scales the "finish under X s" objectives.
  ShowBoard = true      The seed sign over the start line.
  TestMode / TestMap    Force every race onto one map, to try it.
  VerboseLog = false    (Debug) The detailed build log. Off, only each race's
                        seed, warnings and errors are written.
  ShowPath = false      (Debug) Draw the line the bots follow.

---------------------------

To remove the mod: delete the ProcTrack folder from BepInEx\HalfGripMods.
