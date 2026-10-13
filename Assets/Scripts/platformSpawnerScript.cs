
using UnityEngine;

public class platformSpawnerScript : MonoBehaviour
{
    [Header("Spawning")]
    public float gapBetweenPlatforms = 3.3f;
    public GameObject platformPrefab;
    public float minX = -1f;
    public float maxX = 1f;
    public GameObject spawner;
    public playerScript playerScript;

    [Header("Shared Platform Speed")]
    [Header("Shared Platform Speed")]
    public float normalPlatformSpeed = 0.2f;
    public float multiplierSpeed = 1.15f;
    public float boostDuration = 0.4f;
    public float speedTransitionRate = 2f;

    private float currentMultiplier = 1f;
    private float targetMultiplier = 1f;
    private float boostTimer = 0f;
    private Transform lastSpawnedPlatform;
    private float activeJumpMultiplier = 1f;

    public float CurrentPlatformSpeed
    {
        get { return normalPlatformSpeed * currentMultiplier; }
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
        if (boostTimer > 0f)
        {
            boostTimer -= Time.deltaTime;

            if (boostTimer <= 0f)
                targetMultiplier = 1f;
        }

        currentMultiplier = Mathf.MoveTowards(
            currentMultiplier,
            targetMultiplier,
            speedTransitionRate * Time.deltaTime
        );

        // Keep your existing platform-spawning logic below.
        if (playerScript == null || platformPrefab == null || spawner == null)
            return;

        if (playerScript.currentScore < 1)
            return;

        if (lastSpawnedPlatform == null)
        {
            SpawnPlatform();
            return;
        }

        float verticalGap =
            spawner.transform.position.y - lastSpawnedPlatform.position.y;

        if (verticalGap >= gapBetweenPlatforms)
            SpawnPlatform();
    }

    public void ActivateMultiplierSpeed(float jumpMultiplier)
    {
        targetMultiplier = multiplierSpeed;
        boostTimer = boostDuration;
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
}
