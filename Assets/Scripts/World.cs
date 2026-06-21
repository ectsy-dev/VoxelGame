using UnityEngine;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;

public class World : MonoBehaviour
{

    [Header("Player Values")]
    public Transform player;
    public Vector3 spawnPosition;

    [Header("World Generation Values")]
    public string worldSeed;
    public int seedOffSet;

    [Header("Ore Generator")]
    public OreConfig oreConfig;

    // ConcurrentDictionary allows background threads to call GetVoxel safely
    // without an explicit lock. If two threads generate the same chunk simultaneously,
    // GenerateChunk is deterministic so both results are identical — one is discarded.
    ConcurrentDictionary<Vector2Int, short[,,]> genCache = new ConcurrentDictionary<Vector2Int, short[,,]>();

    [Header("Block Materials")]
    public Material atlasMaterial;
    public Material liquidMaterial;
    public Material transparentAtlasMaterial;

    [Header("Lightmap Textures")]
    public Texture2D dayLightmap;
    public Texture2D nightLightmap;

    [Header("Blocks")]
    public BlockTypes[] blockTypes;

    Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    HashSet<Vector2Int> activeChunks = new HashSet<Vector2Int>();

    // Per-chunk sky light maps, cached after ComputeSkyLight() so neighbor chunks can
    // look up the adjacent block's actual computed sky light at face-build time.
    // Entries are removed when chunks are deactivated to free memory.
    public ConcurrentDictionary<Vector2Int, byte[,,]> skyLightCache =
        new ConcurrentDictionary<Vector2Int, byte[,,]>();

    // Chunks whose BuildData() has finished and are waiting for ApplyMesh() on the main thread
    ConcurrentQueue<Chunk> meshReadyQueue = new ConcurrentQueue<Chunk>();

    // Chunks waiting to be created — drained gradually in Update() to avoid a one-frame spike
    Queue<Vector2Int> pendingCreations = new Queue<Vector2Int>();
    HashSet<Vector2Int> pendingSet     = new HashSet<Vector2Int>();
    const int chunkCreationsPerFrame   = 8;

    // Chunks that need to rebuild their mesh because a newly-loaded neighbor
    // provided better border sky light values after their initial build.
    Queue<Vector2Int>  remeshQueue = new Queue<Vector2Int>();
    HashSet<Vector2Int> remeshSet  = new HashSet<Vector2Int>();

    int playerLastChunkX;
    int playerLastChunkZ;

    public void Start()
    {
        Application.targetFrameRate = 144;

        // Lightmap LUT textures — encode brightness for every (blockLight, skyLight) pair.
        // To implement day/night: lerp _GlobalLightLevel 0→1 from sun angle each frame.
        if (dayLightmap   != null) Shader.SetGlobalTexture("_DayLightmap",   dayLightmap);
        if (nightLightmap != null) Shader.SetGlobalTexture("_NightLightmap", nightLightmap);
        Shader.SetGlobalFloat("_GlobalLightLevel", 1.0f);

        PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null)
            player = pc.transform;
        else
            Debug.LogError("[World] No PlayerController found in scene!");

        if (int.TryParse(worldSeed, out int parsedSeed))
            seedOffSet = parsedSeed;
        else
            seedOffSet = worldSeed.GetHashCode();

        seedOffSet = ((seedOffSet % 50000) + 50000) % 50000;

        float spawnX = 8f, spawnZ = 8f;
        int spawnY = AlphaTerrainGen.SEA_LEVEL + 2;

        bool spawnFound = false;
        int cw = (int)VoxelData.chunkWidth;
        for (int r = 0; r <= 4 && !spawnFound; r++)
        {
            for (int dx = -r; dx <= r && !spawnFound; dx++)
            for (int dz = -r; dz <= r && !spawnFound; dz++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                float tx = 8f + dx * cw, tz = 8f + dz * cw;
                for (int y = AlphaTerrainGen.WORLD_HEIGHT - 1; y > AlphaTerrainGen.SEA_LEVEL; y--)
                {
                    short b = GetVoxel(new Vector3(tx, y, tz));
                    if (b != 0 && b != AlphaTerrainGen.ID_WATER)
                    {
                        spawnX = tx; spawnZ = tz; spawnY = y + 1;
                        spawnFound = true;
                        break;
                    }
                }
            }
        }

        float spawnYF = spawnY + (pc != null ? pc.PlayerHeight : 1f);
        spawnPosition = new Vector3(spawnX, spawnYF, spawnZ);
        player.position = spawnPosition;

