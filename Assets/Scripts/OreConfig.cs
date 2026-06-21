using UnityEngine;

[CreateAssetMenu(fileName = "OreConfig", menuName = "Scriptable Objects/OreConfig")]
public class OreConfig : ScriptableObject
{
    public OreNode[] nodes;
}

[System.Serializable]
public class OreNode
{
    public string nodeName;

    public short blockID;

    public int minHeight;
    public int maxHeight;

    public float noiseScale;
    public float noiseThreshold;
    public float noiseOffset;
}
