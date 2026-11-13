using UnityEngine;

/// <summary>
/// Prevents Unity WebGL from globally intercepting all DOM keyboard events.
/// In WebView environments (like Telegram Mini Apps), global capture causes
/// infinite event dispatching loops resulting in "Maximum call stack size exceeded"
/// or "too much recursion" errors.
///
/// This runs automatically before any scene loads via RuntimeInitializeOnLoadMethod,
/// and also as a MonoBehaviour Awake fallback if placed on a GameObject.
/// </summary>
public class WebGLInputFix : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void DisableGlobalKeyboardCapture()
    {
#if !UNITY_EDITOR && UNITY_WEBGL
        WebGLInput.captureAllKeyboardInput = false;
#endif
    }

    private void Awake()
    {
#if !UNITY_EDITOR && UNITY_WEBGL
        WebGLInput.captureAllKeyboardInput = false;
#endif
    }
}

