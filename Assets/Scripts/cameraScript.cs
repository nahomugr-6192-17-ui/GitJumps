using UnityEngine;

public class cameraScript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public Transform player;
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        if (player.position.y >= 0f)
        {
            transform.position = new Vector3(0f, player.position.y, transform.position.z);
        }
    }
}
