using UnityEngine;

/// <summary>
/// Puck Prediction Module
/// Handles: Future Position Estimation, Bounce Direction Prediction,
///          Goal Threat Analysis, Attack Opportunity Detection
/// </summary>
public class PuckPredictionModule
{
    // ─── Configurable Parameters ─────────────────────────────────────────────
    private readonly float fieldMinX;
    private readonly float fieldMaxX;
    private readonly float fieldMinY;
    private readonly float fieldMaxY;

    private const int   MAX_BOUNCES        = 6;
    private const float PREDICTION_TIMESTEP = 0.016f; // ~60 fps
    private const float MAX_PREDICT_TIME   = 2.5f;

    // Goal threat is HIGH when puck is heading toward opponent's goal (top) with Y > this threshold
    private const float PLAYER_GOAL_Y      = -5.5f;   // bottom goal centre Y
    private const float OPPONENT_GOAL_Y    =  5.5f;   // top goal centre Y
    private const float GOAL_HALF_WIDTH    =  1.0f;

    // ─── Cached Results ───────────────────────────────────────────────────────
    public Vector2 PredictedPosition   { get; private set; }
    public bool    IsGoalThreat        { get; private set; }   // puck heading to AI goal (top)
    public bool    IsAttackOpportunity { get; private set; }   // puck heading to player goal (bottom)
    public float   ThreatScore         { get; private set; }   // 0-1
    public float   AttackScore         { get; private set; }   // 0-1

    // ─── Constructor ─────────────────────────────────────────────────────────
    public PuckPredictionModule(float minX, float maxX, float minY, float maxY)
    {
        fieldMinX = minX;
        fieldMaxX = maxX;
        fieldMinY = minY;
        fieldMaxY = maxY;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Run a full prediction update. Call from AI brain every fixed update.
    /// </summary>
    public void UpdatePrediction(Vector2 puckPosition, Vector2 puckVelocity, float lookaheadTime)
    {
        lookaheadTime = Mathf.Clamp(lookaheadTime, 0.1f, MAX_PREDICT_TIME);

        PredictedPosition   = SimulatePath(puckPosition, puckVelocity, lookaheadTime, out bool reachesTop, out bool reachesBottom);
        IsGoalThreat        = reachesTop;
        IsAttackOpportunity = reachesBottom;

        // Threat score: how centred is the puck trajectory on the AI goal?
        ThreatScore  = IsGoalThreat  ? 1f - Mathf.Clamp01(Mathf.Abs(PredictedPosition.x) / GOAL_HALF_WIDTH) : 0f;
        AttackScore  = IsAttackOpportunity ? 1f - Mathf.Clamp01(Mathf.Abs(PredictedPosition.x) / GOAL_HALF_WIDTH) : 0f;
    }

    /// <summary>
    /// Predict where the puck will be at a specific time, accounting for wall bounces.
    /// </summary>
    public Vector2 PredictPositionAt(Vector2 pos, Vector2 vel, float time)
    {
        return SimulatePath(pos, vel, time, out _, out _);
    }

    /// <summary>
    /// Returns the ideal intercept position for the AI paddle given current puck state.
    /// </summary>
    public Vector2 GetInterceptPosition(Vector2 puckPos, Vector2 puckVelocity,
                                        Vector2 paddlePos, float paddleSpeed)
    {
        // Binary-search for the earliest time where the paddle can physically reach the puck
        float tMin = 0f;
        float tMax = MAX_PREDICT_TIME;

        for (int i = 0; i < 10; i++)
        {
            float tMid     = (tMin + tMax) * 0.5f;
            Vector2 future = PredictPositionAt(puckPos, puckVelocity, tMid);
            float   dist   = Vector2.Distance(paddlePos, future);
            float   canTravel = paddleSpeed * tMid;

            if (canTravel >= dist)
                tMax = tMid;
            else
                tMin = tMid;
        }

        return PredictPositionAt(puckPos, puckVelocity, tMax);
    }

    // ─── Private Simulation ───────────────────────────────────────────────────

    private Vector2 SimulatePath(Vector2 pos, Vector2 vel, float duration,
                                  out bool reachedTop, out bool reachedBottom)
    {
        reachedTop    = false;
        reachedBottom = false;

        if (vel.sqrMagnitude < 0.001f)
            return pos;

        float elapsed = 0f;
        int   bounces = 0;

        while (elapsed < duration && bounces < MAX_BOUNCES)
        {
            float dt = PREDICTION_TIMESTEP;

            // Time to hit each wall
            float tLeft   = (vel.x < 0) ? (fieldMinX - pos.x) / vel.x : float.MaxValue;
            float tRight  = (vel.x > 0) ? (fieldMaxX - pos.x) / vel.x : float.MaxValue;
            float tBottom = (vel.y < 0) ? (fieldMinY - pos.y) / vel.y : float.MaxValue;
            float tTop    = (vel.y > 0) ? (fieldMaxY - pos.y) / vel.y : float.MaxValue;

            float tWall = Mathf.Min(tLeft, tRight, tBottom, tTop);

            if (tWall > duration - elapsed)
            {
                // No wall hit within remaining time
                pos += vel * (duration - elapsed);
                elapsed = duration;
                break;
            }

            // Step to the wall
            pos     += vel * tWall;
            elapsed += tWall;
            bounces++;

            // Bounce the velocity
            if (Mathf.Approximately(tWall, tLeft) || Mathf.Approximately(tWall, tRight))
                vel.x = -vel.x;

            if (Mathf.Approximately(tWall, tBottom))
            {
                reachedBottom = true;
                vel.y = -vel.y;
            }
            if (Mathf.Approximately(tWall, tTop))
            {
                reachedTop = true;
                vel.y = -vel.y;
            }

            // Small epsilon to avoid re-triggering
            pos += vel * 0.001f;
        }

        // Goal threat analysis on final position
        if (!reachedTop    && pos.y >= fieldMaxY - 0.3f) reachedTop    = true;
        if (!reachedBottom && pos.y <= fieldMinY + 0.3f) reachedBottom = true;

        return pos;
    }
}