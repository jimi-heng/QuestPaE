using UnityEngine;

public class LookController : MonoBehaviour
{
    public Transform headBone;
    public float turnDirection = 25f;
    void LateUpdate()
    {
        headBone.localRotation *= Quaternion.Euler(10f, turnDirection, 0);
    }
}
