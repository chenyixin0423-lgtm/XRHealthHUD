using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// DataFeed drives HUD updates using simulated health data and triggers basic alerts
/// (heart high, goal reached, sedentary reminder).
///
/// This version includes:
/// - Smooth heart-rate/steps simulation using an activity state machine (Rest/Walk/Run/Recover)
/// - Heart-high persistent alert with optional hysteresis/hold time
/// - Goal reached toast that auto-hides
/// - Sedentary (inactivity) reminder based on "no step increase for N minutes"
/// - Centralized alert handling via AlertManager with priority (HeartHigh > Sedentary)
/// - Split heart-rate UI into Label / Value / Unit so only the numeric value can animate and change color
/// - Subtle heart-rate pulse animation (small amplitude, low frequency)
/// </summary>
public class DataFeed : MonoBehaviour
{
    // --- Auto-hide timing for toasts/panels ---
    [SerializeField] private float goalToastSeconds = 30f;    // How long the goal toast stays visible (seconds)
    [SerializeField] private float heartRefreshSeconds = 5f;  // How often to refresh heart alert text (seconds)

    // Optional: hysteresis and "stable safe" hold time before clearing the heart alert
    [SerializeField] private float heartHysteresis = 0f;      // e.g., 10f => safe only when below (threshold - 10)
    [SerializeField] private float clearHoldSeconds = 0f;     // e.g., 5f => stay safe for 5s before clearing (0 disables)

    // Runtime coroutines
    private Coroutine heartLoopCo, goalHideCo;

    [Header("UI Refs")]
    public TMP_Text heartLabelText;   // Text_Heart_Label
    public TMP_Text heartValueText;   // Text_Heart_Value
    public TMP_Text heartUnitText;    // Text_Heart_Unit
    public TMP_Text stepsText;
    public TMP_Text goalText;

    [Header("Managers")]
    [SerializeField] private AlertManager alertManager;

    [Header("HUD Animations")]
    [SerializeField] private bool enableHeartPulse = true;

    [Tooltip("Peak scale for the pulse animation. Keep close to 1.0 for subtle effect.")]
    [SerializeField] private float heartPulseScale = 1.05f;   // smaller amplitude

    [SerializeField] private float heartPulseUpTime = 0.08f;   // seconds
    [SerializeField] private float heartPulseDownTime = 0.14f; // seconds

    [Tooltip("Minimum seconds between pulse animations (e.g., 5 means pulse at most once every 5s).")]
    [SerializeField] private float heartPulseIntervalSeconds = 5f;

    private Coroutine heartPulseCo;
    private float heartPulseTimer = 0f;

    [Header("Heart Value Coloring")]
    [SerializeField] private bool enableHeartValueColor = true;

    [Tooltip("<= this bpm => Green")]
    [SerializeField] private float heartGreenMax = 110f;

    [Tooltip("<= this bpm => Orange, above => Red")]
    [SerializeField] private float heartOrangeMax = 140f;

    [SerializeField] private Color heartLowColor = new Color(0.35f, 0.85f, 0.45f, 1f);   // green
    [SerializeField] private Color heartMidColor = new Color(0.96f, 0.69f, 0.33f, 1f);   // orange
    [SerializeField] private Color heartHighColor = new Color(0.99f, 0.51f, 0.51f, 1f);  // red


    [Header("Label/Unit Styling")]
    [Tooltip("If enabled, force label & unit to stay neutral (won't inherit any previous color changes).")]
    [SerializeField] private bool forceNeutralLabelUnitColor = true;

    [SerializeField] private Color neutralLabelUnitColor = new Color(0.92f, 0.92f, 0.92f, 1f);

    // Heart alert panel/text (kept as fallback; primary path uses AlertManager)
    public GameObject alertPanel;
    public TMP_Text alertText;

    // Goal reached panel (existing)
    public GameObject goalPanel;
    public TMP_Text goalAlertText;

    [Header("Simulation Settings")]
    public bool simulate = true;

    [Tooltip("Main update frequency for the simulation and UI refresh (Hz).")]
    public float updateHz = 1f;

    [Tooltip("Clamp range for simulated heart rate (bpm).")]
    public Vector2 heartRange = new Vector2(58f, 165f);

