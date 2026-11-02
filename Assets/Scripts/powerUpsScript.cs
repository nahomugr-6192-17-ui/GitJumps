using UnityEngine;

public class powerUpsMovementScript : MonoBehaviour
{
    public platformSpawnerScript spawnerController;
    public playerScript playerScript;
    public float destroyY = -12f;

    void Start()
    {
        if (spawnerController == null)
        {
            spawnerController =
                FindAnyObjectByType<platformSpawnerScript>();
        }

        if (playerScript == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");

            if (playerObject != null)
            {
                playerScript = playerObject.GetComponent<playerScript>();
            }
        }
    }

    void Update()
    {
        if (spawnerController == null || playerScript == null)
            return;

        if (playerScript.currentScore < 1)
            return;

        float speed = spawnerController.CurrentPlatformSpeed;

        transform.position +=
            Vector3.down * speed * Time.deltaTime;

        if (transform.position.y <= destroyY)
        {
            Destroy(gameObject);
        }
    }
}