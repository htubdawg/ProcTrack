using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// The parts of a terrain map beyond the road itself: the old track's hazards carried onto the new lap, the tornado put across the road,
	/// the Tokyo wall and city blocks, and trees beside the Mountain road. Everything is chosen from the seed.
	/// </summary>
	internal static partial class TerrainScene
	{
		// ---- carrying the old track's hazards onto the new lap -----------------------------------------------------

		private sealed class Planned
		{
			public GameObject Clone;
			public Vector3 NewPivotXZ;
			public Vector3 NewCenterXZ;
			public float CenterAboveGround;
			public float PivotAboveCenter;
			public Quaternion Rotation;
			public Vector3 Scale;
			/// <summary>Set on the main piece of a carried hall: parts of it that the rest of the lap would run into are switched off (lap points outside the window round the anchor).</summary>
			public bool Prune;
			public int PruneMid;
			public int PruneBack;
		public int PruneAhead;
			/// <summary>Half the size of the carried hall on the ground (scaled): with Prune set, it is sunk to the lowest ground under it, never left floating.</summary>
public Vector3 Extent;
			/// <summary>The footprint of the hall's main building alone (not the yard and ground round it): its middle on the new lap, half sizes, and the turn that took it there.</summary>
			public bool HasHall;
			public Vector3 HallCentre;
			public Vector3 HallHalf;
public Quaternion HallTurn;
			/// <summary>The main building's triangles seen from above on the new lap, as corner triples (x, z); null when the mesh cannot be read.</summary>
public Vector2[] HallTris;
			/// <summary>Where the hall's giant spinner is to stand (flat), found by its place against the old road, not by the hall's own placing.</summary>
			public bool HasSpinner;
			public Vector3 SpinnerAt;
		}

		private struct Area
		{
			public float MinX, MaxX, MinZ, MaxZ;
		}

		private static readonly List<Planned> planned = new List<Planned>();
		private static Transform pen;
private static Layout lap;
		/// <summary>The lap's first big U-turn the hall was put on: the middle of its circle, its radius, and its apex point. Set by CarryTurn.</summary>
private static bool uSet;
		private static int uMid;
		/// <summary>Where the giant spinner stands (flat) and how far its arms sweep, with a margin: nothing else may stand in this circle (the road may cross it).</summary>
		private static bool spinnerSet;
		private static Vector3 spinnerPos;
		private static float spinnerR;
		private static Vector3 uCentre;
		private static Vector3 uApex;
		private static float uRadius;
		private static float uScale = 1f;
		/// <summary>Lap points the hazards were put at; the tunnel keeps clear of them.</summary>
		private static readonly List<int> spots = new List<int>();

		/// <summary>
		/// For each hazard the profile lists: a copy is made (switched off, in a pen, so none of its scripts run in the wrong place), and a
		/// spot is found on the new lap. The copy is put where the new road is as the original stood to the old road: the same side, the
		/// same distance from it, the same angle to it, the same height above the ground. The original is hidden. The copies are put on
		/// the ground and switched on by <see cref="LandTransplants"/> once the terrain is final.
		/// </summary>
		private static void PlanTransplants(GameObject env, Layout layout, List<Vector3> oldSamples, Ground oldGround, int seed)
		{
planned.Clear();
			spots.Clear();
uSet = false;
			spinnerSet = false;
			lap = layout;
			if (P.Transplants.Length == 0)
			{
				return;
			}
			if (oldSamples.Count < 3)
			{
				Mod.Log.LogWarning(P.Key + ": the old track line has no samples, its hazards are not carried over.");
				return;
			}
			int n = layout.Count;
			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0x4A5A2DUL);
			}
			pen = new GameObject("Pen").transform;
			pen.SetParent(root.transform, false);
			pen.gameObject.SetActive(false);

			List<int> used = spots;
			foreach (Transplant tp in P.Transplants)
			{
				List<Transform> sources = new List<Transform>();
				foreach (Transform t in FindAll(env.transform, tp.Path))
				{
					if (tp.Each)
					{
						foreach (Transform child in t)
						{
							if (child.gameObject.activeSelf)
							{
								sources.Add(child);
							}
						}
					}
					else if (t.gameObject.activeSelf)
					{
						sources.Add(t);
					}
				}
				if (sources.Count == 0)
				{
					Mod.Info(P.Key + ": no " + tp.Name + " found at " + tp.Path + " (switched off in this variant of the scene), none carried over.");
					continue;
				}
				List<Transform> also = new List<Transform>();
				foreach (string path in tp.Also)
				{
					foreach (Transform t in FindAll(env.transform, path))
					{
						if (t.gameObject.activeSelf)
						{
							also.Add(t);
						}
					}
				}
				int made = 0;
				for (int k = 0; k < tp.Count; k++)
				{
					List<Transform> items = new List<Transform> { sources[k % sources.Count] };
					foreach (Transform a in also)
					{
						if (tp.AlsoRadius <= 0f || (a.position - items[0].position).magnitude <= tp.AlsoRadius)
						{
							items.Add(a);
						}
					}
					if (tp.Turn ? CarryTurn(items, tp, layout, oldSamples, oldGround, used, ahead) : Carry(items, tp, layout, oldSamples, oldGround, rng, used, behind, ahead))
					{
						made++;
					}
				}
				Mod.Info(P.Key + ": " + made + " of " + tp.Count + " " + tp.Name + " carried onto the new lap");
			}
		}

