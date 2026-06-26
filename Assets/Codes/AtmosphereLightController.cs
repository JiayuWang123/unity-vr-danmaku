using UnityEngine;

public enum AtmosphereMode
{
    Neutral,
    Excited,
    Tension,
    Goal
}

public class AtmosphereLightController : MonoBehaviour
{
    [Header("场景主光")]
    public Light directionalLight;

    [Header("当前模式（只读）")]
    public AtmosphereMode currentMode = AtmosphereMode.Neutral;

    [Header("过渡速度")]
    public float transitionSpeed = 1.5f;

    [Header("Neutral 平静")]
    public Color neutralLightColor = new Color(1f, 0.95f, 0.85f);
    public Color neutralAmbientColor = new Color(0.21f, 0.23f, 0.26f);
    public float neutralIntensity = 1f;

    [Header("Excited 高潮")]
    public Color excitedLightColor = new Color(1f, 0.85f, 0.5f);
    public Color excitedAmbientColor = new Color(0.3f, 0.2f, 0.1f);
    public float excitedIntensity = 1.3f;

    [Header("Tension 紧张")]
    public Color tensionLightColor = new Color(0.6f, 0.75f, 1f);
    public Color tensionAmbientColor = new Color(0.1f, 0.15f, 0.3f);
    public float tensionIntensity = 0.7f;

    [Header("Goal 进球闪一下")]
    public Color goalLightColor = new Color(1f, 0.9f, 0.3f);
    public float goalFlashIntensity = 2f;
    public float goalFlashDuration = 0.35f;

    private Color targetLightColor;
    private Color targetAmbientColor;
    private float targetIntensity;
    private float goalFlashTimer;
    private AtmosphereMode afterGoalMode = AtmosphereMode.Excited;

    private void Start()
    {
        if (directionalLight == null)
            directionalLight = FindObjectOfType<Light>();

        ApplyModeInstant(AtmosphereMode.Neutral);
    }

    private void Update()
    {
        if (directionalLight == null)
            return;

        if (goalFlashTimer > 0f)
        {
            goalFlashTimer -= Time.deltaTime;
            if (goalFlashTimer <= 0f)
                SetMode(afterGoalMode);
        }

        directionalLight.color = Color.Lerp(
            directionalLight.color, targetLightColor, Time.deltaTime * transitionSpeed);

        directionalLight.intensity = Mathf.Lerp(
            directionalLight.intensity, targetIntensity, Time.deltaTime * transitionSpeed);

        RenderSettings.ambientSkyColor = Color.Lerp(
            RenderSettings.ambientSkyColor, targetAmbientColor, Time.deltaTime * transitionSpeed);
    }

    public void SetMode(AtmosphereMode mode)
    {
        currentMode = mode;
        goalFlashTimer = 0f;

        switch (mode)
        {
            case AtmosphereMode.Excited:
                targetLightColor = excitedLightColor;
                targetAmbientColor = excitedAmbientColor;
                targetIntensity = excitedIntensity;
                break;
            case AtmosphereMode.Tension:
                targetLightColor = tensionLightColor;
                targetAmbientColor = tensionAmbientColor;
                targetIntensity = tensionIntensity;
                break;
            default:
                targetLightColor = neutralLightColor;
                targetAmbientColor = neutralAmbientColor;
                targetIntensity = neutralIntensity;
                break;
        }
    }

    public void TriggerGoal()
    {
        currentMode = AtmosphereMode.Goal;
        targetLightColor = goalLightColor;
        targetIntensity = goalFlashIntensity;
        goalFlashTimer = goalFlashDuration;
    }

    private void ApplyModeInstant(AtmosphereMode mode)
    {
        SetMode(mode);
        if (directionalLight != null)
        {
            directionalLight.color = targetLightColor;
            directionalLight.intensity = targetIntensity;
        }
        RenderSettings.ambientSkyColor = targetAmbientColor;
    }
}
