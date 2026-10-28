using System;
using System.Linq;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// Adds the maps to <c>RunConfig.maps</c>. Same method as the Highway mod: a copy of a real map's entry with its own name and track
	/// index. The circuit copies Stadium's; each terrain map (see Profiles) copies its own scene's.
	/// </summary>
	internal static class MapEntry
	{
		/// <summary>The scene the circuit is built in: Stadium's.</summary>
		internal static int HostTrack;

		/// <summary>Stadium's lap is about 1457 m; times scale with the ratio of lap lengths.</summary>
		private const float StadiumLap = 1457f;

		/// <summary>The index the circuit reports as its track, so its records and neighbour rules do not mix with Stadium's or Highway's (20).</summary>
		private const int TrackId = 21;

		/// <summary>The weathers the circuit is dealt from: clear, and the ones the game ships for the Stadium.</summary>
		private static readonly System.Collections.Generic.List<WeatherProfile> circuitWeathers = new System.Collections.Generic.List<WeatherProfile>();

		private static readonly System.Collections.Generic.HashSet<string> reported = new System.Collections.Generic.HashSet<string>();

		internal static void Ensure()
		{
			try
			{
				RunConfig config = RunConfig.Instance;
				if (config == null || config.maps == null)
				{
					return;
				}
				if (Mod.Map == null)
				{
					MapDefinition source = config.FindMap("StadiumAsh") ?? config.maps.FirstOrDefault(m => m != null);
					if (source == null)
					{
						Mod.Log.LogWarning("The run config has no map to copy, the circuit is not added.");
						return;
					}
					Mod.Map = Make(source);
					AddWeathers(config, circuitWeathers, HostTrack, Mod.Map);
					Mod.Info("Procedural circuit map made from " + source.name + " (host track " + HostTrack + ", benchmark " + Mod.Map.timeEarly.ToString("0") + "/" + Mod.Map.timeMid.ToString("0") + "/" + Mod.Map.timeLate.ToString("0") + " s)");
				}
				// Made either way, so TestMode can still put races on it; only added to the pool when the setting is on.
				if (Mod.Circuit.Value && !config.maps.Contains(Mod.Map))
				{
					config.maps.Add(Mod.Map);
					Mod.Info("Procedural circuit added to the run config (" + config.maps.Count + " maps)");
				}

				foreach (MapProfile p in Profiles.All)
				{
					// A map that is switched off in the settings is still made when TestMode names it, so it can be tried without adding it to the pool.
					bool wanted = p.Enabled() || (Mod.TestMode.Value && Mod.TestMap.Value == p.Key);
					if (!wanted)
					{
						continue;
					}
					if (p.Map == null)
					{
						MapDefinition source = config.FindMap(p.SourceMap)
							?? config.maps.FirstOrDefault(m => m != null && m != Mod.Map && Profiles.For(m) == null && m.name.StartsWith(p.SourcePrefix, StringComparison.OrdinalIgnoreCase));
						if (source == null)
						{
							if (reported.Add(p.Key))
							{
								Mod.Log.LogWarning("The run config has no " + p.Key + " map to copy, the procedural " + p.Key + " is not added.");
							}
							continue;
						}
						p.HostTrack = source.trackIndex;
						// The lap keeps its source's weather as it is: the fog and the light are most of each map's look.
						p.Map = Copy(source, p.MapName, p.Display, p.TrackId, (float)p.Lap / p.SourceLap);
						AddWeathers(config, p.Weathers, p.HostTrack, p.Map);
						Mod.Info("Procedural " + p.Key + " map made from " + source.name + " (host track " + p.HostTrack + ", benchmark " + p.Map.timeEarly.ToString("0") + "/" + p.Map.timeMid.ToString("0") + "/" + p.Map.timeLate.ToString("0") + " s)");
					}
					if (!config.maps.Contains(p.Map) && p.Enabled())
					{
						config.maps.Add(p.Map);
						Mod.Info("Procedural " + p.Key + " added to the run config (" + config.maps.Count + " maps)");
					}
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not add the procedural circuit map: " + e);
			}
		}

		internal static void Remove()
		{
			RunConfig config = RunConfig.Instance;
			if (config != null)
			{
				config.maps.RemoveAll(m => m != null && (m == Mod.Map || Profiles.For(m) != null));
			}
		}

		/// <summary>Fills <paramref name="pool"/> with the map's own weather, then each other weather the game's maps of the same scene use.</summary>
		private static void AddWeathers(RunConfig config, System.Collections.Generic.List<WeatherProfile> pool, int hostTrack, MapDefinition ours)
		{
			pool.Clear();
			if (ours.weather != null)
			{
				pool.Add(ours.weather);
			}
			foreach (MapDefinition other in config.maps)
			{
				if (other == null || other == ours || other == Mod.Map || Profiles.For(other) != null || other.trackIndex != hostTrack || other.weather == null)
				{
					continue;
				}
				if (!pool.Contains(other.weather))
				{
					pool.Add(other.weather);
				}
			}
			Mod.Info(ours.displayName + ": " + pool.Count + " weathers to deal from (" + string.Join(", ", pool.Select(w => w.name).ToArray()) + ")");
		}

		/// <summary>
		/// Gives the map one of its scene's weathers, chosen from the seed. Called when the scene to load is asked for, which is before it
		/// loads and so before the weather is read. In a room the seed is the host's, so every player gets the same weather.
		/// </summary>
		internal static void PickWeather(MapDefinition map)
		{
			if (!Mod.RandomWeather.Value)
			{
				return;
			}
			System.Collections.Generic.List<WeatherProfile> pool = map == Mod.Map ? circuitWeathers : Profiles.For(map)?.Weathers;
			if (pool == null || pool.Count < 2)
			{
				return;
			}
			int seed = SeedSource.Resolve(out string label);
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0xBADC0DEUL);
			}
			WeatherProfile pick = pool[(int)(rng.Next() * pool.Count)];
			if (map.weather != pick)
			{
				map.weather = pick;
				Mod.Log.LogInfo(map.displayName + ": weather " + pick.name + " (seed " + seed + ", " + label + ")");
			}
		}

		private static MapDefinition Make(MapDefinition source)
		{
			HostTrack = source.trackIndex;
			MapDefinition map = Copy(source, "ProcCircuit", "Procedural Circuit", TrackId, (float)Generator.TargetLength / StadiumLap);

			if (source.weather != null)
			{
				WeatherProfile weather = UnityEngine.Object.Instantiate(source.weather);
				weather.hideFlags = HideFlags.HideAndDontSave;
				weather.rain = false;
				weather.snow = false;
				weather.ash = false;
				weather.fogActive = false;
				weather.headlightsActive = false;
				weather.gripExponent = 1f;
				weather.gripMultiplier = 1f;
				map.weather = weather;
			}

			return map;
		}

