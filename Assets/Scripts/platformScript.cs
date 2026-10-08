using UnityEngine;

public class platformScript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public float startingSpeed = 0.0000002f;
    public float maxSpeed = 8f;
    public float speedIncreaseRate = 0.1f;
    private float currentSpeed;
    void Start()
    {
        currentSpeed = startingSpeed;
    }

    // Update is called once per frame
    void Update()
    {
        //platformMovement();
    }

    void platformMovement()
    {
        currentSpeed += speedIncreaseRate * Time.deltaTime;
        currentSpeed = Mathf.Clamp(currentSpeed, startingSpeed, maxSpeed);
        transform.position += Vector3.down * currentSpeed * Time.deltaTime;
    }
}
