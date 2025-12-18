using UnityEngine;

public class GlassShatterEffect : MonoBehaviour
{
    public ParticleSystem shatterParticles;
    public float destroyAfter = 2.5f;

    void Start()
    {
        if (shatterParticles != null)
        {
            shatterParticles.Play();
        }
        Destroy(gameObject, destroyAfter);
    }
}
