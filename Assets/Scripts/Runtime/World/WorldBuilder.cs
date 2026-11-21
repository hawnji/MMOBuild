using System.Collections.Generic;
using Embervale.Shared.Data;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Heights, tile types and collision of the shared world (world.json). Terrain vertex heights are the
    /// heightmap bilinearly resampled to 1 m, with river/lake beds carved so water reads clearly.
    /// </summary>
    public sealed class WorldTerrain
    {
        public readonly int Width, Height;
        public readonly float[] Corner;   // (Width+1)*(Height+1) carved heights at tile corners
        public readonly float[] WaterLevel; // same layout, surface height for water tiles
        public readonly byte[] Tiles;
        public readonly byte[] Blocked;

        public WorldTerrain(WorldDef w)
        {
            Width = w.Width;
            Height = w.Height;
            Tiles = w.DecodeTiles();
            Blocked = w.DecodeBlocked();
            var hm = w.DecodeHeights();
            int res = Mathf.Max(1, w.Heightmap.Resolution);
            int hw = w.Heightmap.Width, hh = w.Heightmap.Height;
            Corner = new float[(Width + 1) * (Height + 1)];
            WaterLevel = new float[(Width + 1) * (Height + 1)];
            for (int z = 0; z <= Height; z++)
            for (int x = 0; x <= Width; x++)
            {
                float fx = (float)x / res, fz = (float)z / res;
                int x0 = Mathf.Clamp((int)fx, 0, hw - 1), z0 = Mathf.Clamp((int)fz, 0, hh - 1);
                int x1 = Mathf.Min(x0 + 1, hw - 1), z1 = Mathf.Min(z0 + 1, hh - 1);
                float tx = fx - x0, tz = fz - z0;
                float h = Mathf.Lerp(Mathf.Lerp(hm[z0 * hw + x0], hm[z0 * hw + x1], tx), Mathf.Lerp(hm[z1 * hw + x0], hm[z1 * hw + x1], tx), tz);
                int water = 0, deep = 0, n = 0;
                for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    int tx2 = x + dx, tz2 = z + dz;
                    if (tx2 < 0 || tz2 < 0 || tx2 >= Width || tz2 >= Height) continue;
                    n++;
                    var t = (TileType)Tiles[tz2 * Width + tx2];
                    if (t == TileType.Water || t == TileType.DeepWater) water++;
                    if (t == TileType.DeepWater) deep++;
                }
                int i = z * (Width + 1) + x;
                WaterLevel[i] = h - 0.25f;
                Corner[i] = h - (water == 0 ? 0f : water == n ? (deep > 0 ? 1.6f : 0.9f) : 0.45f);
            }
        }

        public TileType TileAt(int x, int z) => x < 0 || z < 0 || x >= Width || z >= Height ? TileType.Rock : (TileType)Tiles[z * Width + x];
        public bool IsBlocked(int x, int z) => x < 0 || z < 0 || x >= Width || z >= Height || Blocked[z * Width + x] != 0;
        public float CornerHeight(int x, int z) => Corner[Mathf.Clamp(z, 0, Height) * (Width + 1) + Mathf.Clamp(x, 0, Width)];

        /// <summary>Ground height at a world position (metres), matching the rendered terrain.</summary>
        public float SampleHeight(float x, float z)
        {
            x = Mathf.Clamp(x, 0, Width - 0.001f);
            z = Mathf.Clamp(z, 0, Height - 0.001f);
            int x0 = (int)x, z0 = (int)z;
            float tx = x - x0, tz = z - z0;
            float a = CornerHeight(x0, z0), b = CornerHeight(x0 + 1, z0), c = CornerHeight(x0, z0 + 1), d = CornerHeight(x0 + 1, z0 + 1);
            // match the triangle split used by the terrain mesh
            return tx + tz <= 1f ? a + (b - a) * tx + (c - a) * tz : d + (c - d) * (1 - tx) + (b - d) * (1 - tz);
        }
    }

    /// <summary>Marks terrain colliders so clicks know they hit the ground.</summary>
    public sealed class GroundSurface : MonoBehaviour { }

    /// <summary>Fades a building's roof while the local player is inside its footprint.</summary>
    public sealed class BuildingView : MonoBehaviour
    {
        public Rect Footprint;
        public Renderer[] Roof = new Renderer[0];
        private bool _hidden;

        public void SetPlayerPosition(Vector3 p)
        {
            bool inside = Settings.ShowRoofs && Footprint.Contains(new Vector2(p.x, p.z));
            if (inside == _hidden) return;
            _hidden = inside;
            foreach (var r in Roof) if (r != null) r.enabled = !inside;
        }
    }

    /// <summary>
    /// Builds every static part of the world on the client: terrain chunks, water, buildings, walls,
    /// cliffs, props and decoration. Dynamic things (nodes, NPCs, monsters, doors, stations...) are
    /// server entities and are spawned by EntityManager instead.
    /// </summary>
    public sealed class WorldBuilder
    {
        public const int ChunkSize = 32;
        public readonly WorldDef Def;
        public readonly WorldTerrain Terrain;
        public readonly Transform Root;
        public readonly List<BuildingView> Buildings = new List<BuildingView>();
        public Texture2D MapTexture { get; private set; }

        private readonly AssetRegistry _assets;
        private readonly HashSet<long> _occupied = new HashSet<long>();

        public WorldBuilder(WorldDef def, AssetRegistry assets, Transform parent)
        {
            Def = def;
            _assets = assets;
            Terrain = new WorldTerrain(def);
            Root = new GameObject("World").transform;
            Root.SetParent(parent, false);
        }

        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

        public void BuildAll()
        {
            BuildTerrain();
            BuildWater();
            BuildBuildings();
            BuildProps();
            BuildWallsAndCliffs();
            BuildDecor();
        }

        // ------------------------------------------------------------------ terrain
        public static Color TileColor(TileType t, float n)
        {
            Color c;
            switch (t)
            {
                case TileType.Grass: c = new Color(0.40f, 0.60f, 0.24f); break;
                case TileType.Path: c = new Color(0.56f, 0.45f, 0.32f); break;
                case TileType.Water: c = new Color(0.34f, 0.40f, 0.30f); break;
                case TileType.DeepWater: c = new Color(0.20f, 0.26f, 0.24f); break;
                case TileType.Sand: c = new Color(0.82f, 0.74f, 0.53f); break;
                case TileType.Rock: c = new Color(0.50f, 0.49f, 0.47f); break;
                case TileType.Marsh: c = new Color(0.33f, 0.40f, 0.24f); break;
                case TileType.Stone: c = new Color(0.62f, 0.60f, 0.56f); break;
                case TileType.Forest: c = new Color(0.27f, 0.46f, 0.19f); break;
                case TileType.Snow: c = new Color(0.92f, 0.93f, 0.96f); break;
                default: c = Color.magenta; break;
            }
            return c * (0.92f + n * 0.16f);
        }

        private Texture2D BuildGroundTexture(int pxPerTile)
        {
            int W = Terrain.Width * pxPerTile, H = Terrain.Height * pxPerTile;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "TerrainColors" };
            var px = new Color32[W * H];
            var rng = new System.Random(Def.Seed);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int tx = x / pxPerTile, tz = y / pxPerTile;
                float n = (float)(Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.7 + rng.NextDouble() * 0.3);
                // stone and path tiles get a cobble/plank pattern
                var t = Terrain.TileAt(tx, tz);
                if (t == TileType.Stone && ((x % pxPerTile == 0) || (y % pxPerTile == 0))) n -= 0.6f;
                px[y * W + x] = TileColor(t, n);
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        public void BuildTerrain()
        {
            var ground = BuildGroundTexture(4);
            MapTexture = BuildMapTexture();
            var mat = _assets.TerrainMaterial != null ? new Material(_assets.TerrainMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.name = "Terrain (runtime)";
            mat.mainTexture = ground;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", ground);
            var terrainRoot = new GameObject("Terrain").transform;
            terrainRoot.SetParent(Root, false);
            for (int cz = 0; cz < Terrain.Height; cz += ChunkSize)
            for (int cx = 0; cx < Terrain.Width; cx += ChunkSize)
            {
                var mesh = BuildChunkMesh(cx, cz, Mathf.Min(ChunkSize, Terrain.Width - cx), Mathf.Min(ChunkSize, Terrain.Height - cz));
                var go = new GameObject("Chunk_" + cx + "_" + cz);
                go.transform.SetParent(terrainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.AddComponent<GroundSurface>();
                go.isStatic = true;
            }
        }

        private Mesh BuildChunkMesh(int x0, int z0, int w, int d)
        {
            var verts = new Vector3[(w + 1) * (d + 1)];
            var uvs = new Vector2[verts.Length];
            for (int z = 0; z <= d; z++)
            for (int x = 0; x <= w; x++)
            {
                int i = z * (w + 1) + x;
                verts[i] = new Vector3(x0 + x, Terrain.CornerHeight(x0 + x, z0 + z), z0 + z);
                uvs[i] = new Vector2((x0 + x) / (float)Terrain.Width, (z0 + z) / (float)Terrain.Height);
            }
            var tris = new int[w * d * 6];
            int k = 0;
            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int a = z * (w + 1) + x, b = a + 1, c = a + (w + 1), e = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = e;
            }
            var mesh = new Mesh { name = "TerrainChunk" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Top-down map (1 px per tile) used by the minimap and world map.</summary>
        private Texture2D BuildMapTexture()
        {
            var tex = new Texture2D(Terrain.Width, Terrain.Height, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "WorldMap" };
            var px = new Color32[Terrain.Width * Terrain.Height];
            for (int z = 0; z < Terrain.Height; z++)
            for (int x = 0; x < Terrain.Width; x++)
            {
                var t = Terrain.TileAt(x, z);
                Color c = t == TileType.Water ? new Color(0.25f, 0.45f, 0.70f) : t == TileType.DeepWater ? new Color(0.15f, 0.32f, 0.58f) : TileColor(t, 0.5f);
                float shade = Mathf.Clamp((Terrain.CornerHeight(x + 1, z + 1) - Terrain.CornerHeight(x, z)) * 0.25f, -0.15f, 0.15f);
                c = c * (1f + shade);
                px[z * Terrain.Width + x] = c;
            }
            foreach (var b in Def.Buildings)
                for (int z = b.Z; z < b.Z + b.D; z++)
                for (int x = b.X; x < b.X + b.W; x++)
                {
                    bool edge = x == b.X || z == b.Z || x == b.X + b.W - 1 || z == b.Z + b.D - 1;
                    px[z * Terrain.Width + x] = edge ? new Color32(120, 70, 50, 255) : new Color32(170, 150, 130, 255);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ------------------------------------------------------------------ water
        public void BuildWater()
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int z = 0; z < Terrain.Height; z++)
            for (int x = 0; x < Terrain.Width; x++)
            {
                var t = Terrain.TileAt(x, z);
                if (t != TileType.Water && t != TileType.DeepWater) continue;
                int b = verts.Count;
                // extend slightly into neighbouring tiles so shores have no gaps
                for (int dz = 0; dz <= 1; dz++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    float h = Terrain.WaterLevel[(z + dz) * (Terrain.Width + 1) + x + dx];
                    verts.Add(new Vector3(x + dx * 1.0f + (dx == 0 ? -0.15f : 0.15f), h, z + dz + (dz == 0 ? -0.15f : 0.15f)));
                    uvs.Add(new Vector2((x + dx) * 0.25f, (z + dz) * 0.25f));
                }
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
            if (verts.Count == 0) return;
            var mesh = new Mesh { name = "Water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var go = new GameObject("Water");
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _assets.WaterMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<WaterAnimator>();
        }

        // ------------------------------------------------------------------ buildings
        public void BuildBuildings()
        {
            var parent = new GameObject("Buildings").transform;
            parent.SetParent(Root, false);
            foreach (var b in Def.Buildings)
            {
                var go = _assets.Spawn("building_" + b.Id, parent);
                go.name = b.Name + " (" + b.Id + ")";
                float cx = b.X + b.W * 0.5f, cz = b.Z + b.D * 0.5f;
                go.transform.position = new Vector3(cx, b.Height, cz);
                Tintable.On(go);
                var view = go.AddComponent<BuildingView>();
                view.Footprint = new Rect(b.X + 0.5f, b.Z + 0.5f, b.W - 1f, b.D - 1f);
                var roof = new List<Renderer>();
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    if (r.name.StartsWith("Roof")) roof.Add(r);
                view.Roof = roof.ToArray();
                Buildings.Add(view);
                for (int z = b.Z; z < b.Z + b.D; z++)
                for (int x = b.X; x < b.X + b.W; x++) _occupied.Add(Key(x, z));
            }
        }

        // ------------------------------------------------------------------ props / decor
        private static readonly HashSet<string> ProceduralProps = new HashSet<string> { "city_wall", "cave_shell", "ruin_complex" };

        public void BuildProps()
        {
            var parent = new GameObject("Props").transform;
            parent.SetParent(Root, false);
            foreach (var p in Def.Props)
            {
                if (ProceduralProps.Contains(p.Model)) continue;
                Place(p, parent);
                _occupied.Add(Key((int)p.X, (int)p.Z));
            }
            StaticBatchingUtility.Combine(parent.gameObject);
        }

        public void BuildDecor()
        {
            var parent = new GameObject("Decor").transform;
            parent.SetParent(Root, false);
            foreach (var p in Def.Decor) Place(p, parent);
            StaticBatchingUtility.Combine(parent.gameObject);
        }

        private GameObject Place(PropPlacement p, Transform parent)
        {
            var go = _assets.Spawn(p.Model, parent);
            go.transform.position = new Vector3(p.X, Terrain.SampleHeight(p.X, p.Z), p.Z);
            go.transform.rotation = Quaternion.Euler(0, p.Rot, 0);
            go.transform.localScale = Vector3.one * (p.Scale <= 0 ? 1f : p.Scale);
            Tintable.On(go);
            go.isStatic = true;
            return go;
        }

        /// <summary>City walls, ruin walls and cliffs are generated from the blocked rock tiles.</summary>
        public void BuildWallsAndCliffs()
        {
            var parent = new GameObject("Walls").transform;
            parent.SetParent(Root, false);
            PropPlacement city = null, ruin = null, cave = null;
            foreach (var p in Def.Props)
            {
                if (p.Model == "city_wall") city = p;
                else if (p.Model == "ruin_complex") ruin = p;
                else if (p.Model == "cave_shell") cave = p;
            }
            foreach (var s in Def.Stations) _occupied.Add(Key((int)s.X, (int)s.Z));
            foreach (var n in Def.Nodes) _occupied.Add(Key((int)n.X, (int)n.Z));
            var rng = new System.Random(Def.Seed + 7);
            for (int z = 1; z < Terrain.Height - 1; z++)
            for (int x = 1; x < Terrain.Width - 1; x++)
            {
                if (!Terrain.IsBlocked(x, z) || Terrain.TileAt(x, z) != TileType.Rock || _occupied.Contains(Key(x, z))) continue;
                string model;
                float rot;
                bool alongX = Terrain.IsBlocked(x - 1, z) || Terrain.IsBlocked(x + 1, z);
                if (city != null && Mathf.Abs(x + 0.5f - city.X) <= city.W * 0.5f + 1 && Mathf.Abs(z + 0.5f - city.Z) <= city.D * 0.5f + 1)
                {
                    model = "city_wall_stone";
                    rot = alongX ? 0 : 90;
                }
                else if (ruin != null && x >= ruin.X0 && x <= ruin.X1 && z >= ruin.Z0 && z <= ruin.Z1)
                {
                    model = "ruin_wall_stone";
                    rot = alongX ? 0 : 90;
                }
                else
                {
                    model = "cave_rock_stone";
                    rot = (float)rng.NextDouble() * 360f;
                }
                var go = _assets.Spawn(model, parent);
                go.transform.position = new Vector3(x + 0.5f, Terrain.SampleHeight(x + 0.5f, z + 0.5f), z + 0.5f);
                go.transform.rotation = Quaternion.Euler(0, rot, 0);
                if (model == "cave_rock_stone") go.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.6f);
                Tintable.On(go);
                go.isStatic = true;
            }
            // the cave shell gets a ring of tall rocks so it reads as a cavern from the camera
            if (cave != null)
            {
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI * 2 / 24;
                    float px = cave.X + Mathf.Cos(a) * 12.5f, pz = cave.Z + Mathf.Sin(a) * 12.5f;
                    var go = _assets.Spawn("rock_large", parent);
                    go.transform.position = new Vector3(px, Terrain.SampleHeight(px, pz), pz);
                    go.transform.rotation = Quaternion.Euler(0, i * 37, 0);
                    go.transform.localScale = Vector3.one * 1.6f;
                    Tintable.On(go);
                }
            }
            StaticBatchingUtility.Combine(parent.gameObject);
        }

        public void UpdateRoofs(Vector3 playerPos)
        {
            foreach (var b in Buildings) b.SetPlayerPosition(playerPos);
        }
    }

    /// <summary>Scrolls the water texture and gently bobs the surface colour.</summary>
    public sealed class WaterAnimator : MonoBehaviour
    {
        private Material _mat;
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        private void Start() { _mat = GetComponent<Renderer>().material; }

        private void Update()
        {
            if (_mat == null) return;
            float t = Time.time * 0.02f;
            if (_mat.HasProperty(BaseMapST)) _mat.SetVector(BaseMapST, new Vector4(1, 1, t, t * 0.6f));
            else _mat.mainTextureOffset = new Vector2(t, t * 0.6f);
        }
    }
}
