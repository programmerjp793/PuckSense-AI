// Assets/Scripts/Store/StoreManager.cs
// Native ETH only (Sepolia Testnet)
//
// FIXES:
//   CS0101 / CS0229 (PurchaseResult duplicate): REMOVED PurchaseResult class from
//     bottom of this file. It is now defined ONLY in SharedModels.cs.
//   CS0579 (duplicate [Serializable]): caused by the duplicate PurchaseResult — fixed.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;

public class StoreManager : MonoBehaviour
{
    public static StoreManager Instance { get; private set; }

    [Header("Coins & ETH Display")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text ttkBalanceText;   // rename label to "ETH Balance" in Inspector
    [Tooltip("Shows 'Loading...' while fetching blockchain items")]
    [SerializeField] private GameObject loadingIndicator;

    [Header("Detail Panel")]
    [SerializeField] private GameObject  itemDetailPanel;
    [SerializeField] private Image       detailItemIcon;
    [SerializeField] private TMP_Text    detailItemName;
    [SerializeField] private TMP_Text    detailItemType;
    [SerializeField] private TMP_Text    detailItemDescription;
    [SerializeField] private Button      detailPurchaseButton;
    [SerializeField] private TMP_Text    detailPriceText;
    [SerializeField] private Button      detailCloseButton;
    [Tooltip("Shows 'Processing payment...' overlay during blockchain/PayMongo purchase")]
    [SerializeField] private GameObject  purchasingOverlay;
    [SerializeField] private TMP_Text    purchasingStatusText;

    [Header("Local Store Items (Skins / Powerups)")]
    [SerializeField] private List<StoreItemButton> storeItemButtons = new();

    [Header("Blockchain Store Items (ETH / Fiat)")]
    [Tooltip("Assign UI buttons for blockchain items (wallet upgrades, feature unlocks)")]
    [SerializeField] private List<BlockchainItemButton> blockchainItemButtons = new();

    // PurchaseResult comes from SharedModels.cs — do not redefine it here
    [HideInInspector] public UnityEvent<List<BlockchainStoreItem>> OnBlockchainItemsLoaded = new();
    [HideInInspector] public UnityEvent<string>                    OnPurchaseStarted       = new();
    [HideInInspector] public UnityEvent<PurchaseResult>            OnPurchaseCompleted     = new();
    [HideInInspector] public UnityEvent<string>                    OnPurchaseFailed        = new();

    public List<BlockchainStoreItem> BlockchainCatalog  { get; private set; } = new();
    public List<string>              OwnedBlockchainIds { get; private set; } = new();

    private int    _playerCoins;
    private string _selectedLocalItemId;
    private BlockchainStoreItem _selectedBlockchainItem;
    private LocalStoreItem      _selectedLocalItem;

    private string _pendingPaymentIntentId;
    private string _pendingItemId;
    private bool   _polling;
    private bool   _isBlockchainMode;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        LoadPlayerCoins();
        UpdateCoinsDisplay();
    }

