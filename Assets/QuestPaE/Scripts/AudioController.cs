using UnityEngine;
using System.Collections;
using Unity.VisualScripting;

public class AudioController : MonoBehaviour
{
    private AudioSource audioSource;
    private Coroutine audioCoroutine;

    public float delayTime = 0f;

    void Start()
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
