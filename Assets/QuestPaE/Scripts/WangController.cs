using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    public class WangController : BaseController
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

        public void NodAndWave()
        {
            StartCoroutine(NodAndWaveAfterDelay());
        }

        private IEnumerator NodAndWaveAfterDelay()
        {
            yield return StartCoroutine(NodAfterDelay(delay, nodTime));
            yield return StartCoroutine(WaveAfterDelay(3, waveTime));
        }
        
        public void StartShow()
        {
            StartCoroutine(ShowAfterDelay(delay));
        }
        
        private IEnumerator ShowAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            animator.SetTrigger("showTrigger");
        }
    }
}