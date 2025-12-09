using UnityEngine;

public class WaveController : MonoBehaviour
{
    private Animator animator;
    public Transform headBone;
    public float turnDirection = 25f;
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    public void StartWave()
    {
        animator.SetTrigger("waveTrigger");
    }

    void LateUpdate()
    {
        headBone.localRotation *= Quaternion.Euler(10f, turnDirection, 0);
    }
}
