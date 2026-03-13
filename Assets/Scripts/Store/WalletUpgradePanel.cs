// Assets/Scripts/Store/WalletUpgradePanel.cs
// Native ETH only (Sepolia Testnet)
//
// FIXES:
//   CS0229 (PurchaseResult ambiguity): PurchaseResult is now defined ONLY in SharedModels.cs.
//   CS1501 (Setup overload): passes 4 args to UpgradeItemCard.Setup().

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WalletUpgradePanel : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TMP_Text ttkBalanceText;   // rename label to "ETH Balance" in Inspector
    [SerializeField] private Button   closeButton;

    [Header("Items Container")]
    [SerializeField] private Transform  itemsContainer;
    [SerializeField] private GameObject upgradeItemCardPrefab;

    [Header("Payment Status Panel")]
    [SerializeField] private GameObject paymentStatusPanel;
    [SerializeField] private TMP_Text   statusMessageText;
    [SerializeField] private TMP_Text   txHashText;
    [SerializeField] private Button     doneButton;

    [Header("Loading")]
    [SerializeField] private GameObject loadingOverlay;

    private List<StoreManager.BlockchainStoreItem> _upgrades = new();

    // ─────────────────────────────────────────────────────────────────────────
    void OnEnable()
    {
        paymentStatusPanel.SetActive(false);
        loadingOverlay.SetActive(false);

        closeButton.onClick.AddListener(ClosePanel);
        doneButton.onClick.AddListener(OnDoneClicked);

        RefreshEthBalance();
        LoadUpgrades();

        StoreManager.Instance.OnPurchaseCompleted.AddListener(OnPurchaseCompleted);
        StoreManager.Instance.OnPurchaseFailed.AddListener(OnPurchaseFailed);
        StoreManager.Instance.OnPurchaseStarted.AddListener(OnPurchaseStarted);

        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.AddListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnTransactionSent += OnPurchaseTransactionSent;
        }
    }

    void OnDisable()
    {
        closeButton.onClick.RemoveAllListeners();
        doneButton.onClick.RemoveAllListeners();

        if (StoreManager.Instance != null)
        {
            StoreManager.Instance.OnPurchaseCompleted.RemoveListener(OnPurchaseCompleted);
            StoreManager.Instance.OnPurchaseFailed.RemoveListener(OnPurchaseFailed);
            StoreManager.Instance.OnPurchaseStarted.RemoveListener(OnPurchaseStarted);
        }

        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.RemoveListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnTransactionSent -= OnPurchaseTransactionSent;
        }
    }

    // ── Balance ───────────────────────────────────────────────────────────────

    void RefreshEthBalance()
    {
        ttkBalanceText.text = WalletManager.Instance != null
            ? $"Balance: {WalletManager.Instance.EthBalance} ETH"
            : "Balance: Connect Wallet";
    }

    void OnEthBalanceUpdated(string balance)
    {
        ttkBalanceText.text = $"Balance: {balance} ETH";
    }

    // ── Load items ────────────────────────────────────────────────────────────

    async void LoadUpgrades()
    {
        loadingOverlay.SetActive(true);
        ClearCards();

        if (StoreManager.Instance.BlockchainCatalog.Count == 0)
            await Task.Delay(1000);

        _upgrades = StoreManager.Instance.BlockchainCatalog
            .FindAll(i => i.itemType == "wallet_upgrade" || i.itemType == "feature_unlock");

        foreach (var item in _upgrades)
            SpawnUpgradeCard(item);

        loadingOverlay.SetActive(false);
    }

    void SpawnUpgradeCard(StoreManager.BlockchainStoreItem item)
    {
        var card   = Instantiate(upgradeItemCardPrefab, itemsContainer);
        var cardUI = card.GetComponent<UpgradeItemCard>();
        if (cardUI == null) return;

        bool isOwned = StoreManager.Instance.PlayerOwnsBlockchainItem(item.itemId);

        // FIX CS1501: UpgradeItemCard.Setup() now takes 4 params (item, isOwned, onFiat, onETH)
        cardUI.Setup(item, isOwned, OnBuyWithFiatClicked, OnBuyWithETHClicked);
    }

    void ClearCards()
    {
        foreach (Transform child in itemsContainer)
            Destroy(child.gameObject);
    }

    // ── Purchase: Fiat (GCash/Card via PayMongo) ──────────────────────────────

    void OnBuyWithFiatClicked(string itemId)
    {
        StoreManager.Instance.PurchaseBlockchainItemWithFiat(itemId);
    }

    // ── Purchase: ETH (MetaMask Mobile deep link) ─────────────────────────────

    async void OnBuyWithETHClicked(string itemId)
    {
        if (!WalletManager.Instance.IsConnected)
        {
            ShowStatus("Connect your wallet first", "", false);
            return;
        }

        var catalogItem = StoreManager.Instance.BlockchainCatalog.Find(i => i.itemId == itemId);
        if (catalogItem == null) { ShowStatus("Item not found", "", false); return; }

        loadingOverlay.SetActive(true);
        statusMessageText.text = $"Opening MetaMask for {catalogItem.name}...";

        bool opened = await WalletManager.Instance.PurchaseStoreItem(
            catalogItem.numericId,
            onTxSent: (txHash) => ShowStatus(
                $"{catalogItem.name} purchased!\n" +
                $"{catalogItem.priceETHFormatted} ETH sent to treasury.",
                txHash, true)
        );

        loadingOverlay.SetActive(false);
        if (!opened) ShowStatus("Could not open MetaMask Mobile.", "", false);
    }

    // ── StoreManager event handlers ───────────────────────────────────────────

    // FIX CS0229: PurchaseResult now has a single definition in SharedModels.cs only.
    void OnPurchaseStarted(string itemId)
    {
        paymentStatusPanel.SetActive(true);
        statusMessageText.text  = "Opening PayMongo checkout...";
        statusMessageText.color = Color.yellow;
        txHashText.text         = "";
        doneButton.gameObject.SetActive(false);
    }

    void OnPurchaseCompleted(PurchaseResult result)
    {
        paymentStatusPanel.SetActive(true);
        statusMessageText.text  = "Payment Successful!";
        statusMessageText.color = Color.green;
        txHashText.text = string.IsNullOrEmpty(result.txHash)
            ? ""
            : $"Tx: {result.txHash.Substring(0, 10)}...";
        doneButton.gameObject.SetActive(true);

        WalletManager.Instance?.RefreshBalance();
        RefreshEthBalance();
        ClearCards();
        foreach (var item in _upgrades) SpawnUpgradeCard(item);
    }

    void OnPurchaseFailed(string error)
    {
        paymentStatusPanel.SetActive(true);
        statusMessageText.text  = $"Payment Failed\n{error}";
        statusMessageText.color = Color.red;
        txHashText.text         = "";
        doneButton.gameObject.SetActive(true);
    }

    void OnPurchaseTransactionSent(string txHash)
    {
        ShowStatus("Transaction confirmed!\nWallet upgrade applied.", txHash, true);
        WalletManager.Instance?.RefreshBalance();
        ClearCards();
        foreach (var item in _upgrades) SpawnUpgradeCard(item);
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void ShowStatus(string message, string txHash, bool success)
    {
        loadingOverlay.SetActive(false);
        paymentStatusPanel.SetActive(true);
        statusMessageText.text  = message;
        statusMessageText.color = success ? Color.green : Color.red;

        bool hasTx = !string.IsNullOrEmpty(txHash);
        txHashText.gameObject.SetActive(hasTx);
        if (hasTx) txHashText.text = $"TX: {txHash.Substring(0, 10)}...";

        doneButton.gameObject.SetActive(true);
    }

    void OnDoneClicked()  => paymentStatusPanel.SetActive(false);
    void ClosePanel()     => gameObject.SetActive(false);
}