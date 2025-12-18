using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public int totalBooks = 5;
    public int collectedBooks = 0;

    public TextMeshProUGUI booksText;
    public GameObject winScreen;

    // Persist inventory across restart
    List<string> keptItemIds = new List<string>();
    bool hasWon = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        UpdateUI();
        if (winScreen != null) winScreen.SetActive(false);
        hasWon = false;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public void CollectBook()
    {
        collectedBooks++;
        UpdateUI();
        if (collectedBooks >= totalBooks)
        {
            Win();
        }
    }

    void UpdateUI()
    {
        if (booksText != null)
        {
            if (hasWon)
            {
                booksText.text = "You win";
            }
            else
            {
                booksText.text = $"Books: {collectedBooks}/{totalBooks}";
            }
        }
    }

    void Win()
    {
        // Show a simple victory message via the books text instead of enabling a win screen
        hasWon = true;
        UpdateUI();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Restart()
    {
        // Capture held items from player inventory
        keptItemIds.Clear();
        var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
        if (interactor != null && interactor.inventory != null)
        {
            var id = interactor.inventory.GetHeldItemId();
            if (!string.IsNullOrEmpty(id)) keptItemIds.Add(id);
        }

        collectedBooks = 0;
        hasWon = false;
        UpdateUI();
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Restore kept items into player inventory
        if (keptItemIds.Count == 0) return;
        var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
        if (interactor == null || interactor.inventory == null) return;

        var items = Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.None);
        foreach (var id in keptItemIds)
        {
            foreach (var item in items)
            {
                if (item.GetStableId() == id)
                {
                    // Ensure item is picked up and assigned to inventory
                    interactor.inventory.Pickup(item);
                    break;
                }
            }
        }
        keptItemIds.Clear();
        // Ensure UI reflects reset/respawned collectibles
        UpdateUI();
    }
}
