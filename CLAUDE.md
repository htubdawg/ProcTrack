# ProcTrack

A Half Grip (Steam app 4163270, Unity, Windows) mod that adds procedural race maps: a new closed lap built from a seed every race.
The maps are Circuit (in the Stadium), Forest, Mountain, Factory and Tokyo. Current release: see `ProcTrack/mod.json` (`version`, `gameVersion`).

## How the mod runs

- It is a **source mod** for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools), which runs as a BepInEx 5 plugin. At game start the loader compiles
  `ProcTrack/src/*.cs` with Roslyn 4.8 (**C# 10**, netstandard2.1) against the game's `Assembly-CSharp.dll`, the Unity DLLs, BepInEx, 0Harmony and HalfGripTools.
  Nobody ships a DLL. What ships is the `ProcTrack/` folder (`mod.json`, `src/`, `sprites/`). It is installed to `<game>\BepInEx\HalfGripMods\ProcTrack\`.
- The entry point is `Mod : HalfGripMod` in `src/Mod.cs`. It binds the config (`halfgrip.proctrack.cfg`) and registers the scene callbacks, and `ctx.PatchAll()` applies the
  `[HarmonyPatch]` classes in `src/Patches.cs`.
- The loader API (`ModContext`, `ctx.Items`, `ctx.Keys`, sprites) is in `docs/WRITING_MODS.md`. What is known about the game's classes is in `docs/GAME_NOTES.md`. Both are
  in the HalfGripTools repo. The Highway and Multiplayer mods are in [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods). Several techniques here copy Highway's.

## Layout of src/

| File | Role |
|---|---|
| `Mod.cs` | Config, scene-load hooks |
| `MapEntry.cs` | Adds the maps to `RunConfig.maps` as copies of real map entries, each with its own track index |
| `Patches.cs` | Harmony: `MenuManager.TrackIndex`, `TrackManager.SpawnEnemies`, `RunState.BeginRace`, `RunMap.Generate` |
| `Layout.cs` | Lap generator: a closed loop of points about 5 m apart, a straight grid, corners, an optional hairpin |
| `Rng.cs` | SplitMix64. Deterministic on every machine (see the rules below) |
| `SeedSource.cs` | Seed per race: solo is random or `SeedOverride`; in a Multiplayer room it is the host's, read from `Multiplayer.Room.Seed` by reflection |
| `CircuitScene.cs`, `Mesh.cs` | The Stadium circuit, built as meshes on a flat slab |
| `TerrainScene*.cs` | Terrain maps: road cut into the scene terrain (`.Expand` enlarges it, `.Extras` holds the hazards/walls/Tokyo city, `.Markers` the signs and edge wall, `.Tunnel` the mountain ice tube, `.Yard` the factory yard and hairpin walls/trees) |
| `Profiles.cs` | Per-map `MapProfile` data and `Transplant`s (old-track hazards carried onto the new lap) |
| `MultiplayerLeave.cs` | Works around the Multiplayer mod's 20 s scene-load lockout after a player leaves a room |

## Rules that matter

- **Determinism.** Every player in a Multiplayer room must build the identical track and weather from the same seed. Draw only from `Rng`, never from
  `System.Random` or `UnityEngine.Random`. The shape of the lap must not depend on any config setting. Any change to generation means everyone in a room needs the
  same ProcTrack version.
- Bump `version` in `mod.json` with each release, and keep the version in `README.md` in step.
- Stay in C# 10 and netstandard2.1 APIs. The in-game compiler does not take newer language features.
- Match the existing style: tabs, `///` summaries that explain *why*, `Mod.Info(...)` for verbose logging, and `Mod.Log.LogWarning` with a try/catch around risky
  scene work.
- Do not invent an `ObjectiveKind` (a saved run that has one is deleted). See the pitfalls in GAME_NOTES.

## Checking changes

- A full type check needs the game installed (Windows). Use HalfGripTools `tools\check-mods.ps1 -ModsPath <folder holding ProcTrack> -Mod ProcTrack -GameDir "<game folder>"`.
- To test in game, copy `ProcTrack/` to `<game>\BepInEx\HalfGripMods\`, set `TestMode=true` and `TestMap=<map>` in the cfg, and read `BepInEx\LogOutput.log`.
  `VerboseLog` and `ShowPath` help with debugging.

## Working with the owner

- If you lack access to something (a repo, a host, a tool, game files), ask the owner for it. Do not try workarounds, and do not rebuild a program that
  already exists when access to it could be granted.