/// <summary>The long horizontal axis of an object's meshes (the principal axis of its vertices seen from above). False when the meshes cannot be read.</summary>
		private static bool PrincipalAxis(Transform item, out Vector3 axis)
		{
			axis = Vector3.right;
			double sx = 0, sz = 0, sxx = 0, szz = 0, sxz = 0;
			int count = 0;
			try
			{
				foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
				{
					Mesh mesh = filter.sharedMesh;
					if (mesh == null || !mesh.isReadable)
					{
						continue;
					}
					Vector3[] v = mesh.vertices;
					Matrix4x4 w = filter.transform.localToWorldMatrix;
					int stride = Math.Max(1, v.Length / 3000);
					for (int i = 0; i < v.Length; i += stride)
					{
						Vector3 p = w.MultiplyPoint3x4(v[i]);
						sx += p.x; sz += p.z; sxx += (double)p.x * p.x; szz += (double)p.z * p.z; sxz += (double)p.x * p.z;
						count++;
					}
				}
			}
			catch (Exception)
			{
				return false;
			}
			if (count < 8)
			{
				return false;
			}
			double mx = sx / count;
			double mz = sz / count;
			double cxx = sxx / count - mx * mx;
			double czz = szz / count - mz * mz;
			double cxz = sxz / count - mx * mz;
			if (Math.Abs(cxx - czz) + Math.Abs(cxz) < 1e-3)
			{
				return false;
			}
			double angle = 0.5 * Math.Atan2(2.0 * cxz, cxx - czz);
			axis = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
			return true;
		}

		private static bool Carry(List<Transform> items, Transplant tp, Layout layout, List<Vector3> oldSamples, Ground oldGround, Rng rng, List<int> used, int behind, int ahead)
		{
			int n = layout.Count;
			List<Renderer> list = new List<Renderer>();
			foreach (Transform item in items)
			{
				list.AddRange(item.GetComponentsInChildren<Renderer>());
			}
			Renderer[] renderers = list.ToArray();
			Bounds all = new Bounds(items[0].position, Vector3.zero);
			bool any = false;
			foreach (Renderer r in renderers)
			{
				if (!any)
				{
					all = r.bounds;
					any = true;
				}
				else
				{
					all.Encapsulate(r.bounds);
				}
			}
			Vector3 c = all.center;

			// The old road at the object: its nearest sample and the way it ran there.
			int m = oldSamples.Count;
			int nearest = 0;
			float nearestD = float.MaxValue;
			for (int i = 0; i < m; i++)
			{
				float dx = oldSamples[i].x - c.x;
				float dz = oldSamples[i].z - c.z;
				float d = dx * dx + dz * dz;
				if (d < nearestD)
				{
					nearestD = d;
					nearest = i;
				}
			}
			Vector3 pOld = oldSamples[nearest];
			Vector3 fOld = oldSamples[Math.Min(nearest + 1, m - 1)] - oldSamples[Math.Max(nearest - 1, 0)];
			fOld.y = 0f;
			fOld = fOld.sqrMagnitude > 1e-6f ? fOld.normalized : Vector3.right;

if (tp.CentreOnRoad && PrincipalAxis(items[0], out Vector3 axis))
			{
				// The ramp's own long axis, not the old road's heading at its nearest sample, is what has to lie along the new road.
				float dAxis = Vector3.SignedAngle(fOld, axis, Vector3.up);
				while (dAxis > 90f) { dAxis -= 180f; }
				while (dAxis < -90f) { dAxis += 180f; }
				if (Math.Abs(dAxis) < 45f)
				{
					fOld = Quaternion.AngleAxis(dAxis, Vector3.up) * fOld;
					Mod.Info(P.Key + ": " + tp.Name + " lay " + dAxis.ToString("0.0") + " degrees off the old road's heading, lined up with the new road");
				}
			}

			float alongHalf = 0f;
			Vector3 e = all.extents;
			for (int corner = 0; corner < 8; corner++)
			{
				Vector3 p = c + Vector3.Scale(e, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
				alongHalf = Math.Max(alongHalf, Math.Abs(Vector3.Dot(p - c, fOld)));
			}
			float scale = 1f;
			if (tp.MaxHalf > 0f && alongHalf > tp.MaxHalf)
			{
				scale = Mathf.Max(tp.MinScale, tp.MaxHalf / alongHalf);
			}
			int half = tp.OnRoad ? Mathf.Clamp(Mathf.CeilToInt((alongHalf * scale + 15f) / layout.Step), 6, 45) : 8;

			int lo = ahead + half + 10;
			int hi = n - behind - half - 10;
			if (hi <= lo)
			{
				return false;
			}
			List<int> candidates = new List<int>();
			for (int i = lo; i < hi; i += 2)
			{
				candidates.Add(i);
			}
			for (int i = candidates.Count - 1; i > 0; i--)
			{
				int j = (int)(rng.Next() * (i + 1));
				int swap = candidates[i];
				candidates[i] = candidates[j];
				candidates[j] = swap;
			}

			Area area = TerrainArea();
			int chosen = -1;
			int fewest = int.MaxValue;
			// First only the straightest stretches; if the lap has none, a looser test.
			foreach (float bend in new[] { 0.10f, 0.35f })
			{
				int evaluated = 0;
				foreach (int t in candidates)
				{
						if (!Spaced(used, t, n, tp.Spacing) || !Straight(layout, t, half, bend))
						{
							continue;
						}
						if (tp.StraightStart)
						{
							// A straight run ahead, and (on the first pass) a bend just behind: the start of a straight.
							if (!Straight(layout, t + 22, 22, bend))
							{
								continue;
							}
							if (bend < 0.2f && Straight(layout, t - 22, 22, 0.25f))
							{
								continue;
							}
						}
					int bad = Overlaps(renderers, layout, t, half, pOld, fOld, scale, tp.OnRoad, area);
					if (bad < fewest)
					{
						fewest = bad;
						chosen = t;
					}
					if (bad == 0 || ++evaluated >= 80)
					{
						break;
					}
				}
				if (chosen >= 0 && fewest <= (tp.OnRoad ? 3 : 0))
				{
					break;
				}
			}
			if (chosen < 0 || fewest > (tp.OnRoad ? 3 : 0))
			{
				Mod.Info(P.Key + ": no room found for the " + tp.Name + " on this lap (" + (chosen < 0 ? "no straight enough" : fewest + " road points would run into it") + "), it is left out.");
				return false;
			}
			used.Add(chosen);

			Vector3 pNew = layout.Points[chosen];
			Vector3 fNew = Tangent(layout, chosen);
Vector3 shift = Vector3.zero;
			if (tp.CentreOnRoad)
			{
				// The old object stood across the road at some offset; the copy is slid along the road's cross direction until its middle is
				// within the middle 45 percent of the road, on the side it stood, so it is on the tarmac and not on the grass beside it.
				Quaternion tn = Quaternion.AngleAxis(Vector3.SignedAngle(fOld, fNew, Vector3.up), Vector3.up);
				Vector3 centreNew = Flat(pNew) + tn * (Flat(c - pOld) * scale);
				Vector3 across = layout.Right(chosen);
				float offset = Vector3.Dot(centreNew - Flat(pNew), across);
				float wanted = Mathf.Clamp(offset, -0.45f * RoadHalfWidth, 0.45f * RoadHalfWidth);
				shift = across * (wanted - offset);
				Mod.Info(P.Key + ": " + tp.Name + " stood " + offset.ToString("0.0") + " m off the road's middle, slid " + (wanted - offset).ToString("0.0") + " m across");
			}
			AddPlans(items, tp, pOld, pNew, fOld, fNew, scale, c, oldGround, false, 0, 0, 0, shift);
			Mod.Info(P.Key + ": " + tp.Name + " (" + items.Count + " objects, " + renderers.Length + " renderers, half length " + alongHalf.ToString("0") + " m, scale " + scale.ToString("0.00") + ") put at lap point " + chosen + " of " + n + ", " + fewest + " overlapping road points");
			return true;
		}

		/// <summary>
		/// Makes the copies (switched off, in the pen) of the items, moved by the rigid map that takes the old point pOld, heading fOld to the
		/// new point pNew, heading fNew, sized by scale about it. c is the middle of the items' bounds, for the height above the ground.
		/// </summary>
		private static void AddPlans(List<Transform> items, Transplant tp, Vector3 pOld, Vector3 pNew, Vector3 fOld, Vector3 fNew, float scale, Vector3 c, Ground oldGround,bool prune, int mid, int back, int ahead, Vector3 shift = default(Vector3), Vector3 extent = default(Vector3))
		{
			Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(fOld, fNew, Vector3.up), Vector3.up);
			bool first = true;
			foreach (Transform src in items)
			{
				GameObject clone = UnityEngine.Object.Instantiate(src.gameObject, pen, false);
				clone.name = src.name;
				Vector3 pivot = src.position;
				planned.Add(new Planned
				{
					Clone = clone,
NewPivotXZ = Flat(pNew) + turn * (Flat(pivot - pOld) * scale) + shift,
					NewCenterXZ = Flat(pNew) + turn * (Flat(c - pOld) * scale) + shift,
					CenterAboveGround = (c.y - oldGround.At(c.x, c.z)) * scale,
					PivotAboveCenter = (pivot.y - c.y) * scale,
					Rotation = turn * src.rotation,
					Scale = Vector3.Scale(src.localScale, src.parent != null ? src.parent.lossyScale : Vector3.one) * scale,
					Prune = prune && first,
					PruneMid = mid,
					PruneBack = back,
PruneAhead = ahead,
					Extent = extent
				});
if (prune && first)
				{
					Planned made = planned[planned.Count - 1];
					foreach (Transform sub in src.GetComponentsInChildren<Transform>())
					{
						Renderer main = sub.name == "factory" ? sub.GetComponent<Renderer>() : null;
						if (main != null)
						{
							Vector3 fc = main.bounds.center;
							made.HasHall = true;
							made.HallCentre = Flat(pNew) + turn * (Flat(fc - pOld) * scale) + shift;
							made.HallHalf = main.bounds.extents * scale;
made.HallTurn = turn;
							MeshFilter mf = sub.GetComponent<MeshFilter>();
							if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
							{
								Vector3[] mv = mf.sharedMesh.vertices;
								int[] mt = mf.sharedMesh.triangles;
								Vector2[] flat = new Vector2[mv.Length];
								for (int vi = 0; vi < mv.Length; vi++)
								{
									Vector3 nwp = Flat(pNew) + turn * (Flat(sub.TransformPoint(mv[vi]) - pOld) * scale) + shift;
									flat[vi] = new Vector2(nwp.x, nwp.z);
								}
								Vector2[] tris = new Vector2[mt.Length];
								for (int ti = 0; ti < mt.Length; ti++)
								{
									tris[ti] = flat[mt[ti]];
								}
								made.HallTris = tris;
							}
							break;
						}
					}
				}
				first = false;
				if (tp.HideOriginal)
				{
					Hide(src.gameObject);
				}
			}
		}

		/// <summary>The point <paramref name="s"/> metres along the closed line from point mid (backwards when negative).</summary>
		private static Vector3 AtArc(IList<Vector3> pts, int mid, float s)
		{
			int n = pts.Count;
			int dir = s >= 0f ? 1 : -1;
			float left = Math.Abs(s);
			int i = mid;
			for (int guard = 0; guard < n; guard++)
			{
				int j = ((i + dir) % n + n) % n;
				float seg = Flat(pts[j] - pts[i]).magnitude;
				if (seg >= left)
				{
					return Vector3.Lerp(pts[i], pts[j], seg > 1e-4f ? left / seg : 0f);
				}
				left -= seg;
				i = j;
			}
			return pts[i];
		}

		private struct TurnInfo
		{
			public Vector3 Centre;
			public float Radius;
			public float Heading;
			public int Mid;
			public float Delta;
			public float Arc;
		}

		/// <summary>
		/// The sharpest left bend of <paramref name="arc"/> metres of road between points from and to: the window that turns most. Returns the
		/// middle of the bend, the way the road heads there, and the centre and radius of the circle it follows. When within is above 0, only
		/// windows whose middle is that close to <paramref name="near"/> count.
		/// </summary>
		private static bool FindTurn(IList<Vector3> pts, int from, int to, float arc, Vector3 near, float within, out TurnInfo info)
		{
			info = default;
			int n = pts.Count;
			to = Math.Min(to, n - 1);
			float[] length = new float[n];
			float[] heading = new float[n];
			for (int i = 1; i < n; i++)
			{
				length[i] = length[i - 1] + Flat(pts[i] - pts[i - 1]).magnitude;
			}
			float previous = 0f;
			for (int i = 0; i < n; i++)
			{
				Vector3 d = pts[Math.Min(i + 1, n - 1)] - pts[Math.Max(i - 1, 0)];
				float h = Mathf.Atan2(d.z, d.x);
				if (i > 0)
				{
					while (h - previous > Mathf.PI)
					{
						h -= 2f * Mathf.PI;
					}
					while (h - previous < -Mathf.PI)
					{
						h += 2f * Mathf.PI;
					}
				}
				heading[i] = h;
				previous = h;
			}
			float best = 0.5f;
			bool found = false;
			for (int i = Math.Max(from, 0); i < to; i++)
			{
				int j = i;
				while (j < to && length[j] - length[i] < arc)
				{
					j++;
				}
				if (length[j] - length[i] < arc * 0.8f)
				{
					break;
				}
				int mid = (i + j) / 2;
				if (within > 0f && Flat(pts[mid] - near).magnitude > within)
				{
					continue;
				}
				float delta = heading[j] - heading[i];
				if (delta > best)
				{
					best = delta;
					found = true;
					float run = length[j] - length[i];
					float h = heading[mid];
					Vector3 left = new Vector3(-Mathf.Sin(h), 0f, Mathf.Cos(h));
					info = new TurnInfo { Mid = mid, Heading = h, Delta = delta, Arc = run, Radius = run / delta, Centre = Flat(pts[mid]) + left * (run / delta) };
				}
			}
			return found;
		}

		/// <summary>
		/// For the factory hall: the old road turns through it in a U-turn (left, about 170 degrees, radius about 150). The hall, the giant
		/// spinner that stands on the far side of the turn from it and the three hammers go on the lap's first U-turn, with the same relation
		/// to it: the centres of the two turns are put together, the hall is turned to the way the new turn heads and sized by the ratio of
		/// the radii (between 0.8 and 1.35).
		/// </summary>
		private static bool CarryTurn(List<Transform> items, Transplant tp, Layout layout, List<Vector3> oldSamples, Ground oldGround, List<int> used, int ahead)
		{
			List<Renderer> list = new List<Renderer>();
			foreach (Transform item in items)
			{
				list.AddRange(item.GetComponentsInChildren<Renderer>());
			}
			if (list.Count == 0)
			{
				return false;
			}
// The hall's own bounds place the turn (the barrel machine and the hammers that go with it may reach far from it).
			Renderer[] hallRenderers = items[0].GetComponentsInChildren<Renderer>();
			if (hallRenderers.Length == 0)
			{
				return false;
			}
			Bounds all = hallRenderers[0].bounds;
			foreach (Renderer r in hallRenderers)
			{
				all.Encapsulate(r.bounds);
			}
			Vector3 c = all.center;
			if (!FindTurn(oldSamples, 0, oldSamples.Count - 1, 360f, c, 450f, out TurnInfo o))
			{
				Mod.Log.LogWarning(P.Key + ": the old road has no U-turn near the " + tp.Name + ", it is left out.");
				return false;
			}
			if (!FindTurn(layout.Points, ahead + 10, layout.Count - 1, 360f, Vector3.zero, 0f, out TurnInfo nw))
			{
				Mod.Log.LogWarning(P.Key + ": the new lap has no U-turn to put the " + tp.Name + " on, it is left out.");
				return false;
			}
float scale = Mathf.Clamp(nw.Radius / o.Radius, 0.8f, 1.35f);
			uSet = true;
			uCentre = nw.Centre;
			uApex = layout.Points[nw.Mid];
uRadius = nw.Radius;
			uMid = nw.Mid;
			uScale = scale;
// Fit: the old road from just before the apex to 500 m past it, laid on the new road at the same distances (the new ones
			// stretched by the scale), by the turn and shift that bring them closest. The exit then follows the old exit, which is what
			// runs through the hall.
			List<Vector3> oa = new List<Vector3>();
			List<Vector3> na = new List<Vector3>();
for (float d = -260f; d <= 500f; d += 20f)
			{
				oa.Add(Flat(AtArc(oldSamples, o.Mid, d)));
				na.Add(Flat(AtArc(layout.Points, nw.Mid, d * scale)));
			}
			Vector3 co = Vector3.zero;
			Vector3 cn = Vector3.zero;
			for (int k = 0; k < oa.Count; k++)
			{
				co += oa[k];
				cn += na[k];
			}
			co /= oa.Count;
			cn /= na.Count;
			float sinSum = 0f;
			float cosSum = 0f;
			for (int k = 0; k < oa.Count; k++)
			{
				Vector3 u = oa[k] - co;
				Vector3 v = na[k] - cn;
				sinSum += Vector3.Cross(u, v).y;
				cosSum += Vector3.Dot(u, v);
			}
			float fit = Mathf.Atan2(sinSum, cosSum) * Mathf.Rad2Deg;
			Quaternion fitTurn = Quaternion.AngleAxis(fit, Vector3.up);
			float off = 0f;
			for (int k = 0; k < oa.Count; k++)
			{
				off += (cn + fitTurn * ((oa[k] - co) * scale) - na[k]).magnitude;
			}
			Vector3 fOld = Vector3.right;
			Vector3 fNew = fitTurn * Vector3.right;
			o.Centre = co;
			nw.Centre = cn;
			Mod.Info(P.Key + ": the " + tp.Name + " is fitted to the new road with an average miss of " + (off / oa.Count).ToString("0.0") + " m over 600 m of the exit");
			used.Add(nw.Mid);
CollectProtos(items[0]);
			Vector3 spinnerAt = Vector3.zero;
			bool hasSpinner = false;
			foreach (Transform sub in items[0].GetComponentsInChildren<Transform>())
			{
				if (sub.name != "spinner")
				{
					continue;
				}
				// Its place against the old road: the arc distance of the nearest old point from the turn's middle, and how far to the left of the road it stands.
				int mOld = oldSamples.Count;
				int near = 0;
				float nearD = float.MaxValue;
				for (int i = 0; i < mOld; i++)
				{
					float d2 = Flat(oldSamples[i] - sub.position).sqrMagnitude;
					if (d2 < nearD)
					{
						nearD = d2;
						near = i;
					}
				}
				float sOld = 0f;
				int step = near >= o.Mid ? 1 : -1;
				for (int i = o.Mid; i != near; i += step)
				{
					sOld += step * Flat(oldSamples[Mathf.Clamp(i + step, 0, mOld - 1)] - oldSamples[i]).magnitude;
				}
				Vector3 tOld = Flat(oldSamples[Math.Min(near + 1, mOld - 1)] - oldSamples[Math.Max(near - 1, 0)]);
				tOld = tOld.sqrMagnitude > 1e-6f ? tOld.normalized : Vector3.right;
				float lat = Vector3.Dot(Flat(sub.position - oldSamples[near]), new Vector3(-tOld.z, 0f, tOld.x));
float alongNew = P.SpinnerAlong > 0f ? P.SpinnerAlong : sOld * scale;
				Vector3 pa = AtArc(layout.Points, nw.Mid, alongNew);
Vector3 tNew = Flat(AtArc(layout.Points, nw.Mid, alongNew + 5f) - AtArc(layout.Points, nw.Mid, alongNew - 5f));
				tNew = tNew.sqrMagnitude > 1e-6f ? tNew.normalized : Vector3.right;
float latUse = P.SpinnerLateral > 0f ? Mathf.Sign(lat == 0f ? 1f : lat) * P.SpinnerLateral : lat * scale;
				spinnerAt = Flat(pa) + new Vector3(-tNew.z, 0f, tNew.x) * latUse;
hasSpinner = true;
				Renderer[] spinRenderers = sub.GetComponentsInChildren<Renderer>();
				if (spinRenderers.Length > 0)
				{
					Bounds sb = spinRenderers[0].bounds;
					foreach (Renderer sr in spinRenderers)
					{
						sb.Encapsulate(sr.bounds);
					}
					spinnerR = Mathf.Sqrt(sb.extents.x * sb.extents.x + sb.extents.z * sb.extents.z) * scale + 25f;
				}
				else
				{
					spinnerR = 130f;
				}
				spinnerSet = true;
				spinnerPos = spinnerAt;
				Mod.Info(P.Key + ": the spinner stood " + lat.ToString("0") + " m to the left of the old road at " + sOld.ToString("0") + " m from the turn's middle; it goes " + (lat * scale).ToString("0") + " m to the left of the new road at lap point (" + pa.x.ToString("0") + "," + pa.z.ToString("0") + ")");
				break;
			}
AddPlans(items, tp, o.Centre, nw.Centre, fOld, fNew, scale, c, oldGround, true, nw.Mid, 120, 190, default(Vector3), all.extents * scale);
			if (hasSpinner)
			{
				Planned first = planned[planned.Count - items.Count];
				first.HasSpinner = true;
				first.SpinnerAt = spinnerAt;
			}
			Mod.Info(P.Key + ": " + tp.Name + " (" + items.Count + " objects, " + list.Count + " renderers, bounds " + all.size.x.ToString("0") + " x " + all.size.z.ToString("0") + " m, centre " + c.x.ToString("0") + "," + c.z.ToString("0") + ")put round the first U-turn: old turn radius " + o.Radius.ToString("0") + " over " + (o.Delta * Mathf.Rad2Deg).ToString("0") + " degrees at " + o.Centre.x.ToString("0") + "," + o.Centre.z.ToString("0") + ", new turn radius " + nw.Radius.ToString("0") + " over " + (nw.Delta * Mathf.Rad2Deg).ToString("0") + " degrees at lap point " + nw.Mid + ", scale " + scale.ToString("0.00"));
			return true;
		}

		private static void LandTransplants()
		{
			foreach (Planned plan in planned)
			{
				if (plan.Clone == null)
				{
					continue;
				}
float ground = terrain.GetPosition().y + terrain.SampleHeight(new Vector3(plan.NewCenterXZ.x, 0f, plan.NewCenterXZ.z));
				if (plan.Prune)
				{
					// A hall as big as this one spans hills: it goes down to the lowest ground under it, half a metre lower, so it stands in
					// the ground on the high side and never floats.
ground = Mathf.Max(LowestGround(plan.NewCenterXZ, plan.Extent.x * 0.45f, plan.Extent.z * 0.45f) - 0.5f, ground - 2.5f);
				}
				Vector3 at = new Vector3(plan.NewPivotXZ.x, ground + plan.CenterAboveGround + plan.PivotAboveCenter, plan.NewPivotXZ.z);
				Transform t = plan.Clone.transform;
				t.SetPositionAndRotation(at, plan.Rotation);
				t.localScale = plan.Scale;
				// Out of the switched off pen and into the scene: this is when its scripts start.
				t.SetParent(root.transform, true);
				plan.Clone.SetActive(true);
				if (plan.Prune)
				{
					Prune(plan);
				}
			}
			if (planned.Count > 0)
			{
				Physics.SyncTransforms();
			}
		}

