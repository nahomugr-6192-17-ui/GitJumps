using UnityEngine;

/// <summary>
/// Smooth 2D character animation controller for GitJumps.
/// Drives Idle, Running (grounded horizontal movement with alternating legs), Tucked Jump,
/// Commit Boost, Downward Dive Fall, Bug Vibration, and Floor Lie Down poses
/// seamlessly based on player movement state, physics, and collisions without modifying core game logic.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationController : MonoBehaviour
{
    private playerScript player;
    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sr;
    private bool wasGrounded = true;

    [Header("Visual Container")]
    public Transform visualTransform;

    [Header("Poses")]
    public Sprite idleSprite;
    public Sprite idleBreatheSprite;
    public Sprite jumpSprite;
    public Sprite boostSprite;
    public Sprite fallSprite;
    public Sprite landSprite;
    public Sprite lieDownSprite;
    public Sprite lieDownRestSprite;

    [Header("Running Poses")]
    public Sprite[] runSprites;
    public float runCycleSpeed = 10f;
    private float runTimer = 0f;
    private int currentRunFrame = 0;

    [Header("Timing")]
    public float landDuration = 0.14f;
    public float idleCycleDuration = 0.8f;
    public float bugVibrationDuration = 0.4f;

    [Header("Tilt & Smoothness")]
    public float maxDiveAngle = 165f;
    public float tiltSmoothSpeed = 8f;

    private float landTimer = 0f;
    private float idleTimer = 0f;
    private float bugVibrationTimer = 0f;
    private bool idleBreatheState = false;
    private bool isLyingDown = false;
    private float currentZAngle = 0f;

    void Awake()
    {
        player = GetComponent<playerScript>();
        rb = GetComponent<Rigidbody2D>();

        // Wire visualTransform: use assigned field, child named "CatVisual", or this transform
        if (visualTransform == null)
        {
            Transform child = transform.Find("CatVisual");
            visualTransform = child != null ? child : transform;
        }

        sr = visualTransform.GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();

        anim = visualTransform.GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        // Load fallback sprites from Resources or sprite sheet if unassigned or missing
        bool needsRun = runSprites == null || runSprites.Length == 0 || runSprites[0] == null;
        if (idleSprite == null || jumpSprite == null || boostSprite == null || fallSprite == null || lieDownSprite == null || lieDownRestSprite == null || needsRun)
        {
            Sprite[] sheet = Resources.FindObjectsOfTypeAll<Sprite>();
            Sprite r0 = null, r1 = null, r2 = null, r3 = null;
            foreach (Sprite s in sheet)
            {
                if (s == null) continue;
                if (idleSprite == null && s.name == "Cat_0") idleSprite = s;
                if (idleBreatheSprite == null && s.name == "Cat_Idle_1") idleBreatheSprite = s;
                if (jumpSprite == null && s.name == "Cat_Jump") jumpSprite = s;
                if (fallSprite == null && s.name == "Cat_Fall") fallSprite = s;
                if (landSprite == null && s.name == "Cat_Land") landSprite = s;
                if (boostSprite == null && s.name == "Cat_Jump_Boost") boostSprite = s;
                if (lieDownSprite == null && s.name == "Cat_LieDown") lieDownSprite = s;
                if (lieDownRestSprite == null && (s.name == "Cat_LieDown_3" || s.name == "Cat_LieDown_Rest")) lieDownRestSprite = s;
                if (r0 == null && s.name == "Cat_Run_0") r0 = s;
                if (r1 == null && s.name == "Cat_Run_1") r1 = s;
                if (r2 == null && s.name == "Cat_Run_2") r2 = s;
                if (r3 == null && s.name == "Cat_Run_3") r3 = s;
            }

            if (needsRun && r0 != null)
            {
                runSprites = new Sprite[] { r0, r1 != null ? r1 : r0, r2 != null ? r2 : r0, r3 != null ? r3 : r0 };
            }
            if (lieDownRestSprite == null && lieDownSprite != null)
            {
                lieDownRestSprite = lieDownSprite;
            }
        }
    }

    /// <summary>
    /// Triggered upon Bug collision: initiates 0.4s rapid shaking without stopping downward gravity acceleration.
    /// </summary>
    public void TriggerBugVibration()
    {
        bugVibrationTimer = bugVibrationDuration;
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.SetTrigger("BugHit");
        }
    }

    /// <summary>
    /// Triggered when the player reaches the floor at Game Over: locks player horizontally on the floor.
    /// </summary>
    public void TriggerLieDown()
    {
        isLyingDown = true;
        bugVibrationTimer = 0f;
        landTimer = 0f;
        currentZAngle = 0f;

        if (visualTransform != null)
        {
            visualTransform.localPosition = Vector3.zero;
            visualTransform.localRotation = Quaternion.identity;
        }

        if (sr != null)
        {
            sr.flipX = false;
        }

        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.SetBool("IsGameOverFloor", true);
            anim.SetBool("IsGrounded", true);
            anim.SetBool("IsDeathFalling", false);
            anim.SetBool("IsRunning", false);
            anim.SetBool("IsBoosted", false);
            anim.Play("LieDown", 0, 0f);
        }
        else if (sr != null)
        {
            if (lieDownRestSprite != null) sr.sprite = lieDownRestSprite;
            else if (lieDownSprite != null) sr.sprite = lieDownSprite;
        }
    }

    void Update()
    {
        // =========================================================================
        // PRIORITY 1: GAME OVER FLOOR (HORIZONTAL LYING DOWN WITH TAIL ON FLOOR)
        // =========================================================================
        if (isLyingDown)
        {
            if (visualTransform != null)
            {
                visualTransform.localPosition = Vector3.zero;
                visualTransform.localRotation = Quaternion.identity;
            }
            if (sr != null)
            {
                sr.flipX = false;
            }
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.SetBool("IsGameOverFloor", true);
                anim.SetBool("IsGrounded", true);
                anim.SetBool("IsDeathFalling", false);
                anim.SetBool("IsRunning", false);
                anim.SetBool("IsBoosted", false);
            }
            else if (sr != null)
            {
                if (lieDownRestSprite != null) sr.sprite = lieDownRestSprite;
                else if (lieDownSprite != null) sr.sprite = lieDownSprite;
            }

            // Guaranteed visibility safety: ensure sprite is never null
            if (sr != null && sr.sprite == null)
            {
                if (lieDownRestSprite != null) sr.sprite = lieDownRestSprite;
                else if (lieDownSprite != null) sr.sprite = lieDownSprite;
                else if (idleSprite != null) sr.sprite = idleSprite;
            }
            return;
        }

        bool isGrounded = player != null ? player.isGrounded : true;
        float velX = rb != null ? rb.linearVelocity.x : 0f;
        float velY = rb != null ? rb.linearVelocity.y : 0f;
        bool isGameOver = player != null && player.isGameOver;
        bool isFalling = player != null && player.playerIsFalling;
        bool isBug = player != null && player.isBugTriggered;

        // Death/Falling condition: ONLY true when game over / death fall has actually started!
        // A negative vertical velocity by itself NEVER causes isDeathFalling!
        bool isDeathFalling = (isGameOver || isFalling || isBug) && !isGrounded;

        // Detect landing impact when transitioning from airborne to grounded during normal gameplay
        if (!wasGrounded && isGrounded && velY <= 0.2f && !isGameOver)
        {
            landTimer = landDuration;
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.SetTrigger("Land");
            }
        }

        bool isBoosted = player != null && player.isCommitTriggered;

        // Running: strictly grounded horizontal movement
        bool isRunning = isGrounded && Mathf.Abs(velX) > 0.2f && !isGameOver && !isDeathFalling && landTimer <= 0f;

        // Flip sprite based on horizontal movement
        if (sr != null && !isGameOver && !isDeathFalling)
        {
            if (velX > 0.2f)
            {
                sr.flipX = false;
            }
            else if (velX < -0.2f)
            {
                sr.flipX = true;
            }
        }

        // Update Animator parameters
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.SetBool("IsGrounded", isGrounded);
            anim.SetFloat("VelocityY", velY);
            anim.SetBool("IsBoosted", isBoosted);
            anim.SetBool("IsDeathFalling", isDeathFalling);
            anim.SetBool("IsGameOverFloor", isLyingDown);
            anim.SetBool("IsRunning", isRunning);
        }

        // =========================================================================
        // PRIORITY 3: BUG COLLISION VIBRATION
        // Rapid horizontal shaking (+-0.08f) and alternating rotation (+-4 deg)
        // Applies ONLY to visualTransform without shaking Rigidbody2D/collider!
        // =========================================================================
        if (bugVibrationTimer > 0f)
        {
            bugVibrationTimer -= Time.deltaTime;
            float shakeFreq = 60f;
            float shakeX = Mathf.Sin(Time.time * shakeFreq) * 0.08f;
            float shakeRot = Mathf.Cos(Time.time * shakeFreq) * 4f;

            if (visualTransform != null)
            {
                visualTransform.localPosition = new Vector3(shakeX, 0f, 0f);
                visualTransform.localRotation = Quaternion.Euler(0f, 0f, shakeRot);
            }

            if (sr != null && fallSprite != null)
            {
                sr.sprite = fallSprite;
            }

            if (bugVibrationTimer <= 0f && visualTransform != null)
            {
                visualTransform.localPosition = Vector3.zero;
            }

            wasGrounded = isGrounded;
            return;
        }

        // =========================================================================
        // ROTATION / TILT HANDLING
        // =========================================================================
        float targetAngle = 0f;

        if (isDeathFalling)
        {
            // Smooth head-down dive ONLY during death/game-over fall
            float fallProgress = Mathf.Clamp01(-velY / 10f);
            float baseDiveAngle = velX < -0.1f ? maxDiveAngle : -maxDiveAngle;
            targetAngle = Mathf.Lerp(0f, baseDiveAngle, fallProgress);
            currentZAngle = Mathf.Lerp(currentZAngle, targetAngle, Time.deltaTime * tiltSmoothSpeed);
        }
        else
        {
            // Normal gameplay: KEEP THE CAT UPRIGHT (0°) AT ALL TIMES
            // (Standing, normal jump rise, jump apex, normal jump descent, booster launch, booster descent)
            currentZAngle = 0f;
        }

        if (visualTransform != null)
        {
            visualTransform.localPosition = Vector3.zero;
            visualTransform.localRotation = Quaternion.Euler(0f, 0f, currentZAngle);
        }

        // =========================================================================
        // SPRITE SELECTION (Code-driven backup / direct synchronization)
        // =========================================================================
        if (sr != null)
        {
            if (isDeathFalling)
            {
                // PRIORITY 2: Death fall pose
                if (fallSprite != null) sr.sprite = fallSprite;
            }
            else if (!isGrounded)
            {
                if (isBoosted)
                {
                    // PRIORITY 4: CommitBoost launch & descent (upright)
                    if (boostSprite != null) sr.sprite = boostSprite;
                    else if (jumpSprite != null) sr.sprite = jumpSprite;
                }
                else
                {
                    // PRIORITY 5: Normal Jump (upright tucked pose during both rise and descent!)
                    if (jumpSprite != null) sr.sprite = jumpSprite;
                }
            }
            else
            {
                // Grounded
                if (landTimer > 0f)
                {
                    landTimer -= Time.deltaTime;
                    if (landSprite != null) sr.sprite = landSprite;
                }
                else if (isRunning)
                {
                    // PRIORITY 6: Grounded running animation cycle
                    runTimer += Time.deltaTime * runCycleSpeed;
                    if (runTimer >= 1f)
                    {
                        runTimer -= 1f;
                        int frameCount = runSprites != null && runSprites.Length > 0 ? runSprites.Length : 4;
                        currentRunFrame = (currentRunFrame + 1) % frameCount;
                    }
                    if (runSprites != null && runSprites.Length > currentRunFrame && runSprites[currentRunFrame] != null)
                    {
                        sr.sprite = runSprites[currentRunFrame];
                    }
                    else if (idleSprite != null)
                    {
                        sr.sprite = idleSprite;
                    }
                }
                else
                {
                    // PRIORITY 7: Idle approved breathing loop
                    currentRunFrame = 0;
                    runTimer = 0f;
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

            // Absolute visibility guarantee: SpriteRenderer must never be null
            if (sr.sprite == null)
            {
                if (idleSprite != null) sr.sprite = idleSprite;
            }
        }

        wasGrounded = isGrounded;
    }
}
