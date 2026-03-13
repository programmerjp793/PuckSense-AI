// Assets/GameManager.cs
// Native ETH only (Sepolia Testnet)
//
// Changes from existing:
//   • [Header] "Blockchain Reward": enableTTKRewards → enableETHRewards
//   • _savedMatchId comment: "ClaimTTKReward" → "ClaimETHReward"
//   • EndGame(): "TTK reward initiated" → "ETH reward initiated" (log string)
//   • OnMatchValidatedForReward(): "claiming TTK reward" → "claiming ETH reward"
//   • ClaimTTKReward() renamed → ClaimETHReward()
//     - internal logs: "TTK" → "ETH"
//     - field reference: enableTTKRewards → enableETHRewards
//   • Everything else unchanged: scoring, timer, UI, scene flow, SaveMatchToBackend

using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Score Settings")]
    [SerializeField] private int scoreToWin    = 10;
    [SerializeField] private int playerScore   = 0;
    [SerializeField] private int opponentScore = 0;

    [Header("Time Settings")]
    [SerializeField] private float matchTimeLimit = 180f;
    [SerializeField] private float currentTime;
    [SerializeField] private bool  isTimeRunning  = true;

    [Header("UI References")]
    [SerializeField] private TMP_Text   playerScoreText;
    [SerializeField] private TMP_Text   opponentScoreText;
    [SerializeField] private TMP_Text   timerText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TMP_Text   winnerText;
    [SerializeField] private TMP_Text   finalScoreText;
    [SerializeField] private GameObject roundPanel;
    [SerializeField] private TMP_Text   roundText;

    [Header("Win Panel Buttons")]
    [SerializeField] private UnityEngine.UI.Button homeButton;
    [SerializeField] private UnityEngine.UI.Button restartButton;

    [Header("Quit Button")]
    [SerializeField] private UnityEngine.UI.Button quitButton;

    [Header("Quit Confirmation Panel")]
    [SerializeField] private GameObject            quitConfirmationPanel;
    [SerializeField] private TMP_Text              quitConfirmationText;
    [SerializeField] private UnityEngine.UI.Button quitConfirmButton;
    [SerializeField] private UnityEngine.UI.Button quitCancelButton;

    [Header("Game Objects")]
    [SerializeField] private GameObject puck;
    [SerializeField] private GameObject paddle1;
    [SerializeField] private GameObject paddle2;
    [SerializeField] private Transform  puckStartPosition;
    [SerializeField] private Transform  paddle1StartPosition;
    [SerializeField] private Transform  paddle2StartPosition;

    [Header("Goal Zones")]
    [SerializeField] private BoxCollider2D playerGoalZone;
    [SerializeField] private BoxCollider2D opponentGoalZone;

    [Header("Serving Settings")]
    [SerializeField] private Vector2 playerServePosition   = new Vector2(0f, -3f);
    [SerializeField] private Vector2 opponentServePosition = new Vector2(0f,  3f);
    [SerializeField] private float   serveDelay  = 2f;
    [SerializeField] private float   freezeDelay = 1f;

    // FIX: was enableTTKRewards → enableETHRewards
    [Header("Blockchain Reward")]
    [SerializeField] private bool enableETHRewards = true;

    private bool isGameActive      = true;
    private bool isRoundTransition = false;
    private bool isPaddlesFrozen   = false;
    private bool playerServes      = true;
    private bool isQuitGame        = false;

    // FIX comment: used by ClaimETHReward() (was ClaimTTKReward)
    private string _savedMatchId = null;

    private PaddleAI paddleAI;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else                  Destroy(gameObject);
    }

    void Start()
    {
        currentTime = matchTimeLimit;
        UpdateUI();

        if (winPanel != null)              winPanel.SetActive(false);
        if (roundPanel != null)            roundPanel.SetActive(false);
        if (quitConfirmationPanel != null) quitConfirmationPanel.SetActive(false);

        if (playerGoalZone != null)   playerGoalZone.isTrigger   = true;
        if (opponentGoalZone != null) opponentGoalZone.isTrigger = true;

        SetupWinPanelButtons();
        SetupQuitButton();

        playerServes = Random.value > 0.5f;

        if (SceneFlowManager.Instance != null && SceneFlowManager.Instance.IsAIvsPlayerMode())
        {
            paddleAI = FindObjectOfType<PaddleAI>();
            if (paddleAI != null)
                Debug.Log("[GameManager] PaddleAI found and cached.");
            else
                Debug.LogWarning("[GameManager] AIvsPlayer mode but PaddleAI not found!");
        }
    }

    // ─── Button Setup ─────────────────────────────────────────────────────────

    void SetupWinPanelButtons()
    {
        if (homeButton != null)    homeButton.onClick.AddListener(OnHomeButtonClicked);
        else Debug.LogWarning("[GameManager] Home button not assigned!");

        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
        else Debug.LogWarning("[GameManager] Restart button not assigned!");
    }

    void SetupQuitButton()
    {
        if (quitButton != null)        quitButton.onClick.AddListener(ShowQuitConfirmation);
        if (quitConfirmButton != null) quitConfirmButton.onClick.AddListener(ConfirmQuit);
        if (quitCancelButton != null)  quitCancelButton.onClick.AddListener(CancelQuit);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (isGameActive && isTimeRunning && !isRoundTransition)
        {
            currentTime -= Time.deltaTime;
            UpdateTimerUI();
            if (currentTime <= 0) { currentTime = 0; TimeUp(); }
        }
    }

    // ─── Scoring ──────────────────────────────────────────────────────────────

    public void PlayerScored()
    {
        if (!isGameActive || isRoundTransition) return;

        playerScore++;
        Debug.Log($"[GameManager] Player scored! {playerScore} - {opponentScore}");
        UpdateUI();

        playerServes = false;

        if (playerScore >= scoreToWin)
            EndGame("PLAYER WINS!", playerScore, opponentScore);
        else
        {
            StartNewRound("GOAL! Player Scored!");
            paddleAI?.NotifyGoalScored();
        }
    }

    public void OpponentScored()
    {
        if (!isGameActive || isRoundTransition) return;

        opponentScore++;
        Debug.Log($"[GameManager] Opponent scored! {playerScore} - {opponentScore}");
        UpdateUI();

        playerServes = true;

        if (opponentScore >= scoreToWin)
            EndGame("OPPONENT WINS!", playerScore, opponentScore);
        else
        {
            StartNewRound("GOAL! Opponent Scored!");
            paddleAI?.NotifyGoalScored();
        }
    }

    void TimeUp()
    {
        isGameActive = false;

        if      (playerScore > opponentScore) EndGame("TIME UP! PLAYER WINS!",    playerScore, opponentScore);
        else if (opponentScore > playerScore) EndGame("TIME UP! OPPONENT WINS!",  playerScore, opponentScore);
        else                                  EndGame("TIME UP! IT'S A TIE!",     playerScore, opponentScore);
    }

    // ─── Round Management ─────────────────────────────────────────────────────

    void StartNewRound(string message)
    {
        isRoundTransition = true;

        if (roundPanel != null && roundText != null)
        {
            roundPanel.SetActive(true);
            roundText.text = message;
        }

        Invoke("ResetPositions", serveDelay);
    }

    void ResetPositions()
    {
        ResetPaddles();

        if (puck != null)
        {
            Rigidbody2D puckRb = puck.GetComponent<Rigidbody2D>();
            if (puckRb != null) { puckRb.linearVelocity = Vector2.zero; puckRb.angularVelocity = 0f; }
            puck.transform.position = playerServes ? playerServePosition : opponentServePosition;
        }

        if (roundPanel != null) roundPanel.SetActive(false);
        isRoundTransition = false;
    }

    public void ResetPaddles()
    {
        if (paddle1 != null && paddle1StartPosition != null)
        {
            paddle1.transform.position = paddle1StartPosition.position;
            Rigidbody2D rb = paddle1.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }

        if (paddle2 != null && paddle2StartPosition != null)
        {
            paddle2.transform.position = paddle2StartPosition.position;
            Rigidbody2D rb = paddle2.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    // ─── End Game ─────────────────────────────────────────────────────────────

    async void EndGame(string message, int finalPlayerScore, int finalOpponentScore)
    {
        isGameActive  = false;
        isTimeRunning = false;

        if (puck != null)
        {
            Rigidbody2D puckRb = puck.GetComponent<Rigidbody2D>();
            if (puckRb != null) { puckRb.linearVelocity = Vector2.zero; puckRb.angularVelocity = 0f; }
        }

        bool playerWon = message.Contains("PLAYER WINS");
        bool isTie     = message.Contains("TIE");

        if (!isQuitGame)
        {
            if (playerWon)
            {
                await SaveMatchToBackend(finalPlayerScore, finalOpponentScore, "player");
                StoreManager.RewardCoins(10);
                WalletManager.Instance?.SyncCoinsFromPrefs();

                // Use MatchManager flow if a match was created via /match/create
                // Otherwise fall back to direct ClaimETHReward
                if (MatchManager.Instance != null && MatchManager.Instance.CurrentMatch != null)
                {
                    MatchManager.Instance.SubmitMatchResult(true);
                    MatchManager.Instance.OnMatchValidated.AddListener(OnMatchValidatedForReward);
                }
                else
                {
                    ClaimETHReward(); // FIX: was ClaimTTKReward()
                }

                // FIX: was "10 coins + TTK reward initiated"
                Debug.Log("[GameManager] Player won — 10 coins + ETH reward initiated!");
            }
            else if (isTie)
            {
                await SaveMatchToBackend(finalPlayerScore, finalOpponentScore, "tie");
                StoreManager.RewardCoins(5);
                WalletManager.Instance?.SyncCoinsFromPrefs();
                WalletManager.Instance?.SaveSession();
                Debug.Log("[GameManager] Tie — 5 coins rewarded and session saved!");
            }
            else
            {
                await SaveMatchToBackend(finalPlayerScore, finalOpponentScore, "opponent");
                WalletManager.Instance?.SaveSession();
                Debug.Log("[GameManager] Player lost — match saved, session persisted.");
            }
        }
        else
        {
            WalletManager.Instance?.SaveSession();
            Debug.Log("[GameManager] Player quit — session saved, no rewards.");
        }

        if (winPanel != null && winnerText != null)
        {
            winPanel.SetActive(true);
            winnerText.text = message;

            if (finalScoreText != null)
                finalScoreText.text =
                    $"Final Score\nPlayer: {finalPlayerScore}  -  Opponent: {finalOpponentScore}";
        }

        Debug.Log($"[GameManager] Game Over: {message} | {finalPlayerScore}-{finalOpponentScore}");
    }

    /// <summary>
    /// Called when MatchManager validates the match — then claims ETH reward.
    /// Listener removed immediately to prevent duplicate calls.
    /// </summary>
    private void OnMatchValidatedForReward(string matchId)
    {
        MatchManager.Instance.OnMatchValidated.RemoveListener(OnMatchValidatedForReward);
        // FIX: was "claiming TTK reward" → "claiming ETH reward"
        Debug.Log($"[GameManager] Match validated: {matchId} — claiming ETH reward...");
        MatchManager.Instance.ClaimReward();
    }

    // ─── Backend: Save Match ──────────────────────────────────────────────────

    private async System.Threading.Tasks.Task SaveMatchToBackend(
        int finalPlayerScore, int finalOpponentScore, string winner)
    {
        try
        {
            string matchId = $"match_{System.DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Range(1000, 9999)}";

            string body = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                matchId,
                playerScore      = finalPlayerScore,
                opponentScore    = finalOpponentScore,
                winner,
                durationSeconds  = Mathf.RoundToInt(matchTimeLimit - currentTime),
                walletAddress    = WalletManager.Instance?.WalletAddress ?? "",
                aiDifficulty     = "medium",
                validationPassed = winner == "player",
            });

            string json = await ApiClient.Instance.PostAsync("/match/save", body);
            var    resp = Newtonsoft.Json.JsonConvert.DeserializeObject<SaveMatchResponse>(json);

            if (resp?.success == true)
            {
                _savedMatchId = resp.matchId ?? matchId;
                Debug.Log($"[GameManager] Match saved. matchId: {_savedMatchId}");
            }
            else
            {
                _savedMatchId = matchId;
                Debug.LogWarning($"[GameManager] Match save failed: {resp?.message}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GameManager] SaveMatchToBackend error: {e.Message}");
        }
    }

    // ─── Backend: Claim ETH Reward (fallback) ─────────────────────────────────
    // FIX: renamed from ClaimTTKReward() → ClaimETHReward()
    // Used when no MatchManager /match/create flow was started (casual matches).

    private async void ClaimETHReward()
    {
        // FIX: was enableTTKRewards → enableETHRewards
        if (!enableETHRewards) return;

        if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
        {
            // FIX: was "TTK skipped" → "ETH skipped"
            Debug.Log("[GameManager] No wallet — ETH reward skipped, saving session.");
            WalletManager.Instance?.SaveSession();
            return;
        }

        try
        {
            string body = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                matchResult   = "win",
                playerScore,
                opponentScore,
                walletAddress = WalletManager.Instance.WalletAddress,
                matchId       = _savedMatchId,
            });

            string json = await ApiClient.Instance.PostAsync("/reward/claim", body);
            // FIX: was "TTK reward claimed" → "ETH reward claimed"
            Debug.Log($"[GameManager] ETH reward claimed: {json}");

            WalletManager.Instance.RefreshBalance();
            WalletManager.Instance.SyncCoinsFromPrefs();
            WalletManager.Instance.SaveSession();
        }
        catch (System.Exception e)
        {
            // FIX: was "TTK claim failed" → "ETH claim failed"
            Debug.LogWarning($"[GameManager] ETH claim failed: {e.Message}");
            WalletManager.Instance?.SaveSession();
        }
    }

    // ─── Quit Flow ────────────────────────────────────────────────────────────

    private void ShowQuitConfirmation()
    {
        if (quitConfirmationPanel != null)
        {
            quitConfirmationPanel.SetActive(true);
            if (quitConfirmationText != null)
                quitConfirmationText.text = "Are you sure you want to quit?\nYou will lose this match.";
            isGameActive  = false;
            isTimeRunning = false;
        }
    }

    private void ConfirmQuit()
    {
        isQuitGame = true;
        if (quitConfirmationPanel != null) quitConfirmationPanel.SetActive(false);
        EndGame("OPPONENT WINS!", playerScore, opponentScore);
    }

    private void CancelQuit()
    {
        if (quitConfirmationPanel != null) quitConfirmationPanel.SetActive(false);
        isGameActive  = true;
        isTimeRunning = true;
    }

    // ─── Navigation ───────────────────────────────────────────────────────────

    private void OnHomeButtonClicked()
    {
        Debug.Log("[GameManager] HOME clicked — returning to HomeScreen");
        int latestCoins = PlayerPrefs.GetInt("PlayerCoins", 100);
        Debug.Log($"[GameManager] Coins on home: {latestCoins}");

        if (SceneFlowManager.Instance != null)
            SceneFlowManager.Instance.ReturnToHomeScreen();
        else
        {
            Debug.LogWarning("[GameManager] SceneFlowManager null — loading HomeScreen directly.");
            SceneManager.LoadScene("HomeScreen");
        }
    }

    public void ReturnToHomeScreen() => OnHomeButtonClicked();

    public void RestartGame()
    {
        Debug.Log("[GameManager] Restarting...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Application.Quit();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    // ─── UI ───────────────────────────────────────────────────────────────────

    void UpdateUI()
    {
        if (playerScoreText != null)   playerScoreText.text   = playerScore.ToString();
        if (opponentScoreText != null) opponentScoreText.text = opponentScore.ToString();
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(currentTime / 60);
        int seconds = Mathf.FloorToInt(currentTime % 60);
        timerText.text  = string.Format("{0:00}:{1:00}", minutes, seconds);
        timerText.color = currentTime <= 30f ? Color.red
                        : currentTime <= 60f ? Color.yellow
                        : Color.white;
    }

    public bool IsGameActive()     => isGameActive && !isRoundTransition;
    public bool ArePaddlesFrozen() => isPaddlesFrozen;

    // ─── Response Models ──────────────────────────────────────────────────────

    [System.Serializable]
    private class SaveMatchResponse
    {
        public bool   success;
        public string message;
        public string matchId;
    }
}