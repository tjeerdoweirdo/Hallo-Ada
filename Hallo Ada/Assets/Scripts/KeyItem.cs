using UnityEngine;

public class KeyItem : PickupItem
{
    public string keyId = "KeyA";
    public float useRange = 3f;

    public override void OnUsed(PlayerInteractor interactor)
    {
        var cam = interactor.cam;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, useRange))
        {
            var door = hit.collider.GetComponentInParent<Door>();
            if (door != null)
            {
                if (door.TryUnlock(keyId))
                {
                    interactor.inventory.DropHeld(door.transform.position + door.transform.forward * 0.5f);
                }
            }
        }
    }
}