    void Start()
    {
        LoadPlayerCoins();
        UpdateCoinsDisplay();
        SetupLocalItemButtons();
        SetupDetailPanel();
        HideAllOverlays();

        if (ApiClient.Instance != null)
            LoadBlockchainItems();

        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.AddListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnCoinsUpdated.AddListener(OnCoinsUpdatedFromWallet);
            UpdateEthDisplay(WalletManager.Instance.EthBalance);

            _playerCoins = WalletManager.Instance.CoinsBalance;
            UpdateCoinsDisplay();
        }
    }

    void OnDestroy()
    {
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.RemoveListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnCoinsUpdated.RemoveListener(OnCoinsUpdatedFromWallet);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  LOCAL STORE (Coins-based: Skins, Powerups)
    // ═══════════════════════════════════════════════════════════════════════════

    void SetupLocalItemButtons()
    {
        foreach (var itemButton in storeItemButtons)
        {
            if (itemButton.button == null || itemButton.itemData == null)
            {
                Debug.LogWarning("[Store] StoreItemButton has missing button or itemData.");
                continue;
            }
            itemButton.button.onClick.RemoveAllListeners();
            var capturedItem = itemButton.itemData;
            itemButton.button.onClick.AddListener(() => ShowLocalItemDetails(capturedItem));
            Debug.Log($"[Store] Local button connected: {itemButton.itemData.itemName}");
        }
    }

    void ShowLocalItemDetails(LocalStoreItem item)
    {
        _isBlockchainMode       = false;
        _selectedLocalItem      = item;
        _selectedBlockchainItem = null;

        if (itemDetailPanel == null) return;
        itemDetailPanel.SetActive(true);

        if (detailItemIcon != null)
        {
            detailItemIcon.enabled        = item.icon != null;
            detailItemIcon.sprite         = item.icon;
            detailItemIcon.preserveAspect = true;
        }

        if (detailItemName != null)        detailItemName.text        = item.itemName;
        if (detailItemType != null)        detailItemType.text        = GetLocalItemTypeDisplay(item.itemType);
        if (detailItemDescription != null) detailItemDescription.text = item.description;

        bool isPurchased = PlayerPrefs.GetInt("Purchased_" + item.itemID, 0) == 1;
        SetupPurchaseButton(
            isPurchased: isPurchased,
            canAfford:   _playerCoins >= item.price,
            isFree:      item.price == 0,
            priceLabel:  item.price == 0 ? "GET" : item.price + " COINS",
            onBuyAction: PurchaseLocalItem
        );
    }

    void PurchaseLocalItem()
    {
        if (_selectedLocalItem == null) return;

        if (_playerCoins < _selectedLocalItem.price)
        {
            FlashPriceError("NOT ENOUGH COINS!");
            return;
        }

        _playerCoins -= _selectedLocalItem.price;
        SavePlayerCoins();
        UpdateCoinsDisplay();
        WalletManager.Instance?.SyncCoinsFromPrefs();

        PlayerPrefs.SetInt("Purchased_" + _selectedLocalItem.itemID, 1);
        PlayerPrefs.Save();

        ApplyLocalItemEffect(_selectedLocalItem);

        Debug.Log($"[Store] Purchased local item: {_selectedLocalItem.itemName}");

        if (detailPriceText != null)      detailPriceText.text             = "OWNED";
        if (detailPurchaseButton != null) detailPurchaseButton.interactable = false;

        OnPurchaseCompleted.Invoke(new PurchaseResult
        {
            itemId      = _selectedLocalItem.itemID,
            paymentType = "local_coins",
        });

        Invoke(nameof(CloseDetailPanel), 1.5f);
    }

    void ApplyLocalItemEffect(LocalStoreItem item)
    {
        switch (item.itemType)
        {
            case ItemType.PaddleSkin:  PlayerPrefs.SetString("SelectedPaddleSkin",  item.itemID); break;
            case ItemType.PuckSkin:    PlayerPrefs.SetString("SelectedPuckSkin",    item.itemID); break;
            case ItemType.TableTheme:  PlayerPrefs.SetString("SelectedTableTheme",  item.itemID); break;
            case ItemType.Powerup:     PlayerPrefs.SetInt("Powerup_" + item.itemID, 1);           break;
        }
        PlayerPrefs.Save();
        Debug.Log($"[Store] Applied effect for: {item.itemID}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BLOCKCHAIN STORE (ETH / PayMongo fiat)
    // ═══════════════════════════════════════════════════════════════════════════

    public async void LoadBlockchainItems()
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(true);
        try
        {
            string json = await ApiClient.Instance.GetAsync("/store/items");
            var    resp = JsonConvert.DeserializeObject<ItemsResponse>(json);

            if (resp?.success == true)
            {
                BlockchainCatalog = resp.items ?? new List<BlockchainStoreItem>();
                Debug.Log($"[Store] Loaded {BlockchainCatalog.Count} blockchain items (ETH pricing).");
                await RefreshOwnedBlockchainItemsAsync();
                BindBlockchainItemButtons();
                OnBlockchainItemsLoaded.Invoke(BlockchainCatalog);
            }
            else
            {
                Debug.LogWarning("[Store] Failed to load blockchain items: " + resp?.message);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Store] Blockchain items load failed: {e.Message}");
        }
        finally
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
        }
    }

    void BindBlockchainItemButtons()
    {
        foreach (var btn in blockchainItemButtons)
        {
            if (btn.button == null) continue;
            var catalogItem = BlockchainCatalog.Find(i => i.itemId == btn.itemId);
            if (catalogItem == null) continue;
            btn.button.onClick.RemoveAllListeners();
            var captured = catalogItem;
            btn.button.onClick.AddListener(() => ShowBlockchainItemDetails(captured));
        }
    }

    void ShowBlockchainItemDetails(BlockchainStoreItem item)
    {
        _isBlockchainMode       = true;
        _selectedBlockchainItem = item;
        _selectedLocalItem      = null;

        if (itemDetailPanel == null) return;
        itemDetailPanel.SetActive(true);

        if (detailItemIcon != null)
        {
            var btn = blockchainItemButtons.Find(b => b.itemId == item.itemId);
            detailItemIcon.sprite  = btn?.icon;
            detailItemIcon.enabled = btn?.icon != null;
        }

        if (detailItemName != null)
            detailItemName.text = item.name;
        if (detailItemType != null)
            detailItemType.text = item.itemType == "wallet_upgrade" ? "Wallet Upgrade" : "Feature Unlock";
        if (detailItemDescription != null)
            detailItemDescription.text =
                $"Blockchain item\nPrice: {item.priceETHFormatted} ETH  |  PHP {item.pricePHP:F2}";

        bool isPurchased = OwnedBlockchainIds.Contains(item.itemId);
        bool walletOk    = WalletManager.Instance != null && WalletManager.Instance.IsConnected;

        string priceLabel = isPurchased ? "OWNED"
                          : !walletOk   ? "Connect Wallet"
                          : $"{item.priceETHFormatted} ETH  |  Pay with GCash/Card";

        SetupPurchaseButton(
            isPurchased: isPurchased,
            canAfford:   walletOk,
            isFree:      false,
            priceLabel:  priceLabel,
            onBuyAction: () => PurchaseBlockchainItemWithFiat(item.itemId)
        );
    }

    public async void PurchaseBlockchainItemWithFiat(string itemId)
    {
        if (!WalletManager.Instance.IsConnected)
        {
            Debug.LogWarning("[Store] Wallet not connected.");
            FlashPriceError("Connect wallet first!");
            return;
        }

        try
        {
            OnPurchaseStarted.Invoke(itemId);
            ShowPurchasingOverlay("Creating payment...");

            string body = JsonConvert.SerializeObject(new { itemId });
            string json = await ApiClient.Instance.PostAsync("/payment/create-intent", body);
            var    resp = JsonConvert.DeserializeObject<CreateIntentResponse>(json);

            if (resp?.success != true)
            {
                HidePurchasingOverlay();
                OnPurchaseFailed.Invoke(resp?.message ?? "Payment creation failed");
                FlashPriceError("Payment failed. Try again.");
                return;
            }

            _pendingPaymentIntentId = resp.paymentIntentId;
            _pendingItemId          = itemId;

            ShowPurchasingOverlay("Opening PayMongo checkout...");
            Debug.Log($"[Store] Opening PayMongo: {resp.checkoutUrl}");
            Application.OpenURL(resp.checkoutUrl);
            StartPaymentPolling(resp.paymentIntentId);
        }
        catch (Exception e)
        {
            HidePurchasingOverlay();
            Debug.LogError($"[Store] Fiat purchase error: {e.Message}");
            OnPurchaseFailed.Invoke(e.Message);
            FlashPriceError("Error. Try again.");
        }
    }

    public async void CheckPaymentStatus()
    {
        if (string.IsNullOrEmpty(_pendingPaymentIntentId)) return;
        await PollPaymentStatusAsync(_pendingPaymentIntentId);
    }

    private async void StartPaymentPolling(string intentId)
    {
        if (_polling) return;
        _polling = true;
        ShowPurchasingOverlay("Waiting for payment confirmation...");

        int maxAttempts = 40;
        int attempt     = 0;

        while (_polling && attempt < maxAttempts)
        {
            await Task.Delay(3000);
            attempt++;
            ShowPurchasingOverlay($"Checking payment... ({attempt * 3}s)");
            bool done = await PollPaymentStatusAsync(intentId);
            if (done) break;
        }

        if (_polling)
        {
            _polling = false;
            HidePurchasingOverlay();
            OnPurchaseFailed.Invoke("Payment confirmation timed out. Check your payment history.");
        }
    }

    private async Task<bool> PollPaymentStatusAsync(string intentId)
    {
        try
        {
            string json = await ApiClient.Instance.GetAsync($"/payment/status/{intentId}");
            var    resp = JsonConvert.DeserializeObject<PaymentStatusResponse>(json);

            Debug.Log($"[Store] Payment status: {resp?.status}");

            if (resp?.status == "succeeded" || resp?.status == "confirmed")
            {
                _polling = false;
                HidePurchasingOverlay();

                if (_pendingItemId != null && !OwnedBlockchainIds.Contains(_pendingItemId))
                    OwnedBlockchainIds.Add(_pendingItemId);

                var result = new PurchaseResult
                {
                    itemId      = _pendingItemId,
                    txHash      = resp.txHash,
                    explorerUrl = resp.explorerUrl,
                    paymentType = "fiat",
                };

                _pendingPaymentIntentId = null;
                _pendingItemId          = null;

                OnPurchaseCompleted.Invoke(result);

                WalletManager.Instance?.RefreshBalance();
                WalletManager.Instance?.SaveSession();

                if (_selectedBlockchainItem != null)
                    ShowBlockchainItemDetails(_selectedBlockchainItem);

                Debug.Log($"[Store] Payment confirmed! TxHash: {result.txHash}");
                return true;
            }

            if (resp?.status is "cancelled" or "payment_failed")
            {
                _polling = false;
                HidePurchasingOverlay();
                OnPurchaseFailed.Invoke("Payment was cancelled or failed.");
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Store] Poll error: {e.Message}");
        }

        return false;
    }

    private async Task RefreshOwnedBlockchainItemsAsync()
    {
        try
        {
            string json = await ApiClient.Instance.GetAsync("/store/owned");
            var    resp = JsonConvert.DeserializeObject<OwnedResponse>(json);
            if (resp?.success == true)
                OwnedBlockchainIds = new List<string>(resp.ownedItems ?? Array.Empty<string>());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Store] Owned items refresh failed: {e.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SHARED UI HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    void SetupDetailPanel()
    {
        if (itemDetailPanel != null)   itemDetailPanel.SetActive(false);
        if (detailCloseButton != null) detailCloseButton.onClick.AddListener(CloseDetailPanel);
    }

    void SetupPurchaseButton(bool isPurchased, bool canAfford, bool isFree,
                             string priceLabel, Action onBuyAction)
    {
        if (detailPurchaseButton == null || detailPriceText == null) return;

        detailPurchaseButton.onClick.RemoveAllListeners();
        detailPriceText.text = priceLabel;

        var colors = detailPurchaseButton.colors;

        if (isPurchased)
        {
            detailPurchaseButton.interactable = false;
            colors.normalColor = colors.disabledColor = new Color(0.5f, 0.5f, 0.5f);
        }
        else if (isFree || canAfford)
        {
            detailPurchaseButton.interactable = true;
            colors.normalColor      = new Color(0.2f, 0.6f, 0.9f);
            colors.highlightedColor = new Color(0.3f, 0.7f, 1.0f);
            colors.pressedColor     = new Color(0.1f, 0.4f, 0.7f);
            detailPurchaseButton.onClick.AddListener(() => onBuyAction?.Invoke());
        }
        else
        {
            detailPurchaseButton.interactable = false;
            colors.normalColor = colors.disabledColor = new Color(0.6f, 0.3f, 0.3f);
        }

        detailPurchaseButton.colors = colors;
    }

    void CloseDetailPanel()
    {
        if (itemDetailPanel != null) itemDetailPanel.SetActive(false);
        _selectedLocalItem      = null;
        _selectedBlockchainItem = null;
        HidePurchasingOverlay();
    }

    void FlashPriceError(string message)
    {
        if (detailPriceText == null) return;
        detailPriceText.text  = message;
        detailPriceText.color = Color.red;
        CancelInvoke(nameof(ResetPriceText));
        Invoke(nameof(ResetPriceText), 2f);
    }

    void ResetPriceText()
    {
        if (detailPriceText == null) return;
        detailPriceText.color = Color.white;

        if (_isBlockchainMode && _selectedBlockchainItem != null)
            detailPriceText.text = $"{_selectedBlockchainItem.priceETHFormatted} ETH  |  Pay with GCash/Card";
        else if (!_isBlockchainMode && _selectedLocalItem != null)
            detailPriceText.text = _selectedLocalItem.price == 0
                ? "GET" : _selectedLocalItem.price + " COINS";
    }

    void ShowPurchasingOverlay(string message)
    {
        if (purchasingOverlay != null)    purchasingOverlay.SetActive(true);
        if (purchasingStatusText != null) purchasingStatusText.text = message;
    }

    void HidePurchasingOverlay()
    {
        if (purchasingOverlay != null) purchasingOverlay.SetActive(false);
    }

    void HideAllOverlays()
    {
        HidePurchasingOverlay();
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
    }

    // ─── Coins ────────────────────────────────────────────────────────────────

    void UpdateCoinsDisplay()
    {
        if (coinsText != null) coinsText.text = _playerCoins.ToString();
    }

    void UpdateEthDisplay(string balance)
    {
        if (ttkBalanceText != null) ttkBalanceText.text = balance + " ETH";
    }

    void OnEthBalanceUpdated(string balance) => UpdateEthDisplay(balance);

    void OnCoinsUpdatedFromWallet(int coins)
    {
        _playerCoins = coins;
        UpdateCoinsDisplay();
        Debug.Log($"[Store] Coins synced from WalletManager: {coins}");
    }

    void SavePlayerCoins()
    {
        PlayerPrefs.SetInt("PlayerCoins", _playerCoins);
        PlayerPrefs.Save();
    }

    void LoadPlayerCoins()
    {
        _playerCoins = PlayerPrefs.GetInt("PlayerCoins", 100);
    }

    public void AddCoins(int amount)
    {
        _playerCoins += amount;
        SavePlayerCoins();
        UpdateCoinsDisplay();
        WalletManager.Instance?.SyncCoinsFromPrefs();
    }

    public static void RewardCoins(int amount)
    {
        int current = PlayerPrefs.GetInt("PlayerCoins", 100) + amount;
        PlayerPrefs.SetInt("PlayerCoins", current);
        PlayerPrefs.Save();
        Debug.Log($"[Store] Rewarded {amount} coins. Total: {current}");
        if (Instance != null)
        {
            Instance._playerCoins = current;
            Instance.UpdateCoinsDisplay();
        }
    }

    // ─── Public helpers ───────────────────────────────────────────────────────

    public bool PlayerOwnsBlockchainItem(string itemId) => OwnedBlockchainIds.Contains(itemId);

    public bool PlayerOwnsLocalItem(string itemId) =>
        PlayerPrefs.GetInt("Purchased_" + itemId, 0) == 1;

    string GetLocalItemTypeDisplay(ItemType type) => type switch
    {
        ItemType.PaddleSkin => "Paddle Skin",
        ItemType.PuckSkin   => "Puck Skin",
        ItemType.TableTheme => "Table Theme",
        ItemType.Powerup    => "Power-up",
        _                   => type.ToString(),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    //  DATA MODELS  (local to StoreManager only)
    // ═══════════════════════════════════════════════════════════════════════════

    [Serializable]
    public class BlockchainStoreItem
    {
        public string itemId;            // string key e.g. "wallet_upgrade_1"
        public int    numericId;         // SmartStore.sol item ID (1-4)
        public string name;
        public string itemType;          // "wallet_upgrade" | "ai_replay" | "custom_skin"
        public float  priceETH;          // native ETH price
        public string priceETHFormatted; // e.g. "0.0010"
        public float  pricePHP;          // fiat price in PHP
        public int    tier;
        public bool   active;
    }

    [Serializable] private class ItemsResponse
    {
        public bool                      success;
        public string                    message;
        public List<BlockchainStoreItem> items;
    }

    [Serializable] private class OwnedResponse
    {
        public bool     success;
        public string[] ownedItems;
    }

    [Serializable] private class CreateIntentResponse
    {
        public bool   success;
        public string message;
        public string paymentIntentId;
        public string clientKey;
        public int    amount;
        public string checkoutUrl;
    }

    [Serializable] private class PaymentStatusResponse
    {
        public bool   success;
        public string status;
        public string txHash;
        public string itemId;
        public string explorerUrl;
    }

    // ── NOTE: PurchaseResult is NOT here — it lives in SharedModels.cs ────────
}

// ─── Supporting types (global, used by Inspector) ────────────────────────────

[Serializable]
public class StoreItemButton
{
    public Button         button;
    public LocalStoreItem itemData;
}

[Serializable]
public class LocalStoreItem
{
    public string   itemID;
    public string   itemName;
    public string   description;
    public int      price;
    public Sprite   icon;
    public ItemType itemType;
}

public enum ItemType
{
    PaddleSkin,
    PuckSkin,
    TableTheme,
    Powerup,
}

[Serializable]
public class BlockchainItemButton
{
    public Button button;
    public string itemId;
    public Sprite icon;
}

// ── PurchaseResult intentionally omitted — defined in SharedModels.cs ─────────