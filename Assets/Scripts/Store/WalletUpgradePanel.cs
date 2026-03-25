// Assets/Scripts/Store/WalletUpgradePanel.cs
// Native ETH only (Sepolia Testnet)
//
// BACKEND WIRING (via StoreManager + WalletManager):
//   • Opens panel  → LoadUpgrades() waits for StoreManager.BlockchainCatalog
//                    (populated by GET /store/items in StoreManager.LoadBlockchainItemsAsync)
//   • Fiat buy     → StoreManager.PurchaseBlockchainItemWithFiat()
//                    → POST /purchase/create-intent  then polls GET /purchase/status/:id
//   • ETH  buy     → WalletManager.PurchaseStoreItem()
//                    → POST /purchase/prepare-store-tx  then MetaMask deep-link
//
// PREFAB REQUIREMENTS:
//   Assign in Inspector:
//     ttkBalanceText    — TMP_Text  showing ETH balance
//     closeButton       — Button    top-right close
//     itemsContainer    — Transform (ScrollView content rect or plain RectTransform)
//     upgradeItemCardPrefab — UpgradeItemCard prefab (Assets/Prefabs/UpgradeItemCard)
//     paymentStatusPanel — GameObject (overlay)
//     statusMessageText  — TMP_Text inside paymentStatusPanel
//     txHashText         — TMP_Text inside paymentStatusPanel
//     doneButton         — Button   inside paymentStatusPanel
//     loadingOverlay     — GameObject (spinner)

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WalletUpgradePanel : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────────────────────────────

    [Header("Header")]
    [SerializeField] private TMP_Text ttkBalanceText;
    [SerializeField] private Button   closeButton;

    [Header("Items Container")]
    [SerializeField] private Transform  itemsContainer;
    [SerializeField] private GameObject upgradeItemCardPrefab;

    [Header("Payment Status Panel")]
    [SerializeField] private GameObject paymentStatusPanel;
    [SerializeField] private TMP_Text   statusMessageText;
    [SerializeField] private TMP_Text   txHashText;
    [SerializeField] private Button     doneButton;
    [Tooltip("Optional 'View on Explorer' button shown after a confirmed ETH tx")]
    [SerializeField] private Button     explorerButton;
    [SerializeField] private TMP_Text   explorerButtonText;

    [Header("Loading")]
    [SerializeField] private GameObject loadingOverlay;

    // ── State ─────────────────────────────────────────────────────────────────
    private List<StoreManager.BlockchainStoreItem> _upgrades   = new();
    private string _lastExplorerUrl;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        ResetStatusPanel();
        SafeSetActive(loadingOverlay, false);

        // Buttons
        closeButton?.onClick.AddListener(ClosePanel);
        doneButton?.onClick.AddListener(OnDoneClicked);
        explorerButton?.onClick.AddListener(OnExplorerClicked);
        SafeSetActive(explorerButton?.gameObject, false);

        // WalletManager events
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.AddListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnTransactionSent += OnEthTransactionSent;
        }

        // StoreManager events
        if (StoreManager.Instance != null)
        {
            StoreManager.Instance.OnPurchaseStarted.AddListener(OnPurchaseStarted);
            StoreManager.Instance.OnPurchaseCompleted.AddListener(OnPurchaseCompleted);
            StoreManager.Instance.OnPurchaseFailed.AddListener(OnPurchaseFailed);
        }

        RefreshEthBalance();
        _ = LoadUpgradesAsync();
    }

    private void OnDisable()
    {
        closeButton?.onClick.RemoveAllListeners();
        doneButton?.onClick.RemoveAllListeners();
        explorerButton?.onClick.RemoveAllListeners();

        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.RemoveListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnTransactionSent -= OnEthTransactionSent;
        }

        if (StoreManager.Instance != null)
        {
            StoreManager.Instance.OnPurchaseStarted.RemoveListener(OnPurchaseStarted);
            StoreManager.Instance.OnPurchaseCompleted.RemoveListener(OnPurchaseCompleted);
            StoreManager.Instance.OnPurchaseFailed.RemoveListener(OnPurchaseFailed);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BALANCE DISPLAY
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshEthBalance()
    {
        if (ttkBalanceText == null) return;
        ttkBalanceText.text = WalletManager.Instance?.IsConnected == true
            ? $"Balance: {WalletManager.Instance.EthBalance} ETH"
            : "Balance: Connect Wallet";
    }

    private void OnEthBalanceUpdated(string balance)
    {
        if (ttkBalanceText != null)
            ttkBalanceText.text = $"Balance: {balance} ETH";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LOAD & SPAWN CARDS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits for StoreManager to populate BlockchainCatalog (from GET /store/items),
    /// then triggers a fresh fetch if still empty, then spawns cards.
    /// </summary>
    private async Task LoadUpgradesAsync()
    {
        SafeSetActive(loadingOverlay, true);
        ClearCards();

        // Wait up to 3s for StoreManager to finish its own fetch
        int waited = 0;
        while (StoreManager.Instance.BlockchainCatalog.Count == 0 && waited < 6)
        {
            await Task.Delay(500);
            waited++;
        }

        // If still empty, request a fresh fetch from the backend
        if (StoreManager.Instance.BlockchainCatalog.Count == 0)
        {
            Debug.Log("[WalletUpgradePanel] Catalog empty — requesting fresh fetch from /store/items");
            await StoreManager.Instance.LoadBlockchainItemsAsync();
        }

        // Filter to wallet upgrades and feature unlocks
        _upgrades = StoreManager.Instance.BlockchainCatalog
            .FindAll(i => i.itemType == "wallet_upgrade" ||
                          i.itemType == "feature_unlock"  ||
                          i.itemType == "ai_replay"        ||
                          i.itemType == "custom_skin");

        if (_upgrades.Count == 0)
            Debug.LogWarning("[WalletUpgradePanel] No upgrades found in catalog.");

        foreach (var item in _upgrades)
            SpawnCard(item);

        SafeSetActive(loadingOverlay, false);
    }

    private void SpawnCard(StoreManager.BlockchainStoreItem item)
    {
        if (upgradeItemCardPrefab == null)
        {
            Debug.LogError("[WalletUpgradePanel] upgradeItemCardPrefab is not assigned!");
            return;
        }

        var go   = Instantiate(upgradeItemCardPrefab, itemsContainer);
        var card = go.GetComponent<UpgradeItemCard>();
        if (card == null)
        {
            Debug.LogError("[WalletUpgradePanel] UpgradeItemCard component missing from prefab!");
            return;
        }

        bool isOwned = StoreManager.Instance.PlayerOwnsBlockchainItem(item.itemId);
        card.Setup(item, isOwned, OnBuyFiatClicked, OnBuyEthClicked);
    }

    private void ClearCards()
    {
        if (itemsContainer == null) return;
        foreach (Transform child in itemsContainer)
            Destroy(child.gameObject);
    }

    private void RefreshCards()
    {
        ClearCards();
        foreach (var item in _upgrades)
            SpawnCard(item);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PURCHASE HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fiat purchase: POST /purchase/create-intent → PayMongo checkout.
    /// StoreManager handles the full flow and fires events below.
    /// </summary>
    private void OnBuyFiatClicked(string itemId)
    {
        Debug.Log($"[WalletUpgradePanel] Fiat buy: {itemId}");
        StoreManager.Instance.PurchaseBlockchainItemWithFiat(itemId);
    }

    /// <summary>
    /// ETH purchase: Redirects to the React Native web app via StoreManager
    /// </summary>
    private void OnBuyEthClicked(string itemId)
    {
        if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
        {
            ShowStatus("Connect your wallet first.", null, false);
            return;
        }

        var item = StoreManager.Instance.BlockchainCatalog.Find(i => i.itemId == itemId);
        if (item == null)
        {
            ShowStatus("Item not found in catalog.", null, false);
            return;
        }

        // Delegate to the StoreManager which handles the new Web App flow
        StoreManager.Instance.PurchaseBlockchainItemViaWebApp(item.itemId, item.numericId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  STORE MANAGER EVENT HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    private void OnPurchaseStarted(string itemId)
    {
        ResetStatusPanel();
        SafeSetActive(paymentStatusPanel, true);
        if (statusMessageText != null)
        {
            statusMessageText.text  = "Opening PayMongo checkout...";
            statusMessageText.color = Color.yellow;
        }
        doneButton?.gameObject.SetActive(false);
    }

    private void OnPurchaseCompleted(PurchaseResult result)
    {
        SafeSetActive(paymentStatusPanel, true);

        if (statusMessageText != null)
        {
            statusMessageText.text  = "✓ Payment Successful!";
            statusMessageText.color = Color.green;
        }

        if (txHashText != null)
        {
            bool hasTx = !string.IsNullOrEmpty(result.txHash);
            txHashText.gameObject.SetActive(hasTx);
            if (hasTx)
                txHashText.text = $"Tx: {TruncateHash(result.txHash)}";
        }

        // Show explorer button if we have a URL
        _lastExplorerUrl = result.explorerUrl;
        SafeSetActive(explorerButton?.gameObject,
            !string.IsNullOrEmpty(result.explorerUrl));

        doneButton?.gameObject.SetActive(true);

        // Update wallet + cards
        WalletManager.Instance?.RefreshBalance();
        RefreshEthBalance();

        if (!string.IsNullOrEmpty(result.itemId) &&
            !StoreManager.Instance.OwnedBlockchainIds.Contains(result.itemId))
            StoreManager.Instance.OwnedBlockchainIds.Add(result.itemId);

        RefreshCards();

        Debug.Log($"[WalletUpgradePanel] Purchase complete: {result.itemId}  tx:{result.txHash}");
    }

    private void OnPurchaseFailed(string error)
    {
        SafeSetActive(paymentStatusPanel, true);

        if (statusMessageText != null)
        {
            statusMessageText.text  = $"✗ Payment Failed\n{error}";
            statusMessageText.color = Color.red;
        }

        if (txHashText != null) txHashText.gameObject.SetActive(false);

        SafeSetActive(explorerButton?.gameObject, false);
        doneButton?.gameObject.SetActive(true);

        Debug.LogWarning($"[WalletUpgradePanel] Purchase failed: {error}");
    }

    private void OnEthTransactionSent(string txHash)
    {
        ShowStatus("Transaction confirmed!\nWallet upgrade applied.", txHash, true);
        WalletManager.Instance?.RefreshBalance();
        RefreshCards();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowStatus(string message, string txHash, bool success)
    {
        SafeSetActive(loadingOverlay, false);
        SafeSetActive(paymentStatusPanel, true);

        if (statusMessageText != null)
        {
            statusMessageText.text  = message;
            statusMessageText.color = success ? Color.green : Color.red;
        }

        bool hasTx = !string.IsNullOrEmpty(txHash);
        if (txHashText != null)
        {
            txHashText.gameObject.SetActive(hasTx);
            if (hasTx) txHashText.text = $"TX: {TruncateHash(txHash)}";
        }

        SafeSetActive(explorerButton?.gameObject, false);
        doneButton?.gameObject.SetActive(true);
    }

    private void ResetStatusPanel()
    {
        SafeSetActive(paymentStatusPanel, false);
        if (txHashText != null) txHashText.gameObject.SetActive(false);
        SafeSetActive(explorerButton?.gameObject, false);
        doneButton?.gameObject.SetActive(false);
        _lastExplorerUrl = null;
    }

    private void OnDoneClicked()    => ResetStatusPanel();
    private void ClosePanel()       => gameObject.SetActive(false);

    private void OnExplorerClicked()
    {
        if (!string.IsNullOrEmpty(_lastExplorerUrl))
            Application.OpenURL(_lastExplorerUrl);
    }

    private static string TruncateHash(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return "";
        return hash.Length > 12 ? $"{hash.Substring(0, 10)}..." : hash;
    }

    private static void SafeSetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}