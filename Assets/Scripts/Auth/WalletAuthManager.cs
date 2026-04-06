// Assets/Scripts/Auth/WalletAuthManager.cs
// Handles wallet-based and Unity device-ID-based authentication.
//
// Balance sync on re-login:
//   The /auth/wallet-login and /auth/unity-login responses now include
//   cachedEthBalance and cachedEthBalanceWei.  On login success we push
//   that value straight into WalletManager so the balance UI populates
//   immediately — before any manual RefreshBalance() call is made.
//   WalletManager.RefreshBalance() is then invoked in the background to
//   fetch the live on-chain value and overwrite the cached one.
//
// NOTE: PlayerProfile and PlayerStats are defined in SharedModels.cs ONLY.
//   They have been removed from the bottom of this file to fix CS0101 errors.

using System;
using UnityEngine;
using UnityEngine.Events;
using Newtonsoft.Json;

public class WalletAuthManager : MonoBehaviour
{
    public static WalletAuthManager Instance { get; private set; }

    [HideInInspector] public UnityEvent<PlayerProfile> OnLoginSuccess = new();
    [HideInInspector] public UnityEvent<string>        OnLoginFailed  = new();

    public PlayerProfile CurrentPlayer { get; private set; }
    public string        JwtToken      { get; private set; }
    public bool          IsLoggedIn    => CurrentPlayer != null && !string.IsNullOrEmpty(JwtToken);

    private const string PREF_JWT     = "JWT_Token";
    private const string PREF_PLAYER  = "Player_Profile";
    private const string PREF_WALLET  = "Wallet_Address";

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start() => TryAutoLogin();

    // ─────────────────────────────────────────────────────────────────────────
    //  AUTO-LOGIN (restore saved session)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores a saved JWT + player profile from PlayerPrefs.
    /// If the saved profile includes a cachedEthBalance, that value is pushed
    /// into WalletManager immediately so the balance UI is never empty on start.
    /// </summary>
    public void TryAutoLogin()
    {
        string savedJwt     = PlayerPrefs.GetString(PREF_JWT,    "");
        string savedProfile = PlayerPrefs.GetString(PREF_PLAYER, "");

        if (string.IsNullOrEmpty(savedJwt) || string.IsNullOrEmpty(savedProfile))
            return;

        JwtToken      = savedJwt;
        CurrentPlayer = JsonConvert.DeserializeObject<PlayerProfile>(savedProfile);

        Debug.Log($"[Auth] Session restored: {CurrentPlayer?.username}  " +
                  $"balance: {CurrentPlayer?.cachedEthBalance} ETH");

        ApplyCachedBalanceToWalletManager(CurrentPlayer);
        OnLoginSuccess.Invoke(CurrentPlayer);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WALLET LOGIN
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /auth/wallet-login
    /// The response includes cachedEthBalance (refreshed live on the server).
    /// We apply it immediately then trigger a background live-refresh in case
    /// the server's Sepolia RPC call timed out.
    /// </summary>
    public async void LoginWithWallet(string walletAddress)
    {
        try
        {
            string body = JsonConvert.SerializeObject(new { walletAddress });
            string json = await ApiClient.Instance.PostAsync("/auth/wallet-login", body);
            var    resp = JsonConvert.DeserializeObject<LoginResponse>(json);

            if (resp?.success != true)
                throw new Exception(resp?.message ?? "Login failed");

            JwtToken      = resp.token;
            CurrentPlayer = resp.player;

            // Persist session to PlayerPrefs (includes balance fields)
            PlayerPrefs.SetString(PREF_JWT,    JwtToken);
            PlayerPrefs.SetString(PREF_PLAYER, JsonConvert.SerializeObject(CurrentPlayer));
            PlayerPrefs.SetString(PREF_WALLET, walletAddress);
            PlayerPrefs.Save();

            ApplyCachedBalanceToWalletManager(CurrentPlayer);

            Debug.Log($"[Auth] ✅ Wallet login: {CurrentPlayer?.username}  " +
                      $"balance: {CurrentPlayer?.cachedEthBalance} ETH");

            OnLoginSuccess.Invoke(CurrentPlayer);

            // Background live-refresh — overwrites the cached value if RPC is healthy
            if (WalletManager.Instance != null && !string.IsNullOrEmpty(walletAddress))
                WalletManager.Instance.RefreshBalance();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] Wallet login failed: {e.Message}");
            OnLoginFailed.Invoke(e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUEST LOGIN
    // ─────────────────────────────────────────────────────────────────────────

    public void LoginAsGuest()
    {
        CurrentPlayer = new PlayerProfile { username = "Guest", walletAddress = null };
        Debug.Log("[Auth] Guest mode");
        OnLoginSuccess.Invoke(CurrentPlayer);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LOGOUT
    // ─────────────────────────────────────────────────────────────────────────

    public void Logout()
    {
        CurrentPlayer = null;
        JwtToken      = null;
        PlayerPrefs.DeleteKey(PREF_JWT);
        PlayerPrefs.DeleteKey(PREF_PLAYER);
        PlayerPrefs.DeleteKey(PREF_WALLET);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static void ApplyCachedBalanceToWalletManager(PlayerProfile profile)
    {
        if (WalletManager.Instance == null || profile == null) return;

        if (!string.IsNullOrEmpty(profile.walletAddress))
            WalletManager.Instance.SetWalletAddress(profile.walletAddress);

        string balance = profile.cachedEthBalance;
        if (!string.IsNullOrEmpty(balance) && balance != "0.0000")
        {
            WalletManager.Instance.SetCachedBalance(balance, profile.cachedEthBalanceWei ?? "0");
            Debug.Log($"[Auth] Applied cached balance to WalletManager: {balance} ETH");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DATA MODELS
    // ─────────────────────────────────────────────────────────────────────────
    // PlayerProfile and PlayerStats are defined in SharedModels.cs — NOT here.

    [Serializable]
    private class LoginResponse
    {
        public bool          success;
        public string        message;
        public string        token;
        public PlayerProfile player;   // type from SharedModels.cs
    }
}