using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public Inventory inventory;
    // Support arrays of UI elements for multiple slots. If only single elements are assigned, fallback to them.
    public TextMeshProUGUI[] itemLabels;
    public Image[] itemIcons;
    public Color emptyColor = new Color(1f, 1f, 1f, 0.35f);
    public Color filledColor = new Color(1f, 1f, 1f, 1f);
    public Color selectedColor = new Color(0.8f, 1f, 0.6f, 1f);

    void Awake()
    {
        if (inventory == null)
        {
            inventory = FindPlayerInventory();
        }
    }

    void Update()
    {
        HandleInput();
        UpdateUI();
    }

    void HandleInput()
    {
        if (inventory == null) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SelectSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SelectSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SelectSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SelectSlot(3);
    }

    Inventory FindPlayerInventory()
    {
        var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
        if (interactor != null && interactor.inventory != null) return interactor.inventory;
        return Object.FindFirstObjectByType<Inventory>();
    }

    void UpdateUI()
    {
        // If arrays are provided, populate each slot; otherwise fallback to single-label/icon behavior
        if (itemLabels != null && itemIcons != null && itemLabels.Length == itemIcons.Length && itemLabels.Length > 0)
        {
            for (int i = 0; i < itemLabels.Length; i++)
            {
                var item = (inventory != null) ? inventory.GetSlot(i) : null;
                bool hasItem = item != null;
                itemLabels[i].text = hasItem ? item.displayName : "Empty";
                // highlight selected slot
                bool selected = (inventory != null && inventory.selectedSlot == i);
                itemLabels[i].color = selected ? selectedColor : (hasItem ? filledColor : emptyColor);
                var iconSprite = hasItem ? item.icon : null;
                itemIcons[i].sprite = iconSprite;
                itemIcons[i].color = selected ? selectedColor : ((iconSprite != null && hasItem) ? filledColor : emptyColor);
            }
            return;
        }

        // Fallback single-slot UI: use first slot
        var first = (inventory != null) ? inventory.GetSlot(0) : null;
        bool hasFirst = first != null;
        if (itemLabels != null && itemLabels.Length > 0 && itemLabels[0] != null)
        {
            bool selected = (inventory != null && inventory.selectedSlot == 0);
            itemLabels[0].text = hasFirst ? first.displayName : "Empty";
            itemLabels[0].color = selected ? selectedColor : (hasFirst ? filledColor : emptyColor);
        }
        if (itemIcons != null && itemIcons.Length > 0 && itemIcons[0] != null)
        {
            bool selected = (inventory != null && inventory.selectedSlot == 0);
            var iconSprite = hasFirst ? first.icon : null;
            itemIcons[0].sprite = iconSprite;
            itemIcons[0].color = selected ? selectedColor : ((iconSprite != null && hasFirst) ? filledColor : emptyColor);
        }
    }
}
