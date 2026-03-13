using UnityEngine;

public class PaddleController : MonoBehaviour
{
    private Rigidbody2D rb;
    private Vector2 lastPosition;
    private Vector2 paddleVelocity;
    private Vector2 targetPosition;
    private Camera mainCamera;
    private bool isDragging = false;
    private Vector2 touchOffset;
    private int activeTouchId = -1;
    private Vector2 lastDragPosition;
    private Vector2 dragVelocity;
    private Vector2 dragStartPosition;
    
    [Header("Paddle Settings")]
    [SerializeField] private float forceMultiplier = 3f;
    [SerializeField] private float maxHitForce = 60f;
    [SerializeField] private float minHitForce = 3f;
    
    [Header("Distance-Based Force Scaling")]
    [SerializeField] private float minDragDistance = 0.5f;
    [SerializeField] private float maxDragDistance = 3.5f;
    
    [Header("Flick Settings")]
    [SerializeField] private float flickMultiplier = 1.8f;
    [SerializeField] private float maxFlickForce = 30f;
    [SerializeField] private float flickDamping = 0.65f;
    
    [Header("Movement Constraints")]
    [SerializeField] private float minY = -6.75f;
    [SerializeField] private float maxY = -1f;
    [SerializeField] private float minX = -2.75f;
    [SerializeField] private float maxX = 2.75f;
    
    [Header("Physics Material")]
    [SerializeField] private PhysicsMaterial2D paddlePhysicsMaterial;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;
        lastPosition = rb.position;
        targetPosition = rb.position;
        
