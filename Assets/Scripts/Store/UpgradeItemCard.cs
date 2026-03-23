// Assets/Scripts/Store/UpgradeItemCard.cs
// Native ETH only (Sepolia Testnet)
//
// PREFAB WIRING (matches UpgradeItemCard prefab seen in Unity screenshot):
//
//   UpgradeItemCard (root)
//   └── Panel
//       ├── ItemNameText      (TMP_Text)   ← itemNameText
//       ├── ItemTypeText      (TMP_Text)   ← itemTypeText
//       ├── PriceText         (TMP_Text)   ← priceText
//       ├── StatusText        (TMP_Text)   ← statusText
//       └── ButtonRow
//           ├── BuyFiatButton (Button)     ← buyFiatButton
//           │   └── Text (TMP)            ← buyFiatButtonText   [child of BuyFiatButton]
//           └── BuyEthButton  (Button)    ← buyEthButton
//               └── Text (TMP)            ← buyEthButtonText    [child of BuyEthButton]
//
// HOW TO WIRE IN INSPECTOR:
//   1. Select the UpgradeItemCard prefab in Assets/Prefabs.
//   2. Find the UpgradeItemCard component in the Inspector.
//   3. Drag each child object to the matching serialized field (see labels above).
//
// Setup() is called by WalletUpgradePanel.SpawnUpgradeCard() with 4 params:
//   (BlockchainStoreItem, isOwned, onBuyWithFiat, onBuyWithETH)

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UpgradeItemCard : MonoBehaviour
{
    // ── Serialized fields — drag children here in the prefab Inspector ────────

    [Header("Info Labels")]
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text itemTypeText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private TMP_Text statusText;

    [Header("Buy Buttons  (ButtonRow children)")]
    [Tooltip("BuyFiatButton — GCash / Card via PayMongo")]
    [SerializeField] private Button   buyFiatButton;
    [Tooltip("Text child of BuyFiatButton")]
    [SerializeField] private TMP_Text buyFiatButtonText;

    [Tooltip("BuyEthButton — ETH via MetaMask Mobile deep link")]
    [SerializeField] private Button   buyEthButton;
    [Tooltip("Text child of BuyEthButton")]
    [SerializeField] private TMP_Text buyEthButtonText;

    // ── Optional icon ─────────────────────────────────────────────────────────
    [Header("Optional")]
    [Tooltip("Optional icon image on the card — leave null if not used")]
    [SerializeField] private Image itemIcon;

    // ─────────────────────────────────────────────────────────────────────────
    //  Public API — called by WalletUpgradePanel.SpawnUpgradeCard()
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the card UI for one blockchain store item.
    /// </summary>
    /// <param name="item">Item data fetched from /store/items (blockchainService)</param>
    /// <param name="isOwned">Whether the connected wallet already owns this item</param>
    /// <param name="onBuyWithFiat">Callback invoked when "Buy with GCash/Card" is tapped</param>
    /// <param name="onBuyWithETH">Callback invoked when "Pay with ETH" is tapped</param>
    public void Setup(
        StoreManager.BlockchainStoreItem item,
        bool                             isOwned,
        Action<string>                   onBuyWithFiat,
        Action<string>                   onBuyWithETH)
    {
        // ── Null-guard ────────────────────────────────────────────────────────
        if (item == null)
        {
            Debug.LogError("[UpgradeItemCard] Setup called with null item.");
            return;
        }

        // ── Info labels ───────────────────────────────────────────────────────
        if (itemNameText != null) itemNameText.text = item.name;

        if (itemTypeText != null)
            itemTypeText.text = item.itemType == "wallet_upgrade" ? "Wallet Upgrade" : "Feature Unlock";

        if (priceText != null)
            priceText.text = $"{item.priceETHFormatted} ETH  |  ₱{item.pricePHP:F2}";

        // ── Status + buttons ──────────────────────────────────────────────────
        if (isOwned)
        {
            SetOwnedState();
        }
        else
        {
            SetAvailableState(item, onBuyWithFiat, onBuyWithETH);
        }
    }

    // ── Private state helpers ─────────────────────────────────────────────────

    private void SetOwnedState()
    {
        if (statusText != null)
        {
            statusText.text  = "✓ OWNED";
            statusText.color = new Color(0.3f, 0.9f, 0.3f);   // green
        }

        SetButtonState(buyFiatButton, buyFiatButtonText, false, "Owned");
        SetButtonState(buyEthButton,  buyEthButtonText,  false, "Owned");
    }

    private void SetAvailableState(
        StoreManager.BlockchainStoreItem item,
        Action<string> onBuyWithFiat,
        Action<string> onBuyWithETH)
    {
        if (statusText != null)
        {
            statusText.text  = "AVAILABLE";
            statusText.color = Color.white;
        }

        // Fiat button
        SetButtonState(buyFiatButton, buyFiatButtonText, true,
            $"₱{item.pricePHP:F2}  GCash/Card");
        buyFiatButton.onClick.RemoveAllListeners();
        buyFiatButton.onClick.AddListener(() =>
        {
            Debug.Log($"[Card] Fiat buy tapped: {item.itemId}");
            onBuyWithFiat?.Invoke(item.itemId);
        });

        // ETH button
        SetButtonState(buyEthButton, buyEthButtonText, true,
            $"{item.priceETHFormatted} ETH");
        buyEthButton.onClick.RemoveAllListeners();
        buyEthButton.onClick.AddListener(() =>
        {
            Debug.Log($"[Card] ETH buy tapped: {item.itemId}");
            onBuyWithETH?.Invoke(item.itemId);
        });
    }

    /// <summary>Sets button interactable + label text.</summary>
    private void SetButtonState(Button btn, TMP_Text label, bool interactable, string text)
    {
        if (btn != null)   btn.interactable = interactable;
        if (label != null) label.text       = text;
    }

    // ── Runtime icon setter (called externally if icon loaded async) ──────────
    public void SetIcon(Sprite sprite)
    {
        if (itemIcon == null) return;
        itemIcon.sprite  = sprite;
        itemIcon.enabled = sprite != null;
    }

    // ── Validation helper — fires in Editor if fields are unbound ─────────────
    private void OnValidate()
    {
        if (itemNameText    == null) Debug.LogWarning($"[UpgradeItemCard] {name}: itemNameText not assigned.");
        if (itemTypeText    == null) Debug.LogWarning($"[UpgradeItemCard] {name}: itemTypeText not assigned.");
        if (priceText       == null) Debug.LogWarning($"[UpgradeItemCard] {name}: priceText not assigned.");
        if (statusText      == null) Debug.LogWarning($"[UpgradeItemCard] {name}: statusText not assigned.");
        if (buyFiatButton   == null) Debug.LogWarning($"[UpgradeItemCard] {name}: buyFiatButton not assigned.");
        if (buyEthButton    == null) Debug.LogWarning($"[UpgradeItemCard] {name}: buyEthButton not assigned.");
    }
}