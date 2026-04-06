using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;

/// <summary>
/// MatchmakingPanelController
/// Handles matchmaking mode selection (1v1, AI vs Player) and the High Score panel.
///
/// In the Unity Inspector, assign:
///   - oneVsOneButton    → the existing "1v1" button
///   - aiVsPlayerButton  → the "AI vs Player" button
///   - highScoreButton   → the new "High Score" button
///   - highScorePanel    → a panel with a ScrollView for high score rows
///   - highScoreContentParent → the Content transform inside the ScrollView
///   - highScoreRowPrefab     → a prefab with TMP_Text children (see Unity Setup Guide)
/// </summary>
public class MatchmakingPanelController : MonoBehaviour
{
    [Header("Matchmaking Buttons")]
    [SerializeField] private Button oneVsOneButton;
    [SerializeField] private Button aiVsPlayerButton;

    [Header("Optional: Back Button")]
    [SerializeField] private Button backButton;

    [Header("High Score")]
    [SerializeField] private Button     highScoreButton;
    [SerializeField] private GameObject highScorePanel;
    [SerializeField] private Transform  highScoreContentParent;  // ScrollView > Viewport > Content
    [SerializeField] private GameObject highScoreRowPrefab;      // Prefab with TMP_Text children
    [SerializeField] private Button     highScoreBackButton;
    [SerializeField] private TMP_Text   highScoreStatusText;     // "Loading..." / "No records" / personal best
    [SerializeField] private TMP_Text   personalBestText;        // Displays "Best Time: mm:ss"

    void Start()
    {
        // ─── Matchmaking Buttons ──────────────────────────────────────────────
        if (oneVsOneButton != null)
            oneVsOneButton.onClick.AddListener(OnOneVsOneClicked);
        else
            Debug.LogWarning("[MatchmakingPanelController] oneVsOneButton not assigned.");

        if (aiVsPlayerButton != null)
            aiVsPlayerButton.onClick.AddListener(OnAIvsPlayerClicked);
        else
            Debug.LogWarning("[MatchmakingPanelController] aiVsPlayerButton not assigned.");

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);

        // ─── High Score Buttons ───────────────────────────────────────────────
        if (highScoreButton != null)
            highScoreButton.onClick.AddListener(OnHighScoreClicked);
        else
            Debug.LogWarning("[MatchmakingPanelController] highScoreButton not assigned.");

        if (highScoreBackButton != null)
            highScoreBackButton.onClick.AddListener(OnHighScoreBackClicked);

