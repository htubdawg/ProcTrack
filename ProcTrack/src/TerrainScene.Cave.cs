using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// The Mountain's own cave (its ice tube, snow walls and icicles), bent to follow the new road. Each vertex of each of its meshes is
	/// measured against the old road where it ran through the cave (how far along, how far to the side, how high above it) and put as far
	/// along, to the side and above the new road through the tunnel stretch. If any of its meshes cannot be read while the game runs, the
	/// built tunnel (TerrainScene.Tunnel) stands in for it.
	/// </summary>
	internal static partial class TerrainScene
	{
		/// <summary>The old road through the cave, resampled every CaveStep metres and run on past both its ends; null when there is no cave.</summary>
		private static List<Vector3> caveLine;
		/// <summary>The distance along caveLine to each of its points.</summary>
		private static List<float> caveAlong;
		/// <summary>How far along caveLine the cave's own stretch of road starts, and how long it is.</summary>
		private static float caveStart;
		private static float caveLength;
		private const float CaveStep = 2f;

		/// <summary>
		/// The cave's bounds and the stretch of the old road through them (the longest run of old track samples inside), measured while the
		/// cave is switched on: a switched off renderer has no bounds. Sets <see cref="TunnelLength"/> to the cave's length on the new lap.
		/// </summary>
		private static void MeasureCave(GameObject env, List<Vector3> oldSamples, Ground oldGround, float step)
		{
			caveLine = null;
			Transform cave = FindFirst(env.transform, "Decoration|Cave");
			Renderer[] rs = cave != null ? cave.GetComponentsInChildren<Renderer>() : new Renderer[0];
			if (rs.Length == 0 || oldSamples.Count < 4)
			{
				return;
			}
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs)
			{
				b.Encapsulate(r.bounds);
			}
			int m = oldSamples.Count;
			bool[] inside = new bool[m];
			for (int i = 0; i < m; i++)
			{
				Vector3 p = oldSamples[i];
				inside[i] = p.x > b.min.x - 2f && p.x < b.max.x + 2f && p.z > b.min.z - 2f && p.z < b.max.z + 2f;
			}
			// The longest run of samples inside, round the closed line.
			int bestFrom = -1;
			int bestCount = 0;
			for (int i = 0; i < m; i++)
			{
				if (!inside[i] || inside[(i - 1 + m) % m])
				{
					continue;
				}
				int count = 0;
				while (count < m && inside[(i + count) % m])
				{
					count++;
				}
				if (count > bestCount)
				{
					bestCount = count;
					bestFrom = i;
				}
			}
			if (bestCount < 2)
			{
				Mod.Info(P.Key + ": the old road does not run through the cave's bounds; the tunnel is built.");
				return;
			}
			// Catmull-Rom through the samples from three before the run to three after it, every CaveStep metres: the samples are some 20 m
			// apart, and measuring against their straight segments would fold the vertices at each sample.
			List<Vector3> control = new List<Vector3>();
			for (int k = -4; k <= bestCount + 3; k++)
			{
				control.Add(oldSamples[((bestFrom + k) % m + m) % m]);
			}
			caveLine = new List<Vector3>();
			caveAlong = new List<float>();
			float along = 0f;
			for (int c = 1; c + 2 < control.Count; c++)
			{
				Vector3 p0 = control[c - 1], p1 = control[c], p2 = control[c + 1], p3 = control[c + 2];
				int parts = Math.Max(1, Mathf.CeilToInt((p2 - p1).magnitude / CaveStep));
				for (int s = 0; s < parts; s++)
				{
					float t = (float)s / parts;
					float t2 = t * t;
					float t3 = t2 * t;
					Vector3 p = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
					if (caveLine.Count > 0)
					{
						along += (p - caveLine[caveLine.Count - 1]).magnitude;
					}
					// The run's first sample is control point 4, its last control point bestCount + 3.
					if (c == 4 && s == 0)
					{
						caveStart = along;
					}
					if (c == bestCount + 3 && s == 0)
					{
						caveLength = along - caveStart;
					}
					caveLine.Add(p);
					caveAlong.Add(along);
				}
			}
			// Heights are taken from the old ground under the road, not from the track line, which stands at one height (3) the whole lap
			// round while the road (near 0 at the start line) follows the crater floor. The original tube stands on that ground.
			for (int k = 0; k < caveLine.Count; k++)
			{
				Vector3 p = caveLine[k];
				caveLine[k] = new Vector3(p.x, oldGround.At(p.x, p.z), p.z);
			}
			TunnelLength = Mathf.Clamp(Mathf.RoundToInt(caveLength / step), 20, 90);
			Mod.Info(P.Key + ": the cave spans " + caveLength.ToString("0") + " m of the old road (" + bestCount + " samples); its stretch of the new lap is " + (TunnelLength * step).ToString("0") + " m");
		}

		/// <summary>
		/// A copy of the cave with every mesh bent onto the new road from lap point <paramref name="from"/>. False, and nothing built, when
		/// there is no measured cave or one of its meshes cannot be read.
		/// </summary>
		private static bool BendCave(GameObject env, Layout layout, int from)
		{
			Transform cave = FindFirst(env.transform, "Decoration|Cave");
			if (caveLine == null || cave == null)
			{
				return false;
			}
			foreach (MeshFilter f in cave.GetComponentsInChildren<MeshFilter>(true))
			{
				if (f.sharedMesh != null && !f.sharedMesh.isReadable)
				{
					Mod.Log.LogWarning(P.Key + ": the cave's mesh " + f.sharedMesh.name + " cannot be read while the game runs, so the cave cannot be bent onto the new road; the tunnel is built instead.");
					return false;
				}
			}
			// The copy keeps the original's place, so each of its parts has the same transform as the part it was copied from. The original
			// is switched off by now, so the copy starts switched off, and the scene's mirroring is taken off it before it wakes.
			GameObject copy = UnityEngine.Object.Instantiate(cave.gameObject, cave.position, cave.rotation, root.transform);
			copy.name = "Cave";
			copy.transform.localScale = cave.lossyScale;
			Component mirror = copy.GetComponent("MirrorGeometry");
			if (mirror != null)
			{
				UnityEngine.Object.DestroyImmediate(mirror);
			}
			float scale = TunnelLength * layout.Step / Math.Max(1f, caveLength);
			int meshes = 0;
			int vertices = 0;
			Dictionary<Mesh, Mesh> bentFor = new Dictionary<Mesh, Mesh>();
			foreach (MeshFilter f in copy.GetComponentsInChildren<MeshFilter>(true))
			{
				Mesh src = f.sharedMesh;
				if (src == null)
				{
					continue;
				}
				Matrix4x4 toWorld = f.transform.localToWorldMatrix;
				Matrix4x4 toLocal = f.transform.worldToLocalMatrix;
				Vector3[] v = src.vertices;
				for (int i = 0; i < v.Length; i++)
				{
					v[i] = toLocal.MultiplyPoint3x4(Bend(toWorld.MultiplyPoint3x4(v[i]), layout, from, scale));
				}
				Mesh bent = UnityEngine.Object.Instantiate(src);
				bent.name = src.name + " (bent)";
				bent.vertices = v;
				bent.RecalculateNormals();
				bent.RecalculateTangents();
				bent.RecalculateBounds();
				f.sharedMesh = bent;
				bentFor[src] = bent;
				meshes++;
				vertices += v.Length;
				// A collider on the same part, on the same mesh, follows it.
				if (f.TryGetComponent(out MeshCollider collider) && collider.sharedMesh == src)
				{
					collider.sharedMesh = null;
					collider.sharedMesh = bent;
				}
			}
			copy.SetActive(true);
			Mod.Info(P.Key + ": the cave's own " + meshes + " meshes (" + vertices + " vertices) bent onto the new road from lap point " + from + ", " + (TunnelLength * layout.Step).ToString("0") + " m");
			return true;
		}

		/// <summary>
		/// A point measured against the old road through the cave (along it, to its right, above it) and put the same way against the new road
		/// from lap point <paramref name="from"/>, its distance along stretched by <paramref name="scale"/>.
		/// </summary>
		private static Vector3 Bend(Vector3 w, Layout layout, int from, float scale)
		{
			int best = 0;
			float bestD = float.MaxValue;
			for (int k = 0; k < caveLine.Count; k++)
			{
				float dx = caveLine[k].x - w.x;
				float dz = caveLine[k].z - w.z;
				float d = dx * dx + dz * dz;
				if (d < bestD)
				{
					bestD = d;
					best = k;
				}
			}
			// The nearest point on the segment before or after the nearest sample.
			int a = Math.Max(0, best - 1);
			int c = Math.Min(caveLine.Count - 1, best + 1);
			Vector3 p0 = caveLine[a];
			Vector3 p1 = caveLine[c];
			Vector3 seg = new Vector3(p1.x - p0.x, 0f, p1.z - p0.z);
			float len = Math.Max(1e-3f, seg.magnitude);
			Vector3 dir = seg / len;
			float t = Mathf.Clamp(Vector3.Dot(new Vector3(w.x - p0.x, 0f, w.z - p0.z), dir), 0f, len);
			Vector3 on = p0 + (p1 - p0) * (t / len);
			Vector3 right = new Vector3(dir.z, 0f, -dir.x);
			float lateral = Vector3.Dot(new Vector3(w.x - on.x, 0f, w.z - on.z), right);
			float height = w.y - on.y;
			float s = caveAlong[a] + t - caveStart;

			// The same place on the new road: lap points are layout.Step apart from lap point from.
			int n = layout.Count;
			float u = from + s * scale / layout.Step;
			int i = Mathf.FloorToInt(u);
			float f = u - i;
			int i0 = ((i % n) + n) % n;
			int i1 = (i0 + 1) % n;
			Vector3 p = Vector3.Lerp(layout.Points[i0], layout.Points[i1], f);
			Vector3 r = Vector3.Lerp(layout.Right(i0), layout.Right(i1), f).normalized;
			return p + r * lateral + Vector3.up * height;
		}
	}
}
