// Assets/Scripts/Store/StoreManager.cs
// Native ETH only (Sepolia Testnet)
//
// BACKEND WIRING:
//   GET  /store/items               → store.js  → blockchainService.getStoreItems()
//   GET  /store/owned               → store.js  → blockchainService.getPlayerItems()
//   POST /purchase/create-intent    → payment.js → in-memory intent store
//   GET  /purchase/status/:intentId → payment.js → intent status poll
//   POST /purchase/prepare-store-tx → payment.js → blockchainService.prepareStorePurchaseTx()
//
// DATA MAPPING (blockchainService.getStoreItems response → BlockchainStoreItem):
//   itemId          ← item.itemId  (string: "wallet_upgrade_1", etc.)
//   numericId       ← item.numericId
//   name            ← item.name
//   priceETH        ← item.price   (float, already in ETH)
//   priceETHFormatted ← formatted from item.price
//   pricePHP        ← derived from priceETH × PHP_RATE (configurable)
//   itemType        ← mapped from itemId prefix
//
// PurchaseResult is defined in SharedModels.cs — do NOT redefine here.

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

    // ── PHP conversion rate (update to match your real rate) ─────────────────
    [Header("Fiat Conversion")]
    [Tooltip("ETH → PHP exchange rate used for display only")]
    [SerializeField] private float ethToPHPRate = 170000f;   // ~₱170,000 per ETH

    // ── UI References ─────────────────────────────────────────────────────────
    [Header("Coins & ETH Display")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text ttkBalanceText;   // label: "ETH Balance"
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
    [SerializeField] private GameObject  purchasingOverlay;
    [SerializeField] private TMP_Text    purchasingStatusText;

    [Header("Local Store Items (Skins / Powerups)")]
    [SerializeField] private List<StoreItemButton> storeItemButtons = new();

    [Header("Blockchain Store Items (ETH / Fiat)")]
    [SerializeField] private List<BlockchainItemButton> blockchainItemButtons = new();

    // ── Events ────────────────────────────────────────────────────────────────
    [HideInInspector] public UnityEvent<List<BlockchainStoreItem>> OnBlockchainItemsLoaded = new();
    [HideInInspector] public UnityEvent<string>                    OnPurchaseStarted       = new();
    [HideInInspector] public UnityEvent<PurchaseResult>            OnPurchaseCompleted     = new();
    [HideInInspector] public UnityEvent<string>                    OnPurchaseFailed        = new();

    // ── Public state ──────────────────────────────────────────────────────────
    public List<BlockchainStoreItem> BlockchainCatalog  { get; private set; } = new();
    public List<string>              OwnedBlockchainIds { get; private set; } = new();

    // ── Private state ─────────────────────────────────────────────────────────
    private int    _playerCoins;
    private BlockchainStoreItem _selectedBlockchainItem;
    private LocalStoreItem      _selectedLocalItem;
    private string _pendingPaymentIntentId;
    private string _pendingItemId;
    private bool   _polling;
    private bool   _isBlockchainMode;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

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

        // Subscribe to wallet events
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.AddListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnCoinsUpdated.AddListener(OnCoinsUpdatedFromWallet);
            UpdateEthDisplay(WalletManager.Instance.EthBalance);
            _playerCoins = WalletManager.Instance.CoinsBalance;
            UpdateCoinsDisplay();
        }

        // Fetch store catalog from backend
        if (ApiClient.Instance != null)
            _ = LoadBlockchainItemsAsync();
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
    //  LOCAL STORE  (Coins — skins / powerups)
    // ═══════════════════════════════════════════════════════════════════════════

    private void SetupLocalItemButtons()
    {
        foreach (var itemButton in storeItemButtons)
        {
            if (itemButton.button == null || itemButton.itemData == null)
            {
                Debug.LogWarning("[Store] StoreItemButton missing button or itemData.");
                continue;
            }
            itemButton.button.onClick.RemoveAllListeners();
            var captured = itemButton.itemData;
            itemButton.button.onClick.AddListener(() => ShowLocalItemDetails(captured));
            Debug.Log($"[Store] Local button wired: {itemButton.itemData.itemName}");
        }
    }

    private void ShowLocalItemDetails(LocalStoreItem item)
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
            priceLabel:  item.price == 0 ? "GET" : $"{item.price} COINS",
            onBuyAction: PurchaseLocalItem
        );
    }

    private void PurchaseLocalItem()
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

        if (detailPriceText != null)      detailPriceText.text              = "OWNED";
        if (detailPurchaseButton != null) detailPurchaseButton.interactable  = false;

        OnPurchaseCompleted.Invoke(new PurchaseResult
        {
            itemId      = _selectedLocalItem.itemID,
            paymentType = "local_coins",
        });

        Invoke(nameof(CloseDetailPanel), 1.5f);
    }

    private void ApplyLocalItemEffect(LocalStoreItem item)
    {
        switch (item.itemType)
        {
            case ItemType.PaddleSkin:  PlayerPrefs.SetString("SelectedPaddleSkin",  item.itemID); break;
            case ItemType.PuckSkin:    PlayerPrefs.SetString("SelectedPuckSkin",    item.itemID); break;
            case ItemType.TableTheme:  PlayerPrefs.SetString("SelectedTableTheme",  item.itemID); break;
            case ItemType.Powerup:     PlayerPrefs.SetInt("Powerup_" + item.itemID, 1);           break;
        }
        PlayerPrefs.Save();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BLOCKCHAIN STORE  (ETH / PayMongo fiat)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches catalog from GET /store/items then owned items from GET /store/owned.
    /// blockchainService.getStoreItems() returns items with fields:
    ///   itemId (string), numericId, name, price (float ETH), priceWei, isAvailable
    /// We map these onto BlockchainStoreItem and derive pricePHP + priceETHFormatted.
    /// </summary>
    public async void LoadBlockchainItems() => await LoadBlockchainItemsAsync();

    public async Task LoadBlockchainItemsAsync()
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(true);
        try
        {
            string json = await ApiClient.Instance.GetAsync("/store/items");
            var    resp = JsonConvert.DeserializeObject<ItemsResponse>(json);

            if (resp?.success == true)
            {
                // Map raw backend items → BlockchainStoreItem (fill derived fields)
                var catalog = new List<BlockchainStoreItem>();
                foreach (var raw in resp.items ?? new List<RawStoreItem>())
                {
                    catalog.Add(MapRawItem(raw));
                }

                BlockchainCatalog = catalog;
                Debug.Log($"[Store] Loaded {BlockchainCatalog.Count} blockchain items.");

                await RefreshOwnedBlockchainItemsAsync();
                BindBlockchainItemButtons();
                OnBlockchainItemsLoaded.Invoke(BlockchainCatalog);
            }
            else
            {
                Debug.LogWarning("[Store] /store/items returned success=false: " + resp?.message);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Store] LoadBlockchainItems failed: {e.Message}");
        }
        finally
        {
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
        }
    }

    /// <summary>
    /// Maps a raw backend item (from blockchainService.getStoreItems) to
    /// the Unity-side BlockchainStoreItem, filling in derived display fields.
    /// </summary>
    private BlockchainStoreItem MapRawItem(RawStoreItem raw)
    {
        float priceETH = raw.price;   // already in ETH from fromWei() in blockchainService
        string formatted = priceETH.ToString("F4");

        // Infer itemType from the string itemId
        string itemType = "feature_unlock";
        if (raw.itemId?.Contains("wallet_upgrade") == true) itemType = "wallet_upgrade";
        else if (raw.itemId?.Contains("ai_replay")  == true) itemType = "feature_unlock";
        else if (raw.itemId?.Contains("custom_skin") == true) itemType = "custom_skin";

        return new BlockchainStoreItem
        {
            itemId           = raw.itemId ?? $"item_{raw.numericId}",
            numericId        = raw.numericId,
            name             = raw.name ?? "Unknown Item",
            itemType         = itemType,
            priceETH         = priceETH,
            priceETHFormatted = formatted,
            pricePHP         = priceETH * ethToPHPRate,
            active           = raw.isAvailable,
        };
    }

    private void BindBlockchainItemButtons()
    {
        foreach (var btn in blockchainItemButtons)
        {
            if (btn.button == null) continue;
            var catalogItem = BlockchainCatalog.Find(i => i.itemId == btn.itemId);
            if (catalogItem == null) continue;
            btn.button.onClick.RemoveAllListeners();
            var captured = catalogItem;
            btn.button.onClick.AddListener(() => ShowBlockchainItemDetails(captured));
            Debug.Log($"[Store] Blockchain button wired: {catalogItem.name}");
        }
    }

    private void ShowBlockchainItemDetails(BlockchainStoreItem item)
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
                $"Blockchain item\nPrice: {item.priceETHFormatted} ETH  |  ₱{item.pricePHP:F2}";

        bool isPurchased = OwnedBlockchainIds.Contains(item.itemId);
        bool walletOk    = WalletManager.Instance != null && WalletManager.Instance.IsConnected;

        string priceLabel = isPurchased ? "OWNED"
                          : !walletOk   ? "Connect Wallet First"
                          : $"{item.priceETHFormatted} ETH  |  Buy with Wallet";

        SetupPurchaseButton(
            isPurchased: isPurchased,
            canAfford:   walletOk,
            isFree:      false,
            priceLabel:  priceLabel,
            onBuyAction: () => PurchaseBlockchainItemViaWebApp(item.itemId, item.numericId)
        );
    }

    // ── Web App Purchase Flow ─────────────────────────────────────────────────

    /// <summary>
    /// Initiates purchase via the React Native wallet web app.
    /// Opens the web app in the device browser → user connects MetaMask → signs tx.
    /// When user returns to the game, polls ownership to confirm purchase.
    /// </summary>
    public async void PurchaseBlockchainItemViaWebApp(string itemId, int numericId)
    {
        if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
        {
            FlashPriceError("Connect wallet first!");
            return;
        }

        try
        {
            OnPurchaseStarted.Invoke(itemId);
            ShowPurchasingOverlay("Preparing transaction...");

            bool opened = await WalletManager.Instance.PurchaseViaWebApp(numericId);

            if (!opened)
            {
                HidePurchasingOverlay();
                FlashPriceError("Failed to open wallet app.");
                OnPurchaseFailed.Invoke("Failed to open wallet web app");
                return;
            }

            ShowPurchasingOverlay("Complete purchase in browser...\nReturn here after confirming.");

            // Store pending item info for when user returns
            _pendingItemId = itemId;
            _pendingWebPurchaseNumericId = numericId;
            _awaitingWebPurchase = true;

        }
        catch (Exception e)
        {
            HidePurchasingOverlay();
            Debug.LogError($"[Store] Web app purchase error: {e.Message}");
            OnPurchaseFailed.Invoke(e.Message);
            FlashPriceError("Error. Try again.");
        }
    }

    // Tracking state for web app purchase polling
    private int  _pendingWebPurchaseNumericId;
    private bool _awaitingWebPurchase;

    /// <summary>
    /// Called when the app regains focus after web app purchase.
    /// Polls ownership to check if the purchase completed.
    /// </summary>
    private async void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && _awaitingWebPurchase && _pendingWebPurchaseNumericId > 0)
        {
            _awaitingWebPurchase = false;
            ShowPurchasingOverlay("Verifying purchase...");

            bool owned = await WalletManager.Instance.PollOwnershipAsync(
                _pendingWebPurchaseNumericId, maxAttempts: 15, delayMs: 3000
            );

            if (owned)
            {
                HidePurchasingOverlay();
                string stringItemId = _pendingItemId;

                if (!string.IsNullOrEmpty(stringItemId) && !OwnedBlockchainIds.Contains(stringItemId))
                    OwnedBlockchainIds.Add(stringItemId);

                var result = new PurchaseResult
                {
                    itemId      = stringItemId,
                    paymentType = "web_app_eth",
                };

                OnPurchaseCompleted.Invoke(result);
                WalletManager.Instance?.RefreshBalance();
                WalletManager.Instance?.SaveSession();

                // Refresh the detail panel
                if (_selectedBlockchainItem != null)
                    ShowBlockchainItemDetails(_selectedBlockchainItem);

                Debug.Log($"[Store] Web app purchase confirmed! Item: {stringItemId}");
            }
            else
            {
                HidePurchasingOverlay();
                OnPurchaseFailed.Invoke("Purchase not confirmed. If you completed the transaction, please wait and try refreshing.");
                FlashPriceError("Not confirmed yet. Try again.");
            }

            _pendingItemId = null;
            _pendingWebPurchaseNumericId = 0;
        }
    }

    // ── Fiat Purchase (PayMongo / GCash — legacy) ─────────────────────────────

    /// <summary>
    /// Legacy: POST /purchase/create-intent  (payment.js)
    /// Then opens checkout URL and polls GET /purchase/status/:intentId
    /// </summary>
    public async void PurchaseBlockchainItemWithFiat(string itemId)
    {
        if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
        {
            FlashPriceError("Connect wallet first!");
            return;
        }

        try
        {
            OnPurchaseStarted.Invoke(itemId);
            ShowPurchasingOverlay("Creating payment...");

            string body = JsonConvert.SerializeObject(new { itemId });
            string json = await ApiClient.Instance.PostAsync("/purchase/create-intent", body);
            var    resp = JsonConvert.DeserializeObject<CreateIntentResponse>(json);

            if (resp?.success != true)
            {
                HidePurchasingOverlay();
                string err = resp?.message ?? "Payment creation failed";
                OnPurchaseFailed.Invoke(err);
                FlashPriceError("Payment failed. Try again.");
                return;
            }

            _pendingPaymentIntentId = resp.paymentIntentId;
            _pendingItemId          = itemId;

            // Open PayMongo checkout if URL provided
            if (!string.IsNullOrEmpty(resp.checkoutUrl))
            {
                ShowPurchasingOverlay("Opening PayMongo checkout...");
                Debug.Log($"[Store] PayMongo URL: {resp.checkoutUrl}");
                Application.OpenURL(resp.checkoutUrl);
            }
            else
            {
                ShowPurchasingOverlay("Waiting for payment confirmation...");
                Debug.LogWarning("[Store] No checkoutUrl returned — polling status immediately.");
            }

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
        if (!string.IsNullOrEmpty(_pendingPaymentIntentId))
            await PollPaymentStatusAsync(_pendingPaymentIntentId);
    }

    private async void StartPaymentPolling(string intentId)
    {
        if (_polling) return;
        _polling = true;

        const int maxAttempts = 40;  // 40 × 3s = 2 min
        int attempt = 0;

        while (_polling && attempt < maxAttempts)
        {
            await Task.Delay(3000);
            attempt++;
            ShowPurchasingOverlay($"Checking payment... ({attempt * 3}s)");
            bool done = await PollPaymentStatusAsync(intentId);
            if (done) return;
        }

        // Timed out
        _polling = false;
        HidePurchasingOverlay();
        OnPurchaseFailed.Invoke("Payment confirmation timed out. Check your payment history.");
    }

    /// <summary>GET /purchase/status/:intentId (payment.js)</summary>
    private async Task<bool> PollPaymentStatusAsync(string intentId)
    {
        try
        {
            string json = await ApiClient.Instance.GetAsync($"/purchase/status/{intentId}");
            var    resp = JsonConvert.DeserializeObject<PaymentStatusResponse>(json);

            Debug.Log($"[Store] Intent status: {resp?.status}");

            if (resp?.status is "succeeded" or "confirmed")
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

                // Refresh the open detail panel
                if (_selectedBlockchainItem != null)
                    ShowBlockchainItemDetails(_selectedBlockchainItem);

                Debug.Log($"[Store] Fiat purchase confirmed! Tx: {result.txHash}");
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

    // ── Owned items ───────────────────────────────────────────────────────────

    /// <summary>
    /// GET /store/owned  (store.js)
    /// Requires wallet address — sent via X-Wallet-Address header (ApiClient injects it).
    /// Falls back to query param if wallet is connected.
    /// </summary>
    private async Task RefreshOwnedBlockchainItemsAsync()
    {
        try
        {
            // Build endpoint — include walletAddress as query param as extra fallback
            string endpoint = "/store/owned";
            if (WalletManager.Instance != null && WalletManager.Instance.IsConnected)
                endpoint += $"?walletAddress={Uri.EscapeDataString(WalletManager.Instance.WalletAddress)}";

            string json = await ApiClient.Instance.GetAsync(endpoint);
            var    resp = JsonConvert.DeserializeObject<OwnedResponse>(json);

            if (resp?.success == true)
            {
                OwnedBlockchainIds = new List<string>(resp.ownedItems ?? Array.Empty<string>());
                Debug.Log($"[Store] Owned items: {OwnedBlockchainIds.Count}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Store] RefreshOwnedItems failed: {e.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SHARED UI HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private void SetupDetailPanel()
    {
        if (itemDetailPanel != null)   itemDetailPanel.SetActive(false);
        if (detailCloseButton != null) detailCloseButton.onClick.AddListener(CloseDetailPanel);
    }

    private void SetupPurchaseButton(bool isPurchased, bool canAfford, bool isFree,
                                     string priceLabel, Action onBuyAction)
    {
        if (detailPurchaseButton == null || detailPriceText == null) return;

        detailPurchaseButton.onClick.RemoveAllListeners();
        detailPriceText.text = priceLabel;
        var colors = detailPurchaseButton.colors;

        if (isPurchased)
        {
            detailPurchaseButton.interactable  = false;
            colors.normalColor = colors.disabledColor = new Color(0.5f, 0.5f, 0.5f);
        }
        else if (isFree || canAfford)
        {
            detailPurchaseButton.interactable  = true;
            colors.normalColor      = new Color(0.2f, 0.6f, 0.9f);
            colors.highlightedColor = new Color(0.3f, 0.7f, 1.0f);
            colors.pressedColor     = new Color(0.1f, 0.4f, 0.7f);
            detailPurchaseButton.onClick.AddListener(() => onBuyAction?.Invoke());
        }
        else
        {
            detailPurchaseButton.interactable  = false;
            colors.normalColor = colors.disabledColor = new Color(0.6f, 0.3f, 0.3f);
        }

        detailPurchaseButton.colors = colors;
    }

    private void CloseDetailPanel()
    {
        if (itemDetailPanel != null) itemDetailPanel.SetActive(false);
        _selectedLocalItem      = null;
        _selectedBlockchainItem = null;
        HidePurchasingOverlay();
    }

    private void FlashPriceError(string message)
    {
        if (detailPriceText == null) return;
        detailPriceText.text  = message;
        detailPriceText.color = Color.red;
        CancelInvoke(nameof(ResetPriceText));
        Invoke(nameof(ResetPriceText), 2f);
    }

    private void ResetPriceText()
    {
        if (detailPriceText == null) return;
        detailPriceText.color = Color.white;

        if (_isBlockchainMode && _selectedBlockchainItem != null)
            detailPriceText.text = $"{_selectedBlockchainItem.priceETHFormatted} ETH  |  ₱{_selectedBlockchainItem.pricePHP:F2}";
        else if (!_isBlockchainMode && _selectedLocalItem != null)
            detailPriceText.text = _selectedLocalItem.price == 0 ? "GET" : $"{_selectedLocalItem.price} COINS";
    }

    private void ShowPurchasingOverlay(string message)
    {
        if (purchasingOverlay != null)    purchasingOverlay.SetActive(true);
        if (purchasingStatusText != null) purchasingStatusText.text = message;
    }

    private void HidePurchasingOverlay()
    {
        if (purchasingOverlay != null) purchasingOverlay.SetActive(false);
    }

    private void HideAllOverlays()
    {
        HidePurchasingOverlay();
        if (loadingIndicator != null) loadingIndicator.SetActive(false);
    }

    // ── Coins ─────────────────────────────────────────────────────────────────

    private void UpdateCoinsDisplay()
    {
        if (coinsText != null) coinsText.text = _playerCoins.ToString();
    }

    private void UpdateEthDisplay(string balance)
    {
        if (ttkBalanceText != null) ttkBalanceText.text = balance + " ETH";
    }

    private void OnEthBalanceUpdated(string balance)  => UpdateEthDisplay(balance);

    private void OnCoinsUpdatedFromWallet(int coins)
    {
        _playerCoins = coins;
        UpdateCoinsDisplay();
    }

    private void SavePlayerCoins()
    {
        PlayerPrefs.SetInt("PlayerCoins", _playerCoins);
        PlayerPrefs.Save();
    }

    private void LoadPlayerCoins()
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

    // ── Public helpers ────────────────────────────────────────────────────────

    public bool PlayerOwnsBlockchainItem(string itemId) => OwnedBlockchainIds.Contains(itemId);
    public bool PlayerOwnsLocalItem(string itemId)      =>
        PlayerPrefs.GetInt("Purchased_" + itemId, 0) == 1;

    private string GetLocalItemTypeDisplay(ItemType type) => type switch
    {
        ItemType.PaddleSkin => "Paddle Skin",
        ItemType.PuckSkin   => "Puck Skin",
        ItemType.TableTheme => "Table Theme",
        ItemType.Powerup    => "Power-up",
        _                   => type.ToString(),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    //  DATA MODELS — Unity-side
    // ═══════════════════════════════════════════════════════════════════════════

    [Serializable]
    public class BlockchainStoreItem
    {
        public string itemId;             // "wallet_upgrade_1" etc.
        public int    numericId;          // SmartStore.sol item ID (1-4)
        public string name;
        public string itemType;           // "wallet_upgrade" | "feature_unlock" | "custom_skin"
        public float  priceETH;           // ETH price (float)
        public string priceETHFormatted;  // e.g. "0.0010"
        public float  pricePHP;           // derived display-only fiat price
        public int    tier;
        public bool   active;
    }

    // ── Raw response DTOs (match blockchainService.getStoreItems JSON exactly) ──

    [Serializable]
    private class RawStoreItem
    {
        public string itemId;       // string key
        public int    numericId;
        public string name;
        public float  price;        // already in ETH (fromWei applied on server)
        public string priceWei;
        public bool   isAvailable;
    }

    [Serializable]
    private class ItemsResponse
    {
        public bool            success;
        public string          message;
        public int             count;
        public List<RawStoreItem> items;
    }

    [Serializable]
    private class OwnedResponse
    {
        public bool     success;
        public string[] ownedItems;
        public int      count;
    }

    [Serializable]
    private class CreateIntentResponse
    {
        public bool   success;
        public string message;
        public string paymentIntentId;
        public string clientKey;
        public int    amount;
        public string checkoutUrl;
        public string status;
        public int    itemId;
    }

    [Serializable]
    private class PaymentStatusResponse
    {
        public bool   success;
        public string status;
        public string txHash;
        public string itemId;
        public string explorerUrl;
        public string intentId;
    }

    // ── PurchaseResult lives in SharedModels.cs — not here ───────────────────
}

// ─── Inspector-facing supporting types ───────────────────────────────────────

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