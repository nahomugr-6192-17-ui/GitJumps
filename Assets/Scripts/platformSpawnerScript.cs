
using UnityEngine;

public class platformSpawnerScript : MonoBehaviour
{
    [Header("Spawning")]
    public float gapBetweenPlatforms = 2.5f;
    public GameObject platformPrefab;
    public float minX = -0.5f;
    public float maxX = 0.5f;
    public GameObject spawner;
    public playerScript playerScript;

    [Header("Platform Speed")]
    public float basePlatformSpeed = 0.6f;

    [Header("Temporary Speed Boost")]
    public float speedBoostMultiplier = 5f;
    public float speedBoostDuration = 0.001f;

    public float platformSpeedCoolingRate = 1f;
    private float activeSpeedBoostMultiplier = 1f;

    private float speedBoostTimer = 0f;
    private bool isSpeedBoostActive = false;

    private Transform lastSpawnedPlatform;
    private GameObject lastSpawnedPlatformObject;
    private int lastSpawnedFrame = -1;
    private readonly System.Collections.Generic.List<Transform> spawnedPlatforms = new System.Collections.Generic.List<Transform>();

    public bool IsSpeedBoostActive => isSpeedBoostActive;
    public GameObject Destroyer;
    public GameObject LandingBlock;

    public float CurrentPlatformSpeed
    {
        get
        {
            int score = playerScript != null
                ? playerScript.currentScore
                : 0;

            float scoreMultiplier = 1f;

            if (score >= 150)
                scoreMultiplier = 7.2f;
            else if (score >= 140)
                scoreMultiplier = 7f;
            else if (score >= 130)
                scoreMultiplier = 6.8f;
            else if (score >= 120)
                scoreMultiplier = 6.6f;
            else if (score >= 110)
                scoreMultiplier = 6.4f;
            else if (score >= 105)
                scoreMultiplier = 6.2f;
            else if (score >= 100)
                scoreMultiplier = 6f;
            else if (score >= 90)
                scoreMultiplier = 5.8f;
            else if (score >= 80)
                scoreMultiplier = 5.4f;
            else if (score >= 70)
                scoreMultiplier = 5.2f;
            else if (score >= 65)
                scoreMultiplier = 5f;
            else if (score >= 60)
                scoreMultiplier = 4.8f;
            else if (score >= 55)
                scoreMultiplier = 4.6f;
            else if (score >= 50)
                scoreMultiplier = 4.2f;
            else if (score >= 40)
                scoreMultiplier = 3.4f;
            else if (score >= 30)
                scoreMultiplier = 3f;
            else if (score >= 20)
                scoreMultiplier = 2.5f;
            else if (score >= 10)
                scoreMultiplier = 2f;
            else if (score >= 5)
                scoreMultiplier = 1.5f;

            float boostMultiplier = isSpeedBoostActive
                ? activeSpeedBoostMultiplier
                : 1f;

            return basePlatformSpeed * scoreMultiplier * boostMultiplier;
        }
    }

    void Start()
    {
        if (playerScript == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");

            if (playerObject != null)
                playerScript = playerObject.GetComponent<playerScript>();
        }
    }


    void Update()
    {
        // Update the temporary speed boost timer.
        if (isSpeedBoostActive)
        {
            speedBoostTimer -= Time.deltaTime;

            if (speedBoostTimer <= 0f)
            {
                speedBoostTimer = 0f;
                isSpeedBoostActive = false;
            }
        }

        // Destroy containers independently of spawning.
        DestroyBlockContainer();

        // Stop only if spawning references are missing.
        if (playerScript == null ||
            platformPrefab == null ||
            spawner == null)
        {
            return;
        }

        // Start spawning after the player scores.
        if (playerScript.currentScore < 1)
            return;

        // Spawn if there is no valid last-spawned container.
        if (lastSpawnedPlatform == null)
        {
            Debug.Log("Last BlockContainer is null. Attempting to spawn a replacement.");
            SpawnPlatform();
            return;
        }

        // Spawn another container when the vertical gap is reached.
        float verticalGap =
            spawner.transform.position.y - lastSpawnedPlatform.position.y;

        if (verticalGap >= gapBetweenPlatforms)
        {
            SpawnPlatform();
        }

        if (LandingBlock != null && LandingBlock.transform.position.y <= -10f)
        {
            Destroy(LandingBlock);
        }
    }


    void SpawnPlatform()
    {

        if (platformPrefab == null)
        {
            Debug.LogError("Spawn failed: platformPrefab is not assigned.");
            return;
        }

        if (spawner == null)
        {
            Debug.LogError("Spawn failed: spawner is not assigned.");
            return;
        }

        float randomX = Random.Range(minX, maxX);

        Vector3 spawnPosition = new Vector3(
            randomX,
            spawner.transform.position.y,
            0f
        );

        GameObject newPlatform = Instantiate(
            platformPrefab,
            spawnPosition,
            Quaternion.identity
        );

        lastSpawnedPlatform = newPlatform.transform;
        lastSpawnedPlatformObject = newPlatform;
        lastSpawnedFrame = Time.frameCount;
        spawnedPlatforms.Add(newPlatform.transform);
    }

    public void TriggerPlatformSpeedBoost(
        float multiplier,
        float duration)
    {
        activeSpeedBoostMultiplier = multiplier;
        speedBoostTimer = duration;
        isSpeedBoostActive = duration > 0f;

        if (!isSpeedBoostActive)
        {
            activeSpeedBoostMultiplier = 1f;
        }
    }

    private void DestroyBlockContainer()
    {
        if (Destroyer == null)
            return;

        float destroyerY = Destroyer.transform.position.y;

        GameObject[] containers =
            GameObject.FindGameObjectsWithTag("BlockContainer");

        foreach (GameObject container in containers)
        {
            if (container == null)
                continue;

            // Do not destroy the most recently spawned container.
            if (container == lastSpawnedPlatformObject)
                continue;

            // Only destroy containers that have passed below
            // the Destroyer's Y position.
            if (container.transform.position.y <= destroyerY)
            {
                Destroy(container);
            }
        }
    }

    public void ScrollWorldDown(float amount)
    {
        if (amount <= 0f)
            return;

        GameObject[] containers =
            GameObject.FindGameObjectsWithTag("BlockContainer");

        foreach (GameObject container in containers)
        {
            if (container == null)
                continue;

            platformScript containerScript =
                container.GetComponent<platformScript>();

            if (containerScript != null)
            {
                containerScript.ScrollDown(amount);
            }
            else
            {
                container.transform.position +=
                    Vector3.down * amount;
            }
        }

        // Move the spawner so new containers continue appearing
        // above the visible gameplay area.
        if (spawner != null)
        {
            spawner.transform.position +=
                Vector3.down * amount;
        }
    }
}
