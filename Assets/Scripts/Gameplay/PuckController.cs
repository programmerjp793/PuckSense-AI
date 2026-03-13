using UnityEngine;

public class PuckController : MonoBehaviour
{
    private Rigidbody2D rb;
    
    [Header("Puck Settings")]
    [SerializeField] private float maxSpeed = 80f;
    [SerializeField] private float minSpeed = 0.5f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
    }

    void FixedUpdate()
    {
        float currentSpeed = rb.linearVelocity.magnitude;
        
        // Clamp maximum speed for smooth, fast gameplay
        if (currentSpeed > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }

        // Stop puck if moving too slowly
        if (currentSpeed > 0 && currentSpeed < minSpeed)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}