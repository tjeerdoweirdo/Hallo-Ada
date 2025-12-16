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
    }

    void Update()
    {
        UpdatePrompt();
        if (Input.GetKeyDown(interactKey))
        {
            TryInteract();
        }
        if (Input.GetKeyDown(KeyCode.Q))
        {
            inventory.DropHeld(cam.transform.position + cam.transform.forward * 1.2f);
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
