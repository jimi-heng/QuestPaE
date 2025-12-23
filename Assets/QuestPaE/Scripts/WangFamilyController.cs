using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    public class WangFamilyController : BaseController
    {
        [SerializeField] private float nodTime;

        public void StartNod()
        {
            StartCoroutine(NodAfterDelay(delay, nodTime));
        }

        private IEnumerator NodAfterDelay(float delay, float nodTime)
        {
            yield return new WaitForSeconds(delay);
            animator.SetTrigger("nodTrigger");
            yield return new WaitForSeconds(nodTime);
            animator.SetTrigger("standTrigger");
        }
    }
}