using UnityEngine;

/// <summary>
/// Behavior Tree – Decision Layer
/// Analyses game state and selects the high-level AI behaviour,
/// then passes the decision to the FSM for execution.
/// 
/// Priority order (highest first):
///   1. ResetAfterScore  – if a goal was just scored
///   2. Defense          – immediate threat to AI goal
///   3. Offense          – puck is in AI half OR approaching midfield (offensive bias)
///   4. ReturnHome       – puck is in player half, no immediate threat
///   5. Idle             – default fallback
/// </summary>
public class AIBehaviorTree
{
    // ─── Sub-systems ──────────────────────────────────────────────────────────
    private readonly AIStateMachine        fsm;
    private readonly RuleBasedActionSystem rules;
    private readonly PuckPredictionModule  predictor;

    // ─── Configuration ────────────────────────────────────────────────────────
    private readonly float fieldMidY;       // Y = 0 assumed as mid-line
    private readonly float aiHalfMinY;      // start of AI territory
    private readonly float defenseLineY;    // Y where AI guards goal
    private readonly float threatLineY;     // puck Y above which it's a threat
    private readonly float paddleMoveSpeed;

    // Hysteresis: require state to be valid for this many seconds before switching
    // Lowered from 0.08f → 0.03f so AI commits to offense faster
    private const float STATE_HYSTERESIS = 0.03f;
    private AIStateMachine.AIState pendingState    = AIStateMachine.AIState.Idle;
    private float                  pendingStateTime = 0f;

    // Reset flag (set by external GameManager callback)
    private bool  postGoalReset  = false;
    private float resetDuration  = 1.5f;
    private float resetStartTime = -999f;

    // ─── Constructor ─────────────────────────────────────────────────────────
    public AIBehaviorTree(
        AIStateMachine        stateMachine,
        RuleBasedActionSystem ruleSystem,
        PuckPredictionModule  predModule,
        float midY, float halfStartY, float defLine, float threatLine, float moveSpeed)
    {
        fsm             = stateMachine;
        rules           = ruleSystem;
        predictor       = predModule;
        fieldMidY       = midY;
        aiHalfMinY      = halfStartY;
        defenseLineY    = defLine;
        threatLineY     = threatLine;
        paddleMoveSpeed = moveSpeed;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Notify the tree that a goal was just scored (triggers reset state).
    /// </summary>
    public void OnGoalScored()
    {
        postGoalReset  = true;
        resetStartTime = Time.time;
        fsm.TransitionTo(AIStateMachine.AIState.ResetAfterScore);
    }

    /// <summary>
    /// Main tick. Call every FixedUpdate.
    /// Returns the desired paddle world position.
    /// </summary>
    public Vector2 Tick(
        Vector2 paddlePos,
        Vector2 puckPos,
        Vector2 puckVelocity,
        float   lookaheadTime = 0.8f)
    {
        // ── Update sub-systems ────────────────────────────────────────────────
        rules.UpdatePuckState(puckPos, puckVelocity);

        Vector2 perceivedPuck     = rules.GetPerceivedPuckPosition();
        Vector2 perceivedVelocity = rules.GetPerceivedPuckVelocity();

        predictor.UpdatePrediction(perceivedPuck, perceivedVelocity, lookaheadTime);

        Vector2 interceptPos = predictor.GetInterceptPosition(
            perceivedPuck, perceivedVelocity, paddlePos, paddleMoveSpeed);

        // ── Decision Tree ─────────────────────────────────────────────────────
        AIStateMachine.AIState decidedState = DecideState(paddlePos, perceivedPuck, perceivedVelocity);

        // Apply hysteresis to avoid rapid flipping
        if (decidedState != pendingState)
        {
            pendingState     = decidedState;
            pendingStateTime = Time.time;
        }
        else if (Time.time - pendingStateTime >= STATE_HYSTERESIS)
        {
            fsm.TransitionTo(decidedState);
        }

        // ── Get Desired Movement from FSM ─────────────────────────────────────
        Vector2 rawDesired = fsm.GetDesiredPosition(
            paddlePos, perceivedPuck,
            predictor.PredictedPosition, interceptPos,
            reactionRadius: 0.6f);

        // ── Apply Rule-Based Adjustments ──────────────────────────────────────
        Vector2 finalPosition = ApplyRules(
            fsm.CurrentState, rawDesired, paddlePos, perceivedPuck, perceivedVelocity);

        return finalPosition;
    }

    // ─── Private Decision Logic ───────────────────────────────────────────────

    private AIStateMachine.AIState DecideState(
        Vector2 paddlePos, Vector2 puckPos, Vector2 puckVelocity)
    {
        // Node 1: Post-goal reset
        if (postGoalReset)
        {
            if (Time.time - resetStartTime < resetDuration)
                return AIStateMachine.AIState.ResetAfterScore;
            else
                postGoalReset = false;
        }

        // Node 2: Immediate threat to AI goal (defense critical)
        bool immediateThreat = rules.IsImmediateThreat(puckPos, puckVelocity, threatLineY);
        if (immediateThreat)
            return AIStateMachine.AIState.Defense;

        // Node 3: Puck is in AI half OR approaching midfield — push offense
        // Extended from >= aiHalfMinY to >= aiHalfMinY - 1.5f so the AI
        // presses forward even when the puck is still 1.5 units past midline.
        if (puckPos.y >= aiHalfMinY - 1.5f)
        {
            // Go offensive whenever there's any attack opportunity,
            // even if the puck is moving toward the AI.
            if (predictor.AttackScore > 0.2f || puckVelocity.y >= 0)
                return AIStateMachine.AIState.Offense;
            else
                return AIStateMachine.AIState.Defense;
        }

        // Node 4: Puck in player half – return home
        if (puckPos.y < fieldMidY)
            return AIStateMachine.AIState.ReturnHome;

        // Node 5: Fallback
        return AIStateMachine.AIState.Defense;
    }

    private Vector2 ApplyRules(
        AIStateMachine.AIState state,
        Vector2 rawDesired,
        Vector2 paddlePos,
        Vector2 puckPos,
        Vector2 puckVelocity)
    {
        switch (state)
        {
            case AIStateMachine.AIState.Defense:
                // Use goal guard logic for X, lock to defense Y
                Vector2 guardPos = rules.GetGoalGuardPosition(paddlePos);
                return new Vector2(guardPos.x, rawDesired.y);

            case AIStateMachine.AIState.Offense:
                // Use noisy tracking toward the intercept
                return rules.GetTrackingPosition(rawDesired);

            case AIStateMachine.AIState.ReturnHome:
            case AIStateMachine.AIState.ResetAfterScore:
            case AIStateMachine.AIState.Idle:
            default:
                return rawDesired;  // No rule modification for home/reset
        }
    }
}