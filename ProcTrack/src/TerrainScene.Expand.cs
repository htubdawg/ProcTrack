using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// A bigger terrain for a map whose own is too small for the lap (the Factory's is 1500 m square). A new terrain data is made: the old
	/// ground, paint, grass and trees keep their places in the world, the ground outside them continues the old edge (and is then roughened
	/// like the rest), and the terrain object is moved so that the old area sits in the middle of the new.
	/// </summary>
	internal static partial class TerrainScene
	{
/// <summary>The terrain as the scene had it, before it was enlarged: what the old objects stood on. Null when the terrain was not enlarged.</summary>
		private static Ground preExpand;

		private static float Bilinear(float[,] a, float u, float v)
		{
			int h = a.GetLength(0);
			int w = a.GetLength(1);
			u = Mathf.Clamp(u, 0f, w - 1);
			v = Mathf.Clamp(v, 0f, h - 1);
			int i = Math.Min((int)u, w - 2);
			int j = Math.Min((int)v, h - 2);
			float fu = u - i;
			float fv = v - j;
			return Mathf.Lerp(Mathf.Lerp(a[j, i], a[j, i + 1], fu), Mathf.Lerp(a[j + 1, i], a[j + 1, i + 1], fu), fv);
		}

		private static TerrainData ExpandTerrain(TerrainData src, float factor, Terrain t, out Vector3 newPos)
		{
			TerrainData d = UnityEngine.Object.Instantiate(src);
			Vector3 os = src.size;
			Vector3 ns = new Vector3(os.x * factor, os.y, os.z * factor);
			Vector3 op = t.GetPosition();
			newPos = new Vector3(op.x - (ns.x - os.x) * 0.5f, op.y, op.z - (ns.z - os.z) * 0.5f);
			Vector3 np = newPos;

			// Ground: the old heights where they exist, the old edge continued beyond it.
			int ores = src.heightmapResolution;
float[,] oh = src.GetHeights(0, 0, ores, ores);
			preExpand = new Ground(op, os, (float[,])oh.Clone());
			int nres = (ores - 1) * 2 + 1;
			d.heightmapResolution = nres;
			d.size = ns;
float[,] nh = new float[nres, nres];
			// FlatBase: the old hills are thrown away (they are cliffs and rims where the new lap runs over them); the level is the old middle height.
			List<float> sorted = new List<float>();
			for (int j = 0; j < ores; j += 4)
			{
				for (int i = 0; i < ores; i += 4)
				{
					sorted.Add(oh[j, i]);
				}
			}
			sorted.Sort();
			float flatLevel = sorted[sorted.Count / 2];
			for (int j = 0; j < nres; j++)
			{
				float wz = np.z + j * ns.z / (nres - 1);
				for (int i = 0; i < nres; i++)
				{
					float wx = np.x + i * ns.x / (nres - 1);
nh[j, i] = P.FlatBase ? flatLevel : Bilinear(oh, (wx - op.x) / os.x * (ores - 1), (wz - op.z) / os.z * (ores - 1));
				}
			}
			d.SetHeights(0, 0, nh);

			// Paint: the nearest old cell, the edge continued beyond.
			int ar = src.alphamapResolution;
			int layers = src.alphamapLayers;
			float[,,] oa = src.GetAlphamaps(0, 0, ar, ar);
			int nar = ar * 2;
			d.alphamapResolution = nar;
			float[,,] na = new float[nar, nar, layers];
			for (int j = 0; j < nar; j++)
			{
				float wz = np.z + (j + 0.5f) * ns.z / nar;
				int oj = Mathf.Clamp((int)((wz - op.z) / os.z * ar), 0, ar - 1);
				for (int i = 0; i < nar; i++)
				{
					float wx = np.x + (i + 0.5f) * ns.x / nar;
					int oi = Mathf.Clamp((int)((wx - op.x) / os.x * ar), 0, ar - 1);
					for (int l = 0; l < layers; l++)
					{
						na[j, i, l] = oa[oj, oi, l];
					}
				}
			}
			d.SetAlphamaps(0, 0, na);

			// Grass: where the old terrain was, none beyond.
			int dw = src.detailWidth;
			int dh = src.detailHeight;
			int ndr = dw * 2;
			d.SetDetailResolution(ndr, src.detailResolutionPerPatch);
			for (int l = 0; l < src.detailPrototypes.Length; l++)
			{
				int[,] od = src.GetDetailLayer(0, 0, dw, dh, l);
				int[,] nd = new int[ndr, ndr];
				for (int j = 0; j < ndr; j++)
				{
					float wz = np.z + (j + 0.5f) * ns.z / ndr;
					float fz = (wz - op.z) / os.z;
					if (fz < 0f || fz >= 1f)
					{
						continue;
					}
					int oj = (int)(fz * dh);
					for (int i = 0; i < ndr; i++)
					{
						float wx = np.x + (i + 0.5f) * ns.x / ndr;
						float fx = (wx - op.x) / os.x;
						if (fx < 0f || fx >= 1f)
						{
							continue;
						}
						nd[j, i] = od[oj, (int)(fx * dw)];
					}
				}
				d.SetDetailLayer(0, 0, l, nd);
			}

			// Trees keep their world places.
			TreeInstance[] trees = src.treeInstances;
			for (int k = 0; k < trees.Length; k++)
			{
				float wx = op.x + trees[k].position.x * os.x;
				float wz = op.z + trees[k].position.z * os.z;
				trees[k].position = new Vector3((wx - np.x) / ns.x, trees[k].position.y, (wz - np.z) / ns.z);
			}
			d.SetTreeInstances(trees, true);
			Mod.Info(P.Key + ": terrain enlarged from " + os.x.ToString("0") + " x " + os.z.ToString("0") + " to " + ns.x.ToString("0") + " x " + ns.z.ToString("0") + " m (heights " + nres + ", paint " + nar + ", grass " + ndr + ")");
			return d;
		}
	}
}
