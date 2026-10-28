using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace HalfGripProcTrack
{
	/// <summary>
	/// Race maps that are built from a seed each time a race loads: a start straight and a random run of corners. The circuit, a 3.2 km
	/// tarmac lap, is built from code inside the Stadium scene; the forest, mountain, factory and Tokyo laps (about 2.7 to 2.9 km) are cut
	/// into the terrain of the game's own scene of that name. Like the Highway mod they have no scene of their own, and are added to the
	/// run map's pool.
	///
	/// Only the settings below that change how the lap is scored or shown are settings. The shape of the lap is not: every player in a room
	/// has to build the same track, and a setting on one machine would break that.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;
		internal static ModContext Ctx;

		internal static ConfigEntry<bool> TestMode;
		internal static ConfigEntry<string> TestMap;
		internal static ConfigEntry<int> SeedOverride;
		internal static ConfigEntry<bool> RandomWeather;
		internal static ConfigEntry<float> BenchmarkFactor;
		internal static ConfigEntry<bool> ShowBoard;
		internal static ConfigEntry<bool> ShowPath;
		internal static ConfigEntry<bool> Verbose;

		/// <summary>A detail line for the log: only written when Debug.VerboseLog is on. (The seed of each race, warnings and errors are always written.)</summary>
		internal static void Info(string text)
		{
			if (Verbose != null && Verbose.Value)
			{
				Log.LogInfo(text);
			}
		}

		/// <summary>The circuit's map entry in the run config, once it exists.</summary>
		internal static MapDefinition Map;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			TestMode = ctx.Config.Bind("Map", "TestMode", false, "Every race you start is on the map TestMap names, whatever the run map says. For trying it out. Off, the maps are dealt like any other map.");
			TestMap = ctx.Config.Bind("Map", "TestMap", "Forest", new ConfigDescription("The map TestMode puts every race on.", new AcceptableValueList<string>("Forest", "Mountain", "Factory", "Tokyo", "Circuit")));
			ConfigEntry<bool> forest = ctx.Config.Bind("Map", "Forest", true, "Add the forest lap to the run map's pool. In a Multiplayer room every player needs the same setting. Read at game start.");
			ConfigEntry<bool> mountain = ctx.Config.Bind("Map", "Mountain", true, "Add the mountain lap (a road across the crater floor of the Winter Mountain) to the run map's pool. New in 0.3.0. In a Multiplayer room every player needs the same setting. Read at game start.");
			ConfigEntry<bool> factory = ctx.Config.Bind("Map", "Factory", true, "Add the factory lap (a road across the Fire Factory's dirt basin) to the run map's pool. New in 0.3.0. In a Multiplayer room every player needs the same setting. Read at game start.");
			ConfigEntry<bool> tokyo = ctx.Config.Bind("Map", "Tokyo", true, "Add the Tokyo lap (a road across the flat city ground inside the skyline) to the run map's pool. New in 0.3.0. In a Multiplayer room every player needs the same setting. Read at game start.");
			Profiles.Forest.Enabled = () => forest.Value;
			Profiles.Mountain.Enabled = () => mountain.Value;
			Profiles.Factory.Enabled = () => factory.Value;
			Profiles.Tokyo.Enabled = () => tokyo.Value;
			SeedOverride = ctx.Config.Bind("Map", "SeedOverride", 0, "Solo only: build the lap from this seed instead of a random one, to replay a track (the seed is written on the sign over the start line and in the log). 0 means not set (a new random seed every race). Ignored in a Multiplayer room, where the host's seed is used.");
			RandomWeather = ctx.Config.Bind("Map", "RandomWeather", true, "Each race on a procedural map is dealt one of the weathers the game has for that scene (the forest: rainy day, rainy night, red ash; the mountain: day, snowy night; the factory: fire, night; the circuit: clear, ash, rainy night). Chosen from the seed, so everyone in a room gets the same. Off, each map keeps the weather it was made with.");
			BenchmarkFactor = ctx.Config.Bind("Map", "BenchmarkFactor", 0.9f, "Time objectives (\"finish under X s\") are Stadium's times scaled by lap length times this. Lower is harder. Tune it from your own times. Changes only your own objectives.");
			ShowBoard = ctx.Config.Bind("Map", "ShowBoard", true, "Put a sign with the seed over the start line.");
			Verbose = ctx.Config.Bind("Debug", "VerboseLog", false, "Write the detailed build log (hall fit, spinner, walls, grass, ...) to the BepInEx log. Off, only each race's seed, warnings and errors are logged.");
			ShowPath = ctx.Config.Bind("Debug", "ShowPath", false, "Draw the sampled track line the AI and the lap counter follow.");

			MapEntry.Ensure();
			ctx.OnSceneLoaded(scene =>
			{
				if (scene.name == "SCENE_UI_MAIN_MENU")
				{
					MultiplayerLeave.Clear();
				}
				MapEntry.Ensure();
				CircuitScene.OnSceneLoaded(scene);
				TerrainScene.OnSceneLoaded(scene);
			});
			ctx.OnUnload(MapEntry.Remove);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			CircuitScene.Unload();
			TerrainScene.Unload();
		}
	}
}
