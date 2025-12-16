using UnityEngine;

public class Inventory : MonoBehaviour
{
    public PickupItem heldItem;

    public bool IsHolding => heldItem != null;

    public bool CanPickup(PickupItem item) => !IsHolding;

    public void Pickup(PickupItem item)
    {
        if (heldItem == null)
        {
            heldItem = item;
            item.OnPickedUp(this);
        }
    }

    public void DropHeld(Vector3 worldPosition)
    {
        if (heldItem == null) return;
        var item = heldItem;
        heldItem = null;
        item.OnDropped(worldPosition);
    }

    public void UseHeld(PlayerInteractor interactor)
    {
        if (heldItem == null) return;
        heldItem.OnUsed(interactor);
    }

    public string GetHeldItemId()
    {
        return heldItem != null ? heldItem.GetStableId() : null;
    }
}
