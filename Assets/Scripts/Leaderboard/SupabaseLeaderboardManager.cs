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

        /// <summary>
        /// Returns the existing instance without ever creating one.
        /// Use in OnEnable/OnDisable to avoid recursive prefab instantiation.
        /// </summary>
        public static SupabaseLeaderboardManager InstanceIfExists => _instance;

        /// <summary>
        /// Returns the singleton instance if one exists in the scene.
        /// Never instantiates a prefab — the LeaderboardManager must be placed
        /// in each scene that needs it. This eliminates the cross-class recursive
        /// Instantiate loop that caused "too much recursion" in WebGL.
        /// </summary>
        public static SupabaseLeaderboardManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<SupabaseLeaderboardManager>(FindObjectsInactive.Include);
                }
                return _instance;
            }
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
        public float requestTimeoutSeconds = 15f;

        // Cached States
        public PlayerProfile CurrentPlayer { get; private set; }
        public LeaderboardResponse LatestLeaderboard { get; private set; }
        public bool IsFetchingLeaderboard { get; private set; }
        public bool IsInitializingPlayer { get; private set; }
        public bool IsSubmittingScore { get; private set; }

        // Events
        public event Action<PlayerProfile> OnPlayerProfileUpdated;
        public event Action<LeaderboardResponse> OnLeaderboardLoaded;
        public event Action<string> OnLeaderboardError;
        public event Action<SubmitScoreResponse> OnScoreSubmitted;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
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
            if (IsInitializingPlayer) return;

            string initData = GetTelegramAuthData();
            if (string.IsNullOrEmpty(initData))
            {
                Debug.Log("[SupabaseLeaderboardManager] No Telegram initData available. Player operating in guest mode.");
                onComplete?.Invoke(null);
                return;
            }

            StartCoroutine(InitializePlayerRoutine(initData, onComplete));
        }

        private IEnumerator InitializePlayerRoutine(string initData, Action<PlayerProfileResponse> onComplete)
        {
            IsInitializingPlayer = true;
            string url = $"{supabaseUrl.TrimEnd('/')}/functions/v1/get-or-create-player";
            var payload = new PlayerInitPayload { initData = initData };
            string jsonBody = JsonUtility.ToJson(payload);

            using (UnityWebRequest req = CreatePostRequest(url, jsonBody))
            {
                yield return req.SendWebRequest();

                IsInitializingPlayer = false;

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
                        else
                        {
                            Debug.LogWarning($"[SupabaseLeaderboardManager] InitializePlayer returned error: {response?.error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SupabaseLeaderboardManager] Failed to parse player profile response: {ex.Message}");
                    }
                }
                else
                {
                    string errDetail = ExtractErrorMessage(req);
                    Debug.LogWarning($"[SupabaseLeaderboardManager] InitializePlayer failed: {errDetail}");
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
                // initData is safely transmitted inside JSON body to prevent browser XHR byte-encoding errors
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
                    string errDetail = ExtractErrorMessage(req);
                    string err = $"Network Error: {errDetail}";
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
            if (score < 0 || IsSubmittingScore) return;
            StartCoroutine(SubmitScoreRoutine(score, onComplete, onError));
        }

        private IEnumerator SubmitScoreRoutine(int score, Action<SubmitScoreResponse> onComplete, Action<string> onError)
        {
            IsSubmittingScore = true;
            string initData = GetTelegramAuthData();
            if (string.IsNullOrEmpty(initData))
            {
                Debug.Log("[SupabaseLeaderboardManager] Cannot submit score without Telegram authentication (guest mode).");
                IsSubmittingScore = false;
                onError?.Invoke("Score saved locally (Play via Telegram to join global leaderboard).");
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
                    string errDetail = ExtractErrorMessage(req);
                    string err = $"Score submission error: {errDetail}";
                    Debug.LogWarning($"[SupabaseLeaderboardManager] {err}");
                    onError?.Invoke(err);
                }
            }
        }

        private string ExtractErrorMessage(UnityWebRequest req)
        {
            if (req == null) return "Unknown Error";

            if (req.downloadHandler != null && !string.IsNullOrEmpty(req.downloadHandler.text))
            {
                try
                {
                    var errObj = JsonUtility.FromJson<ErrorResponse>(req.downloadHandler.text);
                    if (errObj != null && !string.IsNullOrEmpty(errObj.error))
                    {
                        return errObj.error;
                    }
                }
                catch { }
            }

            if (req.responseCode > 0)
            {
                return $"HTTP {req.responseCode} - {req.error}";
            }

            return !string.IsNullOrEmpty(req.error) ? req.error : "Connection Failed";
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

