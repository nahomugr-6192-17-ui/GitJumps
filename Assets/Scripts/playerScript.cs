using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class playerScript : MonoBehaviour
{
    private Rigidbody2D rb;
    public float jumpForce = 8.6f;
    public float jumpForceMultiplier = 1.1f;
    public float moveSpeed = 7f;
    public bool isGrounded = false;
    public GameObject ground;
    private GameObject camera;
    /*public GameObject platform1;
    public GameObject platform2;*/
    private HashSet<GameObject> scoredContainers = new HashSet<GameObject>();
    public int currentScore = 0;
    private int highScore = 0;
    private platformScript standingPlatform;
    public platformSpawnerScript spawnerController;
    public bool isCoolingPlatformSpeed = false;
    public float platformSpeedCoolingRate = 1f;
    [Header("Movement Smoothing")]
    public float horizontalAcceleration = 35f;
    public float horizontalDeceleration = 45f;

    [Header("Jump Control")]
    [Range(0.1f, 1f)]
    public float jumpCutMultiplier = 0.5f;

    private bool jumpWasHeld;
    public GameObject Booster;
    public GameObject Bug;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        camera = GameObject.Find("Main Camera");
        currentScore = 0;
        spawnerController = FindAnyObjectByType<platformSpawnerScript>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current == null)
            return;

        bool leftPressed =
            Keyboard.current.aKey.isPressed ||
            Keyboard.current.leftArrowKey.isPressed;

        bool rightPressed =
            Keyboard.current.dKey.isPressed ||
            Keyboard.current.rightArrowKey.isPressed;

        bool jumpPressed =
            Keyboard.current.wKey.isPressed ||
            Keyboard.current.upArrowKey.isPressed;

        bool jumpHeld =
            Keyboard.current.wKey.isPressed ||
            Keyboard.current.upArrowKey.isPressed;

        // Smooth horizontal acceleration and deceleration.
        float targetSpeed = 0f;

        if (leftPressed && !rightPressed)
            targetSpeed = -moveSpeed;
        else if (rightPressed && !leftPressed)
            targetSpeed = moveSpeed;

        float movementRate = targetSpeed == 0f
            ? horizontalDeceleration
            : horizontalAcceleration;

        rb.linearVelocityX = Mathf.MoveTowards(
            rb.linearVelocityX,
            targetSpeed,
            movementRate * Time.deltaTime
        );

        // Jump once when the jump key is pressed.
        if (jumpPressed && isGrounded)
        {
            Jump();
        }

        // Cut the jump short when the player releases jump
        // while still moving upward.
        if (jumpWasHeld && !jumpHeld && rb.linearVelocityY > 0f)
        {
            rb.linearVelocityY *= jumpCutMultiplier;
        }

        jumpWasHeld = jumpHeld;
    }



    void OnCollisionEnter2D(Collision2D collision)
    {
        // SprintBlocker triggers on contact from any direction.
        if (!collision.gameObject.CompareTag("Floor") &&
            !collision.gameObject.CompareTag("Platform"))
        {
            return;
        }

        foreach (ContactPoint2D contact in collision.contacts)
        {
            // Only count contact with the top of a surface as landing.
            if (contact.normal.y <= 0.5f)
                continue;

            isGrounded = true;

            // Boost when landing on a platform above the target height.
            if (collision.gameObject.CompareTag("Platform") &&
                transform.position.y >= 5.6f &&
                spawnerController != null && !isCoolingPlatformSpeed)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    5f,
                    0.85f
                );
            }

            if (collision.gameObject.CompareTag("Platform"))
            {
                platformScript newPlatform =
                    collision.gameObject.GetComponentInParent<platformScript>();

                if (newPlatform != null)
                {
                    if (standingPlatform != null &&
                        standingPlatform != newPlatform)
                    {
                        standingPlatform.SetPlayerStanding(false);
                    }

                    standingPlatform = newPlatform;
                    standingPlatform.player = transform;
                    standingPlatform.SetPlayerStanding(true);
                }
            }
            else
            {
                StopPlatformCarry();
            }

            break;
        }
        
        if (collision.gameObject.CompareTag("Booster"))
        {
            if (spawnerController != null)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    5f,
                    1f
                );
            }

            Destroy(GameObject.FindWithTag("Booster"));
            return;
        }

        if (collision.gameObject.CompareTag("Bug"))
        {
            Destroy(collision.gameObject);
            return;
        }
    }
    void OnCollisionExit2D(Collision2D collision)
    {

        if (collision.gameObject.CompareTag("Platform") ||
    collision.gameObject.CompareTag("Floor"))
        {
            isCoolingPlatformSpeed = true;
        }
        if (collision.gameObject.CompareTag("Floor") ||
            collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = false;
        }

        // Stop carrying only if the player left the platform
        // controlled by this particular platformScript.
        platformScript exitedPlatform =
            collision.gameObject.GetComponentInParent<platformScript>();

        if (standingPlatform != null &&
            exitedPlatform == standingPlatform)
        {
            StopPlatformCarry();
        }
    }

    private void StopPlatformCarry()
    {
        if (standingPlatform != null)
        {
            standingPlatform.SetPlayerStanding(false);
            standingPlatform = null;
        }
    }

    private void Jump()
    {
        isCoolingPlatformSpeed = false;
        StopPlatformCarry();
        float usedMultiplier = jumpForceMultiplier;
        isGrounded = false;
        rb.linearVelocityY = 0f;
        rb.AddForce(
            Vector2.up * jumpForce * usedMultiplier,
            ForceMode2D.Impulse
        );

        float probabilisticJumpCount = Random.value;
        int jumpCount = PlayerPrefs.GetInt("JumpCount", 0);

        if (probabilisticJumpCount < 0.45)
        {
            jumpCount = 1;
        }
        else if (probabilisticJumpCount < 0.8)
        {
            jumpCount = 2;
        }
        else
        {
            jumpCount = 3;
        }

        spawnerController.speedBoostDuration = 1.8f;
        // Reset to default speed before applying new multiplier
        //jumpCount++;

        if (spawnerController != null)
        {
            if (jumpCount == 3)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    5f,
                    0.5f
                );
            }
        }
        PlayerPrefs.SetInt("JumpCount", jumpCount);
    }



    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("BlockContainer"))
        {
            if (scoredContainers.Add(collision.gameObject))
            {
                currentScore++;
                /*if (currentScore > highScore)
                {
                    highScore = currentScore;
                    PlayerPrefs.SetInt("HighScore", highScore);
                }*/
                PlayerPrefs.SetInt("Score", currentScore);
                Debug.Log("Score: " + currentScore);
            }
            if (currentScore == 3)
            {
                if (spawnerController != null)
                {
                    spawnerController.TriggerPlatformSpeedBoost(
                        5f,
                        0.85f
                    );
                }
            }
        }

        if (collision.gameObject.CompareTag("SprintBlocker"))
        {
            if (spawnerController != null)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    5f,
                    0.85f
                );
            }

            return;
        }
    }

}
