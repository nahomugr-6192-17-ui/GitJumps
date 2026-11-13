using UnityEngine;

/// <summary>
/// Placeholder component kept for backward compatibility with scenes that reference it.
/// No runtime logic — the recursion fix is in the singleton Instance getters
/// (SupabaseLeaderboardManager.cs and LeaderboardUIController.cs) which no longer
/// instantiate prefabs via Resources.Load, eliminating cross-class recursive instantiation.
/// </summary>
public class WebGLInputFix : MonoBehaviour
{
}

