using UnityEngine;

public class Inventory : MonoBehaviour
{
    public PickupItem[] slots = new PickupItem[4];
    public Transform handAnchor;
    public Vector3 holdLocalPosition = new Vector3(0.3f, -0.3f, 0.6f);
    public Vector3 holdLocalEuler = new Vector3(0f, 0f, 0f);
    public bool IsHolding => GetFirstHeld() != null;

    // Selected/equipped slot index (0-based). Default to 0.
    public int selectedSlot = 0;
    PickupItem equippedItem = null;

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        selectedSlot = index;
        UpdateEquippedVisual();
    }

    public PickupItem GetSelectedItem()
    {
        return GetSlot(selectedSlot);
    }

    public bool CanPickup(PickupItem item)
    {
        return GetFirstEmptySlot() != -1;
    }

    int GetFirstEmptySlot()
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i] == null) return i;
        return -1;
    }

    int GetFirstHeldSlot()
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i] != null) return i;
        return -1;
    }

    public PickupItem GetFirstHeld()
    {
        int s = GetFirstHeldSlot();
        return s >= 0 ? slots[s] : null;
    }

    public PickupItem GetSlot(int index)
    {
        if (index < 0 || index >= slots.Length) return null;
        return slots[index];
    }

    public void Pickup(PickupItem item)
    {
        int s = GetFirstEmptySlot();
        if (s == -1) return;
        slots[s] = item;
        item.OnPickedUp(this);
        // if we picked up into the currently selected slot, equip visually
        if (s == selectedSlot)
        {
            UpdateEquippedVisual();
        }
        else
        {
            // hide items that are stored but not equipped
            if (item != null) item.gameObject.SetActive(false);
        }
    }

    public void DropHeld(Vector3 worldPosition)
    {
        // drop the currently selected slot
        DropSlot(selectedSlot, worldPosition);
    }

    public void DropSlot(int slotIndex, Vector3 worldPosition)
    {
        if (slotIndex < 0 || slotIndex >= slots.Length) return;
        var item = slots[slotIndex];
        if (item == null) return;
        slots[slotIndex] = null;
        // If this item is currently equipped (visible in hand), make sure it is unparented and physics enabled
        if (equippedItem == item)
        {
            // unparent and enable physics so OnDropped can place it correctly
            item.transform.SetParent(null, true);
            var rb = item.GetComponentInChildren<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            equippedItem = null;
        }
        item.OnDropped(worldPosition);
    }

    public void UseHeld(PlayerInteractor interactor)
    {
        // use the selected slot
        UseSlot(selectedSlot, interactor);
    }

    public void UseSlot(int slotIndex, PlayerInteractor interactor)
    {
        if (slotIndex < 0 || slotIndex >= slots.Length) return;
        var item = slots[slotIndex];
        if (item == null) return;
        item.OnUsed(interactor);
    }

    public string GetHeldItemId()
    {
        var first = GetFirstHeld();
        return first != null ? first.GetStableId() : null;
    }

    public string GetHeldItemId(int slotIndex)
    {
        var it = GetSlot(slotIndex);
        return it != null ? it.GetStableId() : null;
    }

    public void ThrowHeld(Vector3 origin, Vector3 direction, float force)
    {
        // throw the selected slot
        ThrowSlot(selectedSlot, origin, direction, force);
    }

    public void ThrowSlot(int slotIndex, Vector3 origin, Vector3 direction, float force)
    {
        if (slotIndex < 0 || slotIndex >= slots.Length) return;
        var item = slots[slotIndex];
        if (item == null) return;
        slots[slotIndex] = null;
        Vector3 spawnPos = origin + direction.normalized * 0.6f;
        // If it was equipped, unparent and enable physics before throwing
        if (equippedItem == item)
        {
            item.transform.SetParent(null, true);
            var rb0 = item.GetComponentInChildren<Rigidbody>();
            if (rb0 != null) rb0.isKinematic = false;
            equippedItem = null;
        }
        item.OnDropped(spawnPos);
        var rb = item.GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(direction.normalized * force, ForceMode.VelocityChange);
        }
    }

    void UpdateEquippedVisual()
    {
        // Unequip current if it doesn't match selected slot
        var target = GetSlot(selectedSlot);
        if (equippedItem != null && equippedItem != target)
        {
            UnequipCurrent();
        }
        if (target != null && equippedItem != target)
        {
            EquipItem(target);
        }
    }

    void EquipItem(PickupItem item)
    {
        if (item == null || handAnchor == null) return;
        equippedItem = item;
        // ensure item is active before parenting
        item.gameObject.SetActive(true);
        // parent to hand anchor and set local transform
        item.transform.SetParent(handAnchor, false);
        item.transform.localPosition = holdLocalPosition;
        item.transform.localEulerAngles = holdLocalEuler;
        // try to disable physics if present
        var rb = item.GetComponentInChildren<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }

    void UnequipCurrent()
    {
        if (equippedItem == null) return;
        var item = equippedItem;
        // hide the visual and keep item stored in inventory
        item.gameObject.SetActive(false);
        // reparent under this inventory so hierarchy stays tidy
        item.transform.SetParent(this.transform, false);
        var rb = item.GetComponentInChildren<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        equippedItem = null;
    }
}
