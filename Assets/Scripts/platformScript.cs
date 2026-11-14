
using UnityEngine;
using System.Collections.Generic;

public class platformScript : MonoBehaviour
{
    [Header("Platform Movement")]
    public bool playerIsStandingOnThisPlatform = false;
    public Transform player;
    private Rigidbody2D playerRb;
    private float currentSpeed;

    [Header("Number of Platforms")]
    public int numberOfPlatforms = 4;

    [Header("Horizontal Movement")]
    public float horizontalSpeed = 3f;
    public float minX = -12f;
    public float maxX = 12f;

    private GameObject activePlatform;
    private int horizontalDirection = 1;
    public playerScript playerScript;
    public platformSpawnerScript spawnerController;
    private Collider2D standingCollider;

    public GameObject ActivePlatform => activePlatform;

    public void ScrollDown(float amount)
    {
        transform.position += Vector3.down * amount;
    }

    void Start()
    {
        NumOfPlatforms();

        if (spawnerController == null)
        {
            spawnerController =
                FindAnyObjectByType<platformSpawnerScript>();
        }

        GameObject playerObject = GameObject.FindWithTag("Player");

        if (playerObject != null)
        {
            playerScript = playerObject.GetComponent<playerScript>();
            playerRb = playerObject.GetComponent<Rigidbody2D>();
        }
    }

    void FixedUpdate()
    {
        // 1. Move container downward
        Vector2 containerDelta = PlatformMovement();

        // 2. Move active platform horizontally
        Vector2 activePlatformDelta = MoveActivePlatform();

        // 3. Carry the player with the platform's movement if standing on it
        if (playerIsStandingOnThisPlatform && playerRb != null && playerScript != null && playerScript.isGrounded)
        {
            // Only carry horizontally if the player is standing on the active horizontal platform
            bool isOnActivePlatform = (activePlatform != null &&
                                       standingCollider != null &&
                                       standingCollider.gameObject == activePlatform);

            Vector2 horizontalMovement = isOnActivePlatform ? activePlatformDelta : Vector2.zero;
            Vector2 carryMovement = containerDelta + horizontalMovement;

            if (carryMovement.sqrMagnitude > 0f)
            {
                playerRb.position += carryMovement;
            }

            // Downward velocity matching to prevent bouncing and loss of contact
            if (containerDelta.y < 0f)
            {
                float platformSpeedY = containerDelta.y / Time.fixedDeltaTime;
                // Only lock downward velocity if player is resting/falling, not jumping upward
                if (playerRb.linearVelocityY <= 0.1f)
                {
                    playerRb.linearVelocityY = platformSpeedY;
                }
            }
        }
    }

    private void NumOfPlatforms()
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        List<GameObject> platforms = new List<GameObject>();

        foreach (Transform child in children)
        {
            if (child != transform && child.CompareTag("Platform"))
            {
                platforms.Add(child.gameObject);
            }
        }

        if (platforms.Count == 0)
        {
            Debug.LogWarning("No child platforms found in this BlockContainer.");
            return;
        }

        // Deactivate all platforms first.
        foreach (GameObject platform in platforms)
        {
            platform.SetActive(false);
        }

        // Choose how many platforms to activate (Random Probablistic).
        int activeCount = Mathf.Min(
            ProbabilisticRandomizer(),
            numberOfPlatforms,
            platforms.Count
        );

        // Shuffle the list so we don't select the same platform twice.
        for (int i = 0; i < activeCount; i++)
        {
            int randomIndex = Random.Range(i, platforms.Count);

            GameObject temp = platforms[i];
            platforms[i] = platforms[randomIndex];
            platforms[randomIndex] = temp;

            platforms[i].SetActive(true);
        }

        // Only move a platform if exactly one was activated.
        activePlatform = activeCount == 1 ? platforms[0] : null;
        horizontalDirection = 1;
    }


    private Vector2 MoveActivePlatform()
    {
        if (activePlatform == null || !activePlatform.activeSelf)
            return Vector2.zero;

        Vector3 previousPosition = activePlatform.transform.position;

        float newX = previousPosition.x
            + horizontalDirection * horizontalSpeed * Time.fixedDeltaTime;

        if (newX >= maxX)
        {
            newX = maxX;
            horizontalDirection = -1;
        }
        else if (newX <= minX)
        {
            newX = minX;
            horizontalDirection = 1;
        }

        activePlatform.transform.position = new Vector3(
            newX,
            previousPosition.y,
            previousPosition.z
        );

        Vector3 movementDelta =
            activePlatform.transform.position - previousPosition;

        return (Vector2)movementDelta;
    }

    public void SetPlayerStanding(bool isStanding)
    {
        playerIsStandingOnThisPlatform = isStanding;
        if (!isStanding)
        {
            standingCollider = null;
        }
    }

    public void SetStandingPlatformCollider(Collider2D col)
    {
        standingCollider = col;
    }

    private Vector2 PlatformMovement()
    {
        if (spawnerController == null || playerScript == null)
            return Vector2.zero;

        if (playerScript.currentScore < 1)
            return Vector2.zero;

        float speed = spawnerController.CurrentPlatformSpeed;

        Vector3 previousPosition = transform.position;

        transform.position +=
            Vector3.down * speed * Time.fixedDeltaTime;

        Vector3 movementDelta =
            transform.position - previousPosition;

        return (Vector2)movementDelta;
    }

    private int ProbabilisticRandomizer()
    {
        float roll = Random.value; // Random value between 0 and 1

        if (roll < 0.60f)
            return 1; // 40% probability
        else if (roll < 0.80f)
            return 2; // 25% probability
        else if (roll < 0.98f)
            return 3; // 20% probability
        else
            return 4; // 10% probability
    }
}