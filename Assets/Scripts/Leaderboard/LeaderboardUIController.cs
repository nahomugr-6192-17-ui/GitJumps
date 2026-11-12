using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitJumps.Leaderboard
{
    [RequireComponent(typeof(UIDocument))]
    public class LeaderboardUIController : MonoBehaviour
    {
        private static LeaderboardUIController _instance;
        public static LeaderboardUIController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<LeaderboardUIController>();
                    if (_instance == null)
                    {
                        var prefab = Resources.Load<GameObject>("LeaderboardManager");
                        if (prefab != null)
                        {
                            var go = Instantiate(prefab);
                            go.name = "LeaderboardManager";
                            _instance = go.GetComponent<LeaderboardUIController>();
                        }
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        private UIDocument uiDocument;
        private VisualElement rootElement;

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null) return;

            QueryUIElements();
            RegisterCallbacks();

            if (SupabaseLeaderboardManager.Instance != null)
            {
                SupabaseLeaderboardManager.Instance.OnLeaderboardLoaded += HandleLeaderboardLoaded;
                SupabaseLeaderboardManager.Instance.OnLeaderboardError += HandleLeaderboardError;
                SupabaseLeaderboardManager.Instance.OnPlayerProfileUpdated += HandlePlayerProfileUpdated;

                if (SupabaseLeaderboardManager.Instance.CurrentPlayer != null)
                {
                    UpdateProfileDisplay(SupabaseLeaderboardManager.Instance.CurrentPlayer);
                }
            }
        }

        private void OnDisable()
        {
            UnregisterCallbacks();

            if (SupabaseLeaderboardManager.Instance != null)
            {
                SupabaseLeaderboardManager.Instance.OnLeaderboardLoaded -= HandleLeaderboardLoaded;
                SupabaseLeaderboardManager.Instance.OnLeaderboardError -= HandleLeaderboardError;
                SupabaseLeaderboardManager.Instance.OnPlayerProfileUpdated -= HandlePlayerProfileUpdated;
            }
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
                closeButton.clicked += HideLeaderboard;

            if (refreshButton != null)
                refreshButton.clicked += RefreshLeaderboard;
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
            if (leaderboardOverlay == null)
                QueryUIElements();

            if (leaderboardOverlay != null)
            {
                leaderboardOverlay.style.display = DisplayStyle.Flex;
            }

            // Update personal profile
            if (SupabaseLeaderboardManager.Instance != null && SupabaseLeaderboardManager.Instance.CurrentPlayer != null)
            {
                UpdateProfileDisplay(SupabaseLeaderboardManager.Instance.CurrentPlayer);
            }
            else
            {
                SetProfileLoadingState();
                if (SupabaseLeaderboardManager.Instance != null)
                {
                    SupabaseLeaderboardManager.Instance.InitializePlayer((res) =>
                    {
                        if (res != null && res.player != null)
                        {
                            UpdateProfileDisplay(res.player);
                        }
                    });
                }
            }

            RefreshLeaderboard();
        }

        public void HideLeaderboard()
        {
            if (leaderboardOverlay != null)
            {
                leaderboardOverlay.style.display = DisplayStyle.None;
            }
        }

        public void RefreshLeaderboard()
        {
            SetLoadingState();

            if (SupabaseLeaderboardManager.Instance != null)
            {
                SupabaseLeaderboardManager.Instance.FetchLeaderboard(
                    onComplete: HandleLeaderboardLoaded,
                    onError: HandleLeaderboardError
                );
            }
            else
            {
                HandleLeaderboardError("Leaderboard manager not initialized.");
            }
        }

        private void HandleLeaderboardLoaded(LeaderboardResponse response)
        {
            if (response == null || !response.success || response.leaderboard == null)
            {
                HandleLeaderboardError(response?.error ?? "Failed to load leaderboard data.");
                return;
            }

            if (response.current_player != null)
            {
                UpdateProfileDisplay(response.current_player);
            }

            PopulateTable(response.leaderboard);
        }

        private void HandleLeaderboardError(string error)
        {
            if (loadingState != null) loadingState.style.display = DisplayStyle.None;
            if (emptyState != null) emptyState.style.display = DisplayStyle.None;
            if (scrollView != null) scrollView.style.display = DisplayStyle.None;

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
            UpdateProfileDisplay(profile);
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
                if (profile.global_rank == 1) profileGlobalRank.text = "🥇 #1";
                else if (profile.global_rank == 2) profileGlobalRank.text = "🥈 #2";
                else if (profile.global_rank == 3) profileGlobalRank.text = "🥉 #3";
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

                string myPlayerCode = SupabaseLeaderboardManager.Instance?.CurrentPlayer?.player_code;

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
            Label rankLabel = new Label(entry.rank_display);
            rankLabel.AddToClassList("row-rank-text");
            rankLabel.AddToClassList("col-rank");

            row.Add(nameLabel);
            row.Add(scoreLabel);
            row.Add(rankLabel);

            return row;
        }
    }
}

