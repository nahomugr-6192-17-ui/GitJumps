using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.UIElements;

public class playerScript : MonoBehaviour
{
    private Rigidbody2D rb;
    public float jumpForce = 8.6f;
    public float jumpForceMultiplier = 1.1f;
    public float moveSpeed = 7f;
    public bool isGrounded = false;
    private bool buttonsEnabled = true;
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
    [Header("Commit Boost")]
    public float commitJumpMultiplier = 3f;
    public float commitBoostDuration = 0.95f;

    private float defaultJumpForceMultiplier;
    private Coroutine commitBoostCoroutine;
    public GameObject Commit;
    public GameObject Bug;
    public bool isCommitTriggered = false;
    public bool isBugTriggered = false;
    public ParticleSystem commitParticleEffect;
    private Coroutine commitParticleCoroutine;

    [Header("Bug Hit Effect")]
    public float bugFlashDuration = 0.5f;
    public float bugFlashInterval = 0.08f;

    private SpriteRenderer playerSprite;
    private Coroutine bugFlashCoroutine;

    //private float gapBetweenBoosters = 10f;

    public bool isGameOver = false;
    [Header("Game Over")]
    public float gameOverTriggerY = -30f;
    private bool gameOverTriggerEnabled = false;
    private Collider2D playerCollider;

    [Header("Landing Disappearance Effect")]
    public float collapseFadeDuration = 0.4f;

    private HashSet<GameObject> fadingObjects = new HashSet<GameObject>();

