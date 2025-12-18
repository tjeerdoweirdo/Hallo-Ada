using UnityEngine;
using System;

public static class NoiseSystem
{
    public static event Action<Vector3, float> OnNoise;

    public static void RaiseNoise(Vector3 position, float radius)
    {
        OnNoise?.Invoke(position, radius);
    }
}
