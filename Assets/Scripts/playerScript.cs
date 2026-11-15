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
    private Collider2D standingPlatformCollider;
    public platformSpawnerScript spawnerController;
    public bool isCoolingPlatformSpeed = false;
    public float platformSpeedCoolingRate = 1f;
    [Header("Movement Smoothing")]
    public float horizontalAcceleration = 35f;
    public float horizontalDeceleration = 45f;

    [Header("Commit Boost")]
    public float commitJumpMultiplier = 1.3f;
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
    public bool playerIsFalling = false;
    private Color originalPlayerColor = Color.white;
    [Header("Game Over")]
    public float gameOverTriggerY = -30f;
    private bool gameOverTriggerEnabled = false;
    private Collider2D playerCollider;

    [Header("Landing Disappearance Effect")]
    public float collapseFadeDuration = 0.4f;

    private HashSet<GameObject> fadingObjects = new HashSet<GameObject>();

    public UIDocument currentScoreUI;
    private Label currentScoreLabel;
    private bool isSprintBlockerActive = true;
    private GameObject sprintBlocker;
    public UIDocument gameOverUI;
    private Label CurrentScoreGameOver;
    private Label BestScore;
    private Button RestartButton;
    private Button HomeButton;
    private Button LeaderboardButton;
    private bool gameOverHandled = false;
    private bool gameOverUIShown = false;
    private bool gameOverSoundPlayed = false;

    [Header("Mobile / Tablet Landscape Controls")]
    private bool isMobileLeftHeld = false;
    private bool isMobileRightHeld = false;
    private bool isMobileJumpHeld = false;
    private VisualElement mobileControlsRoot;
    private Button btnMoveLeft;
    private Button btnMoveRight;
    private Button btnJump;
    private Vector3 initialPlayerScale;
    private float initialMoveSpeed;
    private float initialJumpForce;
    private float initialGameOverTriggerY;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        camera = GameObject.Find("Main Camera");
        currentScoreLabel = currentScoreUI.rootVisualElement.Q<Label>("currentScoreLabel");
        currentScore = 0;
        currentScoreLabel.text = "Score: " + currentScore.ToString();
        highScore = PlayerPrefs.GetInt("HighScore", 0);
        spawnerController = FindAnyObjectByType<platformSpawnerScript>();
        defaultJumpForceMultiplier = jumpForceMultiplier;
        playerSprite = GetComponentInChildren<SpriteRenderer>();
        if (playerSprite != null) originalPlayerColor = playerSprite.color;
        playerCollider = GetComponent<Collider2D>();
        sprintBlocker = GameObject.FindWithTag("SprintBlocker");


        CurrentScoreGameOver = gameOverUI.rootVisualElement.Q<Label>("CurrentScoreGameOver");
        BestScore = gameOverUI.rootVisualElement.Q<Label>("BestScore");
        RestartButton = gameOverUI.rootVisualElement.Q<Button>("RestartButton");
        HomeButton = gameOverUI.rootVisualElement.Q<Button>("HomeButton");
        RestartButton.clicked += RestartGame;
        HomeButton.clicked += GoHome;

        LeaderboardButton = gameOverUI.rootVisualElement.Q<Button>("LeaderboardButton");
        if (LeaderboardButton != null)
        {
            LeaderboardButton.clicked += OpenLeaderboard;
        }

        initialPlayerScale = transform.localScale;
        initialMoveSpeed = moveSpeed;
        initialJumpForce = jumpForce;
        initialGameOverTriggerY = gameOverTriggerY;

        ApplyGameplayScale(ResponsiveGameplayScaler.CurrentScaleFactor);
        ResponsiveGameplayScaler.OnLayoutChanged += OnLayoutChangedHandler;

        // Initialize mobile on-screen controls
        if (currentScoreUI != null && currentScoreUI.rootVisualElement != null)
        {
            mobileControlsRoot = currentScoreUI.rootVisualElement.Q<VisualElement>("mobile-controls-root");
            btnMoveLeft = currentScoreUI.rootVisualElement.Q<Button>("btn-move-left");
            btnMoveRight = currentScoreUI.rootVisualElement.Q<Button>("btn-move-right");
            btnJump = currentScoreUI.rootVisualElement.Q<Button>("btn-jump");

            UpdateMobileControlsDisplay(ResponsiveGameplayScaler.ActiveLayoutMode, ResponsiveGameplayScaler.CurrentScaleFactor);

            SetupTouchButton(btnMoveLeft, held => isMobileLeftHeld = held);
            SetupTouchButton(btnMoveRight, held => isMobileRightHeld = held);
            SetupTouchButton(btnJump, held => isMobileJumpHeld = held);
        }

        // Telemetry: record gameplay start (non-blocking)
        GitJumps.Leaderboard.SupabaseLeaderboardManager.Instance?.TrackGameStarted();

        // Start background music when gameplay begins
        AudioManager.Instance?.PlayBackgroundMusic();
    }

    private void OnDestroy()
    {
        ResponsiveGameplayScaler.OnLayoutChanged -= OnLayoutChangedHandler;
    }

    private void OnLayoutChangedHandler(DeviceLayoutMode mode, float scaleFactor)
    {
        ApplyGameplayScale(scaleFactor);
        UpdateMobileControlsDisplay(mode, scaleFactor);
    }

    private void ApplyGameplayScale(float scaleFactor)
    {
        if (scaleFactor <= 0f) scaleFactor = 1f;

        if (ResponsiveGameplayScaler.IsDesktopLayout())
        {
            // Desktop: restore exact 100% desktop scale and speeds
            transform.localScale = initialPlayerScale;
            moveSpeed = initialMoveSpeed;
            jumpForce = initialJumpForce;
            gameOverTriggerY = initialGameOverTriggerY;
        }
        else
        {
            // Mobile or Tablet: scale player size and proportional physics
            transform.localScale = initialPlayerScale * scaleFactor;
            moveSpeed = initialMoveSpeed;
            // Provide strong jumpForce so player easily reaches the landing block and subsequent platforms
            jumpForce = initialJumpForce * 1.07f;
            gameOverTriggerY = initialGameOverTriggerY;
        }
    }

    private void UpdateMobileControlsDisplay(DeviceLayoutMode mode, float scaleFactor)
    {
        if (mobileControlsRoot == null) return;

        if (mode == DeviceLayoutMode.Desktop)
        {
            // Desktop: keep desktop layout the way it is! Hide on-screen touch buttons
            mobileControlsRoot.style.display = DisplayStyle.None;
            isMobileLeftHeld = false;
            isMobileRightHeld = false;
            isMobileJumpHeld = false;
        }
        else
        {
            // Mobile / Tablet: display touch buttons
            mobileControlsRoot.style.display = DisplayStyle.Flex;

            float btnWidth = (mode == DeviceLayoutMode.Tablet) ? 114f : 96f;
            float btnHeight = (mode == DeviceLayoutMode.Tablet) ? 80f : 72f;
            float jumpWidth = (mode == DeviceLayoutMode.Tablet) ? 144f : 120f;

            if (btnMoveLeft != null)
            {
                btnMoveLeft.style.width = btnWidth;
                btnMoveLeft.style.height = btnHeight;
            }
            if (btnMoveRight != null)
            {
                btnMoveRight.style.width = btnWidth;
                btnMoveRight.style.height = btnHeight;
            }
            if (btnJump != null)
            {
                btnJump.style.width = jumpWidth;
                btnJump.style.height = btnHeight;
            }
        }
    }

    private void SetupTouchButton(Button btn, System.Action<bool> stateSetter)
    {
        if (btn == null) return;

        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            stateSetter(true);
            btn.CapturePointer(evt.pointerId);
            btn.style.backgroundColor = new StyleColor(new Color(0.22f, 0.28f, 0.38f, 0.95f));
        });

        btn.RegisterCallback<PointerUpEvent>(evt =>
        {
            stateSetter(false);
            if (btn.HasPointerCapture(evt.pointerId))
                btn.ReleasePointer(evt.pointerId);
            btn.style.backgroundColor = new StyleColor(new Color(0.07f, 0.09f, 0.13f, 0.78f));
        });

        btn.RegisterCallback<PointerCancelEvent>(evt =>
        {
            stateSetter(false);
            if (btn.HasPointerCapture(evt.pointerId))
                btn.ReleasePointer(evt.pointerId);
            btn.style.backgroundColor = new StyleColor(new Color(0.07f, 0.09f, 0.13f, 0.78f));
        });

        btn.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            if (!btn.HasPointerCapture(evt.pointerId))
            {
                stateSetter(false);
                btn.style.backgroundColor = new StyleColor(new Color(0.07f, 0.09f, 0.13f, 0.78f));
            }
        });
    }

    // Update is called once per frame
    void Update()
    {
        bool leftPressed = isMobileLeftHeld;
        bool rightPressed = isMobileRightHeld;
        bool jumpPressed = isMobileJumpHeld;

        if (Keyboard.current != null)
        {
            leftPressed |=
                Keyboard.current.aKey.isPressed ||
                Keyboard.current.leftArrowKey.isPressed;

            rightPressed |=
                Keyboard.current.dKey.isPressed ||
                Keyboard.current.rightArrowKey.isPressed;

            jumpPressed |=
                Keyboard.current.wKey.isPressed ||
                Keyboard.current.upArrowKey.isPressed;
        }

        if (buttonsEnabled == true && !isGameOver)
        {

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
        }

        /*float distanceBetweenBoosters = Mathf.Abs(
            platform1.transform.position.y - platform2.transform.position.y
        );
        Debug.Log("Distance between boosters: " + distanceBetweenBoosters);
        */

        if (gameOverUIShown == true)
        {
            gameOverUI.rootVisualElement.style.display = DisplayStyle.Flex;
            if (mobileControlsRoot != null)
                mobileControlsRoot.style.display = DisplayStyle.None;
        }
        else
        {
            gameOverUI.rootVisualElement.style.display = DisplayStyle.None;
        }
        ActivateSprintBlocker();
        if ((isGameOver || transform.position.y <= gameOverTriggerY) && !gameOverSoundPlayed)
        {
            TriggerGameOverAudio();
        }
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

        // Game Over landing: safely halt motion and lock into lying pose on the floor
        if (collision.gameObject.CompareTag("Floor") && isGameOver)
        {
            HandleFloorLanding();
            return;
        }

        foreach (ContactPoint2D contact in collision.contacts)
        {
            // Only count contact with the top of a surface as landing.
            if (contact.normal.y <= 0.5f)
                continue;

            // Don't land/attach while actively jumping upward
            if (rb.linearVelocityY > 0.1f)
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
                    AttachToPlatform(collision.collider, newPlatform);
                }
            }
            else
            {
                StopPlatformCarry();
            }

            break;
        }
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Platform") &&
            !collision.gameObject.CompareTag("Floor"))
        {
            return;
        }

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y <= 0.5f)
                continue;

            if (rb.linearVelocityY > 0.1f)
                continue;

            isGrounded = true;

            if (collision.gameObject.CompareTag("Platform"))
            {
                platformScript pScript =
                    collision.gameObject.GetComponentInParent<platformScript>();

                if (pScript != null && (standingPlatform != pScript || standingPlatformCollider != collision.collider))
                {
                    AttachToPlatform(collision.collider, pScript);
                }
            }
            break;
        }
    }

    private void AttachToPlatform(
        Collider2D platformCollider,
        platformScript platformController)
    {
        if (platformCollider == null || platformController == null)
            return;

        if (standingPlatform != null && standingPlatform != platformController)
        {
            standingPlatform.SetPlayerStanding(false);
            standingPlatform.SetStandingPlatformCollider(null);
        }

        standingPlatform = platformController;
        standingPlatformCollider = platformCollider;

        standingPlatform.player = transform;
        standingPlatform.SetStandingPlatformCollider(platformCollider);
        standingPlatform.SetPlayerStanding(true);

        isGrounded = true;

        // Immediately synchronize downward velocity with the descending platform
        if (spawnerController != null && currentScore >= 1)
        {
            rb.linearVelocityY = -spawnerController.CurrentPlatformSpeed;
        }
        else
        {
            rb.linearVelocityY = 0f;
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

        if (standingPlatformCollider != null &&
            collision.collider == standingPlatformCollider)
        {
            StopPlatformCarry();
            isGrounded = false;
        }
        else if (collision.gameObject.CompareTag("Floor"))
        {
            isGrounded = false;
        }
    }

    public void StopPlatformCarry()
    {
        if (standingPlatform != null)
        {
            standingPlatform.SetPlayerStanding(false);
            standingPlatform.SetStandingPlatformCollider(null);
            standingPlatform = null;
        }

        standingPlatformCollider = null;
    }

    private void Jump()
    {
        AudioManager.Instance?.PlayJump();

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
                    5f,
                    0.45f
                );
            }

            if (currentScore <= 60 && currentScore >= 3)
            {
                spawnerController.TriggerPlatformSpeedBoost(
                    4.8f,
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
                PlayerPrefs.SetInt("Score", currentScore);
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
            isSprintBlockerActive = false;
            Debug.Log("Commit activated!");

            ActivateCommitBoost();
            spawnerController.TriggerPlatformSpeedBoost(
                5.5f,
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

        if (collision.gameObject.CompareTag("Floor") && isGameOver)
        {
            HandleFloorLanding();
            return;
        }

        if (collision.gameObject.CompareTag("Bug"))
        {
            if (isBugTriggered) return;
            isBugTriggered = true;
            playerIsFalling = true;
            isGameOver = true;
            TriggerGameOverAudio();
            BugCollision();
            Destroy(collision.gameObject);
            if (Commit != null) Destroy(Commit);
            PlayerAnimationController animCtrl = GetComponent<PlayerAnimationController>();
            if (animCtrl != null) animCtrl.TriggerBugVibration();
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

    private void ActivateSprintBlocker()
    {
        if (sprintBlocker == null) return;
        sprintBlocker.SetActive(isSprintBlockerActive);
    }

    private System.Collections.IEnumerator ResetCommitBoost()
    {
        yield return new WaitForSeconds(commitBoostDuration);

        jumpForceMultiplier = defaultJumpForceMultiplier;

        isSprintBlockerActive = true;

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



    private void BugCollision()
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

        while (elapsedTime < bugFlashDuration)
        {
            if (playerSprite != null) playerSprite.color = Color.red;

            yield return new WaitForSeconds(bugFlashInterval);

            if (playerSprite != null) playerSprite.color = originalPlayerColor;

            yield return new WaitForSeconds(bugFlashInterval);

            elapsedTime += bugFlashInterval * 2f;
        }

        if (playerSprite != null) playerSprite.color = originalPlayerColor;

        bugFlashCoroutine = null;
    }


    public void HandleFloorLanding()
    {
        if (!isGameOver) return;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (bugFlashCoroutine != null)
        {
            StopCoroutine(bugFlashCoroutine);
            bugFlashCoroutine = null;
        }
        if (playerSprite != null) playerSprite.color = originalPlayerColor;
        PlayerAnimationController animCtrl = GetComponent<PlayerAnimationController>();
        if (animCtrl != null) animCtrl.TriggerLieDown();
    }
    private void UpdateGameOverTrigger()
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
        }
    }


    private void GameOver()
    {
        if (!isGameOver || gameOverHandled)
            return;

        // Wait until the player reaches the Game Over display height.
        if (transform.position.y > -36f)
            return;

        gameOverHandled = true;
        gameOverUIShown = true;
        currentScoreUI.rootVisualElement.style.display = DisplayStyle.None;

        if (spawnerController != null)
        {
            spawnerController.basePlatformSpeed = 0f;
        }

        buttonsEnabled = false;

        TriggerGameOverAudio();

        GameOverScenes();
    }

    public void TriggerGameOverAudio()
    {
        if (gameOverSoundPlayed) return;
        gameOverSoundPlayed = true;

        AudioManager.Instance?.PlayGameOver();
    }


    private void GameOverScenes()
    {
        CurrentScoreGameOver.text = currentScore.ToString();
        if (currentScore > highScore)
        {
            highScore = currentScore;
            PlayerPrefs.SetInt("HighScore", highScore);
            PlayerPrefs.Save();
        }
        BestScore.text = "Best: " + highScore.ToString();

        // Submit final score to Supabase Global Leaderboard (if configured)
        if (GitJumps.Leaderboard.SupabaseLeaderboardManager.Instance != null)
        {
            GitJumps.Leaderboard.SupabaseLeaderboardManager.Instance.SubmitScore(currentScore);
        }
    }

    private void OpenLeaderboard()
    {
        if (GitJumps.Leaderboard.LeaderboardUIController.Instance != null)
        {
            GitJumps.Leaderboard.LeaderboardUIController.Instance.ShowLeaderboard();
        }
    }

    private void RestartGame()
    {
        Time.timeScale = 1f;

        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }

    private void GoHome()
    {
        Time.timeScale = 1f;

        UnityEngine.SceneManagement.SceneManager.LoadScene("HomeScene");
    }
}