        if (paddlePhysicsMaterial != null)
        {
            rb.sharedMaterial = paddlePhysicsMaterial;
        }
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.ArePaddlesFrozen())
        {
            if (isDragging)
            {
                isDragging = false;
                activeTouchId = -1;
            }
            return;
        }
        
        HandleTouchInput();
    }

    void FixedUpdate()
    {
        Vector2 currentPosition = rb.position;
        paddleVelocity = (currentPosition - lastPosition) / Time.fixedDeltaTime;
        lastPosition = currentPosition;
        
        if (isDragging)
        {
            rb.MovePosition(targetPosition);
        }
        else if (dragVelocity.magnitude > 0.15f)
        {
            Vector2 momentum = dragVelocity * flickMultiplier;
            
            momentum.x = Mathf.Clamp(momentum.x, -maxFlickForce, maxFlickForce);
            momentum.y = Mathf.Clamp(momentum.y, -maxFlickForce, maxFlickForce);
            
            rb.AddForce(momentum, ForceMode2D.Force);
            dragVelocity *= flickDamping;
        }
        else
        {
            dragVelocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Check if a screen position is in the bottom half (player control zone)
    /// </summary>
    private bool IsInPlayerControlZone(Vector2 screenPos)
    {
        return screenPos.y < Screen.height / 2f;
    }

    void HandleTouchInput()
    {
        if (Input.touchCount > 0)
        {
            Touch activeTouch = default(Touch);
            bool foundTouch = false;
            
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                
                // Check if this touch is assigned to this paddle
                if (touch.fingerId == activeTouchId)
                {
                    activeTouch = touch;
                    foundTouch = true;
                    break;
                }
                
                // New touch - check if it's in player control zone
                if (activeTouchId == -1 && touch.phase == TouchPhase.Began)
                {
                    // Only process touches in bottom half of screen (player zone)
                    if (!IsInPlayerControlZone(touch.position))
                    {
                        continue; // Skip this touch, it's for opponent
                    }
                    
                    Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(touch.position.x, touch.position.y, mainCamera.nearClipPlane));
                    Collider2D hit = Physics2D.OverlapPoint(worldPos);
                    
                    if (hit != null && hit.gameObject == gameObject)
                    {
                        // Paddle grabbed - start drag
                        activeTouchId = touch.fingerId;
                        isDragging = true;
                        dragStartPosition = worldPos;
                        touchOffset = rb.position - worldPos;
                        lastDragPosition = worldPos;
                        dragVelocity = Vector2.zero;
                        activeTouch = touch;
                        foundTouch = true;
                        Debug.Log($"Player Paddle grabbed! Touch ID: {activeTouchId}");
                        break;
                    }
                    else
                    {
                        // Paddle not hit - tap to teleport to world position
                        Vector2 constrainedPos = worldPos;
                        constrainedPos.x = Mathf.Clamp(constrainedPos.x, minX, maxX);
                        constrainedPos.y = Mathf.Clamp(constrainedPos.y, minY, maxY);
                        
                        rb.MovePosition(constrainedPos);
                        targetPosition = constrainedPos;
                        dragStartPosition = constrainedPos;
                        
                        Debug.Log($"Player Paddle teleported to: {constrainedPos}");
                    }
                }
            }
            
            // Process active touch if found
            if (foundTouch && isDragging)
            {
                if (activeTouch.phase == TouchPhase.Ended || activeTouch.phase == TouchPhase.Canceled)
                {
                    Debug.Log($"Player Paddle released! Touch ID: {activeTouchId}");
                    isDragging = false;
                    activeTouchId = -1;
                    Debug.Log($"Flick velocity: {dragVelocity}");
                }
                else
                {
                    Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(activeTouch.position.x, activeTouch.position.y, mainCamera.nearClipPlane));
                    targetPosition = worldPos + touchOffset;
                    
                    dragVelocity = (worldPos - lastDragPosition) / Time.deltaTime;
                    lastDragPosition = worldPos;
                    
                    // Constrain to player side (bottom half)
                    targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
                    targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
                }
            }
        }
        else
        {
            // No touches - reset drag state
            if (isDragging)
            {
                isDragging = false;
                activeTouchId = -1;
                Debug.Log($"Flick velocity: {dragVelocity}");
            }
        }
        
        #if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            
            // Check if click is in player zone
            if (!IsInPlayerControlZone(mousePos))
            {
                return; // Skip if in opponent zone
            }
            
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCamera.nearClipPlane));
            Collider2D hit = Physics2D.OverlapPoint(worldPos);
            
            if (hit != null && hit.gameObject == gameObject)
            {
                // Paddle grabbed - start drag
                isDragging = true;
                dragStartPosition = worldPos;
                touchOffset = rb.position - worldPos;
                lastDragPosition = worldPos;
                dragVelocity = Vector2.zero;
                Debug.Log("Player Paddle grabbed with mouse!");
            }
            else
            {
                // Paddle not hit - tap to teleport to world position
                Vector2 constrainedPos = worldPos;
                constrainedPos.x = Mathf.Clamp(constrainedPos.x, minX, maxX);
                constrainedPos.y = Mathf.Clamp(constrainedPos.y, minY, maxY);
                
                rb.MovePosition(constrainedPos);
                targetPosition = constrainedPos;
                dragStartPosition = constrainedPos;
                
                Debug.Log($"Player Paddle teleported to: {constrainedPos}");
            }
        }
        
        if (Input.GetMouseButton(0) && isDragging)
        {
            Vector2 mousePos = Input.mousePosition;
            
            // Check if still in player zone
            if (!IsInPlayerControlZone(mousePos))
            {
                isDragging = false;
                activeTouchId = -1;
                return;
            }
            
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCamera.nearClipPlane));
            targetPosition = worldPos + touchOffset;
            
            dragVelocity = (worldPos - lastDragPosition) / Time.deltaTime;
            lastDragPosition = worldPos;
            
            // Constrain to player side (bottom half)
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
        }
        
        if (Input.GetMouseButtonUp(0))
        {
            if (isDragging)
            {
                Debug.Log("Player Paddle released with mouse!");
                Debug.Log($"Flick velocity: {dragVelocity}");
                isDragging = false;
            }
        }
        #endif
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Puck"))
        {
            Rigidbody2D puckRb = collision.gameObject.GetComponent<Rigidbody2D>();
            
            if (puckRb != null)
            {
                Vector2 hitDirection = (puckRb.position - rb.position).normalized;
                
                // Distance-based force scaling
                float dragDistance = Vector2.Distance(dragStartPosition, rb.position);
                float distanceScale = Mathf.Clamp01(dragDistance / maxDragDistance);
                
                float velocityMagnitude = paddleVelocity.magnitude;
                float baseForceMagnitude = Mathf.Clamp(velocityMagnitude * forceMultiplier, minHitForce, maxHitForce);
                
                // Apply distance scaling: 40% to 100% of force based on drag distance
                float forceMagnitude = baseForceMagnitude * (0.4f + distanceScale * 0.6f);
                
                Vector2 forceDirection = (hitDirection * 0.7f + paddleVelocity.normalized * 0.3f).normalized;
                
                float paddleMomentum = rb.mass * velocityMagnitude;
                float impactAbsorptionFactor = Mathf.Clamp01(paddleMomentum / 10f);
                
                Vector2 force = forceDirection * forceMagnitude;
                puckRb.linearVelocity = Vector2.zero;
                
                Vector2 finalForce = force * (0.5f + impactAbsorptionFactor * 0.5f);
                puckRb.AddForce(finalForce, ForceMode2D.Impulse);
                
                Debug.Log($"Player hit puck - Distance: {dragDistance:F2}, Scale: {distanceScale:F2}, Velocity: {velocityMagnitude:F2}, Force: {forceMagnitude:F2}, Final: {finalForce.magnitude:F2}");
            }
        }
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Puck") && !isDragging)
        {
            Rigidbody2D puckRb = collision.gameObject.GetComponent<Rigidbody2D>();
            
            if (puckRb != null)
            {
                float paddleMomentum = rb.mass * paddleVelocity.magnitude;
                float impactAbsorptionFactor = Mathf.Clamp01(paddleMomentum / 10f);
                
                if (impactAbsorptionFactor < 0.3f)
                {
                    puckRb.linearVelocity *= 0.98f;
                }
            }
            
            rb.linearVelocity = Vector2.zero;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(
            new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0),
            new Vector3(maxX - minX, maxY - minY, 0)
        );
        
        if (Application.isPlaying && rb != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(rb.position, rb.position + paddleVelocity * 0.1f);
        }
    }
}