/// <summary>
		/// A carried hall stands round a stretch of road (the window of lap points round its anchor). Its loose parts (a pipe, a tower, a
		/// container, a shed) that stand on the road, or across some other stretch of it, are moved away from the road in 10 m steps until
		/// they are clear; only one that will not clear in 80 m is switched off. The hall and the spinner are single pieces and stay.
		/// </summary>
		private static void Prune(Planned plan)
		{
			Transform top = plan.Clone.transform;
int pushed = 0;
			int snapped = 0;
			int removed = 0;
			int blocked = 0;
			List<Transform> children = new List<Transform>();
			foreach (Transform child in top)
			{
				children.Add(child);
			}
			foreach (Transform child in children)
			{
				if (!child.gameObject.activeSelf)
				{
					continue;
				}
				Bounds bounds = new Bounds(child.position, Vector3.zero);
				bool measured = false;
				foreach (Renderer r in child.GetComponentsInChildren<Renderer>())
				{
					if (measured) { bounds.Encapsulate(r.bounds); } else { bounds = r.bounds; measured = true; }
				}
				foreach (Collider col in child.GetComponentsInChildren<Collider>())
				{
					if (measured) { bounds.Encapsulate(col.bounds); } else { bounds = col.bounds; measured = true; }
				}
if (!measured)
				{
					continue;
				}
// The spinner's whole sweep is kept clear: a loose part that stands in it is moved straight out of it, or switched off if it will not clear.
				if (spinnerSet && child.name != "factory" && child.name != "spinner")
				{
					float need = spinnerR + Math.Max(bounds.extents.x, bounds.extents.z);
					int tries = 0;
					while (Flat(bounds.center - spinnerPos).magnitude < need && tries < 40)
					{
						Vector3 away = Flat(bounds.center - spinnerPos);
						away = away.sqrMagnitude > 1f ? away.normalized : Vector3.right;
						child.position += away * 15f;
						bounds.center += away * 15f;
						tries++;
					}
					if (Flat(bounds.center - spinnerPos).magnitude < need)
					{
						child.gameObject.SetActive(false);
						removed++;
						continue;
					}
				}
				// Every part but the main building comes down to the ground under it (the lowest point of its footprint, 0.3 m into it), so
				// nothing hangs in the air where the hills differ from the old flat ground; none is ever lifted.
				if (child.name != "factory")
				{
					float low = LowestGround(bounds.center, bounds.extents.x * 0.8f, bounds.extents.z * 0.8f);
					float lift = bounds.min.y - low;
					if (lift > 0.3f)
					{
						float drop = Mathf.Min(lift + 0.3f, 15f);
						child.position += Vector3.down * drop;
						bounds.center += Vector3.down * drop;
						snapped++;
					}
				}
if (child.name == "spinner" && plan.HasSpinner)
				{
					// Stood where it belongs by the road, its underside a little into the ground there.
					Vector3 shift = new Vector3(plan.SpinnerAt.x - bounds.center.x, 0f, plan.SpinnerAt.z - bounds.center.z);
					child.position += shift;
					bounds.center += shift;
					float low = LowestGround(bounds.center, bounds.extents.x * 0.5f, bounds.extents.z * 0.5f);
					float dy = low - 0.5f - bounds.min.y;
					child.position += Vector3.up * dy;
					bounds.center += Vector3.up * dy;
				}
				if (child.name == "spinner")
				{
					Mod.Info(P.Key + ": the spinner stands at " + child.position.x.ToString("0") + "," + child.position.y.ToString("0") + "," + child.position.z.ToString("0") + ", " + bounds.size.x.ToString("0") + " x " + bounds.size.y.ToString("0") + " x " + bounds.size.z.ToString("0") + " m, ground there " + LowestGround(bounds.center, bounds.extents.x * 0.5f, bounds.extents.z * 0.5f).ToString("0") + ", active " + child.gameObject.activeInHierarchy);
				}
				if (bounds.size.y < 3f)
				{
					continue;
				}
				int hits = RoadHits(bounds, plan, out Vector3 nearest);
				if (hits == 0)
				{
					continue;
				}
				if (child.name == "factory" || child.name == "spinner")
				{
					blocked += hits;
					continue;
				}
				int steps = 0;
				while (hits > 0 && steps < 8)
				{
					Vector3 away = Flat(bounds.center - nearest);
					away = away.sqrMagnitude > 1f ? away.normalized : Vector3.right;
					Vector3 delta = away * 10f;
					child.position += delta;
					bounds.center += delta;
					steps++;
					hits = RoadHits(bounds, plan, out nearest);
				}
				if (hits > 0)
				{
					child.gameObject.SetActive(false);
					removed++;
				}
				else
				{
					pushed++;
				}
			}
			Mod.Info(P.Key + ":parts of the " + top.name + " lowered to the ground: " + snapped + "; loose parts that stood in the road: " + pushed + " moved aside, " + removed + " switched off; " + blocked + " lap points are inside the bounds of its hall or spinner");
		}

		/// <summary>How many lap points are inside the bounds (round the anchor only what is on the tarmac itself, elsewhere with a margin), and the one nearest the bounds' middle.</summary>
		private static int RoadHits(Bounds bounds, Planned plan, out Vector3 nearest)
		{
			int n = lap.Count;
			float pad = RoadHalfWidth + 3f;
			int hits = 0;
			float best = float.MaxValue;
			nearest = bounds.center;
			for (int i = 0; i < n; i++)
			{
				int d = (((i - plan.PruneMid + n / 2) % n) + n) % n - n / 2;
				float margin = d >= -plan.PruneBack && d <= plan.PruneAhead ? RoadHalfWidth + 0.5f : pad;
				Vector3 p = lap.Points[i];
				if (p.x >= bounds.min.x - margin && p.x <= bounds.max.x + margin && p.z >= bounds.min.z - margin && p.z <= bounds.max.z + margin)
				{
					hits++;
					float dist = Flat(p - bounds.center).sqrMagnitude;
					if (dist < best)
					{
						best = dist;
						nearest = p;
					}
				}
			}
			return hits;
		}

