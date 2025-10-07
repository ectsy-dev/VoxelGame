using UnityEngine;
using System.Collections.Generic;

public class VoxelData
{
    public static readonly uint chunkWidth = 16;
    public static readonly uint chunkHeight = 128;

    public static readonly int worldSeed;

    public static readonly uint worldSizeInChunks = 10;
    public static readonly uint worldSizeInVoxels = worldSizeInChunks * chunkWidth;

    public static readonly uint viewDistanceInChunks = 5;

    public static readonly int textureAtlasSizeInBlocks = 32;
    public static readonly float normalizedBlockTextureSize = 1f / (float)textureAtlasSizeInBlocks;

    public static readonly Vector3[] voxelVerts = {
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(1, 1, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        new Vector3Int(1, 1, 1),
        new Vector3Int(0, 1, 1)
    };

    public static readonly Vector3[] faceChecks =
    {
        new Vector3Int(0, 0, -1), // Back Face
        new Vector3Int(0, 0, 1),  // Front Face
        new Vector3Int(0, 1, 0),  // Top Face
        new Vector3Int(0, -1, 0), // Bottom Face
        new Vector3Int(-1, 0, 0), // Left Face
        new Vector3Int(1, 0, 0)   // Right Face
    };

    public static readonly int[,] voxelTris = {
        {0, 3, 1, 2}, // Back Face
        {5, 6, 4, 7}, // Front Face
        {3, 7, 2, 6}, // Top Face
        {1, 5, 0, 4}, // Bottom Face
        {4, 7, 0, 3}, // Left Face
        {1, 2, 5, 6}  // Right Face
    };

    public static readonly Vector2[] voxelUvs =
    {

        new Vector2Int(0, 0),
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(1, 1)

    };
}
