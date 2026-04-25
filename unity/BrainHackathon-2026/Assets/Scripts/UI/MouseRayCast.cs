using UnityEngine;
using UnityEngine.InputSystem; // 1. Add this namespace!

public class MouseRayCast : MonoBehaviour
{
    [Header("Raycast Settings")]
    public float maxDistance = 100f;
    
    private IGazeInteractable currentInteractable; 

    void Update()
    {
        // Safety check: ensure a mouse is actually plugged in/detected
        if (Mouse.current == null) return;

        // 2. Read the mouse position using the New Input System
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray mouseRay = Camera.main.ScreenPointToRay(mousePos);
        
        RaycastHit hit;

        if (Physics.Raycast(mouseRay, out hit, maxDistance))
        {
            IGazeInteractable hitInteractable = hit.collider.GetComponent<IGazeInteractable>();

            if (hitInteractable != currentInteractable)
            {
                currentInteractable?.OnLookExit(); 
                currentInteractable = hitInteractable; 
                currentInteractable?.OnLookEnter(); 
            }

            currentInteractable?.OnLookStay();
        }
        else
        {
            if (currentInteractable != null)
            {
                currentInteractable.OnLookExit();
                currentInteractable = null; 
            }
        }
    }
}