/// <summary>True when (x, z) is within <paramref name="reach"/> of some triangle (corner triples) seen from above: tested at the point and four points round it.</summary>
		private static bool NearTris(Vector2[] tris, float x, float z, float reach)
		{
			for (int k = 0; k < 5; k++)
			{
				float px = x + (k == 1 ? reach : (k == 2 ? -reach : 0f));
				float pz = z + (k == 3 ? reach : (k == 4 ? -reach : 0f));
				for (int t = 0; t + 2 < tris.Length; t += 3)
				{
					Vector2 a = tris[t];
					Vector2 b = tris[t + 1];
					Vector2 c = tris[t + 2];
					float d1 = (px - b.x) * (a.y - b.y) - (a.x - b.x) * (pz - b.y);
					float d2 = (px - c.x) * (b.y - c.y) - (b.x - c.x) * (pz - c.y);
					float d3 = (px - a.x) * (c.y - a.y) - (c.x - a.x) * (pz - a.y);
					bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
					bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
					if (!(neg && pos))
					{
						return true;
					}
				}
			}
			return false;
		}

		private static Vector3 Flat(Vector3 v)
		{
			return new Vector3(v.x, 0f, v.z);
		}

		private static Vector3 Tangent(Layout layout, int i)
		{
			int n = layout.Count;
			Vector3 t = layout.Points[(i + 1) % n] - layout.Points[(i - 1 + n) % n];
			t.y = 0f;
			return t.sqrMagnitude > 1e-6f ? t.normalized : Vector3.right;
		}

		private static bool Spaced(List<int> used, int t, int n, int spacing)
		{
			foreach (int u in used)
			{
				int d = Math.Abs(t - u);
				if (Math.Min(d, n - d) < spacing)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True when the lap bends by less than <paramref name="limit"/> radians in all, from half points before point t to half points after.</summary>
		private static bool Straight(Layout layout, int t, int half, float limit)
		{
			int n = layout.Count;
			float sum = 0f;
			for (int i = t - half; i <= t + half; i++)
			{
				sum += Mathf.Abs(Loop.Turn(layout, ((i % n) + n) % n));
			}
			return sum <= limit;
		}

		/// <summary>The terrain's flat extent, less a margin, for checking that a carried object stays on the map.</summary>
		private static Area TerrainArea()
		{
			Vector3 p = terrain.GetPosition();
			Vector3 size = terrain.terrainData.size;
			return new Area { MinX = p.x + 60f, MaxX = p.x + size.x - 60f, MinZ = p.z + 60f, MaxZ = p.z + size.z - 60f };
		}

		/// <summary>
		/// How many points of the new lap would run into the object if it were put at lap point t. Each lap point is mapped back into the
		/// old scene's coordinates (undoing the move and the shrinking) and tested against the old bounds of the object's solid parts, with
		/// the road's half width as margin. For something that stands on the road the points on its own stretch (half + 8 either side of t)
		/// do not count, since the road is meant to run through it. Parts that end up off the map count as five each.
		/// </summary>
		private static int Overlaps(Renderer[] renderers, Layout layout, int t, int half, Vector3 pOld, Vector3 fOld, float scale, bool onRoad, Area area)
		{
			int n = layout.Count;
			Vector3 pNew = layout.Points[t];
			float yaw = Vector3.SignedAngle(fOld, Tangent(layout, t), Vector3.up);
			Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
			Quaternion back = Quaternion.AngleAxis(-yaw, Vector3.up);
			int bad = 0;
			List<Bounds> solid = new List<Bounds>();
			foreach (Renderer r in renderers)
			{
				Bounds b = r.bounds;
				Vector3 at = Flat(pNew) + turn * (Flat(b.center - pOld) * scale);
				if (at.x < area.MinX || at.x > area.MaxX || at.z < area.MinZ || at.z > area.MaxZ)
				{
					bad += 5;
				}
				// Floors and signs are flat; walls, halls and machines have some height.
				if (b.size.y >= 3f)
				{
					solid.Add(b);
				}
			}
			float pad = (RoadHalfWidth + 3f) / scale;
			for (int i = 0; i < n; i++)
			{
				if (onRoad)
				{
					int d = Math.Abs(i - t);
					if (Math.Min(d, n - d) <= half + 8)
					{
						continue;
					}
				}
				Vector3 q = pOld + back * (Flat(layout.Points[i] - pNew) / scale);
				foreach (Bounds b in solid)
				{
					if (q.x >= b.min.x - pad && q.x <= b.max.x + pad && q.z >= b.min.z - pad && q.z <= b.max.z + pad)
					{
						bad++;
						break;
					}
				}
			}
			return bad;
		}

		// ---- the tornado ----------------------------------------------------------------------------------------------

		/// <summary>
		/// Forest Field's tornado patrols a straight line between two path points that was drawn across the old track. It is put on a line
		/// across the new road at a fairly straight spot a good way round the lap, so it crosses the road on purpose: a moving hazard to
		/// time, not a sight that happens to be near. (The game switches the whole tornado off in three races out of four; that stays.)
		/// </summary>
		private static void PlaceTornado(Layout layout, int seed)
		{
			MonoBehaviour tornado = null;
			foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (mb != null && mb.GetType().Name == "Tornado")
				{
					tornado = mb;
					break;
				}
			}
			if (tornado == null)
			{
				Mod.Info(P.Key + ": no tornado in this scene.");
				return;
			}
			if (!tornado.gameObject.activeInHierarchy)
			{
				Mod.Info(P.Key + ": the tornado is switched off this race.");
				return;
			}
			Traverse t = Traverse.Create(tornado);
			Transform[] points = t.Field("pathPoints").GetValue<Transform[]>();
			if (points == null || points.Length < 2 || points[0] == null || points[1] == null)
			{
				Mod.Log.LogWarning(P.Key + ": the tornado has no two path points, it is left where it is.");
				return;
			}

			int n = layout.Count;
			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0x70A4EDUL);
			}
			// A dozen spots in the middle of the lap (nowhere near the grid), the one that bends least.
			int lo = n / 5;
			int hi = n * 4 / 5;
			int best = lo;
			float bestBend = float.MaxValue;
			for (int k = 0; k < 12; k++)
			{
				int i = lo + (int)(rng.Next() * (hi - lo));
				if (i < ahead + 20 || i > n - behind - 20)
				{
					continue;
				}
				float bend = 0f;
				for (int j = i - 10; j <= i + 10; j++)
				{
					bend += Mathf.Abs(Loop.Turn(layout, ((j % n) + n) % n));
				}
				if (bend < bestBend)
				{
					bestBend = bend;
					best = i;
				}
			}

			Vector3 centre = layout.Points[best];
			Vector3 side = layout.Right(best);
			float reach = RoadHalfWidth + 75f;
			float y = centre.y + 4.5f;
			Vector3 a = new Vector3(centre.x - side.x * reach, y, centre.z - side.z * reach);
			Vector3 b = new Vector3(centre.x + side.x * reach, y, centre.z + side.z * reach);
			points[0].position = a;
			points[1].position = b;
			tornado.transform.position = a;
			Traverse index = t.Field("currentPointIndex");
			if (index.FieldExists())
			{
				index.SetValue(1);
			}
			Traverse speed = t.Field("speed");
			if (speed.FieldExists())
			{
				speed.SetValue(4f);
			}
			Mod.Info(P.Key + ": tornado now patrols across the road at lap point " + best + " of " + n + ", " + (2f * reach).ToString("0") + " m each way");
		}

		// ---- Tokyo: the wall and the city blocks ---------------------------------------------------------------------

		private sealed class Block
		{
			public Transform T;
			public Vector3 Size;
			public Vector3 Centre;
		}

		private const int BorderKinds = 5;

		/// <summary>A stretch of the city with its own look: which kinds of wall it uses (0 striped barrier, 1 tall neon wall, 2 fence, 3 brick, 4 plain), its neon colours (indexes into Neon), and how tall the blocks behind it are.</summary>
		private sealed class CityTheme
		{
			public string Name;
			public int[] Kinds;
			public int[] Colours;
			public float MinHeight;
			public float MaxHeight;
		}

		private static readonly CityTheme[] Themes =
		{
			new CityTheme { Name = "Neon strip", Kinds = new[] { 1, 1, 4 }, Colours = new[] { 1, 4, 0 }, MinHeight = 0f, MaxHeight = 1000f },
			new CityTheme { Name = "Temple quarter", Kinds = new[] { 3, 2, 3 }, Colours = new[] { 2, 3 }, MinHeight = 0f, MaxHeight = 30f },
			new CityTheme { Name = "Harbour", Kinds = new[] { 0, 2, 4 }, Colours = new[] { 0, 3 }, MinHeight = 0f, MaxHeight = 24f },
			new CityTheme { Name = "Downtown", Kinds = new[] { 4, 1, 3 }, Colours = new[] { 0, 1, 2 }, MinHeight = 22f, MaxHeight = 1000f }
		};


		private static readonly Color[] Neon =
		{
			new Color(0.10f, 0.90f, 1.00f), new Color(1.00f, 0.15f, 0.70f), new Color(1.00f, 0.55f, 0.10f), new Color(0.55f, 1.00f, 0.25f), new Color(0.65f, 0.35f, 1.00f)
		};