    public UIDocument currentScoreUI;
    private Label currentScoreLabel;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        camera = GameObject.Find("Main Camera");
        currentScoreLabel = currentScoreUI.rootVisualElement.Q<Label>("currentScoreLabel");
        currentScore = 0;
        currentScoreLabel.text = "Score: " + currentScore.ToString();
        spawnerController = FindAnyObjectByType<platformSpawnerScript>();
        defaultJumpForceMultiplier = jumpForceMultiplier;
        playerSprite = GetComponent<SpriteRenderer>();
        playerCollider = GetComponent<Collider2D>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current == null)
            return;
        if (buttonsEnabled == true && !isGameOver)
        {
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

        /*float distanceBetweenBoosters = Mathf.Abs(
            platform1.transform.position.y - platform2.transform.position.y
        );
        Debug.Log("Distance between boosters: " + distanceBetweenBoosters);
        */
        GameOver();
        UpdateGameOverTrigger();
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

            if (collision.gameObject.CompareTag("Platform"))
            {
                CollapseObjectsBelow(collision.gameObject);
            }

            // Boost when landing on a platform above the target height.
            if (collision.gameObject.CompareTag("Platform") &&
                transform.position.y >= 4.6f &&
                spawnerController != null && !isCoolingPlatformSpeed)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    5f,
                    0.5f
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
    }

    private void CollapseObjectsBelow(GameObject landedPlatform)
    {
        float landingY = landedPlatform.transform.position.y;

        // Find platforms underneath the platform the player landed on.
        GameObject[] platforms =
            GameObject.FindGameObjectsWithTag("Platform");

        foreach (GameObject platform in platforms)
        {
            if (platform == landedPlatform)
                continue;

            if (platform.transform.position.y < landingY)
            {
                StartFade(platform);
            }
        }

        // Find Bugs underneath the landing platform.
        GameObject[] bugs =
            GameObject.FindGameObjectsWithTag("Bug");

        foreach (GameObject bug in bugs)
        {
            if (bug.transform.position.y < landingY)
            {
                StartFade(bug);
            }
        }

        // Find Commit/Booster objects underneath the landing platform.
        GameObject[] commits =
            GameObject.FindGameObjectsWithTag("Commit");

        foreach (GameObject commit in commits)
        {
            if (commit.transform.position.y < landingY)
            {
                StartFade(commit);
            }
        }
    }


    private void StartFade(GameObject target)
    {
        if (target == null || fadingObjects.Contains(target))
            return;

        fadingObjects.Add(target);

        StartCoroutine(FadeAndDestroy(target));
    }


    private System.Collections.IEnumerator FadeAndDestroy(GameObject target)
    {
        SpriteRenderer[] sprites =
            target.GetComponentsInChildren<SpriteRenderer>(true);

        Collider2D[] colliders =
            target.GetComponentsInChildren<Collider2D>(true);

        // Prevent interaction while the object is disappearing.
        foreach (Collider2D col in colliders)
        {
            col.enabled = false;
        }

        Color[] originalColors = new Color[sprites.Length];

        for (int i = 0; i < sprites.Length; i++)
        {
            originalColors[i] = sprites[i].color;
        }

        float elapsed = 0f;

        while (elapsed < collapseFadeDuration)
        {
            elapsed += Time.deltaTime;

            float alpha = 1f - Mathf.Clamp01(
                elapsed / collapseFadeDuration
            );

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                {
                    Color color = originalColors[i];
                    color.a = originalColors[i].a * alpha;

                    sprites[i].color = color;
                }
            }

            yield return null;
        }

        Destroy(target);
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
        isCommitTriggered = false;
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
                    4f,
                    0.45f
                );
            }

            if (currentScore <= 50 && currentScore >= 3)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    4f,
                    0.5f
                );
            }

            if (currentScore >= 1)
            {
                jumpForce = 7f;
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
                currentScoreLabel.text = "Score: " + currentScore.ToString();
            }
        }

        if (collision.gameObject.CompareTag("SprintBlocker"))
        {
            if (spawnerController != null)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    5f,
                    0.65f
                );
            }

            return;
        }

        if (collision.gameObject.CompareTag("Commit"))
        {
            isCommitTriggered = true;
            Debug.Log("Commit activated!");

            ActivateCommitBoost();
            spawnerController.TriggerPlatformSpeedBoost(
                5f,
                1f
            );

            // Start the particle trail.
            if (commitParticleEffect != null)
            {
                if (commitParticleCoroutine != null)
                {
                    StopCoroutine(commitParticleCoroutine);
                }

                commitParticleCoroutine =
                    StartCoroutine(CommitParticleTrail());
            }

            Destroy(collision.gameObject);
        }

        if (collision.gameObject.CompareTag("Bug"))
        {
            isBugTriggered = true;
            BugCollision();
            isGameOver = true;
            Destroy(Commit);
        }
    }

    private void ActivateCommitBoost()
    {
        isCoolingPlatformSpeed = true;

        if (commitBoostCoroutine != null)
        {
            StopCoroutine(commitBoostCoroutine);
        }

        // Prevent the normal jump from interfering with the boost.
        isGrounded = false;
        StopPlatformCarry();

        // Give the player a strong upward launch.
        rb.linearVelocityY = 0f;

        rb.AddForce(
            Vector2.up * jumpForce * commitJumpMultiplier,
            ForceMode2D.Impulse
        );

        // Restore the normal jump multiplier after the boost duration.
        commitBoostCoroutine = StartCoroutine(ResetCommitBoost());

    }

    private System.Collections.IEnumerator ResetCommitBoost()
    {
        yield return new WaitForSeconds(commitBoostDuration);

        jumpForceMultiplier = defaultJumpForceMultiplier;

        commitBoostCoroutine = null;
    }

    private System.Collections.IEnumerator CommitParticleTrail()
    {
        // Clear any previous particles.
        commitParticleEffect.Stop(
            false,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        // Start emitting particles.
        commitParticleEffect.Play();

        // Keep emitting while the player is moving upward.
        while (rb.linearVelocityY > 0f)
        {
            yield return null;
        }

        // Stop emitting, but allow existing particles to disappear naturally.
        commitParticleEffect.Stop(
            false,
            ParticleSystemStopBehavior.StopEmitting
        );

        commitParticleCoroutine = null;
    }



    void BugCollision()
    {
        if (bugFlashCoroutine != null)
        {
            StopCoroutine(bugFlashCoroutine);
        }

        bugFlashCoroutine = StartCoroutine(BugFlashEffect());
    }

    private System.Collections.IEnumerator BugFlashEffect()
    {
        float elapsedTime = 0f;

        Color originalColor = playerSprite.color;

        while (elapsedTime < bugFlashDuration)
        {
            playerSprite.color = Color.red;

            yield return new WaitForSeconds(bugFlashInterval);

            playerSprite.color = originalColor;

            yield return new WaitForSeconds(bugFlashInterval);

            elapsedTime += bugFlashInterval * 2f;
        }

        playerSprite.color = originalColor;

        bugFlashCoroutine = null;
    }

    void UpdateGameOverTrigger()
    {
        if (!isGameOver || playerCollider == null)
            return;

        // Enable trigger mode when Game Over begins.
        if (!gameOverTriggerEnabled)
        {
            playerCollider.isTrigger = true;
            gameOverTriggerEnabled = true;
        }

        // Restore physical collision when the player reaches Y = -30.
        if (transform.position.y <= gameOverTriggerY)
        {
            playerCollider.isTrigger = false;
            gameOverTriggerEnabled = false;
        }
    }

    void GameOver()
    {
        if (!isGameOver)
            return;

        if (spawnerController != null)
        {
            spawnerController.basePlatformSpeed = 0f;
            buttonsEnabled = false;
        }
    }
}
