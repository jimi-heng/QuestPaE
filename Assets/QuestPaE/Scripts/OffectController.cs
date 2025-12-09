using UnityEngine;

public class OffectController : MonoBehaviour
{
    public float offsetX = 0.05f;
    public float offsetY = 0.05f;
    public float offsetZ = 0.05f; 

    public void OffectControl()
    {
        transform.localPosition = new Vector3(offsetX, offsetY, offsetZ);
    }
}