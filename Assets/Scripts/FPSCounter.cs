using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    float deltaAccum;
    int frames;
    float displayFPS;

    GUIStyle style;

    void Update()
    {
        deltaAccum += Time.unscaledDeltaTime;
        frames++;

        if (deltaAccum >= 0.5f)
        {
            displayFPS = frames / deltaAccum;
            deltaAccum = 0f;
            frames = 0;
        }
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.normal.textColor = Color.white;
        }

        // Shadow
        GUI.color = Color.black;
        GUI.Label(new Rect(11, 11, 120, 30), $"FPS: {displayFPS:0}");
        // Text
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, 120, 30), $"FPS: {displayFPS:0}");
    }
}
