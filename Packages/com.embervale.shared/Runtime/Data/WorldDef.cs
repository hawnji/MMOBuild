using System;
using System.Collections.Generic;

namespace Embervale.Shared.Data
{
    /// <summary>Tile types painted by Tools/Content/generate_world.py.</summary>
    public enum TileType : byte { Grass, Path, Water, Sand, Rock, Marsh, Stone, Forest, Snow, DeepWater }

    public sealed class EncodedGrid
    {
        public string Encoding;
        public string Data;
        public int Resolution;
        public int Width;
        public int Height;
        public float MaxHeight;
    }

    public sealed class RegionDef
    {
        public string Id;
        public string Name;
        public int X0, Z0, X1, Z1;
        public string Music;
        public string Fog;
        public string Ambient;

        public bool Contains(float x, float z) => x >= X0 && x < X1 && z >= Z0 && z < Z1;
    }

    public sealed class LocationDef
    {
        public float X, Z;
        public float Radius;
    }

    public sealed class BuildingPlacement
    {
        public int Id;
        public string Kind;
        public string Style;
        public int X, Z, W, D;
        public string Door;
        public int DoorId;
        public string Name;
        public string Roof;
        public float Height;
    }

    public sealed class DoorPlacement
    {
        public int Id;
        public int X, Z;
        public string Side;
        public int Building;
        public bool Open;
    }

    public sealed class StationPlacement
    {
        public int Id;
        public string Type;
        public float X, Z;
        public string Model;
        public float Rot;
    }

    public sealed class NodePlacement
    {
        public int Id;
        public string Def;
        public float X, Z;
    }

    public sealed class SpawnPlacement
    {
        public int Id;
        public string Monster;
        public float X, Z;
        public int Count;
        public float Radius;
    }

    public sealed class NpcPlacement
    {
        public int Id;
        public string Def;
        public float X, Z;
        public float Rot;
    }

    public sealed class PatchPlacement
    {
        public int Id;
        public float X, Z;
        public int W, D;
    }

    public sealed class ObstaclePlacement
    {
        public int Id;
        public string Def;
        public float X, Z;
        public int Order;
        public float EndX, EndZ;
    }

    public sealed class StallPlacement
    {
        public int Id;
        public string Def;
        public float X, Z;
    }

    public sealed class PropPlacement
    {
        public string Model;
        public float X, Z;
        public float Rot;
        public float Scale = 1f;
        public float W, D, Length;
        public float X0, Z0, X1, Z1;
    }

    public sealed class LakeDef
    {
        public string Id;
        public float X, Z, Radius;
    }

    public sealed class PointDef
    {
        public float X, Z;
    }

    public sealed class WorldDef
    {
        public int Version;
        public int Seed;
        public int Width;
        public int Height;
        public EncodedGrid Heightmap;
        public EncodedGrid Tiles;
        public EncodedGrid Blocked;
        public List<RegionDef> Regions = new List<RegionDef>();
        public Dictionary<string, LocationDef> Locations = new Dictionary<string, LocationDef>();
        public List<BuildingPlacement> Buildings = new List<BuildingPlacement>();
        public List<DoorPlacement> Doors = new List<DoorPlacement>();
        public List<StationPlacement> Stations = new List<StationPlacement>();
        public List<NodePlacement> Nodes = new List<NodePlacement>();
        public List<SpawnPlacement> Spawns = new List<SpawnPlacement>();
        public List<NpcPlacement> Npcs = new List<NpcPlacement>();
        public List<PatchPlacement> FarmPatches = new List<PatchPlacement>();
        public List<ObstaclePlacement> Agility = new List<ObstaclePlacement>();
        public List<StallPlacement> Stalls = new List<StallPlacement>();
        public List<PropPlacement> Props = new List<PropPlacement>();
        public List<PropPlacement> Decor = new List<PropPlacement>();
        public List<LakeDef> Lakes = new List<LakeDef>();
        public List<PointDef> River = new List<PointDef>();

        public byte[] DecodeTiles() => Convert.FromBase64String(Tiles.Data);
        public byte[] DecodeBlocked() => Convert.FromBase64String(Blocked.Data);

        /// <summary>Heights in metres, row-major [z * width + x] at Heightmap.Resolution spacing.</summary>
        public float[] DecodeHeights()
        {
            var raw = Convert.FromBase64String(Heightmap.Data);
            var result = new float[raw.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int v = raw[i * 2] | (raw[i * 2 + 1] << 8);
                result[i] = v / 65535f * Heightmap.MaxHeight;
            }
            return result;
        }

        public RegionDef RegionAt(float x, float z)
        {
            foreach (var r in Regions) if (r.Contains(x, z)) return r;
            return Regions.Count > 0 ? Regions[Regions.Count - 1] : null;
        }
    }
}
