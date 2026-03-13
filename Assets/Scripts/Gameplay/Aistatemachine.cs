using UnityEngine;

/// <summary>
/// Finite State Machine – Execution Layer
/// States: Defense | Offense | ReturnHome | ResetAfterScore
/// The Behavior Tree selects the state; the FSM executes movement logic.
///
/// UPDATED: Added ApplyAIProfile() so AIEngine can set difficulty params.
/// All existing logic is unchanged.
/// </summary>
public class AIStateMachine
{
    // ─── State Enum ──────────────────────────────────────────────────────────
    public enum AIState
    {
        Idle,
        Defense,
        Offense,
        ReturnHome,
        ResetAfterScore
    }

    // ─── Properties ──────────────────────────────────────────────────────────
    public AIState CurrentState  { get; private set; } = AIState.Idle;
    public AIState PreviousState { get; private set; } = AIState.Idle;

    // ─── Private Fields ───────────────────────────────────────────────────────
    private float stateEnterTime = 0f;

    private readonly Vector2 homePosition;
    private readonly Vector2 defensePosition;
    private readonly float   defenseLineY;
    private readonly float   attackLineY;
    private readonly float   paddleMoveSpeed;

    // ─── Constructor ─────────────────────────────────────────────────────────
    public AIStateMachine(Vector2 home, float defLine, float atkLine, float moveSpeed)
    {
        homePosition    = home;
        defenseLineY    = defLine;
        attackLineY     = atkLine;
        paddleMoveSpeed = moveSpeed;

        defensePosition = new Vector2(home.x, defLine);
    }

    // ─── State Transitions ────────────────────────────────────────────────────

    /// <summary>Transitions to the given state. Ignores if already in that state.</summary>
    public void TransitionTo(AIState newState)
    {
        if (newState == CurrentState) return;

        PreviousState  = CurrentState;
        CurrentState   = newState;
        stateEnterTime = Time.time;

        Debug.Log($"[AIStateMachine] {PreviousState} → {newState}");
    }

    public float TimeInState => Time.time - stateEnterTime;

    // ─── NEW: Apply AI Profile from backend ──────────────────────────────────

    /// <summary>
    /// Called by AIEngine when a server-assigned AI Profile is received.
    /// Maps the strategy string to a starting AIState.
    /// </summary>
    public void ApplyStrategy(string strategy)
    {
        AIState startState = strategy switch
        {
            "aggressive" => AIState.Offense,
            "defensive"  => AIState.Defense,
            "balanced"   => AIState.ReturnHome,
            "adaptive"   => AIState.Idle,
            _            => AIState.Idle,
        };

        TransitionTo(startState);
        Debug.Log($"[AIStateMachine] Strategy '{strategy}' → starting state: {startState}");
    }

    // ─── Movement Output Per State ────────────────────────────────────────────

    /// <summary>
    /// Returns the desired world position the AI paddle should move toward this frame.
    /// Call every FixedUpdate after the Behavior Tree has set CurrentState.
    /// </summary>
    public Vector2 GetDesiredPosition(
        Vector2 paddlePos,
        Vector2 puckPos,
        Vector2 predictedPuckPos,
        Vector2 interceptPos,
        float   reactionRadius)
    {
        switch (CurrentState)
        {
            case AIState.Defense:
                return ExecuteDefense(paddlePos, puckPos, reactionRadius);

            case AIState.Offense:
                return ExecuteOffense(paddlePos, puckPos, predictedPuckPos, interceptPos);

            case AIState.ReturnHome:
                return homePosition;

            case AIState.ResetAfterScore:
                return homePosition;

            case AIState.Idle:
            default:
                return homePosition;
        }
    }

    // ─── State Execution ─────────────────────────────────────────────────────

    private Vector2 ExecuteDefense(Vector2 paddlePos, Vector2 puckPos, float reactionRadius)
    {
        float targetX = puckPos.x;

        if (Mathf.Abs(puckPos.x - paddlePos.x) < reactionRadius)
            targetX = paddlePos.x;

        return new Vector2(targetX, defenseLineY);
    }

    private Vector2 ExecuteOffense(Vector2 paddlePos, Vector2 puckPos,
                                   Vector2 predictedPos, Vector2 interceptPos)
    {
        float distToPuck = Vector2.Distance(paddlePos, puckPos);
        return (distToPuck < 1.5f) ? puckPos : interceptPos;
    }
}