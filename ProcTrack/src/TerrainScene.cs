using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripProcTrack
{
	/// <summary>
	/// Turns one of the game's terrain scenes (Forest Field, the Winter Mountain, the Fire Factory, the Tokyo Temple) into a procedural
	/// lap when a race on one of the procedural terrain maps loads. Where the Stadium circuit lays meshes on a flat slab, this cuts the
	/// lap into the scene's terrain: the road is levelled along the lap, and is either painted on the terrain (Forest Field, whose own road
	/// is painted terrain with its road mesh switched off, and the Factory) or a mesh laid on it (the Mountain and Tokyo, whose own roads
	/// are meshes). What differs between the scenes is in <see cref="MapProfile"/>; the steps are the same.
	///
	/// The terrain is never edited in place. The data on the terrain at load is FellableTerrain's runtime copy, but nothing promises it
	/// is not the shared asset, and an edit to that would carry over into the next race on the real Forest map. The data is cloned, the
	/// clone is edited and handed to the terrain and its collider: what the game's TrackVariantApplier and FellableTerrain also do.
	///
	/// Steps: a lap from the seed, placed where the ground is gentlest and least wooded; road heights from the ground, smoothed and slope
	/// limited, the start straight dead level; the terrain levelled under the road and banked back to the old ground; the old road paint,
	/// grass and wheat wiped, and new ones laid along the new road with a few random wheat fields; trees on the road taken out, trees on
	/// the banks moved down or up to the new ground; FellableTerrain rebuilt so the trunks match the trees; then the track line, the grid
	/// and the hazards, as on the circuit. Everything comes from the seed and the terrain asset, so every player in a room cuts the same lap.
	/// </summary>
	internal static partial class TerrainScene
	{
		/// <summary>The profile of the map being raced; null when no procedural terrain map is loaded.</summary>
		internal static MapProfile P;

		/// <summary>Half the width of the road.</summary>
		private static float RoadHalfWidth => P.Half;

		/// <summary>Flat ground on each side of the road before the bank starts.</summary>
		private static float Shoulder => P.Shoulder;

		/// <summary>The bank back to the old ground runs 2.5 across for every 1 up or down, and is never narrower than MinBank or wider than MaxBank.</summary>
		private static float BankSlope => P.BankSlope;
		private static float MinBank => P.MinBank;
		private static float MaxBank => P.MaxBank;

		/// <summary>Trees closer than this to the road edge are taken out.</summary>
		private const float TreeClear = 8f;

		/// <summary>The steepest the road gets, rise over run.</summary>
		private const float MaxGrade = 0.09f;

		/// <summary>The lap stays this far inside the terrain's edge, so its banks do too.</summary>
		private const float EdgeMargin = 100f;

		/// <summary>The road is dead level from this far behind the line to LevelAhead past it: the grid, the semaphore and the line.</summary>
		private const float LevelBehind = 200f;
		private const float LevelAhead = 60f;

		/// <summary>Placement: one tree in the way costs as much as this many metres of climb.</summary>
		private const float TreeWeight = 0.3f;

		/// <summary>Placement: each metre the lap's middle is from the old track's middle costs this much. Without it the flat, bare ground at the map's edge wins, outside the forest.</summary>
		private const float HomeWeight = 1.5f;

		/// <summary>
		/// Placement: each 10 m of road within BackdropClear of a tree line costs this much. The tree lines (Decoration/TreeLines) are flat
		/// sprites with no collider, the backdrop ringing the map; a road through one is driven through a wall of painted trees.
		/// </summary>
		private const float BackdropWeight = 400f;
		private static float BackdropClear => RoadHalfWidth + 15f;
		private const float SearchStep = 20f;

		/// <summary>A wheat field keeps at least FieldGap from the road edge, comes within FieldNear of it somewhere, and clears no more than FieldTrees trees.</summary>
		private const float FieldGap = 25f;
		private const float FieldNear = 70f;
		private const int FieldTrees = 100;

		/// <summary>How far from the middle of the road the terrain is changed.</summary>
		private static float Reach => RoadHalfWidth + Shoulder + MaxBank;

		private static GameObject root;
		private static readonly List<GameObject> hidden = new List<GameObject>();
		private static Collider rainPlane;
		private static Terrain terrain;
		private static TerrainCollider terrainCollider;
		private static TerrainData original;
		private static TerrainData clone;
		private static bool warnedHandling;
		private static Vector3 heading;
		private static float lineLift = 0.08f;

		/// <summary>True while the forest lap is built in the loaded scene.</summary>
		internal static bool Active => root != null && terrain != null;

		// ---- load / unload -----------------------------------------------------------------------------------------

		internal static void OnSceneLoaded(Scene scene)
		{
			root = null;
			hidden.Clear();
			rainPlane = null;
			terrain = null;
			terrainCollider = null;
			original = null;
			P = null;
			if (clone != null)
			{
				// Its terrain went with the last scene.
				UnityEngine.Object.Destroy(clone);
				clone = null;
			}

MapProfile profile = Profiles.For(RunState.ActiveMap);
			if (profile == null)
			{
				if (RunState.ActiveMap != null && RunState.ActiveMap.name != null && RunState.ActiveMap.name.StartsWith("Proc"))
				{
					Mod.Log.LogInfo("Scene " + scene.name + " loaded with run map " + RunState.ActiveMap.name + " (not a terrain map of ours).");
				}
				return;
			}
TrackManager tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
			if (tm == null || tm.playerCarController == null || tm.lapManager == null || tm.lapManager.trackLine == null)
			{
				Mod.Log.LogWarning(profile.Key + ": scene " + scene.name + " loaded but its race objects are not ready (track manager " + (tm == null ? "missing" : "found") + (tm != null ? ", car " + (tm.playerCarController != null) + ", laps " + (tm.lapManager != null) + ", line " + (tm.lapManager != null && tm.lapManager.trackLine != null) : "") + "): the map is not built.");
				return;
			}
			P = profile;
			Mod.Log.LogInfo(profile.Key + ": building in scene " + scene.name + "...");
			System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				Build(scene, tm);
				Mod.Log.LogInfo(profile.Key + ": built in " + clock.ElapsedMilliseconds + " ms");
			}
			catch (Exception e)
			{
				Mod.Log.LogError("The procedural " + profile.Key + " map could not be built, the scene is put back as it was: " + e);
				Unload();
			}
		}

		internal static void Unload()
		{
			if (root != null)
			{
				UnityEngine.Object.Destroy(root);
			}
			foreach (GameObject go in hidden)
			{
				if (go != null)
				{
					go.SetActive(true);
				}
			}
			if (rainPlane != null)
			{
				rainPlane.enabled = true;
			}
			if (terrain != null && original != null && terrain.terrainData != original)
			{
				SwapData(original);
			}
			hidden.Clear();
			planned.Clear();
			root = null;
		}

		// ---- building ----------------------------------------------------------------------------------------------

		private static void Build(Scene scene, TrackManager tm)
		{
			GameObject env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ENVIROMENT");
			if (env == null)
			{
				throw new InvalidOperationException("this scene has no ENVIROMENT object, so it is not the " + P.Key + " scene");
			}
			Transform terrainObject = FindFirst(env.transform, P.TerrainPath);
			terrain = terrainObject != null ? terrainObject.GetComponent<Terrain>() : UnityEngine.Object.FindFirstObjectByType<Terrain>();
			if (terrain == null || terrain.terrainData == null)
			{
				throw new InvalidOperationException("this scene has no terrain, so it is not the " + P.Key + " scene");
			}
			terrainCollider = terrain.GetComponent<TerrainCollider>();
original = terrain.terrainData;
preExpand = null;
			if (P.Expand > 1f)
			{
				original = ExpandTerrain(original, P.Expand, terrain, out Vector3 bigAt);
				terrain.terrainData = original;
				if (terrainCollider != null)
				{
					terrainCollider.terrainData = original;
				}
				terrain.transform.position = bigAt;
				Physics.SyncTransforms();
			}

			// The new lap starts where the old one did and heads the same way, so the grid slots keep their heading.
			PathManager oldLine = tm.lapManager.trackLine;
			if (oldLine.sampledPoints == null || oldLine.sampledPoints.Count < 2)
			{
				throw new InvalidOperationException("the scene's track line has no samples");
			}
			Vector3 oldStart = oldLine.sampledPoints[0];
			// Copied now: the track line is rebuilt for the new lap further down.
			List<Vector3> oldSamples = new List<Vector3>(oldLine.sampledPoints);
			Vector3 home = Vector3.zero;
			foreach (Vector3 p in oldLine.sampledPoints)
			{
				home += p;
			}
			home /= oldLine.sampledPoints.Count;
			Vector3 forward = oldLine.sampledPoints[1] - oldStart;
			forward.y = 0f;
			forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.right;
			// TrackManager.Awake has already put the car on its slot, facing the slot's heading. The grid slots are moved, not turned.
			Vector3 facing = tm.playerCarController.transform.forward;
			facing.y = 0f;
			float off = Vector3.Angle(facing, forward);
			if (off > 15f)
			{
				Mod.Log.LogWarning("The car faces " + off.ToString("0") + " degrees away from the old track's start: the grid may stand across the new straight.");
			}

			int seed = SeedSource.Resolve(out string label);
			SeedSource.Forget();
			DateTime started = DateTime.UtcNow;
float[,] rawHeights = original.GetHeights(0, 0, original.heightmapResolution, original.heightmapResolution);
			// What the old scene's objects stood on, for carrying them over; the ground the new lap is placed on and cut into is roughened.
Ground oldGround = preExpand ?? new Ground(terrain.GetPosition(), original.size, (float[,])rawHeights.Clone());
			if (P.Roughness > 0f)
			{
				Roughen(rawHeights, original.size, seed);
			}
			Ground ground = new Ground(terrain.GetPosition(), original.size, rawHeights);
			TreeInstance[] trees = original.treeInstances;
			double room = Math.Min(ground.SizeX, ground.SizeZ) - 2.0 * EdgeMargin;
			Shape shape = new Shape
			{
				Length = P.Lap,
				StraightMin = P.StraightMin,
				StraightMax = P.StraightMax,
				Wobble = P.Wobble,
				Harmonics = P.Harmonics,
				AmpCap = P.AmpCap,
				MinRadius = P.MinRadius,
				Separation = P.Separation,
				BackStraight = P.BackStraight,
				NoMirror = P.NoMirror,
Bumps = P.Bumps,
				HairpinRadius = P.HairpinRadius,
Taper = P.Taper,
				TaperLow = P.TaperLow,
TaperSpread = P.TaperSpread,
				FlareRadius = P.FlareRadius,
				Neck = P.Neck,
				MaxSpan = P.SpanX > 0.0 ? Math.Min(P.SpanX, ground.SizeX - 2.0 * EdgeMargin) : room
			};
			shape.MaxSpanZ = P.SpanZ > 0.0 ? Math.Min(P.SpanZ, ground.SizeZ - 2.0 * EdgeMargin) : (P.SpanX > 0.0 ? room : double.NaN);
			Layout local = Generator.Make(seed, shape);
			List<Rect> backdrop = Backdrop(env);
			Layout layout = Place(local, ground, trees, backdrop, forward, home, out float score);
			float low = layout.Points.Min(p => p.y);
			float high = layout.Points.Max(p => p.y);
			Mod.Log.LogInfo(P.Key + " seed " + seed + " (" + label + "): lap " + layout.Length.ToString("0") + ", tightest corner radius " + (layout.Fallback ? "n/a (plain shape)" : layout.MinRadius.ToString("0")) + ", drawn on try " + (layout.Attempt + 1) + ", placed at score " + score.ToString("0") + ", road from " + low.ToString("0.0") + " to " + high.ToString("0.0") + " high");

			root = new GameObject("Proc" + P.Key);
			lineLift = 0.08f;
			Transform roadSource = P.RoadFrom != null ? FindFirst(env.transform, P.RoadFrom) : null;
			if (P.RoadFrom != null && roadSource == null)
			{
				Mod.Log.LogWarning("The " + P.Key + " scene has no road object at " + P.RoadFrom + ": the new road is not drawn.");
			}


			// Copies of the old track's hazards are made before anything is hidden, and put on the ground once the terrain is final.
PlanTransplants(env, layout, oldSamples, oldGround, seed);
			// The tunnel is chosen before the markers, so no sign or barrel is put inside it. (It keeps clear of the hazards, not of the markers, so the order does not move it.)
			tunnelFrom = -1;
			if (P.Tunnel)
			{
				PlanTunnel(layout, seed);
			}
			if (P.Markers)
			{
				PlanMarkers(env, layout, oldSamples);
			}
			if (roadSource != null)
			{
				// Cloned before the old road is hidden, so the copy starts out switched on. Built after the road is levelled for the tunnel.
				BuildMeshRoad(roadSource, layout);
				lineLift = 0.13f;
			}

			HideOldTrack(scene, env, layout);

			clone = UnityEngine.Object.Instantiate(original);
			clone.name = original.name + " (ProcTrack)";
			Cut(clone, ground, trees, layout, seed);
			SwapData(clone);
			LandTransplants();

			Mod.Info(P.Key + " terrain cut in " + (DateTime.UtcNow - started).TotalMilliseconds.ToString("0") + " ms");

			Transform groundObject = env.transform.Find("Ground");
			int layer = groundObject != null ? groundObject.gameObject.layer : terrain.gameObject.layer;
			Material source = groundObject != null && groundObject.TryGetComponent(out MeshRenderer groundRenderer) ? groundRenderer.sharedMaterial : null;
			BuildLine(layout, source, layer);
			if (Mod.ShowBoard.Value)
			{
				Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.right, Vector3.up));
				CircuitScene.BuildBoard(root.transform, layout.Points[0], yaw, RoadHalfWidth, "PROCEDURAL " + P.Key.ToUpperInvariant(), seed, label, source, layer);
			}

