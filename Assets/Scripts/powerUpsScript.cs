using UnityEngine;

public class powerUpsMovementScript : MonoBehaviour
{
    public platformSpawnerScript spawnerController;
    public playerScript playerScript;

    [Header("Horizontal Movement")]
    public bool moveHorizontally = false;
    public float horizontalSpeed = 3f;
    public float minX = -8f;
    public float maxX = 8f;

    private int horizontalDirection = 1;

    void Start()
    {
        if (spawnerController == null)
        {
            spawnerController =
                FindAnyObjectByType<platformSpawnerScript>();
        }

        if (playerScript == null)
        {
            GameObject playerObject =
                GameObject.FindWithTag("Player");

            if (playerObject != null)
            {
                playerScript =
                    playerObject.GetComponent<playerScript>();
            }
        }
    }

    void Update()
    {
        if (spawnerController == null || playerScript == null)
            return;

        if (playerScript.currentScore < 1)
            return;

        // Existing downward movement.
        float speed = spawnerController.CurrentPlatformSpeed;

        transform.position +=
            Vector3.down * speed * Time.deltaTime;

        // Horizontal movement for selected PowerUps.
        if (moveHorizontally)
        {
            float newX = transform.position.x
                + horizontalDirection * horizontalSpeed
                * Time.deltaTime;

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

            transform.position = new Vector3(
                newX,
                transform.position.y,
                transform.position.z
            );
        }
    }
}
