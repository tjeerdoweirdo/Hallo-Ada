using UnityEngine;

public class GenericPickupItem : PickupItem
{
    public override void OnUsed(PlayerInteractor interactor)
    {
        // Default: no special use; could convert to throw if desired
        // For now, do nothing on right-click.
    }
}
