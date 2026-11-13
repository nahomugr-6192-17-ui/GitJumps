using UnityEngine;

public class cameraScript : MonoBehaviour
{
    public GameObject player;
    public GameObject ground;

    [Header("Camera Positions")]
    public float gameplayCameraY = 1.4f;
    public float fallCameraY = -30f;
    public float initialCameraY = 0f;
    public float fallGroundY = -40f;

    [Header("Player Detection")]
    public float playerFallY = -8.57f;

    [Header("Camera Movement")]
    public float smoothSpeed = 5f;

    [Header("Adaptive Framing")]
    public float targetAspect = 16f / 9f; // 1.777778f
    public float baselineOrthoSize = 10f;

    private Rigidbody2D playerRb;
    private Collider2D playerCol;
    private playerScript playerScript;
    private platformSpawnerScript spawnerScript;

    private bool cameraLocked = false;
    private bool playerIsFalling = false;
    private playerScript playerScriptInstance;

    private Camera cam;
    private float lastAspect = -1f;
    private float effectiveGameplayY;
    private float effectiveInitialY;
    private float effectiveFallCameraY;

    public float EffectiveGameplayY => effectiveGameplayY;
    public float EffectiveInitialY => effectiveInitialY;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        UpdateCameraFraming();
    }

    public void UpdateCameraFraming()
    {
        if (cam == null)
            cam = GetComponent<Camera>();

        if (cam == null)
            return;

        float currentAspect = cam.aspect;
        lastAspect = currentAspect;

        float targetHalfWidth = baselineOrthoSize * targetAspect; // 17.77778f

        if (currentAspect < targetAspect)
        {
            // Portrait / tall screens: expand vertical view to preserve full horizontal playable width (35.56 units)
            float requiredOrthoSize = targetHalfWidth / currentAspect;
            cam.orthographicSize = requiredOrthoSize;

            float deltaSize = requiredOrthoSize - baselineOrthoSize;

            // Anchor camera bottom at -8.6f (gameplay) and -10.0f (initial)
            effectiveGameplayY = gameplayCameraY + deltaSize;
            effectiveInitialY = initialCameraY + deltaSize;
            effectiveFallCameraY = fallCameraY + deltaSize;

            UpdateSideBlockers(requiredOrthoSize);
        }
        else
        {
            // Landscape (16:9 or wider): standard baseline size and coordinates
            cam.orthographicSize = baselineOrthoSize;
            effectiveGameplayY = gameplayCameraY;
            effectiveInitialY = initialCameraY;
            effectiveFallCameraY = fallCameraY;

            UpdateSideBlockers(baselineOrthoSize);
        }
    }

    private void UpdateSideBlockers(float orthoSize)
    {
        // SideBlockers are children of Main Camera rotated 90 degrees around Z.
        // In this local orientation, local X is aligned with world Y.
        // Baseline localScale.x is 2.3f for baseline orthoSize 10.
        float verticalScale = Mathf.Max(2.3f, 2.3f * (orthoSize / baselineOrthoSize) * 1.5f);
        foreach (Transform child in transform)
        {
            if (child.name.ToLower().Contains("sideblocker") || child.name.ToLower().Contains("side"))
            {
                Vector3 s = child.localScale;
                child.localScale = new Vector3(verticalScale, s.y, s.z);
            }
        }
    }

    void Start()
    {
        if (player != null)
        {
            playerRb = player.GetComponent<Rigidbody2D>();
        }

        transform.position = new Vector3(
            transform.position.x,
            effectiveInitialY,
            transform.position.z
        );
    }

    void LateUpdate()
    {
        if (player == null)
            return;

        if (cam != null && Mathf.Abs(cam.aspect - lastAspect) > 0.001f)
        {
            UpdateCameraFraming();
        }

        // --------------------------------
        // STATE 1: Normal camera movement
        // --------------------------------
        if (!cameraLocked)
        {
            // Once the player reaches the gameplay height, lock the camera
            if (player.transform.position.y >= -5f)
            {
                cameraLocked = true;
                ground.transform.position = new Vector3(
                    ground.transform.position.x,
                    fallGroundY,
                    ground.transform.position.z
                );
                float requiredGroundWidth = Mathf.Max(36f, cam.orthographicSize * cam.aspect * 2.2f);
                ground.transform.localScale = new Vector3(
                    requiredGroundWidth,
                    ground.transform.localScale.y,
                    ground.transform.localScale.z
                );
            }
        }

        // --------------------------------
        // STATE 2: Camera locked
        // --------------------------------
        if (cameraLocked && !playerIsFalling)
        {
            // Player has fallen out of the camera
            if (player.transform.position.y <= playerFallY)
            {
                playerIsFalling = true;
                playerScriptInstance = player.GetComponent<playerScript>();
                if (playerScriptInstance != null)
                {
                    playerScriptInstance.isGameOver = true;
                    playerScriptInstance.playerIsFalling = true;
                }
            }
        }

        // --------------------------------
        // STATE 3: Player falling
        // --------------------------------
        float targetY;

        if (playerIsFalling || player.transform.position.y <= -15f)
        {
            targetY = effectiveFallCameraY;

            playerScript = player.GetComponent<playerScript>();
            if (playerScript != null)
            {
                playerScript.isGameOver = true;
                playerScript.playerIsFalling = true;
            }
            ground.transform.position = new Vector3(
                   ground.transform.position.x,
                   fallGroundY,
                   ground.transform.position.z
               );
            float requiredGroundWidth = Mathf.Max(36f, cam.orthographicSize * cam.aspect * 2.2f);
            ground.transform.localScale = new Vector3(
                    requiredGroundWidth,
                    ground.transform.localScale.y,
                    ground.transform.localScale.z
                );
        }
        else if (cameraLocked)
        {
            targetY = effectiveGameplayY;
        }
        else
        {
            // Before the camera locks, follow the player upward
            targetY = effectiveInitialY;
        }

        Vector3 targetPosition = new Vector3(
            transform.position.x,
            targetY,
            transform.position.z
        );

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothSpeed * Time.deltaTime
        );
    }
}