    [Tooltip("Legacy per-tick step increment range. Kept for backwards compatibility (not used in activity cadence mode).")]
    public int stepIncrementMin = 1;
    public int stepIncrementMax = 6;

    [Header("Alert Thresholds")]
    public float heartHigh = 140f;
    public int stepGoal = 5000;

    [Header("Sedentary Alert")]
    [SerializeField] private bool enableSedentaryAlert = true;

    [Tooltip("Minutes without step increase before showing an inactivity reminder. Use small value (e.g., 0.2) for demo.")]
    [SerializeField] private float sedentaryMinutes = 1f;

    // Internal data
    private float heartRate = 0f;
    private int steps = 0;
    private bool goalAnnounced = false;

    // Update time step derived from updateHz
    private float uiDt = 0f;

    // --- Activity simulation model ---
    private enum ActivityState { Rest, Walk, Run, Recover }

    [Header("Simulation Model (Advanced)")]
    [Tooltip("Enable the activity-state simulation (Rest/Walk/Run/Recover). If false, uses a simpler smoothing model.")]
    [SerializeField] private bool useActivityModel = true;

    [Tooltip("Approximate resting heart rate used by the model. Will be clamped into heartRange.")]
    [SerializeField] private float baselineHeart = 72f;

    [Tooltip("Target heart-rate offsets (bpm) added on top of baseline for Rest/Walk/Run.")]
    [SerializeField] private Vector3 activityHeartOffsets = new Vector3(0f, 18f, 55f); // Rest, Walk, Run offsets

    [Tooltip("Step cadence in steps/second for Rest/Walk/Run.")]
    [SerializeField] private Vector3 activityCadence = new Vector3(0.2f, 2.0f, 3.5f); // Rest, Walk, Run cadence

    [Tooltip("How quickly heart rate moves toward the target (higher = faster response).")]
    [SerializeField] private float heartResponsiveness = 1.6f;

    [Tooltip("Small random drift applied to heart target to avoid being too 'perfect' (bpm).")]
    [SerializeField] private float heartNoiseBpm = 1.5f;

    [Tooltip("How long each activity episode tends to last (seconds). Lower values => more frequent state switching.")]
    [SerializeField] private Vector2 episodeDurationRange = new Vector2(25f, 60f);

    [Tooltip("Chance to transition to a higher intensity state when an episode ends.")]
    [Range(0f, 1f)]
    [SerializeField] private float escalateProbability = 0.55f;

    [Tooltip("Chance to de-escalate to a lower intensity state when an episode ends.")]
    [Range(0f, 1f)]
    [SerializeField] private float deescalateProbability = 0.30f;

    private ActivityState state = ActivityState.Rest;
    private float stateTimer = 0f;
    private float stateDuration = 40f;

    // Fractional step accumulator (cadence-based simulation)
    private float stepsAcc = 0f;

    // Sedentary detection state
    private int lastStepsForSedentary = 0;
    private float sedentaryTimer = 0f;
    private bool sedentaryAnnounced = false;

    void Start()
    {
        // Initialize panels
        if (alertPanel) alertPanel.SetActive(false);
        if (goalPanel) goalPanel.SetActive(false);

        // Initialize UI text (split heart line)
        if (heartLabelText) heartLabelText.text = "Heart Rate:";
        if (heartValueText) heartValueText.text = "---";
        if (heartUnitText) heartUnitText.text = "bpm";
        if (stepsText) stepsText.text = "Steps: ---";
        UpdateGoalLine(false);

        // Keep label/unit neutral
        if (forceNeutralLabelUnitColor)
        {
            if (heartLabelText) heartLabelText.color = neutralLabelUnitColor;
            if (heartUnitText) heartUnitText.color = neutralLabelUnitColor;
        }

        // Initialize simulation values
        baselineHeart = Mathf.Clamp(baselineHeart, heartRange.x, heartRange.y);
        heartRate = baselineHeart;
        steps = 0;
        stepsAcc = 0f;

        // Initialize activity model timers
        PickNewEpisodeDuration();
        stateTimer = 0f;

        // Initialize sedentary detector
        lastStepsForSedentary = steps;
        sedentaryTimer = 0f;
        sedentaryAnnounced = false;

        // Initialize pulse timer
        heartPulseTimer = 0f;

        StartCoroutine(Updater());
    }

