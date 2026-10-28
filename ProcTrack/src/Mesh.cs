using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// All the triangles of one colour. Every face has its own vertices, so Unity's normals come out flat, and each face is wound to look
	/// toward a hint direction, so no call has to get the winding right by hand. (Same idea as the Highway mod's MeshKit.)
	/// </summary>
	internal sealed class Batch
	{
		public readonly string Name;
		public readonly Color Color;
		public readonly bool Collide;
		public readonly bool Wall;
		/// <summary>Rough ground: gets the handling of grass (less grip, more drag).</summary>
		public readonly bool Rough;

		private readonly List<Vector3> verts = new List<Vector3>();
		private readonly List<int> tris = new List<int>();
		private readonly List<Vector2> uvs = new List<Vector2>();

		public Batch(string name, Color color, bool collide, bool wall = false, bool rough = false)
		{
			Name = name;
			Color = color;
			Collide = collide;
			Wall = wall;
			Rough = rough;
		}

		public int Count => verts.Count;

		public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint)
		{
			if (Vector3.Dot(Vector3.Cross(b - a, c - a), hint) < 0f)
			{
				Vector3 swap = b;
				b = d;
				d = swap;
			}
			int i = verts.Count;
			verts.Add(a);
			verts.Add(b);
			verts.Add(c);
			verts.Add(d);
			for (int k = 0; k < 4; k++)
			{
				uvs.Add(Vector2.zero);
			}
			tris.Add(i);
			tris.Add(i + 1);
			tris.Add(i + 2);
			tris.Add(i);
			tris.Add(i + 2);
			tris.Add(i + 3);
		}

		/// <summary>A quad whose corners carry texture coordinates (ua belongs to a, and so on).</summary>
		public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 hint)
		{
			int i = verts.Count;
			Quad(a, b, c, d, hint);
			// Quad swaps b and d when the winding faces away from the hint; the coordinates go with the corners.
			bool swapped = verts[i + 1] != b;
			uvs[i] = ua;
			uvs[i + 1] = swapped ? ud : ub;
			uvs[i + 2] = uc;
			uvs[i + 3] = swapped ? ub : ud;
		}

		public void Box(Vector3 center, Vector3 size)
		{
			Vector3 h = size * 0.5f;
			Vector3 x = new Vector3(h.x, 0f, 0f);
			Vector3 y = new Vector3(0f, h.y, 0f);
			Vector3 z = new Vector3(0f, 0f, h.z);
			Face(center, x, y, z);
			Face(center, -x, y, z);
			Face(center, y, x, z);
			Face(center, -y, x, z);
			Face(center, z, x, y);
			Face(center, -z, x, y);
		}

/// <summary>A box of any orientation: ax, ay, az are the three half edges from the centre.</summary>
		public void OBox(Vector3 center, Vector3 ax, Vector3 ay, Vector3 az)
		{
			Face(center, ax, ay, az);
			Face(center, -ax, ay, az);
			Face(center, ay, ax, az);
			Face(center, -ay, ax, az);
			Face(center, az, ax, ay);
			Face(center, -az, ax, ay);
		}

		private void Face(Vector3 center, Vector3 normal, Vector3 u, Vector3 v)
		{
			Vector3 c = center + normal;
			Quad(c - u - v, c + u - v, c + u + v, c - u + v, normal);
		}

		public Mesh ToMesh()
		{
			Mesh mesh = new Mesh { name = "ProcCircuit_" + Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}

	/// <summary>Things built along the whole closed loop of a lap. Indices wrap, so there is no seam where the lap closes.</summary>
	internal static class Loop
	{
		/// <summary>A strip along the lap between two (lateral offset, height) lines. Positive lateral is to the right of the direction of travel.</summary>
		public static void Ribbon(Batch b, Layout l, float latA, float liftA, float latB, float liftB, Func<int, bool> skip = null)
		{
			int n = l.Count;
			for (int i = 0; i < n; i++)
			{
				if (skip != null && skip(i))
				{
					continue;
				}
				b.Quad(l.At(i, latA, liftA), l.At(i, latB, liftB), l.At(i + 1, latB, liftB), l.At(i + 1, latA, liftA), Vector3.up);
			}
		}

		/// <summary>
		/// <see cref="Ribbon"/> with texture coordinates: u is the lateral offset over <paramref name="tile"/>, v the way along the lap over
		/// <paramref name="tile"/> (the points are evenly spaced, so it is index times step). A road mesh that borrows the old road's texture.
		/// </summary>
		public static void RibbonUV(Batch b, Layout l, float latA, float liftA, float latB, float liftB, float tile)
		{
			int n = l.Count;
			for (int i = 0; i < n; i++)
			{
				float v0 = i * l.Step / tile;
				float v1 = (i + 1) * l.Step / tile;
				b.QuadUV(l.At(i, latA, liftA), l.At(i, latB, liftB), l.At(i + 1, latB, liftB), l.At(i + 1, latA, liftA),
					new Vector2(latA / tile, v0), new Vector2(latB / tile, v0), new Vector2(latB / tile, v1), new Vector2(latA / tile, v1), Vector3.up);
			}
		}

		/// <summary>One section of a strip: the step from point i to point i + 1 only.</summary>
		public static void Cell(Batch b, Layout l, int i, float latA, float latB, float lift)
		{
			b.Quad(l.At(i, latA, lift), l.At(i, latB, lift), l.At(i + 1, latB, lift), l.At(i + 1, latA, lift), Vector3.up);
		}

		/// <summary>Sweeps a closed cross-section (lateral, height) around the whole lap. Used for the walls.</summary>
		public static void Sweep(Batch b, Layout l, Vector2[] profile)
		{
			SweepRange(b, l, profile, 0, l.Count);
		}

		/// <summary>Sweeps the cross-section along <paramref name="count"/> steps of the lap starting at point <paramref name="from"/> (wrapping round the end).</summary>
		public static void SweepRange(Batch b, Layout l, Vector2[] profile, int from, int count)
		{
			int n = l.Count;
			int m = profile.Length;
			float cx = 0f;
			float cy = 0f;
			foreach (Vector2 v in profile)
			{
				cx += v.x;
				cy += v.y;
			}
			cx /= m;
			cy /= m;
			for (int q = 0; q < count; q++)
			{
				int i = (from + q) % n;
				int j = (i + 1) % n;
				Vector3 r0 = l.Right(i);
				Vector3 r1 = l.Right(j);
				Vector3 p0 = l.Points[i];
				Vector3 p1 = l.Points[j];
				for (int k = 0; k < m; k++)
				{
					Vector2 a = profile[k];
					Vector2 c = profile[(k + 1) % m];
					Vector3 hint = r0 * ((a.x + c.x) * 0.5f - cx) + Vector3.up * ((a.y + c.y) * 0.5f - cy);
					b.Quad(p0 + r0 * a.x + Vector3.up * a.y, p0 + r0 * c.x + Vector3.up * c.y,
						p1 + r1 * c.x + Vector3.up * c.y, p1 + r1 * a.x + Vector3.up * a.y, hint);
				}
			}
		}

		/// <summary>Signed turn at point i: negative is a right turn, positive a left turn, in radians per point (small angles).</summary>
		public static float Turn(Layout l, int i)
		{
			int n = l.Count;
			Vector3 a = l.Points[i] - l.Points[(i - 1 + n) % n];
			Vector3 c = l.Points[(i + 1) % n] - l.Points[i];
			float cross = a.x * c.z - a.z * c.x;
			float len = Mathf.Max(1e-3f, a.magnitude * c.magnitude);
			return cross / len;
		}
	}
}
