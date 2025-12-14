using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Centralized alert controller with simple priority handling.
/// Higher priority alerts override lower priority ones.
/// Adds smooth fade-in/out via CanvasGroup if present.
/// </summary>
public class AlertManager : MonoBehaviour
{
    public enum AlertType
    {
        None = 0,
        Sedentary = 1,
        HeartHigh = 2
    }

    [Header("UI Refs (Shared Alert Panel)")]
    [SerializeField] private GameObject alertPanel;
    [SerializeField] private TMP_Text alertText;

    [Header("Fade (Optional)")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeInTime = 0.12f;
    [SerializeField] private float fadeOutTime = 0.16f;

    [Header("Behavior")]
    [Tooltip("If true, lower priority alerts cannot override a currently active higher-priority alert.")]
    [SerializeField] private bool lockByPriority = true;

    private AlertType currentType = AlertType.None;
    private Coroutine fadeCo;

    private void Awake()
    {
        // Auto-find CanvasGroup on the panel if not assigned
        if (canvasGroup == null && alertPanel != null)
            canvasGroup = alertPanel.GetComponent<CanvasGroup>();

        if (canvasGroup != null)
            canvasGroup.alpha = alertPanel != null && alertPanel.activeSelf ? 1f : 0f;
    }

    public bool Show(AlertType type, string message)
    {
        if (type == AlertType.None) return false;

        if (lockByPriority && currentType != AlertType.None && type < currentType)
        {
            // Reject: a higher-priority alert is already active.
            return false;
        }

        currentType = type;

        if (alertPanel && !alertPanel.activeSelf)
            alertPanel.SetActive(true);

        if (alertText)
            alertText.text = message;

        FadeTo(1f, fadeInTime);
        return true;
    }

    public void UpdateIfCurrent(AlertType type, string message)
    {
        if (type != currentType) return;
        if (alertText) alertText.text = message;
    }

    public void Clear(AlertType type)
    {
        if (type != currentType) return;

        currentType = AlertType.None;

        // If we have CanvasGroup, fade out then disable the panel
        if (canvasGroup != null && alertPanel != null)
        {
            FadeTo(0f, fadeOutTime, disableAfter: true);
        }
        else
        {
            // Fallback: immediate hide
            if (alertPanel) alertPanel.SetActive(false);
        }
    }

    public AlertType GetCurrentType() => currentType;

    private void FadeTo(float targetAlpha, float duration, bool disableAfter = false)
    {
        if (canvasGroup == null)
        {
            // No CanvasGroup available; just hard-toggle
            if (alertPanel != null)
                alertPanel.SetActive(targetAlpha > 0.01f);
            return;
        }

        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(FadeRoutine(targetAlpha, duration, disableAfter));
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration, bool disableAfter)
    {
        float start = canvasGroup.alpha;
        float t = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (t < duration)
        {
            float k = t / duration;
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, k);
            t += Time.deltaTime;
            yield return null;
        }
        canvasGroup.alpha = targetAlpha;

        if (disableAfter && alertPanel != null)
            alertPanel.SetActive(false);

        fadeCo = null;
    }
}
