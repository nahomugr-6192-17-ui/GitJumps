
using UnityEngine;

public class platformSpawnerScript : MonoBehaviour
{
    [Header("Spawning")]
    public float gapBetweenPlatforms = 2.5f;
    public GameObject platformPrefab;
    public float minX = -1f;
    public float maxX = 1f;
    public GameObject spawner;
    public playerScript playerScript;

    [Header("Platform Speed")]
    public float basePlatformSpeed = 0.6f;

    [Header("Temporary Speed Boost")]
    public float speedBoostMultiplier = 8f;
    public float speedBoostDuration = 12f;

    private float speedBoostTimer = 0f;
    private bool isSpeedBoostActive = false;

    private Transform lastSpawnedPlatform;

    public bool IsSpeedBoostActive => isSpeedBoostActive;

    public float CurrentPlatformSpeed
    {
        get
        {
            int score = playerScript != null
                ? playerScript.currentScore
                : 0;

            float scoreMultiplier = 1f;

            if (score >= 150)
                scoreMultiplier = 5f;
            else if (score >= 140)
                scoreMultiplier = 4.8f;
            else if (score >= 130)
                scoreMultiplier = 4.6f;
            else if (score >= 120)
                scoreMultiplier = 4.5f;
            else if (score >= 110)
                scoreMultiplier = 4.2f;
            else if (score >= 105)
                scoreMultiplier = 4.1f;
            else if (score >= 100)
                scoreMultiplier = 4f;
            else if (score >= 90)
                scoreMultiplier = 3.8f;
            else if (score >= 80)
                scoreMultiplier = 3.6f;
            else if (score >= 70)
                scoreMultiplier = 3.4f;
            else if (score >= 65)
                scoreMultiplier = 3.2f;
            else if (score >= 60)
                scoreMultiplier = 3.5f;
            else if (score >= 55)
                scoreMultiplier = 3.3f;
            else if (score >= 50)
                scoreMultiplier = 3.1f;
            else if (score >= 40)
                scoreMultiplier = 2.9f;
            else if (score >= 30)
                scoreMultiplier = 2.7f;
            else if (score >= 20)
                scoreMultiplier = 2.5f;
            else if (score >= 10)
                scoreMultiplier = 2f;
            else if (score >= 5)
                scoreMultiplier = 1.5f;
            else if (score >= 100)
                scoreMultiplier = 4f;
            else if (score >= 90)
                scoreMultiplier = 3.8f;
            else if (score >= 80)
                scoreMultiplier = 3.6f;
            else if (score >= 70)
                scoreMultiplier = 3.4f;
            else if (score >= 65)
                scoreMultiplier = 3.2f;
            else if (score >= 60)
                scoreMultiplier = 3.5f;
            else if (score >= 55)
                scoreMultiplier = 3.3f;
            else if (score >= 50)
                scoreMultiplier = 3.1f;
            else if (score >= 40)
                scoreMultiplier = 2.9f;
            else if (score >= 30)
                scoreMultiplier = 2.7f;
            else if (score >= 20)
                scoreMultiplier = 2.5f;
            else if (score >= 10)
                scoreMultiplier = 2f;
            else if (score >= 5)
                scoreMultiplier = 1.5f;

            float boostMultiplier = isSpeedBoostActive
                ? speedBoostMultiplier
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
        // End the temporary speed boost when its timer expires.
        if (isSpeedBoostActive)
        {
            speedBoostTimer -= Time.deltaTime;

            if (speedBoostTimer <= 0f)
            {
                speedBoostTimer = 0f;
                isSpeedBoostActive = false;
            }
        }

        // Check that all required references are assigned.
        if (playerScript == null ||
            platformPrefab == null ||
            spawner == null)
        {
            return;
        }

        // Start spawning platforms after the player scores.
        if (playerScript.currentScore < 1)
            return;

        // Spawn the first platform.
        if (lastSpawnedPlatform == null)
        {
            SpawnPlatform();
            return;
        }

        // Spawn another platform when the required vertical gap is reached.
        float verticalGap =
            spawner.transform.position.y - lastSpawnedPlatform.position.y;

        if (verticalGap >= gapBetweenPlatforms)
            SpawnPlatform();
    }

    void SpawnPlatform()
    {
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
    }

    public void TriggerPlatformSpeedBoost()
    {
        isSpeedBoostActive = true;
        speedBoostTimer = speedBoostDuration;
    }
}
