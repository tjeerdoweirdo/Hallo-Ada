using UnityEngine;

public class BreakableWindow : MonoBehaviour
{
    public float breakForce = 7f;
    public AudioClip breakSound;
    public GameObject brokenWindowPrefab;
    public bool destroyOnBreak = true;

    bool broken = false;

    void OnCollisionEnter(Collision collision)
    {
        if (broken) return;
        var rb = collision.rigidbody;
        if (rb == null) return;
        float impact = collision.relativeVelocity.magnitude;
        if (impact < breakForce) return;

        Break(collision.GetContact(0).point);
    }

    public void Break(Vector3 hitPoint)
    {
        if (broken) return;
        broken = true;
        if (breakSound != null)
        {
            AudioSource.PlayClipAtPoint(breakSound, hitPoint, 1f);
        }
        // If a brokenWindowPrefab is assigned, spawn it. Otherwise, just disappear.
        if (brokenWindowPrefab != null)
        {
            Instantiate(brokenWindowPrefab, transform.position, transform.rotation);
        }
        // Notify global noise system so AI always knows where the break occurred
        NoiseSystem.RaiseNoise(hitPoint, float.PositiveInfinity);
        // Always destroy the window object on break (disappear)
        Destroy(gameObject);
    }
}
