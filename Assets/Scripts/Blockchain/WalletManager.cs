// Assets/Scripts/Blockchain/WalletManager.cs
// Native ETH only (Sepolia Testnet)
//
// FIX (CS1061): ApiClient has no Post<T>/Get<T> generic methods.
//   All calls now use PostAsync(string)/GetAsync(string) + Newtonsoft manual deserialize.

using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

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

    // ── Deep link ─────────────────────────────────────────────────────────────
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

    public async void RefreshBalance()
    {
        await RefreshBalanceAsync();
    }

    private async Task RefreshBalanceAsync()
    {
        if (!IsConnected) return;
        try
        {
            string json = await ApiClient.Instance.GetAsync(
                $"/wallet/balance?address={WalletAddress}");
            var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<BalanceResponse>(json);

            if (resp?.success == true)
            {
                string formatted = resp.balanceFormatted;

                if (string.IsNullOrEmpty(formatted) && !string.IsNullOrEmpty(resp.balance))
                {
                    if (decimal.TryParse(resp.balance, out decimal wei))
                        formatted = (wei / 1_000_000_000_000_000_000m).ToString("F4");
                    else
                        formatted = "0.0000";
                }

                EthBalance = formatted ?? "0.0000";
                WalletTier = resp.tier;

                PlayerPrefs.SetString($"EthBalance_{WalletAddress}", EthBalance);
                PlayerPrefs.Save();

                OnBalanceUpdated.Invoke(EthBalance);
                OnTierUpdated?.Invoke(WalletTier);
                Debug.Log($"[Wallet] ETH Balance: {EthBalance} | Tier: {WalletTier}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Wallet] Balance refresh failed: {e.Message}");
        }
    }

    // ── Store Purchase ────────────────────────────────────────────────────────

    // FIX CS1061: was ApiClient.Instance.Post<StorePurchaseTxData>() which does not exist.
    // Now uses PostAsync (returns string) + manual Newtonsoft deserialize.
    public async Task<bool> PurchaseStoreItem(int itemId, Action<string> onTxSent = null)
    {
        if (!IsConnected) { OnError?.Invoke("Wallet not connected"); return false; }

        try
        {
            string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { itemId });
            string json = await ApiClient.Instance.PostAsync("/payment/prepare-store-tx", body);
            var response = Newtonsoft.Json.JsonConvert.DeserializeObject<StorePurchaseTxData>(json);

            if (response == null || string.IsNullOrEmpty(response.deepLink))
            {
                OnError?.Invoke("Failed to prepare transaction");
                return false;
            }

            _pendingTxCallback = onTxSent;
            Debug.Log($"[Wallet] Opening MetaMask for purchase: {response.deepLink}");
            Application.OpenURL(response.deepLink);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Purchase failed: {ex.Message}");
            return false;
        }
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
    void OnApplicationQuit() { SaveSession(); }

    // ── Deep link handler ─────────────────────────────────────────────────────

    private void OnDeepLinkActivated(string url)
    {
        Debug.Log($"[Wallet] Deep link received: {url}");
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
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(
                new { walletAddress = WalletAddress, signature = "mobile-direct" });
            await ApiClient.Instance.PostAsync("/auth/link-wallet", body);
            Debug.Log("[Wallet] Wallet linked to player.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Wallet] Link failed: {e.Message}");
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

    // ── Response Models (private, not shared) ─────────────────────────────────

    [Serializable]
    private class BalanceResponse
    {
        public bool   success;
        public string balance;
        public string balanceFormatted;
        public string symbol;
        public int    tier;
    }

    [Serializable]
    public class StorePurchaseTxData
    {
        public string deepLink;
        public string itemName;
        public float  priceETH;
        public string message;
    }
}