using UnityEngine;
using UnityEngine.UI;
using Vuforia;

public class ModelActive : MonoBehaviour
{
    public GameObject model; // The target model
    public AudioSource audioSource;
    AudioClip clip;
    public GameObject Text1;
    void Start()
    {
        GetComponent<ObserverBehaviour>().OnTargetStatusChanged += OnTargetStatusChanged;
    }

    private void OnTargetStatusChanged(ObserverBehaviour observer, TargetStatus status)
    {
        // If catche the model
        if (status.Status == Status.TRACKED)
        {
            model.SetActive(true);//
            audioSource.clip = null;//

        }
        // If did not catche the model
        else if (status.Status == Status.NO_POSE)
        {
            model.SetActive(false);//
            audioSource.clip = null;//
            Text1.SetActive(false);// 
        }
    }
}
