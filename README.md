# Procedural Maps (ProcTrack) for Half Grip

**Version 1.0.24** · for Half Grip Demo 0.418 · needs [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools)

Race maps that build a brand new track from a seed every race. Five procedural maps are dealt into your runs like any other map, each
one built in one of the game's own scenes, with its props and hazards carried onto the new road. In a Multiplayer room everybody
races the same track.

*100% AI generated code.*

**Video showcase:** https://youtu.be/2Q-mIE49L5M

## The maps

Every lap is a chain of corners turning both ways, laid out anew from the seed (the Factory keeps its own long lap through the hall).

| Map | Lap | Built in | What you race |
|---|---|---|---|
| **Procedural Circuit** | 3.2 km | Stadium | A walled tarmac circuit with kerbs, eight cars on the grid, a grandstand along the start straight, and the Stadium's pit box, bridge and skyline set round it |
| **Procedural Forest** | 2.7 km | Forest Field | A wide dirt road with trees beyond its sharpest corner, wheat fields, a forest edge inside the backdrop, and the game's tornado |
| **Procedural Mountain** | 2.9 km | Winter Mountain | A winding road across the crater with snow banked along it, an ice tube, the ice arches, twisty ramps and curve signs |
| **Procedural Factory** | 3.4 km | Fire Factory | Straight into the factory building (the U-turn through the hall, hammers, barrel machine), then out to a big loop past the giant spinner, with a yard and a skyline round it |
| **Procedural Tokyo** | 2.7 km | Tokyo Temple | A tight walled city course with themed districts, torii gates with sign billboards, and machine guns on the rooftops |

Each map's icon is its source map's icon, turned upside down and tinted: circuit yellow, forest green, mountain blue, factory orange,
Tokyo pink.

### Weather

Each race gets one of the weathers the game has for that scene (turn this off with `Map.RandomWeather`):

| Map | Weathers |
|---|---|
| Circuit | clear, ash, rainy night |
| Forest | red ash, rainy day, rainy night |
| Mountain | day, snowy night |
| Factory | fire, night |
| Tokyo | rainy neon night (its only one) |

The bots follow a racing line: the road pulled taut, cutting the inside of the bends.

## Seeds and Multiplayer

- **Solo:** a new random seed every race. The seed is on the sign over the start line and in the log; put it in `Map.SeedOverride` to
  race that track again.
- **Multiplayer room** (with the [Multiplayer](https://github.com/4l3x4nd3r0/HalfGripMods) mod): everybody gets the host's seed, so
  the same track and weather. **Everyone in the room needs the same ProcTrack version and the same map settings.**

## Install

1. Install [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools) if you have not already (the
   [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods) installer sets up BepInEx and HalfGripTools for you).
2. Download this repository (**Code > Download ZIP**) and open the zip.
3. Delete any older `ProcTrack` folder in `<game>\BepInEx\HalfGripMods\`, then copy the **`ProcTrack`** folder out of the zip into
   `<game>\BepInEx\HalfGripMods\`.
4. Start the game and wait for the main menu. The first start compiles the mod, which takes a few seconds. The mod manager in the main
   menu should list **Procedural Maps**.

> [!IMPORTANT]
> **Copy only the `ProcTrack` folder, not the whole download.** The zip unpacks to `ProcTrack-main\`, and the mod is the `ProcTrack`
> folder inside it. `mod.json` must sit **directly** inside `HalfGripMods\ProcTrack\`, or the mod does not load.

Right:

```
Half Grip Demo\
└── BepInEx\
    └── HalfGripMods\
        └── ProcTrack\
            ├── mod.json
            ├── sprites\
            └── src\
```

Wrong (one folder too deep, the mod does not load):

```
Half Grip Demo\
└── BepInEx\
    └── HalfGripMods\
        └── ProcTrack-main\      <- the whole download
            └── ProcTrack\
                └── mod.json
```

`<game>` is the game's folder, usually `C:\Program Files (x86)\Steam\steamapps\common\Half Grip Demo`. If the maps do not show up,
look for `Loaded Procedural Maps` in `BepInEx\LogOutput.log`.

## Settings

In `BepInEx\config\halfgrip.proctrack.cfg` (open it in Notepad), written the first time the game runs with the mod. Restart the game
after changing it.

| Setting | Default | What it does |
|---|---|---|
| `Map.Circuit`, `Map.Forest`, `Map.Mountain`, `Map.Factory`, `Map.Tokyo` | `true` | Deal that map into your runs. Everyone in a Multiplayer room needs the same values |
| `Map.SeedOverride` | `0` | Solo only: build every lap from this seed, to race a track again. `0` gives a new random seed each race |
| `Map.RandomWeather` | `true` | Deal a weather from the scene's own weathers. Off, each map keeps the weather it was copied with |
| `Map.BenchmarkFactor` | `0.9` | Scales the "finish under X s" objectives to the lap's length. Lower is harder |
| `Map.ShowBoard` | `true` | The sign with the seed over the start line |
| `Map.TestMode` | `false` | Put every race on the map `Map.TestMap` names, to try it out |
| `Map.TestMap` | `Forest` | The map for `TestMode`: `Circuit`, `Forest`, `Mountain`, `Factory` or `Tokyo` |
| `Debug.VerboseLog` | `false` | Write the detailed build log (every hazard, sign, wall and tree placed) |
| `Debug.ShowPath` | `false` | Draw the line the bots and the lap counter follow |

## Notes

- **The log.** With `Debug.VerboseLog` off, each race writes a few lines to `BepInEx\LogOutput.log`: that the map is being built, its
  seed, lap length and tightest corner, how long the build took, and the weather dealt. Warnings and errors are always written. If a map
  fails to build, the scene is put back as it was and the log says why.
- **Removing the mod:** delete the `ProcTrack` folder from `BepInEx\HalfGripMods`. Finish or abandon a run that has procedural maps on
  its map first.
- **Speedrun mode:** HalfGripTools does not load mods in speedrun mode, so there are no procedural maps there.
- **[MiniMap](https://github.com/htubdawg/HalfGripMiniMap)** draws the procedural laps too.
