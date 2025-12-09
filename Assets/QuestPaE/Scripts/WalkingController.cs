using UnityEngine;
using System.Collections;

public class WalkingController : MonoBehaviour
{
    private Animator animator;
    public float turnDirection = 25f;
    public Transform headBone;
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void LateUpdate()
    {
        headBone.localRotation *= Quaternion.Euler(0, turnDirection, 0);
    }

    public void StartWalk()
    {
        animator.SetTrigger("walkTrigger");
        StartCoroutine(DelayStandTrigger());
    }

    private IEnumerator DelayStandTrigger()
    {
        yield return new WaitForSeconds(2f); 
        animator.SetTrigger("standTrigger");
    }

}
