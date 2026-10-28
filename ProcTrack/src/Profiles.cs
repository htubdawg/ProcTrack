using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripProcTrack
{
	/// <summary>
	/// A hazard or landmark of the old track that is carried onto the new lap: a copy is made, and put where the new road is as the original
	/// stood to the old road (same side, same distance, same angle to it, same height above the ground). The original is hidden.
	/// </summary>
	internal sealed class Transplant
	{
		public string Name;
		/// <summary>The object, below the scene's ENVIROMENT, in the '|' path syntax of <see cref="MapProfile"/>.</summary>
		public string Path;
		public int Count = 1;
		/// <summary>True: it stands on or beside the road, so it is put on a straight stretch. False: it stands apart (a machine beside the road) and only needs room.</summary>
		public bool OnRoad = true;
		/// <summary>No two copies of this kind, and no copy of any kind, closer than this many lap points (5 m each) apart.</summary>
		public int Spacing = 40;
		public bool HideOriginal = true;
		/// <summary>Other objects (same '|' paths) carried with this one by the same move, keeping their place relative to it (the hammers inside the factory hall).</summary>
		public string[] Also = new string[0];
		/// <summary>
		/// True: the object is anchored to the old road's sharpest bend near it (a U-turn) and put on the lap's first U-turn, turned and sized to
		/// match (the factory hall, which the old road turns through). Nothing else about where it goes is searched.
		/// </summary>
		public bool Turn;
		/// <summary>True: put near the start of a straight (a bend behind it, a straight run of 220 m ahead), not anywhere along one (a ramp to jump from).</summary>
		public bool StraightStart;
		/// <summary>For the objects in <see cref="Also"/>: only those within this many metres of the main object go with it (a jump pad belongs to its ramp). 0: all.</summary>
		public float AlsoRadius;

		/// <summary>True: after placing, slid sideways along the road until its middle is within the middle 45 percent of the road (a ramp), on the side it stood on.</summary>
		public bool CentreOnRoad;
	}

	/// <summary>
	/// Everything that differs between the game's terrain scenes: which map's entry is copied, how long and wide the lap is, which of the
	/// old track's objects are in the way, and how the road is made (painted onto the terrain, or a mesh laid on top of it).
	///
	/// Paths are relative to the scene's ENVIROMENT object and use '|' between names, because one of the game's objects is called
	/// "Start/Finish_Line" with a slash in its name. The names all come from SceneDump files of game 0.418.
	/// </summary>
	internal sealed class MapProfile
	{
		/// <summary>Short name: "Forest", "Mountain", "Factory", "Tokyo". Used in the settings, the log and the sign over the start line.</summary>
		public string Key;
		public string Display;
		/// <summary>The map entry of the game that is copied (its weather, music and icon are kept), and a name prefix to fall back on.</summary>
		public string SourceMap;
		public string SourcePrefix;
		public int TrackId;
		public string MapName;

		/// <summary>Our lap length, and the source map's own lap length in game units; times are scaled by the ratio.</summary>
		public double Lap;
		public float SourceLap;
		/// <summary>Half the width of the road.</summary>
		public float Half;
		public double StraightMin;
		public double StraightMax;
		/// <summary>The lap's bounding box may not be longer than this along the start straight and across it. 0: the terrain's size less its margins.</summary>
		public double SpanX;
		public double SpanZ;

		/// <summary>How bendy the lap is (see <see cref="Shape"/>). The defaults are the forest's.</summary>
		public double Wobble = 0.0028;
		public int Harmonics = 6;
		public double AmpCap = 230.0;
		public double MinRadius = 115.0;
		public double Separation = 140.0;
		/// <summary>Length of the dead straight kept in the back straight (see <see cref="Shape"/>). 0: none.</summary>
		public double BackStraight = 0.0;
		/// <summary>True: the lap always turns left (the old road's U-turn that is carried over turns left and cannot be mirrored).</summary>
		public bool NoMirror;
		/// <summary>Above 0: only the last this many metres of the start straight are kept dead straight (see <see cref="Shape.StraightKeep"/>). 0: all of it.</summary>
		public double StraightKeep;
		/// <summary>True: an S-curved tunnel is built over a bendy stretch of the lap, from the cave's material.</summary>
		public bool Tunnel;
/// <summary>
		/// A very sharp hairpin (see <see cref="Shape.HairpinRadius"/>): its radius in metres, and the lean of the two straights toward each
		/// other. 0: the lap is a stadium with no hairpin.
		/// </summary>
		public double HairpinRadius;
public double Taper = 0.15;
		public double TaperLow = 0.8;
public double TaperSpread = 0.5;
		public double FlareRadius;
public double Neck = 300.0;
		/// <summary>Above 0: the giant spinner stands this many metres along the lap from the hall turn (instead of where the old road put it).</summary>
public float SpinnerAlong;
		/// <summary>Above 0: the spinner stands this far (metres) to the side of the road, so its arms sweep across the tarmac.</summary>
		public float SpinnerLateral;
		/// <summary>True: a thick stand of the scene's own trees stands beyond the hairpin's outside, to fly into when the turn is missed.</summary>
		public bool HairpinTrees;
		/// <summary>True: sheds, containers, towers and pipes from the scene's own factory yard, and runs of wall and fence, line the road.</summary>
public bool Yard;
		/// <summary>Above 1: the terrain is rebuilt this many times bigger across (the old area stays in the middle).</summary>
public float Expand = 1f;
		/// <summary>True (with Expand): the enlarged terrain is flat at the old middle height, with only the seeded hills (Roughness) on it.</summary>
		public bool FlatBase;
		/// <summary>True: curve signs before the sharp bends, and a few burning barrels at them, copied from the scene's own (see SignGroup and FireGroups).</summary>
		public bool Markers;
		/// <summary>The group (in the '|' path syntax) whose children are the scene's curve signs. Each is a left or a right sign by its material.</summary>
		public string SignGroup;
		/// <summary>The scene's burning barrels to copy to the bends. Empty: no barrels.</summary>
		public string[] FireGroups = new string[0];
		/// <summary>True: new grass is laid along both edges of the road, from the scene's own grass layer, so the road shows from afar.</summary>
		public bool EdgeGrass;
		/// <summary>True: a tall invisible wall round the edge of the terrain.</summary>
		public bool EdgeWall = true;
		/// <summary>How much the terrain is roughened with seeded hills before the road is cut: the swing in metres, and the wavelength of the biggest hills. 0: the terrain is as the game has it.</summary>
		public float Roughness;
		public float RoughWave = 300f;
		/// <summary>Humps in the lap's edge (see <see cref="Shape.Bump"/>), to give a lap more corners where it would be a plain stadium.</summary>
		public Shape.Bump[] Bumps;

		/// <summary>Flat ground beside the road before the bank starts; the bank runs BankSlope across for every 1 up or down, within MinBank and MaxBank.</summary>
		public float Shoulder = 4f;
		public float BankSlope = 2.5f;
		public float MinBank = 18f;
		public float MaxBank = 45f;

		/// <summary>Placement weights: each metre climbed along the lap costs ClimbWeight, each metre of fall across the road AcrossWeight. Higher picks flatter ground.</summary>
		public float ClimbWeight = 1f;
		public float AcrossWeight = 0.5f;

		public string TerrainPath;

		/// <summary>Objects hidden whole. Their children are not looked at.</summary>
		public string[] HideAll = new string[0];
		/// <summary>Objects whose children are all hidden (the old arena, which holds the old road, its start line and its walls).</summary>
		public string[] HideChildren = new string[0];
		/// <summary>Objects at the scene's root that go with the old track's rules (reset zones).</summary>
		public string[] HideRoots = new string[0];
		/// <summary>Groups whose items are hidden one by one when the new road or its banks would run into them.</summary>
		public string[] InWay = new string[0];
		/// <summary>Groups whose children are the items hidden one by one when in the way (for a group inside an object that must stay, like the skyline ring).</summary>
		public string[] InWayItems = new string[0];
		/// <summary>Groups the lap is steered clear of when it is placed (buildings, a backdrop ring). What is still in the way is hidden.</summary>
		public string[] Avoid = new string[0];

		/// <summary>A circle the whole lap must stay inside: the backdrop ring of a scene with a skyline. Radius 0: none.</summary>
		public float RingX;
		public float RingZ;
		public float RingR;

		/// <summary>
		/// Forest: the old road's paint and grass are wiped and new ones laid, with wheat fields. Off: the terrain's own paint and grass are kept
		/// except on the road and a few metres beside it.
		/// </summary>
		public bool Rebuild;
		public string BaseLayer;
		/// <summary>The terrain paint layer the road is painted with. Null: none (the road is a mesh, or there is no road).</summary>
		public string RoadLayer;
		public string DirtLayer;
		public bool Wheat;

		/// <summary>An object of the old road that holds its mesh renderer and its HandlingModifier: the new road borrows its material, layer and grip. Null: no road mesh.</summary>
		public string RoadFrom;
		public Color RoadColor = new Color(0.17f, 0.17f, 0.18f);
		/// <summary>Units of road per repeat of the borrowed texture, across and along.</summary>
		public float RoadTile = 40f;

		/// <summary>A continuous wall on both sides of the road, close to its edge, and city blocks behind it (Tokyo).</summary>
		public bool Walls;
		/// <summary>The group (in the '|' path syntax) whose buildings are copied to line the walls, and the name prefixes that count as city blocks.</summary>
		public string BuildingGroup;

		/// <summary>0: no trees added. Otherwise the share (0 to 1) of the road's sides that get a stand of trees beside them, taken from the scene's own trees.</summary>
		public float RoadsideTrees;

		/// <summary>True: the scene's tornado is put on a path across the road instead of the old track.</summary>
		public bool Tornado;

		/// <summary>Hazards and landmarks of the old track that are carried onto the new lap.</summary>
		public Transplant[] Transplants = new Transplant[0];

		/// <summary>The map entry built from the game's, once it exists, and the track index of the scene it is hosted in.</summary>
		public MapDefinition Map;
		public int HostTrack;

		/// <summary>The weathers this map is dealt from: the ones the game ships for the same scene. Empty: the map keeps the one it was copied with.</summary>
		public List<WeatherProfile> Weathers = new List<WeatherProfile>();

		/// <summary>The setting that adds the map to the pool.</summary>
		public Func<bool> Enabled = () => true;
	}

	internal static class Profiles
	{
		public static readonly MapProfile Forest = new MapProfile
		{
			Key = "Forest",
			Display = "Procedural Forest",
			MapName = "ProcForest",
			SourceMap = "ForestRed",
			SourcePrefix = "Forest",
			TrackId = 22,
			Lap = 2700.0,
			SourceLap = 2690f,
			Half = 40f,
			StraightMin = 320.0,
			StraightMax = 560.0,
						TerrainPath = "GroundTerrain",
			HideAll = new[] { "Decoration|Fences" },
			HideChildren = new[] { "TrackArena2" },
			InWay = new[] { "Decoration" },
			Avoid = new[] { "Decoration|TreeLines" },
			Rebuild = true,
			BaseLayer = "NewLayer",
			RoadLayer = "ForrestRoadTexture",
			DirtLayer = "ForrestDirtLayer",
			Wheat = true,
Tornado = true,
			// Half 40 (an 80 m road): the hairpin is wide in metres but still the sharpest turn by far.
			HairpinRadius = 82.0,
			Taper = 0.2,
			MinRadius = 70.0,
			Separation = 150.0,
			// Bendier than the circuit's waves, two humps on the back half (in or out by the seed), and only the grid's 330 m of the
			// start straight kept straight: the teardrop's start straight alone was over a quarter of the lap. 2.7 km and at most 1250 m
			// across (SpanX, SpanZ), about the width the old 2.7 km laps had, so the lap stays inside the backdrop tree lines. Checked over
			// 300 seeds off line at 1250 m: no plain fallback lap, the longest straight 13 percent of the lap on average and 16 at most.
			SpanX = 1250.0,
			SpanZ = 1250.0,
			Wobble = 0.006,
			Harmonics = 8,
			AmpCap = 260.0,
			StraightKeep = 330.0,
			Bumps = new[]
			{
				new Shape.Bump { At = 200.0, Width = 480.0, Height = 110.0, From = 1, Flip = true },
				new Shape.Bump { At = -420.0, Width = 520.0, Height = 120.0, From = 1, Flip = true }
			},
			HairpinTrees = true,
			Roughness = 5f,
			RoughWave = 300f
		};

		/// <summary>
		/// A crater floor ringed by mountains. The road is a 33,000 vertex mesh in Track3, with a snow plane, a dirt plane and a pit wall
		/// that all follow the old road; they go. The terrain's own grip is uniform, so nothing is painted. The cave is hidden and a new ice
		/// tube is built over a bendy stretch of the lap from its rock (see TerrainScene.Tunnel), the twisty ramps are carried onto the
		/// starts of straights, the scene's curve signs are set before the sharp bends, the banks are wide and gentle, and stands of the
		/// scene's trees are added beside the road.
		/// </summary>
		public static readonly MapProfile Mountain = new MapProfile
		{
			Key = "Mountain",
			Display = "Procedural Mountain",
			MapName = "ProcMountain",
			SourceMap = "MountainDay",
			SourcePrefix = "Mountain",
			TrackId = 23,
			Lap = 2900.0,
			SourceLap = 3188f,
			Half = 18f,
StraightMin = 360.0,
			StraightMax = 560.0,
			BackStraight = 300.0,
			HairpinRadius = 36.0,
			Taper = 0.2,
			MinRadius = 30.0,
			Separation = 90.0,
			HairpinTrees = true,
			Roughness = 6f,
			RoughWave = 280f,
			SpanX = 1450.0,
			SpanZ = 900.0,
			Shoulder = 9f,
			BankSlope = 4f,
			MinBank = 36f,
			MaxBank = 85f,
			ClimbWeight = 2f,
			AcrossWeight = 1.5f,
			TerrainPath = "Terrain",
				HideAll = new[] { "Track3", "Decoration|Fences", "Decoration|CurveSigns", "Decoration|Cave" },
			InWay = new[] { "Decoration" },
			RoadFrom = "Track3|TrackFlat",
			RoadColor = new Color(0.30f, 0.32f, 0.36f),
			RoadTile = 60f,
			RoadsideTrees = 0.75f,
			Markers = true,
			SignGroup = "Decoration|CurveSigns",
Tunnel = true,
				Transplants = new[]
				{

					// The two twisty ramps, each with the jump pad that sits on it, put on the road near the start of a straight.
					new Transplant { Name = "ramp", Path = "Decoration|RampaTwisty", Also = new[] { "Decoration|Cube" }, AlsoRadius = 40f, Count = 1, OnRoad = true, Spacing = 70, StraightStart = true, CentreOnRoad = true },
					new Transplant { Name = "second ramp", Path = "Decoration|RampaTwisty (1)", Also = new[] { "Decoration|Cube" }, AlsoRadius = 40f, Count = 1, OnRoad = true, Spacing = 70, StraightStart = true, CentreOnRoad = true }
				}
		};

		/// <summary>
		/// An open dirt basin with a factory yard in it. The old track has no road of its own: the terrain is the road, with uniform grip.
		/// The new lap is painted onto it with the asphalt layer so it can be seen. The factory hall (with its barrel machine and one set of
		/// hammers) is fitted round the lap's first U-turn, the giant spinner stands across the road on the big loop, and the other set of
		/// hammers goes on a straight; the rest of the hazard group is hidden. The camp and the water tower are steered clear of; the yard's
		/// sheds, containers, towers and pipes, with runs of wall and fence, line the road (see TerrainScene.Yard). The terrain is enlarged
		/// and flattened round the old area so the 3.4 km lap fits.
		/// </summary>
		public static readonly MapProfile Factory = new MapProfile
		{
			Key = "Factory",
			Display = "Procedural Factory",
			MapName = "ProcFactory",
			SourceMap = "FactoryNight",
			SourcePrefix = "Factory",
			TrackId = 24,
Lap = 3400.0,
			SourceLap = 2822f,
			Half = 18f,
			// Long straights and wide U-turns (radius about 150, like the old road's), so the hall fits round the first U-turn.
			StraightMin = 840.0,
			StraightMax = 900.0,
			NoMirror = true,
			BackStraight = 0.0,
// Far from the hall, on the big loop: three humps, each pushed out or in by the seed, so the loop is different every time.
			Bumps = new[]
			{
				new Shape.Bump { At = 2050.0, Width = 420.0, Height = 130.0, From = 0, Flip = true },
				new Shape.Bump { At = 2500.0, Width = 380.0, Height = 140.0, From = 0, Flip = true },
				new Shape.Bump { At = 2950.0, Width = 360.0, Height = 120.0, From = 0, Flip = true }
			},
			HairpinRadius = 118.0,
			Taper = 0.5,
			TaperLow = 0.85,
			TaperSpread = 0.3,
			FlareRadius = 250.0,
Neck = 600.0,
SpinnerAlong = 1500f,
			SpinnerLateral = 45f,
			MinRadius = 30.0,
Separation = 120.0,
Yard = true,
			Markers = true,
			SignGroup = "Decor|Curve Signs",
			FireGroups = new[] { "Decor|Tomberon", "Decor|GarbageInFlames" },
			EdgeGrass = true,
Roughness = 14f,
			RoughWave = 420f,
SpanX = 2000.0,
			SpanZ = 1300.0,
Expand = 1.8f,
			FlatBase = true,
			TerrainPath = "Terrain",
			HideAll = new[] { "Hazzards", "Decor|Start/Finish_Line", "Decor|Curve Signs" },
			HideRoots = new[] { "ResetZones" },
			InWay = new[] { "Decor" },
			Avoid = new[] { "Decor|Camp", "Decor|WTOWER3" },
			RoadLayer = "AsphaltRoadLayer",
			Transplants = new[]
			{
					// The factory hall (the giant spinner stands on the far side of the turn from it) and the second set of three hammers, as one
					// piece round the lap's first U-turn, as the old road turns through them.
					new Transplant { Name = "factory hall", Path = "Decor|Factory",Also = new[] { "Hazzards|Hammers|Set2", "Hazzards|FactoryAssembly" }, Count = 1, OnRoad = true, Spacing = 90, Turn = true },
				// The other set of three hammers, in a row on some other straight.
				new Transplant { Name = "hammer series", Path = "Hazzards|Hammers|Set1", Count = 1, OnRoad = true, Spacing = 60 },

			}
		};

		/// <summary>
		/// A flat city with a skyline ring 1800 across. The road is a mesh (TrackFlat) with barrier walls, a lane marking mesh and a train
		/// on rails that all follow the old road; they go. The lap is kept inside the skyline ring. It is a tight, twisting lap lined by a
		/// continuous wall and city blocks copied from the scene's own buildings.
		/// </summary>
		public static readonly MapProfile Tokyo = new MapProfile
		{
			Key = "Tokyo",
			Display = "Procedural Tokyo",
			MapName = "ProcTokyo",
			SourceMap = "TokyoNeon",
			SourcePrefix = "Tokyo",
			TrackId = 25,
			Lap = 2700.0,
			SourceLap = 2867f,
			Half = 20f,
			StraightMin = 320.0,
			StraightMax = 560.0,
			SpanX = 1300.0,
			SpanZ = 800.0,
			Wobble = 0.008,
			Harmonics = 8,
			AmpCap = 300.0,
HairpinRadius = 36.0,
			Taper = 0.22,
			MinRadius = 30.0,
			Separation = 90.0,
			TerrainPath = "Terrain",
			HideAll = new[] { "TrackArena5|TrackFlat", "TrackArena5|Start/Finish_Line", "TrackArena5|Decorations", "LaneDelimiter", "TrainAssembly", "Decoration|Barriers" },
			InWay = new[] { "Decoration" },
			// TrackArena5 also holds CityLine, the skyline ring: it must stay, so only its Buildings are looked at.
			InWayItems = new[] { "TrackArena5|Buildings" },
			Avoid = new[] { "Decoration|Buildings" },
			RingX = -29.5f,
			RingZ = 184f,
			RingR = 800f,
			RoadFrom = "TrackArena5|TrackFlat|Cube",
			RoadColor = new Color(0.12f, 0.12f, 0.14f),
			RoadTile = 40f,
			Walls = true,
			BuildingGroup = "Decoration|Buildings"
		};

		public static readonly MapProfile[] All = { Forest, Mountain, Factory, Tokyo };

		/// <summary>The profile whose map entry this is, or null.</summary>
		public static MapProfile For(MapDefinition map)
		{
			if (map == null)
			{
				return null;
			}
			foreach (MapProfile p in All)
			{
				if (p.Map == map)
				{
					return p;
				}
			}
			return null;
		}
	}
}
