using UnityEngine;

public class DisappearOnTrigger : MonoBehaviour
{
    public void changeVisible()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }else
            gameObject.SetActive(true);
    }
}
