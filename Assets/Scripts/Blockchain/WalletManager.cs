// Assets/Scripts/Blockchain/WalletManager.cs
// Native ETH only (Sepolia Testnet)
//
// BACKEND WIRING (all paths relative to ApiClient.backendBaseUrl = ".../api"):
//   GET  /wallet/balance?address=0x...   → wallet.js  → blockchainService.getPlayerInfo()
//   POST /auth/link-wallet               → auth.js     → links wallet to player record
//   POST /purchase/prepare-store-tx      → payment.js  → blockchainService.prepareStorePurchaseTx()
//
// FIXES:
//   • Balance endpoint now passes address as query param AND as X-Wallet-Address header
//     (ApiClient injects the header automatically).
//   • RefreshBalanceAsync parses both `balance` (wei string) and `balanceFormatted` (ETH string)
//     from wallet.js /balance response.  `tier` is read from /wallet/info instead.
//   • PurchaseStoreItem posts to /purchase/prepare-store-tx (payment.js legacy alias).

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

    // ── State ─────────────────────────────────────────────────────────────────
    public string WalletAddress { get; private set; }
    public string EthBalance    { get; private set; } = "0";
    public int    CoinsBalance  { get; private set; } = 100;
    public int    WalletTier    { get; private set; } = 0;

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

        WalletAddress = PlayerPrefs.GetString("WalletAddress", "");
        CoinsBalance  = PlayerPrefs.GetInt("PlayerCoins", 100);
        EthBalance    = PlayerPrefs.GetString($"EthBalance_{WalletAddress}", "0");
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
        PlayerPrefs.SetString("WalletAddress", WalletAddress);
        PlayerPrefs.Save();

        EthBalance = PlayerPrefs.GetString($"EthBalance_{WalletAddress}", "0");

        Debug.Log($"[Wallet] Wallet set: {WalletAddress}");
        OnWalletConnected?.Invoke(WalletAddress);

        await LinkWalletToPlayer();
        await RefreshBalanceAsync();
        await RefreshTierAsync();        // fetch tier from /wallet/info
    }

    public void DisconnectWallet()
    {
        SaveSession();
        WalletAddress = "";
        EthBalance    = "0";
        CoinsBalance  = 100;
        WalletTier    = 0;
        PlayerPrefs.DeleteKey("WalletAddress");
        PlayerPrefs.Save();
        OnWalletDisconnected?.Invoke();
        Debug.Log("[Wallet] Wallet disconnected.");
    }

    // ── Balance ───────────────────────────────────────────────────────────────

    public async void RefreshBalance() => await RefreshBalanceAsync();

    /// <summary>
    /// Calls GET /wallet/balance?address=0x...
    /// wallet.js returns { success, walletAddress, balance (wei string), note }
    /// We convert wei → ETH here because the /balance endpoint on wallet.js
    /// does NOT return balanceFormatted or tier (only /wallet/info does).
    /// </summary>
    private async Task RefreshBalanceAsync()
    {
        if (!IsConnected) return;
        try
        {
            // ApiClient will also inject X-Wallet-Address header automatically
            string json = await ApiClient.Instance.GetAsync(
                $"/wallet/balance?address={Uri.EscapeDataString(WalletAddress)}");

            var resp = JsonConvert.DeserializeObject<BalanceResponse>(json);

            if (resp?.success == true)
            {
                // wallet.js /balance returns raw wei in `balance`
                string formatted = resp.balanceFormatted;

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

                EthBalance = formatted ?? "0.0000";

                PlayerPrefs.SetString($"EthBalance_{WalletAddress}", EthBalance);
                PlayerPrefs.Save();

                OnBalanceUpdated.Invoke(EthBalance);
                Debug.Log($"[Wallet] ETH Balance: {EthBalance}");
            }
            else
            {
                Debug.LogWarning($"[Wallet] Balance fetch returned success=false");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Wallet] Balance refresh failed: {e.Message}");
        }
    }

    /// <summary>
    /// Calls GET /wallet/info to get the tier (wallet.js returns wallet.tier).
    /// This is a separate call so balance and tier can update independently.
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

    // ── Store Purchase via Web App (new flow) ──────────────────────────────────

    /// <summary>
    /// Called by StoreManager to initiate a web-app-based purchase.
    /// Calls POST /purchase/prepare-web-tx → opens wallet web app URL in browser.
    /// After user returns, polls /purchase/check-ownership to verify purchase.
    /// </summary>
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

            string url = response.webAppUrl;
            
            // Format for MetaMask in-app browser deep link
            // e.g. https://metamask.app.link/dapp/pucksense-wallet.netlify.app/pay?...
            string cleanUrl = url.Replace("https://", "").Replace("http://", "");
            string metamaskUrl = $"https://metamask.app.link/dapp/{cleanUrl}";

            // Open the wallet web app directly inside MetaMask Mobile
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

    /// <summary>
    /// Polls POST /purchase/check-ownership to verify the web app purchase completed.
    /// Call this after Application.focusChanged or OnApplicationPause(false).
    /// </summary>
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

    /// <summary>
    /// Legacy: Calls POST /purchase/prepare-store-tx
    /// payment.js prepareStorePurchaseTx → blockchainService.prepareStorePurchaseTx()
    /// Returns a MetaMask deep-link URL.
    /// </summary>
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

            // ── URI priority: deepLink (https universal link) → metamask:// scheme → eip681Uri
            // Backend now returns all three. The EIP-681 scheme fixes:
            //   1. Wrong network — @11155111 forces Sepolia
            //   2. Plain transfer — function name + params encode the calldata correctly
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

    /// <summary>
    /// Picks the best URI to open MetaMask from the backend response.
    /// Priority: deepLink (https universal link) → metamaskSchemeUri → eip681Uri → deepLink fallback
    /// </summary>
    private static string PickBestUri(StorePurchaseTxData r)
    {
#if UNITY_ANDROID
        // On Android, the metamask:// custom scheme is most reliable for direct app open
        if (!string.IsNullOrEmpty(r.metamaskSchemeUri)) return r.metamaskSchemeUri;
#endif
        // iOS + fallback: use the https universal link which the OS routes to MetaMask
        if (!string.IsNullOrEmpty(r.deepLink))           return r.deepLink;
        if (!string.IsNullOrEmpty(r.metamaskSchemeUri))  return r.metamaskSchemeUri;
        if (!string.IsNullOrEmpty(r.eip681Uri))          return r.eip681Uri;
        return r.deepLink ?? "";
    }

    // ── Coins ─────────────────────────────────────────────────────────────────

    public void SyncCoinsFromPrefs()
    {
        CoinsBalance = PlayerPrefs.GetInt("PlayerCoins", 100);
        OnCoinsUpdated.Invoke(CoinsBalance);
        Debug.Log($"[Wallet] Coins synced: {CoinsBalance}");
    }

    // ── Session ───────────────────────────────────────────────────────────────

    public void SaveSession()
    {
        PlayerPrefs.SetInt("PlayerCoins", CoinsBalance);
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

    /// <summary>POST /auth/link-wallet — ties wallet address to player account.</summary>
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
            // Non-fatal — wallet may already be linked
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
    // These match the JSON shapes returned by payment.js

    [Serializable]
    private class BalanceResponse
    {
        public bool   success;
        public string balance;          // raw wei string from wallet.js /balance
        public string balanceFormatted; // only present on /wallet/info wallet object
        public string symbol;
        public int    tier;
        public string note;
    }

    [Serializable]
    private class WalletInfoResponse
    {
        public bool         success;
        public WalletData   wallet;

        [Serializable]
        public class WalletData
        {
            public string address;
            public string balance;
            public string balanceFormatted;
            public string symbol;
            public int    tier;
        }
    }

    [Serializable]
    public class StorePurchaseTxData
    {
        public bool   success;
        public string message;

        // Primary deep link — https universal link → MetaMask
        public string deepLink;

        // Fallback URIs returned by the fixed blockchainService.js
        public string eip681Uri;          // ethereum:<addr>@11155111/buyItem?uint256=<id>&value=<wei>
        public string metamaskSchemeUri;  // metamask://wc?uri=<eip681>

        // Debug / verification
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
        public string webAppUrl;      // URL to open in browser
        public string sessionId;      // tracking ID
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