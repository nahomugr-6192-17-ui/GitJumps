using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GitJumps.Leaderboard
{
    public class SupabaseLeaderboardManager : MonoBehaviour
    {
        private static SupabaseLeaderboardManager _instance;
        public static SupabaseLeaderboardManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<SupabaseLeaderboardManager>();
                    if (_instance == null)
                    {
                        var prefab = Resources.Load<GameObject>("LeaderboardManager");
                        if (prefab != null)
                        {
                            var go = Instantiate(prefab);
                            go.name = "LeaderboardManager";
                            _instance = go.GetComponent<SupabaseLeaderboardManager>();
                        }
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Supabase Configuration")]
        [Tooltip("The Supabase project URL, e.g. https://xyzcompany.supabase.co")]
        public string supabaseUrl = "https://suqrmebduvkeqhshasff.supabase.co";

        [Tooltip("The public Supabase anon key")]
        public string supabaseAnonKey = "sb_publishable_P9Jkwm8KLreE_EhVWh_Kjg_yRhD8rRK";

        [Header("Authentication & Development")]
        [Tooltip("When true, allows development mock authentication in Editor / dev builds")]
        public bool isDevelopmentMode = true;

        public long devTelegramId = 1001;
        public string devDisplayName = "Local Developer";

        [Header("Network Settings")]
        public float requestTimeoutSeconds = 8f;

        // Cached States
        public PlayerProfile CurrentPlayer { get; private set; }
        public LeaderboardResponse LatestLeaderboard { get; private set; }
        public bool IsFetchingLeaderboard { get; private set; }
        public bool IsSubmittingScore { get; private set; }

        // Events
        public event Action<PlayerProfile> OnPlayerProfileUpdated;
        public event Action<LeaderboardResponse> OnLeaderboardLoaded;
        public event Action<string> OnLeaderboardError;
        public event Action<SubmitScoreResponse> OnScoreSubmitted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Automatically initialize player profile if credentials are configured
            if (IsConfigured())
            {
                InitializePlayer();
            }
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrEmpty(supabaseUrl) &&
                   !supabaseUrl.Contains("your-project-id") &&
                   !string.IsNullOrEmpty(supabaseAnonKey);
        }

        public string GetTelegramAuthData()
        {
            return TelegramWebBridge.GetInitData(isDevelopmentMode, devTelegramId, devDisplayName);
        }

        /// <summary>
        /// Registers or retrieves the current player's profile and global rank.
        /// </summary>
        public void InitializePlayer(Action<PlayerProfileResponse> onComplete = null)
        {
            string initData = GetTelegramAuthData();
            if (string.IsNullOrEmpty(initData))
            {
                Debug.LogWarning("[SupabaseLeaderboardManager] No Telegram initData available. Player will operate in guest mode until authenticated.");
                return;
            }

            StartCoroutine(InitializePlayerRoutine(initData, onComplete));
        }

        private IEnumerator InitializePlayerRoutine(string initData, Action<PlayerProfileResponse> onComplete)
        {
            string url = $"{supabaseUrl.TrimEnd('/')}/functions/v1/get-or-create-player";
            var payload = new PlayerInitPayload { initData = initData };
            string jsonBody = JsonUtility.ToJson(payload);

            using (UnityWebRequest req = CreatePostRequest(url, jsonBody))
            {
                yield return req.SendWebRequest();

                PlayerProfileResponse response = null;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        response = JsonUtility.FromJson<PlayerProfileResponse>(req.downloadHandler.text);
                        if (response != null && response.success && response.player != null)
                        {
                            CurrentPlayer = response.player;
                            OnPlayerProfileUpdated?.Invoke(CurrentPlayer);
                            Debug.Log($"[SupabaseLeaderboardManager] Player authenticated: {CurrentPlayer.display_name} ({CurrentPlayer.player_code}), Rank #{CurrentPlayer.global_rank}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SupabaseLeaderboardManager] Failed to parse player profile response: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[SupabaseLeaderboardManager] InitializePlayer failed: {req.responseCode} - {req.error}\n{req.downloadHandler.text}");
                }

                onComplete?.Invoke(response);
            }
        }

        /// <summary>
        /// Retrieves the Top 100 global leaderboard and the current player's rank.
        /// </summary>
        public void FetchLeaderboard(Action<LeaderboardResponse> onComplete = null, Action<string> onError = null)
        {
            if (IsFetchingLeaderboard) return;
            StartCoroutine(FetchLeaderboardRoutine(onComplete, onError));
        }

        private IEnumerator FetchLeaderboardRoutine(Action<LeaderboardResponse> onComplete, Action<string> onError)
        {
            IsFetchingLeaderboard = true;
            string initData = GetTelegramAuthData();
            string url = $"{supabaseUrl.TrimEnd('/')}/functions/v1/get-leaderboard";

            var payload = new PlayerInitPayload { initData = initData };
            string jsonBody = JsonUtility.ToJson(payload);

            using (UnityWebRequest req = CreatePostRequest(url, jsonBody))
            {
                if (!string.IsNullOrEmpty(initData))
                {
                    req.SetRequestHeader("x-telegram-init-data", initData);
                }

                yield return req.SendWebRequest();

                IsFetchingLeaderboard = false;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    LeaderboardResponse res = null;
                    try
                    {
                        res = JsonUtility.FromJson<LeaderboardResponse>(req.downloadHandler.text);
                    }
                    catch (Exception ex)
                    {
                        string err = $"JSON Parse Error: {ex.Message}";
                        Debug.LogError($"[SupabaseLeaderboardManager] {err}");
                        onError?.Invoke(err);
                        OnLeaderboardError?.Invoke(err);
                        yield break;
                    }

                    if (res != null && res.success)
                    {
                        LatestLeaderboard = res;
                        if (res.current_player != null)
                        {
                            CurrentPlayer = res.current_player;
                            OnPlayerProfileUpdated?.Invoke(CurrentPlayer);
                        }
                        OnLeaderboardLoaded?.Invoke(res);
                        onComplete?.Invoke(res);
                    }
                    else
                    {
                        string err = res?.error ?? "Unknown leaderboard error";
                        onError?.Invoke(err);
                        OnLeaderboardError?.Invoke(err);
                    }
                }
                else
                {
                    string err = $"Network Error: {req.error}";
                    Debug.LogWarning($"[SupabaseLeaderboardManager] FetchLeaderboard failed: {err}");
                    onError?.Invoke(err);
                    OnLeaderboardError?.Invoke(err);
                }
            }
        }

        /// <summary>
        /// Submits the final game score. Guaranteed atomic on the server; lower scores will not overwrite higher bests.
        /// </summary>
        public void SubmitScore(int score, Action<SubmitScoreResponse> onComplete = null, Action<string> onError = null)
        {
            if (score < 0) return;
            StartCoroutine(SubmitScoreRoutine(score, onComplete, onError));
        }

        private IEnumerator SubmitScoreRoutine(int score, Action<SubmitScoreResponse> onComplete, Action<string> onError)
        {
            IsSubmittingScore = true;
            string initData = GetTelegramAuthData();
            if (string.IsNullOrEmpty(initData))
            {
                Debug.LogWarning("[SupabaseLeaderboardManager] Cannot submit score without Telegram authentication.");
                IsSubmittingScore = false;
                onError?.Invoke("No Telegram authentication data found.");
                yield break;
            }

            string url = $"{supabaseUrl.TrimEnd('/')}/functions/v1/submit-score";
            var payload = new ScoreSubmissionPayload
            {
                initData = initData,
                score = score
            };
            string jsonBody = JsonUtility.ToJson(payload);

            using (UnityWebRequest req = CreatePostRequest(url, jsonBody))
            {
                yield return req.SendWebRequest();

                IsSubmittingScore = false;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    SubmitScoreResponse res = null;
                    try
                    {
                        res = JsonUtility.FromJson<SubmitScoreResponse>(req.downloadHandler.text);
                    }
                    catch (Exception ex)
                    {
                        string err = $"JSON Parse Error: {ex.Message}";
                        Debug.LogError($"[SupabaseLeaderboardManager] {err}");
                        onError?.Invoke(err);
                        yield break;
                    }

                    if (res != null && res.success && res.player != null)
                    {
                        CurrentPlayer = res.player;
                        OnPlayerProfileUpdated?.Invoke(CurrentPlayer);
                        OnScoreSubmitted?.Invoke(res);
                        Debug.Log($"[SupabaseLeaderboardManager] Score {score} submitted. Best: {res.player.best_score}, Rank: #{res.player.global_rank}");
                        onComplete?.Invoke(res);
                    }
                    else
                    {
                        string err = res?.error ?? "Score submission error";
                        onError?.Invoke(err);
                    }
                }
                else
                {
                    string err = $"Score submission network error: {req.error}";
                    Debug.LogWarning($"[SupabaseLeaderboardManager] {err}");
                    onError?.Invoke(err);
                }
            }
        }

        private UnityWebRequest CreatePostRequest(string url, string jsonBody)
        {
            UnityWebRequest req = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(supabaseAnonKey))
            {
                req.SetRequestHeader("apikey", supabaseAnonKey);
                req.SetRequestHeader("Authorization", $"Bearer {supabaseAnonKey}");
            }

            req.timeout = Mathf.RoundToInt(requestTimeoutSeconds);
            return req;
        }
    }
}

