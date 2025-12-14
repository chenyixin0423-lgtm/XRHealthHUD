using UnityEngine;
using TMPro;

public class FPSDisplay : MonoBehaviour
{
    public TMP_Text fpsText;
    private float deltaTime = 0.0f;
    private float updateInterval = 0.5f;  
    private float timeSinceUpdate = 0.0f;

    void Update()
    {
        deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;

        timeSinceUpdate += Time.deltaTime;
        if (timeSinceUpdate >= updateInterval)
        {
            if (fpsText != null)
                fpsText.text = $"FPS: {fps:F1}";
            timeSinceUpdate = 0.0f;
        }
    }
}
