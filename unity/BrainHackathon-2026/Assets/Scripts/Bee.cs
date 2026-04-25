using UnityEngine;

public class Bee : MonoBehaviour
{
    private void OnDestroy()
    {
        Score.AddScore(1);
        Camera.main.GetComponent<CameraShake>().Shake();
    }
}