using System.Collections.Generic;
using UnityEngine;

public class Chunk
{

    public ChunkCoord coord;

    GameObject chunkObject;
    MeshRenderer meshRenderer;
    MeshFilter meshFilter;

    List<Vector3> vertices = new List<Vector3>();
    List<int> triangles = new List<int>();
    List<Vector2> uvs = new List<Vector2>();
    List<Vector3> normals = new List<Vector3>();

    List<Vector3> transparentVertices = new List<Vector3>();
    List<int> transparentTriangles = new List<int>();
    List<Vector2> transparentUvs = new List<Vector2>();
    List<Vector3> transparentNormals = new List<Vector3>();

    public short[,,] voxelMap = new short[VoxelData.chunkWidth, VoxelData.chunkHeight, VoxelData.chunkWidth];

    // Cached as a plain field so BuildData() can read it safely from a background thread
    // without touching the Unity API (chunkObject.transform.position is main-thread only)
    Vector3 chunkPosition;

    // Set to true at the end of BuildData(). volatile ensures the main thread sees
    // the update immediately without CPU register caching.
    public volatile bool isDataReady = false;

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

        meshRenderer.materials = new Material[] { worldObj.atlasMaterial, worldObj.transparentAtlasMaterial };
        chunkObject.transform.parent = worldObj.transform;

        chunkPosition = new Vector3(coord.x * VoxelData.chunkWidth, 0, coord.z * VoxelData.chunkWidth);
        chunkObject.transform.position = chunkPosition;

    }

    // Called on a background thread — no Unity API calls allowed here
    public void BuildData()
    {
        PopulateVoxelMap();
        CreateChunkData();
        isDataReady = true;
    }

    // Called on the main thread once BuildData() is complete
    public void ApplyMesh()
    {
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

                    voxelMap[x, y, z] = worldObj.GetVoxel(new Vector3(x, y, z) + chunkPosition);

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

                    short id = voxelMap[x, y, z];
                    if (id == 0) continue;
                    if (id >= worldObj.blockTypes.Length)
                    {
                        Debug.LogError($"Block ID {id} at ({x},{y},{z}) exceeds blockTypes array length {worldObj.blockTypes.Length}");
                        continue;
                    }

                    if (worldObj.blockTypes[id].isTransparent)
                        AddVoxelDataToChunk(new Vector3(x, y, z), true);
                    else if (worldObj.blockTypes[id].isSolid)
                        AddVoxelDataToChunk(new Vector3(x, y, z), false);

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

    public void AddVoxelDataToChunk(Vector3 pos, bool transparent)
    {

        short thisID = voxelMap[(int)pos.x, (int)pos.y, (int)pos.z];

        for (int h = 0; h < 6; h++)
        {

            Vector3 adjacentPos = pos + VoxelData.faceChecks[h];
            short adjID = GetBlockIDAt(adjacentPos);
            bool renderFace = transparent
                ? adjID != thisID
                : !CheckVoxel(adjacentPos);

            if (!renderFace) continue;

            List<Vector3> verts = transparent ? transparentVertices : vertices;
            List<int> tris = transparent ? transparentTriangles : triangles;
            List<Vector3> norms = transparent ? transparentNormals : normals;

            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 0]]);
            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 1]]);
            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 2]]);
            verts.Add(pos + VoxelData.voxelVerts[VoxelData.voxelTris[h, 3]]);

            Vector3 faceNormal = VoxelData.faceChecks[h];
            norms.Add(faceNormal);
            norms.Add(faceNormal);
            norms.Add(faceNormal);
            norms.Add(faceNormal);

            AddTexture(worldObj.blockTypes[thisID].GetTextureID(h), transparent);

            int idx = verts.Count - 4;
            tris.Add(idx);
            tris.Add(idx + 1);
            tris.Add(idx + 2);
            tris.Add(idx + 2);
            tris.Add(idx + 1);
            tris.Add(idx + 3);

        }

    }

    public void CreateMesh()
    {
        var allVertices = new List<Vector3>(vertices);
        allVertices.AddRange(transparentVertices);

        var allUvs = new List<Vector2>(uvs);
        allUvs.AddRange(transparentUvs);

        var allNormals = new List<Vector3>(normals);
        allNormals.AddRange(transparentNormals);

        int solidCount = vertices.Count;
        var offsetTransTris = new int[transparentTriangles.Count];
        for (int i = 0; i < transparentTriangles.Count; i++)
            offsetTransTris[i] = transparentTriangles[i] + solidCount;

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = allVertices.ToArray();
        mesh.uv = allUvs.ToArray();
        mesh.normals = allNormals.ToArray();
        mesh.subMeshCount = 2;
        mesh.SetTriangles(triangles.ToArray(), 0);
        mesh.SetTriangles(offsetTransTris, 1);
        meshFilter.mesh = mesh;
    }

    public void AddTexture(int textureID, bool transparent = false)
    {

        float y = textureID / VoxelData.textureAtlasSizeInBlocks;
        float x = textureID - (y * VoxelData.textureAtlasSizeInBlocks);

        x *= VoxelData.normalizedBlockTextureSize;
        y *= VoxelData.normalizedBlockTextureSize;

        float uvOffset = 0.0001f;
        List<Vector2> targetUvs = transparent ? transparentUvs : uvs;
        targetUvs.Add(new Vector2(x + uvOffset, y + uvOffset));
        targetUvs.Add(new Vector2(x + uvOffset, y + VoxelData.normalizedBlockTextureSize - uvOffset));
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