    private void PickNewEpisodeDuration()
    {
        stateDuration = Random.Range(
            Mathf.Max(5f, episodeDurationRange.x),
            Mathf.Max(episodeDurationRange.x + 1f, episodeDurationRange.y)
        );
    }

    IEnumerator Updater()
    {
        float hz = Mathf.Max(0.01f, updateHz);
        var wait = new WaitForSeconds(1f / hz);

        while (true)
        {
            uiDt = 1f / hz;

            if (simulate)
            {
                if (useActivityModel) SimulateWithActivityModel(uiDt);
                else SimulateWithSimpleSmoothing(uiDt);
            }

            // --- Update HUD texts (split heart line) ---
            float hrInt = Mathf.Round(heartRate);

            if (heartLabelText) heartLabelText.text = "Heart Rate:";
            if (heartValueText) heartValueText.text = $"{hrInt:F0}";
            if (heartUnitText) heartUnitText.text = "bpm";

            // Force label/unit neutral each frame (optional but robust)
            if (forceNeutralLabelUnitColor)
            {
                if (heartLabelText) heartLabelText.color = neutralLabelUnitColor;
                if (heartUnitText) heartUnitText.color = neutralLabelUnitColor;
            }

            // Color ONLY the numeric value
            if (enableHeartValueColor && heartValueText != null)
                ApplyHeartValueColor(hrInt);

            // Pulse ONLY the numeric value at a fixed interval (not on every value change)
            if (enableHeartPulse && heartValueText != null)
            {
                heartPulseTimer += uiDt;
                if (heartPulseTimer >= Mathf.Max(0.1f, heartPulseIntervalSeconds))
                {
                    heartPulseTimer = 0f;
                    TriggerHeartPulse();
                }
            }

            if (stepsText) stepsText.text = $"Steps: {steps}";

            // --- Sedentary detection ---
            if (enableSedentaryAlert)
            {
                if (steps > lastStepsForSedentary)
                {
                    lastStepsForSedentary = steps;
                    sedentaryTimer = 0f;
                    sedentaryAnnounced = false;
                }
                else
                {
                    sedentaryTimer += uiDt;
                    if (!sedentaryAnnounced && sedentaryTimer >= sedentaryMinutes * 60f)
                    {
                        sedentaryAnnounced = true;
                        ShowSedentaryAlert();
                    }
                }
            }

            // Goal reached: show only once, then auto-hide
            if (!goalAnnounced && steps >= stepGoal)
            {
                goalAnnounced = true;
                UpdateGoalLine(true);
                ShowGoalAlert();
            }

            // Heart high alert: show while above threshold
            if (heartRate >= heartHigh)
            {
                ShowHeartAlert();
            }

            yield return wait;
        }
    }

    private void ApplyHeartValueColor(float hr)
    {
        if (heartValueText == null) return;

        // Ensure thresholds are ordered correctly even if user edits inspector badly
        float gMax = Mathf.Min(heartGreenMax, heartOrangeMax);
        float oMax = Mathf.Max(heartGreenMax, heartOrangeMax);

        if (hr <= gMax) heartValueText.color = heartLowColor;         // Green
        else if (hr <= oMax) heartValueText.color = heartMidColor;    // Orange
        else heartValueText.color = heartHighColor;                   // Red
    }


    /// <summary>
    /// Simple model (fallback): gently drift heart rate and steps with smoothing.
    /// </summary>
    private void SimulateWithSimpleSmoothing(float dt)
    {
        float target = baselineHeart + Random.Range(-6f, 12f);
        target = Mathf.Clamp(target, heartRange.x, heartRange.y);

        float alpha = 1f - Mathf.Exp(-heartResponsiveness * dt);
        heartRate = Mathf.Lerp(heartRate, target, alpha);

        int inc = Random.Range(stepIncrementMin, stepIncrementMax + 1);
        steps += Mathf.Max(0, inc);
    }

