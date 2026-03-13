// AIEngine.cs
// Central AI Engine controller.
// Receives the server-assigned AI Profile from MatchManager
// and applies it to PaddleAI.
//
// Data models (AIProfile, MatchData) are defined in SharedModels.cs
// Do NOT redefine them here.

using UnityEngine;
using UnityEngine.Events;

public class AIEngine : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static AIEngine Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("AI Subsystem References")]
    [Tooltip("Assign your PaddleAI GameObject. Auto-found if left empty.")]
    [SerializeField] private PaddleAI _paddleAI;

    // ─── Public State ─────────────────────────────────────────────────────────
    public AIProfile CurrentProfile { get; private set; }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (_paddleAI == null)
            _paddleAI = FindFirstObjectByType<PaddleAI>();

        if (_paddleAI == null)
            Debug.LogWarning("[AIEngine] PaddleAI not found. Assign it in Inspector.");

        if (MatchManager.Instance != null)
        {
            // ✅ FIX: Use the correct UnityAction type — MatchData is now in SharedModels.cs
            MatchManager.Instance.OnMatchCreated.AddListener(OnMatchCreated);
            Debug.Log("[AIEngine] Subscribed to MatchManager.OnMatchCreated.");
        }
        else
        {
            Debug.LogWarning("[AIEngine] MatchManager not found in scene.");
        }
    }

    void OnDestroy()
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.OnMatchCreated.RemoveListener(OnMatchCreated);
    }

    // ─── Event Handler ────────────────────────────────────────────────────────

    // ✅ FIX: Parameter type matches MatchManager.OnMatchCreated event type
    private void OnMatchCreated(MatchData matchData)
    {
        if (matchData?.aiProfile == null)
        {
            Debug.LogWarning("[AIEngine] Received null AI profile.");
            return;
        }

        ApplyProfile(matchData.aiProfile);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public void ApplyProfile(AIProfile profile)
    {
        if (profile == null) return;

        CurrentProfile = profile;

        Debug.Log($"[AIEngine] Applying profile: {profile.difficulty} | " +
                  $"speed={profile.reactionSpeed:F2} | " +
                  $"error={profile.errorMargin:F2} | " +
                  $"strategy={profile.strategy}");

        if (_paddleAI != null)
            _paddleAI.ApplyAIProfile(profile);
        else
            Debug.LogWarning("[AIEngine] PaddleAI not assigned — profile not applied.");
    }

    // ─── Editor Testing ───────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("Test: Apply Easy Profile")]
    void TestEasy() => ApplyProfile(new AIProfile
        { difficulty = "easy", reactionSpeed = 0.3f, errorMargin = 0.5f, strategy = "defensive" });

    [ContextMenu("Test: Apply Medium Profile")]
    void TestMedium() => ApplyProfile(new AIProfile
        { difficulty = "medium", reactionSpeed = 0.55f, errorMargin = 0.3f, strategy = "balanced" });

    [ContextMenu("Test: Apply Hard Profile")]
    void TestHard() => ApplyProfile(new AIProfile
        { difficulty = "hard", reactionSpeed = 0.85f, errorMargin = 0.15f, strategy = "aggressive" });

    [ContextMenu("Test: Apply Expert Profile")]
    void TestExpert() => ApplyProfile(new AIProfile
        { difficulty = "expert", reactionSpeed = 0.97f, errorMargin = 0.05f, strategy = "adaptive" });
#endif
}