/// <summary>The source map's own icon turned upside down and colourised (a colour per procedural map), so each procedural map is recognisable next to the original. Null if the icon cannot be read.</summary>
		private static Sprite MakeIcon(Sprite src, string name)
		{
			if (src == null || src.texture == null)
			{
				return null;
			}
			try
			{
				Color tint = name == "ProcForest" ? new Color(0.45f, 0.95f, 0.4f)
					: name == "ProcMountain" ? new Color(0.45f, 0.8f, 1f)
					: name == "ProcFactory" ? new Color(1f, 0.45f, 0.15f)
					: name == "ProcTokyo" ? new Color(1f, 0.35f, 0.85f)
					: new Color(1f, 0.8f, 0.2f);
				Texture2D tex = src.texture;
				Rect r = src.textureRect;
				int w = Mathf.Max(1, Mathf.RoundToInt(r.width));
				int h = Mathf.Max(1, Mathf.RoundToInt(r.height));
				RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
				Graphics.Blit(tex, rt);
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = rt;
				Texture2D read = new Texture2D(w, h, TextureFormat.RGBA32, false);
				read.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
				read.Apply();
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(rt);
				Color32[] px = read.GetPixels32();
				Color32[] outPx = new Color32[px.Length];
				for (int y = 0; y < h; y++)
				{
					for (int x = 0; x < w; x++)
					{
						Color32 c = px[(h - 1 - y) * w + x];
						float lum = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
						Color tinted = tint * Mathf.Clamp01(lum * 1.5f);
						outPx[y * w + x] = new Color32(
							(byte)Mathf.Clamp(Mathf.Lerp(c.r, tinted.r * 255f, 0.65f), 0f, 255f),
							(byte)Mathf.Clamp(Mathf.Lerp(c.g, tinted.g * 255f, 0.65f), 0f, 255f),
							(byte)Mathf.Clamp(Mathf.Lerp(c.b, tinted.b * 255f, 0.65f), 0f, 255f),
							c.a);
					}
				}
				read.SetPixels32(outPx);
				read.Apply();
				read.hideFlags = HideFlags.HideAndDontSave;
				Sprite made = Sprite.Create(read, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
				made.hideFlags = HideFlags.HideAndDontSave;
				return made;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not make a coloured icon from " + src.name + ": " + e.Message);
				return null;
			}
		}

		/// <summary>A copy of a map entry with its own name and track index, no mirrored or reversed variants, and times scaled by <paramref name="lapRatio"/>.</summary>
		private static MapDefinition Copy(MapDefinition source, string name, string displayName, int trackId, float lapRatio)
		{
			MapDefinition map = UnityEngine.Object.Instantiate(source);
			map.hideFlags = HideFlags.HideAndDontSave;
			map.name = name;
			map.displayName = displayName;
			map.conditions = string.Empty;
			map.trackIndex = trackId;
			map.allowMirror = false;
			map.allowReverse = false;
			map.allowMirrorReverse = false;
			map.mirrorIcon = null;
			map.reverseIcon = null;
			map.mirrorReverseIcon = null;

			float scale = lapRatio * Mod.BenchmarkFactor.Value;
			map.timeEarly = source.timeEarly * scale;
			map.timeMid = source.timeMid * scale;
			map.timeLate = source.timeLate * scale;

Sprite icon = MakeIcon(source.icon, name);
			if (icon == null)
			{
				icon = Mod.Ctx.Sprites.LoadSprite("proctrack_icon.png", source.icon);
			}
			if (icon != null)
			{
				map.icon = icon;
			}
			return map;
		}
	}
}
