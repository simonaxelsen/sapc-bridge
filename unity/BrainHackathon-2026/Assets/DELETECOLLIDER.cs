using UnityEngine;

public class DELETECOLLIDER : MonoBehaviour
{
    // This method is called when the Collider attached to this object 
    // enters another collider (provided one has a Rigidbody).
    private void OnTriggerEnter(Collider other)
    {
        // Optional: You can check for a specific tag to prevent accidental deletion
        if (other.CompareTag("Bee")) 
        {
        Debug.Log("Collision detected! Destroying object: " + other.gameObject.name);

        GazeBeautyBreakable beautyBreakable = other.GetComponentInParent<GazeBeautyBreakable>();
        if (beautyBreakable != null && beautyBreakable.TriggerFromBeamHit())
        {
            return;
        }

        GpuPrefabBurstSpawner.PlayIfPresent(other.gameObject);
        Destroy(other.gameObject);
        }
    }
}
