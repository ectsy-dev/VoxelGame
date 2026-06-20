using UnityEngine;

[ExecuteAlways]
public class SkyboxController : MonoBehaviour
{
    public Material skyboxMaterial;

    [Header("Sky Colors")]
    public Color zenithColor  = new Color(0.10f, 0.35f, 0.78f);
    public Color horizonColor = new Color(0.60f, 0.80f, 1.00f);
    public Color groundColor  = new Color(0.05f, 0.05f, 0.05f);

    [Header("Fog")]
    public float fogStart = 80f;
    public float fogEnd   = 175f;

    void OnEnable()  => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        if (skyboxMaterial == null) return;

        skyboxMaterial.SetColor("_ZenithColor",  zenithColor);
        skyboxMaterial.SetColor("_HorizonColor", horizonColor);
        skyboxMaterial.SetColor("_GroundColor",  groundColor);

        RenderSettings.skybox          = skyboxMaterial;
        RenderSettings.fog             = true;
        RenderSettings.fogMode         = FogMode.Linear;
        RenderSettings.fogColor        = horizonColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance  = fogEnd;

        DynamicGI.UpdateEnvironment();
    }
}
