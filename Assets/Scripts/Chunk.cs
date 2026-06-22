using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class Chunk
{

    public ChunkCoord coord;

    GameObject   chunkObject;
    MeshRenderer meshRenderer;
    MeshFilter   meshFilter;

    // Submesh 0 — opaque blocks
    List<Vector3> vertices  = new List<Vector3>();
    List<int>     triangles = new List<int>();
    List<Vector2> uvs       = new List<Vector2>();
    List<Vector3> normals   = new List<Vector3>();

    // Submesh 1 — liquid blocks (water, lava, etc.)
    List<Vector3> liqVerts  = new List<Vector3>();
    List<int>     liqTris   = new List<int>();
    List<Vector2> liqUvs    = new List<Vector2>();
    List<Vector3> liqNorms  = new List<Vector3>();
    List<Color>   liqColors = new List<Color>(); // R = wave weight (0 = solid neighbor, 1 = open)

    // Submesh 2 — other transparent blocks (glass, leaves, etc.)
    List<Vector3> transVerts  = new List<Vector3>();
    List<int>     transTris   = new List<int>();
    List<Vector2> transUvs    = new List<Vector2>();
    List<Vector3> transNorms  = new List<Vector3>();

    // Vertex color alpha = FaceBrightness × skyLight (0-15) × AO (per corner).
    // Liquid R channel carries wave weight; opaque/trans R is unused (set to 0).
    List<Color> opaqueColors = new List<Color>();
    List<Color> transColors  = new List<Color>();

    // h: 0=Back, 1=Front, 2=Top, 3=Bottom, 4=Left, 5=Right
    static readonly float[] FaceBrightness = { 0.8f, 0.8f, 1.0f, 0.5f, 0.8f, 0.8f };

    // Per-voxel sky light: 15 = unobstructed sky access, 0 = underground/occluded.
    // Built once before mesh generation, nulled after upload.
    byte[,,] skyLightMap;

    // AO side offsets: aoOffsets[face, vertex, 0/1] = the two tangent directions to check.
    // Corner block = side0 + side1. All offsets are relative to (pos + faceNormal).
    // Derived from voxelTris vertex positions — each vertex sits at a specific face corner.
    static readonly Vector3Int[,,] aoOffsets = new Vector3Int[6, 4, 2]
    {
        { // Face 0: Back  (Z-) — verts {0,3,1,2} → (0,0,0),(0,1,0),(1,0,0),(1,1,0)
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0,-1, 0) },
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0, 1, 0) },
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0,-1, 0) },
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0, 1, 0) },
        },
        { // Face 1: Front (Z+) — verts {5,6,4,7} → (1,0,1),(1,1,1),(0,0,1),(0,1,1)
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0,-1, 0) },
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0, 1, 0) },
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0,-1, 0) },
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0, 1, 0) },
        },
        { // Face 2: Top   (Y+) — verts {3,7,2,6} → (0,1,0),(0,1,1),(1,1,0),(1,1,1)
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0, 0, 1) },
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0, 0, 1) },
        },
        { // Face 3: Bottom(Y-) — verts {1,5,0,4} → (1,0,0),(1,0,1),(0,0,0),(0,0,1)
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int( 1, 0, 0), new Vector3Int( 0, 0, 1) },
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int(-1, 0, 0), new Vector3Int( 0, 0, 1) },
        },
        { // Face 4: Left  (X-) — verts {4,7,0,3} → (0,0,1),(0,1,1),(0,0,0),(0,1,0)
            { new Vector3Int( 0,-1, 0), new Vector3Int( 0, 0, 1) },
            { new Vector3Int( 0, 1, 0), new Vector3Int( 0, 0, 1) },
            { new Vector3Int( 0,-1, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int( 0, 1, 0), new Vector3Int( 0, 0,-1) },
        },
        { // Face 5: Right (X+) — verts {1,2,5,6} → (1,0,0),(1,1,0),(1,0,1),(1,1,1)
            { new Vector3Int( 0,-1, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int( 0, 1, 0), new Vector3Int( 0, 0,-1) },
            { new Vector3Int( 0,-1, 0), new Vector3Int( 0, 0, 1) },
            { new Vector3Int( 0, 1, 0), new Vector3Int( 0, 0, 1) },
        },
    };

    public short[,,] voxelMap = new short[VoxelData.chunkWidth, VoxelData.chunkHeight, VoxelData.chunkWidth];

    [ThreadStatic] static Queue<Vector3Int> _bfsQueue;
    static Queue<Vector3Int> BfsQueue { get { return _bfsQueue ?? (_bfsQueue = new Queue<Vector3Int>(4096)); } }

    // Interleaved vertex layout written into native mesh buffers on the background thread.
    [StructLayout(LayoutKind.Sequential)]
    struct MeshVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Color   color;
        public Vector2 uv;
    }


    Vector3 chunkPosition;

    public volatile bool isDataReady   = false;
    public volatile bool isMeshPending = false;
    public          bool isFirstBuild  = true;

    World worldObj;

    public Chunk(World _worldObj, ChunkCoord _coord)
    {
        worldObj = _worldObj;
        coord    = _coord;

        chunkObject = new GameObject { name = "Chunk " + coord.x + ", " + coord.z };
        meshFilter   = chunkObject.AddComponent<MeshFilter>();
        meshRenderer = chunkObject.AddComponent<MeshRenderer>();

        meshRenderer.materials = new Material[]
        {
            worldObj.atlasMaterial,
            worldObj.liquidMaterial,
            worldObj.transparentAtlasMaterial
        };

        chunkObject.transform.parent   = worldObj.transform;
        chunkPosition                  = new Vector3(coord.x * VoxelData.chunkWidth, 0, coord.z * VoxelData.chunkWidth);
        chunkObject.transform.position = chunkPosition;
    }

    public void BuildData()
    {
        PopulateVoxelMap();

        // Ring 2: isolated sky light only — these feed ring-1 seeding so that
        // light can travel two chunk-boundaries deep (e.g. large caves).
        PreWarmNeighborIsolated(coord.x - 2, coord.z    );
        PreWarmNeighborIsolated(coord.x + 2, coord.z    );
        PreWarmNeighborIsolated(coord.x,     coord.z - 2);
        PreWarmNeighborIsolated(coord.x,     coord.z + 2);
        PreWarmNeighborIsolated(coord.x - 1, coord.z - 1);
        PreWarmNeighborIsolated(coord.x - 1, coord.z + 1);
        PreWarmNeighborIsolated(coord.x + 1, coord.z - 1);
        PreWarmNeighborIsolated(coord.x + 1, coord.z + 1);

        // Ring 1: isolated BFS + cross-chunk seeding from ring 2.
        PreWarmNeighborFull(coord.x - 1, coord.z    );
        PreWarmNeighborFull(coord.x + 1, coord.z    );
        PreWarmNeighborFull(coord.x,     coord.z - 1);
        PreWarmNeighborFull(coord.x,     coord.z + 1);

        ComputeSkyLight();
        CreateChunkData();
        PrepareMeshData();
        isMeshPending = true;
        isDataReady = true;
    }

    public void ApplyMesh()
    {
        isMeshPending = false;
        CreateMesh();
    }

    // Called when a newly-loaded neighbor may have provided better border sky light values.
    // Re-computes sky light from the updated cache then rebuilds vertex data.
    // voxelMap is untouched so terrain data stays valid throughout.
    public void RebuildMeshData()
    {
        ComputeSkyLight();
        vertices.Clear();    triangles.Clear();  uvs.Clear();      normals.Clear();
        liqVerts.Clear();    liqTris.Clear();    liqUvs.Clear();   liqNorms.Clear();
        transVerts.Clear();  transTris.Clear();  transUvs.Clear(); transNorms.Clear();
        opaqueColors.Clear(); liqColors.Clear(); transColors.Clear();
        CreateChunkData();
        PrepareMeshData();
    }

    public void PopulateVoxelMap()
    {
        var src = worldObj.GetChunkRawData(coord.x, coord.z);
        Array.Copy(src, voxelMap, src.Length);
    }

    // Ring-2 pre-warm: isolated BFS only, no seeding. Feeds ring-1 seeding.
    void PreWarmNeighborIsolated(int nx, int nz)
    {
        var key = new Vector2Int(nx, nz);
        if (worldObj.skyLightCache.ContainsKey(key)) return;
        worldObj.skyLightCache.TryAdd(key,
            ComputeSkyLightStatic(worldObj.GetChunkRawData(nx, nz), worldObj.blockTypes));
    }

    // Ring-1 pre-warm: isolated BFS + cross-chunk seeding from whatever's in cache (ring 2).
    void PreWarmNeighborFull(int nx, int nz)
    {
        var key = new Vector2Int(nx, nz);
        if (worldObj.skyLightCache.ContainsKey(key)) return;
        worldObj.skyLightCache.TryAdd(key,
            ComputeSkyLightWithSeeding(worldObj.GetChunkRawData(nx, nz), worldObj.blockTypes, nx, nz));
    }

    void ComputeSkyLight()
    {
        skyLightMap = ComputeSkyLightWithSeeding(voxelMap, worldObj.blockTypes, coord.x, coord.z);
        worldObj.skyLightCache[new Vector2Int(coord.x, coord.z)] = skyLightMap;
    }

    // Isolated BFS then a cross-chunk seeding pass from cached neighbors.
    // Shared by ring-1 pre-warm and self so both get the same two-hop propagation.
    byte[,,] ComputeSkyLightWithSeeding(short[,,] data, BlockTypes[] blockTypes, int cx, int cz)
    {
        var sl    = ComputeSkyLightStatic(data, blockTypes);
        int w     = (int)VoxelData.chunkWidth;
        int h     = (int)VoxelData.chunkHeight;
        var queue = BfsQueue;
        queue.Clear();

        SeedBorder(sl, data, blockTypes, new Vector2Int(cx - 1, cz), w - 1, true,  0,     queue);
        SeedBorder(sl, data, blockTypes, new Vector2Int(cx + 1, cz), 0,     true,  w - 1, queue);
        SeedBorder(sl, data, blockTypes, new Vector2Int(cx, cz - 1), w - 1, false, 0,     queue);
        SeedBorder(sl, data, blockTypes, new Vector2Int(cx, cz + 1), 0,     false, w - 1, queue);

        while (queue.Count > 0)
        {
            var  p     = queue.Dequeue();
            byte level = sl[p.x, p.y, p.z];
            if (level <= 1) continue;
            byte next = (byte)(level - 1);
            for (int f = 0; f < 6; f++)
            {
                int nx2 = p.x + (int)VoxelData.faceChecks[f].x;
                int ny2 = p.y + (int)VoxelData.faceChecks[f].y;
                int nz2 = p.z + (int)VoxelData.faceChecks[f].z;
                if (nx2 < 0 || nx2 >= w || ny2 < 0 || ny2 >= h || nz2 < 0 || nz2 >= w) continue;
                if (sl[nx2, ny2, nz2] >= next) continue;
                short nid    = data[nx2, ny2, nz2];
                bool nopaque = nid != 0 && !blockTypes[nid].isTransparent && !blockTypes[nid].isLiquid;
                if (nopaque) continue;
                sl[nx2, ny2, nz2] = next;
                queue.Enqueue(new Vector3Int(nx2, ny2, nz2));
            }
        }

        return sl;
    }

    // Injects light from one border of a cached neighbor into sl, queuing updated cells.
    // neighborBorderIdx: x (or z) index in the neighbor that directly faces us.
    // isXBorder: true = W/E (X-axis), false = S/N (Z-axis).
    // ourBorderIdx: our x (or z) index that receives the injected light.
    void SeedBorder(byte[,,] sl, short[,,] data, BlockTypes[] blockTypes,
                    Vector2Int neighborKey, int neighborBorderIdx,
                    bool isXBorder, int ourBorderIdx, Queue<Vector3Int> queue)
    {
        if (!worldObj.skyLightCache.TryGetValue(neighborKey, out byte[,,] nsl)) return;
        int w = (int)VoxelData.chunkWidth;
        int h = (int)VoxelData.chunkHeight;
        for (int a = 0; a < w; a++)
        for (int y = 0; y < h; y++)
        {
            byte nLight = isXBorder ? nsl[neighborBorderIdx, y, a] : nsl[a, y, neighborBorderIdx];
            if (nLight <= 1) continue;
            byte inject = (byte)(nLight - 1);
            int ox = isXBorder ? ourBorderIdx : a;
            int oz = isXBorder ? a : ourBorderIdx;
            if (sl[ox, y, oz] >= inject) continue;
            short bid    = data[ox, y, oz];
            bool  opaque = bid != 0 && !blockTypes[bid].isTransparent && !blockTypes[bid].isLiquid;
            if (opaque) continue;
            sl[ox, y, oz] = inject;
            queue.Enqueue(new Vector3Int(ox, y, oz));
        }
    }

    // Column-scan + BFS sky light for any voxel data array.
    // Extracted so PreWarmNeighborSkyLight can run it without a Chunk instance.
    static byte[,,] ComputeSkyLightStatic(short[,,] data, BlockTypes[] blockTypes)
    {
        int w = (int)VoxelData.chunkWidth;
        int h = (int)VoxelData.chunkHeight;
        var sl    = new byte[w, h, w];
        var queue = BfsQueue;
        queue.Clear();

        for (int x = 0; x < w; x++)
        for (int z = 0; z < w; z++)
        {
            for (int y = h - 1; y >= 0; y--)
            {
                short id   = data[x, y, z];
                bool opaque = id != 0
                    && !blockTypes[id].isTransparent
                    && !blockTypes[id].isLiquid;
                if (opaque) break;
                sl[x, y, z] = 15;
                queue.Enqueue(new Vector3Int(x, y, z));
            }
        }

        while (queue.Count > 0)
        {
            Vector3Int p     = queue.Dequeue();
            byte       level = sl[p.x, p.y, p.z];
            if (level <= 1) continue;
            byte next = (byte)(level - 1);

            for (int f = 0; f < 6; f++)
            {
                int nx = p.x + (int)VoxelData.faceChecks[f].x;
                int ny = p.y + (int)VoxelData.faceChecks[f].y;
                int nz = p.z + (int)VoxelData.faceChecks[f].z;

                if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= w) continue;
                if (sl[nx, ny, nz] >= next) continue;

                short nid    = data[nx, ny, nz];
                bool nopaque = nid != 0
                    && !blockTypes[nid].isTransparent
                    && !blockTypes[nid].isLiquid;
                if (nopaque) continue;

                sl[nx, ny, nz] = next;
                queue.Enqueue(new Vector3Int(nx, ny, nz));
            }
        }

        return sl;
    }

    // Returns AO level 0-3 for a specific vertex corner of a face.
    // 0 = no occlusion, 3 = two solid side neighbours (darkest corner).
    int GetAO(Vector3 pos, int face, int vertex)
    {
        Vector3 normal = VoxelData.faceChecks[face];
        Vector3Int s1 = aoOffsets[face, vertex, 0];
        Vector3Int s2 = aoOffsets[face, vertex, 1];

        bool side1  = CheckVoxel(pos + normal + new Vector3(s1.x, s1.y, s1.z));
        bool side2  = CheckVoxel(pos + normal + new Vector3(s2.x, s2.y, s2.z));
        bool corner = CheckVoxel(pos + normal + new Vector3(s1.x + s2.x, s1.y + s2.y, s1.z + s2.z));

        if (side1 && side2) return 3;
        return (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
    }

    // Maps AO level 0-3 to a brightness multiplier (1.0 = unoccluded, 0.6 = fully occluded).
    // 0.6 gives ~49% final brightness at max AO through the shade formula, matching
    // Minecraft's ~50% darkening. 0.35 was too aggressive (~31%), making edges look
    // like hard shadows rather than subtle contact darkening.
    static float AoFactor(int ao) => Mathf.Lerp(1.0f, 0.35f, ao / 3.0f);

    public void CreateChunkData()
    {
        const int sectionHeight = 16;
        int sectionCount = (int)VoxelData.chunkHeight / sectionHeight;
        var nonEmpty = new bool[sectionCount];

        for (int s = 0; s < sectionCount; s++)
        {
            int yBase = s * sectionHeight;
            for (int x = 0; x < (int)VoxelData.chunkWidth && !nonEmpty[s]; x++)
            for (int y = yBase; y < yBase + sectionHeight && !nonEmpty[s]; y++)
            for (int z = 0; z < (int)VoxelData.chunkWidth && !nonEmpty[s]; z++)
                if (voxelMap[x, y, z] != 0) nonEmpty[s] = true;
        }

        for (int s = 0; s < sectionCount; s++)
        {
            if (!nonEmpty[s]) continue;
            int yBase = s * sectionHeight;
            for (int x = 0; x < VoxelData.chunkWidth; x++)
            for (int y = yBase; y < yBase + sectionHeight; y++)
            for (int z = 0; z < VoxelData.chunkWidth; z++)
            {
                short id = voxelMap[x, y, z];
                if (id == 0) continue;
                if (id >= worldObj.blockTypes.Length)
                {
                    Debug.LogError($"Block ID {id} at ({x},{y},{z}) exceeds blockTypes array length {worldObj.blockTypes.Length}");
                    continue;
                }

                var bt  = worldObj.blockTypes[id];
                var pos = new Vector3(x, y, z);

                if (bt.isLiquid)
                    AddVoxelDataToChunk(pos, 1);
                else if (bt.isTransparent)
                    AddVoxelDataToChunk(pos, 2);
                else if (bt.isSolid)
                    AddVoxelDataToChunk(pos, 0);
            }
        }
    }

    bool IsVoxelInChunk(int x, int y, int z)
    {
        return x >= 0 && x < VoxelData.chunkWidth &&
               y >= 0 && y < VoxelData.chunkHeight &&
               z >= 0 && z < VoxelData.chunkWidth;
    }

    public bool IsActive
    {
        get => chunkObject.activeSelf;
        set => chunkObject.SetActive(value);
    }

    public Vector3 position => chunkPosition;

    public bool CheckVoxel(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x);
        int y = Mathf.FloorToInt(pos.y);
        int z = Mathf.FloorToInt(pos.z);

        if (!IsVoxelInChunk(x, y, z))
            return worldObj.blockTypes[worldObj.GetVoxel(pos + chunkPosition)].isSolid;

        return worldObj.blockTypes[voxelMap[x, y, z]].isSolid;
    }

    short GetBlockIDAt(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x);
        int y = Mathf.FloorToInt(pos.y);
        int z = Mathf.FloorToInt(pos.z);

        if (!IsVoxelInChunk(x, y, z))
            return worldObj.GetVoxel(pos + chunkPosition);

        return voxelMap[x, y, z];
    }

    public void AddVoxelDataToChunk(Vector3 pos, int submesh)
    {
        short thisID       = voxelMap[(int)pos.x, (int)pos.y, (int)pos.z];
        bool  isTransparent = submesh > 0;

        // Resolve target lists once — submesh is constant for all faces of this block
        List<Vector3> verts;
        List<int>     tris;
        List<Vector2> uvsTarget;
        List<Vector3> norms;

        switch (submesh)
        {
            case 1:  verts = liqVerts;   tris = liqTris;   uvsTarget = liqUvs;   norms = liqNorms;   break;
            case 2:  verts = transVerts; tris = transTris; uvsTarget = transUvs; norms = transNorms; break;
            default: verts = vertices;   tris = triangles;  uvsTarget = uvs;      norms = normals;    break;
        }

        for (int h = 0; h < 6; h++)
        {
            Vector3 adjacentPos = pos + VoxelData.faceChecks[h];
            short   adjID       = GetBlockIDAt(adjacentPos);
            bool    renderFace  = isTransparent ? adjID != thisID : !CheckVoxel(adjacentPos);
            if (!renderFace) continue;

            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 0]]);
            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 1]]);
            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 2]]);
            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 3]]);

            Vector3 faceNormal = VoxelData.faceChecks[h];
            norms.Add(faceNormal);
            norms.Add(faceNormal);
            norms.Add(faceNormal);
            norms.Add(faceNormal);

            AddTexture(worldObj.blockTypes[thisID].GetTextureID(h), uvsTarget);

            // Sky light of the adjacent (visible-from) block: 15 = open sky, 0 = underground.
            // Using the ADJACENT block (not the solid block itself) gives correct results:
            //   top of surface block → adjacent air above has skyLight=15 → bright
            //   top of block under an overhang → adjacent air above has skyLight=0 → dark (shadowed)
            //   cave ceiling → adjacent cave air has skyLight=0 → dark
            Vector3 aPos = pos + VoxelData.faceChecks[h];
            int ax = (int)aPos.x, ay = (int)aPos.y, az = (int)aPos.z;
            byte adjSkyLight;
            if (ay >= VoxelData.chunkHeight)
                adjSkyLight = 15;  // above chunk top = open sky
            else if (ay < 0)
                adjSkyLight = 0;
            else if (IsVoxelInChunk(ax, ay, az))
                adjSkyLight = skyLightMap[ax, ay, az];
            else
            {
                // Adjacent block is in a neighboring chunk.
                // Primary: read from neighbor's cached skyLightMap (their BFS result).
                // This matches the reference implementation (0xfabian/mc get_neighbor_chunk).
                // Fallback when cache miss: upward column scan on neighbor raw data.
                int nChunkX = coord.x, nChunkZ = coord.z;
                int nLocalX = ax,      nLocalZ = az;

                if      (ax < 0)                               { nChunkX--; nLocalX = ax + (int)VoxelData.chunkWidth; }
                else if (ax >= (int)VoxelData.chunkWidth)      { nChunkX++; nLocalX = ax - (int)VoxelData.chunkWidth; }
                if      (az < 0)                               { nChunkZ--; nLocalZ = az + (int)VoxelData.chunkWidth; }
                else if (az >= (int)VoxelData.chunkWidth)      { nChunkZ++; nLocalZ = az - (int)VoxelData.chunkWidth; }

                var nKey = new Vector2Int(nChunkX, nChunkZ);
                if (worldObj.skyLightCache.TryGetValue(nKey, out byte[,,] nSkyLight))
                {
                    adjSkyLight = nSkyLight[nLocalX, ay, nLocalZ];
                }
                else
                {
                    // Neighbor not computed yet — column scan upward in raw data.
                    short[,,] nData = worldObj.GetChunkRawData(nChunkX, nChunkZ);
                    adjSkyLight = 15;
                    for (int sy = ay + 1; sy < (int)VoxelData.chunkHeight; sy++)
                    {
                        short nid    = nData[nLocalX, sy, nLocalZ];
                        bool nopaque = nid != 0
                            && !worldObj.blockTypes[nid].isTransparent
                            && !worldObj.blockTypes[nid].isLiquid;
                        if (nopaque) { adjSkyLight = 0; break; }
                    }
                }
            }

            // Vertex color channels — read by all three shaders:
            //   Opaque/Transparent:  R=skyLight, G=blockLight(0), B=AO, A=diffuse
            //   Liquid:              R=waveWeight, G=skyLight,    B=AO, A=diffuse
            // The lightmap LUT in the shader maps (blockLight, skyLight) → brightness color.
            float skyNorm = adjSkyLight / 15.0f;
            float diff    = FaceBrightness[h];
            if (submesh == 1)
            {
                if (h == 2) // top face — vertex-centric solid check for wave dampening
                {
                    // Each corner checks 3 surrounding blocks. Adjacent water blocks
                    // sharing a corner always sample the same neighbors — no seam.
                    bool w00 = !CheckVoxel(pos + new Vector3(-1, 0,  0)) && !CheckVoxel(pos + new Vector3( 0, 0, -1)) && !CheckVoxel(pos + new Vector3(-1, 0, -1));
                    bool w01 = !CheckVoxel(pos + new Vector3(-1, 0,  0)) && !CheckVoxel(pos + new Vector3( 0, 0,  1)) && !CheckVoxel(pos + new Vector3(-1, 0,  1));
                    bool w10 = !CheckVoxel(pos + new Vector3( 1, 0,  0)) && !CheckVoxel(pos + new Vector3( 0, 0, -1)) && !CheckVoxel(pos + new Vector3( 1, 0, -1));
                    bool w11 = !CheckVoxel(pos + new Vector3( 1, 0,  0)) && !CheckVoxel(pos + new Vector3( 0, 0,  1)) && !CheckVoxel(pos + new Vector3( 1, 0,  1));
                    liqColors.Add(new Color(w00 ? 1f : 0f, skyNorm, AoFactor(GetAO(pos, h, 0)), diff));
                    liqColors.Add(new Color(w01 ? 1f : 0f, skyNorm, AoFactor(GetAO(pos, h, 1)), diff));
                    liqColors.Add(new Color(w10 ? 1f : 0f, skyNorm, AoFactor(GetAO(pos, h, 2)), diff));
                    liqColors.Add(new Color(w11 ? 1f : 0f, skyNorm, AoFactor(GetAO(pos, h, 3)), diff));
                }
                else
                {
                    liqColors.Add(new Color(0f, skyNorm, AoFactor(GetAO(pos, h, 0)), diff));
                    liqColors.Add(new Color(0f, skyNorm, AoFactor(GetAO(pos, h, 1)), diff));
                    liqColors.Add(new Color(0f, skyNorm, AoFactor(GetAO(pos, h, 2)), diff));
                    liqColors.Add(new Color(0f, skyNorm, AoFactor(GetAO(pos, h, 3)), diff));
                }
            }
            else if (submesh == 0)
            {
                opaqueColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 0)), diff));
                opaqueColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 1)), diff));
                opaqueColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 2)), diff));
                opaqueColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 3)), diff));
            }
            else
            {
                transColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 0)), diff));
                transColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 1)), diff));
                transColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 2)), diff));
                transColors.Add(new Color(skyNorm, 0f, AoFactor(GetAO(pos, h, 3)), diff));
            }

            int idx = verts.Count - 4;
            tris.Add(idx);
            tris.Add(idx + 1);
            tris.Add(idx + 2);
            tris.Add(idx + 2);
            tris.Add(idx + 1);
            tris.Add(idx + 3);
        }
    }

    // Runs on background thread. Merges submesh buffers and pre-offsets indices so
    // CreateMesh() on the main thread only needs to do the native buffer write and API calls.
    void PrepareMeshData()
    {
        int opaqueCount = vertices.Count;

        for (int i = 0; i < liqTris.Count; i++) liqTris[i] += opaqueCount;
        vertices.AddRange(liqVerts); uvs.AddRange(liqUvs); normals.AddRange(liqNorms);
        liqVerts.Clear(); liqUvs.Clear(); liqNorms.Clear();

        int baseAfterLiquid = vertices.Count;
        for (int i = 0; i < transTris.Count; i++) transTris[i] += baseAfterLiquid;
        vertices.AddRange(transVerts); uvs.AddRange(transUvs); normals.AddRange(transNorms);
        transVerts.Clear(); transUvs.Clear(); transNorms.Clear();

        opaqueColors.AddRange(liqColors); opaqueColors.AddRange(transColors);
        liqColors.Clear(); transColors.Clear();
    }

    // Runs on main thread. Writes pre-merged list data into a single interleaved native
    // buffer in one pass — faster than six separate mesh.Set* calls.
    public void CreateMesh()
    {
        int totalVerts   = vertices.Count;
        int triCount0    = triangles.Count;
        int triCount1    = liqTris.Count;
        int triCount2    = transTris.Count;
        int totalIndices = triCount0 + triCount1 + triCount2;

        var mda = Mesh.AllocateWritableMeshData(1);
        var md  = mda[0];

        md.SetVertexBufferParams(totalVerts,
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color,     VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
        md.SetIndexBufferParams(totalIndices, IndexFormat.UInt32);

        var vBuf = md.GetVertexData<MeshVertex>();
        for (int i = 0; i < totalVerts; i++)
            vBuf[i] = new MeshVertex { position = vertices[i], normal = normals[i], uv = uvs[i], color = opaqueColors[i] };

        var iBuf = md.GetIndexData<uint>();
        int t = 0;
        for (int i = 0; i < triCount0; i++) iBuf[t++] = (uint)triangles[i];
        for (int i = 0; i < triCount1; i++) iBuf[t++] = (uint)liqTris[i];
        for (int i = 0; i < triCount2; i++) iBuf[t++] = (uint)transTris[i];

        md.subMeshCount = 3;
        md.SetSubMesh(0, new SubMeshDescriptor(0,                     triCount0), MeshUpdateFlags.DontRecalculateBounds);
        md.SetSubMesh(1, new SubMeshDescriptor(triCount0,             triCount1), MeshUpdateFlags.DontRecalculateBounds);
        md.SetSubMesh(2, new SubMeshDescriptor(triCount0 + triCount1, triCount2), MeshUpdateFlags.DontRecalculateBounds);

        var mesh = new Mesh();
        Mesh.ApplyAndDisposeWritableMeshData(mda, mesh,
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontNotifyMeshUsers |
            MeshUpdateFlags.DontRecalculateBounds);
        mesh.bounds = new Bounds(new Vector3(8f, 128f, 8f), new Vector3(16f, 256f, 16f));
        meshFilter.mesh = mesh;
        skyLightMap = null;

        vertices.Clear(); uvs.Clear(); normals.Clear(); opaqueColors.Clear();
        triangles.Clear(); liqTris.Clear(); transTris.Clear();
    }

    public void AddTexture(int textureID, List<Vector2> targetUvs)
    {
        float y = textureID / VoxelData.textureAtlasSizeInBlocks;
        float x = textureID - (y * VoxelData.textureAtlasSizeInBlocks);

        x *= VoxelData.normalizedBlockTextureSize;
        y *= VoxelData.normalizedBlockTextureSize;

        float uvOffset = 0.0001f;
        targetUvs.Add(new Vector2(x + uvOffset,                                    y + uvOffset));
        targetUvs.Add(new Vector2(x + uvOffset,                                    y + VoxelData.normalizedBlockTextureSize - uvOffset));
        targetUvs.Add(new Vector2(x + VoxelData.normalizedBlockTextureSize - uvOffset, y + uvOffset));
        targetUvs.Add(new Vector2(x + VoxelData.normalizedBlockTextureSize - uvOffset, y + VoxelData.normalizedBlockTextureSize - uvOffset));
    }

}

public class ChunkCoord : System.IEquatable<ChunkCoord>
{

    public int x;
    public int z;

    public ChunkCoord(int _x, int _z)
    {
        x = _x;
        z = _z;
    }

    public bool Equals(ChunkCoord other)
    {
        if (other == null)
            return false;
        return other.x == x && other.z == z;
    }

    public override bool Equals(object other) => Equals(other as ChunkCoord);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + x;
        hash = hash * 31 + z;
        return hash;
    }

}
