using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// What stands round a hairpin or a factory lap: the wall to crash into beyond a hairpin's outside (Factory), the thick stand of trees
	/// to fly into (Forest, Mountain), and the Factory yard: runs of barrier, sheet wall and fence, and the scene's own sheds, containers,
	/// towers and pipes along the road.
	/// </summary>
	internal static partial class TerrainScene
	{
		private static Batch GetBatch(Dictionary<string, Batch> batches, string name, Color color, bool collide = false, bool wall = false)
		{
			if (!batches.TryGetValue(name, out Batch made))
			{
				made = new Batch(name, color, collide, wall);
				batches[name] = made;
			}
			return made;
		}

		/// <summary>Makes one object per batch: renderer, and a mesh collider (tagged Wall) where the batch collides. A batch called hidden... has no renderer.</summary>
		private static int Emit(Dictionary<string, Batch> batches, Transform parent, Material source)
		{
			int meshes = 0;
			foreach (Batch batch in batches.Values)
			{
				if (batch.Count == 0)
				{
					continue;
				}
				GameObject go = new GameObject(batch.Name);
				go.transform.SetParent(parent, false);
				Mesh mesh = batch.ToMesh();
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				if (!batch.Name.StartsWith("hidden"))
				{
					go.AddComponent<MeshRenderer>().sharedMaterial = batch.Name.StartsWith("glow") ? MakeGlow(source, batch.Color) : CircuitScene.MakeMaterial(source, batch.Color);
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
						Mod.Log.LogWarning("The tag Wall does not exist, " + batch.Name + " keeps the default tag.");
					}
				}
				meshes++;
			}
			return meshes;
		}

		/// <summary>+1 when the hairpin's outside is on the right of the direction of travel (it turns left), -1 when on the left.</summary>
		private static float OutsideSide(Layout layout)
		{
			float sum = 0f;
			int n = layout.Count;
			for (int q = -layout.HairpinSpan / 2; q <= layout.HairpinSpan / 2; q++)
			{
				sum += Loop.Turn(layout, ((layout.Hairpin + q) % n + n) % n);
			}
			return sum >= 0f ? 1f : -1f;
		}

		// ---- the trees beyond the hairpin --------------------------------------------------------------------------------

		/// <summary>About 70 of the scene's own trees on the hairpin's outside, from just beyond the cleared strip out to 70 m, so missing the turn ends in the woods.</summary>
		private static void AddHairpinTrees(List<TreeInstance> kept, TreeInstance[] original, Ground cut, Layout layout, int seed)
		{
			if (original.Length == 0 || layout.Hairpin < 0)
			{
				return;
			}
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0x4A19F1UL);
			}
			int n = layout.Count;
			float h = RoadHalfWidth;
			float clear = h + TreeClear;
			float clear2 = clear * clear;
			float side = OutsideSide(layout);
			int span = layout.HairpinSpan / 2 + 10;
			int added = 0;
			for (int k = 0; k < 420 && added < 70; k++)
			{
				int index = ((layout.Hairpin + (int)(rng.Range(-span, span))) % n + n) % n;
				float lateral = side * (clear + 1f + (float)rng.Next() * 70f);
				int template = (int)(rng.Next() * original.Length);
				float scale = (float)rng.Range(0.85, 1.25);
				float rotation = (float)rng.Range(0.0, 6.2831853);
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
			Mod.Info(P.Key + ": " + added + " trees added beyond the hairpin's outside");
		}

		// ---- the Factory yard ---------------------------------------------------------------------------------------------

		private sealed class Proto
		{
			public Transform T;
			public Vector3 Size;
			public Vector3 Centre;
		}

		private static readonly List<Proto> yardProtos = new List<Proto>();

		/// <summary>Remembers the hall's loose pieces (sheds, containers, towers, pipes) and their sizes before the original is hidden.</summary>
		private static void CollectProtos(Transform hall)
		{
			yardProtos.Clear();
			foreach (Transform child in hall)
			{
				string n = child.name;
				if (!child.gameObject.activeSelf || n == "factory" || n == "spinner" || n == "road2" || n.StartsWith("Plane") || n.StartsWith("TreeTrunk"))
				{
					continue;
				}
				Renderer[] renderers = child.GetComponentsInChildren<Renderer>();
				if (renderers.Length == 0)
				{
					continue;
				}
				Bounds b = renderers[0].bounds;
				foreach (Renderer r in renderers)
				{
					b.Encapsulate(r.bounds);
				}
				if (b.size.y < 2.5f ||Math.Max(b.size.x, b.size.z) > 260f || b.size.y > 130f || Math.Min(b.size.x, b.size.z) < 1.5f)
				{
					continue;
				}
				yardProtos.Add(new Proto { T = child, Size = b.size, Centre = b.center });
			}
		}

		/// <summary>The lowest ground under a footprint: the middle and eight points round it, so a piece on a slope is sunk into the high side, never left floating.</summary>
		private static float LowestGround(Vector3 centre, float halfX, float halfZ)
		{
			float low = terrain.GetPosition().y + terrain.SampleHeight(new Vector3(centre.x, 0f, centre.z));
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI * 0.25f;
				float y = terrain.GetPosition().y + terrain.SampleHeight(new Vector3(centre.x + Mathf.Cos(a) * halfX, 0f, centre.z + Mathf.Sin(a) * halfZ));
				low = Math.Min(low, y);
			}
			return low;
		}

		/// <summary>A tall solid wall along the middle between the two straights of the big U-turn, from the building's mouth outward until the roads part too far to matter,
		/// so the turn cannot be cut across in front of the hall. Nothing is added inside the building.</summary>
		private static int BuildMedian(Dictionary<string, Batch> batches, Layout layout)
		{
			IList<Vector3> pts = layout.Points;
			float far = uRadius * 3.5f + 200f;
			Vector3 tAt(float s)
			{
				return Flat(AtArc(pts, uMid, s + 4f) - AtArc(pts, uMid, s - 4f)).normalized;
			}
			float full = Vector3.Angle(tAt(-far), tAt(far));
			float s0 = uRadius;
			for (float s = 20f; s < far; s += 5f)
			{
				if (Vector3.Angle(tAt(-s), tAt(s)) >= full * 0.97f)
				{
					s0 = s;
					break;
				}
			}
			float h = RoadHalfWidth;
			Batch body = GetBatch(batches, "median wall", new Color(0.33f, 0.2f, 0.16f), true, true);
			Batch cap = GetBatch(batches, "median cap", new Color(0.72f, 0.7f, 0.64f));
			Vector3 prev = Vector3.zero;
			bool have = false;
			int segs = 0;
// The wall begins where the building ends: the first stretch of the middle that is clear of the hall (its footprint plus 14 m, and 14 m either side).
			bool InHall(Vector3 p)
			{
				foreach (Planned pl in planned)
				{
					if (pl.Prune && pl.HasHall)
					{
						Vector3 lc = Quaternion.Inverse(pl.HallTurn) * new Vector3(p.x - pl.HallCentre.x, 0f, p.z - pl.HallCentre.z);
						if (Math.Abs(lc.x) < pl.HallHalf.x + 14f && Math.Abs(lc.z) < pl.HallHalf.z + 14f)
						{
							if (pl.HallTris == null || NearTris(pl.HallTris, p.x, p.z, 14f))
							{
								return true;
							}
						}
					}
				}
				return false;
			}
			float sStart = s0 - 25f;
			for (; sStart < s0 + 275f; sStart += 10f)
			{
				Vector3 sa = AtArc(pts, uMid, -sStart);
				Vector3 sb = AtArc(pts, uMid, sStart);
				Vector3 sm = Flat((sa + sb) * 0.5f);
				Vector3 sd = Flat(sb - sa).normalized;
				Vector3 sn = new Vector3(-sd.z, 0f, sd.x);
				if (!InHall(sm) && !InHall(sm + sn * 14f) && !InHall(sm - sn * 14f))
				{
					break;
				}
			}
// Anchored to the building itself: follow the road out of the turn along both legs; the building ends where the road leaves its footprint.
bool OnFootprint(Planned pl, Vector3 p)
			{
				if (pl.HallTris != null)
				{
					return NearTris(pl.HallTris, p.x, p.z, 0f);
				}
				// The mesh could not be read: the building's own oriented bounding box (a rectangle, like the building).
				Vector3 lc = Quaternion.Inverse(pl.HallTurn) * new Vector3(p.x - pl.HallCentre.x, 0f, p.z - pl.HallCentre.z);
				return Math.Abs(lc.x) < pl.HallHalf.x && Math.Abs(lc.z) < pl.HallHalf.z;
			}
			float lastIn = -1f;
			foreach (Planned pl in planned)
			{
if (pl.Prune && pl.HasHall)
				{
					for (float s = 0f; s < 1400f; s += 4f)
					{
						Vector3 la = AtArc(pts, uMid, -s);
						Vector3 lb = AtArc(pts, uMid, s);
if (OnFootprint(pl, la) || OnFootprint(pl, lb))
						{
							lastIn = Math.Max(lastIn, s);
						}
						else if (lastIn >= 0f && s - lastIn > 60f)
						{
							break;
						}
					}
				}
			}
// The building's bounding rectangle is far bigger than what can be seen, so the visible front is taken as 405 m (the old building's, measured in the game) times the hall's scale from the turn's middle.
sStart = 450f;
			// Better: ask the physics world where the building's front stands. Rays run along the middle of the corridor between the two roads, toward the hall,
			// from 750 m out; the nearest solid thing that is not the ground is the front of the building.
			{
				Physics.SyncTransforms();
				Vector3 pNear = Flat((AtArc(pts, uMid, -450f) + AtArc(pts, uMid, 450f)) * 0.5f);
				Vector3 pFar = Flat((AtArc(pts, uMid, -750f) + AtArc(pts, uMid, 750f)) * 0.5f);
				Vector3 dirIn = Flat(pNear - pFar);
				float gapHalf = Flat(AtArc(pts, uMid, -750f) - AtArc(pts, uMid, 750f)).magnitude * 0.5f - (h + 8f);
				if (dirIn.sqrMagnitude > 1f && gapHalf > 20f)
				{
					dirIn = dirIn.normalized;
					Vector3 sideR = new Vector3(-dirIn.z, 0f, dirIn.x);
					float nearest = float.MaxValue;
					for (int k = -1; k <= 1; k++)
					{
						for (int hy = 0; hy < 3; hy++)
						{
							Vector3 o = pFar + sideR * (k * gapHalf * 0.35f);
							o.y = terrain.GetPosition().y + terrain.SampleHeight(new Vector3(o.x, 0f, o.z)) + 6f + hy * 14f;
							RaycastHit[] hits = Physics.RaycastAll(o, dirIn, 1000f, ~0, QueryTriggerInteraction.Ignore);
							foreach (RaycastHit hit in hits)
							{
if (hit.collider is TerrainCollider || hit.distance < 1f)
								{
									continue;
								}
								// Only the building itself counts: a huge, tall collider (not the small towers, signs and props that stand about it, nor flat ground planes).
								Vector3 hs = hit.collider.bounds.size;
								if (Math.Max(hs.x, hs.z) < 200f || hs.y < 15f)
								{
									continue;
								}
								nearest = Math.Min(nearest, hit.distance);
							}
						}
					}
					float front = 750f - nearest;
					if (nearest < 1000f && front > 250f && front < 800f)
					{
sStart = front - 30f;
						Mod.Info(P.Key + ": the building's front found by ray at " + front.ToString("0") + " m from the turn's middle");
					}
					else
					{
						Mod.Info(P.Key + ": no building front found by ray (nearest " + nearest.ToString("0") + "), using 450 m");
					}
				}
			}
			float endS = sStart;
			for (; endS < sStart + 400f; endS += 10f)
			{
				Vector3 ea = AtArc(pts, uMid, -endS);
				Vector3 eb = AtArc(pts, uMid, endS);
				Vector3 em = Flat((ea + eb) * 0.5f);
				if (Flat(ea - eb).magnitude < 2f * (h + 32f) || (spinnerSet && Flat(em - spinnerPos).magnitude < spinnerR + 15f))
				{
					break;
				}
			}
			float halfEnd = endS;
			Mod.Info(P.Key + ": bypass wall planned from " + sStart.ToString("0") + " to " + endS.ToString("0") + " m from the turn's middle (the road leaves the building footprint at " + lastIn.ToString("0") + " m; the bend ends " + s0.ToString("0") + " m out; building tests " + (InHall(Flat((AtArc(pts, uMid, -sStart) + AtArc(pts, uMid, sStart)) * 0.5f)) ? "still inside" : "clear") + ")");
float s2 = sStart;
for (; s2 < halfEnd; s2 += 10f)
			{
				Vector3 a = AtArc(pts, uMid, -s2);
				Vector3 b = AtArc(pts, uMid, s2);
				if (Flat(a - b).magnitude < 2f * (h + 32f))
				{
					break;
				}
				Vector3 m = Flat((a + b) * 0.5f);
				if (spinnerSet && Flat(m - spinnerPos).magnitude < spinnerR + 15f)
				{
					break;
				}
				if (have)
				{
					Vector3 d = m - prev;
					float len = d.magnitude;
					if (len > 1f)
					{
						Vector3 dir = d / len;
						Vector3 side = new Vector3(-dir.z, 0f, dir.x);
						Vector3 c = (m + prev) * 0.5f;
						float low = LowestGround(c, 10f, 10f) - 5f;
						float top = low + 5f + 45f;
						Vector3 centre = new Vector3(c.x, (low + top) * 0.5f, c.z);
						body.OBox(centre, dir * (len * 0.5f + 0.5f), Vector3.up * ((top - low) * 0.5f), side * 7f);
						cap.OBox(new Vector3(c.x, top + 0.8f, c.z), dir * (len * 0.5f + 0.6f), Vector3.up * 0.9f, side * 8.2f);
						if (segs % 8 == 4)
						{
							body.OBox(new Vector3(c.x, top + 12f, c.z), dir * 12f, Vector3.up * 12f, side * 11f);
							cap.OBox(new Vector3(c.x, top + 24.8f, c.z), dir * 12.6f, Vector3.up * 0.8f, side * 11.6f);
						}
						segs++;
					}
				}
				prev = m;
				have = true;
			}
return (int)(s2 - sStart);
		}

		/// <summary>A solid wall along the edge of the road on the side away from the spinner, over the stretch where the spinner reaches, so it cannot be dodged by driving off the road.</summary>
		private static int BuildSpinnerWall(Dictionary<string, Batch> batches, Layout layout)
		{
			int n = layout.Count;
			float h = RoadHalfWidth;
			float reach = spinnerR + 90f;
			Batch body = GetBatch(batches, "spinner wall", new Color(0.3f, 0.2f, 0.17f), true, true);
			Batch cap = GetBatch(batches, "spinner wall cap", new Color(0.7f, 0.68f, 0.6f));
			int segments = 0;
			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				Vector3 a = layout.Points[i];
				Vector3 b = layout.Points[j];
				if (Flat(a - spinnerPos).magnitude > reach || Flat(b - spinnerPos).magnitude > reach)
				{
					continue;
				}
				float side = Vector3.Dot(Flat(spinnerPos - a), layout.Right(i)) >= 0f ? -1f : 1f;
				float lateral = side * (h + 5f);
				Vector3 pa = layout.At(i, lateral, 0f);
				Vector3 pb = layout.At(j, lateral, 0f);
				Vector3 d = Flat(pb - pa);
				float len = d.magnitude;
				if (len < 0.5f)
				{
					continue;
				}
				Vector3 dir = d / len;
				Vector3 thick = new Vector3(-dir.z, 0f, dir.x) * 2.5f;
				Vector3 c = (pa + pb) * 0.5f;
				float low = LowestGround(c, 6f, 6f) - 4f;
				float top = LowestGround(c, 6f, 6f) + 12f;
				body.OBox(new Vector3(c.x, (low + top) * 0.5f, c.z), dir * (len * 0.5f + 0.4f), Vector3.up * ((top - low) * 0.5f), thick);
				cap.OBox(new Vector3(c.x, top + 0.4f, c.z), dir * (len * 0.5f + 0.5f), Vector3.up * 0.4f, thick * 1.25f);
				segments++;
			}
			return segments;
		}

		private static void BuildYard(Layout layout, int seed, Material source)
		{
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0xFAC70FUL);
			}
			int n = layout.Count;
			float h = RoadHalfWidth;
			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			float a = h + 2.5f;
			float b = a + 0.9f;
			Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
			Color concrete = new Color(0.58f, 0.57f, 0.55f);
			Color sheet = new Color(0.42f, 0.27f, 0.20f);
			Color metal = new Color(0.14f, 0.14f, 0.16f);

			// Runs of wall and fence, with open stretches between.
			int runs = 0;
			for (int s = 0; s < 2; s++)
			{
				float sg = s == 0 ? 1f : -1f;
				int at = ahead + 6;
				while (at < n - behind - 6)
				{
int length = 5 + (int)(rng.Next() * 8.0);
int gap = 30 + (int)(rng.Next() * 60.0);
					int kind = (int)(rng.Next() * 3.0);
					int end = Math.Min(n - behind - 6, at + length);
					if (end > at)
					{
						int count = end - at;
						switch (kind)
						{
							case 0:
								Loop.SweepRange(GetBatch(batches, "yard barrier", concrete, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * b, -0.6f), new Vector2(sg * (b - 0.2f), 1.1f), new Vector2(sg * (a + 0.2f), 1.1f) }, at, count);
								for (int i = at; i < end; i++)
								{
									bool orange = (i / 2) % 2 == 0;
									Loop.Cell(GetBatch(batches, orange ? "yard orange" : "yard white", orange ? new Color(0.92f, 0.45f, 0.08f) : new Color(0.9f, 0.9f, 0.88f)), layout, i, sg * (a + 0.2f), sg * (b - 0.2f), 1.14f);
								}
								break;
							case 1:
								Loop.SweepRange(GetBatch(batches, "yard sheet wall", sheet, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * (a + 0.35f), -0.6f), new Vector2(sg * (a + 0.35f), 4.5f), new Vector2(sg * a, 4.5f) }, at, count);
								break;
							default:
								Loop.SweepRange(GetBatch(batches, "hidden yard fence", metal, true, true), layout, new[] { new Vector2(sg * a, -0.6f), new Vector2(sg * (a + 0.4f), -0.6f), new Vector2(sg * (a + 0.4f), 3.2f), new Vector2(sg * a, 3.2f) }, at, count);
								foreach (float rail in new[] { 1.0f, 2.0f, 3.0f })
								{
									Loop.SweepRange(GetBatch(batches, "yard rail", metal), layout, new[] { new Vector2(sg * a, rail - 0.07f), new Vector2(sg * (a + 0.3f), rail - 0.07f), new Vector2(sg * (a + 0.3f), rail + 0.07f), new Vector2(sg * a, rail + 0.07f) }, at, count);
								}
								for (int i = at; i < end; i += 3)
								{
									GetBatch(batches, "yard rail", metal).Box(layout.At(i, sg * (a + 0.15f), 1.7f), new Vector3(0.3f, 3.4f, 0.3f));
								}
								break;
						}
						runs++;
					}
					at = end + gap;
				}
			}
