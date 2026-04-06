// Assets/Scripts/SharedModels.cs
// Single source of truth for all shared data models used across scripts.
// Place in: Assets/Scripts/SharedModels.cs  (root of Scripts folder)
//
// ⚠️ IMPORTANT:
//   • If AIEngine.cs, MatchManager.cs, or any other script defines AIProfile,
//     MatchData, or PlayerProfile — DELETE those definitions from those files.
//   • PurchaseResult is defined ONLY here. It has been REMOVED from the bottom
//     of StoreManager.cs to fix CS0101 / CS0229 duplicate definition errors.
//   • PlayerProfile and PlayerStats are defined ONLY here.
//     DELETE the copies at the bottom of WalletAuthManager.cs.

using System;
using UnityEngine;

// ─── AI Models ───────────────────────────────────────────────────────────────

[Serializable]
public class AIProfile
{
    public string difficulty;     // "easy" | "medium" | "hard" | "expert"
    public float  reactionSpeed;  // 0.0 (slow) -> 1.0 (instant)
    public float  errorMargin;    // 0.0 (perfect) -> 1.0 (very noisy)
    public string strategy;       // "aggressive" | "defensive" | "balanced" | "adaptive"
    public string profileHash;    // SHA256 hash for anti-cheat verification
}

[Serializable]
public class MatchData
{
    public string    matchId;
    public AIProfile aiProfile;
    public string    matchToken;
    public string    difficulty;
}

// ─── Player Models ────────────────────────────────────────────────────────────

[Serializable]
public class PlayerProfile
{
    public string      id;
    public string      unityPlayerId;
    public string      username;
    public string      email;
    public string      walletAddress;
    public PlayerStats stats;
    public string[]    ownedItems;

    // Balance fields — populated by every login response so the wallet UI
    // can display the last-known balance instantly without waiting for an RPC call.
    public string cachedEthBalance;     // e.g. "0.0123"
    public string cachedEthBalanceWei;  // e.g. "12300000000000000"
    public string ethBalanceFetchedAt;  // ISO date string

    // High Score — personal best win time
    public int    bestTime;             // fastest win duration in seconds (0 = no wins yet)
    public string bestTimeMatchId;      // matchId where the best time was achieved

    public bool IsWalletConnected =>
        WalletManager.Instance != null && WalletManager.Instance.IsConnected;

    public string LiveWalletAddress =>
        WalletManager.Instance?.WalletAddress ?? walletAddress;
}

[Serializable]
public class PlayerStats
{
    public int    wins;
    public int    losses;
    public int    ties;
    public int    totalMatches;
    public string rewardsEarned;  // total ETH earned (wei string)
}

// ─── Purchase Models ──────────────────────────────────────────────────────────
// PurchaseResult is defined HERE and ONLY here.
// Do NOT redefine it in StoreManager.cs or anywhere else.

[Serializable]
public class PurchaseResult
{
    public string itemId;
    public string txHash;
    public string explorerUrl;
    public string paymentType;  // "local_coins" | "fiat" | "eth" | "web_app_eth"
}

// ─── High Score Models ────────────────────────────────────────────────────────

[Serializable]
public class HighScoreEntry
{
    public int    rank;
    public string matchId;
    public string difficulty;
    public int    playerScore;
    public int    opponentScore;
    public int    durationSecs;
    public string startedAt;
}

[Serializable]
public class HighScoresResponse
{
    public bool             success;
    public int              bestTime;          // personal best in seconds
    public string           bestTimeMatchId;
    public HighScoreEntry[] highscores;
}