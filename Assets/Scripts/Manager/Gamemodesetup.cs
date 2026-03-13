using UnityEngine;

/// <summary>
/// GameModeSetup
/// Attach to a GameObject in the GameScene.
/// On Start() it reads the current game mode from SceneFlowManager and
/// activates either PaddleOpponent (human 2P) or PaddleAI (AI).
///
/// Inspector Setup:
///   paddleOpponent – the GameObject with PaddleOpponent.cs
///   paddleAI       – the GameObject with PaddleAI.cs
///                   (can be the same GameObject with both components,
///                    or two separate GameObjects sharing the same position)
/// </summary>
public class GameModeSetup : MonoBehaviour
{
    [Header("Paddle GameObjects")]
    [SerializeField] private GameObject paddleOpponentObject;   // human 2P paddle
    [SerializeField] private GameObject paddleAIObject;         // AI paddle

    [Header("Optional UI")]
    [SerializeField] private GameObject aiIndicatorUI;          // "VS AI" label, badge, etc.

    void Start()
    {
        string mode = SceneFlowManager.Instance != null
            ? SceneFlowManager.Instance.GetCurrentGameMode()
            : PlayerPrefs.GetString("GameMode", SceneFlowManager.MODE_ONE_VS_ONE);

        Debug.Log($"[GameModeSetup] Mode = {mode}");

        bool isAIMode = mode == SceneFlowManager.MODE_AI_VS_PLAYER;

        // Enable the correct paddle
        if (paddleOpponentObject != null) paddleOpponentObject.SetActive(!isAIMode);
        if (paddleAIObject       != null) paddleAIObject.SetActive(isAIMode);

        // Show optional AI indicator
        if (aiIndicatorUI != null) aiIndicatorUI.SetActive(isAIMode);
    }
}