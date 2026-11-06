using UnityEngine;

/// <summary>
/// Smooth 2D character animation controller for GitJumps.
/// Drives Idle, Jump, Falling, and Landing visual poses based on player movement state
/// without modifying player physics, colliders, or gameplay mechanics.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationController : MonoBehaviour
{
    private playerScript player;
    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sr;
    private bool wasGrounded = true;

    [Header("Poses")]
    public Sprite idleSprite;
    public Sprite idleBreatheSprite;
    public Sprite jumpSprite;
    public Sprite fallSprite;
    public Sprite landSprite;

    [Header("Timing")]
    public float landDuration = 0.14f;
    public float idleCycleDuration = 0.8f;

    private float landTimer = 0f;
    private float idleTimer = 0f;
    private bool idleBreatheState = false;

    void Awake()
    {
        player = GetComponent<playerScript>();
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        bool isGrounded = player != null ? player.isGrounded : true;
        float velY = rb != null ? rb.linearVelocity.y : 0f;

        // Detect landing impact when transitioning from airborne to grounded
        if (!wasGrounded && isGrounded && velY <= 0.2f)
        {
            landTimer = landDuration;
            if (anim != null)
            {
                anim.SetTrigger("Land");
            }
        }

        // Animator state machine update
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.SetBool("IsGrounded", isGrounded);
            anim.SetFloat("VelocityY", velY);
        }
        else if (sr != null)
        {
            // Direct Sprite Animation fallback (smooth frame updates)
            if (landTimer > 0f)
            {
                landTimer -= Time.deltaTime;
                if (landSprite != null) sr.sprite = landSprite;
            }
            else if (!isGrounded)
            {
                if (velY > 0.5f)
                {
                    if (jumpSprite != null) sr.sprite = jumpSprite;
                }
                else
                {
                    if (fallSprite != null) sr.sprite = fallSprite;
                }
            }
            else
            {
                // Idle breathing & gentle tail motion loop
                idleTimer += Time.deltaTime;
                if (idleTimer >= idleCycleDuration)
                {
                    idleTimer = 0f;
                    idleBreatheState = !idleBreatheState;
                }

                if (idleBreatheState && idleBreatheSprite != null)
                {
                    sr.sprite = idleBreatheSprite;
                }
                else if (idleSprite != null)
                {
                    sr.sprite = idleSprite;
                }
            }
        }

        wasGrounded = isGrounded;
    }
}
