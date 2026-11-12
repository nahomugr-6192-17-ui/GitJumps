using System;

namespace GitJumps.Leaderboard
{
    [Serializable]
    public class LeaderboardEntry
    {
        public int rank;
        public string display_name;
        public int best_score;
        public string rank_display;
        public string player_code;
    }

    [Serializable]
    public class PlayerProfile
    {
        public string id;
        public long telegram_id;
        public string player_code;
        public string display_name;
        public int best_score;
        public long global_rank;
        public bool is_new_player;
    }

    [Serializable]
    public class LeaderboardResponse
    {
        public bool success;
        public int count;
        public LeaderboardEntry[] leaderboard;
        public PlayerProfile current_player;
        public string error;
    }

    [Serializable]
    public class SubmitScoreResponse
    {
        public bool success;
        public PlayerProfile player;
        public string error;
    }

    [Serializable]
    public class PlayerProfileResponse
    {
        public bool success;
        public PlayerProfile player;
        public string error;
    }

    [Serializable]
    public class ScoreSubmissionPayload
    {
        public string initData;
        public int score;
    }

    [Serializable]
    public class PlayerInitPayload
    {
        public string initData;
    }
}

