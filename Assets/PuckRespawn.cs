using UnityEngine;
using TMPro;

public class PuckRespawn : MonoBehaviour
{
    private Rigidbody2D rb;
    
    [Header("Boundary Settings")]
    [SerializeField] private float maxX = 3f;          // Maximum X boundary
    [SerializeField] private float maxY = 8f;          // Maximum Y boundary (top)
    [SerializeField] private float minX = -3f;         // Minimum X boundary
    [SerializeField] private float minY = -8f;         // Minimum Y boundary (bottom)
    
    [Header("Goal Zone Settings")]
    [SerializeField] private float goalMinX = -1f;     // Left edge of goal
    [SerializeField] private float goalMaxX = 1f;      // Right edge of goal
    [SerializeField] private float topGoalY = 7f;      // Y position of top goal
    [SerializeField] private float bottomGoalY = -7f;  // Y position of bottom goal
    [SerializeField] private float goalTolerance = 0.5f; // How close to goal Y to ignore
    
    [Header("Respawn Settings")]
    [SerializeField] private Vector2 respawnPosition = Vector2.zero;  // Default center
    [SerializeField] private bool useGameManagerSpawn = true;          // Use GameManager's spawn logic
    [SerializeField] private float outMessageDuration = 1f;            // How long to show "OUT" message
    
    [Header("UI References")]
    [SerializeField] private GameObject outBoundPanel;  // The panel to show
    [SerializeField] private TMP_Text outBoundText;     // The "OUT" text
    
    [Header("Detection Settings")]
    [SerializeField] private float checkInterval = 0.1f;  // How often to check (seconds)
    private float nextCheckTime = 0f;
    
    // Track if puck went through a goal (set by GoalZone triggers)
    private bool justScoredGoal = false;
    
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
        if (rb == null)
        {
            Debug.LogError("PuckRespawn: Rigidbody2D component not found on Puck!");
        }
        
