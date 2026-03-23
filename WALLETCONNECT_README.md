# WalletConnect Integration for Air Hockey Game

This document outlines the step-by-step implementation of WalletConnect integration to replace MetaMask deep links with proper WalletConnect protocol for mobile blockchain purchases.

## Overview

The integration allows players to connect their wallets (MetaMask, Trust Wallet, etc.) via WalletConnect and make purchases on the Sepolia testnet without leaving the Unity mobile app.

## Architecture

```
Unity Game
    │
    ▼
WalletConnect SDK ──────┐
    │                   │
    ▼                   ▼
WalletConnect Modal  Player's Wallet App
    │                   │
    ▼                   ▼
WalletConnect Protocol  Signs Transaction
    │                   │
    ▼                   ▼
Backend API ────────────┼─► Sepolia Network
    │                   │
    ▼                   ▼
Smart Contract        Transaction Confirmed
```

## Prerequisites

1. **WalletConnect Project ID**: Get from [WalletConnect Cloud](https://cloud.walletconnect.com/)
2. **Infura API Key**: For Sepolia RPC (if not using default)
3. **Smart Contract**: Deployed on Sepolia testnet
4. **Backend API**: Running with purchase endpoints

## Setup Steps

### 1. Install WalletConnect SDK

The WalletConnect Unity SDK has been updated to use WalletConnectSharp.Unity in `Packages/manifest.json`:

```json
"dependencies": {
  "com.walletconnect.unity": "https://github.com/WalletConnect/WalletConnectSharp.git#unity-package",
  // ... other dependencies
}
```

**Note**: We switched from `web3modal` to `WalletConnectSharp.Unity` due to prefab availability and better Unity integration.

### 3. Add WalletConnect to Scene

**CRITICAL STEP**: The WalletConnectSharp.Unity SDK requires a prefab to be added to your scene:

1. After importing the package, find the `WalletConnectUnity` prefab in:
   - `Packages/com.walletconnect.unity/Runtime/Prefabs/WalletConnectUnity.prefab`
2. Drag the `WalletConnectUnity` prefab into your LoginScene (or whichever scene handles wallet connection)
3. Configure the prefab with your Project ID: `d6971ce734d61ce5b8b0838bb8f9b2ff`
4. **IMPORTANT**: Add the compilation symbol `WALLET_CONNECT_SHARP` to enable full functionality:
   - Go to `Edit → Project Settings → Player → Other Settings → Scripting Define Symbols`
   - Add `WALLET_CONNECT_SHARP` to the list
5. The prefab automatically manages the WalletConnect session and provides the `WalletConnectUnity.Instance` singleton

### 3. Configure WalletConnect

The WalletConnectManager is pre-configured with:
- **Project ID**: `d6971ce734d61ce5b8b0838bb8f9b2ff` ✅
- **Network**: Sepolia testnet (chain ID: 11155111)
- **RPC Methods**: `eth_sendTransaction`, `personal_sign`, `eth_sign`

### 4. Implementation Status

✅ **Completed:**
- WalletConnectManager.cs with full API implementation
- WalletManager.cs updated to use WalletConnect
- WalletConnectUI.cs with event handling
- Backend API endpoints for transaction preparation
- Package configuration in manifest.json
- Comprehensive documentation

🔄 **Next Steps:**
- Add WalletConnectUnity prefab to LoginScene
- Test package installation in Unity Editor
- Verify wallet connection on mobile device
- Test end-to-end purchase flow

### 5. Backend API Updates

The backend has been updated with a new endpoint:

- `POST /api/purchase/prepare-tx` - Returns transaction data for WalletConnect

New blockchain service function:
- `prepareStorePurchaseTxForWalletConnect()` - Returns encoded transaction data

### 6. Unity Integration

#### WalletConnectManager.cs
- Handles WalletConnect session management
- Sends transaction requests to connected wallets
- Manages network switching to Sepolia

#### Updated WalletManager.cs
- Uses WalletConnectManager for wallet connection
- Sends transactions via WalletConnect instead of deep links
- Maintains compatibility with existing purchase flow

#### Updated WalletConnectUI.cs
- Instantiates WalletConnectManager if needed
- Shows connection status and handles WalletConnect events
- Updates UI based on connection state

## Flow Changes

### Old Flow (MetaMask Deep Links)
1. Unity → Backend: Prepare transaction
2. Backend → Unity: MetaMask deep link
3. Unity: Open MetaMask app
4. Player: Sign in MetaMask
5. MetaMask: Return txHash via deep link
6. Unity: Submit txHash to backend

### New Flow (WalletConnect)
1. Unity → Backend: Prepare transaction
2. Backend → Unity: Transaction data (to, value, data)
3. Unity → WalletConnect: Send transaction request
4. Wallet App: Show approval UI
5. Player: Approve transaction
6. Wallet App → Unity: Return txHash
7. Unity: Submit txHash to backend

## Testing Checklist

### Before Testing
- [ ] WalletConnect Project ID configured
- [ ] Backend running on correct port
- [ ] Smart contract deployed on Sepolia
- [ ] Player has test ETH on Sepolia
- [ ] Wallet app supports WalletConnect (MetaMask, Trust Wallet, etc.)
- [ ] **WalletConnectSharp.Unity package installed in Unity**
- [ ] **WalletConnectUnity prefab added to LoginScene**
- [ ] **WALLET_CONNECT_SHARP compilation symbol added to Player Settings**

### Connection Testing
- [ ] Tap "Connect Wallet" in game
- [ ] WalletConnect modal appears
- [ ] Select wallet app
- [ ] Approve connection in wallet
- [ ] Wallet address appears in UI
- [ ] Balance loads correctly

### Purchase Testing
- [ ] Select item to purchase
- [ ] Confirm purchase
- [ ] Transaction request sent to wallet
- [ ] Approve transaction in wallet
- [ ] Transaction hash received
- [ ] Backend confirms transaction
- [ ] Item added to player inventory

## Error Handling

The integration includes comprehensive error handling:

- Network switching failures
- Transaction rejections
- Backend communication errors
- Wallet disconnection
- Invalid transaction data

## Security Notes

- Unity app never holds private keys
- All signing happens in the wallet app
- Transaction data is validated by backend
- No sensitive data stored in Unity

## Troubleshooting

### Common Issues

1. **"WalletConnect not initialized"**
   - Check Project ID is set correctly
   - Ensure WalletConnectManager is in scene

2. **"Transaction failed"**
   - Verify player has sufficient ETH
   - Check network is Sepolia
   - Confirm contract address is correct

3. **"Connection timeout"**
   - Check internet connection
   - Verify wallet app supports WalletConnect
   - Try different wallet app

### Debug Logs

Enable debug logging in Unity Console to see:
- WalletConnect connection events
- Transaction requests/responses
- Backend API calls
- Error messages

## API Reference

### WalletConnectManager

```csharp
// Connect wallet
WalletConnectManager.Instance.ConnectWallet();

// Send transaction
await WalletConnectManager.Instance.SendTransaction(to, value, data, gasLimit);

// Check connection
bool isConnected = WalletConnectManager.Instance.IsConnected;
string address = WalletConnectManager.Instance.WalletAddress;
```

### Backend Endpoints

```javascript
// Prepare transaction for WalletConnect
POST /api/purchase/prepare-tx
{
  "itemId": 1,
  "walletAddress": "0x..."
}

// Response
{
  "success": true,
  "storeAddress": "0x...",
  "priceETHWei": "1000000000000000",
  "encodedData": "0xe7fb74c70000000000000000000000000000000000000000000000000000000000000001",
  "gasLimit": "120000",
  // ... other fields
}
```

## Next Steps

1. Test on actual mobile device
2. Add more wallet app support
3. Implement transaction history
4. Add gas estimation features
5. Support multiple networks