using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }
    
    [Header("Volume Settings")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TMP_Text masterVolumeText;
    [SerializeField] private TMP_Text musicVolumeText;
    [SerializeField] private TMP_Text sfxVolumeText;
    
    [Header("Sensitivity Settings")]
    [SerializeField] private Slider paddleSensitivitySlider;
    [SerializeField] private TMP_Text sensitivityText;
    
    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    
    // Settings values
    private float masterVolume = 1f;
    private float musicVolume = 0.7f;
    private float sfxVolume = 0.8f;
    private float paddleSensitivity = 1f;
    
    void Awake()
    {
        // Singleton pattern with DontDestroyOnLoad
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        LoadSettings();
        InitializeUI();
    }
    
    void InitializeUI()
    {
        // Setup volume sliders
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = masterVolume;
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }
        
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = musicVolume;
            musicVolumeSlider.onValueChanged.AddListener(SetMusicVolume);
        }
        
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = sfxVolume;
            sfxVolumeSlider.onValueChanged.AddListener(SetSFXVolume);
        }
        
        // Setup sensitivity slider
        if (paddleSensitivitySlider != null)
        {
            paddleSensitivitySlider.value = paddleSensitivity;
            paddleSensitivitySlider.onValueChanged.AddListener(SetPaddleSensitivity);
        }
        
        // Update all displays
        UpdateVolumeDisplays();
        UpdateSensitivityDisplay();
    }
    
    public void SetMasterVolume(float value)
    {
        masterVolume = value;
        UpdateVolumeDisplays();
        ApplyAudioSettings();
        SaveSettings();
    }
    
    public void SetMusicVolume(float value)
    {
        musicVolume = value;
        UpdateVolumeDisplays();
        ApplyAudioSettings();
        SaveSettings();
    }
    
    public void SetSFXVolume(float value)
    {
        sfxVolume = value;
        UpdateVolumeDisplays();
        ApplyAudioSettings();
        SaveSettings();
    }
    
    public void SetPaddleSensitivity(float value)
    {
        paddleSensitivity = value;
        UpdateSensitivityDisplay();
        SaveSettings();
    }
    
    void UpdateVolumeDisplays()
    {
        if (masterVolumeText != null)
            masterVolumeText.text = Mathf.RoundToInt(masterVolume * 100) + "%";
        
        if (musicVolumeText != null)
            musicVolumeText.text = Mathf.RoundToInt(musicVolume * 100) + "%";
        
        if (sfxVolumeText != null)
            sfxVolumeText.text = Mathf.RoundToInt(sfxVolume * 100) + "%";
    }
    
    void UpdateSensitivityDisplay()
    {
        if (sensitivityText != null)
            sensitivityText.text = paddleSensitivity.ToString("F1") + "x";
    }
    
    void ApplyAudioSettings()
    {
        float finalMusicVolume = masterVolume * musicVolume;
        float finalSFXVolume = masterVolume * sfxVolume;
        
        if (musicSource != null)
            musicSource.volume = finalMusicVolume;
        
        if (sfxSource != null)
            sfxSource.volume = finalSFXVolume;
        
        // Set global audio listener volume
        AudioListener.volume = masterVolume;
    }
    
    void SaveSettings()
    {
        PlayerPrefs.SetFloat("MasterVolume", masterVolume);
        PlayerPrefs.SetFloat("MusicVolume", musicVolume);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolume);
        PlayerPrefs.SetFloat("PaddleSensitivity", paddleSensitivity);
        PlayerPrefs.Save();
        
        Debug.Log("Settings saved");
    }
    
    void LoadSettings()
    {
        masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        musicVolume = PlayerPrefs.GetFloat("MusicVolume", 0.7f);
        sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 0.8f);
        paddleSensitivity = PlayerPrefs.GetFloat("PaddleSensitivity", 1f);
        
        ApplyAudioSettings();
        
        Debug.Log("Settings loaded");
    }
    
    public float GetPaddleSensitivity()
    {
        return paddleSensitivity;
    }
    
    public void ResetToDefaults()
    {
        SetMasterVolume(1f);
        SetMusicVolume(0.7f);
        SetSFXVolume(0.8f);
        SetPaddleSensitivity(1f);
        
        // Update sliders
        if (masterVolumeSlider != null) masterVolumeSlider.value = 1f;
        if (musicVolumeSlider != null) musicVolumeSlider.value = 0.7f;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = 0.8f;
        if (paddleSensitivitySlider != null) paddleSensitivitySlider.value = 1f;
        
        Debug.Log("Settings reset to defaults");
    }
}