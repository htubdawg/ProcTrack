using HarmonyLib;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>The maps have their own track indices; the scene to load is Stadium's for the circuit, and each terrain map's own source scene.</summary>
	[HarmonyPatch(typeof(MenuManager), "TrackIndex")]
	internal static class TrackIndexPatch
	{
		private static bool Prefix(ref int __result)
		{
			if (!RunState.Active || !RunState.HasSelection)
			{
				return true;
			}
			MapDefinition map = RunState.Selected.map;
			if (Mod.Map != null && map == Mod.Map)
			{
				MapEntry.PickWeather(map);
				__result = MapEntry.HostTrack;
				return false;
			}
			MapProfile profile = Profiles.For(map);
			if (profile != null)
			{
				MapEntry.PickWeather(map);
				__result = profile.HostTrack;
				return false;
			}
			return true;
		}
	}

	/// <summary>The game spawns rivals at the Stadium's ground height; on a terrain lap they are lifted onto the road.</summary>
	[HarmonyPatch(typeof(TrackManager), "SpawnEnemies")]
	internal static class SpawnEnemiesPatch
	{
		private static void Postfix(TrackManager __instance)
		{
			if (TerrainScene.Active)
			{
				TerrainScene.LiftRivals(__instance);
			}
		}
	}

	internal static class TestTarget
	{
		/// <summary>The map TestMode puts races on: the terrain map TestMap names if it exists, else the circuit.</summary>
		internal static MapDefinition Map
		{
			get
			{
				foreach (MapProfile p in Profiles.All)
				{
					if (Mod.TestMap.Value == p.Key && p.Map != null)
					{
						return p.Map;
					}
				}
				return Mod.Map;
			}
		}
	}

	/// <summary>TestMode: whatever node the player picked, the race is on the test map.</summary>
	[HarmonyPatch(typeof(RunState), nameof(RunState.BeginRace))]
	internal static class TestModePatch
	{
		private static void Prefix()
		{
			MapDefinition test = TestTarget.Map;
			if (Mod.TestMode.Value && test != null && RunState.HasSelection && !RunState.Over)
			{
				RunState.Selected.map = test;
				RunState.Selected.variant = TrackVariant.Normal;
			}
		}
	}

	/// <summary>TestMode: a new run's whole map is the test map, with time limits that fit its length.</summary>
	[HarmonyPatch(typeof(RunMap), nameof(RunMap.Generate))]
	internal static class TestModeMapPatch
	{
		private static void Postfix(RunConfig config, RunMap __result)
		{
			MapDefinition test = TestTarget.Map;
			if (!Mod.TestMode.Value || test == null || __result == null || config == null)
			{
				return;
			}
			int columns = Mathf.Max(1, __result.columns.Count);
			foreach (RunNode node in __result.nodes)
			{
				node.map = test;
				node.variant = TrackVariant.Normal;
				node.unknownMap = false;
				if (node.objective.kind == ObjectiveKind.Time)
				{
					float limit = test.TimeLimit(Mathf.Min(2, node.column * 3 / columns));
					node.objective.amount = limit > 0f ? Mathf.CeilToInt(limit * config.timeLimitMargin) : 0;
				}
			}
			Mod.Info("TestMode: every node of the new run map is " + test.displayName);
		}
	}
}
