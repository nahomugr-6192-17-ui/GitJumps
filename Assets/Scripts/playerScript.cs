using UnityEngine;
using UnityEngine.InputSystem;

public class playerScript : MonoBehaviour
{
    private Rigidbody2D rb;
    public float jumpForce = 8.6f;
    public float jumpForceMultiplier = 1.1f;
    public float moveSpeed = 8f;
    public bool isGrounded = false;
    public GameObject ground;
    private GameObject camera;
    /*public GameObject platform1;
    public GameObject platform2;*/
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        camera = GameObject.Find("Main Camera");
    }

    // Update is called once per frame
    void Update()
    {
        if ((Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) && isGrounded == true)
        {
            Jump();
        }
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
        {
            rb.linearVelocityX = -moveSpeed;
        }
        else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
        {
            rb.linearVelocityX = moveSpeed;
        }
        else
        {
            rb.linearVelocityX = 0;
        }

        /*float distance = Vector3.Distance(
            platform1.transform.position,
            platform2.transform.position
        );
        Debug.Log("Distance: " + distance);*/
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor") || collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = true;
        }
    }

    // Update is called once per frame
    void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor") || collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = false;
        }
    }

    private void Jump()
    {
        isGrounded = false;
        rb.linearVelocityY = 0f;
        rb.AddForce(Vector2.up * jumpForce * jumpForceMultiplier, ForceMode2D.Impulse);

        int jumpCount = PlayerPrefs.GetInt("JumpCount", 0);
        jumpCount++;
        PlayerPrefs.SetInt("JumpCount", jumpCount);
        if (jumpCount == 2)
        {
            jumpForceMultiplier = 1.3f;
            jumpCount = 0;
            PlayerPrefs.SetInt("JumpCount", jumpCount);
        }
        else
        {
            jumpForceMultiplier = 1.1f;
        }
    }
}
