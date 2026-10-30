# ProcTrack

A Half Grip (Steam app 4163270, Unity 6, Windows) mod that adds procedural race maps: a new closed lap built from a seed every race. The maps
are Circuit (in the Stadium), Forest, Mountain, Factory and Tokyo. The release is `ProcTrack/mod.json` (`version`, `gameVersion`): 1.0.25 for
game 0.418 and HalfGripTools 1.0.5 at the time of writing.

## How the mod runs

- A **source mod** for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools), a BepInEx 5 plugin. At game start the loader compiles
  `ProcTrack/src/*.cs` with Roslyn 4.8 (**C# 10**, netstandard2.1) against the game's DLLs, BepInEx, 0Harmony and HalfGripTools. What ships is
  the `ProcTrack/` folder (`mod.json`, `icon.png`, `src/`, `sprites/`), installed as `<game>\BepInEx\HalfGripMods\ProcTrack\`. The manager
  lists it as **Procedural Maps**.
- `mod.json` `"icon": "icon.png"` is the mod manager's picture (HalfGripTools 1.0.5+). `sprites/proctrack_icon.png` is something else: the
  fallback run-map icon when a source map's icon cannot be read.
- Entry point `Mod : HalfGripMod` in `src/Mod.cs`: config (`halfgrip.proctrack.cfg`), scene callbacks, `ctx.PatchAll()` for `src/Patches.cs`.
- Loader API: HalfGripTools `docs/WRITING_MODS.md`; known game internals: its `docs/GAME_NOTES.md`. The Highway and Multiplayer mods are in
  [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods); several techniques here copy Highway's.

## Layout of src/

| File | Role |
|---|---|
| `Mod.cs` | Config, scene-load hooks |
| `MapEntry.cs` | Adds the maps to `RunConfig.maps` as copies of real map entries (own track index, tinted icon, scaled time objectives); deals the weather |
| `Patches.cs` | Harmony: `MenuManager.TrackIndex`, `TrackManager.SpawnEnemies`, `RunState.BeginRace`, `RunMap.Generate` (TestMode) |
| `Layout.cs` | The lap generator (`Shape`, `Generator`), see below |
| `Rng.cs` | SplitMix64 and `DetMath`: the same numbers on every machine |
| `SeedSource.cs` | Seed per race: solo random or `SeedOverride`; in a Multiplayer room the host's, read from `Multiplayer.Room.Seed` by reflection |
| `CircuitScene.cs`, `CircuitScene.Stadium.cs`, `Mesh.cs` | The circuit on a flat slab in the Stadium. `.Stadium`: the road copied from `TrackFlat`, the grandstand, the Stadium's pit box/bridge/towers/blocks copied round the new lap by direction and distance, the cameras' draw distance raised to cover it all |
| `TerrainScene.cs` | Terrain maps: placement (`Place`/`Score`, with the Forest's tree-line boundary), road heights (`Profile`), the terrain cut, paint, grass, trees, the mesh road and its snow edge banks, the grid |
| `TerrainScene.Extras.cs` | `Transplant`s (old-track objects carried onto the new lap), the Factory hall and spinner, the tornado, Tokyo's city, roadside trees |
| `TerrainScene.Markers.cs` | Curve signs and burning barrels from each scene's own (by material: `LeftCurveSignTexture`/`Right...`), the invisible edge wall |
| `TerrainScene.Tunnel.cs` | The Mountain's ice tube, built from the cave's materials, with snow drifts against its walls |
| `TerrainScene.Yard.cs`, `.Expand.cs` | The Factory's yard and skyline; enlarging a terrain (Factory) |
| `Profiles.cs` | `MapProfile` per terrain map: every per-map number and switch, with comments saying why |
| `MultiplayerLeave.cs` | Works around the Multiplayer mod's 20 s scene-load lockout after leaving a room |

## The generator (Layout.cs)

Three base shapes, then a sum of seeded sine waves (`Wobble`, `Harmonics`), checked for `MinRadius`, `Separation` and the span limits, redrawn up
to 600 times, else the plain stadium (`Fallback`).

- **Corner chain** (`Shape.Chain`): Circuit (`Shape.Circuit`), Forest, Mountain and Tokyo. Six-ish corner points round a squashed ring, some
  pulled in (`DentChance`, `DentDepth`) so corners turn both ways, rounded with arcs, scaled to the lap length. The longest straight is the start
  straight; only its last `StraightKeep` metres stay straight (the grid). `ChainStraights` keeps that many 260 m straights unwobbled (the
  Mountain's ramps).
- **Teardrop** (`HairpinRadius`, with `FlareRadius`/`Neck`/`Bumps`): the Factory only, because its hall must sit on the U-turn.
- **Stadium**: only the fallback now.
- **Adding an option must not change another map's laps for any seed**: new random draws and changed formulas go behind the option, so a
  map that does not set it draws exactly as before.

## Rules that matter

- **Determinism.** Every player in a room must build the identical track and weather from the seed: draw only from `Rng`, never
  `System.Random`/`UnityEngine.Random`; the lap's shape must not depend on a config setting.
- **Bump `version` in `mod.json` with every change that ships, and the version line in `README.md` with it.** The Multiplayer mod checksums each
  mod's files in a room, so even an asset change (the icon) needs a new version.
- C# 10 and netstandard2.1 only. Match the style: tabs, `///` summaries that say why, `Mod.Info(...)` for the verbose log (only with
  `Debug.VerboseLog`), `Mod.Log.LogWarning` and try/catch round risky scene work. A failed build puts the scene back (`Unload`).
