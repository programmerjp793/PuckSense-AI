using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TutorialManager : MonoBehaviour
{
    [Header("Tutorial Pages")]
    [SerializeField] private List<GameObject> tutorialPages = new List<GameObject>();
    
    [Header("Navigation Buttons")]
    [SerializeField] private Button nextButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button startGameButton;
    
    [Header("Page Indicators")]
    [SerializeField] private List<Image> pageIndicators = new List<Image>();
    [SerializeField] private Color activeIndicatorColor = Color.white;
    [SerializeField] private Color inactiveIndicatorColor = Color.gray;
    
    [Header("Tutorial Content")]
    [SerializeField] private Text pageTitle;
    [SerializeField] private Text pageDescription;
    
    private int currentPage = 0;
    private bool isInitialized = false;
    
    void Start()
    {
        // Auto-initialize if not called manually
        if (!isInitialized)
        {
            Initialize();
        }
    }
    
    public void Initialize()
    {
        if (isInitialized) return;
        
        // Setup button listeners
        if (nextButton != null) nextButton.onClick.AddListener(NextPage);
        if (previousButton != null) previousButton.onClick.AddListener(PreviousPage);
        if (startGameButton != null) startGameButton.onClick.AddListener(StartGameFromTutorial);
        
        currentPage = 0;
        UpdatePageDisplay();
        
        isInitialized = true;
        Debug.Log("TutorialManager initialized with " + tutorialPages.Count + " pages");
    }
    
    void UpdatePageDisplay()
    {
        // Hide all pages
        foreach (GameObject page in tutorialPages)
        {
            page.SetActive(false);
        }
        
        // Show current page
        if (currentPage >= 0 && currentPage < tutorialPages.Count)
        {
            tutorialPages[currentPage].SetActive(true);
        }
        
        // Update navigation buttons
        UpdateNavigationButtons();
        
        // Update page indicators
        UpdatePageIndicators();
        
        Debug.Log($"Showing tutorial page: {currentPage + 1}/{tutorialPages.Count}");
    }
    
    void UpdateNavigationButtons()
    {
        // Previous button should be disabled on first page
        if (previousButton != null)
        {
            previousButton.interactable = (currentPage > 0);
        }
        
        // Next button should be disabled on last page
        if (nextButton != null)
        {
            nextButton.interactable = (currentPage < tutorialPages.Count - 1);
        }
        
        // Show start game button only on last page
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(currentPage == tutorialPages.Count - 1);
        }
    }
    
    void UpdatePageIndicators()
    {
        for (int i = 0; i < pageIndicators.Count; i++)
        {
            if (pageIndicators[i] != null)
            {
                pageIndicators[i].color = (i == currentPage) ? activeIndicatorColor : inactiveIndicatorColor;
            }
        }
    }
    
    public void NextPage()
    {
        if (currentPage < tutorialPages.Count - 1)
        {
            currentPage++;
            UpdatePageDisplay();
        }
    }
    
    public void PreviousPage()
    {
        if (currentPage > 0)
        {
            currentPage--;
            UpdatePageDisplay();
        }
    }
    
    public void JumpToPage(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < tutorialPages.Count)
        {
            currentPage = pageIndex;
            UpdatePageDisplay();
        }
    }
    
    public void StartGameFromTutorial()
    {
        Debug.Log("Starting game from tutorial...");
        
        // You can use SceneFlowManager if available, or load directly
        if (SceneFlowManager.Instance != null)
        {
            SceneFlowManager.Instance.LoadGameplay("Tutorial");
        }
        else
        {
            // Fallback: load gameplay scene directly
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
        }
    }
    
    public int GetCurrentPage()
    {
        return currentPage;
    }
    
    public int GetTotalPages()
    {
        return tutorialPages.Count;
    }
}