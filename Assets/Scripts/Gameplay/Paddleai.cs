using UnityEngine;

/// <summary>
/// PaddleAI – AI-controlled opponent paddle for "AI vs Player" mode.
/// 
/// Integrates the four-module hybrid AI architecture:
///   1. PuckPredictionModule  – future puck position / threat analysis
///   2. AIStateMachine        – execution-layer FSM
///   3. RuleBasedActionSystem – tracking, guarding, reaction delay, error injection
///   4. AIBehaviorTree        – decision layer that selects FSM state
///
/// Drop this component onto the opponent paddle GameObject in the GameScene
/// when the game mode is "AIvsPlayer". Disable PaddleOpponent on the same object.
///
/// UPDATED: Added ApplyAIProfile() so AIEngine can apply server-assigned profiles.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PaddleAI : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Transform puckTransform;

    [Header("Movement Constraints (Opponent / Top Half)")]
    [SerializeField] private float minX         = -2.75f;
    [SerializeField] private float maxX         =  2.75f;
    [SerializeField] private float minY         =  1.0f;
    [SerializeField] private float maxY         =  6.75f;

    [Header("AI Configuration")]
    [SerializeField] private RuleBasedActionSystem.Difficulty difficulty = RuleBasedActionSystem.Difficulty.Medium;
    [SerializeField] private float paddleMoveSpeed = 12f;
    [SerializeField] private float lookaheadTime   = 1.2f;

    [Header("Goal Guard Setup")]
    [Tooltip("Y of the AI's goal line (top goal)")]
    [SerializeField] private float defenseLineY  =  5.8f;
    [Tooltip("Y above which a puck heading upward is a goal threat")]
    [SerializeField] private float threatLineY   =  3.5f;
    [Tooltip("Half-width of the AI goal opening")]
    [SerializeField] private float goalHalfWidth =  1.0f;

    [Header("Physics Material")]
    [SerializeField] private PhysicsMaterial2D paddlePhysicsMaterial;

    [Header("Force Settings (collision)")]
    [SerializeField] private float forceMultiplier = 3f;
    [SerializeField] private float maxHitForce     = 60f;
    [SerializeField] private float minHitForce     = 3f;

    // ─── AI Module Instances ──────────────────────────────────────────────────
    private PuckPredictionModule  predictor;
    private AIStateMachine        fsm;
    private RuleBasedActionSystem rules;
    private AIBehaviorTree        behaviorTree;

    // ─── Physics ─────────────────────────────────────────────────────────────
    private Rigidbody2D rb;
    private Vector2     lastPosition;
    private Vector2     paddleVelocity;
    private Vector2     targetPosition;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (paddlePhysicsMaterial != null)
            rb.sharedMaterial = paddlePhysicsMaterial;
    }

    void Start()
    {
        lastPosition   = rb.position;
        targetPosition = rb.position;

        InitAI();

        // Auto-find puck if not assigned
        if (puckTransform == null)
        {
            GameObject puckObj = GameObject.FindGameObjectWithTag("Puck");
            if (puckObj != null) puckTransform = puckObj.transform;
            else Debug.LogWarning("[PaddleAI] Puck not found! Assign puckTransform in Inspector.");
        }

        Debug.Log("[PaddleAI] AI paddle initialised. Difficulty: " + difficulty);
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.ArePaddlesFrozen())
            return;

        if (!IsAIMode()) return;

        Tick();
    }

    void FixedUpdate()
    {
        Vector2 currentPos = rb.position;
        paddleVelocity = (currentPos - lastPosition) / Time.fixedDeltaTime;
        lastPosition   = currentPos;

        Vector2 clamped = new Vector2(
            Mathf.Clamp(targetPosition.x, minX, maxX),
            Mathf.Clamp(targetPosition.y, minY, maxY));

        Vector2 newPos = Vector2.MoveTowards(rb.position, clamped, paddleMoveSpeed * Time.fixedDeltaTime);
        rb.MovePosition(newPos);
    }

    // ─── AI Tick ─────────────────────────────────────────────────────────────

    private void Tick()
    {
        if (puckTransform == null) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsGameActive()) return;

        Rigidbody2D puckRb  = puckTransform.GetComponent<Rigidbody2D>();
        Vector2     puckPos = puckTransform.position;
        Vector2     puckVel = (puckRb != null) ? puckRb.linearVelocity : Vector2.zero;

        targetPosition = behaviorTree.Tick(rb.position, puckPos, puckVel, lookaheadTime);
    }

    // ─── Initialization ───────────────────────────────────────────────────────

    private void InitAI()
    {
        float fieldMinX = minX;
        float fieldMaxX = maxX;
        float fieldMinY = -maxY;
        float fieldMaxY = maxY;

        predictor = new PuckPredictionModule(fieldMinX, fieldMaxX, fieldMinY, fieldMaxY);

        Vector2 homePos     = new Vector2(0f, (minY + maxY) * 0.5f);
        float   aiHalfStart = 0f;

        fsm = new AIStateMachine(homePos, defenseLineY, aiHalfStart, paddleMoveSpeed);

        rules = new RuleBasedActionSystem(
            difficulty,
            gMinX: -goalHalfWidth,
            gMaxX:  goalHalfWidth,
            gY:     defenseLineY - 0.3f);

        behaviorTree = new AIBehaviorTree(
            fsm, rules, predictor,
            midY:       0f,
            halfStartY: aiHalfStart,
            defLine:    defenseLineY,
            threatLine: threatLineY,
            moveSpeed:  paddleMoveSpeed);
    }

    // ─── Difficulty Control ───────────────────────────────────────────────────

    /// <summary>
    /// Change difficulty using the preset enum.
    /// Called between rounds or from a difficulty menu.
    /// </summary>
    public void SetDifficulty(RuleBasedActionSystem.Difficulty newDifficulty)
    {
        difficulty = newDifficulty;
        rules?.SetDifficulty(newDifficulty);
    }

    // ─── NEW: Apply Server-Assigned AI Profile ────────────────────────────────

    /// <summary>
    /// Called by AIEngine when a backend AI Profile is received from MatchManager.
    /// Maps server float values (reactionSpeed, errorMargin, strategy) directly
    /// to the existing AI subsystem parameters — overrides the Inspector difficulty.
    /// </summary>
    public void ApplyAIProfile(AIProfile profile)
    {
        if (profile == null) return;

        Debug.Log($"[PaddleAI] ApplyAIProfile → {profile.difficulty} | " +
                  $"speed={profile.reactionSpeed:F2} | " +
                  $"error={profile.errorMargin:F2} | " +
                  $"strategy={profile.strategy}");

        // 1. Apply reaction speed + error margin to RuleBasedActionSystem
        //    (overrides the preset difficulty values with precise server values)
        if (rules != null)
        {
            rules.SetReactionSpeed(profile.reactionSpeed);
            rules.SetErrorMargin(profile.errorMargin);
        }
        else
        {
            Debug.LogWarning("[PaddleAI] rules is null — InitAI() may not have run yet.");
        }

        // 2. Apply strategy to AIStateMachine starting state
        if (fsm != null)
        {
            fsm.ApplyStrategy(profile.strategy);
        }
        else
        {
            Debug.LogWarning("[PaddleAI] fsm is null — InitAI() may not have run yet.");
        }

        // 3. PuckPredictionModule and AIBehaviorTree adapt automatically
        //    through the updated rules and fsm — no direct changes needed.

        // 4. Update the Inspector difficulty label to match (cosmetic)
        difficulty = profile.difficulty switch
        {
            "easy"   => RuleBasedActionSystem.Difficulty.Easy,
            "hard"   => RuleBasedActionSystem.Difficulty.Hard,
            "expert" => RuleBasedActionSystem.Difficulty.Expert,
            _        => RuleBasedActionSystem.Difficulty.Medium,
        };
    }

    // ─── Goal Notification ────────────────────────────────────────────────────

    /// <summary>
    /// Call this when a goal is scored to trigger the reset state in the behavior tree.
    /// </summary>
    public void NotifyGoalScored()
    {
        behaviorTree?.OnGoalScored();
    }

    // ─── Collision – Hitting the Puck ─────────────────────────────────────────

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Puck")) return;

        Rigidbody2D puckRb = collision.gameObject.GetComponent<Rigidbody2D>();
        if (puckRb == null) return;

        Vector2 hitDir   = (puckRb.position - rb.position).normalized;
        float   velMag   = paddleVelocity.magnitude;
        float   forceMag = Mathf.Clamp(velMag * forceMultiplier, minHitForce, maxHitForce);

        Vector2 forceDir = (hitDir * 0.7f + paddleVelocity.normalized * 0.3f).normalized;
        puckRb.linearVelocity = Vector2.zero;
        puckRb.AddForce(forceDir * forceMag, ForceMode2D.Impulse);

        Debug.Log($"[PaddleAI] Hit puck – vel:{velMag:F2}  force:{forceMag:F2}");
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Puck")) return;

        Rigidbody2D puckRb = collision.gameObject.GetComponent<Rigidbody2D>();
        if (puckRb != null)
        {
            float momentum = rb.mass * paddleVelocity.magnitude;
            if (Mathf.Clamp01(momentum / 10f) < 0.3f)
                puckRb.linearVelocity *= 0.98f;
        }
        rb.linearVelocity = Vector2.zero;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private bool IsAIMode()
    {
        string mode = SceneFlowManager.Instance != null
            ? SceneFlowManager.Instance.GetCurrentGameMode()
            : PlayerPrefs.GetString("GameMode", "");
        return mode == "AIvsPlayer";
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0),
            new Vector3(maxX - minX, maxY - minY, 0));

        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(minX, defenseLineY, 0), new Vector3(maxX, defenseLineY, 0));

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(minX, threatLineY, 0), new Vector3(maxX, threatLineY, 0));

        if (Application.isPlaying && predictor != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(predictor.PredictedPosition, 0.2f);
        }
    }
}