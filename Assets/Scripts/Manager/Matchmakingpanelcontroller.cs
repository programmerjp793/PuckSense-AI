using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add this button wiring to your existing HomeScreenManager,
/// OR attach this as a separate component on MatchmakingPanel.
///
/// In the Unity Inspector, assign:
///   - oneVsOneButton  → the existing "1v1" button
///   - aiVsPlayerButton → the new "AI vs Player" button
/// Both buttons call SceneFlowManager to load the GameScene
/// with the appropriate game mode string.
/// </summary>
public class MatchmakingPanelController : MonoBehaviour
{
    [Header("Matchmaking Buttons")]
    [SerializeField] private Button oneVsOneButton;
    [SerializeField] private Button aiVsPlayerButton;

    [Header("Optional: Back Button")]
    [SerializeField] private Button backButton;

    void Start()
    {
        if (oneVsOneButton != null)
            oneVsOneButton.onClick.AddListener(OnOneVsOneClicked);
        else
            Debug.LogWarning("[MatchmakingPanelController] oneVsOneButton not assigned.");

        if (aiVsPlayerButton != null)
            aiVsPlayerButton.onClick.AddListener(OnAIvsPlayerClicked);
        else
            Debug.LogWarning("[MatchmakingPanelController] aiVsPlayerButton not assigned.");

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnOneVsOneClicked()
    {
        Debug.Log("MatchmakingPanel: 1v1 selected");
        if (SceneFlowManager.Instance != null)
            SceneFlowManager.Instance.LoadOneVsOne();
        else
            Debug.LogError("SceneFlowManager instance not found!");
    }

    private void OnAIvsPlayerClicked()
    {
        Debug.Log("MatchmakingPanel: AI vs Player selected");
        if (SceneFlowManager.Instance != null)
            SceneFlowManager.Instance.LoadAIvsPlayer();
        else
            Debug.LogError("SceneFlowManager instance not found!");
    }

    private void OnBackClicked()
    {
        gameObject.SetActive(false);
    }
}