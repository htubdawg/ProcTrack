using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// The Stadium's own things on the circuit. Its road object is copied for the new road (its material, grip and layer). Its stands,
	/// catch fence, barrier and centre island are single meshes shaped round the old oval, so they cannot follow the new lap: a grandstand
	/// is built along the new start straight instead. Everything that stands on its own (the pit box, the bridge over the road, the tower
	/// blocks, water towers and damaged towers outside) is copied and set round the new lap as it stood round the old one: in the same
	/// direction from the middle of the track, on the same side of the road (infield or outside), as far from it, and turned the same way
	/// to it. A copy that finds no clear ground is left out.
	/// </summary>
	internal static partial class CircuitScene
	{
		/// <summary>The scene's own things that are carried round the new lap, under ENVIROMENT. A group with children is carried child by child.</summary>
		private static readonly string[] CarriedGroups = { "Track/Pitbox", "Decoration/Bridge", "Decoration/TowerDamaged", "Decoration/Outside" };

		/// <summary>The circuit's ground is this far below the road, where the Stadium's was level with it.</summary>
		private const float GroundDrop = 0.55f;

		/// <summary>The outer face of the circuit's walls, from the middle of the road, less the road's half width.</summary>
		private const float WallBack = 17.5f;

		private struct Placed
		{
			public Vector3 Centre;
			public float Radius;
		}

		/// <summary>
		/// A copy of Stadium's TrackFlat with the lap's ribbon as its mesh and collider, so the road has the Stadium's asphalt, grip and
		/// suspension offset, layer and tag. Its texture is borrowed only when it repeats. Null when the scene has no TrackFlat.
		/// </summary>
		private static GameObject BuildStadiumRoad(GameObject env, Layout layout, float h)
		{
			Transform source = env.transform.Find("Track/TrackFlat");
			if (source == null)
			{
				Mod.Log.LogWarning("The Stadium has no Track/TrackFlat: the circuit's road is plain.");
				return null;
			}
			Batch tarmac = new Batch("asphalt", new Color(0.17f, 0.17f, 0.18f), true);
			Loop.RibbonUV(tarmac, layout, -h, 0f, h, 0f, 40f);
			Mesh mesh = tarmac.ToMesh();

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
				material = MakeMaterial(old, tarmac.Color);
			}
			// TrackFlat has two materials; the ribbon has one part.
			renderer.sharedMaterials = new[] { material };
			MeshCollider collider = go.GetComponent<MeshCollider>() ?? go.AddComponent<MeshCollider>();
			collider.enabled = true;
			collider.convex = false;
			collider.sharedMesh = null;
			collider.sharedMesh = mesh;
			Mod.Info("Circuit road from TrackFlat: " + (material.mainTexture != null ? "texture " + material.mainTexture.name : "plain colour") + ", layer " + LayerMask.LayerToName(go.layer) + ", grip " + (go.GetComponent<HandlingModifier>() != null ? "the Stadium's" : "default"));
			return go;
		}

		/// <summary>+1 when the infield is to the right of the direction of travel, -1 when it is to the left (from the lap's signed area).</summary>
		private static float InsideSign(Layout layout)
		{
			double area = 0.0;
			int n = layout.Count;
			for (int i = 0; i < n; i++)
			{
				Vector3 a = layout.Points[i];
				Vector3 b = layout.Points[(i + 1) % n];
				area += a.x * b.z - b.x * a.z;
			}
			// Positive area: the lap runs anticlockwise seen from above (x east, z north), so the infield is on the left.
			return area > 0.0 ? -1f : 1f;
		}

		/// <summary>
		/// A grandstand along the start straight, on its outside behind the wall: eight tiers of seats in the Stadium's colours, a back
		/// wall above them, over the 250 m of straight that ends at the start line. Each tier is its own block, so every face is lit right.
		/// </summary>
		private static void BuildGrandstand(Layout layout, float h, int layer, Material source)
		{
			int n = layout.Count;
			float side = -InsideSign(layout);
			int count = 50;
			int from = n - count - 1;
			Batch concrete = new Batch("grandstand", new Color(0.62f, 0.62f, 0.60f), true, wall: true);
			Batch seatsA = new Batch("grandstand seats", new Color(0.12f, 0.28f, 0.62f), false);
			Batch seatsB = new Batch("grandstand seats red", new Color(0.70f, 0.14f, 0.12f), false);
			float a = h + WallBack + 1.5f;
			const int tiers = 8;
			const float depth = 2.4f;
			const float rise = 1.2f;
			float back = a + tiers * depth;
			for (int k = 0; k < tiers; k++)
			{
				float x0 = a + k * depth;
				float top = (k + 1) * rise;
				Loop.SweepRange(concrete, layout, Rect(side, x0, back, -GroundDrop, top), from, count);
				Loop.SweepRange(k % 2 == 0 ? seatsA : seatsB, layout, Rect(side, x0 + 0.3f, x0 + 1.1f, top, top + 0.45f), from, count);
			}
			Loop.SweepRange(concrete, layout, Rect(side, back, back + 1f, -GroundDrop, tiers * rise + 3.5f), from, count);
			foreach (Batch batch in new[] { concrete, seatsA, seatsB })
			{
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
					}
				}
			}
			Mod.Info("Circuit grandstand: " + tiers + " tiers over " + (count * layout.Step).ToString("0") + " m on the " + (side > 0f ? "right" : "left") + " of the start straight");
		}

		/// <summary>A closed rectangle cross-section from lateral x0 to x1 (on <paramref name="side"/>) and height y0 to y1.</summary>
		private static Vector2[] Rect(float side, float x0, float x1, float y0, float y1)
		{
			return new[] { new Vector2(side * x0, y0), new Vector2(side * x1, y0), new Vector2(side * x1, y1), new Vector2(side * x0, y1) };
		}

		/// <summary>
		/// Copies of the Stadium's free-standing things set round the new lap (see the class summary). The old track line is the scene's
		/// own, read before the lap replaces it.
		/// </summary>
		private static void CarryStadium(GameObject env, Layout layout, List<Vector3> oldLine, float h)
		{
			if (oldLine.Count < 3)
			{
				Mod.Log.LogWarning("The Stadium's track line has no samples: its buildings are not carried round the circuit.");
				return;
			}
			List<Transform> items = new List<Transform>();
			foreach (string path in CarriedGroups)
			{
				Transform group = env.transform.Find(path);
				if (group == null)
				{
					continue;
				}
				if (group.GetComponent<Renderer>() != null || group.childCount == 0)
				{
					items.Add(group);
				}
				else
				{
					foreach (Transform child in group)
					{
						if (child.gameObject.activeSelf)
						{
							items.Add(child);
						}
					}
				}
			}

			Vector3 oldCentre = Vector3.zero;
			foreach (Vector3 p in oldLine)
			{
				oldCentre += p;
			}
			oldCentre /= oldLine.Count;
			int n = layout.Count;
			Vector3 newCentre = Vector3.zero;
			foreach (Vector3 p in layout.Points)
			{
				newCentre += p;
			}
			newCentre /= n;
			float inside = InsideSign(layout);
			float[] angle = new float[n];
			for (int i = 0; i < n; i++)
			{
				angle[i] = Mathf.Atan2(layout.Points[i].z - newCentre.z, layout.Points[i].x - newCentre.x);
			}

			// The grandstand (built later, see BuildGrandstand) holds the outside of the start straight.
			List<Placed> placed = new List<Placed>();
			for (int i = n - 51; i < n; i += 3)
			{
				placed.Add(new Placed { Centre = layout.At(i, -inside * (h + WallBack + 11f), 0f), Radius = 11f });
			}
			int carried = 0;
			List<string> left = new List<string>();
			foreach (Transform item in items)
			{
				if (!Bounds(item, out Bounds b))
				{
					continue;
				}
				Vector3 c = Flat(b.center);
				int m = Nearest(oldLine, c);
				Vector3 tOld = Flat(oldLine[(m + 1) % oldLine.Count] - oldLine[(m - 1 + oldLine.Count) % oldLine.Count]).normalized;
				Vector3 rOld = new Vector3(tOld.z, 0f, -tOld.x);
				Vector3 off = c - Flat(oldLine[m]);
				float lat = Vector3.Dot(off, rOld);
				bool crosses = b.min.x < oldLine[m].x && b.max.x > oldLine[m].x && b.min.z < oldLine[m].z && b.max.z > oldLine[m].z;
				bool infield = Vector3.Dot(off, Flat(oldCentre - oldLine[m])) > 0f;
				float radius = Mathf.Max(b.extents.x, b.extents.z);
				bool ok = crosses
					? PlaceAcross(item, b, c, m, tOld, rOld, oldLine, layout, h, placed)
					: PlaceBeside(item, b, c, tOld, lat, infield, radius, Mathf.Atan2(c.z - oldCentre.z, c.x - oldCentre.x), angle, inside, layout, h, placed);
				if (ok)
				{
					carried++;
				}
				else
				{
					left.Add(item.name);
				}
			}
			Mod.Info("Circuit: " + carried + " of the Stadium's " + items.Count + " buildings and towers set round the lap" + (left.Count > 0 ? "; no clear ground for " + string.Join(", ", left.ToArray()) : ""));
		}

		/// <summary>
		/// Something that stood beside the old road: put at the lap point in the same direction from the middle of the track, on the same
		/// side (infield or outside), as far from the road as it was (but clear of the walls), turned the same way to the road. If the
		/// ground there is taken, nearby points and longer distances are tried.
		/// </summary>
		private static bool PlaceBeside(Transform item, Bounds b, Vector3 c, Vector3 tOld, float lat, bool infield, float radius, float dirOld, float[] angle, float inside, Layout layout, float h, List<Placed> placed)
		{
			int n = layout.Count;
			int home = 0;
			float best = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				float d = Mathf.Abs(Mathf.DeltaAngle(angle[i] * Mathf.Rad2Deg, dirOld * Mathf.Rad2Deg));
				if (d < best)
				{
					best = d;
					home = i;
				}
			}
			float side = infield ? inside : -inside;
			float clear = h + WallBack + 6f + radius;
			float want = Mathf.Max(clear, Mathf.Abs(lat));
			// Outside, the skyline is kept within reach of the eye; the infield takes what room there is.
			if (!infield)
			{
				want = Mathf.Min(want, Mathf.Max(clear, 420f));
			}
			for (int step = 0; step <= 12; step++)
			{
				int shift = (step + 1) / 2 * (step % 2 == 0 ? 1 : -1) * 6;
				int i = ((home + shift) % n + n) % n;
				foreach (float extra in infield ? new[] { 0f, -0.25f, -0.5f } : new[] { 0f, 60f, 140f })
				{
					float dist = Mathf.Max(clear, want * (1f + (infield ? extra : 0f)) + (infield ? 0f : extra));
					Vector3 at = layout.At(i, side * dist, 0f);
					if (!Clear(at, radius, clear - radius, layout, placed))
					{
						continue;
					}
					// The same turn to the road as before; across to the other side of the road, half a turn more, so it still faces it.
					Vector3 tNew = Flat(layout.Points[(i + 1) % n] - layout.Points[(i - 1 + n) % n]).normalized;
					float turn = Vector3.SignedAngle(tOld, tNew, Vector3.up);
					if (Mathf.Sign(lat) != Mathf.Sign(side))
					{
						turn += 180f;
					}
					Copy(item, c, at, turn, 1f, -GroundDrop);
					placed.Add(new Placed { Centre = at, Radius = radius });
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Something that spanned the old road (the bridge): put across a straight of the new lap half way round from the start, turned the
		/// same way to the road, and made bigger if it is too short to clear the circuit's walls.
		/// </summary>
		private static bool PlaceAcross(Transform item, Bounds b, Vector3 c, int m, Vector3 tOld, Vector3 rOld, List<Vector3> oldLine, Layout layout, float h, List<Placed> placed)
		{
			float half = 0f;
			for (int k = 0; k < 8; k++)
			{
				Vector3 corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, 0f, (k & 2) == 0 ? b.min.z : b.max.z);
				half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(corner - Flat(oldLine[m]), rOld)));
			}
			float need = h + WallBack + 2f;
			float scale = half > 1f ? Mathf.Max(1f, need / half) : 1f;
			if (scale > 1.8f)
			{
				Mod.Info("Circuit: the " + item.name + " spans " + (2f * half).ToString("0") + " m, too short to stand over the circuit's " + (2f * need).ToString("0") + " m, it is left out.");
				return false;
			}
			int n = layout.Count;
			float lat = Vector3.Dot(c - Flat(oldLine[m]), rOld);
			int pick = -1;
			float straightest = float.MaxValue;
			for (int i = n / 3; i < n * 2 / 3; i++)
			{
				float bend = 0f;
				for (int q = -12; q <= 12; q++)
				{
					bend += Mathf.Abs(Loop.Turn(layout, ((i + q) % n + n) % n));
				}
				if (bend < straightest)
				{
					straightest = bend;
					pick = i;
				}
			}
			if (pick < 0)
			{
				return false;
			}
			Vector3 at = layout.At(pick, lat * scale, 0f);
			Vector3 tNew = Flat(layout.Points[(pick + 1) % n] - layout.Points[(pick - 1 + n) % n]).normalized;
			Copy(item, c, at, Vector3.SignedAngle(tOld, tNew, Vector3.up), scale, -GroundDrop);
			placed.Add(new Placed { Centre = at, Radius = Mathf.Max(b.extents.x, b.extents.z) * scale });
			Mod.Info("Circuit: the " + item.name + " crosses the road at lap point " + pick + " of " + n + (scale > 1f ? ", made " + scale.ToString("0.00") + " times bigger to clear the walls" : ""));
			return true;
		}

		/// <summary>No lap point within <paramref name="road"/> of the spot, and no other copy within reach of it.</summary>
		private static bool Clear(Vector3 at, float radius, float road, Layout layout, List<Placed> placed)
		{
			float road2 = (road + radius) * (road + radius);
			foreach (Vector3 p in layout.Points)
			{
				float dx = p.x - at.x;
				float dz = p.z - at.z;
				if (dx * dx + dz * dz < road2)
				{
					return false;
				}
			}
			foreach (Placed other in placed)
			{
				if ((Flat(other.Centre) - Flat(at)).magnitude < other.Radius + radius + 10f)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>A copy of the item under the circuit, its middle moved from <paramref name="from"/> to <paramref name="to"/>, turned about it and scaled about it.</summary>
		private static void Copy(Transform item, Vector3 from, Vector3 to, float turnDegrees, float scale, float lift)
		{
			Quaternion turn = Quaternion.AngleAxis(turnDegrees, Vector3.up);
			Vector3 pivot = turn * ((item.position - from) * scale);
			GameObject copy = UnityEngine.Object.Instantiate(item.gameObject, root.transform, true);
			copy.name = item.name;
			copy.transform.SetPositionAndRotation(new Vector3(to.x + pivot.x, item.position.y * scale + lift, to.z + pivot.z), turn * item.rotation);
			copy.transform.localScale = item.lossyScale * scale;
			copy.SetActive(true);
		}

		private static bool Bounds(Transform item, out Bounds bounds)
		{
			bounds = default;
			bool any = false;
			foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
			{
				if (any)
				{
					bounds.Encapsulate(r.bounds);
				}
				else
				{
					bounds = r.bounds;
					any = true;
				}
			}
			return any;
		}

		private static int Nearest(List<Vector3> line, Vector3 at)
		{
			int best = 0;
			float d = float.MaxValue;
			for (int i = 0; i < line.Count; i++)
			{
				float e = (Flat(line[i]) - at).sqrMagnitude;
				if (e < d)
				{
					d = e;
					best = i;
				}
			}
			return best;
		}

		private static Vector3 Flat(Vector3 v)
		{
			return new Vector3(v.x, 0f, v.z);
		}
	}
}
