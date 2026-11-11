using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class HowToPlay : MonoBehaviour
{
    public UIDocument HowToPlayScene;

    private Button PlayButtonInHTP;

    private const string TutorialCompletedKey = "HasCompletedHowToPlay";

    void Start()
    {
        if (HowToPlayScene == null)
        {
            Debug.LogError("HowToPlayScene UIDocument is not assigned!");
            return;
        }

        PlayButtonInHTP =
            HowToPlayScene.rootVisualElement.Q<Button>("PlayButtonInHTP");

        if (PlayButtonInHTP == null)
        {
            Debug.LogError("PlayButtonInHTP was not found in the UXML!");
            return;
        }

        PlayButtonInHTP.clicked += OnHowToPlayCompleted;
    }

    private void OnHowToPlayCompleted()
    {
        PlayerPrefs.SetInt(TutorialCompletedKey, 1);
        PlayerPrefs.Save();

        SceneManager.LoadScene("GamePlayScene");
    }
}
