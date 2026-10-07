using UnityEngine;
using UnityEngine.InputSystem;

public class playerScript : MonoBehaviour
{
    private Rigidbody2D rb;
    public float jumpForce = 10f;
    public float moveSpeed = 8f;
    bool isGrounded = false;
    public GameObject platform1;
    public GameObject platform2;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        if ((Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame) && isGrounded == true)
        {
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
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

        float distance = Vector3.Distance(
            platform1.transform.position,
            platform2.transform.position
        );
        Debug.Log("Distance: " + distance);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor") || collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = true;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
            {
                rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            }
        }
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor") || collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = false;
        }
    }
}
