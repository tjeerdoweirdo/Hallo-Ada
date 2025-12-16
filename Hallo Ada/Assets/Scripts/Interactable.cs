using UnityEngine;

public interface Interactable
{
    string GetInteractText();
    void Interact(PlayerInteractor interactor);
}
