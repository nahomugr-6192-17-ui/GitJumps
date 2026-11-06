
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
    public float minX = -8f;
    public float maxX = 8f;

    private GameObject activePlatform;
    private int horizontalDirection = 1;
    public playerScript playerScript;
    public platformSpawnerScript spawnerController;

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
        PlatformMovement();
        MoveActivePlatform();
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


    private void MoveActivePlatform()
    {
        if (activePlatform == null || !activePlatform.activeSelf)
            return;

        float newX = activePlatform.transform.position.x
            + horizontalDirection * horizontalSpeed * Time.deltaTime;

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

        Vector3 previousPosition = activePlatform.transform.position;

        activePlatform.transform.position = new Vector3(
            newX,
            previousPosition.y,
            previousPosition.z
        );

        Vector3 movementDelta =
            activePlatform.transform.position - previousPosition;

        if (playerIsStandingOnThisPlatform && playerRb != null)
        {
            playerRb.MovePosition(
                playerRb.position + (Vector2)movementDelta
            );
        }
    }

    public void SetPlayerStanding(bool isStanding)
    {
        playerIsStandingOnThisPlatform = isStanding;
    }

    private void PlatformMovement()
    {
        if (spawnerController == null || playerScript == null)
            return;

        if (playerScript.currentScore < 1)
            return;

        float speed = spawnerController.CurrentPlatformSpeed;

        transform.position += Vector3.down * speed * Time.deltaTime;
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