private sealed class Roof
		{
			public GameObject Clone;
			public int Index;
			public float Depth;
		}

		/// <summary>A sign board: the sprite's picture, lit, cut out where it is clear, seen from both sides.</summary>
		private static Material MakeSign(Material source, Sprite sprite)
		{
			Material m = CircuitScene.MakeMaterial(source, Color.white);
			Texture tex = sprite.texture;
			m.mainTexture = tex;
			if (m.HasProperty("_BaseMap"))
			{
				m.SetTexture("_BaseMap", tex);
			}
			if (m.HasProperty("_EmissionMap"))
			{
				m.EnableKeyword("_EMISSION");
				m.SetTexture("_EmissionMap", tex);
				m.SetColor("_EmissionColor", Color.white * 1.3f);
			}
			if (m.HasProperty("_Cull"))
			{
				m.SetFloat("_Cull", 0f);
			}
			if (m.HasProperty("_AlphaClip"))
			{
				m.SetFloat("_AlphaClip", 1f);
				m.SetFloat("_Cutoff", 0.4f);
				m.EnableKeyword("_ALPHATEST_ON");
				m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
			}
			return m;
		}

		/// <summary>
		/// The Tokyo map has a machine gun that rides on its train (TrainTurret). The train is left out of the new lap, so copies of the gun
		/// stand on the roofs of some of the buildings along the road instead, turned toward it. Each is given a stand to be its "train" (the
		/// thing the gun measures its turning from and ignores when it looks for a line of sight).
		/// </summary>
		private static int PlaceShooters(List<Roof> roofs, Layout layout, Rng rng, Transform parent, int ahead, int behind, int wanted)
		{
			TrainTurret[] found = UnityEngine.Object.FindObjectsByType<TrainTurret>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			if (found.Length == 0)
			{
				Mod.Log.LogWarning("Tokyo: the scene has no TrainTurret to put on the roofs.");
				return 0;
			}
			TrainTurret proto = found[0];
			Transform train = Traverse.Create(proto).Field("trainRoot").GetValue<Transform>();
// The gun's place in the train's frame (turn and size, the offset along the train is dropped): the stand takes the train's size, so the whole chain of scales and turns collapses to this one.
			Matrix4x4 inTrain = train != null ? train.worldToLocalMatrix * proto.transform.localToWorldMatrix : proto.transform.localToWorldMatrix;
			Quaternion rel = inTrain.rotation;
			Vector3 scale = inTrain.lossyScale;
			Vector3 standScale = (train != null ? train.lossyScale : Vector3.one) * 1.5f;
			int n = layout.Count;
			for (int i = roofs.Count - 1; i > 0; i--)
			{
				int j = (int)(rng.Next() * (i + 1));
				Roof swap = roofs[i];
				roofs[i] = roofs[j];
				roofs[j] = swap;
			}
			List<int> used = new List<int>();
			int made = 0;
			foreach (Roof roof in roofs)
			{
				if (made >= wanted)
				{
					break;
				}
				if (roof.Index < ahead + 50 || roof.Index > n - behind - 20 || !Spaced(used, roof.Index, n, 90))
				{
					continue;
				}
				Renderer[] rs = roof.Clone.GetComponentsInChildren<Renderer>();
				if (rs.Length == 0)
				{
					continue;
				}
				Bounds bb = rs[0].bounds;
				foreach (Renderer r in rs)
				{
					bb.Encapsulate(r.bounds);
				}
				Vector3 toRoad = Flat(layout.Points[roof.Index] - bb.center);
				toRoad = toRoad.sqrMagnitude > 1f ? toRoad.normalized : Vector3.forward;
				Vector3 pos = new Vector3(bb.center.x, bb.max.y + 0.3f, bb.center.z) + toRoad * Mathf.Max(0f, roof.Depth * 0.5f - 3f);
				GameObject stand = new GameObject("Shooter");
				stand.SetActive(false);
				stand.transform.SetParent(parent, false);
stand.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toRoad, Vector3.up));
				stand.transform.localScale = standScale;
				GameObject gun = UnityEngine.Object.Instantiate(proto.gameObject, stand.transform, false);
				gun.name = "MachineGun";
				gun.transform.localPosition = Vector3.zero;
