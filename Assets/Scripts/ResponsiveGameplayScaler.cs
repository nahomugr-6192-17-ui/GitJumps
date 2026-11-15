using System;
using System.Runtime.InteropServices;
using UnityEngine;

public enum DeviceLayoutMode
{
    Auto,
    Desktop,
    Tablet,
    Mobile
}

public class ResponsiveGameplayScaler : MonoBehaviour
{
    private static ResponsiveGameplayScaler _instance;
    public static ResponsiveGameplayScaler Instance => _instance;

    [Header("Layout Settings")]
    [Tooltip("Leave as Auto for automatic runtime detection. Set explicitly to test in Editor.")]
    public DeviceLayoutMode layoutOverride = DeviceLayoutMode.Auto;

    [Header("Scale Multipliers")]
    [Range(0.5f, 1.0f)] public float mobileScaleMultiplier = 0.72f;
    [Range(0.6f, 1.0f)] public float tabletScaleMultiplier = 0.85f;
    public const float DesktopScaleMultiplier = 1.0f;

    public static DeviceLayoutMode ActiveLayoutMode { get; private set; } = DeviceLayoutMode.Desktop;
    public static float CurrentScaleFactor { get; private set; } = 1.0f;

    public static event Action<DeviceLayoutMode, float> OnLayoutChanged;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int GetDeviceFormFactor();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        if (_instance == null)
        {
            GameObject go = new GameObject("ResponsiveGameplayScaler");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ResponsiveGameplayScaler>();
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        UpdateLayout();
    }

    private void Update()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            UpdateLayout();
        }
    }

    public void UpdateLayout()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        DeviceLayoutMode detectedMode = DetermineLayoutMode();
        ActiveLayoutMode = detectedMode;

        switch (detectedMode)
        {
            case DeviceLayoutMode.Mobile:
                CurrentScaleFactor = mobileScaleMultiplier;
                break;
            case DeviceLayoutMode.Tablet:
                CurrentScaleFactor = tabletScaleMultiplier;
                break;
            case DeviceLayoutMode.Desktop:
            default:
                CurrentScaleFactor = DesktopScaleMultiplier;
                break;
        }

        OnLayoutChanged?.Invoke(ActiveLayoutMode, CurrentScaleFactor);
    }

    private DeviceLayoutMode DetermineLayoutMode()
    {
        if (layoutOverride != DeviceLayoutMode.Auto)
        {
            return layoutOverride;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            int formFactor = GetDeviceFormFactor();
            if (formFactor == 1) return DeviceLayoutMode.Mobile;
            if (formFactor == 2) return DeviceLayoutMode.Tablet;
            return DeviceLayoutMode.Desktop;
        }
        catch
        {
            // Fallback to Unity runtime detection
        }
#endif

#if UNITY_EDITOR
        // In Editor, detect based on Game View resolution
        float minDim = Mathf.Min(Screen.width, Screen.height);
        float maxDim = Mathf.Max(Screen.width, Screen.height);
        float aspect = maxDim / Mathf.Max(1f, minDim);

        if (minDim <= 500)
            return DeviceLayoutMode.Mobile;
        if (minDim <= 750 && aspect < 1.7f)
            return DeviceLayoutMode.Tablet;

        return DeviceLayoutMode.Desktop;
#else
        if (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld)
        {
            float minDim = Mathf.Min(Screen.width, Screen.height);
            float maxDim = Mathf.Max(Screen.width, Screen.height);
            float aspect = maxDim / Mathf.Max(1f, minDim);

            // Tablets have 4:3, 3:2, or 16:10 aspect ratios or larger shortest dimensions
            if (minDim >= 600 || aspect < 1.6f)
                return DeviceLayoutMode.Tablet;

            return DeviceLayoutMode.Mobile;
        }

        return DeviceLayoutMode.Desktop;
#endif
    }

    public static bool IsDesktopLayout()
    {
        return ActiveLayoutMode == DeviceLayoutMode.Desktop;
    }

    public static bool IsTabletLayout()
    {
        return ActiveLayoutMode == DeviceLayoutMode.Tablet;
    }

    public static bool IsMobileLayout()
    {
        return ActiveLayoutMode == DeviceLayoutMode.Mobile;
    }
}
