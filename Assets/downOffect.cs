using UnityEngine;

public class downOffect : MonoBehaviour
{
    public float offsetY = 0.015f; // ÏòÏÂÆ«ÒÆ

    public void DownOffect()
    {
        transform.localPosition = new Vector3(0, -offsetY, 0);
    }
}