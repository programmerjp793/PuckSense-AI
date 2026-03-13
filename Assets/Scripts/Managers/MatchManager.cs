// Assets/Scripts/Managers/MatchManager.cs
// Native ETH only (Sepolia Testnet)
//
// Changes from existing:
//   • "earn TTK"          → "earn ETH"         (StartAIRewardMatch warning)
//   • "cannot claim TTK"  → "cannot claim ETH"  (ClaimReward warning)
//   • "TTK |"             → "ETH wei |"         (ClaimReward debug log)
//   • explorerUrl comment updated (sepolia.etherscan.io)
//   • Everything else unchanged: match flow, anti-cheat, JWT, events

using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using Newtonsoft.Json;

public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }

    [HideInInspector] public UnityEvent<MatchData> OnMatchCreated   = new();
    [HideInInspector] public UnityEvent<string>    OnMatchValidated = new();
    [HideInInspector] public UnityEvent<string>    OnRewardClaimed  = new();
    [HideInInspector] public UnityEvent<string>    OnMatchError     = new();

    public MatchData CurrentMatch { get; private set; }
    public int       PlayerScore  { get; private set; }
    public int       AIScore      { get; private set; }
    public bool      MatchActive  { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─── Step 1: Create Match ─────────────────────────────────────────────────

    public async void StartAIRewardMatch(string difficulty = "medium")
    {
        try
        {
            PlayerScore = 0;
            AIScore     = 0;

            if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
            {
                // FIX: was "earn TTK" → "earn ETH"
                OnMatchError.Invoke("Wallet not connected. Connect wallet to earn ETH.");
                Debug.LogWarning("[Match] Wallet not connected — cannot start reward match.");
                return;
            }

            Debug.Log($"[Match] Creating match. Difficulty: {difficulty} | " +
                      $"Wallet: {WalletManager.Instance.WalletAddress}");

            string body = JsonConvert.SerializeObject(new { difficulty });
            string json = await ApiClient.Instance.PostAsync("/match/create", body);
            var    resp = JsonConvert.DeserializeObject<CreateMatchResponse>(json);

            if (resp?.success != true)
            {
                OnMatchError.Invoke(resp?.message ?? "Match creation failed");
                return;
            }

            CurrentMatch = new MatchData
            {
                matchId    = resp.matchId,
                matchToken = resp.matchToken,
                difficulty = difficulty,
                aiProfile  = resp.aiProfile,
            };

            MatchActive = true;

            Debug.Log($"[Match] Match created: {CurrentMatch.matchId} | " +
                      $"Difficulty: {resp.aiProfile?.difficulty}");

            OnMatchCreated.Invoke(CurrentMatch);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Match] StartAIRewardMatch failed: {e.Message}");
            OnMatchError.Invoke(e.Message);
        }
    }

    // ─── Step 2: Record Goal ──────────────────────────────────────────────────

    public void RecordGoal(bool playerScored)
    {
        if (!MatchActive) return;

        if (playerScored) PlayerScore++;
        else              AIScore++;

        Debug.Log($"[Match] Goal! Score → Player:{PlayerScore}  AI:{AIScore}");
    }

    // ─── Step 3: Submit Result ────────────────────────────────────────────────

    public async void SubmitMatchResult(bool playerWon)
    {
        if (CurrentMatch == null)
        {
            Debug.LogWarning("[Match] No active match to submit.");
            return;
        }

        try
        {
            string profileJson = JsonConvert.SerializeObject(CurrentMatch.aiProfile);
            string profileHash = ComputeSHA256(profileJson);

            string body = JsonConvert.SerializeObject(new
            {
                matchId       = CurrentMatch.matchId,
                matchToken    = CurrentMatch.matchToken,
                playerScore   = PlayerScore,
                aiScore       = AIScore,
                playerWon,
                profileHash,
                walletAddress = WalletManager.Instance?.WalletAddress ?? "",
            });

            string json = await ApiClient.Instance.PostAsync("/match/submit-result", body);
            var    resp = JsonConvert.DeserializeObject<SimpleResponse>(json);

            MatchActive = false;

            if (resp?.success == true)
            {
                Debug.Log($"[Match] Result submitted. PlayerWon: {playerWon}");
                OnMatchValidated.Invoke(CurrentMatch.matchId);
            }
            else
            {
                Debug.LogWarning("[Match] Submit failed: " + resp?.message);
                OnMatchError.Invoke(resp?.message ?? "Submit failed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Match] SubmitMatchResult failed: {e.Message}");
            OnMatchError.Invoke(e.Message);
        }
    }

    // ─── Step 4: Claim Reward ─────────────────────────────────────────────────

    public async void ClaimReward()
    {
        if (CurrentMatch == null)
        {
            Debug.LogWarning("[Match] No match to claim reward for.");
            return;
        }

        if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
        {
            // FIX: was "cannot claim TTK reward" → "ETH reward"
            OnMatchError.Invoke("Wallet not connected — cannot claim ETH reward.");
            Debug.LogWarning("[Match] Wallet not connected — reward skipped.");
            return;
        }

        try
        {
            string body = JsonConvert.SerializeObject(new
            {
                matchId       = CurrentMatch.matchId,
                walletAddress = WalletManager.Instance.WalletAddress,
            });

            string json = await ApiClient.Instance.PostAsync("/reward/claim", body);
            var    resp = JsonConvert.DeserializeObject<RewardResponse>(json);

            if (resp?.success == true)
            {
                // FIX: was "TTK |" → "ETH wei |"
                // explorerUrl now points to sepolia.etherscan.io (set by backend)
                Debug.Log($"[Match] Reward claimed! {resp.rewardAmount} ETH wei | " +
                          $"TxHash: {resp.txHash}");

                OnRewardClaimed.Invoke(resp.txHash);

                // Refresh ETH balance from chain + sync coins + persist
                WalletManager.Instance.RefreshBalance();
                WalletManager.Instance.SyncCoinsFromPrefs();
                WalletManager.Instance.SaveSession();
            }
            else
            {
                Debug.LogWarning("[Match] Claim failed: " + resp?.message);
                OnMatchError.Invoke(resp?.message ?? "Claim failed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Match] ClaimReward failed: {e.Message}");
            OnMatchError.Invoke(e.Message);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private string ComputeSHA256(string input)
    {
        using var sha  = SHA256.Create();
        byte[]    bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

    [Serializable] private class CreateMatchResponse
    {
        public bool      success;
        public string    message;
        public string    matchId;
        public string    matchToken;
        public AIProfile aiProfile;
    }

    [Serializable] private class SimpleResponse
    {
        public bool   success;
        public string message;
    }

    [Serializable] private class RewardResponse
    {
        public bool   success;
        public string message;
        public string txHash;
        public string rewardAmount; // ETH in wei (was TTK in wei — same field, same type)
        public string explorerUrl;  // now sepolia.etherscan.io (set by backend reward.js)
    }
}