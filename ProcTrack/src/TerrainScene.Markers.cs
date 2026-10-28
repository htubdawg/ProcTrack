using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// Things that let a driver see where the road goes: the scene's own curve signs, set before the sharp bends on the outside of the road,
	/// and a few of its burning barrels at the bends; and a tall invisible wall round the edge of the terrain so nobody can drive off the map.
	/// </summary>
	internal static partial class TerrainScene
	{
		private static float Yaw(Vector3 v)
		{
			return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
		}

		private static void Mark(Transform src, Vector3 where, Vector3 faceAlong, float sink, bool signed)
		{
			Renderer[] rs = src.GetComponentsInChildren<Renderer>();
			if (rs.Length == 0)
			{
				return;
			}
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs)
			{
				b.Encapsulate(r.bounds);
			}
			Quaternion turn = Quaternion.identity;
			if (signed)
			{
				// The old sign faced oncoming traffic one way or the other along the road; the copy keeps that relation to the new road.
				int m = 0;
				float best = float.MaxValue;
				for (int i = 0; i < oldRoad.Count; i++)
				{
					float d = Flat(oldRoad[i] - src.position).sqrMagnitude;
					if (d < best)
					{
						best = d;
						m = i;
					}
				}
				Vector3 t = Flat(oldRoad[Math.Min(m + 1, oldRoad.Count - 1)] - oldRoad[Math.Max(m - 1, 0)]);
				Vector3 right = Flat(src.right);
				float sgn = Vector3.Dot(right, t) >= 0f ? 1f : -1f;
				if (right.sqrMagnitude > 1e-4f)
				{
					turn = Quaternion.AngleAxis(Vector3.SignedAngle(right,faceAlong * -sgn, Vector3.up), Vector3.up);
				}
			}
			GameObject clone = UnityEngine.Object.Instantiate(src.gameObject, pen, false);
			clone.name = src.name;
			planned.Add(new Planned
			{
				Clone = clone,
				NewPivotXZ = Flat(where),
				NewCenterXZ = Flat(where),
				CenterAboveGround = b.extents.y - sink,
				PivotAboveCenter = src.position.y - b.center.y,
				Rotation = turn * src.rotation,
				Scale = Vector3.Scale(src.localScale, src.parent != null ? src.parent.lossyScale : Vector3.one)
			});
		}

		private static List<Vector3> oldRoad = new List<Vector3>();

		/// <summary>
		/// Curve signs before the sharpest bends of the lap (a left sign for a left bend, on the outside of the road, 70 m before the bend), and a
		/// burning barrel on the outside of the first four bends. Little: six signs and four barrels at most.
		/// </summary>
		private static void PlanMarkers(GameObject env, Layout layout, List<Vector3> oldSamples)
		{
			if (pen == null)
			{
				return;
			}
			oldRoad = oldSamples;
			List<Transform> lefts = new List<Transform>();
			List<Transform> rights = new List<Transform>();
			foreach (Transform group in FindAll(env.transform, "Decor|Curve Signs"))
			{
				foreach (Transform sign in group)
				{
					if (sign.name.StartsWith("Left"))
					{
						lefts.Add(sign);
					}
					else if (sign.name.StartsWith("Right"))
					{
						rights.Add(sign);
					}
				}
			}
			List<Transform> fires = new List<Transform>();
			foreach (string path in new[] { "Decor|Tomberon", "Decor|GarbageInFlames" })
			{
				fires.AddRange(FindAll(env.transform, path));
			}
			int n = layout.Count;
			int window = Mathf.RoundToInt(120f / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			float[] sum = new float[n];
			for (int i = 0; i < n; i++)
			{
				float s = 0f;
				for (int q = 0; q < window; q++)
				{
					s += Loop.Turn(layout, (i + q) % n);
				}
				sum[i] = s;
			}
			List<int> corners = new List<int>();
			for (int i = ahead + 20; i < n - behind - window - 8; i++)
			{
				if (Math.Abs(sum[i]) < 0.9f)
				{
					continue;
				}
				bool peak = true;
				for (int q = -window; q <= window && peak; q++)
				{
					int j = ((i + q) % n + n) % n;
					peak = Math.Abs(sum[j]) <= Math.Abs(sum[i]) + 1e-6f;
				}
				if (peak)
				{
					corners.Add(i);
				}
			}
			corners.Sort((a, b) => Math.Abs(sum[b]).CompareTo(Math.Abs(sum[a])));
			List<int> taken = new List<int>();
			int signs = 0;
			int barrels = 0;
			float h = RoadHalfWidth;
			foreach (int c in corners)
			{
				if (signs >= 6 || !Spaced(taken, c, n, 2 * window) || !Spaced(spots, c + window / 2, n, 30))
				{
					continue;
				}
				taken.Add(c);
				bool left = sum[c] > 0f;
				float outside = left ? 1f : -1f;
				List<Transform> pool = left ? lefts : rights;
				if (pool.Count == 0)
				{
					pool = left ? rights : lefts;
				}
				int before = ((c - Mathf.RoundToInt(70f / layout.Step)) % n + n) % n;
				if (pool.Count > 0 && before > ahead + 4)
				{
Vector3 at = layout.At(before, outside * (h + 4f), 0f);
					if (!(spinnerSet && Flat(at - spinnerPos).magnitude < spinnerR))
					Mark(pool[signs % pool.Count], at, Tangent(layout, before), 0.1f, true);
					signs++;
				}
				if (fires.Count > 0 && barrels < 4)
				{
					int apex = (c + window / 2) % n;
Vector3 at = layout.At(apex, outside * (h + 6.5f), 0f);
					if (!(spinnerSet && Flat(at - spinnerPos).magnitude < spinnerR))
					Mark(fires[barrels % fires.Count], at, Vector3.forward, 0.3f, false);
					barrels++;
				}
			}
			Mod.Info(P.Key + ": " + signs + " curve signs and " + barrels + " burning barrels set at " + taken.Count + " bends");
		}

		/// <summary>A tall invisible wall just inside the four edges of the terrain: a car that fails a turn near the edge of the map stops there.</summary>
		private static void BuildEdgeWall(Ground g)
		{
			Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
			Batch wall = GetBatch(batches, "hidden edge wall", Color.black, true, true);
			float t = 6f;
			float inset = 4f;
			float low = g.Y0 - 60f;
			float high = g.Y0 + g.SizeY + 150f;
			float cy = (low + high) * 0.5f;
			float hy = high - low;
			float cx = g.X0 + g.SizeX * 0.5f;
			float cz = g.Z0 + g.SizeZ * 0.5f;
			wall.Box(new Vector3(cx, cy, g.Z0 + inset + t * 0.5f), new Vector3(g.SizeX, hy, t));
			wall.Box(new Vector3(cx, cy, g.Z0 + g.SizeZ - inset - t * 0.5f), new Vector3(g.SizeX, hy, t));
			wall.Box(new Vector3(g.X0 + inset + t * 0.5f, cy, cz), new Vector3(t, hy, g.SizeZ));
			wall.Box(new Vector3(g.X0 + g.SizeX - inset - t * 0.5f, cy, cz), new Vector3(t, hy, g.SizeZ));
			Transform parent = new GameObject("EdgeWall").transform;
			parent.SetParent(root.transform, false);
			Emit(batches, parent, null);
			Mod.Info(P.Key + ": invisible wall round the edge of the terrain");
		}
	}
}
