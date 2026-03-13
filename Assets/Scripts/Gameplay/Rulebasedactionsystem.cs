using UnityEngine;

/// <summary>
/// Rule-Based Action System
/// Handles: Puck Tracking, Goal Guarding Logic,
///          Reaction Delay Simulation, Random Error Injection
///
/// UPDATED: Added SetReactionSpeed() and SetErrorMargin() so
/// AIEngine can apply server-assigned AI profiles at runtime.
/// All existing logic is unchanged.
/// </summary>
public class RuleBasedActionSystem
{
    // ─── Configuration ────────────────────────────────────────────────────────
    private float reactionDelay;
    private float errorMagnitude;
    private float errorFrequency;
    private float trackingAccuracy;

    // ─── Goal Guard ───────────────────────────────────────────────────────────
    private readonly float goalMinX;
    private readonly float goalMaxX;
    private readonly float goalGuardY;

    // ─── Reaction Delay ───────────────────────────────────────────────────────
    private Vector2 delayedPuckPosition;
    private Vector2 delayedPuckVelocity;
    private float   lastUpdateTime = -999f;

    // ─── Error State ──────────────────────────────────────────────────────────
    private Vector2 currentError     = Vector2.zero;
    private float   nextErrorChange  = 0f;
    private float   errorChangePeriod = 0.4f;

    // ─── Difficulty Preset ────────────────────────────────────────────────────
    public enum Difficulty { Easy, Medium, Hard, Expert }

    // ─── Constructor ─────────────────────────────────────────────────────────
    public RuleBasedActionSystem(Difficulty difficulty, float gMinX, float gMaxX, float gY)
    {
        goalMinX = gMinX;
        goalMaxX = gMaxX;
        goalGuardY = gY;
        SetDifficulty(difficulty);
    }

    // ─── Difficulty Preset ────────────────────────────────────────────────────

    public void SetDifficulty(Difficulty difficulty)
    {
        switch (difficulty)
        {
            case Difficulty.Easy:
                reactionDelay    = 0.10f;
                errorMagnitude   = 0.4f;
                errorFrequency   = 0.15f;
                trackingAccuracy = 0.88f;
                break;
            case Difficulty.Medium:
                reactionDelay    = 0.25f;
                errorMagnitude   = 0.7f;
                errorFrequency   = 0.30f;
                trackingAccuracy = 0.72f;
                break;
            case Difficulty.Hard:
                reactionDelay    = 0.12f;
                errorMagnitude   = 0.3f;
                errorFrequency   = 0.15f;
                trackingAccuracy = 0.88f;
                break;
            case Difficulty.Expert:
                reactionDelay    = 0.04f;
                errorMagnitude   = 0.08f;
                errorFrequency   = 0.05f;
                trackingAccuracy = 0.97f;
                break;
        }
        Debug.Log($"[RuleBasedActionSystem] Difficulty set to {difficulty}");
    }

    // ─── NEW: Apply AI Profile params from backend ────────────────────────────

    /// <summary>
    /// Called by PaddleOpponent.ApplyAIProfile() with server-assigned values.
    /// reactionSpeed: 0.0 (slow) → 1.0 (instant)
    /// Maps to reactionDelay: speed=1.0 → delay=0.02s, speed=0.2 → delay=0.40s
    /// </summary>
    public void SetReactionSpeed(float reactionSpeed)
    {
        reactionSpeed = Mathf.Clamp01(reactionSpeed);
        reactionDelay = Mathf.Lerp(0.40f, 0.02f, reactionSpeed);
        Debug.Log($"[RuleBasedActionSystem] ReactionSpeed={reactionSpeed:F2} → delay={reactionDelay:F3}s");
    }

    /// <summary>
    /// Called by PaddleOpponent.ApplyAIProfile() with server-assigned values.
    /// errorMargin: 0.0 (perfect) → 1.0 (very noisy)
    /// Maps directly to errorMagnitude and errorFrequency.
    /// </summary>
    public void SetErrorMargin(float errorMargin)
    {
        errorMargin      = Mathf.Clamp01(errorMargin);
        errorMagnitude   = Mathf.Lerp(0.05f, 0.8f, errorMargin);
        errorFrequency   = Mathf.Lerp(0.02f, 0.4f, errorMargin);
        trackingAccuracy = Mathf.Lerp(0.98f, 0.65f, errorMargin);
        Debug.Log($"[RuleBasedActionSystem] ErrorMargin={errorMargin:F2} → " +
                  $"magnitude={errorMagnitude:F2}, freq={errorFrequency:F2}");
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public void UpdatePuckState(Vector2 puckPosition, Vector2 puckVelocity)
    {
        if (Time.time - lastUpdateTime >= reactionDelay)
        {
            delayedPuckPosition = puckPosition;
            delayedPuckVelocity = puckVelocity;
            lastUpdateTime      = Time.time;
        }
    }

    public Vector2 GetPerceivedPuckPosition()
    {
        RefreshError();
        return delayedPuckPosition + currentError;
    }

    public Vector2 GetPerceivedPuckVelocity() => delayedPuckVelocity;

    public Vector2 GetGoalGuardPosition(Vector2 paddlePos)
    {
        float guardX  = Mathf.Clamp(delayedPuckPosition.x, goalMinX, goalMaxX);
        float centreX = (goalMinX + goalMaxX) * 0.5f;
        guardX = Mathf.Lerp(centreX, guardX, trackingAccuracy);

        RefreshError();
        guardX += currentError.x * 0.4f;

        return new Vector2(guardX, goalGuardY);
    }

    public Vector2 GetTrackingPosition(Vector2 desiredTarget)
    {
        RefreshError();
        return desiredTarget + currentError;
    }

    public bool IsImmediateThreat(Vector2 puckPos, Vector2 puckVel, float threatYThreshold)
    {
        bool headingToAIGoal = puckVel.y > 0;
        bool aboveThreatLine  = puckPos.y > threatYThreshold;
        bool nearGoalWidth    = Mathf.Abs(puckPos.x) <= (goalMaxX - goalMinX) * 0.5f + 0.5f;
        return headingToAIGoal && aboveThreatLine && nearGoalWidth;
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void RefreshError()
    {
        if (Time.time < nextErrorChange) return;

        nextErrorChange = Time.time + errorChangePeriod;

        if (Random.value < errorFrequency)
        {
            float angle  = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = Random.Range(0f, errorMagnitude);
            currentError = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
        else
        {
            currentError = Vector2.Lerp(currentError, Vector2.zero, 0.5f);
        }
    }
}