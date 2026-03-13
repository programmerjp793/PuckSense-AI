// Assets/Scripts/Store/UpgradeItemCard.cs
// Native ETH only (Sepolia Testnet)
//
// FIXES:
//   CS1061: priceInTTKFormatted -> priceETHFormatted
//   CS1501: Setup() now takes 4 args: item, isOwned, onBuyWithFiat, onBuyWithETH
//           (WalletUpgradePanel calls it with 4 params)

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UpgradeItemCard : MonoBehaviour
{
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text itemTypeText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private TMP_Text statusText;

    [Header("Buy Buttons")]
    [SerializeField] private Button   buyFiatButton;    // GCash / Card via PayMongo
    [SerializeField] private TMP_Text buyFiatButtonText;
    [SerializeField] private Button   buyEthButton;     // ETH via MetaMask Mobile
    [SerializeField] private TMP_Text buyEthButtonText;

    // FIX CS1501: signature updated from 3 params to 4 params.
    // WalletUpgradePanel.SpawnUpgradeCard() calls: Setup(item, isOwned, onFiat, onETH)
    public void Setup(
        StoreManager.BlockchainStoreItem item,
        bool isOwned,
        Action<string> onBuyWithFiat,
        Action<string> onBuyWithETH)
    {
        itemNameText.text = item.name;
        itemTypeText.text = item.itemType == "wallet_upgrade" ? "Wallet Upgrade" : "Feature Unlock";

        // FIX CS1061: was item.priceInTTKFormatted -- field renamed to priceETHFormatted
        priceText.text = $"{item.priceETHFormatted} ETH  |  PHP {item.pricePHP:F2}";

        if (isOwned)
        {
            statusText.text = "OWNED";
            statusText.color = Color.green;

            buyFiatButton.interactable = false;
            buyEthButton.interactable  = false;
            if (buyFiatButtonText) buyFiatButtonText.text = "Owned";
            if (buyEthButtonText)  buyEthButtonText.text  = "Owned";
        }
        else
        {
            statusText.text  = "AVAILABLE";
            statusText.color = Color.white;

            // Fiat button: GCash / Card
            buyFiatButton.interactable = true;
            if (buyFiatButtonText) buyFiatButtonText.text = $"Pay PHP {item.pricePHP:F2} (GCash/Card)";
            buyFiatButton.onClick.RemoveAllListeners();
            buyFiatButton.onClick.AddListener(() => onBuyWithFiat(item.itemId));

            // ETH button: MetaMask Mobile deep link
            buyEthButton.interactable = true;
            if (buyEthButtonText) buyEthButtonText.text = $"Pay {item.priceETHFormatted} ETH";
            buyEthButton.onClick.RemoveAllListeners();
            buyEthButton.onClick.AddListener(() => onBuyWithETH(item.itemId));
        }
    }
}