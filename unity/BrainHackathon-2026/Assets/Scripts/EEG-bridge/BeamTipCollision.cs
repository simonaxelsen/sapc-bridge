using UnityEngine;

public class BeamTipCollision : MonoBehaviour
{
    // This function runs automatically whenever something touches this object
    private void OnTriggerEnter(Collider other)
    {
        // Check if the object that hit us has the "Bee" trademark (Tag)
        if (other.CompareTag("Bee"))
        {
            Debug.Log("Hit a bee! Destroying: " + other.gameObject.name);
            Destroy(other.gameObject);
        }
    }
}