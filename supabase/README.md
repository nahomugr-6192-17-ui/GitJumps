# GitJumps — Supabase Global Leaderboard Backend

This directory contains the database migrations and server-side Edge Functions for the **GitJumps Global Leaderboard**.

---

## 1. Database Setup (Phase 1)

1. Open your **[Supabase Dashboard](https://supabase.com/dashboard)**.
2. Select your GitJumps project.
3. In the left sidebar, navigate to **SQL Editor** -> **New query**.
4. Paste and execute the contents of [`supabase/migrations/001_create_leaderboard_tables.sql`](migrations/001_create_leaderboard_tables.sql).
5. To test and verify the database rules, paste and run [`supabase/tests/001_test_leaderboard_tables.sql`](tests/001_test_leaderboard_tables.sql).

---

## 2. Deploying Edge Functions (Phase 2)

Using the Supabase CLI from the root of your project:

### Step 1: Login & Link Project
```bash
npx supabase login
npx supabase link --project-ref <your-supabase-project-id>
```

### Step 2: Configure Server Secrets
```bash
# Required: Your Telegram Bot Token (never commit this or share it)
npx supabase secrets set TELEGRAM_BOT_TOKEN="123456789:ABCdefGHIjklMNOpqrsTUVwxyz"

# Optional for development: Enables mock testing in Unity Editor
npx supabase secrets set ALLOW_DEV_AUTH="true"
```

### Step 3: Deploy Functions
```bash
npx supabase functions deploy get-or-create-player --no-verify-jwt
npx supabase functions deploy submit-score --no-verify-jwt
npx supabase functions deploy get-leaderboard --no-verify-jwt
```
*(Note: `--no-verify-jwt` allows public/anonymous clients to call these endpoints; security is handled internally by Telegram HMAC cryptographic validation and RLS).*

---

## 3. Unity Client Configuration (Phase 3 & 4)

In your Unity Editor:
1. Open the project.
2. Add a GameObject named `LeaderboardManager` to your starting scene (e.g. `HomeScene` and/or `GamePlayScene`).
3. Attach `SupabaseLeaderboardManager` and `LeaderboardUIController`.
4. Configure in the Inspector:
   - **Supabase URL**: Your Project URL from Supabase Dashboard (Settings -> API).
   - **Supabase Anon Key**: Your public anon key from Supabase Dashboard (Settings -> API).
   - **Is Development Mode**: Checked for Editor testing (`dev:1001:Local Developer`).
5. Run the game in Unity Play Mode:
   - Click the **LEADERBOARD** button on the Home Screen or Game Over screen.
   - The Top 100 leaderboard with 3 columns (`Display name`, `Best score`, `Leaderboard` with medals 🥇, 🥈, 🥉) and personal profile card (`GJ-####`, personal best, global rank) will display live.

