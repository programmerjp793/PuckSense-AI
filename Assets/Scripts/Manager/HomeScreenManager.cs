// Assets/Scripts/Manager/HomeScreenManager.cs
// Native ETH only (Sepolia Testnet)
//
// Changes from existing:
//   • Comment "coins and TTK" → "coins and ETH balance" (Start sync comment)
//   • Comment "Refresh TTK balance" → "Refresh ETH balance" (OpenWalletUpgrades comment)
//   • Comment "Refresh balances...coins + TTK" → "coins + ETH" (ShowStore comment)
//   • All logic unchanged — RefreshBalance() already works for ETH

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HomeScreenManager : MonoBehaviour
{
    [Header("Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject storePanel;
    [SerializeField] private GameObject matchmakingPanel;
    [SerializeField] private GameObject howToPlayPanel;
    [SerializeField] private GameObject highScorePanel;      // ← High Score panel (child of matchmaking)

    [Header("Wallet Upgrade")]
    [SerializeField] private GameObject walletUpgradePanel;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button storeButton;
    [SerializeField] private Button howToPlayButton;
    [SerializeField] private Button quitButton;

    [Header("Settings Buttons")]
    [SerializeField] private Button settingsBackButton;

    [Header("Store Buttons")]
    [SerializeField] private Button storeBackButton;
    [SerializeField] private Button walletUpgradesButton;

    [Header("How to Play Buttons")]
    [SerializeField] private Button howToPlayBackButton;

    [Header("Matchmaking Buttons")]
    [SerializeField] private Button matchmakingBackButton;
    [SerializeField] private Button oneVsOneButton;

    [Header("Scene Names")]
    [SerializeField] private string gameplaySceneName = "GameScene";

    void Start()
    {
        ShowMainMenu();

        // FIX comment: was "coins and TTK" → "coins and ETH balance"
        // Ensures coins and ETH balance are up to date after returning from GameScene
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.SyncCoinsFromPrefs();
            WalletManager.Instance.RefreshBalance();
            Debug.Log("[HomeScreen] Session synced on load.");
        }

        if (playButton != null)       playButton.onClick.AddListener(ShowMatchmaking);
        if (settingsButton != null)   settingsButton.onClick.AddListener(ShowSettings);
        if (storeButton != null)      storeButton.onClick.AddListener(ShowStore);
        if (howToPlayButton != null)  howToPlayButton.onClick.AddListener(ShowHowToPlay);
        if (quitButton != null)       quitButton.onClick.AddListener(QuitGame);

        if (settingsBackButton != null)    settingsBackButton.onClick.AddListener(ShowMainMenu);
        if (storeBackButton != null)       storeBackButton.onClick.AddListener(ShowMainMenu);
        if (howToPlayBackButton != null)   howToPlayBackButton.onClick.AddListener(ShowMainMenu);
        if (matchmakingBackButton != null) matchmakingBackButton.onClick.AddListener(ShowMainMenu);
        if (walletUpgradesButton != null)  walletUpgradesButton.onClick.AddListener(OpenWalletUpgrades);
        if (oneVsOneButton != null)        oneVsOneButton.onClick.AddListener(StartOneVsOne);

        Debug.Log("[HomeScreen] HomeScreenManager initialized.");
    }

    // ─── Panel Navigation ─────────────────────────────────────────────────────

    void ShowMainMenu()
    {
        HideAllPanels();
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    void ShowSettings()
    {
        HideAllPanels();
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    void ShowStore()
    {
        HideAllPanels();
        if (storePanel != null) storePanel.SetActive(true);

        // FIX comment: was "coins + TTK" → "coins + ETH balance"
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.SyncCoinsFromPrefs();
            WalletManager.Instance.RefreshBalance();
            Debug.Log("[HomeScreen] Balances refreshed on store open.");
        }
    }

    void ShowHowToPlay()
    {
        HideAllPanels();
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(true);
            TutorialManager tutorialManager = howToPlayPanel.GetComponent<TutorialManager>();
            if (tutorialManager != null) tutorialManager.Initialize();
        }
    }

    void ShowMatchmaking()
    {
        HideAllPanels();
        if (matchmakingPanel != null) matchmakingPanel.SetActive(true);
    }

    public void OpenWalletUpgrades()
    {
        if (walletUpgradePanel != null) walletUpgradePanel.SetActive(true);

        // FIX comment: was "Refresh TTK balance" → "Refresh ETH balance"
        if (WalletManager.Instance != null)
            WalletManager.Instance.RefreshBalance();

        Debug.Log("[HomeScreen] Wallet Upgrades opened.");
    }

    void HideAllPanels()
    {
        if (mainMenuPanel != null)    mainMenuPanel.SetActive(false);
        if (settingsPanel != null)    settingsPanel.SetActive(false);
        if (storePanel != null)       storePanel.SetActive(false);
        if (matchmakingPanel != null) matchmakingPanel.SetActive(false);
        if (howToPlayPanel != null)   howToPlayPanel.SetActive(false);
        if (highScorePanel != null)   highScorePanel.SetActive(false);
        // walletUpgradePanel has its own close button — not hidden here
    }

    // ─── Game Start ───────────────────────────────────────────────────────────

    void StartOneVsOne()
    {
        Debug.Log("[HomeScreen] Starting 1v1 match...");
        PlayerPrefs.SetString("GameMode", "1v1");
        PlayerPrefs.Save();

        if (!string.IsNullOrEmpty(gameplaySceneName))
            SceneManager.LoadScene(gameplaySceneName);
        else
            Debug.LogError("[HomeScreen] Gameplay scene name not set!");
    }

    void QuitGame()
    {
        if (WalletManager.Instance != null)
            WalletManager.Instance.SaveSession();

        Debug.Log("[HomeScreen] Quitting game...");
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}