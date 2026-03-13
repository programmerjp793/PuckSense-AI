// Assets/Scripts/SharedModels.cs
// Single source of truth for all shared data models used across scripts.
// Place in: Assets/Scripts/SharedModels.cs  (root of Scripts folder)
//
// ⚠️ IMPORTANT:
//   • If AIEngine.cs, MatchManager.cs, or any other script defines AIProfile,
//     MatchData, or PlayerProfile — DELETE those definitions from those files.
//   • PurchaseResult is defined ONLY here. It has been REMOVED from the bottom
//     of StoreManager.cs to fix CS0101 / CS0229 duplicate definition errors.

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
    public string      username;
    public string      walletAddress;
    public PlayerStats stats;
    public string[]    ownedItems;

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
    public string paymentType;  // "local_coins" | "fiat" | "eth"
}