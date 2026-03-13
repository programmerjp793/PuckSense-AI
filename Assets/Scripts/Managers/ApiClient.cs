// ApiClient.cs
// Centralized HTTP client for all backend API calls.

using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance { get; private set; }

    [Header("Backend Configuration")]
    [Tooltip("Base URL of your Node.js backend. No trailing slash.")]
    public string backendBaseUrl = "https://airhockey-backend.onrender.com/api";

    [Header("Settings")]
    public int timeoutSeconds = 30;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task<string> GetAsync(string endpoint)
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] GET {url}");

        using var request = UnityWebRequest.Get(url);
        InjectAuthHeader(request);
        request.timeout = timeoutSeconds;

        await SendAsync(request);

        if (IsError(request))
            throw new Exception($"GET {endpoint} [{request.responseCode}]: {request.downloadHandler.text}");

        return request.downloadHandler.text;
    }

    public async Task<string> PostAsync(string endpoint, string jsonBody = "{}")
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] POST {url}");

        byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        InjectAuthHeader(request);
        request.timeout = timeoutSeconds;

        await SendAsync(request);

        if (IsError(request))
            throw new Exception($"POST {endpoint} [{request.responseCode}]: {request.downloadHandler.text}");

        return request.downloadHandler.text;
    }

    public async Task<string> PutAsync(string endpoint, string jsonBody = "{}")
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] PUT {url}");

        byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);

        using var request = new UnityWebRequest(url, "PUT");
        request.uploadHandler   = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        InjectAuthHeader(request);
        request.timeout = timeoutSeconds;

        await SendAsync(request);

        if (IsError(request))
            throw new Exception($"PUT {endpoint} [{request.responseCode}]: {request.downloadHandler.text}");

        return request.downloadHandler.text;
    }

    private void InjectAuthHeader(UnityWebRequest request)
    {
        // Use WalletAuthManager instead of AuthManager
        string token = WalletAuthManager.Instance?.JwtToken
                    ?? PlayerPrefs.GetString("JWT_Token", "");

        if (!string.IsNullOrEmpty(token))
            request.SetRequestHeader("Authorization", "Bearer " + token);
    }

    private Task SendAsync(UnityWebRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        request.SendWebRequest().completed += _ => tcs.SetResult(true);
        return tcs.Task;
    }

    private bool IsError(UnityWebRequest request) =>
        request.result == UnityWebRequest.Result.ConnectionError    ||
        request.result == UnityWebRequest.Result.ProtocolError      ||
        request.result == UnityWebRequest.Result.DataProcessingError;
}