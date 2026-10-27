using UnityEngine;

public class cameraScript : MonoBehaviour
{
    public GameObject player;
    public GameObject ground;

    [Header("Camera Positions")]
    public float gameplayCameraY = 1.4f;
    public float fallCameraY = -14.5f;
    public float initialCameraY = 0f;
    public float fallGroundY = -24f;

    [Header("Player Detection")]
    public float playerFallY = -8.57f;

    [Header("Camera Movement")]
    public float smoothSpeed = 5f;

    private Rigidbody2D playerRb;
    private Collider2D playerCol;

    private bool cameraLocked = false;
    private bool playerIsFalling = false;

    void Start()
    {
        if (player != null)
        {
            playerRb = player.GetComponent<Rigidbody2D>();
        }
        transform.position = new Vector3(
            transform.position.x,
            initialCameraY,
            transform.position.z
        );
    }

    void LateUpdate()
    {
        if (player == null)
            return;

        // --------------------------------
        // STATE 1: Normal camera movement
        // --------------------------------
        if (!cameraLocked)
        {
            // Once the player reaches the gameplay height,
            // lock the camera at Y = 1.05
            if (player.transform.position.y >= -5f)
            {
                cameraLocked = true;
                ground.transform.position = new Vector3(
                    ground.transform.position.x,
                    fallGroundY,
                    ground.transform.position.z
                );
            }
        }

        // --------------------------------
        // STATE 2: Camera locked at 1.05
        // --------------------------------
        if (cameraLocked && !playerIsFalling)
        {
            // Player has fallen out of the camera
            if (player.transform.position.y <= playerFallY)
            {
                playerIsFalling = true;
            }
        }

        // --------------------------------
        // STATE 3: Player falling
        // --------------------------------
        float targetY;

        if (playerIsFalling)
        {
            targetY = fallCameraY;
            Destroy(GameObject.FindWithTag("BlockContainer"));
        }
        else if (cameraLocked)
        {
            targetY = gameplayCameraY;
        }
        else
        {
            // Before the camera locks, follow the player upward
            targetY = initialCameraY;
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