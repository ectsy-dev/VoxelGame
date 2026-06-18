using UnityEngine;
using System.Collections.Generic;

public class World : MonoBehaviour
{

    public Transform player;
    public Vector3 spawnPosition;

    public string worldSeed;
    public int seedOffSet;

    public BiomeAttributes[] biomes;

    // Per-chunk raw block data, generated once on demand
    Dictionary<Vector2Int, short[,,]> genCache = new Dictionary<Vector2Int, short[,,]>();

    public Material atlasMaterial;
    public Material transparentAtlasMaterial;
    public BlockTypes[] blockTypes;

    // Vector2Int(chunkX, chunkZ) -> Chunk. Uses Unity's built-in struct equality — no custom hash needed.
    Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    HashSet<Vector2Int> activeChunks = new HashSet<Vector2Int>();

    int playerLastChunkX;
    int playerLastChunkZ;

    public void Start()
    {

        // Always use the PlayerController's own transform so World tracks the object that actually moves
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

        // Search outward until we find a land block above sea level
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

        spawnPosition = new Vector3(spawnX, spawnY, spawnZ);
        player.position = spawnPosition;

        playerLastChunkX = Mathf.FloorToInt(spawnPosition.x / (int)VoxelData.chunkWidth);
        playerLastChunkZ = Mathf.FloorToInt(spawnPosition.z / (int)VoxelData.chunkWidth);

        Debug.Log($"[World] Start complete. Player={player.name} spawn={spawnPosition} chunk=({playerLastChunkX},{playerLastChunkZ})");

        CheckViewDistance();

    }

    private void Update()
    {

        int cx = Mathf.FloorToInt(player.position.x / (int)VoxelData.chunkWidth);
        int cz = Mathf.FloorToInt(player.position.z / (int)VoxelData.chunkWidth);

        if (Time.frameCount % 120 == 0)
            Debug.Log($"[World] tick: player pos={player.position} chunk=({cx},{cz}) last=({playerLastChunkX},{playerLastChunkZ}) totalChunks={chunks.Count}");

        if (cx != playerLastChunkX || cz != playerLastChunkZ)
        {
            Debug.Log($"[World] chunk change ({playerLastChunkX},{playerLastChunkZ}) -> ({cx},{cz})");
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
        int created = 0;

        for (int x = pCx - viewDist; x < pCx + viewDist; x++)
        {
            for (int z = pCz - viewDist; z < pCz + viewDist; z++)
            {
                Vector2Int coord = new Vector2Int(x, z);

                if (!chunks.ContainsKey(coord))
                {
                    CreateNewChunk(x, z);
                    created++;
                }
                else if (!chunks[coord].IsActive)
                    chunks[coord].IsActive = true;

                newActiveChunks.Add(coord);
            }
        }

        int deactivated = 0;
        foreach (Vector2Int c in activeChunks)
        {
            if (!newActiveChunks.Contains(c) && chunks.ContainsKey(c))
            {
                chunks[c].IsActive = false;
                deactivated++;
            }
        }

        activeChunks = newActiveChunks;
        Debug.Log($"[World] CheckViewDistance at ({pCx},{pCz}): created={created} deactivated={deactivated} totalChunks={chunks.Count} active={activeChunks.Count}");

    }

    void CreateNewChunk(int x, int z)
    {

        Vector2Int coord = new Vector2Int(x, z);
        chunks[coord] = new Chunk(this, new ChunkCoord(x, z));

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

        if (chunks.ContainsKey(coord) && chunks[coord] != null)
            return blockTypes[chunks[coord].voxelMap[xLocal, yi, zLocal]].isSolid;

        return blockTypes[GetVoxel(new Vector3(xi, yi, zi))].isSolid;

    }

    public short GetVoxel(Vector3 pos)
    {
        int y = Mathf.FloorToInt(pos.y);
        if (y < 0 || y >= VoxelData.chunkHeight) return 0;

        int chunkX = Mathf.FloorToInt(pos.x / (int)VoxelData.chunkWidth);
        int chunkZ = Mathf.FloorToInt(pos.z / (int)VoxelData.chunkWidth);
        int lx = Mathf.FloorToInt(pos.x) - chunkX * (int)VoxelData.chunkWidth;
        int lz = Mathf.FloorToInt(pos.z) - chunkZ * (int)VoxelData.chunkWidth;

        var key = new Vector2Int(chunkX, chunkZ);
        if (!genCache.ContainsKey(key))
        {
            bool[,] west  = NeighborWaterMap(chunkX - 1, chunkZ);
            bool[,] east  = NeighborWaterMap(chunkX + 1, chunkZ);
            bool[,] south = NeighborWaterMap(chunkX, chunkZ - 1);
            bool[,] north = NeighborWaterMap(chunkX, chunkZ + 1);
            genCache[key] = AlphaTerrainGen.GenerateChunk(chunkX, chunkZ, seedOffSet,
                biomes != null && biomes.Length > 0 ? biomes[0].nodes : null,
                west, east, south, north);
        }

        return genCache[key][lx, y, lz];
    }

    // Returns a 16×16 water map for a neighbor chunk.
    // Fast path: extracts from genCache if the neighbor is already generated.
    // Slow path: runs raw terrain only (no surface passes) via BuildWaterMap.
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