// Where the hall stands: nothing of the yard goes inside it.
			List<Vector3> halls = new List<Vector3>();
			List<float> hallR = new List<float>();
			foreach (Planned pl in planned)
			{
				if (pl.Prune)
				{
					halls.Add(pl.NewCenterXZ);
					hallR.Add(0.8f * Mathf.Sqrt(pl.Extent.x * pl.Extent.x + pl.Extent.z * pl.Extent.z));
				}
			}
int medianLen = 0;
			if (uSet)
			{
				medianLen = BuildMedian(batches, layout);
			}
int spinnerWall = 0;
			if (spinnerSet)
			{
				spinnerWall = BuildSpinnerWall(batches, layout);
			}
			Mod.Info(P.Key + ": wall of " + spinnerWall + " segments on the far side of the road from the spinner");
			Transform yard = new GameObject("Yard").transform;
			yard.SetParent(root.transform, false);
			int meshes = Emit(batches, yard, source);

			// The scene's own pieces (sheds, containers, buildings, towers, pipes), each sunk into the ground rather than left standing above it:
			// a few beside the road, many more in the open country beyond it, and a row across the top of the first big U-turn.
			int placed = 0;
			int scattered = 0;
			int barrier = 0;
			if (yardProtos.Count == 0)
			{
				Mod.Log.LogWarning(P.Key + ": no loose pieces of the yard were found to line the road with.");
				Mod.Info(P.Key + ": yard of " + runs + " runs of wall and fence (" + meshes + " meshes), no pieces");
				return;
			}
			List<Vector3> stood = new List<Vector3>();
			Vector3 tsize = terrain.terrainData.size;
			Vector3 tpos = terrain.GetPosition();

			bool Clear(Vector3 centre, float radius, float roadGap)
			{
				float need = roadGap + radius;
				for (int i = 0; i < n; i += 2)
				{
					float dx = layout.Points[i].x - centre.x;
					float dz = layout.Points[i].z - centre.z;
					if (dx * dx + dz * dz < need * need)
					{
						return false;
					}
				}
				foreach (Planned pl in planned)
				{
					if (pl.Prune && pl.HasHall)
					{
						Vector3 lc = Quaternion.Inverse(pl.HallTurn) * new Vector3(centre.x - pl.HallCentre.x, 0f, centre.z - pl.HallCentre.z);
						if (Math.Abs(lc.x) < pl.HallHalf.x + radius && Math.Abs(lc.z) < pl.HallHalf.z + radius)
						{
							return false;
						}
					}
				}
if (spinnerSet)
				{
					float sdx = spinnerPos.x - centre.x;
					float sdz = spinnerPos.z - centre.z;
					float sneed = spinnerR + radius;
					if (sdx * sdx + sdz * sdz < sneed * sneed)
					{
						return false;
					}
				}
				foreach (Vector3 o in stood)
				{
					float dx = o.x - centre.x;
					float dz = o.z - centre.z;
					float sum = o.y + radius + 6f;
					if (dx * dx + dz * dz < sum * sum)
					{
						return false;
					}
				}
				return true;
			}

			void Stand(Proto proto, Vector3 centre, Vector3 alongDir, float jitter)
			{
				bool xLong = proto.Size.x >= proto.Size.z;
				float yaw = Vector3.SignedAngle(xLong ? Vector3.right : Vector3.forward, alongDir, Vector3.up) + (rng.Next() < 0.5 ? 0f : 180f) + jitter;
				Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
				Transform t = proto.T;
				float bottom = proto.Centre.y - proto.Size.y * 0.5f;
				Vector3 pivotOffset = t.position - proto.Centre;
				float ground = LowestGround(centre, proto.Size.x * 0.5f, proto.Size.z * 0.5f) - 0.4f;
				Vector3 place = new Vector3(centre.x, ground + (t.position.y - bottom), centre.z) + turn * new Vector3(pivotOffset.x, 0f, pivotOffset.z);
				GameObject clone = UnityEngine.Object.Instantiate(t.gameObject, place, turn * t.rotation, yard);
				clone.name = t.name;
				clone.transform.localScale = Vector3.Scale(t.localScale, t.parent != null ? t.parent.lossyScale : Vector3.one);
				clone.SetActive(true);
				stood.Add(new Vector3(centre.x, Math.Max(proto.Size.x, proto.Size.z) * 0.5f, centre.z));
			}

			// Beside the road.
			for (int s = 0; s < 2; s++)
			{
				float sg = s == 0 ? 1f : -1f;
				int at = ahead + 8;
				while (at < n - behind - 8 && placed < 12)
				{
					at += 25 + (int)(rng.Next() * 40.0);
					Proto proto = yardProtos[(int)(rng.Next() * yardProtos.Count)];
					float along = Math.Max(proto.Size.x, proto.Size.z);
					float depth = Math.Min(proto.Size.x, proto.Size.z);
					int index = at % n;
					Vector3 centre = layout.At(index, sg * (b + 12f + (float)rng.Next() * 30f + depth * 0.5f), 0f);
					if (!Clear(centre, along * 0.5f, 6f))
					{
						continue;
					}
					Stand(proto, centre, Tangent(layout, index), (float)rng.Range(-12.0, 12.0));
					placed++;
				}
			}

			// The open country: up to 45 more, anywhere on the terrain at least 70 m from the road and 80 m from its edge.
			for (int attempt = 0; attempt < 600 && scattered < 45; attempt++)
			{
				Proto proto = yardProtos[(int)(rng.Next() * yardProtos.Count)];
				float radius = Math.Max(proto.Size.x, proto.Size.z) * 0.5f;
				float x = tpos.x + 80f + (float)rng.Next() * (tsize.x - 160f);
				float z = tpos.z + 80f + (float)rng.Next() * (tsize.z - 160f);
				Vector3 centre = new Vector3(x, 0f, z);
				if (!Clear(centre, radius, 70f))
				{
					continue;
				}
				double ang = rng.Next() * 6.2831853;
				Stand(proto, centre, new Vector3((float)Math.Cos(ang), 0f, (float)Math.Sin(ang)), 0f);
				scattered++;
			}

			// Across the top of the first big U-turn: a row of buildings, 40 m beyond the ends of the arc, from one straight toward the other,
			// so the turn cannot be cut across. None stands on the building's own walls.
