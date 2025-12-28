using UnityEngine;

public class PuckController : MonoBehaviour
{
    private Rigidbody2D rb;
    
    [Header("Puck Settings")]
    [SerializeField] private float maxSpeed = 15f;
    [SerializeField] private float minSpeed = 0.1f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
        // Make sure puck starts completely still
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
    }

    void FixedUpdate()
    {
        // Limit maximum speed
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }

        // Stop puck if moving too slowly
        if (rb.linearVelocity.magnitude < minSpeed)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}