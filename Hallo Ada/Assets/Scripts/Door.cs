using UnityEngine;

public class Door : MonoBehaviour, Interactable
{
    public string doorId = "";
    public bool isLocked = false;
    public bool isOpen = false;
    public float openAngle = 90f;
    public float openSpeed = 4f;
    public bool hingeOnLeftEdge = true; // if false, right edge

    Quaternion closedRot;
    Quaternion openRot;
    Vector3 hingeWorldPos;
    float currentAngle = 0f;
    float targetAngle = 0f;

    void Awake()
    {
        closedRot = transform.localRotation;
        openRot = closedRot * Quaternion.Euler(0f, openAngle, 0f);
        ComputeHingePosition();
    }

    void OnEnable()
    {
        // Ensure doors start closed visually
        isOpen = false;
        transform.localRotation = closedRot;
        currentAngle = 0f;
        targetAngle = 0f;
    }

    void Update()
    {
        targetAngle = isOpen ? openAngle : 0f;
        float step = openSpeed * Time.deltaTime * 30f; // scale speed to feel snappy
        float next = Mathf.MoveTowards(currentAngle, targetAngle, step);
        float delta = next - currentAngle;
        if (Mathf.Abs(delta) > 0.0001f)
        {
            RotateAroundHinge(delta);
            currentAngle = next;
        }
    }

    public string GetInteractText()
    {
        if (isLocked) return "Door is locked";
        return isOpen ? "E - Close door" : "E - Open door";
    }

    public void Interact(PlayerInteractor interactor)
    {
        if (isLocked) return;
        isOpen = !isOpen;
    }

    public bool TryUnlock(string keyId)
    {
        if (!isLocked) return true;
        if (string.IsNullOrEmpty(doorId)) return false;
        if (doorId == keyId)
        {
            isLocked = false;
            return true;
        }
        return false;
    }

    void ComputeHingePosition()
    {
        var col = GetComponent<Collider>();
        if (col == null)
        {
            hingeWorldPos = transform.position;
            return;
        }
        // Determine the left edge along local +X/-X based on setting
        Bounds b = col.bounds; // world-space bounds
        Vector3 localRight = transform.right;
        // Project bounds extents along door's right vector to get width
        float halfWidth = Vector3.Dot(localRight, (b.max - b.center));
        halfWidth = Mathf.Abs(halfWidth);
        Vector3 edgeOffset = localRight * (hingeOnLeftEdge ? -halfWidth : halfWidth);
        hingeWorldPos = new Vector3(transform.position.x, b.center.y, transform.position.z) + edgeOffset;
    }

    void RotateAroundHinge(float deltaAngle)
    {
        // Rotate the door around the hinge world position along its up axis
        Vector3 axis = transform.up;
        transform.RotateAround(hingeWorldPos, axis, deltaAngle);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        ComputeHingePosition();
        Gizmos.color = hingeOnLeftEdge ? Color.green : Color.blue;
        Gizmos.DrawCube(hingeWorldPos, new Vector3(0.08f, 0.4f, 0.08f));
        // Draw a line from hinge to door center for clarity
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(hingeWorldPos, transform.position);
    }
#endif
}
