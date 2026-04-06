// Assets/Scripts/Blockchain/WalletManager.cs
// Native ETH only (Sepolia Testnet)
//
// Balance sync changes (from WalletManager_BalancePatch.cs):
//   1. Added EthBalanceWei property — raw wei string kept in sync with EthBalance.
//   2. Added SetCachedBalance() — called by WalletAuthManager on login to populate
//      the UI instantly from the server-cached value before any RPC call is made.
//   3. RefreshBalanceAsync() now persists both formatted and wei values to
//      PlayerPrefs after every successful live fetch, and also reads
//      balanceFormatted + fromCache fields from the updated /wallet/balance response.
//   4. Awake() restores both PREF_ETH_BALANCE and PREF_ETH_BALANCE_WEI on cold start.

using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Newtonsoft.Json;

public class WalletManager : MonoBehaviour
{
    public static WalletManager Instance { get; private set; }

    // ── Sepolia constants ─────────────────────────────────────────────────────
    public const int    SEPOLIA_CHAIN_ID     = 11155111;
    public const string SEPOLIA_CHAIN_ID_HEX = "0xaa36a7";
    public const string EXPLORER_BASE        = "https://sepolia.etherscan.io";

    // ── PlayerPrefs keys ──────────────────────────────────────────────────────
    // Centralised here so nothing else hard-codes key strings.
    private const string PREF_WALLET_ADDRESS  = "WalletAddress";
    private const string PREF_PLAYER_COINS    = "PlayerCoins";
    private const string PREF_ETH_BALANCE     = "CachedEthBalance";     // NEW
    private const string PREF_ETH_BALANCE_WEI = "CachedEthBalanceWei";  // NEW

    // ── State ─────────────────────────────────────────────────────────────────
    public string WalletAddress   { get; private set; }
    public string EthBalance      { get; private set; } = "0.0000";
    public string EthBalanceWei   { get; private set; } = "0";           // NEW
    public int    CoinsBalance    { get; private set; } = 100;
    public int    WalletTier      { get; private set; } = 0;

    public bool IsConnected => !string.IsNullOrEmpty(WalletAddress);

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string> OnWalletConnected;
    public event Action         OnWalletDisconnected;
    public UnityEvent<string>   OnBalanceUpdated = new();
    public UnityEvent<int>      OnCoinsUpdated   = new();
    public event Action<string> OnTransactionSent;
    public event Action<int>    OnTierUpdated;
    public event Action<string> OnError;

    // ── Internal ──────────────────────────────────────────────────────────────
    private const string DEEP_LINK_RETURN = "airhockey://wallet-callback";
    private Action<string> _pendingTxCallback;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Application.deepLinkActivated += OnDeepLinkActivated;

        // Restore wallet address
        WalletAddress = PlayerPrefs.GetString(PREF_WALLET_ADDRESS, "");
        CoinsBalance  = PlayerPrefs.GetInt(PREF_PLAYER_COINS, 100);

        // NEW: restore both formatted balance and wei from dedicated prefs keys.
        // This replaces the old per-address key ("EthBalance_0x...") with a
        // single consistent key so SetCachedBalance() and RefreshBalanceAsync()
        // always read/write the same location.
        string savedBalance    = PlayerPrefs.GetString(PREF_ETH_BALANCE,     "0.0000");
        string savedBalanceWei = PlayerPrefs.GetString(PREF_ETH_BALANCE_WEI, "0");

