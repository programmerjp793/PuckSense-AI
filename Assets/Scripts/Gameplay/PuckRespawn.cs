using UnityEngine;
using TMPro;

public class PuckRespawn : MonoBehaviour
{
    private Rigidbody2D rb;
    private GameManager gameManager;
    private Vector2 lastFramePosition;
    
    [Header("Boundary Settings")]
    [SerializeField] private float maxX = 4.75f;       // Maximum X boundary (Wall_Right collider position)
    [SerializeField] private float maxY = 8f;          // Maximum Y boundary (top)
    [SerializeField] private float minX = -4.75f;      // Minimum X boundary (Wall_Left collider position)
    [SerializeField] private float minY = -8f;         // Minimum Y boundary (bottom)
    
    [Header("Wall Collision Detection (Physics Tunneling)")]
    [SerializeField] private float wallLeftX = -4.75f;  // Wall_Left X position
    [SerializeField] private float wallRightX = 4.75f;  // Wall_Right X position
    [SerializeField] private float wallColliderThickness = 0.5f;  // Thickness of wall colliders for tunneling detection
    
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
    
    // Track if puck is in a goal zone (set by OnTriggerEnter2D with goal colliders)
    private bool isInGoalZone = false;
    private bool justScoredGoal = false;
    
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        gameManager = GameManager.Instance;
        lastFramePosition = transform.position;
        
        if (rb == null)
        {
            Debug.LogError("PuckRespawn: Rigidbody2D component not found on Puck!");
        }
        
        if (gameManager == null)
        {
            Debug.LogError("PuckRespawn: GameManager.Instance not found!");
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
            DetectPhysicsTunneling();
        }
        
