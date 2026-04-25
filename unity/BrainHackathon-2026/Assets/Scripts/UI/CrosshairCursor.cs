using UnityEngine;

public class CrosshairCursor : MonoBehaviour
{
    public Texture2D crosshairTexture;

    void Start()
    {
        // Center of the image = click point
        Vector2 hotspot = new Vector2(
            crosshairTexture.width / 2,
            crosshairTexture.height / 2
        );

        Cursor.SetCursor(crosshairTexture, hotspot, CursorMode.Auto);
    }
}