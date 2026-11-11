using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class HomeSceenScript : MonoBehaviour
{
    [Header("Home Scene UI")]
    public UIDocument HomeScene;

    private Button PlayButton;
    private Button HowToButton;
    private Button QuitButton;

    private const string TutorialCompletedKey = "HasCompletedHowToPlay";

    [Header("Player Movement")]
    public Rigidbody2D playerRb;

    public float jumpForce = 8.6f;
    public float jumpForceMultiplier = 1.1f;
    public float moveSpeed = 7f;

    [Header("Movement Smoothing")]
    public float horizontalAcceleration = 85f;
    public float horizontalDeceleration = 100f;

    [Header("Jump Control")]
    [Range(0.1f, 1f)]
    public float jumpCutMultiplier = 0.5f;

    private bool jumpWasHeld;
    private bool isGrounded;


    void Start()
    {
        // Register UI button callbacks once.
        PlayButton = HomeScene.rootVisualElement.Q<Button>("PlayButton");
        HowToButton = HomeScene.rootVisualElement.Q<Button>("HowToButton");
        QuitButton = HomeScene.rootVisualElement.Q<Button>("QuitButton");

        PlayButton.RegisterCallback<ClickEvent>(OnPlayClicked);
        HowToButton.RegisterCallback<ClickEvent>(OnHowToClicked);
        QuitButton.RegisterCallback<ClickEvent>(OnQuitClicked);
    }

    void Update()
    {
        if (playerRb == null || Keyboard.current == null)
            return;

        // Horizontal input.
        bool leftPressed =
            Keyboard.current.aKey.isPressed ||
            Keyboard.current.leftArrowKey.isPressed;

        bool rightPressed =
            Keyboard.current.dKey.isPressed ||
            Keyboard.current.rightArrowKey.isPressed;

        // Jump input.
        bool jumpHeld =
            Keyboard.current.spaceKey.isPressed ||
            Keyboard.current.wKey.isPressed ||
            Keyboard.current.upArrowKey.isPressed;

        bool jumpPressed =
            Keyboard.current.wKey.isPressed ||
            Keyboard.current.upArrowKey.isPressed;

        // Smooth horizontal acceleration and deceleration.
        float targetSpeed = 0f;

        if (leftPressed && !rightPressed)
            targetSpeed = -moveSpeed;
        else if (rightPressed && !leftPressed)
            targetSpeed = moveSpeed;

        float movementRate = targetSpeed == 0f
            ? horizontalDeceleration
            : horizontalAcceleration;

        playerRb.linearVelocityX = Mathf.MoveTowards(
            playerRb.linearVelocityX,
            targetSpeed,
            movementRate * Time.deltaTime
        );

        // Jump when grounded.
        if (jumpPressed && isGrounded)
        {
            Jump();
        }

        // Variable jump height.
        if (jumpWasHeld && !jumpHeld && playerRb.linearVelocityY > 0f)
        {
            playerRb.linearVelocityY *= jumpCutMultiplier;
        }

        jumpWasHeld = jumpHeld;
    }

    private void Jump()
    {
        isGrounded = false;

        playerRb.linearVelocityY = 0f;

        playerRb.AddForce(
            Vector2.up * jumpForce * jumpForceMultiplier,
            ForceMode2D.Impulse
        );
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // The home screen player can jump only when touching the ground.
        if (collision.gameObject.CompareTag("Floor") || collision.gameObject.CompareTag("Platform"))
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y > 0.5f)
                {
                    isGrounded = true;
                    break;
                }
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Floor"))
        {
            isGrounded = false;
        }
    }

    private void OnPlayClicked(ClickEvent ev)
    {
        bool tutorialCompleted =
            PlayerPrefs.GetInt(TutorialCompletedKey, 0) == 1;

        if (tutorialCompleted)
        {
            SceneManager.LoadScene("GamePlayScene");
        }
        else
        {
            SceneManager.LoadScene("HowToPlayScene");
        }
    }

    private void OnHowToClicked(ClickEvent ev)
    {
        SceneManager.LoadScene("HowToPlayScene");
    }

    private void OnQuitClicked(ClickEvent ev)
    {
        Application.Quit();
    }
}