        playerLastChunkX = Mathf.FloorToInt(spawnPosition.x / (int)VoxelData.chunkWidth);
        playerLastChunkZ = Mathf.FloorToInt(spawnPosition.z / (int)VoxelData.chunkWidth);

        Debug.Log($"[World] Start complete. Player={player.name} spawn={spawnPosition} chunk=({playerLastChunkX},{playerLastChunkZ})");

        CheckViewDistance();

    }

    private void Update()
    {

        // Apply up to 3 ready chunk meshes per frame to avoid a spike
        int applied = 0;
        while (applied < 3 && meshReadyQueue.TryDequeue(out Chunk readyChunk))
        {
            readyChunk.ApplyMesh();
            // On first build only: dirty the 4 neighbours so they pick up any
            // border sky light improvements our BFS introduced into the cache.
            // Remesh rebuilds skip this to prevent infinite dirty chains.
            if (readyChunk.isFirstBuild)
            {
                readyChunk.isFirstBuild = false;
                DirtyNeighbors(readyChunk.coord);
            }
            applied++;
        }

        // Rebuild dirty chunks (up to 2 per frame) on background threads
        int rebuilt = 0;
        while (rebuilt < 2 && remeshQueue.Count > 0)
        {
            Vector2Int key = remeshQueue.Dequeue();
            remeshSet.Remove(key);
            if (chunks.TryGetValue(key, out Chunk dirtyChunk) && dirtyChunk.isDataReady && dirtyChunk.IsActive && !dirtyChunk.isMeshPending)
            {
                dirtyChunk.isMeshPending = true;
                Task.Run(() =>
                {
                    dirtyChunk.RebuildMeshData();
                    meshReadyQueue.Enqueue(dirtyChunk);
                });
                rebuilt++;
            }
        }

        // Create pending chunks gradually — closest first, capped per frame
        int spawned = 0;
        while (spawned < chunkCreationsPerFrame && pendingCreations.Count > 0)
        {
            Vector2Int coord = pendingCreations.Dequeue();
            pendingSet.Remove(coord);
            if (!chunks.ContainsKey(coord) && activeChunks.Contains(coord))
            {
                CreateNewChunk(coord.x, coord.y);
                spawned++;
            }
        }

        int cx = Mathf.FloorToInt(player.position.x / (int)VoxelData.chunkWidth);
        int cz = Mathf.FloorToInt(player.position.z / (int)VoxelData.chunkWidth);

        if (cx != playerLastChunkX || cz != playerLastChunkZ)
        {
            playerLastChunkX = cx;
            playerLastChunkZ = cz;
            CheckViewDistance();
        }

    }

    void CheckViewDistance()
    {

        int pCx = Mathf.FloorToInt(player.position.x / (int)VoxelData.chunkWidth);
        int pCz = Mathf.FloorToInt(player.position.z / (int)VoxelData.chunkWidth);
        int viewDist = (int)VoxelData.viewDistanceInChunks;

        HashSet<Vector2Int> newActiveChunks = new HashSet<Vector2Int>();
        var toQueue = new List<Vector2Int>();

        for (int x = pCx - viewDist; x < pCx + viewDist; x++)
        {
            for (int z = pCz - viewDist; z < pCz + viewDist; z++)
            {
                Vector2Int coord = new Vector2Int(x, z);

                if (!chunks.ContainsKey(coord) && !pendingSet.Contains(coord))
                    toQueue.Add(coord);
                else if (chunks.ContainsKey(coord) && !chunks[coord].IsActive)
                    chunks[coord].IsActive = true;

                newActiveChunks.Add(coord);
            }
        }

        // Enqueue closest chunks first so nearby terrain appears before distant terrain
        toQueue.Sort((a, b) =>
        {
            int da = (a.x - pCx) * (a.x - pCx) + (a.y - pCz) * (a.y - pCz);
            int db = (b.x - pCx) * (b.x - pCx) + (b.y - pCz) * (b.y - pCz);
            return da.CompareTo(db);
        });
        foreach (var coord in toQueue)
        {
            var c = coord;
            Task.Run(() => GetOrGenerateChunkData(c.x, c.y));
            pendingCreations.Enqueue(coord);
            pendingSet.Add(coord);
        }

        int deactivated = 0;
        foreach (Vector2Int c in activeChunks)
        {
            if (!newActiveChunks.Contains(c) && chunks.ContainsKey(c))
            {
                chunks[c].IsActive = false;
                skyLightCache.TryRemove(c, out _);
                deactivated++;
            }
        }

        activeChunks = newActiveChunks;

    }

    void CreateNewChunk(int x, int z)
    {
        Vector2Int coord = new Vector2Int(x, z);
        Chunk chunk = new Chunk(this, new ChunkCoord(x, z));
        chunks[coord] = chunk;

        Task.Run(() =>
        {
            chunk.BuildData();
            meshReadyQueue.Enqueue(chunk);
        });
    }

    public bool CheckForVoxel(float x, float y, float z)
    {

        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        int zi = Mathf.FloorToInt(z);

        if (yi < 0 || yi >= VoxelData.chunkHeight)
            return true;

        int xChunk = Mathf.FloorToInt(x / (int)VoxelData.chunkWidth);
        int zChunk = Mathf.FloorToInt(z / (int)VoxelData.chunkWidth);
        int xLocal = xi - xChunk * (int)VoxelData.chunkWidth;
        int zLocal = zi - zChunk * (int)VoxelData.chunkWidth;

        Vector2Int coord = new Vector2Int(xChunk, zChunk);

        if (chunks.ContainsKey(coord) && chunks[coord] != null && chunks[coord].isDataReady)
            return blockTypes[chunks[coord].voxelMap[xLocal, yi, zLocal]].isSolid;

        return blockTypes[GetVoxel(new Vector3(xi, yi, zi))].isSolid;

    }

    short[,,] GetOrGenerateChunkData(int chunkX, int chunkZ)
    {
        var key = new Vector2Int(chunkX, chunkZ);
        return genCache.GetOrAdd(key, _ =>
        {
            bool[,] west  = NeighborWaterMap(chunkX - 1, chunkZ);
            bool[,] east  = NeighborWaterMap(chunkX + 1, chunkZ);
            bool[,] south = NeighborWaterMap(chunkX, chunkZ - 1);
            bool[,] north = NeighborWaterMap(chunkX, chunkZ + 1);
            return AlphaTerrainGen.GenerateChunk(chunkX, chunkZ, seedOffSet,
                oreConfig != null ? oreConfig.nodes : null,
                west, east, south, north);
        });
    }

    public short[,,] GetChunkRawData(int chunkX, int chunkZ) =>
        GetOrGenerateChunkData(chunkX, chunkZ);

    public short GetVoxel(Vector3 pos)
    {
        int y = Mathf.FloorToInt(pos.y);
        if (y < 0 || y >= VoxelData.chunkHeight) return 0;

        int chunkX = Mathf.FloorToInt(pos.x / (int)VoxelData.chunkWidth);
        int chunkZ = Mathf.FloorToInt(pos.z / (int)VoxelData.chunkWidth);
        int lx = Mathf.FloorToInt(pos.x) - chunkX * (int)VoxelData.chunkWidth;
        int lz = Mathf.FloorToInt(pos.z) - chunkZ * (int)VoxelData.chunkWidth;

        var data = GetOrGenerateChunkData(chunkX, chunkZ);
        return data[lx, y, lz];
    }

    void DirtyNeighbors(ChunkCoord coord)
    {
        var offsets = new Vector2Int[]
        {
            new Vector2Int(-1,  0),
            new Vector2Int( 1,  0),
            new Vector2Int( 0, -1),
            new Vector2Int( 0,  1),
        };
        foreach (var offset in offsets)
        {
            var key = new Vector2Int(coord.x + offset.x, coord.z + offset.y);
            if (chunks.ContainsKey(key) && chunks[key].isDataReady && !remeshSet.Contains(key))
            {
                remeshQueue.Enqueue(key);
                remeshSet.Add(key);
            }
        }
    }

    bool[,] NeighborWaterMap(int cx, int cz)
    {
        if (genCache.TryGetValue(new Vector2Int(cx, cz), out short[,,] cached))
        {
            var m = new bool[16, 16];
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                m[x, z] = cached[x, AlphaTerrainGen.SEA_LEVEL, z] == 0;
            return m;
        }
        return AlphaTerrainGen.BuildWaterMap(cx, cz, seedOffSet);
    }

}

[System.Serializable]
public class BlockTypes
{

    public string blockName;
    public bool isSolid;
    public bool isTransparent;
    public bool isLiquid;

    public int backFaceTexture;
    public int frontFaceTexture;
    public int topFaceTexture;
    public int bottomFaceTexture;
    public int leftFaceTexture;
    public int rightFaceTexture;

    public int GetTextureID(int faceIndex) => faceIndex switch
    {
        0 => backFaceTexture,
        1 => frontFaceTexture,
        2 => topFaceTexture,
        3 => bottomFaceTexture,
        4 => leftFaceTexture,
        5 => rightFaceTexture,
        _ => throw new System.ArgumentOutOfRangeException(nameof(faceIndex), "Invalid face index")
    };

}
