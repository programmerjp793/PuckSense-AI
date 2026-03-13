// Assets/Scripts/Blockchain/WalletConnectUI.cs
// Native ETH only (Sepolia Testnet)
//
// Changes from existing (pasted code):
//   • ttkBalanceText     → ethBalanceText (rename Inspector field label to "ETH Balance")
//   • OnLoginSuccess()   → shows ETH balance instead of TTK
//   • OnOpenMetaMask()   → calls WalletManager.ConnectMetaMask() (proper deep link)
//   • Added:  networkText, wrongNetworkPanel, switchNetworkButton, loadingSpinner
//             OnSwitchNetwork(), ShowManualFallback(), BalanceRefreshLoop()
//   • Kept:   walletAddressInput, connectWalletButton, guestButton,
//             WalletAuthManager login flow, GoToHomeScreen(), exact same Start() shape

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class WalletConnectUI : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private TMP_InputField walletAddressInput;
    [SerializeField] private Button         connectWalletButton;
    [SerializeField] private Button         guestButton;
    [SerializeField] private Button         openMetaMaskButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text ethBalanceText;  // was ttkBalanceText — rename label in Inspector
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private TMP_Text networkText;     // NEW — assign a TMP_Text, shows "Sepolia Testnet"

    [Header("Network Warning")]                        // NEW — optional panel if wrong network
    [SerializeField] private GameObject wrongNetworkPanel;
    [SerializeField] private Button     switchNetworkButton;

    [Header("Loading")]                                // NEW — optional spinner
    [SerializeField] private GameObject loadingSpinner;

    private Coroutine _refreshLoop;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        connectWalletButton.onClick.AddListener(OnConnectClicked);
        guestButton.onClick.AddListener(OnGuestClicked);
        openMetaMaskButton.onClick.AddListener(OnOpenMetaMask);
        switchNetworkButton?.onClick.AddListener(OnSwitchNetwork);

        WalletAuthManager.Instance.OnLoginSuccess.AddListener(_ => OnLoginSuccess());
        WalletAuthManager.Instance.OnLoginFailed.AddListener(OnLoginFailed);

        // Hook WalletManager events
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.AddListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnError += OnWalletError;
        }

        // Network label
        if (networkText != null) networkText.text = "Sepolia Testnet";

        // Restore saved wallet into input field (key kept same as original "Wallet_Address")
        string saved = PlayerPrefs.GetString("Wallet_Address", "");
        if (!string.IsNullOrEmpty(saved))
        {
            walletAddressInput.text = saved;
            // Show cached ETH balance while re-connecting
            string cached = PlayerPrefs.GetString($"EthBalance_{saved.ToLower()}", "0");
            SetEthBalanceText(cached);
        }
    }

    void OnDestroy()
    {
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.OnBalanceUpdated.RemoveListener(OnEthBalanceUpdated);
            WalletManager.Instance.OnError -= OnWalletError;
        }
        if (_refreshLoop != null) StopCoroutine(_refreshLoop);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    void OnConnectClicked()
    {
        string address = walletAddressInput.text.Trim();

        if (!address.StartsWith("0x") || address.Length != 42)
        {
            statusText.text = "Invalid wallet address";
            return;
        }

        statusText.text = "Connecting...";
        connectWalletButton.interactable = false;
        loadingSpinner?.SetActive(true);
        WalletAuthManager.Instance.LoginWithWallet(address);
    }

    void OnGuestClicked()
    {
        WalletAuthManager.Instance.LoginAsGuest();
    }

    /// <summary>
    /// Opens MetaMask Mobile via deep link (was Application.OpenURL("metamask://")).
    /// Shows manual address fallback after 3s if deep link doesn't return.
    /// </summary>
    void OnOpenMetaMask()
    {
        WalletManager.Instance?.ConnectMetaMask();
        statusText.text = "Opening MetaMask...";
        StartCoroutine(ShowManualFallback(3f));
    }

    void OnSwitchNetwork()
    {
        WalletManager.Instance?.SwitchToSepolia();
        statusText.text = "Switching to Sepolia...";
        wrongNetworkPanel?.SetActive(false);
    }

    // ── Auth callbacks ────────────────────────────────────────────────────────

    void OnLoginSuccess()
    {
        loadingSpinner?.SetActive(false);
        var player = WalletAuthManager.Instance.CurrentPlayer;

        if (usernameText != null)
            usernameText.text = player?.username ?? "Guest";

        // Refresh ETH balance if wallet connected (was TTK)
        if (WalletManager.Instance != null &&
            !string.IsNullOrEmpty(player?.walletAddress))
        {
            WalletManager.Instance.SetWalletAddress(player.walletAddress);
            WalletManager.Instance.RefreshBalance();

            // Start periodic balance refresh every 15s
            if (_refreshLoop != null) StopCoroutine(_refreshLoop);
            _refreshLoop = StartCoroutine(BalanceRefreshLoop());
        }

        Invoke(nameof(GoToHomeScreen), 1f);
    }

    void OnLoginFailed(string error)
    {
        loadingSpinner?.SetActive(false);
        statusText.text = $"Failed: {error}";
        connectWalletButton.interactable = true;
    }

    void OnWalletError(string error)
    {
        loadingSpinner?.SetActive(false);
        statusText.text = $"⚠ {error}";
        connectWalletButton.interactable = true;
    }

    // ── Balance display ───────────────────────────────────────────────────────

    void OnEthBalanceUpdated(string balance)
    {
        SetEthBalanceText(balance);
    }

    void SetEthBalanceText(string balance)
    {
        // was: ttkBalanceText.text = $"{bal} TTK"
        if (ethBalanceText != null)
            ethBalanceText.text = $"{balance} ETH";
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator ShowManualFallback(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (WalletManager.Instance != null && !WalletManager.Instance.IsConnected)
        {
            loadingSpinner?.SetActive(false);
            statusText.text = "MetaMask didn't open? Enter address manually and tap Connect.";
        }
    }

    private IEnumerator BalanceRefreshLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(15f);
            if (WalletManager.Instance != null && WalletManager.Instance.IsConnected)
                WalletManager.Instance.RefreshBalance();
        }
    }

    void GoToHomeScreen() => SceneManager.LoadScene("HomeScreen");
}