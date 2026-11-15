using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitJumps.Leaderboard
{
    [RequireComponent(typeof(UIDocument))]
    public class LeaderboardUIController : MonoBehaviour
    {
        private static LeaderboardUIController _instance;

        /// <summary>
        /// Returns the existing instance without creating one. Safe for use
        /// during Awake/OnEnable/OnDisable to prevent recursive instantiation.
        /// </summary>
        public static LeaderboardUIController InstanceIfExists => _instance;

        /// <summary>
        /// Returns the singleton instance if one exists in the scene.
        /// Never instantiates a prefab — the LeaderboardManager must be placed
        /// in each scene that needs it.
        /// </summary>
        public static LeaderboardUIController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<LeaderboardUIController>(FindObjectsInactive.Include);
                }
                return _instance;
            }
        }

        private UIDocument uiDocument;
        private VisualElement rootElement;
        private bool _isUIInitialized = false;

        // UI Elements
        private VisualElement leaderboardOverlay;
        private Button closeButton;
        private Button refreshButton;
        private ScrollView scrollView;

        // Personal Profile Elements
        private Label profileDisplayName;
        private Label profilePlayerCode;
        private Label profileBestScore;
        private Label profileGlobalRank;

        // State Containers
        private VisualElement loadingState;
        private VisualElement errorState;
        private VisualElement emptyState;
        private Label errorMessageLabel;

        // Medal Sprites for WebGL
        private Sprite goldMedalSprite;
        private Sprite silverMedalSprite;
        private Sprite bronzeMedalSprite;

        private void LoadMedalSprites()
        {
            if (goldMedalSprite == null)
                goldMedalSprite = Resources.Load<Sprite>("Medals/Medal_Gold");
            if (silverMedalSprite == null)
                silverMedalSprite = Resources.Load<Sprite>("Medals/Medal_Silver");
            if (bronzeMedalSprite == null)
                bronzeMedalSprite = Resources.Load<Sprite>("Medals/Medal_Bronze");
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            _instance = this;

            LoadMedalSprites();
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null)
            {
                uiDocument.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnEnable()
        {
            // Subscribe to manager events safely without touching the visual tree yet
            var manager = SupabaseLeaderboardManager.InstanceIfExists;
            if (manager != null)
            {
                manager.OnLeaderboardLoaded -= HandleLeaderboardLoaded;
                manager.OnLeaderboardLoaded += HandleLeaderboardLoaded;

                manager.OnLeaderboardError -= HandleLeaderboardError;
                manager.OnLeaderboardError += HandleLeaderboardError;

                manager.OnPlayerProfileUpdated -= HandlePlayerProfileUpdated;
                manager.OnPlayerProfileUpdated += HandlePlayerProfileUpdated;

                if (_isUIInitialized && manager.CurrentPlayer != null)
                {
                    UpdateProfileDisplay(manager.CurrentPlayer);
                }
            }
        }

        private void OnDisable()
        {
            if (_isUIInitialized)
            {
                UnregisterCallbacks();
            }

            var manager = SupabaseLeaderboardManager.InstanceIfExists;
            if (manager != null)
            {
                manager.OnLeaderboardLoaded -= HandleLeaderboardLoaded;
                manager.OnLeaderboardError -= HandleLeaderboardError;
                manager.OnPlayerProfileUpdated -= HandlePlayerProfileUpdated;
            }
        }

        private void EnsureUIInitialized()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            if (!uiDocument.enabled) uiDocument.enabled = true;

            if (rootElement == uiDocument.rootVisualElement && _isUIInitialized) return;

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null) return;

            QueryUIElements();
            RegisterCallbacks();
            _isUIInitialized = true;
        }

        private void QueryUIElements()
        {
            leaderboardOverlay = rootElement.Q<VisualElement>("LeaderboardOverlay");
            closeButton = rootElement.Q<Button>("LeaderboardCloseButton");
            refreshButton = rootElement.Q<Button>("LeaderboardRefreshButton");
            scrollView = rootElement.Q<ScrollView>("LeaderboardScrollView");

            profileDisplayName = rootElement.Q<Label>("ProfileDisplayName");
            profilePlayerCode = rootElement.Q<Label>("ProfilePlayerCode");
            profileBestScore = rootElement.Q<Label>("ProfileBestScore");
            profileGlobalRank = rootElement.Q<Label>("ProfileGlobalRank");

            loadingState = rootElement.Q<VisualElement>("LoadingState");
            errorState = rootElement.Q<VisualElement>("ErrorState");
            emptyState = rootElement.Q<VisualElement>("EmptyState");
            errorMessageLabel = rootElement.Q<Label>("ErrorMessageLabel");
        }

        private void RegisterCallbacks()
        {
            if (closeButton != null)
            {
                closeButton.clicked -= HideLeaderboard;
                closeButton.clicked += HideLeaderboard;
            }

            if (refreshButton != null)
            {
                refreshButton.clicked -= RefreshLeaderboard;
                refreshButton.clicked += RefreshLeaderboard;
            }
        }

        private void UnregisterCallbacks()
        {
            if (closeButton != null)
                closeButton.clicked -= HideLeaderboard;

            if (refreshButton != null)
                refreshButton.clicked -= RefreshLeaderboard;
        }

        public void ShowLeaderboard()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null && !uiDocument.enabled)
            {
                uiDocument.enabled = true;
            }

            EnsureUIInitialized();

            if (leaderboardOverlay != null)
            {
                leaderboardOverlay.style.display = DisplayStyle.Flex;
            }

            var manager = SupabaseLeaderboardManager.Instance;
            if (manager != null)
            {
                manager.OnLeaderboardLoaded -= HandleLeaderboardLoaded;
                manager.OnLeaderboardLoaded += HandleLeaderboardLoaded;
                manager.OnLeaderboardError -= HandleLeaderboardError;
                manager.OnLeaderboardError += HandleLeaderboardError;
                manager.OnPlayerProfileUpdated -= HandlePlayerProfileUpdated;
                manager.OnPlayerProfileUpdated += HandlePlayerProfileUpdated;

                if (manager.CurrentPlayer != null)
                {
                    UpdateProfileDisplay(manager.CurrentPlayer);
                }
                else
                {
                    string authData = manager.GetTelegramAuthData();
                    if (string.IsNullOrEmpty(authData))
                    {
                        SetProfileGuestState();
                    }
                    else
                    {
                        SetProfileLoadingState();
                        if (!manager.IsInitializingPlayer)
                        {
                            manager.InitializePlayer((res) =>
                            {
                                if (res != null && res.player != null)
                                {
                                    UpdateProfileDisplay(res.player);
                                }
                                else
                                {
                                    SetProfileGuestState();
                                }
                            });
                        }
                    }
                }
            }
            else
            {
                SetProfileGuestState();
            }

            RefreshLeaderboard();
        }

        public void HideLeaderboard()
        {
            if (leaderboardOverlay != null)
            {
                leaderboardOverlay.style.display = DisplayStyle.None;
            }

            if (uiDocument != null)
            {
                uiDocument.enabled = false;
            }
            _isUIInitialized = false;
        }

        public void RefreshLeaderboard()
        {
            EnsureUIInitialized();
            SetLoadingState();

            var manager = SupabaseLeaderboardManager.Instance;
            if (manager != null)
            {
                manager.FetchLeaderboard();
            }
            else
            {
                HandleLeaderboardError("Leaderboard manager not initialized.");
            }
        }

        private void HandleLeaderboardLoaded(LeaderboardResponse response)
        {
            if (!_isUIInitialized) return;

            if (response == null || !response.success || response.leaderboard == null)
            {
                HandleLeaderboardError(response?.error ?? "Failed to load leaderboard data.");
                return;
            }

            if (response.current_player != null)
            {
                UpdateProfileDisplay(response.current_player);
            }
            else if (SupabaseLeaderboardManager.InstanceIfExists?.CurrentPlayer == null)
            {
                SetProfileGuestState();
            }

            PopulateTable(response.leaderboard);
        }

        private void HandleLeaderboardError(string error)
        {
            if (!_isUIInitialized) return;

            if (loadingState != null) loadingState.style.display = DisplayStyle.None;
            if (emptyState != null) emptyState.style.display = DisplayStyle.None;
            if (scrollView != null) scrollView.style.display = DisplayStyle.None;

            if (SupabaseLeaderboardManager.InstanceIfExists?.CurrentPlayer == null)
            {
                SetProfileGuestState();
            }

            if (errorState != null)
            {
                errorState.style.display = DisplayStyle.Flex;
                if (errorMessageLabel != null)
                {
                    errorMessageLabel.text = $"Could not load leaderboard:\n{error}";
                }
            }
        }

        private void HandlePlayerProfileUpdated(PlayerProfile profile)
        {
            if (_isUIInitialized)
            {
                UpdateProfileDisplay(profile);
            }
        }

        private void SetLoadingState()
        {
            if (loadingState != null) loadingState.style.display = DisplayStyle.Flex;
            if (errorState != null) errorState.style.display = DisplayStyle.None;
            if (emptyState != null) emptyState.style.display = DisplayStyle.None;
            if (scrollView != null) scrollView.style.display = DisplayStyle.None;
        }

        private void SetProfileLoadingState()
        {
            if (profileDisplayName != null) profileDisplayName.text = "Loading...";
            if (profilePlayerCode != null) profilePlayerCode.text = "GJ-....";
            if (profileBestScore != null) profileBestScore.text = "-";
            if (profileGlobalRank != null) profileGlobalRank.text = "-";
        }

        private void SetProfileGuestState(string guestName = "Guest Player")
        {
            int localHighScore = PlayerPrefs.GetInt("HighScore", 0);
            if (profileDisplayName != null) profileDisplayName.text = guestName;
            if (profilePlayerCode != null) profilePlayerCode.text = "GJ-....";
            if (profileBestScore != null) profileBestScore.text = localHighScore > 0 ? localHighScore.ToString() : "-";
            if (profileGlobalRank != null) profileGlobalRank.text = "-";
        }

        private void UpdateProfileDisplay(PlayerProfile profile)
        {
            if (profile == null) return;

            if (profileDisplayName != null)
                profileDisplayName.text = profile.display_name;

            if (profilePlayerCode != null)
                profilePlayerCode.text = profile.player_code;

            if (profileBestScore != null)
                profileBestScore.text = profile.best_score.ToString();

            if (profileGlobalRank != null)
            {
                if (profile.global_rank == 1) profileGlobalRank.text = "1st Place";
                else if (profile.global_rank == 2) profileGlobalRank.text = "2nd Place";
                else if (profile.global_rank == 3) profileGlobalRank.text = "3rd Place";
                else if (profile.global_rank > 0) profileGlobalRank.text = $"#{profile.global_rank}";
                else profileGlobalRank.text = "-";
            }
        }

        private void PopulateTable(LeaderboardEntry[] entries)
        {
            if (loadingState != null) loadingState.style.display = DisplayStyle.None;
            if (errorState != null) errorState.style.display = DisplayStyle.None;

            if (entries == null || entries.Length == 0)
            {
                if (emptyState != null) emptyState.style.display = DisplayStyle.Flex;
                if (scrollView != null) scrollView.style.display = DisplayStyle.None;
                return;
            }

            if (emptyState != null) emptyState.style.display = DisplayStyle.None;
            if (scrollView != null)
            {
                scrollView.style.display = DisplayStyle.Flex;
                scrollView.Clear();

                string myPlayerCode = SupabaseLeaderboardManager.InstanceIfExists?.CurrentPlayer?.player_code;

                for (int i = 0; i < entries.Length; i++)
                {
                    LeaderboardEntry entry = entries[i];
                    VisualElement row = CreateTableRow(entry, i % 2 == 0, entry.player_code == myPlayerCode);
                    scrollView.Add(row);
                }
            }
        }

        private VisualElement CreateTableRow(LeaderboardEntry entry, bool isEven, bool isCurrentPlayer)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("table-row");
            row.AddToClassList(isEven ? "table-row-even" : "table-row-odd");

            if (isCurrentPlayer)
            {
                row.AddToClassList("table-row-highlight");
            }

            // Col 1: Display Name
            Label nameLabel = new Label(entry.display_name);
            nameLabel.AddToClassList("row-name-text");
            nameLabel.AddToClassList("col-name");

            // Col 2: Best Score
            Label scoreLabel = new Label(entry.best_score.ToString());
            scoreLabel.AddToClassList("row-score-text");
            scoreLabel.AddToClassList("col-score");

            // Col 3: Leaderboard Position (Medal or Rank Number)
            VisualElement rankContainer = new VisualElement();
            rankContainer.AddToClassList("col-rank");
            rankContainer.AddToClassList("rank-container");

            if (goldMedalSprite == null || silverMedalSprite == null || bronzeMedalSprite == null)
            {
                LoadMedalSprites();
            }

            Sprite medal = null;
            if (entry.rank == 1 || entry.rank_display == "🥇") medal = goldMedalSprite;
            else if (entry.rank == 2 || entry.rank_display == "🥈") medal = silverMedalSprite;
            else if (entry.rank == 3 || entry.rank_display == "🥉") medal = bronzeMedalSprite;

            if (medal != null)
            {
                VisualElement medalIcon = new VisualElement();
                medalIcon.AddToClassList("medal-icon");
                medalIcon.style.backgroundImage = new StyleBackground(medal);
                rankContainer.Add(medalIcon);
            }
            else
            {
                // Fallback: If sprite is not available, never display invisible emoji characters.
                // Display numeric rank cleanly.
                string display = entry.rank > 0 ? entry.rank.ToString() : (entry.rank_display ?? "");
                display = display.Replace("🥇", "1").Replace("🥈", "2").Replace("🥉", "3");
                Label rankLabel = new Label(display);
                rankLabel.AddToClassList("row-rank-text");
                rankContainer.Add(rankLabel);
            }

            row.Add(nameLabel);
            row.Add(scoreLabel);
            row.Add(rankContainer);

            return row;
        }
    }
}