        // Hide the out bound panel at start
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
    }
    
    void Update()
    {
        // Check boundaries periodically for better performance
        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            CheckBoundaries();
        }
    }
    
    void OnTriggerEnter2D(Collider2D other)
    {
        // Detect collision with wall colliders (physics tunneling detection)
        string wallName = other.gameObject.name;
        
        if (wallName.Contains("Wall_"))
        {
            Debug.Log($"Puck hit wall: {wallName}");
            
            // Check if this is a goal-related collision
            if (IsNearGoal(transform.position))
            {
                Debug.Log("Puck near goal - no OUT message");
                // Don't show OUT message, goal zone will handle scoring
                justScoredGoal = true;
                Invoke("ResetGoalFlag", 0.5f); // Reset flag after short delay
            }
            else
            {
                Debug.LogWarning($"Puck went out through {wallName}! Showing OUT message");
                ShowOutMessage();
            }
        }
        
        // Detect if puck entered a goal zone
        if (other.gameObject.name.Contains("Goal"))
        {
            Debug.Log($"Puck entered goal zone: {other.gameObject.name}");
            justScoredGoal = true;
            Invoke("ResetGoalFlag", 1f); // Reset flag after goal handling
        }
    }
    
    void ResetGoalFlag()
    {
        justScoredGoal = false;
        Debug.Log("Goal flag reset");
    }
    
    void CheckBoundaries()
    {
        Vector2 position = transform.position;
        
        // Check if puck is outside play area
        bool isOutOfBounds = position.x > maxX || position.x < minX || 
                            position.y > maxY || position.y < minY;
        
        if (isOutOfBounds)
        {
            // Check if puck just scored
            if (justScoredGoal)
            {
                Debug.Log("Puck out of bounds but scored - no OUT message, silent respawn");
                RespawnPuckSilent();
                return;
            }
            
            // Check if puck went through a goal zone
            bool wentThroughGoal = IsInGoalZone(position);
            
            if (wentThroughGoal)
            {
                // Puck went through goal - don't show OUT message
                Debug.Log($"Puck went through goal at ({position.x:F2}, {position.y:F2}) - No OUT message");
                justScoredGoal = true;
                RespawnPuckSilent();
            }
            else
            {
                // Puck went out of bounds through sides/corners - show OUT message
                Debug.LogWarning($"Puck out of bounds at ({position.x:F2}, {position.y:F2})! Showing OUT message");
                ShowOutMessage();
            }
        }
    }
    
    bool IsInGoalZone(Vector2 position)
    {
        // Check if puck is in the X range of goals
        bool inGoalXRange = position.x >= goalMinX && position.x <= goalMaxX;
        
        // Check if puck is near top goal
        bool nearTopGoal = Mathf.Abs(position.y - topGoalY) < goalTolerance;
        
        // Check if puck is near bottom goal
        bool nearBottomGoal = Mathf.Abs(position.y - bottomGoalY) < goalTolerance;
        
        // Return true if puck is in goal zone
        return inGoalXRange && (nearTopGoal || nearBottomGoal);
    }
    
    bool IsNearGoal(Vector2 position)
    {
        // More lenient check for wall collisions near goals
        bool inGoalXRange = position.x >= (goalMinX - 0.5f) && position.x <= (goalMaxX + 0.5f);
        bool nearTopGoal = Mathf.Abs(position.y - topGoalY) < (goalTolerance + 1f);
        bool nearBottomGoal = Mathf.Abs(position.y - bottomGoalY) < (goalTolerance + 1f);
        
        return inGoalXRange && (nearTopGoal || nearBottomGoal);
    }
    
    void ShowOutMessage()
    {
        // Don't show OUT if puck just scored
        if (justScoredGoal)
        {
            Debug.Log("Skipping OUT message - puck scored");
            RespawnPuckSilent();
            return;
        }
        
        // Show the OUT panel
        if (outBoundPanel != null && outBoundText != null)
        {
            outBoundPanel.SetActive(true);
            outBoundText.text = "OUT!";
            Debug.Log("Displaying OUT message");
            
            // Schedule the respawn after the message duration
            Invoke("RespawnPuck", outMessageDuration);
        }
        else
        {
            // If no UI is assigned, respawn immediately
            Debug.LogWarning("OutBoundPanel or OutBoundText not assigned! Respawning immediately.");
            RespawnPuck();
        }
    }
    
    void RespawnPuckSilent()
    {
        // Cancel any pending OUT message
        CancelInvoke("RespawnPuck");
        
        // Hide OUT panel if showing
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
        
        // Respawn without showing OUT message (used when scoring)
        Vector2 spawnPosition = respawnPosition;
        
        // If using GameManager spawn logic and it exists
        if (useGameManagerSpawn && GameManager.Instance != null)
        {
            spawnPosition = Vector2.zero;
            Debug.Log("Puck respawned silently - GameManager will handle positioning");
        }
        else
        {
            spawnPosition = respawnPosition;
            Debug.Log($"Puck respawned silently at: {spawnPosition}");
        }
        
        // Reset position
        transform.position = spawnPosition;
        
        // Clear physics state
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
    
    void RespawnPuck()
    {
        // Hide the OUT panel
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
        
        Vector2 spawnPosition = respawnPosition;
        
        // If using GameManager spawn logic and it exists
        if (useGameManagerSpawn && GameManager.Instance != null)
        {
            // Let GameManager handle the positioning (it tracks who scored)
            // We just reset to center and let GameManager reposition if needed
            spawnPosition = Vector2.zero;
            Debug.Log("Puck respawned - GameManager will handle positioning");
        }
        else
        {
            // Use manual respawn position
            spawnPosition = respawnPosition;
            Debug.Log($"Puck respawned at manual position: {spawnPosition}");
        }
        
        // Reset position
        transform.position = spawnPosition;
        
        // Clear physics state
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        
        // Reset goal flag
        justScoredGoal = false;
    }
    
    // Manual respawn method that can be called from other scripts
    public void ForceRespawn()
    {
        Debug.Log("Force respawn triggered!");
        
        // Cancel any pending respawn
        CancelInvoke("RespawnPuck");
        CancelInvoke("ResetGoalFlag");
        
        // Hide panel and respawn immediately
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
        
        justScoredGoal = false;
        RespawnPuck();
    }
    
    // Manual respawn to specific position
    public void ForceRespawn(Vector2 position)
    {
        Debug.Log($"Force respawn to position: {position}");
        
        // Cancel any pending respawn
        CancelInvoke("RespawnPuck");
        CancelInvoke("ResetGoalFlag");
        
        // Hide panel
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
        
        transform.position = position;
        
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        
        justScoredGoal = false;
    }
    
    // Public method to mark that a goal was scored (call from GoalZone)
    public void MarkGoalScored()
    {
        justScoredGoal = true;
        CancelInvoke("ResetGoalFlag");
        Invoke("ResetGoalFlag", 1f);
        Debug.Log("Goal scored flag set by GoalZone");
    }
    
    // Visual boundary display in editor
    void OnDrawGizmosSelected()
    {
        // Draw boundary rectangle
        Gizmos.color = Color.red;
        
        Vector3 topLeft = new Vector3(minX, maxY, 0);
        Vector3 topRight = new Vector3(maxX, maxY, 0);
        Vector3 bottomLeft = new Vector3(minX, minY, 0);
        Vector3 bottomRight = new Vector3(maxX, minY, 0);
        
        // Draw boundary lines
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);
        
        // Draw respawn position
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(respawnPosition, 0.2f);
        
        // Draw goal zones
        Gizmos.color = Color.yellow;
        
        // Top goal zone
        Vector3 topGoalLeft = new Vector3(goalMinX, topGoalY, 0);
        Vector3 topGoalRight = new Vector3(goalMaxX, topGoalY, 0);
        Gizmos.DrawLine(topGoalLeft, topGoalRight);
        Gizmos.DrawWireCube(new Vector3((goalMinX + goalMaxX) / 2f, topGoalY, 0), 
                           new Vector3(goalMaxX - goalMinX, goalTolerance * 2, 0));
        
        // Bottom goal zone
        Vector3 bottomGoalLeft = new Vector3(goalMinX, bottomGoalY, 0);
        Vector3 bottomGoalRight = new Vector3(goalMaxX, bottomGoalY, 0);
        Gizmos.DrawLine(bottomGoalLeft, bottomGoalRight);
        Gizmos.DrawWireCube(new Vector3((goalMinX + goalMaxX) / 2f, bottomGoalY, 0), 
                           new Vector3(goalMaxX - goalMinX, goalTolerance * 2, 0));
    }
}