    /// <summary>
    /// Activity model: Rest/Walk/Run/Recover with episode durations.
    /// - Heart rate moves smoothly toward a state-dependent target + noise.
    /// - Steps are driven by cadence (steps/second) with fractional accumulation.
    /// </summary>
    private void SimulateWithActivityModel(float dt)
    {
        stateTimer += dt;
        if (stateTimer >= stateDuration)
        {
            stateTimer = 0f;
            TransitionActivityState();
            PickNewEpisodeDuration();
        }

        float heartTarget = baselineHeart;

        switch (state)
        {
            case ActivityState.Rest:
                heartTarget = baselineHeart + activityHeartOffsets.x;
                break;
            case ActivityState.Walk:
                heartTarget = baselineHeart + activityHeartOffsets.y;
                break;
            case ActivityState.Run:
                heartTarget = baselineHeart + activityHeartOffsets.z;
                break;
            case ActivityState.Recover:
                heartTarget = baselineHeart + Mathf.Max(4f, activityHeartOffsets.y * 0.4f);
                break;
        }

        heartTarget += Random.Range(-heartNoiseBpm, heartNoiseBpm);

        heartTarget = Mathf.Clamp(heartTarget, heartRange.x, heartRange.y);
        float alpha = 1f - Mathf.Exp(-heartResponsiveness * dt);
        heartRate = Mathf.Lerp(heartRate, heartTarget, alpha);

        float cadence = 0f;
        switch (state)
        {
            case ActivityState.Rest: cadence = Mathf.Max(0f, activityCadence.x); break;
            case ActivityState.Walk: cadence = Mathf.Max(0f, activityCadence.y); break;
            case ActivityState.Run: cadence = Mathf.Max(0f, activityCadence.z); break;
            case ActivityState.Recover: cadence = Mathf.Max(0f, activityCadence.y * 0.6f); break;
        }

        float noise = Random.Range(0.9f, 1.1f);
        float cadenceScaled = cadence * noise;

        stepsAcc += cadenceScaled * dt;
        int add = Mathf.FloorToInt(stepsAcc);
        if (add > 0)
        {
            steps += add;
            stepsAcc -= add;
        }
    }

    /// <summary>
    /// Simple stochastic transitions between Rest/Walk/Run/Recover.
    /// </summary>
    private void TransitionActivityState()
    {
        float r = Random.value;

        if (state == ActivityState.Rest)
        {
            if (r < escalateProbability) state = (Random.value < 0.15f) ? ActivityState.Run : ActivityState.Walk;
            else state = ActivityState.Rest;
            return;
        }

        if (state == ActivityState.Walk)
        {
            if (r < escalateProbability) state = ActivityState.Run;
            else if (r < escalateProbability + deescalateProbability) state = ActivityState.Rest;
            else state = ActivityState.Recover;
            return;
        }

        if (state == ActivityState.Run)
        {
            state = (Random.value < 0.8f) ? ActivityState.Recover : ActivityState.Walk;
            return;
        }

        state = (Random.value < 0.6f) ? ActivityState.Rest : ActivityState.Walk;
    }

    private void UpdateGoalLine(bool achieved)
    {
        if (!goalText) return;
        goalText.text = achieved ? $"Goal: {stepGoal} (achieved)" : $"Goal: {stepGoal}";
    }

    // --- Heart high alert (persistent panel; refresh text periodically; closes when safe) ---
    private void ShowHeartAlert()
    {
        string msg = $"Heart rate high ({heartRate:F0} bpm). Take a short rest.";

        if (alertManager != null)
        {
            alertManager.Show(AlertManager.AlertType.HeartHigh, msg);
        }
        else
        {
            if (alertPanel && !alertPanel.activeSelf) alertPanel.SetActive(true);
            if (alertText) alertText.text = msg;
        }

        if (heartLoopCo != null) StopCoroutine(heartLoopCo);
        heartLoopCo = StartCoroutine(HeartAlertLoop());
    }

