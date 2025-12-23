using UnityEngine;
using System.Collections;

public class ShowController : MonoBehaviour
{
    [SerializeField] private float delayTime;
    [SerializeField] private Material sharedMat;
    [SerializeField] private Material sharedMat1;
    public void ShowCloth()
    {
        StartCoroutine(ShowAfterDelay());
    }
    
    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSeconds(delayTime);
        sharedMat.SetFloat("_check", 1);
        sharedMat1.SetFloat("_check", 1);
    }
    
    public void HideCloth()
    {
        sharedMat.SetFloat("_check", 0);
        sharedMat1.SetFloat("_check", 0);
    }
}
