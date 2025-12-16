using UnityEngine;
using UnityEngine.UI;

public class ReticleDot : MonoBehaviour
{
    public Image dot;
    public float smallSize = 6f;
    public float bigSize = 12f;
    public Color idleColor = new Color(1f, 1f, 1f, 0.35f);
    public Color activeColor = new Color(1f, 1f, 1f, 1f);
    public float lerpSpeed = 12f;

    float targetSize;
    Color targetColor;

    void Awake()
    {
        if (dot == null) dot = GetComponent<Image>();
        targetSize = smallSize;
        targetColor = idleColor;
        ApplyImmediate();
    }

    public void SetActive(bool isActive)
    {
        targetSize = isActive ? bigSize : smallSize;
        targetColor = isActive ? activeColor : idleColor;
    }

    void Update()
    {
        if (dot == null) return;
        RectTransform rt = dot.rectTransform;
        float size = Mathf.Lerp(rt.sizeDelta.x, targetSize, Time.deltaTime * lerpSpeed);
        rt.sizeDelta = new Vector2(size, size);
        dot.color = Color.Lerp(dot.color, targetColor, Time.deltaTime * lerpSpeed);
    }

    void ApplyImmediate()
    {
        if (dot == null) return;
        RectTransform rt = dot.rectTransform;
        rt.sizeDelta = new Vector2(targetSize, targetSize);
        dot.color = targetColor;
    }
}
