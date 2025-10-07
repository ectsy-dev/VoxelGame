using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public static class Noise
{

    public static float Get2DNoise(Vector2 pos, float offset, float scale, int seedOffSet)
    {

        uint textureWidth = VoxelData.chunkWidth;
        uint textureHeight = VoxelData.chunkWidth;

        float amplitude = 1.6f;
        float frequency = 0.05f;

        float octaves = 4;

        float noiseValue = 0;

        for (int i = 0; i < octaves; i++)
        {

            noiseValue += GeneratePerlinValue(pos, offset, scale, seedOffSet, ref amplitude, ref frequency);

        }

        return noiseValue;

    }

    public static float GeneratePerlinValue(Vector2 pos, float offset, float scale, int seedOffSet, ref float amplitude, ref float frequency)
    {

        float persistence = 0.5f;
        float lacunarity = 2.0f;

        float perlineValue = 0;

        float xCoord = (pos.x + seedOffSet + offset) * frequency * scale;
        float yCoord = (pos.y + seedOffSet + offset) * frequency * scale;
        
        amplitude *= persistence;
        frequency *= lacunarity;

        return perlineValue = Mathf.PerlinNoise(xCoord, yCoord) * amplitude;

    }


    public static bool Get3DNoise(Vector3 pos, float threshold, float scale, float offset, int seedOffSet)
    {
        float xCoord = (pos.x + offset + seedOffSet) * scale;
        float yCoord = (pos.y + offset + seedOffSet) * scale;
        float zCoord = (pos.z + offset + seedOffSet) * scale;

        float AB = Mathf.PerlinNoise(xCoord, yCoord);
        float BC = Mathf.PerlinNoise(yCoord, zCoord);
        float AC = Mathf.PerlinNoise(xCoord, zCoord);
        float BA = Mathf.PerlinNoise(yCoord, xCoord);
        float CB = Mathf.PerlinNoise(zCoord, yCoord);
        float CA = Mathf.PerlinNoise(zCoord, xCoord);

        if ((AB + BC + AC + BA + CB + CA) / 6f > threshold)
            return true;
        
        else
            return false;

    }

}
