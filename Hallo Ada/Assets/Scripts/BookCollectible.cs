using UnityEngine;

public class BookCollectible : MonoBehaviour, Interactable
{
    public string bookName = "Book";

    public string GetInteractText()
    {
        return $"E - Collect {bookName}";
    }

    public void Interact(PlayerInteractor interactor)
    {
        GameManager.Instance.CollectBook();
        Destroy(gameObject);
    }
}
