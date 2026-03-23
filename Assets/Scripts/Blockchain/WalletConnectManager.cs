// Assets/Scripts/Blockchain/WalletConnectManager.cs
// WalletConnect integration for Unity mobile app
// Replaces MetaMask deep links with WalletConnect protocol

using System;
using System.Threading.Tasks;
using UnityEngine;

#if WALLET_CONNECT_SHARP
using WalletConnectSharp;
#endif

public class WalletConnectManager : MonoBehaviour
{
    public static WalletConnectManager Instance { get; private set; }

    // ── Configuration ──────────────────────────────────────────────────────────

    private const string PROJECT_ID = "d6971ce734d61ce5b8b0838bb8f9b2ff"; // Your WalletConnect Project ID
    private const int SEPOLIA_CHAIN_ID = 11155111;
    private const string SEPOLIA_RPC_URL = "https://sepolia.infura.io/v3/YOUR_INFURA_KEY"; // Replace with your Infura key

    // ── State ──────────────────────────────────────────────────────────────────

#if WALLET_CONNECT_SHARP
    private WalletConnectUnity walletConnect;
#else
    private object walletConnect; // Stub for when package not installed
#endif
    private bool isInitialized = false;
    private string connectedAddress = "";

    // ── Events ─────────────────────────────────────────────────────────────────

    public event Action<string> OnWalletConnected;
    public event Action OnWalletDisconnected;
    public event Action<string, string> OnTransactionSent; // (txHash, itemId)
    public event Action<string> OnError;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeWalletConnect();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeWalletConnect()
    {
        try
        {
            // WalletConnect is initialized automatically by the WalletConnectUnity prefab
            // We'll get the instance when needed
            isInitialized = true;
            Debug.Log("[WalletConnect] Manager initialized successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"[WalletConnect] Manager initialization failed: {e.Message}");
            OnError?.Invoke($"WalletConnect initialization failed: {e.Message}");
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Connect to a wallet via WalletConnect
    /// </summary>
    public async void ConnectWallet()
    {
        if (!isInitialized)
        {
            OnError?.Invoke("WalletConnect not initialized");
            return;
        }

#if WALLET_CONNECT_SHARP
        try
        {
            // Get the WalletConnect instance (should be set up by WalletConnectUnity prefab)
            walletConnect = WalletConnectUnity.Instance;
            if (walletConnect == null)
            {
                OnError?.Invoke("WalletConnect instance not found. Make sure WalletConnectUnity prefab is in the scene.");
                Debug.LogError("[WalletConnect] WalletConnectUnity.Instance is null. Add WalletConnectUnity prefab to scene.");
                return;
            }

            // Subscribe to events
            walletConnect.OnConnected += OnWalletConnect;
            walletConnect.OnDisconnected += OnWalletDisconnect;

            await walletConnect.Connect();
            Debug.Log("[WalletConnect] Connection initiated");
        }
        catch (Exception e)
        {
            Debug.LogError($"[WalletConnect] Connection failed: {e.Message}");
            OnError?.Invoke($"Connection failed: {e.Message}");
        }
#else
        Debug.LogWarning("[WalletConnect] WalletConnectSharp package not installed. Please install the package first.");
        OnError?.Invoke("WalletConnect package not installed. Please install WalletConnectSharp.Unity package.");
#endif
    }

    /// <summary>
    /// Disconnect from current wallet
    /// </summary>
    public async void DisconnectWallet()
    {
#if WALLET_CONNECT_SHARP
        if (walletConnect == null)
        {
            OnWalletDisconnected?.Invoke();
            return;
        }

        try
        {
            // Unsubscribe from events
            walletConnect.OnConnected -= OnWalletConnect;
            walletConnect.OnDisconnected -= OnWalletDisconnect;

            await walletConnect.Disconnect();
            Debug.Log("[WalletConnect] Disconnected");
            OnWalletDisconnected?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[WalletConnect] Disconnect failed: {e.Message}");
        }
#else
        Debug.LogWarning("[WalletConnect] WalletConnectSharp package not installed.");
        OnWalletDisconnected?.Invoke();
#endif
    }

    /// <summary>
    /// Send a transaction for purchasing an item
    /// </summary>
    public async Task<string> SendPurchaseTransaction(string itemId, string to, string value, string data)
    {
#if WALLET_CONNECT_SHARP
        if (walletConnect == null || string.IsNullOrEmpty(connectedAddress))
        {
            throw new Exception("Wallet not connected");
        }

        try
        {
            // Create transaction object for WalletConnectSharp
            var transaction = new
            {
                from = connectedAddress,
                to = to,
                value = value,
                data = data
            };

            // Send transaction via WalletConnect
            var result = await walletConnect.SendTransaction(transaction);
            Debug.Log($"[WalletConnect] Transaction sent: {result}");

            return result.ToString();
        }
        catch (Exception e)
        {
            Debug.LogError($"[WalletConnect] Send purchase transaction failed: {e.Message}");
            throw;
        }
#else
        throw new NotImplementedException("WalletConnect package not installed. Please install WalletConnectSharp.Unity package first.");
#endif
    }

    /// <summary>
    /// Get the connected wallet address
    /// </summary>
    public string GetAddress()
    {
        return connectedAddress;
    }

    /// <summary>
    /// Check if wallet is connected
    /// </summary>
    public bool IsConnected()
    {
#if WALLET_CONNECT_SHARP
        return walletConnect != null && !string.IsNullOrEmpty(connectedAddress);
#else
        return false;
#endif
    }

    /// <summary>
    /// Get the project ID (for use by other components)
    /// </summary>
    public string GetProjectId()
    {
        return PROJECT_ID;
    }

    /// <summary>
    /// Event handler for wallet connection
    /// </summary>
#if WALLET_CONNECT_SHARP
    private void OnWalletConnect(object sender, EventArgs e)
    {
        // Get the connected address from the session
        if (walletConnect != null && walletConnect.Session != null)
        {
            connectedAddress = walletConnect.Session.Accounts[0];
            Debug.Log($"[WalletConnect] Connected to wallet: {connectedAddress}");
            OnWalletConnected?.Invoke(connectedAddress);
        }
    }

    /// <summary>
    /// Event handler for wallet disconnection
    /// </summary>
    private void OnWalletDisconnect(object sender, EventArgs e)
    {
        connectedAddress = null;
        Debug.Log("[WalletConnect] Disconnected from wallet");
        OnWalletDisconnected?.Invoke();
    }
#else
    private void OnWalletConnect(object sender, EventArgs e) { }
    private void OnWalletDisconnect(object sender, EventArgs e) { }
#endif
}