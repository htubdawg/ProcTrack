using System;
using System.Reflection;

namespace HalfGripProcTrack
{
	/// <summary>
	/// The Multiplayer mod refuses every scene load except the main menu for 20 s after you leave a room from a race, and the garage counts
	/// as "not the menu". Start the next race within those 20 s and the garage opens, then the track load is refused and the loading screen
	/// never ends. Once the main menu is up that window is over, so it is closed here. Done by reflection; if the member is not there
	/// (another Multiplayer version) nothing happens.
	/// </summary>
	internal static class MultiplayerLeave
	{
		private static FieldInfo field;
		private static bool looked;

		internal static void Clear()
		{
			try
			{
				if (!looked)
				{
					looked = true;
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
							field = type.GetField("leavingUntil", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
							break;
						}
					}
				}
				if (field != null && field.FieldType == typeof(float) && (float)field.GetValue(null) != 0f)
				{
					field.SetValue(null, 0f);
					Mod.Info("Closed the Multiplayer mod's leaving window.");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not close the Multiplayer leaving window: " + e.Message);
			}
		}
	}
}