        if (savedBalance != "0.0000")
        {
            EthBalance    = savedBalance;
            EthBalanceWei = savedBalanceWei;
            // Don't fire OnBalanceUpdated here — UI listeners aren't wired yet.
            // WalletAuthManager.TryAutoLogin() calls SetCachedBalance() shortly
            // after Awake(), which does fire the event at the right time.
            Debug.Log($"[Wallet] Cold-start balance restored: {EthBalance} ETH");
        }
        else if (!string.IsNullOrEmpty(WalletAddress))
        {
            // Fallback: read the old per-address key written by the previous version
            EthBalance = PlayerPrefs.GetString($"EthBalance_{WalletAddress}", "0.0000");
        }
    }

    void OnDestroy()
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
    }

    // ── Wallet Connection ─────────────────────────────────────────────────────

    public void ConnectMetaMask()
    {
        string link = $"metamask://connect?redirectUrl={Uri.EscapeDataString(DEEP_LINK_RETURN)}";
        Application.OpenURL(link);
        Debug.Log($"[Wallet] MetaMask deep link opened: {link}");
    }

    public void SwitchToSepolia()
    {
        string link = $"metamask://switch-chain?chainId={SEPOLIA_CHAIN_ID_HEX}" +
                      $"&redirectUrl={Uri.EscapeDataString(DEEP_LINK_RETURN)}";
        Application.OpenURL(link);
        Debug.Log("[Wallet] Switching MetaMask to Sepolia");
    }

    /// <summary>
    /// Called after MetaMask deep-link returns with an address,
    /// or called directly from a UI input field.
    /// </summary>
    public async void SetWalletAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || !address.StartsWith("0x"))
        {
            Debug.LogWarning("[Wallet] Invalid wallet address.");
            OnError?.Invoke("Invalid wallet address");
            return;
        }

        WalletAddress = address.Trim().ToLower();
        PlayerPrefs.SetString(PREF_WALLET_ADDRESS, WalletAddress);
        PlayerPrefs.Save();

        // Restore any previously cached balance for this address while the
        // live refresh is in flight so the UI is never empty.
        string prev = PlayerPrefs.GetString(PREF_ETH_BALANCE, "0.0000");
        if (prev != "0.0000")
            SetCachedBalance(prev, PlayerPrefs.GetString(PREF_ETH_BALANCE_WEI, "0"));

        Debug.Log($"[Wallet] Wallet set: {WalletAddress}");
        OnWalletConnected?.Invoke(WalletAddress);

        await LinkWalletToPlayer();
        await RefreshBalanceAsync();
        await RefreshTierAsync();
    }

    public void DisconnectWallet()
    {
        SaveSession();
        WalletAddress = "";
        EthBalance    = "0.0000";
        EthBalanceWei = "0";
        CoinsBalance  = 100;
        WalletTier    = 0;
        PlayerPrefs.DeleteKey(PREF_WALLET_ADDRESS);
        PlayerPrefs.Save();
        OnWalletDisconnected?.Invoke();
        Debug.Log("[Wallet] Wallet disconnected.");
    }

    // ── Balance ───────────────────────────────────────────────────────────────

    // NEW: Applies a pre-fetched (server-cached) ETH balance and fires
    // OnBalanceUpdated so the UI refreshes immediately without any network call.
    // Called by WalletAuthManager right after a successful login response, and
    // from Awake() when restoring a saved session.
    public void SetCachedBalance(string formattedBalance, string balanceWei = "0")
    {
        EthBalance    = formattedBalance ?? "0.0000";
        EthBalanceWei = balanceWei       ?? "0";

        // Persist so TryAutoLogin can repopulate on next cold start
        PlayerPrefs.SetString(PREF_ETH_BALANCE,     EthBalance);
        PlayerPrefs.SetString(PREF_ETH_BALANCE_WEI, EthBalanceWei);
        PlayerPrefs.Save();

        OnBalanceUpdated?.Invoke(EthBalance);
        Debug.Log($"[Wallet] Cached balance applied: {EthBalance} ETH");
    }

    public async void RefreshBalance() => await RefreshBalanceAsync();

    /// <summary>
    /// GET /wallet/balance?address=0x...
    /// Updated wallet.js now returns both `balance` (wei) and `balanceFormatted`
    /// (4-decimal ETH string).  After a successful fetch both values are saved
    /// to PlayerPrefs so they survive a process kill / cold start.
    /// </summary>
    private async Task RefreshBalanceAsync()
    {
        if (!IsConnected) return;
        try
        {
            string json = await ApiClient.Instance.GetAsync(
                $"/wallet/balance?address={Uri.EscapeDataString(WalletAddress)}");

            var resp = JsonConvert.DeserializeObject<BalanceResponse>(json);

            if (resp?.success == true)
            {
                // Prefer the formatted string returned by the updated wallet.js
                string formatted = resp.balanceFormatted;

                // Fallback: convert raw wei if balanceFormatted is absent
                // (handles older server builds during a rolling deploy)
                if (string.IsNullOrEmpty(formatted) && !string.IsNullOrEmpty(resp.balance))
                {
                    if (decimal.TryParse(resp.balance,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out decimal wei))
                        formatted = (wei / 1_000_000_000_000_000_000m).ToString("F4");
                    else
                        formatted = "0.0000";
                }

                EthBalance    = formatted           ?? "0.0000";
                EthBalanceWei = resp.balance        ?? "0";

                // Persist both values — this is what survives a cold start
                PlayerPrefs.SetString(PREF_ETH_BALANCE,     EthBalance);
                PlayerPrefs.SetString(PREF_ETH_BALANCE_WEI, EthBalanceWei);
                // Keep the legacy per-address key for WalletConnectUI compatibility
                PlayerPrefs.SetString($"EthBalance_{WalletAddress}", EthBalance);
                PlayerPrefs.Save();

                OnBalanceUpdated.Invoke(EthBalance);
                Debug.Log($"[Wallet] ETH Balance: {EthBalance} (fromCache={resp.fromCache})");
            }
            else
            {
                Debug.LogWarning("[Wallet] Balance fetch returned success=false");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Wallet] Balance refresh failed: {e.Message}");
            // Don't clear the displayed balance on a transient error —
            // the cached value from PlayerPrefs is still showing.
        }
    }

    /// <summary>
    /// GET /wallet/info — fetches tier (and re-syncs owned items as a side effect).
    /// Separate from balance so both can update independently.
    /// </summary>
    private async Task RefreshTierAsync()
    {
        if (!IsConnected) return;
        try
        {
            string json = await ApiClient.Instance.GetAsync("/wallet/info");
            var resp = JsonConvert.DeserializeObject<WalletInfoResponse>(json);

            if (resp?.success == true && resp.wallet != null)
            {
                WalletTier = resp.wallet.tier;
                OnTierUpdated?.Invoke(WalletTier);
                Debug.Log($"[Wallet] Tier: {WalletTier}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Wallet] Tier refresh failed: {e.Message}");
        }
    }

    // ── Store Purchase via Web App ─────────────────────────────────────────────

    public async Task<bool> PurchaseViaWebApp(int itemId, Action<string> onTxSent = null)
    {
        if (!IsConnected) { OnError?.Invoke("Wallet not connected"); return false; }

        try
        {
            var payload = new { itemId, walletAddress = WalletAddress };
            string body = JsonConvert.SerializeObject(payload);

            string json = await ApiClient.Instance.PostAsync("/purchase/prepare-web-tx", body);
            var response = JsonConvert.DeserializeObject<WebAppPurchaseResponse>(json);

            if (response == null || !response.success)
            {
                string msg = response?.message ?? "Failed to prepare web transaction";
                OnError?.Invoke(msg);
                Debug.LogWarning($"[Wallet] prepare-web-tx failed: {msg}");
                return false;
            }

            _pendingTxCallback = onTxSent;

            string url      = response.webAppUrl;
            string cleanUrl = url.Replace("https://", "").Replace("http://", "");
            string metamaskUrl = $"https://metamask.app.link/dapp/{cleanUrl}";

            Debug.Log($"[Wallet] Opening MetaMask dApp browser: {metamaskUrl}");
            Application.OpenURL(metamaskUrl);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Purchase failed: {ex.Message}");
            Debug.LogError($"[Wallet] PurchaseViaWebApp error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PollOwnershipAsync(int itemId, int maxAttempts = 20, int delayMs = 3000)
    {
        if (!IsConnected) return false;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                var payload = new { walletAddress = WalletAddress, itemId };
                string body = JsonConvert.SerializeObject(payload);
                string json = await ApiClient.Instance.PostAsync("/purchase/check-ownership", body);

                var resp = JsonConvert.DeserializeObject<OwnershipCheckResponse>(json);
                if (resp?.success == true && resp.ownsItem)
                {
                    Debug.Log($"[Wallet] Ownership confirmed for item {itemId}");
                    OnTransactionSent?.Invoke($"item_{itemId}_owned");
                    // Refresh live balance now that the purchase ETH has been spent
                    _ = RefreshBalanceAsync();
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Wallet] Ownership poll error: {e.Message}");
            }

            await Task.Delay(delayMs);
        }

        Debug.LogWarning($"[Wallet] Ownership poll timed out for item {itemId}");
        return false;
    }

    // ── Store Purchase (ETH via MetaMask deep-link — legacy) ─────────────────

    public async Task<bool> PurchaseStoreItem(int itemId, Action<string> onTxSent = null)
    {
        if (!IsConnected) { OnError?.Invoke("Wallet not connected"); return false; }

        try
        {
            var payload = new { itemId, walletAddress = WalletAddress };
            string body = JsonConvert.SerializeObject(payload);

            string json = await ApiClient.Instance.PostAsync("/purchase/prepare-store-tx", body);
            var response = JsonConvert.DeserializeObject<StorePurchaseTxData>(json);

            if (response == null || !response.success)
            {
                string msg = response?.message ?? "Failed to prepare transaction";
                OnError?.Invoke(msg);
                Debug.LogWarning($"[Wallet] prepare-store-tx failed: {msg}");
                return false;
            }

            _pendingTxCallback = onTxSent;

            string uriToOpen = PickBestUri(response);
            Debug.Log($"[Wallet] Opening MetaMask via: {uriToOpen}");
            Application.OpenURL(uriToOpen);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Purchase failed: {ex.Message}");
            Debug.LogError($"[Wallet] PurchaseStoreItem error: {ex.Message}");
            return false;
        }
    }

    private static string PickBestUri(StorePurchaseTxData r)
    {
#if UNITY_ANDROID
        if (!string.IsNullOrEmpty(r.metamaskSchemeUri)) return r.metamaskSchemeUri;
#endif
        if (!string.IsNullOrEmpty(r.deepLink))          return r.deepLink;
        if (!string.IsNullOrEmpty(r.metamaskSchemeUri)) return r.metamaskSchemeUri;
        if (!string.IsNullOrEmpty(r.eip681Uri))         return r.eip681Uri;
        return r.deepLink ?? "";
    }

    // ── Coins ─────────────────────────────────────────────────────────────────

    public void SyncCoinsFromPrefs()
    {
        CoinsBalance = PlayerPrefs.GetInt(PREF_PLAYER_COINS, 100);
        OnCoinsUpdated.Invoke(CoinsBalance);
        Debug.Log($"[Wallet] Coins synced: {CoinsBalance}");
    }

    // ── Session ───────────────────────────────────────────────────────────────

    public void SaveSession()
    {
        PlayerPrefs.SetInt(PREF_PLAYER_COINS, CoinsBalance);
        // Save balance under both the new canonical key and the legacy per-address key
        PlayerPrefs.SetString(PREF_ETH_BALANCE,     EthBalance);
        PlayerPrefs.SetString(PREF_ETH_BALANCE_WEI, EthBalanceWei);
        if (!string.IsNullOrEmpty(WalletAddress))
            PlayerPrefs.SetString($"EthBalance_{WalletAddress}", EthBalance);
        PlayerPrefs.Save();
        Debug.Log($"[Wallet] Session saved — Coins: {CoinsBalance}, ETH: {EthBalance}");
    }

    void OnApplicationPause(bool paused) { if (paused) SaveSession(); }
    void OnApplicationQuit()             { SaveSession(); }

    // ── Deep Link Handler ─────────────────────────────────────────────────────

    private void OnDeepLinkActivated(string url)
    {
        Debug.Log($"[Wallet] Deep link: {url}");
        var query = ParseQuery(url);

        if (url.StartsWith("airhockey://wallet-callback"))
        {
            if (query.TryGetValue("address", out var address) && !string.IsNullOrEmpty(address))
                SetWalletAddress(address);
        }
        else if (url.StartsWith("airhockey://tx-callback"))
        {
            if (query.TryGetValue("hash", out var hash) && !string.IsNullOrEmpty(hash))
            {
                Debug.Log($"[Wallet] TX confirmed: {hash}");
                OnTransactionSent?.Invoke(hash);
                _pendingTxCallback?.Invoke(hash);
                _pendingTxCallback = null;
                _ = RefreshBalanceAsync();
            }
        }
    }

    // ── Wallet Linking ────────────────────────────────────────────────────────

    private async Task LinkWalletToPlayer()
    {
        try
        {
            var payload = new { walletAddress = WalletAddress, signature = "mobile-direct" };
            string body = JsonConvert.SerializeObject(payload);
            await ApiClient.Instance.PostAsync("/auth/link-wallet", body);
            Debug.Log("[Wallet] Wallet linked to player.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Wallet] Link failed (may already be linked): {e.Message}");
        }
    }

    // ── Explorer ──────────────────────────────────────────────────────────────

    public void ViewOnExplorer(string txHashOrAddress)
    {
        string type = txHashOrAddress.Length > 42 ? "tx" : "address";
        Application.OpenURL($"{EXPLORER_BASE}/{type}/{txHashOrAddress}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private System.Collections.Generic.Dictionary<string, string> ParseQuery(string url)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>();
        int idx = url.IndexOf('?');
        if (idx < 0) return result;
        foreach (var part in url.Substring(idx + 1).Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2) result[kv[0]] = Uri.UnescapeDataString(kv[1]);
        }
        return result;
    }

    // ── Response DTOs ─────────────────────────────────────────────────────────

    [Serializable]
    private class BalanceResponse
    {
        public bool   success;
        public string balance;           // raw wei string
        public string balanceFormatted;  // 4-decimal ETH string — new field from wallet.js
        public bool   fromCache;         // true when live RPC was unavailable
        public string symbol;
        public int    tier;
        public string note;
        public string warning;
    }

    [Serializable]
    private class WalletInfoResponse
    {
        public bool       success;
        public WalletData wallet;

        [Serializable]
        public class WalletData
        {
            public string address;
            public string balance;
            public string balanceFormatted;
            public string symbol;
            public int    tier;
            public bool   fromCache;
        }
    }

    [Serializable]
    public class StorePurchaseTxData
    {
        public bool   success;
        public string message;
        public string deepLink;
        public string eip681Uri;
        public string metamaskSchemeUri;
        public string calldataHex;
        public string valueWei;
        public string gasLimit;
        public string storeAddress;
        public int    chainId;
        public int    itemId;
        public string itemName;
        public float  priceETH;
    }

    [Serializable]
    public class WebAppPurchaseResponse
    {
        public bool   success;
        public string message;
        public string webAppUrl;
        public string sessionId;
        public string storeAddress;
        public string calldataHex;
        public string valueWei;
        public string gasLimit;
        public int    chainId;
        public int    itemId;
        public string stringItemId;
        public string itemName;
        public float  priceETH;
    }

    [Serializable]
    private class OwnershipCheckResponse
    {
        public bool   success;
        public bool   ownsItem;
        public int    itemId;
        public string itemName;
        public string playerAddress;
    }
}