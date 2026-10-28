using System;
using System.Reflection;

namespace HalfGripProcTrack
{
	/// <summary>
	/// Where the seed of a race comes from.
	/// In a Multiplayer room: the host's seed. The Multiplayer mod's start message already carries one number for the whole room, and every
	/// player's game stores it in Multiplayer.Room.Seed before the scene loads. That class is internal to another mod, so it is read by
	/// reflection and the Multiplayer mod stays untouched. If it is not there, or its members were renamed, this falls back to solo seeds
	/// and says so in the log. Players in one room would then get different tracks.
	/// Solo: SeedOverride if set, else a random number.
	/// </summary>
	internal static class SeedSource
	{
		private const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		private static Type roomType;
		private static FieldInfo seedField;
		private static PropertyInfo inRaceProperty;
		private static bool reportedMissing;
		private static bool haveRandom;
		private static int randomSeed;

		/// <summary>
		/// A random solo seed is drawn once and kept until <see cref="Forget"/>, so the weather (picked when the scene is chosen) and the lap
		/// (built when it has loaded) come from the same seed.
		/// </summary>
		internal static void Forget()
		{
			haveRandom = false;
		}

		internal static int Resolve(out string label)
		{
			if (InRoomRace(out int roomSeed))
			{
				label = "ROOM";
				return roomSeed;
			}
			if (Mod.SeedOverride.Value != 0)
			{
				label = "SET IN SETTINGS";
				return Mod.SeedOverride.Value;
			}
			label = "RANDOM";
			if (!haveRandom)
			{
				randomSeed = (Environment.TickCount * 31) ^ new Random().Next(1, int.MaxValue);
				if (randomSeed == 0)
				{
					randomSeed = 1;
				}
				haveRandom = true;
			}
			return randomSeed;
		}

		private static bool InRoomRace(out int seed)
		{
			seed = 0;
			try
			{
				if (roomType == null)
				{
					foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						Type type = null;
						try
						{
							type = assembly.GetType("Multiplayer.Room", false);
						}
						catch (Exception)
						{
						}
						if (type != null)
						{
							roomType = type;
							break;
						}
					}
					if (roomType == null)
					{
						return false;   // no Multiplayer mod loaded, so no room
					}
					seedField = roomType.GetField("Seed", Any);
					inRaceProperty = roomType.GetProperty("InRace", Any);
					if (seedField == null || inRaceProperty == null)
					{
						Mod.Log.LogWarning("Found Multiplayer.Room but not its Seed field and InRace property. A Multiplayer update may have renamed them. In a room, every player will get a different track until this mod is updated.");
					}
					else
					{
						Mod.Info("Multiplayer mod found: in a room, the host's seed is used.");
					}
				}
				if (seedField == null || inRaceProperty == null)
				{
					return false;
				}
				if (!(bool)inRaceProperty.GetValue(null, null))
				{
					return false;
				}
				seed = (int)seedField.GetValue(null);
				return true;
			}
			catch (Exception e)
			{
				if (!reportedMissing)
				{
					reportedMissing = true;
					Mod.Log.LogWarning("Could not read the Multiplayer room seed: " + e.Message);
				}
				return false;
			}
		}
	}
}