gun.transform.localRotation = rel;
				gun.transform.localScale = scale;
				TrainTurret turret = gun.GetComponent<TrainTurret>();
				Traverse.Create(turret).Field("trainRoot").SetValue(stand.transform);
				gun.SetActive(true);
				turret.enabled = true;
				stand.SetActive(true);
used.Add(roof.Index);
				made++;
				Renderer[] gr = gun.GetComponentsInChildren<Renderer>(true);
				if (gr.Length > 0)
				{
					Bounds gb = gr[0].bounds;
					foreach (Renderer r in gr)
					{
						gb.Encapsulate(r.bounds);
					}
					Mod.Info("Tokyo: machine gun " + made + " at lap point " + roof.Index + ", " + gr.Length + " renderers, " + gb.size.x.ToString("0.0") + " x " + gb.size.y.ToString("0.0") + " x " + gb.size.z.ToString("0.0") + " m, on a roof " + (pos.y).ToString("0") + " m up");
				}
			}
			Mod.Info("Tokyo: " + made + " rooftop machine guns (from the train's TrainTurret, scale x1.5, among " + roofs.Count + " roofs of 14 m or more)");
			return made;
		}

		private static Material MakeGlow(Material source, Color color)
		{
			Material m = CircuitScene.MakeMaterial(source, color);
			if (m.HasProperty("_EmissionColor"))
			{
				m.EnableKeyword("_EMISSION");
				m.SetColor("_EmissionColor", color * 2.2f);
			}
			return m;
		}

		/// <summary>
		/// The border of the road, in runs of 90 to 230 m, each side on its own, each run one of five kinds: a low striped concrete barrier,
		/// a tall dark wall with a neon strip, a fence on posts, a mid height brick wall with a pale cap, or a plain concrete wall with the
		/// city blocks standing right against it. Every kind is solid (the fence has an invisible wall in it), all in the same band beside
		/// the road, so the road stays closed in the whole way round. Behind the border stand city blocks: copies of the scene's own
		/// buildings, turned so their long side runs along the road, one after another. A block is left out where another stretch of road
		/// is too near to fit it (inside a tight bend).
		/// </summary>
		private static void BuildCity(GameObject env, Layout layout, int seed, Material source)
		{
			float h = RoadHalfWidth;
			int n = layout.Count;
			float a = h + 1.5f;
			float b = h + 4.0f;
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0xC17B1DUL);
			}

			int[] sign = { 1, -1 };
			int[] theme = new int[n];
			List<int> boundaries = new List<int>();
			{
				int from = 0;
				int last = -1;
				while (from < n)
				{
					int length = 50 + (int)(rng.Next() * 41.0);
					int t = (int)(rng.Next() * Themes.Length);
					if (t == last)
					{
						t = (t + 1) % Themes.Length;
					}
					for (int i = from; i < Math.Min(n, from + length); i++)
					{
						theme[i] = t;
					}
					if (from > 0)
					{
						boundaries.Add(from);
					}
					last = t;
					from += length;
				}
			}
			int[][] kind = new int[2][];
			int[][] glow = new int[2][];
			for (int s = 0; s < 2; s++)
			{
				kind[s] = new int[n];
				glow[s] = new int[n];
				int previous = -1;
				int at = 0;
				while (at < n)
				{
					CityTheme th = Themes[theme[at]];
					int end = at + 1;
					while (end < n && theme[end] == theme[at])
					{
						end++;
					}
					int length = 14 + (int)(rng.Next() * 17.0);
					int k = th.Kinds[(int)(rng.Next() * th.Kinds.Length)];
					int colour = th.Colours[(int)(rng.Next() * th.Colours.Length)];
					if (k == previous && th.Kinds.Length > 1)
					{
						k = th.Kinds[(Array.IndexOf(th.Kinds, k) + 1) % th.Kinds.Length];
					}
					int stop = Math.Min(end, at + length);
					for (int i = at; i < stop; i++)
					{
						kind[s][i] = k;
						glow[s][i] = colour;
					}
					previous = k;
					at = stop;
				}
			}

			Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
			Batch Get(string name, Color color, bool collide = false, bool wall = false)
			{
				if (!batches.TryGetValue(name, out Batch made))
				{
					made = new Batch(name, color, collide, wall);
					batches[name] = made;
				}
				return made;
			}

			Color concrete = new Color(0.56f, 0.56f, 0.58f);
			Color dark = new Color(0.20f, 0.21f, 0.25f);
			Color brick = new Color(0.52f, 0.30f, 0.24f);
			Color pale = new Color(0.82f, 0.82f, 0.80f);
			Color plain = new Color(0.34f, 0.34f, 0.36f);
			Color metal = new Color(0.13f, 0.13f, 0.15f);

			for (int s = 0; s < 2; s++)
			{
				float sg = sign[s];
				int i0 = 0;
				while (i0 < n)
				{
					int i1 = i0 + 1;
					while (i1 < n && kind[s][i1] == kind[s][i0])
					{
						i1++;
					}
					int count = i1 - i0;
					int run = kind[s][i0];
					Color neon = Neon[glow[s][i0]];
					switch (run)
					{
						case 0:
							Loop.SweepRange(Get("concrete", concrete, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * b, -0.6f),new Vector2(sg * (b - 0.5f), 3.6f), new Vector2(sg * (a + 0.5f), 3.6f) }, i0, count);
							for (int i = i0; i < i1; i++)
							{
								bool red = (i / 3) % 2 == 0;
								Loop.Cell(red ? Get("kerb red", new Color(0.80f, 0.12f, 0.10f)) : Get("kerb white", new Color(0.90f, 0.90f, 0.88f)),layout, i, sg * (a + 0.5f), sg * (b - 0.5f), 3.64f);
							}
							break;
						case 1:
							Loop.SweepRange(Get("tall wall", dark, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * b, -0.6f),new Vector2(sg * b, 9.5f), new Vector2(sg * a, 9.5f) }, i0, count);
							for (int i = i0; i < i1; i++)
							{
								Vector3 toRoad = layout.Right(i % n) * -sg;
								float lat = sg * (a - 0.03f);
Get("glow" + glow[s][i0], neon).Quad(layout.At(i, lat, 7.1f), layout.At(i, lat, 8.0f), layout.At(i + 1, lat, 8.0f), layout.At(i + 1, lat, 7.1f), toRoad);
							}
							break;
						case 2:
							Loop.SweepRange(Get("hidden fence", metal, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * (a + 0.4f), -0.6f),new Vector2(sg * (a + 0.4f), 8.0f), new Vector2(sg * a, 8.0f) }, i0, count);
