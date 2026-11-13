using UnityEngine;

/// <summary>
/// Placeholder for WebGL input fixes.
/// The actual recursion prevention is handled in index.html via a JavaScript
/// re-entrancy guard on keyboard events, which prevents Telegram's WebView
/// from creating infinite event dispatch loops without breaking Unity's
/// Input System initialization (which requires captureAllKeyboardInput = true).
/// </summary>
public class WebGLInputFix : MonoBehaviour
{
    // The fix is in index.html JavaScript — see the keyboard event re-entrancy guard.
    // We intentionally do NOT set WebGLInput.captureAllKeyboardInput = false here
    // because that prevents the Input System from initializing its keyboard device
    // table, causing "RuntimeError: index out of bounds" during callMain.
}

