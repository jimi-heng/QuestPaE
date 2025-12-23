using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    public class AudioController : MonoBehaviour
    {
        private AudioSource audioSource;
        private Coroutine audioCoroutine;

        public float delayTime = 0f;

        private void Start()
        {
            audioSource = GetComponent<AudioSource>();
        }

        public void StartAudio()
        {
            audioCoroutine = StartCoroutine(PlayAudioAfterDelay());
        }

        private IEnumerator PlayAudioAfterDelay()
        {
            yield return new WaitForSeconds(delayTime);

            audioSource.Play();
        }

        public void StopAudio()
        {
            StopCoroutine(audioCoroutine);
            audioSource.Stop();
        }
    }
}