foreach (float rail in new[] { 1.5f, 3.0f, 4.5f, 6.0f, 7.5f })
							{
								Loop.SweepRange(Get("fence rail", metal), layout, new[] { new Vector2(sg * a, rail - 0.07f), new Vector2(sg * (a + 0.3f), rail - 0.07f), new Vector2(sg * (a + 0.3f), rail + 0.07f), new Vector2(sg * a, rail + 0.07f) }, i0, count);
							}
							for (int i = i0; i < i1; i += 3)
							{
Get("fence rail", metal).Box(layout.At(i, sg * (a + 0.15f), 4.0f), new Vector3(0.3f, 8.0f, 0.3f));
							}
							break;
						case 3:
							Loop.SweepRange(Get("brick wall", brick, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * b, -0.6f),new Vector2(sg * b, 6.4f), new Vector2(sg * a, 6.4f) }, i0, count);
							for (int i = i0; i < i1; i++)
							{
Loop.Cell(Get("wall cap", pale), layout, i, sg * a, sg * b, 6.44f);
							}
							break;
						default:
							Loop.SweepRange(Get("plain wall", plain, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * b, -0.6f),new Vector2(sg * b, 8.0f), new Vector2(sg * a, 8.0f) }, i0, count);
							break;
					}
					i0 = i1;
				}
			}

