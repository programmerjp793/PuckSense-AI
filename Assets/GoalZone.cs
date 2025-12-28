using UnityEngine;

public class GoalZone : MonoBehaviour
{
    [Header("Goal Settings")]
    [SerializeField] private bool isPlayerGoal = false;    // True = Player's goal (bottom) - Opponent scores here
    [SerializeField] private bool isOpponentGoal = false;  // True = Opponent's goal (top) - Player scores here
    
    void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"GoalZone triggered by: {other.gameObject.name} with tag: {other.tag}");
        
        // Check if puck entered the goal
        if (other.CompareTag("Puck"))
        {
            Debug.Log($"Puck detected! isPlayerGoal: {isPlayerGoal}, isOpponentGoal: {isOpponentGoal}");
            
            if (GameManager.Instance != null)
            {
                if (isPlayerGoal)
                {
                    // This is the BOTTOM goal (Player's goal)
                    // Puck entered player's goal = Opponent (Paddle 2 - Top) scores
                    // Updates OpponentScoreBackground at the top
                    Debug.Log("Goal at BOTTOM! Calling OpponentScored()");
                    GameManager.Instance.OpponentScored();
                }
                
                if (isOpponentGoal)
                {
                    // This is the TOP goal (Opponent's goal)
                    // Puck entered opponent's goal = Player (Paddle 1 - Bottom) scores
                    // Updates PlayerScoreBackground at the bottom
                    Debug.Log("Goal at TOP! Calling PlayerScored()");
                    GameManager.Instance.PlayerScored();
                }
                
                // Warning if neither flag is set
                if (!isPlayerGoal && !isOpponentGoal)
                {
                    Debug.LogWarning($"GoalZone '{gameObject.name}': Neither isPlayerGoal nor isOpponentGoal is set! Please configure this goal zone in the Inspector.");
                }
            }
            else
            {
                Debug.LogError("GoalZone: GameManager.Instance is NULL! Make sure GameManager exists in the scene.");
            }
        }
        else
        {
            Debug.Log($"Not a puck. Object: {other.gameObject.name}, Tag: {other.tag}");
        }
    }
}