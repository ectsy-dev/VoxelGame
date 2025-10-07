using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class Chunk
{

    public ChunkCoord coord;

    GameObject chunkObject;
    MeshRenderer meshRenderer;
    MeshFilter meshFilter;

    int vertexIndex = 0;
    List<Vector3> vertices = new List<Vector3>();
    List<int> triangles = new List<int>();
    List<Vector2> uvs = new List<Vector2>();

    short[,,] voxelMap = new short[VoxelData.chunkWidth, VoxelData.chunkHeight, VoxelData.chunkWidth];

    World worldObj;

    public Chunk(World _worldObj, ChunkCoord _coord)
    {

        worldObj = _worldObj;
        coord = _coord;

        chunkObject = new GameObject
        {
            name = "Chunk " + coord.x + ", " + coord.z
        };

        meshFilter = chunkObject.AddComponent<MeshFilter>();
        meshRenderer = chunkObject.AddComponent<MeshRenderer>();

        meshRenderer.material = worldObj.atlasMaterial;
        chunkObject.transform.parent = worldObj.transform;
        chunkObject.transform.position = new Vector3(coord.x * VoxelData.chunkWidth, 0, coord.z * VoxelData.chunkWidth);

        PopulateVoxelMap();
        CreateChunkData();
        CreateMesh();


    }

    public void PopulateVoxelMap()
    {

        for (int x = 0; x < VoxelData.chunkWidth; x++)
        {
            for (int y = 0; y < VoxelData.chunkHeight; y++)
            {
                for (int z = 0; z < VoxelData.chunkWidth; z++)
                {

                    voxelMap[x, y, z] = worldObj.GetVoxel(new Vector3(x, y, z) + position);

                }
            }
        }

    }

    public void CreateChunkData()
    {

        for (int x = 0; x < VoxelData.chunkWidth; x++)
        {
            for (int y = 0; y < VoxelData.chunkHeight; y++)
            {
                for (int z = 0; z < VoxelData.chunkWidth; z++)
                {

                    if (worldObj.blockTypes[voxelMap[x, y, z]].isSolid)
                        AddVoxelDataToChunk(new Vector3(x, y, z));

                }
            }
        }

    }

    bool IsVoxelInChunk(int x, int y, int z)
    {
        if (x < 0 || x > VoxelData.chunkWidth - 1 || y < 0 || y > VoxelData.chunkHeight - 1 || z < 0 || z > VoxelData.chunkWidth - 1)
            return false;

        return true;
    }

    public bool IsActive
    {

        get => chunkObject.activeSelf;
        set => chunkObject.SetActive(value);

    }

    public Vector3 position => chunkObject.transform.position;

    public bool CheckVoxel(Vector3 pos)
    {

        int x = Mathf.FloorToInt(pos.x);
        int y = Mathf.FloorToInt(pos.y);
        int z = Mathf.FloorToInt(pos.z);

        if (!IsVoxelInChunk(x, y, z))
            return worldObj.blockTypes[worldObj.GetVoxel(pos + position)].isSolid;

        return worldObj.blockTypes[voxelMap[x, y, z]].isSolid;

    }

    public void AddVoxelDataToChunk(Vector3 pos)
    {

        for (int h = 0; h < 6; h++)
        {

            if(!CheckVoxel(pos + VoxelData.faceChecks[h]))
            {

                short blockID = voxelMap[(int)pos.x, (int)pos.y, (int)pos.z];

                vertices.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 0]]);
                vertices.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 1]]);
                vertices.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 2]]);
                vertices.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 3]]);

                AddTexture(worldObj.blockTypes[blockID].GetTextureID(h));

                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex + 2);
                triangles.Add(vertexIndex + 2);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex + 3);
                vertexIndex += 4;   

            }

        }

    }

    public void CreateMesh()
    {
        Mesh mesh = new Mesh
        {
            vertices = vertices.ToArray(),
            triangles = triangles.ToArray(),
            uv = uvs.ToArray()
        };

        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
    }

    public void AddTexture(int textureID)
    {

        float y = textureID / VoxelData.textureAtlasSizeInBlocks;
        float x = textureID - (y * VoxelData.textureAtlasSizeInBlocks);

        x *= VoxelData.normalizedBlockTextureSize;
        y *= VoxelData.normalizedBlockTextureSize;

        float uvOffset = 0.001f;
        uvs.Add(new Vector2(x + uvOffset, y + uvOffset));
        uvs.Add(new Vector2(x + uvOffset, y + VoxelData.normalizedBlockTextureSize - uvOffset));
        uvs.Add(new Vector2(x + VoxelData.normalizedBlockTextureSize - uvOffset, y + uvOffset));
        uvs.Add(new Vector2(x + VoxelData.normalizedBlockTextureSize - uvOffset, y + VoxelData.normalizedBlockTextureSize - uvOffset));

    }

}

public class ChunkCoord
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

}
