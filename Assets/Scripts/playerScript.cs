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
    public float platformSpeedCoolingRate = 3f;
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
        if ((Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) && isGrounded == true)
        {
            Jump();
        }
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
        {
            rb.linearVelocityX = -moveSpeed;
        }
        else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
        {
            rb.linearVelocityX = moveSpeed;
        }
        else
        {
            rb.linearVelocityX = 0;
        }

        /*float distance = Vector3.Distance(
            platform1.transform.position,
            platform2.transform.position
        );
        Debug.Log("Distance: " + distance);*/
    }



    void OnCollisionEnter2D(Collision2D collision)
    {
        // SprintBlocker triggers on contact from any direction.
        if (collision.gameObject.CompareTag("SprintBlocker"))
        {
            if (spawnerController != null)
            {
                spawnerController.TriggerPlatformSpeedBoost();
            }

            return;
        }

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
                transform.position.y >= 3.0f &&
                spawnerController != null)
            {
                spawnerController.TriggerPlatformSpeedBoost();
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

        int jumpCount = Random.Range(1, 3);
        // Reset to default speed before applying new multiplier
        //jumpCount++;

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
        }
    }

}