if (false && uSet)
			{
				Vector3 outward = Flat(uApex - uCentre);
				outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
				Vector3 chord = new Vector3(-outward.z, 0f, outward.x);
				Vector3 mid = Flat(uCentre) - outward * (uRadius * 0.1f + 40f);
				float t = -uRadius * 1.05f;
				int guard = 0;
				while (t < uRadius * 1.05f && guard++ < 40)
				{
					Proto proto = yardProtos[(int)(rng.Next() * yardProtos.Count)];
					float along = Math.Max(proto.Size.x, proto.Size.z);
					float depth = Math.Min(proto.Size.x, proto.Size.z);
					Vector3 centre = mid + chord * (t + along * 0.5f);
					t += along + 3f;
					if (!Clear(centre, Math.Max(along, depth) * 0.5f, h + 18f))
					{
						continue;
					}
					bool onWalls = false;
					foreach (Planned pl in planned)
					{
						if (pl.Prune && pl.HasHall && pl.HallTris != null && NearTris(pl.HallTris, centre.x, centre.z, 0f))
						{
							onWalls = true;
						}
					}
					if (onWalls)
					{
						continue;
					}
					Stand(proto, centre, chord, 0f);
					barrier++;
				}
			}
// A skyline on the enlarged ground: tall pieces far from the hall, out in the band the original terrain never covered.
			int skyline = 0;
			Vector3 hallAt = Vector3.zero;
			bool haveHall = false;
			foreach (Planned pl in planned)
			{
				if (pl.Prune && pl.HasHall)
				{
					hallAt = pl.HallCentre;
					haveHall = true;
				}
			}
			Vector3 tmid = new Vector3(tpos.x + tsize.x * 0.5f, 0f, tpos.z + tsize.z * 0.5f);
			bool banded = tsize.x > 2000f;
			for (int attempt = 0; attempt < 2500 && skyline < 90; attempt++)
			{
				Proto proto = yardProtos[(int)(rng.Next() * yardProtos.Count)];
				for (int k = 0; k < 3; k++)
				{
					Proto other = yardProtos[(int)(rng.Next() * yardProtos.Count)];
					if (other.Size.y > proto.Size.y)
					{
						proto = other;
					}
				}
				float radius = Math.Max(proto.Size.x, proto.Size.z) * 0.5f;
				float x = tpos.x + 60f + (float)rng.Next() * (tsize.x - 120f);
				float z = tpos.z + 60f + (float)rng.Next() * (tsize.z - 120f);
				if (banded && Math.Abs(x - tmid.x) < 770f && Math.Abs(z - tmid.z) < 770f)
				{
					continue;
				}
				Vector3 centre = new Vector3(x, 0f, z);
				if (haveHall && Flat(centre - hallAt).magnitude < 650f)
				{
					continue;
				}
				if (!Clear(centre, radius, 60f))
				{
					continue;
				}
				double ang = rng.Next() * 6.2831853;
				Stand(proto, centre, new Vector3((float)Math.Cos(ang), 0f, (float)Math.Sin(ang)), 0f);
				skyline++;
			}
			Mod.Info(P.Key + ": skyline of " + skyline + " buildings on the outer ground; wall out of the hall of " + medianLen + " m");
			Mod.Info(P.Key + ": yard of " + runs + " runs of wall and fence (" + meshes + " meshes), " + placed + " pieces beside the road, " + scattered + " in the open country, " + barrier + " across the top of the U-turn, from " + yardProtos.Count + " kinds");
		}
	}
}