    private IEnumerator HeartAlertLoop()
    {
        float safeThreshold = heartHigh - Mathf.Max(0f, heartHysteresis);
        float stable = 0f;

        while (true)
        {
            if (alertManager != null)
            {
                alertManager.UpdateIfCurrent(
                    AlertManager.AlertType.HeartHigh,
                    $"Heart rate high ({heartRate:F0} bpm). Take a short rest."
                );
            }
            else
            {
                if (alertText) alertText.text = $"Heart rate high ({heartRate:F0} bpm). Take a short rest.";
            }

            float t = 0f;
            while (t < Mathf.Max(0.1f, heartRefreshSeconds))
            {
                bool safeNow = heartHysteresis > 0f
                    ? (heartRate < safeThreshold)
                    : (heartRate < heartHigh);

                if (safeNow)
                {
                    if (clearHoldSeconds <= 0f)
                    {
                        if (alertManager != null) alertManager.Clear(AlertManager.AlertType.HeartHigh);
                        else if (alertPanel) alertPanel.SetActive(false);

                        heartLoopCo = null;
                        yield break;
                    }
                    else
                    {
                        stable += Time.deltaTime;
                        if (stable >= clearHoldSeconds)
                        {
                            if (alertManager != null) alertManager.Clear(AlertManager.AlertType.HeartHigh);
                            else if (alertPanel) alertPanel.SetActive(false);

                            heartLoopCo = null;
                            yield break;
                        }
                    }
                }
                else
                {
                    stable = 0f;
                }

                t += Time.deltaTime;
                yield return null;
            }
        }
    }

    // --- Sedentary reminder (uses the shared alert panel with lower priority) ---
    private void ShowSedentaryAlert()
    {
        const string msg = "You've been inactive for a while. Consider standing up or taking a short walk.";

        if (alertManager != null)
        {
            alertManager.Show(AlertManager.AlertType.Sedentary, msg);
            return;
        }

        if (alertPanel && !alertPanel.activeSelf) alertPanel.SetActive(true);
        if (alertText) alertText.text = msg;
    }

    // --- Goal reached toast (fixed duration then auto-hide) ---
    private void ShowGoalAlert()
    {
        if (goalAlertText) goalAlertText.text = "Goal reached!";
        if (goalPanel && !goalPanel.activeSelf)
        {
            goalPanel.SetActive(true);
            if (goalHideCo != null) StopCoroutine(goalHideCo);
            goalHideCo = StartCoroutine(HideAfterSeconds(goalPanel, goalToastSeconds));
        }
    }

    private IEnumerator HideAfterSeconds(GameObject panel, float seconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, seconds));
        if (panel) panel.SetActive(false);
    }

    // Legacy button hooks (safe to keep even if not used)
    public void HideAlert()
    {
        if (alertManager != null)
        {
            var current = alertManager.GetCurrentType();
            if (current != AlertManager.AlertType.None)
                alertManager.Clear(current);
        }
        else
        {
            if (alertPanel) alertPanel.SetActive(false);
        }

        if (heartLoopCo != null) { StopCoroutine(heartLoopCo); heartLoopCo = null; }
    }

    public void HideGoalPanel()
    {
        if (goalPanel) goalPanel.SetActive(false);
        if (goalHideCo != null) { StopCoroutine(goalHideCo); goalHideCo = null; }
    }

    private void TriggerHeartPulse()
    {
        if (heartValueText == null) return;

        if (heartPulseCo != null) StopCoroutine(heartPulseCo);
        heartPulseCo = StartCoroutine(HeartPulseRoutine());
    }

    private IEnumerator HeartPulseRoutine()
    {
        Transform t = heartValueText.transform;
        Vector3 baseScale = Vector3.one;
        Vector3 peakScale = Vector3.one * Mathf.Max(1.01f, heartPulseScale);

        float up = Mathf.Max(0.01f, heartPulseUpTime);
        float time = 0f;
        while (time < up)
        {
            float k = time / up;
            t.localScale = Vector3.Lerp(baseScale, peakScale, k);
            time += Time.deltaTime;
            yield return null;
        }
        t.localScale = peakScale;

        float down = Mathf.Max(0.01f, heartPulseDownTime);
        time = 0f;
        while (time < down)
        {
            float k = time / down;
            t.localScale = Vector3.Lerp(peakScale, baseScale, k);
            time += Time.deltaTime;
            yield return null;
        }
        t.localScale = baseScale;

        heartPulseCo = null;
    }

#if UNITY_EDITOR
    void Update()
    {
        // Editor-only reset helper
        if (Input.GetKeyDown(KeyCode.R))
        {
            steps = 0;
            stepsAcc = 0f;
            goalAnnounced = false;
            UpdateGoalLine(false);

            lastStepsForSedentary = steps;
            sedentaryTimer = 0f;
            sedentaryAnnounced = false;

            heartPulseTimer = 0f;
        }
    }
#endif
}
