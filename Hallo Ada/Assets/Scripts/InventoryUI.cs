using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public Inventory inventory;
    public TextMeshProUGUI itemLabel;
    public Image itemIcon;
    public Color emptyColor = new Color(1f, 1f, 1f, 0.35f);
    public Color filledColor = new Color(1f, 1f, 1f, 1f);

    void Awake()
    {
        if (inventory == null)
        {
            inventory = FindPlayerInventory();
        }
    }

    void Update()
    {
        UpdateUI();
    }

    Inventory FindPlayerInventory()
    {
        var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
        if (interactor != null && interactor.inventory != null) return interactor.inventory;
        return Object.FindFirstObjectByType<Inventory>();
    }

    void UpdateUI()
    {
        var item = (inventory != null) ? inventory.heldItem : null;
        bool hasItem = item != null;

        if (itemLabel != null)
        {
            itemLabel.text = hasItem ? item.displayName : "Empty";
            itemLabel.color = hasItem ? filledColor : emptyColor;
        }

        if (itemIcon != null)
        {
            var iconSprite = hasItem ? item.icon : null;
            itemIcon.sprite = iconSprite;
            itemIcon.color = (iconSprite != null && hasItem) ? filledColor : emptyColor;
        }
    }
}