if (P.Tunnel)
			{
				BuildTunnel(env, layout, seed, source);
			}
if (P.EdgeWall)
			{
				BuildEdgeWall(ground);
			}
			if (P.HairpinWall)
			{
				BuildHairpinWall(layout, source);
			}
			if (P.Yard)
			{
				BuildYard(layout, seed, source);
			}
			if (P.Walls)
			{
				BuildCity(env, layout, seed, source);
			}
			if (P.Tornado)
			{
				PlaceTornado(layout, seed);
			}

			heading = forward;
			CircuitScene.Rewire(tm, layout, RoadHalfWidth, hidden);
			PlaceGrid(tm, layout, forward, oldStart);
			CircuitScene.SpreadHazards(layout, seed, root.transform);
		}

		// ---- placing the lap ---------------------------------------------------------------------------------------

		/// <summary>
		/// Turns the lap to <paramref name="forward"/>, slides it over the terrain in SearchStep steps and keeps the spot with the lowest
		/// score: metres climbed, plus half the fall across the road, plus TreeWeight for each tree in the way, plus twice the rise along
		/// the start straight, plus HomeWeight for each metre between the lap's middle and <paramref name="home"/>. Then gives every point
		/// its road height.
		/// </summary>
		private static Layout Place(Layout local, Ground g, TreeInstance[] trees, List<Rect> backdrop, Vector3 forward, Vector3 home, out float best)
		{
			int n = local.Count;
			Vector3 right = new Vector3(forward.z, 0f, -forward.x);
			Vector3[] flat = new Vector3[n];
			Vector3[] side = new Vector3[n];
			float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
			Vector3 middle = Vector3.zero;
			for (int i = 0; i < n; i++)
			{
				// The generator's lap heads +X with its right at -Z: local x runs along forward, local z along the left.
				Vector3 p = local.Points[i];
				flat[i] = forward * p.x - right * p.z;
				Vector3 r = local.Right(i);
				side[i] = forward * r.x - right * r.z;
				minX = Math.Min(minX, flat[i].x);
				maxX = Math.Max(maxX, flat[i].x);
				minZ = Math.Min(minZ, flat[i].z);
				maxZ = Math.Max(maxZ, flat[i].z);
				middle += flat[i];
			}
			middle /= n;

			float loX = g.X0 + EdgeMargin - minX;
			float hiX = g.X0 + g.SizeX - EdgeMargin - maxX;
			float loZ = g.Z0 + EdgeMargin - minZ;
			float hiZ = g.Z0 + g.SizeZ - EdgeMargin - maxZ;
			if (loX > hiX)
			{
				Mod.Log.LogWarning("The lap is wider than the terrain allows, it is centred and may run off the edge.");
				loX = hiX = (loX + hiX) * 0.5f;
			}
			if (loZ > hiZ)
			{
				Mod.Log.LogWarning("The lap is deeper than the terrain allows, it is centred and may run off the edge.");
				loZ = hiZ = (loZ + hiZ) * 0.5f;
			}

			TreeGrid wood = new TreeGrid(g, trees);
			int behind = Mathf.RoundToInt(LevelBehind / local.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / local.Step);
			int stepsX = (int)((hiX - loX) / SearchStep);
			int stepsZ = (int)((hiZ - loZ) / SearchStep);
			best = float.MaxValue;
			float bestX = loX;
			float bestZ = loZ;
			for (int ix = 0; ix <= stepsX; ix++)
			{
				for (int iz = 0; iz <= stepsZ; iz++)
				{
					float tx = loX + ix * SearchStep;
					float tz = loZ + iz * SearchStep;
					float dx = middle.x + tx - home.x;
					float dz = middle.z + tz - home.z;
					float s = Score(flat, side, tx, tz, g, wood, backdrop, behind, ahead) + HomeWeight * Mathf.Sqrt(dx * dx + dz * dz);
					if (s < best)
					{
						best = s;
						bestX = tx;
						bestZ = tz;
					}
				}
			}

			Vector3 offset = new Vector3(bestX, 0f, bestZ);
			float[] y = Profile(flat, side, offset, g, behind, ahead);
			Vector3[] points = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				points[i] = new Vector3(flat[i].x + offset.x, y[i], flat[i].z + offset.z);
			}
			return new Layout
			{
				Points = points,
				Length = local.Length,
				Step = local.Step,
				Seed = local.Seed,
				Attempt = local.Attempt,
				MinRadius = local.MinRadius,
				Fallback = local.Fallback
			};
		}

		private static float Score(Vector3[] flat, Vector3[] side, float tx, float tz, Ground g, TreeGrid wood, List<Rect> backdrop, int behind, int ahead)
		{
			int n = flat.Length;
			float w = RoadHalfWidth + 12f;
			float climb = 0f;
			float across = 0f;
			float trees = 0f;
			float startLow = float.MaxValue;
			float startHigh = float.MinValue;
			int blocked = 0;
			float first = 0f;
			float prev = 0f;
			for (int i = 0; i < n; i += 2)
			{
				float x = flat[i].x + tx;
				float z = flat[i].z + tz;
				float c = g.At(x, z);
				if (i == 0)
				{
					first = c;
				}
				else
				{
					climb += Math.Abs(c - prev);
				}
				prev = c;
				across += Math.Abs(g.At(x - side[i].x * w, z - side[i].z * w) - g.At(x + side[i].x * w, z + side[i].z * w));
				trees += wood.Count(x, z);
				if (P.RingR > 0f)
				{
					float rx = x - P.RingX;
					float rz = z - P.RingZ;
					float inner = P.RingR - RoadHalfWidth - Shoulder - 20f;
					if (rx * rx + rz * rz > inner * inner)
					{
						blocked++;
					}
				}
				foreach (Rect r in backdrop)
				{
					if (r.Contains(new Vector2(x, z)))
					{
						blocked++;
						break;
					}
				}
				if (i <= ahead || i >= n - behind)
				{
					startLow = Math.Min(startLow, c);
					startHigh = Math.Max(startHigh, c);
				}
			}
			climb += Math.Abs(first - prev);
			return P.ClimbWeight * climb + P.AcrossWeight * across + TreeWeight * trees + 2f * (startHigh - startLow) + BackdropWeight * blocked;
		}

		/// <summary>The flat extent of everything in the profile's Avoid groups (each tree line sprite, each building), grown by BackdropClear.</summary>
		private static List<Rect> Backdrop(GameObject env)
		{
			List<Rect> rects = new List<Rect>();
			foreach (string path in P.Avoid)
			{
				foreach (Transform group in FindAll(env.transform, path))
				{
					// A group with children is a set of items; one without is the item.
					List<Transform> items = new List<Transform>();
					foreach (Transform child in group)
					{
						items.Add(child);
					}
					if (items.Count == 0)
					{
						items.Add(group);
					}
					foreach (Transform item in items)
					{
						if (!item.gameObject.activeInHierarchy)
						{
							continue;
						}
						foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
						{
							Bounds b = r.bounds;
							rects.Add(Rect.MinMaxRect(b.min.x - BackdropClear, b.min.z - BackdropClear, b.max.x + BackdropClear, b.max.z + BackdropClear));
						}
					}
				}
			}
			return rects;
		}

		/// <summary>
		/// Road heights: the ground under the middle and both edges, averaged, then smoothed over about 85 m. The start straight is set to
		/// its mean height, and everything else is held to MaxGrade from there on.
		/// </summary>
		private static float[] Profile(Vector3[] flat, Vector3[] side, Vector3 offset, Ground g, int behind, int ahead)
		{
			int n = flat.Length;
			float w = RoadHalfWidth;
			float[] y = new float[n];
			for (int i = 0; i < n; i++)
			{
				float x = flat[i].x + offset.x;
				float z = flat[i].z + offset.z;
				y[i] = (g.At(x, z) + g.At(x - side[i].x * w, z - side[i].z * w) + g.At(x + side[i].x * w, z + side[i].z * w)) / 3f;
			}
			y = Blur(y, 8, 3, null);

			bool[] level = new bool[n];
			float sum = 0f;
			int count = 0;
			for (int i = 0; i < n; i++)
			{
				level[i] = i <= ahead || i >= n - behind;
				if (level[i])
				{
					sum += y[i];
					count++;
				}
			}
			float startHeight = sum / count;
			for (int i = 0; i < n; i++)
			{
				if (level[i])
				{
					y[i] = startHeight;
				}
			}

			// The points are evenly spaced along the generator's plain shape, not along the bent lap: inside a bend they sit closer.
			float[] rise = new float[n];
			for (int i = 1; i < n; i++)
			{
				Vector3 d = flat[i] - flat[i - 1];
				rise[i] = MaxGrade * Mathf.Sqrt(d.x * d.x + d.z * d.z);
			}
			LimitGrade(y, rise, ahead, n - behind);
			y = Blur(y, 2, 2, level);
			LimitGrade(y, rise, ahead, n - behind);
			return y;
		}

		/// <summary>Holds the free stretch between the level ends (indices first + 1 to last - 1) to at most rise[i] between points i - 1 and i.</summary>
		private static void LimitGrade(float[] y, float[] rise, int first, int last)
		{
			for (int pass = 0; pass < 4; pass++)
			{
				for (int i = first + 1; i < last; i++)
				{
					y[i] = Mathf.Clamp(y[i], y[i - 1] - rise[i], y[i - 1] + rise[i]);
				}
				for (int i = last - 1; i > first; i--)
				{
					y[i] = Mathf.Clamp(y[i], y[i + 1] - rise[i + 1], y[i + 1] + rise[i + 1]);
				}
			}
		}

		/// <summary>A moving average round the closed lap. Points marked in <paramref name="keep"/> are not changed.</summary>
		private static float[] Blur(float[] y, int radius, int passes, bool[] keep)
		{
			int n = y.Length;
			float[] a = (float[])y.Clone();
			float[] b = new float[n];
			for (int p = 0; p < passes; p++)
			{
				for (int i = 0; i < n; i++)
				{
					if (keep != null && keep[i])
					{
						b[i] = a[i];
						continue;
					}
					float sum = 0f;
					for (int k = -radius; k <= radius; k++)
					{
						sum += a[((i + k) % n + n) % n];
					}
					b[i] = sum / (2 * radius + 1);
				}
				float[] swap = a;
				a = b;
				b = swap;
			}
			return a;
		}

		// ---- cutting the terrain -----------------------------------------------------------------------------------

		/// <summary>Heights, paint, grass and wheat, and trees of <paramref name="data"/>, a copy of the scene's terrain data.</summary>
		private static void Cut(TerrainData data, Ground g, TreeInstance[] trees, Layout layout, int seed)
		{
			Vector3[] pts = layout.Points;
			float h = RoadHalfWidth;

			// Heights: the road height out to the shoulder, then a bank back to the old ground.
			int res = data.heightmapResolution;
			Field near = new Field(pts, res, res, g.X0, g.Z0, g.SizeX / (res - 1), g.SizeZ / (res - 1), Reach);
			float[,] heights = (float[,])g.Heights.Clone();
			for (int j = 0; j < res; j++)
			{
				for (int i = 0; i < res; i++)
				{
					int k = j * res + i;
					float d = near.Dist[k];
					if (d >= Reach)
					{
						continue;
					}
					float road = near.RoadY[k];
					float old = g.Y0 + g.SizeY * heights[j, i];
					float y;
					if (d <= h + Shoulder)
					{
// Under a drawn road mesh the ground is sunk, so a slope or a bend never lets a cell of it come up through the tarmac.
							float drop = P.RoadFrom != null ? 0.7f * (1f - Mathf.Clamp01((d - h - 1f) / Math.Max(1f, Shoulder - 1f))) : 0f;
							y = road - drop;
					}
					else
					{
						float bank = Mathf.Clamp(Math.Abs(road - old) * BankSlope, MinBank, MaxBank);
						float u = (d - h - Shoulder) / bank;
						if (u >= 1f)
						{
							continue;
						}
						y = Mathf.Lerp(road, old, (float)DetMath.Smooth(u));
					}
					heights[j, i] = Mathf.Clamp01((y - g.Y0) / g.SizeY);
				}
			}
			data.SetHeights(0, 0, heights);
			Ground cut = new Ground(new Vector3(g.X0, g.Y0, g.Z0), new Vector3(g.SizeX, g.SizeY, g.SizeZ), heights);

			// Wheat fields go on flat ground beside the road, in sight of it but off it. The alphamap's field of distances serves both.
			int aw = data.alphamapWidth;
			int ah = data.alphamapHeight;
			Field paintNear = new Field(pts, aw, ah, g.X0, g.Z0, g.SizeX / (aw - 1), g.SizeZ / (ah - 1), Math.Max(Reach, h + FieldNear + 10f));
			List<Patch> fields = P.Wheat ? Fields(seed, g, paintNear, trees) : new List<Patch>();

			if (P.Rebuild)
			{
				// Paint: the base layer everywhere, the road layer on the road, the dirt layer under the wheat. This wipes the old road's paint.
				int layers = data.alphamapLayers;
				int baseLayer = LayerIndex(data, P.BaseLayer, 0);
				int roadLayer = LayerIndex(data, P.RoadLayer, 2);
				int dirtLayer = LayerIndex(data, P.DirtLayer, 3);
				if (baseLayer < layers && roadLayer < layers)
				{
					float[,,] paint = new float[ah, aw, layers];
					for (int j = 0; j < ah; j++)
					{
						for (int i = 0; i < aw; i++)
						{
							float x = paintNear.X0 + i * paintNear.DX;
							float z = paintNear.Z0 + j * paintNear.DZ;
							float d = paintNear.Dist[j * aw + i];
							float road = 1f - Mathf.Clamp01((d - (h - 1.5f)) / 4f);
							float dirt = 0f;
							if (dirtLayer < layers)
							{
								foreach (Patch f in fields)
								{
									dirt = Math.Max(dirt, f.Weight(x, z, 3f));
								}
								dirt *= 1f - road;
								paint[j, i, dirtLayer] += dirt;
							}
							paint[j, i, roadLayer] += road;
							paint[j, i, baseLayer] += 1f - road - dirt;
						}
					}
					data.SetAlphamaps(0, 0, paint);
				}
				else
				{
					Mod.Log.LogWarning("The " + P.Key + " terrain has " + layers + " paint layers, fewer than expected: the road is not painted.");
				}
			}
			else if (P.RoadLayer != null)
			{
				// Keep the scene's own paint and lay the road layer over it, soft at the edges.
				int layers = data.alphamapLayers;
				int roadLayer = LayerIndex(data, P.RoadLayer, -1);
				if (roadLayer >= 0 && roadLayer < layers)
				{
					float[,,] paint = data.GetAlphamaps(0, 0, aw, ah);
					for (int j = 0; j < ah; j++)
					{
						for (int i = 0; i < aw; i++)
						{
							float d = paintNear.Dist[j * aw + i];
							if (d >= h + 5f)
							{
								continue;
							}
							float road = 1f - Mathf.Clamp01((d - (h - 1.5f)) / 4f);
							if (road <= 0f)
							{
								continue;
							}
							for (int l = 0; l < layers; l++)
							{
								paint[j, i, l] *= 1f - road;
							}
							paint[j, i, roadLayer] += road;
						}
					}
					data.SetAlphamaps(0, 0, paint);
				}
			}

			if (P.Rebuild)
			{
				// Grass and wheat: all of the old wiped, grass along both road edges, a sprinkle of tufts beyond, wheat in the fields.
				int dw = data.detailWidth;
				int dh = data.detailHeight;
				int kinds = data.detailPrototypes.Length;
				int wheat = DetailIndex(data, "wheat", false, 0);
				int grass = DetailIndex(data, "grass", true, 1);
				int tufts = DetailIndex(data, "grassW", true, 2);
				int[][,] detail = new int[kinds][,];
				int[] full = new int[kinds];
				for (int l = 0; l < kinds; l++)
				{
					detail[l] = new int[dh, dw];
					int[,] old = data.GetDetailLayer(0, 0, dw, dh, l);
					foreach (int v in old)
					{
						full[l] = Math.Max(full[l], v);
					}
					if (full[l] == 0)
					{
						full[l] = 200;
					}
				}
				Field detailNear = new Field(pts, dw, dh, g.X0 + 0.5f * g.SizeX / dw, g.Z0 + 0.5f * g.SizeZ / dh, g.SizeX / dw, g.SizeZ / dh, h + 32f);
				for (int j = 0; j < dh; j++)
				{
					for (int i = 0; i < dw; i++)
					{
						float x = detailNear.X0 + i * detailNear.DX;
						float z = detailNear.Z0 + j * detailNear.DZ;
						float d = detailNear.Dist[j * dw + i];
						if (wheat >= 0 && InAny(fields, x, z, -2f))
						{
							detail[wheat][j, i] = full[wheat];
						}
						// Grass along both road edges as on the Factory (EdgeGrass): full on the first 7 m, thinning out to 22 m.
						else if (grass >= 0 && d >= h + 0.5f && d <= h + 22f && Hash(i, j, seed + 77) < (d <= h + 7f ? 1f : 0.55f * (1f - (d - h - 7f) / 15f)))
						{
							detail[grass][j, i] = full[grass];
						}
						else if (tufts >= 0 && d > h + 7f && d <= h + 30f && Hash(i, j, seed) < 0.05f)
						{
							detail[tufts][j, i] = full[tufts];
						}
					}
				}
				for (int l = 0; l < kinds; l++)
				{
					data.SetDetailLayer(0, 0, l, detail[l]);
				}
			}
			else
			{
				// The scene's own grass stays, except on the road and a few metres beside it.
				int dw = data.detailWidth;
				int dh = data.detailHeight;
				int kinds = data.detailPrototypes.Length;
				if (kinds > 0)
				{
					Field detailNear = new Field(pts, dw, dh, g.X0 + 0.5f * g.SizeX / dw, g.Z0 + 0.5f * g.SizeZ / dh, g.SizeX / dw, g.SizeZ / dh, h + 8f);
					for (int l = 0; l < kinds; l++)
					{
						int[,] layerData = data.GetDetailLayer(0, 0, dw, dh, l);
						bool changed = false;
						for (int j = 0; j < dh; j++)
						{
							for (int i = 0; i < dw; i++)
							{
								if (layerData[j, i] != 0 && detailNear.Dist[j * dw + i] <= h + 4f)
								{
									layerData[j, i] = 0;
									changed = true;
								}
							}
						}
							if (changed)
							{
								data.SetDetailLayer(0, 0, l, layerData);
							}
						}
						if (P.EdgeGrass)
						{
							// New grass along both edges of the road (full on the first 7 m, thinning out to 22 m), from the scene's own grass layer.
							int gl = DetailIndex(data, "grass", false, 0);
							if (gl >= 0 && gl < kinds)
							{
								int[,] layerData = data.GetDetailLayer(0, 0, dw, dh, gl);
								int full = 0;
								foreach (int v in layerData)
								{
									full = Math.Max(full, v);
								}
								if (full == 0)
								{
									full = 190;
								}
								Field grassNear = new Field(pts, dw, dh, g.X0 + 0.5f * g.SizeX / dw, g.Z0 + 0.5f * g.SizeZ / dh,g.SizeX / dw, g.SizeZ / dh, h + 34f);
int laid = 0;
								// No new grass inside the carried hall.
List<Planned> halls = new List<Planned>();
								foreach (Planned pl in planned)
								{
									if (pl.Prune && pl.HasHall)
									{
										halls.Add(pl);
									}
								}
								for (int j = 0; j < dh; j++)
								{
									for (int i = 0; i < dw; i++)
									{
										float d = grassNear.Dist[j * dw + i];
if (d < h + 0.5f || d > h + 32f)
										{
											continue;
										}
bool inHall = false;
										float gx = grassNear.X0 + i * grassNear.DX;
										float gz = grassNear.Z0 + j * grassNear.DZ;
for (int q = 0; q < halls.Count && !inHall; q++)
										{
Vector3 local = Quaternion.Inverse(halls[q].HallTurn) * new Vector3(gx - halls[q].HallCentre.x, 0f, gz - halls[q].HallCentre.z);
											bool box = Math.Abs(local.x) < halls[q].HallHalf.x && Math.Abs(local.z) < halls[q].HallHalf.z;
											// The building's own footprint (its triangles from above, 5 m round them) where its mesh can be read, else the middle half of its bounds.
											inHall = box && (halls[q].HallTris != null ? NearTris(halls[q].HallTris, gx, gz, 5f) : (Math.Abs(local.x) < halls[q].HallHalf.x * 0.5f && Math.Abs(local.z) < halls[q].HallHalf.z * 0.5f));
										}
if (inHall)
										{
											continue;
										}
										// On the roads running into and out of the hall (within 320 m of its box): 6 m of bare dirt, then a wider band of grass (full to 14 m, thinning to 30 m).
										bool nearHall = false;
										for (int q = 0; q < halls.Count && !nearHall; q++)
										{
											Vector3 local2 = Quaternion.Inverse(halls[q].HallTurn) * new Vector3(gx - halls[q].HallCentre.x, 0f, gz - halls[q].HallCentre.z);
											nearHall = Math.Abs(local2.x) < halls[q].HallHalf.x + 320f && Math.Abs(local2.z) < halls[q].HallHalf.z + 320f;
										}
										float fullTo = nearHall ? 14f : 7f;
										float outTo = nearHall ? 30f : 22f;
										if (d > h + outTo || (nearHall && d < h + 6f))
										{
											continue;
										}
float keep = d <= h + fullTo ? 1f : 0.55f * (1f - (d - h - fullTo) / (outTo - fullTo));
										if (Hash(i, j, seed + 77) < keep)
										{
											layerData[j, i] = full;
											laid++;
										}
									}
								}
								data.SetDetailLayer(0, 0, gl, layerData);
								Mod.Info(P.Key + ": " + laid + " patches of grass laid along the road");
							}
						}
					}
			}

			// Trees: out of the road and the fields, and down or up to the new ground on the banks.
			List<TreeInstance> kept = new List<TreeInstance>(trees.Length);
			int removed = 0;
			int moved = 0;
			for (int t = 0; t < trees.Length; t++)
			{
				TreeInstance tree = trees[t];
				float x = g.X0 + tree.position.x * g.SizeX;
				float z = g.Z0 + tree.position.z * g.SizeZ;
				float d = near.DistAt(x, z);
if (d < h + TreeClear || InAny(fields, x, z, 3f) || (spinnerSet && (x - spinnerPos.x) * (x - spinnerPos.x) + (z - spinnerPos.z) * (z - spinnerPos.z) < spinnerR * spinnerR))
				{
					removed++;
					continue;
				}
if (d < Reach || P.Roughness > 0f)
					{
						tree.position = new Vector3(tree.position.x, (cut.At(x, z) - g.Y0) / g.SizeY, tree.position.z);
						moved++;
					}
				kept.Add(tree);
			}
if (P.RoadsideTrees > 0f)
			{
				AddRoadsideTrees(kept, trees, cut, layout, seed);
			}
			if (P.HairpinTrees)
			{
				AddHairpinTrees(kept, trees, cut, layout, seed);
			}
			data.treeInstances = kept.ToArray();
			Mod.Info(P.Key + ": " + fields.Count + " wheat fields, " + removed + " trees taken out, " + moved + " moved to the new ground, " + kept.Count + " left");
		}

		/// <summary>One to three wheat fields, tried up to 150 times at random spots, kept where the ground is flat, not too wooded, and beside the road.</summary>
		private static List<Patch> Fields(int seed, Ground g, Field road, TreeInstance[] trees)
		{
			List<Patch> fields = new List<Patch>();
			unchecked
			{
				Rng rng = new Rng((ulong)(uint)seed ^ 0xF1E1DUL);
				int want = 1 + (int)(rng.Next() * 3.0);
				for (int attempt = 0; attempt < 150 && fields.Count < want; attempt++)
				{
					// Every try makes the same five draws, so what a try finds never changes what the next one draws.
					double cx = g.X0 + EdgeMargin + rng.Next() * (g.SizeX - 2.0 * EdgeMargin);
					double cz = g.Z0 + EdgeMargin + rng.Next() * (g.SizeZ - 2.0 * EdgeMargin);
					double a = rng.Range(45.0, 110.0);
					double b = rng.Range(35.0, 75.0);
					double angle = rng.Range(0.0, DetMath.Pi);
					Patch p = new Patch((float)cx, (float)cz, (float)DetMath.Cos(angle), (float)DetMath.Sin(angle), (float)a, (float)b);
					if (Open(p, g, road, trees, fields))
					{
						fields.Add(p);
					}
				}
			}
			return fields;
		}

		private static bool Open(Patch p, Ground g, Field road, TreeInstance[] trees, List<Patch> fields)
		{
			float low = float.MaxValue;
			float high = float.MinValue;
			bool seen = false;
			for (float u = -p.A; u <= p.A + 0.01f; u += 8f)
			{
				for (float v = -p.B; v <= p.B + 0.01f; v += 8f)
				{
					float x = p.X + p.UX * u - p.UZ * v;
					float z = p.Z + p.UZ * u + p.UX * v;
					if (x < g.X0 + 30f || x > g.X0 + g.SizeX - 30f || z < g.Z0 + 30f || z > g.Z0 + g.SizeZ - 30f)
					{
						return false;
					}
					float d = road.DistAt(x, z);
					if (d < RoadHalfWidth + FieldGap)
					{
						return false;
					}
					seen |= d <= RoadHalfWidth + FieldNear;
					foreach (Patch other in fields)
					{
						if (other.Contains(x, z, 15f))
						{
							return false;
						}
					}
					float y = g.At(x, z);
					low = Math.Min(low, y);
					high = Math.Max(high, y);
				}
			}
			if (!seen || high - low > 12f)
			{
				return false;
			}
			int inside = 0;
			foreach (TreeInstance tree in trees)
			{
				if (p.Contains(g.X0 + tree.position.x * g.SizeX, g.Z0 + tree.position.z * g.SizeZ, 3f) && ++inside > FieldTrees)
				{
					return false;
				}
			}
			return true;
		}

		private static bool InAny(List<Patch> fields, float x, float z, float pad)
		{
			foreach (Patch f in fields)
			{
				if (f.Contains(x, z, pad))
				{
					return true;
				}
			}
			return false;
		}

		private static int LayerIndex(TerrainData data, string name, int fallback)
		{
			TerrainLayer[] layers = data.terrainLayers;
			for (int i = 0; i < layers.Length; i++)
			{
				if (layers[i] != null && layers[i].name == name)
				{
					return i;
				}
			}
			if (fallback >= 0)
			{
				Mod.Log.LogWarning("No terrain layer named " + name + ", using layer " + fallback + ".");
			}
			else
			{
				Mod.Log.LogWarning("No terrain layer named " + name + ", the road is not painted.");
			}
			return fallback;
		}

		/// <summary>The detail (grass) layer whose texture or prefab is named <paramref name="name"/>, or only starts with it, or -1.</summary>
		private static int DetailIndex(TerrainData data, string name, bool exact, int fallback)
		{
			DetailPrototype[] kinds = data.detailPrototypes;
			for (int i = 0; i < kinds.Length; i++)
			{
				string n = kinds[i].prototypeTexture != null ? kinds[i].prototypeTexture.name : kinds[i].prototype != null ? kinds[i].prototype.name : string.Empty;
				if (exact ? n == name : n.StartsWith(name, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
			if (fallback < kinds.Length)
			{
				Mod.Log.LogWarning("No grass layer named " + name + ", using layer " + fallback + ".");
				return fallback;
			}
			return -1;
		}

		/// <summary>0 up to 1, the same for the same cell and seed on every machine.</summary>
/// <summary>
		/// Adds seeded hills to the terrain's heights before the lap is placed: two octaves of smooth lattice noise (the biggest hills
		/// RoughWave metres across), swinging P.Roughness metres up or down, faded out within 200 m of the terrain's edge so the scene's
		/// backdrop mountains and borders stay as they are. Plain arithmetic in a fixed order, so every machine builds the same ground.
		/// </summary>
		private static void Roughen(float[,] heights, Vector3 size, int seed)
		{
			int res = heights.GetLength(0);
			float amplitude = P.Roughness / size.y;
			float wave = Mathf.Max(60f, P.RoughWave);
			for (int j = 0; j < res; j++)
			{
				for (int i = 0; i < res; i++)
				{
					float x = (float)i / (res - 1) * size.x;
					float z = (float)j / (res - 1) * size.z;
					float edge = Mathf.Min(Mathf.Min(x, size.x - x), Mathf.Min(z, size.z - z));
					float fade = (float)DetMath.Smooth(edge / 200f);
					if (fade <= 0f)
					{
						continue;
					}
					float n = 0.65f * Lattice(x / wave, z / wave, seed) + 0.35f * Lattice(x / (wave * 0.43f), z / (wave * 0.43f), seed + 101);
					heights[j, i] = Mathf.Clamp01(heights[j, i] + n * amplitude * fade);
				}
			}
		}

		/// <summary>Smooth value noise in -1..1: random values on a unit lattice, blended with the smooth step.</summary>
		private static float Lattice(float x, float z, int seed)
		{
			int ix = Mathf.FloorToInt(x);
			int iz = Mathf.FloorToInt(z);
			float fx = (float)DetMath.Smooth(x - ix);
			float fz = (float)DetMath.Smooth(z - iz);
			float a = Hash(ix, iz, seed) * 2f - 1f;
			float b = Hash(ix + 1, iz, seed) * 2f - 1f;
			float c = Hash(ix, iz + 1, seed) * 2f - 1f;
			float d = Hash(ix + 1, iz + 1, seed) * 2f - 1f;
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
		}

		private static float Hash(int i, int j, int seed)
		{
			unchecked
			{
				uint x = (uint)i * 73856093u ^ (uint)j * 19349663u ^ (uint)seed * 83492791u;
				x ^= x >> 13;
				x *= 0x5BD1E995u;
				x ^= x >> 15;
				return (x & 0xFFFFFFu) / 16777216f;
			}
		}

		/// <summary>
		/// Gives the terrain and its collider new data. The game's FellableTerrain built its trunk colliders (under "Tree Trunks") from
		/// the old trees, so it is taken off, trunks and all, and put back on. Its Awake clones the terrain data once more, hands the copy
		/// to the terrain and the collider, and builds the trunks from it; its OnDestroy only unregisters the old trunks, so they are
		/// destroyed here. HandlingModifier reads the paint from the terrain's data the first time it is asked after terrainResolved is
		/// cleared, so that is cleared last, once the data on the terrain is final.
		/// </summary>
		private static void SwapData(TerrainData data)
		{
			FellableTerrain fellable = terrain.GetComponent<FellableTerrain>();
			bool hadFellable = fellable != null;
			if (hadFellable)
			{
				UnityEngine.Object.DestroyImmediate(fellable);
				Transform trunks = terrain.transform.Find("Tree Trunks");
				if (trunks != null)
				{
					UnityEngine.Object.DestroyImmediate(trunks.gameObject);
				}
			}
			terrain.terrainData = data;
			if (terrainCollider != null)
			{
				terrainCollider.terrainData = data;
			}
			if (hadFellable)
			{
				terrain.gameObject.AddComponent<FellableTerrain>();
			}
			terrain.Flush();
			foreach (HandlingModifier handling in terrain.GetComponents<HandlingModifier>())
			{
				Traverse t = Traverse.Create(handling);
				Traverse textures = t.Field("alphamapTextures");
				Traverse resolved = t.Field("terrainResolved");
				if (textures.FieldExists() && resolved.FieldExists())
				{
					textures.SetValue(null);
					resolved.SetValue(false);
				}
				else if (!warnedHandling)
				{
					warnedHandling = true;
					Mod.Log.LogWarning("HandlingModifier has no alphamapTextures or terrainResolved field any more: grip may follow the old road's paint.");
				}
			}
			Physics.SyncTransforms();
		}

		// ---- the old track -----------------------------------------------------------------------------------------

		/// <summary>
		/// Hides what belongs to the old road, as the profile lists it (old road meshes, start lines, fences, hazards, trains, reset zones),
		/// the rain plane's collider, and any building, camp or backdrop the new road or its banks would run into.
		/// </summary>
		private static void HideOldTrack(Scene scene, GameObject env, Layout layout)
		{
			foreach (string path in P.HideChildren)
			{
				foreach (Transform t in FindAll(env.transform, path))
				{
					foreach (Transform child in t)
					{
						Hide(child.gameObject);
					}
				}
			}
			foreach (string path in P.HideAll)
			{
				foreach (Transform t in FindAll(env.transform, path))
				{
					Hide(t.gameObject);
				}
			}
			foreach (string name in P.HideRoots)
			{
				foreach (GameObject go in scene.GetRootGameObjects())
				{
					if (go.name == name)
					{
						Hide(go);
					}
				}
			}
			Transform rain = FindFirst(env.transform, "Weather|ParticleColliderPlane");
			if (rain != null && rain.TryGetComponent(out rainPlane))
			{
				rainPlane.enabled = false;
			}

			int count = 0;
			foreach (string path in P.InWay)
			{
				foreach (Transform container in FindAll(env.transform, path))
				{
					foreach (Transform group in container)
					{
						// The tree lines too: placement keeps the lap clear of them, but where nothing else fits, one in the way is hidden.
						if (!group.gameObject.activeSelf)
						{
							continue;
						}
						// An object that is itself a solid thing (a big rock, a ramp) is tested whole, not by its (no) children.
						if ((group.GetComponent<Renderer>() != null || group.GetComponent<Collider>() != null) && InTheWay(group, layout))
						{
							Hide(group.gameObject);
							count++;
							continue;
						}
						foreach (Transform item in group)
						{
							if (item.gameObject.activeSelf && InTheWay(item, layout))
							{
								Hide(item.gameObject);
								count++;
							}
						}
					}
				}
			}
			foreach (string path in P.InWayItems)
			{
				foreach (Transform group in FindAll(env.transform, path))
				{
					foreach (Transform item in group)
					{
						if (item.gameObject.activeSelf && InTheWay(item, layout))
						{
							Hide(item.gameObject);
							count++;
						}
					}
				}
			}
			Mod.Info(P.Key + ": " + hidden.Count + " objects of the old track hidden, " + count + " of them buildings, props or backdrop in the new road's way");
		}

		/// <summary>
		/// Every object at <paramref name="path"/> below <paramref name="root"/>: names between '|', matched exactly one level at a time
		/// (the game has an object called "Start/Finish_Line", so '/' cannot separate them).
		/// </summary>
		private static List<Transform> FindAll(Transform root, string path)
		{
			List<Transform> current = new List<Transform> { root };
			foreach (string part in path.Split('|'))
			{
				List<Transform> next = new List<Transform>();
				foreach (Transform t in current)
				{
					foreach (Transform child in t)
					{
						if (child.name == part)
						{
							next.Add(child);
						}
					}
				}
				current = next;
			}
			return current;
		}

		private static Transform FindFirst(Transform root, string path)
		{
			List<Transform> all = FindAll(root, path);
			return all.Count > 0 ? all[0] : null;
		}

		private static bool InTheWay(Transform item, Layout layout)
		{
			// A group's pivot is often the world origin, so it only counts when the item has nothing to measure.
			Bounds bounds = new Bounds(item.position, Vector3.zero);
			bool measured = false;
			foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
			{
				if (measured)
				{
					bounds.Encapsulate(r.bounds);
				}
				else
				{
					bounds = r.bounds;
					measured = true;
				}
			}
			foreach (Collider c in item.GetComponentsInChildren<Collider>())
			{
				if (measured)
				{
					bounds.Encapsulate(c.bounds);
				}
				else
				{
					bounds = c.bounds;
					measured = true;
				}
			}
			float minX = bounds.min.x - Reach;
			float maxX = bounds.max.x + Reach;
			float minZ = bounds.min.z - Reach;
			float maxZ = bounds.max.z + Reach;
			foreach (Vector3 p in layout.Points)
			{
				if (p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ)
				{
					return true;
				}
			}
			return false;
		}

		private static void Hide(GameObject go)
		{
			if (go.activeSelf)
			{
				go.SetActive(false);
				hidden.Add(go);
			}
		}

		// ---- the mesh road -----------------------------------------------------------------------------------------

		/// <summary>
		/// For scenes whose road is a mesh (the Mountain, Tokyo): a copy of the old road object, so it brings its layer, tag, material and
		/// HandlingModifier (grip, drag, suspension offset), with the lap's ribbon as its mesh and collider. Its texture is borrowed only when
		/// it repeats; otherwise the road is a plain colour, since a texture that was unwrapped once over the old road would smear. White
		/// edge lines go on top.
		/// </summary>
		private static void BuildMeshRoad(Transform source, Layout layout)
		{
			float h = RoadHalfWidth;
			Batch tarmac = new Batch("road", P.RoadColor, true);
			Loop.RibbonUV(tarmac, layout, -h, 0.05f, h, 0.05f, P.RoadTile);
			Mesh mesh = tarmac.ToMesh();
			mesh.name = "ProcTrack_" + P.Key + "_road";

			GameObject go = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
			go.name = "Road";
			go.SetActive(true);
			go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
			go.transform.localScale = Vector3.one;
			for (int i = go.transform.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
			}
			MeshFilter filter = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
			filter.sharedMesh = mesh;
			MeshRenderer renderer = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
			renderer.enabled = true;
			Material old = renderer.sharedMaterial;
			Texture texture = old != null ? old.mainTexture : null;
			Material material;
			if (old != null && texture != null && texture.wrapMode == TextureWrapMode.Repeat)
			{
				material = new Material(old) { hideFlags = HideFlags.HideAndDontSave };
				material.mainTextureScale = Vector2.one;
				material.mainTextureOffset = Vector2.zero;
			}
			else
			{
				material = CircuitScene.MakeMaterial(old, P.RoadColor);
			}
			renderer.sharedMaterial = material;
			MeshCollider collider = go.GetComponent<MeshCollider>() ?? go.AddComponent<MeshCollider>();
			collider.enabled = true;
			collider.convex = false;
			collider.sharedMesh = null;
			collider.sharedMesh = mesh;

			Batch edge = new Batch("edge", new Color(0.92f, 0.92f, 0.90f), false);
			Loop.Ribbon(edge, layout, -h + 0.8f, 0.07f, -h + 1.6f, 0.07f);
			Loop.Ribbon(edge, layout, h - 1.6f, 0.07f, h - 0.8f, 0.07f);
			GameObject lines = new GameObject("EdgeLines") { layer = go.layer };
			lines.transform.SetParent(root.transform, false);
			lines.AddComponent<MeshFilter>().sharedMesh = edge.ToMesh();
			lines.AddComponent<MeshRenderer>().sharedMaterial = CircuitScene.MakeMaterial(old, edge.Color);
			Mod.Info(P.Key + ": road mesh " + mesh.vertexCount + " vertices, " + (material.mainTexture != null ? "texture " + material.mainTexture.name : "plain colour") + ", layer " + LayerMask.LayerToName(go.layer));
		}

		// ---- the line, the grid ------------------------------------------------------------------------------------

		/// <summary>A two row chequer across the road from point 0 to point 2, as on the circuit. The start is level, so it lies flat on the ground.</summary>
		private static void BuildLine(Layout layout, Material source, int layer)
		{
			float h = RoadHalfWidth;
			Batch white = new Batch("white", new Color(0.92f, 0.92f, 0.90f), false);
			Batch black = new Batch("black", new Color(0.05f, 0.05f, 0.05f), false);
			int cells = Mathf.RoundToInt(h);
			float cell = 2f * h / cells;
			for (int row = 0; row < 2; row++)
			{
				for (int c = 0; c < cells; c++)
				{
					Batch b = (c + row) % 2 == 0 ? white : black;
					Loop.Cell(b, layout, row, -h + cell * c, -h + cell * (c + 1), lineLift);
				}
			}
			foreach (Batch batch in new[] { white, black })
			{
				GameObject go = new GameObject(batch.Name) { layer = layer };
				go.transform.SetParent(root.transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
				go.AddComponent<MeshRenderer>().sharedMaterial = CircuitScene.MakeMaterial(source, batch.Color);
			}
		}

		/// <summary>
		/// The grid on the level straight behind the line. Forest Field and the Mountain have two sets of slots with the same names, one per variant, and
		/// the game picks one set; every slot of a name goes to the same spot, so whichever set the game uses is right. The new lap heads
		/// the way the old one did, so no slot has to be turned. The semaphores keep where they were relative to the old start line.
		/// </summary>
		private static void PlaceGrid(TrackManager tm, Layout layout, Vector3 forward, Vector3 oldStart)
		{
			Vector3 start = layout.Points[0];
			Vector3 right = new Vector3(forward.z, 0f, -forward.x);
float lane = RoadHalfWidth * 0.28f;
			Transform container = tm.startingPositionsContainer.transform;
			List<string> names = new List<string>();
			Dictionary<string, List<Transform>> slots = new Dictionary<string, List<Transform>>();
			foreach (Transform slot in container)
			{
				if (!slots.TryGetValue(slot.name, out List<Transform> same))
				{
					same = new List<Transform>();
					slots[slot.name] = same;
					names.Add(slot.name);
				}
				same.Add(slot);
			}

			CarControllerNew car = tm.playerCarController;
			Transform playerSlot = Traverse.Create(tm).Field("playerStart").GetValue<Transform>();
			if (playerSlot == null && slots.TryGetValue("PlayerPosition", out List<Transform> players))
			{
				playerSlot = players.FirstOrDefault(t => t.gameObject.activeInHierarchy) ?? players[0];
			}
			float lift = playerSlot != null ? Mathf.Clamp(car.transform.position.y - playerSlot.position.y, 0.2f, 3f) : 0.5f;

			List<string> rivals = names.Where(n => n != "PlayerPosition").ToList();
			float spacing = Mathf.Min(30f, (LevelBehind - 10f) / (rivals.Count + 1));
			for (int k = 0; k <= rivals.Count; k++)
			{
				Vector3 at = start + forward * (-spacing * (k + 1)) + right * (k % 2 == 0 ? lane : -lane);
				string name = k < rivals.Count ? rivals[k] : "PlayerPosition";
				if (slots.TryGetValue(name, out List<Transform> same))
				{
					foreach (Transform slot in same)
					{
						slot.position = at;
					}
				}
				if (k == rivals.Count)
				{
CircuitScene.MoveCar(car, at + Vector3.up * lift);
						Mod.Info(P.Key + ": the player's slot is " + (spacing * (k + 1)).ToString("0") + " m behind the line, " + (k % 2 == 0 ? lane : -lane).ToString("0.0") + " m off the middle of a road " + (2f * RoadHalfWidth).ToString("0") + " m wide");
				}
			}

// The painted markers on the slots (GridPosition sprites, a few metres from each slot) must not stick out past the edge of the road: any that does is shrunk to fit.
			float limit = RoadHalfWidth - 1.2f;
			int shrunk = 0;
			foreach (Transform slot in container)
			{
				Physics.SyncTransforms();
				foreach (Renderer r in slot.GetComponentsInChildren<Renderer>(true))
				{
					Bounds bb = r.bounds;
					float lat = Math.Abs(Vector3.Dot(bb.center - start, right));
					float ext = Math.Abs(right.x) * bb.extents.x + Math.Abs(right.z) * bb.extents.z;
					if (lat + ext > limit)
					{
						float k = Mathf.Max(0.15f, (limit - lat) / Math.Max(0.01f, ext));
						if (k < 1f)
						{
							r.transform.localScale *= k;
							shrunk++;
						}
					}
				}
			}
			if (shrunk > 0)
			{
				Mod.Info(P.Key + ": " + shrunk + " grid markers stuck out past the road edge and were shrunk to fit");
			}

			// The semaphores (some scenes have two, one switched off) keep where they were relative to the old start line.
			foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (t.name.StartsWith("RaceStarterSemaphore") && (t.parent == null || !t.parent.name.StartsWith("RaceStarterSemaphore")))
				{
					t.position = start + (t.position - oldStart);
				}
			}
		}

		/// <summary>
		/// TrackManager.SpawnEnemies puts every rival at height 1 (or at its slot's height less the grid height it read in Awake, before
		/// the grid was moved), facing +X. That is the Stadium's flat ground; on a terrain lap the start can be up on the hills and the rivals
		/// would start inside the terrain. Each is lifted to 1 above the ground under it, the same 1 the game adds, and turned down the
		/// start straight.
		/// </summary>
		internal static void LiftRivals(TrackManager tm)
		{
			List<EnemyController> rivals = Traverse.Create(tm).Field("enemies").GetValue<List<EnemyController>>();
			if (rivals == null)
			{
				Mod.Log.LogWarning("TrackManager has no enemies list: the rivals could not be put on the ground.");
				return;
			}
			Quaternion facing = Quaternion.LookRotation(heading, Vector3.up);
			float baseY = terrain.GetPosition().y;
			int lifted = 0;
			foreach (EnemyController rival in rivals)
			{
				if (rival == null)
				{
					continue;
				}
				Transform t = rival.transform;
				Vector3 p = t.position;
				Vector3 target = new Vector3(p.x, baseY + terrain.SampleHeight(p) + 1f, p.z);
				t.SetPositionAndRotation(target, facing);
				Rigidbody body = rival.GetComponent<Rigidbody>();
				if (body != null)
				{
					body.position = target;
					body.rotation = facing;
					body.linearVelocity = Vector3.zero;
					body.angularVelocity = Vector3.zero;
				}
				lifted++;
			}
			Physics.SyncTransforms();
			Mod.Info(P.Key + ": " + lifted + " rivals put on the ground at the grid");
		}

		// ---- helpers -----------------------------------------------------------------------------------------------

		/// <summary>The terrain's heights as world heights, bilinear between samples.</summary>
		private sealed class Ground
		{
			public readonly float[,] Heights;
			public readonly int Res;
			public readonly float X0, Y0, Z0, SizeX, SizeY, SizeZ;

			public Ground(Vector3 position, Vector3 size, float[,] heights)
			{
				Heights = heights;
				Res = heights.GetLength(0);
				X0 = position.x;
				Y0 = position.y;
				Z0 = position.z;
				SizeX = size.x;
				SizeY = size.y;
				SizeZ = size.z;
			}

			public float At(float x, float z)
			{
				float u = Mathf.Clamp((x - X0) / SizeX * (Res - 1), 0f, Res - 1);
				float v = Mathf.Clamp((z - Z0) / SizeZ * (Res - 1), 0f, Res - 1);
				int i = Math.Min((int)u, Res - 2);
				int j = Math.Min((int)v, Res - 2);
				float fu = u - i;
				float fv = v - j;
				float a = Heights[j, i] + (Heights[j, i + 1] - Heights[j, i]) * fu;
				float b = Heights[j + 1, i] + (Heights[j + 1, i + 1] - Heights[j + 1, i]) * fu;
				return Y0 + SizeY * (a + (b - a) * fv);
			}
		}

		/// <summary>
		/// For each point of a grid over the terrain, the flat distance to the nearest point of the lap and the road height there.
		/// Points farther than the radius keep float.MaxValue. Grid point (i, j) is at (X0 + i DX, Z0 + j DZ).
		/// </summary>
		private sealed class Field
		{
			public readonly int W, H;
			public readonly float X0, Z0, DX, DZ;
			public readonly float[] Dist;
			public readonly float[] RoadY;

			public Field(Vector3[] pts, int w, int h, float x0, float z0, float dx, float dz, float radius)
			{
				W = w;
				H = h;
				X0 = x0;
				Z0 = z0;
				DX = dx;
				DZ = dz;
				Dist = new float[w * h];
				RoadY = new float[w * h];
				for (int k = 0; k < Dist.Length; k++)
				{
					Dist[k] = float.MaxValue;
				}
				float r2 = radius * radius;
				int n = pts.Length;
				for (int s = 0; s < n; s++)
				{
					Vector3 a = pts[s];
					Vector3 b = pts[(s + 1) % n];
					int i0 = Math.Max(0, Mathf.CeilToInt((Math.Min(a.x, b.x) - radius - x0) / dx));
					int i1 = Math.Min(w - 1, Mathf.FloorToInt((Math.Max(a.x, b.x) + radius - x0) / dx));
					int j0 = Math.Max(0, Mathf.CeilToInt((Math.Min(a.z, b.z) - radius - z0) / dz));
					int j1 = Math.Min(h - 1, Mathf.FloorToInt((Math.Max(a.z, b.z) + radius - z0) / dz));
					float abx = b.x - a.x;
					float abz = b.z - a.z;
					float len2 = abx * abx + abz * abz;
					for (int j = j0; j <= j1; j++)
					{
						float z = z0 + j * dz;
						for (int i = i0; i <= i1; i++)
						{
							float x = x0 + i * dx;
							float t = len2 > 1e-6f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.z) * abz) / len2) : 0f;
							float px = a.x + abx * t - x;
							float pz = a.z + abz * t - z;
							float d2 = px * px + pz * pz;
							if (d2 > r2)
							{
								continue;
							}
							int k = j * w + i;
							float d = Mathf.Sqrt(d2);
							if (d < Dist[k])
							{
								Dist[k] = d;
								RoadY[k] = a.y + (b.y - a.y) * t;
							}
						}
					}
				}
			}

			/// <summary>The distance at the grid point nearest (x, z).</summary>
			public float DistAt(float x, float z)
			{
				int i = Mathf.RoundToInt((x - X0) / DX);
				int j = Mathf.RoundToInt((z - Z0) / DZ);
				if (i < 0 || j < 0 || i >= W || j >= H)
				{
					return float.MaxValue;
				}
				return Dist[j * W + i];
			}
		}

		/// <summary>Trees counted in 20 m squares, for the placement score.</summary>
		private sealed class TreeGrid
		{
			private const float Cell = 20f;
			private readonly int nx, nz;
			private readonly float x0, z0;
			private readonly int[] counts;

			public TreeGrid(Ground g, TreeInstance[] trees)
			{
				x0 = g.X0;
				z0 = g.Z0;
				nx = Mathf.CeilToInt(g.SizeX / Cell);
				nz = Mathf.CeilToInt(g.SizeZ / Cell);
				counts = new int[nx * nz];
				foreach (TreeInstance tree in trees)
				{
					int i = Mathf.Clamp((int)(tree.position.x * g.SizeX / Cell), 0, nx - 1);
					int j = Mathf.Clamp((int)(tree.position.z * g.SizeZ / Cell), 0, nz - 1);
					counts[j * nx + i]++;
				}
			}

			public int Count(float x, float z)
			{
				int i = (int)((x - x0) / Cell);
				int j = (int)((z - z0) / Cell);
				return i < 0 || j < 0 || i >= nx || j >= nz ? 0 : counts[j * nx + i];
			}
		}

		/// <summary>A wheat field: a rectangle of half sides A and B, centred on (X, Z), its A side along (UX, UZ).</summary>
		private sealed class Patch
		{
			public readonly float X, Z, UX, UZ, A, B;

			public Patch(float x, float z, float ux, float uz, float a, float b)
			{
				X = x;
				Z = z;
				UX = ux;
				UZ = uz;
				A = a;
				B = b;
			}

			public bool Contains(float x, float z, float pad)
			{
				float dx = x - X;
				float dz = z - Z;
				return Math.Abs(dx * UX + dz * UZ) <= A + pad && Math.Abs(-dx * UZ + dz * UX) <= B + pad;
			}

			/// <summary>1 inside, 0 outside, ramping over <paramref name="soft"/> inside the edge.</summary>
			public float Weight(float x, float z, float soft)
			{
				float dx = x - X;
				float dz = z - Z;
				float u = A - Math.Abs(dx * UX + dz * UZ);
				float v = B - Math.Abs(-dx * UZ + dz * UX);
				return Mathf.Clamp01(u / soft) * Mathf.Clamp01(v / soft);
			}
		}
	}
}
