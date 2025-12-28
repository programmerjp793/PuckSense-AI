using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("Score Settings")]
    [SerializeField] private int scoreToWin = 20;
    [SerializeField] private int playerScore = 0;
    [SerializeField] private int opponentScore = 0;
    
    [Header("Time Settings")]
    [SerializeField] private float matchTimeLimit = 180f; // 3 minutes in seconds
    [SerializeField] private float currentTime;
    [SerializeField] private bool isTimeRunning = true;
    
    [Header("UI References")]
    [SerializeField] private TMP_Text playerScoreText;
    [SerializeField] private TMP_Text opponentScoreText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private GameObject roundPanel;
    [SerializeField] private TMP_Text roundText;
    
    [Header("Game Objects")]
    [SerializeField] private GameObject puck;
    [SerializeField] private GameObject paddle1;
    [SerializeField] private GameObject paddle2;
    [SerializeField] private Transform puckStartPosition;
    [SerializeField] private Transform paddle1StartPosition;
    [SerializeField] private Transform paddle2StartPosition;
    
    [Header("Goal Zones")]
    [SerializeField] private BoxCollider2D playerGoalZone;  // Bottom goal
    [SerializeField] private BoxCollider2D opponentGoalZone; // Top goal
    
    [Header("Serving Settings")]
    [SerializeField] private Vector2 playerServePosition = new Vector2(0f, -3f);  // Position near player's goal
    [SerializeField] private Vector2 opponentServePosition = new Vector2(0f, 3f); // Position near opponent's goal
    [SerializeField] private float serveDelay = 2f; // Time before serving after a goal
    
    private bool isGameActive = true;
    private bool isRoundTransition = false;
    private bool playerServes = true; // True = player serves next, False = opponent serves next
    
    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        currentTime = matchTimeLimit;
        UpdateUI();
        
        if (winPanel != null) winPanel.SetActive(false);
        if (roundPanel != null) roundPanel.SetActive(false);
        
        // Set up goal zones as triggers
        if (playerGoalZone != null) playerGoalZone.isTrigger = true;
        if (opponentGoalZone != null) opponentGoalZone.isTrigger = true;
        
        // Start with a random server or default to player
        playerServes = Random.value > 0.5f;
    }
    
    void Update()
    {
        if (isGameActive && isTimeRunning && !isRoundTransition)
        {
            // Update timer
            currentTime -= Time.deltaTime;
            UpdateTimerUI();
            
            // Check if time is up
            if (currentTime <= 0)
            {
                currentTime = 0;
                TimeUp();
            }
        }
    }
    
    public void PlayerScored()
    {
        // Called when puck enters opponent's goal (top) - Player with Paddle 1 scores
        Debug.Log($"PlayerScored() called! isGameActive: {isGameActive}, isRoundTransition: {isRoundTransition}");
        
        if (!isGameActive || isRoundTransition) 
        {
            Debug.LogWarning("PlayerScored() blocked - game not active or in transition");
            return;
        }
        
        playerScore++;
        Debug.Log($"✓ Player (Bottom - Paddle 1) scored! NEW SCORE: {playerScore} - {opponentScore}");
        UpdateUI(); // Updates PlayerScoreBackground (bottom display)
        
        // Opponent serves next (the one who got scored on)
        playerServes = false;
        
        if (playerScore >= scoreToWin)
        {
            EndGame("PLAYER WINS!");
        }
        else
        {
            Debug.Log("Starting new round for player score... Opponent will serve.");
            StartNewRound("GOAL! Player Scored!");
        }
    }
    
    public void OpponentScored()
    {
        // Called when puck enters player's goal (bottom) - Opponent with Paddle 2 scores
        Debug.Log($"OpponentScored() called! isGameActive: {isGameActive}, isRoundTransition: {isRoundTransition}");
        
        if (!isGameActive || isRoundTransition) 
        {
            Debug.LogWarning("OpponentScored() blocked - game not active or in transition");
            return;
        }
        
        opponentScore++;
        Debug.Log($"✓ Opponent (Top - Paddle 2) scored! NEW SCORE: {playerScore} - {opponentScore}");
        UpdateUI(); // Updates OpponentScoreBackground (top display)
        
        // Player serves next (the one who got scored on)
        playerServes = true;
        
        if (opponentScore >= scoreToWin)
        {
            EndGame("OPPONENT WINS!");
        }
        else
        {
            Debug.Log("Starting new round for opponent score... Player will serve.");
            StartNewRound("GOAL! Opponent Scored!");
        }
    }
    
    void TimeUp()
    {
        isGameActive = false;
        
        // Determine winner based on score
        if (playerScore > opponentScore)
        {
            EndGame("TIME UP! PLAYER WINS!");
        }
        else if (opponentScore > playerScore)
        {
            EndGame("TIME UP! OPPONENT WINS!");
        }
        else
        {
            EndGame("TIME UP! IT'S A TIE!");
        }
    }
    
    void StartNewRound(string message)
    {
        isRoundTransition = true;
        
        // Show round message
        if (roundPanel != null && roundText != null)
        {
            roundPanel.SetActive(true);
            roundText.text = message;
        }
        
        // Reset positions after delay
        Invoke("ResetPositions", serveDelay);
    }
    
    void ResetPositions()
    {
        // Reset puck to serving position
        if (puck != null)
        {
            Rigidbody2D puckRb = puck.GetComponent<Rigidbody2D>();
            if (puckRb != null)
            {
                puckRb.linearVelocity = Vector2.zero;
                puckRb.angularVelocity = 0f;
            }
            
            // Position puck based on who is serving
            if (playerServes)
            {
                puck.transform.position = playerServePosition;
                Debug.Log("Puck placed at player's serve position (bottom)");
            }
            else
            {
                puck.transform.position = opponentServePosition;
                Debug.Log("Puck placed at opponent's serve position (top)");
            }
        }
        
        // Reset paddle 1
        if (paddle1 != null && paddle1StartPosition != null)
        {
            paddle1.transform.position = paddle1StartPosition.position;
            Rigidbody2D paddle1Rb = paddle1.GetComponent<Rigidbody2D>();
            if (paddle1Rb != null) paddle1Rb.linearVelocity = Vector2.zero;
        }
        
        // Reset paddle 2
        if (paddle2 != null && paddle2StartPosition != null)
        {
            paddle2.transform.position = paddle2StartPosition.position;
            Rigidbody2D paddle2Rb = paddle2.GetComponent<Rigidbody2D>();
            if (paddle2Rb != null) paddle2Rb.linearVelocity = Vector2.zero;
        }
        
        // Hide round panel
        if (roundPanel != null)
        {
            roundPanel.SetActive(false);
        }
        
        isRoundTransition = false;
    }
    
    void EndGame(string message)
    {
        isGameActive = false;
        isTimeRunning = false;
        
        // Stop puck
        if (puck != null)
        {
            Rigidbody2D puckRb = puck.GetComponent<Rigidbody2D>();
            if (puckRb != null)
            {
                puckRb.linearVelocity = Vector2.zero;
                puckRb.angularVelocity = 0f;
            }
        }
        
        // Show win panel
        if (winPanel != null && winnerText != null)
        {
            winPanel.SetActive(true);
            winnerText.text = message;
        }
        
        Debug.Log($"Game Over! {message}");
    }
    
    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    
    public void QuitGame()
    {
        Application.Quit();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
    
    void UpdateUI()
    {
        Debug.Log($"UpdateUI() called - Player: {playerScore}, Opponent: {opponentScore}");
        
        if (playerScoreText != null)
        {
            playerScoreText.text = playerScore.ToString();
            Debug.Log($"PlayerScoreText updated to: {playerScore}");
        }
        else
        {
            Debug.LogWarning("PlayerScoreText is NULL! Cannot update player score display.");
        }
        
        if (opponentScoreText != null)
        {
            opponentScoreText.text = opponentScore.ToString();
            Debug.Log($"OpponentScoreText updated to: {opponentScore}");
        }
        else
        {
            Debug.LogWarning("OpponentScoreText is NULL! Cannot update opponent score display.");
        }
    }
    
    void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(currentTime / 60);
            int seconds = Mathf.FloorToInt(currentTime % 60);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            
            // Change color when time is running out
            if (currentTime <= 30f)
            {
                timerText.color = Color.red;
            }
            else if (currentTime <= 60f)
            {
                timerText.color = Color.yellow;
            }
            else
            {
                timerText.color = Color.white;
            }
        }
    }
    
    public bool IsGameActive()
    {
        return isGameActive && !isRoundTransition;
    }
}