using UnityEngine;

public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;

    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
            }

            if (_instance == null)
            {
                var prefab = Resources.Load<GameObject>("AudioManager");
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    _instance = go.GetComponent<AudioManager>();
                }
            }

            return _instance;
        }
        private set
        {
            _instance = value;
        }
    }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("Audio Clips")]
    [SerializeField] private AudioClip bgMusicClip;
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip gameOverClip;

    [Header("Volume Configuration")]
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.8f;

    public AudioSource MusicSource => musicSource;
    public AudioSource SfxSource => sfxSource;
    public AudioClip BgMusicClip => bgMusicClip;
    public AudioClip JumpClip => jumpClip;
    public AudioClip GameOverClip => gameOverClip;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);
        SetupAudioSources();
    }

    private void SetupAudioSources()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.volume = musicVolume;
        musicSource.spatialBlend = 0f; // 2D stereo/mono audio

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.volume = sfxVolume;
        sfxSource.spatialBlend = 0f; // 2D audio
    }

    private void Start()
    {
        PlayBackgroundMusic();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public void PlayBackgroundMusic()
    {
        if (musicSource == null || bgMusicClip == null)
            return;

        // If already playing this track, continue playing seamlessly without restarting
        if (musicSource.isPlaying && musicSource.clip == bgMusicClip)
            return;

        musicSource.clip = bgMusicClip;
        musicSource.loop = true;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    public void StopBackgroundMusic()
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            musicSource.Stop();
        }
    }

    public void PlayJump()
    {
        if (sfxSource != null && jumpClip != null)
        {
            sfxSource.PlayOneShot(jumpClip, sfxVolume);
        }
    }

    public void PlayGameOver()
    {
        StopBackgroundMusic();

        if (sfxSource != null && gameOverClip != null)
        {
            sfxSource.PlayOneShot(gameOverClip, sfxVolume);
        }
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        if (sfxSource != null)
        {
            sfxSource.volume = sfxVolume;
        }
    }
}
