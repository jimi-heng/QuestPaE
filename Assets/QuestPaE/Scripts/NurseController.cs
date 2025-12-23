using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    public class NurseController : BaseController
    {
        [SerializeField] private float talkTime;

        public void StartTalk()
        {
            StartCoroutine(TalkAfterDelay(delay, talkTime));
        }

        private IEnumerator TalkAfterDelay(float delay, float talkTime)
        {
            yield return new WaitForSeconds(delay);
            animator.SetTrigger("talkTrigger");
            yield return new WaitForSeconds(talkTime);
            animator.SetTrigger("standTrigger");
        }

        public void TalkAndWave()
        {
            StartCoroutine(TalkAndWaveAfterDelay());
        }

        private IEnumerator TalkAndWaveAfterDelay()
        {
            yield return StartCoroutine(TalkAfterDelay(delay, talkTime));
            yield return StartCoroutine(WaveAfterDelay(6, waveTime));
        }
    }
}