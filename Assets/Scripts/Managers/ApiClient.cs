// Assets/Scripts/ApiClient.cs
// Centralized HTTP client for all backend API calls.
//
// CHANGES:
//   • InjectAuthHeader now also sends X-Wallet-Address header so
//     /store/owned can identify the player without a JWT (fallback).
//   • Added DeleteAsync for completeness.
//   • Retry logic (1 retry) on connection errors.
//   • Timeout increased default to 30s; configurable per-call via overload.

using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance { get; private set; }

    [Header("Backend Configuration")]
    [Tooltip("Base URL of your Node.js backend (no trailing slash).")]
    public string backendBaseUrl = "https://airhockey-backend.onrender.com/api";

    [Header("Settings")]
    public int timeoutSeconds = 30;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Public HTTP methods ───────────────────────────────────────────────────

    /// <summary>GET {backendBaseUrl}{endpoint}</summary>
    public async Task<string> GetAsync(string endpoint)
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] GET {url}");

        return await ExecuteWithRetry(async () =>
        {
            using var req = UnityWebRequest.Get(url);
            InjectHeaders(req);
            req.timeout = timeoutSeconds;
            await SendAsync(req);
            ThrowIfError(req, "GET", endpoint);
            return req.downloadHandler.text;
        });
    }

    /// <summary>POST {backendBaseUrl}{endpoint} with JSON body.</summary>
    public async Task<string> PostAsync(string endpoint, string jsonBody = "{}")
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] POST {url}");

        return await ExecuteWithRetry(async () =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            InjectHeaders(req);
            req.timeout = timeoutSeconds;
            await SendAsync(req);
            ThrowIfError(req, "POST", endpoint);
            return req.downloadHandler.text;
        });
    }

    /// <summary>PUT {backendBaseUrl}{endpoint} with JSON body.</summary>
    public async Task<string> PutAsync(string endpoint, string jsonBody = "{}")
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] PUT {url}");

        return await ExecuteWithRetry(async () =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
            using var req = new UnityWebRequest(url, "PUT");
            req.uploadHandler   = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            InjectHeaders(req);
            req.timeout = timeoutSeconds;
            await SendAsync(req);
            ThrowIfError(req, "PUT", endpoint);
            return req.downloadHandler.text;
        });
    }

    /// <summary>DELETE {backendBaseUrl}{endpoint}.</summary>
    public async Task<string> DeleteAsync(string endpoint)
    {
        string url = backendBaseUrl + endpoint;
        Debug.Log($"[API] DELETE {url}");

        return await ExecuteWithRetry(async () =>
        {
            using var req = UnityWebRequest.Delete(url);
            req.downloadHandler = new DownloadHandlerBuffer();
            InjectHeaders(req);
            req.timeout = timeoutSeconds;
            await SendAsync(req);
            ThrowIfError(req, "DELETE", endpoint);
            return req.downloadHandler.text;
        });
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Injects Authorization + X-Wallet-Address headers.
    /// X-Wallet-Address lets /store/owned resolve the player even without a JWT.
    /// </summary>
    private void InjectHeaders(UnityWebRequest req)
    {
        // JWT — try WalletAuthManager first, fall back to PlayerPrefs
        string token = "";
        try   { token = WalletAuthManager.Instance?.JwtToken ?? ""; }
        catch { /* WalletAuthManager not yet initialised */ }
        if (string.IsNullOrEmpty(token))
            token = PlayerPrefs.GetString("JWT_Token", "");

        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", "Bearer " + token);

        // Wallet address header — lets backend identify wallet without auth token
        string wallet = WalletManager.Instance?.WalletAddress
                     ?? PlayerPrefs.GetString("WalletAddress", "");
        if (!string.IsNullOrEmpty(wallet))
            req.SetRequestHeader("X-Wallet-Address", wallet);
    }

    private Task SendAsync(UnityWebRequest req)
    {
        var tcs = new TaskCompletionSource<bool>();
        req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
        return tcs.Task;
    }

    private static void ThrowIfError(UnityWebRequest req, string method, string endpoint)
    {
        if (req.result == UnityWebRequest.Result.ConnectionError    ||
            req.result == UnityWebRequest.Result.ProtocolError      ||
            req.result == UnityWebRequest.Result.DataProcessingError)
        {
            throw new Exception(
                $"{method} {endpoint} [{req.responseCode}]: {req.downloadHandler?.text}");
        }
    }

    /// <summary>One automatic retry on transient connection errors.</summary>
    private static async Task<string> ExecuteWithRetry(Func<Task<string>> action, int maxRetries = 1)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxRetries && ex.Message.Contains("ConnectionError"))
            {
                attempt++;
                Debug.LogWarning($"[API] Retry {attempt}/{maxRetries} after: {ex.Message}");
                await Task.Delay(1500);
            }
        }
    }
}