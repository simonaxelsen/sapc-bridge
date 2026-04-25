using UnityEngine;

public class FanInteractable : MonoBehaviour, IGazeInteractable
{
    private Animator anim;

    void Start()
    {
        anim = GetComponent<Animator>();
    }

    public void OnLookEnter()
    {
        // Mouse entered! Trigger the WindUp animation
        anim.SetTrigger("TurnOn"); 
    }

    public void OnLookStay()
    {
        // We don't need to do anything every frame for the fan, 
        // but the interface requires this method to exist!
    }

    public void OnLookExit()
    {
        // Mouse left! Trigger the WindDown animation
        anim.SetTrigger("TurnOff"); 
    }
}