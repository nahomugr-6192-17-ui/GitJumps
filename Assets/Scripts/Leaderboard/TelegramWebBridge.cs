using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GitJumps.Leaderboard
{
    public static class TelegramWebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string GetTelegramInitData();

        [DllImport("__Internal")]
        private static extern int HasTelegramWebApp();
#endif

        /// <summary>
        /// Retrieves the Telegram WebApp initData string.
        /// In WebGL, reads from the JavaScript Telegram WebApp API.
        /// In Editor/Dev mode, returns a development mock string if allowed.
        /// </summary>
        public static string GetInitData(bool allowDevMode, long devTelegramId = 1001, string devDisplayName = "Local Developer")
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                string initData = GetTelegramInitData();
                if (!string.IsNullOrEmpty(initData))
                {
                    return initData;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelegramWebBridge] WebGL GetTelegramInitData failed: {ex.Message}");
            }

            // In WebGL builds outside Telegram, only allow dev mock data if explicitly a debug build
            if (!Debug.isDebugBuild)
            {
                return string.Empty;
            }
#endif

            // In Editor or when development mode is explicitly enabled
            if (allowDevMode)
            {
                string encodedName = Uri.EscapeDataString(devDisplayName);
                return $"dev:{devTelegramId}:{encodedName}";
            }

            return string.Empty;
        }

        public static bool IsRunningInTelegram()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return HasTelegramWebApp() == 1;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}

