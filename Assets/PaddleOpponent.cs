using UnityEngine;

public class PaddleOpponent : MonoBehaviour
{
    private Rigidbody2D rb;
    private Vector2 lastPosition;
    private Vector2 paddleVelocity;
    private Camera mainCamera;
    private bool isDragging = false;
    private Vector2 touchOffset;
    private int activeTouchId = -1;
    
    [Header("Paddle Settings")]
    [SerializeField] private float forceMultiplier = 2f;
    [SerializeField] private float maxHitForce = 20f;
    [SerializeField] private float minHitForce = 2f;
    
    [Header("Movement Constraints - Opponent Side")]
    [SerializeField] private float minY = 1f;      // Opposite of Paddle 1
    [SerializeField] private float maxY = 7f;      // Top half of table
    [SerializeField] private float minX = -2.75f;
    [SerializeField] private float maxX = 2.75f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;
        lastPosition = transform.position;
    }

    void Update()
    {
        HandleTouchInput();
    }

    void FixedUpdate()
    {
        // Calculate paddle velocity for realistic collisions
        Vector2 currentPosition = transform.position;
        paddleVelocity = (currentPosition - lastPosition) / Time.fixedDeltaTime;
        lastPosition = currentPosition;
    }

    void HandleTouchInput()
    {
        // Handle touch input for mobile devices
        if (Input.touchCount > 0)
        {
            Touch activeTouch = default(Touch);
            bool foundTouch = false;
            
            // Find the active touch for this paddle
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                
                if (touch.fingerId == activeTouchId)
                {
                    activeTouch = touch;
                    foundTouch = true;
                    break;
                }
                
                // If no active touch and this touch just began, check if it's on the paddle
                if (activeTouchId == -1 && touch.phase == TouchPhase.Began)
                {
                    Vector2 touchPosition = touch.position;
                    Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(touchPosition.x, touchPosition.y, mainCamera.nearClipPlane));
                    Collider2D hit = Physics2D.OverlapPoint(worldPos);
                    
                    if (hit != null && hit.gameObject == gameObject)
                    {
                        activeTouchId = touch.fingerId;
                        isDragging = true;
                        touchOffset = (Vector2)transform.position - worldPos;
                        activeTouch = touch;
                        foundTouch = true;
                        Debug.Log("Opponent paddle grabbed!");
                        break;
                    }
                }
            }
            
            // Process the active touch
            if (foundTouch && isDragging)
            {
                if (activeTouch.phase == TouchPhase.Ended || activeTouch.phase == TouchPhase.Canceled)
                {
                    Debug.Log("Opponent paddle released!");
                    isDragging = false;
                    activeTouchId = -1;
                }
                else
                {
                    // Move paddle immediately with touch position
                    Vector2 touchPosition = activeTouch.position;
                    Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(touchPosition.x, touchPosition.y, mainCamera.nearClipPlane));
                    Vector2 targetPosition = worldPos + touchOffset;
                    
                    // Constrain movement to opponent's side (top half)
                    targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
                    targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
                    
                    // Directly set position for instant, accurate response
                    rb.MovePosition(targetPosition);
                }
            }
        }
        else
        {
            // If no touches, reset
            if (isDragging)
            {
                isDragging = false;
                activeTouchId = -1;
            }
        }
        
        // Fallback to mouse input for testing in Unity Editor
        #if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCamera.nearClipPlane));
            Collider2D hit = Physics2D.OverlapPoint(worldPos);
            
            if (hit != null && hit.gameObject == gameObject)
            {
                isDragging = true;
                touchOffset = (Vector2)transform.position - worldPos;
                Debug.Log("Opponent paddle grabbed with mouse!");
            }
        }
        
        if (Input.GetMouseButton(0) && isDragging)
        {
            Vector2 mousePos = Input.mousePosition;
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCamera.nearClipPlane));
            Vector2 targetPosition = worldPos + touchOffset;
            
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
            
            // Directly set position for instant response
            rb.MovePosition(targetPosition);
        }
        
        if (Input.GetMouseButtonUp(0))
        {
            if (isDragging)
            {
                Debug.Log("Opponent paddle released with mouse!");
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
                // Get collision contact point
                ContactPoint2D contact = collision.contacts[0];
                
                // Calculate hit direction from paddle center to puck
                Vector2 hitDirection = (puckRb.position - rb.position).normalized;
                
                // Calculate force based on paddle velocity
                float velocityMagnitude = paddleVelocity.magnitude;
                float forceMagnitude = Mathf.Clamp(velocityMagnitude * forceMultiplier, minHitForce, maxHitForce);
                
                // Combine paddle velocity direction with hit direction for more realistic feel
                Vector2 forceDirection = (hitDirection * 0.7f + paddleVelocity.normalized * 0.3f).normalized;
                
                // Apply the force
                Vector2 force = forceDirection * forceMagnitude;
                puckRb.linearVelocity = Vector2.zero; // Reset puck velocity for clean hit
                puckRb.AddForce(force, ForceMode2D.Impulse);
                
                Debug.Log($"Opponent hit puck - Paddle velocity: {velocityMagnitude:F2}, Force applied: {forceMagnitude:F2}");
            }
        }
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        // Prevent paddle from continuously pushing the puck
        if (collision.gameObject.CompareTag("Puck") && !isDragging)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    // Visual feedback in editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan; // Different color to distinguish from player paddle
        Gizmos.DrawWireCube(
            new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0),
            new Vector3(maxX - minX, maxY - minY, 0)
        );
        
        // Draw velocity vector
        if (Application.isPlaying && rb != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)paddleVelocity * 0.1f);
        }
    }
}