#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Utility to create a simple BearTrap prefab into Assets/Resources/Prefabs
public static class CreateBearTrapPrefab
{
    [MenuItem("Tools/Create BearTrap Prefab")]
    public static void CreatePrefab()
    {
        string dir = "Assets/Resources/Prefabs";
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "BearTrap.prefab");

        // Temporary GameObject to assemble the prefab
        GameObject root = new GameObject("BearTrap_Temp");
        var trap = root.AddComponent<BearTrap>();

        // Armed visual (simple cube)
        GameObject armed = GameObject.CreatePrimitive(PrimitiveType.Cube);
        armed.name = "ArmedVisual";
        armed.transform.SetParent(root.transform, false);
        armed.transform.localPosition = Vector3.zero;
        armed.transform.localScale = new Vector3(0.6f, 0.1f, 0.6f);
        // Remove default collider from primitive (we'll add our own on root)
        Object.DestroyImmediate(armed.GetComponent<Collider>());

        // Disarmed visual (flat quad)
        GameObject disarmed = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disarmed.name = "DisarmedVisual";
        disarmed.transform.SetParent(root.transform, false);
        disarmed.transform.localPosition = Vector3.zero;
        disarmed.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        disarmed.transform.localScale = Vector3.one * 0.6f;
        Object.DestroyImmediate(disarmed.GetComponent<Collider>());

        // Do not assign scene object references into the prefab to avoid inspector UIElement issues.
        // Users can assign `visualArmed` and `visualDisarmed` on the prefab asset in the Editor after creation.

        // Add a trigger collider on the root for detecting steps
        var col = root.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = Vector3.up * 0.05f;
        col.size = new Vector3(0.6f, 0.1f, 0.6f);

        // Add a kinematic rigidbody so physics queries behave correctly if needed
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Save as prefab inside Resources so it can be loaded at runtime via Resources.Load
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        if (prefab != null)
        {
            Debug.Log("Created BearTrap prefab at " + path);
        }
        else
        {
            Debug.LogError("Failed to create BearTrap prefab at " + path);
        }

        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
