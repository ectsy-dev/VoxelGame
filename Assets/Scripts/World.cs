using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

public class World : MonoBehaviour
{

    public Transform player;
    public Vector3 spawnPosition;

    public int worldSeed;
    public int seedOffSet;

    public BiomeAttributes[] biomes;

    public Material atlasMaterial;
    public BlockTypes[] blockTypes;

    List<ChunkCoord> activeChunks = new List<ChunkCoord>();
    ChunkCoord playerCurrentChunkCoord;
    ChunkCoord playerLastChunkCoord;
    Chunk[][] chunks = new Chunk[VoxelData.worldSizeInChunks][];

    public void Start()
    {

        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i] = new Chunk[VoxelData.worldSizeInChunks];
        }

        seedOffSet = worldSeed;

        spawnPosition = new Vector3(((VoxelData.worldSizeInChunks * VoxelData.chunkWidth) / 2f), biomes[0].terrainHeight - 13, ((VoxelData.worldSizeInChunks * VoxelData.chunkWidth) / 2f));

        GenerateWorld();
        playerLastChunkCoord = GetChunkCoordFromVector3(player.position);

    }

    private void Update()
    {
        
        playerCurrentChunkCoord = GetChunkCoordFromVector3(player.position);

        if (!playerCurrentChunkCoord.Equals(playerLastChunkCoord))
        {

            CheckViewDistance();
            playerLastChunkCoord = playerCurrentChunkCoord;

        }

    }

    void GenerateWorld()
    {

        for(int x = (int)((VoxelData.worldSizeInChunks / 2) - VoxelData.viewDistanceInChunks); x < (VoxelData.worldSizeInChunks / 2) + VoxelData.viewDistanceInChunks; x++)
        {
            for(int z = (int)((VoxelData.worldSizeInChunks / 2) - VoxelData.viewDistanceInChunks); z < (VoxelData.worldSizeInChunks / 2) + VoxelData.viewDistanceInChunks; z++)
            {

                CreateNewChunk(x, z);

            }
        }

        player.position = spawnPosition;

    }

    void CheckViewDistance()
    {
        
        List<ChunkCoord> previouslyActiveChunks = new List<ChunkCoord>(activeChunks);

        ChunkCoord playerCoord = GetChunkCoordFromVector3(player.position);

        for(int x = (int)(playerCoord.x - VoxelData.viewDistanceInChunks); x < playerCoord.x + VoxelData.viewDistanceInChunks; x++)
        {

            for (int z = (int)(playerCoord.z - VoxelData.viewDistanceInChunks); z < playerCoord.z + VoxelData.viewDistanceInChunks; z++)
            {

                if (IsChunkInWorld(new ChunkCoord(x, z)))
                {

                    if (chunks[x][z] == null)
                        CreateNewChunk(x, z);

                    else if (!chunks[x][z].IsActive)
                    {

                        chunks[x][z].IsActive = true;
                        activeChunks.Add(new ChunkCoord(x, z));

                    }

                }

                for(int i = 0; i < previouslyActiveChunks.Count; i++)
                {

                    if (previouslyActiveChunks[i].Equals(new ChunkCoord(x, z)))
                        previouslyActiveChunks.RemoveAt(i);

                }
                
            }

        }

        foreach(ChunkCoord c in previouslyActiveChunks)
            chunks[c.x][c.z].IsActive = false;

        previouslyActiveChunks.Clear();

    }

    ChunkCoord GetChunkCoordFromVector3(Vector3 pos)
    {

        int x = Mathf.FloorToInt(pos.x / VoxelData.chunkWidth);
        int z = Mathf.FloorToInt(pos.z / VoxelData.chunkWidth);
        return new ChunkCoord(x, z);

    }

    public bool CheckForVoxel(float x, float y, float z)
    {

        uint xCheck = (uint)Mathf.FloorToInt(x);
        uint yCheck = (uint)Mathf.FloorToInt(y);
        uint zCheck = (uint)Mathf.FloorToInt(z);

        uint xChunk = xCheck / VoxelData.chunkWidth;
        uint zChunk = zCheck / VoxelData.chunkWidth;

        xCheck -= (xChunk * VoxelData.chunkWidth);
        zCheck -= (zChunk * VoxelData.chunkWidth);

        return blockTypes[chunks[xChunk][zChunk].voxelMap[xCheck, yCheck, zCheck]].isSolid;

    }

    public short GetVoxel(Vector3 pos)
    {

        int yPos = Mathf.FloorToInt(pos.y);

        if (!IsVoxelInWorld(pos))
            return 0;

        if (yPos <= Random.Range(0, 4))
            return 1;

        int terrainHeight = Mathf.FloorToInt(biomes[0].terrainHeight * Noise.Get2DNoise(new Vector2(pos.x, pos.z), 500f, biomes[0].terrainScale, seedOffSet)) + biomes[0].solidGroundHeight;
        short voxelValue = 0;

        if (yPos == terrainHeight)
            voxelValue = 2;

        else if (yPos < terrainHeight && yPos > terrainHeight - 4)
            voxelValue = 3;

        else if (yPos > terrainHeight)
            return 0;

        else
            voxelValue = 4;

        if(voxelValue == 4 || voxelValue == 3 || voxelValue == 2)
        {

            foreach(Nodes nodes in biomes[0].nodes)
            {

                if(yPos > nodes.minHeight && yPos < nodes.maxHeight)
                    if(Noise.Get3DNoise(pos, nodes.noiseThreshold, nodes.noiseScale, nodes.noiseOffset, seedOffSet))
                        voxelValue = nodes.blockID;

            }

        }

        return voxelValue;

    }

    void CreateNewChunk(int x, int z)
    {

        chunks[x][z] = new Chunk(this, new ChunkCoord(x, z));
        activeChunks.Add(new ChunkCoord(x, z));

    }

    bool IsChunkInWorld(ChunkCoord coord)
    {
        if (coord.x < 0 || coord.x >= VoxelData.worldSizeInChunks - 1 || coord.z < 0 || coord.z >= VoxelData.worldSizeInChunks - 1)
            return false;
        else
            return true;
    }

    bool IsVoxelInWorld(Vector3 pos)
    {
        if (pos.x < 0 || pos.x >= VoxelData.worldSizeInVoxels || pos.y < 0 || pos.y >= VoxelData.chunkHeight || pos.z < 0 || pos.z >= VoxelData.worldSizeInVoxels)
            return false;
        else
            return true;
    }

}

[System.Serializable]
public class BlockTypes
{

    public string blockName;
    public bool isSolid;

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
