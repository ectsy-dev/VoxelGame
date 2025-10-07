using UnityEngine;

[CreateAssetMenu(fileName = "BiomeAttributes", menuName = "Scriptable Objects/BiomeAttributes")]
public class BiomeAttributes : ScriptableObject
{

    public string biomeName;
    
    public int solidGroundHeight;

    public int terrainHeight;

    public float terrainScale;

    public Nodes[] nodes;

}

[System.Serializable]
public class Nodes
{

    public string nodName;

    public short blockID;

    public int minHeight;
    public int maxHeight;

    public float noiseScale;
    public float noiseThreshold;
    public float noiseOffset;

}
