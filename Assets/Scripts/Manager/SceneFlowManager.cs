// Assets/Scripts/Manager/SceneFlowManager.cs
// Native ETH only (Sepolia Testnet)
//
// Changes from existing:
//   • SaveSessionBeforeTransition() comment: "coins + TTK" → "coins + ETH balance"
//   • All logic unchanged — SyncCoinsFromPrefs() and SaveSession() are token-agnostic

using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneFlowManager : MonoBehaviour
{
    public static SceneFlowManager Instance { get; private set; }

    public const string MODE_ONE_VS_ONE   = "OneVsOne";
    public const string MODE_AI_VS_PLAYER = "AIvsPlayer";
    public const string MODE_TUTORIAL     = "Tutorial";

    [Header("Scene Names")]
    public string homeScreenScene = "HomeScreen";
    public string gameplayScene   = "GameScene";
    public string loadingScene    = "LoadingScreen";

    private string currentGameMode = "";
    private bool   isTutorialMode  = false;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else                  Destroy(gameObject);
    }

    // ─── Scene Loading ────────────────────────────────────────────────────────

    public void LoadHomeScreen()
    {
        SaveSessionBeforeTransition();
        isTutorialMode  = false;
        currentGameMode = "";
        SceneManager.LoadScene(homeScreenScene);
    }

    public void LoadGameplay(string gameMode)
    {
        SaveSessionBeforeTransition();
        currentGameMode = gameMode;
        isTutorialMode  = false;
        PersistGameMode(gameMode);
        SceneManager.LoadScene(gameplayScene);
    }

    public void LoadGameplayWithLoading(string gameMode)
    {
        SaveSessionBeforeTransition();
        currentGameMode = gameMode;
        isTutorialMode  = false;
        PersistGameMode(gameMode);
        SceneManager.LoadScene(loadingScene);
    }

    public void LoadTutorial()
    {
        SaveSessionBeforeTransition();
        isTutorialMode  = true;
        currentGameMode = MODE_TUTORIAL;
        PersistGameMode(MODE_TUTORIAL);
        SceneManager.LoadScene(gameplayScene);
    }

    // ─── Mode Shortcuts ───────────────────────────────────────────────────────

    public void LoadOneVsOne()              => LoadGameplay(MODE_ONE_VS_ONE);
    public void LoadAIvsPlayer()            => LoadGameplay(MODE_AI_VS_PLAYER);
    public void LoadAIvsPlayerWithLoading() => LoadGameplayWithLoading(MODE_AI_VS_PLAYER);

    // ─── Navigation ───────────────────────────────────────────────────────────

    public void ReturnToHomeScreen()
    {
        SaveSessionBeforeTransition();
        currentGameMode = "";
        isTutorialMode  = false;
        SceneManager.LoadScene(homeScreenScene);
    }

    // ─── State Queries ────────────────────────────────────────────────────────

    public string GetCurrentGameMode()
    {
        if (string.IsNullOrEmpty(currentGameMode))
            currentGameMode = PlayerPrefs.GetString("GameMode", "");
        return currentGameMode;
    }

    public bool IsOneVsOneMode()   => GetCurrentGameMode() == MODE_ONE_VS_ONE;
    public bool IsAIvsPlayerMode() => GetCurrentGameMode() == MODE_AI_VS_PLAYER;
    public bool IsTutorialMode()   => isTutorialMode;

    public void SetTutorialMode(bool active) => isTutorialMode = active;

    // ─── Utility ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves wallet session (coins + ETH balance) before any scene transition
    /// so balances are never lost between scenes.
    /// FIX comment: was "coins + TTK" → "coins + ETH balance"
    /// </summary>
    private void SaveSessionBeforeTransition()
    {
        if (WalletManager.Instance != null)
        {
            WalletManager.Instance.SyncCoinsFromPrefs();
            WalletManager.Instance.SaveSession();
            Debug.Log("[SceneFlow] Session saved before scene transition.");
        }
    }

    private void PersistGameMode(string mode)
    {
        PlayerPrefs.SetString("GameMode", mode);
        PlayerPrefs.Save();
    }

    public void QuitGame()
    {
        SaveSessionBeforeTransition();
        Debug.Log("[SceneFlow] Quitting game...");
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}