using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>One generated lap: a closed loop of points about 5 m apart. Point 0 is the start line, the car heads +X there, and at least the 230 m behind it are straight (the grid); 290 on the Stadium circuit.</summary>
	internal sealed class Layout
	{
		public Vector3[] Points;
		public float Length;
		public float Step;
		public int Seed;
		public int Attempt;
		public float MinRadius;
		public bool Fallback;
		/// <summary>The lap point at the middle of the hairpin and the number of points it spans, or -1 and 0 for a lap without one.</summary>
		public int Hairpin = -1;
		public int HairpinSpan;


		public int Count => Points.Length;

		/// <summary>The unit vector to the right of the direction of travel at point i, flat.</summary>
		public Vector3 Right(int i)
		{
			int n = Points.Length;
			Vector3 f = Points[(i + 1) % n] - Points[(i - 1 + n) % n];
			f.y = 0f;
			if (f.sqrMagnitude < 1e-6f)
			{
				return Vector3.back;
			}
			f.Normalize();
			return new Vector3(f.z, 0f, -f.x);
		}

		public Vector3 At(int i, float lateral, float lift)
		{
			int m = ((i % Points.Length) + Points.Length) % Points.Length;
			return Points[m] + Right(m) * lateral + Vector3.up * lift;
		}
	}

	/// <summary>
	/// The size of lap a map asks for. <see cref="Circuit"/> holds the numbers the mod started with: change them and every seed gives
	/// another Stadium track.
	/// </summary>
	internal sealed class Shape
	{
		public double Length;
		public double StraightMin;
		public double StraightMax;
		/// <summary>No side of the lap's bounding box may be longer than this. Infinity: no limit.</summary>
		public double MaxSpan = double.PositiveInfinity;
		/// <summary>The limit across the start straight (the Z side). NaN: the same as <see cref="MaxSpan"/>, which then also limits the straight's own side.</summary>
		public double MaxSpanZ = double.NaN;
		/// <summary>
		/// How bendy the lap may be. The curvature of each sine wave is held to Wobble times 0.9, so a larger number gives tighter bends; Harmonics
		/// is how many waves are added (2 up to Harmonics + 1 laps round); AmpCap caps one wave's sideways swing. MinRadius and Separation are
		/// the checks: no corner tighter than MinRadius, no two stretches more than 200 apart along the lap closer than Separation.
		/// The defaults are the circuit's and the forest's.
		/// </summary>
		public double Wobble = 0.0028;
		public int Harmonics = 6;
		public double AmpCap = 230.0;
		public double MinRadius = 115.0;
		public double Separation = 140.0;
		/// <summary>
		/// Length of a stretch kept dead straight in the middle of the second (back) straight, so something built for a straight road (a
		/// tunnel, a factory hall) has one. 0: none, the whole back straight bends like the rest. Must be under the straight's length.
		/// </summary>
		public double BackStraight = 0.0;
		/// <summary>True: the lap is never mirrored, so its corners all turn left.</summary>
		public bool NoMirror = false;
		/// <summary>
		/// Above 0: only the last this many metres of the start straight are kept dead straight (the grid behind the line and the start;
		/// the line is 90 before the straight's end), and the rest of it bends like the lap. 0: the whole start straight is kept.
		/// </summary>
		public double StraightKeep = 0.0;
		/// <summary>
		/// True: the lap's base is a chain of corners in place of the teardrop or stadium: ChainMin to ChainMax corner points round a ring
		/// (squashed to the span limits), some of them (DentChance each) pulled in toward the middle by DentDepth, each corner rounded with an
		/// arc of 1.4 to 3.9 times MinRadius, joined by straights, all scaled to the lap's length. A point pulled in turns against the lap, so
		/// the corners go both ways. The longest straight is the start straight.
		/// </summary>
		public bool Chain = false;
		public int ChainMin = 6;
		public int ChainMax = 9;
		public double DentChance = 0.4;
		public double DentDepth = 0.55;
		/// <summary>The chain must have at least this many straights of ChainStraightLength or more (the start straight among them), for
		/// things carried onto the start of a straight (ramps). The first 250 m of each such straight is kept clear of the wobble, so it stays
		/// a straight. 0: no such rule (and no straight but the start's kept).</summary>
		public int ChainStraights = 0;
		public double ChainStraightLength = 260.0;

		/// <summary>Humps in the lap's edge: a stretch pushed out sideways by up to Height and back again over Width metres.</summary>
		public Bump[] Bumps;
		/// <summary>
		/// Above 0: the lap's base is a teardrop, not a stadium: a wide half circle at one end (the first turn, where something big can stand)
		/// and a hairpin of about this radius (in metres, stretched by up to 30 percent by the seed) at the other, joined by two straights
		/// that lean toward each other by about <see cref="Taper"/> radians (jittered by the seed). It is the outline of two circles.
		/// </summary>
		public double HairpinRadius = 0.0;
public double Taper = 0.15;
		public double TaperLow = 0.8;
public double TaperSpread = 0.5;
		/// <summary>Above 0: a rounder lap in place of the teardrop. The hairpin circle (HairpinRadius) at one end, two parallel necks of length Neck leading away from it, then each flaring outward on an arc of this radius into one big circle at the other end. Taper is then the flare angle.</summary>
		public double FlareRadius = 0.0;
		public double Neck = 300.0;

		public sealed class Bump
		{
			/// <summary>Distance of the hump's middle along the lap from its reference: the start of the lap's base shape (From 0) or the end of its first half circle (From 1).</summary>
			public double At;
public double Width;
			/// <summary>True: the seed also picks which way the hump goes (out or in).</summary>
			public bool Flip;
			public double Height;
			public int From;
		}

		/// <summary>
		/// The circuit: a chain of six to nine corners going both ways in a box 1500 by 1000, corners down to a 60 m radius and 110 m between
		/// stretches (the walls stand 37.5 m out, so two stretches that pass keep 35 m between their walls), and 360 m of the start straight
		/// kept straight (270 m behind the line: the eight car grid needs 240). Checked off line over 200 seeds: no plain fallback lap, the
		/// longest straight 12 percent of the lap (was 17), about a fifth of the turning against the lap (was 4 percent).
		/// </summary>
		public static readonly Shape Circuit = new Shape
		{
			Length = Generator.TargetLength, StraightMin = 380.0, StraightMax = 700.0, MaxSpan = 1500.0, MaxSpanZ = 1000.0,
			MinRadius = 60.0, Separation = 110.0, Wobble = 0.004, StraightKeep = 360.0,
			Chain = true, ChainMin = 6, ChainMax = 9, DentChance = 0.4, DentDepth = 0.9
		};
	}

	/// <summary>
	/// Builds a lap from a seed. Everything here is plain arithmetic on doubles with our own generator and sine, in a fixed order, so every
	/// machine given the same seed builds the same lap. Do not change the order of the random draws without bumping the mod version:
	/// a room's checksum covers this file, so players with different copies cannot join each other anyway.
	///
	/// The shape: a stadium (two straights joined by two half circles, the start on one straight), bent by a sum of a few sine waves
	/// laid along the lap. The wave is faded out along the start straight so it stays dead straight. Then the lap is tested for tight
	/// corners and for coming near itself, and thrown away and redrawn if it fails.
	/// </summary>
	internal static class Generator
	{
		/// <summary>Lap length in game units. The run map's time objectives are scaled from it, so it is the same for every seed.</summary>
		public const double TargetLength = 3200.0;

		/// <summary>Half the width of the tarmac. The walls stand 14 beyond it.</summary>
		public const float RoadHalfWidth = 20f;

		private const double Step = 5.0;
		private const int Attempts = 600;

		public static Layout Make(int seed)
		{
			return Make(seed, Shape.Circuit);
		}

		public static Layout Make(int seed, Shape shape)
		{
			for (int attempt = 0; attempt < Attempts; attempt++)
			{
				Layout layout = Try(seed, attempt, 1.0, true, shape);
				if (layout != null)
				{
					return layout;
				}
			}
			// Never seen in testing (about 1 try in 8 passes), but a seed must always give a track: the plain stadium, no waves.
			Mod.Log.LogWarning("No lap passed the checks for seed " + seed + " in " + Attempts + " tries, using the plain stadium shape.");
			Layout plain = Try(seed, 0, 0.0, false, shape);
			plain.Fallback = true;
			return plain;
		}

		private static Layout Try(int seed, int attempt, double amplitude, bool check, Shape shape)
		{
			unchecked
			{
				Rng rng = new Rng((ulong)(uint)seed * 1000003UL + (ulong)attempt * 7919UL + 17UL);
				double straight = rng.Range(shape.StraightMin, shape.StraightMax);
double radius = (shape.Length - 2.0 * straight) / (2.0 * DetMath.Pi);

				// The teardrop base: the outline of a big circle and a small one. The start straight is the first tangent line, then the big
				// half circle, the back straight, and the hairpin (the small circle) back onto the start straight.
				bool tear = shape.HairpinRadius > 0.0;
				double lead = straight;
				double backLen = straight;
				double arcEnd;
double r2 = 0.0;
				double fromLead = 90.0;
				bool lolli = false;
				double a2 = 0.0;
double[] segLen = new double[8];
double[] segX = new double[8];
double[] segZ = new double[8];
double[] segH = new double[8];
double[] segR = new double[8];
				double chainMid = -1.0;
				List<double> keptStraights = null;
				if (shape.Chain)
				{
					if (!ChainBase(rng, shape, out segLen, out segX, out segZ, out segH, out segR, out lead, out chainMid, out r2, out a2, out keptStraights))
					{
						if (check)
						{
							return null;
						}
						// The plain stadium in its place (the fallback, all tries failed). ChainBase has set its outs to nothing, so the stadium's
						// own start straight, hairpin and middle are put back, or the start would be read from before the lap's first point.
						shape = new Shape { Length = shape.Length, StraightMin = shape.StraightMin, StraightMax = shape.StraightMax };
						tear = false;
						lead = straight;
						backLen = straight;
						chainMid = -1.0;
						keptStraights = null;
						r2 = 0.0;
						a2 = 0.0;
						arcEnd = straight + DetMath.Pi * radius;
					}
					else
					{
						tear = true;
						backLen = 0.0;
						arcEnd = lead;
					}
				}
				else if (tear && shape.FlareRadius > 0.0)
				{
					double theta = shape.Taper * (shape.TaperLow + shape.TaperSpread * rng.Next());
					r2 = shape.HairpinRadius * (1.0 + 0.3 * rng.Next());
					double rf = shape.FlareRadius;
					double nk = shape.Neck;
					double cs = DetMath.Cos(theta);
					double sn = DetMath.Sin(theta);
					double bigA = (r2 + rf * (1.0 - cs)) / cs;
					double bigB = sn / cs;
					double mid = (shape.Length - DetMath.Pi * r2 - 2.0 * nk - 2.0 * rf * theta - bigA * (DetMath.Pi + 2.0 * theta)) / (2.0 + bigB * (DetMath.Pi + 2.0 * theta));
					if (mid < 20.0)
					{
						if (check)
						{
							return null;
						}
						mid = 20.0;
					}
					double bigR = bigA + bigB * mid;
					a2 = DetMath.Pi;
double[] spec = { nk, DetMath.Pi * r2, nk, rf * theta, mid, bigR * (DetMath.Pi + 2.0 * theta), mid, rf * theta };
double[] specR = { 0.0, r2, 0.0, -rf, 0.0, bigR, 0.0, -rf };
					double hx = 0.0;
					double hz = 0.0;
					double hh = 0.0;
					for (int q = 0; q < 8; q++)
					{
						segLen[q] = spec[q];
						segX[q] = hx;
						segZ[q] = hz;
						segH[q] = hh;
						segR[q] = specR[q];
						if (specR[q] == 0.0)
						{
							hx += spec[q] * DetMath.Cos(hh);
							hz += spec[q] * DetMath.Sin(hh);
						}
						else
						{
							double cx = hx - specR[q] * DetMath.Sin(hh);
							double cz = hz + specR[q] * DetMath.Cos(hh);
							hh += spec[q] / specR[q];
							hx = cx + specR[q] * DetMath.Sin(hh);
							hz = cz - specR[q] * DetMath.Cos(hh);
						}
					}
lead = nk;
					fromLead = 280.0;
					lolli = true;
					backLen = lead;
arcEnd = lead + DetMath.Pi * r2 + nk;
				}
				else if (tear)
				{
					double phi = shape.Taper * (shape.TaperLow + shape.TaperSpread * rng.Next());
					r2 = shape.HairpinRadius * (1.0 + 0.3 * rng.Next());
					double cot = DetMath.Cos(phi) / DetMath.Sin(phi);
					double big = (shape.Length + 2.0 * r2 * cot - r2 * (DetMath.Pi - 2.0 * phi)) / (2.0 * cot + DetMath.Pi + 2.0 * phi);
					double tl = (big - r2) * cot;
					double a1 = DetMath.Pi + 2.0 * phi;
					a2 = DetMath.Pi - 2.0 * phi;
					lead = tl;
					backLen = tl;
					double hx = 0.0;
					double hz = 0.0;
					double hh = -phi;
					for (int q = 0; q < 4; q++)
					{
						bool line = q % 2 == 0;
						double rad = q == 1 ? big : (q == 3 ? r2 : 0.0);
						double sweep = q == 1 ? a1 : a2;
						segLen[q] = line ? tl : rad * sweep;
						segX[q] = hx;
						segZ[q] = hz;
						segH[q] = hh;
						segR[q] = rad;
						if (line)
						{
							hx += tl * DetMath.Cos(hh);
							hz += tl * DetMath.Sin(hh);
						}
						else
						{
							double cx = hx - rad * DetMath.Sin(hh);
							double cz = hz + rad * DetMath.Cos(hh);
							hh += sweep;
							hx = cx + rad * DetMath.Sin(hh);
							hz = cz - rad * DetMath.Cos(hh);
						}
					}
					arcEnd = tl + big * a1;
				}
				else
				{
					arcEnd = straight + DetMath.Pi * radius;
				}
double hairpinMid = chainMid >= 0.0 ? chainMid : lolli ? lead + DetMath.Pi * r2 * 0.5 : shape.Length - r2 * a2 * 0.5;

				int[] ks = new int[shape.Harmonics];
				for (int k = 0; k < ks.Length; k++)
				{
					ks[k] = k + 2;
				}
				double[] amp = new double[ks.Length];
				double[] phase = new double[ks.Length];
				for (int k = 0; k < ks.Length; k++)
				{
					double w = 2.0 * DetMath.Pi * ks[k] / shape.Length;
					double limit = shape.Wobble / (w * w);
					amp[k] = rng.Range(-1.0, 1.0) * Math.Min(limit * 0.9, shape.AmpCap) * amplitude;
					phase[k] = rng.Range(0.0, 2.0 * DetMath.Pi);
				}

double[] bumpHeight = new double[shape.Bumps != null ? shape.Bumps.Length : 0];
					for (int b = 0; b < bumpHeight.Length; b++)
					{
bumpHeight[b] = shape.Bumps[b].Height * (0.7 + 0.6 * rng.Next());
						if (shape.Bumps[b].Flip && rng.Next() < 0.5)
						{
							bumpHeight[b] = -bumpHeight[b];
						}
					}
					int count = (int)(shape.Length / Step + 0.5);
				double ds = shape.Length / count;
				double[] px = new double[count];
				double[] pz = new double[count];
				for (int i = 0; i < count; i++)
				{
					double s = i * ds;
double x, z, nx, nz;
					if (tear)
					{
						TearPoint(s, segLen, segX, segZ, segH, segR, out x, out z, out nx, out nz);
					}
					else
					{
						BasePoint(s, straight, radius, out x, out z, out nx, out nz);
					}
						// Distance along the lap from the stretch kept dead straight: the whole start straight, or with StraightKeep only its last
						// StraightKeep metres (the grid and the start line), so the rest of it bends like the lap.
						double keepFrom = shape.StraightKeep > 0.0 ? Math.Max(0.0, lead - shape.StraightKeep) : 0.0;
						double dist = s <= lead ? Math.Max(0.0, keepFrom - s) : Math.Min(s - lead, shape.Length - s + keepFrom);
					double window = DetMath.Smooth(dist / 200.0);
					if (shape.BackStraight > 0.0)
					{
double middle = arcEnd + backLen * 0.5;
						double outside = Math.Max(0.0, Math.Abs(s - middle) - shape.BackStraight * 0.5);
window *= DetMath.Smooth(outside / 150.0);
					}
					if (tear)
					{
						// The hairpin keeps its shape: no wobble on it, and the wobble comes in over 250 m beyond it.
						// Measured round the loop when the start straight bends (StraightKeep): it begins where the teardrop's hairpin ends. Without
						// it the start straight never bends, and the straight measure is kept so every other map's laps stay as they were.
						double fromMid = Math.Abs(s - hairpinMid);
						if (shape.StraightKeep > 0.0)
						{
							fromMid = Math.Min(fromMid, shape.Length - fromMid);
						}
						double fromHairpin = Math.Max(0.0, fromMid - r2 * a2 * 0.5 - (lolli ? 450.0 : 0.0));
						window *= DetMath.Smooth(fromHairpin / 250.0);
					}
					if (keptStraights != null)
					{
						// The chain's long straights (from, to pairs) keep no wobble, fading in over 120 m beyond them.
						double near = double.MaxValue;
						for (int q = 0; q < keptStraights.Count; q += 2)
						{
							near = Math.Min(near, Math.Max(0.0, Math.Max(keptStraights[q] - s, s - keptStraights[q + 1])));
						}
						window *= DetMath.Smooth(near / 120.0);
					}
					double d = 0.0;
					for (int k = 0; k < ks.Length; k++)
					{
						d += amp[k] * DetMath.Sin(2.0 * DetMath.Pi * ks[k] * s / shape.Length + phase[k]);
					}
d *= window;

						for (int b = 0; b < bumpHeight.Length; b++)
						{
							double u = s - (shape.Bumps[b].At + (shape.Bumps[b].From == 1 ? arcEnd : 0.0));
							if (Math.Abs(u) < shape.Bumps[b].Width * 0.5)
							{
								d += bumpHeight[b] * 0.5 * (1.0 + DetMath.Cos(2.0 * DetMath.Pi * u / shape.Bumps[b].Width));
							}
						}

					px[i] = x + nx * d;
					pz[i] = z + nz * d;
				}

				// Start the lap 90 before the end of the start straight: 290 or more of straight behind the line for the grid.
int s0 = (int)((lead - fromLead) / ds + 0.5);
				double ox = px[s0];
				double oz = pz[s0];
bool mirror = rng.Next() < 0.5 && !shape.NoMirror;

				Vector3[] points = new Vector3[count];
				for (int i = 0; i < count; i++)
				{
					int from = (i + s0) % count;
					double x = px[from] - ox;
					double z = pz[from] - oz;
					points[i] = new Vector3((float)x, 0f, (float)(mirror ? -z : z));
				}

// Turn the lap so that it heads exactly +X at the start line (the teardrop's straights lean, and the grid is laid straight behind the line).
				{
					double ang = Math.Atan2(points[1].z - points[count - 1].z, points[1].x - points[count - 1].x);
					double ca = Math.Cos(-ang);
					double sa = Math.Sin(-ang);
					for (int i = 0; i < count; i++)
					{
						double px0 = points[i].x;
						double pz0 = points[i].z;
						points[i] = new Vector3((float)(px0 * ca - pz0 * sa), 0f, (float)(px0 * sa + pz0 * ca));
					}
				}

				double minRadius = double.MaxValue;
				if (check)
				{
					// The lap has to fit the map: no side of its bounding box longer than the shape allows.
					if (!double.IsPositiveInfinity(shape.MaxSpan))
					{
						double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
						for (int i = 0; i < count; i++)
						{
							minX = Math.Min(minX, X(points, i));
							maxX = Math.Max(maxX, X(points, i));
							minZ = Math.Min(minZ, Z(points, i));
							maxZ = Math.Max(maxZ, Z(points, i));
						}
						if (maxX - minX > shape.MaxSpan || maxZ - minZ > (double.IsNaN(shape.MaxSpanZ) ? shape.MaxSpan : shape.MaxSpanZ))
						{
							return null;
						}
					}

					// Menger curvature of three points 15 apart: 1 / r = 2 |ab x bc| / (|ab| |bc| |ac|)
					for (int i = 0; i < count; i++)
					{
						int ia = (i - 3 + count) % count;
						int ic = (i + 3) % count;
						double abx = X(points, i) - X(points, ia);
						double abz = Z(points, i) - Z(points, ia);
						double bcx = X(points, ic) - X(points, i);
						double bcz = Z(points, ic) - Z(points, i);
						double acx = X(points, ic) - X(points, ia);
						double acz = Z(points, ic) - Z(points, ia);
						double cross = Math.Abs(abx * bcz - abz * bcx);
						if (cross > 1e-9)
						{
							double r = Math.Sqrt(abx * abx + abz * abz) * Math.Sqrt(bcx * bcx + bcz * bcz) * Math.Sqrt(acx * acx + acz * acz) / (2.0 * cross);
							if (r < minRadius)
							{
								minRadius = r;
							}
						}
					}
					if (minRadius < shape.MinRadius)
					{
						return null;
					}

					// Two stretches of road more than 200 apart along the lap must not come within Separation of each other.
					int near = (int)(200.0 / ds);
					double sep2 = shape.Separation * shape.Separation;
					for (int i = 0; i < count; i++)
					{
						for (int j = i + near; j < count; j++)
						{
							if (count - (j - i) < near)
							{
								continue;
							}
							double dx = X(points, i) - X(points, j);
							double dz = Z(points, i) - Z(points, j);
							if (dx * dx + dz * dz < sep2)
							{
								return null;
							}
						}
					}
				}

				return new Layout
				{
					Points = points,
					Length = (float)(count * ds),
					Step = (float)ds,
					Seed = seed,
Attempt = attempt,
						Hairpin = tear ? (((int)(hairpinMid / ds + 0.5) - s0) % count + count) % count : -1,
						HairpinSpan = tear ? (int)(r2 * a2 / ds + 0.5) : 0,

					MinRadius = (float)minRadius
				};
			}
		}

		/// <summary>
		/// The corner chain (see <see cref="Shape.Chain"/>) as segments for <see cref="TearPoint"/>: the start straight first, then arcs and
		/// straights in turn. Every corner point makes the same draws whatever happens, so one corner never changes another's. False when no
		/// straight is long enough for the grid or the corners do not fit their straights.
		/// </summary>
		private static bool ChainBase(Rng rng, Shape shape, out double[] len, out double[] sx, out double[] sz, out double[] sh, out double[] rad, out double lead, out double mid, out double hr, out double ha, out List<double> kept)
		{
			len = sx = sz = sh = rad = null;
			lead = mid = hr = ha = 0.0;
			kept = null;
			int k = shape.ChainMin + (int)(rng.Next() * (shape.ChainMax - shape.ChainMin + 1));
			double squash = double.IsNaN(shape.MaxSpanZ) || double.IsPositiveInfinity(shape.MaxSpan) ? 0.7 : Math.Min(1.0, shape.MaxSpanZ / shape.MaxSpan);
			double[] vx = new double[k];
			double[] vz = new double[k];
			double[] want = new double[k];
			for (int i = 0; i < k; i++)
			{
				double jitter = rng.Next() - 0.5;
				bool dent = rng.Next() < shape.DentChance;
				double depth = rng.Next();
				want[i] = shape.MinRadius * (1.4 + 2.5 * rng.Next());
				double a = 2.0 * DetMath.Pi * (i + 0.6 * jitter) / k;
				double f = dent ? 1.0 - shape.DentDepth * (0.6 + 0.4 * depth) : 1.0 - 0.15 * depth;
				vx[i] = DetMath.Cos(a) * f;
				vz[i] = DetMath.Sin(a) * f * squash;
			}
			double[] turn = new double[k];
			double[] side = new double[k];
			double perimeter = 0.0;
			for (int i = 0; i < k; i++)
			{
				int p = (i - 1 + k) % k;
				int q = (i + 1) % k;
				double ix = vx[i] - vx[p], iz = vz[i] - vz[p], ox = vx[q] - vx[i], oz = vz[q] - vz[i];
				turn[i] = Math.Atan2(ix * oz - iz * ox, ix * ox + iz * oz);
				side[i] = Math.Sqrt(ox * ox + oz * oz);
				perimeter += side[i];
			}
			double scale = shape.Length / perimeter;
			double[] rho = new double[k];
			double[] tan = new double[k];
			for (int pass = 0; pass < 6; pass++)
			{
				for (int i = 0; i < k; i++)
				{
					int p = (i - 1 + k) % k;
					double room = 0.45 * scale * Math.Min(side[p], side[i]);
					double t = Math.Tan(Math.Abs(turn[i]) * 0.5);
					rho[i] = want[i];
					if (rho[i] * t > room)
					{
						rho[i] = Math.Max(shape.MinRadius * 1.1, room / Math.Max(1e-6, t));
					}
					tan[i] = rho[i] * t;
				}
				double length = 0.0;
				for (int i = 0; i < k; i++)
				{
					length += scale * side[i] - tan[i] - tan[(i + 1) % k] + rho[i] * Math.Abs(turn[i]);
				}
				scale *= shape.Length / length;
			}
			int first = -1;
			double longest = 0.0;
			int long_ = 0;
			for (int i = 0; i < k; i++)
			{
				double straight = scale * side[i] - tan[i] - tan[(i + 1) % k];
				if (straight < 10.0)
				{
					return false;
				}
				if (straight >= shape.ChainStraightLength)
				{
					long_++;
				}
				if (straight > longest)
				{
					longest = straight;
					first = i;
				}
			}
			if (longest < Math.Max(360.0, shape.StraightKeep + 30.0) || long_ < shape.ChainStraights)
			{
				return false;
			}
			kept = shape.ChainStraights > 0 ? new List<double>() : null;
			len = new double[2 * k];
			sx = new double[2 * k];
			sz = new double[2 * k];
			sh = new double[2 * k];
			rad = new double[2 * k];
			double hx = 0.0, hz = 0.0, hh = 0.0, acc = 0.0, sharpest = -1.0;
			for (int m = 0; m < k; m++)
			{
				int i = (first + m) % k;
				int c = (i + 1) % k;
				double straight = scale * side[i] - tan[i] - tan[c];
				len[2 * m] = straight;
				if (kept != null && m > 0 && straight >= shape.ChainStraightLength)
				{
					kept.Add(acc + 10.0);
					kept.Add(acc + Math.Min(straight - 10.0, 250.0));
				}
				sx[2 * m] = hx;
				sz[2 * m] = hz;
				sh[2 * m] = hh;
				hx += straight * DetMath.Cos(hh);
				hz += straight * DetMath.Sin(hh);
				acc += straight;
				double arc = rho[c] * Math.Abs(turn[c]);
				double r = turn[c] >= 0.0 ? rho[c] : -rho[c];
				len[2 * m + 1] = arc;
				sx[2 * m + 1] = hx;
				sz[2 * m + 1] = hz;
				sh[2 * m + 1] = hh;
				rad[2 * m + 1] = r;
				double cx = hx - r * DetMath.Sin(hh);
				double cz = hz + r * DetMath.Cos(hh);
				hh += arc / r;
				hx = cx + r * DetMath.Sin(hh);
				hz = cz - r * DetMath.Cos(hh);
				if (Math.Abs(turn[c]) > sharpest)
				{
					sharpest = Math.Abs(turn[c]);
					mid = acc + arc * 0.5;
					hr = rho[c];
					ha = Math.Abs(turn[c]);
				}
				acc += arc;
			}
			lead = len[0];
			return true;
		}

/// <summary>The teardrop at distance s along it, with the outward (right hand) normal: the four segments are line, left arc, line, left arc.</summary>
		private static void TearPoint(double s, double[] len, double[] sx, double[] sz, double[] sh, double[] rad, out double x, out double z, out double nx, out double nz)
		{
			double acc = 0.0;
for (int k = 0; k < len.Length; k++)
			{
				if (s <= acc + len[k] || k == len.Length - 1)
				{
					double u = s - acc;
					double h = sh[k];
					if (rad[k] == 0.0)
					{
						x = sx[k] + u * DetMath.Cos(h);
						z = sz[k] + u * DetMath.Sin(h);
						nx = DetMath.Sin(h);
						nz = -DetMath.Cos(h);
						return;
					}
					double r = rad[k];
					double cx = sx[k] - r * DetMath.Sin(h);
					double cz = sz[k] + r * DetMath.Cos(h);
					double hh = h + u / r;
					x = cx + r * DetMath.Sin(hh);
					z = cz - r * DetMath.Cos(hh);
					nx = DetMath.Sin(hh);
					nz = -DetMath.Cos(hh);
					return;
				}
				acc += len[k];
			}
			x = z = nx = nz = 0.0;
		}

		private static double X(Vector3[] p, int i)
		{
			return p[i].x;
		}

		private static double Z(Vector3[] p, int i)
		{
			return p[i].z;
		}

		/// <summary>
		/// The plain stadium at distance s along it, with the outward normal. It starts at the origin heading +X, turns left through a half
		/// circle, comes back along a second straight of the same length, and turns left again onto the start.
		/// </summary>
		private static void BasePoint(double s, double straight, double radius, out double x, out double z, out double nx, out double nz)
		{
			if (s < straight)
			{
				x = s; z = 0.0; nx = 0.0; nz = -1.0;
				return;
			}
			s -= straight;
			double arc = DetMath.Pi * radius;
			if (s < arc)
			{
				double a = s / radius;
				x = straight + radius * DetMath.Sin(a);
				z = radius - radius * DetMath.Cos(a);
				nx = DetMath.Sin(a);
				nz = -DetMath.Cos(a);
				return;
			}
			s -= arc;
			if (s < straight)
			{
				x = straight - s; z = 2.0 * radius; nx = 0.0; nz = 1.0;
				return;
			}
			s -= straight;
			double b = s / radius;
			x = 0.0 - radius * DetMath.Sin(b);
			z = radius + radius * DetMath.Cos(b);
			nx = -DetMath.Sin(b);
			nz = DetMath.Cos(b);
		}
	}
}