        // Hide high score panel at start
        if (highScorePanel != null)
            highScorePanel.SetActive(false);
    }

    // ─── Matchmaking ──────────────────────────────────────────────────────────

    private void OnOneVsOneClicked()
    {
        Debug.Log("MatchmakingPanel: 1v1 selected");
        if (SceneFlowManager.Instance != null)
            SceneFlowManager.Instance.LoadOneVsOne();
        else
            Debug.LogError("SceneFlowManager instance not found!");
    }

    private void OnAIvsPlayerClicked()
    {
        Debug.Log("MatchmakingPanel: AI vs Player selected");
        if (SceneFlowManager.Instance != null)
            SceneFlowManager.Instance.LoadAIvsPlayer();
        else
            Debug.LogError("SceneFlowManager instance not found!");
    }

    private void OnBackClicked()
    {
        // Hide high score panel if it's open
        if (highScorePanel != null)
            highScorePanel.SetActive(false);

        gameObject.SetActive(false);
    }

    // ─── High Score ───────────────────────────────────────────────────────────

    private async void OnHighScoreClicked()
    {
        Debug.Log("[MatchmakingPanel] High Score button clicked");

        if (highScorePanel == null)
        {
            Debug.LogError("[MatchmakingPanel] highScorePanel not assigned!");
            return;
        }

        highScorePanel.SetActive(true);

        // Show loading state
        if (highScoreStatusText != null)
            highScoreStatusText.text = "Loading...";
        if (personalBestText != null)
            personalBestText.text = "";

        // Clear previous rows
        ClearHighScoreRows();

        // Fetch from backend
        try
        {
            if (ApiClient.Instance == null)
            {
                Debug.LogError("[MatchmakingPanel] ApiClient.Instance is null!");
                if (highScoreStatusText != null)
                    highScoreStatusText.text = "Error: API not available";
                return;
            }

            string json = await ApiClient.Instance.GetAsync("/match/highscores");
            Debug.Log($"[MatchmakingPanel] Highscores response: {json}");

            var response = JsonConvert.DeserializeObject<HighScoresResponse>(json);

            if (response == null || !response.success)
            {
                if (highScoreStatusText != null)
                    highScoreStatusText.text = "Failed to load high scores.";
                return;
            }

            // Display personal best
            if (personalBestText != null)
            {
                if (response.bestTime > 0)
                    personalBestText.text = $"🏆 Personal Best: {FormatTime(response.bestTime)}";
                else
                    personalBestText.text = "No personal best yet — win a match!";
            }

            // Populate rows
            if (response.highscores == null || response.highscores.Length == 0)
            {
                if (highScoreStatusText != null)
                    highScoreStatusText.text = "No high score records yet.\nWin matches to see your best times!";
                return;
            }

            // Hide status text since we have data
            if (highScoreStatusText != null)
                highScoreStatusText.text = "";

            PopulateHighScores(response.highscores);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MatchmakingPanel] Failed to fetch highscores: {e.Message}");
            if (highScoreStatusText != null)
                highScoreStatusText.text = "Error loading high scores.";
        }
    }

    private void PopulateHighScores(HighScoreEntry[] entries)
    {
        if (highScoreContentParent == null || highScoreRowPrefab == null)
        {
            Debug.LogWarning("[MatchmakingPanel] highScoreContentParent or highScoreRowPrefab not assigned.");
            return;
        }

        foreach (var entry in entries)
        {
            GameObject row = Instantiate(highScoreRowPrefab, highScoreContentParent);
            row.SetActive(true);

            // Find TMP_Text children by name
            // Expected children: "RankText", "DifficultyText", "ScoreText", "TimeText"
            TMP_Text rankText       = FindChildText(row, "RankText");
            TMP_Text difficultyText = FindChildText(row, "DifficultyText");
            TMP_Text scoreText      = FindChildText(row, "ScoreText");
            TMP_Text timeText       = FindChildText(row, "TimeText");

            if (rankText != null)       rankText.text       = $"#{entry.rank}";
            if (difficultyText != null) difficultyText.text = CapitalizeFirst(entry.difficulty);
            if (scoreText != null)      scoreText.text      = $"{entry.playerScore} - {entry.opponentScore}";
            if (timeText != null)       timeText.text       = FormatTime(entry.durationSecs);

            Debug.Log($"[HighScore] Row {entry.rank}: {entry.difficulty} | " +
                      $"{entry.playerScore}-{entry.opponentScore} | {FormatTime(entry.durationSecs)}");
        }
    }

    private void ClearHighScoreRows()
    {
        if (highScoreContentParent == null) return;

        foreach (Transform child in highScoreContentParent)
        {
            // Skip the header row if it has the tag "Header" or name "HeaderRow"
            if (child.name == "HeaderRow") continue;
            Destroy(child.gameObject);
        }
    }

    private void OnHighScoreBackClicked()
    {
        if (highScorePanel != null)
            highScorePanel.SetActive(false);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Format seconds into mm:ss display string.</summary>
    private string FormatTime(int totalSeconds)
    {
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    /// <summary>Find a TMP_Text component on a child by name (recursive).</summary>
    private TMP_Text FindChildText(GameObject parent, string childName)
    {
        Transform found = parent.transform.Find(childName);
        if (found != null)
            return found.GetComponent<TMP_Text>();

        // Recursive search
        foreach (Transform child in parent.transform)
        {
            TMP_Text result = FindChildText(child.gameObject, childName);
            if (result != null) return result;
        }

        Debug.LogWarning($"[MatchmakingPanel] Child '{childName}' not found in {parent.name}");
        return null;
    }

    /// <summary>Capitalize first letter of a string.</summary>
    private string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Unknown";
        return char.ToUpper(s[0]) + s.Substring(1);
    }
}