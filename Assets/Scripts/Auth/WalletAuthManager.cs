using UnityEngine;
using UnityEngine.Events;
using Newtonsoft.Json;

public class WalletAuthManager : MonoBehaviour
{
    public static WalletAuthManager Instance { get; private set; }

    [HideInInspector] public UnityEvent<PlayerProfile> OnLoginSuccess = new();
    [HideInInspector] public UnityEvent<string> OnLoginFailed = new();

    public PlayerProfile CurrentPlayer { get; private set; }
    public string JwtToken { get; private set; }
    public bool IsLoggedIn => CurrentPlayer != null && !string.IsNullOrEmpty(JwtToken);

    private const string PREF_JWT = "JWT_Token";
    private const string PREF_PLAYER = "Player_Profile";
    private const string PREF_WALLET = "Wallet_Address";

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        TryAutoLogin();
    }

    public void TryAutoLogin()
    {
        string savedJwt = PlayerPrefs.GetString(PREF_JWT, "");
        string savedProfile = PlayerPrefs.GetString(PREF_PLAYER, "");

        if (!string.IsNullOrEmpty(savedJwt) && !string.IsNullOrEmpty(savedProfile))
        {
            JwtToken = savedJwt;
            CurrentPlayer = JsonConvert.DeserializeObject<PlayerProfile>(savedProfile);
            Debug.Log($"[Auth] Session restored: {CurrentPlayer?.username}");
            OnLoginSuccess.Invoke(CurrentPlayer);
        }
    }

    public async void LoginWithWallet(string walletAddress)
    {
        try
        {
            string body = JsonConvert.SerializeObject(new { walletAddress });
            string json = await ApiClient.Instance.PostAsync("/auth/wallet-login", body);
            var response = JsonConvert.DeserializeObject<LoginResponse>(json);

            if (response?.success != true)
                throw new System.Exception(response?.message ?? "Login failed");

            JwtToken = response.token;
            CurrentPlayer = response.player;

            PlayerPrefs.SetString(PREF_JWT, JwtToken);
            PlayerPrefs.SetString(PREF_PLAYER, JsonConvert.SerializeObject(CurrentPlayer));
            PlayerPrefs.SetString(PREF_WALLET, walletAddress);
            PlayerPrefs.Save();

            Debug.Log($"[Auth] ✅ Wallet login success: {CurrentPlayer?.username}");
            OnLoginSuccess.Invoke(CurrentPlayer);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Auth] Wallet login failed: {e.Message}");
            OnLoginFailed.Invoke(e.Message);
        }
    }

    public void LoginAsGuest()
    {
        CurrentPlayer = new PlayerProfile
        {
            username = "Guest",
            walletAddress = null
        };
        Debug.Log("[Auth] Guest mode");
        OnLoginSuccess.Invoke(CurrentPlayer);
    }

    public void Logout()
    {
        CurrentPlayer = null;
        JwtToken = null;
        PlayerPrefs.DeleteKey(PREF_JWT);
        PlayerPrefs.DeleteKey(PREF_PLAYER);
        PlayerPrefs.DeleteKey(PREF_WALLET);
        PlayerPrefs.Save();
    }

    [System.Serializable]
    private class LoginResponse
    {
        public bool success;
        public string message;
        public string token;
        public PlayerProfile player;
    }
}