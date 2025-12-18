using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A simple bear trap that can be placed (OnUsed), armed, disarmed (Interact) and picked up when disarmed.
// When stepped on it stuns the player or AI for a short duration and becomes disarmed so it can be picked up.
public class BearTrap : PickupItem
{
    [Header("Trap Settings")]
    public bool armed = true;
    public float stunDuration = 4f;
    public float triggerNoiseRadius = 12f;
    public AudioClip armClip;
    public AudioClip disarmClip;
    public AudioClip triggerClip;
    public ParticleSystem triggerEffect;
    public bool singleUse = true; // if true, trap becomes disarmed after first trigger

    [Header("Visuals")]
    // A single renderer whose material will be swapped to indicate trap state
    public Renderer targetRenderer;
    public Material armedMaterial;
    public Material disarmedMaterial;
    public Material triggeredMaterial;

    // Owner (AI or player) who placed the trap. Used so AI can avoid its own traps if desired.
    public GameObject owner;

    // Active traps registry for AI queries
    public static List<BearTrap> ActiveTraps = new List<BearTrap>();

    void OnEnable()
    {
        if (armed && !ActiveTraps.Contains(this)) ActiveTraps.Add(this);
        UpdateVisuals();
    }

    void OnDisable()
    {
        ActiveTraps.Remove(this);
    }

    void UpdateVisuals()
    {
        if (targetRenderer != null)
        {
            if (armed && armedMaterial != null) targetRenderer.material = armedMaterial;
            else if (!armed && disarmedMaterial != null) targetRenderer.material = disarmedMaterial;
        }
    }

    public override string GetInteractText()
    {
        if (armed) return "E - Disarm Trap";
        return base.GetInteractText();
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (armed)
        {
            // Disarm instantly (could be extended to require hold)
            Disarm();
            if (disarmClip != null) AudioSource.PlayClipAtPoint(disarmClip, transform.position);
            // then allow pickup if player has inventory space
            if (interactor != null && interactor.inventory != null && interactor.inventory.CanPickup(this))
            {
                interactor.inventory.Pickup(this);
            }
            return;
        }
        base.Interact(interactor);
    }

    public override void OnUsed(PlayerInteractor interactor)
    {
        // Place the trap in front of camera and arm it
        if (interactor == null || interactor.cam == null) return;
        Vector3 spawn = interactor.cam.transform.position + interactor.cam.transform.forward * 1.2f;
        if (Physics.Raycast(interactor.cam.transform.position + Vector3.up * 1f, interactor.cam.transform.forward, out RaycastHit hit, 2.5f))
        {
            spawn = hit.point - interactor.cam.transform.forward * 0.2f;
        }
        // Project down to ground
        if (Physics.Raycast(spawn + Vector3.up * 1.5f, Vector3.down, out RaycastHit ghit, 3f))
        {
            spawn = ghit.point;
        }

        // Drop the item from inventory which calls OnDropped
        interactor.inventory.DropHeld(spawn);
        // set owner to player
        owner = interactor.gameObject;
    }

    public override void OnDropped(Vector3 worldPosition)
    {
        base.OnDropped(worldPosition);
        // Arm when dropped into world
        Arm();
    }

    void Arm()
    {
        armed = true;
        if (!ActiveTraps.Contains(this)) ActiveTraps.Add(this);
        UpdateVisuals();
        if (armClip != null) AudioSource.PlayClipAtPoint(armClip, transform.position);
        // make colliders triggers so stepping can be detected
        foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = true;
    }

    void Disarm()
    {
        armed = false;
        ActiveTraps.Remove(this);
        UpdateVisuals();
        foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!armed) return;

        // ignore triggers
        if (other.isTrigger) return;

        // Player
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc != null)
        {
            StartCoroutine(TriggerRoutine(other.gameObject, true));
            return;
        }

        // AI
        var ai = other.GetComponentInParent<Adaai>();
        if (ai != null)
        {
            // If the AI placed this trap, ignore it
            if (owner != null && owner == ai.gameObject) return;
            StartCoroutine(TriggerRoutine(other.gameObject, false));
            return;
        }
    }

    IEnumerator TriggerRoutine(GameObject victim, bool isPlayer)
    {
        // play effect and sound
        if (triggerEffect != null) triggerEffect.Play();
        if (triggerClip != null) AudioSource.PlayClipAtPoint(triggerClip, transform.position);
        if (targetRenderer != null && triggeredMaterial != null) targetRenderer.material = triggeredMaterial;

        // Raise noise so other AI can hear
        NoiseSystem.RaiseNoise(transform.position, triggerNoiseRadius);

        // Apply stun effect
        if (isPlayer)
        {
            var pc = victim.GetComponentInParent<PlayerController>();
            var inter = victim.GetComponentInParent<PlayerInteractor>();
            if (pc != null) pc.enabled = false;
            if (inter != null) inter.enabled = false;
            yield return new WaitForSeconds(stunDuration);
            if (pc != null) pc.enabled = true;
            if (inter != null) inter.enabled = true;
        }
        else
        {
            var ai = victim.GetComponentInParent<Adaai>();
            var agent = ai != null ? ai.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            if (ai != null) ai.enabled = false;
            if (agent != null) agent.isStopped = true;
            yield return new WaitForSeconds(stunDuration);
            if (ai != null) ai.enabled = true;
            if (agent != null) agent.isStopped = false;
        }

        // After trigger, either remove or disarm so it can be picked up
        if (singleUse)
        {
            Disarm();
        }
    }

    // Helper for AI to check for nearby traps (optionally ignoring those placed by same owner)
    public static bool IsTrapNear(Vector3 pos, float radius, GameObject ignoreOwner = null)
    {
        float rr = radius * radius;
        for (int i = 0; i < ActiveTraps.Count; i++)
        {
            var t = ActiveTraps[i];
            if (t == null || !t.armed) continue;
            if (ignoreOwner != null && t.owner == ignoreOwner) continue;
            if ((t.transform.position - pos).sqrMagnitude <= rr) return true;
        }
        return false;
    }
}
