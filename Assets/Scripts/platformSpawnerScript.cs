
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

    [Header("PowerUp Spawning")]
    public GameObject powerUpsPrefab;

    public float powerUpMinX = -5f;
    public float powerUpMaxX = 5f;

    public float minPowerUpGap = 7f;
    public float maxPowerUpGap = 10f;

    private Transform lastSpawnedPowerUp;
    private float nextPowerUpGap;

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

    private void Start()
    {
        if (playerScript == null)
            playerScript = FindAnyObjectByType<playerScript>();

        nextPowerUpGap = Random.Range(minPowerUpGap, maxPowerUpGap);

        RandomizePowerUpHierarchy();
    }

    private void RandomizePowerUpHierarchy()
    {
        Transform booster = powerUpsPrefab.transform.Find("Booster");
        Transform bug = powerUpsPrefab.transform.Find("Bug");

        if (booster == null || bug == null)
        {
            Debug.LogError("PowerUps prefab must contain Booster and Bug.");
            return;
        }

        int randomPowerUp = BoosterSpawnerRandomizer();

        booster.gameObject.SetActive(randomPowerUp == 2);
        bug.gameObject.SetActive(randomPowerUp == 1);
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
                activeSpeedBoostMultiplier = 1f;
            }
        }

        // Destroy containers independently of spawning.
        DestroyBlockContainer();
        DestroyPowerUpContainer();

        if (playerScript == null)
            return;

        // Start both spawning systems after the player scores.
        if (playerScript.currentScore < 1)
            return;

        // --------------------------------
        // POWERUP SPAWNING
        // --------------------------------

        // Spawn the first PowerUp.
        if (lastSpawnedPowerUp == null)
        {
            SpawnPowerUp();
        }
        else
        {
            // Measure the distance between the spawner
            // and the latest spawned PowerUp.
            float powerUpGap =
                spawner.transform.position.y -
                lastSpawnedPowerUp.position.y;

            // Spawn another PowerUp when the randomized gap is reached.
            if (powerUpGap >= nextPowerUpGap)
            {
                SpawnPowerUp();
            }
        }

        // --------------------------------
        // PLATFORM SPAWNING
        // --------------------------------

        if (platformPrefab == null || spawner == null)
            return;

        if (lastSpawnedPlatform == null)
        {
            SpawnPlatform();
            return;
        }

        float verticalGap =
            spawner.transform.position.y -
            lastSpawnedPlatform.position.y;

        if (verticalGap >= gapBetweenPlatforms)
        {
            SpawnPlatform();
        }

        if (LandingBlock != null &&
            LandingBlock.transform.position.y <= -10f)
        {
            Destroy(LandingBlock);
        }
    }


    void SpawnPlatform()
    {
        if (platformPrefab == null)
            return;

        if (spawner == null)
            return;

        float randomX = Random.Range(minX, maxX);

        Vector3 spawnPosition = new Vector3(
            randomX,
            transform.position.y,
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

    private void SpawnPowerUp()
    {
        if (powerUpsPrefab == null || spawner == null)
            return;

        float randomX = Random.Range(
            powerUpMinX,
            powerUpMaxX
        );

        Vector3 spawnPosition = new Vector3(
            randomX,
            spawner.transform.position.y,
            0f
        );

        GameObject newPowerUp = Instantiate(
            powerUpsPrefab,
            spawnPosition,
            Quaternion.identity
        );

        Transform booster = newPowerUp.transform.Find("Booster");
        Transform bug = newPowerUp.transform.Find("Bug");

        if (booster == null || bug == null)
        {
            Debug.LogError(
                "PowerUps prefab must contain children named Booster and Bug."
            );

            Destroy(newPowerUp);
            return;
        }

        lastSpawnedPowerUp = newPowerUp.transform;

        int selectedPowerUp = BoosterSpawnerRandomizer();

        if (selectedPowerUp == 2)
        {
            booster.gameObject.SetActive(true);
            bug.gameObject.SetActive(false);
        }
        else
        {
            booster.gameObject.SetActive(false);
            bug.gameObject.SetActive(true);
        }

        Debug.Log(selectedPowerUp == 2
            ? "Booster spawned!"
            : "Bug spawned!");

        // Generate the distance required for the next PowerUp.
        nextPowerUpGap = Random.Range(
            minPowerUpGap,
            maxPowerUpGap
        );
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

    private void DestroyPowerUpContainer()
    {
        if (Destroyer == null)
            return;

        float destroyerY = Destroyer.transform.position.y;

        GameObject[] powerUps =
            GameObject.FindGameObjectsWithTag("PowerUps");

        foreach (GameObject powerUp in powerUps)
        {
            if (powerUp == null)
                continue;

            // Only destroy PowerUps that have passed below
            // the Destroyer's Y position.
            if (powerUp.transform.position.y <= destroyerY)
            {
                Destroy(powerUp);
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

    int BoosterSpawnerRandomizer()
    {
        float roll = Random.value;
        int assignedValue = 0;

        if (roll <= 0.8)
        {
            assignedValue = 2;
        }
        else
        {
            assignedValue = 1;
        }

        return assignedValue;
    }
}