        // Update last frame position at end of frame
        lastFramePosition = transform.position;
    }
    
    void OnTriggerEnter2D(Collider2D other)
    {
        string objectName = other.gameObject.name;
        
        // ===== GOAL ZONE DETECTION (Box Collider 2D with "Goal" in name) =====
        if (objectName.Contains("Goal"))
        {
            Debug.Log($"⭐ PUCK ENTERED GOAL ZONE: {objectName} - This is a GOAL, not an OUT");
            isInGoalZone = true;
            justScoredGoal = true;
            
            // Cancel any pending OUT message
            CancelInvoke("RespawnPuck");
            
            // Don't show OUT message at all
            if (outBoundPanel != null)
            {
                outBoundPanel.SetActive(false);
            }
            
            // Schedule flag reset
            CancelInvoke("ResetGoalFlag");
            Invoke("ResetGoalFlag", 2f);
            
            return; // EXIT - Don't process wall logic
        }
        
        // ===== WALL COLLISION DETECTION =====
        if (objectName.Contains("Wall_"))
        {
            Debug.Log($"Puck hit wall: {objectName}");
            
            // If puck is currently in a goal zone, don't show OUT message
            if (isInGoalZone || justScoredGoal)
            {
                Debug.Log("Puck in goal zone - ignoring wall collision, no OUT message");
                return;
            }
            
            // Check if this is a goal-related collision by proximity
            if (IsNearGoal(transform.position))
            {
                Debug.Log("Puck near goal area - no OUT message");
                justScoredGoal = true;
                Invoke("ResetGoalFlag", 0.5f);
                return;
            }
            
            // Not near goal - show OUT message
            Debug.LogWarning($"Puck went out through {objectName}! Showing OUT message");
            ShowOutMessage();
        }
    }
    
    void OnTriggerExit2D(Collider2D other)
    {
        // When puck exits goal zone collider
        if (other.gameObject.name.Contains("Goal"))
        {
            Debug.Log($"Puck exited goal zone: {other.gameObject.name}");
            isInGoalZone = false;
        }
    }
    
    void ResetGoalFlag()
    {
        justScoredGoal = false;
        isInGoalZone = false;
        Debug.Log("Goal flag reset - puck no longer considered in goal zone");
    }
    
    /// <summary>
    /// Detects physics tunneling - when puck passes completely through wall colliders
    /// This handles cases where the puck moves too fast to register a collision
    /// Checks if puck crossed from one side of the wall to the other
    /// </summary>
    void DetectPhysicsTunneling()
    {
        Vector2 currentPos = transform.position;
        
        // Skip if puck is in goal zone
        if (isInGoalZone || justScoredGoal)
            return;
        
        // ===== CHECK IF PUCK TUNNELED THROUGH LEFT WALL =====
        // Puck was on the right side of wall (lastFramePos > wallLeft), now on the left side (currentPos < wallLeft)
        if (lastFramePosition.x > wallLeftX && currentPos.x < wallLeftX)
        {
            // Check if it's not a goal
            if (!IsNearGoal(currentPos))
            {
                Debug.LogWarning($"⚠️ PHYSICS TUNNELING DETECTED: Puck passed through LEFT WALL at ({currentPos.x:F2}, {currentPos.y:F2})");
                ShowOutMessage();
                return;
            }
            else
            {
                Debug.Log($"Puck tunneled through LEFT WALL but in goal zone, no OUT message");
            }
        }
        
        // ===== CHECK IF PUCK TUNNELED THROUGH RIGHT WALL =====
        // Puck was on the left side of wall (lastFramePos < wallRight), now on the right side (currentPos > wallRight)
        if (lastFramePosition.x < wallRightX && currentPos.x > wallRightX)
        {
            // Check if it's not a goal
            if (!IsNearGoal(currentPos))
            {
                Debug.LogWarning($"⚠️ PHYSICS TUNNELING DETECTED: Puck passed through RIGHT WALL at ({currentPos.x:F2}, {currentPos.y:F2})");
                ShowOutMessage();
                return;
            }
            else
            {
                Debug.Log($"Puck tunneled through RIGHT WALL but in goal zone, no OUT message");
            }
        }
    }
    
    void CheckBoundaries()
    {
        Vector2 position = transform.position;
        
        // Check if puck is outside play area (beyond the walls)
        bool isOutOfBounds = position.x > wallRightX + wallColliderThickness || 
                            position.x < wallLeftX - wallColliderThickness || 
                            position.y > maxY || 
                            position.y < minY;
        
        if (isOutOfBounds)
        {
            // ===== IF IN GOAL ZONE - DO NOT RESPAWN =====
            if (isInGoalZone || justScoredGoal)
            {
                Debug.Log("⭐ Puck out of bounds BUT in goal zone - NOT respawning, GameManager will handle");
                return; // Exit without respawning - GameManager handles puck positioning
            }
            
            // Check if puck went through a goal zone boundary
            bool wentThroughGoal = IsInGoalZone(position);
            
            if (wentThroughGoal)
            {
                // Puck went through goal - don't show OUT message
                Debug.Log($"⭐ Puck went through goal zone at ({position.x:F2}, {position.y:F2}) - No OUT message");
                isInGoalZone = true;
                justScoredGoal = true;
                Invoke("ResetGoalFlag", 2f);
                return; // Don't respawn - GameManager handles it
            }
            
            // ===== PUCK OUT OF BOUNDS (NOT IN GOAL) - SHOW OUT MESSAGE =====
            Debug.LogWarning($"Puck out of bounds at ({position.x:F2}, {position.y:F2})! Showing OUT message");
            ShowOutMessage();
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
        // Don't show OUT if puck is in goal zone
        if (isInGoalZone || justScoredGoal)
        {
            Debug.Log("Skipping OUT message - puck in goal zone");
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
    
    void RespawnPuck()
    {
        // Hide the OUT panel
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
        
        // ===== IF PUCK SCORED A GOAL - DO NOT RESPAWN HERE =====
        if (justScoredGoal || isInGoalZone)
        {
            Debug.Log("⭐ GOAL SCORED - Not respawning at center. GameManager will handle puck positioning via ResetPositions()");
            return;
        }
        
        // ===== REGULAR OUT OF BOUNDS RESPAWN =====
        Vector2 spawnPosition = respawnPosition;
        
        // If using GameManager spawn logic and it exists
        if (useGameManagerSpawn && gameManager != null)
        {
            Debug.Log("Puck out of bounds (not a goal) - calling GameManager.ResetPaddles()");
            gameManager.ResetPaddles();
            spawnPosition = Vector2.zero;
        }
        else
        {
            spawnPosition = respawnPosition;
            Debug.Log($"Puck respawned at: {spawnPosition}");
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
    
    // Manual respawn method that can be called from other scripts
    public void ForceRespawn()
    {
        Debug.Log("Force respawn triggered!");
        
        // Cancel any pending respawn
        CancelInvoke("RespawnPuck");
        CancelInvoke("ResetGoalFlag");
        
        // Hide panel
        if (outBoundPanel != null)
        {
            outBoundPanel.SetActive(false);
        }
        
        justScoredGoal = false;
        isInGoalZone = false;
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
        isInGoalZone = false;
    }
    
    // Public method to be called by GoalZone when goal is detected
    public void OnGoalDetected()
    {
        Debug.Log("⭐ OnGoalDetected() called - marking puck in goal zone");
        isInGoalZone = true;
        justScoredGoal = true;
        CancelInvoke("ResetGoalFlag");
        Invoke("ResetGoalFlag", 2f);
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
        
        // Draw wall collision zones (for tunneling detection)
        Gizmos.color = Color.cyan;
        
        // Left wall tunneling detection line
        Vector3 leftWallTop = new Vector3(wallLeftX, maxY, 0);
        Vector3 leftWallBottom = new Vector3(wallLeftX, minY, 0);
        Gizmos.DrawLine(leftWallTop, leftWallBottom);
        Gizmos.DrawWireCube(new Vector3(wallLeftX, 0, 0), new Vector3(wallColliderThickness * 2, maxY - minY, 1));
        
        // Right wall tunneling detection line
        Vector3 rightWallTop = new Vector3(wallRightX, maxY, 0);
        Vector3 rightWallBottom = new Vector3(wallRightX, minY, 0);
        Gizmos.DrawLine(rightWallTop, rightWallBottom);
        Gizmos.DrawWireCube(new Vector3(wallRightX, 0, 0), new Vector3(wallColliderThickness * 2, maxY - minY, 1));
        
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