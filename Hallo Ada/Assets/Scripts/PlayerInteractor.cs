using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerInteractor : MonoBehaviour
{
    public Camera cam;
    public float interactRange = 3f;
    public KeyCode interactKey = KeyCode.E;
    public Inventory inventory;
    public TextMeshProUGUI promptText;
    public ReticleDot reticle;
    [Header("Throwing")]
    public KeyCode dropOrThrowKey = KeyCode.Q;
    public float throwChargeMax = 0.8f;
    public float minThrowForce = 4f;
    public float maxThrowForce = 12f;
    float throwCharge;

    void Awake()
    {
        if (cam == null)
        {
            var pc = GetComponent<PlayerController>();
            cam = pc != null ? pc.playerCamera : Camera.main;
        }
        if (inventory == null)
        {
            inventory = GetComponent<Inventory>();
            if (inventory == null) inventory = gameObject.AddComponent<Inventory>();
        }
        if (inventory != null && inventory.handAnchor == null && cam != null)
        {
            var anchor = new GameObject("HandAnchor").transform;
            anchor.SetParent(cam.transform, false);
            anchor.localPosition = new Vector3(0.3f, -0.3f, 0.6f);
            anchor.localRotation = Quaternion.identity;
            inventory.handAnchor = anchor;
        }
    }

    void Update()
    {
        UpdatePrompt();
        if (Input.GetKeyDown(interactKey))
        {
            TryInteract();
        }
        // Drop / Throw logic on Q
        if (Input.GetKeyDown(dropOrThrowKey))
        {
            throwCharge = 0f;
            if (reticle != null) reticle.SetCharging(0f);
        }
        if (Input.GetKey(dropOrThrowKey))
        {
            throwCharge += Time.deltaTime;
            if (reticle != null)
            {
                float t = Mathf.Clamp01(throwCharge / throwChargeMax);
                reticle.SetCharging(t);
            }
        }
        if (Input.GetKeyUp(dropOrThrowKey))
        {
            if (inventory != null && inventory.IsHolding)
            {
                float t = Mathf.Clamp01(throwCharge / throwChargeMax);
                if (t > 0.2f)
                {
                    float force = Mathf.Lerp(minThrowForce, maxThrowForce, t);
                    inventory.ThrowHeld(cam.transform.position + cam.transform.forward * 0.8f, cam.transform.forward, force);
                }
                else
                {
                    inventory.DropHeld(cam.transform.position + cam.transform.forward * 1.2f);
                }
            }
            if (reticle != null) reticle.ClearCharging();
        }
        if (Input.GetMouseButtonDown(1))
        {
            inventory.UseHeld(this);
        }
    }

    void UpdatePrompt()
    {
        string text = "";
        var target = RaycastInteractable();
        bool hasTarget = target != null;
        if (hasTarget)
        {
            text = target.GetInteractText();
        }
        if (promptText != null) promptText.text = text;
        if (reticle != null) reticle.SetActive(hasTarget);
    }

    Interactable RaycastInteractable()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        // Collide with triggers to support door trigger colliders
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, ~0, QueryTriggerInteraction.Collide))
        {
            // Direct door detection for reliability
            var door = hit.collider.GetComponentInParent<Door>();
            if (door != null) return door;

            // Generic interactable search
            var t = hit.collider.transform;
            var behaviours = t.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is Interactable inter) return inter;
            }
        }
        return null;
    }

    void TryInteract()
    {
        var i = RaycastInteractable();
        if (i != null)
        {
            i.Interact(this);
        }
    }
}
