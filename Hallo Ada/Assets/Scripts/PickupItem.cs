using UnityEngine;

public abstract class PickupItem : MonoBehaviour, Interactable
{
    public string displayName = "Item";
    public bool pickedUp;
    public Sprite icon;
    public string itemId = ""; // optional stable id; if empty, computed from name+position

    public virtual string GetInteractText()
    {
        return pickedUp ? "" : $"E - Pick up {displayName}";
    }

    public virtual void Interact(PlayerInteractor interactor)
    {
        if (pickedUp) return;
        if (interactor.inventory.CanPickup(this))
        {
            interactor.inventory.Pickup(this);
        }
    }

    public virtual void OnPickedUp(Inventory inventory)
    {
        pickedUp = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var r in GetComponentsInChildren<Rigidbody>()) r.isKinematic = true;
        foreach (var rend in GetComponentsInChildren<Renderer>()) rend.enabled = false;
        gameObject.SetActive(false);
    }

    public virtual void OnDropped(Vector3 worldPosition)
    {
        pickedUp = false;
        transform.position = worldPosition;
        foreach (var rend in GetComponentsInChildren<Renderer>()) rend.enabled = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        foreach (var r in GetComponentsInChildren<Rigidbody>()) r.isKinematic = false;
        gameObject.SetActive(true);
    }

    public abstract void OnUsed(PlayerInteractor interactor);

    public string GetStableId()
    {
        if (!string.IsNullOrEmpty(itemId)) return itemId;
        // Compute a simple stable id from name + position rounded
        Vector3 p = transform.position;
        int x = Mathf.RoundToInt(p.x * 100f);
        int y = Mathf.RoundToInt(p.y * 100f);
        int z = Mathf.RoundToInt(p.z * 100f);
        return $"{gameObject.name}_{x}_{y}_{z}";
    }
}
