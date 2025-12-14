using UnityEngine;
using TMPro;
using System.Diagnostics;

public class UILatencyTester : MonoBehaviour
{
    public TMP_Text latencyText;
    private Stopwatch stopwatch;

    void Start()
    {
        stopwatch = new Stopwatch();
        InvokeRepeating(nameof(SimulateDataUpdate), 1f, 1f); 
    }

    void SimulateDataUpdate()
    {
        stopwatch.Restart();
        StartCoroutine(UpdateUI());
    }

    System.Collections.IEnumerator UpdateUI()
    {
        yield return null; 
        stopwatch.Stop();
        if (latencyText != null)
            latencyText.text = $"UI Delay: {stopwatch.ElapsedMilliseconds} ms";
    }
}
