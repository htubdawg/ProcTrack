using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// The Mountain's tunnel. The original is a simple curved tube, an S, that the road runs through. This one is built along a bendy stretch
	/// of the new lap: a shell with an inside and an outside surface, both solid, swept along the road, with a rim at each end and a few lamps
	/// inside. The terrain cannot be raised over a road (it is a height field), so the tube stands up out of the ground like the original.
	/// </summary>
	internal static partial class TerrainScene
	{
		private const int TunnelLength = 44;
		private const float InnerHalf = 23f;
		private const float OuterHalf = 26.5f;
		// Upright walls 7 m high under the arch: 22 m inside at the top, a tube more than a flat vault (it was 4 + 11, and read as squat).
		private const float WallHeight = 7f;
		private const float InnerRise = 15f;
		private const float OuterRise = 18.5f;
		private const int ArchSegments = 12;

		/// <summary>The cross-section from the left foot, over the top, to the right foot, as (lateral, height) pairs.</summary>
		private static Vector2[] Arch(float half, float wall, float rise, float foot)
		{
			List<Vector2> p = new List<Vector2> { new Vector2(-half, foot) };
			for (int k = 0; k <= ArchSegments; k++)
			{
				float a = Mathf.PI * (1f - (float)k / ArchSegments);
				p.Add(new Vector2(half * Mathf.Cos(a), wall + rise * Mathf.Sin(a)));
			}
			p.Add(new Vector2(half, foot));
			return p.ToArray();
		}

				/// <summary>
		/// Picks a stretch of <paramref name="length"/> points away from the grid and from the hazards already placed, with no bend tighter
		/// than a 70 m radius, that bends the most, an S (a left and a right bend both) before a plain curve before nothing. -1: none.
		/// </summary>
		private static int ChooseTunnel(Layout layout, Rng rng, int length)
		{
			int n = layout.Count;
			int behind = Mathf.RoundToInt(LevelBehind / layout.Step);
			int ahead = Mathf.RoundToInt(LevelAhead / layout.Step);
			float tightest = layout.Step / 70f;
			int best = -1;
			float bestScore = -1f;
			for (int i = ahead + 30; i < n - behind - length - 30; i += 2)
			{
				if (!Spaced(spots, i + length / 2, n, 40 + length / 2))
				{
					continue;
				}
				float left = 0f;
				float right = 0f;
				float worst = 0f;
				for (int q = 0; q < length; q++)
				{
					float t = Loop.Turn(layout, (i + q) % n);
					if (t > 0f) { left += t; } else { right -= t; }
					worst = Math.Max(worst, Math.Abs(t));
				}
				if (worst > tightest)
				{
					continue;
				}
				float score = Math.Min(left, right) * 3f + left + right + (float)rng.Next() * 0.05f;
				if (left + right < 0.25f)
				{
					score *= 0.1f;
				}
				if (score > bestScore)
				{
					bestScore = score;
					best = i;
				}
			}
			return best;
		}

		/// <summary>Road heights from lap point a to point b (either may be outside 0..n) set to a straight slope between their two ends: no bumps.</summary>
		private static void Level(Layout layout, int a, int b)
		{
			int n = layout.Count;
			float ya = layout.Points[((a % n) + n) % n].y;
			float yb = layout.Points[((b % n) + n) % n].y;
			for (int i = a; i <= b; i++)
			{
				float u = (float)(i - a) / (b - a);
				int m = ((i % n) + n) % n;
				Vector3 p = layout.Points[m];
				p.y = Mathf.Lerp(ya, yb, u);
				layout.Points[m] = p;
			}
		}

		private static int tunnelFrom = -1;

		/// <summary>Chooses the stretch and levels the road through it (a straight slope from end to end, no bumps), before the road and the terrain are built.</summary>
		private static void PlanTunnel(Layout layout, int seed)
		{
			Rng rng;
			unchecked
			{
				rng = new Rng((ulong)(uint)seed ^ 0x7077E1UL);
			}
			tunnelFrom = ChooseTunnel(layout, rng, TunnelLength);
			if (tunnelFrom < 0)
			{
				return;
			}
			spots.Add(tunnelFrom + TunnelLength / 2);
			Level(layout, tunnelFrom - 14, tunnelFrom + TunnelLength + 14);
		}

		private static void BuildTunnel(GameObject env, Layout layout, int seed, Material fallback)
		{
int from = tunnelFrom;
			if (from < 0)
			{
				Mod.Info(P.Key + ": no stretch of this lap suits the tunnel, none is built.");
				return;
			}
			int n = layout.Count;

			// The cave's own rock, if it repeats; else plain rock grey.
			Material rock = null;
			Transform cave = FindFirst(env.transform, "Decoration|Cave");
			Renderer caveRenderer = cave != null ? cave.GetComponentInChildren<Renderer>(true) : null;
			Material old = caveRenderer != null ? caveRenderer.sharedMaterial : null;
			Color grey = new Color(0.42f, 0.46f, 0.52f);
			if (old != null && old.mainTexture != null && old.mainTexture.wrapMode == TextureWrapMode.Repeat)
			{
				rock = new Material(old) { hideFlags = HideFlags.HideAndDontSave };
				rock.mainTextureScale = Vector2.one;
				rock.mainTextureOffset = Vector2.zero;
			}
			else
			{
				rock = CircuitScene.MakeMaterial(old != null ? old : fallback, grey);
			}

			Vector2[] outer = Arch(OuterHalf, WallHeight, OuterRise, -3f);
			Vector2[] inner = Arch(InnerHalf, WallHeight, InnerRise, -0.3f);
			Batch shell = new Batch("tunnel", grey, true, true);
			const float tile = 16f;
			float[] ou = new float[outer.Length];
			float[] iu = new float[inner.Length];
			for (int k = 1; k < outer.Length; k++)
			{
				ou[k] = ou[k - 1] + (outer[k] - outer[k - 1]).magnitude / tile;
			}
			for (int k = 1; k < inner.Length; k++)
			{
				iu[k] = iu[k - 1] + (inner[k] - inner[k - 1]).magnitude / tile;
			}
			Vector2 centre = new Vector2(0f, WallHeight);
			for (int q = 0; q < TunnelLength; q++)
			{
				int i = (from + q) % n;
				int j = (i + 1) % n;
				Vector3 r0 = layout.Right(i);
				Vector3 r1 = layout.Right(j);
				Vector3 p0 = layout.Points[i];
				Vector3 p1 = layout.Points[j];
				float v0 = (from + q) * layout.Step / tile;
				float v1 = (from + q + 1) * layout.Step / tile;
				for (int k = 0; k + 1 < outer.Length; k++)
				{
					Vector2 a = outer[k];
					Vector2 c = outer[k + 1];
					Vector2 mid = (a + c) * 0.5f - centre;
					Vector3 hint = r0 * mid.x + Vector3.up * mid.y;
					shell.QuadUV(p0 + r0 * a.x + Vector3.up * a.y, p0 + r0 * c.x + Vector3.up * c.y, p1 + r1 * c.x + Vector3.up * c.y, p1 + r1 * a.x + Vector3.up * a.y,
						new Vector2(ou[k], v0), new Vector2(ou[k + 1], v0), new Vector2(ou[k + 1], v1), new Vector2(ou[k], v1), hint);
				}
				for (int k = 0; k + 1 < inner.Length; k++)
				{
					Vector2 a = inner[k];
					Vector2 c = inner[k + 1];
					Vector2 mid = (a + c) * 0.5f - centre;
					Vector3 hint = -(r0 * mid.x + Vector3.up * mid.y);
					shell.QuadUV(p0 + r0 * a.x + Vector3.up * a.y, p0 + r0 * c.x + Vector3.up * c.y, p1 + r1 * c.x + Vector3.up * c.y, p1 + r1 * a.x + Vector3.up * a.y,
						new Vector2(iu[k], v0), new Vector2(iu[k + 1], v0), new Vector2(iu[k + 1], v1), new Vector2(iu[k], v1), hint);
				}
			}
			// The rims: the ring between outside and inside at each end, facing out along the road.
			for (int end = 0; end < 2; end++)
			{
				int i = (from + (end == 0 ? 0 : TunnelLength)) % n;
				Vector3 r = layout.Right(i);
				Vector3 p = layout.Points[i];
				Vector3 along = Tangent(layout, i) * (end == 0 ? -1f : 1f);
				for (int k = 0; k + 1 < inner.Length; k++)
				{
					// The outside arch is a little taller and wider; pair the points by their place along the arch.
					Vector2 oa = outer[k];
					Vector2 oc = outer[k + 1];
					Vector2 ia = inner[k];
					Vector2 ic = inner[k + 1];
					shell.Quad(p + r * oa.x + Vector3.up * oa.y, p + r * oc.x + Vector3.up * oc.y, p + r * ic.x + Vector3.up * ic.y, p + r * ia.x + Vector3.up * ia.y, along);
				}
			}

			// Snow banked against both inner walls, as in the original cave (its CaveWalls, and their snow): a rounded bank 6 m up the
			// wall, a third of the way to the top, reaching the road's edge. Solid, not a wall.
			Transform caveWalls = FindFirst(env.transform, "Decoration|Cave|CaveWalls");
			Renderer wallsRenderer = caveWalls != null ? caveWalls.GetComponent<Renderer>() : null;
			Material oldSnow = wallsRenderer != null ? wallsRenderer.sharedMaterial : null;
			Material snow;
			if (oldSnow != null && oldSnow.mainTexture != null && oldSnow.mainTexture.wrapMode == TextureWrapMode.Repeat)
			{
				snow = new Material(oldSnow) { hideFlags = HideFlags.HideAndDontSave };
				snow.mainTextureScale = Vector2.one;
				snow.mainTextureOffset = Vector2.zero;
			}
			else
			{
				snow = CircuitScene.MakeMaterial(oldSnow != null ? oldSnow : rock, Snow);
			}
			float roadEdge = RoadHalfWidth - 0.2f;
			Batch banks = new Batch("tunnel snow bank", Snow, true);
			foreach (float sg in new[] { 1f, -1f })
			{
				// Across from the road's edge to the wall, over the top of the bank; a flat foot under it closes it.
				Vector2[] bank =
				{
					new Vector2(sg * roadEdge, -0.3f), new Vector2(sg * roadEdge, 0.05f), new Vector2(sg * (InnerHalf - 3.9f), 1.6f),
					new Vector2(sg * (InnerHalf - 2.9f), 3.3f), new Vector2(sg * (InnerHalf - 1.8f), 4.8f), new Vector2(sg * (InnerHalf - 0.8f), 5.7f),
					new Vector2(sg * (InnerHalf - 0.1f), 6f), new Vector2(sg * (InnerHalf - 0.1f), -0.3f)
				};
				Vector2 middle = Vector2.zero;
				foreach (Vector2 v in bank)
				{
					middle += v;
				}
				middle /= bank.Length;
				float[] bu = new float[bank.Length];
				for (int k = 1; k < bank.Length; k++)
				{
					bu[k] = bu[k - 1] + (bank[k] - bank[k - 1]).magnitude / tile;
				}
				for (int q = 0; q < TunnelLength; q++)
				{
					int i = (from + q) % n;
					int j = (i + 1) % n;
					Vector3 r0 = layout.Right(i);
					Vector3 r1 = layout.Right(j);
					Vector3 p0 = layout.Points[i];
					Vector3 p1 = layout.Points[j];
					float v0 = (from + q) * layout.Step / tile;
					float v1 = (from + q + 1) * layout.Step / tile;
					for (int k = 0; k + 1 < bank.Length; k++)
					{
						Vector2 a = bank[k];
						Vector2 c = bank[k + 1];
						Vector2 mid = (a + c) * 0.5f - middle;
						banks.QuadUV(p0 + r0 * a.x + Vector3.up * a.y, p0 + r0 * c.x + Vector3.up * c.y, p1 + r1 * c.x + Vector3.up * c.y, p1 + r1 * a.x + Vector3.up * a.y,
							new Vector2(bu[k], v0), new Vector2(bu[k + 1], v0), new Vector2(bu[k + 1], v1), new Vector2(bu[k], v1), r0 * mid.x + Vector3.up * mid.y);
					}
				}
			}

			GameObject go = new GameObject("Tunnel");
			go.transform.SetParent(root.transform, false);
			GameObject snowBanks = new GameObject("SnowBanks");
			snowBanks.transform.SetParent(go.transform, false);
			Mesh bankMesh = banks.ToMesh();
			snowBanks.AddComponent<MeshFilter>().sharedMesh = bankMesh;
			snowBanks.AddComponent<MeshRenderer>().sharedMaterial = snow;
			snowBanks.AddComponent<MeshCollider>().sharedMesh = bankMesh;
			Mesh mesh = shell.ToMesh();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = rock;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
			try
			{
				go.tag = "Wall";
			}
			catch (Exception)
			{
				Mod.Log.LogWarning("The tag Wall does not exist, the tunnel keeps the default tag.");
			}

			// A lamp every 40 m, warm, so the inside is not black under the shadow of the shell.
			int lamps = 0;
			for (int q = 4; q < TunnelLength; q += 8)
			{
				GameObject lamp = new GameObject("Lamp");
				lamp.transform.SetParent(go.transform, false);
				lamp.transform.position = layout.Points[(from + q) % n] + Vector3.up * 14f;
				Light light = lamp.AddComponent<Light>();
				light.type = LightType.Point;
				light.range = 48f;
				light.intensity = 3.5f;
				light.color = new Color(1f, 0.86f, 0.62f);
				light.shadows = LightShadows.None;
				lamps++;
			}

			float left = 0f;
			float right = 0f;
			for (int q = 0; q < TunnelLength; q++)
			{
				float t = Loop.Turn(layout, (from + q) % n);
				if (t > 0f) { left += t; } else { right -= t; }
			}
			Mod.Info(P.Key + ": tunnel of " + (TunnelLength * layout.Step).ToString("0") + " m at lap point " + from + " of " + n + " (bends " + (left * Mathf.Rad2Deg).ToString("0") + " degrees left, " + (right * Mathf.Rad2Deg).ToString("0") + " right), " + mesh.vertexCount + " vertices, " + lamps + " lamps, " + (rock.mainTexture != null ? "texture " + rock.mainTexture.name : "plain rock colour") + ", snow banks " + (snow.mainTexture != null ? "texture " + snow.mainTexture.name : "plain snow colour"));
		}
	}
}
