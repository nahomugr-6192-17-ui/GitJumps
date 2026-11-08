using UnityEngine;

public class starScript : MonoBehaviour
{
    [Header("Spawner Reference")]
    public platformSpawnerScript spawnerController;

    [Header("Speed Settings")]
    public float speedOffset = 1f;

    [Header("Despawn Settings")]
    public float despawnY = -11.5f;

    public void Initialize(platformSpawnerScript spawner)
    {
        spawnerController = spawner;
        if (spawnerController != null)
        {
            speedOffset = spawnerController.starSpeedOffset;
            if (spawnerController.Destroyer != null)
            {
                despawnY = spawnerController.Destroyer.transform.position.y - 1.5f;
            }
        }
    }

    private void Start()
    {
        if (spawnerController == null)
        {
            spawnerController = FindAnyObjectByType<platformSpawnerScript>();
        }

        if (spawnerController != null)
        {
            speedOffset = spawnerController.starSpeedOffset;
            if (spawnerController.Destroyer != null)
            {
                despawnY = spawnerController.Destroyer.transform.position.y - 1.5f;
            }
        }
    }

    private void Update()
    {
        if (spawnerController != null)
        {
            // Halt stars on game over when platform movement stops
            if (spawnerController.basePlatformSpeed <= 0f)
                return;

            float currentOffset = spawnerController.starSpeedOffset;
            float speed = spawnerController.CurrentPlatformSpeed + currentOffset;
            transform.position += Vector3.down * speed * Time.deltaTime;
        }

        if (transform.position.y <= despawnY)
        {
            Destroy(gameObject);
        }
    }
}