// An invisible wall, 16 m high, over the whole border on both sides: whatever the kind of wall, nothing gets over it.
			foreach (float sg in sign)
			{
				Loop.SweepRange(Get("hidden guard", metal, true, true), layout, new[] { new Vector2(sg * (a + 0.1f), -1f), new Vector2(sg * (b - 0.1f), -1f), new Vector2(sg * (b - 0.1f), 16f), new Vector2(sg * (a + 0.1f), 16f) }, 0, n);
			}

			// Signs for the gates: the scene's own shop signs, if it has any.
			List<Sprite> signSprites = new List<Sprite>();
			{
				HashSet<string> seen = new HashSet<string>();
				foreach (SpriteRenderer sr in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
				{
					Sprite sp = sr.sprite;
					if (sp == null || sp.texture == null || !sp.name.Contains("Sign") || !seen.Add(sp.name) || signSprites.Count >= 10)
					{
						continue;
					}
					float aspect = sp.textureRect.width / Math.Max(1f, sp.textureRect.height);
					if (aspect > 0.4f && aspect < 5f)
					{
						signSprites.Add(sp);
					}
				}
			}

			// A torii-style gate across the road at every change of theme (not in the grid, not in the first or last stretch, only where the road is straight):
			// two pillars outside the wall band, a long top beam and a lower one, lit strips, and a shop sign hanging from the lower beam. Nothing of it can be hit.
			bool[] gateZone = new bool[n];
			int gateAhead = Mathf.RoundToInt(LevelAhead / layout.Step);
			int gateBehind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int gates = 0;
			Color lacquer = new Color(0.62f, 0.09f, 0.08f);
			Color blackwood = new Color(0.10f, 0.10f, 0.12f);
			foreach (int boundary in boundaries)
			{
				int at = -1;
				foreach (int off in new[] { 0, 3, -3, 6, -6, 10, -10, 15, -15, 20, -20 })
				{
					int t = boundary + off;
					if (t > gateAhead + 10 && t < n - gateBehind - 10 && Straight(layout, t, 5, 0.16f))
					{
						at = t;
						break;
					}
				}
				if (at < 0)
				{
					continue;
				}
				CityTheme th = Themes[theme[boundary]];
				Color trim = Neon[th.Colours[0]];
				Color wood = th.Name == "Temple quarter" ? lacquer : blackwood;
				Vector3 p = layout.Points[at];
				Vector3 f = Tangent(layout, at);
				Vector3 r = layout.Right(at);
				float px = b + 2.4f;
				Batch post = Get("gate post", wood);
				foreach (float sg in sign)
				{
					post.OBox(p + r * (sg * px) + Vector3.up * 6.5f, r * 0.9f, Vector3.up * 7.5f, f * 0.9f);
					post.OBox(p + r * (sg * px) + Vector3.up * 14.3f, r * 1.3f, Vector3.up * 0.4f, f * 1.3f);
				}
				post.OBox(p + Vector3.up * 14.7f, r * (px + 4f), Vector3.up * 0.55f, f * 1.1f);
				post.OBox(p + Vector3.up * 11.8f, r * px, Vector3.up * 0.4f, f * 0.5f);
				Batch lit = Get("glow" + th.Colours[0], trim);
				foreach (float side in new[] { -1f, 1f })
				{
					Vector3 face = p + f * (side * 1.15f) + Vector3.up * 14.7f;
					lit.OBox(face, r * (px + 3.6f), Vector3.up * 0.14f, f * 0.05f);
					Vector3 low = p + f * (side * 0.55f) + Vector3.up * 11.45f;
					lit.OBox(low, r * (px - 0.4f), Vector3.up * 0.1f, f * 0.05f);
				}
				if (signSprites.Count > 0)
				{
					int which = (at + (int)(rng.Next() * 7.0)) % signSprites.Count;
					Sprite sp = signSprites[which];
					Rect tr = sp.textureRect;
					float aspect = tr.width / Math.Max(1f, tr.height);
					float height = 3.0f;
					float width = Math.Min(10f, height * aspect);
					height = width / aspect;
					float top = 11.3f;
					Texture tex = sp.texture;
					Vector2 u0 = new Vector2(tr.xMin / tex.width, tr.yMin / tex.height);
					Vector2 u1 = new Vector2(tr.xMax / tex.width, tr.yMax / tex.height);
					Batch board = Get("sign:" + which, Color.white);
					foreach (float side in new[] { -1f, 1f })
					{
						Vector3 c = p + f * (side * 0.12f);
						Vector3 left = c - r * (width * 0.5f);
						Vector3 rightEdge = c + r * (width * 0.5f);
						// The picture's left edge goes on the viewer's left: seen from ahead of the gate that is the road's right-hand edge (the -r side is the viewer's right).
						Vector3 a0 = side > 0f ? rightEdge : left;
						Vector3 a1 = side > 0f ? left : rightEdge;
						board.QuadUV(a0 + Vector3.up * (top - height), a1 + Vector3.up * (top - height), a1 + Vector3.up * top, a0 + Vector3.up * top,
							new Vector2(u0.x, u0.y), new Vector2(u1.x, u0.y), new Vector2(u1.x, u1.y), new Vector2(u0.x, u1.y), f * side);
					}
					signSprites[which] = sp;
				}
				for (int q = -9; q <= 9; q++)
				{
					gateZone[((at + q) % n + n) % n] = true;
				}
				gates++;
			}

			int meshes = 0;
			foreach (Batch batch in batches.Values)
			{
				if (batch.Count == 0)
				{
					continue;
				}
				GameObject go = new GameObject(batch.Name);
				go.transform.SetParent(root.transform, false);
				Mesh mesh = batch.ToMesh();
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				if (!batch.Name.StartsWith("hidden"))
				{
go.AddComponent<MeshRenderer>().sharedMaterial = batch.Name.StartsWith("glow") ? MakeGlow(source, batch.Color) : (batch.Name.StartsWith("sign:") ? MakeSign(source, signSprites[int.Parse(batch.Name.Substring(5))]) : CircuitScene.MakeMaterial(source, batch.Color));
				}
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
						Mod.Log.LogWarning("The tag Wall does not exist, the Tokyo walls keep the default tag.");
					}
				}
				meshes++;
			}

			List<Block> blocks = new List<Block>();
			foreach (Transform group in FindAll(env.transform, P.BuildingGroup))
			{
				foreach (Transform building in group)
				{
					bool city = building.name.StartsWith("TallBuilding") || (building.name.StartsWith("Building") && !building.name.StartsWith("BuildingTrain"));
					if (!city || !building.gameObject.activeSelf)
					{
						continue;
					}
					Renderer[] renderers = building.GetComponentsInChildren<Renderer>();
					if (renderers.Length == 0)
					{
						continue;
					}
					Bounds bounds = renderers[0].bounds;
					foreach (Renderer r in renderers)
					{
						bounds.Encapsulate(r.bounds);
					}
					// Buildings far bigger than a city block (the backdrop towers) would swallow the road.
					if (Math.Max(bounds.size.x, bounds.size.z) <= 160f && Math.Min(bounds.size.x, bounds.size.z) >= 8f && bounds.size.y >= 10f)
					{
						blocks.Add(new Block { T = building, Size = bounds.size, Centre = bounds.center });
					}
				}
			}
			if (blocks.Count == 0)
			{
				Mod.Log.LogWarning("Tokyo: no building of a suitable size found at " + P.BuildingGroup + ", the walls have no blocks behind them.");
				return;
			}

			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			Transform cityRoot = new GameObject("City").transform;
			cityRoot.SetParent(root.transform, false);
			int placed = 0;
			List<Roof> roofs = new List<Roof>();
			// How far behind the wall the first row of blocks reaches at each point of each side, so a second row can stand behind it.
			float[][] reach = { new float[n], new float[n] };
			for (int row = 0; row < 2; row++)
			{
				for (int s = 0; s < 2; s++)
				{
					float side = sign[s];
					float pos = 0f;
					while (pos < layout.Length && placed < 700)
					{
						CityTheme here = Themes[theme[Mathf.RoundToInt(pos / layout.Step) % n]];
						Block block = blocks[(int)(rng.Next() * blocks.Count)];
						for (int tries = 0; tries < 8 && (block.Size.y < here.MinHeight || block.Size.y > here.MaxHeight); tries++)
						{
							block = blocks[(int)(rng.Next() * blocks.Count)];
						}
						bool xLong = block.Size.x >= block.Size.z;
						float along = xLong ? block.Size.x : block.Size.z;
						float depth = xLong ? block.Size.z : block.Size.x;
						double flip = rng.Next();
						int index = Mathf.RoundToInt((pos + along * 0.5f) / layout.Step) % n;
						pos += along + 2f;
if (index <= ahead + 4 || index >= n - behind || gateZone[index])
						{
							continue;
						}
						int span = Math.Max(1, Mathf.RoundToInt(along * 0.5f / layout.Step));
						// Against the plain wall they stand flush, behind the others there is a gap; the second row stands behind the first.
						float gap = kind[s][index] == BorderKinds - 1 ? 0.6f : 4f;
						if (row == 1)
						{
							float behindFirst = 0f;
							for (int q = -span; q <= span; q++)
							{
								behindFirst = Math.Max(behindFirst, reach[s][((index + q) % n + n) % n]);
							}
							gap = Math.Max(gap, behindFirst + 4f);
						}
						float lateral = side * (b + gap + depth * 0.5f);
						Vector3 centre = layout.At(index, lateral, 0f);
						float need = Math.Abs(lateral) - 3f;
						bool clear = true;
						for (int i = 0; i < n; i += 2)
						{
							float dx = layout.Points[i].x - centre.x;
							float dz = layout.Points[i].z - centre.z;
							if (dx * dx + dz * dz < need * need)
							{
								clear = false;
								break;
							}
						}
						if (!clear)
						{
							continue;
						}
						if (row == 0)
						{
							for (int q = -span; q <= span; q++)
							{
								int at = ((index + q) % n + n) % n;
								reach[s][at] = Math.Max(reach[s][at], gap + depth);
							}
						}
						float yaw = Vector3.SignedAngle(xLong ? Vector3.right : Vector3.forward, Tangent(layout, index), Vector3.up) + (flip < 0.5 ? 0f : 180f);
						Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
						Transform proto = block.T;
						float bottom = block.Centre.y - block.Size.y * 0.5f;
						Vector3 pivotOffset = proto.position - block.Centre;
						Vector3 place = new Vector3(centre.x, layout.Points[index].y - 0.3f + (proto.position.y - bottom), centre.z) + turn * new Vector3(pivotOffset.x, 0f, pivotOffset.z);
						GameObject clone = UnityEngine.Object.Instantiate(proto.gameObject, place, turn * proto.rotation, cityRoot);
						clone.name = proto.name;
						clone.transform.localScale = Vector3.Scale(proto.localScale, proto.parent != null ? proto.parent.lossyScale : Vector3.one);
						clone.SetActive(true);
						placed++;
						if (row == 0 && block.Size.y >= 14f)
						{
							roofs.Add(new Roof { Clone = clone, Index = index, Depth = depth });
						}
					}
				}
			}
			int shooters = PlaceShooters(roofs, layout, rng, cityRoot, ahead, behind, 6);
			Mod.Info("Tokyo: border in " + meshes + " meshes (four themed districts of 250 to 450 m, " + gates +" gates between them (" + signSprites.Count + " shop signs found for them), " + shooters + " rooftop shooters, five kinds of wall, 3.6 to 9.5 m high under a 16 m invisible guard wall), " + placed + " city blocks in two rows behind it (from " + blocks.Count + " of the scene's buildings)");
		}

		// ---- trees beside the road -----------------------------------------------------------------------------------

		/// <summary>
		/// Stands of trees along the road, each copied from one of the scene's own trees (so each is a prototype the terrain already has):
		/// the road is divided into stretches of about 70 m, and each side of each stretch gets a stand with the probability the profile
		/// gives, ten to twenty trees between the cleared strip and 25 m beyond it.
		/// </summary>
		private static void AddRoadsideTrees(List<TreeInstance> kept, TreeInstance[] original, Ground cut, Layout layout, int seed)
		{
			if (original.Length == 0)
			{
				return;
			}
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0x7EEF05UL);
			}
			int n = layout.Count;
			float h = RoadHalfWidth;
			float clear = h + TreeClear;
			float clear2 = clear * clear;
			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			int chunk = Math.Max(1, Mathf.RoundToInt(70f / layout.Step));
			int added = 0;
			for (int side = -1; side <= 1; side += 2)
			{
				for (int start = 0; start < n; start += chunk)
				{
					bool stand = rng.Next() < P.RoadsideTrees;
					int count = 10 + (int)(rng.Next() * 11.0);
					if (!stand)
					{
						continue;
					}
					for (int k = 0; k < count; k++)
					{
						int index = (start + (int)(rng.Next() * chunk)) % n;
						float lateral = side * (clear + 1f + (float)rng.Next() * 24f);
						int template = (int)(rng.Next() * original.Length);
						float scale = (float)rng.Range(0.85, 1.2);
						float rotation = (float)rng.Range(0.0, 6.2831853);
						if (index <= ahead || index >= n - behind)
						{
							continue;
						}
						Vector3 p = layout.At(index, lateral, 0f);
						bool onRoad = false;
						for (int i = 0; i < n; i += 2)
						{
							float dx = layout.Points[i].x - p.x;
							float dz = layout.Points[i].z - p.z;
							if (dx * dx + dz * dz < clear2)
							{
								onRoad = true;
								break;
							}
						}
						if (onRoad)
						{
							continue;
						}
						TreeInstance tree = original[template];
						float y = cut.At(p.x, p.z);
						tree.position = new Vector3((p.x - cut.X0) / cut.SizeX, Mathf.Clamp01((y - cut.Y0) / cut.SizeY), (p.z - cut.Z0) / cut.SizeZ);
						tree.widthScale *= scale;
						tree.heightScale *= scale;
						tree.rotation = rotation;
						kept.Add(tree);
						added++;
					}
				}
			}
			Mod.Info(P.Key + ": " + added + " trees added beside the road");
		}
	}
}
