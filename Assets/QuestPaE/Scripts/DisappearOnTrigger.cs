using UnityEngine;

namespace QuestPaE.Scripts
{
    public class DisappearOnTrigger : MonoBehaviour
    {
        public void ChangeVisible()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
            else
                gameObject.SetActive(true);
        }
    }
}