- Do not invent an `ObjectiveKind` (a saved run with one is deleted).

## Checking changes

Private repo [HalfGripRefs](https://github.com/htubdawg/HalfGripRefs) (attach it to the session) has the game's DLLs (`game/`, laid out as the
game folder), scene dumps of every map (`scenedumps/0.418/<map>/`) and tools (`tools/README.md`):

- **Compile:** `tools/check-mod.sh ../ProcTrack/ProcTrack/src` must print `0 Error(s)`, `0 Warning(s)`. Needs the .NET 8 SDK
  (`apt-get install -y dotnet-sdk-8.0` if the environment's setup script has not installed it).
- **Lap shapes:** `tools/LapViz` runs this repo's generator outside the game: fallbacks, longest straight, turning against the lap, drawings.
  Tune shape settings there before touching the mod, and show the owner drawings. Its shapes are copies of `Profiles.cs`: keep them in step.
- **Placement and heights:** `tools/scripts/terrainsim.py` / `forestsim.py` on a dump's height map.
- **Game code:** decompile `Assembly-CSharp.dll` with ilspycmd 8.2 (`tools/README.md`).
- **In game** only the owner can test. Each PR lists what to look at, and the log lines (`VerboseLog`) that tell the outcomes apart.

Traps already hit:

- Scene dumps collapse repeated names after `ShownPerGroup` (default 3); the full Forest dump (`..._20261028-193038`) sets it to 100. Dumps come
  in two variants: terrain data named `... (Mirrored)` has z flipped. ProcTrack races the normal one.
- The track line's height is not the road's (the Mountain's is 3 m everywhere). Measure heights from the ground.
- A switched-off renderer has no bounds: measure scene objects before hiding them.
- The Forest's tree-line sprites hang high (some past the terrain's edge) and close the play area off as an irregular polygon, not a circle;
  the lap is held 100 m inside it, with a belt of trees in between.
- Copying a whole scene object can wake its scripts. Bending the original cave onto the road (1.0.21) was reverted: the tunnel was not built
  (the cave's 546 m stretch fit no lap) and a stack overflow followed (cause not found; Scene Dump was also running).

## Working with the owner

- Propose with evidence (drawings, numbers, dump findings), get a go-ahead, then implement. One PR per change on the session's branch; the
  owner merges and tests in game. Before saying anything about a PR's state, `git fetch` and look: never assume it is unmerged.
- Do not push something you intend to undo. Keep tools and scratch work out of this repo (they go in HalfGripRefs).
- If you lack access to something (a repo, a host, a tool, game files), ask for it instead of working around it, and do not rebuild a program
  that already exists when access to it could be granted.
- The owner does not want to touch GitHub beyond pressing merge.
