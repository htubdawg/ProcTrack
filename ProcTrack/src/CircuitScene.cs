using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripProcTrack
{
	/// <summary>
	/// Turns the Stadium scene into the procedural circuit when a race on it loads. Runs on <c>sceneLoaded</c>: after every Awake and before
	/// any Start, so the lap counter, the grid, the hazards and the reset logic all find the new track. The method is the Highway mod's
	/// (hide Stadium's ground and track, build meshes, swap the track line, move the grid), without its stacked roads.
	/// </summary>
	internal static partial class CircuitScene
	{
		private static PathManager path;
		private static GameObject root;
		private static readonly List<GameObject> hidden = new List<GameObject>();
		private static Collider rainPlane;

		private struct Palette
		{
			public Color Verge;
			public Color Ground;
		}

		// ---- load / unload -----------------------------------------------------------------------------------------

		internal static void OnSceneLoaded(Scene scene)
		{
			path = null;
			root = null;
			hidden.Clear();
			rainPlane = null;
			// The cameras raised for the last circuit went with its scene.
			raised.Clear();

			if (Mod.Map == null || RunState.ActiveMap != Mod.Map)
			{
				return;
			}
			TrackManager tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
			if (tm == null || tm.playerCarController == null || tm.lapManager == null || tm.lapManager.trackLine == null)
			{
				Mod.Log.LogWarning("Circuit: scene " + scene.name + " loaded but its race objects are not ready (track manager " + (tm == null ? "missing" : "found") + (tm != null ? ", car " + (tm.playerCarController != null) + ", laps " + (tm.lapManager != null) + ", line " + (tm.lapManager != null && tm.lapManager.trackLine != null) : "") + "): the map is not built.");
				return;
			}
			Mod.Log.LogInfo("Circuit: building in scene " + scene.name + "...");
			System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				Build(scene, tm);
				Mod.Log.LogInfo("Circuit: built in " + clock.ElapsedMilliseconds + " ms");
			}
			catch (Exception e)
			{
				Mod.Log.LogError("The procedural circuit could not be built, the scene is put back as it was: " + e);
				Unload();
			}
		}

		internal static void Unload()
		{
			RestoreDrawDistance();
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
			hidden.Clear();
			path = null;
			root = null;
		}

		// ---- building ----------------------------------------------------------------------------------------------

		private static void Build(Scene scene, TrackManager tm)
		{
			GameObject env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ENVIROMENT");
			if (env == null)
			{
				throw new InvalidOperationException("this scene has no ENVIROMENT object, so it is not the Stadium scene");
			}

			Transform ground = env.transform.Find("Ground");
			int layer = ground != null ? ground.gameObject.layer : 0;
			Material source = ground != null && ground.TryGetComponent(out MeshRenderer groundRenderer) ? groundRenderer.sharedMaterial : null;
			// Read before the lap replaces it: the Stadium's buildings are set round the new lap by where they stood to this one.
			List<Vector3> oldLine = new List<Vector3>(tm.lapManager.trackLine.sampledPoints ?? new List<Vector3>());

			int seed = SeedSource.Resolve(out string label);
			SeedSource.Forget();
			DateTime started = DateTime.UtcNow;
			Layout layout = Generator.Make(seed);
			Mod.Log.LogInfo("Circuit seed " + seed + " (" + label + "): lap " + layout.Length.ToString("0") + ", tightest corner radius " + (layout.Fallback ? "n/a (plain shape)" : layout.MinRadius.ToString("0")) + ", drawn on try " + (layout.Attempt + 1) + ", generated in " + (DateTime.UtcNow - started).TotalMilliseconds.ToString("0") + " ms");

			root = new GameObject("ProcCircuit");
			// Copied while the Stadium's objects are still switched on: a switched off renderer has no bounds to measure.
			GameObject road = BuildStadiumRoad(env, layout, Generator.RoadHalfWidth);
			CarryStadium(env, layout, oldLine, Generator.RoadHalfWidth);

			foreach (string name in new[] { "Ground", "Track", "Decoration/Outside", "Decoration/Bridge", "Decoration/TowerDamaged" })
			{
				Transform t = env.transform.Find(name);
				if (t != null && t.gameObject.activeSelf)
				{
					t.gameObject.SetActive(false);
					hidden.Add(t.gameObject);
				}
			}
			Transform rain = env.transform.Find("Weather/ParticleColliderPlane");
			if (rain != null && rain.TryGetComponent(out rainPlane))
			{
				rainPlane.enabled = false;
			}

			int meshes = 0;
			int triangles = 0;
			foreach (Batch batch in BuildBatches(layout, seed))
			{
				// The Stadium's own road object carries the tarmac when it was found.
				if (batch.Count == 0 || (road != null && batch.Name == "asphalt"))
				{
					continue;
				}
				GameObject go = new GameObject(batch.Name) { layer = layer };
				go.transform.SetParent(root.transform, false);
				Mesh mesh = batch.ToMesh();
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(source, batch.Color);
				if (batch.Collide)
				{
					go.AddComponent<MeshCollider>().sharedMesh = mesh;
				}
				if (batch.Wall)
				{
					try
					{
						go.tag = "Wall";
					}
					catch (Exception)
					{
						Mod.Log.LogWarning("The tag Wall does not exist, barriers keep the default tag.");
					}
				}
				if (batch.Rough)
				{
					HandlingModifier handling = go.AddComponent<HandlingModifier>();
					handling.gripModifier = 0.7f;
					handling.resistanceModifier = 3f;
					handling.background = true;
				}
				meshes++;
				triangles += mesh.triangles.Length / 3;
			}
			Mod.Info("Circuit built: " + meshes + " meshes, " + triangles + " triangles");
			BuildGrandstand(layout, Generator.RoadHalfWidth, layer, source);

			if (Mod.ShowBoard.Value)
			{
				BuildBoard(root.transform, layout.Points[0], Quaternion.identity, Generator.RoadHalfWidth, "PROCEDURAL CIRCUIT", seed, label, source, layer);
			}

			Rewire(tm, layout, Generator.RoadHalfWidth, hidden);
			PlaceGrid(tm, env, layout);
			SpreadHazards(layout, seed, root.transform);
			RaiseDrawDistance(layout);
		}

		private static Palette Pick(int seed)
		{
			unchecked
			{
				Rng rng = new Rng((ulong)(uint)seed ^ 0x5DEECE66DUL);
				switch ((int)(rng.Next() * 4.0))
				{
					case 0: return new Palette { Verge = new Color(0.30f, 0.46f, 0.20f), Ground = new Color(0.27f, 0.42f, 0.18f) };   // grass
					case 1: return new Palette { Verge = new Color(0.74f, 0.62f, 0.40f), Ground = new Color(0.68f, 0.56f, 0.36f) };   // sand
					case 2: return new Palette { Verge = new Color(0.38f, 0.34f, 0.22f), Ground = new Color(0.33f, 0.29f, 0.19f) };   // dry earth
					default: return new Palette { Verge = new Color(0.86f, 0.89f, 0.93f), Ground = new Color(0.80f, 0.84f, 0.89f) }; // snow
				}
			}
		}

		/// <summary>All the geometry of the lap. The road is at height 0, the ground 0.55 below it.</summary>
		private static List<Batch> BuildBatches(Layout layout, int seed)
		{
			float h = Generator.RoadHalfWidth;
			Palette palette = Pick(seed);
			int n = layout.Count;

			Batch asphalt = new Batch("asphalt", new Color(0.17f, 0.17f, 0.18f), true);
			Batch verge = new Batch("verge", palette.Verge, true, rough: true);
			Batch field = new Batch("field", palette.Ground, true, rough: true);
			Batch wall = new Batch("wall", new Color(0.64f, 0.64f, 0.62f), true, wall: true);
			Batch white = new Batch("white", new Color(0.92f, 0.92f, 0.90f), false);
			Batch red = new Batch("kerb red", new Color(0.80f, 0.12f, 0.10f), false);
			Batch black = new Batch("black", new Color(0.05f, 0.05f, 0.05f), false);

			// The road.
			Loop.Ribbon(asphalt, layout, -h, 0f, h, 0f);

			// Run-off: a gentle slope down from each edge, then a flat strip up to the wall.
			Loop.Ribbon(verge, layout, h, 0f, h + 12f, -0.5f);
			Loop.Ribbon(verge, layout, h + 12f, -0.5f, h + 16f, -0.5f);
			Loop.Ribbon(verge, layout, -h, 0f, -(h + 12f), -0.5f);
			Loop.Ribbon(verge, layout, -(h + 12f), -0.5f, -(h + 16f), -0.5f);

			// Everything outside the walls: one big slab, its top just under the run-off.
			Vector3 centre = Vector3.zero;
			foreach (Vector3 p in layout.Points)
			{
				centre += p;
			}
			centre /= n;
			field.Box(new Vector3(centre.x, -1.05f, centre.z), new Vector3(9000f, 1f, 9000f));

			// Walls, 14 beyond the road edge on both sides, 3.5 thick so a fast car cannot get through.
			Loop.Sweep(wall, layout, new[] { new Vector2(h + 14f, -0.5f), new Vector2(h + 17.5f, -0.5f), new Vector2(h + 17.5f, 3.5f), new Vector2(h + 14f, 3.5f) });
			Loop.Sweep(wall, layout, new[] { new Vector2(-(h + 14f), -0.5f), new Vector2(-(h + 14f), 3.5f), new Vector2(-(h + 17.5f), 3.5f), new Vector2(-(h + 17.5f), -0.5f) });

			// Paint: an edge line on each side and dashes down the middle.
			Loop.Ribbon(white, layout, h - 4.2f, 0.06f, h - 3f, 0.06f);
			Loop.Ribbon(white, layout, -(h - 4.2f), 0.06f, -(h - 3f), 0.06f);
			for (int i = 3; i < n - 1; i++)
			{
				if ((i / 2) % 2 == 0)
				{
					Loop.Cell(white, layout, i, -0.4f, 0.4f, 0.06f);
				}
			}

			// Red and white kerbs on the inside of every real corner (radius under about 500).
			for (int i = 0; i < n; i++)
			{
				float turn = Loop.Turn(layout, i);
				if (Mathf.Abs(turn) < 0.01f)
				{
					continue;
				}
				Batch kerb = i % 2 == 0 ? red : white;
				if (turn < 0f)
				{
					Loop.Cell(kerb, layout, i, h - 2f, h, 0.06f);
				}
				else
				{
					Loop.Cell(kerb, layout, i, -h, -(h - 2f), 0.06f);
				}
			}

			// The start line: a two row chequer across the road, from point 0 to point 2.
			int cells = Mathf.RoundToInt(h);
			float cell = 2f * h / cells;
			for (int row = 0; row < 2; row++)
			{
				for (int c = 0; c < cells; c++)
				{
					Batch b = (c + row) % 2 == 0 ? white : black;
					Loop.Cell(b, layout, row, -h + cell * c, -h + cell * (c + 1), 0.08f);
				}
			}

			return new List<Batch> { asphalt, verge, field, wall, white, red, black };
		}

		// ---- the sign over the start line ---------------------------------------------------------------------------

		private static TMP_FontAsset font;

		/// <summary>The gantry over the start line at <paramref name="at"/>. It is built for a start heading +X and turned by <paramref name="yaw"/>.</summary>
		internal static void BuildBoard(Transform parent, Vector3 at, Quaternion yaw, float h, string title, int seed, string label, Material source, int layer)
		{
			GameObject holder = new GameObject("Board") { layer = layer };
			holder.transform.SetParent(parent, false);
			holder.transform.SetPositionAndRotation(at, yaw);
			at = Vector3.zero;
			Batch frame = new Batch("gantry", new Color(0.36f, 0.37f, 0.40f), false);
			Batch board = new Batch("board", new Color(0.04f, 0.10f, 0.20f), false);
			float side = h + 4f;
			// The start heads +X, so the sign is built with the world axes.
			frame.Box(at + new Vector3(0f, 5.5f, side), new Vector3(1.6f, 11f, 1.6f));
			frame.Box(at + new Vector3(0f, 5.5f, -side), new Vector3(1.6f, 11f, 1.6f));
			frame.Box(at + new Vector3(0f, 11.3f, 0f), new Vector3(1.6f, 1.6f, 2f * side + 1.6f));
			board.Box(at + new Vector3(-1.1f, 8.4f, 0f), new Vector3(0.5f, 4f, 2f * h - 6f));
			foreach (Batch batch in new[] { frame, board })
			{
				GameObject go = new GameObject(batch.Name) { layer = layer };
				go.transform.SetParent(holder.transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
				go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(source, batch.Color);
			}

			if (font == null)
			{
				font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.Contains("m6x11"))
					?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
			}
			if (font == null)
			{
				Mod.Log.LogWarning("No TextMeshPro font found, the sign stays blank.");
				return;
			}
			GameObject text = new GameObject("BoardText");
			text.transform.SetParent(holder.transform, false);
			// Same facing rule as the Highway mod's gantry text: looking along the object's forward (+X) shows its back, so the face points to -X, toward the grid.
			text.transform.localPosition = at + new Vector3(-1.5f, 8.4f, 0f);
			text.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
			TextMeshPro tmp = text.AddComponent<TextMeshPro>();
			tmp.font = font;
			tmp.text = title + "\nSEED " + seed + "   " + label;
			tmp.color = new Color(0.95f, 0.95f, 0.90f);
			tmp.alignment = TextAlignmentOptions.Center;
			tmp.rectTransform.sizeDelta = new Vector2(2f * h - 8f, 3.4f);
			tmp.enableAutoSizing = true;
			tmp.fontSizeMin = 1f;
			tmp.fontSizeMax = 200f;
			tmp.overflowMode = TextOverflowModes.Overflow;
		}

		internal static Material MakeMaterial(Material source, Color color)
		{
			Shader shader = source != null ? source.shader : null;
			Material m = shader != null ? new Material(shader) : null;
			if (m == null || (!m.HasProperty("_BaseColor") && !m.HasProperty("_Color")))
			{
				shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
				m = new Material(shader);
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", color);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", color);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			m.hideFlags = HideFlags.HideAndDontSave;
			return m;
		}

		// ---- the track line, the grid, the hazards ------------------------------------------------------------------

		/// <summary>
		/// The racing line: the middle of the road pulled taut (each point toward the midpoint of its neighbours, many times over) while it is kept
		/// within the road, so it cuts the inside of each bend and straightens through the wiggles, as a driver would. Offsets are to the right of
		/// the middle (metres); the result is at the same ground height as the middle.
		/// </summary>
		private static Vector3[] RacingLine(Layout layout, float limit, out float[] offset)
		{
			int n = layout.Points.Length;
			Vector3[] centre = layout.Points;
			Vector3[] p = (Vector3[])centre.Clone();
			Vector3[] right = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				right[i] = layout.Right(i);
			}
			offset = new float[n];
			Vector3[] next = new Vector3[n];
			for (int pass = 0; pass < 400; pass++)
			{
				for (int i = 0; i < n; i++)
				{
					Vector3 mid = (p[(i + n - 1) % n] + p[(i + 1) % n]) * 0.5f;
					Vector3 q = p[i] + (mid - p[i]) * 0.6f;
					float o = Mathf.Clamp(Vector3.Dot(q - centre[i], right[i]), -limit, limit);
					offset[i] = o;
					next[i] = centre[i] + right[i] * o;
				}
				Vector3[] swap = p;
				p = next;
				next = swap;
			}
			return p;
		}

		/// <summary>The line thinned into waypoints: one every step metres along it (evenly, so the game's spline does not kink), as indices.</summary>
		private static List<int> ControlPoints(Vector3[] d, float step)
		{
			List<int> pts = new List<int> { 0 };
			float since = 0f;
			for (int i = 1; i < d.Length - 1; i++)
			{
				since += (d[i] - d[i - 1]).magnitude;
				if (since >= step)
				{
					pts.Add(i);
					since = 0f;
				}
			}
			// The last waypoint must not sit on top of the first one: the loop closes between them.
			while (pts.Count > 4 && Vector3.Distance(d[pts[pts.Count - 1]], d[0]) < step * 0.4f)
			{
				pts.RemoveAt(pts.Count - 1);
			}
			return pts;
		}

		/// <summary>Replaces Stadium's track line with the lap, and tells the lap counter, the AI and the scatter about it.</summary>
		internal static void Rewire(TrackManager tm, Layout layout, float h, List<GameObject> hidden)
		{
			LapManager lap = tm.lapManager;
			PathManager line = lap.trackLine;

			foreach (Transform child in line.transform.Cast<Transform>().ToList())
			{
				child.SetParent(null);
				UnityEngine.Object.Destroy(child.gameObject);
			}
// The line the bots drive: pulled taut inside the road (6 m kept from each edge), and each waypoint's width is what is left of the road around it.
			float limit = Math.Max(1f, h - 6f);
			Vector3[] race = RacingLine(layout, limit, out float[] offset);
			List<int> control = ControlPoints(race, 40f);
			for (int i = 0; i < control.Count; i++)
			{
				GameObject point = new GameObject("PC" + (i + 1));
				point.transform.SetParent(line.transform, true);
point.transform.position = race[control[i]];
float room = Math.Max(4f, h - Math.Abs(offset[control[i]]) - 1f);
				point.transform.localScale = new Vector3(room, 1f, room);
				point.AddComponent<PathControlPointEditorController>();
			}

			AccessTools.Method(typeof(PathManager), "InitializePath").Invoke(line, null);
			Traverse.Create(line).Field("snapLanesToGround").SetValue(false);
			line.BakeRacingLine();

			path = line;
			if (Mod.ShowPath.Value && line.TryGetComponent(out LineRenderer renderer))
			{
				renderer.enabled = true;
			}

			// The other track lines belong to Stadium.
			lap.enemyTrackLines = new List<PathManager> { line };
			foreach (PathManager other in UnityEngine.Object.FindObjectsByType<PathManager>(FindObjectsSortMode.None))
			{
				if (other != line)
				{
					other.gameObject.SetActive(false);
					hidden.Add(other.gameObject);
				}
			}

			TrackScatter scatter = UnityEngine.Object.FindFirstObjectByType<TrackScatter>();
			if (scatter != null)
			{
				scatter.path = line;
			}

			Mod.Info("Circuit track line: " + control.Count + " control points, " + line.sampledPoints.Count + " samples, " + line.trackLength.ToString("0") + " m, " + line.lanes.Count + " lanes");
		}

		/// <summary>Rivals on the circuit: the Stadium has five grid slots, the Factory and Tokyo seven, so two are added to make the same field.</summary>
		private const int Rivals = 7;

		/// <summary>
		/// The grid goes on the start straight, behind the line, heading +X as Stadium's own grid does, so no slot has to be turned. The
		/// rivals are spawned one per slot in TrackManager's own list (read in its Awake, spawned in its Start, and this runs between),
		/// so the added slots go into that list.
		/// </summary>
		private static void PlaceGrid(TrackManager tm, GameObject env, Layout layout)
		{
			float h = Generator.RoadHalfWidth;
			Transform container = tm.startingPositionsContainer.transform;
			Transform playerSlot = Traverse.Create(tm).Field("playerStart").GetValue<Transform>() ?? container.Cast<Transform>().FirstOrDefault(t => t.name == "PlayerPosition");
			CarControllerNew car = tm.playerCarController;
			float lift = playerSlot != null ? Mathf.Clamp(car.transform.position.y - playerSlot.position.y, 0.2f, 3f) : 0.5f;

			List<Transform> slots = Traverse.Create(tm).Field("startPositions").GetValue<List<Transform>>();
			if (slots == null)
			{
				Mod.Log.LogWarning("TrackManager has no startPositions list: the circuit keeps the Stadium's grid size.");
				slots = container.Cast<Transform>().Where(t => t != playerSlot && t.gameObject.activeInHierarchy).ToList();
			}
			else if (slots.Count > 0)
			{
				while (slots.Count < Rivals)
				{
					// A copy of a slot brings its painted grid marker along.
					GameObject added = UnityEngine.Object.Instantiate(slots[0].gameObject, container);
					added.name = (slots.Count + 1).ToString();
					added.SetActive(true);
					slots.Add(added.transform);
				}
			}

			float laneRight = -h * 0.35f;   // heading +X, right is -Z
			float laneLeft = h * 0.35f;
			for (int k = 0; k < slots.Count; k++)
			{
				slots[k].position = new Vector3(-30f * (k + 1), 0f, k % 2 == 0 ? laneRight : laneLeft);
			}
			// The straight behind the line is at least 290 m: eight cars 30 m apart fit.
			int row = slots.Count + 1;
			Vector3 playerAt = new Vector3(-30f * row, 0f, row % 2 == 1 ? laneRight : laneLeft);
			Mod.Info("Circuit grid: " + slots.Count + " rivals, the player " + (30f * row).ToString("0") + " m behind the line");
			if (playerSlot != null)
			{
				playerSlot.position = playerAt;
			}
			MoveCar(car, playerAt + Vector3.up * lift);

			Transform semaphore = env.transform.Find("RaceStarterSemaphore");
			if (semaphore != null)
			{
				semaphore.position = new Vector3(-22f, semaphore.position.y, -(h + 3f));
			}
		}

		/// <summary>Puts the player's car at <paramref name="target"/>, stopped, with the audio listener off during the jump.</summary>
		internal static void MoveCar(CarControllerNew car, Vector3 target)
		{
			Transform ct = car.transform;
			AudioListener listener = car.GetComponentInChildren<AudioListener>(true);
			bool listenerOn = listener != null && listener.enabled;
			if (listenerOn)
			{
				listener.enabled = false;
			}
			ct.position = target;
			Rigidbody body = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (body != null)
			{
				body.position = target;
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}
			if (listenerOn)
			{
				listener.enabled = true;
			}
			Physics.SyncTransforms();
		}

		/// <summary>Chicken and trap spots all round the lap, away from the grid.</summary>
		internal static void SpreadHazards(Layout layout, int seed, Transform parent)
		{
			HazardsSpawner_FINAL spawner = UnityEngine.Object.FindFirstObjectByType<HazardsSpawner_FINAL>();
			if (spawner == null)
			{
				return;
			}
			Rng rng = new Rng((ulong)(uint)seed ^ 77UL);
			List<GameObject> spots = new List<GameObject>();
			int count = 40;
			int n = layout.Count;
			for (int i = 0; i < count; i++)
			{
				float s = 300f + (layout.Length - 600f) * i / (count - 1);
				int index = Mathf.Clamp(Mathf.RoundToInt(s / layout.Step), 0, n - 1);
				GameObject spot = new GameObject("HazardSpot" + i);
				spot.transform.SetParent(parent, false);
				spot.transform.position = layout.Points[index] + layout.Right(index) * (float)rng.Range(-6.0, 6.0);
				spots.Add(spot);
			}
			Traverse.Create(spawner).Field("spawnPositions").SetValue(spots);
		}
	}
}
