using UnityEngine;

public abstract class PickupItem : MonoBehaviour, Interactable
{
    public string displayName = "Item";
    public bool pickedUp;
    public Sprite icon;
    public string itemId = ""; // optional stable id; if empty, computed from name+position

    [Header("Impact Audio")]
    public AudioClip[] impactClips;
    public float impactMinVelocity = 0.7f;
    public float impactCooldown = 0.2f;
    public float impactMaxVolume = 1f;
    float _lastImpactTime = -10f;

    [Header("Noise (Hearing)")]
    public float noiseRadius = 8f;
    public bool scaleNoiseWithImpact = true;
    public float maxNoiseRadius = 20f;

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
        foreach (var r in GetComponentsInChildren<Rigidbody>()) { r.isKinematic = true; r.useGravity = false; }

        if (inventory != null && inventory.handAnchor != null)
        {
            transform.SetParent(inventory.handAnchor, worldPositionStays: false);
            transform.localPosition = inventory.holdLocalPosition;
            transform.localEulerAngles = inventory.holdLocalEuler;
            foreach (var rend in GetComponentsInChildren<Renderer>()) rend.enabled = true;
            gameObject.SetActive(true);
        }
        else
        {
            foreach (var rend in GetComponentsInChildren<Renderer>()) rend.enabled = false;
            gameObject.SetActive(false);
        }
    }

    public virtual void OnDropped(Vector3 worldPosition)
    {
        pickedUp = false;
        transform.SetParent(null, true);
        transform.position = worldPosition;
        foreach (var rend in GetComponentsInChildren<Renderer>()) rend.enabled = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        foreach (var r in GetComponentsInChildren<Rigidbody>()) { r.isKinematic = false; r.useGravity = true; }
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

    void OnCollisionEnter(Collision collision)
    {
        if (pickedUp) return;
        if (impactClips == null || impactClips.Length == 0) return;
        if (Time.time - _lastImpactTime < impactCooldown) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < impactMinVelocity) return;

        Vector3 point = transform.position;
        if (collision.contactCount > 0)
        {
            point = collision.GetContact(0).point;
        }

        float t = Mathf.InverseLerp(impactMinVelocity, impactMinVelocity * 4f, speed);
        float vol = Mathf.Lerp(0.2f, impactMaxVolume, t);
        var clip = impactClips[Random.Range(0, impactClips.Length)];
        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, point, vol);
            _lastImpactTime = Time.time;
        }

        // Raise a noise event for AI hearing
        float radius = noiseRadius;
        if (scaleNoiseWithImpact)
        {
            float scale = Mathf.Lerp(0.6f, 1.5f, t);
            radius = Mathf.Min(noiseRadius * scale, maxNoiseRadius);
        }
        NoiseSystem.RaiseNoise(point, radius);
    }
}
